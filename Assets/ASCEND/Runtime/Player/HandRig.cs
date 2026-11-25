using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>Hand with real finger joints driven by the Touch controller: grip curls middle/ring/little, trigger curls
    /// the index, resting the thumb on A/B/stick curls the thumb. Uses the XR Hands sample hand mesh when the world
    /// builder found it; otherwise builds a stylised neon hand. Also moves the XRI poke point to the index fingertip.</summary>
    public class HandRig : MonoBehaviour
    {
        public Hand hand = Hand.Right;
        [Tooltip("Skinned hand model (XR Hands sample LeftHand/RightHand.fbx instance). Optional.")]
        public Transform model;
        [Header("Fit to the controller")]
        public Vector3 wristOffset = new Vector3(0.03f, -0.035f, -0.095f);
        public float fingerPitch = 32f;
        public float scale = 1f;

        class Bone { public Transform t; public Quaternion rest; public Vector3 axis; public float max; }
        readonly List<Bone>[] fingers = new List<Bone>[5];   // thumb, index, middle, ring, little
        readonly float[] curl = new float[5], curlTarget = new float[5];
        Transform wrist, palm, indexTip, rootFit;
        Vector3 palmLocalN = Vector3.down;
        public Transform IndexTip { get { return indexTip; } }
        /// <summary>The wrist joint (the hand root when the model has none): where the sleeve starts.</summary>
        public Transform Wrist { get { return wrist != null ? wrist : transform; } }
        /// <summary>Direction the fingers point (the controller forward, tilted down like the hand model).</summary>
        public Vector3 HandForward { get { return transform.rotation * (Quaternion.AngleAxis(fingerPitch, Vector3.right) * Vector3.forward); } }
        /// <summary>Back of the hand (palms face each other on the Touch controllers).</summary>
        public Vector3 HandUp { get { return transform.rotation * (hand == Hand.Right ? Vector3.right : Vector3.left); } }
        public Transform Palm { get { return palm != null ? palm : transform; } }
        public Vector3 PalmNormal { get; private set; }
        Component pokeAttachOwner;
        Transform pokeAttach;
        Renderer[] renderers;

        static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };

        void Start()
        {
            for (int i = 0; i < 5; i++) fingers[i] = new List<Bone>();
            if (model == null) model = BuildFallback();
            Setup();
            var rig = Rig.I;
            if (rig != null)
            {
                var pokeObj = hand == Hand.Right ? rig.RightPoke : rig.LeftPoke;
                if (pokeObj != null) pokeAttach = FindAttach(pokeObj);
            }
            renderers = GetComponentsInChildren<Renderer>(true);
            ApplyTint();
            gameObject.AddComponent<PlayerArm>().Init(this);   // sleeve, elbow and (right) smartwatch
            if (GameState.Exists) GameState.I.Changed += ApplyTint;
        }

        void OnDestroy() { if (GameState.Exists) GameState.I.Changed -= ApplyTint; }

        static Transform FindAttach(Transform poke)
        {
            var pp = Rig.FindDeep(poke, "Poke Point");
            return pp != null ? pp : poke;
        }

        void ApplyTint()
        {
            if (renderers == null) return;
            var c = GameState.Exists ? Palette.RarityColor(GameState.I.nft) : Palette.Magenta;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_RimColor", c);
            foreach (var r in renderers) if (r != null && r.sharedMaterial == Palette.HandSkin) r.SetPropertyBlock(mpb);
        }

        Transform Find(string suffix)
        {
            string p = hand == Hand.Right ? "R_" : "L_";
            var t = Rig.FindDeep(model, p + suffix);
            if (t == null) t = Rig.FindDeep(model, (hand == Hand.Right ? "Right" : "Left") + suffix);
            return t;
        }

        void Setup()
        {
            wrist = Find("Wrist"); palm = Find("Palm");
            var midProx = Find("MiddleProximal"); var idxProx = Find("IndexProximal"); var litProx = Find("LittleProximal");
            indexTip = Find("IndexTip");
            if (wrist == null || midProx == null || idxProx == null || litProx == null) return;

            // 1) orient the whole model: fingers forward/down along the controller, palm facing inwards
            rootFit = model;
            Vector3 f = (midProx.position - wrist.position).normalized;
            Vector3 s = (idxProx.position - litProx.position).normalized;
            Vector3 palmN = Vector3.Cross(f, s).normalized * (hand == Hand.Right ? 1f : -1f);
            Quaternion modelFrame = Quaternion.LookRotation(f, -palmN);
            Vector3 tf = Quaternion.AngleAxis(fingerPitch, Vector3.right) * Vector3.forward;   // forward, tilted down
            Vector3 tPalm = hand == Hand.Right ? Vector3.left : Vector3.right;                   // palm faces the other hand
            Quaternion target = transform.rotation * Quaternion.LookRotation(tf, -tPalm);
            model.rotation = target * Quaternion.Inverse(modelFrame) * model.rotation;
            model.localScale *= scale;
            var off = new Vector3(hand == Hand.Right ? wristOffset.x : -wristOffset.x, wristOffset.y, wristOffset.z);
            model.position += transform.TransformPoint(off) - wrist.position;

            // 2) curl axes from the rest pose
            f = (midProx.position - wrist.position).normalized;
            s = (idxProx.position - litProx.position).normalized;
            palmN = Vector3.Cross(f, s).normalized * (hand == Hand.Right ? 1f : -1f);
            for (int i = 0; i < 5; i++)
            {
                string[] joints = i == 0 ? new[] { "Metacarpal", "Proximal", "Distal" } : new[] { "Proximal", "Intermediate", "Distal" };
                float[] max = i == 0 ? new[] { 20f, 35f, 55f } : new[] { 72f, 95f, 62f };
                for (int j = 0; j < joints.Length; j++)
                {
                    var b = Find(FingerNames[i] + joints[j]);
                    if (b == null) continue;
                    Transform child = b.childCount > 0 ? b.GetChild(0) : null;
                    Vector3 dir = child != null ? (child.position - b.position).normalized : f;
                    Vector3 toward = i == 0 ? (palmN * 0.7f + (wrist.position - b.position).normalized * 0.0f + f * 0.3f).normalized : palmN;
                    Vector3 axis = Vector3.Cross(dir, toward);
                    if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(dir, f);
                    axis.Normalize();
                    fingers[i].Add(new Bone { t = b, rest = b.localRotation, axis = Quaternion.Inverse(b.rotation) * axis, max = max[j] });
                }
            }
            PalmNormal = palmN;
            if (palm != null) palmLocalN = Quaternion.Inverse(palm.rotation) * palmN;
            if (indexTip != null)
            {
                var tipGo = indexTip.gameObject;
                var sc = tipGo.AddComponent<SphereCollider>(); sc.radius = 0.009f; sc.isTrigger = true;
                var rb = tipGo.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
                tipGo.AddComponent<Fingertip>().hand = hand;
            }
        }

        void Update()
        {
            float grip = XRIn.Grip(hand), trig = XRIn.Trigger(hand);
            bool trigTouch = XRIn.TriggerTouched(hand), thumb = XRIn.ThumbTouched(hand);
            var target = curlTarget;   // reused: no per-frame array allocation (two hands × 72 fps)
            target[0] = thumb ? 0.85f : 0.1f;
            target[1] = trig > 0.05f ? 0.3f + 0.7f * trig : (trigTouch ? 0.28f : 0.02f);
            target[2] = grip; target[3] = grip * 0.98f; target[4] = grip * 0.96f;
            for (int i = 0; i < 5; i++)
            {
                curl[i] = Mathf.Lerp(curl[i], target[i], 1f - Mathf.Exp(-18f * Time.deltaTime));
                foreach (var b in fingers[i])
                    b.t.localRotation = b.rest * Quaternion.AngleAxis(curl[i] * b.max, b.axis);
            }
            if (palm != null) PalmNormal = palm.rotation * palmLocalN;
        }

        void LateUpdate()
        {
            if (pokeAttach != null && indexTip != null) pokeAttach.position = indexTip.position;
        }

        /// <summary>Stylised neon hand built from tubes when the XR Hands sample mesh is not available.
        /// Uses the same joint names so the curl code is shared.</summary>
        Transform BuildFallback()
        {
            string p = hand == Hand.Right ? "R_" : "L_";
            float side = hand == Hand.Right ? 1f : -1f;
            var root = new GameObject("NeonHand").transform;
            root.SetParent(transform, false);
            var wristT = Joint(root, p + "Wrist", Vector3.zero);
            var palmT = Joint(wristT, p + "Palm", new Vector3(0, 0, 0.045f));
            var k = new MeshKit();
            k.Box(new Vector3(0, 0, 0.045f), new Vector3(0.075f, 0.022f, 0.085f), Palette.Dark, 0f);
            k.Box(new Vector3(0, 0.0115f, 0.045f), new Vector3(0.05f, 0.002f, 0.06f), Palette.Magenta, 1f);
            k.Spawn("PalmMesh", wristT, Palette.Neon, false);
            float[] xs = { -0.042f * side, -0.026f * side, -0.008f * side, 0.01f * side, 0.027f * side };
            float[] lens = { 0.032f, 0.038f, 0.042f, 0.039f, 0.031f };
            for (int i = 0; i < 5; i++)
            {
                Vector3 start = i == 0 ? new Vector3(xs[0], -0.004f, 0.015f) : new Vector3(xs[i], 0, 0.088f);
                Quaternion dirRot = i == 0 ? Quaternion.Euler(0, -45f * side, 0) : Quaternion.identity;
                string[] names = i == 0 ? new[] { "Metacarpal", "Proximal", "Distal", "Tip" } : new[] { "Proximal", "Intermediate", "Distal", "Tip" };
                Transform parent = wristT;
                Vector3 pos = start;
                for (int j = 0; j < names.Length; j++)
                {
                    var jt = Joint(parent, p + FingerNames[i] + names[j], parent == wristT ? pos : dirRot * Vector3.forward * lens[i] * (j == 1 ? 1f : 0.8f));
                    if (parent == wristT) jt.localRotation = dirRot;
                    if (j < names.Length - 1)
                    {
                        var seg = new MeshKit();
                        float len = lens[i] * (j == 0 ? 1f : 0.8f);
                        seg.Capsule(Vector3.zero, Vector3.forward * len, 0.0085f, 8, Palette.Dark, 0f);
                        seg.Torus(Vector3.forward * len * 0.5f, 0.0088f, 0.0015f, 10, 4, Quaternion.Euler(90, 0, 0), Palette.Magenta, 1f);
                        seg.Spawn("Seg", jt, Palette.Neon, false);
                    }
                    parent = jt;
                }
            }
            return root;
        }

        static Transform Joint(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }
    }
}
