using System;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Forwards fingertip touches from the button cap trigger.</summary>
    public class PokeRelay : MonoBehaviour
    {
        public PokeButton button;
        void OnTriggerEnter(Collider other) { if (other.GetComponent<Fingertip>() != null) button.Press(); }
    }
}
