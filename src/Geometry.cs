using System;
using System.Drawing;

namespace Tastatur
{
    [Flags]
    internal enum Edge { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

    /// <summary>Rechenteil für Verschieben und Größe ändern, ohne Windows testbar.</summary>
    internal static class Geometry
    {
        /// <summary>Rechteck so verschieben, dass es ganz im Bereich liegt (bei Übergröße oben links bündig).</summary>
        /// <summary>Dialog so legen, dass er die Tastatur nicht verdeckt und nicht von ihr verdeckt wird:
        /// erst über der Tastatur, sonst daneben, sonst über der Tastatur und dafür niedriger (der Dialog scrollt).</summary>
        public static Rectangle AvoidKeyboard(Rectangle dlg, Rectangle kb, Rectangle area)
        {
            if (kb.IsEmpty || !dlg.IntersectsWith(kb) || !area.IntersectsWith(kb)) return dlg;
            int above = kb.Top - area.Top, below = area.Bottom - kb.Bottom, left = kb.Left - area.Left, right = area.Right - kb.Right;
            int cx = area.Left + (area.Width - dlg.Width) / 2;
            if (above >= dlg.Height) return Clamp(new Rectangle(cx, area.Top + (above - dlg.Height) / 2, dlg.Width, dlg.Height), area);
            if (below >= dlg.Height) return Clamp(new Rectangle(cx, kb.Bottom + (below - dlg.Height) / 2, dlg.Width, dlg.Height), area);
            if (left >= dlg.Width) return Clamp(new Rectangle(area.Left + (left - dlg.Width) / 2, area.Top + (area.Height - dlg.Height) / 2, dlg.Width, dlg.Height), area);
            if (right >= dlg.Width) return Clamp(new Rectangle(kb.Right + (right - dlg.Width) / 2, area.Top + (area.Height - dlg.Height) / 2, dlg.Width, dlg.Height), area);
            // nirgends ganz Platz: in den größten freien Streifen, Höhe bzw. Breite kürzen
            if (above >= below && above >= 200) return new Rectangle(cx, area.Top, dlg.Width, above);
            if (below >= 200) return new Rectangle(cx, kb.Bottom, dlg.Width, below);
            if (left >= right && left >= 300) return new Rectangle(area.Left, area.Top + Math.Max(0, (area.Height - dlg.Height) / 2), left, Math.Min(dlg.Height, area.Height));
            if (right >= 300) return new Rectangle(kb.Right, area.Top + Math.Max(0, (area.Height - dlg.Height) / 2), right, Math.Min(dlg.Height, area.Height));
            return dlg;
        }

        /// <summary>Platz beim Mauszeiger: waagrecht mittig, knapp darunter (gap). Ist unten zu wenig Platz, darüber.
        /// So bleibt die Stelle unter dem Mauszeiger frei sichtbar.</summary>
        public static Rectangle AtMouse(Point mouse, Size size, Rectangle area, int gap)
        {
            return AtMouse(mouse, size, area, gap, "unten-mitte");
        }

        /// <summary>Lage zur Maus: "unten-mitte", "unten-rechts", "unten-links", "oben-mitte", "oben-rechts", "oben-links".
        /// unten = Tastatur unter der Maus, rechts = Tastatur geht von der Maus nach rechts (ihre linke Ecke an der Maus).
        /// Passt die gewünschte Seite nicht, wird auf die andere gewechselt.</summary>
        public static Rectangle AtMouse(Point mouse, Size size, Rectangle area, int gap, string place)
        {
            place = place ?? "unten-mitte";
            bool wantAbove = place.StartsWith("oben");
            string h = place.EndsWith("rechts") ? "rechts" : place.EndsWith("links") ? "links" : "mitte";
            int x = h == "rechts" ? mouse.X : h == "links" ? mouse.X - size.Width : mouse.X - size.Width / 2;
            if (h == "rechts" && x + size.Width > area.Right && mouse.X - size.Width >= area.Left) x = mouse.X - size.Width;   // rechts kein Platz: nach links
            if (h == "links" && x < area.Left && mouse.X + size.Width <= area.Right) x = mouse.X;
            int below = mouse.Y + gap, above = mouse.Y - gap - size.Height;
            bool fitsBelow = below + size.Height <= area.Bottom, fitsAbove = above >= area.Top;
            int y;
            if (wantAbove ? fitsAbove : fitsBelow) y = wantAbove ? above : below;
            else if (wantAbove ? fitsBelow : fitsAbove) y = wantAbove ? below : above;
            else y = (area.Bottom - mouse.Y) >= (mouse.Y - area.Top) ? below : above;   // passt nirgends ganz: wo mehr Platz ist
            return Clamp(new Rectangle(x, y, size.Width, size.Height), area);
        }

