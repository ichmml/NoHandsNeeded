using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace Tastatur
{
    /// <summary>
    /// Farben der Tastatur. Fertige Schemas (dunkel = "Standard", hell, nacht = "Dunkel", kontrast, blau, gruen)
    /// oder "eigen": jeder Bereich mit eigener Farbe (Settings.CustomColors).
    /// </summary>
    internal class Theme
    {
        public Color Background, Key, KeySpecial, KeyHover, Text, TextDim, Accent, Locked, Suggestion, Border;
        /// <summary>1.2.04.2: Vorschlagstext, Makro-Knöpfe, Verweil-Fortschritt, gedrückte Taste.</summary>
        public Color SuggestionText, MacroKey, Dwell, Pressed;
        /// <summary>Heller Hintergrund (für Mischfarben wie rot auf hell/dunkel).</summary>
        public bool IsLight;

        /// <summary>Schlüssel der fertigen Schemas in der Reihenfolge der Kacheln. "dunkel" ist das bisherige Standard-Schema.</summary>
        public static readonly string[] Presets = { "dunkel", "hell", "nacht", "kontrast", "blau", "gruen" }; // i18n-ok
        public const string Custom = "eigen"; // i18n-ok
        public const string Default = "dunkel"; // i18n-ok

        /// <summary>Bereiche, die man einzeln einfärben kann (Schlüssel in einstellungen.txt).</summary>
        public static readonly string[] Areas = { "hintergrund", "tasten", "beschriftung", "sondertasten", "leiste", "leiste_text", "makros", "verweilen", "gedrueckt" }; // i18n-ok

        public static bool IsKnown(string name)
        {
            return name == Custom || Array.IndexOf(Presets, name) >= 0;
        }

        public static string PresetLabel(string key)
        {
            switch (key)
            {
                case "dunkel": return L.T("Standard");
                case "hell": return L.T("Hell");
                case "nacht": return L.T("Dunkel");
                case "kontrast": return L.T("Hoher Kontrast");
                case "blau": return L.T("Blau");
                case "gruen": return L.T("Grün");
                case "eigen": return L.T("Eigene Farben");
            }
            return key;
        }

        public static string AreaLabel(string area)
        {
            switch (area)
            {
                case "hintergrund": return L.T("Hintergrund der Tastatur");
                case "tasten": return L.T("Tasten");
                case "beschriftung": return L.T("Tastenbeschriftung");
                case "sondertasten": return L.T("Sondertasten (Shift, Enter, Löschen, Aa, Makro …)");
                case "leiste": return L.T("Vorschlagsleiste");
                case "leiste_text": return L.T("Vorschlagstext");
                case "makros": return L.T("Makro-Knöpfe");
                case "verweilen": return L.T("Verweilen (Fortschritt und Hervorhebung)");
                case "gedrueckt": return L.T("Gedrückte Taste");
            }
            return area;
        }

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        /// <summary>Fertiges Schema. Unbekannter Name: Standard.</summary>
        public static Theme Get(string name)
        {
            Theme t;
            switch (name)
            {
                case "hell":
                    t = new Theme
                    {
                        Background = C(214, 218, 224), Key = C(255, 255, 255), KeySpecial = C(236, 238, 242), KeyHover = C(204, 226, 255),
                        Text = C(20, 20, 24), TextDim = C(110, 114, 122), Accent = C(0, 110, 230), Locked = C(220, 110, 0),
                        Suggestion = C(246, 248, 250), Border = C(180, 186, 196)
                    };
                    break;
                case "nacht":
                    t = new Theme
                    {
                        Background = C(0, 0, 0), Key = C(30, 30, 32), KeySpecial = C(18, 18, 20), KeyHover = C(58, 58, 64),
                        Text = C(235, 235, 235), TextDim = C(130, 130, 136), Accent = C(120, 120, 255), Locked = C(255, 150, 50),
                        Suggestion = C(22, 22, 24), Border = C(0, 0, 0)
                    };
                    break;
                case "kontrast":
                    t = new Theme
                    {
                        Background = C(0, 0, 0), Key = C(34, 34, 34), KeySpecial = C(56, 56, 56), KeyHover = C(90, 80, 0),
                        Text = C(255, 230, 0), TextDim = C(255, 255, 255), Accent = C(0, 200, 255), Locked = C(255, 255, 255),
                        Suggestion = C(34, 34, 34), Border = C(255, 230, 0)
                    };
                    break;
                case "blau":
                    t = new Theme
                    {
                        Background = C(16, 34, 64), Key = C(36, 70, 120), KeySpecial = C(26, 52, 94), KeyHover = C(60, 102, 164),
                        Text = C(245, 248, 255), TextDim = C(160, 182, 214), Accent = C(90, 190, 255), Locked = C(255, 170, 60),
                        Suggestion = C(28, 56, 100), Border = C(10, 22, 44)
                    };
                    break;
                case "gruen":
                    t = new Theme
                    {
                        Background = C(18, 44, 30), Key = C(38, 84, 58), KeySpecial = C(28, 64, 44), KeyHover = C(62, 120, 86),
                        Text = C(244, 252, 246), TextDim = C(160, 196, 172), Accent = C(120, 220, 120), Locked = C(255, 180, 60),
                        Suggestion = C(30, 70, 48), Border = C(10, 28, 18)
                    };
                    break;
                default:
                    t = new Theme
                    {
                        Background = C(28, 29, 34), Key = C(54, 57, 66), KeySpecial = C(40, 42, 50), KeyHover = C(78, 84, 98),
                        Text = C(245, 246, 248), TextDim = C(150, 155, 165), Accent = C(76, 154, 255), Locked = C(255, 159, 67),
                        Suggestion = C(46, 49, 58), Border = C(20, 21, 25)
                    };
                    break;
            }
            t.SuggestionText = t.Text;
            t.MacroKey = t.Key;
            t.Dwell = t.Accent;
            t.Pressed = t.Accent;
            t.IsLight = Luminance(t.Background) > 0.4;
            return t;
        }

        /// <summary>Farben, wie sie die Einstellungen verlangen (Schema oder eigene Farben).</summary>
        public static Theme For(Settings s)
        {
            if (s == null) return Get(Default);
            if (s.Theme != Custom) return Get(s.Theme);
            return FromCustom(s.CustomColors);
        }

        /// <summary>Eigene Farben: fehlende Bereiche kommen vom Standard-Schema.</summary>
        public static Theme FromCustom(string custom)
        {
            var t = Get(Default);
            var map = ParseCustom(custom);
            foreach (var kv in map) t.SetArea(kv.Key, kv.Value);
            t.Derive();
            return t;
        }

        /// <summary>Abgeleitete Farben nach dem Ändern einzelner Bereiche.</summary>
        private void Derive()
        {
            TextDim = Blend(Text, Key, 0.42f);
            KeyHover = Blend(Key, Dwell, 0.35f);
            Accent = Dwell;
            Border = Blend(Background, Color.Black, 0.3f);
            IsLight = Luminance(Background) > 0.4;
        }

        public Color GetArea(string area)
        {
            switch (area)
            {
                case "hintergrund": return Background;
                case "tasten": return Key;
                case "beschriftung": return Text;
                case "sondertasten": return KeySpecial;
                case "leiste": return Suggestion;
                case "leiste_text": return SuggestionText;
                case "makros": return MacroKey;
                case "verweilen": return Dwell;
                case "gedrueckt": return Pressed;
            }
            return Background;
        }

        public void SetArea(string area, Color c)
        {
            switch (area)
            {
                case "hintergrund": Background = c; break;
                case "tasten": Key = c; break;
                case "beschriftung": Text = c; break;
                case "sondertasten": KeySpecial = c; break;
                case "leiste": Suggestion = c; break;
                case "leiste_text": SuggestionText = c; break;
                case "makros": MacroKey = c; break;
                case "verweilen": Dwell = c; break;
                case "gedrueckt": Pressed = c; break;
            }
        }

        /// <summary>Alle Bereiche eines Schemas als Text für Settings.CustomColors ("hintergrund=#1C1D22;tasten=…").</summary>
        public string ToCustom()
        {
            var parts = new List<string>();
            foreach (var a in Areas) parts.Add(a + "=" + ToHex(GetArea(a)));
            return string.Join(";", parts);
        }

        public static Dictionary<string, Color> ParseCustom(string custom)
        {
            var d = new Dictionary<string, Color>();
            foreach (var part in (custom ?? "").Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string k = part.Substring(0, eq).Trim().ToLowerInvariant();
                Color c;
                if (Array.IndexOf(Areas, k) >= 0 && TryParseHex(part.Substring(eq + 1), out c)) d[k] = c;
            }
            return d;
        }

        /// <summary>Eigene Farben bereinigen: nur bekannte Bereiche und gültige Farben, feste Reihenfolge.</summary>
        public static string NormalizeCustom(string custom)
        {
            var map = ParseCustom(custom);
            var parts = new List<string>();
            foreach (var a in Areas) if (map.ContainsKey(a)) parts.Add(a + "=" + ToHex(map[a]));
            return string.Join(";", parts);
        }

        public static string ToHex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        public static bool TryParseHex(string s, out Color c)
        {
            c = Color.Empty;
            s = (s ?? "").Trim().TrimStart('#');
            int v;
            if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            c = Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
            return true;
        }

        public static Color Blend(Color a, Color b, float f)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));
        }

        /// <summary>Relative Helligkeit nach WCAG (0 = schwarz, 1 = weiß).</summary>
        public static double Luminance(Color c)
        {
            Func<int, double> ch = v => { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); };
            return 0.2126 * ch(c.R) + 0.7152 * ch(c.G) + 0.0722 * ch(c.B);
        }

        /// <summary>Kontrastverhältnis nach WCAG, 1 bis 21. Unter 4,5 ist Text schlecht lesbar.</summary>
        public static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        public const double MinContrast = 4.5;

        /// <summary>Paare (Text auf Fläche), die gut lesbar sein müssen. Liefert die schlecht lesbaren als Text.</summary>
        public List<string> LowContrast()
        {
            var bad = new List<string>();
            Action<string, Color, Color> chk = (what, fg, bg) => { if (Contrast(fg, bg) < MinContrast) bad.Add(what); };
            chk(L.T("Beschriftung auf Tasten"), Text, Key);
            chk(L.T("Beschriftung auf Sondertasten"), Text, KeySpecial);
            chk(L.T("Vorschlagstext auf der Leiste"), SuggestionText, Suggestion);
            chk(L.T("Beschriftung auf Makro-Knöpfen"), Text, MacroKey);
            return bad;
        }

        /// <summary>Farben der Palette zum Anklicken (4 Reihen mit je 6).</summary>
        public static readonly Color[] Palette =
        {
            C(0, 0, 0), C(40, 40, 46), C(90, 92, 100), C(160, 164, 172), C(220, 222, 228), C(255, 255, 255),
            C(200, 30, 40), C(240, 90, 40), C(255, 160, 0), C(255, 230, 0), C(130, 200, 40), C(20, 140, 60),
            C(0, 160, 150), C(0, 180, 230), C(30, 110, 230), C(20, 40, 120), C(110, 60, 200), C(200, 60, 160),
            C(120, 70, 40), C(250, 235, 210), C(255, 190, 200), C(200, 230, 255), C(210, 245, 210), C(16, 34, 64)
        };
    }
}
