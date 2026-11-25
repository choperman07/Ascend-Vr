using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ascend
{
    // ====================================================================== HUD
    /// <summary>Head-locked line at the top of the view: day, clock and quota (or what to do next).</summary>
    public class RunHud : MonoBehaviour
    {
        TextMeshPro t; Transform holder;
        public static RunHud Create()
        {
            var go = new GameObject("Run HUD");
            var h = go.AddComponent<RunHud>();
            h.t = Txt.Make("HudText", go.transform, Vector3.zero, "", 0.026f, Palette.Text, Palette.UI, TextAlignmentOptions.Center, 0.9f);
            h.t.outlineWidth = 0.22f; h.t.outlineColor = new Color32(10, 6, 20, 255);
            h.t.fontSharedMaterial = h.t.fontMaterial;   // own material for the outline
            return h;
        }
        public void Set(string s) { if (t != null) t.text = s; }
        void LateUpdate()
        {
            if (!Rig.Ready) return;
            var cam = Rig.I.Head;
            transform.position = cam.position + cam.forward * 0.7f + cam.up * 0.24f;
            transform.rotation = Quaternion.LookRotation(cam.forward, cam.up);
        }
    }
}
