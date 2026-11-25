using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;

namespace Ascend
{
    /// <summary>Who is "looking" for things that react to the player (dialog boxes, neighbours, the dog): the player's
    /// head, or the trailer camera while a trailer is being filmed.</summary>
    public static class TrailerView
    {
        public static Transform Override;
        /// <summary>How far in front of the trailer lens the dialog box floats (metres).</summary>
        public static float DialogDistance = 1.25f;
        public static Transform Head { get { return Override != null ? Override : Rig.I != null ? Rig.I.Head : null; } }
    }

    /// <summary>Scripted camera for the game trailer. In Play mode it flies its own camera through the real game (live
    /// guests, dealers, slot machines, the raid boss) shot by shot and hands every frame, at a fixed frame rate, to
    /// whoever listens (the editor's trailer recorder writes them to MP4). Game time is locked to the frame rate while
    /// recording, so the footage is perfectly smooth however long each frame takes to render.</summary>
    public class TrailerDirector : MonoBehaviour
    {
        public static event Action<string> ShotStarted;
        public static event Action<string, Texture2D> FrameReady;
        public static event Action<string> ShotEnded;
        public static event Action Finished;

        public int width = 1920, height = 1080, fps = 60;
        public int shutter = 2;          // sub-frames averaged per frame: real motion blur on everything that moves
        /// <summary>Only these shots (comma separated); empty = all.</summary>
        public string only = "";

        Camera cam; RenderTexture rtMsaa, rt; Texture2D tex;
        delegate void PoseAt(float u, out Vector3 pos, out Vector3 look);

        class Shot
        {
            public string name; public int floor; public float dur, fov = 50f;
            public Func<bool> find;          // locates the subject on the active floor; false = skip the shot
            public PoseAt pose;
            public Action<float> at;          // per-frame hook (u) for events such as a win show
            public Action done;               // clean-up once the shot is recorded (or skipped)
            public Func<float, float> fovAt;  // animated zoom (overrides fov)
            public Func<Vector3> focus;       // what the lens focuses on (default: Paco, else the look point)
            public float aperture = 5.6f;     // depth of field: lower = shallower
            public float shake = 1f;          // hand-held drift amount
        }

        IEnumerator Start()
        {
            PlayerPrefs.SetInt("asc2_intro_seen", 1);
            for (float w = 0; (FloorManager.I == null || Rig.I == null || IntroCinematic.Playing) && w < 30f; w += Time.unscaledDeltaTime) yield return null;
            yield return new WaitForSecondsRealtime(2f);

            // our own camera, tagged MainCamera so billboards and the win show face it; the player's camera steps aside
            if (Rig.I != null && Rig.I.Cam != null) Rig.I.Cam.tag = "Untagged";
            var go = new GameObject("TrailerCam");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 1500f; cam.fieldOfView = 50f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            rtMsaa = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "TrailerRT" };
            rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = "TrailerResolve" };
            cam.targetTexture = rtMsaa;
            tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            CinemaLook();
            // everything that reacts to "the player" (dealers greeting, neighbours, dialog boxes) reacts to the lens
            TrailerView.Override = cam.transform;
            // the how-to-play board is for players, not for the film
            foreach (var wb in FindObjectsByType<WelcomeBoard>(FindObjectsInactive.Include, FindObjectsSortMode.None)) wb.gameObject.SetActive(false);

