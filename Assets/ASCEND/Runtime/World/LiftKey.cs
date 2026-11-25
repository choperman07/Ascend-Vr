using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Wide rectangular lift key: fingertip touch (TileTouch), controller ray (XRSimpleInteractable) or mouse
    /// click (DesktopTester → TileTouch). The cap sinks and flashes when pressed.</summary>
    public class LiftKey : MonoBehaviour
    {
        public Action onPress;
        public TextMeshPro label, name_, tier;
        public GameObject lockIcon;
        Transform cap; Renderer capR;
        Vector3 capRest;
        Color baseCol = Color.gray; float baseEmit = 0.1f;
        float press, last = -10f;
        const float Cooldown = 0.45f;

        public static LiftKey Create(string name, Transform parent, Vector3 pos, Vector3 capPos, Vector2 capSize, Vector3 hitCenter, Vector3 hitSize,
            string text, TMP_FontAsset font, float em, Action onPress)
        {
            var root = new GameObject("LiftKey_" + name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            var key = root.AddComponent<LiftKey>();
            key.onPress = onPress;
            var ck = new MeshKit();
            ck.Box(Vector3.zero, new Vector3(capSize.x, capSize.y, 0.016f), Color.white, 0.2f);
            ck.Box(new Vector3(0, capSize.y * 0.5f - 0.004f, -0.0085f), new Vector3(capSize.x - 0.01f, 0.003f, 0.001f), Color.white, 0.5f);   // light catch on the top edge
            var capGo = ck.Spawn("Cap", root.transform, capPos, Quaternion.identity, Palette.Neon, false);
            key.cap = capGo.transform; key.capR = capGo.GetComponent<Renderer>(); key.capRest = capPos;
            key.label = Txt.Make("Label", key.cap, new Vector3(0, 0, -0.0105f), text, em, Palette.Text, font, TextAlignmentOptions.Center, capSize.x);
            key.label.enableWordWrapping = false;
            var col = root.AddComponent<BoxCollider>(); col.center = hitCenter; col.size = hitSize;
            root.AddComponent<TileTouch>().onTouch = key.Press;
            var si = root.AddComponent<XRSimpleInteractable>();
            si.selectEntered.AddListener(_ => key.Press());
            return key;
        }

        public void Paint(Color c, float emit, Color ink)
        {
            baseCol = c; baseEmit = emit;
            Fx.SetColor(capR, c, emit);
            if (label != null) label.color = ink;
        }

        public void Press()
        {
            if (Time.time - last < Cooldown) return;
            last = Time.time;
            press = 1f;
            Sfx.Play("ui_click", transform.position, 0.8f, 1.15f);
            XRIn.HapticBoth(0.3f, 0.04f);
            if (onPress != null) onPress();
        }

        void Update()
        {
            if (press <= 0f) return;
            press = Mathf.MoveTowards(press, 0f, Time.deltaTime * 4f);
            cap.localPosition = capRest + new Vector3(0, 0, 0.008f * Mathf.Sin(press * Mathf.PI * 0.5f));
            Fx.SetColor(capR, Color.Lerp(baseCol, Color.white, press * 0.55f), baseEmit + press);
        }
    }
}
