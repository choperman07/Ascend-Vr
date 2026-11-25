using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;
#if ASCEND_NGO
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
#endif

namespace Ascend.EditorTools
{
    /// <summary>ASCEND → Build World (v2). Creates Assets/ASCEND/Scenes/ASCEND_World.unity: a 7-floor casino at the top of a
    /// skyscraper (city view all around), every game with its dealer, the DJ stage, bar, security, raid rooftop, the XR rig
    /// with finger-tracked hands, wrist teleport menu, main menu, online + voice chat, reflection probes and Quest settings.</summary>
    public static class AscendWorldBuilder
    {
        const string ScenePath = "Assets/ASCEND/Scenes/ASCEND_World.unity";
        const float R = Tower.FloorRadius, H = 7.6f;
        static readonly List<GameObject> statics = new List<GameObject>();

        [MenuItem("ASCEND/Build World", false, 0)]
        public static void BuildWorld()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            try
            {
                EditorUtility.DisplayProgressBar("ASCEND", "Preparing assets…", 0.05f);
                RepairProjectSettings(false);
                CleanGenerated();
                OptimizeAudio();
                CreateFonts();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Palette.ClearCache();
                Palette.Prewarm();
                EditorUtility.DisplayProgressBar("ASCEND", "Imported characters, animations and props…", 0.1f);
                AscendAssets.Setup();
                statics.Clear();

                EditorUtility.DisplayProgressBar("ASCEND", "Sky, city and tower…", 0.15f);
                SetupLighting();
                var world = new GameObject("ASCEND World").transform;
                var systems = new GameObject("ASCEND Systems");
                systems.AddComponent<GameState>();
                systems.AddComponent<Sfx>();
                systems.AddComponent<Juice>();
                var fm = systems.AddComponent<FloorManager>();
                systems.AddComponent<BotDirector>();
                systems.AddComponent<SecurityDirector>();
                systems.AddComponent<OnlineSession>();
                systems.AddComponent<Hud>();
                var gq = systems.AddComponent<GraphicsQuality>();
                gq.post = SetupPostProcessing(systems.transform);
                new GameObject("ASCEND Main Menu").AddComponent<MainMenu>();

                BuildCity(world);
                var floors = new Transform[Tower.Count];
                for (int f = 0; f < Tower.Count; f++)
                {
                    EditorUtility.DisplayProgressBar("ASCEND", "Floor " + (f + 1) + " · " + Tower.Floors[f].name, 0.2f + f * 0.08f);
                    floors[f] = BuildFloor(world, f);
                }
                fm.floors = floors;

                EditorUtility.DisplayProgressBar("ASCEND", "Player rig, hands and menus…", 0.8f);
                SetupRig();
                SetupEventSystem();
                SetupNetwork();
                foreach (var s in statics) MarkStatic(s);

                EditorUtility.DisplayProgressBar("ASCEND", "Baking reflections…", 0.88f);
                BakeProbes(floors);
                for (int f = 1; f < floors.Length; f++) floors[f].gameObject.SetActive(false);

                EnsureFolder("Assets/ASCEND/Scenes");
                EditorSceneManager.SaveScene(scene, ScenePath);
                AddToBuild(ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log("[ASCEND] World v2 built: " + ScenePath + " · press Play (desktop: WASD + right mouse to look, left click buttons) or build to Quest 2.");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        // ==================================================================== helpers
        static Vector3 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)); }
        static Quaternion Yaw(float deg) { return Quaternion.Euler(0, deg, 0); }

        static GameObject Static(string name, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            statics.Add(go);
            return go;
        }

        static void Spot(Transform floorRoot, int floor, string label, Vector3 localPos, float yaw, bool main = false)
        {
            var go = new GameObject("TP · " + label);
            go.transform.SetParent(floorRoot, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Yaw(yaw);
            var tp = go.AddComponent<TeleportPoint>(); tp.label = label; tp.floor = floor; tp.main = main;
        }

        static void Bot(Transform floorRoot, int floor, SpotKind kind, Vector3 localPos)
        {
            var go = new GameObject("BotSpot · " + kind);
            go.transform.SetParent(floorRoot, false);
            go.transform.localPosition = localPos;
            var b = go.AddComponent<BotSpot>(); b.kind = kind; b.floor = floor;
        }

        /// <summary>Places a game on the floor: facing outwards (player side towards the centre), adds its stand-here teleport
        /// spot, two bot spots and a nav obstacle.</summary>
        static T Game<T>(Transform floorRoot, int floor, float angle, float radius, string title, string sub, int accent,
                         Tier tier = Tier.Rookie, string voice = "dealerF", Rarity dealer = Rarity.Legendary, long min = 100, long max = 25000,
                         bool spot = true, bool rug = true) where T : TableGame
        {
            var go = new GameObject(title);
            go.transform.SetParent(floorRoot, false);
            go.transform.localPosition = Dir(angle) * radius;
            go.transform.localRotation = Yaw(angle);
            var g = go.AddComponent<T>();
            g.title = title; g.subtitle = sub; g.accentIndex = accent; g.requiredTier = tier; g.voice = voice; g.dealerRarity = dealer; g.minBet = min; g.maxBet = max;
            var nav = go.AddComponent<BoxCollider>(); nav.center = new Vector3(0, 0.4f, 0.35f); nav.size = new Vector3(2.0f, 0.8f, 1.5f);
            if (spot) Spot(floorRoot, floor, title, go.transform.localPosition + Yaw(angle) * new Vector3(0, 0, -1.05f), angle);
            Bot(floorRoot, floor, SpotKind.Watch, go.transform.localPosition + Yaw(angle) * new Vector3(1.9f, 0, -2.2f));
            if (rug) Rug(go.transform.localPosition + Yaw(angle) * new Vector3(0, 0, -0.1f), typeof(ArcadeStation).IsAssignableFrom(typeof(T)) ? new Vector2(3.4f, 3.0f) : new Vector2(4.6f, 4.0f), angle);
            return g;
        }

        static T Place<T>(Transform floorRoot, string name, float angle, float radius, float yawOffset = 0f) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(floorRoot, false);
            go.transform.localPosition = Dir(angle) * radius;
            go.transform.localRotation = Yaw(angle + yawOffset);
            return go.AddComponent<T>();
        }

