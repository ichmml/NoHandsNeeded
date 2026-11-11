using System;
using System.Collections.Generic;
using System.Linq;

namespace Tastatur
{
    internal enum KeyKind { Char, Special, Modifier, Caps, AltGr, Space, CaseToggle, Drag, LayoutToggle, FnToggle }

    internal class KeyDef
    {
        public KeyKind Kind;
        public string Normal, Shifted, AltGr;   // für Zeichentasten
        public string Label;                    // für Sondertasten
        public ushort Vk;                       // für Sondertasten
        public Modifiers Mod;                   // für Sondertasten Strg/Alt/Shift/Win
        public float Width = 1f;

        // wird beim Layouten gesetzt
        public System.Drawing.RectangleF Rect;
    }

    /// <summary>Deutsches QWERTZ-Layout, jede Reihe 15 Einheiten breit.</summary>
    internal static class KeyLayouts
    {
        private static KeyDef C(string n, string s, string a = null, float w = 1f)
        {
            return new KeyDef { Kind = KeyKind.Char, Normal = n, Shifted = s, AltGr = a, Width = w };
        }

        private static KeyDef S(string label, ushort vk, float w = 1f)
        {
            return new KeyDef { Kind = KeyKind.Special, Label = label, Vk = vk, Width = w };
        }

        private static KeyDef M(string label, Modifiers m, float w = 1f)
        {
            return new KeyDef { Kind = KeyKind.Modifier, Label = label, Mod = m, Width = w };
        }

        private static KeyDef Aa(float w)
        {
            return new KeyDef { Kind = KeyKind.CaseToggle, Label = "Aa", Width = w };
        }

        /// <summary>Beschriftung der Sondertasten in der Sprache der Oberfläche.</summary>
        public static Func<string, string> Label = t => t;

        /// <summary>Codes der Sprachen mit eigenem Layout.</summary>
        public static readonly string[] Codes = { "de", "en", "nl", "fr", "da", "pl", "cs", "it", "es", "tr", "ru" };

        /// <summary>Eine Reihe aus Kurzschreibweise: Einträge mit Leerzeichen getrennt, je Eintrag Zeichen normal, Shift, AltGr.</summary>
        private static List<KeyDef> Row(string spec)
        {
            var r = new List<KeyDef>();
            foreach (var e in spec.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var cs = StringInfoChars(e);
                r.Add(C(cs[0], cs.Length > 1 ? cs[1] : cs[0], cs.Length > 2 ? cs[2] : null));
            }
            return r;
        }

        private static string[] StringInfoChars(string e)
        {
            var list = new List<string>();
            var en = System.Globalization.StringInfo.GetTextElementEnumerator(e);
            while (en.MoveNext()) list.Add((string)en.Current);
            return list.ToArray();
        }

        private class Spec { public string Num, Top, Home, Bottom; public string Dead = ""; }

