using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tastatur
{
    /// <summary>Sucht deutschen Text im Quellcode, der nicht über L.T/L.F läuft. Daten und Dateiformate
    /// werden in der Zeile mit "// i18n-ok" markiert, Protokolleinträge (Log.Write) zählen nicht.</summary>
    internal static class I18nTests
    {
        private static readonly Regex Literal = new Regex(@"@?""((?:[^""\\]|\\.)*)""");
        private static readonly Regex German = new Regex(@"[äöüßÄÖÜ]|\b(der|die|das|und|nicht|ist|mit|für|oder|ein|eine|kein|keine|aus|nie|bitte|Bitte|hell|dunkel|Taste|Wort|Wörter|Fenster|Datei|Fehler|Speichern|Abbrechen|Passwort|Tresor|Makro|Sprache|Leertaste|Eingabe|gesperrt|Zeile|Gruppe|Beispiel|Unbekannt\w*)\b");
        private static readonly Regex Wrapped = new Regex(@"(L\.T|L\.F|Label)\(\s*$");

        public static List<string> Scan(string srcDir)
        {
            var hits = new List<string>();
            foreach (var f in Directory.GetFiles(srcDir, "*.cs").OrderBy(x => x))
            {
                var lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Contains("i18n-ok") || line.Contains("Log.Write") || line.Contains("sb.AppendLine(") || line.TrimStart().StartsWith("//")
                        || line.TrimStart().StartsWith("[assembly") || line.TrimStart().StartsWith("case \"")) continue;
                    string code = StripComment(line);
                    foreach (Match m in Literal.Matches(code))
                    {
                        string t = m.Groups[1].Value;
                        if (Wrapped.IsMatch(code.Substring(0, m.Index))) continue;
                        if (t.StartsWith(".") && t.Length <= 6) continue;          // Dateiendungen wie ".neu"
                        if (!Regex.IsMatch(t, "[A-Za-zÄÖÜäöüß]{3}")) continue;
                        if (German.IsMatch(t)) hits.Add(Path.GetFileName(f) + ":" + (i + 1) + "  " + t);
                    }
                }
            }
            return hits;
        }

        /// <summary>Zeilenkommentar abschneiden, aber nicht in Zeichenketten.</summary>
        private static string StripComment(string line)
        {
            bool inStr = false;
            for (int i = 0; i < line.Length - 1; i++)
            {
                char c = line[i];
                if (c == '"' && (i == 0 || line[i - 1] != '\\')) inStr = !inStr;
                else if (c == '\'' && !inStr && i + 2 < line.Length && line[i + 2] == '\'') i += 2;
                else if (!inStr && c == '/' && line[i + 1] == '/') return line.Substring(0, i);
            }
            return line;
        }

        public static void Run(string dePath, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.02: Keine deutschen Reste im Code ---");
            string src = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dePath)), "..", "src"));
            if (!Directory.Exists(src)) { Console.WriteLine("     (src nicht gefunden, übersprungen)"); return; }
            var hits = Scan(src);
            foreach (var h in hits.Take(15)) Console.WriteLine("     " + h);
            Check(hits.Count == 0, "kein deutscher Text außerhalb von L.T (" + hits.Count + " Treffer)");

            L.Init("en");
            Check(Macro.ColorLabel("grün") == "green" && Macro.ColorLabel("") == "none", "Farbnamen übersetzt");
            Check(new MacroStep(StepKind.Keys, "x").ToString().StartsWith("Keys"), "Schrittart übersetzt");
            var ex = MacroStore.Examples();
            Check(ex.Any(m => m.Name == "Copy all") && ex.All(m => !m.Steps.Any(s => s.Value.Contains("Morten") || s.Value.Contains("Strg"))), "Beispiel-Makros auf Englisch, ohne Namen");
            Check(MacroStore.Parse(MacroStore.Serialize(ex)).Count == ex.Count, "englische Beispiele lassen sich wieder einlesen");
            var en = MacroStore.Parse("- name: Hi\n  group: Web\n  color: green\n  hotkey: Ctrl+Shift+H\n  steps:\n    - keys: Ctrl+A\n    - wait: 100\n    - open: notepad.exe\n    - text: hello\n");
            Check(en.Count == 1 && en[0].Group == "Web" && en[0].Steps.Count == 4 && Macro.ColorValue(en[0].Color).HasValue, "englische Schlüssel in makros.yaml");
            string err = null;
            try { MacroStore.Parse("- name: x\n  foo: bar\n"); } catch (MacroFileException e2) { err = e2.Message; }
            Check(err != null && err.StartsWith("Line") && err.Contains("Unknown"), "Fehlermeldung auf Englisch: " + err);
            Check(new KeyCombo { Vk = InputSender.VK_BACK, Mods = Modifiers.Ctrl }.ToString() == "Ctrl+Backspace" && KeyCombo.Parse("Ctrl+Backspace") != null, "Tastennamen englisch und wieder lesbar");
            int cb;
            Check(Placeholders.Expand("{weekday}", new DateTime(2026, 11, 1), out cb) == "Sunday", "Platzhalter englisch: {weekday}");
            Check(Vault.SuggestName("", null) == "Password", "Namensvorschlag im Tresor übersetzt");
            L.Init("de");

            Console.WriteLine("\n--- 1.02: Anleitung in allen Sprachen ---");
            string manDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dePath)), "..", "Anleitung"));
            Func<string, int> headings = text =>
            {
                var ls = text.Replace("\r", "").Split('\n');
                int n = 0;
                for (int i = 1; i < ls.Length; i++) if (Regex.IsMatch(ls[i], "^[-=]{3,}$") && ls[i - 1].Trim().Length > 0) n++;
                return n;
            };
            string deText = File.ReadAllText(Path.Combine(manDir, Manual.FileName("de")));
            int deCount = headings(deText);
            foreach (var lang in L.Languages)
            {
                string f = Path.Combine(manDir, Manual.FileName(lang));
                string t = File.Exists(f) ? File.ReadAllText(f) : "";
                bool ai = lang == "de" || t.Contains("github.com/ichmml/nohandsneeded");
                Check(t.Length > 15000 && headings(t) == deCount && t.Contains(AppInfo.Version) && ai && !t.Contains("–"), lang + ": Anleitung mit " + headings(t) + " von " + deCount + " Abschnitten, Version, KI-Hinweis");
            }
            Manual.Resource = n => { string fp = Path.Combine(manDir, n); return File.Exists(fp) ? File.OpenRead(fp) : null; };
            string tmp = Path.Combine(Path.GetTempPath(), "tastatur-man-" + Guid.NewGuid().ToString("N"));
            string p1 = Manual.Find("tr", Path.Combine(tmp, "exe"), tmp);
            Check(p1 != null && p1.EndsWith("Anleitung-tr.txt") && File.ReadAllText(p1).Contains("Ekran klavyesi"), "eingebettete Anleitung wird in der Oberflächensprache ausgepackt");
            string p2 = Manual.Find("ja", Path.Combine(tmp, "exe"), tmp);
            Check(p2 != null && p2.EndsWith("Anleitung-en.txt"), "unbekannte Sprache: Englisch");
            try { Directory.Delete(tmp, true); } catch { }

            Check(new KeyCombo { Vk = InputSender.VK_BACK, Mods = Modifiers.Ctrl }.ToString() == "Strg+Rück" && MacroStore.Examples().Any(m => m.Name == "Alles kopieren"), "Deutsch wie bisher");
        }
    }
}
