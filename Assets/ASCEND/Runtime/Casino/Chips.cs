using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Chip denominations, meshes, value → chips decomposition and the tidy stack layout every pile uses.</summary>
    public static class ChipKit
    {
        public static readonly long[] Values = { 100, 500, 1000, 5000, 25000 };
        public const float Radius = 0.021f, Thick = 0.0048f;
        /// <summary>Chips per column before a stack is split into a new column next to it (a real dealer never builds towers).</summary>
        public const int PerColumn = 10;
        /// <summary>Centre distance between two neighbouring columns (they just touch).</summary>
        public const float Pitch = Radius * 2f + 0.0015f;
        static readonly Mesh[] meshes = new Mesh[5];
        // hex-packed column centres (units of Pitch): every prefix is a tight cluster (pair, triangle, rhombus... hexagon)
        static readonly Vector2[] Hex = {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.866f), new Vector2(-0.5f, 0.866f),
            new Vector2(-1f, 0f), new Vector2(-0.5f, -0.866f), new Vector2(0.5f, -0.866f) };

        public static int Index(long v) { for (int i = Values.Length - 1; i >= 0; i--) if (v >= Values[i]) return i; return 0; }

        public static Mesh MeshFor(int i)
        {
            if (meshes[i] != null) return meshes[i];
            var k = new MeshKit();
            var tile = new Rect(i * 0.2f, 0f, 0.2f, 1f);
            var side = new Rect(i * 0.2f + 0.004f, 0.42f, 0.003f, 0.16f);
            k.Cylinder(Vector3.zero, Radius, Radius, Thick, 22, Quaternion.identity, Color.white, 0.05f, true, Color.white, 0.12f, tile, side);
            meshes[i] = k.ToMesh("chip_" + Values[i]);
            return meshes[i];
        }

        /// <summary>Greedy split of an amount into chip values (max count, biggest first).</summary>
        public static List<long> Split(long amount, int max = 24)
        {
            var l = new List<long>();
            for (int i = Values.Length - 1; i >= 0 && amount > 0; i--)
                while (amount >= Values[i] && l.Count < max) { l.Add(Values[i]); amount -= Values[i]; }
            if (amount > 0 && l.Count < max + 1) l.Add(amount);   // odd remainder chip (e.g. 1.95x payouts)
            return l;
        }

        /// <summary>Number of columns a pile of n chips is split into.</summary>
        public static int Columns(int n) { return Mathf.Clamp((n + PerColumn - 1) / PerColumn, 1, Hex.Length); }

        /// <summary>Centre of column col (of cols) relative to the pile centre, in the pile's local XZ plane.</summary>
        public static Vector3 ColumnOffset(int col, int cols)
        {
            cols = Mathf.Clamp(cols, 1, Hex.Length); col = Mathf.Clamp(col, 0, cols - 1);
            Vector2 c = Vector2.zero;
            for (int i = 0; i < cols; i++) c += Hex[i];
            c /= cols;
            var h = Hex[col] - c;
            return new Vector3(h.x * Pitch, 0f, h.y * Pitch);
        }

        /// <summary>Local position (pile frame: y along the felt normal, 0 = the felt) of the centre of chip i in a tidy pile
        /// of n chips sorted biggest first: balanced columns of at most PerColumn, chips touching, the first chip of
        /// every column lying on the felt.</summary>
        public static Vector3 StackLocal(int i, int n)
        {
            n = Mathf.Max(1, n);
            int cols = Columns(n);
            int per = (n + cols - 1) / cols;
            int col = Mathf.Min(i / per, cols - 1), lvl = i - col * per;
            return ColumnOffset(col, cols) + Vector3.up * (Thick * (lvl + 0.5f));
        }

        /// <summary>Radius of the footprint of a tidy pile of n chips.</summary>
        public static float PileRadius(int n)
        {
            int cols = Columns(n); float r = 0f;
            for (int i = 0; i < cols; i++) r = Mathf.Max(r, ColumnOffset(i, cols).magnitude);
            return r + Radius;
        }

        public static GameObject Visual(long value, Transform parent, Vector3 pos)
        {
            var go = new GameObject("Chip_" + value);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.AddComponent<MeshFilter>().sharedMesh = MeshFor(Index(value));
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Palette.Textured("chips");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go;
        }
    }
}