        private static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>
        {
            { "de", new Spec {
                Num = "^° 1! 2\"² 3§³ 4$ 5% 6& 7/{ 8([ 9)] 0=} ß?\\ ´`",
                Top = "qQ@ wW eE€ rR tT zZ uU iI oO pP üÜ +*~",
                Home = "aA sS dD fF gG hH jJ kK lL öÖ äÄ #'",
                Bottom = "<>| yY xX cC vV bB nN mMµ ,; .: -_" } },
            { "en", new Spec {
                Num = "`~ 1! 2@ 3# 4$ 5% 6^ 7& 8* 9( 0) -_ =+",
                Top = "qQ wW eE€ rR tT yY uU iI oO pP [{ ]}", // i18n-ok
                Home = "aA sS dD fF gG hH jJ kK lL ;: '\" \\|",
                Bottom = "@@ zZ xX cC vV bB nN mM ,< .> /?" } },
            { "nl", new Spec {
                Num = "`~ 1! 2@ 3# 4$ 5% 6^ 7& 8* 9( 0) -_ =+",
                Top = "qQ wW eE€ rR tT yYü uUú iIí oOó pPö [{ ]}", // i18n-ok
                Home = "aAá sS dD fF gG hH jJ kK lL ;: '\" \\|",
                Bottom = "@@ zZ xX cC vV bB nNñ mM ,< .> /?",
                Dead = "'\"`^~" } },
            { "fr", new Spec {
                Num = "²² &1 é2~ \"3# '4{ (5[ -6| è7` _8\\ ç9^ à0@ )°] =+}",
                Top = "aA zZ eE€ rR tT yY uU iI oO pP ^¨ $£¤",
                Home = "qQ sS dD fF gG hH jJ kK lL mM ù% *µ",
                Bottom = "<> wW xX cC vV bB nN ,? ;. :/ !§",
                Dead = "^¨" } },
            { "da", new Spec {
                Num = "½§ 1! 2\"@ 3#£ 4¤$ 5% 6& 7/{ 8([ 9)] 0=} +? ´`|",
                Top = "qQ wW eE€ rR tT yY uU iI oO pP åÅ ¨^~", // i18n-ok
                Home = "aA sS dD fF gG hH jJ kK lL æÆ øØ '*",
                Bottom = "<>\\ zZ xX cC vV bB nN mMµ ,; .: -_",
                Dead = "´`¨^~" } },
            { "pl", new Spec {
                Num = "`~ 1! 2@ 3# 4$ 5% 6^ 7& 8* 9( 0) -_ =+",
                Top = "qQ wW eEę rR tT yY uU€ iI oOó pP [{ ]}",
                Home = "aAą sSś dD fF gG hH jJ kK lLł ;: '\" \\|",
                Bottom = "@@ zZż xXź cCć vV bB nNń mM ,< .> /?" } },
            { "cs", new Spec {
                Num = ";° +1 ě2 š3 č4 ř5 ž6 ý7 á8 í9 é0 =% ´ˇ",
                Top = "qQ\\ wW| eE€ rR tT zZ uU iI oO pP ú/ )(",
                Home = "aA sS dD fF gG hH jJ kK lL ů\" §! ¨'",
                Bottom = "\\| yY xX cC vV bB nN mM ,? .: -_",
                Dead = "´ˇ¨" } },
            { "it", new Spec {
                Num = "\\| 1! 2\" 3£ 4$ 5% 6& 7/ 8( 9) 0= '? ì^",
                Top = "qQ wW eE€ rR tT yY uU iI oO pP èé[ +*]", // i18n-ok
                Home = "aA sS dD fF gG hH jJ kK lL òç@ à°# ù§",
                Bottom = "<> zZ xX cC vV bB nN mM ,; .: -_" } },
            { "es", new Spec {
                Num = "ºª\\ 1!| 2\"@ 3·# 4$~ 5% 6&¬ 7/ 8( 9) 0= '? ¡¿",
                Top = "qQ wW eE€ rR tT yY uU iI oO pP `^[ +*]", // i18n-ok
                Home = "aA sS dD fF gG hH jJ kK lL ñÑ ´¨{ çÇ}",
                Bottom = "<> zZ xX cC vV bB nN mM ,; .: -_",
                Dead = "`^´¨" } },
            { "tr", new Spec {
                Num = "\"é 1! 2'£ 3^# 4+$ 5% 6& 7/{ 8([ 9)] 0=} *?\\ -_|",
                Top = "qQ@ wW eE€ rR tT yY uU ıI oO pP ğĞ üÜ",
                Home = "aA sS dD fF gG hH jJ kK lL şŞ iİ ,;",
                Bottom = "<>| zZ xX cC vV bB nN mM öÖ çÇ .:" } },
            { "ru", new Spec {
                Num = "ёЁ 1! 2\" 3№ 4; 5% 6: 7? 8* 9( 0) -_ =+",
                Top = "йЙ цЦ уУ кК еЕ нН гГ шШ щЩ зЗ хХ ъЪ",
                Home = "фФ ыЫ вВ аА пП рР оО лЛ дД жЖ эЭ \\/",
                Bottom = "<> яЯ чЧ сС мМ иИ тТ ьЬ бБ юЮ .," } },
        };

