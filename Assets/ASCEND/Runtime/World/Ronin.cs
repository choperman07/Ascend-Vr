using UnityEngine;

namespace Ascend
{
    public enum RoninRole { Player, Dealer, Security, DJ, Host, Bartender }

    /// <summary>Builds the "Neon Ronin" 3D avatar from the NFT art: helmet with teal visor, mask, chip shoulder pads,
    /// suit emblem and rarity upgrades (aura, armour, gold trims, halo and crown). One skinned mesh = one draw call.</summary>
    public static class RoninBuilder
    {
        // bone indices
        public const int Hips = 0, Spine = 1, Head = 2, UArmL = 3, FArmL = 4, HandL = 5, UArmR = 6, FArmR = 7, HandR = 8, ThighL = 9, ShinL = 10, ThighR = 11, ShinR = 12;
        static readonly string[] BoneNames = { "Hips", "Spine", "Head", "UpperArm.L", "ForeArm.L", "Hand.L", "UpperArm.R", "ForeArm.R", "Hand.R", "Thigh.L", "Shin.L", "Thigh.R", "Shin.R" };

        // joint positions in root space (front = +Z), 1.75 m tall
        static readonly Vector3[] JointPos =
        {
            new Vector3(0, 0.95f, 0), new Vector3(0, 1.12f, 0), new Vector3(0, 1.50f, 0.01f),
            new Vector3(-0.21f, 1.42f, 0), new Vector3(-0.235f, 1.14f, -0.01f), new Vector3(-0.245f, 0.88f, 0.01f),
            new Vector3(0.21f, 1.42f, 0), new Vector3(0.235f, 1.14f, -0.01f), new Vector3(0.245f, 0.88f, 0.01f),
            new Vector3(-0.1f, 0.93f, 0), new Vector3(-0.1f, 0.5f, 0.02f), new Vector3(0.1f, 0.93f, 0), new Vector3(0.1f, 0.5f, 0.02f)
        };
        static readonly int[] Parent = { -1, 0, 1, 1, 3, 4, 1, 6, 7, 0, 9, 0, 11 };

