using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Your arm in VR: a sleeve from the wrist to the elbow and an upper arm up to an estimated shoulder, solved
    /// every frame with a two-bone IK (elbow down and out, blended with the hand's own direction so the forearm always
    /// continues the wrist). The sleeve follows your avatar look (Neon Ronin armour, High Roller loud shirt with a gold
    /// bracelet, Sharp Suit with a white cuff and cufflink, Showtime velvet), trimmed in your NFT rarity colour.
    /// The right arm wears the ASCEND smartwatch: time left today, the quota and your chips at a glance (turn the wrist
    /// further and the full wrist menu opens above it). Only shown with a headset.</summary>
    public class PlayerArm : MonoBehaviour
    {
        public Hand hand = Hand.Right;
        HandRig rig;
        Transform forearm, upper, elbow, cuff, watch;
        Renderer[] rends;
        TextMeshPro watchBig, watchSmall;
        Transform watchBar;
        float nextWatch;
        int builtFor = -1;
        const float Fore = 0.26f, Upper = 0.29f;

        public void Init(HandRig r) { rig = r; hand = r.hand; }

        void Start()
        {
            if (rig == null) rig = GetComponent<HandRig>();
            Rebuild();
            if (GameState.Exists) GameState.I.Changed += OnChanged;
        }

        void OnDestroy() { if (GameState.Exists) GameState.I.Changed -= OnChanged; }

        void OnChanged()
        {
            int key = (int)AvatarStyles.Current * 16 + (GameState.Exists ? (int)GameState.I.nft : 0);
            if (key != builtFor) Rebuild();
        }

        // ------------------------------------------------------------------ look per style
        struct Look { public Color sleeve, sleeve2, trim, cuff; public bool shirtCuff, bracelet, armour, velvet; }

        static Look LookFor(AvatarStyle s, Color rarity)
        {
            switch (s)
            {
                case AvatarStyle.Android:   // HIGH ROLLER: loud shirt, gold chain bracelet
                    return new Look { sleeve = Palette.Hex("E8496A"), sleeve2 = Palette.Hex("F5C542"), trim = Palette.Gold, cuff = Palette.Hex("E8496A"), bracelet = true };
                case AvatarStyle.Vanguard:  // SHARP SUIT: black jacket, white shirt cuff, gold cufflink
                    return new Look { sleeve = Palette.Hex("17151D"), sleeve2 = Palette.Hex("22202A"), trim = Palette.Hex("3A3646"), cuff = Palette.Hex("F2F2F2"), shirtCuff = true };
                case AvatarStyle.Michelle:  // SHOWTIME: velvet with gold piping
                    return new Look { sleeve = Palette.Hex("5B2A86"), sleeve2 = Palette.Hex("6E35A0"), trim = Palette.Gold, cuff = Palette.Hex("F5C542"), velvet = true };
                default:                    // NEON RONIN: armour plates lit in the rarity colour
                    return new Look { sleeve = Palette.Hex("1C1830"), sleeve2 = Palette.Hex("2A2444"), trim = rarity, cuff = Palette.Hex("2A2444"), armour = true };
            }
        }

        void Rebuild()
        {
            foreach (var t in new[] { forearm, upper, elbow, cuff, watch }) if (t != null) Destroy(t.gameObject);
            var style = AvatarStyles.Current;
            var rar = GameState.Exists ? Palette.RarityColor(GameState.I.nft) : Palette.Magenta;
            builtFor = (int)style * 16 + (GameState.Exists ? (int)GameState.I.nft : 0);
            var L = LookFor(style, rar);
            float side = hand == Hand.Right ? 1f : -1f;

            // forearm: unit length along +Z (wrist at 0, elbow at 1), scaled in Z every frame (radii stay put)
            var fk = new MeshKit();
            fk.Cylinder(new Vector3(0, 0, 0.5f), 0.034f, 0.046f, 1f, 18, Quaternion.Euler(90, 0, 0), L.sleeve, 0f, false);
            if (L.armour)
            {
                for (int i = 0; i < 3; i++) fk.Cylinder(new Vector3(0, 0, 0.22f + i * 0.24f), 0.037f + i * 0.004f, 0.039f + i * 0.004f, 0.16f, 18, Quaternion.Euler(90, 0, 0), L.sleeve2, 0.02f, false);
                for (int i = 0; i < 3; i++) fk.Torus(new Vector3(0, 0, 0.14f + i * 0.24f), 0.038f + i * 0.004f, 0.0025f, 18, 4, Quaternion.Euler(90, 0, 0), L.trim, 1.2f);
                fk.Box(new Vector3(0, 0.04f, 0.5f), new Vector3(0.012f, 0.006f, 0.6f), L.trim, 1.2f);   // light strip on top
            }
            else if (L.velvet)
            {
                for (int i = 0; i < 2; i++) fk.Torus(new Vector3(0, 0, 0.3f + i * 0.35f), 0.04f + i * 0.004f, 0.003f, 18, 4, Quaternion.Euler(90, 0, 0), L.trim, 0.5f);
            }
            else if (L.bracelet)
            {
                // loud shirt: big flowers along the sleeve
                for (int i = 0; i < 7; i++)
                {
                    float z = 0.15f + i * 0.12f, a = i * 67f * Mathf.Deg2Rad;
                    float r = 0.035f + z * 0.012f;
                    fk.Sphere(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z), new Vector3(0.012f, 0.012f, 0.03f), 6, i % 2 == 0 ? L.sleeve2 : Palette.Hex("4EC9E8"), 0.15f);
                }
            }
            else
            {
                fk.Box(new Vector3(0, 0.0f, 0.5f), new Vector3(0.002f, 0.093f, 0.9f), L.sleeve2, 0f);   // seam
            }
            forearm = Spawn(fk, "Forearm");

            // elbow joint
            var ek = new MeshKit();
            ek.Sphere(Vector3.zero, 0.05f, 14, L.armour ? L.sleeve2 : L.sleeve, 0f);
            if (L.armour) ek.Torus(Vector3.zero, 0.051f, 0.003f, 18, 4, Quaternion.Euler(90, 0, 0), L.trim, 1.2f);
            elbow = Spawn(ek, "Elbow");

            // upper arm (mostly out of view: just a shoulder of sleeve when you look down)
            var uk = new MeshKit();
            uk.Cylinder(new Vector3(0, 0, 0.5f), 0.05f, 0.058f, 1f, 16, Quaternion.Euler(90, 0, 0), L.sleeve, 0f, false);
            if (L.armour) uk.Torus(new Vector3(0, 0, 0.35f), 0.054f, 0.0025f, 16, 4, Quaternion.Euler(90, 0, 0), L.trim, 1.2f);
            upper = Spawn(uk, "UpperArm");

            // cuff at the wrist (not scaled)
            var ck = new MeshKit();
            if (L.shirtCuff)
            {
                ck.Cylinder(new Vector3(0, 0, 0.005f), 0.036f, 0.037f, 0.03f, 18, Quaternion.Euler(90, 0, 0), L.cuff, 0.05f);
                ck.Cylinder(new Vector3(0, 0, 0.035f), 0.038f, 0.039f, 0.03f, 18, Quaternion.Euler(90, 0, 0), L.sleeve, 0f);
                ck.Sphere(new Vector3(-side * 0.036f, 0.0f, 0.008f), 0.007f, 8, Palette.Gold, 0.6f);   // cufflink
            }
            else
            {
                ck.Cylinder(new Vector3(0, 0, 0.012f), 0.037f, 0.038f, 0.026f, 18, Quaternion.Euler(90, 0, 0), L.cuff, L.armour ? 0.05f : 0f);
                ck.Torus(new Vector3(0, 0, -0.001f), 0.037f, 0.0025f, 18, 4, Quaternion.Euler(90, 0, 0), L.trim, L.armour ? 1.4f : 0.6f);
            }
            if (L.bracelet && hand == Hand.Left)
                for (int i = 0; i < 14; i++)
                {
                    float a = i / 14f * Mathf.PI * 2f;
                    ck.Sphere(new Vector3(Mathf.Cos(a) * 0.036f, Mathf.Sin(a) * 0.036f, -0.012f), 0.0055f, 6, Palette.Gold, 0.7f);
                }
            cuff = Spawn(ck, "Cuff");

            // the ASCEND smartwatch on the right wrist, on top of the forearm
            if (hand == Hand.Right)
            {
                watch = new GameObject("Watch").transform; watch.SetParent(transform, false);
                var wk = new MeshKit();
                wk.Box(new Vector3(0, 0.041f, 0.03f), new Vector3(0.036f, 0.008f, 0.042f), Palette.Hex("15121E"), 0f);
                wk.Box(new Vector3(0, 0.0455f, 0.03f), new Vector3(0.031f, 0.001f, 0.037f), Palette.Hex("070510"), 0.1f);
                wk.Torus(new Vector3(0f, 0f, 0.03f), 0.04f, 0.004f, 20, 4, Quaternion.Euler(90, 0, 0), Palette.Hex("22202A"), 0f);   // strap
                wk.Box(new Vector3(0.019f, 0.042f, 0.03f), new Vector3(0.004f, 0.005f, 0.008f), Palette.Gold, 0.6f);   // crown
                wk.Spawn("WatchBody", watch, Palette.Glossy, false);
                // screen texts lie on the face, readable when you look down at your wrist
                watchBig = Txt.Make("Time", watch, new Vector3(0, 0.0468f, 0.034f), "", 0.011f, Palette.Text, Palette.Display, TextAlignmentOptions.Center, 0.03f);
                watchBig.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
                watchSmall = Txt.Make("Info", watch, new Vector3(0, 0.0468f, 0.022f), "", 0.0045f, Palette.Gold, Palette.Display, TextAlignmentOptions.Center, 0.03f);
                watchSmall.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
                var bk = new MeshKit(); bk.Box(new Vector3(0.5f, 0, 0), new Vector3(1f, 0.0006f, 0.0025f), Palette.Gold, 1.2f);
                watchBar = global::Ascend.Build.Empty("Bar", watch, new Vector3(-0.012f, 0.0469f, 0.044f)).transform;
                bk.Spawn("Fill", watchBar, Palette.Neon, false);
            }

            rends = GetComponentsInChildren<Renderer>(true);
            // only the arm meshes, never the hand (HandRig keeps its own renderers)
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var t in new[] { forearm, upper, elbow, cuff, watch })
                if (t != null) list.AddRange(t.GetComponentsInChildren<Renderer>(true));
            rends = list.ToArray();
            foreach (var r in rends) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
            visible = true;   // fresh renderers start enabled; LateUpdate hides them again if needed
        }

        Transform Spawn(MeshKit k, string name)
        {
            // parented to the rig root (not the controller) so the IK places it in world space
            var parent = Rig.I != null ? Rig.I.transform : transform;
            return k.Spawn(name, parent, Vector3.zero, Quaternion.identity, Palette.Glossy, false).transform;
        }

        void LateUpdate()
        {
            var R = Rig.I;
            bool show = R != null && R.Head != null && XRIn.HasHeadset && rig != null && !IntroCinematic.Playing;
            SetVisible(show);
            if (!show) return;

            var head = R.Head;
            float side = hand == Hand.Right ? 1f : -1f;
            Vector3 f = head.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Vector3 S = head.position + Vector3.down * 0.24f + right * side * 0.19f - f * 0.07f;   // shoulder estimate

            Transform wristT = rig.Wrist;
            Vector3 handFwd = rig.HandForward;
            Vector3 handUp = rig.HandUp;
            Vector3 W = wristT.position - handFwd * 0.012f;

            // two-bone IK, elbow pulled down and out
            Vector3 toW = W - S; float d = toW.magnitude;
            Vector3 dir = d > 1e-4f ? toW / d : Vector3.down;
            float dc = Mathf.Clamp(d, 0.08f, Upper + Fore - 0.002f);
            float cosA = (Upper * Upper + dc * dc - Fore * Fore) / (2f * Upper * dc);
            float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
            Vector3 pole = (Vector3.down * 1f + right * side * 0.55f - f * 0.25f);
            Vector3 perp = pole - Vector3.Dot(pole, dir) * dir;
            if (perp.sqrMagnitude < 1e-5f) perp = Vector3.Cross(dir, right); perp.Normalize();
            Vector3 Eik = S + dir * Upper * cosA + perp * Upper * sinA;
            // the forearm must continue the wrist: blend the IK direction with straight back along the hand
            Vector3 dIk = (Eik - W).normalized;
            Vector3 dHand = -handFwd;
            Vector3 fdir = Vector3.Slerp(dIk, dHand, 0.55f).normalized;
            Vector3 E = W + fdir * Fore;

            // forearm: from the wrist (z = 0) to the elbow (z = 1); roll follows the back of the hand
            Vector3 up = handUp - Vector3.Dot(handUp, fdir) * fdir;
            if (up.sqrMagnitude < 1e-5f) up = Vector3.up; up.Normalize();
            var frot = Quaternion.LookRotation(fdir, up);
            forearm.SetPositionAndRotation(W, frot);
            forearm.localScale = new Vector3(1f, 1f, Fore);
            cuff.SetPositionAndRotation(W, frot);
            elbow.position = E;
            var ud = S - E; float ul = ud.magnitude;
            if (ul > 1e-4f) { upper.SetPositionAndRotation(E, Quaternion.LookRotation(ud / ul, up)); upper.localScale = new Vector3(1f, 1f, ul); }

            if (watch != null)
            {
                watch.SetPositionAndRotation(W, frot);
                if (Time.time >= nextWatch) { nextWatch = Time.time + 0.5f; UpdateWatch(); }
            }
        }

        bool visible = true;
        void SetVisible(bool v)
        {
            if (v == visible || rends == null) return;
            visible = v;
            foreach (var r in rends) if (r != null) r.enabled = v;
            if (watchBig != null) watchBig.enabled = v;
            if (watchSmall != null) watchSmall.enabled = v;
        }

        void UpdateWatch()
        {
            var gs = GameState.Exists ? GameState.I : null;
            float t = 1f; Color c = Palette.Teal;
            if (Run.Active && RunDirector.I != null)
            {
                bool running = RunDirector.I.DayRunning;
                float left = RunDirector.I.TimeLeft;
                watchBig.text = running ? Run.Clock(left) : System.DateTime.Now.ToString("HH:mm");
                watchBig.color = running && left < 30f ? Palette.Loss : Palette.Text;
                long q = Run.Quota; long chips = gs != null ? gs.chips : 0;
                watchSmall.text = Run.FinalDay ? "DÍA " + Run.Day + " · RAID" : "DÍA " + Run.Day + " · " + Run.N(chips) + "/" + Run.N(q);
                t = q > 0 ? Mathf.Clamp01(chips / (float)q) : 1f;
                c = t >= 1f ? Palette.Money : Palette.Gold;
            }
            else
            {
                watchBig.text = System.DateTime.Now.ToString("HH:mm");
                watchBig.color = Palette.Text;
                watchSmall.text = gs != null ? gs.chips.ToString("N0") + " · " + GameState.TierNames[(int)gs.CurrentTier] : "ASCEND";
                t = gs != null ? gs.TierProgress : 0f;
                c = gs != null ? Palette.TierColor(gs.CurrentTier) : Palette.Teal;
            }
            watchBar.localScale = new Vector3(Mathf.Max(0.001f, t) * 0.024f, 1f, 1f);
            watchSmall.color = c;
        }
    }
}
