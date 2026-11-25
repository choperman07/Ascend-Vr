using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Case Battle: you vs three guests. Everyone puts in the same bet and opens 3 cases; the highest total takes
    /// everything that was opened. Case odds published (expected value 98.5%). The winner also earns an NFT crate.</summary>
    public class CaseBattle : ArcadeStation
    {
        const int Players = 4, Rounds = 3;
        readonly TextMeshPro[] reelT = new TextMeshPro[Players], totalT = new TextMeshPro[Players], nameT = new TextMeshPro[Players];
        readonly Renderer[] podR = new Renderer[Players];
        readonly AvatarBody[] bots = new AvatarBody[Players];
        readonly int[] botIdx = new int[Players];
        bool running2;
        PokeButton goBtn;

        protected override string Rules
        {
            get
            {
                var sb = new System.Text.StringBuilder("4 PLAYERS · 3 CASES EACH · HIGHEST TOTAL TAKES ALL\n");
                sb.Append("CASES HOLD ");
                for (int i = 0; i < Odds.BattleMult.Length; i++) sb.Append(Odds.BattleMult[i].ToString("0.#")).Append('x').Append(i < Odds.BattleMult.Length - 1 ? ", " : "\n");
                sb.Append("WINNER EARNS A CRATE");
                return sb.ToString();
            }
        }
        protected override float CounterWidth { get { return 1.4f; } }
        protected override Vector3 StatusPos { get { return new Vector3(0, 3.65f, 1.0f); } }

        Vector3 PodPos(int i) { return new Vector3(-1.35f + i * 0.9f, 0, 1.15f); }

        protected override void BuildMachine()
        {
            var k = new MeshKit();
            for (int i = 0; i < Players; i++)
            {
                var p = PodPos(i);
                k.Box(p + new Vector3(0, 2.45f, 0.1f), new Vector3(0.7f, 1.1f, 0.08f), Palette.Hex("0A0812"), 0.03f);
                k.Cylinder(p + new Vector3(0, 0.05f, -0.15f), 0.35f, 0.38f, 0.1f, 28, Quaternion.identity, Palette.Dark, 0.05f);
            }
            k.Box(new Vector3(0, 3.15f, 1.25f), new Vector3(3.6f, 0.12f, 0.1f), Palette.Metal, 0.05f);
            k.Spawn("Pods", transform, Palette.Glossy, false);
            for (int i = 0; i < Players; i++)
            {
                var p = PodPos(i);
                var fk = new MeshKit(); fk.Bar(new Vector3(-0.36f, 1.9f, 0), new Vector3(0.36f, 1.9f, 0), 0.02f, Color.white, 1f); fk.Bar(new Vector3(-0.36f, 3.0f, 0), new Vector3(0.36f, 3.0f, 0), 0.02f, Color.white, 1f);
                fk.Bar(new Vector3(-0.36f, 1.9f, 0), new Vector3(-0.36f, 3.0f, 0), 0.02f, Color.white, 1f); fk.Bar(new Vector3(0.36f, 1.9f, 0), new Vector3(0.36f, 3.0f, 0), 0.02f, Color.white, 1f);
                var fg = fk.Spawn("PodFrame" + i, transform, p + new Vector3(0, 0, 0.05f), Quaternion.identity, Palette.Neon, false);
                podR[i] = fg.GetComponent<Renderer>();
                Fx.SetColor(podR[i], i == 0 ? Palette.Gold : Accent, 0.6f);
                nameT[i] = Txt.Make("Name" + i, transform, p + new Vector3(0, 2.88f, 0.04f), i == 0 ? "YOU" : "", 0.05f, Palette.Text, Palette.Display);
                reelT[i] = Txt.Glow("Reel" + i, transform, p + new Vector3(0, 2.45f, 0.04f), "-", 0.18f, Palette.Text);
                totalT[i] = Txt.Make("Total" + i, transform, p + new Vector3(0, 2.08f, 0.04f), "", 0.07f, Palette.Gold, Palette.Display);
                if (i > 0)
                {
                    botIdx[i] = Odds.Range(0, Roster.Count);
                    bots[i] = Avatars.Create("Rival" + i, transform, p + new Vector3(0, 0.1f, -0.15f), 180f, Roster.Face(botIdx[i]));
                    var prof = bots[i].gameObject.AddComponent<AvatarProfile>();
                    prof.displayName = Roster.Name(botIdx[i]); prof.rarity = Roster.Face(botIdx[i]); prof.xp = Roster.XP(botIdx[i]); prof.squad = Roster.Squad(botIdx[i]); prof.status = "IN A CASE BATTLE";
                    nameT[i].text = Roster.Name(botIdx[i]);
                }
            }
            Txt.Glow("Title", transform, new Vector3(0, 3.35f, 1.18f), "CASE BATTLE", 0.16f, Accent);
            goBtn = PokeButton.Create("Battle", transform, new Vector3(0.45f, TableY + 0.035f, -0.32f), Quaternion.identity, 0.05f, Palette.Gold, "BATTLE", () => StartCoroutine(Battle()));
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "START BATTLE", 20, y, 520, 90, Palette.Gold, () => StartCoroutine(Battle()), 40);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 320); } }

        static string M(int idx) { return Odds.BattleMult[idx].ToString("0.#") + "x"; }
        static Color C(int idx) { return idx >= 6 ? Palette.Magenta : idx >= 4 ? Palette.Violet : idx >= 3 ? Palette.Gold : idx >= 1 ? Palette.Text : Palette.Muted; }

        IEnumerator Battle()
        {
            if (running2) yield break;
            long stake = Take();
            if (stake <= 0) yield break;
            running2 = true;
            goBtn.SetInteractable(false);
            Say("battle");
            Sfx.Play("battle_start", W(new Vector3(0, 1.8f, 1f)), 1f);
            for (int i = 1; i < Players; i++)
            {
                botIdx[i] = Odds.Range(0, Roster.Count);
                nameT[i].text = Roster.Name(botIdx[i]);
                var prof = bots[i].GetComponent<AvatarProfile>(); prof.displayName = Roster.Name(botIdx[i]); prof.rarity = Roster.Face(botIdx[i]);
                bots[i].Play(Emote.Wave, 1f);
            }
            float[] total = new float[Players];
            for (int i = 0; i < Players; i++) { totalT[i].text = "0.0x"; Fx.SetColor(podR[i], i == 0 ? Palette.Gold : Accent, 0.6f); }
            for (int round = 0; round < Rounds; round++)
            {
                int[] res = new int[Players];
                for (int i = 0; i < Players; i++) res[i] = Odds.RollBattle();
                float t = 0, dur = 2.2f, next = 0;
                while (t < dur)
                {
                    t += Time.deltaTime;
                    float k = t / dur;
                    if (t >= next)
                    {
                        // filler symbols are drawn from the same published odds (no fake near-misses)
                        for (int i = 0; i < Players; i++) { int f = Odds.RollBattle(); reelT[i].text = M(f); reelT[i].color = C(f); }
                        Sfx.Play("battle_tick", W(new Vector3(0, 1.6f, 1f)), 0.35f, 1f + k * 0.4f);
                        next = t + Mathf.Lerp(0.05f, 0.28f, k * k);
                    }
                    yield return null;
                }
                for (int i = 0; i < Players; i++)
                {
                    reelT[i].text = M(res[i]); reelT[i].color = C(res[i]);
                    total[i] += Odds.BattleMult[res[i]];
                    totalT[i].text = total[i].ToString("0.0") + "x";
                    if (res[i] >= 4) { Sfx.Play(res[i] >= 6 ? "reveal_mythic" : "reveal_epic", W(PodPos(i) + Vector3.up * 2.4f), 0.8f); Juice.Sparks(W(PodPos(i) + Vector3.up * 2.45f), C(res[i]), 24); }
                    StartCoroutine(Fx.Pulse(reelT[i].transform, 1.25f, 0.25f));
                }
                Sfx.Play("reel_stop", W(new Vector3(0, 1.6f, 1f)), 0.9f);
                XRIn.HapticBoth(0.3f, 0.05f);
                yield return new WaitForSeconds(0.9f);
            }
            float best = -1; var winners = new List<int>();
            for (int i = 0; i < Players; i++) { if (total[i] > best + 1e-4f) { best = total[i]; winners.Clear(); winners.Add(i); } else if (Mathf.Abs(total[i] - best) < 1e-4f) winners.Add(i); }
            float potMult = 0; for (int i = 0; i < Players; i++) potMult += total[i];
            potMult /= Rounds;   // each case is worth bet/3 × multiplier
            long pot = (long)(stake * (double)potMult);
            foreach (var w in winners) { Fx.SetColor(podR[w], Palette.Mint, 1.2f); if (w > 0) { bots[w].Play(Emote.Cheer, 2f); Roster.Gain(botIdx[w], 200); } }
            for (int i = 1; i < Players; i++) if (!winners.Contains(i)) bots[i].Play(Emote.Facepalm, 1.5f);
            bool youWin = winners.Contains(0);
            long yours = youWin ? pot / winners.Count : 0;
            // a 4-way tie can split a pot smaller than your bet: that is not celebrated as a win
            Status(youWin ? (winners.Count > 1 ? "TIE · SPLIT POT " : "YOU WIN THE POT ") + yours.ToString("N0") : nameT[winners[0]].text + " TAKES THE POT", yours > stake ? Palette.Mint : Palette.Muted);
            if (youWin) { if (yours > stake) Say("battle_won"); GameState.I.GiveCrate("CASE BATTLE WIN"); GameState.I.Record("battle_win"); }
            PayDirect(yours, stake, W(PodPos(0) + Vector3.up * 1.2f));
            if (!youWin) ChatWall.Post(nameT[winners[0]].text, "GG! " + best.ToString("0.0") + "x", Palette.Text, false);
            yield return new WaitForSeconds(1.5f);
            goBtn.SetInteractable(true);
            running2 = false;
        }
    }
}
