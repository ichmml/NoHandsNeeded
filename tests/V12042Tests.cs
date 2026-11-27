using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Tastatur
{
    /// <summary>1.2.04.2: Groß/Klein per globalem Kürzel, Farbauswahl, fette Schrift.</summary>
    internal static class V12042Tests
    {
        private static KeyDef Find(List<List<KeyDef>> rows, string normal)
        {
            return rows.SelectMany(r => r).First(k => k.Kind == KeyKind.Char && k.Normal == normal);
        }

        public static void Run(Predictor p, Action<bool, string> Check)
        {
            CaseHotkey(p, Check);
            HotkeySettings(Check);
            Colors(Check);
            BoldFont(Check);
        }

        private static void CaseHotkey(Predictor p, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.04.2: Groß/Klein per Kürzel ---");
            var o = new FakeOutput();
            var e = new KeyboardEngine(o, p, new Settings());
            var L = KeyLayouts.CreateGerman();
            var space = L.SelectMany(r => r).First(k => k.Kind == KeyKind.Space);
            Action<string> type = s => { foreach (char c in s) { if (c == ' ') e.Press(space); else e.Press(Find(L, c.ToString())); } };

            e.ResetWord(false);
            type("das haus ");
            o.Log.Clear();
            // Das Kürzel ruft dieselbe Funktion wie die Aa-Taste auf (KeyboardForm.ToggleCaseFromHotkey → ToggleCase)
            Check(e.CanToggleCase, "nach Leerzeichen ist das letzte Wort bekannt");
            e.ToggleCase();
            Check(o.Doc.ToString() == "das Haus ", "Kürzel 1x: '" + o.Doc + "'");
            Check(!o.Log.Any(x => x.Contains("Ctrl")), "keine Zwischenablage, nur Rücktaste und Text");
            e.ToggleCase();
            Check(o.Doc.ToString() == "das haus ", "Kürzel 2x: wieder klein: '" + o.Doc + "'");
            e.ToggleCase();
            Check(o.Doc.ToString() == "das Haus ", "Kürzel 3x: wechselt weiter reihum: '" + o.Doc + "'");

            // mitten im Wort
            type("aut");
            e.ToggleCase();
            Check(o.Doc.ToString() == "das Haus Aut", "wirkt auch auf das angefangene Wort: '" + o.Doc + "'");

            // unbekanntes Wort (Klick ins Fenster, Fensterwechsel): nichts verändern
            e.ResetWord(false);
            string before = o.Doc.ToString();
            o.Log.Clear();
            Check(!e.CanToggleCase, "nach Fensterwechsel kein Wort bekannt");
            e.ToggleCase();
            Check(o.Doc.ToString() == before && o.Log.Count == 0, "ohne bekanntes Wort wird nichts gesendet");

            // Aufblinken: an/aus im Wechsel, danach aus
            var now = DateTime.UtcNow;
            var until = now.AddMilliseconds(720);
            var seq = Enumerable.Range(0, 7).Select(i => KeyboardForm.BlinkOn(now.AddMilliseconds(i * 120 + 10), until)).ToList();
            Check(seq.Contains(true) && seq.Contains(false), "Aa-Taste blinkt (an und aus): " + string.Join(",", seq));
            Check(!KeyboardForm.BlinkOn(until.AddMilliseconds(1), until), "nach dem Blinken wieder normal");
        }

        private static void HotkeySettings(Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.04.2: Kürzel in den Einstellungen ---");
            var s = new Settings();
            Check(s.HotkeyCase == "NumMal" && KeyCombo.Parse(s.HotkeyCase) != null, "Standard-Kürzel: Ziffernblock Stern (" + s.HotkeyCase + ")");
            Check(KeyCombo.Parse("NumMultiply").ToString() == KeyCombo.Parse("NumMal").ToString() && KeyCombo.Parse("Num*") != null, "NumMultiply und Num* meinen dieselbe Taste");
            var back = Settings.FromText(s.ToText());
            Check(back.HotkeyCase == "NumMal", "Kürzel wird gespeichert und gelesen");

            // alte Einstellungen (Version 6) ohne das neue Kürzel: wird einmalig angelegt, der Rest bleibt
            var old = Settings.FromText("einstellungen_version = 6\nkuerzel_einblenden = F2\nkuerzel_makros = NumPlus\nkuerzel_sprache = Strg+Alt+Shift+L\n");
            Check(old.HotkeyCase == "NumMal", "alte Einstellungen: Groß/Klein-Kürzel neu angelegt");
            Check(old.HotkeyShow == "F2" && old.HotkeyMacros == "NumPlus" && old.HotkeyLanguage == "Strg+Alt+Shift+L", "andere Kürzel bleiben unverändert");

            // Ziffernblock Stern schon anders belegt: nicht doppelt vergeben
            var taken = Settings.FromText("einstellungen_version = 6\nkuerzel_makros = Num*\n");
            Check(taken.HotkeyCase == "" && taken.HotkeyMacros == "Num*", "Taste schon belegt: Groß/Klein bleibt aus");

            // selbst ausgeschaltet bleibt aus, selbst geändert bleibt
            Check(Settings.FromText("einstellungen_version = 7\nkuerzel_grossklein = \n").HotkeyCase == "", "selbst ausgeschaltet bleibt aus");
            Check(Settings.FromText("einstellungen_version = 7\nkuerzel_grossklein = NumPlus\n").HotkeyCase == "NumPlus", "selbst geändert bleibt");

            // Registrieren (ohne Windows): neben den anderen Kürzeln, eigene Nummer
            var wanted = new List<HotkeyRequest>
            {
                new HotkeyRequest { Id = HotkeyIds.Show, Name = "show", Combo = "Pause" },
                new HotkeyRequest { Id = HotkeyIds.Macros, Name = "macros", Combo = "NumMinus" },
                new HotkeyRequest { Id = HotkeyIds.Case, Name = "case", Combo = "NumMal" },
            };
            var failed = new List<HotkeyStatus>();
            var plan = HotkeyPlan.Build(wanted, k => Tuple.Create(k.Vk, Modifiers.None), failed);
            var c = plan.FirstOrDefault(r => r.Id == HotkeyIds.Case);
            Check(failed.Count == 0 && c != null && c.Vk == 0x6A, "Groß/Klein-Kürzel wird als Num * (0x6A) registriert");
            failed.Clear();
            var dup = HotkeyPlan.Build(new List<HotkeyRequest>
            {
                new HotkeyRequest { Id = HotkeyIds.Macros, Name = "macros", Combo = "NumMal" },
                new HotkeyRequest { Id = HotkeyIds.Case, Name = "case", Combo = "Num*" },
            }, k => Tuple.Create(k.Vk, Modifiers.None), failed);
            Check(dup.Count == 1 && failed.Count == 1 && failed[0].Id == HotkeyIds.Case, "doppelt vergeben wird gemeldet");
        }

        private static void Colors(Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.04.2: Farbauswahl ---");
            Check(Theme.Presets.Length == 6 && Theme.Presets.All(Theme.IsKnown) && Theme.IsKnown("eigen") && !Theme.IsKnown("lila"), "sechs Farbschemas und eigene Farben");
            var std = Theme.Get("dunkel");
            var old = Theme.Get("irgendwas");
            Check(std.Background == old.Background && std.Key == Color.FromArgb(54, 57, 66), "Standard ist das bisherige Schema");
            Check(Theme.Get("hell").IsLight && !std.IsLight, "hell/dunkel erkannt");
            foreach (var k in Theme.Presets)
            {
                var bad = Theme.Get(k).LowContrast();
                Check(bad.Count == 0, "Schema " + k + " gut lesbar" + (bad.Count > 0 ? ": " + string.Join(", ", bad) : ""));
            }
            var hc = Theme.Get("kontrast");
            Check(hc.Background == Color.FromArgb(0, 0, 0) && hc.Text == Color.FromArgb(255, 230, 0), "Hoher Kontrast: Schwarz/Gelb");

            // Einstellungen: alte Werte, neue Werte, Unsinn
            Check(Settings.FromText("farbschema = hell\n").Theme == "hell", "altes Schema hell bleibt");
            Check(Settings.FromText("farbschema = dunkel\n").Theme == "dunkel", "altes Schema dunkel bleibt (= Standard)");
            Check(Settings.FromText("farbschema = blau\n").Theme == "blau" && Settings.FromText("farbschema = quatsch\n").Theme == Theme.Default, "neue Schemas und unbekannte Werte");

            // eigene Farben
            var t = Theme.Get("blau");
            t.SetArea("tasten", Color.FromArgb(200, 30, 40));
            string custom = t.ToCustom();
            var s = new Settings { Theme = "eigen", CustomColors = custom };
            var back = Settings.FromText(s.ToText());
            var bt = Theme.For(back);
            Check(back.Theme == "eigen" && bt.Key == Color.FromArgb(200, 30, 40) && bt.Background == t.Background, "eigene Farben gespeichert und angewendet");
            Check(Theme.Areas.All(a => bt.GetArea(a) == t.GetArea(a)), "alle neun Bereiche überstehen Speichern und Laden");
            Check(bt.Accent == bt.Dwell && bt.KeyHover != bt.Key, "Hervorhebung folgt der Verweilfarbe");
            var partial = Theme.FromCustom("tasten=#FF0000;quatsch=#123456;hintergrund=kaputt");
            Check(partial.Key == Color.FromArgb(255, 0, 0) && partial.Background == std.Background, "fehlende und kaputte Werte bekommen den Standard");
            Check(Theme.NormalizeCustom("tasten=#ff0000;quatsch=#123456;hintergrund=kaputt") == "tasten=#FF0000", "eigene Farben bereinigt");

            // Kontrastprüfung
            Check(Math.Abs(Theme.Contrast(Color.Black, Color.White) - 21) < 0.01 && Math.Abs(Theme.Contrast(Color.Gray, Color.Gray) - 1) < 0.01, "Kontrast nach WCAG");
            var grey = Theme.Get("dunkel");
            grey.SetArea("beschriftung", Color.FromArgb(70, 72, 80));
            Check(grey.LowContrast().Count >= 2, "graue Schrift auf grauen Tasten wird gemeldet");
            Check(Theme.Palette.Length == 24, "Palette mit 24 Farben");

            // Export/Import: Farben gehören zu den Einstellungen
            var src = new Settings { Theme = "eigen", CustomColors = custom, BoldFont = false };
            var dst = new Settings();
            dst.CopyFrom(Settings.FromText(src.ToText()));
            Check(dst.Theme == "eigen" && dst.CustomColors == Theme.NormalizeCustom(custom) && !dst.BoldFont, "Import übernimmt Farben und Schrift");
            var oldExport = Settings.FromText("einstellungen_version = 6\nfenster_x = 10\n");
            Check(oldExport.Theme == Theme.Default && oldExport.CustomColors == "" && oldExport.BoldFont, "alter Export ohne Farben: Standard-Schema, fette Schrift");
            // Makros mit eigener Farbe behalten sie
            Check(Macro.ColorValue("grün").HasValue, "Makro-Farben bleiben verfügbar");
        }

        private static void BoldFont(Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.04.2: Fette Schrift ---");
            Check(new Settings().BoldFont, "fette Schrift ab Werk an");
            Check(KeyboardForm.LabelStyle(true) == FontStyle.Bold && KeyboardForm.LabelStyle(false) == FontStyle.Regular, "Schalter wirkt auf den Schriftstil");
            Check(!Settings.FromText("fette_schrift = nein\n").BoldFont && Settings.FromText("fette_schrift = ja\n").BoldFont, "Schalter wird gespeichert");
            var L = KeyLayouts.CreateGerman();
            Check(Find(L, ".").Bold && Find(L, ",").Bold, "Satzzeichen bleiben zusätzlich größer");
        }
    }
}
