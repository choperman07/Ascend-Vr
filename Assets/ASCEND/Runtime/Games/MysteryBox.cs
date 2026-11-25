using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Mystery Box: grab the box and shake it until it bursts open (or press OPEN). Prize decided by the published table (RTP 97%).</summary>
    public class MysteryBox : ArcadeStation
    {
        public static readonly PrizeTable Table = new PrizeTable(
            new[] { "DUST", "BRONZE", "SILVER", "GOLD", "PLATINUM", "DIAMOND", "ASCEND" },
            new[] { 0.1f, 0.5f, 1f, 2f, 4f, 10f, 50f },
            new double[] { 44, 26, 14, 10, 4.4, 1.3, 0.3 });
        static readonly Color[] TierCol = { Palette.Muted, Palette.Coral, Palette.Silver, Palette.Gold, Palette.Teal, Palette.Violet, Palette.Magenta };
        static readonly string[] RevealSfx = { "reveal_common", "reveal_common", "reveal_rare", "reveal_rare", "reveal_epic", "reveal_legendary", "reveal_mythic" };

        Transform box, lid, home;
        XRGrabInteractable grab;
        float shake; bool opening;
        Vector3 lastVel;
        TextMeshPro prizeT, shakeT;
        Renderer boxR;

        protected override string Rules { get { return "SHAKE THE BOX TO OPEN IT\n" + Table.Describe(4) + "\nPAYOUT = BET × PRIZE"; } }
        protected override float CounterWidth { get { return 1.3f; } }

        protected override void BuildMachine()
        {
            var k = new MeshKit();
            for (int i = 0; i < 3; i++)
                k.Box(new Vector3(-0.35f + i * 0.35f, 1.55f + (i % 2) * 0.12f, 0.75f), new Vector3(0.28f, 0.28f, 0.28f), Quaternion.Euler(0, 20 * (i - 1), 0), TierCol[3 + i], 0.35f);
            k.Box(new Vector3(0, 1.2f, 0.75f), new Vector3(1.2f, 0.08f, 0.4f), Palette.Metal, 0.05f);
            k.Box(new Vector3(0, 0.6f, 0.75f), new Vector3(0.12f, 1.2f, 0.12f), Palette.Metal, 0.05f);
            k.Spawn("Display", transform, Palette.Glossy, false);
            Txt.Glow("Title", transform, new Vector3(0, 2.2f, 0.75f), "MYSTERY BOX", 0.2f, Accent);
            prizeT = Txt.Make("Prize", transform, new Vector3(0, 1.98f, 0.7f), "", 0.09f, Palette.Gold, Palette.Display);
            shakeT = Txt.Make("Shake", transform, new Vector3(0.45f, TableY + 0.32f, 0.05f), "", 0.04f, Palette.Text, Palette.Display);
            shakeT.gameObject.AddComponent<Billboard>();
            home = Build.Empty("Home", transform, new Vector3(0.45f, TableY + 0.1f, 0.05f)).transform;
            SpawnBox();
            PokeButton.Create("Open", transform, new Vector3(-0.12f, TableY + 0.035f, -0.38f), Quaternion.identity, 0.04f, Palette.Gold, "OPEN", () => StartCoroutine(Open()));
        }

        void SpawnBox()
        {
            var k = new MeshKit();
            var c = Accent;
            k.Box(Vector3.zero, new Vector3(0.16f, 0.14f, 0.16f), Palette.Hex("1E1530"), 0.15f);
            k.Box(Vector3.zero, new Vector3(0.03f, 0.145f, 0.165f), c, 1f);
            k.Box(Vector3.zero, new Vector3(0.165f, 0.145f, 0.03f), c, 1f);
            box = k.Spawn("Box", transform, home.localPosition, Quaternion.Euler(0, 25, 0), Palette.Neon, false).transform;
            boxR = box.GetComponent<Renderer>();
            var lk = new MeshKit();
            lk.Box(new Vector3(0, 0.0f, 0), new Vector3(0.175f, 0.03f, 0.175f), Palette.Hex("2A1D40"), 0.2f);
            lk.Box(new Vector3(0, 0.0f, 0), new Vector3(0.035f, 0.035f, 0.18f), c, 1f);
            lk.Box(new Vector3(0, 0.0f, 0), new Vector3(0.18f, 0.035f, 0.035f), c, 1f);
            lk.Sphere(new Vector3(0, 0.03f, 0), new Vector3(0.05f, 0.025f, 0.03f), 8, c, 1f);
            lid = lk.Spawn("Lid", box, new Vector3(0, 0.085f, 0), Quaternion.identity, Palette.Neon, false).transform;
            Build.GlowSprite(box, Vector3.zero, 0.35f, c, 0.5f);
            box.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.18f, 0.2f, 0.18f);
            var rb = box.gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            grab = box.gameObject.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous; grab.throwOnDetach = false;
            grab.selectExited.AddListener(_ => { if (!opening) StartCoroutine(GoHome()); });
            grab.activated.AddListener(_ => StartCoroutine(Open()));
            VelocityTracker.On(box);
            shake = 0;
        }

        IEnumerator GoHome()
        {
            yield return null;
            if (grab == null || grab.isSelected || opening) yield break;
            yield return StartCoroutine(Fx.Arc(box, home.position, 0.08f, 0.35f));
            if (box != null) box.localRotation = Quaternion.Euler(0, 25, 0);
        }

        protected override void ExtraPanel(RectTransform p, float y)
        {
            UIKit.Btn(p, "OPEN BOX", 20, y, 520, 90, Palette.Gold, () => StartCoroutine(Open()), 40);
        }
        protected override Vector2 PanelSize { get { return new Vector2(560, 320); } }

        void Update()
        {
            if (box == null || opening) return;
            if (grab != null && grab.isSelected)
            {
                var v = VelocityTracker.On(box).velocity;
                float jerk = (v - lastVel).magnitude;
                lastVel = v;
                if (jerk > 0.35f && v.magnitude > 0.6f)
                {
                    shake += jerk * Time.deltaTime * 2.2f;
                    if (Time.frameCount % 6 == 0) { Sfx.Play("box_rattle", box.position, Mathf.Clamp01(jerk * 0.3f), 0.9f + shake * 0.3f); XRIn.HapticBoth(Mathf.Clamp01(0.15f + shake * 0.4f), 0.03f); }
                }
                Fx.SetColor(boxR, Color.Lerp(Color.white, Palette.Gold, shake), 0.3f + shake);
                shakeT.text = shake < 0.02f ? "SHAKE IT!" : new string('|', Mathf.Clamp((int)(shake * 12), 1, 12));
                shakeT.transform.position = box.position + Vector3.up * 0.2f;
                if (shake >= 1f) StartCoroutine(Open());
            }
            else { shakeT.text = "GRAB & SHAKE"; shakeT.transform.position = home.position + Vector3.up * 0.22f; }
        }

        IEnumerator Open()
        {
            if (opening || box == null) yield break;
            long stake = Take();
            if (stake <= 0) { shake = 0f; yield break; }   // no bet possible: reset, or a full shake meter retries (and re-notifies) every frame
            opening = true;
            Say("box");
            int r = Table.Roll();
            var b = box; var l = lid; var g = grab;
            if (g.isSelected && g.interactionManager != null) g.interactionManager.CancelInteractableSelection((IXRSelectInteractable)g);
            Destroy(g); grab = null;
            // anticipation: fast rattle building up (same for every result)
            for (int i = 0; i < 6; i++)
            {
                Sfx.Play("box_rattle", b.position, 0.6f, 1f + i * 0.08f);
                StartCoroutine(Fx.Shake(b, 0.006f + i * 0.002f, 0.12f));
                XRIn.HapticBoth(0.2f + i * 0.08f, 0.04f);
                yield return new WaitForSeconds(0.13f);
            }
            Sfx.Play("box_open", b.position, 1f);
            Sfx.Play(RevealSfx[r], b.position, 0.9f);
            Juice.Sparks(b.position + Vector3.up * 0.1f, TierCol[r], 20 + r * 12);
            StartCoroutine(Fx.Arc(l, b.position + Vector3.up * 0.4f + transform.right * 0.2f, 0.2f, 0.4f));
            var prize = new MeshKit();
            prize.Sphere(Vector3.zero, 0.05f + r * 0.008f, 14, TierCol[r], 1f);
            var pg = prize.Spawn("Prize", transform, transform.InverseTransformPoint(b.position), Quaternion.identity, Palette.Neon, false).transform;
            Build.GlowSprite(pg, Vector3.zero, 0.3f + r * 0.08f, TierCol[r], 1f);
            yield return StartCoroutine(Fx.Move(pg, b.position + Vector3.up * 0.35f, 0.5f, false));
            float mult = Table.mult[r];
            prizeT.text = Table.names[r] + " · " + mult.ToString("0.##") + "x";
            prizeT.color = TierCol[r];
            Status(Table.names[r], TierCol[r]);
            if (r >= 4) Say(r >= 6 ? "mythic" : r == 5 ? "legendary" : "epic");
            PayDirect((long)(stake * (double)mult), stake, pg.position);
            if (r == 6) GameState.I.Unlock("ASCEND BOX", "OPENED THE ASCEND MYSTERY BOX", 2500);
            yield return new WaitForSeconds(1.6f);
            Destroy(pg.gameObject); Destroy(b.gameObject);
            SpawnBox();
            Sfx.Play("ui_open", home.position, 0.5f);
            opening = false;
        }
    }
}
