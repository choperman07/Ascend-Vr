using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>Proportions of a character body that matter for sitting (metres, measured from the actual rig).</summary>
    public struct BodyDims
    {
        public float stature, thigh, shin, ankle, hipW, arm;
        public static BodyDims Default(float stature)
        {
            float s = stature / 1.75f;
            return new BodyDims { stature = stature, thigh = 0.44f * s, shin = 0.43f * s, ankle = 0.08f * s, hipW = 0.09f * s, arm = 0.56f * s };
        }
    }

    /// <summary>Where every part of a seated body goes, in seat space (origin on the floor under the seat, +Z = facing).
    /// Computed once when somebody sits, from the seat's real furniture and the sitter's real proportions.</summary>
    public struct SitPlan
    {
        public bool valid;
        public Vector3 hips;            // hip-joint midpoint
        public Vector3 footL, footR;    // ankle targets (on the floor or on the stool's foot ring)
        public Vector3 kneeL, kneeR;    // knee positions (IK hints)
        public Vector3 handL, handR;    // where the hands rest (table rail / counter, or the thighs)
        public Vector3 legDir;          // where the knees point
        public float lean;              // torso pitch in degrees: + leans in over the table, - reclines into the backrest
        public float handW;             // how firmly the hands rest (0..1)
        public bool atTable;            // hands rest on a table / counter in front
    }

    /// <summary>A place to sit: casino stool at a table, bar stool, lounge chair or one place on a sofa. The transform sits
    /// on the floor under the hips with forward facing the table / bar / room. Guests sit here (real seated pose: hips on the
    /// cushion, back against the backrest, thighs over the cushion, feet on the foot ring or the floor, never through the
    /// table) and the player can take any free seat (SIT button / teleport spot).</summary>
    public class Seat : MonoBehaviour
    {
        [Tooltip("Height of the cushion surface above the floor")] public float height = 0.62f;
        [Tooltip("Where the hips go along the seat's forward axis")] public float depth = -0.02f;
        public bool tableEdge = true;
        [Tooltip("Table top height relative to the floor")] public float tableTop = 0.9f;
        [Tooltip("Front face of the backrest along forward (seat space); below -1 = no backrest")] public float back = -0.25f;
        [Tooltip("Front edge of the cushion along forward (seat space)")] public float front = 0.25f;
        [Tooltip("Height of the foot ring the feet rest on; 0 = feet on the floor")] public float footY = 0f;
        [Tooltip("Front of the foot ring along forward (seat space)")] public float footZ = 0.16f;
        public bool forPlayer;
        public TableGame table;
        [HideInInspector] public AvatarBody occupant;
        [HideInInspector] public bool playerOn;
        public bool Free { get { return occupant == null && !forPlayer && !playerOn; } }

        public static readonly List<Seat> All = new List<Seat>();
        void OnEnable() { All.Add(this); }
        // the occupant is kept while the floor is switched off: clearing it made the stool look free on return, so the
        // bot director sat a second guest on top of a poker regular still sitting there (destroyed guests read as null)
        void OnDisable() { All.Remove(this); }

        public bool Take(AvatarBody b)
        {
            if (!Free || b == null) return false;
            occupant = b;
            b.SitOn(this);
            return true;
        }

        public void Leave()
        {
            if (occupant != null) { var o = occupant; occupant = null; o.SitOn(null); }
        }

        /// <summary>Seat space → world (the seat transform's position and yaw, never its scale).</summary>
        public Vector3 W(Vector3 local) { return transform.position + transform.rotation * local; }

        // ------------------------------------------------------------------ furniture
        public enum Style { CasinoStool, BarStool, VipChair }

        /// <summary>Builds a seat with its furniture: padded leather cushion, gold-trimmed backrest, chrome column and
        /// foot ring. Two draw calls (leather + metal); everything is static-batched by the builder.</summary>
        public static Seat Create(string name, Transform parent, Vector3 localPos, float yaw, Style style, float tableTop = 0.9f, bool withCollider = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var seat = go.AddComponent<Seat>();
            seat.tableTop = tableTop;
            float h = style == Style.BarStool ? 0.78f : style == Style.VipChair ? 0.5f : 0.64f;
            seat.tableEdge = style != Style.VipChair;

            if (style != Style.VipChair && Props.Has("Chair_06"))
            {
                // the Casino FREE stool (red cushion, backrest, chrome foot ring), scaled so the cushion sits at the old height.
                // Measured from its triangles: cushion top 0.56, backrest face 0.27 behind the centre, front edge 0.25,
                // foot ring at 0.2 with a 0.28 radius.
                // bar stools are stretched upwards only, so they stay slim instead of swallowing the guest sitting on them
                float k = style == Style.BarStool ? 1.4f : 1.1f, kw = style == Style.BarStool ? 1.08f : 1.1f;
                var st = Props.Place("Chair_06", go.transform, Vector3.zero, 0f, 1f, false);
                if (st != null) st.transform.localScale = Vector3.Scale(st.transform.localScale, new Vector3(kw, k, kw));
                seat.height = 0.56f * k; seat.back = -0.27f * kw; seat.front = 0.25f * kw;
                seat.footY = 0.2f * k + 0.01f; seat.footZ = 0.27f * kw;
                seat.depth = seat.back + 0.11f;
                var pk = new GameObject("SitPick"); pk.transform.SetParent(go.transform, false);
                var pcol = pk.AddComponent<BoxCollider>(); pcol.isTrigger = true;
                pcol.size = new Vector3(0.62f, seat.height + 0.25f, 0.62f); pcol.center = new Vector3(0, (seat.height + 0.25f) * 0.5f, 0);
                return seat;
            }
            var leather = new MeshKit { boxUV = 2.2f };
            var metal = new MeshKit();
            if (style == Style.VipChair)
            {
                leather.Box(new Vector3(0, h - 0.06f, -0.02f), new Vector3(0.62f, 0.16f, 0.58f), Color.white, 0f);
                leather.Box(new Vector3(0, h + 0.32f, -0.3f), new Vector3(0.62f, 0.62f, 0.14f), Quaternion.Euler(-8f, 0, 0), Color.white, 0f);
                for (int s = -1; s <= 1; s += 2) leather.Box(new Vector3(s * 0.33f, h + 0.08f, -0.04f), new Vector3(0.1f, 0.22f, 0.56f), Color.white, 0f);
                metal.Box(new Vector3(0, h * 0.45f, -0.02f), new Vector3(0.66f, h * 0.7f, 0.6f), Palette.Hex("120E1A"), 0f);
                metal.Bar(new Vector3(-0.33f, h + 0.2f, 0.24f), new Vector3(0.33f, h + 0.2f, 0.24f), 0.012f, Palette.Gold, 0.4f);
                // cushion top h + 0.02, backrest front face ~0.22 behind the centre at lumbar height, front edge 0.27
                seat.height = h + 0.02f; seat.back = -0.215f; seat.front = 0.27f; seat.footY = 0f;
            }
            else
            {
                float r = style == Style.BarStool ? 0.21f : 0.24f;
                leather.Cylinder(new Vector3(0, h - 0.045f, 0), r, r * 0.96f, 0.09f, 28, Quaternion.identity, Color.white, 0f);
                leather.Sphere(new Vector3(0, h, 0), new Vector3(r * 0.96f, 0.035f, r * 0.96f), 18, Color.white, 0f);
                // curved backrest
                for (int i = 0; i < 9; i++)
                {
                    float a0 = Mathf.Lerp(-62f, 62f, i / 9f), a1 = Mathf.Lerp(-62f, 62f, (i + 1) / 9f);
                    var p0 = Quaternion.Euler(0, a0 + 180f, 0) * Vector3.forward * (r + 0.01f);
                    var p1 = Quaternion.Euler(0, a1 + 180f, 0) * Vector3.forward * (r + 0.01f);
                    var mid = (p0 + p1) * 0.5f;
                    leather.Box(mid + Vector3.up * (h + 0.2f), new Vector3((p1 - p0).magnitude + 0.005f, 0.24f, 0.05f), Quaternion.LookRotation(-mid.normalized), Color.white, 0f);
                }
                metal.Torus(new Vector3(0, h + 0.33f, 0), r + 0.012f, 0.012f, 28, 6, Quaternion.identity, Palette.Gold, 0.35f, 130f);
                metal.Cylinder(new Vector3(0, h - 0.11f, 0), 0.07f, 0.05f, 0.05f, 16, Quaternion.identity, Palette.Silver, 0f);
                metal.Cylinder(new Vector3(0, (h - 0.13f) * 0.5f, 0), 0.03f, 0.03f, h - 0.13f, 14, Quaternion.identity, Palette.Silver, 0f);
                metal.Cylinder(new Vector3(0, 0.02f, 0), 0.24f, 0.26f, 0.04f, 28, Quaternion.identity, Palette.Hex("2A2632"), 0f);
                metal.Torus(new Vector3(0, h * 0.42f, 0), 0.2f, 0.012f, 24, 6, Quaternion.identity, Palette.Silver, 0f);
                for (int s = 0; s < 3; s++)
                {
                    var d = Quaternion.Euler(0, s * 120f, 0) * Vector3.forward;
                    metal.Bar(d * 0.03f + Vector3.up * h * 0.42f, d * 0.2f + Vector3.up * h * 0.42f, 0.012f, Palette.Silver, 0f);
                }
                metal.Torus(new Vector3(0, 0.045f, 0), 0.25f, 0.006f, 28, 4, Quaternion.identity, Palette.Gold, 0.9f);
                // measured from the geometry above: the padded top bulges 3 cm over the cushion rim, the backrest's inner
                // face is r - 1.5 cm behind the centre, and the feet rest on the front of the foot ring (r 0.2 at 0.42 h)
                seat.height = h + 0.03f; seat.back = -(r - 0.015f); seat.front = r;
                seat.footY = h * 0.42f + 0.012f; seat.footZ = 0.2f;
            }
            seat.depth = seat.back + 0.11f;
            leather.Spawn("Cushion", go.transform, Palette.Leather);
            metal.Spawn("Frame", go.transform, Palette.BrushedMetal);
            // pick volume: aim at it (VR) or click it (desktop) to sit
            var pick = new GameObject("SitPick"); pick.transform.SetParent(go.transform, false);
            var pc = pick.AddComponent<BoxCollider>(); pc.isTrigger = true;
            pc.size = new Vector3(0.62f, h + 0.25f, 0.62f); pc.center = new Vector3(0, (h + 0.25f) * 0.5f, 0);
            // no solid collider: a stool must never feel like an invisible wall when you walk up to sit
            // (table bodies, counters and walls already keep you out of the furniture)
            return seat;
        }

        // ------------------------------------------------------------------ fitting to imported furniture
        /// <summary>Measures an imported chair / sofa (its real triangles, not its bounds: prop pivots and sizes vary) and
        /// fits this seat to one place on it: the transform moves onto the cushion under the hips, and height / back /
        /// front are set from the surfaces actually found. lateral = offset of this place along the prop's right axis.
        /// Returns false (seat untouched) when no cushion is found there.</summary>
        public bool FitTo(GameObject furniture, float lateral)
        {
            if (furniture == null) return false;
            // probe in a frame on the floor under this place, facing the way the furniture faces
            var rot = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            var basePos = furniture.transform.position + furniture.transform.rotation * Vector3.right * lateral;
            basePos.y = transform.position.y;
            var frame = Matrix4x4.TRS(basePos, rot, Vector3.one).inverse;
            Probe.Begin(new Bounds(new Vector3(0, 1.1f, 0), new Vector3(1.2f, 2.2f, 3.2f)));
            Probe.AddAll(furniture.transform, frame, null);
            // seat profile along the centre line of this place, from the front: the cushion runs until the surface
            // climbs steeply (the backrest) — soft front rolls and cushion bulges are not mistaken for it
            float frontZ = 0f, top = -1f;
            var ys = new List<float>(140);
            for (float z = 1.4f; z > -1.4f; z -= 0.02f)
            {
                float y;
                if (!Probe.TopY(0f, z, 1.6f, out y) || y < 0.18f) { if (ys.Count > 0) break; continue; }
                if (ys.Count == 0) frontZ = z;
                ys.Add(y);
            }
            if (ys.Count == 0) { Probe.End(); return false; }
            top = ys[0];
            for (int j = 1; j < ys.Count; j++)
            {
                bool steep = j >= 3 && ys[j] - ys[j - 3] > 0.05f;
                if ((steep && ys[j] > top + 0.03f) || ys[j] > top + 0.12f) break;
                top = Mathf.Max(top, ys[j]);
            }
            // backrest face at lumbar height (cushion + 0.18 / + 0.3): the frontmost hit coming from the front
            float backZ = -9f, bz;
            if (Probe.RayZFromFront(0f, top + 0.18f, frontZ, out bz)) backZ = bz;
            if (Probe.RayZFromFront(0f, top + 0.3f, frontZ, out bz)) backZ = backZ < -8f ? bz : Mathf.Max(backZ, bz);
            Probe.End();
            if (backZ < -8f) backZ = frontZ - 0.55f;
            // origin under the hips (default sitter, against the backrest), so the player's view sits where a guest's head is
            // (same rule as Plan for a 1.75 m sitter: against the backrest, or forward to the edge, or feet up on a very deep one)
            float hzBack = backZ + 0.11f, minHz = frontZ + 0.02f - 0.44f;
            float hipsZ = minHz - hzBack > 0.18f ? hzBack : Mathf.Max(hzBack, minHz);
            float shift = hipsZ - (-0.03f);
            transform.position = basePos + rot * new Vector3(0, 0, shift);
            transform.rotation = rot;
            height = top; depth = -0.03f;
            back = backZ - shift; front = frontZ - shift; footY = 0f;
            probed = false;
            return true;
        }

        /// <summary>How many sitting places fit on a sofa (1-3) and their lateral offsets, from the inner width between its
        /// armrests (measured just above the cushion, ignoring loose pillows).</summary>
        public static float[] SofaPlaces(GameObject sofa, float seatYaw)
        {
            float inner = 0.6f;
            if (sofa != null)
            {
                var rot = Quaternion.Euler(0, seatYaw, 0);
                var frame = Matrix4x4.TRS(sofa.transform.position, rot, Vector3.one).inverse;
                Probe.Begin(new Bounds(new Vector3(0, 1.1f, 0), new Vector3(4f, 2.2f, 3f)));
                Probe.AddAll(sofa.transform, frame, null);
                // cushion profile on the centre line, then outwards along a line 10 cm behind the front edge (clear of
                // loose pillows leaning on the backrest) until the surface rises into an armrest or drops away
                float top = -1f, frontZ = 0f, y;
                var ys = new List<float>(140);
                for (float z = 1.4f; z > -1.4f; z -= 0.02f)
                {
                    if (!Probe.TopY(0f, z, 1.6f, out y) || y < 0.18f) { if (ys.Count > 0) break; continue; }
                    if (ys.Count == 0) frontZ = z;
                    ys.Add(y);
                }
                float best = 0f;
                if (ys.Count > 0)
                {
                    top = ys[0];
                    for (int j = 1; j < ys.Count; j++)
                    {
                        bool steep = j >= 3 && ys[j] - ys[j - 3] > 0.05f;
                        if ((steep && ys[j] > top + 0.03f) || ys[j] > top + 0.12f) break;
                        top = Mathf.Max(top, ys[j]);
                    }
                    float zs = frontZ - 0.1f, er = 2f, el = 2f;
                    for (float x = 0f; x < 2f; x += 0.02f) if (!Probe.TopY(x, zs, 1.6f, out y) || y > top + 0.07f || y < top - 0.12f) { er = x; break; }
                    for (float x = 0f; x < 2f; x += 0.02f) if (!Probe.TopY(-x, zs, 1.6f, out y) || y > top + 0.07f || y < top - 0.12f) { el = x; break; }
                    best = Mathf.Min(er, el);
                }
                Probe.End();
                if (best > 0.2f) inner = best;
                else
                {
                    var b = new Bounds(sofa.transform.position, Vector3.zero);
                    foreach (var rr in sofa.GetComponentsInChildren<Renderer>()) b.Encapsulate(rr.bounds);
                    inner = Mathf.Max(0.3f, Mathf.Max(b.extents.x, b.extents.z) - 0.2f);
                }
            }
            int n = Mathf.Clamp(Mathf.FloorToInt(inner * 2f / 0.55f), 1, 3);
            var res = new float[n];
            for (int i = 0; i < n; i++) res[i] = (i - (n - 1) * 0.5f) * (inner * 2f / n);
            return res;
        }

        // ------------------------------------------------------------------ what is in front of the seat
        [System.NonSerialized] bool probed;
        [System.NonSerialized] float kneeClear = 9f, footClear = 9f, edgeZ = -1f, railY;
        [System.NonSerialized] readonly float[] felt = new float[10];

        /// <summary>Height of the playing surface (felt / counter top) at this distance in front of the seat, measured
        /// from the real table geometry (chips placed by guests sit exactly on it).</summary>
        public float SurfaceAt(float zLocal)
        {
            EnsureProbe();
            if (edgeZ < 0f) return table != null ? table.TableY : tableTop;
            float f = Mathf.Clamp((zLocal - edgeZ) / 0.06f, 0f, felt.Length - 1.001f);
            int i = (int)f;
            return Mathf.Lerp(felt[i], felt[i + 1], f - i);
        }

        /// <summary>Front edge of the table / counter in front (seat space z, -1 = none) and the height of its rail.</summary>
        public float EdgeZ { get { EnsureProbe(); return edgeZ; } }
        public float RailY { get { EnsureProbe(); return railY; } }

        void EnsureProbe()
        {
            if (probed) return;
            probed = true;
            kneeClear = footClear = 9f; edgeZ = -1f; railY = tableTop;
            if (!tableEdge) return;
            Transform root = table != null ? table.transform : transform.parent;
            if (root == null) return;
            var frame = Matrix4x4.TRS(transform.position, Quaternion.Euler(0, transform.eulerAngles.y, 0), Vector3.one).inverse;
            Probe.Begin(new Bounds(new Vector3(0, 0.8f, 0.7f), new Vector3(1.0f, 1.6f, 1.4f)));
            Probe.AddAll(root, frame, transform);
            float top = height, z;
            for (int xi = -1; xi <= 1; xi++)
            {
                float x = xi * 0.12f;
                for (int yi = 0; yi < 3; yi++) if (Probe.RayZ(x, top + 0.05f + yi * 0.09f, 0.0f, out z)) kneeClear = Mathf.Min(kneeClear, z);
                if (xi != 0) for (int yi = 0; yi < 3; yi++) if (Probe.RayZ(x, 0.05f + yi * Mathf.Max(0.05f, (top - 0.25f) * 0.5f), 0.0f, out z)) footClear = Mathf.Min(footClear, z);
            }
            // the rail / counter top: first place in front where two of three samples find a surface at hand height
            for (float zz = 0.12f; zz < 1.3f; zz += 0.02f)
            {
                int hits = 0; float lo = 9f, y;
                for (int xi = -1; xi <= 1; xi++)
                    if (Probe.TopY(xi * 0.14f, zz, top + 0.75f, out y) && y > top + 0.08f) { hits++; lo = Mathf.Min(lo, y); }
                if (hits >= 2) { edgeZ = zz; railY = lo; break; }
            }
            if (edgeZ > 0f && Probe.RayZ(0f, railY - 0.012f, 0f, out z) && z < edgeZ) edgeZ = z;
            if (table != null && edgeZ < 0f) { edgeZ = 0.42f; railY = table.TableY; }
            // the surface just inside the edge, every 6 cm: the lowest of three samples (a stray card or button is not the felt)
            float last = table != null ? table.TableY : railY;
            for (int i = 0; i < felt.Length; i++)
            {
                float zz = edgeZ + 0.03f + i * 0.06f, lo = 9f, y;
                for (int xi = -1; xi <= 1; xi++) if (Probe.TopY(xi * 0.05f, zz, railY + 0.06f, out y) && y > railY - 0.2f) lo = Mathf.Min(lo, y);
                felt[i] = lo < 8f ? lo : last; last = felt[i];
            }
            Probe.End();
        }

        /// <summary>The seated pose for a body with these proportions. side (+1 / -1) picks which way crossed / angled legs
        /// go when the table leaves no room for the knees.</summary>
        public SitPlan Plan(BodyDims d, int side)
        {
            EnsureProbe();
            var p = new SitPlan { valid = true };
            float sc = Mathf.Max(0.5f, d.stature / 1.75f);
            float hy = height + 0.075f * sc;                                   // hip joints sit ~7.5 cm over a (slightly giving) cushion
            bool hasBack = back > -1f;
            float hz = hasBack ? back + 0.11f * sc : depth;                    // pelvis against the backrest
            float minHz = front + 0.02f - d.thigh;                              // deep sofas: the knee pit reaches the front edge
            bool feetUp = false;
            if (hasBack && !tableEdge && minHz - hz > 0.18f * sc) { feetUp = true; p.lean = -6f; }   // very deep cushion: lounge back, feet up on it
            else if (hz < minHz) { p.lean = -Mathf.Clamp(Mathf.Atan2(minHz - hz, 0.45f * sc) * Mathf.Rad2Deg, 0f, 20f); hz = minHz; }
            else if (!tableEdge) p.lean = -5f;                                  // relaxed into the backrest
            else p.lean = 5f;                                                   // engaged: leaning in a little over the table
            float minBack = hasBack ? back + 0.085f * sc : hz - 0.06f;
            float yaw = 0f;
            for (int iter = 0; iter < 16; iter++)
            {
                Legs(ref p, d, sc, hy, hz, yaw * side, feetUp);
                if (kneeClear > 8f && footClear > 8f) break;
                float kneeFront = Mathf.Max(p.kneeL.z, p.kneeR.z) + 0.065f * sc;
                float toes = Mathf.Max(p.footL.z, p.footR.z) + 0.13f * sc;
                float needK = kneeFront - (kneeClear - 0.025f), needF = toes - (footClear - 0.02f);
                if (needK <= 0f && needF <= 0f) break;
                if (hz - minBack > 0.005f) { hz -= Mathf.Min(Mathf.Max(needK, needF), hz - minBack) + 0.002f; continue; }
                if (yaw >= 60f) break;
                yaw += 7.5f;                                                    // no knee room: angle the legs aside
            }
            p.hips = new Vector3(0, hy, hz);
            // hands: on the rail / counter if it is within reach, otherwise resting on the thighs
            float ez = edgeZ;
            if (tableEdge && ez > 0f)
            {
                var rest = new Vector3(0.16f * sc, railY + 0.018f, ez + 0.045f);
                for (int i = 0; i < 2; i++)
                {
                    // shoulder after leaning in (the torso pivots about the hips, ~0.5 m to the shoulders)
                    float la = p.lean * Mathf.Deg2Rad;
                    var sh = new Vector3(0.19f * sc, hy + 0.48f * sc * Mathf.Cos(la), hz + 0.02f + 0.48f * sc * Mathf.Sin(la));
                    float dist = (rest - sh).magnitude;
                    if (i == 0 && dist > d.arm * 0.9f) { p.lean = Mathf.Clamp(p.lean + Mathf.Asin(Mathf.Clamp01((dist - d.arm * 0.88f) / (0.48f * sc))) * Mathf.Rad2Deg, p.lean, 18f); continue; }
                    if (dist < d.arm * 1.04f)
                    {
                        p.atTable = true; p.handW = 0.7f;
                        p.handR = rest + Vector3.up * 0.03f; p.handL = new Vector3(-0.18f * sc, railY + 0.05f, ez + 0.04f);
                    }
                    break;
                }
            }
            if (!p.atTable)
            {
                p.handW = 0.9f;
                var up = Vector3.up * 0.08f * sc;
                p.handL = Vector3.Lerp(new Vector3(-d.hipW, hy, hz), p.kneeL, 0.82f) + up - Vector3.right * 0.01f;
                p.handR = Vector3.Lerp(new Vector3(d.hipW, hy, hz), p.kneeR, 0.82f) + up + Vector3.right * 0.01f;
            }
            return p;
        }

        void Legs(ref SitPlan p, BodyDims d, float sc, float hy, float hz, float yaw, bool feetUp)
        {
            var mid = new Vector3(0, hy, hz);
            var turn = Quaternion.Euler(0, yaw, 0);
            p.legDir = turn * Vector3.forward;
            for (int s = -1; s <= 1; s += 2)
            {
                var hip = new Vector3(s * d.hipW, hy, hz);
                float fx = s * (d.hipW + 0.025f * sc);
                Vector3 foot;
                if (feetUp)
                {
                    // heels resting on the cushion near its front edge, knees up
                    foot = new Vector3(fx * 1.3f, height + d.ankle * 0.9f, Mathf.Min(front - 0.08f, hz + (d.thigh + d.shin) * 0.9f));
                }
                else if (footY > 0.01f)
                {
                    // ball of the foot on the front of the foot ring (circle of radius footZ)
                    float rz = Mathf.Sqrt(Mathf.Max(0.0025f, footZ * footZ - fx * fx));
                    foot = new Vector3(fx, footY + d.ankle * 0.8f, rz - 0.035f * sc);
                }
                else
                {
                    // feet flat on the floor, shins near vertical (angled forward on low lounge seats)
                    float drop = hy - d.ankle;
                    float a = (drop > d.shin ? 4f : 14f) * Mathf.Deg2Rad;
                    float ky = d.ankle + d.shin * Mathf.Cos(a);
                    float dy = ky - hy;
                    float kz = hz + Mathf.Sqrt(Mathf.Max(0.01f, d.thigh * d.thigh - dy * dy));
                    foot = new Vector3(fx, d.ankle, kz + d.shin * Mathf.Sin(a));
                }
                // angled legs: rotate knee and foot about the vertical through the pelvis
                foot = mid + turn * (foot - mid);
                var hipT = mid + turn * (hip - mid);
                var knee = Knee(hipT, foot, d.thigh, d.shin, p.legDir + Vector3.up * 0.35f);
                if (s < 0) { p.footL = foot; p.kneeL = knee; } else { p.footR = foot; p.kneeR = knee; }
            }
        }

        /// <summary>Two-bone solve: the knee (or elbow) between root and end, bending towards pole.</summary>
        public static Vector3 Knee(Vector3 root, Vector3 end, float a, float b, Vector3 pole)
        {
            var dir = end - root; float dist = dir.magnitude;
            if (dist < 1e-4f) return root + pole.normalized * a;
            var n = dir / dist;
            dist = Mathf.Clamp(dist, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
            float x = (a * a - b * b + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, a * a - x * x));
            var pd = pole - n * Vector3.Dot(pole, n);
            if (pd.sqrMagnitude < 1e-6f) pd = Vector3.up;
            return root + n * x + pd.normalized * h;
        }

        // ------------------------------------------------------------------ triangle probe
        /// <summary>Axis-aligned rays against real mesh triangles, in a local frame (no physics: table colliders are
        /// solid prisms down to the floor, and imported furniture only has convex hulls). Triangles are gathered once into
        /// a shared buffer, clipped to a box, so each probe costs one pass over the meshes near the seat.</summary>
        static class Probe
        {
            static readonly List<Vector3> tri = new List<Vector3>(2048);
            static readonly List<Vector3> verts = new List<Vector3>(4096);
            static readonly List<int> idx = new List<int>(8192);
            static Bounds box;

            public static void Begin(Bounds localBox) { tri.Clear(); box = localBox; }
            public static void End() { tri.Clear(); verts.Clear(); idx.Clear(); }

            public static void AddAll(Transform root, Matrix4x4 worldToLocal, Transform skip)
            {
                var localToWorld = worldToLocal.inverse;
                // world AABB of the probe box, to skip meshes that are nowhere near
                var wb = new Bounds(localToWorld.MultiplyPoint3x4(box.center), Vector3.zero);
                for (int c = 0; c < 8; c++)
                    wb.Encapsulate(localToWorld.MultiplyPoint3x4(box.center + Vector3.Scale(box.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null || !mesh.isReadable) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || !mr.enabled || !wb.Intersects(mr.bounds)) continue;
                    if (skip != null && mf.transform.IsChildOf(skip)) continue;
                    var n = mf.name;
                    if (n.StartsWith("Glow") || n.StartsWith("Beam") || n == "StandHere" || n.StartsWith("Chip")) continue;
                    if (mf.GetComponentInParent<Seat>() != null || mf.GetComponentInParent<AvatarBody>() != null || mf.GetComponentInParent<Chip>() != null) continue;
                    if (mf.GetComponent<TMPro.TMP_Text>() != null || mf.GetComponentInParent<Canvas>() != null) continue;
                    var m = worldToLocal * mf.transform.localToWorldMatrix;
                    mesh.GetVertices(verts);
                    for (int i = 0; i < verts.Count; i++) verts[i] = m.MultiplyPoint3x4(verts[i]);
                    for (int sm = 0; sm < mesh.subMeshCount; sm++)
                    {
                        if (mesh.GetTopology(sm) != MeshTopology.Triangles) continue;
                        mesh.GetTriangles(idx, sm);
                        for (int i = 0; i + 2 < idx.Count; i += 3)
                        {
                            Vector3 a = verts[idx[i]], b = verts[idx[i + 1]], c = verts[idx[i + 2]];
                            var mn = Vector3.Min(a, Vector3.Min(b, c)); var mx = Vector3.Max(a, Vector3.Max(b, c));
                            if (mx.x < box.min.x || mn.x > box.max.x || mx.y < box.min.y || mn.y > box.max.y || mx.z < box.min.z || mn.z > box.max.z) continue;
                            tri.Add(a); tri.Add(b); tri.Add(c);
                        }
                    }
                }
            }

            // barycentric hit of the 2D point (u, v) in triangle (a, b, c); returns the interpolated third coordinate
            static bool Hit(float u, float v, Vector3 a, Vector3 b, Vector3 c, int ax, out float w)
            {
                // ax: 0 → (u,v) = (y,z) value x · 1 → (x,z) value y · 2 → (x,y) value z
                float au, av, bu, bv, cu, cv, aw, bw, cw;
                if (ax == 2) { au = a.x; av = a.y; bu = b.x; bv = b.y; cu = c.x; cv = c.y; aw = a.z; bw = b.z; cw = c.z; }
                else if (ax == 1) { au = a.x; av = a.z; bu = b.x; bv = b.z; cu = c.x; cv = c.z; aw = a.y; bw = b.y; cw = c.y; }
                else { au = a.y; av = a.z; bu = b.y; bv = b.z; cu = c.y; cv = c.z; aw = a.x; bw = b.x; cw = c.x; }
                w = 0f;
                float d = (bu - au) * (cv - av) - (cu - au) * (bv - av);
                if (Mathf.Abs(d) < 1e-9f) return false;
                float l1 = ((bu - u) * (cv - v) - (cu - u) * (bv - v)) / d;
                float l2 = ((cu - u) * (av - v) - (au - u) * (cv - v)) / d;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) return false;
                w = l1 * aw + l2 * bw + l3 * cw;
                return true;
            }

            /// <summary>Nearest surface along +Z from z0 on the line (x, y).</summary>
            public static bool RayZ(float x, float y, float z0, out float z)
            {
                z = float.MaxValue; bool any = false; float w;
                for (int i = 0; i < tri.Count; i += 3)
                    if (Hit(x, y, tri[i], tri[i + 1], tri[i + 2], 2, out w) && w >= z0 && w < z) { z = w; any = true; }
                return any;
            }

            /// <summary>Nearest surface along -Z coming from far in front (z &gt; zFrom - 0.05 ignored: the cushion's own front).</summary>
            public static bool RayZFromFront(float x, float y, float zFrom, out float z)
            {
                z = float.MinValue; bool any = false; float w;
                for (int i = 0; i < tri.Count; i += 3)
                    if (Hit(x, y, tri[i], tri[i + 1], tri[i + 2], 2, out w) && w < zFrom - 0.05f && w > z) { z = w; any = true; }
                return any;
            }

            /// <summary>Highest surface below yMax on the vertical line (x, z).</summary>
            public static bool TopY(float x, float z, float yMax, out float y)
            {
                y = float.MinValue; bool any = false; float w;
                for (int i = 0; i < tri.Count; i += 3)
                    if (Hit(x, z, tri[i], tri[i + 1], tri[i + 2], 1, out w) && w <= yMax && w > y) { y = w; any = true; }
                return any;
            }

            /// <summary>Nearest surface sideways (+X or -X) from the centre line at height y, depth z.</summary>
            public static bool RayX(float y, float z, bool positive, out float x)
            {
                x = positive ? float.MaxValue : float.MinValue; bool any = false; float w;
                for (int i = 0; i < tri.Count; i += 3)
                {
                    if (!Hit(y, z, tri[i], tri[i + 1], tri[i + 2], 0, out w)) continue;
                    if (positive ? (w > 0.02f && w < x) : (w < -0.02f && w > x)) { x = w; any = true; }
                }
                return any;
            }
        }
    }
}
