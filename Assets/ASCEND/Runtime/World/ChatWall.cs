using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Lobby chat wall: live feed of the floor (bots, big wins, online players) + quick chat for VR.</summary>
    public class ChatWall : MonoBehaviour
    {
        static ChatWall _i;
        static readonly List<string> lines = new List<string>();
        TextMeshProUGUI logTxt;
        public static readonly string[] Quick = { "GG!", "WHO'S RAIDING?", "NICE PULL!", "SQUAD UP?", "LET'S GO!", "TAKING A BREAK" };
        public static IList<string> Lines { get { return lines; } }
        public static event System.Action Updated;

        void Awake() { _i = this; }

        void Start()
        {
            var p = UIKit.Panel("ChatPanel", transform, new Vector3(0, 1.8f, 0), Quaternion.identity, new Vector2(1100, 1060), 2.1f, Palette.Teal);
            UIKit.Label(p, "LIVE CHAT", 36, 16, 800, 80, 72, Palette.Text, Palette.Display);
            UIKit.Label(p, "QUICK CHAT BELOW · VOICE CHAT ON THE ONLINE FLOOR (MENU > ONLINE)", 36, 92, 1030, 36, 20, Palette.Muted, Palette.Mono);
            UIKit.Box(p, 30, 140, 1040, 640, UIKit.CellBg);
            logTxt = UIKit.Label(p, "", 50, 150, 1000, 620, 28, Palette.Text, Palette.Body, TextAlignmentOptions.BottomLeft);
            for (int i = 0; i < Quick.Length; i++)
            {
                string q = Quick[i];
                UIKit.Btn(p, q, 30 + (i % 3) * 350, 800 + (i / 3) * 110, 340, 96, Palette.Teal, () => PlayerSays(q), 34);
            }
            Redraw();
        }

        public static void PlayerSays(string q)
        {
            Post(GameState.I.playerName, q, Palette.Teal, true);
            GameState.I.Record("chat");
            var bots = FindFirstObjectByType<BotDirector>();
            if (bots != null) bots.Reply(q);
        }

        public static void Post(string who, string msg, Color c, bool sendOnline)
        {
            lines.Add("<color=#" + ColorUtility.ToHtmlStringRGB(c) + "><b>" + who + "</b></color>  " + msg);
            while (lines.Count > 14) lines.RemoveAt(0);
            if (_i != null) _i.Redraw();
            if (Updated != null) Updated();
            if (sendOnline)
            {
                var net = FindFirstObjectByType<OnlineSession>();
                if (net != null && net.Connected) net.SendText(msg);
            }
        }

        void Redraw() { Fill(logTxt, lines.Count); }

        /// <summary>Shows the newest lines (at most maxLines) that fit the label's box. Long messages wrap onto two
        /// lines; with bottom alignment the overflow would otherwise spill up over the panel's title.</summary>
        public static void Fill(TextMeshProUGUI t, int maxLines)
        {
            if (t == null) return;
            var r = t.rectTransform.rect;
            int start = Mathf.Max(0, lines.Count - maxLines);
            var sb = new System.Text.StringBuilder();
            for (; start < lines.Count; start++)
            {
                sb.Length = 0;
                for (int i = start; i < lines.Count; i++) { if (i > start) sb.Append('\n'); sb.Append(lines[i]); }
                if (r.height <= 0f || start == lines.Count - 1 || t.GetPreferredValues(sb.ToString(), r.width, 0f).y <= r.height) break;
            }
            t.text = start < lines.Count ? sb.ToString() : "";
        }
    }
}
