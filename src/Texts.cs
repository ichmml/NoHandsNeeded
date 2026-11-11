using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Tastatur
{
    /// <summary>
    /// Texte der Oberfläche in der Sprache des Nutzers. Schlüssel ist der deutsche Text.
    /// Dateien: Texte/xx.txt mit "deutscher Text&lt;TAB&gt;Übersetzung", eingebettet als texte_xx.txt.
    /// Fehlt eine Übersetzung: Englisch, sonst Deutsch.
    /// </summary>
    internal static class L
    {
        public static readonly string[] Languages = { "de", "en", "nl", "fr", "da", "pl", "cs", "it", "es", "tr", "ru" };

        /// <summary>Eigenname der Sprache für die Auswahl in den Einstellungen.</summary>
        public static readonly Dictionary<string, string> NativeNames = new Dictionary<string, string>
        {
            { "de", "Deutsch" }, { "en", "English" }, { "nl", "Nederlands" }, { "fr", "Français" }, { "da", "Dansk" },
            { "pl", "Polski" }, { "cs", "Čeština" }, { "it", "Italiano" }, { "es", "Español" }, { "tr", "Türkçe" }, { "ru", "Русский" }, // i18n-ok
        };

        private static Dictionary<string, string> _cur = new Dictionary<string, string>();
        private static Dictionary<string, string> _en = new Dictionary<string, string>();
        public static string Lang = "de";
        public static Func<string, Stream> Resource = name => Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

        /// <summary>Welche Sprache gilt? "auto" = Windows-Anzeigesprache, sonst Englisch.</summary>
        public static string Resolve(string setting)
        {
            string code = setting;
            if (string.IsNullOrEmpty(code) || code == "auto") code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            return Array.IndexOf(Languages, code) >= 0 ? code : "en";
        }

        public static void Init(string setting)
        {
            Lang = Resolve(setting);
            _en = Lang == "de" ? new Dictionary<string, string>() : Load("en");
            _cur = Lang == "de" || Lang == "en" ? (Lang == "en" ? _en : new Dictionary<string, string>()) : Load(Lang);
            KeyLayouts.Label = T;
        }

        public static Dictionary<string, string> Load(string code)
        {
            var d = new Dictionary<string, string>();
            try
            {
                using (var s = Resource("texte_" + code + ".txt"))
                {
                    if (s == null) return d;
                    using (var r = new StreamReader(s, Encoding.UTF8))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            if (line.Length == 0 || line.StartsWith("#")) continue;
                            int tab = line.IndexOf('\t');
                            if (tab <= 0) continue;
                            string v = Unescape(line.Substring(tab + 1));
                            if (v.Length > 0) d[Unescape(line.Substring(0, tab))] = v;
                        }
                    }
                }
            }
            catch { }
            return d;
        }

        public static string Unescape(string s)
        {
            return s.Replace("\\n", "\n").Replace("\\t", "\t");
        }

        public static string Escape(string s)
        {
            return s.Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        /// <summary>Text übersetzen. Unbekannt: Englisch, sonst der deutsche Text.</summary>
        public static string T(string german)
        {
            if (german == null || Lang == "de") return german;
            string v;
            if (_cur.TryGetValue(german, out v)) return v;
            if (_en.TryGetValue(german, out v)) return v;
            return german;
        }

        /// <summary>Übersetzen und Platzhalter {0}, {1} füllen.</summary>
        public static string F(string german, params object[] args)
        {
            try { return string.Format(T(german), args); }
            catch (FormatException) { return string.Format(german, args); }
        }
    }
}