            var wanted = new HashSet<string>();
            foreach (var s in only.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) wanted.Add(s.Trim());
            foreach (var shot in Mafia())
            {
                if (wanted.Count > 0 && !wanted.Contains(shot.name)) continue;
                yield return Record(shot);
                if (shot.done != null) shot.done();
            }
            foreach (var shot in Story())
            {
                if (wanted.Count > 0 && !wanted.Contains(shot.name)) continue;
                yield return Record(shot);
                if (shot.done != null) shot.done();
            }
            foreach (var shot in Shots())
            {
                if (wanted.Count > 0 && !wanted.Contains(shot.name)) continue;
                yield return Record(shot);
            }
            Time.captureFramerate = 0;
            TrailerView.Override = null;
            if (Finished != null) Finished();
        }

        IEnumerator Record(Shot s)
        {
            Time.captureFramerate = 0;
            if (s.floor >= 0 && FloorManager.I.Current != s.floor)
            {
                FloorManager.I.Activate(s.floor, false);
                // let the floor fill up: guests walk in, sit down and start playing
                yield return new WaitForSecondsRealtime(9f);
            }
            if (s.find != null && !s.find()) { Debug.LogWarning("[ASCEND] Trailer: no subject for shot " + s.name); yield break; }
            if (s.at != null) s.at(0f);
            cam.fieldOfView = s.fov;
            int n = Mathf.RoundToInt(s.dur * fps);
            // a second of pre-roll at the start pose so motion and animation are settled when the clip starts
            Vector3 p, l;
            s.pose(0f, out p, out l); Aim(p, l);
            if (s.fovAt != null) cam.fieldOfView = s.fovAt(0f);
            Lens(s, p, l, 0f);
            if (paco != null) ClearView(cam.transform.position, paco.transform.position + Vector3.up * 1.2f, keep);
            yield return new WaitForSecondsRealtime(1f);
            int sub = Mathf.Max(1, shutter);
            Time.captureFramerate = fps * sub;
            if (ShotStarted != null) ShotStarted(s.name);
            int px = width * height;
            if (sub > 1 && (acc == null || acc.Length != px * 3)) { acc = new int[px * 3]; outPx = new Color32[px]; }
            for (int f = 0; f < n; f++)
            {
                if (sub > 1) System.Array.Clear(acc, 0, acc.Length);
                for (int j = 0; j < sub; j++)
                {
                    float ft = f + j / (float)sub;
                    float u = n > 1 ? Mathf.Min(1f, ft / (n - 1)) : 0f;
                    s.pose(u, out p, out l); Aim(p, l);
                    if (s.fovAt != null) cam.fieldOfView = s.fovAt(u);
                    Lens(s, p, l, ft / fps);
                    if (s.at != null) s.at(u);
                    if (paco != null) ClearView(cam.transform.position, paco.transform.position + Vector3.up * 1.2f, keep);
                    yield return new WaitForEndOfFrame();
                    Graphics.Blit(rtMsaa, rt);
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    RenderTexture.active = prev;
                    if (sub > 1)
                    {
                        var c = tex.GetPixels32();
                        for (int i = 0, k = 0; i < px; i++, k += 3) { acc[k] += c[i].r; acc[k + 1] += c[i].g; acc[k + 2] += c[i].b; }
                    }
                }
                if (sub > 1)
                {
                    int half = sub / 2;
                    for (int i = 0, k = 0; i < px; i++, k += 3) outPx[i] = new Color32((byte)((acc[k] + half) / sub), (byte)((acc[k + 1] + half) / sub), (byte)((acc[k + 2] + half) / sub), 255);
                    tex.SetPixels32(outPx);
                }
                tex.Apply(false);
                if (FrameReady != null) FrameReady(s.name, tex);
            }
            Time.captureFramerate = 0;
            if (ShotEnded != null) ShotEnded(s.name);
        }

        // ------------------------------------------------------------------ the film look
        DepthOfField dof;
        int[] acc; Color32[] outPx;

        /// <summary>A trailer-only volume on top of the game's grade: shallow depth of field, filmic tonemapping,
        /// richer contrast, soft vignette and a whisper of grain; plus soft sun shadows through the windows.</summary>
        void CinemaLook()
        {
            var vgo = new GameObject("Trailer Look");
            vgo.transform.SetParent(transform, false);
            var vol = vgo.AddComponent<UnityEngine.Rendering.Volume>();
            vol.isGlobal = true; vol.priority = 50f;
            var prof = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            dof = prof.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Bokeh); dof.focusDistance.Override(4f); dof.focalLength.Override(50f); dof.aperture.Override(5.6f);
            dof.bladeCount.Override(6); dof.bladeCurvature.Override(0.9f);
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var ca = prof.Add<ColorAdjustments>(true); ca.postExposure.Override(0.35f); ca.contrast.Override(16f); ca.saturation.Override(18f);
            var bl = prof.Add<Bloom>(true); bl.threshold.Override(0.9f); bl.intensity.Override(0.9f); bl.scatter.Override(0.7f);
            var vg = prof.Add<Vignette>(true); vg.intensity.Override(0.3f); vg.smoothness.Override(0.45f);
            var fg = prof.Add<FilmGrain>(true); fg.type.Override(FilmGrainLookup.Thin1); fg.intensity.Override(0.12f);
            var smh = prof.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.96f, 0.98f, 1.06f, 0f)); smh.highlights.Override(new Vector4(1.05f, 1.0f, 0.95f, 0f));
            vol.sharedProfile = prof;

            object urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (urp != null)
            {
                var t = urp.GetType();
                var pd = t.GetProperty("shadowDistance"); if (pd != null && pd.CanWrite) pd.SetValue(urp, 55f, null);
                var pc = t.GetProperty("shadowCascadeCount"); if (pc != null && pc.CanWrite) pc.SetValue(urp, 4, null);
            }
            Light sun = RenderSettings.sun;
            if (sun == null) foreach (var lt in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (lt.type == LightType.Directional) { sun = lt; break; }
            if (sun != null) { sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.62f; sun.shadowNormalBias = 0.35f; sun.intensity = 1.3f; }
        }

        /// <summary>Focus pull and a slow hand-held drift (two noise octaves) on every frame.</summary>
        void Lens(Shot s, Vector3 p, Vector3 l, float t)
        {
            Vector3 fp = s.focus != null ? s.focus() : paco != null ? paco.HeadBone.position : l;
            if (dof != null) { dof.focusDistance.value = Mathf.Max(0.3f, Vector3.Dot(fp - p, cam.transform.forward)); dof.aperture.value = s.aperture; dof.focalLength.value = Mathf.Clamp(24f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), 20f, 120f); }
            if (s.shake <= 0f) return;
            float seed = s.name.GetHashCode() % 97;
            float yaw = (Mathf.PerlinNoise(seed, t * 0.35f) - 0.5f) * 0.9f + (Mathf.PerlinNoise(seed + 5f, t * 1.1f) - 0.5f) * 0.25f;
            float pitch = (Mathf.PerlinNoise(seed + 11f, t * 0.3f) - 0.5f) * 0.6f + (Mathf.PerlinNoise(seed + 17f, t * 1.2f) - 0.5f) * 0.18f;
            float roll = (Mathf.PerlinNoise(seed + 23f, t * 0.25f) - 0.5f) * 0.5f;
            cam.transform.rotation = cam.transform.rotation * Quaternion.Euler(pitch * s.shake, yaw * s.shake, roll * s.shake);
        }

        void Aim(Vector3 pos, Vector3 look)
        {
            cam.transform.position = pos;
            var d = look - pos;
            if (d.sqrMagnitude > 1e-6f) cam.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        // ------------------------------------------------------------------ helpers
        static float E(float u) { return u * u * (3f - 2f * u); }               // smoothstep
        static float Y(int floor) { return Tower.Y(floor); }
        static Vector3 Ring(float deg, float r, float y) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r); }

        static Transform FloorRoot(int f) { return FloorManager.I != null && f < FloorManager.I.floors.Length ? FloorManager.I.floors[f] : null; }

        static T Pick<T>(int floor, Func<T, bool> ok = null, int nth = 0) where T : Component
        {
            var root = FloorRoot(floor); if (root == null) return null;
            var all = new List<T>(root.GetComponentsInChildren<T>(false));
            if (ok != null) all = all.FindAll(x => ok(x));
            all.Sort((a, b) => a.name.CompareTo(b.name));
            return all.Count > 0 ? all[Mathf.Clamp(nth, 0, all.Count - 1)] : null;
        }

        static Transform Named(int floor, string n, Func<Transform, bool> ok = null)
        {
            var root = FloorRoot(floor); if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(false)) if (t.name == n && (ok == null || ok(t))) return t;
            return null;
        }

        /// <summary>Camera path in a subject's local frame: from a to b (local positions), looking at la → lb.</summary>
        static PoseAt Local(Func<Transform> subject, Vector3 a, Vector3 b, Vector3 la, Vector3 lb, bool ease = true)
        {
            return (float u, out Vector3 p, out Vector3 l) =>
            {
                var t = subject(); float k = ease ? E(u) : u;
                p = t.TransformPoint(Vector3.Lerp(a, b, k)); l = t.TransformPoint(Vector3.Lerp(la, lb, k));
            };
        }

        /// <summary>Orbit round a subject: angle a0→a1 (deg, local, 0 = in front on -Z), radius r0→r1, height h0→h1.</summary>
        static PoseAt Orbit(Func<Transform> subject, float a0, float a1, float r0, float r1, float h0, float h1, float lookH)
        {
            return (float u, out Vector3 p, out Vector3 l) =>
            {
                var t = subject(); float k = E(u);
                float a = Mathf.Lerp(a0, a1, k) * Mathf.Deg2Rad, r = Mathf.Lerp(r0, r1, k);
                p = t.TransformPoint(new Vector3(Mathf.Sin(a) * r, Mathf.Lerp(h0, h1, k), -Mathf.Cos(a) * r));
                l = t.TransformPoint(new Vector3(0, lookH, 0));
            };
        }

        // ------------------------------------------------------------------ the shot list (grouped by floor)
        IEnumerable<Shot> Shots()
        {
            Transform tA = null, tB = null; bool waved = false;

            // ---- rooftop / exterior (floor 9)
            yield return new Shot
            {
                name = "exterior", floor = 8, dur = 7f, fov = 42f,
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u);
                    p = Ring(Mathf.Lerp(-40f, 15f, k), Mathf.Lerp(125f, 82f, k), Mathf.Lerp(98f, 84f, k));
                    l = new Vector3(0, Mathf.Lerp(58f, 64f, k), 0);
                }
            };
            yield return new Shot
            {
                name = "roof", floor = 8, dur = 6f, fov = 55f,
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u), y = Y(8);
                    p = Ring(Mathf.Lerp(150f, 195f, k), Mathf.Lerp(24f, 17f, k), y + Mathf.Lerp(16f, 4.5f, k));
                    l = new Vector3(0, y + Mathf.Lerp(0f, 4f, k), -8f);
                }
            };
            yield return new Shot
            {
                name = "boss", floor = 8, dur = 6f, fov = 48f, aperture = 11f,
                find = () =>
                {
                    if ((tA = Named(8, "TheHouse")) == null) return false;
                    // a clean portrait: the fight's shield discs and orbiting chips would fly across the lens
                    foreach (Transform c in tA) if (c.name == "Shields" || c.name == "ChipOrbit" || c.name.StartsWith("CardPylon")) c.gameObject.SetActive(false);
                    return true;
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u);
                    var b = tA.position;
                    var fwd = -tA.forward; fwd.y = 0; fwd.Normalize();   // its face (-Z) looks at the arena
                    var side = Vector3.Cross(Vector3.up, fwd);
                    p = b + fwd * Mathf.Lerp(16f, 11.5f, k) + side * Mathf.Lerp(-4f, 2.5f, k) + Vector3.down * Mathf.Lerp(3.6f, 2.6f, k);
                    l = b + Vector3.up * Mathf.Lerp(-1.2f, -0.4f, k);
                }
            };

            // ---- lobby (floor 1)
            yield return new Shot
            {
                // deep focus (the whole room sharp, front to back) on a slow rising glide across the floor
                name = "lobby", floor = 0, dur = 6f, fov = 60f, aperture = 16f, shake = 0.5f,
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u);
                    // from the outer ring, across the open floor to the vault and the slot banks (never through the core's columns)
                    p = Ring(Mathf.Lerp(-78f, -56f, k), Mathf.Lerp(17.2f, 16.2f, k), Mathf.Lerp(2.1f, 2.9f, k));
                    l = Ring(Mathf.Lerp(-2f, 16f, k), 13.5f, 1.5f);
                },
                focus = () => Ring(8f, 13.5f, 1.5f)
            };
            yield return new Shot
            {
                name = "vault", floor = 0, dur = 5f, fov = 45f,
                find = () => (tA = Named(0, "Turntable", t => t.parent == FloorRoot(0))) != null,
                pose = Orbit(() => tA, -40f, 30f, 5.2f, 4.2f, 2.2f, 1.5f, 0.9f)
            };
            yield return new Shot
            {
                name = "bar", floor = 0, dur = 5f, fov = 50f,
                find = () => (tA = Pick<NeonBar>(0)?.transform) != null,
                pose = Local(() => tA, new Vector3(-2.4f, 1.55f, -4.2f), new Vector3(1.8f, 1.45f, -3.4f), new Vector3(-0.4f, 1.3f, 1.2f), new Vector3(0.4f, 1.3f, 1.2f))
            };
            yield return new Shot
            {
                name = "dance", floor = 0, dur = 6f, fov = 55f,
                find = () => (tA = Pick<DJStage>(0)?.transform) != null,
                pose = Local(() => tA, new Vector3(1.5f, 1.6f, -12.5f), new Vector3(-0.5f, 3.2f, -8.5f), new Vector3(0, 1.4f, -4f), new Vector3(0, 1.9f, 0f))
            };
            yield return new Shot
            {
                name = "cashier", floor = 0, dur = 4.5f, fov = 48f,
                find = () => (tA = Named(0, "Table_06")) != null,
                pose = Local(() => tA, new Vector3(2.6f, 1.5f, 3.4f), new Vector3(-1.2f, 1.25f, 2.6f), new Vector3(0, 0.9f, 0), new Vector3(0.3f, 0.9f, 0))
            };
            yield return new Shot
            {
                name = "lounge", floor = 0, dur = 5f, fov = 50f,
                find = () => (tA = Named(0, "Couch_05")) != null,
                pose = Local(() => tA, new Vector3(-1.6f, 1.25f, 3.6f), new Vector3(1.4f, 1.1f, 3.1f), new Vector3(0, 0.7f, 0), new Vector3(0, 0.75f, 0))
            };

            // ---- The Classics (floor 2)
            yield return new Shot
            {
                name = "roulette", floor = 1, dur = 6f, fov = 42f,
                find = () =>
                {
                    tA = Pick<Roulette>(1)?.transform; if (tA == null) return false;
                    tB = null; foreach (var t in tA.GetComponentsInChildren<Transform>()) if (t.name == "Wheel") { tB = t.parent; break; }
                    if (tB == null) tB = tA; return true;
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    // orbit the wheel (its rig: the wheel itself spins) in the table's frame, above the stools
                    float k = E(u), a = Mathf.Lerp(-70f, 10f, k) * Mathf.Deg2Rad, r = Mathf.Lerp(1.25f, 0.95f, k);
                    var c = tB.position;
                    p = c + tA.TransformDirection(new Vector3(Mathf.Sin(a) * r, 0, -Mathf.Cos(a) * r)) + Vector3.up * Mathf.Lerp(0.75f, 0.55f, k);
                    l = c + Vector3.up * 0.05f;
                }
            };
            yield return new Shot
            {
                name = "blackjack", floor = 1, dur = 5.5f, fov = 46f,
                find = () => { waved = false; return (tA = Pick<Blackjack>(1)?.transform) != null; },
                // the dealer pulls four cards out of the shoe
                at = u => { if (waved || u < 0.04f) return; waved = true; tA.GetComponent<Blackjack>().DemoDeal(); },
                pose = Local(() => tA, new Vector3(-1.4f, 1.95f, -0.85f), new Vector3(1.2f, 1.85f, -0.95f), new Vector3(0, 0.95f, 0.2f), new Vector3(0, 0.95f, 0.25f))
            };
            yield return new Shot
            {
                name = "classics", floor = 1, dur = 6f, fov = 60f,
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u), y = Y(1);
                    p = Ring(Mathf.Lerp(150f, 110f, k), Mathf.Lerp(19.5f, 17f, k), y + Mathf.Lerp(5.6f, 3.2f, k));
                    l = Ring(Mathf.Lerp(60f, 30f, k), 8f, y + 0.6f);
                }
            };

            // ---- The Card Room (floor 3)
            yield return new Shot
            {
                name = "poker", floor = 2, dur = 5.5f, fov = 50f,
                find = () => (tA = Pick<PokerTable>(2)?.transform) != null,
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u), a = Mathf.Lerp(-30f, 40f, k) * Mathf.Deg2Rad;
                    p = tA.TransformPoint(new Vector3(Mathf.Sin(a) * 1.2f, Mathf.Lerp(3.6f, 3.0f, k), -Mathf.Cos(a) * 1.2f));
                    l = tA.TransformPoint(new Vector3(0, 0.9f, 0.1f));
                }
            };

            // ---- Fast Rounds (floor 5)
            yield return new Shot
            {
                name = "plinko", floor = 4, dur = 4.5f, fov = 48f,
                find = () => (tA = Pick<Plinko>(4)?.transform) != null,
                pose = Local(() => tA, new Vector3(-1.3f, 1.7f, -2.6f), new Vector3(0.6f, 1.6f, -2.0f), new Vector3(0, 1.4f, 0.3f), new Vector3(0, 1.4f, 0.3f))
            };

            // ---- Luck & Loot (floor 6): slot banks and a big win
            yield return new Shot
            {
                name = "slots", floor = 5, dur = 6f, fov = 52f,
                find = () => (tA = Pick<Slots>(5, null, 2)?.transform) != null,
                pose = Local(() => tA, new Vector3(-3.4f, 1.75f, -2.6f), new Vector3(2.6f, 1.55f, -2.2f), new Vector3(-1.2f, 1.5f, 0.3f), new Vector3(1.2f, 1.5f, 0.3f))
            };
            bool fired = false;
            yield return new Shot
            {
                name = "win", floor = 5, dur = 7f, fov = 46f,
                // a machine nobody is sitting at, so no head gets between the camera and the reels
                find = () => { fired = false; return (tB = Pick<Slots>(5, m => m.PlayerSeat == null || m.PlayerSeat.occupant == null, 2)?.transform) != null; },
                pose = Local(() => tB, new Vector3(0.45f, 1.75f, -2.3f), new Vector3(0.1f, 1.72f, -3.0f), new Vector3(0, 1.5f, 0.3f), new Vector3(0, 1.62f, 0.3f)),
                at = u =>
                {
                    if (fired || u < 0.12f) return;
                    fired = true;
                    Juice.Celebrate(tB.TransformPoint(new Vector3(0, 0, -0.9f)), 48000, 32f);
                }
            };

            // ---- Sky Lounge (floor 8)
            yield return new Shot
            {
                name = "skylounge", floor = 7, dur = 5.5f, fov = 55f,
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u), y = Y(7);
                    p = Ring(Mathf.Lerp(-120f, -80f, k), Mathf.Lerp(18f, 15f, k), y + Mathf.Lerp(2.2f, 1.6f, k));
                    l = Ring(Mathf.Lerp(-30f, 10f, k), 9f, y + 1.1f);
                }
            };
        }

        // ================================================================== the story cut: PACO
        // Paco (the high-roller look: gold chain, loud shirt) comes to the casino to fix all his problems in one night.
        // Shots named p_* are acted by him; the edit mixes them with the ambient shots above.
        AvatarBody paco;
        DonGun donGun;

        AvatarBody SpawnPaco(int floor, Vector3 world, float yaw) { return SpawnPacoIn(FloorRoot(floor), world, yaw, true); }

        AvatarBody SpawnPacoIn(Transform root, Vector3 world, float yaw, bool nav)
        {
            KillPaco();
            NavMeshHit h;
            if (nav && NavMesh.SamplePosition(world, out h, 1.5f, NavMesh.AllAreas)) world = h.position;
            var lib = AssetLibrary.I;
            paco = lib != null && lib.android != null
                ? (AvatarBody)HumanoidBody.CreateFrom(lib.android, lib.humanoidController, lib, "Paco", root, root.InverseTransformPoint(world), yaw - root.eulerAngles.y, Rarity.Legendary, RoninRole.Player, 1.76f, HumanoidBody.Kind.Extra)
                : Avatars.CreateStyled("Paco", root, root.InverseTransformPoint(world), yaw - root.eulerAngles.y, Rarity.Legendary, AvatarStyle.Android, 1.78f);
            paco.transform.rotation = Quaternion.Euler(0, yaw, 0);
            { var phb = paco as HumanoidBody; if (phb != null) phb.idleSet = new[] { "Idle" }; }   // a calm, clean idle on camera
            // Paco's signature look: a loud golden suit (nobody else in the casino wears one)
            foreach (var r in paco.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                if (!(n.Contains("Jacket") || n.Contains("Outwear") || n.Contains("Hat") || n.Contains("Pants"))) continue;
                var gold = n.Contains("Pants") ? Palette.Character("paco_pants", null, Palette.Hex("2A1E3C"), 0.05f, 0.5f, 0.3f)
                                               : Palette.Character("paco_gold", null, Palette.Hex("F2B132"), 0.15f, 0.6f, 0.35f);
                var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = gold; r.sharedMaterials = ms;
                r.SetPropertyBlock(null);
            }
            if (!nav) return paco;
            // guests walk round him like round anyone else
            var ob = paco.gameObject.AddComponent<NavMeshObstacle>();
            ClearAround(world, 0.7f);
            ob.shape = NavMeshObstacleShape.Capsule; ob.radius = 0.3f; ob.height = 1.8f; ob.center = new Vector3(0, 0.9f, 0);
            return paco;
        }

        void KillPaco()
        {
            if (paco == null) return;
            foreach (var st in Seat.All) if (st != null && st.occupant == paco) st.occupant = null;
            Destroy(paco.gameObject); paco = null;
        }

        /// <summary>Clears a seat for Paco (a guest sitting there is sent off for a walk).</summary>
        static void Vacate(Seat s)
        {
            if (s == null || s.occupant == null) return;
            var o = s.occupant; s.Leave();
            var bot = o.GetComponent<BotAvatar>();
            if (bot != null) bot.gameObject.SetActive(false);
        }

        /// <summary>Guests standing (or sitting) between the camera and Paco step out of the shot.</summary>
        readonly HashSet<AvatarBody> keep = new HashSet<AvatarBody>();
        static void ClearView(Vector3 cam, Vector3 target, HashSet<AvatarBody> keep)
        {
            var seg = target - cam; float len = seg.magnitude; if (len < 1e-3f) return; var dir = seg / len;
            for (int i = BotAvatar.All.Count - 1; i >= 0; i--)
            {
                var b = BotAvatar.All[i]; if (b == null || !b.gameObject.activeInHierarchy) continue;
                if (keep.Contains(b.GetComponent<AvatarBody>())) continue;
                var c = b.transform.position + Vector3.up * 1.1f;
                float t = Mathf.Clamp(Vector3.Dot(c - cam, dir), 0f, len - 0.45f);
                var q = cam + dir * t;
                var d = c - q; d.y *= 0.5f;
                if (d.magnitude < 0.6f || (b.transform.position - cam).sqrMagnitude < 1.1f) b.gameObject.SetActive(false);
            }
        }

        static void ClearAround(Vector3 p, float r)
        {
            for (int i = BotAvatar.All.Count - 1; i >= 0; i--)
            {
                var b = BotAvatar.All[i]; if (b == null || !b.gameObject.activeInHierarchy) continue;
                var d = b.transform.position - p; d.y = 0;
                if (d.magnitude < r) b.gameObject.SetActive(false);
            }
        }

        void SitPaco(Seat s)
        {
            Vacate(s);
            s.occupant = paco; paco.SitOn(s);
        }

        /// <summary>Walks Paco along a NavMesh path (start → end) at a steady pace; returns the path length.</summary>
        static List<Vector3> PathBetween(Vector3 a, Vector3 b)
        {
            var list = new List<Vector3>();
            NavMeshHit h;
            if (NavMesh.SamplePosition(a, out h, 2f, NavMesh.AllAreas)) a = h.position;
            if (NavMesh.SamplePosition(b, out h, 2f, NavMesh.AllAreas)) b = h.position;
            var path = new NavMeshPath();
            if (NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.corners.Length > 1) list.AddRange(path.corners);
            else { list.Add(a); list.Add(b); }
            return list;
        }

        static Vector3 Along(List<Vector3> pts, float dist, out Vector3 dir)
        {
            dir = Vector3.forward;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var seg = pts[i + 1] - pts[i]; float len = seg.magnitude;
                if (len < 1e-4f) continue;
                dir = seg / len;
                if (dist <= len) return pts[i] + dir * dist;
                dist -= len;
            }
            return pts[pts.Count - 1];
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        readonly List<GameObject> extras = new List<GameObject>();
        void KillExtras() { foreach (var g in extras) if (g != null) Destroy(g); extras.Clear(); keep.Clear(); }

        IEnumerable<Shot> Story()
        {
            Transform tA = null; Seat seat = null; Slots machine = null; AvatarBody cashier = null; float slotSide = 1f, seatZ = -1f;
            List<Vector3> route = null; Vector3 heading = Vector3.forward, camAt = Vector3.zero, lineSpot = Vector3.zero;
            bool[] cue = new bool[6];
            Action reset = () => { for (int i = 0; i < cue.Length; i++) cue[i] = false; };
            Func<int, float, float, bool> Cue = (i, u, at) => { if (cue[i] || u < at) return false; cue[i] = true; return true; };
            Func<Vector3> pacoHead = () => paco.HeadBone.position;

            // ---- 1. steadicam behind Paco as he strides out of the lift into the lobby
            yield return new Shot
            {
                name = "p_arrive", floor = 0, dur = 7f, fov = 42f, aperture = 2.8f, shake = 1.2f,
                find = () =>
                {
                    reset(); float y = Y(0);
                    route = PathBetween(Ring(-48f, 4.9f, y), Ring(-48f, 12.5f, y));
                    heading = Flat(route[route.Count - 1] - route[0]);
                    SpawnPaco(0, route[0], Quaternion.LookRotation(heading).eulerAngles.y);
                    return true;
                },
                at = u =>
                {
                    Vector3 dir; var p = Along(route, u * 7f * 1.08f, out dir);
                    paco.transform.position = p;
                    paco.transform.rotation = Quaternion.Slerp(paco.transform.rotation, Quaternion.LookRotation(Flat(dir)), 0.15f);
                    lineSpot = p;
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    var side = Vector3.Cross(Vector3.up, heading);
                    float k = E(u);
                    var want = paco.transform.position - heading * Mathf.Lerp(1.5f, 2.3f, k) + side * 0.55f + Vector3.up * Mathf.Lerp(0.95f, 1.35f, k);
                    camAt = u <= 0f ? want : Vector3.Lerp(camAt, want, 0.25f);
                    p = camAt; l = paco.transform.position + heading * 5f + Vector3.up * Mathf.Lerp(1.6f, 1.3f, k);
                },
                focus = () => paco.transform.position + Vector3.up * 1.4f,
                done = KillPaco
            };

            // ---- 2. he turns to us: "¡Hoy lo arreglo todo!"
            yield return new Shot
            {
                name = "p_line", floor = 0, dur = 4f, fov = 34f, aperture = 2f, shake = 0.8f,
                find = () =>
                {
                    reset(); float y = Y(0);
                    if (route == null) route = PathBetween(Ring(-48f, 4.9f, y), Ring(-48f, 12.5f, y));
                    heading = Flat(route[route.Count - 1] - route[0]);
                    Vector3 d; var p = Along(route, 7f * 1.08f, out d);
                    SpawnPaco(0, p, Quaternion.LookRotation(-heading).eulerAngles.y);
                    ClearAround(p, 2.2f);
                    return true;
                },
                at = u =>
                {
                    paco.lookAtTarget = cam.transform;
                    if (Cue(0, u, 0.12f)) paco.Play(Emote.Talk, 2.2f);
                    if (Cue(1, u, 0.7f)) paco.Play(Emote.Cheer, 1.6f);
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u); var side = Vector3.Cross(Vector3.up, heading);
                    var c = paco.transform.position;
                    p = c - heading * Mathf.Lerp(2.1f, 1.45f, k) + side * 0.25f + Vector3.up * 1.52f;
                    l = c + Vector3.up * 1.45f;
                },
                done = KillPaco
            };

            // ---- 3. roulette: "¡Todo al rojo!" … black.
            yield return new Shot
            {
                name = "p_roulette", floor = 1, dur = 7f, fov = 38f, aperture = 2.2f, shake = 0.7f,
                find = () =>
                {
                    reset();
                    var rl = Pick<Roulette>(1, null, 1); if (rl == null) rl = Pick<Roulette>(1); if (rl == null) return false;
                    seat = null; float best = -1e9f;
                    foreach (var st in rl.GetComponentsInChildren<Seat>()) { float sc = -st.transform.localPosition.x; if (!st.forPlayer && sc > best) { best = sc; seat = st; } }
                    if (seat == null) return false;
                    // a little further from the rail, so his knees stay out of the table
                    seat.transform.position -= seat.transform.forward * 0.14f;
                    SpawnPaco(1, seat.transform.position, seat.transform.eulerAngles.y);
                    SitPaco(seat);
                    return true;
                },
                at = u =>
                {
                    if (Cue(0, u, 0.2f)) paco.Play(Emote.Talk, 1.4f);
                    if (Cue(1, u, 0.55f)) paco.Play(Emote.Facepalm, 3f);
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u);
                    p = seat.W(new Vector3(Mathf.Lerp(0.4f, 0.2f, k), Mathf.Lerp(1.6f, 1.45f, k), Mathf.Lerp(1.95f, 1.35f, k)));
                    l = seat.W(new Vector3(0f, 1.15f, 0.05f));
                },
                done = () => { if (seat != null) seat.transform.position += seat.transform.forward * 0.14f; KillPaco(); }
            };

            // ---- 4. dancing his problems away (the robot, badly), with the floor to himself
            yield return new Shot
            {
                name = "p_dance", floor = 0, dur = 6f, fov = 40f, aperture = 2.4f, shake = 1.1f,
                find = () =>
                {
                    reset();
                    tA = Pick<DJStage>(0)?.transform; if (tA == null) return false;
                    SpawnPaco(0, tA.TransformPoint(new Vector3(0.6f, 0, -5.6f)), tA.eulerAngles.y + 180f);
                    paco.pose = Pose.Dance; paco.danceStyle = 5;
                    ClearAround(paco.transform.position, 1.9f);
                    return true;
                },
                at = u => { paco.lookAtTarget = u > 0.5f ? cam.transform : null; },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u); var c = paco.transform;
                    p = c.position + c.TransformDirection(new Vector3(Mathf.Lerp(-1.1f, -0.3f, k), 0, Mathf.Lerp(3.2f, 2.3f, k))) + Vector3.up * Mathf.Lerp(1.1f, 1.35f, k);
                    l = c.position + Vector3.up * 1.15f;
                },
                done = KillPaco
            };

            // ---- 5. the slot machine: reels, EPIC WIN, "¡LO SABÍA!"
            yield return new Shot
            {
                name = "p_slots", floor = 5, dur = 8f, fov = 40f, aperture = 2.4f, shake = 0.8f,
                find = () =>
                {
                    reset();
                    machine = null; slotSide = 1f;
                    var all = new List<Slots>(FloorRoot(5).GetComponentsInChildren<Slots>());
                    all.Sort((a, b) => a.name.CompareTo(b.name));
                    foreach (var m in all)
                    {
                        if (m.PlayerSeat == null) continue;
                        int near = 0; float nx = 0f;
                        foreach (var o in all) { if (o == m) continue; var lp = m.transform.InverseTransformPoint(o.transform.position); if (lp.magnitude < 1.6f) { near++; nx = lp.x; } }
                        if (near == 1) { machine = m; slotSide = nx > 0 ? -1f : 1f; break; }
                    }
                    if (machine == null) machine = Pick<Slots>(5, m => m.PlayerSeat != null, 3);
                    if (machine == null) return false;
                    seat = machine.PlayerSeat;
                    seatZ = machine.transform.InverseTransformPoint(seat.transform.position).z;
                    SpawnPaco(5, seat.transform.position, seat.transform.eulerAngles.y);
                    SitPaco(seat);
                    return true;
                },
                at = u =>
                {
                    if (Cue(0, u, 0.3f)) Juice.Celebrate(machine.transform.TransformPoint(new Vector3(0, 0, -0.9f)), 48000, 32f);
                    if (Cue(1, u, 0.36f)) paco.Play(Emote.Cheer, 4.5f);
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    var m = machine.transform;
                    if (u < 0.42f)
                    {
                        float k = E(u / 0.42f);
                        p = m.TransformPoint(new Vector3(slotSide * Mathf.Lerp(0.95f, 0.8f, k), 1.65f, seatZ - Mathf.Lerp(1.15f, 0.95f, k)));
                        l = m.TransformPoint(new Vector3(0f, 1.42f, 0.1f));
                    }
                    else
                    {
                        float k = E((u - 0.42f) / 0.58f);
                        p = m.TransformPoint(new Vector3(slotSide * Mathf.Lerp(1.05f, 0.92f, k), Mathf.Lerp(1.4f, 1.32f, k), seatZ + Mathf.Lerp(0.9f, 0.75f, k)));
                        l = paco.HeadBone.position + Vector3.down * 0.12f;
                    }
                },
                focus = () => machine.transform.TransformPoint(new Vector3(0f, 1.4f, 0.1f)),
                done = KillPaco
            };

            // ---- 6-8. the cashier: over-the-shoulder exchange, then the slow push-in on "¿Cómo?"
            Func<bool> cashierScene = () =>
            {
                reset();
                tA = Named(0, "Table_06"); if (tA == null) return false;
                SpawnPaco(0, tA.TransformPoint(new Vector3(0.1f, 0, 0.9f)), tA.eulerAngles.y + 180f);
                var root = FloorRoot(0);
                cashier = Avatars.Create("Cajera", root, root.InverseTransformPoint(tA.TransformPoint(new Vector3(0.1f, 0, -0.62f))), tA.eulerAngles.y - root.eulerAngles.y, Rarity.Epic, RoninRole.Host, 1.68f);
                extras.Add(cashier.gameObject); keep.Add(cashier);
                // the regular cashiers step out of this scene
                foreach (var b in root.GetComponentsInChildren<AvatarBody>())
                    if (b != paco && b != cashier && (b.transform.position - tA.position).sqrMagnitude < 2.6f * 2.6f) b.gameObject.SetActive(false);
                paco.lookAtTarget = cashier.HeadBone; cashier.lookAtTarget = paco.HeadBone;
                return true;
            };
            yield return new Shot
            {
                name = "p_cashier", floor = 0, dur = 4f, fov = 36f, aperture = 2f, shake = 0.7f,
                find = cashierScene,
                at = u => { if (Cue(0, u, 0.08f)) paco.Play(Emote.Talk, 2f); },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    // over the cashier's shoulder onto Paco
                    float k = E(u);
                    p = tA.TransformPoint(new Vector3(Mathf.Lerp(0.5f, 0.42f, k), Mathf.Lerp(1.6f, 1.56f, k), Mathf.Lerp(-1.3f, -1.12f, k)));
                    l = paco.HeadBone.position + Vector3.down * 0.05f;
                },
                done = () => { KillPaco(); KillExtras(); }
            };
            yield return new Shot
            {
                name = "p_cajera", floor = 0, dur = 4.5f, fov = 36f, aperture = 2f, shake = 0.7f,
                find = cashierScene,
                at = u => { if (Cue(0, u, 0.05f)) cashier.Play(Emote.Talk, 3.4f); },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    // reverse angle: over Paco's shoulder onto the cashier
                    float k = E(u);
                    p = tA.TransformPoint(new Vector3(Mathf.Lerp(0.62f, 0.5f, k), 1.7f, Mathf.Lerp(1.75f, 1.6f, k)));
                    l = cashier.HeadBone.position + Vector3.down * 0.05f;
                },
                focus = () => cashier.HeadBone.position,
                done = () => { KillPaco(); KillExtras(); }
            };
            yield return new Shot
            {
                name = "p_como", floor = 0, dur = 4f, fov = 30f, aperture = 1.8f, shake = 0.4f,
                find = cashierScene,
                at = u =>
                {
                    paco.lookAtTarget = cashier.HeadBone;
                    if (Cue(0, u, 0.5f)) paco.Play(Emote.Facepalm, 2.2f);
                },
                // the slow, dramatic push-in of a telenovela
                fovAt = u => Mathf.Lerp(30f, 14f, E(Mathf.Clamp01(u / 0.45f))),
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    p = tA.TransformPoint(new Vector3(0.7f, 1.58f, -0.55f));
                    l = paco.HeadBone.position + Vector3.down * 0.04f;
                },
                done = () => { KillPaco(); KillExtras(); }
            };

            // ---- 9. the lounge: no problem solved… but he levelled up
            yield return new Shot
            {
                name = "p_lounge", floor = 0, dur = 7f, fov = 40f, aperture = 2.4f, shake = 0.8f,
                find = () =>
                {
                    reset();
                    var t = Named(0, "Sofa Seat 1"); seat = t != null ? t.GetComponent<Seat>() : null;
                    if (seat == null) return false;
                    SpawnPaco(0, seat.transform.position, seat.transform.eulerAngles.y);
                    SitPaco(seat);
                    return true;
                },
                at = u =>
                {
                    if (Cue(0, u, 0.05f)) paco.Play(Emote.Facepalm, 2.8f);
                    if (Cue(1, u, 0.6f)) paco.Play(Emote.Cheer, 2.6f);
                    paco.lookAtTarget = u > 0.55f ? cam.transform : null;
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u);
                    p = seat.W(new Vector3(Mathf.Lerp(0.9f, 0.45f, k), Mathf.Lerp(1.25f, 1.15f, k), Mathf.Lerp(2.6f, 1.9f, k)));
                    l = seat.W(new Vector3(0f, 0.95f, 0.1f));
                },
                done = KillPaco
            };
        }
    
        // ================================================================== the MAFIA MODE cut (trailer 2)
        // Paco wakes up at the caravan park, owes Don Tuchi, rides to the casino, meets the dealer in the lift, and
        // either pays… or takes a trip to the docks. Shots named m_*; floor -1 = off the tower (no floor switch).
        RunWorld mw;
        DialogBox dlg;

        RunWorld MW()
        {
            if (mw == null)
            {
                mw = RunWorld.Build();
                foreach (var st in mw.sites) mw.ShowSite(st, false);
                mw.limbo.gameObject.SetActive(false);
            }
            return mw;
        }
        Vector3 H(float x, float y, float z) { return MW().home.TransformPoint(new Vector3(x, y, z)); }
        void Say(Transform who, string name, params string[] lines)
        {
            // the film's dialogue boxes are laid over in the edit (always sharp, never behind an actor)
            if (!EngineDialogs) return;
            if (dlg == null) dlg = DialogBox.Create();
            dlg.Show(who, name, lines, "", null, true);
        }
        const bool EngineDialogs = false;
        void Hush() { if (dlg != null) dlg.gameObject.SetActive(false); }
        void HomeOnly(bool home, int site = -1, bool limbo = false)
        {
            var w = MW();
            w.home.gameObject.SetActive(home);
            for (int i = 0; i < w.sites.Count; i++) w.ShowSite(w.sites[i], i == site);
            w.limbo.gameObject.SetActive(limbo);
            TrailerView.Override = cam.transform;
        }
        /// <summary>Side-on two-shot of two heads: camera off the line between them (on the side nearer
        /// <paramref name="prefer"/>), <paramref name="dist"/> metres out, nudged toward A so B's face reads.</summary>
        static void TwoShot(Vector3 a, Vector3 b, Vector3 prefer, float dist, float towardA, float k, out Vector3 p, out Vector3 l)
        {
            var mid = (a + b) * 0.5f; var ax = b - a; ax.y = 0; float len = ax.magnitude; ax = len > 1e-4f ? ax / len : Vector3.forward;
            var perp = Vector3.Cross(Vector3.up, ax);
            if (Vector3.Dot(prefer - mid, perp) < 0) perp = -perp;
            p = mid + perp * dist - ax * (len * towardA) + Vector3.up * Mathf.Lerp(0.0f, -0.05f, k);
            l = mid + Vector3.down * 0.12f;
        }
        static float YawTo(Vector3 a, Vector3 b) { var d = b - a; d.y = 0; return Quaternion.LookRotation(d.sqrMagnitude > 1e-6f ? d : Vector3.forward).eulerAngles.y; }

        IEnumerable<Shot> Mafia()
        {
            bool[] cue = new bool[6];
            Action reset = () => { for (int i = 0; i < cue.Length; i++) cue[i] = false; };
            Func<int, float, float, bool> Cue = (i, u, at) => { if (cue[i] || u < at) return false; cue[i] = true; return true; };
            Vector3 a0 = Vector3.zero, a1 = Vector3.zero;
            Transform tA = null; AvatarBody dealer = null; RunWorld.Site site = null; GameObject flipPanel = null;

            // ---- 0. (test only) a line-up of the standing idles and gestures, to check each one by eye
            var lineup = new List<AvatarBody>(); bool dbgDone = false; float lineT = -10f;
            string[] lineSt = { "Idle", "IdleLook", "IdleThink", "IdleHips", "IdleBack", "IdleEar", "IdlePhone", "IdleArms", "Wave", "Talk", "Cheer", "Clap", "Facepalm", "Nod" };
            yield return new Shot
            {
                name = "t_anim", floor = -1, dur = 5f, fov = 60f, aperture = 16f, shake = 0f,
                find = () =>
                {
                    HomeOnly(true); lineT = -10f;
                    for (int i = 0; i < lineSt.Length; i++)
                    {
                        float x = -8.4f + i * 1.3f;
                        var b = Avatars.Create(lineSt[i], MW().home, new Vector3(x, 0, -12f), 180f, Rarity.Common, i % 3 == 0 ? RoninRole.Security : RoninRole.Player, 1.75f);
                        var hb = b as HumanoidBody; if (hb != null) hb.idleSet = new[] { lineSt[i].StartsWith("Idle") ? lineSt[i] : "Idle" };
                        b.pose = Pose.Idle; lineup.Add(b);
                        Txt.Make("L", MW().home, new Vector3(x, 2.2f, -12f), lineSt[i], 0.16f, Color.white, Palette.Display).gameObject.AddComponent<Billboard>();
                    }
                    return true;
                },
                pose = (float u, out Vector3 p, out Vector3 l) => { p = H(0f, 1.5f, -21f); l = H(0f, 1.1f, -12f); },
                at = u =>
                {
                    if (Time.time - lineT > 3.2f)
                    {
                        lineT = Time.time;
                        for (int i = 0; i < lineup.Count; i++)
                        {
                            Emote e;
                            if (!lineSt[i].StartsWith("Idle") && Enum.TryParse(lineSt[i], out e)) lineup[i].Play(e, 3f);
                        }
                    }
                    if (u < 0.5f || lineup.Count == 0 || dbgDone) return; dbgDone = true;
                    var sb = new System.Text.StringBuilder();
                    foreach (var b in lineup) { var hb = b as HumanoidBody; sb.AppendLine(b.name + ": " + (hb != null ? hb.DebugState : "not humanoid")); }
                    System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../Recordings/Trailer/debug.txt"), sb.ToString());
                },
                done = () => { foreach (var b in lineup) if (b != null) Destroy(b.gameObject); lineup.Clear(); }
            };

            // ---- 1. drone over the caravan park: smoke, birds, the sign
            yield return new Shot
            {
                name = "m_park", floor = -1, dur = 8f, fov = 48f, aperture = 8f, shake = 0.6f,
                find = () => { HomeOnly(true); return true; },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u);
                    p = H(Mathf.Lerp(-17f, -6f, k), Mathf.Lerp(13f, 5.2f, k), Mathf.Lerp(-12f, 17.5f, k));
                    l = H(Mathf.Lerp(2f, 0.5f, k), Mathf.Lerp(0.5f, 1.4f, k), Mathf.Lerp(4f, 8f, k));
                },
                focus = () => H(0.5f, 1.2f, 7f)
            };

            // ---- 2. Paco steps out of the half-ruined house
            yield return new Shot
            {
                name = "m_wake", floor = -1, dur = 5.5f, fov = 38f, aperture = 2.8f, shake = 1f,
                find = () =>
                {
                    reset(); HomeOnly(true);
                    a0 = H(1.2f, 0f, 2.2f); a1 = H(1.5f, 0f, 5.8f);
                    SpawnPacoIn(MW().home, a0, YawTo(a0, a1), false);
                    return true;
                },
                at = u =>
                {
                    float k = Mathf.Clamp01(u * 1.25f);
                    paco.transform.position = Vector3.Lerp(a0, a1, k);
                    if (u > 0.8f) paco.lookAtTarget = cam.transform;
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u);
                    p = H(Mathf.Lerp(3.6f, 3.2f, k), Mathf.Lerp(1.45f, 1.5f, k), Mathf.Lerp(7.6f, 7.2f, k));
                    l = paco.transform.position + Vector3.up * 1.3f;
                },
                done = KillPaco
            };

            // ---- 3. the neighbours round the campfire (their speech bubbles pop up)
            yield return new Shot
            {
                name = "m_neigh", floor = -1, dur = 5.5f, fov = 44f, aperture = 11f, shake = 0.6f,
                find = () => { HomeOnly(true); return true; },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u);
                    p = H(Mathf.Lerp(3.2f, 4.2f, k), Mathf.Lerp(1.45f, 1.35f, k), Mathf.Lerp(7.6f, 8.0f, k));
                    l = H(Mathf.Lerp(5.6f, 6.1f, k), 1.4f, 12.0f);
                },
                focus = () => H(5f, 1.1f, 11.8f)
            };

            // ---- 4. El Tato, asleep on the sofa, dreaming of red
            yield return new Shot
            {
                name = "m_tato", floor = -1, dur = 4f, fov = 62f, aperture = 11f, shake = 0.5f,
                find = () =>
                {
                    HomeOnly(true);
                    try
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var hb in FindObjectsByType<HumanoidBody>(FindObjectsSortMode.None)) if (hb.name == "EL TATO" || hb.name == "LOLI" || hb.name == "DON TUCHI") sb.AppendLine(hb.Describe());
                        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../Recordings/Trailer/debug2.txt"), sb.ToString());
                    }
                    catch (Exception) { }
                    return true;
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u);
                    // outside the house wall (x -3.6), in the yard in front of his sofa
                    // (he lies along the whole sofa, head on the far armrest, the dream cloud floating over him)
                    p = H(Mathf.Lerp(-3.85f, -3.9f, k), Mathf.Lerp(1.6f, 1.45f, k), Mathf.Lerp(-0.1f, 0.5f, k));
                    l = H(-5.6f, 0.95f, 0.55f);
                },
                focus = () => H(-5.5f, 0.85f, 0.4f)
            };

            // ---- 5. Chusco the dog comes running
            yield return new Shot
            {
                name = "m_dog", floor = -1, dur = 4.5f, fov = 42f, aperture = 2f, shake = 1.2f,
                find = () =>
                {
                    HomeOnly(true);
                    var life = MW().home.GetComponent<HomeLife>(); if (life == null) return false;
                    life.PlaceDog(new Vector3(7.2f, 0f, -0.5f));
                    return true;
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u);
                    p = H(Mathf.Lerp(7.6f, 8.1f, k), Mathf.Lerp(0.55f, 0.6f, k), Mathf.Lerp(2.6f, 5.6f, k));
                    l = H(Mathf.Lerp(7.3f, 7.9f, k), 0.35f, Mathf.Lerp(0.5f, 3.6f, k));
                }
            };

            // ---- 6. Don Tuchi by the car: today's quota (his text box, as in the game)
            yield return new Shot
            {
                name = "m_don", floor = -1, dur = 6.5f, fov = 36f, aperture = 14f, shake = 0.6f,
                find = () =>
                {
                    reset(); HomeOnly(true);
                    var don = MW().don;
                    a0 = H(-0.4f, 0f, 4.4f);
                    SpawnPacoIn(MW().home, a0, YawTo(a0, don.transform.position), false);
                    paco.lookAtTarget = don.HeadBone; don.lookAtTarget = paco.HeadBone;
                    { var dhb = don as HumanoidBody; if (dhb != null) dhb.idleSet = new[] { "Idle" }; }
                    return true;
                },
                at = u =>
                {
                    var don = MW().don;
                    // no hand-waving here: both stand still, arms at their sides, and only the heads move
                    if (Cue(0, u, 0.04f)) Say(don.transform, "DON TUCHI", "Hoy quiero 3.000 fichas.", "Tienes cinco minutos.");
                    if (Cue(3, u, 0.45f)) don.Play(Emote.Nod, 1.2f);
                    if (Cue(1, u, 0.7f)) paco.Play(Emote.Nod, 1.4f);
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u);
                    var don = MW().don;
                    // side-on two-shot from the open side of the yard (never over his hat)
                    TwoShot(paco.HeadBone.position, don.HeadBone.position, H(3f, 1.5f, 4f), Mathf.Lerp(2.5f, 2.1f, k), 0.15f, k, out p, out l);
                },
                focus = () => MW().don.HeadBone.position,
                done = () => { Hush(); KillPaco(); MW().don.lookAtTarget = null; }
            };

            // ---- 7. the car door swings open
            yield return new Shot
            {
                name = "m_car", floor = -1, dur = 5f, fov = 40f, aperture = 5.6f, shake = 0.8f,
                find = () =>
                {
                    reset(); HomeOnly(true);
                    var car = MW().car; car.SetOpen(0f);
                    a1 = car.DoorPoint; a0 = H(-1.0f, 0f, 5.0f);
                    SpawnPacoIn(MW().home, a0, YawTo(a0, a1), false);
                    return true;
                },
                at = u =>
                {
                    var car = MW().car;
                    car.SetOpen(Mathf.Clamp01((u - 0.05f) / 0.3f));
                    if (Cue(0, u, 0.05f)) Sfx.Play("push", car.DoorPoint, 0.8f);
                    paco.transform.position = Vector3.Lerp(a0, a1, E(Mathf.Clamp01((u - 0.2f) / 0.75f)));
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u); var d = MW().car.DoorPoint;
                    p = d + new Vector3(Mathf.Lerp(-3.2f, -2.6f, k), Mathf.Lerp(1.3f, 1.2f, k), Mathf.Lerp(-2.4f, -1.6f, k));
                    l = d + Vector3.up * 1.0f;
                },
                done = () => { KillPaco(); MW().car.SetOpen(0f); }
            };

            // ---- 8. the lobby lift: EL CAMELLO makes his pitch
            yield return new Shot
            {
                name = "m_lift", floor = 0, dur = 6.5f, fov = 60f, aperture = 14f, shake = 0.5f,
                find = () =>
                {
                    reset(); HomeOnly(false); TrailerView.DialogDistance = 0.42f;
                    var ed = ElevatorDealer.I; if (ed == null || ed.Body == null) return false;
                    tA = ed.transform; dealer = ed.Body;
                    a0 = tA.TransformPoint(new Vector3(0.15f, 0f, -1.6f));
                    SpawnPacoIn(FloorRoot(0), a0, YawTo(a0, dealer.transform.position), false);
                    paco.lookAtTarget = dealer.HeadBone; dealer.lookAtTarget = paco.HeadBone;
                    return true;
                },
                at = u =>
                {
                    if (Cue(0, u, 0.04f)) { Say(dealer.HeadBone != null ? dealer.HeadBone : dealer.transform, "EL CAMELLO", "Psst. Tú. Sí, tú.", "Cien fichas y este ascensor baja a donde no baja nadie."); dealer.Play(Emote.Talk, 2.2f); }
                    if (Cue(1, u, 0.4f)) dealer.Play(Emote.Talk, 2.4f);
                    if (Cue(2, u, 0.55f)) paco.Play(Emote.Nod, 1.6f);
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u);
                    // two-shot across the cabin, from the middle of the car
                    TwoShot(paco.HeadBone.position, dealer.HeadBone.position, tA.TransformPoint(new Vector3(0f, 1.5f, -1.9f)), Mathf.Lerp(1.0f, 0.9f, k), 0.1f, k, out p, out l);
                },
                focus = () => dealer.HeadBone.position,
                done = () => { Hush(); KillPaco(); if (dealer != null) dealer.lookAtTarget = null; TrailerView.DialogDistance = 1.25f; }
            };

            // ---- 9. the basement nobody talks about: the team's hall of fame
            yield return new Shot
            {
                name = "m_basement", floor = 0, dur = 6f, fov = 50f, aperture = 4f, shake = 0.6f,
                find = () => { if (CreditsBasement.I == null) return false; CreditsBasement.I.Reveal(30f); return true; },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u); var o = CreditsBasement.Origin;
                    p = o + new Vector3(Mathf.Lerp(0.6f, 0f, k), Mathf.Lerp(1.5f, 1.7f, k), Mathf.Lerp(-9.6f, -2.5f, k));
                    l = o + new Vector3(0f, Mathf.Lerp(1.8f, 2.4f, k), 11f);
                },
                focus = () => CreditsBasement.Origin + new Vector3(0, 2f, 4f)
            };

            // ---- 9b. the easter egg in the basement corner: LA CAJA and its streamer ("gold, gold, gold, gold")
            yield return new Shot
            {
                name = "m_case", floor = 0, dur = 7.5f, fov = 52f, aperture = 11f, shake = 0.5f,
                find = () => { reset(); if (CreditsBasement.I == null) return false; CreditsBasement.I.Reveal(30f); TrailerView.Override = cam.transform; return CaseEgg.I != null; },
                at = u => { if (Cue(0, u, 0.02f)) CaseEgg.I.DemoSpin(4); },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u); var t = CaseEgg.I.transform;
                    p = t.TransformPoint(new Vector3(Mathf.Lerp(1.5f, 0.9f, k), Mathf.Lerp(1.7f, 1.65f, k), Mathf.Lerp(3.7f, 3.1f, k)));
                    l = t.TransformPoint(new Vector3(-0.35f, 1.55f, 0.5f));
                },
                focus = () => CaseEgg.I.Reel != null ? CaseEgg.I.Reel.position : CaseEgg.I.transform.position + Vector3.up * 1.9f
            };

            // ---- 10-12. the new tables: wheel of fortune, duck race, cross the road
            yield return new Shot
            {
                name = "m_wheel", floor = 1, dur = 8f, fov = 44f, aperture = 8f, shake = 0.6f,
                find = () => { reset(); TrailerView.Override = cam.transform; var w = Pick<FortuneWheel>(1); if (w == null) return false; tA = w.transform; return true; },
                at = u => { if (Cue(0, u, 0.02f)) tA.GetComponent<FortuneWheel>().DemoSpin(10); },
                pose = Local(() => tA, new Vector3(-1.0f, 1.45f, -1.7f), new Vector3(-0.35f, 1.7f, -1.25f), new Vector3(0, 1.85f, 0.62f), new Vector3(0, 1.95f, 0.62f)),
                focus = () => tA.TransformPoint(new Vector3(0, 1.95f, 0.62f))
            };
            yield return new Shot
            {
                name = "m_duck", floor = 1, dur = 8f, fov = 40f, aperture = 8f, shake = 0.6f,
                find = () => { reset(); var d = Pick<DuckRace>(1); if (d == null) return false; tA = d.transform; return true; },
                at = u => { if (Cue(0, u, 0.02f)) tA.GetComponent<DuckRace>().DemoRace(); },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    // a near-static wide view of the whole canal: the camera must NOT follow the ducks, or they
                    // look frozen on screen (the old tracking move matched their speed)
                    float k = E(u);
                    p = tA.TransformPoint(new Vector3(Mathf.Lerp(-0.15f, 0.05f, k), 1.78f, Mathf.Lerp(-1.15f, -1.0f, k)));
                    l = tA.TransformPoint(new Vector3(0.02f, 1.12f, 0.36f));
                }
            };
            yield return new Shot
            {
                name = "m_road", floor = 2, dur = 7f, fov = 42f, aperture = 2.8f, shake = 0.6f,
                find = () => { reset(); var c = Pick<CrossTheRoad>(2); if (c == null) return false; tA = c.transform; return true; },
                at = u => { if (Cue(0, u, 0.02f)) tA.GetComponent<CrossTheRoad>().DemoCross(99, 0.25f); },
                pose = Local(() => tA, new Vector3(-0.5f, 1.75f, -0.7f), new Vector3(0.35f, 1.62f, -0.45f), new Vector3(-0.3f, 1.15f, 0.36f), new Vector3(0.25f, 1.15f, 0.36f)),
                focus = () => tA.TransformPoint(new Vector3(0, 1.16f, 0.36f))
            };

            // ---- 13. didn't pay: the docks at dusk
            yield return new Shot
            {
                name = "m_exec", floor = -1, dur = 6.5f, fov = 24f, aperture = 14f, shake = 0.5f,
                fovAt = u => u < 0.6f ? 24f : 34f,
                find = () =>
                {
                    reset(); HomeOnly(false, 0); TrailerView.DialogDistance = 0.45f;
                    site = MW().sites[0];
                    var fwd = Quaternion.Euler(0, site.yaw, 0) * Vector3.forward;
                    SpawnPacoIn(site.root, site.spot, site.yaw, false);
                    paco.lookAtTarget = site.don.HeadBone; site.don.lookAtTarget = paco.HeadBone; foreach (var g in site.goons) g.lookAtTarget = paco.HeadBone;
                    a0 = fwd;
                    return true;
                },
                at = u =>
                {
                    if (Cue(0, u, 0.05f)) Say(site.don.transform, "DON TUCHI", "Te faltaban 212 fichas.", "Nada personal. Son negocios.");
                    // he draws at 4.2 s and fires at 5.5 s: the edit cuts on the flash
                    // the side angle is the Don and Paco alone: his men step out of the frame
                    if (Cue(3, u, 0.6f)) foreach (var g in site.goons) if (g != null) g.gameObject.SetActive(false);
                    if (Cue(1, u, 4.2f / 6.5f)) { donGun = DonGun.Draw(site.don, paco.HeadBone); paco.Play(Emote.Cheer, 3f); }
                    if (Cue(2, u, 5.5f / 6.5f) && donGun != null) donGun.Fire();
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u);
                    // reverse angle: over Don's shoulder onto Paco's face (sea behind him), slightly off-axis
                    var dh = site.don.HeadBone.position; var ph = paco.HeadBone.position;
                    var ax = ph - dh; ax.y = 0; ax.Normalize(); var side = Vector3.Cross(Vector3.up, ax);
                    p = dh - ax * Mathf.Lerp(0.9f, 0.6f, k) + side * 0.55f + Vector3.up * 0.05f;
                    l = ph + Vector3.down * 0.15f;
                    if (u >= 0.6f)
                    {
                        // hard cut to the side: the Don on the left with the gun out, Paco on the right with his hands up
                        float k2 = Mathf.InverseLerp(0.6f, 1f, u);
                        var mid = (dh + ph) * 0.5f;
                        p = mid - side * Mathf.Lerp(3.3f, 3.0f, k2) - ax * 0.25f + Vector3.down * 0.1f;
                        l = mid + Vector3.down * 0.12f;
                    }
                },
                focus = () => (paco.HeadBone.position + site.don.HeadBone.position) * 0.5f,
                done = () => { Hush(); foreach (var g in site.goons) if (g != null) g.gameObject.SetActive(true); if (donGun != null) Destroy(donGun.gameObject); KillPaco(); HomeOnly(false); TrailerView.DialogDistance = 1.25f; }
            };

            // ---- 14. you paid it all: double or nothing? Seen from above: the coin flies up at the lens, clatters
            // down onto the tray, and only then does the verdict appear.
            TMPro.TextMeshPro flipT = null, flipT2 = null; float flipShown = -1f; float slow0 = CoinFlip.Slow;
            yield return new Shot
            {
                name = "m_flip", floor = -1, dur = 11.5f, fov = 46f, aperture = 14f, shake = 0.35f,
                find = () =>
                {
                    reset(); HomeOnly(false, -1, true); flipShown = -1f; CoinFlip.Slow = 0.3f;   // extra slow for the film: the fall rides the ticking
                    var tray = MW().LimboSpot + new Vector3(0, 0.8f, 1.2f);
                    flipPanel = new GameObject("Trailer Flip Set");
                    flipPanel.transform.position = tray;
                    var lg = new GameObject("Key"); lg.transform.SetParent(flipPanel.transform, false); lg.transform.localPosition = new Vector3(0.5f, 1.6f, -0.4f);
                    var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 5f; l.intensity = 0.7f; l.color = new Color(1f, 0.93f, 0.8f); l.shadows = LightShadows.None;
                    flipT = Txt.Make("Verdict", flipPanel.transform, new Vector3(0, 0.12f, -0.6f), "¡DOBLE!", 0.2f, Palette.Gold, Palette.UI, TMPro.TextAlignmentOptions.Center);
                    flipT2 = Txt.Make("Chips", flipPanel.transform, new Vector3(0, 0.1f, -0.8f), "2.568.000 FICHAS", 0.085f, Color.white, Palette.UI, TMPro.TextAlignmentOptions.Center);
                    foreach (var tx in new[] { flipT, flipT2 }) { tx.outlineWidth = 0.2f; tx.outlineColor = new Color32(14, 10, 22, 255); tx.gameObject.SetActive(false); }
                    return true;
                },
                at = u =>
                {
                    if (Cue(0, u, 0.06f)) StartCoroutine(CoinFlip.Flip(MW().LimboSpot + new Vector3(0, 1.35f, 1.2f), Vector3.forward, h => { }, false, 30f));   // lands on the A: the edit pushes into it and out comes the title
                    if (flipShown > 0f && flipT != null)
                    {
                        if (Cue(1, u, 0f)) { flipT.gameObject.SetActive(true); flipT2.gameObject.SetActive(true); Juice.Sparks(flipPanel.transform.position + Vector3.up * 0.1f, Palette.Gold, 50); }
                        float k = Mathf.Clamp01((Time.time - flipShown) / 0.35f); float pop = 1f + Mathf.Sin(k * Mathf.PI) * 0.25f;
                        var face = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
                        flipT.transform.rotation = face; flipT2.transform.rotation = face;
                        flipT.transform.localScale = Vector3.one * k * pop; flipT2.transform.localScale = Vector3.one * Mathf.Clamp01(k * 1.5f - 0.5f);
                    }
                },
                pose = (float u, out Vector3 p, out Vector3 l) =>
                {
                    float k = E(u); var o = MW().LimboSpot;
                    // straight down on the tray; the verdict appears under the coin, inside the frame
                    p = o + new Vector3(Mathf.Lerp(0.1f, 0f, k), Mathf.Lerp(2.75f, 2.3f, k), Mathf.Lerp(0.62f, 0.7f, k));
                    l = o + new Vector3(0, 0.8f, Mathf.Lerp(0.98f, 0.9f, k));
                },
                focus = () => MW().LimboSpot + new Vector3(0, 0.85f, 1.2f),
                done = () => { CoinFlip.Slow = slow0; if (flipPanel != null) Destroy(flipPanel); var ct = GameObject.Find("Coin Toss"); if (ct != null) Destroy(ct); HomeOnly(false); }
            };
        }
    }
}
