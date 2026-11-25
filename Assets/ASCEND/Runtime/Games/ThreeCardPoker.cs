using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Three Card Poker: ANTE (and optional PAIR PLUS), three cards each. PLAY (match the ante) or FOLD.
    /// Dealer qualifies with Queen-high. Ante bonus for straights and better; Pair Plus pays on its own.</summary>
    public class ThreeCardPoker : CardTable
    {
        BetSpot ante, play, pairPlus;
        PokeButton dealBtn, playBtn, foldBtn;
        TextMeshPro youT, dealerT;
        readonly List<int> you = new List<int>(), house = new List<int>();
        bool deciding, decided, folded;
        protected override int Decks { get { return 1; } }
        protected override string FeltTexture { get { return "felt_threecard"; } }
        protected override string Rules { get { return "PLAY = ANTE · DEALER QUALIFIES WITH Q-HIGH\nWIN 2x ANTE & PLAY · NO QUALIFY: ANTE 2x, PLAY PUSH\nANTE BONUS: STRAIGHT +1 · TRIPS +4 · STR FLUSH +5\nPAIR PLUS: PAIR 2x · FLUSH 4x · STRAIGHT 7x\n  TRIPS 31x · STR FLUSH 41x"; } }
        protected override Vector3 TrayPos { get { return new Vector3(-0.58f, TableY + 0.03f, -0.28f); } }
        protected override BetSpot DefaultSpot { get { return ante; } }

        protected override void BuildSpots()
        {
            pairPlus = BetSpot.Create(this, "PAIR PLUS", 41f, transform, FeltLocal(0.5f, 0.50f) + Vector3.up * TableY, new Vector2(0.16f, 0.12f));
            ante = BetSpot.Create(this, "ANTE", 2f, transform, FeltLocal(0.38f, 0.70f) + Vector3.up * TableY, new Vector2(0.16f, 0.16f));
            play = BetSpot.Create(this, "PLAY", 2f, transform, FeltLocal(0.62f, 0.70f) + Vector3.up * TableY, new Vector2(0.16f, 0.16f));
            spots.Add(pairPlus); spots.Add(ante); spots.Add(play);
            dealBtn = PokeButton.Create("Deal", transform, new Vector3(0.36f, TableY + 0.035f, -0.36f), Quaternion.Euler(0, 20, 0), 0.04f, Palette.Gold, "DEAL", () => StartCoroutine(Round()));
            playBtn = PokeButton.Create("Play", transform, new Vector3(0.52f, TableY + 0.035f, -0.24f), Quaternion.Euler(0, 30, 0), 0.035f, Palette.Mint, "PLAY", () => Decide(false));
            foldBtn = PokeButton.Create("Fold", transform, new Vector3(0.64f, TableY + 0.035f, -0.11f), Quaternion.Euler(0, 40, 0), 0.035f, Palette.Red, "FOLD", () => Decide(true));
            youT = Txt.Glow("You", transform, new Vector3(-0.38f, TableY + 0.22f, -0.02f), "", 0.06f, Palette.Teal);
            dealerT = Txt.Glow("Dealer", transform, new Vector3(-0.38f, TableY + 0.28f, 0.3f), "", 0.06f, Palette.Gold);
            onTap = () => Decide(false); onWave = () => Decide(true);
            Buttons();
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "DEAL", 20, y, 520, 70, Palette.Gold, () => StartCoroutine(Round()), 36);
            UIKit.Btn(p, "PLAY", 20, y + 80, 250, 70, Palette.Mint, () => Decide(false), 34);
            UIKit.Btn(p, "FOLD", 290, y + 80, 250, 70, Palette.Red, () => Decide(true), 34);
            UIKit.Btn(p, "+ PAIR PLUS", 20, y + 160, 520, 56, Palette.Teal, () => { if (AcceptingBets) pairPlus.AddValue(minBet, W(TrayPos) + Vector3.up * 0.05f); }, 26);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 450); } }

        void Buttons()
        {
            dealBtn.SetInteractable(!running);
            playBtn.SetInteractable(deciding); foldBtn.SetInteractable(deciding);
        }

        void Decide(bool fold)
        {
            if (!deciding) return;
            if (!fold && !play.AddLocked(ante.Total, W(TrayPos) + Vector3.up * 0.05f)) return;
            folded = fold; deciding = false; decided = true;
            Buttons();
            StartCoroutine(Resolve());
        }

        /// <summary>The play bet is placed by the PLAY action only.</summary>
        public override bool CanBetOn(BetSpot s) { return s != play; }

        // the decision wait is plain state (no coroutine), so a hand left mid-decision resumes when you come back
        protected override bool Resumable { get { return deciding; } }

        protected override void OnRoundAborted()
        {
            deciding = decided = folded = false;
            you.Clear(); house.Clear(); houseCards.Clear();
            ClearCardsNow();
            youT.text = dealerT.text = "";
            Buttons();
        }

        // ---------------------------------------------------------------- hand evaluation (rank: 5 SF, 4 trips, 3 straight, 2 flush, 1 pair, 0 high)
        public static int Category(List<int> h, out int[] key)
        {
            var r = new List<int> { h[0] % 13, h[1] % 13, h[2] % 13 }; r.Sort(); r.Reverse();
            bool flush = (h[0] / 13) % 4 == (h[1] / 13) % 4 && (h[1] / 13) % 4 == (h[2] / 13) % 4;
            bool straight = (r[0] == r[1] + 1 && r[1] == r[2] + 1);
            if (r[0] == 12 && r[1] == 1 && r[2] == 0) { straight = true; r = new List<int> { 1, 0, -1 }; }   // A-2-3 (ace low)
            bool trips = r[0] == r[1] && r[1] == r[2];
            bool pair = !trips && (r[0] == r[1] || r[1] == r[2]);
            int cat = straight && flush ? 5 : trips ? 4 : straight ? 3 : flush ? 2 : pair ? 1 : 0;
            if (pair) { int pr = r[1]; int kick = r[0] == r[1] ? r[2] : r[0]; key = new[] { pr, pr, kick }; }
            else key = r.ToArray();
            return cat;
        }

        static readonly string[] CatNames = { "HIGH CARD", "PAIR", "FLUSH", "STRAIGHT", "THREE OF A KIND", "STRAIGHT FLUSH" };

        static int Compare(List<int> a, List<int> b)
        {
            int[] ka, kb; int ca = Category(a, out ka), cb = Category(b, out kb);
            if (ca != cb) return ca.CompareTo(cb);
            for (int i = 0; i < 3; i++) if (ka[i] != kb[i]) return ka[i].CompareTo(kb[i]);
            return 0;
        }

        static float PairPlusMult(int cat) { switch (cat) { case 5: return 41f; case 4: return 31f; case 3: return 7f; case 2: return 4f; case 1: return 2f; } return 0f; }
        static float AnteBonus(int cat) { switch (cat) { case 5: return 5f; case 4: return 4f; case 3: return 1f; } return 0f; }

        readonly List<Card3D> houseCards = new List<Card3D>();
        int yc; float ppMult;

        IEnumerator Round()
        {
            if (running) yield break;
            if (ante.Total <= 0) { GameState.I.Notify(pairPlus.Total > 0 ? "PAIR PLUS NEEDS AN ANTE" : "PUT CHIPS ON ANTE", Palette.Gold); yield break; }
            if (play.Total > 0) play.Refund();
            if (!BeginRound()) yield break;
            deciding = decided = folded = false; Buttons();
            yield return StartCoroutine(ClearCards());
            youT.text = dealerT.text = "";
            you.Clear(); house.Clear();
            Say("good_luck");
            houseCards.Clear();
            for (int i = 0; i < 3; i++)
            {
                you.Add(shoe.Draw()); yield return StartCoroutine(Deal(you[i], new Vector3(-0.08f + i * 0.08f, 0, -0.02f), true));
                house.Add(shoe.Draw()); yield return StartCoroutine(Deal(house[i], new Vector3(-0.08f + i * 0.08f, 0, 0.28f), false)); houseCards.Add(onTable[onTable.Count - 1]);
            }
            int[] k; yc = Category(you, out k);
            youT.text = CatNames[yc];
            ppMult = pairPlus.Total > 0 ? PairPlusMult(yc) : 0f;
            if (ppMult > 0) { Status("PAIR PLUS · " + CatNames[yc] + " · " + ppMult + "x", Palette.Gold); Sfx.Play("win_small", pairPlus.transform.position, 0.8f); yield return new WaitForSeconds(1f); }
            deciding = true; Buttons();
            Status("PLAY OR FOLD?", Palette.Text);
            Say("call_fold");
        }

        IEnumerator Resolve()
        {
            int[] k;
            foreach (var c in houseCards) if (c != null) yield return StartCoroutine(c.Flip(0.35f));
            int dc = Category(house, out k);
            dealerT.text = CatNames[dc];
            bool qualifies = dc > 0 || house.Exists(c => c % 13 >= 10);   // Q-high or better
            float anteM, playM;
            if (folded) { anteM = 0f; playM = 0f; Status("FOLDED", Palette.Muted); }
            else if (!qualifies) { anteM = 2f; playM = 1f; Status("DEALER DOESN'T QUALIFY · ANTE WINS", Palette.Mint); Say("no_qualify"); }
            else
            {
                int cmp = Compare(you, house);
                anteM = cmp > 0 ? 2f : cmp == 0 ? 1f : 0f; playM = anteM;
                Status(cmp > 0 ? "YOU WIN · " + CatNames[yc] : cmp == 0 ? "PUSH" : "DEALER WINS · " + CatNames[dc], cmp > 0 ? Palette.Mint : cmp == 0 ? Palette.Text : Palette.Red);
                Say("qualifies");
            }
            if (!folded) anteM += AnteBonus(yc);
            yield return new WaitForSeconds(1f);
            yield return StartCoroutine(Settle(s => s == ante ? anteM : s == play ? playM : s == pairPlus ? ppMult : 0f));
            deciding = decided = false; Buttons();
            yield return new WaitForSeconds(1f);
            if (!running) Status("PLACE YOUR BETS", Palette.Text);
        }
    }
}
