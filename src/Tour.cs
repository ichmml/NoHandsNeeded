using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Tastatur
{
    internal class TourStep
    {
        public string Target;       // Stelle auf der Tastatur (siehe KeyboardForm.TourTargetRect), "tray" oder null
        public string Title;
        public string Text;
        public string ExtraLabel;   // zusätzlicher Knopf, z. B. "Sprache herunterladen"
        public Action Extra;
    }

    /// <summary>Die Schritte der Einführung, in der Sprache der Oberfläche.</summary>
    internal static class TourSteps
    {
        public static List<TourStep> Create(string languageInfo, string extraLabel, Action extra)
        {
            return new List<TourStep>
            {
                new TourStep { Target = "keyboard", Title = L.T("Willkommen!"),
                    Text = L.T("Diese Bildschirmtastatur tippt in das Programm, in dem du gerade schreibst, und nimmt ihm nie den Fokus weg. Klick dich mit \"Weiter\" durch die wichtigsten Stellen.") + (languageInfo != null ? "\n\n" + languageInfo : ""),
                    ExtraLabel = extraLabel, Extra = extra },
                new TourStep { Target = "band", Title = L.T("Wortvorschläge"),
                    Text = L.T("Ganz links steht rot, was du gerade tippst. Daneben stehen passende Wörter, ein Klick übernimmt das Wort samt Leerzeichen. Die Tastatur lernt mit, welche Wörter du oft schreibst. Ein neues Wort übernimmst du mit einem Klick auf das rote Wort, dann merkt sie es sich sofort.") },
                new TourStep { Target = "case", Title = L.T("Groß/Klein-Taste"),
                    Text = L.T("\"abc ↑\" macht das letzte Wort groß, \"ABC ↓\" macht es klein. Das geht auch nachträglich, nach Leerzeichen oder Punkt.") },
                new TourStep { Target = "drag", Title = L.T("Verschieben und Größe"),
                    Text = L.T("Halte ✥ fest und zieh die Tastatur, wohin du willst. Oder bleib auf einer beliebigen Taste, bis sich der orange Ring füllt: Dann hängt die Tastatur an der Maus. Die Größe änderst du an jedem Rand.") },
                new TourStep { Target = "mods", Title = L.T("Strg, Shift, Alt, Win, AltGr"),
                    Text = L.T("Einmal tippen gilt für die nächste Taste, zweimal tippen schaltet sie wieder aus.") },
                new TourStep { Target = "macro", Title = L.T("Makros"),
                    Text = L.T("Textbausteine, Tastenfolgen und Programme mit einem Klick. Mit ● nimmst du einfach auf, was du tippst.") },
                new TourStep { Target = "lang", Title = L.T("Sprache"),
                    Text = L.T("Wechselt die Sprache der Vorschläge und das Tastenlayout. Welche Sprachen dabei sind, stellst du in den Einstellungen ein.") },
                new TourStep { Target = "vault", Title = L.T("Passwort-Tresor"),
                    Text = L.T("Zeigt deine gespeicherten Passwörter. In Passwortfeldern erscheinen sie von selbst, ein Klick tippt das Passwort ein.") },
                new TourStep { Target = "dwell", Title = L.T("Verweilklick"),
                    Text = L.T("Schaltet den Verweilklick an oder aus: Eine Taste löst aus, wenn du kurz mit der Maus darauf bleibst.") },
                new TourStep { Target = "settings", Title = L.T("Einstellungen"),
                    Text = L.T("Hier passt du alles an: Größe, Farben, Sprachen, Kürzel und mehr. Diese Einführung findest du dort auch wieder.") },
                new TourStep { Target = "collapse", Title = L.T("Einklappen"),
                    Text = L.T("Klappt die Tastatur zu einem kleinen runden Knopf zusammen. Ein Klick darauf holt sie zurück.") },
                new TourStep { Target = "tray", Title = L.T("Tray-Symbol und Kürzel"),
                    Text = L.T("Unten rechts in der Taskleiste sitzt das Symbol der Tastatur. Die Taste Pause blendet die Tastatur ein und aus, ideal für eine Taste am Quadstick.") },
                new TourStep { Target = "keyboard", Title = L.T("Fertig!"),
                    Text = L.T("Viel Spaß beim Schreiben. Alles Weitere steht in der Anleitung: \"📖 Anleitung öffnen\" ganz oben in den Einstellungen oder im Tray-Menü. Die Tastatur ist kostenlos. Wenn sie dir hilft, freue ich mich über eine Spende.") },
            };
        }
    }

    /// <summary>
    /// Sprechblase der Einführung. Fenster ohne Fokus, Knöpfe gehen auch mit Verweilklick.
    /// </summary>
    internal class TourForm : NoActivateForm
    {
        private readonly List<TourStep> _steps;
        private readonly Func<string, Rectangle?> _targetRect;
        private readonly Settings _settings;
        private int _index;
        private Rectangle? _target;
        private bool _arrowUp;           // Pfeil zeigt nach oben (Blase unter der Stelle)
        private int _arrowX;
        private readonly Timer _timer = new Timer { Interval = 40 };
        private string _hover;
        private DateTime _hoverSince;
        private readonly Dictionary<string, Rectangle> _buttons = new Dictionary<string, Rectangle>();

        public event Action<bool> Finished;   // true = bis zum Ende durchgeklickt
        public int Index { get { return _index; } }

        private const int Arrow = 18;

        public TourForm(List<TourStep> steps, Func<string, Rectangle?> targetRect, Settings settings)
        {
            _steps = steps; _targetRect = targetRect; _settings = settings;
            BackColor = Color.FromArgb(255, 252, 240);
            _timer.Tick += (o, e) => OnTick();
        }

        public void Start()
        {
            _index = 0;
            // erst das Fenster anlegen, damit Größe und Form mit der richtigen Skalierung berechnet werden
            CreateControl();
            var unused = Handle;
            ShowStep();
            Show();
            ShowStep();
            _timer.Start();
        }

        private float Scale1 { get { return (IsHandleCreated ? DpiInfo.ForWindow(Handle) : DpiInfo.SystemDpi) / 96f; } }

        private Font TitleFont { get { return new Font("Segoe UI", 17f * Scale1, FontStyle.Bold, GraphicsUnit.Pixel); } }
        private Font TextFont { get { return new Font("Segoe UI", 15.5f * Scale1, FontStyle.Regular, GraphicsUnit.Pixel); } }
        private Font ButtonFont { get { return new Font("Segoe UI", 16f * Scale1, FontStyle.Bold, GraphicsUnit.Pixel); } }

        private void ShowStep()
        {
            var st = _steps[_index];
            var kb = _targetRect("keyboard") ?? Screen.PrimaryScreen.WorkingArea;
            if (st.Target == null || st.Target == "keyboard" || st.Target == "tray") _targetRect("none");   // kein Rahmen
            _target = st.Target == "tray" ? TrayRect() : st.Target == null || st.Target == "keyboard" ? null : _targetRect(st.Target);

            int w = (int)(560 * Scale1);
            int pad = (int)(18 * Scale1);
            int h;
            using (var g = CreateGraphics())
            using (var tf = TitleFont)
            using (var xf = TextFont)
            {
                var ts = g.MeasureString(st.Title, tf, w - 2 * pad);
                var xs = g.MeasureString(st.Text, xf, w - 2 * pad);
                h = pad + (int)ts.Height + (int)(8 * Scale1) + (int)xs.Height + pad + (int)(52 * Scale1) + pad
                    + (st.ExtraLabel != null ? (int)(60 * Scale1) : 0);
            }
            int total = h + Arrow;
            var anchor = _target ?? kb;
            var wa = Screen.FromRectangle(kb).WorkingArea;
            int ax = anchor.Left + anchor.Width / 2;
            int x = Math.Max(wa.Left + 8, Math.Min(wa.Right - w - 8, ax - w / 2));
            int y;
            // über der Tastatur, sonst darunter, sonst oben auf dem Bildschirm
            var avoid = st.Target == "tray" ? anchor : kb;
            if (avoid.Top - total - 4 >= wa.Top) { y = avoid.Top - total - 4; _arrowUp = false; }
            else if (avoid.Bottom + total + 4 <= wa.Bottom) { y = avoid.Bottom + 4; _arrowUp = true; }
            else { y = wa.Top + 8; _arrowUp = false; }
            _arrowX = Math.Max(Arrow * 2, Math.Min(w - Arrow * 2, ax - x));
            Bounds = new Rectangle(x, y, w, total);
            ApplyShape(w, total);
            Invalidate();
        }

        /// <summary>Fensterform: abgerundete Blase plus Pfeil, der Rest ist durchsichtig.</summary>
        private void ApplyShape(int w, int h)
        {
            var path = new GraphicsPath();
            var box = new Rectangle(0, _arrowUp ? Arrow : 0, w, h - Arrow);
            int r = (int)(14 * Scale1);
            path.AddArc(box.Left, box.Top, r, r, 180, 90);
            path.AddArc(box.Right - r, box.Top, r, r, 270, 90);
            path.AddArc(box.Right - r, box.Bottom - r, r, r, 0, 90);
            path.AddArc(box.Left, box.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            if (_target != null)
            {
                if (_arrowUp) path.AddPolygon(new[] { new Point(_arrowX - Arrow, Arrow + 2), new Point(_arrowX + Arrow, Arrow + 2), new Point(_arrowX, 0) });
                else path.AddPolygon(new[] { new Point(_arrowX - Arrow, h - Arrow - 2), new Point(_arrowX + Arrow, h - Arrow - 2), new Point(_arrowX, h) });
            }
            var old = Region;
            Region = new Region(path);
            if (old != null) old.Dispose();
        }

        private static Rectangle TrayRect()
        {
            var b = Screen.PrimaryScreen.Bounds;
            var wa = Screen.PrimaryScreen.WorkingArea;
            int h = Math.Max(30, b.Bottom - wa.Bottom);
            return new Rectangle(b.Right - 260, b.Bottom - h, 200, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.FromArgb(255, 252, 240));
            var st = _steps[_index];
            int pad = (int)(18 * Scale1);
            var box = new Rectangle(0, _arrowUp ? Arrow : 0, Width - 1, Height - Arrow - 1);
            var bg = Color.FromArgb(255, 252, 240);
            var accent = Color.FromArgb(255, 170, 0);
            using (var path = new GraphicsPath())
            {
                int r = (int)(14 * Scale1);
                path.AddArc(box.Left, box.Top, r, r, 180, 90);
                path.AddArc(box.Right - r, box.Top, r, r, 270, 90);
                path.AddArc(box.Right - r, box.Bottom - r, r, r, 0, 90);
                path.AddArc(box.Left, box.Bottom - r, r, r, 90, 90);
                path.CloseFigure();
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                using (var p = new Pen(accent, 3f)) g.DrawPath(p, path);
            }
            if (_target != null)
            {
                Point[] tri = _arrowUp
                    ? new[] { new Point(_arrowX - Arrow, Arrow + 2), new Point(_arrowX + Arrow, Arrow + 2), new Point(_arrowX, 0) }
                    : new[] { new Point(_arrowX - Arrow, Height - Arrow - 3), new Point(_arrowX + Arrow, Height - Arrow - 3), new Point(_arrowX, Height - 1) };
                using (var b = new SolidBrush(accent)) g.FillPolygon(b, tri);
            }

            int y = box.Top + pad;
            int w = Width - 2 * pad;
            using (var tf = TitleFont)
            using (var xf = TextFont)
            using (var bf = ButtonFont)
            using (var dark = new SolidBrush(Color.FromArgb(30, 30, 30)))
            using (var dim = new SolidBrush(Color.FromArgb(110, 110, 110)))
            {
                string counter = (_index + 1) + " / " + _steps.Count;
                var cs = g.MeasureString(counter, xf);
                g.DrawString(counter, xf, dim, box.Right - pad - cs.Width, y);
                var ts = g.MeasureString(st.Title, tf, w - (int)cs.Width - 10);
                g.DrawString(st.Title, tf, dark, new RectangleF(box.Left + pad, y, w - cs.Width - 10, ts.Height + 4));
                y += (int)ts.Height + (int)(8 * Scale1);
                var xs = g.MeasureString(st.Text, xf, w);
                g.DrawString(st.Text, xf, dark, new RectangleF(box.Left + pad, y, w, xs.Height + 4));
                y += (int)xs.Height + pad;

                _buttons.Clear();
                int bh = (int)(52 * Scale1);
                if (st.ExtraLabel != null)
                {
                    _buttons["extra"] = new Rectangle(box.Left + pad, y, w, bh);
                    y += bh + (int)(8 * Scale1);
                }
                bool last = _index == _steps.Count - 1;
                int gap = (int)(10 * Scale1);
                int bw = (w - 2 * gap) / 3;
                if (!last) _buttons["skip"] = new Rectangle(box.Left + pad, y, bw, bh);
                if (_index > 0) _buttons["back"] = new Rectangle(box.Left + pad + bw + gap, y, bw, bh);
                _buttons["next"] = new Rectangle(box.Left + pad + 2 * (bw + gap), y, bw, bh);

                foreach (var kv in _buttons)
                {
                    string label = kv.Key == "extra" ? st.ExtraLabel : kv.Key == "skip" ? L.T("Überspringen")
                        : kv.Key == "back" ? L.T("Zurück") : last ? L.T("Fertig") : L.T("Weiter");
                    bool primary = kv.Key == "next";
                    var fill = primary ? accent : kv.Key == "extra" ? Color.FromArgb(70, 130, 220) : Color.FromArgb(230, 226, 214);
                    if (_hover == kv.Key) fill = ControlPaint.Dark(fill, 0.05f);
                    using (var b = new SolidBrush(fill)) g.FillRectangle(b, kv.Value);
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (var tb = new SolidBrush(primary || kv.Key == "extra" ? Color.White : Color.FromArgb(40, 40, 40)))
                        g.DrawString(label, bf, tb, kv.Value, sf);
                    // Verweil-Fortschritt
                    if (_settings.DwellEnabled && _hover == kv.Key)
                    {
                        double f = Math.Min(1, (DateTime.UtcNow - _hoverSince).TotalMilliseconds / Math.Max(200, _settings.DwellMs));
                        using (var pb = new SolidBrush(Color.FromArgb(40, 90, 200)))
                            g.FillRectangle(pb, kv.Value.Left, kv.Value.Bottom - 6, (int)(kv.Value.Width * f), 6);
                    }
                }
            }
        }

        private string ButtonAt(Point p)
        {
            foreach (var kv in _buttons) if (kv.Value.Contains(p)) return kv.Key;
            return null;
        }

        private void OnTick()
        {
            var p = PointToClient(Cursor.Position);
            string b = ClientRectangle.Contains(p) ? ButtonAt(p) : null;
            if (b != _hover) { _hover = b; _hoverSince = DateTime.UtcNow; Invalidate(); return; }
            if (_hover == null) return;
            if (_settings.DwellEnabled)
            {
                Invalidate();
                if ((DateTime.UtcNow - _hoverSince).TotalMilliseconds >= Math.Max(200, _settings.DwellMs))
                {
                    string h = _hover;
                    _hoverSince = DateTime.UtcNow.AddSeconds(3600);   // erst nach Verlassen wieder auslösen
                    Press(h);
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            string b = ButtonAt(e.Location);
            if (b != null) Press(b);
        }

        public void Press(string button)
        {
            switch (button)
            {
                case "next":
                    if (_index >= _steps.Count - 1) { End(true); return; }
                    _index++; break;
                case "back":
                    if (_index > 0) _index--; break;
                case "skip":
                    End(false); return;
                case "extra":
                    var x = _steps[_index].Extra;
                    if (x != null) x();
                    return;
            }
            _hover = null;
            ShowStep();
        }

        private void End(bool completed)
        {
            _timer.Stop();
            var h = Finished;
            if (h != null) h(completed);
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