        /// <summary>Kombinierende Zeichen für Tottasten: ´ + e = é.</summary>
        private static readonly Dictionary<char, char> Combining = new Dictionary<char, char>
        {
            { '´', '\u0301' }, { '\'', '\u0301' }, { '`', '\u0300' }, { '^', '\u0302' }, { '¨', '\u0308' },
            { '"', '\u0308' }, { '~', '\u0303' }, { 'ˇ', '\u030C' },
        };

        /// <summary>Tottasten des Layouts (Zeichen → kombinierendes Zeichen), leer wenn keine.</summary>
        public static Dictionary<char, char> DeadKeys(string code)
        {
            var d = new Dictionary<char, char>();
            Spec sp;
            if (code != null && Specs.TryGetValue(code, out sp))
                foreach (char c in sp.Dead) d[c] = Combining[c];
            return d;
        }

        /// <summary>Zeichen mit Tottaste zusammensetzen. Geht das nicht, beide Zeichen nacheinander.</summary>
        public static string Compose(char dead, char combining, string next)
        {
            if (next == " ") return dead.ToString();
            string c = (next + combining).Normalize(System.Text.NormalizationForm.FormC);
            return c.Length == next.Length ? c : dead + next;
        }

        public static bool Has(string code) { return code != null && Specs.ContainsKey(code); }

        public static List<List<KeyDef>> CreateGerman() { return Create("de"); }

        /// <summary>Layout einer Sprache (unbekannt = Deutsch). latinToggle: Russisch mit Taste zum lateinischen Layout.</summary>
        public static List<List<KeyDef>> Create(string code, bool latinOfRussian = false)
        {
            Spec sp;
            if (code == null || !Specs.TryGetValue(code, out sp)) { code = "de"; sp = Specs["de"]; }
            bool toggle = code == "ru" || latinOfRussian;
            var rows = new List<List<KeyDef>>();

            var r0 = Row(sp.Num);
            r0.Add(S("⌫", InputSender.VK_BACK, 2f));
            rows.Add(r0);

            var r1 = new List<KeyDef> { S(Label("Tab ⇥"), InputSender.VK_TAB, 1.5f) };
            r1.AddRange(Row(sp.Top));
            r1.Add(S(Label("Entf"), InputSender.VK_DELETE, 1.5f));
            rows.Add(r1);

            var r2 = new List<KeyDef> { new KeyDef { Kind = KeyKind.Caps, Label = "⇪", Width = 1.5f } };
            r2.AddRange(Row(sp.Home));
            r2.Add(S(Label("Enter ↵"), InputSender.VK_RETURN, 1.5f));
            rows.Add(r2);

            var r3 = new List<KeyDef> { M(Label("⇧ Shift"), Modifiers.Shift, 1.5f) };
            r3.AddRange(Row(sp.Bottom));
            r3.Add(S("↑", InputSender.VK_UP));
            r3.Add(M(Label("⇧ Shift"), Modifiers.Shift, 1.5f));
            rows.Add(r3);

            rows.Add(BottomRow(toggle ? (code == "ru" ? "ABC" : "АБВ") : null));
            return rows;
        }

        /// <summary>Die vier Sonderzeichen unten (Einstellung), z. B. "! ? : -" oder ".: ,;" (zweites Zeichen = Shift).</summary>
        public const string DefaultBottomChars = "! ? : -";
        public static string BottomChars = DefaultBottomChars;

