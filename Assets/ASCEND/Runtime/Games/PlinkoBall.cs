using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    public class PlinkoBall : MonoBehaviour
    {
        public Plinko game;
        XRGrabInteractable g;
        public bool Held { get { if (g == null) g = GetComponent<XRGrabInteractable>(); return g != null && g.isSelected; } }
        float lowSince = -1;
        void Update()
        {
            // balls that fall off the counter come back to the hopper
            // while held, XRI unparents the ball: measure against the station, not transform.parent (null → exception)
            if (game == null) return;
            if (!Held && transform.position.y < game.transform.position.y + 0.3f)
            {
                if (lowSince < 0) lowSince = Time.time;
                if (Time.time - lowSince > 2f) { game.SpawnBall(new Vector3(-0.62f, game.TableY + 0.1f, -0.3f)); Destroy(gameObject); }
            }
            else lowSince = -1;
        }
    }
}
