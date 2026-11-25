using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Ascend
{
    /// <summary>3D TextMeshPro labels (signs, name tags, numbers floating over tables).</summary>
    public static class Txt
    {
        /// <summary>height = line height in metres; width = wrap width in metres (0 = no wrap).</summary>
        public static TextMeshPro Make(string name, Transform parent, Vector3 localPos, string text, float height, Color color,
            TMP_FontAsset font = null, TextAlignmentOptions align = TextAlignmentOptions.Center, float width = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var t = go.AddComponent<TextMeshPro>();
            t.font = font != null ? font : Palette.Display;
            t.fontSharedMaterial = OneSided(t.font);
            t.fontSize = height * 10f;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.richText = true;
            t.enableWordWrapping = width > 0f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(width > 0 ? width : 20f, height * 2f);
            t.overflowMode = TextOverflowModes.Overflow;
            var r = t.GetComponent<MeshRenderer>();
            if (r != null) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
            return t;
        }

        static readonly System.Collections.Generic.Dictionary<TMP_FontAsset, Material> oneSided = new System.Collections.Generic.Dictionary<TMP_FontAsset, Material>();
        /// <summary>World signs are drawn from the front only: seen from behind they disappear instead of reading mirrored.
        /// One shared material per font, so the texts still batch.</summary>
        public static Material OneSided(TMP_FontAsset font)
        {
            if (font == null || font.material == null) return null;
            Material m;
            if (oneSided.TryGetValue(font, out m) && m != null) return m;
            m = new Material(font.material) { name = font.material.name + " (one-sided)" };
            if (m.HasProperty(ShaderUtilities.ShaderTag_CullMode)) m.SetFloat(ShaderUtilities.ShaderTag_CullMode, 2f);
            oneSided[font] = m;
            return m;
        }

        public static TextMeshPro Glow(string name, Transform parent, Vector3 localPos, string text, float height, Color color, float width = 0f)
        {
            var t = Make(name, parent, localPos, text, height, color, Palette.Display, TextAlignmentOptions.Center, width);
            t.fontStyle = FontStyles.Normal;
            return t;
        }
    }

    /// <summary>World-space uGUI panels with TextMeshPro. Coordinates are canvas pixels, origin top-left, y downwards.
    /// Panels get a TrackedDeviceGraphicRaycaster (controller rays + finger poke) and a GraphicRaycaster (mouse).</summary>
    public static class UIKit
    {
        public static readonly Color PanelBg = new Color(0.035f, 0.03f, 0.06f, 0.84f);
        public static readonly Color CellBg = new Color(1f, 1f, 1f, 0.06f);

        public static RectTransform Panel(string name, Transform parent, Vector3 localPos, Quaternion localRot, Vector2 px, float worldWidth, Color border, bool background = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 2f;
            go.AddComponent<GraphicRaycaster>();
            AddXRRaycaster(go);
            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = px;
            rt.localScale = Vector3.one * (worldWidth / px.x);
            if (background)
            {
                var bg = go.AddComponent<Image>();
                UISkin.Round(bg, 28);
                bg.color = PanelBg;
                UISkin.Border(rt, Color.Lerp(UISkin.Line, border, 0.18f), 28);
                // seen from behind, a solid back instead of the text read backwards through the panel
                var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
                back.name = "PanelBack";
                var bc = back.GetComponent<Collider>(); if (bc != null) UnityEngine.Object.DestroyImmediate(bc);
                back.transform.SetParent(go.transform, false);
                back.transform.localPosition = new Vector3(0, 0, 3f);
                back.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                back.transform.localScale = new Vector3(px.x - 8f, px.y - 8f, 1f);
                var br = back.GetComponent<MeshRenderer>();
                br.sharedMaterial = Palette.PanelBack;
                br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return rt;
        }

        static Type _xrRaycaster; static bool _searched;
        public static void AddXRRaycaster(GameObject go)
        {
            if (!_searched) { _searched = true; _xrRaycaster = FindType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster"); }
            if (_xrRaycaster != null && go.GetComponent(_xrRaycaster) == null) go.AddComponent(_xrRaycaster);
        }

        public static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, false); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        public static RectTransform Rect(string name, RectTransform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Place(rt, x, y, w, h);
            return rt;
        }

        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static Image Box(RectTransform parent, float x, float y, float w, float h, Color c, bool raycast = false)
        {
            var rt = Rect("Box", parent, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            float m = Mathf.Min(w, h);
            if (m >= 14f) UISkin.Round(img, Mathf.Min(14f, m * 0.5f));   // cards and bars get soft corners, hairlines stay square
            img.color = c; img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Rounded outline around a rect (t ≈ line thickness in pixels).</summary>
        public static void Frame(RectTransform parent, float x, float y, float w, float h, Color c, float t = 3)
        {
            var rt = Rect("Frame", parent, x, y, w, h);
            UISkin.Border(rt, c, Mathf.Clamp(t * 4.8f, 8f, Mathf.Min(w, h) * 0.5f));
        }

        public static TextMeshProUGUI Label(RectTransform parent, string text, float x, float y, float w, float h, float size, Color c,
            TMP_FontAsset font = null, TextAlignmentOptions anchor = TextAlignmentOptions.MidlineLeft)
        {
            var rt = Rect("Label", parent, x, y, w, h);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font != null ? font : Palette.Body;
            t.fontSize = size; t.color = c; t.alignment = anchor; t.text = text;
            t.enableWordWrapping = true; t.overflowMode = TextOverflowModes.Overflow;
            t.richText = true; t.raycastTarget = false;
            // headings and captions use the chunky UI face; mono and body copy keep their faces
            if (font == Palette.Display || font == Palette.UI) UISkin.Type(t, size);
            return t;
        }

        public static Button Btn(RectTransform parent, string label, float x, float y, float w, float h, Color accent, UnityAction onClick, float size = 30)
        {
            var rt = Rect("Btn_" + label, parent, x, y, w, h);
            float r = Mathf.Min(18f, Mathf.Min(w, h) * 0.32f);
            var img = rt.gameObject.AddComponent<Image>();
            UISkin.Round(img, r);
            img.color = new Color(accent.r * 0.16f + 0.02f, accent.g * 0.16f + 0.02f, accent.b * 0.16f + 0.03f, 0.82f);
            // solid block that fades in on hover / selection (the caption turns dark on top of it)
            var hl = Box(rt, 0, 0, w, h, Color.Lerp(accent, Color.white, 0.12f));
            hl.name = "Highlight"; UISkin.Round(hl, r);
            var hc = hl.color; hc.a = 0f; hl.color = hc;
            UISkin.Border(rt, Color.Lerp(UISkin.Line, accent, 0.3f), r);
            var txt = Label(rt, label, 8, 0, w - 16, h, size, Palette.Text, Palette.UI, TextAlignmentOptions.Center);
            txt.name = "Caption";
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.None;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            b.onClick.AddListener(() => { Sfx.UI("ui_click", 0.6f); XRIn.HapticBoth(0.25f, 0.03f); });
            if (onClick != null) b.onClick.AddListener(onClick);
            var juice = rt.gameObject.AddComponent<ButtonJuice>();
            juice.highlight = hl; juice.caption = txt; juice.captionColor = Palette.Text;
            rt.gameObject.AddComponent<HoverSound>();
            return b;
        }

        public static void SetCaption(Button b, string s)
        {
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.text = s;
        }

        public static RawImage Pic(RectTransform parent, Texture tex, float x, float y, float w, float h)
        {
            var rt = Rect("Pic", parent, x, y, w, h);
            var r = rt.gameObject.AddComponent<RawImage>();
            r.texture = tex; r.raycastTarget = false;
            return r;
        }

        /// <summary>Horizontal progress bar; returns the fill rect, update with SetBar.</summary>
        public static RectTransform Bar(RectTransform parent, float x, float y, float w, float h, Color c)
        {
            Box(parent, x, y, w, h, new Color(1, 1, 1, 0.08f));
            var fill = Box(parent, x, y, w, h, c);
            fill.name = "Fill";
            fill.rectTransform.sizeDelta = new Vector2(0, h);
            fill.gameObject.AddComponent<LayoutElement>().preferredWidth = w;
            return fill.rectTransform;
        }

        public static void SetBar(RectTransform fill, float t)
        {
            var le = fill.GetComponent<LayoutElement>();
            float w = le != null ? le.preferredWidth : 100f;
            fill.sizeDelta = new Vector2(w * Mathf.Clamp01(t), fill.sizeDelta.y);
        }

        public static void Clear(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(rt.GetChild(i).gameObject);
        }
    }
}
