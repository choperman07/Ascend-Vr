using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>MAFIA MODE rules: 15 days, a quota per day, floors that open as the days go by, the rooftop raid as
    /// the final day, and the black-market modifiers. Pure data and maths; RunDirector runs the days.</summary>
    public static class Run
    {
        public const int Days = 15;
        public const long StartChips = 2500;
        public const float BaseDaySeconds = 300f;
        const string PrefKey = "asc_mafia_mode";

        /// <summary>Mafia mode is the main mode; the free casino is an option in Settings.</summary>
        public static bool Enabled { get { return PlayerPrefs.GetInt(PrefKey, 1) == 1; } set { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); } }
        /// <summary>A run is being played right now (chips are run chips: never saved, no deposits).</summary>
        public static bool Active;

        public static int Day = 1;
        public static long ExtraDebt;        // the cousin's loans come back on tomorrow's quota
        public static float BonusSeconds;    // energy drinks for today
        public static long BestDay;          // stats for the game-over screen
        public static int RunsLost;

        // ------------------------------------------------------------------ quota and floors
        public static bool FinalDay { get { return Day >= Days; } }

        // Quota rules from Roberto's "Gamble With Your Friends" rebuild (MECHANICS.md): Don Tuchi only has to SEE the
        // chips at the end of the day (he does not take them), and tomorrow's quota catches up with what you have:
        //     next = round2((quota + (chips - quota) × 0.75) × Multiplier(day))
        // so a good day makes the next one harder. The first quota is the starting chips × 1.2 (2,500 → 3,000).
        public const long FirstQuota = 3000;
        public const double CatchUp = 0.75;
        static readonly double[] Multipliers = { 1.2, 1.3, 1.5, 1.7, 1.9, 2.1, 2.3, 2.6, 3.0, 3.3, 3.6, 4.0 };

        /// <summary>Today's quota before any of the cousin's loans (set at the end of each day).</summary>
        public static long BaseQuota = FirstQuota;
        public static long Quota { get { return FinalDay ? 0 : BaseQuota + ExtraDebt; } }
        /// <summary>Prices scale with the stakes of the day.</summary>
        public static long PriceBase { get { return Math.Max(FirstQuota, BaseQuota); } }

        /// <summary>Multiplier applied after day <paramref name="day"/> (1..13). The original climbs through its table
        /// in 11 steps (quotas 2..12); here there are 13 nights, so the table is stretched over them and softened so the
        /// whole climb ends where the original's does (its last quota ≈ 11,000 × the first).</summary>
        public static double Multiplier(int day)
        {
            int last = Days - 2;                                      // 13 transitions: after day 1 .. after day 13
            double t = 1.0 + (Mathf.Clamp(day, 1, last) - 1) * 10.0 / (last - 1);   // 1 .. 11 in the table
            int i = Math.Min((int)t, Multipliers.Length - 1);
            double f = t - i;
            double m = i >= Multipliers.Length - 1 ? Multipliers[i] : Multipliers[i] + (Multipliers[i + 1] - Multipliers[i]) * f;
            return Math.Pow(m, Soften);
        }
        const double Soften = 0.845;

        /// <summary>Rounds to the first two significant digits (12,345 → 12,000), half away from zero.</summary>
        public static long Round2(double v)
        {
            if (v <= 0) return 0;
            int digits = (int)Math.Floor(Math.Log10(v)) + 1;
            if (digits <= 2) return (long)Math.Round(v, MidpointRounding.AwayFromZero);
            double p = Math.Pow(10, digits - 2);
            return (long)(Math.Round(v / p, MidpointRounding.AwayFromZero) * p);
        }

        /// <summary>The quota for the day after day <paramref name="day"/>, given the quota met and the chips held.</summary>
        public static long NextQuota(int day, long quota, long chips)
        {
            double caught = quota + (chips - quota) * CatchUp;
            return Math.Max(FirstQuota, Round2(caught * Multiplier(day)));
        }

        public static float DaySeconds { get { return BaseDaySeconds + 30f * Stacks("reloj") + BonusSeconds + (FinalDay ? 60f : 0f); } }

        /// <summary>Day on which each floor opens. 8 (the roof) only on the last day, and then nothing else.</summary>
        static readonly int[] Opens = { 1, 1, 3, 5, 7, 9, 11, 13, Days };
        public static int OpensOn(int floor) { return floor >= 0 && floor < Opens.Length ? Opens[floor] : Days; }
        public static bool FloorOpen(int floor)
        {
            if (FinalDay) return floor == Tower.Count - 1;
            return floor < Tower.Count - 1 && Day >= OpensOn(floor);
        }
        public static int StartFloor { get { return FinalDay ? Tower.Count - 1 : 0; } }

        // ------------------------------------------------------------------ modifiers
        public enum Kind { Day, Run, Charge, Instant }
        public class Mod
        {
            public string id, name, desc; public Kind kind; public float price; public int max; public Color color;
            public Mod(string id, string name, string desc, Kind k, float price, int max, Color c) { this.id = id; this.name = name; this.desc = desc; kind = k; this.price = price; this.max = max; color = c; }
        }

        public static readonly Mod[] Catalog =
        {
            new Mod("trebol", "TRÉBOL OXIDADO", "Hoy todas tus ganancias pagan +25 %.", Kind.Day, 0.22f, 1, Palette.Mint),
            new Mod("gafas", "GAFAS DE LA SUERTE", "+10 % en todas las ganancias. Para toda la partida. Se acumulan (máx. 3).", Kind.Run, 0.30f, 3, Palette.Teal),
            new Mod("moneda", "MONEDA TRUCADA", "Tu próxima victoria paga el TRIPLE.", Kind.Charge, 0.18f, 3, Palette.Gold),
            new Mod("dado", "DADO CARGADO", "Tus próximas 5 victorias pagan +50 %.", Kind.Charge, 0.26f, 1, Palette.Coral),
            new Mod("racha", "RACHA CALIENTE", "Hoy cada victoria seguida suma +12 % (hasta +60 %).", Kind.Day, 0.20f, 1, Palette.Red),
            new Mod("cunado", "AMULETO DEL CUÑADO", "Hoy ganas +35 %... pero a veces tu cuñado te pide un 'préstamo'.", Kind.Day, 0.14f, 1, Palette.Violet),
            new Mod("seguro", "SEGURO DUDOSO", "Al acabar el día te devuelve el 30 % de lo que hayas perdido hoy.", Kind.Day, 0.20f, 1, Palette.Silver),
            new Mod("bebida", "BEBIDA ENERGÉTICA", "+60 segundos de casino hoy.", Kind.Instant, 0.10f, 9, Palette.Lime),
            new Mod("reloj", "RELOJ ROBADO", "Todos los días duran 30 segundos más. Para toda la partida.", Kind.Run, 0.28f, 2, Palette.Gold),
            new Mod("abogado", "ABOGADO BARATO", "Si un día te quedas corto por menos de un 15 %, te libra. Una vez.", Kind.Charge, 0.35f, 1, Palette.Teal),
            new Mod("prestamo", "PRÉSTAMO DEL PRIMO", "+50 % de la cuota de hoy en fichas YA. Mañana la cuota sube el doble de lo prestado.", Kind.Instant, 0.0f, 1, Palette.Magenta),
            new Mod("todo", "TODO O NADA", "Cara: duplicas TODAS tus fichas. Cruz: te quedas a cero.", Kind.Instant, 0.0f, 1, Palette.Red),
        };
        public static Mod Get(string id) { foreach (var m in Catalog) if (m.id == id) return m; return null; }

        static readonly Dictionary<string, int> runStacks = new Dictionary<string, int>();
        static readonly HashSet<string> dayMods = new HashSet<string>();
        static readonly Dictionary<string, int> charges = new Dictionary<string, int>();
        public static int Stacks(string id) { int n; return runStacks.TryGetValue(id, out n) ? n : 0; }
        public static bool Today(string id) { return dayMods.Contains(id); }
        public static int Charges(string id) { int n; return charges.TryGetValue(id, out n) ? n : 0; }
        static void UseCharge(string id) { int n = Charges(id); if (n > 0) charges[id] = n - 1; }

        public static long PriceOf(Mod m)
        {
            if (m.id == "prestamo" || m.id == "todo") return 0;
            return Math.Max(250, (long)(Math.Round(PriceBase * m.price / 50.0) * 50.0));
        }

        /// <summary>Owned and still useful (so the shop can show "SOLD OUT" / "MAX").</summary>
        public static bool Maxed(Mod m)
        {
            switch (m.kind)
            {
                case Kind.Day: return Today(m.id);
                case Kind.Run: return Stacks(m.id) >= m.max;
                case Kind.Charge: return Charges(m.id) >= (m.id == "dado" ? 5 : m.max);
                default: return m.id == "prestamo" && loanToday;
            }
        }
        static bool loanToday;

        /// <summary>Applies a bought modifier (instant ones are resolved by the caller: coin flip, loan, timer).</summary>
        public static void Grant(Mod m)
        {
            switch (m.kind)
            {
                case Kind.Day: dayMods.Add(m.id); break;
                case Kind.Run: runStacks[m.id] = Stacks(m.id) + 1; break;
                case Kind.Charge: charges[m.id] = Charges(m.id) + (m.id == "dado" ? 5 : 1); break;
            }
            if (m.id == "prestamo") loanToday = true;
        }

        public static IEnumerable<string> OwnedLabels()
        {
            foreach (var kv in runStacks) if (kv.Value > 0) yield return Get(kv.Key).name + (kv.Value > 1 ? " ×" + kv.Value : "");
            foreach (var id in dayMods) yield return Get(id).name + " (HOY)";
            foreach (var kv in charges) if (kv.Value > 0) yield return Get(kv.Key).name + " · " + kv.Value + (kv.Value == 1 ? " USO" : " USOS");
        }

        /// <summary>Every win goes through here (GameState.Pay): the modifiers multiply the profit, never the stake.</summary>
        public static long AdjustWin(long amount, long stake)
        {
            long profit = amount - stake;
            if (!Active || profit <= 0) return amount;
            float m = 1f + 0.10f * Stacks("gafas");
            if (Today("trebol")) m += 0.25f;
            if (Today("racha")) m += 0.12f * Mathf.Min(GameState.I.winStreak, 5);
            if (Today("cunado")) m += 0.35f;
            string note = null;
            if (Charges("moneda") > 0) { m += 2f; UseCharge("moneda"); note = "MONEDA TRUCADA · ¡TRIPLE!"; }
            if (Charges("dado") > 0) { m += 0.5f; UseCharge("dado"); if (note == null) note = "DADO CARGADO · +50 % (" + Charges("dado") + " MÁS)"; }
            double p = profit * (double)m;
            if (Today("cunado") && UnityEngine.Random.value < 0.18f)
            {
                long take = (long)(p * 0.15);
                p -= take;
                GameState.I.Notify("TU CUÑADO: \"¿ME DEJAS " + take.ToString("N0") + "? TE LO DEVUELVO EL LUNES\"", Palette.Violet);
            }
            if (note != null) GameState.I.Notify(note, Palette.Gold);
            return stake + (long)p;
        }

        // ------------------------------------------------------------------ lifecycle
        public static void Reset()
        {
            Day = 1; ExtraDebt = 0; BonusSeconds = 0; loanToday = false; BaseQuota = FirstQuota;
            runStacks.Clear(); dayMods.Clear(); charges.Clear();
        }

        /// <summary>Called after Don Tuchi takes his cut: tomorrow begins.</summary>
        public static void NextDay(long loanBack, long chipsAtEnd)
        {
            BaseQuota = NextQuota(Day, BaseQuota, chipsAtEnd);
            Day++;
            ExtraDebt = loanBack;
            BonusSeconds = 0; loanToday = false;
            dayMods.Clear();
        }

        public static bool UseLawyer() { if (Charges("abogado") <= 0) return false; UseCharge("abogado"); return true; }

        public static string Clock(float s) { s = Mathf.Max(0, s); int m = (int)(s / 60f); int sec = (int)(s % 60f); return m.ToString("0") + ":" + sec.ToString("00"); }
        /// <summary>Spanish thousands separator (12.500), without depending on the device's cultures.</summary>
        public static string N(long v) { return v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", "."); }
    }
}
