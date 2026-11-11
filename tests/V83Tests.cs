using System;
using System.IO;
using System.Linq;

namespace Tastatur
{
    internal static class V83Tests
    {
        public static void Run(string dePath, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 8.3: Getipptes nach Sonderzeichen ---");
            string resDir = Path.GetDirectoryName(Path.GetFullPath(dePath));
            Func<string, Stream> res = rn => { string fp = Path.Combine(resDir, rn); return File.Exists(fp) ? File.OpenRead(fp) : null; };
            string tmp = Path.Combine(Path.GetTempPath(), "tastatur-v83-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var lm = new LanguageManager(tmp, res);
            var set = new LanguageSet();
            set.SetActive(new[] { lm.Get("de") }, lm.LoadCustomWords());
            var store = new LearningStore { Code = "de" };
            set.SetStore("de", store);
            var o = new FakeOutput();
            var e = new KeyboardEngine(o, set, new Settings { AutoCapitalize = false });
            e.WordFinished += (w, p, st) => set.Learn(w, p, st);
            var keys = KeyLayouts.CreateGerman().SelectMany(x => x).ToList();
            var space = keys.First(k => k.Kind == KeyKind.Space);
            var back = keys.First(k => k.Kind == KeyKind.Special && k.Vk == InputSender.VK_BACK);
            Action<string> type = str =>
            {
                foreach (char c in str)
                {
                    string cs = c.ToString();
                    if (c == ' ') { e.Press(space); continue; }
                    var k = keys.FirstOrDefault(x => x.Kind == KeyKind.Char && x.Normal == cs);
                    if (k == null) { k = keys.First(x => x.Kind == KeyKind.Char && x.Shifted == cs); e.Shift = ModState.Once; }
                    e.Press(k);
                }
            };

            type("s!add");
            Check(o.Doc.ToString() == "s!add", "getippt: " + o.Doc);
            Check(e.CurrentChunk == "s!add", "oben bleibt \"s!add\" stehen: " + e.CurrentChunk);
            Check(e.CurrentWord == "add", "Vorschläge weiter zum Wort \"add\"");
            e.Press(back);
            Check(e.CurrentChunk == "s!ad", "Rücktaste: \"s!ad\"");
            o.Doc.Length = 0;
            string acc = e.AcceptTyped();
            Check(acc == "s!ad" && o.Doc.ToString() == " ", "Klick auf \"s!ad\" setzt nur ein Leerzeichen");
            Check(e.CurrentChunk == "", "danach leer");
            type("www.mdm-3d_x.de");
            Check(e.CurrentChunk == "www.mdm-3d_x.de", "Adresse mit . - _ bleibt ganz stehen");
            type(" Hallo");
            Check(e.CurrentChunk == "Hallo" && e.CurrentWord == "Hallo", "normales Wort wie bisher");

            Console.WriteLine("\n--- 8.3: Passwort im Tresor ---");
            e.PasswordMode = true;
            type("Qzx!7v");
            Check(e.SecretText == "Qzx!7v", "Klartext oben: " + e.SecretText);
            Check(e.AcceptTyped() == null, "kein Übernehmen im Passwortmodus");
            int before = store.WordCount;
            e.TakeSecret();
            e.PasswordMode = false;
            Check(store.WordCount == before && !set.Knows("Qzx") && !set.Knows("7v"), "Passwort wird nicht gelernt");
            Check(e.SecretLength == 0, "nach Verlassen verworfen");

            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
