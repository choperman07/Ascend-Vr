using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Sic Bo: three dice under a glass dome. SMALL / BIG / ODD / EVEN, every total from 4 to 17, any triple and
    /// single numbers (paid once per die showing it). The dealer shakes the dome; the winning areas light up.</summary>
    public class SicBo : TableGame
    {
        static readonly Vector3 FeltC = new Vector3(-0.15f, 0, 0.1f);
        const float FeltW = 2.3f, FeltH = 1.1f;
        /// <summary>The layout leans toward the stool so the far rows (SMALL / BIG / TRIPLE) read from the seat.</summary>
        const float Tilt = 12f;
        Transform board;
        static readonly int[] TotalPays = { 0, 0, 0, 0, 61, 31, 18, 13, 9, 7, 7, 7, 7, 9, 13, 18, 31, 61 };   // index = total (x return)
        readonly Dictionary<BetSpot, System.Func<int, int, int, float>> rule = new Dictionary<BetSpot, System.Func<int, int, int, float>>();
        BetSpot big;
        Transform dome, shaker;
        readonly Transform[] dice = new Transform[3];
        PokeButton shakeBtn;
        TextMeshPro resultT;

        public override bool AutoHidePanel { get { return true; } }
        // RTPs computed over the 216 outcomes; the rules card turns every " · " into a new line, so commas are used here
        protected override string Rules { get { return "SMALL 4–10 / BIG 11–17 2x, LOSE ON TRIPLES\nODD / EVEN 2x, LOSE ON TRIPLES\nANY TRIPLE 31x\nTOTALS 4/17 61x, 5/16 31x, 6/15 18x, 7/14 13x, 8/13 9x, 9–12 7x\nSINGLE NUMBER 2x / 3x / 4x ON 1 / 2 / 3 DICE"; } }
        protected override Vector3 DealerPos { get { return new Vector3(0.9f, 0, 0.95f); } }
        protected override Vector3 TrayPos { get { return new Vector3(-0.4f, TableY + 0.02f, -0.62f); } }
        protected override Vector3 PanelPos { get { return new Vector3(0.62f, TableY + 0.1f, -0.78f); } }
        protected override BetSpot DefaultSpot { get { return big; } }
        protected override List<Vector4> SeatSpots
        {
            get { return new List<Vector4> { new Vector4(-1.45f, 0, -1.0f, 15f), new Vector4(-0.75f, 0, -1.2f, 5f), new Vector4(0.8f, 0, -1.2f, -5f), new Vector4(1.5f, 0, -1.0f, -18f) }; }
        }

        /// <summary>Print fractions (u across, v from the far edge) → board local (hinge on the near edge, +Z up the slope).</summary>
        Vector3 Cell(float u0, float v0, float u1, float v1) { float u = (u0 + u1) * 0.5f, v = (v0 + v1) * 0.5f; return new Vector3(FeltC.x + (u - 0.5f) * FeltW, 0.004f, (1f - v) * FeltH); }
        BetSpot Spot(string id, float pays, float u0, float v0, float u1, float v1, System.Func<int, int, int, float> win)
        {
            var s = BetSpot.Create(this, id, pays, board, Cell(u0, v0, u1, v1), new Vector2((u1 - u0) * FeltW * 0.92f, (v1 - v0) * FeltH * 0.9f));
            spots.Add(s); rule[s] = win; return s;
        }

        static bool Triple(int a, int b, int c) { return a == b && b == c; }

        protected override void BuildTable()
        {
            var body = new MeshKit { boxUV = 1.2f };
            body.Box(new Vector3(0.2f, TableY * 0.5f - 0.05f, 0.1f), new Vector3(3.0f, TableY - 0.1f, 1.35f), Color.white, 0f);
            body.Spawn("Body", transform, Palette.Wood);
            var rail = new MeshKit { boxUV = 2f };
            rail.Box(new Vector3(0.2f, TableY - 0.0f, -0.6f), new Vector3(3.1f, 0.08f, 0.14f), Color.white, 0f);
            rail.Spawn("Rail", transform, Palette.Leather);
            var top = new MeshKit();
            top.Box(new Vector3(0.2f, TableY - 0.03f, 0.1f), new Vector3(3.05f, 0.05f, 1.4f), Palette.Hex("1B1328"), 0f);
            top.Bar(new Vector3(-1.35f, TableY + 0.045f, -0.68f), new Vector3(1.75f, TableY + 0.045f, -0.68f), 0.012f, Palette.Gold, 1f);
            top.Bar(new Vector3(-1.35f, 0.06f, -0.55f), new Vector3(1.75f, 0.06f, -0.55f), 0.02f, Accent, 1f);
            top.Spawn("Top", transform, Palette.Lacquer, false);
            // the printed layout on a built-in riser, hinged on its near edge and raised at the back (the shaker stays flat)
            board = RaisedBoard.Create(transform, new Vector3(0, TableY, FeltC.z - FeltH * 0.5f), Tilt);
            RaisedBoard.Riser(transform, board, FeltC.x - FeltW * 0.5f, FeltC.x + FeltW * 0.5f, FeltH, Tilt, Palette.Lacquer, Palette.Hex("1B1328"), Palette.Gold);
            var felt = new MeshKit();
            felt.Plane(new Vector3(FeltC.x, 0.001f, FeltH * 0.5f), new Vector2(FeltW, FeltH), Quaternion.identity, Color.white, 0.12f);
            felt.Spawn("Felt", board, Palette.Textured("felt_sicbo"), false);
            var col = gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0.2f, 0.45f, 0.1f); col.size = new Vector3(3.1f, 0.9f, 1.45f);

            // top row: SMALL ODD ANY-TRIPLE EVEN BIG
            Spot("SMALL", 2f, 0.02f, 0.06f, 0.22f, 0.30f, (a, b, c) => !Triple(a, b, c) && a + b + c <= 10 ? 2f : 0f);
            Spot("ODD", 2f, 0.22f, 0.06f, 0.40f, 0.30f, (a, b, c) => !Triple(a, b, c) && (a + b + c) % 2 == 1 ? 2f : 0f);
            Spot("ANY TRIPLE", 31f, 0.40f, 0.06f, 0.60f, 0.30f, (a, b, c) => Triple(a, b, c) ? 31f : 0f);
            Spot("EVEN", 2f, 0.60f, 0.06f, 0.78f, 0.30f, (a, b, c) => !Triple(a, b, c) && (a + b + c) % 2 == 0 ? 2f : 0f);
            big = Spot("BIG", 2f, 0.78f, 0.06f, 0.98f, 0.30f, (a, b, c) => !Triple(a, b, c) && a + b + c >= 11 ? 2f : 0f);
            // totals 4..17
            for (int t = 4; t <= 17; t++)
            {
                int tt = t; float u0 = 0.02f + (t - 4) * 0.0686f;
                Spot("T" + t, TotalPays[t], u0, 0.34f, u0 + 0.0686f, 0.58f, (a, b, c) => a + b + c == tt ? TotalPays[tt] : 0f);
            }
            // singles 1..6
            for (int n = 1; n <= 6; n++)
            {
                int nn = n; float u0 = 0.02f + (n - 1) * 0.16f;
                Spot("S" + n, 4f, u0, 0.62f, u0 + 0.16f, 0.92f, (a, b, c) => { int k = (a == nn ? 1 : 0) + (b == nn ? 1 : 0) + (c == nn ? 1 : 0); return k == 0 ? 0f : 1f + k; });
            }

            // the shaker: gold base + glass dome with three dice
            shaker = Build.Empty("Shaker", transform, new Vector3(1.35f, TableY, 0.35f)).transform;
            var sk = new MeshKit();
            sk.Cylinder(new Vector3(0, 0.03f, 0), 0.17f, 0.19f, 0.06f, 32, Quaternion.identity, Palette.Gold, 0.2f);
            sk.Disc(new Vector3(0, 0.061f, 0), 0.16f, 32, Quaternion.identity, Palette.Hex("0E2A26"), 0.05f);
            sk.Spawn("Base", shaker, Palette.Gold3, false);
            var dk = new MeshKit();
            dk.Sphere(new Vector3(0, 0.06f, 0), new Vector3(0.16f, 0.15f, 0.16f), 20, Palette.Hex("8FE8FF"), 0.18f);
            dome = dk.Spawn("Dome", shaker, Palette.Glow, false).transform;
            for (int i = 0; i < 3; i++) dice[i] = MakeDie("Die" + i, shaker, new Vector3(-0.06f + i * 0.06f, 0.085f, (i % 2) * 0.04f - 0.02f));
            resultT = Txt.Glow("Result", transform, new Vector3(1.35f, 1.65f, 0.6f), "", 0.14f, Palette.Text);
            shakeBtn = PokeButton.Create("Shake", transform, new Vector3(0.95f, TableY + 0.05f, -0.62f), Quaternion.identity, 0.05f, Palette.Gold, "SHAKE", () => StartCoroutine(Round()));
            Build.Beam(transform, new Vector3(0.2f, 3.6f, 0.1f), Quaternion.identity, 2.8f, 1.4f, Accent, 0.12f);
        }

        Transform MakeDie(string n, Transform parent, Vector3 pos)
        {
            var k = new MeshKit();
            k.Box(Vector3.zero, Vector3.one * 0.045f, Palette.Text, 0.2f);
            float h = 0.0232f;
            Vector3[] faces = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            int[] count = { 1, 6, 3, 4, 2, 5 };
            for (int f = 0; f < 6; f++)
            {
                var nrm = faces[f]; var tA = Vector3.Cross(nrm, Mathf.Abs(nrm.y) > 0.5f ? Vector3.right : Vector3.up).normalized; var tB = Vector3.Cross(nrm, tA);
                var col = count[f] == 1 || count[f] == 4 ? Palette.Red : Palette.Dark;
                foreach (var o in PipsFor(count[f])) k.Box(nrm * h + (tA * o.x + tB * o.y) * 0.011f, Vector3.one * 0.0075f, col, count[f] == 1 || count[f] == 4 ? 0.8f : 0f);
            }
            var go = k.Spawn(n, parent, pos, Quaternion.identity, Palette.Glossy, false);
            return go.transform;
        }

        static Vector2[] PipsFor(int n)
        {
            switch (n)
            {
                case 1: return new[] { Vector2.zero };
                case 2: return new[] { new Vector2(-1, -1), new Vector2(1, 1) };
                case 3: return new[] { new Vector2(-1, -1), Vector2.zero, new Vector2(1, 1) };
                case 4: return new[] { new Vector2(-1, -1), new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1) };
                case 5: return new[] { new Vector2(-1, -1), new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), Vector2.zero };
                default: return new[] { new Vector2(-1, -1), new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, 0), new Vector2(1, 0) };
            }
        }

        static Quaternion FaceUp(int n)
        {
            switch (n)
            {
                case 1: return Quaternion.identity;
                case 6: return Quaternion.Euler(180, 0, 0);
                case 3: return Quaternion.Euler(0, 0, 90);
                case 4: return Quaternion.Euler(0, 0, -90);
                case 2: return Quaternion.Euler(-90, 0, 0);
                default: return Quaternion.Euler(90, 0, 0);
            }
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Label(p, "QUICK BETS GO ON BIG · DROP CHIPS ON ANY AREA", 20, y, 520, 30, 18, Palette.Muted, Palette.Mono);
            UIKit.Btn(p, "SMALL +" + minBet, 20, y + 36, 250, 56, Palette.Teal, () => spots[0].AddValue(minBet, W(TrayPos)), 24);
            UIKit.Btn(p, "TRIPLE +" + minBet, 290, y + 36, 250, 56, Palette.Magenta, () => spots[2].AddValue(minBet, W(TrayPos)), 24);
            UIKit.Btn(p, "SHAKE", 20, y + 100, 520, 84, Palette.Gold, () => StartCoroutine(Round()), 40);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 410); } }

        IEnumerator Round()
        {
            if (!BeginRound()) yield break;
            shakeBtn.SetInteractable(false);
            foreach (var s in spots) RaisedBoard.Highlight(s, false, Palette.Gold);
            Status("NO MORE BETS", Palette.Gold);
            // one line at a time (two clips at once overlap and the second subtitle replaces the first)
            float said = Say("no_more");
            yield return new WaitForSeconds(Mathf.Min(said, 1.0f));
            Say("sicbo_shake");
            StartCoroutine(Reach(shaker.position + Vector3.up * 0.2f, 1.6f));
            int a = Odds.Range(1, 7), b = Odds.Range(1, 7), c = Odds.Range(1, 7);
            int[] res = { a, b, c };
            var rattle = Sfx.Loop("dice_shake", shaker, 0.9f);
            for (float t = 0; t < 1.6f; t += Time.deltaTime)
            {
                shaker.localPosition = new Vector3(1.35f, TableY + Mathf.Abs(Mathf.Sin(t * 30f)) * 0.03f, 0.35f) + new Vector3(Mathf.Sin(t * 41f), 0, Mathf.Cos(t * 37f)) * 0.012f;
                for (int i = 0; i < 3; i++) { dice[i].Rotate(new Vector3(600 + i * 90, 420, 300) * Time.deltaTime, Space.Self); dice[i].localPosition = new Vector3(-0.06f + i * 0.06f, 0.085f + Mathf.Abs(Mathf.Sin(t * 25f + i)) * 0.05f, (i % 2) * 0.04f - 0.02f); }
                yield return null;
            }
            if (rattle != null) Destroy(rattle.gameObject);
            shaker.localPosition = new Vector3(1.35f, TableY, 0.35f);
            for (int i = 0; i < 3; i++) { dice[i].localRotation = Quaternion.Euler(0, Random.Range(0, 360f), 0) * FaceUp(res[i]); dice[i].localPosition = new Vector3(-0.06f + i * 0.06f, 0.085f, (i % 2) * 0.04f - 0.02f); }
            Sfx.Play("dice_land", shaker.position, 1f);
            // lift the dome
            for (float t = 0; t < 0.5f; t += Time.deltaTime) { dome.localPosition = Vector3.up * Fx.EaseOut(t / 0.5f) * 0.25f; yield return null; }
            int total = a + b + c;
            bool trip = Triple(a, b, c);
            resultT.text = a + " · " + b + " · " + c + "\n<size=140%>" + total + "</size>";
            Status(trip ? "TRIPLE " + a + "!" : (total <= 10 ? "SMALL " : "BIG ") + total, trip ? Palette.Magenta : Palette.Gold);
            Say(trip ? "sicbo_triple" : total <= 10 ? "sicbo_small" : "sicbo_big");
            foreach (var kv in rule) if (kv.Value(a, b, c) > 0f) RaisedBoard.Highlight(kv.Key, true, Palette.Gold);
            yield return new WaitForSeconds(1.2f);
            yield return StartCoroutine(Settle(s => rule[s](a, b, c)));
            for (float t = 0; t < 0.4f; t += Time.deltaTime) { dome.localPosition = Vector3.up * (1f - t / 0.4f) * 0.25f; yield return null; }
            dome.localPosition = Vector3.zero;
            foreach (var s in spots) RaisedBoard.Highlight(s, false, Palette.Gold);
            shakeBtn.SetInteractable(true);
            Status("PLACE YOUR BETS", Palette.Text);
        }
    
        protected override void OnRoundAborted()
        {
            if (shakeBtn != null) shakeBtn.SetInteractable(true);
            if (dome != null) dome.localPosition = Vector3.zero;
            foreach (var s in spots) if (s != null) RaisedBoard.Highlight(s, false, Palette.Gold);
        }
    }

    /// <summary>A printed betting layout tilted toward the seated player (far edge raised) on a built-in wedge riser, so
    /// every zone of a long board reads from the stool. Everything printed on it (felt, BetSpots, labels, markers) is
    /// parented to the pivot, so cells, trigger colliders, highlights and TableGame.SpotAt (which works in each spot's own
    /// local space) stay locked to the print. The pivot sits on the hinge line (the near edge, at felt height); its local
    /// XZ plane is the tilted felt, local X matches the table's X and local +Z runs up the slope, away from the player.</summary>
    public static class RaisedBoard
    {
        public static Transform Create(Transform table, Vector3 hinge, float deg)
        {
            var go = new GameObject("RaisedBoard");
            go.transform.SetParent(table, false);
            go.transform.localPosition = hinge;
            go.transform.localRotation = Quaternion.Euler(-deg, 0, 0);   // negative X tilt raises +Z (the far edge)
            return go.transform;
        }

        /// <summary>Height (above the hinge) and table-local Z of a point `along` metres up the slope.</summary>
        public static Vector2 Rise(float along, float deg) { float r = deg * Mathf.Deg2Rad; return new Vector2(along * Mathf.Sin(r), along * Mathf.Cos(r)); }

        /// <summary>The solid wedge under a board (x0..x1 in table X, `depth` metres of slope), gold trim on its edges and
        /// a thin solid deck so loose chips rest on the print instead of sinking into the riser.</summary>
        public static void Riser(Transform table, Transform board, float x0, float x1, float depth, float deg, Material mat, Color body, Color trim)
        {
            var top = Rise(depth, deg);
            float h = top.x, zf = top.y, xc = (x0 + x1) * 0.5f, half = (x1 - x0) * 0.5f;
            var hp = board.localPosition;
            var k = new MeshKit();
            // right-angled wedge: hypotenuse 3 mm under the felt, back face straight down to the table top.
            // Prism extrudes along its local Y, so each half is turned on its side (local Y → ±X).
            k.Push(new Vector3(xc, hp.y - 0.003f, hp.z), Quaternion.Euler(0, 0, -90));    // (x, y, z) → (y, -x, z)
            k.Prism(new List<Vector2> { new Vector2(0, 0), new Vector2(0, zf), new Vector2(-h, zf) }, 0f, half, body, 0f);
            k.Pop();
            k.Push(new Vector3(xc, hp.y - 0.003f, hp.z), Quaternion.Euler(0, 0, 90));     // (x, y, z) → (-y, x, z)
            k.Prism(new List<Vector2> { new Vector2(0, 0), new Vector2(h, zf), new Vector2(0, zf) }, 0f, half, body, 0f);
            k.Pop();
            k.Spawn("Riser", table, mat, false);
            var t = new MeshKit();
            t.Bar(new Vector3(x0, 0.004f, 0.004f), new Vector3(x0, 0.004f, depth), 0.012f, trim, 1f);
            t.Bar(new Vector3(x1, 0.004f, 0.004f), new Vector3(x1, 0.004f, depth), 0.012f, trim, 1f);
            t.Bar(new Vector3(x0, 0.004f, depth), new Vector3(x1, 0.004f, depth), 0.012f, trim, 1f);
            t.Spawn("Trim", board, Palette.Neon, false);
            var deck = board.gameObject.AddComponent<BoxCollider>();
            deck.center = new Vector3(xc, -0.009f, depth * 0.5f); deck.size = new Vector3(x1 - x0, 0.014f, depth);
        }

        /// <summary>BetSpot.Highlight lays its glow flat in world space; on a tilted board it is turned to lie on the felt.</summary>
        public static void Highlight(BetSpot s, bool on, Color c)
        {
            s.Highlight(on, c);
            var g = s.transform.Find("Glow");
            if (g != null) g.rotation = s.transform.rotation * Quaternion.Euler(90, 0, 0);
        }
    }
}
