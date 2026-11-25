using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Ascend
{
    /// <summary>Main-hall DJ stage: DJ Ronin on the decks, LED wall + dance floor driven by the live audio spectrum,
    /// moving beams, speaker stacks that pump, and a secret DISCO button on the booth.</summary>
    public class DJStage : MonoBehaviour
    {
        public static bool Disco { get; private set; }
        const int W = 64, H = 24;
        /// <summary>LED dance floor: TX × TZ tiles of 0.8 m (11.2 m × 8 m) in front of the stage, one mesh + a tiny texture.</summary>
        public const int TX = 14, TZ = 10; public const float TileSize = 0.8f, FloorFront = -2.0f;
        public static float FloorHalfWidth { get { return TX * TileSize * 0.5f; } }
        public static float FloorDepth { get { return TZ * TileSize; } }
        Texture2D ledTex; Color32[] px;
        readonly float[] spec = new float[256];
        readonly float[] bands = new float[W];
        readonly List<Transform> beams = new List<Transform>();
        readonly List<Transform> cones = new List<Transform>();
        Texture2D floorTex; Color32[] floorPx = new Color32[TX * TZ];
        AvatarBody dj;
        float bass, discoUntil, nextHype;
        static readonly Color32 Bg = new Color32(6, 4, 12, 255);

        void Start()
        {
            var k = new MeshKit();
            k.Box(new Vector3(0, 0.3f, 0), new Vector3(7f, 0.6f, 3.2f), Palette.Hex("120D1C"), 0.02f);
            k.Bar(new Vector3(-3.5f, 0.6f, -1.6f), new Vector3(3.5f, 0.6f, -1.6f), 0.03f, Palette.Magenta, 1f);
            k.Bar(new Vector3(-3.5f, 0.02f, -1.6f), new Vector3(3.5f, 0.02f, -1.6f), 0.03f, Palette.Teal, 1f);
            // booth
            k.Box(new Vector3(0, 1.1f, -0.2f), new Vector3(2.0f, 1.0f, 0.7f), Palette.Hex("1B1328"), 0.05f);
            k.Bar(new Vector3(-1.0f, 1.6f, -0.56f), new Vector3(1.0f, 1.6f, -0.56f), 0.02f, Palette.Gold, 1f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Cylinder(new Vector3(s * 0.5f, 1.62f, -0.2f), 0.2f, 0.2f, 0.03f, 28, Quaternion.identity, Palette.Dark, 0.1f, true, Palette.Hex("222222"), 0.1f);
                k.Ring(new Vector3(s * 0.5f, 1.64f, -0.2f), 0.17f, 0.19f, 28, Quaternion.identity, Palette.Teal, 1f);
            }
            k.Box(new Vector3(0, 1.62f, -0.2f), new Vector3(0.4f, 0.03f, 0.3f), Palette.Metal, 0.2f);
            // truss
            for (int s = -1; s <= 1; s += 2) k.Bar(new Vector3(s * 3.4f, 0.6f, 1.2f), new Vector3(s * 3.4f, 5.2f, 1.2f), 0.12f, Palette.Metal, 0.05f);
            k.Bar(new Vector3(-3.4f, 5.2f, 1.2f), new Vector3(3.4f, 5.2f, 1.2f), 0.12f, Palette.Metal, 0.05f);
            k.Bar(new Vector3(-3.4f, 5.2f, -1.4f), new Vector3(3.4f, 5.2f, -1.4f), 0.08f, Palette.Metal, 0.05f);
            k.Spawn("Stage", transform, Palette.Glossy, false);
            // solid stage platform and booth (the dance floor in front stays walkable)
            var sc = Build.Empty("StageCol", transform, new Vector3(0, 0.3f, 0)).AddComponent<BoxCollider>(); sc.size = new Vector3(7f, 0.6f, 3.2f);
            var bc = Build.Empty("BoothCol", transform, new Vector3(0, 1.1f, -0.2f)).AddComponent<BoxCollider>(); bc.size = new Vector3(2f, 1f, 0.7f);

            // LED wall (dynamic texture)
            ledTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            px = new Color32[W * H];
            var lk = new MeshKit(); lk.Quad(Vector3.zero, new Vector2(6.4f, 2.4f), Quaternion.Euler(0, 0, 0), Color.white, 1f);
            lk.Spawn("LEDWall", transform, new Vector3(0, 3.0f, 1.3f), Quaternion.identity, Palette.ScreenMat(ledTex), false);
            Txt.Glow("Logo", transform, new Vector3(0, 4.55f, 1.25f), "ASCEND · LIVE", 0.35f, Palette.Text);

            // speaker stacks
            for (int s = -1; s <= 1; s += 2)
                for (int j = 0; j < 2; j++)
                {
                    var sk = new MeshKit();
                    sk.Box(Vector3.zero, new Vector3(0.9f, 0.9f, 0.8f), Palette.Hex("0E0B16"), 0.02f);
                    sk.Cylinder(new Vector3(0, 0, -0.41f), 0.32f, 0.32f, 0.02f, 24, Quaternion.Euler(90, 0, 0), Palette.Dark, 0.1f);
                    sk.Ring(new Vector3(0, 0, -0.425f), 0.3f, 0.34f, 24, Quaternion.Euler(-90, 0, 0), s < 0 ? Palette.Magenta : Palette.Teal, 1f);
                    sk.Sphere(new Vector3(0, 0, -0.43f), new Vector3(0.12f, 0.12f, 0.06f), 10, Palette.Metal, 0.2f);
                    var cone = sk.Spawn("Speaker", transform, new Vector3(s * 4.1f, 0.45f + j * 0.92f, -0.6f), Quaternion.Euler(0, -s * 15f, 0), Palette.Neon, false).transform;
                    cones.Add(cone);
                }

            // moving beams
            for (int i = 0; i < 6; i++)
            {
                var c = i % 3 == 0 ? Palette.Magenta : i % 3 == 1 ? Palette.Teal : Palette.Violet;
                var pivot = Build.Empty("BeamPivot" + i, transform, new Vector3(-3f + i * 1.2f, 5.1f, -1.4f)).transform;
                Build.Beam(pivot, Vector3.zero, Quaternion.Euler(90, 0, 0), 9f, 0.6f, c, 0.12f);
                beams.Add(pivot);
            }

            // dance floor (in front of the stage): one mesh, each tile samples its own pixel of a TX×TZ texture
            floorTex = new Texture2D(TX, TZ, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var tk = new MeshKit();
            for (int z = 0; z < TZ; z++)
                for (int x = 0; x < TX; x++)
                {
                    var uv = new Rect((x + 0.5f) / TX, (z + 0.5f) / TZ, 0.0001f, 0.0001f);
                    var c = new Vector3(-FloorHalfWidth + (x + 0.5f) * TileSize, 0.02f, FloorFront - (z + 0.5f) * TileSize);
                    tk.Box(c, new Vector3(TileSize - 0.05f, 0.04f, TileSize - 0.05f), Quaternion.identity, Color.white, 1f, uv);
                }
            tk.Spawn("DanceFloor", transform, Palette.ScreenMat(floorTex), false);
            // chrome edge around the floor
            var ek = new MeshKit();
            float x0 = -FloorHalfWidth - 0.05f, x1 = FloorHalfWidth + 0.05f, z0 = FloorFront + 0.05f, z1 = FloorFront - FloorDepth - 0.05f;
            ek.Bar(new Vector3(x0, 0.03f, z0), new Vector3(x1, 0.03f, z0), 0.03f, Palette.Gold, 1f);
            ek.Bar(new Vector3(x0, 0.03f, z1), new Vector3(x1, 0.03f, z1), 0.03f, Palette.Gold, 1f);
            ek.Bar(new Vector3(x0, 0.03f, z0), new Vector3(x0, 0.03f, z1), 0.03f, Palette.Gold, 1f);
            ek.Bar(new Vector3(x1, 0.03f, z0), new Vector3(x1, 0.03f, z1), 0.03f, Palette.Gold, 1f);
            ek.Spawn("FloorEdge", transform, Palette.Neon, false);
            // mirror ball over the middle of the floor
            var mb = new MeshKit(); mb.Sphere(Vector3.zero, 0.38f, 18, Palette.Silver, 0.6f);
            var ball = mb.Spawn("MirrorBall", transform, new Vector3(0, 5.6f, FloorFront - FloorDepth * 0.5f), Quaternion.identity, Palette.Lacquer, false);
            ball.AddComponent<Spin>().speed = new Vector3(0, 40f, 0);
            Build.GlowSprite(ball.transform, Vector3.zero, 2.2f, Palette.Text, 0.35f);

            // the DJ
            dj = Avatars.Create("DJ", transform, new Vector3(0, 0.6f, 0.35f), 180f, Rarity.Mythic, RoninRole.DJ);
            dj.pose = Pose.DJ; dj.bpm = 150f;
            var ap = dj.gameObject.AddComponent<AvatarProfile>(); ap.displayName = "DJ ASCENDANT"; ap.rarity = Rarity.Mythic; ap.xp = 520000; ap.status = "LIVE ON THE MAIN STAGE"; ap.squad = "ASCEND CREW";

            // disco easter egg: a tiny unlabeled button under the booth lip
            var btn = PokeButton.Create("Disco", transform, new Vector3(0.85f, 1.62f, -0.5f), Quaternion.identity, 0.02f, Palette.Gold, "", StartDisco);
            btn.cooldown = 2f;

        }

        void OnEnable() { nextHype = Time.time + 8f; }
        // the disco is a lobby-floor thing: leaving the floor ends it (otherwise the static flag stays on and guards on
        // other floors dance forever, because this Update no longer runs to time it out)
        void OnDisable() { Disco = false; }

        void StartDisco()
        {
            Disco = true; discoUntil = Time.time + 25f;
            Sfx.Play("disco_on", transform.position + Vector3.up * 2f, 1f);
            Voice.Say("dj_disco", dj.HeadBone, 0.6f);
            GameState.I.Unlock("NIGHT FEVER", "FOUND THE SECRET DISCO BUTTON", 400);
            Sfx.UI("secret_found", 0.8f);
        }

        void Update()
        {
            if (Disco && Time.time > discoUntil) Disco = false;
            AudioListener.GetSpectrumData(spec, 0, FFTWindow.BlackmanHarris);
            float b = 0; for (int i = 1; i < 6; i++) b += spec[i];
            b = Mathf.Clamp01(b * 6f);
            // fallback beat when there is no audio (editor muted etc.)
            float beat = Mathf.Pow(1f - Mathf.Repeat(Time.time * 150f / 60f, 1f), 3f);
            if (b < 0.01f) b = beat * 0.8f;
            bass = Mathf.Lerp(bass, b, Time.deltaTime * 18f);
            for (int x = 0; x < W; x++)
            {
                int i0 = 1 + (int)(Mathf.Pow(x / (float)W, 2f) * 180f);
                float v = 0; for (int j = 0; j < 3; j++) v += spec[Mathf.Min(255, i0 + j)];
                v = Mathf.Clamp01(Mathf.Sqrt(v) * 2.2f);
                if (v < 0.01f) v = Mathf.Abs(Mathf.Sin(x * 0.4f + Time.time * 3f)) * bass;
                bands[x] = Mathf.Max(v, bands[x] - Time.deltaTime * 1.6f);
            }
            DrawLed();
            for (int i = 0; i < cones.Count; i++) cones[i].localScale = Vector3.one * (1f + bass * 0.06f);
            for (int i = 0; i < beams.Count; i++)
            {
                float t = Time.time * (Disco ? 1.6f : 0.6f) + i;
                beams[i].localRotation = Quaternion.Euler(Mathf.Sin(t) * 35f - 25f, Mathf.Cos(t * 0.7f) * 45f, 0);
            }
            if (floorTex != null)
            {
                int beatN = (int)(Time.time * 124f / 60f);
                for (int z = 0; z < TZ; z++)
                    for (int x = 0; x < TX; x++)
                    {
                        float h = Disco ? Mathf.Repeat(Time.time * 0.5f + x * 0.12f + z * 0.09f, 1f) : (x + z) % 2 == 0 ? 0.88f : 0.52f;
                        // ripple from the centre on every beat + spectrum columns
                        float dist = Mathf.Sqrt((x - TX * 0.5f) * (x - TX * 0.5f) + (z - TZ * 0.5f) * (z - TZ * 0.5f));
                        float ripple = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Repeat(Time.time * 124f / 60f, 1f) * 8f - dist)) * bass;
                        float on = Disco ? (((int)(Time.time * 4f) + x * 3 + z * 5) % 3 == 0 ? 1f : 0.2f)
                                         : Mathf.Clamp01(bands[Mathf.Min(W - 1, x * W / TX)] * 1.1f + ripple + ((beatN + x + z) % 4 == 0 ? bass * 0.6f : 0f));
                        floorPx[z * TX + x] = (Color32)Color.HSVToRGB(h, 0.85f, 0.12f + on * 0.88f);
                    }
                floorTex.SetPixels32(floorPx); floorTex.Apply(false);
            }
            if (Time.time > nextHype && dj != null)
            {
                nextHype = Time.time + Random.Range(25f, 45f);
                Voice.Say("dj_hype" + Random.Range(1, 5), dj.HeadBone, 0.6f, 0.8f);
                dj.Play(Emote.Cheer, 1.2f);
            }
        }

        void DrawLed()
        {
            for (int i = 0; i < px.Length; i++) px[i] = Bg;
            float hueBase = Time.time * 0.05f;
            for (int x = 0; x < W; x++)
            {
                int h = Mathf.RoundToInt(bands[x] * (H - 2));
                var c = Color.HSVToRGB(Mathf.Repeat(hueBase + x / (float)W * 0.6f, 1f), Disco ? 1f : 0.85f, 1f);
                for (int y = 0; y < h; y++)
                {
                    int yy = H / 2 + (y % 2 == 0 ? y / 2 : -(y / 2) - 1);
                    if (yy < 0 || yy >= H) continue;
                    float f = 1f - Mathf.Abs(yy - H / 2) / (H * 0.5f) * 0.6f;
                    px[yy * W + x] = (Color32)(c * f);
                }
            }
            ledTex.SetPixels32(px);
            ledTex.Apply(false);
        }
    }
}
