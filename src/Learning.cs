using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Tastatur
{
    internal class LearnedWord
    {
        public string Form;     // bevorzugte Schreibweise
        public int Count;
    }

    /// <summary>
    /// Was die Tastatur über eine Sprache gelernt hat: Wörter mit Häufigkeit und bevorzugter Schreibweise,
    /// dazu Wortfolgen (welches Wort auf welches folgt). Liegt als Textdatei in NoHandsNeeded-Daten\Lernen.
    /// Gespeichert werden nur Wörter und Zähler, keine ganzen Texte.
    /// </summary>
    internal class LearningStore
    {
        private const int MaxWordLength = 40;
        private const int MaxNextPerWord = 30;

        private readonly Dictionary<string, LearnedWord> _words = new Dictionary<string, LearnedWord>();
        private readonly Dictionary<string, Dictionary<string, int>> _next = new Dictionary<string, Dictionary<string, int>>();
        private string[] _sortedKeys;   // für die Präfixsuche, wird bei Bedarf neu gebaut

        public string Code = "";
        public bool Dirty;

        public int WordCount { get { return _words.Count; } }
        public int PairCount { get { return _next.Values.Sum(d => d.Count); } }

        private static string Key(string w) { return w.ToLowerInvariant(); }

        public static bool IsLearnable(string w)
        {
            if (string.IsNullOrEmpty(w) || w.Length > MaxWordLength) return false;
            bool letter = false;
            foreach (char c in w)
            {
                if (char.IsLetter(c)) letter = true;
                else if (!char.IsDigit(c) && c != '-' && c != '\'') return false;
            }
            if (!letter) return false;
            // sieht aus wie ein Passwort oder Code (Buchstaben und Ziffern gemischt, lang): nicht lernen
            if (w.Any(char.IsDigit) && w.Length >= 6) return false;
            return true;
        }

        /// <summary>Ein Wort ist fertig getippt. atSentenceStart: Großschreibung kam evtl. nur vom Satzanfang.</summary>
        public void Learn(string word, string prev, bool atSentenceStart, int weight = 1)
        {
            if (!IsLearnable(word)) return;
            string k = Key(word);
            LearnedWord lw;
            if (!_words.TryGetValue(k, out lw))
            {
                lw = new LearnedWord { Form = word, Count = 0 };
                _words[k] = lw;
                _sortedKeys = null;
            }
            else if (!atSentenceStart) lw.Form = word;   // am Satzanfang groß geschrieben sagt nichts über die Schreibweise
            lw.Count += weight;
            if (prev != null && IsLearnable(prev))
            {
                string pk = Key(prev);
                Dictionary<string, int> d;
                if (!_next.TryGetValue(pk, out d)) { d = new Dictionary<string, int>(); _next[pk] = d; }
                int c;
                d.TryGetValue(k, out c);
                d[k] = c + weight;
                if (d.Count > MaxNextPerWord)
                {
                    var weakest = d.OrderBy(x => x.Value).First().Key;
                    if (weakest != k) d.Remove(weakest);
                }
            }
            Dirty = true;
        }

        /// <summary>Rückgängig, z. B. wenn ein Wort per Rücktaste wieder geöffnet wurde.</summary>
        public void Unlearn(string word, string prev)
        {
            if (!IsLearnable(word)) return;
            string k = Key(word);
            LearnedWord lw;
            if (_words.TryGetValue(k, out lw))
            {
                lw.Count--;
                if (lw.Count <= 0) { _words.Remove(k); _sortedKeys = null; }
            }
            if (prev != null)
            {
                Dictionary<string, int> d;
                if (_next.TryGetValue(Key(prev), out d))
                {
                    int c;
                    if (d.TryGetValue(k, out c)) { if (c <= 1) d.Remove(k); else d[k] = c - 1; }
                }
            }
            Dirty = true;
        }

        /// <summary>Mit der Groß/Klein-Taste gewählte Schreibweise merken.</summary>
        public void SetForm(string word)
        {
            if (!IsLearnable(word)) return;
            string k = Key(word);
            LearnedWord lw;
            if (_words.TryGetValue(k, out lw)) lw.Form = word;
            else { _words[k] = new LearnedWord { Form = word, Count = 0 }; _sortedKeys = null; }
            Dirty = true;
        }

        public LearnedWord Get(string word)
        {
            LearnedWord lw;
            return word != null && _words.TryGetValue(Key(word), out lw) ? lw : null;
        }

        public int PairCountOf(string prev, string word)
        {
            Dictionary<string, int> d; int c;
            if (prev == null || !_next.TryGetValue(Key(prev), out d)) return 0;
            return d.TryGetValue(Key(word), out c) ? c : 0;
        }

        /// <summary>Gelernte Wörter, die mit dem Präfix beginnen.</summary>
        public IEnumerable<LearnedWord> WithPrefix(string prefix)
        {
            if (_sortedKeys == null) { _sortedKeys = _words.Keys.ToArray(); Array.Sort(_sortedKeys, StringComparer.Ordinal); }
            string p = Key(prefix ?? "");
            int lo = 0, hi = _sortedKeys.Length;
            while (lo < hi) { int mid = (lo + hi) / 2; if (string.CompareOrdinal(_sortedKeys[mid], p) < 0) lo = mid + 1; else hi = mid; }
            for (int i = lo; i < _sortedKeys.Length && _sortedKeys[i].StartsWith(p, StringComparison.Ordinal); i++)
                yield return _words[_sortedKeys[i]];
        }

        /// <summary>Wie WithPrefix, aber ohne Akzente verglichen (nur zusätzliche Treffer).</summary>
        public IEnumerable<LearnedWord> WithPrefixFolded(string prefix)
        {
            string p = Key(prefix ?? "");
            if (p.Length == 0 || !TextFold.IsPlain(p)) yield break;
            foreach (var kv in _words)
                if (!kv.Key.StartsWith(p, StringComparison.Ordinal) && TextFold.Fold(kv.Key).StartsWith(p, StringComparison.Ordinal))
                    yield return kv.Value;
        }

        /// <summary>Wörter, die oft auf prev folgen, mit Zähler.</summary>
        public IEnumerable<KeyValuePair<LearnedWord, int>> NextOf(string prev)
        {
            Dictionary<string, int> d;
            if (prev == null || !_next.TryGetValue(Key(prev), out d)) yield break;
            foreach (var kv in d.OrderByDescending(x => x.Value))
            {
                LearnedWord lw;
                if (_words.TryGetValue(kv.Key, out lw)) yield return new KeyValuePair<LearnedWord, int>(lw, kv.Value);
            }
        }

        public List<LearnedWord> All()
        {
            return _words.Values.OrderByDescending(w => w.Count).ThenBy(w => w.Form, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public void Remove(string word)
        {
            string k = Key(word);
            if (_words.Remove(k)) _sortedKeys = null;
            _next.Remove(k);
            foreach (var d in _next.Values) d.Remove(k);
            Dirty = true;
        }

        public void Clear()
        {
            _words.Clear(); _next.Clear(); _sortedKeys = null; Dirty = true;
        }

        // ---------- Datei ----------

        public void Save(string path)
        {
            string text = ToText();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".neu";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            Dirty = false;
        }

        /// <summary>Anderes Gelerntes dazunehmen (z. B. vom zweiten PC). Zähler: der größere gewinnt,
        /// so zählt mehrfaches Importieren derselben Datei nicht doppelt. Liefert die Zahl neuer Wörter.</summary>
        public int MergeFrom(LearningStore o)
        {
            int added = 0;
            foreach (var kv in o._words)
            {
                LearnedWord lw;
                if (!_words.TryGetValue(kv.Key, out lw)) { _words[kv.Key] = new LearnedWord { Form = kv.Value.Form, Count = kv.Value.Count }; added++; }
                else if (kv.Value.Count > lw.Count) { lw.Count = kv.Value.Count; lw.Form = kv.Value.Form; }
            }
            foreach (var kv in o._next)
            {
                Dictionary<string, int> d;
                if (!_next.TryGetValue(kv.Key, out d)) { d = new Dictionary<string, int>(); _next[kv.Key] = d; }
                foreach (var n in kv.Value)
                {
                    int c;
                    if (!d.TryGetValue(n.Key, out c) || n.Value > c) d[n.Key] = n.Value;
                }
            }
            _sortedKeys = null;
            Dirty = true;
            return added;
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(L.T("Gelernte Wörter der Bildschirmtastatur")).Append(" (").Append(Code).Append(")\n");
            sb.Append("# ").Append(L.T("w <TAB> Wort <TAB> Anzahl   und   f <TAB> Wort <TAB> Folgewort <TAB> Anzahl")).Append("\n");
            foreach (var w in _words.Values.OrderByDescending(x => x.Count))
                sb.Append("w\t").Append(w.Form).Append('\t').Append(w.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var kv in _next)
                foreach (var n in kv.Value)
                    sb.Append("f\t").Append(kv.Key).Append('\t').Append(n.Key).Append('\t').Append(n.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }

        public static LearningStore Load(string path, string code)
        {
            if (!File.Exists(path)) return new LearningStore { Code = code };
            return FromText(File.ReadAllText(path, Encoding.UTF8), code);
        }

        public static LearningStore FromText(string text, string code)
        {
            var s = new LearningStore { Code = code };
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split('\t');
                int c;
                if (p[0] == "w" && p.Length >= 3 && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out c) && p[1].Length > 0)
                    s._words[Key(p[1])] = new LearnedWord { Form = p[1], Count = c };
                else if (p[0] == "f" && p.Length >= 4 && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out c))
                {
                    Dictionary<string, int> d;
                    if (!s._next.TryGetValue(p[1], out d)) { d = new Dictionary<string, int>(); s._next[p[1]] = d; }
                    d[p[2]] = c;
                }
            }
            s.Dirty = false;
            return s;
        }
    }
}
