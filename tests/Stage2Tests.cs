using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Tastatur
{
    internal static class Stage2Tests
    {
        public static void Run(string dePath, Action<bool, string> Check)
        {
            string resDir = Path.GetDirectoryName(Path.GetFullPath(dePath));
            Func<string, Stream> res = rn => { string fp = Path.Combine(resDir, rn); return File.Exists(fp) ? File.OpenRead(fp) : null; };
            string tmp = Path.Combine(Path.GetTempPath(), "tastatur-s2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var lm = new LanguageManager(tmp, res);
            var de = lm.Get("de"); var en = lm.Get("en");

            Console.WriteLine("\n--- Lernen ---");
            var set = new LanguageSet();
            set.SetActive(new[] { de, en }, lm.LoadCustomWords());
            set.SetMode("de");
            set.SetStore("de", new LearningStore { Code = "de" });
            set.SetStore("en", new LearningStore { Code = "en" });

            Check(!LearningStore.IsLearnable("Xk9pQ2rT") && !LearningStore.IsLearnable("123456") && LearningStore.IsLearnable("Quadstick"), "Passwortartiges wird nicht gelernt");

            set.Learn("Wurbeltaste", null, false);
            Check(!set.Suggest("wurb", 8, false).Contains("Wurbeltaste"), "neues Wort nach 1-mal noch nicht vorgeschlagen");
            set.Learn("Wurbeltaste", null, false);
            Check(set.Suggest("wurb", 8, false).Contains("Wurbeltaste"), "nach 2-mal vorgeschlagen");
            set.Learn("Quadstick", null, false); set.Learn("Quadstick", null, false);
            var q = set.Suggest("quad", 8, false);
            Console.WriteLine("     quad -> " + string.Join(", ", q));
            Check(q.Count > 0 && q[0] == "Quadstick", "nach 2-mal ganz vorne, mit gelernter Schreibweise");
            set.MinNewCount = 1;
            set.Learn("Morteneske", null, false);
            Check(set.Suggest("morte", 8, false).Contains("Morteneske"), "Schwelle 1 wirkt sofort");
            set.MinNewCount = 2;
            set.Unlearn("Morteneske", null);
            Check(!set.Suggest("morte", 8, false).Contains("Morteneske"), "Backspace macht Lernen rückgängig");

            for (int i = 0; i < 3; i++) set.Learn("Rollstuhl", "mein", false);
            var next = set.Suggest("", "mein", 6, false);
            Console.WriteLine("     nach 'mein' -> " + string.Join(", ", next));
            Check(next.Contains("Rollstuhl"), "Folgewort wird nach 'mein' vorgeschlagen");

            set.SetForm("iPhone");
            set.Learn("iPhone", null, false); set.Learn("iPhone", null, false);
            Check(set.Suggest("iph", 5, false).Contains("iPhone"), "Schreibweise vom Groß/Klein-Knopf gemerkt");
            set.Learn("Haus", null, true);
            Check(!set.Suggest("hau", 8, false).Contains("haus") , "Satzanfang ändert die Schreibweise nicht");

            string lp = Path.Combine(tmp, "Lernen", "de.txt");
            set.Store("de").Save(lp);
            var loaded = LearningStore.Load(lp, "de");
            Check(loaded.Get("quadstick") != null && loaded.Get("quadstick").Count == 2 && loaded.PairCountOf("mein", "rollstuhl") == 3 && !loaded.Dirty,
                "Gelerntes speichern und laden");

            // Engine: Lernen beim Tippen
            var settings = new Settings();
            var o = new FakeOutput();
            var e = new KeyboardEngine(o, set, settings);
            var L = KeyLayouts.CreateGerman();
            var learned = new List<string>();
            e.WordFinished += (w, prev, start) => learned.Add((prev ?? "-") + ">" + w + (start ? "*" : ""));
            Func<string, KeyDef> find = c => L.SelectMany(r => r).First(k => k.Kind == KeyKind.Char && k.Normal == c);
            var space = L.SelectMany(r => r).First(k => k.Kind == KeyKind.Space);
            var enter = L.SelectMany(r => r).First(k => k.Kind == KeyKind.Special && k.Vk == InputSender.VK_RETURN);
            var back = L.SelectMany(r => r).First(k => k.Kind == KeyKind.Special && k.Vk == InputSender.VK_BACK);
            Action<string> type = str => { foreach (char c in str) e.Press(c == ' ' ? space : find(c.ToString())); };
            type("ich bin da. gut ");
            Console.WriteLine("     gelernt: " + string.Join(" ", learned));
            Check(learned.SequenceEqual(new[] { "->ich", "ich>bin", "bin>da", "->Gut*" }), "Wörter mit Vorgänger und Satzanfang gemeldet");

            Console.WriteLine("\n--- Passwortfeld ---");
            learned.Clear(); o.Doc.Length = 0;
            bool submitted = false;
            e.SecretSubmitted += () => submitted = true;
            e.PasswordMode = true;
            type("geheim");
            e.Press(back);
            type("x7");
            Check(o.Doc.ToString() == "geheix7", "im Passwortfeld wird normal getippt: " + o.Doc);
            Check(learned.Count == 0 && e.Suggestions().Count == 0, "kein Lernen, keine Vorschläge im Passwortfeld");
            e.Press(enter);
            Check(submitted, "Enter meldet abgeschicktes Passwort");
            Check(e.TakeSecret() == "geheix7" && e.TakeSecret() == null, "getipptes Passwort einmal abholbar");
            e.PasswordMode = false;
            type("weiter ");
            Check(learned.Count == 1, "danach wird wieder gelernt");

            // Aufnahme pausiert
            var rec = new RecordingOutput(new FakeOutput());
            rec.Start(); rec.SendText("abc"); rec.Paused = true; rec.SendText("PASSWORT"); rec.Paused = false; rec.SendText("d");
            var steps = rec.Stop();
            string all = string.Join("", steps.Where(s => s.Kind == StepKind.Text).Select(s => s.Value));
            Check(all == "abcd", "Makro-Aufnahme lässt Passwörter weg: " + all);

            Console.WriteLine("\n--- Aus Texten lernen ---");
            var set2 = new LanguageSet();
            set2.SetActive(new[] { de, en }, null);
            set2.SetStore("de", new LearningStore { Code = "de" });
            set2.SetStore("en", new LearningStore { Code = "en" });
            string docs = Path.Combine(tmp, "texte");
            Directory.CreateDirectory(Path.Combine(docs, ".obsidian"));
            File.WriteAllText(Path.Combine(docs, "notiz.md"),
                "---\ntags: test\n---\n# Überschrift\nMein Quadstick steuert die Bildschirmtastatur. Der Quadstick ist super.\n\nPasswort: Xk9pQ2rTzz\nMeine IBAN ist DE12 3456\n```\ncodeWort\n```\n[[Andere Notiz|Verlinkung]] siehe https://example.org/xyz\n");
            File.WriteAllText(Path.Combine(docs, "english.txt"), "The weather is nice today and the keyboard works with my quadstick.\n");
            File.WriteAllText(Path.Combine(docs, ".obsidian", "app.json"), "{\"versteckt\": true}");
            File.WriteAllText(Path.Combine(docs, "bild.png"), "kein text");
            var ir = TextImporter.Import(new[] { docs }, set2, null, CancellationToken.None);
            var sde = set2.Store("de"); var sen = set2.Store("en");
            Console.WriteLine("     " + ir.Files + " Dateien, " + ir.Words + " Wörter, DE " + sde.WordCount + ", EN " + sen.WordCount);
            Check(ir.Files == 2, "nur Textdateien, keine Punkt-Ordner");
            Check(sde.Get("quadstick") != null && sde.Get("quadstick").Count == 2, "Wörter aus Markdown gelernt");
            Check(sde.Get("xk9pq2rtzz") == null && sde.Get("iban") == null && sde.Get("passwort") == null, "Passwort- und IBAN-Zeilen übersprungen");
            Check(sde.Get("codewort") == null && sde.Get("tags") == null && sde.Get("example") == null, "Code, Frontmatter und Links nicht gelernt");
            Check(sde.Get("verlinkung") != null, "Linktext gelernt");
            Check(sen.Get("weather") != null && sde.Get("weather") == null, "englischer Text landet im englischen Speicher");
            Check(sde.PairCountOf("der", "quadstick") == 1, "Wortfolgen aus Texten gelernt");

            string mail = "From: a@b.de\nSubject: Hallo\nContent-Type: text/plain\n\nLiebe Grüße aus Kiel\n\nAm 1.1.2026 um 10:00 schrieb Bob:\n> zitierter Quatsch\n";
            string mt = TextImporter.FromMail(mail);
            Check(mt.Contains("Grüße aus Kiel") && !mt.Contains("Subject") && !mt.Contains("zitierter"), "Mail: ohne Kopf und Zitate");
            string ht = TextImporter.FromHtml("<html><style>p{x:y}</style><p>Hallo&nbsp;Welt</p><script>var a;</script></html>");
            Check(ht.Contains("Hallo") && ht.Contains("Welt") && !ht.Contains("var a") && !ht.Contains("x:y"), "HTML: nur Text");

            Console.WriteLine("\n--- Tresor ---");
            var entries = new List<VaultEntry>
            {
                new VaultEntry { Name = "Gmail", User = "morten@x.de", Password = "a\tb=c\nd", Match = "gmail, google" },
                new VaultEntry { Name = "Bank", Password = "S1cher!", Match = "sparkasse" }
            };
            var back2 = Vault.Deserialize(Vault.Serialize(entries));
            Check(back2.Count == 2 && back2[0].Password == "a\tb=c\nd" && back2[0].User == "morten@x.de" && back2[1].Match == "sparkasse", "Einträge mit Sonderzeichen sicher speicherbar");
            Check(entries[0].Score("Posteingang - Gmail - Google Chrome", "chrome") > entries[1].Score("Posteingang - Gmail - Google Chrome", "chrome"), "passender Eintrag zuerst");

            bool allOk = true;
            for (int i = 0; i < 200; i++)
            {
                string g = Vault.Generate(16, true);
                if (g.Length != 16 || !g.Any(char.IsUpper) || !g.Any(char.IsLower) || !g.Any(char.IsDigit) || g.All(char.IsLetterOrDigit) || g.IndexOfAny("lIO01".ToCharArray()) >= 0) allOk = false;
            }
            Check(allOk, "Generator: 16 Zeichen, alle Gruppen, keine verwechselbaren Zeichen");
            Check(Vault.Generate(12, false).All(char.IsLetterOrDigit), "Generator ohne Sonderzeichen");
            Check(Vault.Generate(4, true).Length == 8 && Vault.Generate(200, true).Length == 64, "Generator begrenzt Länge");
            string sn = Vault.SuggestName("Anmelden – Google Konten - Google Chrome", "chrome");
            Console.WriteLine("     Name-Vorschlag: " + sn);
            Check(sn.IndexOf("Google", StringComparison.OrdinalIgnoreCase) >= 0 && sn.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) < 0, "Name aus Fenstertitel ohne Browsername");
            Check(PasswordWatcher.IsProtectedState(0x20000000 | 0x100000) && !PasswordWatcher.IsProtectedState(0x100000), "Passwortfeld-Status erkannt");

            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
