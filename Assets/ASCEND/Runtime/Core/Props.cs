using UnityEngine;

namespace Ascend
{
    /// <summary>Places props from the Casino FREE pack (ithappy) exactly as the pack ships them: its own URP materials
    /// (palette atlas, fabric, glass, emissive screens) are kept untouched. Only when a tint or glow is requested is
    /// the material rebuilt on ASCEND/Neon with the pack texture. Returns null when the prop is missing, so callers can
    /// fall back to procedural geometry in the same style.</summary>
    public static class Props
    {
        public static bool Has(string prop) { var lib = AssetLibrary.I; return lib != null && lib.Prop(prop) != null; }

        public static GameObject Place(string prop, Transform parent, Vector3 localPos, float yaw, float scale = 1f, bool collider = false, Color? tint = null, float emit = 0f)
        {
            var lib = AssetLibrary.I;
            var pf = lib != null ? lib.Prop(prop) : null;
            if (pf == null) return null;
            var go = Object.Instantiate(pf, parent, false);
            go.name = prop;
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = pf.transform.localScale * scale;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    // the pack's own look: keep its URP materials as they are
                    if (tint == null && emit <= 0f && src != null && src.shader != null && src.shader.name.StartsWith("Universal Render Pipeline")) continue;
                    Texture tex = null;
                    if (src != null)
                    {
                        if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
                        if (tex == null && src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");
                    }
                    if (tex == null && lib != null) tex = lib.PropTexture(prop);
                    string key = "prop_" + (tex != null ? tex.name : (src != null ? src.name : "none")) + (tint.HasValue ? "_" + ColorUtility.ToHtmlStringRGB(tint.Value) : "") + (emit > 0f ? "_e" : "");
                    var baseCol = src != null && tex == null && src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white;
                    mats[i] = Palette.Character(key, tex, tint ?? baseCol, emit, 0.45f, 0.12f);
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            if (collider && rends.Length > 0)
            {
                // follow the furniture's real shape: one convex hull per mesh instead of one big box
                bool any = false;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || mf.sharedMesh.vertexCount < 4) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh; mc.convex = true;
                    mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;
                    any = true;
                }
                if (any) return go;
                var b = new Bounds(go.transform.InverseTransformPoint(rends[0].bounds.center), Vector3.zero);
                foreach (var r in rends)
                {
                    var wb = r.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = wb.center + Vector3.Scale(wb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                        b.Encapsulate(go.transform.InverseTransformPoint(corner));
                    }
                }
                var bc = go.AddComponent<BoxCollider>();
                bc.center = b.center; bc.size = b.size;
            }
            return go;
        }
    }
}
