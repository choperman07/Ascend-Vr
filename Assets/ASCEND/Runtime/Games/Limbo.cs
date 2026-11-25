using System.Collections;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Limbo: set a target multiplier (1.10x – 1,000x). A rocket climbs a holographic gauge; if the result
    /// reaches your target you win target × bet. Win chance = 97% ÷ target, shown live (RTP 97%).</summary>
    public class Limbo : ArcadeStation
    {
        static readonly float[] Targets = { 1.1f, 1.25f, 1.5f, 2f, 3f, 5f, 10f, 25f, 50f, 100f, 250f, 1000f };
        int ti = 3;
        bool playing;
        TextMeshPro targetT, resultT, chanceT;
        Transform gauge, rocket, line;
        PokeButton playBtn;

        float Target { get { return Targets[ti]; } }
        protected override string Rules { get { return "SET A TARGET MULTIPLIER, PRESS PLAY\nWIN IF THE RESULT REACHES YOUR TARGET\nA WIN PAYS YOUR TARGET"; } }

        protected override void BuildMachine()
        {
            var k = new MeshKit();
            k.Box(new Vector3(0, 1.75f, 0.6f), new Vector3(1.3f, 1.5f, 0.06f), Palette.Hex("07050D"), 0f);
            for (int i = 0; i <= 10; i++)
            {
                float y = 1.05f + i * 0.13f;
                k.Bar(new Vector3(-0.55f, y, 0.565f), new Vector3(0.55f, y, 0.565f), i % 5 == 0 ? 0.006f : 0.002f, Palette.Violet, i % 5 == 0 ? 0.7f : 0.3f);
            }
            for (int s = -1; s <= 1; s += 2) k.Bar(new Vector3(s * 0.65f, 1.0f, 0.56f), new Vector3(s * 0.65f, 2.5f, 0.56f), 0.015f, Accent, 1f);
            k.Spawn("Screen", transform, Palette.Neon, false);
            // log scale labels
            string[] lab = { "1x", "", "", "", "", "10x", "", "", "", "", "100x+" };
            for (int i = 0; i <= 10; i += 5) Txt.Make("G" + i, transform, new Vector3(-0.58f, 1.07f + i * 0.13f, 0.55f), lab[i], 0.04f, Palette.Muted, Palette.Mono, TextAlignmentOptions.Right, 0.3f);
            var lk = new MeshKit(); lk.Box(Vector3.zero, new Vector3(1.1f, 0.012f, 0.01f), Palette.Gold, 1f);
            line = lk.Spawn("TargetLine", transform, new Vector3(0, 1.3f, 0.555f), Quaternion.identity, Palette.Neon, false).transform;
            var rk = new MeshKit();
            rk.Cylinder(Vector3.zero, 0.035f, 0.035f, 0.12f, 12, Quaternion.identity, Palette.Text, 0.7f);
            rk.Cylinder(new Vector3(0, 0.09f, 0), 0.035f, 0.0f, 0.07f, 12, Quaternion.identity, Palette.Magenta, 1f);
            rk.Cylinder(new Vector3(0, -0.08f, 0), 0.02f, 0.045f, 0.05f, 12, Quaternion.identity, Palette.Gold, 1f);
            rocket = rk.Spawn("Rocket", transform, new Vector3(0, 1.05f, 0.54f), Quaternion.identity, Palette.Neon, false).transform;
            targetT = Txt.Glow("Target", transform, new Vector3(0, 2.68f, 0.56f), "", 0.13f, Palette.Gold);
            resultT = Txt.Glow("Result", transform, new Vector3(0.35f, 1.9f, 0.55f), "", 0.14f, Palette.Text);
            chanceT = Txt.Make("Chance", transform, new Vector3(0, TableY + 0.15f, 0.25f), "", 0.05f, Palette.Mint, Palette.Display);
            PokeButton.Create("Down", transform, new Vector3(-0.3f, TableY + 0.035f, -0.36f), Quaternion.identity, 0.025f, Palette.Teal, "-", () => Step(-1));
            PokeButton.Create("Up", transform, new Vector3(-0.15f, TableY + 0.035f, -0.36f), Quaternion.identity, 0.025f, Palette.Teal, "+", () => Step(1));
            playBtn = PokeButton.Create("Play", transform, new Vector3(0.38f, TableY + 0.035f, -0.32f), Quaternion.identity, 0.045f, Palette.Gold, "PLAY", () => StartCoroutine(Play()));
            Step(0);
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "TARGET -", 20, y, 250, 70, Palette.Teal, () => Step(-1), 28);
            UIKit.Btn(p, "TARGET +", 290, y, 250, 70, Palette.Teal, () => Step(1), 28);
            UIKit.Btn(p, "PLAY", 20, y + 80, 520, 80, Palette.Gold, () => StartCoroutine(Play()), 38);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 390); } }

        static float GaugeY(float m) { float g = Mathf.Clamp01(Mathf.Log10(Mathf.Max(1f, m)) / 2f); return 1.05f + g * 1.3f; }

        void Step(int d)
        {
            if (playing) return;
            ti = Mathf.Clamp(ti + d, 0, Targets.Length - 1);
            targetT.text = "TARGET " + Target.ToString(Target < 10 ? "0.00" : "0") + "x";
            chanceT.text = "WIN " + (Target * 100f).ToString("N0") + " PER 100 BET";
            line.localPosition = new Vector3(0, GaugeY(Target), 0.555f);
        }

        IEnumerator Play()
        {
            if (playing) yield break;
            long stake = Take();
            if (stake <= 0) yield break;
            playing = true; playBtn.SetInteractable(false);
            double u = 1.0 - Odds.R();                       // (0, 1]
            float result = (float)System.Math.Floor(0.97 / u * 100.0) / 100f;
            if (result < 1f) result = 1f;
            bool win = result >= Target;
            var loop = Sfx.Loop("rocket_loop", rocket, 0.6f);
            float dur = Mathf.Lerp(0.8f, 2.2f, Mathf.Clamp01(Mathf.Log10(result) / 2f));
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float k = Fx.EaseOut(t / dur);
                float m = Mathf.Pow(result, k);
                rocket.localPosition = new Vector3(Mathf.Sin(t * 30f) * 0.004f, GaugeY(m), 0.54f);
                resultT.text = m.ToString("0.00") + "x";
                resultT.color = m >= Target ? Palette.Mint : Palette.Text;
                yield return null;
            }
            if (loop != null) Destroy(loop.gameObject);
            resultT.text = result.ToString("0.00") + "x";
            resultT.color = win ? Palette.Mint : Palette.Red;
            if (win) Say("limbo_win");
            Status(win ? "WIN · " + Target.ToString("0.##") + "x" : "RESULT " + result.ToString("0.00") + "x", win ? Palette.Mint : Palette.Muted);
            if (!win) Sfx.Play("crash_boom", rocket.position, 0.5f);
            PayDirect(win ? (long)(stake * (double)Target) : 0, stake, rocket.position);
            yield return new WaitForSeconds(1.2f);
            rocket.localPosition = new Vector3(0, 1.05f, 0.54f);
            resultT.color = Palette.Text;
            playing = false; playBtn.SetInteractable(true);
        }
    }
}
