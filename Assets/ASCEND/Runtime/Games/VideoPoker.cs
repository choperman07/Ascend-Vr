using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Video Poker bar-top machine, Jacks or Better 9/6 (RTP 99.5% with perfect strategy). DEAL five cards,
    /// touch the cards you want to HOLD, DRAW replaces the rest. Paytable on the glass, winning row lights up.</summary>
    public class VideoPoker : ArcadeStation
    {
        static readonly string[] HandNames = { "ROYAL FLUSH", "STRAIGHT FLUSH", "FOUR OF A KIND", "FULL HOUSE", "FLUSH", "STRAIGHT", "THREE OF A KIND", "TWO PAIR", "JACKS OR BETTER" };
        static readonly float[] HandPays = { 800f, 50f, 25f, 9f, 6f, 4f, 3f, 2f, 1f };
        readonly int[] hand = new int[5];
        readonly bool[] hold = new bool[5];
        readonly Card3D[] cards = new Card3D[5];
        readonly TextMeshPro[] holdT = new TextMeshPro[5];
        readonly TextMeshPro[] rows = new TextMeshPro[9];
        Shoe deck;
        bool dealt, busy, dealing, drawing;
        long stake;
        PokeButton dealBtn;
        TextMeshPro resultT;

        protected override string Rules { get { return "JACKS OR BETTER · 9/6 · 1 DECK\nDEAL · TOUCH CARDS TO HOLD · DRAW\nPAYS FOR 1: ROYAL 800 · SF 50 · 4K 25 · FH 9\nFLUSH 6 · STRAIGHT 4 · 3K 3 · 2P 2 · JJ+ 1"; } }
        protected override float CounterWidth { get { return 1.0f; } }

        protected override void BuildMachine()
        {
            var body = new MeshKit();
            body.Box(new Vector3(0, 0.5f, 0.4f), new Vector3(0.95f, 1.0f, 0.55f), Palette.Hex("120E1C"), 0f);
            body.Box(new Vector3(0, 1.55f, 0.55f), new Vector3(0.95f, 1.1f, 0.3f), Quaternion.Euler(-15f, 0, 0), Palette.Hex("120E1C"), 0f);
            body.Spawn("Cabinet", transform, Palette.Lacquer, false);
            var scr = new MeshKit();
            scr.Quad(new Vector3(0, 1.55f, 0.395f), new Vector2(0.86f, 0.98f), Quaternion.Euler(-15f, 0, 0), Palette.Hex("060C2A"), 0.85f);
            for (int s = -1; s <= 1; s += 2) scr.Bar(new Vector3(s * 0.48f, 1.0f, 0.37f), new Vector3(s * 0.48f, 2.08f, 0.66f), 0.014f, Accent, 1f);
            scr.Spawn("Screen", transform, Palette.Neon, false);
            var screen = Build.Empty("ScreenSpace", transform, new Vector3(0, 1.55f, 0.385f), Quaternion.Euler(-15f, 0, 0)).transform;
            for (int i = 0; i < 9; i++)
            {
                rows[i] = Txt.Make("Row" + i, screen, new Vector3(0, 0.44f - i * 0.045f, 0), HandNames[i] + "  " + HandPays[i] + "x", 0.032f, Palette.Gold, Palette.Display);
                rows[i].rectTransform.sizeDelta = new Vector2(0.8f, 0.05f);
            }
            resultT = Txt.Glow("Result", screen, new Vector3(0, -0.02f, 0), "", 0.07f, Palette.Text);
            for (int i = 0; i < 5; i++)
            {
                holdT[i] = Txt.Make("Hold" + i, screen, new Vector3(-0.32f + i * 0.16f, -0.36f, 0), "", 0.035f, Palette.Mint, Palette.Display);
                // touch target in front of each card slot
                var hz = new GameObject("HoldZone" + i);
                hz.transform.SetParent(screen, false);
                hz.transform.localPosition = new Vector3(-0.32f + i * 0.16f, -0.2f, -0.02f);
                hz.AddComponent<BoxCollider>().size = new Vector3(0.14f, 0.2f, 0.05f);
                int idx = i;
                hz.AddComponent<TileTouch>().onTouch = () => Toggle(idx);
                hz.AddComponent<XRSimpleInteractable>().selectEntered.AddListener(_ => Toggle(idx));
            }
            dealBtn = PokeButton.Create("Deal", transform, new Vector3(0.3f, TableY + 0.035f, -0.32f), Quaternion.identity, 0.045f, Palette.Gold, "DEAL", () => StartCoroutine(DealOrDraw()));
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                PokeButton.Create("H" + i, transform, new Vector3(-0.32f + i * 0.13f, TableY + 0.035f, -0.42f), Quaternion.identity, 0.022f, Palette.Teal, "HOLD", () => Toggle(idx), 0.02f);
            }
            deck = new Shoe(1);
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            for (int i = 0; i < 5; i++) { int idx = i; UIKit.Btn(p, "HOLD " + (i + 1), 20 + i * 104, y, 96, 60, Palette.Teal, () => Toggle(idx), 20); }
            UIKit.Btn(p, "DEAL / DRAW", 20, y + 70, 520, 80, Palette.Gold, () => StartCoroutine(DealOrDraw()), 36);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 380); } }

        Transform Screen { get { return transform.Find("ScreenSpace"); } }
        Vector3 SlotPos(int i) { return Screen.TransformPoint(new Vector3(-0.32f + i * 0.16f, -0.2f, -0.012f)); }
        Quaternion SlotRot { get { return Screen.rotation * Quaternion.Euler(-90, 0, 0); } }

        void Toggle(int i)
        {
            if (!dealt || busy) return;
            hold[i] = !hold[i];
            holdT[i].text = hold[i] ? "HELD" : "";
            Sfx.Play("ui_click", SlotPos(i), 0.6f, hold[i] ? 1.2f : 0.9f);
        }

        IEnumerator Place(int i, int card)
        {
            if (cards[i] != null) Destroy(cards[i].gameObject);
            var c = Card3D.Spawn(card, SlotPos(i), SlotRot, false);
            c.transform.SetParent(transform, true);   // stays with the machine (hidden with its floor)
            c.transform.localScale = Vector3.one * 2.1f;
            cards[i] = c;
            yield return StartCoroutine(c.Flip(0.18f));
        }

        IEnumerator DealOrDraw()
        {
            if (busy) yield break;
            busy = true;
            foreach (var r in rows) r.color = Palette.Gold;
            if (!dealt)
            {
                stake = Take();
                if (stake <= 0) { busy = false; yield break; }
                deck = new Shoe(1);
                resultT.text = "";
                for (int i = 0; i < 5; i++) { hold[i] = false; holdT[i].text = ""; hand[i] = deck.Draw(); }
                dealing = true;
                for (int i = 0; i < 5; i++) yield return StartCoroutine(Place(i, hand[i]));
                dealing = false;
                int pre = Evaluate(hand);
                if (pre >= 0) { rows[pre].color = Palette.Mint; resultT.text = HandNames[pre]; }
                dealt = true;
                if (dealBtn != null) dealBtn.SetLabel("DRAW");
                Status("TOUCH CARDS TO HOLD · THEN DRAW", Palette.Text);
                if (Random.value < 0.35f) Voice.Say("host_vp_deal", transform, 1.6f, 0.6f);
            }
            else
            {
                for (int i = 0; i < 5; i++) if (!hold[i]) hand[i] = deck.Draw();
                drawing = true;
                for (int i = 0; i < 5; i++) if (!hold[i]) yield return StartCoroutine(Place(i, hand[i]));
                drawing = false;
                int res = Evaluate(hand);
                float m = res >= 0 ? HandPays[res] : 0f;
                if (res >= 0) { rows[res].color = Palette.Mint; StartCoroutine(Fx.Pulse(rows[res].transform, 1.2f, 0.3f)); }
                resultT.text = res >= 0 ? HandNames[res] : "NO WIN";
                Status(res >= 0 ? HandNames[res] + " · " + m + "x" : "NO WIN", res >= 0 ? Palette.Gold : Palette.Muted);
                if (res == 0) GameState.I.Unlock("ROYALTY", "A ROYAL FLUSH ON VIDEO POKER", 3000);
                PayDirect((long)(stake * (double)m), stake, SlotPos(2));
                dealt = false;
                if (dealBtn != null) dealBtn.SetLabel("DEAL");
                for (int i = 0; i < 5; i++) { hold[i] = false; holdT[i].text = ""; }
            }
            busy = false;
        }

        /// <summary>Leaving the floor mid-animation used to leave the machine "busy" forever. The hand is already
        /// decided when the animation starts: an interrupted deal is voided (stake back), an interrupted draw is paid.</summary>
        protected override void OnDisable()
        {
            base.OnDisable();
            if (!busy || !GameState.Exists) return;
            if (dealing) { GameState.I.Pay(stake, stake, transform.position); dealt = false; }
            else if (drawing)
            {
                int res = Evaluate(hand);
                GameState.I.Pay(res >= 0 ? (long)(stake * (double)HandPays[res]) : 0, stake, transform.position);
                if (res >= 0) resultT.text = HandNames[res]; else resultT.text = "NO WIN";
                dealt = false;
            }
            dealing = drawing = busy = false;
            for (int i = 0; i < 5; i++) { hold[i] = false; if (holdT[i] != null) holdT[i].text = ""; }
            if (dealBtn != null) dealBtn.SetLabel(dealt ? "DRAW" : "DEAL");
        }

        /// <summary>Index into HandNames (0 = royal ... 8 = jacks or better), -1 = nothing.</summary>
        public static int Evaluate(int[] h)
        {
            var ranks = new int[13]; var suits = new int[4];
            foreach (var c in h) { ranks[c % 13]++; suits[(c / 13) % 4]++; }
            bool flush = false; foreach (var s in suits) if (s == 5) flush = true;
            int distinct = 0, low = -1, high = -1;
            for (int r = 0; r < 13; r++) if (ranks[r] > 0) { distinct++; if (low < 0) low = r; high = r; }
            bool straight = distinct == 5 && (high - low == 4 || (ranks[12] == 1 && ranks[0] == 1 && ranks[1] == 1 && ranks[2] == 1 && ranks[3] == 1));
            if (straight && flush) return (low == 8 && high == 12) ? 0 : 1;   // 10..A royal
            int four = 0, three = 0, pairs = 0, highPair = 0;
            for (int r = 0; r < 13; r++)
            {
                if (ranks[r] == 4) four++;
                else if (ranks[r] == 3) three++;
                else if (ranks[r] == 2) { pairs++; if (r >= 9) highPair++; }   // J(9) Q K A
            }
            if (four > 0) return 2;
            if (three > 0 && pairs > 0) return 3;
            if (flush) return 4;
            if (straight) return 5;
            if (three > 0) return 6;
            if (pairs == 2) return 7;
            if (highPair > 0) return 8;
            return -1;
        }
    }
}
