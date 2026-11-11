using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Tastatur
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (LockScreen.IsLockMode(args))
            {
                // Sperrbildschirm: eigene, abgespeckte Tastatur (eigener Desktop, eigenes Mutex)
                DpiInfo.Enable();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Settings.InitDataDir();
                Application.Run(new LockScreenContext());
                return;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\Bildschirmtastatur_Morten", out created))
            {
                if (!created) return; // läuft schon

                DpiInfo.Enable();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new AppContext());
            }
        }
    }

    internal class AppContext : ApplicationContext, IAppHost
    {

        private readonly Settings _settings;
        private readonly KeyboardEngine _engine;
        private readonly RecordingOutput _recorder;
        private readonly MacroRunner _runner;
        private readonly KeyboardForm _keyboard;
        private readonly MiniForm _mini;
        private readonly FocusWatcher _watcher = new FocusWatcher();
        private readonly HotkeyManager _hotkeys = new HotkeyManager();
        private readonly NotifyIcon _tray;
        private SettingsForm _settingsForm;
        private MacroEditorForm _editor;
        private KeyTestForm _keyTest;

        private readonly LanguageManager _langs;
        private readonly LanguageSet _langSet = new LanguageSet();
        private string _lastProgram;

        private List<Macro> _macros = new List<Macro>();
        private string _macroError;

        // Lernen
        private bool _learnPaused;
        private bool _numbersLoaded;

        // Wort pro Fenster und echte Tastendrücke
        private readonly WindowStates _states = new WindowStates();
        private IntPtr _activeWin, _clickSwitchTo;
        private DateTime _clickSwitchAt;
        private readonly PhysicalKeys _physical = new PhysicalKeys();
        private KeyboardHook _physHook;
        private HashSet<ushort> _hotkeyVks = new HashSet<ushort>();
        private LearnedWordsForm _learnedForm;
        private MenuItem _pauseItem;

        // Tresor
        private readonly Vault _vault;
        private string _vaultError;
        private readonly PasswordWatcher _pwWatcher = new PasswordWatcher();
        private bool _vaultShown;
        private string _offer;                 // gerade getipptes Passwort, das gespeichert werden könnte
        private string _offerTitle, _offerProcess;
        private DateTime _offerUntil;
        private VaultEditorForm _vaultEditor;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 15000 };
        private DateTime _lastLearnSave = DateTime.Now;

        public AppContext()
        {
            Settings.InitDataDir();
            _settings = Settings.Load();
            L.Init(_settings.UiLanguage);
            Log.Write("Start, Version " + AppInfo.Version + ", Daten in " + Settings.DataDir);

            _langs = new LanguageManager(Settings.DataDir);
            LoadLanguages();

            _vault = new Vault(System.IO.Path.Combine(Settings.DataDir, "tresor.dat"));
            try { _vault.Load(); }
            catch (Exception e) { _vaultError = "Tresor konnte nicht gelesen werden: " + e.Message; Log.Write(_vaultError); }

            _recorder = new RecordingOutput(new SystemOutput());
            _engine = new KeyboardEngine(_recorder, _langSet, _settings);
            _runner = new MacroRunner(new SystemOutput(), MacroRunner.PhysicalModifiersHeld);
            _runner.OnError = msg => { _keyboard.ShowStatus(msg, 5000); };

            KeyLayouts.BottomChars = _settings.BottomChars;
            DpiInfo.KeyboardBounds = () => _keyboard != null && !_keyboard.IsDisposed && _keyboard.Visible ? _keyboard.Bounds : Rectangle.Empty;
            _keyboard = new KeyboardForm(_settings, _engine);
            _keyboard.MacroSource = () => _macros;
            _keyboard.MacroErrorSource = () => _macroError;
            _keyboard.LanguageLabel = () => _langSet.Label;
            _keyboard.LayoutCodeSource = LayoutCodeFor;
            _keyboard.SetLayout(LayoutCodeFor(), true);
            _keyboard.LanguageCycleRequested += CycleLanguage;
            _mini = new MiniForm(_settings);

            _keyboard.SettingsRequested += ShowSettings;
            _keyboard.CollapseRequested += Collapse;
            _keyboard.MacroChosen += RunMacro;
            _keyboard.RecordStartRequested += StartRecording;
            _keyboard.RecordStopRequested += StopRecording;
            _keyboard.RecordCancelRequested += CancelRecording;
            _keyboard.QuickSaveRequested += QuickSave;
            _keyboard.EditMacrosRequested += OpenMacroEditor;
            _mini.ExpandRequested += Expand;

            // Lernen
            _engine.WordFinished += (w, prev, start) => { if (LearnActive) _langSet.Learn(w, prev, start); };
            _engine.WordReopened += (w, prev) => { if (LearnActive) _langSet.Unlearn(w, prev); };
            _engine.CaseChanged += (oldW, newW) => { if (LearnActive) _langSet.SetForm(newW); };
            _engine.NumberFinished += (n, w) => { if (LearnActive) _langSet.LearnNumber(n, w); };
            _engine.EmailFinished += e =>
            {
                if (LearnActive) _langSet.LearnEmail(e);
                if (!_ownWindowActive) { _lastEmail = e; _lastEmailTitle = _pwWatcher.WindowTitle; _lastEmailAt = DateTime.Now; }   // für "+ Passwort hinzufügen"
            };
            _engine.IbanFinished += iban => { if (LearnActive) _langSet.LearnIban(iban); };
            _engine.NumberReopened += (n, w) => { if (LearnActive) _langSet.UnlearnNumber(n, w); };

            // Tresor
            _keyboard.OverrideBand = VaultBand;
            _keyboard.VaultShown = () => _vaultShown;
            _keyboard.VaultToggleRequested += ToggleVault;
            _engine.SecretSubmitted += () => { var sec = _engine.TakeSecret(); _keyboard.HideReveal(); _keyboard.VaultTypedLength = 0; if (sec != null) Offer(sec); };
            _keyboard.KnownWord = w => _langSet.Knows(w);
            _keyboard.EmailSuggest = chunk => _langSet.SuggestEmails(chunk, 2);
            _keyboard.EmailFieldSuggest = chunk => _langSet.SuggestEmailsInField(chunk, 4);
            _keyboard.IbanSuggest = (part, max) => _langSet.SuggestIbans(part, max);
            _pwWatcher.IbanFieldChanged += iban => _keyboard.BeginInvoke((Action)(() =>
            {
                _keyboard.IbanFieldMode = iban && _settings.LearnIban;
                _ibanField = iban; _engine.NoPunctuationSpace = _ibanField || _mailField;
                _keyboard.UpdateBand();
            }));
            _pwWatcher.EmailFieldChanged += mail => _keyboard.BeginInvoke((Action)(() =>
            {
                _keyboard.EmailFieldMode = mail && _settings.EmailFieldSuggest;
                _mailField = mail; _engine.NoPunctuationSpace = _ibanField || _mailField;
                _keyboard.UpdateBand();
            }));
            // Passwort im eigenen Tresor-Fenster: sichtbar tippen, aber nichts lernen und nichts anbieten
            SecretField.TextChanged += () => _keyboard.UpdateBand();
            SecretField.Focus += (focused, plain) => _keyboard.BeginInvoke((Action)(() =>
            {
                _ownSecret = focused;
                if (focused)
                {
                    _engine.PasswordMode = true;
                    _recorder.Paused = true;
                    _keyboard.PlainSecret = plain;
                }
                else
                {
                    _engine.TakeSecret();   // verwerfen, wird ja im Fenster gespeichert
                    _engine.PasswordMode = false;
                    _recorder.Paused = false;
                    _keyboard.PlainSecret = false;
                    _keyboard.HideReveal();
                }
                _keyboard.UpdateBand();
            }));
            _keyboard.TypedAccepted += (w, unknown) => { if (unknown && LearnActive) _langSet.LearnUntilSuggested(w); };
            _keyboard.CloseRequested += () => Exit();
            _pwWatcher.Changed += isPw => _keyboard.BeginInvoke((Action)(() => OnPasswordField(isPw)));
            _timer.Tick += (o, e) => OnTimer();
            _timer.Start();

            // Fensterwechsel: Wort des alten Fensters merken, das des neuen laden.
            // Klick im aktiven Fenster: Cursor ist woanders, neues Wort beginnt.
            try { _activeWin = FocusWatcher.Root(Native.GetForegroundWindow()); } catch { }
            _watcher.ForegroundChanged += root => _keyboard.BeginInvoke((Action)(() =>
            {
                if (Native.IsShellWindow(root)) return;   // Klick in die Taskleiste ist kein Fensterwechsel
                OnOwnWindow(IsOwnProcess(root));
                _lastAppWin = root;
                OnWindowSwitch(root);
            }));
            _watcher.ClickOutside += root => _keyboard.BeginInvoke((Action)(() => OnClickOutside(root)));

            // echte Tastendrücke (Quadstick): rot anzeigen, angefangenes Wort vergessen
            _keyboard.Physical = _physical;
            try
            {
                _physHook = new KeyboardHook();
                _physHook.Observe = (vk, down, own, scan) => { if (!own) OnPhysicalKey(vk, down, scan); };
                if (!_physHook.Active) Log.Write("Echte Tastendrücke können nicht angezeigt werden (Hook nicht aktiv)");
            }
            catch (Exception e) { Log.Write("Echte Tastendrücke: " + e.Message); }

            _tray = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = AppInfo.Name + " " + AppInfo.Version,
                Visible = true,
                ContextMenu = BuildTrayMenu()
            };
            _tray.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) Toggle(); };

            // Knopf in der Taskleiste: Klick blendet ein/aus, der Fokus geht gleich zurück ans Programm
            _taskbar = new TaskbarButton(LoadIcon());
            _taskbar.Clicked += OpenFull;
            _taskbar.ReturnFocusTo = () => _lastAppWin;
            _taskbar.CreateControl();
            _watcher.AddOwnWindow(_taskbar.Handle);
            if (_settings.TaskbarButton) _taskbar.ShowButton();

            if (_settings.StartCollapsed && _settings.TourSeen) _mini.Show(); else _keyboard.Show();
            if (!_settings.TourSeen)
            {
                // Einführung beim ersten Start, kurz nachdem die Tastatur da ist
                var tt = new System.Windows.Forms.Timer { Interval = 900 };
                tt.Tick += (o, e) => { tt.Stop(); tt.Dispose(); ShowTour(); };
                tt.Start();
            }
            _watcher.AddOwnWindow(_keyboard.Handle);
            _watcher.AddOwnWindow(_mini.Handle);
            _watcher.Start();
            try { _pwWatcher.Start(); } catch (Exception e) { Log.Write("Passwortfeld-Erkennung nicht verfügbar: " + e.Message); }

            if (_settings.Autostart) Settings.ApplyAutostart(true); // Pfad aktuell halten, falls die .exe verschoben wurde

            LoadMacros();
            OnProgramChanged();
            var failed = Failed(ApplyHotkeys());
            if (_vaultError != null)
                _tray.ShowBalloonTip(8000, L.T("Passwort-Tresor"), _vaultError, ToolTipIcon.Warning);
            else if (_macroError != null)
                _tray.ShowBalloonTip(8000, L.T("Makros"), _macroError, ToolTipIcon.Warning);
            else if (failed.Count > 0)
                _tray.ShowBalloonTip(8000, L.T("Kürzel nicht aktiv"), string.Join("\n", failed), ToolTipIcon.Warning);
        }

        private static Icon LoadIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
            catch { return SystemIcons.Application; }
        }

        // ---------- Fenster ----------

        private void Collapse()
        {
            _keyboard.Hide();
            _mini.Show();
        }

        private void Expand()
        {
            _mini.Hide();
            _keyboard.Show();
            _keyboard.ReassertTopmost();
        }

        private TaskbarButton _taskbar;
        private IntPtr _lastAppWin;   // zuletzt benutztes fremdes Fenster (dahin geht der Fokus nach Taskleisten-Klick)

        private void ApplyTaskbarButton()
        {
            if (_taskbar == null) return;
            if (_settings.TaskbarButton) _taskbar.ShowButton(); else _taskbar.HideButton();
        }

        /// <summary>Taskleisten-Knopf: Tastatur immer ganz öffnen (aus ausgeblendet oder eingeklappt), nie schließen.</summary>
        private void OpenFull()
        {
            if (!_keyboard.Visible) Expand();
            _keyboard.ReassertTopmost();
        }

        private void Toggle()
        {
            if (_keyboard.Visible) Collapse(); else Expand();
        }

        /// <summary>Kürzel (z. B. Pause): beim Öffnen dort, wo die Maus ist (abschaltbar).</summary>
        private void HotkeyToggle()
        {
            if (_keyboard.Visible) { Collapse(); return; }
            if (_settings.OpenAtMouse) _keyboard.MoveToMouse();
            Expand();
        }

        private void HotkeyMacros()
        {
            if (!_keyboard.Visible) Expand();
            if (_keyboard.MacroMode) _keyboard.CloseMacroBar(); else _keyboard.OpenMacroBar();
        }

        private ContextMenu BuildTrayMenu()
        {
            return new ContextMenu(new[]
            {
                new MenuItem(L.T("Tastatur zeigen / verstecken"), (o, e) => Toggle()),
                new MenuItem(L.T("Makros bearbeiten"), (o, e) => OpenMacroEditor()),
                (_pauseItem = new MenuItem(_learnPaused ? L.T("Lernen fortsetzen") : L.T("Lernen pausieren"), (o, e) => ToggleLearnPause())),
                new MenuItem(L.T("Passwort-Tresor bearbeiten"), (o, e) => OpenVaultEditor()),
                new MenuItem(L.T("Einstellungen"), (o, e) => ShowSettings()),
                new MenuItem(L.T("Anleitung öffnen"), (o, e) => OpenManual()),
                new MenuItem(L.T("Einführung zeigen"), (o, e) => ShowTour()),
                new MenuItem(L.T("☕ Kaffee spendieren"), (o, e) => Settings.OpenLink(_settings.DonateLink)),
                new MenuItem("-"),
                new MenuItem(L.T("Beenden"), (o, e) => Exit())
            });
        }

        /// <summary>Sprache der Oberfläche gewechselt: Texte neu laden, Tastatur und Menüs neu aufbauen.</summary>
        public void ApplyUiLanguage()
        {
            L.Init(_settings.UiLanguage);
            _settings.Save();
            _tray.ContextMenu = BuildTrayMenu();
            _keyboard.SetLayout(_keyboard.LayoutCode == "en" ? "en" : LayoutCodeFor(), true);
            _keyboard.ApplySettings();
            _keyboard.UpdateBand();
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                var old = _settingsForm;
                _settingsForm = null;
                old.BeginInvoke((Action)(() => { old.Close(); ShowSettings(); }));
            }
            Log.Write("Sprache der Oberfläche: " + L.Lang);
        }

        private TourForm _tour;

        /// <summary>Anleitung in der Sprache der Oberfläche öffnen (sonst Englisch). Liegt sie nicht neben
        /// der exe, wird die eingebaute Fassung nach NoHandsNeeded-Daten\Anleitung geschrieben.</summary>
        public void OpenManual()
        {
            try
            {
                string path = Manual.Find(L.Lang, System.IO.Path.GetDirectoryName(Application.ExecutablePath), Settings.DataDir);
                if (path == null) { MessageBox.Show(L.T("Die Anleitung wurde nicht gefunden."), L.T("Anleitung")); return; }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception e) { Log.Write("Anleitung: " + e.Message); MessageBox.Show(L.T("Die Anleitung ließ sich nicht öffnen: ") + e.Message, L.T("Anleitung")); }
        }

        public void ShowTour()
        {
            if (_tour != null && !_tour.IsDisposed) return;
            Expand();
            string sys = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            var builtIn = LanguageManager.BuiltIns.FirstOrDefault(b => b.Code == sys);
            string info, extraLabel = null;
            Action extra = null;
            if (builtIn != null)
            {
                bool active = _langSet.Active.Any(p => p.Code == sys);
                info = active ? L.F("Deine Windows-Sprache ist {0}, die Tastatur ist auf {0} gestellt.", L.T(builtIn.Name))
                              : L.F("Deine Windows-Sprache ist {0}. Du kannst sie in den Einstellungen unter Sprachen einschalten.", L.T(builtIn.Name));
            }
            else
            {
                string native = sys;
                try { native = System.Globalization.CultureInfo.GetCultureInfo(sys).NativeName; } catch { }
                info = L.F("Deine Windows-Sprache ({0}) ist leider noch nicht eingebaut.", native);
            }
            _tour = new TourForm(TourSteps.Create(info, extraLabel, extra), id => _keyboard.TourTargetRect(id), _settings);
            _tour.Finished += done =>
            {
                _keyboard.TourHighlight = null;
                _keyboard.Invalidate();
                _settings.TourSeen = true;
                _settings.Save();
                Log.Write(done ? "Einführung bis zum Ende angesehen" : "Einführung übersprungen");
            };
            _tour.Start();
        }

        // Sperrbildschirm: Platzhalter, siehe LockScreen.cs
        public string LockScreenStatus { get { return LockScreen.Status(); } }
        public string SetLockScreen(bool on) { return LockScreen.SetEnabled(on); }

        private void ShowSettings()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed) { _settingsForm.Activate(); return; }
            _settingsForm = new SettingsForm(_settings, this);
            _settingsForm.SettingsChanged += () =>
            {
                _langSet.MinNewCount = _settings.LearnMinNew;
                if (!_settings.VaultEnabled) { _vaultShown = false; _offer = null; }
                _langSet.NumbersEnabled = _settings.LearnNumbers; _langSet.EmailsEnabled = _settings.LearnEmails; _langSet.IbansEnabled = _settings.LearnIban; NumberRules.AllowSecret = _settings.LearnSecretNumbers;
                _keyboard.ApplySettings(); _mini.ApplySettings(); _keyboard.ApplySizeFromSettings(); _keyboard.UpdateBand();
                ApplyTaskbarButton();
            };
            _settingsForm.ResetPositionRequested += () => { _keyboard.ResetBounds(); Expand(); };
            _settingsForm.ExitRequested += Exit;
            _settingsForm.Show();
        }

        // ---------- Kürzel ----------

        // ---------- Sprachen ----------

        public LanguageManager Languages { get { return _langs; } }
        public string LanguageSource { get { return _settings.LanguageSource; } }

        private void LoadLanguages()
        {
            var codes = _settings.ActiveLanguages.Split(',').Select(c => c.Trim().ToLowerInvariant())
                .Where(c => c.Length > 0).Distinct().ToList();
            var preds = new List<Predictor>();
            foreach (var c in codes)
            {
                var p = _langs.Get(c);
                if (p != null) preds.Add(p); else Log.Write("Sprache " + c + " ist nicht installiert");
            }
            if (preds.Count == 0) { var de = _langs.Get("de"); if (de != null) preds.Add(de); }
            foreach (var l in _langs.Installed()) if (!preds.Any(p => p.Code == l.Code)) _langs.Unload(l.Code);
            _settings.ActiveLanguages = string.Join(",", preds.Select(p => p.Code));
            _langSet.OfferMix = _settings.OfferMix;
            _langSet.SetActive(preds, _langs.LoadCustomWords());
            _langSet.SetMode(_settings.LanguageMode);
            _settings.LanguageMode = _langSet.Mode;
            _langSet.MinNewCount = _settings.LearnMinNew;
            _langSet.NumbersEnabled = _settings.LearnNumbers; _langSet.EmailsEnabled = _settings.LearnEmails; _langSet.IbansEnabled = _settings.LearnIban; NumberRules.AllowSecret = _settings.LearnSecretNumbers;
            if (!_numbersLoaded)
            {
                try { _langSet.Emails = EmailStore.Load(EmailsPath); }
                catch (Exception e) { Log.Write("Gelernte E-Mail-Adressen nicht lesbar: " + e.Message); }
                try { _langSet.Numbers = NumberStore.Load(NumbersPath); }
                catch (Exception e) { Log.Write("Gelernte Zahlen nicht lesbar: " + e.Message); }
                _numbersLoaded = true;
            }
            foreach (var p in preds)
            {
                if (_langSet.Store(p.Code) != null) continue;
                LearningStore st;
                try { st = LearningStore.Load(LearnPath(p.Code), p.Code); }
                catch (Exception e) { Log.Write("Gelerntes für " + p.Code + " nicht lesbar: " + e.Message); st = new LearningStore { Code = p.Code }; }
                _langSet.SetStore(p.Code, st);
            }
        }

        /// <summary>Nach Änderungen in den Einstellungen (aktive Sprachen, Reihenfolge, Download).</summary>
        public void LanguagesChanged()
        {
            LoadLanguages();
            _settings.Save();
            _keyboard.ApplySettings();
        }

        public void ActivateLanguage(string code)
        {
            var codes = _settings.ActiveLanguages.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
            if (!codes.Contains(code)) codes.Add(code);
            _settings.ActiveLanguages = string.Join(",", codes);
            LanguagesChanged();
        }

        /// <summary>Tastenlayout: passend zur (ersten) Sprache oder fest eingestellt. Ohne eigenes Layout: Englisch.</summary>
        private string LayoutCodeFor()
        {
            if (_settings.KeyLayout == "de" || _settings.KeyLayout == "en") return _settings.KeyLayout;
            string code = _langSet.Mode == LanguageSet.MixKey ? (_langSet.Active.Count > 0 ? _langSet.Active[0].Code : "de") : _langSet.Mode;
            return KeyLayouts.Has(code) ? code : "en";
        }

        private void CycleLanguage()
        {
            string mode = _langSet.Next();
            _settings.LanguageMode = mode;
            if (_settings.PerProgramLanguage && _lastProgram != null) _settings.SetProgramLanguage(_lastProgram, mode);
            _settings.Save();
            _keyboard.ApplySettings();
            _keyboard.ShowStatus(L.T("Sprache: ") + _langSet.LongLabel + (_settings.PerProgramLanguage && _lastProgram != null ? L.T("  (für ") + _lastProgram + ")" : ""), 1500);
        }

        private void OnProgramChanged()
        {
            string prog = KeyboardForm.ForegroundProcessName();
            if (prog == null) return;
            _lastProgram = prog;
            if (!_settings.PerProgramLanguage) return;
            string mode;
            if (_settings.ProgramLanguageMap().TryGetValue(prog, out mode) && mode != _langSet.Mode)
            {
                _langSet.SetMode(mode);
                _settings.LanguageMode = _langSet.Mode;
                _keyboard.ApplySettings();
            }
        }

        public void SuspendHotkeys() { _hotkeys.Suspend(); }

        public List<HotkeyStatus> CurrentHotkeyStatus { get { return _hotkeys.Status; } }

        private static List<string> Failed(List<HotkeyStatus> status)
        {
            return status.Where(x => !x.Ok).Select(x => x.Combo + " (" + x.Name + "): " + x.Reason).ToList();
        }

        public List<HotkeyStatus> ApplyHotkeys()
        {
            _hotkeys.ClearWanted();
            _hotkeys.Add(HotkeyIds.Show, L.T("Tastatur ein-/ausblenden"), _settings.HotkeyShow, HotkeyToggle);
            _hotkeys.Add(HotkeyIds.Macros, L.T("Makroleiste öffnen"), _settings.HotkeyMacros, HotkeyMacros);
            _hotkeys.Add(HotkeyIds.Language, L.T("Sprache wechseln"), _settings.HotkeyLanguage, () => { if (!_keyboard.Visible) Expand(); CycleLanguage(); });
            for (int i = 0; i < _macros.Count; i++)
            {
                var m = _macros[i];
                if (m.Hotkey.Length > 0) _hotkeys.Add(HotkeyIds.MacroBase + i, L.T("Makro ") + m.Name, m.Hotkey, () => RunMacro(m));
            }
            // Tasten der globalen Kürzel erzeugen keinen Text: sie vergessen das Wort nicht
            var vks = new HashSet<ushort>();
            foreach (var combo in new[] { _settings.HotkeyShow, _settings.HotkeyMacros, _settings.HotkeyLanguage }.Concat(_macros.Select(x => x.Hotkey)))
            {
                try
                {
                    if (string.IsNullOrEmpty(combo)) continue;
                    var kc = KeyCombo.Parse(combo);
                    Modifiers extra;
                    if (kc != null) { ushort v = kc.ResolveVk(out extra); if (v != 0) vks.Add(PhysicalKeys.Normalize(v)); }
                }
                catch { }
            }
            _hotkeyVks = vks;
            return _hotkeys.Resume();
        }

        public void OpenKeyTest()
        {
            if (_keyTest != null && !_keyTest.IsDisposed) { _keyTest.Activate(); return; }
            _keyTest = new KeyTestForm(_hotkeys, _hotkeys.Status);
            _keyTest.Show();
        }

        // ---------- Makros ----------

        private void LoadMacros()
        {
            string err;
            var list = MacroStore.Load(out err);
            _macroError = err;
            if (list != null) _macros = list;
        }

        private void SaveMacros(List<Macro> list)
        {
            try
            {
                MacroStore.Save(list);
                _macros = list;
                _macroError = null;
            }
            catch (Exception e)
            {
                MessageBox.Show(L.T("makros.yaml konnte nicht gespeichert werden:\n") + e.Message, L.T("Makros"));
                return;
            }
            var failed = Failed(ApplyHotkeys());
            if (failed.Count > 0)
                _tray.ShowBalloonTip(6000, L.T("Kürzel nicht aktiv"), string.Join("\n", failed), ToolTipIcon.Warning);
        }

        private async void RunMacro(Macro m)
        {
            if (_recorder.Recording) { _keyboard.ShowStatus(L.T("Während der Aufnahme keine Makros")); return; }
            if (_runner.Busy) return;
            try { await _runner.Run(m); }
            finally { _engine.ResetWord(false); }
        }

        private List<string> Groups()
        {
            return _macros.Select(m => m.Group).Where(g => g.Length > 0).Distinct().ToList();
        }

        private static string SuggestName(string text)
        {
            var words = text.Replace("\n", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string n = string.Join(" ", words.Take(4));
            if (n.Length > 30) n = n.Substring(0, 30).TrimEnd() + "…";
            return n.Length > 0 ? n : L.T("Neues Makro");
        }

        private void AddMacro(Macro m)
        {
            if (_macroError != null)
            {
                _keyboard.ShowStatus(L.T("makros.yaml hat einen Fehler, bitte erst reparieren"), 4000);
                return;
            }
            var list = _macros.Select(x => x.Clone()).ToList();
            list.Add(m);
            SaveMacros(list);
            _keyboard.ShowStatus(L.T("Makro \"") + m.Name + L.T("\" gespeichert"));
        }

        private void QuickSave()
        {
            string text = _engine.LastTyped.Trim();
            if (text.Length == 0)
            {
                _keyboard.CloseMacroBar();
                _keyboard.ShowStatus(L.T("Erst etwas tippen, dann + zum Speichern"), 3500);
                return;
            }
            _keyboard.CloseMacroBar();
            using (var d = new NameDialog(L.T("Als Textbaustein speichern"), L.T("Text: ") + text, SuggestName(text), Groups()))
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                var m = new Macro { Name = d.MacroName, Group = d.Group };
                m.Steps.Add(new MacroStep(StepKind.Text, text));
                AddMacro(m);
            }
        }

        private void StartRecording()
        {
            _recorder.Start();
            _keyboard.Recording = true;
            _keyboard.ShowStatus(L.T("Aufnahme läuft: einfach tippen, dann ■ Stopp"), 3500);
        }

        private void CancelRecording()
        {
            _recorder.Stop();
            _keyboard.Recording = false;
            _keyboard.ShowStatus(L.T("Aufnahme verworfen"));
        }

        private void StopRecording()
        {
            var steps = _recorder.Stop();
            _keyboard.Recording = false;
            steps.RemoveAll(s => s.Kind == StepKind.Text && s.Value.Length == 0);
            if (steps.Count == 0) { _keyboard.ShowStatus(L.T("Nichts aufgenommen")); return; }

            string preview = string.Join("\n", steps.Take(4).Select(s => s.ToString()));
            if (steps.Count > 4) preview += "\n…";
            var firstText = steps.FirstOrDefault(s => s.Kind == StepKind.Text);
            string name = firstText != null ? SuggestName(firstText.Value) : L.T("Aufnahme ") + DateTime.Now.ToString("HH:mm");
            using (var d = new NameDialog(L.T("Aufnahme speichern"), preview, name, Groups()))
            {
                if (d.ShowDialog() != DialogResult.OK) { _keyboard.ShowStatus(L.T("Aufnahme verworfen")); return; }
                var m = new Macro { Name = d.MacroName, Group = d.Group, Steps = steps };
                AddMacro(m);
            }
        }

        public void OpenMacroEditor()
        {
            if (_editor != null && !_editor.IsDisposed) { _editor.Activate(); return; }
            _editor = new MacroEditorForm(_macros, this, _macroError);
            _editor.Saved += list => SaveMacros(list);
            _editor.ReloadRequested += () =>
            {
                LoadMacros();
                if (_macroError != null) MessageBox.Show(_editor, _macroError, L.T("Makros"));
                else { _editor.SetMacros(_macros); ApplyHotkeys(); }
            };
            _editor.Show();
        }

        // ---------- Wort pro Fenster ----------

        private void OnClickOutside(IntPtr root)
        {
            if (root == IntPtr.Zero || root == _activeWin) { _engine.ResetWord(false); return; }
            // Klick in ein anderes Fenster: das kommt gleich nach vorne
            _clickSwitchTo = root;
            _clickSwitchAt = DateTime.Now;
        }

        private void OnWindowSwitch(IntPtr root)
        {
            if (root == _activeWin) return;
            var now = DateTime.Now;
            string oldProg = _lastProgram;
            var old = _engine.SaveState();
            _states.Save(_activeWin, old, now);
            if (old != null && !old.IsEmpty && oldProg != null) Log.Write("Wort-Zustand gespeichert für " + oldProg);

            bool byClick = root == _clickSwitchTo && (now - _clickSwitchAt).TotalSeconds < 3;
            _clickSwitchTo = IntPtr.Zero;
            _activeWin = root;

            OnProgramChanged();   // erst Sprache, dann Zustand
            var st = _states.Take(root, now);
            if (st != null && byClick && !_settings.ContinueAfterClick) st = null;
            _engine.RestoreState(st);
            if (st != null) Log.Write("Wort-Zustand geladen für " + (_lastProgram ?? "?") + (byClick ? " (nach Klick)" : ""));
            try { _states.Prune(now, Native.IsWindow); } catch { _states.Prune(now, null); }
        }

        // ---------- Echte Tastendrücke ----------

        private void OnPhysicalKey(ushort vk, bool down, uint scan)
        {
            // AltGr schickt vorneweg ein unechtes linkes Strg
            if (vk == 0xA2 && (scan & 0x200) != 0) return;
            var now = DateTime.UtcNow;
            bool isMod = PhysicalKeys.IsModifier(vk);
            if (down)
            {
                bool first = !_physical.IsDown(vk);
                _physical.Down(vk, now);
                if (first)
                {
                    var mods = _physical.HeldMods;
                    if (!isMod && !_hotkeyVks.Contains(PhysicalKeys.Normalize(vk)))
                        _engine.ResetWord(false);   // eingefügter oder diktierter Text ist unbekannt
                    // keine Statuszeile mehr für echte Tasten (8.5), nur das rote Aufleuchten auf der Tastatur
                }
            }
            else if (_physical.Up(vk, now))
            {
                _engine.ResetWord(false);   // z. B. Strg+Shift für Wispr Flow: danach kommt diktierter Text
            }
            if (_settings.ShowPhysicalKeys && _keyboard.Visible) _keyboard.Invalidate();
        }

        // ---------- Lernen ----------

        private bool LearnActive { get { return _settings.LearningEnabled && !_learnPaused && !_ownWindowActive; } }

        private bool _ownWindowActive;
        private bool _ibanField, _mailField;   // in solchen Feldern kein Leerzeichen nach Satzzeichen   // eines unserer eigenen Fenster (Tresor-Eintrag, Einstellungen, Sicherung) ist vorne

        private static bool IsOwnProcess(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid != (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return false;
            return !Native.IsIconic(hwnd);   // der unsichtbare Taskleisten-Knopf ist immer minimiert und zählt nicht
        }

        /// <summary>Eigenes Fenster kommt nach vorne: Passwortfeld der Webseite ist damit verlassen,
        /// in Name und E-Mail ganz normal tippen (keine Punkte), aber nichts lernen.</summary>
        private void OnOwnWindow(bool own)
        {
            _ownWindowActive = own;
            if (own && !_ownSecret) LeaveForeignPassword(true);   // wie beim Verlassen des Felds: Speichern anbieten
        }

        /// <summary>Passwortmodus der fremden Seite beenden. offer: getipptes Passwort zum Speichern anbieten.</summary>
        private void LeaveForeignPassword(bool offer)
        {
            if (!_engine.PasswordMode) return;
            _keyboard.HideReveal();
            _keyboard.VaultTypedLength = 0;
            string secret = _engine.TakeSecret();
            _vaultShown = false;
            _engine.PasswordMode = false;
            _recorder.Paused = false;
            if (offer && secret != null) Offer(secret);
            _keyboard.UpdateBand();
        }

        private static string LearnPath(string code)
        {
            return System.IO.Path.Combine(Settings.DataDir, "Lernen", code + ".txt");
        }

        private static string NumbersPath { get { return System.IO.Path.Combine(Settings.DataDir, "Lernen", "zahlen.txt"); } }
        private static string EmailsPath { get { return System.IO.Path.Combine(Settings.DataDir, "Lernen", "emails.txt"); } }

        private void SaveLearning()
        {
            if (_langSet.Emails.Dirty)
            {
                try { _langSet.Emails.Save(EmailsPath); }
                catch (Exception e) { Log.Write("Gelernte E-Mail-Adressen nicht gespeichert: " + e.Message); }
            }
            if (_langSet.Numbers.Dirty)
            {
                try { _langSet.Numbers.Save(NumbersPath); }
                catch (Exception e) { Log.Write("Gelernte Zahlen nicht gespeichert: " + e.Message); }
            }
            foreach (var st in _langSet.Stores.ToList())
            {
                if (!st.Dirty) continue;
                try { st.Save(LearnPath(st.Code)); }
                catch (Exception e) { Log.Write("Gelerntes für " + st.Code + " nicht gespeichert: " + e.Message); }
            }
            _lastLearnSave = DateTime.Now;
        }

        private void ToggleLearnPause()
        {
            _learnPaused = !_learnPaused;
            _pauseItem.Text = _learnPaused ? L.T("Lernen fortsetzen") : L.T("Lernen pausieren");
            _keyboard.ShowStatus(_learnPaused ? L.T("Lernen pausiert") : L.T("Lernen läuft wieder"));
        }

        public string LearningSummary
        {
            get
            {
                var parts = _langSet.Active.Select(p =>
                {
                    var st = _langSet.Store(p.Code);
                    return p.Code.ToUpperInvariant() + ": " + (st == null ? 0 : st.WordCount) + L.T(" Wörter");
                });
                return L.T("Bisher gelernt: ") + string.Join(", ", parts) + L.T(", Zahlen: ") + _langSet.Numbers.Count;
            }
        }

        public void ImportTexts(List<string> paths)
        {
            using (var f = new ImportForm(paths, _langSet))
            {
                f.ShowDialog();
                var r = f.Result;
                SaveLearning();
                _keyboard.UpdateBand();
                if (r == null) return;
                string msg = r.Files + L.T(" Dateien gelesen, ") + r.Words + L.T(" Wörter in ") + r.Sentences + L.T(" Sätzen gelernt")
                    + (r.Numbers > 0 ? L.T(", dazu ") + r.Numbers + L.T("-mal Datum oder Telefonnummer.") : ".");
                if (r.WordsPerLanguage.Count > 1) msg += "\n" + string.Join(", ", r.WordsPerLanguage.Select(kv => kv.Key.ToUpperInvariant() + " " + kv.Value));
                if (r.Skipped > 0) msg += "\n" + r.Skipped + L.T(" Dateien übersprungen.");
                if (r.Errors.Count > 0) msg += "\n\n" + string.Join("\n", r.Errors.Take(5));
                msg += L.T("\n\nGespeichert wird nur, welche Wörter wie oft vorkommen, nicht die Texte selbst. Zeilen mit Passwort, PIN, IBAN und Ähnlichem werden übersprungen.");
                MessageBox.Show(msg, L.T("Aus meinen Texten lernen"));
            }
        }

        public void OpenLearnedWords()
        {
            if (_learnedForm != null && !_learnedForm.IsDisposed) { _learnedForm.Activate(); return; }
            _learnedForm = new LearnedWordsForm(_langSet);
            _learnedForm.Changed += () => { SaveLearning(); _keyboard.UpdateBand(); };
            _learnedForm.Show();
        }

        private void OnTimer()
        {
            if ((DateTime.Now - _lastLearnSave).TotalMinutes >= 2 && (_langSet.Numbers.Dirty || _langSet.Emails.Dirty || _langSet.Stores.Any(st => st.Dirty))) SaveLearning();
            bool changed = false;
            if (_offer != null && DateTime.Now > _offerUntil) { _offer = null; changed = true; }
            int min = _settings.VaultAutoLockMinutes;
            if (_vault.HasPin && !_vault.Locked && min > 0 && (DateTime.Now - _vault.LastActivity).TotalMinutes >= min
                && (_vaultEditor == null || _vaultEditor.IsDisposed))
            {
                _vault.Lock();
                Log.Write("Tresor automatisch gesperrt");
                changed = true;
            }
            if (changed) _keyboard.UpdateBand();
        }

        // ---------- Tresor ----------

        private bool _ownSecret;   // Passwortfeld im eigenen Tresor-Fenster hat den Fokus

        private void OnPasswordField(bool isPw)
        {
            if (_ownSecret) return;   // das regelt das Tresor-Fenster selbst
            if (isPw && KeyboardForm.ForegroundProcessName() == null) return;   // Passwortfeld in einem eigenen Fenster: regelt SecretField
            if (isPw)
            {
                _engine.PasswordMode = true;
                _recorder.Paused = true;
                if (_settings.VaultEnabled && _settings.VaultAutoShow) _vaultShown = true;
            }
            else
            {
                _keyboard.HideReveal();
                _keyboard.VaultTypedLength = 0;
                string secret = _engine.TakeSecret();
                bool own = KeyboardForm.ForegroundProcessName() == null;   // eigener Dialog (z. B. PIN): nichts anbieten
                if (_engine.PasswordMode) _vaultShown = false;
                _engine.PasswordMode = false;
                _recorder.Paused = false;
                if (secret != null && !own) Offer(secret);
            }
            _keyboard.UpdateBand();
        }

        private void Offer(string secret)
        {
            if (!_settings.VaultEnabled || !_settings.VaultOfferSave) return;
            if (secret.Length < 4 || _vaultError != null) return;
            if (_vault.ContainsPassword(secret)) return;
            _offer = secret;
            _offerTitle = _pwWatcher.FieldTitle ?? _pwWatcher.WindowTitle;
            _offerProcess = _pwWatcher.FieldProcess ?? _pwWatcher.Process;
            _offerUntil = DateTime.Now.AddSeconds(90);
            _keyboard.UpdateBand();
        }

        private void ToggleVault()
        {
            if (!_settings.VaultEnabled) { _keyboard.ShowStatus(L.T("Der Tresor ist in den Einstellungen ausgeschaltet"), 3000); return; }
            if (_offer != null) { _offer = null; _vaultShown = true; }
            else _vaultShown = !_vaultShown;
            if (!_vaultShown) { _vaultPage = 0; if (_vaultSearch != null) { _vaultSearch = null; _keyboard.KeyIntercept = null; } }
            _keyboard.UpdateBand();
        }

        private int VaultSlots
        {
            get { return Math.Max(4, Math.Max(1, _settings.SuggestionCount) * Math.Max(1, Math.Min(2, _settings.SuggestionRows))); }
        }

        private List<BandItem> VaultBand()
        {
            if (!_settings.VaultEnabled) return null;
            var gold = Color.FromArgb(230, 160, 40);
            if (_offer != null)
            {
                return new List<BandItem>
                {
                    new BandItem { Label = L.T("Im Tresor speichern?"), Dim = true, Do = () => { } },
                    new BandItem { Label = L.T("⚿ Speichern"), Bold = true, Color = gold, Do = SaveOffer },
                    new BandItem { Label = L.T("Nein"), Do = () => { _offer = null; _keyboard.UpdateBand(); } }
                };
            }
            if (!_vaultShown) return null;
            var items = new List<BandItem>();
            if (_vaultError != null)
            {
                items.Add(new BandItem { Label = L.T("Tresor nicht lesbar"), Dim = true, Do = () => _keyboard.ShowStatus(_vaultError, 6000) });
            }
            else if (_vault.Locked)
            {
                items.Add(new BandItem { Label = L.T("🔒 Tresor entsperren"), Bold = true, Color = gold, Do = () => { if (UnlockVault()) _keyboard.UpdateBand(); } });
            }
            else
            {
                var list = _vault.Sorted(_pwWatcher.WindowTitle, _pwWatcher.Process);
                if (_vaultSearch != null) list = Vault.Filter(list, _vaultSearch);
                // feste Knöpfe: Suche, + hinzufügen, (🎲), ✕ ; bei mehr Einträgen als Platz zusätzlich "weiter"
                int fixedCount = _vaultSearch != null ? 2 : (_engine.PasswordMode ? 4 : 3);
                int room = Math.Max(1, VaultSlots - fixedCount);
                int pages = 1;
                if (list.Count > room) { room = Math.Max(1, room - 1); pages = (list.Count + room - 1) / room; }
                if (_vaultPage >= pages) _vaultPage = 0;
                if (_vaultSearch != null)
                    items.Add(new BandItem { Label = "🔍 " + _vaultSearch + "▏", Dim = _vaultSearch.Length == 0, Do = () => { } });
                if (list.Count == 0)
                    items.Add(new BandItem { Label = _vaultSearch != null ? L.T("nichts gefunden") : L.T("Tresor ist noch leer"), Dim = true, Do = () => { } });
                foreach (var e in list.Skip(_vaultPage * room).Take(room))
                {
                    var entry = e;
                    items.Add(new BandItem { Label = "⚿ " + entry.Name, Color = gold, Do = () => TypeFromVault(entry) });
                }
                if (pages > 1)
                    items.Add(new BandItem { Label = L.T("weiter  ") + (_vaultPage + 1) + "/" + pages + "  ›", Bold = true,
                        Do = () => { _vaultPage = (_vaultPage + 1) % pages; _keyboard.UpdateBand(); } });
                if (_vaultSearch == null)
                {
                    items.Add(new BandItem { Label = L.T("🔍 Suchen"), Do = StartVaultSearch });
                    items.Add(new BandItem { Label = L.T("+ Passwort hinzufügen"), Bold = true, Do = AddPasswordDialog });
                    if (_engine.PasswordMode) items.Add(new BandItem { Label = L.T("🎲 Erzeugen und eintippen"), Do = TypeNewPassword });
                }
            }
            if (_vaultSearch != null)
                items.Add(new BandItem { Label = L.T("✕ Suche beenden"), Do = EndVaultSearch });
            else
                items.Add(new BandItem { Label = L.T("✕ Ausblenden"), Do = () => { _vaultShown = false; _vaultPage = 0; _keyboard.UpdateBand(); } });
            return items;
        }

        private int _vaultPage;
        private string _vaultSearch;   // null = keine Suche, sonst die getippten Buchstaben


        private void StartVaultSearch()
        {
            if (_vault.Locked && !UnlockVault()) return;
            _vaultSearch = ""; _vaultPage = 0;
            _keyboard.KeyIntercept = VaultSearchKey;
            _keyboard.ShowStatus(L.T("Namen tippen, die Liste wird kleiner"), 2500);
            _keyboard.UpdateBand();
        }

        private void EndVaultSearch()
        {
            _vaultSearch = null; _vaultPage = 0;
            _keyboard.KeyIntercept = null;
            _keyboard.UpdateBand();
        }

        /// <summary>Tasten während der Suche: Buchstaben und Ziffern in die Suche, Rücktaste löscht, Esc/Enter beendet.</summary>
        private bool VaultSearchKey(KeyDef k)
        {
            if (_vaultSearch == null) return false;
            if (!_vaultShown) { EndVaultSearch(); return false; }   // Tresor ist weg: Tasten wieder ganz normal
            if (k.Kind == KeyKind.Char)
            {
                string c = _engine.OutputFor(k);
                if (!string.IsNullOrEmpty(c)) { _vaultSearch += c; _vaultPage = 0; _engine.ClearOneShotMods(); }
                _keyboard.UpdateBand();
                return true;
            }
            if (k.Kind == KeyKind.Space) { _vaultSearch += " "; _keyboard.UpdateBand(); return true; }
            if (k.Kind == KeyKind.Special && k.Vk == InputSender.VK_BACK)
            {
                if (_vaultSearch.Length > 0) _vaultSearch = _vaultSearch.Substring(0, _vaultSearch.Length - 1);
                _vaultPage = 0;
                _keyboard.UpdateBand();
                return true;
            }
            if (k.Kind == KeyKind.Special && (k.Vk == InputSender.VK_ESCAPE || k.Vk == InputSender.VK_RETURN)) { EndVaultSearch(); return true; }
            return false;   // Shift, Umschalten usw. ganz normal
        }

        private void TypeFromVault(VaultEntry e)
        {
            if (_vault.Locked && !UnlockVault()) return;
            new SystemOutput().SendText(e.Password);    // direkt, ohne Aufnahme, ohne Lernen, ohne Zwischenablage
            _keyboard.VaultTypedLength = e.Password.Length;   // Eingabefeld: nur Punkte
            e.LastUsed = DateTime.Now;
            _vault.LastActivity = DateTime.Now;
            try { _vault.Save(); } catch (Exception ex) { Log.Write("Tresor speichern: " + ex.Message); }
            _engine.TakeSecret();
            _vaultShown = false;
            if (_vaultSearch != null) { _vaultSearch = null; _keyboard.KeyIntercept = null; }
            _vaultPage = 0;
            _keyboard.UpdateBand();
            _keyboard.ShowStatus(L.T("Passwort \"") + e.Name + L.T("\" eingetippt"), 2000);
        }

        private void TypeNewPassword()
        {
            string pw = Vault.Generate(_settings.GeneratorLength, _settings.GeneratorSymbols);
            new SystemOutput().SendText(pw);
            _engine.TakeSecret();
            _vaultShown = false;
            _offer = pw;
            _offerTitle = _pwWatcher.FieldTitle ?? _pwWatcher.WindowTitle;
            _offerProcess = _pwWatcher.FieldProcess ?? _pwWatcher.Process;
            _offerUntil = DateTime.Now.AddMinutes(5);
            _keyboard.UpdateBand();
            _keyboard.ShowStatus(L.T("Neues Passwort eingetippt. Jetzt speichern, sonst ist es weg!"), 5000);
        }

        private bool _savingOffer;   // Dialog ist schon offen, kein zweiter

        private void SaveOffer()
        {
            if (_savingOffer) return;
            _savingOffer = true;
            try { SaveOfferInner(); }
            finally { _savingOffer = false; }
        }

        private void SaveOfferInner()
        {
            string pw = _offer;
            if (pw == null) return;
            if (_vault.Locked && !UnlockVault()) return;
            var e = new VaultEntry
            {
                Name = Vault.SuggestName(_offerTitle, _offerProcess),
                Password = pw,
                User = RecentEmail(_offerTitle),
                Match = _offerProcess ?? ""
            };
            if (e.Name.Length == 0) e.Name = L.T("Passwort ") + DateTime.Now.ToString("dd.MM. HH:mm");
            using (var d = new VaultEntryDialog(e, _settings))
            {
                if (d.ShowDialog() != DialogResult.OK) { _keyboard.ShowStatus(L.T("Nicht gespeichert. \"Speichern\" geht noch kurz."), 3000); return; }
                _vault.Entries.Add(d.Result);
            }
            try { _vault.Save(); _offer = null; _keyboard.ShowStatus(L.T("Im Tresor gespeichert"), 2500); }
            catch (Exception ex) { MessageBox.Show(L.T("Tresor konnte nicht gespeichert werden:\n") + ex.Message, L.T("Passwort-Tresor")); }
            _vault.LastActivity = DateTime.Now;
            _keyboard.UpdateBand();
        }

        private bool _addingPassword;
        private string _lastEmail, _lastEmailTitle;
        private DateTime _lastEmailAt;

        /// <summary>Kurz vorher im selben Fenster getippte E-Mail-Adresse (meist der Benutzername zum Passwort).</summary>
        private string RecentEmail(string title)
        {
            if (_lastEmail == null) return "";
            double min = (DateTime.Now - _lastEmailAt).TotalMinutes;
            bool sameWindow = title != null && title == _lastEmailTitle;
            return (sameWindow && min < 15) || min < 3 ? _lastEmail : "";
        }

        /// <summary>"+ Passwort hinzufügen": Fenster für einen neuen Eintrag, Name nach Webseite oder Programm vorgeschlagen.</summary>
        private void AddPasswordDialog()
        {
            if (_addingPassword) return;
            _addingPassword = true;
            try
            {
                string title = _pwWatcher.WindowTitle, proc = _pwWatcher.Process;
                // Gerade auf der Seite getipptes Passwort gleich mitnehmen (vor der PIN-Abfrage, die schließt das Feld)
                string typed = null;
                if (_engine.PasswordMode && !_ownSecret && _engine.SecretLength > 0)
                {
                    typed = _engine.SecretText;
                    title = _pwWatcher.FieldTitle ?? title; proc = _pwWatcher.FieldProcess ?? proc;
                }
                else if (_offer != null && DateTime.Now <= _offerUntil)
                {
                    typed = _offer; title = _offerTitle ?? title; proc = _offerProcess ?? proc;
                }
                var e = new VaultEntry { Name = Vault.SuggestName(title, proc), Password = typed ?? "", User = RecentEmail(title), Match = proc ?? "", Created = DateTime.Now };
                // Passwortfeld der Seite ist damit fertig. Bei "Abbrechen" bleibt das Angebot zum Speichern.
                if (_engine.PasswordMode && !_ownSecret) LeaveForeignPassword(true);
                if (_vault.Locked && !UnlockVault()) return;
                using (var d = new VaultEntryDialog(e, _settings))
                {
                    if (d.ShowDialog() != DialogResult.OK) return;
                    _vault.Entries.Add(d.Result);
                    if (_offer != null && _offer == d.Result.Password) _offer = null;   // ist ja jetzt gespeichert
                }
                try { _vault.Save(); _keyboard.ShowStatus(L.T("Im Tresor gespeichert"), 2500); }
                catch (Exception ex) { MessageBox.Show(L.T("Tresor konnte nicht gespeichert werden:\n") + ex.Message, L.T("Passwort-Tresor")); }
                _vault.LastActivity = DateTime.Now;
                _vaultShown = true;
                _keyboard.UpdateBand();
            }
            finally { _addingPassword = false; }
        }

        private bool UnlockVault()
        {
            if (!_vault.Locked) return true;
            for (int i = 0; i < 3; i++)
            {
                using (var d = new PinDialog(L.T("Tresor entsperren"), i == 0 ? L.T("PIN eingeben") : L.T("PIN falsch, nochmal versuchen")))
                {
                    if (d.ShowDialog() != DialogResult.OK) return false;
                    if (_vault.Unlock(d.Pin)) { Log.Write("Tresor entsperrt"); return true; }
                }
            }
            Log.Write("Tresor: dreimal falsche PIN");
            return false;
        }

        // ---------- Sichern und übertragen ----------

        private string LearnDir { get { return System.IO.Path.Combine(Settings.DataDir, "Lernen"); } }

        private List<string> LearnedCodesOnDisk()
        {
            var codes = new List<string>();
            try
            {
                if (System.IO.Directory.Exists(LearnDir))
                    foreach (var f in System.IO.Directory.GetFiles(LearnDir, "*.txt"))
                    {
                        string c = System.IO.Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        if (c != "zahlen" && c != "emails" && c.Length <= 12 && c.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')) codes.Add(c);
                    }
            }
            catch (Exception e) { Log.Write("Lernordner: " + e.Message); }
            foreach (var st in _langSet.Stores) if (!codes.Contains(st.Code)) codes.Add(st.Code);
            return codes;
        }

        private LearningStore StoreFor(string code)
        {
            var st = _langSet.Store(code);
            if (st != null) return st;
            try { return LearningStore.Load(LearnPath(code), code); }
            catch { return new LearningStore { Code = code }; }
        }

        private string ReadCustomWords()
        {
            try { return System.IO.File.Exists(_langs.CustomWordsPath) ? System.IO.File.ReadAllText(_langs.CustomWordsPath, System.Text.Encoding.UTF8) : ""; }
            catch { return ""; }
        }

        private static int CustomCount(string text)
        {
            return text.Split('\n').Count(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#"));
        }

        private string BackupFolder()
        {
            string f = _settings.BackupFolder;
            if (!string.IsNullOrEmpty(f) && System.IO.Directory.Exists(f)) return f;
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        public void ExportData()
        {
            SaveLearning();
            var codes = LearnedCodesOnDisk();
            int words = codes.Sum(c => StoreFor(c).WordCount);
            string custom = ReadCustomWords();
            bool vaultUsable = _vaultError == null && _settings.VaultEnabled;
            string vaultInfo = _vaultError != null ? L.T("nicht lesbar") : _vault.Locked ? L.T("gesperrt, PIN wird abgefragt") : _vault.Entries.Count.ToString();
            using (var f = new ExportForm(L.F("{0} Wörter, {1} Zahlen", words, _langSet.Numbers.Count), CustomCount(custom).ToString(), _macros.Count.ToString(), vaultInfo, vaultUsable))
            {
                if (f.ShowDialog() != DialogResult.OK) return;
                if (f.VaultIncluded && !UnlockVault()) return;
                var b = new Backup { Computer = Environment.MachineName };
                if (f.Words)
                {
                    foreach (var c in codes) { var st = StoreFor(c); if (st.WordCount > 0) b.Files[Backup.LearnFile(c)] = st.ToText(); }
                    b.Files[Backup.NumbersFile] = _langSet.Numbers.ToText(true, f.VaultIncluded);   // PINs und Kartennummern nur in die verschlüsselte Sicherung
                    if (_langSet.Emails.Count > 0) b.Files[Backup.EmailsFile] = _langSet.Emails.ToText();
                }
                if (f.Custom) b.Files[Backup.CustomFile] = custom;
                if (f.Macros) b.Files[Backup.MacroFile] = MacroStore.Serialize(_macros);
                if (f.VaultIncluded) b.Files[Backup.VaultFile] = Vault.Serialize(_vault.Entries);
                string password = f.Password;
                using (var d = new SaveFileDialog
                {
                    Title = L.T("Sicherung speichern"), Filter = AppInfo.Name + " (*.nohands)|*.nohands",
                    InitialDirectory = BackupFolder(), FileName = Backup.DefaultFileName(DateTime.Now), OverwritePrompt = true
                })
                {
                    if (d.ShowDialog() != DialogResult.OK) { b.Files.Clear(); return; }
                    try
                    {
                        b.Save(d.FileName, password);
                        _settings.BackupFolder = System.IO.Path.GetDirectoryName(d.FileName);
                        _settings.Save();
                        Log.Write("Sicherung exportiert (" + string.Join(", ", b.Files.Keys.Select(k => k == Backup.VaultFile ? "tresor" : k)) + ")");
                        MessageBox.Show(L.T("Gespeichert:\n") + d.FileName + (f.VaultIncluded ? L.T("\n\nDie Datei ist verschlüsselt. Ohne das Sicherungspasswort lässt sie sich nicht öffnen.") : ""), L.T("Daten exportieren"));
                    }
                    catch (Exception e) { MessageBox.Show(L.T("Hat nicht geklappt: ") + e.Message, L.T("Daten exportieren")); }
                    finally { b.Files.Clear(); }
                }
            }
        }

        public void ImportData()
        {
            string path;
            using (var d = new OpenFileDialog { Title = L.T("Sicherung öffnen"), Filter = AppInfo.Name + " (*.nohands, *.tastatur)|*.nohands;*.tastatur|" + L.T("Alle Dateien") + "|*.*", InitialDirectory = BackupFolder() })
            {
                if (d.ShowDialog() != DialogResult.OK) return;
                path = d.FileName;
            }
            Backup.Header h;
            try { h = Backup.ReadHeader(System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8)); }
            catch (Exception e) { MessageBox.Show(e.Message, L.T("Daten importieren")); return; }
            using (var f = new ImportForm2(h))
            {
                if (f.ShowDialog() != DialogResult.OK || f.Result == null) return;
                var b = f.Result;
                try
                {
                    var report = new List<string>();
                    if (f.Words)
                    {
                        int nw = 0;
                        foreach (var c in b.LearnCodes)
                        {
                            var st = StoreFor(c);
                            nw += st.MergeFrom(LearningStore.FromText(b.Files[Backup.LearnFile(c)], c));
                            st.Save(LearnPath(c));
                        }
                        int nn = 0;
                        if (b.Files.ContainsKey(Backup.NumbersFile)) { nn = _langSet.Numbers.MergeFrom(NumberStore.FromText(b.Files[Backup.NumbersFile])); _langSet.Numbers.Save(NumbersPath); }
                        if (b.Files.ContainsKey(Backup.EmailsFile)) { int ne = _langSet.Emails.MergeFrom(EmailStore.FromText(b.Files[Backup.EmailsFile])); _langSet.Emails.Save(EmailsPath); if (ne > 0) report.Add(L.F("{0} neue E-Mail-Adressen", ne)); }
                        report.Add(L.F("{0} neue Wörter, {1} neue Zahlen", nw, nn));
                    }
                    if (f.Custom && b.Files.ContainsKey(Backup.CustomFile))
                    {
                        int added;
                        string merged = Backup.MergeCustomWords(ReadCustomWords(), b.Files[Backup.CustomFile], out added);
                        if (added > 0) System.IO.File.WriteAllText(_langs.CustomWordsPath, merged, new System.Text.UTF8Encoding(false));
                        report.Add(L.F("{0} neue eigene Begriffe", added));
                    }
                    if (f.Macros && b.Files.ContainsKey(Backup.MacroFile))
                    {
                        int added, replaced;
                        var list = Backup.MergeMacros(_macros, MacroStore.Parse(b.Files[Backup.MacroFile]), out added, out replaced);
                        if (added + replaced > 0) SaveMacros(list);
                        report.Add(L.F("{0} neue Makros, {1} ersetzt", added, replaced));
                    }
                    if (f.VaultIncluded && b.HasVault)
                    {
                        if (_vaultError != null) report.Add(L.T("Passwörter übersprungen: ") + _vaultError);
                        else if (!UnlockVault()) report.Add(L.T("Passwörter übersprungen (Tresor gesperrt)"));
                        else
                        {
                            int added, updated;
                            var incoming = Vault.Deserialize(b.Files[Backup.VaultFile]);
                            Backup.MergeVault(_vault.Entries, incoming, out added, out updated);
                            foreach (var e in incoming) e.Password = null;
                            if (added + updated > 0) _vault.Save();
                            report.Add(L.F("{0} neue Passwörter, {1} aktualisiert", added, updated));
                        }
                    }
                    LoadLanguages();
                    _keyboard.UpdateBand();
                    _settings.BackupFolder = System.IO.Path.GetDirectoryName(path);
                    _settings.Save();
                    Log.Write("Sicherung importiert: " + string.Join("; ", report));
                    MessageBox.Show(L.T("Fertig.\n\n") + string.Join("\n", report), L.T("Daten importieren"));
                }
                catch (Exception e) { MessageBox.Show(L.T("Hat nicht geklappt: ") + e.Message, L.T("Daten importieren")); }
                finally { b.Files.Clear(); }
            }
        }

        public void OpenVaultEditor()
        {
            if (_vaultError != null) { MessageBox.Show(_vaultError + L.T("\n\nDie Datei tresor.dat im Datenordner prüfen oder eine Sicherung (tresor.dat.bak) zurückholen."), L.T("Passwort-Tresor")); return; }
            if (_vaultEditor != null && !_vaultEditor.IsDisposed) { _vaultEditor.Activate(); return; }
            if (!UnlockVault()) return;
            _vaultEditor = new VaultEditorForm(_vault, _settings);
            _vaultEditor.Changed += () => { _vault.LastActivity = DateTime.Now; _keyboard.UpdateBand(); };
            _vaultEditor.FormClosed += (o, e) => _vault.LastActivity = DateTime.Now;
            _vaultEditor.Show();
        }

        public string VaultSummary
        {
            get
            {
                if (_vaultError != null) return _vaultError;
                string pin = _vault.HasPin ? L.T("mit PIN") : L.T("ohne PIN");
                if (_vault.Locked) return L.T("Tresor gesperrt (") + pin + ")";
                return _vault.Entries.Count + L.T(" Passwörter gespeichert, ") + pin;
            }
        }

        public bool VaultHasPin { get { return _vault.HasPin; } }

        public string ChangeVaultPin(bool setNew)
        {
            if (_vaultError != null) return _vaultError;
            if (!setNew && !_vault.HasPin) return L.T("Es ist keine PIN gesetzt.");
            if (!UnlockVault()) return null;
            try
            {
                if (setNew)
                {
                    string pin = PinDialog.AskNew(null);
                    if (pin == null) return null;
                    _vault.SetPin(pin);
                    _vault.LastActivity = DateTime.Now;
                    return L.T("PIN gespeichert. Merk sie dir gut: ohne PIN kommt niemand mehr an die Passwörter, auch du nicht.");
                }
                _vault.SetPin(null);
                return L.T("PIN entfernt. Der Tresor ist weiter mit deinem Windows-Konto verschlüsselt.");
            }
            catch (Exception e) { return L.T("Hat nicht geklappt: ") + e.Message; }
        }

        private void Exit()
        {
            _settings.Save();
            _timer.Stop();
            SaveLearning();
            _pwWatcher.Dispose();
            if (_physHook != null) _physHook.Dispose();
            if (_vaultEditor != null && !_vaultEditor.IsDisposed) _vaultEditor.Close();
            if (_learnedForm != null && !_learnedForm.IsDisposed) _learnedForm.Close();
            _watcher.Dispose();
            _hotkeys.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            if (_settingsForm != null && !_settingsForm.IsDisposed) _settingsForm.Close();
            if (_editor != null && !_editor.IsDisposed) _editor.Close();
            if (_keyTest != null && !_keyTest.IsDisposed) _keyTest.Close();
            Log.Write("Beendet");
            _keyboard.Close();
            _mini.Close();
            ExitThread();
        }
    }
}
