using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Tastatur
{
    internal interface IAppHost
    {
        void SuspendHotkeys();
        /// <summary>Kürzel neu registrieren, liefert den Stand jedes Kürzels.</summary>
        List<HotkeyStatus> ApplyHotkeys();
        List<HotkeyStatus> CurrentHotkeyStatus { get; }
        void OpenMacroEditor();
        void OpenKeyTest();
        LanguageManager Languages { get; }
        string LanguageSource { get; }
        void LanguagesChanged();
        void ActivateLanguage(string code);
        string LearningSummary { get; }
        void ImportTexts(List<string> paths);
        void OpenLearnedWords();
        void OpenVaultEditor();
        string VaultSummary { get; }
        bool VaultHasPin { get; }
        /// <summary>PIN festlegen oder ändern (neu = true) bzw. entfernen (neu = false). Liefert Meldung für den Nutzer.</summary>
        string ChangeVaultPin(bool setNew);
        void ApplyUiLanguage();
        void ShowTour();
        string LockScreenStatus { get; }
        string SetLockScreen(bool on);
        void ExportData();
        void ImportData();
        void OpenManual();
    }

    /// <summary>Einstellungen mit großen, gut anklickbaren Elementen. Änderungen wirken sofort.</summary>
    internal class SettingsForm : Form
    {
        private readonly Settings _s;
        private readonly IAppHost _host;
        public event Action SettingsChanged;
        public event Action ResetPositionRequested;
        public event Action ExitRequested;

        private readonly FlowLayoutPanel _panel;
        private Label _hotkeyWarning;
        private readonly List<Action<List<HotkeyStatus>>> _hotkeyRefresh = new List<Action<List<HotkeyStatus>>>();

        public SettingsForm(Settings s, IAppHost host)
        {
            DpiInfo.Attach(this);
            _s = s;
            _host = host;
            Text = AppInfo.Name + ": " + L.T("Einstellungen") + "  (" + L.T("Version") + " " + AppInfo.Version + ")";
            Font = new Font("Segoe UI", 12f);
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(680, 820);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;

            _panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(16)
            };
            Controls.Add(_panel);

            // Ganz oben und auffällig: Ko-fi
            var kofiNote = new Label { Text = L.T("Die Tastatur ist kostenlos. Wenn sie dir hilft, freue ich mich über eine Spende."), AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(4, 10, 4, 4) };
            _panel.Controls.Add(kofiNote);
            var kofi = new Button { Text = L.T("☕ Unterstütze mich mit einer Spende (Ko-fi)"), AutoSize = true, MinimumSize = new Size(420, 56),
                BackColor = Color.FromArgb(255, 94, 91), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font(Font.FontFamily, Font.Size * 1.1f, FontStyle.Bold), Margin = new Padding(4, 2, 4, 10) };
            kofi.FlatAppearance.BorderSize = 0;
            kofi.Click += (o, e) => Settings.OpenLink(_s.DonateLink);
            _panel.Controls.Add(kofi);
            // Direkt unter Ko-fi: woher der Name kommt
            _panel.Controls.Add(new Label { Text = L.T("Warum NoHandsNeeded? Der Name ist Englisch und heißt „keine Hände nötig“. Genau dafür ist die Tastatur gebaut: für alle, die den PC ohne Hände bedienen, zum Beispiel mit einem Mundjoystick wie dem Quadstick, einer Kopfmaus oder mit den Augen."), AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(4, 0, 4, 12) });
            var manual = new Button { Text = L.T("📖 Anleitung öffnen"), AutoSize = true, MinimumSize = new Size(420, 50), Margin = new Padding(4, 0, 4, 10) };
            manual.Click += (o, e) => _host.OpenManual();
            _panel.Controls.Add(manual);

            Heading(L.T("Aussehen"));
            Slider(L.T("Deckkraft Tastatur"), 30, 100, 5, s.OpacityPercent, v => v + " %", v => s.OpacityPercent = v);
            Slider(L.T("Deckkraft runder Knopf (eingeklappt)"), 20, 100, 5, s.MiniOpacityPercent, v => v + " %", v => s.MiniOpacityPercent = v);
            Choice(L.T("Farbschema"), new[] { "dunkel", "hell" }, s.Theme, v => s.Theme = v, new[] { L.T("dunkel"), L.T("hell") }); // i18n-ok

            Heading(L.T("Bedienung"));
            Check(L.T("Verweilklick (Taste löst nach Verweilzeit aus)"), s.DwellEnabled, v => s.DwellEnabled = v);
            Slider(L.T("Verweilzeit"), 200, 3000, 100, s.DwellMs, v => (v / 1000.0).ToString("0.0") + " s", v => s.DwellMs = v);
            Check(L.T("Echte Tastendrücke anzeigen (z. B. vom Quadstick, leuchten rot)"), s.ShowPhysicalKeys, v => s.ShowPhysicalKeys = v);
            Slider(L.T("Nachleuchten"), 0, 5000, 250, s.PhysicalGlowMs, v => v == 0 ? L.T("aus") : (v / 1000.0).ToString("0.0#") + " s", v => s.PhysicalGlowMs = v);
            Note(L.T("Die Tastatur merkt sich pro Fenster das angefangene Wort. Wechselst du zurück, schreibst du einfach weiter."));
            Check(L.T("Auch nach Klick ins Fenster weiterschreiben"), s.ContinueAfterClick, v => s.ContinueAfterClick = v);
            Note(L.T("Aus: Nur beim Wechsel über Taskleiste, Alt+Tab oder Titelleiste geht es weiter. Ein Klick in den Text kann den Cursor versetzen."));

            Heading(L.T("Wortvorschläge"));
            Choice(L.T("Zeilen"), new[] { L.T("1 Zeile"), L.T("2 Zeilen") }, s.SuggestionRows == 2 ? L.T("2 Zeilen") : L.T("1 Zeile"),
                v => s.SuggestionRows = v == L.T("2 Zeilen") ? 2 : 1);
            Slider(L.T("Vorschläge pro Zeile"), 1, 8, 1, s.SuggestionCount, v => v.ToString(), v => s.SuggestionCount = v);
            Slider(L.T("Schriftgröße Vorschläge und Makros"), 20, 70, 5, s.SuggestionFontPercent, v => v + " %", v => s.SuggestionFontPercent = v);
            Check(L.T("Satzanfang automatisch groß"), s.AutoCapitalize, v => s.AutoCapitalize = v);
            Check(L.T("Leerzeichen nach Vorschlag einfügen"), s.AutoSpaceAfterSuggestion, v => s.AutoSpaceAfterSuggestion = v);
            Check(L.T("Leerzeichen nach . , ! ? : von selbst einfügen"), s.AutoSpaceAfterPunctuation, v => s.AutoSpaceAfterPunctuation = v);
            Check(L.T("Getipptes Wort oben links zeigen (rot)"), s.ShowInputField, v => s.ShowInputField = v);
            var pwOpts = new[] { L.T("Punkte mit 👁"), L.T("immer nur Punkte") };
            Choice(L.T("Passwörter beim Tippen"), pwOpts, s.PasswordReveal ? pwOpts[0] : pwOpts[1], v => s.PasswordReveal = v == pwOpts[0]);
            Slider(L.T("Klartext automatisch ausblenden nach"), 3, 30, 1, s.RevealSeconds, v => v + " s", v => s.RevealSeconds = v);
            Note(L.T("Im Passwortfeld stehen nur Punkte. 👁 zeigt das Passwort kurz im Klartext, solange ist die Tastatur aus Bildschirmfotos und Bildschirmfreigaben ausgenommen."));

            Heading(L.T("Sprachen"));
            Note(L.T("Haken = aktiv. Die Reihenfolge gilt für den Sprach-Knopf oben in der Tastatur."));
            AddLanguages();
            Check(L.T("Gemischt (z. B. DE+EN) beim Umschalten anbieten"), s.OfferMix, v => { s.OfferMix = v; _host.LanguagesChanged(); });
            Check(L.T("Sprache pro Programm merken"), s.PerProgramLanguage, v => s.PerProgramLanguage = v);
            Note(L.T("Wechselst du in einem Programm die Sprache, merkt sich die Tastatur das und stellt beim nächsten Mal in diesem Programm automatisch um. Zum Beispiel Outlook Englisch, WhatsApp Deutsch."));
            var layouts = new[] { L.T("passend zur Sprache"), L.T("immer Deutsch (QWERTZ)"), L.T("immer Englisch (QWERTY)") };
            var layoutKeys = new[] { "sprache", "de", "en" };
            Choice(L.T("Tastenlayout"), layouts, layouts[Math.Max(0, Array.IndexOf(layoutKeys, s.KeyLayout))], v => s.KeyLayout = layoutKeys[Array.IndexOf(layouts, v)]);
            AddButton(L.T("Eigene Begriffe bearbeiten (Namen, Fachwörter)"), () =>
            {
                try
                {
                    _host.Languages.LoadCustomWords();
                    var proc = Process.Start("notepad.exe", "\"" + _host.Languages.CustomWordsPath + "\"");
                    if (proc != null) { proc.EnableRaisingEvents = true; proc.Exited += (o2, e2) => BeginInvoke((Action)_host.LanguagesChanged); }
                }
                catch { }
            });

            Heading(L.T("Sonderzeichen unten"));
            Note(L.T("Die vier Tasten unten neben abc/ABC. Pro Feld ein Zeichen, optional ein zweites für Shift (z. B. \".:\"). Leer = Standard."));
            var charRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var charBoxes = new List<TextBox>();
            var cur = s.BottomChars.Split(' ');
            for (int i = 0; i < 4; i++)
            {
                charRow.Controls.Add(new Label { Text = (i + 1) + ":", AutoSize = true, Margin = new Padding(i == 0 ? 4 : 14, 12, 2, 4) });
                var tb = new TextBox { Text = i < cur.Length ? cur[i] : "", Width = 70, MaxLength = 4, TextAlign = HorizontalAlignment.Center, Font = new Font(Font.FontFamily, Font.Size * 1.3f) };
                charBoxes.Add(tb);
                charRow.Controls.Add(tb);
            }
            Action saveChars = () =>
            {
                string v = KeyLayouts.NormalizeBottomChars(string.Join(" ", charBoxes.Select(t => t.Text.Trim().Length == 0 ? "\u0001" : t.Text.Trim().Replace(" ", ""))));
                if (v == s.BottomChars) return;
                s.BottomChars = v; Changed();
            };
            foreach (var tb in charBoxes) tb.TextChanged += (o, e) => saveChars();
            _panel.Controls.Add(charRow);
            AddButton(L.T("Auf Standard zurücksetzen"), () =>
            {
                var d = KeyLayouts.DefaultBottomChars.Split(' ');
                for (int i = 0; i < 4; i++) charBoxes[i].Text = d[i];
                s.BottomChars = KeyLayouts.DefaultBottomChars; Changed();
            });

            Heading(L.T("Lernen"));
            Note(L.T("Die Tastatur merkt sich, welche Wörter du schreibst, in welcher Schreibweise und welches Wort oft auf welches folgt. Alles bleibt auf diesem PC. In Passwortfeldern wird nie gelernt."));
            Check(L.T("Aus meinem Schreiben lernen"), s.LearningEnabled, v => s.LearningEnabled = v);
            Check(L.T("Auch Zahlen lernen (Datum, Telefonnummern, Postleitzahlen)"), s.LearnNumbers, v => s.LearnNumbers = v);
            Check(L.T("E-Mail-Adressen lernen und nach 3 Buchstaben vorschlagen"), s.LearnEmails, v => s.LearnEmails = v);
            Check(L.T("In E-Mail-Feldern gleich die E-Mail-Adressen zeigen"), s.EmailFieldSuggest, v => s.EmailFieldSuggest = v);
            Check(L.T("IBAN merken und vorschlagen"), s.LearnIban, v => s.LearnIban = v);
            Check(L.T("PINs und Kartennummern lernen (verschlüsselt)"), s.LearnSecretNumbers, v => s.LearnSecretNumbers = v);
            Note(L.T("Zahlen werden schon nach dem ersten Mal vorgeschlagen. Eine IBAN nur, wenn die Prüfziffer stimmt. PINs und Kartennummern liegen verschlüsselt auf diesem PC und kommen nur in eine Sicherung mit Passwort."));
            Slider(L.T("Neue Wörter vorschlagen ab"), 1, 5, 1, s.LearnMinNew, v => v == 1 ? L.T("1-mal geschrieben") : v + L.T("-mal geschrieben"), v => s.LearnMinNew = v);
            var learnInfo = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(4, 4, 4, 4), Text = _host.LearningSummary };
            _panel.Controls.Add(learnInfo);
            AddButton(L.T("Aus meinen Texten lernen (Ordner wählen)"), () =>
            {
                using (var d = new FolderBrowserDialog { Description = L.T("Ordner mit deinen Texten (z. B. Obsidian, Dokumente). Gelesen werden .txt .md .docx .html .eml .mbox, auch in Unterordnern.") })
                    if (d.ShowDialog(this) == DialogResult.OK) { _host.ImportTexts(new List<string> { d.SelectedPath }); learnInfo.Text = _host.LearningSummary; }
            });
            AddButton(L.T("Aus einzelnen Dateien lernen"), () =>
            {
                using (var d = new OpenFileDialog { Multiselect = true, Filter = L.T("Texte|*.txt;*.md;*.docx;*.html;*.htm;*.eml;*.mbox|Alle Dateien|*.*") })
                    if (d.ShowDialog(this) == DialogResult.OK) { _host.ImportTexts(d.FileNames.ToList()); learnInfo.Text = _host.LearningSummary; }
            });
            AddButton(L.T("Gelernte Wörter ansehen und löschen"), () => _host.OpenLearnedWords());

            Heading(L.T("Passwort-Tresor"));
            Note(L.T("Passwörter liegen verschlüsselt nur für dein Windows-Konto auf diesem PC. Sie erscheinen nie in den normalen Wortvorschlägen und werden direkt getippt, nie über die Zwischenablage."));
            Check(L.T("Tresor benutzen"), s.VaultEnabled, v => s.VaultEnabled = v);
            Check(L.T("In Passwortfeldern automatisch zeigen"), s.VaultAutoShow, v => s.VaultAutoShow = v);
            Check(L.T("Nach dem Eintippen \"Im Tresor speichern?\" fragen"), s.VaultOfferSave, v => s.VaultOfferSave = v);
            Slider(L.T("Länge neuer Passwörter"), 8, 32, 1, s.GeneratorLength, v => v + L.T(" Zeichen"), v => s.GeneratorLength = v);
            Check(L.T("Neue Passwörter mit Sonderzeichen"), s.GeneratorSymbols, v => s.GeneratorSymbols = v);
            var vaultInfo = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(4, 4, 4, 4), Text = _host.VaultSummary };
            _panel.Controls.Add(vaultInfo);
            AddButton(L.T("Tresor bearbeiten"), () => _host.OpenVaultEditor());
            Note(L.T("Mit PIN ist der Tresor nach dem Start gesperrt und wird nach einer Weile ohne Benutzung wieder gesperrt. Die PIN gibst du über große Zifferntasten ein."));
            AddButton(L.T("PIN festlegen oder ändern"), () => { string m = _host.ChangeVaultPin(true); vaultInfo.Text = _host.VaultSummary; if (m != null) MessageBox.Show(this, m, "PIN"); });
            AddButton(L.T("PIN entfernen"), () => { string m = _host.ChangeVaultPin(false); vaultInfo.Text = _host.VaultSummary; if (m != null) MessageBox.Show(this, m, "PIN"); });
            Slider(L.T("Mit PIN automatisch sperren nach"), 0, 240, 5, s.VaultAutoLockMinutes, v => v == 0 ? L.T("nie") : v + L.T(" Minuten"), v => s.VaultAutoLockMinutes = v);

            Heading(L.T("Sichern und auf anderen PC übertragen"));
            Note(L.T("Gelernte Wörter, Wortfolgen, Zahlen, eigene Begriffe, Makros und auf Wunsch die Passwörter in eine Datei packen. Auf dem anderen PC importieren, dann wird alles zusammengeführt."));
            AddButton(L.T("Daten exportieren …"), () => _host.ExportData());
            AddButton(L.T("Daten importieren …"), () => { _host.ImportData(); vaultInfo.Text = _host.VaultSummary; });

            Heading(L.T("Verschieben und Größe"));
            Note(L.T("Verschieben: Verschieben-Taste unten rechts ziehen, oder auf einer Taste lange draufbleiben. Größe: an jedem Rand oder an der Ecke unten links ziehen."));
            Slider(L.T("Breite"), 20, 100, 1, (int)Math.Round(s.WidthPercent), v => v + L.T(" % des Bildschirms"), v => s.WidthPercent = v);
            Slider(L.T("Höhe"), 15, 80, 1, (int)Math.Round(s.HeightPercent), v => v + L.T(" % des Bildschirms"), v => s.HeightPercent = v);
            var docks = new[] { L.T("unten rechts"), L.T("unten mittig"), L.T("unten links") };
            var dockKeys = new[] { "rechts", "mitte", "links" };
            Choice(L.T("Standardposition (für Zurücksetzen)"), docks, docks[Math.Max(0, Array.IndexOf(dockKeys, s.DockPosition))],
                v => s.DockPosition = dockKeys[Array.IndexOf(docks, v)]);
            AddButton(L.T("Größe und Position zurücksetzen"), () => { var h = ResetPositionRequested; if (h != null) h(); });
            Note(L.T("Die Größe gilt in Prozent des Bildschirms. Zieht man die Tastatur auf einen anderen Bildschirm, passt sie sich dort an."));
            Check(L.T("Verschieben durch langes Draufbleiben"), s.HoldToDrag, v => s.HoldToDrag = v);
            Check(L.T("Beim Kürzel (z. B. Pause) dort öffnen, wo die Maus ist"), s.OpenAtMouse, v => s.OpenAtMouse = v);
            var places = new[] { L.T("unter der Maus, mittig"), L.T("unter der Maus, nach rechts"), L.T("unter der Maus, nach links"),
                L.T("über der Maus, mittig"), L.T("über der Maus, nach rechts"), L.T("über der Maus, nach links") };
            Choice(L.T("Wo die Tastatur dann erscheint"), places, places[Math.Max(0, Array.IndexOf(Settings.MousePlaces, s.MousePlace))],
                v => s.MousePlace = Settings.MousePlaces[Array.IndexOf(places, v)]);
            Slider(L.T("Maustaste gedrückt halten bis Verschieben"), 300, 3000, 100, s.LongPressMs, v => (v / 1000.0).ToString("0.0") + " s", v => s.LongPressMs = v);
            Slider(L.T("Verweilen bis Verschieben"), 0, 6000, 250, s.DwellDragMs,
                v => v == 0 ? L.T("automatisch (doppelte Verweilzeit)") : (v / 1000.0).ToString("0.0#") + " s", v => s.DwellDragMs = v);
            Slider(L.T("Maus ruhig halten zum Absetzen"), 300, 3000, 100, s.DropMs, v => (v / 1000.0).ToString("0.0") + " s", v => s.DropMs = v);
            Slider(L.T("Greifzone am Rand zum Größe ändern"), 4, 40, 2, s.ResizeBorder, v => v + L.T(" Pixel"), v => s.ResizeBorder = v);

            Heading(L.T("Globale Kürzel"));
            Note(L.T("Funktionieren auch, wenn ein anderes Programm aktiv ist. Ideal für eine Quadstick-Taste."));
            AddQuadstickExample();
            AddHotkey(HotkeyIds.Show, L.T("Tastatur ein-/ausblenden"), () => s.HotkeyShow, v => s.HotkeyShow = v);
            AddHotkey(HotkeyIds.Macros, L.T("Makroleiste öffnen"), () => s.HotkeyMacros, v => s.HotkeyMacros = v);
            AddHotkey(HotkeyIds.Language, L.T("Sprache wechseln"), () => s.HotkeyLanguage, v => s.HotkeyLanguage = v);
            _hotkeyWarning = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(600, 0), Margin = new Padding(4, 6, 4, 6) };
            _panel.Controls.Add(_hotkeyWarning);
            AddButton(L.T("Kürzel testen"), () => _host.OpenKeyTest());
            AddButton(L.T("Protokoll öffnen"), () => { try { if (Log.FilePath != null && System.IO.File.Exists(Log.FilePath)) Process.Start("notepad.exe", "\"" + Log.FilePath + "\""); } catch { } });

            Heading(L.T("Makros"));
            AddButton(L.T("Makros bearbeiten"), () => _host.OpenMacroEditor());

            Heading(L.T("Start"));
            Check(L.T("Knopf in der Taskleiste zeigen (Klick öffnet die Tastatur ganz)"), s.TaskbarButton, v => s.TaskbarButton = v);
            Check(L.T("Mit Windows starten"), s.Autostart, v => { s.Autostart = v; Settings.ApplyAutostart(v); });
            Check(L.T("Eingeklappt starten"), s.StartCollapsed, v => s.StartCollapsed = v);

            Heading(L.T("Sonstiges"));
            var uiCodes = new List<string> { "auto" }; uiCodes.AddRange(L.Languages);
            var uiNames = uiCodes.Select(c => c == "auto" ? L.T("automatisch (wie Windows)") : L.NativeNames[c]).ToArray();
            _panel.Controls.Add(new Label { Text = L.T("Sprache der Oberfläche"), AutoSize = true, Margin = new Padding(4, 6, 4, 0) });
            var uiBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Margin = new Padding(4) };
            uiBox.Items.AddRange(uiNames);
            uiBox.SelectedIndex = Math.Max(0, uiCodes.IndexOf(s.UiLanguage));
            _panel.Controls.Add(uiBox);
            AddButton(L.T("Übernehmen"), () =>
            {
                string code = uiCodes[Math.Max(0, uiBox.SelectedIndex)];
                if (code == s.UiLanguage) return;
                s.UiLanguage = code;
                _host.ApplyUiLanguage();
            });
            AddButton(L.T("Einführung nochmal zeigen"), () => _host.ShowTour());

            Heading(L.T("Sperrbildschirm"));
            var lockInfo = new Label { AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(4, 0, 4, 6), Text = _host.LockScreenStatus };
            _panel.Controls.Add(lockInfo);
            var lockCb = new CheckBox { Text = L.T("Auf dem Sperrbildschirm anzeigen (zum Eingeben der PIN)"), AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(4, 6, 4, 6), Padding = new Padding(4),
                Checked = LockScreen.Enabled, Enabled = LockScreen.Ready };
            lockCb.CheckedChanged += (o, e) => { string m = _host.SetLockScreen(lockCb.Checked); lockInfo.Text = _host.LockScreenStatus; if (m != null) MessageBox.Show(this, m, L.T("Sperrbildschirm")); };
            _panel.Controls.Add(lockCb);
            AddButton(L.T("Datenordner öffnen"), () => { try { Process.Start("explorer.exe", "\"" + Settings.DataDir + "\""); } catch { } });
            AddButton(L.T("Schließen"), Close);
            AddButton(L.T("Tastatur beenden"), () => { var h = ExitRequested; if (h != null) h(); });
            // Platz am Ende, damit der letzte Knopf beim Scrollen ganz sichtbar ist
            _panel.Controls.Add(new Label { Height = 60, Width = 10, Margin = new Padding(0) });

            ShowHotkeyStatus(_host.CurrentHotkeyStatus);
        }

        private void Changed()
        {
            _s.Save();
            var h = SettingsChanged; if (h != null) h();
        }

        private void ShowHotkeyStatus(List<HotkeyStatus> status)
        {
            foreach (var r in _hotkeyRefresh) r(status);
            var other = new List<string>();
            foreach (var st in status)
                if (!st.Ok && st.Id >= HotkeyIds.MacroBase) other.Add(st.Name + " (" + st.Combo + "): " + st.Reason);
            _hotkeyWarning.Text = other.Count == 0 ? "" : L.T("Makro-Kürzel nicht aktiv: ") + string.Join("; ", other);
            _hotkeyWarning.Visible = other.Count > 0;
        }

        private void Heading(string text)
        {
            _panel.Controls.Add(new Label
            {
                Text = text, AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                Margin = new Padding(0, 14, 0, 6)
            });
        }

        private void Note(string text)
        {
            _panel.Controls.Add(new Label
            {
                Text = text, AutoSize = true, ForeColor = SystemColors.GrayText,
                MaximumSize = new Size(600, 0), Margin = new Padding(4, 0, 4, 6)
            });
        }

        private void Check(string text, bool value, Action<bool> set)
        {
            var cb = new CheckBox
            {
                Text = text, Checked = value, AutoSize = true, MaximumSize = new Size(600, 0),
                Margin = new Padding(4, 6, 4, 6), Padding = new Padding(4)
            };
            cb.CheckedChanged += (o, e) => { set(cb.Checked); Changed(); };
            _panel.Controls.Add(cb);
            // Lange Texte (andere Sprachen) umbrechen statt abschneiden
            int textW = cb.MaximumSize.Width - 30;
            var one = TextRenderer.MeasureText(text, cb.Font);
            if (one.Width > textW)
            {
                var sz = TextRenderer.MeasureText(text, cb.Font, new Size(textW, 0), TextFormatFlags.WordBreak);
                cb.AutoSize = false;
                cb.CheckAlign = ContentAlignment.TopLeft;
                cb.TextAlign = ContentAlignment.TopLeft;
                cb.Size = new Size(cb.MaximumSize.Width, sz.Height + cb.Padding.Vertical + 4);
            }
        }

        private void Slider(string text, int min, int max, int step, int value, Func<int, string> fmt, Action<int> set)
        {
            var label = new Label { AutoSize = true, Margin = new Padding(4, 6, 4, 0) };
            Action<int> upd = v => label.Text = text + ": " + fmt(v);
            var tb = new TrackBar
            {
                Minimum = min, Maximum = max, SmallChange = step, LargeChange = step,
                TickFrequency = step * Math.Max(1, (max - min) / step / 10),
                Value = Math.Max(min, Math.Min(max, value)), Width = 400, Height = 50
            };
            upd(tb.Value);
            tb.ValueChanged += (o, e) =>
            {
                int v = (int)Math.Round((double)tb.Value / step) * step;
                v = Math.Max(min, Math.Min(max, v));
                upd(v); set(v); Changed();
            };

            // große Plus/Minus-Knöpfe, damit man den Regler nicht ziehen muss
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            var minus = new Button { Text = "−", Width = 56, Height = 44 };
            var plus = new Button { Text = "+", Width = 56, Height = 44 };
            minus.Click += (o, e) => tb.Value = Math.Max(min, tb.Value - step);
            plus.Click += (o, e) => tb.Value = Math.Min(max, tb.Value + step);
            row.Controls.Add(minus); row.Controls.Add(tb); row.Controls.Add(plus);

            _panel.Controls.Add(label);
            _panel.Controls.Add(row);
        }

        private void Choice(string text, string[] options, string current, Action<string> set, string[] labels = null)
        {
            _panel.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(4, 6, 4, 0) });
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0), WrapContents = true, MaximumSize = new Size(640, 0) };
            foreach (var opt in options)
            {
                var o = opt;
                int idx = Array.IndexOf(options, opt);
                var rb = new RadioButton { Text = labels != null && idx < labels.Length ? labels[idx] : o, Checked = o == current, AutoSize = true, Margin = new Padding(6), Padding = new Padding(4) };
                rb.CheckedChanged += (s, e) => { if (rb.Checked) { set(o); Changed(); } };
                row.Controls.Add(rb);
            }
            _panel.Controls.Add(row);
        }

        private CheckedListBox _langList;
        private bool _langUpdating;

        private void AddLanguages()
        {
            _langList = new CheckedListBox { Width = 600, Height = 170, CheckOnClick = true, IntegralHeight = false, Margin = new Padding(4) };
            _langList.ItemCheck += (o, e) =>
            {
                if (_langUpdating) return;
                // ItemCheck kommt vor der Änderung, deshalb danach auswerten
                BeginInvoke((Action)SaveLanguageSelection);
            };
            _panel.Controls.Add(_langList);

            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Width = 620, Margin = new Padding(0) };
            Func<string, Action, Button> btn = (t, a) =>
            {
                var b = new Button { Text = t, AutoSize = true, MinimumSize = new Size(90, 44), Margin = new Padding(4) };
                b.Click += (o, e) => a();
                return b;
            };
            row.Controls.Add(btn("▲", () => MoveLanguage(-1)));
            row.Controls.Add(btn("▼", () => MoveLanguage(1)));
            row.Controls.Add(btn(L.T("Entfernen"), DeleteLanguage));
            row.Controls.Add(btn(L.T("Aus Datei hinzufügen"), ImportLanguage));
            _panel.Controls.Add(row);
            FillLanguages();

        }

        private void FillLanguages()
        {
            _langUpdating = true;
            var active = _s.ActiveLanguages.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var all = _host.Languages.Installed();
            var ordered = active.Select(c => all.FirstOrDefault(l => l.Code == c)).Where(l => l != null)
                .Concat(all.Where(l => !active.Contains(l.Code))).ToList();
            _langList.Items.Clear();
            foreach (var l in ordered) _langList.Items.Add(l, active.Contains(l.Code));
            _langUpdating = false;
        }

        private void SaveLanguageSelection()
        {
            var codes = new List<string>();
            for (int i = 0; i < _langList.Items.Count; i++)
                if (_langList.GetItemChecked(i)) codes.Add(((LanguageInfo)_langList.Items[i]).Code);
            if (codes.Count == 0)
            {
                MessageBox.Show(this, L.T("Mindestens eine Sprache muss aktiv sein."), L.T("Sprachen"));
                FillLanguages();
                return;
            }
            _s.ActiveLanguages = string.Join(",", codes);
            _host.LanguagesChanged();
        }

        private void MoveLanguage(int d)
        {
            int i = _langList.SelectedIndex, j = i + d;
            if (i < 0 || j < 0 || j >= _langList.Items.Count) return;
            _langUpdating = true;
            var item = _langList.Items[i];
            bool chk = _langList.GetItemChecked(i);
            _langList.Items.RemoveAt(i);
            _langList.Items.Insert(j, item);
            _langList.SetItemChecked(j, chk);
            _langList.SelectedIndex = j;
            _langUpdating = false;
            SaveLanguageSelection();
        }

        private void DeleteLanguage()
        {
            var l = _langList.SelectedItem as LanguageInfo;
            if (l == null) return;
            if (l.BuiltIn) { MessageBox.Show(this, L.T(l.Name) + L.T(" ist fest eingebaut und kann nur abgeschaltet werden (Haken weg)."), L.T("Sprachen")); return; }
            if (MessageBox.Show(this, L.T(l.Name) + L.T(" löschen?"), L.T("Sprachen"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            _s.ActiveLanguages = string.Join(",", _s.ActiveLanguages.Split(',').Where(c => c.Trim() != l.Code));
            _host.Languages.Delete(l.Code);
            _host.LanguagesChanged();
            FillLanguages();
        }

        private void ImportLanguage()
        {
            using (var d = new OpenFileDialog { Title = L.T("Wortliste auswählen (Wort, Tabulator, Häufigkeit)"), Filter = L.T("Wortliste (*.txt)|*.txt|Alle Dateien|*.*") })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var data = System.IO.File.ReadAllBytes(d.FileName);
                    LanguageInfo info;
                    string err = _host.Languages.Install(data, System.IO.Path.GetFileNameWithoutExtension(d.FileName), out info);
                    if (err != null) { MessageBox.Show(this, err, L.T("Sprachpaket")); return; }
                    _host.ActivateLanguage(info.Code);
                    FillLanguages();
                    MessageBox.Show(this, L.T(info.Name) + L.T(" wurde hinzugefügt und eingeschaltet."), L.T("Sprachpaket"));
                }
                catch (Exception ex) { MessageBox.Show(this, L.T("Datei nicht lesbar: ") + ex.Message, L.T("Sprachpaket")); }
            }
        }

        /// <summary>Beispielbild: so sieht die Belegung im Quadstick-Blatt aus.</summary>
        private void AddQuadstickExample()
        {
            try
            {
                using (var st = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("quadstick_beispiel.png"))
                {
                    if (st == null) return;
                    Image img;
                    using (var tmp = Image.FromStream(st)) img = new Bitmap(tmp);   // Kopie, damit der Stream zu sein darf
                    int w = Math.Min(img.Width, 600);
                    var pic = new PictureBox
                    {
                        Image = img, SizeMode = PictureBoxSizeMode.Zoom,
                        Width = w, Height = (int)Math.Round(img.Height * (double)w / img.Width),
                        Margin = new Padding(4, 8, 4, 2), BorderStyle = BorderStyle.FixedSingle
                    };
                    _panel.Controls.Add(pic);
                }
                Note(L.T("So sieht die Belegung im Quadstick-Blatt aus. In der gelben Spalte den gewünschten Eingang wählen (z. B. mp_triple_puff), dann die Konfiguration auf den Quadstick laden."));
            }
            catch { }
        }

        private void AddButton(string text, Action click)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 48, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(4, 6, 4, 6), MinimumSize = new Size(320, 48) };
            b.Click += (o, e) => click();
            _panel.Controls.Add(b);
        }

        private void AddHotkey(int id, string text, Func<string> get, Action<string> set)
        {
            var label = new Label { AutoSize = true, Margin = new Padding(4, 10, 4, 0), Font = new Font("Segoe UI", 12f, FontStyle.Bold) };
            var state = new Label { AutoSize = true, Margin = new Padding(4, 2, 4, 2), MaximumSize = new Size(600, 0) };
            Action<List<HotkeyStatus>> refresh = status =>
            {
                string combo = get();
                label.Text = text + ":  " + (string.IsNullOrEmpty(combo) ? L.T("aus") : combo);
                if (string.IsNullOrEmpty(combo)) { state.Text = L.T("ausgeschaltet"); state.ForeColor = SystemColors.GrayText; return; }
                var st = status == null ? null : status.Find(x => x.Id == id);
                if (st != null && st.Ok) { state.Text = L.T("✓ aktiv"); state.ForeColor = Color.ForestGreen; }
                else { state.Text = L.T("✗ nicht aktiv") + (st != null && st.Reason != null ? ": " + st.Reason : ""); state.ForeColor = Color.Firebrick; }
            };
            _hotkeyRefresh.Add(refresh);

            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            var rec = new Button { Text = L.T("Aufnehmen"), AutoSize = true, MinimumSize = new Size(150, 44) };
            var pick = new Button { Text = L.T("Aus Liste ▾"), AutoSize = true, MinimumSize = new Size(150, 44) };
            var off = new Button { Text = L.T("Aus"), AutoSize = true, MinimumSize = new Size(90, 44) };

            Action<string> apply = v =>
            {
                set(v ?? "");
                Changed();
                ShowHotkeyStatus(_host.ApplyHotkeys());
            };

            rec.Click += (o, e) =>
            {
                string r = CaptureKey(this, _host, text);
                if (r != null) apply(r); else ShowHotkeyStatus(_host.CurrentHotkeyStatus);
            };
            pick.Click += (o, e) =>
            {
                var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 13f) };
                for (int i = 13; i <= 24; i++)
                {
                    string k = "F" + i;
                    menu.Items.Add(k, null, (s2, e2) => apply(k));
                }
                foreach (var k in new[] { "Pause", "NumGeteilt", "NumMal", "NumMinus", "NumPlus", "Strg+Alt+Shift+E", "Strg+Alt+Shift+M", "Strg+Alt+Shift+T", "Rollen" })
                {
                    string kk = k;
                    menu.Items.Add(kk, null, (s2, e2) => apply(kk));
                }
                menu.Show(pick, new Point(0, pick.Height));
            };
            off.Click += (o, e) => apply("");

            row.Controls.Add(rec); row.Controls.Add(pick); row.Controls.Add(off);
            _panel.Controls.Add(label);
            _panel.Controls.Add(state);
            _panel.Controls.Add(row);
        }

        /// <summary>Kürzel aufnehmen, während die anderen Kürzel kurz ausgeschaltet sind.</summary>
        public static string CaptureKey(IWin32Window owner, IAppHost host, string what)
        {
            host.SuspendHotkeys();
            try
            {
                using (var d = new KeyCaptureDialog(what))
                    return d.ShowDialog(owner) == DialogResult.OK ? d.Result : null;
            }
            finally { host.ApplyHotkeys(); }
        }
    }
}
