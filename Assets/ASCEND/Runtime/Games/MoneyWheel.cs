using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Big Six money wheel: a 2-metre lit wheel with 54 segments and a leather clapper. Bet on 1, 2, 5, 10,
    /// 20, JOKER or ASCEND; the host spins it, the clapper ticks every peg and the wheel slows down for real.</summary>
    public class MoneyWheel : TableGame
    {
        // segment layout around the wheel (54): index into Values
        public static readonly int[] Layout = BuildLayout();
        static readonly string[] Labels = { "1", "2", "5", "10", "20", "JOKER", "ASCEND" };
        static readonly float[] Pays = { 2f, 3f, 6f, 11f, 21f, 41f, 41f };
        BetSpot[] bets = new BetSpot[7];
        /// <summary>Betting strip: near edge (hinge) z, depth up the slope, half width, tilt and the ring centre (board local).</summary>
        const float StripNear = -0.33f, StripDepth = 0.42f, StripHalfW = 1.07f, Tilt = 12f, RingZ = 0.15f;
        Transform board;
        Transform wheel, clapper;
        float angle;
        PokeButton spinBtn;
        TextMeshPro histT;
        readonly List<int> history = new List<int>();

        static int[] BuildLayout()
        {
            // the classic order: 1s everywhere, 2s and 5s spread, the big ones far apart
            string pattern = "1 2 1 5 1 2 1 10 1 2 1 5 2 1 2 20 1 2 1 5 1 2 1 J 1 2 1 10 1 5 1 2 1 2 1 5 1 10 2 1 2 20 1 2 1 5 1 2 1 10 2 1 5 A";
            var parts = pattern.Split(' ');
            var l = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                switch (parts[i]) { case "1": l[i] = 0; break; case "2": l[i] = 1; break; case "5": l[i] = 2; break; case "10": l[i] = 3; break; case "20": l[i] = 4; break; case "J": l[i] = 5; break; default: l[i] = 6; break; }
            }
            return l;
        }

        public override bool AutoHidePanel { get { return true; } }
        protected override string Rules
        {
            get
            {
                // one symbol per line (the rules card turns every " · " into a new line, so none are used here)
                var sb = new System.Text.StringBuilder("BIG SIX, 54 SEGMENTS: BET ON THE SYMBOL THAT STOPS UNDER THE CLAPPER\n");
                for (int i = 0; i < 7; i++)
                {
                    int n = 0; foreach (var x in Layout) if (x == i) n++;
                    sb.Append(Labels[i]).Append("  PAYS ").Append(Pays[i]).Append('x');
                    if (i < 6) sb.Append('\n');
                }
                return sb.ToString();
            }
        }
        protected override Vector3 DealerPos { get { return new Vector3(1.25f, 0, 0.6f); } }
        protected override Vector3 TrayPos { get { return new Vector3(-0.55f, TableY + 0.02f, -0.45f); } }
        protected override Vector3 StatusPos { get { return new Vector3(0, 3.45f, 0.9f); } }
        protected override BetSpot DefaultSpot { get { return bets[0]; } }
        protected override List<Vector4> SeatSpots { get { return new List<Vector4> { new Vector4(-1.25f, 0, -0.95f, 20f), new Vector4(1.15f, 0, -0.95f, -20f) }; } }

        protected override void BuildTable()
        {
            // betting counter
            var body = new MeshKit { boxUV = 1.5f };
            body.Box(new Vector3(0, TableY * 0.5f - 0.03f, -0.15f), new Vector3(2.2f, TableY - 0.06f, 0.6f), Color.white, 0f);
            body.Spawn("Counter", transform, Palette.Wood);
            var top = new MeshKit();
            top.Box(new Vector3(0, TableY - 0.02f, -0.15f), new Vector3(2.26f, 0.04f, 0.66f), Palette.Hex("120E1C"), 0f);
            top.Bar(new Vector3(-1.13f, TableY + 0.0f, -0.48f), new Vector3(1.13f, TableY, -0.48f), 0.03f, Palette.Gold, 0.9f);
            top.Bar(new Vector3(-1.1f, 0.06f, -0.45f), new Vector3(1.1f, 0.06f, -0.45f), 0.02f, Accent, 1f);
            top.Spawn("Top", transform, Palette.Lacquer, false);
            var col = gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0, 0.45f, -0.15f); col.size = new Vector3(2.26f, 0.9f, 0.66f);
            // the betting strip leans toward the player on a built-in riser (hinge on its near edge, far edge raised), so the
            // symbol and payout printed at the back of every tile read from the stool
            board = RaisedBoard.Create(transform, new Vector3(0, TableY, StripNear), Tilt);
            RaisedBoard.Riser(transform, board, -StripHalfW, StripHalfW, StripDepth, Tilt, Palette.Lacquer, Palette.Hex("120E1C"), Palette.Gold);
            var plate = new MeshKit();
            plate.Box(new Vector3(0, 0f, StripDepth * 0.5f), new Vector3(StripHalfW * 2f, 0.004f, StripDepth), Palette.Hex("0A0712"), 0f);
            plate.Spawn("Strip", board, Palette.Neon, false);
            for (int i = 0; i < 7; i++)
            {
                float x = -0.93f + i * 0.31f;
                var c = i == 5 ? Palette.Magenta : i == 6 ? Palette.Gold : i >= 3 ? Palette.Violet : Palette.Teal;
                var tile = new MeshKit();
                tile.Box(new Vector3(x, 0.003f, StripDepth * 0.5f), new Vector3(0.28f, 0.006f, StripDepth), Palette.Hex("0E0A18"), 0f);
                tile.Ring(new Vector3(x, 0.008f, RingZ), 0.09f, 0.1f, 28, Quaternion.identity, c, 1f);
                tile.Spawn("Tile" + i, board, Palette.Neon, false);
                // label behind the ring, lifted 20° off the (already tilted) strip: ~32° toward the player in all
                var lab = Txt.Make("L" + i, board, new Vector3(x, 0.012f, 0.33f), Labels[i] + "\n<size=60%>" + Pays[i] + "x</size>", 0.065f, c, Palette.Display);
                lab.transform.localRotation = Quaternion.Euler(70, 0, 0);
                bets[i] = BetSpot.Create(this, Labels[i], Pays[i], board, new Vector3(x, 0.006f, RingZ), new Vector2(0.26f, 0.26f));
                spots.Add(bets[i]);
            }

            // the wheel on a tall stand behind the counter
            var stand = new MeshKit();
            stand.Box(new Vector3(0, 1.2f, 0.95f), new Vector3(0.3f, 2.4f, 0.2f), Palette.Hex("15111F"), 0f);
            stand.Cylinder(new Vector3(0, 2.25f, 0.92f), 1.12f, 1.12f, 0.12f, 64, Quaternion.Euler(90, 0, 0), Palette.Hex("1A1428"), 0f);
            stand.Torus(new Vector3(0, 2.25f, 0.85f), 1.12f, 0.035f, 72, 6, Quaternion.Euler(90, 0, 0), Palette.Gold, 0.85f);
            for (int i = 0; i < 24; i++)
            {
                var p = Quaternion.Euler(0, 0, i * 15f) * Vector3.up * 1.16f;
                stand.Sphere(new Vector3(p.x, 2.25f + p.y, 0.85f), 0.03f, 8, i % 2 == 0 ? Palette.Gold : Palette.Magenta, 1f);
            }
            stand.Spawn("Stand", transform, Palette.Lacquer, false);
            wheel = Build.Empty("Wheel", transform, new Vector3(0, 2.25f, 0.84f)).transform;
            var wk = new MeshKit();
            wk.Disc(Vector3.zero, 1.05f, 108, Quaternion.Euler(-90, 0, 0), Color.white, 0.55f, new Rect(0, 0, 1, 1));
            wk.Spawn("Face", wheel, Palette.Textured("money_wheel", 0.6f), false);
            var pegs = new MeshKit();
            for (int i = 0; i < Layout.Length; i++)
            {
                var p = Quaternion.Euler(0, 0, (i + 0.5f) * 360f / Layout.Length) * Vector3.up * 1.0f;   // pegs sit between segments
                pegs.Cylinder(new Vector3(p.x, p.y, -0.03f), 0.012f, 0.012f, 0.06f, 8, Quaternion.Euler(90, 0, 0), Palette.Silver, 0.2f);
            }
            pegs.Cylinder(new Vector3(0, 0, -0.04f), 0.14f, 0.14f, 0.08f, 32, Quaternion.Euler(90, 0, 0), Palette.Gold, 0.6f);
            pegs.Spawn("Pegs", wheel, Palette.BrushedMetal, false);
            clapper = Build.Empty("Clapper", transform, new Vector3(0, 3.38f, 0.8f)).transform;
            var ck = new MeshKit();
            ck.Box(new Vector3(0, -0.06f, 0), new Vector3(0.05f, 0.14f, 0.02f), Palette.Red, 0.4f);
            ck.Sphere(new Vector3(0, 0.0f, 0), 0.025f, 10, Palette.Gold, 0.6f);
            ck.Spawn("Flap", clapper, Palette.Neon, false);
            histT = Txt.Make("History", transform, new Vector3(-1.45f, 2.25f, 0.85f), "", 0.08f, Palette.Text, Palette.Display, TextAlignmentOptions.Center, 0.6f);
            spinBtn = PokeButton.Create("Spin", transform, new Vector3(0.75f, TableY + 0.035f, -0.42f), Quaternion.identity, 0.05f, Palette.Gold, "SPIN", () => StartCoroutine(Spin()));
            var lg = new GameObject("WheelLight"); lg.transform.SetParent(transform, false); lg.transform.localPosition = new Vector3(0, 2.3f, -0.4f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 3f; l.intensity = 1.4f; l.color = new Color(1f, 0.85f, 0.75f); l.shadows = LightShadows.None;
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            for (int i = 0; i < 7; i++)
            {
                int k = i;
                UIKit.Btn(p, Labels[i], 20 + (i % 4) * 130, y + (i / 4) * 64, 122, 56, i >= 5 ? Palette.Magenta : Palette.Teal, () => bets[k].AddValue(minBet, W(TrayPos) + Vector3.up * 0.05f), 24);
            }
            UIKit.Btn(p, "SPIN", 20 + 3 * 130, y + 64, 122, 56, Palette.Gold, () => StartCoroutine(Spin()), 26);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 360); } }

        IEnumerator Spin()
        {
            if (!BeginRound()) yield break;
            spinBtn.SetInteractable(false);
            Status("NO MORE BETS", Palette.Gold);
            Say("wheel_spin");
            StartCoroutine(Reach(wheel.position + transform.right * 0.9f, 0.8f));
            int n = Layout.Length;
            int result = Odds.Range(0, n);
            float seg = 360f / n;
            // the pointer is at the top; segment i (printed i*seg clockwise from the top) sits under it at angle = i*seg
            float target = result * seg + Random.Range(-seg * 0.35f, seg * 0.35f);
            float start = angle;
            float turns = 360f * Random.Range(3, 5);
            float end = start + turns + Mathf.Repeat(target - start, 360f);
            // pegs sit between segments, (i + 0.5) * seg: the clapper ticks when one passes the top
            float dur = 6.5f, lastPeg = Mathf.Floor(start / seg - 0.5f);
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float k = t / dur;
                angle = Mathf.Lerp(start, end, 1f - Mathf.Pow(1f - k, 3f));
                wheel.localRotation = Quaternion.Euler(0, 0, -angle);
                float peg = Mathf.Floor(angle / seg - 0.5f);
                if (peg != lastPeg)
                {
                    lastPeg = peg;
                    Sfx.Play("ball_tick", clapper.position, 0.5f, Random.Range(0.9f, 1.1f));
                    clapper.localRotation = Quaternion.Euler(0, 0, 25f);
                }
                clapper.localRotation = Quaternion.Slerp(clapper.localRotation, Quaternion.identity, Time.deltaTime * 14f);
                yield return null;
            }
            angle = end;
            int kind = Layout[result];
            history.Insert(0, kind); if (history.Count > 8) history.RemoveAt(8);
            var sb = new System.Text.StringBuilder("LAST\n"); foreach (var h in history) sb.Append(Labels[h]).Append('\n');
            histT.text = sb.ToString();
            Status(Labels[kind] + (kind >= 5 ? "!!" : "") + " · PAYS " + Pays[kind] + "x", kind >= 4 ? Palette.Gold : Palette.Text);
            // celebrate a big segment only when the player actually has chips on it (a loss is never celebrated)
            bool playerHit = bets[kind].Total > 0;
            if (kind >= 5 && playerHit) Say("wheel_big");
            if (kind >= 4 && playerHit) { Sfx.Play("jackpot", wheel.position, 0.8f); Juice.Burst("confetti", wheel.position, 40); }
            RaisedBoard.Highlight(bets[kind], true, Palette.Gold);
            yield return new WaitForSeconds(1.1f);
            yield return StartCoroutine(Settle(s => s == bets[kind] ? Pays[kind] : 0f));
            RaisedBoard.Highlight(bets[kind], false, Palette.Gold);
            spinBtn.SetInteractable(true);
            Status("PLACE YOUR BETS", Palette.Text);
        }
    
        protected override void OnRoundAborted()
        {
            if (spinBtn != null) spinBtn.SetInteractable(true);
            foreach (var b in bets) if (b != null) RaisedBoard.Highlight(b, false, Palette.Gold);
        }
    }
}
