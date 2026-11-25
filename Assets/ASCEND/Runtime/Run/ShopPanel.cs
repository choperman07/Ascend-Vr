using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ascend
{
    // ====================================================================== El Rata's black market
    /// <summary>Four random offers a day (re-roll for a few chips), bought with run chips. In the casino the same
    /// panel also has the button to call the driver and end the day early.</summary>
    public class ShopPanel : MonoBehaviour
    {
        RectTransform panel, body;
        bool casino;
        readonly List<Run.Mod> offers = new List<Run.Mod>();
        int offersDay = -1;
        static int rerolls;
        public float showDist = 3.2f;
        public static readonly List<ShopPanel> All = new List<ShopPanel>();

        public static ShopPanel Create(Transform parent, Vector3 worldPos, bool inCasino)
        {
            var go = new GameObject(inCasino ? "Chofer & Rata" : "Rata Shop");
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            var s = go.AddComponent<ShopPanel>();
            s.casino = inCasino;
            s.panel = UIKit.Panel("ShopPanel", go.transform, Vector3.zero, Quaternion.identity, new Vector2(1300, 980), 1.25f, Palette.Magenta);
            s.body = UIKit.Rect("Body", s.panel, 0, 0, 1300, 980);
            go.AddComponent<FacePlayer>();
            s.Build();
            return s;
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Roll()
        {
            offers.Clear(); offersDay = Run.Day;
            var pool = new List<Run.Mod>();
            foreach (var m in Run.Catalog) if (!(Run.FinalDay && (m.id == "prestamo" || m.id == "abogado"))) pool.Add(m);
            for (int i = 0; i < 4 && pool.Count > 0; i++) { int j = UnityEngine.Random.Range(0, pool.Count); offers.Add(pool[j]); pool.RemoveAt(j); }
        }

        long RerollCost { get { return Math.Max(150, Run.PriceBase / 20) * (1 + rerolls); } }

        public void Build()
        {
            if (offersDay != Run.Day) { Roll(); rerolls = 0; }
            UIKit.Clear(body);
            var panel = body;
            UIKit.Label(panel, casino ? "EL RATA · MERCADO NEGRO" : "EL RATA · MERCADO NEGRO", 40, 22, 900, 64, 50, Palette.Gold, Palette.UI, TextAlignmentOptions.MidlineLeft);
            UIKit.Label(panel, "\"Cosas para pasarlo bien. Sin preguntas.\"", 42, 84, 900, 40, 26, Palette.Muted, Palette.Body, TextAlignmentOptions.MidlineLeft);
            UIKit.Label(panel, Run.N(GameState.I.chips) + " FICHAS", 860, 24, 400, 60, 40, Palette.Money, Palette.UI, TextAlignmentOptions.MidlineRight);
            for (int i = 0; i < offers.Count; i++)
            {
                var m = offers[i]; float y = 140 + i * 170;
                UIKit.Box(panel, 36, y, 1228, 156, new Color(m.color.r * 0.18f, m.color.g * 0.18f, m.color.b * 0.18f, 0.9f));
                UIKit.Label(panel, m.name, 60, y + 10, 820, 52, 40, m.color, Palette.UI, TextAlignmentOptions.MidlineLeft);
                string tag = m.kind == Run.Kind.Day ? "HOY" : m.kind == Run.Kind.Run ? "TODA LA PARTIDA" : m.kind == Run.Kind.Charge ? "USOS" : "AL INSTANTE";
                UIKit.Label(panel, tag, 60, y + 112, 400, 34, 22, Palette.Muted, Palette.Mono, TextAlignmentOptions.MidlineLeft);
                UIKit.Label(panel, m.desc, 60, y + 58, 860, 60, 26, Palette.Text, Palette.Body, TextAlignmentOptions.TopLeft);
                long price = Run.PriceOf(m);
                bool maxed = Run.Maxed(m);
                bool usable = !(m.id == "bebida" && !(RunDirector.I != null && RunDirector.I.InCasino));
                string cap = maxed ? "YA LO TIENES" : !usable ? "SOLO EN EL CASINO" : price > 0 ? Run.N(price) : m.id == "todo" ? "¡A X2 O NADA!" : "GRATIS*";
                var b = UIKit.Btn(panel, cap, 940, y + 30, 300, 96, maxed || !usable ? Palette.Muted : m.color, () => Buy(m), price > 0 ? 36 : 30);
                b.interactable = !maxed && usable;
            }
            float by = 140 + 4 * 170 + 4;
            UIKit.Btn(panel, "OTRAS OFERTAS · " + Run.N(RerollCost), 36, by, 600, 80, Palette.Violet, Reroll, 30);
            if (casino) UIKit.Btn(panel, "LLAMAR AL CHÓFER (ACABAR EL DÍA)", 656, by, 608, 80, Palette.Red, () => { if (RunDirector.I != null) RunDirector.I.EndDayEarly(); }, 28);
            else
            {
                var owned = string.Join("  ·  ", new List<string>(Run.OwnedLabels()).ToArray());
                UIKit.Label(panel, owned.Length > 0 ? "LLEVAS: " + owned : "* El préstamo se paga mañana con intereses.", 656, by, 608, 80, 20, Palette.Muted, Palette.Mono, TextAlignmentOptions.MidlineLeft);
            }
        }

        void Reroll()
        {
            long c = RerollCost;
            if (GameState.I.chips < c) { GameState.I.Notify("NO TE LLEGA · " + Run.N(c) + " FICHAS", Palette.Red); Sfx.UI("ui_error"); return; }
            GameState.I.TakeChips(c); rerolls++; GameState.I.Notify("EL RATA SACA OTRA CAJA", Palette.Violet);
            Roll(); offersDay = Run.Day; RefreshAll();
        }

        void Buy(Run.Mod m)
        {
            long price = Run.PriceOf(m);
            if (Run.Maxed(m)) return;
            if (price > GameState.I.chips) { GameState.I.Notify("NO TE LLEGA · " + Run.N(price) + " FICHAS", Palette.Red); Sfx.UI("ui_error"); return; }
            if (price > 0) GameState.I.TakeChips(price);
            Run.Grant(m);
            Sfx.UI("cashout", 0.8f);
            Juice.Sparks(transform.position, m.color, 30);
            if (RunDirector.I != null) RunDirector.I.OnBought(m, this);
            GameState.I.Notify("COMPRADO · " + m.name, m.color);
            RefreshAll();
        }

        public static void RefreshAll() { foreach (var s in All.ToArray()) if (s != null) s.Build(); }

        void Update()
        {
            if (!Rig.Ready) return;
            bool near = (Rig.I.Head.position - transform.position).sqrMagnitude < showDist * showDist;
            if (panel.gameObject.activeSelf != near) { panel.gameObject.SetActive(near); if (near) Build(); }
        }
    }
}
