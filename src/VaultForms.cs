using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tastatur
{
    /// <summary>PIN-Eingabe mit großen Zifferntasten, ohne dass man tippen muss.</summary>
    internal class PinDialog : Form
    {
        public string Pin { get { return _pin; } }
        private string _pin = "";
        private readonly Label _display, _hint;

        public PinDialog(string title, string hint)
        {
            DpiInfo.Attach(this);
            Text = title;
            Font = new Font("Segoe UI", 16f);
            Size = new Size(420, 600);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            MinimizeBox = MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            KeyPreview = true;

            _hint = new Label { Text = hint, Dock = DockStyle.Top, Height = 60, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12f) };
            _display = new Label { Dock = DockStyle.Top, Height = 60, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 26f, FontStyle.Bold), Text = "" };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5, Padding = new Padding(10) };
            for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            for (int i = 0; i < 5; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "⌫", "0", "OK" };
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                var b = new Button { Text = k, Dock = DockStyle.Fill, Margin = new Padding(4) };
                if (k == "⌫") b.Font = new Font("Segoe UI Symbol", 18f);
                b.Click += (o, e) => Press(k);
                grid.Controls.Add(b, i % 3, i / 3);
            }
            var cancel = new Button { Text = L.T("Abbrechen"), Dock = DockStyle.Fill, Margin = new Padding(4) };
            cancel.Click += (o, e) => { DialogResult = DialogResult.Cancel; Close(); };
            grid.Controls.Add(cancel, 0, 4);
            grid.SetColumnSpan(cancel, 3);

            Controls.Add(grid);
            Controls.Add(_display);
            Controls.Add(_hint);
        }

        private void Press(string k)
        {
            if (k == "⌫") { if (_pin.Length > 0) _pin = _pin.Substring(0, _pin.Length - 1); }
            else if (k == "OK") { if (_pin.Length >= 4) { DialogResult = DialogResult.OK; Close(); return; } _hint.Text = L.T("Mindestens 4 Ziffern"); }
            else if (_pin.Length < 12) _pin += k;
            _display.Text = new string('●', _pin.Length);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var k = keyData & Keys.KeyCode;
            if (k >= Keys.D0 && k <= Keys.D9) { Press(((char)('0' + (k - Keys.D0))).ToString()); return true; }
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) { Press(((char)('0' + (k - Keys.NumPad0))).ToString()); return true; }
            if (k == Keys.Back) { Press("⌫"); return true; }
            if (k == Keys.Enter) { Press("OK"); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Neue PIN zweimal abfragen. null = abgebrochen oder ungleich.</summary>
        public static string AskNew(IWin32Window owner)
        {
            string first;
            using (var d = new PinDialog(L.T("PIN festlegen"), L.T("Neue PIN eingeben (4 bis 12 Ziffern)")))
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                first = d.Pin;
            }
            using (var d = new PinDialog(L.T("PIN festlegen"), L.T("PIN zur Sicherheit wiederholen")))
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                if (d.Pin != first) { MessageBox.Show(owner, L.T("Die beiden PINs sind verschieden. Bitte nochmal."), "PIN"); return null; }
            }
            return first;
        }
    }

    /// <summary>Passwortfeld in einem eigenen Fenster (Tresor, Sicherung): (im Feld?, Klartext sichtbar?).
    /// Solange lernt die Tastatur nichts und zeigt oben die Buchstaben bzw. Punkte.</summary>
    internal static class SecretField
    {
        public static event Action<bool, bool> Focus;
        /// <summary>Text im Feld hat sich geändert (die Tastatur zeigt ihn oben an).</summary>
        public static event Action TextChanged;

        /// <summary>Das eigene Passwortfeld, das gerade den Fokus hat (sonst null). Die Tastatur zeigt oben genau dessen Inhalt.</summary>
        public static TextBox Current { get; private set; }

        public static void Raise(bool focused, bool plain) { Raise(null, focused, plain); }

        public static void Raise(TextBox tb, bool focused, bool plain)
        {
            if (focused) Current = tb;
            else if (tb == null || Current == tb) Current = null;
            var h = Focus;
            if (h != null) h(focused, plain);
        }

        /// <summary>Inhalt für die Anzeige oben: Klartext, wenn das Feld Klartext zeigt, sonst Punkte.</summary>
        public static string DisplayText()
        {
            var tb = Current;
            if (tb == null || tb.IsDisposed) return null;
            return tb.UseSystemPasswordChar ? new string('•', tb.TextLength) : tb.Text;
        }

        internal static void Watch(TextBox tb)
        {
            tb.TextChanged += (o, e) => { if (Current == tb) { var h = TextChanged; if (h != null) h(); } };
        }

        /// <summary>Textfeld als Passwortfeld anmelden. Liefert eine Aktion, die nach "Anzeigen / verbergen" aufgerufen wird.</summary>
        public static Action Attach(TextBox tb)
        {
            bool active = false;
            Action<bool> r = f =>
            {
                if (!f && !active) return;
                active = f;
                Raise(tb, f, f && !tb.UseSystemPasswordChar);
            };
            Watch(tb);
            tb.Enter += (o, e) => r(true);
            tb.Leave += (o, e) => r(false);
            tb.HandleDestroyed += (o, e) => r(false);
            return () => { if (tb.Focused) r(true); };
        }
    }

    /// <summary>Einen Tresor-Eintrag anlegen oder ändern.</summary>
    internal class VaultEntryDialog : Form
    {
        public VaultEntry Result { get; private set; }
        private readonly TextBox _name, _user, _pw, _match;
        private readonly Settings _s;

        /// <summary>Fokus im Passwortfeld dieses Fensters: (im Feld?, Klartext sichtbar?). Die Tastatur lernt dann nichts.</summary>
        private bool _secretActive;

        private void RaiseSecret(bool focused)
        {
            if (!focused && !_secretActive) return;
            _secretActive = focused;
            SecretField.Raise(_pw, focused, focused && !_pw.UseSystemPasswordChar);
        }

        public VaultEntryDialog(VaultEntry entry, Settings settings, bool hidePassword = false)
        {
            DpiInfo.Attach(this);
            _s = settings;
            var e = entry.Clone();
            Text = L.T("Passwort-Eintrag");
            Font = new Font("Segoe UI", 13f);
            Size = new Size(760, 600);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            MinimizeBox = MaximizeBox = false;

            var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14) };
            Controls.Add(root);
            root.Controls.Add(Ui.Lbl(L.T("Name (so steht es in der Tresorleiste)")));
            _name = new TextBox { Text = e.Name, Width = 680 };
            root.Controls.Add(_name);
            root.Controls.Add(Ui.Lbl(L.T("Benutzername oder E-Mail (nur zur Erinnerung, optional)")));
            _user = new TextBox { Text = e.User, Width = 680, AccessibleName = L.T("Benutzername oder E-Mail") };
            root.Controls.Add(_user);
            root.Controls.Add(Ui.Lbl(L.T("Passwort")));
            // Immer im Klartext, auch beim Bearbeiten, damit man sieht, ob man richtig tippt. Verbergen auf Knopfdruck.
            _pw = new TextBox { Text = e.Password, Width = 680, UseSystemPasswordChar = hidePassword };
            SecretField.Watch(_pw);
            _pw.Enter += (o, ev) => RaiseSecret(true);
            _pw.Leave += (o, ev) => RaiseSecret(false);
            FormClosed += (o, ev) => RaiseSecret(false);
            root.Controls.Add(_pw);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            row.Controls.Add(Ui.Btn(L.T("Anzeigen / verbergen"), () => { _pw.UseSystemPasswordChar = !_pw.UseSystemPasswordChar; if (_pw.Focused) RaiseSecret(true); }, 220));
            row.Controls.Add(Ui.Btn(L.T("Sicheres Passwort erzeugen"), () => { _pw.Text = Vault.Generate(_s.GeneratorLength, _s.GeneratorSymbols); _pw.UseSystemPasswordChar = false; if (_pw.Focused) RaiseSecret(true); }, 280));
            root.Controls.Add(row);
            root.Controls.Add(Ui.Lbl(L.T("Passend zu (Stichwörter aus dem Fenstertitel, z. B. \"gmail, google\")")));
            _match = new TextBox { Text = e.Match, Width = 680 };
            root.Controls.Add(_match);
            // Cursor gleich ins erste leere Feld (Passwort schon von der Seite übernommen: weiter beim Benutzernamen)
            Shown += (o, ev) =>
            {
                if (_name.Text.Length == 0) _name.Focus();
                else if (_user.Text.Length == 0) _user.Focus();
                else if (_pw.Text.Length == 0) _pw.Focus();
            };

            var bottom = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 14, 0, 0) };
            bottom.Controls.Add(Ui.Btn(L.T("Speichern"), () =>
            {
                if (_name.Text.Trim().Length == 0 || _pw.Text.Length == 0) { MessageBox.Show(this, L.T("Name und Passwort dürfen nicht leer sein.")); return; }
                if (e.Password != _pw.Text) e.Changed = DateTime.Now;
                e.Name = _name.Text.Trim(); e.User = _user.Text.Trim(); e.Password = _pw.Text; e.Match = _match.Text.Trim();
                Result = e; DialogResult = DialogResult.OK; Close();
            }, 200));
            bottom.Controls.Add(Ui.Btn(L.T("Abbrechen"), () => { DialogResult = DialogResult.Cancel; Close(); }, 200));
            root.Controls.Add(bottom);
        }
    }

    /// <summary>Liste aller gespeicherten Passwörter. Passwörter werden nie im Klartext gezeigt.</summary>
    internal class VaultEditorForm : Form
    {
        private readonly Vault _vault;
        private readonly Settings _s;
        private readonly ListBox _list;
        public event Action Changed;

        public VaultEditorForm(Vault vault, Settings settings)
        {
            DpiInfo.Attach(this);
            _vault = vault; _s = settings;
            Text = L.T("Passwort-Tresor");
            Font = new Font("Segoe UI", 13f);
            Size = new Size(900, 620);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;

            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, ItemHeight = 40, DrawMode = DrawMode.OwnerDrawFixed };
            _list.DrawItem += (o, e) =>
            {
                e.DrawBackground();
                if (e.Index >= 0 && e.Index < _vault.Entries.Count)
                {
                    var v = _vault.Entries[e.Index];
                    string t = v.Name + (v.User.Length > 0 ? "   (" + v.User + ")" : "") + "   ••••••••";
                    TextRenderer.DrawText(e.Graphics, t, e.Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                e.DrawFocusRectangle();
            };
            _list.DoubleClick += (o, e) => Edit();
            var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 230, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(6) };
            side.Controls.Add(Ui.Btn(L.T("Neu"), New));
            side.Controls.Add(Ui.Btn(L.T("Bearbeiten"), Edit));
            side.Controls.Add(Ui.Btn(L.T("Löschen"), Delete));
            side.Controls.Add(Ui.Btn(L.T("Schließen"), Close));
            Controls.Add(_list);
            Controls.Add(side);
            Fill(0);
        }

        private void Fill(int sel)
        {
            _list.Items.Clear();
            foreach (var v in _vault.Entries) _list.Items.Add(v.Name);
            if (_list.Items.Count > 0) _list.SelectedIndex = Math.Max(0, Math.Min(sel, _list.Items.Count - 1));
        }

        private void SaveAll(int sel)
        {
            try { _vault.Save(); }
            catch (Exception e) { MessageBox.Show(this, L.T("Tresor konnte nicht gespeichert werden: ") + e.Message); }
            Fill(sel);
            var h = Changed; if (h != null) h();
        }

        private void New()
        {
            using (var d = new VaultEntryDialog(new VaultEntry(), _s))
                if (d.ShowDialog(this) == DialogResult.OK) { _vault.Entries.Add(d.Result); SaveAll(_vault.Entries.Count - 1); }
        }

        private void Edit()
        {
            int i = _list.SelectedIndex;
            if (i < 0) return;
            using (var d = new VaultEntryDialog(_vault.Entries[i], _s))
                if (d.ShowDialog(this) == DialogResult.OK) { _vault.Entries[i] = d.Result; SaveAll(i); }
        }

        private void Delete()
        {
            int i = _list.SelectedIndex;
            if (i < 0) return;
            if (MessageBox.Show(this, "\"" + _vault.Entries[i].Name + L.T("\" aus dem Tresor löschen?"), L.T("Löschen"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            _vault.Entries.RemoveAt(i);
            SaveAll(i);
        }
    }

    /// <summary>Gelernte Wörter ansehen, suchen und löschen.</summary>
    internal class LearnedWordsForm : Form
    {
        private readonly LanguageSet _langs;
        private readonly ComboBox _lang;
        private readonly TextBox _search;
        private readonly ListBox _list;
        private readonly Label _info;
        private List<LearnedWord> _shown = new List<LearnedWord>();
        public event Action Changed;

        public LearnedWordsForm(LanguageSet langs)
        {
            DpiInfo.Attach(this);
            _langs = langs;
            Text = L.T("Gelernte Wörter");
            Font = new Font("Segoe UI", 13f);
            Size = new Size(900, 680);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(8), WrapContents = false };
            _lang = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var p in langs.Active) _lang.Items.Add(L.T(p.Name) + " (" + p.Code.ToUpperInvariant() + ")");
            _lang.Items.Add(L.T("Zahlen"));
            _lang.Items.Add(L.T("E-Mail-Adressen"));
            _search = new TextBox { Width = 380 };
            top.Controls.Add(_lang);
            top.Controls.Add(new Label { Text = L.T("Suchen:"), AutoSize = true, Margin = new Padding(14, 8, 4, 0) });
            top.Controls.Add(_search);
            _info = new Label { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8, 8, 0, 0), ForeColor = SystemColors.GrayText };
            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended };
            var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 240, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(6) };
            side.Controls.Add(Ui.Btn(L.T("Markierte löschen"), DeleteSelected, 220));
            side.Controls.Add(Ui.Btn(L.T("Alles vergessen"), ClearAll, 220));
            side.Controls.Add(Ui.Btn(L.T("Schließen"), Close, 220));
            Controls.Add(_list);
            Controls.Add(side);
            Controls.Add(_info);
            Controls.Add(top);

            _lang.SelectedIndexChanged += (o, e) => Fill();
            _search.TextChanged += (o, e) => Fill();
            if (_lang.Items.Count > 0) _lang.SelectedIndex = 0;
        }

        private LearningStore Current()
        {
            int i = _lang.SelectedIndex;
            return i >= 0 && i < _langs.Active.Count ? _langs.Store(_langs.Active[i].Code) : null;
        }

        private bool NumbersShown { get { return _lang.SelectedIndex == _langs.Active.Count; } }
        private bool EmailsShown { get { return _lang.SelectedIndex == _langs.Active.Count + 1; } }

        private void Fill()
        {
            if (EmailsShown)
            {
                _list.BeginUpdate();
                _list.Items.Clear();
                string qe = _search.Text.Trim().ToLowerInvariant();
                _shown = _langs.Emails.All().Where(w => qe.Length == 0 || w.Form.ToLowerInvariant().Contains(qe)).ToList();
                foreach (var w in _shown) _list.Items.Add(w.Form + "   (" + w.Count + "×)");
                _info.Text = _langs.Emails.Count + L.T(" E-Mail-Adressen gelernt, für alle Sprachen");
                _list.EndUpdate();
                return;
            }
            if (NumbersShown)
            {
                _list.BeginUpdate();
                _list.Items.Clear();
                string qn = _search.Text.Trim();
                _shown = _langs.Numbers.All().Where(w => qn.Length == 0 || w.Form.Contains(qn) || NumberRules.Key(w.Form).Contains(NumberRules.Digits(qn))).ToList();
                foreach (var w in _shown) _list.Items.Add(w.Form + "   (" + w.Count + "×)");
                _info.Text = _langs.Numbers.Count + L.T(" Zahlen gelernt (Datum, Telefonnummern, Postleitzahlen), für alle Sprachen");
                _list.EndUpdate();
                return;
            }
            var st = Current();
            _list.BeginUpdate();
            _list.Items.Clear();
            _shown = new List<LearnedWord>();
            if (st != null)
            {
                string q = _search.Text.Trim().ToLowerInvariant();
                _shown = st.All().Where(w => q.Length == 0 || w.Form.ToLowerInvariant().Contains(q)).Take(3000).ToList();
                foreach (var w in _shown) _list.Items.Add(w.Form + "   (" + w.Count + "×)");
                _info.Text = st.WordCount + L.T(" Wörter, ") + st.PairCount + L.T(" Wortfolgen gelernt") + (_shown.Count >= 3000 ? L.T("   (die ersten 3000 angezeigt)") : "");
            }
            _list.EndUpdate();
        }

        private void DeleteSelected()
        {
            if (EmailsShown)
            {
                foreach (int i in _list.SelectedIndices) _langs.Emails.Remove(_shown[i].Form);
                Fill();
                var he = Changed; if (he != null) he();
                return;
            }
            if (NumbersShown)
            {
                foreach (int i in _list.SelectedIndices) _langs.Numbers.Remove(_shown[i].Form);
                Fill();
                var hn = Changed; if (hn != null) hn();
                return;
            }
            var st = Current();
            if (st == null || _list.SelectedIndices.Count == 0) return;
            foreach (int i in _list.SelectedIndices) st.Remove(_shown[i].Form);
            Fill();
            var h = Changed; if (h != null) h();
        }

        private void ClearAll()
        {
            if (EmailsShown)
            {
                if (MessageBox.Show(this, L.T("Alle gelernten E-Mail-Adressen vergessen?"), L.T("Alles vergessen"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                _langs.Emails.Clear();
                Fill();
                var he = Changed; if (he != null) he();
                return;
            }
            if (NumbersShown)
            {
                if (MessageBox.Show(this, L.T("Alle gelernten Zahlen vergessen?"), L.T("Alles vergessen"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                _langs.Numbers.Clear();
                Fill();
                var hn = Changed; if (hn != null) hn();
                return;
            }
            var st = Current();
            if (st == null) return;
            if (MessageBox.Show(this, L.T("Alles Gelernte für ") + _lang.Text + L.T(" vergessen?"), L.T("Alles vergessen"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            st.Clear();
            Fill();
            var h = Changed; if (h != null) h();
        }
    }

    /// <summary>Fortschritt beim Lernen aus Texten. Läuft im Hintergrund, abbrechbar.</summary>
    internal class ImportForm : Form
    {
        private readonly Label _label;
        private readonly ProgressBar _bar;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        public ImportResult Result { get; private set; }

        public ImportForm(List<string> paths, LanguageSet langs)
        {
            DpiInfo.Attach(this);
            Text = L.T("Aus meinen Texten lernen");
            Font = new Font("Segoe UI", 13f);
            Size = new Size(640, 260);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            MinimizeBox = MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            _label = new Label { Dock = DockStyle.Top, Height = 70, Padding = new Padding(12), Text = L.T("Suche Dateien …") };
            _bar = new ProgressBar { Dock = DockStyle.Top, Height = 34, Margin = new Padding(12) };
            var stop = new Button { Text = L.T("Abbrechen"), Dock = DockStyle.Bottom, Height = 56 };
            stop.Click += (o, e) => { _cts.Cancel(); stop.Enabled = false; };
            Controls.Add(stop);
            Controls.Add(_bar);
            Controls.Add(_label);

            Shown += async (o, e) =>
            {
                Result = await Task.Run(() => TextImporter.Import(paths, langs, (i, n, f) =>
                {
                    if (i % 5 != 0 && i != n) return;
                    BeginInvoke((Action)(() =>
                    {
                        _bar.Maximum = Math.Max(1, n);
                        _bar.Value = Math.Min(i, _bar.Maximum);
                        _label.Text = L.T("Datei ") + i + L.T(" von ") + n + "\n" + f;
                    }));
                }, _cts.Token));
                DialogResult = DialogResult.OK;
                Close();
            };
        }
    }
}
