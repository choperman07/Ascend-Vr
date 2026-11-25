using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Dice: set a target, grab the cup, shake it and turn it over (or press ROLL). Win if the roll 0.00–99.99
    /// is under the target; payout 97 ÷ target (RTP 97%).</summary>
    public class Dice : ArcadeStation
    {
        int target = 50;
        TextMeshPro targetT, rollT, payT;
        Transform cup, dieA, dieB, bar, marker;
        XRGrabInteractable cupGrab;
        Vector3 cupHome; Quaternion cupHomeRot;
        float shake; Vector3 lastVel; AudioSource rattle;
        bool rolling;

        protected override string Rules { get { return "SET A TARGET 2–95\nWIN IF THE ROLL IS UNDER IT\nLOWER TARGET = BIGGER PAYOUT (50: 1.94x · 10: 9.7x)"; } }
        float Payout { get { return 97f / target; } }

        protected override void BuildMachine()
        {
            var k = new MeshKit();
            // dice tray (rimmed felt)
            k.Box(new Vector3(0, TableY + 0.035f, 0.25f), new Vector3(0.9f, 0.01f, 0.45f), Palette.Hex("0E2A26"), 0.05f);
            k.Bar(new Vector3(-0.45f, TableY + 0.06f, 0.025f), new Vector3(0.45f, TableY + 0.06f, 0.025f), 0.03f, Palette.Hex("2A1D10"));
            k.Bar(new Vector3(-0.45f, TableY + 0.06f, 0.475f), new Vector3(0.45f, TableY + 0.06f, 0.475f), 0.03f, Palette.Hex("2A1D10"));
            k.Bar(new Vector3(-0.45f, TableY + 0.06f, 0.025f), new Vector3(-0.45f, TableY + 0.06f, 0.475f), 0.03f, Palette.Hex("2A1D10"));
            k.Bar(new Vector3(0.45f, TableY + 0.06f, 0.025f), new Vector3(0.45f, TableY + 0.06f, 0.475f), 0.03f, Palette.Hex("2A1D10"));
            // display board
            k.Box(new Vector3(0, 1.85f, 0.7f), new Vector3(1.8f, 0.8f, 0.06f), Palette.Hex("08060F"));
            k.Bar(new Vector3(-0.8f, 1.6f, 0.66f), new Vector3(0.8f, 1.6f, 0.66f), 0.05f, Palette.Hex("2A0D18"), 0.2f);
            k.Spawn("Tray", transform, Palette.Neon, false);
            var bk = new MeshKit(); bk.Box(Vector3.zero, new Vector3(1.6f, 0.05f, 0.01f), Palette.Mint, 1f);
            bar = bk.Spawn("WinBar", transform, new Vector3(-0.8f, 1.6f, 0.65f), Quaternion.identity, Palette.Neon, false).transform;
            var mk = new MeshKit(); mk.Box(Vector3.zero, new Vector3(0.012f, 0.12f, 0.02f), Palette.Text, 1f);
            marker = mk.Spawn("Marker", transform, new Vector3(0, 1.6f, 0.64f), Quaternion.identity, Palette.Neon, false).transform;
            targetT = Txt.Glow("Target", transform, new Vector3(-0.45f, 2.0f, 0.66f), "", 0.14f, Palette.Gold);
            payT = Txt.Make("Pay", transform, new Vector3(0.45f, 2.0f, 0.66f), "", 0.08f, Palette.Text, Palette.Display);
            rollT = Txt.Glow("Roll", transform, new Vector3(0, 2.4f, 0.66f), "SHAKE THE CUP", 0.14f, Palette.Text);
            dieA = MakeDie("DieA", new Vector3(-0.1f, TableY + 0.08f, 0.25f));
            dieB = MakeDie("DieB", new Vector3(0.1f, TableY + 0.08f, 0.25f));
            // the cup
            var ck = new MeshKit();
            ck.Cylinder(new Vector3(0, 0.07f, 0), 0.065f, 0.075f, 0.14f, 20, Quaternion.identity, Palette.Hex("231633"), 0.1f, false);
            ck.Disc(new Vector3(0, 0.14f, 0), 0.075f, 20, Quaternion.identity, Palette.Hex("231633"), 0.1f);
            ck.Torus(new Vector3(0, 0.005f, 0), 0.066f, 0.008f, 20, 5, Quaternion.identity, Palette.Gold, 1f);
            ck.Torus(new Vector3(0, 0.1f, 0), 0.072f, 0.005f, 20, 5, Quaternion.identity, Accent, 1f);
            cup = ck.Spawn("Cup", transform, new Vector3(-0.62f, TableY + 0.03f, -0.28f), Quaternion.identity, Palette.Neon, false).transform;
            cupHome = cup.localPosition; cupHomeRot = cup.localRotation;
            var cc = cup.gameObject.AddComponent<CapsuleCollider>(); cc.center = new Vector3(0, 0.07f, 0); cc.height = 0.16f; cc.radius = 0.075f;
            var rb = cup.gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            cupGrab = cup.gameObject.AddComponent<XRGrabInteractable>();
            cupGrab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            cupGrab.throwOnDetach = false;
            cupGrab.selectExited.AddListener(_ => { if (!rolling) StartCoroutine(ReturnCup()); });
            PokeButton.Create("Roll", transform, new Vector3(0.36f, TableY + 0.035f, -0.32f), Quaternion.identity, 0.045f, Palette.Gold, "ROLL", () => StartCoroutine(Roll()));
            PokeButton.Create("M10", transform, new Vector3(-0.3f, TableY + 0.035f, -0.4f), Quaternion.identity, 0.022f, Palette.Teal, "-10", () => Step(-10), 0.025f);
            PokeButton.Create("M1", transform, new Vector3(-0.2f, TableY + 0.035f, -0.4f), Quaternion.identity, 0.022f, Palette.Teal, "-1", () => Step(-1), 0.025f);
            PokeButton.Create("P1", transform, new Vector3(-0.1f, TableY + 0.035f, -0.4f), Quaternion.identity, 0.022f, Palette.Teal, "+1", () => Step(1), 0.025f);
            PokeButton.Create("P10", transform, new Vector3(0.0f, TableY + 0.035f, -0.4f), Quaternion.identity, 0.022f, Palette.Teal, "+10", () => Step(10), 0.025f);
            Step(0);
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "-10", 20, y, 120, 60, Palette.Teal, () => Step(-10), 28);
            UIKit.Btn(p, "-1", 150, y, 120, 60, Palette.Teal, () => Step(-1), 28);
            UIKit.Btn(p, "+1", 290, y, 120, 60, Palette.Teal, () => Step(1), 28);
            UIKit.Btn(p, "+10", 420, y, 120, 60, Palette.Teal, () => Step(10), 28);
            UIKit.Btn(p, "ROLL", 20, y + 70, 520, 80, Palette.Gold, () => StartCoroutine(Roll()), 36);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 370); } }

        Transform MakeDie(string n, Vector3 pos)
        {
            var k = new MeshKit();
            k.Box(Vector3.zero, Vector3.one * 0.06f, Palette.Hex("1A1428"), 0.1f);
            float h = 0.031f;
            // pips as small glowing studs on each face (1..6)
            Vector3[] faces = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            int[] count = { 1, 6, 3, 4, 2, 5 };
            for (int f = 0; f < 6; f++)
            {
                var nrm = faces[f]; var tA = Vector3.Cross(nrm, Mathf.Abs(nrm.y) > 0.5f ? Vector3.right : Vector3.up).normalized; var tB = Vector3.Cross(nrm, tA);
                var offs = PipOffsets(count[f]);
                foreach (var o in offs) k.Box(nrm * h + (tA * o.x + tB * o.y) * 0.016f, Vector3.one * 0.009f, Accent, 1f);
            }
            var go = k.Spawn(n, transform, pos, Quaternion.identity, Palette.Neon, false);
            return go.transform;
        }

        static Vector2[] PipOffsets(int n)
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

        void Step(int d)
        {
            if (rolling) return;
            target = Mathf.Clamp(target + d, 2, 95);
            targetT.text = "UNDER " + target;
            payT.text = Payout.ToString("0.00") + "x\nUNDER " + target;
            bar.localScale = new Vector3(target / 100f, 1, 1);
            bar.localPosition = new Vector3(-0.8f + 0.8f * target / 100f, 1.6f, 0.65f);
        }

        void Update()
        {
            if (rolling || !cupGrab.isSelected) { if (rattle != null) rattle.volume = Mathf.MoveTowards(rattle.volume, 0, Time.deltaTime * 3); return; }
            // shake detection from the controller holding the cup
            var rig = Rig.I;
            if (rig != null)
            {
                var hand = rig.LeftCtrl == null || (rig.RightCtrl != null && (rig.RightCtrl.position - cup.position).sqrMagnitude < (rig.LeftCtrl.position - cup.position).sqrMagnitude) ? rig.RightCtrl : rig.LeftCtrl;
                if (hand != null)
                {
                    var track = VelocityTracker.On(hand);
                    var v = track.velocity;
                    shake = Mathf.Clamp(shake + (v - lastVel).magnitude * 0.15f - Time.deltaTime * 0.6f, 0f, 3f);
                    lastVel = v;
                }
            }
            if (rattle == null) rattle = Sfx.Loop("dice_shake", cup, 0f);
            if (rattle != null) rattle.volume = Mathf.Clamp01(shake * 0.5f);
            if (shake > 0.4f && Time.frameCount % 6 == 0) XRIn.HapticBoth(Mathf.Min(0.6f, shake * 0.2f), 0.02f);
            bool upsideDown = Vector3.Dot(cup.up, Vector3.up) < -0.3f;
            // reset first: if the stake is refused (no chips, loss limit) this must not retry, notify and refund every frame
            if (upsideDown && shake > 0.8f) { shake = 0f; StartCoroutine(Roll()); }
        }

        IEnumerator ReturnCup()
        {
            yield return new WaitForSeconds(0.2f);
            if (cupGrab.isSelected) yield break;
            cup.localPosition = cupHome; cup.localRotation = cupHomeRot;
        }

        IEnumerator Roll()
        {
            if (rolling) yield break;
            long stake = Take();
            if (stake <= 0) yield break;
            rolling = true; shake = 0f;
            if (rattle != null) rattle.volume = 0f;
            Say("roll");
            int roll = Odds.Range(0, 10000);
            Sfx.Play("dice_roll", dieA.position, 1f);
            Vector3 a0 = new Vector3(-0.1f, TableY + 0.08f, 0.25f), b0 = new Vector3(0.12f, TableY + 0.08f, 0.22f);
            float dur = 1.1f;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float k = t / dur;
                float hop = Mathf.Abs(Mathf.Sin(k * Mathf.PI * 3.5f)) * 0.18f * (1f - k);
                dieA.localPosition = a0 + new Vector3(Mathf.Sin(k * 9f) * 0.12f * (1 - k), hop, Mathf.Cos(k * 7f) * 0.06f * (1 - k));
                dieB.localPosition = b0 + new Vector3(Mathf.Cos(k * 8f) * 0.12f * (1 - k), hop * 0.8f, Mathf.Sin(k * 6f) * 0.06f * (1 - k));
                dieA.Rotate(new Vector3(540, 300, 160) * Time.deltaTime * (1 - k), Space.Self);
                dieB.Rotate(new Vector3(-320, 480, 220) * Time.deltaTime * (1 - k), Space.Self);
                float shown = Odds.Range(0, 10000) / 100f;
                rollT.text = shown.ToString("00.00");
                marker.localPosition = new Vector3(-0.8f + 1.6f * shown / 100f, 1.6f, 0.64f);
                yield return null;
            }
            Sfx.Play("dice_land", dieA.position, 1f);
            float value = roll / 100f;
            bool win = roll < target * 100;
            rollT.text = value.ToString("00.00");
            rollT.color = win ? Palette.Mint : Palette.Red;
            marker.localPosition = new Vector3(-0.8f + 1.6f * value / 100f, 1.6f, 0.64f);
            Status(win ? "UNDER " + target + " · WIN" : "NOT UNDER " + target, win ? Palette.Mint : Palette.Muted);
            // integer maths: (float)97/10 is 9.6999998, which would pay 969 instead of the printed 9.70x on 100
            PayDirect(win ? stake * 97L / target : 0, stake, dieA.position + Vector3.up * 0.2f);
            yield return new WaitForSeconds(0.8f);
            rollT.color = Palette.Text;
            rolling = false;
            if (!cupGrab.isSelected) StartCoroutine(ReturnCup());
        }
    }
}
