using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    public enum Tier { Rookie = 0, Contender = 1, Elite = 2, Legend = 3, Mythic = 4 }
    public enum Rarity { None = 0, Common = 1, Rare = 2, Epic = 3, Legendary = 4, Mythic = 5 }

    /// <summary>Player progression, wallet (fictional PLAY CHIPS), stats, achievements and settings.
    /// Chip flow: TakeChips (chips go to your hand) → CommitBet (the round starts: stats, XP, limits) → Pay (winnings).</summary>
    public class GameState : MonoBehaviour
    {
        static GameState _i;
        static bool _quitting;
#if UNITY_WEBGL && !UNITY_EDITOR
        // Browser build: every visit starts a brand-new game (and a new anonymous online player),
        // so people opening the same link never inherit someone else's chips, day or missions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void FreshWebVisit() { PlayerPrefs.DeleteAll(); PlayerPrefs.Save(); }
#endif
        public static bool Exists { get { return _i != null; } }
        public static GameState I
        {
            get
            {
                if (_i == null && !_quitting)
                {
                    _i = FindFirstObjectByType<GameState>();
                    if (_i == null) _i = new GameObject("GameState").AddComponent<GameState>();
                }
                return _i;
            }
        }

        public static readonly long[] TierXP = { 0, 5000, 25000, 100000, 250000 };
        public static readonly string[] TierNames = { "ROOKIE", "CONTENDER", "ELITE", "LEGEND", "MYTHIC" };
        public const long WagerXPDailyCap = 2000;   // Fair Climb Rule
        public const long StreakXP = 150;
        public const long RaidWinXP = 1000;

        [Header("Player")]
        public string playerName = "NEON RONIN #0042";
        public long chips = 50000;
        public long xp = 68400;
        public Rarity nft = Rarity.Legendary;
        [Tooltip("Demo mode: every floor is open regardless of tier (for presentations).")]
        public bool demoUnlockAll = true;

        [Header("Responsible play")]
        public long sessionLossLimit = 0;   // 0 = off (optional responsible-play tool in Settings)
        public float realityCheckMinutes = 20f;

        [Header("Security")]
        [Tooltip("Net session winnings that make casino security escort you out.")]
        public long securityWinThreshold = 150000;

        [Header("Settings")]
        public float musicVolume = 0.6f;
        public float sfxVolume = 1f;
        public bool snapTurn = true;
        public bool voiceChat = true;

        [NonSerialized] public long sessionNet, sessionWagered, sessionWon, biggestWin, wins, rounds;
        [NonSerialized] public long wagerXPToday;
        [NonSerialized] public float sessionStart;
        [NonSerialized] public float breakUntil = -1f, bannedUntil = -1f;
        [NonSerialized] public int bonusCrates;
        [NonSerialized] public int[] owned = new int[6];
        [NonSerialized] public int winStreak;
        [NonSerialized] public long jackpot = JackpotSeed;
        public const long JackpotSeed = 250000;
        public const float JackpotContribution = 0.01f;   // 1% of every bet feeds THE HOUSE JACKPOT
        readonly HashSet<string> achievements = new HashSet<string>();
        readonly Dictionary<string, long> stats = new Dictionary<string, long>();
        public readonly List<Mission> missions = new List<Mission>();
        bool dailyClaimed, streakPending;

        public event Action Changed;
        public event Action<string, Color> Toast;
        /// <summary>amount paid, multiplier of the stake, world position of the table (for celebrations).</summary>
        public event Action<long, float, Vector3> Won;
        public event Action<Tier> TierUp;

        void Awake()
        {
            if (_i != null && _i != this) { Destroy(gameObject); return; }
            _i = this;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            Load();
            sessionStart = Time.time;
        }

        void Start()
        {
            if (streakPending) { streakPending = false; Notify("DAILY STREAK", Palette.Teal); AddXP(StreakXP); }
        }

        // PlayerPrefs.Set* only writes to memory: flush to disk when Quest suspends the app (it may never get a quit)
        void OnApplicationPause(bool p) { if (p) { Save(); PlayerPrefs.Save(); } }
        void OnApplicationQuit() { _quitting = true; Save(); PlayerPrefs.Save(); }

        // ------------------------------------------------------------ tiers
        public Tier CurrentTier
        {
            get
            {
                for (int t = TierXP.Length - 1; t >= 0; t--) if (xp >= TierXP[t]) return (Tier)t;
                return Tier.Rookie;
            }
        }
        public long NextTierXP { get { int t = (int)CurrentTier; return t >= 4 ? TierXP[4] : TierXP[t + 1]; } }
        public float TierProgress
        {
            get
            {
                int t = (int)CurrentTier; if (t >= 4) return 1f;
                return Mathf.Clamp01((xp - TierXP[t]) / (float)(TierXP[t + 1] - TierXP[t]));
            }
        }
        public bool CanAccess(Tier needed) { return demoUnlockAll || CurrentTier >= needed; }

        public static float BoostFor(Rarity r)
        {
            switch (r) { case Rarity.Rare: return 1.05f; case Rarity.Epic: return 1.10f; case Rarity.Legendary: case Rarity.Mythic: return 1.15f; default: return 1f; }
        }
        public float NftBoost { get { return BoostFor(nft); } }

        // ------------------------------------------------------------ incentives (XP and crates only — never better odds)
        /// <summary>LUCKY HOUR: the first 4 minutes of every 20 (real clock) give double XP on everything.</summary>
        public static bool LuckyHour { get { return System.DateTime.Now.Minute % 20 < 4; } }
        public const int LoyaltyRounds = 25;
        bool luckyShown;
        void Update()
        {
            if (Time.frameCount % 36 != 0) return;   // the wall clock twice a second is plenty (DateTime.Now isn't free)
            bool lh = LuckyHour;
            if (lh && !luckyShown) { luckyShown = true; Notify("LUCKY HOUR · DOUBLE XP FOR 4 MINUTES", Palette.Gold); Sfx.UI("streak", 0.7f); }
            else if (!lh) luckyShown = false;
        }

        public void SetDemo(bool on)
        {
            demoUnlockAll = on; Fire();
            Notify(on ? "DEMO MODE · ALL FLOORS OPEN" : "DEMO OFF · FLOORS FOLLOW YOUR TIER", Palette.Violet);
        }

        // ------------------------------------------------------------ wallet
        public bool OnBreak { get { return Time.time < breakUntil; } }
        public bool Banned { get { return Time.time < bannedUntil; } }

        /// <summary>Chips leave the wallet and go to your hand / the table.</summary>
        public bool TakeChips(long amount)
        {
            if (amount <= 0) return false;
            if (amount > chips) { Notify("NOT ENOUGH CHIPS · OPEN THE MENU TO DEPOSIT", Palette.Red); Sfx.UI("ui_error"); return false; }
            chips -= amount; Fire();
            return true;
        }

        /// <summary>Unused chips come back to the wallet.</summary>
        public void ReturnChips(long amount)
        {
            if (amount <= 0) return;
            chips += amount; Fire();
        }

        /// <summary>A round starts with this stake. Checks breaks, bans and the loss limit; counts XP (Fair Climb Rule).</summary>
        public bool CommitBet(long amount)
        {
            if (amount <= 0) return false;
            if (OnBreak) { Notify("ON A BREAK · BACK SOON", Palette.Gold); return false; }
            if (Banned) { Notify("SECURITY: TABLES CLOSED FOR YOU · " + Mathf.CeilToInt(bannedUntil - Time.time) + " S", Palette.Red); return false; }
            if (sessionLossLimit > 0 && -sessionNet + amount > sessionLossLimit) { Notify("SESSION LOSS LIMIT REACHED · TAKE A BREAK", Palette.Gold); return false; }
            sessionNet -= amount; sessionWagered += amount; rounds++;
            if (rounds % LoyaltyRounds == 0) { bonusCrates++; Notify("LOYALTY · " + LoyaltyRounds + " ROUNDS PLAYED · FREE CRATE IN THE MENU", Palette.Mint); }
            jackpot += (long)(amount * JackpotContribution);
            Record("rounds"); Record("wagered", amount);
            long px = amount / 10;
            long allowed = Math.Max(0, Math.Min(px, WagerXPDailyCap - wagerXPToday));
            wagerXPToday += allowed;
            AddXP(allowed, false);
            Save(); Fire();
            return true;
        }

        /// <summary>Takes from the wallet and commits in one step (button-based games).</summary>
        public bool Wager(long amount)
        {
            if (amount > chips) { Notify("NOT ENOUGH CHIPS · OPEN THE MENU TO DEPOSIT", Palette.Red); Sfx.UI("ui_error"); return false; }
            if (!CommitBet(amount)) return false;
            chips -= amount; Fire();
            return true;
        }

        /// <summary>Winnings (stake included) go back to the wallet. stake is used to compute the multiplier for celebrations.</summary>
        public void Pay(long amount, long stake, Vector3 where)
        {
            if (amount <= 0) { winStreak = 0; return; }
            amount = Run.AdjustWin(amount, stake);   // mafia-mode modifiers (profit only)
            chips += amount; sessionNet += amount;
            if (amount > stake)
            {
                sessionWon += amount - stake; wins++; winStreak++;
                if (amount > biggestWin) biggestWin = amount;
            }
            else winStreak = 0;
            Save(); Fire();
            float mult = stake > 0 ? amount / (float)stake : 1f;
            if (Won != null && amount > stake) Won(amount, mult, where);
            if (amount > stake) Record("wins");
            if (amount > stake) AddXP(20 + Math.Min(winStreak, 10) * 10 + (long)Mathf.Min(500f, (mult - 1f) * 15f), winStreak >= 2);
            if (winStreak == 3) Unlock("HOT STREAK", "3 WINS IN A ROW", 200);
            if (winStreak == 5) Unlock("ON FIRE", "5 WINS IN A ROW", 500);
            if (mult >= 25f) Unlock("BIG FISH", "A 25x WIN", 500);
        }
        public void Pay(long amount) { Pay(amount, amount, Vector3.zero); }

        /// <summary>Free chips (crates, demo deposits, rewards). Not counted as winnings.</summary>
        public void Grant(long amount, string why)
        {
            if (Run.Active) { Notify("MODO MAFIA: AQUÍ NO HAY DEPÓSITOS NI REGALOS · SOLO LO QUE GANES", Palette.Red); Sfx.UI("ui_error"); return; }
            chips += amount; Save(); Fire();
            Notify("+" + amount.ToString("N0") + " PLAY CHIPS · " + why, Palette.Mint);
        }

        public void Withdraw(long amount)
        {
            amount = Math.Min(amount, chips);
            if (amount <= 0) return;
            chips -= amount; Save(); Fire();
            Notify("CASHED OUT " + amount.ToString("N0") + " (DEMO)", Palette.Silver);
        }

        public void AddXP(long amount, bool announce = true)
        {
            if (amount <= 0) return;
            Tier before = CurrentTier;
            long boosted = (long)Mathf.Round(amount * NftBoost * (LuckyHour ? 2f : 1f));
            xp += boosted;
            if (announce) Notify("+" + boosted.ToString("N0") + " XP", Palette.Teal);
            if (CurrentTier != before)
            {
                bonusCrates++;
                Notify("TIER UP · " + TierNames[(int)CurrentTier] + " · BONUS CRATE", Palette.TierColor(CurrentTier));
                if (TierUp != null) TierUp(CurrentTier);
            }
            Save(); Fire();
        }

        // ------------------------------------------------------------ NFTs
        public bool Owns(Rarity r) { return r != Rarity.None && owned[(int)r] > 0; }
        public void AddNft(Rarity r) { if (r == Rarity.None) return; owned[(int)r]++; Save(); Fire(); }

        public void SetNft(Rarity r)
        {
            nft = r; Save(); Fire();
            Notify("AVATAR SET · " + r.ToString().ToUpperInvariant() + (BoostFor(r) > 1f ? " · +" + Mathf.RoundToInt((BoostFor(r) - 1f) * 100) + "% XP" : ""), Palette.RarityColor(r));
        }

        // ------------------------------------------------------------ crates (earned, never bought)
        public int CratesReady { get { return (dailyClaimed && !demoUnlockAll ? 0 : 1) + bonusCrates; } }
        public bool TakeCrate()
        {
            if (bonusCrates > 0) { bonusCrates--; Save(); Fire(); return true; }
            if (!dailyClaimed || demoUnlockAll)
            {
                dailyClaimed = true;
                PlayerPrefs.SetString("asc_crate_day", Today); Save(); Fire(); return true;
            }
            return false;
        }
        public void GiveCrate(string why) { bonusCrates++; Save(); Fire(); Notify("CRATE EARNED · " + why, Palette.Gold); }

        /// <summary>Pays THE HOUSE JACKPOT (progressive pool fed by 1% of every bet) and reseeds it.</summary>
        public long WinJackpot(Vector3 where)
        {
            long amount = jackpot;
            jackpot = JackpotSeed;
            chips += amount; sessionNet += amount; sessionWon += amount; wins++;
            if (amount > biggestWin) biggestWin = amount;
            Save(); Fire();
            if (Won != null) Won(amount, 1000f, where);
            Unlock("BEAT THE HOUSE", "WON THE HOUSE JACKPOT", 5000);
            return amount;
        }

        /// <summary>Pays a share of THE HOUSE JACKPOT (raid entry below the full-share stake) and keeps the rest in the pool.</summary>
        public long WinJackpotShare(float share, long stake, Vector3 where)
        {
            share = Mathf.Clamp01(share);
            if (share >= 0.999f) return WinJackpot(where);
            long amount = (long)(jackpot * (double)share);
            jackpot = Math.Max(JackpotSeed, jackpot - amount);
            chips += amount; sessionNet += amount; sessionWon += amount; wins++;
            if (amount > biggestWin) biggestWin = amount;
            Save(); Fire();
            if (Won != null) Won(amount, stake > 0 ? amount / (float)stake : 100f, where);
            Unlock("BEAT THE HOUSE", "WON THE HOUSE JACKPOT", 5000);
            return amount;
        }

        // ------------------------------------------------------------ missions (daily) and stats
        public long Stat(string key) { long v; return stats.TryGetValue(key, out v) ? v : 0; }
        public void Record(string key, long amount = 1)
        {
            stats[key] = Stat(key) + amount;
            foreach (var m in missions)
            {
                if (m.done || m.stat != key) continue;
                m.progress = Math.Min(m.target, m.progress + amount);
                if (m.progress >= m.target)
                {
                    m.done = true;
                    Notify("MISSION COMPLETE · " + m.title + (Run.Active ? "" : " · +" + m.rewardChips.ToString("N0") + " CHIPS"), Palette.Mint);
                    Sfx.UI("mission_complete");
                    Voice.Announce("ann_mission");
                    if (!Run.Active) chips += m.rewardChips;   // mafia mode: chips only come from playing
                    AddXP(m.rewardXP);
                    if (m.crate) bonusCrates++;
                }
            }
            SaveMissions();
        }

        static readonly Mission[] MissionPool =
        {
            new Mission("tables","PLAY 10 ROUNDS ON ANY TABLE","rounds",10,300,2500,false),
            new Mission("winner","WIN 5 ROUNDS","wins",5,400,3000,false),
            new Mission("roulette","SPIN THE WHEEL 5 TIMES","roulette",5,250,2000,false),
            new Mission("blackjack","PLAY 5 HANDS OF BLACKJACK","blackjack",5,250,2000,false),
            new Mission("battle","WIN A CASE BATTLE","battle_won",1,500,5000,true),
            new Mission("raid","DEFEAT THE HOUSE ON THE ROOF","raid_cleared",1,1000,10000,true),
            new Mission("fish","CATCH 3 FISH","fish",3,250,2000,false),
            new Mission("crate","OPEN 2 CRATES","crates",2,300,0,false),
            new Mission("social","SAY HI WITH QUICK CHAT 3 TIMES","chat",3,150,1000,false),
            new Mission("dance","DANCE AT THE DJ STAGE FOR 30 SECONDS","dance",30,200,1500,false),
            new Mission("slots","PULL THE SLOT LEVER 10 TIMES","slots",10,250,2000,false),
            new Mission("plinko","DROP 10 PLINKO BALLS","plinko",10,250,2000,false),
        };

        string missionDay;
        void PickMissions()
        {
            missions.Clear();
            missionDay = Today;
            int seed = int.Parse(missionDay) % 997;
            var rnd = new System.Random(seed);
            var pool = new List<Mission>(MissionPool);
            for (int i = 0; i < 3 && pool.Count > 0; i++)
            {
                int k = rnd.Next(pool.Count);
                missions.Add(pool[k].Clone()); pool.RemoveAt(k);
            }
            var saved = PlayerPrefs.GetString("asc2_mis_" + missionDay, "");
            var parts = saved.Split('|');
            for (int i = 0; i < missions.Count && i < parts.Length; i++)
            {
                long p; if (long.TryParse(parts[i], out p)) { missions[i].progress = p; missions[i].done = p >= missions[i].target; }
            }
        }

        void SaveMissions()
        {
            var arr = new string[missions.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = missions[i].progress.ToString();
            // the key of the day these missions were picked for (a session running past midnight must not write
            // yesterday's progress onto today's, different, missions)
            PlayerPrefs.SetString("asc2_mis_" + (missionDay ?? Today), string.Join("|", arr));
        }

        // ------------------------------------------------------------ achievements
        public bool Has(string id) { return achievements.Contains(id); }
        public IEnumerable<string> Achievements { get { return achievements; } }
        public void Unlock(string id, string desc, long xpReward)
        {
            if (achievements.Contains(id)) return;
            achievements.Add(id);
            Notify("ACHIEVEMENT · " + id + " · " + desc, Palette.Gold);
            Sfx.UI("achievement");
            AddXP(xpReward);
            Save();
        }

        // ------------------------------------------------------------ responsible play / security
        public void SetLossLimit(long v) { sessionLossLimit = v < 10000 ? 0 : v; Fire(); }
        public void TakeBreak(float seconds)
        {
            breakUntil = Time.time + seconds; Fire();
            Notify("BREAK STARTED · GAMES PAUSED", Palette.Gold);
        }
        public void Ban(float seconds)
        {
            bannedUntil = Time.time + seconds;
            sessionNet = 0;   // a fresh start after being escorted out
            Fire();
        }

        public void Notify(string msg, Color c) { if (Toast != null) Toast(msg, c); }
        void Fire() { if (Changed != null) Changed(); }

        // ------------------------------------------------------------ persistence
        static string Today { get { return DateTime.Now.ToString("yyyyMMdd"); } }

        void Load()
        {
            long v;
            // corrupted / hand-edited prefs must never give a negative wallet, an out-of-range enum or NaN volumes
            if (long.TryParse(PlayerPrefs.GetString("asc2_chips", ""), out v)) chips = Math.Max(0, v);
            if (long.TryParse(PlayerPrefs.GetString("asc2_xp", ""), out v)) xp = Math.Max(0, v);
            if (long.TryParse(PlayerPrefs.GetString("asc2_best", ""), out v)) biggestWin = Math.Max(0, v);
            int nv = PlayerPrefs.GetInt("asc2_nft", (int)nft);
            nft = nv >= (int)Rarity.None && nv <= (int)Rarity.Mythic ? (Rarity)nv : Rarity.Common;
            bonusCrates = Mathf.Max(0, PlayerPrefs.GetInt("asc2_bonus", 0));
            musicVolume = Clamp01OrDefault(PlayerPrefs.GetFloat("asc2_music", musicVolume), 0.6f);
            sfxVolume = Clamp01OrDefault(PlayerPrefs.GetFloat("asc2_sfx", sfxVolume), 1f);
            snapTurn = PlayerPrefs.GetInt("asc2_snap", snapTurn ? 1 : 0) == 1;
            voiceChat = PlayerPrefs.GetInt("asc2_voice", voiceChat ? 1 : 0) == 1;
            string own = PlayerPrefs.GetString("asc2_own", "0,1,0,0,1,0");
            var parts = own.Split(',');
            for (int i = 0; i < owned.Length && i < parts.Length; i++) { int n; int.TryParse(parts[i], out n); owned[i] = Mathf.Max(0, n); }
            foreach (var a in PlayerPrefs.GetString("asc2_ach", "").Split('|')) if (a.Length > 0) achievements.Add(a);
            string day = Today;
            if (PlayerPrefs.GetString("asc2_day", "") == day) wagerXPToday = Math.Max(0, PlayerPrefs.GetInt("asc2_wxp", 0));
            else streakPending = true;
            PlayerPrefs.SetString("asc2_day", day);
            dailyClaimed = PlayerPrefs.GetString("asc_crate_day", "") == day;
            if (long.TryParse(PlayerPrefs.GetString("asc2_jp", ""), out v)) jackpot = Math.Max(v, JackpotSeed);
            PickMissions();
        }

        public void Save()
        {
            if (!Run.Active) PlayerPrefs.SetString("asc2_chips", chips.ToString());   // run chips are never saved over the free casino's
            PlayerPrefs.SetString("asc2_xp", xp.ToString());
            PlayerPrefs.SetString("asc2_best", biggestWin.ToString());
            PlayerPrefs.SetInt("asc2_nft", (int)nft);
            PlayerPrefs.SetInt("asc2_wxp", (int)wagerXPToday);
            PlayerPrefs.SetInt("asc2_bonus", bonusCrates);
            PlayerPrefs.SetFloat("asc2_music", musicVolume);
            PlayerPrefs.SetFloat("asc2_sfx", sfxVolume);
            PlayerPrefs.SetInt("asc2_snap", snapTurn ? 1 : 0);
            PlayerPrefs.SetInt("asc2_voice", voiceChat ? 1 : 0);
            PlayerPrefs.SetString("asc2_own", string.Join(",", Array.ConvertAll(owned, x => x.ToString())));
            PlayerPrefs.SetString("asc2_ach", string.Join("|", new List<string>(achievements).ToArray()));
            PlayerPrefs.SetString("asc2_jp", jackpot.ToString());
        }

        static float Clamp01OrDefault(float f, float def) { return float.IsNaN(f) || float.IsInfinity(f) ? def : Mathf.Clamp01(f); }

        public void SettingsChanged() { Save(); Fire(); }

        [ContextMenu("Reset progress")]
        public void ResetProgress()
        {
            chips = 50000; xp = 68400; nft = Rarity.Legendary; wagerXPToday = 0; sessionNet = 0; sessionWagered = 0; sessionWon = 0;
            bonusCrates = 0; biggestWin = 0; wins = 0; rounds = 0; winStreak = 0;
            owned = new int[] { 0, 1, 0, 0, 1, 0 };
            achievements.Clear();
            dailyClaimed = false; PlayerPrefs.DeleteKey("asc_crate_day");
            jackpot = JackpotSeed; stats.Clear(); PlayerPrefs.DeleteKey("asc2_mis_" + Today); PickMissions();
            Save(); Fire();
        }
    }

    [Serializable]
    public class Mission
    {
        public string id, title, stat; public long target, progress, rewardXP, rewardChips; public bool crate, done;
        public Mission(string id, string title, string stat, long target, long xp, long chips, bool crate)
        { this.id = id; this.title = title; this.stat = stat; this.target = target; rewardXP = xp; rewardChips = chips; this.crate = crate; }
        public Mission Clone() { return new Mission(id, title, stat, target, rewardXP, rewardChips, crate); }
    }

    /// <summary>All odds in one place, shown to the player before every game.</summary>
    public static class Odds
    {
        static readonly System.Random _rng = new System.Random();
        public static double R() { lock (_rng) return _rng.NextDouble(); }
        public static int Range(int min, int maxExclusive) { lock (_rng) return _rng.Next(min, maxExclusive); }

        // Crate: Common, Rare, Epic, Legendary, Mythic (percent)
        public static readonly float[] CrateBase = { 62f, 25f, 9f, 3.5f, 0.5f };
        public static float[] Crate(Tier t)
        {
            var o = (float[])CrateBase.Clone();
            if (t >= Tier.Contender) o[1] *= 2f;
            if (t >= Tier.Elite) o[2] *= 2f;
            if (t >= Tier.Legend) o[3] *= 2f;
            float sum = 0; for (int i = 0; i < o.Length; i++) sum += o[i];
            o[0] -= sum - 100f;
            return o;
        }
        public static Rarity RollRarity(float[] pct)
        {
            double r = R() * 100.0, acc = 0;
            for (int i = 0; i < pct.Length; i++) { acc += pct[i]; if (r < acc) return (Rarity)(i + 1); }
            return Rarity.Common;
        }
        public static long CrateChips(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return 100 * Range(1, 6);
                case Rarity.Rare: return 250 * Range(4, 11);
                case Rarity.Epic: return 5000;
                case Rarity.Legendary: return 25000;
                case Rarity.Mythic: return 100000;
            }
            return 0;
        }
        public static string CrateReward(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return "100–500 CHIPS · 1H XP BOOST";
                case Rarity.Rare: return "1,000–2,500 CHIPS · 10 FREE SPINS";
                case Rarity.Epic: return "5,000 CHIPS · ANIMATED FRAME";
                case Rarity.Legendary: return "25,000 CHIPS · RAID PASS";
                case Rarity.Mythic: return "100,000 CHIPS · 1/1 NFT TRAIT";
            }
            return "";
        }

        // Case battle multipliers (percent). Expected return = 0.985.
        public static readonly float[] BattleMult = { 0.5f, 1f, 1.5f, 2f, 3f, 5f, 10f, 100f };
        public static readonly double[] BattlePct = { 52, 28, 10, 6, 2.5, 1, 0.49999, 0.00001 };
        public static int RollBattle()
        {
            double r = R() * 100.0, acc = 0;
            for (int i = 0; i < BattlePct.Length; i++) { acc += BattlePct[i]; if (r < acc) return i; }
            return 0;
        }

        // Raid loot (rolled only after The House is defeated). Index 0 = THE HOUSE JACKPOT (progressive pool).
        public static readonly string[] RaidNames = { "THE HOUSE JACKPOT", "MEGA LOOT", "BIG LOOT", "LOOT", "STAKE BACK", "CONSOLATION" };
        public static readonly float[] RaidMult = { 0f, 15f, 5f, 2f, 1f, 0.3f };
        public static readonly double[] RaidPct = { 1.0, 1.0, 4.0, 14.0, 30.0, 50.0 };
        public static int RollRaid()
        {
            double r = R() * 100.0, acc = 0;
            for (int i = 0; i < RaidPct.Length; i++) { acc += RaidPct[i]; if (r < acc) return i; }
            return RaidPct.Length - 1;
        }

        public static string Pct(double p)
        {
            if (p >= 1) return p.ToString("0.##") + "%";
            if (p >= 0.01) return p.ToString("0.#####") + "%";
            return p.ToString("0.#########") + "%";
        }
    }
}
