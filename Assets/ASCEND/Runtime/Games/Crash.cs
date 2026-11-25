using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Crash: the rocket climbs and the multiplier with it — slap CASH OUT before it explodes.
    /// Crash point = 0.97 / (1 - u): chance to reach x = 97% / x. Other guests ride along on the big screen.</summary>
    public class Crash : ArcadeStation
    {
        PokeButton launchBtn, cashBtn;
        TextMeshPro multT, ridersT, histT;
        Transform rocket, screen;
        LineRenderer trail;
        bool flying, cashed;
        float mult = 1f;
        long stake;
        float autoCash;
        static readonly float[] AutoSteps = { 0f, 1.5f, 2f, 3f, 5f, 10f };
        int autoIdx;
        readonly List<string> history = new List<string>();
        TMPro.TextMeshProUGUI autoT;

        protected override string Rules { get { return "THE MULTIPLIER CLIMBS UNTIL IT CRASHES\nCASH OUT ANY TIME BEFORE THE CRASH\nYOUR BET × THE MULTIPLIER WHEN YOU CASH OUT"; } }

        protected override void BuildMachine()
        {
            var k = new MeshKit();
            k.Box(new Vector3(0, 1.85f, 1.2f), new Vector3(3.2f, 1.9f, 0.12f), Palette.Hex("06050C"), 0.05f);
            k.Bar(new Vector3(-1.62f, 0.9f, 1.13f), new Vector3(1.62f, 0.9f, 1.13f), 0.04f, Accent, 1f);
            k.Bar(new Vector3(-1.62f, 2.8f, 1.13f), new Vector3(1.62f, 2.8f, 1.13f), 0.04f, Accent, 1f);
            k.Bar(new Vector3(-1.62f, 0.9f, 1.13f), new Vector3(-1.62f, 2.8f, 1.13f), 0.04f, Accent, 1f);
            k.Bar(new Vector3(1.62f, 0.9f, 1.13f), new Vector3(1.62f, 2.8f, 1.13f), 0.04f, Accent, 1f);
            for (int i = 1; i < 5; i++) k.Bar(new Vector3(-1.4f, 1.05f + i * 0.33f, 1.14f), new Vector3(1.4f, 1.05f + i * 0.33f, 1.14f), 0.004f, Palette.Muted, 0.3f);
            k.Box(new Vector3(0, 0.45f, 1.25f), new Vector3(0.3f, 0.9f, 0.3f), Palette.Metal);
            k.Spawn("Screen", transform, Palette.Neon, false);
            screen = transform;

            var tg = new GameObject("Trail"); tg.transform.SetParent(transform, false);
            trail = tg.AddComponent<LineRenderer>();
            trail.useWorldSpace = false; trail.widthMultiplier = 0.03f; trail.positionCount = 0;
            trail.sharedMaterial = Palette.FlatGlow; trail.startColor = Palette.Mint; trail.endColor = Palette.Mint;
            var rk = new MeshKit();
            rk.Capsule(new Vector3(0, -0.07f, 0), new Vector3(0, 0.07f, 0), 0.04f, 10, Palette.Text, 0.5f);
            rk.Cylinder(new Vector3(0, 0.1f, 0), 0.04f, 0.0f, 0.06f, 10, Quaternion.identity, Palette.Magenta, 1f);
            rk.Box(new Vector3(0.045f, -0.08f, 0), new Vector3(0.04f, 0.05f, 0.01f), Palette.Magenta, 1f);
            rk.Box(new Vector3(-0.045f, -0.08f, 0), new Vector3(0.04f, 0.05f, 0.01f), Palette.Magenta, 1f);
            rocket = rk.Spawn("Rocket", transform, Palette.Neon, false).transform;
            Build.GlowSprite(rocket, new Vector3(0, -0.14f, 0), 0.25f, Palette.Coral, 1f);
            rocket.localPosition = RocketPos(0);
            multT = Txt.Glow("Mult", transform, new Vector3(0, 2.2f, 1.1f), "READY", 0.36f, Palette.Text);
            ridersT = Txt.Make("Riders", transform, new Vector3(1.05f, 2.45f, 1.1f), "", 0.05f, Palette.Muted, Palette.Mono, TextAlignmentOptions.TopLeft, 0.9f);
            ridersT.rectTransform.sizeDelta = new Vector2(0.9f, 0.6f);
            histT = Txt.Make("History", transform, new Vector3(0, 1.0f, 1.1f), "", 0.06f, Palette.Muted, Palette.Display);

            launchBtn = PokeButton.Create("Launch", transform, new Vector3(-0.08f, TableY + 0.035f, -0.3f), Quaternion.identity, 0.045f, Palette.Gold, "LAUNCH", Launch);
            cashBtn = PokeButton.Create("Cash", transform, new Vector3(0.42f, TableY + 0.035f, -0.28f), Quaternion.identity, 0.075f, Palette.Mint, "CASH OUT", CashOut, 0.05f);
            cashBtn.SetInteractable(false);
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "LAUNCH", 20, y, 250, 80, Palette.Gold, Launch, 36);
            UIKit.Btn(p, "CASH OUT", 290, y, 250, 80, Palette.Mint, CashOut, 36);
            UIKit.Btn(p, "AUTO", 20, y + 90, 160, 60, Palette.Teal, () => { if (!flying) { autoIdx = (autoIdx + 1) % AutoSteps.Length; autoCash = AutoSteps[autoIdx]; autoT.text = AutoLabel(); } }, 28);
            autoT = UIKit.Label(p, AutoLabel(), 190, y + 90, 350, 60, 24, Palette.Text, Palette.Mono);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 380); } }

        string AutoLabel() { return autoCash <= 0 ? "AUTO CASH-OUT: OFF" : "AUTO CASH-OUT: " + autoCash.ToString("0.0") + "x"; }

        static float Floor2(float m) { return Mathf.Floor(m * 100f + 1e-3f) / 100f; }

        Vector3 RocketPos(float t)
        {
            float x = Mathf.Lerp(-1.35f, 1.35f, Mathf.Clamp01(t / 14f));
            float m = Mathf.Exp(0.1f * t);
            float y = 1.05f + Mathf.Clamp01(Mathf.Log(m) / Mathf.Log(12f)) * 1.55f;
            return new Vector3(x, y, 1.08f);
        }

        void Launch()
        {
            if (flying) return;
            stake = Take();
            if (stake <= 0) return;
            StartCoroutine(Fly());
        }

        void CashOut()
        {
            if (!flying || cashed) return;
            cashed = true;
            cashBtn.SetInteractable(false);
            float m = Floor2(mult);   // the multiplier on the screen is the one paid
            long pay = (long)(stake * (double)m);
            Status("CASHED OUT " + m.ToString("0.00") + "x", pay > stake ? Palette.Mint : Palette.Text);
            Sfx.Play("cashout", transform.position + Vector3.up, 1f);
            Say("cashout");
            PayDirect(pay, stake, W(new Vector3(0, TableY + 0.3f, 0.2f)));
            multT.color = Palette.Mint;
        }

        IEnumerator Fly()
        {
            flying = true; cashed = false;
            launchBtn.SetInteractable(false); cashBtn.SetInteractable(true);
            Status("LAUNCH!", Palette.Gold); Say("launch");
            multT.color = Palette.Text;
            double u = Odds.R();
            float crashAt = Mathf.Clamp(Mathf.Floor((float)(0.97 / (1.0 - u)) * 100f) / 100f, 1f, 1000f);
            // the other riders (simulated): each picks a target
            var riders = new List<KeyValuePair<string, float>>();
            for (int i = 0; i < 5; i++) riders.Add(new KeyValuePair<string, float>(Roster.Name(Odds.Range(0, Roster.Count)), Random.Range(1.2f, 6f)));
            var pts = new List<Vector3>();
            var loop = Sfx.Loop("rocket_loop", rocket, 0.7f);
            float t = 0; mult = 1f;
            while (crashAt > 1f)
            {
                t += Time.deltaTime;
                mult = Mathf.Exp(0.1f * t);
                // auto cash-out wins when the crash point is AT or above the target (chance 97% ÷ target, as published)
                if (!cashed && autoCash > 0 && autoCash <= crashAt && mult >= autoCash) { mult = autoCash; CashOut(); }
                if (mult >= crashAt) { mult = crashAt; break; }
                multT.text = Floor2(mult).ToString("0.00") + "x";
                multT.fontSize = 3.6f + Mathf.Min(1.4f, Mathf.Log(mult) * 0.6f);
                var p = RocketPos(t);
                rocket.localPosition = p;
                rocket.localRotation = Quaternion.Euler(0, 0, -50f + Mathf.Min(40f, t * 3f));
                if (pts.Count == 0 || (pts[pts.Count - 1] - p).sqrMagnitude > 0.0006f) { pts.Add(p); trail.positionCount = pts.Count; trail.SetPositions(pts.ToArray()); }
                if (loop != null) loop.pitch = 0.8f + Mathf.Min(1.2f, Mathf.Log(mult) * 0.4f);
                var sb = new System.Text.StringBuilder("RIDERS\n");
                foreach (var r in riders) sb.Append(r.Key).Append(mult >= r.Value ? "  <color=#5CFFB0>" + r.Value.ToString("0.00") + "x OUT</color>\n" : "  ...\n");
                ridersT.text = sb.ToString();
                if (!cashed && Time.frameCount % 30 == 0) XRIn.Haptic(Hand.Right, Mathf.Min(0.5f, 0.05f + Mathf.Log(mult) * 0.1f), 0.03f);
                yield return null;
            }
            if (loop != null) Destroy(loop.gameObject);
            multT.text = "CRASHED @ " + crashAt.ToString("0.00") + "x";
            multT.fontSize = 3.0f;
            multT.color = Palette.Red;
            Sfx.Play("crash_boom", rocket.position, 1f);
            Juice.Sparks(rocket.position, Palette.Coral, 60);
            StartCoroutine(Fx.Shake(rocket, 0.05f, 0.4f));
            cashBtn.SetInteractable(false);
            if (!cashed) { Status("CRASHED · NO WIN", Palette.Muted); Say("crashed"); PayDirect(0, stake, rocket.position); }
            history.Insert(0, "<color=#" + (crashAt >= 2 ? "5CFFB0" : "E0314B") + ">" + crashAt.ToString("0.00") + "x</color>");
            if (history.Count > 8) history.RemoveAt(history.Count - 1);
            histT.text = string.Join("   ", history.ToArray());
            yield return new WaitForSeconds(2.2f);
            rocket.localPosition = RocketPos(0); trail.positionCount = 0;
            multT.text = "READY"; multT.color = Palette.Text; multT.fontSize = 3.6f;
            flying = false;
            launchBtn.SetInteractable(true);
            Status("PLACE YOUR BET", Palette.Text);
        }
    }
}
