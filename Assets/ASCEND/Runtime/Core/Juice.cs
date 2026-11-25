using System.Collections;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Feedback layer: every real win (payout above the stake) gets light, sound, particles, haptics and a
    /// counting number, scaled by how big it is. Pushes and losses get a neutral sound only — no fake celebrations.</summary>
    public class Juice : MonoBehaviour
    {
        static Juice _i;
        public static Juice I
        {
            get
            {
                if (_i == null)
                {
                    _i = FindFirstObjectByType<Juice>();
                    if (_i == null) _i = new GameObject("Juice").AddComponent<Juice>();
                }
                return _i;
            }
        }

        ParticleSystem chips, confetti, sparks;
        Mesh ringMesh;

        void Awake()
        {
            if (_i != null && _i != this) { Destroy(gameObject); return; }
            _i = this;
            chips = MakeSystem("ChipBurst", Palette.ParticleChips, true, 0.09f, 7f, 1.6f, 2.5f, 300);
            confetti = MakeSystem("Confetti", Palette.ParticleConfetti, false, 0.035f, 6f, 0.35f, 3.5f, 500);
            sparks = MakeSystem("Sparks", Palette.Glow, false, 0.08f, 4f, 0.2f, 0.9f, 300);
        }

        void OnEnable() { GameState.I.Won += OnWon; }
        void OnDisable() { if (GameState.Exists) GameState.I.Won -= OnWon; }

        ParticleSystem MakeSystem(string name, Material mat, bool chipSheet, float size, float speed, float gravity, float life, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startColor = chipSheet ? new ParticleSystem.MinMaxGradient(Color.white)
                : new ParticleSystem.MinMaxGradient(new Color(1f, 0.25f, 0.6f), new Color(0.2f, 0.95f, 1f));
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 28f; shape.radius = 0.08f;
            shape.rotation = new Vector3(-90f, 0, 0);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            if (chipSheet)
            {
                var tsa = ps.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 5; tsa.numTilesY = 1;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 4.99f);
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        public static void Burst(string kind, Vector3 pos, int count)
        {
            var me = I;
            var ps = kind == "chips" ? me.chips : kind == "sparks" ? me.sparks : me.confetti;
            ps.transform.position = pos;
            ps.Emit(count);
        }

        public static void Sparks(Vector3 pos, Color c, int count = 20)
        {
            var me = I;
            var ep = new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true, startColor = c };
            me.sparks.Emit(ep, count);
        }

        void OnWon(long amount, float mult, Vector3 where)
        {
            Celebrate(where == Vector3.zero ? HeadAhead() : where, amount, mult);
        }

        static Vector3 HeadAhead()
        {
            var cam = Camera.main;
            if (cam == null) return Vector3.up * 1.5f;
            var f = cam.transform.forward; f.y = 0; f.Normalize();
            return cam.transform.position + f * 1.2f;
        }

        static readonly string[] Titles = { "WIN", "BIG WIN", "MEGA WIN", "EPIC WIN", "JACKPOT" };
        static readonly string[] Sounds = { "win_small", "win_big", "win_mega", "win_epic", "jackpot" };
        static readonly float[] TierMult = { 0f, 3f, 8f, 25f, 100f };
        static Color TierColor(int t) { return t == 0 ? Palette.Money : t == 1 ? Palette.Teal : t == 2 ? Palette.Magenta : t == 3 ? Palette.Gold : Palette.Coral; }
        static int TierOf(float mult) { int t = 0; for (int i = 1; i < TierMult.Length; i++) if (mult >= TierMult[i]) t = i; return t; }

        /// <summary>Win feedback (only ever called for a real net win: payout above the stake). mult = payout / stake.
        /// The win show counts the amount up and climbs the tiers live (WIN → BIG → MEGA → EPIC → JACKPOT) as the
        /// counter passes each threshold; the number it lands on is always the real payout.</summary>
        public static void Celebrate(Vector3 pos, long amount, float mult)
        {
            var me = I;
            int tier = TierOf(mult);
            Burst("chips", pos + Vector3.up * 0.3f, 12 + tier * 25);
            me.StartCoroutine(me.Ring(pos + Vector3.up * 0.05f, TierColor(0), 0.6f));
            me.StartCoroutine(me.WinShow(pos, amount, mult, tier));
            foreach (var b in BotAvatar.All)
            {
                if (b == null || b.job == BotJob.Waiter || b.job == BotJob.Patrol) continue;
                if ((b.transform.position - pos).sqrMagnitude < (tier >= 2 ? 100f : 36f) && Random.value < 0.75f)
                    b.Anim.Play(Random.value < 0.5f ? Emote.Clap : Emote.Cheer, 1.6f);
            }
        }

        /// <summary>One tier step of the show: sound, voice, light, confetti, fireworks, coin rain, haptics.</summary>
        void TierFx(Vector3 pos, int tier)
        {
            var c = TierColor(tier);
            Sfx.Play(Sounds[tier], pos, 1f);
            if (tier >= 1) Voice.Announce(tier == 1 ? "ann_big" : tier == 2 ? "ann_mega" : tier == 3 ? "ann_epic" : "ann_jackpot");
            if (tier >= 2) Sfx.Play("coin_shower", pos, 0.8f);
            if (tier >= 3) Sfx.UI("crowd_cheer", 0.7f);
            if (tier >= 1) Burst("confetti", pos + Vector3.up * 0.8f, 40 + tier * 80);
            // Casino FREE extras: the pack's multicolour confetti blast and its win chimes
            var lib = AssetLibrary.I;
            if (lib != null)
            {
                if (tier >= 1 && lib.confetti != null) Spawn(lib.confetti, pos + Vector3.up * 0.9f, 4f);
                if (tier >= 3 && lib.confettiRain != null) Spawn(lib.confettiRain, pos + Vector3.up * 2.6f, 6f);
                var chime = tier >= 2 ? lib.winBonus : lib.winChime;
                if (chime != null) AudioSource.PlayClipAtPoint(chime, pos, tier >= 1 ? 0.8f : 0.45f);
            }
            StartCoroutine(Flash(pos + Vector3.up * 0.8f, c, 1.5f + tier * 1.2f, 0.6f + tier * 0.3f));
            if (tier >= 1) StartCoroutine(Ring(pos + Vector3.up * 0.05f, c, 0.6f + tier * 0.5f));
            if (tier >= 1) StartCoroutine(Fireworks(pos, tier * 2 + 1, c));
            if (tier >= 2 && Rig.Ready) Burst("chips", Rig.I.Head.position + Vector3.up * 1.2f, 30 + tier * 20);
            StartCoroutine(XRIn.Pattern(Hand.Right, 2 + tier * 2, 0.3f + tier * 0.15f, 0.07f));
            StartCoroutine(XRIn.Pattern(Hand.Left, 2 + tier * 2, 0.3f + tier * 0.15f, 0.07f));
        }

        static void Spawn(GameObject prefab, Vector3 pos, float life)
        {
            var go = Instantiate(prefab, pos, Quaternion.identity);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) ps.Play(true);
            Destroy(go, life);
        }

        static Texture2D _rays;
        /// <summary>Soft sunburst (16 rays) drawn once, spun behind the win title.</summary>
        static Texture2D Rays()
        {
            if (_rays != null) return _rays;
            const int S = 256; var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Atan2(dy, dx);
                    float ray = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 8f)), 6f);
                    float fall = Mathf.Clamp01(1f - r) * Mathf.Clamp01(r * 4f);
                    float v = Mathf.Clamp01(ray * fall * 1.2f + Mathf.Clamp01(1f - r * 1.6f) * 0.35f);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(v * 255));
                }
            _rays = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "WinRays" };
            _rays.SetPixels32(px); _rays.Apply(false, true);
            return _rays;
        }

        static void Grad(TMP_Text t, Color c)
        {
            t.enableVertexGradient = true;
            var hi = Color.Lerp(c, Color.white, 0.55f);
            t.colorGradient = new VertexGradient(hi, hi, c, Color.Lerp(c, Color.black, 0.15f));
        }

        IEnumerator WinShow(Vector3 pos, long amount, float mult, int finalTier)
        {
            // a sticker-style card that floats over the table and turns to face you
            var root = new GameObject("WinShow").transform;
            root.position = pos + Vector3.up * 1.3f;
            root.gameObject.AddComponent<Billboard>();
            var card = UIKit.Panel("Card", root, Vector3.zero, Quaternion.identity, new Vector2(1400, 820), 1.25f + finalTier * 0.12f, Color.white, false);
            foreach (var g in card.GetComponentsInChildren<UnityEngine.UI.Graphic>()) g.raycastTarget = false;
            var rays = UIKit.Pic(card, Rays(), 200, -90, 1000, 1000);
            var title = UIKit.Label(card, Titles[0], 0, 160, 1400, 220, 190, Color.white, Palette.UI, TextAlignmentOptions.Center);
            title.enableAutoSizing = false; title.fontSharedMaterial = UISkin.Sticker(Palette.UI); title.characterSpacing = 4f;
            Grad(title, TierColor(0));
            var amt = UIKit.Label(card, "+0", 0, 380, 1400, 150, 130, Palette.Money, Palette.UI, TextAlignmentOptions.Center);
            amt.enableAutoSizing = false; amt.fontSharedMaterial = UISkin.Sticker(Palette.UI);
            Grad(amt, Palette.Money);
            // ×multiplier pill and win-streak pill under the number
            var pill = UIKit.Rect("Mult", card, 560, 548, 280, 92);
            var pi = pill.gameObject.AddComponent<UnityEngine.UI.Image>(); UISkin.Round(pi, 46); pi.color = Palette.Gold; pi.raycastTarget = false;
            var pt = UIKit.Label(pill, "×1", 0, 0, 280, 92, 64, UISkin.Ink, Palette.UI, TextAlignmentOptions.Center);
            pt.enableAutoSizing = false; pt.fontSharedMaterial = Palette.UI.material;
            int streak = GameState.I.winStreak;
            TMP_Text st = null; RectTransform sp = null;
            if (streak >= 2)
            {
                sp = UIKit.Rect("Streak", card, 470, 660, 460, 76);
                var si = sp.gameObject.AddComponent<UnityEngine.UI.Image>(); UISkin.Round(si, 38); si.color = Palette.Coral; si.raycastTarget = false;
                st = UIKit.Label(sp, "WIN STREAK ×" + streak, 0, 0, 460, 76, 46, UISkin.Ink, Palette.UI, TextAlignmentOptions.Center);
                st.enableAutoSizing = false; st.fontSharedMaterial = Palette.UI.material;
                sp.localScale = Vector3.zero;
                if (streak >= 3) { Sfx.UI("streak", 0.8f); if (streak == 3) Voice.Announce("ann_streak"); }
            }

            long stake = mult > 0f ? (long)Mathf.Max(1f, amount / mult) : amount;
            int shown = 0;
            TierFx(pos, 0);
            float count = 0.9f + finalTier * 1.0f;          // bigger wins count for longer (and climb more tiers)
            float slam = 0f, tick = 0f;
            float dur = count + 1.6f + finalTier * 0.4f;
            var basePos = root.position;
            for (float e = 0; e < dur; e += Time.deltaTime)
            {
                // count-up (ease-out), ticking faster and higher as it climbs
                float k = Mathf.Clamp01(e / count);
                float ke = 1f - Mathf.Pow(1f - k, 2.2f);
                long v = (long)(amount * ke);
                amt.text = "+" + v.ToString("N0");
                float liveMult = stake > 0 ? v / (float)stake : 1f;
                float pm = k >= 1f ? mult : liveMult;
                pt.text = "×" + pm.ToString(pm >= 10f ? "0" : "0.0");
                tick -= Time.deltaTime;
                if (k < 1f && tick <= 0f) { Sfx.UI("xp_tick", 0.35f, 0.9f + ke * 0.8f); tick = Mathf.Lerp(0.09f, 0.035f, ke); }
                if (k >= 1f && e - Time.deltaTime < count) { Sfx.UI("chips_payout", 0.8f); StartCoroutine(Fx.Pulse(pill, 1.25f, 0.25f)); }
                // tier escalation as the counter passes each threshold
                int live = Mathf.Min(finalTier, TierOf(liveMult));
                if (k >= 1f) live = finalTier;
                if (live > shown)
                {
                    shown = live; slam = 0f;
                    title.text = Titles[shown]; Grad(title, TierColor(shown));
                    TierFx(pos, shown);
                }
                // title slam (overshoot), pill/streak pop, rays spin, a little shake on the big ones
                slam += Time.deltaTime;
                float s1 = slam < 0.12f ? Mathf.Lerp(1.7f, 0.9f, slam / 0.12f) : Mathf.Lerp(0.9f, 1f, Fx.EaseOutBack(Mathf.Min(1f, (slam - 0.12f) / 0.25f)));
                title.rectTransform.localScale = Vector3.one * s1;
                float appear = Fx.EaseOutBack(Mathf.Min(1f, e / 0.3f));
                float fade = Mathf.Clamp01((dur - e) / 0.45f);
                root.localScale = Vector3.one * appear * (0.92f + 0.08f * fade);
                if (sp != null) sp.localScale = Vector3.one * Fx.EaseOutBack(Mathf.Clamp01((e - 0.35f) / 0.3f));
                rays.rectTransform.localRotation = Quaternion.Euler(0, 0, e * (25f + shown * 12f));
                rays.color = Palette.A(TierColor(shown), (0.35f + shown * 0.1f) * fade * (0.8f + 0.2f * Mathf.Sin(e * 6f)));
                float shake = shown >= 2 && slam < 0.35f ? (0.35f - slam) * 0.02f * shown : 0f;
                root.position = basePos + Vector3.up * e * 0.05f + Random.insideUnitSphere * shake;
                title.alpha = amt.alpha = pt.alpha = fade; if (st != null) st.alpha = fade;
                pi.color = Palette.A(Palette.Gold, fade);
                yield return null;
            }
            Destroy(root.gameObject);
        }

        IEnumerator Flash(Vector3 pos, Color c, float intensity, float dur)
        {
            var go = new GameObject("WinFlash"); go.transform.position = pos;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.range = 4f + intensity; l.shadows = LightShadows.None;
            for (float t = 0; t < dur; t += Time.deltaTime) { l.intensity = intensity * (1f - t / dur) * (0.75f + 0.25f * Mathf.Sin(t * 30f)); yield return null; }
            Destroy(go);
        }

        IEnumerator Ring(Vector3 pos, Color c, float radius)
        {
            // one shared ring mesh (tinted per win): a runtime mesh per celebration was never destroyed with its object
            GameObject go;
            if (ringMesh == null)
            {
                var k = new MeshKit(); k.Ring(Vector3.zero, 0.92f, 1f, 48, Quaternion.identity, Color.white, 1.5f);
                go = k.Spawn("WinRing", null, pos, Quaternion.identity, Palette.FlatGlow, false);
                ringMesh = go.GetComponent<MeshFilter>().sharedMesh;
            }
            else
            {
                go = new GameObject("WinRing"); go.transform.position = pos;
                go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Palette.FlatGlow;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }
            var r = go.GetComponent<Renderer>();
            for (float t = 0; t < 0.8f; t += Time.deltaTime)
            {
                float f = t / 0.8f;
                go.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, radius, Fx.EaseOut(f));
                Fx.SetColor(r, c * (1f - f), 1.5f * (1f - f));
                yield return null;
            }
            Destroy(go);
        }

        IEnumerator Fireworks(Vector3 pos, int n, Color c)
        {
            for (int i = 0; i < n; i++)
            {
                var from = pos + new Vector3(Random.Range(-1.2f, 1.2f), 0.5f, Random.Range(-1.2f, 1.2f));
                var to = from + Vector3.up * Random.Range(2.2f, 3.4f);
                var rocket = Build.GlowSprite(null, Vector3.zero, 0.25f, Palette.Gold, 0.9f);
                rocket.position = from;
                Sfx.Play("orb_whoosh_loop", from, 0.3f, 1.8f);
                for (float t = 0; t < 0.5f; t += Time.deltaTime) { if (rocket == null) break; rocket.position = Vector3.Lerp(from, to, Fx.EaseOut(t / 0.5f)); yield return null; }
                if (rocket != null) Destroy(rocket.gameObject);
                var col = i % 3 == 0 ? c : i % 3 == 1 ? Palette.Gold : Palette.Magenta;
                Sparks(to, col, 60);
                Burst("confetti", to, 40);
                Sfx.Play("explosion_small", to, 0.35f, 1.4f);
                yield return new WaitForSeconds(Random.Range(0.12f, 0.3f));
            }
        }

        /// <summary>Neutral feedback for a push / loss (deliberately understated).</summary>
        public static void Lose(Vector3 pos) { Sfx.Play("lose_soft", pos, 0.6f); }
        public static void Push(Vector3 pos) { Sfx.Play("push", pos, 0.6f); }
    }
}
