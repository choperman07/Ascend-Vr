using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ascend.EditorTools
{
    /// <summary>ASCEND → Build Web (WebGL): a browser version of the desktop game (WASD + mouse, no VR) in Builds/Web.
    /// Brotli with the decompression fallback, so it plays from any static host without special server headers.
    /// The editor is switched back to the platform it was on when the build ends. Log: Builds/web_build_log.txt.</summary>
    public static class WebBuild
    {
        [MenuItem("ASCEND/Build Web (WebGL)", false, 55)]
        public static void Build()
        {
            var log = new StringBuilder();
            Action<string> L = s => { log.AppendLine(s); Debug.Log("[ASCEND Web] " + s); };
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds"));
            var dir = Path.Combine(root, "Web");
            Directory.CreateDirectory(root);
            L("ASCEND web build · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · Unity " + Application.unityVersion);
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                L("SKIPPED: Web Build Support is not installed (Unity Hub → Installs → Manage → Add modules).");
                File.WriteAllText(Path.Combine(root, "web_build_log.txt"), log.ToString());
                return;
            }
            var prevTarget = EditorUserBuildSettings.activeBuildTarget;
            var prevGroup = BuildPipeline.GetBuildTargetGroup(prevTarget);
            PlayerSettings.companyName = "UCJC Product Narrative";
            PlayerSettings.productName = "ASCEND";
            PlayerSettings.runInBackground = true;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.memorySize = 512;
            try { PlayerSettings.WebGL.initialMemorySize = 512; PlayerSettings.WebGL.maximumMemorySize = 4096; PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric; } catch (Exception e) { L("memory settings: " + e.Message); }
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.showDiagnostics = false;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);
            // only the game's own scene: asset-pack demo scenes in the build list would ship their whole content
            var scenes = EditorBuildSettings.scenes.Where(x => x.enabled && File.Exists(x.path) && x.path.StartsWith("Assets/ASCEND/")).Select(x => x.path).ToArray();
            if (scenes.Length == 0) scenes = new[] { "Assets/ASCEND/Scenes/ASCEND_World.unity" };
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            var opts = new BuildPlayerOptions { scenes = scenes, target = BuildTarget.WebGL, targetGroup = BuildTargetGroup.WebGL, locationPathName = dir, options = BuildOptions.None };
            L("Building WebGL → " + dir + " (" + scenes.Length + " scene/s)");
            var report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            L("WebGL: " + sum.result + " · " + (sum.totalSize / (1024f * 1024f)).ToString("0") + " MB · " + sum.totalTime.ToString(@"hh\:mm\:ss") + " · " + sum.totalErrors + " errors");
            if (sum.result != BuildResult.Succeeded)
                foreach (var st in report.steps) foreach (var m in st.messages) if (m.type == LogType.Error || m.type == LogType.Exception) L("  ERROR: " + m.content);
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) L("  " + f.Substring(dir.Length) + " · " + (new FileInfo(f).Length / (1024f * 1024f)).ToString("0.0") + " MB");
            File.WriteAllText(Path.Combine(root, "web_build_log.txt"), log.ToString());
            if (prevTarget != BuildTarget.WebGL) EditorUserBuildSettings.SwitchActiveBuildTarget(prevGroup, prevTarget);
        }
    }
}
