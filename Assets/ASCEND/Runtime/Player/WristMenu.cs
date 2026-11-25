using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ascend
{
    /// <summary>Teleport menu on the right forearm: turn your wrist towards your face (like checking a watch) and it
    /// appears. Tap a floor or a spot with your left index finger (or the left ray).</summary>
    public class WristMenu : MonoBehaviour
    {
        public float showDot = 0.55f;
        public Vector3 offset = new Vector3(0.045f, 0.0f, -0.14f);
        public Vector3 euler = new Vector3(0f, -90f, 0f);
        RectTransform panel;
        CanvasGroup group;
        Transform anchor;
        TextMeshProUGUI header, chipsT, pageT, clockT, infoT;
        RectTransform bar;
        float nextInfo;
        RectTransform spotsArea;
        readonly List<Button> floorBtns = new List<Button>();
        bool visible;
        float vis;
        int page;   // -1 = floors, otherwise floor index for spots

        void Start()
        {
            var rig = Rig.I;
            if (rig == null || rig.RightCtrl == null) { enabled = false; return; }
            anchor = new GameObject("WristAnchor").transform;
            anchor.SetParent(rig.RightCtrl, false);
            // on top of the forearm, a little behind the wrist; the panel faces the back of the hand (outwards)
            anchor.localPosition = offset;
            anchor.localRotation = Quaternion.Euler(euler);
            panel = UIKit.Panel("WristPanel", anchor, Vector3.zero, Quaternion.identity, new Vector2(600, 860), 0.17f, Palette.Teal);
            group = panel.gameObject.AddComponent<CanvasGroup>();
            // header: brand + watch time
            UIKit.Box(panel, 16, 14, 8, 44, Palette.Magenta);
            header = UIKit.Label(panel, "ASCEND", 36, 8, 330, 56, 46, Palette.Text, Palette.Display);
            clockT = UIKit.Label(panel, "", 330, 12, 250, 48, 38, Palette.Teal, Palette.Mono, TextAlignmentOptions.MidlineRight);
            // status card: the day and the quota in mafia mode, tier and chips in the free casino
            UIKit.Box(panel, 16, 72, 568, 150, new Color(1f, 1f, 1f, 0.06f));
            UIKit.Frame(panel, 16, 72, 568, 150, new Color(1f, 1f, 1f, 0.12f), 2);
            infoT = UIKit.Label(panel, "", 34, 80, 532, 44, 30, Palette.Text, Palette.Display);
            chipsT = UIKit.Label(panel, "", 34, 124, 532, 40, 26, Palette.Gold, Palette.Mono);
            bar = UIKit.Bar(panel, 34, 176, 532, 24, Palette.Gold);
            pageT = UIKit.Label(panel, "TELEPORT · FLOORS", 24, 232, 552, 34, 24, Palette.Teal, Palette.Display);
            for (int i = Tower.Count - 1; i >= 0; i--)
            {
                int f = i;
                int row = Tower.Count - 1 - i;
                float rowH = Mathf.Min(82f, 580f / Tower.Count);
                var b = UIKit.Btn(panel, (i == Tower.Count - 1 ? "R" : (i + 1).ToString()) + "  " + Tower.Floors[i].name, 20, 272 + row * rowH, 560, rowH - 8, Tower.Floors[i].color, () => FloorClicked(f), rowH > 70 ? 30 : 26);
                b.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
                floorBtns.Add(b);
            }
            spotsArea = UIKit.Rect("Spots", panel, 0, 272, 600, 580);
            spotsArea.gameObject.SetActive(false);
            panel.gameObject.SetActive(false);
            GameState.I.Changed += Refresh;
            Refresh();
        }

        void OnDestroy() { if (GameState.Exists) GameState.I.Changed -= Refresh; }

        void Refresh()
        {
            if (chipsT == null) return;
            var gs = GameState.I;
            UpdateInfo();
            int row = 0;
            for (int i = Tower.Count - 1; i >= 0; i--, row++)
            {
                bool ok = Run.Active ? Run.FloorOpen(i) : gs.CanAccess(Tower.Floors[i].tier);
                bool here = FloorManager.I != null && FloorManager.I.Current == i;
                UIKit.SetCaption(floorBtns[row], (i == Tower.Count - 1 ? "R" : (i + 1).ToString()) + "  " + Tower.Floors[i].name + (here ? "  ◄" : "") + (ok ? "" : Run.Active ? "  · DÍA " + Run.OpensOn(i) : "  · " + GameState.TierNames[(int)Tower.Floors[i].tier]));
                floorBtns[row].targetGraphic.color = ok ? new Color(0.06f, 0.05f, 0.1f, 0.96f) : new Color(0.15f, 0.04f, 0.06f, 0.96f);
                UISkin.SetSelected(floorBtns[row], here);
            }
        }

        /// <summary>Clock + status card (twice a second while the menu is open).</summary>
        void UpdateInfo()
        {
            var gs = GameState.I; if (gs == null || infoT == null) return;
            clockT.text = System.DateTime.Now.ToString("HH:mm");
            float t; Color c;
            if (Run.Active)
            {
                var rd = RunDirector.I;
                bool running = rd != null && rd.DayRunning;
                infoT.text = "DÍA " + Run.Day + "/" + Run.Days + (running ? "  ·  QUEDAN " + Run.Clock(rd.TimeLeft) : "  ·  EN CASA");
                if (Run.FinalDay) { chipsT.text = Run.N(gs.chips) + " FICHAS · HOY: EL RAID"; t = 1f; c = Palette.Red; }
                else
                {
                    long q = Run.Quota;
                    chipsT.text = "CUOTA  " + Run.N(gs.chips) + " / " + Run.N(q) + (gs.chips >= q ? "  ✓" : "");
                    t = q > 0 ? gs.chips / (float)q : 1f; c = t >= 1f ? Palette.Money : Palette.Gold;
                }
                if (running && rd.TimeLeft < 30f) clockT.color = Palette.Loss; else clockT.color = Palette.Teal;
            }
            else
            {
                infoT.text = GameState.TierNames[(int)gs.CurrentTier] + "  ·  " + gs.xp.ToString("N0") + " XP";
                chipsT.text = gs.chips.ToString("N0") + " CHIPS";
                t = gs.TierProgress; c = Palette.TierColor(gs.CurrentTier);
                clockT.color = Palette.Teal;
            }
            UIKit.SetBar(bar, t);
            var img = bar.GetComponent<UnityEngine.UI.Image>(); if (img != null) img.color = c;
        }

        void FloorClicked(int f)
        {
            var fm = FloorManager.I; if (fm == null) return;
            if (f == fm.Current) { ShowSpots(f); return; }
            fm.GoTo(f);
            Sfx.UI("teleport", 0.5f);
        }

        void ShowSpots(int f)
        {
            page = f;
            foreach (var b in floorBtns) b.gameObject.SetActive(false);
            UIKit.Clear(spotsArea);
            spotsArea.gameObject.SetActive(true);
            pageT.text = "TELEPORT · " + Tower.Floors[f].name;
            int k = 0;
            UIKit.Btn(spotsArea, "◄ ALL FLOORS", 20, 0, 560, 64, Palette.Muted, BackToFloors, 28);
            foreach (var p in FloorManager.I.points)
            {
                if (p.floor != f || k >= 7) continue;
                var tp = p;
                UIKit.Btn(spotsArea, p.label, 20, 76 + k * 76, 560, 68, Tower.Floors[f].color, () => FloorManager.I.GoTo(tp.floor, tp), 30);
                k++;
            }
        }

        void BackToFloors()
        {
            page = -1;
            spotsArea.gameObject.SetActive(false);
            foreach (var b in floorBtns) b.gameObject.SetActive(true);
            pageT.text = "TELEPORT · FLOORS";
            Refresh();
        }

        void LateUpdate()
        {
            var rig = Rig.I; if (rig == null || anchor == null) return;
            Vector3 toHead = (rig.Head.position - anchor.position);
            float dist = toHead.magnitude;
            float dot = Vector3.Dot(-anchor.forward, toHead / Mathf.Max(dist, 1e-4f));
            bool want = (dot > showDot && dist < 0.75f) || XRIn.KeyDown("t") ? true : (visible && dot > showDot - 0.15f && dist < 0.8f);
            if (!XRIn.HasHeadset && XRIn.KeyDown("t")) desktopToggle = !desktopToggle;
            if (!XRIn.HasHeadset) want = desktopToggle;
            if (want != visible)
            {
                visible = want;
                if (visible) { panel.gameObject.SetActive(true); Sfx.UI("wrist_open", 0.5f); XRIn.Haptic(Hand.Right, 0.15f, 0.04f); Refresh(); if (page >= 0 && FloorManager.I != null && page != FloorManager.I.Current) BackToFloors(); }
            }
            vis = Mathf.MoveTowards(vis, visible ? 1f : 0f, Time.deltaTime * 6f);
            if (visible && Time.time >= nextInfo) { nextInfo = Time.time + 0.5f; UpdateInfo(); }
            group.alpha = vis;
            group.interactable = group.blocksRaycasts = vis > 0.6f;
            panel.localScale = Vector3.one * (0.17f / 600f) * Mathf.Lerp(0.85f, 1f, vis);
            if (vis <= 0.001f && panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
            // desktop: float it in front of the camera. It is parented to the head instead of being re-placed every frame
            // under the desktop hand: DesktopTester moves that hand in its own LateUpdate, so the panel lagged/jumped a
            // frame behind whenever the view turned. Back on the controller as soon as a headset is in use.
            var host = XRIn.HasHeadset ? rig.RightCtrl : rig.Head;
            if (host != null && anchor.parent != host)
            {
                anchor.SetParent(host, false);
                anchor.localPosition = XRIn.HasHeadset ? offset : new Vector3(0, -0.05f, 0.45f);
                anchor.localRotation = XRIn.HasHeadset ? Quaternion.Euler(euler) : Quaternion.identity;
            }
        }
        bool desktopToggle;
    }
}
