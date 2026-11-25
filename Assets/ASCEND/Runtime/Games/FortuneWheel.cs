using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Wheel of Fortune (rules from Roberto's "Gamble With Your Friends" rebuild): one bet, one spin, the pointer
    /// pays the multiplier it stops on. 20 segments: 0 ×1, 0.1 ×5, 0.25 ×4, 0.5 ×3, 2 ×2, 3 ×2, 5 ×1 and SPIN ×2 (free
    /// re-spin with the same stake). Expected return 100 %. 7 turns + a random angle over 7 s.</summary>
    public class FortuneWheel : ArcadeStation
    {
        public static readonly string[] Segments = { "0.1", "2", "0.25", "SPIN", "0.5", "0.1", "3", "0.25", "0", "0.1", "5", "0.5", "0.25", "2", "0.1", "SPIN", "0.5", "3", "0.25", "0.1" };
        const float SpinTime = 7f; const int Turns = 7;
        const float R = 0.62f;
        static readonly Vector3 Hub = new Vector3(0, 1.95f, 0.62f);
        float Seg { get { return 360f / Segments.Length; } }

        Transform wheel, pointer;
        static Material wedgeMat;
        TextMeshPro resultT;
        PokeButton spinBtn;
        bool playing;
        float angle;
        long stake;

        protected override string Rules
        {
            get { return "ONE BET, ONE SPIN\nTHE POINTER PAYS ITS MULTIPLIER\n5x · 3x · 2x · 0.5x · 0.25x · 0.1x · 0x\nSPIN = FREE SPIN AGAIN"; }
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 300); } }
        /// <summary>Above the wheel (the default sat right across its face).</summary>
        protected override Vector3 StatusPos { get { return new Vector3(0, Hub.y + R + 0.62f, 0.55f); } }

        static Color Col(string s)
        {
            // deep, saturated paint: read under the casino's bright lights (and the trailer's bloom) without washing out
            if (s == "SPIN") return Palette.Hex("5B1FA6");
            if (s == "0") return Palette.Hex("0C0A10");
            float m = float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            return m >= 5f ? Palette.Hex("B0124F") : m >= 3f ? Palette.Hex("B3530A") : m >= 2f ? Palette.Hex("A87A00") : m >= 0.5f ? Palette.Hex("0F5E86") : m >= 0.25f ? Palette.Hex("1D3F9A") : Palette.Hex("1E2A5C");
        }

        protected override void BuildMachine()
        {
            // stand + rim with bulbs
            var st = new MeshKit();
            st.Box(new Vector3(0, 1.3f, Hub.z + 0.12f), new Vector3(0.16f, 1.3f, 0.1f), Palette.Hex("15111F"), 0f);
            st.Cylinder(Hub + new Vector3(0, 0, 0.06f), R + 0.08f, R + 0.08f, 0.06f, 64, Quaternion.Euler(90, 0, 0), Palette.Hex("1A1428"), 0f);
            st.Torus(Hub + new Vector3(0, 0, 0.01f), R + 0.06f, 0.022f, 72, 6, Quaternion.Euler(90, 0, 0), Palette.Gold, 0.3f);
            for (int i = 0; i < 20; i++)
            {
                var p = Quaternion.Euler(0, 0, i * 18f + 9f) * Vector3.up * (R + 0.06f);
                st.Sphere(Hub + new Vector3(p.x, p.y, -0.015f), 0.018f, 8, i % 2 == 0 ? Palette.Gold : Palette.Magenta, 0.6f);
            }
            st.Spawn("Stand", transform, Palette.Lacquer, false);

            // the wheel: one wedge per segment (built in a frame where +Y faces the player and z is "up")
            wheel = Build.Empty("Wheel", transform, Hub).transform;
            var face = Build.Empty("Face", wheel, Vector3.zero, Quaternion.Euler(-90, 0, 0)).transform;
            var wk = new MeshKit();
            for (int i = 0; i < Segments.Length; i++)
            {
                var outline = new List<Vector2> { Vector2.zero };
                for (int s = 0; s <= 6; s++)
                {
                    float a = (i * Seg - Seg * 0.5f + s * Seg / 6f) * Mathf.Deg2Rad;
                    outline.Add(new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * R);
                }
                wk.Prism(outline, 0f, 0.012f, Col(Segments[i]), 0f);
            }
            // matte paint: no rim light, no reflection, no glow
            if (wedgeMat == null) wedgeMat = Palette.Flat(new Color(0.42f, 0.42f, 0.42f), 0f, 0f, 0.05f);   // the hall lights are hot: paint it dark so the colours stay colours
            wk.Spawn("Wedges", face, wedgeMat, false);
            var dk = new MeshKit();
            for (int i = 0; i < Segments.Length; i++)
            {
                var p = Quaternion.Euler(0, 0, i * Seg + Seg * 0.5f) * Vector3.up;
                dk.Bar(Vector3.zero + p * 0.1f + new Vector3(0, 0, -0.016f), p * R + new Vector3(0, 0, -0.016f), 0.006f, Palette.Gold, 0.25f);
            }
            dk.Cylinder(new Vector3(0, 0, -0.02f), 0.09f, 0.09f, 0.04f, 32, Quaternion.Euler(90, 0, 0), Palette.Gold, 0.25f);
            dk.Spawn("Dividers", wheel, Palette.Glossy, false);
            for (int i = 0; i < Segments.Length; i++)
            {
                string s = Segments[i];
                var lab = Txt.Make("L" + i, wheel, Quaternion.Euler(0, 0, i * Seg) * Vector3.up * (R * 0.68f) + new Vector3(0, 0, -0.02f),
                    s == "SPIN" ? "SPIN" : s + "x", s == "SPIN" ? 0.07f : 0.09f, s == "0" ? Palette.Hex("FF5A5A") : Color.white, Palette.Display);
                lab.transform.localRotation = Quaternion.Euler(0, 0, i * Seg + 90f);
                lab.fontStyle = FontStyles.Bold;
                lab.outlineWidth = 0.22f; lab.outlineColor = new Color32(8, 6, 14, 255);
            }

            // pointer at the top
            pointer = Build.Empty("Pointer", transform, Hub + new Vector3(0, R + 0.06f, -0.04f)).transform;
            var pk = new MeshKit();
            pk.Cylinder(new Vector3(0, -0.03f, 0), 0.0f, 0.045f, 0.09f, 3, Quaternion.Euler(0, 0, 180), Palette.Red, 1f);
            pk.Sphere(new Vector3(0, 0.03f, 0), 0.025f, 10, Palette.Gold, 0.8f);
            pk.Spawn("Flap", pointer, Palette.Neon, false);

            resultT = Txt.Glow("Result", transform, Hub + new Vector3(0, R + 0.28f, -0.05f), "WHEEL OF FORTUNE", 0.1f, Palette.Gold);
            spinBtn = PokeButton.Create("Spin", transform, new Vector3(0.38f, TableY + 0.035f, -0.32f), Quaternion.identity, 0.05f, Palette.Gold, "SPIN", () => StartCoroutine(Play()));
            var lg = new GameObject("WheelLight"); lg.transform.SetParent(transform, false); lg.transform.localPosition = Hub + new Vector3(0, 0.2f, -0.6f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 2.2f; l.intensity = 0.25f; l.color = new Color(1f, 0.92f, 0.85f); l.shadows = LightShadows.None;
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "SPIN", 20, y, 520, 90, Palette.Gold, () => StartCoroutine(Play()), 40);
        }

        IEnumerator Play()
        {
            if (playing) yield break;
            stake = Take();
            if (stake <= 0) { Status("PLACE A BET ON THE CIRCLE", Palette.Gold); yield break; }
            playing = true; spinBtn.SetInteractable(false);
            Say("good_luck");
            while (true)
            {
                int pick = (int)(Odds.R() * Segments.Length) % Segments.Length;
                Status("SPINNING...", Palette.Teal);
                resultT.text = "· · ·"; resultT.color = Palette.Text;
                yield return Spin(pick);
                string s = Segments[pick];
                if (s == "SPIN")
                {
                    resultT.text = "SPIN AGAIN!"; resultT.color = Palette.Violet;
                    Status("FREE SPIN!", Palette.Violet);
                    Sfx.UI("streak", 0.7f);
                    yield return new WaitForSeconds(0.8f);
                    continue;
                }
                float m = float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
                long pay = (long)(stake * (double)m);
                resultT.text = s + "x"; resultT.color = m >= 1f ? Palette.Mint : m > 0f ? Palette.Gold : Palette.Red;
                Status(m >= 1f ? "WIN · " + s + "x · " + pay.ToString("N0") : m > 0f ? s + "x · " + pay.ToString("N0") + " BACK" : "ZERO · STAKE LOST", m >= 1f ? Palette.Mint : Palette.Muted);
                PayDirect(pay, stake, pointer.position);
                stake = 0;
                break;
            }
            yield return new WaitForSeconds(1.2f);
            playing = false; spinBtn.SetInteractable(true);
        }

        IEnumerator Spin(int pick)
        {
            // segment i sits at local angle i*Seg; the wheel's angle a puts it at a + i*Seg; the pointer is at 0
            float jitter = (float)(Odds.R() - 0.5) * Seg * 0.7f;
            float target = -pick * Seg + jitter;
            float start = angle;
            float end = target - 360f * Turns;
            while (end > start - 360f * Turns) end -= 360f;    // always at least 7 full turns, always the same way
            int lastPeg = Mathf.FloorToInt(start / Seg);
            for (float t = 0; t < SpinTime; t += Time.deltaTime)
            {
                float k = 1f - Mathf.Pow(1f - t / SpinTime, 3f);
                angle = Mathf.Lerp(start, end, k);
                wheel.localRotation = Quaternion.Euler(0, 0, angle);
                int peg = Mathf.FloorToInt((angle + Seg * 0.5f) / Seg);
                if (peg != lastPeg)
                {
                    lastPeg = peg;
                    Sfx.Play("ball_tick", pointer.position, 0.35f, 0.9f + Random.value * 0.2f);
                    pointer.localRotation = Quaternion.Euler(0, 0, -14f);
                }
                pointer.localRotation = Quaternion.Slerp(pointer.localRotation, Quaternion.identity, Time.deltaTime * 12f);
                yield return null;
            }
            angle = Mathf.Repeat(end, 360f);
            wheel.localRotation = Quaternion.Euler(0, 0, angle);
            pointer.localRotation = Quaternion.identity;
        }

        /// <summary>Trailer: one spin onto segment <paramref name="pick"/>, no chips involved.</summary>
        public void DemoSpin(int pick) { StartCoroutine(Demo(pick)); }
        IEnumerator Demo(int pick)
        {
            resultT.text = "· · ·"; resultT.color = Palette.Text;
            yield return Spin(pick);
            string s = Segments[pick];
            resultT.text = s == "SPIN" ? "SPIN AGAIN!" : s + "x"; resultT.color = s == "5" ? Palette.Magenta : Palette.Gold;
            if (s != "0" && s != "SPIN") Juice.Sparks(pointer.position, Palette.Gold, 40);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (wheel == null || !GameState.Exists) return;
            if (playing && stake > 0)
            {
                GameState.I.Pay(stake, stake, transform.position);
                GameState.I.Notify("WHEEL OF FORTUNE · ROUND VOID · " + stake.ToString("N0") + " RETURNED", Palette.Gold);
            }
            stake = 0; playing = false;
            if (spinBtn != null) spinBtn.SetInteractable(true);
        }
    }
}
