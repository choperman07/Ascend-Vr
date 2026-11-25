using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Ascend.EditorTools
{
    /// <summary>Import rules for everything under Assets/ASCEND/Imported (third-party CC0 / Mixamo / KayKit / Asset Store)
    /// and the generated PBR textures: humanoid rigs, in-place looping clips, normal maps, mobile texture sizes.</summary>
    public class AscendImportRules : AssetPostprocessor
    {
        const string Root = "Assets/ASCEND/Imported/";
        static bool In(string path, string sub) { return path.Replace('\\', '/').StartsWith(Root + sub); }

        void OnPreprocessTexture()
        {
            var ti = (TextureImporter)assetImporter;
            string p = assetPath.Replace('\\', '/');
            if (p.Contains("/ASCEND/Textures/PBR/"))
            {
                string n = Path.GetFileNameWithoutExtension(p);
                if (n.EndsWith("_normal")) ti.textureType = TextureImporterType.NormalMap;
                else if (n.EndsWith("_mask")) ti.sRGBTexture = false;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 4;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.maxTextureSize = n.StartsWith("felt") ? 512 : 1024;
                ti.textureCompression = TextureImporterCompression.Compressed;
            }
            else if (p.Contains("/ASCEND/Resources/ASCEND/Textures/"))
            {
                // printed game surfaces (felts, wheels, cards, chips, slot symbols): stay sharp at grazing angles and up close
                string n = Path.GetFileNameWithoutExtension(p);
                bool print = n.StartsWith("felt") || n.Contains("wheel") || n == "cards" || n == "chips" || n == "slot_symbols";
                ti.mipmapEnabled = true;
                ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
                ti.anisoLevel = print ? 16 : 4;
                ti.filterMode = FilterMode.Trilinear;
                ti.maxTextureSize = print ? 4096 : 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                if (print)
                {
                    var android = ti.GetPlatformTextureSettings("Android");
                    android.overridden = true; android.maxTextureSize = 4096;
                    android.format = TextureImporterFormat.ASTC_4x4;
                    ti.SetPlatformTextureSettings(android);
                }
            }
            else if (p.StartsWith(Root))
            {
                ti.maxTextureSize = 1024;
                ti.anisoLevel = 2;
                if (Path.GetFileNameWithoutExtension(p).EndsWith("_Normal")) ti.textureType = TextureImporterType.NormalMap;
            }
        }

        /// <summary>Music (the 2-hour background set) streams from disk instead of being decompressed into memory;
        /// short effects and voices load compressed and play instantly.</summary>
        void OnPreprocessAudio()
        {
            var ai = assetImporter as AudioImporter;
            if (ai == null || !assetPath.Contains("ASCEND/Audio/")) return;
            var st = ai.defaultSampleSettings;
            if (assetPath.Contains("/Audio/music/"))
            {
                st.loadType = AudioClipLoadType.Streaming; st.compressionFormat = AudioCompressionFormat.Vorbis; st.quality = 0.5f;
                ai.loadInBackground = true; st.preloadAudioData = false;
            }
            else
            {
                st.loadType = AudioClipLoadType.CompressedInMemory; st.compressionFormat = AudioCompressionFormat.Vorbis; st.quality = 0.6f;
            }
            ai.defaultSampleSettings = st;
        }

        void OnPreprocessModel()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(Root)) return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.isReadable = In(p, "Props/");   // props get convex mesh colliders built from their own meshes
            mi.meshCompression = ModelImporterMeshCompression.Medium;
            mi.importBlendShapes = false;
            mi.importCameras = false; mi.importLights = false;
            if (In(p, "Characters/") || In(p, "Animations/"))
            {
                bool robot = Path.GetFileNameWithoutExtension(p).Contains("ServiceBot");
                mi.animationType = robot ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = true;
                mi.animationCompression = ModelImporterAnimationCompression.Optimal;
                mi.resampleCurves = true;
                mi.optimizeGameObjects = false;     // IK + look-at need the bone transforms
                if (robot) mi.globalScale = 1f;
            }
            else if (In(p, "Props/"))
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.addCollider = false;
                mi.generateSecondaryUV = false;
            }
        }

        static readonly string[] LoopKeys = { "idle", "walk", "run", "samba", "dance", "sit_chair_idle", "shooting", "aiming", "spellcasting", "sneak", "lie_idle", "sit_floor_idle", "blocking", "sitting", "strafe" };

        void OnPreprocessAnimation()
        {
            string p = assetPath.Replace('\\', '/');
            if (!(In(p, "Characters/") || In(p, "Animations/"))) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            var keep = new List<ModelImporterClipAnimation>();
            foreach (var c in clips)
            {
                string n = Short(c.name);
                if (n.Contains("t-pose") || n.Contains("tpose")) continue;
                bool loop = LoopKeys.Any(k => n.Contains(k)) && !n.Contains("down") && !n.Contains("standup") && !n.Contains("_start") && !n.Contains("land");
                c.loopTime = loop;
                c.loopPose = loop;
                // in place: the NavMeshAgent / game code moves the character, the clip only animates the body
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                keep.Add(c);
            }
            mi.clipAnimations = keep.ToArray();
        }

        /// <summary>"Rig|Sit_Chair_Idle" → "sit_chair_idle", "Armature|idle_Armature" → "idle_armature".</summary>
        public static string Short(string clipName)
        {
            string n = clipName ?? "";
            if (n.Contains("mixamo.com")) return "mixamo.com";
            int i = n.LastIndexOf('|');
            if (i >= 0 && i < n.Length - 1) n = n.Substring(i + 1);
            return n.ToLowerInvariant();
        }
    }

    /// <summary>Builds the animator controller and the runtime asset library from the Casino FREE pack (ithappy):
    /// people, casino animations, props, slot cabinets, confetti, win chimes, carpet and sky.</summary>
    public static class AscendAssets
    {
        public const string Imported = "Assets/ASCEND/Imported";
        const string GenDir = "Assets/ASCEND/Imported/Generated";
        const string LibPath = "Assets/ASCEND/Resources/ASCEND/AssetLibrary.asset";

        // Casino FREE (ithappy) clips first; ASCEND's own dance clips; KayKit only for the raid actions the pack has no clip for
        static readonly St[] HumanStates =
        {
            // The Casino FREE pack ships its people without their animation takes (only the locomotion set in
            // AC_Animations has motion), so: locomotion + idles from AC_Animations, ASCEND's own dances, KayKit for the
            // raid and for sitting / cheering. Everything else (waving, talking, thinking...) is done with IK on top of
            // the idle (HumanoidBody), and a state that has no clip of its own is simply not created.
            new St("Idle", new[] { "idle" }),
            new St("Walk", new[] { "adult_walk", "ac_walk_f" }),
            new St("Run", new[] { "adult_run", "ac_run_f" }),
            new St("Sit", new[] { "adult_male_sit_01", "sit_chair_idle" }),
            new St("Dance", new[] { "adult_danceidle", "dance_hype" }),
            new St("DanceGriddy", new[] { "dance_griddy" }),
            new St("DanceFloss", new[] { "dance_floss" }),
            new St("DanceHype", new[] { "dance_hype" }),
            new St("DanceRunningMan", new[] { "dance_runningman" }),
            new St("DanceRobot", new[] { "dance_robot" }),
            new St("Deal", new[] { "adult_dealer_givecards", "idle" }),
            new St("Shoot", new[] { "1h_ranged_shooting", "2h_ranged_shooting", "1h_ranged_shoot" }),
            new St("Aim", new[] { "2h_ranged_aiming", "1h_ranged_aiming" }),
            new St("Guard", new[] { "adult_security_idle_1", "idle" }),
            new St("Hit", new[] { "hit_a", "hit_b" }),
            new St("Death", new[] { "death_a" }),
            new St("PickUp", new[] { "pickup", "use_item" }),
            new St("Throw", new[] { "throw" }),
            new St("Dodge", new[] { "dodge_left", "dodge_right" }),
            new St("SlotSit", new[] { "adult_slot_sitting", "sit_chair_idle" }),
            new St("Bartend", new[] { "adult_barmen_shake", "idle" }),
            new St("Tray", new[] { "adult_waiter_walktray", "ac_walk_f" }),
            new St("TrayIdle", new[] { "adult_waiter_idletray", "idle" }),
            // optional: only created when a clip for it exists
            new St("IdleLook", new[] { "idle_lookaround" }, true),
            new St("Cheer", new[] { "adult_gamestanding_win", "cheer" }, true),
            new St("Nod", new[] { "adult_talkgesturelisten" }, true),
            new St("Facepalm", new[] { "adult_gamestanding_lose" }, true),
            new St("Wave", new[] { "adult_wavehello" }, true),
            new St("Clap", new[] { "adult_clap" }, true),
            new St("Talk", new[] { "adult_talkgestureactive" }, true),
            new St("SitWin", new[] { "adult_gamesitting_win" }, true),
            new St("SitLose", new[] { "adult_gamesitting_lose" }, true),
            new St("SitFloor", new[] { "sit_floor_idle" }, true),
        };

        static readonly St[] RobotStates =
        {
            new St("Idle", new[] { "idle" }), new St("Walk", new[] { "walking" }), new St("Run", new[] { "running" }), new St("Sit", new[] { "sitting" }),
            new St("Dance", new[] { "dance" }), new St("Cheer", new[] { "thumbsup" }), new St("Nod", new[] { "yes" }), new St("Facepalm", new[] { "no" }),
            new St("Deal", new[] { "idle" }), new St("Shoot", new[] { "punch" }), new St("Aim", new[] { "idle" }), new St("Guard", new[] { "idle" }),
            new St("Hit", new[] { "no" }), new St("Death", new[] { "death" }), new St("Wave", new[] { "wave" }), new St("PickUp", new[] { "yes" }),
            new St("Throw", new[] { "punch" }), new St("Dodge", new[] { "jump" }), new St("Sad", new[] { "no" }),
        };

        class St { public string state; public string[] keys; public bool optional; public St(string s, string[] k, bool opt = false) { state = s; keys = k; optional = opt; } }

        public static bool HasImports { get { return AssetDatabase.IsValidFolder(Imported); } }

        /// <summary>Creates/refreshes controllers + library. Safe to call when nothing is imported (library is cleared).</summary>
        public static AssetLibrary Setup()
        {
            EnsureDir("Assets/ASCEND/Resources/ASCEND");
            var lib = AssetDatabase.LoadAssetAtPath<AssetLibrary>(LibPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<AssetLibrary>();
                AssetDatabase.CreateAsset(lib, LibPath);
            }
            // ---- characters: the Casino FREE ready-made people (humanoid, one shared avatar for the adult skeleton)
            lib.android = Pack("Prefabs/Ready_Characters/200.prefab");          // gentleman in hat and jacket (guests, DJ, NFT look)
            lib.guestF = Pack("Prefabs/Ready_Characters/31_Cashier.prefab");     // croupiers, hosts, bartenders, waiters
            lib.security = Pack("Prefabs/Ready_Characters/77_Security.prefab");  // security and the raid squad
            lib.highRoller = Pack("Prefabs/Ready_Characters/209.prefab");        // big guy with the gold chain
            lib.showgirl = Pack("Prefabs/Ready_Characters/3_Actor.prefab");      // top hat and velvet suit
            lib.serviceBot = null; lib.robotController = null;                   // waiters are people now (pack has no robots)
            lib.extraHumans.Clear();
            foreach (var h in new[] { lib.android, lib.highRoller, lib.showgirl }) if (h != null) lib.extraHumans.Add(h);
            lib.confetti = Pack("VFX/FX_Prefabs/Confetti/Confetti_blast_multicolor.prefab");
            lib.confettiRain = Pack("VFX/FX_Prefabs/Confetti/Confetti_directional_multicolor.prefab");
            lib.winChime = AssetDatabase.LoadAssetAtPath<AudioClip>(PackRoot + "/Sound/Callum Donaldson - Casino Tones - Ascending Win Chime.wav");
            lib.winBonus = AssetDatabase.LoadAssetAtPath<AudioClip>(PackRoot + "/Sound/Callum Donaldson - Casino Tones - Win Bonus Chimes.wav");
            lib.carpet = AssetDatabase.LoadAssetAtPath<Texture2D>(PackRoot + "/Textures/Carpet_4.png");
            lib.carpetNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(PackRoot + "/Textures/Carpet_Normal.png");
            lib.skybox = AssetDatabase.LoadAssetAtPath<Material>(PackRoot + "/Skyboxes/Skybox_5.mat");

            if (HasPack)
            {
                EnsureDir(GenDir);
                PackClipSettings();
                var humanClips = new List<AnimationClip>();
                foreach (var rel in new[] { "Meshes/Adult_Base.fbx", "Meshes/AC_Animations.fbx", "Meshes/Plus-Size_Base.fbx", "Meshes/Senior_Base.fbx" })
                    humanClips.AddRange(Clips(PackRoot + "/" + rel));
                // ASCEND's own hand-animated dances, then KayKit motion only for the raid actions the pack has no clip for
                humanClips.AddRange(Clips(Imported + "/Animations/Anim_Dances.fbx"));
                humanClips.AddRange(Clips(Imported + "/Animations/Anim_KayKit.fbx").Where(c => RaidOnly(AscendImportRules.Short(c.name))));
                try
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var rel in new[] { "Meshes/Adult_Base.fbx", "Meshes/AC_Animations.fbx", "Meshes/Plus-Size_Base.fbx", "Meshes/Senior_Base.fbx" })
                    {
                        var mi = AssetImporter.GetAtPath(PackRoot + "/" + rel) as ModelImporter;
                        var all = AssetDatabase.LoadAllAssetsAtPath(PackRoot + "/" + rel).OfType<AnimationClip>().ToList();
                        sb.AppendLine(rel + ": importer=" + (mi != null) + " importAnimation=" + (mi != null && mi.importAnimation) + " type=" + (mi != null ? mi.animationType.ToString() : "?") +
                                      " clipAnimations=" + (mi != null && mi.clipAnimations != null ? mi.clipAnimations.Length : -1) + " defaultClips=" + (mi != null && mi.defaultClipAnimations != null ? mi.defaultClipAnimations.Length : -1) +
                                      " importedTakes=" + (mi != null && mi.importedTakeInfos != null ? mi.importedTakeInfos.Length : -1) + " assets=" + all.Count);
                        foreach (var c in all.Take(12)) sb.AppendLine("    " + c.name + " human=" + c.humanMotion + " legacy=" + c.legacy + " len=" + c.length.ToString("0.00"));
                    }
                    sb.AppendLine("used: " + string.Join(", ", humanClips.Select(c => c.name)));
                    File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Recordings/Trailer/clips.txt")), sb.ToString());
                }
                catch (System.Exception e) { Debug.LogWarning(e); }
                Debug.Log("[ASCEND] Humanoid clips found: " + humanClips.Count + " (" + string.Join(", ", humanClips.Take(6).Select(c => c.name)) + " ...)");
                lib.humanoidController = humanClips.Count > 0 ? BuildController(GenDir + "/ASCEND_Humanoid.controller", HumanStates, humanClips, true) : null;
            }
            else
            {
                lib.humanoidController = null;
                Debug.LogWarning("[ASCEND] Casino FREE pack not found at " + PackRoot + " — import it from the Package Manager (My Assets).");
            }

            // the pack characters keep their own materials
            lib.androidSkins.Clear();
            lib.guestSkin = null; lib.securitySkin = null; lib.securityVisor = null;

            lib.props.Clear(); lib.propTextures.Clear();
            if (HasPack)
            {
                // props need readable meshes for their convex colliders and the runtime NavMesh bake
                foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { PackRoot + "/Meshes/Assets", PackRoot + "/Meshes/Frame" }))
                {
                    var imp = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(g)) as ModelImporter;
                    if (imp != null && !imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
                }
                foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { PackRoot + "/Prefabs/Assets" }))
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                    if (go != null && lib.Prop(go.name) == null) { lib.props.Add(go); lib.propTextures.Add(null); }
                }
                // slot cabinets as bare models (the pack prefabs carry the pack's own reels and demo scripts)
                foreach (var n in new[] { "Slot_Machine_01", "Slot_Machine_02", "Slot_Machine_Aquarium_01" })
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(PackRoot + "/Meshes/Assets/" + n + ".fbx");
                    if (go != null) { lib.props.Add(go); lib.propTextures.Add(null); }
                }
            }
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            AssetLibrary.ClearCache();
            Debug.Log("[ASCEND] Casino FREE library: people=" + lib.extraHumans.Count + " dealer=" + (lib.guestF != null) + " security=" + (lib.security != null) +
                      " humanoidCtrl=" + (lib.humanoidController != null) + " props=" + lib.props.Count + " confetti=" + (lib.confetti != null) + " sky=" + (lib.skybox != null));
            return lib;
        }

        public const string PackRoot = "Assets/ithappy/Casino_Free";
        public static bool HasPack { get { return AssetDatabase.IsValidFolder(PackRoot); } }
        static GameObject Pack(string rel) { return AssetDatabase.LoadAssetAtPath<GameObject>(PackRoot + "/" + rel); }

        static IEnumerable<AnimationClip> Clips(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__") && c.humanMotion);
        }

        static bool RaidOnly(string n)
        {
            foreach (var k in new[] { "1h_ranged", "2h_ranged", "hit_", "death_", "dodge_", "throw", "pickup", "use_item", "sit_chair_idle", "sit_floor_idle", "cheer" }) if (n.StartsWith(k)) return true;
            return false;
        }

        static readonly string[] PackLoops = { "idle", "walk", "run", "danceidle", "sitting", "_sit_0", "sitbench", "sitground", "sittable", "sitdrink",
                                               "computerwork", "talkgesture", "barmen", "cleaner", "phone", "thinking", "stroller", "givecards", "crouch_idle" };

        /// <summary>The pack ships every clip as play-once; ASCEND's states loop the idles, walks, sits and dances and play
        /// in place (the game moves the people). Edits the pack's import settings once and reimports.</summary>
        static void PackClipSettings()
        {
            foreach (var rel in new[] { "Meshes/Adult_Base.fbx", "Meshes/AC_Animations.fbx", "Meshes/Plus-Size_Base.fbx", "Meshes/Senior_Base.fbx" })
            {
                var mi = AssetImporter.GetAtPath(PackRoot + "/" + rel) as ModelImporter;
                if (mi == null) continue;
                // the pack's clips must actually be imported: with this off every state silently fell back to one dance clip
                if (!mi.importAnimation || mi.animationType != ModelImporterAnimationType.Human)
                {
                    mi.importAnimation = true; mi.animationType = ModelImporterAnimationType.Human;
                    mi.SaveAndReimport();
                    mi = AssetImporter.GetAtPath(PackRoot + "/" + rel) as ModelImporter;
                    if (mi == null) continue;
                    Debug.Log("[ASCEND] Re-enabled animation import on " + rel);
                }
                var clips = mi.clipAnimations;
                if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
                bool changed = false;
                foreach (var c in clips)
                {
                    string n = c.name.ToLowerInvariant();
                    bool loop = PackLoops.Any(k => n.Contains(k)) && !n.Contains("win") && !n.Contains("lose") && !n.Contains("jump");
                    if (c.loopTime != loop || c.loopPose != loop || !c.lockRootPositionXZ || !c.lockRootRotation || !c.lockRootHeightY)
                    {
                        c.loopTime = loop; c.loopPose = loop;
                        c.lockRootRotation = true; c.keepOriginalOrientation = true;
                        c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                        c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                        changed = true;
                    }
                }
                if (changed) { mi.clipAnimations = clips; mi.SaveAndReimport(); }
            }
        }

        static GameObject Model(string rel)
        {
            foreach (var ext in new[] { ".fbx", ".FBX", ".glb", ".prefab" })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(Imported + "/" + rel + ext);
                if (go != null) return go;
            }
            return null;
        }

        static Texture2D Tex(string rel)
        {
            foreach (var ext in new[] { ".png", ".jpg", ".tga" })
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(Imported + "/" + rel + ext);
                if (t != null) return t;
            }
            return null;
        }

        static void AddNormal(Material m, Texture2D n)
        {
            if (m == null || n == null) return;
            m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 0.8f); m.SetFloat("_UseNormal", 1f); m.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(m);
        }

        static Texture2D FirstTexture(string rel)
        {
            string path = Imported + "/" + rel + ".fbx";
            var tex = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>().FirstOrDefault();
            if (tex != null) return tex;
            // embedded media are extracted next to the model (Name.fbm/)
            string dir = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(path);
            foreach (var folder in new[] { dir + "/" + name + ".fbm", dir + "/Textures", dir })
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
                {
                    var tp = AssetDatabase.GUIDToAssetPath(g);
                    var fn = Path.GetFileNameWithoutExtension(tp).ToLowerInvariant();
                    if (folder == dir && !fn.Contains(name.Split('_').Last().ToLowerInvariant().Substring(0, 3))) continue;
                    if (fn.Contains("normal") || fn.Contains("_n") || fn.Contains("spec")) continue;
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
                }
            }
            return null;
        }

        static AnimationClip Find(List<AnimationClip> clips, string[] keys)
        {
            foreach (var k in keys)
            {
                foreach (var c in clips) { var s = AscendImportRules.Short(c.name); if (s == k) return c; }
                foreach (var c in clips) { var s = AscendImportRules.Short(c.name); if (s.StartsWith(k + "_") || s.StartsWith(k)) return c; }
            }
            return null;
        }

        static AnimatorController BuildController(string path, St[] states, List<AnimationClip> clips, bool ik)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            AnimationClip idle = Find(clips, states[0].keys) ?? clips[0];
            AnimatorState first = null;
            int i = 0;
            foreach (var sk in states)
            {
                string state = sk.state; string[] keys = sk.keys;
                var clip = Find(clips, keys);
                if (clip == null && sk.optional) continue;
                if (clip == null) clip = idle;
                var st = sm.AddState(state, new Vector3(250 + (i % 4) * 220, 60 + (i / 4) * 70, 0));
                st.motion = clip;
                st.writeDefaultValues = true;
                if (first == null) first = st;
                i++;
            }
            sm.defaultState = first;
            var layers = ctrl.layers;
            layers[0].iKPass = ik;
            ctrl.layers = layers;
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        static void EnsureDir(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
            var parent = Path.GetDirectoryName(dir).Replace('\\', '/');
            EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
        }
    }
}
