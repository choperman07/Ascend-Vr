using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Anything a blaster bolt can hit in the raid.</summary>
    public class RaidTarget : MonoBehaviour
    {
        public RaidPart part;
        public RaidArena arena;
        public int hp = 1;
    }
}
