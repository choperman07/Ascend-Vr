using TMPro;
using UnityEngine;

namespace Ascend
{

    /// <summary>Point with your right index finger (grip closed, trigger released) — or aim the controller ray — at
    /// a player for a moment to open their player card next to them.</summary>
    public class PointInspector : MonoBehaviour
    {
        public float dwell = 0.35f;
        LineRenderer laser;
        AvatarProfile current; float hold;
        Transform card; TextMeshPro nameT, infoT; Renderer nftR; float showUntil;
        HandRig rightHand;
        Collider lastCol; AvatarProfile lastProfile;

        void Start()
        {
            var go = new GameObject("PointLaser");
            go.transform.SetParent(transform, false);
            laser = go.AddComponent<LineRenderer>();
            laser.sharedMaterial = Palette.FlatGlow;
            laser.widthMultiplier = 0.004f; laser.positionCount = 2; laser.useWorldSpace = true;
            laser.startColor = Palette.A(Palette.Teal, 0.9f); laser.endColor = Palette.A(Palette.Teal, 0f);
            laser.enabled = false;
            BuildCard();
        }

        void BuildCard()
        {
            card = new GameObject("ProfileCard").transform;
            card.SetParent(transform, false);
            const float W = 0.74f, Hh = 0.42f;
            var k = new MeshKit();
            k.Box(new Vector3(0, 0, 0.004f), new Vector3(W, Hh, 0.006f), Palette.Darker, 0.2f);
            k.Bar(new Vector3(-W / 2, Hh / 2, 0), new Vector3(W / 2, Hh / 2, 0), 0.008f, Palette.Teal, 1f);
            k.Bar(new Vector3(-W / 2, -Hh / 2, 0), new Vector3(W / 2, -Hh / 2, 0), 0.006f, Palette.Magenta, 1f);
            k.Bar(new Vector3(-0.115f, -Hh / 2 + 0.03f, -0.001f), new Vector3(-0.115f, Hh / 2 - 0.03f, -0.001f), 0.003f, Palette.Muted, 0.6f);
            k.Spawn("Back", card, Palette.Neon, false);
            var nft = Build.NftCard("NFT", card, new Vector3(-0.235f, 0, -0.004f), 0.2f, Rarity.Common);
            nftR = nft.GetComponent<Renderer>();
            // text lives strictly inside the right-hand column and shrinks to fit (never spills out of the card)
            nameT = Txt.Make("Name", card, new Vector3(0.12f, 0.135f, -0.005f), "", 0.05f, Palette.Text, Palette.Display, TextAlignmentOptions.Left, 0.43f);
            nameT.rectTransform.sizeDelta = new Vector2(0.43f, 0.085f);
            nameT.enableWordWrapping = false; nameT.enableAutoSizing = true; nameT.fontSizeMin = 0.22f; nameT.fontSizeMax = 0.5f;
            nameT.overflowMode = TextOverflowModes.Ellipsis;
            infoT = Txt.Make("Info", card, new Vector3(0.12f, -0.045f, -0.005f), "", 0.028f, Palette.Muted, Palette.Mono, TextAlignmentOptions.TopLeft, 0.43f);
            infoT.rectTransform.sizeDelta = new Vector2(0.43f, 0.25f);
            infoT.enableWordWrapping = true; infoT.enableAutoSizing = true; infoT.fontSizeMin = 0.14f; infoT.fontSizeMax = 0.28f;
            infoT.overflowMode = TextOverflowModes.Ellipsis; infoT.lineSpacing = -10f;
            card.gameObject.SetActive(false);
        }

        void Update()
        {
            var rig = Rig.I; if (rig == null) return;
            if (rightHand == null && Time.frameCount % 30 == 0) rightHand = rig.RightCtrl != null ? rig.RightCtrl.GetComponentInChildren<HandRig>() : null;   // retry twice a second, not every frame
            bool pointing = XRIn.Pointing(Hand.Right);
            Ray ray;
            if (pointing && rightHand != null && rightHand.IndexTip != null)
            {
                var tip = rightHand.IndexTip;
                var dir = (tip.position - rightHand.Palm.position).normalized;
                ray = new Ray(tip.position, dir);
            }
            else if (rig.RightCtrl != null) ray = new Ray(rig.RightCtrl.position, rig.RightCtrl.forward);
            else ray = new Ray(rig.Head.position, rig.Head.forward);

            AvatarProfile hit = null; RaycastHit h;
            if (Physics.Raycast(ray, out h, 25f, ~0, QueryTriggerInteraction.Ignore))
            {
                // the ray rests on the same collider for many frames: only walk the hierarchy when it changes
                if (h.collider != lastCol) { lastCol = h.collider; lastProfile = lastCol.GetComponentInParent<AvatarProfile>(); }
                hit = lastProfile;
            }
            laser.enabled = pointing;
            if (pointing)
            {
                laser.SetPosition(0, ray.origin);
                laser.SetPosition(1, ray.origin + ray.direction * (h.collider != null ? h.distance : 4f));
                laser.startColor = Palette.A(hit != null ? Palette.Gold : Palette.Teal, 0.9f);
            }
            if (hit != null && hit == current) hold += Time.deltaTime;
            else { current = hit; hold = 0; }
            if (current != null && hold >= dwell && (pointing || hold >= dwell * 2.5f)) Show(current);

            if (card.gameObject.activeSelf)
            {
                if (Time.time > showUntil || shown == null) { card.gameObject.SetActive(false); shown = null; }
                else
                {
                    var head = rig.Head.position;
                    var target = shown.transform.position + Vector3.up * 2.15f * shown.transform.lossyScale.y;
                    card.position = Vector3.Lerp(card.position, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
                    card.rotation = Quaternion.LookRotation(card.position - head);
                }
            }
        }

        AvatarProfile shown;
        void Show(AvatarProfile p)
        {
            showUntil = Time.time + 2.5f;
            if (shown == p && card.gameObject.activeSelf) return;
            shown = p;
            card.gameObject.SetActive(true);
            card.position = p.transform.position + Vector3.up * 2.1f;
            nftR.sharedMaterial = Palette.CardMat(p.rarity);
            var tier = p.TierNow;
            nameT.text = p.displayName;
            infoT.text = "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.TierColor(tier)) + ">" + GameState.TierNames[(int)tier] + "</color> · " +
                         "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.RarityColor(p.rarity)) + ">" + p.rarity.ToString().ToUpperInvariant() + " NFT</color>\n" +
                         p.xp.ToString("N0") + " XP · +" + Mathf.RoundToInt((GameState.BoostFor(p.rarity) - 1f) * 100) + "% BOOST\n" +
                         (string.IsNullOrEmpty(p.squad) ? "" : "SQUAD " + p.squad + "\n") +
                         (p.online ? "<color=#5CFFB0>● ONLINE PLAYER</color>" : p.status);
            Sfx.UI("ui_hover", 0.6f);
            XRIn.Haptic(Hand.Right, 0.2f, 0.04f);
        }
    }
}
