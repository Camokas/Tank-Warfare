using System;
using UnityEngine;

namespace TankWarfare.Core
{
    public enum TankType
    {
        Heavy = 0,
        Medium = 1,
        Light = 2
    }

    [Serializable]
    public readonly struct TankSpec
    {
        public readonly float MoveSpeed;
        public readonly float TurnSpeed;
        public readonly float Damage;
        public readonly float BulletSpeed;
        public readonly float ReloadSeconds;
        public readonly float MaxHealth;

        public TankSpec(float moveSpeed, float turnSpeed, float damage, float bulletSpeed,
            float reloadSeconds, float maxHealth)
        {
            MoveSpeed = moveSpeed;
            TurnSpeed = turnSpeed;
            Damage = damage;
            BulletSpeed = bulletSpeed;
            ReloadSeconds = reloadSeconds;
            MaxHealth = maxHealth;
        }
    }

    public static class TankCatalog
    {
        public static TankSpec Get(TankType type)
        {
            return type switch
            {
                TankType.Heavy => new TankSpec(3.6f, 95f, 55f, 9f, 0.95f, 150f),
                TankType.Light => new TankSpec(6.3f, 145f, 25f, 16f, 0.45f, 80f),
                _ => new TankSpec(4.8f, 120f, 40f, 12f, 0.70f, 110f)
            };
        }

        public static string DisplayName(TankType type)
        {
            return type switch
            {
                TankType.Heavy => "Тяжёлый",
                TankType.Light => "Лёгкий",
                _ => "Средний"
            };
        }

        public static Color Color(TankType type, int playerId)
        {
            Color baseColor = type switch
            {
                TankType.Heavy => new Color(0.25f, 0.42f, 0.22f),
                TankType.Light => new Color(0.25f, 0.55f, 0.42f),
                _ => new Color(0.31f, 0.48f, 0.30f)
            };

            return playerId == 0
                ? baseColor
                : UnityEngine.Color.Lerp(baseColor, new UnityEngine.Color(0.65f, 0.22f, 0.16f), 0.72f);
        }
    }
}
