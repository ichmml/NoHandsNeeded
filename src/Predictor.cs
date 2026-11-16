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
                if (c <= 0x7F) { sb.Append(c); continue; }
                sb.Append(FoldChar(c));
            }
            return sb.ToString();
        }

        private static readonly Dictionary<char, string> _charFold = new Dictionary<char, string>();

        /// <summary>Ein Zeichen ohne Akzent (é -> e). Einmal ausgerechnet, dann gemerkt.</summary>
        private static string FoldChar(char c)
        {
            string r;
            lock (_charFold)
            {
                if (_charFold.TryGetValue(c, out r)) return r;
                var sb = new StringBuilder(2);
                foreach (char x in c.ToString().Normalize(NormalizationForm.FormD))
                    if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(x) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(x);
                r = sb.ToString();
                _charFold[c] = r;
            }
            return r;
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
        /// <summary>Eingebaute Wortfolgen (kann null sein).</summary>
        public volatile Bigrams Pairs;

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
            var keys = new List<string>(1024);
            var words = new List<string>(1024);
            var freqs = new List<int>(1024);
            using (var r = new StreamReader(s, Encoding.UTF8, false, 1 << 16))
            {
                string line;
                bool first = true;
                while ((line = r.ReadLine()) != null)
                {
                    if (first)
                    {
                        first = false;
                        var h = ReadHeader(line.TrimStart('\uFEFF'));
                        if (h != null) { if (Code.Length == 0) Code = h.Item1; if (Name.Length == 0) Name = h.Item2; continue; }
                    }
                    if (line.Length == 0 || line[0] == '#') continue;
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    int f = 0, j = tab + 1;
                    bool num = false, neg = false;
                    while (j < line.Length && line[j] == ' ') j++;
                    if (j < line.Length && line[j] == '-') { neg = true; j++; }
                    for (; j < line.Length && line[j] >= '0' && line[j] <= '9'; j++) { f = f * 10 + (line[j] - '0'); num = true; }
                    while (j < line.Length && char.IsWhiteSpace(line[j])) j++;
                    if (!num || j != line.Length) continue;
                    if (neg) f = -f;
                    string w = line.Substring(0, tab);
                    if (w[0] == ' ' || w[w.Length - 1] == ' ') { w = w.Trim(); if (w.Length == 0) continue; }
                    string k = w.ToLowerInvariant();
                    if (k == w) k = w;   // gleiche Zeichenkette teilen, spart Speicher
                    keys.Add(k); words.Add(w); freqs.Add(f);
                }
            }
            int n = keys.Count;
            _keys = keys.ToArray();
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            bool sorted = true;   // die eingebauten Listen sind schon alphabetisch: dann nicht sortieren
            for (int i = 1; i < n && sorted; i++) if (string.CompareOrdinal(_keys[i - 1], _keys[i]) > 0) sorted = false;
            if (!sorted) Array.Sort(_keys, order, StringComparer.Ordinal);
            _words = new string[n];
            _freq = new int[n];
            for (int i = 0; i < n; i++) { _words[i] = words[order[i]]; _freq[i] = freqs[order[i]]; }
            words = null; keys = null; freqs = null;

            // häufigste Wörter für leere Eingabe: nur die besten 200 suchen, nicht alles sortieren
            var top = new List<int>(256);
            int minTop = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (top.Count >= 200 && _freq[i] <= minTop) continue;
                top.Add(i);
                if (top.Count > 400)
                {
                    top.Sort((x, y) => _freq[y] != _freq[x] ? _freq[y].CompareTo(_freq[x]) : x.CompareTo(y));
                    top.RemoveRange(200, top.Count - 200);
                    minTop = _freq[top[199]];
                }
            }
            top.Sort((x, y) => _freq[y] != _freq[x] ? _freq[y].CompareTo(_freq[x]) : x.CompareTo(y));
            if (top.Count > 200) top.RemoveRange(200, top.Count - 200);
            _topByFreq = top.ToArray();

            var fk = new List<string>();
            var fi = new List<int>();
            for (int i = 0; i < n; i++)
            {
                string k = _keys[i];
                bool plain = true;
                foreach (char c in k) if (c > 0x7F) { plain = false; break; }
                if (plain) continue;
                string f = TextFold.Fold(k);
                if (f != k) { fk.Add(f); fi.Add(i); }
            }
            var fkArr = fk.ToArray();
            var fiArr = fi.ToArray();
            Array.Sort(fkArr, fiArr, StringComparer.Ordinal);
            _foldKeys = fkArr;
            _foldIdx = fiArr;
            return n;
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
            // gleiches Wort in anderer Schreibweise (karton -> Karton) immer mitnehmen und nach vorn
            int same = best.FindIndex(i => _keys[i].Length == p.Length && _keys[i] == p && _words[i] != typed);
            if (same > 0) { int v = best[same]; best.RemoveAt(same); best.Insert(0, v); }
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
            string fix = null;   // richtige Schreibweise des Getippten (karton -> Karton, iphone -> iPhone)
            foreach (var c in cands)
            {
                string w = Shape(c.Word, typed, capitalize);
                if (fix == null && IsCaseFix(w, typed)) fix = w;
            }
            if (fix != null) { result.Add(fix); seen.Add(fix); }
            foreach (var c in cands)
            {
                if (result.Count >= max) break;
                string w = Shape(c.Word, typed, capitalize);
                if (w == typed) continue;
                if (seen.Add(w)) result.Add(w);
            }
            return result;
        }

        /// <summary>Gleiches Wort, nur anders groß/klein geschrieben?</summary>
        public static bool IsCaseFix(string word, string typed)
        {
            return !string.IsNullOrEmpty(typed) && word != typed && string.Equals(word, typed, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// Eingebaute Wortfolgen einer Sprache: welche Wörter folgen oft auf ein Wort ("vielen" -> "Dank").
    /// Datei: wort&lt;TAB&gt;folgewort&lt;TAB&gt;punkte ... (Punkte 1 bis 99 = Anteil in Prozent).
    /// </summary>
    internal class Bigrams
    {
        private readonly Dictionary<string, Scored[]> _next = new Dictionary<string, Scored[]>(StringComparer.Ordinal);
        private readonly bool _turkish;
        private static readonly Scored[] None = new Scored[0];

        public Bigrams(string code) { _turkish = code == "tr"; }

        public int Count { get { return _next.Count; } }

        public string Key(string w)
        {
            if (string.IsNullOrEmpty(w)) return "";
            if (_turkish) w = w.Replace('I', 'ı').Replace('İ', 'i');
            return w.ToLowerInvariant();
        }

        public int Load(Stream s)
        {
            using (var r = new StreamReader(s, Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var p = line.Split('\t');
                    if (p.Length < 3) continue;
                    var list = new List<Scored>((p.Length - 1) / 2);
                    for (int i = 1; i + 1 < p.Length; i += 2)
                    {
                        int sc;
                        if (p[i].Length > 0 && int.TryParse(p[i + 1], out sc)) list.Add(new Scored(p[i], sc));
                    }
                    if (list.Count > 0) _next[Key(p[0])] = list.ToArray();
                }
            }
            return _next.Count;
        }

        /// <summary>Typische Folgewörter, das häufigste zuerst. Nie null.</summary>
        public Scored[] NextOf(string prev)
        {
            Scored[] r;
            return prev != null && _next.TryGetValue(Key(prev), out r) ? r : None;
        }

        /// <summary>Punkte (1 bis 99), wenn next oft auf prev folgt, sonst 0.</summary>
        public int Of(string prev, string next)
        {
            if (prev == null || next == null) return 0;
            foreach (var x in NextOf(prev)) if (string.Equals(x.Word, next, StringComparison.OrdinalIgnoreCase)) return x.Freq;
            return 0;
        }
    }
}
