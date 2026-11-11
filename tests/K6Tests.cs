using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tastatur
{
    internal static class K6Tests
    {
        public static void Run(string dePath, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- Echte Tastendrücke ---");
            // ohne Windows: Zeichen → VK für Buchstaben/Ziffern, ß = 0xDB wie auf deutscher Tastatur
            var pk = new PhysicalKeys(c => c == 'ß' ? (ushort)0xDB : PhysicalKeys.DefaultCharToVk(c));
            var L = KeyLayouts.CreateGerman();
            var nav = KeyLayouts.CreateNav();
            var keys = L.SelectMany(x => x).ToList();
            var navKeys = nav.SelectMany(x => x).ToList();
            Func<ushort, List<KeyDef>, List<string>> hits = (vk, ks) => ks.Where(k => pk.Matches(k, vk)).Select(k => k.Label ?? k.Normal).ToList();

            Check(hits(0xA2, keys).SequenceEqual(new[] { "Strg" }), "Strg links → Strg");
            Check(hits(0xA1, keys).Count == 2 && hits(0xA1, keys).All(x => x.Contains("Shift")), "Shift rechts → beide Shift-Tasten");
            Check(hits(0x56, keys).SequenceEqual(new[] { "v" }), "V → v");
            Check(hits(0x0D, keys).Count == 1 && hits(0x0D, keys)[0].StartsWith("Enter"), "Enter → Enter");
            Check(hits(0xA5, keys).SequenceEqual(new[] { "AltGr" }) && hits(0xA4, keys).SequenceEqual(new[] { "Alt" }), "Alt rechts → AltGr, Alt links → Alt");
            Check(hits(0xDB, keys).SequenceEqual(new[] { "ß" }), "ß über das Layout");
            Check(hits(0x74, keys).Count == 0 && hits(0x74, navKeys).Count == 1, "F5 nur in der Fn-Ebene");
            Check(hits(0x13, keys).Count == 0 && hits(0x6D, keys).Count == 0, "Pause und NumMinus gibt es nicht auf der Tastatur");
            Check(PhysicalKeys.ComboText(Modifiers.Ctrl, 0x56) == "Strg+V" && PhysicalKeys.ComboText(Modifiers.Ctrl | Modifiers.Shift, 0) == "Strg+Shift"
                && PhysicalKeys.ComboText(Modifiers.None, 0x6D) == "NumMinus", "Text für die Statuszeile");

            var t0 = new DateTime(2026, 11, 6, 12, 0, 0);
            var v = keys.First(k => k.Normal == "v");
            var strg = keys.First(k => k.Label == "Strg");
            pk.Down(0xA2, t0); pk.Down(0x56, t0);
            Check(pk.Glow(v, t0.AddSeconds(10), 1500) == 1 && pk.Glow(strg, t0, 1500) == 1, "rot, solange gedrückt (auch lange)");
            Check(pk.HeldMods == Modifiers.Ctrl, "gehaltene Sondertasten");
            pk.Up(0x56, t0.AddMilliseconds(100));
            bool chord = pk.Up(0xA2, t0.AddMilliseconds(120));
            Check(!chord, "Strg+V ist keine reine Sondertasten-Kombi");
            Check(pk.Glow(v, t0.AddMilliseconds(600), 1500) == 1, "nach dem Loslassen erst noch voll rot");
            double mid = pk.Glow(v, t0.AddMilliseconds(1450), 1500);
            Check(mid > 0 && mid < 1, "dann blendet es aus");
            Check(pk.Glow(v, t0.AddMilliseconds(1700), 1500) == 0 && !pk.Active(t0.AddMilliseconds(1800), 1500), "nach Ablauf wieder normal");
            Check(pk.Glow(v, t0.AddMilliseconds(150), 0) == 0, "Nachleuchten 0 = sofort aus");
            pk.Down(0xA2, t0); pk.Down(0xA0, t0);
            pk.Up(0xA0, t0); bool wispr = pk.Up(0xA2, t0);
            Check(wispr, "Strg+Shift (Wispr Flow) wird als Kombi erkannt");
            pk.Down(0xA0, t0);
            Check(!pk.Up(0xA0, t0), "Shift allein zählt nicht");

            Console.WriteLine("\n--- Wort pro Fenster ---");
            string resDir = Path.GetDirectoryName(Path.GetFullPath(dePath));
            var p = new Predictor();
            using (var s = File.OpenRead(dePath)) p.Load(s);
            var o = new FakeOutput();
            var e = new KeyboardEngine(o, p, new Settings());
            var states = new WindowStates();
            IntPtr A = new IntPtr(100), B = new IntPtr(200);
            Action<string> type = str =>
            {
                foreach (char c in str)
                {
                    var k = c == ' ' ? keys.First(x => x.Kind == KeyKind.Space) : keys.First(x => x.Kind == KeyKind.Char && x.Normal == c.ToString());
                    e.Press(k);
                }
            };
            Func<IntPtr, IntPtr, int, bool> sw = (from, to, dummy) =>
            {
                states.Save(from, e.SaveState(), t0);
                e.RestoreState(states.Take(to, t0));
                return true;
            };

            type("ich bin hal");
            var sugA = e.Suggestions();
            sw(A, B, 0);
            Check(e.CurrentWord == "", "neues Fenster: nichts bekannt");
            type("xyz");
            sw(B, A, 0);
            Check(e.CurrentWord == "hal", "zurück in A: Wort \"hal\" ist wieder da");
            Check(e.Suggestions().SequenceEqual(sugA), "Vorschläge passen wieder");
            Check(e.PreviousWord == "bin", "Wort davor auch");
            type(" ");
            e.ToggleCase();
            Check(o.Doc.ToString().EndsWith("Hal "), "Groß/Klein wirkt auf das Wort aus A: " + o.Doc);
            sw(A, B, 0);
            Check(e.CurrentWord == "xyz", "und B hat sein eigenes Wort");

            e.ResetWord(false);   // so verhält sich ein Klick im aktiven Fenster und ein echter Tastendruck
            Check(e.CurrentWord == "" && e.LastWord == null, "Klick im aktiven Fenster / echter Tastendruck vergisst");
            states.Save(B, e.SaveState(), t0);
            Check(states.Take(B, t0) == null, "leerer Zustand wird nicht gemerkt");

            type("geh");
            e.PasswordMode = true;
            type("geheim");
            Check(e.SaveState() == null, "im Passwortfeld kein Zustand");
            states.Save(B, e.SaveState(), t0);
            Check(states.Take(B, t0) == null, "Passwortfeld landet nicht im Speicher");
            e.PasswordMode = false;

            var many = new WindowStates();
            var st = new EngineState { Word = "x" };
            for (int i = 1; i <= 35; i++) many.Save(new IntPtr(i), st, t0.AddSeconds(i));
            Check(many.Count == WindowStates.MaxWindows && many.Take(new IntPtr(1), t0.AddSeconds(40)) == null && many.Take(new IntPtr(35), t0.AddSeconds(40)) != null,
                "höchstens 30 Fenster, das älteste fliegt raus");
            Check(many.Take(new IntPtr(20), t0.AddMinutes(31)) == null, "nach 30 Minuten verworfen");
            many.Prune(t0.AddSeconds(50), h => h.ToInt64() % 2 == 0);
            Check(many.Take(new IntPtr(33), t0.AddSeconds(50)) == null && many.Take(new IntPtr(32), t0.AddSeconds(50)) != null, "geschlossene Fenster werden gelöscht");
        }
    }
}
