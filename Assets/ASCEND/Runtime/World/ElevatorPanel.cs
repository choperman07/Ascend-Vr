using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Elevator panel next to the core: one poke button per floor (locked floors show their tier).</summary>
    public class ElevatorPanel : MonoBehaviour
    {
        public int floor;
        void Start()
        {
            var k = new MeshKit();
            k.Box(new Vector3(0, 1.28f, 0.02f), new Vector3(0.36f, 1.0f, 0.04f), Palette.Hex("0E0B16"), 0.02f);
            k.Bar(new Vector3(-0.18f, 0.78f, 0), new Vector3(-0.18f, 1.78f, 0), 0.01f, Palette.Gold, 1f);
            k.Bar(new Vector3(0.18f, 0.78f, 0), new Vector3(0.18f, 1.78f, 0), 0.01f, Palette.Gold, 1f);
            k.Spawn("Panel", transform, Palette.Glossy, false);
            Txt.Make("Here", transform, new Vector3(0, 1.82f, 0), "FLOOR " + (floor + 1) + " · " + Tower.Floors[floor].name, 0.03f, Palette.Gold, Palette.Display);
            float step = Mathf.Min(0.12f, 0.88f / Tower.Count);
            for (int i = 0; i < Tower.Count; i++)
            {
                int f = Tower.Count - 1 - i;
                var fi = Tower.Floors[f];
                var b = PokeButton.Create("F" + f, transform, new Vector3(-0.08f, 1.68f - i * step, -0.01f), Quaternion.Euler(-90, 0, 0), 0.03f, f == floor ? Palette.Gold : fi.color, (f + 1).ToString(), () => Go(f), 0.03f);
                Txt.Make("L" + f, transform, new Vector3(0.07f, 1.68f - i * step, -0.01f), fi.name.Split('·')[0].Trim(), 0.018f, Palette.Muted, Palette.Display, TextAlignmentOptions.Left, 0.2f);
            }
        }
        void Go(int f)
        {
            if (FloorManager.I == null) return;
            if (f == FloorManager.I.Current) { Sfx.UI("ui_error", 0.5f); return; }
            FloorManager.I.GoTo(f);
        }
    }
}
