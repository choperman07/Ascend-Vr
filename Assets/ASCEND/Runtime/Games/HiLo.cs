using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Hi-Lo card ladder: a giant card is turned; call HIGHER or LOWER for the next one. Every correct call
    /// sets the multiplier to 0.95 ÷ the chance of the whole streak (the house edge is taken once, not per call, so the
    /// RTP is 95% whenever you cash out). Next multiplier and chance shown for each call. Same rank loses. Cash out any time.</summary>
    public class HiLo : ArcadeStation
    {
        int current = -1, streak;
        float mult = 1f;
        double fair = 1.0;   // 1 ÷ probability of the whole streak so far
        long stake;
        bool playing, busy;
        Card3D shown;
        readonly List<Card3D> trail = new List<Card3D>();
        TextMeshPro multT, hiT, loT, streakT;
        PokeButton startBtn, hiBtn, loBtn, cashBtn;

        protected override string Rules { get { return "START TURNS A CARD (ACE HIGH)\nCALL HIGHER OR LOWER · SAME RANK LOSES\nEVERY CORRECT CALL RAISES THE MULTIPLIER\nCASH OUT ANY TIME"; } }

        static int Rank(int c) { return c % 13; }
        float PHi { get { return (12 - Rank(current)) / 13f; } }
        float PLo { get { return Rank(current) / 13f; } }

        protected override void BuildMachine()
        {
            var k = new MeshKit();
            k.Box(new Vector3(0, 1.75f, 0.6f), new Vector3(1.4f, 1.4f, 0.06f), Palette.Hex("08060F"), 0f);
            k.Box(new Vector3(0, 1.62f, 0.55f), new Vector3(0.5f, 0.7f, 0.02f), Palette.Hex("120D1F"), 0f);
            for (int s = -1; s <= 1; s += 2) k.Bar(new Vector3(s * 0.7f, 1.05f, 0.56f), new Vector3(s * 0.7f, 2.45f, 0.56f), 0.015f, Accent, 1f);
            k.Bar(new Vector3(-0.7f, 2.45f, 0.56f), new Vector3(0.7f, 2.45f, 0.56f), 0.015f, Accent, 1f);
            k.Spawn("Board", transform, Palette.Neon, false);
            multT = Txt.Glow("Mult", transform, new Vector3(0, 2.3f, 0.55f), "1.00x", 0.15f, Palette.Text);
            streakT = Txt.Make("Streak", transform, new Vector3(0, 1.17f, 0.55f), "", 0.05f, Palette.Muted, Palette.Mono);
            hiT = Txt.Make("Hi", transform, new Vector3(0.48f, 1.85f, 0.55f), "", 0.05f, Palette.Mint, Palette.Display);
            loT = Txt.Make("Lo", transform, new Vector3(0.48f, 1.45f, 0.55f), "", 0.05f, Palette.Red, Palette.Display);
            // laid out around the chip tray (x -0.50..-0.14) and the BET ring (x 0.03..0.21): START used to sit under the
            // tray's chip stacks and HIGHER on top of the bet ring
            startBtn = PokeButton.Create("Start", transform, new Vector3(-0.62f, TableY + 0.035f, -0.34f), Quaternion.identity, 0.045f, Palette.Gold, "START", () => StartCoroutine(Begin()));
            hiBtn = PokeButton.Create("Higher", transform, new Vector3(0.3f, TableY + 0.035f, -0.36f), Quaternion.identity, 0.04f, Palette.Mint, "HIGHER", () => StartCoroutine(Call(true)));
            loBtn = PokeButton.Create("Lower", transform, new Vector3(0.5f, TableY + 0.035f, -0.36f), Quaternion.identity, 0.04f, Palette.Red, "LOWER", () => StartCoroutine(Call(false)));
            cashBtn = PokeButton.Create("Cash", transform, new Vector3(0.64f, TableY + 0.035f, -0.2f), Quaternion.identity, 0.05f, Palette.Gold, "CASH OUT", CashOut);
            Buttons();
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "START", 20, y, 520, 64, Palette.Gold, () => StartCoroutine(Begin()), 32);
            UIKit.Btn(p, "HIGHER", 20, y + 72, 250, 70, Palette.Mint, () => StartCoroutine(Call(true)), 32);
            UIKit.Btn(p, "LOWER", 290, y + 72, 250, 70, Palette.Red, () => StartCoroutine(Call(false)), 32);
            UIKit.Btn(p, "CASH OUT", 20, y + 150, 520, 64, Palette.Gold, CashOut, 30);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 440); } }

        void Buttons()
        {
            startBtn.SetInteractable(!playing && !busy);
            hiBtn.SetInteractable(playing && !busy && current >= 0 && PHi > 0);
            loBtn.SetInteractable(playing && !busy && current >= 0 && PLo > 0);
            cashBtn.SetInteractable(playing && !busy && streak > 0);
            if (playing && current >= 0)
            {
                hiT.text = PHi > 0 ? "HIGHER " + (PHi * 100f).ToString("0") + "% · " + NextMult(PHi).ToString("0.00") + "x" : "—";
                loT.text = PLo > 0 ? "LOWER " + (PLo * 100f).ToString("0") + "% · " + NextMult(PLo).ToString("0.00") + "x" : "—";
            }
            else hiT.text = loT.text = "";
            multT.text = mult.ToString("0.00") + "x";
            streakT.text = playing ? "STREAK " + streak : "";
        }

        float NextMult(float p) { return (float)(0.95 * fair / p); }

        IEnumerator Show(int card)
        {
            if (shown != null) { trail.Add(shown); StartCoroutine(Fx.Arc(shown.transform, W(new Vector3(-0.45f + trail.Count * 0.03f, 1.3f + trail.Count * 0.01f, 0.53f)), 0.05f, 0.3f)); }
            var c = Card3D.Spawn(card, W(new Vector3(0, 1.62f, 0.4f)), transform.rotation * Quaternion.Euler(-90, 0, 0), false);
            c.transform.localScale = Vector3.one * 2.6f;
            shown = c;
            Sfx.Play("card_flip", c.transform.position, 0.8f);
            yield return StartCoroutine(c.Flip(0.35f));
        }

        IEnumerator Begin()
        {
            if (playing || busy) yield break;
            stake = Take();
            if (stake <= 0) yield break;
            busy = true; playing = true; mult = 1f; fair = 1.0; streak = 0;
            foreach (var t in trail) if (t != null) Destroy(t.gameObject);
            trail.Clear();
            if (shown != null) Destroy(shown.gameObject);
            shown = null;
            Buttons();
            current = Odds.Range(0, 52);
            yield return StartCoroutine(Show(current));
            busy = false; Buttons();
            Status("HIGHER OR LOWER?", Palette.Text);
            Say("hilo");
        }

        IEnumerator Call(bool higher)
        {
            if (!playing || busy) yield break;
            float p = higher ? PHi : PLo;
            if (p <= 0) yield break;
            busy = true; Buttons();
            int next = Odds.Range(0, 52);
            yield return StartCoroutine(Show(next));
            bool ok = higher ? Rank(next) > Rank(current) : Rank(next) < Rank(current);
            current = next;
            if (ok)
            {
                fair /= p; mult = (float)(0.95 * fair); streak++;
                Sfx.Play("gem", shown.transform.position, 0.8f, 1f + streak * 0.05f);
                Status("CORRECT · " + mult.ToString("0.00") + "x", Palette.Mint);
                busy = false; Buttons();
            }
            else
            {
                playing = false;
                Status("WRONG CALL", Palette.Red);
                PayDirect(0, stake, shown.transform.position);
                mult = 1f; fair = 1.0; streak = 0;
                busy = false; Buttons();
            }
        }

        void CashOut()
        {
            if (!playing || busy || streak == 0) return;
            playing = false;
            Status("CASHED OUT " + mult.ToString("0.00") + "x", Palette.Gold);
            Say("cashout");
            PayDirect((long)(stake * (double)mult), stake, W(new Vector3(0, 1.6f, 0.4f)));
            mult = 1f; fair = 1.0; streak = 0;
            Buttons();
        }
    }
}
