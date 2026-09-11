using System;
using System.Collections.Generic;
using TankWarfare.Core;
using TankWarfare.Network;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TankWarfare.Gameplay
{
    public sealed class GameClient : MonoBehaviour
    {
        private enum ScreenState { Menu, Connecting, Lobby, Playing, Finished, Statistics }
        private const float InputInterval = 0.05f;

        private readonly Dictionary<int, TankView> tanks = new Dictionary<int, TankView>();
        private readonly Dictionary<int, PlayerSnapshot> playerSnapshots = new Dictionary<int, PlayerSnapshot>();
        private GameUi ui;
        private WebSocketTransport transport;
        private WorldView world;
        private LocalStatsDatabase database;
        private PlayerStatistics lifetime;
        private ScreenState screen;
        private NetworkMessage latest;
        private MatchPlayerStatistics localMatch;
        private string pendingOperation;
        private string roomCode = string.Empty;
        private string committedMatchId = string.Empty;
        private int localPlayerId = -1;
        private TankType selectedTank = TankType.Medium;
        private float nextInputTime;
        private int inputSequence;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            ui = FindAnyObjectByType<GameUi>();
            if (ui == null)
            {
                Debug.LogError("На сцене отсутствует GameUi.");
                enabled = false;
                return;
            }

            database = new LocalStatsDatabase();
            lifetime = database.Load();
            ui.nicknameInput.text = SanitizeName(lifetime.nickname);
            ui.serverInput.text = PlayerPrefs.GetString("tw.server-url", DefaultServerUrl());

            GameObject transportObject = new GameObject("WebSocketTransport");
            transportObject.transform.SetParent(transform);
            transport = transportObject.AddComponent<WebSocketTransport>();
            transport.Opened += OnTransportOpened;
            transport.MessageReceived += OnMessage;
            transport.Failed += OnTransportFailed;
            transport.Closed += OnTransportClosed;

            try { world = new WorldView(); }
            catch (Exception exception)
            {
                Debug.LogError(exception.Message);
                enabled = false;
                return;
            }

            BindButtons();
            SelectTank(TankType.Medium);
            ShowScreen(ScreenState.Menu);
        }

        private void BindButtons()
        {
            ui.createButton.onClick.AddListener(() => BeginConnection("create"));
            ui.joinButton.onClick.AddListener(() => BeginConnection("join"));
            ui.statisticsButton.onClick.AddListener(OpenStatistics);
            ui.exitButton.onClick.AddListener(Application.Quit);
            ui.cancelButton.onClick.AddListener(ReturnToMenu);
            ui.leaveLobbyButton.onClick.AddListener(ReturnToMenu);
            ui.resultMenuButton.onClick.AddListener(ReturnToMenu);
            ui.resultStatisticsButton.onClick.AddListener(OpenStatistics);
            ui.statisticsBackButton.onClick.AddListener(() => ShowScreen(ScreenState.Menu));
            ui.heavyButton.onClick.AddListener(() => SelectTank(TankType.Heavy));
            ui.mediumButton.onClick.AddListener(() => SelectTank(TankType.Medium));
            ui.lightButton.onClick.AddListener(() => SelectTank(TankType.Light));
        }

        private void Update()
        {
            if (screen != ScreenState.Playing || !transport.IsOpen || Time.unscaledTime < nextInputTime) return;

            nextInputTime = Time.unscaledTime + InputInterval;
            Keyboard keyboard = Keyboard.current;
            float move = 0f;
            float turn = 0f;
            bool fire = false;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.isPressed) move += 1f;
                if (keyboard.downArrowKey.isPressed) move -= 1f;
                if (keyboard.leftArrowKey.isPressed) turn -= 1f;
                if (keyboard.rightArrowKey.isPressed) turn += 1f;
                fire = keyboard.spaceKey.isPressed;
            }

            transport.Send(JsonUtility.ToJson(new NetworkMessage
            {
                type = "input", sequence = ++inputSequence, move = move, turn = turn, fire = fire
            }));
            if (tanks.TryGetValue(localPlayerId, out TankView localTank)) localTank.SetMovement(move);
        }

        private void SelectTank(TankType type)
        {
            selectedTank = type;
            ui.heavyButton.interactable = type != TankType.Heavy;
            ui.mediumButton.interactable = type != TankType.Medium;
            ui.lightButton.interactable = type != TankType.Light;
            TankSpec spec = TankCatalog.Get(type);
            ui.tankInfoText.text = $"Скорость {spec.MoveSpeed:0.0}   •   Урон {spec.Damage:0}   •   Скорость пули {spec.BulletSpeed:0}";
        }

        private void BeginConnection(string operation)
        {
            string nickname = SanitizeName(ui.nicknameInput.text);
            roomCode = ui.roomInput.text.Trim().ToUpperInvariant();
            string serverUrl = ui.serverInput.text.Trim();
            ui.nicknameInput.text = nickname;
            ui.roomInput.text = roomCode;

            if (operation == "join" && roomCode.Length < 4)
            {
                SetMenuStatus("Введите адрес комнаты.");
                return;
            }
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            {
                SetMenuStatus("Адрес сервера должен начинаться с ws:// или wss://");
                return;
            }
            if (Application.platform == RuntimePlatform.WebGLPlayer && Application.absoluteURL.StartsWith("https:") &&
                serverUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            {
                SetMenuStatus("На itch.io нужен защищённый адрес сервера wss://");
                return;
            }

            lifetime.nickname = nickname;
            database.Save(lifetime);
            PlayerPrefs.SetString("tw.server-url", serverUrl);
            PlayerPrefs.Save();
            pendingOperation = operation;
            ui.connectionStatusText.text = "Связываемся с сервером…";
            ShowScreen(ScreenState.Connecting);
            transport.Connect(serverUrl);
        }

        private void OnTransportOpened()
        {
            ui.connectionStatusText.text = "Соединение установлено";
            transport.Send(JsonUtility.ToJson(new NetworkMessage
            {
                type = pendingOperation, room = roomCode, name = lifetime.nickname, tankType = (int)selectedTank
            }));
        }

        private void OnMessage(string json)
        {
            NetworkMessage message;
            try { message = JsonUtility.FromJson<NetworkMessage>(json); }
            catch (Exception)
            {
                ui.connectionStatusText.text = "Сервер прислал некорректные данные";
                return;
            }

            if (message == null || string.IsNullOrEmpty(message.type)) return;
            switch (message.type)
            {
                case "welcome":
                    localPlayerId = message.playerId;
                    roomCode = message.room;
                    committedMatchId = string.Empty;
                    world.BuildWalls(message.walls);
                    ui.roomAddressText.text = roomCode;
                    ui.gameAddressText.text = $"Адрес игры: {roomCode}";
                    ShowScreen(ScreenState.Lobby);
                    break;
                case "snapshot": ApplySnapshot(message); break;
                case "error":
                    SetMenuStatus(string.IsNullOrEmpty(message.error) ? "Ошибка сервера" : message.error);
                    ShowScreen(ScreenState.Menu);
                    transport.Close();
                    break;
            }
        }

        private void ApplySnapshot(NetworkMessage message)
        {
            latest = message;
            if (message.players != null)
            {
                foreach (PlayerSnapshot snapshot in message.players)
                {
                    playerSnapshots[snapshot.id] = snapshot;
                    TankType type = Enum.IsDefined(typeof(TankType), snapshot.tankType) ? (TankType)snapshot.tankType : TankType.Medium;
                    if (!tanks.TryGetValue(snapshot.id, out TankView view))
                    {
                        view = TankView.Create(snapshot.id, type);
                        tanks[snapshot.id] = view;
                        view.Apply(snapshot, true);
                    }
                    else view.Apply(snapshot);
                }
            }

            world.ApplyWalls(message.walls);
            HashSet<int> firedBy = world.ApplyBullets(message.bullets);
            foreach (int playerId in firedBy)
                if (tanks.TryGetValue(playerId, out TankView tank)) tank.PlayFire();

            RefreshHud();
            if (message.phase == "waiting") { ShowScreen(ScreenState.Lobby); return; }
            if (message.phase == "finished")
            {
                localMatch = FindStatistics(message.statistics, localPlayerId);
                CommitMatchOnce(message);
                RefreshResult();
                ShowScreen(ScreenState.Finished);
            }
            else ShowScreen(ScreenState.Playing);
        }

        private void RefreshHud()
        {
            if (latest == null) return;
            ui.scoreText.text = $"{latest.scoreA}  :  {latest.scoreB}";
            ui.roundText.text = $"Раунд {latest.round}";
            ui.playerANameText.text = PlayerName(0);
            ui.playerBNameText.text = PlayerName(1);
            ui.playerAHealth.fillAmount = Health(0);
            ui.playerBHealth.fillAmount = Health(1);
        }

        private void RefreshResult()
        {
            bool won = latest != null && latest.winner == localPlayerId;
            ui.resultTitleText.text = won ? "ПОБЕДА" : "ПОРАЖЕНИЕ";
            ui.resultTitleText.color = won ? new Color(0.52f, 0.93f, 0.50f) : new Color(1f, 0.43f, 0.36f);
            MatchPlayerStatistics match = localMatch ?? new MatchPlayerStatistics();
            ui.resultBodyText.text = $"Счёт   {latest?.scoreA ?? 0} : {latest?.scoreB ?? 0}\n\n" +
                $"Выпущено пуль                 {match.shots}\n" +
                $"Пройдено                       {match.meters:0.0} м\n" +
                $"Сломано стен                   {match.walls}";
        }

        private void OpenStatistics()
        {
            if (screen == ScreenState.Finished) transport.Close();
            string favorite = lifetime.matches > 0 ? TankCatalog.DisplayName(lifetime.FavoriteTank) : "—";
            ui.statisticsBodyText.text = $"Ник                             {lifetime.nickname}\n\n" +
                $"Побед                           {lifetime.victories}\n" +
                $"Поражений                       {lifetime.defeats}\n" +
                $"Любимый танк                    {favorite}\n" +
                $"Пройдено                        {lifetime.metersDriven:0.0} м\n" +
                $"Выстрелов                       {lifetime.shotsFired}\n" +
                $"Сломано стен                    {lifetime.wallsBroken}";
            ShowScreen(ScreenState.Statistics);
        }

        private void CommitMatchOnce(NetworkMessage message)
        {
            if (string.IsNullOrEmpty(message.matchId) || committedMatchId == message.matchId) return;
            if (PlayerPrefs.GetString("tw.last-match", string.Empty) == message.matchId)
            {
                committedMatchId = message.matchId;
                return;
            }
            lifetime.AddMatch(message.winner == localPlayerId, selectedTank, localMatch);
            database.Save(lifetime);
            committedMatchId = message.matchId;
            PlayerPrefs.SetString("tw.last-match", message.matchId);
            PlayerPrefs.Save();
        }

        private void OnTransportFailed(string error)
        {
            SetMenuStatus($"Не удалось подключиться: {error}");
            if (screen != ScreenState.Finished) ShowScreen(ScreenState.Menu);
        }

        private void OnTransportClosed()
        {
            if (screen is ScreenState.Connecting or ScreenState.Lobby or ScreenState.Playing)
            {
                SetMenuStatus("Соединение с сервером закрыто");
                ShowScreen(ScreenState.Menu);
            }
        }

        private void ReturnToMenu()
        {
            transport.Close();
            world.ClearDynamic();
            foreach (TankView tank in tanks.Values) Destroy(tank.gameObject);
            tanks.Clear();
            playerSnapshots.Clear();
            latest = null;
            localMatch = null;
            localPlayerId = -1;
            pendingOperation = string.Empty;
            SetMenuStatus(string.Empty);
            ShowScreen(ScreenState.Menu);
        }

        private void ShowScreen(ScreenState target)
        {
            screen = target;
            ui.menuPanel.SetActive(target == ScreenState.Menu);
            ui.connectionPanel.SetActive(target == ScreenState.Connecting);
            ui.lobbyPanel.SetActive(target == ScreenState.Lobby);
            ui.hudPanel.SetActive(target is ScreenState.Playing or ScreenState.Finished);
            ui.resultPanel.SetActive(target == ScreenState.Finished);
            ui.statisticsPanel.SetActive(target == ScreenState.Statistics);
        }

        private void SetMenuStatus(string message) => ui.menuStatusText.text = message;
        private float Health(int id) => playerSnapshots.TryGetValue(id, out PlayerSnapshot value)
            ? Mathf.Clamp01(value.health / Mathf.Max(1f, value.maxHealth)) : 0f;
        private string PlayerName(int id) => playerSnapshots.TryGetValue(id, out PlayerSnapshot value) ? value.name : "—";

        private static MatchPlayerStatistics FindStatistics(MatchPlayerStatistics[] values, int id)
        {
            if (values != null)
                foreach (MatchPlayerStatistics value in values)
                    if (value.playerId == id) return value;
            return new MatchPlayerStatistics { playerId = id };
        }

        private static string SanitizeName(string value)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length > 18) value = value.Substring(0, 18);
            return string.IsNullOrEmpty(value) ? "Игрок" : value;
        }

        private static string DefaultServerUrl()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return "wss://YOUR-SERVER.example.com";
#else
            return "ws://localhost:8080";
#endif
        }
    }
}
