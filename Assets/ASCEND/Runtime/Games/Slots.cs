using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Weighted multiplier table shared by luck games. RTP is computed from the table itself.</summary>
    public class PrizeTable
    {
        public readonly string[] names; public readonly float[] mult; public readonly double[] pct;
        public PrizeTable(string[] names, float[] mult, double[] pct) { this.names = names; this.mult = mult; this.pct = pct; }
        public int Roll()
        {
            double r = Odds.R() * 100.0, acc = 0;
            for (int i = 0; i < pct.Length; i++) { acc += pct[i]; if (r < acc) return i; }
            return 0;
        }
        public double RTP { get { double s = 0; for (int i = 0; i < pct.Length; i++) s += pct[i] / 100.0 * mult[i]; return s; } }
        public string Describe(int perLine = 3)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pct.Length; i++)
            {
                sb.Append(names[i]).Append(' ').Append(mult[i].ToString("0.##")).Append('x');
                if (i < pct.Length - 1) sb.Append(i % perLine == perLine - 1 ? "\n" : " · ");
            }
            return sb.ToString();
        }
    }

    /// <summary>Reel slot machine in the Casino FREE cabinets (Slot_Machine_01 / 02 / Aquarium, ithappy) with the pack's
    /// own fruit symbols on three reels in the cabinet window, a pay table on the lower screen, CREDIT / BET / WIN LEDs
    /// under the reels, pull lever (grab, finger tap or mouse click) or SPIN button, reel-stop haptics. Same honest maths
    /// on every theme: 5 symbols with fixed per-reel weights. The symbol that stops on the pay line is exactly the symbol
    /// that was evaluated; the reels never swing back across the line (no fake near-misses).</summary>
    public class Slots : ArcadeStation
    {
        public int theme;   // 0 = Fruit Cocktail (Slot_Machine_01), 1 = Golden 7 (Slot_Machine_02), 2 = Aqua Fruits (aquarium cabinet)
        static readonly int[] Weights = { 40, 28, 18, 10, 4 };          // symbol 0..4 per reel (percent)
        static readonly float[] Three = { 2f, 5f, 15f, 80f, 250f };
        static readonly float[] Two = { 0.5f, 0.95f, 1.5f, 2f, 5f };
        static readonly int[] Strip = { 0, 1, 0, 2, 1, 3, 0, 1, 2, 0, 4, 1 };   // what is printed around each reel
        static readonly string[][] Names =
        {
            new[] { "CHERRY", "LEMON", "PLUM", "MELON", "CLOVER" },
            new[] { "CHERRY", "BANANA", "CROWN", "DIAMOND", "SEVEN" },
            new[] { "ORANGE", "APPLE", "BERRY", "CROWN", "DIAMOND" },
        };
        static readonly string[] Titles = { "FRUIT COCKTAIL", "GOLDEN 7", "AQUA FRUITS" };
        /// <summary>Casino FREE cabinet of each theme (the cabinet art already carries the game's title).</summary>
        static readonly string[] Cabinets = { "Slot_Machine_01", "Slot_Machine_02", "Slot_Machine_Aquarium_01" };
        public static string TitleOf(int theme) { return Titles[Mathf.Clamp(theme, 0, Titles.Length - 1)]; }
        const int N = 12; const float Step = 360f / N, R = 0.2f;
        /// <summary>Height of one symbol on the drum (arc length of a 30° slice).</summary>
        static readonly float Pitch = 2f * Mathf.PI * R / N;
        /// <summary>Reel width: one atlas cell is one 30° slice at the strip's true aspect (1365 × 819 px).</summary>
        static readonly float ReelW = Pitch * 1365f / 819f;
        const float ReelGap = 0.19f;   // centre to centre: three 0.175 m reels in the 0.56 m window of the pack cabinet

        // ---- reel atlas: Resources/ASCEND/Textures/slot_symbols.png (4096², composed from the Casino FREE Slots.png symbols)
        // 3 columns = themes, 5 rows = symbols (row 0 at the top of the image). Each 1365×819 cell is one 30° slice of
        // the reel strip at the strip's true world aspect, on the pack's dark reel background so mips never seam.
        const float AtlasPx = 4096f, CellPxW = 1365f, CellPxH = 819f, CellInsetPx = 3f;

        static Rect Cell(int th, int sym)
        {
            float w = CellPxW / AtlasPx, h = CellPxH / AtlasPx, e = CellInsetPx / AtlasPx;
            return new Rect(th * w + e, 1f - (sym + 1) * h + e, w - 2f * e, h - 2f * e);
        }

        /// <summary>The square part of a cell around the symbol (pay table icons).</summary>
        static Rect IconUV(int th, int sym)
        {
            var c = Cell(th, sym);
            float fw = c.width * (Pitch / ReelW);
            return new Rect(c.x + (c.width - fw) * 0.5f, c.y, fw, c.height);
        }

        readonly Transform[] reels = new Transform[3];
        readonly float[] angle = new float[3];
        Transform lever, leverHandle;
        XRGrabInteractable leverGrab;
        float leverAngle;
        bool leverAnimating, wasGrabbed;
        Vector3 grabStart; float grabAngle0;
        bool spinning, spinGuest, spinPaid, waitingForGuest;
        int[] spinRes; float[] spinTo; long spinStake; AudioSource spinLoop;
        long lastStake, lastWin; string lastGuest = "";
        Renderer[] lamps;
        readonly Renderer[] winFrames = new Renderer[3];
        int winMask; float winBig;
        TextMeshPro meter, creditT, betLedT, winT;

        int Th { get { return Mathf.Clamp(theme, 0, 2); } }
        string[] SymbolNames { get { return Names[Th]; } }

        protected override string Rules
        {
            get
            {
                var n = SymbolNames;
                var sb = new StringBuilder("3 SAME ON THE LINE: ");
                for (int s = 0; s < 5; s++) sb.Append(n[s]).Append(' ').Append(Three[s].ToString("0.##")).Append('x').Append(s < 4 ? " · " : "\n");
                sb.Append("ANY 2 SAME: ");
                for (int s = 0; s < 5; s++) sb.Append(Two[s].ToString("0.##")).Append('x').Append(s < 4 ? " · " : "\n");
                sb.Append("PULL THE LEVER OR PRESS SPIN");
                return sb.ToString();
            }
        }
        protected override float CounterWidth { get { return 1.0f; } }
        /// <summary>The host stands beside the cabinet on a side with no neighbouring machine. In a bank the machines are
        /// 1.25 m apart, so the old fixed spot (x -1.05) put the host inside the next cabinet.</summary>
        protected override Vector3 DealerPos { get { return new Vector3(dealerSide * 1.05f, 0, 0.3f); } }
        int dealerSide = -1;

        protected override void Start()
        {
            if (hasDealer && dealerSide != 0)
            {
                bool leftFree = SideFree(-1), rightFree = SideFree(1);
                dealerSide = leftFree ? -1 : rightFree ? 1 : 0;
                if (dealerSide == 0) hasDealer = false;
            }
            base.Start();
        }

        bool SideFree(int side)
        {
            if (transform.parent == null) return true;
            foreach (var other in transform.parent.GetComponentsInChildren<TableGame>(true))
            {
                if (other == this) continue;
                var l = transform.InverseTransformPoint(other.transform.position);
                if (Mathf.Abs(l.z) < 1.2f && l.x * side > 0.3f && l.x * side < 1.9f) return false;
            }
            return true;
        }
        /// <summary>Above the topper: the default (y 2.05, z 0.55) sat inside the cabinet, so win/lose messages were invisible.</summary>
        protected override Vector3 StatusPos { get { return new Vector3(0, 3.08f, 0.32f); } }

        protected override void BuildMachine()
        {
            var th = Th;
            if (statusT != null) statusT.fontSize = 0.8f;                 // narrower than a table sign: neighbours are 1.25 m apart
            if (betT != null) betT.gameObject.SetActive(false);            // the BET LED shows it on the machine itself

            // ---- cabinet: the Casino FREE slot machine, turned to face the player (-Z) and set back so its button deck
            // ends where the old cabinet's did. Window for the reels: y 1.24-1.65, 0.56 m wide, leaning back 9°.
            var cab = Props.Place(Cabinets[th], transform, new Vector3(0, 0, 0.42f), 180f, 1f, false);
            bool pack = cab != null;
            Vector3 bankPos; Quaternion bankRot; Vector2 window;
            Transform payRoot, ledRoot;
            Vector3 leverPos, spinPos, lampY; float lampStep;
            if (pack)
            {
                bankPos = new Vector3(0, 1.443f, 0.304f); bankRot = Quaternion.Euler(9f, 0, 0); window = new Vector2(0.57f, 0.41f);
                // the pay table covers the cabinet's lower screen (y 1.70-2.04); the LEDs sit on the sloped face under the reels
                payRoot = Build.Empty("Paytable", transform, new Vector3(0, 1.872f, 0.296f)).transform;
                payRoot.localRotation = Quaternion.Euler(3f, 0, 0);
                ledRoot = Build.Empty("Leds", transform, new Vector3(0, 1.12f, 0.104f)).transform;
                ledRoot.localRotation = Quaternion.Euler(51f, 0, 0);
                leverPos = new Vector3(0.47f, 1.4f, 0.3f); spinPos = new Vector3(0.24f, 1.0f, -0.03f);
                lampY = new Vector3(-0.35f, 2.465f, 0.135f); lampStep = 0.1f;
                var bk = new MeshKit();
                // below the cabinet's own GRAND / JACKPOT banner (it overhangs the top of this screen)
                bk.Quad(new Vector3(0, -0.04f, 0), new Vector2(0.586f, 0.265f), Quaternion.identity, Palette.Hex("1E1C27"), 0f);
                bk.Spawn("PayBack", payRoot, Palette.Neon, false);
                var lk0 = new MeshKit();
                lk0.Quad(Vector3.zero, new Vector2(0.56f, 0.15f), Quaternion.identity, Palette.Hex("15131C"), 0f);
                lk0.Spawn("LedBack", ledRoot, Palette.Neon, false);
            }
            else
            {
                // procedural stand-in when the pack is missing
                bankPos = new Vector3(0, 1.62f, 0.2f); bankRot = Quaternion.identity; window = new Vector2(0.6f, 0.64f);
                var body = new MeshKit();
                var dark = Palette.Hex("3A2F55"); var trim = Palette.Hex("E8E4F2");
                body.Box(new Vector3(0, 0.5f, 0.42f), new Vector3(0.86f, 1.0f, 0.56f), dark, 0f);
                body.Box(new Vector3(0, 1.68f, 0.45f), new Vector3(0.86f, 1.36f, 0.5f), dark, 0f);
                body.Box(new Vector3(0, 1.02f, 0.08f), new Vector3(0.86f, 0.08f, 0.28f), Quaternion.Euler(18f, 0, 0), trim, 0f);
                body.Spawn("Cabinet", transform, Palette.Lacquer, false);
                payRoot = Build.Empty("Paytable", transform, new Vector3(0, 2.18f, 0.195f)).transform;
                ledRoot = Build.Empty("Leds", transform, new Vector3(0, 1.2f, 0.195f)).transform;
                leverPos = new Vector3(0.5f, 1.45f, 0.35f); spinPos = new Vector3(0.24f, TableY + 0.035f, -0.32f);
                lampY = new Vector3(-0.35f, 2.4f, 0.19f); lampStep = 0.1f;
                var bk = new MeshKit();
                bk.Quad(Vector3.zero, new Vector2(0.8f, 0.345f), Quaternion.identity, Palette.Hex("1E1C27"), 0f);
                bk.Spawn("PayBack", payRoot, Palette.Neon, false);
            }

            // ---- printed pay table (the real multipliers from Three / Two next to each symbol, always in sync with the maths)
            // five rows that fit the visible part of the screen (the header used to hide behind the cabinet's banner
            // and the first row was cut in half by it)
            float rowTop = pack ? 0.058f : 0.1f, rowStep = pack ? 0.049f : 0.055f;
            if (!pack)
            {
                var hdr = Txt.Make("PayTitle", payRoot, new Vector3(0, 0.145f, -0.002f), "TOP PRIZE " + Three[4].ToString("0.##") + "x · LINE PAYS", 0.026f, Palette.Gold, Palette.UI, TextAlignmentOptions.Center, 0.55f);
                hdr.enableWordWrapping = false;
            }
            var pay = new MeshKit();
            for (int s = 0; s < 5; s++)
            {
                float y = rowTop - s * rowStep;
                pay.Quad(new Vector3(-0.215f, y, -0.002f), new Vector2(0.068f, 0.041f), Quaternion.identity, Color.white, 1f, IconUV(th, 4 - s));
                var p3 = Txt.Make("Pay3_" + s, payRoot, new Vector3(-0.025f, y, -0.003f), "3 = " + Three[4 - s].ToString("0.##") + "x", 0.03f, s == 0 ? Palette.Gold : Palette.Text, Palette.UI, TextAlignmentOptions.Left, 0.2f);
                var p2 = Txt.Make("Pay2_" + s, payRoot, new Vector3(0.175f, y, -0.003f), "2 = " + Two[4 - s].ToString("0.##") + "x", 0.026f, Palette.Hex("C9C4DA"), Palette.UI, TextAlignmentOptions.Left, 0.16f);
                p3.enableWordWrapping = false; p2.enableWordWrapping = false;
            }
            pay.Spawn("Icons", payRoot, Palette.Textured("slot_symbols", 1f), false);
            meter = null;

            // ---- CREDIT / BET / WIN LEDs
            string[] ledNames = { "CREDIT", "BET", "WIN" };
            Color[] ledCols = { Palette.Mint, Palette.Gold, Palette.Magenta };
            var leds = new TextMeshPro[3];
            for (int i = 0; i < 3; i++)
            {
                float cx = -0.19f + i * 0.19f;
                Txt.Make("Led" + ledNames[i], ledRoot, new Vector3(cx, 0.045f, -0.002f), ledNames[i], 0.022f, Palette.Muted, Palette.UI);
                var t = Txt.Make("LedValue" + i, ledRoot, new Vector3(cx, -0.015f, -0.003f), "", 0.05f, ledCols[i], Palette.Mono, TextAlignmentOptions.Center, 0.17f);
                t.enableWordWrapping = false; t.enableAutoSizing = true; t.fontSizeMin = 0.2f; t.fontSizeMax = 0.5f;
                t.fontStyle = FontStyles.Bold;
                leds[i] = t;
            }
            creditT = leds[0]; betLedT = leds[1]; winT = leds[2];

            // ---- marquee bulbs along the top of the cabinet (win chaser)
            lamps = new Renderer[8];
            for (int i = 0; i < 8; i++)
            {
                var lk = new MeshKit(); lk.Sphere(Vector3.zero, 0.02f, 8, Color.white, 1f);
                var lg = lk.Spawn("Lamp" + i, transform, lampY + new Vector3(i * lampStep, 0, 0), Quaternion.identity, Palette.Neon, false);
                lamps[i] = lg.GetComponent<Renderer>();
            }

            // ---- reels: three drums in the cabinet window. The drums sit in a parent squashed to 20 % depth, so the
            // strip curves like a reel but stays in front of the shallow window recess from edge to edge.
            var bank = Build.Empty("Reels", transform, bankPos).transform;
            bank.localRotation = bankRot;
            var squash = Build.Empty("Drums", bank, new Vector3(0, 0, 0.005f)).transform;
            squash.localScale = new Vector3(1f, 1f, 0.2f);
            const int Sub = 4;
            float chord = 2f * R * Mathf.Sin(Step / Sub * 0.5f * Mathf.Deg2Rad);
            var symMat = Palette.Textured("slot_symbols", 1f);
            var deco = new MeshKit();
            for (int r = 0; r < 3; r++)
            {
                float rx = (r - 1) * ReelGap;
                var reel = Build.Empty("Reel" + r, squash, new Vector3(rx, 0, 0)).transform;
                var drum = new MeshKit();
                drum.Cylinder(Vector3.zero, R - 0.004f, R - 0.004f, ReelW, 24, Quaternion.Euler(0, 0, 90), Palette.Hex("201E27"), 0f, true);
                drum.Spawn("Drum", reel, Palette.Neon, false);
                var sk = new MeshKit();
                for (int i = 0; i < N; i++)
                {
                    var c = Cell(th, Strip[i]);
                    for (int j = 0; j < Sub; j++)
                    {
                        float a = i * Step - Step * 0.5f + (j + 0.5f) * Step / Sub;     // j = 0 is the bottom of the cell
                        var uv = new Rect(c.x, c.y + c.height * j / Sub, c.width, c.height / Sub);
                        sk.Quad(Quaternion.Euler(a, 0, 0) * new Vector3(0, 0, -R), new Vector2(ReelW, chord * 1.002f), Quaternion.Euler(a, 0, 0), Color.white, 1f, uv);
                    }
                }
                sk.Spawn("Symbols", reel, symMat, false);
                reels[r] = reel;
                angle[r] = -Odds.Range(0, N) * Step;
                reel.localRotation = Quaternion.Euler(angle[r], 0, 0);

                // win highlight: a glowing frame around this reel's pay-line symbol
                var fk = new MeshKit();
                float hw = ReelW * 0.5f - 0.004f, hh = Pitch * 0.5f, t = 0.006f;
                fk.Bar(new Vector3(-hw, -hh, 0), new Vector3(hw, -hh, 0), t, Color.white, 1f);
                fk.Bar(new Vector3(-hw, hh, 0), new Vector3(hw, hh, 0), t, Color.white, 1f);
                fk.Bar(new Vector3(-hw, -hh, 0), new Vector3(-hw, hh, 0), t, Color.white, 1f);
                fk.Bar(new Vector3(hw, -hh, 0), new Vector3(hw, hh, 0), t, Color.white, 1f);
                var fg = fk.Spawn("WinFrame" + r, bank, new Vector3(rx, 0, -0.05f), Quaternion.identity, Palette.Neon, false);
                winFrames[r] = fg.GetComponent<Renderer>();
                fg.SetActive(false);
                if (r < 2) deco.Box(new Vector3(rx + ReelGap * 0.5f, 0, -0.042f), new Vector3(0.012f, window.y, 0.012f), Palette.Hex("F3EFF8"), 0.1f);
            }
            // pay line with arrow markers at both ends
            deco.Box(new Vector3(0, 0, -0.048f), new Vector3(window.x - 0.02f, 0.0035f, 0.002f), Palette.Red, 1f);
            for (int sx = -1; sx <= 1; sx += 2)
                deco.Box(new Vector3(sx * (window.x * 0.5f - 0.012f), 0, -0.049f), new Vector3(0.022f, 0.022f, 0.004f), Quaternion.Euler(0, 0, 45f), Palette.Red, 1f);
            deco.Spawn("PayLine", bank, Palette.Neon, false);

            // glass: transparent over the three visible rows, shading toward the drum edges, faint reflection streaks
            var glassShader = Shader.Find("ASCEND/Overlay");
            if (glassShader != null)
            {
                if (glassMat == null)
                {
                    glassMat = new Material(glassShader) { name = "m_slot_glass" };
                    glassMat.SetTexture("_BaseMap", Palette.Tex("slot_glass"));
                    glassMat.SetColor("_BaseColor", Color.white);
                    glassMat.SetFloat("_ZTest", 4f);       // LessEqual: hidden by the frame, the pay line and your hands
                    glassMat.SetFloat("_Cull", 2f);
                    glassMat.renderQueue = 3000;
                }
                var gk = new MeshKit();
                gk.Quad(new Vector3(0, 0, -0.056f), window, Quaternion.identity, Color.white, 1f);
                gk.Spawn("Glass", bank, glassMat, false);
            }

            // lever on the right side (pulls down TOWARD you)
            lever = Build.Empty("Lever", transform, leverPos).transform;
            var lv = new MeshKit();
            lv.Cylinder(Vector3.zero, 0.05f, 0.05f, 0.06f, 16, Quaternion.Euler(0, 0, 90), Palette.Hex("C9CCD8"), 0.1f);
            lv.Tube(Vector3.zero, new Vector3(0.04f, 0.42f, 0), 0.014f, 10, Palette.Silver, 0.2f);
            lv.Spawn("Arm", lever, Palette.BrushedMetal, false);
            var hk = new MeshKit(); hk.Sphere(Vector3.zero, 0.045f, 14, Palette.Red, 0.8f);
            leverHandle = hk.Spawn("Handle", lever, new Vector3(0.04f, 0.44f, 0), Quaternion.identity, Palette.Neon, false).transform;
            leverHandle.gameObject.AddComponent<SphereCollider>().radius = 0.06f;
            var hrb = leverHandle.gameObject.AddComponent<Rigidbody>(); hrb.isKinematic = true; hrb.useGravity = false;
            leverGrab = leverHandle.gameObject.AddComponent<XRGrabInteractable>();
            leverGrab.trackPosition = false; leverGrab.trackRotation = false; leverGrab.throwOnDetach = false;
            // finger tap (VR) and mouse click (desktop) pull the lever too
            leverHandle.gameObject.AddComponent<TileTouch>().onTouch = PullLever;
            Build.GlowSprite(leverHandle, Vector3.zero, 0.2f, Palette.Red, 0.6f);
            PokeButton.Create("Spin", transform, spinPos, Quaternion.identity, 0.045f, Palette.Gold, "SPIN", () => StartCoroutine(Spin()));
            // solid cabinet on a child: TableGame.FitCollider deletes BoxColliders on the root. Exactly as wide as the
            // cabinet, so it never sits in the line of sight from the stool to the lever.
            var col = Build.Empty("CabinetCollider", transform, Vector3.zero).AddComponent<BoxCollider>();
            if (pack) { col.center = new Vector3(0, 1.32f, 0.28f); col.size = new Vector3(0.86f, 2.64f, 0.76f); }
            else { col.center = new Vector3(0, 1.4f, 0.42f); col.size = new Vector3(0.86f, 2.8f, 0.6f); }
            RefreshLeds();
        }

        static Material glassMat;

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "SPIN", 20, y, 520, 100, Palette.Gold, () => StartCoroutine(Spin()), 44);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 330); } }

        static double RtpValue
        {
            get
            {
                double ev = 0;
                for (int s = 0; s < 5; s++) { double p = Weights[s] / 100.0; ev += p * p * p * Three[s] + 3 * p * p * (1 - p) * Two[s]; }
                return ev;
            }
        }

        // ---- guests playing: a seated NPC pulls the lever every few seconds (no chips of yours involved)
        float nextGuest;
        bool GuestPlaying { get { return PlayerSeat != null && PlayerSeat.occupant != null; } }

        static int Pick()
        {
            int r = Odds.Range(0, 100), acc = 0;
            for (int i = 0; i < 5; i++) { acc += Weights[i]; if (r < acc) return i; }
            return 0;
        }

        /// <summary>Pay of a result: 3 same = Three[s], any 2 same = Two[s]. mask = reels that form the win.</summary>
        static float Evaluate(int[] res, out int mask, out int sym)
        {
            mask = 0; sym = -1;
            if (res[0] == res[1] && res[1] == res[2]) { mask = 7; sym = res[0]; return Three[sym]; }
            if (res[0] == res[1]) { mask = 3; sym = res[0]; }
            else if (res[1] == res[2]) { mask = 6; sym = res[1]; }
            else if (res[0] == res[2]) { mask = 5; sym = res[0]; }
            return sym >= 0 ? Two[sym] : 0f;
        }

        string Label(int mask, int sym, float mult)
        {
            if (sym < 0) return "NO WIN";
            return (mask == 7 ? "3 × " : "2 × ") + SymbolNames[sym] + " · " + mult.ToString("0.##") + "x";
        }

        void Update()
        {
            // lever: grabbed (near or by ray) it follows how far you pull your hand DOWN / toward you since the grab;
            // past 55° it spins. (The old absolute hand angle snapped a ray grab straight to 75°, and the arm swung
            // away from the player while the hand pulled toward them.)
            if (leverGrab != null && leverGrab.isSelected && leverGrab.interactorsSelecting.Count > 0)
            {
                var hand = transform.InverseTransformPoint(leverGrab.interactorsSelecting[0].transform.position);
                if (!wasGrabbed) { wasGrabbed = true; grabStart = hand; grabAngle0 = leverAngle; }
                float pull = (grabStart.y - hand.y) + Mathf.Max(0f, grabStart.z - hand.z) * 0.5f;
                float targetAngle = Mathf.Clamp(grabAngle0 + pull / 0.28f * 75f, 0f, 75f);
                if (targetAngle > 55f && leverAngle <= 55f && !spinning) { Sfx.Play("lever_pull", lever.position, 1f); XRIn.HapticBoth(0.6f, 0.08f); StartCoroutine(Spin()); }
                leverAngle = targetAngle;
            }
            else
            {
                wasGrabbed = false;
                if (!leverAnimating) leverAngle = Mathf.MoveTowards(leverAngle, 0f, Time.deltaTime * 240f);
            }
            if (lever != null) lever.localRotation = Quaternion.Euler(-leverAngle, 0, 0);   // negative X: handle comes toward -Z (the player)
            if (lamps != null)
                for (int i = 0; i < lamps.Length; i++)
                {
                    bool on = spinning ? ((int)(Time.time * 12) + i) % 2 == 0 : winMask != 0 && winBig >= 0 ? ((int)(Time.time * 6) + i) % 2 == 0 : ((int)(Time.time * 2) + i) % 4 == 0;
                    Fx.SetColor(lamps[i], on ? (i % 2 == 0 ? Palette.Gold : Palette.Magenta) : Palette.Dark, on ? 1f : 0.1f);
                }
            if (winMask != 0 && winBig >= 0)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.time * (winBig > 0 ? 12f : 7f));
                var c = Color.Lerp(Palette.Gold, Color.white, k * 0.6f);
                for (int r = 0; r < 3; r++) if (winFrames[r] != null && (winMask & (1 << r)) != 0) Fx.SetColor(winFrames[r], c, 1f);
            }
            if (GuestPlaying && !spinning && Time.time > nextGuest)
            {
                nextGuest = Time.time + Random.Range(4.5f, 9f);
                StartCoroutine(Spin(true));
            }
            if (Time.frameCount % 10 == 0) RefreshLeds();
        }

        long ShownBet { get { return bet != null && bet.Total > 0 ? bet.Total : lastStake > 0 ? lastStake : minBet; } }

        void RefreshLeds()
        {
            if (meter != null) meter.text = "TOP PRIZE " + ((long)(ShownBet * Three[4])).ToString("N0");
            if (creditT == null) return;
            if (GuestPlaying)
            {
                creditT.text = "IN USE"; betLedT.text = "GUEST"; winT.text = spinning ? "· · ·" : lastGuest;
                return;
            }
            creditT.text = GameState.Exists ? GameState.I.chips.ToString("N0") : "0";
            betLedT.text = ShownBet.ToString("N0");
            winT.text = spinning && !spinGuest ? "· · ·" : lastWin.ToString("N0");
        }

        void ShowWin(int mask, float mult)
        {
            // a pair that returns less than the stake (0.5x / 0.95x) is a loss: it is outlined quietly, never celebrated
            winMask = mask; winBig = mult >= 15f ? 1f : mult > 1f ? 0f : -1f;
            for (int r = 0; r < 3; r++)
            {
                if (winFrames[r] == null) continue;
                bool on = (mask & (1 << r)) != 0;
                winFrames[r].gameObject.SetActive(on);
                if (on && winBig < 0) Fx.SetColor(winFrames[r], Palette.Muted, 0.6f);
            }
        }

        /// <summary>Finger tap or mouse click on the handle: the lever swings down and the reels spin.</summary>
        void PullLever()
        {
            if (spinning || leverAnimating || (leverGrab != null && leverGrab.isSelected)) return;
            StartCoroutine(LeverSwing(false));
        }

        IEnumerator LeverSwing(bool guest)
        {
            leverAnimating = true;
            Sfx.Play("lever_pull", lever.position, guest ? 0.35f : 1f);
            if (!guest) XRIn.HapticBoth(0.6f, 0.08f);
            for (float t = 0; t < 0.3f; t += Time.deltaTime) { leverAngle = Mathf.Lerp(0f, 65f, t / 0.3f); yield return null; }
            leverAngle = 65f;
            leverAnimating = false;
            if (!guest) StartCoroutine(Spin());
        }

        IEnumerator Spin() { return Spin(false); }

        IEnumerator Spin(bool guest)
        {
            if (spinning)
            {
                // a guest's spin is finishing as you take over the machine: yours starts right after it (no dead press)
                if (guest || !spinGuest || waitingForGuest) yield break;
                waitingForGuest = true;
                while (spinning) yield return null;
                waitingForGuest = false;
            }
            if (!guest && GuestPlaying) { GameState.I.Notify("THIS MACHINE IS TAKEN · SIT DOWN TO ASK FOR IT", Palette.Gold); yield break; }
            long stake = guest ? 0 : Take();       // ConsumeStake also records the "slots" mission step
            if (!guest && stake <= 0) yield break;
            spinning = true; spinGuest = guest; spinPaid = false; spinStake = stake;
            if (!guest) lastStake = stake;
            ShowWin(0, 0f);
            if (guest)
            {
                StartCoroutine(LeverSwing(true));
            }
            else if (Random.value < 0.3f) Say("lever");
            RefreshLeds();
            // the outcome is drawn first, with the published weights; each reel then stops ON that symbol
            int[] res = { Pick(), Pick(), Pick() };
            float[] from = (float[])angle.Clone();
            float[] to = new float[3];
            for (int r = 0; r < 3; r++)
            {
                // a strip position that shows the result symbol on the pay line (every symbol is printed at least once)
                int count = 0;
                for (int i = 0; i < N; i++) if (Strip[i] == res[r]) count++;
                int pick = Odds.Range(0, count), pos = 0;
                for (int i = 0; i < N; i++) if (Strip[i] == res[r] && pick-- == 0) { pos = i; break; }
                float target = -pos * Step;
                float cur = Mathf.Repeat(from[r], 360f);
                float delta = Mathf.Repeat(cur - Mathf.Repeat(target, 360f), 360f);
                to[r] = from[r] - delta - 360f * (3 + r);
            }
            spinRes = res; spinTo = to;
            spinLoop = Sfx.Loop("reel_spin_loop", transform, guest ? 0.25f : 0.6f, 1f, guest ? 8f : 25f);
            float[] dur = { 1.6f, 2.1f, 2.6f };
            bool[] stopped = new bool[3];
            float t = 0;
            while (!stopped[2])
            {
                t += Time.deltaTime;
                for (int r = 0; r < 3; r++)
                {
                    if (stopped[r]) continue;
                    float k = Mathf.Clamp01(t / dur[r]);
                    // ease out with a small settle bounce (4°, under half a symbol): never swings a symbol back across the line
                    float settle = k > 0.9f ? Mathf.Sin((k - 0.9f) / 0.1f * Mathf.PI) * 4f : 0f;
                    angle[r] = Mathf.Lerp(from[r], to[r], Fx.EaseOut(k)) - settle;
                    reels[r].localRotation = Quaternion.Euler(angle[r], 0, 0);
                    if (k >= 1f)
                    {
                        stopped[r] = true; angle[r] = Mathf.Repeat(to[r], 360f); reels[r].localRotation = Quaternion.Euler(angle[r], 0, 0);
                        Sfx.Play("reel_stop", reels[r].position, guest ? 0.35f : 1f);
                        if (!guest) XRIn.HapticBoth(0.3f, 0.04f);
                        if (!guest && res[r] >= 3) Sfx.Play("reveal_" + ((Rarity)(res[r] + 1)).ToString().ToLowerInvariant(), reels[r].position, 0.4f);
                    }
                }
                yield return null;
            }
            if (spinLoop != null) Destroy(spinLoop.gameObject);
            spinLoop = null;
            int mask, sym;
            float mult = Evaluate(res, out mask, out sym);
            string label = Label(mask, sym, mult);
            Status(label, mult > 1 ? Palette.Gold : mult > 0 ? Palette.Text : Palette.Muted);
            ShowWin(mask, mult);
            if (guest)
            {
                lastGuest = mult > 0 ? mult.ToString("0.##") + "x" : "0";
                var g = PlayerSeat.occupant;
                if (g != null) { g.Reach(true, lever.position, 0f); if (mult > 1) g.Play(mult >= 15 ? Emote.Cheer : Emote.Clap, 1.4f); else if (Random.value < 0.25f) g.Play(Emote.Facepalm, 1.2f); }
                if (mult > 1) { Sfx.Play("slot_jingle", transform.position + Vector3.up * 1.6f, 0.35f); Sfx.Play("chips_payout", transform.position + Vector3.up, 0.3f); }
                if (mult >= 15) Juice.Sparks(transform.TransformPoint(new Vector3(0, 2.3f, 0.1f)), Palette.Gold, 40);
                spinPaid = true; spinning = false;
                RefreshLeds();
                yield break;
            }
            if (mult > 1) Sfx.Play("slot_jingle", transform.position + Vector3.up * 1.6f, 0.9f);
            long payout = (long)(stake * (double)mult);
            spinPaid = true;
            lastWin = payout;
            PayDirect(payout, stake, transform.TransformPoint(new Vector3(0, 1.2f, 0.2f)));
            if (res[0] == 4 && res[1] == 4 && res[2] == 4) GameState.I.Unlock("MYTHIC LINE", "THREE TOP SYMBOLS ON THE SLOTS", 2000);
            spinning = false;
            RefreshLeds();
        }

        /// <summary>The floor is switched off mid-spin (elevator / wrist menu): the spin coroutine dies with it. Before,
        /// <c>spinning</c> stayed true forever — the machine never spun again (guest spins included, so the always-busy
        /// NPC machines were the ones that broke) and a player's stake was lost. The result was already drawn when the
        /// reels started, so the spin is settled exactly as it would have landed (no void-and-refund exploit).</summary>
        protected override void OnDisable()
        {
            base.OnDisable();
            waitingForGuest = false; leverAnimating = false; wasGrabbed = false;
            if (!spinning) return;
            StopAllCoroutines();
            if (spinLoop != null) Destroy(spinLoop.gameObject);
            spinLoop = null;
            if (spinTo != null)
                for (int r = 0; r < 3; r++)
                {
                    angle[r] = Mathf.Repeat(spinTo[r], 360f);
                    if (reels[r] != null) reels[r].localRotation = Quaternion.Euler(angle[r], 0, 0);
                }
            if (!spinGuest && !spinPaid && spinRes != null && GameState.Exists)
            {
                int mask, sym;
                float mult = Evaluate(spinRes, out mask, out sym);
                long payout = (long)(spinStake * (double)mult);
                GameState.I.Pay(payout, spinStake, transform.position);
                lastWin = payout;
                ShowWin(mask, mult);
                GameState.I.Notify("SLOTS · " + Label(mask, sym, mult) + (payout > 0 ? " · PAID " + payout.ToString("N0") : ""), mult > 1 ? Palette.Gold : Palette.Muted);
            }
            spinPaid = true; spinning = false; leverAngle = 0f;
        }
    }
}
