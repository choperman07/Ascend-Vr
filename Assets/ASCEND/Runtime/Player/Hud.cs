using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Minimal HUD: toasts float in front of you for a moment, your balance lives on a left-wrist watch
    /// (look at it to read it), reality checks and tier-ups. Nothing stays glued to your face.</summary>
    public class Hud : MonoBehaviour
    {
        Transform toastRoot, watch;
        TMP_Text toastT, watchChips, watchSub;
        UnityEngine.UI.Image toastBar;
        readonly Queue<KeyValuePair<string, Color>> queue = new Queue<KeyValuePair<string, Color>>();
        bool showing;
        float playStart, nextReality;
        long shownChips = -1;

        void Start()
        {
            toastRoot = new GameObject("Toast").transform;
            toastRoot.SetParent(transform, false);
            // toast card: dark see-through rounded card, white outline, coloured tab on the left, chunky caption
            var card = UIKit.Panel("Card", toastRoot, Vector3.zero, Quaternion.identity, new Vector2(1100, 120), 1.0f, Color.white);
            foreach (var img in card.GetComponentsInChildren<UnityEngine.UI.Image>()) img.raycastTarget = false;   // never eat a pointer ray
            toastBar = UIKit.Box(card, 26, 28, 16, 64, Palette.Gold);
            toastT = UIKit.Label(card, "", 64, 0, 1012, 120, 46, Palette.Text, Palette.UI, TextAlignmentOptions.MidlineLeft);
            toastRoot.gameObject.SetActive(false);
            gameObject.AddComponent<DesktopHud>();
            GameState.I.Toast += OnToast;
            GameState.I.TierUp += OnTierUp;
            GameState.I.Changed += OnChanged;
            playStart = Time.time;
            nextReality = Time.time + GameState.I.realityCheckMinutes * 60f;
            BuildWatch();
        }
        // the toast coroutine dies with the component: without this, "showing" stays true and no toast ever shows again
        void OnDisable()
        {
            showing = false;
            if (toastRoot != null) toastRoot.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (!GameState.Exists) return;
            GameState.I.Toast -= OnToast; GameState.I.TierUp -= OnTierUp; GameState.I.Changed -= OnChanged;
        }

        void BuildWatch()
        {
            if (Rig.I == null || Rig.I.LeftCtrl == null) return;
            watch = new GameObject("WristWatch").transform;
            watch.SetParent(Rig.I.LeftCtrl, false);
            watch.localPosition = new Vector3(-0.04f, 0.0f, -0.13f);
            watch.localRotation = Quaternion.Euler(0, 90, 0);
            var k = new MeshKit();
            k.Box(new Vector3(0, 0, 0.006f), new Vector3(0.075f, 0.05f, 0.01f), Palette.Bg, 0f);
            k.Bar(new Vector3(-0.038f, -0.026f, 0), new Vector3(0.038f, -0.026f, 0), 0.003f, Palette.Gold, 1f);
            k.Bar(new Vector3(-0.038f, 0.026f, 0), new Vector3(0.038f, 0.026f, 0), 0.003f, Palette.Gold, 1f);
            k.Spawn("Face", watch, Palette.Neon, false);
            watchChips = Txt.Make("Chips", watch, new Vector3(0, 0.006f, 0), "", 0.016f, Palette.Money, Palette.UI);
            watchSub = Txt.Make("Sub", watch, new Vector3(0, -0.014f, 0), "", 0.0075f, Palette.Muted, Palette.Mono);
            OnChanged();
        }

        void OnChanged()
        {
            if (watchChips == null) return;
            var gs = GameState.I;
            if (shownChips >= 0 && gs.chips != shownChips) StartCoroutine(Fx.Pulse(watch, 1.12f, 0.2f));
            shownChips = gs.chips;
            watchChips.text = gs.chips.ToString("N0");
            watchSub.text = "PLAY CHIPS · JACKPOT " + gs.jackpot.ToString("N0") + "\nSESSION " + (gs.sessionNet >= 0 ? "+" : "") + gs.sessionNet.ToString("N0");
        }

        void OnTierUp(Tier t)
        {
            Voice.Announce("ann_level_up");
            Sfx.UI("level_up", 0.9f);
            var cam = Rig.Ready ? Rig.I.Head : null;
            if (cam != null) Juice.Burst("confetti", cam.position + cam.forward * 1.2f, 120);
            OnToast("TIER UP · " + GameState.TierNames[(int)t] + " · NEW FLOORS UNLOCKED", Palette.TierColor(t));
        }

        void OnToast(string msg, Color c)
        {
            if (toastT == null) return;
            if (queue.Count >= 8) queue.Dequeue();   // drop the oldest rather than lagging further behind
            queue.Enqueue(new KeyValuePair<string, Color>(msg, c));
            if (!showing && isActiveAndEnabled) StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            showing = true;
            while (queue.Count > 0)
            {
                var m = queue.Dequeue();
                toastT.text = m.Key; toastT.color = Color.Lerp(Palette.Text, m.Value, 0.35f); toastBar.color = m.Value;
                toastRoot.gameObject.SetActive(true);
                Sfx.UI("ui_open", 0.35f);
                // a burst of toasts (win + XP + mission + achievement…) must not queue up for half a minute
                float t = 0, dur = queue.Count >= 3 ? 1.2f : queue.Count >= 1 ? 1.8f : 2.6f;
                while (t < dur)
                {
                    t += Time.deltaTime;
                    if (Rig.Ready)
                    {
                        var h = Rig.I.Head;
                        var f = h.forward; f.y = Mathf.Clamp(f.y, -0.5f, 0.3f); f.Normalize();
                        var target = h.position + f * 1.1f + Vector3.down * 0.32f;
                        toastRoot.position = t < 0.05f ? target : Vector3.Lerp(toastRoot.position, target, Time.deltaTime * 4f);
                        toastRoot.rotation = Quaternion.LookRotation(toastRoot.position - h.position);
                    }
                    toastRoot.localScale = Vector3.one * Fx.EaseOutBack(Mathf.Min(1f, t / 0.25f)) * Mathf.Clamp01((dur - t) / 0.25f);
                    yield return null;
                }
                toastRoot.gameObject.SetActive(false);
            }
            showing = false;
        }

        void Update()
        {
            // the watch is only bright when you look at it
            if (watch != null && Rig.Ready)
            {
                float d = Vector3.Dot(-watch.forward, (Rig.I.Head.position - watch.position).normalized);
                float a = Mathf.InverseLerp(0.4f, 0.8f, d);
                watchChips.alpha = 0.15f + a * 0.85f; watchSub.alpha = a;
            }
            // reality check (responsible play): a reminder every N minutes
            if (Time.time > nextReality)
            {
                nextReality = Time.time + GameState.I.realityCheckMinutes * 60f;
                int min = Mathf.RoundToInt((Time.time - playStart) / 60f);
                Voice.Announce("ann_reality");
                OnToast("REALITY CHECK · " + min + " MIN PLAYED · SESSION " + GameState.I.sessionNet.ToString("N0") + " · MENU > BREAK", Palette.Gold);
            }
        }
    }
}
