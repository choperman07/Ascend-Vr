using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Mines: touch tiles with your finger to reveal gems; each gem raises the multiplier, a mine ends it.
    /// Multiplier after k gems = 0.95 × fair odds → RTP 95% whenever you stop.</summary>
    public class Mines : ArcadeStation
    {
        static readonly int[] MineSteps = { 1, 3, 5, 10, 20 };
        int mineIdx = 1;
        readonly Transform[] tiles = new Transform[25];
        readonly Renderer[] tileR = new Renderer[25];
        readonly bool[] mine = new bool[25], open = new bool[25];
        TextMeshPro multT, minesT, nextT;
        PokeButton startBtn, cashBtn;
        bool playing; int found; long stake;
        Transform board;

        int MineCount { get { return MineSteps[mineIdx]; } }
        protected override string Rules { get { return "PICK THE NUMBER OF MINES, START,\nTHEN TOUCH TILES TO FIND GEMS\nEVERY GEM RAISES THE MULTIPLIER\nCASH OUT ANY TIME"; } }

        protected override void BuildMachine()
        {
            board = Build.Empty("Board", transform, new Vector3(0, TableY + 0.42f, 0.18f), Quaternion.Euler(-62, 0, 0)).transform;   // raised and upright: easy to reach seated or standing
            var k = new MeshKit();
            k.Box(new Vector3(0, -0.02f, 0), new Vector3(0.78f, 0.03f, 0.78f), Palette.Hex("0C0916"));
            k.Bar(new Vector3(-0.39f, 0, -0.39f), new Vector3(0.39f, 0, -0.39f), 0.012f, Accent, 1f);
            k.Bar(new Vector3(-0.39f, 0, 0.39f), new Vector3(0.39f, 0, 0.39f), 0.012f, Accent, 1f);
            k.Bar(new Vector3(-0.39f, 0, -0.39f), new Vector3(-0.39f, 0, 0.39f), 0.012f, Accent, 1f);
            k.Bar(new Vector3(0.39f, 0, -0.39f), new Vector3(0.39f, 0, 0.39f), 0.012f, Accent, 1f);
            k.Spawn("Frame", board, Palette.Neon, false);
            var support = new MeshKit(); support.Box(new Vector3(0, TableY + 0.1f, 0.34f), new Vector3(0.7f, 0.5f, 0.22f), Palette.Dark);
            support.Spawn("Support", transform, Palette.Neon, false);
            for (int i = 0; i < 25; i++)
            {
                var tk = new MeshKit();
                tk.Box(Vector3.zero, new Vector3(0.125f, 0.03f, 0.125f), Color.white, 0.35f);
                tk.Box(new Vector3(0, 0.016f, 0), new Vector3(0.09f, 0.004f, 0.09f), Color.white, 0.6f);
                var go = tk.Spawn("Tile" + i, board, new Vector3(-0.3f + (i % 5) * 0.15f, 0.015f, 0.3f - (i / 5) * 0.15f), Quaternion.identity, Palette.Neon, false);
                tiles[i] = go.transform; tileR[i] = go.GetComponent<Renderer>();
                Fx.SetColor(tileR[i], Palette.Violet * 0.6f, 0.4f);
                var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(0.13f, 0.06f, 0.13f);
                int idx = i;
                go.AddComponent<TileTouch>().onTouch = () => Reveal(idx);
                var si = go.AddComponent<XRSimpleInteractable>(); si.selectEntered.AddListener(_ => Reveal(idx));
            }
            multT = Txt.Glow("Mult", transform, new Vector3(0, 2.0f, 0.55f), "1.00x", 0.22f, Palette.Text);
            nextT = Txt.Make("Next", transform, new Vector3(0, 1.8f, 0.55f), "", 0.06f, Palette.Muted, Palette.Mono);
            // -/+ sit left of the chip tray (the + used to be under the tray's chip stacks: unpressable)
            minesT = Txt.Make("Mines", transform, new Vector3(-0.61f, TableY + 0.12f, -0.3f), "", 0.05f, Palette.Red, Palette.Display);
            PokeButton.Create("Less", transform, new Vector3(-0.67f, TableY + 0.035f, -0.25f), Quaternion.identity, 0.025f, Palette.Red, "-", () => Step(-1));
            PokeButton.Create("More", transform, new Vector3(-0.56f, TableY + 0.035f, -0.25f), Quaternion.identity, 0.025f, Palette.Red, "+", () => Step(1));
            startBtn = PokeButton.Create("Start", transform, new Vector3(0.38f, TableY + 0.035f, -0.32f), Quaternion.identity, 0.045f, Palette.Gold, "START", StartRound);
            cashBtn = PokeButton.Create("Cash", transform, new Vector3(0.6f, TableY + 0.035f, -0.25f), Quaternion.identity, 0.05f, Palette.Mint, "CASH OUT", CashOut);
            cashBtn.SetInteractable(false);
            Step(0);
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "MINES -", 20, y, 160, 70, Palette.Red, () => Step(-1), 28);
            UIKit.Btn(p, "MINES +", 190, y, 160, 70, Palette.Red, () => Step(1), 28);
            UIKit.Btn(p, "START", 360, y, 180, 70, Palette.Gold, StartRound, 30);
            UIKit.Btn(p, "CASH OUT", 20, y + 80, 520, 70, Palette.Mint, CashOut, 32);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 380); } }

        void Step(int d)
        {
            if (playing) return;
            mineIdx = Mathf.Clamp(mineIdx + d, 0, MineSteps.Length - 1);
            minesT.text = MineCount + " MINES";
            UpdateTexts();
        }

        float MultAfter(int k)
        {
            double m = 0.95;
            for (int i = 0; i < k; i++) m *= (25.0 - i) / (25.0 - MineCount - i);
            return (float)m;
        }

        void UpdateTexts()
        {
            multT.text = MultAfter(found).ToString("0.00") + "x";
            int left = 25 - found;
            nextT.text = "NEXT GEM " + MultAfter(found + 1).ToString("0.00") + "x";
        }

        void StartRound()
        {
            if (playing) return;
            stake = Take();
            if (stake <= 0) return;
            playing = true; found = 0;
            for (int i = 0; i < 25; i++) { mine[i] = false; open[i] = false; tiles[i].localScale = Vector3.one; Fx.SetColor(tileR[i], Palette.Violet * 0.6f, 0.4f); }
            int placed = 0;
            while (placed < MineCount) { int r = Odds.Range(0, 25); if (!mine[r]) { mine[r] = true; placed++; } }
            startBtn.SetInteractable(false); cashBtn.SetInteractable(false);
            Status("TOUCH A TILE", Palette.Text); Say("careful");
            UpdateTexts();
        }

        void Reveal(int i)
        {
            if (!playing || open[i]) return;
            open[i] = true;
            var pos = tiles[i].position;
            if (mine[i])
            {
                Fx.SetColor(tileR[i], Palette.Red, 1f);
                Sfx.Play("mine_boom", pos, 1f);
                Juice.Sparks(pos, Palette.Red, 50);
                StartCoroutine(Fx.Shake(board, 0.02f, 0.4f));
                XRIn.HapticBoth(0.9f, 0.3f);
                Say("boom");
                End(0, "MINE · NO WIN");
                return;
            }
            found++;
            Fx.SetColor(tileR[i], Palette.Mint, 1f);
            StartCoroutine(Fx.Pulse(tiles[i], 1.25f, 0.25f));
            Sfx.Play("gem", pos, 0.9f, 1f + found * 0.04f);
            Juice.Sparks(pos, Palette.Mint, 14);
            XRIn.HapticBoth(0.25f, 0.05f);
            cashBtn.SetInteractable(true);
            UpdateTexts();
            if (found == 25 - MineCount) CashOut();
        }

        void CashOut()
        {
            if (!playing || found == 0) return;
            long pay = (long)(stake * (double)MultAfter(found));
            End(pay, found + " GEMS · " + MultAfter(found).ToString("0.00") + "x");
        }

        void End(long pay, string msg)
        {
            playing = false;
            for (int i = 0; i < 25; i++) if (mine[i] && !open[i]) Fx.SetColor(tileR[i], Palette.Red * 0.5f, 0.5f);
            Status(msg, pay > stake ? Palette.Mint : Palette.Muted);   // a cash-out under 1x (1 gem with 1 mine) is not a win
            PayDirect(pay, stake, board.position + Vector3.up * 0.2f);
            startBtn.SetInteractable(true); cashBtn.SetInteractable(false);
        }
    }
}
