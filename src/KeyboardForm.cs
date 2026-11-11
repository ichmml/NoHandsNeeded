using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace Tastatur
{
    internal enum ToolKind { None, Drag, Fn, Esc, Slot, Macro, Dwell, Settings, Collapse, Close, Rec, QuickSave, Edit, Cancel, Lang, Vault, Input }

    internal class Target
    {
        public KeyDef Key;
        public ToolKind Tool;
        public int Index;
        public RectangleF Rect;
        public bool Repeatable;
    }

    /// <summary>Ein Knopf in der oberen Leiste: Wortvorschlag, Makro, Gruppe oder Blättern.</summary>
    internal class BandItem
    {
        public string Label;
        public Color? Color;
        public Color? TextColor;   // z. B. rot für das getippte Wort
        public string Badge;       // klein unten rechts, z. B. "neu" oder Anzahl Zeichen
        public bool Dim;
        public bool Bold;
        public Action Do;
    }

    internal class KeyboardForm : NoActivateForm
    {
        private readonly Settings _settings;
        private readonly KeyboardEngine _engine;
        private List<List<KeyDef>> _main = KeyLayouts.CreateGerman();
        private string _layoutCode = "de";
        private bool _latin;   // Russisch: lateinisches Layout eingeschaltet
        /// <summary>Welches Layout gerade gelten soll (Sprache oder fest eingestellt).</summary>
        public Func<string> LayoutCodeSource;
        public string LayoutCode { get { return _latin ? "en" : _layoutCode; } }

        /// <summary>Tastenlayout wechseln (z. B. beim Sprachwechsel).</summary>
        public void SetLayout(string code, bool force = false)
        {
            if (!KeyLayouts.Has(code)) code = "en";
            if (code == _layoutCode && !force) return;
            _layoutCode = code;
            _latin = false;
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            string code = _latin ? "en" : _layoutCode;
            _main = KeyLayouts.Create(code, _latin);
            _nav = KeyLayouts.CreateNav();
            _engine.DeadKeys = KeyLayouts.DeadKeys(code);
            try { _engine.LayoutCulture = code == "tr" ? new System.Globalization.CultureInfo("tr-TR") : System.Globalization.CultureInfo.InvariantCulture; }
            catch { _engine.LayoutCulture = System.Globalization.CultureInfo.InvariantCulture; }
            _hover = null;
            BuildLayout();
            Invalidate();
        }
        private List<List<KeyDef>> _nav = KeyLayouts.CreateNav();
        private readonly List<Target> _targets = new List<Target>();
        private bool _physicalWasActive;
        private List<BandItem> _band = new List<BandItem>();
        private Theme _theme;

        private Font _fontMain, _fontSmall, _fontTool, _fontBand;
        private float _rowH;
        private RectangleF _grip, _bandRect;

        // Makroleiste
        private bool _macroMode;
        private string _macroGroup;     // null = oberste Ebene
        private int _macroPage;
        private string _fgProcess;
        public Func<List<Macro>> MacroSource;
        public Func<string> MacroErrorSource;
        private bool _recording;

        // Statusmeldung über der Leiste
        private string _status;
        private DateTime _statusUntil;

        // Verweilklick
        private readonly Timer _timer = new Timer { Interval = 30 };
        private Target _hover;
        private DateTime _hoverSince;
        private bool _armed;

        // Rückmeldung beim Drücken
        private Target _flash;
        private DateTime _flashUntil;

        // Verschieben
        private enum DragMode { None, Held, Dwell }
        private DragMode _drag = DragMode.None;
        private Point _dragOffset;
        private Rectangle _dragStartBounds;
        private double _opacityBeforeDrag;
        private DwellDrag _dwellDrag;
        private bool _dwellDragPossible;        // langes Verweilen auf der aktuellen Taste startet das Ziehen

        // langes Drücken mit der Maustaste
        private Target _pressTarget;
        private DateTime _pressAt;
        private const int PressRingDelayMs = 250;

        // Größe ändern
        private Edge _resizeEdge = Edge.None;
        private Rectangle _resizeStartBounds;
        private Point _resizeStartMouse;

        public Func<string> LanguageLabel;
        /// <summary>Liefert die Tresor-Leiste oder das Speichern-Angebot, sonst null (dann Wortvorschläge).</summary>
        public Func<List<BandItem>> OverrideBand;
        public Func<bool> VaultShown;

        private int _topmostCounter;

        public event Action SettingsRequested;
        public event Action CollapseRequested;
        /// <summary>✕ zweimal getippt: Tastatur beenden.</summary>
        public event Action CloseRequested;
        /// <summary>Fängt Tasten ab (Tresor-Suche). true = verbraucht, nicht ans Programm schicken.</summary>
        public Func<KeyDef, bool> KeyIntercept;
        private DateTime _closeArmedUntil = DateTime.MinValue;
        public event Action<Macro> MacroChosen;
        public event Action RecordStartRequested;
        public event Action RecordStopRequested;
        public event Action RecordCancelRequested;
        public event Action QuickSaveRequested;
        public event Action EditMacrosRequested;
        public event Action LanguageCycleRequested;
        public event Action VaultToggleRequested;

        // ---------- Eingabefeld: zeigt, was getippt wird ----------
        /// <summary>Kennt die Tastatur das Wort? (für "neu")</summary>
        public Func<string, bool> KnownWord;
        /// <summary>Wort aus dem Eingabefeld übernommen: Wort, war es neu?</summary>
        public event Action<string, bool> TypedAccepted;
        /// <summary>Länge eines Tresor-Passworts, das gerade eingetippt wurde (nur Punkte), 0 = keins.</summary>
        public int VaultTypedLength;
        /// <summary>Passwort wird im eigenen Tresor-Fenster sichtbar getippt: oben Buchstaben statt Punkte.</summary>
        public bool PlainSecret;
        private DateTime _revealUntil = DateTime.MinValue;
        private RectangleF _eyeRect;
        private bool _captureExcluded;
        private bool _blinkOn = true;
        private DateTime _blinkAt = DateTime.UtcNow;

        public bool Revealing { get { return DateTime.UtcNow < _revealUntil; } }

        /// <summary>👁: Klartext an (für RevealSeconds) oder wieder aus.</summary>
        public void ToggleReveal()
        {
            if (!_settings.PasswordReveal || LockMode || VaultTypedLength > 0) return;
            _revealUntil = Revealing ? DateTime.MinValue : DateTime.UtcNow.AddSeconds(_settings.RevealSeconds);
            UpdateCaptureExclusion();
            RefreshBand();
            Invalidate();
        }

        public void HideReveal()
        {
            _revealUntil = DateTime.MinValue;
            UpdateCaptureExclusion();
            RefreshBand();
            Invalidate();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        /// <summary>Solange Klartext zu sehen ist: nicht in Bildschirmfotos und Bildschirmfreigaben.</summary>
        private void UpdateCaptureExclusion()
        {
            bool want = Revealing;
            if (want == _captureExcluded || !IsHandleCreated) return;
            bool ok = false;
            try { ok = SetWindowDisplayAffinity(Handle, want ? 0x11u : 0u); } catch { }
            if (!ok && want) Log.Write("Tastatur konnte nicht aus Bildschirmfotos ausgenommen werden (Windows-Version zu alt?)");
            _captureExcluded = want;
        }

        /// <summary>Text im Eingabefeld (ohne Strich). masked: Punkte statt Zeichen.</summary>
        public string InputText(out bool password, out bool unknown)
        {
            password = _engine.PasswordMode;
            unknown = false;
            if (password)
            {
                string own = LockMode ? null : SecretField.DisplayText();   // eigenes Tresor-Fenster: genau das, was im Feld steht
                if (own != null) return own;
                int n = _engine.SecretLength;
                if (n == 0 && VaultTypedLength > 0) return new string('•', VaultTypedLength);
                if (PlainSecret && !LockMode) return _engine.SecretText;
                if (Revealing && _settings.PasswordReveal && !LockMode && VaultTypedLength == 0) return _engine.SecretText;
                return new string('•', n);
            }
            string num = _engine.CurrentNumber, word = _engine.CurrentWord;
            bool numeric = num.Length > 0 && (word.Length == 0 || word.All(char.IsDigit));
            string text = numeric ? num : word;
            string chunk = _engine.CurrentChunk;
            if (chunk.Length > text.Length) { text = chunk; unknown = false; }   // samt Sonderzeichen, z. B. "s!add"
            if (_engine.DeadPending != '\0') text += _engine.DeadPending;
            if (!numeric && text == word && word.Length >= 2 && word.Any(char.IsLetter) && KnownWord != null && !KnownWord(word)) unknown = true;
            return text;
        }

        private void DrawInput(Graphics g, Target t)
        {
            var r = t.Rect;
            bool pw, unknown;
            string text = InputText(out pw, out unknown);
            var bg = Color.FromArgb(252, 252, 246);
            var fg = Color.FromArgb(25, 25, 30);
            using (var path = Rounded(r, 6))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                using (var p = new Pen(_theme.Accent, Math.Max(2f, _rowH * 0.05f))) g.DrawPath(p, path);
            }
            float pad = Math.Max(4f, r.Height * 0.08f);
            var inner = new RectangleF(r.X + pad, r.Y + pad, r.Width - pad * 2, r.Height - pad * 2);
            _eyeRect = RectangleF.Empty;
            bool eye = pw && _settings.PasswordReveal && !LockMode && VaultTypedLength == 0 && _engine.SecretLength > 0;
            if (eye)
            {
                float ew = Math.Min(inner.Height, inner.Width * 0.35f);
                _eyeRect = new RectangleF(inner.Right - ew, inner.Y, ew, inner.Height);
                inner.Width -= ew + pad;
                using (var b = new SolidBrush(Revealing ? _theme.Accent : Color.FromArgb(225, 225, 218)))
                using (var path = Rounded(_eyeRect, 5)) g.FillPath(b, path);
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var f = new Font("Segoe UI Emoji", Math.Max(8f, _eyeRect.Height * 0.42f), FontStyle.Regular, GraphicsUnit.Pixel))
                using (var tb = new SolidBrush(Revealing ? Color.White : fg))
                    g.DrawString("👁", f, tb, _eyeRect, sf);
            }
            // kleine Zeile unten: "neu" bzw. Anzahl Zeichen im Passwortfeld
            string note = pw ? (text.Length > 0 ? text.Length.ToString() : "") : unknown ? L.T("neu") : "";
            float noteH = note.Length > 0 ? inner.Height * 0.28f : 0f;
            var textRect = new RectangleF(inner.X, inner.Y, inner.Width, inner.Height - noteH);
            string shown = text + (_blinkOn ? "|" : " ");
            using (var f0 = new Font(_fontBand.FontFamily, Math.Max(8f, Math.Min(_fontBand.Size * 1.05f, textRect.Height * 0.62f)), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var tb = new SolidBrush(text.Length == 0 ? Color.FromArgb(150, 150, 150) : fg))
            using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            {
                float minSize = Math.Max(8f, f0.Size * 0.6f);
                float size = f0.Size;
                var sz = g.MeasureString(shown, f0, PointF.Empty, sf);
                if (sz.Width > textRect.Width) size = Math.Max(minSize, f0.Size * textRect.Width / sz.Width);
                using (var f = new Font(f0.FontFamily, size, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    // passt es immer noch nicht: vorne mit … kürzen, das Ende bleibt sichtbar
                    string s2 = shown;
                    int cut = 0;
                    while (g.MeasureString(s2, f, PointF.Empty, sf).Width > textRect.Width && cut < text.Length)
                    {
                        cut++;
                        s2 = "…" + text.Substring(cut) + (_blinkOn ? "|" : " ");
                    }
                    g.DrawString(s2, f, tb, textRect, sf);
                }
            }
            if (noteH > 0)
                using (var f = new Font(_fontBand.FontFamily, Math.Max(7f, noteH * 0.8f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var tb = new SolidBrush(unknown ? Color.FromArgb(215, 120, 0) : Color.FromArgb(120, 120, 120)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
                    g.DrawString(note, f, tb, new RectangleF(inner.X, inner.Bottom - noteH, inner.Width, noteH), sf);
        }

        private void ActivateInput()
        {
            if (_engine.PasswordMode) return;
            bool pw, unknown;
            InputText(out pw, out unknown);
            string w = _engine.AcceptTyped();
            if (w == null) return;
            var h = TypedAccepted;
            if (h != null) h(w, unknown);
            if (unknown) ShowStatus(L.F("„{0}“ gemerkt", w), 1500);
        }

        /// <summary>Rahmen der Einführung (in Fensterkoordinaten), null = keiner.</summary>
        public RectangleF? TourHighlight;

        /// <summary>Wo liegt eine Stelle für die Einführung? In Bildschirmkoordinaten, null wenn nicht sichtbar.</summary>
        public Rectangle? TourTargetRect(string id)
        {
            RectangleF? r = null;
            Func<Func<Target, bool>, RectangleF?> find = pred =>
            {
                foreach (var t in _targets) if (pred(t)) return t.Rect;
                return null;
            };
            if (id == "none") { TourHighlight = null; Invalidate(); return null; }
            switch (id)
            {
                case "keyboard": r = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height); break;
                case "band":
                    r = _bandRect;
                    var inp = find(t => t.Key == null && t.Tool == ToolKind.Input);
                    if (inp.HasValue) r = RectangleF.Union(inp.Value, _bandRect);
                    break;
                case "case": r = find(t => t.Key != null && t.Key.Kind == KeyKind.CaseToggle); break;
                case "drag": r = find(t => t.Key != null && t.Key.Kind == KeyKind.Drag); break;
                case "mods":
                    var a = find(t => t.Key != null && t.Key.Kind == KeyKind.Modifier && t.Key.Mod == Modifiers.Ctrl);
                    var b = find(t => t.Key != null && t.Key.Kind == KeyKind.Modifier && t.Key.Mod == Modifiers.Alt);
                    if (a.HasValue && b.HasValue) r = RectangleF.Union(a.Value, b.Value); else r = a;
                    break;
                case "macro": r = find(t => t.Key == null && t.Tool == ToolKind.Macro); break;
                case "lang": r = find(t => t.Key == null && t.Tool == ToolKind.Lang); break;
                case "vault": r = find(t => t.Key == null && t.Tool == ToolKind.Vault); break;
                case "dwell": r = find(t => t.Key == null && t.Tool == ToolKind.Dwell); break;
                case "settings": r = find(t => t.Key == null && t.Tool == ToolKind.Settings); break;
                case "collapse": r = find(t => t.Key == null && t.Tool == ToolKind.Collapse); break;
                case "close": r = find(t => t.Key == null && t.Tool == ToolKind.Close); break;
            }
            if (!r.HasValue) return null;
            if (id != "keyboard") { TourHighlight = r; Invalidate(); }
            var p = PointToScreen(new Point((int)r.Value.X, (int)r.Value.Y));
            return new Rectangle(p.X, p.Y, (int)r.Value.Width, (int)r.Value.Height);
        }

        /// <summary>Echte Tastendrücke für die rote Anzeige (null = aus).</summary>
        public PhysicalKeys Physical;
        private static readonly Color PhysicalRed = Color.FromArgb(215, 45, 45);

        /// <summary>Gibt es die echte Taste auf der gerade sichtbaren Tastatur?</summary>
        public bool HasKeyFor(ushort vk)
        {
            if (Physical == null) return false;
            foreach (var t in _targets) if (t.Key != null && Physical.Matches(t.Key, vk)) return true;
            return false;
        }

        private static Color Blend(Color a, Color b, double f)
        {
            f = Math.Max(0, Math.Min(1, f));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));
        }

        public KeyboardForm(Settings settings, KeyboardEngine engine)
        {
            _settings = settings;
            _engine = engine;
            Text = AppInfo.Name;
            MinimumSize = new Size(480, 180);
            ApplySettings();
            RestoreSavedBounds();

            _engine.Changed += OnEngineChanged;
            _timer.Tick += OnTick;
            _timer.Start();
            RefreshBand();
        }

        private static void Raise(Action a) { if (a != null) a(); }

        public void ApplySettings()
        {
            _theme = Theme.Get(_settings.Theme);
            BackColor = _theme.Background;
            Opacity = _settings.OpacityPercent / 100.0;
            if (LayoutCodeSource != null) SetLayout(LayoutCodeSource());
            if (KeyLayouts.BottomChars != _settings.BottomChars) { KeyLayouts.BottomChars = _settings.BottomChars; ApplyLayout(); }   // sofort, ohne Neustart
            _fontMain = null;
            RefreshBand();
            Invalidate();
        }

        // ---------- Position und Größe ----------

        private static Rectangle[] WorkAreas(out int primary)
        {
            var all = Screen.AllScreens;
            var r = new Rectangle[all.Length];
            primary = 0;
            for (int i = 0; i < all.Length; i++) { r[i] = all[i].WorkingArea; if (all[i].Primary) primary = i; }
            return r;
        }

        private void RestoreSavedBounds()
        {
            int primary;
            var areas = WorkAreas(out primary);
            Bounds = Geometry.Restore(_settings.X, _settings.Y, _settings.WidthPercent, _settings.HeightPercent,
                _settings.DockPosition, areas, primary, MinimumSize);
            Log.Write("Tastatur-Platz beim Start: " + Bounds + DpiInfo.Describe(Cursor.Position));
        }

        /// <summary>Position merken, Größe als Prozent des Bildschirms, auf dem die Tastatur liegt.</summary>
        private void SaveBounds()
        {
            var wa = Screen.FromRectangle(Bounds).WorkingArea;
            _settings.X = Left; _settings.Y = Top; _settings.Width = Width; _settings.Height = Height;
            _settings.WidthPercent = Math.Round(Math.Max(20, Math.Min(100, Width * 100.0 / wa.Width)), 1);
            _settings.HeightPercent = Math.Round(Math.Max(15, Math.Min(80, Height * 100.0 / wa.Height)), 1);
            _settings.Save();
        }

        /// <summary>Standardgröße und Standardplatz auf dem Bildschirm, auf dem die Tastatur gerade ist.</summary>
        public void ResetBounds()
        {
            _settings.WidthPercent = Settings.DefaultWidthPercent;
            _settings.HeightPercent = Settings.DefaultHeightPercent;
            var wa = Screen.FromRectangle(Bounds).WorkingArea;
            Bounds = Geometry.Docked(wa, Geometry.SizeFor(wa, _settings.WidthPercent, _settings.HeightPercent, MinimumSize), _settings.DockPosition);
            SaveBounds();
        }

        /// <summary>Größe aus den Prozent-Reglern übernehmen. Die Seite der Standardposition und die Unterkante bleiben stehen.</summary>
        public void ApplySizeFromSettings()
        {
            var wa = Screen.FromRectangle(Bounds).WorkingArea;
            var size = Geometry.SizeFor(wa, _settings.WidthPercent, _settings.HeightPercent, MinimumSize);
            if (Math.Abs(size.Width - Width) <= 2 && Math.Abs(size.Height - Height) <= 2) return;
            int x = _settings.DockPosition == "links" ? Left
                : _settings.DockPosition == "mitte" ? Left + (Width - size.Width) / 2
                : Right - size.Width;
            var r = new Rectangle(x, Bottom - size.Height, size.Width, size.Height);
            Bounds = Geometry.Fit(r, wa, MinimumSize);
            _settings.X = Left; _settings.Y = Top; _settings.Width = Width; _settings.Height = Height;
        }

        /// <summary>Tastatur zum Mauszeiger holen (beim Öffnen über das Kürzel), Größe passend zu dem Bildschirm, auf dem die Maus ist.</summary>
        public void MoveToMouse()
        {
            int primary;
            var areas = WorkAreas(out primary);
            var p = Cursor.Position;
            var area = areas[Geometry.AreaAt(p, areas)];
            var size = Geometry.SizeFor(area, _settings.WidthPercent, _settings.HeightPercent, MinimumSize);
            int gap = Math.Max(24, area.Height / 25);   // etwa eine Textzeile Abstand
            Bounds = Geometry.AtMouse(p, size, area, gap, _settings.MousePlace);
        }

        /// <summary>Nach dem Absetzen oder einem Wechsel der Skalierung: Größe in Prozent des Bildschirms, ganz drauf.</summary>
        private void FitToScreen(Point at)
        {
            int primary;
            var areas = WorkAreas(out primary);
            var area = areas[Geometry.AreaAt(at, areas)];
            var r = Geometry.Rescale(Bounds, area, _settings.WidthPercent, _settings.HeightPercent, MinimumSize);
            if (r != Bounds) Bounds = r;
        }

        // WM_DPICHANGED: Tastatur kommt auf einen Bildschirm mit anderer Skalierung
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == DpiInfo.WM_DPICHANGED)
            {
                Log.Write("Skalierung gewechselt auf " + ((int)m.WParam.ToInt64() & 0xFFFF) + " dpi");
                // beim Ziehen Größe behalten (die Schrift rechnet in Pixeln), danach passend einsetzen
                if (_drag == DragMode.None && _resizeEdge == Edge.None)
                    BeginInvoke((Action)(() => { FitToScreen(new Point(Left + Width / 2, Top + Height / 2)); SaveBounds(); }));
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        // ---------- Leiste ----------

        private void OnEngineChanged()
        {
            RefreshBand();
            Invalidate();
        }

        public bool Recording
        {
            get { return _recording; }
            set { _recording = value; if (value) _macroMode = false; RefreshBand(); Invalidate(); }
        }

        public bool MacroMode { get { return _macroMode; } }

        /// <summary>Leiste neu aufbauen (z. B. wenn Tresor oder Passwortfeld sich ändern).</summary>
        public void UpdateBand()
        {
            RefreshBand();
            Invalidate();
        }

        public void OpenMacroBar()
        {
            if (_recording) return;
            _macroMode = true;
            _macroGroup = null;
            _macroPage = 0;
            _fgProcess = ForegroundProcessName();
            _hover = null;
            RefreshBand();
            Invalidate();
        }

        public void CloseMacroBar()
        {
            _macroMode = false;
            RefreshBand();
            Invalidate();
        }

        public void ShowStatus(string text, int ms = 2500)
        {
            _status = text;
            _statusUntil = DateTime.UtcNow.AddMilliseconds(ms);
            Invalidate();
        }

        public static string ForegroundProcessName()
        {
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                if (pid == 0 || pid == (uint)Process.GetCurrentProcess().Id) return null;
                using (var p = Process.GetProcessById((int)pid)) return p.ProcessName;
            }
            catch { return null; }
        }

        private int SlotCount
        {
            get { return Math.Max(1, _settings.SuggestionCount) * Math.Max(1, Math.Min(2, _settings.SuggestionRows)); }
        }

        private static readonly KeyDef EscKey = new KeyDef { Kind = KeyKind.Special, Label = "Esc", Vk = InputSender.VK_ESCAPE, Width = 1 };
        private static readonly Color TypedRed = Color.FromArgb(220, 40, 40);

        private void RefreshBand()
        {
            List<BandItem> over = !_macroMode && OverrideBand != null ? OverrideBand() : null;
            var typed = TypedItems();
            int slots = SlotCount;
            var rest = _macroMode ? MacroItems(Math.Max(1, slots - typed.Count)) : over ?? SuggestionItems();
            if (typed.Count > 0 && !_macroMode)
            {
                // getipptes Wort steht schon vorne: nicht doppelt
                string tw = typed[0].Label;
                rest = rest.Where(i => !string.Equals(i.Label, tw, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            _band = typed.Concat(rest).ToList();
            BuildLayout();
        }

        /// <summary>
        /// Erstes Feld oben links: was gerade getippt wird, rot und fett (wie bei der alten Tastatur).
        /// Im Passwortfeld Punkte und daneben 👁.
        /// </summary>
        private List<BandItem> TypedItems()
        {
            var list = new List<BandItem>();
            if (!_settings.ShowInputField) return list;
            bool pw, unknown;
            string text = InputText(out pw, out unknown);
            if (text.Length == 0) return list;
            if (pw)
            {
                list.Add(new BandItem { Label = text, TextColor = TypedRed, Bold = true, Badge = text.Length.ToString(), Do = () => { } });
                if (_settings.PasswordReveal && !LockMode && !PlainSecret && VaultTypedLength == 0 && _engine.SecretLength > 0)
                    list.Add(new BandItem { Label = "👁", Color = Revealing ? _theme.Accent : (Color?)null, Do = ToggleReveal });
                return list;
            }
            list.Add(new BandItem { Label = text, TextColor = TypedRed, Bold = true, Badge = unknown ? L.T("neu") : null, Do = ActivateInput });
            return list;
        }

        /// <summary>Passende gelernte E-Mail-Adressen zum Getippten (setzt das Programm).</summary>
        public Func<string, List<string>> EmailSuggest;
        /// <summary>Im E-Mail-Feld: alle passenden Adressen, auch ohne getippte Buchstaben.</summary>
        public Func<string, List<string>> EmailFieldSuggest;
        public bool EmailFieldMode;
        /// <summary>Gelernte IBANs zum getippten IBAN-Anfang (setzt das Programm).</summary>
        public Func<string, int, List<string>> IbanSuggest;
        /// <summary>Im IBAN-Feld: gelernte IBANs gleich zeigen.</summary>
        public bool IbanFieldMode;

        private List<BandItem> SuggestionItems()
        {
            var list = new List<BandItem>();
            if (EmailSuggest != null && !_engine.PasswordMode && !LockMode)
            {
                var mails = EmailFieldMode && EmailFieldSuggest != null ? EmailFieldSuggest(_engine.CurrentChunk) : EmailSuggest(_engine.CurrentChunk);
                foreach (var em in mails.Take(EmailFieldMode ? 4 : 2))
                {
                    string e = em;
                    list.Add(new BandItem { Label = "✉ " + e, Color = Color.FromArgb(52, 120, 246), Do = () => _engine.CompleteChunk(e) });
                }
            }
            if (IbanSuggest != null && !_engine.PasswordMode && !LockMode)
            {
                string part = _engine.CurrentIbanPart;
                string compact = IbanRules.Compact(part);
                bool afterLabel = _engine.AfterIbanLabel;
                // erst ab Ländercode plus Ziffer ("DE8"), oder "DE" in Großbuchstaben, oder im IBAN-Feld bzw. nach "IBAN:"
                bool show = IbanFieldMode || afterLabel || compact.Length >= 3 || (part.Length == 2 && part == part.ToUpperInvariant());
                if (show)
                    foreach (var ib in IbanSuggest(afterLabel && part.Length == 0 ? "" : part, IbanFieldMode ? 3 : 2))
                    {
                        string i = ib;
                        list.Add(new BandItem { Label = "🏦 " + i, Color = Color.FromArgb(28, 140, 96), Do = () => _engine.CompleteIban(i) });
                    }
            }
            foreach (var s in _engine.Suggestions())
            {
                string w = s;
                list.Add(new BandItem { Label = w, Do = () => _engine.PressSuggestion(w) });
            }
            return list;
        }

        private BandItem MacroItem(Macro m)
        {
            return new BandItem
            {
                Label = m.Name,
                Color = Macro.ColorValue(m.Color),
                Do = () => { CloseMacroBar(); var h = MacroChosen; if (h != null) h(m); }
            };
        }

        private List<BandItem> MacroItems(int slots)
        {
            var macros = MacroSource != null ? MacroSource() : null;
            var fixedItems = new List<BandItem>();
            var items = new List<BandItem>();

            string err = MacroErrorSource != null ? MacroErrorSource() : null;
            if (err != null)
            {
                items.Add(new BandItem { Label = "⚠ " + err, Dim = true });
                return items;
            }
            if (macros == null || macros.Count == 0)
            {
                items.Add(new BandItem { Label = L.T("Noch keine Makros: ✎ anlegen oder ● aufnehmen"), Dim = true });
                return items;
            }

            if (_macroGroup == null)
            {
                var shown = new HashSet<Macro>();
                foreach (var m in macros)
                    if (m.MatchesProgram(_fgProcess)) { items.Add(MacroItem(m)); shown.Add(m); }
                var groups = new List<string>();
                foreach (var m in macros)
                    if (m.Group.Length > 0 && !groups.Contains(m.Group)) groups.Add(m.Group);
                foreach (var g in groups)
                {
                    string gg = g;
                    items.Add(new BandItem
                    {
                        Label = g + "  ›", Bold = true,
                        Do = () => { _macroGroup = gg; _macroPage = 0; RefreshBand(); }
                    });
                }
                foreach (var m in macros)
                    if (m.Group.Length == 0 && !shown.Contains(m)) items.Add(MacroItem(m));
            }
            else
            {
                fixedItems.Add(new BandItem
                {
                    Label = "‹  " + _macroGroup, Bold = true,
                    Do = () => { _macroGroup = null; _macroPage = 0; RefreshBand(); }
                });
                foreach (var m in macros)
                    if (m.Group == _macroGroup) items.Add(MacroItem(m));
            }

            int free = slots - fixedItems.Count;
            var result = new List<BandItem>(fixedItems);
            if (items.Count <= free)
            {
                result.AddRange(items);
                return result;
            }
            int perPage = Math.Max(1, free - 1);
            int pages = (items.Count + perPage - 1) / perPage;
            if (_macroPage >= pages) _macroPage = 0;
            for (int i = _macroPage * perPage; i < Math.Min(items.Count, (_macroPage + 1) * perPage); i++) result.Add(items[i]);
            while (result.Count < slots - 1) result.Add(null);
            result.Add(new BandItem
            {
                Label = L.T("weiter  ") + (_macroPage + 1) + "/" + pages + "  ›", Bold = true,
                Do = () => { _macroPage = (_macroPage + 1) % pages; RefreshBand(); }
            });
            return result;
        }

        // ---------- Layout ----------

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            BuildLayout();
            Invalidate();
        }

        private List<List<KeyDef>> CurrentRows
        {
            get
            {
                return _engine.FnLayer ? _nav : _main;
            }
        }

        private static float RowUnits(List<List<KeyDef>> rows)
        {
            float max = 1;
            foreach (var r in rows)
            {
                float s = 0;
                foreach (var k in r) s += k.Width;
                max = Math.Max(max, s);
            }
            return max;
        }

        /// <summary>Sperrbildschirm: nur Tippen, keine Makros, kein Tresor, keine Einstellungen.</summary>
        public bool LockMode;

        private ToolKind[] SmallTools()
        {
            if (LockMode) return new[] { ToolKind.Dwell };
            if (_macroMode) return new[] { ToolKind.Rec, ToolKind.QuickSave, ToolKind.Edit };
            if (_recording) return new[] { ToolKind.Cancel, ToolKind.Lang, ToolKind.Dwell, ToolKind.Settings };
            return new[] { ToolKind.Lang, ToolKind.Vault, ToolKind.Dwell, ToolKind.Settings, ToolKind.Collapse, ToolKind.Close };
        }

        private void BuildLayout()
        {
            _targets.Clear();
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            float pad = Math.Max(6f, _settings.ResizeBorder);   // Rand bleibt frei zum Ziehen
            var rows = CurrentRows;
            int bandRows = Math.Max(1, Math.Min(2, _settings.SuggestionRows));
            int rowCount = rows.Count + bandRows;
            float w = ClientSize.Width - pad * 2;
            float h = ClientSize.Height - pad * 2;
            _rowH = h / rowCount;
            float tu = w / 15f;                      // Einheit für die Leiste
            float unit = w / RowUnits(rows);         // Einheit für die Tasten
            float gap = Math.Max(2f, Math.Min(6f, _rowH * 0.07f));

            UpdateFonts();

            float x = pad, y = pad;
            var small = SmallTools();
            float bandW;
            float inputW = 0f;   // das Getippte steht jetzt im ersten Vorschlagsfeld (rot)
            if (bandRows == 1)
            {
                AddTool(ToolKind.Esc, 0, x, y, tu, _rowH, gap); x += tu;
                if (inputW > 0) { AddTool(ToolKind.Input, 0, x, y, inputW, _rowH, gap); x += inputW; }
                bandW = w - tu - inputW - tu * 1.25f - tu * 0.75f * small.Length;
                _bandRect = new RectangleF(x, y, bandW, _rowH);
                AddSlots(x, y, bandW, 1, gap);
                x += bandW;
                AddTool(ToolKind.Macro, 0, x, y, tu * 1.25f, _rowH, gap); x += tu * 1.25f;
                foreach (var t in small) { AddTool(t, 0, x, y, tu * 0.75f, _rowH, gap); x += tu * 0.75f; }
            }
            else
            {
                AddTool(ToolKind.Esc, 0, x, y, tu, _rowH * 2, gap);
                x += tu;
                if (inputW > 0) { AddTool(ToolKind.Input, 0, x, y, inputW, _rowH * 2, gap); x += inputW; }
                int cols = (small.Length + 1) / 2;
                bandW = w - tu - inputW - tu * (1.25f + cols * 0.8f);
                _bandRect = new RectangleF(x, y, bandW, _rowH * 2);
                AddSlots(x, y, bandW, 2, gap);
                x += bandW;
                AddTool(ToolKind.Macro, 0, x, y, tu * 1.25f, _rowH * 2, gap); x += tu * 1.25f;
                for (int i = 0; i < small.Length; i++)
                {
                    // zwei Reihen; der letzte Knopf füllt die Reihe bis zum Rand
                    int col = i % cols, row = i / cols;
                    int span = i == small.Length - 1 ? cols - col : 1;
                    AddTool(small[i], 0, x + col * tu * 0.8f, y + row * _rowH, tu * 0.8f * span, _rowH, gap);
                }
            }

            // Tastenreihen
            y = pad + _rowH * bandRows;
            foreach (var row in rows)
            {
                float rowW = 0;
                foreach (var k in row) rowW += k.Width;
                x = pad + (w - rowW * unit) / 2;
                foreach (var k in row)
                {
                    float kw = k.Width * unit;
                    k.Rect = new RectangleF(x + gap / 2, y + gap / 2, kw - gap, _rowH - gap);
                    bool rep = k.Kind == KeyKind.Special && (k.Vk == InputSender.VK_BACK || k.Vk == InputSender.VK_DELETE
                        || (k.Vk >= InputSender.VK_LEFT && k.Vk <= InputSender.VK_DOWN));
                    _targets.Add(new Target { Key = k, Rect = k.Rect, Repeatable = rep });
                    x += kw;
                }
                y += _rowH;
            }

            float g = Math.Max(24f, _settings.ResizeBorder * 2f);
            _grip = new RectangleF(0, ClientSize.Height - g, g, g);   // unten links
        }

        private void AddSlots(float x, float y, float width, int bandRows, float gap)
        {
            int perRow = Math.Max(1, _settings.SuggestionCount);
            float sw = width / perRow;
            // Das getippte Wort (rot, erstes Feld) wird nie gekürzt: sein Feld wird so breit wie nötig,
            // die Vorschläge daneben teilen sich den Rest (bei wenig Platz eben weniger Vorschläge).
            float typedW = TypedSlotWidth(gap);
            int index = 0;
            for (int r = 0; r < bandRows; r++)
            {
                float rx = x, rest = width;
                int count = perRow;
                if (r == 0 && typedW > sw)
                {
                    float tw = Math.Min(width, typedW);
                    AddTool(ToolKind.Slot, index++, rx, y, tw, _rowH, gap);
                    rx += tw; rest -= tw;
                    count = (int)Math.Floor(rest / (sw * 0.75f));
                    count = Math.Max(0, Math.Min(perRow - 1, count));
                    float each = count > 0 ? rest / count : 0;
                    for (int c = 0; c < count; c++) AddTool(ToolKind.Slot, index++, rx + c * each, y, each, _rowH, gap);
                    continue;
                }
                for (int c = 0; c < count; c++)
                    AddTool(ToolKind.Slot, index++, rx + c * sw, y + r * _rowH, sw, _rowH, gap);
            }
        }

        /// <summary>Breite, die das getippte Wort in voller Schrift braucht (0 = kein getipptes Wort vorne).</summary>
        private float TypedSlotWidth(float gap)
        {
            if (_band.Count == 0 || _band[0] == null || !_band[0].TextColor.HasValue || _fontBand == null) return 0;
            string label = _band[0].Label ?? "";
            var font = ForText(_fontBand, label);
            using (var bold = new Font(font.FontFamily, font.Size, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                float w = TextRenderer.MeasureText(label, bold, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                return w + gap + 20f + (string.IsNullOrEmpty(_band[0].Badge) ? 0 : _fontBand.Size * 0.8f);
            }
        }

        private void AddTool(ToolKind kind, int index, float x, float y, float w, float h, float gap)
        {
            _targets.Add(new Target
            {
                Tool = kind, Index = index,
                Rect = new RectangleF(x + gap / 2, y + gap / 2, w - gap, h - gap)
            });
        }

        private void UpdateFonts()
        {
            float main = Math.Max(8f, _rowH * 0.36f);
            float band = Math.Max(8f, _rowH * _settings.SuggestionFontPercent / 100f);
            if (_fontMain != null && Math.Abs(_fontMain.Size - main) < 0.5f && Math.Abs(_fontBand.Size - band) < 0.5f) return;
            if (_fontMain != null) { _fontMain.Dispose(); _fontSmall.Dispose(); _fontTool.Dispose(); _fontBand.Dispose(); }
            _fontMain = new Font("Segoe UI", main, FontStyle.Regular, GraphicsUnit.Pixel);
            _fontSmall = new Font("Segoe UI", Math.Max(7f, _rowH * 0.2f), FontStyle.Regular, GraphicsUnit.Pixel);
            _fontTool = new Font("Segoe UI", Math.Max(8f, _rowH * 0.3f), FontStyle.Regular, GraphicsUnit.Pixel);
            _fontBand = new Font("Segoe UI", band, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // ---------- Zeichnen ----------

        private static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Text mittig zeichnen, bei Bedarf kleiner, damit er reinpasst.</summary>
        private static readonly Dictionary<float, Font> SymbolFonts = new Dictionary<float, Font>();

        /// <summary>Symbole wie ⌫ ⚙ ✎ gibt es sicher in "Segoe UI Symbol", nicht in jeder Schrift.</summary>
        private static Font ForText(Font font, string text)
        {
            bool sym = false;
            foreach (char c in text) if (c >= 0x2190 && c < 0x2C00) { sym = true; break; }
            if (!sym) return font;
            Font f;
            if (!SymbolFonts.TryGetValue(font.Size, out f))
            {
                if (SymbolFonts.Count > 40)
                {
                    foreach (var old in SymbolFonts.Values) old.Dispose();
                    SymbolFonts.Clear();
                }
                f = new Font("Segoe UI Symbol", font.Size, FontStyle.Regular, GraphicsUnit.Pixel);
                SymbolFonts[font.Size] = f;
            }
            return f;
        }

        private static void DrawFit(Graphics g, string text, Font font, Brush brush, RectangleF r, StringFormat sf, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            font = ForText(font, text);
            var style = bold ? FontStyle.Bold : font.Style;
            var size = g.MeasureString(text, font);
            float avail = Math.Max(4f, r.Width - 6f);
            if (size.Width <= avail && !bold) { g.DrawString(text, font, brush, r, sf); return; }
            float scale = Math.Min(1f, avail / Math.Max(1f, size.Width));
            using (var f = new Font(font.FontFamily, Math.Max(6f, font.Size * scale), style, GraphicsUnit.Pixel))
                g.DrawString(text, f, brush, r, sf);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(_theme.Background);
            if (_fontMain == null) return;

            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                foreach (var t in _targets)
                {
                    if (t.Key != null) DrawKey(g, t, center); else DrawTool(g, t, center);
                }

                // Statusmeldung über der Leiste
                if (_status != null && DateTime.UtcNow < _statusUntil)
                {
                    var r = RectangleF.Inflate(_bandRect, -2, -2);
                    using (var path = Rounded(r, 6))
                    using (var b = new SolidBrush(_theme.Accent)) g.FillPath(b, path);
                    using (var tb = new SolidBrush(Color.White)) DrawFit(g, _status, _fontBand, tb, r, center);
                }
            }

            // Einführung: dicker Rahmen um die erklärte Stelle
            if (TourHighlight.HasValue)
            {
                var hr = RectangleF.Inflate(TourHighlight.Value, 3, 3);
                using (var p = new Pen(Color.FromArgb(255, 170, 0), Math.Max(4f, _rowH * 0.09f)))
                using (var path = Rounded(hr, 8)) g.DrawPath(p, path);
            }

            // Aufnahme: roter Rahmen um die ganze Tastatur
            if (_recording)
                using (var p = new Pen(Color.FromArgb(220, 53, 69), 4f)) g.DrawRectangle(p, 2, 2, ClientSize.Width - 4, ClientSize.Height - 4);

            // Griff zum Größe ändern (unten links), gut sichtbar
            using (var pen = new Pen(_theme.Accent, 2.5f))
            {
                for (int i = 1; i <= 3; i++)
                {
                    float o = _grip.Width * i / 3.5f;
                    g.DrawLine(pen, _grip.Left + o, _grip.Bottom - 3, _grip.Left + 3, _grip.Bottom - o);
                }
            }

            // Verschieben läuft: Hinweis und Ring zum Absetzen
            if (_drag != DragMode.None)
            {
                using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    var r = RectangleF.Inflate(_bandRect, -2, -2);
                    using (var path = Rounded(r, 6))
                    using (var b = new SolidBrush(_theme.Locked)) g.FillPath(b, path);
                    string msg = _drag == DragMode.Held
                        ? L.T("✥ Verschieben: loslassen zum Absetzen · Esc/Rechtsklick bricht ab")
                        : L.T("✥ Verschieben: Maus ruhig halten oder klicken zum Absetzen · Esc/Rechtsklick bricht ab");
                    using (var tb = new SolidBrush(Color.White)) DrawFit(g, msg, _fontBand, tb, r, center);
                }
                if (_drag == DragMode.Dwell && _dwellDrag != null)
                {
                    var c = PointToClient(Cursor.Position);
                    float rad = Math.Max(18f, _rowH * 0.45f);
                    var rr = new RectangleF(c.X - rad, c.Y - rad, rad * 2, rad * 2);
                    using (var p = new Pen(Color.FromArgb(120, _theme.Text), 3f)) g.DrawEllipse(p, rr);
                    double f = _dwellDrag.DropProgress(DateTime.UtcNow);
                    if (f > 0) using (var p = new Pen(_theme.Locked, 6f)) g.DrawArc(p, rr, -90, (float)(360 * f));
                }
            }
        }

        /// <summary>Ring, der anzeigt, dass gleich das Verschieben startet.</summary>
        private void DrawDragRing(Graphics g, RectangleF r, double frac)
        {
            if (frac <= 0) return;
            frac = Math.Min(1, frac);
            float rad = Math.Min(r.Width, r.Height) * 0.36f;
            var c = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
            var rr = new RectangleF(c.X - rad, c.Y - rad, rad * 2, rad * 2);
            using (var p = new Pen(_theme.Locked, Math.Max(3f, _rowH * 0.07f))) g.DrawArc(p, rr, -90, (float)(360 * frac));
        }

        private Color KeyColor(Target t, Color normal)
        {
            if (Same(_flash, t) && DateTime.UtcNow < _flashUntil) return _theme.Accent;
            if (Same(_hover, t)) return _theme.KeyHover;
            return normal;
        }

        private void DrawBox(Graphics g, Target t, Color fill, Color? outline)
        {
            float rad = Math.Max(3f, Math.Min(t.Rect.Height, _rowH) * 0.12f);
            using (var path = Rounded(t.Rect, rad))
            using (var b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
                if (outline.HasValue)
                    using (var p = new Pen(outline.Value, Math.Max(2f, _rowH * 0.05f))) g.DrawPath(p, path);
            }
            DrawDwellProgress(g, t, rad);
        }

        private void DrawDwellProgress(Graphics g, Target t, float rad)
        {
            var now = DateTime.UtcNow;
            // langes Drücken: Ring füllt sich bis zum Verschieben
            if (_pressTarget != null && Same(_pressTarget, t))
            {
                double ms = (now - _pressAt).TotalMilliseconds;
                if (ms > PressRingDelayMs) DrawDragRing(g, t.Rect, (ms - PressRingDelayMs) / Math.Max(1, _settings.LongPressMs - PressRingDelayMs));
            }
            // langes Verweilen: nach dem Verweilbalken füllt sich der Ring bis zum Verschieben
            if (_settings.DwellEnabled && _dwellDragPossible && Same(_hover, t) && _drag == DragMode.None)
            {
                double ms = (now - _hoverSince).TotalMilliseconds;
                bool dragKey = IsDragKey(t);
                double from = dragKey ? 0 : _settings.DwellMs;
                double to = dragKey ? _settings.DwellMs : _settings.EffectiveDwellDragMs;
                if (ms > from) DrawDragRing(g, t.Rect, (ms - from) / Math.Max(1, to - from));
            }
            if (!_settings.DwellEnabled || !_armed || !Same(_hover, t) || t.Tool == ToolKind.Drag) return;
            double frac = (now - _hoverSince).TotalMilliseconds / _settings.DwellMs;
            if (frac <= 0) return;
            frac = Math.Min(1, frac);
            var r = t.Rect;
            float bh = Math.Max(3f, _rowH * 0.08f);
            using (var b = new SolidBrush(_theme.Accent))
                g.FillRectangle(b, r.X + rad / 2, r.Bottom - bh - 2, (float)((r.Width - rad) * frac), bh);
        }

        private Color? StateOutline(ModState s)
        {
            if (s == ModState.Once) return _theme.Accent;
            if (s == ModState.Locked) return _theme.Locked;
            return null;
        }

        private void DrawKey(Graphics g, Target t, StringFormat center)
        {
            var k = t.Key;
            Color fill = k.Kind == KeyKind.Char || k.Kind == KeyKind.Space ? _theme.Key : _theme.KeySpecial;
            Color? outline = null;
            if (k.Kind == KeyKind.Modifier) outline = StateOutline(_engine.GetMod(k.Mod));
            else if (k.Kind == KeyKind.AltGr) outline = StateOutline(_engine.AltGr);
            else if (k.Kind == KeyKind.Caps && _engine.Caps) outline = _theme.Locked;
            else if (k.Kind == KeyKind.FnToggle && _engine.FnLayer) outline = _theme.Locked;
            else if (k.Kind == KeyKind.Char && _engine.DeadPending != '\0')
            {
                string dp = _engine.DeadPending.ToString();
                if (k.Normal == dp || k.Shifted == dp || k.AltGr == dp) outline = _theme.Locked;   // Tottaste wartet
            }
            if (outline == null && k.Kind == KeyKind.Modifier && k.Mod == Modifiers.Shift && _engine.AutoShiftActive) outline = _theme.TextDim;

            var shown = KeyColor(t, fill);
            if (Physical != null && _settings.ShowPhysicalKeys)
            {
                double glow = Physical.Glow(k, DateTime.UtcNow, _settings.PhysicalGlowMs);
                if (glow > 0) shown = Blend(shown, PhysicalRed, glow);
            }
            DrawBox(g, t, shown, outline);

            using (var tb = new SolidBrush(_theme.Text))
            using (var db = new SolidBrush(_theme.TextDim))
            using (var ab = new SolidBrush(_theme.Accent))
            {
                if (k.Kind == KeyKind.Char)
                {
                    string main = _engine.OutputFor(k);
                    if (main.Length == 0) main = k.Normal;
                    var color = _engine.AltGr != ModState.Off && k.AltGr == null ? db : tb;
                    DrawFit(g, main, _fontMain, color, t.Rect, center);

                    // kleine Zweitbelegung oben links, AltGr unten rechts
                    bool caseKey = k.Normal.Length == 1 && char.IsLetter(k.Normal[0]) && k.Shifted.Length == 1 && char.IsUpper(k.Shifted[0]);
                    if (!caseKey && k.Shifted != k.Normal && _engine.AltGr == ModState.Off)
                        g.DrawString(_engine.Shift != ModState.Off ? k.Normal : k.Shifted, _fontSmall, db, t.Rect.X + 4, t.Rect.Y + 2);
                    if (k.AltGr != null && _engine.AltGr == ModState.Off)
                    {
                        var sz = g.MeasureString(k.AltGr, _fontSmall);
                        g.DrawString(k.AltGr, _fontSmall, ab, t.Rect.Right - sz.Width - 3, t.Rect.Bottom - sz.Height - 2);
                    }
                }
                else if (k.Kind == KeyKind.CaseToggle)
                {
                    int dir = _engine.CaseDirection;
                    if (dir == 0) DrawFit(g, "Aa", _fontMain, db, t.Rect, center);
                    else DrawCaseLabel(g, t.Rect, dir > 0, tb, center);
                }
                else
                {
                    string label = k.Label;
                    if (k.Kind == KeyKind.Space) label = KeyLayouts.Label("Leertaste");
                    var f = (k.Kind == KeyKind.Special || k.Kind == KeyKind.Drag) && label.Length == 1 ? _fontMain : _fontTool;
                    DrawFit(g, label, f, k.Kind == KeyKind.Space ? db : k.Kind == KeyKind.Drag ? ab : tb, t.Rect, center);
                    if (k.Kind == KeyKind.Modifier || k.Kind == KeyKind.AltGr)
                    {
                        var st = k.Kind == KeyKind.AltGr ? _engine.AltGr : _engine.GetMod(k.Mod);
                        if (st == ModState.Locked)
                            g.DrawString("fest", _fontSmall, db, t.Rect.X + 4, t.Rect.Y + 2);
                    }
                }
            }
        }

        /// <summary>"abc" mit Pfeil hoch (wird groß) oder "ABC" mit Pfeil runter (wird klein).</summary>
        private void DrawCaseLabel(Graphics g, RectangleF r, bool up, Brush brush, StringFormat center)
        {
            float pad = r.Width * 0.06f;
            var inner = RectangleF.Inflate(r, -pad, 0);
            float arrowW = Math.Min(inner.Width * 0.4f, r.Height * 0.7f);
            var textRect = new RectangleF(inner.X, r.Y, inner.Width - arrowW, r.Height);
            var arrowRect = new RectangleF(inner.Right - arrowW, r.Y, arrowW, r.Height);
            using (var f = new Font("Segoe UI", _fontMain.Size, FontStyle.Bold, GraphicsUnit.Pixel))
                DrawFit(g, _engine.CaseLabel, f, brush, textRect, center);
            DrawCaseArrow(g, arrowRect, up, brush);
        }

        /// <summary>Dicker Pfeil: nach oben = wird groß, nach unten = wird klein.</summary>
        private static void DrawCaseArrow(Graphics g, RectangleF r, bool up, Brush brush)
        {
            float h = r.Height * 0.72f;
            float w = Math.Min(r.Width * 0.92f, h * 0.9f);
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            float top = cy - h / 2, bottom = cy + h / 2;
            float headH = h * 0.52f, shaft = w * 0.44f;
            PointF[] pts;
            if (up)
            {
                float yHead = top + headH;
                pts = new[]
                {
                    new PointF(cx, top), new PointF(cx + w / 2, yHead), new PointF(cx + shaft / 2, yHead),
                    new PointF(cx + shaft / 2, bottom), new PointF(cx - shaft / 2, bottom), new PointF(cx - shaft / 2, yHead),
                    new PointF(cx - w / 2, yHead)
                };
            }
            else
            {
                float yHead = bottom - headH;
                pts = new[]
                {
                    new PointF(cx, bottom), new PointF(cx + w / 2, yHead), new PointF(cx + shaft / 2, yHead),
                    new PointF(cx + shaft / 2, top), new PointF(cx - shaft / 2, top), new PointF(cx - shaft / 2, yHead),
                    new PointF(cx - w / 2, yHead)
                };
            }
            g.FillPolygon(brush, pts);
        }

        private static Color Blend(Color a, Color b, float f)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));
        }

        private void DrawTool(Graphics g, Target t, StringFormat center)
        {
            if (t.Tool == ToolKind.Slot) { DrawSlot(g, t, center); return; }
            if (t.Tool == ToolKind.Input) { DrawInput(g, t); return; }

            string label = "";
            Color fill = _theme.KeySpecial;
            Color? outline = null;
            Color text = _theme.Text;
            Font font = _fontMain;
            var red = Color.FromArgb(220, 53, 69);
            switch (t.Tool)
            {
                case ToolKind.Drag:
                    label = "✥"; fill = _theme.Background; text = _theme.TextDim; break;
                case ToolKind.Fn:
                    label = "Fn"; if (_engine.FnLayer) outline = _theme.Locked; break;
                case ToolKind.Esc:
                    label = "Esc"; break;
                case ToolKind.Macro:
                    font = _fontTool;
                    if (_recording) { label = L.T("■ Stopp"); fill = red; text = Color.White; }
                    else if (_macroMode) { label = L.T("✕ Zurück"); outline = _theme.Accent; }
                    else label = L.T("Makros");
                    break;
                case ToolKind.Dwell:
                    label = "◷"; if (_settings.DwellEnabled) outline = _theme.Accent; else text = _theme.TextDim; break;
                case ToolKind.Settings:
                    label = "⚙"; break;
                case ToolKind.Collapse:
                    label = "▾"; break;
                case ToolKind.Close:
                    label = "✕";
                    if (DateTime.Now <= _closeArmedUntil) { fill = red; text = Color.White; }
                    break;
                case ToolKind.Rec:
                    label = "●"; text = red; break;
                case ToolKind.QuickSave:
                    label = "+"; break;
                case ToolKind.Edit:
                    label = "✎"; break;
                case ToolKind.Cancel:
                    label = "✕"; text = red; break;
                case ToolKind.Lang:
                    font = _fontTool; label = LanguageLabel != null ? LanguageLabel() : "DE"; break;
                case ToolKind.Vault:
                    label = "⚿"; if (VaultShown != null && VaultShown()) outline = _theme.Locked; break;
            }
            DrawBox(g, t, t.Tool == ToolKind.Drag ? fill : KeyColor(t, fill), outline);
            using (var b = new SolidBrush(text)) DrawFit(g, label, font, b, t.Rect, center);
        }

        private void DrawSlot(Graphics g, Target t, StringFormat center)
        {
            var item = t.Index < _band.Count ? _band[t.Index] : null;
            if (item == null)
            {
                using (var b = new SolidBrush(_theme.Suggestion)) g.FillRectangle(b, t.Rect.X, t.Rect.Bottom - 2, t.Rect.Width, 2);
                return;
            }
            Color fill = _theme.Suggestion;
            if (item.Color.HasValue) fill = Blend(_theme.Suggestion, item.Color.Value, _settings.Theme == "hell" ? 0.35f : 0.5f); // i18n-ok
            else if (_macroMode && !item.Dim) fill = _theme.Key;
            DrawBox(g, t, item.Dim ? fill : KeyColor(t, fill), null);
            var textColor = item.TextColor ?? (item.Dim ? _theme.TextDim : _theme.Text);
            if (item.TextColor.HasValue && _settings.Theme != "hell") textColor = Color.FromArgb(255, 90, 90);   // auf dunkel heller rot // i18n-ok
            string label = item.Label;
            // getipptes Wort: nie mit … kürzen, notfalls wird nur die Schrift kleiner (das Feld ist schon so breit wie möglich)
            using (var b = new SolidBrush(textColor))
            {
                if (item.TextColor.HasValue)
                    using (var full = new StringFormat(center) { Trimming = StringTrimming.None })
                        DrawFit(g, label, _fontBand, b, t.Rect, full, item.Bold);
                else DrawFit(g, label, _fontBand, b, t.Rect, center, item.Bold);
            }
            if (!string.IsNullOrEmpty(item.Badge))
                using (var f = new Font(_fontBand.FontFamily, Math.Max(7f, _fontBand.Size * 0.42f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var bb = new SolidBrush(Color.FromArgb(230, 140, 20)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Far })
                    g.DrawString(item.Badge, f, bb, RectangleF.Inflate(t.Rect, -4, -2), sf);
        }

        // ---------- Bedienung ----------

        /// <summary>Gleiche Taste, auch wenn das Layout neu berechnet wurde.</summary>
        private static bool Same(Target a, Target b)
        {
            if (a == null || b == null) return a == b;
            if (a.Key != null || b.Key != null) return a.Key == b.Key;
            return a.Tool == b.Tool && a.Index == b.Index;
        }

        private Target HitTest(Point p)
        {
            foreach (var t in _targets) if (t.Rect.Contains(p)) return t;
            return null;
        }

        private static bool IsDragKey(Target t)
        {
            return t != null && (t.Tool == ToolKind.Drag || (t.Key != null && t.Key.Kind == KeyKind.Drag));
        }

        private bool IsClickable(Target t)
        {
            if (t == null || IsDragKey(t)) return false;
            if (t.Tool == ToolKind.Slot)
            {
                var item = t.Index < _band.Count ? _band[t.Index] : null;
                return item != null && item.Do != null && !item.Dim;
            }
            return true;
        }

        private Edge ResizeEdgeAt(Point p)
        {
            if (_grip.Contains(p)) return Edge.Left | Edge.Bottom;
            int border = Math.Max(4, _settings.ResizeBorder);
            return Geometry.HitEdge(ClientSize, p, border, Math.Max(24, border * 2));
        }

        private static Cursor CursorFor(Edge e)
        {
            bool h = (e & (Edge.Left | Edge.Right)) != 0, v = (e & (Edge.Top | Edge.Bottom)) != 0;
            if (h && v) return ((e & Edge.Left) != 0) == ((e & Edge.Top) != 0) ? Cursors.SizeNWSE : Cursors.SizeNESW;
            if (h) return Cursors.SizeWE;
            if (v) return Cursors.SizeNS;
            return Cursors.Default;
        }

        private static bool IsVkDown(int vk)
        {
            try { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; } catch { return false; }
        }

        // ----- Verschieben -----

        private void StartDrag(DragMode mode)
        {
            var c = Cursor.Position;
            _drag = mode;
            _dragStartBounds = Bounds;
            _dragOffset = new Point(c.X - Left, c.Y - Top);
            _opacityBeforeDrag = Opacity;
            Opacity = Math.Min(Opacity, 0.6);
            _dwellDrag = mode == DragMode.Dwell ? new DwellDrag(c, DateTime.UtcNow, _settings.DropMs) : null;
            if (mode == DragMode.Held) Capture = true;
            _pressTarget = null;
            _hover = null;
            _armed = false;
            _dwellDragPossible = false;
            Cursor = Cursors.SizeAll;
            Invalidate();
        }

        private void MoveDrag()
        {
            var c = Cursor.Position;
            var want = new Rectangle(c.X - _dragOffset.X, c.Y - _dragOffset.Y, Width, Height);
            var area = Screen.FromPoint(c).WorkingArea;
            var r = Geometry.Clamp(want, area);
            if (r.Location != Location) Location = r.Location;
        }

        private void EndDrag(bool cancel)
        {
            if (_drag == DragMode.None) return;
            if (cancel) Bounds = _dragStartBounds;
            Opacity = _opacityBeforeDrag;
            if (_drag == DragMode.Held) Capture = false;
            _drag = DragMode.None;
            _dwellDrag = null;
            Cursor = Cursors.Default;
            if (!cancel)
            {
                var c = Cursor.Position;
                FitToScreen(c);
                SaveBounds();
                Log.Write("Abgesetzt bei Maus " + c + ", Tastatur " + Bounds + DpiInfo.Describe(c));
            }
            // die Taste unter dem Mauszeiger erst nach Verlassen wieder auslösen
            _hover = HitTest(PointToClient(Cursor.Position));
            _hoverSince = DateTime.UtcNow;
            _armed = false;
            _dwellDragPossible = false;
            ShowStatus(cancel ? L.T("Verschieben abgebrochen") : L.T("Position gespeichert"), 1200);
            Invalidate();
        }

        // ----- Maus -----

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                if (_drag != DragMode.None) EndDrag(true);
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (_drag == DragMode.Dwell) { EndDrag(false); return; }  // Klick setzt sofort ab

            var edge = ResizeEdgeAt(e.Location);
            if (edge != Edge.None)
            {
                _resizeEdge = edge;
                _resizeStartBounds = Bounds;
                _resizeStartMouse = Cursor.Position;
                Capture = true;
                return;
            }
            var t = HitTest(e.Location);
            if (t == null) return;
            if (IsDragKey(t)) { StartDrag(DragMode.Held); return; }
            _armed = false; // dieser Klick ersetzt den Verweilklick
            if (_settings.HoldToDrag)
            {
                // erst beim Loslassen tippen, damit langes Drücken stattdessen verschieben kann
                _pressTarget = t;
                _pressAt = DateTime.UtcNow;
                Capture = true;
            }
            else Activate(t);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag != DragMode.None) { MoveDrag(); Cursor = Cursors.SizeAll; return; }
            if (_resizeEdge != Edge.None)
            {
                var p = Cursor.Position;
                Bounds = Geometry.Resize(_resizeStartBounds, _resizeEdge, p.X - _resizeStartMouse.X, p.Y - _resizeStartMouse.Y, MinimumSize);
                return;
            }
            var edge = ResizeEdgeAt(e.Location);
            if (edge != Edge.None) Cursor = CursorFor(edge);
            else if (IsDragKey(HitTest(e.Location))) Cursor = Cursors.SizeAll;
            else Cursor = Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (_drag == DragMode.Held) { EndDrag(false); return; }
            if (_resizeEdge != Edge.None)
            {
                _resizeEdge = Edge.None;
                Capture = false;
                var r = Geometry.Fit(Bounds, Screen.FromRectangle(Bounds).WorkingArea, MinimumSize);
                if (r != Bounds) Bounds = r;
                SaveBounds();
                return;
            }
            if (_pressTarget != null)
            {
                var t = _pressTarget;
                _pressTarget = null;
                Capture = false;
                // nur auslösen, wenn man noch auf derselben Taste ist
                if (Same(HitTest(e.Location), t)) Activate(t);
                Invalidate();
            }
        }

        private void Activate(Target t)
        {
            if (!IsClickable(t)) return;
            _flash = t;
            _flashUntil = DateTime.UtcNow.AddMilliseconds(140);

            if (t.Key != null && t.Key.Kind == KeyKind.CaseToggle && !_engine.CanToggleCase)
            {
                ShowStatus(L.T("Kein Wort bekannt: Die Taste wirkt auf das letzte Wort, das mit dieser Tastatur getippt wurde"), 3500);
                Log.Write("Groß/Klein gedrückt, aber kein Wort bekannt");
                return;
            }
            if (t.Key != null && t.Key.Kind == KeyKind.FnToggle)
            {
                _engine.FnLayer = !_engine.FnLayer;
                _hover = null;
                BuildLayout();
                Invalidate();
                return;
            }
            if (t.Key != null && t.Key.Kind == KeyKind.LayoutToggle)
            {
                _latin = !_latin;
                ApplyLayout();
                ShowStatus(_latin ? "ABC" : "АБВ", 1000);
                return;
            }
            if (t.Key != null)
            {
                // Tresor-Suche: Buchstaben gehen in die Suche statt ins Programm
                if (KeyIntercept != null && KeyIntercept(t.Key)) { Invalidate(); return; }
                _engine.Press(t.Key);
            }
            else
            {
                switch (t.Tool)
                {
                    case ToolKind.Input:
                        ActivateInput();
                        break;
                    case ToolKind.Slot:
                        var item = _band[t.Index];
                        item.Do();
                        break;
                    case ToolKind.Fn:
                        _engine.FnLayer = !_engine.FnLayer;
                        _hover = null;
                        BuildLayout();
                        break;
                    case ToolKind.Esc:
                        if (KeyIntercept == null || !KeyIntercept(EscKey)) _engine.Press(EscKey);
                        break;
                    case ToolKind.Macro:
                        if (LockMode) break;
                        if (_recording) Raise(RecordStopRequested);
                        else if (_macroMode) CloseMacroBar();
                        else OpenMacroBar();
                        break;
                    case ToolKind.Dwell:
                        _settings.DwellEnabled = !_settings.DwellEnabled;
                        _settings.Save();
                        break;
                    case ToolKind.Settings:
                        Raise(SettingsRequested);
                        break;
                    case ToolKind.Collapse:
                        Raise(CollapseRequested);
                        break;
                    case ToolKind.Close:
                        // zweimal tippen, damit ein versehentlicher Treffer (Verweilklick) nicht gleich beendet
                        if (DateTime.Now <= _closeArmedUntil) { _closeArmedUntil = DateTime.MinValue; Raise(CloseRequested); }
                        else
                        {
                            _closeArmedUntil = DateTime.Now.AddSeconds(3);
                            Invalidate();
                            ShowStatus(L.T("Nochmal ✕ tippen, dann wird die Tastatur beendet"), 3000);
                            var ct = new Timer { Interval = 3100 };
                            ct.Tick += (o2, e2) => { ct.Stop(); ct.Dispose(); Invalidate(); };
                            ct.Start();
                        }
                        break;
                    case ToolKind.Rec:
                        CloseMacroBar();
                        Raise(RecordStartRequested);
                        break;
                    case ToolKind.QuickSave:
                        Raise(QuickSaveRequested);
                        break;
                    case ToolKind.Edit:
                        CloseMacroBar();
                        Raise(EditMacrosRequested);
                        break;
                    case ToolKind.Cancel:
                        Raise(RecordCancelRequested);
                        break;
                    case ToolKind.Lang:
                        Raise(LanguageCycleRequested);
                        break;
                    case ToolKind.Vault:
                        Raise(VaultToggleRequested);
                        break;
                }
            }
            Invalidate();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!Visible) return;
            var now = DateTime.UtcNow;

            if (++_topmostCounter >= 66) { _topmostCounter = 0; ReassertTopmost(); }
            // Eingabefeld: Strich blinkt, Klartext läuft ab
            if (_settings.ShowInputField && (now - _blinkAt).TotalMilliseconds >= 530)
            {
                _blinkAt = now; _blinkOn = !_blinkOn;
                foreach (var tt in _targets) if (tt.Key == null && tt.Tool == ToolKind.Input) { Invalidate(Rectangle.Ceiling(tt.Rect)); break; }
            }
            if (_captureExcluded && !Revealing) { UpdateCaptureExclusion(); RefreshBand(); Invalidate(); }

            bool redraw = _flash != null && now >= _flashUntil;
            if (redraw) _flash = null;
            if (_status != null && now >= _statusUntil) { _status = null; redraw = true; }
            if (Physical != null && _settings.ShowPhysicalKeys && (_physicalWasActive | (_physicalWasActive = Physical.Active(now, _settings.PhysicalGlowMs)))) redraw = true;

            // Verschieben läuft
            if (_drag != DragMode.None)
            {
                if (IsVkDown(0x1B) || (_drag == DragMode.Dwell && IsVkDown(0x02))) { EndDrag(true); return; }
                if (_drag == DragMode.Held && (Control.MouseButtons & MouseButtons.Left) == 0) { EndDrag(false); return; }
                MoveDrag();
                if (_drag == DragMode.Dwell)
                {
                    if (_dwellDrag.Update(Cursor.Position, now)) { EndDrag(false); return; }
                    if (_dwellDrag.TimedOut(now)) { EndDrag(true); return; }
                }
                Invalidate();
                return;
            }

            // Maustaste lange gedrückt: Verschieben starten
            if (_pressTarget != null)
            {
                if ((Control.MouseButtons & MouseButtons.Left) == 0) { _pressTarget = null; Capture = false; redraw = true; }
                else if (_settings.HoldToDrag && (now - _pressAt).TotalMilliseconds >= _settings.LongPressMs) { StartDrag(DragMode.Held); return; }
                else redraw = true;
            }

            Target cur = null;
            if (_resizeEdge == Edge.None && _pressTarget == null)
            {
                var p = PointToClient(Cursor.Position);
                if (ClientRectangle.Contains(p) && ResizeEdgeAt(p) == Edge.None) cur = HitTest(p);
            }
            if (!Same(cur, _hover))
            {
                _hover = cur;
                _hoverSince = now;
                _armed = IsClickable(cur);
                _dwellDragPossible = cur != null && (IsDragKey(cur) || (_settings.HoldToDrag && cur.Key != null && !cur.Repeatable));
                redraw = true;
            }
            else if (_settings.DwellEnabled && _hover != null)
            {
                double ms = (now - _hoverSince).TotalMilliseconds;
                if (_armed)
                {
                    redraw = true;
                    if (ms >= _settings.DwellMs)
                    {
                        var t = _hover;
                        Activate(t);
                        // Löschen und Pfeile wiederholen sich beim Weiter-Verweilen, alles andere erst nach Verlassen
                        if (t.Repeatable) _hoverSince = now; else _armed = false;
                    }
                }
                if (_dwellDragPossible && _hover != null && Visible)
                {
                    redraw = true;
                    double limit = IsDragKey(_hover) ? _settings.DwellMs : _settings.EffectiveDwellDragMs;
                    if (ms >= limit) { StartDrag(DragMode.Dwell); return; }
                }
            }
            if (redraw) Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_drag != DragMode.None) EndDrag(true);
            SaveBounds();
            base.OnFormClosing(e);
        }
    }
}
