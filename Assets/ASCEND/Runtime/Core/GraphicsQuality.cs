using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace Ascend
{
    /// <summary>Picks the render settings for where the game is running.
    /// Quest 2: higher eye-buffer resolution (sharper text and chips), fixed foveated rendering to pay for it, 4x MSAA,
    /// no HDR/post (keeps 72 fps). Desktop / editor: supersampling, 4x MSAA, HDR, bloom + tonemapping + colour grading.
    /// The player can switch between QUALITY and PERFORMANCE in the menu (settings) and the choice is remembered.</summary>
    public class GraphicsQuality : MonoBehaviour
    {
        public static GraphicsQuality I { get; private set; }
        public enum Preset { Performance, Quality }

        [Tooltip("Global post-processing volume (bloom, tonemapping, grading) created by the world builder")]
        public Volume post;
        [Range(0.8f, 1.6f)] public float questEyeScale = 1.3f;
        [Range(0.5f, 2f)] public float desktopRenderScale = 1.25f;

        public Preset Current { get; private set; }
        bool appliedVR, appliedDesktop;
        float checkUntil;
        static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        void Awake() { I = this; Current = (Preset)PlayerPrefs.GetInt("ascend_gfx", (int)Preset.Quality); }

        void Start() { checkUntil = Time.time + 6f; Apply(); }

        void Update()
        {
            // the headset can come up a few frames after Start
            if (Time.time < checkUntil && XRIn.HasHeadset && !appliedVR) Apply();
        }

        public void Set(Preset p)
        {
            Current = p;
            PlayerPrefs.SetInt("ascend_gfx", (int)p);
            appliedVR = appliedDesktop = false;
            Apply();
            GameState.I?.Notify("GRAPHICS · " + (p == Preset.Quality ? "QUALITY" : "PERFORMANCE"), Palette.Teal);
        }

        public void Toggle() { Set(Current == Preset.Quality ? Preset.Performance : Preset.Quality); }

        void Apply()
        {
            bool vr = XRIn.HasHeadset;
            bool hi = Current == Preset.Quality;
            object urp = GraphicsSettings.currentRenderPipeline;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            QualitySettings.lodBias = hi ? 1.5f : 1f;
            if (vr)
            {
                appliedVR = true;
                XRSettings.eyeTextureResolutionScale = hi ? questEyeScale : 1.0f;
                XRSettings.useOcclusionMesh = true;
                SubsystemManager.GetSubsystems(displays);
                foreach (var d in displays)
                {
                    d.foveatedRenderingLevel = hi ? 1f : 0.6f;
                    d.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
                }
                SetProp(urp, "renderScale", 1f);
                SetProp(urp, "msaaSampleCount", 4);
                SetProp(urp, "supportsHDR", false);
                SetPost(false);
                Application.targetFrameRate = -1;
            }
            else
            {
                appliedDesktop = true;
                SetProp(urp, "renderScale", hi ? desktopRenderScale : 1f);
                SetProp(urp, "msaaSampleCount", hi ? 4 : 2);
                SetProp(urp, "supportsHDR", hi);
                SetPost(hi);
                SetProp(urp, "shadowDistance", hi ? 45f : 0f);
                SetProp(urp, "shadowCascadeCount", hi ? 2 : 1);
                Sun(hi);
                Application.targetFrameRate = 90;
            }
        }

        static Color ambSky, ambEq, ambGround; static bool ambSaved;
        /// <summary>Desktop QUALITY: the sun casts soft shadows (light patches through the windows, people grounded on
        /// the floor) and the ambient comes up a little so the shaded interior stays bright and colourful.</summary>
        static void Sun(bool on)
        {
            Light sun = RenderSettings.sun;
            if (sun == null) foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            if (sun == null) return;
            if (!ambSaved) { ambSky = RenderSettings.ambientSkyColor; ambEq = RenderSettings.ambientEquatorColor; ambGround = RenderSettings.ambientGroundColor; ambSaved = true; }
            sun.shadows = on ? LightShadows.Soft : LightShadows.None;
            sun.shadowStrength = 0.62f; sun.shadowBias = 0.04f; sun.shadowNormalBias = 0.35f;
            float k = on ? 1.14f : 1f;
            RenderSettings.ambientSkyColor = ambSky * k; RenderSettings.ambientEquatorColor = ambEq * k; RenderSettings.ambientGroundColor = ambGround * k;
        }

        /// <summary>URP asset settings through reflection (keeps this script free of the URP asset type graph).</summary>
        static void SetProp(object target, string name, object value)
        {
            if (target == null) return;
            var p = target.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (p == null || !p.CanWrite) return;
            try { p.SetValue(target, System.Convert.ChangeType(value, p.PropertyType), null); } catch { }
        }

        void SetPost(bool on)
        {
            if (post != null) post.enabled = on;
            foreach (var cam in Camera.allCameras)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data == null) continue;
                data.renderPostProcessing = on;
                data.antialiasing = on ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
                data.antialiasingQuality = AntialiasingQuality.Medium;
            }
        }
    }
}
