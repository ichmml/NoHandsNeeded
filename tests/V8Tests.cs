using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tastatur
{
    internal static class V8Tests
    {
        public static void Run(string dePath, Action<bool, string> Check)
        {
            string resDir = Path.GetDirectoryName(Path.GetFullPath(dePath));
            Func<string, Stream> res = rn => { string fp = Path.Combine(resDir, rn); return File.Exists(fp) ? File.OpenRead(fp) : null; };
            string tmp = Path.Combine(Path.GetTempPath(), "tastatur-v8-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var lm = new LanguageManager(tmp, res);

            Console.WriteLine("\n--- Neue Sprachen ---");
            var expect = new Dictionary<string, string> { { "nl", "de" }, { "fr", "le" }, { "es", "que" }, { "it", "che" }, { "da", "og" }, { "pl", "nie" }, { "cs", "je" }, { "tr", "bir" }, { "ru", "и" } };
            foreach (var kv in expect)
            {
                var p = lm.Get(kv.Key);
                Scored sc;
                Check(p != null && p.Count > 20000 && p.TryGet(kv.Value, out sc) && sc.Freq >= 600, kv.Key + ": eingebaut (" + (p == null ? 0 : p.Count) + " Wörter), \"" + kv.Value + "\" häufig");
            }
            Check(lm.Get("es").Suggest("qu", 5, false).Contains("que"), "es: \"qu\" schlägt \"que\" vor");

            Console.WriteLine("\n--- Ohne Akzente ---");
            Func<string, string, int, List<string>> sug = (code, typed, n) => lm.Get(code).Suggest(typed, n, false);
            var z = sug("pl", "zolw", 8);
            Console.WriteLine("     pl zolw -> " + string.Join(", ", z));
            Check(z.Contains("żółw"), "\"zolw\" → \"żółw\"");
            Check(sug("fr", "cafe", 8).Contains("café"), "\"cafe\" → \"café\"");
            Check(sug("tr", "ucak", 8).Contains("uçak"), "\"ucak\" → \"uçak\"");
            Check(sug("es", "nino", 8).Contains("niño"), "\"nino\" → \"niño\"");
            Check(TextFold.Fold("Żółw") == "zolw" && TextFold.Fold("ёлка") == "елка" && TextFold.Fold("йод") == "йод", "Falten: polnisch, ё → е, й bleibt");
            var st = new LearningStore { Code = "de" };
            st.Learn("Café", null, false); st.Learn("Café", null, false);
            var set = new LanguageSet();
            set.SetActive(new[] { lm.Get("de") }, null);
            set.SetStore("de", st);
            Check(set.Suggest("cafe", 8, false).Contains("Café"), "auch gelernte Wörter ohne Akzent");

            Console.WriteLine("\n--- Layouts ---");
            foreach (var code in KeyLayouts.Codes)
            {
                var rows = KeyLayouts.Create(code);
                bool ok = rows.All(r => Math.Abs(r.Sum(k => k.Width) - 15f) < 0.01f);
                Check(ok, code + ": jede Reihe 15 Einheiten");
            }
            Check(KeyLayouts.Create("en", true).All(r => Math.Abs(r.Sum(k => k.Width) - 15f) < 0.01f), "Russisch lateinisch: 15 Einheiten");
            Check(KeyLayouts.Create("ru").SelectMany(r => r).Any(k => k.Kind == KeyKind.LayoutToggle && k.Label == "ABC")
                && KeyLayouts.Create("en", true).SelectMany(r => r).Any(k => k.Kind == KeyKind.LayoutToggle && k.Label == "АБВ"), "Russisch: Taste ABC/АБВ");
            Check(!KeyLayouts.Create("de").SelectMany(r => r).Any(k => k.Kind == KeyKind.LayoutToggle), "Deutsch ohne Umschalttaste");
            var pl = KeyLayouts.Create("pl").SelectMany(r => r).ToList();
            Check(pl.Any(k => k.Normal == "a" && k.AltGr == "ą") && pl.Any(k => k.Normal == "l" && k.AltGr == "ł") && pl.Any(k => k.Normal == "z" && k.AltGr == "ż"), "Polnisch: ą ł ż über AltGr");
            Check(KeyLayouts.Create("es").SelectMany(r => r).Any(k => k.Normal == "ñ" && k.Shifted == "Ñ"), "Spanisch: ñ als eigene Taste");
            Check(KeyLayouts.Create("fr")[1][1].Normal == "a" && KeyLayouts.Create("fr")[2][1].Normal == "q", "Französisch: AZERTY");
            Check(KeyLayouts.Create("tr")[1][8].Normal == "ı" && KeyLayouts.Create("tr")[2][11].Normal == "i", "Türkisch: ı und i");

            // Tottasten und Großschreibung
            var settings = new Settings { AutoCapitalize = false };
            var o = new FakeOutput();
            var e = new KeyboardEngine(o, set, settings);
            Func<List<List<KeyDef>>, string, KeyDef> key = (rows, c) => rows.SelectMany(r => r).First(k => k.Kind == KeyKind.Char && (k.Normal == c));
            var es = KeyLayouts.Create("es");
            e.DeadKeys = KeyLayouts.DeadKeys("es");
            var space = es.SelectMany(r => r).First(k => k.Kind == KeyKind.Space);
            e.Press(key(es, "´")); Check(e.DeadPending == '´' && o.Doc.Length == 0, "Tottaste wartet");
            e.Press(key(es, "e"));
            e.Press(key(es, "´")); e.Press(key(es, "x"));
            e.Press(key(es, "´")); e.Press(space);
            Check(o.Doc.ToString() == "é´x´", "´+e = é, ´+x = ´x, ´+Leertaste = ´: " + o.Doc);
            var fr = KeyLayouts.Create("fr");
            e.DeadKeys = KeyLayouts.DeadKeys("fr"); o.Doc.Length = 0;
            e.Press(key(fr, "^")); e.Press(key(fr, "e"));
            Check(o.Doc.ToString() == "ê", "Französisch ^ + e = ê");
            e.DeadKeys = KeyLayouts.DeadKeys("de");
            Check(e.DeadKeys.Count == 0, "Deutsch ohne Tottasten (wie bisher)");

            var cs = KeyLayouts.Create("cs");
            var ee = key(cs, "ě");
            Check(e.OutputFor(ee) == "ě", "Tschechisch ě");
            e.Shift = ModState.Once; Check(e.OutputFor(ee) == "2", "Shift + ě = 2"); e.Shift = ModState.Off;
            e.Caps = true; Check(e.OutputFor(ee) == "Ě", "Feststelltaste + ě = Ě"); e.Caps = false;
            var tr = KeyLayouts.Create("tr");
            e.LayoutCulture = new System.Globalization.CultureInfo("tr-TR");
            e.Shift = ModState.Once; Check(e.OutputFor(key(tr, "i")) == "İ" && e.OutputFor(key(tr, "ı")) == "I", "Türkisch: i → İ, ı → I"); e.Shift = ModState.Off;
            e.AltGr = ModState.Once; e.Shift = ModState.Once;
            Check(e.OutputFor(pl.First(k => k.Normal == "a")) == "Ą", "AltGr + Shift + a = Ą");
            e.AltGr = ModState.Off; e.Shift = ModState.Off;

            Console.WriteLine("\n--- Systemsprache ---");
            Func<string, bool> avail = c => LanguageManager.BuiltIns.Any(b => b.Code == c);
            var s1 = new Settings(); s1.ApplySystemLanguage("nl", avail);
            Check(s1.ActiveLanguages == "nl,en" && s1.LanguageMode == "nl", "Windows auf Niederländisch → nl,en");
            var s2 = new Settings(); s2.ApplySystemLanguage("en", avail);
            Check(s2.ActiveLanguages == "en" && s2.LanguageMode == "en", "Windows auf Englisch → nur en");
            var s3 = new Settings(); s3.ApplySystemLanguage("ja", avail);
            Check(s3.ActiveLanguages == "en", "unbekannte Sprache → Englisch");
            string f = Path.Combine(tmp, "einst.txt");
            File.WriteAllText(f, "einstellungen_version = 4\nsprachen_aktiv = de,en\nspenden_link = https://ko-fi.com/ichmml\n");
            var old = Settings.LoadFrom(f);
            Check(old.ActiveLanguages == "de,en" && !old.FirstStart, "bestehende Einstellungen bleiben");
            Check(old.DonateLink == "https://ko-fi.com/MDM3D", "Kaffee-Link wird auf MDM3D umgestellt");
            Check(!old.TourSeen, "Einführung kommt einmal");

            Console.WriteLine("\n--- Oberflächentexte ---");
            string texte = Path.Combine(resDir, "Texte");
            L.Resource = n => { string fp = Path.Combine(texte, n.Replace("texte_", "")); return File.Exists(fp) ? File.OpenRead(fp) : null; };
            var de = L.Load("de"); var en = L.Load("en");
            Check(de.Count > 300 && de.Keys.All(en.ContainsKey), "jeder Text hat eine englische Übersetzung (" + de.Count + ")");
            foreach (var lang in L.Languages.Where(x => x != "de"))
            {
                var d = L.Load(lang);
                bool noEmpty = File.ReadAllLines(Path.Combine(texte, lang + ".txt")).Where(l => !l.StartsWith("#") && l.Length > 0).All(l => l.Split('\t').Length == 2 && l.Split('\t')[1].Trim().Length > 0);
                Check(d.Count > 300 && noEmpty && d.Keys.All(de.ContainsKey), lang + ": " + d.Count + " Texte, keine leeren, keine unbekannten Schlüssel");
            }
            L.Init("ru");
            Check(L.T("Einstellungen") == "Настройки" && L.T("Datei unvollständig (") == "File incomplete (" && L.T("gibt es nicht") == "gibt es nicht", "Russisch, sonst Englisch, sonst Deutsch");
            Check(L.F("Deine Windows-Sprache ist {0}, die Tastatur ist auf {0} gestellt.", "X").Contains("X"), "Platzhalter");
            Check(KeyLayouts.Create("de")[4][1].Label == "Ctrl", "Tastenbeschriftung übersetzt (Strg → Ctrl)");
            L.Init("de");
            Check(L.T("Einstellungen") == "Einstellungen" && KeyLayouts.Create("de")[4][1].Label == "Strg", "Deutsch unverändert");

            Console.WriteLine("\n--- Einführung und Sperrbildschirm ---");
            var steps = TourSteps.Create("info", "Knopf", () => { });
            var known = new[] { "keyboard", "band", "case", "drag", "mods", "macro", "lang", "vault", "dwell", "settings", "collapse", "tray" };
            Check(steps.Count == 13 && steps.All(x => known.Contains(x.Target)) && steps[0].ExtraLabel == "Knopf" && steps[0].Text.Contains("info"), "13 Schritte mit gültigen Zielen");
            Check(new NoSuggestions().Suggest("ha", null, 5, false).Count == 0 && new NoSuggestions().SuggestNumbers("10", 5).Count == 0, "Sperrbildschirm: keine Vorschläge");
            Check(LockScreen.IsLockMode(new[] { "/sperrbildschirm" }) && !LockScreen.IsLockMode(new string[0]), "Sperrbildschirm-Modus erkennen");
            Check(!LockScreen.Ready, "ohne Installation und Signatur nicht bereit");

            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