        public static Rectangle Clamp(Rectangle r, Rectangle area)
        {
            int x = r.Width >= area.Width ? area.Left : Math.Max(area.Left, Math.Min(r.X, area.Right - r.Width));
            int y = r.Height >= area.Height ? area.Top : Math.Max(area.Top, Math.Min(r.Y, area.Bottom - r.Height));
            return new Rectangle(x, y, r.Width, r.Height);
        }

        /// <summary>Wie Clamp, aber zu große Fenster werden auf den Bereich verkleinert statt links anzuschlagen.</summary>
        public static Rectangle Fit(Rectangle r, Rectangle area, Size min)
        {
            int w = Math.Max(Math.Min(min.Width, area.Width), Math.Min(r.Width, area.Width));
            int h = Math.Max(Math.Min(min.Height, area.Height), Math.Min(r.Height, area.Height));
            return Clamp(new Rectangle(r.X, r.Y, w, h), area);
        }

        /// <summary>Größe in Prozent der Arbeitsfläche, mindestens min.</summary>
        public static Size SizeFor(Rectangle area, double widthPercent, double heightPercent, Size min)
        {
            int w = (int)Math.Round(area.Width * widthPercent / 100.0);
            int h = (int)Math.Round(area.Height * heightPercent / 100.0);
            return new Size(Math.Min(area.Width, Math.Max(min.Width, w)), Math.Min(area.Height, Math.Max(min.Height, h)));
        }

        /// <summary>Standardplatz: unten bündig, rechts, mittig oder links.</summary>
        public static Rectangle Docked(Rectangle area, Size size, string dock)
        {
            int x = dock == "links" ? area.Left : dock == "mitte" ? area.Left + (area.Width - size.Width) / 2 : area.Right - size.Width;
            return new Rectangle(x, area.Bottom - size.Height, size.Width, size.Height);
        }

        /// <summary>Index des Bereichs, mit dem das Rechteck die größte Überlappung hat, -1 wenn keiner.</summary>
        public static int BestArea(Rectangle r, Rectangle[] areas)
        {
            int best = -1; long bestA = 0;
            for (int i = 0; i < areas.Length; i++)
            {
                var x = Rectangle.Intersect(r, areas[i]);
                long a = (long)x.Width * x.Height;
                if (a > bestA) { bestA = a; best = i; }
            }
            return best;
        }

