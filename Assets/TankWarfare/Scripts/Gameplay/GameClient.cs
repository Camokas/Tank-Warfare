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

        private const float ReferenceWidth = 1280f;
        private const float ReferenceHeight = 720f;
        private const float InputInterval = 0.05f;

        private readonly Dictionary<int, TankView> tanks = new Dictionary<int, TankView>();
        private readonly Dictionary<int, PlayerSnapshot> playerSnapshots = new Dictionary<int, PlayerSnapshot>();

        private WebSocketTransport transport;
        private WorldView world;
        private LocalStatsDatabase database;
        private PlayerStatistics lifetime;
        private ScreenState screen = ScreenState.Menu;
        private NetworkMessage latest;
        private MatchPlayerStatistics localMatch;
        private string pendingOperation;
        private string nickname = "Игрок";
        private string roomCode = string.Empty;
        private string status = string.Empty;
        private string serverUrl = "ws://localhost:8080";
        private string committedMatchId = string.Empty;
        private int localPlayerId = -1;
        private TankType selectedTank = TankType.Medium;
        private float nextInputTime;
        private int inputSequence;

        private GUIStyle titleStyle;
        private GUIStyle headingStyle;
        private GUIStyle labelStyle;
        private GUIStyle centeredStyle;
        private GUIStyle smallStyle;
        private GUIStyle resultStyle;
        private GUIStyle healthStyle;
        private Texture2D panelTexture;
        private Texture2D darkTexture;
        private Texture2D greenTexture;
        private Texture2D redTexture;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            SetupScene();
            database = new LocalStatsDatabase();
            lifetime = database.Load();
            nickname = SanitizeName(lifetime.nickname);
            serverUrl = PlayerPrefs.GetString("tw.server-url", DefaultServerUrl());

            GameObject transportObject = new GameObject("TankWarfareWebSocket");
            transportObject.transform.SetParent(transform);
            transport = transportObject.AddComponent<WebSocketTransport>();
            transport.Opened += OnTransportOpened;
            transport.MessageReceived += OnMessage;
            transport.Failed += OnTransportFailed;
            transport.Closed += OnTransportClosed;

            world = new WorldView();
        }

        private void Update()
        {
            if (screen != ScreenState.Playing || !transport.IsOpen || Time.unscaledTime < nextInputTime)
                return;

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

            var input = new NetworkMessage
            {
                type = "input",
                sequence = ++inputSequence,
                move = move,
                turn = turn,
                fire = fire
            };
            transport.Send(JsonUtility.ToJson(input));
            if (tanks.TryGetValue(localPlayerId, out TankView localTank))
                localTank.SetMovement(move);
        }

        private void OnGUI()
        {
            EnsureStyles();
            Matrix4x4 previous = GUI.matrix;
            float scale = Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight);
            float offsetX = (Screen.width - ReferenceWidth * scale) * 0.5f;
            float offsetY = (Screen.height - ReferenceHeight * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity, Vector3.one * scale);

            switch (screen)
            {
                case ScreenState.Menu: DrawMenu(); break;
                case ScreenState.Connecting: DrawConnecting(); break;
                case ScreenState.Lobby: DrawLobby(); break;
                case ScreenState.Playing: DrawHud(); break;
                case ScreenState.Finished: DrawResult(); break;
                case ScreenState.Statistics: DrawStatistics(); break;
            }

            GUI.matrix = previous;
        }

        private void DrawMenu()
        {
            GUI.Box(new Rect(390f, 65f, 500f, 590f), GUIContent.none, PanelStyle());
            GUI.Label(new Rect(410f, 88f, 460f, 58f), "TANK WARFARE", titleStyle);
            GUI.Label(new Rect(430f, 153f, 420f, 25f), "Ник", smallStyle);
            nickname = GUI.TextField(new Rect(430f, 178f, 420f, 38f), nickname, 18);

            GUI.Label(new Rect(430f, 230f, 420f, 25f), "Класс танка", smallStyle);
            DrawTankSelector(new Rect(430f, 258f, 420f, 42f));
            TankSpec spec = TankCatalog.Get(selectedTank);
            GUI.Label(new Rect(430f, 306f, 420f, 52f),
                $"Скорость {spec.MoveSpeed:0.0}  •  Урон {spec.Damage:0}  •  Пуля {spec.BulletSpeed:0}", centeredStyle);

            if (GUI.Button(new Rect(430f, 365f, 420f, 48f), "СОЗДАТЬ ИГРУ"))
                BeginConnection("create");

            GUI.Label(new Rect(430f, 428f, 145f, 25f), "Адрес комнаты", smallStyle);
            roomCode = GUI.TextField(new Rect(430f, 454f, 250f, 40f), roomCode.ToUpperInvariant(), 8);
            if (GUI.Button(new Rect(690f, 454f, 160f, 40f), "ПОДКЛЮЧИТЬСЯ"))
                BeginConnection("join");

            if (GUI.Button(new Rect(430f, 512f, 205f, 42f), "СТАТИСТИКА"))
                screen = ScreenState.Statistics;
            if (GUI.Button(new Rect(645f, 512f, 205f, 42f), "ВЫХОД"))
                Application.Quit();

            GUI.Label(new Rect(430f, 567f, 420f, 22f), "Сервер (для itch.io укажите wss://)", smallStyle);
            serverUrl = GUI.TextField(new Rect(430f, 591f, 420f, 34f), serverUrl, 160);
            if (!string.IsNullOrEmpty(status))
                GUI.Label(new Rect(410f, 630f, 460f, 24f), status, centeredStyle);
        }

        private void DrawConnecting()
        {
            GUI.Box(new Rect(430f, 250f, 420f, 190f), GUIContent.none, PanelStyle());
            GUI.Label(new Rect(450f, 275f, 380f, 45f), "ПОДКЛЮЧЕНИЕ", headingStyle);
            GUI.Label(new Rect(455f, 328f, 370f, 32f), status, centeredStyle);
            if (GUI.Button(new Rect(510f, 375f, 260f, 42f), "ОТМЕНА"))
                ReturnToMenu();
        }

        private void DrawLobby()
        {
            GUI.Box(new Rect(420f, 220f, 440f, 270f), GUIContent.none, PanelStyle());
            GUI.Label(new Rect(440f, 245f, 400f, 44f), "ИГРА СОЗДАНА", headingStyle);
            GUI.Label(new Rect(440f, 300f, 400f, 26f), "Передайте этот адрес второму игроку:", centeredStyle);
            GUI.Label(new Rect(440f, 332f, 400f, 65f), roomCode, titleStyle);
            GUI.Label(new Rect(440f, 402f, 400f, 26f), "Ожидание подключения…", centeredStyle);
            if (GUI.Button(new Rect(510f, 443f, 260f, 38f), "ПОКИНУТЬ ИГРУ"))
                ReturnToMenu();
        }

        private void DrawHud()
        {
            string leftName = PlayerName(0);
            string rightName = PlayerName(1);
            GUI.Box(new Rect(360f, 16f, 560f, 74f), GUIContent.none, DarkStyle());
            GUI.Label(new Rect(378f, 26f, 182f, 28f), leftName, labelStyle);
            GUI.Label(new Rect(720f, 26f, 182f, 28f), rightName, labelStyle);
            GUI.Label(new Rect(560f, 21f, 160f, 48f), $"{latest?.scoreA ?? 0}  :  {latest?.scoreB ?? 0}", headingStyle);
            GUI.Label(new Rect(560f, 62f, 160f, 20f), $"Раунд {latest?.round ?? 1}", centeredStyle);

            DrawHealthBar(new Rect(378f, 58f, 180f, 15f), Health(0));
            DrawHealthBar(new Rect(722f, 58f, 180f, 15f), Health(1));

            GUI.Box(new Rect(400f, 668f, 480f, 36f), GUIContent.none, DarkStyle());
            GUI.Label(new Rect(410f, 674f, 460f, 24f),
                $"Адрес игры: {roomCode}   •   ↑↓ движение   ←→ поворот   ПРОБЕЛ огонь", centeredStyle);
        }

        private void DrawResult()
        {
            DrawHud();
            GUI.Box(new Rect(370f, 155f, 540f, 420f), GUIContent.none, PanelStyle());
            bool won = latest != null && latest.winner == localPlayerId;
            GUI.Label(new Rect(395f, 182f, 490f, 66f), won ? "ПОБЕДА" : "ПОРАЖЕНИЕ",
                won ? ResultStyle(new Color(0.52f, 0.93f, 0.50f)) : ResultStyle(new Color(1f, 0.43f, 0.36f)));
            GUI.Label(new Rect(410f, 255f, 460f, 44f),
                $"Счёт  {latest?.scoreA ?? 0} : {latest?.scoreB ?? 0}", headingStyle);

            MatchPlayerStatistics match = localMatch ?? new MatchPlayerStatistics();
            DrawStatRow(310f, "Выпущено пуль", match.shots.ToString());
            DrawStatRow(350f, "Пройдено", $"{match.meters:0.0} м");
            DrawStatRow(390f, "Сломано стен", match.walls.ToString());

            if (GUI.Button(new Rect(425f, 475f, 205f, 48f), "В МЕНЮ"))
                ReturnToMenu();
            if (GUI.Button(new Rect(650f, 475f, 205f, 48f), "СТАТИСТИКА"))
            {
                transport.Close();
                screen = ScreenState.Statistics;
            }
        }

        private void DrawStatistics()
        {
            GUI.Box(new Rect(380f, 95f, 520f, 530f), GUIContent.none, PanelStyle());
            GUI.Label(new Rect(405f, 122f, 470f, 50f), "СТАТИСТИКА", headingStyle);
            DrawStatRow(190f, "Ник", lifetime.nickname);
            DrawStatRow(235f, "Побед", lifetime.victories.ToString());
            DrawStatRow(280f, "Поражений", lifetime.defeats.ToString());
            DrawStatRow(325f, "Любимый танк", lifetime.matches > 0 ? TankCatalog.DisplayName(lifetime.FavoriteTank) : "—");
            DrawStatRow(370f, "Пройдено", $"{lifetime.metersDriven:0.0} м");
            DrawStatRow(415f, "Выстрелов", lifetime.shotsFired.ToString());
            DrawStatRow(460f, "Сломано стен", lifetime.wallsBroken.ToString());
            GUI.Label(new Rect(415f, 510f, 450f, 42f),
                "Запись зашифрована и проверяется при загрузке.\nВ WebGL она хранится в IndexedDB браузера.", centeredStyle);
            if (GUI.Button(new Rect(500f, 565f, 280f, 44f), "НАЗАД"))
                screen = ScreenState.Menu;
        }

        private void BeginConnection(string operation)
        {
            nickname = SanitizeName(nickname);
            roomCode = roomCode.Trim().ToUpperInvariant();
            serverUrl = serverUrl.Trim();

            if (operation == "join" && roomCode.Length < 4)
            {
                status = "Введите адрес комнаты";
                return;
            }

            if (Application.platform == RuntimePlatform.WebGLPlayer && Application.absoluteURL.StartsWith("https:") &&
                serverUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            {
                status = "На HTTPS-странице нужен защищённый адрес wss://";
                return;
            }

            lifetime.nickname = nickname;
            database.Save(lifetime);
            PlayerPrefs.SetString("tw.server-url", serverUrl);
            PlayerPrefs.Save();
            pendingOperation = operation;
            status = "Связываемся с сервером…";
            screen = ScreenState.Connecting;
            transport.Connect(serverUrl);
        }

        private void OnTransportOpened()
        {
            status = "Соединение установлено";
            var request = new NetworkMessage
            {
                type = pendingOperation,
                room = roomCode,
                name = nickname,
                tankType = (int)selectedTank
            };
            transport.Send(JsonUtility.ToJson(request));
        }

        private void OnMessage(string json)
        {
            NetworkMessage message;
            try { message = JsonUtility.FromJson<NetworkMessage>(json); }
            catch (Exception) { status = "Сервер прислал некорректные данные"; return; }

            if (message == null || string.IsNullOrEmpty(message.type)) return;
            switch (message.type)
            {
                case "welcome":
                    localPlayerId = message.playerId;
                    roomCode = message.room;
                    committedMatchId = string.Empty;
                    world.BuildWalls(message.walls);
                    screen = ScreenState.Lobby;
                    status = string.Empty;
                    break;
                case "snapshot":
                    ApplySnapshot(message);
                    break;
                case "error":
                    status = string.IsNullOrEmpty(message.error) ? "Ошибка сервера" : message.error;
                    screen = ScreenState.Menu;
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
                    TankType type = Enum.IsDefined(typeof(TankType), snapshot.tankType)
                        ? (TankType)snapshot.tankType
                        : TankType.Medium;
                    if (!tanks.TryGetValue(snapshot.id, out TankView view))
                    {
                        view = TankView.Create(snapshot.id, type);
                        tanks[snapshot.id] = view;
                        view.Apply(snapshot, true);
                    }
                    else
                    {
                        view.Apply(snapshot);
                    }
                }
            }

            world.ApplyWalls(message.walls);
            HashSet<int> firedBy = world.ApplyBullets(message.bullets);
            foreach (int playerId in firedBy)
                if (tanks.TryGetValue(playerId, out TankView tank)) tank.PlayFire();

            if (message.phase == "waiting")
            {
                screen = ScreenState.Lobby;
                return;
            }

            if (message.phase == "finished")
            {
                localMatch = FindStatistics(message.statistics, localPlayerId);
                CommitMatchOnce(message);
                screen = ScreenState.Finished;
            }
            else
            {
                screen = ScreenState.Playing;
            }
        }

        private void CommitMatchOnce(NetworkMessage message)
        {
            if (string.IsNullOrEmpty(message.matchId) || committedMatchId == message.matchId)
                return;

            string persisted = PlayerPrefs.GetString("tw.last-match", string.Empty);
            if (persisted == message.matchId)
            {
                committedMatchId = message.matchId;
                return;
            }

            lifetime.nickname = nickname;
            lifetime.AddMatch(message.winner == localPlayerId, selectedTank, localMatch);
            database.Save(lifetime);
            committedMatchId = message.matchId;
            PlayerPrefs.SetString("tw.last-match", message.matchId);
            PlayerPrefs.Save();
        }

        private void OnTransportFailed(string error)
        {
            status = $"Не удалось подключиться: {error}";
            if (screen != ScreenState.Finished) screen = ScreenState.Menu;
        }

        private void OnTransportClosed()
        {
            if (screen is ScreenState.Connecting or ScreenState.Lobby or ScreenState.Playing)
            {
                status = "Соединение с сервером закрыто";
                screen = ScreenState.Menu;
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
            status = string.Empty;
            screen = ScreenState.Menu;
        }

        private void DrawTankSelector(Rect rect)
        {
            float width = rect.width / 3f;
            if (GUI.Toggle(new Rect(rect.x, rect.y, width - 4f, rect.height), selectedTank == TankType.Heavy, "ТЯЖЁЛЫЙ", GUI.skin.button))
                selectedTank = TankType.Heavy;
            if (GUI.Toggle(new Rect(rect.x + width, rect.y, width - 4f, rect.height), selectedTank == TankType.Medium, "СРЕДНИЙ", GUI.skin.button))
                selectedTank = TankType.Medium;
            if (GUI.Toggle(new Rect(rect.x + width * 2f, rect.y, width - 4f, rect.height), selectedTank == TankType.Light, "ЛЁГКИЙ", GUI.skin.button))
                selectedTank = TankType.Light;
        }

        private void DrawStatRow(float y, string name, string value)
        {
            GUI.Label(new Rect(425f, y, 280f, 34f), name, labelStyle);
            GUI.Label(new Rect(690f, y, 165f, 34f), value, labelStyle);
        }

        private void DrawHealthBar(Rect rect, float ratio)
        {
            GUI.DrawTexture(rect, darkTexture);
            Rect fill = new Rect(rect.x + 2f, rect.y + 2f, (rect.width - 4f) * ratio, rect.height - 4f);
            GUI.DrawTexture(fill, ratio > 0.3f ? greenTexture : redTexture);
        }

        private float Health(int playerId)
        {
            return playerSnapshots.TryGetValue(playerId, out PlayerSnapshot snapshot)
                ? Mathf.Clamp01(snapshot.health / Mathf.Max(1f, snapshot.maxHealth))
                : 0f;
        }

        private string PlayerName(int playerId)
        {
            return playerSnapshots.TryGetValue(playerId, out PlayerSnapshot snapshot) ? snapshot.name : "—";
        }

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

        private void SetupScene()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = cameraObject.AddComponent<Camera>();
            }
            camera.orthographic = true;
            camera.orthographicSize = 13.1f;
            camera.transform.position = new Vector3(0f, 20f, -17f);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, 0f) - camera.transform.position);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.072f, 0.062f);

            Light light = FindAnyObjectByType<Light>();
            if (light == null)
            {
                GameObject lightObject = new GameObject("Sun");
                light = lightObject.AddComponent<Light>();
            }
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            RenderSettings.ambientLight = new Color(0.48f, 0.52f, 0.48f);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            panelTexture = SolidTexture(new Color(0.055f, 0.075f, 0.065f, 0.95f));
            darkTexture = SolidTexture(new Color(0.018f, 0.026f, 0.022f, 0.90f));
            greenTexture = SolidTexture(new Color(0.30f, 0.82f, 0.34f));
            redTexture = SolidTexture(new Color(0.91f, 0.22f, 0.17f));

            titleStyle = NewStyle(38, FontStyle.Bold, TextAnchor.MiddleCenter);
            headingStyle = NewStyle(27, FontStyle.Bold, TextAnchor.MiddleCenter);
            labelStyle = NewStyle(20, FontStyle.Normal, TextAnchor.MiddleLeft);
            centeredStyle = NewStyle(17, FontStyle.Normal, TextAnchor.MiddleCenter);
            smallStyle = NewStyle(15, FontStyle.Normal, TextAnchor.MiddleLeft);
            healthStyle = NewStyle(14, FontStyle.Bold, TextAnchor.MiddleCenter);
            GUI.skin.label.normal.textColor = new Color(0.91f, 0.94f, 0.90f);
            GUI.skin.button.fontSize = 16;
            GUI.skin.button.fontStyle = FontStyle.Bold;
            GUI.skin.textField.fontSize = 18;
            GUI.skin.textField.alignment = TextAnchor.MiddleLeft;
        }

        private GUIStyle PanelStyle()
        {
            var style = new GUIStyle(GUI.skin.box);
            style.normal.background = panelTexture;
            return style;
        }

        private GUIStyle DarkStyle()
        {
            var style = new GUIStyle(GUI.skin.box);
            style.normal.background = darkTexture;
            return style;
        }

        private GUIStyle ResultStyle(Color color)
        {
            if (resultStyle == null) resultStyle = NewStyle(48, FontStyle.Bold, TextAnchor.MiddleCenter);
            resultStyle.normal.textColor = color;
            return resultStyle;
        }

        private static GUIStyle NewStyle(int size, FontStyle fontStyle, TextAnchor alignment)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fontStyle,
                alignment = alignment,
                wordWrap = true,
                normal = { textColor = new Color(0.91f, 0.94f, 0.90f) }
            };
        }

        private static Texture2D SolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
