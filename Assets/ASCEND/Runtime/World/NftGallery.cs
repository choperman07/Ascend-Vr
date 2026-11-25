using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>ASCEND RONIN COLLECTION: a lit museum wall with the five rarities. Each bay has the framed NFT card,
    /// a life-size Neon Ronin of that rarity turning slowly on a pedestal under a spotlight, a brass nameplate with what
    /// it unlocks, and velvet ropes in front. Point at a statue for its card.</summary>
    public class NftGallery : MonoBehaviour
    {
        static readonly string[] Perks =
        {
            "LOBBY ACCESS · +0% XP",
            "+5% XP · FAST ROUNDS",
            "+10% XP · LUCK & LOOT",
            "+20% XP · SKY LOUNGE",
            "+35% XP · INNER CIRCLE",
        };
        static readonly string[] Suits = { "CLUB", "DIAMOND", "HEART", "SPADE", "CROWN" };
        readonly Transform[] turn = new Transform[5];

        void Start()
        {
            const float Gap = 1.3f, W = 7.0f, Wh = 3.3f;
            var k = new MeshKit();
            // backdrop wall with gold frame and LED strips, slight inward curve via angled side panels
            k.Box(new Vector3(0, Wh * 0.5f, 1.25f), new Vector3(W - 1.4f, Wh, 0.12f), Palette.Hex("0E0A16"), 0.02f);
            for (int s = -1; s <= 1; s += 2)
                k.Box(new Vector3(s * (W * 0.5f - 0.45f), Wh * 0.5f, 0.95f), new Vector3(0.12f, Wh, 0.9f), Quaternion.Euler(0, s * 35f, 0), Palette.Hex("0E0A16"), 0.02f);
            k.Bar(new Vector3(-W * 0.5f + 0.7f, Wh, 1.18f), new Vector3(W * 0.5f - 0.7f, Wh, 1.18f), 0.04f, Palette.Gold, 1f);
            k.Bar(new Vector3(-W * 0.5f + 0.7f, 0.06f, 1.18f), new Vector3(W * 0.5f - 0.7f, 0.06f, 1.18f), 0.03f, Palette.Magenta, 1.2f);
            k.Bar(new Vector3(-W * 0.5f + 0.7f, 2.72f, 1.18f), new Vector3(W * 0.5f - 0.7f, 2.72f, 1.18f), 0.015f, Palette.Teal, 1.2f);
            // floor plinth band
            k.Box(new Vector3(0, 0.03f, 0.3f), new Vector3(W - 1.2f, 0.06f, 1.8f), Palette.Hex("15101F"), 0.02f);
            k.Spawn("GalleryWall", transform, Palette.Lacquer, false);
            Build.BoxCol(gameObject, new Vector3(0, Wh * 0.5f, 1.25f), new Vector3(W - 1.4f, Wh, 0.2f));
            for (int i = 0; i < 5; i++)
            {
                var r = (Rarity)(i + 1);
                var rc = Palette.RarityColor(r);
                float x = (i - 2) * Gap;
                // pedestal
                var pk = new MeshKit();
                pk.Cylinder(new Vector3(x, 0.14f, 0.25f), 0.42f, 0.46f, 0.28f, 40, Quaternion.identity, Palette.Hex("140F20"), 0.02f, true, Palette.Hex("1C1529"), 0.04f);
                pk.Torus(new Vector3(x, 0.285f, 0.25f), 0.43f, 0.014f, 40, 5, Quaternion.identity, rc, 1.4f);
                pk.Torus(new Vector3(x, 0.02f, 0.25f), 0.47f, 0.01f, 40, 4, Quaternion.identity, Palette.Gold, 1f);
                pk.Spawn("Pedestal" + i, transform, Palette.Glossy, false);
                // framed card on the wall
                var fk = new MeshKit();
                fk.Box(new Vector3(x, 2.05f, 1.17f), new Vector3(0.7f, 0.94f, 0.04f), Palette.Gold, 0.25f);
                fk.Box(new Vector3(x, 2.05f, 1.15f), new Vector3(0.64f, 0.88f, 0.03f), Palette.Hex("0A0710"), 0.0f);
                fk.Spawn("Frame" + i, transform, Palette.Gold3, false);
                var card = Build.NftCard("Card" + i, transform, new Vector3(x, 2.05f, 1.125f), 0.58f, r);
                card.AddComponent<BoxCollider>().size = new Vector3(0.58f, 0.82f, 0.02f);
                var cap = card.AddComponent<AvatarProfile>(); cap.displayName = "ASCEND RONIN · " + r.ToString().ToUpperInvariant(); cap.rarity = r; cap.xp = GameState.TierXP[i]; cap.status = "NFT · " + Perks[i];
                Build.GlowSprite(transform, new Vector3(x, 2.05f, 1.1f), 1.4f, rc, 0.22f, false);
                // brass nameplate
                var nk = new MeshKit(); nk.Box(new Vector3(x, 1.38f, 1.16f), new Vector3(0.62f, 0.2f, 0.03f), Palette.Hex("2A1E0E"), 0.1f);
                nk.Spawn("Plate" + i, transform, Palette.Gold3, false);
                var nm = Txt.Glow("Name" + i, transform, new Vector3(x, 1.42f, 1.13f), r.ToString().ToUpperInvariant(), 0.085f, rc);
                var pt = Txt.Make("Perk" + i, transform, new Vector3(x, 1.33f, 1.13f), Suits[i] + " · " + Perks[i], 0.032f, Palette.Text, Palette.Display, TextAlignmentOptions.Center, 0.6f);
                // the statue: a life-size Neon Ronin of that rarity, turning slowly
                turn[i] = Build.Empty("Turn" + i, transform, new Vector3(x, 0.29f, 0.25f)).transform;
                var spin = turn[i].gameObject.AddComponent<Spin>(); spin.speed = new Vector3(0, 14f + i * 2f, 0);
                var statue = Avatars.CreateStyled("Ronin" + r, turn[i], Vector3.zero, 180f, r, AvatarStyle.NeonRonin, 1.62f);
                statue.pose = Pose.Idle;
                var ap = statue.gameObject.AddComponent<AvatarProfile>(); ap.displayName = "ASCEND RONIN · " + r.ToString().ToUpperInvariant(); ap.rarity = r; ap.xp = GameState.TierXP[i]; ap.status = "NFT · " + Perks[i];
                // spotlight from above
                Build.Beam(transform, new Vector3(x, 3.25f, 0.25f), Quaternion.Euler(180, 0, 0), 3.0f, 0.55f, rc, 0.12f);
                var l = new GameObject("Spot" + i).AddComponent<Light>();
                l.transform.SetParent(transform, false); l.transform.localPosition = new Vector3(x, 2.9f, -0.2f);
                l.type = LightType.Point; l.range = 2.6f; l.intensity = 1.1f; l.color = Color.Lerp(Color.white, rc, 0.35f); l.shadows = LightShadows.None;
            }
            // velvet ropes in front
            var rk = new MeshKit();
            for (int i = 0; i <= 5; i++)
            {
                float x = (i - 2.5f) * Gap;
                rk.Cylinder(new Vector3(x, 0.45f, -0.55f), 0.025f, 0.025f, 0.9f, 12, Quaternion.identity, Palette.Gold, 0.4f);
                rk.Sphere(new Vector3(x, 0.92f, -0.55f), 0.045f, 12, Palette.Gold, 0.5f);
                rk.Cylinder(new Vector3(x, 0.02f, -0.55f), 0.12f, 0.12f, 0.03f, 16, Quaternion.identity, Palette.Gold, 0.3f);
                if (i < 5)
                    for (int s = 0; s < 8; s++)
                    {
                        float a = s / 8f, b = (s + 1) / 8f;
                        float ya = 0.86f - Mathf.Sin(a * Mathf.PI) * 0.12f, yb = 0.86f - Mathf.Sin(b * Mathf.PI) * 0.12f;
                        rk.Bar(new Vector3(x + a * Gap, ya, -0.55f), new Vector3(x + b * Gap, yb, -0.55f), 0.018f, Palette.Hex("8C1036"), 0.15f);
                    }
            }
            rk.Spawn("Ropes", transform, Palette.Glossy, false);
            var tt = Txt.Glow("Title", transform, new Vector3(0, 3.05f, 1.12f), "ASCEND RONIN COLLECTION", 0.24f, Palette.Gold);
            var st = Txt.Make("Sub", transform, new Vector3(0, 2.82f, 1.12f), "FIVE RARITIES · YOUR NFT IS YOUR AVATAR · WEAR YOURS AT THE AVATAR STUDIO", 0.05f, Palette.Muted, Palette.Mono);
        }
    }
}