        /// <summary>Index des Bereichs, der den Punkt enthält, sonst der nächstgelegene.</summary>
        public static int AreaAt(Point p, Rectangle[] areas)
        {
            int best = 0; long bestD = long.MaxValue;
            for (int i = 0; i < areas.Length; i++)
            {
                var a = areas[i];
                if (a.Contains(p)) return i;
                long dx = p.X < a.Left ? a.Left - p.X : p.X >= a.Right ? p.X - a.Right + 1 : 0;
                long dy = p.Y < a.Top ? a.Top - p.Y : p.Y >= a.Bottom ? p.Y - a.Bottom + 1 : 0;
                if (dx * dx + dy * dy < bestD) { bestD = dx * dx + dy * dy; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Platz beim Start: gespeicherte Position auf dem Bildschirm, auf dem sie liegt, Größe in Prozent dieses Bildschirms.
        /// Ohne Position oder wenn der Bildschirm fehlt: Standardplatz auf dem Hauptbildschirm.
        /// </summary>
        public static Rectangle Restore(int x, int y, double wPct, double hPct, string dock, Rectangle[] areas, int primary, Size min)
        {
            if (x != -1 || y != -1)
            {
                // ungefähre Größe, um den Bildschirm zu finden
                var probe = new Rectangle(x, y, 200, 100);
                int i = BestArea(probe, areas);
                if (i >= 0)
                {
                    var size = SizeFor(areas[i], wPct, hPct, min);
                    return Fit(new Rectangle(x, y, size.Width, size.Height), areas[i], min);
                }
            }
            var pa = areas[primary];
            return Docked(pa, SizeFor(pa, wPct, hPct, min), dock);
        }

        /// <summary>Nach dem Absetzen auf einem (anderen) Bildschirm: Größe in Prozent davon, Mitte bleibt, ganz drauf.</summary>
        public static Rectangle Rescale(Rectangle r, Rectangle area, double wPct, double hPct, Size min)
        {
            var size = SizeFor(area, wPct, hPct, min);
            var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            return Fit(new Rectangle(c.X - size.Width / 2, c.Y - size.Height / 2, size.Width, size.Height), area, min);
        }

        /// <summary>Welcher Rand liegt unter dem Punkt? Ecken haben eine größere Zone.</summary>
        public static Edge HitEdge(Size client, Point p, int border, int corner)
        {
            if (p.X < 0 || p.Y < 0 || p.X >= client.Width || p.Y >= client.Height) return Edge.None;
            bool l = p.X < border, r = p.X >= client.Width - border;
            bool t = p.Y < border, b = p.Y >= client.Height - border;
            bool lc = p.X < corner, rc = p.X >= client.Width - corner;
            bool tc = p.Y < corner, bc = p.Y >= client.Height - corner;
            var e = Edge.None;
            if (l || ((t || b) && lc)) e |= Edge.Left;
            if (r || ((t || b) && rc)) e |= Edge.Right;
            if (t || ((l || r) && tc)) e |= Edge.Top;
            if (b || ((l || r) && bc)) e |= Edge.Bottom;
            return e;
        }

        /// <summary>Neue Fenstergröße beim Ziehen am Rand. Die gegenüberliegende Kante bleibt stehen.</summary>
        public static Rectangle Resize(Rectangle start, Edge e, int dx, int dy, Size min)
        {
            int left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
            if ((e & Edge.Left) != 0) left = Math.Min(start.Left + dx, right - min.Width);
            if ((e & Edge.Right) != 0) right = Math.Max(start.Right + dx, left + min.Width);
            if ((e & Edge.Top) != 0) top = Math.Min(start.Top + dy, bottom - min.Height);
            if ((e & Edge.Bottom) != 0) bottom = Math.Max(start.Bottom + dy, top + min.Height);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }
    }

    /// <summary>
    /// Ziehen per Verweilen: Die Tastatur folgt der Maus. Abgesetzt wird, wenn die Maus sich erst bewegt hat
    /// und dann eine Weile ruhig bleibt.
    /// </summary>
    internal class DwellDrag
    {
        private const int MoveThreshold = 20;   // so weit muss man erst ziehen
        private const int StillRadius = 4;      // so wenig Zittern gilt als ruhig

        private readonly Point _start;
        private Point _last;
        private DateTime _lastMove;
        private bool _moved;
        private readonly int _dropMs;

        public DwellDrag(Point cursor, DateTime now, int dropMs)
        {
            _start = _last = cursor;
            _lastMove = now;
            _dropMs = dropMs;
        }

        public bool Moved { get { return _moved; } }

        /// <summary>Fortschritt bis zum Absetzen (0..1), 0 solange noch nicht gezogen wurde.</summary>
        public double DropProgress(DateTime now)
        {
            if (!_moved) return 0;
            return Math.Min(1, (now - _lastMove).TotalMilliseconds / _dropMs);
        }

        /// <summary>Mausposition melden. Liefert true, wenn jetzt abgesetzt werden soll.</summary>
        public bool Update(Point cursor, DateTime now)
        {
            if (Math.Abs(cursor.X - _start.X) + Math.Abs(cursor.Y - _start.Y) > MoveThreshold) _moved = true;
            if (Math.Abs(cursor.X - _last.X) > StillRadius || Math.Abs(cursor.Y - _last.Y) > StillRadius)
            {
                _last = cursor;
                _lastMove = now;
                return false;
            }
            return _moved && (now - _lastMove).TotalMilliseconds >= _dropMs;
        }

        /// <summary>Nach so langer Zeit ohne Bewegung wird abgebrochen (versehentlich gestartet).</summary>
        public bool TimedOut(DateTime now)
        {
            return !_moved && (now - _lastMove).TotalMilliseconds > 8000;
        }
    }
}
