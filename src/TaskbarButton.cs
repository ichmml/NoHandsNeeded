using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tastatur
{
    /// <summary>
    /// Eigener Knopf in der Taskleiste, solange die Tastatur läuft. Die Tastatur selbst darf nie aktiv
    /// werden (Fokus), deshalb gehört der Knopf zu einem unsichtbaren, immer minimierten Fenster.
    /// Ein Klick darauf blendet die Tastatur ein oder aus und gibt den Fokus sofort an das Programm zurück,
    /// in dem man gerade geschrieben hat.
    /// </summary>
    /// <summary>Lässt innerhalb kurzer Zeit nur ein Ereignis durch.</summary>
    internal class ClickGate
    {
        public TimeSpan Window = TimeSpan.FromMilliseconds(400);
        private DateTime _last = DateTime.MinValue;

        public bool Accept(DateTime now)
        {
            if (now - _last < Window && now >= _last) return false;
            _last = now;
            return true;
        }
    }

    internal class TaskbarButton : Form
    {
        public event Action Clicked;
        /// <summary>Fenster, das danach wieder den Fokus bekommt.</summary>
        public Func<IntPtr> ReturnFocusTo;
        private bool _reacting;

        public TaskbarButton(Icon icon)
        {
            Text = AppInfo.Name;   // Tooltip in der Taskleiste
            Icon = icon;
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            Opacity = 0;
            MinimizeBox = true;
        }

        private bool _ready;

        /// <summary>Knopf anzeigen: minimiert und ohne zu aktivieren (sonst wäre der Fokus weg).</summary>
        public void ShowButton()
        {
            var h = Handle;
            Native.ShowWindow(h, Native.SW_SHOWMINNOACTIVE);
            BeginInvoke((Action)(() => _ready = true));
        }

        public void HideButton()
        {
            _ready = false;
            if (IsHandleCreated) Native.ShowWindow(Handle, 0);   // SW_HIDE
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000 | 0x00080000;           // WS_MINIMIZEBOX | WS_SYSMENU: Taskleisten-Klick minimiert/stellt her
                cp.ExStyle |= 0x00040000;                      // WS_EX_APPWINDOW: immer in der Taskleiste
                cp.ExStyle &= ~0x00000080;                     // nicht WS_EX_TOOLWINDOW
                return cp;
            }
        }

        public void SetTitle(string title) { Text = title; }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!IsHandleCreated || _reacting || !_ready) return;
            if (!Native.IsIconic(Handle)) React();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (!_reacting && _ready) React();
        }

        private readonly ClickGate _gate = new ClickGate();

        private void React()
        {
            // Ein Taskleisten-Klick meldet sich doppelt (Wiederherstellen und Aktivieren): nur einmal auswerten
            if (!_gate.Accept(DateTime.UtcNow))
            {
                BeginInvoke((Action)(() => { if (IsHandleCreated) Native.ShowWindow(Handle, Native.SW_SHOWMINNOACTIVE); }));
                return;
            }
            _reacting = true;
            BeginInvoke((Action)(() =>
            {
                try
                {
                    Native.ShowWindow(Handle, Native.SW_SHOWMINNOACTIVE);
                    var h = Clicked; if (h != null) h();
                    var target = ReturnFocusTo != null ? ReturnFocusTo() : IntPtr.Zero;
                    if (target != IntPtr.Zero && Native.IsWindow(target)) Native.SetForegroundWindow(target);
                }
                catch (Exception ex) { Log.Write("Taskleisten-Knopf: " + ex.Message); }
                finally { BeginInvoke((Action)(() => _reacting = false)); }
            }));
        }

        /// <summary>Schließen über die Taskleiste (Rechtsklick → Fenster schließen) beendet nicht das Programm.</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Native.ShowWindow(Handle, Native.SW_SHOWMINNOACTIVE); }
            base.OnFormClosing(e);
        }
    }
}
