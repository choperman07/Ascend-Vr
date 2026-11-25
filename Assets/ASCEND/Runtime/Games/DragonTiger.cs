using System.Collections;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Dragon Tiger: one card to the Dragon, one to the Tiger, the higher card wins (ace low). Dragon/Tiger pay
    /// 2x; on a tie they lose half. Tie pays 12x, Suited Tie 51x. 8 decks. Fast, loud and very popular in Asia.</summary>
    public class DragonTiger : CardTable
    {
        BetSpot dragon, tiger, tie, suited;
        PokeButton dealBtn;
        TextMeshPro dT, tT;
        protected override int Decks { get { return 8; } }
        protected override string FeltTexture { get { return "felt_dragontiger"; } }
        public override bool AutoHidePanel { get { return true; } }
        protected override string Rules { get { return "DRAGON 2x · TIGER 2x (TIE: HALF BACK)\nTIE 12x · SUITED TIE 51x\nHIGHER CARD WINS · ACE IS LOW\n8 DECKS"; } }
        protected override Vector3 TrayPos { get { return new Vector3(-0.58f, TableY + 0.03f, -0.28f); } }
        protected override BetSpot DefaultSpot { get { return dragon; } }

        protected override void BuildSpots()
        {
            // = the rings printed by v4gen/felts4.py (0.225 x 0.18 m each; TIE and SUITED are 0.19 m apart, so their cells stop
            // just short of each other)
            var ring = new Vector2(0.22f, 0.17f);
            dragon = BetSpot.Create(this, "DRAGON", 2f, transform, FeltLocal(0.28f, 0.64f) + Vector3.up * TableY, ring);
            tie = BetSpot.Create(this, "TIE", 12f, transform, FeltLocal(0.5f, 0.77f) + Vector3.up * TableY, ring);
            suited = BetSpot.Create(this, "SUITED TIE", 51f, transform, FeltLocal(0.5f, 0.53f) + Vector3.up * TableY, ring);
            tiger = BetSpot.Create(this, "TIGER", 2f, transform, FeltLocal(0.72f, 0.64f) + Vector3.up * TableY, ring);
            spots.Add(dragon); spots.Add(tie); spots.Add(suited); spots.Add(tiger);
            dealBtn = PokeButton.Create("Deal", transform, new Vector3(0.62f, TableY + 0.035f, -0.12f), Quaternion.Euler(0, 40, 0), 0.04f, Palette.Gold, "DEAL", () => StartCoroutine(Round()));
            dT = Txt.Glow("D", transform, new Vector3(-0.3f, TableY + 0.25f, 0.15f), "", 0.1f, Palette.Red);
            tT = Txt.Glow("T", transform, new Vector3(0.3f, TableY + 0.25f, 0.15f), "", 0.1f, Palette.Gold);
            Txt.Make("DL", transform, new Vector3(-0.3f, TableY + 0.15f, 0.15f), "DRAGON", 0.045f, Palette.Red, Palette.Display);
            Txt.Make("TL", transform, new Vector3(0.3f, TableY + 0.15f, 0.15f), "TIGER", 0.045f, Palette.Gold, Palette.Display);
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Label(p, "QUICK BETS GO ON DRAGON · DROP CHIPS ON ANY SPOT", 20, y, 520, 30, 18, Palette.Muted, Palette.Mono);
            UIKit.Btn(p, "TIGER +" + minBet.ToString("N0"), 20, y + 36, 250, 60, Palette.Gold, () => SideBet(tiger), 26);
            UIKit.Btn(p, "TIE +" + minBet.ToString("N0"), 290, y + 36, 250, 60, Palette.Mint, () => SideBet(tie), 26);
            UIKit.Btn(p, "DEAL", 20, y + 106, 520, 80, Palette.Gold, () => StartCoroutine(Round()), 40);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 420); } }

        protected override void OnRoundAborted()
        {
            ClearCardsNow();
            if (dT != null) dT.text = tT.text = "";
            if (dealBtn != null) dealBtn.SetInteractable(true);
        }

        void SideBet(BetSpot s)
        {
            if (!AcceptingBets) { GameState.I.Notify("NO MORE BETS · WAIT FOR THE NEXT ROUND", Palette.Gold); return; }
            s.AddValue(minBet, W(TrayPos) + Vector3.up * 0.05f);
        }

        static int Val(int card) { int r = card % 13; return r == 12 ? 1 : r + 2; }   // ace low

        IEnumerator Round()
        {
            if (!BeginRound()) yield break;
            dealBtn.SetInteractable(false);
            yield return StartCoroutine(ClearCards());
            dT.text = tT.text = "";
            Say("no_more");
            int d = shoe.Draw(), t = shoe.Draw();
            yield return StartCoroutine(Deal(d, new Vector3(-0.3f, 0, 0.02f), false));
            var dc = onTable[onTable.Count - 1];
            yield return StartCoroutine(Deal(t, new Vector3(0.3f, 0, 0.02f), false));
            var tc = onTable[onTable.Count - 1];
            yield return new WaitForSeconds(0.4f);
            yield return StartCoroutine(dc.Flip(0.6f)); dT.text = Val(d).ToString();
            yield return new WaitForSeconds(0.25f);
            yield return StartCoroutine(tc.Flip(0.6f)); tT.text = Val(t).ToString();
            int dv = Val(d), tv = Val(t);
            bool isTie = dv == tv, isSuited = isTie && (d / 13) % 4 == (t / 13) % 4;
            if (isTie) { Status(isSuited ? "SUITED TIE!" : "TIE · " + dv, Palette.Mint); Say("tie"); }
            else if (dv > tv) { Status("DRAGON WINS " + dv + "–" + tv, Palette.Red); Say("dt_dragon"); Juice.Burst("chips", W(new Vector3(-0.3f, TableY + 0.2f, 0)), 10); }
            else { Status("TIGER WINS " + tv + "–" + dv, Palette.Gold); Say("dt_tiger"); Juice.Burst("chips", W(new Vector3(0.3f, TableY + 0.2f, 0)), 10); }
            yield return new WaitForSeconds(0.9f);
            yield return StartCoroutine(Settle(s =>
            {
                if (s == tie) return isTie ? 12f : 0f;
                if (s == suited) return isSuited ? 51f : 0f;
                if (isTie) return 0.5f;
                if (s == dragon) return dv > tv ? 2f : 0f;
                return tv > dv ? 2f : 0f;
            }));
            dealBtn.SetInteractable(true);
            yield return new WaitForSeconds(0.8f);
            if (!running) Status("PLACE YOUR BETS", Palette.Text);
        }
    }
}
