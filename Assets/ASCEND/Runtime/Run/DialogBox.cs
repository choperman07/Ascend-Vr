using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ascend
{
    // ====================================================================== text-only dialogue (no voice)
    /// <summary>A speech box above a character: name, typewriter text, one button to go on. Text only, by design.</summary>
    public class DialogBox : MonoBehaviour
    {
        RectTransform panel; TextMeshProUGUI nameT, bodyT; Button next; TextMeshProUGUI nextT;
        string[] lines; int idx; string lastLabel; Action done; float shown; bool typing; string full;
        Transform speaker; bool auto; float autoAt;
        Vector3 baseScale;
        public bool Open { get { return gameObject.activeSelf; } }

        public static DialogBox Create()
        {
            var go = new GameObject("Dialog");
            var d = go.AddComponent<DialogBox>();
            d.panel = UIKit.Panel("DialogPanel", go.transform, Vector3.zero, Quaternion.identity, new Vector2(1200, 420), 0.95f, Palette.Gold);
            d.nameT = UIKit.Label(d.panel, "", 44, 26, 900, 56, 44, Palette.Gold, Palette.UI, TextAlignmentOptions.MidlineLeft);
            d.bodyT = UIKit.Label(d.panel, "", 44, 92, 1112, 200, 46, Palette.Text, Palette.Body, TextAlignmentOptions.TopLeft);
            d.next = UIKit.Btn(d.panel, "SIGUIENTE ▸", 760, 306, 396, 88, Palette.Gold, () => d.Advance(), 38);
            d.nextT = d.next.GetComponentInChildren<TextMeshProUGUI>();
            go.AddComponent<FacePlayer>();
            go.SetActive(false);
            return d;
        }

        public void Show(Transform who, string name, string[] text, string last, Action onDone, bool autoAdvance = false)
        {
            speaker = who; lines = text; idx = 0; lastLabel = last; done = onDone; auto = autoAdvance;
            nameT.text = name;
            gameObject.SetActive(true);
            Place(true);
            next.gameObject.SetActive(!auto);
            Begin();
        }

        public void Hide() { gameObject.SetActive(false); done = null; }

        void Begin()
        {
            full = lines[idx]; shown = 0f; typing = true; bodyT.text = "";
            nextT.text = idx == lines.Length - 1 ? lastLabel : "SIGUIENTE ▸";
            autoAt = -1f;
        }

        void Advance()
        {
            if (typing) { typing = false; bodyT.text = full; return; }
            idx++;
            if (idx >= lines.Length) { var d = done; gameObject.SetActive(false); done = null; if (d != null) d(); return; }
            Begin();
        }

        void Place(bool snap)
        {
            var viewer = TrailerView.Head;
            if (speaker == null || viewer == null) return;
            if (TrailerView.Override != null)
            {
                // trailer camera: lower third, centred, square to the lens (reads like a subtitle)
                // (kept closer than the actors, and scaled to match, so nobody ever stands in front of the words)
                var f = viewer.forward; float d = Mathf.Max(0.2f, TrailerView.DialogDistance);
                if (baseScale == Vector3.zero) baseScale = transform.localScale;
                transform.localScale = baseScale * (d / 1.25f);
                transform.position = viewer.position + f * d - viewer.up * (0.3f * d / 1.25f);
                transform.rotation = Quaternion.LookRotation(f, viewer.up);
                return;
            }
            if (baseScale != Vector3.zero) transform.localScale = baseScale;
            var to = viewer.position - speaker.position; to.y = 0; to = to.sqrMagnitude > 1e-4f ? to.normalized : Vector3.forward;
            // between the two of you, a little to the side and below eye level: close enough to read, never on his face
            var head = viewer.position;
            var toSpeaker = speaker.position - head; toSpeaker.y = 0;
            float dist = toSpeaker.magnitude; toSpeaker = dist > 1e-3f ? toSpeaker / dist : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, toSpeaker);
            var want = head + toSpeaker * Mathf.Clamp(dist * 0.55f, 0.9f, 1.5f) + side * 0.3f + Vector3.down * 0.25f;
            transform.position = snap ? want : Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
            if (snap) { var d = transform.position - viewer.position; d.y = 0; if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d.normalized); }
        }

        void Update()
        {
            Place(false);
            if (typing)
            {
                int before = Mathf.FloorToInt(shown);
                shown += Time.deltaTime * 42f;
                int n = Mathf.Min(full.Length, Mathf.FloorToInt(shown));
                if (n != before && n % 3 == 0) Sfx.UI("ui_hover", 0.18f, 0.85f + UnityEngine.Random.value * 0.2f);
                bodyT.text = full.Substring(0, n);
                if (n >= full.Length) typing = false;
            }
            else if (auto)
            {
                if (autoAt < 0f) autoAt = Time.time + 1.4f + full.Length * 0.03f;
                else if (Time.time > autoAt) Advance();
            }
            // desktop shortcut: Space / E / Enter continues
            if (!auto && (XRIn.KeyDown("space") || XRIn.KeyDown("e") || XRIn.Down(Hand.Right, Btn.Primary))) Advance();
        }
    }
}
