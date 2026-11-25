using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>On a guard: notices chips thrown at them and players poking them (hand in VR, click on desktop).</summary>
    public class GuardHitbox : MonoBehaviour
    {
        public AvatarBody guard;
        public GuardPost post;
        float next, nextPoke;
        void OnTriggerEnter(Collider other)
        {
            // hands only: the rig's CharacterController brushing past the guard is not a poke
            if (Rig.Ready && other.transform.IsChildOf(Rig.I.transform)) { if (!(other is CharacterController)) Poke(); return; }
            if (Time.time < next) return;
            var c = other.GetComponentInParent<Chip>();
            if (c == null) return;
            var rb = c.GetComponent<Rigidbody>();
            if (rb != null && rb.linearVelocity.magnitude < 1.2f) return;
            next = Time.time + 3f;
            if (SecurityDirector.I != null) SecurityDirector.I.ChipHit(guard);
        }
        public void Poke()
        {
            if (Time.time < nextPoke) return;
            nextPoke = Time.time + 0.6f;
            if (post != null) post.Poked();
        }
    }
}
