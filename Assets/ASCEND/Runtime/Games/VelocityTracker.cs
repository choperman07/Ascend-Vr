using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Tracks a transform's velocity (controllers) for gestures: shakes, flicks, casts.</summary>
    public class VelocityTracker : MonoBehaviour
    {
        public Vector3 velocity;
        Vector3 last; bool init;
        public static VelocityTracker On(Transform t)
        {
            var v = t.GetComponent<VelocityTracker>();
            if (v == null) v = t.gameObject.AddComponent<VelocityTracker>();
            return v;
        }
        void LateUpdate()
        {
            if (init) velocity = Vector3.Lerp(velocity, (transform.position - last) / Mathf.Max(Time.deltaTime, 1e-4f), 0.5f);
            last = transform.position; init = true;
        }
    }
}
