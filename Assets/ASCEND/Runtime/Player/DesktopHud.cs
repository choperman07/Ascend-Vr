using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ascend
{
    /// <summary>Screen HUD for desktop play (no headset): tier badge and today's mission card top-left, session
    /// clock top-centre, chips in big green with the session result under them top-right, a "last result" card,
    /// and key hints bottom-right. In VR it hides itself: there the balance lives on the wrist watch and nothing is
    /// glued to your face.</summary>
    public class DesktopHud : MonoBehaviour
    {
        Canvas canvas;
        RectTransform root;
        TextMeshProUGUI tierT, missionT, missionRewardT, missionSubT, floorT, clockT, chipsT, sessionT, lastT, nameT;
        RectTransform missionBar;
        float start;
        long lastChips = -1, lastDelta;

        void Start()
        {
            start = Time.time;
            var go = new GameObject("DesktopHUD", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var sc = go.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            // three 960-px columns pinned to the left, centre and right edges, so the layout holds on any aspect ratio
            root = Column((RectTransform)go.transform, 0f);
            var mid = Column((RectTransform)go.transform, 0.5f);
            var right = Column((RectTransform)go.transform, 1f);
            var midB = Column((RectTransform)go.transform, 0.5f, true);
            var rightB = Column((RectTransform)go.transform, 1f, true);

            // --- top-left: tier badge + today's mission card
            var badge = UIKit.Rect("Tier", root, 32, 28, 300, 56);
            var bi = badge.gameObject.AddComponent<Image>(); UISkin.Round(bi, 12); bi.color = new Color(0.08f, 0.06f, 0.02f, 0.62f); bi.raycastTarget = false;
            UISkin.Border(badge, Palette.Gold, 12);
            tierT = UIKit.Label(badge, "", 18, 0, 270, 56, 30, Palette.Gold, Palette.UI, TextAlignmentOptions.MidlineLeft);

            UIKit.Label(root, "DAILY MISSION", 32, 100, 400, 32, 26, Color.white, Palette.UI, TextAlignmentOptions.MidlineLeft);
            var card = UIKit.Rect("Mission", root, 32, 138, 420, 132);
            var ci = card.gameObject.AddComponent<Image>(); UISkin.Round(ci, 16); ci.color = UISkin.Card; ci.raycastTarget = false;
            UISkin.Border(card, UISkin.Line, 16);
            missionT = UIKit.Label(card, "", 22, 12, 290, 64, 28, Color.white, Palette.UI, TextAlignmentOptions.TopLeft);
            missionRewardT = UIKit.Label(card, "", 300, 12, 100, 32, 24, Palette.Gold, Palette.UI, TextAlignmentOptions.TopRight);
            missionSubT = UIKit.Label(card, "", 22, 74, 380, 24, 18, new Color(1, 1, 1, 0.8f), Palette.UI, TextAlignmentOptions.MidlineLeft);
            var track = UIKit.Rect("Track", card, 22, 102, 376, 16);
            UISkin.Border(track, UISkin.Line, 8);
            missionBar = UIKit.Box(track, 3, 3, 370, 10, Palette.Lime).rectTransform;
            missionBar.gameObject.AddComponent<LayoutElement>().preferredWidth = 370;
            floorT = UIKit.Label(root, "", 32, 288, 600, 32, 26, Color.white, Palette.UI, TextAlignmentOptions.MidlineLeft);

            // --- top-centre: session clock (reality check at a glance)
            UIKit.Label(mid, "SESSION TIME", 280, 22, 400, 30, 22, new Color(1, 1, 1, 0.75f), Palette.UI, TextAlignmentOptions.Center);
            clockT = UIKit.Label(mid, "0:00", 280, 50, 400, 64, 56, Color.white, Palette.UI, TextAlignmentOptions.Center);

            // --- top-right: chips + session result + last result card
            UIKit.Label(right, "PLAY CHIPS", 428, 22, 500, 30, 24, Color.white, Palette.UI, TextAlignmentOptions.MidlineRight);
            chipsT = UIKit.Label(right, "", 328, 48, 600, 84, 80, Palette.Money, Palette.UI, TextAlignmentOptions.MidlineRight);
            sessionT = UIKit.Label(right, "", 328, 128, 600, 40, 34, Palette.Loss, Palette.UI, TextAlignmentOptions.MidlineRight);
            var pc = UIKit.Rect("You", right, 508, 186, 420, 64);
            var pi = pc.gameObject.AddComponent<Image>(); UISkin.Round(pi, 14); pi.color = UISkin.Card; pi.raycastTarget = false;
            UISkin.Border(pc, UISkin.Line, 14);
            lastT = UIKit.Label(pc, "+0", 20, 0, 180, 64, 28, Palette.Money, Palette.UI, TextAlignmentOptions.MidlineLeft);
            nameT = UIKit.Label(pc, "", 160, 0, 240, 64, 28, Color.white, Palette.UI, TextAlignmentOptions.MidlineRight);

            // --- bottom-right: key hints · bottom-centre: the small print
            float y = 1080 - 56;
            foreach (var h in new[] { new[] { "CROUCH", "CTRL" }, new[] { "SIT / PLAY", "CLICK" }, new[] { "FLOORS", "1-9" }, new[] { "CHAT", "C" }, new[] { "MENU", "TAB" } })
            { UISkin.KeyHint(rightB, h[0], h[1], 928, y, 22); y -= 46; }
            UIKit.Label(midB, "ASCEND VR · 18+ · PLAY CHIPS ONLY · NEED HELP? JUGARBIEN.ES", 80, 1080 - 44, 800, 30, 18, new Color(1, 1, 1, 0.6f), Palette.UI, TextAlignmentOptions.Center);

            GameState.I.Changed += Refresh;
            Refresh();
        }

        void OnDestroy() { if (GameState.Exists) GameState.I.Changed -= Refresh; }

        static RectTransform Column(RectTransform parent, float ax, bool bottom = false)
        {
            var go = new GameObject("Col", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            if (bottom) { rt.anchorMin = rt.anchorMax = new Vector2(ax, 0); rt.pivot = new Vector2(ax, 0); rt.sizeDelta = new Vector2(960, 1080); }
            else { rt.anchorMin = new Vector2(ax, 0); rt.anchorMax = new Vector2(ax, 1); rt.pivot = new Vector2(ax, 1); rt.sizeDelta = new Vector2(960, 0); }
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        static string Short(long v)
        {
            long a = v < 0 ? -v : v;
            string s = a >= 10_000_000 ? (a / 1_000_000f).ToString("0.#") + "M" : a >= 100_000 ? (a / 1000f).ToString("0.#") + "K" : a.ToString("N0");
            return (v < 0 ? "-" : "") + s;
        }

        void Refresh()
        {
            if (chipsT == null) return;
            var gs = GameState.I;
            long before = lastChips;
            if (lastChips >= 0 && gs.chips != lastChips) lastDelta = gs.chips - lastChips;
            lastChips = gs.chips;
            // a gain rolls the counter up and pops it; anything else just updates quietly
            if (before >= 0 && gs.chips > before && isActiveAndEnabled) { if (roll != null) StopCoroutine(roll); roll = StartCoroutine(Roll(before, gs.chips)); }
            else if (roll == null) chipsT.text = Short(gs.chips);
            sessionT.text = "SESSION " + (gs.sessionNet >= 0 ? "+" : "") + Short(gs.sessionNet);
            sessionT.color = gs.sessionNet >= 0 ? Palette.Money : Palette.Loss;
            lastT.text = (lastDelta >= 0 ? "+" : "") + Short(lastDelta);
            lastT.color = lastDelta >= 0 ? Palette.Money : Palette.Loss;
            nameT.text = gs.playerName;
            var t = gs.CurrentTier;
            tierT.text = GameState.TierNames[(int)t] + "  ·  " + Short(gs.xp) + " XP";
            Mission m = null;
            foreach (var x in gs.missions) if (!x.done) { m = x; break; }
            if (m == null && gs.missions.Count > 0) m = gs.missions[0];
            if (m != null)
            {
                missionT.text = m.title;
                missionRewardT.text = "+" + m.rewardXP + " XP";
                missionSubT.text = m.done ? "COMPLETED · NEW ONES TOMORROW" : m.progress + " / " + m.target;
                UIKit.SetBar(missionBar, m.target > 0 ? m.progress / (float)m.target : 1f);
            }
            else { missionT.text = "NO MISSIONS"; missionRewardT.text = ""; missionSubT.text = ""; UIKit.SetBar(missionBar, 0); }
        }

        Coroutine roll;
        System.Collections.IEnumerator Roll(long from, long to)
        {
            float dur = Mathf.Clamp(0.5f + Mathf.Log10(Mathf.Max(10f, to - from)) * 0.25f, 0.6f, 1.6f);
            var rt = chipsT.rectTransform; var card = lastT.transform.parent;
            for (float e = 0; e < dur; e += Time.unscaledDeltaTime)
            {
                float k = 1f - Mathf.Pow(1f - e / dur, 3f);
                chipsT.text = Short(from + (long)((to - from) * k));
                float pop = 1f + 0.22f * Mathf.Sin(Mathf.Clamp01(e / 0.35f) * Mathf.PI);
                rt.localScale = Vector3.one * pop;
                card.localScale = Vector3.one * (1f + 0.1f * Mathf.Sin(Mathf.Clamp01(e / 0.3f) * Mathf.PI));
                chipsT.color = Color.Lerp(Color.white, Palette.Money, Mathf.Clamp01(e / dur));
                yield return null;
            }
            chipsT.text = Short(to); rt.localScale = Vector3.one; card.localScale = Vector3.one; chipsT.color = Palette.Money;
            roll = null;
        }

        void Update()
        {
            if (canvas == null) return;
            bool show = !XRIn.HasHeadset && !IntroCinematic.Playing;
            if (canvas.enabled != show) canvas.enabled = show;
            if (!show) return;
            int s = Mathf.FloorToInt(Time.time - start);
            clockT.text = (s / 3600 > 0 ? (s / 3600) + ":" + (s / 60 % 60).ToString("00") : (s / 60).ToString()) + ":" + (s % 60).ToString("00");
            var fm = FloorManager.I;
            if (Run.Active && RunDirector.I != null && !RunDirector.I.InCasino) { if (floorT.text != "") floorT.text = ""; }
            else if (fm != null && fm.Current >= 0 && fm.Current < Tower.Count)
            {
                string f = (fm.Current == Tower.Count - 1 ? "ROOF" : "FLOOR " + (fm.Current + 1)) + "  ·  " + Tower.Floors[fm.Current].name;
                if (floorT.text != f) floorT.text = f;
            }
        }
    }
}
