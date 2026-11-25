using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    public class AutoHide : MonoBehaviour
    {
        public float until;
        void Update() { if (Time.time > until) gameObject.SetActive(false); }
    }
}
