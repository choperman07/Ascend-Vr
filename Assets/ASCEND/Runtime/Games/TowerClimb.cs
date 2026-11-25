using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Neon Tower: climb 8 floors of a holographic tower. On each floor touch one of the doors: all are safe
    /// but one, which hides a trap. The multiplier grows every floor (0.95 × fair odds), cash out after any cleared floor.
    /// Difficulty sets the doors per floor (EASY 4 doors / 1 trap · MEDIUM 3 / 1 · HARD 2 / 1).
    /// Layout (seated player at -Z): a modest board standing on the back of the counter, reclined slightly AWAY from the
    /// player; a read-out plaque on the right; the controls flat on the counter's front-right (the left is kept free for
    /// the chip tray / side table) and the bet circle front-centre.</summary>
    public class TowerClimb : ArcadeStation
    {
        const int Floors = 8;
        static readonly int[] DoorsByMode = { 4, 3, 2 };
        static readonly string[] ModeNames = { "EASY", "MEDIUM", "HARD" };
        int mode = 1;

        // board geometry (board-local: x right, y up, front = -Z)
        static readonly Vector3 BoardPos = new Vector3(-0.08f, 0.95f + 0.05f, -0.17f);
        const float Recline = 10f;              // top leans back, away from the player
        const float RowY0 = 0.1f, RowPitch = 0.08f, TileH = 0.066f;
        const float TilesX0 = -0.285f, TilesW = 0.42f;
        const float TileZ = -0.012f;

        Transform board, climber, rowGlow;
        Renderer climberR;
        readonly Renderer[,] doorR = new Renderer[Floors, 4];
        readonly Transform[,] door = new Transform[Floors, 4];
        readonly TextMeshPro[] rowNumT = new TextMeshPro[Floors], rowMultT = new TextMeshPro[Floors];
        readonly int[] trap = new int[Floors];
        readonly int[] picked = new int[Floors];
        int level; bool playing, finished; long stake;
        TextMeshPro multT, multCapT, nextT, safeT, modeT, modeFlatT;
        PokeButton startBtn, cashBtn, easierBtn, harderBtn;
        Coroutine resetCo, tokenCo;

        int Doors { get { return DoorsByMode[mode]; } }
        protected override string Rules { get { return "PICK A DIFFICULTY, PRESS START, THEN CLIMB:\nTOUCH ONE DOOR ON THE LIT FLOOR · ONE DOOR HIDES A TRAP\nTHE MULTIPLIER GROWS WITH EVERY FLOOR (PRINTED BESIDE EACH ONE)\nEASY 4 DOORS · MEDIUM 3 · HARD 2 · ALWAYS 1 TRAP\nCASH OUT AFTER ANY CLEARED FLOOR · TOP FLOOR PAYS AUTOMATICALLY"; } }
        protected override Vector3 BetPos { get { return new Vector3(-0.08f, TableY + 0.005f, -0.345f); } }

        /// <summary>Payout multiplier after clearing n floors: 0.95 / P(survive n) — so every stopping rule returns 95%.</summary>
        float MultAt(int floorsCleared)
        {
            if (floorsCleared <= 0) return 1f;
            double p = System.Math.Pow((Doors - 1) / (double)Doors, floorsCleared);
            return (float)(0.95 / p);
        }
        static string Fmt(float m) { return (m < 10f ? m.ToString("0.00") : m < 100f ? m.ToString("0.0") : m.ToString("0")) + "x"; }

        Vector3 BL(Vector3 local) { return BoardPos + Quaternion.Euler(Recline, 0, 0) * local; }   // board-local → table-local

        protected override void BuildMachine()
        {
            var rot = Quaternion.Euler(Recline, 0, 0);
            board = Build.Empty("Tower", transform, BoardPos, rot).transform;

            // stand: plinth on the counter + two rear struts holding the reclined board
            var st = new MeshKit();
            st.Box(new Vector3(BoardPos.x, TableY + 0.0375f, -0.1f), new Vector3(0.76f, 0.015f, 0.2f), Palette.Hex("1B1328"), 0.05f);
            st.Bar(new Vector3(BoardPos.x - 0.37f, TableY + 0.046f, -0.2f), new Vector3(BoardPos.x + 0.37f, TableY + 0.046f, -0.2f), 0.006f, Accent, 1f);
            for (int s = -1; s <= 1; s += 2)
            {
                var top = BL(new Vector3(s * 0.3f, 0.55f, 0.04f));
                st.Bar(new Vector3(top.x, TableY + 0.05f, top.z + 0.02f), top, 0.022f, Palette.Metal, 0.1f);
            }
            st.Spawn("Stand", transform, Palette.Glossy, false);

            // face: dark glass plate with an accent frame
            var fr = new MeshKit();
            fr.Box(new Vector3(0, 0.36f, 0.02f), new Vector3(0.76f, 0.74f, 0.03f), Palette.Hex("0B0816"), 0.02f);
            float hx = 0.38f, y0 = -0.01f, y1 = 0.73f, fz = -0.001f;
            fr.Bar(new Vector3(-hx, y0, fz), new Vector3(hx, y0, fz), 0.01f, Accent, 1f);
            fr.Bar(new Vector3(-hx, y1, fz), new Vector3(hx, y1, fz), 0.01f, Accent, 1f);
            fr.Bar(new Vector3(-hx, y0, fz), new Vector3(-hx, y1, fz), 0.01f, Accent, 1f);
            fr.Bar(new Vector3(hx, y0, fz), new Vector3(hx, y1, fz), 0.01f, Accent, 1f);
            // column separators + the ground line the climber starts on
            fr.Bar(new Vector3(TilesX0 - 0.012f, 0.05f, fz), new Vector3(TilesX0 - 0.012f, 0.70f, fz), 0.003f, Palette.A(Accent, 1f) * 0.6f, 0.5f);
            fr.Bar(new Vector3(TilesX0 + TilesW + 0.012f, 0.05f, fz), new Vector3(TilesX0 + TilesW + 0.012f, 0.70f, fz), 0.003f, Accent * 0.6f, 0.5f);
            fr.Bar(new Vector3(TilesX0, 0.045f, fz), new Vector3(TilesX0 + TilesW, 0.045f, fz), 0.006f, Palette.Gold, 0.9f);
            fr.Spawn("Frame", board, Palette.Neon, false);

            // active-row highlight band (moves to the floor you are on)
            var hg = new MeshKit();
            hg.Quad(Vector3.zero, new Vector2(0.74f, RowPitch - 0.004f), Quaternion.identity, Accent * 0.35f, 0.55f);
            hg.Bar(new Vector3(-0.37f, -RowPitch * 0.5f + 0.002f, -0.001f), new Vector3(0.37f, -RowPitch * 0.5f + 0.002f, -0.001f), 0.004f, Accent, 1f);
            hg.Bar(new Vector3(-0.37f, RowPitch * 0.5f - 0.002f, -0.001f), new Vector3(0.37f, RowPitch * 0.5f - 0.002f, -0.001f), 0.004f, Accent, 1f);
            rowGlow = hg.Spawn("RowGlow", board, new Vector3(0, RowY0, 0.002f), Quaternion.identity, Palette.Neon, false).transform;
            rowGlow.gameObject.SetActive(false);

            for (int f = 0; f < Floors; f++)
            {
                float y = RowY0 + f * RowPitch;
                rowNumT[f] = Txt.Make("FloorNo" + (f + 1), board, new Vector3(-0.335f, y, -0.006f), (f + 1).ToString(), 0.058f, Palette.Muted, Palette.Display, TextAlignmentOptions.Center, 0.07f);
                rowMultT[f] = Txt.Make("FloorX" + (f + 1), board, new Vector3(0.255f, y, -0.006f), "", 0.054f, Palette.Muted, Palette.Display, TextAlignmentOptions.Right, 0.19f);
                for (int d = 0; d < 4; d++)
                {
                    var dk = new MeshKit();
                    dk.Box(Vector3.zero, new Vector3(0.1f, TileH, 0.02f), Color.white, 0.35f);                          // door frame
                    dk.Box(new Vector3(0, -0.003f, -0.0105f), new Vector3(0.07f, TileH - 0.018f, 0.002f), Color.white, 0.75f);   // lit panel
                    var go = dk.Spawn("Door" + f + "_" + d, board, Vector3.zero, Quaternion.identity, Palette.Neon, false);
                    door[f, d] = go.transform; doorR[f, d] = go.GetComponent<Renderer>();
                    var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(0.104f, TileH + 0.008f, 0.05f); col.center = new Vector3(0, 0, -0.01f);
                    int ff = f, dd = d;
                    go.AddComponent<TileTouch>().onTouch = () => Pick(ff, dd);
                    go.AddComponent<XRSimpleInteractable>().selectEntered.AddListener(_ => Pick(ff, dd));
                }
            }

            // the climber token
            var ck = new MeshKit();
            ck.Capsule(new Vector3(0, 0.012f, 0), new Vector3(0, 0.03f, 0), 0.0095f, 10, Color.white, 0.9f);
            ck.Sphere(new Vector3(0, 0.047f, 0), 0.0095f, 10, Color.white, 1f);
            ck.Bar(new Vector3(-0.016f, 0.036f, 0), new Vector3(0.016f, 0.036f, 0), 0.005f, Color.white, 0.9f);
            ck.Disc(new Vector3(0, 0.001f, 0), 0.016f, 16, Quaternion.identity, Color.white, 0.6f);
            var cgo = ck.Spawn("Climber", board, GroundPos, Quaternion.identity, Palette.Neon, false);
            climber = cgo.transform; climberR = cgo.GetComponent<Renderer>();
            Build.GlowSprite(climber, new Vector3(0, 0.03f, 0.004f), 0.09f, Palette.Gold, 0.5f, false);
            Fx.SetColor(climberR, Palette.Gold, 1f);

            // read-out plaque on the right, behind the controls (the host stands on the left)
            var pl = Build.Empty("Readout", transform, new Vector3(0.55f, TableY + 0.03f, -0.03f), Quaternion.Euler(8f, 0, 0)).transform;
            var pk = new MeshKit();
            pk.Box(new Vector3(0, 0.235f, 0.015f), new Vector3(0.34f, 0.43f, 0.03f), Palette.Hex("0B0816"), 0.02f);
            pk.Bar(new Vector3(-0.17f, 0.02f, -0.001f), new Vector3(0.17f, 0.02f, -0.001f), 0.008f, Accent, 1f);
            pk.Bar(new Vector3(-0.17f, 0.45f, -0.001f), new Vector3(0.17f, 0.45f, -0.001f), 0.008f, Accent, 1f);
            pk.Bar(new Vector3(-0.17f, 0.02f, -0.001f), new Vector3(-0.17f, 0.45f, -0.001f), 0.008f, Accent, 1f);
            pk.Bar(new Vector3(0.17f, 0.02f, -0.001f), new Vector3(0.17f, 0.45f, -0.001f), 0.008f, Accent, 1f);
            pk.Bar(new Vector3(-0.14f, 0.165f, -0.001f), new Vector3(0.14f, 0.165f, -0.001f), 0.003f, Accent * 0.6f, 0.5f);
            pk.Spawn("Plaque", pl, Palette.Neon, false);
            multCapT = Txt.Make("MultCap", pl, new Vector3(0, 0.405f, -0.006f), "CASH-OUT VALUE", 0.05f, Palette.Muted, Palette.Display, TextAlignmentOptions.Center, 0.3f);
            multT = Txt.Glow("Mult", pl, new Vector3(0, 0.31f, -0.006f), "1.00x", 0.12f, Palette.Text, 0.32f);
            nextT = Txt.Make("Next", pl, new Vector3(0, 0.21f, -0.006f), "", 0.05f, Palette.Text, Palette.Display, TextAlignmentOptions.Center, 0.32f);
            safeT = Txt.Make("Safe", pl, new Vector3(0, 0.12f, -0.006f), "", 0.05f, Palette.Muted, Palette.Display, TextAlignmentOptions.Center, 0.32f);
            modeT = Txt.Make("Mode", pl, new Vector3(0, 0.06f, -0.006f), "", 0.055f, Palette.Gold, Palette.Display, TextAlignmentOptions.Center, 0.32f);
            foreach (var t in new[] { multCapT, nextT, safeT, modeT }) { t.enableWordWrapping = false; t.enableAutoSizing = true; t.fontSizeMin = 0.5f; t.fontSizeMax = t.fontSize; }

            // controls: flat on the counter's front-right, within a seated arm's reach (left side stays free for the chips)
            var cp = new MeshKit();
            cp.Box(new Vector3(0.51f, TableY + 0.034f, -0.315f), new Vector3(0.4f, 0.008f, 0.3f), Palette.Hex("0E0A18"), 0.02f);
            float cy = TableY + 0.039f;
            cp.Bar(new Vector3(0.33f, cy, -0.465f), new Vector3(0.71f, cy, -0.465f), 0.006f, Accent, 1f);
            cp.Bar(new Vector3(0.33f, cy, -0.165f), new Vector3(0.71f, cy, -0.165f), 0.006f, Accent, 1f);
            cp.Bar(new Vector3(0.33f, cy, -0.465f), new Vector3(0.33f, cy, -0.165f), 0.006f, Accent, 1f);
            cp.Bar(new Vector3(0.71f, cy, -0.465f), new Vector3(0.71f, cy, -0.165f), 0.006f, Accent, 1f);
            cp.Bar(new Vector3(0.33f, cy, -0.305f), new Vector3(0.69f, cy, -0.305f), 0.003f, Accent * 0.5f, 0.4f);
            cp.Spawn("Controls", transform, Palette.Neon, false);
            float by = TableY + 0.038f;
            easierBtn = PokeButton.Create("Easier", transform, new Vector3(0.375f, by, -0.215f), Quaternion.identity, 0.028f, Palette.Teal, "EASIER", () => Mode(-1), 0.045f);
            harderBtn = PokeButton.Create("Harder", transform, new Vector3(0.645f, by, -0.215f), Quaternion.identity, 0.028f, Palette.Red, "HARDER", () => Mode(1), 0.045f);
            modeFlatT = Txt.Make("ModeFlat", transform, new Vector3(0.51f, by + 0.004f, -0.225f), "", 0.052f, Palette.Gold, Palette.Display, TextAlignmentOptions.Center, 0.13f);
            modeFlatT.transform.localRotation = Quaternion.Euler(70, 0, 0);
            startBtn = PokeButton.Create("Start", transform, new Vector3(0.405f, by, -0.37f), Quaternion.identity, 0.04f, Palette.Gold, "START", StartRound, 0.05f);
            cashBtn = PokeButton.Create("Cash", transform, new Vector3(0.615f, by, -0.37f), Quaternion.identity, 0.04f, Palette.Mint, "CASH OUT", CashOut, 0.05f);
            Mode(0);
        }

        Vector3 GroundPos { get { return new Vector3(TilesX0 + TilesW * 0.5f, 0.048f, -0.034f); } }
        Vector3 DoorPos(int f, int d)
        {
            float w = TilesW / Doors;
            return new Vector3(TilesX0 + w * (d + 0.5f), RowY0 + f * RowPitch, TileZ);
        }
        Vector3 StandOn(int f, int d) { var p = DoorPos(f, d); return new Vector3(p.x, p.y - TileH * 0.5f + 0.004f, -0.034f); }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "EASIER", 20, y, 160, 70, Palette.Teal, () => Mode(-1), 26);
            UIKit.Btn(p, "HARDER", 190, y, 160, 70, Palette.Red, () => Mode(1), 26);
            UIKit.Btn(p, "START", 360, y, 180, 70, Palette.Gold, StartRound, 30);
            UIKit.Btn(p, "CASH OUT", 20, y + 80, 520, 70, Palette.Mint, CashOut, 32);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 380); } }

        // ------------------------------------------------------------------ difficulty (between rounds only)

        void Mode(int d)
        {
            if (playing)
            {
                if (d != 0) { Sfx.UI("ui_error", 0.5f); Status("DIFFICULTY LOCKED · FINISH OR CASH OUT FIRST", Palette.Gold); }
                return;
            }
            EndReveal();
            mode = Mathf.Clamp(mode + d, 0, 2);
            modeT.text = ModeNames[mode] + " · " + Doors + " DOORS · 1 TRAP";
            modeFlatT.text = ModeNames[mode];
            Layout();
            Texts();
        }

        // ------------------------------------------------------------------ visuals

        void Layout()
        {
            float w = TilesW / Doors;
            bool live = playing || finished;   // finished: the result stays on the board until the reset
            for (int f = 0; f < Floors; f++)
            {
                for (int d = 0; d < 4; d++)
                {
                    bool on = d < Doors;
                    door[f, d].gameObject.SetActive(on);
                    if (!on) continue;
                    door[f, d].localPosition = DoorPos(f, d);
                    door[f, d].localScale = new Vector3((w - 0.012f) / 0.1f, 1f, 1f);
                    Color c; float e;
                    if (live && f < level) { bool mine = picked[f] == d; c = mine ? Palette.Mint : Palette.Violet * 0.25f; e = mine ? 0.9f : 0.12f; }
                    else if (playing && f == level) { c = Palette.Teal; e = 0.8f; }
                    else if (playing) { c = Palette.Violet * 0.45f; e = 0.25f; }
                    else { c = Palette.Violet * 0.55f; e = 0.35f; }
                    Fx.SetColor(doorR[f, d], c, e);
                }
                bool active = playing && f == level, cleared = live && f < level;
                rowNumT[f].color = active ? Palette.Text : cleared ? Palette.Mint : Palette.Muted;
                rowMultT[f].text = Fmt(MultAt(f + 1));
                rowMultT[f].color = active ? Accent : cleared ? Palette.Mint : Palette.Muted;
            }
            rowGlow.gameObject.SetActive(playing);
            if (playing) rowGlow.localPosition = new Vector3(0, RowY0 + Mathf.Min(level, Floors - 1) * RowPitch, 0.002f);
        }

        void Texts()
        {
            if (playing)
            {
                multT.text = Fmt(MultAt(level));
                multT.color = level > 0 ? Palette.Mint : Palette.Muted;
                multCapT.text = level > 0 ? "CASH-OUT VALUE" : "CLEAR FLOOR 1 TO CASH OUT";
                nextT.text = "FLOOR " + (level + 1) + " PAYS " + Fmt(MultAt(level + 1));
                safeT.text = (Doors - 1) + " SAFE DOORS · 1 TRAP";
            }
            else if (!finished)
            {
                multT.text = Fmt(MultAt(Floors));
                multT.color = Palette.Gold;
                multCapT.text = "TOP FLOOR PAYS";
                nextT.text = "PRESS START TO CLIMB";
                safeT.text = (Doors - 1) + " SAFE DOORS · 1 TRAP";
            }
            cashBtn.SetInteractable(playing && level > 0);
            startBtn.SetInteractable(!playing);
            easierBtn.SetInteractable(!playing && mode > 0);
            harderBtn.SetInteractable(!playing && mode < 2);
        }

        void PlaceToken(Vector3 local, Color c)
        {
            if (tokenCo != null) { StopCoroutine(tokenCo); tokenCo = null; }
            climber.localPosition = local; climber.localRotation = Quaternion.identity;
            Fx.SetColor(climberR, c, 1f);
        }

        IEnumerator Hop(Vector3 to, float dur)
        {
            Vector3 from = climber.localPosition;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float k = Fx.EaseInOut(t / dur);
                climber.localPosition = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.035f;
                yield return null;
            }
            climber.localPosition = to;
            tokenCo = null;
        }

        IEnumerator Fall(float dur)
        {
            Vector3 from = climber.localPosition, to = GroundPos;
            Fx.SetColor(climberR, Palette.Red, 1f);
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float k = t / dur;
                climber.localPosition = Vector3.Lerp(from, to, k * k) + Vector3.up * Mathf.Sin(Mathf.Min(1f, k * 3f) * Mathf.PI) * 0.02f;
                climber.localRotation = Quaternion.Euler(0, 0, k * 540f);
                yield return null;
            }
            climber.localPosition = to; climber.localRotation = Quaternion.Euler(0, 0, 90f);
            tokenCo = null;
        }

        void MoveToken(IEnumerator routine)
        {
            if (tokenCo != null) StopCoroutine(tokenCo);
            tokenCo = isActiveAndEnabled ? StartCoroutine(routine) : null;
        }

        // ------------------------------------------------------------------ round

        void StartRound()
        {
            if (playing) return;
            EndReveal();
            stake = Take();
            if (stake <= 0) { Status("PLACE A BET ON THE CIRCLE", Palette.Gold); return; }
            playing = true; finished = false; level = 0;
            for (int f = 0; f < Floors; f++) { trap[f] = Odds.Range(0, Doors); picked[f] = -1; }
            PlaceToken(GroundPos, Palette.Gold);
            Layout(); Texts();
            Status("CLIMB! TOUCH A DOOR ON FLOOR 1", Palette.Teal);
            Say("tower");
        }

        void Pick(int f, int d)
        {
            if (!playing)
            {
                if (!finished) { Sfx.UI("ui_error", 0.3f); Status("PRESS START TO CLIMB", Palette.Gold); }
                return;
            }
            if (f != level || d >= Doors) return;   // only the lit floor can be picked
            if (d == trap[f])
            {
                playing = false; finished = true;
                Layout();
                RevealTraps(f);
                Fx.SetColor(doorR[f, d], Palette.Red, 1f);
                Sfx.Play("mine_boom", door[f, d].position, 0.9f);
                StartCoroutine(Fx.Pulse(door[f, d], 1.25f, 0.3f));
                MoveToken(Fall(0.6f));
                Status("TRAPPED ON FLOOR " + (f + 1) + " · STAKE LOST", Palette.Red);
                multT.text = "TRAP"; multT.color = Palette.Red;
                multCapT.text = "FLOOR " + (f + 1);
                nextT.text = "PRESS START TO CLIMB AGAIN";
                PayDirect(0, stake, door[f, d].position);
                Texts();
                resetCo = StartCoroutine(ResetLater());
                return;
            }
            picked[f] = d;
            level++;
            Sfx.Play("gem", door[f, d].position, 0.8f, 1f + f * 0.06f);
            XRIn.HapticBoth(0.25f, 0.04f);
            MoveToken(Hop(StandOn(f, d), 0.32f));
            if (level >= Floors) { Layout(); CashOut(); return; }
            Layout(); Texts();
            Status("FLOOR " + level + " CLEARED · " + Fmt(MultAt(level)) + " · NEXT " + Fmt(MultAt(level + 1)), Palette.Mint);
        }

        void CashOut()
        {
            if (!playing) return;
            if (level == 0) { Sfx.UI("ui_error", 0.4f); Status("CLEAR FLOOR 1 BEFORE CASHING OUT", Palette.Gold); return; }
            playing = false; finished = true;
            float m = MultAt(level);
            bool top = level >= Floors;
            RevealTraps(level);
            Status((top ? "TOP OF THE TOWER! " : "CASHED OUT ") + Fmt(m), Palette.Gold);
            Say("cashout");
            multT.text = Fmt(m); multT.color = Palette.Gold;
            multCapT.text = top ? "TOP FLOOR · PAID" : "PAID AT FLOOR " + level;
            nextT.text = "PRESS START TO CLIMB AGAIN";
            Fx.SetColor(climberR, Palette.Mint, 1f);
            if (climber != null) StartCoroutine(Fx.Pulse(climber, 1.6f, 0.5f));
            PayDirect((long)(stake * (double)m), stake, board.position + board.up * 0.4f);
            Texts();
            resetCo = StartCoroutine(ResetLater());
        }

        /// <summary>Honest reveal after a round: shows where the traps were on the floors not yet climbed.</summary>
        void RevealTraps(int fromFloor)
        {
            for (int f = fromFloor; f < Floors; f++)
                if (trap[f] < Doors) Fx.SetColor(doorR[f, trap[f]], Palette.Red * 0.6f, 0.45f);
            rowGlow.gameObject.SetActive(false);
        }

        IEnumerator ResetLater()
        {
            yield return new WaitForSeconds(2.2f);
            resetCo = null;
            EndReveal();
        }

        /// <summary>Back to the idle board (called by the reset timer, by START and by a difficulty change).</summary>
        void EndReveal()
        {
            if (resetCo != null) { StopCoroutine(resetCo); resetCo = null; }
            if (!finished) return;
            finished = false; level = 0;
            if (climber != null) PlaceToken(GroundPos, Palette.Gold);
            Layout(); Texts();
        }

        /// <summary>The floor is switched off (lift / wrist menu / escort): coroutines die and the player is gone.
        /// A climb with cleared floors is cashed out at its current value; a climb with nothing cleared is refunded.
        /// Either way the board comes back idle.</summary>
        protected override void OnDisable()
        {
            base.OnDisable();
            if (board == null || !GameState.Exists) return;
            if (playing)
            {
                playing = false;
                if (level > 0)
                {
                    long pay = (long)(stake * (double)MultAt(level));
                    GameState.I.Pay(pay, stake, transform.position);
                    GameState.I.Notify("NEON TOWER · AUTO CASH-OUT " + Fmt(MultAt(level)) + " · " + pay.ToString("N0"), Palette.Gold);
                }
                else if (stake > 0)
                {
                    GameState.I.Pay(stake, stake, transform.position);
                    GameState.I.Notify("NEON TOWER · ROUND VOID · " + stake.ToString("N0") + " RETURNED", Palette.Gold);
                }
                stake = 0;
            }
            resetCo = null; tokenCo = null;   // coroutines are stopped with the object
            finished = false; level = 0;
            PlaceToken(GroundPos, Palette.Gold);
            Layout(); Texts();
        }
    }
}
