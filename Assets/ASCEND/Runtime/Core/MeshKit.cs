using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ascend
{
    /// <summary>Builds whole props as ONE mesh (one draw call) out of boxes, cylinders, spheres, tori and quads.
    /// Vertex colour RGB = albedo, A = emission (read by the ASCEND/Neon shader).</summary>
    public class MeshKit
    {
        readonly List<Vector3> v = new List<Vector3>(1024);
        readonly List<Vector3> n = new List<Vector3>(1024);
        readonly List<Vector2> uv = new List<Vector2>(1024);
        readonly List<Color> c = new List<Color>(1024);
        readonly List<int> t = new List<int>(2048);
        readonly List<int> boneOf = new List<int>(1024);
        /// <summary>Bone index assigned to vertices added from now on (rigid skinning for avatars).</summary>
        public int bone;
        readonly Stack<Matrix4x4> stack = new Stack<Matrix4x4>();
        Matrix4x4 M = Matrix4x4.identity;

        public int VertexCount { get { return v.Count; } }
        /// <summary>When > 0, every primitive added gets box-projected UVs in kit space at this many tiles per metre
        /// (so wood, leather, metal and carpet textures keep a real-world scale on any shape).</summary>
        public float boxUV;

        public MeshKit Push(Vector3 pos, Quaternion rot, Vector3 scale)
        {
            stack.Push(M);
            M = M * Matrix4x4.TRS(pos, rot, scale);
            return this;
        }
        public MeshKit Push(Vector3 pos, Quaternion rot) { return Push(pos, rot, Vector3.one); }
        public MeshKit Push(Vector3 pos) { return Push(pos, Quaternion.identity, Vector3.one); }
        public MeshKit Pop() { if (stack.Count > 0) M = stack.Pop(); return this; }

        static Color Col(Color col, float emit) { col.a = Mathf.Clamp01(emit); return col; }

        void Vtx(Vector3 p, Vector3 nn, Vector2 u, Color col)
        {
            var wp = M.MultiplyPoint3x4(p);
            var wn = M.MultiplyVector(nn).normalized;
            v.Add(wp);
            n.Add(wn);
            if (boxUV > 0f)
            {
                float ax = Mathf.Abs(wn.x), ay = Mathf.Abs(wn.y), az = Mathf.Abs(wn.z);
                if (ay >= ax && ay >= az) u = new Vector2(wp.x, wp.z);
                else if (ax >= az) u = new Vector2(wp.z * Mathf.Sign(wn.x), wp.y);
                else u = new Vector2(-wp.x * Mathf.Sign(wn.z), wp.y);
                u *= boxUV;
            }
            uv.Add(u);
            c.Add(col);
            boneOf.Add(bone);
        }

        void QuadRaw(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 nn, Color col, Rect r)
        {
            int i = v.Count;
            Vtx(a, nn, new Vector2(r.xMin, r.yMin), col);
            Vtx(b, nn, new Vector2(r.xMax, r.yMin), col);
            Vtx(cc, nn, new Vector2(r.xMax, r.yMax), col);
            Vtx(d, nn, new Vector2(r.xMin, r.yMax), col);
            t.Add(i); t.Add(i + 2); t.Add(i + 1);
            t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }

        static readonly Rect Unit = new Rect(0, 0, 1, 1);

        // ------------------------------------------------------------ primitives
        public MeshKit Box(Vector3 center, Vector3 size, Color col, float emit = 0f) { return Box(center, size, Quaternion.identity, col, emit); }

        public MeshKit Box(Vector3 center, Vector3 size, Quaternion rot, Color col, float emit = 0f, Rect? topUV = null)
        {
            Push(center, rot);
            var h = size * 0.5f; var cl = Col(col, emit);
            Vector3 p000 = new Vector3(-h.x, -h.y, -h.z), p100 = new Vector3(h.x, -h.y, -h.z), p110 = new Vector3(h.x, h.y, -h.z), p010 = new Vector3(-h.x, h.y, -h.z);
            Vector3 p001 = new Vector3(-h.x, -h.y, h.z), p101 = new Vector3(h.x, -h.y, h.z), p111 = new Vector3(h.x, h.y, h.z), p011 = new Vector3(-h.x, h.y, h.z);
            QuadRaw(p000, p100, p110, p010, Vector3.back, cl, Unit);     // -Z (front)
            QuadRaw(p101, p001, p011, p111, Vector3.forward, cl, Unit);  // +Z
            QuadRaw(p001, p000, p010, p011, Vector3.left, cl, Unit);     // -X
            QuadRaw(p100, p101, p111, p110, Vector3.right, cl, Unit);    // +X
            QuadRaw(p010, p110, p111, p011, Vector3.up, cl, topUV ?? Unit);  // +Y
            QuadRaw(p001, p101, p100, p000, Vector3.down, cl, Unit);     // -Y
            Pop();
            return this;
        }

        /// <summary>Quad facing -Z (readable by a viewer looking along +Z), centred at pos.</summary>
        public MeshKit Quad(Vector3 center, Vector2 size, Quaternion rot, Color col, float emit = 0f, Rect? uvRect = null)
        {
            Push(center, rot);
            var h = size * 0.5f;
            QuadRaw(new Vector3(-h.x, -h.y, 0), new Vector3(h.x, -h.y, 0), new Vector3(h.x, h.y, 0), new Vector3(-h.x, h.y, 0), Vector3.back, Col(col, emit), uvRect ?? Unit);
            Pop();
            return this;
        }

        /// <summary>Horizontal quad facing up (+Y), UV across X/Z.</summary>
        public MeshKit Plane(Vector3 center, Vector2 size, Quaternion rot, Color col, float emit = 0f, Rect? uvRect = null)
        {
            Push(center, rot);
            var h = size * 0.5f;
            QuadRaw(new Vector3(-h.x, 0, -h.y), new Vector3(h.x, 0, -h.y), new Vector3(h.x, 0, h.y), new Vector3(-h.x, 0, h.y), Vector3.up, Col(col, emit), uvRect ?? Unit);
            Pop();
            return this;
        }

        /// <summary>Cylinder (or cone when rTop != rBottom) along local Y, centred at pos. Caps get planar UVs (u = x, v = z).</summary>
        public MeshKit Cylinder(Vector3 center, float rBottom, float rTop, float height, int seg, Quaternion rot, Color col, float emit = 0f, bool caps = true, Color? capCol = null, float capEmit = -1f, Rect? capUV = null, Rect? sideUV = null)
        {
            var su = sideUV ?? Unit;
            Push(center, rot);
            var cl = Col(col, emit);
            var cc = Col(capCol ?? col, capEmit < 0 ? emit : capEmit);
            float y0 = -height * 0.5f, y1 = height * 0.5f;
            float slope = (rBottom - rTop) / Mathf.Max(height, 1e-4f);
            int start = v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                float s = Mathf.Sin(a), co = Mathf.Cos(a);
                var nn = new Vector3(s, slope, co).normalized;
                Vtx(new Vector3(s * rBottom, y0, co * rBottom), nn, new Vector2(su.xMin + su.width * i / seg, su.yMin), cl);
                Vtx(new Vector3(s * rTop, y1, co * rTop), nn, new Vector2(su.xMin + su.width * i / seg, su.yMax), cl);
            }
            for (int i = 0; i < seg; i++)
            {
                int a = start + i * 2;
                t.Add(a); t.Add(a + 3); t.Add(a + 1);
                t.Add(a); t.Add(a + 2); t.Add(a + 3);
            }
            if (caps)
            {
                var r = capUV ?? Unit;
                if (rTop > 0.0001f) Fan(y1, rTop, seg, Vector3.up, cc, r, false);
                if (rBottom > 0.0001f) Fan(y0, rBottom, seg, Vector3.down, cc, r, true);
            }
            Pop();
            return this;
        }
        public MeshKit Cylinder(Vector3 center, float r, float height, int seg, Color col, float emit = 0f)
        { return Cylinder(center, r, r, height, seg, Quaternion.identity, col, emit); }

        void Fan(float y, float r, int seg, Vector3 nn, Color col, Rect uvr, bool flip)
        {
            int ci = v.Count;
            Vtx(new Vector3(0, y, 0), nn, new Vector2(uvr.center.x, uvr.center.y), col);
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg; float s = Mathf.Sin(a), co = Mathf.Cos(a);
                Vtx(new Vector3(s * r, y, co * r), nn, new Vector2(uvr.xMin + (s * 0.5f + 0.5f) * uvr.width, uvr.yMin + (co * 0.5f + 0.5f) * uvr.height), col);
            }
            for (int i = 0; i < seg; i++)
            {
                if (!flip) { t.Add(ci); t.Add(ci + 1 + i); t.Add(ci + 2 + i); }
                else { t.Add(ci); t.Add(ci + 2 + i); t.Add(ci + 1 + i); }
            }
        }

        /// <summary>Flat disc facing +Y with planar UV (u from x, v from z) — roulette wheels, tables, chip faces.</summary>
        public MeshKit Disc(Vector3 center, float r, int seg, Quaternion rot, Color col, float emit = 0f, Rect? uvRect = null)
        {
            Push(center, rot);
            Fan(0, r, seg, Vector3.up, Col(col, emit), uvRect ?? Unit, false);
            Pop();
            return this;
        }

        /// <summary>Flat annulus facing +Y split in radial bands, with planar UVs in metres / tile (seamless carpets).</summary>
        public MeshKit RingPlanar(Vector3 center, float rIn, float rOut, int seg, int bands, float tile, Color col, float emit = 0f)
        {
            Push(center, Quaternion.identity);
            var cl = Col(col, emit);
            for (int b = 0; b < bands; b++)
            {
                float r0 = Mathf.Lerp(rIn, rOut, b / (float)bands), r1 = Mathf.Lerp(rIn, rOut, (b + 1) / (float)bands);
                int start = v.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg; float s = Mathf.Sin(a), co = Mathf.Cos(a);
                    var p0 = new Vector3(s * r0, 0, co * r0); var p1 = new Vector3(s * r1, 0, co * r1);
                    Vtx(p0, Vector3.up, new Vector2(p0.x / tile, p0.z / tile), cl);
                    Vtx(p1, Vector3.up, new Vector2(p1.x / tile, p1.z / tile), cl);
                }
                for (int i = 0; i < seg; i++)
                {
                    int a = start + i * 2;
                    t.Add(a); t.Add(a + 1); t.Add(a + 3);
                    t.Add(a); t.Add(a + 3); t.Add(a + 2);
                }
            }
            Pop();
            return this;
        }

        /// <summary>Flat ring (annulus) facing +Y.</summary>
        public MeshKit Ring(Vector3 center, float rIn, float rOut, int seg, Quaternion rot, Color col, float emit = 0f)
        {
            Push(center, rot);
            var cl = Col(col, emit); int start = v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg; float s = Mathf.Sin(a), co = Mathf.Cos(a);
                Vtx(new Vector3(s * rIn, 0, co * rIn), Vector3.up, new Vector2(i / (float)seg, 0), cl);
                Vtx(new Vector3(s * rOut, 0, co * rOut), Vector3.up, new Vector2(i / (float)seg, 1), cl);
            }
            for (int i = 0; i < seg; i++)
            {
                int a = start + i * 2;
                t.Add(a); t.Add(a + 1); t.Add(a + 3);
                t.Add(a); t.Add(a + 3); t.Add(a + 2);
            }
            Pop();
            return this;
        }

        public MeshKit Sphere(Vector3 center, Vector3 radii, int seg, Color col, float emit = 0f, Quaternion? rot = null)
        {
            Push(center, rot ?? Quaternion.identity);
            var cl = Col(col, emit); int rings = Mathf.Max(3, seg / 2); int start = v.Count;
            for (int y = 0; y <= rings; y++)
            {
                float lat = Mathf.PI * y / rings; float sy = Mathf.Cos(lat), sr = Mathf.Sin(lat);
                for (int x = 0; x <= seg; x++)
                {
                    float lon = 2 * Mathf.PI * x / seg;
                    var dir = new Vector3(Mathf.Sin(lon) * sr, sy, Mathf.Cos(lon) * sr);
                    Vtx(Vector3.Scale(dir, radii), new Vector3(dir.x / Mathf.Max(radii.x, 1e-4f), dir.y / Mathf.Max(radii.y, 1e-4f), dir.z / Mathf.Max(radii.z, 1e-4f)), new Vector2(x / (float)seg, 1 - y / (float)rings), cl);
                }
            }
            for (int y = 0; y < rings; y++)
                for (int x = 0; x < seg; x++)
                {
                    int a = start + y * (seg + 1) + x, b = a + seg + 1;
                    t.Add(a); t.Add(b); t.Add(a + 1);
                    t.Add(a + 1); t.Add(b); t.Add(b + 1);
                }
            Pop();
            return this;
        }
        public MeshKit Sphere(Vector3 center, float r, int seg, Color col, float emit = 0f) { return Sphere(center, Vector3.one * r, seg, col, emit); }

        /// <summary>Torus around local Y. arc in degrees (start at +Z, going towards +X).</summary>
        public MeshKit Torus(Vector3 center, float R, float r, int segU, int segV, Quaternion rot, Color col, float emit = 0f, float arc = 360f)
        {
            Push(center, rot);
            var cl = Col(col, emit); int start = v.Count;
            for (int i = 0; i <= segU; i++)
            {
                float a = arc * Mathf.Deg2Rad * i / segU;
                var cdir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                for (int j = 0; j <= segV; j++)
                {
                    float b = 2 * Mathf.PI * j / segV;
                    var nn = cdir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    Vtx(cdir * R + nn * r, nn, new Vector2(i / (float)segU, j / (float)segV), cl);
                }
            }
            for (int i = 0; i < segU; i++)
                for (int j = 0; j < segV; j++)
                {
                    int a = start + i * (segV + 1) + j, b = a + segV + 1;
                    t.Add(a); t.Add(b); t.Add(a + 1);
                    t.Add(a + 1); t.Add(b); t.Add(b + 1);
                }
            Pop();
            return this;
        }

        /// <summary>Round tube between two points (neon strips, arms, legs, bars).</summary>
        public MeshKit Tube(Vector3 a, Vector3 b, float r, int seg, Color col, float emit = 0f, bool caps = true)
        {
            var d = b - a; float len = d.magnitude; if (len < 1e-5f) return this;
            var rot = Quaternion.FromToRotation(Vector3.up, d / len);
            return Cylinder((a + b) * 0.5f, r, r, len, seg, rot, col, emit, caps);
        }

        /// <summary>Square bar between two points.</summary>
        public MeshKit Bar(Vector3 a, Vector3 b, float thick, Color col, float emit = 0f)
        {
            var d = b - a; float len = d.magnitude; if (len < 1e-5f) return this;
            return Box((a + b) * 0.5f, new Vector3(thick, thick, len), Quaternion.LookRotation(d / len), col, emit);
        }

        public MeshKit Capsule(Vector3 a, Vector3 b, float r, int seg, Color col, float emit = 0f)
        {
            Tube(a, b, r, seg, col, emit, false);
            Sphere(a, r, seg, col, emit);
            Sphere(b, r, seg, col, emit);
            return this;
        }

        /// <summary>Extruded convex/concave-free polygon (XZ outline) with height along Y — floor plates, counters.</summary>
        public MeshKit Prism(IList<Vector2> outline, float y0, float y1, Color col, float emit = 0f, Color? topCol = null, float topEmit = -1f, float uvScale = 0f)
        {
            var cl = Col(col, emit); var ct = Col(topCol ?? col, topEmit < 0 ? emit : topEmit);
            if (SignedArea(outline) < 0) { var rev = new List<Vector2>(outline); rev.Reverse(); outline = rev; }
            int count = outline.Count;
            // sides
            for (int i = 0; i < count; i++)
            {
                var p = outline[i]; var q = outline[(i + 1) % count];
                var nn = new Vector3(q.y - p.y, 0, -(q.x - p.x)).normalized;
                QuadRaw(new Vector3(p.x, y0, p.y), new Vector3(q.x, y0, q.y), new Vector3(q.x, y1, q.y), new Vector3(p.x, y1, p.y), nn, cl, Unit);
            }
            // top (fan from centroid; outline must be convex or star-shaped)
            var cen = Vector2.zero; foreach (var p in outline) cen += p; cen /= count;
            int ci = v.Count;
            System.Func<Vector2, Vector2> U = p => uvScale > 0 ? p * uvScale : new Vector2(0.5f, 0.5f);
            Vtx(new Vector3(cen.x, y1, cen.y), Vector3.up, U(cen), ct);
            for (int i = 0; i <= count; i++) { var p = outline[i % count]; Vtx(new Vector3(p.x, y1, p.y), Vector3.up, U(p), ct); }
            for (int i = 0; i < count; i++) { t.Add(ci); t.Add(ci + 2 + i); t.Add(ci + 1 + i); }
            return this;
        }
        static float SignedArea(IList<Vector2> p) { float a = 0; for (int i = 0; i < p.Count; i++) { var q = p[(i + 1) % p.Count]; a += p[i].x * q.y - q.x * p[i].y; } return a * 0.5f; }

        /// <summary>Flat polygon facing +Y; UVs map the XZ rectangle `uvWorld` to 0..1 (felts printed with a texture).</summary>
        public MeshKit PolygonTop(IList<Vector2> outline, float y, Color col, float emit, Rect uvWorld)
        {
            if (SignedArea(outline) < 0) { var rev = new List<Vector2>(outline); rev.Reverse(); outline = rev; }
            var cl = Col(col, emit); int count = outline.Count;
            var cen = Vector2.zero; foreach (var p in outline) cen += p; cen /= count;
            System.Func<Vector2, Vector2> U = p => new Vector2((p.x - uvWorld.xMin) / uvWorld.width, (p.y - uvWorld.yMin) / uvWorld.height);
            int ci = v.Count;
            Vtx(new Vector3(cen.x, y, cen.y), Vector3.up, U(cen), cl);
            for (int i = 0; i <= count; i++) { var p = outline[i % count]; Vtx(new Vector3(p.x, y, p.y), Vector3.up, U(p), cl); }
            for (int i = 0; i < count; i++) { t.Add(ci); t.Add(ci + 2 + i); t.Add(ci + 1 + i); }
            return this;
        }

        public static List<Vector2> RegularPolygon(int sides, float radius, float startDeg = 0f)
        {
            var l = new List<Vector2>();
            for (int i = 0; i < sides; i++) { float a = (startDeg + i * 360f / sides) * Mathf.Deg2Rad; l.Add(new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * radius); }
            return l;
        }

        /// <summary>Appends another kit's geometry.</summary>
        public MeshKit Append(Mesh m, Matrix4x4 tr, Color col, float emit = 0f)
        {
            var mv = m.vertices; var mn = m.normals; var mu = m.uv; var mt = m.triangles; int start = v.Count; var cl = Col(col, emit);
            for (int i = 0; i < mv.Length; i++)
            {
                v.Add((M * tr).MultiplyPoint3x4(mv[i]));
                n.Add((M * tr).MultiplyVector(mn.Length > i ? mn[i] : Vector3.up).normalized);
                uv.Add(mu.Length > i ? mu[i] : Vector2.zero);
                c.Add(cl);
                boneOf.Add(bone);
            }
            foreach (var i in mt) t.Add(start + i);
            return this;
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            if (t.Count > 0) m.RecalculateTangents();   // normal-mapped materials
            return m;
        }

        public void Clear() { v.Clear(); n.Clear(); uv.Clear(); c.Clear(); t.Clear(); boneOf.Clear(); stack.Clear(); M = Matrix4x4.identity; bone = 0; }

        /// <summary>Rigidly skinned mesh: vertices were authored in root space with `bone` set per part.
        /// One draw call per avatar, animated by moving the bone transforms.</summary>
        public SkinnedMeshRenderer SpawnSkinned(string name, Transform root, Transform[] bones, Material mat)
        {
            var mesh = ToMesh(name);
            var bw = new BoneWeight[v.Count];
            for (int i = 0; i < bw.Length; i++) { bw[i].boneIndex0 = Mathf.Clamp(boneOf[i], 0, bones.Length - 1); bw[i].weight0 = 1f; }
            mesh.boneWeights = bw;
            var bind = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) bind[i] = bones[i].worldToLocalMatrix * root.localToWorldMatrix;
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[0];
            smr.sharedMaterial = mat != null ? mat : Palette.Neon;
            smr.updateWhenOffscreen = false;
            smr.localBounds = new Bounds(Vector3.up * 0.2f, new Vector3(2.5f, 3.5f, 2.5f));
            smr.shadowCastingMode = ShadowCastingMode.Off; smr.receiveShadows = false;
            smr.quality = SkinQuality.Bone1;
            return smr;
        }

        /// <summary>Creates a GameObject with this mesh. In the editor the mesh is saved as an asset.</summary>
        public GameObject Spawn(string name, Transform parent, Vector3 localPos, Quaternion localRot, Material mat = null, bool persist = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var mesh = ToMesh(name);
            if (persist) mesh = Palette.PersistMesh(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat != null ? mat : Palette.Neon;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            return go;
        }
        public GameObject Spawn(string name, Transform parent, Material mat = null, bool persist = true)
        { return Spawn(name, parent, Vector3.zero, Quaternion.identity, mat, persist); }
    }
}
