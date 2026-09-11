using TankWarfare.Core;
using TankWarfare.Network;
using UnityEngine;

namespace TankWarfare.Gameplay
{
    /// <summary>
    /// Visual-only tank. Replace child meshes and assign an Animator Controller later;
    /// network and gameplay code do not depend on a particular model hierarchy.
    /// </summary>
    public sealed class TankView : MonoBehaviour
    {
        public static readonly int MoveSpeedParameter = Animator.StringToHash("MoveSpeed");
        public static readonly int FireTrigger = Animator.StringToHash("Fire");
        public static readonly int HitTrigger = Animator.StringToHash("Hit");
        public static readonly int DeathTrigger = Animator.StringToHash("Death");
        public static readonly int RespawnTrigger = Animator.StringToHash("Respawn");

        private Animator animator;
        private Transform turret;
        private Transform barrel;
        private Renderer[] renderers;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private float previousHealth;
        private bool previousAlive;
        private float maxHealth = 100f;

        public int PlayerId { get; private set; }
        public float HealthRatio => Mathf.Clamp01(previousHealth / Mathf.Max(1f, maxHealth));
        public Vector3 MuzzlePosition
        {
            get
            {
                if (barrel != null)
                    return barrel.position + barrel.forward * (barrel.lossyScale.z * 0.5f + 0.12f);
                if (turret != null)
                    return turret.position + turret.forward * 1.65f;
                return transform.position + transform.forward * 1.65f + Vector3.up * 0.65f;
            }
        }

        public float MuzzleYaw => turret != null ? turret.eulerAngles.y : transform.eulerAngles.y;

        public static TankView Create(int playerId, TankType type)
        {
            GameObject prefab = Resources.Load<GameObject>($"Prefabs/Tank{type}");
            if (prefab != null)
            {
                GameObject instance = Instantiate(prefab);
                instance.name = $"Tank_{playerId}_{type}";
                TankView loadedView = instance.GetComponent<TankView>() ?? instance.AddComponent<TankView>();
                loadedView.Initialize(playerId, type);
                return loadedView;
            }

            var root = new GameObject($"Tank_{playerId}_{type}");
            TankView view = root.AddComponent<TankView>();

            Color color = TankCatalog.Color(type, playerId);
            CreatePart("Hull", root.transform, new Vector3(0f, 0.42f, 0f), new Vector3(1.25f, 0.42f, 1.65f), color);
            CreatePart("LeftTrack", root.transform, new Vector3(-0.76f, 0.32f, 0f), new Vector3(0.28f, 0.38f, 1.9f), color * 0.42f);
            CreatePart("RightTrack", root.transform, new Vector3(0.76f, 0.32f, 0f), new Vector3(0.28f, 0.38f, 1.9f), color * 0.42f);
            view.turret = CreatePart("Turret", root.transform, new Vector3(0f, 0.82f, 0.05f),
                new Vector3(0.85f, 0.34f, 0.9f), color * 1.12f).transform;
            CreatePart("Barrel", view.turret, new Vector3(0f, 0.05f, 0.92f), new Vector3(0.18f, 0.16f, 1.25f), color * 0.72f);

            view.animator = root.AddComponent<Animator>();
            view.Initialize(playerId, type);
            return view;
        }

        public void Initialize(int playerId, TankType type)
        {
            PlayerId = playerId;
            animator = GetComponent<Animator>();
            turret = transform.Find("Turret");
            barrel = turret != null ? turret.Find("Barrel") : null;
            renderers = GetComponentsInChildren<Renderer>();
            targetPosition = transform.position;
            targetRotation = transform.rotation;

            Color color = TankCatalog.Color(type, playerId);
            foreach (Renderer item in renderers)
            {
                float shade = item.name.Contains("Track") ? 0.42f : item.name == "Barrel" ? 0.72f : 1f;
                item.material = WorldView.CreateMaterial(color * shade);
            }
        }

        public void Apply(PlayerSnapshot snapshot, bool immediate = false)
        {
            targetPosition = new Vector3(snapshot.x, 0f, snapshot.z);
            targetRotation = Quaternion.Euler(0f, snapshot.yaw, 0f);
            maxHealth = snapshot.maxHealth;

            if (previousAlive && !snapshot.alive)
                Trigger(DeathTrigger);
            else if (!previousAlive && snapshot.alive)
                Trigger(RespawnTrigger);

            if (previousHealth > 0f && snapshot.health < previousHealth && snapshot.alive)
                Trigger(HitTrigger);

            previousHealth = snapshot.health;
            previousAlive = snapshot.alive;
            SetVisible(snapshot.alive);

            if (immediate)
            {
                transform.SetPositionAndRotation(targetPosition, targetRotation);
            }
        }

        public void SetMovement(float normalizedSpeed)
        {
            if (HasController())
                animator.SetFloat(MoveSpeedParameter, Mathf.Abs(normalizedSpeed), 0.08f, Time.deltaTime);
        }

        public void PlayFire() => Trigger(FireTrigger);

        private void Update()
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, 15f * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 15f * Time.deltaTime);
        }

        private void Trigger(int hash)
        {
            if (HasController())
                animator.SetTrigger(hash);
        }

        private bool HasController() => animator != null && animator.runtimeAnimatorController != null;

        private void SetVisible(bool visible)
        {
            if (renderers == null) return;
            foreach (Renderer item in renderers)
                item.enabled = visible;
        }

        private static GameObject CreatePart(string partName, Transform parent, Vector3 localPosition,
            Vector3 localScale, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            part.GetComponent<Renderer>().material = WorldView.CreateMaterial(color);
            return part;
        }
    }
}