        /// <summary>Eingabe aufräumen: genau vier Einträge, je ein oder zwei Zeichen, Unbrauchbares = Standard.</summary>
        public static string NormalizeBottomChars(string v)
        {
            var def = DefaultBottomChars.Split(' ');
            var parts = (v ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var outp = new string[4];
            for (int i = 0; i < 4; i++)
            {
                string p = i < parts.Length ? parts[i] : "";
                var si = new System.Globalization.StringInfo(p);
                bool ok = si.LengthInTextElements >= 1 && si.LengthInTextElements <= 2 && !p.Any(char.IsControl);
                outp[i] = ok ? p : def[i];
            }
            return string.Join(" ", outp);
        }

        private static List<string[]> BottomCharPairs()
        {
            var list = new List<string[]>();
            foreach (var p in NormalizeBottomChars(BottomChars).Split(' '))
            {
                var si = new System.Globalization.StringInfo(p);
                string n = si.SubstringByTextElements(0, 1);
                string sh = si.LengthInTextElements > 1 ? si.SubstringByTextElements(1, 1) : n;
                list.Add(new[] { n, sh });
            }
            return list;
        }

        private static List<KeyDef> BottomRow(string toggleLabel)
        {
            var r = new List<KeyDef> {
                new KeyDef { Kind = KeyKind.FnToggle, Label = "Fn", Width = 0.75f },   // Fn unten links, Esc oben links
                M(Label("Strg"), Modifiers.Ctrl, 0.75f),
                M("Win", Modifiers.Win, 0.75f),
                M("Alt", Modifiers.Alt, 0.75f),
                new KeyDef { Kind = KeyKind.Space, Label = "", Width = toggleLabel != null ? 2.25f : 3f },
                new KeyDef { Kind = KeyKind.AltGr, Label = "AltGr", Width = 0.75f },   // direkt rechts neben der Leertaste
            };
            if (toggleLabel != null) r.Add(new KeyDef { Kind = KeyKind.LayoutToggle, Label = toggleLabel, Width = 0.75f });
            r.Add(Aa(1.25f));
            foreach (var p in BottomCharPairs()) r.Add(C(p[0], p[1]));
            r.Add(S("←", InputSender.VK_LEFT, 0.75f)); r.Add(S("↓", InputSender.VK_DOWN, 0.75f)); r.Add(S("→", InputSender.VK_RIGHT, 0.75f));
            r.Add(new KeyDef { Kind = KeyKind.Drag, Label = "✥", Width = 0.75f });
            return r;
        }

        /// <summary>Navigations- und Funktionsebene (Taste "Fn").</summary>
        public static List<List<KeyDef>> CreateNav()
        {
            var rows = new List<List<KeyDef>>();
            var f = new List<KeyDef>();
            for (int i = 0; i < 12; i++) f.Add(S("F" + (i + 1), (ushort)(InputSender.VK_F1 + i), 1.25f));
            rows.Add(f);

            rows.Add(new List<KeyDef> {
                S(Label("Pos1"), InputSender.VK_HOME, 1.5f), S(Label("Ende"), InputSender.VK_END, 1.5f),
                S(Label("Bild ↑"), InputSender.VK_PRIOR, 1.5f), S(Label("Bild ↓"), InputSender.VK_NEXT, 1.5f),
                S(Label("Einfg"), InputSender.VK_INSERT, 1.5f), S(Label("Menü"), InputSender.VK_APPS, 1.5f),
                S("Esc", InputSender.VK_ESCAPE, 1.5f), S(Label("Entf"), InputSender.VK_DELETE, 1.5f),
                S("⌫", InputSender.VK_BACK, 3f)
            });

            rows.Add(new List<KeyDef> {
                S(Label("Tab ⇥"), InputSender.VK_TAB, 1.5f),
                C("@", "@"), C("€", "€"), C("{", "{"), C("}", "}"), C("[", "["), C("]", "]"),
                C("\\", "\\"), C("|", "|"), C("~", "~"), C("µ", "µ"), C("²", "²"), C("³", "³"),
                S(Label("Enter ↵"), InputSender.VK_RETURN, 1.5f)
            });

            rows.Add(new List<KeyDef> {
                M(Label("⇧ Shift"), Modifiers.Shift, 1.5f),
                C(":-)", ":-)", null, 1.5f), C("…", "…"), C("–", "–"), C("„", "„"), C("“", "“"),
                C("«", "«"), C("»", "»"), C("°", "°"), C("§", "§"), C("%", "%"),
                S("↑", InputSender.VK_UP),
                M(Label("⇧ Shift"), Modifiers.Shift, 2f)
            });

            rows.Add(BottomRow(null));
            return rows;
        }
    }
}
