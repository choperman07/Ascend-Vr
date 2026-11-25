using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ascend.EditorTools
{
    /// <summary>ASCEND → Build Desktop: playable copies for computers (no headset needed: WASD + mouse).
    /// Windows → Builds/ASCEND_Windows.zip (ASCEND.exe inside). macOS → Builds/ASCEND_Mac.zip (ASCEND.app, Intel
    /// 64-bit, runs on Apple Silicon through Rosetta). Building for Mac from Windows needs the "Mac Build Support (Mono)"
    /// module in Unity Hub. The Mac zip is written with Unix permissions so the app is still executable after unzipping.
    /// Results go to the Console and to Builds/build_log.txt.</summary>
    public static class DesktopBuild
    {
        static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds")); } }
        static readonly StringBuilder log = new StringBuilder();

        [MenuItem("ASCEND/Build Desktop/Windows + Mac", false, 50)]
        public static void BuildBoth() { Begin(); Windows(); Mac(); End(); }

        [MenuItem("ASCEND/Build Desktop/Windows (.exe)", false, 51)]
        public static void BuildWindows() { Begin(); Windows(); End(); }

        [MenuItem("ASCEND/Build Desktop/Mac (.app)", false, 52)]
        public static void BuildMac() { Begin(); Mac(); End(); }

        static void Begin()
        {
            log.Length = 0;
            Directory.CreateDirectory(Root);
            Log("ASCEND desktop build · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · Unity " + Application.unityVersion);
            PlayerSettings.companyName = "UCJC Product Narrative";
            PlayerSettings.productName = "ASCEND";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);   // no C++ toolchain needed, and it can target Mac from Windows
        }

        static void End()
        {
            Log("Done. Folder: " + Root);
            File.WriteAllText(Path.Combine(Root, "build_log.txt"), log.ToString());
            EditorUtility.RevealInFinder(Root);
        }

        static void Log(string s) { log.AppendLine(s); Debug.Log("[ASCEND Build] " + s); }

        static string[] Scenes()
        {
            var s = EditorBuildSettings.scenes.Where(x => x.enabled && File.Exists(x.path)).Select(x => x.path).ToArray();
            if (s.Length == 0 && File.Exists("Assets/ASCEND/Scenes/ASCEND_World.unity")) s = new[] { "Assets/ASCEND/Scenes/ASCEND_World.unity" };
            return s;
        }

        static bool Run(BuildTarget target, string exePath)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            {
                Log("SKIPPED " + target + ": the build module is not installed. Unity Hub → Installs → 6000.0.84f1 → ⚙ Add modules → "
                    + (target == BuildTarget.StandaloneOSX ? "Mac Build Support (Mono)" : "Windows Build Support (Mono)") + ".");
                return false;
            }
            var dir = Path.GetDirectoryName(exePath);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var opts = new BuildPlayerOptions { scenes = Scenes(), target = target, targetGroup = BuildTargetGroup.Standalone, locationPathName = exePath, options = BuildOptions.None };
            Log("Building " + target + " → " + exePath + " (" + opts.scenes.Length + " scene/s)");
            var report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            Log(target + ": " + sum.result + " · " + (sum.totalSize / (1024f * 1024f)).ToString("0") + " MB · " + sum.totalTime.ToString(@"mm\:ss") + " · " + sum.totalErrors + " errors");
            if (sum.result != BuildResult.Succeeded)
                foreach (var st in report.steps) foreach (var m in st.messages) if (m.type == LogType.Error || m.type == LogType.Exception) Log("  ERROR: " + m.content);
            return sum.result == BuildResult.Succeeded;
        }

        // ------------------------------------------------------------------ Windows
        static void Windows()
        {
            var dir = Path.Combine(Root, "Windows");
            if (!Run(BuildTarget.StandaloneWindows64, Path.Combine(dir, "ASCEND.exe"))) return;
            File.WriteAllText(Path.Combine(dir, "LEEME.txt"), Readme(false), Encoding.UTF8);
            Zip(dir, Path.Combine(Root, "ASCEND_Windows.zip"), "ASCEND", false);
        }

        // ------------------------------------------------------------------ macOS
        static void Mac()
        {
            if (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX)) SetMacArchitecture();
            var dir = Path.Combine(Root, "Mac");
            if (!Run(BuildTarget.StandaloneOSX, Path.Combine(dir, "ASCEND.app"))) return;
            File.WriteAllText(Path.Combine(dir, "LEEME.txt"), Readme(true), Encoding.UTF8);
            Zip(dir, Path.Combine(Root, "ASCEND_Mac.zip"), "ASCEND", true);
        }

        /// <summary>Intel 64-bit: a Mac app built on Windows cannot be code-signed, and macOS refuses unsigned native
        /// Apple-silicon code, while Intel code runs on any Mac (through Rosetta on M1/M2/M3/M4).</summary>
        static void SetMacArchitecture()
        {
            try
            {
                var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.OSXStandalone.UserBuildSettings")).FirstOrDefault(x => x != null);
                var p = t != null ? t.GetProperty("architecture") : null;
                if (p != null) { p.SetValue(null, Enum.Parse(p.PropertyType, "x64ARM64")); Log("Mac architecture: Universal (Intel + Apple silicon)"); }
            }
            catch (Exception e) { Log("Mac architecture not set: " + e.Message); }
        }

        // ------------------------------------------------------------------ zip (with Unix permissions for the Mac app)
        static void Zip(string srcDir, string zipPath, string topFolder, bool unixExec)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            var extAttr = typeof(ZipArchiveEntry).GetProperty("ExternalAttributes");
            int n = 0;
            using (var fs = new FileStream(zipPath, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var f in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
                {
                    var rel = f.Substring(srcDir.Length).TrimStart('\\', '/').Replace('\\', '/');
                    if (rel.Contains("_DoNotShip") || rel.Contains("BurstDebugInformation")) continue;
                    var e = zip.CreateEntry(topFolder + "/" + rel, System.IO.Compression.CompressionLevel.Optimal);
                    if (unixExec && extAttr != null && extAttr.CanWrite)
                    {
                        // regular file, rwxr-xr-x (made-by Unix is not settable here, but macOS Archive Utility honours the mode bits)
                        extAttr.SetValue(e, unchecked((int)(0x81ED0000u)));
                    }
                    using (var es = e.Open()) using (var inp = File.OpenRead(f)) inp.CopyTo(es);
                    n++;
                }
            }
            if (unixExec) MarkUnix(zipPath);
            Log("Zipped " + n + " files → " + zipPath + " (" + (new FileInfo(zipPath).Length / (1024f * 1024f)).ToString("0") + " MB)" + (unixExec && extAttr == null ? " · WARNING: could not store Unix permissions" : ""));
        }

        /// <summary>Windows writes "made by: MS-DOS" in the zip's central directory, so macOS / unzip ignore the
        /// permission bits. Rewrites the host byte of every central-directory entry to 3 (Unix).</summary>
        static void MarkUnix(string zipPath)
        {
            var d = File.ReadAllBytes(zipPath);
            // end-of-central-directory record (search backwards), then walk the central directory entry by entry
            int eocd = -1;
            for (int i = d.Length - 22; i >= Math.Max(0, d.Length - 65557); i--)
                if (d[i] == 0x50 && d[i + 1] == 0x4B && d[i + 2] == 0x05 && d[i + 3] == 0x06) { eocd = i; break; }
            if (eocd < 0) { Log("WARNING: zip directory not found, permissions not marked"); return; }
            int count = BitConverter.ToUInt16(d, eocd + 10);
            long pos = BitConverter.ToUInt32(d, eocd + 16);
            int n = 0;
            for (int e = 0; e < count && pos + 46 <= d.Length; e++)
            {
                if (BitConverter.ToUInt32(d, (int)pos) != 0x02014b50) break;
                d[pos + 5] = 3;   // version made by: host = Unix
                int nameLen = BitConverter.ToUInt16(d, (int)pos + 28), extraLen = BitConverter.ToUInt16(d, (int)pos + 30), commLen = BitConverter.ToUInt16(d, (int)pos + 32);
                pos += 46 + nameLen + extraLen + commLen; n++;
            }
            File.WriteAllBytes(zipPath, d);
            Log("Unix permissions marked on " + n + " entries");
        }

        [MenuItem("ASCEND/Build Desktop/Re-zip Mac (no rebuild)", false, 60)]
        public static void RezipMac()
        {
            Begin();
            var dir = Path.Combine(Root, "Mac");
            if (Directory.Exists(Path.Combine(dir, "ASCEND.app"))) Zip(dir, Path.Combine(Root, "ASCEND_Mac.zip"), "ASCEND", true);
            else Log("No Mac build to zip.");
            End();
        }

        static string Readme(bool mac)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ASCEND · Casino VR (versión de escritorio) · UCJC · Information Technology");
            sb.AppendLine("+18 · Solo fichas de juego · Sin dinero real · ¿Problemas con el juego? jugarbien.es");
            sb.AppendLine();
            if (mac)
            {
                sb.AppendLine("CÓMO ABRIRLO EN MAC");
                sb.AppendLine("1. Descomprime ASCEND_Mac.zip y arrastra ASCEND.app a Aplicaciones (o déjala donde quieras).");
                sb.AppendLine("2. Abre la app Terminal, pega estas tres líneas (Enter tras cada una) y escribe tu contraseña si la pide:");
                sb.AppendLine("     softwareupdate --install-rosetta --agree-to-license");
                sb.AppendLine("     xattr -cr ~/Downloads/ASCEND/ASCEND.app");
                sb.AppendLine("     codesign --force --deep --sign - ~/Downloads/ASCEND/ASCEND.app");
                sb.AppendLine("   (Si la app no está en Descargas, en lugar de la ruta arrastra ASCEND.app a la ventana de Terminal.)");
                sb.AppendLine("3. Doble clic en ASCEND.app. Si aún avisa: Ajustes del Sistema → Privacidad y seguridad → \"Abrir igualmente\".");
                sb.AppendLine("Por qué: la app se compila en Windows y no lleva firma de Apple; estas órdenes la firman en tu Mac y quitan la cuarentena.");
                sb.AppendLine("Universal: nativa en Mac con chip Apple (M1/M2/M3/M4) y en Mac Intel.");
            }
            else
            {
                sb.AppendLine("CÓMO ABRIRLO EN WINDOWS");
                sb.AppendLine("1. Descomprime ASCEND_Windows.zip (clic derecho → Extraer todo). No lo abras desde dentro del zip.");
                sb.AppendLine("2. Doble clic en ASCEND.exe. Si Windows SmartScreen avisa: \"Más información\" → \"Ejecutar de todas formas\".");
                sb.AppendLine("Con unas Meta Quest conectadas por Quest Link / Air Link también se puede jugar en VR.");
            }
            sb.AppendLine();
            sb.AppendLine("CONTROLES (sin gafas)");
            sb.AppendLine("WASD moverse · Q/E o flechas o botón derecho del ratón girar · clic izquierdo usar/sentarse/hablar");
            sb.AppendLine("Espacio levantarse y pasar diálogos · Tab menú · 1-9 plantas (modo libre) · Ctrl agacharse · Alt+F4 salir");
            sb.AppendLine("El modo principal es el MODO MAFIA (15 días). El casino libre se activa en Menú → SETTINGS → MODO.");
            return sb.ToString();
        }
    }
}
