using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Tastatur
{
    /// <summary>
    /// Eine Taste mit Sondertasten, z. B. "Strg+Shift+T", "F13", "Alt+Tab", "Enter".
    /// Wird für Makro-Tastenfolgen und globale Kürzel benutzt.
    /// </summary>
    internal class KeyCombo
    {
        public Modifiers Mods;
        public ushort Vk;       // virtuelle Taste, 0 wenn Char gesetzt ist
        public char Char;       // einzelnes Zeichen wie '#' (Taste wird zur Laufzeit übers Layout gesucht)

        private static readonly Dictionary<string, ushort> Names = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<ushort, string> Canonical = new Dictionary<ushort, string>();

        private static void N(ushort vk, params string[] names)
        {
            foreach (var n in names) Names[n] = vk;
            Canonical[vk] = names[0];
        }

        static KeyCombo()
        {
            N(InputSender.VK_RETURN, "Enter", "Eingabe", "Return"); // i18n-ok
            N(InputSender.VK_TAB, "Tab");
            N(InputSender.VK_ESCAPE, "Esc", "Escape");
            N(InputSender.VK_SPACE, "Leertaste", "Leer", "Space"); // i18n-ok
            N(InputSender.VK_BACK, "Rück", "Rueck", "Backspace", "⌫"); // i18n-ok
            N(InputSender.VK_DELETE, "Entf", "Del", "Delete");
            N(InputSender.VK_INSERT, "Einfg", "Insert", "Ins");
            N(InputSender.VK_HOME, "Pos1", "Home");
            N(InputSender.VK_END, "Ende", "End"); // i18n-ok
            N(InputSender.VK_PRIOR, "BildAuf", "PageUp", "PgUp");
            N(InputSender.VK_NEXT, "BildAb", "PageDown", "PgDn");
            N(InputSender.VK_LEFT, "Links", "Left", "←"); // i18n-ok
            N(InputSender.VK_RIGHT, "Rechts", "Right", "→"); // i18n-ok
            N(InputSender.VK_UP, "Hoch", "Oben", "Up", "↑"); // i18n-ok
            N(InputSender.VK_DOWN, "Runter", "Unten", "Down", "↓"); // i18n-ok
            N(InputSender.VK_APPS, "Menü", "Menue", "Apps"); // i18n-ok
            N(0x13, "Pause"); // i18n-ok
            N(0x91, "Rollen", "ScrollLock"); // i18n-ok
            N(0x2C, "Druck", "PrintScreen"); // i18n-ok
            for (int i = 1; i <= 24; i++) N((ushort)(InputSender.VK_F1 + i - 1), "F" + i);
            for (int i = 0; i <= 9; i++) N((ushort)(0x60 + i), "Num" + i);
            // Rechenzeichen vom Ziffernblock: eigene Namen, sonst würden sie als "/" usw. gespeichert
            // und dann als Shift+7 statt Num / registriert
            N(0x6F, "NumGeteilt", "NumDivide", "Num/"); // i18n-ok
            N(0x6A, "NumMal", "NumMultiply", "Num*"); // i18n-ok
            N(0x6D, "NumMinus", "NumSubtract", "Num-"); // i18n-ok
            N(0x6B, "NumPlus", "NumAdd"); // i18n-ok
            N(0x6E, "NumKomma", "NumDecimal"); // i18n-ok
        }

        public bool IsEmpty { get { return Vk == 0 && Char == '\0'; } }

        /// <summary>Eine Kombi wie "Strg+Shift+T" lesen. Liefert null bei Unbekanntem.</summary>
        public static KeyCombo Parse(string text)
        {
            if (text == null) return null;
            text = text.Trim();
            if (text.Length == 0) return null;

            // "+" als Taste: "Strg++" oder einfach "+"
            var parts = new List<string>();
            var cur = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '+' && cur.Length > 0) { parts.Add(cur.ToString().Trim()); cur.Clear(); }
                else cur.Append(c);
            }
            if (cur.Length > 0) parts.Add(cur.ToString().Trim());
            if (parts.Count == 0) return null;

            var k = new KeyCombo();
            for (int i = 0; i < parts.Count - 1; i++)
            {
                switch (parts[i].ToLowerInvariant())
                {
                    case "strg": case "ctrl": case "control": k.Mods |= Modifiers.Ctrl; break;
                    case "alt": k.Mods |= Modifiers.Alt; break;
                    case "shift": case "umschalt": case "⇧": k.Mods |= Modifiers.Shift; break;
                    case "win": case "windows": k.Mods |= Modifiers.Win; break;
                    default: return null;
                }
            }
            string key = parts[parts.Count - 1];
            ushort vk;
            if (Names.TryGetValue(key, out vk)) { k.Vk = vk; return k; }
            if (key.Length == 1)
            {
                char c = key[0];
                char u = char.ToUpperInvariant(c);
                if ((u >= 'A' && u <= 'Z') || (u >= '0' && u <= '9')) k.Vk = u;
                else k.Char = c;
                return k;
            }
            if (key.StartsWith("VK", StringComparison.OrdinalIgnoreCase))
            {
                int v;
                if (int.TryParse(key.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v > 0 && v < 256)
                {
                    k.Vk = (ushort)v; return k;
                }
            }
            return null;
        }

        /// <summary>Folge wie "Strg+A, Strg+C, Enter" lesen. Fehler: Index des falschen Teils in errorPart.</summary>
        public static List<KeyCombo> ParseSequence(string text, out string errorPart)
        {
            errorPart = null;
            var list = new List<KeyCombo>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (var raw in SplitSequence(text))
            {
                var k = Parse(raw);
                if (k == null) { errorPart = raw.Trim(); return null; }
                list.Add(k);
            }
            return list;
        }

        private static IEnumerable<string> SplitSequence(string text)
        {
            // Komma trennt, außer es ist selbst die Taste ("Strg+,") oder steht alleine (",,")
            var cur = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool keyIsComma = c == ',' && (cur.ToString().Trim().Length == 0 || cur.ToString().TrimEnd().EndsWith("+"));
                if (c == ',' && !keyIsComma)
                {
                    if (cur.ToString().Trim().Length > 0) yield return cur.ToString();
                    cur.Clear();
                }
                else cur.Append(c);
            }
            if (cur.ToString().Trim().Length > 0) yield return cur.ToString();
        }

        /// <summary>
        /// Kombi aus einer virtuellen Taste bauen (z. B. aus einem Tastatur-Hook).
        /// Satzzeichen-Tasten (#, +, ...) werden als Zeichen gespeichert, damit es lesbar bleibt.
        /// </summary>
        public static KeyCombo FromVk(ushort vk, Modifiers mods, Func<ushort, char> vkToChar = null)
        {
            var k = new KeyCombo { Mods = mods };
            bool named = Canonical.ContainsKey(vk) || (vk >= 'A' && vk <= 'Z') || (vk >= '0' && vk <= '9');
            if (!named && vkToChar != null)
            {
                char c = vkToChar(vk);
                if (c > ' ' && !char.IsLetterOrDigit(c)) { k.Char = c; return k; }
            }
            k.Vk = vk;
            return k;
        }

        public static char VkToCharOnWindows(ushort vk)
        {
            try { return (char)(Native.MapVirtualKey(vk, 2) & 0xFFFF); } catch { return '\0'; }
        }

        private static readonly Dictionary<ushort, string> English = new Dictionary<ushort, string>
        {
            { InputSender.VK_RETURN, "Enter" }, { InputSender.VK_TAB, "Tab" }, { InputSender.VK_ESCAPE, "Esc" },
            { InputSender.VK_SPACE, "Space" }, { InputSender.VK_BACK, "Backspace" }, { InputSender.VK_DELETE, "Delete" },
            { InputSender.VK_INSERT, "Insert" }, { InputSender.VK_HOME, "Home" }, { InputSender.VK_END, "End" },
            { InputSender.VK_PRIOR, "PageUp" }, { InputSender.VK_NEXT, "PageDown" }, { InputSender.VK_LEFT, "Left" },
            { InputSender.VK_RIGHT, "Right" }, { InputSender.VK_UP, "Up" }, { InputSender.VK_DOWN, "Down" },
            { InputSender.VK_APPS, "Apps" }, { 0x91, "ScrollLock" }, { 0x2C, "PrintScreen" },
        };

        /// <summary>Tastenname zum Anzeigen und Speichern: deutsch bei deutscher Oberfläche, sonst englisch.
        /// Beide Schreibweisen werden beim Einlesen verstanden.</summary>
        public static string DisplayName(ushort vk)
        {
            string n;
            if (L.Lang != "de" && English.TryGetValue(vk, out n)) return n;
            return KeyName(vk);
        }

        public static string CtrlName { get { return L.Lang == "de" ? "Strg" : "Ctrl"; } }

        public static string KeyName(ushort vk)
        {
            string n;
            if (Canonical.TryGetValue(vk, out n)) return n;
            if ((vk >= 'A' && vk <= 'Z') || (vk >= '0' && vk <= '9')) return ((char)vk).ToString();
            return "VK" + vk;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            if ((Mods & Modifiers.Ctrl) != 0) sb.Append(CtrlName).Append("+");
            if ((Mods & Modifiers.Alt) != 0) sb.Append("Alt+");
            if ((Mods & Modifiers.Shift) != 0) sb.Append("Shift+");
            if ((Mods & Modifiers.Win) != 0) sb.Append("Win+");
            sb.Append(Char != '\0' ? Char.ToString() : DisplayName(Vk));
            return sb.ToString();
        }

        public void Send(IOutput output)
        {
            if (Char != '\0') output.SendCharWithModifiers(Char, Mods);
            else output.SendVk(Vk, Mods);
        }

        /// <summary>Virtuelle Taste für RegisterHotKey (Zeichen werden übers aktuelle Layout aufgelöst).</summary>
        public ushort ResolveVk(out Modifiers extraMods)
        {
            extraMods = Modifiers.None;
            if (Char == '\0') return Vk;
            short r = Native.VkKeyScan(Char);
            if (r == -1) return 0;
            if (((r >> 8) & 1) != 0) extraMods |= Modifiers.Shift;
            return (ushort)(r & 0xFF);
        }
    }
}
