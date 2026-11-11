using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tastatur
{
    internal static class IbanTests
    {
        public static void Run(string dePath, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- IBAN ---");
            Check(IbanRules.IsValid("DE89 3704 0044 0532 0130 00") && IbanRules.IsValid("de89370400440532013000"), "gültige IBAN erkannt, mit und ohne Leerzeichen");
            Check(!IbanRules.IsValid("DE89 3704 0044 0532 0130 01") && !IbanRules.IsValid("DE12 3456 7890 1234 5678 90"), "falsche Prüfziffer abgelehnt");
            Check(IbanRules.Format("de89370400440532013000") == "DE89 3704 0044 0532 0130 00", "einheitlich in Vierergruppen");
            Check(IbanRules.IsValid("NL91 ABNA 0417 1643 00") && IbanRules.IsValid("GB29 NWBK 6016 1331 9268 19"), "auch mit Buchstaben im Bankteil");

            string resDir = Path.GetDirectoryName(Path.GetFullPath(dePath));
            Func<string, Stream> res = rn => { string fp = Path.Combine(resDir, rn); return File.Exists(fp) ? File.OpenRead(fp) : null; };
            string tmp = Path.Combine(Path.GetTempPath(), "nhn-iban-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var lm = new LanguageManager(tmp, res);
            var set = new LanguageSet();
            set.SetActive(new[] { lm.Get("de") }, null);
            set.SetStore("de", new LearningStore { Code = "de" });
            var o = new FakeOutput();
            var e = new KeyboardEngine(o, set, new Settings { AutoCapitalize = false });
            var ibans = new List<string>();
            var nums = new List<string>();
            e.IbanFinished += i => { ibans.Add(i); set.LearnIban(i); };
            e.NumberFinished += (n, w) => { nums.Add(n); set.LearnNumber(n, w); };
            var keys = KeyLayouts.CreateGerman().SelectMany(x => x).ToList();
            var space = keys.First(k => k.Kind == KeyKind.Space);
            Action<string> type = s =>
            {
                foreach (char c in s)
                {
                    if (c == ' ') { e.Press(space); continue; }
                    string cs = c.ToString();
                    var k = keys.FirstOrDefault(x => x.Kind == KeyKind.Char && x.Normal == cs);
                    if (k == null) k = keys.FirstOrDefault(x => x.Kind == KeyKind.Char && x.Shifted == cs);
                    if (k != null && k.Normal != cs) e.Shift = ModState.Once;
                    e.Press(k);
                }
            };

            type("Konto DE89 3704 0044 0532 0130 00 bitte");
            Check(ibans.Count == 1 && ibans[0] == "DE89 3704 0044 0532 0130 00", "IBAN mit Leerzeichen gelernt: " + string.Join(" | ", ibans));
            Check(nums.All(n => NumberRules.Classify(n, false) == null), "Ziffern der IBAN nicht einzeln als Nummer: " + string.Join(" | ", nums));
            e.ResetWord(false); o.Doc.Length = 0;
            type("NL91ABNA0417164300 ");
            Check(ibans.Count == 2 && ibans[1] == "NL91 ABNA 0417 1643 00", "IBAN ohne Leerzeichen gelernt");
            e.ResetWord(false); o.Doc.Length = 0;
            int before = set.Numbers.Count;
            type("DE12 3456 7890 1234 5678 90 ");
            e.ResetWord(false);
            Check(set.Numbers.Count == before, "IBAN mit falscher Prüfziffer wird nicht gelernt");

            o.Doc.Length = 0;
            type("DE8");
            Check(e.CurrentIbanPart == "DE8" && set.SuggestIbans(e.CurrentIbanPart, 2).Contains("DE89 3704 0044 0532 0130 00"), "nach \"DE8\" vorgeschlagen");
            type("9 37");
            Check(e.CurrentIbanPart == "DE89 37" && set.SuggestIbans(e.CurrentIbanPart, 2).Count == 1, "auch mitten in den Vierergruppen");
            e.CompleteIban("DE89 3704 0044 0532 0130 00");
            Check(o.Doc.ToString() == "DE89 3704 0044 0532 0130 00", "Klick ersetzt den Anfang durch die ganze IBAN: " + o.Doc);
            Check(set.Numbers.Get("DE89 3704 0044 0532 0130 00").Count == 2, "Übernahme zählt mit");
            e.ResetWord(false); o.Doc.Length = 0;
            type("IBAN: ");
            Check(e.AfterIbanLabel && e.CurrentIbanPart == "" && set.SuggestIbans("", 3).Count == 2, "nach \"IBAN:\" alle gemerkten IBANs");
            Check(set.SuggestNumbers("3704", 5).Count == 0, "IBAN taucht nicht bei normalen Nummern auf");

            e.PasswordMode = true;
            type("DE89370400440532013000 ");
            e.PasswordMode = false;
            Check(ibans.Count == 3, "im Passwortfeld nichts gelernt (nur die Übernahme von eben zählt)");
            set.IbansEnabled = false;
            Check(set.SuggestIbans("DE", 2).Count == 0, "abschaltbar");
            var back = NumberStore.FromText(set.Numbers.ToText());
            Check(back.Get("NL91 ABNA 0417 1643 00") != null, "IBAN wird mit gespeichert und geladen");
            Console.WriteLine("\n--- An der Maus öffnen ---");
            var area = new System.Drawing.Rectangle(0, 0, 1920, 1040);
            var sz = new System.Drawing.Size(1000, 300);
            var r1 = Geometry.AtMouse(new System.Drawing.Point(900, 300), sz, area, 40);
            Check(r1.Top == 340 && r1.Left == 400, "mittig knapp unter dem Mauszeiger: " + r1);
            var r2 = Geometry.AtMouse(new System.Drawing.Point(900, 900), sz, area, 40);
            Check(r2.Bottom == 860, "unten zu wenig Platz: darüber: " + r2);
            var r3 = Geometry.AtMouse(new System.Drawing.Point(50, 300), sz, area, 40);
            Check(r3.Left == 0, "am linken Rand bleibt sie ganz auf dem Bildschirm: " + r3);
            var r4 = Geometry.AtMouse(new System.Drawing.Point(2500, 300), sz, new System.Drawing.Rectangle(1920, 0, 1280, 1000), 40);
            Check(r4.Left == 2000 && r4.Top == 340, "zweiter Bildschirm: " + r4);
            var r5 = Geometry.AtMouse(new System.Drawing.Point(3150, 300), sz, new System.Drawing.Rectangle(1920, 0, 1280, 1000), 40);
            Check(r5.Right == 3200, "rechter Rand des zweiten Bildschirms: " + r5);
            Check(new Settings().OpenAtMouse, "standardmäßig an");
            var c1 = Geometry.AtMouse(new System.Drawing.Point(900, 300), sz, area, 40, "unten-rechts");
            Check(c1.Left == 900 && c1.Top == 340, "unter der Maus nach rechts: " + c1);
            var c2 = Geometry.AtMouse(new System.Drawing.Point(1500, 600), sz, area, 40, "oben-links");
            Check(c2.Right == 1500 && c2.Bottom == 560, "über der Maus nach links: " + c2);
            var c3 = Geometry.AtMouse(new System.Drawing.Point(1700, 300), sz, area, 40, "unten-rechts");
            Check(c3.Right == 1700, "rechts kein Platz: geht nach links: " + c3);
            var c4 = Geometry.AtMouse(new System.Drawing.Point(900, 100), sz, area, 40, "oben-mitte");
            Check(c4.Top == 140, "oben kein Platz: darunter: " + c4);
            string sp = Path.Combine(tmp, "e.txt");
            File.WriteAllText(sp, "an_maus_lage = oben-links\n");
            var st2 = Settings.LoadFrom(sp);
            File.WriteAllText(sp, "an_maus_lage = quatsch\n");
            Check(st2.MousePlace == "oben-links" && Settings.LoadFrom(sp).MousePlace == "unten-mitte", "Einstellung wird gelesen");

            Console.WriteLine("\n--- Tresor-Suche ---");
            var vl = new List<VaultEntry> { new VaultEntry { Name = "Gmail", User = "morten@gmail.com" }, new VaultEntry { Name = "Amazon" }, new VaultEntry { Name = "udaption Admin", Match = "chrome" } };
            Check(Vault.Filter(vl, "ma").Count == 2 && Vault.Filter(vl, "AMA").Count == 1 && Vault.Filter(vl, "").Count == 3, "Suche nach Name und Benutzer, Groß/Klein egal");
            Check(Vault.Filter(vl, "chrome").Count == 1 && Vault.Filter(vl, "xyz").Count == 0, "auch nach Programm");

            Console.WriteLine("\n--- Windows-Taste ---");
            var ow = new FakeOutput();
            var ew = new KeyboardEngine(ow, set, new Settings());
            var winKey = keys.First(k => k.Kind == KeyKind.Modifier && k.Mod == Modifiers.Win);
            ew.Press(winKey); ew.Press(winKey);
            Check(ow.Log.Contains("VK:5B+None") && ew.Win == ModState.Off, "2x Win öffnet das Startmenü: " + string.Join(" | ", ow.Log));
            ow.Log.Clear();
            ew.Press(winKey); ew.Press(keys.First(k => k.Kind == KeyKind.Char && k.Normal == "e"));
            Check(ow.Log.Any(x => x.StartsWith("VK:45") && x.Contains("Win")) || ow.Log.Any(x => x.Contains("Win")), "1x Win, dann E: Win+E: " + string.Join(" | ", ow.Log));

            Console.WriteLine("\n--- Leerzeichen nach Satzzeichen ---");
            Func<string, string> run = txt => { e.ResetWord(false); o.Doc.Length = 0; type(txt); return o.Doc.ToString(); };
            Check(run("Hallo,wie geht?") == "Hallo, wie geht? ", "nach Komma und Fragezeichen von selbst: '" + o.Doc + "'");
            Check(run("Hallo, wie") == "Hallo, wie", "eigenes Leerzeichen danach wird nicht doppelt: '" + o.Doc + "'");
            Check(run("Haus.Der") == "Haus. Der", "nach Punkt hinter bekanntem Wort: '" + o.Doc + "'");
            Check(run("Was?!") == "Was?! ", "zweites Satzzeichen rückt ran: '" + o.Doc + "'");
            Check(run("1,5") == "1,5" && run("10:30") == "10:30" && run("3.5") == "3.5", "Zahlen bleiben zusammen");
            Check(run("udaption.com") == "udaption.com" && run("www.mdm3d.de") == "www.mdm3d.de", "Webadressen ohne Leerzeichen: '" + o.Doc + "'");
            Check(run("https://x") == "https://x" && run("s!add") == "s!add", "Adressen und Codes bleiben, wie getippt");
            Check(run("Haus,1") == "Haus,1", "Ziffer direkt danach: Leerzeichen wieder weg");
            e.NoPunctuationSpace = true;
            Check(run("Hallo,wie") == "Hallo,wie", "im E-Mail- oder IBAN-Feld aus");
            e.NoPunctuationSpace = false;

            // PINs und Kartennummern: verschlüsselt neben zahlen.txt
            NumberRules.AllowSecret = true;
            var ns = new NumberStore();
            ns.Learn("4111 1111 1111 1111"); ns.Learn("0815"); ns.Learn("0170 1234567");
            string np = Path.Combine(tmp, "Lernen", "zahlen.txt");
            ns.Save(np);
            string plainText = File.ReadAllText(np);
            Check(!plainText.Contains("4111") && !plainText.Contains("0815") && plainText.Contains("0170 1234567"), "zahlen.txt enthält keine PIN und keine Kartennummer");
            Check(File.Exists(NumberStore.SecretPath(np)) && !File.ReadAllText(NumberStore.SecretPath(np)).Contains("4111"), "zahlen_geheim.dat ist verschlüsselt");
            var nl2 = NumberStore.Load(np);
            Check(nl2.Get("4111 1111 1111 1111") != null && nl2.Get("0815") != null && nl2.Count == 3, "beim Laden wieder alle da");
            Check(!ns.ToText(true, false).Contains("4111") && ns.ToText(true, true).Contains("4111"), "Sicherung ohne Passwort ohne PINs und Karten");
            Check(ns.Suggest("4111", 3, 1).Count == 1, "Kartennummer wird vorgeschlagen");
            NumberRules.AllowSecret = false;
            Check(ns.Suggest("4111", 3, 1).Count == 0, "abgeschaltet: nicht mehr vorgeschlagen");
            NumberRules.AllowSecret = true;
            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
namespace Tastatur
{
    internal static class OwnSecretTests
    {
        public static void Run(Action<bool, string> Check)
        {
            Console.WriteLine("\n--- Eigenes Passwortfeld ---");
            if (Environment.OSVersion.Platform == PlatformID.Unix && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            { Console.WriteLine("     übersprungen (kein Bildschirm)"); return; }
            var tb = new System.Windows.Forms.TextBox();
            Check(SecretField.DisplayText() == null, "ohne Fokus nichts");
            SecretField.Raise(tb, true, true);
            tb.Text = "Geheim1!";
            Check(SecretField.DisplayText() == "Geheim1!", "oben steht genau der Feldinhalt im Klartext");
            tb.UseSystemPasswordChar = true;
            Check(SecretField.DisplayText() == "••••••••", "Verbergen schaltet oben mit auf Punkte");
            tb.UseSystemPasswordChar = false;
            SecretField.Raise(tb, false, false);
            Check(SecretField.DisplayText() == null, "nach dem Verlassen nichts mehr");
            tb.Dispose();
        }
    }
}
