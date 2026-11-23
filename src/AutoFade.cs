using System;
using System.Collections.Generic;
using System.Drawing;

namespace Tastatur
{
    /// <summary>
    /// Rechenteil für "Über Textfeldern durchsichtig": ab wann gilt das Feld als verdeckt,
    /// welche Deckkraft gilt gerade, wie weich wird übergeblendet. Ohne Windows testbar.
    /// </summary>
    internal static class AutoFade
    {
        public const int DelayMs = 300;        // so lange muss sich etwas ändern, bevor umgeschaltet wird
        public const int FadeMs = 150;         // Dauer des Übergangs
        public const int StaleMs = 1500;       // ältere Erkennung gilt als "nichts erkannt"

        /// <summary>
        /// Bereich um die Schreibmarke: so viele Zeilen darüber und darunter, links und rechts eine Zeilenhöhe.
        /// Sehr schmale oder flache Rechtecke (Strich-Cursor) bekommen eine Mindestgröße.
        /// </summary>
        public static Rectangle CaretArea(Rectangle caret, int lines)
        {
            int h = Math.Max(caret.Height, 12);
            int w = Math.Max(caret.Width, 2);
            var r = new Rectangle(caret.X, caret.Y, w, h);
            r.Inflate(h, h * Math.Max(0, lines));
            return r;
        }

        /// <summary>UI Automation liefert Rechtecke als links, oben, Breite, Höhe hintereinander.</summary>
        public static List<Rectangle> RectsFromDoubles(double[] d)
        {
            var list = new List<Rectangle>();
            if (d == null) return list;
            for (int i = 0; i + 3 < d.Length; i += 4)
            {
                if (double.IsNaN(d[i]) || double.IsInfinity(d[i]) || double.IsInfinity(d[i + 2])) continue;
                int x = (int)Math.Floor(d[i]), y = (int)Math.Floor(d[i + 1]);
                int r = (int)Math.Ceiling(d[i] + d[i + 2]), b = (int)Math.Ceiling(d[i + 1] + d[i + 3]);
                if (r - x <= 0 && b - y <= 0) continue;
                list.Add(Rectangle.FromLTRB(x, y, Math.Max(r, x + 1), Math.Max(b, y + 1)));
            }
            return list;
        }

        /// <summary>
        /// Schreibmarke aus dem Rechteck eines Zeichens: vor dem Zeichen (linke Kante)
        /// oder, wenn es das Zeichen vor dem Cursor ist, dahinter (rechte Kante).
        /// </summary>
        public static Rectangle CaretFromChar(Rectangle ch, bool afterChar)
        {
            int x = afterChar ? ch.Right - 1 : ch.Left;
            return new Rectangle(x, ch.Top, 2, Math.Max(1, ch.Height));
        }

        /// <summary>Taugt ein Rechteck als Bildschirmbereich? (UIA meldet bei unsichtbaren Feldern 0 oder Riesenwerte)</summary>
        public static bool Plausible(Rectangle r)
        {
            return r.Width > 0 && r.Height > 0 && r.Width < 20000 && r.Height < 20000;
        }

        /// <summary>Programmliste "winword, excel.exe; notepad" → Namen ohne .exe, klein.</summary>
        public static HashSet<string> ParseExclusions(string list)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (list ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.Trim();
                if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) p = p.Substring(0, p.Length - 4);
                if (p.Length > 0) set.Add(p.ToLowerInvariant());
            }
            return set;
        }

        public static string NormalizeExclusions(string list)
        {
            return string.Join(", ", ParseExclusions(list));
        }

        /// <summary>Gewünschte Deckkraft in Prozent. Nie mehr als die normale Deckkraft.</summary>
        public static int TargetPercent(bool enabled, bool covered, bool mouseOver, int normal, int faded, int hover)
        {
            if (!enabled || !covered) return normal;
            return Math.Min(normal, mouseOver ? hover : faded);
        }

        /// <summary>Ein Schritt des weichen Übergangs: höchstens so weit, dass ein ganzer Wechsel FadeMs dauert.</summary>
        public static double Approach(double current, double target, double elapsedMs, double span)
        {
            if (span <= 0 || elapsedMs <= 0) return target;
            double max = span * elapsedMs / FadeMs;
            double diff = target - current;
            if (Math.Abs(diff) <= max) return target;
            return current + Math.Sign(diff) * max;
        }
    }

    /// <summary>
    /// Verdeckt oder nicht, mit Verzögerung gegen Flackern. Solange die Maus auf der Tastatur ist
    /// und es ein Textfeld gibt, bleibt "verdeckt" stehen, damit beim Tippen nichts hin und her springt.
    /// </summary>
    internal class FadeState
    {
        public bool Covered { get; private set; }
        private bool _pending;
        private DateTime _pendingSince;

        public void Reset() { Covered = false; _pending = false; }

        /// <param name="overlap">Textfeld bzw. Cursor liegt jetzt unter der Tastatur</param>
        /// <param name="fieldKnown">Es gibt gerade überhaupt ein erkanntes Textfeld</param>
        /// <param name="mouseOver">Maus ist auf der Tastatur</param>
        public bool Update(bool overlap, bool fieldKnown, bool mouseOver, DateTime now)
        {
            bool want = overlap;
            if (Covered && !want && mouseOver && fieldKnown) want = true;   // beim Tippen stabil bleiben
            if (want == Covered) { _pending = false; return Covered; }
            if (!_pending) { _pending = true; _pendingSince = now; return Covered; }
            if ((now - _pendingSince).TotalMilliseconds >= AutoFade.DelayMs) { Covered = want; _pending = false; }
            return Covered;
        }
    }
}
