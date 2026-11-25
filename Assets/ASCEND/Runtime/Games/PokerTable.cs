using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>TEXAS HOLD'EM against four seated bots (player vs player, not vs the house): blinds, pre-flop / flop /
    /// turn / river betting with FOLD · CHECK/CALL · RAISE · POT · ALL-IN, a dealer croupier who deals and pushes the pot,
    /// side pots and a showdown where every hand is turned face up. Bots play from their real hand strength (Monte Carlo
    /// equity against the players still in) with their own personalities. The house only takes a 2% rake from each pot.</summary>
    public class PokerTable : CardTable
    {
        public long smallBlind = 100;
        const float Rake = 0.02f;
        static readonly string[] BotNames = { "ACE_VEGA", "RIVER_RAT", "NUT_FLUSH", "BLUFF.EXE", "QUEEN_K", "TILT_TOMMY", "SHARK_SOL", "COLD_DECK" };

        class P
        {
            public string name; public bool human; public long stack, bet, total; public bool folded, allIn, acted;
            public List<int> hole = new List<int>(); public Seat seat; public AvatarBody body; public float aggro, loose;
            public TextMeshPro label; public Transform chips; public Vector3 cardPos; public List<Card3D> cards = new List<Card3D>();
        }

        readonly List<P> players = new List<P>();
        P me;
        readonly List<int> board = new List<int>();
        List<int> deck = new List<int>();
        int button;
        long pot, currentBet, minRaise;
        bool handRunning, waitingForMe;
        string myAction; long myRaiseTo;
        TextMeshPro potT, handT;
        TextMeshProUGUI callLabel, infoLabel;
        Transform potChips;

        protected override int Decks { get { return 1; } }
        protected override string FeltTexture { get { return "felt_poker"; } }
        protected override string Rules
        {
            get
            {
return "YOU VS 4 PLAYERS, BLINDS " + smallBlind.ToString("N0") + " / " + (smallBlind * 2).ToString("N0") + "\n" +
                       "TWO CARDS EACH + FIVE ON THE BOARD: THE BEST FIVE-CARD HAND WINS\n" +
                       "ACT FROM THE PANEL: FOLD, CHECK / CALL, RAISE, POT OR ALL-IN\n" +
                       "YOUR CHIPS ARE YOUR STACK · THE HOUSE TAKES 2% OF EACH POT\n" +
                       "PRESS DEAL TO START A HAND";
            }
        }
        protected override bool HasSeats { get { return true; } }
        protected override Vector2 PanelSize { get { return new Vector2(560, 330); } }

        protected override void BuildSpots()
        {
            potT = Txt.Glow("Pot", transform, new Vector3(0, TableY + 0.3f, 0.32f), "", 0.08f, Palette.Gold);
            potT.gameObject.AddComponent<Billboard>();
            handT = Txt.Make("Hand", transform, new Vector3(0, TableY + 0.2f, -0.2f), "", 0.045f, Palette.Mint, Palette.Display);
            handT.gameObject.AddComponent<Billboard>();
            potChips = Build.Empty("PotChips", transform, new Vector3(0, TableY, 0.36f)).transform;
            PokeButton.Create("Deal", transform, new Vector3(0.75f, TableY + 0.035f, -0.05f), Quaternion.Euler(0, 40, 0), 0.04f, Palette.Gold, "DEAL", StartHand);
        }

        protected override bool QuickBets { get { return false; } }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            infoLabel = UIKit.Label(p, "PRESS DEAL TO START A HAND", 20, y, 520, 34, 22, Palette.Muted, Palette.Mono);
            UIKit.Btn(p, "DEAL", 20, y + 40, 520, 60, Palette.Gold, StartHand, 32);
            UIKit.Btn(p, "FOLD", 20, y + 108, 160, 72, Palette.Red, () => Act("fold"), 30);
            var cb = UIKit.Btn(p, "CHECK", 190, y + 108, 350, 72, Palette.Teal, () => Act("call"), 30);
            callLabel = cb.GetComponentInChildren<TextMeshProUGUI>();
            UIKit.Btn(p, "RAISE", 20, y + 188, 160, 72, Palette.Gold, () => Act("raise"), 28);
            UIKit.Btn(p, "POT", 190, y + 188, 170, 72, Palette.Violet, () => Act("pot"), 28);
            UIKit.Btn(p, "ALL-IN", 370, y + 188, 170, 72, Palette.Magenta, () => Act("allin"), 28);
        }

        protected override void Start()
        {
            base.Start();
            // the four bot regulars take their seats
            var names = new List<string>(BotNames);
            for (int i = 0; i < seats.Count && i < 4; i++)
            {
                var st = seats[i];
                int pick = Random.Range(0, names.Count); string nm = names[pick]; names.RemoveAt(pick);
                var r = (Rarity)Random.Range(1, 6);
                var body = Avatars.Create("Poker_" + nm, transform.parent, st.transform.position, st.transform.eulerAngles.y, r, RoninRole.Player, Random.Range(1.64f, 1.84f));
                // a roaming guest may already sit on this stool (the bot director can fill seats first): ask it to leave,
                // otherwise two avatars share one stool and the poker regular stands inside it
                if (st.occupant != null)
                {
                    var guest = st.occupant.GetComponent<BotAvatar>();
                    if (guest != null) guest.Leave(); else st.Leave();
                }
                st.Take(body);
                var prof = body.gameObject.AddComponent<AvatarProfile>();
                prof.displayName = nm; prof.rarity = r; prof.xp = Odds.Range(20000, 300000);
                var pl = new P { name = nm, seat = st, body = body, stack = smallBlind * Random.Range(150, 400), aggro = Random.Range(0.15f, 0.65f), loose = Random.Range(0.1f, 0.5f) };
                prof.status = "POKER · STACK " + pl.stack.ToString("N0");
                pl.label = Txt.Glow("Label" + i, transform, transform.InverseTransformPoint(st.transform.position) + new Vector3(0, 2.05f, 0), "", 0.055f, Palette.Text);
                pl.label.gameObject.AddComponent<Billboard>();
                pl.cardPos = CardSpot(st.transform.position);
                pl.chips = Build.Empty("Chips" + i, transform, Vector3.Lerp(pl.cardPos, new Vector3(0, 0, 0.36f), 0.35f) + Vector3.up * (TableY)).transform;
                players.Add(pl);
            }
            me = new P { name = "YOU", human = true, seat = PlayerSeat };
            // your two cards sit between the chip tray and the board (the tray used to cover them)
            me.cardPos = OnTable(new Vector3(0, 0, -0.03f), 0.2f); me.cardPos.y = 0f;
            handT.transform.localPosition = me.cardPos + new Vector3(0, TableY + 0.13f, 0.06f);   // right above your own two cards
            me.chips = Build.Empty("MyChips", transform, new Vector3(0.2f, TableY, -0.04f)).transform;
            players.Insert(2, me);    // the player sits in the middle of the arc
            button = Random.Range(0, players.Count);
            RefreshLabels();
            Status("TEXAS HOLD'EM · SIT DOWN AND PRESS DEAL", Palette.Text);
        }

        Vector3 CardSpot(Vector3 seatWorld)
        {
            var l = transform.InverseTransformPoint(seatWorld);
            var toC = new Vector3(0, 0, 0.3f) - new Vector3(l.x, 0, l.z);
            var p = new Vector3(l.x, 0, l.z) + toC.normalized * Mathf.Min(0.75f, toC.magnitude * 0.55f);
            return OnTable(p, 0.22f);
        }

        void RefreshLabels()
        {
            foreach (var p in players)
            {
                if (p.human) continue;
                string state = p.folded ? "FOLDED" : p.allIn ? "ALL-IN" : "";
                p.label.text = p.name + "\n<size=70%>" + p.stack.ToString("N0") + (state.Length > 0 ? " · " + state : "") + "</size>";
                p.label.color = p.folded ? Palette.Muted : Palette.Text;
            }
            potT.text = pot > 0 ? "POT " + pot.ToString("N0") : "";
            if (callLabel != null)
            {
                long toCall = currentBet - me.bet;
                callLabel.text = toCall <= 0 ? "CHECK" : "CALL " + System.Math.Min(toCall, GameState.I.chips).ToString("N0");
            }
        }

        // ------------------------------------------------------------ player input
        void Act(string a)
        {
            if (!waitingForMe) { Sfx.UI("ui_error", 0.4f); return; }
            myAction = a;
        }

        void StartHand()
        {
            if (handRunning) return;
            if (PlayerSeating.I == null || PlayerSeating.I.Current != PlayerSeat) { GameState.I.Notify("SIT AT THE POKER TABLE TO PLAY", Palette.Gold); return; }
            if (GameState.I.chips < smallBlind * 4) { GameState.I.Notify("NOT ENOUGH CHIPS · OPEN THE MENU TO DEPOSIT", Palette.Red); return; }
            StartCoroutine(Hand());
        }

        bool PayIn(P p, long amount)
        {
            if (amount <= 0) return true;
            if (p.human)
            {
                amount = System.Math.Min(amount, GameState.I.chips);
                if (amount <= 0 || !GameState.I.Wager(amount)) return false;
                if (GameState.I.chips <= 0) p.allIn = true;
            }
            else
            {
                amount = System.Math.Min(amount, p.stack);
                p.stack -= amount;
                if (p.stack <= 0) p.allIn = true;
            }
            p.bet += amount; p.total += amount; pot += amount;
            foreach (var v in ChipKit.Split(amount, 6))
            {
                var c = ChipKit.Visual(v, p.chips, p.chips.position);
                c.transform.rotation = transform.rotation;
            }
            Tidy(p.chips);
            Sfx.Chip(p.chips.position, 0.6f);
            return true;
        }

        /// <summary>Chips in a pile sit in neat touching columns on the felt (no floating gaps).</summary>
        static void Tidy(Transform pile)
        {
            if (pile == null) return;
            int n = pile.childCount;
            for (int j = 0; j < n; j++)
            {
                var c = pile.GetChild(j);
                c.localPosition = ChipKit.StackLocal(j, n);
                c.localRotation = Quaternion.identity;
            }
        }

        IEnumerator Hand()
        {
            handRunning = true; running = true; AcceptingBets = false;
            yield return StartCoroutine(ClearCards());
            board.Clear(); pot = 0; handT.text = "";
            foreach (Transform c in potChips) Destroy(c.gameObject);
            deck = new List<int>(); for (int c = 0; c < 52; c++) deck.Add(c);
            for (int i = deck.Count - 1; i > 0; i--) { int j = Odds.Range(0, i + 1); int t = deck[i]; deck[i] = deck[j]; deck[j] = t; }
            foreach (var p in players)
            {
                p.hole.Clear(); p.cards.Clear(); p.bet = 0; p.total = 0; p.acted = false;
                p.allIn = false; p.folded = false;
                // busted bots rebuy straight away (sitting them out still dealt them the button / blinds as dead seats)
                if (!p.human && p.stack < smallBlind * 2) p.stack = smallBlind * Random.Range(150, 300);
                foreach (Transform c in p.chips) Destroy(c.gameObject);
            }
            GameState.I.Record("poker");
            button = Next(button);
            int sb = Next(button), bb = Next(sb);
            Status("BLINDS " + smallBlind.ToString("N0") + " / " + (smallBlind * 2).ToString("N0"), Palette.Gold);
            if (!PayIn(players[sb], smallBlind)) FoldOut(players[sb]);
            if (!PayIn(players[bb], smallBlind * 2)) FoldOut(players[bb]);
            currentBet = smallBlind * 2; minRaise = smallBlind * 2;
            RefreshLabels();
            Say("poker_deal");
            // deal two each, one at a time, starting left of the button
            for (int r = 0; r < 2; r++)
                for (int k = 1; k <= players.Count; k++)
                {
                    var p = players[(button + k) % players.Count];
                    if (p.folded) continue;
                    int card = Draw(); p.hole.Add(card);
                    var pos = p.cardPos + transform.InverseTransformDirection(Vector3.Cross(Vector3.up, (W(new Vector3(0, 0, 0.3f)) - W(p.cardPos)).normalized)) * (r == 0 ? -0.05f : 0.05f);
                    float yaw = Mathf.Atan2(-p.cardPos.x, 0.3f - p.cardPos.z) * Mathf.Rad2Deg;
                    yield return StartCoroutine(Deal(card, pos, p.human, yaw));
                    p.cards.Add(onTable[onTable.Count - 1]);
                }
            ShowMyHand();
            // betting rounds
            string[] streets = { "PRE-FLOP", "FLOP", "TURN", "RIVER" };
            for (int st = 0; st < 4 && Alive() > 1; st++)
            {
                if (st > 0)
                {
                    int n = st == 1 ? 3 : 1;
                    Draw();   // burn
                    for (int i = 0; i < n; i++)
                    {
                        int card = Draw(); board.Add(card);
                        yield return StartCoroutine(Deal(card, new Vector3(-0.24f + (board.Count - 1) * 0.12f, 0, 0.12f), true, 0));
                    }
                    ShowMyHand();
                    foreach (var p in players) { p.bet = 0; p.acted = false; }
                    currentBet = 0; minRaise = smallBlind * 2;
                    yield return StartCoroutine(GatherBets());
                }
                Status(streets[st], Palette.Teal);
                if (CanAct() > 1 || (st == 0)) yield return StartCoroutine(BettingRound(st == 0 ? Next(bb) : Next(button)));
                yield return StartCoroutine(GatherBets());
            }
            yield return StartCoroutine(Showdown());
            handRunning = false; running = false; AcceptingBets = true;
            if (infoLabel != null) infoLabel.text = "PRESS DEAL FOR THE NEXT HAND";
            RefreshLabels();
        }

        int Draw() { int c = deck[deck.Count - 1]; deck.RemoveAt(deck.Count - 1); return c; }

        /// <summary>The wallet refused the chips (break, loss limit, ban): the player is folded instead of being asked
        /// again and again for a call that can never go in.</summary>
        void FoldOut(P p)
        {
            p.folded = true; p.acted = true;
            foreach (var c in p.cards) if (c != null) StartCoroutine(Fx.Arc(c.transform, W(new Vector3(-0.62f, TableY + 0.05f, 0.4f)), 0.05f, 0.4f));
            if (p.human) Status("YOU · FOLDED", Palette.Muted);
        }
        int Next(int i) { return (i + 1) % players.Count; }
        int Alive() { int n = 0; foreach (var p in players) if (!p.folded) n++; return n; }
        int CanAct() { int n = 0; foreach (var p in players) if (!p.folded && !p.allIn) n++; return n; }

        void ShowMyHand()
        {
            var cards = new List<int>(me.hole); cards.AddRange(board);
            if (me.folded || cards.Count < 2) { handT.text = ""; return; }
            if (cards.Count >= 5) { var hv = CasinoHoldem.Best(cards); handT.text = "YOU: " + HandName(hv.cat); }
            else handT.text = "YOU: " + Shoe.RankName(me.hole[0]) + " " + Shoe.RankName(me.hole[1]) + ((me.hole[0] / 13 == me.hole[1] / 13) ? " SUITED" : "");
        }

        static string HandName(int cat) { return new[] { "HIGH CARD", "PAIR", "TWO PAIR", "THREE OF A KIND", "STRAIGHT", "FLUSH", "FULL HOUSE", "FOUR OF A KIND", "STRAIGHT FLUSH", "ROYAL FLUSH" }[Mathf.Clamp(cat, 0, 9)]; }

        IEnumerator BettingRound(int first)
        {
            int i = first, guard = 0;
            while (guard++ < 60)
            {
                if (Alive() <= 1) yield break;
                bool done = true;
                foreach (var q in players) if (!q.folded && !q.allIn && (!q.acted || q.bet < currentBet)) { done = false; break; }
                if (done) yield break;
                var p = players[i];
                if (!p.folded && !p.allIn && (!p.acted || p.bet < currentBet))
                {
                    if (p.human) yield return StartCoroutine(MyTurn());
                    else yield return StartCoroutine(BotTurn(p));
                    RefreshLabels();
                }
                i = Next(i);
            }
        }

        IEnumerator MyTurn()
        {
            waitingForMe = true; myAction = null;
            long toCall = currentBet - me.bet;
            if (infoLabel != null) infoLabel.text = "YOUR TURN · " + (toCall > 0 ? "TO CALL " + toCall.ToString("N0") : "CHECK OR BET") + " · POT " + pot.ToString("N0");
            Status("YOUR TURN", Palette.Mint);
            if (Random.value < 0.5f) Say("poker_turn");
            Sfx.UI("ui_open", 0.5f);
            XRIn.HapticBoth(0.2f, 0.05f);
            RefreshLabels();
            float until = Time.time + 30f;
            while (myAction == null && Time.time < until)
            {
                if (PlayerSeating.I == null || PlayerSeating.I.Current != PlayerSeat) { myAction = "fold"; break; }   // left the table
                yield return null;
            }
            if (myAction == null) myAction = toCall > 0 ? "fold" : "call";
            waitingForMe = false;
            Apply(me, myAction);
            if (infoLabel != null) infoLabel.text = "";
        }

        void Apply(P p, string a)
        {
            long toCall = currentBet - p.bet;
            long avail = p.human ? GameState.I.chips : p.stack;
            p.acted = true;
            switch (a)
            {
                case "fold":
                    if (toCall <= 0) { Say(p, "CHECK"); return; }
                    p.folded = true; Say(p, "FOLD");
                    foreach (var c in p.cards) if (c != null) StartCoroutine(Fx.Arc(c.transform, W(new Vector3(-0.62f, TableY + 0.05f, 0.4f)), 0.05f, 0.4f));
                    if (p.body != null) p.body.Play(Emote.Facepalm, 1f);
                    return;
                case "call":
                    if (toCall <= 0) { Say(p, "CHECK"); Sfx.Play("chip_clack1", W(p.cardPos), 0.3f); return; }
                    if (!PayIn(p, System.Math.Min(toCall, avail))) { FoldOut(p); return; }
                    Say(p, p.allIn ? "ALL-IN" : "CALL"); return;
                case "allin":
                    RaiseTo(p, p.bet + avail); return;
                case "pot":
                    RaiseTo(p, currentBet + System.Math.Max(minRaise, pot + toCall)); return;
                default:   // raise: minimum raise
                    RaiseTo(p, currentBet + minRaise); return;
            }
        }

        void RaiseTo(P p, long target)
        {
            long avail = p.human ? GameState.I.chips : p.stack;
            target = System.Math.Min(target, p.bet + avail);
            if (target <= currentBet) { Apply(p, "call"); return; }
            if (!PayIn(p, target - p.bet)) { FoldOut(p); return; }
            long raise = target - currentBet;
            if (raise >= minRaise) { minRaise = raise; foreach (var q in players) if (q != p && !q.folded && !q.allIn) q.acted = false; }
            currentBet = target;
            Say(p, p.allIn ? "ALL-IN " + p.total.ToString("N0") : "RAISE TO " + target.ToString("N0"));
            if (p.body != null) p.body.Play(Emote.Nod, 0.8f);
        }

        void Say(P p, string what)
        {
            if (p.human) Status("YOU · " + what, Palette.Mint);
            else { p.label.text = p.name + "\n<size=70%>" + what + "</size>"; }
        }

        // ------------------------------------------------------------ bots
        IEnumerator BotTurn(P p)
        {
            p.label.color = Palette.Gold;
            // "thinking": the hand-strength simulation is spread over frames (no hitch on Quest)
            float until = Time.time + Random.Range(0.7f, 1.6f), eqSum = 0f; int batches = 0;
            while (batches < 6 || Time.time < until) { if (batches < 6) { eqSum += Equity(p, 30); batches++; } yield return null; }
            float eq = eqSum / batches;
            long toCall = currentBet - p.bet;
            float potOdds = toCall > 0 ? toCall / (float)(pot + toCall) : 0f;
            int opp = Alive() - 1;
            float strength = eq * (1f + 0.15f * (opp - 1));       // equity vs a field
            float bluff = Random.value < p.aggro * 0.25f ? 0.25f : 0f;
            string a;
            if (toCall <= 0)
            {
                if (strength + bluff > 0.62f && Random.value < 0.4f + p.aggro) a = strength > 0.8f && Random.value < 0.4f ? "pot" : "raise";
                else a = "call";
            }
            else
            {
                if (strength > 0.85f && Random.value < 0.5f + p.aggro) a = Random.value < 0.25f ? "allin" : "pot";
                else if (strength + bluff > 0.65f && Random.value < p.aggro) a = "raise";
                else if (eq + p.loose * 0.15f > potOdds) a = "call";
                else a = "fold";
            }
            if (a == "raise" && p.stack < currentBet * 2) a = "call";
            Apply(p, a);
            p.label.color = Palette.Text;
        }

        /// <summary>Monte Carlo equity: the share of random run-outs this hand wins against the players still in.</summary>
        float Equity(P p, int sims)
        {
            var known = new HashSet<int>(p.hole); foreach (var c in board) known.Add(c);
            var rest = new List<int>(); for (int c = 0; c < 52; c++) if (!known.Contains(c)) rest.Add(c);
            int opp = Mathf.Max(1, Alive() - 1);
            float score = 0;
            var mine = new List<int>(7); var theirs = new List<int>(7); var b = new List<int>(5);
            for (int s = 0; s < sims; s++)
            {
                // partial shuffle
                int need = (5 - board.Count) + opp * 2;
                for (int i = 0; i < need && i < rest.Count; i++) { int j = Random.Range(i, rest.Count); int t = rest[i]; rest[i] = rest[j]; rest[j] = t; }
                b.Clear(); b.AddRange(board); int k = 0;
                while (b.Count < 5) b.Add(rest[k++]);
                mine.Clear(); mine.AddRange(p.hole); mine.AddRange(b);
                var mv = CasinoHoldem.Best(mine);
                bool lose = false; bool tie = false;
                for (int o = 0; o < opp; o++)
                {
                    theirs.Clear(); theirs.Add(rest[k++]); theirs.Add(rest[k++]); theirs.AddRange(b);
                    int cmp = CasinoHoldem.Compare(mv, CasinoHoldem.Best(theirs));
                    if (cmp < 0) { lose = true; break; }
                    if (cmp == 0) tie = true;
                }
                if (!lose) score += tie ? 0.5f : 1f;
            }
            return score / sims;
        }

        // ------------------------------------------------------------ interrupted hand (floor switched mid-hand)
        long pendingWin; bool payPending;

        /// <summary>The hand's coroutine died with the floor. Leaving the table mid-hand is a fold (as when you stand
        /// up): what you already put in stays in, but a pot you had already won at the showdown is paid. The bots get
        /// their own chips back and the table is ready for the next hand.</summary>
        protected override void OnRoundAborted()
        {
            if (me == null) return;
            bool wasPending = payPending;   // showdown already shared the pot out: only your payment is missing
            if (wasPending && GameState.Exists) GameState.I.Pay(pendingWin, me.total, transform.position);
            else if (me.total > 0 && !me.folded && GameState.Exists) GameState.I.Notify("LEFT THE POKER TABLE MID-HAND · FOLDED", Palette.Gold);
            payPending = false;
            foreach (var p in players)
            {
                if (!wasPending && !p.human && p.total > 0) p.stack += p.total;
                p.bet = 0; p.total = 0; p.folded = false; p.allIn = false; p.acted = false;
                foreach (Transform c in p.chips) Destroy(c.gameObject);
            }
            foreach (Transform c in potChips) Destroy(c.gameObject);
            pot = 0; currentBet = 0; board.Clear();
            ClearCardsNow();
            handRunning = false; waitingForMe = false; running = false;
            AcceptingBets = GameState.Exists && GameState.I.CanAccess(requiredTier);
            if (handT != null) handT.text = "";
            if (infoLabel != null) infoLabel.text = "PRESS DEAL TO START A HAND";
            RefreshLabels();
        }

        // ------------------------------------------------------------ chips to the pot, showdown, side pots
        IEnumerator GatherBets()
        {
            bool any = false;
            foreach (var p in players)
                foreach (Transform c in p.chips) { any = true; StartCoroutine(MoveChip(c, potChips)); }
            if (any) { Sfx.Play("chips_sweep", W(new Vector3(0, TableY, 0.3f)), 0.6f); yield return new WaitForSeconds(0.5f); }
            RefreshLabels();
        }

        IEnumerator MoveChip(Transform c, Transform to)
        {
            var target = to.position + new Vector3(Random.Range(-0.06f, 0.06f), to.childCount * 0.004f, Random.Range(-0.05f, 0.05f));
            yield return StartCoroutine(Fx.Arc(c, target, 0.05f, 0.4f));
            if (c != null) { c.SetParent(to, true); Tidy(to); }
        }

        IEnumerator Showdown()
        {
            var live = players.FindAll(p => !p.folded);
            if (live.Count > 1)
            {
                Status("SHOWDOWN", Palette.Gold); Say("poker_showdown");
                foreach (var p in live) if (!p.human) foreach (var c in p.cards) if (c != null && !c.faceUp) yield return StartCoroutine(c.Flip(0.3f));
                // run out the board if everyone was all-in early
                while (board.Count < 5) { int card = Draw(); board.Add(card); yield return StartCoroutine(Deal(card, new Vector3(-0.24f + (board.Count - 1) * 0.12f, 0, 0.12f), true, 0)); }
            }
            // side pots: settle level by level of total contribution
            var values = new Dictionary<P, CasinoHoldem.HandValue>();
            foreach (var p in live) { var cs = new List<int>(p.hole); cs.AddRange(board); values[p] = CasinoHoldem.Best(cs); }
            var levels = new List<long>(); foreach (var p in players) if (p.total > 0 && !levels.Contains(p.total)) levels.Add(p.total);
            levels.Sort();
            long prev = 0; long myWin = 0; P bestOverall = null; int mainWinners = 0; bool meInMain = false;
            foreach (var lv in levels)
            {
                long layer = 0; int contributors = 0;
                foreach (var p in players) { long part = System.Math.Max(0, System.Math.Min(p.total, lv) - prev); layer += part; if (part > 0) contributors++; }
                var eligible = live.FindAll(p => p.total >= lv);
                if (eligible.Count == 0) { prev = lv; continue; }
                var winners = new List<P>();
                foreach (var p in eligible)
                {
                    if (winners.Count == 0) { winners.Add(p); continue; }
                    int c = CasinoHoldem.Compare(values.ContainsKey(p) ? values[p] : default(CasinoHoldem.HandValue), values[winners[0]]);
                    if (c > 0) { winners.Clear(); winners.Add(p); } else if (c == 0) winners.Add(p);
                }
                // the rake only comes out of chips that were actually contested: an uncalled bet goes back whole
                long net = contributors > 1 ? (long)(layer * (double)(1f - Rake)) : layer;
                long odd = net - (net / winners.Count) * winners.Count;
                for (int wi = 0; wi < winners.Count; wi++)
                {
                    var w = winners[wi];
                    long share = net / winners.Count + (wi == 0 ? odd : 0);   // odd chips to the first winner, none lost
                    if (w.human) myWin += share; else w.stack += share;
                }
                if (bestOverall == null) { bestOverall = winners[0]; mainWinners = winners.Count; meInMain = winners.Contains(me); }
                prev = lv;
            }
            pendingWin = myWin; payPending = true;
            var winner = meInMain ? me : bestOverall;
            if (winner != null)
            {
                string how = live.Count > 1 && values.ContainsKey(winner) ? " · " + HandName(values[winner].cat) : "";
                // a split pot after the rake is not a win: never celebrate getting less back than you put in
                if (mainWinners > 1) Status((meInMain ? "SPLIT POT · " + myWin.ToString("N0") + " BACK" : "SPLIT POT") + how, Palette.Text);
                else if (winner.human && myWin <= me.total) Status("YOU TAKE " + myWin.ToString("N0") + " BACK" + how, Palette.Text);
                else Status((winner.human ? "YOU WIN" : winner.name + " WINS") + how, winner.human ? Palette.Gold : Palette.Text);
                if (winner.body != null) winner.body.Play(Emote.Cheer, 1.6f);
                foreach (Transform c in potChips) StartCoroutine(Fx.Arc(c, winner.human ? PlayerChest : W(winner.cardPos + Vector3.up * TableY), 0.15f, 0.6f));
                StartCoroutine(Reach(W(new Vector3(0, TableY + 0.05f, 0.3f)), 0.6f));
            }
            yield return new WaitForSeconds(0.8f);
            foreach (Transform c in potChips) Destroy(c.gameObject);
            payPending = false;
            GameState.I.Pay(myWin, me.total, W(new Vector3(0, TableY + 0.2f, -0.2f)));
            if (myWin == 0 && me.total > 0) Juice.Lose(W(me.cardPos));
            pot = 0; RefreshLabels();
            yield return new WaitForSeconds(1.2f);
        }
    }
}
