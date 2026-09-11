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
        private readonly Dictionary<int, GameObject> bullets = new Dictionary<int, GameObject>();
        private readonly HashSet<int> liveBulletIds = new HashSet<int>();

        public WorldView()
        {
            GameObject dynamicRoot = GameObject.Find("DynamicLevel");
            if (dynamicRoot == null)
                throw new InvalidOperationException("На сцене отсутствует объект DynamicLevel.");
            root = dynamicRoot.transform;
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

        public HashSet<int> ApplyBullets(BulletSnapshot[] snapshots)
        {
            liveBulletIds.Clear();
            var newlyCreatedOwners = new HashSet<int>();

            if (snapshots != null)
            {
                foreach (BulletSnapshot snapshot in snapshots)
                {
                    liveBulletIds.Add(snapshot.id);
                    if (!bullets.TryGetValue(snapshot.id, out GameObject bullet))
                    {
                        bullet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        bullet.name = $"Bullet_{snapshot.id}";
                        bullet.transform.SetParent(root);
                        bullet.transform.localScale = Vector3.one * 0.28f;
                        bullet.GetComponent<Renderer>().material = CreateMaterial(new Color(1f, 0.72f, 0.16f), true);
                        Collider collider = bullet.GetComponent<Collider>();
                        if (collider != null) Object.Destroy(collider);
                        bullets[snapshot.id] = bullet;
                        newlyCreatedOwners.Add(snapshot.owner);
                    }

                    Vector3 target = new Vector3(snapshot.x, 0.58f, snapshot.z);
                    bullet.transform.position = Vector3.Lerp(bullet.transform.position, target, 0.72f);
                    bullet.transform.rotation = Quaternion.Euler(0f, snapshot.yaw, 0f);
                }
            }

            var expired = new List<int>();
            foreach (KeyValuePair<int, GameObject> pair in bullets)
                if (!liveBulletIds.Contains(pair.Key)) expired.Add(pair.Key);
            foreach (int id in expired)
            {
                Object.Destroy(bullets[id]);
                bullets.Remove(id);
            }

            return newlyCreatedOwners;
        }

        public void ClearDynamic()
        {
            foreach (GameObject bullet in bullets.Values) Object.Destroy(bullet);
            bullets.Clear();
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
