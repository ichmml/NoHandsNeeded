using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Tastatur
{
    internal static class MoreTests
    {
        static KeyDef Find(List<List<KeyDef>> rows, string normal)
        {
            return rows.SelectMany(r => r).First(k => k.Kind == KeyKind.Char && k.Normal == normal);
        }
        static KeyDef FindLabel(List<List<KeyDef>> rows, string label)
        {
            return rows.SelectMany(r => r).First(k => k.Label == label);
        }

        public static void Run(Predictor p, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- Groß/Klein (Aa) ---");
            Check(KeyboardEngine.NextCase("haus") == "Haus", "haus -> Haus");
            Check(KeyboardEngine.NextCase("Haus") == "haus", "Haus -> haus");
            Check(KeyboardEngine.NextCase("HAUS") == "haus", "HAUS -> haus");
            Check(KeyboardEngine.NextCase("iPhone") == "iphone", "gemischt -> klein");
            Check(KeyboardEngine.NextCase("a") == "A" && KeyboardEngine.NextCase("A") == "a", "einzelner Buchstabe");
            Check(KeyboardEngine.NextCase("123") == "123", "Zahl bleibt");

            var settings = new Settings();
            var o = new FakeOutput();
            var rec = new RecordingOutput(o);
            var e = new KeyboardEngine(rec, p, settings);
            var L = KeyLayouts.CreateGerman();
            var aa = L.SelectMany(r => r).First(k => k.Kind == KeyKind.CaseToggle);
            var space = L.SelectMany(r => r).First(k => k.Kind == KeyKind.Space);
            Action<string> type = str => { foreach (char c in str) { if (c == ' ') e.Press(space); else e.Press(Find(L, c.ToString())); } };

            e.ResetWord(false); o.Doc.Clear();
            type("hallo");
            Check(e.CaseDirection == 1 && e.CaseLabel == "abc", "klein geschrieben: abc mit Pfeil nach oben");
            e.Press(aa); Check(o.Doc.ToString() == "Hallo", "Pfeil hoch: " + o.Doc);
            Check(e.CaseDirection == -1 && e.CaseLabel == "ABC", "danach ABC mit Pfeil nach unten");
            e.Press(aa); Check(o.Doc.ToString() == "hallo", "Pfeil runter: " + o.Doc);
            e.Press(aa); Check(o.Doc.ToString() == "Hallo", "und wieder hoch: " + o.Doc);
            e.Press(aa);
            type("x");
            Check(o.Doc.ToString() == "hallox" && e.CurrentWord == "hallox", "weiterschreiben nach Aa");

            e.ResetWord(false); o.Doc.Clear();
            type("das haus ");
            e.Press(aa);
            Check(o.Doc.ToString() == "das Haus ", "Aa nach Leerzeichen: '" + o.Doc + "'");
            type("ist");
            Check(o.Doc.ToString() == "das Haus ist", "danach normal weiter: '" + o.Doc + "'");

            e.ResetWord(false); o.Doc.Clear();
            type("ja. ");
            e.Press(aa);
            Check(o.Doc.ToString() == "Ja. ", "Aa nach Punkt und Leerzeichen: '" + o.Doc + "'");

            e.ResetWord(false); o.Doc.Clear();
            type("stra");
            e.PressSuggestion("Straße");
            Check(e.CaseDirection == -1, "Straße: Pfeil nach unten");
            e.Press(aa);
            Check(o.Doc.ToString() == "straße ", "Aa nach Vorschlag macht klein: '" + o.Doc + "'");

            e.ResetWord(false); o.Doc.Clear();
            type("haus ");
            e.Press(FindLabel(L, "⌫"));
            Check(e.CurrentWord == "haus", "Rück nach Leerzeichen: Wort wieder aktiv");
            e.Press(aa);
            Check(o.Doc.ToString() == "Haus", "Aa danach: '" + o.Doc + "'");

            e.ResetWord(false); o.Doc.Clear();
            type("haus"); e.Press(FindLabel(L, "Enter ↵"));
            Check(!e.CanToggleCase, "nach Enter ist Aa aus");
            e.ResetWord(false);
            Check(!e.CanToggleCase && e.CaseDirection == 0 && e.CaseLabel == "Aa", "nach Klick/Fensterwechsel graues Aa");
            Check(KeyboardEngine.NextCase("HAUS") == "haus" && KeyboardEngine.NextCase("123") == "123", "ganz groß → klein, Zahl bleibt");

            Console.WriteLine("\n--- Satzzeichen ---");
            Func<string, KeyDef> key = n => L.SelectMany(r => r).First(k => k.Kind == KeyKind.Char && k.Normal == n);
            var bottom = L[L.Count - 1];
            Check(new[] { "!", "?", ":", "-" }.All(n => bottom.Any(k => k.Kind == KeyKind.Char && k.Normal == n)), "! ? : - in der untersten Reihe");
            e.ResetWord(false); o.Doc.Clear();
            type("das"); e.PressSuggestion("Haus");
            e.Press(key("."));
            Check(o.Doc.ToString() == "Haus. ", "Punkt nach Vorschlag rückt ans Wort: '" + o.Doc + "'");
            type("e");
            Check(o.Doc.ToString() == "Haus. E", "danach groß weiter: '" + o.Doc + "'");
            e.ResetWord(false); o.Doc.Clear();
            e.PressSuggestion("Hallo"); e.Press(key(",")); e.PressSuggestion("wie"); e.Press(key("?")); e.Press(key("!"));
            Check(o.Doc.ToString() == "Hallo, wie?! ", "Komma, Fragezeichen, Ausrufezeichen: '" + o.Doc + "'");
            e.ResetWord(false); o.Doc.Clear();
            type("ja ."); 
            Check(o.Doc.ToString() == "ja .", "selbst getipptes Leerzeichen bleibt: '" + o.Doc + "'");
            e.ResetWord(false); o.Doc.Clear();
            e.PressSuggestion("haus"); e.Press(key("."));
            e.Press(aa);
            Check(o.Doc.ToString() == "Haus. ", "Groß/Klein wirkt auch nach Punkt: '" + o.Doc + "'");

            Console.WriteLine("\n--- Zuletzt getippt ---");
            e.ResetWord(false); o.Doc.Clear();
            type("viele gruesse"); e.Press(FindLabel(L, "⌫")); e.Press(FindLabel(L, "Enter ↵")); type("morten");
            Check(e.LastTyped == "viele gruess\nMorten", "LastTyped: '" + e.LastTyped.Replace("\n", "⏎") + "'");

            Console.WriteLine("\n--- Aufnahme ---");
            e.ResetWord(false);
            rec.Start();
            type("hallo"); e.Press(FindLabel(L, "⌫")); type("o welt");
            e.Press(FindLabel(L, "Enter ↵"));
            e.Press(FindLabel(L, "Strg")); type("s");
            var steps = rec.Stop();
            Console.WriteLine("     " + string.Join(" | ", steps.Select(s => s.ToString())));
            Check(steps.Count == 2 && steps[0].Kind == StepKind.Text && steps[0].Value == "hallo welt\n"
                && steps[1].Kind == StepKind.Keys && steps[1].Value == "Strg+S", "Aufnahme ergibt Text + Strg+S");
            type("x");
            Check(rec.Steps.Count == 0, "nach Stopp wird nichts mehr aufgenommen");

            Console.WriteLine("\n--- Tastenkombis ---");
            Check(KeyCombo.Parse("Strg+Shift+T").ToString() == "Strg+Shift+T", "Strg+Shift+T");
            Check(KeyCombo.Parse("ctrl+alt+shift+g").ToString() == "Strg+Alt+Shift+G", "englische Namen");
            Check(KeyCombo.Parse("F13").Vk == 0x7C, "F13");
            Check(KeyCombo.Parse("F24").Vk == 0x87, "F24");
            Check(KeyCombo.Parse("Strg++").Char == '+', "Strg++");
            Check(KeyCombo.Parse("Win+D").Mods == Modifiers.Win, "Win+D");
            Check(KeyCombo.Parse("Foo") == null, "unbekannt -> null");
            string bad;
            var seq = KeyCombo.ParseSequence("Strg+A, Strg+C, Enter", out bad);
            Check(seq != null && seq.Count == 3, "Folge mit 3 Tasten");
            seq = KeyCombo.ParseSequence("Strg+,", out bad);
            Check(seq != null && seq.Count == 1 && seq[0].Char == ',', "Strg+Komma");
            seq = KeyCombo.ParseSequence("Enter, Hupe", out bad);
            Check(seq == null && bad == "Hupe", "Fehler nennt die Taste: " + bad);
            Check(KeyCombo.Parse(new KeyCombo { Vk = 0x7C, Mods = Modifiers.Ctrl }.ToString()).Vk == 0x7C, "Format und zurück");
            Check(KeyCombo.Parse("VK186") != null && KeyCombo.Parse("VK186").Vk == 186, "VK-Nummer");

            Console.WriteLine("\n--- makros.yaml ---");
            var ex = MacroStore.Examples();
            ex.Add(new Macro { Name = "Sonder: \"Zeichen\" # mit {klammern}", Group = "Test", Hotkey = "F16",
                Steps = { new MacroStep(StepKind.Text, "  eingerückt\nzweite: Zeile\n\nnach Leerzeile"),
                          new MacroStep(StepKind.Text, "endet mit Umbruch\n"),
                          new MacroStep(StepKind.Text, "Zeile 1\n  Zeile 2 eingerückt"),
                          new MacroStep(StepKind.Text, "Tab\tim Text"),
                          new MacroStep(StepKind.Wait, "250") } });
            string yaml = MacroStore.Serialize(ex);
            List<Macro> back = null;
            try { back = MacroStore.Parse(yaml); } catch (Exception x) { Console.WriteLine("     " + x.Message); }
            Check(back != null && back.Count == ex.Count, "Beispiele schreiben und wieder lesen");
            if (back != null)
            {
                bool same = true;
                for (int i = 0; i < ex.Count; i++)
                {
                    var a = ex[i]; var b = back[i];
                    if (a.Name != b.Name || a.Group != b.Group || a.Program != b.Program || a.Color != b.Color || a.Hotkey != b.Hotkey || a.Steps.Count != b.Steps.Count)
                    { same = false; Console.WriteLine("     anders: " + a.Name); continue; }
                    for (int j = 0; j < a.Steps.Count; j++)
                        if (a.Steps[j].Kind != b.Steps[j].Kind || a.Steps[j].Value != b.Steps[j].Value)
                        { same = false; Console.WriteLine("     Schritt anders: " + a.Name + " #" + j + " '" + b.Steps[j].Value.Replace("\n", "⏎") + "'"); }
                }
                Check(same, "alle Felder und Schritte identisch");
            }

            string hand = @"# Kommentar
- name: Gruß
  gruppe: Privat
  text: |
    Viele Grüße

    Morten
- name:   Mit Schritten
  farbe: grün
  schritte:
  - oeffnen: notepad.exe
  - warten: 1000
  - text: ""Hallo\n{cursor}""
  - tasten: Strg+S

-
  name: Leere erste Zeile
  tasten: Win+D
";
            List<Macro> hm = null;
            try { hm = MacroStore.Parse(hand); } catch (Exception x) { Console.WriteLine("     " + x.Message); }
            Check(hm != null && hm.Count == 3, "handgeschriebene Datei");
            if (hm != null && hm.Count == 3)
            {
                Check(hm[0].Steps[0].Value == "Viele Grüße\n\nMorten\n", "Blocktext mit | und Leerzeile");
                Check(hm[1].Steps.Count == 4 && hm[1].Steps[2].Value == "Hallo\n{cursor}" && hm[1].Color == "grün", "Schritte auf gleicher Einrückung");
                Check(hm[2].Name == "Leere erste Zeile" && hm[2].Steps[0].Value == "Win+D", "Eintrag mit leerem Strich");
            }

            ExpectError("- name: x\n  quatsch: 1\n", 1, "Unbekannter Eintrag", Check);
            ExpectError("- name: x\n  tasten: Strg+Hupe\n", 1, "Unbekannte Taste", Check);
            ExpectError("- name: x\n\t text: a\n", 2, "Tabulator", Check);
            ExpectError("- name: x\n  text: \"offen\n", 2, "Fehlendes", Check);
            ExpectError("- gruppe: x\n  text: a\n", 1, "ohne \"name\"", Check);
            ExpectError("- name: x\n  warten: bald\n", 1, "Millisekunden", Check);

            Console.WriteLine("\n--- Platzhalter ---");
            var d = new DateTime(2026, 11, 1, 9, 5, 0);
            int cb;
            Check(Placeholders.Expand("{datum}", d, out cb) == "01.11.2026", "{datum}");
            Check(Placeholders.Expand("{uhrzeit} Uhr", d, out cb) == "09:05 Uhr", "{uhrzeit}");
            Check(Placeholders.Expand("{Wochentag}", d, out cb) == "Sonntag", "{wochentag}: " + Placeholders.Expand("{Wochentag}", d, out cb));
            string t = Placeholders.Expand("Hallo {cursor},\nGruß", d, out cb);
            Check(t == "Hallo ,\nGruß" && cb == 6, "{cursor}: zurück " + cb);

            Console.WriteLine("\n--- Makro ausführen ---");
            var mo = new FakeOutput();
            var waits = new List<int>();
            var runner = new MacroRunner(mo, () => false, ms => { waits.Add(ms); return Task.FromResult(0); });
            var m = new Macro { Name = "Test" };
            m.Steps.Add(new MacroStep(StepKind.Keys, "Strg+A, Entf"));
            m.Steps.Add(new MacroStep(StepKind.Wait, "700"));
            m.Steps.Add(new MacroStep(StepKind.Text, "Hi {cursor}!"));
            runner.Run(m).Wait();
            Console.WriteLine("     " + string.Join(" | ", mo.Log));
            Check(mo.Log.Contains("KEY:A+Ctrl") || mo.Log.Contains("VK:41+Ctrl"), "Strg+A gesendet");
            Check(mo.Log.Contains("VK:2E+None"), "Entf gesendet");
            Check(waits.Contains(700), "Pause 700 ms");
            Check(mo.Log.Contains("T:Hi !") && mo.Log.Last() == "VK:25+None", "Text + Cursor zurück");

            int held = 3;
            var r2 = new MacroRunner(new FakeOutput(), () => held-- > 0, ms => Task.FromResult(0));
            r2.Run(new Macro { Name = "x", Steps = { new MacroStep(StepKind.Text, "a") } }).Wait();
            Check(held < 0, "wartet, bis Kürzel-Tasten losgelassen sind");

            Console.WriteLine("\n--- Kürzel-Aufnahme (Hook) ---");
            var tr = new ComboTracker();
            Func<ushort, char> map = vk => vk == 0xBF ? '#' : vk == 0xBB ? '+' : '\0';
            Check(tr.Feed(0x7D, true, map).ToString() == "F14", "F14 alleine");
            Check(tr.Feed(0x7D, false, map) == null, "Loslassen liefert nichts");
            tr.Feed(0xA2, true); tr.Feed(0xA4, true); tr.Feed(0xA0, true);
            Check(tr.Feed(0x45, true, map).ToString() == "Strg+Alt+Shift+E", "Strg+Alt+Shift+E (linke Tasten)");
            tr.Feed(0xA2, false); tr.Feed(0xA4, false); tr.Feed(0xA0, false);
            Check(tr.Current == Modifiers.None, "Sondertasten wieder losgelassen");
            tr.Feed(0x5B, true);
            Check(tr.Feed(0x44, true, map).ToString() == "Win+D", "Win+D");
            tr.Feed(0x5B, false);
            Check(tr.Feed(0xA2, true, map) == null && tr.Feed(0xA2, false, map) == null, "nur Strg drücken liefert noch nichts");
            var hash = tr.Feed(0xBF, true, map);
            Check(hash.ToString() == "#" && KeyCombo.Parse(hash.ToString()).Char == '#', "Satzzeichen-Taste als Zeichen: " + hash);
            Check(tr.Feed(0x87, true, map).ToString() == "F24", "F24");
            Func<ushort, char> numMap = vk => vk == 0x6F ? '/' : vk == 0x6A ? '*' : vk == 0x6D ? '-' : vk == 0x6B ? '+' : '\0';
            Check(tr.Feed(0x6F, true, numMap).ToString() == "NumGeteilt" && KeyCombo.Parse("NumGeteilt").Vk == 0x6F, "Num / bleibt Ziffernblock-Taste");
            Check(tr.Feed(0x6A, true, numMap).ToString() == "NumMal" && KeyCombo.Parse("NumMal").Vk == 0x6A, "Num * bleibt Ziffernblock-Taste");
            Check(tr.Feed(0x6D, true, numMap).ToString() == "NumMinus" && tr.Feed(0x6B, true, numMap).ToString() == "NumPlus", "Num - und Num +");
            Check(KeyCombo.Parse("Num/").Vk == 0x6F && KeyCombo.Parse("Num*").Vk == 0x6A, "Kurzschreibweise Num/ und Num*");
            Check(KeyCombo.FromVk(0xE7, Modifiers.None, map).ToString() == "VK231", "unbekannte Taste als VK-Nummer");

            Console.WriteLine("\n--- Kürzel-Zuordnung ---");
            var calls = new List<string>();
            Func<KeyCombo, Tuple<ushort, Modifiers>> resolve = k => Tuple.Create(k.Char == '#' ? (ushort)0xBF : k.Vk, Modifiers.None);
            var wanted = new List<HotkeyRequest>
            {
                new HotkeyRequest { Id = HotkeyIds.Show, Name = "ein/aus", Combo = "F14", Action = () => calls.Add("show") },
                new HotkeyRequest { Id = HotkeyIds.Macros, Name = "makros", Combo = "f15", Action = () => calls.Add("macros") },
                new HotkeyRequest { Id = HotkeyIds.MacroBase, Name = "m1", Combo = "Strg+Alt+Shift+M", Action = () => calls.Add("m1") },
                new HotkeyRequest { Id = HotkeyIds.MacroBase + 1, Name = "doppelt", Combo = "F14", Action = () => { } },
                new HotkeyRequest { Id = HotkeyIds.MacroBase + 2, Name = "kaputt", Combo = "Strg+Hupe", Action = () => { } },
            };
            var fails = new List<HotkeyStatus>();
            var plan = HotkeyPlan.Build(wanted, resolve, fails);
            Check(plan.Count == 3, "3 Kürzel registrierbar");
            var show = plan.First(x => x.Id == HotkeyIds.Show);
            Check(show.Vk == 0x7D && show.Mods == Native.MOD_NOREPEAT, "F14 → Taste 0x7D ohne Sondertasten");
            var macros = plan.First(x => x.Id == HotkeyIds.Macros);
            Check(macros.Vk == 0x7E && macros.Normalized == "F15", "f15 klein geschrieben erkannt");
            var m1 = plan.First(x => x.Id == HotkeyIds.MacroBase);
            Check(m1.Mods == (Native.MOD_NOREPEAT | Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT), "Strg+Alt+Shift → richtige Mods");
            foreach (var r in plan) r.Action();
            Check(string.Join(",", calls) == "show,macros,m1", "jede Nummer löst die richtige Aktion aus");
            Check(fails.Count == 2 && fails.Any(f => f.Reason == "doppelt vergeben") && fails.Any(f => f.Reason == "unbekannte Taste"), "Fehler: doppelt und unbekannt");
            Check(HotkeyPlan.Win32Reason(1409).Contains("belegt"), "Fehler 1409 verständlich");

            Console.WriteLine("\n--- Programmzuordnung ---");
            var pm = new Macro { Program = "chrome, msedge.exe" };
            Check(pm.MatchesProgram("chrome") && pm.MatchesProgram("MSEDGE") && !pm.MatchesProgram("notepad"), "Programmliste");
            Check(!new Macro().MatchesProgram("chrome"), "ohne Programm keine Zuordnung");

            Console.WriteLine("\n--- Einstellungen und Dateien ---");
            Settings.InitDataDir();
            File.WriteAllText(Path.Combine(Settings.DataDir, "einstellungen.txt"),
                "ansicht = klein\nklein_x = 5\nklein_breite = 300\nklein_kompakt = ja\nkuerzel_ansicht = F13\nfenster_breite = 999\nkaputt ohne gleich\n");
            var old = Settings.Load();
            Check(old.Width == 999, "alte Einträge (groß/klein) werden ignoriert, Rest gelesen");
            var st = new Settings { SuggestionRows = 2, HotkeyShow = "", HotkeyMacros = "Strg+Alt+Shift+M", MiniOpacityPercent = 45, X = 30, Y = 40, Width = 1200, Height = 400 };
            st.Save();
            var st2 = Settings.Load();
            Check(st2.SuggestionRows == 2 && st2.X == 30 && st2.Width == 1200, "Zeilen und Position gespeichert");
            Check(st2.HotkeyShow == "" && st2.HotkeyMacros == "Strg+Alt+Shift+M", "Kürzel gespeichert");
            Check(st2.MiniOpacityPercent == 45, "Deckkraft runder Knopf gespeichert");
            Check(!File.ReadAllText(Path.Combine(Settings.DataDir, "einstellungen.txt")).Contains("klein_"), "alte Einträge verschwinden beim Speichern");

            if (File.Exists(MacroStore.FilePath)) File.Delete(MacroStore.FilePath);
            string err;
            var loaded = MacroStore.Load(out err);
            Check(err == null && loaded.Count == MacroStore.Examples().Count && File.Exists(MacroStore.FilePath), "Beispieldatei wird angelegt");
            loaded.Add(new Macro { Name = "Neu", Steps = { new MacroStep(StepKind.Text, "x") } });
            MacroStore.Save(loaded);
            var again = MacroStore.Load(out err);
            Check(err == null && again.Count == loaded.Count && File.Exists(MacroStore.FilePath + ".bak"), "Speichern mit Sicherung");
            File.WriteAllText(MacroStore.FilePath, "- name: kaputt\n  hupe: 1\n");
            var broken = MacroStore.Load(out err);
            Check(broken == null && err != null && err.Contains("Zeile 1"), "kaputte Datei: Meldung statt Absturz: " + err);
        }

        static void ExpectError(string yaml, int line, string contains, Action<bool, string> Check)
        {
            try
            {
                MacroStore.Parse(yaml);
                Check(false, "Fehler erwartet: " + contains);
            }
            catch (MacroFileException e)
            {
                Check(e.Line == line && e.Message.Contains(contains), "Fehlermeldung: " + e.Message);
            }
        }
    }
}
