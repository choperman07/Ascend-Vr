using System.Collections;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Crate Vault: crates are EARNED (daily, missions, battles, level-ups) — never bought. Tap the crate to open:
    /// it reveals an NFT card of a rarity rolled from the published odds (better tiers boost the odds).</summary>
    public class CrateVault : MonoBehaviour
    {
        Transform crate, lidT, cardSlot;
        TextMeshPro countT, oddsT, rewardT;
        PokeButton openBtn, wearBtn;
        AvatarBody host;
        bool opening;
        Rarity last = Rarity.None;
        GameObject card;

        void Start()
        {
            var k = new MeshKit();
            k.Cylinder(new Vector3(0, 0.45f, 0), 0.55f, 0.6f, 0.9f, 36, Quaternion.identity, Palette.Hex("140F20"), 0.02f, true, Palette.Hex("1B1328"), 0.05f);
            k.Torus(new Vector3(0, 0.905f, 0), 0.56f, 0.015f, 48, 6, Quaternion.identity, Palette.Gold, 1f);
            k.Torus(new Vector3(0, 0.05f, 0), 0.6f, 0.02f, 48, 6, Quaternion.identity, Palette.Gold, 0.6f);
            k.Box(new Vector3(0, 1.9f, 1.1f), new Vector3(2.2f, 1.6f, 0.08f), Palette.Hex("0A0812"), 0.02f);
            k.Spawn("VaultBase", transform, Palette.Glossy, false);
            Build.Beam(transform, new Vector3(0, 3.6f, 0), Quaternion.Euler(90, 0, 0), 2.7f, 0.5f, Palette.Gold, 0.15f);
            Txt.Glow("Title", transform, new Vector3(0, 2.55f, 1.05f), "CRATE VAULT", 0.2f, Palette.Gold);
            Txt.Make("Sub", transform, new Vector3(0, 2.35f, 1.05f), "CRATES ARE EARNED, NEVER BOUGHT", 0.05f, Palette.Muted, Palette.Mono);
            oddsT = Txt.Make("Odds", transform, new Vector3(0, 1.85f, 1.05f), "", 0.055f, Palette.Text, Palette.Mono, TextAlignmentOptions.Center, 2f);
            countT = Txt.Make("Count", transform, new Vector3(0, 1.35f, 1.05f), "", 0.09f, Palette.Gold, Palette.Display);
            rewardT = Txt.Make("Reward", transform, new Vector3(0, 1.6f, -0.2f), "", 0.06f, Palette.Text, Palette.Display);
            rewardT.gameObject.AddComponent<Billboard>();
            cardSlot = Build.Empty("CardSlot", transform, new Vector3(0, 1.45f, 0)).transform;
            BuildCrate();
            openBtn = PokeButton.Create("Open", transform, new Vector3(-0.2f, 0.94f, -0.42f), Quaternion.identity, 0.05f, Palette.Gold, "OPEN", () => StartCoroutine(Open()));
            wearBtn = PokeButton.Create("Wear", transform, new Vector3(0.22f, 0.94f, -0.42f), Quaternion.identity, 0.04f, Palette.Teal, "WEAR", Wear);
            wearBtn.SetInteractable(false);
            host = Avatars.Create("VaultHost", transform, new Vector3(-0.95f, 0, 0.35f), 200f, Rarity.Mythic, RoninRole.Host);
            var prof = host.gameObject.AddComponent<AvatarProfile>(); prof.displayName = "VAULT KEEPER"; prof.rarity = Rarity.Mythic; prof.xp = 400000; prof.status = "STAFF · CRATE VAULT";
            if (Rig.I != null) host.lookAtTarget = Rig.I.Head;
            GameState.I.Changed += Refresh;
            Refresh();
        }
        void OnDestroy() { if (GameState.Exists) GameState.I.Changed -= Refresh; }

        void BuildCrate()
        {
            var k = new MeshKit();
            k.Box(Vector3.zero, new Vector3(0.42f, 0.3f, 0.32f), Palette.Hex("221838"), 0.1f);
            for (int s = -1; s <= 1; s += 2) { k.Box(new Vector3(s * 0.2f, 0, 0), new Vector3(0.03f, 0.31f, 0.33f), Palette.Gold, 0.9f); }
            k.Box(new Vector3(0, 0, -0.165f), new Vector3(0.1f, 0.1f, 0.01f), Palette.Gold, 1f);
            crate = k.Spawn("Crate", transform, new Vector3(0, 1.06f, 0), Quaternion.identity, Palette.Neon, false).transform;
            var lk = new MeshKit(); lk.Box(new Vector3(0, 0.03f, 0.16f), new Vector3(0.44f, 0.06f, 0.34f), Palette.Hex("2C2048"), 0.15f); lk.Box(new Vector3(0, 0.03f, 0.16f), new Vector3(0.45f, 0.065f, 0.04f), Palette.Gold, 0.9f);
            lidT = lk.Spawn("Lid", crate, new Vector3(0, 0.15f, -0.16f), Quaternion.identity, Palette.Neon, false).transform;
            crate.gameObject.AddComponent<Bob>();
            Build.GlowSprite(crate, Vector3.zero, 0.9f, Palette.Gold, 0.4f);
        }

        void Refresh()
        {
            var gs = GameState.I;
            var o = Odds.Crate(gs.CurrentTier);
            oddsT.text = "COMMON · RARE · EPIC · LEGENDARY · MYTHIC\nHIGHER TIERS UNLOCK BETTER CRATES";
            countT.text = gs.CratesReady > 0 ? gs.CratesReady + " CRATE" + (gs.CratesReady > 1 ? "S" : "") + " READY" : "NEXT FREE CRATE TOMORROW\nOR COMPLETE A MISSION";
            if (openBtn != null && !opening) openBtn.SetInteractable(gs.CratesReady > 0);
        }

        IEnumerator Open()
        {
            if (opening) yield break;
            if (!GameState.I.TakeCrate()) { Sfx.UI("ui_error"); yield break; }
            opening = true; openBtn.SetInteractable(false); wearBtn.SetInteractable(false);
            if (card != null) Destroy(card);
            rewardT.text = "";
            Voice.Say("host_crate", host.HeadBone, 0.55f);
            var r = Odds.RollRarity(Odds.Crate(GameState.I.CurrentTier));
            for (int i = 0; i < 8; i++)
            {
                Sfx.Play("box_rattle", crate.position, 0.6f, 0.9f + i * 0.05f);
                StartCoroutine(Fx.Shake(crate, 0.01f + i * 0.002f, 0.1f));
                XRIn.HapticBoth(0.15f + i * 0.06f, 0.03f);
                yield return new WaitForSeconds(0.11f);
            }
            Sfx.Play("crate_open", crate.position, 1f);
            float t = 0;
            while (t < 0.35f) { t += Time.deltaTime; lidT.localRotation = Quaternion.Euler(-110f * Fx.EaseOutBack(t / 0.35f), 0, 0); yield return null; }
            var col = Palette.RarityColor(r);
            Juice.Sparks(crate.position + Vector3.up * 0.2f, col, 20 + (int)r * 15);
            card = Build.NftCard("Pulled", cardSlot, Vector3.zero, 0.3f, r);
            card.transform.position = crate.position;
            Build.GlowSprite(card.transform, new Vector3(0, 0, 0.02f), 0.8f, col, 1f);
            card.AddComponent<Spin>();
            yield return StartCoroutine(Fx.Move(card.transform, Vector3.zero, 0.6f));
            string key = r.ToString().ToLowerInvariant();
            Sfx.Play("reveal_" + key, cardSlot.position, 1f);
            Voice.Say("host_" + key, host.HeadBone, 0.55f);
            long chips = Odds.CrateChips(r);
            GameState.I.AddNft(r);
            GameState.I.Grant(chips, r.ToString().ToUpperInvariant() + " CRATE");
            GameState.I.Record("crate");
            rewardT.text = r.ToString().ToUpperInvariant() + " NFT\n" + Odds.CrateReward(r);
            rewardT.color = col;
            if (r >= Rarity.Legendary) { host.Play(Emote.Cheer, 2f); Juice.Celebrate(cardSlot.position, chips, r == Rarity.Mythic ? 100f : 25f); }
            if (r == Rarity.Mythic) GameState.I.Unlock("ONE OF ONE", "PULLED A MYTHIC FROM A CRATE", 5000);
            last = r;
            wearBtn.SetInteractable(GameState.I.nft != r);
            yield return new WaitForSeconds(0.8f);
            t = 0;
            while (t < 0.3f) { t += Time.deltaTime; lidT.localRotation = Quaternion.Euler(-110f * (1f - t / 0.3f), 0, 0); yield return null; }
            opening = false;
            Refresh();
        }

        void Wear()
        {
            if (last == Rarity.None) return;
            GameState.I.SetNft(last);
            Sfx.UI("shield_up", 0.8f);
            wearBtn.SetInteractable(false);
            rewardT.text = "NOW WEARING " + last.ToString().ToUpperInvariant();
        }
    }
}
