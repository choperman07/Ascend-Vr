using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Ascend
{
    /// <summary>The other guests. Offline they are simulated; online, real players join them on the floor.</summary>
    public static class Roster
    {
        static readonly string[] Names = { "VOLT_KIRA", "PIXEL_SAINT", "ECHO.NOVA", "GLITCH_QUEEN", "ROOK_ZERO", "LUNA//404", "KAIJU_KID", "BYTE_BANDIT", "SOLAR_FOX", "MIRA.EXE", "DRIFT_KING", "NEKO_NULL", "HEX_HANA", "ORBIT_OLLIE", "ZERO_COOL", "NOVA_RAY" };
        static readonly Rarity[] Faces = { Rarity.Mythic, Rarity.Epic, Rarity.Legendary, Rarity.Epic, Rarity.Rare, Rarity.Legendary, Rarity.Common, Rarity.Rare, Rarity.Epic, Rarity.Rare, Rarity.Common, Rarity.Legendary, Rarity.Rare, Rarity.Common, Rarity.Epic, Rarity.Rare };
        static readonly long[] BaseXP = { 98420, 91200, 84900, 77310, 70050, 52400, 41800, 33650, 26900, 18400, 9700, 6200, 30200, 12800, 64000, 22100 };
        static readonly string[] Squads = { "NOVA", "VOLT", "", "GLITCH", "", "LUNA", "", "NOVA", "VOLT", "", "", "GLITCH", "LUNA", "", "VOLT", "NOVA" };
        static long[] xp;
        public static int Count { get { return Names.Length; } }
        public static string Name(int i) { return Names[i % Names.Length]; }
        public static Rarity Face(int i) { return Faces[i % Faces.Length]; }
        public static string Squad(int i) { return Squads[i % Squads.Length]; }
        public static long XP(int i) { if (xp == null) xp = (long[])BaseXP.Clone(); return xp[i % xp.Length]; }
        public static void Gain(int i, long amount) { if (xp == null) xp = (long[])BaseXP.Clone(); xp[i % xp.Length] += amount; }
        public static Tier TierOf(long x)
        {
            for (int t = GameState.TierXP.Length - 1; t >= 0; t--) if (x >= GameState.TierXP[t]) return (Tier)t;
            return Tier.Rookie;
        }
    }

    public enum SpotKind { Wander, Dance, Bar, Table, Window, Watch }

    /// <summary>Spawns guests on the active floor, bakes a NavMesh for it at runtime, and drives chat and floor events.</summary>
    public class BotDirector : MonoBehaviour
    {
        public int botsPerFloor = 10;
        [Tooltip("Dancers packed on the lobby dance floor")] public int lobbyDancers = 8;
        readonly List<BotAvatar> bots = new List<BotAvatar>();
        NavMeshDataInstance navInstance;
        float nextChat, nextEvent;

        static readonly string[] Idle =
        {
            "gg everyone", "who's raiding tonight?", "this place is unreal", "anyone up for a case battle?",
            "that crate reveal never gets old", "taking a quick break, brb", "the view from up here is insane", "cashed out 2x on crash, done for now",
            "squad up on the roof!", "set my loss limit, playing chill tonight", "the DJ is going off", "who wants a coinflip?",
            "new avatar frame dropped", "mines with 3 is my comfort zone", "nice play", "see you on the leaderboard", "the house jackpot is huge right now"
        };
        static readonly string[] Events =
        {
            "{0} pulled an EPIC from a crate", "{0} cashed out 3.4x on Crash", "{0} won a case battle with 5x", "{0}'s squad hit The House to phase 2",
            "{0} reached CONTENDER", "{0} caught a MANTA RAY at Neon Reef", "{0} hit a flush at Casino Hold'em", "{0} hit 17 straight up on roulette"
        };

        void Start()
        {
            if (FloorManager.I != null)
            {
                FloorManager.I.FloorActivated += OnFloor;
                // FloorManager.Start (on the same object) may already have activated the start floor before we subscribed
                if (FloorManager.I.Current >= 0 && bots.Count == 0) OnFloor(FloorManager.I.Current);
            }
            nextChat = Time.time + 4f; nextEvent = Time.time + 10f;
        }

        void OnDestroy() { if (FloorManager.I != null) FloorManager.I.FloorActivated -= OnFloor; if (navInstance.valid) NavMesh.RemoveNavMeshData(navInstance); }

        void OnFloor(int floor) { StopAllCoroutines(); StartCoroutine(Populate(floor)); }

        /// <summary>Everybody leaves (mafia mode, when the player is away from the tower).</summary>
        public void ClearAll()
        {
            StopAllCoroutines();
            foreach (var b in bots) if (b != null) Destroy(b.gameObject);
            bots.Clear();
            foreach (var c in crowd) if (c != null) Destroy(c);
            crowd.Clear();
        }

        [Tooltip("Max guests sitting at tables / machines / the bar on one floor")] public int seatedPerFloor = 20;
        [Tooltip("Share of free seats that get a guest")] [Range(0, 1)] public float seatFill = 0.6f;

        IEnumerator Populate(int floor)
        {
            foreach (var b in bots) if (b != null) Destroy(b.gameObject);
            bots.Clear();
            foreach (var c in crowd) if (c != null) Destroy(c);
            crowd.Clear();
            yield return null;
            yield return null;   // tables build their stools in Start
            Bake(floor);
            var spots = new List<BotSpot>();
            var root = FloorManager.I.floors[floor];
            spots.AddRange(root.GetComponentsInChildren<BotSpot>());
            foreach (var s in spots) { s.taken = false; FaceSpot(s, root); }
            bool roof = floor == Tower.Count - 1;
            var used = new HashSet<int>();
            System.Func<int> nextId = () => { int id; int guard = 0; do { id = Odds.Range(0, Roster.Count); } while (used.Contains(id) && guard++ < 50); used.Add(id); return id; };

            // 1a) always five slot machines with someone pulling the lever
            if (!roof)
            {
                var machines = new List<Slots>(root.GetComponentsInChildren<Slots>());
                Shuffle(machines);
                int filled = 0;
                foreach (var m in machines)
                {
                    if (filled >= 5) break;
                    var ps = m.PlayerSeat; if (ps == null || !ps.Free) continue;
                    int id = nextId();
                    var body = Avatars.Create("Guest_" + Roster.Name(id), transform, ps.transform.position, ps.transform.eulerAngles.y, Roster.Face(id), RoninRole.Player, Random.Range(1.62f, 1.85f));
                    var bot = body.gameObject.AddComponent<BotAvatar>();
                    bot.Init(this, id, spots, floor, BotJob.Guest, ps, WealthFor(floor));
                    bots.Add(bot); filled++;
                    yield return null;
                }
            }
            // 1b) guests sitting and betting at the tables and machines
            if (!roof)
            {
                var seats = new List<Seat>();
                foreach (var st in root.GetComponentsInChildren<Seat>()) if (st.Free) seats.Add(st);
                Shuffle(seats);
                int want = Mathf.Min(seatedPerFloor, Mathf.RoundToInt(seats.Count * seatFill));
                for (int i = 0; i < want && i < seats.Count; i++)
                {
                    int id = nextId();
                    var st = seats[i];
                    var body = Avatars.Create("Guest_" + Roster.Name(id) + "_" + i, transform, st.transform.position, st.transform.eulerAngles.y, Roster.Face(id), RoninRole.Player, Random.Range(1.62f, 1.85f));
                    var bot = body.gameObject.AddComponent<BotAvatar>();
                    bot.Init(this, id, spots, floor, BotJob.Guest, st, WealthFor(floor));
                    bots.Add(bot);
                    if (i % 3 == 2) yield return null;
                }
            }
            if (spots.Count == 0) yield break;
            // 2) guests walking around, drinking at the bar, watching games, dancing, looking out of the windows
            int n = roof ? 3 : botsPerFloor;
            for (int i = 0; i < n; i++)
            {
                int id = nextId();
                var spot = spots[Odds.Range(0, spots.Count)];
                var pos = spot.transform.position + new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
                var anim = Avatars.Create("Guest_" + Roster.Name(id) + "_w" + i, transform, pos, Random.Range(0, 360), Roster.Face(id), RoninRole.Player, Random.Range(1.62f, 1.85f));
                var bot = anim.gameObject.AddComponent<BotAvatar>();
                bot.Init(this, id, spots, floor, BotJob.Guest, null, WealthFor(floor));
                if (Random.value < 0.35f) bot.GiveDrink(Random.Range(0, 3));
                bots.Add(bot);
                yield return null;
            }
            if (roof) yield break;
            // 2b) the lobby dance floor: a crowd facing the DJ
            if (floor == 0) yield return StartCoroutine(DanceCrowd(root, spots));
            // 3) staff: two waiter robots on every floor and a security patrol
            for (int i = 0; i < 2; i++)
            {
                var spot = spots[Odds.Range(0, spots.Count)];
                var w = Avatars.Robot("Waiter " + i, transform, spot.transform.position, Random.Range(0, 360), Rarity.Rare);
                var bot = w.gameObject.AddComponent<BotAvatar>();
                bot.Init(this, i, spots, floor, BotJob.Waiter);
                bots.Add(bot);
                yield return null;
            }
            if (floor != 0)
            {
                var spot = spots[Odds.Range(0, spots.Count)];
                var g = Avatars.Create("Patrol", transform, spot.transform.position, 0f, Rarity.Rare, RoninRole.Security, 1.85f);
                var bot = g.gameObject.AddComponent<BotAvatar>();
                bot.Init(this, floor, spots, floor, BotJob.Patrol);
                bots.Add(bot);
            }
        }

        /// <summary>Who you meet on each floor: the Sky Lounge is mostly high rollers, the lower floors a mix.</summary>
        /// <summary>Bot spots are placed without a rotation, so guests arriving at one would all face +Z. Give each
        /// spot a sensible facing once: the bar counter, the game being watched, the view out of the window, the DJ.</summary>
        static void FaceSpot(BotSpot s, Transform floorRoot)
        {
            if (s.faced) return;
            s.faced = true;
            if (Quaternion.Angle(s.transform.rotation, floorRoot.rotation) > 0.5f) return;   // the builder set one: keep it
            var p = s.transform.position;
            Vector3 dir = Vector3.zero;
            switch (s.kind)
            {
                case SpotKind.Window:
                    dir = p - floorRoot.position; break;
                case SpotKind.Bar:
                    var bar = Nearest<NeonBar>(floorRoot, p, 6f);
                    if (bar != null) dir = bar.transform.forward; break;
                case SpotKind.Dance:
                    var stage = Nearest<DJStage>(floorRoot, p, 15f);
                    if (stage != null) dir = stage.transform.forward; break;
                case SpotKind.Watch:
                case SpotKind.Table:
                    var game = Nearest<TableGame>(floorRoot, p, 5f);
                    if (game != null) dir = game.transform.position - p; break;
            }
            dir.y = 0;
            if (dir.sqrMagnitude > 1e-4f) s.transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

        static T Nearest<T>(Transform root, Vector3 p, float maxDist) where T : Component
        {
            T best = null; float bd = maxDist * maxDist;
            foreach (var c in root.GetComponentsInChildren<T>())
            {
                float d = (c.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        static Wealth WealthFor(int floor)
        {
            float r = Random.value;
            if (floor == 7) return r < 0.6f ? Wealth.HighRoller : Wealth.Regular;
            if (floor == 0) return r < 0.15f ? Wealth.HighRoller : r < 0.5f ? Wealth.Budget : Wealth.Regular;
            return r < 0.2f ? Wealth.HighRoller : r < 0.5f ? Wealth.Budget : Wealth.Regular;
        }

        static readonly string[] Crowd =
        {
            "BASS_BRO", "SYNTH.SOFI", "NEON_NACHO", "VAPOR_VAL", "KILO_KAT", "DROP_DIEGO", "LASER_LIA", "STROBE_SAM", "TEMPO_TOM", "RAVE_RIN",
            "ECHO_ELI", "PULSE_PAU", "WAVE_WEN", "GROOVE_GIL", "BEAT_BEA", "SUB_ZOE", "FLUX_FER", "NOVA_NIL", "DISCO_DANI", "VINYL_VIC"
        };
        // dances that stay on the spot (samba, floss, hype, robot): the griddy and the running man travel and walked
        // dancers through each other
        static readonly int[] StyleBag = { 0, 0, 2, 2, 3, 3, 5 };

        IEnumerator DanceCrowd(Transform floorRoot, List<BotSpot> spots)
        {
            var stage = floorRoot.GetComponentInChildren<DJStage>();
            if (stage == null) yield break;
            var st = stage.transform;
            int n = Mathf.Min(XRIn.HasHeadset && GraphicsQuality.I != null && GraphicsQuality.I.Current == GraphicsQuality.Preset.Performance ? lobbyDancers * 2 / 3 : lobbyDancers, Crowd.Length);
            var taken = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                // scattered over the whole LED floor, never closer than 1.7 m to another dancer
                Vector3 local = Vector3.zero;
                for (int tries = 0; tries < 20; tries++)
                {
                    local = new Vector3(Random.Range(-DJStage.FloorHalfWidth + 0.6f, DJStage.FloorHalfWidth - 0.6f), 0.04f, Random.Range(DJStage.FloorFront - 0.7f, DJStage.FloorFront - DJStage.FloorDepth + 0.6f));
                    bool ok = true; foreach (var t in taken) if ((t - new Vector2(local.x, local.z)).sqrMagnitude < 2.89f) { ok = false; break; }
                    if (ok) break;
                }
                taken.Add(new Vector2(local.x, local.z));
                var pos = st.TransformPoint(local);
                float yaw = st.eulerAngles.y + Random.Range(-35f, 35f);
                var r = (Rarity)(1 + (Mathf.Abs(Crowd[i].GetHashCode()) % 5));
                var body = Avatars.Create(Crowd[i], transform, pos, yaw, r, RoninRole.Player, Random.Range(1.6f, 1.86f));
                body.bpm = 124f;
                var bot = body.gameObject.AddComponent<BotAvatar>();
                bot.Init(this, i, spots, 0, BotJob.Guest, null, Random.value < 0.2f ? Wealth.HighRoller : Random.value < 0.4f ? Wealth.Budget : Wealth.Regular);
                bot.StartDancing(StyleBag[Random.Range(0, StyleBag.Length)]);
                var ap = body.GetComponent<AvatarProfile>();
                if (ap != null) { ap.displayName = Crowd[i]; ap.rarity = r; ap.xp = Odds.Range(800, 40000); ap.status = "ON THE DANCE FLOOR"; }
                bots.Add(bot);
                if (i % 3 == 2) yield return null;
            }
        }
        readonly List<GameObject> crowd = new List<GameObject>();

        static void Shuffle<T>(List<T> l)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = Odds.Range(0, i + 1); var t = l[i]; l[i] = l[j]; l[j] = t; }
        }

        void Bake(int floor)
        {
            if (navInstance.valid) NavMesh.RemoveNavMeshData(navInstance);
            var center = new Vector3(0, Tower.Y(floor), 0);
            var bounds = new Bounds(center + Vector3.up * 1.5f, new Vector3(Tower.FloorRadius * 2.2f, 5f, Tower.FloorRadius * 2.2f));
            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            sources.RemoveAll(s => s.component != null && (s.component.GetComponentInParent<BotAvatar>() != null || s.component.GetComponentInParent<Rig>() != null || (s.component is Collider && ((Collider)s.component).isTrigger)));
            AddBlockers(bounds, sources);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.3f; settings.agentHeight = 1.8f; settings.agentClimb = 0.2f; settings.agentSlope = 30f;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data != null) navInstance = NavMesh.AddNavMeshData(data);
        }

        /// <summary>Things people must walk around that have no solid collider on purpose (the player can lean over them):
        /// stools and chairs, the velvet-rope posts and ropes, the cashier's office chair. Each becomes a not-walkable box.</summary>
        static readonly string[] Blockers = { "Fence_07", "Fence_08", "Chair_Office_01" };
        static void AddBlockers(Bounds bounds, List<NavMeshBuildSource> sources)
        {
            foreach (var s in Object.FindObjectsByType<Seat>(FindObjectsSortMode.None))
            {
                var p = s.transform.position;
                if (!bounds.Contains(p + Vector3.up * 0.5f)) continue;
                float w = Mathf.Clamp(Mathf.Abs(s.back) + Mathf.Abs(s.front), 0.4f, 0.7f) * 0.8f;
                sources.Add(Box(p + Vector3.up * 0.5f, s.transform.rotation, new Vector3(w, 1f, w)));
            }
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (System.Array.IndexOf(Blockers, t.name) < 0) continue;
                var rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (!bounds.Intersects(b)) continue;
                var size = b.size; size.x = Mathf.Max(size.x, 0.12f); size.z = Mathf.Max(size.z, 0.12f); size.y = Mathf.Max(size.y, 1f);
                sources.Add(Box(b.center, Quaternion.identity, size));
            }
        }

        static NavMeshBuildSource Box(Vector3 c, Quaternion r, Vector3 size)
        {
            return new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box, size = size, area = 1,
                transform = Matrix4x4.TRS(c, r, Vector3.one)
            };
        }

        void Update()
        {
            if (bots.Count == 0) return;
            if (Time.time > nextChat)
            {
                nextChat = Time.time + Random.Range(6f, 12f);
                var b = bots[Odds.Range(0, bots.Count)];
                if (b != null && b.job != BotJob.Waiter && b.job != BotJob.Patrol) Say(b, Idle[Odds.Range(0, Idle.Length)]);
            }
            if (Time.time > nextEvent)
            {
                nextEvent = Time.time + Random.Range(15f, 28f);
                int i = Odds.Range(0, Roster.Count);
                Roster.Gain(i, Odds.Range(200, 1500));
                ChatWall.Post("FLOOR", string.Format(Events[Odds.Range(0, Events.Length)], Roster.Name(i)), Palette.Gold, false);
            }
        }

        public void Say(BotAvatar b, string msg)
        {
            b.Say(msg);
            // dance-floor regulars have their own names (rosterId is just their crowd index)
            var ap = b.GetComponent<AvatarProfile>();
            ChatWall.Post(ap != null && !string.IsNullOrEmpty(ap.displayName) ? ap.displayName : Roster.Name(b.rosterId), msg, Palette.Text, false);
        }

        public void Reply(string playerMsg)
        {
            if (bots.Count == 0) return;
            StartCoroutine(ReplyLater(playerMsg));
        }

        IEnumerator ReplyLater(string playerMsg)
        {
            yield return new WaitForSeconds(Random.Range(1.0f, 2.2f));
            var head = Rig.I != null ? Rig.I.Head.position : Vector3.zero;
            BotAvatar best = null; float bd = float.MaxValue;
            foreach (var b in bots) { if (b == null || b.job == BotJob.Waiter || b.job == BotJob.Patrol) continue; float d = (b.transform.position - head).sqrMagnitude; if (d < bd) { bd = d; best = b; } }
            if (best == null) yield break;
            string m = playerMsg.ToUpperInvariant();
            string reply = m.Contains("GG") ? "gg!" : m.Contains("RAID") ? "I'm in, meet on the roof" : m.Contains("PULL") ? "thanks!! that one was huge"
                         : m.Contains("SQUAD") ? "squad up, send the invite" : m.Contains("BREAK") ? "good call, enjoy the break" : m.Contains("LET") ? "LET'S GOOO" : "haha nice";
            best.Turn(head);
            best.Anim.Play(Emote.Wave, 1.5f);
            Say(best, reply);
        }
    }
}
