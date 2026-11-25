using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>A zero-proof mocktail for the player: grab it (grip) and lift it to your mouth to sip — the glass tips,
    /// the level drops, you hear it and feel it. On desktop, click the glass to take a sip.</summary>
    public class PlayerMocktail : MonoBehaviour
    {
        Transform liquid; float level = 1f, nextSip; XRGrabInteractable grab; int sips;
        public static PlayerMocktail Make(Vector3 pos, int kind)
        {
            var go = new GameObject("Mocktail"); go.transform.position = pos;
            var m = go.AddComponent<PlayerMocktail>();
            var k = new MeshKit();
            k.Cylinder(new Vector3(0, 0.075f, 0), 0.034f, 0.042f, 0.15f, 16, Quaternion.identity, new Color(0.75f, 0.85f, 0.95f), 0.15f, false);
            k.Cylinder(new Vector3(0, 0.004f, 0), 0.036f, 0.036f, 0.008f, 16, Quaternion.identity, new Color(0.8f, 0.9f, 1f), 0.2f);
            k.Spawn("Glass", go.transform, Palette.Glossy, false);
            var c = kind % 3 == 0 ? Palette.Magenta : kind % 3 == 1 ? Palette.Teal : Palette.Gold;
            var lq = new MeshKit(); lq.Cylinder(new Vector3(0, 0.06f, 0), 0.031f, 0.037f, 0.12f, 16, Quaternion.identity, c, 0.9f);
            m.liquid = lq.Spawn("Liquid", go.transform, Palette.Neon, false).transform;
            var straw = new MeshKit(); straw.Bar(new Vector3(0.014f, 0.1f, 0), new Vector3(0.03f, 0.21f, 0), 0.004f, Palette.Mint, 1f); straw.Spawn("Straw", go.transform, Palette.Neon, false);
            var col = go.AddComponent<CapsuleCollider>(); col.center = new Vector3(0, 0.08f, 0); col.radius = 0.042f; col.height = 0.16f;
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.3f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            m.grab = go.AddComponent<XRGrabInteractable>();
            m.grab.throwOnDetach = false;
            m.grab.selectEntered.AddListener(_ => { Sfx.Play("chip_grab", go.transform.position, 0.4f, 1.6f); });
            return m;
        }

        void Update()
        {
            // left behind on a floor that got switched off: it falls through the (inactive) floors below — clean it up
            if (transform.position.y < -5f) { Destroy(gameObject); return; }
            if (!Rig.Ready || level <= 0f) return;
            // VR: held and the rim at your lips
            if (grab != null && grab.isSelected)
            {
                var rim = transform.position + transform.up * 0.15f;
                var mouth = Rig.I.Head.position + Vector3.down * 0.08f + Rig.I.Head.forward * 0.05f;
                if ((rim - mouth).sqrMagnitude < 0.12f * 0.12f && Time.time > nextSip) Sip();
            }
        }

        /// <summary>One sip (also what a desktop click does).</summary>
        public void Sip()
        {
            if (level <= 0f || Time.time < nextSip) return;
            nextSip = Time.time + 1.1f;
            if (grab == null || !grab.isSelected) StartCoroutine(DesktopSip());
            level = Mathf.Max(0f, level - 0.25f); sips++;
            liquid.localScale = new Vector3(1f, Mathf.Max(0.02f, level), 1f);
            Sfx.Play("drink_sip", Rig.I.Head.position, 0.7f, Random.Range(0.92f, 1.08f));
            XRIn.HapticBoth(0.12f, 0.15f);
            if (level <= 0f)
            {
                GameState.I.Notify("CHEERS! · ZERO PROOF, FULL STYLE", Palette.Mint);
                if (PlayerPrefs.GetInt("asc2_mocktail", 0) == 0) { PlayerPrefs.SetInt("asc2_mocktail", 1); GameState.I.Unlock("ON THE HOUSE", "FINISHED A MOCKTAIL AT THE HIGH BAR", 150); }
                Destroy(gameObject, 20f);
            }
        }

        IEnumerator DesktopSip()
        {
            var rb = GetComponent<Rigidbody>(); rb.isKinematic = true;
            Vector3 p0 = transform.position; Quaternion r0 = transform.rotation;
            var head = Rig.I.Head;
            for (float t = 0; t < 1f; t += Time.deltaTime)
            {
                float k = Mathf.Sin(t * Mathf.PI);
                var mouthPos = head.position + head.forward * 0.16f + Vector3.down * 0.16f;
                transform.position = Vector3.Lerp(p0, mouthPos, Fx.EaseInOut(Mathf.Clamp01(k * 1.6f)));
                transform.rotation = Quaternion.Slerp(r0, Quaternion.AngleAxis(-50f, head.right) * Quaternion.LookRotation(Vector3.ProjectOnPlane(head.forward, Vector3.up), Vector3.up), k);
                yield return null;
            }
            transform.SetPositionAndRotation(p0, r0);
            rb.isKinematic = false;
        }
    }
}
