using UnityEngine;

namespace Ascend
{
    /// <summary>Gentle floating motion.</summary>
    public class Bob : MonoBehaviour
    {
        public float amplitude = 0.05f, speed = 1.4f, phase;
        Vector3 basePos; bool init;
        /// <summary>Use the current local position as the new rest position (after the object was moved / reparented).</summary>
        public void Rebase() { init = false; }
        void Update()
        {
            if (!init) { basePos = transform.localPosition; init = true; }
            transform.localPosition = basePos + Vector3.up * Mathf.Sin(Time.time * speed + phase) * amplitude;
        }
    }
}
