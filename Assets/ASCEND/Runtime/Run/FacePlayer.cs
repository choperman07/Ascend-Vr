using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ascend
{
    /// <summary>Turns a world panel / sign towards the player (yaw only), so it always reads the right way round.</summary>
    public class FacePlayer : MonoBehaviour
    {
        public float maxDist = 12f;
        void LateUpdate()
        {
            if (!Rig.Ready) return;
            var d = transform.position - Rig.I.Head.position; d.y = 0;
            if (d.sqrMagnitude < 1e-4f || d.sqrMagnitude > maxDist * maxDist) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d.normalized), 1f - Mathf.Exp(-8f * Time.deltaTime));
        }
    }
}
