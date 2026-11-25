using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>The skyscraper: seven floors, each with its own game mode.</summary>
    public static class Tower
    {
        public const float FloorHeight = 8f;
        public const float FloorRadius = 22f;
        public struct FloorInfo
        {
            public string name, sub, voice, music; public Tier tier; public Color color;
            public FloorInfo(string n, string s, Tier t, Color c, string v, string m) { name = n; sub = s; tier = t; color = c; voice = v; music = m; }
        }
        public static readonly FloorInfo[] Floors =
        {
            new FloorInfo("LOBBY · MAIN HALL", "DJ STAGE · HIGH BAR · LOUNGE · SLOTS · NFT GALLERY · LEADERBOARD", Tier.Rookie, Palette.Violet, "ann_f1", Sfx.Background),
            new FloorInfo("THE CLASSICS", "ROULETTE · BLACKJACK · BACCARAT · HIGH-LIMIT TABLES", Tier.Rookie, Palette.Teal, "ann_f2", Sfx.Background),
            new FloorInfo("THE CARD ROOM", "CASINO HOLD'EM · THREE CARD POKER · DRAGON TIGER · VIDEO POKER", Tier.Rookie, Palette.Coral, "ann_fcards", Sfx.Background),
            new FloorInfo("DICE & WHEELS", "CRAPS · SIC BO · MONEY WHEEL · KENO", Tier.Rookie, Palette.Magenta, "ann_fdice", Sfx.Background),
            new FloorInfo("FAST ROUNDS", "CRASH · MINES · PLINKO · DICE · LIMBO · HI-LO · TOWER", Tier.Rookie, Palette.Mint, "ann_f3", Sfx.Background),
            new FloorInfo("LUCK & LOOT", "SLOT BANKS · CATCH A FISH · COINFLIP · MYSTERY BOX · CRATES", Tier.Rookie, Palette.Gold, "ann_f4", Sfx.Background),
            new FloorInfo("DERBY & BATTLES", "NEON DERBY RACES · CASE BATTLES UP TO 100x", Tier.Contender, Palette.Red, "ann_frace", Sfx.Background),
            new FloorInfo("SKY LOUNGE", "HIGH-ROLLER 21 · VIP TABLES · VIP HOST · THE INNER CIRCLE", Tier.Legend, Palette.Gold, "ann_f6", Sfx.Background),
            new FloorInfo("ROOFTOP · RAID", "YOUR SQUAD VS THE HOUSE · JACKPOT", Tier.Elite, Palette.Red, "ann_f7", Sfx.Background),
        };
        public static int Count { get { return Floors.Length; } }
        public static float Y(int floor) { return floor * FloorHeight; }
        /// <summary>Where the elevator lets you out on each floor (in front of the elevator core, facing south-west into the room).</summary>
        public static Vector3 Arrival(int floor) { return new Vector3(0, Y(floor), -5.2f); }
        public const float ArrivalYaw = 180f;

        public static int FloorAt(float y) { return Mathf.Clamp(Mathf.RoundToInt((y + 1f) / FloorHeight - 0.4f), 0, Floors.Length - 1); }
    }
}
