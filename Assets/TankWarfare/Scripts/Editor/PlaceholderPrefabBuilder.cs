#if UNITY_EDITOR
using System.IO;
using TankWarfare.Core;
using TankWarfare.Gameplay;
using UnityEditor;
using UnityEngine;

namespace TankWarfare.Editor
{
    [InitializeOnLoad]
    public static class PlaceholderPrefabBuilder
    {
        private const string Folder = "Assets/TankWarfare/Resources/Prefabs";

        static PlaceholderPrefabBuilder()
        {
            EditorApplication.delayCall += EnsurePrefabsExist;
        }

        [MenuItem("Tools/Tank Warfare/Rebuild Placeholder Prefabs")]
        public static void Rebuild()
        {
            if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
            foreach (TankType type in System.Enum.GetValues(typeof(TankType)))
            {
                string path = $"{Folder}/Tank{type}.prefab";
                AssetDatabase.DeleteAsset(path);
                GameObject instance = TankView.Create(0, type).gameObject;
                instance.name = $"Tank{type}";
                ApplyClassShape(instance, type);
                PrefabUtility.SaveAsPrefabAsset(instance, path);
                Object.DestroyImmediate(instance);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsurePrefabsExist()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (TankType type in System.Enum.GetValues(typeof(TankType)))
            {
                if (!File.Exists($"{Folder}/Tank{type}.prefab"))
                {
                    Rebuild();
                    return;
                }
            }
        }

        private static void ApplyClassShape(GameObject root, TankType type)
        {
            float scale = type switch
            {
                TankType.Heavy => 1.12f,
                TankType.Light => 0.86f,
                _ => 1f
            };
            root.transform.localScale = Vector3.one * scale;
        }
    }
}
#endif
