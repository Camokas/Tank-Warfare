using System.Collections.Generic;
using InvalidOperationException = System.InvalidOperationException;
using TankWarfare.Network;
using UnityEngine;

namespace TankWarfare.Gameplay
{
    public sealed class WorldView
    {
        private readonly Transform root;
        private readonly Dictionary<int, GameObject> walls = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, BulletView> bullets = new Dictionary<int, BulletView>();
        private readonly Queue<BulletView> predictedBullets = new Queue<BulletView>();
        private readonly HashSet<int> liveBulletIds = new HashSet<int>();
        private readonly Material bulletMaterial;

        public WorldView()
        {
            GameObject dynamicRoot = GameObject.Find("DynamicLevel");
            if (dynamicRoot == null)
                throw new InvalidOperationException("На сцене отсутствует объект DynamicLevel.");
            root = dynamicRoot.transform;
            bulletMaterial = CreateMaterial(new Color(1f, 0.72f, 0.16f), true);
        }

        public void BuildWalls(WallSnapshot[] snapshots)
        {
            foreach (GameObject wall in walls.Values)
                Object.Destroy(wall);
            walls.Clear();

            if (snapshots == null) return;
            foreach (WallSnapshot snapshot in snapshots)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Wall_{snapshot.id}";
                cube.transform.SetParent(root);
                cube.transform.position = new Vector3(snapshot.x, 0.65f, snapshot.z);
                cube.transform.localScale = new Vector3(1.35f, 1.3f, 1.35f);
                Color color = snapshot.destructible
                    ? new Color(0.58f, 0.43f, 0.28f)
                    : new Color(0.29f, 0.33f, 0.36f);
                cube.GetComponent<Renderer>().material = CreateMaterial(color);
                Collider collider = cube.GetComponent<Collider>();
                if (collider != null) Object.Destroy(collider);
                walls[snapshot.id] = cube;
            }
        }

        public void ApplyWalls(WallSnapshot[] snapshots)
        {
            if (snapshots == null) return;
            foreach (WallSnapshot snapshot in snapshots)
            {
                if (!walls.TryGetValue(snapshot.id, out GameObject wall))
                    continue;

                if (snapshot.health <= 0f)
                {
                    Object.Destroy(wall);
                    walls.Remove(snapshot.id);
                }
                else if (snapshot.destructible)
                {
                    float damage = Mathf.Clamp01(1f - snapshot.health / 100f);
                    wall.transform.localScale = new Vector3(1.35f, Mathf.Lerp(1.3f, 0.45f, damage), 1.35f);
                }
            }
        }

        public void PredictLocalShot(Vector3 muzzlePosition, float yaw, float speed)
        {
            BulletView bullet = CreateBullet("PredictedBullet");
            bullet.Initialize(muzzlePosition, yaw, speed, true);
            predictedBullets.Enqueue(bullet);
        }

        public HashSet<int> ApplyBullets(BulletSnapshot[] snapshots, int localPlayerId)
        {
            liveBulletIds.Clear();
            var newlyCreatedOwners = new HashSet<int>();
            DiscardExpiredPredictions();

            if (snapshots != null)
            {
                foreach (BulletSnapshot snapshot in snapshots)
                {
                    liveBulletIds.Add(snapshot.id);
                    if (!bullets.TryGetValue(snapshot.id, out BulletView bullet))
                    {
                        bullet = snapshot.owner == localPlayerId ? TakePrediction() : null;
                        if (bullet == null)
                        {
                            bullet = CreateBullet($"Bullet_{snapshot.id}");
                            bullet.Initialize(new Vector3(snapshot.x, 0.82f, snapshot.z),
                                snapshot.yaw, snapshot.speed, false);
                        }
                        else bullet.name = $"Bullet_{snapshot.id}";

                        bullets[snapshot.id] = bullet;
                        newlyCreatedOwners.Add(snapshot.owner);
                    }

                    bullet.Confirm(snapshot);
                }
            }

            var expired = new List<int>();
            foreach (KeyValuePair<int, BulletView> pair in bullets)
                if (!liveBulletIds.Contains(pair.Key)) expired.Add(pair.Key);
            foreach (int id in expired)
            {
                if (bullets[id] != null) Object.Destroy(bullets[id].gameObject);
                bullets.Remove(id);
            }

            return newlyCreatedOwners;
        }

        public void ClearDynamic()
        {
            foreach (BulletView bullet in bullets.Values)
                if (bullet != null) Object.Destroy(bullet.gameObject);
            bullets.Clear();

            while (predictedBullets.Count > 0)
            {
                BulletView bullet = predictedBullets.Dequeue();
                if (bullet != null) Object.Destroy(bullet.gameObject);
            }
        }

        private BulletView CreateBullet(string objectName)
        {
            GameObject bulletObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulletObject.name = objectName;
            bulletObject.transform.SetParent(root);
            bulletObject.transform.localScale = new Vector3(0.24f, 0.24f, 0.42f);
            bulletObject.GetComponent<Renderer>().sharedMaterial = bulletMaterial;

            TrailRenderer trail = bulletObject.AddComponent<TrailRenderer>();
            trail.time = 0.12f;
            trail.minVertexDistance = 0.03f;
            trail.startWidth = 0.12f;
            trail.endWidth = 0f;
            trail.sharedMaterial = bulletMaterial;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;

            Collider collider = bulletObject.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            return bulletObject.AddComponent<BulletView>();
        }

        private BulletView TakePrediction()
        {
            while (predictedBullets.Count > 0)
            {
                BulletView bullet = predictedBullets.Dequeue();
                if (bullet != null && !bullet.IsExpiredPrediction) return bullet;
                if (bullet != null) Object.Destroy(bullet.gameObject);
            }
            return null;
        }

        private void DiscardExpiredPredictions()
        {
            while (predictedBullets.Count > 0)
            {
                BulletView bullet = predictedBullets.Peek();
                if (bullet != null && !bullet.IsExpiredPrediction) break;
                predictedBullets.Dequeue();
                if (bullet != null) Object.Destroy(bullet.gameObject);
            }
        }

        public static Material CreateMaterial(Color color, bool emission = false)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (emission && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.8f);
            }
            return material;
        }

    }
}
