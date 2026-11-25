using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>AVATAR STUDIO (lobby): a turntable shows how other players see you. Pick your look — NEON RONIN (the NFT
    /// collection's look, with the armour of your NFT's rarity), HIGH ROLLER, SHARP SUIT or SHOWTIME — and which of your owned
    /// NFTs you wear. The choice is saved and sent to everyone online.</summary>
    public class AvatarStudio : MonoBehaviour
    {
        Transform turntable;
        AvatarBody preview;
        TextMeshPro styleT, infoT, nftT;
        AvatarStyle shownStyle = (AvatarStyle)(-1); Rarity shownRarity = Rarity.None;
        float rebuildAt;

        void Start()
        {
            var k = new MeshKit();
            k.Cylinder(new Vector3(0, 0.09f, 0.4f), 0.85f, 0.95f, 0.18f, 56, Quaternion.identity, Palette.Hex("15101F"), 0.02f, true, Palette.Hex("1C1529"), 0.05f);
            k.Torus(new Vector3(0, 0.18f, 0.4f), 0.86f, 0.02f, 56, 4, Quaternion.identity, Palette.Teal, 1.2f);
            k.Ring(new Vector3(0, 0.185f, 0.4f), 0.55f, 0.6f, 48, Quaternion.identity, Palette.Magenta, 1f);
            // console in front
            k.Box(new Vector3(0, 0.45f, -0.75f), new Vector3(1.5f, 0.9f, 0.4f), Palette.Hex("140F20"), 0.03f);
            k.Box(new Vector3(0, 0.92f, -0.75f), new Vector3(1.56f, 0.04f, 0.46f), Palette.Hex("1E1530"), 0.05f);
            k.Bar(new Vector3(-0.78f, 0.945f, -0.98f), new Vector3(0.78f, 0.945f, -0.98f), 0.01f, Palette.Teal, 1f);
            // backdrop arch
            k.Torus(new Vector3(0, 1.3f, 1.3f), 1.25f, 0.035f, 48, 5, Quaternion.Euler(90, 0, 0), Palette.Magenta, 1.2f, 180f);
            k.Box(new Vector3(0, 1.3f, 1.36f), new Vector3(2.7f, 2.6f, 0.06f), Palette.Hex("0F0B18"), 0.02f);
            k.Spawn("Studio", transform, Palette.Glossy, false);
            Build.BoxCol(gameObject, new Vector3(0, 0.45f, -0.75f), new Vector3(1.56f, 0.9f, 0.46f));
            Build.BoxCol(gameObject, new Vector3(0, 0.6f, 0.4f), new Vector3(1.8f, 1.2f, 1.8f));
            Build.BoxCol(gameObject, new Vector3(0, 1.3f, 1.36f), new Vector3(2.7f, 2.6f, 0.1f));
            Build.Beam(transform, new Vector3(0, 3.2f, 0.4f), Quaternion.Euler(180, 0, 0), 3.0f, 0.9f, Palette.Teal, 0.18f);
            Build.GlowSprite(transform, new Vector3(0, 1.1f, 0.9f), 2.6f, Palette.Magenta, 0.25f, false);

            Txt.Glow("Title", transform, new Vector3(0, 2.85f, 1.3f), "AVATAR STUDIO", 0.24f, Palette.Text);
            Txt.Make("Sub", transform, new Vector3(0, 2.6f, 1.3f), "THIS IS HOW OTHER PLAYERS SEE YOU", 0.06f, Palette.Muted, Palette.Mono);
            styleT = Txt.Glow("Style", transform, new Vector3(0, 1.35f, -0.98f), "", 0.08f, Palette.Teal);
            styleT.transform.localRotation = Quaternion.Euler(20, 0, 0);
            infoT = Txt.Make("Info", transform, new Vector3(0, 1.22f, -0.98f), "", 0.035f, Palette.Text, Palette.Mono, TextAlignmentOptions.Center, 1.5f);
            infoT.transform.localRotation = Quaternion.Euler(20, 0, 0);
            nftT = Txt.Make("Nft", transform, new Vector3(0, 0.95f, -1.0f), "", 0.04f, Palette.Gold, Palette.Display);
            nftT.transform.localRotation = Quaternion.Euler(70, 0, 0);

            // style buttons (top row) and NFT picker (front)
            for (int i = 0; i < AvatarStyles.Count; i++)
            {
                int idx = i;
                var label = AvatarStyles.Names[i].Replace(" · NFT", "\nNFT");
                PokeButton.Create("Style" + i, transform, new Vector3(-0.54f + i * 0.36f, 0.96f, -0.68f), Quaternion.identity, 0.065f,
                    i == 0 ? Palette.Magenta : Palette.Teal, label, () => Pick((AvatarStyle)idx), 0.022f);
            }
            PokeButton.Create("NftPrev", transform, new Vector3(-0.55f, 0.96f, -0.88f), Quaternion.identity, 0.04f, Palette.Gold, "<", () => CycleNft(-1), 0.04f);
            PokeButton.Create("NftNext", transform, new Vector3(0.55f, 0.96f, -0.88f), Quaternion.identity, 0.04f, Palette.Gold, ">", () => CycleNft(1), 0.04f);

            turntable = Build.Empty("Turntable", transform, new Vector3(0, 0.18f, 0.4f)).transform;
            turntable.gameObject.AddComponent<Spin>().speed = new Vector3(0, 22f, 0);
            AvatarStyles.Changed += OnStyle;
            if (GameState.I != null) GameState.I.Changed += OnState;
            Refresh(true);
        }

        void OnDestroy()
        {
            AvatarStyles.Changed -= OnStyle;
            if (GameState.I != null) GameState.I.Changed -= OnState;
        }

        void OnStyle(AvatarStyle s) { Refresh(false); }
        void OnState() { if (GameState.I != null && GameState.I.nft != shownRarity) Refresh(false); }

        void Pick(AvatarStyle s)
        {
            if (s == AvatarStyles.Current) { Sfx.UI("ui_click", 0.4f); return; }
            AvatarStyles.Current = s;
            Sfx.UI("reveal_rare", 0.6f);
            GameState.I.Notify("AVATAR LOOK · " + AvatarStyles.Names[(int)s], Palette.Teal);
        }

        void CycleNft(int dir)
        {
            var gs = GameState.I; if (gs == null) return;
            int cur = (int)gs.nft;
            for (int step = 1; step <= 5; step++)
            {
                int n = cur + dir * step;
                n = ((n - 1) % 5 + 5) % 5 + 1;   // 1..5
                if (gs.Owns((Rarity)n) || gs.demoUnlockAll) { gs.SetNft((Rarity)n); Sfx.UI("card_flip", 0.6f); return; }
            }
            GameState.I.Notify("OPEN CRATES OR WIN CASE BATTLES FOR MORE NFTs", Palette.Gold);
            Sfx.UI("ui_error", 0.5f);
        }

        void Refresh(bool now)
        {
            var gs = GameState.I;
            var style = AvatarStyles.Current;
            var r = gs != null ? gs.nft : Rarity.Common;
            styleT.text = AvatarStyles.Names[(int)style];
            infoT.text = AvatarStyles.Info[(int)style];
            nftT.text = "<  NFT · " + r.ToString().ToUpperInvariant() + "  >";
            nftT.color = Palette.RarityColor(r);
            if (style == shownStyle && r == shownRarity && preview != null) return;
            rebuildAt = now ? 0f : Time.time + 0.05f;
        }

        void Update()
        {
            if (rebuildAt < 0f || Time.time < rebuildAt) return;
            rebuildAt = -1f;
            var gs = GameState.I;
            shownStyle = AvatarStyles.Current; shownRarity = gs != null ? gs.nft : Rarity.Common;
            if (preview != null) Destroy(preview.gameObject);
            preview = Avatars.CreateStyled("YourAvatar", turntable, Vector3.zero, 0f, shownRarity, shownStyle, 1.78f);
            preview.pose = Pose.Idle;
            preview.Play(Emote.Wave, 1.6f);
            var ap = preview.gameObject.AddComponent<AvatarProfile>();
            ap.displayName = gs != null ? gs.playerName : "YOU"; ap.rarity = shownRarity; ap.xp = gs != null ? gs.xp : 0;
            ap.status = "YOUR AVATAR · " + AvatarStyles.Names[(int)shownStyle];
            Juice.Sparks(turntable.position + Vector3.up * 1f, Palette.RarityColor(shownRarity), 30);
        }
    }
}
