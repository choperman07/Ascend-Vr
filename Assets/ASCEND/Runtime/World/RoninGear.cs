using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>The look a player (or guest) wears in the casino.
    /// NEON RONIN is the NFT collection's look (helmet with magenta outline and teal visor, suit emblem on the forehead,
    /// gold chip shoulder pads, more armour per rarity, halo + orbiting chips for Mythic) built on top of the rigged,
    /// animated android body, so it matches the NFT art and still moves like a real character.</summary>
    public enum AvatarStyle { NeonRonin, Android, Vanguard, Michelle }

    public static class AvatarStyles
    {
        // enum names kept for saved settings and the network; the looks are the Casino FREE people
        public static readonly string[] Names = { "NEON RONIN · NFT", "HIGH ROLLER", "SHARP SUIT", "SHOWTIME" };
        public static readonly string[] Info =
        {
            "THE NFT LOOK · HELMET, VISOR, SUIT EMBLEM AND ARMOUR OF YOUR RARITY",
            "GOLD CHAIN, BRACELET AND A LOUD SHIRT",
            "BLACK SUIT, TIE AND SHADES",
            "TOP HAT AND VELVET SUIT",
        };
        public static int Count { get { return Names.Length; } }
        const string Key = "asc2_style";

        public static AvatarStyle Current
        {
            get { return (AvatarStyle)Mathf.Clamp(PlayerPrefs.GetInt(Key, 0), 0, Count - 1); }
            set { PlayerPrefs.SetInt(Key, (int)value); PlayerPrefs.Save(); if (Changed != null) Changed(value); }
        }
        public static event System.Action<AvatarStyle> Changed;

        /// <summary>Simulated guests: a mix of the Casino FREE people; a few wear their Neon Ronin NFT gear.
        /// Buckets 15-19 (NeonRonin without gear) are the plain gentleman, see PlainGuest.</summary>
        public static AvatarStyle ForGuest(string name)
        {
            int h = Mathf.Abs(name.GetHashCode()) % 20;
            if (h < 3) return AvatarStyle.NeonRonin;
            if (h < 8) return AvatarStyle.Android;
            if (h < 11) return AvatarStyle.Vanguard;
            if (h < 15) return AvatarStyle.Michelle;
            return AvatarStyle.NeonRonin;
        }
        public static bool PlainGuest(string name) { return Mathf.Abs(name.GetHashCode()) % 20 >= 15; }
    }

    /// <summary>Builds the Neon Ronin NFT gear as ONE rigidly skinned mesh bound to a humanoid's bones (one draw call),
    /// authored around the bones' bind-pose positions so it fits any Mixamo-proportioned body.</summary>
    public static class RoninGear
    {
        public static void Attach(Transform root, Animator anim, Rarity r)
        {
            if (anim == null || !anim.isHuman) return;
            System.Func<HumanBodyBones, Transform> B = anim.GetBoneTransform;
            var hips = B(HumanBodyBones.Hips); var head = B(HumanBodyBones.Head);
            if (hips == null || head == null) return;
            var chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine);
            var neck = B(HumanBodyBones.Neck) ?? head;
            var bones = new[]
            {
                hips, chest, head,
                B(HumanBodyBones.LeftUpperArm) ?? chest, B(HumanBodyBones.LeftLowerArm) ?? chest, B(HumanBodyBones.LeftHand) ?? chest,
                B(HumanBodyBones.RightUpperArm) ?? chest, B(HumanBodyBones.RightLowerArm) ?? chest, B(HumanBodyBones.RightHand) ?? chest,
                B(HumanBodyBones.LeftUpperLeg) ?? hips, B(HumanBodyBones.LeftLowerLeg) ?? hips, B(HumanBodyBones.RightUpperLeg) ?? hips, B(HumanBodyBones.RightLowerLeg) ?? hips,
            };
            System.Func<Transform, Vector3> P = t => root.InverseTransformPoint(t.position);
            Vector3 H = P(head), N = P(neck), C = P(chest), Hp = P(hips);
            Vector3 UL = P(bones[3]), UR = P(bones[6]), EL = P(bones[4]), ER = P(bones[7]), WL = P(bones[5]), WR = P(bones[8]);
            Vector3 KL = P(bones[10]), KR = P(bones[12]);
            float k = Mathf.Max(0.5f, (H.y - Hp.y) / 0.55f);   // body scale vs the 1.75 m Ronin proportions

            Color suit = Palette.Hex("17132A"), dark = Palette.Hex("0E0B19"), gold = Palette.Gold, mag = Palette.Magenta, teal = Palette.Teal;
            Color acc = r == Rarity.Epic ? mag : r == Rarity.Legendary ? gold : r == Rarity.Mythic ? Palette.Coral : teal;
            bool rarePlus = r >= Rarity.Rare, epicPlus = r >= Rarity.Epic, legPlus = r >= Rarity.Legendary, myth = r == Rarity.Mythic;
            Color trim = legPlus ? gold : acc;
            var m = new MeshKit();

            // ---------------- head: helmet, magenta outline, teal visor, mask, ear chips, suit emblem
            m.bone = RoninBuilder.Head;
            var hc = H + new Vector3(0, 0.105f * k, 0.012f * k);
            m.Sphere(hc, new Vector3(0.122f, 0.138f, 0.132f) * k, 20, suit);
            m.Torus(hc + new Vector3(0, 0.005f, 0.004f) * k, 0.133f * k, 0.009f * k, 28, 5, Quaternion.Euler(0, 0, 90), mag, 1f);
            m.Torus(hc + new Vector3(0, 0f, 0.022f) * k, 0.125f * k, 0.025f * k, 22, 6, Quaternion.Euler(0, -60, 0), teal, 1f, 120f);
            m.Box(hc + new Vector3(0, -0.078f, 0.1f) * k, new Vector3(0.112f, 0.062f, 0.042f) * k, dark);
            m.Bar(hc + new Vector3(-0.046f, -0.062f, 0.123f) * k, hc + new Vector3(0, -0.102f, 0.123f) * k, 0.006f * k, teal, 1f);
            m.Bar(hc + new Vector3(0.046f, -0.062f, 0.123f) * k, hc + new Vector3(0, -0.102f, 0.123f) * k, 0.006f * k, teal, 1f);
            for (int s = -1; s <= 1; s += 2)
                m.Cylinder(hc + new Vector3(0.124f * s, 0, -0.005f) * k, 0.036f * k, 0.036f * k, 0.02f * k, 12, Quaternion.Euler(0, 0, 90), mag, 1f);
            RoninBuilder.EmblemPublic(m, hc + new Vector3(0, 0.088f, 0.116f) * k, r, 0.034f * k, gold);
            if (myth)
            {
                m.Torus(hc + new Vector3(0, 0.125f, 0) * k, 0.072f * k, 0.012f * k, 20, 5, Quaternion.identity, gold, 1f);
                for (int i = 0; i < 5; i++)
                {
                    float a = i * 72f * Mathf.Deg2Rad;
                    m.Cylinder(hc + new Vector3(Mathf.Sin(a) * 0.067f, 0.16f, Mathf.Cos(a) * 0.067f) * k, 0.016f * k, 0.002f * k, 0.06f * k, 6, Quaternion.identity, gold, 1f, false);
                }
            }
            else if (legPlus) m.Box(hc + new Vector3(0, 0.135f, 0) * k, new Vector3(0.025f, 0.03f, 0.185f) * k, gold, 1f);

            // ---------------- chest: harness plate, neon V lines, emblem, gold collar
            m.bone = RoninBuilder.Spine;
            float zF = C.z + 0.1f * k;
            float chestY = Mathf.Lerp(C.y, N.y, 0.25f);
            m.Box(new Vector3(0, chestY, zF), new Vector3(0.24f, 0.2f, 0.035f) * k, dark, 0f);
            m.Tube(new Vector3(-0.15f * k, N.y - 0.03f * k, zF + 0.012f * k), new Vector3(0, chestY - 0.1f * k, zF + 0.025f * k), 0.008f * k, 6, mag, 1f);
            m.Tube(new Vector3(0.15f * k, N.y - 0.03f * k, zF + 0.012f * k), new Vector3(0, chestY - 0.1f * k, zF + 0.025f * k), 0.008f * k, 6, mag, 1f);
            m.Tube(new Vector3(-0.1f * k, chestY - 0.13f * k, zF), new Vector3(-0.15f * k, N.y - 0.06f * k, zF + 0.005f * k), 0.006f * k, 6, teal, 1f);
            m.Tube(new Vector3(0.1f * k, chestY - 0.13f * k, zF), new Vector3(0.15f * k, N.y - 0.06f * k, zF + 0.005f * k), 0.006f * k, 6, teal, 1f);
            RoninBuilder.EmblemPublic(m, new Vector3(0, chestY + 0.01f * k, zF + 0.03f * k), r, 0.05f * k, gold);
            m.Torus(new Vector3(0, N.y - 0.005f * k, N.z), 0.075f * k, 0.019f * k, 20, 6, Quaternion.identity, gold, 0.9f);
            if (legPlus)
            {
                m.Bar(new Vector3(-0.13f * k, chestY - 0.12f * k, zF + 0.01f * k), new Vector3(-0.17f * k, N.y - 0.05f * k, zF), 0.013f * k, gold, 1f);
                m.Bar(new Vector3(0.13f * k, chestY - 0.12f * k, zF + 0.01f * k), new Vector3(0.17f * k, N.y - 0.05f * k, zF), 0.013f * k, gold, 1f);
            }

            // ---------------- shoulders: gold casino chips (Epic+: armour plates)
            for (int s = -1; s <= 1; s += 2)
            {
                m.bone = s < 0 ? RoninBuilder.UArmL : RoninBuilder.UArmR;
                var sh = (s < 0 ? UL : UR) + new Vector3(0.03f * s, 0.045f, 0) * k;
                var rot = Quaternion.Euler(0, 0, 90);
                m.Cylinder(sh, 0.078f * k, 0.078f * k, 0.03f * k, 18, rot, rarePlus ? gold : Palette.Metal, 0.25f, true, gold, 0.6f);
                m.Torus(sh + new Vector3(0.016f * s, 0, 0) * k, 0.062f * k, 0.008f * k, 18, 5, rot, acc, 1f);
                if (epicPlus)
                {
                    m.Box(sh + new Vector3(-0.02f * s, 0.035f, 0) * k, new Vector3(0.14f, 0.03f, 0.17f) * k, Quaternion.Euler(0, 0, -15f * s), Palette.Hex("231633"));
                    m.Bar(sh + new Vector3(-0.08f * s, 0.06f, 0.09f) * k, sh + new Vector3(0.05f * s, 0.03f, 0.09f) * k, 0.012f * k, mag, 1f);
                }
                // forearm trims and Epic+ bracers
                m.bone = s < 0 ? RoninBuilder.FArmL : RoninBuilder.FArmR;
                var el = s < 0 ? EL : ER; var wr = s < 0 ? WL : WR;
                var along = Quaternion.FromToRotation(Vector3.up, (wr - el).normalized);
                m.Torus(Vector3.Lerp(el, wr, 0.85f), 0.045f * k, 0.01f * k, 16, 5, along, trim, 1f);
                if (epicPlus) m.Cylinder(Vector3.Lerp(el, wr, 0.5f), 0.048f * k, 0.042f * k, 0.12f * k, 10, along, Palette.Hex("231633"), 0f, true, acc, 0.6f);
            }

            // ---------------- hips: belt + buckle
            m.bone = RoninBuilder.Hips;
            m.Torus(Hp + new Vector3(0, 0.06f * k, 0.005f * k), 0.15f * k, 0.013f * k, 24, 6, Quaternion.identity, trim, 1f);
            m.Box(Hp + new Vector3(0, 0.06f, 0.15f) * k, new Vector3(0.07f, 0.05f, 0.02f) * k, gold, 0.8f);

            // ---------------- knees: guards (gold for Legendary+)
            for (int s = -1; s <= 1; s += 2)
            {
                m.bone = s < 0 ? RoninBuilder.ShinL : RoninBuilder.ShinR;
                var kn = s < 0 ? KL : KR;
                m.Box(kn + new Vector3(0, 0.0f, 0.065f) * k, new Vector3(0.085f, 0.085f, 0.04f) * k, legPlus ? gold : Palette.Metal, legPlus ? 0.5f : 0.05f);
                m.Bar(kn + new Vector3(-0.04f, -0.05f, 0.075f) * k, kn + new Vector3(0.04f, -0.05f, 0.075f) * k, 0.008f * k, acc, 1f);
            }

            var smr = m.SpawnSkinned("NeonRoninGear", root, bones, Palette.Neon);
            smr.quality = SkinQuality.Bone1;

            // ---------------- extras: aura, Mythic halo + orbiting chips
            if (rarePlus)
                Stick(Build.GlowSprite(root, new Vector3(0, chestY + 0.1f * k, C.z - 0.25f * k), legPlus ? 1.6f : 1f, legPlus ? gold : Palette.RarityColor(r), legPlus ? 0.45f : 0.25f), chest);
            if (myth)
            {
                var hk = new MeshKit();
                hk.Torus(Vector3.zero, 0.27f * k, 0.014f * k, 40, 6, Quaternion.Euler(90, 0, 0), gold, 1f);
                var halo = hk.Spawn("Halo", root, hc + new Vector3(0, 0.05f, -0.17f) * k, Quaternion.identity, Palette.Neon, false);
                Build.GlowSprite(halo.transform, Vector3.zero, 0.8f, gold, 0.5f);
                Stick(halo.transform, head);
                var orbit = Build.Empty("Orbit", root, new Vector3(0, chestY, C.z));
                Stick(orbit.transform, hips);
                orbit.AddComponent<Spin>().speed = new Vector3(0, 60, 0);
                for (int i = 0; i < 4; i++)
                {
                    float ang = i * 90f * Mathf.Deg2Rad;
                    var ck = new MeshKit();
                    ck.Cylinder(Vector3.zero, 0.035f, 0.035f, 0.008f, 14, Quaternion.Euler(90, 0, 0), i % 2 == 0 ? mag : teal, 0.8f, true, gold, 1f);
                    ck.Spawn("Chip", orbit.transform, new Vector3(Mathf.Sin(ang) * 0.42f, 0.1f * (i % 2), Mathf.Cos(ang) * 0.42f), Quaternion.identity, Palette.Neon, false);
                }
            }
        }

        static void Stick(Transform t, Transform bone) { if (t != null && bone != null) t.SetParent(bone, true); }
    }
}