        // ==================================================================== sky + city
        static void SetupLighting()
        {
            // Casino FREE look: the pack's bright cartoon sky, soft daylight and a clean, airy fill
            var sky = AssetDatabase.LoadAssetAtPath<Material>(AscendAssets.PackRoot + "/Skyboxes/Skybox_5.mat");
            RenderSettings.skybox = sky != null ? sky : Palette.Sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.80f, 0.84f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.64f, 0.62f, 0.70f);
            RenderSettings.ambientGroundColor = new Color(0.38f, 0.34f, 0.40f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.70f, 0.85f, 0.96f);
            RenderSettings.fogDensity = 0.0028f;
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
            var sun = new GameObject("Sunlight").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.Euler(42, -35, 0);
        }

        static void BuildCity(Transform world)
        {
            var root = Static("City (static)", world).transform;
            var rnd = new System.Random(4242);
            Func<double> r = rnd.NextDouble;
            const float street = -200f;
            // tower facade below the casino (outward faces only: invisible from inside, visible from the rooftop)
            var fk = new MeshKit();
            fk.Cylinder(new Vector3(0, (street + Tower.Y(Tower.Count - 1) - 0.4f) * 0.5f, 0), R + 0.25f, R + 0.25f, Tower.Y(Tower.Count - 1) - 0.4f - street, 48, Quaternion.identity, Color.white, 0.9f, false, null, -1f, null, new Rect(0, 0, 24, 30));
            fk.Spawn("TowerFacade", root, Palette.CityWindows);
            // buildings in three rings, chunked into meshes of ~90
            var k = new MeshKit(); int n = 0, chunk = 0;
            for (int i = 0; i < 330; i++)
            {
                float ring = i < 70 ? 50f : i < 180 ? 95f : 165f;
                float ang = (float)(r() * 360.0);
                float dist = ring + (float)(r() * 45.0);
                float w = 10f + (float)(r() * 22f), d = 10f + (float)(r() * 22f);
                float top = street + 40f + (float)(Math.Pow(r(), 0.7) * (i < 70 ? 215f : 190f));
                if (i % 23 == 0) top = 25f + (float)(r() * 30f);   // a few towers rise above the casino
                var c = Dir(ang) * dist;
                float hgt = top - street;
                float uvx = Mathf.Max(1, Mathf.Round(w / 8f)), uvy = Mathf.Max(1, Mathf.Round(hgt / 10f));
                k.Box(new Vector3(c.x, street + hgt * 0.5f, c.z), new Vector3(w, hgt, d), Yaw(ang), Color.white, 0.9f, new Rect(0, 0, 0.2f, 0.2f));
                if (r() < 0.35) k.Box(new Vector3(c.x, top + 2f, c.z), new Vector3(w * 0.6f, 4f, d * 0.6f), Yaw(ang), Palette.Hex("221A33"), 0.05f);
                if (r() < 0.25) k.Bar(new Vector3(c.x, top, c.z), new Vector3(c.x, top + 12f, c.z), 0.4f, Palette.Red, 1f);
                if (++n >= 90) { k.Spawn("Buildings" + chunk++, root, Palette.CityWindows); k = new MeshKit(); n = 0; }
            }
            if (n > 0) k.Spawn("Buildings" + chunk, root, Palette.CityWindows);
            // street grid glowing far below
            var g = new MeshKit();
            g.Plane(new Vector3(0, street, 0), new Vector2(900, 900), Quaternion.identity, Palette.Hex("07050C"), 0f);
            for (int i = -10; i <= 10; i++)
            {
                var col = i % 3 == 0 ? Palette.Gold : Palette.Coral;
                g.Bar(new Vector3(i * 42f, street + 0.2f, -440), new Vector3(i * 42f, street + 0.2f, 440), 1.2f, col, 0.8f);
                g.Bar(new Vector3(-440, street + 0.2f, i * 42f), new Vector3(440, street + 0.2f, i * 42f), 1.2f, col, 0.8f);
            }
            g.Spawn("Streets", root, Palette.Neon);
            // the moon, right behind the rooftop boss
            var moonPos = new Vector3(0, Tower.Y(Tower.Count - 1) + 70f, -220f);
            var mk = new MeshKit(); mk.Sphere(Vector3.zero, 16f, 24, Palette.Hex("F4EEDC"), 1f);
            var moon = mk.Spawn("Moon", world, moonPos, Quaternion.identity, Palette.Neon);
            Build.GlowSprite(moon.transform, Vector3.zero, 110f, Palette.Hex("B7A6FF"), 0.55f);
            moon.AddComponent<MoonHowl>();
        }

        // ==================================================================== one floor
        const float RIn = 8.6f, ROut = 16f, RMid = 12.2f;   // three rings of tables; the mid ring sits in the gaps (±18°, ±90°, ±162°)
        static MeshKit rugs;          // casino carpet under every table cluster (one mesh per floor)
        static MeshKit rugTrim;       // gold edging of the rugs

        static Transform BuildFloor(Transform world, int f)
        {
            var info = Tower.Floors[f];
            bool roof = f == Tower.Count - 1;
            var root = new GameObject("Floor " + (f + 1) + " · " + info.name).transform;
            root.SetParent(world, false);
            root.localPosition = new Vector3(0, Tower.Y(f), 0);
            BuildShell(root, f, roof, info.color);
            Spot(root, f, info.name, Tower.Arrival(f) - root.localPosition, Tower.ArrivalYaw, true);
            for (int i = 0; i < 10; i++) Bot(root, f, SpotKind.Wander, Dir(i * 36f + 18f) * (i % 2 == 0 ? 6.0f : 19.4f));
            for (int i = 0; i < 6; i++) Bot(root, f, SpotKind.Window, Dir(i * 60f + 30f) * 20.2f);
            rugs = new MeshKit(); rugTrim = new MeshKit();
            // gaming floors: wall-to-wall casino carpet between the core and the windows (gold trim on both edges)
            fullCarpet = f >= 1 && f <= 7;
            if (fullCarpet)
            {
                var ck = new MeshKit();
                ck.RingPlanar(new Vector3(0, 0.004f, 0), 6.3f, R - 1.5f, 96, 4, 1.6f, Color.white, 0f);
                ck.Spawn("Carpet", Static("Carpet (static)", root).transform, Palette.Carpet);
                var tk = new MeshKit();
                tk.Torus(new Vector3(0, 0.012f, 0), 6.3f, 0.04f, 96, 4, Quaternion.identity, Color.white, 0.05f);
                tk.Torus(new Vector3(0, 0.012f, 0), R - 1.5f, 0.04f, 96, 4, Quaternion.identity, Color.white, 0.05f);
                tk.Spawn("CarpetTrim", Static("CarpetTrim (static)", root).transform, Palette.Gold3);
            }
            switch (f)
            {
                case 0: Lobby(root); break;
                case 1: Classics(root); break;
                case 2: CardRoom(root); break;
                case 3: DiceAndWheels(root); break;
                case 4: FastRounds(root); break;
                case 5: LuckAndLoot(root); break;
                case 6: DerbyAndBattles(root); break;
                case 7: SkyLounge(root); break;
                case 8: Rooftop(root); break;
            }
            var deco = Static("Carpets (static)", root).transform;
            if (rugs.VertexCount > 0) rugs.Spawn("Rugs", deco, Palette.Carpet);
            if (rugTrim.VertexCount > 0) rugTrim.Spawn("RugTrim", deco, Palette.Gold3);
            rugs = null; rugTrim = null;
            return root;
        }

        /// <summary>A rug of casino carpet (tiling UVs in metres) with a gold edge, in floor-local space.</summary>
        static bool fullCarpet;

        static void Rug(Vector3 center, Vector2 size, float yaw)
        {
            if (rugs == null || fullCarpet) return;
            var rot = Yaw(yaw);
            rugs.Plane(center + Vector3.up * 0.006f, size, rot, Color.white, 0f, new Rect(0, 0, size.x / 1.6f, size.y / 1.6f));
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            var c = new[] { new Vector3(-hx, 0.01f, -hz), new Vector3(hx, 0.01f, -hz), new Vector3(hx, 0.01f, hz), new Vector3(-hx, 0.01f, hz) };
            for (int i = 0; i < 4; i++) rugTrim.Bar(center + rot * c[i], center + rot * c[(i + 1) % 4], 0.035f, Color.white, 0.05f);
        }

        static void BuildShell(Transform root, int f, bool roof, Color accent)
        {
            var shell = Static("Shell (static)", root).transform;
            // floor: marble disc (teleportable) + slab edge
            var fk = new MeshKit();
            fk.Disc(Vector3.zero, R, 72, Quaternion.identity, Color.white, 0f, new Rect(0, 0, 1, 1));
            var floor = fk.Spawn("FloorTop", shell, roof ? Palette.Concrete : Palette.Marble);
            floor.AddComponent<MeshCollider>().sharedMesh = floor.GetComponent<MeshFilter>().sharedMesh;
            MakeTeleportable(floor);
            var e = new MeshKit();
            e.Cylinder(new Vector3(0, -0.2f, 0), R, R, 0.4f, 72, Quaternion.identity, Palette.Hex("4B3A6B"), 0.02f, false);
            e.Torus(new Vector3(0, 0.02f, 0), R - 0.25f, 0.025f, 96, 4, Quaternion.identity, accent, 1f);
            if (!roof) for (int i = 0; i < 2; i++) e.Ring(new Vector3(0, 0.004f, 0), (i == 0 ? 4.4f : 12.2f), (i == 0 ? 4.46f : 12.26f), 96, Quaternion.identity, accent, 0.45f);
            // elevator core: lacquered deco panels with gold bands
            // 48 panels round the core, with a real doorway (6 panels open up to the lintel) into the lift cabin
            var core = new MeshKit();
            for (int i = 0; i < 48; i++)
            {
                float a = i * 7.5f + 3.75f;
                var d = Dir(a); float w = 2f * Mathf.PI * 3f / 48f + 0.01f;
                bool door = d.z < 0f && Mathf.Abs(d.x) * 3f < 1.0f;
                var uv = new Rect(i / 48f * 8f, 0, 8f / 48f, 3f);
                if (!door) core.Quad(d * 3f + Vector3.up * H * 0.5f, new Vector2(w, H), Quaternion.LookRotation(-d), Color.white, 0f, uv);
                else core.Quad(d * 3f + Vector3.up * (2.8f + (H - 2.8f) * 0.5f), new Vector2(w, H - 2.8f), Quaternion.LookRotation(-d), Color.white, 0f, new Rect(uv.x, 3f * 2.8f / H, uv.width, 3f * (H - 2.8f) / H));
            }
            core.Spawn("CorePanels", shell, Palette.DecoPanel);
            e.Torus(new Vector3(0, 3.1f, 0), 3.02f, 0.02f, 48, 4, Quaternion.identity, Palette.Gold, 0.8f);
            // the lift doors are animated by the Elevator component (Runtime/World/Elevator.cs)
            e.Bar(new Vector3(-1.16f, 0, -2.86f), new Vector3(-1.16f, 2.8f, -2.86f), 0.06f, Palette.Gold, 1f);
            e.Bar(new Vector3(1.16f, 0, -2.86f), new Vector3(1.16f, 2.8f, -2.86f), 0.06f, Palette.Gold, 1f);
            e.Bar(new Vector3(-1.16f, 2.8f, -2.86f), new Vector3(1.16f, 2.8f, -2.86f), 0.06f, Palette.Gold, 1f);
            var walls = Build.Empty("Walls (colliders)", shell, Vector3.zero).transform;
            if (!roof)
            {
                // ceiling with light coves
                e.Disc(new Vector3(0, H, 0), R, 72, Quaternion.Euler(180, 0, 0), Palette.Hex("EEE8F4"), 0.02f);
                for (int i = 0; i < 3; i++) e.Torus(new Vector3(0, H - 0.08f, 0), 7.5f + i * 6f, 0.05f, 96, 4, Quaternion.identity, i == 1 ? accent : Palette.Text, 0.9f);
                // glass line: mullions + handrail
                for (int i = 0; i < 48; i++)
                {
                    var p = Dir(i * 7.5f) * (R - 0.15f);
                    e.Box(new Vector3(p.x, H * 0.5f, p.z), new Vector3(0.08f, H, 0.14f), Yaw(i * 7.5f), Palette.Hex("ECE8F2"), 0.04f);
                    if (i % 6 == 0) e.Bar(new Vector3(p.x, 0.02f, p.z) * 0.995f, new Vector3(p.x, H, p.z) * 0.995f, 0.015f, accent, 0.9f);
                }
                e.Torus(new Vector3(0, 1.05f, 0), R - 0.35f, 0.03f, 96, 5, Quaternion.identity, Palette.Silver, 0.35f);
                Chandeliers(shell, accent);
                GlassWall(walls, R - 0.3f, 3.2f, 48);
            }
            else
            {
                // rooftop parapet (inner face, readable from inside) + cap + glass safety barrier
                var pk = new MeshKit();
                for (int i = 0; i < 96; i++)
                {
                    float a = i * 3.75f;
                    var p = Dir(a) * (R - 0.2f);
                    pk.Quad(new Vector3(p.x, 0.55f, p.z), new Vector2(2f * Mathf.PI * (R - 0.2f) / 96f + 0.02f, 1.1f), Quaternion.LookRotation(Dir(a)), Palette.Hex("7A6A9A"), 0.03f);
                }
                pk.Torus(new Vector3(0, 1.12f, 0), R - 0.1f, 0.12f, 96, 6, Quaternion.identity, Palette.Metal, 0.05f);
                pk.Torus(new Vector3(0, 1.2f, 0), R - 0.1f, 0.025f, 96, 4, Quaternion.identity, Palette.Red, 1f);
                pk.Torus(new Vector3(0, 2.35f, 0), R - 0.32f, 0.02f, 96, 4, Quaternion.identity, Palette.Teal, 1f);
                for (int i = 0; i < 48; i++) { var p = Dir(i * 7.5f) * (R - 0.32f); pk.Bar(new Vector3(p.x, 1.2f, p.z), new Vector3(p.x, 2.35f, p.z), 0.03f, Palette.Silver, 0.2f); }
                pk.Spawn("Parapet", shell, Palette.Glossy);
                var gl = new MeshKit();
                for (int i = 0; i < 48; i++)
                {
                    float a = i * 7.5f + 3.75f;
                    var p = Dir(a) * (R - 0.32f);
                    gl.Quad(new Vector3(p.x, 1.78f, p.z), new Vector2(2f * Mathf.PI * (R - 0.32f) / 48f, 1.1f), Quaternion.LookRotation(Dir(a)), Palette.A(Palette.Teal, 0.10f), 0.6f);
                }
                gl.Spawn("GlassBarrier", shell, Palette.FlatGlow);
                GlassWall(walls, R - 0.3f, 2.6f, 48);
                // core cap + antenna
                e.Disc(new Vector3(0, H, 0), 3f, 48, Quaternion.identity, Palette.Hex("7A6A9A"), 0.03f);
                e.Bar(new Vector3(0, H, 0), new Vector3(0, H + 9f, 0), 0.15f, Palette.Metal, 0.1f);
                for (int i = 0; i < 6; i++) e.Bar(new Vector3(0, H + 1 + i * 1.3f, 0), Dir(i * 60f) * 0.9f + new Vector3(0, H + i * 1.3f, 0), 0.04f, Palette.Metal, 0.1f);
                var beacon = Build.GlowSprite(root, new Vector3(0, H + 9.2f, 0), 2.5f, Palette.Red, 1f);
                beacon.gameObject.AddComponent<Blink>();
            }
            e.Spawn("Structure", shell, Palette.Glossy);
            // core walls: a ring of boxes with a gap for the lift doors (front, -Z), so you can walk into the cabin
            for (int i = 0; i < 40; i++)
            {
                float a = i * 9f + 4.5f;
                var p = Dir(a) * 2.85f;
                if (p.z < -2.0f && Mathf.Abs(p.x) < 1.15f) continue;
                var wc = Build.Empty("CoreWall" + i, walls, new Vector3(p.x, H * 0.5f, p.z), Yaw(a));
                Build.BoxCol(wc, Vector3.zero, new Vector3(0.75f, H, 0.3f));
            }
            if (!roof)
            {
                // Casino FREE columns hugging the core (scaled to the 7.6 m ceiling, slimmer) and its CASINO neon on the core
                foreach (var a in new[] { 45f, -45f, 110f, -110f })
                {
                    var col = Decor(root, "Column_17", Dir(a) * 3.75f, a, 1f, true);
                    if (col != null) col.transform.localScale = new Vector3(0.45f, H / 13f, 0.45f);
                }
                foreach (var a in new[] { 0f, 78f, -78f }) Decor(root, "Neon_Sign_01", Dir(a) * 3.06f + Vector3.up * 4.6f, a, 1.3f, false);
            }
            var lift = new GameObject("Elevator"); lift.transform.SetParent(root, false);
            lift.AddComponent<Elevator>().floor = f;
            var info = Tower.Floors[f];
            Txt.Glow("FloorSign", root, new Vector3(0, 3.55f, -3.1f), "FLOOR " + (f + 1) + " · " + info.name, 0.32f, info.color, 6f);
            Txt.Make("FloorSub", root, new Vector3(0, 3.18f, -3.1f), info.sub, 0.11f, Palette.Muted, Palette.Mono, TextAlignmentOptions.Center, 6f);
            var panel = new GameObject("Elevator Panel"); panel.transform.SetParent(root, false);
            panel.transform.localPosition = new Vector3(2.0f, 0, -2.42f); panel.transform.localRotation = Yaw(-39f);
            panel.AddComponent<ElevatorPanel>().floor = f;
            var panel2 = new GameObject("Elevator Panel L"); panel2.transform.SetParent(root, false);
            panel2.transform.localPosition = new Vector3(-2.0f, 0, -2.42f); panel2.transform.localRotation = Yaw(39f);
            panel2.AddComponent<ElevatorPanel>().floor = f;
        }

        /// <summary>Invisible wall of box colliders along the glass so nobody walks (or is pushed) out of the tower.</summary>
        static void GlassWall(Transform parent, float radius, float height, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float a = i * 360f / n;
                var go = Build.Empty("Glass" + i, parent, Dir(a) * (radius + 0.2f), Yaw(a));
                var bc = go.AddComponent<BoxCollider>();
                bc.size = new Vector3(2f * Mathf.PI * radius / n + 0.1f, height, 0.4f);
                bc.center = new Vector3(0, height * 0.5f, 0);
            }
        }

        /// <summary>Three crystal chandeliers: gold rings, hanging crystals and soft glow (cheap: one mesh + sprites).</summary>
        static void Chandeliers(Transform shell, Color accent)
        {
            var k = new MeshKit();
            foreach (var a in new[] { 0f, 120f, -120f })
            {
                var c = Dir(a) * 12.2f + new Vector3(0, H - 1.6f, 0);
                k.Bar(c + Vector3.up * 1.6f, c + Vector3.up * 0.55f, 0.03f, Palette.Gold, 0.4f);
                for (int r = 0; r < 3; r++)
                {
                    float rr = 0.55f + r * 0.38f, y = 0.5f - r * 0.22f;
                    k.Torus(c + Vector3.up * y, rr, 0.025f, 40, 4, Quaternion.identity, Palette.Gold, 0.8f);
                    int n = 10 + r * 6;
                    for (int i = 0; i < n; i++)
                    {
                        var p = c + Dir(i * 360f / n) * rr + Vector3.up * y;
                        k.Bar(p, p + Vector3.down * (0.35f + (i % 3) * 0.12f), 0.012f, Color.Lerp(Color.white, accent, 0.25f), 1.4f);
                        k.Sphere(p + Vector3.down * (0.4f + (i % 3) * 0.12f), 0.035f, 6, Color.white, 1.6f);
                    }
                }
                Build.GlowSprite(shell, c, 3.2f, Color.Lerp(Palette.Gold, accent, 0.3f), 0.35f);
            }
            k.Spawn("Chandeliers", shell, Palette.Neon);
        }

        // ==================================================================== decor helpers
        static GameObject Decor(Transform root, string prop, Vector3 pos, float yaw, float scale = 1f, bool col = true, Color? tint = null, float emit = 0f)
        {
            var go = Props.Place(prop, root, pos, yaw, scale, col, tint, emit);
            if (go != null) statics.Add(go);
            return go;
        }

        /// <summary>A lounge corner by the windows, all Casino FREE furniture: red quilted couch and armchairs, a low table
        /// with fruit (or gold bars in the VIP lounge), a potted palm and a pendant lamp. Guests sit on it.</summary>
        static void LoungeCorner(Transform root, int floor, float angle, float radius, bool vip)
        {
            var c = Dir(angle) * radius;
            float yaw = angle + 180f;   // facing the room
            var rot = Yaw(yaw);
            Rug(c + rot * new Vector3(0, 0, 0.3f), new Vector2(4.6f, 3.6f), yaw);
            var sofaGo = Decor(root, "Couch_05", c + rot * new Vector3(0, 0, 1.0f), yaw, vip ? 1.15f : 1.05f);
            for (int s = -1; s <= 1; s += 2)
            {
                var chairGo = Decor(root, "Armchair_02", c + rot * new Vector3(s * 1.6f, 0, -0.3f), yaw - s * 70f, 1.05f);
                var seat = Seat.Create("Lounge Seat " + s, root, c + rot * new Vector3(s * 1.6f, 0, -0.3f), yaw - s * 70f, Seat.Style.VipChair, 0.5f, false);
                seat.tableEdge = false;
                // fitted to the real chair: cushion height, backrest and front edge measured from its triangles
                if (chairGo != null) { seat.FitTo(chairGo, 0f); foreach (var r in seat.GetComponentsInChildren<Renderer>()) r.enabled = false; }
            }
            // one seat per place the sofa really has (2-3, from the width between its armrests), each fitted to the cushion
            var places = sofaGo != null ? Seat.SofaPlaces(sofaGo, yaw) : new[] { -0.55f, 0.55f };
            for (int i = 0; i < places.Length; i++)
            {
                var seat = Seat.Create("Sofa Seat " + i, root, c + rot * new Vector3(places[i], 0, 0.75f), yaw, Seat.Style.VipChair, 0.5f, false);
                seat.tableEdge = false;
                if (sofaGo != null && seat.FitTo(sofaGo, places[i])) foreach (var r in seat.GetComponentsInChildren<Renderer>()) r.enabled = false;
                else if (sofaGo != null) { UnityEngine.Object.DestroyImmediate(seat.gameObject); continue; }
                // the pick volume follows the cushion height
                var pick = seat.transform.Find("SitPick"); var bc = pick != null ? pick.GetComponent<BoxCollider>() : null;
                if (bc != null) { bc.size = new Vector3(0.55f, seat.height + 0.25f, 0.55f); bc.center = new Vector3(0, (seat.height + 0.25f) * 0.5f, 0.05f); }
            }
            var table = c + rot * new Vector3(0, 0, -0.45f);
            Decor(root, "Table_01", table, yaw, 0.6f);
            if (vip)
            {
                Decor(root, "Gold_Ingot_01", table + Vector3.up * 0.51f + rot * new Vector3(-0.07f, 0, 0), yaw + 90f, 0.8f, false);
                Decor(root, "Gold_Ingot_01", table + Vector3.up * 0.51f + rot * new Vector3(0.07f, 0, 0), yaw + 90f, 0.8f, false);
                Decor(root, "Gold_Ingot_02", table + Vector3.up * 0.6f, yaw + 90f, 0.8f, false);
            }
            else Decor(root, "Fruits_01", table + Vector3.up * 0.51f, yaw, 0.7f, false);
            Decor(root, "Flower_03", c + rot * new Vector3(2.4f, 0, 1.2f), yaw, 0.75f);
            Decor(root, "Flower_03", c + rot * new Vector3(-2.4f, 0, 1.2f), yaw + 140f, 0.65f);
            Decor(root, "Lamp_05", table + Vector3.up * H, yaw, 1f, false);
            Bot(root, floor, SpotKind.Window, c + rot * new Vector3(0, 0, -1.6f));
        }

        /// <summary>Velvet rope line from the Casino FREE pack: brass posts (Fence_07) and sagging red ropes (Fence_08)
        /// round a centre, with an opening of gapDeg facing gapDir (deg, floor space) or none when gapDeg is 0.</summary>
        static void RopeRing(Transform root, Vector3 centre, float radius, int posts, float gapDir, float gapDeg = 0f)
        {
            for (int i = 0; i < posts; i++)
            {
                float a0 = i * 360f / posts, a1 = (i + 1) * 360f / posts;
                var p0 = centre + Dir(a0) * radius; var p1 = centre + Dir(a1) * radius;
                Decor(root, "Fence_07", p0, a0, 1f, false);
                if (gapDeg > 0f && Mathf.Abs(Mathf.DeltaAngle((a0 + a1) * 0.5f, gapDir)) < gapDeg * 0.5f) continue;
                var d = p1 - p0;
                var rope = Decor(root, "Fence_08", (p0 + p1) * 0.5f, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg - 90f, 1f, false);
                if (rope != null) rope.transform.localScale = new Vector3(d.magnitude / 0.94f, 1f, 1f);
            }
        }

        /// <summary>The cashier: Casino FREE desk with monitor, keyboard, mouse, office chair and cash counter, two ATMs.</summary>
        static void Cashier(Transform root, float angle, float radius)
        {
            var c = Dir(angle) * radius;
            float yaw = angle + 180f;            // the desk's front faces the room
            var rot = Yaw(yaw);
            Decor(root, "Table_06", c, yaw, 1f);
            // local +Z points into the room (customers); the cashier works on the window side (-Z)
            Decor(root, "Monitor_02", c + rot * new Vector3(-0.5f, 0.8f, -0.2f), yaw + 180f, 1f, false);
            Decor(root, "Keyboard_01", c + rot * new Vector3(-0.5f, 0.8f, -0.45f), yaw + 180f, 1f, false);
            Decor(root, "Mouse_01", c + rot * new Vector3(-0.12f, 0.8f, -0.45f), yaw + 180f, 1f, false);
            Decor(root, "Cash_Machine_01", c + rot * new Vector3(0.65f, 0.8f, 0.15f), yaw, 1f, false);
            Decor(root, "Cash_10", c + rot * new Vector3(0.95f, 0.8f, -0.3f), yaw + 30f, 1f, false);
            Decor(root, "Book_01", c + rot * new Vector3(0.25f, 0.8f, -0.2f), yaw + 15f, 1f, false);
            Decor(root, "Chair_Office_01", c + rot * new Vector3(-0.4f, 0, -1.15f), yaw, 1f, false);
            Decor(root, "ATM_01", Dir(angle + 9f) * (radius + 1.9f), angle + 180f, 1f);
            Decor(root, "ATM_03", Dir(angle - 9f) * (radius + 1.9f), angle + 180f, 1f);
            Txt.Glow("CashierSign", root, c + Vector3.up * 2.6f, "CASHIER", 0.3f, Palette.Gold).transform.localRotation = Yaw(angle);
            Txt.Make("CashierSub", root, c + Vector3.up * 2.28f, "PLAY CHIPS ONLY · NO REAL MONEY", 0.09f, Palette.Muted, Palette.Mono).transform.localRotation = Yaw(angle);
        }

        /// <summary>A row of machines (slots, video poker, keno) following the ring.</summary>
        static List<T> Bank<T>(Transform root, int floor, float center, float radius, int count, float spacing, Func<int, string> title, string sub,
                               int accent, string voice, Rarity dealer, Action<T, int> setup = null, int hostEvery = 4) where T : TableGame
        {
            var list = new List<T>();
            float step = spacing / radius * Mathf.Rad2Deg;
            for (int i = 0; i < count; i++)
            {
                float a = center + (i - (count - 1) * 0.5f) * step;
                var g = Game<T>(root, floor, a, radius, title(i), sub, (accent + i) % 6, Tier.Rookie, voice, dealer, 100, 25000, i == count / 2, false);
                g.hasDealer = hostEvery > 0 && i % hostEvery == 0;
                if (setup != null) setup(g, i);
                list.Add(g);
            }
            float span = (count - 1) * spacing + 2.4f;
            Rug(Dir(center) * (radius - 0.6f), new Vector2(span, 2.6f), center);
            return list;
        }

        // ==================================================================== floor contents
        static void Lobby(Transform root)
        {
            Place<DJStage>(root, "DJ Stage", 180f, 16.5f);
            for (int i = 0; i < 16; i++) Bot(root, 0, SpotKind.Dance, Dir(180f) * 16.5f + Yaw(180f) * new Vector3(-4.2f + (i % 4) * 2.8f, 0, -3f - (i / 4) * 2f));
            Place<NeonBar>(root, "The High Bar", 90f, 15.5f);
            for (int i = 0; i < 5; i++) Bot(root, 0, SpotKind.Bar, Dir(90f) * 15.5f + Yaw(90f) * new Vector3(-2.2f + i * 1.1f, 0, -1.5f));
            Place<ChatWall>(root, "Chat Wall", -90f, 19.5f);
            Place<Leaderboard>(root, "Leaderboard", -55f, 19.5f);
            Place<NftGallery>(root, "NFT Gallery", -125f, 17f);
            Place<AvatarStudio>(root, "Avatar Studio", 58f, 17.2f);
            Place<WelcomeBoard>(root, "Welcome", 0f, 8.5f);
            // slot banks either side of the welcome board
            Bank<Slots>(root, 0, 32f, 12.6f, 4, 1.25f, i => Slots.TitleOf(i % 3), "LOBBY BANK", 0, "host", Rarity.Epic, (g, i) => g.theme = i % 3);
            Bank<Slots>(root, 0, -32f, 12.6f, 4, 1.25f, i => Slots.TitleOf((i + 2) % 3), "LOBBY BANK", 3, "host", Rarity.Rare, (g, i) => g.theme = (i + 2) % 3);
            Spot(root, 0, "SLOTS (LOBBY)", Dir(32f) * 10.9f, 32f);
            // THE VAULT: a safe, gold bars and cash stacks from the Casino FREE pack on a turntable behind a rope line
            // (display only: ASCEND has no real prizes and play chips have no cash value)
            var tt = Build.Empty("Turntable", root, Dir(0f) * 15.6f).transform;
            var tk = new MeshKit();
            tk.Cylinder(new Vector3(0, 0.08f, 0), 2.7f, 2.7f, 0.16f, 64, Quaternion.identity, Palette.Hex("5A4683"), 0.02f, true, Palette.Hex("6B5796"), 0.05f);
            tk.Torus(new Vector3(0, 0.16f, 0), 2.7f, 0.03f, 64, 4, Quaternion.identity, Palette.Gold, 1f);
            tk.Spawn("Platform", tt, Palette.Glossy, false);
            var plat = tt.gameObject.AddComponent<BoxCollider>(); plat.size = new Vector3(4.2f, 1.2f, 4.2f); plat.center = new Vector3(0, 0.6f, 0);
            var spinner = Build.Empty("Spinner", tt, new Vector3(0, 0.16f, 0)).transform;
            spinner.gameObject.AddComponent<Spin>().speed = new Vector3(0, 9f, 0);
            Props.Place("SafeBox_01", spinner, new Vector3(0, 0, 0.35f), 180f, 1.5f, false);
            for (int i = 0; i < 6; i++)
            {
                // a little pyramid of gold bars in front of the safe
                int row = i < 3 ? 0 : i < 5 ? 1 : 2; int col = i < 3 ? i : i < 5 ? i - 3 : 0; int inRow = 3 - row;
                Props.Place("Gold_Ingot_01", spinner, new Vector3((col - (inRow - 1) * 0.5f) * 0.3f, row * 0.16f, -0.75f), 90f, 1.5f, false);
            }
            for (int i = 0; i < 4; i++)
                Props.Place(i % 2 == 0 ? "Cash_10" : "Cash_07", spinner, Dir(-60f + i * 40f) * 1.6f + Vector3.back * 0.2f, i * 35f, 1.6f, false);
            Props.Place("Cash_11", spinner, new Vector3(0.9f, 0, 0.6f), 20f, 1.6f, false);
            Props.Place("Cash_11", spinner, new Vector3(-0.9f, 0, 0.6f), -20f, 1.6f, false);
            RopeRing(root, Dir(0f) * 15.6f, 3.25f, 24, 180f);
            Txt.Glow("CarSign", root, Dir(0f) * 17.9f + new Vector3(0, 2.6f, 0), "THE VAULT", 0.32f, Palette.Gold).transform.localRotation = Yaw(0f);
            Txt.Make("CarSub", root, Dir(0f) * 17.9f + new Vector3(0, 2.25f, 0), "DISPLAY ONLY · PLAY CHIPS HAVE NO CASH VALUE", 0.09f, Palette.Muted, Palette.Mono).transform.localRotation = Yaw(0f);
            Cashier(root, -20f, 17.4f);
            LoungeCorner(root, 0, 135f, 18.6f, false);
            LoungeCorner(root, 0, -155f, 18.6f, false);
            foreach (var a in new[] { 152f, -152f }) Place<GuardPost>(root, "Security Post", a, 6.4f, 0f);
            Place<GuardPost>(root, "Security Post Stage", 146f, 13.2f, 180f);
            Spot(root, 0, "DANCE FLOOR", Dir(180f) * 9.2f, 180f);
            Spot(root, 0, "THE HIGH BAR", Dir(90f) * 12.5f, 90f);
            Spot(root, 0, "THE VAULT", Dir(0f) * 11.8f, 0f);
            Spot(root, 0, "CASHIER", Dir(-20f) * 15.4f, -20f);
            Spot(root, 0, "CHAT WALL", Dir(-90f) * 16f, -90f);
            Spot(root, 0, "NFT GALLERY", Dir(-125f) * 14f, -125f);
            Spot(root, 0, "AVATAR STUDIO", Dir(58f) * 14.6f, 58f);
            Spot(root, 0, "LOUNGE", Dir(135f) * 15.2f, 135f);
        }

        static void Classics(Transform root)
        {
            Game<Roulette>(root, 1, 0f, RIn, "ROULETTE", "EUROPEAN · SINGLE ZERO", 0, Tier.Rookie, "dealerF", Rarity.Legendary);
            Game<Blackjack>(root, 1, 60f, RIn, "BLACKJACK", "6:5 · DEALER HITS SOFT 17", 1, Tier.Rookie, "dealerM", Rarity.Epic);
            Game<Blackjack>(root, 1, -60f, RIn, "BLACKJACK II", "6:5 · DEALER HITS SOFT 17", 4, Tier.Rookie, "dealerM", Rarity.Rare);
            Game<Baccarat>(root, 1, 120f, RIn, "BACCARAT", "PLAYER · BANKER · TIE", 2, Tier.Rookie, "dealerM", Rarity.Mythic);
            Game<Roulette>(root, 1, -120f, RIn, "ROULETTE II", "EUROPEAN · SINGLE ZERO", 5, Tier.Rookie, "dealerF", Rarity.Epic);
            Game<Roulette>(root, 1, 36f, ROut, "ROULETTE III", "EUROPEAN · SINGLE ZERO", 3, Tier.Rookie, "dealerF", Rarity.Rare, 50, 10000);
            Game<Blackjack>(root, 1, -36f, ROut, "BLACKJACK III", "6:5 · DEALER HITS SOFT 17", 0, Tier.Rookie, "dealerM", Rarity.Legendary, 50, 10000);
            Game<Baccarat>(root, 1, 108f, ROut, "BACCARAT · SQUEEZE", "PLAYER · BANKER · TIE", 1, Tier.Rookie, "dealerM", Rarity.Legendary);
            Game<Blackjack>(root, 1, -108f, ROut, "BLACKJACK IV", "6:5 · DEALER HITS SOFT 17", 2, Tier.Rookie, "dealerM", Rarity.Epic);
            Game<Roulette>(root, 1, 144f, ROut, "HIGH-LIMIT ROULETTE", "CONTENDER · HIGH STAKES", 3, Tier.Contender, "dealerF", Rarity.Mythic, 1000, 50000);
            Game<Blackjack>(root, 1, -144f, ROut, "HIGH-LIMIT 21", "CONTENDER · HIGH STAKES", 5, Tier.Contender, "dealerM", Rarity.Mythic, 1000, 50000);
            LoungeCorner(root, 1, 0f, 19.2f, false);
            Game<Roulette>(root, 1, 18f, RMid, "ROULETTE IV", "EUROPEAN · SINGLE ZERO", 1, Tier.Rookie, "dealerF", Rarity.Epic);
            Game<Blackjack>(root, 1, -18f, RMid, "BLACKJACK V", "6:5 · DEALER HITS SOFT 17", 3, Tier.Rookie, "dealerM", Rarity.Legendary);
            Game<Baccarat>(root, 1, 90f, RMid, "BACCARAT III", "PLAYER · BANKER · TIE", 5, Tier.Rookie, "dealerM", Rarity.Epic);
            Game<FortuneWheel>(root, 1, -90f, RMid, "WHEEL OF FORTUNE", "SPIN · UP TO 5x · FREE SPINS", 2, Tier.Rookie, "host", Rarity.Legendary);   // from Gamble With Your Friends (Roberto)
            Game<Blackjack>(root, 1, 162f, RMid, "BLACKJACK VI", "6:5 · DEALER HITS SOFT 17", 0, Tier.Rookie, "dealerM", Rarity.Rare);
            Game<DuckRace>(root, 1, -162f, RMid, "DUCK RACE", "PICK A DUCK · PAYS 4x", 4, Tier.Rookie, "host", Rarity.Legendary);   // from Gamble With Your Friends (Roberto)
        }

        static void CardRoom(Transform root)
        {
            Game<DragonTiger>(root, 2, 0f, RIn, "DRAGON TIGER", "ONE CARD EACH · HIGHER WINS", 0, Tier.Rookie, "dealerF", Rarity.Legendary);
            Game<CasinoHoldem>(root, 2, 60f, RIn, "CASINO HOLD'EM", "ANTE · CALL · BEAT THE DEALER", 3, Tier.Rookie, "dealerM", Rarity.Legendary);
            Game<CasinoHoldem>(root, 2, -60f, RIn, "CASINO HOLD'EM II", "ANTE · CALL · BEAT THE DEALER", 1, Tier.Rookie, "dealerM", Rarity.Epic);
            Game<ThreeCardPoker>(root, 2, 120f, RIn, "THREE CARD POKER", "ANTE · PLAY · PAIR PLUS", 2, Tier.Rookie, "dealerM", Rarity.Mythic);
            Game<ThreeCardPoker>(root, 2, -120f, RIn, "THREE CARD POKER II", "ANTE · PLAY · PAIR PLUS", 4, Tier.Rookie, "dealerM", Rarity.Rare);
            Game<DragonTiger>(root, 2, 144f, ROut, "DRAGON TIGER II", "ONE CARD EACH · HIGHER WINS", 5, Tier.Rookie, "dealerF", Rarity.Epic);
            Game<CasinoHoldem>(root, 2, -144f, ROut, "HOLD'EM · HIGH LIMIT", "CONTENDER · HIGH STAKES", 0, Tier.Contender, "dealerM", Rarity.Mythic, 1000, 50000);
            Bank<VideoPoker>(root, 2, 60f, ROut, 5, 1.3f, i => "VIDEO POKER " + (i + 1), "JACKS OR BETTER 9/6", 1, "host", Rarity.Epic, null, 0);
            Bank<VideoPoker>(root, 2, -60f, ROut, 5, 1.3f, i => "VIDEO POKER " + (i + 6), "JACKS OR BETTER 9/6", 3, "host", Rarity.Epic, null, 0);
            Spot(root, 2, "VIDEO POKER BAR", Dir(60f) * (ROut - 1.6f), 60f);
            LoungeCorner(root, 2, 0f, 19.2f, false);
            Game<DragonTiger>(root, 2, 18f, RMid, "DRAGON TIGER III", "ONE CARD EACH · HIGHER WINS", 2, Tier.Rookie, "dealerF", Rarity.Rare);
            Game<ThreeCardPoker>(root, 2, -18f, RMid, "THREE CARD POKER III", "ANTE · PLAY · PAIR PLUS", 5, Tier.Rookie, "dealerM", Rarity.Epic);
            Game<PokerTable>(root, 2, 90f, RMid, "TEXAS HOLD'EM", "VS 4 PLAYERS · BLINDS 100 / 200", 0, Tier.Rookie, "dealerM", Rarity.Rare);
            Game<PokerTable>(root, 2, -90f, RMid, "TEXAS HOLD'EM · HIGH STAKES", "VS 4 PLAYERS · BLINDS 1,000 / 2,000", 1, Tier.Rookie, "dealerM", Rarity.Epic).smallBlind = 1000;
            Game<Baccarat>(root, 2, 162f, RMid, "MINI BACCARAT", "PLAYER · BANKER · TIE", 3, Tier.Rookie, "dealerM", Rarity.Rare);
            Game<CrossTheRoad>(root, 2, -162f, RMid, "CROSS THE ROAD", "10 LANES · UP TO 1,000x", 4, Tier.Rookie, "host", Rarity.Legendary);   // from Gamble With Your Friends (Roberto)
        }

        static void DiceAndWheels(Transform root)
        {
            Game<MoneyWheel>(root, 3, 0f, RIn + 0.4f, "MONEY WHEEL", "BIG SIX · 54 SEGMENTS", 2, Tier.Rookie, "host", Rarity.Mythic);
            Game<SicBo>(root, 3, 60f, RIn, "SIC BO", "THREE DICE · BIG · SMALL · TRIPLES", 1, Tier.Rookie, "dealerF", Rarity.Legendary);
            Game<SicBo>(root, 3, -60f, RIn, "SIC BO II", "THREE DICE · BIG · SMALL · TRIPLES", 4, Tier.Rookie, "dealerF", Rarity.Epic);
            Game<Keno>(root, 3, 120f, RIn, "KENO", "PICK UP TO 10 · 20 BALLS", 3, Tier.Rookie, "host", Rarity.Rare);
            Game<Keno>(root, 3, -120f, RIn, "KENO II", "PICK UP TO 10 · 20 BALLS", 5, Tier.Rookie, "host", Rarity.Epic);
            Game<Craps>(root, 3, 36f, ROut, "CRAPS", "PASS · DON'T PASS · FIELD · BIG 6/8", 0, Tier.Rookie, "dealerM", Rarity.Legendary);
            Game<Craps>(root, 3, -36f, ROut, "CRAPS II", "PASS · DON'T PASS · FIELD · BIG 6/8", 2, Tier.Rookie, "dealerM", Rarity.Mythic);
            Game<MoneyWheel>(root, 3, 108f, ROut, "MONEY WHEEL II", "BIG SIX · 54 SEGMENTS", 1, Tier.Rookie, "host", Rarity.Legendary);
            Game<SicBo>(root, 3, -108f, ROut, "SIC BO · HIGH LIMIT", "CONTENDER · HIGH STAKES", 3, Tier.Contender, "dealerF", Rarity.Mythic, 1000, 50000);
            Game<Craps>(root, 3, 144f, ROut, "CRAPS · HIGH LIMIT", "CONTENDER · HIGH STAKES", 4, Tier.Contender, "dealerM", Rarity.Mythic, 1000, 50000);
            Game<Keno>(root, 3, -144f, ROut, "KENO LOUNGE", "PICK UP TO 10 · 20 BALLS", 0, Tier.Rookie, "host", Rarity.Legendary);
            Game<SicBo>(root, 3, 18f, RMid, "SIC BO III", "THREE DICE · BIG · SMALL · TRIPLES", 5, Tier.Rookie, "dealerF", Rarity.Rare);
            Game<Craps>(root, 3, -18f, RMid, "CRAPS III", "PASS · DON'T PASS · FIELD · BIG 6/8", 1, Tier.Rookie, "dealerM", Rarity.Epic);
            Game<Roulette>(root, 3, 90f, RMid, "WHEEL ROOM ROULETTE", "EUROPEAN · SINGLE ZERO", 4, Tier.Rookie, "dealerF", Rarity.Mythic);
            Game<MoneyWheel>(root, 3, -90f, RMid, "MONEY WHEEL III", "BIG SIX · 54 SEGMENTS", 0, Tier.Rookie, "host", Rarity.Epic);
            Game<Keno>(root, 3, 162f, RMid, "KENO III", "PICK UP TO 10 · 20 BALLS", 2, Tier.Rookie, "host", Rarity.Rare);
            Game<SicBo>(root, 3, -162f, RMid, "SIC BO IV", "THREE DICE · BIG · SMALL · TRIPLES", 3, Tier.Rookie, "dealerF", Rarity.Legendary);
        }

        static void FastRounds(Transform root)
        {
            Game<Crash>(root, 4, 0f, RIn + 0.5f, "CRASH", "CASH OUT BEFORE IT BLOWS", 4, Tier.Rookie, "host", Rarity.Legendary);
            Game<Mines>(root, 4, 60f, RIn, "MINES", "FIND THE GEMS", 1, Tier.Rookie, "host", Rarity.Epic);
            Game<Plinko>(root, 4, -60f, RIn, "PLINKO", "DROP THE BALL", 0, Tier.Rookie, "host", Rarity.Rare);
            Game<Dice>(root, 4, 120f, RIn, "DICE", "SHAKE THE CUP", 2, Tier.Rookie, "host", Rarity.Mythic);
            Game<Limbo>(root, 4, -120f, RIn, "LIMBO", "PICK A TARGET · UP TO 1000x", 5, Tier.Rookie, "host", Rarity.Legendary);
            Game<HiLo>(root, 4, 36f, ROut, "HI-LO", "HIGHER OR LOWER", 3, Tier.Rookie, "host", Rarity.Epic);
            Game<TowerClimb>(root, 4, -36f, ROut, "NEON TOWER", "CLIMB · AVOID THE TRAPS", 1, Tier.Rookie, "host", Rarity.Legendary);
            Game<Crash>(root, 4, 108f, ROut, "CRASH II", "CASH OUT BEFORE IT BLOWS", 0, Tier.Rookie, "host", Rarity.Epic);
            Game<Mines>(root, 4, -108f, ROut, "MINES II", "FIND THE GEMS", 5, Tier.Rookie, "host", Rarity.Legendary);
            Game<Plinko>(root, 4, 144f, ROut, "PLINKO II", "DROP THE BALL", 2, Tier.Rookie, "host", Rarity.Mythic);
            Game<Limbo>(root, 4, -144f, ROut, "LIMBO II", "PICK A TARGET · UP TO 1000x", 4, Tier.Rookie, "host", Rarity.Rare);
            Game<Dice>(root, 4, 72f, ROut, "DICE II", "SHAKE THE CUP", 0, Tier.Rookie, "host", Rarity.Epic);
            Game<HiLo>(root, 4, -72f, ROut, "HI-LO II", "HIGHER OR LOWER", 1, Tier.Rookie, "host", Rarity.Rare);
            Game<Crash>(root, 4, 18f, RMid, "CRASH III", "CASH OUT BEFORE IT BLOWS", 2, Tier.Rookie, "host", Rarity.Rare);
            Game<Mines>(root, 4, -18f, RMid, "MINES III", "FIND THE GEMS", 3, Tier.Rookie, "host", Rarity.Epic);
            Game<Plinko>(root, 4, 90f, RMid, "PLINKO III", "DROP THE BALL", 4, Tier.Rookie, "host", Rarity.Legendary);
            Game<Dice>(root, 4, -90f, RMid, "DICE III", "SHAKE THE CUP", 5, Tier.Rookie, "host", Rarity.Rare);
            Game<Limbo>(root, 4, 162f, RMid, "LIMBO III", "PICK A TARGET · UP TO 1000x", 0, Tier.Rookie, "host", Rarity.Epic);
            Game<TowerClimb>(root, 4, -162f, RMid, "NEON TOWER II", "CLIMB · AVOID THE TRAPS", 2, Tier.Rookie, "host", Rarity.Mythic);
        }

        static void LuckAndLoot(Transform root)
        {
            Game<MysteryBox>(root, 5, 0f, RIn, "MYSTERY BOX", "SHAKE IT OPEN", 3, Tier.Rookie, "host", Rarity.Legendary);
            Game<Coinflip>(root, 5, 60f, RIn, "COINFLIP", "HEADS OR TAILS", 1, Tier.Rookie, "host", Rarity.Epic);
            Game<Coinflip>(root, 5, -60f, RIn, "COINFLIP II", "HEADS OR TAILS", 4, Tier.Rookie, "host", Rarity.Rare);
            Game<Fishing>(root, 5, 120f, RIn + 0.3f, "CATCH A FISH", "NEON REEF", 0, Tier.Rookie, "host", Rarity.Legendary);
            Game<MysteryBox>(root, 5, -120f, RIn, "MYSTERY BOX II", "SHAKE IT OPEN", 2, Tier.Rookie, "host", Rarity.Mythic);
            string[] names = { Slots.TitleOf(0), Slots.TitleOf(1), Slots.TitleOf(2) };
            Bank<Slots>(root, 5, 36f, ROut, 5, 1.25f, i => names[i % 3] + " " + (i + 1), "PULL THE LEVER", 0, "host", Rarity.Epic, (g, i) => g.theme = i % 3);
            Bank<Slots>(root, 5, -36f, ROut, 5, 1.25f, i => names[(i + 1) % 3] + " " + (i + 6), "PULL THE LEVER", 2, "host", Rarity.Legendary, (g, i) => g.theme = (i + 1) % 3);
            Bank<Slots>(root, 5, 108f, ROut, 5, 1.25f, i => names[(i + 2) % 3] + " " + (i + 11), "PULL THE LEVER", 4, "host", Rarity.Mythic, (g, i) => g.theme = (i + 2) % 3);
            Bank<Slots>(root, 5, -108f, ROut, 5, 1.25f, i => names[i % 3] + " " + (i + 16), "PULL THE LEVER", 1, "host", Rarity.Rare, (g, i) => g.theme = i % 3);
            Spot(root, 5, "SLOT BANKS", Dir(36f) * (ROut - 2.2f), 36f);
            Game<Fishing>(root, 5, 144f, ROut, "CATCH A FISH II", "NEON REEF", 3, Tier.Rookie, "host", Rarity.Epic);
            Place<CrateVault>(root, "Crate Vault", -144f, ROut);
            Spot(root, 5, "CRATE VAULT", Dir(-144f) * (ROut - 1.3f), -144f);
            Game<Coinflip>(root, 5, 18f, RMid, "COINFLIP III", "HEADS OR TAILS", 2, Tier.Rookie, "host", Rarity.Legendary);
            Game<MysteryBox>(root, 5, -18f, RMid, "MYSTERY BOX III", "SHAKE IT OPEN", 5, Tier.Rookie, "host", Rarity.Rare);
            Bank<Slots>(root, 5, 90f, RMid, 3, 1.25f, i => names[i % 3] + " " + (i + 21), "PULL THE LEVER", 3, "host", Rarity.Epic, (g, i) => g.theme = i % 3);
            Bank<Slots>(root, 5, -90f, RMid, 3, 1.25f, i => names[(i + 2) % 3] + " " + (i + 24), "PULL THE LEVER", 5, "host", Rarity.Legendary, (g, i) => g.theme = (i + 2) % 3);
            Game<Fishing>(root, 5, 162f, RMid, "CATCH A FISH III", "NEON REEF", 1, Tier.Rookie, "host", Rarity.Rare);
            Game<Coinflip>(root, 5, -162f, RMid, "COINFLIP IV", "HEADS OR TAILS", 0, Tier.Rookie, "host", Rarity.Epic);
        }

        static void DerbyAndBattles(Transform root)
        {
            Game<HorseRace>(root, 6, 60f, RIn + 0.4f, "NEON DERBY", "6 CYBER-HORSES · LIVE PAYOUTS", 0, Tier.Contender, "host", Rarity.Mythic);
            Game<HorseRace>(root, 6, -60f, RIn + 0.4f, "NEON DERBY II", "6 CYBER-HORSES · LIVE PAYOUTS", 3, Tier.Contender, "host", Rarity.Legendary);
            Game<CaseBattle>(root, 6, 0f, RIn + 0.6f, "CASE BATTLE", "4 PLAYERS · WINNER TAKES ALL", 1, Tier.Contender, "host", Rarity.Mythic);
            Game<CaseBattle>(root, 6, 72f, ROut, "CASE BATTLE II", "4 PLAYERS · WINNER TAKES ALL", 3, Tier.Contender, "host", Rarity.Legendary);
            Game<CaseBattle>(root, 6, -72f, ROut, "CASE BATTLE III", "4 PLAYERS · WINNER TAKES ALL", 5, Tier.Contender, "host", Rarity.Epic);
            Game<HorseRace>(root, 6, 144f, ROut, "NEON DERBY III", "6 CYBER-HORSES · LIVE PAYOUTS", 2, Tier.Contender, "host", Rarity.Epic);
            Place<Leaderboard>(root, "Leaderboard", 0f, 19.5f);
            Place<CrateVault>(root, "Crate Vault", -144f, ROut);
            Spot(root, 6, "CRATE VAULT", Dir(-144f) * (ROut - 1.3f), -144f);
            LoungeCorner(root, 6, 120f, 19.2f, false);
            Game<CaseBattle>(root, 6, 18f, RMid, "CASE BATTLE IV", "4 PLAYERS · WINNER TAKES ALL", 2, Tier.Contender, "host", Rarity.Rare);
            Game<Blackjack>(root, 6, -18f, RMid, "DERBY 21", "6:5 · DEALER HITS SOFT 17", 4, Tier.Contender, "dealerM", Rarity.Legendary);
            Game<Crash>(root, 6, 162f, RMid, "BATTLE CRASH", "CASH OUT BEFORE IT BLOWS", 5, Tier.Contender, "host", Rarity.Mythic);
            Game<Coinflip>(root, 6, -162f, RMid, "BATTLE COINFLIP", "HEADS OR TAILS", 3, Tier.Contender, "host", Rarity.Legendary);
        }

        static void SkyLounge(Transform root)
        {
            Game<Blackjack>(root, 7, 0f, RIn, "HIGH-ROLLER 21", "LEGEND TABLE · HIGH STAKES", 2, Tier.Legend, "dealerM", Rarity.Mythic, 5000, 100000);
            Game<Roulette>(root, 7, 60f, RIn, "VIP ROULETTE", "LEGEND TABLE · HIGH STAKES", 3, Tier.Legend, "dealerF", Rarity.Mythic, 1000, 50000);
            Game<Baccarat>(root, 7, -60f, RIn, "VIP BACCARAT", "LEGEND TABLE · HIGH STAKES", 1, Tier.Legend, "dealerM", Rarity.Mythic, 1000, 50000);
            Game<Craps>(root, 7, 120f, RIn, "VIP CRAPS", "LEGEND TABLE · HIGH STAKES", 0, Tier.Legend, "dealerM", Rarity.Mythic, 1000, 50000);
            Game<ThreeCardPoker>(root, 7, -120f, RIn, "VIP THREE CARD", "LEGEND TABLE · HIGH STAKES", 5, Tier.Legend, "dealerM", Rarity.Mythic, 1000, 50000);
            Place<VipHost>(root, "VIP Host", 165f, 6.2f, 180f);
            Place<VoteBoard>(root, "Inner Circle Vote", -90f, 18.8f);
            Spot(root, 7, "THE INNER CIRCLE", Dir(-90f) * 15.5f, -90f);
            Place<GuardPost>(root, "Inner Circle Guard", -70f, 15f, 180f);
            Place<NeonBar>(root, "Sky Bar", 90f, 15.5f);
            Spot(root, 7, "SKY BAR", Dir(90f) * 12.5f, 90f);
            LoungeCorner(root, 7, 30f, 18.8f, true);
            LoungeCorner(root, 7, -30f, 18.8f, true);
            LoungeCorner(root, 7, 150f, 18.8f, true);
            LoungeCorner(root, 7, -150f, 18.8f, true);
            var gc = new GameObject("Golden Chip (secret)"); gc.transform.SetParent(root, false);
            gc.transform.localPosition = Dir(-30f) * 18.8f + Yaw(150f) * new Vector3(0, 0.62f, -0.55f);
            gc.AddComponent<GoldenChip>();
            Game<Blackjack>(root, 7, 18f, RMid, "HIGH-ROLLER 21 II", "LEGEND TABLE · HIGH STAKES", 4, Tier.Legend, "dealerF", Rarity.Mythic);
            Game<Roulette>(root, 7, -18f, RMid, "VIP ROULETTE II", "LEGEND TABLE · SINGLE ZERO", 1, Tier.Legend, "dealerF", Rarity.Mythic);
            Game<Baccarat>(root, 7, 162f, RMid, "VIP BACCARAT II", "LEGEND TABLE · HIGH STAKES", 3, Tier.Legend, "dealerM", Rarity.Mythic);
            Game<SicBo>(root, 7, -162f, RMid, "VIP SIC BO", "LEGEND TABLE · HIGH STAKES", 2, Tier.Legend, "dealerF", Rarity.Mythic);
        }

        static void Rooftop(Transform root)
        {
            var arena = Place<RaidArena>(root, "Raid Arena", 180f, 8f);
            arena.bossLocal = new Vector3(0, 6.5f, 16.5f);
            Spot(root, 8, "RAID CONSOLE", Dir(180f) * 6.3f, 180f);
            Spot(root, 8, "HELIPAD", new Vector3(0, 0, 11f), 0f);
            for (int i = 0; i < 4; i++) Bot(root, 8, SpotKind.Watch, Dir(180f + (i - 1.5f) * 22f) * 13f);
            // helipad: painted H in the pack's gold
            {
                var hk = new MeshKit();
                hk.Ring(new Vector3(0, 0.006f, 11f), 3.6f, 3.8f, 64, Quaternion.identity, Palette.Gold, 1f);
                hk.Box(new Vector3(-0.9f, 0.006f, 11f), new Vector3(0.35f, 0.01f, 2.6f), Palette.Gold, 1f);
                hk.Box(new Vector3(0.9f, 0.006f, 11f), new Vector3(0.35f, 0.01f, 2.6f), Palette.Gold, 1f);
                hk.Box(new Vector3(0, 0.006f, 11f), new Vector3(1.8f, 0.01f, 0.35f), Palette.Gold, 1f);
                hk.Spawn("Helipad", Static("Helipad (static)", root).transform, Palette.Neon);
            }
            // cover for the fight: chunky cartoon cargo crates in the pack's colours (with colliders) between you and the boss
            Color[] crate = { Palette.Hex("D9404E"), Palette.Hex("2F86D8"), Palette.Hex("F2B64A"), Palette.Hex("2BB5A7"), Palette.Hex("8A5AD0"), Palette.Hex("F07A2B") };
            for (int i = 0; i < 6; i++)
            {
                float a = 180f + (i - 2.5f) * 26f;
                var p = Dir(a) * (i % 2 == 0 ? 12.5f : 15.2f);
                Crate(root, p, a + 90f, new Vector3(1.7f, 1.5f, 1.5f), crate[i]);
                if (i % 2 == 1) Crate(root, p + Vector3.up * 1.5f, a + 70f, new Vector3(1.3f, 1.1f, 1.2f), crate[(i + 2) % 6]);
            }
            // rooftop plant: air-conditioning units and a water tank
            foreach (var a in new[] { 60f, 100f, -60f, -100f }) AcUnit(root, Dir(a) * 17.2f, a + 180f);
            WaterTank(root, Dir(130f) * 18.4f);
            WaterTank(root, Dir(-130f) * 18.4f);
            // searchlights sweeping the sky
            for (int i = 0; i < 4; i++)
            {
                var piv = Build.Empty("Searchlight" + i, root, Dir(45f + i * 90f) * 19.2f + Vector3.up * 1.4f).transform;
                Build.Beam(piv, Vector3.zero, Quaternion.Euler(-60, 0, 0), 120f, 1.6f, i % 2 == 0 ? Palette.Teal : Palette.Magenta, 0.08f);
                piv.gameObject.AddComponent<Spin>().speed = new Vector3(0, i % 2 == 0 ? 14f : -11f, 0);
            }
            // giant ASCEND letters along the north edge, readable from the arena
            // (two one-sided copies back to back: it reads right from the arena and from the city, never mirrored)
            Txt.Glow("RoofLogo", root, Dir(0f) * 20.4f + Vector3.up * 3.4f, "A S C E N D", 2.2f, Palette.Violet, 30f).transform.localRotation = Yaw(0f);
            Txt.Glow("RoofLogoOut", root, Dir(0f) * 20.45f + Vector3.up * 3.4f, "A S C E N D", 2.2f, Palette.Violet, 30f).transform.localRotation = Yaw(180f);
            // concrete deck tiles with hazard edging around the arena
            var dk = new MeshKit();
            dk.Ring(new Vector3(0, 0.008f, 0), 6.6f, 6.9f, 96, Quaternion.identity, Color.white, 0f);
            dk.Spawn("ArenaHazard", Static("Deck (static)", root).transform, Palette.Hazard);
        }

        // ---- rooftop props, modelled here in the Casino FREE style (chunky, rounded-looking, flat bright colours)
        static void Crate(Transform root, Vector3 pos, float yaw, Vector3 size, Color col)
        {
            var k = new MeshKit();
            var light = Color.Lerp(col, Color.white, 0.35f);
            k.Box(new Vector3(0, size.y * 0.5f, 0), size - new Vector3(0.04f, 0.04f, 0.04f), col, 0f);
            float t = 0.09f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    k.Box(new Vector3(sx * (size.x - t) * 0.5f, size.y * 0.5f, sz * (size.z - t) * 0.5f), new Vector3(t, size.y, t), light, 0f);
            for (int sy = 0; sy <= 1; sy++)
            {
                float y = sy == 0 ? t * 0.5f : size.y - t * 0.5f;
                k.Box(new Vector3(0, y, (size.z - t) * 0.5f), new Vector3(size.x, t, t), light, 0f);
                k.Box(new Vector3(0, y, -(size.z - t) * 0.5f), new Vector3(size.x, t, t), light, 0f);
                k.Box(new Vector3((size.x - t) * 0.5f, y, 0), new Vector3(t, t, size.z), light, 0f);
                k.Box(new Vector3(-(size.x - t) * 0.5f, y, 0), new Vector3(t, t, size.z), light, 0f);
            }
            // diagonal brace on the long faces
            k.Box(new Vector3(0, size.y * 0.5f, size.z * 0.5f), new Vector3(Mathf.Sqrt(size.x * size.x + size.y * size.y) - 0.2f, t * 0.8f, 0.04f), Quaternion.Euler(0, 0, Mathf.Atan2(size.y, size.x) * Mathf.Rad2Deg), light, 0f);
            k.Box(new Vector3(0, size.y * 0.5f, -size.z * 0.5f), new Vector3(Mathf.Sqrt(size.x * size.x + size.y * size.y) - 0.2f, t * 0.8f, 0.04f), Quaternion.Euler(0, 0, -Mathf.Atan2(size.y, size.x) * Mathf.Rad2Deg), light, 0f);
            var go = k.Spawn("Crate", Static("Crate (static)", root).transform, pos, Yaw(yaw), Palette.Neon);
            Build.BoxCol(go, new Vector3(0, size.y * 0.5f, 0), size);
        }

        static void AcUnit(Transform root, Vector3 pos, float yaw)
        {
            var k = new MeshKit();
            k.Box(new Vector3(0, 0.6f, 0), new Vector3(2.0f, 1.2f, 1.3f), Palette.Hex("D9DCE4"), 0f);
            k.Box(new Vector3(0, 0.04f, 0), new Vector3(2.1f, 0.08f, 1.4f), Palette.Hex("8E8A99"), 0f);
            for (int i = -1; i <= 1; i += 2)
            {
                k.Cylinder(new Vector3(i * 0.5f, 1.22f, 0), 0.42f, 0.42f, 0.05f, 24, Quaternion.identity, Palette.Hex("3A3D48"), 0f);
                k.Torus(new Vector3(i * 0.5f, 1.25f, 0), 0.42f, 0.03f, 24, 4, Quaternion.identity, Palette.Hex("F2F3F7"), 0f);
                k.Box(new Vector3(i * 0.5f, 1.25f, 0), new Vector3(0.8f, 0.02f, 0.06f), Palette.Hex("F2F3F7"), 0f);
            }
            for (int j = 0; j < 6; j++) k.Box(new Vector3(-0.8f + j * 0.32f, 0.6f, -0.66f), new Vector3(0.05f, 0.9f, 0.02f), Palette.Hex("A9ADB8"), 0f);
            var go = k.Spawn("AC", Static("AC (static)", root).transform, pos, Yaw(yaw), Palette.Neon);
            Build.BoxCol(go, new Vector3(0, 0.65f, 0), new Vector3(2.1f, 1.3f, 1.4f));
        }

        static void WaterTank(Transform root, Vector3 pos)
        {
            var k = new MeshKit();
            k.Cylinder(new Vector3(0, 2.6f, 0), 1.1f, 1.1f, 2.0f, 28, Quaternion.identity, Palette.Hex("2F86D8"), 0f);
            k.Cylinder(new Vector3(0, 3.75f, 0), 1.15f, 0.25f, 0.4f, 28, Quaternion.identity, Palette.Hex("D9404E"), 0f);
            for (int i = 0; i < 3; i++) k.Torus(new Vector3(0, 1.8f + i * 0.8f, 0), 1.12f, 0.035f, 28, 4, Quaternion.identity, Palette.Hex("F2F3F7"), 0f);
            for (int i = 0; i < 4; i++) { var d = Dir(45f + i * 90f) * 0.8f; k.Bar(d, d + Vector3.up * 1.6f, 0.07f, Palette.Hex("8E8A99"), 0f); }
            var go = k.Spawn("WaterTank", Static("Tank (static)", root).transform, pos, Quaternion.identity, Palette.Neon);
            Build.BoxCol(go, new Vector3(0, 2f, 0), new Vector3(2.2f, 4f, 2.2f));
        }

        // ==================================================================== rig, UI, network
        static void SetupRig()
        {
            GameObject rig = null;
            string best = null;
            foreach (var g in AssetDatabase.FindAssets("XR Origin t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (path.Contains("Simulator") || file.Contains("AR")) continue;
                if (file == "XR Origin (XR Rig)") { best = path; break; }
                if (best == null && file.StartsWith("XR Origin")) best = path;
            }
            if (best != null)
            {
                rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(best));
                Debug.Log("[ASCEND] Using XR rig: " + best);
            }
            if (rig == null)
            {
                Debug.LogWarning("[ASCEND] No 'XR Origin (XR Rig)' prefab found (XR Interaction Toolkit → Samples → Starter Assets). Desktop camera added instead.");
                rig = new GameObject("Desktop Rig");
                var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
                camGo.AddComponent<Camera>(); camGo.AddComponent<AudioListener>();
                camGo.transform.SetParent(rig.transform, false); camGo.transform.localPosition = new Vector3(0, 1.6f, 0);
            }
            rig.transform.position = Tower.Arrival(0);
            rig.transform.rotation = Yaw(Tower.ArrivalYaw);
            if (rig.GetComponent<CharacterController>() == null)
            {
                var cc = rig.AddComponent<CharacterController>();
                cc.height = 1.8f; cc.radius = 0.22f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.3f; cc.skinWidth = 0.03f;
            }
            rig.AddComponent<Rig>();
            rig.AddComponent<PointInspector>();
            rig.AddComponent<PlayerSeating>();
            if (rig.GetComponent<NpcChat>() == null) rig.AddComponent<NpcChat>();
            rig.AddComponent<PlayerBody>();
            rig.AddComponent<WristMenu>();
            if (rig.GetComponent<WristChat>() == null) rig.AddComponent<WristChat>();
            rig.AddComponent<DesktopTester>();
            foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.farClipPlane = 600f;
                cam.nearClipPlane = 0.02f;
            }
            // finger-tracked hands on both controllers (XR Hands sample meshes when available, neon hands otherwise)
            foreach (var side in new[] { Hand.Left, Hand.Right })
            {
                var ctrl = Rig.FindDeep(rig.transform, side == Hand.Left ? "Left Controller" : "Right Controller")
                        ?? Rig.FindDeep(rig.transform, side == Hand.Left ? "LeftHand Controller" : "RightHand Controller");
                if (ctrl == null) continue;
                // resting pose in front of the camera so the editor view is not filled by the hands (tracking overrides it in VR)
                var camT = rig.GetComponentInChildren<Camera>(true);
                if (camT != null && ctrl.parent == camT.transform.parent)
                    ctrl.localPosition = camT.transform.localPosition + new Vector3(side == Hand.Left ? -0.22f : 0.22f, -0.32f, 0.42f);
                var hgo = new GameObject("ASCEND Hand " + side);
                hgo.transform.SetParent(ctrl, false);
                var hr = hgo.AddComponent<HandRig>();
                hr.hand = side;
                var fbx = FindHandModel(side);
                if (fbx != null)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                    inst.transform.SetParent(hgo.transform, false);
                    foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        smr.sharedMaterial = Palette.HandSkin;
                        smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        smr.updateWhenOffscreen = true;
                    }
                    hr.model = inst.transform;
                }
            }
        }

        static GameObject FindHandModel(Hand side)
        {
            string want = side == Hand.Left ? "LeftHand" : "RightHand";
            foreach (var g in AssetDatabase.FindAssets(want + " t:Model"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == want) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            Debug.Log("[ASCEND] XR Hands sample hand mesh not found (Package Manager → XR Hands → Samples → HandVisualizer). Using neon hands.");
            return null;
        }

        static void SetupEventSystem()
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            var t = UIKit.FindType("UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule")
                    ?? UIKit.FindType("UnityEngine.InputSystem.UI.InputSystemUIInputModule");
            if (t != null) es.AddComponent(t); else es.AddComponent<StandaloneInputModule>();
        }

        static void SetupNetwork()
        {
#if ASCEND_NGO
            EnsureFolder("Assets/ASCEND/Prefabs");
            const string prefabPath = "Assets/ASCEND/Prefabs/NetPlayer.prefab";
            var tmp = new GameObject("NetPlayer");
            tmp.AddComponent<NetworkObject>();
            tmp.AddComponent<NetPlayer>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(tmp, prefabPath);
            UnityEngine.Object.DestroyImmediate(tmp);
            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var tr = nmGo.AddComponent<UnityTransport>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = tr;
            nm.NetworkConfig.PlayerPrefab = prefab;
            nm.NetworkConfig.ConnectionApproval = false;
#else
            Debug.Log("[ASCEND] Netcode for GameObjects not installed: the online floor is disabled (bots still populate the casino).");
#endif
        }

        static void MakeTeleportable(GameObject go)
        {
            var t = UIKit.FindType("UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea")
                    ?? UIKit.FindType("UnityEngine.XR.Interaction.Toolkit.TeleportationArea");
            if (t == null) return;
            var comp = go.AddComponent(t);
            try
            {
                var maskType = UIKit.FindType("UnityEngine.XR.Interaction.Toolkit.InteractionLayerMask");
                var prop = t.GetProperty("interactionLayers", BindingFlags.Public | BindingFlags.Instance);
                if (maskType == null || prop == null) return;
                var getMask = maskType.GetMethod("GetMask", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string[]) }, null);
                int bits = getMask != null ? (int)getMask.Invoke(null, new object[] { new[] { "Teleport" } }) : 0;
                if (bits == 0) return;
                var op = maskType.GetMethod("op_Implicit", new[] { typeof(int) });
                if (op != null) prop.SetValue(comp, op.Invoke(null, new object[] { bits }), null);
            }
            catch (Exception ex) { Debug.LogWarning("[ASCEND] Could not set teleport layer: " + ex.Message); }
        }

        static void BakeProbes(Transform[] floors)
        {
            EnsureFolder("Assets/ASCEND/Resources/ASCEND/Generated/Probes");
            for (int f = 0; f < floors.Length; f++)
            {
                var go = new GameObject("Reflections");
                go.transform.SetParent(floors[f], false);
                go.transform.localPosition = new Vector3(0, 2.2f, 0);
                var p = go.AddComponent<ReflectionProbe>();
                p.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
                p.size = new Vector3(R * 2f, 9f, R * 2f);
                p.boxProjection = true;
                p.resolution = 128;
                p.hdr = false;
                p.importance = 1;
                try { Lightmapping.BakeReflectionProbe(p, "Assets/ASCEND/Resources/ASCEND/Generated/Probes/floor" + f + ".exr"); }
                catch (Exception ex) { Debug.LogWarning("[ASCEND] Probe bake skipped: " + ex.Message); }
            }
        }

        // ==================================================================== assets
        static void CleanGenerated()
        {
            string dir = Palette.GenFolder + "/Meshes";
            if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
        }

        static void OptimizeAudio()
        {
            foreach (var g in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/ASCEND/Resources/ASCEND/Audio" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var imp = AssetImporter.GetAtPath(path) as AudioImporter;
                if (imp == null || imp.userData == "ascend2") continue;
                bool music = path.Contains("/music/");
                bool loop = path.Contains("_loop");
                var s = imp.defaultSampleSettings;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = music ? 0.55f : 0.7f;
                s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
                s.preloadAudioData = !music;
                imp.defaultSampleSettings = s;
                imp.forceToMono = !music;
                imp.loadInBackground = music;
                imp.userData = "ascend2";
                imp.SaveAndReimport();
            }
        }

        [MenuItem("ASCEND/Rebuild Fonts", false, 30)]
        public static void CreateFonts()
        {
            EnsureFolder("Assets/ASCEND/Resources/ASCEND/Fonts/TMP");
            MakeFont("BebasNeue-Regular.ttf", "Bebas SDF");
            MakeFont("SpaceMono-Bold.ttf", "SpaceMono SDF");
            MakeFont("Inter.ttf", "Inter SDF");
            MakeFont("LilitaOne-Regular.ttf", "Lilita SDF");
            UISkinPresets();
        }

        /// <summary>The UI skin's font materials (drop shadow, sticker logo) saved as assets so their shader variants
        /// ship in the Quest build, plus Inter as fallback for the arrows and symbols Lilita One does not have.</summary>
        static void UISkinPresets()
        {
            const string dir = "Assets/ASCEND/Resources/ASCEND/Fonts/TMP/";
            var lil = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(dir + "Lilita SDF.asset");
            var inter = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(dir + "Inter SDF.asset");
            if (lil == null) return;
            if (inter != null)
            {
                if (lil.fallbackFontAssetTable == null) lil.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
                if (!lil.fallbackFontAssetTable.Contains(inter)) { lil.fallbackFontAssetTable.Add(inter); EditorUtility.SetDirty(lil); }
            }
            if (AssetDatabase.LoadAssetAtPath<Material>(dir + "Lilita SDF Shadow.mat") == null)
            {
                var m = new Material(lil.material);
                m.EnableKeyword("UNDERLAY_ON");
                m.SetColor("_UnderlayColor", new Color(0, 0, 0, 0.55f));
                m.SetFloat("_UnderlayOffsetX", 0.35f); m.SetFloat("_UnderlayOffsetY", -0.55f);
                m.SetFloat("_UnderlayDilate", 0.25f); m.SetFloat("_UnderlaySoftness", 0.35f);
                AssetDatabase.CreateAsset(m, dir + "Lilita SDF Shadow.mat");
            }
            if (AssetDatabase.LoadAssetAtPath<Material>(dir + "Lilita SDF Sticker.mat") == null)
            {
                var m = new Material(lil.material);
                m.EnableKeyword("OUTLINE_ON"); m.EnableKeyword("UNDERLAY_ON");
                m.SetFloat("_OutlineWidth", 0.32f); m.SetColor("_OutlineColor", Color.white);
                m.SetFloat("_FaceDilate", 0.18f);
                m.SetColor("_UnderlayColor", new Color(0.05f, 0.02f, 0.1f, 0.9f));
                m.SetFloat("_UnderlayOffsetX", 0.6f); m.SetFloat("_UnderlayOffsetY", -0.9f);
                m.SetFloat("_UnderlayDilate", 0.6f); m.SetFloat("_UnderlaySoftness", 0.05f);
                AssetDatabase.CreateAsset(m, dir + "Lilita SDF Sticker.mat");
            }
            AssetDatabase.SaveAssets();
        }

        static void MakeFont(string ttf, string name)
        {
            string outPath = "Assets/ASCEND/Resources/ASCEND/Fonts/TMP/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath) != null) return;
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/ASCEND/Resources/ASCEND/Fonts/" + ttf);
            if (font == null) { Debug.LogWarning("[ASCEND] Font missing: " + ttf); return; }
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (fa == null) return;
            fa.name = name;
            AssetDatabase.CreateAsset(fa, outPath);
            if (fa.atlasTextures != null && fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null) { fa.atlasTextures[0].name = name + " Atlas"; AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa); }
            if (fa.material != null) { fa.material.name = name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
            fa.TryAddCharacters(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~·×÷–—áéíóúñÁÉÍÓÚÑ¿¡%€", out _);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
        }

        // ==================================================================== utilities
        static void MarkStatic(GameObject root)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr.GetComponent<Spin>() != null || tr.GetComponent<Blink>() != null || tr.GetComponent<Canvas>() != null || tr.GetComponent<Billboard>() != null) continue;
                GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        static void AddToBuild(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path == path);
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ==================================================================== post-processing
        /// <summary>Global volume: neutral tonemapping, bloom on the neon, a touch of contrast/saturation and vignette.
        /// GraphicsQuality turns it on for desktop/QUALITY and off on the Quest (frame budget).</summary>
        static UnityEngine.Rendering.Volume SetupPostProcessing(Transform parent)
        {
            const string path = "Assets/ASCEND/Resources/ASCEND/Generated/ASCEND_Post.asset";
            EnsureFolder("Assets/ASCEND/Resources/ASCEND/Generated");
            var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(path);
            if (profile != null) AssetDatabase.DeleteAsset(path);
            profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
            bloom.threshold.Override(0.85f); bloom.intensity.Override(0.75f); bloom.scatter.Override(0.62f); bloom.tint.Override(new Color(1f, 0.92f, 1f));
            var tm = profile.Add<UnityEngine.Rendering.Universal.Tonemapping>(true);
            tm.mode.Override(UnityEngine.Rendering.Universal.TonemappingMode.Neutral);
            var ca = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
            ca.contrast.Override(12f); ca.saturation.Override(14f); ca.postExposure.Override(0.15f);
            var vg = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
            vg.intensity.Override(0.22f); vg.smoothness.Override(0.5f); vg.color.Override(Palette.Hex("12081F"));
            var wb = profile.Add<UnityEngine.Rendering.Universal.WhiteBalance>(true);
            wb.temperature.Override(-6f); wb.tint.Override(8f);
            foreach (var c in profile.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, profile); }
            EditorUtility.SetDirty(profile);
            var go = new GameObject("Post Processing (global)");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<UnityEngine.Rendering.Volume>();
            v.isGlobal = true; v.priority = 1; v.sharedProfile = profile;
            return v;
        }

        // ==================================================================== Quest 2 project settings
        [MenuItem("ASCEND/Configure Project for Quest 2", false, 20)]
        public static void ConfigureQuest()
        {
            PlayerSettings.companyName = "UCJC Product Narrative";
            PlayerSettings.productName = "ASCEND";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.ucjc.ascend");
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.SetMobileMTRendering(UnityEditor.Build.NamedBuildTarget.Android, true);
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 4;
            Debug.Log("[ASCEND] Android/Quest settings applied (IL2CPP, ARM64, ASTC, Linear, single-pass instanced, internet + mic for voice).");
            EditorUtility.DisplayDialog("ASCEND", "Android settings applied.\n\n1. File → Build Profiles → Android → Switch Platform\n2. Project Settings → XR Plug-in Management → Android → OpenXR + 'Meta Quest Support'\n3. (Online + voice) Project Settings → Services → link a Unity Cloud project\n4. Build And Run with the Quest 2 connected.", "OK");
        }

        /// <summary>Some Asset Store "complete projects" (e.g. Low Poly Shooter Pack) overwrite ProjectSettings on import:
        /// no render pipeline (everything URP turns magenta), Gamma colour space, their app id and scripting defines.
        /// Puts ASCEND's settings back: URP on every quality level, URP global settings, Linear, ASCEND identity.</summary>
        [MenuItem("ASCEND/Repair Project Settings", false, 20)]
        public static void RepairProjectSettingsMenu() { RepairProjectSettings(true); }

        public static void RepairProjectSettings(bool dialog)
        {
            const string cfg = "Assets/Settings/Project Configuration/";
            var quality = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(cfg + "Quality URP Config.asset");
            var perf = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(cfg + "Performance URP Config.asset");
            if (quality == null)
                foreach (var g in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
                { quality = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(g)); if (quality != null) break; }
            if (perf == null) perf = quality;
            var log = new System.Text.StringBuilder();
            if (quality != null)
            {
                if (UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != quality) { UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = quality; log.Append("render pipeline → URP; "); }
                int cur = QualitySettings.GetQualityLevel();
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    var want = i < QualitySettings.names.Length / 2 ? perf : quality;
                    if (QualitySettings.renderPipeline != want) QualitySettings.renderPipeline = want;
                }
                QualitySettings.SetQualityLevel(cur, false);
                var global = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineGlobalSettings>(cfg + "UniversalRenderPipelineGlobalSettings.asset");
                if (global != null)
                {
                    try { UnityEditor.Rendering.EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<UnityEngine.Rendering.Universal.UniversalRenderPipeline>(global); }
                    catch (Exception e) { Debug.LogWarning("[ASCEND] URP global settings: " + e.Message); }
                }
            }
            else log.Append("NO URP ASSET FOUND; ");
            if (PlayerSettings.colorSpace != ColorSpace.Linear) { PlayerSettings.colorSpace = ColorSpace.Linear; log.Append("colour space → Linear; "); }
            PlayerSettings.companyName = "UCJC Product Narrative";
            PlayerSettings.productName = "ASCEND";
            foreach (var t in new[] { UnityEditor.Build.NamedBuildTarget.Standalone, UnityEditor.Build.NamedBuildTarget.Android })
            {
                if (PlayerSettings.GetApplicationIdentifier(t) != "com.ucjc.ascend") PlayerSettings.SetApplicationIdentifier(t, "com.ucjc.ascend");
                var defs = new List<string>(PlayerSettings.GetScriptingDefineSymbols(t).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                int n = defs.RemoveAll(d => d == "CROSS_PLATFORM_INPUT" || d == "MOBILE_INPUT" || d == "UNITY_POST_PROCESSING_STACK_V2" || d == "ODIN_INSPECTOR" || d == "ODIN_INSPECTOR_3");
                if (n > 0) { PlayerSettings.SetScriptingDefineSymbols(t, string.Join(";", defs)); log.Append("removed " + n + " foreign defines (" + t.TargetName + "); "); }
            }
            RepairInputAxes(log);
            AssetDatabase.SaveAssets();
            Debug.Log("[ASCEND] Project settings checked: " + (log.Length > 0 ? log.ToString() : "all fine"));
            if (dialog) EditorUtility.DisplayDialog("ASCEND", "Project settings repaired.\n\n" + (log.Length > 0 ? log.ToString() : "Nothing to fix."), "OK");
        }

        /// <summary>A pack overwrote InputManager.asset with an empty axis list, so the EventSystem's input module throws
        /// "Input Axis Horizontal is not setup" every frame. Puts back Unity's default axes that are missing.</summary>
        static void RepairInputAxes(System.Text.StringBuilder log)
        {
            var objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
            if (objs == null || objs.Length == 0) return;
            var so = new SerializedObject(objs[0]);
            var axes = so.FindProperty("m_Axes");
            if (axes == null) return;
            var have = new HashSet<string>();
            for (int i = 0; i < axes.arraySize; i++) have.Add(axes.GetArrayElementAtIndex(i).FindPropertyRelative("m_Name").stringValue);
            // name, negative, positive, altNeg, altPos, type (0 key, 1 mouse, 2 joystick), axis, gravity, dead, sensitivity, snap
            var defs = new object[][]
            {
                new object[] { "Horizontal", "left", "right", "a", "d", 0, 0, 3f, 0.001f, 3f, true },
                new object[] { "Vertical", "down", "up", "s", "w", 0, 0, 3f, 0.001f, 3f, true },
                new object[] { "Fire1", "", "left ctrl", "", "mouse 0", 0, 0, 1000f, 0.001f, 1000f, false },
                new object[] { "Fire2", "", "left alt", "", "mouse 1", 0, 0, 1000f, 0.001f, 1000f, false },
                new object[] { "Fire3", "", "left shift", "", "mouse 2", 0, 0, 1000f, 0.001f, 1000f, false },
                new object[] { "Jump", "", "space", "", "", 0, 0, 1000f, 0.001f, 1000f, false },
                new object[] { "Submit", "", "return", "", "joystick button 0", 0, 0, 1000f, 0.001f, 1000f, false },
                new object[] { "Cancel", "", "escape", "", "joystick button 1", 0, 0, 1000f, 0.001f, 1000f, false },
                new object[] { "Mouse X", "", "", "", "", 1, 0, 0f, 0f, 0.1f, false },
                new object[] { "Mouse Y", "", "", "", "", 1, 1, 0f, 0f, 0.1f, false },
                new object[] { "Mouse ScrollWheel", "", "", "", "", 1, 2, 0f, 0f, 0.1f, false },
            };
            int added = 0;
            foreach (var d in defs)
            {
                if (have.Contains((string)d[0])) continue;
                axes.arraySize++;
                var e = axes.GetArrayElementAtIndex(axes.arraySize - 1);
                e.FindPropertyRelative("m_Name").stringValue = (string)d[0];
                e.FindPropertyRelative("descriptiveName").stringValue = "";
                e.FindPropertyRelative("descriptiveNegativeName").stringValue = "";
                e.FindPropertyRelative("negativeButton").stringValue = (string)d[1];
                e.FindPropertyRelative("positiveButton").stringValue = (string)d[2];
                e.FindPropertyRelative("altNegativeButton").stringValue = (string)d[3];
                e.FindPropertyRelative("altPositiveButton").stringValue = (string)d[4];
                e.FindPropertyRelative("gravity").floatValue = (float)d[7];
                e.FindPropertyRelative("dead").floatValue = (float)d[8];
                e.FindPropertyRelative("sensitivity").floatValue = (float)d[9];
                e.FindPropertyRelative("snap").boolValue = (bool)d[10];
                e.FindPropertyRelative("invert").boolValue = false;
                e.FindPropertyRelative("type").intValue = (int)d[5];
                e.FindPropertyRelative("axis").intValue = (int)d[6];
                e.FindPropertyRelative("joyNum").intValue = 0;
                added++;
            }
            if (added > 0) { so.ApplyModifiedPropertiesWithoutUndo(); log.Append("restored " + added + " input axes; "); }
        }

        [MenuItem("ASCEND/Reset Saved Progress", false, 40)]
        public static void ResetPrefs()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            Debug.Log("[ASCEND] Saved progress cleared.");
        }
    }
}
