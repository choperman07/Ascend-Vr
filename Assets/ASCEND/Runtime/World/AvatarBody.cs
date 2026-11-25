using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>Common API of every character body: the procedural Neon Ronin (NFT avatars) and the imported, rigged
    /// humanoids (androids, guests, security, service robots). Game code only talks to this.</summary>
    public abstract class AvatarBody : MonoBehaviour
    {
        public Pose pose = Pose.Idle;
        public Rarity rarity;
        public RoninRole role;
        public Vector3? lookAt;
        public Transform lookAtTarget;
        public float bpm = 150f;
        [Tooltip("0 samba · 1 griddy · 2 floss · 3 hype · 4 running man · 5 robot")] public int danceStyle;

        public abstract Transform HeadBone { get; }
        public abstract Transform RightHand { get; }
        public abstract Transform LeftHand { get; }
        public abstract void Play(Emote e, float dur = 1.6f);
        /// <summary>Reach a world position with one hand (weight fades in/out smoothly from the caller).</summary>
        public abstract void Reach(bool right, Vector3 world, float weight);

        /// <summary>Proportions used to sit this body (measured from the rig).</summary>
        public virtual BodyDims Dims { get { return BodyDims.Default(1.75f); } }

        /// <summary>Sits on a seat (seat transform = floor under the hips, forward = facing). Null stands up: the seated
        /// pose blends out over ~0.6 s from wherever the body is (a caller can slide it back off the seat meanwhile).</summary>
        public virtual void SitOn(Seat s)
        {
            seat = s;
            if (s != null)
            {
                transform.SetPositionAndRotation(s.transform.position, Quaternion.Euler(0, s.transform.eulerAngles.y, 0));
                sit = s.Plan(Dims, (GetInstanceID() & 1) == 0 ? 1 : -1);
                pose = Pose.Sit;
                // a body created on its seat is already sitting: no stand-to-sit blend on its first frames
                if (Time.time - born < 0.5f) sitW = 1f;
            }
            else if (pose == Pose.Sit) pose = Pose.Idle;
        }

        /// <summary>The current seated pose (body space; valid while sitting and while blending out of it).</summary>
        protected SitPlan sit;
        /// <summary>0 standing … 1 fully seated (eased by the body).</summary>
        protected float sitW;
        /// <summary>When this body was created (set by the concrete body on Awake / Init).</summary>
        protected float born = -10f;
        public float SitWeight { get { return sitW; } }
        public SitPlan SeatPlan { get { return sit; } }
        protected Vector3 BodyPoint(Vector3 local) { return transform.position + transform.rotation * local; }
        /// <summary>World position where this hand rests while seated (table rail, counter or thigh).</summary>
        public Vector3 RestHand(bool right) { return BodyPoint(right ? sit.handR : sit.handL); }

        // ---- guest scripting: BotAvatar drives the arms of a seated guest itself. Reach calls coming from game code while
        // a body is driven (a slot machine asking the guest to pull its lever) are recorded instead of applied, so the
        // guest can answer them with its own natural motion and never snaps to a target out of reach.
        [System.NonSerialized] public bool driven;
        [System.NonSerialized] public bool scripted;
        public float ForeignReachTime { get; private set; }
        public float ForeignReachWeight { get; private set; }
        public Emote LastEmote { get; private set; }
        public float LastEmoteTime { get; private set; }
        /// <summary>Called by Reach implementations: false = ignore this call (recorded for the driver instead).</summary>
        protected bool AcceptReach(float weight)
        {
            if (!driven || scripted) return true;
            ForeignReachTime = Time.time; ForeignReachWeight = weight;
            return false;
        }
        protected void NoteEmote(Emote e) { LastEmote = e; LastEmoteTime = Time.time; }

        protected Seat seat;
        public Seat CurrentSeat { get { return seat; } }
    }

    /// <summary>Creates character bodies: imported rigged humanoids when the asset library has them (built by
    /// ASCEND → Build World from Assets/ASCEND/Imported), otherwise the procedural Neon Ronin.</summary>
    public static class Avatars
    {
        public static bool UseHumanoids = true;

        public static AvatarBody Create(string name, Transform parent, Vector3 localPos, float yaw, Rarity rarity, RoninRole role = RoninRole.Player, float height = 1.75f)
        {
            if (UseHumanoids)
            {
                var lib = AssetLibrary.I;
                if (lib != null && lib.HasHumanoids)
                {
                    var b = HumanoidBody.Create(lib, name, parent, localPos, yaw, rarity, role, height);
                    if (b != null) return b;
                }
            }
            return RoninBuilder.Create(name, parent, localPos, yaw, rarity, role, height);
        }

        /// <summary>A guest or remote player wearing a specific look (Neon Ronin NFT, High Roller, Sharp Suit, Showtime).</summary>
        public static AvatarBody CreateStyled(string name, Transform parent, Vector3 localPos, float yaw, Rarity rarity, AvatarStyle style, float height = 1.75f)
        {
            var lib = AssetLibrary.I;
            if (UseHumanoids && lib != null && lib.HasHumanoids)
            {
                var b = HumanoidBody.Create(lib, name, parent, localPos, yaw, rarity, RoninRole.Player, height, style);
                if (b != null) return b;
            }
            return RoninBuilder.Create(name, parent, localPos, yaw, rarity, RoninRole.Player, height);
        }

        /// <summary>A waiter: with the Casino FREE pack a person in the croupier's waistcoat carrying a tray (the pack's
        /// waiter clips); a service robot only if one is in the library; otherwise a short Ronin.</summary>
        public static AvatarBody Robot(string name, Transform parent, Vector3 localPos, float yaw, Rarity rarity)
        {
            var lib = AssetLibrary.I;
            if (UseHumanoids && lib != null && lib.HasHumanoids && lib.serviceBot == null)
            {
                var hb = HumanoidBody.Create(lib, name, parent, localPos, yaw, rarity, RoninRole.Host, 1.72f);
                if (hb != null) { hb.waiter = true; return hb; }
            }
            if (lib != null && lib.serviceBot != null && lib.robotController != null)
            {
                var b = HumanoidBody.CreateFrom(lib.serviceBot, lib.robotController, lib, name, parent, localPos, yaw, rarity, RoninRole.Host, 1.15f, HumanoidBody.Kind.Robot);
                if (b != null) return b;
            }
            return RoninBuilder.Create(name, parent, localPos, yaw, rarity, RoninRole.Host, 1.3f);
        }
    }
}
