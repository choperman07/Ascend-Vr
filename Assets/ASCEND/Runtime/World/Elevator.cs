using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>The working lift in the central core. Walk up to it and the doors slide open; step inside and pick a floor
    /// on the cabin panel (or the big UP / DOWN keys). The doors close, the cabin hums and shakes while the digital
    /// indicator counts, and the doors open on the new floor — you never leave the cabin. Every floor has its own cabin at
    /// the same spot, so the ride is seamless. Locked floors (tier) show a lock + tier and refuse politely.
    /// The cabin also builds the two call panels beside the doors outside (they replace the old ElevatorPanel look).</summary>
    public class Elevator : MonoBehaviour
    {
        public int floor;
        static readonly List<Elevator> all = new List<Elevator>();
        static bool riding;
        public static bool Riding { get { return riding; } }

        // cabin interior in floor-local space (doors on the core's front at z ≈ -3)
        const float X = 1.15f, Z0 = -2.9f, Z1 = -0.8f, Hgt = 2.75f;
        Transform doorL, doorR;
        float open;           // 0 closed .. 1 open
        bool holdClosed;
        TextMeshPro insideT;
        float closeAt;
        bool myRide;          // this cabin is running the ride coroutine
        AudioSource hum;

        // ---- panel layout (panel-local metres: x right, y up from the floor, front = -Z) ----
        const float PW = 0.76f;            // glass width
        const float RowTop = 1.755f;       // centre of the top floor row
        const float Pitch = 0.1f;          // row spacing
        const float KeyW = 0.12f, KeyH = 0.084f;
        const float NumEm = 0.072f;        // key number (cap ≈ 0.05 m)
        const float NameEm = 0.054f;       // floor name (cap ≈ 0.038 m)
        const float TierEm = 0.05f;        // tier tag (cap ≈ 0.035 m)
        static readonly Color Amber = Palette.Hex("FFB43A");
        static readonly Color GlassCol = Palette.Hex("0A0812");
        static readonly Color KeyIdle = Palette.Hex("2A2536");
        static readonly Color KeyLocked = Palette.Hex("16131E");
        static readonly Color InkDark = Palette.Hex("1A1206");

        /// <summary>One digital readout (cabin panel, above the doors, call panels).</summary>
        class Readout { public TextMeshPro num, arrow, name; }
        readonly List<Readout> readouts = new List<Readout>();
        /// <summary>Floor keys of every panel this lift owns: index = floor.</summary>
        readonly List<LiftKey[]> panels = new List<LiftKey[]>();
        LiftKey upKey, downKey;
        int shownFloor, shownDir, rideTarget = -1;
        bool[] lockedState;
        float nextLockCheck;

        void Awake() { all.Add(this); }
        void OnDestroy() { all.Remove(this); EndRide(); }

        // the floor can be switched off mid-ride (security escort, poke-the-guard, floor keys): the coroutine dies with it,
        // so release the global lock, the door hold and the hum loop here or the lift never works again
        void OnDisable() { EndRide(); holdClosed = false; }

        void OnEnable() { nextLockCheck = 0f; dirty = true; }
        bool dirty;

        void EndRide()
        {
            if (hum != null) { Destroy(hum.gameObject); hum = null; }
            if (myRide) { myRide = false; riding = false; }
            rideTarget = -1; shownDir = 0; shownFloor = floor; toBasement = false;
        }

        // quad facing helpers (MeshKit.Quad faces -Z by default)
        static Quaternion Facing(Vector3 normal) { return Quaternion.LookRotation(-normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up); }

        void Start()
        {
            shownFloor = floor;
            float zc = (Z0 + Z1) * 0.5f, depth = Z1 - Z0;
            // cabin shell: deco-panel walls, marble floor, coffered ceiling — every face points INTO the cabin
            var walls = new MeshKit();
            walls.Quad(new Vector3(0, Hgt * 0.5f, Z1), new Vector2(X * 2, Hgt), Facing(Vector3.back), Color.white, 0f, new Rect(0, 0, 2.3f, 2.75f));
            walls.Quad(new Vector3(-X, Hgt * 0.5f, zc), new Vector2(depth, Hgt), Facing(Vector3.right), Color.white, 0f, new Rect(0, 0, 2f, 2.75f));
            walls.Quad(new Vector3(X, Hgt * 0.5f, zc), new Vector2(depth, Hgt), Facing(Vector3.left), Color.white, 0f, new Rect(0, 0, 2f, 2.75f));
            // inner faces of the door frame (the core wall thickness), so the doorway looks solid
            walls.Quad(new Vector3(-X, Hgt * 0.5f, Z0 - 0.05f), new Vector2(0.1f, Hgt), Facing(Vector3.right), Color.white, 0f, new Rect(0, 0, 0.1f, 2.75f));
            walls.Quad(new Vector3(X, Hgt * 0.5f, Z0 - 0.05f), new Vector2(0.1f, Hgt), Facing(Vector3.left), Color.white, 0f, new Rect(0, 0, 0.1f, 2.75f));
            walls.Spawn("CabinWalls", transform, Palette.DecoPanel, false);
            var fl = new MeshKit();
            fl.Plane(new Vector3(0, 0.008f, zc), new Vector2(X * 2, depth), Quaternion.identity, Color.white, 0f, new Rect(0, 0, 1.2f, 1.1f));
            fl.Spawn("CabinFloor", transform, Palette.Marble, false);
            var k = new MeshKit();
            var trim = Palette.Gold;
            k.Quad(new Vector3(0, Hgt, zc), new Vector2(X * 2, depth), Facing(Vector3.down), Palette.Hex("140F1E"), 0.05f);
            k.Quad(new Vector3(0, Hgt - 0.01f, zc), new Vector2(X * 1.3f, depth * 0.7f), Facing(Vector3.down), Palette.Hex("FFF1DA"), 1.3f);   // light panel
            k.Quad(new Vector3(0, 1.5f, Z1 - 0.01f), new Vector2(X * 1.5f, 1.25f), Facing(Vector3.back), Palette.Hex("2E2A44"), 0.35f);       // mirror
            k.Bar(new Vector3(-X * 0.75f, 0.875f, Z1 - 0.012f), new Vector3(X * 0.75f, 0.875f, Z1 - 0.012f), 0.012f, trim, 1f);
            k.Bar(new Vector3(-X * 0.75f, 2.125f, Z1 - 0.012f), new Vector3(X * 0.75f, 2.125f, Z1 - 0.012f), 0.012f, trim, 1f);
            // handrails on three walls + kick plates + corner trims (the right rail stops short of the floor panel)
            k.Bar(new Vector3(-X + 0.06f, 0.95f, Z1 - 0.06f), new Vector3(X - 0.06f, 0.95f, Z1 - 0.06f), 0.022f, trim, 0.8f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Bar(new Vector3(s * (X - 0.06f), 0.95f, s > 0 ? Z0 + 1.08f : Z0 + 0.3f), new Vector3(s * (X - 0.06f), 0.95f, Z1 - 0.06f), 0.022f, trim, 0.8f);
                k.Box(new Vector3(s * (X - 0.01f), 0.08f, zc), new Vector3(0.02f, 0.16f, depth), Palette.Metal, 0.1f);
                k.Bar(new Vector3(s * X, 0, Z1), new Vector3(s * X, Hgt, Z1), 0.02f, trim, 0.9f);
                k.Bar(new Vector3(s * X, 0, Z0), new Vector3(s * X, Hgt, Z0), 0.03f, trim, 0.9f);
            }
            k.Box(new Vector3(0, 0.08f, Z1 - 0.01f), new Vector3(X * 2, 0.16f, 0.02f), Palette.Metal, 0.1f);
            k.Ring(new Vector3(0, 0.014f, zc), 0.5f, 0.55f, 40, Quaternion.identity, trim, 1f);
            k.Bar(new Vector3(-X, Hgt - 0.02f, Z0), new Vector3(X, Hgt - 0.02f, Z0), 0.03f, trim, 0.9f);
            k.Spawn("CabinDetails", transform, Palette.Glossy, false);
            var l = new GameObject("CabinLight").AddComponent<Light>();
            l.transform.SetParent(transform, false); l.transform.localPosition = new Vector3(0, Hgt - 0.25f, zc);
            l.type = LightType.Point; l.range = 3.4f; l.intensity = 1.6f; l.color = Palette.Hex("FFE2C0"); l.shadows = LightShadows.None;
            // cabin walls block (the doorway stays open; the doors block when closed)
            Build.BoxCol(Build.Empty("CabinBack", transform, new Vector3(0, Hgt * 0.5f, Z1 + 0.1f)), Vector3.zero, new Vector3(X * 2 + 0.4f, Hgt, 0.2f));
            Build.BoxCol(Build.Empty("CabinL", transform, new Vector3(-X - 0.1f, Hgt * 0.5f, zc)), Vector3.zero, new Vector3(0.2f, Hgt, depth + 0.2f));
            Build.BoxCol(Build.Empty("CabinR", transform, new Vector3(X + 0.1f, Hgt * 0.5f, zc)), Vector3.zero, new Vector3(0.2f, Hgt, depth + 0.2f));

            // sliding doors (brushed metal with a gold seam), visible from both sides
            float dw = X;   // each door covers half the opening
            for (int s = -1; s <= 1; s += 2)
            {
                var dk = new MeshKit();
                dk.Box(Vector3.zero, new Vector3(dw, 2.6f, 0.06f), Palette.Metal, 0.12f);
                dk.Bar(new Vector3(-s * dw * 0.5f, -1.3f, -0.035f), new Vector3(-s * dw * 0.5f, 1.3f, -0.035f), 0.008f, Palette.Gold, 1f);
                dk.Bar(new Vector3(-s * dw * 0.5f, -1.3f, 0.035f), new Vector3(-s * dw * 0.5f, 1.3f, 0.035f), 0.008f, Palette.Gold, 1f);
                var d = dk.Spawn(s < 0 ? "DoorL" : "DoorR", transform, new Vector3(s * dw * 0.5f, 1.3f, Z0 - 0.04f), Quaternion.identity, Palette.BrushedMetal, false).transform;
                d.gameObject.AddComponent<BoxCollider>().size = new Vector3(dw, 2.6f, 0.08f);
                if (s < 0) doorL = d; else doorR = d;
            }

            // lintel over the doors (both faces) carrying the digital indicators: outside faces -Z, inside faces +Z
            var lin = new MeshKit();
            lin.Box(new Vector3(0, 2.84f, Z0 - 0.07f), new Vector3(X * 2 + 0.3f, 0.42f, 0.05f), Palette.Metal, 0.08f);
            lin.Bar(new Vector3(-X - 0.15f, 2.63f, Z0 - 0.1f), new Vector3(X + 0.15f, 2.63f, Z0 - 0.1f), 0.012f, Palette.Gold, 1f);
            lin.Spawn("Lintel", transform, Palette.BrushedMetal, false);
            var outside = Build.Empty("IndicatorOut", transform, new Vector3(0, 2.84f, Z0 - 0.095f), Quaternion.identity).transform;
            readouts.Add(BuildReadout(outside, 0.58f, 0.24f, 0.17f, false));
            var inside = Build.Empty("IndicatorIn", transform, new Vector3(0, 2.655f, Z0 + 0.022f), Quaternion.Euler(0, 180, 0)).transform;
            readouts.Add(BuildReadout(inside, 0.4f, 0.11f, 0.085f, false));
            // big hint on the back wall (you face it when you walk in)
            insideT = Txt.Make("Inside", transform, new Vector3(0, 2.36f, Z1 - 0.03f), "", 0.1f, Palette.Gold, Palette.Display, TextAlignmentOptions.Center, 2.1f);

            // cabin floor panel on the right wall, just inside the doors
            var cab = Build.Empty("CabinPanel", transform, new Vector3(X, 0, Z0 + 0.6f), Quaternion.Euler(0, 90, 0)).transform;
            BuildPanel(cab, true);
            if (floor == 0) gameObject.AddComponent<ElevatorDealer>();   // the lobby lift has a passenger who never gets off
            var tag = Txt.Make("Tag", transform, new Vector3(-X + 0.02f, 1.9f, zc), "ASCEND · EXPRESS LIFT", 0.05f, Palette.Gold, Palette.Display, TextAlignmentOptions.Center, 1.6f);
            tag.transform.localRotation = Quaternion.Euler(0, -90, 0);

            // call panels outside, beside the doors: take the place of the old ElevatorPanel objects (same pose)
            bool any = false;
            if (transform.parent != null)
                foreach (var old in transform.parent.GetComponentsInChildren<ElevatorPanel>(true))
                {
                    if (old == null || old.floor != floor) continue;
                    var root = Build.Empty("CallPanel", transform, transform.InverseTransformPoint(old.transform.position), Quaternion.Inverse(transform.rotation) * old.transform.rotation).transform;
                    BuildPanel(root, false);
                    old.gameObject.SetActive(false);
                    any = true;
                }
            if (!any)
            {
                BuildPanel(Build.Empty("CallPanelR", transform, new Vector3(2.0f, 0, -2.42f), Quaternion.Euler(0, -39f, 0)).transform, false);
                BuildPanel(Build.Empty("CallPanelL", transform, new Vector3(-2.0f, 0, -2.42f), Quaternion.Euler(0, 39f, 0)).transform, false);
            }
            RefreshKeys(true);
            RefreshReadouts();
        }

        // ------------------------------------------------------------------ panel construction

        /// <summary>Digital readout: dark glass housing, amber floor digits, ▲/▼ and (optionally) the floor name.</summary>
        Readout BuildReadout(Transform root, float w, float h, float numEm, bool withName)
        {
            var m = new MeshKit();
            m.Box(new Vector3(0, 0, 0.012f), new Vector3(w + 0.03f, h + 0.03f, 0.024f), Palette.Hex("0B0910"), 0.02f);
            m.Quad(new Vector3(0, 0, -0.001f), new Vector2(w, h), Quaternion.identity, Palette.Hex("140B03"), 0.25f);
            m.Bar(new Vector3(-w * 0.5f - 0.015f, h * 0.5f + 0.015f, -0.002f), new Vector3(w * 0.5f + 0.015f, h * 0.5f + 0.015f, -0.002f), 0.006f, Palette.Gold, 1f);
            m.Bar(new Vector3(-w * 0.5f - 0.015f, -h * 0.5f - 0.015f, -0.002f), new Vector3(w * 0.5f + 0.015f, -h * 0.5f - 0.015f, -0.002f), 0.006f, Palette.Gold, 1f);
            m.Spawn("Housing", root, Palette.Glossy, false);
            var r = new Readout();
            float numX = withName ? -w * 0.5f + numEm * 0.75f : -numEm * 0.25f;
            r.num = Txt.Make("Digits", root, new Vector3(numX, 0, -0.005f), "", numEm, Amber, Palette.Mono, TextAlignmentOptions.Center, numEm * 1.6f);
            r.num.fontStyle = FontStyles.Bold;
            r.arrow = Txt.Make("Arrow", root, new Vector3(numX + numEm * 0.95f, 0, -0.005f), "", numEm * 0.6f, Amber, Palette.Body, TextAlignmentOptions.Center, numEm);
            if (withName)
            {
                float nx0 = numX + numEm * 1.5f, nw = w * 0.5f - 0.02f - nx0;
                r.name = Txt.Make("Name", root, new Vector3(nx0 + nw * 0.5f, 0, -0.005f), "", NameEm, Palette.Text, Palette.Display, TextAlignmentOptions.Left, nw);
                FitWidth(r.name, NameEm);
            }
            return r;
        }

        /// <summary>Shrinks a label only if a name would not fit its box (never below the 0.035 m cap height).</summary>
        static void FitWidth(TextMeshPro t, float em)
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = em * 10f;
            t.fontSizeMin = 0.05f * 10f;
            t.enableWordWrapping = false;
        }

        /// <summary>Premium floor panel: brushed-metal bezel, dark glass, gold frame, digital readout on top, one wide key
        /// per floor (big number on the key, the name beside it, lock + tier on locked floors), UP / DOWN in the cabin.</summary>
        void BuildPanel(Transform root, bool cabin)
        {
            float top = 2.0f, bottom = 0.76f, cy = (top + bottom) * 0.5f, ph = top - bottom;
            var m = new MeshKit();
            if (!cabin) m.Box(new Vector3(0, cy, 0.12f), new Vector3(PW + 0.08f, ph + 0.08f, 0.22f), Palette.Metal, 0.05f);   // mounts the call panel on the curved core
            m.Box(new Vector3(0, cy, -0.006f), new Vector3(PW + 0.07f, ph + 0.07f, 0.03f), Palette.Hex("8A8798"), 0.04f);
            m.Spawn("Bezel", root, Palette.BrushedMetal, false);
            var g = new MeshKit();
            g.Box(new Vector3(0, cy, -0.023f), new Vector3(PW, ph, 0.006f), GlassCol, 0.03f);
            float fz = -0.028f, hx = PW * 0.5f, t = 0.008f;
            g.Bar(new Vector3(-hx, top, fz), new Vector3(hx, top, fz), t, Palette.Gold, 0.9f);
            g.Bar(new Vector3(-hx, bottom, fz), new Vector3(hx, bottom, fz), t, Palette.Gold, 0.9f);
            g.Bar(new Vector3(-hx, bottom, fz), new Vector3(-hx, top, fz), t, Palette.Gold, 0.9f);
            g.Bar(new Vector3(hx, bottom, fz), new Vector3(hx, top, fz), t, Palette.Gold, 0.9f);
            if (cabin) g.Bar(new Vector3(-hx + 0.03f, RowTop - (Tower.Count - 1) * Pitch - 0.066f, fz), new Vector3(hx - 0.03f, RowTop - (Tower.Count - 1) * Pitch - 0.066f, fz), 0.004f, Palette.Gold, 0.6f);
            // per-floor colour stripe left of every key
            float keyX = -hx + 0.05f + KeyW * 0.5f;
            for (int i = 0; i < Tower.Count; i++)
            {
                int f = Tower.Count - 1 - i;
                float y = RowTop - i * Pitch;
                g.Box(new Vector3(keyX - KeyW * 0.5f - 0.016f, y, -0.03f), new Vector3(0.007f, KeyH * 0.9f, 0.006f), Tower.Floors[f].color, 1f);
                g.Box(new Vector3(keyX, y, -0.027f), new Vector3(KeyW + 0.012f, KeyH + 0.012f, 0.004f), Palette.Hex("05040A"), 0f);   // key well
            }
            g.Spawn("Glass", root, Palette.Glossy, false);

            // digital readout across the top
            var ro = Build.Empty("Readout", root, new Vector3(0, top - 0.095f, -0.03f), Quaternion.identity).transform;
            readouts.Add(BuildReadout(ro, PW - 0.08f, 0.13f, 0.1f, true));

            // floor keys (top floor first)
            var keys = new LiftKey[Tower.Count];
            for (int i = 0; i < Tower.Count; i++)
            {
                int f = Tower.Count - 1 - i;
                var fi = Tower.Floors[f];
                float y = RowTop - i * Pitch;
                var key = LiftKey.Create("Key" + (f + 1), root, new Vector3(0, y, 0f), new Vector3(keyX, 0, -0.037f), new Vector2(KeyW, KeyH),
                    new Vector3(0, 0, -0.035f), new Vector3(PW - 0.04f, Pitch * 0.92f, 0.05f),
                    (f + 1).ToString(), Palette.Display, NumEm, cabin ? (Action)(() => Ride(f)) : () => Call(f));
                // name beside the key
                float nx0 = keyX + KeyW * 0.5f + 0.026f, nw = 0.29f;
                key.name_ = Txt.Make("Name", key.transform, new Vector3(nx0 + nw * 0.5f, 0, -0.031f), fi.name.Split('·')[0].Trim(), NameEm, Palette.Text, Palette.Display, TextAlignmentOptions.Left, nw);
                FitWidth(key.name_, NameEm);
                // lock icon + tier name on the right (shown only while the floor is locked)
                float tierR = hx - 0.035f, tw = 0.18f;
                key.tier = Txt.Make("Tier", key.transform, new Vector3(tierR - tw * 0.5f, 0, -0.031f), GameState.TierNames[(int)fi.tier], TierEm, Palette.TierColor(fi.tier), Palette.Display, TextAlignmentOptions.Right, tw);
                FitWidth(key.tier, TierEm);
                key.lockIcon = BuildLock(key.transform, new Vector3(tierR - tw - 0.035f, 0, -0.031f), Palette.TierColor(fi.tier));
                keys[f] = key;
            }
            panels.Add(keys);

            if (cabin)
            {
                float y = RowTop - Tower.Count * Pitch - 0.035f;
                upKey = LiftKey.Create("Up", root, new Vector3(-0.17f, y, 0f), new Vector3(0, 0, -0.037f), new Vector2(0.3f, 0.08f),
                    new Vector3(0, 0, -0.035f), new Vector3(0.33f, 0.09f, 0.05f), "▲  UP", Palette.Body, 0.05f, () => Ride(floor + 1));
                downKey = LiftKey.Create("Down", root, new Vector3(0.17f, y, 0f), new Vector3(0, 0, -0.037f), new Vector2(0.3f, 0.08f),
                    new Vector3(0, 0, -0.035f), new Vector3(0.33f, 0.09f, 0.05f), "▼  DOWN", Palette.Body, 0.05f, () => Ride(floor - 1));
                upKey.label.fontStyle = FontStyles.Bold; downKey.label.fontStyle = FontStyles.Bold;
            }
            else
            {
                Txt.Make("Hint", root, new Vector3(0, RowTop - Tower.Count * Pitch - 0.03f, -0.031f), "CALL THE LIFT · PICK A FLOOR", 0.05f, Palette.Gold, Palette.Display, TextAlignmentOptions.Center, PW - 0.06f);
            }
        }

        /// <summary>Padlock drawn with geometry (no font glyph needed).</summary>
        static GameObject BuildLock(Transform parent, Vector3 pos, Color c)
        {
            var k = new MeshKit();
            k.Box(new Vector3(0, -0.008f, 0), new Vector3(0.026f, 0.022f, 0.006f), c, 0.9f);
            k.Box(new Vector3(0, -0.009f, -0.0035f), new Vector3(0.005f, 0.009f, 0.002f), Palette.Hex("0A0812"), 0f);
            k.Bar(new Vector3(-0.0085f, 0.003f, 0), new Vector3(-0.0085f, 0.017f, 0), 0.0045f, c, 0.9f);
            k.Bar(new Vector3(0.0085f, 0.003f, 0), new Vector3(0.0085f, 0.017f, 0), 0.0045f, c, 0.9f);
            k.Bar(new Vector3(-0.0108f, 0.0175f, 0), new Vector3(0.0108f, 0.0175f, 0), 0.0045f, c, 0.9f);
            return k.Spawn("Lock", parent, pos, Quaternion.identity, Palette.Neon, false);
        }

        // ------------------------------------------------------------------ state → visuals

        void RefreshKeys(bool force)
        {
            if (!GameState.Exists) return;
            if (lockedState == null) lockedState = new bool[Tower.Count];
            bool changed = force;
            for (int f = 0; f < Tower.Count; f++)
            {
                bool lk = !GameState.I.CanAccess(Tower.Floors[f].tier);
                if (lk != lockedState[f]) { lockedState[f] = lk; changed = true; }
            }
            if (!changed) return;
            foreach (var keys in panels)
                for (int f = 0; f < keys.Length; f++) PaintKey(keys[f], f, false);
            if (upKey != null) PaintMove(upKey, floor + 1);
            if (downKey != null) PaintMove(downKey, floor - 1);
        }

        void PaintMove(LiftKey key, int target)
        {
            bool ok = target >= 0 && target < Tower.Count;
            bool lk = ok && lockedState != null && lockedState[target];
            if (!ok) key.Paint(KeyLocked, 0.02f, Palette.A(Palette.Muted, 1f) * 0.6f);
            else if (lk) key.Paint(KeyLocked, 0.05f, Palette.Muted);
            else key.Paint(KeyIdle, 0.12f, Palette.Text);
        }

        void PaintKey(LiftKey key, int f, bool blinkOn)
        {
            if (key == null) return;
            bool lk = lockedState != null && lockedState[f];
            bool here = f == floor && rideTarget < 0;
            if (f == rideTarget) key.Paint(blinkOn ? Tower.Floors[f].color : KeyIdle, blinkOn ? 1f : 0.2f, blinkOn ? InkDark : Palette.Text);
            else if (here) key.Paint(Palette.Gold, 1f, InkDark);   // lit: you are here
            else if (lk) key.Paint(KeyLocked, 0.03f, Palette.Muted * 0.75f);
            else key.Paint(KeyIdle, 0.12f, Palette.Text);
            if (key.name_ != null) key.name_.color = here ? Palette.Gold : lk ? Palette.Muted : Palette.Text;
            if (key.tier != null) key.tier.gameObject.SetActive(lk);
            if (key.lockIcon != null) key.lockIcon.SetActive(lk);
        }

        void RefreshReadouts()
        {
            string arrow = shownDir > 0 ? "▲" : shownDir < 0 ? "▼" : "";
            string num = toBasement ? "-1" : (shownFloor + 1).ToString();
            string nm = toBasement ? "SÓTANO" : Tower.Floors[Mathf.Clamp(shownFloor, 0, Tower.Count - 1)].name.Split('·')[0].Trim();
            foreach (var r in readouts)
            {
                if (r.num.text != num) r.num.text = num;
                if (r.arrow.text != arrow) r.arrow.text = arrow;
                if (r.name != null && r.name.text != nm) r.name.text = nm;
            }
        }

        bool PlayerInside(out Vector3 local)
        {
            local = Vector3.zero;
            if (!Rig.Ready) return false;
            local = transform.InverseTransformPoint(Rig.I.Head.position);
            return local.x > -X && local.x < X && local.z > Z0 - 0.05f && local.z < Z1 && local.y > 0f && local.y < Hgt + 0.5f;
        }

        void Update()
        {
            if (dirty && !myRide) { dirty = false; ResetPanel(); }
            if (Time.time >= nextLockCheck) { nextLockCheck = Time.time + 1f; RefreshKeys(false); }
            if (rideTarget >= 0)
            {
                bool on = Mathf.Repeat(Time.time, 0.5f) < 0.3f;
                foreach (var keys in panels) PaintKey(keys[rideTarget], rideTarget, on);
                foreach (var r in readouts) r.arrow.enabled = on;
            }
            if (!Rig.Ready) return;
            Vector3 local;
            bool inside = PlayerInside(out local);
            var head = transform.InverseTransformPoint(Rig.I.Head.position);
            bool near = inside || (Mathf.Abs(head.x) < 1.6f && head.z < Z0 && head.z > Z0 - 2.0f);
            bool want = !holdClosed && (near || Time.time < closeAt);
            float prev = open;
            open = Mathf.MoveTowards(open, want ? 1f : 0f, Time.deltaTime * 1.4f);
            if (prev == 0f && open > 0f) Sfx.Play("elevator_doors", transform.TransformPoint(new Vector3(0, 1.4f, Z0)), 0.6f);
            float e = Fx.EaseInOut(open);
            doorL.localPosition = new Vector3(Mathf.Lerp(-X * 0.5f, -X * 1.45f, e), 1.3f, Z0 - 0.04f);
            doorR.localPosition = new Vector3(Mathf.Lerp(X * 0.5f, X * 1.45f, e), 1.3f, Z0 - 0.04f);
            if (insideT != null)
            {
                string s = riding ? "" : inside ? "FLOOR " + (floor + 1) + " · SELECT A FLOOR ON THE RIGHT-HAND PANEL" : "";
                if (insideT.text != s) insideT.text = s;
            }
        }

        // ------------------------------------------------------------------ travel

        /// <summary>Outside call panel: fade-travel to a floor (same rules as the wrist menu).</summary>
        void Call(int target)
        {
            if (FloorManager.I == null) return;
            if (target == floor) { Sfx.UI("ui_error", 0.5f); GameState.I.Notify("YOU ARE ON FLOOR " + (floor + 1), Palette.Gold); return; }
            FloorManager.I.GoTo(target);
        }

        public void Ride(int target)
        {
            if (target == floor && !riding) { Sfx.UI("ui_error", 0.4f); GameState.I.Notify("YOU ARE ON FLOOR " + (floor + 1), Palette.Gold); return; }
            if (riding || target == floor || target < 0 || target >= Tower.Count || IntroCinematic.Playing) { Sfx.UI("ui_error", 0.4f); return; }
            if (FloorManager.I == null || !FloorManager.I.Allowed(target)) return;
            Vector3 local;
            if (!PlayerInside(out local)) { GameState.I.Notify("STEP INTO THE LIFT FIRST", Palette.Gold); return; }
            StartCoroutine(RideRoutine(target));
        }

        IEnumerator RideRoutine(int target)
        {
            riding = true; myRide = true; holdClosed = true;
            rideTarget = target;
            Sfx.UI("ui_click", 0.5f);
            Sfx.Play("elevator_doors", transform.TransformPoint(new Vector3(0, 1.4f, Z0)), 0.7f);
            Vector3 local;
            while (open > 0f)
            {
                // stepped out while the doors were closing: cancel instead of dragging the player through the shaft
                if (!PlayerInside(out local)) { holdClosed = false; CancelRide(); GameState.I.Notify("STEP INTO THE LIFT FIRST", Palette.Gold); yield break; }
                yield return null;
            }
            var rig = Rig.I;
            int dir = target > floor ? 1 : -1;
            float dur = 1.2f + 0.45f * Mathf.Abs(target - floor);
            hum = Sfx.Loop("rocket_loop", rig.Head, 0.18f, 0f);
            if (hum != null) hum.pitch = 0.45f;
            shownDir = dir;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                int shown = Mathf.RoundToInt(Mathf.Lerp(floor, target, Fx.EaseInOut(t / dur)));
                if (shown != shownFloor) { shownFloor = shown; Sfx.UI("ui_click", 0.15f, 1.6f); }
                RefreshReadouts();
                if (Time.frameCount % 3 == 0) XRIn.HapticBoth(0.05f, 0.02f);
                yield return null;
            }
            if (hum != null) { Destroy(hum.gameObject); hum = null; }
            // jump the rig to the same spot in the next floor's cabin and swap floors
            float dy = Tower.Y(target) - Tower.Y(floor);
            var cc = rig.GetComponent<CharacterController>(); bool on = cc != null && cc.enabled; if (on) cc.enabled = false;
            rig.transform.position += Vector3.up * dy;
            Physics.SyncTransforms();
            if (on) cc.enabled = true;
            holdClosed = false; riding = false; myRide = false;
            ResetPanel();
            FloorManager.I.Activate(target, true);
            foreach (var e in all) if (e != null && e.floor == target) e.Arrive();
            Sfx.UI("elevator_ding", 0.9f);
            Voice.Announce(Tower.Floors[target].voice);
        }

        void CancelRide() { toBasement = false; EndRide(); ResetPanel(); }

        // ------------------------------------------------------------------ the unlisted stop (EL CAMELLO's service)
        bool toBasement;

        /// <summary>Off-the-books ride to the basement: doors close, the readout drops to -1, the cabin hums down, and
        /// <paramref name="arrive"/> takes the player out of the shaft. <paramref name="cancelled"/> runs if the player
        /// steps out while the doors are closing.</summary>
        public bool RideDown(Action arrive, Action cancelled)
        {
            Vector3 local;
            if (riding || IntroCinematic.Playing || !PlayerInside(out local)) return false;
            StartCoroutine(RideDownRoutine(arrive, cancelled));
            return true;
        }

        IEnumerator RideDownRoutine(Action arrive, Action cancelled)
        {
            riding = true; myRide = true; holdClosed = true; toBasement = true; shownDir = -1;
            RefreshReadouts();
            Sfx.Play("elevator_doors", transform.TransformPoint(new Vector3(0, 1.4f, Z0)), 0.7f);
            Vector3 local;
            while (open > 0f)
            {
                if (!PlayerInside(out local)) { holdClosed = false; CancelRide(); if (cancelled != null) cancelled(); yield break; }
                yield return null;
            }
            hum = Sfx.Loop("rocket_loop", Rig.I.Head, 0.2f, 0f);
            if (hum != null) hum.pitch = 0.32f;
            for (float t = 0; t < 2.6f; t += Time.deltaTime)
            {
                if (Time.frameCount % 2 == 0) XRIn.HapticBoth(0.08f, 0.02f);
                yield return null;
            }
            if (hum != null) { Destroy(hum.gameObject); hum = null; }
            Sfx.UI("elevator_ding", 0.9f);
            if (arrive != null) arrive();
            holdClosed = false; riding = false; myRide = false; toBasement = false;
            ResetPanel();
        }

        /// <summary>Back to idle: current floor lit, no arrow, nothing blinking.</summary>
        void ResetPanel()
        {
            rideTarget = -1; shownDir = 0; shownFloor = floor;
            foreach (var r in readouts) r.arrow.enabled = true;
            RefreshKeys(true);
            RefreshReadouts();
        }

        /// <summary>Called on the destination floor's lift: keep the doors shut a moment, then open.</summary>
        public void Arrive()
        {
            open = 0f; closeAt = Time.time + 4f;
            ResetPanel();
            StartCoroutine(OpenLater());
        }

        IEnumerator OpenLater() { holdClosed = true; yield return new WaitForSeconds(0.35f); holdClosed = false; }
    }
}
