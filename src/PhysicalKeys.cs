using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tastatur
{
    /// <summary>
    /// Echte Tastendrücke (Quadstick, Hardware-Tastatur) für die Anzeige: welche Taste leuchtet wie stark rot.
    /// Speichert nichts dauerhaft und lernt nichts. Ohne Windows testbar.
    /// </summary>
    internal class PhysicalKeys
    {
        private readonly HashSet<ushort> _down = new HashSet<ushort>();
        private readonly Dictionary<ushort, DateTime> _released = new Dictionary<ushort, DateTime>();
        private readonly Func<char, ushort> _charToVk;

        // Kombination aus nur Sondertasten (z. B. Strg+Shift für Wispr Flow)
        private Modifiers _chordMods;
        private bool _chordHadKey;

        public PhysicalKeys(Func<char, ushort> charToVk = null)
        {
            _charToVk = charToVk ?? DefaultCharToVk;
        }

        /// <summary>Links/rechts zusammenfassen. Rechtes Alt bleibt eigenständig (AltGr).</summary>
        public static ushort Normalize(ushort vk)
        {
            switch (vk)
            {
                case 0xA0: case 0xA1: return 0x10;
                case 0xA2: case 0xA3: return 0x11;
                case 0xA4: return 0x12;
                case 0x5C: return 0x5B;
                default: return vk;
            }
        }

        public static bool IsModifier(ushort vk)
        {
            vk = Normalize(vk);
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0xA5 || vk == 0x5B;
        }

        public static Modifiers ModOf(ushort vk)
        {
            switch (Normalize(vk))
            {
                case 0x10: return Modifiers.Shift;
                case 0x11: return Modifiers.Ctrl;
                case 0x12: return Modifiers.Alt;
                case 0x5B: return Modifiers.Win;
                default: return Modifiers.None;
            }
        }

        public static ushort DefaultCharToVk(char c)
        {
            char u = char.ToUpperInvariant(c);
            if ((u >= 'A' && u <= 'Z') || (u >= '0' && u <= '9')) return u;
            try
            {
                short r = Native.VkKeyScan(c);
                if (r != -1 && (r & 0xFF) != 0xFF) return (ushort)(r & 0xFF);
            }
            catch { }
            return 0;
        }

        /// <summary>Passt die echte Taste zur Taste auf der Bildschirmtastatur?</summary>
        public bool Matches(KeyDef k, ushort vk)
        {
            vk = Normalize(vk);
            switch (k.Kind)
            {
                case KeyKind.Char: return k.Normal != null && k.Normal.Length == 1 && _charToVk(k.Normal[0]) == vk;
                case KeyKind.Space: return vk == 0x20;
                case KeyKind.Caps: return vk == 0x14;
                case KeyKind.AltGr: return vk == 0xA5;
                case KeyKind.Modifier: return k.Mod != Modifiers.None && ModOf(vk) == k.Mod;
                case KeyKind.Special: return k.Vk != 0 && Normalize(k.Vk) == vk;
                default: return false;
            }
        }

        public void Down(ushort vk, DateTime now)
        {
            vk = Normalize(vk);
            if (_down.Count == 0) { _chordMods = Modifiers.None; _chordHadKey = false; }
            _down.Add(vk);
            _released.Remove(vk);
            if (IsModifier(vk)) _chordMods |= ModOf(vk);
            else _chordHadKey = true;
        }

        /// <summary>Taste losgelassen. Rückgabe true, wenn eine reine Sondertasten-Kombi wie Strg+Shift fertig ist.</summary>
        public bool Up(ushort vk, DateTime now)
        {
            vk = Normalize(vk);
            bool was = _down.Remove(vk);
            if (was) _released[vk] = now;
            if (_down.Count > 0) return false;
            int n = 0;
            foreach (Modifiers m in new[] { Modifiers.Shift, Modifiers.Ctrl, Modifiers.Alt, Modifiers.Win })
                if ((_chordMods & m) != 0) n++;
            bool chord = !_chordHadKey && n >= 2;
            _chordMods = Modifiers.None; _chordHadKey = false;
            return chord;
        }

        public Modifiers HeldMods
        {
            get
            {
                var m = Modifiers.None;
                foreach (var vk in _down) m |= ModOf(vk);
                return m;
            }
        }

        public bool IsDown(ushort vk) { return _down.Contains(Normalize(vk)); }

        /// <summary>1 = voll rot (gedrückt), danach erst rot und zum Schluss ausblenden, 0 = normal.</summary>
        public double Glow(KeyDef k, DateTime now, int afterMs)
        {
            double best = 0;
            foreach (var vk in _down) if (Matches(k, vk)) return 1;
            if (afterMs <= 0) return 0;
            foreach (var kv in _released)
            {
                if (!Matches(k, kv.Key)) continue;
                double ms = (now - kv.Value).TotalMilliseconds;
                if (ms >= afterMs) continue;
                double fadeStart = afterMs * 0.6;
                double g = ms <= fadeStart ? 1 : 1 - (ms - fadeStart) / (afterMs - fadeStart);
                if (g > best) best = g;
            }
            return best;
        }

        /// <summary>Muss noch neu gezeichnet werden (etwas leuchtet)?</summary>
        public bool Active(DateTime now, int afterMs)
        {
            if (_down.Count > 0) return true;
            var old = _released.Where(kv => (now - kv.Value).TotalMilliseconds >= afterMs).Select(kv => kv.Key).ToList();
            foreach (var vk in old) _released.Remove(vk);
            return _released.Count > 0;
        }

        /// <summary>Text für die Statuszeile, z. B. "Strg+V" oder "Strg+Shift".</summary>
        public static string ComboText(Modifiers mods, ushort vk)
        {
            var sb = new StringBuilder();
            if ((mods & Modifiers.Ctrl) != 0) sb.Append(KeyLayouts.Label("Strg")).Append("+");
            if ((mods & Modifiers.Alt) != 0) sb.Append("Alt+");
            if ((mods & Modifiers.Shift) != 0) sb.Append("Shift+");
            if ((mods & Modifiers.Win) != 0) sb.Append("Win+");
            if (vk == 0 || IsModifier(vk)) return sb.Length > 0 ? sb.ToString(0, sb.Length - 1) : "";
            if (vk == 0xA5) sb.Append("AltGr");
            else sb.Append(KeyCombo.DisplayName(vk).Replace("VK", L.T("Taste ")));
            return sb.ToString();
        }
    }

    /// <summary>Zustand der Tastaturlogik pro Fenster, nur im Arbeitsspeicher.</summary>
    internal class WindowStates
    {
        public const int MaxWindows = 30;
        public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

        private class Entry { public EngineState State; public DateTime At; }
        private readonly Dictionary<IntPtr, Entry> _items = new Dictionary<IntPtr, Entry>();

        public int Count { get { return _items.Count; } }

        public void Save(IntPtr hwnd, EngineState state, DateTime now)
        {
            if (hwnd == IntPtr.Zero) return;
            if (state == null || state.IsEmpty) { _items.Remove(hwnd); return; }
            _items[hwnd] = new Entry { State = state, At = now };
            while (_items.Count > MaxWindows)
            {
                var oldest = _items.OrderBy(kv => kv.Value.At).First().Key;
                _items.Remove(oldest);
            }
        }

        /// <summary>Gespeicherten Zustand holen und entfernen (null, wenn keiner oder zu alt).</summary>
        public EngineState Take(IntPtr hwnd, DateTime now)
        {
            Entry e;
            if (!_items.TryGetValue(hwnd, out e)) return null;
            _items.Remove(hwnd);
            return now - e.At > MaxAge ? null : e.State;
        }

        public void Prune(DateTime now, Func<IntPtr, bool> exists)
        {
            var gone = _items.Where(kv => now - kv.Value.At > MaxAge || (exists != null && !exists(kv.Key))).Select(kv => kv.Key).ToList();
            foreach (var h in gone) _items.Remove(h);
        }

        public void Clear() { _items.Clear(); }
    }
}