        public static RoninAnim Create(string name, Transform parent, Vector3 localPos, float yaw, Rarity rarity, RoninRole role = RoninRole.Player, float height = 1.75f)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = localPos;
            root.localRotation = Quaternion.Euler(0, yaw, 0);
            var bones = new Transform[BoneNames.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var b = new GameObject(BoneNames[i]).transform;
                b.SetParent(Parent[i] < 0 ? root : bones[Parent[i]], false);
                b.position = root.TransformPoint(JointPos[i]);
                bones[i] = b;
            }
            var kit = new MeshKit();
            Paint(kit, rarity, role);
            var smr = kit.SpawnSkinned("Body", root, bones, Palette.Neon);
            var anim = root.gameObject.AddComponent<RoninAnim>();
            anim.Init(bones, rarity, role);
            Extras(anim, bones, rarity, role);
            root.localScale = Vector3.one * (height / 1.75f);
            var col = root.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.9f, 0); col.height = 1.8f; col.radius = 0.26f;
            return anim;
        }

        static Color Accent(Rarity r)
        {
            switch (r)
            {
                case Rarity.Rare: return Palette.Teal;
                case Rarity.Epic: return Palette.Magenta;
                case Rarity.Legendary: return Palette.Gold;
                case Rarity.Mythic: return Palette.Coral;
                default: return Palette.Teal;
            }
        }

        static void Paint(MeshKit k, Rarity r, RoninRole role)
        {
            Color suit = Palette.Hex("17132A"), dark = Palette.Hex("0E0B19"), gold = Palette.Gold, mag = Palette.Magenta, teal = Palette.Teal;
            Color acc = Accent(r);
            Color visor = teal;
            if (role == RoninRole.Security) { suit = Palette.Hex("101016"); acc = Palette.Red; visor = Palette.Red; }
            if (role == RoninRole.Dealer) { visor = gold; }
            if (role == RoninRole.Host || role == RoninRole.DJ) { acc = role == RoninRole.DJ ? Palette.Mint : gold; }
            bool rarePlus = r >= Rarity.Rare, epicPlus = r >= Rarity.Epic, legPlus = r >= Rarity.Legendary, myth = r == Rarity.Mythic;
            Color trim = legPlus ? gold : acc;

            // ---------------- hips
            k.bone = Hips;
            k.Box(new Vector3(0, 0.95f, 0), new Vector3(0.3f, 0.16f, 0.2f), suit);
            k.Torus(new Vector3(0, 1.02f, 0), 0.155f, 0.012f, 24, 6, Quaternion.identity, trim, 1f);
            k.Box(new Vector3(0, 1.0f, 0.105f), new Vector3(0.07f, 0.05f, 0.02f), gold, 0.8f);

            // ---------------- torso
            k.bone = Spine;
            k.Push(new Vector3(0, 1.28f, 0), Quaternion.identity, new Vector3(1f, 1f, 0.72f));
            k.Cylinder(Vector3.zero, 0.17f, 0.215f, 0.36f, 16, Quaternion.identity, suit);
            k.Pop();
            k.Box(new Vector3(0, 1.17f, 0.07f), new Vector3(0.2f, 0.12f, 0.06f), dark);
            if (role == RoninRole.Dealer)
            {
                k.Box(new Vector3(0, 1.3f, 0.125f), new Vector3(0.13f, 0.28f, 0.02f), Palette.Text, 0.15f);   // shirt
                k.Box(new Vector3(0, 1.42f, 0.14f), new Vector3(0.09f, 0.035f, 0.02f), gold, 1f);           // bow tie
                k.Box(new Vector3(-0.09f, 1.28f, 0.12f), new Vector3(0.07f, 0.3f, 0.03f), Palette.Hex("0A0810"));
                k.Box(new Vector3(0.09f, 1.28f, 0.12f), new Vector3(0.07f, 0.3f, 0.03f), Palette.Hex("0A0810"));
            }
            else if (role == RoninRole.Security)
            {
                k.Box(new Vector3(0, 1.3f, 0.125f), new Vector3(0.05f, 0.28f, 0.02f), Palette.Hex("2A0A10"), 0f);
                k.Box(new Vector3(0, 1.39f, 0.137f), new Vector3(0.025f, 0.12f, 0.01f), Palette.Red, 1f);
                k.Box(new Vector3(0.11f, 1.36f, 0.13f), new Vector3(0.05f, 0.03f, 0.01f), gold, 1f);           // badge
            }
            else
            {
                // neon V lines from shoulders to the sternum (like the NFT art)
                k.Tube(new Vector3(-0.17f, 1.42f, 0.12f), new Vector3(0, 1.2f, 0.16f), 0.008f, 6, mag, 1f);
                k.Tube(new Vector3(0.17f, 1.42f, 0.12f), new Vector3(0, 1.2f, 0.16f), 0.008f, 6, mag, 1f);
                k.Tube(new Vector3(-0.12f, 1.12f, 0.13f), new Vector3(-0.17f, 1.38f, 0.13f), 0.006f, 6, teal, 1f);
                k.Tube(new Vector3(0.12f, 1.12f, 0.13f), new Vector3(0.17f, 1.38f, 0.13f), 0.006f, 6, teal, 1f);
                Emblem(k, new Vector3(0, 1.3f, 0.165f), r, 0.05f, gold);
            }
            k.Torus(new Vector3(0, 1.47f, 0), 0.085f, 0.02f, 20, 6, Quaternion.identity, gold, 0.9f);    // collar
            // shoulder chips (gold casino chips with a ring)
            for (int s = -1; s <= 1; s += 2)
            {
                var rot = Quaternion.Euler(0, 0, 90);
                k.Cylinder(new Vector3(0.235f * s, 1.43f, 0.0f), 0.075f, 0.075f, 0.028f, 18, rot, rarePlus ? gold : Palette.Metal, 0.25f, true, gold, 0.6f);
                k.Torus(new Vector3(0.25f * s, 1.43f, 0.0f), 0.06f, 0.008f, 18, 5, rot, acc, 1f);
                if (epicPlus)
                {
                    k.Box(new Vector3(0.22f * s, 1.47f, 0f), new Vector3(0.13f, 0.03f, 0.17f), Quaternion.Euler(0, 0, -18f * s), Palette.Hex("231633"));
                    k.Bar(new Vector3(0.16f * s, 1.5f, 0.09f), new Vector3(0.29f * s, 1.45f, 0.09f), 0.012f, mag, 1f);
                }
            }
            if (legPlus)
            {
                k.Bar(new Vector3(-0.15f, 1.1f, 0.12f), new Vector3(-0.19f, 1.4f, 0.1f), 0.014f, gold, 1f);
                k.Bar(new Vector3(0.15f, 1.1f, 0.12f), new Vector3(0.19f, 1.4f, 0.1f), 0.014f, gold, 1f);
                k.Torus(new Vector3(0, 1.12f, 0), 0.165f, 0.01f, 24, 5, Quaternion.identity, gold, 1f);
            }

            // ---------------- head (helmet, visor, mask)
            k.bone = Head;
            var hc = new Vector3(0, 1.635f, 0);
            k.Sphere(hc, new Vector3(0.118f, 0.135f, 0.128f), 18, suit);
            k.Torus(hc + new Vector3(0, 0.005f, 0.005f), 0.128f, 0.009f, 28, 5, Quaternion.Euler(0, 0, 90), role == RoninRole.Security ? Palette.Red : mag, 1f);
            k.Torus(hc + new Vector3(0, 0.0f, 0.02f), 0.121f, 0.024f, 22, 6, Quaternion.Euler(0, 0, 0) * Quaternion.Euler(0, -60, 0), visor, 1f, 120f);  // visor band
            k.Box(hc + new Vector3(0, -0.075f, 0.098f), new Vector3(0.11f, 0.06f, 0.04f), dark);                      // mask
            k.Bar(hc + new Vector3(-0.045f, -0.06f, 0.12f), hc + new Vector3(0, -0.1f, 0.12f), 0.006f, visor, 1f);
            k.Bar(hc + new Vector3(0.045f, -0.06f, 0.12f), hc + new Vector3(0, -0.1f, 0.12f), 0.006f, visor, 1f);
            for (int s = -1; s <= 1; s += 2)
                k.Cylinder(hc + new Vector3(0.12f * s, 0, -0.005f), 0.035f, 0.035f, 0.02f, 12, Quaternion.Euler(0, 0, 90), mag, 1f);
            if (role == RoninRole.DJ)
            {
                k.Torus(hc + new Vector3(0, 0.02f, 0), 0.135f, 0.012f, 24, 5, Quaternion.Euler(0, 0, 90), Palette.Metal, 0.2f, 180f);
                for (int s = -1; s <= 1; s += 2) k.Cylinder(hc + new Vector3(0.14f * s, 0, 0), 0.05f, 0.05f, 0.035f, 14, Quaternion.Euler(0, 0, 90), Palette.Mint, 0.8f);
            }
            else if (role == RoninRole.Security)
                k.Cylinder(hc + new Vector3(0.13f, -0.02f, 0.02f), 0.012f, 0.012f, 0.03f, 8, Quaternion.Euler(0, 0, 90), Palette.Text, 0.6f);
            else
                Emblem(k, hc + new Vector3(0, 0.085f, 0.112f), r, 0.032f, gold);
            if (myth)
            {
                k.Torus(hc + new Vector3(0, 0.12f, 0), 0.07f, 0.012f, 20, 5, Quaternion.identity, gold, 1f);
                for (int i = 0; i < 5; i++)
                {
                    float a = i * 72f * Mathf.Deg2Rad;
                    k.Cylinder(hc + new Vector3(Mathf.Sin(a) * 0.065f, 0.155f, Mathf.Cos(a) * 0.065f), 0.016f, 0.002f, 0.06f, 6, Quaternion.identity, gold, 1f, false);
                }
            }
            else if (legPlus) k.Box(hc + new Vector3(0, 0.13f, 0), new Vector3(0.025f, 0.03f, 0.18f), gold, 1f);   // crest

            // ---------------- arms
            for (int s = -1; s <= 1; s += 2)
            {
                int ua = s < 0 ? UArmL : UArmR, fa = s < 0 ? FArmL : FArmR, ha = s < 0 ? HandL : HandR;
                Vector3 sh = JointPos[ua], el = JointPos[fa], wr = JointPos[ha];
                k.bone = ua;
                k.Capsule(sh, el, 0.055f, 10, suit);
                k.Tube(sh + new Vector3(0.05f * s, -0.02f, 0.02f), el + new Vector3(0.045f * s, 0.03f, 0.02f), 0.006f, 5, acc, 1f, false);
                k.bone = fa;
                k.Capsule(el, wr, 0.047f, 10, suit);
                k.Torus(Vector3.Lerp(el, wr, 0.85f), 0.05f, 0.01f, 16, 5, Quaternion.identity, trim, 1f);
                if (epicPlus) k.Box(Vector3.Lerp(el, wr, 0.45f) + new Vector3(0.015f * s, 0, 0.02f), new Vector3(0.07f, 0.14f, 0.07f), Palette.Hex("231633"));
                k.bone = ha;
                k.Box(wr + new Vector3(0, -0.06f, 0.01f), new Vector3(0.06f, 0.09f, 0.035f), dark);
                k.Box(wr + new Vector3(0, -0.115f, 0.012f), new Vector3(0.055f, 0.04f, 0.03f), suit);
                k.Box(wr + new Vector3(-0.03f * s, -0.06f, 0.03f), new Vector3(0.02f, 0.05f, 0.02f), suit);
            }

            // ---------------- legs
            for (int s = -1; s <= 1; s += 2)
            {
                int th = s < 0 ? ThighL : ThighR, sh = s < 0 ? ShinL : ShinR;
                Vector3 hip = JointPos[th], knee = JointPos[sh], ankle = new Vector3(0.1f * s, 0.08f, 0.0f);
                k.bone = th;
                k.Capsule(hip, knee, 0.072f, 10, suit);
                k.Tube(hip + new Vector3(0.06f * s, -0.05f, 0.03f), knee + new Vector3(0.05f * s, 0.05f, 0.04f), 0.006f, 5, acc, 1f, false);
                k.bone = sh;
                k.Capsule(knee, ankle, 0.058f, 10, suit);
                k.Box(knee + new Vector3(0, 0, 0.055f), new Vector3(0.08f, 0.08f, 0.04f), legPlus ? gold : Palette.Metal, legPlus ? 0.5f : 0f);
                k.Box(new Vector3(0.1f * s, 0.05f, 0.04f), new Vector3(0.1f, 0.09f, 0.24f), dark);
                k.Bar(new Vector3(0.1f * s - 0.05f, 0.02f, 0.16f), new Vector3(0.1f * s + 0.05f, 0.02f, 0.16f), 0.012f, acc, 1f);
            }
        }

        public static void EmblemPublic(MeshKit k, Vector3 c, Rarity r, float s, Color col) { Emblem(k, c, r, s, col); }

        /// <summary>Card-suit emblem by rarity: club, diamond, heart, spade, crown.</summary>
        static void Emblem(MeshKit k, Vector3 c, Rarity r, float s, Color col)
        {
            switch (r)
            {
                case Rarity.Rare:   // diamond
                    k.Box(c, new Vector3(s * 0.7f, s * 0.7f, s * 0.2f), Quaternion.Euler(0, 0, 45), col, 1f); break;
                case Rarity.Epic:   // heart
                    k.Sphere(c + new Vector3(-s * 0.25f, s * 0.15f, 0), s * 0.32f, 8, col, 1f);
                    k.Sphere(c + new Vector3(s * 0.25f, s * 0.15f, 0), s * 0.32f, 8, col, 1f);
                    k.Box(c + new Vector3(0, -s * 0.12f, 0), new Vector3(s * 0.55f, s * 0.55f, s * 0.2f), Quaternion.Euler(0, 0, 45), col, 1f); break;
                case Rarity.Legendary:  // spade
                    k.Sphere(c + new Vector3(-s * 0.25f, -s * 0.05f, 0), s * 0.3f, 8, col, 1f);
                    k.Sphere(c + new Vector3(s * 0.25f, -s * 0.05f, 0), s * 0.3f, 8, col, 1f);
                    k.Box(c + new Vector3(0, s * 0.18f, 0), new Vector3(s * 0.55f, s * 0.55f, s * 0.2f), Quaternion.Euler(0, 0, 45), col, 1f);
                    k.Box(c + new Vector3(0, -s * 0.4f, 0), new Vector3(s * 0.12f, s * 0.35f, s * 0.15f), col, 1f); break;
                case Rarity.Mythic:  // crown
                    k.Box(c + new Vector3(0, -s * 0.2f, 0), new Vector3(s * 0.9f, s * 0.25f, s * 0.15f), col, 1f);
                    for (int i = -1; i <= 1; i++) k.Cylinder(c + new Vector3(i * s * 0.35f, s * 0.12f, 0), s * 0.12f, 0.001f, s * 0.45f, 6, Quaternion.identity, col, 1f, false);
                    break;
                default:  // club
                    k.Sphere(c + new Vector3(0, s * 0.25f, 0), s * 0.24f, 8, col, 1f);
                    k.Sphere(c + new Vector3(-s * 0.25f, -s * 0.05f, 0), s * 0.24f, 8, col, 1f);
                    k.Sphere(c + new Vector3(s * 0.25f, -s * 0.05f, 0), s * 0.24f, 8, col, 1f);
                    k.Box(c + new Vector3(0, -s * 0.35f, 0), new Vector3(s * 0.12f, s * 0.35f, s * 0.15f), col, 1f); break;
            }
        }

        /// <summary>Non-skinned extras parented to bones: aura glows, Mythic halo, orbiting chips, security label.</summary>
        static void Extras(RoninAnim a, Transform[] bones, Rarity r, RoninRole role)
        {
            var head = bones[Head]; var spine = bones[Spine];
            if (role == RoninRole.Player || role == RoninRole.Host)
            {
                if (r >= Rarity.Rare)
                    Build.GlowSprite(spine, new Vector3(0, 0.45f, -0.25f), r >= Rarity.Legendary ? 1.6f : 1.0f, r >= Rarity.Legendary ? Palette.Gold : Palette.RarityColor(r), r >= Rarity.Legendary ? 0.45f : 0.25f);
                if (r == Rarity.Mythic)
                {
                    var k = new MeshKit();
                    k.Torus(Vector3.zero, 0.27f, 0.014f, 40, 6, Quaternion.Euler(90, 0, 0), Palette.Gold, 1f);
                    var halo = k.Spawn("Halo", head, new Vector3(0, 0.16f, -0.16f), Quaternion.identity, Palette.Neon, false);
                    Build.GlowSprite(halo.transform, Vector3.zero, 0.8f, Palette.Gold, 0.5f);
                    var orbit = Build.Empty("Orbit", spine, new Vector3(0, 0.3f, 0));
                    orbit.AddComponent<Spin>().speed = new Vector3(0, 60, 0);
                    for (int i = 0; i < 4; i++)
                    {
                        float ang = i * 90f * Mathf.Deg2Rad;
                        var ck = new MeshKit();
                        ck.Cylinder(Vector3.zero, 0.035f, 0.035f, 0.008f, 14, Quaternion.Euler(90, 0, 0), i % 2 == 0 ? Palette.Magenta : Palette.Teal, 0.8f, true, Palette.Gold, 1f);
                        ck.Spawn("Chip", orbit.transform, new Vector3(Mathf.Sin(ang) * 0.42f, 0.1f * (i % 2), Mathf.Cos(ang) * 0.42f), Quaternion.identity, Palette.Neon, false);
                    }
                }
            }
            if (role == RoninRole.Security)
            {
                Txt.Make("Back", spine, new Vector3(0, 0.22f, -0.17f), "SECURITY", 0.06f, Palette.Red, Palette.Display);
            }
        }
    }
}
