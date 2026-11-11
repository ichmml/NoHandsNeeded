using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Tastatur
{
    /// <summary>Kleines Protokoll in NoHandsNeeded-Daten\protokoll.txt, damit man Probleme nachvollziehen kann.</summary>
    internal static class Log
    {
        private const long MaxBytes = 200 * 1024;

        public static string FilePath
        {
            get { return Settings.DataDir == null ? null : Path.Combine(Settings.DataDir, "protokoll.txt"); }
        }

        public static void Write(string text)
        {
            try
            {
                string p = FilePath;
                if (p == null) return;
                if (File.Exists(p) && new FileInfo(p).Length > MaxBytes)
                {
                    string old = p + ".alt";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(p, old);
                }
                File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }
    }

    internal static class HotkeyIds
    {
        public const int Show = 2, Macros = 3, Language = 4, MacroBase = 100;
    }

    internal class HotkeyStatus
    {
        public int Id;
        public string Name;         // z. B. L.T("Makroleiste öffnen")
        public string Combo;        // wie eingetragen bzw. normalisiert
        public bool Ok;
        public string Reason;       // bei Fehler
    }

    /// <summary>Was registriert werden soll. Ohne Windows-Aufrufe, damit testbar.</summary>
    internal class HotkeyRequest
    {
        public int Id;
        public string Name;
        public string Combo;
        public Action Action;
        public ushort Vk;
        public uint Mods;
        public string Normalized;
    }

    internal static class HotkeyPlan
    {
        /// <summary>
        /// Prüft und übersetzt die gewünschten Kürzel. resolve liefert die virtuelle Taste (0 = gibt es nicht).
        /// </summary>
        public static List<HotkeyRequest> Build(List<HotkeyRequest> wanted, Func<KeyCombo, Tuple<ushort, Modifiers>> resolve, List<HotkeyStatus> failed)
        {
            var ok = new List<HotkeyRequest>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<int>();
            foreach (var w in wanted)
            {
                var k = KeyCombo.Parse(w.Combo);
                if (k == null) { failed.Add(Fail(w, L.T("unbekannte Taste"))); continue; }
                string norm = k.ToString();
                if (!ids.Add(w.Id)) { failed.Add(Fail(w, L.T("interne Nummer doppelt"))); continue; }
                if (!used.Add(norm)) { failed.Add(Fail(w, L.T("doppelt vergeben"))); continue; }
                var r = resolve(k);
                if (r.Item1 == 0) { failed.Add(Fail(w, L.T("Taste gibt es im Tastaturlayout nicht"))); continue; }
                uint mods = Native.MOD_NOREPEAT;
                var all = k.Mods | r.Item2;
                if ((all & Modifiers.Alt) != 0) mods |= Native.MOD_ALT;
                if ((all & Modifiers.Ctrl) != 0) mods |= Native.MOD_CONTROL;
                if ((all & Modifiers.Shift) != 0) mods |= Native.MOD_SHIFT;
                if ((all & Modifiers.Win) != 0) mods |= Native.MOD_WIN;
                w.Vk = r.Item1;
                w.Mods = mods;
                w.Normalized = norm;
                ok.Add(w);
            }
            return ok;
        }

        private static HotkeyStatus Fail(HotkeyRequest w, string reason)
        {
            return new HotkeyStatus { Id = w.Id, Name = w.Name, Combo = w.Combo, Ok = false, Reason = reason };
        }

        public static string Win32Reason(int code)
        {
            switch (code)
            {
                case 1409: return L.T("schon von einem anderen Programm belegt (Fehler 1409)");
                case 0: return L.T("von Windows abgelehnt");
                default: return L.T("Windows-Fehler ") + code;
            }
        }
    }

    /// <summary>
    /// Globale Tastenkürzel über RegisterHotKey. Ein unsichtbares Nachrichtenfenster empfängt WM_HOTKEY,
    /// dadurch ändert sich nie der Fokus.
    /// </summary>
    internal class HotkeyManager : NativeWindow, IDisposable
    {
        private readonly Dictionary<int, HotkeyRequest> _registered = new Dictionary<int, HotkeyRequest>();
        private readonly List<HotkeyRequest> _wanted = new List<HotkeyRequest>();
        private bool _suspended;

        public List<HotkeyStatus> Status { get; private set; }

        /// <summary>Wird bei jedem ausgelösten Kürzel gemeldet (für "Kürzel testen").</summary>
        public event Action<HotkeyRequest> Fired;

        public HotkeyManager()
        {
            Status = new List<HotkeyStatus>();
            CreateHandle(new CreateParams { Caption = "NoHandsNeeded-Kuerzel", Parent = new IntPtr(-3) }); // HWND_MESSAGE
            Log.Write("Kürzel-Fenster angelegt, Handle " + Handle);
        }

        public void Add(int id, string name, string combo, Action action)
        {
            if (string.IsNullOrWhiteSpace(combo)) return;
            _wanted.Add(new HotkeyRequest { Id = id, Name = name, Combo = combo.Trim(), Action = action });
        }

        public void ClearWanted() { _wanted.Clear(); }

        private static Tuple<ushort, Modifiers> ResolveOnWindows(KeyCombo k)
        {
            Modifiers extra;
            ushort vk = k.ResolveVk(out extra);
            return Tuple.Create(vk, extra);
        }

        /// <summary>Alles neu registrieren. Ergebnis steht danach in Status.</summary>
        public List<HotkeyStatus> Apply()
        {
            UnregisterAll();
            var status = new List<HotkeyStatus>();
            if (_suspended) { Status = status; return status; }

            var plan = HotkeyPlan.Build(new List<HotkeyRequest>(_wanted), ResolveOnWindows, status);
            foreach (var f in status) Log.Write("Kürzel NICHT aktiv: " + f.Combo + " → " + f.Name + ": " + f.Reason);
            foreach (var r in plan)
            {
                bool ok = Native.RegisterHotKey(Handle, r.Id, r.Mods, r.Vk);
                int err = ok ? 0 : Marshal.GetLastWin32Error();
                if (ok)
                {
                    _registered[r.Id] = r;
                    status.Add(new HotkeyStatus { Id = r.Id, Name = r.Name, Combo = r.Normalized, Ok = true });
                    Log.Write("Kürzel aktiv: " + r.Normalized + " → " + r.Name + " (Taste 0x" + r.Vk.ToString("X2") + ", Mods 0x" + r.Mods.ToString("X") + ")");
                }
                else
                {
                    string reason = HotkeyPlan.Win32Reason(err);
                    status.Add(new HotkeyStatus { Id = r.Id, Name = r.Name, Combo = r.Normalized, Ok = false, Reason = reason });
                    Log.Write("Kürzel NICHT aktiv: " + r.Normalized + " → " + r.Name + ": " + reason);
                }
            }
            Status = status;
            return status;
        }

        private void UnregisterAll()
        {
            foreach (var id in _registered.Keys) Native.UnregisterHotKey(Handle, id);
            _registered.Clear();
        }

        /// <summary>Während man ein Kürzel aufnimmt, dürfen die alten nicht abfangen.</summary>
        public void Suspend() { _suspended = true; UnregisterAll(); }
        public List<HotkeyStatus> Resume() { _suspended = false; return Apply(); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                HotkeyRequest r;
                if (_registered.TryGetValue(id, out r))
                {
                    Log.Write("Kürzel ausgelöst: " + r.Normalized + " → " + r.Name);
                    var f = Fired; if (f != null) f(r);
                    try { if (r.Action != null) r.Action(); }
                    catch (Exception e) { Log.Write("Fehler beim Ausführen von " + r.Name + ": " + e.Message); }
                }
                else Log.Write("WM_HOTKEY mit unbekannter Nummer " + id);
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            UnregisterAll();
            DestroyHandle();
        }
    }

    /// <summary>
    /// Fängt alle Tastendrücke systemweit ab (WH_KEYBOARD_LL). Funktioniert unabhängig davon, welches
    /// Fenster den Fokus hat, und sieht auch Tasten wie F13 bis F24 vom Quadstick.
    /// </summary>
    internal class KeyboardHook : IDisposable
    {
        public const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        /// <summary>vk, gedrückt?, von Software gesendet? Rückgabe true = Taste schlucken.</summary>
        public Func<ushort, bool, bool, bool> OnKey;
        /// <summary>Nur beobachten: vk, gedrückt?, eigene Eingabe der Tastatur?, Scancode.</summary>
        public Action<ushort, bool, bool, uint> Observe;

        private readonly Native.LowLevelProc _proc;
        private IntPtr _hook;

        public KeyboardHook()
        {
            _proc = Callback;
            try { _hook = Native.SetWindowsHookEx(WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0); }
            catch (Exception e) { Log.Write("Tastatur-Hook: " + e.Message); }
            if (_hook == IntPtr.Zero) Log.Write("Tastatur-Hook konnte nicht gesetzt werden, Fehler " + Marshal.GetLastWin32Error());
        }

        public bool Active { get { return _hook != IntPtr.Zero; } }

        private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && (OnKey != null || Observe != null))
            {
                int msg = wParam.ToInt32();
                var d = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                if (down || up)
                {
                    bool injected = (d.flags & 0x10) != 0;
                    bool swallow = false;
                    if (Observe != null)
                        try { Observe((ushort)d.vkCode, down, d.dwExtraInfo == InputSender.OwnMarker, d.scanCode); } catch { }
                    if (OnKey != null)
                        try { swallow = OnKey((ushort)d.vkCode, down, injected); } catch { }
                    if (swallow) return new IntPtr(1);
                }
            }
            return Native.CallNextHookEx(_hook, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    /// <summary>Merkt sich gedrückte Sondertasten und baut daraus eine Kombi. Ohne Windows testbar.</summary>
    internal class ComboTracker
    {
        private readonly HashSet<ushort> _down = new HashSet<ushort>();

        public static bool IsModifierVk(ushort vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;
        }

        public Modifiers Current
        {
            get
            {
                var m = Modifiers.None;
                foreach (var v in _down)
                {
                    if (v == 0x11 || v == 0xA2 || v == 0xA3) m |= Modifiers.Ctrl;
                    else if (v == 0x12 || v == 0xA4 || v == 0xA5) m |= Modifiers.Alt;
                    else if (v == 0x10 || v == 0xA0 || v == 0xA1) m |= Modifiers.Shift;
                    else if (v == 0x5B || v == 0x5C) m |= Modifiers.Win;
                }
                return m;
            }
        }

        /// <summary>Taste verarbeiten. Liefert eine fertige Kombi, sobald eine normale Taste gedrückt wird.</summary>
        public KeyCombo Feed(ushort vk, bool down, Func<ushort, char> vkToChar = null)
        {
            if (IsModifierVk(vk))
            {
                if (down) _down.Add(vk); else _down.Remove(vk);
                return null;
            }
            if (!down) return null;
            return KeyCombo.FromVk(vk, Current, vkToChar);
        }
    }

    /// <summary>"Drück jetzt die Taste…": erkennt den Tastendruck systemweit, zeigt ihn an, ein Klick übernimmt.</summary>
    internal class KeyCaptureDialog : Form
    {
        public string Result { get; private set; }
        private readonly Label _label, _found;
        private readonly Button _ok;
        private readonly ComboTracker _tracker = new ComboTracker();
        private KeyboardHook _hook;
        private ushort _swallowUp;

        public KeyCaptureDialog(string what)
        {
            DpiInfo.Attach(this);
            Text = L.T("Kürzel aufnehmen");
            Font = new Font("Segoe UI", 14f);
            Size = new Size(620, 400);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            MinimizeBox = MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;

            _label = new Label
            {
                Dock = DockStyle.Top, Height = 110, TextAlign = ContentAlignment.MiddleCenter,
                Text = what + L.T("\nDrück jetzt die Taste oder Tastenkombination (z. B. am Quadstick).")
            };
            _found = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 24f, FontStyle.Bold), Text = "…"
            };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 76, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(10) };
            _ok = new Button { Text = L.T("Übernehmen"), Width = 200, Height = 56, Enabled = false, TabStop = false };
            var again = new Button { Text = L.T("Nochmal"), Width = 170, Height = 56, TabStop = false };
            var cancel = new Button { Text = L.T("Abbrechen"), Width = 170, Height = 56, TabStop = false };
            _ok.Click += (o, e) => { DialogResult = DialogResult.OK; Close(); };
            again.Click += (o, e) => { Result = null; _found.Text = "…"; _ok.Enabled = false; };
            cancel.Click += (o, e) => { Result = null; DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(_ok); buttons.Controls.Add(again); buttons.Controls.Add(cancel);
            Controls.Add(_found);
            Controls.Add(_label);
            Controls.Add(buttons);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _hook = new KeyboardHook { OnKey = OnKey };
            if (!_hook.Active) _found.Text = L.T("Tastatur-Hook fehlt, siehe protokoll.txt");
        }

        private bool OnKey(ushort vk, bool down, bool injected)
        {
            if (!down && vk == _swallowUp) { _swallowUp = 0; return true; }
            var k = _tracker.Feed(vk, down, KeyCombo.VkToCharOnWindows);
            if (k == null) return false;
            Result = k.ToString();
            _found.Text = L.T("Erkannt: ") + Result;
            _ok.Enabled = true;
            Log.Write("Aufnahme erkannt: " + Result + (injected ? " (von Software gesendet)" : ""));
            _swallowUp = vk;
            return true; // nicht ans aktive Programm weitergeben (z. B. Alt+F4)
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_hook != null) _hook.Dispose();
            base.OnFormClosed(e);
        }
    }

    /// <summary>Zeigt jeden ankommenden Tastendruck und jedes ausgelöste Kürzel. Zum Prüfen der Quadstick-Belegung.</summary>
    internal class KeyTestForm : Form
    {
        private readonly ListBox _list;
        private readonly ComboTracker _tracker = new ComboTracker();
        private KeyboardHook _hook;
        private readonly HotkeyManager _hotkeys;
        private readonly Action<HotkeyRequest> _onFired;

        public KeyTestForm(HotkeyManager hotkeys, List<HotkeyStatus> status)
        {
            DpiInfo.Attach(this);
            _hotkeys = hotkeys;
            Text = L.T("Kürzel testen");
            Font = new Font("Segoe UI", 13f);
            Size = new Size(760, 600);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;

            var info = new Label
            {
                Dock = DockStyle.Top, Height = 150, Padding = new Padding(10),
                Text = L.T("Drück jetzt die Taste am Quadstick.\n") +
                       L.T("Erscheint unten \"Taste angekommen\", sendet der Quadstick die Taste.\n") +
                       L.T("Erscheint zusätzlich \"Kürzel ausgelöst\", funktioniert alles.\n\n") +
                       L.T("Aktiv: ") + Summary(status)
            };
            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            var close = new Button { Text = L.T("Schließen"), Dock = DockStyle.Bottom, Height = 56 };
            close.Click += (o, e) => Close();
            Controls.Add(_list);
            Controls.Add(info);
            Controls.Add(close);

            _onFired = r => Add(L.T("→ Kürzel ausgelöst: ") + r.Normalized + " (" + r.Name + ")");
            _hotkeys.Fired += _onFired;
        }

        private static string Summary(List<HotkeyStatus> status)
        {
            var parts = new List<string>();
            foreach (var s in status) parts.Add(s.Combo + (s.Ok ? " ✓" : " ✗"));
            return parts.Count == 0 ? L.T("keine Kürzel eingetragen") : string.Join(",  ", parts);
        }

        private void Add(string text)
        {
            _list.Items.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "   " + text);
            if (_list.Items.Count > 200) _list.Items.RemoveAt(_list.Items.Count - 1);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _hook = new KeyboardHook
            {
                OnKey = (vk, down, injected) =>
                {
                    var k = _tracker.Feed(vk, down, KeyCombo.VkToCharOnWindows);
                    if (k != null) Add(L.T("Taste angekommen: ") + k + (injected ? L.T("  (von Software)") : ""));
                    return false;
                }
            };
            if (!_hook.Active) Add(L.T("Tastatur-Hook konnte nicht gesetzt werden"));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _hotkeys.Fired -= _onFired;
            if (_hook != null) _hook.Dispose();
            base.OnFormClosed(e);
        }
    }
}
