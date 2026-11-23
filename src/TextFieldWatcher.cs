using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace Tastatur
{
    /// <summary>Was zuletzt als Textfeld mit Fokus erkannt wurde. Rechtecke in Bildschirm-Pixeln (Per-Monitor-DPI).</summary>
    internal class FieldInfo
    {
        public Rectangle Field;          // ganzes Textfeld (kann leer sein)
        public Rectangle Caret;          // Schreibmarke (leer, wenn unbekannt)
        public string Process = "";
        public string Kind = "";         // z. B. "Edit", "Document", "Caret"
        public string CaretSource = "";  // "UIA", "GUIThreadInfo" oder ""
        public DateTime At;
    }

    /// <summary>
    /// Fragt im Hintergrund (eigener Thread, MTA) etwa alle 250 ms über UI Automation ab, welches Textfeld
    /// den Fokus hat und wo die Schreibmarke ist. Als Rückfall für die Schreibmarke GetGUIThreadInfo.
    /// Der Fokus wird dabei nie verändert. Hängt eine Abfrage, merkt das die Tastatur am Alter des Ergebnisses
    /// und bleibt normal deckend.
    /// </summary>
    internal class TextFieldWatcher : IDisposable
    {
        private const int IntervalMs = 250;
        private const int UIA_TextPatternId = 10014;
        private const int UIA_BoundingRectanglePropertyId = 30001;
        private const int UIA_ProcessIdPropertyId = 30002;
        private const int UIA_ControlTypePropertyId = 30003;
        private const int UIA_IsTextPatternAvailablePropertyId = 30040;
        private const int UIA_IsValuePatternAvailablePropertyId = 30043;
        private const int UIA_ValueIsReadOnlyPropertyId = 30046;
        private const int UIA_EditControlTypeId = 50004;
        private const int UIA_DocumentControlTypeId = 50030;
        private const int TextUnit_Character = 0;
        private const int Endpoint_Start = 0;

        private static readonly int OwnPid = System.Diagnostics.Process.GetCurrentProcess().Id;

        private Thread _thread;
        private volatile bool _stop;
        private volatile bool _active;
        private volatile bool _wantCaret = true;
        private volatile bool _log;
        private volatile HashSet<string> _exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private volatile FieldInfo _latest;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Dictionary<int, string> _procNames = new Dictionary<int, string>();
        private string _lastLogged;
        private bool _uiaFailedLogged;

        /// <summary>Letztes Ergebnis, null = kein Textfeld (oder abgeschaltet).</summary>
        public FieldInfo Latest { get { return _latest; } }

        /// <summary>Vom UI-Thread bei jedem Tick: an/aus, Modus, Ausnahmen, Protokoll.</summary>
        public void Configure(bool active, bool caretMode, string exclusions, bool log)
        {
            _wantCaret = caretMode;
            _log = log;
            if (exclusions != _exclText) { _exclText = exclusions; _exclude = AutoFade.ParseExclusions(exclusions); }
            if (active && !_active) _wake.Set();
            _active = active;
            if (!active) _latest = null;
        }
        private string _exclText = "";

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Textfeld-Erkennung" };   // i18n-ok
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        private void Loop()
        {
            IUIAutomation uia = null;
            try { uia = (IUIAutomation)new CUIAutomation(); }
            catch (Exception e) { Log.Write("Textfeld-Erkennung: UI Automation nicht verfügbar (" + e.Message + "), nur Cursor über GetGUIThreadInfo"); }

            while (!_stop)
            {
                if (!_active) { _wake.WaitOne(1000); continue; }
                FieldInfo info = null;
                try { info = Query(uia); }
                catch (Exception e)
                {
                    if (!_uiaFailedLogged) { _uiaFailedLogged = true; Log.Write("Textfeld-Erkennung: " + e.GetType().Name + " " + e.Message); }
                }
                _latest = _active ? info : null;
                if (_log) LogChange(info);
                _wake.WaitOne(IntervalMs);
            }
            if (uia != null) try { Marshal.ReleaseComObject(uia); } catch { }
        }

        private FieldInfo Query(IUIAutomation uia)
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return null;
            uint fgPid;
            uint fgThread = Native.GetWindowThreadProcessId(fg, out fgPid);
            if (fgPid == OwnPid) return null;
            string proc = ProcessName((int)fgPid);
            if (_exclude.Contains(proc)) return null;

            var info = new FieldInfo { Process = proc, At = DateTime.UtcNow };
            bool isText = false;

            if (uia != null)
            {
                IUIAutomationElement el = null;
                try
                {
                    el = uia.GetFocusedElement();
                    if (el != null)
                    {
                        int pid = AsInt(Prop(el, UIA_ProcessIdPropertyId));
                        if (pid == OwnPid) return null;
                        if (pid > 0 && pid != fgPid) { string p2 = ProcessName(pid); if (_exclude.Contains(p2)) return null; info.Process = p2; }
                        int ct = AsInt(Prop(el, UIA_ControlTypePropertyId));
                        bool hasText = AsBool(Prop(el, UIA_IsTextPatternAvailablePropertyId));
                        bool hasValue = AsBool(Prop(el, UIA_IsValuePatternAvailablePropertyId));
                        bool readOnly = hasValue && AsBool(Prop(el, UIA_ValueIsReadOnlyPropertyId));
                        // Edit: Eingabefeld. Document nur, wenn beschreibbar gemeldet, sonst wäre z. B. jede Webseite ein "Textfeld".
                        if (ct == UIA_EditControlTypeId) isText = !readOnly;
                        else if (ct == UIA_DocumentControlTypeId) isText = hasValue ? !readOnly : false;
                        else isText = hasText && hasValue && !readOnly;
                        info.Kind = ct == UIA_EditControlTypeId ? "Edit" : ct == UIA_DocumentControlTypeId ? "Document" : "ControlType " + ct;   // i18n-ok

                        var rects = AutoFade.RectsFromDoubles(Prop(el, UIA_BoundingRectanglePropertyId) as double[]);
                        if (rects.Count > 0 && AutoFade.Plausible(rects[0])) info.Field = rects[0];

                        if (hasText)
                        {
                            Rectangle c = CaretFromUia(el);
                            if (!c.IsEmpty)
                            {
                                info.Caret = c; info.CaretSource = "UIA";
                                // Dokumente ohne ValuePattern (z. B. Word, Editor): eine sichtbare Schreibmarke heißt beschreibbar
                                if (!isText && ct == UIA_DocumentControlTypeId && !hasValue) isText = true;
                            }
                        }
                    }
                }
                finally { if (el != null) try { Marshal.ReleaseComObject(el); } catch { } }
            }

            // Rückfall: Schreibmarke klassischer Windows-Programme
            if (info.Caret.IsEmpty && (_wantCaret || uia == null))
            {
                Rectangle c = CaretFromGuiThread(fgThread);
                if (!c.IsEmpty)
                {
                    info.Caret = c; info.CaretSource = "GUIThreadInfo";
                    if (uia == null) { isText = true; info.Kind = "Caret"; }   // i18n-ok
                }
            }

            return isText ? info : null;
        }

        private static Rectangle CaretFromUia(IUIAutomationElement el)
        {
            object pat = null;
            IUIAutomationTextRangeArray sel = null;
            IUIAutomationTextRange range = null;
            try
            {
                pat = el.GetCurrentPattern(UIA_TextPatternId);
                var tp = pat as IUIAutomationTextPattern;
                if (tp == null) return Rectangle.Empty;
                sel = tp.GetSelection();
                if (sel == null || sel.Length < 1) return Rectangle.Empty;
                range = sel.GetElement(0);
                if (range == null) return Rectangle.Empty;

                // markierter Text: dessen Rechtecke; leere Auswahl (normaler Cursor) liefert oft nichts
                var rects = AutoFade.RectsFromDoubles(range.GetBoundingRectangles());
                if (rects.Count > 0) return Union(rects);

                // Zeichen hinter dem Cursor
                var r2 = range.Clone();
                try
                {
                    r2.ExpandToEnclosingUnit(TextUnit_Character);
                    rects = AutoFade.RectsFromDoubles(r2.GetBoundingRectangles());
                    if (rects.Count > 0) return AutoFade.CaretFromChar(rects[0], false);
                }
                finally { Marshal.ReleaseComObject(r2); }

                // Cursor am Textende: Zeichen davor
                var r3 = range.Clone();
                try
                {
                    if (r3.MoveEndpointByUnit(Endpoint_Start, TextUnit_Character, -1) != 0)
                    {
                        rects = AutoFade.RectsFromDoubles(r3.GetBoundingRectangles());
                        if (rects.Count > 0) return AutoFade.CaretFromChar(rects[rects.Count - 1], true);
                    }
                }
                finally { Marshal.ReleaseComObject(r3); }
                return Rectangle.Empty;
            }
            catch { return Rectangle.Empty; }
            finally
            {
                if (range != null) try { Marshal.ReleaseComObject(range); } catch { }
                if (sel != null) try { Marshal.ReleaseComObject(sel); } catch { }
                if (pat != null && Marshal.IsComObject(pat)) try { Marshal.ReleaseComObject(pat); } catch { }
            }
        }

        private static Rectangle Union(List<Rectangle> rects)
        {
            Rectangle u = rects[0];
            for (int i = 1; i < rects.Count; i++) u = Rectangle.Union(u, rects[i]);
            return AutoFade.Plausible(u) ? u : Rectangle.Empty;
        }

        private static Rectangle CaretFromGuiThread(uint thread)
        {
            try
            {
                var gi = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
                if (!GetGUIThreadInfo(thread, ref gi) || gi.hwndCaret == IntPtr.Zero) return Rectangle.Empty;
                var tl = new Native.POINT { X = gi.rcCaret.Left, Y = gi.rcCaret.Top };
                var br = new Native.POINT { X = gi.rcCaret.Right, Y = gi.rcCaret.Bottom };
                if (!ClientToScreen(gi.hwndCaret, ref tl) || !ClientToScreen(gi.hwndCaret, ref br)) return Rectangle.Empty;
                var r = Rectangle.FromLTRB(tl.X, tl.Y, Math.Max(br.X, tl.X + 1), Math.Max(br.Y, tl.Y + 1));
                return AutoFade.Plausible(r) ? r : Rectangle.Empty;
            }
            catch { return Rectangle.Empty; }
        }

        private static object Prop(IUIAutomationElement el, int id)
        {
            try { return el.GetCurrentPropertyValue(id); } catch { return null; }
        }
        private static int AsInt(object o) { return o is int ? (int)o : 0; }
        private static bool AsBool(object o) { return o is bool && (bool)o; }

        private string ProcessName(int pid)
        {
            string n;
            if (_procNames.TryGetValue(pid, out n)) return n;
            try { using (var p = System.Diagnostics.Process.GetProcessById(pid)) n = p.ProcessName.ToLowerInvariant(); }
            catch { n = ""; }
            if (_procNames.Count > 200) _procNames.Clear();
            _procNames[pid] = n;
            return n;
        }

        private void LogChange(FieldInfo f)
        {
            string s = f == null ? "kein Textfeld" : f.Process + " " + f.Kind + " Feld " + f.Field + " Cursor " + (f.Caret.IsEmpty ? "unbekannt" : f.Caret + " (" + f.CaretSource + ")");   // i18n-ok (nur Protokoll)
            // Cursor wandert beim Tippen: nur Programm, Art und Feld vergleichen, sonst wird das Protokoll voll
            string key = f == null ? "-" : f.Process + "|" + f.Kind + "|" + f.Field + "|" + f.CaretSource;
            if (key == _lastLogged) return;
            _lastLogged = key;
            Log.Write("Textfeld-Erkennung: " + s);
        }

        public void Dispose()
        {
            _stop = true;
            _active = false;
            _wake.Set();
        }

        // ---------- Windows ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT rcCaret;
        }

        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Native.POINT p);

        // ---------- UI Automation (nur die benötigten Teile, Reihenfolge wie in UIAutomationClient.h) ----------

        [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
        private class CUIAutomation { }

        [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomation
        {
            void _CompareElements();
            void _CompareRuntimeIds();
            void _GetRootElement();
            void _ElementFromHandle();
            void _ElementFromPoint();
            IUIAutomationElement GetFocusedElement();
        }

        [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationElement
        {
            void _SetFocus();
            void _GetRuntimeId();
            void _FindFirst();
            void _FindAll();
            void _FindFirstBuildCache();
            void _FindAllBuildCache();
            void _BuildUpdatedCache();
            [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int propertyId);
            void _GetCurrentPropertyValueEx();
            void _GetCachedPropertyValue();
            void _GetCachedPropertyValueEx();
            void _GetCurrentPatternAs();
            void _GetCachedPatternAs();
            [return: MarshalAs(UnmanagedType.IUnknown)] object GetCurrentPattern(int patternId);
        }

        [ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationTextPattern
        {
            void _RangeFromPoint();
            void _RangeFromChild();
            IUIAutomationTextRangeArray GetSelection();
        }

        [ComImport, Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationTextRangeArray
        {
            int Length { get; }
            IUIAutomationTextRange GetElement(int index);
        }

        [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IUIAutomationTextRange
        {
            IUIAutomationTextRange Clone();
            void _Compare();
            void _CompareEndpoints();
            void ExpandToEnclosingUnit(int textUnit);
            void _FindAttribute();
            void _FindText();
            void _GetAttributeValue();
            [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)] double[] GetBoundingRectangles();
            void _GetEnclosingElement();
            void _GetText();
            void _Move();
            int MoveEndpointByUnit(int endpoint, int unit, int count);
        }
    }
}
