using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>European roulette (single zero) with a real layout: drop chips on any number, line, column, dozen or outside bet,
    /// press SPIN, the croupier launches the ball, calls the number and settles the table. 37 pockets, RTP 97.3% (shown).</summary>
    public class Roulette : TableGame
    {
        /// <summary>Pocket order of the European wheel (clockwise from the single green zero).</summary>
        public static readonly int[] Order = { 0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26 };
        public static string Label(int n) { return n.ToString(); }
        static bool IsZero(int n) { return n == 0; }
        static readonly HashSet<int> Reds = new HashSet<int> { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };
        public static bool IsRed(int n) { return Reds.Contains(n); }

        // 3 cm left of where it was: the raised layout's right wall clears the wheel bowl
        static readonly Vector3 FeltC = new Vector3(-0.33f, 0, 0.05f);
        const float FeltW = 2.2f, FeltH = 1.1f;
        /// <summary>The betting grid leans toward the stool (far edge raised) so the 3 · 2 · 1 row and 2:1 columns read.</summary>
        const float Tilt = 12f;
        Transform board;
        static readonly Vector3 WheelC = new Vector3(1.3f, 0, 0.15f);
        const float WheelR = 0.4f;

        Transform wheel, ball, dolly, wheelRig;
        Vector3 rigHome;
        float wheelAngle;
        PokeButton spinBtn;
        TextMeshPro historyT;
        readonly List<int> history = new List<int>();
        readonly Dictionary<BetSpot, System.Func<int, bool>> rule = new Dictionary<BetSpot, System.Func<int, bool>>();
        BetSpot red;
        int zeros;

        public override bool AutoHidePanel { get { return true; } }
        // kept to ~8 rendered lines: the rules card turns every " · " into a new line
        protected override string Rules { get { return "STRAIGHT UP 36x, SPLIT 18x, STREET 12x\nCORNER 9x, SIX LINE 6x (CHIPS ON THE LINES)\nCOLUMN / DOZEN 3x\nRED, BLACK, ODD, EVEN, 1-18, 19-36 2x\nTHE GREEN 0 LOSES EVERY OUTSIDE BET\nDROP CHIPS ON THE LAYOUT (OR QUICK BET ON RED), THEN SPIN"; } }
        protected override Vector3 DealerPos { get { return new Vector3(0.75f, 0, 0.95f); } }
        protected override Vector3 TrayPos { get { return new Vector3(-0.3f, TableY + 0.02f, -0.62f); } }
        protected override Vector3 PanelPos { get { return new Vector3(0.62f, TableY + 0.1f, -0.78f); } }
        protected override BetSpot DefaultSpot { get { return red; } }
        protected override List<Vector4> SeatSpots
        {
            get { return new List<Vector4> { new Vector4(-1.45f, 0, -1.05f, 15f), new Vector4(-0.75f, 0, -1.2f, 5f), new Vector4(0.8f, 0, -1.2f, -5f), new Vector4(1.55f, 0, -1.0f, -20f), new Vector4(2.2f, 0, -0.35f, -60f) }; }
        }

        /// <summary>Print fractions (u across, y from the far edge) → board local (hinge on the near edge, +Z up the slope).</summary>
        Vector3 Cell(float u0, float y0, float du, float dy)
        {
            float u = u0 + du * 0.5f, y = y0 + dy * 0.5f;
            return new Vector3(FeltC.x + (u - 0.5f) * FeltW, 0.004f, (1f - y) * FeltH);
        }
        Vector2 Size(float du, float dy) { return new Vector2(du * FeltW * 0.92f, dy * FeltH * 0.92f); }

        BetSpot Spot(string id, float pays, float u0, float y0, float du, float dy, System.Func<int, bool> wins)
        {
            var s = BetSpot.Create(this, id, pays, board, Cell(u0, y0, du, dy), Size(du, dy));
            spots.Add(s); rule[s] = wins;
            return s;
        }

        protected override void BuildTable()
        {
            var k = new MeshKit();
            // table body with a padded rail
            k.Box(new Vector3(0.2f, TableY * 0.5f - 0.05f, 0.1f), new Vector3(3.3f, TableY - 0.1f, 1.4f), Palette.Hex("120D1C"));
            k.Box(new Vector3(0.2f, TableY - 0.03f, 0.1f), new Vector3(3.4f, 0.06f, 1.5f), Palette.Hex("1B1328"));
            k.Bar(new Vector3(-1.5f, TableY - 0.0f, -0.66f), new Vector3(1.9f, TableY, -0.66f), 0.08f, Palette.Hex("2A1D10"));
            k.Bar(new Vector3(-1.5f, TableY + 0.045f, -0.66f), new Vector3(1.9f, TableY + 0.045f, -0.66f), 0.012f, Palette.Gold, 1f);
            k.Bar(new Vector3(-1.5f, 0.06f, -0.6f), new Vector3(1.9f, 0.06f, -0.6f), 0.02f, Accent, 1f);
            k.Spawn("Table", transform, Palette.Glossy, false);
            // the printed layout on a built-in riser, hinged on its near edge and raised at the back (the wheel stays flat)
            board = RaisedBoard.Create(transform, new Vector3(0, TableY, FeltC.z - FeltH * 0.5f), Tilt);
            RaisedBoard.Riser(transform, board, FeltC.x - FeltW * 0.5f, FeltC.x + FeltW * 0.5f, FeltH, Tilt, Palette.Glossy, Palette.Hex("1B1328"), Palette.Gold);
            var felt = new MeshKit();
            felt.Plane(new Vector3(FeltC.x, 0.001f, FeltH * 0.5f), new Vector2(FeltW, FeltH), Quaternion.identity, Color.white, 0.15f);
            felt.Spawn("Felt", board, Palette.Textured("felt_roulette"), false);

            // wheel: bowl + rotating numbered wheel + spokes, ball
            // the whole wheel assembly lives on a rig that rises to eye level and turns to face you on every spin
            rigHome = WheelC + new Vector3(0, TableY, 0);
            wheelRig = Build.Empty("WheelRig", transform, rigHome).transform;
            var bowl = new MeshKit();
            bowl.Cylinder(new Vector3(0, 0.03f, 0), WheelR + 0.1f, WheelR + 0.12f, 0.08f, 48, Quaternion.identity, Palette.Hex("2A1D10"), 0f, true, Palette.Hex("120B08"));
            bowl.Torus(new Vector3(0, 0.075f, 0), WheelR + 0.1f, 0.012f, 48, 6, Quaternion.identity, Palette.Gold, 1f);
            bowl.Cylinder(new Vector3(0, -0.12f, 0), 0.06f, 0.09f, 0.2f, 16, Quaternion.identity, Palette.Gold, 0.4f);   // lift column
            bowl.Spawn("Bowl", wheelRig, Palette.Glossy, false);
            var wk = new MeshKit();
            wk.Disc(Vector3.zero, WheelR, 64, Quaternion.identity, Color.white, 0.25f);
            // no turret over the centre: the ASCEND medallion printed on the wheel stays visible
            wk.Torus(Vector3.up * 0.004f, WheelR * 0.42f, 0.006f, 48, 4, Quaternion.identity, Palette.Gold, 1f);
            wheel = wk.Spawn("Wheel", wheelRig, new Vector3(0, 0.075f, 0), Quaternion.identity, Palette.Textured("wheel", 0.3f), false).transform;
            var bk = new MeshKit(); bk.Sphere(Vector3.zero, 0.013f, 12, Palette.Text, 0.8f);
            ball = bk.Spawn("Ball", wheelRig, Palette.Neon, false).transform;
            ball.localPosition = new Vector3(0, 0.09f, WheelR + 0.06f);
            var dk = new MeshKit(); dk.Cylinder(Vector3.zero, 0.022f, 0.016f, 0.06f, 14, Quaternion.identity, Palette.Gold, 1f);
            dolly = dk.Spawn("Dolly", transform, Palette.Neon, false).transform;
            dolly.gameObject.SetActive(false);

            // bet spots matching the felt texture
            for (int c = 0; c < 12; c++)
                for (int r = 0; r < 3; r++)
                {
                    int num = c * 3 + (3 - r);
                    int n = num;
                    Spot(num.ToString(), 36f, 0.12f + c * 0.06f, 0.10f + r * 0.2f, 0.06f, 0.2f, x => x == n);
                }
            Spot("0", 36f, 0.06f, 0.10f, 0.06f, 0.6f, x => x == 0);
            // inside bets on the lines: splits (18x), corners (9x), streets (12x) and six lines (6x)
            System.Func<int, int, int> Num = (c, r) => c * 3 + (3 - r);
            const float lu = 0.022f, lv = 0.07f;
            for (int c = 0; c < 12; c++)
                for (int r = 0; r < 3; r++)
                {
                    int a = Num(c, r);
                    if (c < 11) { int b = Num(c + 1, r); Spot("SPLIT " + a + "/" + b, 18f, 0.12f + (c + 1) * 0.06f - lu * 0.5f, 0.10f + r * 0.2f + 0.1f - lv * 0.5f, lu, lv, x => x == a || x == b); }
                    if (r < 2) { int b = Num(c, r + 1); Spot("SPLIT " + b + "/" + a, 18f, 0.12f + c * 0.06f + 0.03f - lv * 0.25f, 0.10f + (r + 1) * 0.2f - lu * 0.5f, lv * 0.5f, lu, x => x == a || x == b); }
                    if (c < 11 && r < 2)
                    {
                        int b = Num(c + 1, r), d = Num(c, r + 1), e = Num(c + 1, r + 1);
                        Spot("CORNER " + Mathf.Min(Mathf.Min(a, b), Mathf.Min(d, e)), 9f, 0.12f + (c + 1) * 0.06f - lu * 0.5f, 0.10f + (r + 1) * 0.2f - lu * 0.5f, lu, lu, x => x == a || x == b || x == d || x == e);
                    }
                }
            for (int c = 0; c < 12; c++)
            {
                int lo = c * 3 + 1;
                Spot("STREET " + lo + "-" + (lo + 2), 12f, 0.12f + c * 0.06f + 0.015f, 0.70f - lu * 0.5f, 0.03f, lu, x => x >= lo && x <= lo + 2);
                if (c < 11) Spot("SIX LINE " + lo + "-" + (lo + 5), 6f, 0.12f + (c + 1) * 0.06f - lu * 0.5f, 0.70f - lu * 0.5f, lu, lu, x => x >= lo && x <= lo + 5);
            }
            for (int r = 0; r < 3; r++) { int mod = (3 - r) % 3; Spot("COL" + r, 3f, 0.84f, 0.10f + r * 0.2f, 0.06f, 0.2f, x => !IsZero(x) && x % 3 == mod); }
            for (int d = 0; d < 3; d++) { int lo = d * 12 + 1; Spot("DOZ" + d, 3f, 0.12f + d * 0.24f, 0.70f, 0.24f, 0.1f, x => x >= lo && x <= lo + 11); }
            Spot("1-18", 2f, 0.12f, 0.80f, 0.12f, 0.1f, x => x >= 1 && x <= 18);
            Spot("EVEN", 2f, 0.24f, 0.80f, 0.12f, 0.1f, x => !IsZero(x) && x % 2 == 0);
            red = Spot("RED", 2f, 0.36f, 0.80f, 0.12f, 0.1f, x => !IsZero(x) && IsRed(x));
            Spot("BLACK", 2f, 0.48f, 0.80f, 0.12f, 0.1f, x => !IsZero(x) && !IsRed(x));
            Spot("ODD", 2f, 0.60f, 0.80f, 0.12f, 0.1f, x => x % 2 == 1);
            Spot("19-36", 2f, 0.72f, 0.80f, 0.12f, 0.1f, x => x >= 19 && x <= 36);

            spinBtn = PokeButton.Create("Spin", transform, new Vector3(0.95f, TableY + 0.05f, -0.62f), Quaternion.identity, 0.055f, Palette.Gold, "SPIN", () => StartCoroutine(Spin()), 0.045f);
            historyT = Txt.Make("History", transform, WheelC + new Vector3(0, 2.3f, 0.35f), "", 0.07f, Palette.Text, Palette.Display, TextAlignmentOptions.Center, 1.4f);
            Build.Beam(transform, new Vector3(0, 3.6f, 0.05f), Quaternion.identity, 2.6f, 1.4f, Accent, 0.12f);
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Label(p, "QUICK BETS GO ON RED · OR DROP CHIPS ANYWHERE", 20, y, 520, 30, 18, Palette.Muted, Palette.Mono);
            UIKit.Btn(p, "SPIN", 20, y + 40, 520, 90, Palette.Gold, () => StartCoroutine(Spin()), 44);
        }

        /// <summary>Lifts the wheel off the table to the player's eye line, tilted and turned to face them (and back down).</summary>
        IEnumerator RaiseWheel(bool up)
        {
            var p0 = wheelRig.localPosition; var r0 = wheelRig.localRotation; var s0 = wheelRig.localScale;
            Vector3 p1 = rigHome; Quaternion r1 = Quaternion.identity; Vector3 s1 = Vector3.one;
            if (up)
            {
                p1 = new Vector3(WheelC.x - 0.25f, TableY + 0.68f, WheelC.z + 0.1f);   // tilted 58° its lower rim clears the raised layout
                var viewer = Rig.Ready ? transform.InverseTransformPoint(Rig.I.Head.position) : new Vector3(0, 1.4f, -1.3f);
                var d = viewer - p1; d.y = 0;
                float yaw = Mathf.Atan2(-d.x, -d.z) * Mathf.Rad2Deg;
                r1 = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(-58f, 0, 0);
                s1 = Vector3.one * 1.35f;
                Sfx.Play("lever_pull", wheelRig.position, 0.4f, 0.7f);
            }
            for (float t = 0; t < 0.9f; t += Time.deltaTime)
            {
                float k = Fx.EaseInOut(t / 0.9f);
                wheelRig.localPosition = Vector3.Lerp(p0, p1, k) + (up ? Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.06f : Vector3.zero);
                wheelRig.localRotation = Quaternion.Slerp(r0, r1, k);
                wheelRig.localScale = Vector3.Lerp(s0, s1, k);
                yield return null;
            }
            wheelRig.localPosition = p1; wheelRig.localRotation = r1; wheelRig.localScale = s1;
        }

        IEnumerator Spin()
        {
            if (!BeginRound()) yield break;
            spinBtn.SetInteractable(false);
            dolly.gameObject.SetActive(false);
            Status("NO MORE BETS", Palette.Gold);
            float d = Say("no_more");
            yield return new WaitForSeconds(Mathf.Min(d, 1.0f));
            StartCoroutine(Reach(W(WheelC + new Vector3(0.2f, TableY + 0.1f, 0.1f)), 0.7f));
            yield return new WaitForSeconds(0.35f);

            yield return StartCoroutine(RaiseWheel(true));
            int idx = Odds.Range(0, Order.Length);
            int number = Order[idx];
            var wheelLoop = Sfx.Loop("wheel_spin_loop", wheel, 0.6f);
            var ballLoop = Sfx.Loop("ball_roll_loop", ball, 0.7f);
            float a0 = wheelAngle;
            float dur = 6.2f;
            float targetWheel = a0 + 360f * 3f + Random.Range(0f, 360f);
            float pocketAngle = idx * 360f / Order.Length;
            float ball0 = Random.Range(0f, 360f);
            float ballEndWorld = targetWheel + pocketAngle;   // pocket's angle when the wheel stops
            float ballSpins = 7f;
            float ballStart = ballEndWorld + ballSpins * 360f;   // ball runs the opposite way (decreasing angle)
            var c = new Vector3(0, 0.085f, 0);
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float k = Fx.EaseOut(t / dur);
                wheelAngle = Mathf.Lerp(a0, targetWheel, k);
                wheel.localRotation = Quaternion.Euler(0, wheelAngle, 0);
                float bk = Fx.EaseOut4(Mathf.Clamp01(t / (dur * 0.92f)));
                float ba = Mathf.Lerp(ballStart, ballEndWorld, bk);
                float r = Mathf.Lerp(WheelR + 0.07f, WheelR * 0.86f, Mathf.Clamp01((t - dur * 0.55f) / (dur * 0.3f)));
                float hop = (t > dur * 0.55f && t < dur * 0.85f) ? Mathf.Abs(Mathf.Sin(t * 19f)) * 0.025f : 0f;
                ball.localPosition = c + Quaternion.Euler(0, ba, 0) * new Vector3(0, 0.01f + hop, r);
                if (ballLoop != null) ballLoop.volume = 0.7f * (1f - k * 0.8f);
                if (t > dur * 0.55f && t - Time.deltaTime <= dur * 0.55f) Sfx.Play("ball_drop", ball.position, 0.9f);
                yield return null;
            }
            wheelAngle = targetWheel;
            if (wheelLoop != null) Destroy(wheelLoop.gameObject);
            if (ballLoop != null) Destroy(ballLoop.gameObject);
            ball.localPosition = c + Quaternion.Euler(0, ballEndWorld, 0) * new Vector3(0, 0.01f, WheelR * 0.86f);
            Sfx.Play("ball_tick", ball.position, 1f);

            string col = IsZero(number) ? "GREEN" : IsRed(number) ? "RED" : "BLACK";
            Status(Label(number) + " " + col, IsZero(number) ? Palette.Mint : IsRed(number) ? Palette.Red : Palette.Text);
            Say("num" + number);
            history.Insert(0, number); if (history.Count > 12) history.RemoveAt(history.Count - 1);
            var sb = new System.Text.StringBuilder();
            foreach (var h in history) sb.Append("<color=#").Append(IsZero(h) ? "5CFFB0" : IsRed(h) ? "E0314B" : "F2EEFA").Append(">").Append(Label(h)).Append("</color>  ");
            historyT.text = sb.ToString();
            if (IsZero(number)) { zeros++; if (zeros >= 3) GameState.I.Unlock("ZERO HERO", "THE BALL LANDED ON ZERO 3 TIMES", 300); }

            // dolly on the winning number
            BetSpot win = null;
            foreach (var s in spots) if (s.id == Label(number)) win = s;
            if (win != null)
            {
                // the dolly drops along the cell's own normal and stands square on the tilted layout
                dolly.rotation = win.transform.rotation;
                dolly.position = win.transform.position + win.transform.up * 0.25f;
                dolly.gameObject.SetActive(true);
                StartCoroutine(Fx.Move(dolly, win.transform.position + win.transform.up * 0.03f, 0.4f, false));
                RaisedBoard.Highlight(win, true, Palette.Gold);
            }
            yield return new WaitForSeconds(1.6f);
            StartCoroutine(RaiseWheel(false));
            yield return StartCoroutine(Settle(s => rule[s](number) ? s.pays : 0f));
            if (win != null) RaisedBoard.Highlight(win, false, Palette.Gold);
            Status("PLACE YOUR BETS", Palette.Text);
            spinBtn.SetInteractable(true);
            if (Random.value < 0.5f) Say("bets_open");
        }
    
        protected override void OnRoundAborted()
        {
            if (wheelRig != null) { wheelRig.localPosition = rigHome; wheelRig.localRotation = Quaternion.identity; wheelRig.localScale = Vector3.one; }
            if (spinBtn != null) spinBtn.SetInteractable(true);
            if (dolly != null) dolly.gameObject.SetActive(true);
            foreach (var s in spots) if (s != null) RaisedBoard.Highlight(s, false, Palette.Gold);
        }
    }
}
