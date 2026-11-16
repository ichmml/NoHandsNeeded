using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Tastatur
{
    /// <summary>1.03: richtige Groß/Kleinschreibung, mehr Wörter, eingebaute Wortfolgen.</summary>
    internal static class V103Tests
    {
        public static void Run(string dePath, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.03: Groß/Klein, Wortlisten, Wortfolgen ---");
            string resDir = Path.GetDirectoryName(Path.GetFullPath(dePath));
            Func<string, Stream> res = rn => { string fp = Path.Combine(resDir, rn); return File.Exists(fp) ? File.OpenRead(fp) : null; };
            string tmp = Path.Combine(Path.GetTempPath(), "nhn-103-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var lm = new LanguageManager(tmp, res) { PairsInBackground = false };

            // ---------- Groß/Klein ----------
            var de = lm.Get("de");
            var set = new LanguageSet();
            set.SetActive(new[] { de }, null);
            set.SetStore("de", new LearningStore { Code = "de" });
            var s = set.Suggest("karton", null, 5, false);
            Check(s.Count > 0 && s[0] == "Karton", "karton -> Karton steht ganz vorne: " + string.Join(", ", s));
            Check(s.Contains("Kartons"), "Kartons kommt trotzdem noch: " + string.Join(", ", s));
            s = set.Suggest("montag", null, 5, false);
            Check(s.Count > 0 && s[0] == "Montag", "montag -> Montag: " + string.Join(", ", s));
            s = set.Suggest("Karton", null, 5, false);
            Check(!s.Contains("Karton"), "richtig getippt: Karton nicht doppelt");
            s = set.Suggest("KARTON", null, 5, false);
            Check(!s.Contains("KARTON") && !s.Contains("Karton"), "GROSS getippt: keine Korrektur auf Karton");
            s = set.Suggest("und", null, 5, false);
            Check(!s.Contains("und") && !s.Contains("Und"), "klein richtig: kein Und mitten im Satz");

            var en = lm.Get("en");
            var setEn = new LanguageSet();
            setEn.SetActive(new[] { en }, null);
            setEn.SetStore("en", new LearningStore { Code = "en" });
            s = setEn.Suggest("monday", null, 5, false);
            Check(s.Count > 0 && s[0] == "Monday", "englisch: monday -> Monday: " + string.Join(", ", s));
            s = setEn.Suggest("iphone", null, 5, false);
            Check(s.Count == 0 || s[0] == "iPhone" || !s.Any(x => x.Equals("iphone", StringComparison.OrdinalIgnoreCase)), "iphone: iPhone vorne, falls bekannt: " + string.Join(", ", s));

            // gelerntes Wort mit eigener Schreibweise
            var st = new LearningStore { Code = "de" };
            set.SetStore("de", st);
            for (int i = 0; i < 4; i++) st.Learn("NoHandsNeeded", null, false);
            s = set.Suggest("nohandsneeded", null, 5, false);
            Check(s.Count > 0 && s[0] == "NoHandsNeeded", "gelernt: nohandsneeded -> NoHandsNeeded: " + string.Join(", ", s));
            Check(Predictor.IsCaseFix("Karton", "karton") && !Predictor.IsCaseFix("Karton", "Karton") && !Predictor.IsCaseFix("Kartons", "karton"), "IsCaseFix");

            // ---------- Wortlisten ----------
            var sizes = new List<string>();
            bool allBig = true;
            var sw = Stopwatch.StartNew();
            foreach (var li in LanguageManager.BuiltIns)
            {
                var p = lm.Get(li.Code);
                if (p == null) { allBig = false; sizes.Add(li.Code + " fehlt"); continue; }
                sizes.Add(li.Code + " " + p.Count);
                if (p.Count < 150000) allBig = false;
            }
            sw.Stop();
            Check(allBig, "jede Sprache hat mindestens 150.000 Wörter: " + string.Join(", ", sizes));
            Console.WriteLine("  (alle 11 Sprachen laden: " + sw.ElapsedMilliseconds + " ms)");
            Check(sw.ElapsedMilliseconds < 30000, "alle 11 Sprachen zusammen laden in unter 30 s (Mono, Test-PC)");

            Scored sc;
            Check(lm.Get("de").TryGet("Kartons", out sc) && lm.Get("de").TryGet("freundlichen", out sc), "Wortformen dabei (Kartons, freundlichen)");
            Check(lm.Get("da").TryGet("bilerne", out sc) && lm.Get("da").TryGet("venlig", out sc), "Dänisch: bilerne, venlig");
            Check(lm.Get("tr").TryGet("evlerde", out sc) || lm.Get("tr").TryGet("evlerimiz", out sc), "Türkisch: Formen von ev (evlerde/evlerimiz)");

            // Geschwindigkeit der Vorschläge
            var words = new[] { "k", "ka", "kar", "s", "sch", "ver", "ü", "die", "freund", "a", "be", "st" };
            sw = Stopwatch.StartNew();
            int n = 0;
            for (int r = 0; r < 20; r++)
                foreach (var w in words) { set.Suggest(w, "mit", 8, false); n++; }
            sw.Stop();
            double per = sw.Elapsed.TotalMilliseconds / n;
            Console.WriteLine("  (ein Vorschlag dauert im Schnitt " + per.ToString("0.00") + " ms)");
            Check(per < 25, "Vorschläge bleiben schnell (unter 25 ms pro Tastendruck)");

            // ---------- Wortfolgen ----------
            set.SetStore("de", new LearningStore { Code = "de" });
            Check(de.Pairs != null && de.Pairs.Count > 1000, "deutsche Wortfolgen geladen: " + (de.Pairs == null ? 0 : de.Pairs.Count));
            var missing = LanguageManager.BuiltIns.Where(b => lm.Get(b.Code).Pairs == null || lm.Get(b.Code).Pairs.Count < 300).Select(b => b.Code).ToList();
            Check(missing.Count == 0, "alle 11 Sprachen haben Wortfolgen" + (missing.Count > 0 ? ", fehlt: " + string.Join(" ", missing) : ""));
            s = set.Suggest("", "vielen", 5, false);
            Check(s.Contains("Dank"), "nach \"vielen\" kommt Dank: " + string.Join(", ", s));
            s = set.Suggest("", "freundlichen", 5, false);
            Check(s.Count > 0 && s[0] == "Grüßen", "nach \"freundlichen\" zuerst Grüßen: " + string.Join(", ", s));
            s = set.Suggest("", "mit", 8, false);
            Check(s.Contains("freundlichen") || s.Contains("dem") || s.Contains("der"), "nach \"mit\" typische Folgewörter: " + string.Join(", ", s));
            s = set.Suggest("", "ich", 8, false);
            Check(s.Contains("habe"), "nach \"ich\" kommt habe: " + string.Join(", ", s));
            s = set.Suggest("gr", "freundlichen", 5, false);
            Check(s.Count > 0 && s[0] == "Grüßen", "beim Weitertippen: gr nach freundlichen -> Grüßen: " + string.Join(", ", s));
            s = setEn.Suggest("", "thank", 5, false);
            Check(s.Contains("you"), "englisch: nach thank kommt you: " + string.Join(", ", s));

            // Eigenes hat Vorrang
            var own = new LearningStore { Code = "de" };
            set.SetStore("de", own);
            for (int i = 0; i < 5; i++) { own.Learn("vielen", null, false); own.Learn("Küsschen", "vielen", false); }
            s = set.Suggest("", "vielen", 5, false);
            Check(s.Count > 0 && s[0] == "Küsschen", "gelernte Wortfolge steht vor der eingebauten: " + string.Join(", ", s));
            Check(s.Contains("Dank"), "eingebaute bleibt dahinter: " + string.Join(", ", s));

            // Türkisch: großes I richtig
            var tr = lm.Get("tr");
            Check(tr.Pairs != null && tr.Pairs.Key("IŞIK") == "ışık" && tr.Pairs.Key("İyi") == "iyi", "Türkisch: I/İ richtig klein");

            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
