using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Plinko: grab a ball from the hopper and drop it in the tube (or press DROP). 8 rows, every peg 50/50.
    /// LOW risk 5.6x edges / HIGH risk 29x edges, both RTP 99%.</summary>
    public class Plinko : ArcadeStation
    {
        const int Rows = 8;
        static readonly float[] Low = { 5.6f, 2.1f, 1.1f, 1f, 0.5f, 1f, 1.1f, 2.1f, 5.6f };
        static readonly float[] High = { 29f, 4f, 1.5f, 0.3f, 0.2f, 0.3f, 1.5f, 4f, 29f };
        bool high;
        float[] Mult { get { return high ? High : Low; } }
        const float DX = 0.16f, DY = 0.15f, TopY = 2.65f, BoardZ = 0.62f;
        readonly List<Renderer> bins = new List<Renderer>();
        readonly List<TextMeshPro> binLabels = new List<TextMeshPro>();
        TextMeshPro riskT;
        Transform tube;
        int inFlight;

        protected override string Rules { get { return "EVERY PEG SENDS THE BALL LEFT OR RIGHT\nLOW RISK 5.6x MAX · HIGH RISK 29x MAX\nGRAB A BALL, DROP IT IN THE TUBE"; } }
        protected override Vector3 Intake { get { return tube != null ? tube.position : base.Intake; } }

        protected override void BuildMachine()
        {
            var k = new MeshKit();
            k.Box(new Vector3(0, 1.85f, BoardZ + 0.05f), new Vector3(1.6f, 1.75f, 0.06f), Palette.Hex("08060F"), 0.02f);
            for (int s = -1; s <= 1; s += 2) k.Bar(new Vector3(s * 0.8f, 0.95f, BoardZ), new Vector3(s * 0.8f, 2.75f, BoardZ), 0.03f, Accent, 1f);
            k.Bar(new Vector3(-0.8f, 2.75f, BoardZ), new Vector3(0.8f, 2.75f, BoardZ), 0.03f, Accent, 1f);
            for (int r = 0; r < Rows; r++)
                for (int j = 0; j <= r + 2; j++)
                {
                    float x = (j - (r + 2) * 0.5f) * DX;
                    k.Sphere(new Vector3(x, TopY - (r + 0.5f) * DY, BoardZ - 0.02f), 0.016f, 8, Palette.Text, 0.9f);
                }
            // tube up the side
            k.Cylinder(new Vector3(0.72f, 1.8f, BoardZ - 0.08f), 0.045f, 0.045f, 1.7f, 16, Quaternion.identity, Palette.Teal, 0.25f, false);
            k.Spawn("Board", transform, Palette.Neon, false);
            for (int b = 0; b <= Rows; b++)
            {
                float x = (b - Rows * 0.5f) * DX;
                var bk = new MeshKit(); bk.Box(Vector3.zero, new Vector3(DX * 0.88f, 0.09f, 0.05f), Color.white, 0.8f);
                var go = bk.Spawn("Bin" + b, transform, new Vector3(x, TopY - (Rows + 0.8f) * DY, BoardZ - 0.03f), Quaternion.identity, Palette.Neon, false);
                bins.Add(go.GetComponent<Renderer>());
                binLabels.Add(Txt.Make("BinLabel" + b, go.transform, new Vector3(0, 0, -0.03f), "", 0.045f, Palette.Bg, Palette.Display));
            }
            Txt.Glow("Title", transform, new Vector3(0, 2.92f, BoardZ), "PLINKO", 0.22f, Accent);
            riskT = Txt.Make("Risk", transform, new Vector3(-0.55f, 2.92f, BoardZ), "", 0.06f, Palette.Gold, Palette.Display);
            Paint();
            // tube mouth on the counter + ball hopper
            var tm = new MeshKit();
            tm.Cylinder(Vector3.zero, 0.06f, 0.07f, 0.12f, 18, Quaternion.identity, Palette.Teal, 0.4f, false);
            tm.Torus(new Vector3(0, 0.06f, 0), 0.065f, 0.008f, 18, 5, Quaternion.identity, Palette.Teal, 1f);
            tube = tm.Spawn("TubeMouth", transform, new Vector3(0.48f, TableY + 0.09f, -0.22f), Quaternion.identity, Palette.Neon, false).transform;
            var trig = tube.gameObject.AddComponent<SphereCollider>(); trig.isTrigger = true; trig.radius = 0.07f;
            tube.gameObject.AddComponent<PlinkoIntake>().game = this;
            Txt.Make("TubeLabel", transform, new Vector3(0.48f, TableY + 0.2f, -0.22f), "DROP\nBALL", 0.03f, Palette.Teal, Palette.Display).gameObject.AddComponent<Billboard>();
            var hopper = new MeshKit(); hopper.Box(Vector3.zero, new Vector3(0.24f, 0.04f, 0.16f), Palette.Dark); hopper.Bar(new Vector3(-0.12f, 0.02f, -0.08f), new Vector3(0.12f, 0.02f, -0.08f), 0.008f, Palette.Gold, 1f);
            hopper.Spawn("Hopper", transform, new Vector3(-0.62f, TableY + 0.05f, -0.3f), Quaternion.identity, Palette.Neon, false).AddComponent<BoxCollider>();
            // the station's own collider stops 3 cm under the counter top, so loose balls would sink into the slab and vanish:
            // give the counter surface a real collider (on a child — the root's boxes are replaced by the fitted hull)
            var topCol = Build.Empty("CounterTopCollider", transform, new Vector3(0, TableY + 0.015f, -0.22f)).AddComponent<BoxCollider>();
            topCol.size = new Vector3(CounterWidth + 0.06f, 0.03f, 0.5f);
            for (int i = 0; i < 3; i++) SpawnBall(new Vector3(-0.68f + i * 0.06f, TableY + 0.1f, -0.3f));
            PokeButton.Create("Drop", transform, new Vector3(0.3f, TableY + 0.035f, -0.32f), Quaternion.identity, 0.045f, Palette.Gold, "DROP", () => Drop(null));
            PokeButton.Create("Risk", transform, new Vector3(-0.15f, TableY + 0.035f, -0.38f), Quaternion.identity, 0.03f, Palette.Magenta, "RISK", ToggleRisk);
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "DROP BALL", 20, y, 520, 80, Palette.Gold, () => Drop(null), 36);
            UIKit.Btn(p, "RISK: LOW / HIGH", 20, y + 90, 520, 60, Palette.Magenta, ToggleRisk, 28);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 370); } }

        void ToggleRisk() { if (inFlight > 0) return; high = !high; Paint(); Sfx.UI("ui_click", 0.5f); }

        void Paint()
        {
            riskT.text = high ? "HIGH RISK" : "LOW RISK";
            for (int b = 0; b < bins.Count; b++)
            {
                float m = Mult[b];
                Fx.SetColor(bins[b], m >= 5 ? Palette.Coral : m >= 2 ? Palette.Magenta : m >= 1 ? Palette.Gold : Palette.Teal, 0.8f);
                binLabels[b].text = m.ToString("0.#") + "x";
            }
        }

        public void SpawnBall(Vector3 local)
        {
            var bk = new MeshKit(); bk.Sphere(Vector3.zero, 0.028f, 12, Palette.Gold, 0.8f);
            var go = bk.Spawn("Ball", transform, local, Quaternion.identity, Palette.Neon, false);
            go.AddComponent<SphereCollider>().radius = 0.028f;
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.05f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            var g = go.AddComponent<XRGrabInteractable>(); g.throwOnDetach = true; g.movementType = XRBaseInteractable.MovementType.Instantaneous;
            go.AddComponent<PlinkoBall>().game = this;
        }

        public void Drop(PlinkoBall physical)
        {
            if (inFlight >= 6) return;
            long stake = Take();
            if (stake <= 0) return;
            if (physical != null)
            {
                Destroy(physical.gameObject);
                StartCoroutine(RespawnBall());
            }
            Say("drop");
            StartCoroutine(Ball(stake));
        }

        IEnumerator RespawnBall() { yield return new WaitForSeconds(0.6f); SpawnBall(new Vector3(-0.62f, TableY + 0.1f, -0.3f)); }

        IEnumerator Ball(long stake)
        {
            inFlight++;
            float[] mult = (float[])Mult.Clone();
            var bk = new MeshKit(); bk.Sphere(Vector3.zero, 0.03f, 12, Palette.Gold, 1f);
            var ball = bk.Spawn("Flight", transform, new Vector3(0.72f, 0.95f, BoardZ - 0.08f), Quaternion.identity, Palette.Neon, false).transform;
            Build.GlowSprite(ball, Vector3.zero, 0.15f, Palette.Gold, 0.8f);
            // up the tube
            yield return StartCoroutine(Fx.Move(ball, new Vector3(0.72f, TopY + 0.1f, BoardZ - 0.08f), 0.45f));
            yield return StartCoroutine(Fx.Move(ball, new Vector3(0, TopY + 0.05f, BoardZ - 0.05f), 0.25f));
            int rights = 0; float xPrev = 0, yPrev = TopY + 0.05f;
            for (int r = 0; r < Rows; r++)
            {
                if (Odds.R() < 0.5) rights++;
                float xNext = (rights - (r + 1) * 0.5f) * DX;
                float yNext = TopY - (r + 1) * DY;
                float dur = 0.17f;
                for (float t = 0; t < dur; t += Time.deltaTime)
                {
                    float k = t / dur;
                    ball.localPosition = new Vector3(Mathf.Lerp(xPrev, xNext, k), Mathf.Lerp(yPrev, yNext, k * k) + Mathf.Sin(k * Mathf.PI) * 0.035f, BoardZ - 0.05f);
                    yield return null;
                }
                Sfx.Play("peg", ball.position, 0.6f, 0.9f + r * 0.06f);
                XRIn.Haptic(Hand.Right, 0.08f, 0.01f);
                xPrev = xNext; yPrev = yNext;
            }
            int bin = rights;
            ball.localPosition = new Vector3((bin - Rows * 0.5f) * DX, TopY - (Rows + 0.6f) * DY, BoardZ - 0.05f);
            Sfx.Play("plinko_bin", ball.position, 0.9f);
            var br = bins[bin];
            StartCoroutine(Fx.Pulse(br.transform, 1.3f, 0.3f));
            float m = mult[bin];
            Status(m.ToString("0.#") + "x", m > 1 ? Palette.Mint : m == 1 ? Palette.Text : Palette.Muted);   // a push is not shown as a win
            PayDirect((long)(stake * (double)m), stake, ball.position);   // (the play was already recorded by ConsumeStake)
            yield return new WaitForSeconds(0.3f);
            Destroy(ball.gameObject);
            inFlight--;
        }
    }
}
