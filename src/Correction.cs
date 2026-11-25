using System;
using System.Collections.Generic;
using System.Linq;

namespace Tastatur
{
    internal struct Variant
    {
        public string Text;
        public int Cost;
        public Variant(string t, int c) { Text = t; Cost = c; }
    }

    /// <summary>
    /// Korrekturvorschläge bei Tippfehlern. Die Tastatur ersetzt nie selbst ein Wort, sie bietet die
    /// Korrektur nur in der Vorschlagsleiste an.
    /// </summary>
    internal static class Correction
    {
        /// <summary>Unter diesen Punkten (Häufigkeit minus Abzug) wird nichts vorgeschlagen, sonst kommt Unsinn.</summary>
        public const int MinScore = 300;

        // Abzüge je nach Art des Fehlers (Häufigkeiten liegen etwa zwischen 300 und 770)
        public const int CostUmlaut = 0;        // ae statt ä, ss statt ß
        public const int CostSwap = 10;         // zwei Buchstaben vertauscht
        public const int CostNeighbor = 25;     // Nachbartaste erwischt
        public const int CostDouble = 25;       // Buchstabe doppelt
        public const int CostMissing = 45;      // Buchstabe vergessen
        public const int CostExtra = 45;        // Buchstabe zu viel
        public const int CostOther = 90;        // irgendein anderer Buchstabe

        private const string Letters = "abcdefghijklmnopqrstuvwxyzäöüß"; // i18n-ok

        // QWERTZ, Reihen leicht versetzt wie auf der Tastatur
        private static readonly string[] Rows = { "qwertzuiopü", "asdfghjklöä", "yxcvbnm" }; // i18n-ok
        private static readonly float[] Offsets = { 0f, 0.25f, 0.75f };
        private static readonly Dictionary<char, string> Neighbors = BuildNeighbors();

        private static Dictionary<char, string> BuildNeighbors()
        {
            var pos = new Dictionary<char, Tuple<int, float>>();
            for (int r = 0; r < Rows.Length; r++)
                for (int i = 0; i < Rows[r].Length; i++) pos[Rows[r][i]] = Tuple.Create(r, i + Offsets[r]);
            var d = new Dictionary<char, string>();
            foreach (var a in pos)
            {
                var n = "";
                foreach (var b in pos)
                    if (a.Key != b.Key && Math.Abs(a.Value.Item1 - b.Value.Item1) <= 1 && Math.Abs(a.Value.Item2 - b.Value.Item2) <= 1.1f) n += b.Key;
                d[a.Key] = n;
            }
            return d;
        }

        public static bool IsNeighbor(char a, char b)
        {
            string n;
            return Neighbors.TryGetValue(a, out n) && n.IndexOf(b) >= 0;
        }

        /// <summary>Lohnt sich eine Korrektur überhaupt? Nur Buchstaben, mindestens 3.</summary>
        public static bool Plausible(string typed)
        {
            if (string.IsNullOrEmpty(typed) || typed.Length < 3 || typed.Length > 30) return false;
            foreach (char c in typed) if (!char.IsLetter(c)) return false;
            return true;
        }

        /// <summary>Alle Schreibweisen mit genau einem Tippfehler weniger (klein geschrieben).</summary>
        public static IEnumerable<Variant> Variants(string t)
        {
            // ae/oe/ue/ss statt ä/ö/ü/ß (einzeln und alle zusammen)
            var uml = new[] { new[] { "ae", "ä" }, new[] { "oe", "ö" }, new[] { "ue", "ü" }, new[] { "ss", "ß" } }; // i18n-ok
            string all = t;
            foreach (var u in uml)
            {
                for (int i = t.IndexOf(u[0], StringComparison.Ordinal); i >= 0; i = t.IndexOf(u[0], i + 1, StringComparison.Ordinal))
                    yield return new Variant(t.Substring(0, i) + u[1] + t.Substring(i + 2), CostUmlaut);
                all = all.Replace(u[0], u[1]);
            }
            if (all != t) yield return new Variant(all, CostUmlaut);

            for (int i = 0; i < t.Length; i++)
            {
                // Buchstabe zu viel (doppelt getippt zählt weniger)
                bool dbl = (i > 0 && t[i - 1] == t[i]) || (i + 1 < t.Length && t[i + 1] == t[i]);
                bool nb = (i > 0 && IsNeighbor(t[i - 1], t[i])) || (i + 1 < t.Length && IsNeighbor(t[i + 1], t[i]));
                yield return new Variant(t.Remove(i, 1), dbl ? CostDouble : nb ? CostNeighbor : CostExtra);
                // vertauscht
                if (i + 1 < t.Length && t[i] != t[i + 1])
                    yield return new Variant(t.Substring(0, i) + t[i + 1] + t[i] + t.Substring(i + 2), CostSwap);
                // falscher Buchstabe
                foreach (char c in Letters)
                {
                    if (c == t[i]) continue;
                    yield return new Variant(t.Substring(0, i) + c + t.Substring(i + 1), IsNeighbor(t[i], c) ? CostNeighbor : CostOther);
                }
            }
            // Buchstabe fehlt (auch am Ende, z. B. "hau" für "haus" ist kein Fehler, das macht die normale Vorhersage)
            for (int i = 0; i < t.Length; i++)
                foreach (char c in Letters)
                    yield return new Variant(t.Insert(i, c.ToString()), i > 0 && t[i - 1] == c ? CostDouble : CostMissing);
        }

        /// <summary>
        /// Aus den Kandidaten (das beste zuerst) die nehmen, die gut genug sind. Bei einem fertigen Wort
        /// nur einen klaren Treffer: einen einzigen oder einen deutlich besseren als den zweiten.
        /// </summary>
        public static List<Scored> Pick(List<Scored> cands, int floor, bool wholeWord)
        {
            var ok = cands.Where(c => c.Freq >= floor).ToList();
            if (!wholeWord || ok.Count <= 1) return ok;
            return ok[0].Freq >= ok[1].Freq + 30 ? ok.Take(1).ToList() : new List<Scored>();
        }
    }
}
