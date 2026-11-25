using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ascend
{
    /// <summary>Multi-deck shoe. Card = 0..51, rank = card % 13 (0 = deuce ... 12 = ace), suit = card / 13.</summary>
    public class Shoe
    {
        readonly int decks;
        readonly List<int> cards = new List<int>();
        public Shoe(int decks) { this.decks = decks; Shuffle(); }
        public void Shuffle()
        {
            cards.Clear();
            for (int d = 0; d < decks; d++) for (int c = 0; c < 52; c++) cards.Add(c);
            for (int i = cards.Count - 1; i > 0; i--) { int j = Odds.Range(0, i + 1); int t = cards[i]; cards[i] = cards[j]; cards[j] = t; }
        }
        public int Draw() { if (cards.Count < 20) Shuffle(); int c = cards[cards.Count - 1]; cards.RemoveAt(cards.Count - 1); return c; }
        public static string RankName(int c) { return new[] { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" }[c % 13]; }
    }
}
