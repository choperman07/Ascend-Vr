using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Punto banco with the standard third-card rules and a slow card squeeze. Player 2x, Banker 1.95x,
    /// Tie 9x (Player/Banker bets push on a tie).</summary>
    public class Baccarat : CardTable
    {
        BetSpot playerSpot, bankerSpot, tieSpot;
        PokeButton dealBtn;
        TextMeshPro pTot, bTot;
        protected override int Decks { get { return 8; } }
        protected override string FeltTexture { get { return "felt_baccarat"; } }
        public override bool AutoHidePanel { get { return true; } }
        protected override string Rules { get { return "PLAYER PAYS 2x\nBANKER PAYS 1.95x\nTIE PAYS 9x\nTIE RETURNS PLAYER/BANKER BETS"; } }
        protected override Vector3 TrayPos { get { return new Vector3(-0.58f, TableY + 0.03f, -0.28f); } }
        protected override BetSpot DefaultSpot { get { return bankerSpot; } }

        protected override void BuildSpots()
        {
            // = the rings printed by v4gen/felts4.py (230 px radius on a 4096 x 2048 print over 2.0 x 0.8 m: 0.225 x 0.18 m);
            // TIE sits at 0.67 so its ring clears the ASCEND mark below it
            var ring = new Vector2(0.22f, 0.17f);
            playerSpot = BetSpot.Create(this, "PLAYER", 2f, transform, FeltLocal(0.30f, 0.64f) + Vector3.up * TableY, ring);
            tieSpot = BetSpot.Create(this, "TIE", 9f, transform, FeltLocal(0.50f, 0.67f) + Vector3.up * TableY, ring);
            bankerSpot = BetSpot.Create(this, "BANKER", 1.95f, transform, FeltLocal(0.70f, 0.64f) + Vector3.up * TableY, ring);
            spots.Add(playerSpot); spots.Add(tieSpot); spots.Add(bankerSpot);
            dealBtn = PokeButton.Create("Deal", transform, new Vector3(0.62f, TableY + 0.035f, -0.12f), Quaternion.Euler(0, 40, 0), 0.04f, Palette.Gold, "DEAL", () => StartCoroutine(Round()));
            pTot = Txt.Glow("P", transform, new Vector3(-0.35f, TableY + 0.25f, 0.12f), "", 0.1f, Palette.Teal);
            bTot = Txt.Glow("B", transform, new Vector3(0.35f, TableY + 0.25f, 0.12f), "", 0.1f, Palette.Magenta);
            Txt.Make("PL", transform, new Vector3(-0.35f, TableY + 0.15f, 0.12f), "PLAYER", 0.04f, Palette.Teal, Palette.Display);
            Txt.Make("BL", transform, new Vector3(0.35f, TableY + 0.15f, 0.12f), "BANKER", 0.04f, Palette.Magenta, Palette.Display);
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            // the quick bets only reach BANKER: without these the PLAYER and TIE bets were unreachable from the panel
            UIKit.Label(p, "QUICK BETS GO ON BANKER · DROP CHIPS ON ANY SPOT", 20, y, 520, 30, 18, Palette.Muted, Palette.Mono);
            UIKit.Btn(p, "PLAYER +" + minBet.ToString("N0"), 20, y + 36, 250, 60, Palette.Teal, () => SideBet(playerSpot), 26);
            UIKit.Btn(p, "TIE +" + minBet.ToString("N0"), 290, y + 36, 250, 60, Palette.Gold, () => SideBet(tieSpot), 26);
            UIKit.Btn(p, "DEAL", 20, y + 106, 520, 80, Palette.Gold, () => StartCoroutine(Round()), 40);
        }

        void SideBet(BetSpot s)
        {
            if (!AcceptingBets) { GameState.I.Notify("NO MORE BETS · WAIT FOR THE NEXT ROUND", Palette.Gold); return; }
            s.AddValue(minBet, W(TrayPos) + Vector3.up * 0.05f);
        }

        protected override void OnRoundAborted()
        {
            ClearCardsNow();
            if (pTot != null) pTot.text = bTot.text = "";
            if (dealBtn != null) dealBtn.SetInteractable(true);
        }

        static int Val(int card) { int r = card % 13; return r == 12 ? 1 : (r >= 8 ? 0 : r + 2); }
        static int Tot(List<int> h) { int t = 0; foreach (var c in h) t += Val(c); return t % 10; }

        IEnumerator Round()
        {
            if (!BeginRound()) yield break;
            dealBtn.SetInteractable(false);
            yield return StartCoroutine(ClearCards());
            pTot.text = bTot.text = "";
            Say("no_more");
            var p = new List<int>(); var b = new List<int>();
            var pc = new List<Card3D>(); var bc = new List<Card3D>();
            for (int i = 0; i < 2; i++)
            {
                p.Add(shoe.Draw()); yield return StartCoroutine(Deal(p[i], new Vector3(-0.42f + i * 0.1f, 0, 0.02f), false)); pc.Add(onTable[onTable.Count - 1]);
                b.Add(shoe.Draw()); yield return StartCoroutine(Deal(b[i], new Vector3(0.32f + i * 0.1f, 0, 0.02f), false)); bc.Add(onTable[onTable.Count - 1]);
            }
            // the squeeze: cards turned one by one
            foreach (var c in pc) yield return StartCoroutine(c.Flip(0.55f));
            pTot.text = Tot(p).ToString();
            foreach (var c in bc) yield return StartCoroutine(c.Flip(0.55f));
            bTot.text = Tot(b).ToString();
            int pt = Tot(p), bt = Tot(b);
            if (pt >= 8 || bt >= 8) Say("natural");
            if (pt < 8 && bt < 8)
            {
                int p3 = -1;
                if (pt <= 5)
                {
                    p.Add(shoe.Draw()); yield return StartCoroutine(Deal(p[2], new Vector3(-0.21f, 0, 0.02f), true, 90));
                    p3 = Val(p[2]); pTot.text = Tot(p).ToString();
                }
                bool bankerDraws;
                if (p3 < 0) bankerDraws = bt <= 5;
                else if (bt <= 2) bankerDraws = true;
                else if (bt == 3) bankerDraws = p3 != 8;
                else if (bt == 4) bankerDraws = p3 >= 2 && p3 <= 7;
                else if (bt == 5) bankerDraws = p3 >= 4 && p3 <= 7;
                else if (bt == 6) bankerDraws = p3 == 6 || p3 == 7;
                else bankerDraws = false;
                if (bankerDraws) { b.Add(shoe.Draw()); yield return StartCoroutine(Deal(b[2], new Vector3(0.53f, 0, 0.02f), true, 90)); bTot.text = Tot(b).ToString(); }
            }
            pt = Tot(p); bt = Tot(b);
            int outcome = pt > bt ? 0 : bt > pt ? 1 : 2;
            Status(outcome == 0 ? "PLAYER WINS " + pt + "-" + bt : outcome == 1 ? "BANKER WINS " + bt + "-" + pt : "TIE " + pt, outcome == 0 ? Palette.Teal : outcome == 1 ? Palette.Magenta : Palette.Gold);
            Say(outcome == 0 ? "player_wins" : outcome == 1 ? "banker_wins" : "tie");
            yield return new WaitForSeconds(1f);
            yield return StartCoroutine(Settle(s =>
            {
                if (s == tieSpot) return outcome == 2 ? 9f : 0f;
                if (outcome == 2) return 1f;
                if (s == playerSpot) return outcome == 0 ? 2f : 0f;
                return outcome == 1 ? 1.95f : 0f;
            }));
            dealBtn.SetInteractable(true);
            yield return new WaitForSeconds(1f);
            if (!running) Status("PLACE YOUR BETS", Palette.Text);
        }
    }
}
