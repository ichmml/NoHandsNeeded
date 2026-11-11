using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace Tastatur
{
    internal static class LangTests
    {
        public static void Run(string dePath, Action<bool, string> Check)
        {
            string resDir = Path.GetDirectoryName(Path.GetFullPath(dePath));
            Func<string, Stream> res = rn => { string f = Path.Combine(resDir, rn); return File.Exists(f) ? File.OpenRead(f) : null; };

            Console.WriteLine("\n--- Sprachen ---");
            string tmp = Path.Combine(Path.GetTempPath(), "tastatur-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var lm = new LanguageManager(tmp, res);
            var inst = lm.Installed();
            Check(inst.Count == 11 && inst[0].Code == "de" && inst[1].Code == "en" && inst.All(l => l.BuiltIn), "eingebaut: 11 Sprachen, DE und EN vorne");
            var de = lm.Get("de"); var en = lm.Get("en");
            Check(de != null && de.Count > 50000 && en != null && en.Count > 50000, "beide Wortlisten geladen: " + (de == null ? 0 : de.Count) + " / " + (en == null ? 0 : en.Count));

            var sEn = en.Suggest("mon", 5, false);
            Console.WriteLine("     en mon -> " + string.Join(", ", sEn));
            Check(sEn.Contains("Monday") || sEn.Contains("money"), "englische Vorschläge");
            Check(en.Suggest("i", 8, false).Contains("I"), "\"I\" groß");
            var sEnTh = en.Suggest("th", 5, false);
            Check(sEnTh.All(w => char.IsLower(w[0])), "englisch nicht künstlich groß: " + string.Join(", ", sEnTh));

            var custom = lm.LoadCustomWords();
            Check(File.Exists(lm.CustomWordsPath) && custom.Count == 1, "eigene_woerter.txt angelegt mit Beispielen");

            var set = new LanguageSet();
            set.SetActive(new[] { de, en }, custom);
            set.SetMode("de");
            Check(set.Modes().SequenceEqual(new[] { "de", "en", LanguageSet.MixKey }), "Modi: DE, EN, gemischt");
            Check(set.Label == "DE", "Anzeige DE");
            set.Next(); Check(set.Mode == "en" && set.Label == "EN", "weiter zu EN");
            set.Next(); Check(set.Mode == LanguageSet.MixKey && set.Label == "DE+EN", "weiter zu DE+EN");
            set.Next(); Check(set.Mode == "de", "wieder DE");
            set.OfferMix = false; set.SetActive(new[] { de, en }, custom);
            Check(!set.Modes().Contains(LanguageSet.MixKey), "gemischt abschaltbar");
            set.OfferMix = true; set.SetActive(new[] { de, en }, custom);

            set.SetMode("de");
            var d1 = set.Suggest("th", 5, false);
            set.SetMode("en");
            var e1 = set.Suggest("th", 5, false);
            set.SetMode(LanguageSet.MixKey);
            var m1 = set.Suggest("th", 8, false);
            Console.WriteLine("     th: DE " + string.Join(",", d1) + " | EN " + string.Join(",", e1) + " | MIX " + string.Join(",", m1));
            Check(e1.Contains("the") && !d1.Contains("the"), "Sprache bestimmt die Vorschläge");
            set.SetMode(LanguageSet.MixKey);
            var m2 = set.Suggest("ha", 8, false);
            Console.WriteLine("     ha MIX " + string.Join(",", m2));
            Check(m1.Contains("the") && m2.Contains("have") && m2.Contains("hat"), "gemischt enthält beide Sprachen");
            Check(m1.Distinct(StringComparer.OrdinalIgnoreCase).Count() == m1.Count, "gemischt ohne Doppelte");

            set.SetMode("en");
            Check(set.Suggest("Quad", 3, false).Contains("Quadstick"), "eigene Begriffe auch auf Englisch");
            set.SetMode("de");
            Check(set.Suggest("Quad", 3, false).Contains("Quadstick"), "eigene Begriffe auf Deutsch");
            Check(!set.Suggest("", 5, false).Contains("Quadstick"), "eigene Begriffe nicht bei leerer Eingabe");
            set.SetMode("xx");
            Check(set.Mode == "de", "unbekannter Modus fällt auf erste Sprache zurück");

            Console.WriteLine("\n--- Sprachpakete ---");
            var sb = new StringBuilder("#sprache\tpt\tPortugiesisch\n");
            for (int i = 0; i < 150; i++) sb.Append("mot" + i + "\t" + (500 - i) + "\n");
            var pack = Encoding.UTF8.GetBytes(sb.ToString());
            string code, name; int words;
            Check(LanguageManager.Validate(pack, "egal", out code, out name, out words) == null && code == "pt" && name == "Portugiesisch" && words == 150, "Paket mit Kopfzeile gültig");
            var noHead = Encoding.UTF8.GetBytes(sb.ToString().Substring(sb.ToString().IndexOf('\n') + 1));
            Check(LanguageManager.Validate(noHead, "it", out code, out name, out words) == null && code == "it", "ohne Kopfzeile: Kürzel aus Dateiname");
            Check(LanguageManager.Validate(Encoding.UTF8.GetBytes("a\t1\nb\t2\n"), "xx", out code, out name, out words) != null, "zu wenige Wörter abgelehnt");
            Check(LanguageManager.Validate(noHead, "Mein Paket!", out code, out name, out words) != null, "ungültiges Kürzel abgelehnt");

            LanguageInfo info;
            Check(lm.Install(pack, null, out info) == null && File.Exists(Path.Combine(lm.Dir, "pt.txt")), "Paket installiert");
            inst = lm.Installed();
            Check(inst.Any(l => l.Code == "pt" && l.Name == "Portugiesisch" && !l.BuiltIn), "erscheint in der Liste");
            var fr = lm.Get("pt");
            Check(fr != null && fr.Suggest("mot1", 3, false).Count > 0, "Paket lädt und schlägt vor");
            var dePack = Encoding.UTF8.GetBytes("#sprache\tde\tDeutsch\n" + sb.ToString().Substring(sb.ToString().IndexOf('\n') + 1));
            Check(lm.Install(dePack, null, out info) != null, "eingebaute Sprache nicht überschreibbar");
            Check(!lm.Delete("de") && lm.Delete("pt") && !File.Exists(Path.Combine(lm.Dir, "pt.txt")), "Löschen nur für Pakete");

            string idx = "# Kommentar\n" +
                         "fr\tFranzösisch\tfr.txt\t" + pack.Length + "\t" + PackDownloader.Sha256Hex(pack) + "\n" +
                         "xx\tBöse\t../böse.txt\t10\t" + new string('a', 64) + "\n" +
                         "kaputt\n" +
                         "es\tSpanisch\tes.txt\tzehn\t" + new string('b', 64) + "\n" +
                         "nl\tNiederländisch\tnl.txt\t10\tkurz\n";
            var entries = PackDownloader.ParseIndex(idx);
            Check(entries.Count == 1 && entries[0].Code == "fr" && entries[0].Bytes == pack.Length, "Übersicht: nur gültige Zeilen, kein ../");
            Check(PackDownloader.Verify(entries[0], pack) == null, "Prüfsumme passt");
            var bad = (byte[])pack.Clone(); bad[10] ^= 1;
            Check(PackDownloader.Verify(entries[0], bad) != null, "veränderte Datei wird verworfen");
            Check(PackDownloader.Verify(entries[0], pack.Take(100).ToArray()) != null, "unvollständige Datei wird verworfen");
            Check(PackDownloader.Url("https://x.de/a", "index.txt") == "https://x.de/a/index.txt"
                && PackDownloader.Url("https://x.de/a/", "fr.txt") == "https://x.de/a/fr.txt", "Adressen zusammensetzen");

            string packs = Path.Combine(Path.GetDirectoryName(resDir), "..", "sprachpakete");
            if (Directory.Exists(packs) && File.Exists(Path.Combine(packs, "index.txt")))
            {
                var real = PackDownloader.ParseIndex(File.ReadAllText(Path.Combine(packs, "index.txt")));
                bool ok = real.Count >= 6;
                foreach (var r in real)
                {
                    var data = File.ReadAllBytes(Path.Combine(packs, r.File));
                    if (PackDownloader.Verify(r, data) != null) { ok = false; Console.WriteLine("     falsch: " + r.File); }
                    if (LanguageManager.Validate(data, null, out code, out name, out words) != null || code != r.Code) { ok = false; Console.WriteLine("     ungültig: " + r.File); }
                }
                Check(ok, "mitgelieferte Sprachpakete und index.txt stimmen (" + real.Count + " Pakete)");
            }

            Console.WriteLine("\n--- Verschieben und Größe ---");
            var area = new Rectangle(0, 0, 1920, 1040);
            Check(Geometry.Clamp(new Rectangle(-50, 900, 800, 300), area) == new Rectangle(0, 740, 800, 300), "links/unten raus wird zurückgeholt");
            Check(Geometry.Clamp(new Rectangle(1800, -20, 800, 300), area) == new Rectangle(1120, 0, 800, 300), "rechts/oben raus wird zurückgeholt");
            Check(Geometry.Clamp(new Rectangle(100, 100, 800, 300), area) == new Rectangle(100, 100, 800, 300), "drinnen bleibt unverändert");
            var area2 = new Rectangle(1920, 0, 1280, 1024);
            Check(Geometry.Clamp(new Rectangle(3000, 500, 800, 300), area2) == new Rectangle(2400, 500, 800, 300), "zweiter Monitor");

            var cs = new Size(1100, 380);
            Check(Geometry.HitEdge(cs, new Point(5, 200), 12, 24) == Edge.Left, "linker Rand");
            Check(Geometry.HitEdge(cs, new Point(1095, 200), 12, 24) == Edge.Right, "rechter Rand");
            Check(Geometry.HitEdge(cs, new Point(500, 3), 12, 24) == Edge.Top, "oberer Rand");
            Check(Geometry.HitEdge(cs, new Point(500, 375), 12, 24) == Edge.Bottom, "unterer Rand");
            Check(Geometry.HitEdge(cs, new Point(20, 375), 12, 24) == (Edge.Left | Edge.Bottom), "Ecke unten links (größere Zone)");
            Check(Geometry.HitEdge(cs, new Point(1095, 5), 12, 24) == (Edge.Right | Edge.Top), "Ecke oben rechts");
            Check(Geometry.HitEdge(cs, new Point(500, 200), 12, 24) == Edge.None, "Mitte ist kein Rand");

            var min = new Size(480, 180);
            var st = new Rectangle(100, 100, 1000, 400);
            Check(Geometry.Resize(st, Edge.Right, 50, 0, min) == new Rectangle(100, 100, 1050, 400), "rechts größer");
            Check(Geometry.Resize(st, Edge.Left, -50, 0, min) == new Rectangle(50, 100, 1050, 400), "links größer, rechte Kante bleibt");
            Check(Geometry.Resize(st, Edge.Top | Edge.Left, 900, 900, min) == new Rectangle(620, 320, 480, 180), "nicht kleiner als Mindestgröße");
            Check(Geometry.Resize(st, Edge.Bottom, 0, -30, min) == new Rectangle(100, 100, 1000, 370), "unten kleiner");

            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            var dd = new DwellDrag(new Point(500, 500), t0, 1000);
            Check(!dd.Update(new Point(500, 500), t0.AddMilliseconds(1500)), "ohne Bewegung kein Absetzen");
            Check(!dd.Update(new Point(600, 520), t0.AddMilliseconds(1600)), "Bewegung");
            Check(!dd.Update(new Point(602, 521), t0.AddMilliseconds(2200)), "kleines Zittern zählt als ruhig, noch zu früh");
            Check(dd.Update(new Point(601, 522), t0.AddMilliseconds(2700)), "nach 1 s Ruhe wird abgesetzt");
            var dd2 = new DwellDrag(new Point(500, 500), t0, 1000);
            Check(dd2.TimedOut(t0.AddSeconds(9)), "versehentlich gestartet: Abbruch nach Wartezeit");

            Console.WriteLine("\n--- Einstellungen Sprachen/Verschieben ---");
            Settings.InitDataDir();
            var s1 = new Settings { ActiveLanguages = "en,de", LanguageMode = "mix", OfferMix = false, PerProgramLanguage = true, HotkeyLanguage = "F16",
                HoldToDrag = false, LongPressMs = 1500, DwellDragMs = 2500, DropMs = 800, ResizeBorder = 20 };
            s1.SetProgramLanguage("chrome", "en");
            s1.SetProgramLanguage("WINWORD", "de");
            s1.SetProgramLanguage("chrome", "mix");
            s1.Save();
            var s2 = Settings.Load();
            Check(s2.ActiveLanguages == "en,de" && s2.LanguageMode == "mix" && !s2.OfferMix && s2.PerProgramLanguage && s2.HotkeyLanguage == "F16", "Sprach-Einstellungen gespeichert");
            var map = s2.ProgramLanguageMap();
            Check(map.Count == 2 && map["Chrome"] == "mix" && map["winword"] == "de", "Sprache pro Programm gespeichert");
            Check(!s2.HoldToDrag && s2.LongPressMs == 1500 && s2.DwellDragMs == 2500 && s2.DropMs == 800 && s2.ResizeBorder == 20, "Verschieben-Einstellungen gespeichert");
            Check(new Settings { DwellMs = 800 }.EffectiveDwellDragMs == 1600, "Standard: doppelte Verweilzeit");
            Check(new Settings { DwellMs = 800, DwellDragMs = 900 }.EffectiveDwellDragMs == 1100, "Verweilen bis Ziehen immer deutlich länger als Verweilzeit");

            Console.WriteLine("\n--- Umstellung alter Einstellungen ---");
            string sf = Path.Combine(Settings.DataDir, "einstellungen.txt");
            File.WriteAllText(sf, "verschieben_gedrueckt_ms = 1000\nsprachen_quelle = " + PackDownloader.OldDefaultSources[1] + "\n");
            var old1 = Settings.Load();
            Check(old1.LongPressMs == 500, "alter Standard 1 s wird einmalig 0,5 s");
            Check(old1.LanguageSource == PackDownloader.DefaultSource, "alte Download-Adresse wird umgestellt");
            Check(PackDownloader.DefaultSource.Contains("/nohandsneeded/"), "neue Download-Adresse zeigt auf nohandsneeded");

            Console.WriteLine("\n--- Umbenennung in NoHandsNeeded ---");
            string mig = Path.Combine(Path.GetTempPath(), "nhn_mig_" + Guid.NewGuid().ToString("N"));
            string oldDir = Path.Combine(mig, Settings.OldDataFolderName), newDir = Path.Combine(mig, Settings.DataFolderName);
            Directory.CreateDirectory(oldDir);
            File.WriteAllText(Path.Combine(oldDir, "einstellungen.txt"), "x");
            Settings.MigrateFolder(oldDir, newDir);
            Check(!Directory.Exists(oldDir) && File.Exists(Path.Combine(newDir, "einstellungen.txt")), "alter Datenordner Tastatur-Daten wird übernommen");
            Directory.CreateDirectory(oldDir);
            File.WriteAllText(Path.Combine(oldDir, "einstellungen.txt"), "alt");
            Settings.MigrateFolder(oldDir, newDir);
            Check(File.ReadAllText(Path.Combine(newDir, "einstellungen.txt")) == "x", "vorhandener neuer Ordner wird nicht überschrieben");
            try { Directory.Delete(mig, true); } catch { }
            Check(AppInfo.Name == "NoHandsNeeded", "Programmname");
            old1.Save();
            var again = Settings.Load();
            again.LongPressMs = 1000; again.Save();
            Check(Settings.Load().LongPressMs == 1000, "danach selbst eingestellte 1 s bleibt");
            File.WriteAllText(sf, "verschieben_gedrueckt_ms = 1500\n");
            Check(Settings.Load().LongPressMs == 1500, "selbst geänderter Wert bleibt");
            Check(new Settings().LongPressMs == 500, "neuer Standard 0,5 s");
            Check(new Settings().HotkeyShow == "Pause" && new Settings().HotkeyMacros == "NumMinus", "neue Standard-Kürzel Pause und NumMinus");
            File.WriteAllText(sf, "einstellungen_version = 2\nkuerzel_einblenden = F14\nkuerzel_makros = F15\n");
            var hk = Settings.Load();
            Check(hk.HotkeyShow == "Pause" && hk.HotkeyMacros == "NumMinus", "alte Standard-Kürzel F14/F15 werden umgestellt");
            File.WriteAllText(sf, "einstellungen_version = 2\nkuerzel_einblenden = Strg+Alt+Shift+E\nkuerzel_makros = \n");
            hk = Settings.Load();
            Check(hk.HotkeyShow == "Strg+Alt+Shift+E" && hk.HotkeyMacros == "", "selbst geänderte oder ausgeschaltete Kürzel bleiben");
            File.WriteAllText(sf, "einstellungen_version = 3\nkuerzel_einblenden = F14\n");
            Check(Settings.Load().HotkeyShow == "F14", "nach der Umstellung selbst gewähltes F14 bleibt");
            Check(KeyCombo.Parse("Pause").Vk == 0x13 && KeyCombo.Parse("NumMinus").Vk == 0x6D, "Pause und NumMinus werden erkannt");
            File.WriteAllText(sf, "verschieben_gedrueckt_ms = 100\n");
            Check(Settings.Load().LongPressMs == 300, "mindestens 0,3 s");
            File.WriteAllText(sf, "sprachen_quelle = http://unsicher.de/\n");
            Check(Settings.Load().LanguageSource == PackDownloader.DefaultSource, "nur https als Quelle");
            Check(PackDownloader.IsValidSource("https://mdm3d.de/sprachen/") && !PackDownloader.IsValidSource("ftp://x") && !PackDownloader.IsValidSource("quatsch"), "Adressprüfung");
            Check(PackDownloader.FriendlyError(new System.Net.WebException("x", System.Net.WebExceptionStatus.NameResolutionFailure)).Contains("Keine Internetverbindung"), "Meldung ohne Internet");
            Check(PackDownloader.FriendlyError(new InvalidDataException("Prüfsumme stimmt nicht")) == "Prüfsumme stimmt nicht", "Prüfsummen-Meldung bleibt");

            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
