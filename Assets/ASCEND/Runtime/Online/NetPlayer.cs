using UnityEngine;
#if ASCEND_NGO
using Unity.Collections;
using Unity.Netcode;
#endif

namespace Ascend
{
#if ASCEND_NGO
    /// <summary>Networked player: syncs head + hands + floor + NFT. Remote players appear as their Neon Ronin NFT avatar
    /// whose head and hands follow the real person (IK). Owner writes, everyone reads.</summary>
    public class NetPlayer : NetworkBehaviour
    {
        readonly NetworkVariable<FixedString64Bytes> playerName = new NetworkVariable<FixedString64Bytes>("PLAYER", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<int> rarity = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<int> style = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<long> xp = new NetworkVariable<long>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<int> floor = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<Vector3> headPos = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<Quaternion> headRot = new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<Vector3> leftPos = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<Vector3> rightPos = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        AvatarBody avatar; int builtRarity = -1, builtStyle = -1; float nextSend;
        AvatarProfile profile;
        FixedString64Bytes shownName;
        Vector3 smHead, smL, smR; Quaternion smRot = Quaternion.identity;

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                var gs = GameState.I;
                playerName.Value = new FixedString64Bytes(gs.playerName);
                rarity.Value = (int)gs.nft; xp.Value = gs.xp; style.Value = (int)AvatarStyles.Current;
            }
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner)
            {
                if (Time.time < nextSend) return;
                nextSend = Time.time + 0.05f;
                var rig = Rig.I; if (rig == null) return;
                headPos.Value = rig.Head.position; headRot.Value = rig.Head.rotation;
                if (rig.LeftCtrl != null) leftPos.Value = rig.LeftCtrl.position;
                if (rig.RightCtrl != null) rightPos.Value = rig.RightCtrl.position;
                floor.Value = FloorManager.I != null ? FloorManager.I.Current : 0;
                var gs = GameState.I;
                if (rarity.Value != (int)gs.nft) rarity.Value = (int)gs.nft;
                if (xp.Value != gs.xp) xp.Value = gs.xp;
                if (style.Value != (int)AvatarStyles.Current) style.Value = (int)AvatarStyles.Current;
                return;
            }
            // remote: build / rebuild the avatar for their NFT
            if (avatar == null || builtRarity != rarity.Value || builtStyle != style.Value)
            {
                if (avatar != null) Destroy(avatar.gameObject);
                builtRarity = rarity.Value; builtStyle = style.Value;
                avatar = Avatars.CreateStyled("Avatar", transform, Vector3.zero, 0, (Rarity)builtRarity, (AvatarStyle)builtStyle);
                profile = avatar.gameObject.AddComponent<AvatarProfile>();
                profile.online = true; shownName = default(FixedString64Bytes);
                smHead = headPos.Value; smL = leftPos.Value; smR = rightPos.Value; smRot = headRot.Value;
            }
            bool sameFloor = FloorManager.I == null || floor.Value == FloorManager.I.Current;
            if (avatar.gameObject.activeSelf != sameFloor) avatar.gameObject.SetActive(sameFloor);
            if (!sameFloor) return;
            var nm = playerName.Value;
            if (!nm.Equals(shownName)) { shownName = nm; profile.displayName = nm.ToString(); }   // ToString allocates: only on change
            profile.rarity = (Rarity)rarity.Value; profile.xp = xp.Value;
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
            smHead = Vector3.Lerp(smHead, headPos.Value, k); smL = Vector3.Lerp(smL, leftPos.Value, k); smR = Vector3.Lerp(smR, rightPos.Value, k);
            smRot = Quaternion.Slerp(smRot, headRot.Value, k);
            var fwd = smRot * Vector3.forward; fwd.y = 0; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            float headHeight = 1.62f;
            var feet = new Vector3(smHead.x, smHead.y - headHeight, smHead.z) - fwd.normalized * 0.08f;
            feet.y = Tower.Y(Tower.FloorAt(smHead.y - 1.0f));
            // not standing on a tower floor (raid vault high above the roof): keep the body under the head
            if (Mathf.Abs(feet.y - (smHead.y - headHeight)) > 2.5f) feet.y = smHead.y - headHeight;
            var t = avatar.transform;
            t.position = feet;
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(fwd.normalized), k);
            avatar.lookAt = smHead + smRot * Vector3.forward * 3f;
            avatar.Reach(false, smL, 1f); avatar.Reach(true, smR, 1f);
        }
    }
#else
    public class NetPlayer : MonoBehaviour { }
#endif
}
