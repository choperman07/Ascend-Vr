using UnityEngine;

namespace Ascend
{
    /// <summary>Turns a label / sprite towards the camera (yaw only unless full = true).</summary>
    public class Billboard : MonoBehaviour
    {
        public bool full;
        void LateUpdate()
        {
            var c = Camera.main; if (c == null) return;   // always the current main camera (the trailer swaps it)
            var cam = c.transform;
            Vector3 d = transform.position - cam.position;
            if (!full) d.y = 0;
            if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d);
        }
    }
}
