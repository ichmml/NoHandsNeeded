using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Tastatur
{
    internal class FakeOutput : IOutput
    {
        public readonly StringBuilder Doc = new StringBuilder();
        public readonly List<string> Log = new List<string>();
        public void SendText(string t) { Doc.Append(t); Log.Add("T:" + t); }
        public void SendVk(ushort vk, Modifiers m)
        {
            Log.Add("VK:" + vk.ToString("X2") + "+" + m);
            if (vk == InputSender.VK_BACK && m == Modifiers.None && Doc.Length > 0) Doc.Length--;
            if (vk == InputSender.VK_RETURN && m == Modifiers.None) Doc.Append('\n');
        }
        public bool SendCharWithModifiers(char c, Modifiers m) { Log.Add("KEY:" + c + "+" + m); return true; }
        public void SendBackspaces(int n) { Log.Add("BS:" + n); Doc.Length -= Math.Min(n, Doc.Length); }
    }

    internal static class Tests
    {
        static int fails;
        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "OK   " : "FAIL ") + what);
            if (!ok) fails++;
        }

        static KeyDef Find(List<List<KeyDef>> rows, string normal)
        {
            return rows.SelectMany(r => r).First(k => k.Kind == KeyKind.Char && k.Normal == normal);
        }
        static KeyDef FindLabel(List<List<KeyDef>> rows, string label)
        {
            return rows.SelectMany(r => r).First(k => k.Label == label);
        }

        static int Main(string[] args)
        {
            Check(Marshal.SizeOf(typeof(Native.INPUT)) == (IntPtr.Size == 8 ? 40 : 28), "INPUT-Größe passt zu Windows (" + Marshal.SizeOf(typeof(Native.INPUT)) + ")");

            foreach (var rows in new[] { KeyLayouts.CreateGerman(), KeyLayouts.CreateNav() })
                foreach (var r in rows)
                    Check(Math.Abs(r.Sum(k => k.Width) - 15f) < 0.01, "Reihe hat 15 Einheiten: " + r.Sum(k => k.Width));
            foreach (var rows in new[] { KeyLayouts.CreateGerman(), KeyLayouts.CreateNav() })
            {
                var last = rows[rows.Count - 1];
                Check(last[last.Count - 1].Kind == KeyKind.Drag, "Verschieben-Taste ganz rechts unten");
            }

            var p = new Predictor();
            using (var s = File.OpenRead(args[0])) p.Load(s);
            Check(p.Count > 50000, "Wortliste geladen: " + p.Count);

            var sug = p.Suggest("hau", 5, false);
            Console.WriteLine("     hau -> " + string.Join(", ", sug));
            Check(sug.Contains("Haus") || sug.Contains("hauptsächlich"), "Vorschläge für 'hau'");
            sug = p.Suggest("stra", 5, false);
            Console.WriteLine("     stra -> " + string.Join(", ", sug));
            Check(sug.Contains("Straße"), "ß wiederhergestellt");
            sug = p.Suggest("fü", 5, true);
            Console.WriteLine("     fü (Satzanfang) -> " + string.Join(", ", sug));
            Check(sug.All(w => char.IsUpper(w[0])), "Satzanfang groß");
            sug = p.Suggest("", 5, false);
            Console.WriteLine("     leer -> " + string.Join(", ", sug));

            var settings = new Settings();
            var o = new FakeOutput();
            var e = new KeyboardEngine(o, p, settings);
            var L = KeyLayouts.CreateGerman();
            Action<string> type = str => { foreach (char c in str) { if (c == ' ') e.Press(L[4][4]); else e.Press(Find(L, c.ToString())); } };

            type("hallo");
            Check(o.Doc.ToString() == "hallo", "tippen: " + o.Doc);
            Check(e.CurrentWord == "hallo", "Wortpuffer");
            e.Press(FindLabel(L, "⌫"));
            Check(e.CurrentWord == "hall", "Backspace kürzt Puffer");

            // Vorschlag übernehmen
            o.Doc.Clear(); e.ResetWord(false);
            type("stra");
            e.PressSuggestion("Straße");
            Check(o.Doc.ToString() == "Straße ", "Vorschlag ersetzt mit Großschreibung: '" + o.Doc + "'");
            Check(e.CurrentWord == "", "nach Vorschlag + Leerzeichen leer");

            // Satzanfang automatisch groß
            o.Doc.Clear(); e.ResetWord(false);
            type("ja. ok");
            Check(o.Doc.ToString() == "ja. Ok", "Auto-Groß nach Punkt: '" + o.Doc + "'");

            // Shift einmal
            o.Doc.Clear(); e.ResetWord(false);
            e.Press(FindLabel(L, "⇧ Shift")); type("ab");
            Check(o.Doc.ToString() == "Ab", "Shift einmal: '" + o.Doc + "'");
            // Shift 2x = wieder aus (kein Feststellen mehr)
            o.Doc.Clear();
            e.Press(FindLabel(L, "⇧ Shift")); e.Press(FindLabel(L, "⇧ Shift"));
            Check(e.Shift == ModState.Off, "Shift 2x getippt: wieder aus, nicht fest");
            type("ab");
            Check(o.Doc.ToString() == "ab", "danach klein: '" + o.Doc + "'");
            e.Press(FindLabel(L, "Strg")); e.Press(FindLabel(L, "Strg"));
            Check(e.Ctrl == ModState.Off, "Strg 2x: aus");

            // Strg+C
            o.Log.Clear();
            e.Press(FindLabel(L, "Strg")); type("c");
            Check(o.Log.Last() == "KEY:c+Ctrl", "Strg+C: " + o.Log.Last());
            Check(e.Ctrl == ModState.Off, "Strg nach einem Druck wieder aus");

            // Shift 1x + mehrere Pfeile = Markieren, danach wieder ohne Shift
            o.Log.Clear();
            e.Press(FindLabel(L, "⇧ Shift"));
            e.Press(FindLabel(L, "←")); e.Press(FindLabel(L, "←"));
            Check(o.Log.Count >= 2 && o.Log[o.Log.Count - 1] == "VK:25+Shift" && o.Log[o.Log.Count - 2] == "VK:25+Shift", "Shift+Pfeil mehrfach: " + string.Join(" | ", o.Log));
            o.Doc.Clear(); type("x");
            Check(o.Doc.ToString() == "x" && e.Shift == ModState.Off, "nach dem Markieren wieder klein: '" + o.Doc + "'");

            // AltGr
            o.Doc.Clear(); e.ResetWord(false);
            var altgr = L.SelectMany(r => r).First(k => k.Kind == KeyKind.AltGr);
            e.Press(altgr); type("q"); e.Press(altgr); type("e");
            Check(o.Doc.ToString() == "@€", "AltGr @ und €: '" + o.Doc + "'");

            // Enter -> Satzanfang
            o.Doc.Clear(); e.ResetWord(false);
            type("x"); e.Press(FindLabel(L, "Enter ↵")); type("y");
            Check(o.Doc.ToString() == "x\nY", "nach Enter groß");

            // Vorschlag mitten im Wort mit anderer Schreibweise: Backspaces
            o.Doc.Clear(); o.Log.Clear(); e.ResetWord(false);
            type("strasse");
            e.PressSuggestion("Straße");
            Check(o.Doc.ToString() == "Straße ", "Ersetzen per Backspace: '" + o.Doc + "'");

            MoreTests.Run(p, Check);
            LangTests.Run(args[0], Check);
            Stage2Tests.Run(args[0], Check);
            K5Tests.Run(args[0], Check);
            K6Tests.Run(args[0], Check);
            V8Tests.Run(args[0], Check);
            K81Tests.Run(args[0], Check);
            V83Tests.Run(args[0], Check);
            V84Tests.Run(Check);
            V85Tests.Run(Check);
            I18nTests.Run(args[0], Check);
            MailTests.Run(args[0], Check);
            IbanTests.Run(args[0], Check);
            OwnSecretTests.Run(Check);

            Console.WriteLine(fails == 0 ? "\nAlle Tests bestanden." : "\n" + fails + " Fehler.");
            return fails;
        }
    }
}
