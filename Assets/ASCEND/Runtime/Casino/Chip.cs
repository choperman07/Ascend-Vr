using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>A real, grabbable play chip. Its value already left your wallet while it is in your hand or on the table;
    /// chips left lying around return to your wallet by themselves.</summary>
    public class Chip : MonoBehaviour
    {
        public long value;
        public BetSpot spot;
        public ChipTray home;
        /// <summary>Fixed spin of the chip around its own axis (looks natural in a stack, never changes its height).</summary>
        [HideInInspector] public float yaw;
        Rigidbody rb;
        XRGrabInteractable grab;
        float looseSince = -1f;
        bool held, returning;
        // current placement (in the parent's space, so stacks follow a tilted felt)
        bool moving, placed; Transform tgtParent; Vector3 tgtLocal; Quaternion tgtRot;

        public static Chip Spawn(long value, Vector3 pos, Quaternion rot, ChipTray home)
        {
            var go = ChipKit.Visual(value, null, pos);
            go.transform.rotation = rot;
            var c = go.AddComponent<Chip>();
            c.value = value; c.home = home; c.yaw = Random.Range(0f, 360f);
            var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(ChipKit.Radius * 1.8f, ChipKit.Thick, ChipKit.Radius * 1.8f);
            c.rb = go.AddComponent<Rigidbody>();
            c.rb.mass = 0.012f; c.rb.interpolation = RigidbodyInterpolation.Interpolate; c.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            c.rb.isKinematic = true;
            c.grab = go.AddComponent<XRGrabInteractable>();
            c.grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            c.grab.throwOnDetach = true;
            c.grab.useDynamicAttach = true;
            c.grab.attachEaseInTime = 0.05f;
            c.grab.selectEntered.AddListener(c.OnGrab);
            c.grab.selectExited.AddListener(c.OnRelease);
            return c;
        }

        void OnGrab(SelectEnterEventArgs a)
        {
            held = true;
            Halt();
            if (spot != null) { spot.Remove(this); }
            transform.SetParent(null, true);
            Sfx.Play("chip_grab", transform.position, 0.6f);
            XRIn.HapticBoth(0.12f, 0.02f);
            if (home != null) home.Taken(this);
        }

        void OnRelease(SelectExitEventArgs a)
        {
            held = false;
            if (rb != null) { rb.isKinematic = false; rb.useGravity = true; }
            looseSince = Time.time;
            // let go while already inside a bet cell: OnTriggerEnter fired while it was held, so place it now
            TryDropOnSpot(null);
        }

        public void SetGrabbable(bool on) { if (grab != null) grab.enabled = on; }
        public bool Grabbable { get { return grab != null && grab.enabled; } }

        /// <summary>Stops any placement animation where it is (the chip is about to be animated by someone else).</summary>
        public void Halt()
        {
            if (moving) StopAllCoroutines();
            moving = false;
        }

        /// <summary>Freeze into a bet stack (kept for older callers: flat on the parent, its own spin).</summary>
        public void Snap(Transform parent, Vector3 world)
        {
            Place(parent, world, (parent != null ? parent.rotation : Quaternion.identity) * Quaternion.Euler(0, yaw, 0));
        }

        /// <summary>Freezes the chip and moves it (short hop) to an exact resting pose. Returns the flight time.</summary>
        public float Place(Transform parent, Vector3 world, Quaternion rot)
        {
            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
            looseSince = -1f;
            if (transform.parent != parent) transform.SetParent(parent, true);
            var local = parent != null ? parent.InverseTransformPoint(world) : world;
            var lrot = parent != null ? Quaternion.Inverse(parent.rotation) * rot : rot;
            // already there / already flying there: leave it alone (no restart, no spin)
            if (placed && tgtParent == parent && (tgtLocal - local).sqrMagnitude < 1e-8f && Quaternion.Angle(tgtRot, lrot) < 0.5f)
            {
                if (moving) return 0.05f;
                if ((Pos - local).sqrMagnitude < 1e-8f) return 0f;
            }
            tgtParent = parent; tgtLocal = local; tgtRot = lrot; placed = true;
            StopAllCoroutines();
            float dist = (transform.position - world).magnitude;
            if (!isActiveAndEnabled || dist < 1e-4f) { Finish(); return 0f; }
            float dur = Mathf.Clamp(0.1f + dist * 0.45f, 0.1f, 0.42f);
            moving = true;
            StartCoroutine(MoveTo(dur, dist > 0.08f ? Mathf.Min(0.12f, dist * 0.3f) : 0f));
            return dur;
        }

        /// <summary>Puts the chip at its resting pose at once.</summary>
        public void PlaceNow(Transform parent, Vector3 world, Quaternion rot)
        {
            Place(parent, world, rot);
            Halt(); Finish();
        }

        Vector3 Pos { get { return tgtParent != null ? transform.localPosition : transform.position; } }

        void SetPose(Vector3 p, Quaternion r)
        {
            if (tgtParent != null) { transform.localPosition = p; transform.localRotation = r; }
            else { transform.position = p; transform.rotation = r; }
        }

        IEnumerator MoveTo(float dur, float lift)
        {
            Vector3 p0 = Pos;
            Quaternion r0 = tgtParent != null ? transform.localRotation : transform.rotation;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float k = Fx.EaseInOut(t / dur);
                // the hop rises along the felt normal (parent up) and ends exactly on the stack: never left hovering
                SetPose(Vector3.Lerp(p0, tgtLocal, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * lift, Quaternion.Slerp(r0, tgtRot, k));
                yield return null;
            }
            Finish();
        }

        void Finish()
        {
            moving = false;
            if (!placed || transform.parent != tgtParent) return;   // never placed / taken elsewhere since
            SetPose(tgtLocal, tgtRot);
        }

        // the floor switched off mid-flight: the coroutine dies with it, so land the chip instead of leaving it mid-air
        void OnDisable() { if (moving) Finish(); }

        void OnTriggerEnter(Collider other)
        {
            if (held || spot != null || returning) return;
            var s = other.GetComponent<BetSpot>();
            if (s == null) return;
            TryDropOnSpot(s);
        }

        /// <summary>Several spots can overlap (a split sits on the line between two numbers): picks the best match.</summary>
        bool TryDropOnSpot(BetSpot hint)
        {
            if (held || spot != null || returning) return false;
            BetSpot best = hint; float bd = hint != null ? hint.Affinity(transform.position) : float.MaxValue;
            foreach (var h in Physics.OverlapSphere(transform.position, 0.03f, ~0, QueryTriggerInteraction.Collide))
            {
                var o = h.GetComponent<BetSpot>();
                if (o == null || o == hint || (hint != null && o.game != hint.game)) continue;
                float d = o.Affinity(transform.position);
                if (d < bd) { bd = d; best = o; }
            }
            return best != null && best.TryAdd(this);
        }

        void OnCollisionEnter(Collision col)
        {
            // dropped on the table body, rail, side table or rack but not in a bet cell: the felt sits above the body's
            // collider, so instead of sinking into the table it slides straight back to your rack
            if (held || spot != null || returning || looseSince < 0 || col.collider == null) return;
            if (col.collider.attachedRigidbody != null) return;
            if (col.collider.GetComponentInParent<TableGame>() == null) return;
            StartCoroutine(ReturnToWallet());
        }

        void Update()
        {
            if (held || spot != null || returning || looseSince < 0) return;
            bool fell = transform.position.y < (FloorManager.I != null ? Tower.Y(FloorManager.I.Current) - 0.5f : -50f);
            bool resting = rb != null && !rb.isKinematic && rb.linearVelocity.sqrMagnitude < 0.0004f;
            if (fell || (resting && Time.time - looseSince > 2.5f) || Time.time - looseSince > 8f) StartCoroutine(ReturnToWallet());
        }

        /// <summary>Unused chip: back to the wallet with a little flight.</summary>
        public IEnumerator ReturnToWallet()
        {
            if (returning) yield break;
            returning = true;
            SetGrabbable(false);
            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
            Vector3 target = home != null ? home.transform.position + home.transform.up * 0.05f : (Rig.I != null ? Rig.I.Head.position + Vector3.down * 0.5f : transform.position);
            yield return StartCoroutine(Fx.Arc(transform, target, 0.15f, 0.35f));
            GameState.I.ReturnChips(value);
            Sfx.Chip(target, 0.5f);
            Destroy(gameObject);
        }
    }
}
