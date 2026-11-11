using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Tastatur
{
    internal static class Ui
    {
        public static Button Btn(string text, Action click, int minWidth = 180)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, MinimumSize = new Size(minWidth, 50),
                Margin = new Padding(4), Padding = new Padding(8, 2, 8, 2)
            };
            b.Click += (o, e) => click();
            return b;
        }

        public static Label Lbl(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(4, 10, 4, 2) };
        }
    }

    /// <summary>Liste aller Makros mit großen Knöpfen zum Anlegen, Ändern, Sortieren und Löschen.</summary>
    internal class MacroEditorForm : Form
    {
        private readonly IAppHost _host;
        private List<Macro> _macros;
        private readonly ListBox _list;
        public event Action<List<Macro>> Saved;
        public event Action ReloadRequested;

        public MacroEditorForm(List<Macro> macros, IAppHost host, string loadError)
        {
            DpiInfo.Attach(this);
            _host = host;
            _macros = macros.Select(m => m.Clone()).ToList();
            Text = L.T("Makros bearbeiten");
            Font = new Font("Segoe UI", 13f);
            Size = new Size(980, 720);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;

            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, ItemHeight = 40, DrawMode = DrawMode.OwnerDrawFixed };
            _list.DrawItem += DrawItem;
            _list.DoubleClick += (o, e) => Edit();

            var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 230, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(6), AutoScroll = true };
            // breit genug für lange Beschriftungen in anderen Sprachen
            Load += (o, e) => { int w = 0; foreach (Control c in side.Controls) w = Math.Max(w, c.PreferredSize.Width + c.Margin.Horizontal); side.Width = Math.Max(230, w + side.Padding.Horizontal + SystemInformation.VerticalScrollBarWidth); };
            side.Controls.Add(Ui.Btn(L.T("Neu"), New));
            side.Controls.Add(Ui.Btn(L.T("Bearbeiten"), Edit));
            side.Controls.Add(Ui.Btn(L.T("Kopieren"), Copy));
            side.Controls.Add(Ui.Btn(L.T("▲  Hoch"), () => MoveItem(-1)));
            side.Controls.Add(Ui.Btn(L.T("▼  Runter"), () => MoveItem(1)));
            side.Controls.Add(Ui.Btn(L.T("Löschen"), Delete));
            side.Controls.Add(new Label { Height = 20 });
            side.Controls.Add(Ui.Btn(L.T("Datei im Editor öffnen"), OpenFile));
            side.Controls.Add(Ui.Btn(L.T("Datei neu laden"), () => { var h = ReloadRequested; if (h != null) h(); }));
            side.Controls.Add(Ui.Btn(L.T("Schließen"), Close));

            Controls.Add(_list);
            Controls.Add(side);

            if (loadError != null)
            {
                var warn = new Label
                {
                    Dock = DockStyle.Top, AutoSize = false, Height = 70, ForeColor = Color.Firebrick, Padding = new Padding(8),
                    Text = "⚠ " + loadError + L.T("\nSpeichern hier überschreibt die fehlerhafte Datei (eine Sicherung bleibt als makros.yaml.bak).")
                };
                Controls.Add(warn);
            }
            Fill(0);
        }

        public void SetMacros(List<Macro> macros)
        {
            _macros = macros.Select(m => m.Clone()).ToList();
            Fill(0);
        }

        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= _macros.Count) return;
            var m = _macros[e.Index];
            var c = Macro.ColorValue(m.Color);
            int x = e.Bounds.X + 6;
            if (c.HasValue)
                using (var b = new SolidBrush(c.Value)) e.Graphics.FillRectangle(b, x, e.Bounds.Y + 8, 14, e.Bounds.Height - 16);
            x += 24;
            TextRenderer.DrawText(e.Graphics, m.ToString(), e.Font, new Rectangle(x, e.Bounds.Y, e.Bounds.Width - x, e.Bounds.Height),
                e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        private void Fill(int select)
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var m in _macros) _list.Items.Add(m);
            _list.EndUpdate();
            if (_macros.Count > 0) _list.SelectedIndex = Math.Max(0, Math.Min(select, _macros.Count - 1));
        }

        private void Commit(int select)
        {
            Fill(select);
            var h = Saved; if (h != null) h(_macros.Select(m => m.Clone()).ToList());
        }

        private List<string> Groups()
        {
            return _macros.Select(m => m.Group).Where(g => g.Length > 0).Distinct().ToList();
        }

        private void New()
        {
            var m = new Macro { Name = L.T("Neues Makro") };
            m.Steps.Add(new MacroStep(StepKind.Text, ""));
            using (var d = new MacroEditDialog(m, Groups(), _host))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                int at = _list.SelectedIndex >= 0 ? _list.SelectedIndex + 1 : _macros.Count;
                _macros.Insert(at, d.Result);
                Commit(at);
            }
        }

        private void Edit()
        {
            int i = _list.SelectedIndex;
            if (i < 0) return;
            using (var d = new MacroEditDialog(_macros[i], Groups(), _host))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                _macros[i] = d.Result;
                Commit(i);
            }
        }

        private void Copy()
        {
            int i = _list.SelectedIndex;
            if (i < 0) return;
            var c = _macros[i].Clone();
            c.Name += L.T(" (Kopie)");
            c.Hotkey = "";
            _macros.Insert(i + 1, c);
            Commit(i + 1);
        }

        private void MoveItem(int d)
        {
            int i = _list.SelectedIndex;
            int j = i + d;
            if (i < 0 || j < 0 || j >= _macros.Count) return;
            var t = _macros[i]; _macros[i] = _macros[j]; _macros[j] = t;
            Commit(j);
        }

        private void Delete()
        {
            int i = _list.SelectedIndex;
            if (i < 0) return;
            if (MessageBox.Show(this, L.T("Makro \"") + _macros[i].Name + L.T("\" löschen?"), L.T("Löschen"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _macros.RemoveAt(i);
            Commit(i);
        }

        private void OpenFile()
        {
            try { Process.Start("notepad.exe", "\"" + MacroStore.FilePath + "\""); } catch { }
        }
    }

    /// <summary>Ein Makro bearbeiten: Name, Gruppe, Programm, Farbe, Kürzel und Schritte.</summary>
    internal class MacroEditDialog : Form
    {
        private readonly Macro _m;
        private readonly IAppHost _host;
        public Macro Result { get; private set; }

        private readonly TextBox _name, _program, _value;
        private readonly ComboBox _group, _color;
        private readonly Label _hotkey, _hint;
        private readonly ListBox _steps;
        private bool _updating;

        public MacroEditDialog(Macro macro, List<string> groups, IAppHost host)
        {
            DpiInfo.Attach(this);
            _m = macro.Clone();
            _host = host;
            Text = L.T("Makro bearbeiten");
            Font = new Font("Segoe UI", 13f);
            Size = new Size(1000, 940);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            MinimizeBox = MaximizeBox = false;

            var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
            Controls.Add(root);

            root.Controls.Add(Ui.Lbl(L.T("Name (Beschriftung des Knopfs)")));
            _name = new TextBox { Text = _m.Name, Width = 600 };
            root.Controls.Add(_name);

            root.Controls.Add(Ui.Lbl(L.T("Gruppe (leer = keine)")));
            _group = new ComboBox { Text = _m.Group, Width = 400, DropDownStyle = ComboBoxStyle.DropDown };
            foreach (var g in groups) _group.Items.Add(g);
            root.Controls.Add(_group);

            root.Controls.Add(Ui.Lbl(L.T("Farbe")));
            _color = new ComboBox { Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var c in Macro.ColorNames) _color.Items.Add(Macro.ColorLabel(c));
            int ci = Array.IndexOf(Macro.ColorNames, _m.Color.ToLowerInvariant());
            _color.SelectedIndex = ci < 0 ? 0 : ci;
            root.Controls.Add(_color);

            root.Controls.Add(Ui.Lbl(L.T("Nur für Programm (z. B. chrome oder \"chrome, msedge\", leer = überall)")));
            _program = new TextBox { Text = _m.Program, Width = 600 };
            root.Controls.Add(_program);

            var hk = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
            _hotkey = new Label { AutoSize = true, Margin = new Padding(4, 14, 12, 4) };
            hk.Controls.Add(_hotkey);
            hk.Controls.Add(Ui.Btn(L.T("Kürzel aufnehmen"), () =>
            {
                var r = SettingsForm.CaptureKey(this, _host, L.T("Kürzel für \"") + _name.Text + "\"");
                if (r != null) { _m.Hotkey = r; ShowHotkey(); }
            }, 200));
            hk.Controls.Add(Ui.Btn(L.T("Kein Kürzel"), () => { _m.Hotkey = ""; ShowHotkey(); }, 160));
            root.Controls.Add(hk);
            ShowHotkey();

            root.Controls.Add(Ui.Lbl(L.T("Schritte (werden der Reihe nach ausgeführt)")));
            var stepRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            _steps = new ListBox { Width = 640, Height = 190, IntegralHeight = false };
            _steps.SelectedIndexChanged += (o, e) => ShowStep();
            stepRow.Controls.Add(_steps);
            var stepBtns = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
            stepBtns.Controls.Add(Ui.Btn("▲", () => MoveStep(-1), 90));
            stepBtns.Controls.Add(Ui.Btn("▼", () => MoveStep(1), 90));
            stepBtns.Controls.Add(Ui.Btn("✕", DeleteStep, 90));
            stepRow.Controls.Add(stepBtns);
            root.Controls.Add(stepRow);

            var add = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 900, Margin = new Padding(0) };
            add.Controls.Add(Ui.Btn(L.T("+ Text"), () => AddStep(StepKind.Text, ""), 120));
            add.Controls.Add(Ui.Btn(L.T("+ Tasten"), () => AddStep(StepKind.Keys, ""), 120));
            add.Controls.Add(Ui.Btn(L.T("+ Öffnen"), () => AddStep(StepKind.Open, ""), 120));
            add.Controls.Add(Ui.Btn(L.T("+ Warten"), () => AddStep(StepKind.Wait, "500"), 120));
            add.Controls.Add(Ui.Btn(L.T("Taste aufnehmen"), CaptureIntoStep, 180));
            root.Controls.Add(add);

            _hint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(900, 0), Margin = new Padding(4, 8, 4, 2) };
            root.Controls.Add(_hint);
            _value = new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Width = 900, Height = 130 };
            _value.TextChanged += (o, e) =>
            {
                if (_updating) return;
                int i = _steps.SelectedIndex;
                if (i < 0) return;
                _m.Steps[i].Value = _value.Text.Replace("\r\n", "\n");
                _updating = true;
                _steps.Items[i] = _m.Steps[i].ToString();
                _updating = false;
            };
            root.Controls.Add(_value);

            var bottom = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
            bottom.Controls.Add(Ui.Btn(L.T("Speichern"), Save, 200));
            bottom.Controls.Add(Ui.Btn(L.T("Abbrechen"), () => { DialogResult = DialogResult.Cancel; Close(); }, 200));
            root.Controls.Add(bottom);

            FillSteps(0);
        }

        private void ShowHotkey()
        {
            _hotkey.Text = L.T("Globales Kürzel: ") + (_m.Hotkey.Length > 0 ? _m.Hotkey : "keins");
        }

        private void FillSteps(int select)
        {
            _updating = true;
            _steps.Items.Clear();
            foreach (var s in _m.Steps) _steps.Items.Add(s.ToString());
            _updating = false;
            if (_m.Steps.Count > 0) _steps.SelectedIndex = Math.Max(0, Math.Min(select, _m.Steps.Count - 1));
            else ShowStep();
        }

        private void ShowStep()
        {
            if (_updating) return;
            int i = _steps.SelectedIndex;
            _updating = true;
            if (i < 0)
            {
                _value.Text = "";
                _value.Enabled = false;
                _hint.Text = L.T("Noch keine Schritte. Unten einen hinzufügen.");
            }
            else
            {
                var s = _m.Steps[i];
                _value.Enabled = true;
                _value.Text = s.Value.Replace("\n", "\r\n");
                switch (s.Kind)
                {
                    case StepKind.Text: _hint.Text = L.T("Text: mehrere Zeilen möglich. Platzhalter: {datum} {uhrzeit} {wochentag} {cursor}"); break;
                    case StepKind.Keys: _hint.Text = L.T("Tasten mit Komma trennen, z. B.  Strg+A, Strg+C, Enter   oder  Alt+Tab   oder  Win+D.  Namen: Enter Tab Esc Leertaste Rück Entf Pos1 Ende BildAuf BildAb Links Rechts Hoch Runter F1…F24"); break;
                    case StepKind.Open: _hint.Text = L.T("Programm, Datei, Ordner oder Webseite, z. B.  notepad.exe   C:\\Rechnungen   https://udaption.com"); break;
                    case StepKind.Wait: _hint.Text = L.T("Pause in Millisekunden (1000 = 1 Sekunde)"); break;
                }
            }
            _updating = false;
        }

        private void AddStep(StepKind k, string v)
        {
            int at = _steps.SelectedIndex >= 0 ? _steps.SelectedIndex + 1 : _m.Steps.Count;
            _m.Steps.Insert(at, new MacroStep(k, v));
            FillSteps(at);
            _value.Focus();
        }

        private void CaptureIntoStep()
        {
            var r = SettingsForm.CaptureKey(this, _host, L.T("Taste für den Schritt"));
            if (r == null) return;
            int i = _steps.SelectedIndex;
            if (i >= 0 && _m.Steps[i].Kind == StepKind.Keys)
            {
                var s = _m.Steps[i];
                s.Value = s.Value.Trim().Length == 0 ? r : s.Value.TrimEnd() + ", " + r;
                FillSteps(i);
            }
            else AddStep(StepKind.Keys, r);
        }

        private void MoveStep(int d)
        {
            int i = _steps.SelectedIndex, j = i + d;
            if (i < 0 || j < 0 || j >= _m.Steps.Count) return;
            var t = _m.Steps[i]; _m.Steps[i] = _m.Steps[j]; _m.Steps[j] = t;
            FillSteps(j);
        }

        private void DeleteStep()
        {
            int i = _steps.SelectedIndex;
            if (i < 0) return;
            _m.Steps.RemoveAt(i);
            FillSteps(i);
        }

        private void Save()
        {
            _m.Name = _name.Text.Trim();
            _m.Group = (_group.Text ?? "").Trim();
            _m.Program = _program.Text.Trim();
            _m.Color = _color.SelectedIndex <= 0 ? "" : Macro.ColorNames[_color.SelectedIndex];
            _m.Steps.RemoveAll(s => s.Kind != StepKind.Wait && s.Value.Trim().Length == 0);
            if (_m.Name.Length == 0) { MessageBox.Show(this, L.T("Bitte einen Namen eingeben.")); return; }
            foreach (var s in _m.Steps)
            {
                string bad;
                if (s.Kind == StepKind.Keys && KeyCombo.ParseSequence(s.Value, out bad) == null)
                {
                    MessageBox.Show(this, L.T("Unbekannte Taste \"") + bad + L.T("\" im Schritt: ") + s);
                    return;
                }
                int ms;
                if (s.Kind == StepKind.Wait && (!int.TryParse(s.Value.Trim(), out ms) || ms < 0))
                {
                    MessageBox.Show(this, L.T("\"Warten\" braucht eine Zahl in Millisekunden."));
                    return;
                }
            }
            Result = _m;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Name und Gruppe für ein neues Makro (Aufnahme oder Schnell-Speichern). Ein Klick reicht.</summary>
    internal class NameDialog : Form
    {
        public string MacroName { get { return _name.Text.Trim(); } }
        public string Group { get { return (_group.Text ?? "").Trim(); } }
        private readonly TextBox _name;
        private readonly ComboBox _group;

        public NameDialog(string title, string preview, string suggestedName, List<string> groups)
        {
            DpiInfo.Attach(this);
            Text = title;
            Font = new Font("Segoe UI", 14f);
            Size = new Size(720, 460);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            MinimizeBox = MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;

            var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14) };
            Controls.Add(root);
            root.Controls.Add(new Label
            {
                Text = preview, AutoSize = true, MaximumSize = new Size(660, 110), ForeColor = SystemColors.GrayText
            });
            root.Controls.Add(Ui.Lbl(L.T("Name")));
            _name = new TextBox { Text = suggestedName, Width = 640 };
            root.Controls.Add(_name);
            root.Controls.Add(Ui.Lbl(L.T("Gruppe (leer = keine)")));
            _group = new ComboBox { Width = 400, DropDownStyle = ComboBoxStyle.DropDown };
            foreach (var g in groups) _group.Items.Add(g);
            root.Controls.Add(_group);

            var bottom = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 14, 0, 0) };
            var ok = Ui.Btn(L.T("Speichern"), () =>
            {
                if (MacroName.Length == 0) return;
                DialogResult = DialogResult.OK; Close();
            }, 220);
            bottom.Controls.Add(ok);
            bottom.Controls.Add(Ui.Btn(L.T("Verwerfen"), () => { DialogResult = DialogResult.Cancel; Close(); }, 220));
            root.Controls.Add(bottom);
            AcceptButton = ok;
            Shown += (o, e) => { _name.Focus(); _name.SelectAll(); };
        }
    }
}
