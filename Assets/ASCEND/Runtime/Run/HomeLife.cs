using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Life at the caravan park (the MAFIA MODE spawn): neighbours who talk when you walk past (text bubbles),
    /// a stray dog that follows you, chickens, a cat on the picnic table, birds overhead, fire barrels and the campfire
    /// crackling, a BBQ with smoke, laundry in the wind, a radio playing, newspapers blowing about, a scratch-card kiosk
    /// at the gate and a lot of junk to look at. Everything is home-local and switched off with the home.</summary>
    public class HomeLife : MonoBehaviour
    {
        Transform home;
        static Material _mat;
        static Material Mat { get { if (_mat == null) _mat = Palette.Flat(new Color(0.6f, 0.58f, 0.6f), 0.02f, 0.03f, 0.25f); return _mat; } }

        static readonly Color Rust = Palette.Hex("A0582F"), Plank = Palette.Hex("9A7652"), PlankDark = Palette.Hex("6E5238"), Metal = Palette.Hex("6B6E78"),
            DarkMetal = Palette.Hex("3A3444"), Plastic = Palette.Hex("E8E2D4"), Brick = Palette.Hex("A65A43"), Leaf = Palette.Hex("4F8A4A");

        // ------------------------------------------------------------------ neighbours
        class Npc
        {
            public string name; public AvatarBody body; public string[] lines; public TextMeshPro bubble, tag; public Transform puffA, puffB; public Transform pivot, plate;
            public int idx; public float next, emoteAt; public Emote[] emotes; public bool asleep, thinks, lying, eyesDone, seated; public float range = 6f; public Vector3 lieAlong, lieFront;
        }
        readonly List<Npc> npcs = new List<Npc>();

        // ------------------------------------------------------------------ animals and moving bits
        class Dog { public Transform root, body, head, tail; public Transform[] legs; public int wp; public float wait, barkAt, speed; public bool following; }
        Dog dog;
        static readonly Vector2[] DogPath =
        {
            new Vector2(2.5f, 5.5f), new Vector2(-4.8f, 6.6f), new Vector2(-5.6f, 11.5f), new Vector2(1f, 15.5f), new Vector2(8f, 16f),
            new Vector2(9f, 5.5f), new Vector2(7f, -1f), new Vector2(2.6f, -5.7f), new Vector2(-2f, -8f), new Vector2(-6f, -8.5f),
            new Vector2(-3.9f, -2.5f), new Vector2(-3.9f, 1.2f), new Vector2(-4.6f, 4.2f), new Vector2(-2f, 4.3f)
        };
        class Hen { public Transform root, head; public Vector3 target; public float wait, peck, cluckAt; public Vector3 centre; }
        readonly List<Hen> hens = new List<Hen>();
        class Bird { public Transform root, wl, wr; public float radius, height, speed, phase; }
        readonly List<Bird> birds = new List<Bird>();
        readonly List<Transform> laundry = new List<Transform>();
        readonly List<Transform> papers = new List<Transform>();
        readonly List<Light> flicker = new List<Light>();
        readonly List<float> flickerBase = new List<float>();
        Transform cat; float catMeowAt;
        AudioSource catPurr;
        TextMeshPro kioskResult; float kioskBusyUntil;
        Transform kioskCard;

        static readonly Vector3 Wind = new Vector3(0.9f, 0f, 0.4f).normalized;

        public void Build(Transform root)
        {
            home = root;
            Props();
            Ambience();
            Neighbours();
            Animals();
            Kiosk();
        }

        static GameObject Spawn(MeshKit k, string name, Transform parent) { return k.Spawn(name, parent, Mat, false); }
        GameObject Col(string name, Vector3 local, Vector3 size, float yaw = 0f)
        {
            var g = global::Ascend.Build.Empty(name, home, local, Quaternion.Euler(0, yaw, 0));
            global::Ascend.Build.BoxCol(g, Vector3.zero, size);
            return g;
        }
        TextMeshPro Label(Transform parent, Vector3 local, float yaw, string text, float h, Color c, float width = 0f)
        {
            var t = Txt.Make("Label", parent, local, text, h, c, Palette.Display, TextAlignmentOptions.Center, width);
            t.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            return t;
        }

        // ================================================================== static set dressing (one mesh)
        void Props()
        {
            var k = new MeshKit();

            // entrance arch behind the "CAMPING EL ÚLTIMO RECURSO" sign
            foreach (var x in new[] { -3.3f, 3.3f })
            {
                k.Box(new Vector3(x, 1.9f, 21.0f), new Vector3(0.22f, 3.8f, 0.22f), PlankDark, 0f);
                k.Box(new Vector3(x, 0.15f, 21.0f), new Vector3(0.5f, 0.3f, 0.5f), Palette.Hex("7C776E"), 0f);
            }
            k.Box(new Vector3(0, 3.1f, 21.06f), new Vector3(6.9f, 0.9f, 0.08f), Palette.Hex("2E2836"), 0f);
            k.Box(new Vector3(0, 3.58f, 21.06f), new Vector3(7.0f, 0.06f, 0.1f), Palette.Gold, 0.6f);
            k.Box(new Vector3(0, 2.62f, 21.06f), new Vector3(7.0f, 0.06f, 0.1f), Palette.Gold, 0.6f);
            for (int i = 0; i < 12; i++) k.Sphere(new Vector3(-3.1f + i * 0.565f, 3.72f, 21.0f), 0.05f, 6, i % 2 == 0 ? Palette.Hex("FFE7A0") : Palette.Magenta, i == 7 ? 0.1f : 2f);   // one bulb is dead

            // mailboxes by the gate
            for (int i = 0; i < 4; i++)
            {
                float x = -6.4f + i * 0.75f;
                k.Box(new Vector3(x, 0.55f, 19.6f), new Vector3(0.06f, 1.1f, 0.06f), PlankDark, 0f);
                k.Box(new Vector3(x, 1.18f, 19.6f), new Vector3(0.32f, 0.22f, 0.42f), i == 2 ? Rust : new[] { Palette.Hex("3D6E9E"), Palette.Hex("C0392B"), Rust, Palette.Hex("4E8A52") }[i], 0f);
                if (i == 1) k.Box(new Vector3(x + 0.17f, 1.32f, 19.55f), new Vector3(0.02f, 0.16f, 0.04f), Palette.Red, 0.4f);   // flag up: a letter (another bill)
            }

            // car wreck on bricks, tyres, oil drums
            k.Push(new Vector3(-16f, 0, 15.5f), Quaternion.Euler(0, 32f, 0));
            k.Box(new Vector3(0, 0.75f, 0), new Vector3(3.8f, 0.55f, 1.7f), Palette.Hex("7A8B5A"), 0f);
            k.Box(new Vector3(-0.3f, 1.22f, 0), new Vector3(1.8f, 0.45f, 1.55f), Palette.Hex("6E7E50"), 0f);
            k.Box(new Vector3(-0.3f, 1.23f, 0), new Vector3(1.5f, 0.36f, 1.57f), Palette.Hex("2B3038"), 0.05f);
            k.Box(new Vector3(1.6f, 1.08f, 0.2f), new Vector3(0.9f, 0.08f, 1.5f), Quaternion.Euler(0, 0, -38f), Palette.Hex("7A8B5A"), 0f);   // bonnet open
            foreach (var x in new[] { -1.3f, 1.3f }) foreach (var z in new[] { -0.7f, 0.7f })
                {
                    k.Box(new Vector3(x, 0.12f, z), new Vector3(0.24f, 0.24f, 0.12f), Brick, 0f);
                    k.Box(new Vector3(x, 0.36f, z), new Vector3(0.24f, 0.24f, 0.12f), Brick * 0.92f, 0f);
                }
            k.Box(new Vector3(0, 0.5f, 0), new Vector3(3.6f, 0.06f, 1.6f), Palette.Hex("5B5348"), 0f);
            k.Pop();
            for (int i = 0; i < 4; i++) k.Cylinder(new Vector3(-13.4f, 0.12f + i * 0.24f, 15.2f), 0.38f, 0.38f, 0.22f, 12, Quaternion.identity, Palette.Hex("1B1A22"));
            k.Cylinder(new Vector3(-12.3f, 0.12f, 16.1f), 0.38f, 0.38f, 0.22f, 12, Quaternion.Euler(70, 20, 0), Palette.Hex("1B1A22"));
            foreach (var p in new[] { new Vector3(-17.5f, 0, 12.6f), new Vector3(-16.8f, 0, 12.1f) })
                k.Cylinder(p + Vector3.up * 0.45f, 0.3f, 0.3f, 0.9f, 12, Quaternion.identity, p.x < -17f ? Palette.Hex("2F5E8C") : Rust);

            // gas bottles and satellite dishes on the caravans
            AddCaravanBits(k, new Vector3(-11f, 0, 9f), 20f);
            AddCaravanBits(k, new Vector3(10f, 0, 13f), -35f);
            AddCaravanBits(k, new Vector3(-13f, 0, -7f), 95f);

            // pallets, shopping cart, broken washing machine, bins, sofa
            for (int i = 0; i < 4; i++)
            {
                float y = 0.07f + i * 0.15f;
                k.Push(new Vector3(16.6f, y, 3f), Quaternion.Euler(0, i * 7f, 0));
                for (int s = 0; s < 5; s++) k.Box(new Vector3(-0.48f + s * 0.24f, 0.05f, 0), new Vector3(0.14f, 0.02f, 1.2f), Plank, 0f);
                foreach (var z in new[] { -0.5f, 0f, 0.5f }) k.Box(new Vector3(0, -0.02f, z), new Vector3(1.2f, 0.1f, 0.1f), PlankDark, 0f);
                k.Pop();
            }
            k.Push(new Vector3(-4.3f, 0, 2.5f), Quaternion.Euler(0, 64f, 8f));   // shopping cart on its side-ish
            k.Box(new Vector3(0, 0.62f, 0), new Vector3(0.55f, 0.04f, 0.85f), Metal, 0f);
            foreach (var x in new[] { -0.27f, 0.27f }) k.Box(new Vector3(x, 0.8f, 0), new Vector3(0.02f, 0.36f, 0.85f), Metal, 0f);
            k.Box(new Vector3(0, 0.8f, 0.42f), new Vector3(0.55f, 0.36f, 0.02f), Metal, 0f);
            k.Box(new Vector3(0, 0.8f, -0.42f), new Vector3(0.55f, 0.36f, 0.02f), Metal, 0f);
            k.Bar(new Vector3(-0.27f, 1.05f, -0.5f), new Vector3(0.27f, 1.05f, -0.5f), 0.015f, Palette.Red, 0f);
            foreach (var x in new[] { -0.22f, 0.22f }) foreach (var z in new[] { -0.35f, 0.35f })
                {
                    k.Bar(new Vector3(x, 0.6f, z), new Vector3(x, 0.08f, z), 0.012f, Metal, 0f);
                    k.Cylinder(new Vector3(x, 0.06f, z), 0.06f, 0.06f, 0.03f, 8, Quaternion.Euler(0, 0, 90), Palette.Hex("1B1A22"));
                }
            k.Pop();
            k.Push(new Vector3(4.4f, 0, -1.2f), Quaternion.Euler(0, -80f, 0));   // washing machine with the door hanging
            k.Box(new Vector3(0, 0.43f, 0), new Vector3(0.6f, 0.85f, 0.6f), Plastic, 0f);
            k.Cylinder(new Vector3(0.0f, 0.45f, 0.31f), 0.2f, 0.2f, 0.02f, 18, Quaternion.Euler(90, 0, 0), Palette.Hex("39404C"), 0.05f);
            k.Cylinder(new Vector3(0.24f, 0.32f, 0.38f), 0.21f, 0.21f, 0.04f, 18, Quaternion.Euler(90, 50f, 0), Palette.Hex("8E949E"));
            k.Box(new Vector3(0.12f, 0.8f, 0.31f), new Vector3(0.3f, 0.06f, 0.01f), Palette.Hex("2B2733"), 0.2f);
            k.Pop();
            foreach (var p in new[] { new Vector3(-3.6f, 0, 5.2f), new Vector3(-2.95f, 0, 5.35f) })   // wheelie bins
            {
                k.Box(p + new Vector3(0, 0.5f, 0), new Vector3(0.58f, 1.0f, 0.68f), p.x < -3.3f ? Palette.Hex("2E6B3A") : Palette.Hex("2C4E8C"), 0f);
                k.Box(p + new Vector3(0, 1.03f, 0.04f), new Vector3(0.62f, 0.06f, 0.76f), Quaternion.Euler(p.x < -3.3f ? -18f : 0f, 0, 0), Palette.Hex("1F2A22"), 0f);
            }
            k.Box(new Vector3(-3.0f, 0.06f, 5.9f), new Vector3(0.5f, 0.12f, 0.35f), Palette.Hex("E7B66B"), 0f);   // pizza boxes that missed
            // old sofa outside (El Tato sleeps on it)
            k.Push(new Vector3(-5.6f, 0, 0.4f), Quaternion.Euler(0, 90f, 0));
            k.Box(new Vector3(0, 0.22f, 0), new Vector3(1.9f, 0.44f, 0.85f), Palette.Hex("6B4E7A"), 0f);
            k.Box(new Vector3(0, 0.62f, -0.34f), new Vector3(1.9f, 0.55f, 0.2f), Palette.Hex("5E4470"), 0f);
            foreach (var x in new[] { -0.92f, 0.92f }) k.Box(new Vector3(x, 0.4f, 0), new Vector3(0.18f, 0.4f, 0.85f), Palette.Hex("5E4470"), 0f);
            k.Box(new Vector3(0.3f, 0.47f, 0.05f), new Vector3(0.6f, 0.08f, 0.6f), Palette.Hex("8A6A9A"), 0f);
            k.Box(new Vector3(-0.45f, 0.48f, 0.05f), new Vector3(0.55f, 0.08f, 0.6f), Quaternion.Euler(0, 0, -6), Palette.Hex("7B5C8C"), 0f);
            k.Box(new Vector3(0.55f, 0.2f, 0.36f), new Vector3(0.25f, 0.06f, 0.12f), Palette.Hex("F2ECDD"), 0.1f);   // a sock, sticking out
            k.Pop();
            Col("SofaCol", new Vector3(-5.6f, 0.35f, 0.4f), new Vector3(0.85f, 0.7f, 1.9f));
            Col("BinsCol", new Vector3(-3.27f, 0.5f, 5.27f), new Vector3(1.3f, 1f, 0.75f));
            Col("PalletsCol", new Vector3(16.6f, 0.35f, 3f), new Vector3(1.3f, 0.7f, 1.3f));
            Col("WreckCol", new Vector3(-16f, 0.8f, 15.5f), new Vector3(3.9f, 1.6f, 1.8f), 32f);
            Col("WasherCol", new Vector3(4.4f, 0.43f, -1.2f), new Vector3(0.6f, 0.85f, 0.6f));

            // plastic flamingos, kiddie pool, garden gnome, potted plants by the door, puddles
            foreach (var p in new[] { new Vector3(7.6f, 0, 15.2f), new Vector3(8.2f, 0, 15.8f) })
            {
                k.Bar(p, p + Vector3.up * 0.55f, 0.008f, Palette.Hex("1B1A22"), 0f);
                k.Sphere(p + new Vector3(0, 0.62f, 0), new Vector3(0.09f, 0.11f, 0.17f), 8, Palette.Hex("FF5FA0"), 0.15f);
                k.Bar(p + new Vector3(0, 0.68f, 0.1f), p + new Vector3(0, 0.88f, 0.14f), 0.018f, Palette.Hex("FF5FA0"), 0.15f);
                k.Sphere(p + new Vector3(0, 0.9f, 0.18f), 0.04f, 8, Palette.Hex("FF5FA0"), 0.15f);
                k.Bar(p + new Vector3(0, 0.89f, 0.21f), p + new Vector3(0, 0.84f, 0.27f), 0.01f, Palette.Hex("1B1A22"), 0f);
            }
            k.Cylinder(new Vector3(14.5f, 0.15f, 13f), 1.0f, 1.0f, 0.3f, 24, Quaternion.identity, Palette.Hex("3FA9E0"), 0.05f, true, Palette.Hex("7FD3F5"), 0.25f);
            k.Torus(new Vector3(14.5f, 0.28f, 13f), 1.0f, 0.06f, 24, 6, Quaternion.identity, Palette.Hex("F5C542"), 0.1f);
            k.Sphere(new Vector3(14.2f, 0.4f, 12.8f), 0.12f, 10, Palette.Hex("FF5A3C"), 0.1f);   // beach ball
            Gnome(k, new Vector3(-5.2f, 0, 12.8f), 140f);
            Gnome(k, new Vector3(2.4f, 0, 3.55f), 180f);
            foreach (var x in new[] { 0.25f, 2.15f })
            {
                k.Cylinder(new Vector3(x, 0.15f, 3.42f), 0.14f, 0.18f, 0.3f, 10, Quaternion.identity, Palette.Hex("B5653E"));
                k.Sphere(new Vector3(x, 0.42f, 3.42f), new Vector3(0.22f, 0.2f, 0.22f), 8, Leaf, 0f);
                k.Sphere(new Vector3(x + 0.08f, 0.55f, 3.45f), 0.05f, 6, Palette.Hex("F25C78"), 0.3f);
            }
            foreach (var p in new[] { new Vector3(-1.5f, 0, 9.6f), new Vector3(3.5f, 0, 6.4f), new Vector3(-8.5f, 0, 5.2f), new Vector3(6.5f, 0, -3.4f) })
                k.Disc(p + Vector3.up * 0.032f, 0.5f + Mathf.Abs(p.x) * 0.04f, 14, Quaternion.identity, Palette.Hex("3E4A5C"), 0.05f);

            // notice board by the door
            k.Box(new Vector3(-2.4f, 0.8f, 3.3f), new Vector3(0.07f, 1.6f, 0.07f), PlankDark, 0f);
            k.Box(new Vector3(-1.2f, 0.8f, 3.3f), new Vector3(0.07f, 1.6f, 0.07f), PlankDark, 0f);
            k.Box(new Vector3(-1.8f, 1.35f, 3.32f), new Vector3(1.3f, 0.75f, 0.04f), Palette.Hex("B08A5E"), 0f);
            Color[] paper = { Palette.Hex("F7F2E6"), Palette.Hex("FFE58F"), Palette.Hex("BFE3FF"), Palette.Hex("FFD0D8") };
            Vector3[] pp = { new Vector3(-2.15f, 1.5f, 3.35f), new Vector3(-1.5f, 1.52f, 3.35f), new Vector3(-2.12f, 1.17f, 3.35f), new Vector3(-1.5f, 1.17f, 3.35f) };
            for (int i = 0; i < 4; i++) k.Box(pp[i], new Vector3(0.5f, 0.3f, 0.01f), Quaternion.Euler(0, 0, (i - 1.5f) * 3f), paper[i], 0.1f);

            // dog house, chicken coop, water tank, bike, laundry posts
            k.Push(new Vector3(2.6f, 0, -4.7f), Quaternion.Euler(0, 90f, 0));
            k.Box(new Vector3(0, 0.35f, 0), new Vector3(0.9f, 0.7f, 0.8f), Palette.Hex("B04A3A"), 0f);
            k.Box(new Vector3(0, 0.78f, 0.22f), new Vector3(1.0f, 0.06f, 0.55f), Quaternion.Euler(35, 0, 0), Palette.Hex("4A3E52"), 0f);
            k.Box(new Vector3(0, 0.78f, -0.22f), new Vector3(1.0f, 0.06f, 0.55f), Quaternion.Euler(-35, 0, 0), Palette.Hex("4A3E52"), 0f);
            k.Box(new Vector3(0.451f, 0.27f, 0), new Vector3(0.01f, 0.42f, 0.34f), Palette.Hex("1A1210"), 0f);
            k.Cylinder(new Vector3(0.75f, 0.03f, 0.2f), 0.12f, 0.1f, 0.06f, 12, Quaternion.identity, Palette.Red, 0.1f);   // bowl
            k.Pop();
            Col("DogHouseCol", new Vector3(2.6f, 0.4f, -4.7f), new Vector3(0.8f, 0.8f, 0.9f));
            k.Push(new Vector3(-15.8f, 0, 2.6f), Quaternion.Euler(0, 90f, 0));
            k.Box(new Vector3(0, 0.55f, 0), new Vector3(1.6f, 0.9f, 1.0f), Plank, 0f);
            k.Box(new Vector3(0, 1.08f, 0), new Vector3(1.8f, 0.08f, 1.2f), Quaternion.Euler(0, 0, 8), Palette.Hex("5B4A63"), 0f);
            foreach (var x in new[] { -0.7f, 0.7f }) foreach (var z in new[] { -0.4f, 0.4f }) k.Box(new Vector3(x, 0.05f, z), new Vector3(0.08f, 0.1f, 0.08f), PlankDark, 0f);
            k.Box(new Vector3(0, 0.45f, 0.505f), new Vector3(0.3f, 0.35f, 0.01f), Palette.Hex("1A1210"), 0f);
            k.Box(new Vector3(0, 0.25f, 0.75f), new Vector3(0.3f, 0.03f, 0.6f), Quaternion.Euler(30, 0, 0), PlankDark, 0f);
            k.Pop();
            Col("CoopCol", new Vector3(-15.8f, 0.55f, 2.6f), new Vector3(1.0f, 1.1f, 1.6f));
            k.Cylinder(new Vector3(-17.2f, 3.0f, -3f), 0.8f, 0.8f, 1.4f, 16, Quaternion.identity, Palette.Hex("2F3A48"));
            foreach (var x in new[] { -0.55f, 0.55f }) foreach (var z in new[] { -0.55f, 0.55f })
                    k.Bar(new Vector3(-17.2f + x, 0, -3f + z), new Vector3(-17.2f + x * 0.8f, 2.3f, -3f + z * 0.8f), 0.04f, Rust, 0f);
            Col("TankCol", new Vector3(-17.2f, 1.2f, -3f), new Vector3(1.3f, 2.4f, 1.3f));
            // bike leaning on caravan C
            k.Push(new Vector3(-11.6f, 0, -5.9f), Quaternion.Euler(0, 5f, 12f));
            foreach (var z in new[] { -0.5f, 0.5f }) k.Torus(new Vector3(0, 0.33f, z), 0.32f, 0.02f, 18, 4, Quaternion.Euler(0, 0, 90), Palette.Hex("1B1A22"), 0f);
            k.Bar(new Vector3(0, 0.33f, -0.5f), new Vector3(0, 0.7f, 0f), 0.02f, Palette.Hex("2A7FD0"), 0f);
            k.Bar(new Vector3(0, 0.7f, 0f), new Vector3(0, 0.33f, 0.5f), 0.02f, Palette.Hex("2A7FD0"), 0f);
            k.Bar(new Vector3(0, 0.33f, -0.5f), new Vector3(0, 0.33f, 0.05f), 0.02f, Palette.Hex("2A7FD0"), 0f);
            k.Bar(new Vector3(0, 0.33f, 0.05f), new Vector3(0, 0.75f, -0.15f), 0.02f, Palette.Hex("2A7FD0"), 0f);
            k.Box(new Vector3(0, 0.8f, -0.17f), new Vector3(0.1f, 0.04f, 0.22f), Palette.Hex("1B1A22"), 0f);
            k.Bar(new Vector3(-0.22f, 0.85f, 0.42f), new Vector3(0.22f, 0.85f, 0.42f), 0.015f, Metal, 0f);
            k.Pop();
            foreach (var x in new[] { -6.7f, -1.3f }) k.Bar(new Vector3(x, 0, -6f), new Vector3(x, 1.95f, -6f), 0.035f, Metal, 0f);
            k.Bar(new Vector3(-6.7f, 1.9f, -6f), new Vector3(-1.3f, 1.9f, -6f), 0.006f, Palette.Hex("D8D8D8"), 0f);

            // folding chairs around the campfire, a deckchair, the BBQ, the fire barrel, the radio
            CampChair(k, new Vector3(4.0f, 0, 11.6f), YawTo(new Vector3(4.0f, 0, 11.6f), new Vector3(5.5f, 0, 11f)), Palette.Hex("2E7DD1"));   // a free one
            CampChair(k, new Vector3(6.4f, 0, 12.5f), YawTo(new Vector3(6.4f, 0, 12.5f), new Vector3(5.5f, 0, 11f)), Palette.Hex("D1452E"));
            CampChair(k, new Vector3(6.9f, 0, 10.0f), YawTo(new Vector3(6.9f, 0, 10.0f), new Vector3(5.5f, 0, 11f)), Palette.Hex("3FAE5A"));   // a free one
            k.Push(new Vector3(-8.4f, 0, 12.0f), Quaternion.Euler(0, 160f, 0));   // deckchair
            k.Box(new Vector3(0, 0.3f, 0.1f), new Vector3(0.6f, 0.03f, 0.75f), Quaternion.Euler(-8, 0, 0), Palette.Hex("F2C14E"), 0f);
            k.Box(new Vector3(0, 0.62f, -0.35f), new Vector3(0.6f, 0.65f, 0.03f), Quaternion.Euler(-28, 0, 0), Palette.Hex("F2C14E"), 0f);
            foreach (var x in new[] { -0.3f, 0.3f }) { k.Bar(new Vector3(x, 0, 0.45f), new Vector3(x, 0.32f, 0.2f), 0.012f, PlankDark, 0f); k.Bar(new Vector3(x, 0, -0.45f), new Vector3(x, 0.95f, -0.5f), 0.012f, PlankDark, 0f); }
            k.Pop();
            k.Push(new Vector3(12.4f, 0, 9.6f), Quaternion.Euler(0, 24f, 0));   // BBQ
            k.Cylinder(new Vector3(0, 0.8f, 0), 0.4f, 0.32f, 0.3f, 16, Quaternion.identity, Palette.Hex("1E1C24"));
            k.Disc(new Vector3(0, 0.955f, 0), 0.38f, 16, Quaternion.identity, Palette.Hex("FF6A2A"), 1.2f);
            for (int i = 0; i < 6; i++) k.Bar(new Vector3(-0.34f, 0.97f, -0.25f + i * 0.1f), new Vector3(0.34f, 0.97f, -0.25f + i * 0.1f), 0.006f, Metal, 0f);
            for (int i = 0; i < 4; i++) k.Capsule(new Vector3(-0.2f + i * 0.13f, 0.99f, -0.12f), new Vector3(-0.17f + i * 0.13f, 0.99f, 0.14f), 0.025f, 8, Palette.Hex("A8402A"), 0.1f);   // chorizos
            foreach (var a in new[] { 0f, 120f, 240f }) { var d = Quaternion.Euler(0, a, 0) * Vector3.forward; k.Bar(d * 0.28f + Vector3.up * 0.66f, d * 0.4f, 0.015f, Palette.Hex("1E1C24"), 0f); }
            k.Box(new Vector3(0.62f, 0.45f, 0), new Vector3(0.5f, 0.9f, 0.45f), Palette.Hex("4E8AC4"), 0f);   // cool box
            k.Box(new Vector3(0.62f, 0.92f, 0), new Vector3(0.52f, 0.06f, 0.47f), Plastic, 0f);
            k.Pop();
            Col("BbqCol", new Vector3(12.6f, 0.5f, 9.7f), new Vector3(1.4f, 1f, 0.8f), 24f);
            k.Cylinder(new Vector3(-4.6f, 0.45f, 9.4f), 0.32f, 0.3f, 0.9f, 14, Quaternion.identity, Rust, 0f, true, Palette.Hex("2A1A12"));   // fire barrel
            for (int i = 0; i < 6; i++) { float a = i * 60f * Mathf.Deg2Rad; k.Box(new Vector3(-4.6f + Mathf.Cos(a) * 0.28f, 0.7f, 9.4f + Mathf.Sin(a) * 0.28f), new Vector3(0.05f, 0.08f, 0.05f), Palette.Hex("FFB040"), 1.5f); }
            Col("BarrelCol", new Vector3(-4.6f, 0.45f, 9.4f), new Vector3(0.65f, 0.9f, 0.65f));
            Spawn(k, "Junk", home);

            // texts: mailbox names, notices, graffiti, dog house, coop
            string[] mail = { "EL TATO", "PURI ♥ MARIANO", "SE BUSCA", "EL MANU" };
            for (int i = 0; i < 4; i++) Label(home, new Vector3(-6.4f + i * 0.75f, 1.18f, 19.38f), 180f, mail[i], 0.045f, Palette.Text, 0.5f);
            string[] notes = { "SE BUSCA GATO\n<size=70%>RESPONDE A «FICHAS»</size>", "CLASES DE PÓKER\n<size=70%>5 FICHAS · PREGUNTAR POR MANU</size>",
                               "VENDO CARAVANA\n<size=70%>CASI SIN DEUDAS</size>", "GRUPO DE APOYO\n<size=70%>LUNES 20:00 · NO TRAIGAS FICHAS</size>" };
            for (int i = 0; i < 4; i++) Label(home, pp[i] + new Vector3(0, 0, 0.012f), 180f, notes[i], 0.045f, Palette.Hex("2B2733"), 0.46f).transform.localRotation = Quaternion.Euler(0, 180f, -(i - 1.5f) * 3f);
            Label(home, new Vector3(-21.45f, 0.75f, 2f), -90f, "TODO AL ROJO", 0.3f, Palette.Red, 4f);
            Label(home, new Vector3(4f, 0.75f, -21.45f), 180f, "LA CASA SIEMPRE GANA", 0.3f, Palette.Magenta, 5f);
            Label(home, new Vector3(3.06f, 0.82f, -4.7f), 90f, "CHUSCO", 0.07f, Palette.Text, 0.6f);
            Label(home, new Vector3(16.6f, 0.75f, 2.36f), 180f, "SE REGALA\n<size=70%>(NO PREGUNTES DE DÓNDE)</size>", 0.08f, Palette.Text, 1.2f);
        }

        static float YawTo(Vector3 from, Vector3 to) { var d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

        static void Gnome(MeshKit k, Vector3 p, float yaw)
        {
            k.Push(p, Quaternion.Euler(0, yaw, 0));
            k.Cylinder(new Vector3(0, 0.12f, 0), 0.1f, 0.08f, 0.24f, 10, Quaternion.identity, Palette.Hex("2F5EA8"));
            k.Sphere(new Vector3(0, 0.3f, 0), 0.075f, 10, Palette.Hex("F4C7A8"), 0f);
            k.Sphere(new Vector3(0, 0.26f, 0.05f), new Vector3(0.07f, 0.06f, 0.04f), 8, Palette.Hex("F2F2F2"), 0f);
            k.Cylinder(new Vector3(0, 0.42f, 0), 0.075f, 0.0f, 0.2f, 10, Quaternion.identity, Palette.Hex("D7263D"));
            k.Pop();
        }

        static void CampChair(MeshKit k, Vector3 p, float yaw, Color c, float half = 0.25f)
        {
            k.Push(p, Quaternion.Euler(0, yaw, 0));
            k.Box(new Vector3(0, 0.42f, 0.02f), new Vector3(half * 2f, 0.04f, 0.45f), c, 0f);
            k.Box(new Vector3(0, 0.72f, -0.22f), new Vector3(half * 2f, 0.55f, 0.03f), Quaternion.Euler(-10, 0, 0), c, 0f);
            foreach (var x in new[] { -half, half })
            {
                k.Bar(new Vector3(x, 0, 0.22f), new Vector3(x, 0.42f, -0.2f), 0.012f, DarkMetal, 0f);
                k.Bar(new Vector3(x, 0, -0.22f), new Vector3(x, 0.42f, 0.22f), 0.012f, DarkMetal, 0f);
                k.Bar(new Vector3(x, 0.6f, -0.1f), new Vector3(x, 0.6f, 0.2f), 0.015f, DarkMetal, 0f);
            }
            k.Cylinder(new Vector3(half + 0.02f, 0.66f, 0.15f), 0.035f, 0.035f, 0.1f, 8, Quaternion.identity, Palette.Hex("C0392B"), 0.1f);   // can in the cup holder
            k.Pop();
        }

        static void AddCaravanBits(MeshKit k, Vector3 c, float yaw)
        {
            var r = Quaternion.Euler(0, yaw, 0);
            // satellite dish on the roof
            var d = c + r * new Vector3(1.4f, 2.36f, 0.4f);
            k.Bar(d, d + Vector3.up * 0.35f, 0.02f, Metal, 0f);
            k.Cylinder(d + Vector3.up * 0.45f, 0.26f, 0.05f, 0.08f, 16, r * Quaternion.Euler(70, 0, 0), Plastic);
            // gas bottle and a step
            var g = c + r * new Vector3(-1.9f, 0f, 1.4f);
            k.Cylinder(g + Vector3.up * 0.32f, 0.16f, 0.16f, 0.64f, 12, Quaternion.identity, Palette.Hex("F28A1E"));
            k.Sphere(g + Vector3.up * 0.64f, 0.16f, 10, Palette.Hex("F28A1E"), 0f);
            k.Box(c + r * new Vector3(-1.4f, 0.1f, 1.35f), new Vector3(0.6f, 0.2f, 0.4f), r, PlankDark, 0f);
        }

        // ================================================================== moving and glowing things
        void Ambience()
        {
            // campfire and fire barrel: flames, embers, light flicker, crackle
            Fire(new Vector3(5.5f, 0.3f, 11f), 0.35f, 2.6f);
            Fire(new Vector3(-4.6f, 0.95f, 9.4f), 0.25f, 2.0f);
            // BBQ smoke + sizzle
            Smoke(new Vector3(12.4f, 1.05f, 9.6f));
            var bbq = global::Ascend.Build.Empty("BbqSound", home, new Vector3(12.4f, 1f, 9.6f)).transform;
            Sfx.Loop("sizzle_loop", bbq, 0.35f, 1f, 9f);
            // lamp post flickers
            var lamp = new GameObject("LampFlicker").AddComponent<Light>();
            lamp.transform.SetParent(home, false); lamp.transform.localPosition = new Vector3(-6f, 3.9f, 3.6f);
            lamp.type = LightType.Point; lamp.range = 7f; lamp.intensity = 1.2f; lamp.color = Palette.Hex("FFE7A0"); lamp.shadows = LightShadows.None;
            flicker.Add(lamp); flickerBase.Add(-1.2f);   // negative = broken-bulb style flicker
            // radio on caravan A's step
            var radio = global::Ascend.Build.Empty("Radio", home, new Vector3(-12.3f, 0.22f, 10.6f), Quaternion.Euler(0, 200f, 0)).transform;
            var rk = new MeshKit();
            rk.Box(new Vector3(0, 0.1f, 0), new Vector3(0.36f, 0.2f, 0.12f), Palette.Hex("8C2F39"), 0f);
            rk.Cylinder(new Vector3(-0.08f, 0.1f, 0.061f), 0.055f, 0.055f, 0.01f, 12, Quaternion.Euler(90, 0, 0), Palette.Hex("2B2733"));
            rk.Box(new Vector3(0.1f, 0.13f, 0.061f), new Vector3(0.12f, 0.05f, 0.01f), Palette.Hex("FFC857"), 0.8f);
            rk.Bar(new Vector3(0.14f, 0.2f, 0), new Vector3(0.24f, 0.5f, -0.05f), 0.004f, Metal, 0f);
            Spawn(rk, "RadioMesh", radio);
            Sfx.Loop("lounge_loop", radio, 0.22f, 1f, 11f);
            // birds singing in two trees, wind everywhere (quiet)
            Sfx.Loop("birds_loop", global::Ascend.Build.Empty("BirdsA", home, new Vector3(-14f, 4f, -12f)).transform, 0.35f, 1f, 22f);
            Sfx.Loop("birds_loop", global::Ascend.Build.Empty("BirdsB", home, new Vector3(15f, 4f, 16f)).transform, 0.3f, 1f, 22f);
            Sfx.Loop("city_wind_loop", home, 0.08f, 0f, 50f);

            // laundry swinging in the wind
            Color[] cl = { Palette.Hex("F2F2F2"), Palette.Hex("3D7DD8"), Palette.Hex("F28AA8"), Palette.Hex("F5D04A"), Palette.Hex("6BBF6B"), Palette.Hex("F2F2F2") };
            for (int i = 0; i < 6; i++)
            {
                var t = global::Ascend.Build.Empty("Laundry" + i, home, new Vector3(-6.2f + i * 0.92f, 1.9f, -6f)).transform;
                var k = new MeshKit();
                bool sheet = i == 0 || i == 5;
                var size = sheet ? new Vector3(0.8f, 1.0f, 0.015f) : i == 2 ? new Vector3(0.5f, 0.6f, 0.015f) : new Vector3(0.45f, 0.5f, 0.015f);
                k.Box(new Vector3(0, -size.y * 0.5f, 0), size, cl[i], 0.05f);
                if (i == 3) k.Box(new Vector3(0, -0.62f, 0), new Vector3(0.12f, 0.25f, 0.015f), cl[i], 0.05f);   // a long sock
                k.Box(new Vector3(-size.x * 0.4f, 0.01f, 0), new Vector3(0.02f, 0.05f, 0.03f), Palette.Hex("E85A9B"), 0.2f);
                k.Box(new Vector3(size.x * 0.4f, 0.01f, 0), new Vector3(0.02f, 0.05f, 0.03f), Palette.Hex("4AB3E8"), 0.2f);
                Spawn(k, "Cloth", t);
                laundry.Add(t);
            }

            // newspapers blowing across the park
            for (int i = 0; i < 3; i++)
            {
                var t = global::Ascend.Build.Empty("Newspaper" + i, home, new Vector3(-15f + i * 9f, 0.05f, -10f + i * 9f)).transform;
                var k = new MeshKit();
                k.Box(Vector3.zero, new Vector3(0.36f, 0.004f, 0.26f), Palette.Hex("E6E1D3"), 0f);
                k.Box(new Vector3(0, 0.003f, 0.06f), new Vector3(0.3f, 0.002f, 0.05f), Palette.Hex("2B2733"), 0f);
                Spawn(k, "Paper", t);
                papers.Add(t);
            }

            // birds circling
            for (int i = 0; i < 6; i++)
            {
                var b = new Bird { radius = 10f + i * 2.4f, height = 11f + (i % 3) * 2.5f, speed = (0.25f + 0.05f * (i % 3)) * (i % 2 == 0 ? 1f : -1f), phase = i * 1.1f };
                b.root = global::Ascend.Build.Empty("Bird" + i, home, Vector3.zero).transform;
                var bk = new MeshKit();
                bk.Sphere(Vector3.zero, new Vector3(0.06f, 0.05f, 0.14f), 8, Palette.Hex("2B2733"), 0f);
                Spawn(bk, "Body", b.root);
                foreach (var s in new[] { -1f, 1f })
                {
                    var w = global::Ascend.Build.Empty(s < 0 ? "WL" : "WR", b.root, Vector3.zero).transform;
                    var wk = new MeshKit(); wk.Box(new Vector3(s * 0.16f, 0, 0), new Vector3(0.3f, 0.01f, 0.1f), Palette.Hex("3A3444"), 0f);
                    Spawn(wk, "Wing", w);
                    if (s < 0) b.wl = w; else b.wr = w;
                }
                birds.Add(b);
            }
        }

        void Fire(Vector3 local, float radius, float lightRange)
        {
            var t = global::Ascend.Build.Empty("Fire", home, local).transform;
            var ps = Particles(t, "Flames", Palette.Glow, 0.35f, 0.9f, -0.15f, 0.6f, 60, new Color(1f, 0.55f, 0.15f, 0.9f), new Color(1f, 0.85f, 0.3f, 0.8f), radius, 30f);
            var em = Particles(t, "Embers", Palette.Glow, 0.05f, 1.6f, -0.05f, 1.8f, 12, new Color(1f, 0.6f, 0.2f, 1f), new Color(1f, 0.9f, 0.5f, 1f), radius * 0.8f, 6f);
            var l = new GameObject("FireLight").AddComponent<Light>();
            l.transform.SetParent(t, false); l.transform.localPosition = Vector3.up * 0.5f;
            l.type = LightType.Point; l.range = lightRange * 2f; l.intensity = 1.6f; l.color = Palette.Hex("FF9A4A"); l.shadows = LightShadows.None;
            flicker.Add(l); flickerBase.Add(1.6f);
            Sfx.Loop("fire_loop", t, 0.4f, 1f, 9f);
        }

        void Smoke(Vector3 local)
        {
            var t = global::Ascend.Build.Empty("Smoke", home, local).transform;
            Particles(t, "Smoke", Palette.Glow, 0.5f, 0.5f, -0.06f, 3.2f, 8, new Color(0.45f, 0.45f, 0.5f, 0.18f), new Color(0.6f, 0.6f, 0.65f, 0.12f), 0.2f, 12f, 2.2f);
        }

        static ParticleSystem Particles(Transform parent, string name, Material mat, float size, float speed, float gravity, float life, float rate,
                                        Color a, Color b, float radius, float angle, float grow = 0.3f)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = (int)(rate * life * 1.5f) + 4;
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            var emi = ps.emission; emi.enabled = true; emi.rateOverTime = rate;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = angle; shape.radius = radius;
            shape.rotation = new Vector3(-90f, 0, 0);
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, grow < 1f ? 1f : 0.5f), new Keyframe(1f, grow)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // ================================================================== neighbours
        /// <summary>Which of the pack's people a neighbour is built from (each then gets its own colours and build).</summary>
        enum Look { Gentleman, HighRoller, Showgirl, Croupier, Suit }

        Npc AddNpc(string name, Vector3 local, float yaw, Look look, int seed, float tint, float height, string[] idles, string[] lines, Emote[] emotes)
        {
            var lib = AssetLibrary.I; GameObject prefab = null;
            if (lib != null && lib.HasHumanoids)
                switch (look)
                {
                    case Look.Gentleman: prefab = lib.android; break;
                    case Look.HighRoller: prefab = lib.highRoller; break;
                    case Look.Showgirl: prefab = lib.showgirl; break;
                    case Look.Croupier: prefab = lib.guestF; break;
                    case Look.Suit: prefab = lib.security; break;
                }
            AvatarBody body = prefab != null ? HumanoidBody.CreateFrom(prefab, lib.humanoidController, lib, name, home, local, yaw, Rarity.Common, RoninRole.Player, height, HumanoidBody.Kind.Extra) : null;
            if (body == null) body = Avatars.Create(name, home, local, yaw, Rarity.Common, RoninRole.Player, height);
            if (body == null) return null;
            var hb = body as HumanoidBody;
            if (hb != null) { hb.Vary(seed, tint); if (idles != null) hb.idleSet = idles; }
            body.pose = Pose.Idle;
            var n = new Npc { name = name, body = body, lines = lines, emotes = emotes, next = Random.value * 3f, emoteAt = Time.time + Random.Range(2f, 9f) };
            // speech bubble: bold white on a dark plate, so it reads against the sky from any distance
            var pivot = global::Ascend.Build.Empty("Bubble · " + name, home, local + Vector3.up * 2.2f).transform;
            pivot.gameObject.AddComponent<Billboard>();
            n.bubble = Txt.Make("Text", pivot, Vector3.zero, "", 0.105f, Color.white, Palette.Body, TextAlignmentOptions.Center, 1.9f);
            n.bubble.enableWordWrapping = true;
            n.bubble.fontStyle = FontStyles.Bold;
            n.bubble.outlineWidth = 0.2f; n.bubble.outlineColor = new Color32(14, 10, 22, 255);
            var pk = new MeshKit(); pk.Quad(Vector3.zero, Vector2.one, Quaternion.identity, Color.white, 1f);
            n.plate = pk.Spawn("Plate", pivot, new Vector3(0, 0, 0.012f), Quaternion.identity, PlateMat, false).transform;
            n.pivot = pivot;
            n.tag = Txt.Make("Name", home, local + Vector3.up * 2.05f, name, 0.07f, Palette.Gold, Palette.Display);
            n.tag.gameObject.AddComponent<Billboard>();
            n.tag.gameObject.SetActive(false);
            pivot.gameObject.SetActive(false);
            npcs.Add(n);
            return n;
        }

        static Material plateMat;
        // speech: a dark see-through plate. Thoughts and dreams: a paler, more transparent white cloud.
        static Material PlateMat { get { if (plateMat == null) plateMat = Palette.Translucent(new Color(0.07f, 0.05f, 0.12f, 0.6f)); return plateMat; } }
        static Material cloudMat;
        static Material CloudMat { get { if (cloudMat == null) cloudMat = Palette.Translucent(new Color(1f, 1f, 1f, 0.42f)); return cloudMat; } }
        static Mesh cloudMesh, puffMesh;
        /// <summary>Flat fan in XY: a unit-sized cloud (scalloped edge) or a plain round puff.</summary>
        static Mesh Fan(bool cloud)
        {
            const int N = 96; var v = new Vector3[N + 1]; var c = new Color[N + 1]; var t = new int[N * 3];
            v[0] = Vector3.zero; c[0] = Color.white;
            for (int i = 0; i < N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f;
                float r = cloud ? 0.5f * (0.84f + 0.16f * Mathf.Abs(Mathf.Sin(a * 5.5f))) : 0.5f;
                v[i + 1] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0); c[i + 1] = Color.white;
                t[i * 3] = 0; t[i * 3 + 1] = 1 + (i + 1) % N; t[i * 3 + 2] = 1 + i;
            }
            var m = new Mesh { name = cloud ? "ThoughtCloud" : "ThoughtPuff" }; m.vertices = v; m.colors = c; m.uv = new Vector2[N + 1]; m.triangles = t; m.RecalculateBounds(); m.RecalculateNormals();
            return m;
        }
        static Transform FanObj(string name, Transform parent, Mesh m)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = m; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = CloudMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return g.transform;
        }
        /// <summary>This neighbour thinks (or dreams) instead of talking: the plate becomes a white cloud with a trail of puffs.</summary>
        void Thinks(Npc n)
        {
            if (n == null || n.thinks) return;
            n.thinks = true;
            if (cloudMesh == null) { cloudMesh = Fan(true); puffMesh = Fan(false); }
            Destroy(n.plate.gameObject);
            n.plate = FanObj("Cloud", n.pivot, cloudMesh);
            n.puffA = FanObj("PuffA", n.pivot, puffMesh); n.puffB = FanObj("PuffB", n.pivot, puffMesh);
            n.bubble.color = new Color(0.14f, 0.11f, 0.22f, 1f); n.bubble.outlineWidth = 0f; n.bubble.fontStyle = FontStyles.Bold | FontStyles.Italic;
        }

        /// <summary>Lays a neighbour flat on their back along a piece of furniture (feet at <paramref name="feet"/>, head
        /// towards <paramref name="along"/>), one hand on the belly and the other hanging off the open side.</summary>
        void LieDown(Npc n, Vector3 feet, Vector3 along, Vector3 front)
        {
            if (n == null) return;
            n.lying = true; n.lieAlong = along.normalized; n.lieFront = front.normalized;
            var up = (n.lieAlong + Vector3.up * 0.1f).normalized;
            n.body.transform.localPosition = feet;
            n.body.transform.localRotation = Quaternion.LookRotation(Vector3.Cross(Vector3.Cross(up, Vector3.up), up).normalized, up);
            n.body.pose = Pose.Idle;
            var hb = n.body as HumanoidBody; if (hb != null) { hb.idleSet = new[] { "Idle" }; hb.restHands = false; }
        }

        /// <summary>Sits a neighbour on a piece of furniture: cushion height, where the backrest and the front edge are
        /// (seat space), and which of the pack's seated poses it holds. The clip keeps its own arms.</summary>
        void Seated(Npc n, Vector3 local, float yaw, float height, float back, float front, string state)
        {
            if (n == null) return;
            var g = global::Ascend.Build.Empty(n.name + " · Seat", home, local, Quaternion.Euler(0, yaw, 0));
            var s = g.AddComponent<Seat>();
            s.height = height; s.tableEdge = false; s.back = back; s.front = front; s.footY = 0f; s.footZ = 0.25f;
            var hb = n.body as HumanoidBody;
            if (hb != null) { hb.sitState = state; hb.restHands = true; }
            // seated neighbours keep still: hands on the knees, no gestures and no twisting round to stare
            n.seated = true; n.emotes = new Emote[0];
            s.Take(n.body);
        }

        void Neighbours()
        {
            var standing = new[] { "IdleHips", "Idle", "IdleArms", "IdleLook" };
            AddNpc("MARIANO", new Vector3(12.0f, 0, 8.7f), 24f, Look.HighRoller, 3, 0.75f, 1.74f, new[] { "Idle", "IdleHips", "IdleLook", "IdleThink" }, new[]
            {
                "¡Chorizo a la brasa! Cinco fichas. Bueno, diez.",
                "Yo antes era crupier. Ahora solo le doy la vuelta a los chorizos.",
                "Si ves a Don Tuchi, yo no estoy. Soy un chorizo.",
                "El secreto de la barbacoa es el mismo que el del casino: alguien acaba quemado."
            }, new[] { Emote.Wave, Emote.Nod, Emote.Talk });
            var chairA = new Vector3(4.0f, 0, 11.6f); var chairB = new Vector3(6.4f, 0, 12.5f); var fire = new Vector3(5.5f, 0, 11f);
            var manu = AddNpc("EL MANU", chairB, 0f, Look.Gentleman, 29, 0.8f, 1.8f, null, new[]
            {
                "Tengo un sistema infalible. Ha fallado 37 veces, pero la 38 es la buena.",
                "La martingala funciona. Lo que no funciona es mi cartera.",
                "Dicen que la casa siempre gana. Yo vivo en una caravana, así que...",
                "¿Clases de póker? Cinco fichas. Lo de ganar va aparte."
            }, new[] { Emote.Talk, Emote.Nod });
            Seated(manu, chairB, YawTo(chairB, fire), 0.44f, -0.2f, 0.245f, "SitM3");
            var anselmo = AddNpc("DON ANSELMO", new Vector3(-8.4f, 0, 12.0f), 160f, Look.Suit, 47, 0.6f, 1.68f, null, new[]
            {
                "En mis tiempos las tragaperras daban premio. Mentira, tampoco.",
                "Llevo cuarenta años en este camping. Vine a pasar un fin de semana.",
                "Chaval, el único que gana en el casino es el que no entra.",
                "¿Ves ese coche negro? Antes era mío."
            }, new[] { Emote.Nod });
            Seated(anselmo, new Vector3(-8.4f, 0, 12.0f), 160f, 0.34f, -0.26f, 0.42f, "SitRelax"); Thinks(anselmo);
            AddNpc("PURI", new Vector3(-4.0f, 0, -5.3f), 180f, Look.Croupier, 5, 0.85f, 1.62f, standing, new[]
            {
                "Tiendo la ropa y la deuda, a ver cuál se seca antes.",
                "Mi hijo dijo que las criptos eran el futuro. Ahora vive en la caravana de al lado.",
                "¡Cuidado con la sábana, que está recién lavada!",
                "Mariano me prometió un crucero. Hemos llegado hasta aquí."
            }, new[] { Emote.Talk, Emote.Wave, Emote.Facepalm });
            var tato = AddNpc("EL TATO", new Vector3(-5.6f, 0, 0.4f), 90f, Look.HighRoller, 71, 0.35f, 1.76f, null, new[]
            {
                "Zzz... al rojo... todo al rojo... zzz...",
                "Zzz... doble o nada... zzz...",
                "Zzz... mamá, he ganado... zzz... no, espera...",
            }, new Emote[0]);
            if (tato != null)
            {
                // flat out along the whole sofa, head on the far armrest, dreaming of roulette
                tato.asleep = true; tato.range = 4.5f; Thinks(tato);
                var thb = tato.body as HumanoidBody; if (thb != null) thb.girth = 0.24f;
                LieDown(tato, new Vector3(-5.57f, 0.66f, -0.42f), Vector3.forward, Vector3.right);
                Sfx.Loop("snore_loop", tato.body.transform, 0.35f, 1f, 6f);
            }
            AddNpc("LA LOTERA", new Vector3(5.5f, 0, 19.25f), 180f, Look.Showgirl, 88, 0.9f, 1.66f, new[] { "Idle", "IdleLook", "IdlePhone" }, new[]
            {
                "¡Rasca y gana! Bueno... rasca.",
                "¡Que hoy toca! (Hoy no toca.)",
                "Cincuenta fichas, y la ilusión te la regalo.",
                "El último que ganó se compró ese coche de ahí. El de los ladrillos."
            }, new[] { Emote.Wave, Emote.Talk, Emote.Clap });
        }

        // ================================================================== animals
        void Animals()
        {
            // the dog
            dog = new Dog { speed = 1.2f, wp = 0 };
            dog.root = global::Ascend.Build.Empty("Chusco · Dog", home, new Vector3(DogPath[0].x, 0, DogPath[0].y)).transform;
            Color fur = Palette.Hex("B9864F"), dark = Palette.Hex("5A3D24");
            dog.body = global::Ascend.Build.Empty("Body", dog.root, new Vector3(0, 0.42f, 0)).transform;
            var bk = new MeshKit();
            bk.Capsule(new Vector3(0, 0, -0.22f), new Vector3(0, 0.02f, 0.2f), 0.14f, 10, fur, 0f);
            bk.Box(new Vector3(0, 0.1f, 0.16f), new Vector3(0.05f, 0.03f, 0.08f), Palette.Red, 0.3f);   // collar tag
            bk.Torus(new Vector3(0, 0.03f, 0.2f), 0.12f, 0.015f, 14, 4, Quaternion.Euler(90, 0, 0), Palette.Red, 0.2f);
            Spawn(bk, "Torso", dog.body);
            dog.head = global::Ascend.Build.Empty("Head", dog.body, new Vector3(0, 0.12f, 0.34f)).transform;
            var hk = new MeshKit();
            hk.Sphere(Vector3.zero, new Vector3(0.11f, 0.11f, 0.12f), 10, fur, 0f);
            hk.Box(new Vector3(0, -0.03f, 0.13f), new Vector3(0.09f, 0.08f, 0.12f), fur * 1.08f, 0f);
            hk.Sphere(new Vector3(0, 0f, 0.2f), 0.025f, 8, Palette.Hex("1A1210"), 0f);
            foreach (var s in new[] { -1f, 1f })
            {
                hk.Box(new Vector3(s * 0.09f, 0.06f, -0.01f), new Vector3(0.03f, 0.13f, 0.07f), Quaternion.Euler(0, 0, s * 20f), dark, 0f);
                hk.Sphere(new Vector3(s * 0.045f, 0.04f, 0.1f), 0.015f, 6, Palette.Hex("1A1210"), 0f);
            }
            Spawn(hk, "HeadMesh", dog.head);
            dog.tail = global::Ascend.Build.Empty("Tail", dog.body, new Vector3(0, 0.08f, -0.34f)).transform;
            var tk = new MeshKit(); tk.Capsule(Vector3.zero, new Vector3(0, 0.12f, -0.12f), 0.025f, 6, dark, 0f); Spawn(tk, "TailMesh", dog.tail);
            dog.legs = new Transform[4];
            int li = 0;
            foreach (var z in new[] { 0.18f, -0.2f }) foreach (var x in new[] { -0.08f, 0.08f })
                {
                    var leg = global::Ascend.Build.Empty("Leg", dog.root, new Vector3(x, 0.36f, z)).transform;
                    var lk = new MeshKit(); lk.Capsule(Vector3.zero, new Vector3(0, -0.33f, 0), 0.03f, 6, z > 0 ? fur : dark, 0f); Spawn(lk, "LegMesh", leg);
                    dog.legs[li++] = leg;
                }
            dog.barkAt = Time.time + 5f;

            // chickens around the coop
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector3(-14.2f, 0, 3.2f);
                var h = new Hen { centre = c, wait = Random.value * 2f, cluckAt = Time.time + Random.Range(3f, 12f) };
                h.root = global::Ascend.Build.Empty("Hen" + i, home, c + new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f))).transform;
                Color feathers = i == 3 ? Palette.Hex("1E1C24") : i % 2 == 0 ? Palette.Hex("F2EEE4") : Palette.Hex("B5703A");
                var ck = new MeshKit();
                ck.Sphere(new Vector3(0, 0.17f, 0), new Vector3(0.1f, 0.1f, 0.14f), 10, feathers, 0f);
                ck.Box(new Vector3(0, 0.25f, -0.12f), new Vector3(0.06f, 0.1f, 0.06f), Quaternion.Euler(-30, 0, 0), feathers, 0f);
                foreach (var x in new[] { -0.03f, 0.03f }) ck.Bar(new Vector3(x, 0.08f, 0), new Vector3(x, 0f, 0.01f), 0.007f, Palette.Hex("F2A93B"), 0f);
                Spawn(ck, "Body", h.root);
                h.head = global::Ascend.Build.Empty("Head", h.root, new Vector3(0, 0.27f, 0.11f)).transform;
                var hk2 = new MeshKit();
                hk2.Sphere(Vector3.zero, 0.045f, 8, feathers, 0f);
                hk2.Box(new Vector3(0, 0.05f, 0), new Vector3(0.012f, 0.04f, 0.04f), Palette.Red, 0.3f);
                hk2.Cylinder(new Vector3(0, -0.005f, 0.05f), 0.012f, 0f, 0.03f, 6, Quaternion.Euler(90, 0, 0), Palette.Hex("F2A93B"), 0f);
                Spawn(hk2, "HeadMesh", h.head);
                h.target = h.root.localPosition;
                hens.Add(h);
            }

            // the cat asleep on the picnic table
            cat = global::Ascend.Build.Empty("Cat", home, new Vector3(6.2f, 0.79f, -6.1f), Quaternion.Euler(0, 200f, 0)).transform;
            var catk = new MeshKit();
            Color cfur = Palette.Hex("F09A3E");
            catk.Sphere(new Vector3(0, 0.09f, 0), new Vector3(0.14f, 0.09f, 0.2f), 10, cfur, 0f);
            catk.Sphere(new Vector3(0.02f, 0.11f, 0.17f), 0.075f, 10, cfur, 0f);
            foreach (var s in new[] { -1f, 1f }) catk.Cylinder(new Vector3(0.02f + s * 0.04f, 0.18f, 0.17f), 0.025f, 0f, 0.05f, 4, Quaternion.identity, cfur, 0f);
            catk.Torus(new Vector3(0, 0.03f, 0), 0.16f, 0.025f, 16, 4, Quaternion.identity, cfur * 0.9f, 0f, 200f);   // tail wrapped round
            Spawn(catk, "CatMesh", cat);
            catPurr = Sfx.Loop("cat_purr_loop", cat, 0.3f, 1f, 3f);
        }

        // ================================================================== scratch-card kiosk at the gate
        void Kiosk()
        {
            var k = global::Ascend.Build.Empty("Kiosk · Rasca y Gana", home, new Vector3(5.5f, 0, 18.5f), Quaternion.Euler(0, 180f, 0)).transform;
            var m = new MeshKit();
            m.Box(new Vector3(0, 0.5f, 0), new Vector3(1.8f, 1.0f, 0.6f), Palette.Hex("F2C14E"), 0f);
            m.Box(new Vector3(0, 1.02f, 0), new Vector3(1.9f, 0.05f, 0.7f), Palette.Hex("D9342B"), 0.1f);
            foreach (var x in new[] { -0.9f, 0.9f }) m.Box(new Vector3(x, 1.5f, -0.5f), new Vector3(0.06f, 1.0f, 0.06f), Metal, 0f);
            m.Box(new Vector3(0, 2.05f, -0.4f), new Vector3(2.1f, 0.08f, 1.5f), Quaternion.Euler(-8, 0, 0), Palette.Hex("D9342B"), 0f);
            for (int i = 0; i < 7; i++) m.Box(new Vector3(-0.9f + i * 0.3f, 1.97f, 0.36f), new Vector3(0.15f, 0.12f, 0.02f), i % 2 == 0 ? Palette.Hex("F2F2F2") : Palette.Hex("D9342B"), 0f);
            for (int i = 0; i < 8; i++) m.Box(new Vector3(-0.75f + i * 0.2f, 1.68f, 0.3f), new Vector3(0.14f, 0.2f, 0.005f), Quaternion.Euler(0, 0, (i % 3 - 1) * 4f), i % 2 == 0 ? Palette.Hex("63C7F2") : Palette.Hex("F28AC0"), 0.3f);   // tickets on the string
            m.Bar(new Vector3(-0.9f, 1.8f, 0.3f), new Vector3(0.9f, 1.8f, 0.3f), 0.004f, Palette.Hex("1B1A22"), 0f);
            m.Spawn("KioskMesh", k, Mat, false);
            global::Ascend.Build.BoxCol(k.gameObject, new Vector3(0, 0.5f, 0), new Vector3(1.8f, 1.0f, 0.6f));
            // the seller stands behind (+Z of the kiosk = camp side is -Z of the kiosk after the 180°)
            var sign = Txt.Glow("KioskSign", k, new Vector3(0, 2.35f, 0.3f), "RASCA Y GANA", 0.2f, Palette.Gold, 2.2f);
            sign.transform.localRotation = Quaternion.Euler(0, 180f, 0);
            var price = Txt.Make("Price", k, new Vector3(0, 0.6f, 0.31f), "50 FICHAS\n<size=60%>PREMIOS DE HASTA 2.000</size>", 0.1f, Palette.Hex("2B2733"), Palette.Display, TextAlignmentOptions.Center, 1.6f);
            price.transform.localRotation = Quaternion.Euler(0, 180f, 0);
            PokeButton.Create("Scratch", k, new Vector3(0.55f, 1.06f, 0.2f), Quaternion.identity, 0.06f, Palette.Gold, "RASCAR", Scratch, 0.035f);
            kioskCard = global::Ascend.Build.Empty("Card", k, new Vector3(-0.3f, 1.25f, 0.25f), Quaternion.Euler(0, 180f, 0)).transform;
            var ck = new MeshKit();
            ck.Box(Vector3.zero, new Vector3(0.36f, 0.24f, 0.01f), Palette.Hex("F7F2E6"), 0.2f);
            ck.Box(new Vector3(0, 0, -0.006f), new Vector3(0.3f, 0.12f, 0.002f), Palette.Hex("B9BEC8"), 0.1f);
            ck.Spawn("CardMesh", kioskCard, Mat, false);
            kioskResult = Txt.Make("Result", kioskCard, new Vector3(0, 0, -0.01f), "", 0.05f, Palette.Hex("2B2733"), Palette.Display, TextAlignmentOptions.Center, 0.3f);
            kioskCard.gameObject.SetActive(false);
        }

        void Scratch()
        {
            if (Time.time < kioskBusyUntil) return;
            var gs = GameState.I;
            const long Price = 50;
            if (gs == null || !gs.TakeChips(Price)) return;
            kioskBusyUntil = Time.time + 2.2f;
            // expected return ~74 %: the house always wins, also at the camping
            double r = Odds.R(); long prize = r < 0.005 ? 2000 : r < 0.035 ? 300 : r < 0.115 ? 100 : r < 0.315 ? 50 : 0;
            kioskCard.gameObject.SetActive(true);
            kioskResult.text = "· · ·";
            Sfx.Play("scratch", kioskCard.position, 0.8f);
            StartCoroutine(Reveal(prize, Price));
        }

        System.Collections.IEnumerator Reveal(long prize, long price)
        {
            yield return new WaitForSeconds(1.0f);
            kioskResult.text = prize <= 0 ? "SIGUE\nPROBANDO" : prize == price ? "REINTEGRO\n<size=80%>" + prize + "</size>" : "¡PREMIO!\n<size=80%>" + prize + " FICHAS</size>";
            kioskResult.color = prize > price ? Palette.Hex("1E8A4A") : prize > 0 ? Palette.Hex("2B2733") : Palette.Hex("C0392B");
            GameState.I.Pay(prize, price, kioskCard.position);
            if (prize > price) { Sfx.Play("win_small", kioskCard.position, 0.8f); Juice.Sparks(kioskCard.position, Palette.Gold, 24); }
            else if (prize <= 0) Sfx.Play("lose_soft", kioskCard.position, 0.6f);
            GameState.I.Notify(prize <= 0 ? "RASCA Y GANA · NADA. COMO SIEMPRE." : prize == price ? "RASCA Y GANA · TE DEVUELVEN LO TUYO" : "RASCA Y GANA · ¡" + prize + " FICHAS!", prize > price ? Palette.Mint : Palette.Muted);
            yield return new WaitForSeconds(2.5f);
            if (Time.time >= kioskBusyUntil) kioskCard.gameObject.SetActive(false);
        }

        // ================================================================== per frame
        void Update()
        {
            var headT = TrailerView.Head;
            if (headT == null || home == null) return;
            var head = headT.position;
            float t = Time.time;

            // neighbours: look at you, gesture now and then, talk when you are close
            foreach (var n in npcs)
            {
                if (n.body == null) continue;
                var hp = n.body.HeadBone != null ? n.body.HeadBone.position : n.body.transform.position + Vector3.up * 1.6f;
                float d = Vector3.Distance(head, hp);
                bool near = d < n.range;
                n.body.lookAtTarget = near && !n.asleep && !n.seated ? headT : null;
                n.pivot.position = hp + Vector3.up * 0.5f;
                n.tag.transform.position = hp + Vector3.up * 0.26f;
                if (near != n.pivot.gameObject.activeSelf) { n.pivot.gameObject.SetActive(near); n.tag.gameObject.SetActive(near); if (near) { n.next = 0f; } }
                if (near && t >= n.next)
                {
                    n.bubble.text = n.lines[n.idx % n.lines.Length];
                    n.bubble.ForceMeshUpdate();
                    var sz = n.bubble.GetRenderedValues(false);
                    if (n.thinks)
                    {
                        n.plate.localScale = new Vector3(Mathf.Max(0.3f, sz.x) * 1.22f + 0.3f, Mathf.Max(0.1f, sz.y) * 1.5f + 0.32f, 1f);
                        n.puffA.localScale = Vector3.one * 0.13f; n.puffA.localPosition = new Vector3(-0.1f, -0.2f, 0.012f);
                        n.puffB.localScale = Vector3.one * 0.07f; n.puffB.localPosition = new Vector3(-0.2f, -0.33f, 0.012f);
                    }
                    else
                    n.plate.localScale = new Vector3(Mathf.Max(0.3f, sz.x) + 0.2f, Mathf.Max(0.1f, sz.y) + 0.14f, 1f);
                    // the bubble grows upwards from just above the head, so long lines never cover the face
                    n.bubble.transform.localPosition = new Vector3(0, sz.y * 0.5f, 0);
                    n.plate.localPosition = new Vector3(0, sz.y * 0.5f, 0.012f);
                    n.idx++;
                    n.next = t + 3.2f + n.bubble.text.Length * 0.045f;
                    if (n.emotes.Length > 0) n.body.Play(n.emotes[Random.Range(0, n.emotes.Length)], 1.6f);
                }
                if (!near && n.emotes.Length > 0 && t >= n.emoteAt)
                {
                    n.emoteAt = t + Random.Range(6f, 14f);
                    n.body.Play(n.emotes[Random.Range(0, n.emotes.Length)], 1.4f);
                }
                if (n.asleep && !n.eyesDone && Time.timeSinceLevelLoad > 1f)
                {
                    n.eyesDone = true;
                    var hbe = n.body as HumanoidBody; if (hbe != null) hbe.CloseEyes(true);
                }
                if (n.lying)
                {
                    // one hand on the belly (rising and falling with the snoring), the other hanging off the sofa
                    var bt = n.body.transform; var al = home.TransformDirection(n.lieAlong); var fr = home.TransformDirection(n.lieFront);
                    bool rightOut = Vector3.Dot(bt.right, fr) > 0f;
                    var belly = bt.position + bt.up * (0.98f + 0.0f) + bt.forward * (0.36f + Mathf.Sin(t * 1.3f) * 0.012f);
                    var hang = bt.position + al * 1.12f + fr * 0.5f + Vector3.down * 0.3f;
                    n.body.Reach(!rightOut, belly, 0.95f); n.body.Reach(rightOut, hang, 0.9f);
                    n.pivot.position = hp + Vector3.up * 0.75f;
                }
                if (n.asleep && near && !n.lying)
                {
                    float z = Mathf.Repeat(t * 0.5f, 1f);
                    n.pivot.position = hp + Vector3.up * (0.35f + z * 0.25f);
                }
            }

            UpdateDog(head, t);
            UpdateHens(t);

            // cat: purrs when you are close, meows once when you are right next to it
            if (cat != null)
            {
                float cd = Vector3.Distance(head, cat.position);
                if (catPurr != null) catPurr.volume = Mathf.Lerp(catPurr.volume, cd < 2.5f ? 0.45f : 0f, Time.deltaTime * 2f);
                if (cd < 1.3f && t > catMeowAt) { catMeowAt = t + 12f; Sfx.Play("meow", cat.position, 0.6f); }
                cat.localScale = new Vector3(1f, 1f + Mathf.Sin(t * 1.6f) * 0.03f, 1f);   // breathing
            }

            // laundry in the wind
            for (int i = 0; i < laundry.Count; i++)
            {
                float w = Mathf.Sin(t * 1.7f + i * 0.9f) * 16f + Mathf.Sin(t * 3.1f + i) * 5f + 8f;
                laundry[i].localRotation = Quaternion.Euler(w, Mathf.Sin(t * 0.8f + i) * 6f, 0);
            }

            // newspapers tumble along the wind and wrap round the park
            for (int i = 0; i < papers.Count; i++)
            {
                var p = papers[i].localPosition;
                float gust = 0.6f + Mathf.PerlinNoise(t * 0.3f, i * 7.3f) * 1.4f;
                p += Wind * gust * Time.deltaTime;
                p.y = 0.05f + Mathf.Max(0f, Mathf.Sin(t * 2.3f + i * 2f)) * 0.35f * gust;
                if (p.x > 20f || p.z > 20f) { p.x = -19f; p.z = Random.Range(-18f, 6f); }
                papers[i].localPosition = p;
                papers[i].localRotation = Quaternion.Euler(Mathf.Sin(t * 3f + i) * 40f, t * 90f + i * 50f, Mathf.Cos(t * 2.4f + i) * 35f);
            }

            // birds circling overhead
            foreach (var b in birds)
            {
                float a = t * b.speed + b.phase;
                var p = new Vector3(Mathf.Cos(a) * b.radius, b.height + Mathf.Sin(a * 2.3f) * 0.8f, 3f + Mathf.Sin(a) * b.radius);
                var fwd = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * Mathf.Sign(b.speed);
                b.root.localPosition = p;
                b.root.localRotation = Quaternion.LookRotation(fwd) * Quaternion.Euler(0, 0, -15f * Mathf.Sign(b.speed));
                float flap = Mathf.Sin(t * 9f + b.phase * 3f) * 35f;
                if (Mathf.Repeat(t * 0.2f + b.phase, 1f) > 0.6f) flap = 8f;   // glide now and then
                b.wl.localRotation = Quaternion.Euler(0, 0, flap);
                b.wr.localRotation = Quaternion.Euler(0, 0, -flap);
            }

            // fire and the broken lamp flicker
            for (int i = 0; i < flicker.Count; i++)
            {
                var l = flicker[i]; if (l == null) continue;
                float b = flickerBase[i];
                if (b > 0) l.intensity = b * (0.8f + Mathf.PerlinNoise(t * 7f, i * 3.1f) * 0.45f);
                else l.intensity = Mathf.PerlinNoise(t * 3f, 9.1f) > 0.32f ? -b : (Random.value > 0.5f ? -b * 0.15f : 0f);
            }
        }

        /// <summary>Trailer: put the dog somewhere (home-local) and let it come to the camera.</summary>
        public void PlaceDog(Vector3 local) { if (dog == null) return; dog.root.localPosition = local; dog.following = false; }

        void UpdateDog(Vector3 head, float t)
        {
            if (dog == null) return;
            var pos = dog.root.localPosition;
            var playerLocal = home.InverseTransformPoint(head); playerLocal.y = 0;
            float toPlayer = Vector3.Distance(new Vector3(pos.x, 0, pos.z), playerLocal);
            // follows you around when you get close, wanders its round otherwise
            if (!dog.following && toPlayer < 4f) { dog.following = true; dog.barkAt = Mathf.Min(dog.barkAt, t + 0.5f); }
            else if (dog.following && toPlayer > 12f) dog.following = false;
            Vector3 target;
            float stopAt;
            if (dog.following) { target = playerLocal + (pos - playerLocal).normalized * 1.2f; stopAt = 0.4f; }
            else
            {
                var w = DogPath[dog.wp]; target = new Vector3(w.x, 0, w.y); stopAt = 0.3f;
                if (Vector3.Distance(new Vector3(pos.x, 0, pos.z), target) < 0.4f)
                {
                    if (dog.wait <= 0f) dog.wait = Random.Range(1f, 4f);
                    dog.wait -= Time.deltaTime;
                    if (dog.wait <= 0f) dog.wp = (dog.wp + 1) % DogPath.Length;
                }
            }
            var delta = target - new Vector3(pos.x, 0, pos.z);
            float dist = delta.magnitude;
            float speed = dist > stopAt ? (dog.following ? Mathf.Clamp(dist * 1.5f, 0.6f, 3.2f) : dog.speed) : 0f;
            if (speed > 0f)
            {
                var dir = delta / dist;
                pos += dir * Mathf.Min(speed * Time.deltaTime, dist);
                var want = Quaternion.LookRotation(dir);
                dog.root.localRotation = Quaternion.Slerp(dog.root.localRotation, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
            }
            else if (dog.following)
            {
                var look = playerLocal - new Vector3(pos.x, 0, pos.z);
                if (look.sqrMagnitude > 0.01f) dog.root.localRotation = Quaternion.Slerp(dog.root.localRotation, Quaternion.LookRotation(look), Time.deltaTime * 4f);
            }
            pos.y = 0f;
            dog.root.localPosition = pos;
            // legs trot, body bobs, tail wags (faster when it is with you)
            float gait = speed > 0.05f ? t * (6f + speed * 3f) : 0f;
            for (int i = 0; i < 4; i++)
            {
                float ph = (i == 0 || i == 3) ? 0f : Mathf.PI;
                dog.legs[i].localRotation = Quaternion.Euler(speed > 0.05f ? Mathf.Sin(gait + ph) * 28f : 0f, 0, 0);
            }
            dog.body.localPosition = new Vector3(0, 0.42f + (speed > 0.05f ? Mathf.Abs(Mathf.Sin(gait)) * 0.025f : Mathf.Sin(t * 2f) * 0.004f), 0);
            dog.tail.localRotation = Quaternion.Euler(0, Mathf.Sin(t * (dog.following ? 18f : 6f)) * (dog.following ? 40f : 15f), 0);
            dog.head.localRotation = Quaternion.Euler(speed > 0.05f ? 5f : Mathf.Sin(t * 0.7f) * 8f, dog.following && speed < 0.05f ? Mathf.Sin(t * 1.3f) * 20f : 0f, 0);
            if (dog.following && t >= dog.barkAt) { dog.barkAt = t + Random.Range(6f, 14f); Sfx.Play("bark", dog.head.position, 0.6f, Random.Range(0.92f, 1.08f)); }
        }

        void UpdateHens(float t)
        {
            foreach (var h in hens)
            {
                var p = h.root.localPosition;
                var d = h.target - p; d.y = 0;
                if (d.magnitude < 0.05f)
                {
                    h.wait -= Time.deltaTime;
                    h.peck = Mathf.Repeat(h.peck + Time.deltaTime * 2.2f, 1f);
                    h.head.localRotation = Quaternion.Euler(h.peck < 0.3f ? 60f * Mathf.Sin(h.peck / 0.3f * Mathf.PI) : 0f, 0, 0);
                    if (h.wait <= 0f) { h.target = h.centre + new Vector3(Random.Range(-2.6f, 2.6f), 0, Random.Range(-2.6f, 2.6f)); h.wait = Random.Range(1.5f, 4f); }
                }
                else
                {
                    var dir = d.normalized;
                    p += dir * Mathf.Min(0.55f * Time.deltaTime, d.magnitude);
                    p.y = Mathf.Abs(Mathf.Sin(t * 14f)) * 0.015f;
                    h.root.localPosition = p;
                    h.root.localRotation = Quaternion.Slerp(h.root.localRotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);
                    h.head.localRotation = Quaternion.Euler(Mathf.Sin(t * 14f) * 10f, 0, 0);
                }
                if (t >= h.cluckAt) { h.cluckAt = t + Random.Range(6f, 16f); Sfx.Play("cluck", h.root.position, 0.4f, Random.Range(0.9f, 1.2f)); }
            }
        }
    }
}
