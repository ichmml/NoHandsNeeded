using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tastatur
{
    /// <summary>1.2.04: Wort löschen, Groß-/Kleinschreibung der Vorschläge, Korrekturvorschläge.</summary>
    internal static class V1204Tests
    {
        public static void Run(string dePath, Action<bool, string> Check)
        {
            string resDir = Path.GetDirectoryName(Path.GetFullPath(dePath));
            Func<string, Stream> res = rn => { string fp = Path.Combine(resDir, rn); return File.Exists(fp) ? File.OpenRead(fp) : null; };
            string tmp = Path.Combine(Path.GetTempPath(), "tastatur-1204-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var lm = new LanguageManager(tmp, res);
            var de = lm.Get("de"); var en = lm.Get("en");

            Layout(Check);
            DeleteWordTests(de, Check);
            CaseTests(de, en, lm, Check);
            CorrectionTests(de, en, lm, Check);

            try { Directory.Delete(tmp, true); } catch { }
        }

        private static KeyDef Find(List<List<KeyDef>> rows, string normal)
        {
            return rows.SelectMany(r => r).First(k => k.Kind == KeyKind.Char && k.Normal == normal);
        }

        private static void Layout(Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.04: Taste Wort löschen ---");
            foreach (var code in KeyLayouts.Codes)
            {
                var L = KeyLayouts.Create(code);
                var r0 = L[0]; var r1 = L[1];
                float bs = r0.Take(r0.Count - 1).Sum(k => k.Width);          // ⌫ fängt hier an
                int di = r1.FindIndex(k => k.Kind == KeyKind.DeleteWord);
                float dx = di < 0 ? -1 : r1.Take(di).Sum(k => k.Width);
                bool ok = di >= 0 && r0[r0.Count - 1].Vk == InputSender.VK_BACK && dx >= bs - 0.01f && dx + r1[di].Width <= 15.01f
                    && Math.Abs(r1.Sum(k => k.Width) - 15f) < 0.01f;
                if (!ok || code == "de") Check(ok, "Wort löschen direkt unter der Rücktaste (" + code + ")");
            }
            var nav = KeyLayouts.CreateNav();
            Check(nav.SelectMany(r => r).Any(k => k.Kind == KeyKind.DeleteWord) && nav.All(r => Math.Abs(r.Sum(k => k.Width) - 15f) < 0.01f),
                "Wort löschen auch auf der Fn-Ebene");
            Check(KeyLayouts.CreateGerman()[1].Any(k => k.Kind == KeyKind.Special && k.Vk == InputSender.VK_DELETE), "Entf bleibt");

            Check(KeyboardEngine.WordLengthAtEnd("hallo welt ") == 5, "Länge: Wort samt Leerzeichen");
            Check(KeyboardEngine.WordLengthAtEnd("hallo welt") == 4, "Länge: angefangenes Wort");
            Check(KeyboardEngine.WordLengthAtEnd("das Haus. ") == 6, "Länge: Satzzeichen gehört dazu");
            Check(KeyboardEngine.WordLengthAtEnd("ja!? ") == 5, "Länge: mehrere Satzzeichen");
            Check(KeyboardEngine.WordLengthAtEnd("eins\n") == 1, "Länge: Zeilenumbruch allein");
            Check(KeyboardEngine.WordLengthAtEnd("eins   ") == 7, "Länge: mehrere Leerzeichen");
            Check(KeyboardEngine.WordLengthAtEnd("") == 0, "Länge: leer = unbekannt");
            Check(KeyboardEngine.WordLengthAtEnd("a, ") == 3 && KeyboardEngine.WordLengthAtEnd(" , ") == 2, "Länge: nur Satzzeichen");
        }

        private static void DeleteWordTests(Predictor p, Action<bool, string> Check)
        {
            var settings = new Settings();
            var o = new FakeOutput();
            var e = new KeyboardEngine(o, p, settings);
            var L = KeyLayouts.CreateGerman();
            var del = L.SelectMany(r => r).First(k => k.Kind == KeyKind.DeleteWord);
            var space = L.SelectMany(r => r).First(k => k.Kind == KeyKind.Space);
            Action<string> type = str => { foreach (char c in str) { if (c == ' ') e.Press(space); else e.Press(Find(L, c.ToString())); } };
            var reopened = new List<string>();
            var finished = new List<string>();
            e.WordReopened += (w, prev) => reopened.Add(w);
            e.WordFinished += (w, prev, st) => finished.Add(w);

            e.ResetWord(false);
            type("hallo welt ");
            finished.Clear();
            e.Press(del);
            Check(o.Doc.ToString() == "hallo ", "Wort löschen: letztes Wort samt Leerzeichen weg: '" + o.Doc + "'");
            Check(reopened.Contains("welt") && finished.Count == 0, "gelöschtes Wort wird nicht gelernt, nichts neu gelernt");
            Check(e.CaseTarget == "hallo" && e.CurrentWord == "", "danach kennt Aa das Wort davor");
            e.Press(del);
            Check(o.Doc.ToString() == "", "zweiter Druck löscht das Wort davor: '" + o.Doc + "'");

            o.Doc.Clear(); e.ResetWord(false);
            type("hallo we");
            e.Press(del);
            Check(o.Doc.ToString() == "hallo " && e.CurrentWord == "", "angefangenes Wort löschen: '" + o.Doc + "'");
            type("du");
            Check(o.Doc.ToString() == "hallo du" && e.PreviousWord == "hallo", "weiterschreiben, Wort davor bleibt bekannt");

            o.Doc.Clear(); e.ResetWord(true);
            type("gut. ja");
            e.Press(Find(L, "."));
            e.Press(space);
            e.Press(del);
            Check(o.Doc.ToString() == "Gut. ", "Satzzeichen am Wort werden mit gelöscht: '" + o.Doc + "'");
            type("x");
            Check(o.Doc.ToString() == "Gut. X", "danach wieder Satzanfang: '" + o.Doc + "'");

            // Rückfall: nichts bekannt
            o.Doc.Clear(); o.Log.Clear(); e.ResetWord(false);
            e.Press(del);
            Check(o.Log.Count == 2 && o.Log[0] == "VK:25+Shift, Ctrl" && o.Log[1] == "VK:08+None", "unbekannt: Strg+Shift+Pfeil links, dann Rücktaste: " + string.Join(" | ", o.Log));
            Check(!o.Log.Any(x => x.StartsWith("VK:08+Ctrl")), "kein Strg+Rücktaste");

            // Strg wurde vorher gedrückt: wirkt nicht auf Wort löschen und ist danach aus
            o.Doc.Clear(); o.Log.Clear(); e.ResetWord(false);
            type("eins zwei");
            e.Press(L.SelectMany(r => r).First(k => k.Label == "Strg"));
            e.Press(del);
            Check(o.Doc.ToString() == "eins " && e.Ctrl == ModState.Off, "Strg vorher: trotzdem nur das Wort, Strg wieder aus");

            // Markierung mit Shift + Pfeilen: Markierung löschen
            o.Doc.Clear(); o.Log.Clear(); e.ResetWord(false);
            type("abc");
            e.Press(L.SelectMany(r => r).First(k => k.Label == "⇧ Shift"));
            e.Press(L.SelectMany(r => r).First(k => k.Label == "←"));
            o.Log.Clear();
            e.Press(del);
            Check(o.Log.Count == 1 && o.Log[0] == "VK:08+None", "nach Markieren mit Shift + Pfeil: nur Rücktaste: " + string.Join(" | ", o.Log));

            // Passwortfeld
            o.Doc.Clear(); o.Log.Clear(); e.ResetWord(false);
            e.PasswordMode = true;
            type("geheim");
            e.Press(del);
            Check(o.Doc.ToString() == "" && e.SecretLength == 0, "Passwortfeld: Getipptes gelöscht");
            e.PasswordMode = false;
        }

        private static void CaseTests(Predictor de, Predictor en, LanguageManager lm, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.04: Groß-/Kleinschreibung der Vorschläge ---");
            Scored sc;
            Check(de.HasExact("Zeit") && !de.HasExact("zeit"), "Wortliste: Zeit groß");
            Check(de.HasExact("Platz") && de.HasExact("Ende") && de.HasExact("Vertrag"), "Wortliste: Platz, Ende, Vertrag groß");
            Check(de.HasExact("essen") && de.HasExact("Essen") && de.HasExact("leben") && de.HasExact("Leben"), "Wortliste: essen und Essen, leben und Leben");
            Check(de.HasSeveralForms("essen") && !de.HasSeveralForms("Zeit"), "mehrere Schreibweisen erkannt");
            Check(de.TryGet("Essen", out sc) && sc.Word == "Essen" && de.TryGet("essen", out sc) && sc.Word == "essen", "Nachschlagen findet die genau passende Schreibweise");
            Check(de.CapitalFormOf("zeit") == "Zeit" && de.CapitalFormOf("essen") == null && de.CapitalFormOf("und") == null, "klein getipptes Substantiv: richtige Schreibweise");

            var s = de.Suggest("zei", 8, false);
            Console.WriteLine("     zei -> " + string.Join(", ", s));
            Check(s.Contains("Zeit") && !s.Contains("zeit"), "zei: Zeit groß, nicht klein");
            s = de.Suggest("zeit", 5, false);
            Check(s.Count > 0 && s[0] == "Zeit", "zeit getippt: Zeit ganz vorn");

            var set = new LanguageSet();
            set.SetActive(new[] { de, en }, lm.LoadCustomWords());
            set.SetMode("de");
            var store = new LearningStore { Code = "de" };
            set.SetStore("de", store);
            s = set.Suggest("ess", 10, false);
            Console.WriteLine("     ess -> " + string.Join(", ", s));
            Check(s.Contains("essen") && s.Contains("Essen"), "ess: beide Schreibweisen als eigene Vorschläge");
            s = set.Suggest("ess", 10, true);
            Check(s.Count(w => w == "Essen") == 1 && !s.Contains("essen"), "Satzanfang: nur einmal Essen");

            // Lernen: klein getipptes Substantiv verdirbt die Schreibweise nicht
            for (int i = 0; i < 3; i++) set.Learn("zeit", null, false);
            Check(store.Get("zeit").Form == "Zeit", "zeit klein getippt: gelernt wird Zeit");
            s = set.Suggest("zei", 8, false);
            Check(s.Contains("Zeit") && !s.Contains("zeit"), "nach dem Lernen weiter Zeit groß");
            for (int i = 0; i < 4; i++) set.Learn("essen", null, false);
            s = set.Suggest("ess", 10, false);
            Check(s.Contains("Essen") && s.IndexOf("essen") >= 0 && s.IndexOf("essen") < s.IndexOf("Essen"), "oft gelerntes essen vorn, Essen bleibt dabei: " + string.Join(", ", s));
            for (int i = 0; i < 2; i++) set.Learn("Platz", null, false);
            set.Learn("platz", null, false);
            Check(store.Get("platz").Form == "Platz", "einmal klein getippt ändert nichts");

            // bewusst mit der Groß/Klein-Taste gewählt: bleibt
            set.SetForm("haus");
            set.Learn("haus", null, false);
            Check(store.Get("haus").Form == "haus" && store.Get("haus").Chosen, "mit Aa gewählte Kleinschreibung bleibt");

            // alte, falsch gelernte Kleinschreibung bereinigen
            var old = LearningStore.FromText("w\tvertrag\t5\nw\tzeit\t3\nw\thaus\t4\tgewählt\nw\tessen\t2\n", "de");
            Check(old.Get("haus").Chosen && !old.Get("vertrag").Chosen, "gewählte Schreibweise wird gelesen");
            set.SetStore("de", old);
            Check(old.Get("vertrag").Form == "Vertrag" && old.Get("zeit").Form == "Zeit", "alte falsche Kleinschreibung bereinigt");
            Check(old.Get("haus").Form == "haus" && old.Get("essen").Form == "essen", "gewählte und echte Kleinschreibung bleiben");
            Check(old.Dirty, "Bereinigung wird gespeichert");
            var again = LearningStore.FromText(old.ToText(), "de");
            Check(again.Get("haus").Chosen && again.Get("haus").Form == "haus" && again.Get("vertrag").Form == "Vertrag", "speichern und laden mit gewählter Schreibweise");
            var merged = new LearningStore { Code = "de" };
            merged.MergeFrom(again);
            Check(merged.Get("haus").Chosen, "Import übernimmt gewählte Schreibweise");

            // Wortfolgen: Bonus nur für die passende Schreibweise
            set.SetStore("de", new LearningStore { Code = "de" });
            s = set.Suggest("ess", "beim", 10, false);
            Console.WriteLine("     beim ess -> " + string.Join(", ", s));
            Check(s.Count > 0 && s[0] == "Essen", "nach 'beim': Essen vorn");
            s = set.Suggest("", "freundlichen", 6, false);
            Check(s.Contains("Grüßen"), "freundlichen -> Grüßen bleibt groß");

            // Englisch wird nicht künstlich großgeschrieben
            set.SetMode("en");
            s = set.Suggest("tim", 6, false);
            Check(s.Contains("time") && !s.Contains("Time"), "Englisch: time klein");
            set.SetMode("de");
        }

        private static void CorrectionTests(Predictor de, Predictor en, LanguageManager lm, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.04: Korrekturvorschläge ---");
            Check(Correction.IsNeighbor('a', 's') && Correction.IsNeighbor('z', 'u') && Correction.IsNeighbor('y', 'a') && !Correction.IsNeighbor('a', 'p'), "QWERTZ-Nachbartasten");
            Check(Correction.Variants("hasu").Any(v => v.Text == "haus"), "Vertauschung wird erzeugt");
            Check(Correction.Variants("gruesse").Any(v => v.Text == "grüße" && v.Cost == 0), "ue und ss werden ü und ß");

            Func<string, List<string>> fix = t => de.Corrections(t, 3, false, false);
            var c = fix("hasu");
            Console.WriteLine("     hasu -> " + string.Join(", ", c));
            Check(c.Count > 0 && c[0] == "Haus", "hasu -> Haus");
            c = fix("gruesse");
            Console.WriteLine("     gruesse -> " + string.Join(", ", c));
            Check(c.Contains("Grüße"), "gruesse -> Grüße");
            c = fix("tsch");
            Console.WriteLine("     tsch -> " + string.Join(", ", c));
            c = fix("treffne");
            Console.WriteLine("     treffne -> " + string.Join(", ", c));
            Check(c.Contains("treffen") || c.Contains("Treffen"), "treffne -> treffen");
            c = fix("schreibtsich");
            Console.WriteLine("     schreibtsich -> " + string.Join(", ", c));
            Check(c.Contains("Schreibtisch"), "schreibtsich -> Schreibtisch");
            c = fix("vielleciht");
            Console.WriteLine("     vielleciht -> " + string.Join(", ", c));
            Check(c.Contains("vielleicht"), "vielleciht -> vielleicht");
            c = fix("Hasu");
            Check(c.Count > 0 && c[0] == "Haus", "Großbuchstabe getippt: Haus");
            Check(fix("haus").Count == 0 && fix("hau").Count == 0 && fix("stra").Count == 0, "keine Korrektur für bekannte Wörter und Anfänge");
            Check(fix("ha").Count == 0 && fix("a1b").Count == 0, "keine Korrektur bei zwei Buchstaben oder Ziffern");
            Check(de.Corrections("hasu", 3, true, false)[0] == "Haus", "Satzanfang groß");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) { fix("schreibtsich"); fix("qxzvb"); fix("hasu"); }
            sw.Stop();
            Console.WriteLine("     60 Korrekturen: " + sw.ElapsedMilliseconds + " ms");
            Check(sw.ElapsedMilliseconds < 3000, "Korrektur ist schnell genug");

            var set = new LanguageSet();
            set.SetActive(new[] { de, en }, lm.LoadCustomWords());
            set.SetMode("de");
            set.SetStore("de", new LearningStore { Code = "de" });
            Check(set.Corrections("hasu", 3, false, false).FirstOrDefault() == "Haus", "über die Sprachen: hasu -> Haus");
            set.Learn("Hasubert", null, false); set.Learn("Hasubert", null, false);
            Check(set.Corrections("hasu", 3, false, false).Count == 0, "gelerntes Wort mit diesem Anfang: keine Korrektur");
            Check(set.Corrections("hasu", 3, false, true).Count == 1, "fertiges Wort: genau ein klarer Treffer: " + string.Join(", ", set.Corrections("hasu", 3, false, true)));
            set.SetMode(LanguageSet.MixKey);
            Check(set.Corrections("tme", 3, false, false).Count >= 0 && set.Corrections("time", 3, false, false).Count == 0, "gemischt: englisches Wort ist kein Tippfehler");
            set.SetMode("de");

            // in der Tastatur (frisch, ohne das oben gelernte Hasubert)
            set = new LanguageSet();
            set.SetActive(new[] { de, en }, lm.LoadCustomWords());
            set.SetMode("de");
            set.SetStore("de", new LearningStore { Code = "de" });
            var settings = new Settings();
            var o = new FakeOutput();
            var e = new KeyboardEngine(o, set, settings);
            var L = KeyLayouts.CreateGerman();
            var space = L.SelectMany(r => r).First(k => k.Kind == KeyKind.Space);
            Action<string> type = str => { foreach (char ch in str) { if (ch == ' ') e.Press(space); else e.Press(Find(L, ch.ToString())); } };

            e.ResetWord(false);
            type("das hasu");
            var sug = e.Suggestions();
            Console.WriteLine("     'das hasu' -> " + string.Join(", ", sug));
            Check(sug.Count > 0 && sug[0] == "Haus" && e.IsCorrection("Haus"), "Korrektur vorn und markiert");
            Check(o.Doc.ToString() == "das hasu", "nichts automatisch ersetzt");
            e.PressSuggestion("Haus");
            Check(o.Doc.ToString() == "das Haus ", "Klick ersetzt das ganze Wort: '" + o.Doc + "'");

            o.Doc.Clear(); e.ResetWord(false);
            type("das hasu ");
            sug = e.Suggestions();
            Console.WriteLine("     'das hasu ' -> " + string.Join(", ", sug));
            Check(sug.Count > 0 && sug[0] == "Haus" && e.IsCorrection("Haus"), "nach dem Leerzeichen bleibt die Korrektur stehen");
            e.PressSuggestion("Haus");
            Check(o.Doc.ToString() == "das Haus ", "Korrektur ersetzt das Wort vor dem Leerzeichen: '" + o.Doc + "'");
            Check(e.CaseTarget == "Haus", "danach ist Haus das letzte Wort");
            type("ist");
            Check(o.Doc.ToString() == "das Haus ist", "normal weiterschreiben");

            o.Doc.Clear(); e.ResetWord(false);
            type("das hasu k");
            sug = e.Suggestions();
            Check(!sug.Contains("Haus") || !e.IsCorrection("Haus"), "beim Weiterschreiben verschwindet die Korrektur");

            o.Doc.Clear(); e.ResetWord(false);
            type("das haus ");
            e.Suggestions();
            Check(!e.IsCorrection("Haus") && !e.IsCorrection("haus"), "bekanntes Wort: keine Korrektur");

            settings.CorrectionSuggestions = false;
            o.Doc.Clear(); e.ResetWord(false);
            type("das hasu");
            sug = e.Suggestions();
            Check(!e.IsCorrection("Haus"), "abgeschaltet: keine Korrektur");
            settings.CorrectionSuggestions = true;

            var st = new Settings();
            Check(st.CorrectionSuggestions, "Standard: an");
            st.CorrectionSuggestions = false;
            string path = Path.Combine(Path.GetTempPath(), "tastatur-1204-einst-" + Guid.NewGuid().ToString("N") + ".txt");
            st.SaveTo(path);
            var st2 = Settings.LoadFrom(path);
            Check(!st2.CorrectionSuggestions, "Einstellung wird gespeichert");
            try { File.Delete(path); } catch { }
        }
    }
}
