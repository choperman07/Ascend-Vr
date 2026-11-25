using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ascend
{
    /// <summary>Base for every station with a dealer or host: physical chip betting (tray → bet spots), dealer avatar
    /// with voice, status sign, odds plaque, quick-bet panel, round lifecycle and payouts (sweep / push / pay).
    /// Local space: the player stands on -Z, the dealer on +Z, table top at y = TableY.</summary>
    public abstract class TableGame : MonoBehaviour
    {
        public string title = "TABLE";
        public string subtitle = "";
        public Tier requiredTier = Tier.Rookie;
        public int accentIndex;
        public long minBet = 100, maxBet = 25000;
        public Rarity dealerRarity = Rarity.Legendary;
        public string voice = "dealerF";
        public bool hasDealer = true;

        public static readonly Color[] Accents = { Palette.Teal, Palette.Magenta, Palette.Gold, Palette.Violet, Palette.Mint, Palette.Coral };
        public Color Accent { get { return Accents[Mathf.Clamp(accentIndex, 0, Accents.Length - 1)]; } }
        public virtual float TableY { get { return 0.9f; } }
        protected virtual Vector3 DealerPos { get { return new Vector3(0, 0, 0.85f); } }
        /// <summary>Legacy felt position of the chip rack: only used when the table has no side table (the rack now
        /// lives on a small round side table at the player's left hand, see BuildSideTable). Chips for quick bets always
        /// come out of the rack wherever it is (ChipSource).</summary>
        protected virtual Vector3 TrayPos { get { return new Vector3(-0.05f, TableY + 0.02f, -0.5f); } }
        protected virtual Vector3 PanelPos { get { return new Vector3(0.52f, TableY + 0.12f, -0.58f); } }
        protected virtual Vector3 StatusPos { get { return new Vector3(0, 2.05f, 0.55f); } }
        protected abstract string Rules { get; }

        protected AvatarBody dealer;
        protected TextMeshPro statusT, betT;
        protected readonly List<BetSpot> spots = new List<BetSpot>();
        protected RectTransform panel;
        protected ChipTray tray;
        public bool AcceptingBets { get; protected set; }
        protected bool running;
        readonly Dictionary<string, long> lastBets = new Dictionary<string, long>();
        readonly List<BetSpot> addOrder = new List<BetSpot>();
        public readonly List<Seat> seats = new List<Seat>();
        public Seat PlayerSeat { get; private set; }
        bool built;

        // ------------------------------------------------------------ seats (override per table shape)
        protected virtual bool HasSeats { get { return true; } }
        /// <summary>Centre the stools face.</summary>
        protected virtual Vector3 SeatCenter { get { return new Vector3(0, 0, 0.1f); } }
        protected virtual float SeatRadius { get { return 1.3f; } }
        /// <summary>Angles (deg, 0 = in front of the table on -Z) of the guest stools. The player's stool is at 0.</summary>
        protected virtual float[] SeatAngles { get { return new[] { -64f, -33f, 33f, 64f }; } }
        protected virtual Seat.Style SeatStyle { get { return Seat.Style.CasinoStool; } }
        /// <summary>Guest stools as (x, y=0, z, yaw). Default: an arc around SeatCenter at SeatAngles.</summary>
        protected virtual List<Vector4> SeatSpots
        {
            get
            {
                var l = new List<Vector4>();
                foreach (var a in SeatAngles)
                {
                    var dir = Quaternion.Euler(0, a, 0) * Vector3.back;
                    var pos = SeatCenter + dir * SeatRadius;
                    l.Add(new Vector4(pos.x, 0, pos.z, Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg));
                }
                return l;
            }
        }
        /// <summary>Machines: guests may use the player's stool while you are away.</summary>
        protected virtual bool SharedPlayerSeat { get { return false; } }
        /// <summary>Table downlight colour (warm white with a hint of the accent).</summary>
        protected virtual bool HasLamp { get { return true; } }

        protected Vector3 W(Vector3 local) { return transform.TransformPoint(local); }
        protected Vector3 PlayerChest { get { return Rig.I != null ? Rig.I.Head.position + Vector3.down * 0.45f : W(new Vector3(0, 1.2f, -0.9f)); } }
        protected Vector3 DealerChips { get { return dealer != null ? dealer.RightHand.position : W(new Vector3(0, TableY + 0.05f, 0.5f)); } }
        protected Quaternion Flat { get { return transform.rotation; } }
        /// <summary>The player's chip rack (on the side table next to the player's seat).</summary>
        public ChipTray Tray { get { return tray; } }
        /// <summary>World point quick-bet chips leave from: just above the rack.</summary>
        public Vector3 ChipSource { get { return tray != null ? tray.transform.TransformPoint(new Vector3(0, 0.06f, 0)) : W(TrayPos) + transform.up * 0.05f; } }
        /// <summary>World point won / returned chips fly back to: into the rack.</summary>
        public Vector3 ChipHome { get { return tray != null ? tray.transform.TransformPoint(new Vector3(0, 0.03f, 0)) : PlayerChest; } }
        /// <summary>Chips in flight owned by the table (swept piles, chips swallowed by a machine, payouts flying to you):
        /// destroyed when the floor switches off so none is left hanging in the air.</summary>
        readonly List<GameObject> loose = new List<GameObject>();

        protected virtual void Start()
        {
            if (built) return;
            built = true;
            BuildBase();
            BuildTable();
            FitCollider();
            BuildSeats();
            BuildSideTable();
            BuildPanel();
            AcceptingBets = true;
            GameState.I.Changed += Refresh;
            Refresh();
            Status("PLACE YOUR BETS", Palette.Text);
            if (dealer != null) dealer.lookAtTarget = Rig.I != null ? Rig.I.Head : null;
        }

        protected virtual void OnDestroy() { if (GameState.Exists) GameState.I.Changed -= Refresh; }

        void OnEnable() { if (built && dealer != null && Rig.I != null) dealer.lookAtTarget = Rig.I.Head; }

        // ------------------------------------------------------------ interrupted rounds
        bool settling, roundOpen; long pendingPay, pendingStake;

        /// <summary>The floor is switched off when you take the elevator / wrist menu: every coroutine of the round dies
        /// with it. Without this the table stayed "running" forever (no more bets, chips locked, stake lost). A round
        /// that was already being paid is paid in full; any other round is voided and the locked stakes go back.</summary>
        protected virtual void OnDisable()
        {
            foreach (var g in loose) if (g != null) Destroy(g);
            loose.Clear();
            if (!built || !GameState.Exists) return;
            if (!settling && roundOpen && Resumable) return;   // waiting for the player's decision: the hand simply resumes
            if (!settling && !roundOpen) { if (running) { KillLoops(); OnRoundAborted(); } return; }   // games that run their own rounds (poker, craps)
            if (settling) GameState.I.Pay(pendingPay, pendingStake, transform.position);
            else
            {
                long back = 0;
                foreach (var s in spots) if (s != null && s.locked) back += s.Total;
                if (back > 0) { GameState.I.Pay(back, back, transform.position); GameState.I.Notify("ROUND INTERRUPTED · " + back.ToString("N0") + " RETURNED", Palette.Gold); }
            }
            foreach (var s in spots)
            {
                if (s == null || !s.locked) continue;
                foreach (var c in s.chips) if (c != null) Destroy(c.gameObject);
                s.chips.Clear(); s.Lock(false);
            }
            settling = false; roundOpen = false; running = false;
            AcceptingBets = GameState.I.CanAccess(requiredTier);
            KillLoops();
            OnRoundAborted();
            BetsChanged();
            if (statusT != null && AcceptingBets) { statusT.text = "PLACE YOUR BETS"; statusT.color = Palette.Text; }
        }

        /// <summary>Sound loops started by a round (wheel, ball, dice rattle) die with it instead of resuming forever.</summary>
        void KillLoops()
        {
            foreach (var a in GetComponentsInChildren<AudioSource>(true))
                if (a != null && a.gameObject.name.StartsWith("loop_")) Destroy(a.gameObject);
        }

        /// <summary>Reset game-specific round state (flags, buttons, cards) after an interrupted round.</summary>
        protected virtual void OnRoundAborted() { }

        /// <summary>True while the round only waits for the player (no coroutine running): it survives being disabled.</summary>
        protected virtual bool Resumable { get { return false; } }

        void BuildBase()
        {
            if (hasDealer)
            {
                dealer = Avatars.Create("Dealer", transform, DealerPos, 180f, dealerRarity, voice.StartsWith("host") ? RoninRole.Host : RoninRole.Dealer, 1.8f);
                var prof = dealer.gameObject.AddComponent<AvatarProfile>();
                prof.displayName = title + " DEALER"; prof.rarity = dealerRarity; prof.xp = 120000; prof.status = "ASCEND STAFF · AT YOUR SERVICE";
                DealerManner.Attach(dealer, transform.position.y + TableY);
            }
            statusT = Txt.Glow("Status", transform, StatusPos, "", 0.13f, Palette.Text, 2.4f);
            betT = Txt.Make("Bet", transform, StatusPos + new Vector3(0, -0.17f, 0), "", 0.07f, Palette.Gold, Palette.Display);
            // the table sign turns to whoever looks at it (it used to read backwards from the far side of the table)
            var signPivot = new GameObject("StatusSign").transform; signPivot.SetParent(transform, false); signPivot.localPosition = StatusPos;
            statusT.transform.SetParent(signPivot, true); betT.transform.SetParent(signPivot, true);
            statusT.transform.localRotation = Quaternion.identity; betT.transform.localRotation = Quaternion.identity;
            signPivot.gameObject.AddComponent<Billboard>();
            // rules now open as a big, sharp card the first time you sit here (and from the RULES button)
            var trayGo = new GameObject("ChipTray");
            trayGo.transform.SetParent(transform, false);
            trayGo.transform.localPosition = TrayPos;
            tray = trayGo.AddComponent<ChipTray>();
            var k = new MeshKit();
            k.Ring(new Vector3(0, 0.02f, -1.05f), 0.36f, 0.4f, 40, Quaternion.identity, Accent, 1f);
            k.Spawn("StandHere", transform, Palette.Neon, false);
        }

        protected abstract void BuildTable();

        /// <summary>Solid body that follows the real outline of the table / machine (2D convex hull of its own geometry,
        /// extruded from the floor to just under the felt) — round tables are round, semicircles are semicircles, and the
        /// stools, dealer, chips and signs are left out, so you can walk right up to it without hitting an invisible box.</summary>
        public Bounds TableBounds { get; private set; }
        void FitCollider()
        {
            var inv = transform.worldToLocalMatrix;
            var pts = new List<Vector2>();
            bool any = false; var b = new Bounds();
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || mf.sharedMesh == null) continue;
                if (r.GetComponent<TMPro.TMP_Text>() != null || r.name == "StandHere" || r.name.StartsWith("Glow") || r.name.StartsWith("Beam")) continue;
                if (dealer != null && r.transform.IsChildOf(dealer.transform)) continue;
                if (!PartOfTable(r)) continue;
                var mesh = mf.sharedMesh;
                if (!mesh.isReadable) continue;
                var m = inv * mf.transform.localToWorldMatrix;
                var verts = mesh.vertices;
                int stepV = Mathf.Max(1, verts.Length / 600);
                for (int i = 0; i < verts.Length; i += stepV)
                {
                    var p = m.MultiplyPoint3x4(verts[i]);
                    if (p.y > TableY + 1.4f || p.y < -0.05f) continue;    // ignore signs and beams high above
                    pts.Add(new Vector2(p.x, p.z));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (!any) return;
            TableBounds = b;
            float top = Mathf.Min(b.max.y, TableY - 0.03f);
            foreach (var c in GetComponents<BoxCollider>()) if (!c.isTrigger) Destroy(c);
            var hull = Hull(pts);
            outline = hull.Count >= 3 ? hull : null;
            if (outline != null) { outlineCentre = Vector2.zero; foreach (var q in hull) outlineCentre += q; outlineCentre /= hull.Count; }
            if (hull.Count < 3) { var bc = gameObject.AddComponent<BoxCollider>(); bc.center = new Vector3(b.center.x, top * 0.5f, b.center.z); bc.size = new Vector3(b.size.x, Mathf.Max(0.3f, top), b.size.z); return; }
            var k = new MeshKit();
            k.Prism(hull, 0f, Mathf.Max(0.3f, top), Color.white, 0f);
            var colMesh = k.ToMesh(name + "_Col");
            var mc = gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = colMesh; mc.convex = true;
        }

        /// <summary>Andrew's monotone chain, slightly shrunk so the padded rail edge is not a wall 2 cm out.</summary>
        static List<Vector2> Hull(List<Vector2> p)
        {
            p.Sort((a, c) => a.x != c.x ? a.x.CompareTo(c.x) : a.y.CompareTo(c.y));
            var h = new List<Vector2>();
            System.Func<Vector2, Vector2, Vector2, float> cross = (o, a, c) => (a.x - o.x) * (c.y - o.y) - (a.y - o.y) * (c.x - o.x);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int i = 0; i < p.Count; i++)
                {
                    var q = pass == 0 ? p[i] : p[p.Count - 1 - i];
                    while (h.Count >= start + 2 && cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1);
                    h.Add(q);
                }
                h.RemoveAt(h.Count - 1);
            }
            // keep it under 64 points for a cheap convex collider
            while (h.Count > 64) { var t = new List<Vector2>(); for (int i = 0; i < h.Count; i += 2) t.Add(h[i]); h = t; }
            return h;
        }

        /// <summary>Real outline of the table (convex hull, table local XZ, counter-clockwise) and its centre.</summary>
        List<Vector2> outline; Vector2 outlineCentre;

        /// <summary>Clamps a point (table local) onto the playing surface, margin metres inside the edge (chip stacks, trays):
        /// inside the bounding box AND inside the real outline (a half-moon's corners are not table).</summary>
        public Vector3 OnTable(Vector3 local, float margin = 0.18f)
        {
            var b = TableBounds;
            if (b.size == Vector3.zero) return local;
            local.x = Mathf.Clamp(local.x, b.min.x + margin, b.max.x - margin);
            local.z = Mathf.Clamp(local.z, b.min.z + margin, b.max.z - margin);
            if (outline == null) return local;
            var p = new Vector2(local.x, local.z);
            if (Inside(outline, p) >= margin) return local;
            // pull it towards the middle until it is margin inside the outline
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 18; i++) { float m = (lo + hi) * 0.5f; if (Inside(outline, Vector2.Lerp(outlineCentre, p, m)) >= margin) lo = m; else hi = m; }
            var q = Vector2.Lerp(outlineCentre, p, lo);
            local.x = q.x; local.z = q.y;
            return local;
        }

        /// <summary>Signed distance of p inside a convex counter-clockwise polygon (negative = outside).</summary>
        static float Inside(List<Vector2> h, Vector2 p)
        {
            float m = float.MaxValue;
            for (int i = 0; i < h.Count; i++)
            {
                var a = h[i]; var e = h[(i + 1) % h.Count] - a;
                float len = e.magnitude; if (len < 1e-5f) continue;
                float d = (e.x * (p.y - a.y) - e.y * (p.x - a.x)) / len;
                if (d < m) m = d;
            }
            return m;
        }

        /// <summary>Meshes that make up the table itself (not the dealer, text, glows, chips, cards, seats, buttons, UI, the
        /// rack or the side table).</summary>
        bool PartOfTable(Renderer r)
        {
            if (r.GetComponent<TMPro.TMP_Text>() != null || r.name == "StandHere" || r.name.StartsWith("Glow") || r.name.StartsWith("Beam") || r.name.StartsWith("Chip_")) return false;
            if (dealer != null && r.transform.IsChildOf(dealer.transform)) return false;
            if (tray != null && r.transform.IsChildOf(tray.transform)) return false;
            if (sideTable != null && r.transform.IsChildOf(sideTable)) return false;
            if (r.name == "SideTable" || r.GetComponentInParent<ChipTray>() != null) return false;
            if (r.GetComponentInParent<Chip>() != null || r.GetComponentInParent<PokeButton>() != null || r.GetComponentInParent<Seat>() != null) return false;
            if (r.GetComponentInParent<Canvas>() != null || r.GetComponentInParent<Card3D>() != null) return false;
            return true;
        }

        // ------------------------------------------------------------ felt surface (chips rest exactly on it)
        List<MeshFilter> surfaceMeshes;

        /// <summary>Height (along up) of the highest table surface under a world point, relative to that point, looking
        /// 3 cm above and below. Probes the real table meshes (felts, tiles, counters have no colliders), so chips lie on the
        /// felt even on a tilted felt or a raised tile. 0 when nothing is found.</summary>
        public float SurfaceOffset(Vector3 world, Vector3 up)
        {
            if (surfaceMeshes == null)
            {
                surfaceMeshes = new List<MeshFilter>();
                foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
                {
                    var r = mf.GetComponent<MeshRenderer>();
                    if (r == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    if (!PartOfTable(r)) continue;
                    var rb = r.GetComponentInParent<Rigidbody>(); if (rb != null) continue;   // dice, balls
                    surfaceMeshes.Add(mf);
                }
            }
            const float above = 0.35f, range = 0.45f;   // tall enough to find the top of a raised (tilted) betting board
            up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;
            var o = world + up * above; var d = -up;
            var ray = new Ray(o, d);
            float best = float.MaxValue;
            foreach (var mf in surfaceMeshes)
            {
                if (mf == null || !mf.gameObject.activeInHierarchy) continue;
                var r = mf.GetComponent<MeshRenderer>(); if (r == null) continue;
                var bb = r.bounds; bb.Expand(0.004f);
                float bd;
                if (!bb.Contains(o) && (!bb.IntersectRay(ray, out bd) || bd > range)) continue;
                var tr = mf.transform;
                Vector3 lo = tr.InverseTransformPoint(o), ld = tr.InverseTransformVector(d);   // same t as in world (affine)
                var mesh = mf.sharedMesh; var v = mesh.vertices; var tris = mesh.triangles;
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    Vector3 a = v[tris[i]], e1 = v[tris[i + 1]] - a, e2 = v[tris[i + 2]] - a;
                    var pv = Vector3.Cross(ld, e2); float det = Vector3.Dot(e1, pv);
                    if (Mathf.Abs(det) < 1e-9f) continue;
                    float inv = 1f / det; var tv = lo - a;
                    float u = Vector3.Dot(tv, pv) * inv; if (u < 0f || u > 1f) continue;
                    var qv = Vector3.Cross(tv, e1);
                    float w = Vector3.Dot(ld, qv) * inv; if (w < 0f || u + w > 1f) continue;
                    float t = Vector3.Dot(e2, qv) * inv;
                    if (t < 0f || t > range || t >= best) continue;
                    var nW = tr.TransformDirection(Vector3.Cross(e1, e2)).normalized;
                    if (Mathf.Abs(Vector3.Dot(nW, up)) < 0.6f) continue;   // only faces you can put a chip on
                    best = t;
                }
            }
            return best < float.MaxValue ? above - best : 0f;
        }

        /// <summary>A world point moved onto the felt: clamped margin inside the table outline, at the surface height.</summary>
        public Vector3 FeltPoint(Vector3 world, float margin = 0.1f)
        {
            var l = transform.InverseTransformPoint(world);
            l = OnTable(l, margin); l.y = TableY;
            var w = W(l);
            return w + transform.up * (SurfaceOffset(w, transform.up) + 0.0004f);
        }

        void BuildSeats()
        {
            if (HasLamp)
            {
                var lg = new GameObject("TableLight");
                lg.transform.SetParent(transform, false);
                lg.transform.localPosition = new Vector3(0, 2.7f, 0.15f);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point; l.range = 3.6f; l.intensity = 1.7f; l.shadows = LightShadows.None;
                l.color = Color.Lerp(new Color(1f, 0.86f, 0.7f), Accent, 0.25f);
                l.renderMode = LightRenderMode.ForcePixel;
            }
            if (!HasSeats) return;
            int n = 0;
            var dealerAt = DealerPos;
            foreach (var sp in SeatSpots)
            {
                float yaw = sp.w;
                // every stool at a dealer table points at the croupier
                if (hasDealer && !SharedPlayerSeat)
                {
                    var d = new Vector3(dealerAt.x - sp.x, 0, dealerAt.z - sp.z);
                    if (d.sqrMagnitude > 0.01f) yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                }
                var st = Seat.Create("Seat " + (n++), transform, new Vector3(sp.x, 0, sp.z), yaw, SeatStyle, TableY);
                st.table = this;
                seats.Add(st);
            }
            // the player's own stool, right behind the stand ring (no collider: you stand / sit on it)
            // the player's stool sits ~45 cm from the table's real front edge, so the felt, buttons and cards are in reach
            PlayerSeat = Seat.Create("Player Seat", transform, new Vector3(0, 0, PlayerSeatZ), 0f, SeatStyle, TableY, false);
            PlayerSeat.forPlayer = !SharedPlayerSeat; PlayerSeat.table = this;
            if (SharedPlayerSeat) seats.Add(PlayerSeat);
        }

        float PlayerSeatZ { get { return TableBounds.size != Vector3.zero ? Mathf.Clamp(TableBounds.min.z - 0.45f, -1.32f, -0.8f) : -1.32f; } }

        // ------------------------------------------------------------ side table with your chip rack
        Transform sideTable;
        const float SideTopR = 0.15f;

        /// <summary>A small round side table (weighted foot, slim pedestal, round top with a glowing accent band) at the
        /// player's LEFT hand, about 0.4 m from the seated head, holding the chip rack: the felt stays free for bets and the
        /// betting panel floats on the right. It is placed clear of the table outline, the guests' stools (and knees) and
        /// your own stool; it has a solid collider and is not part of the table hull.</summary>
        void BuildSideTable()
        {
            if (tray == null) return;
            Vector3 seatPos; Quaternion seatRot; float seatH = 0.64f;
            if (PlayerSeat != null) { seatPos = PlayerSeat.transform.localPosition; seatRot = PlayerSeat.transform.localRotation; seatH = PlayerSeat.height; }
            else { seatPos = new Vector3(0, 0, PlayerSeatZ); seatRot = Quaternion.identity; }   // no stool: where it would be
            Vector3 right = seatRot * Vector3.right, fwd = seatRot * Vector3.forward;
            var head = seatPos + fwd * 0.1f;
            // clearances, centre to centre: a stool's foot (r 0.26) vs our foot (r 0.10) and a sitter's torso (r ~0.22) vs our
            // top (r 0.15); a guest's knees (r 0.12, under our top) vs our pedestal (r 0.06). Our top may overhang the edge
            // of your own cushion a little (it is 15 cm higher), never its foot.
            float ownR = seatH < 0.55f ? 0.48f : 0.37f;
            var obs = new List<Vector3>();   // (x, clearance, z) in table local
            foreach (var st in seats)
            {
                if (st == null || st == PlayerSeat) continue;
                var p = st.transform.localPosition;
                obs.Add(new Vector3(p.x, st.height < 0.55f ? 0.5f : 0.39f, p.z));
                var knee = p + st.transform.localRotation * Vector3.forward * 0.36f;
                obs.Add(new Vector3(knee.x, 0.2f, knee.z));
            }
            var ring = new Vector2(0, -1.05f);
            Vector3 bestP = seatPos + right * -0.4f + fwd * 0.14f; float bestCost = float.MaxValue;
            for (float ox = -0.3f; ox >= -0.62f; ox -= 0.02f)
                for (float oz = -0.3f; oz <= 0.36f; oz += 0.02f)
                {
                    var p = seatPos + right * ox + fwd * oz;
                    var p2 = new Vector2(p.x, p.z);
                    float bad = 0f;
                    bad += Mathf.Max(0f, ownR - (p2 - new Vector2(seatPos.x, seatPos.z)).magnitude);
                    bad += Mathf.Max(0f, (p2 - new Vector2(head.x, head.z)).magnitude - 0.56f);           // within easy reach
                    if (outline != null) bad += Mathf.Max(0f, Inside(outline, p2) + SideTopR + 0.05f); // clear of the table
                    foreach (var o in obs) bad += Mathf.Max(0f, o.y - (p2 - new Vector2(o.x, o.z)).magnitude);
                    float cost = bad * 40f + (new Vector2(ox, oz) - new Vector2(-0.4f, 0.14f)).magnitude
                               + Mathf.Max(0f, 0.52f - (p2 - ring).magnitude) * 0.5f;              // keep off the stand ring if we can
                    if (cost < bestCost) { bestCost = cost; bestP = p; }
                }
            bestP.y = 0f;
            float top = Mathf.Clamp(TableY - 0.08f, 0.7f, 1.05f);

            var go = new GameObject("SideTable");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = bestP;
            sideTable = go.transform;
            var body = Palette.Hex("1B1328"); var dark = Palette.Hex("120D1C");
            var k = new MeshKit();
            k.Cylinder(new Vector3(0, 0.012f, 0), 0.1f, 0.092f, 0.024f, 32, Quaternion.identity, dark, 0f);                // weighted foot
            k.Torus(new Vector3(0, 0.025f, 0), 0.088f, 0.005f, 32, 6, Quaternion.identity, Palette.Gold, 0.6f);
            k.Cylinder(new Vector3(0, 0.064f, 0), 0.05f, 0.024f, 0.08f, 20, Quaternion.identity, body, 0f);                // lower flare
            float colH = top - 0.104f - 0.09f;
            k.Cylinder(new Vector3(0, 0.104f + colH * 0.5f, 0), 0.022f, 0.022f, colH, 16, Quaternion.identity, body, 0f);   // pedestal
            k.Torus(new Vector3(0, 0.104f + colH * 0.55f, 0), 0.026f, 0.006f, 20, 6, Quaternion.identity, Accent, 1f);      // accent collar
            k.Cylinder(new Vector3(0, top - 0.0625f, 0), 0.024f, 0.06f, 0.055f, 20, Quaternion.identity, body, 0f);        // upper flare
            k.Cylinder(new Vector3(0, top - 0.0175f, 0), SideTopR - 0.006f, SideTopR, 0.035f, 40, Quaternion.identity, body, 0f);   // top
            k.Torus(new Vector3(0, top - 0.0175f, 0), SideTopR + 0.001f, 0.0055f, 40, 6, Quaternion.identity, Accent, 1f);  // glowing band
            k.Disc(new Vector3(0, top + 0.0005f, 0), SideTopR - 0.018f, 40, Quaternion.identity, dark, 0f);                  // inlay
            k.Ring(new Vector3(0, top + 0.0008f, 0), SideTopR - 0.02f, SideTopR - 0.014f, 40, Quaternion.identity, Palette.Gold, 0.8f);
            k.Spawn("SideTable", go.transform, Palette.Glossy, false);
            // solid (a box inside the round top / foot): you don't walk through it; not part of the table hull
            var col = go.AddComponent<BoxCollider>();
            const float half = 0.07f;   // inside the foot (r 0.10) and the top
            col.center = new Vector3(0, top * 0.5f, 0); col.size = new Vector3(half * 2f, top, half * 2f);

            // the rack on top, its front (local -Z) facing your seated head
            var dir = new Vector3(bestP.x - head.x, 0, bestP.z - head.z);
            float yaw = dir.sqrMagnitude > 1e-4f ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg : 0f;
            tray.transform.SetParent(go.transform, false);
            tray.transform.localPosition = new Vector3(0, top + 0.001f, 0);
            tray.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        }

        // ------------------------------------------------------------ first-time rules card
        RectTransform rulesCard;
        static RectTransform openRules;
        string RulesKey { get { return "asc2_rules_" + GetType().Name; } }

        /// <summary>Called by PlayerSeating when you sit at this table / machine: the first time you play a game its rules
        /// open as a large card in front of you; after that they only show from the RULES button.</summary>
        public void PlayerSat()
        {
            if (PlayerPrefs.GetInt(RulesKey, 0) == 0) StartCoroutine(RulesLater());
        }

        IEnumerator RulesLater() { yield return new WaitForSeconds(0.7f); ShowRules(); }

        public void ShowRules()
        {
            if (openRules != null) Destroy(openRules.gameObject);
            if (!Rig.Ready) return;
            PlayerPrefs.SetInt(RulesKey, 1); PlayerPrefs.Save();
            var head = Rig.I.Head;
            var fwd = Rig.I.HeadForwardFlat;
            var pos = head.position + fwd * 0.8f + Vector3.up * 0.02f;
            rulesCard = UIKit.Panel("RulesCard", null, pos, Quaternion.LookRotation(fwd, Vector3.up), new Vector2(1100, 760), 0.86f, Accent);
            openRules = rulesCard;
            UIKit.Label(rulesCard, title, 40, 26, 1020, 80, 64, Accent, Palette.Display);
            if (!string.IsNullOrEmpty(subtitle)) UIKit.Label(rulesCard, subtitle, 40, 100, 1020, 40, 28, Palette.Muted, Palette.Mono);
            UIKit.Label(rulesCard, "HOW TO PLAY", 40, 150, 1020, 44, 32, Palette.Gold, Palette.Display);
            var rulesT = UIKit.Label(rulesCard, Rules.Replace(" · ", "\n· "), 40, 200, 1020, 440, 34, Palette.Text, Palette.Body, TextAlignmentOptions.TopLeft);
            // long rule sheets (blackjack side bets, poker, three card) ran 16+ lines and spilled over the button
            rulesT.enableAutoSizing = true; rulesT.fontSizeMin = 16; rulesT.fontSizeMax = 34;
            UIKit.Btn(rulesCard, "GOT IT · LET'S PLAY", 300, 660, 500, 76, Accent, () => { if (rulesCard != null) Destroy(rulesCard.gameObject); }, 34);
            Sfx.UI("ui_open", 0.5f);
            StartCoroutine(CloseRulesWhenLeaving(rulesCard));
        }

        IEnumerator CloseRulesWhenLeaving(RectTransform card)
        {
            while (card != null)
            {
                if (!PlayerSeating.Seated && Rig.Ready && (Rig.I.Head.position - card.position).sqrMagnitude > 9f) { Destroy(card.gameObject); yield break; }
                yield return new WaitForSeconds(0.3f);
            }
        }

        /// <summary>The bet cell under a world point on the felt (smallest cell wins, so line bets beat the numbers they touch).</summary>
        public BetSpot SpotAt(Vector3 world)
        {
            BetSpot best = null; float area = float.MaxValue;
            foreach (var s in spots)
            {
                if (s == null) continue;
                var l = s.transform.InverseTransformPoint(world);
                if (Mathf.Abs(l.y) > 0.08f) continue;   // not on this spot's felt plane
                if (Mathf.Abs(l.x) <= s.size.x * 0.5f && Mathf.Abs(l.z) <= s.size.y * 0.5f)
                {
                    float a = s.size.x * s.size.y;
                    if (a < area) { area = a; best = s; }
                }
            }
            return best;
        }

        /// <summary>The bet cell a ray points at, each cell's felt plane taken along its own up (tilted felts).</summary>
        public BetSpot SpotAt(Ray ray)
        {
            BetSpot best = null; float area = float.MaxValue;
            foreach (var s in spots)
            {
                if (s == null) continue;
                var plane = new Plane(s.transform.up, s.transform.position);
                float enter;
                if (!plane.Raycast(ray, out enter)) continue;
                var l = s.transform.InverseTransformPoint(ray.GetPoint(enter));
                if (Mathf.Abs(l.x) <= s.size.x * 0.5f && Mathf.Abs(l.z) <= s.size.y * 0.5f)
                {
                    float a = s.size.x * s.size.y;
                    if (a < area) { area = a; best = s; }
                }
            }
            return best;
        }

        public void NoteAdd(BetSpot s) { addOrder.Add(s); }

        /// <summary>Some bets only open at certain moments (craps pass line on the come-out roll).</summary>
        public virtual bool CanBetOn(BetSpot s) { return true; }

        protected void UndoBet()
        {
            if (!AcceptingBets) return;
            for (int i = addOrder.Count - 1; i >= 0; i--)
            {
                var s = addOrder[i]; addOrder.RemoveAt(i);
                if (s != null && s.RefundLast()) return;
            }
            Sfx.UI("ui_error", 0.4f);
        }

        protected void DoubleBets()
        {
            if (!AcceptingBets) return;
            long need = TotalBet;
            if (need <= 0) { GameState.I.Notify("PLACE A BET FIRST", Palette.Gold); return; }
            if (GameState.I.chips < need) { GameState.I.Notify("NOT ENOUGH CHIPS TO DOUBLE", Palette.Red); return; }
            var snapshot = new List<KeyValuePair<BetSpot, long>>();
            foreach (var s in spots) if (s.Total > 0) snapshot.Add(new KeyValuePair<BetSpot, long>(s, s.Total));
            foreach (var kv in snapshot) kv.Key.AddValue(kv.Value, ChipSource);
            Sfx.Play("chip_stack", ChipSource, 0.7f);
        }

        /// <summary>Seated guests react to the round (cheer, groan, clap) like real players around you.</summary>
        protected void GuestsReact(bool playerWon)
        {
            foreach (var st in seats)
            {
                var o = st.occupant; if (o == null) continue;
                float r = Random.value;
                if (playerWon && r < 0.45f) o.Play(Random.value < 0.5f ? Emote.Clap : Emote.Cheer, 1.6f);
                else if (r < 0.25f) o.Play(Emote.Cheer, 1.4f);
                else if (r < 0.45f) o.Play(Emote.Facepalm, 1.4f);
                else if (r < 0.6f) o.Play(Emote.Nod, 1.0f);
            }
        }

        /// <summary>The spot used by quick-bet buttons.</summary>
        protected virtual BetSpot DefaultSpot { get { return spots.Count > 0 ? spots[0] : null; } }

        protected virtual Vector2 PanelSize { get { return new Vector2(560, 420); } }
        /// <summary>Spin / deal games: the betting panel tucks itself away while the round plays and comes back for the next bets.</summary>
        public virtual bool AutoHidePanel { get { return false; } }

        /// <summary>False for games with their own action buttons only (poker vs players).</summary>
        protected virtual bool QuickBets { get { return true; } }

        protected virtual void BuildPanel()
        {
            panel = UIKit.Panel("Panel", transform, PanelPos, Quaternion.Euler(42f, 18f, 0), PanelSize, 0.42f, Accent);
            // betting UI comes up to the player instead of lying on the table
            panel.gameObject.AddComponent<PlayerLevelPanel>().Init(transform, new Vector3(0, 0, -1.05f), new Vector3(PanelPos.x + 0.15f, 0, PanelPos.z - 0.1f));
            float w = PanelSize.x;
            UIKit.Label(panel, QuickBets ? "QUICK BET" : "YOUR MOVE", 20, 12, 150, 36, 24, Palette.Muted, Palette.Display);
            UIKit.Btn(panel, "HIDE", w - 380, 8, 80, 40, Palette.Muted, () => { var pl = panel.GetComponent<PlayerLevelPanel>(); if (pl != null) pl.Minimise(); }, 26);
            UIKit.Btn(panel, "RULES", w - 290, 8, 130, 40, Palette.Violet, ShowRules, 22);
            UIKit.Btn(panel, "EXIT", w - 150, 8, 130, 40, Palette.Red, () => { if (PlayerSeating.I != null) PlayerSeating.I.StandUp(true); }, 22);
            if (!QuickBets) { ExtraPanel(panel, 56); return; }
            long[] q = { 100, 500, 1000, 5000 };
            for (int i = 0; i < q.Length; i++)
            {
                long v = q[i];
                UIKit.Btn(panel, "+" + (v >= 1000 ? (v / 1000) + "K" : v.ToString()), 20 + i * ((w - 40) / 4), 52, (w - 40) / 4 - 8, 70, Palette.Gold, () => QuickBet(v), 32);
            }
            float bw = (w - 40) / 4;
            UIKit.Btn(panel, "UNDO", 20, 132, bw - 8, 64, Palette.Violet, UndoBet, 26);
            UIKit.Btn(panel, "x2", 20 + bw, 132, bw - 8, 64, Palette.Mint, DoubleBets, 30);
            UIKit.Btn(panel, "CLEAR", 20 + bw * 2, 132, bw - 8, 64, Palette.Red, ClearBets, 26);
            UIKit.Btn(panel, "REBET", 20 + bw * 3, 132, bw - 8, 64, Palette.Teal, Rebet, 26);
            ExtraPanel(panel, 210);
        }

        /// <summary>Game-specific controls below the quick bets (y = start).</summary>
        protected virtual void ExtraPanel(RectTransform p, float y) { }

        protected virtual void QuickBet(long v)
        {
            var s = DefaultSpot;
            if (s == null) return;
            if (!AcceptingBets) { GameState.I.Notify("NO MORE BETS · WAIT FOR THE NEXT ROUND", Palette.Gold); return; }
            s.AddValue(v, ChipSource);
        }

        protected void ClearBets()
        {
            if (!AcceptingBets) return;
            foreach (var s in spots) s.Refund();
        }

        protected void Rebet()
        {
            if (!AcceptingBets || lastBets.Count == 0) return;
            foreach (var s in spots)
            {
                long v;
                if (lastBets.TryGetValue(s.id, out v) && v > 0) s.AddValue(v, ChipSource);
            }
        }

        public virtual void BetsChanged()
        {
            long t = TotalBet;
            if (betT != null) betT.text = t > 0 ? "YOUR BET " + t.ToString("N0") : "";
        }

        protected long TotalBet { get { long t = 0; foreach (var s in spots) t += s.Total; return t; } }

        protected virtual void Refresh()
        {
            bool open = GameState.I.CanAccess(requiredTier);
            if (!open && statusT != null) Status("LOCKED · " + GameState.TierNames[(int)requiredTier] + " TIER", Palette.TierColor(requiredTier));
            AcceptingBets = open && !running;
        }

        protected void Status(string s, Color c)
        {
            if (statusT == null) return;
            statusT.text = s; statusT.color = c;
            StartCoroutine(Fx.Pulse(statusT.transform, 1.08f, 0.25f));
        }

        protected float Say(string key) { return dealer != null ? Voice.Say(voice + "_" + key, dealer.HeadBone, 0.55f) : 0f; }

        /// <summary>Dealer reaches with the right hand to a world point and back.</summary>
        protected IEnumerator Reach(Vector3 world, float dur = 0.5f, bool right = true)
        {
            if (dealer == null) { yield return new WaitForSeconds(dur); yield break; }
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float w = Mathf.Sin(Mathf.Clamp01(t / dur) * Mathf.PI);
                dealer.Reach(right, world, w);
                yield return null;
            }
            dealer.Reach(right, world, 0f);
        }

        /// <summary>Starts a round with the chips on the spots. False when nothing is bet or a limit stops it.</summary>
        protected bool BeginRound()
        {
            if (running) return false;
            if (!GameState.I.CanAccess(requiredTier)) { Sfx.UI("ui_error"); return false; }
            long total = TotalBet;
            if (total <= 0) { GameState.I.Notify("PLACE A BET FIRST · GRAB CHIPS OR USE QUICK BET", Palette.Gold); Sfx.UI("ui_error", 0.5f); return false; }
            if (!GameState.I.CommitBet(total)) { foreach (var s in spots) s.Refund(); return false; }
            lastBets.Clear();
            foreach (var s in spots) { if (s.Total > 0) lastBets[s.id] = s.Total; s.Lock(true); }
            addOrder.Clear();
            running = true; roundOpen = true; AcceptingBets = false;
            GameState.I.Record(GetType().Name.ToLowerInvariant());
            return true;
        }

        /// <summary>Resolves every spot with its multiplier (0 = lose, 1 = push, 2 = even money...).</summary>
        protected IEnumerator Settle(System.Func<BetSpot, float> multiplier)
        {
            long stake = 0, pay = 0;
            var winners = new List<BetSpot>(); var losers = new List<BetSpot>();
            var amounts = new Dictionary<BetSpot, long>();
            foreach (var s in spots)
            {
                long t = s.Total; if (t <= 0) continue;
                stake += t;
                float m = multiplier(s);
                long p = (long)System.Math.Floor(t * (double)m);
                amounts[s] = p; pay += p;
                if (p >= t) winners.Add(s); else losers.Add(s);   // half-back bets are raked; the refund is credited
            }
            settling = true; pendingPay = pay; pendingStake = stake;
            if (losers.Count > 0)
            {
                if (dealer != null) StartCoroutine(Reach(losers[0].transform.position, 0.6f));
                StartCoroutine(SweepAll(losers));
                yield return new WaitForSeconds(0.6f);
            }
            if (winners.Count > 0)
            {
                if (dealer != null) StartCoroutine(Reach(winners[0].transform.position, 0.6f, false));
                foreach (var s in winners) StartCoroutine(s.PayOut(amounts[s], DealerChips, ChipHome));
                yield return new WaitForSeconds(1.5f);
            }
            var where = W(new Vector3(0, TableY + 0.1f, -0.1f));
            settling = false;
            GameState.I.Pay(pay, stake, where);
            GuestsReact(pay > stake);
            if (pay > stake) { Say(pay >= stake * 5 ? "big" : "winner"); dealerCheer(); }
            else if (pay == stake && stake > 0) { Juice.Push(where); Say("push"); }
            else if (stake > 0) { Juice.Lose(where); if (Random.value < 0.4f) Say("next_time"); }
            foreach (var s in spots) s.Lock(false);
            running = false; roundOpen = false;
            AcceptingBets = GameState.I.CanAccess(requiredTier);
            BetsChanged();
        }

        /// <summary>The dealer rakes every losing bet into ONE tidy pile on the felt between the bets and the dealer
        /// (clamped inside the table outline), then takes it away.</summary>
        IEnumerator SweepAll(List<BetSpot> losers)
        {
            var all = new List<Chip>();
            Vector3 c = Vector3.zero; int ns = 0;
            foreach (var s in losers)
            {
                if (s == null) continue;
                c += s.transform.position; ns++;
                all.AddRange(s.TakeAll());
            }
            foreach (var s in losers) if (s != null) s.locked = false;
            if (all.Count == 0 || ns == 0) yield break;
            all.Sort((a, b) => b.value.CompareTo(a.value));
            c /= ns;
            Sfx.Play("chips_sweep", c, 0.7f);
            var toward = W(new Vector3(DealerPos.x, TableY, DealerPos.z));
            var pile = FeltPoint(Vector3.Lerp(c, toward, 0.35f), 0.08f + ChipKit.PileRadius(all.Count));
            var rot = transform.rotation;
            for (int i = 0; i < all.Count; i++)
            {
                var ch = all[i]; if (ch == null) continue;
                ch.transform.SetParent(transform, true);
                loose.Add(ch.gameObject);
                StartCoroutine(Fx.Arc(ch.transform, pile + rot * ChipKit.StackLocal(i, all.Count), 0.05f, 0.4f + i * 0.008f, rot * Quaternion.Euler(0, ch.yaw, 0)));
            }
            yield return new WaitForSeconds(0.75f + all.Count * 0.008f);
            foreach (var ch in all) if (ch != null) { loose.Remove(ch.gameObject); Destroy(ch.gameObject); }
        }

        /// <summary>Multi-roll games (craps): commits whatever was added to the felt since the last call.
        /// Returns false if a limit blocks it (the new chips are refunded).</summary>
        protected bool CommitNew(Dictionary<BetSpot, long> committed)
        {
            long add = 0;
            foreach (var s in spots) { long c; committed.TryGetValue(s, out c); if (s.Total > c) add += s.Total - c; }
            if (add <= 0) return true;
            if (!GameState.I.CanAccess(requiredTier)) { Sfx.UI("ui_error"); return false; }
            if (!GameState.I.CommitBet(add))
            {
                foreach (var s in spots) { long c; committed.TryGetValue(s, out c); if (s.Total > c) s.RefundAbove(c); }
                return false;
            }
            foreach (var s in spots) committed[s] = s.Total;
            GameState.I.Record(GetType().Name.ToLowerInvariant());
            return true;
        }

        /// <summary>Resolves only some spots (others stay on the felt): mult 0 = rake, 1 = return, >1 = pay.</summary>
        protected IEnumerator ResolveSpots(Dictionary<BetSpot, float> results, Dictionary<BetSpot, long> committed)
        {
            long stake = 0, pay = 0; bool any = false;
            var raked = new List<BetSpot>();
            foreach (var kv in results)
            {
                var s = kv.Key; long t = s.Total; if (t <= 0) continue;
                any = true; stake += t;
                long p = (long)System.Math.Floor(t * (double)kv.Value);
                pay += p;
                if (p <= 0) raked.Add(s);
                else StartCoroutine(s.PayOut(p, DealerChips, ChipHome));
                if (committed != null) committed[s] = 0;
            }
            if (raked.Count > 0) StartCoroutine(SweepAll(raked));
            if (!any) yield break;
            if (dealer != null) StartCoroutine(Reach(W(new Vector3(0, TableY + 0.05f, 0.1f)), 0.6f));
            yield return new WaitForSeconds(1.6f);
            var where = W(new Vector3(0, TableY + 0.1f, -0.1f));
            GameState.I.Pay(pay, stake, where);
            if (pay > stake) { GuestsReact(true); Say("winner"); }
            else if (pay < stake) Juice.Lose(where);
            BetsChanged();
        }

        // ------------------------------------------------------------ arcade-style helpers (one play = one stake)
        /// <summary>Takes the chips on the spot as the stake of a single play (they fly into the machine).
        /// Returns 0 when there is no bet or a limit stops it. With autoRebet the same bet is placed again.</summary>
        protected long ConsumeStake(BetSpot s, Vector3 into, bool autoRebet = true)
        {
            if (!GameState.I.CanAccess(requiredTier)) { Sfx.UI("ui_error"); return 0; }
            long stake = s.Total;
            if (stake <= 0)
            {
                // nothing on the spot: place the minimum automatically so tapping "play" always works
                if (!s.AddValue(minBet, ChipSource)) return 0;
                stake = s.Total;
            }
            if (!GameState.I.CommitBet(stake)) { s.Refund(); return 0; }
            GameState.I.Record(GetType().Name.ToLowerInvariant());
            var list = s.TakeAll();
            foreach (var c in list) if (c != null) { c.transform.SetParent(transform, true); loose.Add(c.gameObject); StartCoroutine(SwallowChip(c, into)); }
            if (autoRebet && GameState.I.chips >= stake) StartCoroutine(RebetLater(s, stake));
            BetsChanged();
            return stake;
        }

        IEnumerator SwallowChip(Chip c, Vector3 into)
        {
            yield return StartCoroutine(Fx.Arc(c.transform, into, 0.1f, 0.35f));
            if (c != null) { loose.Remove(c.gameObject); Destroy(c.gameObject); }
        }

        IEnumerator RebetLater(BetSpot s, long amount)
        {
            yield return new WaitForSeconds(0.45f);
            if (s.Total == 0 && GameState.I.chips >= amount) s.AddValue(amount, ChipSource);
        }

        /// <summary>Pays a single play: chips burst from the machine to the player, wallet credited, feedback.</summary>
        protected void PayDirect(long payout, long stake, Vector3 from)
        {
            if (payout > 0)
            {
                int i = 0;
                foreach (var v in ChipKit.Split(payout, 14))
                {
                    // burst out of the machine (inside the table outline), then fly into your rack
                    var g = ChipKit.Visual(v, transform, from + Random.insideUnitSphere * 0.05f);
                    loose.Add(g);
                    StartCoroutine(FlyAndDie(g.transform, 0.25f + i * 0.04f));
                    i++;
                }
                Sfx.Play("chips_payout", from, 0.8f);
            }
            GameState.I.Pay(payout, stake, from);
            if (payout > stake) { if (dealer != null) dealer.Play(Emote.Cheer, 1.2f); }
            else if (payout == stake && stake > 0) Juice.Push(from);
            else Juice.Lose(from);
        }

        IEnumerator FlyAndDie(Transform t, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (t == null) yield break;
            yield return StartCoroutine(Fx.Arc(t, ChipHome, 0.25f, 0.45f));
            if (t != null) { loose.Remove(t.gameObject); Destroy(t.gameObject); }
        }

        void dealerCheer() { if (dealer != null) dealer.Play(Emote.Clap, 1.2f); }
    }
}
