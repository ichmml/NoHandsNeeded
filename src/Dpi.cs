using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Tastatur
{
    /// <summary>
    /// Skalierung pro Bildschirm (Per-Monitor-DPI v2). Damit stimmen Mauskoordinaten und Bildschirmgrenzen auch,
    /// wenn die Bildschirme unterschiedlich skaliert sind (z. B. 125 % und 100 %).
    /// </summary>
    internal static class DpiInfo
    {
        public const int WM_DPICHANGED = 0x02E0;

        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int value);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point pt, uint flags);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();

        public static string Mode = "unbekannt";

        /// <summary>Ganz am Anfang aufrufen, bevor ein Fenster entsteht.</summary>
        public static void Enable()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) { Mode = "Per-Monitor v2"; return; } }
            catch { }
            try { if (SetProcessDpiAwareness(2) == 0) { Mode = "Per-Monitor"; return; } }
            catch { }
            try { Native.SetProcessDPIAware(); Mode = "System"; }
            catch { }
            // schlägt auch fehl, wenn das Manifest es schon gesetzt hat: dann gilt das Manifest
            if (Mode == "unbekannt") Mode = "Manifest";
        }

        public static int ForPoint(Point p)
        {
            try
            {
                uint x, y;
                var mon = MonitorFromPoint(p, 2);
                if (GetDpiForMonitor(mon, 0, out x, out y) == 0) return (int)x;
            }
            catch { }
            return 96;
        }

        public static int ForWindow(IntPtr hwnd)
        {
            try { return (int)GetDpiForWindow(hwnd); } catch { return SystemDpi; }
        }

        public static int SystemDpi
        {
            get { try { return (int)GetDpiForSystem(); } catch { return 96; } }
        }

        /// <summary>Alle Bildschirme für das Protokoll.</summary>
        public static string Describe(Point mouse)
        {
            var sb = new StringBuilder();
            sb.Append(" | DPI-Modus ").Append(Mode).Append(", System ").Append(SystemDpi).Append(" dpi");
            foreach (var s in Screen.AllScreens)
            {
                var c = new Point(s.Bounds.Left + s.Bounds.Width / 2, s.Bounds.Top + s.Bounds.Height / 2);
                sb.Append(" | ").Append(s.DeviceName.Replace(@"\\.\", "")).Append(s.Primary ? " (Haupt)" : "")
                  .Append(" ").Append(s.Bounds).Append(" Arbeitsfläche ").Append(s.WorkingArea) // i18n-ok
                  .Append(" ").Append(ForPoint(c)).Append(" dpi");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Dialoge mit Schrift in Punkt werden mit der System-Skalierung gebaut. Öffnet sich ein Dialog auf einem
        /// Bildschirm mit anderer Skalierung, wird er passend verkleinert oder vergrößert.
        /// </summary>
        public static void Attach(Form f)
        {
            f.Load += (o, e) => { FitDialog(f); PlaceBesideKeyboard(f); };
        }

        /// <summary>Wo die Tastatur gerade steht (setzt das Programm). Leer = eingeklappt oder versteckt.</summary>
        public static Func<Rectangle> KeyboardBounds;

        private static void PlaceBesideKeyboard(Form f)
        {
            try
            {
                if (KeyboardBounds == null) return;
                var kb = KeyboardBounds();
                if (kb.IsEmpty) return;
                var wa = Screen.FromRectangle(f.Bounds).WorkingArea;
                var r = Geometry.AvoidKeyboard(f.Bounds, kb, wa);
                if (r != f.Bounds) { f.StartPosition = FormStartPosition.Manual; f.Bounds = r; }
            }
            catch (Exception e) { Log.Write("Dialog neben Tastatur: " + e.Message); }
        }

        private static void FitDialog(Form f)
        {
            try
            {
                int mon = ForWindow(f.Handle), sys = SystemDpi;
                if (mon <= 0 || sys <= 0 || Math.Abs(mon - sys) < 6) return;
                float k = mon / (float)sys;
                var explicitFonts = new List<KeyValuePair<Control, Font>>();
                Collect(f, explicitFonts);
                f.Font = Scaled(f.Font, k);
                foreach (var kv in explicitFonts) kv.Key.Font = Scaled(kv.Value, k);
                f.Scale(new SizeF(k, k));
                var wa = Screen.FromHandle(f.Handle).WorkingArea;
                var r = Geometry.Fit(f.Bounds, wa, f.MinimumSize);
                if (f.StartPosition == FormStartPosition.CenterScreen)
                    r = Geometry.Clamp(new Rectangle(wa.Left + (wa.Width - r.Width) / 2, wa.Top + (wa.Height - r.Height) / 2, r.Width, r.Height), wa);
                f.Bounds = r;
            }
            catch (Exception e) { Log.Write("Dialog-Skalierung: " + e.Message); }
        }

        private static void Collect(Control parent, List<KeyValuePair<Control, Font>> list)
        {
            foreach (Control c in parent.Controls)
            {
                // eigene Schrift (nicht vom Elternfenster geerbt)
                if (!ReferenceEquals(c.Font, parent.Font) && !c.Font.Equals(parent.Font)) list.Add(new KeyValuePair<Control, Font>(c, c.Font));
                Collect(c, list);
            }
        }

        private static Font Scaled(Font f, float k)
        {
            return new Font(f.FontFamily, Math.Max(6f, f.Size * k), f.Style, f.Unit);
        }
    }
}
