using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tastatur
{
    internal interface ISuggester
    {
        /// <summary>prev = das Wort davor (für gelernte Wortfolgen), kann null sein.</summary>
        List<string> Suggest(string typed, string prev, int max, bool capitalize);
        /// <summary>Gelernte Zahlen, deren Ziffern so anfangen.</summary>
        List<string> SuggestNumbers(string typed, int max);
    }

    /// <summary>Wörter ohne Akzente vergleichen: "zolw" findet "żółw", "cafe" findet "café".</summary>
    internal static class TextFold
    {
        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            string low = s.ToLowerInvariant();
            bool plain = true;
            foreach (char c in low) if (c > 0x7F) { plain = false; break; }
            if (plain) return low;
            var sb = new StringBuilder(low.Length);
            foreach (char c in low)
            {
                switch (c)
                {
                    case 'ł': sb.Append('l'); continue;
                    case 'ø': sb.Append('o'); continue;
                    case 'đ': sb.Append('d'); continue;
                    case 'ı': sb.Append('i'); continue;
                    case 'æ': sb.Append("ae"); continue;
                    case 'œ': sb.Append("oe"); continue;
                    case 'ß': sb.Append("ss"); continue;
                    case 'ё': sb.Append('е'); continue;
                }
                // kyrillische Buchstaben (z. B. й) bleiben, sie sind eigene Buchstaben
                if (c >= 0x400 && c <= 0x4FF) { sb.Append(c); continue; }
                string d = c.ToString().Normalize(NormalizationForm.FormD);
                foreach (char x in d)
                    if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(x) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(x);
            }
            return sb.ToString();
        }

        /// <summary>Ist die Eingabe schon ohne Akzente? Nur dann wird auch akzentfrei gesucht.</summary>
        public static bool IsPlain(string s) { return Fold(s) == (s ?? "").ToLowerInvariant(); }
    }

    internal struct Scored
    {
        public string Word;
        public int Freq;
        public Scored(string w, int f) { Word = w; Freq = f; }
    }

    /// <summary>
    /// Wortvorhersage für eine Sprache: Präfixsuche im Grundwortschatz, sortiert nach Häufigkeit.
    /// Ab Stufe 2 kommt hier das Lernen dazu (pro Sprache getrennt).
    /// </summary>
    internal class Predictor : ISuggester
    {
        public List<string> SuggestNumbers(string typed, int max) { return new List<string>(); }

        private string[] _words;     // Originalschreibweise
        private string[] _keys;      // kleingeschrieben, sortiert
        private int[] _freq;
        private int[] _topByFreq;    // Indizes der häufigsten Wörter (für leere Eingabe)
        private string[] _foldKeys;  // ohne Akzente, sortiert (nur Wörter mit Akzenten)
        private int[] _foldIdx;

        public string Code = "";
        public string Name = "";

        public int Count { get { return _words == null ? 0 : _words.Length; } }

        /// <summary>Kopfzeile eines Sprachpakets lesen: "#sprache&lt;TAB&gt;kürzel&lt;TAB&gt;name". Null, wenn keine.</summary>
        public static Tuple<string, string> ReadHeader(string firstLine)
        {
            if (firstLine == null || !firstLine.StartsWith("#sprache\t")) return null;
            var p = firstLine.Split('\t');
            if (p.Length < 2 || p[1].Trim().Length == 0) return null;
            return Tuple.Create(p[1].Trim().ToLowerInvariant(), p.Length > 2 ? p[2].Trim() : p[1].Trim().ToUpperInvariant());
        }

        /// <summary>Laden. Zeilen mit # werden übersprungen. Liefert die Zahl der gültigen Zeilen.</summary>
        public int Load(Stream s)
        {
            var list = new List<Tuple<string, string, int>>();
            using (var r = new StreamReader(s, Encoding.UTF8))
            {
                string line;
                bool first = true;
                while ((line = r.ReadLine()) != null)
                {
                    if (first)
                    {
                        first = false;
                        var h = ReadHeader(line.TrimStart('﻿'));
                        if (h != null) { if (Code.Length == 0) Code = h.Item1; if (Name.Length == 0) Name = h.Item2; continue; }
                    }
                    if (line.StartsWith("#")) continue;
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    string w = line.Substring(0, tab).Trim();
                    if (w.Length == 0) continue;
                    int f;
                    if (!int.TryParse(line.Substring(tab + 1).Trim(), out f)) continue;
                    list.Add(Tuple.Create(w.ToLowerInvariant(), w, f));
                }
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            _keys = new string[list.Count];
            _words = new string[list.Count];
            _freq = new int[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                _keys[i] = list[i].Item1; _words[i] = list[i].Item2; _freq[i] = list[i].Item3;
            }
            var idx = new int[list.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            Array.Sort(idx, (a, b) => _freq[b].CompareTo(_freq[a]));
            _topByFreq = new int[Math.Min(200, idx.Length)];
            Array.Copy(idx, _topByFreq, _topByFreq.Length);
            var folds = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < _keys.Length; i++)
            {
                string f = TextFold.Fold(_keys[i]);
                if (f != _keys[i]) folds.Add(new KeyValuePair<string, int>(f, i));
            }
            folds.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            _foldKeys = folds.Select(x => x.Key).ToArray();
            _foldIdx = folds.Select(x => x.Value).ToArray();
            return list.Count;
        }

        public void LoadWords(IEnumerable<string> words, int freq)
        {
            var sb = new StringBuilder();
            foreach (var w in words) sb.Append(w).Append('\t').Append(freq).Append('\n');
            Load(new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        private int LowerBound(string prefix)
        {
            int lo = 0, hi = _keys.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (string.CompareOrdinal(_keys[mid], prefix) < 0) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>Kandidaten mit Häufigkeit, ohne Groß-/Kleinanpassung. Bei leerer Eingabe die häufigsten Wörter.</summary>
        public List<Scored> Candidates(string typed, int max)
        {
            var result = new List<Scored>();
            if (_keys == null || max <= 0) return result;
            if (string.IsNullOrEmpty(typed))
            {
                foreach (int i in _topByFreq)
                {
                    if (_keys[i].Length < 2) continue;
                    result.Add(new Scored(_words[i], _freq[i]));
                    if (result.Count >= max) break;
                }
                return result;
            }
            string p = typed.ToLowerInvariant();
            int start = LowerBound(p);
            var best = new List<int>();
            for (int i = start; i < _keys.Length && _keys[i].StartsWith(p, StringComparison.Ordinal); i++)
            {
                if (_keys[i].Length == p.Length && _words[i] == typed) continue; // schon fertig getippt
                best.Add(i);
            }
            if (_foldKeys != null && _foldKeys.Length > 0 && TextFold.IsPlain(p))
            {
                int lo = 0, hi = _foldKeys.Length;
                while (lo < hi) { int mid = (lo + hi) / 2; if (string.CompareOrdinal(_foldKeys[mid], p) < 0) lo = mid + 1; else hi = mid; }
                var have = new HashSet<int>(best);
                for (int i = lo; i < _foldKeys.Length && _foldKeys[i].StartsWith(p, StringComparison.Ordinal); i++)
                    if (have.Add(_foldIdx[i])) best.Add(_foldIdx[i]);
            }
            best.Sort((a, b) => _freq[b].CompareTo(_freq[a]));
            for (int j = 0; j < best.Count && j < max; j++) result.Add(new Scored(_words[best[j]], _freq[best[j]]));
            return result;
        }

        /// <summary>Wort im Grundwortschatz nachschlagen (Groß-/Kleinschreibung egal).</summary>
        public bool TryGet(string word, out Scored result)
        {
            result = new Scored();
            if (_keys == null || string.IsNullOrEmpty(word)) return false;
            string k = word.ToLowerInvariant();
            int i = LowerBound(k);
            if (i < _keys.Length && _keys[i] == k) { result = new Scored(_words[i], _freq[i]); return true; }
            return false;
        }

        public List<string> Suggest(string typed, string prev, int max, bool capitalize)
        {
            return Suggest(typed, max, capitalize);
        }

        public List<string> Suggest(string typed, int max, bool capitalize)
        {
            return Finish(Candidates(typed, max * 2), typed, max, capitalize);
        }

        /// <summary>Schreibweise anpassen, Doppelte entfernen, auf max kürzen.</summary>
        public static List<string> Finish(List<Scored> cands, string typed, int max, bool capitalize)
        {
            var result = new List<string>();
            var seen = new HashSet<string>();
            foreach (var c in cands)
            {
                string w = Shape(c.Word, typed, capitalize);
                if (seen.Add(w)) result.Add(w);
                if (result.Count >= max) break;
            }
            return result;
        }

        /// <summary>Groß-/Kleinschreibung an Eingabe und Satzanfang anpassen.</summary>
        public static string Shape(string word, string typed, bool capitalize)
        {
            bool upperFirst = capitalize;
            if (!string.IsNullOrEmpty(typed) && char.IsUpper(typed[0])) upperFirst = true;
            if (!string.IsNullOrEmpty(typed) && typed.Length > 1 && IsAllUpper(typed))
                return word.ToUpperInvariant();
            if (upperFirst && word.Length > 0 && char.IsLower(word[0]))
                return char.ToUpperInvariant(word[0]) + word.Substring(1);
            return word;
        }

        private static bool IsAllUpper(string s)
        {
            foreach (char c in s) if (char.IsLetter(c) && !char.IsUpper(c)) return false;
            return true;
        }
    }
}
