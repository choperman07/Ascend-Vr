using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Ascend
{
    /// <summary>Single access point to the player's XR rig (head, controllers, locomotion) plus fade-teleports.
    /// Works with the XR Interaction Toolkit "XR Origin (XR Rig)" prefab and with the desktop fallback camera.</summary>
    public class Rig : MonoBehaviour
    {
        static Rig _i;
        public static Rig I
        {
            get
            {
                if (_i == null) _i = FindFirstObjectByType<Rig>();
                return _i;
            }
        }
        public static bool Ready { get { return I != null && I.Head != null; } }

        public XROrigin Origin { get; private set; }
        public Transform Head { get; private set; }
        public Transform LeftCtrl { get; private set; }
        public Transform RightCtrl { get; private set; }
        public Camera Cam { get; private set; }
        public Transform LeftPoke { get; private set; }
        public Transform RightPoke { get; private set; }

        Renderer fadeQuad;
        MaterialPropertyBlock fadeMpb;
        float fade;
        bool teleporting;
        public bool Frozen { get; private set; }
        public int Floor { get; set; }
        public event System.Action<int> FloorChanged;

        void Awake()
        {
            _i = this;
            Origin = GetComponent<XROrigin>();
            Cam = Origin != null && Origin.Camera != null ? Origin.Camera : GetComponentInChildren<Camera>(true);
            if (Cam == null) Cam = Camera.main;
            Head = Cam != null ? Cam.transform : transform;
            LeftCtrl = FindDeep(transform, "Left Controller") ?? FindDeep(transform, "LeftHand Controller");
            RightCtrl = FindDeep(transform, "Right Controller") ?? FindDeep(transform, "RightHand Controller");
            if (LeftCtrl == null) LeftCtrl = MakeDesktopHand("Left Controller", new Vector3(-0.22f, -0.3f, 0.35f));
            if (RightCtrl == null) RightCtrl = MakeDesktopHand("Right Controller", new Vector3(0.22f, -0.3f, 0.35f));
            LeftPoke = FindDeep(LeftCtrl, "Poke Interactor");
            RightPoke = FindDeep(RightCtrl, "Poke Interactor");
            HideControllerModels();
            BuildFade();
        }

        Transform MakeDesktopHand(string name, Vector3 offset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Head, false);
            go.transform.localPosition = offset;
            go.transform.localRotation = Quaternion.Euler(20, 0, 0);
            return go.transform;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>Our finger hands replace the controller models.</summary>
        void HideControllerModels()
        {
            foreach (var n in new[] { "Left Controller Visual", "Right Controller Visual", "Poke Point Affordances" })
            {
                foreach (var t in GetComponentsInChildren<Transform>(true))
                    if (t.name == n) foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }
        }

        void BuildFade()
        {
            if (Head == null) return;
            var k = new MeshKit();
            k.Quad(Vector3.zero, new Vector2(2, 2), Quaternion.identity, Color.white, 1f);
            var go = k.Spawn("Fade", Head, new Vector3(0, 0, 0.12f), Quaternion.identity, Palette.Fade, false);
            fadeQuad = go.GetComponent<Renderer>();
            fadeMpb = new MaterialPropertyBlock();
            SetFade(0f);
        }

        public bool Teleporting { get { return teleporting; } }

        public void SetFade(float a)
        {
            fade = a;
            if (fadeQuad == null) return;
            fadeQuad.enabled = a > 0.001f;
            fadeMpb.SetColor("_BaseColor", new Color(0, 0, 0, a));
            fadeQuad.SetPropertyBlock(fadeMpb);
        }

        /// <summary>Feet position on the floor below the head.</summary>
        public Vector3 Feet
        {
            get
            {
                var p = Head.position;
                p.y = Origin != null ? Origin.transform.position.y : p.y - 1.6f;
                return p;
            }
        }

        public Vector3 HeadForwardFlat { get { var f = Head.forward; f.y = 0; return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward; } }

        // a teleport coroutine cut by deactivation would leave "teleporting" set (every later teleport ignored) and the
        // view black
        void OnDisable() { if (teleporting) { teleporting = false; SetFade(0f); } }

        /// <summary>Fade out, move feet to the target (facing yaw), fade in.</summary>
        public void Teleport(Vector3 feet, float yaw, int floor = -1, string sfx = "teleport")
        {
            if (teleporting) return;
            StartCoroutine(TeleportRoutine(feet, yaw, floor, sfx));
        }

        IEnumerator TeleportRoutine(Vector3 feet, float yaw, int floor, string sfx)
        {
            teleporting = true;
            if (!string.IsNullOrEmpty(sfx)) Sfx.UI(sfx, 0.8f);
            for (float t = 0; t < 0.22f; t += Time.deltaTime) { SetFade(t / 0.22f); yield return null; }
            SetFade(1f);
            if (floor >= 0 && floor != Floor) { Floor = floor; if (FloorChanged != null) FloorChanged(floor); }
            yield return null;
            Place(feet, yaw);
            yield return new WaitForSeconds(0.08f);
            for (float t = 0; t < 0.3f; t += Time.deltaTime) { SetFade(1f - t / 0.3f); yield return null; }
            SetFade(0f);
            teleporting = false;
        }

        /// <summary>Instant move (no fade).</summary>
        public void Place(Vector3 feet, float yaw)
        {
            var cc = GetComponent<CharacterController>();
            bool ccOn = cc != null && cc.enabled;
            if (ccOn) cc.enabled = false;
            if (Origin != null)
            {
                float headYaw = Head.eulerAngles.y;
                Origin.RotateAroundCameraUsingOriginUp(yaw - headYaw);
                var camLocal = Origin.CameraInOriginSpacePos;
                Origin.MoveCameraToWorldLocation(feet + Vector3.up * camLocal.y);
            }
            else
            {
                transform.position = feet;
                transform.rotation = Quaternion.Euler(0, yaw, 0);
            }
            Physics.SyncTransforms();
            if (ccOn) cc.enabled = true;
        }

        /// <summary>Locks locomotion (security escort, cut-scenes).</summary>
        public void Freeze(bool on)
        {
            Frozen = on;
            var loco = FindDeep(transform, "Locomotion");
            if (loco != null) loco.gameObject.SetActive(!on);
        }
    }
}
