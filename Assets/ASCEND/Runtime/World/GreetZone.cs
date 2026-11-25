using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Fires once when the player's head enters (cooldown 30 s).</summary>
    public class GreetZone : MonoBehaviour
    {
        public System.Action onEnter;
        float next;
        SphereCollider c;
        void Update()
        {
            if (!Rig.Ready || Time.time < next) return;
            if (c == null) c = GetComponent<SphereCollider>();
            if ((Rig.I.Head.position - transform.position).sqrMagnitude < c.radius * c.radius) { next = Time.time + 30f; if (onEnter != null) onEnter(); }
        }
    }
}
