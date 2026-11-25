using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace Ascend.EditorTools
{
    /// <summary>ASCEND → Record Trailer Shots: enters Play mode, lets the TrailerDirector fly its camera through the
    /// live game and writes every shot as a 1920×1080, 60 fps H.264 MP4 into Recordings/Trailer (next to Assets, so
    /// Unity doesn't import them). Play mode stops on its own when the last shot is done.</summary>
    [InitializeOnLoad]
    public static class TrailerRecorder
    {
        const string Flag = "ascend_record_trailer";
        const string OnlyKey = "ascend_record_trailer_only";
        static MediaEncoder enc;

        static TrailerRecorder() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem("ASCEND/Record Trailer Shots", false, 30)]
        public static void Record()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[ASCEND] Stop Play mode first."); return; }
            PlayerPrefs.SetInt("asc2_intro_seen", 1); PlayerPrefs.SetInt("asc_trailer_rec", 1); PlayerPrefs.Save();
            SessionState.SetBool(Flag, true);
            EditorApplication.isPlaying = true;
        }

        /// <summary>Re-records only some shots: ASCEND_TRAILER_ONLY="win,slots" style list in EditorPrefs.</summary>
        public static void RecordOnly(string shots) { EditorPrefs.SetString(OnlyKey, shots); Record(); }

        static string Dir { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../Recordings/Trailer")); } }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false))
            {
                SessionState.SetBool(Flag, false);
                Directory.CreateDirectory(Dir);
                var go = new GameObject("ASCEND Trailer Director");
                var d = go.AddComponent<TrailerDirector>();
                d.only = EditorPrefs.GetString(OnlyKey, ""); EditorPrefs.DeleteKey(OnlyKey);
                // retakes: Recordings/Trailer/only.txt lists the shots to record again (comma separated)
                var onlyFile = Path.Combine(Dir, "only.txt");
                if (string.IsNullOrEmpty(d.only) && File.Exists(onlyFile)) d.only = File.ReadAllText(onlyFile).Trim();
                TrailerDirector.ShotStarted += Started;
                TrailerDirector.FrameReady += Frame;
                TrailerDirector.ShotEnded += Ended;
                TrailerDirector.Finished += Done;
            }
            else if (s == PlayModeStateChange.ExitingPlayMode)
            {
                PlayerPrefs.SetInt("asc_trailer_rec", 0); PlayerPrefs.Save();
                Close();
                TrailerDirector.ShotStarted -= Started; TrailerDirector.FrameReady -= Frame;
                TrailerDirector.ShotEnded -= Ended; TrailerDirector.Finished -= Done;
            }
        }

        static void Started(string shot)
        {
            Close();
            var attr = new VideoTrackAttributes
            {
                frameRate = new MediaRational(60), width = 1920, height = 1080,
                includeAlpha = false, bitRateMode = VideoBitrateMode.High
            };
            string path = Path.Combine(Dir, shot + ".mp4");
            enc = new MediaEncoder(path, attr);
            Debug.Log("[ASCEND] Trailer: recording " + shot);
        }

        static void Frame(string shot, Texture2D tex) { if (enc != null) enc.AddFrame(tex); }
        static void Ended(string shot) { Close(); Debug.Log("[ASCEND] Trailer: saved " + shot + ".mp4"); }
        static void Close() { if (enc != null) { enc.Dispose(); enc = null; } }

        static void Done()
        {
            Close();
            Debug.Log("[ASCEND] Trailer shots done: " + Dir);
            EditorApplication.isPlaying = false;
        }
    }
}
