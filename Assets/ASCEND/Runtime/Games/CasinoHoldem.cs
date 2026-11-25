using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Casino Hold'em against the dealer: ANTE → 2 cards each + flop → CALL (2× ante) or FOLD → turn, river,
    /// dealer qualifies with a pair of 4s. House edge ~2.2% on the ante.</summary>
    public class CasinoHoldem : CardTable
    {
        static readonly string[] HandNames = { "HIGH CARD", "PAIR", "TWO PAIR", "THREE OF A KIND", "STRAIGHT", "FLUSH", "FULL HOUSE", "FOUR OF A KIND", "STRAIGHT FLUSH", "ROYAL FLUSH" };
        static readonly string[] HandVoice = { "high", "pair", "two_pair", "trips", "straight", "flush", "full", "quads", "sflush", "royal" };
        static readonly float[] AntePays = { 1, 1, 1, 1, 1, 2, 3, 10, 20, 100 };   // ante pays X:1 when the player wins

        BetSpot ante, call;
        PokeButton dealBtn, callBtn, foldBtn;
        readonly int[] p = new int[2], d = new int[2], board = new int[5];
        bool deciding, inHand;
        TextMeshPro handT;

        protected override string FeltTexture { get { return "felt_holdem"; } }
        protected override string Rules { get { return "ANTE, THEN CALL 2x ANTE OR FOLD\nDEALER QUALIFIES: PAIR OF 4s+\nNO QUALIFY: ANTE PAYS, CALL PUSHES\nWIN: ANTE PAYS TABLE · CALL 1:1\nROYAL 100 · SF 20 · QUADS 10 · FULL 3 · FLUSH 2"; } }
        protected override Vector3 TrayPos { get { return new Vector3(-0.58f, TableY + 0.03f, -0.28f); } }
        protected override BetSpot DefaultSpot { get { return ante; } }

        protected override void BuildSpots()
        {
            ante = BetSpot.Create(this, "ANTE", 2f, transform, FeltLocal(0.40f, 0.66f) + Vector3.up * TableY, new Vector2(0.2f, 0.2f));
            call = BetSpot.Create(this, "CALL", 2f, transform, FeltLocal(0.60f, 0.66f) + Vector3.up * TableY, new Vector2(0.2f, 0.2f));
            spots.Add(ante); spots.Add(call);
            dealBtn = PokeButton.Create("Deal", transform, new Vector3(0.36f, TableY + 0.035f, -0.36f), Quaternion.Euler(0, 20, 0), 0.04f, Palette.Gold, "DEAL", () => StartCoroutine(Round()));
            callBtn = PokeButton.Create("Call", transform, new Vector3(0.52f, TableY + 0.035f, -0.22f), Quaternion.Euler(0, 30, 0), 0.035f, Palette.Teal, "CALL", Call);
            foldBtn = PokeButton.Create("Fold", transform, new Vector3(-0.36f, TableY + 0.035f, -0.38f), Quaternion.Euler(0, -20, 0), 0.035f, Palette.Red, "FOLD", Fold);
            handT = Txt.Glow("Hand", transform, new Vector3(0, TableY + 0.32f, -0.05f), "", 0.08f, Palette.Gold);
            onTap = Call; onWave = Fold;
            SetButtons();
        }

        protected override void ExtraPanel(RectTransform pnl, float y)
        {
            UIKit.Btn(pnl, "DEAL", 20, y, 520, 70, Palette.Gold, () => StartCoroutine(Round()), 34);
            UIKit.Btn(pnl, "CALL 2x", 20, y + 80, 250, 80, Palette.Teal, Call, 34);
            UIKit.Btn(pnl, "FOLD", 290, y + 80, 250, 80, Palette.Red, Fold, 34);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 400); } }

        public override void BetsChanged()
        {
            base.BetsChanged();
            if (!running && call != null && call.Total > 0) call.Refund();   // the call is placed by the CALL action only
        }

        /// <summary>The call is placed by the CALL action only.</summary>
        public override bool CanBetOn(BetSpot s) { return s != call; }

        protected override bool Resumable { get { return deciding; } }

        protected override void OnRoundAborted()
        {
            deciding = false; inHand = false;
            ClearCardsNow();
            handT.text = "";
            SetButtons();
        }

        void SetButtons()
        {
            dealBtn.SetInteractable(!inHand);
            callBtn.SetInteractable(deciding);
            foldBtn.SetInteractable(deciding);
        }

        Vector3 BoardPos(int i) { return new Vector3(-0.2f + i * 0.1f, 0, 0.14f); }

        IEnumerator Round()
        {
            if (inHand) yield break;
            if (call.Total > 0) call.Refund();
            if (!BeginRound()) yield break;
            inHand = true; SetButtons();
            yield return StartCoroutine(ClearCards());
            handT.text = "";
            for (int i = 0; i < 2; i++) p[i] = shoe.Draw();
            for (int i = 0; i < 2; i++) d[i] = shoe.Draw();
            for (int i = 0; i < 5; i++) board[i] = shoe.Draw();
            Status("GOOD LUCK", Palette.Text);
            yield return StartCoroutine(Deal(p[0], new Vector3(-0.05f, 0, -0.04f), true));
            yield return StartCoroutine(Deal(d[0], new Vector3(-0.05f, 0, 0.3f), false));
            yield return StartCoroutine(Deal(p[1], new Vector3(0.05f, 0.0015f, -0.04f), true));
            yield return StartCoroutine(Deal(d[1], new Vector3(0.05f, 0.0015f, 0.3f), false));
            for (int i = 0; i < 3; i++) yield return StartCoroutine(Deal(board[i], BoardPos(i), true));
            var cur = new List<int> { p[0], p[1], board[0], board[1], board[2] };
            handT.text = HandNames[Best(cur).cat];
            deciding = true; SetButtons();
            Status("CALL OR FOLD?", Palette.Text);
            Say("call_fold");
        }

        void Call()
        {
            if (!deciding) return;
            if (!call.AddLocked(ante.Total * 2, W(TrayPos) + Vector3.up * 0.05f)) return;
            deciding = false; SetButtons();
            StartCoroutine(Showdown());
        }

        void Fold()
        {
            if (!deciding) return;
            deciding = false; SetButtons();
            StartCoroutine(FoldRoutine());
        }

        IEnumerator FoldRoutine()
        {
            Status("FOLDED", Palette.Muted);
            yield return StartCoroutine(Settle(s => 0f));
            inHand = false; SetButtons();
            yield return new WaitForSeconds(1f);
            if (!inHand) Status("PLACE YOUR ANTE", Palette.Text);
        }

        IEnumerator Showdown()
        {
            for (int i = 3; i < 5; i++) yield return StartCoroutine(Deal(board[i], BoardPos(i), true));
            yield return StartCoroutine(onTable[1].Flip());
            yield return StartCoroutine(onTable[3].Flip());
            var ph = Best(new List<int> { p[0], p[1], board[0], board[1], board[2], board[3], board[4] });
            var dh = Best(new List<int> { d[0], d[1], board[0], board[1], board[2], board[3], board[4] });
            handT.text = "YOU: " + HandNames[ph.cat] + "\nDEALER: " + HandNames[dh.cat];
            Say(HandVoice[ph.cat]);
            yield return new WaitForSeconds(1.0f);
            bool qualifies = dh.cat >= 2 || (dh.cat == 1 && dh.ranks[0] >= 2);   // pair of 4s (rank index 2) or better
            float anteMult, callMult;
            if (!qualifies)
            {
                Status("DEALER DOES NOT QUALIFY", Palette.Gold); Say("no_qualify");
                anteMult = 1f + AntePays[ph.cat]; callMult = 1f;
            }
            else
            {
                Say("qualifies");
                int cmp = Compare(ph, dh);
                if (cmp > 0) { Status("YOU WIN · " + HandNames[ph.cat], Palette.Mint); anteMult = 1f + AntePays[ph.cat]; callMult = 2f; }
                else if (cmp == 0) { Status("PUSH", Palette.Text); anteMult = 1f; callMult = 1f; }
                else { Status("DEALER WINS · " + HandNames[dh.cat], Palette.Red); anteMult = 0f; callMult = 0f; }
            }
            if (ph.cat >= 5) GameState.I.Unlock("CARD SHARK", "A FLUSH OR BETTER AT CASINO HOLD'EM", 300);
            yield return new WaitForSeconds(0.8f);
            yield return StartCoroutine(Settle(s => s == ante ? anteMult : callMult));
            inHand = false; SetButtons();
            yield return new WaitForSeconds(1f);
            if (!inHand) Status("PLACE YOUR ANTE", Palette.Text);
        }

        // ------------------------------------------------------------ hand evaluation (best 5 of 7)
        public struct HandValue { public int cat; public int[] ranks; }

        public static HandValue Best(List<int> cards)
        {
            HandValue best = new HandValue { cat = -1, ranks = new int[0] };
            int n = cards.Count;
            var five = new int[5];
            for (int a = 0; a < n; a++) for (int b = a + 1; b < n; b++) for (int c = b + 1; c < n; c++) for (int d = c + 1; d < n; d++) for (int e = d + 1; e < n; e++)
            {
                five[0] = cards[a]; five[1] = cards[b]; five[2] = cards[c]; five[3] = cards[d]; five[4] = cards[e];
                var v = Eval5(five);
                if (best.cat < 0 || Compare(v, best) > 0) best = v;
            }
            if (n < 5) best = Eval5(cards.ToArray());
            return best;
        }

        public static int Compare(HandValue x, HandValue y)
        {
            if (x.cat != y.cat) return x.cat.CompareTo(y.cat);
            for (int i = 0; i < Mathf.Min(x.ranks.Length, y.ranks.Length); i++) if (x.ranks[i] != y.ranks[i]) return x.ranks[i].CompareTo(y.ranks[i]);
            return 0;
        }

        static HandValue Eval5(int[] cs)
        {
            var counts = new int[13]; bool flush = cs.Length == 5; int suit = cs[0] / 13;
            foreach (var c in cs) { counts[c % 13]++; if (c / 13 != suit) flush = false; }
            // groups sorted by (count desc, rank desc)
            var groups = new List<int[]>();
            for (int r = 12; r >= 0; r--) if (counts[r] > 0) groups.Add(new[] { counts[r], r });
            groups.Sort((g1, g2) => g1[0] != g2[0] ? g2[0].CompareTo(g1[0]) : g2[1].CompareTo(g1[1]));
            var ranks = new List<int>(); foreach (var g in groups) ranks.Add(g[1]);
            bool straight = false; int top = -1;
            if (groups.Count == 5)
            {
                if (ranks[0] - ranks[4] == 4) { straight = true; top = ranks[0]; }
                else if (ranks[0] == 12 && ranks[1] == 3) { straight = true; top = 3; }
            }
            int cat;
            if (straight && flush) cat = top == 12 ? 9 : 8;
            else if (groups[0][0] == 4) cat = 7;
            else if (groups[0][0] == 3 && groups.Count > 1 && groups[1][0] == 2) cat = 6;
            else if (flush) cat = 5;
            else if (straight) cat = 4;
            else if (groups[0][0] == 3) cat = 3;
            else if (groups[0][0] == 2 && groups.Count > 1 && groups[1][0] == 2) cat = 2;
            else if (groups[0][0] == 2) cat = 1;
            else cat = 0;
            return new HandValue { cat = cat, ranks = straight ? new[] { top } : ranks.ToArray() };
        }
    }
}
