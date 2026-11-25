using UnityEngine;

namespace Ascend
{
    public enum Pose { Idle, Walk, Dance, Play, DJ, Shoot, Guard, Bartend, Sit, Deal }
    public enum Emote { None, Cheer, Clap, Wave, Point, Facepalm, Nod, Talk }

    /// <summary>Procedural animation for the Ronin: idle breathing, walk cycle from velocity, dancing on the beat,
    /// two-bone IK so dealers really reach the shoe / wheel / chips, head look-at and one-shot emotes.</summary>
    public class RoninAnim : AvatarBody
    {
        [HideInInspector] public Vector3 leftTarget, rightTarget;
        [HideInInspector] public float leftWeight, rightWeight;

        Transform[] b;
        Quaternion[] rest;
        Vector3[] restDir;
        Vector3 lastPos;
        float speed, phase, seed;
        Emote emote; float emoteT, emoteDur;
        float lUpper, lFore, rUpper, rFore, lThigh, lShin;

        public override Transform HeadBone { get { return b != null ? b[RoninBuilder.Head] : transform; } }
        public override Transform RightHand { get { return b[RoninBuilder.HandR]; } }
        public override Transform LeftHand { get { return b[RoninBuilder.HandL]; } }

        public void Init(Transform[] bones, Rarity r, RoninRole ro)
        {
            b = bones; rarity = r; role = ro;
            rest = new Quaternion[b.Length]; restDir = new Vector3[b.Length];
            for (int i = 0; i < b.Length; i++)
            {
                rest[i] = b[i].localRotation;
                restDir[i] = b[i].childCount > 0 ? b[i].GetChild(0).localPosition.normalized : Vector3.down;
            }
            restDir[RoninBuilder.HandL] = restDir[RoninBuilder.HandR] = Vector3.down;
            restDir[RoninBuilder.ShinL] = restDir[RoninBuilder.ShinR] = Vector3.down;
            lThigh = Vector3.Distance(b[RoninBuilder.ThighL].position, b[RoninBuilder.ShinL].position) / transform.lossyScale.y;
            lShin = 0.42f;   // knee joint (0.5) to ankle (0.08), see RoninBuilder.JointPos
            lUpper = Vector3.Distance(b[RoninBuilder.UArmL].position, b[RoninBuilder.FArmL].position) / transform.lossyScale.y;
            lFore = Vector3.Distance(b[RoninBuilder.FArmL].position, b[RoninBuilder.HandL].position) / transform.lossyScale.y;
            rUpper = lUpper; rFore = lFore;
            seed = Random.value * 10f;
            lastPos = transform.position;
            born = Time.time;
        }

        public override void Play(Emote e, float dur = 1.6f) { NoteEmote(e); emote = e; emoteT = 0; emoteDur = dur; }

        public override void Reach(bool right, Vector3 world, float weight)
        {
            if (!AcceptReach(weight)) return;
            if (right) { rightTarget = world; rightWeight = weight; } else { leftTarget = world; leftWeight = weight; }
        }

        float Scale { get { return Mathf.Max(0.01f, transform.lossyScale.y); } }
        public override BodyDims Dims
        {
            get
            {
                float sc = Scale;
                return new BodyDims { stature = 1.75f * sc, thigh = (lThigh > 0f ? lThigh : 0.43f) * sc, shin = lShin * sc, ankle = 0.08f * sc, hipW = 0.1f * sc, arm = (lUpper + lFore + 0.06f) * sc };
            }
        }

