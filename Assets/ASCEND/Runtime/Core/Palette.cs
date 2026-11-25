using System.Collections.Generic;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Ascend
{
    /// <summary>Brand colours, fonts, textures and the handful of shared materials. In the editor every material is
    /// saved under Assets/ASCEND/Resources/ASCEND/Generated so Quest builds keep the shaders.</summary>
    public static class Palette
    {
        public static readonly Color Bg = Hex("08070D");
        public static readonly Color Teal = Hex("2DE2E6");
        public static readonly Color Magenta = Hex("FF2E97");
        public static readonly Color Gold = Hex("FFC857");
        public static readonly Color Violet = Hex("9B6BFF");
        public static readonly Color Mint = Hex("5CFFB0");
        public static readonly Color Silver = Hex("C8CEDC");
        public static readonly Color Text = Hex("F2EEFA");
        public static readonly Color Muted = Hex("9A94B0");
        public static readonly Color Coral = Hex("FF8C78");
        public static readonly Color Red = Hex("E0314B");
        public static readonly Color Dark = Hex("15111F");
        public static readonly Color Darker = Hex("0D0A14");
        public static readonly Color Metal = Hex("2A2438");
        // UI skin accents: the selection block, money up / money down
        public static readonly Color Lime = Hex("A6EB3F");
        public static readonly Color Money = Hex("45F07A");
        public static readonly Color Loss = Hex("FF4B6A");

        public static Color Hex(string h) { Color c; ColorUtility.TryParseHtmlString("#" + h, out c); return c; }
        public static Color A(Color c, float a) { c.a = a; return c; }

        public static Color RarityColor(Rarity r)
        {
            switch (r)
            {
                case Rarity.Rare: return Teal;
                case Rarity.Epic: return Magenta;
                case Rarity.Legendary: return Gold;
                case Rarity.Mythic: return Coral;
                default: return Silver;
            }
        }

        public static Color TierColor(Tier t)
        {
            switch (t)
            {
                case Tier.Contender: return Teal;
                case Tier.Elite: return Magenta;
                case Tier.Legend: return Gold;
                case Tier.Mythic: return Coral;
                default: return Silver;
            }
        }

        // ---------------------------------------------------------------- fonts (TextMeshPro SDF)
        static TMP_FontAsset _display, _mono, _body, _fallback;
        public static TMP_FontAsset Display { get { return _display ? _display : (_display = LoadFont("Bebas SDF")); } }
        public static TMP_FontAsset Mono { get { return _mono ? _mono : (_mono = LoadFont("SpaceMono SDF")); } }
        public static TMP_FontAsset Body { get { return _body ? _body : (_body = LoadFont("Inter SDF")); } }
        static TMP_FontAsset _ui;
        /// <summary>Chunky rounded UI face (Lilita One, SIL OFL). Falls back to the display face until ASCEND → Rebuild Fonts has run.</summary>
        public static TMP_FontAsset UI { get { if (_ui) return _ui; var f = Resources.Load<TMP_FontAsset>("ASCEND/Fonts/TMP/Lilita SDF"); return _ui = f != null ? f : Display; } }

        static TMP_FontAsset LoadFont(string n)
        {
            var f = Resources.Load<TMP_FontAsset>("ASCEND/Fonts/TMP/" + n);
            if (f != null) return f;
            if (_fallback == null) _fallback = TMP_Settings.defaultFontAsset;
            if (_fallback == null) _fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            return _fallback;
        }

        // ---------------------------------------------------------------- textures
        static readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();
        public static Texture2D Tex(string name)
        {
            Texture2D t;
            if (_tex.TryGetValue(name, out t) && t != null) return t;
            t = Resources.Load<Texture2D>("ASCEND/Textures/" + name);
            if (t == null) t = Texture2D.whiteTexture;
            _tex[name] = t;
            return t;
        }

        public static Texture2D Card(Rarity r)
        {
            string n = r == Rarity.None ? "common" : r.ToString().ToLowerInvariant();
            return Resources.Load<Texture2D>("ASCEND/Cards/" + n);
        }

        // ---------------------------------------------------------------- materials
        public const string GenFolder = "Assets/ASCEND/Resources/ASCEND/Generated";
        static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();

        /// <summary>The main vertex-coloured material (albedo + emission in vertex colour).</summary>
        public static Material Neon { get { return Get("m_neon", () => NeonMat(null, 0.35f, 0f)); } }
        /// <summary>Same as Neon but with a strong reflection of the baked probe (floors, bar tops, tables).</summary>
        public static Material Glossy { get { return Get("m_glossy", () => NeonMat(null, 0.2f, 0.55f)); } }
        // ---- Casino FREE style: flat, softly lit colours like the pack's palette material (no photo textures). The
        // carpet is the pack's own Carpet_4 with its normal map; everything else is a plain colour × vertex colour.
        public static Material Marble { get { return Get("m_marble", () => Flat(Hex("3A3150"), 0.05f, 0.35f, 0.55f)); } }
        /// <summary>Casino carpet: the Casino FREE carpet texture, one motif every ~5 m (floor UVs tile every 1.6 m).</summary>
        public static Material Carpet { get { return Get("m_carpet", () => PackCarpet()); } }
        public static Material Wood { get { return Get("m_wood", () => Flat(Hex("8A5634"), 0.04f, 0.15f, 0.4f)); } }
        public static Material Leather { get { return Get("m_leather", () => Flat(Hex("B3283A"), 0.06f, 0.1f, 0.35f)); } }
        public static Material BrushedMetal { get { return Get("m_metal", () => Flat(Hex("BFC3CE"), 0.04f, 0.4f, 0.55f, 0.6f)); } }
        public static Material Gold3 { get { return Get("m_gold", () => Flat(Hex("F2B64A"), 0.04f, 0.45f, 0.65f, 0.85f)); } }
        /// <summary>Back of the world-space panels: a dark plate so their text never shows through mirrored.</summary>
        public static Material PanelBack { get { return Get("m_panelback", () => Flat(Hex("241D38"), 0.02f, 0.02f, 0.3f)); } }
        public static Material Concrete { get { return Get("m_concrete", () => Flat(Hex("8E8A99"), 0.03f, 0.03f, 0.3f)); } }
        public static Material Plate { get { return Get("m_plate", () => Flat(Hex("A3A1B2"), 0.03f, 0.2f, 0.4f, 0.3f)); } }
        public static Material DecoPanel { get { return Get("m_deco", () => Flat(Hex("5A4683"), 0.05f, 0.2f, 0.4f)); } }
        public static Material Hazard { get { return Get("m_hazard", () => { var m = NeonMat(Tex("PBR/hazard_albedo"), 0.05f, 0.05f); m.SetFloat("_Smoothness", 0.35f); return m; }); } }
        /// <summary>Glossy vertex-coloured plastic/lacquer with a fine detail normal so big flat surfaces catch light.</summary>
        public static Material Lacquer { get { return Get("m_lacquer", () => { var m = NeonMat(null, 0.2f, 0.6f); Detail(m, "PBR/metal_normal", 3f, 0.25f); return m; }); } }

        /// <summary>Character material (no vertex colours): optional texture, tint, emission, smoothness and rim.</summary>
        public static Material Character(string key, Texture tex, Color tint, float emit, float smooth, float rim)
        {
            return Get("m_char_" + key, () =>
            {
                var m = NeonMat(tex, rim, smooth * 0.5f);
                m.SetFloat("_UseVertexColor", 0f);
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_EmitAmount", emit);
                m.SetFloat("_EmissionBoost", emit > 0f ? 1.8f : 1f);
                m.SetFloat("_Smoothness", smooth);
                m.SetFloat("_Specular", 0.6f);
                m.SetColor("_RimColor", Color.Lerp(tint, Violet, 0.5f));
                return m;
            });
        }

        /// <summary>Flat-colour lit material in the Casino FREE look (vertex colour × tint, soft highlight, small reflection).</summary>
        public static Material Flat(Color tint, float rim, float reflect, float smooth, float metal = 0f)
        {
            var m = NeonMat(null, rim, reflect);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Specular", 0.5f);
            return m;
        }

        static Material PackCarpet()
        {
            Texture2D tex = null, nrm = null;
#if UNITY_EDITOR
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ithappy/Casino_Free/Textures/Carpet_4.png");
            nrm = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ithappy/Casino_Free/Textures/Carpet_Normal.png");
#endif
            if (tex == null) return Flat(Hex("3B3245"), 0f, 0f, 0.15f);
            var m = NeonMat(tex, 0f, 0f);
            m.SetTextureScale("_BaseMap", new Vector2(0.3f, 0.3f));
            if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", 0.6f); m.SetFloat("_UseNormal", 1); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", 0.15f);
            m.SetFloat("_Specular", 0.2f);
            return m;
        }

        /// <summary>Lit PBR material from a generated texture set: albedo + normal + mask (+ optional vertex tint).</summary>
        public static Material Pbr(string set, float tiling, float rim, float reflect, float bump, float smooth)
        {
            var m = NeonMat(Tex("PBR/" + set + "_albedo"), rim, reflect);
            m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            var nrm = Resources.Load<Texture2D>("ASCEND/Textures/PBR/" + set + "_normal");
            if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", bump); m.SetFloat("_UseNormal", 1); m.EnableKeyword("_NORMALMAP"); }
            var msk = Resources.Load<Texture2D>("ASCEND/Textures/PBR/" + set + "_mask");
            if (msk != null) { m.SetTexture("_MaskMap", msk); m.SetFloat("_UseMask", 1); m.EnableKeyword("_MASKMAP"); }
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Specular", 0.6f);
            return m;
        }

        /// <summary>Adds a tiling detail normal (felt fibres, brushed micro-scratches) to a material.</summary>
        public static void Detail(Material m, string tex, float tiling, float strength)
        {
            var d = Resources.Load<Texture2D>("ASCEND/Textures/" + tex);
            if (d == null) return;
            m.SetTexture("_DetailNormalMap", d);
            m.SetTextureScale("_DetailNormalMap", new Vector2(tiling, tiling));
            m.SetFloat("_DetailNormalScale", strength);
            m.SetFloat("_UseDetail", 1); m.EnableKeyword("_DETAIL");
        }
        public static Material CityWindows { get { return Get("m_city", () => { var m = NeonMat(Tex("city_windows"), 0f, 0f); m.SetFloat("_EmitAmount", 0.3f); m.SetFloat("_UseVertexColor", 1f); m.SetFloat("_EmissionBoost", 1f); m.SetFloat("_Smoothness", 0.5f); return m; }); } }
        public static Material HandSkin { get { return Get("m_hand", () => { var m = NeonMat(null, 1.2f, 0.25f); m.SetFloat("_UseVertexColor", 0f); m.SetColor("_BaseColor", Hex("1C1830")); m.SetColor("_RimColor", Magenta); m.SetFloat("_RimPower", 2.2f); return m; }); } }
        public static Material Glow { get { return Get("m_glow", () => GlowMat(Tex("glow"), 1f)); } }
        public static Material Beam { get { return Get("m_beam", () => GlowMat(Tex("beam"), 1f)); } }
        public static Material FlatGlow { get { return Get("m_flatglow", () => GlowMat(Texture2D.whiteTexture, 1f)); } }
        public static Material Fade { get { return Get("m_fade", () => OverlayMat(Texture2D.whiteTexture, 8f, Color.black)); } }
        public static Material ParticleChips { get { return Get("m_pchips", () => OverlayMat(Tex("chips"), 4f, Color.white)); } }
        public static Material ParticleConfetti { get { return Get("m_pconf", () => OverlayMat(Texture2D.whiteTexture, 4f, Color.white)); } }
        public static Material Sky { get { return Get("m_sky", () => new Material(FindShader("ASCEND/Sky", "Skybox/Procedural"))); } }

        public static Material Textured(string texName, float emit = 0f, float reflect = 0f)
        {
            return Get("m_tex_" + texName + "_" + Mathf.RoundToInt(emit * 10) + "_" + Mathf.RoundToInt(reflect * 10), () =>
            {
                var m = NeonMat(Tex(texName), 0.15f, reflect);
                m.SetFloat("_EmitAmount", emit);
                // printed felt layouts get a woven fibre normal so the cloth reads as fabric up close
                if (texName.StartsWith("felt")) { Detail(m, "PBR/felt_normal", 9f, 0.55f); m.SetFloat("_Smoothness", 0.15f); m.SetFloat("_Specular", 0.15f); }
                return m;
            });
        }

        /// <summary>Uncached emissive material for a runtime texture (LED walls, screens).</summary>
        public static Material ScreenMat(Texture tex) { var m = NeonMat(tex, 0f, 0f); m.SetFloat("_EmissionBoost", 1.4f); return m; }

        public static Material CardMat(Rarity r)
        {
            if (r == Rarity.None) r = Rarity.Common;
            return Get("m_nft_" + r, () =>
            {
                var m = NeonMat(Card(r), 0f, 0f);
                m.SetFloat("_EmitAmount", 1f);
                m.SetFloat("_EmissionBoost", 1.1f);
                return m;
            });
        }

        static Material NeonMat(Texture tex, float rim, float reflect)
        {
            var m = new Material(FindShader("ASCEND/Neon", "Universal Render Pipeline/Unlit"));
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetFloat("_RimStrength", rim * 0.4f);
            m.SetFloat("_Reflect", reflect);
            m.enableInstancing = true;
            return m;
        }

        static Material GlowMat(Texture tex, float intensity)
        {
            var m = new Material(FindShader("ASCEND/Glow", "Universal Render Pipeline/Particles/Unlit"));
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Intensity", intensity);
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Unlit, alpha-blended, depth-tested: speech-bubble plates, thought clouds.</summary>
        public static Material Translucent(Color c) { var m = OverlayMat(Texture2D.whiteTexture, 4f, c); m.renderQueue = 2990; return m; }
        static Material OverlayMat(Texture tex, float zTest, Color c)
        {
            var m = new Material(FindShader("ASCEND/Overlay", "Universal Render Pipeline/Unlit"));
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_ZTest", zTest);
            if (zTest < 8f) m.renderQueue = 3000;
            return m;
        }

        public static Shader FindShader(string a, string b)
        {
            var s = Shader.Find(a);
            if (s == null) s = Shader.Find(b);
            return s;
        }

        /// <summary>Style generation of the saved materials ("_cf" = Casino FREE look). Older generations stay on disk unused.</summary>
        const string Gen = "_cf";

        static Material Get(string key, System.Func<Material> make)
        {
            key += Gen;
            Material m;
            if (_mats.TryGetValue(key, out m) && m != null) return m;
            m = Resources.Load<Material>("ASCEND/Generated/" + key);
#if UNITY_EDITOR
            if (m == null && !Application.isPlaying)
            {
                EnsureFolder();
                m = make(); m.name = key;
                AssetDatabase.CreateAsset(m, GenFolder + "/" + key + ".mat");
            }
#endif
            if (m == null) { m = make(); m.name = key; }
            _mats[key] = m;
            return m;
        }

        /// <summary>Creates every material once in the editor so that runtime lookups find saved assets.</summary>
        public static void Prewarm()
        {
            var _ = Neon; _ = Glossy; _ = Marble; _ = Carpet; _ = Wood; _ = Leather; _ = BrushedMetal; _ = Gold3; _ = Concrete; _ = Plate; _ = DecoPanel; _ = Hazard; _ = Lacquer; _ = CityWindows; _ = HandSkin; _ = Glow; _ = Beam; _ = FlatGlow; _ = Fade;
            _ = ParticleChips; _ = ParticleConfetti; _ = Sky;
            for (int r = 1; r <= 5; r++) CardMat((Rarity)r);
            foreach (var t in new[] { "cards", "chips", "felt_roulette", "felt_blackjack", "felt_baccarat", "felt_holdem", "felt_highroller", "felt_dragontiger", "felt_threecard", "felt_sicbo", "felt_craps", "money_wheel", "slot_symbols", "wheel" })
                Textured(t);
            Textured("wheel", 0.3f);
            Textured("cards", 0f, 0f);
        }

        /// <summary>In the editor, stores a procedural mesh as an asset so scenes keep it after reload.</summary>
        public static Mesh PersistMesh(Mesh m, string folder = "Meshes")
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EnsureFolder();
                string dir = GenFolder + "/" + folder;
                if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(GenFolder, folder);
                string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + Safe(m.name) + ".asset");
                AssetDatabase.CreateAsset(m, path);
            }
#endif
            return m;
        }

        static string Safe(string s)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }

#if UNITY_EDITOR
        public static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/ASCEND/Resources")) AssetDatabase.CreateFolder("Assets/ASCEND", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/ASCEND/Resources/ASCEND")) AssetDatabase.CreateFolder("Assets/ASCEND/Resources", "ASCEND");
            if (!AssetDatabase.IsValidFolder(GenFolder)) AssetDatabase.CreateFolder("Assets/ASCEND/Resources/ASCEND", "Generated");
        }
#endif

        public static void ClearCache() { _mats.Clear(); _tex.Clear(); _display = _mono = _body = _ui = null; }
    }
}
