using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>The places of MAFIA MODE outside the tower, modelled in code in the Casino FREE low-poly style:
    /// the half-ruined house in a caravan park (where every day starts, Don Tuchi and his car outside), three
    /// "quiet places" where the mafia takes you when you come up short, and a dark limbo for the game-over / ending
    /// screens. Built once at runtime, far from the tower.</summary>
    public class RunWorld : MonoBehaviour
    {
        public static readonly Vector3 HomePos = new Vector3(700f, 0f, 0f);
        static readonly Vector3[] ExecPos = { new Vector3(-700f, 0f, 0f), new Vector3(0f, 0f, 700f), new Vector3(700f, 0f, 700f) };
        public static readonly Vector3 LimboPos = new Vector3(-700f, 0f, 700f);
        public static readonly string[] ExecNames = { "EL MUELLE", "EL DESGUACE", "EL DESCAMPADO" };

        public Transform home, limbo;
        public Vector3 HomeSpawn { get { return home.TransformPoint(new Vector3(-2.1f, 0.1f, -1.6f)); } }
        public float HomeYaw { get { return 25f; } }
        public Vector3 LimboSpot { get { return limbo.position; } }
        public AvatarBody don, rata;
        public Car car;
        public Transform ratStall, calendar;
        TextMeshPro calendarT;

        public class Site { public Transform root; public Vector3 spot; public float yaw; public AvatarBody don; public List<AvatarBody> goons = new List<AvatarBody>(); public Car car; }
        public readonly List<Site> sites = new List<Site>();

        // ------------------------------------------------------------------ palette (Casino FREE: flat, warm, chunky)
        static readonly Color Dirt = Palette.Hex("8A7356"), Grass = Palette.Hex("6E8F4E"), Plank = Palette.Hex("9A7652"), PlankDark = Palette.Hex("6E5238"),
            Roof = Palette.Hex("5B4A63"), Cream = Palette.Hex("EDE3CC"), Rust = Palette.Hex("B0603A"), Asphalt = Palette.Hex("3B3644"),
            Water = Palette.Hex("2E6E9E"), Leaf = Palette.Hex("4F8A4A"), Trunk = Palette.Hex("6B4A30"), CarBlack = Palette.Hex("17141F"), Chrome = Palette.Hex("C9CCD8");

        static Material _mat;
        /// <summary>Matte, toned-down vertex-colour material for the outdoor sets (full sun and open sky).</summary>
        static Material Mat { get { if (_mat == null) _mat = Palette.Flat(new Color(0.6f, 0.58f, 0.6f), 0.02f, 0.03f, 0.25f); return _mat; } }

        public static RunWorld Build()
        {
            var go = new GameObject("MAFIA MODE · World");
            var w = go.AddComponent<RunWorld>();
            w.BuildHome();
            for (int i = 0; i < ExecPos.Length; i++) w.sites.Add(w.BuildSite(i));
            w.BuildLimbo();
            return w;
        }

        // ------------------------------------------------------------------ helpers
        static GameObject Solid(string name, Transform parent, Vector3 center, Vector3 size, Color col, float emit = 0f, bool collider = true, Quaternion? rot = null)
        {
            var k = new MeshKit();
            k.Box(Vector3.zero, size, col, emit);
            var go = k.Spawn(name, parent, center, rot ?? Quaternion.identity, Mat, false);
            if (collider) global::Ascend.Build.BoxCol(go, Vector3.zero, size);
            return go;
        }

        static void Ground(Transform root, float size, Color col, Color patch, int seed)
        {
            var k = new MeshKit();
            k.Plane(Vector3.zero, new Vector2(size, size), Quaternion.identity, col, 0f);
            var rnd = new System.Random(seed);
            for (int i = 0; i < 40; i++)
            {
                float a = (float)rnd.NextDouble() * 6.283f, r = 3f + (float)rnd.NextDouble() * size * 0.4f;
                k.Disc(new Vector3(Mathf.Cos(a) * r, 0.01f, Mathf.Sin(a) * r), 0.8f + (float)rnd.NextDouble() * 2.4f, 10, Quaternion.identity, patch, 0f);
            }
            k.Spawn("Ground", root, Mat);
            global::Ascend.Build.BoxCol(Global(root, "GroundCol"), new Vector3(0, -0.5f, 0), new Vector3(size, 1f, size));
        }
        static GameObject Global(Transform parent, string name) { var g = new GameObject(name); g.transform.SetParent(parent, false); return g; }

        static void Walls(Transform root, float half)
        {
            // invisible bounds so nobody wanders off into the void
            foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                var g = Global(root, "Bound");
                g.transform.localPosition = d * half + Vector3.up * 2f;
                var c = g.AddComponent<BoxCollider>();
                c.size = new Vector3(Mathf.Abs(d.x) > 0 ? 0.5f : half * 2f, 4f, Mathf.Abs(d.z) > 0 ? 0.5f : half * 2f);
            }
        }

        static void Tree(MeshKit k, Vector3 p, float s)
        {
            k.Cylinder(p + Vector3.up * 0.9f * s, 0.18f * s, 0.14f * s, 1.8f * s, 7, Quaternion.identity, Trunk);
            k.Cylinder(p + Vector3.up * 2.4f * s, 1.3f * s, 0.05f * s, 2.0f * s, 8, Quaternion.identity, Leaf);
            k.Cylinder(p + Vector3.up * 3.4f * s, 0.95f * s, 0.05f * s, 1.6f * s, 8, Quaternion.identity, Leaf * 1.08f);
        }

        static Light PointLight(Transform parent, Vector3 local, Color c, float intensity, float range)
        {
            var g = Global(parent, "Light"); g.transform.localPosition = local;
            var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            return l;
        }

        static TextMeshPro Sign(Transform parent, Vector3 local, Vector3 faceFromLocal, string text, float h, Color c, float width = 0f)
        {
            var t = Txt.Make("Sign", parent, local, text, h, c, Palette.Display, TextAlignmentOptions.Center, width);
            var d = local - faceFromLocal; d.y = 0;
            t.transform.localRotation = Quaternion.LookRotation(d.normalized);
            return t;
        }

        // ------------------------------------------------------------------ the car (opening rear-right door)
        public class Car
        {
            public Transform root, door; float open;
            public Vector3 DoorPoint { get { return root.TransformPoint(new Vector3(-0.55f, 0f, -1.55f)); } }
            public void SetOpen(float k) { open = Mathf.Clamp01(k); door.localRotation = Quaternion.Euler(0, -72f * Fx.EaseOut(open), 0); }
            public float Open { get { return open; } }
        }

        Car BuildCar(Transform parent, Vector3 local, float yaw, string name)
        {
            var root = global::Ascend.Build.Empty(name, parent, local, Quaternion.Euler(0, yaw, 0)).transform;
            var k = new MeshKit();
            // forward is +X: body, cabin, hood, trunk, bumpers, wheels, lights
            k.Box(new Vector3(0, 0.62f, 0), new Vector3(4.6f, 0.55f, 1.86f), CarBlack, 0f);
            k.Box(new Vector3(-0.25f, 1.12f, 0), new Vector3(2.3f, 0.5f, 1.66f), CarBlack * 1.15f, 0f);
            k.Box(new Vector3(-0.25f, 1.13f, 0), new Vector3(2.0f, 0.4f, 1.68f), Palette.Hex("1E2A3A"), 0.05f);   // tinted glass band
            k.Box(new Vector3(2.32f, 0.5f, 0), new Vector3(0.12f, 0.22f, 1.9f), Chrome, 0f);
            k.Box(new Vector3(-2.32f, 0.5f, 0), new Vector3(0.12f, 0.22f, 1.9f), Chrome, 0f);
            foreach (var z in new[] { -0.65f, 0.65f })
            {
                k.Box(new Vector3(2.3f, 0.72f, z), new Vector3(0.06f, 0.14f, 0.36f), Palette.Hex("FFF4C2"), 1.2f);
                k.Box(new Vector3(-2.3f, 0.72f, z), new Vector3(0.06f, 0.12f, 0.34f), Palette.Red, 1f);
            }
            foreach (var x in new[] { -1.45f, 1.45f })
                foreach (var z in new[] { -0.86f, 0.86f })
                {
                    k.Cylinder(new Vector3(x, 0.36f, z), 0.36f, 0.36f, 0.26f, 14, Quaternion.Euler(90, 0, 0), Palette.Hex("111015"));
                    k.Cylinder(new Vector3(x, 0.36f, z * 1.16f), 0.2f, 0.2f, 0.02f, 12, Quaternion.Euler(90, 0, 0), Chrome);
                }
            k.Spawn("Body", root, Mat);
            global::Ascend.Build.BoxCol(root.gameObject, new Vector3(0, 0.8f, 0), new Vector3(4.6f, 1.3f, 1.9f));
            // rear-right door (right side is -Z): hinge at its front edge, opens outward
            var hinge = global::Ascend.Build.Empty("DoorHinge", root, new Vector3(0.05f, 0, -0.93f)).transform;
            var dk = new MeshKit();
            dk.Box(new Vector3(-0.55f, 0.72f, -0.02f), new Vector3(1.1f, 0.62f, 0.06f), CarBlack * 1.1f, 0f);
            dk.Box(new Vector3(-0.55f, 1.14f, -0.02f), new Vector3(0.95f, 0.36f, 0.05f), Palette.Hex("1E2A3A"), 0.05f);
            dk.Box(new Vector3(-0.92f, 0.85f, -0.07f), new Vector3(0.16f, 0.04f, 0.04f), Chrome, 0f);
            dk.Spawn("Door", hinge, Mat);
            // dark interior behind the door so an open door shows a seat, not the void
            var ik = new MeshKit();
            ik.Box(new Vector3(-0.6f, 0.75f, -0.45f), new Vector3(0.9f, 0.3f, 0.6f), Palette.Hex("2A2030"), 0f);
            ik.Box(new Vector3(-0.98f, 1.05f, -0.45f), new Vector3(0.14f, 0.6f, 0.62f), Palette.Hex("2A2030"), 0f);
            ik.Spawn("Interior", root, Mat);
            var c = new Car { root = root, door = hinge };
            c.SetOpen(0f);
            return c;
        }

        // ------------------------------------------------------------------ HOME: caravan park, half-ruined house
        void BuildHome()
        {
            home = global::Ascend.Build.Empty("Home · Camping El Último Recurso", transform, HomePos).transform;
            Ground(home, 140f, Dirt, Grass, 11);
            Walls(home, 22f);
            var k = new MeshKit();

            // ---- the house: plank floor, walls with gaps, a half roof, boarded windows
            Solid("Floor", home, new Vector3(0, 0.05f, 0), new Vector3(7.2f, 0.1f, 6.2f), PlankDark);
            // back wall (-Z) with a hole
            Solid("WallBackL", home, new Vector3(-2.2f, 1.4f, -3f), new Vector3(2.8f, 2.8f, 0.16f), Plank);
            Solid("WallBackR", home, new Vector3(2.3f, 1.4f, -3f), new Vector3(2.6f, 2.8f, 0.16f), Plank);
            Solid("WallBackTop", home, new Vector3(0.05f, 2.4f, -3f), new Vector3(1.5f, 0.8f, 0.16f), Plank);
            // left / right walls with window holes boarded up
            Solid("WallL1", home, new Vector3(-3.6f, 1.4f, -1.8f), new Vector3(0.16f, 2.8f, 2.4f), Plank);
            Solid("WallL2", home, new Vector3(-3.6f, 1.4f, 2.1f), new Vector3(0.16f, 2.8f, 1.8f), Plank);
            Solid("WallL3", home, new Vector3(-3.6f, 0.5f, 0.35f), new Vector3(0.16f, 1.0f, 1.7f), Plank);
            Solid("WallL4", home, new Vector3(-3.6f, 2.5f, 0.35f), new Vector3(0.16f, 0.6f, 1.7f), Plank);
            Solid("WallR", home, new Vector3(3.6f, 1.4f, 0f), new Vector3(0.16f, 2.8f, 6.2f), Plank);
            // front wall (+Z): door gap at x 0.6..1.8
            Solid("WallF1", home, new Vector3(-1.5f, 1.4f, 3f), new Vector3(4.2f, 2.8f, 0.16f), Plank);
            Solid("WallF2", home, new Vector3(2.7f, 1.4f, 3f), new Vector3(1.8f, 2.8f, 0.16f), Plank);
            Solid("WallF3", home, new Vector3(1.2f, 2.55f, 3f), new Vector3(1.2f, 0.5f, 0.16f), Plank);
            // boards nailed across the window holes, a loose plank leaning on the wall
            k.Box(new Vector3(-3.66f, 1.5f, 0.35f), new Vector3(0.05f, 0.18f, 1.9f), Quaternion.Euler(28, 0, 0), PlankDark, 0f);
            k.Box(new Vector3(-3.66f, 1.5f, 0.35f), new Vector3(0.05f, 0.18f, 1.9f), Quaternion.Euler(-24, 0, 0), PlankDark, 0f);
            k.Box(new Vector3(0.05f, 1.55f, -3.07f), new Vector3(1.6f, 0.16f, 0.05f), Quaternion.Euler(0, 0, 18), PlankDark, 0f);
            k.Box(new Vector3(3.3f, 1.2f, 2.0f), new Vector3(0.12f, 2.4f, 0.22f), Quaternion.Euler(0, 0, -14), Plank, 0f);
            // half a roof (the other half is a hole with the sky in it) and a fallen beam
            k.Box(new Vector3(-1.9f, 2.9f, 0), new Vector3(3.8f, 0.16f, 6.6f), Quaternion.Euler(0, 0, 6), Roof, 0f);
            k.Box(new Vector3(2.2f, 2.95f, -2.2f), new Vector3(3.2f, 0.16f, 2.2f), Quaternion.Euler(0, 0, -5), Roof, 0f);
            k.Box(new Vector3(2.9f, 1.2f, 0.2f), new Vector3(0.18f, 0.18f, 3.4f), Quaternion.Euler(32, 8, 0), PlankDark, 0f);
            // furniture: mattress on the floor, crate with an old TV, fridge, trash bags, pizza boxes, a hanging bulb
            k.Box(new Vector3(-2.3f, 0.2f, -1.6f), new Vector3(1.4f, 0.22f, 2.0f), Palette.Hex("D8CFB6"), 0f);
            k.Box(new Vector3(-2.3f, 0.36f, -2.35f), new Vector3(0.9f, 0.12f, 0.4f), Palette.Hex("F2ECDD"), 0f);
            k.Disc(new Vector3(-2.1f, 0.315f, -1.3f), 0.28f, 10, Quaternion.identity, Palette.Hex("B9A77E"), 0f);
            k.Box(new Vector3(1.8f, 0.3f, -2.3f), new Vector3(0.8f, 0.6f, 0.6f), Plank, 0f);
            k.Box(new Vector3(1.8f, 0.86f, -2.3f), new Vector3(0.7f, 0.52f, 0.5f), Palette.Hex("2B2733"), 0f);
            k.Box(new Vector3(1.8f, 0.88f, -2.04f), new Vector3(0.52f, 0.36f, 0.02f), Palette.Hex("6FA8D8"), 0.6f);
            k.Box(new Vector3(3.05f, 0.9f, -2.3f), new Vector3(0.7f, 1.8f, 0.65f), Palette.Hex("DCD6C8"), 0f);
            k.Box(new Vector3(2.71f, 1.2f, -2.1f), new Vector3(0.04f, 0.5f, 0.06f), Chrome, 0f);
            k.Sphere(new Vector3(-3.0f, 0.3f, 2.3f), new Vector3(0.38f, 0.32f, 0.38f), 8, Palette.Hex("1B1A22"), 0f);
            k.Sphere(new Vector3(-2.5f, 0.25f, 2.5f), new Vector3(0.3f, 0.25f, 0.3f), 8, Palette.Hex("1B1A22"), 0f);
            for (int i = 0; i < 4; i++) k.Box(new Vector3(0.3f + i * 0.03f, 0.13f + i * 0.05f, -0.4f), new Vector3(0.45f, 0.04f, 0.45f), Quaternion.Euler(0, i * 9f, 0), Palette.Hex("E7B66B"), 0f);
            k.Bar(new Vector3(-0.6f, 2.85f, -0.2f), new Vector3(-0.6f, 2.3f, -0.2f), 0.015f, Palette.Hex("222222"), 0f);
            k.Sphere(new Vector3(-0.6f, 2.24f, -0.2f), 0.07f, 8, Palette.Hex("FFE7A0"), 2.5f);
            k.Spawn("House", home, Mat);
            PointLight(home, new Vector3(-0.6f, 2.1f, -0.2f), Palette.Hex("FFD08A"), 1.4f, 7f);
            // graffiti and the calendar Don Tuchi keeps updating
            Sign(home, new Vector3(-1.5f, 1.85f, 2.9f), new Vector3(-1.5f, 1.6f, 0f), "DEBES PASTA", 0.42f, Palette.Red);
            calendar = global::Ascend.Build.Empty("Calendar", home, new Vector3(3.5f, 1.6f, 0.6f)).transform;
            var ck = new MeshKit(); ck.Box(Vector3.zero, new Vector3(0.04f, 0.7f, 0.55f), Palette.Hex("F7F2E6"), 0.1f); ck.Box(new Vector3(-0.025f, 0.26f, 0), new Vector3(0.01f, 0.16f, 0.55f), Palette.Red, 0.3f);
            ck.Spawn("Paper", calendar, Mat);
            calendarT = Txt.Make("CalendarText", calendar, new Vector3(-0.03f, -0.06f, 0), "", 0.1f, Palette.Hex("2B2733"), Palette.Display, TextAlignmentOptions.Center, 0.5f);
            calendarT.transform.localRotation = Quaternion.LookRotation(Vector3.right);

            // ---- the caravan park
            var pk = new MeshKit();
            Caravan(pk, new Vector3(-11f, 0, 9f), 20f, Palette.Hex("E9DFC4"), Palette.Hex("6FA8A0"));
            Caravan(pk, new Vector3(10f, 0, 13f), -35f, Palette.Hex("F1E8D2"), Palette.Hex("D98E73"));
            Caravan(pk, new Vector3(-13f, 0, -7f), 95f, Palette.Hex("E2D6BC"), Palette.Hex("8C7BB8"));
            // picnic table, campfire, string lights, a lamp post
            pk.Box(new Vector3(6f, 0.75f, -6f), new Vector3(2f, 0.08f, 0.9f), Plank, 0f);
            foreach (var x in new[] { 5.2f, 6.8f }) pk.Box(new Vector3(x, 0.37f, -6f), new Vector3(0.08f, 0.74f, 0.8f), PlankDark, 0f);
            foreach (var z in new[] { -6.7f, -5.3f }) pk.Box(new Vector3(6f, 0.45f, z), new Vector3(2f, 0.06f, 0.3f), Plank, 0f);
            for (int i = 0; i < 9; i++) { float a = i * 40f * Mathf.Deg2Rad; pk.Sphere(new Vector3(5.5f + Mathf.Cos(a) * 0.7f, 0.12f, 11f + Mathf.Sin(a) * 0.7f), 0.18f, 6, Palette.Hex("8D8794"), 0f); }
            pk.Cylinder(new Vector3(5.5f, 0.25f, 11f), 0.35f, 0.02f, 0.5f, 7, Quaternion.identity, Palette.Hex("FF8A3D"), 2.5f);
            pk.Cylinder(new Vector3(5.5f, 0.12f, 11f), 0.5f, 0.45f, 0.08f, 8, Quaternion.identity, Palette.Hex("4A3524"), 0f);
            pk.Cylinder(new Vector3(-6f, 2f, 3.6f), 0.07f, 0.07f, 4f, 8, Quaternion.identity, Palette.Hex("3A3444"), 0f);
            pk.Sphere(new Vector3(-6f, 4.05f, 3.6f), 0.16f, 8, Palette.Hex("FFE7A0"), 2.5f);
            for (int i = 0; i <= 10; i++)
            {
                float u = i / 10f; var a = new Vector3(-6f, 3.6f, 3.6f); var b = new Vector3(-10f, 2.6f, 8.2f);
                var p = Vector3.Lerp(a, b, u) + Vector3.down * Mathf.Sin(u * Mathf.PI) * 0.45f;
                pk.Sphere(p, 0.06f, 6, i % 3 == 0 ? Palette.Gold : i % 3 == 1 ? Palette.Magenta : Palette.Teal, 2.2f);
            }
            // trees and a fence around the park
            var rnd = new System.Random(5);
            for (int i = 0; i < 28; i++)
            {
                float a = (float)rnd.NextDouble() * 6.283f, r = 17f + (float)rnd.NextDouble() * 30f;
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                if (Mathf.Abs(p.x) < 6f && p.z > 4f && p.z < 20f) continue;
                Tree(pk, p, 0.9f + (float)rnd.NextDouble() * 0.7f);
            }
            for (int i = 0; i < 44; i++)
            {
                float x = -22f + i; pk.Box(new Vector3(x, 0.6f, 21.6f), new Vector3(0.08f, 1.2f, 0.08f), PlankDark, 0f); pk.Box(new Vector3(x, 0.6f, -21.6f), new Vector3(0.08f, 1.2f, 0.08f), PlankDark, 0f);
                pk.Box(new Vector3(21.6f, 0.6f, x), new Vector3(0.08f, 1.2f, 0.08f), PlankDark, 0f); pk.Box(new Vector3(-21.6f, 0.6f, x), new Vector3(0.08f, 1.2f, 0.08f), PlankDark, 0f);
            }
            foreach (var y in new[] { 0.5f, 0.95f })
            {
                pk.Box(new Vector3(0, y, 21.6f), new Vector3(43.2f, 0.06f, 0.04f), Plank, 0f); pk.Box(new Vector3(0, y, -21.6f), new Vector3(43.2f, 0.06f, 0.04f), Plank, 0f);
                pk.Box(new Vector3(21.6f, y, 0), new Vector3(0.04f, 0.06f, 43.2f), Plank, 0f); pk.Box(new Vector3(-21.6f, y, 0), new Vector3(0.04f, 0.06f, 43.2f), Plank, 0f);
            }
            // gravel drive where the car waits
            pk.Box(new Vector3(0, 0.015f, 8.2f), new Vector3(12f, 0.03f, 3.6f), Palette.Hex("A39A8C"), 0f);
            pk.Spawn("Park", home, Mat);
            PointLight(home, new Vector3(5.5f, 0.8f, 11f), Palette.Hex("FF9A4A"), 2.2f, 6f);
            PointLight(home, new Vector3(-6f, 3.8f, 3.6f), Palette.Hex("FFE2A0"), 1.2f, 8f);
            Sign(home, new Vector3(0f, 3.1f, 20.8f), new Vector3(0f, 2f, 0f), "CAMPING EL ÚLTIMO RECURSO", 0.6f, Palette.Gold, 14f);

            // ---- the black car on the drive, Don Tuchi next to it
            car = BuildCar(home, new Vector3(0.6f, 0, 8.6f), 0f, "Don's Car");
            don = Avatars.Create("DON TUCHI", home, new Vector3(-1.4f, 0, 6.3f), 160f, Rarity.Legendary, RoninRole.Security, 1.8f);
            don.pose = Pose.Idle;   // relaxed, hands down: he owns the place (the security stance held one arm out all day)
            { var dh = don as HumanoidBody; if (dh != null) dh.idleSet = new[] { "IdleBack", "Idle", "IdleArms" }; }
            Sign(home, new Vector3(-1.4f, 2.25f, 6.3f), new Vector3(-1.4f, 2f, 0f), "DON TUCHI", 0.16f, Palette.Gold).gameObject.AddComponent<Billboard>();

            // ---- El Rata's black-market stall
            ratStall = global::Ascend.Build.Empty("Stall", home, new Vector3(-7.6f, 0, 6.2f), Quaternion.Euler(0, 210f, 0)).transform;
            var sk = new MeshKit();
            sk.Box(new Vector3(0, 0.75f, 0), new Vector3(1.8f, 0.06f, 0.8f), Palette.Hex("5A6E8C"), 0f);
            foreach (var x in new[] { -0.8f, 0.8f }) sk.Bar(new Vector3(x, 0, 0), new Vector3(x, 0.75f, 0), 0.04f, Chrome, 0f);
            sk.Box(new Vector3(-0.5f, 0.9f, 0), new Vector3(0.3f, 0.24f, 0.3f), Palette.Mint, 0.2f);
            sk.Box(new Vector3(0.1f, 0.85f, 0.1f), new Vector3(0.24f, 0.14f, 0.2f), Palette.Gold, 0.3f);
            sk.Cylinder(new Vector3(0.55f, 0.92f, -0.1f), 0.08f, 0.08f, 0.28f, 10, Quaternion.identity, Palette.Lime, 0.5f);
            sk.Box(new Vector3(0, 1.8f, 0.35f), new Vector3(2.1f, 0.06f, 1.1f), Quaternion.Euler(-10, 0, 0), Palette.Hex("C0392B"), 0f);
            foreach (var x in new[] { -1f, 1f }) sk.Bar(new Vector3(x, 0, 0.75f), new Vector3(x, 1.75f, 0.75f), 0.04f, Chrome, 0f);
            sk.Spawn("StallMesh", ratStall, Mat);
            global::Ascend.Build.BoxCol(ratStall.gameObject, new Vector3(0, 0.4f, 0), new Vector3(1.8f, 0.8f, 0.8f));
            var lib = AssetLibrary.I;
            rata = lib != null && lib.android != null
                ? (AvatarBody)HumanoidBody.CreateFrom(lib.android, lib.humanoidController, lib, "EL RATA", ratStall, new Vector3(0, 0, 0.75f), 180f, Rarity.Rare, RoninRole.Player, 1.66f, HumanoidBody.Kind.Extra)
                : Avatars.Create("EL RATA", ratStall, new Vector3(0, 0, 0.75f), 180f, Rarity.Rare, RoninRole.Player, 1.66f);
            Txt.Make("StallSign", ratStall, new Vector3(0, 2.05f, -0.15f), "EL RATA · MERCADO NEGRO", 0.16f, Palette.Gold, Palette.Display, TextAlignmentOptions.Center, 3f).gameObject.AddComponent<Billboard>();

            // ---- things to do while Don Tuchi waits (from Roberto's Gamble With Your Friends street)
            home.gameObject.AddComponent<HomeFun>().Build(home);
            home.gameObject.AddComponent<HomeLife>().Build(home);   // neighbours, animals, fires, junk: the park is alive
        }

        static void Caravan(MeshKit k, Vector3 p, float yaw, Color body, Color stripe)
        {
            var r = Quaternion.Euler(0, yaw, 0);
            k.Push(p, r);
            k.Box(new Vector3(0, 1.35f, 0), new Vector3(4.6f, 2.0f, 2.2f), body, 0f);
            k.Box(new Vector3(0, 1.0f, 0), new Vector3(4.62f, 0.3f, 2.22f), stripe, 0f);
            k.Box(new Vector3(0.6f, 1.6f, 1.11f), new Vector3(1.1f, 0.6f, 0.02f), Palette.Hex("3D5A73"), 0.15f);
            k.Box(new Vector3(-1.4f, 1.15f, 1.11f), new Vector3(0.7f, 1.6f, 0.02f), Palette.Hex("A3907A"), 0f);
            k.Cylinder(new Vector3(0.2f, 0.32f, 1.0f), 0.32f, 0.32f, 0.22f, 12, Quaternion.Euler(90, 0, 0), Palette.Hex("1B1A22"));
            k.Cylinder(new Vector3(0.2f, 0.32f, -1.0f), 0.32f, 0.32f, 0.22f, 12, Quaternion.Euler(90, 0, 0), Palette.Hex("1B1A22"));
            k.Bar(new Vector3(2.3f, 0.45f, 0), new Vector3(3.2f, 0.3f, 0), 0.08f, Palette.Hex("3A3444"), 0f);
            k.Pop();
        }

        public void SetCalendar(int day)
        {
            if (calendarT != null) calendarT.text = "DÍA " + day + "\n<size=60%>QUEDAN " + Mathf.Max(0, Run.Days - day) + "</size>";
        }

        // ------------------------------------------------------------------ the three quiet places
        Site BuildSite(int i)
        {
            var s = new Site();
            s.root = global::Ascend.Build.Empty("Exec · " + ExecNames[i], transform, ExecPos[i]).transform;
            var k = new MeshKit();
            if (i == 0)
            {
                // the pier at the docks: planks over dark water, bollards, crates and a sack of cement
                var water = new MeshKit(); water.Plane(new Vector3(0, -0.8f, 0), new Vector2(400, 400), Quaternion.identity, Water, 0.15f); water.Spawn("Water", s.root, Mat);
                Solid("Pier", s.root, new Vector3(0, -0.15f, 2f), new Vector3(7f, 0.3f, 22f), Plank);
                for (int z = -8; z <= 12; z += 2) k.Box(new Vector3(0, 0.005f, z), new Vector3(7f, 0.01f, 0.05f), PlankDark, 0f);
                foreach (var x in new[] { -3.3f, 3.3f }) for (int z = -8; z <= 12; z += 4) k.Cylinder(new Vector3(x, 0.25f, z), 0.15f, 0.15f, 0.5f, 8, Quaternion.identity, Palette.Hex("3A3444"));
                k.Box(new Vector3(2.4f, 0.45f, 5f), new Vector3(0.9f, 0.9f, 0.9f), Rust, 0f);
                k.Box(new Vector3(2.5f, 1.2f, 5.1f), new Vector3(0.6f, 0.6f, 0.6f), Palette.Hex("6E8CA0"), 0f);
                k.Box(new Vector3(-2.2f, 0.25f, 3.6f), new Vector3(0.7f, 0.5f, 0.45f), Palette.Hex("CFC8BA"), 0f);
                k.Cylinder(new Vector3(-1.4f, 0.22f, 3.8f), 0.28f, 0.24f, 0.44f, 12, Quaternion.identity, Palette.Hex("8D8794"));
                k.Spawn("Dock", s.root, Mat);
                Sign(s.root, new Vector3(-2.2f, 0.27f, 3.37f), new Vector3(-2.2f, 0.3f, 0f), "CEMENTO", 0.1f, Palette.Hex("2B2733"));
            }
            else if (i == 1)
            {
                // the scrapyard: crushed cars stacked up, tyres, a chain-link fence and a crane
                Ground(s.root, 120f, Palette.Hex("6F6658"), Palette.Hex("5C5448"), 21);
                var cols = new[] { Rust, Palette.Hex("3F6F9E"), Palette.Hex("9E3F4A"), Palette.Hex("C9B458"), Palette.Hex("5E8A5A") };
                int n = 0;
                foreach (var x in new[] { -5f, 5f })
                    for (int z = -2; z <= 10; z += 3)
                        for (int y = 0; y < 3; y++)
                            k.Box(new Vector3(x + (n % 2) * 0.3f, 0.35f + y * 0.72f, z), new Vector3(2.6f, 0.7f, 1.6f), Quaternion.Euler(0, (n++ * 37) % 14 - 7, 0), cols[n % cols.Length] * 0.85f, 0f);
                for (int t = 0; t < 6; t++) k.Torus(new Vector3(-2.4f + t * 0.1f, 0.15f + t * 0.24f, 6f), 0.36f, 0.12f, 12, 6, Quaternion.identity, Palette.Hex("1B1A22"), 0f);
                for (int x = -12; x <= 12; x += 2) k.Bar(new Vector3(x, 0, 14f), new Vector3(x, 2.4f, 14f), 0.05f, Chrome, 0f);
                k.Box(new Vector3(0, 1.2f, 14f), new Vector3(24f, 2.4f, 0.02f), Palette.A(Chrome, 1f) * 0.6f, 0f);
                k.Bar(new Vector3(8f, 0, 18f), new Vector3(8f, 12f, 18f), 0.5f, Palette.Gold, 0f);
                k.Bar(new Vector3(8f, 12f, 18f), new Vector3(-4f, 13f, 12f), 0.4f, Palette.Gold, 0f);
                k.Spawn("Scrapyard", s.root, Mat);
            }
            else
            {
                // the empty lot: dry grass, a freshly dug hole with a shovel, one street light
                Ground(s.root, 120f, Palette.Hex("A49A5E"), Palette.Hex("8C8450"), 31);
                k.Disc(new Vector3(1.2f, 0.02f, 4.2f), 1.0f, 14, Quaternion.identity, Palette.Hex("1A1410"), 0f);
                k.Sphere(new Vector3(2.8f, 0.05f, 4.4f), new Vector3(1.1f, 0.5f, 0.9f), 10, Palette.Hex("6B4A30"), 0f);
                k.Bar(new Vector3(2.7f, 0.3f, 4.3f), new Vector3(2.9f, 1.5f, 4.2f), 0.04f, Palette.Hex("8A6E52"), 0f);
                k.Box(new Vector3(2.66f, 0.28f, 4.32f), new Vector3(0.24f, 0.32f, 0.03f), Quaternion.Euler(0, 0, 10), Chrome, 0f);
                k.Bar(new Vector3(-4f, 0, 6f), new Vector3(-4f, 5f, 6f), 0.12f, Palette.Hex("3A3444"), 0f);
                k.Bar(new Vector3(-4f, 5f, 6f), new Vector3(-2.8f, 5f, 6f), 0.08f, Palette.Hex("3A3444"), 0f);
                k.Sphere(new Vector3(-2.8f, 4.85f, 6f), 0.2f, 8, Palette.Hex("FFE7A0"), 2.5f);
                var rnd = new System.Random(9);
                for (int t = 0; t < 14; t++) { float a = (float)rnd.NextDouble() * 6.283f; Tree(k, new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (20f + (float)rnd.NextDouble() * 25f), 1.1f); }
                k.Spawn("Lot", s.root, Mat);
                PointLight(s.root, new Vector3(-2.8f, 4.6f, 6f), Palette.Hex("FFE2A0"), 2f, 12f);
            }
            Walls(s.root, 12f);
            // you stand here; Don in front of you, two goons, the car behind you with its lights on
            s.spot = s.root.position; s.yaw = 0f;
            s.don = Avatars.Create("DON TUCHI", s.root, new Vector3(0, 0, 2.8f), 180f, Rarity.Legendary, RoninRole.Security, 1.8f);
            s.don.pose = Pose.Idle;
            foreach (var x in new[] { -1.4f, 1.4f })
            {
                var g = Avatars.Create("GOON", s.root, new Vector3(x * 1.25f, 0, 2.3f), 180f + x * 12f, Rarity.Common, RoninRole.Security, 1.9f);
                g.pose = Pose.Idle; s.goons.Add(g);
                var gh = g as HumanoidBody; if (gh != null) gh.idleSet = x < 0 ? new[] { "IdleBack", "Idle", "IdleEar" } : new[] { "IdleArms", "Idle", "IdleBack" };
            }
            s.car = BuildCar(s.root, new Vector3(-1.2f, 0, -4.5f), -90f + 20f, "Car");
            PointLight(s.root, new Vector3(-0.6f, 0.9f, -2.2f), Palette.Hex("FFF4C2"), 2.2f, 9f);
            s.root.gameObject.SetActive(false);
            return s;
        }

        // ------------------------------------------------------------------ the limbo (game over / ending)
        void BuildLimbo()
        {
            limbo = global::Ascend.Build.Empty("Limbo", transform, LimboPos).transform;
            var dark = Palette.Hex("0B0910");
            Solid("Floor", limbo, new Vector3(0, -0.05f, 0), new Vector3(14f, 0.1f, 14f), Palette.Hex("15111F"));
            Solid("Ceil", limbo, new Vector3(0, 5f, 0), new Vector3(14f, 0.1f, 14f), dark, 0f, false);
            Solid("N", limbo, new Vector3(0, 2.5f, 7f), new Vector3(14f, 5f, 0.1f), dark);
            Solid("S", limbo, new Vector3(0, 2.5f, -7f), new Vector3(14f, 5f, 0.1f), dark);
            Solid("E", limbo, new Vector3(7f, 2.5f, 0), new Vector3(0.1f, 5f, 14f), dark);
            Solid("W", limbo, new Vector3(-7f, 2.5f, 0), new Vector3(0.1f, 5f, 14f), dark);
            var k = new MeshKit(); k.Ring(new Vector3(0, 0.01f, 0), 1.1f, 1.2f, 48, Quaternion.identity, Palette.Gold, 1.5f); k.Spawn("Ring", limbo, Mat);
            PointLight(limbo, new Vector3(0, 3.5f, 1.5f), Palette.Hex("FFE2B0"), 1.6f, 9f);
        }

        public void ShowSite(Site s, bool on) { if (s != null) s.root.gameObject.SetActive(on); }
    }
}
