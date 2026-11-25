using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Blackjack with a dealer: chips on BET, press DEAL. Tap the felt to HIT, wave your hand to STAND
    /// (or use the buttons). Dealer hits soft 17, blackjack pays 6:5, double on any two cards.</summary>
    public class Blackjack : CardTable
    {
        readonly List<int> player = new List<int>(), dealerHand = new List<int>();
        PokeButton dealBtn, hitBtn, standBtn, doubleBtn;
        TextMeshPro playerTotalT, dealerTotalT;
        BetSpot bet, ppSpot, t3Spot, insSpot;
        PokeButton insBtn;
        float ppMult, t3Mult, insMult;
        bool inHand, acting, doubled, insuranceOpen;

        protected override string FeltTexture { get { return requiredTier >= Tier.Legend ? "felt_highroller" : "felt_blackjack"; } }
        protected override string Rules { get { return "WIN 2x · BLACKJACK PAYS 6:5 (2.2x) · INSURANCE 3x\nDEALER HITS SOFT 17 · 6 DECKS\nPERFECT PAIRS: MIXED 7x · COLOUR 13x · PERFECT 26x\n21+3: FLUSH 6x · STRAIGHT 11x · TRIPS 31x\n  STRAIGHT FLUSH 41x · SUITED TRIPS 101x\nTAP FELT = HIT · WAVE = STAND"; } }
        protected override Vector3 TrayPos { get { return new Vector3(-0.58f, TableY + 0.03f, -0.28f); } }
        protected override BetSpot DefaultSpot { get { return bet; } }

        protected override void BuildSpots()
        {
            bet = BetSpot.Create(this, "BET", 2f, transform, FeltLocal(0.5f, 0.66f) + Vector3.up * TableY, new Vector2(0.2f, 0.2f));
            spots.Add(bet);
            ppSpot = BetSpot.Create(this, "PERFECT PAIRS", 26f, transform, FeltLocal(0.33f, 0.70f) + Vector3.up * TableY, new Vector2(0.16f, 0.16f));
            t3Spot = BetSpot.Create(this, "21+3", 101f, transform, FeltLocal(0.67f, 0.70f) + Vector3.up * TableY, new Vector2(0.16f, 0.16f));
            insSpot = BetSpot.Create(this, "INSURANCE", 3f, transform, FeltLocal(0.5f, 0.46f) + Vector3.up * TableY, new Vector2(0.3f, 0.08f));
            spots.Add(ppSpot); spots.Add(t3Spot); spots.Add(insSpot);
            insBtn = PokeButton.Create("Insurance", transform, new Vector3(-0.5f, TableY + 0.035f, -0.25f), Quaternion.Euler(0, -30, 0), 0.032f, Palette.Violet, "INSURE", Insure);
            dealBtn = PokeButton.Create("Deal", transform, new Vector3(0.36f, TableY + 0.035f, -0.36f), Quaternion.Euler(0, 20, 0), 0.04f, Palette.Gold, "DEAL", () => StartCoroutine(DealRound()));
            hitBtn = PokeButton.Create("Hit", transform, new Vector3(0.5f, TableY + 0.035f, -0.25f), Quaternion.Euler(0, 30, 0), 0.035f, Palette.Mint, "HIT", Hit);
            standBtn = PokeButton.Create("Stand", transform, new Vector3(0.62f, TableY + 0.035f, -0.12f), Quaternion.Euler(0, 40, 0), 0.035f, Palette.Red, "STAND", Stand);
            doubleBtn = PokeButton.Create("Double", transform, new Vector3(-0.36f, TableY + 0.035f, -0.38f), Quaternion.Euler(0, -20, 0), 0.035f, Palette.Magenta, "DOUBLE", Double);
            playerTotalT = Txt.Glow("PlayerTotal", transform, new Vector3(0.3f, TableY + 0.25f, 0.0f), "", 0.09f, Palette.Teal);
            dealerTotalT = Txt.Glow("DealerTotal", transform, new Vector3(0.3f, TableY + 0.3f, 0.3f), "", 0.09f, Palette.Gold);
            onTap = Hit; onWave = Stand;
            SetButtons();
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "DEAL", 20, y, 250, 80, Palette.Gold, () => StartCoroutine(DealRound()), 36);
            UIKit.Btn(p, "HIT", 290, y, 250, 80, Palette.Mint, Hit, 36);
            UIKit.Btn(p, "STAND", 20, y + 90, 250, 80, Palette.Red, Stand, 36);
            UIKit.Btn(p, "DOUBLE", 290, y + 90, 250, 80, Palette.Magenta, Double, 36);
            UIKit.Btn(p, "+ PAIRS", 20, y + 180, 170, 60, Palette.Teal, () => SideBet(ppSpot), 24);
            UIKit.Btn(p, "+ 21+3", 200, y + 180, 160, 60, Palette.Teal, () => SideBet(t3Spot), 24);
            UIKit.Btn(p, "INSURE", 370, y + 180, 170, 60, Palette.Violet, Insure, 24);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 470); } }

        /// <summary>Insurance is only offered by the dealer (INSURE button); chips dropped there before the deal were refunded anyway.</summary>
        public override bool CanBetOn(BetSpot s) { return s != insSpot; }

        protected override bool Resumable { get { return inHand && !acting; } }

        protected override void OnRoundAborted()
        {
            inHand = false; acting = false; doubled = false; insuranceOpen = false;
            player.Clear(); dealerHand.Clear();
            ClearCardsNow();
            playerTotalT.text = dealerTotalT.text = "";
            SetButtons();
        }

        void SideBet(BetSpot s)
        {
            if (!AcceptingBets) return;
            s.AddValue(minBet, W(TrayPos) + Vector3.up * 0.05f);
        }

        void Insure()
        {
            if (!insuranceOpen || insSpot.Total > 0) return;
            long half = bet.Total / 2;
            if (half <= 0 || !insSpot.AddLocked(half, W(TrayPos) + Vector3.up * 0.05f)) return;
            Status("INSURED " + half.ToString("N0"), Palette.Violet);
        }

        static int Rank(int c) { return c % 13; }
        static int Suit(int c) { return (c / 13) % 4; }
        static bool Red(int c) { int s = Suit(c); return s == 1 || s == 2; }

        static float PerfectPairs(int a, int b)
        {
            if (Rank(a) != Rank(b)) return 0f;
            if (Suit(a) == Suit(b)) return 26f;
            if (Red(a) == Red(b)) return 13f;
            return 7f;
        }

        static float TwentyOnePlus3(int a, int b, int c)
        {
            int[] r = { Rank(a), Rank(b), Rank(c) }; System.Array.Sort(r);
            bool flush = Suit(a) == Suit(b) && Suit(b) == Suit(c);
            bool trips = r[0] == r[1] && r[1] == r[2];
            bool straight = (r[1] == r[0] + 1 && r[2] == r[1] + 1) || (r[0] == 0 && r[1] == 1 && r[2] == 12);   // A-2-3
            if (trips && flush) return 101f;
            if (straight && flush) return 41f;
            if (trips) return 31f;
            if (straight) return 11f;
            if (flush) return 6f;
            return 0f;
        }

        void SetButtons()
        {
            if (insBtn != null) insBtn.SetInteractable(insuranceOpen);
            dealBtn.SetInteractable(!inHand && !running);
            hitBtn.SetInteractable(inHand && !acting);
            standBtn.SetInteractable(inHand && !acting);
            doubleBtn.SetInteractable(inHand && !acting && player.Count == 2 && !doubled);
        }

        public static int Total(List<int> hand, out bool soft)
        {
            int t = 0, aces = 0;
            foreach (int c in hand) { int r = c % 13; if (r == 12) { aces++; t += 11; } else if (r >= 8) t += 10; else t += r + 2; }
            while (t > 21 && aces > 0) { t -= 10; aces--; }
            soft = aces > 0; return t;
        }
        public static int Total(List<int> hand) { bool s; return Total(hand, out s); }

        Vector3 PlayerCardPos(int i) { return new Vector3(-0.06f + i * 0.05f, i * 0.0015f, 0.02f - i * 0.012f); }
        Vector3 DealerCardPos(int i) { return new Vector3(-0.1f + i * 0.095f, 0, 0.27f); }

        IEnumerator DealRound()
        {
            if (inHand) yield break;
            if (bet.Total <= 0 && (ppSpot.Total > 0 || t3Spot.Total > 0)) { GameState.I.Notify("SIDE BETS NEED A MAIN BET", Palette.Gold); yield break; }
            insSpot.Refund();
            if (!BeginRound()) yield break;
            inHand = true; acting = true; doubled = false; ppMult = t3Mult = insMult = 0f;
            player.Clear(); dealerHand.Clear();
            SetButtons();
            yield return StartCoroutine(ClearCards());
            Status("GOOD LUCK", Palette.Text);
            Say("good_luck");
            player.Add(shoe.Draw()); yield return StartCoroutine(Deal(player[0], PlayerCardPos(0), true));
            dealerHand.Add(shoe.Draw()); yield return StartCoroutine(Deal(dealerHand[0], DealerCardPos(0), true));
            player.Add(shoe.Draw()); yield return StartCoroutine(Deal(player[1], PlayerCardPos(1), true));
            dealerHand.Add(shoe.Draw()); yield return StartCoroutine(Deal(dealerHand[1], DealerCardPos(1), false));
            UpdateTotals(true);
            // side bets resolve on the first cards
            if (ppSpot.Total > 0) { ppMult = PerfectPairs(player[0], player[1]); if (ppMult > 0) { Status((ppMult >= 26 ? "PERFECT PAIR · " : ppMult >= 13 ? "COLOURED PAIR · " : "MIXED PAIR · ") + ppMult + "x", Palette.Gold); Sfx.Play("win_small", W(ppSpot.transform.localPosition), 0.8f); yield return new WaitForSeconds(0.9f); } }
            if (t3Spot.Total > 0) { t3Mult = TwentyOnePlus3(player[0], player[1], dealerHand[0]); if (t3Mult > 0) { Status("21+3 · " + t3Mult + "x", Palette.Gold); Sfx.Play("win_small", W(t3Spot.transform.localPosition), 0.8f); yield return new WaitForSeconds(0.9f); } }
            if (Rank(dealerHand[0]) == 12 && bet.Total > 0)
            {
                insuranceOpen = true;
                insBtn.SetInteractable(true);
                Status("INSURANCE? · PAYS 2 TO 1", Palette.Violet);
                Say("insurance");
                for (float w = 0; w < 4.5f && insSpot.Total == 0; w += Time.deltaTime) yield return null;
                insuranceOpen = false;
                insBtn.SetInteractable(false);
            }
            bool pbj = Total(player) == 21, dbj = Total(dealerHand) == 21;
            if (insSpot.Total > 0) insMult = dbj ? 3f : 0f;
            if (pbj || dbj)
            {
                yield return StartCoroutine(onTable[3].Flip());
                UpdateTotals(false);
                if (pbj && dbj) { Status("PUSH · BOTH BLACKJACK", Palette.Text); yield return Finish(1f); }
                else if (pbj) { Status("BLACKJACK!", Palette.Gold); Say("blackjack"); yield return Finish(2.2f); }
                else { Status("DEALER BLACKJACK", Palette.Red); Say("blackjack"); yield return Finish(0f); }
                yield break;
            }
            acting = false;
            Status("HIT OR STAND?", Palette.Text);
            Say("hit_stand");
            SetButtons();
        }

        void UpdateTotals(bool hideHole)
        {
            playerTotalT.text = player.Count > 0 ? Total(player).ToString() : "";
            if (dealerHand.Count == 0) dealerTotalT.text = "";
            else dealerTotalT.text = hideHole ? Total(new List<int> { dealerHand[0] }).ToString() : Total(dealerHand).ToString();
        }

        void Hit() { if (inHand && !acting) StartCoroutine(HitRoutine()); }
        IEnumerator HitRoutine()
        {
            acting = true; SetButtons();
            player.Add(shoe.Draw());
            yield return StartCoroutine(Deal(player[player.Count - 1], PlayerCardPos(player.Count - 1), true));
            UpdateTotals(true);
            int t = Total(player);
            if (t > 21) { Status("BUST · " + t, Palette.Red); Say("bust"); yield return StartCoroutine(onTable[3].Flip()); UpdateTotals(false); yield return Finish(0f); yield break; }
            if (t == 21 || doubled) { yield return StartCoroutine(DealerTurn()); yield break; }
            acting = false; SetButtons();
        }

        void Stand() { if (inHand && !acting) StartCoroutine(DealerTurn()); }

        void Double()
        {
            if (!inHand || acting || player.Count != 2 || doubled) return;
            if (!bet.AddLocked(bet.Total, W(TrayPos) + Vector3.up * 0.05f)) return;
            doubled = true;
            StartCoroutine(HitRoutine());
        }

        IEnumerator DealerTurn()
        {
            acting = true; SetButtons();
            Status("DEALER'S TURN", Palette.Gold);
            yield return StartCoroutine(onTable[3].Flip());
            UpdateTotals(false);
            yield return new WaitForSeconds(0.5f);
            while (true)
            {
                bool soft; int dt = Total(dealerHand, out soft);
                if (dt > 17 || (dt == 17 && !soft)) break;   // dealer hits soft 17
                dealerHand.Add(shoe.Draw());
                yield return StartCoroutine(Deal(dealerHand[dealerHand.Count - 1], DealerCardPos(dealerHand.Count - 1), true));
                UpdateTotals(false);
                yield return new WaitForSeconds(0.35f);
            }
            int p = Total(player), d = Total(dealerHand);
            if (d >= 12 && d <= 21) Say("has" + d);
            yield return new WaitForSeconds(0.6f);
            if (d > 21) { Status("DEALER BUSTS · YOU WIN", Palette.Mint); Say("dealer_bust"); yield return Finish(2f); }
            else if (p > d) { Status(p + " BEATS " + d, Palette.Mint); yield return Finish(2f); }
            else if (p == d) { Status("PUSH · " + p, Palette.Text); yield return Finish(1f); }
            else { Status(d + " BEATS " + p, Palette.Red); yield return Finish(0f); }
        }

        Coroutine Finish(float mult)
        {
            return StartCoroutine(FinishRoutine(mult));
        }

        IEnumerator FinishRoutine(float mult)
        {
            yield return StartCoroutine(Settle(s => s == bet ? mult : s == ppSpot ? ppMult : s == t3Spot ? t3Mult : s == insSpot ? insMult : 0f));
            inHand = false; acting = false;
            SetButtons();
            yield return new WaitForSeconds(1.2f);
            if (!inHand) Status("PLACE YOUR BETS", Palette.Text);
        }

        /// <summary>Trailer: four cards out of the shoe, no chips involved.</summary>
        public void DemoDeal() { if (isActiveAndEnabled) StartCoroutine(DemoRoutine()); }
        IEnumerator DemoRoutine()
        {
            yield return StartCoroutine(ClearCards());
            for (int i = 0; i < 4; i++)
                yield return StartCoroutine(Deal(shoe.Draw(), i % 2 == 0 ? PlayerCardPos(i / 2) : DealerCardPos(i / 2), i < 3));
        }
    }
}
