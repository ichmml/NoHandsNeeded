using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Tastatur
{
    /// <summary>Was soll in die Sicherung? Große Haken, Passwort beim Tippen sichtbar.</summary>
    internal class ExportForm : Form
    {
        public bool Words { get { return _words.Checked; } }
        public bool Custom { get { return _custom.Checked; } }
        public bool Macros { get { return _macros.Checked; } }
        public bool VaultIncluded { get { return _vault.Checked; } }
        public string Password { get { return _pw.Text; } }

        private readonly CheckBox _words, _custom, _macros, _vault;
        private readonly TextBox _pw;
        private readonly Label _hint;

        public ExportForm(string wordsInfo, string customInfo, string macroInfo, string vaultInfo, bool vaultUsable)
        {
            DpiInfo.Attach(this);
            Text = L.T("Daten exportieren");
            Font = new Font("Segoe UI", 13f);
            Size = new Size(780, 720);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            MinimizeBox = MaximizeBox = false;

            var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14) };
            Controls.Add(root);
            root.Controls.Add(new Label { Text = L.T("Alles in eine Datei packen, z. B. für den zweiten PC. Dort über \"Importieren\" wieder einlesen."), AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(4, 0, 4, 8) });
            Func<string, string, bool, CheckBox> box = (t, info, on) =>
            {
                var c = new CheckBox { Text = t + "  (" + info + ")", Checked = on, AutoSize = true, Margin = new Padding(4, 8, 4, 4) };
                root.Controls.Add(c);
                return c;
            };
            _words = box(L.T("Gelernte Wörter, Wortfolgen und Zahlen"), wordsInfo, true);
            _custom = box(L.T("Eigene Begriffe"), customInfo, true);
            _macros = box(L.T("Makros"), macroInfo, true);
            _vault = box(L.T("Passwörter aus dem Tresor"), vaultInfo, vaultUsable);
            _vault.Enabled = vaultUsable;

            root.Controls.Add(Ui.Lbl(L.T("Passwort für die Sicherung")));
            _pw = new TextBox { Width = 700 };
            var refresh = SecretField.Attach(_pw);
            root.Controls.Add(_pw);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            row.Controls.Add(Ui.Btn(L.T("Anzeigen / verbergen"), () => { _pw.UseSystemPasswordChar = !_pw.UseSystemPasswordChar; refresh(); }, 220));
            root.Controls.Add(row);
            _hint = new Label { AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(4, 6, 4, 4) };
            root.Controls.Add(_hint);
            Action upd = () => _hint.Text = _vault.Checked
                ? L.T("Mit Passwörtern wird die Datei immer verschlüsselt. Das Passwort brauchst du beim Importieren, schreib es dir auf. Mindestens 8 Zeichen.")
                : L.T("Ohne Passwörter darfst du das Feld leer lassen. Mit Passwort wird die Datei verschlüsselt.");
            _vault.CheckedChanged += (o, e) => upd();
            upd();

            var bottom = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 14, 0, 0) };
            bottom.Controls.Add(Ui.Btn(L.T("Speichern unter …"), () =>
            {
                if (!Words && !Custom && !Macros && !VaultIncluded) { MessageBox.Show(this, L.T("Bitte mindestens einen Haken setzen.")); return; }
                if (VaultIncluded && _pw.Text.Length < 8) { MessageBox.Show(this, L.T("Für Passwörter braucht die Sicherung ein Passwort mit mindestens 8 Zeichen.")); _pw.Focus(); return; }
                DialogResult = DialogResult.OK; Close();
            }, 240));
            bottom.Controls.Add(Ui.Btn(L.T("Abbrechen"), () => { DialogResult = DialogResult.Cancel; Close(); }, 200));
            root.Controls.Add(bottom);
        }
    }

    /// <summary>Sicherung öffnen (bei Bedarf mit Passwort) und auswählen, was übernommen wird.</summary>
    internal class ImportForm2 : Form
    {
        public Backup Result { get; private set; }
        public bool Words { get { return _words != null && _words.Checked; } }
        public bool Custom { get { return _custom != null && _custom.Checked; } }
        public bool Macros { get { return _macros != null && _macros.Checked; } }
        public bool VaultIncluded { get { return _vault != null && _vault.Checked; } }

        private readonly Backup.Header _header;
        private readonly FlowLayoutPanel _root;
        private TextBox _pw;
        private Label _pwHint;
        private CheckBox _words, _custom, _macros, _vault;
        private int _tries;

        public ImportForm2(Backup.Header header)
        {
            DpiInfo.Attach(this);
            _header = header;
            Text = L.T("Daten importieren");
            Font = new Font("Segoe UI", 13f);
            Size = new Size(780, 720);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            MinimizeBox = MaximizeBox = false;
            _root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14) };
            Controls.Add(_root);

            string from = header.Created == DateTime.MinValue ? "" : header.Created.ToString("g");
            if (header.Computer.Length > 0) from += (from.Length > 0 ? ", " : "") + header.Computer;
            if (from.Length > 0) _root.Controls.Add(new Label { Text = L.T("Sicherung vom ") + from, AutoSize = true, Margin = new Padding(4, 0, 4, 8) });

            if (header.Encrypted) AskPassword();
            else ShowContent(Backup.Open(header, null));
        }

        private void AskPassword()
        {
            _root.Controls.Add(Ui.Lbl(L.T("Die Sicherung ist verschlüsselt. Passwort der Sicherung:")));
            _pw = new TextBox { Width = 700 };
            var refresh = SecretField.Attach(_pw);
            _root.Controls.Add(_pw);
            _pwHint = new Label { AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(4, 4, 4, 4) };
            _root.Controls.Add(_pwHint);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            row.Controls.Add(Ui.Btn(L.T("Öffnen"), TryOpen, 200));
            row.Controls.Add(Ui.Btn(L.T("Anzeigen / verbergen"), () => { _pw.UseSystemPasswordChar = !_pw.UseSystemPasswordChar; refresh(); }, 220));
            row.Controls.Add(Ui.Btn(L.T("Abbrechen"), () => { DialogResult = DialogResult.Cancel; Close(); }, 200));
            _root.Controls.Add(row);
            Shown += (o, e) => _pw.Focus();
        }

        private void TryOpen()
        {
            Cursor = Cursors.WaitCursor;
            Backup b = null;
            try { b = Backup.Open(_header, _pw.Text); }
            catch (Exception ex) { _pwHint.Text = L.T("Datei beschädigt: ") + ex.Message; return; }
            finally { Cursor = Cursors.Default; }
            if (b == null)
            {
                _tries++;
                _pwHint.Text = L.T("Passwort falsch, nochmal versuchen.");
                _pw.SelectAll(); _pw.Focus();
                return;
            }
            _pw.Text = "";
            _root.Controls.Clear();
            ShowContent(b);
        }

        private void ShowContent(Backup b)
        {
            Result = b;
            _root.Controls.Add(new Label { Text = L.T("Was soll übernommen werden? Vorhandenes bleibt, Neues kommt dazu."), AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(4, 0, 4, 8) });
            Func<string, bool, CheckBox> box = (t, has) =>
            {
                var c = new CheckBox { Text = t, Checked = has, Enabled = has, AutoSize = true, Margin = new Padding(4, 8, 4, 4) };
                _root.Controls.Add(c);
                return c;
            };
            var codes = b.LearnCodes.ToList();
            int words = 0, pairs = 0;
            foreach (var c in codes) { var st = LearningStore.FromText(b.Files[Backup.LearnFile(c)], c); words += st.WordCount; pairs += st.PairCount; }
            int nums = b.Files.ContainsKey(Backup.NumbersFile) ? NumberStore.FromText(b.Files[Backup.NumbersFile]).Count : 0;
            _words = box(L.F("Gelernte Wörter ({0}), Wortfolgen ({1}), Zahlen ({2})", words, pairs, nums) + (codes.Count > 0 ? ", " + string.Join(", ", codes.Select(c => c.ToUpperInvariant())) : ""), codes.Count > 0 || nums > 0);
            int custom = b.Files.ContainsKey(Backup.CustomFile) ? b.Files[Backup.CustomFile].Split('\n').Count(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#")) : 0;
            _custom = box(L.F("Eigene Begriffe ({0})", custom), b.Files.ContainsKey(Backup.CustomFile));
            int macros = 0;
            if (b.Files.ContainsKey(Backup.MacroFile)) { try { macros = MacroStore.Parse(b.Files[Backup.MacroFile]).Count; } catch { } }
            _macros = box(L.F("Makros ({0})", macros), macros > 0);
            int pws = b.HasVault ? Vault.Deserialize(b.Files[Backup.VaultFile]).Count : 0;
            _vault = box(L.F("Passwörter ({0})", pws), pws > 0);
            _root.Controls.Add(new Label { Text = L.T("Bei gleichen Wörtern zählt der höhere Zähler. Ein Makro mit gleichem Namen wird ersetzt. Bei einem Passwort mit gleichem Namen und Benutzer gewinnt das zuletzt geänderte oder benutzte."), AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(4, 10, 4, 4) });
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 14, 0, 0) };
            row.Controls.Add(Ui.Btn(L.T("Übernehmen"), () => { DialogResult = DialogResult.OK; Close(); }, 220));
            row.Controls.Add(Ui.Btn(L.T("Abbrechen"), () => { Result = null; DialogResult = DialogResult.Cancel; Close(); }, 200));
            _root.Controls.Add(row);
        }
    }
}
