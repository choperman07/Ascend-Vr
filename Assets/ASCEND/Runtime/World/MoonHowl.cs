using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Stare at the moon from the rooftop for a few seconds…</summary>
    public class MoonHowl : MonoBehaviour
    {
        float stare; bool done;
        void Update()
        {
            if (done || !Rig.Ready) return;
            var d = (transform.position - Rig.I.Head.position).normalized;
            if (Vector3.Dot(Rig.I.Head.forward, d) > 0.985f) stare += Time.deltaTime; else stare = Mathf.Max(0, stare - Time.deltaTime);
            if (stare > 4f)
            {
                done = true;
                Sfx.UI("howl", 1f);
                GameState.I.Unlock("LUNATIC", "HOWLED AT THE MOON FROM THE ROOFTOP", 300);
            }
        }
    }
}
