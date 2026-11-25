using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Calls back when a fingertip enters (mines tiles, pads).</summary>
    public class TileTouch : MonoBehaviour
    {
        public System.Action onTouch;
        void OnTriggerEnter(Collider other) { if (other.GetComponent<Fingertip>() != null && onTouch != null) onTouch(); }
    }
}
