using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Tastatur
{
    internal class ImportResult
    {
        public int Files, Skipped, Words, Sentences, Numbers;
        public Dictionary<string, int> WordsPerLanguage = new Dictionary<string, int>();
        public List<string> Errors = new List<string>();
    }

    /// <summary>
    /// Lernt aus vorhandenen Texten (Ordner mit .txt, .md, .docx, .html, .eml, .mbox).
    /// Gespeichert wird nur die Wortstatistik, keine Texte. Zeilen mit Passwort-Hinweisen werden übersprungen.
    /// </summary>
    internal static class TextImporter
    {
        public static readonly string[] Extensions = { ".txt", ".md", ".docx", ".html", ".htm", ".eml", ".mbox" };
        private const long MaxFileBytes = 30L * 1024 * 1024;

        private static readonly Regex WordRe = new Regex(@"[\p{L}\p{N}][\p{L}\p{N}'’\-]*", RegexOptions.Compiled);
        private static readonly Regex SecretLine = new Regex(@"passw|kennw|password|\bpin\b|zugangsdaten|login|iban|\bbic\b|token|api[- _]?key|secret",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NumberRe = new Regex(
            @"(?<![\w.+/-])(?:\d{1,2}([./-])\d{1,2}\1(?:\d{4}|\d{2})|\d{4}-\d{2}-\d{2}|(?:\+|0|\(0)[\d()/\-]*\d(?: [\d()/\-]*\d)*|\d{5})(?![\w.,/-]?\d)(?: (?=(\p{L})))?",
            RegexOptions.Compiled);
        private static readonly Regex UrlOrMail = new Regex(@"(https?://|www\.)\S+|\S+@\S+\.\S+", RegexOptions.Compiled);

        // ---------- Text aus Dateien holen ----------

        public static string ExtractText(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".docx": return FromDocx(path);
                case ".html": case ".htm": return FromHtml(File.ReadAllText(path, Encoding.UTF8));
                case ".md": return FromMarkdown(File.ReadAllText(path, Encoding.UTF8));
                case ".eml": case ".mbox": return FromMail(File.ReadAllText(path, Encoding.UTF8));
                default: return File.ReadAllText(path, Encoding.UTF8);
            }
        }

        public static string FromDocx(string path)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                var entry = zip.GetEntry("word/document.xml");
                if (entry == null) return "";
                string xml;
                using (var r = new StreamReader(entry.Open(), Encoding.UTF8)) xml = r.ReadToEnd();
                xml = Regex.Replace(xml, @"</w:p>", "\n");
                xml = Regex.Replace(xml, @"<w:tab/>|<w:br/>", " ");
                xml = Regex.Replace(xml, @"<[^>]+>", "");
                return WebUtility.HtmlDecode(xml);
            }
        }

        public static string FromHtml(string html)
        {
            html = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<(br|p|div|li|tr|h\d)[^>]*>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<[^>]+>", " ");
            return WebUtility.HtmlDecode(html);
        }

        public static string FromMarkdown(string md)
        {
            md = Regex.Replace(md, @"\A---\s*\n.*?\n---\s*\n", "", RegexOptions.Singleline);   // Frontmatter
            md = Regex.Replace(md, @"```.*?```", "\n", RegexOptions.Singleline);              // Codeblöcke
            md = Regex.Replace(md, @"`[^`]*`", " ");
            md = Regex.Replace(md, @"!\[\[[^\]]*\]\]|!\[[^\]]*\]\([^)]*\)", " ");              // Bilder
            md = Regex.Replace(md, @"\[\[([^\]|]*\|)?([^\]]*)\]\]", "$2");                     // [[Link|Text]]
            md = Regex.Replace(md, @"\[([^\]]*)\]\([^)]*\)", "$1");                            // [Text](url)
            md = Regex.Replace(md, @"^\s*(#+|[-*+]|\d+\.|>|\|)\s*", "", RegexOptions.Multiline);
            md = md.Replace("**", "").Replace("__", "").Replace("|", " ");
            return md;
        }

        /// <summary>Mails: Kopfzeilen, angehängte Dateien und zitierte Antworten (Zeilen mit &gt;) weglassen.</summary>
        public static string FromMail(string raw)
        {
            var sb = new StringBuilder();
            var lines = raw.Replace("\r\n", "\n").Split('\n');
            bool inHeader = true, skipPart = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("From ") && line.Length > 5 && (line.Contains("@") || char.IsDigit(line[5]))) { inHeader = true; skipPart = false; continue; }
                if (inHeader) { if (line.Trim().Length == 0) inHeader = false; continue; }
                if (line.StartsWith("--")) { skipPart = false; inHeader = true; continue; }   // neuer MIME-Teil hat eigenen Kopf
                if (line.StartsWith("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase)) { skipPart = true; continue; }
                if (skipPart) continue;
                if (Regex.IsMatch(line, @"^[A-Za-z0-9+/=]{60,}$")) continue;
                if (line.StartsWith(">")) continue;
                if (Regex.IsMatch(line, @"^(Am|On) .{5,120}(schrieb|wrote).*:$")) continue;
                sb.Append(line.Replace("=\n", "")).Append('\n');
            }
            string text = sb.ToString();
            if (text.Contains("<") && Regex.IsMatch(text, @"<(p|div|br|html)", RegexOptions.IgnoreCase)) text = FromHtml(text);
            text = Regex.Replace(text, @"=([0-9A-F]{2})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
            return text;
        }

        // ---------- Lernen ----------

        /// <summary>Text in Sätze und Wörter zerlegen und lernen. Sprache wird je Absatz bestimmt.</summary>
        public static void LearnText(string text, LanguageSet langs, ImportResult result)
        {
            var paragraphs = Regex.Split(text.Replace("\r", ""), @"\n\s*\n");
            foreach (var para in paragraphs)
            {
                var lines = para.Split('\n').Where(l => !SecretLine.IsMatch(l)).Select(l => UrlOrMail.Replace(l, " "));
                string clean = string.Join("\n", lines);
                if (clean.Trim().Length == 0) continue;
                if (langs.NumbersEnabled)
                    foreach (Match nm in NumberRe.Matches(clean))
                    {
                        bool allow = NumberRules.AllowSecret;
                        NumberRules.AllowSecret = false;   // aus Texten nie PINs oder Kartennummern
                        string f;
                        try { f = NumberRules.Classify(nm.Value.TrimEnd(' '), nm.Groups[2].Success); }
                        finally { NumberRules.AllowSecret = allow; }
                        if (f != null) { langs.Numbers.Learn(f); result.Numbers++; }
                    }
                var store = PickLanguage(clean, langs);
                if (store == null) continue;
                foreach (var sentence in Regex.Split(clean, @"[.!?;:\n]+"))
                {
                    string prev = null;
                    bool first = true;
                    int n = 0;
                    foreach (Match m in WordRe.Matches(sentence))
                    {
                        string w = m.Value.Replace('’', '\'').Trim('-', '\'');
                        if (!LearningStore.IsLearnable(w)) { prev = null; first = false; continue; }
                        store.Learn(w, prev, first);
                        prev = w;
                        first = false;
                        n++;
                    }
                    if (n > 0)
                    {
                        result.Sentences++;
                        result.Words += n;
                        int c;
                        result.WordsPerLanguage.TryGetValue(store.Code, out c);
                        result.WordsPerLanguage[store.Code] = c + n;
                    }
                }
            }
        }

        /// <summary>Welche aktive Sprache passt zum Text? Die, deren Grundwortschatz die meisten Wörter kennt.</summary>
        public static LearningStore PickLanguage(string text, LanguageSet langs)
        {
            var words = WordRe.Matches(text).Cast<Match>().Select(m => m.Value).Take(200).ToList();
            if (words.Count == 0 || langs.Active.Count == 0) return null;
            Predictor best = null; int bestHits = -1;
            foreach (var p in langs.Active)
            {
                int hits = 0;
                Scored sc;
                foreach (var w in words) if (p.TryGet(w, out sc) && sc.Freq >= 400) hits++;
                if (hits > bestHits) { bestHits = hits; best = p; }
            }
            return best == null ? null : langs.Store(best.Code);
        }

        public static List<string> FindFiles(string folder)
        {
            var list = new List<string>();
            var stack = new Stack<string>();
            stack.Push(folder);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string name = Path.GetFileName(dir);
                if (dir != folder && (name.StartsWith(".") || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))) continue;
                try
                {
                    foreach (var f in Directory.GetFiles(dir))
                        if (Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())) list.Add(f);
                    foreach (var d in Directory.GetDirectories(dir)) stack.Push(d);
                }
                catch { }
            }
            return list;
        }

        /// <summary>Alle passenden Dateien eines Ordners (oder eine einzelne Datei) einlesen.</summary>
        public static ImportResult Import(IEnumerable<string> paths, LanguageSet langs, Action<int, int, string> progress, CancellationToken cancel)
        {
            var result = new ImportResult();
            var files = new List<string>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p)) files.AddRange(FindFiles(p));
                else if (File.Exists(p)) files.Add(p);
            }
            for (int i = 0; i < files.Count; i++)
            {
                if (cancel.IsCancellationRequested) break;
                string f = files[i];
                if (progress != null) progress(i, files.Count, Path.GetFileName(f));
                try
                {
                    if (new FileInfo(f).Length > MaxFileBytes) { result.Skipped++; continue; }
                    string text = ExtractText(f);
                    LearnText(text, langs, result);
                    result.Files++;
                }
                catch (Exception e)
                {
                    result.Skipped++;
                    if (result.Errors.Count < 20) result.Errors.Add(Path.GetFileName(f) + ": " + e.Message);
                }
            }
            if (progress != null) progress(files.Count, files.Count, "");
            Log.Write("Aus Texten gelernt: " + result.Files + " Dateien, " + result.Words + " Wörter, " + result.Skipped + " übersprungen");
            return result;
        }
    }
}
