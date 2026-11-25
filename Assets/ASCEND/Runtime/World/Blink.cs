using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ascend
{
    /// <summary>Aircraft warning light.</summary>
    public class Blink : MonoBehaviour
    {
        Vector3 s;
        void Start() { s = transform.localScale; }
        void Update() { transform.localScale = s * (Mathf.Repeat(Time.time, 1.6f) < 0.25f ? 1f : 0.05f); }
    }
}
