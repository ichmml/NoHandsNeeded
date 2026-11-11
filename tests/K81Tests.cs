using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tastatur
{
    internal static class K81Tests
    {
        public static void Run(string dePath, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- Eingabefeld ---");
            string resDir = Path.GetDirectoryName(Path.GetFullPath(dePath));
            Func<string, Stream> res = rn => { string fp = Path.Combine(resDir, rn); return File.Exists(fp) ? File.OpenRead(fp) : null; };
            string tmp = Path.Combine(Path.GetTempPath(), "tastatur-k81-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var lm = new LanguageManager(tmp, res);
            var set = new LanguageSet();
            set.SetActive(new[] { lm.Get("de") }, lm.LoadCustomWords());
            set.SetStore("de", new LearningStore { Code = "de" });
            var o = new FakeOutput();
            var e = new KeyboardEngine(o, set, new Settings());
            e.WordFinished += (w, p, st) => set.Learn(w, p, st);
            var L = KeyLayouts.CreateGerman();
            var keys = L.SelectMany(x => x).ToList();
            var space = keys.First(k => k.Kind == KeyKind.Space);
            var back = keys.First(k => k.Kind == KeyKind.Special && k.Vk == InputSender.VK_BACK);
            Action<string> type = str => { foreach (char c in str) e.Press(c == ' ' ? space : keys.First(k => k.Kind == KeyKind.Char && k.Normal == c.ToString())); };

            type("mor");
            Check(e.CurrentWord == "mor", "Tippen zeigt \"mor\"");
            e.Press(back);
            Check(e.CurrentWord == "mo", "Rücktaste: \"mo\"");
            type("rtenx ");
            Check(e.CurrentWord == "", "Leerzeichen leert das Feld");

            Check(!set.Knows("Mortenx") && set.Knows("Haus"), "bekannt / neu erkennen");
            type("mortenx");
            o.Doc.Length = 0;
            string acc = e.AcceptTyped();
            set.LearnUntilSuggested(acc);
            Check(acc == "mortenx" && o.Doc.ToString() == " " && e.CurrentWord == "", "Klick aufs Feld übernimmt mit Leerzeichen: " + o.Doc);
            Check(set.Knows("mortenx") && set.Suggest("morte", 8, false).Contains("mortenx"), "neues Wort danach sofort vorgeschlagen");
            Check(e.AcceptTyped() == null, "leeres Feld: nichts passiert");

            type("10.09.19");
            Check(e.CurrentNumber == "10.09.19", "Zahl erscheint komplett: " + e.CurrentNumber);
            e.ResetWord(false);

            var es = KeyLayouts.Create("es");
            e.DeadKeys = KeyLayouts.DeadKeys("es");
            type("caf");
            e.Press(es.SelectMany(r => r).First(k => k.Normal == "´"));
            Check(e.CurrentWord == "caf" && e.DeadPending == '´', "Tottaste wartet sichtbar (caf´)");
            e.DeadKeys = KeyLayouts.DeadKeys("de");
            e.ResetWord(false);

            Console.WriteLine("\n--- Eingabefeld im Passwortfeld ---");
            e.PasswordMode = true;
            type("geheim7");
            Check(e.SecretLength == 7 && e.SecretText == "geheim7", "Passwort: 7 Zeichen, Klartext nur auf Abruf");
            Check(e.AcceptTyped() == null, "Klick aufs Feld übernimmt im Passwortfeld nichts");
            e.Press(back);
            Check(e.SecretLength == 6, "Rücktaste im Passwortfeld");
            e.TakeSecret();
            Check(e.SecretLength == 0 && e.SecretText == "", "nach Enter/Verlassen leer");
            e.PasswordMode = false;
            string log = Path.Combine(Settings.DataDir ?? tmp, "protokoll.txt");
            Check(!File.Exists(log) || !File.ReadAllText(log).Contains("geheim"), "nichts im Protokoll");

            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
