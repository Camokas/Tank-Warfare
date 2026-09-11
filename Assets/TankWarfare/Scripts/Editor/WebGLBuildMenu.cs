#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TankWarfare.Editor
{
    public static class WebGLBuildMenu
    {
        [MenuItem("Tools/Tank Warfare/Build WebGL")]
        public static void Build()
        {
            const string output = "Builds/WebGL";
            Directory.CreateDirectory(output);
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = true;
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"WebGL build готов: {Path.GetFullPath(output)}");
                EditorUtility.RevealInFinder(output);
            }
            else
            {
                Debug.LogError($"WebGL build завершился со статусом {report.summary.result}");
            }
        }
    }
}
#endif
