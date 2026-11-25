using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Keno: touch up to 10 of the 80 numbers on the big board (or QUICK PICK), press DRAW. 20 balls are drawn
    /// from the air blower; matches pay from the table below. The RTP of every pick count is computed from the
    /// hypergeometric odds and printed next to the paytable.</summary>
    public class Keno : ArcadeStation
    {
        // pays[picks][hits] = total return per 1 bet (1x = stake back). Missing every number never pays, and each pick count
        // only pays from a sensible minimum of hits. Exact RTPs (hypergeometric: 80 numbers, 20 drawn), as printed by RTP():
        //  1 pick 87.50% · 2 picks 90.19% · 3 picks 90.19% · 4 picks 88.84% · 5 picks 88.46%
        //  6 picks 89.16% · 7 picks 89.48% · 8 picks 89.05% · 9 picks 87.82% · 10 picks 88.03%
        static readonly float[][] Pays =
        {
            null,
            new float[] { 0, 3.5f },                                   // 1 of 1
            new float[] { 0, 0, 15 },                                  // 2 of 2
            new float[] { 0, 0, 2, 45 },                               // 2+ of 3
            new float[] { 0, 0, 1, 5, 150 },                           // 2+ of 4
            new float[] { 0, 0, 0, 3, 15, 700 },                       // 3+ of 5
            new float[] { 0, 0, 0, 1, 5, 75, 3000 },                   // 3+ of 6
            new float[] { 0, 0, 0, 1, 3, 20, 300, 7000 },              // 3+ of 7
            new float[] { 0, 0, 0, 0, 2, 10, 110, 1500, 10000 },       // 4+ of 8
            new float[] { 0, 0, 0, 0, 1, 5, 50, 300, 4000, 10000 },    // 4+ of 9
            new float[] { 0, 0, 0, 0, 0, 2, 22, 180, 1500, 5000, 10000 },   // 5+ of 10
        };
        /// <summary>The paid multiplier, exactly as the code pays it (no hits never pays).</summary>
        public static float Multiplier(int picks, int hits)
        {
            if (picks < 1 || picks > 10 || hits <= 0) return 0f;
            return Pays[picks][Mathf.Min(hits, picks)];
        }
        static string X(float m) { return (m >= 1000f ? m.ToString("N0") : m.ToString("0.##")) + "x"; }
        readonly bool[] picked = new bool[80];
        readonly bool[] drawnSet = new bool[80];
        readonly Renderer[] cellR = new Renderer[80];
        readonly Transform[] cells = new Transform[80];
        int picks;
        bool drawing;
        TextMeshPro payT, infoT, ballT;
        Transform blower;
        readonly List<Transform> balls = new List<Transform>();
        PokeButton drawBtn;

        public override bool AutoHidePanel { get { return true; } }
        protected override string Rules
        {
            get
            {
                // computed from the same table the game pays from; the rules card makes every " · " a new line
                var sb = new System.Text.StringBuilder("PICK 1–10 OF 80 NUMBERS, 20 BALLS ARE DRAWN\nPAYS DEPEND ON PICKS AND HITS (TABLE BESIDE THE BOARD), NO HITS NEVER PAYS");
                sb.Append("\nQUICK PICK CHOOSES 6, CLEAR TO START AGAIN");
                return sb.ToString();
            }
        }
        protected override float CounterWidth { get { return 1.6f; } }
        // above the board: at the default height the status and bet lines were printed over the numbers
        protected override Vector3 StatusPos { get { return new Vector3(0, 2.82f, 0.5f); } }
        /// <summary>The number board leans toward the player (top forward) around its centre line.</summary>
        const float BoardTilt = 10f;

        static double Choose(int n, int k)
        {
            if (k < 0 || k > n) return 0;
            double r = 1; for (int i = 1; i <= k; i++) r = r * (n - k + i) / i;
            return r;
        }

        public static double RTP(int p)
        {
            if (p < 1 || p > 10) return 0;
            double total = Choose(80, 20), ev = 0;
            for (int h = 0; h <= p; h++)
            {
                double prob = Choose(p, h) * Choose(80 - p, 20 - h) / total;
                ev += prob * Multiplier(p, h);
            }
            return ev;
        }

        protected override void BuildMachine()
        {
            // the big board (8 rows × 10), leaning toward the player around its centre so the top rows face the stool
            var pivot = Build.Empty("BoardPivot", transform, new Vector3(0, 1.85f, 0.62f)).transform;
            pivot.localRotation = Quaternion.Euler(-BoardTilt, 0, 0);   // top edge comes forward, face looks down at the player
            var b = new MeshKit();
            b.Box(Vector3.zero, new Vector3(1.75f, 1.35f, 0.06f), Palette.Hex("07050D"), 0f);
            for (int s = -1; s <= 1; s += 2) b.Bar(new Vector3(s * 0.88f, -0.68f, -0.04f), new Vector3(s * 0.88f, 0.68f, -0.04f), 0.015f, Accent, 1f);
            b.Bar(new Vector3(-0.88f, 0.68f, -0.04f), new Vector3(0.88f, 0.68f, -0.04f), 0.015f, Accent, 1f);
            b.Spawn("Board", pivot, Palette.Neon, false);
            for (int i = 0; i < 80; i++)
            {
                int r = i / 10, c = i % 10;
                var ck = new MeshKit();
                ck.Box(Vector3.zero, new Vector3(0.14f, 0.12f, 0.02f), Color.white, 0.35f);
                var go = ck.Spawn("N" + (i + 1), pivot, new Vector3(-0.72f + c * 0.16f, 0.53f - r * 0.14f, -0.035f), Quaternion.identity, Palette.Neon, false);
                cells[i] = go.transform; cellR[i] = go.GetComponent<Renderer>();
                Txt.Make("T", go.transform, new Vector3(0, 0, -0.012f), (i + 1).ToString(), 0.05f, Palette.Text, Palette.Display);
                go.AddComponent<BoxCollider>().size = new Vector3(0.15f, 0.13f, 0.06f);
                int idx = i;
                go.AddComponent<TileTouch>().onTouch = () => Toggle(idx);
                go.AddComponent<XRSimpleInteractable>().selectEntered.AddListener(_ => Toggle(idx));
            }
            // ball blower dome on the left
            blower = Build.Empty("Blower", transform, new Vector3(-1.15f, TableY + 0.25f, 0.35f)).transform;
            var bk = new MeshKit();
            bk.Cylinder(new Vector3(0, -0.12f, 0), 0.14f, 0.18f, 0.24f, 24, Quaternion.identity, Palette.Gold, 0.2f);
            bk.Spawn("Base", blower, Palette.Gold3, false);
            var gk = new MeshKit(); gk.Sphere(new Vector3(0, 0.12f, 0), 0.2f, 20, Palette.Hex("9FE8FF"), 0.15f);
            gk.Spawn("Glass", blower, Palette.Glow, false);
            for (int i = 0; i < 14; i++)
            {
                var sk = new MeshKit(); sk.Sphere(Vector3.zero, 0.025f, 10, i % 3 == 0 ? Palette.Gold : i % 3 == 1 ? Palette.Teal : Palette.Magenta, 0.6f);
                balls.Add(sk.Spawn("Ball" + i, blower, new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(0.02f, 0.2f), Random.Range(-0.1f, 0.1f)), Quaternion.identity, Palette.Neon, false).transform);
            }
            ballT = Txt.Glow("Ball", transform, new Vector3(-1.15f, 1.85f, 0.4f), "", 0.14f, Palette.Gold);
            // paytable on its own dark plate beside the board (11 lines max: header + 10 rows)
            var plate = new MeshKit();
            plate.Box(new Vector3(1.25f, 1.63f, 0.585f), new Vector3(0.68f, 0.72f, 0.02f), Palette.Hex("07050D"), 0f);
            plate.Bar(new Vector3(0.91f, 1.985f, 0.572f), new Vector3(1.59f, 1.985f, 0.572f), 0.01f, Accent, 1f);
            plate.Spawn("PayPlate", transform, Palette.Neon, false);
            payT = Txt.Make("Pay", transform, new Vector3(1.25f, 1.92f, 0.55f), "", 0.04f, Palette.Text, Palette.Mono, TextAlignmentOptions.TopLeft, 0.6f);
            infoT = Txt.Make("Info", transform, new Vector3(0, 1.08f, 0.55f), "", 0.05f, Palette.Gold, Palette.Display);
            PokeButton.Create("Quick", transform, new Vector3(-0.2f, TableY + 0.035f, -0.36f), Quaternion.identity, 0.035f, Palette.Teal, "QUICK", QuickPick);
            PokeButton.Create("Clear", transform, new Vector3(0.0f, TableY + 0.035f, -0.36f), Quaternion.identity, 0.03f, Palette.Red, "CLEAR", ClearPicks);
            drawBtn = PokeButton.Create("Draw", transform, new Vector3(0.38f, TableY + 0.035f, -0.32f), Quaternion.identity, 0.045f, Palette.Gold, "DRAW", () => StartCoroutine(Draw()));
            Refresh2();
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "QUICK PICK", 20, y, 250, 70, Palette.Teal, QuickPick, 28);
            UIKit.Btn(p, "CLEAR", 290, y, 250, 70, Palette.Red, ClearPicks, 28);
            UIKit.Btn(p, "DRAW", 20, y + 80, 520, 80, Palette.Gold, () => StartCoroutine(Draw()), 38);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 390); } }

        readonly float[] lastToggle = new float[80];

        void Toggle(int i)
        {
            if (drawing) return;
            // a finger touch and an interactor select can both fire for one press: don't let them pick and un-pick
            if (Time.time - lastToggle[i] < 0.3f && lastToggle[i] > 0f) return;
            lastToggle[i] = Time.time;
            if (picked[i]) { picked[i] = false; picks--; }
            else if (picks < 10) { picked[i] = true; picks++; Sfx.Play("ui_click", cells[i].position, 0.5f, 1f + picks * 0.03f); }
            else { GameState.I.Notify("10 NUMBERS MAX", Palette.Gold); return; }
            ResetDraw();
            Refresh2();
        }

        void QuickPick()
        {
            if (drawing) return;
            ClearPicks();
            int want = 6;
            while (picks < want) { int i = Odds.Range(0, 80); if (!picked[i]) { picked[i] = true; picks++; } }
            Sfx.Play("ui_open", cells[40].position, 0.6f);
            Refresh2();
        }

        void ClearPicks()
        {
            if (drawing) return;
            for (int i = 0; i < 80; i++) picked[i] = false;
            picks = 0; ResetDraw(); Refresh2();
        }

        void ResetDraw() { for (int i = 0; i < 80; i++) drawnSet[i] = false; }

        void Refresh2()
        {
            for (int i = 0; i < 80; i++)
            {
                bool hit = picked[i] && drawnSet[i];
                var c = hit ? Palette.Mint : picked[i] ? Palette.Gold : drawnSet[i] ? Palette.Magenta : Palette.Violet * 0.35f;
                Fx.SetColor(cellR[i], c, hit ? 1f : picked[i] || drawnSet[i] ? 0.75f : 0.25f);
            }
            var sb = new System.Text.StringBuilder();
            if (picks > 0)
            {
                sb.Append("PICK ").Append(picks).Append(" · PAYS\n");
                // exactly the multipliers Draw() pays
                for (int h = picks; h >= 1; h--) { float m = Multiplier(picks, h); if (m > 0) sb.Append(h < 10 ? " " : "").Append(h).Append(h == 1 ? " HIT   " : " HITS  ").Append(X(m)).Append('\n'); }
            }
            else
            {
                sb.Append("TOUCH 1–10 NUMBERS\n");
                sb.Append("MORE PICKS = BIGGER TOP PRIZES\n");
            }
            payT.text = sb.ToString();
            infoT.text = picks + " / 10 PICKED";
        }

        IEnumerator Draw()
        {
            if (drawing) yield break;
            if (picks == 0) { GameState.I.Notify("PICK SOME NUMBERS FIRST", Palette.Gold); yield break; }
            long stake = Take();
            if (stake <= 0) yield break;
            drawing = true; drawBtn.SetInteractable(false);
            ResetDraw(); Refresh2();
            var pool = new List<int>(); for (int i = 0; i < 80; i++) pool.Add(i);
            int hits = 0;
            var blow = Sfx.Loop("orb_whoosh_loop", blower, 0.4f);
            Say("keno_draw");
            for (int n = 0; n < 20; n++)
            {
                int j = Odds.Range(0, pool.Count); int num = pool[j]; pool.RemoveAt(j);
                for (float t = 0; t < 0.22f; t += Time.deltaTime)
                {
                    foreach (var b in balls) b.localPosition = new Vector3(Mathf.Sin(Time.time * 9f + b.GetInstanceID()) * 0.12f, 0.12f + Mathf.Sin(Time.time * 13f + b.GetInstanceID() * 0.3f) * 0.09f, Mathf.Cos(Time.time * 11f + b.GetInstanceID()) * 0.12f);
                    yield return null;
                }
                drawnSet[num] = true;
                bool hit = picked[num];
                if (hit) { hits++; Sfx.Play("gem", cells[num].position, 0.8f, 1f + hits * 0.06f); StartCoroutine(Fx.Pulse(cells[num], 1.3f, 0.25f)); }
                else Sfx.Play("ball_tick", cells[num].position, 0.4f);
                ballT.text = (num + 1).ToString();
                Refresh2();
                infoT.text = hits + " HITS · BALL " + (n + 1) + "/20";
            }
            if (blow != null) Destroy(blow.gameObject);
            float m = Multiplier(picks, hits);
            Status(hits + " OF " + picks + (m > 0 ? " · " + X(m) : ""), m > 1 ? Palette.Gold : m > 0 ? Palette.Text : Palette.Muted);
            if (m >= 2f) Say("keno_win");
            PayDirect((long)(stake * (double)m), stake, ballT.transform.position);
            yield return new WaitForSeconds(0.8f);
            drawing = false; drawBtn.SetInteractable(true);
        }
    
        protected override void OnRoundAborted() { drawing = false; if (drawBtn != null) drawBtn.SetInteractable(true); }
    }
}
