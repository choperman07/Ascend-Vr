using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    public class PlinkoIntake : MonoBehaviour
    {
        public Plinko game;
        void OnTriggerEnter(Collider other)
        {
            var b = other.GetComponentInParent<PlinkoBall>();
            if (b != null && !b.Held) game.Drop(b);
        }
    }
}
