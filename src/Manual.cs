using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Tastatur
{
    /// <summary>Die ausführliche Anleitung (Anleitung-xx.txt) in 11 Sprachen, auch in der exe eingebettet.</summary>
    internal static class Manual
    {
        public static Func<string, Stream> Resource = name => Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

        public static string FileName(string lang) { return "Anleitung-" + lang + ".txt"; }

        /// <summary>Text der eingebetteten Anleitung oder null.</summary>
        public static string Embedded(string lang)
        {
            using (var s = Resource(FileName(lang)))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        /// <summary>Pfad zur Anleitung: erst neben der exe (Ordner Anleitung), sonst eingebettete Fassung
        /// nach dataDir\Anleitung schreiben. Sprache der Oberfläche, sonst Englisch.</summary>
        public static string Find(string lang, string exeDir, string dataDir)
        {
            foreach (var l in new[] { lang, "en", "de" })
            {
                if (string.IsNullOrEmpty(l)) continue;
                string beside = Path.Combine(exeDir ?? "", "Anleitung", FileName(l));
                if (File.Exists(beside)) return beside;
                string text = Embedded(l);
                if (text == null) continue;
                string dir = Path.Combine(dataDir, "Anleitung");
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, FileName(l));
                if (!File.Exists(target) || File.ReadAllText(target, Encoding.UTF8) != text)
                    File.WriteAllText(target, text, new UTF8Encoding(true));   // mit BOM, damit Notepad UTF-8 erkennt
                return target;
            }
            return null;
        }
    }
}
