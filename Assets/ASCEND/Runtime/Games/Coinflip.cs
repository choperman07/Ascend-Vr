using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Coinflip: call HEADS or TAILS, then grab the giant coin and toss it up (or press FLIP). 50/50, pays 1.92x (RTP 96%).</summary>
    public class Coinflip : ArcadeStation
    {
        const float Pays = 1.92f;
        bool callHeads = true, flipping;
        Transform coin, pad;
        XRGrabInteractable grab;
        PokeButton headsBtn, tailsBtn;
        TextMeshPro callT, streakT;
        int streak;
        readonly System.Collections.Generic.List<bool> history = new System.Collections.Generic.List<bool>();
        TextMeshPro histT;

        protected override string Rules { get { return "CALL IT, THEN TOSS THE COIN\nHEADS OR TAILS\nWIN PAYS 1.92x"; } }
        protected override float CounterWidth { get { return 1.3f; } }
        Vector3 PadPos { get { return new Vector3(0, TableY + 0.04f, 0.2f); } }

        protected override void BuildMachine()
        {
            var k = new MeshKit();
            k.Cylinder(new Vector3(0, TableY * 0.5f, 0.45f), 0.32f, 0.36f, TableY, 32, Quaternion.identity, Palette.Hex("140F20"), 0.02f, true, Palette.Hex("1B1328"), 0.05f);
            k.Torus(new Vector3(0, TableY + 0.01f, 0.45f), 0.33f, 0.012f, 40, 6, Quaternion.identity, Accent, 1f);
            k.Spawn("Pedestal", transform, Palette.Glossy, false);
            pad = Build.Empty("Pad", transform, new Vector3(0, TableY + 0.06f, 0.45f)).transform;
            Build.Beam(transform, new Vector3(0, 3.2f, 0.45f), Quaternion.Euler(90, 0, 0), 2.3f, 0.35f, Accent, 0.12f);
            coin = BuildCoin().transform;
            coin.SetParent(transform, false);
            coin.localPosition = pad.localPosition; coin.localRotation = Quaternion.identity;
            var col = coin.gameObject.AddComponent<CapsuleCollider>(); col.direction = 1; col.radius = 0.11f; col.height = 0.04f;
            var rb = coin.gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            grab = coin.gameObject.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous; grab.throwOnDetach = false;
            grab.selectExited.AddListener(_ => StartCoroutine(Released()));
            grab.activated.AddListener(_ => Flip(Vector3.up * 2.5f));
            VelocityTracker.On(coin);

            Txt.Glow("Title", transform, new Vector3(0, 2.25f, 0.6f), "COINFLIP", 0.2f, Accent);
            callT = Txt.Make("Call", transform, new Vector3(0, 1.95f, 0.6f), "", 0.09f, Palette.Gold, Palette.Display);
            streakT = Txt.Make("Streak", transform, new Vector3(0, 1.78f, 0.6f), "", 0.05f, Palette.Muted, Palette.Mono);
            histT = Txt.Make("History", transform, new Vector3(0, 1.65f, 0.6f), "", 0.05f, Palette.Muted, Palette.Display);
            headsBtn = PokeButton.Create("Heads", transform, new Vector3(-0.15f, TableY + 0.035f, -0.38f), Quaternion.identity, 0.04f, Palette.Teal, "HEADS", () => Call(true));
            tailsBtn = PokeButton.Create("Tails", transform, new Vector3(0.38f, TableY + 0.035f, -0.38f), Quaternion.identity, 0.04f, Palette.Magenta, "TAILS", () => Call(false));
            PokeButton.Create("Flip", transform, new Vector3(0.45f, TableY + 0.035f, -0.18f), Quaternion.identity, 0.035f, Palette.Gold, "FLIP", () => Flip(Vector3.up * 2.5f));
            Call(true);
        }

        static GameObject BuildCoin()
        {
            var k = new MeshKit();
            k.Cylinder(Vector3.zero, 0.11f, 0.11f, 0.025f, 40, Quaternion.identity, Palette.Gold, 0.5f, false);
            k.Disc(new Vector3(0, 0.0126f, 0), 0.11f, 40, Quaternion.identity, Palette.Teal, 0.7f);
            k.Disc(new Vector3(0, -0.0126f, 0), 0.11f, 40, Quaternion.Euler(180, 0, 0), Palette.Magenta, 0.7f);
            k.Torus(new Vector3(0, 0.013f, 0), 0.095f, 0.006f, 40, 5, Quaternion.identity, Palette.Gold, 1f);
            k.Torus(new Vector3(0, -0.013f, 0), 0.095f, 0.006f, 40, 5, Quaternion.identity, Palette.Gold, 1f);
            var go = k.Spawn("Coin", null, Vector3.zero, Quaternion.identity, Palette.Neon, false);
            var h = Txt.Make("H", go.transform, new Vector3(0, 0.0135f, 0), "H", 0.12f, Palette.Bg, Palette.Display); h.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var t = Txt.Make("T", go.transform, new Vector3(0, -0.0135f, 0), "T", 0.12f, Palette.Bg, Palette.Display); t.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            return go;
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "HEADS", 20, y, 250, 80, Palette.Teal, () => Call(true), 34);
            UIKit.Btn(p, "TAILS", 290, y, 250, 80, Palette.Magenta, () => Call(false), 34);
            UIKit.Btn(p, "FLIP", 20, y + 90, 520, 70, Palette.Gold, () => Flip(Vector3.up * 2.5f), 34);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 380); } }

        void Call(bool heads)
        {
            if (flipping) return;
            callHeads = heads;
            callT.text = "YOUR CALL: " + (heads ? "HEADS" : "TAILS");
            callT.color = heads ? Palette.Teal : Palette.Magenta;
            Sfx.UI("ui_click", 0.5f);
            if (Random.value < 0.4f) Say(heads ? "heads" : "tails");
        }

        IEnumerator Released()
        {
            yield return null;
            if (grab.isSelected || flipping) yield break;
            var v = VelocityTracker.On(coin).velocity;
            if (v.y > 0.7f) Flip(v);
            else StartCoroutine(Home());
        }

        IEnumerator Home()
        {
            yield return StartCoroutine(Fx.Arc(coin, pad.position, 0.08f, 0.35f));
            coin.localRotation = Quaternion.identity;
        }

        void Flip(Vector3 v)
        {
            if (flipping) return;
            long stake = Take();
            if (stake <= 0) { StartCoroutine(Home()); return; }
            StartCoroutine(FlipRoutine(stake, v));
        }

        IEnumerator FlipRoutine(long stake, Vector3 v)
        {
            flipping = true;
            headsBtn.SetInteractable(false); tailsBtn.SetInteractable(false);
            if (grab.isSelected && grab.interactionManager != null) grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
            bool heads = Odds.R() < 0.5;
            Say("call_it");
            Sfx.Play("coin_flip", coin.position, 1f);
            XRIn.HapticBoth(0.3f, 0.05f);
            var start = coin.position;
            float up = Mathf.Clamp(v.y * 0.35f, 0.6f, 1.2f);
            var peak = Vector3.Lerp(start, pad.position, 0.5f) + Vector3.up * up;
            float dur = 1.25f + up * 0.4f;
            int turns = 6 + Odds.Range(0, 3);
            float endX = heads ? 0f : 180f;
            float t = 0;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                var a = Vector3.Lerp(start, peak, k); var b = Vector3.Lerp(peak, pad.position, k);
                coin.position = Vector3.Lerp(a, b, k);
                coin.rotation = transform.rotation * Quaternion.Euler(Mathf.Lerp(0, turns * 360f + endX, Fx.EaseOut(k)), k * 90f, 0);
                yield return null;
            }
            coin.position = pad.position;
            coin.rotation = transform.rotation * Quaternion.Euler(endX, 90, 0);
            Sfx.Play("coin_land", coin.position, 1f);
            yield return StartCoroutine(Fx.Shake(coin, 0.01f, 0.25f));
            bool win = heads == callHeads;
            Say(heads ? "heads" : "tails");
            history.Insert(0, heads); if (history.Count > 10) history.RemoveAt(10);
            var sb = new System.Text.StringBuilder();
            foreach (var h in history) sb.Append(h ? "<color=#2DE2E6>H</color> " : "<color=#FF2E97>T</color> ");
            histT.text = sb.ToString();
            streak = win ? streak + 1 : 0;
            streakT.text = streak >= 2 ? "STREAK x" + streak : "";
            if (streak == 5) GameState.I.Unlock("CALLED IT", "FIVE COINFLIPS IN A ROW", 750);
            Status((heads ? "HEADS" : "TAILS") + (win ? " · YOU WIN" : " · NO WIN"), win ? Palette.Mint : Palette.Muted);
            PayDirect(win ? (long)(stake * (double)Pays) : 0, stake, coin.position + Vector3.up * 0.1f);
            yield return new WaitForSeconds(1.2f);
            coin.localRotation = Quaternion.identity;
            headsBtn.SetInteractable(true); tailsBtn.SetInteractable(true);
            flipping = false;
        }
    }
}
