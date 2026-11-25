using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>The golden chip is hidden somewhere in the tower. Grab it once a day for a bonus.</summary>
    public class GoldenChip : MonoBehaviour
    {
        void Start()
        {
            if (PlayerPrefs.GetString("asc2_golden", "") == System.DateTime.Now.ToString("yyyyMMdd")) { gameObject.SetActive(false); return; }
            var go = ChipKit.Visual(25000, transform, transform.position);
            go.transform.localScale *= 1.4f;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) Fx.SetColor(r, Palette.Gold, 1f);
            Build.GlowSprite(transform, Vector3.zero, 0.25f, Palette.Gold, 0.6f);
            var col = gameObject.AddComponent<SphereCollider>(); col.radius = 0.08f;
            var rb = gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true;
            var g = gameObject.AddComponent<XRGrabInteractable>();
            g.selectEntered.AddListener(_ => Found());
            gameObject.AddComponent<Spin>().speed = new Vector3(0, 90, 0);
        }
        bool found;
        /// <summary>Grabbed in VR (or clicked on the desktop).</summary>
        public void Found()
        {
            if (found) return;
            found = true;
            PlayerPrefs.SetString("asc2_golden", System.DateTime.Now.ToString("yyyyMMdd"));
            Sfx.UI("secret_found", 1f);
            Juice.Sparks(transform.position, Palette.Gold, 60);
            GameState.I.Grant(7777, "GOLDEN CHIP");
            GameState.I.Unlock("GOLDEN CHIP", "FOUND THE HIDDEN GOLDEN CHIP", 777);
            StartCoroutine(Vanish());
        }
        IEnumerator Vanish() { yield return new WaitForSeconds(0.5f); gameObject.SetActive(false); }
    }
}
