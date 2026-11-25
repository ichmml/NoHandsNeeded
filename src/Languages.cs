using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Tastatur
{
    internal class LanguageInfo
    {
        public string Code;
        public string Name;
        public bool BuiltIn;
        public string FilePath;     // bei heruntergeladenen/importierten Paketen

        public override string ToString()
        {
            return L.T(Name) + " (" + Code.ToUpperInvariant() + ")" + (BuiltIn ? "" : L.T("  · hinzugefügt"));
        }
    }

    /// <summary>Verwaltet installierte Sprachen. Geladen wird nur, was aktiv ist.</summary>
    internal class LanguageManager
    {
        public static readonly LanguageInfo[] BuiltIns =
        {
            new LanguageInfo { Code = "de", Name = "Deutsch", BuiltIn = true }, // i18n-ok
            new LanguageInfo { Code = "en", Name = "Englisch", BuiltIn = true }, // i18n-ok
            new LanguageInfo { Code = "nl", Name = "Niederländisch", BuiltIn = true }, // i18n-ok
            new LanguageInfo { Code = "fr", Name = "Französisch", BuiltIn = true }, // i18n-ok
            new LanguageInfo { Code = "da", Name = "Dänisch", BuiltIn = true }, // i18n-ok
            new LanguageInfo { Code = "pl", Name = "Polnisch", BuiltIn = true }, // i18n-ok
            new LanguageInfo { Code = "cs", Name = "Tschechisch", BuiltIn = true }, // i18n-ok
            new LanguageInfo { Code = "it", Name = "Italienisch", BuiltIn = true }, // i18n-ok
            new LanguageInfo { Code = "es", Name = "Spanisch", BuiltIn = true }, // i18n-ok
            new LanguageInfo { Code = "tr", Name = "Türkisch", BuiltIn = true }, // i18n-ok
            new LanguageInfo { Code = "ru", Name = "Russisch", BuiltIn = true }, // i18n-ok
        };

        /// <summary>Eingebaute Wortliste öffnen: erst unkomprimiert, sonst .gz.</summary>
        private Stream OpenBuiltIn(string code)
        {
            var s = _resource("wortliste_" + code + ".txt");
            if (s != null) return s;
            var gz = _resource("wortliste_" + code + ".txt.gz");
            return gz == null ? null : new System.IO.Compression.GZipStream(gz, System.IO.Compression.CompressionMode.Decompress);
        }

        private readonly Dictionary<string, Predictor> _loaded = new Dictionary<string, Predictor>();
        private readonly string _dir;
        private readonly Func<string, Stream> _resource;

        public string Dir { get { return _dir; } }

        public LanguageManager(string dataDir, Func<string, Stream> resource = null)
        {
            _dir = Path.Combine(dataDir, "Sprachen");
            _resource = resource ?? (name => Assembly.GetExecutingAssembly().GetManifestResourceStream(name));
            try { Directory.CreateDirectory(_dir); } catch { }
        }

        public static bool IsValidCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length > 12) return false;
            foreach (char c in code) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-' && c != '_') return false;
            return true;
        }

        public List<LanguageInfo> Installed()
        {
            var list = new List<LanguageInfo>(BuiltIns.Select(b => new LanguageInfo { Code = b.Code, Name = b.Name, BuiltIn = true }));
            try
            {
                foreach (var f in Directory.GetFiles(_dir, "*.txt").OrderBy(x => x))
                {
                    string code = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (!IsValidCode(code) || BuiltIns.Any(b => b.Code == code)) continue;
                    string name = code.ToUpperInvariant();
                    try
                    {
                        using (var r = new StreamReader(f, Encoding.UTF8))
                        {
                            var h = Predictor.ReadHeader((r.ReadLine() ?? "").TrimStart('﻿'));
                            if (h != null) name = h.Item2;
                        }
                    }
                    catch { }
                    list.Add(new LanguageInfo { Code = code, Name = name, FilePath = f });
                }
            }
            catch { }
            return list;
        }

        public LanguageInfo Find(string code)
        {
            return Installed().FirstOrDefault(l => l.Code == code);
        }

        /// <summary>Sprache laden (einmal, danach aus dem Speicher). Null, wenn nicht vorhanden.</summary>
        public Predictor Get(string code)
        {
            Predictor p;
            if (_loaded.TryGetValue(code, out p)) return p;
            var info = Find(code);
            if (info == null) return null;
            p = new Predictor { Code = code, Name = info.Name };
            try
            {
                if (info.BuiltIn)
                {
                    using (var s = OpenBuiltIn(code)) { if (s == null) return null; p.Load(s); }
                    LoadPairs(p);
                }
                else
                    using (var s = File.OpenRead(info.FilePath)) p.Load(s);
            }
            catch (Exception e) { Log.Write("Sprache " + code + " konnte nicht geladen werden: " + e.Message); return null; }
            _loaded[code] = p;
            return p;
        }

        public void Unload(string code) { _loaded.Remove(code); }

        /// <summary>Eingebaute Wortfolgen dazuladen (wortfolgen_xx.txt.gz). Fehlen sie, geht es ohne.</summary>
        private void LoadPairs(Predictor p)
        {
            try
            {
                var gz = _resource("wortfolgen_" + p.Code + ".txt.gz");
                if (gz == null) return;
                Action load = () =>
                {
                    try
                    {
                        var b = new Bigrams(p.Code);
                        using (var s = new System.IO.Compression.GZipStream(gz, System.IO.Compression.CompressionMode.Decompress)) b.Load(s);
                        p.Pairs = b;   // erst fertig geladen, dann sichtbar
                    }
                    catch (Exception e) { Log.Write("Wortfolgen " + p.Code + ": " + e.Message); }
                };
                if (PairsInBackground) Task.Run(load); else load();
            }
            catch (Exception e) { Log.Write("Wortfolgen " + p.Code + ": " + e.Message); }
        }

        /// <summary>Wortfolgen im Hintergrund laden, damit die Tastatur sofort da ist.</summary>
        public bool PairsInBackground = true;

        // ---------- eigene Begriffe ----------

        public string CustomWordsPath { get { return Path.Combine(Path.GetDirectoryName(_dir), "eigene_woerter.txt"); } }

        public Predictor LoadCustomWords()
        {
            var p = new Predictor { Code = "eigene", Name = L.T("Eigene Begriffe") };
            try
            {
                if (!File.Exists(CustomWordsPath))
                    File.WriteAllText(CustomWordsPath,
                        "# " + L.T("Eigene Begriffe: ein Wort pro Zeile, z. B. Namen und Fachwörter.") + "\n" +
                        "# " + L.T("Sie werden in allen Sprachen vorgeschlagen.") + "\n" +
                        "Quadstick\n", new UTF8Encoding(false));
                var words = File.ReadAllLines(CustomWordsPath, Encoding.UTF8)
                    .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#"));
                p.LoadWords(words, 650);
            }
            catch (Exception e) { Log.Write("eigene_woerter.txt: " + e.Message); }
            return p;
        }

        // ---------- Sprachpakete prüfen und installieren ----------

        /// <summary>Prüft eine Wortliste. Liefert null bei Erfolg, sonst eine Fehlermeldung.</summary>
        public static string Validate(byte[] data, string fallbackCode, out string code, out string name, out int words)
        {
            code = null; name = null; words = 0;
            var p = new Predictor();
            try { words = p.Load(new MemoryStream(data)); }
            catch (Exception e) { return L.T("Datei nicht lesbar: ") + e.Message; }
            code = p.Code.Length > 0 ? p.Code : (fallbackCode ?? "").ToLowerInvariant();
            name = p.Name.Length > 0 ? p.Name : code.ToUpperInvariant();
            if (!IsValidCode(code)) return L.T("Kein gültiges Sprachkürzel (z. B. fr.txt oder Kopfzeile \"#sprache fr Französisch\").");
            if (words < 100) return L.T("Zu wenige Wörter (") + words + L.T("). Format: Wort, Tabulator, Häufigkeit, eine Zeile pro Wort.");
            return null;
        }

        /// <summary>Paket speichern. Eingebaute Sprachen (DE, EN) lassen sich nicht ersetzen.</summary>
        public string Install(byte[] data, string fallbackCode, out LanguageInfo info)
        {
            info = null;
            string code, name; int words;
            var err = Validate(data, fallbackCode, out code, out name, out words);
            if (err != null) return err;
            if (BuiltIns.Any(b => b.Code == code)) return name + L.T(" ist schon fest eingebaut.");
            string target = Path.Combine(_dir, code + ".txt");
            string tmp = target + ".neu";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
            _loaded.Remove(code);
            info = new LanguageInfo { Code = code, Name = name, FilePath = target };
            Log.Write("Sprache installiert: " + code + " (" + name + "), " + words + " Wörter");
            return null;
        }

        public bool Delete(string code)
        {
            var info = Find(code);
            if (info == null || info.BuiltIn) return false;
            try { File.Delete(info.FilePath); _loaded.Remove(code); Log.Write("Sprache gelöscht: " + code); return true; }
            catch { return false; }
        }
    }

    /// <summary>Die aktiven Sprachen und der aktuelle Modus (eine Sprache oder alle gemischt).</summary>
    internal class LanguageSet : ISuggester
    {
        public const string MixKey = "mix";

        private readonly List<Predictor> _active = new List<Predictor>();
        private Predictor _custom;
        public bool OfferMix = true;
        private string _mode = "de";

        public List<Predictor> Active { get { return _active; } }
        public string Mode { get { return _mode; } }

        public void SetActive(IEnumerable<Predictor> predictors, Predictor custom)
        {
            _active.Clear();
            _active.AddRange(predictors.Where(p => p != null));
            _custom = custom;
            SetMode(_mode);
            foreach (var st in _stores.Values) FixStoredForms(st);
        }

        private Predictor PredictorFor(string code)
        {
            return _active.FirstOrDefault(p => p.Code == code);
        }

        /// <summary>
        /// Klein geschriebene Form eines Worts, das die Wortliste nur großgeschrieben kennt ("zeit"):
        /// dann die richtige Schreibweise ("Zeit"). Null = Schreibweise passt.
        /// So lernt die Tastatur keine falsche Kleinschreibung, wenn ich (oder Wispr Flow) ein Substantiv klein schreibe.
        /// </summary>
        public string ListFormFor(string code, string word)
        {
            var p = PredictorFor(code);
            return p == null ? null : p.CapitalFormOf(word);
        }

        /// <summary>Früher falsch gelernte Kleinschreibung korrigieren. Mit der Groß/Klein-Taste gewählte bleibt.</summary>
        public int FixStoredForms(LearningStore st)
        {
            if (st == null) return 0;
            string code = st.Code;
            return st.FixForms(w => ListFormFor(code, w));
        }

        /// <summary>Reihum: jede aktive Sprache, dann (wenn erlaubt) alle gemischt.</summary>
        public List<string> Modes()
        {
            var m = _active.Select(p => p.Code).ToList();
            if (OfferMix && _active.Count >= 2) m.Add(MixKey);
            return m;
        }

        public void SetMode(string mode)
        {
            var modes = Modes();
            _mode = modes.Contains(mode) ? mode : (modes.Count > 0 ? modes[0] : "de");
        }

        public string Next()
        {
            var modes = Modes();
            if (modes.Count == 0) return _mode;
            int i = modes.IndexOf(_mode);
            _mode = modes[(i + 1) % modes.Count];
            return _mode;
        }

        public string Label
        {
            get
            {
                if (_mode == MixKey)
                    return _active.Count <= 3 ? string.Join("+", _active.Select(p => p.Code.ToUpperInvariant())) : L.T("Alle");
                return _mode.ToUpperInvariant();
            }
        }

        public string LongLabel
        {
            get
            {
                if (_mode == MixKey) return string.Join(" + ", _active.Select(p => L.T(p.Name)));
                var p0 = _active.FirstOrDefault(p => p.Code == _mode);
                return p0 != null ? L.T(p0.Name) : _mode;
            }
        }

        private IEnumerable<Predictor> Current()
        {
            if (_mode == MixKey) return _active;
            return _active.Where(p => p.Code == _mode);
        }

        // ---------- Lernen ----------

        private readonly Dictionary<string, LearningStore> _stores = new Dictionary<string, LearningStore>();
        /// <summary>Neue, unbekannte Wörter erst vorschlagen, wenn sie so oft getippt wurden.</summary>
        public int MinNewCount = 2;

        // ---------- Zahlen (sprachunabhängig) ----------
        public NumberStore Numbers = new NumberStore();
        public EmailStore Emails = new EmailStore();
        public bool EmailsEnabled = true;

        public void LearnEmail(string email) { if (EmailsEnabled) Emails.Learn(email); }

        public List<string> SuggestEmailsInField(string typed, int max)
        {
            return EmailsEnabled ? Emails.SuggestAny(typed, max) : new List<string>();
        }

        public List<string> SuggestEmails(string typed, int max)
        {
            return EmailsEnabled ? Emails.Suggest(typed, max, 1) : new List<string>();   // Adressen gleich ab dem ersten Mal
        }
        public bool NumbersEnabled = true;
        public bool IbansEnabled = true;

        /// <summary>Gültige IBAN merken (liegt bei den Zahlen, Datei Lernen\zahlen.txt).</summary>
        public void LearnIban(string iban) { if (IbansEnabled && IbanRules.IsValid(iban)) Numbers.Learn(IbanRules.Format(iban)); }

        public List<string> SuggestIbans(string typed, int max)
        {
            return IbansEnabled ? Numbers.SuggestIbans(typed ?? "", max) : new List<string>();
        }

        /// <summary>Getippte Zahl lernen, wenn sie ein Datum, eine Telefonnummer oder Postleitzahl ist.</summary>
        public void LearnNumber(string raw, bool followedByWord)
        {
            if (!NumbersEnabled) return;
            string f = NumberRules.Classify(raw, followedByWord);
            if (f != null) Numbers.Learn(f);
        }

        public void UnlearnNumber(string raw, bool followedByWord)
        {
            string f = NumberRules.Classify(raw, followedByWord);
            if (f != null) Numbers.Unlearn(f);
        }

        public List<string> SuggestNumbers(string typed, int max)
        {
            return NumbersEnabled ? Numbers.Suggest(typed, max, 1) : new List<string>();   // Nummern gleich ab dem ersten Mal
        }

        public void SetStore(string code, LearningStore store)
        {
            _stores[code] = store;
            if (store == null) return;
            if (string.IsNullOrEmpty(store.Code)) store.Code = code;
            store.ListForm = w => ListFormFor(code, w);
            FixStoredForms(store);
        }
        public IEnumerable<LearningStore> Stores { get { return _stores.Values; } }

        public LearningStore Store(string code)
        {
            LearningStore st;
            return code != null && _stores.TryGetValue(code, out st) ? st : null;
        }

        /// <summary>In welche Sprache ein Wort gelernt wird: aktuelle Sprache, bei gemischt die, die das Wort kennt.</summary>
        public LearningStore TargetFor(string word)
        {
            if (_mode != MixKey) return Store(_mode);
            Scored sc;
            foreach (var p in _active)
                if (p.TryGet(word, out sc)) return Store(p.Code);
            foreach (var p in _active)
            {
                var st = Store(p.Code);
                if (st != null && st.Get(word) != null) return st;
            }
            return _active.Count > 0 ? Store(_active[0].Code) : null;
        }

        public void Learn(string word, string prev, bool atSentenceStart)
        {
            var st = TargetFor(word);
            if (st != null) st.Learn(word, prev, atSentenceStart);
        }

        /// <summary>Wort so oft lernen, dass es ab jetzt vorgeschlagen wird.</summary>
        public void LearnUntilSuggested(string word)
        {
            var st = TargetFor(word);
            if (st == null || !LearningStore.IsLearnable(word)) return;
            var lw = st.Get(word);
            int have = lw == null ? 0 : lw.Count;
            if (have < MinNewCount) st.Learn(word, null, false, MinNewCount - have);
        }

        /// <summary>Kennt die Tastatur das Wort (Wortliste, Gelerntes, eigene Begriffe)?</summary>
        public bool Knows(string word)
        {
            if (string.IsNullOrEmpty(word)) return true;
            Scored sc;
            foreach (var p in _active) if (p.TryGet(word, out sc)) return true;
            if (_custom != null && _custom.TryGet(word, out sc)) return true;
            foreach (var st in _stores.Values) { var lw = st.Get(word); if (lw != null && lw.Count >= MinNewCount) return true; }
            return false;
        }

        public void Unlearn(string word, string prev)
        {
            var st = TargetFor(word);
            if (st != null) st.Unlearn(word, prev);
        }

        public void SetForm(string word)
        {
            var st = TargetFor(word);
            if (st != null) st.SetForm(word);
        }

        // ---------- Vorschläge ----------

        private static double Ln(int c) { return Math.Log(1 + Math.Max(0, c)); }

        /// <summary>Bonus beim Weitertippen, wenn das Wort laut eingebauten Wortfolgen oft auf prev folgt.</summary>
        /// <summary>
        /// form wird auf die Schreibweise aus der Wortfolge gesetzt (freundlichen -> Grüßen, nicht grüßen),
        /// außer der Nutzer hat eine eigene Schreibweise gelernt (keepForm).
        /// </summary>
        private static double BuiltInPairBonus(Predictor p, string prev, ref string form, bool keepForm)
        {
            if (p.Pairs == null || prev == null) return 0;
            var next = p.Pairs.NextOf(prev);
            // genau diese Schreibweise folgt oft (beim Essen, wir essen): Bonus nur für sie
            foreach (var x in next)
                if (string.Equals(x.Word, form, StringComparison.Ordinal)) return Math.Min(250, 100 + 6 * x.Freq);
            foreach (var x in next)
                if (string.Equals(x.Word, form, StringComparison.OrdinalIgnoreCase))
                {
                    // gibt es beide Schreibweisen in der Liste, bekommt nur die passende den Bonus
                    if (p.HasExact(x.Word) && p.HasExact(form)) return 0;
                    if (!keepForm) form = x.Word;
                    return Math.Min(250, 100 + 6 * x.Freq);
                }
            return 0;
        }

        /// <summary>
        /// Gelernte Schreibweise auf einen Kandidaten anwenden? Nicht, wenn die Liste beide Schreibweisen
        /// als eigene Wörter hat (essen/Essen): dann bleibt jede, wie sie ist, und nur die gelernte bekommt den Bonus.
        /// </summary>
        private static bool AppliesTo(Predictor p, LearnedWord lw, string candidate)
        {
            if (lw == null) return false;
            if (string.Equals(lw.Form, candidate, StringComparison.Ordinal)) return true;
            return !(p.HasExact(lw.Form) && p.HasExact(candidate));
        }

        /// <summary>Schlüssel zum Zusammenfassen: gleiche Wörter einmal, außer beide Schreibweisen sind echte Wörter.</summary>
        private string MergeKey(string word)
        {
            foreach (var p in Current()) if (p.HasSeveralForms(word) && p.HasExact(word)) return word;
            return word.ToLowerInvariant();
        }

        public List<string> Suggest(string typed, int max, bool capitalize)
        {
            return Suggest(typed, null, max, capitalize);
        }

        /// <summary>Korrekturvorschläge abschalten (Einstellung).</summary>
        public bool CorrectionsEnabled = true;

        /// <summary>
        /// Korrekturvorschläge bei Tippfehlern aus den aktuellen Sprachen. Leer, wenn eine Sprache,
        /// das Gelernte oder die eigenen Begriffe das Getippte kennen.
        /// </summary>
        public List<string> Corrections(string typed, int max, bool capitalize, bool wholeWord)
        {
            var res = new List<string>();
            if (!CorrectionsEnabled || !Correction.Plausible(typed)) return res;
            if (_custom != null && (wholeWord ? _custom.Knows(typed) : _custom.HasPrefix(typed))) return res;
            foreach (var st in _stores.Values)
            {
                if (wholeWord) { var lw = st.Get(typed); if (lw != null) return res; }
                else if (st.WithPrefix(typed).Concat(st.WithPrefixFolded(typed)).Any(lw => lw.Count >= MinNewCount)) return res;
            }
            var all = new List<Scored>();
            foreach (var p in Current())
            {
                var f = p.FixesFor(typed, max, wholeWord);
                if (f == null) return res;   // diese Sprache kennt das Wort
                all.AddRange(f);
            }
            foreach (var c in all.OrderByDescending(x => x.Freq))
            {
                string w = Predictor.Shape(c.Word, typed, capitalize);
                if (!res.Contains(w)) res.Add(w);
                if (res.Count >= max) break;
            }
            if (wholeWord && res.Count > 1) res.RemoveRange(1, res.Count - 1);
            return res;
        }

        /// <summary>
        /// Vorschläge aus Grundwortschatz und Gelerntem. Punkte: Häufigkeit im Grundwortschatz (ca. 300 bis 770),
        /// plus Bonus für oft benutzte Wörter, plus Bonus, wenn das Wort oft auf prev folgt.
        /// </summary>
        public List<string> Suggest(string typed, string prev, int max, bool capitalize)
        {
            typed = typed ?? "";
            var merged = new Dictionary<string, Scored>(StringComparer.Ordinal);
            Action<string, double> add = (word, score) =>
            {
                if (word == typed) return;   // schon fertig getippt
                Scored old;
                int sc = (int)Math.Round(score);
                string key = MergeKey(word);
                if (!merged.TryGetValue(key, out old) || sc > old.Freq) merged[key] = new Scored(word, sc);
            };

            foreach (var p in Current())
            {
                var st = Store(p.Code);
                foreach (var c in p.Candidates(typed, max * 3))
                {
                    double score = c.Freq;
                    string form = c.Word;
                    if (st != null)
                    {
                        var lw = st.Get(c.Word);
                        if (!AppliesTo(p, lw, c.Word)) lw = null;
                        if (lw != null) { score += 40 * Ln(lw.Count); form = lw.Form; }
                        int pc = st.PairCountOf(prev, c.Word);
                        if (pc > 0) score += 120 + 50 * Ln(pc);
                        else if (typed.Length > 0) score += BuiltInPairBonus(p, prev, ref form, lw != null);
                    }
                    else if (typed.Length > 0) score += BuiltInPairBonus(p, prev, ref form, false);
                    add(form, score);
                }
                if (typed.Length > 0 && prev != null && p.Pairs != null)
                {
                    // typische Folgewörter, die so anfangen, auch wenn sie seltener sind (freundlichen gr -> Grüßen)
                    string tl = typed.ToLowerInvariant(), tf = TextFold.IsPlain(typed) ? TextFold.Fold(typed) : null;
                    foreach (var nx in p.Pairs.NextOf(prev))
                    {
                        string nl = nx.Word.ToLowerInvariant();
                        if (!nl.StartsWith(tl, StringComparison.Ordinal) && (tf == null || !TextFold.Fold(nl).StartsWith(tf, StringComparison.Ordinal))) continue;
                        Scored baseSc;
                        double score = p.TryGet(nx.Word, out baseSc) ? baseSc.Freq : 300;
                        string form = nx.Word;
                        bool own = false;
                        if (st != null)
                        {
                            var lw = st.Get(nx.Word);
                            if (!AppliesTo(p, lw, nx.Word)) lw = null;
                            if (lw != null) { score += 40 * Ln(lw.Count); if (lw.Count >= MinNewCount) { form = lw.Form; own = true; } }
                            int pc = st.PairCountOf(prev, nx.Word);
                            if (pc > 0) { score += 120 + 50 * Ln(pc); add(form, score); continue; }
                        }
                        string f2 = form;
                        score += BuiltInPairBonus(p, prev, ref f2, own);
                        add(f2, score);
                    }
                }
                if (typed.Length == 0 && prev != null && p.Pairs != null)
                {
                    // eingebaute Wortfolgen: typische nächste Wörter, auch wenn noch nichts gelernt ist.
                    // Gelernte Wortfolgen (900 und mehr) stehen davor.
                    foreach (var nx in p.Pairs.NextOf(prev))
                    {
                        string form = nx.Word;
                        if (st != null) { var lw = st.Get(nx.Word); if (lw != null && lw.Count >= MinNewCount && AppliesTo(p, lw, nx.Word)) form = lw.Form; }
                        add(form, 780 + 1.2 * nx.Freq);
                    }
                }
                if (st == null) continue;

                if (typed.Length > 0)
                {
                    foreach (var lw in st.WithPrefix(typed).Concat(st.WithPrefixFolded(typed)))
                    {
                        Scored baseSc;
                        bool known = p.TryGet(lw.Form, out baseSc);
                        if (!known && lw.Count < MinNewCount) continue;
                        double score = known ? baseSc.Freq + 40 * Ln(lw.Count) : Math.Min(720, 480 + 60 * Ln(lw.Count));
                        int pc = st.PairCountOf(prev, lw.Form);
                        if (pc > 0) score += 120 + 50 * Ln(pc);
                        else { string f2 = lw.Form; score += BuiltInPairBonus(p, prev, ref f2, true); }
                        add(lw.Form, score);
                    }
                }
                else if (prev != null)
                {
                    // nächstes Wort: was folgt bei mir oft auf das Wort davor?
                    int n = 0;
                    foreach (var kv in st.NextOf(prev))
                    {
                        Scored baseSc;
                        if (kv.Key.Count < MinNewCount && !p.TryGet(kv.Key.Form, out baseSc)) continue;
                        if (kv.Value < 2) continue;
                        add(kv.Key.Form, 900 + 50 * Ln(kv.Value));
                        if (++n >= max * 2) break;
                    }
                }
            }
            if (_custom != null && typed.Length > 0)
                foreach (var c in _custom.Candidates(typed, max)) add(c.Word, c.Freq);

            var sorted = merged.Values.OrderByDescending(x => x.Freq).ToList();
            return Predictor.Finish(sorted, typed, max, capitalize);
        }
    }

    internal class PackEntry
    {
        public string Code, Name, File, Sha256;
        public long Bytes;
        public bool Installed;

        public override string ToString()
        {
            string size = (Bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " MB";
            return L.T(Name) + " (" + Code.ToUpperInvariant() + "), " + size + (Installed ? L.T("   ✓ installiert") : "");
        }
    }

    /// <summary>Sprachpakete aus dem Netz. Wird nur auf ausdrücklichen Klick benutzt.</summary>
    internal static class PackDownloader
    {
        public const string DefaultSource = "https://raw.githubusercontent.com/ichmml/nohandsneeded/main/sprachpakete/";
        /// <summary>Frühere Standard-Adressen, werden beim Laden der Einstellungen auf die neue umgestellt.</summary>
        public static readonly string[] OldDefaultSources = {
            "https://raw.githubusercontent.com/ichmml/bildschirmtastatur-sprachen/main/",
            "https://raw.githubusercontent.com/ichmml/bildschirmtastatur/main/sprachpakete/" };

        public static bool IsValidSource(string s)
        {
            Uri u;
            return !string.IsNullOrWhiteSpace(s) && Uri.TryCreate(s.Trim(), UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Length > 0;
        }

        /// <summary>Verständliche Meldung zu einem Download-Fehler.</summary>
        public static string FriendlyError(Exception e)
        {
            var we = e as WebException;
            if (e is AggregateException && e.InnerException != null) return FriendlyError(e.InnerException);
            if (we != null)
            {
                var resp = we.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotFound)
                    return L.T("Unter dieser Adresse liegen noch keine Sprachpakete. Sprachen lassen sich über \"Aus Datei hinzufügen\" auch ohne Internet installieren.");
                if (we.Status == WebExceptionStatus.NameResolutionFailure || we.Status == WebExceptionStatus.ConnectFailure)
                    return L.T("Keine Internetverbindung oder Server nicht erreichbar. Die Tastatur läuft normal weiter.");
                if (we.Status == WebExceptionStatus.Timeout)
                    return L.T("Der Server antwortet nicht (Zeitüberschreitung). Später nochmal versuchen.");
                if (resp != null)
                    return L.T("Der Server meldet einen Fehler (") + (int)resp.StatusCode + L.T("). Die Tastatur läuft normal weiter.");
            }
            if (e is InvalidDataException) return e.Message;
            return L.T("Download fehlgeschlagen: ") + e.Message;
        }

        /// <summary>Übersichtsdatei lesen: kürzel, name, datei, bytes, sha256 (Tab-getrennt, # = Kommentar).</summary>
        public static List<PackEntry> ParseIndex(string text)
        {
            var list = new List<PackEntry>();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                var p = l.Split('\t');
                if (p.Length < 5) continue;
                long bytes;
                if (!long.TryParse(p[3].Trim(), out bytes)) continue;
                string code = p[0].Trim().ToLowerInvariant();
                string file = p[2].Trim();
                if (!LanguageManager.IsValidCode(code) || file.Contains("/") || file.Contains("\\") || file.Contains("..")) continue;
                string sha = p[4].Trim().ToLowerInvariant();
                if (sha.Length != 64) continue;
                list.Add(new PackEntry { Code = code, Name = p[1].Trim(), File = file, Bytes = bytes, Sha256 = sha });
            }
            return list;
        }

        public static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(data);
                var sb = new StringBuilder();
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Prüft Größe und Prüfsumme. Null = in Ordnung.</summary>
        public static string Verify(PackEntry e, byte[] data)
        {
            if (data.LongLength != e.Bytes) return L.T("Datei unvollständig (") + data.LongLength + L.T(" statt ") + e.Bytes + L.T(" Bytes).");
            if (Sha256Hex(data) != e.Sha256) return L.T("Prüfsumme stimmt nicht, Datei wurde verworfen.");
            return null;
        }

        private static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient { Encoding = Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = AppInfo.Name;
            return wc;
        }

        public static string Url(string source, string file)
        {
            if (string.IsNullOrWhiteSpace(source)) source = DefaultSource;
            return source.TrimEnd('/') + "/" + file;
        }

        public static async Task<List<PackEntry>> FetchIndex(string source)
        {
            using (var wc = Client())
            {
                Log.Write("Sprachpakete: Übersicht laden von " + Url(source, "index.txt"));
                string text = await wc.DownloadStringTaskAsync(Url(source, "index.txt"));
                return ParseIndex(text);
            }
        }

        public static async Task<byte[]> FetchPack(string source, PackEntry e)
        {
            using (var wc = Client())
            {
                Log.Write("Sprachpakete: lade " + e.File);
                var data = await wc.DownloadDataTaskAsync(Url(source, e.File));
                string err = Verify(e, data);
                if (err != null) { Log.Write("Sprachpaket " + e.Code + ": " + err); throw new InvalidDataException(err); }
                return data;
            }
        }
    }
}
