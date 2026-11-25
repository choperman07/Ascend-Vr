using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Lobby leaderboard: XP ranking of the roster + you (bots climb over time).</summary>
    public class Leaderboard : MonoBehaviour
    {
        TextMeshProUGUI rowsT;
        float next;

        void Start()
        {
            var p = UIKit.Panel("Board", transform, new Vector3(0, 2.0f, 0), Quaternion.identity, new Vector2(900, 1000), 1.8f, Palette.Gold);
            UIKit.Label(p, "LEADERBOARD", 36, 16, 820, 90, 80, Palette.Gold, Palette.Display);
            UIKit.Label(p, "SEASON 1 · RANKED BY XP · XP COMES FROM PLAY, MISSIONS AND RAIDS", 36, 100, 840, 40, 20, Palette.Muted, Palette.Mono);
            rowsT = UIKit.Label(p, "", 36, 150, 840, 820, 32, Palette.Text, Palette.Body, TextAlignmentOptions.TopLeft);
            Redraw();
        }

        void Update() { if (Time.time > next) { next = Time.time + 6f; Roster.Gain(Odds.Range(0, Roster.Count), Odds.Range(20, 400)); Redraw(); } }

        void Redraw()
        {
            var list = new List<KeyValuePair<string, long>>();
            for (int i = 0; i < Roster.Count; i++) list.Add(new KeyValuePair<string, long>(Roster.Name(i), Roster.XP(i)));
            var gs = GameState.I;
            list.Add(new KeyValuePair<string, long>("<color=#FFC857>" + gs.playerName + " (YOU)</color>", gs.xp));
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Mathf.Min(12, list.Count); i++)
            {
                var t = Roster.TierOf(list[i].Value);
                sb.Append(i < 3 ? "<color=#FFC857>" : "<color=#9A94B0>").Append((i + 1).ToString("00")).Append("</color>  ")
                  .Append(list[i].Key).Append("  <color=#").Append(ColorUtility.ToHtmlStringRGB(Palette.TierColor(t))).Append(">")
                  .Append(GameState.TierNames[(int)t]).Append("</color>  <color=#9A94B0>").Append(list[i].Value.ToString("N0")).Append(" XP</color>\n");
            }
            rowsT.text = sb.ToString();
        }
    }
}