        void LateUpdate()
        {
            if (b == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var v = (transform.position - lastPos) / dt; v.y = 0; lastPos = transform.position;
            speed = Mathf.Lerp(speed, v.magnitude, 1f - Mathf.Exp(-10f * dt));
            float t = Time.time + seed;
            for (int i = 0; i < b.Length; i++) b[i].localRotation = rest[i];
            var hipsBase = new Vector3(0, 0.95f, 0);
            b[RoninBuilder.Hips].localPosition = hipsBase;

            float target = pose == Pose.Sit && seat != null && sit.valid ? 1f : 0f;
            sitW = Mathf.MoveTowards(sitW, target, dt / 0.65f);
            float e = Mathf.SmoothStep(0f, 1f, sitW);
            bool sitting = sit.valid && e > 0.001f;
            bool walking = (speed > 0.15f || pose == Pose.Walk) && pose != Pose.Sit && !sitting;
            if (walking)
            {
                phase += dt * Mathf.Clamp(speed, 0.6f, 2.2f) * 5.2f;
                float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
                Rot(RoninBuilder.ThighL, s * 28f, 0, 0); Rot(RoninBuilder.ThighR, -s * 28f, 0, 0);
                Rot(RoninBuilder.ShinL, Mathf.Max(0, -c) * 40f, 0, 0); Rot(RoninBuilder.ShinR, Mathf.Max(0, c) * 40f, 0, 0);
                Rot(RoninBuilder.UArmL, -s * 22f, 0, -6f); Rot(RoninBuilder.UArmR, s * 22f, 0, 6f);
                Rot(RoninBuilder.FArmL, -18f, 0, 0); Rot(RoninBuilder.FArmR, -18f, 0, 0);
                b[RoninBuilder.Hips].localPosition = hipsBase + Vector3.up * Mathf.Abs(s) * 0.035f;
                Rot(RoninBuilder.Spine, 4f, Mathf.Sin(phase) * 5f, 0);
            }
            else
            {
                switch (pose)
                {
                    case Pose.Dance: Dance(t); break;
                    case Pose.DJ: DJ(t); break;
                    case Pose.Shoot:
                        Rot(RoninBuilder.UArmR, -80f, 0, 8f); Rot(RoninBuilder.UArmL, -70f, 25f, -10f);
                        Rot(RoninBuilder.FArmL, -40f, 0, 0);
                        Rot(RoninBuilder.Spine, 0, -10f, 0);
                        b[RoninBuilder.Hips].localPosition = hipsBase + Vector3.up * Mathf.Sin(t * 4f) * 0.01f;
                        break;
                    case Pose.Guard:
                        Rot(RoninBuilder.UArmL, -20f, 0, 12f); Rot(RoninBuilder.UArmR, -20f, 0, -12f);
                        Rot(RoninBuilder.FArmL, -75f, 0, 0); Rot(RoninBuilder.FArmR, -75f, 0, 0);
                        Breathe(t); break;
                    case Pose.Play:
                        Rot(RoninBuilder.UArmL, -35f, 0, 5f); Rot(RoninBuilder.UArmR, -35f, 0, -5f);
                        Rot(RoninBuilder.FArmL, -50f + Mathf.Sin(t * 1.3f) * 6f, 0, 0); Rot(RoninBuilder.FArmR, -50f + Mathf.Sin(t * 1.1f + 1) * 6f, 0, 0);
                        Rot(RoninBuilder.Spine, 8f, 0, 0);
                        Breathe(t); break;
                    case Pose.Bartend:
                        Rot(RoninBuilder.UArmR, -40f + Mathf.Sin(t * 3f) * 20f, 0, -10f); Rot(RoninBuilder.FArmR, -70f, 0, 0);
                        Rot(RoninBuilder.UArmL, -30f, 0, 8f); Rot(RoninBuilder.FArmL, -60f, 0, 0);
                        Breathe(t); break;
                    case Pose.Sit:
                        Breathe(t); break;   // the seated body itself is solved below from the seat plan
                    case Pose.Deal:
                        Rot(RoninBuilder.UArmL, -40f, 0, 5f); Rot(RoninBuilder.UArmR, -40f, 0, -5f);
                        Rot(RoninBuilder.FArmL, -55f, 0, 0); Rot(RoninBuilder.FArmR, -55f, 0, 0);
                        Breathe(t); break;
                    default: Breathe(t); break;
                }
            }

            if (sitting) SitPose(t, e);
            if (emote != Emote.None) ApplyEmote(dt, sitting);
            Look();
            float rw = rightWeight, lw = leftWeight; Vector3 rt = rightTarget, lt = leftTarget;
            if (sitting && emote == Emote.None)
            {
                // seated hands rest on the rail / counter / thighs; a reach blends in on top
                rt = Vector3.Lerp(BodyPoint(sit.handR), rightTarget, Mathf.Clamp01(rightWeight)); rw = Mathf.Max(rw, sit.handW * e);
                lt = Vector3.Lerp(BodyPoint(sit.handL), leftTarget, Mathf.Clamp01(leftWeight)); lw = Mathf.Max(lw, sit.handW * e);
            }
            if (rw > 0.001f) SolveArm(true, rt, rw);
            if (lw > 0.001f) SolveArm(false, lt, lw);
        }

        /// <summary>Seated body from the seat plan: hips on the cushion, torso lean, legs solved to the feet targets.</summary>
        void SitPose(float t, float e)
        {
            float sc = Scale;
            var hipsBase = b[RoninBuilder.Hips].localPosition;
            b[RoninBuilder.Hips].localPosition = Vector3.Lerp(hipsBase, sit.hips / sc + new Vector3(0, 0.02f, 0), e);   // hip joints are 2 cm under the pelvis bone
            float lean = (sit.lean + Mathf.Max(rightWeight, leftWeight) * 6f + Mathf.Sin(t * 0.23f) * 1.6f) * e;
            b[RoninBuilder.Spine].localRotation = b[RoninBuilder.Spine].localRotation * Quaternion.Euler(lean, Mathf.Sin(t * 0.11f) * 3f * e, 0);
            var legFwd = transform.rotation * sit.legDir;
            SolveLimb(RoninBuilder.ThighL, RoninBuilder.ShinL, lThigh * sc, lShin * sc, BodyPoint(sit.footL), BodyPoint(sit.kneeL) + legFwd * 0.3f, e);
            SolveLimb(RoninBuilder.ThighR, RoninBuilder.ShinR, lThigh * sc, lShin * sc, BodyPoint(sit.footR), BodyPoint(sit.kneeR) + legFwd * 0.3f, e);
        }

        void SolveLimb(int ui, int li, float l1, float l2, Vector3 target, Vector3 hint, float w)
        {
            var up = b[ui]; var lo = b[li];
            Vector3 a = up.position;
            var knee = Seat.Knee(a, target, l1, l2, hint - a);
            var dirEnd = (target - knee).normalized;
            Quaternion upIK = Quaternion.FromToRotation(up.rotation * restDir[ui], (knee - a).normalized) * up.rotation;
            up.rotation = Quaternion.Slerp(up.rotation, upIK, w);
            Quaternion loIK = Quaternion.FromToRotation(lo.rotation * restDir[li], dirEnd) * lo.rotation;
            lo.rotation = Quaternion.Slerp(lo.rotation, loIK, w);
        }

        void Rot(int i, float x, float y, float z) { b[i].localRotation = rest[i] * Quaternion.Euler(x, y, z); }

        void Breathe(float t)
        {
            float br = Mathf.Sin(t * 1.6f);
            Rot(RoninBuilder.Spine, br * 1.5f, Mathf.Sin(t * 0.4f) * 4f, 0);
            Rot(RoninBuilder.UArmL, 0, 0, -4f + br * 1.5f); Rot(RoninBuilder.UArmR, 0, 0, 4f - br * 1.5f);
            Rot(RoninBuilder.FArmL, -8f, 0, 0); Rot(RoninBuilder.FArmR, -8f, 0, 0);
            b[RoninBuilder.Hips].localPosition = new Vector3(Mathf.Sin(t * 0.35f) * 0.01f, 0.95f, 0);
        }

        void Dance(float t)
        {
            float beat = Time.time * bpm / 60f + seed;
            float k = Mathf.Abs(Mathf.Sin(beat * Mathf.PI));
            int move = Mathf.FloorToInt((beat + seed) / 8f) % 4;
            b[RoninBuilder.Hips].localPosition = new Vector3(Mathf.Sin(beat * Mathf.PI * 0.5f) * 0.05f, 0.95f - k * 0.07f, 0);
            Rot(RoninBuilder.ThighL, -k * 18f, 0, 0); Rot(RoninBuilder.ThighR, -k * 18f, 0, 0);
            Rot(RoninBuilder.ShinL, k * 30f, 0, 0); Rot(RoninBuilder.ShinR, k * 30f, 0, 0);
            Rot(RoninBuilder.Spine, k * 6f, Mathf.Sin(beat * Mathf.PI * 0.5f) * 15f, 0);
            switch (move)
            {
                case 0: // hands up, pumping
                    Rot(RoninBuilder.UArmL, 0, 0, -150f + k * 15f); Rot(RoninBuilder.UArmR, 0, 0, 150f - k * 15f);
                    break;
                case 1: // fist pump right
                    Rot(RoninBuilder.UArmR, -150f - k * 20f, 0, 15f); Rot(RoninBuilder.FArmR, -30f, 0, 0);
                    Rot(RoninBuilder.UArmL, -20f, 0, 0); Rot(RoninBuilder.FArmL, -80f, 0, 0);
                    break;
                case 2: // arms swing side to side
                    float sw = Mathf.Sin(beat * Mathf.PI * 0.5f);
                    Rot(RoninBuilder.UArmL, -60f, sw * 40f, -20f); Rot(RoninBuilder.UArmR, -60f, sw * 40f, 20f);
                    Rot(RoninBuilder.FArmL, -60f, 0, 0); Rot(RoninBuilder.FArmR, -60f, 0, 0);
                    break;
                default: // the "ronin" pose: one arm points to the sky
                    Rot(RoninBuilder.UArmL, 0, 0, -170f); Rot(RoninBuilder.UArmR, -30f, 0, 20f); Rot(RoninBuilder.FArmR, -90f + k * 30f, 0, 0);
                    break;
            }
            Rot(RoninBuilder.Head, k * 12f - 4f, 0, 0);
        }

        void DJ(float t)
        {
            float beat = Time.time * bpm / 60f;
            float k = Mathf.Abs(Mathf.Sin(beat * Mathf.PI));
            Rot(RoninBuilder.Head, k * 15f, 0, 0);
            Rot(RoninBuilder.Spine, 10f + k * 4f, 0, 0);
            Rot(RoninBuilder.UArmR, -55f + Mathf.Sin(t * 2f) * 10f, 0, -12f); Rot(RoninBuilder.FArmR, -55f, 0, 0);
            bool hype = Mathf.FloorToInt(beat / 16f) % 2 == 1;
            if (hype) { Rot(RoninBuilder.UArmL, 0, 0, -160f + k * 20f); }
            else { Rot(RoninBuilder.UArmL, -55f, 0, 12f); Rot(RoninBuilder.FArmL, -50f + Mathf.Sin(t * 5f) * 15f, 0, 0); }
            b[RoninBuilder.Hips].localPosition = new Vector3(0, 0.95f - k * 0.03f, 0);
        }

        void ApplyEmote(float dt, bool sitting)
        {
            emoteT += dt;
            float k = Mathf.Sin(Mathf.Clamp01(emoteT / emoteDur) * Mathf.PI);
            float f = Mathf.Sin(emoteT * 14f);
            switch (emote)
            {
                case Emote.Cheer:
                    Blend(RoninBuilder.UArmL, Quaternion.Euler(0, 0, -160f), k); Blend(RoninBuilder.UArmR, Quaternion.Euler(0, 0, 160f), k);
                    if (!sitting) b[RoninBuilder.Hips].localPosition += Vector3.up * Mathf.Abs(f) * 0.05f * k; break;
                case Emote.Talk:
                    Blend(RoninBuilder.UArmR, Quaternion.Euler(-35f + f * 6f, 0, -10f), k * 0.8f); Blend(RoninBuilder.FArmR, Quaternion.Euler(-60f, 0, 0), k * 0.8f);
                    Blend(RoninBuilder.UArmL, Quaternion.Euler(-25f, 0, 8f), k * 0.5f); Blend(RoninBuilder.FArmL, Quaternion.Euler(-50f - f * 8f, 0, 0), k * 0.5f); break;
                case Emote.Clap:
                    Blend(RoninBuilder.UArmL, Quaternion.Euler(-60f, -30f + f * 8f, 0), k); Blend(RoninBuilder.UArmR, Quaternion.Euler(-60f, 30f - f * 8f, 0), k);
                    Blend(RoninBuilder.FArmL, Quaternion.Euler(-50f, 0, 0), k); Blend(RoninBuilder.FArmR, Quaternion.Euler(-50f, 0, 0), k); break;
                case Emote.Wave:
                    Blend(RoninBuilder.UArmR, Quaternion.Euler(0, 0, 150f), k); Blend(RoninBuilder.FArmR, Quaternion.Euler(0, 0, f * 25f), k); break;
                case Emote.Point:
                    Blend(RoninBuilder.UArmR, Quaternion.Euler(-85f, 0, 0), k); break;
                case Emote.Facepalm:
                    Blend(RoninBuilder.UArmR, Quaternion.Euler(-120f, 30f, 0), k); Blend(RoninBuilder.FArmR, Quaternion.Euler(-100f, 0, 0), k);
                    Blend(RoninBuilder.Head, Quaternion.Euler(20f, 0, 0), k); break;
                case Emote.Nod:
                    Blend(RoninBuilder.Head, Quaternion.Euler(f * 12f, 0, 0), k); break;
            }
            if (emoteT >= emoteDur) emote = Emote.None;
        }

        void Blend(int i, Quaternion add, float k) { b[i].localRotation = Quaternion.Slerp(b[i].localRotation, rest[i] * add, k); }

        void Look()
        {
            Vector3? target = lookAtTarget != null ? lookAtTarget.position : lookAt;
            if (target == null) return;
            var head = b[RoninBuilder.Head];
            var dir = target.Value - head.position;
            var local = Quaternion.Inverse(b[RoninBuilder.Spine].rotation) * dir;
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -65f, 65f);
            float pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -35f, 35f);
            head.localRotation = head.localRotation * Quaternion.Euler(pitch, yaw, 0);
        }

