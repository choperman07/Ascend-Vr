using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>EL SÓTANO · the team's hall of fame. A brick basement under the lobby with a gold-framed portrait of
    /// everyone who made ASCEND, each with a spotlight and a name plate. The only way down is EL CAMELLO in the lobby
    /// lift (100 chips, see ElevatorDealer); the door at the end of the room takes you back up into that lift.
    /// Photos: drop them in Assets/ASCEND/Resources/ASCEND/Equipo (jpg/png, any size; portraits are cropped to fit).
    /// Names and roles (optional): Assets/ASCEND/Resources/ASCEND/Equipo/equipo.txt, one line per photo:
    ///     archivo | Nombre | Rol          (archivo = file name without extension)
    /// Without that file the name is taken from the file name ("ana_garcia" → "ANA GARCIA").</summary>
    public class CreditsBasement : MonoBehaviour
    {
        public static CreditsBasement I { get; private set; }
        public static readonly Vector3 Origin = new Vector3(0f, -14f, 0f);
        const float HalfW = 5f, HalfL = 11f, Height = 4.2f;

        Transform root;
        Vector3 exitDoor;
        float cooldown, showUntil;
        public bool PlayerInside { get; private set; }

        struct Member { public string file, name, role; public Texture2D photo; }

        void Awake() { I = this; }

        void Start()
        {
            root = new GameObject("EL SÓTANO · Hall of Fame").transform;
            root.position = Origin;
            BuildRoom();
            BuildPortraits(LoadTeam());
            // easter egg in the corner behind where you arrive: LA CAJA and its streamer
            var egg = new GameObject("Easter Egg · LA CAJA").transform; egg.SetParent(root, false);
            egg.localPosition = new Vector3(-2.7f, 0f, -HalfL + 0.02f);
            egg.gameObject.AddComponent<CaseEgg>();
            root.gameObject.SetActive(false);
        }

        static Material _mat;
        static Material Mat { get { if (_mat == null) _mat = Palette.Flat(new Color(0.82f, 0.8f, 0.84f), 0.05f, 0.08f, 0.3f); return _mat; } }

        static GameObject Solid(string name, Transform parent, Vector3 center, Vector3 size, Color col, float emit = 0f, bool collider = true)
        {
            var k = new MeshKit(); k.Box(Vector3.zero, size, col, emit);
            var go = k.Spawn(name, parent, center, Quaternion.identity, Mat, false);
            if (collider) Build.BoxCol(go, Vector3.zero, size);
            return go;
        }

        static Light Lamp(Transform parent, Vector3 local, Color c, float intensity, float range, LightType type = LightType.Point, Vector3? aim = null, float angle = 50f)
        {
            var g = new GameObject(type == LightType.Spot ? "Spot" : "Bulb"); g.transform.SetParent(parent, false); g.transform.localPosition = local;
            if (aim.HasValue) g.transform.rotation = Quaternion.LookRotation(aim.Value);
            var l = g.AddComponent<Light>(); l.type = type; l.color = c; l.intensity = intensity; l.range = range; l.spotAngle = angle; l.shadows = LightShadows.None;
            return l;
        }

        // ------------------------------------------------------------------ the room
        void BuildRoom()
        {
            var brickA = Palette.Hex("7A3B33"); var brickB = Palette.Hex("8E4A3E"); var mortar = Palette.Hex("3A2A2E");
            // floor: dark wooden planks + a long red runner down the middle
            Solid("Floor", root, new Vector3(0, -0.1f, 0), new Vector3(HalfW * 2, 0.2f, HalfL * 2), Palette.Hex("4A3226"));
            var fk = new MeshKit();
            for (float x = -HalfW + 0.15f; x < HalfW; x += 0.3f) fk.Box(new Vector3(x, 0.002f, 0), new Vector3(0.02f, 0.004f, HalfL * 2), Palette.Hex("2E1F18"), 0f);
            fk.Box(new Vector3(0, 0.012f, 0.4f), new Vector3(2.2f, 0.02f, HalfL * 2 - 2.4f), Palette.Hex("8C1C2B"), 0f);
            fk.Box(new Vector3(-1.08f, 0.014f, 0.4f), new Vector3(0.06f, 0.022f, HalfL * 2 - 2.4f), Palette.Gold, 0.2f);
            fk.Box(new Vector3(1.08f, 0.014f, 0.4f), new Vector3(0.06f, 0.022f, HalfL * 2 - 2.4f), Palette.Gold, 0.2f);
            fk.Spawn("Planks", root, Mat, false);
            // ceiling with pipes and hanging bulbs (a proper basement)
            Solid("Ceiling", root, new Vector3(0, Height + 0.1f, 0), new Vector3(HalfW * 2, 0.2f, HalfL * 2), Palette.Hex("2A2230"), 0f, false);
            var ck = new MeshKit();
            foreach (var x in new[] { -3.6f, -3.3f, 3.4f })
                ck.Cylinder(new Vector3(x, Height - 0.25f, 0), 0.07f, 0.07f, HalfL * 2, 10, Quaternion.Euler(90, 0, 0), Palette.Hex("8D8794"), 0f);
            ck.Cylinder(new Vector3(0, Height - 0.12f, 0), 0.12f, 0.12f, HalfL * 2, 12, Quaternion.Euler(90, 0, 0), Palette.Hex("B0603A"), 0f);
            for (float z = -8f; z <= 8f; z += 4f)
            {
                ck.Bar(new Vector3(0, Height, z), new Vector3(0, Height - 0.7f, z), 0.012f, Palette.Hex("222222"), 0f);
                ck.Sphere(new Vector3(0, Height - 0.76f, z), 0.08f, 8, Palette.Hex("FFE7A0"), 2.6f);
            }
            ck.Spawn("Pipes", root, Mat, false);
            for (float z = -8f; z <= 8f; z += 4f) Lamp(root, new Vector3(0, Height - 0.9f, z), Palette.Hex("FFD6A0"), 1.1f, 7f);

            // brick walls (each brick a box, staggered rows, two tones)
            var bk = new MeshKit();
            bk.Box(new Vector3(-HalfW - 0.06f, Height / 2, 0), new Vector3(0.1f, Height, HalfL * 2), mortar, 0f);
            bk.Box(new Vector3(HalfW + 0.06f, Height / 2, 0), new Vector3(0.1f, Height, HalfL * 2), mortar, 0f);
            bk.Box(new Vector3(0, Height / 2, HalfL + 0.06f), new Vector3(HalfW * 2, Height, 0.1f), mortar, 0f);
            bk.Box(new Vector3(0, Height / 2, -HalfL - 0.06f), new Vector3(HalfW * 2, Height, 0.1f), mortar, 0f);
            var rnd = new System.Random(7);
            const float bw = 0.5f, bh = 0.24f;
            int row = 0;
            for (float y = bh / 2; y < Height; y += bh + 0.02f, row++)
            {
                float off = (row % 2) * bw * 0.5f;
                for (float z = -HalfL + off; z < HalfL; z += bw + 0.02f)
                {
                    float len = Mathf.Min(bw, HalfL - z); if (len < 0.08f) continue;
                    var c = rnd.NextDouble() < 0.5 ? brickA : brickB; c *= 0.9f + (float)rnd.NextDouble() * 0.2f; c.a = 1;
                    bk.Box(new Vector3(-HalfW + 0.02f, y, z + len / 2), new Vector3(0.06f, bh, len), c, 0f);
                    bk.Box(new Vector3(HalfW - 0.02f, y, z + len / 2), new Vector3(0.06f, bh, len), c, 0f);
                }
                for (float x = -HalfW + off; x < HalfW; x += bw + 0.02f)
                {
                    float len = Mathf.Min(bw, HalfW - x); if (len < 0.08f) continue;
                    var c = rnd.NextDouble() < 0.5 ? brickA : brickB; c *= 0.9f + (float)rnd.NextDouble() * 0.2f; c.a = 1;
                    bk.Box(new Vector3(x + len / 2, y, HalfL - 0.02f), new Vector3(len, bh, 0.06f), c, 0f);
                    bk.Box(new Vector3(x + len / 2, y, -HalfL + 0.02f), new Vector3(len, bh, 0.06f), c, 0f);
                }
            }
            bk.Spawn("Bricks", root, Mat, false);
            foreach (var s in new[] { -1, 1 })
            {
                var g = new GameObject("WallCol"); g.transform.SetParent(root, false); g.transform.localPosition = new Vector3(s * (HalfW + 0.05f), Height / 2, 0);
                g.AddComponent<BoxCollider>().size = new Vector3(0.3f, Height, HalfL * 2);
                var h = new GameObject("WallCol"); h.transform.SetParent(root, false); h.transform.localPosition = new Vector3(0, Height / 2, s * (HalfL + 0.05f));
                h.AddComponent<BoxCollider>().size = new Vector3(HalfW * 2, Height, 0.3f);
            }

            // end wall: the title in neon
            var t1 = Txt.Glow("Title", root, new Vector3(0, 3.15f, HalfL - 0.12f), "LOS QUE HICIERON ASCEND", 0.42f, Palette.Gold, 9f);
            var t2 = Txt.Make("Sub", root, new Vector3(0, 2.6f, HalfL - 0.12f), "UCJC · INFORMATION TECHNOLOGY · 2026", 0.17f, Palette.Text, Palette.UI, TextAlignmentOptions.Center, 9f);
            var t3 = Txt.Make("Sub2", root, new Vector3(0, 2.3f, HalfL - 0.12f), "GRACIAS POR JUGAR · +18 · SOLO FICHAS DE JUEGO", 0.1f, Palette.Muted, Palette.Mono, TextAlignmentOptions.Center, 9f);
            Lamp(root, new Vector3(0, 3.8f, HalfL - 1.6f), Palette.Hex("FFE2A0"), 2.2f, 6f, LightType.Spot, new Vector3(0, -0.35f, 1f), 70f);

            // centre: a pedestal with the golden ASCEND chip, a sofa to sit and look, plants in the corners
            var pk = new MeshKit();
            pk.Cylinder(new Vector3(0, 0.5f, 7.3f), 0.45f, 0.38f, 1f, 24, Quaternion.identity, Palette.Hex("2A2236"), 0f);
            pk.Cylinder(new Vector3(0, 1.02f, 7.3f), 0.5f, 0.5f, 0.05f, 24, Quaternion.identity, Palette.Gold, 0.3f);
            pk.Spawn("Pedestal", root, Mat, false);
            Build.BoxCol(root.Find("Pedestal").gameObject, new Vector3(0, 0.5f, 7.3f), new Vector3(1f, 1f, 1f));
            var chip = new GameObject("GoldenChip").transform; chip.SetParent(root, false); chip.localPosition = new Vector3(0, 1.45f, 7.3f);
            var gk = new MeshKit();
            gk.Cylinder(Vector3.zero, 0.32f, 0.32f, 0.06f, 40, Quaternion.Euler(90, 0, 0), Palette.Gold, 0.6f);
            for (int i = 0; i < 8; i++) gk.Box(new Vector3(Mathf.Sin(i * Mathf.PI / 4) * 0.27f, Mathf.Cos(i * Mathf.PI / 4) * 0.27f, 0), new Vector3(0.08f, 0.05f, 0.07f), Quaternion.Euler(0, 0, -i * 45f), Palette.Hex("FFF1C2"), 1f);
            gk.Spawn("Chip", chip, Mat, false);
            var ct = Txt.Glow("ChipText", chip, new Vector3(0, 0, -0.035f), "A", 0.3f, Palette.Hex("5A3A10"));
            chip.gameObject.AddComponent<Spin>().speed = new Vector3(0, 40f, 0);
            Lamp(root, new Vector3(0, 3.2f, 7.3f), Palette.Hex("FFE7A0"), 3f, 4f, LightType.Spot, Vector3.down, 30f);
            if (Props.Has("Couch_05")) Props.Place("Couch_05", root, new Vector3(0, 0, 3.6f), 180f, 1.05f, true);
            foreach (var p in new[] { new Vector3(-4.2f, 0, 10.2f), new Vector3(4.2f, 0, 10.2f), new Vector3(-4.2f, 0, -10.2f), new Vector3(4.2f, 0, -10.2f) })
                if (Props.Has("Flower_03")) Props.Place("Flower_03", root, p, Random.Range(0f, 360f), 0.75f, true);

            // the way out (back to the lobby)
            exitDoor = root.TransformPoint(new Vector3(0, 0, -HalfL + 0.5f));
            Door(root, new Vector3(0, 0, -HalfL + 0.08f), 0f, "SALIDA · ASCENSOR");
            var hint = Txt.Make("Welcome", root, new Vector3(0, 2.95f, -HalfL + 0.14f), "EL SÓTANO", 0.32f, Palette.Gold, Palette.UI, TextAlignmentOptions.Center, 6f);
            hint.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        }

        /// <summary>A wooden door in a gold frame with a green EXIT-style sign above it (local -Z faces the viewer).</summary>
        static void Door(Transform parent, Vector3 local, float yaw, string label)
        {
            var d = new GameObject("Door").transform; d.SetParent(parent, false); d.localPosition = local; d.localRotation = Quaternion.Euler(0, yaw, 0);
            var k = new MeshKit();
            k.Box(new Vector3(0, 1.1f, 0.02f), new Vector3(1.1f, 2.2f, 0.06f), Palette.Hex("5A3A24"), 0f);
            for (int i = 0; i < 3; i++) k.Box(new Vector3(0, 0.45f + i * 0.65f, 0.05f), new Vector3(0.85f, 0.5f, 0.02f), Palette.Hex("6E4A30"), 0f);
            k.Box(new Vector3(-0.62f, 1.15f, 0.04f), new Vector3(0.12f, 2.3f, 0.1f), Palette.Gold, 0.25f);
            k.Box(new Vector3(0.62f, 1.15f, 0.04f), new Vector3(0.12f, 2.3f, 0.1f), Palette.Gold, 0.25f);
            k.Box(new Vector3(0, 2.32f, 0.04f), new Vector3(1.36f, 0.12f, 0.1f), Palette.Gold, 0.25f);
            k.Sphere(new Vector3(0.38f, 1.05f, 0.08f), 0.045f, 8, Palette.Gold, 0.5f);
            k.Box(new Vector3(0, 2.62f, 0.05f), new Vector3(1.4f, 0.32f, 0.04f), Palette.Hex("1B6B3A"), 0.9f);
            k.Spawn("DoorMesh", d, Mat, false);
            var t = Txt.Make("DoorSign", d, new Vector3(0, 2.62f, 0.02f), label, 0.13f, Color.white, Palette.UI, TextAlignmentOptions.Center, 1.35f);
            t.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        }

        // ------------------------------------------------------------------ the portraits
        static List<Member> LoadTeam()
        {
            var tex = Resources.LoadAll<Texture2D>("ASCEND/Equipo").OrderBy(t => t.name).ToList();
            var info = new Dictionary<string, string[]>();
            var txt = Resources.Load<TextAsset>("ASCEND/Equipo/equipo");
            if (txt != null)
                foreach (var line in txt.text.Split('\n'))
                {
                    var p = line.Split('|').Select(x => x.Trim()).ToArray();
                    if (p.Length >= 2 && p[0].Length > 0 && !p[0].StartsWith("#")) info[p[0].ToLowerInvariant()] = p;
                }
            var list = new List<Member>();
            foreach (var t in tex)
            {
                string[] p; info.TryGetValue(t.name.ToLowerInvariant(), out p);
                list.Add(new Member
                {
                    file = t.name, photo = t,
                    name = p != null && p.Length > 1 ? p[1] : t.name.Replace('_', ' ').Replace('-', ' ').ToUpperInvariant(),
                    role = p != null && p.Length > 2 ? p[2] : ""
                });
            }
            // people listed in equipo.txt whose photo hasn't arrived yet still get their frame
            foreach (var kv in info) if (!list.Any(m => m.file.ToLowerInvariant() == kv.Key))
                    list.Add(new Member { file = kv.Key, name = kv.Value[1], role = kv.Value.Length > 2 ? kv.Value[2] : "" });
            return list;
        }

        void BuildPortraits(List<Member> team)
        {
            // only the people who are actually here: no empty frames. Left wall takes the odd one out; each wall
            // spreads its portraits evenly along the gallery.
            int n = team.Count; if (n == 0) return;
            int left = Mathf.CeilToInt(n / 2f), right = n - left;
            float z0 = -5.6f, z1 = 7.6f;
            for (int i = 0; i < n; i++)
            {
                int side = i % 2 == 0 ? -1 : 1; int idx = i / 2; int cnt = side < 0 ? left : right;
                float z = Mathf.Lerp(z0, z1, (idx + 0.5f) / cnt);
                Portrait(new Vector3(side * (HalfW - 0.1f), 0, z), side, team[i], true);
            }
        }

        void Portrait(Vector3 wallPoint, int side, Member m, bool real)
        {
            // local frame: -Z faces into the room
            var t = new GameObject("Portrait · " + (real ? m.name : "libre")).transform;
            t.SetParent(root, false);
            t.localPosition = wallPoint;
            t.localRotation = Quaternion.LookRotation(new Vector3(side, 0, 0));
            t.localScale = Vector3.one * 1.22f;
            const float fw = 1.15f, fh = 1.5f, cy = 1.85f;
            var k = new MeshKit();
            // ornate-ish gold frame: outer bevel + inner lip
            k.Box(new Vector3(0, cy, 0), new Vector3(fw + 0.24f, fh + 0.24f, 0.07f), Palette.Hex("C9922E"), 0.15f);
            k.Box(new Vector3(0, cy, -0.04f), new Vector3(fw + 0.12f, fh + 0.12f, 0.04f), Palette.Gold, 0.3f);
            k.Box(new Vector3(0, cy, -0.05f), new Vector3(fw, fh, 0.04f), Palette.Hex("120E18"), 0f);
            for (int c = 0; c < 4; c++)
                k.Sphere(new Vector3((c % 2 == 0 ? -1 : 1) * (fw / 2 + 0.12f), cy + (c < 2 ? 1 : -1) * (fh / 2 + 0.12f), -0.05f), 0.06f, 8, Palette.Gold, 0.4f);
            // name plate
            k.Box(new Vector3(0, cy - fh / 2 - 0.42f, -0.02f), new Vector3(1.2f, 0.36f, 0.04f), Palette.Hex("2A2236"), 0f);
            k.Box(new Vector3(0, cy - fh / 2 - 0.42f, -0.01f), new Vector3(1.28f, 0.44f, 0.03f), Palette.Gold, 0.2f);
            // little brass picture lamp above
            k.Bar(new Vector3(0, cy + fh / 2 + 0.16f, 0), new Vector3(0, cy + fh / 2 + 0.3f, -0.22f), 0.02f, Palette.Gold, 0.2f);
            k.Cylinder(new Vector3(0, cy + fh / 2 + 0.3f, -0.26f), 0.05f, 0.05f, 0.5f, 10, Quaternion.Euler(0, 0, 90), Palette.Gold, 0.4f);
            k.Spawn("Frame", t, Mat, false);
            var lamp = Lamp(t, new Vector3(0, cy + fh / 2 + 0.25f, -0.55f), Palette.Hex("FFE2B0"), real ? 2.2f : 0.8f, 3.2f, LightType.Spot, Vector3.down, 70f);
            lamp.transform.localRotation = Quaternion.LookRotation(new Vector3(0, -0.9f, 0.55f));

            // the photo (cropped to the frame's portrait aspect) or a silhouette
            var canvas = new GameObject("Photo").transform; canvas.SetParent(t, false); canvas.localPosition = new Vector3(0, cy, -0.075f);
            var qk = new MeshKit(); qk.Quad(Vector3.zero, new Vector2(fw - 0.06f, fh - 0.06f), Quaternion.identity, Color.white, 0f);
            var mf = canvas.gameObject.AddComponent<MeshFilter>(); mf.sharedMesh = qk.ToMesh("photo_quad");
            var mr = canvas.gameObject.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (real && m.photo != null)
            {
                var mat = Palette.Character("photo_" + m.file, m.photo, Color.white, 0.5f, 0.15f, 0f);
                float want = (fw - 0.06f) / (fh - 0.06f), have = m.photo.width / (float)Mathf.Max(1, m.photo.height);
                var sc = have > want ? new Vector2(want / have, 1f) : new Vector2(1f, have / want);
                mat.SetTextureScale("_BaseMap", sc);
                mat.SetTextureOffset("_BaseMap", new Vector2((1f - sc.x) * 0.5f, (1f - sc.y) * (have > want ? 0.5f : 0.7f)));   // tall photos keep the top (faces)
                mr.sharedMaterial = mat;
            }
            else
            {
                mr.sharedMaterial = Palette.Character("photo_blank", null, Palette.Hex("2C2538"), 0.2f, 0.1f, 0f);
                var sk = new MeshKit();
                sk.Sphere(new Vector3(0, cy + 0.22f, -0.09f), new Vector3(0.2f, 0.24f, 0.02f), 16, Palette.Hex("4A4060"), 0f);
                sk.Sphere(new Vector3(0, cy - 0.38f, -0.09f), new Vector3(0.4f, 0.3f, 0.02f), 16, Palette.Hex("4A4060"), 0f);
                sk.Spawn("Silhouette", t, Mat, false);
                var q = Txt.Make("Soon", t, new Vector3(0, cy - 0.62f, -0.1f), "FOTO PRÓXIMAMENTE", 0.07f, Palette.Muted, Palette.UI, TextAlignmentOptions.Center, 1f);
                q.transform.localRotation = Quaternion.Euler(0, 180f, 0) * Quaternion.Euler(0, 180f, 0);
            }
            var nameT = Txt.Make("Name", t, new Vector3(0, cy - fh / 2 - 0.37f, -0.05f), real ? m.name.ToUpperInvariant() : "· · ·", 0.13f, Palette.Gold, Palette.UI, TextAlignmentOptions.Center, 1.15f);
            nameT.enableAutoSizing = true; nameT.fontSizeMin = 0.6f; nameT.fontSizeMax = 1.3f;
            if (real && !string.IsNullOrEmpty(m.role))
                Txt.Make("Role", t, new Vector3(0, cy - fh / 2 - 0.52f, -0.05f), m.role.ToUpperInvariant(), 0.065f, Palette.Text, Palette.Mono, TextAlignmentOptions.Center, 1.15f);
        }

        // ------------------------------------------------------------------ travel
        /// <summary>Trailer: keep the room switched on for a while without the player in it.</summary>
        public void Reveal(float seconds) { showUntil = Time.time + seconds; if (root != null) root.gameObject.SetActive(true); }

        /// <summary>Called by the lobby lift once EL CAMELLO has been paid and the cabin has "gone down".</summary>
        public void EnterFromLift()
        {
            if (!Rig.Ready) return;
            cooldown = Time.time + 2f; showUntil = Time.time + 3f;
            root.gameObject.SetActive(true);
            Rig.I.Teleport(root.TransformPoint(new Vector3(0, 0, -HalfL + 1.8f)), 0f, -1, "");
            if (GameState.I != null) GameState.I.Notify("PLANTA -1 · LOS QUE HICIERON ASCEND", Palette.Gold);
        }

        void Update()
        {
            if (!Rig.Ready || root == null) return;
            var feet = Rig.I.Feet;
            PlayerInside = Mathf.Abs(feet.y - Origin.y) < 3f;
            bool show = PlayerInside || Time.time < showUntil;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!PlayerInside || Time.time < cooldown || Rig.I.Teleporting || PlayerSeating.Seated) return;
            if (Flat(feet - exitDoor) < 0.75f)
            {
                cooldown = Time.time + 2f;
                Sfx.UI("elevator_doors", 0.6f);
                var fm = FloorManager.I;
                if (fm != null && fm.Current != 0) fm.Activate(0, false);
                var dealer = ElevatorDealer.I;
                if (dealer != null)
                {
                    Vector3 to; float yaw;
                    dealer.Landing(out to, out yaw);
                    Rig.I.Teleport(to, yaw, -1, "");
                    dealer.Welcome();
                }
                else if (fm != null && fm.floors.Length > 0)
                    Rig.I.Teleport(fm.floors[0].TransformPoint(new Vector3(0, 0, -5f)), fm.floors[0].eulerAngles.y + 180f, -1, "");
            }
        }

        static float Flat(Vector3 d) { d.y = 0; return d.magnitude; }
    }
}
