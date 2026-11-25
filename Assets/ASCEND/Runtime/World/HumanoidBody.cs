using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>An imported, rigged character driven by Mecanim. The shared controller (built by the world builder from
    /// the imported clips) has one state per logical action: Idle, Walk, Run, Sit, Dance, Cheer, Nod, Facepalm, Deal,
    /// Shoot, Aim, Guard, Hit, Death. Hand gestures (wave, clap, point, reach for chips, DJ decks, cocktail shaker) and
    /// head look-at are layered on top with humanoid IK, so every dealer really reaches the shoe, wheel and chips.</summary>
    public class HumanoidBody : AvatarBody
    {
        public enum Kind { Android, GuestF, Security, Robot, Extra }

        public Kind kind;
        public AvatarStyle style = AvatarStyle.Android;
        /// <summary>Carries a tray: walks and waits with the pack's waiter clips.</summary>
        public bool waiter;
        /// <summary>Standing idles this body drifts between (animator states); null = the plain idle only.</summary>
        public string[] idleSet;
        /// <summary>Animator state used while seated instead of "Sit" (the pack's other chair poses).</summary>
        public string sitState;
        /// <summary>False = the seated clip keeps its own arms (no hands pinned to the thighs / rail).</summary>
        public bool restHands = true;
        /// <summary>World height the hands never go below in front of the body (a dealer's table top); &lt; 0 = off.</summary>
        public float handFloor = -1f;
        float idleNext; int idleIdx; string idleGesture;
        Animator anim;
        Transform model, head, handR, handL, hips;
        Vector3 lastPos;
        float speed, seed, lookW, emoteT, emoteDur;
        Emote emote;
        string current = "";
        Vector3 rT, lT; float rW, lW;
        Vector3 lookPoint;
        bool human;
        Vector3 modelBase;

        public override Transform HeadBone { get { return head != null ? head : transform; } }
        public override Transform RightHand { get { return handR != null ? handR : transform; } }
        public override Transform LeftHand { get { return handL != null ? handL : transform; } }
        public Animator Animator { get { return anim; } }
        public string DebugState
        {
            get
            {
                var ci = anim != null ? anim.GetCurrentAnimatorClipInfo(0) : null;
                return "state=" + current + " speed=" + speed.ToString("0.000") + " pose=" + pose + " clip=" + (ci != null && ci.Length > 0 && ci[0].clip != null ? ci[0].clip.name : "?") + " animSpeed=" + (anim != null ? anim.speed.ToString("0.00") : "?") + " pos=" + transform.position.ToString("0.0000");
            }
        }

        static readonly Dictionary<string, Kind> nameKind = new Dictionary<string, Kind>();

        // ------------------------------------------------------------------ factory
        public static HumanoidBody Create(AssetLibrary lib, string name, Transform parent, Vector3 localPos, float yaw, Rarity rarity, RoninRole role, float height, AvatarStyle? style = null)
        {
            // Casino FREE people: 200 (gentleman), 209 (high roller), 3_Actor (showgirl), 31_Cashier (croupier), 77_Security
            GameObject prefab = null; Kind k = Kind.Extra;
            bool gear = false, shades = true;
            if (role == RoninRole.Player)
            {
                var st = style ?? AvatarStyles.ForGuest(name);
                switch (st)
                {
                    case AvatarStyle.NeonRonin: prefab = lib.android; gear = style.HasValue || !AvatarStyles.PlainGuest(name); break;
                    case AvatarStyle.Android: prefab = lib.highRoller; break;
                    case AvatarStyle.Vanguard: prefab = lib.security; k = Kind.Security; break;
                    case AvatarStyle.Michelle: prefab = lib.showgirl; break;
                }
                if (prefab != null)
                {
                    var hb = CreateFrom(prefab, lib.humanoidController, lib, name, parent, localPos, yaw, rarity, role, height, k);
                    if (hb != null && gear) RoninGear.Attach(hb.transform, hb.anim, rarity == Rarity.None ? Rarity.Common : rarity);
                    if (hb != null) hb.style = st;
                    return hb;
                }
            }
            int h = Mathf.Abs(name.GetHashCode());
            switch (role)
            {
                case RoninRole.Security: prefab = lib.security; k = Kind.Security; break;
                case RoninRole.Bartender:
                case RoninRole.Host: prefab = lib.guestF; k = Kind.GuestF; break;
                case RoninRole.Dealer:
                    // croupiers: the cashier in waistcoat and bow tie, or the suit without the shades
                    if (h % 2 == 0 || lib.security == null) { prefab = lib.guestF; k = Kind.GuestF; }
                    else { prefab = lib.security; k = Kind.Extra; shades = false; }
                    break;
                case RoninRole.DJ: prefab = lib.showgirl != null ? lib.showgirl : lib.android; break;
                default:
                    if (lib.extraHumans.Count > 0) prefab = lib.extraHumans[h % lib.extraHumans.Count];
                    break;
            }
            if (prefab == null) prefab = lib.android != null ? lib.android : lib.guestF != null ? lib.guestF : lib.security;
            if (prefab == null) return null;
            var body = CreateFrom(prefab, lib.humanoidController, lib, name, parent, localPos, yaw, rarity, role, height, k);
            if (body != null && !shades)
                foreach (var r in body.GetComponentsInChildren<Renderer>(true)) if (r.name.Contains("Glasses")) r.enabled = false;
            return body;
        }

        public static HumanoidBody CreateFrom(GameObject prefab, RuntimeAnimatorController ctrl, AssetLibrary lib, string name, Transform parent, Vector3 localPos, float yaw,
                                              Rarity rarity, RoninRole role, float height, Kind kind)
        {
            if (prefab == null || ctrl == null) return null;
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = localPos;
            root.localRotation = Quaternion.Euler(0, yaw, 0);
            var model = Instantiate(prefab, root, false).transform;
            model.name = "Model";
            model.localPosition = Vector3.zero; model.localRotation = Quaternion.identity;
            // measure the bind pose and scale to the requested height
            var rends = model.GetComponentsInChildren<Renderer>(true);
            float top = 0f;
            foreach (var r in rends) { var b = r.bounds; top = Mathf.Max(top, b.max.y - root.position.y); }
            if (top < 0.2f) top = 1.75f;
            model.localScale = model.localScale * (height / top);

            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.gameObject.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            anim.updateMode = AnimatorUpdateMode.Normal;

            var body = root.gameObject.AddComponent<HumanoidBody>();
            body.kind = kind; body.rarity = rarity; body.role = role;
            body.anim = anim; body.model = model; body.modelBase = model.localPosition;
            body.human = anim.isHuman && anim.avatar != null && anim.avatar.isValid;
            if (body.human)
            {
                body.head = anim.GetBoneTransform(HumanBodyBones.Head);
                body.handR = anim.GetBoneTransform(HumanBodyBones.RightHand);
                body.handL = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                body.hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            }
            else
            {
                body.head = Rig.FindDeep(model, "Head");
                body.handR = Rig.FindDeep(model, "Hand.R") ?? Rig.FindDeep(model, "HandR");
                body.handL = Rig.FindDeep(model, "Hand.L") ?? Rig.FindDeep(model, "HandL");
                body.hips = Rig.FindDeep(model, "Hips");
            }
            model.gameObject.AddComponent<IKRelay>().body = body;
            body.Measure(height);
            body.Skin(lib, rends);
            if (kind != Kind.Robot)
            {
                // everybody shifts between a couple of idles at their own pace
                // (entries that are not animator states are IK gestures over the idle: see DoIK)
                body.idleSet = role == RoninRole.Dealer || role == RoninRole.Host || role == RoninRole.Bartender ? new[] { "Idle", "IdleLook" }
                             : role == RoninRole.Security ? new[] { "Idle", "IdleBack", "IdleLook", "IdleEar", "IdleBack", "IdleArms" }
                             : new[] { "Idle", "IdleLook", "IdleThink", "Idle", "IdleHips", "IdleArms", "IdlePhone" };
                body.idleIdx = Mathf.Abs(name.GetHashCode()) % 7; body.idleNext = Time.time + Random.Range(3f, 9f);
            }
            foreach (var r in rends)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) { smr.updateWhenOffscreen = false; smr.quality = SkinQuality.Bone2; }
            }
            var col = root.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, height * 0.5f, 0); col.height = height; col.radius = 0.26f;
            return body;
        }

        void Skin(AssetLibrary lib, Renderer[] rends)
        {
            int ri = Mathf.Clamp((int)rarity, 1, 5);
            Color accent = Palette.RarityColor(rarity == Rarity.None ? Rarity.Rare : rarity);
            if (role == RoninRole.Security) accent = Palette.Teal;
            if (role == RoninRole.Dealer && kind == Kind.Android) accent = Palette.RarityColor(rarity == Rarity.None ? Rarity.Legendary : rarity);
            foreach (var r in rends)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string mn = (mats[i] != null ? mats[i].name : "") + "|" + r.name;
                    switch (kind)
                    {
                        case Kind.Android:
                            bool joints = mn.Contains("Joint");
                            mats[i] = LibMat(lib.androidSkins, ri * 2 + (joints ? 1 : 0)) ?? Palette.Character(joints ? "android_j" + ri : "android_s" + ri, null, joints ? accent : Palette.Hex("16131F"), joints ? 0.85f : 0f, joints ? 0.4f : 0.8f, joints ? 0f : 0.45f);
                            break;
                        case Kind.GuestF:
                            mats[i] = lib.guestSkin != null ? lib.guestSkin : mats[i];
                            break;
                        case Kind.Security:
                            bool visor = mn.ToLowerInvariant().Contains("visor");
                            mats[i] = visor ? (lib.securityVisor != null ? lib.securityVisor : mats[i]) : (lib.securitySkin != null ? lib.securitySkin : mats[i]);
                            break;
                        case Kind.Robot:
                            if (mn.Contains("Main")) mats[i] = Palette.Character("robot_main" + ri, null, accent, 0.15f, 0.75f, 0.35f);
                            else if (mn.Contains("Grey")) mats[i] = Palette.Character("robot_grey", null, Palette.Hex("C9CCD8"), 0f, 0.7f, 0.2f);
                            else mats[i] = Palette.Character("robot_black", null, Palette.Hex("15121C"), 0f, 0.8f, 0.3f);
                            break;
                    }
                }
                r.sharedMaterials = mats;
            }
            if (kind == Kind.Extra && role != RoninRole.Dealer) Vary(Mathf.Abs(name.GetHashCode()), role == RoninRole.Player ? 0.5f : 0.3f);
        }

        /// <summary>Makes this body its own person: each garment gets its own hue (skin, face and hair keep the pack's
        /// palette), glasses on or off, and a slightly different build.</summary>
        public void Vary(int seed, float strength)
        {
            var rnd = new System.Random(seed);
            float hue = Mathf.Repeat(seed * 0.618034f, 1f);
            float sat = 0.5f + (float)rnd.NextDouble() * 0.35f;
            bool glasses = rnd.Next(3) != 0;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                if (n.Contains("Glasses") && role != RoninRole.Security) r.enabled = glasses;
                if (!Clothes(n)) continue;
                bool legs = n.Contains("Pants") || n.Contains("Shorts") || n.Contains("Skirt");
                float h = Mathf.Repeat(hue + (legs ? 0.42f : n.Contains("Hat") ? 0.06f : n.Contains("Shirt") ? 0.2f : 0f), 1f);
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", Color.Lerp(Color.white, Color.HSVToRGB(h, sat, legs ? 0.8f : 1f), strength));
                r.SetPropertyBlock(mpb);
            }
            if (model != null && !varied)
            {
                varied = true;
                float wide = 0.94f + (float)rnd.NextDouble() * 0.14f;
                model.localScale = Vector3.Scale(model.localScale, new Vector3(wide, 1f, wide));
            }
        }
        bool varied;

        static bool Clothes(string n)
        {
            foreach (var k in new[] { "Jacket", "Shirt", "Pants", "Shorts", "Skirt", "Outwear", "Hat_1", "Hat_0", "Actor_Hat" }) if (n.Contains(k)) return true;
            return false;
        }

        static Material LibMat(List<Material> list, int i) { return list != null && i >= 0 && i < list.Count ? list[i] : null; }

        // ------------------------------------------------------------------ API
        Transform upLegL, upLegR;
        BodyDims dims;
        bool dimsSet;

        public override BodyDims Dims { get { return dimsSet ? dims : BodyDims.Default(1.75f); } }

        void Awake() { seed = Random.value * 10f; lastPos = transform.position; born = Time.time; }

        /// <summary>Leg and arm lengths from the rig's own bones (any pose: bone lengths do not change).</summary>
        void Measure(float height)
        {
            dims = BodyDims.Default(height);
            if (!human || anim == null) return;
            upLegL = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); upLegR = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var lo = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg); var ft = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            var ua = anim.GetBoneTransform(HumanBodyBones.RightUpperArm); var la = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if (upLegL == null || upLegR == null || lo == null || ft == null) return;
            dims.thigh = Vector3.Distance(upLegL.position, lo.position);
            dims.shin = Vector3.Distance(lo.position, ft.position);
            dims.ankle = Mathf.Clamp(ft.position.y - transform.position.y, 0.05f * height / 1.75f, 0.13f * height / 1.75f);
            dims.hipW = Mathf.Clamp(Vector3.Distance(upLegL.position, upLegR.position) * 0.5f, 0.06f, 0.14f);
            if (ua != null && la != null && handR != null) dims.arm = Vector3.Distance(ua.position, la.position) + Vector3.Distance(la.position, handR.position);
            dimsSet = dims.thigh > 0.2f && dims.shin > 0.2f;
            if (!dimsSet) dims = BodyDims.Default(height);
        }

        public override void SitOn(Seat s)
        {
            base.SitOn(s);
            if (s != null && sitW >= 1f)
            {
                string st = sitState != null && Has(sitState) ? sitState : "Sit";
                if (anim != null && Has(st)) { anim.Play(st, 0, Mathf.Repeat(seed, 1f)); current = st; }
            }
            if (s != null) SkirtToThighs();
        }

        bool skirtRebound;
        /// <summary>The pack's skirts are skinned to the pelvis only, so on a chair they hang straight down through the
        /// seat. Seated, each skirt vertex is re-weighted onto the thigh on its side (more the lower it hangs), so the hem
        /// rides on the lap the way cloth does.</summary>
        void SkirtToThighs()
        {
            if (skirtRebound || anim == null) return;
            var lt = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var rt = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var hp = anim.GetBoneTransform(HumanBodyBones.Hips);
            if (lt == null || rt == null || hp == null) return;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!r.name.Contains("Skirt") || r.sharedMesh == null || !r.sharedMesh.isReadable) continue;
                var bones = r.bones; int li = -1, ri = -1, hi = -1;
                for (int i = 0; i < bones.Length; i++) { if (bones[i] == lt) li = i; else if (bones[i] == rt) ri = i; else if (bones[i] == hp) hi = i; }
                var src = r.sharedMesh; var bp = src.bindposes;
                if (li < 0 || ri < 0 || li >= bp.Length || ri >= bp.Length) continue;
                var m = Instantiate(src); m.name = src.name + " (seated)";
                Vector3 pl = bp[li].inverse.MultiplyPoint3x4(Vector3.zero), pr = bp[ri].inverse.MultiplyPoint3x4(Vector3.zero);
                // mesh-space axes: up from the hip joints towards the pelvis bone (or mesh up), side from right to left hip
                Vector3 mid = (pl + pr) * 0.5f, side = (pl - pr).normalized;
                Vector3 up = Vector3.up;
                if (hi >= 0 && hi < bp.Length) { var ph = bp[hi].inverse.MultiplyPoint3x4(Vector3.zero); var u = ph - mid; u -= side * Vector3.Dot(u, side); if (u.sqrMagnitude > 1e-6f) up = u.normalized; }
                var vs = m.vertices; float lo = 0f;
                for (int i = 0; i < vs.Length; i++) lo = Mathf.Min(lo, Vector3.Dot(vs[i] - mid, up));
                if (lo > -0.02f) lo = -0.3f;
                int root = hi >= 0 ? hi : 0;
                var bw = m.boneWeights; if (bw == null || bw.Length != vs.Length) continue;
                float halfW = Mathf.Max(0.02f, (pl - pr).magnitude * 0.5f);
                for (int i = 0; i < vs.Length; i++)
                {
                    var d = vs[i] - mid; float h = Vector3.Dot(d, up);
                    float t = Mathf.Clamp01((0.03f - h) / (0.03f - lo));                  // 0 at the waist, 1 at the hem
                    float leg = Mathf.SmoothStep(0f, 0.97f, t);
                    float x = Mathf.Clamp(Vector3.Dot(d, side) / halfW, -1f, 1f);          // +1 left hip, -1 right hip
                    float wl = leg * Mathf.Clamp01(0.5f + x * 0.9f), wr = leg - wl;
                    var b = new BoneWeight { boneIndex0 = root, weight0 = 1f - leg, boneIndex1 = li, weight1 = wl, boneIndex2 = ri, weight2 = wr, boneIndex3 = root, weight3 = 0f };
                    bw[i] = b;
                }
                m.boneWeights = bw;
                r.sharedMesh = m;
                r.updateWhenOffscreen = true;
            }
            skirtRebound = true;
        }

        public override void Play(Emote e, float dur = 1.6f) { NoteEmote(e); emote = e; emoteT = 0f; emoteDur = dur; }

        public override void Reach(bool right, Vector3 world, float weight)
        {
            if (!AcceptReach(weight)) return;
            if (right) { rT = world; rW = weight; } else { lT = world; lW = weight; }
        }

        /// <summary>Animator states of the dance styles (0 = samba; the rest are authored for ASCEND: griddy, floss, hype, running man, robot).</summary>
        public static readonly string[] DanceStates = { "Dance", "DanceGriddy", "DanceFloss", "DanceHype", "DanceRunningMan", "DanceRobot" };

        static string PoseState(Pose p)
        {
            switch (p)
            {
                case Pose.Dance: return "Dance";
                case Pose.Shoot: return "Shoot";
                case Pose.Guard: return "Guard";
                case Pose.Sit: return "Sit";
                case Pose.Deal: return "Deal";
                default: return "Idle";
            }
        }

        string EmoteState(Emote e)
        {
            switch (e)
            {
                // (a state exists only when there is a real clip for it; otherwise the gesture is done with IK over the idle)
                case Emote.Cheer: return null;   // the pack ships no cheer clip: arms-up is done by IK
                case Emote.Nod: return Has("Nod") ? "Nod" : null;
                case Emote.Facepalm: return Has("Facepalm") ? "Facepalm" : null;
                case Emote.Clap: return Has("Clap") ? "Clap" : human ? null : "Wave";
                // the pack's own wave and talking clips (the IK versions stretched big bodies' arms out like a zombie)
                case Emote.Wave: return Has("Wave") ? "Wave" : null;
                case Emote.Talk: return human && Has("Talk") ? "Talk" : null;
                case Emote.Point: return human ? null : "Wave";   // pointing at something needs IK
                default: return null;
            }
        }

        static readonly Dictionary<string, int> hashes = new Dictionary<string, int>();
        bool Has(string st)
        {
            int h;
            if (!hashes.TryGetValue(st, out h)) { h = Animator.StringToHash(st); hashes[st] = h; }
            return anim.HasState(0, h);
        }

        void Update()
        {
            if (anim == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var v = (transform.position - lastPos) / dt; v.y = 0; lastPos = transform.position;
            speed = Mathf.Lerp(speed, v.magnitude, 1f - Mathf.Exp(-8f * dt));
            if (emote != Emote.None) { emoteT += dt; if (emoteT >= emoteDur) emote = Emote.None; }

            // sitting is decided by the seat, not by the root sliding while a guest stands up and steps back
            bool sitting = pose == Pose.Sit || sitW > 0.35f;
            string want;
            string es = emote != Emote.None && !sitting ? EmoteState(emote) : null;
            if (sitting)
            {
                // seated reactions from the pack (fist pump / head in hands) once fully down on the seat
                want = "Sit";
                if (sitW > 0.9f && emote == Emote.Cheer && Has("SitWin")) want = "SitWin";
                else if (sitW > 0.9f && emote == Emote.Facepalm && Has("SitLose")) want = "SitLose";
            }
            else if (speed > 2.4f) want = "Run";
            else if (speed > 0.18f || pose == Pose.Walk) want = waiter && Has("Tray") ? "Tray" : "Walk";
            else if (waiter && es == null && Has("TrayIdle")) want = "TrayIdle";
            else if (es != null) want = es;
            else want = PoseState(pose);
            if (want == "Idle" && idleSet != null && idleSet.Length > 0)
            {
                if (Time.time >= idleNext) { idleIdx += 1 + Random.Range(0, idleSet.Length - 1); idleNext = Time.time + Random.Range(5f, 11f); }
                var alt = idleSet[idleIdx % idleSet.Length];
                if (Has(alt)) { want = alt; idleGesture = null; }
                else idleGesture = alt == "Idle" ? null : alt;
            }
            else if (want != "Idle" && want != "IdleLook") idleGesture = null;
            if (want == "Sit" && sitState != null && Has(sitState)) want = sitState;
            if (want == "Dance" && danceStyle > 0)
            {
                var st = DanceStates[Mathf.Clamp(danceStyle, 0, DanceStates.Length - 1)];
                if (Has(st)) want = st;
            }
            if (want != current)
            {
                // never cross-fade into a state the controller does not have (that leaves the rig in its bind T-pose)
                if (!Has(want)) want = Has("Idle") ? "Idle" : current;
                if (want != current)
                {
                    float offset = want.StartsWith("Idle") || want.StartsWith("Sit") || want.StartsWith("Dance") ? Mathf.Repeat(seed, 1f) : 0f;
                    bool loco = want == "Walk" || want == "Run" || want == "Tray" || current == "Walk" || current == "Run" || current == "Tray";
                    anim.CrossFadeInFixedTime(want, loco ? 0.25f : want.StartsWith("Sit") && current.StartsWith("Sit") ? 0.35f : want.StartsWith("Sit") || current.StartsWith("Sit") ? 0.6f : want.StartsWith("Idle") && current.StartsWith("Idle") ? 0.9f : 0.45f, 0, offset * 0.5f);
                    current = want;
                }
            }
            anim.speed = want == "Walk" || want == "Tray" ? Mathf.Clamp(speed / 1.2f, 0.7f, 1.4f) : want == "Dance" ? bpm / 150f : want.StartsWith("Dance") ? bpm / 120f : 1f;
        }

        void LateUpdate()
        {
            if (model == null) return;
            float target = pose == Pose.Sit && seat != null && sit.valid ? 1f : 0f;
            sitW = Mathf.MoveTowards(sitW, target, Time.deltaTime / 0.65f);
            float e = Mathf.SmoothStep(0f, 1f, sitW);
            // sitting: slide the body so the hip joints land exactly where the seat plan puts them, whatever the clip's
            // and the rig's proportions are (the offset is recomputed from the un-offset pose every frame: no drift)
            if (e > 0f && sit.valid && upLegL != null && upLegR != null)
            {
                var mid = (upLegL.position + upLegR.position) * 0.5f;
                var cur = transform.InverseTransformPoint(mid) - (model.localPosition - modelBase);
                model.localPosition = modelBase + (sit.hips - cur) * e;
            }
            else if (e > 0f && sit.valid && hips != null)
            {
                var cur = transform.InverseTransformPoint(hips.position) - (model.localPosition - modelBase);
                model.localPosition = modelBase + (sit.hips + Vector3.up * 0.06f - cur) * e;
            }
            else model.localPosition = Vector3.Lerp(model.localPosition, modelBase, 1f - Mathf.Exp(-10f * Time.deltaTime));
        }

        /// <summary>Called from the Animator's GameObject (IKRelay) during the IK pass.</summary>
        public void DoIK(int layer)
        {
            if (!human || anim == null) return;
            float dt = Time.deltaTime, t = Time.time + seed;
            float e = Mathf.SmoothStep(0f, 1f, sitW);
            bool seated = e > 0.5f;
            var fwd = transform.forward; var right = transform.right;
            float k = emote != Emote.None ? Mathf.Sin(Mathf.Clamp01(emoteT / Mathf.Max(emoteDur, 0.01f)) * Mathf.PI) : 0f;
            k = Mathf.Clamp01(k * 2.2f);

            // ---- torso: seated lean (in over the table / back into the sofa) + slow weight shifts, about the pelvis
            if (e > 0.001f && sit.valid)
            {
                float reachLean = Mathf.Max(srW, slW) * 6f;
                float lean = (sit.lean + reachLean + Mathf.Sin(t * 0.23f) * 1.6f) * e;
                float roll = Mathf.Sin(t * 0.17f + 1.3f) * 1.8f * e;
                float twist = Mathf.Sin(t * 0.11f) * 3f * e;
                anim.bodyRotation = Quaternion.AngleAxis(twist, Vector3.up) * Quaternion.AngleAxis(lean, right) * Quaternion.AngleAxis(roll, fwd) * anim.bodyRotation;
            }

            // ---- head: look-at, with seated head shakes / nods done here (the emote clips would stand the body up)
            Vector3? lt = lookAtTarget != null ? lookAtTarget.position : lookAt;
            var headPos = head != null ? head.position : transform.position + Vector3.up * 1.6f;
            if (k > 0f && ((emote == Emote.Facepalm && (seated || !Has("Facepalm"))) || (emote == Emote.Nod && (seated || !Has("Nod")))))
            {
                var ahead = headPos + fwd * 1.5f + Vector3.down * 0.35f;
                lt = emote == Emote.Facepalm ? ahead + right * Mathf.Sin(t * 9f) * 0.45f * k : ahead + Vector3.up * Mathf.Sin(t * 11f) * 0.3f * k;
            }
            if (!lt.HasValue && !seated && emote == Emote.None && idleGesture == "IdlePhone") lt = headPos + fwd * 0.35f + Vector3.down * 0.45f;
            bool wantLook = lt.HasValue && pose != Pose.Dance;
            lookW = Mathf.MoveTowards(lookW, wantLook ? 1f : 0f, dt * 2f);
            if (lt.HasValue) lookPoint = Vector3.Lerp(lookPoint == Vector3.zero ? lt.Value : lookPoint, lt.Value, 1f - Mathf.Exp(-6f * dt));
            if (lookW > 0.001f)
            {
                anim.SetLookAtWeight(lookW * 0.85f, seated ? 0.08f : 0.15f, 0.75f, 1f, 0.6f);
                anim.SetLookAtPosition(lookPoint);
            }

            // ---- legs: feet on the foot ring / floor, knees forward (or angled aside when the table leaves no room)
            if (e > 0.001f && sit.valid)
            {
                var legFwd = transform.rotation * sit.legDir;
                bool ring = seat != null && seat.footY > 0.01f;
                var footRot = Quaternion.LookRotation(legFwd, Vector3.up) * Quaternion.Euler(ring ? 22f : 0f, 0, 0);
                FootGoal(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, BodyPoint(sit.footL), BodyPoint(sit.kneeL) + legFwd * 0.3f, footRot, e);
                FootGoal(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, BodyPoint(sit.footR), BodyPoint(sit.kneeR) + legFwd * 0.3f, footRot, e);
            }
            else
            {
                anim.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f); anim.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
                anim.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f); anim.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
            }

            // ---- hands
            var chest = head != null ? head.position + Vector3.down * 0.35f : transform.position + Vector3.up * 1.3f;
            Vector3 clipR = anim.GetIKPosition(AvatarIKGoal.RightHand), clipL = anim.GetIKPosition(AvatarIKGoal.LeftHand);
            float wr = rW, wl = lW; Vector3 pr = rT, pl = lT;
            // gesture geometry is measured from this body's own shoulders and arm length, so big and small people
            // bend their elbows the same way (never a straight "zombie" arm)
            float a = dimsSet && dims.arm > 0.2f ? dims.arm : 0.55f * Mathf.Max(0.6f, dims.stature / 1.75f);
            var boneR = anim.GetBoneTransform(HumanBodyBones.RightUpperArm); var boneL = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Vector3 shoR = boneR != null ? boneR.position : chest + right * 0.2f, shoL = boneL != null ? boneL.position : chest - right * 0.2f;
            Quaternion? rotR = null, rotL = null; float rotWR = 0.55f, rotWL = 0.55f; Vector3? hintR = null, hintL = null;
            string gest = !seated && emote == Emote.None && rW < 0.01f && lW < 0.01f && speed < 0.15f && (pose == Pose.Idle || pose == Pose.Guard) ? idleGesture : null;
            if (emote == Emote.Wave && k > 0f && (seated || !Has("Wave")))
            {
                // hand up beside the head, forearm swinging from the elbow, palm to whoever is being greeted
                float sw = Mathf.Sin(t * 8.5f);
                pr = shoR + right * (0.30f + sw * 0.16f) * a + Vector3.up * 0.60f * a + fwd * 0.26f * a;
                hintR = shoR + right * 0.62f * a + Vector3.down * 0.12f * a + fwd * 0.08f * a;
                rotR = Quaternion.LookRotation(Vector3.up, -fwd) * Quaternion.Euler(0, 0, -sw * 18f); rotWR = 0.85f;
                wr = Mathf.Max(wr, k);
            }
            else if (emote == Emote.Clap && k > 0f)
            {
                float gap = (0.02f + Mathf.Abs(Mathf.Sin(t * 9f)) * 0.22f) * a;
                var c = chest + fwd * 0.52f * a + Vector3.up * 0.05f;
                pr = c + right * gap; pl = c - right * gap; wr = Mathf.Max(wr, k); wl = Mathf.Max(wl, k);
                rotR = Quaternion.LookRotation(fwd + Vector3.up * 0.6f, -right); rotL = Quaternion.LookRotation(fwd + Vector3.up * 0.6f, right); rotWR = rotWL = 0.8f;
                if (Mathf.Sin(t * 9f) * Mathf.Sin((t - dt) * 9f) < 0f && k > 0.6f) Sfx.Play("clap", c, 0.15f, Random.Range(0.9f, 1.15f));
            }
            else if (emote == Emote.Point && k > 0f)
            {
                pr = shoR + fwd * 0.85f * a + Vector3.up * 0.05f * a; wr = Mathf.Max(wr, k);
            }
            else if (emote == Emote.Cheer && k > 0f && (true))
            {
                // both fists up, pumping
                float pump = Mathf.Abs(Mathf.Sin(t * 7f)) * 0.14f * a;
                pr = shoR + Vector3.up * (0.72f * a + pump) + right * 0.16f * a + fwd * 0.12f * a;
                pl = shoL + Vector3.up * (0.70f * a + pump) - right * 0.16f * a + fwd * 0.12f * a;
                rotR = rotL = Quaternion.LookRotation(Vector3.up, -fwd); rotWR = rotWL = 0.7f;
                wr = Mathf.Max(wr, k); wl = Mathf.Max(wl, k);
            }
            else if (emote == Emote.Facepalm && k > 0f && (seated || !Has("Facepalm")))
            {
                pr = headPos + fwd * 0.21f + Vector3.up * 0.06f + right * 0.03f; wr = Mathf.Max(wr, k * 0.95f);
                hintR = shoR + right * 0.35f * a + Vector3.down * 0.45f * a + fwd * 0.35f * a;
                rotR = Quaternion.LookRotation(Vector3.up - fwd * 0.3f, fwd); rotWR = 0.7f;
            }
            else if (emote == Emote.Talk && k > 0f && (seated || !Has("Talk")))
            {
                // conversational hands: forearms up in front of the belly, palms turning out on the beat, never in step
                float b1 = Mathf.Sin(t * 2.6f), b2 = Mathf.Sin(t * 3.4f + 1f);
                pr = shoR + fwd * (0.40f + Mathf.Max(0f, b1) * 0.10f) * a + Vector3.down * (0.40f - Mathf.Max(0f, b2) * 0.20f) * a + right * (0.03f + b1 * 0.07f) * a;
                pl = shoL + fwd * 0.38f * a + Vector3.down * (0.42f - Mathf.Max(0f, -b1) * 0.14f) * a - right * (0.02f - b2 * 0.06f) * a;
                hintR = shoR + right * 0.25f * a + Vector3.down * 0.6f * a - fwd * 0.15f * a;
                hintL = shoL - right * 0.25f * a + Vector3.down * 0.6f * a - fwd * 0.15f * a;
                rotR = Quaternion.LookRotation(fwd + Vector3.up * 0.35f, Vector3.up + right * (0.6f + b1 * 0.5f));
                rotL = Quaternion.LookRotation(fwd + Vector3.up * 0.35f, Vector3.up - right * (0.6f - b2 * 0.5f));
                rotWR = rotWL = 0.6f;
                wr = Mathf.Max(wr, k * 0.9f); wl = Mathf.Max(wl, k * 0.6f);
            }
            else if (gest == "IdleThink")
            {
                // chin in hand, the other arm folded under the elbow
                pr = headPos + fwd * 0.13f + Vector3.down * 0.13f + right * 0.01f;
                pl = chest + fwd * 0.42f * a + right * 0.12f * a + Vector3.down * 0.30f * a;
                hintR = shoR + right * 0.10f * a + Vector3.down * 0.60f * a + fwd * 0.45f * a;
                hintL = shoL - right * 0.45f * a + Vector3.down * 0.45f * a + fwd * 0.2f * a;
                rotR = Quaternion.LookRotation(Vector3.up - fwd * 0.2f, fwd); rotL = Quaternion.LookRotation(right + fwd * 0.2f, Vector3.up); rotWR = rotWL = 0.6f;
                wr = Mathf.Max(wr, 0.95f); wl = Mathf.Max(wl, 0.9f);
            }
            else if (gest == "IdleHips")
            {
                // hands on hips, elbows out
                var hp = hips != null ? hips.position : transform.position + Vector3.up * 0.95f;
                pr = hp + right * 0.205f + Vector3.up * 0.07f + fwd * 0.03f; pl = hp - right * 0.205f + Vector3.up * 0.07f + fwd * 0.03f;
                hintR = shoR + right * 0.75f * a + Vector3.down * 0.4f * a - fwd * 0.25f * a;
                hintL = shoL - right * 0.75f * a + Vector3.down * 0.4f * a - fwd * 0.25f * a;
                rotR = Quaternion.LookRotation(Vector3.down + fwd * 0.5f, right); rotL = Quaternion.LookRotation(Vector3.down + fwd * 0.5f, -right); rotWR = rotWL = 0.5f;
                wr = Mathf.Max(wr, 0.95f); wl = Mathf.Max(wl, 0.95f);
            }
            else if (gest == "IdleBack")
            {
                // hands clasped behind the back (security at ease)
                var hp = hips != null ? hips.position : transform.position + Vector3.up * 0.95f;
                pr = hp - fwd * 0.17f + right * 0.03f + Vector3.up * 0.06f; pl = hp - fwd * 0.17f - right * 0.03f + Vector3.up * 0.04f;
                hintR = shoR + right * 0.45f * a + Vector3.down * 0.5f * a - fwd * 0.45f * a;
                hintL = shoL - right * 0.45f * a + Vector3.down * 0.5f * a - fwd * 0.45f * a;
                rotWR = rotWL = 0f;
                wr = Mathf.Max(wr, 0.95f); wl = Mathf.Max(wl, 0.95f);
            }
            else if (gest == "IdleEar")
            {
                // a finger to the earpiece
                pr = headPos + right * 0.115f + fwd * 0.03f + Vector3.down * 0.02f;
                hintR = shoR + right * 0.6f * a + Vector3.down * 0.25f * a + fwd * 0.1f * a;
                rotR = Quaternion.LookRotation(Vector3.up - right * 0.4f, fwd); rotWR = 0.6f;
                wr = Mathf.Max(wr, 0.95f);
            }
            else if (gest == "IdlePhone")
            {
                // thumbing a phone held in front of the chest
                pl = chest + fwd * 0.50f * a - right * 0.04f * a + Vector3.down * 0.12f * a;
                pr = pl + right * 0.09f + Vector3.down * 0.015f + Vector3.up * Mathf.Max(0f, Mathf.Sin(t * 5f)) * 0.012f;
                rotL = Quaternion.LookRotation(fwd + Vector3.up * 0.9f, Vector3.up - fwd * 0.6f + right * 0.3f); rotR = Quaternion.LookRotation(fwd * 0.6f - right * 0.6f + Vector3.up * 0.4f, Vector3.up); rotWR = rotWL = 0.6f;
                wr = Mathf.Max(wr, 0.9f); wl = Mathf.Max(wl, 0.95f);
            }
            else if (gest == "IdleArms")
            {
                // arms folded across the chest
                var c = chest + fwd * 0.34f * a + Vector3.down * 0.22f * a;
                pr = c - right * 0.20f * a; pl = c + right * 0.20f * a + Vector3.up * 0.04f * a + fwd * 0.03f;
                hintR = shoR + right * 0.5f * a + Vector3.down * 0.65f * a + fwd * 0.1f * a;
                hintL = shoL - right * 0.5f * a + Vector3.down * 0.65f * a + fwd * 0.1f * a;
                rotR = Quaternion.LookRotation(-right + Vector3.up * 0.2f, Vector3.up); rotL = Quaternion.LookRotation(right + Vector3.up * 0.2f, Vector3.up); rotWR = rotWL = 0.6f;
                wr = Mathf.Max(wr, 0.95f); wl = Mathf.Max(wl, 0.95f);
            }
            else if (pose == Pose.DJ && emote == Emote.None)
            {
                float beat = Mathf.Pow(1f - Mathf.Repeat(Time.time * bpm / 60f, 1f), 3f);
                pr = chest + fwd * 0.45f + Vector3.down * (0.32f - beat * 0.03f) + right * (0.26f + Mathf.Sin(t * 1.3f) * 0.04f);
                pl = chest + fwd * 0.45f + Vector3.down * 0.32f - right * (0.26f + Mathf.Sin(t * 0.9f) * 0.05f);
                if (Mathf.Repeat(t, 6f) > 4.5f) pl += Vector3.up * 0.45f;   // hand up for the drop
                wr = Mathf.Max(wr, 0.85f); wl = Mathf.Max(wl, 0.85f);
            }
            else if (pose == Pose.Bartend && emote == Emote.None)
            {
                pr = chest + fwd * 0.3f + right * 0.15f + Vector3.up * (0.1f + Mathf.Sin(t * 14f) * 0.07f);
                pl = chest + fwd * 0.3f - right * 0.05f + Vector3.up * (0.12f + Mathf.Sin(t * 14f + 0.3f) * 0.07f);
                wr = Mathf.Max(wr, 0.8f); wl = Mathf.Max(wl, 0.8f);
            }
            else if (pose == Pose.Play && emote == Emote.None)
            {
                // hands resting on the rail, fiddling with chips now and then
                pr = chest + fwd * 0.42f + Vector3.down * 0.42f + right * 0.18f + Vector3.up * Mathf.Max(0, Mathf.Sin(t * 0.7f)) * 0.04f;
                pl = chest + fwd * 0.42f + Vector3.down * 0.42f - right * 0.2f;
                wr = Mathf.Max(wr, 0.55f); wl = Mathf.Max(wl, 0.55f);
            }
            else if (e > 0.001f && sit.valid && emote == Emote.None && restHands)
            {
                // seated: hands rest on the rail / counter (or the thighs); a caller's reach (chips, a button, a drink at
                // the chest) wins in proportion to its weight instead of being overwritten by the rest pose
                var restR = BodyPoint(sit.handR) + Vector3.up * Mathf.Max(0f, Mathf.Sin(t * 0.6f)) * 0.012f;
                var restL = BodyPoint(sit.handL);
                pr = Vector3.Lerp(restR, rT, Mathf.Clamp01(rW));
                pl = Vector3.Lerp(restL, lT, Mathf.Clamp01(lW));
                wr = Mathf.Max(wr, sit.handW * e); wl = Mathf.Max(wl, sit.handW * e);
            }
            // smooth every hand target and weight: switching between gestures, reaches and rest never snaps
            // (scripted guest reaches already follow eased arcs, so they are tracked more tightly)
            float kp = 1f - Mathf.Exp(-(driven ? 20f : 9f) * dt);
            if (srW < 0.001f) sr = pr; if (slW < 0.001f) sl = pl;
            sr = Vector3.Lerp(sr, pr, kp); sl = Vector3.Lerp(sl, pl, kp);
            srW = Mathf.MoveTowards(srW, Mathf.Clamp01(wr), dt * 2.6f);
            slW = Mathf.MoveTowards(slW, Mathf.Clamp01(wl), dt * 2.6f);
            float ew = Mathf.SmoothStep(0f, 1f, srW), lw = Mathf.SmoothStep(0f, 1f, slW);
            if (rotR.HasValue) { qr = srW < 0.02f ? rotR.Value : Quaternion.Slerp(qr, rotR.Value, kp); qrW = Mathf.MoveTowards(qrW, rotWR, dt * 2.6f); } else qrW = Mathf.MoveTowards(qrW, rotWR < 0.01f ? 0f : -1f, dt * 2.6f);
            if (rotL.HasValue) { ql = slW < 0.02f ? rotL.Value : Quaternion.Slerp(ql, rotL.Value, kp); qlW = Mathf.MoveTowards(qlW, rotWL, dt * 2.6f); } else qlW = Mathf.MoveTowards(qlW, rotWL < 0.01f ? 0f : -1f, dt * 2.6f);
            // ---- the body is solid: a hand that the clip, a gesture or a reach would put inside the torso (or the
            // belly of the bigger builds) is slid out to its surface instead
            {
                Vector3 outR, outL;
                bool hitR = PushOut(Vector3.Lerp(clipR, sr, ew), out outR), hitL = PushOut(Vector3.Lerp(clipL, sl, lw), out outL);
                if (hitR) colR = colWR < 0.02f ? outR : Vector3.Lerp(colR, outR, 1f - Mathf.Exp(-14f * dt));
                if (hitL) colL = colWL < 0.02f ? outL : Vector3.Lerp(colL, outL, 1f - Mathf.Exp(-14f * dt));
                colWR = Mathf.MoveTowards(colWR, hitR ? 1f : 0f, dt * 5f); colWL = Mathf.MoveTowards(colWL, hitL ? 1f : 0f, dt * 5f);
                if (colWR > 0.001f) { sr = Vector3.Lerp(Vector3.Lerp(clipR, sr, ew), colR, colWR); ew = Mathf.Max(ew, colWR); }
                if (colWL > 0.001f) { sl = Vector3.Lerp(Vector3.Lerp(clipL, sl, lw), colL, colWL); lw = Mathf.Max(lw, colWL); }
            }
            Goal(AvatarIKGoal.RightHand, sr, ew, fwd, right);
            Goal(AvatarIKGoal.LeftHand, sl, lw, fwd, -right);
            if (qrW >= 0f) { anim.SetIKRotationWeight(AvatarIKGoal.RightHand, ew * qrW); if (qrW > 0f) anim.SetIKRotation(AvatarIKGoal.RightHand, qr); }
            if (qlW >= 0f) { anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, lw * qlW); if (qlW > 0f) anim.SetIKRotation(AvatarIKGoal.LeftHand, ql); }
            if (hintR.HasValue) hR = srW < 0.02f ? hintR.Value : Vector3.Lerp(hR, hintR.Value, kp);
            if (hintL.HasValue) hL = slW < 0.02f ? hintL.Value : Vector3.Lerp(hL, hintL.Value, kp);
            hRW = Mathf.MoveTowards(hRW, hintR.HasValue ? 1f : 0f, dt * 2.6f); hLW = Mathf.MoveTowards(hLW, hintL.HasValue ? 1f : 0f, dt * 2.6f);
            if (handFloor > 0f && !seated)
            {
                // a dealer's hands stay on top of the table: whatever the clip or a reach asks for is lifted onto the felt
                KeepAbove(AvatarIKGoal.RightHand, Vector3.Lerp(clipR, sr, ew), fwd, ref ew);
                KeepAbove(AvatarIKGoal.LeftHand, Vector3.Lerp(clipL, sl, lw), fwd, ref lw);
            }
            // elbows out, back and down so arms bend naturally while reaching (never straight-armed robots)
            var shR = chest + right * 0.2f + Vector3.up * 0.12f; var shL = chest - right * 0.2f + Vector3.up * 0.12f;
            anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, ew * Mathf.Lerp(0.7f, 1f, hRW));
            anim.SetIKHintPosition(AvatarIKHint.RightElbow, Vector3.Lerp(Vector3.Lerp(shR, sr, 0.5f) + right * 0.22f + Vector3.down * 0.22f - fwd * 0.06f, hR, hRW));
            anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, lw * Mathf.Lerp(0.7f, 1f, hLW));
            anim.SetIKHintPosition(AvatarIKHint.LeftElbow, Vector3.Lerp(Vector3.Lerp(shL, sl, 0.5f) - right * 0.22f + Vector3.down * 0.22f - fwd * 0.06f, hL, hLW));
        }

        Vector3 sr, sl; float srW, slW;
        Vector3 colR, colL; float colWR, colWL;
        /// <summary>Half-thickness of the torso (m). 0 = from the build; the plus-size people are given more.</summary>
        public float girth;

        /// <summary>Torso as a capsule from the pelvis to the neck. True (and the nearest point on its surface) when
        /// <paramref name="p"/> is inside it. Works in any orientation (standing, seated, lying on a sofa).</summary>
        bool PushOut(Vector3 p, out Vector3 q)
        {
            q = p;
            if (hips == null || head == null) return false;
            float sc = Mathf.Max(0.6f, dims.stature > 0.5f ? dims.stature / 1.75f : 1f);
            float r = (girth > 0f ? girth : 0.165f) * sc * (model != null ? Mathf.Max(0.9f, model.localScale.x / Mathf.Max(0.01f, model.localScale.y)) : 1f);
            Vector3 a = hips.position, b = head.position, ax = b - a; float len = ax.magnitude; if (len < 0.05f) return false; ax /= len;
            a -= ax * 0.06f; b -= ax * 0.1f;
            // bellies stick out in front, not behind
            var f = transform.forward * (girth > 0.2f ? 0.05f * sc : 0.015f * sc); a += f; b += f * 0.4f;
            float t = Mathf.Clamp(Vector3.Dot(p - a, ax), 0f, Vector3.Distance(a, b));
            var c = a + ax * t; var d = p - c; float m = d.magnitude, keep = r + 0.045f;
            if (m >= keep) return false;
            if (m < 1e-4f) d = transform.forward; else d /= m;
            q = c + d * keep;
            return true;
        }

        // ------------------------------------------------------------------ eyes
        Transform lids;
        /// <summary>Closes (or opens) the eyes: skin-coloured lids with a lash line over each eye. The face is measured
        /// from the mesh when it can be read; otherwise from the pack's standard head proportions.</summary>
        public void CloseEyes(bool closed)
        {
            if (lids != null) { lids.gameObject.SetActive(closed); return; }
            if (!closed || head == null) return;
            float sc = Mathf.Max(0.6f, dims.stature > 0.5f ? dims.stature / 1.75f : 1f);
            Vector3 up = transform.up, fwd = transform.forward, right = transform.right;
            float faceFwd = (EyeFwd + 0.006f) * sc; Color skin = new Color(0.66f, 0.46f, 0.36f);
            try
            {
                var probe = head.position + up * 0.09f * sc + fwd * 0.45f * sc;   // out in front of the nose
                var cheek = head.position + up * 0.05f * sc + right * 0.45f * sc;   // skin is sampled on the cheek, never on the eye
                float best = 1e9f, bestSkin = 1e9f;
                foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (r.sharedMesh == null || !r.sharedMesh.isReadable || Clothes(r.name) || r.name.Contains("Glasses") || r.name.Contains("Hair")) continue;
                    var baked = new Mesh(); r.BakeMesh(baked, true);
                    var vs = baked.vertices; var uvs = baked.uv; var l2w = r.transform.localToWorldMatrix;
                    for (int i = 0; i < vs.Length; i++)
                    {
                        var w = l2w.MultiplyPoint3x4(vs[i]); float d = (w - probe).sqrMagnitude;
                        if (d < best) { best = d; faceFwd = Vector3.Dot(w - head.position, fwd) - 0.03f * sc; }
                        float ds = (w - cheek).sqrMagnitude;
                        if (ds >= bestSkin) continue;
                        bestSkin = ds;
                        var mat = r.sharedMaterial; if (mat == null || i >= uvs.Length) continue;
                        var tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : mat.HasProperty("_MainTex") ? mat.mainTexture : null;
                        Color tint = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                        var sk = tex != null ? SamplePixel(tex, uvs[i]) * tint : tint; sk.a = 1f;
                        // a lid is never white or black: if the sample missed the skin, keep the default tone
                        float mx = Mathf.Max(sk.r, Mathf.Max(sk.g, sk.b)), mn = Mathf.Min(sk.r, Mathf.Min(sk.g, sk.b));
                        if (mx > 0.2f && mx - mn > 0.08f && sk.r > sk.b) skin = sk;
                    }
                    Destroy(baked);
                }
            }
            catch (System.Exception) { }
            lids = new GameObject("Eyelids").transform;
            lids.SetParent(transform, false);
            var k = new MeshKit(); var lk = new MeshKit(); var lash = new Color(0.16f, 0.1f, 0.08f);
            foreach (var sx in new[] { -1f, 1f })
            {
                var c = new Vector3(sx * EyeSide * sc, 0, 0);
                k.Sphere(c, new Vector3(0.031f, 0.027f, 0.028f) * sc, 14, Color.white, 0f);
                for (int i = -3; i <= 3; i++)      // the lash line: a shallow downward arc along the bottom of the lid
                    lk.Box(c + new Vector3(i * 0.0065f * sc, (-0.012f + i * i * 0.0009f) * sc, 0.026f * sc), new Vector3(0.0075f, 0.0038f, 0.006f) * sc, Quaternion.Euler(0, 0, i * 9f), Color.white, 0f);
            }
            // the tone goes in the material itself (the flat shader does not tint by vertex colour)
            k.Spawn("Lids", lids, Palette.Flat(skin, 0.02f, 0.02f, 0.25f), false);
            lk.Spawn("Lashes", lids, Palette.Flat(lash, 0.02f, 0.02f, 0.25f), false);
            lids.position = head.position + up * EyeUp * sc + fwd * (faceFwd + 0.012f * sc);
            lids.rotation = Quaternion.LookRotation(fwd, up);
            lids.SetParent(head, true);
        }
        /// <summary>Head-bone → eye line, in metres for a 1.75 m person (tuned on the Casino pack's heads).</summary>
        public static float EyeUp = 0.118f, EyeFwd = 0.098f, EyeSide = 0.047f;

        static Material lidMat;
        static Material LidMat { get { if (lidMat == null) lidMat = Palette.Flat(Color.white, 0.02f, 0.02f, 0.25f); return lidMat; } }

        static Color SamplePixel(Texture tex, Vector2 uv)
        {
            int W = Mathf.Clamp(tex.width, 16, 2048), Hh = Mathf.Clamp(tex.height, 16, 2048);   // full size: palette atlases have tiny cells
            var rt = RenderTexture.GetTemporary(W, Hh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(Mathf.Clamp(Mathf.Repeat(uv.x, 1f) * W, 0, W - 1), Mathf.Clamp(Mathf.Repeat(uv.y, 1f) * Hh, 0, Hh - 1), 1, 1), 0, 0); t.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var c = t.GetPixel(0, 0); Destroy(t);
            return c;
        }

        /// <summary>Debug: what this character is made of (renderers, materials, head measurements).</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(name + " head=" + (head != null ? transform.InverseTransformPoint(head.position).ToString("F3") : "-") + " hips=" + (hips != null ? transform.InverseTransformPoint(hips.position).ToString("F3") : "-") + " stature=" + dims.stature.ToString("F2"));
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial; var smr = r as SkinnedMeshRenderer;
                var c = transform.InverseTransformPoint(r.bounds.center); var e = r.bounds.size;
                sb.AppendLine("  " + r.name + (r.enabled && r.gameObject.activeInHierarchy ? "" : " (off)") + " mat=" + (m != null ? m.name : "-") + " tex=" + (m != null && m.mainTexture != null ? m.mainTexture.name : "-")
                    + " readable=" + (smr != null && smr.sharedMesh != null && smr.sharedMesh.isReadable) + " c=" + c.ToString("F2") + " size=" + e.ToString("F2"));
            }
            return sb.ToString();
        }
        Quaternion qr = Quaternion.identity, ql = Quaternion.identity; float qrW = -1f, qlW = -1f;   // -1 = default palm-down rotation
        Vector3 hR, hL; float hRW, hLW;

        void KeepAbove(AvatarIKGoal g, Vector3 p, Vector3 fwd, ref float w)
        {
            float ahead = Vector3.Dot(p - transform.position, fwd);
            if (ahead < 0.14f || p.y >= handFloor) return;
            // the closer to the body, the less it matters (hands hanging by the hips are behind the table edge)
            float k = Mathf.Clamp01((ahead - 0.14f) / 0.1f);
            p.y = Mathf.Lerp(p.y, handFloor, k);
            anim.SetIKPositionWeight(g, 1f); anim.SetIKPosition(g, p);
            anim.SetIKRotationWeight(g, Mathf.Max(0.55f, k)); anim.SetIKRotation(g, Quaternion.LookRotation(fwd, Vector3.up));
            w = 1f;
        }

        void FootGoal(AvatarIKGoal g, AvatarIKHint h, Vector3 p, Vector3 knee, Quaternion rot, float w)
        {
            anim.SetIKPositionWeight(g, w); anim.SetIKPosition(g, p);
            anim.SetIKRotationWeight(g, w * 0.8f); anim.SetIKRotation(g, rot);
            anim.SetIKHintPositionWeight(h, w); anim.SetIKHintPosition(h, knee);
        }

        void Goal(AvatarIKGoal g, Vector3 p, float w, Vector3 fwd, Vector3 side)
        {
            w = Mathf.Clamp01(w);
            anim.SetIKPositionWeight(g, w);
            // palm down, fingers forward: hands land flat on felt and chips instead of twisting
            anim.SetIKRotationWeight(g, w * 0.55f);
            if (w > 0.001f)
            {
                anim.SetIKPosition(g, p);
                anim.SetIKRotation(g, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(20f, 0, 0));
            }
        }
    }
}
