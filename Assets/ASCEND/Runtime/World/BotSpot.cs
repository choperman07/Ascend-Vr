using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Ascend
{
    /// <summary>Activity spot for simulated guests (placed by the world builder).</summary>
    public class BotSpot : MonoBehaviour
    {
        public SpotKind kind = SpotKind.Wander;
        public int floor;
        [HideInInspector] public bool taken;
        [HideInInspector] public bool faced;
    }
}
