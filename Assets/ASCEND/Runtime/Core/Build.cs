using UnityEngine;

namespace Ascend
{
    /// <summary>GameObject helpers on top of MeshKit: empties, glow sprites, light beams, colliders.</summary>
    public static class Build
    {
        public static GameObject Empty(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go;
        }
        public static GameObject Empty(string name, Transform parent, Vector3 localPos, Quaternion localRot)
        {
            var go = Empty(name, parent, localPos);
            go.transform.localRotation = localRot;
            return go;
        }

        static Mesh _quad, _cone;
        static Mesh QuadMesh
        {
            get
            {
                if (_quad == null) { var k = new MeshKit(); k.Quad(Vector3.zero, Vector2.one, Quaternion.identity, Color.white, 1f); _quad = k.ToMesh("glow_quad"); _quad = Palette.PersistMesh(_quad, "Shared"); }
                return _quad;
            }
        }
        static Mesh ConeMesh
        {
            get
            {
                if (_cone == null)
                {
                    var k = new MeshKit();
                    // open cone pointing down -Y from apex at 0, length 1, radius 1 at the bottom; UV v = 1 at apex
                    k.Cylinder(new Vector3(0, -0.5f, 0), 1f, 0.02f, 1f, 20, Quaternion.identity, Color.white, 1f, false);
                    _cone = k.ToMesh("beam_cone"); _cone = Palette.PersistMesh(_cone, "Shared");
                }
                return _cone;
            }
        }

        /// <summary>Additive soft glow that always faces the camera (fake bloom for lamps, neon, wins).</summary>
        public static Transform GlowSprite(Transform parent, Vector3 localPos, float size, Color c, float intensity = 1f, bool billboard = true)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Palette.Glow;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            var mpb = new MaterialPropertyBlock(); c.a = 1f; mpb.SetColor("_BaseColor", c * intensity); r.SetPropertyBlock(mpb);
            if (billboard) go.AddComponent<Billboard>().full = true;
            return go.transform;
        }

        /// <summary>Volumetric-looking light cone (stage lights, spotlights over tables).</summary>
        public static Transform Beam(Transform parent, Vector3 localPos, Quaternion localRot, float length, float radius, Color c, float intensity = 0.25f)
        {
            var go = new GameObject("Beam");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = new Vector3(radius, length, radius);
            go.AddComponent<MeshFilter>().sharedMesh = ConeMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Palette.Beam;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            var mpb = new MaterialPropertyBlock(); c.a = 1f; mpb.SetColor("_BaseColor", c * intensity); r.SetPropertyBlock(mpb);
            return go.transform;
        }

        public static BoxCollider BoxCol(GameObject go, Vector3 center, Vector3 size, bool trigger = false)
        {
            var b = go.AddComponent<BoxCollider>(); b.center = center; b.size = size; b.isTrigger = trigger; return b;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }

        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying) Object.Destroy(t.GetChild(i).gameObject);
                else Object.DestroyImmediate(t.GetChild(i).gameObject);
            }
        }

        /// <summary>NFT card as a 3D object (front and back), used by galleries, crates and profile cards.</summary>
        public static GameObject NftCard(string name, Transform parent, Vector3 localPos, float width, Rarity r)
        {
            float h = width * 1372f / 972f;
            var k = new MeshKit();
            k.Quad(Vector3.zero, new Vector2(width, h), Quaternion.identity, Color.white, 1f);
            k.Quad(Vector3.zero, new Vector2(width, h), Quaternion.Euler(0, 180, 0), Color.white, 1f);
            var go = k.Spawn(name, parent, localPos, Quaternion.identity, Palette.CardMat(r));
            return go;
        }
    }
}
