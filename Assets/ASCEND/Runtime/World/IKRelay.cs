using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>Forwards Mecanim's IK callback (only sent to the Animator's own GameObject) to the body.</summary>
    public class IKRelay : MonoBehaviour
    {
        public HumanoidBody body;
        void OnAnimatorIK(int layer) { if (body != null) body.DoIK(layer); }
    }
}
