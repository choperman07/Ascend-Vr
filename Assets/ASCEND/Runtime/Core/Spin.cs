using UnityEngine;

namespace Ascend
{
    /// <summary>Constant rotation (local axes).</summary>
    public class Spin : MonoBehaviour
    {
        public Vector3 speed = new Vector3(0, 30, 0);
        void Update() { transform.Rotate(speed * Time.deltaTime, Space.Self); }
    }
}
