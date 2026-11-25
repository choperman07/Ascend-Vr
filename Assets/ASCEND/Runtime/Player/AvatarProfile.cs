using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Who someone is: shown only when you point at them (never floating over everyone all the time).</summary>
    public class AvatarProfile : MonoBehaviour
    {
        public string displayName = "PLAYER";
        public Rarity rarity = Rarity.Common;
        public long xp = 5000;
        public string status = "ON THE FLOOR";
        public string squad = "";
        public bool online;
        public Tier TierNow { get { return Roster.TierOf(xp); } }
    }
}
