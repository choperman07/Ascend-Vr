using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    public class LuckyCat : MonoBehaviour
    {
        Transform head, tail;
        AudioSource purr;
        int pets; float lastPet, petting;
        void Start()
        {
            var k = new MeshKit();
            var black = Palette.Hex("15121C");
            k.Sphere(new Vector3(0, 0.09f, 0), new Vector3(0.09f, 0.09f, 0.14f), 12, black, 0.02f);
            k.Sphere(new Vector3(0, 0.04f, 0.09f), new Vector3(0.06f, 0.04f, 0.04f), 8, black, 0.02f);
            k.Spawn("Body", transform, Palette.Glossy, false);
            var hk = new MeshKit();
            hk.Sphere(Vector3.zero, new Vector3(0.07f, 0.065f, 0.065f), 12, black, 0.02f);
            hk.Box(new Vector3(-0.04f, 0.06f, 0), new Vector3(0.035f, 0.05f, 0.02f), Quaternion.Euler(0, 0, 20), black, 0.02f);
            hk.Box(new Vector3(0.04f, 0.06f, 0), new Vector3(0.035f, 0.05f, 0.02f), Quaternion.Euler(0, 0, -20), black, 0.02f);
            hk.Sphere(new Vector3(-0.025f, 0.01f, -0.058f), new Vector3(0.014f, 0.01f, 0.006f), 8, Palette.Gold, 1.5f);
            hk.Sphere(new Vector3(0.025f, 0.01f, -0.058f), new Vector3(0.014f, 0.01f, 0.006f), 8, Palette.Gold, 1.5f);
            hk.Sphere(new Vector3(0, -0.012f, -0.064f), 0.006f, 6, Palette.Coral, 0.6f);
            head = hk.Spawn("Head", transform, new Vector3(0, 0.2f, -0.1f), Quaternion.identity, Palette.Glossy, false).transform;
            var tk = new MeshKit(); tk.Capsule(Vector3.zero, new Vector3(0, 0.16f, 0.05f), 0.015f, 6, black, 0.02f);
            tail = tk.Spawn("Tail", transform, new Vector3(0, 0.06f, 0.14f), Quaternion.identity, Palette.Glossy, false).transform;
            var col = gameObject.AddComponent<SphereCollider>(); col.isTrigger = true; col.radius = 0.16f; col.center = new Vector3(0, 0.14f, 0);
            var ap = gameObject.AddComponent<AvatarProfile>(); ap.displayName = "LUCKY · THE HOUSE CAT"; ap.rarity = Rarity.Mythic; ap.xp = 777777; ap.status = "DOES NOT CHANGE THE ODDS. LIKES PETS.";
            purr = Sfx.Loop("cat_purr_loop", transform, 0f, 1f, 4f);
        }

        void OnTriggerStay(Collider other)
        {
            if (other.GetComponent<Fingertip>() == null && other.GetComponentInParent<HandRig>() == null) return;
            petting = 0.4f;
        }

        /// <summary>Desktop: a click on Lucky counts as a stroke (in VR you pet her with your hand).</summary>
        public void Pet() { petting = Mathf.Max(petting, 0.6f); }

        void Update()
        {
            petting -= Time.deltaTime;
            bool on = petting > 0f;
            if (purr != null) purr.volume = Mathf.MoveTowards(purr.volume, on ? 0.9f : 0f, Time.deltaTime * 2f);
            if (on && Time.time - lastPet > 2.5f)
            {
                lastPet = Time.time; pets++;
                if (Random.value < 0.5f) Sfx.Play("meow", head.position, 0.8f, Random.Range(0.9f, 1.15f));
                XRIn.HapticBoth(0.15f, 0.2f);
                if (pets == 5) { GameState.I.Unlock("CAT PERSON", "PETTED LUCKY FIVE TIMES", 250); Sfx.UI("secret_found", 0.7f); Voice.Say("bar_cat", transform, 0.4f); }
            }
            if (head != null)
            {
                Quaternion target = Quaternion.identity;
                if (Rig.Ready) { var d = Rig.I.Head.position - head.position; if (d.sqrMagnitude < 9f) target = Quaternion.Inverse(transform.rotation) * Quaternion.LookRotation(-d); }
                head.localRotation = Quaternion.Slerp(head.localRotation, on ? Quaternion.Euler(-15, 0, Mathf.Sin(Time.time * 3f) * 8f) : Quaternion.Slerp(Quaternion.identity, target, 0.6f), Time.deltaTime * 4f);
            }
            if (tail != null) tail.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * (on ? 4f : 1.3f)) * 25f, Mathf.Sin(Time.time * 0.9f) * 20f, 0);
        }
    }
}
