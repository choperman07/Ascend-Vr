using System;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Physical arcade button: press it with your fingertip, aim the controller ray and pull the trigger,
    /// or click it with the mouse in the editor.</summary>
    public class PokeButton : MonoBehaviour
    {
        public Action onPress;
        public bool interactable = true;
        public float cooldown = 0.35f;
        Transform cap; Renderer capR; TextMeshPro label;
        Color color; float last = -10f; float press;

        public static PokeButton Create(string name, Transform parent, Vector3 localPos, Quaternion localRot, float radius, Color c, string text, Action onPress, float textSize = 0.04f)
        {
            var root = new GameObject("Btn_" + name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = localRot;
            var k = new MeshKit();
            k.Cylinder(new Vector3(0, 0.01f, 0), radius * 1.25f, radius * 1.18f, 0.02f, 28, Quaternion.identity, Palette.Dark, 0f);
            k.Torus(new Vector3(0, 0.02f, 0), radius * 1.15f, 0.004f, 28, 5, Quaternion.identity, c, 1f);
            k.Spawn("Base", root.transform, Palette.Neon, false);
            var ck = new MeshKit();
            ck.Cylinder(Vector3.zero, radius, radius * 0.92f, 0.022f, 28, Quaternion.identity, c, 0.85f);
            var capGo = ck.Spawn("Cap", root.transform, new Vector3(0, 0.032f, 0), Quaternion.identity, Palette.Neon, false);
            var b = root.AddComponent<PokeButton>();
            b.cap = capGo.transform; b.capR = capGo.GetComponent<Renderer>(); b.color = c; b.onPress = onPress;
            var col = root.AddComponent<BoxCollider>(); col.isTrigger = false; col.size = new Vector3(radius * 2.4f, 0.05f, radius * 2.4f); col.center = new Vector3(0, 0.03f, 0);
            var trig = capGo.AddComponent<SphereCollider>(); trig.isTrigger = true; trig.radius = radius * 0.9f; trig.center = new Vector3(0, 0.005f, 0);
            capGo.AddComponent<PokeRelay>().button = b;
            var si = root.AddComponent<XRSimpleInteractable>();
            si.selectEntered.AddListener(_ => b.Press());
            if (!string.IsNullOrEmpty(text))
            {
                // the word sits on the cap itself (dark ink on the lit colour): always on the button, never hanging
                // off the table edge or over the felt print
                float h = Mathf.Min(radius * 1.15f, radius * 1.75f / (0.47f * Mathf.Max(1, text.Length)));
                b.label = Txt.Make("Label", capGo.transform, new Vector3(0, 0.0125f, 0), text, h, Ink, Palette.Display);
                b.label.transform.localRotation = Quaternion.Euler(90, 0, 0);
                b.label.fontStyle = FontStyles.Bold;
            }
            b.Glow(Build.GlowSprite(root.transform, new Vector3(0, 0.05f, 0), radius * 4f, c, 0.35f));
            return b;
        }

        static readonly Color Ink = new Color(0.07f, 0.05f, 0.12f, 1f);
        Transform glow;
        void Glow(Transform g) { glow = g; }

        public void SetLabel(string s) { if (label != null) label.text = s; }

        public void SetInteractable(bool on)
        {
            interactable = on;
            Fx.SetColor(capR, on ? color : color * 0.25f, on ? 0.85f : 0.1f);
            if (glow != null) glow.gameObject.SetActive(on);
            if (label != null) label.color = on ? Ink : new Color(0.75f, 0.72f, 0.82f, 0.8f);
        }

        public void Press()
        {
            if (!interactable || Time.time - last < cooldown) return;
            last = Time.time;
            press = 1f;
            Sfx.Play("ui_click", transform.position, 0.8f, 0.8f);
            XRIn.HapticBoth(0.35f, 0.04f);
            if (onPress != null) onPress();
        }

        void Update()
        {
            press = Mathf.MoveTowards(press, 0f, Time.deltaTime * 6f);
            if (cap != null) cap.localPosition = new Vector3(0, 0.032f - press * 0.014f, 0);
        }
    }
}
