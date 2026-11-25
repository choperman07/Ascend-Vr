using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    public enum RaidPart { Body, Core, Shield, Orb, Drone }

    /// <summary>ROOFTOP RAID · first-person shooter vs THE HOUSE. Pay an entry, fight with a blaster in each hand
    /// (trigger = fire) next to your squad. Phases: orbs → laser sweep (duck!) → drones. Win the fight to roll the
    /// published loot table — with a 1% shot at THE HOUSE JACKPOT. Lose (HP 0 or time out) and the entry stays with the house.</summary>
    public partial class RaidArena : MonoBehaviour
    {
        public static readonly long[] Entries = { 5000, 10000, 25000 };
        /// <summary>Mafia mode, day 15: free entry, a tougher boss, and the result goes to the run.</summary>
        public static bool FinalMode;
        public static bool FinalFighting;
        public static event System.Action<bool> Finished;
        static int BossMaxHP { get { return FinalMode ? 1400 : 1000; } }
        const float FightTime = 180f;
        const int BodyDmg = 3, CoreDmg = 8, SquadDmg = 1;
        public Vector3 bossLocal = new Vector3(0, 5.5f, 13f);

        int entryIdx = 1;
        bool fighting, live;
        int bossHP, phase;
        float playerHP, timeLeft, invulnUntil;
        Transform boss, core, crown, shieldRing, eye;
        Renderer coreR, bossR;
        readonly List<Transform> shields = new List<Transform>();
        readonly List<Transform> orbs = new List<Transform>();
        readonly List<Transform> drones = new List<Transform>();
        readonly List<AvatarBody> squad = new List<AvatarBody>();
        Transform[] blasters = new Transform[2];
        float[] nextShot = new float[2];
        readonly Vector3[] blasterRest = new Vector3[2];   // overlapping recoil kicks must return to the holder's rest pose
        readonly List<LineRenderer> tracers = new List<LineRenderer>();
        AudioSource laserLoop, spinLoop;
        float standH;   // the player's standing eye height above the arena floor (max seen this fight)
        TextMeshPro hpT, timeT, playerT, infoT, entryT, lootT;
        Transform hpFill;
        PokeButton startBtn;
        Renderer hurtR; MaterialPropertyBlock hurtMpb; float hurt;
        AudioSource heartbeat;
        LineRenderer laser; bool laserOn; float laserAngle;
        long stake;
        readonly Queue<LineRenderer> tracerPool = new Queue<LineRenderer>();

        /// <summary>Yaw of the imported blaster model inside its holder (barrel along +Z).</summary>
        public static float BlasterYaw = 180f;

        Vector3 Start0 { get { return transform.TransformPoint(new Vector3(0, 0, -1.7f)); } }

        void Start()
        {
            BuildConsole();
            BuildBoss();
            BuildVault();
            for (int i = 0; i < 3; i++)
            {
                int ri = Odds.Range(0, Roster.Count);
                var bot = Avatars.Create("Squad" + i, transform, i == 0 ? new Vector3(-2.2f, 0, -1.9f) : i == 1 ? new Vector3(2.2f, 0, -1.9f) : new Vector3(-1.3f, 0, -3.1f), 0f, Roster.Face(ri), RoninRole.Security);
                var gun = Props.Place(i == 1 ? "BlasterRepeater" : "Blaster", bot.RightHand, new Vector3(0, 0.02f, 0.06f), 0f, 1f);
                if (gun == null) { var gk = new MeshKit(); BlasterMesh(gk, Vector3.zero, Quaternion.identity); gun = gk.Spawn("Blaster", bot.RightHand, new Vector3(0, 0.02f, 0.06f), Quaternion.identity, Palette.Neon, false); }
                gun.transform.localRotation = Quaternion.Euler(0, 90f, -90f);
                var ap = bot.gameObject.AddComponent<AvatarProfile>(); ap.displayName = Roster.Name(ri); ap.rarity = Roster.Face(ri); ap.xp = Roster.XP(ri); ap.squad = Roster.Squad(ri); ap.status = "RAID SQUAD";
                bot.pose = Pose.Guard;
                bot.lookAtTarget = boss;
                squad.Add(bot);
            }
            var lg = new GameObject("Laser"); lg.transform.SetParent(transform, false);
            laser = lg.AddComponent<LineRenderer>(); laser.positionCount = 3; laser.widthMultiplier = 0.25f; laser.sharedMaterial = Palette.FlatGlow;
            laser.startColor = Palette.Red; laser.endColor = Palette.A(Palette.Red, 0.5f); laser.enabled = false;
            Idle();
            GameState.I.Changed += OnStateChanged;
        }

        void OnDestroy() { if (GameState.Exists) GameState.I.Changed -= OnStateChanged; }
        // the console's POOL figure grows with every bet in the casino: keep it current while nobody is fighting
        void OnStateChanged() { if (!fighting && entryT != null) Idle(); }

        // ------------------------------------------------------------ build
        void BuildConsole()
        {
            var k = new MeshKit();
            k.Box(new Vector3(0, 0.5f, -1.0f), new Vector3(1.4f, 1.0f, 0.5f), Palette.Hex("140F20"), 0.03f);
            k.Box(new Vector3(0, 1.02f, -1.0f), new Vector3(1.46f, 0.04f, 0.56f), Palette.Hex("1E1530"), 0.05f);
            k.Bar(new Vector3(-0.73f, 1.045f, -1.28f), new Vector3(0.73f, 1.045f, -1.28f), 0.012f, Palette.Red, 1f);
            k.Ring(new Vector3(0, 0.005f, -1.7f), 0.45f, 0.5f, 40, Quaternion.identity, Palette.Red, 1f);
            k.Spawn("Console", transform, Palette.Glossy, false);
            var cc = Build.Empty("ConsoleCol", transform, new Vector3(0, 0.5f, -1.0f)).AddComponent<BoxCollider>(); cc.size = new Vector3(1.46f, 1.0f, 0.56f);
            Txt.Make("StandLabel", transform, new Vector3(0, 0.01f, -2.3f), "STAND HERE TO RAID", 0.06f, Palette.Red, Palette.Display).transform.localRotation = Quaternion.Euler(90, 0, 0);
            var info = Build.Empty("Info", transform, new Vector3(-2.6f, 1.9f, -0.5f), Quaternion.Euler(0, -25, 0)).transform;
            Txt.Glow("Title", info, new Vector3(0, 0.55f, 0), "RAID: THE HOUSE", 0.2f, Palette.Red);
            infoT = Txt.Make("Rules", info, new Vector3(0, 0.12f, 0), "", 0.05f, Palette.Text, Palette.Mono, TextAlignmentOptions.Center, 2.2f);
            var sb = new System.Text.StringBuilder("WIN THE FIGHT, THEN ROLL THE LOOT:\n");
            for (int i = 0; i < Odds.RaidNames.Length; i++)
                sb.Append(Odds.RaidNames[i]).Append(i == 0 ? " (POOL × ENTRY/25K)" : " " + Odds.RaidMult[i].ToString("0.#") + "x").Append(i % 2 == 1 ? "\n" : " · ");
            sb.Append("\nFIGHT IN THE VAULT (NO COVER): DODGE CARDS, CHIP SLAMS, ROULETTE · DUCK THE LASER\nLOSE THE FIGHT = ENTRY LOST · TRIGGER = FIRE");
            infoT.text = sb.ToString();
            entryT = Txt.Make("Entry", transform, new Vector3(0, 1.12f, -1.31f), "", 0.05f, Palette.Gold, Palette.Display);
            entryT.transform.localRotation = Quaternion.Euler(10, 0, 0);
            lootT = Txt.Glow("Loot", info, new Vector3(0, -0.45f, 0), "", 0.12f, Palette.Gold);
            var flip = Quaternion.identity;
            PokeButton.Create("EntryDown", transform, new Vector3(-0.5f, 1.06f, -1.05f), flip, 0.03f, Palette.Muted, "-", () => { if (!fighting) { entryIdx = Mathf.Max(0, entryIdx - 1); Idle(); } });
            PokeButton.Create("EntryUp", transform, new Vector3(-0.3f, 1.06f, -1.05f), flip, 0.03f, Palette.Muted, "+", () => { if (!fighting) { entryIdx = Mathf.Min(Entries.Length - 1, entryIdx + 1); Idle(); } });
            startBtn = PokeButton.Create("StartRaid", transform, new Vector3(0.3f, 1.06f, -1.05f), flip, 0.06f, Palette.Red, "START\nRAID", () => StartCoroutine(Fight()), 0.035f);
            // weapon rack decoration
            if (Props.Place("Blaster", transform, new Vector3(-0.32f, 1.05f, -0.9f), 90f, 1f) == null | Props.Place("BlasterRepeater", transform, new Vector3(0.32f, 1.05f, -0.9f), 90f, 1f) == null)
            {
                var rk = new MeshKit();
                for (int i = 0; i < 2; i++) BlasterMesh(rk, new Vector3(-0.25f + i * 0.5f, 1.12f, -1.0f), Quaternion.Euler(0, 90, 0));
                rk.Spawn("RackBlasters", transform, Palette.Neon, false);
            }
            // in-world HUD during the fight
            var hud = Build.Empty("BossHud", transform, bossLocal + new Vector3(0, 3.2f, 0)).transform;
            hudT = hud;
            hud.gameObject.AddComponent<Billboard>();
            hpT = Txt.Glow("BossHP", hud, Vector3.zero, "", 0.4f, Palette.Red);
            var bar = new MeshKit(); bar.Quad(new Vector3(0, 0, 0.01f), new Vector2(4f, 0.18f), Quaternion.identity, Palette.Dark, 0.2f);
            bar.Spawn("HPBack", hud, new Vector3(0, -0.4f, 0), Quaternion.identity, Palette.Neon, false);
            var fill = new MeshKit(); fill.Quad(new Vector3(2f, 0, 0), new Vector2(4f, 0.16f), Quaternion.identity, Palette.Red, 1f);
            hpFill = fill.Spawn("HPFill", hud, new Vector3(-2f, -0.4f, -0.005f), Quaternion.identity, Palette.Neon, false).transform;
            timeT = Txt.Make("Time", hud, new Vector3(0, -0.75f, 0), "", 0.25f, Palette.Text, Palette.Display);
        }

        static void BlasterMesh(MeshKit k, Vector3 pos, Quaternion rot)
        {
            k.Push(pos, rot);
            // chunky toy blaster in the Casino FREE colours: red body, white barrel, dark grip, gold stripes
            k.Box(new Vector3(0, 0.0f, 0.06f), new Vector3(0.06f, 0.075f, 0.22f), Palette.Hex("D9404E"), 0f);
            k.Box(new Vector3(0, 0.045f, 0.03f), new Vector3(0.045f, 0.025f, 0.12f), Palette.Hex("EEF0F5"), 0f);
            k.Box(new Vector3(0, -0.065f, 0.0f), new Vector3(0.04f, 0.1f, 0.045f), Quaternion.Euler(-15, 0, 0), Palette.Hex("3A3D48"), 0f);
            k.Cylinder(new Vector3(0, 0.0f, 0.21f), 0.024f, 0.02f, 0.1f, 14, Quaternion.Euler(90, 0, 0), Palette.Hex("EEF0F5"), 0f);
            k.Torus(new Vector3(0, 0.0f, 0.255f), 0.024f, 0.007f, 14, 4, Quaternion.Euler(90, 0, 0), Palette.Hex("FFD54A"), 1f);
            k.Bar(new Vector3(0.031f, 0.012f, -0.03f), new Vector3(0.031f, 0.012f, 0.16f), 0.006f, Palette.Gold, 0.6f);
            k.Bar(new Vector3(-0.031f, 0.012f, -0.03f), new Vector3(-0.031f, 0.012f, 0.16f), 0.006f, Palette.Gold, 0.6f);
            k.Pop();
        }

        void BuildBoss()
        {
            boss = Build.Empty("TheHouse", transform, bossLocal, Quaternion.identity).transform;   // the model faces -Z: the arena
            var k = new MeshKit();
            // THE HOUSE: a towering art-deco croupier automaton. Tuxedo torso tapering into a stack of chip-like tiers,
            // an ivory mask with one red visor, white-gloved hands fanning cards, and a roulette wheel turning behind its
            // head like a halo. (Faces -Z; the red core sits in the shirt front, the visor is where the laser comes from.)
            Color suit = Palette.Hex("17101F"), suit2 = Palette.Hex("2A1A44"), ivory = Palette.Hex("EDE6F4"), glove = Palette.Hex("F6F2FA");
            // torso (flattened front to back so the core stands proud of the shirt)
            k.Push(Vector3.zero, Quaternion.identity, new Vector3(1f, 1f, 0.6f));
            k.Cylinder(new Vector3(0, -0.75f, 0), 1.0f, 1.42f, 1.9f, 24, Quaternion.identity, suit, 0.02f);
            k.Cylinder(new Vector3(0, 0.3f, 0), 1.42f, 0.55f, 0.2f, 24, Quaternion.identity, suit2, 0.05f);
            k.Cylinder(new Vector3(0, -1.78f, 0), 0.78f, 1.0f, 0.16f, 24, Quaternion.identity, Palette.Gold, 0.5f);
            k.Pop();
            // the lower body: tiers narrowing to a point, banded in gold like stacked chips
            float[] tierR = { 0.74f, 0.58f, 0.42f, 0.27f };
            for (int t = 0; t < tierR.Length; t++)
            {
                float y = -2.05f - t * 0.34f;
                k.Cylinder(new Vector3(0, y, 0), tierR[t] * 0.82f, tierR[t], 0.28f, 20, Quaternion.identity, t % 2 == 0 ? suit2 : suit, 0.03f);
                k.Torus(new Vector3(0, y + 0.14f, 0), tierR[t], 0.022f, 24, 5, Quaternion.identity, Palette.Gold, 0.8f);
                for (int d = 0; d < 8; d++)
                {
                    float a = (d + t * 0.5f) / 8f * Mathf.PI * 2f;
                    k.Box(new Vector3(Mathf.Cos(a) * tierR[t] * 0.92f, y, Mathf.Sin(a) * tierR[t] * 0.92f), new Vector3(0.09f, 0.2f, 0.03f), Quaternion.Euler(0, 90f - a * Mathf.Rad2Deg, 0), t % 2 == 0 ? Palette.Magenta : Palette.Teal, 0.9f);
                }
            }
            k.Cylinder(new Vector3(0, -3.55f, 0), 0f, 0.2f, 0.5f, 12, Quaternion.identity, Palette.Gold, 0.7f);
            // shirt front, lapels, bow tie, studs
            k.Box(new Vector3(0, -0.55f, -0.72f), new Vector3(0.78f, 1.5f, 0.12f), ivory, 0.12f);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                k.Box(new Vector3(sd * 0.62f, -0.35f, -0.76f), new Vector3(0.5f, 1.55f, 0.1f), Quaternion.Euler(0, 0, sd * 16f), suit2, 0.04f);
                k.Bar(new Vector3(sd * 0.3f, 0.36f, -0.83f), new Vector3(sd * 0.52f, -1.12f, -0.83f), 0.03f, Palette.Gold, 0.8f);
                k.Cylinder(new Vector3(sd * 0.2f, 0.34f, -0.86f), 0.2f, 0.03f, 0.36f, 4, Quaternion.Euler(0, 0, sd * 90f), Palette.Red, 0.5f);
            }
            k.Sphere(new Vector3(0, 0.34f, -0.9f), 0.075f, 10, Palette.Gold, 0.8f);
            for (int b = 0; b < 3; b++) k.Sphere(new Vector3(0, -1.18f - b * 0.17f, -0.8f + b * 0.02f), 0.045f, 8, Palette.Gold, 0.9f);
            k.Torus(new Vector3(0, -0.6f, -0.8f), 0.5f, 0.05f, 36, 6, Quaternion.Euler(90, 0, 0), Palette.Gold, 1f);
            // shoulders: angular epaulettes with gold fringe
            for (int sd = -1; sd <= 1; sd += 2)
            {
                k.Push(new Vector3(sd * 1.52f, 0.2f, 0), Quaternion.Euler(0, 0, -sd * 14f));
                k.Box(new Vector3(0, 0, 0), new Vector3(1.0f, 0.3f, 0.95f), suit2, 0.05f);
                k.Box(new Vector3(0, 0.17f, 0), new Vector3(1.06f, 0.05f, 1.0f), Palette.Gold, 0.7f);
                for (int fz = 0; fz < 6; fz++)
                    k.Cylinder(new Vector3(sd * 0.48f, -0.3f, -0.4f + fz * 0.16f), 0.03f, 0.04f, 0.32f, 6, Quaternion.identity, Palette.Gold, 0.6f);
                k.Pop();
                // arm: upper arm, elbow joint, forearm, cuff
                Vector3 sh = new Vector3(sd * 1.72f, 0.02f, 0), el = new Vector3(sd * 2.25f, -1.0f, -0.25f), wr = new Vector3(sd * 1.95f, -1.55f, -1.15f);
                k.Capsule(sh, el, 0.24f, 12, suit, 0.02f);
                k.Sphere(el, 0.27f, 12, Palette.Gold, 0.5f);
                k.Capsule(el, wr, 0.2f, 12, suit, 0.02f);
                var fore = Quaternion.LookRotation((wr - el).normalized, Vector3.up);
                k.Cylinder(Vector3.Lerp(el, wr, 0.9f), 0.25f, 0.25f, 0.2f, 16, fore * Quaternion.Euler(90, 0, 0), ivory, 0.15f);
                k.Sphere(Vector3.Lerp(el, wr, 0.9f) + new Vector3(sd * 0.25f, 0, 0), 0.05f, 8, Palette.Gold, 1f);
                // gloved hand, palm up, holding a fan of five cards
                k.Push(wr + (wr - el).normalized * 0.22f, Quaternion.Euler(-35f, sd * 20f, 0));
                k.Box(new Vector3(0, 0, 0), new Vector3(0.42f, 0.13f, 0.4f), glove, 0.1f);
                for (int fg = 0; fg < 4; fg++)
                    k.Capsule(new Vector3(-0.15f + fg * 0.1f, 0.02f, -0.2f), new Vector3(-0.15f + fg * 0.1f, 0.2f, -0.34f), 0.047f, 8, glove, 0.1f);
                k.Capsule(new Vector3(sd * -0.22f, 0.02f, 0.02f), new Vector3(sd * -0.3f, 0.2f, -0.12f), 0.05f, 8, glove, 0.1f);
                for (int c = 0; c < 5; c++)
                {
                    k.Push(new Vector3(0, 0.1f, -0.05f), Quaternion.Euler(-12f, 0, (c - 2) * -17f));
                    k.Box(new Vector3(0, 0.48f, c * 0.012f), new Vector3(0.36f, 0.54f, 0.012f), Color.white, 0.55f);
                    bool red = (c + (sd > 0 ? 1 : 0)) % 2 == 0;
                    k.Box(new Vector3(0, 0.48f, c * 0.012f - 0.008f), new Vector3(0.14f, 0.14f, 0.004f), Quaternion.Euler(0, 0, 45f), red ? Palette.Red : Palette.Hex("15101F"), red ? 0.9f : 0f);
                    k.Box(new Vector3(-0.12f, 0.68f, c * 0.012f - 0.008f), new Vector3(0.05f, 0.05f, 0.004f), Quaternion.Euler(0, 0, 45f), red ? Palette.Red : Palette.Hex("15101F"), red ? 0.9f : 0f);
                    k.Pop();
                }
                k.Pop();
            }
            // neck and head: an ivory mask, long and narrow, with a red visor, brow ridge and cheek vents
            k.Cylinder(new Vector3(0, 0.45f, 0), 0.3f, 0.26f, 0.3f, 14, Quaternion.identity, Palette.Gold, 0.4f);
            k.Sphere(new Vector3(0, 1.08f, 0.02f), new Vector3(0.6f, 0.8f, 0.64f), 22, suit, 0.03f);
            k.Sphere(new Vector3(0, 1.02f, -0.12f), new Vector3(0.5f, 0.74f, 0.52f), 22, ivory, 0.1f);
            k.Box(new Vector3(0, 1.05f, -0.56f), new Vector3(0.86f, 0.17f, 0.2f), Palette.Red, 1.3f);
            k.Box(new Vector3(0, 1.2f, -0.57f), new Vector3(0.96f, 0.08f, 0.22f), Quaternion.Euler(-10f, 0, 0), suit, 0.02f);
            for (int sd = -1; sd <= 1; sd += 2)
                for (int v = 0; v < 3; v++)
                    k.Box(new Vector3(sd * 0.3f, 0.78f - v * 0.1f, -0.55f + v * 0.03f), new Vector3(0.2f, 0.035f, 0.06f), Quaternion.Euler(0, sd * -24f, sd * 8f), suit, 0.02f);
            k.Bar(new Vector3(0, 0.95f, -0.64f), new Vector3(0, 0.5f, -0.5f), 0.035f, Palette.Gold, 0.7f);
            k.Cylinder(new Vector3(0, 0.34f, -0.36f), 0f, 0.16f, 0.2f, 4, Quaternion.identity, Palette.Gold, 0.6f);
            var go = k.Spawn("Body", boss, Palette.Lacquer, false);
            // the halo: a roulette wheel standing behind head and shoulders, turning slowly
            var halo = Build.Empty("Halo", boss, new Vector3(0, 0.95f, 0.75f)).transform;
            var hk = new MeshKit();
            const int Pockets = 37; float hr = 2.05f;
            for (int p = 0; p < Pockets; p++)
            {
                float a = p / (float)Pockets * 360f;
                var q = Quaternion.Euler(0, 0, a);
                Color pc = p == 0 ? Palette.Hex("1FA85A") : p % 2 == 0 ? Palette.Hex("C2182F") : Palette.Hex("120D1A");
                hk.Box(q * new Vector3(0, hr, 0), new Vector3(hr * 2f * Mathf.PI / Pockets * 0.92f, 0.42f, 0.07f), q, pc, p % 2 == 0 ? 0.7f : 0.05f);
                hk.Box(q * new Vector3(0, hr + 0.3f, -0.01f), new Vector3(0.05f, 0.14f, 0.09f), q, Palette.Gold, 0.9f);
            }
            hk.Torus(Vector3.zero, hr + 0.25f, 0.045f, 64, 6, Quaternion.Euler(90, 0, 0), Palette.Gold, 0.9f);
            hk.Torus(Vector3.zero, hr - 0.25f, 0.035f, 64, 6, Quaternion.Euler(90, 0, 0), Palette.Gold, 0.9f);
            hk.Torus(Vector3.zero, hr - 0.75f, 0.02f, 48, 5, Quaternion.Euler(90, 0, 0), Palette.Magenta, 1f);
            for (int sp = 0; sp < 8; sp++)
            {
                var q = Quaternion.Euler(0, 0, sp * 45f);
                hk.Bar(q * new Vector3(0, 0.75f, 0.02f), q * new Vector3(0, hr - 0.25f, 0.02f), 0.025f, Palette.Gold, 0.6f);
            }
            hk.Spawn("Wheel", halo, Palette.Lacquer, false);
            halo.gameObject.AddComponent<Spin>().speed = new Vector3(0, 0, 10f);
            bossR = go.GetComponent<Renderer>();
            var bodyCol = go.AddComponent<CapsuleCollider>(); bodyCol.radius = 1.4f; bodyCol.height = 4.2f; bodyCol.center = new Vector3(0, -0.2f, 0);
            var bt = go.AddComponent<RaidTarget>(); bt.part = RaidPart.Body; bt.arena = this;
            var ck = new MeshKit(); ck.Sphere(Vector3.zero, 0.38f, 18, Palette.Red, 1.5f);
            core = ck.Spawn("Core", boss, new Vector3(0, -0.6f, -0.95f), Quaternion.identity, Palette.Neon, false).transform;
            coreR = core.GetComponent<Renderer>();
            core.gameObject.AddComponent<SphereCollider>().radius = 0.42f;
            var ct = core.gameObject.AddComponent<RaidTarget>(); ct.part = RaidPart.Core; ct.arena = this;
            Build.GlowSprite(core, Vector3.zero, 2.2f, Palette.Red, 0.9f);
            eye = Build.Empty("Eye", boss, new Vector3(0, 1.05f, -0.75f)).transform;
            Build.GlowSprite(eye, Vector3.zero, 1.6f, Palette.Red, 0.8f);
            // the crown: a band of gold with nine points (tall and short in turn), each tipped with a jewel
            var cr = new MeshKit();
            cr.Cylinder(Vector3.zero, 0.5f, 0.56f, 0.2f, 28, Quaternion.identity, Palette.Gold, 0.8f, false);
            cr.Torus(new Vector3(0, -0.1f, 0), 0.5f, 0.045f, 28, 6, Quaternion.identity, Palette.Gold, 1f);
            cr.Torus(new Vector3(0, 0.1f, 0), 0.56f, 0.03f, 28, 6, Quaternion.identity, Palette.Gold, 1f);
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f; bool tall = i % 2 == 0; float h = tall ? 0.55f : 0.34f;
                var at = new Vector3(Mathf.Cos(a) * 0.55f, 0.1f + h * 0.5f, Mathf.Sin(a) * 0.55f);
                cr.Cylinder(at, 0.1f, 0.0f, h, 4, Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), Palette.Gold, 0.9f);
                cr.Sphere(at + Vector3.up * (h * 0.5f + 0.03f), tall ? 0.06f : 0.04f, 8, tall ? Palette.Red : Palette.Teal, 1.4f);
                cr.Sphere(new Vector3(Mathf.Cos(a) * 0.54f, 0f, Mathf.Sin(a) * 0.54f), 0.04f, 8, i % 3 == 0 ? Palette.Magenta : Palette.Hex("F6F2FA"), 1.1f);
            }
            crown = cr.Spawn("Crown", boss, new Vector3(0, 1.95f, 0), Quaternion.identity, Palette.Lacquer, false).transform;
            shieldRing = Build.Empty("Shields", boss, new Vector3(0, -0.4f, 0)).transform;
            for (int i = 0; i < 8; i++)
            {
                var sk = new MeshKit();
                sk.Cylinder(Vector3.zero, 0.5f, 0.5f, 0.1f, 24, Quaternion.Euler(90, 0, 0), Palette.Magenta, 0.6f, true, Palette.Gold, 0.8f);
                float a = i / 8f * Mathf.PI * 2f;
                var s = sk.Spawn("Shield" + i, shieldRing, new Vector3(Mathf.Cos(a) * 2.6f, 0, Mathf.Sin(a) * 2.6f), Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))), Palette.Neon, false).transform;
                s.gameObject.AddComponent<SphereCollider>().radius = 0.5f;
                var st = s.gameObject.AddComponent<RaidTarget>(); st.part = RaidPart.Shield; st.arena = this; st.hp = 6;
                shields.Add(s);
            }
            // orbit of giant casino chips and four holo-card pylons around THE HOUSE
            var orbit = Build.Empty("ChipOrbit", boss, new Vector3(0, 0.2f, 0)).transform;
            var ok = new MeshKit();
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                var c = new Vector3(Mathf.Cos(a) * 4.2f, Mathf.Sin(a * 3f) * 0.5f, Mathf.Sin(a) * 4.2f);
                var col = TableGame.Accents[i % TableGame.Accents.Length];
                ok.Cylinder(c, 0.42f, 0.42f, 0.09f, 24, Quaternion.Euler(70, i * 30f, 0), Palette.Hex("1A1426"), 0.1f, true, col, 0.9f);
                ok.Torus(c, 0.36f, 0.025f, 24, 4, Quaternion.Euler(70, i * 30f, 0), Color.white, 1f);
            }
            ok.Spawn("Chips", orbit, Palette.Neon, false);
            orbit.gameObject.AddComponent<Spin>().speed = new Vector3(0, -24f, 0);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                var pk = new MeshKit();
                pk.Box(Vector3.zero, new Vector3(0.9f, 1.3f, 0.03f), Color.white, 0.9f);
                pk.Box(new Vector3(0, 0, -0.02f), new Vector3(0.78f, 1.18f, 0.01f), i % 2 == 0 ? Palette.Red : Palette.Hex("15101F"), 0.8f);
                var pylon = pk.Spawn("CardPylon" + i, boss, new Vector3(Mathf.Cos(a) * 5.6f, -1.5f, Mathf.Sin(a) * 5.6f), Quaternion.Euler(0, -i * 90f - 45f, 12f), Palette.Neon, false);
                pylon.AddComponent<Bob>().amplitude = 0.4f;
                Txt.Glow("Suit" + i, pylon.transform, new Vector3(0, 0, -0.03f), new[] { "A", "K", "Q", "J" }[i], 0.5f, i % 2 == 0 ? Color.white : Palette.Red);
            }
            boss.localScale = Vector3.one * 1.35f;
            boss.gameObject.AddComponent<Bob>().amplitude = 0.25f;
            var ap = boss.gameObject.AddComponent<AvatarProfile>(); ap.displayName = "THE HOUSE"; ap.rarity = Rarity.Mythic; ap.xp = 9999999; ap.status = "RAID BOSS · THE HOUSE ALWAYS WINS?";
        }

        // ------------------------------------------------------------ state
        void Idle()
        {
            entryT.text = "ENTRY " + Entries[entryIdx].ToString("N0") + "  ·  JACKPOT SHARE " + Mathf.RoundToInt(Mathf.Clamp01(Entries[entryIdx] / 25000f) * 100) + "%  ·  POOL " + GameState.I.jackpot.ToString("N0");
            hpT.text = "THE HOUSE"; timeT.text = "WAITING FOR CHALLENGERS"; worldHudCode = -1;
            hpFill.localScale = Vector3.one;
        }

        IEnumerator Fight()
        {
            if (fighting) yield break;
            if (!FinalMode && !GameState.I.CanAccess(Tier.Elite)) { GameState.I.Notify("RAIDS NEED ELITE TIER", Palette.Red); Sfx.UI("ui_error"); yield break; }
            stake = FinalMode ? 0 : Entries[entryIdx];
            if (!FinalMode && !GameState.I.Wager(stake)) yield break;
            FinalFighting = FinalMode;
            GameState.I.Record("raid");
            fighting = true; startBtn.SetInteractable(false);
            lootT.text = "";
            bossHP = BossMaxHP; phase = 1; playerHP = 100; timeLeft = FightTime;
            foreach (var s in shields) { s.gameObject.SetActive(true); s.GetComponent<RaidTarget>().hp = 6; }
            ClearAttacks();   // nothing from a previous fight (late orbs, slam chips) may survive into this one
            enraged = false; attacking = 0; lastAtk = Atk.Combo; live = false; standH = 0f;
            yield return StartCoroutine(EnterVault());
            EquipBlasters(true);
            BuildHurt();
            BuildPlayerHud();
            Sfx.Music("raid_loop", 0.55f);
            yield return StartCoroutine(BossEntrance());
            Voice.Say("boss_welcome", boss, 3.2f);
            foreach (var b in squad) { b.pose = Pose.Shoot; b.Play(Emote.Nod, 0.6f); }
            yield return new WaitForSeconds(2.2f);
            Voice.Say("boss_always", boss, 3.2f);
            float nextDrone = float.MaxValue, nextSquad = Time.time + 0.5f;
            nextAttack = Time.time + 1.2f;
            bool won = false; live = true;
            while (true)
            {
                timeLeft -= Time.deltaTime;
                // phases
                float hpk = bossHP / (float)BossMaxHP;
                if (phase == 1 && hpk <= 0.66f) { yield return StartCoroutine(PhaseShift(2)); nextAttack = Mathf.Max(nextAttack, Time.time + 2f); }
                if (phase == 2 && hpk <= 0.33f) { yield return StartCoroutine(PhaseShift(3)); nextDrone = Time.time + 1.5f; nextAttack = Mathf.Max(nextAttack, Time.time + 2f); }
                if (!enraged && phase == 3 && hpk <= 0.12f) Enrage();
                // attack deck (one pattern at a time; combos in phase 3 / enrage)
                if (Time.time > nextAttack && attacking == 0 && !laserOn) { nextAttack = float.MaxValue; StartCoroutine(RunAttack()); }
                if (Time.time > nextDrone && drones.Count < (enraged ? 5 : 3)) { SpawnDrone(); nextDrone = Time.time + (enraged ? 3.5f : 6f); }
                if (Time.time > nextSquad) { SquadFire(); nextSquad = Time.time + Random.Range(0.35f, 0.7f); }
                TrackStanding();
                MoveOrbs(); MoveDrones(); MoveCards();
                if (PlayerLeftVault()) timeLeft = 0;
                Shoot();
                UpdateHud();
                if (bossHP <= 0) { won = true; break; }
                if (playerHP <= 0 || timeLeft <= 0) break;
                yield return null;
            }
            live = false;
            ClearAttacks();
            DestroyPlayerHud();
            if (heartbeat != null) { Destroy(heartbeat.gameObject); heartbeat = null; }
            hurt = 0; if (hurtR != null) hurtR.enabled = false;   // the red low-HP tint stayed on after a win
            bool final = FinalMode;
            FinalFighting = false;
            if (won) yield return StartCoroutine(Victory());
            else yield return StartCoroutine(Defeat());
            if (final && Finished != null) Finished(won);
            EquipBlasters(false);
            foreach (var b in squad) b.pose = Pose.Guard;
            if (FloorManager.I != null && FloorManager.I.Current >= 0)
            {
                // the roof has no track of its own (music ""): stop the raid loop like FloorManager does on arrival
                var m = Tower.Floors[FloorManager.I.Current].music;
                if (string.IsNullOrEmpty(m)) Sfx.StopMusic(); else Sfx.Music(m, 0.35f);
            }
            fighting = false; startBtn.SetInteractable(true);
            Idle();
        }

        IEnumerator PhaseShift(int p)
        {
            phase = p;
            invulnUntil = Time.time + 2.5f;
            Sfx.Play("boss_phase", boss.position, 1f);
            Sfx.Play("boss_roar", boss.position, 1f, 0.85f);
            Voice.Say(p == 2 ? "boss_phase2" : "boss_phase3", boss, 3.2f);
            StartCoroutine(Fx.Shake(boss, 0.15f, 1.2f));
            Juice.Sparks(core.position, Palette.Red, 80);
            XRIn.HapticBoth(0.6f, 0.4f);
            foreach (var s in shields) { s.gameObject.SetActive(true); s.GetComponent<RaidTarget>().hp = 4 + p * 2; }
            yield return null;
        }

        int worldHudCode = -1;
        void UpdateHud()
        {
            UpdatePlayerHud();
            if (phud == null) { hudHP = Mathf.CeilToInt(playerHP); hudTime = Mathf.CeilToInt(Mathf.Max(0, timeLeft)); }
            float hpk = Mathf.Max(0, bossHP) / (float)BossMaxHP;
            hpFill.localScale = new Vector3(hpk, 1, 1);
            // world HUD texts: rebuilt only when a shown number changes (UpdatePlayerHud refreshed the cache first)
            int code = (hudTime * 1000 + hudHP) * 4 + phase;
            if (code != worldHudCode) { worldHudCode = code; hpT.text = "THE HOUSE · PHASE " + phase; timeT.text = hudTime + "s   ·   YOUR HP " + hudHP; }
            shieldRing.Rotate(0, (40f + phase * 25f) * Time.deltaTime, 0, Space.Self);
            crown.Rotate(0, 30f * Time.deltaTime, 0, Space.Self);
            bool inv = Time.time < invulnUntil;
            Fx.SetColor(coreR, inv ? Palette.Silver : Palette.Red, inv ? 0.5f : 1.2f + Mathf.Sin(Time.time * 8f) * 0.4f);
            hurt = Mathf.MoveTowards(hurt, playerHP < 30 ? 0.15f + Mathf.Sin(Time.time * 6f) * 0.05f : 0f, Time.deltaTime * 0.8f);
            if (hurtR != null) { hurtR.enabled = hurt > 0.005f; hurtMpb.SetColor("_BaseColor", new Color(0.8f, 0, 0.05f, hurt)); hurtR.SetPropertyBlock(hurtMpb); }
            if (playerHP < 30 && heartbeat == null && Rig.Ready) heartbeat = Sfx.Loop("heartbeat_loop", Rig.I.Head, 0.8f, 0f);
        }

        // ------------------------------------------------------------ player weapons
        void EquipBlasters(bool on)
        {
            for (int h = 0; h < 2; h++)
            {
                if (blasters[h] != null) { Destroy(blasters[h].gameObject); blasters[h] = null; }
                if (!on || !Rig.Ready) continue;
                var ctrl = h == 0 ? Rig.I.LeftCtrl : Rig.I.RightCtrl;
                if (ctrl == null) continue;
                bool desk = !XRIn.HasHeadset;   // no headset: hold the blasters in front of the camera
                var parent = desk ? Rig.I.Head : ctrl;
                var pos = desk ? new Vector3(h == 0 ? -0.18f : 0.18f, -0.2f, 0.35f) : new Vector3(0, -0.02f, 0.02f);
                var rot = desk ? Quaternion.identity : Quaternion.Euler(-20, 0, 0);
                var holder = Build.Empty("Blaster" + h, parent, pos, rot);
                // procedural toy blaster in the Casino FREE colours (a library blaster is used only if one is registered)
                var model = Props.Place(h == 0 ? "BlasterRepeater" : "Blaster", holder.transform, new Vector3(0, -0.06f, -0.12f), BlasterYaw, 1f);
                if (model == null) { var k = new MeshKit(); BlasterMesh(k, Vector3.zero, Quaternion.identity); k.Spawn("Mesh", holder.transform, Palette.Neon, false); }
                Build.Empty("Muzzle", holder.transform, new Vector3(0, 0, model != null ? 0.3f : 0.26f));
                blasters[h] = holder.transform; blasterRest[h] = pos;
            }
            if (on) Sfx.UI("blaster_charge", 0.8f);
        }

        void Shoot()
        {
            for (int h = 0; h < 2; h++)
            {
                var b = blasters[h];
                if (b == null) continue;
                bool held = XRIn.Trigger((Hand)h) > 0.55f;
                if (!held || Time.time < nextShot[h]) continue;
                nextShot[h] = Time.time + 0.15f;
                var muzzle = b.GetChild(0);
                var dir = muzzle.forward;
                if (!XRIn.HasHeadset && Rig.Ready) dir = Rig.I.Head.forward;   // desktop: aim with the view
                Vector3 end = muzzle.position + dir * 60f;
                // nearest RaidTarget or solid surface: a plain SphereCast stopped at the first trigger of any kind (zone
                // volumes, the player's own hand/finger triggers) and swallowed the shot
                RaidTarget t; Vector3 point;
                if (ShotHit(muzzle.position, XRIn.HasHeadset ? 0.08f : 0.14f, dir, out t, out point))
                {
                    end = point;
                    if (t != null) Hit(t, point, h);
                }
                Tracer(muzzle.position, end, Palette.Teal);
                StartCoroutine(MuzzleFlash(muzzle));
                Sfx.Play(Sfx.Clip(h == 0 ? "kn_repeater" : "kn_blaster") != null ? (h == 0 ? "kn_repeater" : "kn_blaster") : "blaster_shot", muzzle.position, 0.5f, Random.Range(0.92f, 1.08f));
                XRIn.Haptic((Hand)h, 0.35f, 0.04f);
                StartCoroutine(Kick(b, blasterRest[h]));
            }
        }

        static readonly RaycastHit[] shotHits = new RaycastHit[24];
        bool ShotHit(Vector3 from, float radius, Vector3 dir, out RaidTarget target, out Vector3 point)
        {
            target = null; point = Vector3.zero;
            int n = Physics.SphereCastNonAlloc(from, radius, dir, shotHits, 80f, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue; bool any = false;
            var rigRoot = Rig.Ready ? Rig.I.transform : null;
            for (int i = 0; i < n; i++)
            {
                var c = shotHits[i].collider;
                if (c == null || shotHits[i].distance >= best) continue;
                if (rigRoot != null && c.transform.IsChildOf(rigRoot)) continue;
                var t = c.GetComponentInParent<RaidTarget>();
                if (t == null && c.isTrigger) continue;
                // a hit reported at distance 0 (overlapping at the start) has no point: use the muzzle
                point = shotHits[i].distance > 0f ? shotHits[i].point : from;
                best = shotHits[i].distance; target = t; any = true;
            }
            return any;
        }

        IEnumerator Kick(Transform b, Vector3 rest)
        {
            b.localPosition = rest + new Vector3(0, 0.005f, -0.025f);
            for (float t = 0; t < 0.1f; t += Time.deltaTime) { if (b == null) yield break; b.localPosition = Vector3.Lerp(b.localPosition, rest, t / 0.1f); yield return null; }
            if (b != null) b.localPosition = rest;
        }

        void Hit(RaidTarget t, Vector3 p, int hand)
        {
            switch (t.part)
            {
                case RaidPart.Body:
                case RaidPart.Core:
                    if (Time.time < invulnUntil) { Sfx.Play("shield_up", p, 0.4f, 1.5f); return; }
                    int dmg = t.part == RaidPart.Core ? CoreDmg : BodyDmg;
                    bossHP -= dmg;
                    DamageNumber(p, t.part == RaidPart.Core ? "CRIT " + dmg : dmg.ToString(), t.part == RaidPart.Core ? Palette.Gold : Palette.Text);
                    Sfx.Play(t.part == RaidPart.Core ? "boss_hit" : "hit_marker", p, t.part == RaidPart.Core ? 0.8f : 0.5f);
                    Juice.Sparks(p, t.part == RaidPart.Core ? Palette.Gold : Palette.Red, t.part == RaidPart.Core ? 14 : 6);
                    if (t.part == RaidPart.Core) { XRIn.Haptic((Hand)hand, 0.6f, 0.05f); if (Random.value < 0.06f) Voice.Say("boss_hit", boss, 3.2f); }
                    Fx.SetColor(bossR, Color.white, 0.8f);
                    StartCoroutine(ClearFlash());
                    break;
                case RaidPart.Shield:
                    t.hp--;
                    Sfx.Play("hit_marker", p, 0.4f, 0.8f);
                    Juice.Sparks(p, Palette.Magenta, 5);
                    if (t.hp <= 0) { Sfx.Play("explosion_small", p, 0.8f); Juice.Sparks(p, Palette.Gold, 30); t.gameObject.SetActive(false); DamageNumber(p, "SHIELD DOWN", Palette.Magenta); }
                    break;
                case RaidPart.Orb:
                case RaidPart.Drone:
                    Sfx.Play(t.part == RaidPart.Drone && Sfx.Clip("kn_drone_die") != null ? "kn_drone_die" : "explosion_small", p, 0.8f);
                    Juice.Sparks(p, t.part == RaidPart.Orb ? Palette.Violet : Palette.Coral, 24);
                    orbs.Remove(t.transform); drones.Remove(t.transform);
                    DamageNumber(p, t.part == RaidPart.Drone ? "DRONE DOWN" : "ORB", Palette.Mint);
                    Destroy(t.gameObject);
                    GameState.I.Record("raid_kills");
                    break;
            }
        }

        IEnumerator ClearFlash() { yield return new WaitForSeconds(0.05f); if (bossR != null) Fx.ClearColor(bossR); }

        void Tracer(Vector3 a, Vector3 b, Color c)
        {
            LineRenderer lr = tracerPool.Count > 0 ? tracerPool.Dequeue() : null;
            if (lr == null)
            {
                var g = new GameObject("Tracer"); g.transform.SetParent(transform, false);
                lr = g.AddComponent<LineRenderer>(); lr.positionCount = 2; lr.sharedMaterial = Palette.FlatGlow; lr.widthMultiplier = 0.025f;
                tracers.Add(lr);
            }
            lr.startColor = c; lr.endColor = Palette.A(c, 0.1f);
            lr.SetPosition(0, a); lr.SetPosition(1, b);
            lr.gameObject.SetActive(true);
            StartCoroutine(HideTracer(lr));
        }
        IEnumerator HideTracer(LineRenderer lr) { yield return new WaitForSeconds(0.06f); if (lr == null) yield break; lr.gameObject.SetActive(false); tracerPool.Enqueue(lr); }

        /// <summary>HideTracer coroutines die with OnDisable: hide every tracer and rebuild the pool from scratch.</summary>
        void ResetTracers()
        {
            tracerPool.Clear();
            foreach (var lr in tracers) if (lr != null) { lr.gameObject.SetActive(false); tracerPool.Enqueue(lr); }
        }

        void SquadFire()
        {
            if (squad.Count == 0) return;
            var target = Random.value < 0.3f && drones.Count > 0 && drones[0] != null ? drones[0].position : core.position + Random.insideUnitSphere * 0.8f;
            // never shoot through the player: the rear bot stands right behind your spawn point
            AvatarBody b = null;
            int first = Odds.Range(0, squad.Count);
            for (int k = 0; k < squad.Count && b == null; k++)
            {
                var c = squad[(first + k) % squad.Count];
                if (c != null && !LineNearPlayer(c.RightHand.position, target, 0.45f)) b = c;
            }
            if (b == null) return;
            Tracer(b.RightHand.position, target, Palette.Mint);
            Sfx.Play("blaster_shot", b.RightHand.position, 0.25f, 1.2f);
            if (Time.time >= invulnUntil) bossHP -= SquadDmg;
        }

        bool LineNearPlayer(Vector3 a, Vector3 b, float r)
        {
            if (!Rig.Ready) return false;
            var head = Rig.I.Head.position;
            float feet = Rig.I.transform.position.y;
            var ab = b - a; float len2 = ab.sqrMagnitude; if (len2 < 1e-6f) return false;
            // closest point of the segment to the body axis (sampled at chest and head height)
            for (int i = 0; i < 2; i++)
            {
                var p = new Vector3(head.x, i == 0 ? head.y : Mathf.Lerp(feet, head.y, 0.6f), head.z);
                float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
                if ((a + ab * k - p).sqrMagnitude < r * r) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ boss attacks
        Vector3 PlayerHead { get { return Rig.Ready ? Rig.I.Head.position : Start0 + Vector3.up * 1.6f; } }

        void SpawnOrb()
        {
            var o = SpawnShared("Orb", transform, Vector3.zero, Quaternion.identity, Palette.Neon, OrbMesh).transform;
            o.position = core.position + Random.insideUnitSphere * 0.5f;
            o.rotation = Quaternion.LookRotation(PlayerHead + Vector3.down * 0.35f - o.position);
            Destroy(o.gameObject, 9f);   // a dodged orb flies on and expires instead of circling forever
            Build.GlowSprite(o, Vector3.zero, 1.1f, Palette.Violet, 0.9f);
            var oc = o.gameObject.AddComponent<SphereCollider>(); oc.radius = 0.35f; oc.isTrigger = true;   // trigger: hurts you, never shoves you
            var t = o.gameObject.AddComponent<RaidTarget>(); t.part = RaidPart.Orb; t.arena = this;
            orbs.Add(o);
            Sfx.Play("boss_orb_fire", o.position, 0.9f);
        }

        void MoveOrbs()
        {
            float speed = OrbSpeed;
            for (int i = orbs.Count - 1; i >= 0; i--)
            {
                var o = orbs[i];
                if (o == null) { orbs.RemoveAt(i); continue; }
                var to = PlayerHead + Vector3.down * 0.35f - o.position;
                if (HitsBody(o.position, 0.22f))
                {
                    Damage(12f, o.position);
                    orbs.RemoveAt(i); Destroy(o.gameObject);
                    continue;
                }
                // homing with a limited turn rate that stops in the last 3 m: they chase you, but a late sidestep
                // dodges them (perfect homing at 4-6 m/s could only be shot down, never dodged, in a VR room)
                if (to.sqrMagnitude > 9f && to.sqrMagnitude > 1e-4f)
                    o.rotation = Quaternion.RotateTowards(o.rotation, Quaternion.LookRotation(to), (enraged ? 75f : 60f) * Time.deltaTime);
                o.position += o.forward * speed * Time.deltaTime;
            }
        }

        void SpawnDrone()
        {
            Transform d;
            var model = Props.Place("EnemyDrone", transform, Vector3.zero, 0f, 0.8f, false, null, 0.3f);
            if (model != null) { d = model.transform; Build.GlowSprite(d, new Vector3(0, 0.25f, 0), 0.9f, Palette.Coral, 0.7f); }
            else
            {
                d = SpawnShared("Drone", transform, Vector3.zero, Quaternion.identity, Palette.Neon, DroneMesh).transform;
            }
            d.position = core.position;
            var dc = d.gameObject.AddComponent<SphereCollider>(); dc.radius = 0.35f; dc.isTrigger = true;
            var t = d.gameObject.AddComponent<RaidTarget>(); t.part = RaidPart.Drone; t.arena = this;
            drones.Add(d);
            Sfx.Loop("drone_buzz_loop", d, 0.5f, 1f, 12f);
        }

        void MoveDrones()
        {
            for (int i = drones.Count - 1; i >= 0; i--)
            {
                var d = drones[i];
                if (d == null) { drones.RemoveAt(i); continue; }
                float a = Time.time * 0.6f + i * 1.6f;
                var target = PlayerHead + new Vector3(Mathf.Cos(a) * 3.5f, 0.8f + Mathf.Sin(a * 1.7f) * 0.6f, Mathf.Sin(a) * 3.5f);
                d.position = Vector3.Lerp(d.position, target, Time.deltaTime * 0.8f);
                d.rotation = Quaternion.LookRotation(PlayerHead - d.position);
                if (!droneCharging.Contains(d) && Random.value < Time.deltaTime * (enraged ? 0.9f : 0.6f)) StartCoroutine(DroneShot(d));
            }
        }

        readonly HashSet<Transform> droneCharging = new HashSet<Transform>();

        /// <summary>Telegraphed drone shot: a red sight locks where your head IS, the drone charges for 0.55 s, then fires
        /// at that spot — step or lean out of the dot and it misses (no random hits you can't avoid).</summary>
        IEnumerator DroneShot(Transform d)
        {
            droneCharging.Add(d);
            var aim = PlayerHead;
            var lr = TracerLine();
            Sfx.Play("blaster_charge", d.position, 0.35f, 1.4f);
            for (float t = 0; t < 0.55f && live && d != null; t += Time.deltaTime)
            {
                if (lr != null) { lr.enabled = true; lr.SetPosition(0, d.position); lr.SetPosition(1, aim); lr.widthMultiplier = 0.004f + 0.01f * (t / 0.55f); lr.startColor = lr.endColor = Palette.A(Palette.Red, 0.35f + t); }
                yield return null;
            }
            if (lr != null) Destroy(lr.gameObject);
            if (d != null && live)
            {
                Tracer(d.position, aim, Palette.Coral);
                Sfx.Play(Sfx.Clip("kn_drone_shot") != null ? "kn_drone_shot" : "blaster_shot", d.position, 0.5f, 0.9f);
                if ((PlayerHead - aim).sqrMagnitude < 0.42f * 0.42f) Damage(6f, aim);
            }
            droneCharging.Remove(d);
        }

        LineRenderer TracerLine()
        {
            var go = new GameObject("DroneSight"); go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>(); lr.positionCount = 2; lr.sharedMaterial = Palette.FlatGlow; lr.enabled = false;
            Destroy(go, 2f);
            return lr;
        }

        IEnumerator Laser()
        {
            laserOn = true;
            Voice.Say("boss_laser", boss, 3.2f);
            Sfx.Play("laser_charge", eye.position, 1f);
            GameState.I.Notify(XRIn.HasHeadset ? "LASER INCOMING · DUCK!" : "LASER INCOMING · DUCK! (HOLD CTRL)", Palette.Red);
            // telegraph: thin line sweeping where the beam will go
            // the beam sits just under the player's own standing eye height and "ducked" means the head is below the
            // duck line (78% of standing height): the same move for a tall, short or seated player
            float floorY = Field.position.y;
            float stand = StandingHeight();
            float duckLine = stand * 0.78f;
            float beamY = floorY + duckLine + 0.14f;
            laser.enabled = true; laser.widthMultiplier = 0.02f;
            float t = 0;
            float side = Random.value < 0.5f ? -1f : 1f;
            while (t < 1.6f && live)
            {
                t += Time.deltaTime;
                laserAngle = side * 60f;
                SetLaser(beamY, laserAngle);
                yield return null;
            }
            if (!live) { laser.enabled = false; laserOn = false; yield break; }
            laserLoop = Sfx.Loop("laser_beam_loop", eye, 1f, 1f, 40f);
            laser.widthMultiplier = 0.35f;
            bool hit = false;
            t = 0;
            float playerAng = PlayerAngle();
            while (t < 2.4f && live)
            {
                t += Time.deltaTime;
                float prev = laserAngle;
                laserAngle = Mathf.Lerp(side * 60f, -side * 60f, t / 2.4f);
                SetLaser(beamY, laserAngle);
                playerAng = PlayerAngle();
                bool crossed = (prev - playerAng) * (laserAngle - playerAng) <= 0f;
                bool ducked = Rig.Ready && (Rig.I.Head.position.y - floorY < duckLine || XRIn.DesktopCrouch);
                if (crossed && !hit && Rig.Ready && !ducked) { hit = true; Damage(25f, Rig.I.Head.position); }
                else if (crossed && !hit && Rig.Ready) { hit = true; GameState.I.Unlock("LIMBO", "DUCKED UNDER THE HOUSE LASER", 300); Sfx.UI("whistle", 0.5f); }
                XRIn.HapticBoth(0.1f, 0.02f);
                yield return null;
            }
            if (laserLoop != null) Destroy(laserLoop.gameObject);
            laserLoop = null;
            laser.enabled = false; laserOn = false;
        }

        /// <summary>Same convention as SetLaser: Euler(0, a) · (0,0,-1) = (-sin a, 0, -cos a). (It used atan2(x, -z),
        /// the mirror image: away from the centre line the hit landed when the beam was on the other side of you.)</summary>
        float PlayerAngle()
        {
            var local = Field.InverseTransformPoint(PlayerHead) - BossLocal;
            return Mathf.Atan2(-local.x, -local.z) * Mathf.Rad2Deg;
        }

        /// <summary>The beam drops from the eye to the beam height within a few metres of THE HOUSE, then runs level across
        /// the whole arena — the height you see is the height that is checked, wherever you stand.</summary>
        void SetLaser(float y, float angle)
        {
            var start = eye.position;
            var dirLocal = Quaternion.Euler(0, angle, 0) * new Vector3(0, 0, -1);
            var near = Field.TransformPoint(BossLocal + dirLocal * 3f); near.y = y;
            var far = Field.TransformPoint(BossLocal + dirLocal * 32f); far.y = y;
            laser.SetPosition(0, start);
            laser.SetPosition(1, near);
            laser.SetPosition(2, far);
        }

        void TrackStanding()
        {
            if (!Rig.Ready || !inVault) return;
            float h = Rig.I.Head.position.y - Field.position.y;
            if (h > standH && h < 2.3f) standH = h;
        }

        float StandingHeight()
        {
            // seeded with a typical eye height until the player has been seen standing; clamped to sane values
            return Mathf.Clamp(standH > 0.5f ? standH : 1.6f, 1.05f, 2.0f);
        }

        void Damage(float amount, Vector3 at)
        {
            if (!live || Time.time < playerInvuln) return;
            playerInvuln = Time.time + 0.3f;
            playerHP = Mathf.Max(0, playerHP - amount * (FinalMode ? 1.3f : 1f));   // the final day hits harder
            Sfx.UI("player_hurt", 0.9f);
            XRIn.HapticBoth(0.9f, 0.18f);
            hurt = Mathf.Max(hurt, 0.45f);
            ShakeView(0.01f, 0.15f);
        }

        /// <summary>Camera shakes run on the rig, not on this arena: StopAllCoroutines (floor switched mid-fight) used to
        /// leave the camera offset displaced for good.</summary>
        void ShakeView(float amount, float dur)
        {
            if (Rig.Ready && Rig.I.Head.parent != null && Rig.I.isActiveAndEnabled) Rig.I.StartCoroutine(Fx.Shake(Rig.I.Head.parent, amount, dur));
        }

        void BuildHurt()
        {
            if (hurtR != null || !Rig.Ready) return;
            var k = new MeshKit(); k.Quad(Vector3.zero, new Vector2(2, 2), Quaternion.identity, Color.white, 1f);
            var go = k.Spawn("Hurt", Rig.I.Head, new Vector3(0, 0, 0.125f), Quaternion.identity, Palette.Fade, false);
            hurtR = go.GetComponent<Renderer>(); hurtMpb = new MaterialPropertyBlock(); hurtR.enabled = false;
        }

        // ------------------------------------------------------------ outcome
        IEnumerator Victory()
        {
            yield return StartCoroutine(BossDeath());
            yield return new WaitForSeconds(1.2f);
            yield return StartCoroutine(LeaveVault());
            Sfx.Music("raid_loop", 0.25f);
            Sfx.UI("raid_victory", 1f);
            Voice.Announce("ann_raid_clear");
            foreach (var b in squad) b.Play(Emote.Cheer, 3f);
            GameState.I.AddXP(GameState.RaidWinXP);
            GameState.I.Record("raid_cleared");
            yield return new WaitForSeconds(1.5f);
            if (FinalMode) { lootT.text = "¡DEUDA SALDADA!"; yield return new WaitForSeconds(2f); ResetBoss(); boss.gameObject.SetActive(true); yield break; }
            // loot roll: a slot-style reel whose filler is drawn from the same published odds
            float t = 0, next = 0;
            while (t < 3f)
            {
                t += Time.deltaTime;
                if (t >= next) { lootT.text = Odds.RaidNames[Odds.RollRaid()]; Sfx.UI("battle_tick", 0.4f, 1f + t * 0.15f); next = t + Mathf.Lerp(0.05f, 0.3f, t / 3f); }
                yield return null;
            }
            int r = Odds.RollRaid();
            var at = transform.TransformPoint(new Vector3(0, 1.6f, -1.2f));
            if (r == 0)
            {
                float share = Mathf.Clamp01(stake / 25000f);
                lootT.text = "THE HOUSE JACKPOT!";
                Voice.Announce("ann_jackpot");
                Sfx.UI("jackpot", 1f);
                long won = GameState.I.WinJackpotShare(share, stake, at);
                lootT.text = "THE HOUSE JACKPOT · " + won.ToString("N0");
                Sfx.UI("crowd_cheer", 1f);
            }
            else
            {
                long pay = (long)(stake * (double)Odds.RaidMult[r]);
                lootT.text = Odds.RaidNames[r] + " · " + pay.ToString("N0");
                GameState.I.Pay(pay, stake, at);
            }
            GameState.I.Unlock("RAIDER", "DEFEATED THE HOUSE", 1000);
            yield return new WaitForSeconds(4f);
            ResetBoss();
            boss.gameObject.SetActive(true);
        }

        IEnumerator Defeat()
        {
            Sfx.UI("raid_defeat", 1f);
            Voice.Say("boss_win", boss, 3.2f);
            Voice.Announce("ann_raid_fail");
            yield return StartCoroutine(BossGloat());
            yield return StartCoroutine(LeaveVault());
            lootT.text = playerHP <= 0 ? "KNOCKED OUT · THE HOUSE KEEPS THE ENTRY" : "TIME UP · THE HOUSE KEEPS THE ENTRY";
            lootT.color = Palette.Muted;
            foreach (var b in squad) b.Play(Emote.Facepalm, 2f);
            if (stake > 0) GameState.I.Pay(0, stake, transform.position);
            yield return new WaitForSeconds(3f);
            lootT.color = Palette.Gold;
            hurt = 0; if (hurtR != null) hurtR.enabled = false;
        }
    }
}
