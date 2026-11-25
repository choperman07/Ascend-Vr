using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>Imported asset references. Lives in Resources/ASCEND/AssetLibrary.asset; filled by the world builder.</summary>
    public class AssetLibrary : ScriptableObject
    {
        [Header("Characters (Casino FREE ready-made people)")]
        public GameObject android;      // gentleman in hat and jacket (guests, the NFT look)
        public GameObject guestF;       // croupier / cashier: dealers, hosts, bartenders, waiters
        public GameObject security;     // casino security and raid squad
        public GameObject highRoller;   // big guy with the gold chain
        public GameObject showgirl;     // top hat and velvet suit (guests, DJ)
        public GameObject serviceBot;   // unused with the Casino FREE pack (waiters are people)
        public List<GameObject> extraHumans = new List<GameObject>();   // the guest looks
        [Header("Casino FREE extras")]
        public GameObject confetti, confettiRain;
        public AudioClip winChime, winBonus;
        public Texture2D carpet, carpetNormal;
        public Material skybox;
        [Header("Animation")]
        public RuntimeAnimatorController humanoidController;
        public RuntimeAnimatorController robotController;
        [Header("Materials made by the builder")]
        public List<Material> androidSkins = new List<Material>();   // per rarity: [surface, joints] pairs
        public Material guestSkin, securitySkin, securityVisor;
        [Header("Props (FBX prefabs)")]
        public List<GameObject> props = new List<GameObject>();
        [Tooltip("Atlas texture of each prop (KayKit packs keep it next to the FBX), same order as props")]
        public List<Texture2D> propTextures = new List<Texture2D>();
        public Texture2D PropTexture(string n)
        {
            for (int i = 0; i < props.Count && i < propTextures.Count; i++) if (props[i] != null && props[i].name == n) return propTextures[i];
            return null;
        }

        static AssetLibrary _i; static bool looked;
        public static AssetLibrary I
        {
            get
            {
                if (_i == null && !looked) { looked = true; _i = Resources.Load<AssetLibrary>("ASCEND/AssetLibrary"); }
                return _i;
            }
        }
        public static void ClearCache() { _i = null; looked = false; }

        public bool HasHumanoids { get { return humanoidController != null && (android != null || guestF != null || security != null); } }

        public GameObject Prop(string n)
        {
            for (int i = 0; i < props.Count; i++) if (props[i] != null && props[i].name == n) return props[i];
            return null;
        }
    }
}
