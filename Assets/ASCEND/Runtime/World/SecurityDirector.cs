using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Casino security. Guards patrol; if your session winnings pass the threshold, two of them walk up
    /// and escort you out of the floor (lobby + 2 min table ban). Throw a chip at a guard at your own risk.</summary>
    public class SecurityDirector : MonoBehaviour
    {
        public static SecurityDirector I { get; private set; }
        readonly List<AvatarBody> guards = new List<AvatarBody>();
        readonly List<Vector3> posts = new List<Vector3>();
        bool escorting; int warnings;
        float nextCheck;

        void Awake() { I = this; }

        public void Register(AvatarBody g) { guards.Add(g); posts.Add(g.transform.localPosition); }

        void Update()
        {
            if (escorting || Time.time < nextCheck || !GameState.Exists || !Rig.Ready) return;
            nextCheck = Time.time + 1f;
            var gs = GameState.I;
            if (gs.sessionNet > gs.securityWinThreshold * 0.75f && warnings == 0)
            {
                warnings = 1;
                var g = Nearest(); if (g != null) { Voice.Say("security_watch", g.HeadBone, 0.55f); g.lookAtTarget = Rig.I.Head; }
                gs.Notify("SECURITY IS WATCHING YOU…", Palette.Red);
            }
            if (gs.sessionNet > gs.securityWinThreshold) StartCoroutine(Escort());
        }

        AvatarBody Nearest()
        {
            AvatarBody best = null; float bd = float.MaxValue;
            foreach (var g in guards) { if (g == null || !g.gameObject.activeInHierarchy) continue; float d = (g.transform.position - Rig.I.Head.position).sqrMagnitude; if (d < bd) { bd = d; best = g; } }
            return best;
        }

        IEnumerator Escort()
        {
            escorting = true;
            var gs = GameState.I;
            Sfx.UI("security_alert", 0.9f);
            // a guard arrives next to the player (on whatever floor) — teleported in from the elevator, then walks the last metres
            var head = Rig.I.Head;
            var fwd = Rig.I.HeadForwardFlat;
            var feet = Rig.I.Feet;
            var probe = feet + fwd * 5.5f; probe.y = 0;
            if (probe.magnitude > Tower.FloorRadius - 2f || new Vector2(probe.x, probe.z).magnitude < 3.5f) { var c = -new Vector3(feet.x, 0, feet.z); fwd = c.sqrMagnitude > 0.01f ? c.normalized : Vector3.forward; }
            var g = Avatars.Create("Escort", null, feet + fwd * 5f, 0f, Rarity.Rare, RoninRole.Security);
            g.transform.rotation = Quaternion.LookRotation(-fwd);
            var g2 = Avatars.Create("Escort2", null, feet + fwd * 5.5f + Vector3.Cross(Vector3.up, fwd) * 0.8f, 0f, Rarity.Rare, RoninRole.Security);
            g2.transform.rotation = g.transform.rotation;
            g.pose = Pose.Walk; g2.pose = Pose.Walk;
            g.lookAtTarget = head; g2.lookAtTarget = head;
            float t = 0;
            var a1 = g.transform.position; var a2 = g2.transform.position;
            var b1 = feet + fwd * 0.9f; var b2 = feet + fwd * 1.1f + Vector3.Cross(Vector3.up, fwd) * 0.7f;
            while (t < 2.6f) { t += Time.deltaTime; float k = t / 2.6f; g.transform.position = Vector3.Lerp(a1, b1, k); g2.transform.position = Vector3.Lerp(a2, b2, k); if ((int)(t * 2.4f) != (int)((t - Time.deltaTime) * 2.4f)) Sfx.Play("footstep", g.transform.position, 0.6f); yield return null; }
            g.pose = Pose.Guard; g2.pose = Pose.Guard;
            float d = Voice.Say("security_word", g.HeadBone, 0.55f);
            g.Play(Emote.Point, 1.5f);
            yield return new WaitForSeconds(Mathf.Max(1.8f, d));
            d = Voice.Say("security_come", g.HeadBone, 0.55f);
            gs.Notify("YOU WON TOO MUCH · SECURITY ESCORTS YOU OUT", Palette.Red);
            yield return new WaitForSeconds(Mathf.Max(1.8f, d));
            gs.Ban(120f);
            gs.Unlock("TOO GOOD", "GOT ESCORTED OUT BY SECURITY", 1500);
            if (FloorManager.I != null) FloorManager.I.GoTo(0);
            yield return new WaitForSeconds(0.6f);
            Destroy(g.gameObject); Destroy(g2.gameObject);
            yield return new WaitForSeconds(1.2f);
            var lobbyGuard = Nearest();
            if (lobbyGuard != null) Voice.Say("security_locked", lobbyGuard.HeadBone, 0.55f);
            gs.Notify("TABLES CLOSED FOR YOU FOR 2 MIN · ENJOY THE BAR AND THE DJ", Palette.Gold);
            warnings = 0;
            escorting = false;
        }

        /// <summary>A thrown chip hit a guard.</summary>
        public void ChipHit(AvatarBody g)
        {
            if (g == null) return;
            Voice.Say(Random.value < 0.5f ? "security_dont" : "security_watch", g.HeadBone, 0.55f);
            g.Play(Emote.Facepalm, 1.2f);
            GameState.I.Unlock("BAD IDEA", "THREW A CHIP AT SECURITY", 100);
        }
    }
}
