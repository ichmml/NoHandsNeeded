using System;
using System.Windows.Forms;

namespace Tastatur
{
    /// <summary>
    /// Fenster, das nie den Fokus bekommt: WS_EX_NOACTIVATE + MA_NOACTIVATE.
    /// Dadurch landen alle Eingaben im Programm, in dem man gerade schreibt.
    /// </summary>
    internal class NoActivateForm : Form
    {
        public NoActivateForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(Native.MA_NOACTIVATE);
                return;
            }
            if (m.Msg == Native.WM_ENABLE && m.WParam == IntPtr.Zero && StayEnabled)
            {
                // Ein eigener Dialog (ShowDialog) sperrt alle Fenster im Thread. Die Tastatur muss aber
                // klickbar bleiben, sonst kann man im Dialog nichts tippen. Also gleich wieder freigeben.
                base.WndProc(ref m);
                var h = Handle;
                try { BeginInvoke((Action)(() => { if (IsHandleCreated) Native.EnableWindow(h, true); })); } catch { }
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>Auch während eigener Dialoge klickbar bleiben.</summary>
        public bool StayEnabled = true;

        /// <summary>Wieder ganz nach oben, ohne zu aktivieren (manche Programme drängeln sich davor).</summary>
        public void ReassertTopmost()
        {
            if (!IsHandleCreated || !Visible) return;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }
}
