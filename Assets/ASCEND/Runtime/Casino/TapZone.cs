using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>Felt area that detects a fingertip tap.</summary>
    public class TapZone : MonoBehaviour
    {
        public CardTable table;
        void OnTriggerEnter(Collider other) { if (other.GetComponent<Fingertip>() != null && table != null) table.Tapped(); }
    }
}
