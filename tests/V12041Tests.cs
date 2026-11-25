using System;
using System.Collections.Generic;
using System.Linq;

namespace Tastatur
{
    /// <summary>1.2.04.1: Rücktaste/Enter/Leerzeichen vom Quadstick mitzählen, Satzzeichen unten fett.</summary>
    internal static class V12041Tests
    {
        /// <summary>Merkt sich, mit welchem Wort davor gefragt wurde, und schlägt nach "Grüße" "Morten" vor.</summary>
        private class PairSuggester : ISuggester
        {
            public string LastPrev;
            public List<string> Suggest(string typed, string prev, int max, bool capitalize)
            {
                LastPrev = prev;
                var l = new List<string>();
                if (typed.Length == 0 && string.Equals(prev, "grüße", StringComparison.OrdinalIgnoreCase)) l.Add("Morten");
                return l;
            }
            public List<string> SuggestNumbers(string typed, int max) { return new List<string>(); }
            public List<string> Corrections(string typed, int max, bool capitalize, bool wholeWord) { return new List<string>(); }
            public bool Knows(string word) { return true; }
        }

        private static KeyDef Find(List<List<KeyDef>> rows, string normal)
        {
            return rows.SelectMany(r => r).First(k => k.Kind == KeyKind.Char && k.Normal == normal);
        }

        public static void Run(Predictor p, Action<bool, string> Check)
        {
            External(p, Check);
            Pairs(Check);
            Bold(Check);
        }

        private static void External(Predictor p, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.04.1: Tasten vom Quadstick ---");
            var o = new FakeOutput();
            var e = new KeyboardEngine(o, p, new Settings());
            var L = KeyLayouts.CreateGerman();
            var space = L.SelectMany(r => r).First(k => k.Kind == KeyKind.Space);
            Action<string> type = s => { foreach (char c in s) { if (c == ' ') e.Press(space); else e.Press(Find(L, c.ToString())); } };

            e.ResetWord(false);
            type("hallo hauz");
            o.Log.Clear();
            bool handled = e.ExternalKey(InputSender.VK_BACK, Modifiers.None);
            Check(handled && e.CurrentWord == "hau", "Rücktaste vom Quadstick kürzt das Wort: '" + e.CurrentWord + "'");
            Check(o.Log.Count == 0, "Rücktaste von außen wird nicht noch einmal gesendet");
            Check(e.Suggestions().Count > 0, "Vorschläge bleiben stehen");
            Check(e.PreviousWord == "hallo", "Wort davor bleibt bekannt");

            // gehaltene Rücktaste (Wiederholung): jedes Zeichen zählt
            e.ExternalKey(InputSender.VK_BACK, Modifiers.None);
            e.ExternalKey(InputSender.VK_BACK, Modifiers.None);
            e.ExternalKey(InputSender.VK_BACK, Modifiers.None);
            Check(e.CurrentWord == "", "drei weitere Rücktasten: Wort leer");
            e.ExternalKey(InputSender.VK_BACK, Modifiers.None);
            e.ExternalKey(InputSender.VK_BACK, Modifiers.None);
            e.ExternalKey(InputSender.VK_BACK, Modifiers.None);
            type("p");
            Check(e.CurrentWord == "halp", "weiter zurück ins Wort davor und weitertippen: '" + e.CurrentWord + "'");

            // Leerzeichen von außen beendet das Wort wie auf der Bildschirmtastatur
            e.ResetWord(false);
            type("guten");
            Check(e.ExternalKey(InputSender.VK_SPACE, Modifiers.None) && e.CurrentWord == "" && e.PreviousWord == "guten",
                "Leerzeichen vom Quadstick: Wort fertig, Wortfolge geht weiter");

            // mit Strg/Alt/Win: unbekannt, der Aufrufer vergisst das Wort
            e.ResetWord(false);
            type("abc");
            Check(!e.ExternalKey(InputSender.VK_BACK, Modifiers.Ctrl), "Strg+Rücktaste wird nicht mitgezählt");
            Check(!e.ExternalKey(InputSender.VK_BACK, Modifiers.Alt), "Alt+Rücktaste wird nicht mitgezählt");
            Check(e.ExternalKey(InputSender.VK_BACK, Modifiers.Shift) && e.CurrentWord == "ab", "Shift+Rücktaste ist eine normale Rücktaste");
            Check(!e.ExternalKey(InputSender.VK_LEFT, Modifiers.None), "Pfeiltaste: unbekannt, Wort wird vergessen");
            Check(!e.ExternalKey(InputSender.VK_DELETE, Modifiers.None), "Entf: unbekannt");
            Check(!e.ExternalKey(0x41, Modifiers.None), "Buchstabe von außen: unbekannt");

            // Passwortfeld: wie bisher
            e.ResetWord(false);
            e.PasswordMode = true;
            Check(!e.ExternalKey(InputSender.VK_BACK, Modifiers.None), "im Passwortfeld wie bisher");
            e.PasswordMode = false;

            // eigene Rücktaste und Rücktaste von außen gemischt
            e.ResetWord(false); o.Doc.Clear();
            type("tes");
            e.ExternalKey(InputSender.VK_BACK, Modifiers.None); o.Doc.Length--;   // kam am Fenster an, nicht über die Tastatur
            type("st");
            Check(e.CurrentWord == "test", "nach Rücktaste von außen weiterschreiben: '" + e.CurrentWord + "'");
        }

