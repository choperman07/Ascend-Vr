using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ascend
{
    /// <summary>The "party casino" UI skin: chunky rounded type (Lilita One), dark see-through rounded cards with a
    /// thin light outline, big solid highlight blocks on hover/selection and a soft drop shadow under the text.
    /// Every sprite is generated at runtime (no texture files), so the look stays sharp at any size in VR.</summary>
    public static class UISkin
    {
        public static readonly Color Card = new Color(0.035f, 0.03f, 0.06f, 0.74f);
        public static readonly Color CardSolid = new Color(0.06f, 0.05f, 0.1f, 0.94f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.86f);
        public static readonly Color LineSoft = new Color(1f, 1f, 1f, 0.32f);
        public static readonly Color Ink = new Color(0.04f, 0.03f, 0.07f, 1f);

        const int Tex = 128, Rad = 48, Stroke = 10;   // master sprite: 128 px, corner radius 48 px, outline 10 px
        static Sprite _fill, _line;

        /// <summary>Solid rounded rectangle (9-sliced).</summary>
        public static Sprite Fill { get { if (_fill == null) _fill = Make(false); return _fill; } }
        /// <summary>Rounded outline only (9-sliced).</summary>
        public static Sprite Outline { get { if (_line == null) _line = Make(true); return _line; } }

        static Sprite Make(bool outline)
        {
            var t = new Texture2D(Tex, Tex, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = outline ? "UISkinLine" : "UISkinFill" };
            var px = new Color32[Tex * Tex];
            float h = Tex * 0.5f;
            for (int y = 0; y < Tex; y++)
                for (int x = 0; x < Tex; x++)
                {
                    // signed distance to a rounded square filling the texture
                    float qx = Mathf.Abs(x + 0.5f - h) - (h - Rad), qy = Mathf.Abs(y + 0.5f - h) - (h - Rad);
                    float d = Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0) - Rad;
                    float a = Mathf.Clamp01(0.5f - d);                          // inside the shape
                    if (outline) a = Mathf.Min(a, Mathf.Clamp01(d + Stroke + 0.5f));   // only the band near the edge
                    px[y * Tex + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, Tex, Tex), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(Rad, Rad, Rad, Rad));
        }

        /// <summary>Turns an Image into a rounded card. radius = corner radius in canvas pixels.</summary>
        public static Image Round(Image img, float radius, bool outline = false)
        {
            img.sprite = outline ? Outline : Fill;
            img.type = Image.Type.Sliced;
            img.fillCenter = true;
            img.pixelsPerUnitMultiplier = Rad / Mathf.Max(1f, radius);
            return img;
        }

        /// <summary>Thin rounded outline drawn over the parent rect, matching a Round() fill of the same radius
        /// (the stroke is about a fifth of the corner radius: 14 px corner → 3 px line).</summary>
        public static Image Border(RectTransform parent, Color c, float radius)
        {
            var go = new GameObject("Outline", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.raycastTarget = false; img.color = c;
            img.sprite = Outline; img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = Rad / Mathf.Max(1f, radius);
            return img;
        }

        // ------------------------------------------------------------------ text
        static readonly Dictionary<int, Material> _shadow = new Dictionary<int, Material>();
        static readonly Dictionary<int, Material> _sticker = new Dictionary<int, Material>();

        /// <summary>Font material with a soft drop shadow (readable over any 3D background). Uses the preset built
        /// by ASCEND → Rebuild Fonts when present, so the shader variant ships in the Quest build.</summary>
        public static Material Shadow(TMP_FontAsset f)
        {
            if (f == null) return null;
            Material m;
            if (_shadow.TryGetValue(f.GetInstanceID(), out m) && m != null) return m;
            m = f == Palette.UI ? Resources.Load<Material>("ASCEND/Fonts/TMP/Lilita SDF Shadow") : null;
            if (m == null)
            {
                m = new Material(f.material) { name = f.name + " Shadow" };
                m.EnableKeyword("UNDERLAY_ON");
                m.SetColor("_UnderlayColor", new Color(0, 0, 0, 0.55f));
                m.SetFloat("_UnderlayOffsetX", 0.35f); m.SetFloat("_UnderlayOffsetY", -0.55f);
                m.SetFloat("_UnderlayDilate", 0.25f); m.SetFloat("_UnderlaySoftness", 0.35f);
            }
            _shadow[f.GetInstanceID()] = m;
            return m;
        }

        /// <summary>"Sticker" logo material: thick white outline + hard dark shadow.</summary>
        public static Material Sticker(TMP_FontAsset f)
        {
            if (f == null) return null;
            Material m;
            if (_sticker.TryGetValue(f.GetInstanceID(), out m) && m != null) return m;
            m = f == Palette.UI ? Resources.Load<Material>("ASCEND/Fonts/TMP/Lilita SDF Sticker") : null;
            if (m == null)
            {
                m = new Material(f.material) { name = f.name + " Sticker" };
                m.EnableKeyword("OUTLINE_ON"); m.EnableKeyword("UNDERLAY_ON");
                m.SetFloat("_OutlineWidth", 0.32f); m.SetColor("_OutlineColor", Color.white);
                m.SetFloat("_FaceDilate", 0.18f);
                m.SetColor("_UnderlayColor", new Color(0.05f, 0.02f, 0.1f, 0.9f));
                m.SetFloat("_UnderlayOffsetX", 0.6f); m.SetFloat("_UnderlayOffsetY", -0.9f);
                m.SetFloat("_UnderlayDilate", 0.6f); m.SetFloat("_UnderlaySoftness", 0.05f);
            }
            _sticker[f.GetInstanceID()] = m;
            return m;
        }

        /// <summary>Applies the UI type style: Lilita One, drop shadow and shrink-to-fit (Lilita is wider than the
        /// old condensed display face, so long captions shrink instead of spilling out of their boxes).</summary>
        public static void Type(TMP_Text t, float size, bool fit = true)
        {
            t.font = Palette.UI;
            var sh = Shadow(t.font); if (sh != null) t.fontSharedMaterial = sh;
            t.fontSize = size;
            if (fit) { t.enableAutoSizing = true; t.fontSizeMax = size; t.fontSizeMin = Mathf.Max(8f, size * 0.55f); }
            t.characterSpacing = 1.5f;
        }

        /// <summary>Big two-line sticker logo ("ASCEND" over a smaller line), our own mark.</summary>
        public static void Logo(RectTransform parent, float x, float y, float w, float size)
        {
            var top = UIKit.Label(parent, "ASCEND", x, y, w, size * 1.05f, size, Palette.Teal, Palette.UI, TextAlignmentOptions.MidlineLeft);
            top.fontSharedMaterial = Sticker(Palette.UI);
            top.enableAutoSizing = false; top.characterSpacing = 3f;
            top.enableVertexGradient = true;
            top.colorGradient = new VertexGradient(Palette.Hex("6FF4F6"), Palette.Hex("6FF4F6"), Palette.Teal, Palette.Hex("1FB8D6"));
            var sub = UIKit.Label(parent, "CASINO TOWER", x + size * 0.08f, y + size * 0.92f, w, size * 0.55f, size * 0.46f, Palette.Magenta, Palette.UI, TextAlignmentOptions.MidlineLeft);
            sub.fontSharedMaterial = Sticker(Palette.UI);
            sub.enableAutoSizing = false; sub.characterSpacing = 4f;
            sub.enableVertexGradient = true;
            sub.colorGradient = new VertexGradient(Palette.Hex("FF7AC0"), Palette.Hex("FF7AC0"), Palette.Magenta, Palette.Hex("E01F7E"));
        }

        /// <summary>Small key-cap hint ("MENU [TAB]") for the desktop HUD.</summary>
        public static void KeyHint(RectTransform parent, string label, string key, float rightX, float y, float size = 20)
        {
            float kw = Mathf.Max(34f, key.Length * size * 0.62f + 16f);
            var cap = UIKit.Rect("Key_" + key, parent, rightX - kw, y, kw, size + 14);
            var ci = cap.gameObject.AddComponent<Image>(); Round(ci, 7); ci.color = new Color(1, 1, 1, 0.92f); ci.raycastTarget = false;
            var kt = UIKit.Label(cap, key, 0, 0, kw, size + 14, size * 0.8f, Ink, Palette.UI, TextAlignmentOptions.Center);
            kt.fontSharedMaterial = Palette.UI.material;
            UIKit.Label(parent, label, rightX - kw - 420, y, 408, size + 14, size, Color.white, Palette.UI, TextAlignmentOptions.MidlineRight);
        }

        /// <summary>Marks a button as the current choice (solid highlight block, dark caption) or clears it.</summary>
        public static void SetSelected(Button b, bool on)
        {
            if (b == null) return;
            var j = b.GetComponent<ButtonJuice>();
            if (j != null) j.Selected = on;
        }
    }
}