        void SolveArm(bool right, Vector3 target, float w)
        {
            int ua = right ? RoninBuilder.UArmR : RoninBuilder.UArmL, fa = right ? RoninBuilder.FArmR : RoninBuilder.FArmL;
            var up = b[ua]; var fo = b[fa];
            float sc = transform.lossyScale.y;
            float l1 = (right ? rUpper : lUpper) * sc, l2 = (right ? rFore : lFore) * sc;
            Vector3 a = up.position;
            Vector3 toT = target - a; float d = Mathf.Clamp(toT.magnitude, 0.02f, l1 + l2 - 0.002f);
            Vector3 dir = toT.normalized;
            float cosA = (l1 * l1 + d * d - l2 * l2) / (2f * l1 * d);
            float angA = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));
            Vector3 pole = transform.right * (right ? 0.7f : -0.7f) - transform.up * 0.8f - transform.forward * 0.3f;
            pole = Vector3.ProjectOnPlane(pole, dir).normalized;
            Vector3 elbow = a + (dir * Mathf.Cos(angA) + pole * Mathf.Sin(angA)) * l1;
            Vector3 hand = a + dir * d;
            Quaternion upBase = up.rotation, foBase;
            Quaternion upIK = Quaternion.FromToRotation(up.rotation * restDir[ua], (elbow - a).normalized) * up.rotation;
            up.rotation = Quaternion.Slerp(upBase, upIK, w);
            foBase = fo.rotation;
            Quaternion foIK = Quaternion.FromToRotation(fo.rotation * restDir[fa], (hand - fo.position).normalized) * fo.rotation;
            fo.rotation = Quaternion.Slerp(foBase, foIK, w);
        }
    }
}
