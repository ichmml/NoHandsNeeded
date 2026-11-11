using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace Tastatur
{
    internal static class K5Tests
    {
        public static void Run(string dePath, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- Größe und Bildschirme ---");
            var min = new Size(480, 180);
            // Hauptbildschirm 2560x1440 (Taskleiste 48), rechts daneben 1920x1080
            var main = new Rectangle(0, 0, 2560, 1392);
            var second = new Rectangle(2560, 0, 1920, 1032);
            var areas = new[] { main, second };

            var r = Geometry.Restore(-1, -1, 60, 31, "rechts", areas, 0, min);
            Console.WriteLine("     Standard: " + r);
            Check(r.Width == 1536 && r.Height == 432 && r.Right == main.Right && r.Bottom == main.Bottom, "Standard 60 % x 31 %, unten rechts bündig");
            var rm = Geometry.Restore(-1, -1, 60, 31, "mitte", areas, 0, min);
            Check(rm.Left == (2560 - 1536) / 2, "Standard unten mittig");
            var rl = Geometry.Restore(-1, -1, 60, 31, "links", areas, 0, min);
            Check(rl.Left == 0 && rl.Bottom == main.Bottom, "Standard unten links");

            var r2 = Geometry.Restore(3000, 500, 60, 31, "rechts", areas, 0, min);
            Check(r2.Width == 1152 && r2.Height == 320 && second.Contains(r2), "gespeicherter Platz auf dem zweiten Bildschirm, Größe in % davon");
            var gone = Geometry.Restore(5000, 300, 60, 31, "rechts", new[] { main }, 0, min);
            Check(gone == r, "Bildschirm weg: Standardplatz auf dem Hauptbildschirm");

            // Ziehen über die Grenze und absetzen auf dem kleineren
            var dragged = new Rectangle(2400, 600, 1536, 432);
            var drop = Geometry.Rescale(dragged, second, 60, 31, min);
            Console.WriteLine("     abgesetzt: " + drop);
            Check(second.Contains(drop) && drop.Width == 1152, "Absetzen auf dem zweiten: ganz drauf, 60 % von dessen Breite");
            var backR = Geometry.Rescale(drop, main, 60, 31, min);
            Check(main.Contains(backR) && backR.Width == 1536, "zurück auf den Hauptbildschirm: wieder 60 % davon");
            var big = Geometry.Fit(new Rectangle(2600, 100, 2500, 500), second, min);
            Check(big.Width == 1920 && second.Contains(big), "zu breit: auf Bildschirmbreite verkleinert statt angeschlagen");
            Check(Geometry.AreaAt(new Point(3000, 10), areas) == 1 && Geometry.AreaAt(new Point(100, 1500), areas) == 0, "Bildschirm unter der Maus (auch knapp daneben)");
            var sz = Geometry.SizeFor(new Rectangle(0, 0, 800, 600), 60, 31, min);
            Check(sz.Width == 480 && sz.Height == 186, "Mindestgröße bleibt");

            var st = new Settings();
            Check(st.WidthPercent == 60 && st.HeightPercent == 31 && st.DockPosition == "rechts", "neue Einstellungen: 60/31 unten rechts");
            string tmp = Path.Combine(Path.GetTempPath(), "tastatur-k5-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            string f = Path.Combine(tmp, "einstellungen.txt");
            File.WriteAllText(f, "einstellungen_version = 3\nfenster_x = 100\nfenster_y = 200\nfenster_breite = 1100\nfenster_hoehe = 380\n");
            var old = Settings.LoadFrom(f);
            Check(old.WidthPercent == 60 && old.HeightPercent == 31 && old.X == -1 && old.FileVersion == Settings.CurrentVersion, "Umstellung v3 → v4 setzt Standardgröße einmal");
            old.WidthPercent = 55.5; old.HeightPercent = 28; old.DockPosition = "links"; old.X = 50;
            old.SaveTo(f);
            var again = Settings.LoadFrom(f);
            Check(again.WidthPercent == 55.5 && again.HeightPercent == 28 && again.DockPosition == "links" && again.X == 50, "eigene Größe bleibt danach erhalten");

            Console.WriteLine("\n--- Zahlen ---");
            Check(NumberRules.Classify("10.09.1991", false) == "10.09.1991", "Datum wird gelernt");
            Check(NumberRules.Classify("32.13.1991", false) == null, "ungültiges Datum nicht");
            Check(NumberRules.Classify("0170 1234567", false) == "0170 1234567" && NumberRules.Classify("+49 9071 123456", false) != null, "Telefonnummern");
            NumberRules.AllowSecret = false;
            Check(NumberRules.Classify("89407", true) == "89407" && NumberRules.Classify("89407", false) == null, "Postleitzahl nur vor einem Wort");
            Check(NumberRules.Classify("4111111111111111", false) == null, "Kartennummer nie (wenn abgeschaltet)");
            Check(NumberRules.Classify("01711234567", false) == "01711234567" && NumberRules.Classify("+491711234567", false) != null && NumberRules.Classify("0171/1234567", false) != null, "Handynummern in allen Schreibweisen");
            Check(NumberRules.Classify("123456789", false) == "123456789" && NumberRules.Classify("4711-0815-42", false) != null, "lange Nummern ab 9 Ziffern");
            Check(NumberRules.Classify("123456", false) == null && NumberRules.Classify("0815", false) == null && NumberRules.Classify("250", false) == null, "Codes, PINs und Mengen nie (wenn abgeschaltet)");
            NumberRules.AllowSecret = true;
            Check(NumberRules.Classify("4111 1111 1111 1111", false) == "4111 1111 1111 1111" && NumberRules.Classify("4111111111111111", false) != null, "Kartennummer gelernt, wenn eingeschaltet");
            Check(NumberRules.Classify("0815", false) == "0815" && NumberRules.Classify("123456", false) == "123456" && NumberRules.Classify("250", false) == null, "PIN gelernt, kurze Mengen weiter nicht");
            Check(NumberRules.IsSecret("4111 1111 1111 1111") && NumberRules.IsSecret("0815") && !NumberRules.IsSecret("0170 1234567") && !NumberRules.IsSecret("10.09.1991"), "PIN und Karte gelten als geheim");

            string resDir = Path.GetDirectoryName(Path.GetFullPath(dePath));
            Func<string, Stream> res = rn => { string fp = Path.Combine(resDir, rn); return File.Exists(fp) ? File.OpenRead(fp) : null; };
            var lm = new LanguageManager(tmp, res);
            var set = new LanguageSet();
            set.SetActive(new[] { lm.Get("de") }, null);
            set.SetStore("de", new LearningStore { Code = "de" });
            var o = new FakeOutput();
            var settings = new Settings();
            var e = new KeyboardEngine(o, set, settings);
            var nums = new List<string>();
            e.NumberFinished += (n, w) => { nums.Add(n + (w ? "+W" : "")); set.LearnNumber(n, w); };
            e.NumberReopened += (n, w) => set.UnlearnNumber(n, w);
            var L = KeyLayouts.CreateGerman();
            var keys = L.SelectMany(x => x).ToList();
            var space = keys.First(k => k.Kind == KeyKind.Space);
            var back = keys.First(k => k.Kind == KeyKind.Special && k.Vk == InputSender.VK_BACK);
            var enter = keys.First(k => k.Kind == KeyKind.Special && k.Vk == InputSender.VK_RETURN);
            Action<string> type = str =>
            {
                foreach (char c in str)
                {
                    if (c == ' ') { e.Press(space); continue; }
                    var k = keys.FirstOrDefault(x => x.Kind == KeyKind.Char && x.Normal == c.ToString());
                    if (k == null) { k = keys.First(x => x.Kind == KeyKind.Char && x.Shifted == c.ToString()); e.Shift = ModState.Once; }
                    e.Press(k);
                }
            };

            type("geboren am 10.09.1991 in ");
            Console.WriteLine("     erkannt: " + string.Join(" | ", nums) + "   Text: " + o.Doc);
            Check(nums.Contains("10.09.1991"), "Datum mit Punkten als ein Stück");
            Check(o.Doc.ToString() == "geboren am 10.09.1991 in ", "keine Großschreibung nach den Punkten im Datum");
            nums.Clear(); o.Doc.Length = 0;
            type("gut. am 1.2.2020 ist");
            Check(o.Doc.ToString() == "gut. Am 1.2.2020 ist", "nach Satzende groß, nach Datum klein: " + o.Doc);
            type(" 1009");
            Check(e.Suggestions().Contains("10.09.1991"), "schon nach 1-mal ein Vorschlag: " + string.Join(", ", e.Suggestions()));
            e.ResetWord(false); o.Doc.Length = 0; nums.Clear();
            type("10.09.1991 ");
            type("1009");
            var sg = e.Suggestions();
            Console.WriteLine("     1009 -> " + string.Join(", ", sg));
            Check(sg.Count > 0 && sg[0] == "10.09.1991", "nach 2-mal: \"1009\" findet \"10.09.1991\"");
            o.Doc.Length = 0;
            o.Doc.Append("x 1009");
            e.PressSuggestion("10.09.1991");
            Check(o.Doc.ToString() == "x 10.09.1991 ", "Übernahme ersetzt die Ziffern durch die gelernte Schreibweise: " + o.Doc);
            Check(set.Numbers.Get("10.09.1991").Count == 3, "Übernahme zählt mit");

            e.ResetWord(false); nums.Clear();
            type("Tel 0170 1234567 ruf");
            Console.WriteLine("     erkannt: " + string.Join(" | ", nums));
            Check(nums.Contains("0170 1234567"), "Telefonnummer mit Leerzeichen als ein Stück");
            e.ResetWord(false); nums.Clear(); o.Doc.Length = 0;
            type("01761234567");
            e.Press(enter);
            type("0176");
            Check(e.Suggestions().Contains("01761234567"), "Handynummer schon nach einmal Tippen mit Enter vorgeschlagen: " + string.Join(", ", e.Suggestions()));
            e.ResetWord(false); nums.Clear(); o.Doc.Length = 0;
            type("ruf 512 345 678 an");
            Check(nums.Contains("512 345 678"), "Nummer mit Leerzeichen ohne 0 vorne als ein Stück: " + string.Join(" | ", nums));
            e.ResetWord(false); nums.Clear(); o.Doc.Length = 0;
            type("am 10.09.1991 15 Leute");
            Check(nums.Contains("10.09.1991") && !nums.Any(x => x.Contains("1991 15")), "Datum und Menge bleiben getrennt: " + string.Join(" | ", nums));
            e.ResetWord(false); nums.Clear(); o.Doc.Length = 0;
            NumberRules.AllowSecret = false;
            type("seit 2019 12 Jahre");
            Check(!nums.Any(x => NumberRules.Classify(x.Replace("+W", ""), false) != null), "Jahreszahl und Menge werden nicht gelernt (PINs aus): " + string.Join(" | ", nums));
            NumberRules.AllowSecret = true;
            e.ResetWord(false); o.Doc.Length = 0;
            e.ResetWord(false); nums.Clear();
            type("89407 Dillingen");
            Check(nums.Contains("89407+W"), "Postleitzahl vor Ort erkannt");

            // Rücktaste direkt danach vergisst wieder
            e.ResetWord(false);
            int before = set.Numbers.Get("10.09.1991").Count;
            type("10.09.1991 ");
            e.Press(back);
            Check(set.Numbers.Get("10.09.1991").Count == before, "Rücktaste direkt danach nimmt das Lernen zurück");
            Check(e.CurrentNumber == "10.09.1991", "Zahl ist wieder offen");
            e.Press(enter);
            Check(set.Numbers.Get("10.09.1991").Count == before + 1, "Enter schließt sie wieder ab");

            // IBAN, Codes und Passwortfelder
            e.ResetWord(false); nums.Clear();
            int countBefore = set.Numbers.Count;
            type("DE12 3456 7890 1234 5678 90 ");
            type("TAN 123456 ");
            type("4111111111111111 ");
            Check(set.Numbers.Count == countBefore, "IBAN, TAN und Kartennummer nicht gelernt");
            e.PasswordMode = true;
            type("0170 7654321 ");
            e.PasswordMode = false;
            Check(set.Numbers.Get("0170 7654321") == null, "nichts aus Passwortfeldern");
            set.NumbersEnabled = false;
            type("0170 5555555 x 0170 5555555 x ");
            Check(set.Numbers.Get("0170 5555555") == null, "Zahlen lernen abschaltbar");
            set.NumbersEnabled = true;

            string np = Path.Combine(tmp, "Lernen", "zahlen.txt");
            set.Numbers.Save(np);
            var nl = NumberStore.Load(np);
            Check(nl.Get("10.09.1991") != null && nl.Get("10.09.1991").Count == set.Numbers.Get("10.09.1991").Count && !nl.Dirty, "Zahlen speichern und laden");

            // aus Texten
            var set2 = new LanguageSet();
            set2.SetActive(new[] { lm.Get("de") }, null);
            set2.SetStore("de", new LearningStore { Code = "de" });
            string docs = Path.Combine(tmp, "texte");
            Directory.CreateDirectory(docs);
            File.WriteAllText(Path.Combine(docs, "a.md"), "Geburtstag am 10.09.1991 in Dillingen.\nRuf an unter 0170 1234567 oder +49 9071 123456.\nAdresse: Hauptstraße 5, 89407 Dillingen\nMeine PIN 0815 und IBAN DE12 3456 7890 1234\nBestellung 4711 kam am 3.11.2026\n");
            var ir = TextImporter.Import(new[] { docs }, set2, null, CancellationToken.None);
            var learned = set2.Numbers.All().Select(w => w.Form).ToList();
            Console.WriteLine("     aus Text: " + string.Join(" | ", learned));
            Check(learned.Contains("10.09.1991") && learned.Contains("0170 1234567") && learned.Contains("+49 9071 123456") && learned.Contains("89407"), "Datum, Telefon, PLZ aus Texten");
            Check(!learned.Contains("0815") && !learned.Contains("4711") && !learned.Any(x => x.StartsWith("3456") || x.StartsWith("12")), "PIN-, IBAN-Zeile und Bestellnummer nicht");

            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
