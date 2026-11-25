using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ascend
{

    // ====================================================================== big title cards
    public static class RunCard
    {
        /// <summary>A big two-line title floating in front of the player for a few seconds.</summary>
        public static IEnumerator Show(string title, string sub, Color c, float seconds)
        {
            if (!Rig.Ready) yield break;
            var head = Rig.I.Head; var f = Rig.I.HeadForwardFlat;
            var go = new GameObject("RunCard");
            go.transform.position = head.position + f * 2.2f + Vector3.up * 0.1f;
            go.transform.rotation = Quaternion.LookRotation(f);
            var a = Txt.Make("Title", go.transform, new Vector3(0, 0.16f, 0), title, 0.36f, c, Palette.UI, TextAlignmentOptions.Center, 4f);
            a.outlineWidth = 0.18f; a.outlineColor = new Color32(10, 6, 20, 255); a.fontSharedMaterial = a.fontMaterial;
            var b = Txt.Make("Sub", go.transform, new Vector3(0, -0.2f, 0), sub, 0.12f, Palette.Text, Palette.UI, TextAlignmentOptions.Center, 4f);
            b.outlineWidth = 0.2f; b.outlineColor = new Color32(10, 6, 20, 255); b.fontSharedMaterial = b.fontMaterial;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.Min(1f, t / 0.3f), o = Mathf.Min(1f, (seconds - t) / 0.4f);
                go.transform.localScale = Vector3.one * (0.7f + 0.3f * Fx.EaseOutBack(k));
                a.alpha = b.alpha = Mathf.Min(k, o);
                yield return null;
            }
            UnityEngine.Object.Destroy(go);
        }
    }

    // ====================================================================== coin flip (TODO O NADA / DOBLE O NADA)
    public static class CoinFlip
    {
        /// <summary>The ASCEND coin: one face says X2, the other carries the tower's "A". It is tossed over a small
        /// felt tray in front of the player, tumbles under gravity, bounces twice, rattles round on its rim and settles
        /// flat. heads = X2 up. Calls done(heads) once it lies still. <paramref name="force"/> fixes the result (trailer).</summary>
        /// <summary>The whole toss runs in slow motion, so you can follow the coin turning.</summary>
        public static float Slow = 0.5f;
        public static IEnumerator Flip(Vector3 at, Vector3 faceDir, Action<bool> done, bool? force = null, float linger = 2.8f)
        {
            bool heads = force ?? UnityEngine.Random.value < 0.5f;
            const float R = 0.2f, Thick = 0.028f, G = 9.81f;
            faceDir.y = 0; if (faceDir.sqrMagnitude < 1e-4f) faceDir = Vector3.forward;
            var root = new GameObject("Coin Toss").transform;
            root.position = at + Vector3.down * 0.55f; root.rotation = Quaternion.LookRotation(faceDir.normalized);

            // the tray: a floating disc of felt with a gold rim
            var tk = new MeshKit();
            tk.Cylinder(new Vector3(0, -0.03f, 0), 0.5f, 0.5f, 0.06f, 48, Quaternion.identity, Palette.Hex("17121F"), 0f, true, Palette.Hex("3B1F63"), 0f);
            tk.Torus(new Vector3(0, 0.0f, 0), 0.5f, 0.018f, 48, 6, Quaternion.identity, Palette.Gold, 0.35f);
            tk.Ring(new Vector3(0, 0.002f, 0), 0.3f, 0.308f, 48, Quaternion.identity, Palette.Hex("7A52B8"), 0.2f);
            tk.Cylinder(new Vector3(0, -0.09f, 0), 0.12f, 0.3f, 0.06f, 32, Quaternion.identity, Palette.Hex("100C18"), 0f);
            tk.Spawn("Tray", root, Palette.Flat(Color.white, 0.02f, 0.05f, 0.25f), false);

            // the coin, axis up: X2 on top, A underneath
            var coin = new GameObject("Coin").transform; coin.SetParent(root, false);
            var k = new MeshKit();
            Color gold = Palette.Hex("E9B23C"), deep = Palette.Hex("B07A1C"), ink = Palette.Hex("4A2C08");
            k.Cylinder(Vector3.zero, R, R, Thick, 56, Quaternion.identity, deep, 0.05f, true, gold, 0.08f);
            for (int s = -1; s <= 1; s += 2)
            {
                float y = s * (Thick * 0.5f + 0.0012f);
                var rot = Quaternion.Euler(s > 0 ? 0 : 180, 0, 0);
                k.Ring(new Vector3(0, y, 0), R * 0.86f, R * 0.93f, 56, rot, deep, 0.05f);
                for (int d = 0; d < 24; d++)
                {
                    float a = d / 24f * Mathf.PI * 2f;
                    k.Box(new Vector3(Mathf.Cos(a) * R * 0.795f, y, Mathf.Sin(a) * R * 0.795f), new Vector3(0.008f, 0.002f, 0.008f), Quaternion.Euler(0, 45, 0), deep, 0.05f);
                }
            }
            for (int d = 0; d < 56; d++)      // milled edge
            {
                float a = d / 56f * Mathf.PI * 2f;
                k.Box(new Vector3(Mathf.Cos(a) * R, 0, Mathf.Sin(a) * R), new Vector3(0.006f, Thick * 0.9f, 0.006f), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), deep, 0.05f);
            }
            k.Spawn("Disc", coin, Palette.Flat(Color.white, 0.03f, 0.35f, 0.7f, 0.9f), false);
            var top = Txt.Make("X2", coin, new Vector3(0, Thick * 0.5f + 0.003f, 0), "X2", 0.17f, ink, Palette.UI, TextAlignmentOptions.Center, 0.4f);
            top.transform.localRotation = Quaternion.Euler(90, 0, 0); top.fontStyle = FontStyles.Bold;
            var bottom = Txt.Make("A", coin, new Vector3(0, -Thick * 0.5f - 0.003f, 0), "A", 0.24f, ink, Palette.Display, TextAlignmentOptions.Center, 0.4f);
            bottom.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var sub = Txt.Make("ASub", coin, new Vector3(0, -Thick * 0.5f - 0.003f, 0.105f), "ASCEND", 0.032f, ink, Palette.Display, TextAlignmentOptions.Center, 0.4f);
            sub.transform.localRotation = Quaternion.Euler(-90, 0, 0);

            float rest = Thick * 0.5f + 0.004f;          // centre height when lying flat on the felt
            float endFlip = heads ? 0f : 180f;           // rotation about the coin's own X that leaves the right face up
            Sfx.Play("coin_flip", at, 1f);

            // ---- 1. the toss: a real parabola from the thumb, six and a half turns, drifting a little
            float v0 = 4.1f, y0 = 0.32f;
            float T = (v0 + Mathf.Sqrt(v0 * v0 + 2f * G * (y0 - rest - R * 0.4f))) / G;
            float tilt0 = 24f;                           // lands on its rim, not flat
            float spin = 360f * 6f + endFlip + tilt0;
            Vector2 drift = new Vector2(UnityEngine.Random.Range(-0.07f, 0.07f), UnityEngine.Random.Range(-0.02f, 0.08f));
            float yaw0 = UnityEngine.Random.Range(-25f, 25f), yawRate = UnityEngine.Random.Range(-50f, 50f);
            for (float t = 0; t < T; t += Time.deltaTime * Slow)
            {
                float u = t / T;
                coin.localPosition = new Vector3(drift.x * u, y0 + v0 * t - 0.5f * G * t * t, drift.y * u);
                coin.localRotation = Quaternion.Euler(0, yaw0 + yawRate * t, 0) * Quaternion.Euler(-spin * (1f - u), 0, 0) * Quaternion.Euler(tilt0, 0, 0) * Quaternion.Euler(endFlip, 0, 0);
                yield return null;
            }

            // ---- 2. two bounces, each lower and shorter, rocking over onto the other rim
            float yaw = yaw0 + yawRate * T, tilt = tilt0;
            Vector3 p = new Vector3(drift.x, 0, drift.y), slide = new Vector3(drift.x, 0, drift.y).normalized * 0.12f;
            float[] hop = { 0.15f, 0.045f }; float[] nextTilt = { -15f, 9f };
            for (int b = 0; b < hop.Length; b++)
            {
                Sfx.Play("coin_land", root.position, b == 0 ? 0.9f : 0.5f, b == 0 ? 1f : 1.25f);
                if (b == 0) XRIn.HapticBoth(0.6f, 0.12f);
                float tb = 2f * Mathf.Sqrt(2f * hop[b] / G), from = tilt;
                for (float t = 0; t < tb; t += Time.deltaTime * Slow)
                {
                    float u = t / tb;
                    tilt = Mathf.Lerp(from, nextTilt[b], u * u * (3f - 2f * u));
                    float lift = rest + Mathf.Abs(Mathf.Sin(tilt * Mathf.Deg2Rad)) * R;
                    coin.localPosition = p + slide * (b == 0 ? 0.6f : 0.25f) * u + Vector3.up * (lift + 4f * hop[b] * u * (1f - u));
                    coin.localRotation = Quaternion.Euler(0, yaw + (b == 0 ? 40f : 15f) * u, 0) * Quaternion.Euler(tilt, 0, 0) * Quaternion.Euler(endFlip, 0, 0);
                    yield return null;
                }
                p += slide * (b == 0 ? 0.6f : 0.25f); yaw += b == 0 ? 40f : 15f;
            }

            // ---- 3. the rattle: the rim rolls round faster and faster while the lean dies away
            Sfx.Play("coin_land", root.position, 0.35f, 1.5f);
            float lean = Mathf.Abs(tilt), phase = tilt > 0 ? 0f : Mathf.PI;
            for (float t = 0; t < 1.0f; t += Time.deltaTime * Slow)
            {
                float a = lean * Mathf.Exp(-4.2f * t) * (1f - t);
                phase += (9f + 34f * t) * Time.deltaTime * Slow;
                var axis = new Vector3(Mathf.Cos(phase), 0, Mathf.Sin(phase));
                coin.localPosition = p + Vector3.up * (rest + Mathf.Sin(a * Mathf.Deg2Rad) * R);
                coin.localRotation = Quaternion.AngleAxis(a, axis) * Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(endFlip, 0, 0);
                yield return null;
            }
            coin.localPosition = p + Vector3.up * rest;
            coin.localRotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(endFlip, 0, 0);
            XRIn.HapticBoth(0.4f, 0.1f);
            yield return new WaitForSeconds(0.35f);
            if (done != null) done(heads);
            yield return new WaitForSeconds(linger);
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
