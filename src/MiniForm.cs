using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Tastatur
{
    /// <summary>Kleiner schwebender Knopf, wenn die Tastatur eingeklappt ist. Klick oder Verweilen klappt sie wieder auf.</summary>
    internal class MiniForm : NoActivateForm
    {
        private readonly Settings _settings;
        private readonly Timer _timer = new Timer { Interval = 40 };
        private DateTime _hoverSince;
        private bool _hovering, _armed;
        private bool _down, _moved;
        private Point _downScreen, _offset;

        public event Action ExpandRequested;

        public MiniForm(Settings settings)
        {
            _settings = settings;
            Size = new Size(64, 64);
            Location = StartLocation(settings.MiniX, settings.MiniY, Size);
            _timer.Tick += OnTick;
            ApplySettings();
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(0, 0, Width, Height);
                Region = new Region(path);
            }
        }

        /// <summary>Gespeicherter Platz, wenn der Bildschirm noch da ist, sonst unten rechts auf dem Hauptbildschirm.</summary>
        private static Point StartLocation(int x, int y, Size size)
        {
            if (x != -1 || y != -1)
            {
                var r = new Rectangle(x, y, size.Width, size.Height);
                foreach (var s in Screen.AllScreens)
                    if (s.WorkingArea.IntersectsWith(r)) return Geometry.Clamp(r, s.WorkingArea).Location;
            }
            var wa = Screen.PrimaryScreen.WorkingArea;
            return new Point(wa.Right - size.Width - 26, wa.Bottom - size.Height - 26);
        }

        public void ApplySettings()
        {
            Opacity = Math.Max(20, Math.Min(100, _settings.MiniOpacityPercent)) / 100.0;
            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { _armed = false; _hovering = false; _timer.Start(); } else _timer.Stop();
        }

        private void OnTick(object sender, EventArgs e)
        {
            bool inside = ClientRectangle.Contains(PointToClient(Cursor.Position));
            if (inside && !_hovering) { _hovering = true; _armed = true; _hoverSince = DateTime.UtcNow; }
            if (!inside) { _hovering = false; _armed = false; }
            if (_settings.DwellEnabled && _armed && !_down &&
                (DateTime.UtcNow - _hoverSince).TotalMilliseconds >= _settings.DwellMs)
            {
                _armed = false;
                Expand();
            }
            Invalidate();
        }

        private void Expand()
        {
            var h = ExpandRequested; if (h != null) h();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _down = true; _moved = false;
            _downScreen = Cursor.Position;
            _offset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_down) return;
            var p = Cursor.Position;
            if (Math.Abs(p.X - _downScreen.X) + Math.Abs(p.Y - _downScreen.Y) > 6) _moved = true;
            if (_moved)
            {
                var want = new Rectangle(p.X - _offset.X, p.Y - _offset.Y, Width, Height);
                Location = Geometry.Clamp(want, Screen.FromPoint(p).WorkingArea).Location;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_down) return;
            _down = false; Capture = false;
            if (_moved)
            {
                _settings.MiniX = Left; _settings.MiniY = Top; _settings.Save();
            }
            else Expand();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Get(_settings.Theme);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(t.Background);
            var r = new RectangleF(4, 4, Width - 8, Height - 8);
            using (var b = new SolidBrush(_hovering ? t.KeyHover : t.Key)) g.FillEllipse(b, r);
            using (var f = new Font("Segoe UI Symbol", 26f, GraphicsUnit.Pixel))
            using (var tb = new SolidBrush(t.Text))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("⌨", f, tb, r, sf);
            if (_settings.DwellEnabled && _armed)
            {
                float frac = (float)Math.Min(1, (DateTime.UtcNow - _hoverSince).TotalMilliseconds / _settings.DwellMs);
                using (var p = new Pen(t.Accent, 4)) g.DrawArc(p, r, -90, 360 * frac);
            }
        }
    }
}
