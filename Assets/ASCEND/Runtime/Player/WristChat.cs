using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>LIVE CHAT on the inside of the left forearm: turn your left arm outwards (palm up) AND look at the
    /// forearm at the same time and the chat opens — the floor feed, big wins, online players, plus quick-chat buttons
    /// you tap with the right index finger or ray. Desktop: press C.</summary>
    public class WristChat : MonoBehaviour
    {
        public float showDot = 0.6f, gazeAngle = 32f;
        RectTransform panel; CanvasGroup group; Transform anchor;
        TextMeshProUGUI logT;
        bool visible, desktopToggle; float vis, holdT;

        void Start()
        {
            var rig = Rig.I;
            if (rig == null || rig.LeftCtrl == null) { enabled = false; return; }
            anchor = new GameObject("ChatAnchor").transform;
            anchor.SetParent(rig.LeftCtrl, false);
            anchor.localPosition = new Vector3(0.05f, 0.0f, -0.15f);   // inner forearm (palm side) of the left arm
            anchor.localRotation = Quaternion.Euler(0f, -90f, 0f);
            panel = UIKit.Panel("WristChat", anchor, Vector3.zero, Quaternion.identity, new Vector2(640, 780), 0.19f, Palette.Magenta);
            group = panel.gameObject.AddComponent<CanvasGroup>();
            UIKit.Label(panel, "LIVE CHAT", 24, 10, 592, 60, 48, Palette.Text, Palette.Display);
            UIKit.Label(panel, "FLOOR FEED · BIG WINS · ONLINE PLAYERS", 24, 66, 592, 30, 20, Palette.Muted, Palette.Mono);
            UIKit.Box(panel, 18, 100, 604, 440, UIKit.CellBg);
            logT = UIKit.Label(panel, "", 30, 108, 580, 424, 24, Palette.Text, Palette.Body, TextAlignmentOptions.BottomLeft);
            for (int i = 0; i < ChatWall.Quick.Length; i++)
            {
                string q = ChatWall.Quick[i];
                UIKit.Btn(panel, q, 18 + (i % 2) * 306, 556 + (i / 2) * 72, 298, 64, Palette.Magenta, () => { ChatWall.PlayerSays(q); Sfx.UI("ui_click", 0.4f); }, 24);
            }
            panel.gameObject.SetActive(false);
            ChatWall.Updated += Redraw;
            Redraw();
        }

        void OnDestroy() { ChatWall.Updated -= Redraw; }

        void Redraw()
        {
            if (logT == null) return;
            ChatWall.Fill(logT, 11);
        }

        void LateUpdate()
        {
            var rig = Rig.I; if (rig == null || anchor == null) return;
            var head = rig.Head;
            Vector3 toHead = head.position - anchor.position;
            float dist = toHead.magnitude;
            float facing = Vector3.Dot(-anchor.forward, toHead / Mathf.Max(dist, 1e-4f));                   // forearm turned outwards, towards the face
            float gaze = Vector3.Angle(head.forward, anchor.position - head.position);                      // and you look at it
            bool gesture = facing > showDot && gaze < gazeAngle && dist < 0.8f;
            holdT = gesture ? holdT + Time.deltaTime : 0f;
            bool want = visible ? (facing > showDot - 0.2f && gaze < gazeAngle + 20f && dist < 0.85f) : holdT > 0.25f;
            if (!XRIn.HasHeadset)
            {
                if (XRIn.KeyDown("c")) desktopToggle = !desktopToggle;
                want = desktopToggle;
            }
            if (want != visible)
            {
                visible = want;
                if (visible) { panel.gameObject.SetActive(true); Redraw(); Sfx.UI("wrist_open", 0.5f); XRIn.Haptic(Hand.Left, 0.15f, 0.04f); }
            }
            vis = Mathf.MoveTowards(vis, visible ? 1f : 0f, Time.deltaTime * 6f);
            group.alpha = vis;
            group.interactable = group.blocksRaycasts = vis > 0.6f;
            panel.localScale = Vector3.one * (0.19f / 640f) * Mathf.Lerp(0.85f, 1f, vis);
            if (vis <= 0.001f && panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
            if (!XRIn.HasHeadset && visible)
            {
                anchor.position = head.position + head.forward * 0.45f - head.right * 0.12f - head.up * 0.05f;
                anchor.rotation = Quaternion.LookRotation(head.forward);
            }
        }
    }
}
