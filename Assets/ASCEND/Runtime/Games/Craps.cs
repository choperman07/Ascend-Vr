using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Craps with a stickman: PASS / DON'T PASS with a real point (the puck), FIELD, BIG 6 / BIG 8 and the
    /// one-roll props ANY 7, ANY CRAPS and YO 11. Grab both dice and throw them down the table (or press ROLL).
    /// Bets that are not decided stay on the felt for the next roll, like a real game.</summary>
    public class Craps : TableGame
    {
        // Tub interior: x ±1.36, z -0.42..0.92. The left 1.92 m is the printed layout on a riser that leans toward the
        // player (hinged on its near edge, far edge raised); the right 0.69 m is the flat dice pit where the dice land.
        // One felt print covers both: the board uses u 0..BoardU of the texture, the pit the rest.
        const float BoardX0 = -1.30f, BoardX1 = 0.62f, BoardNear = -0.40f, BoardDepth = 1.25f, Tilt = 10f;
        const float PitX0 = 0.61f, PitX1 = 1.30f, PitZ0 = -0.40f, PitZ1 = 0.90f, BoardU = 0.74f;
        Transform board;
        BetSpot pass, dontPass, field, big6, big8, any7, anyCraps, yo11;
        readonly Dictionary<BetSpot, long> committed = new Dictionary<BetSpot, long>();
        Transform dieA, dieB, puck;
        XRGrabInteractable grabA, grabB;
        Vector3 homeA, homeB;
        TextMeshPro rollT, pointT;
        PokeButton rollBtn;
        int point;
        bool rolling;
        readonly List<int> history = new List<int>();

        public override float TableY { get { return 0.92f; } }
        // every RTP computed for these exact rules; commas instead of " · " (the rules card makes each " · " a new line)
        protected override string Rules { get { return "PASS 2x: COME-OUT 7/11 WINS, 2/3/12 LOSES\nANY OTHER TOTAL IS THE POINT: HIT IT AGAIN BEFORE A 7\nDON'T PASS 2x: THE OPPOSITE, 12 PUSHES\nLINE BETS ARE MADE ON THE COME-OUT AND STAY UNTIL DECIDED\nFIELD 2x, 2 AND 12 PAY 3x\nBIG 6 / BIG 8 2x IF IT ROLLS BEFORE A 7\nANY 7 5x, ANY CRAPS 8x, YO 11 16x"; } }
        protected override Vector3 DealerPos { get { return new Vector3(0.3f, 0, 1.15f); } }
        protected override Vector3 TrayPos { get { return new Vector3(-0.75f, TableY + 0.03f, -0.52f); } }
        protected override Vector3 PanelPos { get { return new Vector3(0.75f, TableY + 0.1f, -0.75f); } }
        protected override Vector3 StatusPos { get { return new Vector3(0, 2.3f, 1.05f); } }
        protected override BetSpot DefaultSpot { get { return pass; } }
        protected override List<Vector4> SeatSpots
        {
            get { return new List<Vector4> { new Vector4(-1.6f, 0, -0.95f, 10f), new Vector4(-0.85f, 0, -1.15f, 0f), new Vector4(0.9f, 0, -1.15f, 0f), new Vector4(1.65f, 0, -0.95f, -10f) }; }
        }
        protected override Seat.Style SeatStyle { get { return Seat.Style.BarStool; } }

        /// <summary>Board print fractions (u across the board, v from its far edge) → board local (hinge on the near edge, +Z up the slope).</summary>
        Vector3 Cell(float u0, float v0, float u1, float v1) { float u = (u0 + u1) * 0.5f, v = (v0 + v1) * 0.5f; return new Vector3(BoardX0 + u * (BoardX1 - BoardX0), 0.004f, (1f - v) * BoardDepth); }
        Vector2 Size(float u0, float v0, float u1, float v1) { return new Vector2((u1 - u0) * (BoardX1 - BoardX0) * 0.94f, (v1 - v0) * BoardDepth * 0.9f); }
        BetSpot Spot(string id, float pays, float u0, float v0, float u1, float v1)
        {
            var s = BetSpot.Create(this, id, pays, board, Cell(u0, v0, u1, v1), Size(u0, v0, u1, v1));
            spots.Add(s); committed[s] = 0; return s;
        }

        protected override void BuildTable()
        {
            // the tub: high padded rail all around, mirror-black inner wall, green felt
            var body = new MeshKit { boxUV = 1.2f };
            body.Box(new Vector3(0, TableY * 0.5f - 0.04f, 0.25f), new Vector3(2.9f, TableY - 0.08f, 1.55f), Palette.Hex("1A1210"), 0f);
            body.Spawn("Body", transform, Palette.Wood);
            // padded walls: the player's rail stays low; the back wall is tall like a real craps tub and the side walls rise
            // toward it, always above the raised layout
            var rail = new MeshKit { boxUV = 2f };
            rail.Box(new Vector3(0, TableY + 0.07f, -0.48f), new Vector3(2.95f, 0.14f, 0.12f), Color.white, 0f);
            rail.Box(new Vector3(0, TableY + 0.13f, 0.98f), new Vector3(2.95f, 0.26f, 0.12f), Color.white, 0f);
            float wallSlope = Mathf.Atan2(0.12f, 1.46f) * Mathf.Rad2Deg;   // top 0.14 at the player's end, 0.26 at the back
            rail.Box(new Vector3(-1.42f, TableY + 0.05f, 0.25f), new Vector3(0.12f, 0.30f, 1.55f), Quaternion.Euler(-wallSlope, 0, 0), Color.white, 0f);
            rail.Box(new Vector3(1.42f, TableY + 0.05f, 0.25f), new Vector3(0.12f, 0.30f, 1.55f), Quaternion.Euler(-wallSlope, 0, 0), Color.white, 0f);
            rail.Spawn("Rail", transform, Palette.Leather);
            var neon = new MeshKit();
            neon.Bar(new Vector3(-1.48f, TableY + 0.15f, -0.55f), new Vector3(1.48f, TableY + 0.15f, -0.55f), 0.012f, Palette.Gold, 1f);
            neon.Bar(new Vector3(-1.45f, 0.05f, -0.42f), new Vector3(1.45f, 0.05f, -0.42f), 0.02f, Accent, 1f);
            // chip rack groove along the player rail
            neon.Box(new Vector3(0, TableY + 0.12f, -0.52f), new Vector3(2.7f, 0.02f, 0.06f), Palette.Hex("0B0812"), 0f);
            neon.Spawn("Trim", transform, Palette.Neon, false);
            // dark deck under everything (no gaps into the tub between the board, the pit and the walls)
            neon.Clear();
            neon.Box(new Vector3(0, TableY - 0.006f, 0.25f), new Vector3(2.72f, 0.008f, 1.34f), Palette.Hex("0B0812"), 0f);
            neon.Spawn("Deck", transform, Palette.Neon, false);
            // the layout on its riser, leaning toward the player
            board = RaisedBoard.Create(transform, new Vector3(0, TableY, BoardNear), Tilt);
            RaisedBoard.Riser(transform, board, BoardX0, BoardX1, BoardDepth, Tilt, Palette.Lacquer, Palette.Hex("1A1210"), Palette.Gold);
            var felt = new MeshKit();
            felt.Plane(new Vector3((BoardX0 + BoardX1) * 0.5f, 0.001f, BoardDepth * 0.5f), new Vector2(BoardX1 - BoardX0, BoardDepth), Quaternion.identity, Color.white, 0.12f, new Rect(0, 0, BoardU, 1));
            felt.Spawn("Felt", board, Palette.Textured("felt_craps"), false);
            // the flat dice pit on the right
            var pit = new MeshKit();
            pit.Plane(new Vector3((PitX0 + PitX1) * 0.5f, TableY + 0.001f, (PitZ0 + PitZ1) * 0.5f), new Vector2(PitX1 - PitX0, PitZ1 - PitZ0), Quaternion.identity, Color.white, 0.12f, new Rect(BoardU, 0, 1f - BoardU, 1));
            pit.Spawn("Pit", transform, Palette.Textured("felt_craps"), false);
            var col = gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0, 0.5f, 0.25f); col.size = new Vector3(3.0f, 1.0f, 1.65f);

            // cells = the boxes printed by v4gen/tables4.py (board fractions)
            pass = Spot("PASS", 2f, 0.02f, 0.65f, 0.73f, 0.95f);
            dontPass = Spot("DONT PASS", 2f, 0.02f, 0.53f, 0.73f, 0.62f);
            field = Spot("FIELD", 2f, 0.02f, 0.28f, 0.73f, 0.50f);
            big6 = Spot("BIG 6", 2f, 0.76f, 0.65f, 0.87f, 0.95f);
            big8 = Spot("BIG 8", 2f, 0.87f, 0.65f, 0.98f, 0.95f);
            any7 = Spot("ANY 7", 5f, 0.76f, 0.09f, 0.98f, 0.25f);
            anyCraps = Spot("ANY CRAPS", 8f, 0.76f, 0.27f, 0.98f, 0.43f);
            yo11 = Spot("YO 11", 16f, 0.76f, 0.45f, 0.98f, 0.61f);

            homeA = new Vector3(-0.2f, TableY + 0.18f, -0.5f); homeB = new Vector3(-0.1f, TableY + 0.18f, -0.5f);
            dieA = MakeDie("DieA", homeA, out grabA); dieB = MakeDie("DieB", homeB, out grabB);
            var pk = new MeshKit();
            pk.Cylinder(Vector3.zero, 0.05f, 0.05f, 0.012f, 24, Quaternion.identity, Palette.Text, 0.6f);
            pk.Torus(new Vector3(0, 0.006f, 0), 0.05f, 0.004f, 24, 4, Quaternion.identity, Palette.Dark, 0f);
            puck = pk.Spawn("Puck", board, Palette.Neon, false).transform;   // lies on the tilted point boxes
            pointT = Txt.Make("PuckText", puck, new Vector3(0, 0.008f, 0), "OFF", 0.03f, Palette.Dark, Palette.Display);
            pointT.transform.localRotation = Quaternion.Euler(90, 0, 0);
            PlacePuck();
            rollT = Txt.Glow("Roll", transform, new Vector3((PitX0 + PitX1) * 0.5f, 1.45f, 0.8f), "", 0.2f, Palette.Text);   // over the dice pit
            rollBtn = PokeButton.Create("Roll", transform, new Vector3(0.55f, TableY + 0.17f, -0.55f), Quaternion.identity, 0.05f, Palette.Gold, "ROLL", () => StartCoroutine(Roll()));
            Build.Beam(transform, new Vector3(0, 3.6f, 0.25f), Quaternion.identity, 3.0f, 1.6f, Accent, 0.12f);
        }

        Transform MakeDie(string n, Vector3 pos, out XRGrabInteractable grab)
        {
            var k = new MeshKit();
            k.Box(Vector3.zero, Vector3.one * 0.05f, Palette.Hex("D8122E"), 0.15f);
            float h = 0.026f;
            Vector3[] faces = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            int[] count = { 1, 6, 3, 4, 2, 5 };
            for (int f = 0; f < 6; f++)
            {
                var nrm = faces[f]; var tA = Vector3.Cross(nrm, Mathf.Abs(nrm.y) > 0.5f ? Vector3.right : Vector3.up).normalized; var tB = Vector3.Cross(nrm, tA);
                foreach (var o in Pips(count[f])) k.Box(nrm * h + (tA * o.x + tB * o.y) * 0.013f, Vector3.one * 0.008f, Palette.Text, 1f);
            }
            var go = k.Spawn(n, transform, pos, Quaternion.identity, Palette.Glossy, false);
            go.AddComponent<BoxCollider>().size = Vector3.one * 0.05f;
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            grab = go.AddComponent<XRGrabInteractable>();
            grab.throwOnDetach = false;
            grab.selectExited.AddListener(a =>
            {
                // a throw down the table rolls the dice
                var hand = a.interactorObject != null ? a.interactorObject.transform : null;
                float speed = hand != null ? VelocityTracker.On(hand).velocity.magnitude : 0f;
                if (speed > 0.9f && !rolling) StartCoroutine(Roll());
                else StartCoroutine(Home());
            });
            return go.transform;
        }

        static Vector2[] Pips(int n)
        {
            switch (n)
            {
                case 1: return new[] { Vector2.zero };
                case 2: return new[] { new Vector2(-1, -1), new Vector2(1, 1) };
                case 3: return new[] { new Vector2(-1, -1), Vector2.zero, new Vector2(1, 1) };
                case 4: return new[] { new Vector2(-1, -1), new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1) };
                case 5: return new[] { new Vector2(-1, -1), new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), Vector2.zero };
                default: return new[] { new Vector2(-1, -1), new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, 0), new Vector2(1, 0) };
            }
        }

        static Quaternion FaceUp(int n)
        {
            switch (n)
            {
                case 1: return Quaternion.identity;
                case 6: return Quaternion.Euler(180, 0, 0);
                case 3: return Quaternion.Euler(0, 0, 90);
                case 4: return Quaternion.Euler(0, 0, -90);
                case 2: return Quaternion.Euler(-90, 0, 0);
                default: return Quaternion.Euler(90, 0, 0);
            }
        }

        IEnumerator Home()
        {
            yield return new WaitForSeconds(0.15f);
            if (rolling) yield break;
            if (!grabA.isSelected) { dieA.localPosition = homeA; dieA.localRotation = Quaternion.identity; }
            if (!grabB.isSelected) { dieB.localPosition = homeB; dieB.localRotation = Quaternion.identity; }
        }

        void PlacePuck()
        {
            // ON: in the middle of the point box (print centres 0.08 + i * 0.118, v 0.03..0.17); OFF: at the left end of the
            // CRAPS line, next to the "4"
            int[] pts = { 4, 5, 6, 8, 9, 10 };
            int idx = System.Array.IndexOf(pts, point);
            float pu = idx >= 0 ? 0.08f + idx * 0.118f : 0.035f, pv = idx >= 0 ? 0.10f : 0.22f;
            puck.localPosition = Cell(pu, pv, pu, pv) + Vector3.up * 0.006f;
            puck.localRotation = Quaternion.identity;
            pointT.text = point > 0 ? "ON" : "OFF";
            Fx.SetColor(puck.GetComponent<Renderer>(), point > 0 ? Palette.Text : Palette.Hex("15111F"), point > 0 ? 0.8f : 0.1f);
            pointT.color = point > 0 ? Palette.Dark : Palette.Text;
        }

        public override bool CanBetOn(BetSpot s)
        {
            // no line bets (and no adding to them) once a point is on: a don't-pass added after the point is a big player edge
            if ((s == pass || s == dontPass) && point != 0) { GameState.I.Notify("LINE BETS OPEN ON THE COME-OUT ROLL", Palette.Gold); return false; }
            return true;
        }

        protected override void BuildPanel()
        {
            base.BuildPanel();
            // the base CLEAR refunds every spot, locked or not: here it must leave the pass line (a contract bet) alone
            foreach (var b in panel.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (b.name != "Btn_CLEAR") continue;
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() => { Sfx.UI("ui_click", 0.6f); XRIn.HapticBoth(0.25f, 0.03f); ClearUnlocked(); });
            }
        }

        void ClearUnlocked()
        {
            if (!AcceptingBets || rolling) return;
            foreach (var s in spots) if (!s.locked && s.Total > 0) { s.Refund(); committed[s] = 0; }
        }

        /// <summary>Between rolls only the pass line stays locked while a point is on (it cannot be taken down).</summary>
        void LockContracts()
        {
            foreach (var s in spots) s.Lock(false);
            if (point != 0 && pass.Total > 0) pass.Lock(true);
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Label(p, "QUICK BETS GO ON THE PASS LINE · THROW THE DICE", 20, y, 520, 30, 18, Palette.Muted, Palette.Mono);
            UIKit.Btn(p, "FIELD +" + minBet, 20, y + 36, 170, 56, Palette.Teal, () => field.AddValue(minBet, W(TrayPos)), 22);
            UIKit.Btn(p, "ANY 7 +" + minBet, 200, y + 36, 160, 56, Palette.Red, () => any7.AddValue(minBet, W(TrayPos)), 22);
            UIKit.Btn(p, "DON'T +" + minBet, 370, y + 36, 170, 56, Palette.Violet, () => dontPass.AddValue(minBet, W(TrayPos)), 22);
            UIKit.Btn(p, "ROLL", 20, y + 100, 520, 84, Palette.Gold, () => StartCoroutine(Roll()), 40);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 410); } }

        IEnumerator Roll()
        {
            if (rolling) yield break;
            bool any = false; foreach (var s in spots) if (s.Total > 0) any = true;
            if (!any) { GameState.I.Notify("PUT CHIPS ON THE PASS LINE FIRST", Palette.Gold); StartCoroutine(Home()); yield break; }
            if (!CommitNew(committed)) { StartCoroutine(Home()); yield break; }
            rolling = true; running = true; AcceptingBets = false;
            foreach (var s in spots) s.Lock(true);
            rollBtn.SetInteractable(false);
            grabA.enabled = grabB.enabled = false;
            Status(point == 0 ? "COMING OUT!" : "POINT IS " + point, Palette.Gold);
            Say(point == 0 ? "craps_comeout" : "craps_roll");
            int a = Odds.Range(1, 7), b = Odds.Range(1, 7), total = a + b;
            Sfx.Play("dice_roll", W(new Vector3(0.95f, TableY, 0.4f)), 1f);
            Vector3 fromA = dieA.localPosition, fromB = dieB.localPosition;
            // the dice land flat in the pit (x 0.61..1.30), never on the tilted layout
            Vector3 toA = new Vector3(0.86f + Random.Range(-0.04f, 0.04f), TableY + 0.026f, 0.42f + Random.Range(-0.12f, 0.12f));
            Vector3 toB = toA + new Vector3(Random.Range(0.09f, 0.14f), 0, Random.Range(-0.08f, 0.08f));
            Quaternion endA = Quaternion.Euler(0, Random.Range(0, 360f), 0) * FaceUp(a), endB = Quaternion.Euler(0, Random.Range(0, 360f), 0) * FaceUp(b);
            float dur = 1.3f;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float k = t / dur, e = Fx.EaseOut(k);
                float hop = Mathf.Abs(Mathf.Sin(k * Mathf.PI * 4f)) * 0.16f * (1f - k);
                float arc = Mathf.Sin(e * Mathf.PI) * 0.28f;   // lob over the raised layout into the pit
                dieA.localPosition = Vector3.Lerp(fromA, toA, e) + Vector3.up * (hop + arc);
                dieB.localPosition = Vector3.Lerp(fromB, toB, e) + Vector3.up * (hop * 0.85f + arc);
                if (k < 0.82f) { dieA.Rotate(new Vector3(720, 380, 240) * Time.deltaTime * (1 - k), Space.Self); dieB.Rotate(new Vector3(-540, 620, 300) * Time.deltaTime * (1 - k), Space.Self); }
                else { dieA.localRotation = Quaternion.Slerp(dieA.localRotation, endA, 0.35f); dieB.localRotation = Quaternion.Slerp(dieB.localRotation, endB, 0.35f); }
                if (k > 0.45f && k - Time.deltaTime / dur <= 0.45f) Sfx.Play("dice_land", dieA.position, 0.8f);
                yield return null;
            }
            dieA.localRotation = endA; dieB.localRotation = endB;
            Sfx.Play("dice_land", dieA.position, 1f);
            history.Insert(0, total); if (history.Count > 10) history.RemoveAt(10);
            rollT.text = a + " + " + b + " = <size=150%>" + total + "</size>";

            var res = new Dictionary<BetSpot, float>();
            // one-roll bets
            res[field] = (total == 2 || total == 12) ? 3f : (total == 3 || total == 4 || total == 9 || total == 10 || total == 11) ? 2f : 0f;
            res[any7] = total == 7 ? 5f : 0f;
            res[anyCraps] = (total == 2 || total == 3 || total == 12) ? 8f : 0f;
            res[yo11] = total == 11 ? 16f : 0f;
            if (total == 6) res[big6] = 2f; if (total == 8) res[big8] = 2f;
            if (total == 7) { res[big6] = 0f; res[big8] = 0f; }
            string msg, call = null;
            if (point == 0)
            {
                if (total == 7 || total == 11) { res[pass] = 2f; res[dontPass] = 0f; msg = "NATURAL " + total + " · PASS WINS"; call = total == 7 ? "craps_natural" : "craps_yo"; }
                else if (total == 2 || total == 3) { res[pass] = 0f; res[dontPass] = 2f; msg = "CRAPS " + total + " · DON'T PASS WINS"; call = "craps_craps"; }
                else if (total == 12) { res[pass] = 0f; res[dontPass] = 1f; msg = "CRAPS 12 · DON'T PASS PUSHES"; call = "craps_craps"; }
                else { point = total; msg = "THE POINT IS " + point; Sfx.Play("chip_place", puck.position, 0.8f); call = "craps_point"; }
            }
            else
            {
                if (total == point) { res[pass] = 2f; res[dontPass] = 0f; msg = "POINT " + point + " HIT · PASS WINS"; point = 0; call = "craps_made"; }
                else if (total == 7) { res[pass] = 0f; res[dontPass] = 2f; msg = "SEVEN OUT · DON'T PASS WINS"; point = 0; call = "craps_sevenout"; }
                else { msg = total + " · POINT IS STILL " + point; if (total == 11) call = "craps_yo"; }
            }
            if (call != null) Say(call);
            PlacePuck();
            Status(msg, total == 7 && point == 0 ? Palette.Red : Palette.Gold);
            // spots stay locked while chips are swept / paid: a chip grabbed mid-flight would return to the wallet as well
            yield return StartCoroutine(ResolveSpots(res, committed));
            foreach (var s in spots) { if (s.Total == 0) committed[s] = 0; }
            LockContracts();
            rolling = false; running = false;
            Refresh();
            rollBtn.SetInteractable(true);
            grabA.enabled = grabB.enabled = true;
            yield return StartCoroutine(Home());
            yield return new WaitForSeconds(0.6f);
            if (!rolling) Status(point == 0 ? "COME-OUT ROLL · PLACE YOUR BETS" : "POINT IS " + point + " · ROLL AGAIN", Palette.Text);
        }
    
        /// <summary>Floor switched mid-roll: the roll is void, the bets that were already working stay on the felt.</summary>
        protected override void OnRoundAborted()
        {
            rolling = false; running = false; AcceptingBets = true;
            if (rollBtn != null) rollBtn.SetInteractable(true);
            if (dieA != null) { dieA.localPosition = homeA; dieA.localRotation = Quaternion.identity; }
            if (dieB != null) { dieB.localPosition = homeB; dieB.localRotation = Quaternion.identity; }
        }
    }
}
