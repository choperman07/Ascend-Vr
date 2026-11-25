using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Neon bar with a bartender and LUCKY, the house cat (pet her: she purrs).</summary>
    public class NeonBar : MonoBehaviour
    {
        AvatarBody bartender;
        PlayerMocktail served; bool mixing;

        IEnumerator Serve()
        {
            if (mixing) yield break;
            if (served != null && Vector3.Distance(served.transform.position, transform.position) < 3f) { Voice.Say("bar_cheers", bartender.HeadBone, 0.5f); yield break; }
            mixing = true;
            Voice.Say("bar_order", bartender.HeadBone, 0.55f);
            var shaker = W(new Vector3(0.25f, 1.25f, 1.3f));
            for (float t = 0; t < 1.6f; t += Time.deltaTime)
            {
                bartender.Reach(true, shaker + Vector3.up * Mathf.Sin(t * 22f) * 0.06f, Mathf.Clamp01(t * 3f));
                bartender.Reach(false, shaker + Vector3.up * Mathf.Sin(t * 22f + 0.5f) * 0.06f + transform.right * -0.08f, Mathf.Clamp01(t * 3f));
                yield return null;
            }
            Sfx.Play("drink_pour", shaker, 0.6f);
            var m = PlayerMocktail.Make(W(new Vector3(0.1f, 1.095f, 1.0f)), Random.Range(0, 3));
            var rb = m.GetComponent<Rigidbody>(); rb.isKinematic = true;
            var to = W(new Vector3(-0.28f, 1.095f, 0.3f));
            var from = m.transform.position;
            for (float t = 0; t < 0.8f; t += Time.deltaTime)
            {
                float k = Fx.EaseOut(t / 0.8f);
                m.transform.position = Vector3.Lerp(from, to, k);
                bartender.Reach(true, m.transform.position + Vector3.up * 0.1f, 1f - k * 0.7f);
                bartender.Reach(false, transform.position, 0f);
                yield return null;
            }
            m.transform.position = to; rb.isKinematic = false;
            bartender.Reach(true, transform.position, 0f);
            Sfx.Play("chip_place", to, 0.35f, 1.5f);
            Voice.Say("bar_enjoy", bartender.HeadBone, 0.5f);
            served = m; mixing = false;
        }

        Vector3 W(Vector3 l) { return transform.TransformPoint(l); }

        // leaving the floor mid-mix kills the coroutine: without this the bar would never serve again
        void OnDisable()
        {
            if (!mixing) return;
            mixing = false;
            if (bartender != null) { bartender.Reach(true, transform.position, 0f); bartender.Reach(false, transform.position, 0f); }
        }

        void Start()
        {
            var k = new MeshKit();
            var outline = new List<Vector2>();
            for (int i = 0; i <= 16; i++) { float a = Mathf.Lerp(-70f, 70f, i / 16f) * Mathf.Deg2Rad; outline.Add(new Vector2(Mathf.Sin(a) * 2.4f, -Mathf.Cos(a) * 1.4f + 1.4f)); }
            for (int i = 16; i >= 0; i--) { float a = Mathf.Lerp(-70f, 70f, i / 16f) * Mathf.Deg2Rad; outline.Add(new Vector2(Mathf.Sin(a) * 1.85f, -Mathf.Cos(a) * 0.95f + 1.4f)); }
            k.Prism(outline, 0f, 1.08f, Palette.Hex("140F20"), 0.02f, Palette.Hex("231833"), 0.05f);
            for (int i = 0; i < 16; i++)
            {
                float a0 = Mathf.Lerp(-70f, 70f, i / 16f) * Mathf.Deg2Rad, a1 = Mathf.Lerp(-70f, 70f, (i + 1) / 16f) * Mathf.Deg2Rad;
                k.Bar(new Vector3(Mathf.Sin(a0) * 2.42f, 1.09f, -Mathf.Cos(a0) * 1.42f + 1.4f), new Vector3(Mathf.Sin(a1) * 2.42f, 1.09f, -Mathf.Cos(a1) * 1.42f + 1.4f), 0.02f, Palette.Magenta, 1f);
                k.Bar(new Vector3(Mathf.Sin(a0) * 2.42f, 0.06f, -Mathf.Cos(a0) * 1.42f + 1.4f), new Vector3(Mathf.Sin(a1) * 2.42f, 0.06f, -Mathf.Cos(a1) * 1.42f + 1.4f), 0.02f, Palette.Teal, 1f);
            }
            // back shelf with glowing bottles
            k.Box(new Vector3(0, 1.4f, 2.5f), new Vector3(4f, 2.8f, 0.3f), Palette.Hex("0D0A14"), 0.02f);
            for (int r = 0; r < 3; r++)
            {
                k.Box(new Vector3(0, 0.9f + r * 0.55f, 2.3f), new Vector3(3.8f, 0.03f, 0.3f), Palette.Metal, 0.2f);
                for (int i = 0; i < 12; i++)
                {
                    var c = Color.HSVToRGB(Mathf.Repeat(i * 0.13f + r * 0.3f, 1f), 0.8f, 1f);
                    k.Cylinder(new Vector3(-1.7f + i * 0.31f, 0.92f + r * 0.55f + 0.14f, 2.3f), 0.045f, 0.035f, 0.28f, 10, Quaternion.identity, c, 0.8f);
                }
            }
            k.Spawn("Bar", transform, Palette.Glossy, false);
            // solid counter + back wall (you can lean on it, not walk through it)
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.Lerp(-56f, 56f, i / 4f);
                var c = Build.Empty("CounterCol" + i, transform, new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 2.12f, 0.55f, -Mathf.Cos(a * Mathf.Deg2Rad) * 1.18f + 1.4f), Quaternion.Euler(0, -a, 0)).AddComponent<BoxCollider>();
                c.size = new Vector3(1.05f, 1.1f, 0.5f);
            }
            var wall = Build.Empty("ShelfCol", transform, new Vector3(0, 1.4f, 2.5f)).AddComponent<BoxCollider>(); wall.size = new Vector3(4f, 2.8f, 0.35f);
            // real bar stools: guests sit and chat, you can sit too
            for (int i = 0; i < 7; i++)
            {
                float a = Mathf.Lerp(-58f, 58f, i / 6f);
                float ar = a * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(ar) * 2.95f, 0, -Mathf.Cos(ar) * 1.85f + 1.4f);
                var st = Seat.Create("Bar Stool " + i, transform, p, -a, Seat.Style.BarStool, 1.08f);
                st.tableEdge = true;
            }
            // fruit bowls on the counter and the pack's CASINO neon on the back wall (Casino FREE)
            for (int i = 0; i < 3; i++)
            {
                float a = Mathf.Lerp(-42f, 42f, i / 2f) * Mathf.Deg2Rad;
                Props.Place("Fruits_01", transform, new Vector3(Mathf.Sin(a) * 2.1f, 1.09f, -Mathf.Cos(a) * 1.18f + 1.4f), i * 47f, 0.7f);
            }
            Props.Place("Neon_Sign_01", transform, new Vector3(0, 1.75f, 2.3f), 180f, 1f);
            Txt.Glow("Sign", transform, new Vector3(0, 3.1f, 2.3f), "THE HIGH BAR", 0.3f, Palette.Magenta);
            Txt.Make("Menu", transform, new Vector3(1.45f, 2.6f, 2.3f), "MOCKTAILS ONLY\nNEON SOUR · ZERO PROOF\nSKYLINE SPRITZ · ZERO PROOF", 0.06f, Palette.Muted, Palette.Mono);
            bartender = Avatars.Create("Bartender", transform, new Vector3(0, 0, 1.75f), 180f, Rarity.Epic, RoninRole.Bartender);
            bartender.pose = Pose.Bartend;
            var ap = bartender.gameObject.AddComponent<AvatarProfile>(); ap.displayName = "MIXER · BARTENDER"; ap.rarity = Rarity.Epic; ap.xp = 30000; ap.status = "STAFF · THE HIGH BAR";
            if (Rig.I != null) bartender.lookAtTarget = Rig.I.Head;
            var trig = Build.Empty("GreetZone", transform, new Vector3(0, 1, -0.5f)).AddComponent<SphereCollider>(); trig.isTrigger = true; trig.radius = 1.6f;
            trig.gameObject.AddComponent<GreetZone>().onEnter = () => Voice.Say("bar_hello", bartender.HeadBone, 0.55f);
            // order a mocktail: the bartender mixes it and slides it across; pick it up and bring it to your mouth to sip
            PokeButton.Create("OrderDrink", transform, new Vector3(-0.62f, 1.17f, 0.3f), Quaternion.Euler(-90, 0, 0), 0.045f, Palette.Magenta, "ORDER\nMOCKTAIL", () => StartCoroutine(Serve()));
            // Lucky the cat sits on the counter
            var cat = Build.Empty("Lucky", transform, new Vector3(1.6f, 1.09f, 0.95f), Quaternion.Euler(0, 200, 0));
            cat.AddComponent<LuckyCat>();
        }
    }
}
