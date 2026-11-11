using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Tastatur
{
    /// <summary>Schickt Tastendrücke per SendInput an das aktive Fenster.</summary>
    internal static class InputSender
    {
        public const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10,
            VK_CONTROL = 0x11, VK_MENU = 0x12, VK_ESCAPE = 0x1B, VK_SPACE = 0x20,
            VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24,
            VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28,
            VK_INSERT = 0x2D, VK_DELETE = 0x2E, VK_LWIN = 0x5B, VK_APPS = 0x5D,
            VK_LSHIFT = 0xA0, VK_LCONTROL = 0xA2, VK_LMENU = 0xA4, VK_F1 = 0x70;

        private static readonly int InputSize = Marshal.SizeOf(typeof(Native.INPUT));

        private static bool IsExtended(ushort vk)
        {
            switch (vk)
            {
                case VK_LEFT: case VK_UP: case VK_RIGHT: case VK_DOWN:
                case VK_PRIOR: case VK_NEXT: case VK_END: case VK_HOME:
                case VK_INSERT: case VK_DELETE: case VK_LWIN: case VK_APPS:
                    return true;
            }
            return false;
        }

        /// <summary>Kennung in dwExtraInfo: So erkennt der Tastatur-Hook eigene Eingaben und zeigt sie nicht rot an.</summary>
        public static readonly IntPtr OwnMarker = new IntPtr(0x54535441);   // "ATST"

        private static Native.INPUT Key(ushort vk, bool up)
        {
            var i = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            i.u.ki.dwExtraInfo = OwnMarker;
            i.u.ki.wVk = vk;
            i.u.ki.dwFlags = (up ? Native.KEYEVENTF_KEYUP : 0) | (IsExtended(vk) ? Native.KEYEVENTF_EXTENDEDKEY : 0);
            return i;
        }

        private static Native.INPUT Uni(char c, bool up)
        {
            var i = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            i.u.ki.dwExtraInfo = OwnMarker;
            i.u.ki.wScan = c;
            i.u.ki.dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0);
            return i;
        }

        private static void Send(List<Native.INPUT> list)
        {
            if (list.Count == 0) return;
            Native.SendInput((uint)list.Count, list.ToArray(), InputSize);
        }

        private static List<ushort> ModifierVks(Modifiers m)
        {
            var l = new List<ushort>();
            if ((m & Modifiers.Ctrl) != 0) l.Add(VK_LCONTROL);
            if ((m & Modifiers.Alt) != 0) l.Add(VK_LMENU);
            if ((m & Modifiers.Shift) != 0) l.Add(VK_LSHIFT);
            if ((m & Modifiers.Win) != 0) l.Add(VK_LWIN);
            return l;
        }

        /// <summary>Virtuelle Taste mit gedrückten Sondertasten senden (z. B. Strg+C, Shift+Pfeil).</summary>
        public static void SendVk(ushort vk, Modifiers mods)
        {
            var list = new List<Native.INPUT>();
            var mv = ModifierVks(mods);
            foreach (var m in mv) list.Add(Key(m, false));
            list.Add(Key(vk, false));
            list.Add(Key(vk, true));
            for (int i = mv.Count - 1; i >= 0; i--) list.Add(Key(mv[i], true));
            Send(list);
        }

        /// <summary>Text unabhängig vom Tastaturlayout senden (Umlaute, Sonderzeichen).</summary>
        public static void SendText(string text)
        {
            var list = new List<Native.INPUT>();
            foreach (char c in text)
            {
                if (c == '\r') continue;
                if (c == '\n') { list.Add(Key(VK_RETURN, false)); list.Add(Key(VK_RETURN, true)); continue; }
                if (c == '\t') { list.Add(Key(VK_TAB, false)); list.Add(Key(VK_TAB, true)); continue; }
                if (c == ' ') { list.Add(Key(VK_SPACE, false)); list.Add(Key(VK_SPACE, true)); continue; }
                list.Add(Uni(c, false));
                list.Add(Uni(c, true));
            }
            Send(list);
        }

        public static void SendBackspaces(int n)
        {
            var list = new List<Native.INPUT>();
            for (int i = 0; i < n; i++) { list.Add(Key(VK_BACK, false)); list.Add(Key(VK_BACK, true)); }
            Send(list);
        }

        /// <summary>Zeichen als echte Taste senden, damit Kombis wie Strg+C funktionieren.</summary>
        public static bool SendCharWithModifiers(char c, Modifiers mods)
        {
            ushort vk = 0;
            char u = char.ToUpperInvariant(c);
            if ((u >= 'A' && u <= 'Z') || (u >= '0' && u <= '9'))
            {
                vk = u;
            }
            else
            {
                uint pidIgnored;
                uint tid = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pidIgnored);
                short r = Native.VkKeyScanEx(c, Native.GetKeyboardLayout(tid));
                if (r == -1) return false;
                vk = (ushort)(r & 0xFF);
                int shiftState = (r >> 8) & 0xFF;
                if ((shiftState & 1) != 0) mods |= Modifiers.Shift;
            }
            SendVk(vk, mods);
            return true;
        }
    }

    [Flags]
    internal enum Modifiers
    {
        None = 0, Shift = 1, Ctrl = 2, Alt = 4, Win = 8
    }
}
