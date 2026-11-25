using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ascend
{
    /// <summary>VIP host of the Sky Lounge: greets Legends and Mythics.</summary>
    public class VipHost : MonoBehaviour
    {
        AvatarBody host; float next;
        void Start()
        {
            host = Avatars.Create("VipHost", transform, Vector3.zero, 0f, Rarity.Mythic, RoninRole.Host);
            var ap = host.gameObject.AddComponent<AvatarProfile>(); ap.displayName = "SERAPHINE · VIP HOST"; ap.rarity = Rarity.Mythic; ap.xp = 610000; ap.status = "STAFF · SKY LOUNGE";
        }
        void Update()
        {
            if (!Rig.Ready || Time.time < next) return;
            if ((Rig.I.Head.position - transform.position).sqrMagnitude < 9f)
            {
                next = Time.time + 40f;
                host.lookAtTarget = Rig.I.Head;
                host.Play(Emote.Wave, 1.4f);
                Voice.Say(GameState.I.CurrentTier >= Tier.Mythic || GameState.I.nft == Rarity.Mythic ? "vip_legend" : "vip_hello", host.HeadBone, 0.55f);
            }
        }
    }
}
