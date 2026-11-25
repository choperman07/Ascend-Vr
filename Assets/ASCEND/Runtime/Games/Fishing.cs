using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Catch a Fish: grab the rod, flick it toward the pond to cast (or trigger / CAST), wait for the bite,
    /// then yank the rod up (or trigger / REEL) within 1.5 s. The catch is decided at the cast — a missed bite is a free recast.</summary>
    public class Fishing : ArcadeStation
    {
        public static readonly PrizeTable Table = new PrizeTable(
            new[] { "OLD BOOT", "MINNOW", "TROUT", "SALMON", "TUNA", "SWORDFISH", "GOLDEN KOI" },
            new[] { 0f, 0.5f, 1f, 2f, 4f, 8f, 25f },
            new double[] { 35, 30, 18, 10, 5, 1.6, 0.4 });
        static readonly Color[] FishCol = { Palette.Hex("5A4A3A"), Palette.Silver, Palette.Teal, Palette.Coral, Palette.Violet, Palette.Magenta, Palette.Gold };

        enum St { Idle, Flying, Waiting, Bite, Reeling }
        St st = St.Idle;
        Transform rod, tip, rack, bobber, pond;
        XRGrabInteractable rodGrab;
        LineRenderer line;
        long stake; int result = -1;
        float biteAt, biteUntil, castBlockUntil; Vector3 bobRest;
        int btnState = -1;
        readonly List<Transform> fishes = new List<Transform>();
        PokeButton castBtn, reelBtn;
        TextMeshPro catchT;
        Vector3 PondCenter { get { return new Vector3(0, 0.42f, 1.45f); } }

        protected override string Rules { get { return "GRAB THE ROD · FLICK TO CAST · YANK ON THE BITE\n" + Table.Describe(4) + "\nTHE CATCH IS DECIDED WHEN YOU CAST\nMISSED BITE = FREE RECAST"; } }
        protected override float CounterWidth { get { return 1.3f; } }

        protected override void BuildMachine()
        {
            // pond with a glowing rim and a few fish swimming
            var k = new MeshKit();
            k.Cylinder(PondCenter + Vector3.down * 0.22f, 1.0f, 1.05f, 0.42f, 40, Quaternion.identity, Palette.Hex("120D1C"), 0.02f, true, Palette.Hex("0B2A3A"), 0.25f);
            k.Torus(PondCenter, 1.02f, 0.03f, 48, 6, Quaternion.identity, Accent, 1f);
            for (int i = 0; i < 7; i++)
            {
                float a = i * 0.9f;
                k.Sphere(PondCenter + new Vector3(Mathf.Cos(a) * 0.85f, 0.03f, Mathf.Sin(a) * 0.85f + 0.05f), new Vector3(0.12f, 0.06f, 0.09f), 8, Palette.Hex("1E2A24"), 0.05f);
            }
            k.Spawn("Pond", transform, Palette.Glossy, false);
            pond = Build.Empty("PondCenter", transform, PondCenter).transform;
            for (int i = 0; i < 6; i++)
            {
                var fk = new MeshKit();
                var c = FishCol[1 + i % 6];
                fk.Sphere(Vector3.zero, new Vector3(0.035f, 0.025f, 0.09f), 8, c, 0.7f);
                fk.Box(new Vector3(0, 0, -0.1f), new Vector3(0.005f, 0.05f, 0.04f), c, 0.7f);
                var f = fk.Spawn("Fish" + i, pond, new Vector3(0, -0.02f, 0), Quaternion.identity, Palette.Neon, false).transform;
                fishes.Add(f);
            }
            Txt.Glow("Title", transform, new Vector3(0, 2.2f, 1.6f), "CATCH A FISH", 0.2f, Accent);
            catchT = Txt.Make("Catch", transform, new Vector3(0, 1.85f, 1.6f), "", 0.09f, Palette.Gold, Palette.Display);
            var pt = Txt.Make("Prizes", transform, new Vector3(-1.35f, 1.5f, 1.2f), Table.Describe(1), 0.045f, Palette.Muted, Palette.Mono, TextAlignmentOptions.Left, 0.9f);
            pt.transform.localRotation = Quaternion.Euler(0, -30, 0);

            // rod rack + rod
            rack = Build.Empty("Rack", transform, new Vector3(0.5f, TableY + 0.06f, -0.3f), Quaternion.Euler(-60, 0, 0)).transform;
            var rk = new MeshKit(); rk.Box(new Vector3(0, -0.05f, 0), new Vector3(0.1f, 0.04f, 0.1f), Palette.Metal); rk.Ring(new Vector3(0, -0.02f, 0), 0.03f, 0.04f, 16, Quaternion.identity, Accent, 1f);
            rk.Spawn("RackBase", transform, new Vector3(0.5f, TableY + 0.03f, -0.3f), Quaternion.identity, Palette.Neon, false);
            rod = Build.Empty("Rod", transform, rack.localPosition, rack.localRotation).transform;
            var r = new MeshKit();
            r.Cylinder(new Vector3(0, 0, -0.05f), 0.017f, 0.017f, 0.22f, 12, Quaternion.Euler(90, 0, 0), Palette.Dark, 0.05f);
            r.Cylinder(new Vector3(0, -0.03f, 0.02f), 0.025f, 0.025f, 0.03f, 14, Quaternion.Euler(0, 0, 90), Palette.Silver, 0.2f);   // reel
            r.Tube(new Vector3(0, 0, 0.06f), new Vector3(0, 0, 1.15f), 0.007f, 8, Accent, 0.9f);
            r.Spawn("RodMesh", rod, Palette.Neon, false);
            tip = Build.Empty("Tip", rod, new Vector3(0, 0, 1.15f)).transform;
            Build.GlowSprite(tip, Vector3.zero, 0.06f, Accent, 0.8f);
            var col = rod.gameObject.AddComponent<CapsuleCollider>(); col.direction = 2; col.radius = 0.04f; col.height = 0.4f; col.center = new Vector3(0, 0, 0.0f);
            var rb = rod.gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            rodGrab = rod.gameObject.AddComponent<XRGrabInteractable>();
            rodGrab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            rodGrab.throwOnDetach = false;
            rodGrab.attachTransform = Build.Empty("Attach", rod, Vector3.zero).transform;
            rodGrab.selectExited.AddListener(_ => StartCoroutine(ReturnRod()));
            rodGrab.activated.AddListener(_ => Action());
            VelocityTracker.On(tip);

            // bobber + line
            var bk = new MeshKit(); bk.Sphere(new Vector3(0, 0.012f, 0), 0.022f, 10, Palette.Red, 0.8f); bk.Sphere(new Vector3(0, -0.01f, 0), 0.02f, 10, Palette.Text, 0.6f);
            bobber = bk.Spawn("Bobber", transform, Palette.Neon, false).transform;
            bobber.gameObject.SetActive(false);
            var lg = new GameObject("Line"); lg.transform.SetParent(transform, false);
            line = lg.AddComponent<LineRenderer>(); line.positionCount = 12; line.widthMultiplier = 0.003f; line.sharedMaterial = Palette.FlatGlow;
            line.startColor = line.endColor = Palette.A(Palette.Text, 0.7f); line.enabled = false;

            castBtn = PokeButton.Create("Cast", transform, new Vector3(0.12f, TableY + 0.035f, -0.38f), Quaternion.identity, 0.04f, Palette.Gold, "CAST", () => { if (st == St.Idle) Action(); });
            reelBtn = PokeButton.Create("Reel", transform, new Vector3(-0.08f, TableY + 0.035f, -0.38f), Quaternion.identity, 0.04f, Palette.Mint, "REEL", () => { if (st == St.Bite || st == St.Waiting) Action(); });
        }
        protected override Vector3 BetPos { get { return new Vector3(0.12f, TableY + 0.005f, -0.22f); } }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "CAST / REEL", 20, y, 520, 90, Palette.Gold, Action, 40);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 320); } }

        IEnumerator ReturnRod()
        {
            yield return null;
            if (rodGrab.isSelected) yield break;
            yield return StartCoroutine(Fx.Move(rod, rack.localPosition, 0.35f));
            rod.localRotation = rack.localRotation;
        }

        /// <summary>One button for everything: cast when idle, strike when the fish bites.</summary>
        void Action()
        {
            if (st == St.Idle) Cast();
            else if (st == St.Bite) StartCoroutine(Reel(true));
            else if (st == St.Waiting) { Sfx.Play("reel_click_loop", tip.position, 0.3f, 1.4f); Status("TOO EARLY · WAIT FOR THE BITE", Palette.Coral); }
        }

        void Cast()
        {
            if (result < 0)
            {
                stake = Take();
                if (stake <= 0) { castBlockUntil = Time.time + 1f; return; }   // a rod flick spans several frames: one refusal, not one per frame
                result = Table.Roll();   // the catch is fixed now — the skill part only decides WHEN you get it
            }
            Say("cast");
            StartCoroutine(Fly());
        }

        IEnumerator Fly()
        {
            st = St.Flying;
            Sfx.Play("cast_whoosh", tip.position, 1f);
            XRIn.Haptic(Hand.Right, 0.4f, 0.06f);
            bobber.gameObject.SetActive(true); line.enabled = true;
            var target = PondCenter + new Vector3(Random.Range(-0.5f, 0.5f), 0.01f, Random.Range(-0.35f, 0.3f));
            bobber.position = tip.position;
            yield return StartCoroutine(Fx.Arc(bobber, W(target), 0.5f, 0.7f));
            Sfx.Play("splash", bobber.position, 0.8f);
            Juice.Sparks(bobber.position, Palette.Teal, 10);
            st = St.Waiting;
            Status("WAIT FOR IT...", Palette.Text);
            biteAt = Time.time + Random.Range(1.5f, 4.5f);
        }

        void Update()
        {
            // swimming fish
            for (int i = 0; i < fishes.Count; i++)
            {
                float t = Time.time * (0.25f + i * 0.04f) + i * 1.3f;
                float rad = 0.35f + 0.25f * Mathf.Sin(i * 2.1f + Time.time * 0.1f);
                var p = new Vector3(Mathf.Cos(t) * rad, -0.05f - 0.03f * (i % 3), Mathf.Sin(t) * rad);
                var f = fishes[i];
                if (st == St.Bite && i == 0 && bobber.gameObject.activeSelf) p = pond.InverseTransformPoint(bobber.position) + new Vector3(0, -0.06f, 0);
                var d = p - f.localPosition;
                if (d.sqrMagnitude > 1e-6f) f.localRotation = Quaternion.Slerp(f.localRotation, Quaternion.LookRotation(d), Time.deltaTime * 6f);
                f.localPosition = Vector3.Lerp(f.localPosition, p, Time.deltaTime * 2f);
            }
            if (line.enabled)
            {
                var a = tip.position; var b = bobber.position;
                for (int i = 0; i < line.positionCount; i++)
                {
                    float k = i / (float)(line.positionCount - 1);
                    var p = Vector3.Lerp(a, b, k); p.y -= Mathf.Sin(k * Mathf.PI) * (st == St.Bite || st == St.Reeling ? 0.02f : 0.12f);
                    line.SetPosition(i, p);
                }
            }
            if (st == St.Waiting)
            {
                bobber.position += Vector3.up * Mathf.Sin(Time.time * 3f) * 0.0004f;
                if (Time.time >= biteAt) Bite();
            }
            else if (st == St.Bite)
            {
                bobber.position = bobRest + Vector3.down * (0.02f + Mathf.Abs(Mathf.Sin(Time.time * 18f)) * 0.025f);
                if (Time.time > biteUntil) StartCoroutine(Reel(false));
            }
            // physical gestures with the rod in hand
            if (rodGrab != null && rodGrab.isSelected)
            {
                var v = VelocityTracker.On(tip).velocity;
                if (st == St.Idle && Time.time > castBlockUntil && Vector3.Dot(v, transform.forward) > 2.2f && v.magnitude > 2.6f) Action();
                else if (st == St.Bite && (v.y > 1.4f || Vector3.Dot(v, -transform.forward) > 1.8f)) Action();
            }
            // only touch the buttons when the state changes (SetInteractable allocates a property block every call)
            int bs = (st == St.Idle ? 1 : 0) | (st == St.Bite || st == St.Waiting ? 2 : 0);
            if (castBtn != null && bs != btnState) { btnState = bs; castBtn.SetInteractable((bs & 1) != 0); reelBtn.SetInteractable((bs & 2) != 0); }
        }

        void Bite()
        {
            st = St.Bite;
            biteUntil = Time.time + 1.5f;
            Sfx.Play("bite", bobber.position, 1f);
            Sfx.Play("splash", bobber.position, 0.5f, 1.3f);
            bobRest = bobber.position;
            Status("FISH ON! PULL!", Palette.Gold);
            Say("fish_on");
            StartCoroutine(XRIn.Pattern(Hand.Right, 4, 0.8f, 0.07f));
            StartCoroutine(XRIn.Pattern(Hand.Left, 4, 0.5f, 0.07f));
        }

        IEnumerator Reel(bool hooked)
        {
            st = St.Reeling;
            if (!hooked)
            {
                Status("IT GOT AWAY · FREE RECAST", Palette.Coral);
                Say("miss");
                Sfx.Play("splash", bobber.position, 0.5f, 0.8f);
                yield return StartCoroutine(Fx.Arc(bobber, tip.position, 0.2f, 0.4f));
                bobber.gameObject.SetActive(false); line.enabled = false;
                st = St.Idle;   // result and stake stay: next cast is free and lands the same catch
                yield break;
            }
            var loop = Sfx.Loop("reel_click_loop", tip, 0.7f);
            // the catch comes out of the water
            var fk = new MeshKit();
            int r = result;
            Transform fish;
            bool duck = r == 0 && Odds.R() < 0.02;
            if (r == 0 && !duck)
            {
                fk.Box(new Vector3(0, 0.04f, 0), new Vector3(0.08f, 0.12f, 0.08f), FishCol[0], 0.1f);
                fk.Box(new Vector3(0, -0.01f, 0.05f), new Vector3(0.08f, 0.05f, 0.15f), FishCol[0], 0.1f);
            }
            else if (duck)
            {
                fk.Sphere(Vector3.zero, new Vector3(0.07f, 0.055f, 0.09f), 10, Palette.Gold, 0.8f);
                fk.Sphere(new Vector3(0, 0.07f, 0.05f), 0.04f, 10, Palette.Gold, 0.8f);
                fk.Box(new Vector3(0, 0.065f, 0.1f), new Vector3(0.03f, 0.01f, 0.03f), Palette.Coral, 1f);
            }
            else
            {
                float s = 0.7f + r * 0.18f;
                fk.Sphere(Vector3.zero, new Vector3(0.05f, 0.04f, 0.13f) * s, 12, FishCol[r], 0.8f);
                fk.Box(new Vector3(0, 0, -0.15f * s), new Vector3(0.006f, 0.08f * s, 0.05f * s), FishCol[r], 0.9f);
                fk.Box(new Vector3(0, 0.045f * s, 0), new Vector3(0.006f, 0.04f * s, 0.06f * s), FishCol[r], 0.9f);
            }
            fish = fk.Spawn("Catch", transform, transform.InverseTransformPoint(bobber.position), Quaternion.Euler(-70, 0, 0), Palette.Neon, false).transform;
            if (r >= 4) Build.GlowSprite(fish, Vector3.zero, 0.5f, FishCol[r], 0.9f);
            bobber.gameObject.SetActive(false);
            Sfx.Play("splash", fish.position, 1f, 1.1f);
            Juice.Sparks(fish.position, Palette.Teal, 24);
            var show = W(new Vector3(0, 1.55f, 0.55f));
            float t = 0;
            var from = fish.position;
            while (t < 0.9f)
            {
                t += Time.deltaTime;
                fish.position = Vector3.Lerp(from, show, Fx.EaseOut(t / 0.9f)) + Vector3.up * Mathf.Sin(t / 0.9f * Mathf.PI) * 0.3f;
                fish.Rotate(0, 0, Mathf.Sin(Time.time * 40f) * 4f, Space.Self);
                line.SetPosition(line.positionCount - 1, fish.position);
                yield return null;
            }
            if (loop != null) Destroy(loop.gameObject);
            line.enabled = false;
            float mult = Table.mult[r];
            string name = duck ? "RUBBER DUCK?!" : Table.names[r];
            catchT.text = name + (mult > 0 ? "  " + mult.ToString("0.##") + "x" : "");
            catchT.color = mult >= 4 ? Palette.Gold : mult >= 1 ? Palette.Mint : Palette.Muted;
            Status(name, catchT.color);
            if (mult >= 1) { Sfx.Play("catch_fanfare", show, mult >= 4 ? 1f : 0.7f); Say("catch"); }
            if (duck) { Sfx.Play("secret_found", show, 1f); GameState.I.Unlock("QUACK", "FISHED OUT THE RUBBER DUCK", 500); }
            if (r == 6) GameState.I.Unlock("GOLDEN KOI", "CAUGHT THE GOLDEN KOI", 2500);
            GameState.I.Record("fish");
            PayDirect((long)(stake * (double)mult), stake, show);
            result = -1; stake = 0;
            float spin = 0;
            while (spin < 1.6f) { spin += Time.deltaTime; fish.Rotate(0, 180f * Time.deltaTime, 0, Space.World); yield return null; }
            yield return StartCoroutine(Fx.Move(fish, W(PondCenter), 0.5f, false));
            Sfx.Play("splash", fish.position, 0.5f);
            Destroy(fish.gameObject);
            st = St.Idle;
        }
    }
}
