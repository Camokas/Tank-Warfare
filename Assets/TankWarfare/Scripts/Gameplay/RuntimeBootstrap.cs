using UnityEngine;

namespace TankWarfare.Gameplay
{
    public static class RuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Object.FindAnyObjectByType<GameClient>() != null)
                return;

            var root = new GameObject("TankWarfare");
            root.AddComponent<GameClient>();
        }
    }
}
