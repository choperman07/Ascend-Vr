using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>Half-moon card table with felt print, a shoe, dealt 3D cards and hand gestures:
    /// tap the felt with your finger = HIT, wave a flat hand over the table = STAND (like a real casino).</summary>
    public abstract class CardTable : TableGame
    {
        protected abstract string FeltTexture { get; }
        protected Shoe shoe;
        protected readonly List<Card3D> onTable = new List<Card3D>();
        protected virtual Vector3 ShoePos { get { return new Vector3(0.62f, TableY + 0.06f, 0.36f); } }
        protected virtual int Decks { get { return 6; } }
        protected override Vector3 DealerPos { get { return new Vector3(0, 0, 1.0f); } }
        protected override Vector3 SeatCenter { get { return new Vector3(0, 0, 0.38f); } }
        protected override float SeatRadius { get { return 1.5f; } }
        protected override float[] SeatAngles { get { return new[] { -66f, -34f, 34f, 66f }; } }
        public System.Action onTap, onWave;
        float lastGesture;
        Vector3 lastHandPos; bool haveHand;

        protected override void BuildTable()
        {
            shoe = new Shoe(Decks);
            var k = new MeshKit();
            var outline = new List<Vector2>();
            for (int i = 0; i <= 24; i++) { float a = Mathf.PI * i / 24f; outline.Add(new Vector2(Mathf.Cos(a) * 1.05f, -Mathf.Sin(a) * 0.85f + 0.38f)); }
            k.Prism(outline, 0.08f, TableY - 0.03f, Palette.Hex("120D1C"), 0f);
            var rail = new List<Vector2>();
            for (int i = 0; i <= 32; i++) { float a = Mathf.PI * i / 32f; rail.Add(new Vector2(Mathf.Cos(a) * 1.08f, -Mathf.Sin(a) * 0.88f + 0.38f)); }
            for (int i = 0; i < rail.Count - 1; i++)
            {
                var a = new Vector3(rail[i].x, TableY, rail[i].y); var b = new Vector3(rail[i + 1].x, TableY, rail[i + 1].y);
                k.Bar(a, b, 0.07f, Palette.Hex("2A1D10"));
                k.Bar(a + Vector3.up * 0.04f, b + Vector3.up * 0.04f, 0.01f, Palette.Gold, 1f);
                k.Bar(new Vector3(a.x * 0.95f, 0.05f, (a.z - 0.38f) * 0.95f + 0.38f), new Vector3(b.x * 0.95f, 0.05f, (b.z - 0.38f) * 0.95f + 0.38f), 0.015f, Accent, 1f);
            }
            k.Box(new Vector3(0, TableY - 0.04f, 0.6f), new Vector3(2.16f, 0.06f, 0.44f), Palette.Hex("1B1328"));
            // shoe
            // a real dealing shoe: black lacquer cradle with gold edges, the pack leaning inside on a slope and the
            // next card showing at the mouth (cards are dealt from exactly there)
            k.Push(ShoePos + new Vector3(0, -0.055f, 0), Quaternion.Euler(0, -20, 0));
            Color lac = Palette.Hex("15101F"), ivory = Palette.Hex("B8B0A0"), back = Palette.Hex("5A1230");
            k.Box(new Vector3(0, 0.006f, 0), new Vector3(0.115f, 0.012f, 0.25f), lac);                                  // base
            foreach (var sx in new[] { -1f, 1f })
            {
                k.Box(new Vector3(sx * 0.054f, 0.05f, 0.02f), new Vector3(0.008f, 0.09f, 0.21f), lac);                 // side walls
                k.Box(new Vector3(sx * 0.054f, 0.097f, 0.02f), new Vector3(0.01f, 0.005f, 0.21f), Palette.Gold, 0.5f);
                k.Box(new Vector3(sx * 0.054f, 0.028f, -0.105f), new Vector3(0.008f, 0.045f, 0.04f), Quaternion.Euler(-35, 0, 0), lac);
            }
            k.Box(new Vector3(0, 0.05f, 0.122f), new Vector3(0.115f, 0.09f, 0.008f), lac);                               // back wall
            k.Box(new Vector3(0, 0.097f, 0.122f), new Vector3(0.117f, 0.005f, 0.01f), Palette.Gold, 0.5f);
            k.Box(new Vector3(0, 0.052f, 0.012f), new Vector3(0.094f, 0.068f, 0.2f), Quaternion.Euler(-14, 0, 0), ivory, 0f);   // the pack (card edges)
            for (int i = 0; i < 9; i++) k.Box(new Vector3(0, 0.0865f - i * 0.0055f * 0.25f, 0.1f - i * 0.022f), new Vector3(0.095f, 0.0006f, 0.0012f), Quaternion.Euler(-14, 0, 0), Palette.Hex("B9B2A4"));
            k.Box(new Vector3(0, 0.0885f, 0.012f), new Vector3(0.09f, 0.002f, 0.196f), Quaternion.Euler(-14, 0, 0), back, 0f);    // top card, back up
            k.Box(new Vector3(0, 0.0895f, 0.012f), new Vector3(0.07f, 0.0015f, 0.17f), Quaternion.Euler(-14, 0, 0), Palette.Hex("8A6A1E"), 0f);
            k.Box(new Vector3(0, 0.02f, -0.118f), new Vector3(0.1f, 0.03f, 0.008f), Quaternion.Euler(-35, 0, 0), lac);            // the lip
            k.Box(new Vector3(0, 0.033f, -0.126f), new Vector3(0.102f, 0.004f, 0.01f), Quaternion.Euler(-35, 0, 0), Palette.Gold, 0.6f);
            k.Box(new Vector3(0, 0.03f, -0.1f), new Vector3(0.088f, 0.0015f, 0.06f), Quaternion.Euler(-35, 0, 0), back, 0f);     // next card at the mouth
            k.Box(new Vector3(0, 0.103f, 0.07f), new Vector3(0.05f, 0.012f, 0.05f), Palette.Hex("C9A227"), 0.3f);                  // brass weight
            k.Pop();
            k.Spawn("Table", transform, Palette.Glossy, false);
            var felt = new MeshKit();
            var inset = new List<Vector2>();
            for (int i = 0; i <= 24; i++) { float a = Mathf.PI * i / 24f; inset.Add(new Vector2(Mathf.Cos(a) * 1.0f, -Mathf.Sin(a) * 0.8f + 0.38f)); }
            felt.PolygonTop(inset, TableY + 0.001f, Color.white, 0.15f, new Rect(-1.0f, -0.42f, 2.0f, 0.8f));
            felt.Spawn("Felt", transform, Palette.Textured(FeltTexture), false);
            Build.Beam(transform, new Vector3(0, 3.6f, 0.05f), Quaternion.identity, 2.6f, 1.2f, Accent, 0.12f);

            BuildSpots();
            // gesture zone on the felt in front of the player — only on games that use the tap gesture: on the others
            // (baccarat, dragon tiger, poker) it did nothing but swallow desktop clicks meant for the bet spots under it
            if (onTap != null)
            {
                var zone = new GameObject("TapZone");
                zone.transform.SetParent(transform, false);
                zone.transform.localPosition = new Vector3(0, TableY + 0.02f, 0.02f);
                var col = zone.AddComponent<BoxCollider>(); col.isTrigger = true; col.size = new Vector3(0.7f, 0.05f, 0.18f);
                zone.AddComponent<TapZone>().table = this;
            }
        }

        protected abstract void BuildSpots();

        /// <summary>Felt print coordinates (u across, yImg from the top of the texture) → table local position.</summary>
        public static Vector3 FeltLocal(float u, float yImg) { return new Vector3((u - 0.5f) * 2f, 0f, 0.38f - yImg * 0.8f); }

        /// <summary>Called by the tap zone when a fingertip taps the felt.</summary>
        public void Tapped()
        {
            if (Time.time - lastGesture < 0.8f) return;
            lastGesture = Time.time;
            Sfx.Play("card_slide", W(new Vector3(0, TableY, -0.12f)), 0.4f);
            if (onTap != null) onTap();
        }

        protected virtual void Update()
        {
            // wave gesture: a flat hand moving sideways fast above the felt
            var rig = Rig.I; if (rig == null || onWave == null) return;
            for (int hi = 0; hi < 2; hi++)
            {
                var h = hi == 0 ? rig.RightCtrl : rig.LeftCtrl;   // no per-frame array allocation (Quest GC)
                if (h == null) continue;
                var local = transform.InverseTransformPoint(h.position);
                bool over = Mathf.Abs(local.x) < 0.7f && local.z > -0.45f && local.z < 0.25f && local.y > TableY && local.y < TableY + 0.35f;
                if (!over) continue;
                if (haveHand)
                {
                    var v = (h.position - lastHandPos) / Mathf.Max(Time.deltaTime, 1e-4f);
                    var lv = transform.InverseTransformDirection(v);
                    if (Mathf.Abs(lv.x) > 1.1f && Mathf.Abs(lv.y) < 0.6f && Time.time - lastGesture > 0.8f)
                    {
                        lastGesture = Time.time;
                        onWave();
                    }
                }
                lastHandPos = h.position; haveHand = true;
                return;
            }
            haveHand = false;
        }

        /// <summary>Dealer takes a card from the shoe and deals it to a felt position (local).</summary>
        protected IEnumerator Deal(int card, Vector3 localTarget, bool faceUp, float yaw = 0f)
        {
            var from = W(ShoePos + Quaternion.Euler(0, -20, 0) * new Vector3(0, -0.02f, -0.11f));
            StartCoroutine(Reach(from, 0.35f));
            yield return new WaitForSeconds(0.15f);
            var c = Card3D.Spawn(card, from, Flat * Quaternion.Euler(0, -20, 0), false);
            c.transform.SetParent(transform, true);   // cards belong to the table (hidden with its floor, never left floating)
            onTable.Add(c);
            yield return StartCoroutine(c.DealTo(W(localTarget + Vector3.up * (TableY + 0.003f)), Flat * Quaternion.Euler(0, yaw, 0), faceUp));
        }

        protected IEnumerator ClearCards()
        {
            if (onTable.Count == 0) yield break;
            var discard = W(new Vector3(-0.62f, TableY + 0.05f, 0.4f));
            StartCoroutine(Reach(W(new Vector3(0, TableY + 0.05f, 0.1f)), 0.6f, false));
            Sfx.Play("card_slide", discard, 0.6f);
            foreach (var c in onTable) if (c != null) StartCoroutine(Fx.Arc(c.transform, discard, 0.05f, 0.4f));
            yield return new WaitForSeconds(0.45f);
            foreach (var c in onTable) if (c != null) Destroy(c.gameObject);
            onTable.Clear();
        }

        /// <summary>Removes the cards at once (interrupted round).</summary>
        protected void ClearCardsNow()
        {
            foreach (var c in onTable) if (c != null) Destroy(c.gameObject);
            onTable.Clear();
        }
    }
}
