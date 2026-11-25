using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Inner Circle (Mythic floor): holders vote on what the next season adds. Your vote counts once per day.</summary>
    public class VoteBoard : MonoBehaviour
    {
        static readonly string[] Options = { "NEW RAID BOSS: THE DEALER", "ROOFTOP DJ SET EVERY FRIDAY", "SQUAD CASE BATTLES", "MYTHIC-ONLY SLOT CABINET" };
        readonly int[] votes = { 412, 388, 301, 177 };
        readonly List<UnityEngine.UI.Button> btns = new List<UnityEngine.UI.Button>();
        TextMeshProUGUI resultT;
        bool voted;

        void Start()
        {
            voted = PlayerPrefs.GetString("asc2_vote_day", "") == System.DateTime.Now.ToString("yyyyMMdd");
            var p = UIKit.Panel("Vote", transform, new Vector3(0, 1.9f, 0), Quaternion.identity, new Vector2(1000, 900), 1.9f, Palette.Magenta);
            UIKit.Label(p, "INNER CIRCLE VOTE", 36, 16, 920, 90, 78, Palette.Magenta, Palette.Display);
            UIKit.Label(p, "MYTHIC HOLDERS DECIDE WHAT ASCEND BUILDS NEXT", 36, 100, 920, 40, 22, Palette.Muted, Palette.Mono);
            for (int i = 0; i < Options.Length; i++)
            {
                int idx = i;
                btns.Add(UIKit.Btn(p, Options[i], 36, 160 + i * 130, 920, 110, Palette.Magenta, () => Vote(idx), 34));
            }
            resultT = UIKit.Label(p, "", 36, 700, 920, 180, 26, Palette.Text, Palette.Mono, TextAlignmentOptions.TopLeft);
            Redraw();
        }

        void Vote(int i)
        {
            if (voted) { Sfx.UI("ui_error"); return; }
            voted = true; votes[i]++;
            PlayerPrefs.SetString("asc2_vote_day", System.DateTime.Now.ToString("yyyyMMdd"));
            Sfx.UI("achievement", 0.7f);
            GameState.I.Unlock("VOICE OF THE CIRCLE", "VOTED IN THE INNER CIRCLE", 300);
            Redraw();
        }

        void Redraw()
        {
            int total = 0; foreach (var v in votes) total += v;
            var sb = new System.Text.StringBuilder(voted ? "THANKS FOR VOTING · RESULTS:\n" : "TAP AN OPTION TO VOTE · LIVE RESULTS:\n");
            for (int i = 0; i < Options.Length; i++) sb.Append((votes[i] * 100f / total).ToString("0")).Append("%  ").Append(Options[i]).Append('\n');
            resultT.text = sb.ToString();
            for (int i = 0; i < btns.Count; i++) btns[i].interactable = !voted;
        }
    }
}
