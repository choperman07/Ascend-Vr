using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Ascend
{
    /// <summary>Area of the felt where chips are bet. Chips dropped here are coloured up to the fewest chips and stacked
    /// in neat columns (biggest at the bottom, max PerColumn high) lying flat on the felt, along the spot's own up.</summary>
    public class BetSpot : MonoBehaviour
    {
        public string id = "BET";
        [Tooltip("Total returned per 1 chip on a win (2 = even money, 36 = straight up).")]
        public float pays = 2f;
        public TableGame game;
        public readonly List<Chip> chips = new List<Chip>();
        public long Total { get { long t = 0; foreach (var c in chips) if (c != null) t += c.value; return t; } }
        public bool locked;
        public Vector2 size = new Vector2(0.2f, 0.2f);
        /// <summary>Most chips a spot shows after colouring up (beyond that the last chip carries the remainder).</summary>
        public const int MaxChips = 40;
        Transform glow;
        readonly List<long> adds = new List<long>();               // amounts in the order they were bet (UNDO)
        readonly List<GameObject> transients = new List<GameObject>();   // chips flying away (sweep / payout)
        float tidyAt = -1f; bool tidyQueued;
        float surface = float.NaN;                                   // felt height along up from the spot origin

        public static BetSpot Create(TableGame game, string id, float pays, Transform parent, Vector3 localPos, Vector2 size, bool marker = false)
        {
            var go = new GameObject("Spot_" + id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var col = go.AddComponent<BoxCollider>(); col.isTrigger = true; col.size = new Vector3(size.x, 0.035f, size.y); col.center = new Vector3(0, 0.0125f, 0);   // thin: a ray from a seated angle hits the cell you point at
            var s = go.AddComponent<BetSpot>(); s.id = id; s.pays = pays; s.game = game; s.size = size;
            if (marker)
            {
                var k = new MeshKit(); k.Ring(Vector3.up * 0.001f, Mathf.Min(size.x, size.y) * 0.42f, Mathf.Min(size.x, size.y) * 0.46f, 32, Quaternion.identity, game != null ? game.Accent : Palette.Gold, 1f);
                k.Spawn("Ring", go.transform, Palette.Neon, false);
            }
            return s;
        }

        // ------------------------------------------------------------ stack layout
        /// <summary>Height of the felt above the spot origin along its up (probed once on the real table meshes).</summary>
        float Surface
        {
            get
            {
                if (float.IsNaN(surface)) surface = game != null ? game.SurfaceOffset(transform.position, transform.up) : 0f;
                return surface + 0.0004f;
            }
        }

        /// <summary>World position of chip i of n in this spot's tidy pile (centre of the chip).</summary>
        Vector3 Slot(int i, int n) { return transform.TransformPoint(ChipKit.StackLocal(i, n) + Vector3.up * Surface); }

        Quaternion Flat(Chip c) { return transform.rotation * Quaternion.Euler(0, c.yaw, 0); }

        static void SortBigFirst(List<Chip> l)
        {
            // stable insertion sort: equal chips keep their places, so nothing shuffles needlessly
            for (int i = 1; i < l.Count; i++)
            {
                var c = l[i]; int j = i - 1;
                while (j >= 0 && l[j].value < c.value) { l[j + 1] = l[j]; j--; }
                l[j + 1] = c;
            }
        }

        /// <summary>Moves every chip to its slot. sort = biggest denominations at the bottom (the provisional layout while
        /// chips are still flying in keeps the order so new chips land on top). Returns the longest flight.</summary>
        float Arrange(bool instant, bool sort)
        {
            chips.RemoveAll(c => c == null);
            if (sort) SortBigFirst(chips);
            float longest = 0f; int n = chips.Count;
            for (int i = 0; i < n; i++)
            {
                var c = chips[i];
                if (instant) c.PlaceNow(transform, Slot(i, n), Flat(c));
                else longest = Mathf.Max(longest, c.Place(transform, Slot(i, n), Flat(c)));
            }
            return longest;
        }

        /// <summary>Colours the pile up to the fewest chips worth newTotal (high denominations first), reusing the chips that
        /// already have a wanted value. The difference to the current total must be settled by the caller.</summary>
        void Rebuild(long newTotal)
        {
            chips.RemoveAll(c => c == null);
            var want = newTotal > 0 ? ChipKit.Split(newTotal, MaxChips) : new List<long>();
            var spare = new List<Chip>(chips);
            var keep = new List<Chip>(); var need = new List<long>();
            foreach (var v in want)
            {
                int k = -1;
                for (int i = 0; i < spare.Count; i++) if (spare[i].value == v) { k = i; break; }
                if (k >= 0) { keep.Add(spare[k]); spare.RemoveAt(k); } else need.Add(v);
            }
            if (spare.Count == 0 && need.Count == 0) return;
            bool frozen = locked;
            foreach (var c in chips) if (!c.Grabbable) frozen = true;
            Vector3 at = spare.Count > 0 ? spare[0].transform.position : Slot(0, 1);
            foreach (var c in spare) { chips.Remove(c); Destroy(c.gameObject); }
            ChipTray tray = game != null ? game.Tray : null;
            foreach (var v in need)
            {
                var c = Chip.Spawn(v, at, transform.rotation, tray);
                c.spot = this;
                c.transform.SetParent(transform, true);
                c.SetGrabbable(!frozen);
                chips.Add(c);
            }
        }

        void QueueTidy(float delay)
        {
            tidyAt = Mathf.Max(tidyAt, Time.time + delay);
            if (tidyQueued || !isActiveAndEnabled) return;
            tidyQueued = true;
            StartCoroutine(TidyLater());
        }

        /// <summary>Once the chips have landed: colour up and restack (bigger at the bottom, columns of 10).</summary>
        IEnumerator TidyLater()
        {
            while (Time.time < tidyAt) yield return null;
            tidyQueued = false; tidyAt = -1f;
            int before = chips.Count;
            Rebuild(Total);
            Arrange(false, true);
            if (chips.Count < before) Sfx.Play("chip_stack", transform.position, 0.35f, Random.Range(0.95f, 1.08f));
        }

        void OnEnable()
        {
            tidyQueued = false; tidyAt = -1f;
            if (chips.Count > 0) { Rebuild(Total); Arrange(true, true); }
        }

        void OnDisable()
        {
            tidyQueued = false; tidyAt = -1f;
            foreach (var g in transients) if (g != null) Destroy(g);
            transients.Clear();
        }

        /// <summary>Keeps the UNDO history consistent with what is really on the spot (chips grabbed off, coloured up).</summary>
        void TrimAdds()
        {
            long t = Total, sum = 0;
            foreach (var a in adds) sum += a;
            while (sum > t && adds.Count > 0)
            {
                long over = sum - t, last = adds[adds.Count - 1];
                if (last <= over) { adds.RemoveAt(adds.Count - 1); sum -= last; }
                else { adds[adds.Count - 1] = last - over; sum -= over; }
            }
        }

        // ------------------------------------------------------------ betting
        public bool TryAdd(Chip c)
        {
            if (locked || game == null || !game.AcceptingBets || !game.CanBetOn(this)) { Sfx.UI("ui_error", 0.4f); return false; }
            /* no table limits */
            Add(c);
            return true;
        }

        public void Add(Chip c)
        {
            Put(c);
            adds.Add(c.value);
            Sfx.Play("chip_place", transform.position, 0.8f, Random.Range(0.95f, 1.08f));
            if (game != null) { game.NoteAdd(this); game.BetsChanged(); }
        }

        /// <summary>Puts a chip on the pile (on top for now; colour-up and restack once it has landed).</summary>
        void Put(Chip c)
        {
            c.spot = this;
            if (c.home == null && game != null) c.home = game.Tray;
            chips.Add(c);
            QueueTidy(Arrange(false, false) + 0.06f);
        }

        /// <summary>Returns the last bet placed on this spot to the wallet (UNDO).</summary>
        public bool RefundLast()
        {
            if (locked || chips.Count == 0) return false;
            TrimAdds();
            long t = Total;
            long amt = adds.Count > 0 ? adds[adds.Count - 1] : chips[chips.Count - 1].value;
            if (adds.Count > 0) adds.RemoveAt(adds.Count - 1);
            amt = System.Math.Min(amt, t);
            if (amt <= 0) return false;
            Rebuild(t - amt);
            Arrange(false, true);
            GameState.I.ReturnChips(amt);
            Sfx.Play("chip_grab", transform.position, 0.6f);
            if (game != null) game.BetsChanged();
            return true;
        }

        /// <summary>Returns everything above keep to the wallet (a bet a limit refused). Returns the amount refunded.</summary>
        public long RefundAbove(long keep)
        {
            if (locked) return 0;
            long t = Total; if (t <= keep) return 0;
            long amt = t - System.Math.Max(0, keep);
            Rebuild(t - amt);
            Arrange(false, true);
            TrimAdds();
            GameState.I.ReturnChips(amt);
            Sfx.Play("chip_grab", transform.position, 0.6f);
            if (game != null) game.BetsChanged();
            return amt;
        }

        /// <summary>How close a point is to this spot, relative to its size (smaller spots win on shared lines).</summary>
        public float Affinity(Vector3 world)
        {
            var l = transform.InverseTransformPoint(world);
            float dx = l.x / Mathf.Max(0.01f, size.x * 0.5f), dz = l.z / Mathf.Max(0.01f, size.y * 0.5f);
            return dx * dx + dz * dz;
        }

        public void Remove(Chip c)
        {
            chips.Remove(c);
            c.spot = null;
            TrimAdds();
            Arrange(false, true);   // the chips above it settle down: nothing is left hovering
            if (game != null) game.BetsChanged();
        }

        /// <summary>Quick-bet (buttons / desktop): chips fly from your rack onto the spot. The chips always come out of the
        /// table's chip rack when it has one (from is only used by tables without a rack).</summary>
        public bool AddValue(long amount, Vector3 from)
        {
            if (amount <= 0 || locked || game == null || !game.AcceptingBets || !game.CanBetOn(this)) return false;
            /* no table limits */
            if (!GameState.I.TakeChips(amount)) return false;
            SpawnChips(amount, from, true);
            adds.Add(amount);
            game.NoteAdd(this); game.BetsChanged();
            return true;
        }

        /// <summary>Adds chips during a round (double down, call): takes from the wallet and commits the extra stake.</summary>
        public bool AddLocked(long amount, Vector3 from)
        {
            if (amount > GameState.I.chips) { GameState.I.Notify("NOT ENOUGH CHIPS", Palette.Red); return false; }
            if (!GameState.I.CommitBet(amount)) return false;
            GameState.I.TakeChips(amount);
            SpawnChips(amount, from, false);
            if (game != null) game.BetsChanged();
            return true;
        }

        void SpawnChips(long amount, Vector3 from, bool grabbable)
        {
            if (game != null && game.Tray != null) from = game.ChipSource;
            ChipTray tray = game != null ? game.Tray : null;
            var vals = ChipKit.Split(amount, 12);
            for (int i = 0; i < vals.Count; i++)
            {
                // leave the rack as a small neat stack, then hop one by one onto the felt
                var c = Chip.Spawn(vals[i], from + transform.up * (ChipKit.Thick * i), transform.rotation, tray);
                c.SetGrabbable(grabbable);
                Put(c);
            }
            Sfx.Play("chip_place", transform.position, 0.8f, Random.Range(0.95f, 1.08f));
        }

        /// <summary>Refunds the chips (bets withdrawn before the round).</summary>
        public void Refund()
        {
            long t = Total;
            foreach (var c in chips) if (c != null) Destroy(c.gameObject);
            chips.Clear(); adds.Clear();
            if (t > 0) { GameState.I.ReturnChips(t); Sfx.Play("chip_stack", transform.position, 0.6f); }
            if (game != null) game.BetsChanged();
        }

        public void Lock(bool on)
        {
            locked = on;
            foreach (var c in chips) if (c != null) c.SetGrabbable(!on);
        }

        public void Highlight(bool on, Color c)
        {
            if (on && glow == null) glow = Build.GlowSprite(transform, Vector3.up * 0.02f, 0.25f, c, 0.9f, false);
            if (glow != null) { glow.gameObject.SetActive(on); glow.rotation = transform.rotation * Quaternion.Euler(90, 0, 0); }
        }

        /// <summary>Takes every chip off the spot (they now belong to whoever animates them: sweep, payout, machine).</summary>
        public List<Chip> TakeAll()
        {
            chips.RemoveAll(c => c == null);
            var list = new List<Chip>(chips);
            chips.Clear(); adds.Clear();
            tidyQueued = false; tidyAt = -1f; StopAllCoroutines();
            foreach (var c in list) { c.Halt(); c.SetGrabbable(false); }
            SortBigFirst(list);
            return list;
        }

        /// <summary>Losing bet: the dealer rakes the chips into a tidy pile on the felt on the dealer's side, then takes it.</summary>
        public IEnumerator Sweep(Vector3 toDealer)
        {
            var list = TakeAll();
            foreach (var c in list) transients.Add(c.gameObject);
            if (list.Count > 0) Sfx.Play("chips_sweep", transform.position, 0.7f);
            var pile = game != null ? game.FeltPoint(Vector3.Lerp(transform.position, toDealer, 0.4f), 0.1f + ChipKit.PileRadius(list.Count)) : toDealer;
            var rot = game != null ? game.transform.rotation : transform.rotation;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                StartCoroutine(Fx.Arc(c.transform, pile + rot * ChipKit.StackLocal(i, list.Count), 0.05f, 0.42f, rot * Quaternion.Euler(0, c.yaw, 0)));
            }
            yield return new WaitForSeconds(0.7f);
            foreach (var c in list) if (c != null) { transients.Remove(c.gameObject); Destroy(c.gameObject); }
            locked = false;
        }

        /// <summary>Winning bet: the dealer stacks the payout on the felt next to the stake, then everything flies back to
        /// your rack. The wallet is credited by TableGame.</summary>
        public IEnumerator PayOut(long payout, Vector3 fromDealer, Vector3 toPlayer)
        {
            var list = TakeAll();
            foreach (var c in list) transients.Add(c.gameObject);   // removed from the floor with it if it switches off
            long extra = payout - Total_of(list);
            var paid = new List<GameObject>();
            if (extra > 0)
            {
                Sfx.Play("chips_payout", transform.position, 0.8f);
                var vals = ChipKit.Split(extra, 30);
                int n = vals.Count;
                // the dealer's chips come off the felt between the spot and the dealer, as a tidy pile
                var src = game != null ? game.FeltPoint(Vector3.Lerp(transform.position, fromDealer, 0.4f), 0.1f + ChipKit.PileRadius(n)) : fromDealer;
                var srcRot = game != null ? game.transform.rotation : transform.rotation;
                // ...and are stacked beside the stake (towards the middle of the table), always inside the table outline
                var pc = PayoutCentre(list.Count, n);
                for (int i = 0; i < n; i++)
                {
                    var g = ChipKit.Visual(vals[i], transform, src + srcRot * ChipKit.StackLocal(i, n));
                    var yawRot = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                    g.transform.rotation = srcRot * yawRot;
                    paid.Add(g); transients.Add(g);
                    StartCoroutine(Fx.Arc(g.transform, pc + transform.rotation * ChipKit.StackLocal(i, n), 0.08f, 0.35f + i * 0.015f, transform.rotation * yawRot));
                }
                yield return new WaitForSeconds(0.45f + n * 0.015f);
            }
            yield return new WaitForSeconds(0.35f);
            Sfx.Play("chip_stack", transform.position, 0.8f);
            foreach (var c in list) if (c != null) StartCoroutine(Fx.Arc(c.transform, toPlayer, 0.15f, 0.4f));
            foreach (var g in paid) if (g != null) StartCoroutine(Fx.Arc(g.transform, toPlayer, 0.18f, 0.45f));
            yield return new WaitForSeconds(0.48f);
            foreach (var c in list) if (c != null) { transients.Remove(c.gameObject); Destroy(c.gameObject); }
            foreach (var g in paid) if (g != null) { transients.Remove(g); Destroy(g); }
            locked = false;
        }

        /// <summary>World centre (on the felt) of the payout pile: beside the stake pile, on the side facing the middle of
        /// the table, clamped inside the table outline.</summary>
        Vector3 PayoutCentre(int stakeCount, int payCount)
        {
            float gap = ChipKit.PileRadius(Mathf.Max(1, stakeCount)) + ChipKit.PileRadius(payCount) + 0.006f;
            float side = 1f;
            if (game != null && game.transform.InverseTransformPoint(transform.position).x > 0f) side = -1f;
            var local = new Vector3(side * gap, Surface, 0f);
            var w = transform.TransformPoint(local);
            if (game == null) return w;
            var tl = game.transform.InverseTransformPoint(w);
            var cl = game.OnTable(tl, ChipKit.PileRadius(payCount) + 0.05f);
            if ((cl - tl).sqrMagnitude < 1e-8f) return w;
            // pushed back inside: keep it on this spot's felt plane
            w = game.transform.TransformPoint(cl);
            var d = transform.InverseTransformPoint(w); d.y = Surface;
            return transform.TransformPoint(d);
        }

        static long Total_of(List<Chip> l) { long t = 0; foreach (var c in l) if (c != null) t += c.value; return t; }
    }
}
