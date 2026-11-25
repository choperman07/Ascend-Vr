using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ascend
{
    /// <summary>First-visit intro: seven slow cinematic shots across the tower (rooftop skyline, the lobby and its DJ
    /// floor, the Classics, the slot banks, the Sky Lounge and the Raid roof) narrated by the ASCEND narrator, with
    /// letterbox bars on the desktop view and subtitles in front of the eyes in VR. The camera only glides (it never
    /// rotates your view in VR) and every cut is a fade. Skip any time with A / B (or Space / Esc on desktop).
    /// Plays once; replay from Menu → Settings → REPLAY INTRO.</summary>
    public class IntroCinematic : MonoBehaviour
    {
        const string Key = "asc2_intro_seen";
        public static bool ShouldPlay { get { return PlayerPrefs.GetInt(Key, 0) == 0; } }
        /// <summary>True while the cinematic owns the rig (desktop keys, floor travel and the lift are blocked).</summary>
        public static bool Playing { get; private set; }
        bool finished, mine;
        public static void Replay() { if (FloorManager.I != null && FloorManager.I.GetComponent<IntroCinematic>() == null) FloorManager.I.gameObject.AddComponent<IntroCinematic>(); }

        struct Shot
        {
            public int floor; public float a0, a1, r0, r1, h0, h1, dur; public string vo, title;
            public Shot(int f, float a0, float a1, float r0, float r1, float h0, float h1, float dur, string vo, string title)
            { floor = f; this.a0 = a0; this.a1 = a1; this.r0 = r0; this.r1 = r1; this.h0 = h0; this.h1 = h1; this.dur = dur; this.vo = vo; this.title = title; }
        }

        // angle (deg around the tower axis), radius and height above the floor glide from *0 to *1; the view looks at the centre
        static readonly Shot[] Shots =
        {
            new Shot(8, 20f, 50f, 30f, 24f, 9f, 6f, 5.2f, "narr_1", ""),
            new Shot(0, 105f, 140f, 15f, 11.5f, 3.2f, 1.7f, 5.6f, "narr_2", "LOBBY · MAIN HALL"),
            new Shot(1, 30f, 70f, 13f, 9.5f, 2.2f, 1.2f, 6.2f, "narr_3", "THE CLASSICS"),
            new Shot(5, -50f, -10f, 14f, 11f, 1.8f, 1.4f, 5f, "narr_4", "LUCK & LOOT"),
            new Shot(7, 100f, 140f, 14f, 10f, 2.6f, 1.5f, 5f, "narr_5", "SKY LOUNGE"),
            new Shot(8, 200f, 225f, 20f, 13f, 4f, 2.2f, 4.6f, "narr_6", "ROOFTOP · RAID"),
            new Shot(0, 180f, 180f, 9f, 5.6f, 0.4f, 0f, 4.6f, "narr_7", ""),
        };

        bool skip; AudioSource narr;
        Canvas overlay; RectTransform barTop, barBottom; TextMeshProUGUI skipHint;
        TextMeshPro sub, titleT;

        IEnumerator Start()
        {
            var rig = Rig.I;
            if (rig == null) { Destroy(this); yield break; }
            yield return null;
            if (Playing || FloorManager.I == null) { Destroy(this); yield break; }
            Playing = true; mine = true;
            // replayed from the menu while seated / mid-conversation: release the seat and the guest first, or the
            // seat stays taken and standing up later teleports you back to a floor that is switched off
            if (PlayerSeating.Seated) PlayerSeating.I.StandUp(false);
            if (NpcChat.I != null) NpcChat.I.Close();
            rig.Freeze(true);
            Sfx.Music(Sfx.Background, 0.5f); Sfx.musicLock = Sfx.Background;
            BuildOverlay();
            // subtitles / shot title float in front of the eyes (works in the headset and on the desktop view)
            sub = Txt.Make("IntroSub", rig.Head, new Vector3(0, -0.32f, 1.4f), "", 0.055f, Palette.Text, Palette.Display, TextAlignmentOptions.Center, 1.6f);
            titleT = Txt.Glow("IntroTitle", rig.Head, new Vector3(0, 0.25f, 1.6f), "", 0.09f, Palette.Gold, 2f);
            rig.SetFade(1f);
            for (float t = 0; t < 0.6f && !skip; t += Time.deltaTime) { Bars(Mathf.Clamp01(t / 0.6f)); yield return null; }

            for (int i = 0; i < Shots.Length && !skip; i++)
            {
                var s = Shots[i];
                if (FloorManager.I.Current != s.floor) FloorManager.I.Activate(s.floor, false);
                float y = Tower.Y(s.floor);
                var start = P(s, y, 0f);
                // face the centre of the floor from the start of the glide (no rotation during the shot: VR-friendly)
                var mid = P(s, y, 0.5f);
                // look ACROSS the room along a chord (70° ahead on the 10 m ring), not at the centre where the
                // elevator core would fill the frame
                float am = (s.a0 + s.a1) * 0.5f + (s.a1 >= s.a0 ? 70f : -70f);
                var look = new Vector3(Mathf.Sin(am * Mathf.Deg2Rad) * 10f, mid.y, Mathf.Cos(am * Mathf.Deg2Rad) * 10f);
                float yaw = Mathf.Atan2(look.x - mid.x, look.z - mid.z) * Mathf.Rad2Deg;
                if (i == Shots.Length - 1) yaw = Tower.ArrivalYaw;
                rig.Place(start, yaw);
                titleT.text = s.title; sub.text = Voice.Text(s.vo);
                narr = Sfx.UI(s.vo, 1f);
                float voLen = narr != null && narr.clip != null ? narr.clip.length : 3f;
                float dur = Mathf.Max(s.dur, voLen + 1.2f);
                for (float t = 0; t < dur && !skip; t += Time.deltaTime)
                {
                    float k = t / dur;
                    // fade in / out at the cut
                    rig.SetFade(Mathf.Max(1f - t / 0.45f, (t - (dur - 0.45f)) / 0.45f, 0f));
                    titleT.alpha = Mathf.Clamp01(Mathf.Min((t - 0.6f) / 0.5f, (dur - 0.8f - t) / 0.5f));
                    var p = i == Shots.Length - 1 ? Vector3.Lerp(start, Tower.Arrival(0), Fx.EaseInOut(k)) : P(s, y, Fx.EaseInOut(k));
                    MoveFeet(rig, p);
                    yield return null;
                }
            }
            // back to the lobby, at the elevator, in control
            rig.SetFade(1f);
            if (FloorManager.I.Current != 0) FloorManager.I.Activate(0, false);
            rig.Place(Tower.Arrival(0), Tower.ArrivalYaw);
            Sfx.musicLock = null; Sfx.Music(Tower.Floors[0].music, 0.35f);
            if (sub != null) Destroy(sub.gameObject);
            if (titleT != null) Destroy(titleT.gameObject);
            for (float t = 0; t < 0.6f; t += Time.deltaTime) { rig.SetFade(1f - t / 0.6f); Bars(1f - t / 0.6f); yield return null; }
            rig.SetFade(0f);
            if (overlay != null) Destroy(overlay.gameObject);
            rig.Freeze(false);
            finished = true; Playing = false;
            PlayerPrefs.SetInt(Key, 1); PlayerPrefs.Save();
            Voice.Announce("ann_welcome", 0.8f);
            Destroy(this);
        }

        static Vector3 P(Shot s, float y, float k)
        {
            float a = Mathf.Lerp(s.a0, s.a1, k) * Mathf.Deg2Rad, r = Mathf.Lerp(s.r0, s.r1, k);
            return new Vector3(Mathf.Sin(a) * r, y + Mathf.Lerp(s.h0, s.h1, k), Mathf.Cos(a) * r);
        }

        /// <summary>Glide: translate the XR origin so the feet follow the path (the player's own head yaw is untouched).</summary>
        static void MoveFeet(Rig rig, Vector3 feet)
        {
            var o = rig.Origin != null ? rig.Origin.transform : rig.transform;
            var head = rig.Head.position;
            var cur = new Vector3(head.x, o.position.y, head.z);
            var cc = rig.GetComponent<CharacterController>();
            bool on = cc != null && cc.enabled; if (on) cc.enabled = false;
            o.position += feet - cur;
            if (on) cc.enabled = true;
        }

        void Update()
        {
            if (skip) return;
            bool s = XRIn.Down(Hand.Right, Btn.Primary) || XRIn.Down(Hand.Right, Btn.Secondary) || XRIn.Down(Hand.Left, Btn.Primary);
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null && (k.spaceKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame)) s = true;
#endif
            if (s) { skip = true; if (narr != null) narr.Stop(); }
        }

        void BuildOverlay()
        {
            var go = new GameObject("IntroLetterbox", typeof(Canvas), typeof(CanvasScaler));
            overlay = go.GetComponent<Canvas>(); overlay.renderMode = RenderMode.ScreenSpaceOverlay; overlay.sortingOrder = 500;
            barTop = Bar(go.transform, true); barBottom = Bar(go.transform, false);
            var h = new GameObject("Skip", typeof(RectTransform)); h.transform.SetParent(barBottom, false);
            skipHint = h.AddComponent<TextMeshProUGUI>(); skipHint.text = "SPACE / A · SKIP"; skipHint.fontSize = 20; skipHint.color = new Color(1, 1, 1, 0.55f);
            skipHint.alignment = TextAlignmentOptions.Right; skipHint.font = Palette.Display;
            var rt = h.GetComponent<RectTransform>(); rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1); rt.offsetMin = new Vector2(20, 10); rt.offsetMax = new Vector2(-30, -10);
            Bars(0f);
        }

        static RectTransform Bar(Transform parent, bool top)
        {
            var go = new GameObject(top ? "Top" : "Bottom", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Color.black;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, top ? 1 : 0); rt.anchorMax = new Vector2(1, top ? 1 : 0); rt.pivot = new Vector2(0.5f, top ? 1 : 0);
            return rt;
        }

        void Bars(float k)
        {
            float h = Screen.height * 0.12f * k;
            if (barTop != null) barTop.sizeDelta = new Vector2(0, h);
            if (barBottom != null) barBottom.sizeDelta = new Vector2(0, h);
        }

        void OnDestroy()
        {
            if (!mine) return;   // a duplicate that never started
            if (Sfx.musicLock == Sfx.Background) Sfx.musicLock = null;
            if (overlay != null) Destroy(overlay.gameObject);
            if (sub != null) Destroy(sub.gameObject);
            if (titleT != null) Destroy(titleT.gameObject);
            // torn down before the end (scene unload, component removed): never leave the player frozen and blacked out
            if (!finished)
            {
                Playing = false;
                if (narr != null) narr.Stop();
                var rig = Rig.I;
                if (rig != null) { rig.SetFade(0f); rig.Freeze(false); }
            }
        }
    }
}
