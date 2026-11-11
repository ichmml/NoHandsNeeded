using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Tastatur
{
    /// <summary>Liste verfügbarer Sprachpakete. Lädt nur nach Klick, prüft die Prüfsumme.</summary>
    internal class LanguageDownloadForm : Form
    {
        private readonly IAppHost _host;
        private readonly ListBox _list;
        private readonly TextBox _status;
        private readonly Button _load, _retry;
        private List<PackEntry> _entries = new List<PackEntry>();
        private bool _busy;

        public LanguageDownloadForm(IAppHost host)
        {
            DpiInfo.Attach(this);
            _host = host;
            Text = L.T("Weitere Sprachen herunterladen");
            Font = new Font("Segoe UI", 13f);
            Size = new Size(780, 680);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            MinimizeBox = MaximizeBox = false;

            // Textfelder statt Labels: die lange Adresse wird so auch ohne Leerzeichen umgebrochen
            var info = WrapBox(L.T("Quelle:\r\n") + PackDownloader.Url(host.LanguageSource, "index.txt") + L.T("\r\nEs wird nur geladen, was du hier anklickst."), 120);
            info.Dock = DockStyle.Top;
            info.ForeColor = SystemColors.GrayText;
            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, ItemHeight = 36, DrawMode = DrawMode.OwnerDrawFixed };
            _list.DrawItem += (o, e) =>
            {
                e.DrawBackground();
                if (e.Index >= 0)
                    TextRenderer.DrawText(e.Graphics, _list.Items[e.Index].ToString(), e.Font,
                        new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), e.ForeColor, TextFormatFlags.VerticalCenter);
            };
            _list.DoubleClick += (o, e) => Download();

            _status = WrapBox(L.T("Lade Übersicht …"), 110);
            _status.Dock = DockStyle.Bottom;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(8) };
            _load = new Button { Text = L.T("Herunterladen"), Width = 220, Height = 54, Enabled = false };
            _retry = new Button { Text = L.T("Erneut versuchen"), Width = 200, Height = 54, Visible = false };
            var close = new Button { Text = L.T("Schließen"), Width = 180, Height = 54 };
            _load.Click += (o, e) => Download();
            _retry.Click += (o, e) => LoadIndex();
            close.Click += (o, e) => Close();
            buttons.Controls.Add(_load); buttons.Controls.Add(_retry); buttons.Controls.Add(close);

            Controls.Add(_list);
            Controls.Add(info);
            Controls.Add(_status);
            Controls.Add(buttons);
            Shown += (o, e) => LoadIndex();
        }

        private static TextBox WrapBox(string text, int height)
        {
            return new TextBox
            {
                Text = text, Height = height, Multiline = true, ReadOnly = true, WordWrap = true, TabStop = false,
                BorderStyle = BorderStyle.None, BackColor = SystemColors.Control, ScrollBars = ScrollBars.None,
                Margin = new Padding(10)
            };
        }

        private async void LoadIndex()
        {
            if (_busy) return;
            _busy = true;
            _retry.Visible = false;
            _status.Text = L.T("Lade Übersicht …");
            try
            {
                _entries = await PackDownloader.FetchIndex(_host.LanguageSource);
                Fill();
                _status.Text = _entries.Count == 0 ? L.T("Keine Sprachpakete gefunden.") : L.T("Sprache auswählen und \"Herunterladen\" klicken.");
            }
            catch (Exception e)
            {
                Log.Write("Sprachpakete: Übersicht fehlgeschlagen: " + e.Message);
                _status.Text = PackDownloader.FriendlyError(e);
                _retry.Visible = true;
            }
            finally { _busy = false; }
        }

        private static string Short(string s)
        {
            return s.Length > 120 ? s.Substring(0, 117) + "…" : s;
        }

        private void Fill()
        {
            var installed = _host.Languages.Installed().Select(l => l.Code).ToList();
            foreach (var e in _entries) e.Installed = installed.Contains(e.Code);
            int sel = _list.SelectedIndex;
            _list.Items.Clear();
            foreach (var e in _entries) _list.Items.Add(e);
            if (_list.Items.Count > 0) _list.SelectedIndex = Math.Max(0, Math.Min(sel, _list.Items.Count - 1));
            _load.Enabled = _list.Items.Count > 0;
        }

        private async void Download()
        {
            var e = _list.SelectedItem as PackEntry;
            if (e == null || _busy) return;
            _busy = true;
            _load.Enabled = false;
            _status.Text = L.T("Lade ") + e.Name + " …";
            try
            {
                var data = await PackDownloader.FetchPack(_host.LanguageSource, e);
                LanguageInfo info;
                string err = _host.Languages.Install(data, e.Code, out info);
                if (err != null) { _status.Text = err; return; }
                _host.ActivateLanguage(info.Code);
                _status.Text = L.T(info.Name) + L.T(" ist installiert und eingeschaltet. Umschalten mit dem Sprach-Knopf oben in der Tastatur.");
                Fill();
            }
            catch (Exception ex)
            {
                _status.Text = PackDownloader.FriendlyError(ex);
            }
            finally { _busy = false; _load.Enabled = _list.Items.Count > 0; }
        }
    }
}
