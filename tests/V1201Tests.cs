using System;
using System.IO;
using System.Linq;

namespace Tastatur
{
    /// <summary>1.2.01: Passwort beim Tippen oben links im Klartext, 👁 und 🔍 ohne leeres Kästchen.</summary>
    internal static class V1201Tests
    {
        public static void Run(Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.01: Passwort im Klartext ---");
            string tmp = Path.Combine(Path.GetTempPath(), "nhn-1201-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            string f = Path.Combine(tmp, "einst.txt");

            Check(new Settings().PasswordPlain, "neu: Passwort im Klartext ist Standard");
            File.WriteAllText(f, "einstellungen_version = 5\npasswort_klartext = ja\n");
            Check(Settings.LoadFrom(f).PasswordPlain, "Umstellung: bisher \"Punkte mit 👁\" wird Klartext");
            File.WriteAllText(f, "einstellungen_version = 5\npasswort_klartext = nein\n");
            var dots = Settings.LoadFrom(f);
            Check(!dots.PasswordPlain && !dots.PasswordReveal, "Umstellung: \"immer nur Punkte\" bleibt");
            var eye = new Settings { PasswordPlain = false, PasswordReveal = true };
            eye.SaveTo(f);
            var back = Settings.LoadFrom(f);
            Check(!back.PasswordPlain && back.PasswordReveal, "\"Punkte mit 👁\" wird gespeichert und bleibt nach dem Neustart");
            Check(back.FileVersion == Settings.CurrentVersion, "Einstellungsversion aktuell");
            try { Directory.Delete(tmp, true); } catch { }

            Check(KeyboardForm.NeedsSymbolFont("👁") && KeyboardForm.NeedsSymbolFont("🔍 Suchen") && KeyboardForm.NeedsSymbolFont("⚿ Gmail"), "👁 und 🔍 bekommen die Symbolschrift (kein leeres Kästchen)");
            Check(!KeyboardForm.NeedsSymbolFont("Gmail"), "normaler Text bleibt in der normalen Schrift");

            if (Environment.OSVersion.Platform == PlatformID.Unix && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            { Console.WriteLine("     Tastatur-Teil übersprungen (kein Bildschirm)"); return; }

            var L = KeyLayouts.CreateGerman();
            Func<Settings, Tuple<string, bool>> typePw = st =>
            {
                var e = new KeyboardEngine(new FakeOutput(), new Predictor(), st);
                using (var kb = new KeyboardForm(st, e))
                {
                    e.PasswordMode = true;
                    foreach (char c in "geheim7") foreach (var row in L) foreach (var k in row) if (k.Kind == KeyKind.Char && k.Normal == c.ToString()) e.Press(k);
                    bool pw, unk;
                    return Tuple.Create(kb.InputText(out pw, out unk), kb.PlainTyping);
                }
            };
            var plain = typePw(new Settings());
            Check(plain.Item1 == "geheim7" && plain.Item2, "Klartext: oben steht das getippte Passwort: " + plain.Item1);
            var dotted = typePw(new Settings { PasswordPlain = false, PasswordReveal = true });
            Check(dotted.Item1 == "•••••••" && !dotted.Item2, "Punkte mit 👁: weiter nur Punkte");

            var st2 = new Settings();
            var e2 = new KeyboardEngine(new FakeOutput(), new Predictor(), st2);
            using (var kb2 = new KeyboardForm(st2, e2))
            {
                e2.PasswordMode = true;
                kb2.VaultTypedLength = 9;   // Passwort aus dem Tresor eingesetzt
                bool pw, unk;
                Check(kb2.InputText(out pw, out unk) == "•••••••••", "Passwort aus dem Tresor bleibt auch im Klartext-Modus Punkte");
                kb2.VaultTypedLength = 0;
                kb2.LockMode = true;
                foreach (var row in L) foreach (var k in row) if (k.Kind == KeyKind.Char && k.Normal == "x") e2.Press(k);
                Check(kb2.InputText(out pw, out unk) == "•" && !kb2.PlainTyping, "Sperrbildschirm: weiter Punkte");
            }
        }
    }
}