        private static void Pairs(Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.04.1: Wortfolge über Absätze ---");
            var sg = new PairSuggester();
            var o = new FakeOutput();
            var e = new KeyboardEngine(o, sg, new Settings());
            var L = KeyLayouts.CreateGerman();
            var space = L.SelectMany(r => r).First(k => k.Kind == KeyKind.Space);
            var enter = L.SelectMany(r => r).First(k => k.Kind == KeyKind.Special && k.Vk == InputSender.VK_RETURN);
            var learned = new List<string>();
            e.WordFinished += (w, prev, st) => learned.Add(prev + ">" + w);
            Action<string> type = s => { foreach (char c in s) { if (c == ' ') e.Press(space); else e.Press(Find(L, c.ToString())); } };

            e.ResetWord(true);
            type("liebe grüße");
            e.ExternalKey(InputSender.VK_RETURN, Modifiers.None);
            e.ExternalKey(InputSender.VK_RETURN, Modifiers.None);
            Check(e.Suggestions().Contains("Morten") && string.Equals(sg.LastPrev, "grüße", StringComparison.OrdinalIgnoreCase), "Quadstick: Grüße + Enter + Enter: Morten wird vorgeschlagen");
            Check(e.SentenceStart, "nach Enter ist Satzanfang");
            e.ExternalKey(InputSender.VK_BACK, Modifiers.None);
            e.ExternalKey(InputSender.VK_BACK, Modifiers.None);
            Check(string.Equals(e.CurrentWord, "grüße", StringComparison.OrdinalIgnoreCase), "zwei Rücktasten: wieder im Wort Grüße: '" + e.CurrentWord + "'");

            e.ResetWord(true);
            type("liebe grüße");
            e.Press(enter); e.Press(enter);
            Check(e.Suggestions().Contains("Morten"), "Bildschirmtastatur: Grüße + Enter + Enter: Morten wird vorgeschlagen");
            learned.Clear();
            type("morten ");
            Check(learned.Any(x => string.Equals(x, "grüße>morten", StringComparison.OrdinalIgnoreCase)), "Wortfolge Grüße > Morten wird gelernt: " + string.Join(", ", learned));

            e.ResetWord(true);
            type("fertig");
            e.Press(Find(L, "."));
            e.Press(enter);
            e.Suggestions();
            Check(sg.LastPrev == null, "nach Punkt und Enter: keine Wortfolge über den Satz hinaus");
        }

        private static void Bold(Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.04.1: Satzzeichen unten fett ---");
            var L = KeyLayouts.CreateGerman();
            var r3 = L[3];
            Check(Find(L, ".").Bold && Find(L, ",").Bold && Find(L, "-").Bold, "Punkt, Komma, Bindestrich fett");
            Check(!Find(L, "y").Bold && !Find(L, "m").Bold && !Find(L, "1").Bold, "Buchstaben und Ziffern normal");
            var bottom = L[L.Count - 1].Where(k => k.Kind == KeyKind.Char).ToList();
            Check(bottom.Count == 4 && bottom.All(k => k.Bold), "die vier Sonderzeichen unten fett");
            foreach (var code in KeyLayouts.Codes)
            {
                var rows = KeyLayouts.Create(code);
                bool ok = rows.SelectMany(r => r).Where(k => k.Bold).All(k => k.Kind == KeyKind.Char && !k.Normal.Any(char.IsLetterOrDigit))
                    && rows[rows.Count - 1].Where(k => k.Kind == KeyKind.Char).All(k => k.Bold);
                if (!ok || code == "de") Check(ok, "fett nur bei Satzzeichen (" + code + ")");
            }
        }
    }
}
