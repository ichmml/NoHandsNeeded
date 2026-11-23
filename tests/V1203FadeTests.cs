using System;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Tastatur
{
    /// <summary>1.2.02: Über Textfeldern durchsichtig.</summary>
    internal static class V1203FadeTests
    {
        public static void Run(Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.02: Durchsichtig über Textfeldern ---");

            // Bereich um den Cursor: 2 Zeilen darüber und darunter, eine Zeilenhöhe links und rechts
            var caret = new Rectangle(1000, 500, 2, 20);
            var area = AutoFade.CaretArea(caret, 2);
            Check(area.Top == 460 && area.Bottom == 560 && area.Left == 980 && area.Right == 1022, "Cursorbereich mit 2 Zeilen Rand: " + area);
            var tiny = AutoFade.CaretArea(new Rectangle(10, 10, 0, 0), 2);
            Check(tiny.Height == 12 * 5 && tiny.Width >= 2 + 24, "Strich-Cursor ohne Höhe bekommt Mindestgröße");

            // Überlappung mit der Tastatur (unten rechts auf 2560 x 1440)
            var kb = new Rectangle(1024, 990, 1536, 410);
            Check(AutoFade.CaretArea(new Rectangle(2000, 1200, 2, 18), 2).IntersectsWith(kb), "Cursor hinter der Tastatur → verdeckt");
            Check(!AutoFade.CaretArea(new Rectangle(2000, 700, 2, 18), 2).IntersectsWith(kb), "Cursor weit darüber → frei");
            Check(AutoFade.CaretArea(new Rectangle(2000, 960, 2, 18), 2).IntersectsWith(kb), "Cursor knapp über der Tastatur zählt wegen Rand als verdeckt");

            // Rechtecke aus UI Automation (links, oben, Breite, Höhe), auch Kommazahlen und Müll
            var rs = AutoFade.RectsFromDoubles(new double[] { 10.5, 20.25, 7.5, 16, 100, 200, 0, 0, double.NaN, 1, 2, 3, 30, 40, 5 });
            Check(rs.Count == 1 && rs[0] == Rectangle.FromLTRB(10, 20, 18, 37), "UIA-Rechtecke umgerechnet, leere/kaputte verworfen");
            Check(AutoFade.RectsFromDoubles(null).Count == 0, "kein Array → keine Rechtecke");
            Check(!AutoFade.Plausible(new Rectangle(0, 0, 0, 10)) && !AutoFade.Plausible(new Rectangle(0, 0, 50000, 10)) && AutoFade.Plausible(new Rectangle(-1920, 0, 100, 20)),
                "unplausible Rechtecke aussortiert, linker Zweitbildschirm (negative X) erlaubt");

            // Cursor aus Zeichenrechteck
            var ch = new Rectangle(300, 100, 9, 18);
            Check(AutoFade.CaretFromChar(ch, false).X == 300 && AutoFade.CaretFromChar(ch, true).X == 308 && AutoFade.CaretFromChar(ch, true).Height == 18,
                "Cursor vor dem Zeichen bzw. am Textende dahinter");

            // DPI: Per-Monitor v2, Tastatur und UIA liefern beide echte Pixel. Zweiter Bildschirm rechts mit 100 %:
            var kb2 = new Rectangle(2560 + 400, 700, 1100, 380);
            Check(AutoFade.CaretArea(new Rectangle(2560 + 900, 900, 2, 16), 2).IntersectsWith(kb2), "zweiter Bildschirm: gleiche Pixel-Rechnung");
            Check(!AutoFade.CaretArea(new Rectangle(2000, 900, 2, 16), 2).IntersectsWith(kb2), "Cursor auf dem anderen Bildschirm → frei");

            // Ausnahmen
            var ex = AutoFade.ParseExclusions(" WINWORD.exe, excel;notepad  ");
            Check(ex.Count == 3 && ex.Contains("winword") && ex.Contains("Excel") && ex.Contains("notepad"), "Ausnahmen: .exe weg, Groß/Klein egal, Trenner , ; Leerzeichen");
            Check(AutoFade.NormalizeExclusions("Chrome.EXE;; chrome") == "chrome", "Ausnahmen bereinigt, doppelte weg");

            // Zieldeckkraft
            Check(AutoFade.TargetPercent(true, true, false, 95, 40, 75) == 40, "verdeckt → 40 %");
            Check(AutoFade.TargetPercent(true, true, true, 95, 40, 75) == 75, "verdeckt, Maus auf Tastatur → 75 %");
            Check(AutoFade.TargetPercent(true, false, false, 95, 40, 75) == 95, "frei → normale Deckkraft");
            Check(AutoFade.TargetPercent(false, true, false, 95, 40, 75) == 95, "abgeschaltet → normale Deckkraft");
            Check(AutoFade.TargetPercent(true, true, true, 60, 40, 100) == 60, "nie mehr als die normale Deckkraft");

            // weicher Übergang: 95 → 40 in 150 ms
            double v = 95; int steps = 0;
            while (v != 40 && steps < 100) { v = AutoFade.Approach(v, 40, 30, 55); steps++; }
            Check(steps == 5, "Übergang 95 → 40 % dauert 5 Ticks à 30 ms (" + steps + ")");
            Check(AutoFade.Approach(80, 95, 1000, 55) == 95, "nach langer Pause sofort am Ziel");

            // Verzögerung gegen Flackern
            var t0 = new DateTime(2026, 11, 22, 18, 0, 0, DateTimeKind.Utc);
            var fs = new FadeState();
            Check(!fs.Update(true, true, false, t0), "Überlappung: nicht sofort");
            Check(!fs.Update(true, true, false, t0.AddMilliseconds(200)), "nach 200 ms noch nicht");
            Check(fs.Update(true, true, false, t0.AddMilliseconds(310)), "nach 300 ms durchsichtig");
            Check(fs.Update(false, true, false, t0.AddMilliseconds(400)), "kurz frei: bleibt durchsichtig");
            Check(fs.Update(true, true, false, t0.AddMilliseconds(500)), "wieder verdeckt: kein Flackern");
            Check(fs.Update(false, true, false, t0.AddMilliseconds(600)) && !fs.Update(false, true, false, t0.AddMilliseconds(950)), "300 ms frei → wieder normal");

            // beim Tippen stabil: Maus auf der Tastatur, Cursor wandert aus dem verdeckten Bereich
            var fs2 = new FadeState();
            fs2.Update(true, true, true, t0); fs2.Update(true, true, true, t0.AddMilliseconds(350));
            Check(fs2.Covered, "verdeckt mit Maus auf der Tastatur");
            Check(fs2.Update(false, true, true, t0.AddMilliseconds(400)) && fs2.Update(false, true, true, t0.AddMilliseconds(2000)), "Maus auf Tastatur, Feld noch da: bleibt stabil");
            fs2.Update(false, true, false, t0.AddMilliseconds(2100));
            Check(!fs2.Update(false, true, false, t0.AddMilliseconds(2450)), "Maus weg und frei → normal");
            var fs3 = new FadeState();
            fs3.Update(true, true, true, t0); fs3.Update(true, true, true, t0.AddMilliseconds(350));
            fs3.Update(false, false, true, t0.AddMilliseconds(400));
            Check(!fs3.Update(false, false, true, t0.AddMilliseconds(750)), "Textfeld weg (z. B. Mail gesendet) → normal, auch mit Maus auf Tastatur");

            // Einstellungen
            string tmp = Path.Combine(Path.GetTempPath(), "nhn1202_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            string f = Path.Combine(tmp, "einst.txt");
            File.WriteAllText(f, "einstellungen_version = 6\ndeckkraft_prozent = 90\n");
            var old = Settings.LoadFrom(f);
            Check(old.AutoFadeOn && old.AutoFadeCaret && old.AutoFadePercent == 40 && old.AutoFadeHoverPercent == 75 && old.AutoFadeExclude == "" && !old.AutoFadeLog && old.OpacityPercent == 90,
                "alte Einstellungen: Standardwerte ergänzt, Rest bleibt");
            old.AutoFadeOn = false; old.AutoFadeCaret = false; old.AutoFadePercent = 25; old.AutoFadeHoverPercent = 100; old.AutoFadeExclude = "winword, excel"; old.AutoFadeLog = true;
            old.SaveTo(f);
            var back = Settings.LoadFrom(f);
            Check(!back.AutoFadeOn && !back.AutoFadeCaret && back.AutoFadePercent == 25 && back.AutoFadeHoverPercent == 100 && back.AutoFadeExclude == "winword, excel" && back.AutoFadeLog,
                "Einstellungen gespeichert und wieder geladen");
            File.WriteAllText(f, "durchsichtig_deckkraft_prozent = 3\ndurchsichtig_deckkraft_maus_prozent = 500\ndurchsichtig_modus = quatsch\n");
            var bad = Settings.LoadFrom(f);
            Check(bad.AutoFadePercent == 10 && bad.AutoFadeHoverPercent == 100 && bad.AutoFadeCaret, "unsinnige Werte begrenzt");
            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
