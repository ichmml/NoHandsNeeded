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
                    using (var s = OpenBuiltIn(code)) { if (s == null) return null; p.Load(s); }
                else
                    using (var s = File.OpenRead(info.FilePath)) p.Load(s);
            }
            catch (Exception e) { Log.Write("Sprache " + code + " konnte nicht geladen werden: " + e.Message); return null; }
            _loaded[code] = p;
            return p;
        }

        public void Unload(string code) { _loaded.Remove(code); }

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

        public void SetStore(string code, LearningStore store) { _stores[code] = store; }
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

        public List<string> Suggest(string typed, int max, bool capitalize)
        {
            return Suggest(typed, null, max, capitalize);
        }

        /// <summary>
        /// Vorschläge aus Grundwortschatz und Gelerntem. Punkte: Häufigkeit im Grundwortschatz (ca. 300 bis 770),
        /// plus Bonus für oft benutzte Wörter, plus Bonus, wenn das Wort oft auf prev folgt.
        /// </summary>
        public List<string> Suggest(string typed, string prev, int max, bool capitalize)
        {
            typed = typed ?? "";
            var merged = new Dictionary<string, Scored>(StringComparer.OrdinalIgnoreCase);
            Action<string, double> add = (word, score) =>
            {
                if (word == typed) return;   // schon fertig getippt
                Scored old;
                int sc = (int)Math.Round(score);
                if (!merged.TryGetValue(word, out old) || sc > old.Freq) merged[word] = new Scored(word, sc);
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
                        if (lw != null) { score += 40 * Ln(lw.Count); form = lw.Form; }
                        int pc = st.PairCountOf(prev, c.Word);
                        if (pc > 0) score += 120 + 50 * Ln(pc);
                    }
                    add(form, score);
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
