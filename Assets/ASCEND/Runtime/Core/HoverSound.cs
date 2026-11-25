using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Ascend
{
    /// <summary>Soft tick when a pointer hovers a button.</summary>
    public class HoverSound : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler
    {
        float last;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e)
        {
            if (Time.unscaledTime - last < 0.08f) return;
            last = Time.unscaledTime;
            Sfx.UI("ui_hover", 0.35f);
            XRIn.HapticBoth(0.08f, 0.015f);
        }
    }
}
