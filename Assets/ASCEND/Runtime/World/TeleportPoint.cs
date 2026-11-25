using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>Named teleport destination (wrist menu). Placed by the world builder.</summary>
    public class TeleportPoint : MonoBehaviour
    {
        public string label = "SPOT";
        public int floor;
        public bool main;   // shown in the floor list (the elevator arrival)
        static readonly List<TeleportPoint> all = new List<TeleportPoint>();
        public static IList<TeleportPoint> All { get { return all; } }
        void Awake() { if (!all.Contains(this)) all.Add(this); }
        void OnDestroy() { all.Remove(this); }
    }
}
