using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>Physical playing card (face from the card atlas, ASCEND back), dealt with an arc and a flip.</summary>
    public class Card3D : MonoBehaviour
    {
        public const float W = 0.085f, H = 0.119f;
        public int card;
        public bool faceUp;
        static readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();

        static Rect Face(int c)
        {
            int r = c % 13, s = c / 13;
            return new Rect(r * 140f / 2048f, 1f - (s + 1) * 196f / 1024f, 140f / 2048f, 196f / 1024f);
        }
        static readonly Rect Back = new Rect(0f, 1f - 5f * 196f / 1024f, 140f / 2048f, 196f / 1024f);

        static Mesh MeshFor(int c)
        {
            Mesh m;
            if (meshes.TryGetValue(c, out m) && m != null) return m;
            var k = new MeshKit();
            // lying flat: face on +Y, back on -Y
            k.Plane(new Vector3(0, 0.0006f, 0), new Vector2(W, H), Quaternion.identity, Color.white, 0.1f, Face(c));
            k.Plane(new Vector3(0, -0.0006f, 0), new Vector2(W, H), Quaternion.Euler(0, 0, 180), Color.white, 0.1f, Back);
            k.Box(Vector3.zero, new Vector3(W * 0.995f, 0.0008f, H * 0.995f), Palette.Text, 0.1f);
            m = k.ToMesh("card_" + c);
            meshes[c] = m;
            return m;
        }

        public static Card3D Spawn(int card, Vector3 pos, Quaternion rot, bool faceUp)
        {
            var go = new GameObject("Card_" + Shoe.RankName(card));
            go.transform.position = pos;
            go.AddComponent<MeshFilter>().sharedMesh = MeshFor(card);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Palette.Textured("cards");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            var c = go.AddComponent<Card3D>(); c.card = card; c.faceUp = faceUp;
            go.transform.rotation = rot * (faceUp ? Quaternion.identity : Quaternion.Euler(0, 0, 180));
            return c;
        }

        /// <summary>Deals from the shoe to a spot on the felt (world), with a swish and optional flip.</summary>
        public IEnumerator DealTo(Vector3 target, Quaternion flatRot, bool faceUp, float dur = 0.38f)
        {
            Sfx.Play("card_deal", transform.position, 0.7f, Random.Range(0.95f, 1.08f));
            var end = flatRot * (faceUp ? Quaternion.identity : Quaternion.Euler(0, 0, 180));
            yield return StartCoroutine(Fx.Arc(transform, target, 0.06f, dur, end));
            this.faceUp = faceUp;
        }

        public IEnumerator Flip(float dur = 0.3f)
        {
            Sfx.Play("card_flip", transform.position, 0.7f);
            var p0 = transform.position; var r0 = transform.rotation; var r1 = r0 * Quaternion.Euler(0, 0, 180);
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float k = Fx.EaseInOut(t / dur);
                transform.position = p0 + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.05f;
                transform.rotation = Quaternion.Slerp(r0, r1, k);
                yield return null;
            }
            transform.position = p0; transform.rotation = r1;
            faceUp = !faceUp;
        }
    }
}
