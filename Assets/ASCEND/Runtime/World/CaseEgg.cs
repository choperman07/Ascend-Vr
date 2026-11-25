using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Easter egg in EL SÓTANO: LA CAJA. A battered weapon-style crate on a table, a reel of drops on the wall
    /// behind it and EL STREAMER, who has clearly been here for days, chanting "Gold, gold, gold, gold" at every spin.
    /// 50 chips a crate. The reel scrolls and slows onto one tile: blue 0.2x · purple 0.6x · pink 2x · red 5x ·
    /// gold 30x (1 in 100). Return ≈ 97 %. Original art and an invented character: a parody of case-opening streams.</summary>
    public class CaseEgg : MonoBehaviour
    {
        public static CaseEgg I { get; private set; }
        public const long Price = 50;
        static readonly Color[] Tier = { Palette.Hex("3D6BFF"), Palette.Hex("8B47FF"), Palette.Hex("D32CE6"), Palette.Hex("EB4B4B"), Palette.Hex("F2C230") };
        static readonly string[] TierName = { "AZUL", "MORADO", "ROSA", "ROJO", "GOLD" };
        static readonly float[] Chance = { 0.60f, 0.25f, 0.10f, 0.04f, 0.01f };
        static readonly float[] Pays = { 0.2f, 0.6f, 2f, 5f, 30f };
        const int Tiles = 44, Land = 37; const float Pitch = 0.3f, Window = 1.12f;

        Transform strip, lid; readonly List<Transform> tiles = new List<Transform>(); readonly List<Renderer> tileR = new List<Renderer>();
        readonly int[] tileTier = new int[Tiles];
        PokeButton btn; TextMeshPro status, say; Transform sayPivot, sayPlate;
        AvatarBody streamer; bool spinning; float sayUntil;
        public Transform Reel { get; private set; }
        public AvatarBody Streamer { get { return streamer; } }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            var mat = Palette.Flat(Color.white, 0.03f, 0.08f, 0.3f);
            // ---- the reel: a dark screen on the wall with a gold marker in the middle
            Reel = Build.Empty("Reel", transform, new Vector3(0, 1.95f, 0.12f), Quaternion.Euler(0, 180f, 0)).transform;   // faces the room
            var k = new MeshKit();
            k.Box(new Vector3(0, 0, 0.03f), new Vector3(Window * 2 + 0.3f, 0.86f, 0.06f), Palette.Hex("0E0B16"), 0f);
            k.Box(new Vector3(0, 0.45f, 0.02f), new Vector3(Window * 2 + 0.34f, 0.05f, 0.09f), Palette.Gold, 0.4f);
            k.Box(new Vector3(0, -0.45f, 0.02f), new Vector3(Window * 2 + 0.34f, 0.05f, 0.09f), Palette.Gold, 0.4f);
            foreach (var sx in new[] { -1f, 1f }) k.Box(new Vector3(sx * (Window + 0.17f), 0, -0.02f), new Vector3(0.1f, 0.9f, 0.12f), Palette.Hex("1B1528"), 0f);
            k.Box(new Vector3(0, 0, -0.06f), new Vector3(0.012f, 0.8f, 0.01f), Palette.Gold, 1.6f);
            k.Cylinder(new Vector3(0, 0.39f, -0.06f), 0f, 0.04f, 0.07f, 3, Quaternion.Euler(0, 0, 180), Palette.Gold, 1.6f);
            k.Cylinder(new Vector3(0, -0.39f, -0.06f), 0f, 0.04f, 0.07f, 3, Quaternion.identity, Palette.Gold, 1.6f);
            k.Spawn("Screen", Reel, Palette.Neon, false);
            Txt.Glow("Title", Reel, new Vector3(0, 0.66f, 0), "LA CAJA", 0.2f, Palette.Gold);
            status = Txt.Make("Status", Reel, new Vector3(0, -0.62f, 0), "50 FICHAS · AZUL 0.2x · MORADO 0.6x · ROSA 2x · ROJO 5x · GOLD 30x", 0.055f, Palette.Text, Palette.Body);
            strip = Build.Empty("Strip", Reel, new Vector3(0, 0, -0.01f)).transform;
            for (int i = 0; i < Tiles; i++)
            {
                var t = Build.Empty("Tile" + i, strip, new Vector3(i * Pitch, 0, 0)).transform;
                var tk = new MeshKit();
                tk.Box(Vector3.zero, new Vector3(Pitch - 0.03f, 0.62f, 0.02f), Palette.Hex("231C33"), 0f);
                tk.Box(new Vector3(0, -0.27f, -0.012f), new Vector3(Pitch - 0.03f, 0.07f, 0.012f), Color.white, 1.2f);
                // the drop: a blocky little "skin" silhouette (barrel, body, grip, stock)
                tk.Box(new Vector3(0.02f, 0.05f, -0.014f), new Vector3(0.17f, 0.035f, 0.012f), Color.white, 0.55f);
                tk.Box(new Vector3(-0.03f, 0.03f, -0.014f), new Vector3(0.09f, 0.07f, 0.012f), Color.white, 0.55f);
                tk.Box(new Vector3(-0.045f, -0.03f, -0.014f), new Vector3(0.035f, 0.08f, 0.012f), Quaternion.Euler(0, 0, 14), Color.white, 0.55f);
                tk.Box(new Vector3(-0.1f, 0.02f, -0.014f), new Vector3(0.06f, 0.05f, 0.012f), Color.white, 0.55f);
                var go = tk.Spawn("Body", t, Palette.Neon, false);
                tiles.Add(t); tileR.Add(go.GetComponent<Renderer>());
            }
            Deck(-1);
            Scroll(0f);

            // ---- the crate on its table
            var ck = new MeshKit();
            ck.Box(new Vector3(0, 0.4f, 1.0f), new Vector3(1.0f, 0.06f, 0.6f), Palette.Hex("3A2A22"), 0f);
            foreach (var sx in new[] { -0.44f, 0.44f }) foreach (var sz in new[] { 0.76f, 1.24f }) ck.Box(new Vector3(sx, 0.2f, sz), new Vector3(0.06f, 0.4f, 0.06f), Palette.Hex("2A1E18"), 0f);
            Color olive = Palette.Hex("5B6146"), steel = Palette.Hex("8D9096");
            ck.Box(new Vector3(0, 0.56f, 1.0f), new Vector3(0.56f, 0.26f, 0.36f), olive, 0f);
            foreach (var sx in new[] { -0.2f, 0.2f }) ck.Box(new Vector3(sx, 0.56f, 1.0f), new Vector3(0.05f, 0.27f, 0.37f), steel, 0f);
            ck.Box(new Vector3(0, 0.56f, 0.815f), new Vector3(0.2f, 0.09f, 0.012f), Palette.Hex("F2C230"), 0.5f);
            ck.Box(new Vector3(0, 0.6f, 0.808f), new Vector3(0.07f, 0.06f, 0.02f), steel, 0f);
            ck.Spawn("Crate", transform, mat, false);
            Txt.Make("Stencil", transform, new Vector3(0, 0.56f, 0.807f), "ASCEND", 0.035f, Palette.Hex("1B1A12"), Palette.Display).transform.localRotation = Quaternion.identity;
            lid = Build.Empty("Lid", transform, new Vector3(0, 0.69f, 1.18f)).transform;
            var lk = new MeshKit();
            lk.Box(new Vector3(0, 0.03f, -0.18f), new Vector3(0.58f, 0.06f, 0.38f), olive, 0f);
            foreach (var sx in new[] { -0.2f, 0.2f }) lk.Box(new Vector3(sx, 0.03f, -0.18f), new Vector3(0.05f, 0.065f, 0.385f), steel, 0f);
            lk.Spawn("LidMesh", lid, mat, false);
            Build.BoxCol(Build.Empty("Col", transform, new Vector3(0, 0.4f, 1.0f)), Vector3.zero, new Vector3(1.0f, 0.8f, 0.6f));

            // ---- the button on a pedestal next to the table
            var pk = new MeshKit(); pk.Cylinder(new Vector3(0.9f, 0.46f, 1.15f), 0.11f, 0.08f, 0.92f, 16, Quaternion.identity, Palette.Hex("1B1528"), 0f);
            pk.Spawn("Pedestal", transform, mat, false);
            btn = PokeButton.Create("Open", transform, new Vector3(0.9f, 0.93f, 1.15f), Quaternion.identity, 0.07f, Palette.Gold, "ABRIR", Open);

            // ---- EL STREAMER: hoodie, headset with a mic, three days without sleep
            var lib = AssetLibrary.I; var at = new Vector3(-1.0f, 0f, 1.55f);
            if (Avatars.UseHumanoids && lib != null && lib.HasHumanoids && lib.android != null)
                streamer = HumanoidBody.CreateFrom(lib.android, lib.humanoidController, lib, "EL STREAMER", transform, at, 35f, Rarity.Rare, RoninRole.Player, 1.84f, HumanoidBody.Kind.Extra);
            if (streamer == null) streamer = Avatars.Create("EL STREAMER", transform, at, 35f, Rarity.Rare, RoninRole.Player, 1.84f);
            if (streamer != null)
            {
                streamer.pose = Pose.Idle;
                var hb = streamer as HumanoidBody;
                if (hb != null) { hb.Vary(17, 0.9f); hb.idleSet = new[] { "IdleArms", "IdleThink", "Idle" }; }
                foreach (var r in streamer.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.Contains("Hat")) r.enabled = false;
                    if (r.name.Contains("Jacket") || r.name.Contains("Shirt") || r.name.Contains("Outwear")) { var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.14f)); r.SetPropertyBlock(mpb); }
                }
                var head = streamer.HeadBone;
                if (head != null && head != streamer.transform)
                {
                    float sc = 1.84f / 1.75f;
                    var rig = new GameObject("Headset").transform; rig.SetParent(streamer.transform, false);
                    rig.position = head.position + streamer.transform.up * HumanoidBody.EyeUp * sc; rig.rotation = streamer.transform.rotation;
                    var hk = new MeshKit(); Color blk = Palette.Hex("15151A");
                    hk.Torus(new Vector3(0, 0.0f, -0.01f), 0.135f * sc, 0.014f, 28, 6, Quaternion.Euler(90, 0, 0), blk, 0f, 180f);
                    foreach (var sx in new[] { -1f, 1f }) { hk.Cylinder(new Vector3(sx * 0.135f * sc, -0.01f, -0.01f), 0.05f, 0.05f, 0.04f, 16, Quaternion.Euler(0, 0, 90), blk, 0f); hk.Cylinder(new Vector3(sx * 0.158f * sc, -0.01f, -0.01f), 0.03f, 0.03f, 0.008f, 12, Quaternion.Euler(0, 0, 90), Palette.Hex("F2C230"), 1.2f); }
                    hk.Bar(new Vector3(-0.135f * sc, -0.03f, 0f), new Vector3(-0.07f * sc, -0.1f, HumanoidBody.EyeFwd * sc + 0.02f), 0.008f, blk, 0f);
                    hk.Sphere(new Vector3(-0.07f * sc, -0.1f, HumanoidBody.EyeFwd * sc + 0.02f), 0.016f, 8, blk, 0f);
                    hk.Spawn("Mesh", rig, mat, false);
                    rig.SetParent(head, true);
                }
            }
            // his bubble (dark, see-through, like everyone's)
            sayPivot = Build.Empty("Say", transform, at + Vector3.up * 2.35f).transform; sayPivot.gameObject.AddComponent<Billboard>();
            say = Txt.Make("Text", sayPivot, Vector3.zero, "", 0.1f, Color.white, Palette.Body, TextAlignmentOptions.Center, 1.8f);
            say.enableWordWrapping = true; say.fontStyle = FontStyles.Bold;
            var qk = new MeshKit(); qk.Quad(Vector3.zero, Vector2.one, Quaternion.identity, Color.white, 1f);
            sayPlate = qk.Spawn("Plate", sayPivot, new Vector3(0, 0, 0.012f), Quaternion.identity, Palette.Translucent(new Color(0.07f, 0.05f, 0.12f, 0.6f)), false).transform;
            sayPivot.gameObject.SetActive(false);
            var lg = new GameObject("CaseLight"); lg.transform.SetParent(transform, false); lg.transform.localPosition = new Vector3(0, 2.6f, 1.6f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.85f, 0.55f); l.intensity = 1.4f; l.range = 4.5f; l.shadows = LightShadows.None;
        }

        void Say(string s, float secs)
        {
            say.text = s; say.ForceMeshUpdate(); var sz = say.GetRenderedValues(false);
            sayPlate.localScale = new Vector3(Mathf.Max(0.3f, sz.x) + 0.2f, Mathf.Max(0.1f, sz.y) + 0.14f, 1f);
            sayPivot.gameObject.SetActive(true); sayUntil = Time.time + secs;
        }

        void Update()
        {
            if (sayPivot != null && sayPivot.gameObject.activeSelf && Time.time > sayUntil) sayPivot.gameObject.SetActive(false);
            if (streamer != null) streamer.lookAtTarget = spinning ? Reel : (TrailerView.Head != null && (TrailerView.Head.position - streamer.transform.position).sqrMagnitude < 16f ? TrailerView.Head : null);
        }

        /// <summary>Fills the strip with random drops; <paramref name="win"/> (0-4) is put on the landing tile.</summary>
        void Deck(int win)
        {
            for (int i = 0; i < Tiles; i++)
            {
                int t = i == Land && win >= 0 ? win : Roll(i == Land + 1 || i == Land - 1 ? 0.06f : 0f);   // gold loves to stop one tile away
                tileTier[i] = t; Fx.SetColor(tileR[i], Tier[t], t == 4 ? 1.3f : 0.7f);
            }
        }
        static int Roll(float goldBonus = 0f)
        {
            float r = Random.value - goldBonus; if (r < 0f) return 4;
            float acc = 0f; for (int i = 0; i < Chance.Length; i++) { acc += Chance[i]; if (r < acc) return i; }
            return 0;
        }
        void Scroll(float x)
        {
            strip.localPosition = new Vector3(-x, 0, -0.01f);
            for (int i = 0; i < Tiles; i++) { float lx = i * Pitch - x; tiles[i].gameObject.SetActive(Mathf.Abs(lx) < Window); }
        }

        void Open()
        {
            if (spinning) return;
            var gs = GameState.I;
            if (gs == null || !gs.TakeChips(Price)) { Say("Sin fichas no hay caja, hermano.", 2.5f); Sfx.UI("ui_error", 0.4f); return; }
            StartCoroutine(Spin(Roll(), Price));
        }

        /// <summary>Trailer: one crate, no chips, the drop decided.</summary>
        public void DemoSpin(int tier) { if (!spinning && isActiveAndEnabled) StartCoroutine(Spin(Mathf.Clamp(tier, 0, 4), 0)); }

        IEnumerator Spin(int win, long stake)
        {
            spinning = true; btn.SetInteractable(false);
            Deck(win);
            status.text = "ABRIENDO..."; status.color = Palette.Text;
            Sfx.Play("chips_sweep", transform.position + Vector3.up, 0.6f, 0.7f);
            if (streamer != null) streamer.Play(Emote.Cheer, 5.6f);
            float end = Land * Pitch + Random.Range(-0.11f, 0.11f), dur = 5.6f; int last = -1; float nextGold = 0f;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float u = t / dur, k = 1f - Mathf.Pow(1f - u, 3.4f);
                float x = end * k; Scroll(x);
                lid.localRotation = Quaternion.Euler(Mathf.Lerp(0f, 105f, Mathf.Clamp01(t / 0.6f)), 0, 0);
                int idx = Mathf.RoundToInt(x / Pitch);
                if (idx != last) { last = idx; Sfx.Play("ball_tick", Reel.position, 0.4f, 0.9f + u * 0.5f); }
                if (t >= nextGold) { nextGold = t + 1.15f; Say(u < 0.75f ? "Gold, gold, gold, gold..." : "GOLD, GOLD, GOLD, GOLD!!", 1.2f); }
                yield return null;
            }
            Scroll(end);
            long pay = (long)(stake * (double)Pays[win]);
            status.text = TierName[win] + " · " + Pays[win].ToString("0.#") + "x" + (stake > 0 ? " · " + pay.ToString("N0") + " FICHAS" : "");
            status.color = Tier[win];
            var at = Reel.position - Reel.forward * 0.2f;
            if (win == 4)
            {
                Say("¡¡GOOOOOLD!! ¡¡VAMOOOOS!!", 4f); Juice.Sparks(at, Tier[4], 80); Sfx.UI("streak", 0.9f);
                if (streamer != null) streamer.Play(Emote.Cheer, 3.5f);
            }
            else if (win == 3) { Say("¡Rojo! Casi. Casi gold.", 3f); Juice.Sparks(at, Tier[3], 30); if (streamer != null) streamer.Play(Emote.Clap, 2f); }
            else { Say(win == 0 ? "Azul. Otra vez azul. Abre otra." : "Bueno... la siguiente es gold. Fijo.", 3f); if (streamer != null) streamer.Play(Emote.Facepalm, 2f); }
            if (stake > 0 && GameState.Exists && pay > 0) GameState.I.Pay(pay, stake, at);
            yield return new WaitForSeconds(1.6f);
            for (float t = 0; t < 0.4f; t += Time.deltaTime) { lid.localRotation = Quaternion.Euler(Mathf.Lerp(105f, 0f, t / 0.4f), 0, 0); yield return null; }
            lid.localRotation = Quaternion.identity;
            spinning = false; btn.SetInteractable(true);
        }
    }
}
