using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Keeps your body out of tables, machines, walls and other people — also when you walk in your real room
    /// (the CharacterController only blocks stick locomotion). A capsule follows your head; anything solid it overlaps
    /// pushes the rig back out horizontally. If your head still ends up inside geometry the view dims so you never see
    /// through a table or a guest.</summary>
    public class PlayerBody : MonoBehaviour
    {
        public float radius = 0.2f;
        CapsuleCollider probe;
        readonly Collider[] near = new Collider[32];
        float dim;

        void Start()
        {
            var go = new GameObject("BodyProbe");
            go.transform.SetParent(transform, false);
            probe = go.AddComponent<CapsuleCollider>();
            probe.isTrigger = true;               // only used for ComputePenetration, never collides by itself
            probe.radius = radius; probe.direction = 1;
        }

        static bool Ignore(Collider c, Transform rig)
        {
            if (c.isTrigger) return true;
            if (c.transform.IsChildOf(rig)) return true;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return true;   // chips, dice, balls
            if (c.GetComponentInParent<XRGrabInteractable>() != null) return true;
            return false;
        }

        void LateUpdate()
        {
            var rig = Rig.I; if (rig == null || rig.Head == null || probe == null) return;
            if (PlayerSeating.Seated || rig.Frozen) { SetDim(0f); return; }
            var head = rig.Head.position;
            float feet = rig.transform.position.y;
            float top = head.y + 0.08f, bottom = feet + 0.35f;
            float h = Mathf.Max(radius * 2f, top - bottom);
            var center = new Vector3(head.x, (top + bottom) * 0.5f, head.z);
            probe.transform.position = center; probe.transform.rotation = Quaternion.identity;
            probe.height = h;
            int n = Physics.OverlapCapsuleNonAlloc(new Vector3(center.x, bottom + radius, center.z), new Vector3(center.x, top - radius, center.z), radius, near, ~0, QueryTriggerInteraction.Ignore);
            Vector3 push = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var c = near[i];
                if (c == null || Ignore(c, rig.transform)) continue;
                Vector3 dir; float dist;
                if (Physics.ComputePenetration(probe, probe.transform.position, probe.transform.rotation, c, c.transform.position, c.transform.rotation, out dir, out dist))
                {
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-4f) continue;
                    push += dir.normalized * dist;
                }
            }
            if (push.sqrMagnitude > 1e-6f)
            {
                push = Vector3.ClampMagnitude(push, 0.25f);
                var cc = rig.GetComponent<CharacterController>();
                bool on = cc != null && cc.enabled; if (on) cc.enabled = false;
                rig.transform.position += push;
                if (on) cc.enabled = true;
            }
            // head inside something (leaning over/into a table or a person): dim the view
            bool inside = Physics.CheckSphere(rig.Head.position, 0.09f, ~0, QueryTriggerInteraction.Ignore) && HeadBlocked(rig);
            SetDim(inside ? 0.85f : 0f);
        }

        bool HeadBlocked(Rig rig)
        {
            int n = Physics.OverlapSphereNonAlloc(rig.Head.position, 0.09f, near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (near[i] != null && !Ignore(near[i], rig.transform)) return true;
            return false;
        }

        void SetDim(float target)
        {
            float prev = dim;
            dim = Mathf.MoveTowards(dim, target, Time.deltaTime * 4f);
            if (Mathf.Abs(prev - dim) > 1e-4f || (target == 0f && prev > 0f)) Rig.I.SetFade(dim);
        }
    }
}
