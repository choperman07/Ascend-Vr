using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Easing curves and small coroutine tweens.</summary>
    public static class Fx
    {
        public static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - Mathf.Pow(1f - t, 3f); }
        public static float EaseOut4(float t) { t = Mathf.Clamp01(t); return 1f - Mathf.Pow(1f - t, 4f); }
        public static float EaseIn(float t) { t = Mathf.Clamp01(t); return t * t * t; }
        public static float EaseInOut(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        public static float EaseOutBack(float t) { t = Mathf.Clamp01(t) - 1f; const float s = 1.7f; return 1f + t * t * ((s + 1f) * t + s); }
        public static float Bounce(float t) { t = Mathf.Clamp01(t); return Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)) * (1f - t); }

        // Overlapping Pulse / Shake tweens on the same transform share the rest value captured by the first one, so
        // a second tween started mid-way never "bakes in" the first one's offset (scale / position drifting forever).
        class Rest { public Vector3 v; public int n; public float until; }
        static readonly Dictionary<Transform, Rest> scaleRest = new Dictionary<Transform, Rest>();
        static readonly Dictionary<Transform, Rest> posRest = new Dictionary<Transform, Rest>();

        static Vector3 Acquire(Dictionary<Transform, Rest> d, Transform t, Vector3 current, float dur)
        {
            Rest r;
            if (!d.TryGetValue(t, out r)) { r = new Rest(); d[t] = r; }
            // n stuck above 0 after a tween's coroutine was stopped: stale once its time ran out
            if (r.n <= 0 || Time.time > r.until) { r.v = current; r.n = 0; r.until = 0f; }
            r.n++;
            r.until = Mathf.Max(r.until, Time.time + dur + 0.25f);
            return r.v;
        }

        static void Release(Dictionary<Transform, Rest> d, Transform t)
        {
            Rest r;
            if (d.TryGetValue(t, out r)) { r.n--; if (r.n <= 0) d.Remove(t); }
        }

        public static IEnumerator Pulse(Transform t, float scale, float dur)
        {
            if (t == null) yield break;
            Vector3 b = Acquire(scaleRest, t, t.localScale, dur);
            for (float e = 0; e < dur; e += Time.deltaTime)
            {
                if (t == null) { Release(scaleRest, t); yield break; }
                t.localScale = b * (1f + (scale - 1f) * Mathf.Sin(e / dur * Mathf.PI));
                yield return null;
            }
            Release(scaleRest, t);
            if (t != null) t.localScale = b;
        }

        public static IEnumerator Move(Transform t, Vector3 to, float dur, bool local = true)
        {
            if (t == null) yield break;
            Vector3 from = local ? t.localPosition : t.position;
            for (float e = 0; e < dur; e += Time.deltaTime)
            {
                if (t == null) yield break;
                var p = Vector3.Lerp(from, to, EaseInOut(e / dur));
                if (local) t.localPosition = p; else t.position = p;
                yield return null;
            }
            if (t == null) yield break;
            if (local) t.localPosition = to; else t.position = to;
        }

        /// <summary>Moves along a parabola in world space (chips flying, cards dealt).</summary>
        public static IEnumerator Arc(Transform t, Vector3 to, float height, float dur, Quaternion? endRot = null)
        {
            if (t == null) yield break;
            Vector3 from = t.position; Quaternion r0 = t.rotation; Quaternion r1 = endRot ?? r0;
            for (float e = 0; e < dur; e += Time.deltaTime)
            {
                if (t == null) yield break;
                float k = EaseInOut(e / dur);
                t.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * height;
                t.rotation = Quaternion.Slerp(r0, r1, k);
                yield return null;
            }
            if (t == null) yield break;
            t.position = to; t.rotation = r1;
        }

        public static IEnumerator Shake(Transform t, float amount, float dur)
        {
            if (t == null) yield break;
            // comfort: shaking the player's own view in a headset causes nausea, so it becomes a faint tremor (haptics carry the hit)
            if (XRIn.HasHeadset && Rig.Ready && Rig.I.Head.IsChildOf(t)) amount *= 0.15f;
            Vector3 b = Acquire(posRest, t, t.localPosition, dur);
            for (float e = 0; e < dur; e += Time.deltaTime)
            {
                if (t == null) { Release(posRest, t); yield break; }
                t.localPosition = b + Random.insideUnitSphere * amount * (1f - e / dur);
                yield return null;
            }
            Release(posRest, t);
            if (t != null) t.localPosition = b;
        }

        /// <summary>Counts a TMP label up from 0 to value (the "money counting" feel).</summary>
        public static IEnumerator CountUp(TMP_Text label, long value, float dur, string prefix = "+", string suffix = "")
        {
            for (float e = 0; e < dur; e += Time.deltaTime)
            {
                if (label == null) yield break;
                long v = (long)(value * EaseOut(e / dur));
                label.text = prefix + v.ToString("N0") + suffix;
                yield return null;
            }
            if (label != null) label.text = prefix + value.ToString("N0") + suffix;
        }

        static MaterialPropertyBlock _mpb;
        static int BaseColorId, EmitId;
        /// <summary>Per-renderer tint (no material copies). Called every frame by HUDs, so it reuses one block.</summary>
        public static void SetColor(Renderer r, Color c, float emit = 1f)
        {
            if (r == null) return;
            if (_mpb == null) { _mpb = new MaterialPropertyBlock(); BaseColorId = Shader.PropertyToID("_BaseColor"); EmitId = Shader.PropertyToID("_EmitAmount"); }
            var mpb = _mpb;
            r.GetPropertyBlock(mpb);   // overwrites the block with this renderer's current values
            c.a = 1f;
            mpb.SetColor(BaseColorId, c);
            if (emit >= 0) mpb.SetFloat(EmitId, emit);
            r.SetPropertyBlock(mpb);
        }
        public static void ClearColor(Renderer r) { if (r != null) r.SetPropertyBlock(null); }
    }
}
