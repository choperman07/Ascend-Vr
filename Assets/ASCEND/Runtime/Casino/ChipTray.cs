using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Your round chip rack (on the side table by your seat): grab a chip of any value (it comes out of your
    /// wallet), drop it on a bet spot. Local -Z faces the player.</summary>
    public class ChipTray : MonoBehaviour
    {
        /// <summary>Radius of the circle the five chip wells sit on, and the height of a well floor.</summary>
        public const float WellRing = 0.068f, WellFloor = 0.016f;
        /// <summary>Outer radius of the rack.</summary>
        public const float Outer = 0.106f;
        const int StackN = 6;
        readonly Chip[] top = new Chip[5];
        readonly Vector3[] slots = new Vector3[5];

        void Start()
        {
            var k = new MeshKit();
            // round puck with a gold lip
            k.Cylinder(new Vector3(0, 0.007f, 0), Outer, Outer - 0.004f, 0.014f, 40, Quaternion.identity, Palette.Dark, 0f);
            k.Torus(new Vector3(0, 0.0138f, 0), Outer - 0.003f, 0.0032f, 40, 6, Quaternion.identity, Palette.Gold, 1f);
            k.Cylinder(new Vector3(0, 0.0148f, 0), 0.016f, 0.016f, 0.0024f, 16, Quaternion.identity, Palette.Gold, 0.5f);
            for (int i = 0; i < 5; i++)
            {
                // 100 back-left → 25K back-right, the 1K well right in front of you
                float a = (-144f + i * 72f) * Mathf.Deg2Rad;
                slots[i] = new Vector3(Mathf.Sin(a) * WellRing, 0, -Mathf.Cos(a) * WellRing);
                k.Cylinder(slots[i] + Vector3.up * (WellFloor - 0.0012f), 0.0255f, 0.0255f, 0.0024f, 20, Quaternion.identity, Palette.Darker, 0f);
                k.Torus(slots[i] + Vector3.up * (WellFloor - 0.001f), 0.026f, 0.0016f, 20, 4, Quaternion.identity, Palette.Gold, 0.5f);
            }
            k.Spawn("Tray", transform, Palette.Neon, false);
            for (int i = 0; i < 5; i++)
            {
                // static stack look (chips touching, lying in the well)
                var stack = new MeshKit();
                for (int j = 0; j < StackN; j++) stack.Append(ChipKit.MeshFor(i), Matrix4x4.TRS(slots[i] + Vector3.up * (WellFloor + ChipKit.Thick * (j + 0.5f)), Quaternion.Euler(0, j * 37f, 0), Vector3.one), Color.white, 0.05f);
                stack.Spawn("Stack" + i, transform, Palette.Textured("chips"), false);
                // small value tag floating just above each stack, tilted towards you
                Txt.Make("V" + i, transform, slots[i] + new Vector3(0, WellFloor + ChipKit.Thick * (StackN + 1) + 0.014f, 0), ChipKit.Values[i] >= 1000 ? (ChipKit.Values[i] / 1000) + "K" : ChipKit.Values[i].ToString(), 0.016f, Palette.Text, Palette.Display).transform.localRotation = Quaternion.Euler(40, 0, 0);
            }
            GameState.I.Changed += Refill;
            Refill();
        }

        void OnDestroy() { if (GameState.Exists) GameState.I.Changed -= Refill; }

        /// <summary>The grabbable chip lies exactly on top of the static stack.</summary>
        Vector3 TopPos(int i) { return transform.TransformPoint(slots[i] + Vector3.up * (WellFloor + ChipKit.Thick * (StackN + 0.5f))); }

        public void Taken(Chip c)
        {
            for (int i = 0; i < 5; i++) if (top[i] == c)
            {
                top[i] = null;
                c.home = this;
                if (!GameState.I.TakeChips(c.value)) { Destroy(c.gameObject); }
                StartCoroutine(RefillLater());
            }
        }

        IEnumerator RefillLater() { yield return new WaitForSeconds(0.35f); Refill(); }

        void Refill()
        {
            if (!isActiveAndEnabled) return;
            for (int i = 0; i < 5; i++)
            {
                bool afford = GameState.I.chips >= ChipKit.Values[i];
                if (top[i] == null && afford)
                {
                    top[i] = Chip.Spawn(ChipKit.Values[i], TopPos(i), transform.rotation * Quaternion.Euler(0, StackN * 37f, 0), this);
                    top[i].transform.SetParent(transform, true);
                }
                else if (top[i] != null && !afford) { Destroy(top[i].gameObject); top[i] = null; }
            }
        }
    }
}
