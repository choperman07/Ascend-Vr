using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Cross the Road (rules from Roberto's "Gamble With Your Friends" rebuild): a neon chicken crosses ten lanes
    /// of traffic. Each lane raises the multiplier 1 → 1.2 → 1.5 → 2 → 3 → 5 → 10 → 25 → 75 → 250 → 1,000x; stepping
    /// from lane s survives with chance m(s) / m(s+1). Cash out any time after the first lane; reaching the far side
    /// cashes out at 1,000x. Expected return 100 %.</summary>
    public class CrossTheRoad : ArcadeStation
    {
        public static readonly double[] Mult = { 1.0, 1.2, 1.5, 2.0, 3.0, 5.0, 10.0, 25.0, 75.0, 250.0, 1000.0 };
        const int Lanes = 10;
        const float LaneW = 0.128f, Depth = 0.5f;

        Transform road, chicken;
        TextMeshPro multT;
        readonly List<Transform> cars = new List<Transform>();
        readonly List<float> carSpeed = new List<float>();
        readonly List<int> carLane = new List<int>();
        int targetLane = -1, hitLane = -1;      // the lane the chicken is hopping into / the lane where a driver will not brake
        const float StopGap = 0.1f, FollowGap = 0.16f;
        PokeButton startBtn, goBtn, cashBtn;
        bool playing, moving, demo;
        int demoSplat;
        /// <summary>Trailer: the chicken crosses lane after lane and gets flattened on lane <paramref name="splatOn"/>.</summary>
        public void DemoCross(int splatOn, float every) { StartCoroutine(DemoRoutine(splatOn, every)); }
        IEnumerator DemoRoutine(int splatOn, float every)
        {
            demo = true; demoSplat = splatOn; stake = 100; playing = true; step = 0; PlaceChicken();
            multT.text = "1x · 100"; multT.color = Palette.Text;
            while (playing)
            {
                yield return new WaitForSeconds(every);
                if (!moving) yield return StepRoutine();
            }
            demo = false;
        }
        int step;
        long stake;

        protected override string Rules
        {
            get { return "PRESS START, THEN GO TO CROSS ONE LANE\nEVERY LANE RAISES THE MULTIPLIER\nGET HIT AND THE STAKE IS LOST\nCASH OUT WHENEVER YOU WANT · 1,000x ON THE FAR SIDE"; }
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 380); } }
        protected override float CounterWidth { get { return 1.7f; } }

        static float LaneX(int i) { return -0.64f + i * LaneW; }

        protected override void BuildMachine()
        {
            // the road diorama leans toward the player behind the counter
            road = Build.Empty("Road", transform, new Vector3(0, 1.16f, 0.36f), Quaternion.Euler(-24f, 0, 0)).transform;
            var k = new MeshKit();
            k.Box(new Vector3(0, -0.03f, 0), new Vector3(LaneW * (Lanes + 1) + 0.12f, 0.05f, Depth + 0.1f), Palette.Hex("120D1C"), 0f);
            for (int i = 0; i <= Lanes; i++)
            {
                bool side = i == 0 || i == Lanes;
                k.Box(new Vector3(LaneX(i), 0.0f, 0), new Vector3(LaneW - 0.004f, 0.01f, Depth), side ? Palette.Hex("2E7D4F") : Palette.Hex("24222C"), side ? 0.15f : 0f);
                if (!side)
                    for (float z = -Depth * 0.5f + 0.03f; z < Depth * 0.5f; z += 0.08f)
                        k.Box(new Vector3(LaneX(i) + LaneW * 0.5f, 0.007f, z + 0.02f), new Vector3(0.005f, 0.004f, 0.04f), Palette.Text, 0.6f);
            }
            k.Bar(new Vector3(LaneX(0) - LaneW * 0.5f - 0.05f, 0.0f, -Depth * 0.5f - 0.05f), new Vector3(LaneX(Lanes) + LaneW * 0.5f + 0.05f, 0.0f, -Depth * 0.5f - 0.05f), 0.01f, Accent, 1f);
            k.Spawn("Lanes", road, Palette.Neon, false);
            for (int i = 0; i <= Lanes; i++)
            {
                var t = Txt.Make("M" + i, road, new Vector3(LaneX(i), 0.012f, -Depth * 0.5f + 0.045f), Fmt(Mult[i]), 0.034f, i >= 7 ? Palette.Magenta : i >= 4 ? Palette.Gold : Palette.Text, Palette.Display);
                t.transform.localRotation = Quaternion.Euler(70f, 0, 0);
            }

            // traffic: two cars per road lane, going up or down the lane
            var colors = new[] { Palette.Teal, Palette.Magenta, Palette.Gold, Palette.Coral, Palette.Violet, Palette.Lime };
            for (int i = 1; i < Lanes; i++)
            {
                // one speed per lane and half a lane apart: cars never drive through each other
                float sp = (0.12f + Random.value * 0.12f) * (i % 2 == 0 ? 1f : -1f);
                float z0 = Random.Range(-Depth * 0.5f, 0f);
                for (int n = 0; n < 2; n++)
                {
                    var c = MakeCar(colors[(i + n) % colors.Length]);
                    c.localPosition = new Vector3(LaneX(i), 0.025f, z0 + n * Depth * 0.5f);
                    c.localRotation = Quaternion.Euler(0, sp > 0 ? 0 : 180, 0);
                    cars.Add(c); carSpeed.Add(sp); carLane.Add(i);
                }
            }

            chicken = Build.Empty("Chicken", road, Vector3.zero).transform;
            var ck = new MeshKit();
            ck.Sphere(new Vector3(0, 0.03f, 0), new Vector3(0.026f, 0.028f, 0.03f), 12, Palette.Text, 0.4f);
            ck.Sphere(new Vector3(0, 0.065f, 0.012f), 0.017f, 10, Palette.Text, 0.4f);
            ck.Box(new Vector3(0, 0.085f, 0.01f), new Vector3(0.006f, 0.012f, 0.016f), Palette.Red, 0.8f);
            ck.Cylinder(new Vector3(0, 0.063f, 0.032f), 0.007f, 0.0f, 0.014f, 6, Quaternion.Euler(90, 0, 0), Palette.Gold, 0.8f);
            ck.Bar(new Vector3(-0.008f, 0.0f, 0), new Vector3(-0.008f, 0.012f, 0), 0.003f, Palette.Gold, 0.6f);
            ck.Bar(new Vector3(0.008f, 0.0f, 0), new Vector3(0.008f, 0.012f, 0), 0.003f, Palette.Gold, 0.6f);
            ck.Spawn("Body", chicken, Vector3.zero, Quaternion.Euler(0, 90, 0), Palette.Neon, false);
            PlaceChicken();

            multT = Txt.Glow("Mult", transform, new Vector3(0, 1.62f, 0.62f), "CROSS THE ROAD", 0.1f, Palette.Gold);
            float by = TableY + 0.035f;
            startBtn = PokeButton.Create("Start", transform, new Vector3(0.36f, by, -0.36f), Quaternion.identity, 0.04f, Palette.Gold, "START", StartRound, 0.05f);
            goBtn = PokeButton.Create("Go", transform, new Vector3(0.52f, by, -0.24f), Quaternion.identity, 0.04f, Palette.Teal, "GO", Step, 0.05f);
            cashBtn = PokeButton.Create("Cash", transform, new Vector3(0.68f, by, -0.36f), Quaternion.identity, 0.04f, Palette.Mint, "CASH", CashOut, 0.05f);
            Buttons();
        }

        Transform MakeCar(Color c)
        {
            var car = Build.Empty("Car", road, Vector3.zero).transform;
            var k = new MeshKit();
            k.Box(new Vector3(0, 0.012f, 0), new Vector3(0.07f, 0.022f, 0.11f), c, 0.6f);
            k.Box(new Vector3(0, 0.032f, -0.008f), new Vector3(0.06f, 0.02f, 0.06f), Palette.Hex("1A1626"), 0.1f);
            k.Box(new Vector3(-0.022f, 0.014f, 0.056f), new Vector3(0.014f, 0.008f, 0.003f), Palette.Text, 1.2f);
            k.Box(new Vector3(0.022f, 0.014f, 0.056f), new Vector3(0.014f, 0.008f, 0.003f), Palette.Text, 1.2f);
            k.Spawn("Body", car, Palette.Glossy, false);
            return car;
        }

        static string Fmt(double m) { return (m < 10 ? m.ToString("0.##") : m.ToString("N0")) + "x"; }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "START", 20, y, 250, 80, Palette.Gold, StartRound, 34);
            UIKit.Btn(p, "GO ▸", 290, y, 250, 80, Palette.Teal, Step, 34);
            UIKit.Btn(p, "CASH OUT", 20, y + 90, 520, 80, Palette.Mint, CashOut, 34);
        }

        void PlaceChicken() { chicken.localPosition = new Vector3(LaneX(step), 0.01f, 0f); chicken.localScale = Vector3.one; chicken.localRotation = Quaternion.identity; }

        void Buttons()
        {
            startBtn.SetInteractable(!playing);
            goBtn.SetInteractable(playing && !moving);
            cashBtn.SetInteractable(playing && !moving && step > 0);
        }

        void Update()
        {
            if (road == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f), half = Depth * 0.5f;
            int standing = playing && step >= 1 && step < Lanes ? step : -1;
            for (int i = 0; i < cars.Count; i++)
            {
                var p = cars[i].localPosition;
                int lane = carLane[i];
                float dir = Mathf.Sign(carSpeed[i]);
                float move = Mathf.Abs(carSpeed[i]) * dt * (lane == hitLane ? 3.2f : 1f);
                // drivers brake for the chicken: they stop a car's length short of it and wait until it has moved on
                // (a car already level with it carries on; the chicken waits for that one before hopping in)
                if (lane != hitLane && (lane == standing || lane == targetLane))
                {
                    float ahead = (0f - p.z) * dir;
                    if (ahead > StopGap - 0.002f) move = Mathf.Min(move, Mathf.Max(0f, ahead - StopGap));
                }
                // ... and queue behind the car in front
                int other = (i % 2 == 0) ? i + 1 : i - 1;
                float gap = Mathf.Repeat((cars[other].localPosition.z - p.z) * dir, Depth);
                if (lane != hitLane) move = Mathf.Min(move, Mathf.Max(0f, gap - FollowGap));
                p.z += dir * move;
                if (p.z > half) p.z -= Depth;
                if (p.z < -half) p.z += Depth;
                cars[i].localPosition = p;
                // cars drive in and out of the tunnels at both ends instead of popping
                float edge = Mathf.Clamp01((half - Mathf.Abs(p.z)) / 0.045f);
                cars[i].localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, edge);
            }
        }

        /// <summary>True when no car is level with the crossing point of this lane.</summary>
        bool LaneClear(int lane)
        {
            for (int i = 0; i < cars.Count; i++)
                if (carLane[i] == lane && Mathf.Abs(cars[i].localPosition.z) < StopGap - 0.004f) return false;
            return true;
        }

        void StartRound()
        {
            if (playing) return;
            stake = Take();
            if (stake <= 0) { Status("PLACE A BET ON THE CIRCLE", Palette.Gold); return; }
            playing = true; step = 0; PlaceChicken();
            multT.text = "1x · " + stake.ToString("N0"); multT.color = Palette.Text;
            Status("PRESS GO TO CROSS THE FIRST LANE", Palette.Teal);
            Say("good_luck");
            Buttons();
        }

        void Step()
        {
            if (!playing || moving) { if (!playing) { Sfx.UI("ui_error", 0.3f); Status("PRESS START FIRST", Palette.Gold); } return; }
            if (step >= Lanes) return;
            StartCoroutine(StepRoutine());
        }

        IEnumerator StepRoutine()
        {
            moving = true; Buttons();
            bool hit = demo ? step + 1 >= demoSplat : Odds.R() > Mult[step] / Mult[step + 1];
            Vector3 from = chicken.localPosition, to = new Vector3(LaneX(step + 1), 0.01f, 0f);
            // look both ways: the next lane's drivers start braking, and the chicken waits for a gap
            targetLane = step + 1 < Lanes ? step + 1 : -1;
            for (float w = 0; w < 1.2f && targetLane > 0 && !LaneClear(targetLane); w += Time.deltaTime)
            {
                chicken.localRotation = Quaternion.Euler(0, Mathf.Sin(w * 14f) * 25f, 0);
                yield return null;
            }
            chicken.localRotation = Quaternion.identity;
            Sfx.Play("chip_place", chicken.position, 0.4f, 1.4f);
            for (float t = 0; t < 0.35f; t += Time.deltaTime)
            {
                float k = t / 0.35f;
                chicken.localPosition = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.05f;
                yield return null;
            }
            chicken.localPosition = to;
            if (hit)
            {
                // this lane's driver was looking at the phone: no braking, straight through
                hitLane = step + 1; targetLane = -1;
                Sfx.Play("whistle", chicken.position, 0.35f, 1.6f);
                for (float w = 0; w < 2.5f; w += Time.deltaTime)
                {
                    bool struck = false;
                    for (int i = 0; i < cars.Count; i++)
                        if (carLane[i] == hitLane && Mathf.Abs(cars[i].localPosition.z) < 0.05f) struck = true;
                    if (struck) break;
                    yield return null;
                }
                chicken.localScale = new Vector3(1.6f, 0.15f, 1.6f);
                Sfx.Play("crash_boom", chicken.position, 0.6f);
                yield return new WaitForSeconds(0.45f);
                hitLane = -1;
                multT.text = "SPLAT!"; multT.color = Palette.Red;
                Status("HIT ON LANE " + (step + 1) + " · STAKE LOST", Palette.Red);
                Say("boom");
                if (!demo) PayDirect(0, stake, chicken.position);
                stake = 0; playing = false; moving = false;
                yield return new WaitForSeconds(1.4f);
                step = 0; PlaceChicken(); multT.text = "CROSS THE ROAD"; multT.color = Palette.Gold;
                Buttons();
                yield break;
            }
            step++;
            targetLane = -1;
            moving = false;
            multT.text = Fmt(Mult[step]) + " · " + ((long)(stake * Mult[step])).ToString("N0");
            multT.color = Palette.Mint;
            Sfx.Play("gem", chicken.position, 0.5f, 0.8f + step * 0.06f);
            if (step >= Lanes) { CashOut(); yield break; }
            Status("LANE " + step + " CLEARED · NEXT " + Fmt(Mult[step + 1]) + " · " + (Mult[step] / Mult[step + 1] * 100).ToString("0") + "% SAFE", Palette.Mint);
            Buttons();
        }

        void CashOut()
        {
            if (!playing || moving || step == 0) { if (playing && step == 0) { Sfx.UI("ui_error", 0.4f); Status("CROSS ONE LANE BEFORE CASHING OUT", Palette.Gold); } return; }
            double m = Mult[step];
            long pay = (long)(stake * m);
            Status((step >= Lanes ? "MADE IT ACROSS! " : "CASHED OUT ") + Fmt(m) + " · " + pay.ToString("N0"), Palette.Gold);
            Say("cashout");
            PayDirect(pay, stake, chicken.position);
            stake = 0; playing = false;
            StartCoroutine(ResetLater());
            Buttons();
        }

        IEnumerator ResetLater()
        {
            yield return new WaitForSeconds(1.4f);
            if (playing) yield break;
            step = 0; PlaceChicken(); multT.text = "CROSS THE ROAD"; multT.color = Palette.Gold;
            Buttons();
        }

        /// <summary>Floor switched off mid-crossing: cash out what was cleared, or refund an untouched stake.</summary>
        protected override void OnDisable()
        {
            base.OnDisable();
            if (road == null || !GameState.Exists) return;
            if (playing && stake > 0)
            {
                long pay = step > 0 ? (long)(stake * Mult[step]) : stake;
                GameState.I.Pay(pay, stake, transform.position);
                GameState.I.Notify("CROSS THE ROAD · AUTO CASH-OUT · " + pay.ToString("N0"), Palette.Gold);
            }
            stake = 0; playing = false; moving = false; step = 0; targetLane = -1; hitLane = -1;
            PlaceChicken();
            if (multT != null) { multT.text = "CROSS THE ROAD"; multT.color = Palette.Gold; }
            Buttons();
        }
    }
}
