using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Tastatur
{
    /// <summary>
    /// Beobachtet Fensterwechsel und Mausklicks außerhalb der Tastatur,
    /// damit die Wortvorhersage weiß, wann ein neues Wort beginnt.
    /// Ab Stufe 2/3 kommt hier die Passwortfeld-Erkennung (UI Automation) dazu.
    /// </summary>
    internal class FocusWatcher : IDisposable
    {
        /// <summary>Neues Vordergrundfenster (Hauptfenster).</summary>
        public event Action<IntPtr> ForegroundChanged;
        /// <summary>Klick außerhalb der Tastatur, mit dem Hauptfenster unter der Maus.</summary>
        public event Action<IntPtr> ClickOutside;

        public static IntPtr Root(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;
            IntPtr r = Native.GetAncestor(hwnd, Native.GA_ROOT);
            return r == IntPtr.Zero ? hwnd : r;
        }

        private readonly Native.WinEventProc _winEventProc;
        private readonly Native.LowLevelProc _mouseProc;
        private IntPtr _winHook, _mouseHook;
        private readonly HashSet<IntPtr> _ownWindows = new HashSet<IntPtr>();

        public FocusWatcher()
        {
            _winEventProc = OnWinEvent;
            _mouseProc = OnMouse;
        }

        public void AddOwnWindow(IntPtr h) { _ownWindows.Add(h); }

        public void Start()
        {
            _winHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, Native.GetModuleHandle(null), 0);
        }

        private static readonly uint OwnPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        /// <summary>
        /// Gehört das Fenster zur Tastatur selbst (Tastatur oder runder Knopf)?
        /// Früher über eine Liste von Fenster-Handles. Windows kann das Handle aber neu anlegen
        /// (z. B. beim Ändern der Deckkraft), dann galt jeder Klick auf die Tastatur als "Klick woanders"
        /// und das angefangene Wort wurde vergessen. Deshalb jetzt über Prozess und Fensterstil.
        /// </summary>
        private bool IsOwn(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            IntPtr root = Native.GetAncestor(hwnd, Native.GA_ROOT);
            if (root == IntPtr.Zero) root = hwnd;
            if (_ownWindows.Contains(hwnd) || _ownWindows.Contains(root)) return true;
            uint pid;
            Native.GetWindowThreadProcessId(root, out pid);
            if (pid != OwnPid) return false;
            int ex = Native.GetWindowLong(root, Native.GWL_EXSTYLE);
            return (ex & Native.WS_EX_NOACTIVATE) != 0;
        }

        private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (IsOwn(hwnd)) return;
            var h = ForegroundChanged;
            if (h != null) h(Root(hwnd));
        }

        private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN || msg == Native.WM_MBUTTONDOWN)
                {
                    var data = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    IntPtr w = Native.WindowFromPoint(data.pt);
                    if (!IsOwn(w))
                    {
                        var h = ClickOutside;
                        if (h != null) h(Root(w));
                    }
                }
            }
            return Native.CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (_winHook != IntPtr.Zero) Native.UnhookWinEvent(_winHook);
            if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
            _winHook = _mouseHook = IntPtr.Zero;
        }
    }
}
