using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Tastatur
{
    /// <summary>Einstellungen als einfache Textdatei (Schlüssel = Wert), gut lesbar und von Hand änderbar.</summary>
    internal class Settings
    {
        public int X = -1, Y = -1, Width = 1100, Height = 380;   // Width/Height: zuletzt tatsächliche Pixel (nur Info)
        public double WidthPercent = 60, HeightPercent = 31;     // maßgeblich: Größe in % der Arbeitsfläche des Bildschirms
        public string DockPosition = "rechts";                    // Standardposition unten: rechts, mitte, links
        public const double DefaultWidthPercent = 60, DefaultHeightPercent = 31;
        public int OpacityPercent = 95;
        public bool DwellEnabled = false;
        public string DonateLink = DefaultDonateLink;     // Kaffee-Link, öffnet sich nur auf Klick
        public const string DefaultDonateLink = "https://ko-fi.com/MDM3D";
        public bool ShowInputField = true;              // Eingabefeld oben links: zeigt, was ich tippe
        public int InputFieldWidth = 2;                 // in Vorschlagsfeldern (1 bis 4)
        public bool PasswordReveal = true;              // im Passwortfeld 👁 zum kurzen Anzeigen
        public int RevealSeconds = 10;                  // Klartext automatisch ausblenden nach
        public bool ShowPhysicalKeys = true;            // echte Tastendrücke (Quadstick) rot anzeigen
        public int PhysicalGlowMs = 1500;               // so lange leuchten sie nach dem Loslassen
        public bool ContinueAfterClick = true;          // Wort pro Fenster auch nach Klick ins Fenster weiterführen
        public int DwellMs = 800;
        public int SuggestionCount = 5;
        public bool AutoCapitalize = true;
        public bool AutoSpaceAfterSuggestion = true;
        public bool AutoSpaceAfterPunctuation = true;
        public bool OpenAtMouse = true;                 // beim Kürzel dort öffnen, wo die Maus ist
        public string MousePlace = "unten-mitte";
        public static readonly string[] MousePlaces = { "unten-mitte", "unten-rechts", "unten-links", "oben-mitte", "oben-rechts", "oben-links" };   // i18n-ok      // Lage zur Maus: unten/oben + mitte/rechts/links   // nach . , ! ? : ; von selbst ein Leerzeichen
        public bool Autostart = false;
        public bool StartCollapsed = false;
        public int MiniX = -1, MiniY = -1;
        public string Theme = "dunkel"; // i18n-ok

        // Vorschlagsleiste
        public int SuggestionRows = 1;              // 1 oder 2
        public int SuggestionFontPercent = 40;      // Schrift der Vorschläge in % der Zeilenhöhe

        // Deckkraft des runden Mini-Knopfs (eingeklappt)
        public int MiniOpacityPercent = 100;

        // globale Kürzel ("" = aus)
        public string HotkeyShow = "Pause";
        public string HotkeyMacros = "NumMinus";
        public string HotkeyLanguage = "";

        // Sprachen
        public string ActiveLanguages = "de,en";        // Reihenfolge = Reihenfolge beim Umschalten
        public string LanguageMode = "de";              // Kürzel oder "mix"
        public string KeyLayout = "sprache";            // "sprache" = passend zur Sprache, sonst fest "de" oder "en"
        public bool OfferMix = true;
        public bool PerProgramLanguage = false;
        public string BottomChars = KeyLayouts.DefaultBottomChars;   // vier Zeichen unten, je 1 Zeichen + optional Shift-Zeichen
        public string BackupFolder = "";
        public bool TaskbarButton = true;               // eigener Knopf in der Taskleiste                // zuletzt benutzter Ordner für Sicherungen
        public string ProgramLanguages = "";            // z. B. "chrome=en;outlook=de"
        public string LanguageSource = PackDownloader.DefaultSource;

        // Lernen
        public bool LearningEnabled = true;
        public int LearnMinNew = 2;                     // neue Wörter ab so vielen Malen vorschlagen
        public bool LearnEmails = true;
        public bool LearnIban = true;                   // IBAN merken und vorschlagen (nur gültige)
        public bool LearnSecretNumbers = true;          // PINs und Kartennummern lernen (verschlüsselt gespeichert)
        public bool EmailFieldSuggest = true;           // in E-Mail-Feldern gleich die Adressen zeigen                 // E-Mail-Adressen merken und vorschlagen
        public bool LearnNumbers = true;                // auch Datum, Telefonnummern, Postleitzahlen

        // Passwort-Tresor
        public bool VaultEnabled = true;
        public bool VaultAutoShow = true;               // in Passwortfeldern automatisch zeigen
        public bool VaultOfferSave = true;              // nach Eingabe "Im Tresor speichern?" anbieten
        public int VaultAutoLockMinutes = 15;           // nur mit PIN, 0 = nie
        public int GeneratorLength = 16;
        public bool GeneratorSymbols = true;

        // Verschieben und Größe
        public bool HoldToDrag = true;                  // Verschieben durch langes Draufbleiben
        public int LongPressMs = 500;                   // Maustaste gedrückt halten
        public int DwellDragMs = 0;                     // Verweilen bis Ziehen, 0 = doppelte Verweilzeit
        public int DropMs = 1000;                       // so lange still halten zum Absetzen
        public int ResizeBorder = 12;                   // Greifzone am Rand in Pixel

        /// <summary>Version der Datei, für einmalige Umstellungen alter Werte.</summary>
        public const int CurrentVersion = 5;
        public int FileVersion = CurrentVersion;

        public int EffectiveDwellDragMs { get { return DwellDragMs > 0 ? Math.Max(DwellDragMs, DwellMs + 300) : DwellMs * 2; } }

        public Dictionary<string, string> ProgramLanguageMap()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (ProgramLanguages ?? "").Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string prog = part.Substring(0, eq).Trim(), lang = part.Substring(eq + 1).Trim();
                if (prog.Length > 0 && lang.Length > 0) d[prog] = lang;
            }
            return d;
        }

        public void SetProgramLanguage(string program, string mode)
        {
            var d = ProgramLanguageMap();
            d[program] = mode;
            var parts = new List<string>();
            foreach (var kv in d) parts.Add(kv.Key + "=" + kv.Value);
            ProgramLanguages = string.Join(";", parts);
        }

        public static string DataDir { get; private set; }
        private static string FilePath { get { return Path.Combine(DataDir, "einstellungen.txt"); } }

        /// <summary>Portabel: Daten neben der .exe (NoHandsNeeded-Daten), falls beschreibbar, sonst in %APPDATA%\NoHandsNeeded.</summary>
        public static void InitDataDir()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string portable = Path.Combine(exeDir, DataFolderName);
            string oldPortable = Path.Combine(exeDir, OldDataFolderName);
            MigrateFolder(oldPortable, portable);
            if (!Directory.Exists(portable) && Directory.Exists(oldPortable)) portable = oldPortable; // Umbenennen ging nicht
            try
            {
                Directory.CreateDirectory(portable);
                string test = Path.Combine(portable, ".schreibtest");
                File.WriteAllText(test, "ok");
                File.Delete(test);
                DataDir = portable;
            }
            catch
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                DataDir = Path.Combine(appData, AppInfo.Name);
                MigrateFolder(Path.Combine(appData, "Tastatur"), DataDir);
                if (!Directory.Exists(DataDir) && Directory.Exists(Path.Combine(appData, "Tastatur")))
                    DataDir = Path.Combine(appData, "Tastatur"); // Umbenennen ging nicht, alten Ordner weiter benutzen
                Directory.CreateDirectory(DataDir);
            }
        }

        public const string DataFolderName = "NoHandsNeeded-Daten";
        public const string OldDataFolderName = "Tastatur-Daten";   // Name bis 1.01

        /// <summary>Alten Datenordner (vor der Umbenennung) übernehmen, falls der neue noch nicht existiert.
        /// Klappt das Umbenennen nicht (Datei offen, keine Rechte), wird der alte Ordner weiter benutzt.</summary>
        internal static void MigrateFolder(string oldDir, string newDir)
        {
            try
            {
                if (Directory.Exists(oldDir) && !Directory.Exists(newDir)) Directory.Move(oldDir, newDir);
            }
            catch { }
        }

        public static Settings Load() { return LoadFrom(FilePath); }

        /// <summary>Kein Einstellungsdatei vorhanden: allererster Start.</summary>
        public bool FirstStart;
        public string SystemLanguage = "de";        // beim ersten Start erkannte Windows-Sprache
        public string UiLanguage = "auto";          // Sprache der Oberfläche, "auto" = Windows
        public bool TourSeen = false;

        /// <summary>Sprachen beim ersten Start nach der Windows-Sprache: sie zuerst, Englisch dazu.</summary>
        public void ApplySystemLanguage(string twoLetter, Func<string, bool> available)
        {
            string code = (twoLetter ?? "").ToLowerInvariant();
            SystemLanguage = code;
            if (code == "en" || !available(code)) { ActiveLanguages = "en"; LanguageMode = "en"; return; }
            ActiveLanguages = code + ",en";
            LanguageMode = code;
        }

        public static Settings LoadFrom(string path)
        {
            var s = new Settings();
            if (!File.Exists(path))
            {
                s.FirstStart = true;
                s.ApplySystemLanguage(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                    c => Array.Exists(LanguageManager.BuiltIns, b => b.Code == c));
                return s;
            }
            s.FileVersion = 1;   // Dateien ohne Versionsangabe stammen von vor der Umstellung
            try
            {
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    s.Apply(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
                }
            }
            catch { }
            s.Migrate();
            return s;
        }

        /// <summary>Einmalige Umstellungen alter Standardwerte. Selbst geänderte Werte bleiben.</summary>
        public void Migrate()
        {
            if (FileVersion < 2)
            {
                if (LongPressMs == 1000) LongPressMs = 500;
                if (Array.IndexOf(PackDownloader.OldDefaultSources, LanguageSource) >= 0) LanguageSource = PackDownloader.DefaultSource;
            }
            if (FileVersion < 3)
            {
                // F13 bis F24 kann der Quadstick nicht senden
                if (HotkeyShow == "F14") HotkeyShow = "Pause";
                if (HotkeyMacros == "F15") HotkeyMacros = "NumMinus";
            }
            if (FileVersion < 4)
            {
                // Größe jetzt in Prozent, einmal auf die neue Standardgröße (wie die alte Tastatur), unten rechts
                WidthPercent = DefaultWidthPercent; HeightPercent = DefaultHeightPercent;
                DockPosition = "rechts"; X = -1; Y = -1;
            }
            if (FileVersion < 5)
            {
                if (DonateLink == "https://ko-fi.com/ichmml") DonateLink = DefaultDonateLink;
            }
            FileVersion = CurrentVersion;
        }

        /// <summary>Link im Standardbrowser öffnen (nur https).</summary>
        public static void OpenLink(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            try { System.Diagnostics.Process.Start(url); }
            catch (Exception e) { Log.Write("Link konnte nicht geöffnet werden: " + e.Message); }
        }

        private static double D(string v, double def)
        {
            double r; return double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        private static int I(string v, int def)
        {
            int r; return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        private static bool B(string v, bool def)
        {
            v = v.ToLowerInvariant();
            if (v == "ja" || v == "true" || v == "1") return true;
            if (v == "nein" || v == "false" || v == "0") return false;
            return def;
        }

        private void Apply(string k, string v)
        {
            switch (k)
            {
                case "fenster_x": X = I(v, X); break;
                case "fenster_breite_prozent": WidthPercent = Math.Max(20, Math.Min(100, D(v, WidthPercent))); break;
                case "fenster_hoehe_prozent": HeightPercent = Math.Max(15, Math.Min(80, D(v, HeightPercent))); break;
                case "fenster_position": DockPosition = v == "links" || v == "mitte" ? v : "rechts"; break;
                case "spenden_link": DonateLink = v.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? v : DefaultDonateLink; break;
                case "oberflaeche_sprache": UiLanguage = v.Length > 0 ? v.ToLowerInvariant() : "auto"; break;
                case "einfuehrung_gesehen": TourSeen = B(v, TourSeen); break;
                case "tastenlayout": KeyLayout = v == "de" || v == "en" ? v : "sprache"; break;
                case "eingabefeld": ShowInputField = B(v, ShowInputField); break;
                case "eingabefeld_breite": InputFieldWidth = Math.Max(1, Math.Min(4, I(v, InputFieldWidth))); break;
                case "passwort_klartext": PasswordReveal = B(v, PasswordReveal); break;
                case "klartext_sekunden": RevealSeconds = Math.Max(3, Math.Min(30, I(v, RevealSeconds))); break;
                case "tasten_anzeigen": ShowPhysicalKeys = B(v, ShowPhysicalKeys); break;
                case "tasten_nachleuchten_ms": PhysicalGlowMs = Math.Max(0, Math.Min(5000, I(v, PhysicalGlowMs))); break;
                case "weiterschreiben_nach_klick": ContinueAfterClick = B(v, ContinueAfterClick); break;
                case "lernen_zahlen": LearnNumbers = B(v, LearnNumbers); break;
                case "lernen_emails": LearnEmails = B(v, LearnEmails); break;
                case "lernen_iban": LearnIban = B(v, LearnIban); break;
                case "lernen_pins_karten": LearnSecretNumbers = B(v, LearnSecretNumbers); break;
                case "email_felder": EmailFieldSuggest = B(v, EmailFieldSuggest); break;
                case "fenster_y": Y = I(v, Y); break;
                case "fenster_breite": Width = I(v, Width); break;
                case "fenster_hoehe": Height = I(v, Height); break;
                case "deckkraft_prozent": OpacityPercent = Math.Max(30, Math.Min(100, I(v, OpacityPercent))); break;
                case "verweilklick": DwellEnabled = B(v, DwellEnabled); break;
                case "verweilzeit_ms": DwellMs = Math.Max(200, Math.Min(5000, I(v, DwellMs))); break;
                case "anzahl_vorschlaege": SuggestionCount = Math.Max(1, Math.Min(8, I(v, SuggestionCount))); break;
                case "satzanfang_gross": AutoCapitalize = B(v, AutoCapitalize); break;
                case "leerzeichen_nach_vorschlag": AutoSpaceAfterSuggestion = B(v, AutoSpaceAfterSuggestion); break;
                case "leerzeichen_nach_satzzeichen": AutoSpaceAfterPunctuation = B(v, AutoSpaceAfterPunctuation); break;
                case "an_maus_oeffnen": OpenAtMouse = B(v, OpenAtMouse); break;
                case "an_maus_lage": if (Array.IndexOf(MousePlaces, v.Trim()) >= 0) MousePlace = v.Trim(); break;
                case "autostart": Autostart = B(v, Autostart); break;
                case "eingeklappt_starten": StartCollapsed = B(v, StartCollapsed); break;
                case "mini_x": MiniX = I(v, MiniX); break;
                case "mini_y": MiniY = I(v, MiniY); break;
                case "farbschema": Theme = v == "hell" ? "hell" : "dunkel"; break;
                case "vorschlag_zeilen": SuggestionRows = Math.Max(1, Math.Min(2, I(v, SuggestionRows))); break;
                case "vorschlag_schrift_prozent": SuggestionFontPercent = Math.Max(20, Math.Min(70, I(v, SuggestionFontPercent))); break;
                case "mini_deckkraft_prozent": MiniOpacityPercent = Math.Max(20, Math.Min(100, I(v, MiniOpacityPercent))); break;
                case "kuerzel_einblenden": HotkeyShow = v; break;
                case "kuerzel_makros": HotkeyMacros = v; break;
                case "kuerzel_sprache": HotkeyLanguage = v; break;
                case "sprachen_aktiv": ActiveLanguages = v; break;
                case "sprache": LanguageMode = v.ToLowerInvariant(); break;
                case "sprache_gemischt_anbieten": OfferMix = B(v, OfferMix); break;
                case "sprache_pro_programm": PerProgramLanguage = B(v, PerProgramLanguage); break;
                case "programm_sprachen": ProgramLanguages = v; break;
                case "sicherung_ordner": BackupFolder = v; break;
                case "taskleiste_knopf": TaskbarButton = B(v, TaskbarButton); break;
                case "sonderzeichen_unten": BottomChars = KeyLayouts.NormalizeBottomChars(v); break;
                case "sprachen_quelle": if (PackDownloader.IsValidSource(v)) LanguageSource = v.Trim(); break;
                case "lernen": LearningEnabled = B(v, LearningEnabled); break;
                case "lernen_neue_woerter_ab": LearnMinNew = Math.Max(1, Math.Min(5, I(v, LearnMinNew))); break;
                case "tresor": VaultEnabled = B(v, VaultEnabled); break;
                case "tresor_automatisch_zeigen": VaultAutoShow = B(v, VaultAutoShow); break;
                case "tresor_speichern_anbieten": VaultOfferSave = B(v, VaultOfferSave); break;
                case "tresor_sperren_minuten": VaultAutoLockMinutes = Math.Max(0, Math.Min(240, I(v, VaultAutoLockMinutes))); break;
                case "generator_laenge": GeneratorLength = Math.Max(8, Math.Min(32, I(v, GeneratorLength))); break;
                case "generator_sonderzeichen": GeneratorSymbols = B(v, GeneratorSymbols); break;
                case "einstellungen_version": FileVersion = I(v, 1); break;
                case "verschieben_lang_draufbleiben": HoldToDrag = B(v, HoldToDrag); break;
                case "verschieben_gedrueckt_ms": LongPressMs = Math.Max(300, Math.Min(5000, I(v, LongPressMs))); break;
                case "verschieben_verweilen_ms": DwellDragMs = Math.Max(0, Math.Min(10000, I(v, DwellDragMs))); break;
                case "absetzen_ms": DropMs = Math.Max(300, Math.Min(5000, I(v, DropMs))); break;
                case "rand_greifzone": ResizeBorder = Math.Max(4, Math.Min(40, I(v, ResizeBorder))); break;
            }
        }

        private static string Yn(bool b) { return b ? "ja" : "nein"; }

        public void Save() { SaveTo(FilePath); }

        public void SaveTo(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Einstellungen von " + AppInfo.Name);
            sb.AppendLine("einstellungen_version = " + CurrentVersion);
            sb.AppendLine("# Kann von Hand bearbeitet werden, wirkt beim nächsten Start.");
            sb.AppendLine("fenster_x = " + X);
            sb.AppendLine("fenster_y = " + Y);
            sb.AppendLine("fenster_breite_prozent = " + WidthPercent.ToString("0.#", CultureInfo.InvariantCulture));
            sb.AppendLine("fenster_hoehe_prozent = " + HeightPercent.ToString("0.#", CultureInfo.InvariantCulture));
            sb.AppendLine("# Position beim Zurücksetzen: rechts, mitte oder links");
            sb.AppendLine("fenster_position = " + DockPosition);
            sb.AppendLine("fenster_breite = " + Width);
            sb.AppendLine("fenster_hoehe = " + Height);
            sb.AppendLine("deckkraft_prozent = " + OpacityPercent);
            sb.AppendLine("verweilklick = " + Yn(DwellEnabled));
            sb.AppendLine("verweilzeit_ms = " + DwellMs);
            sb.AppendLine("# Vorschläge pro Zeile");
            sb.AppendLine("anzahl_vorschlaege = " + SuggestionCount);
            sb.AppendLine("vorschlag_zeilen = " + SuggestionRows);
            sb.AppendLine("vorschlag_schrift_prozent = " + SuggestionFontPercent);
            sb.AppendLine("satzanfang_gross = " + Yn(AutoCapitalize));
            sb.AppendLine("leerzeichen_nach_vorschlag = " + Yn(AutoSpaceAfterSuggestion));
            sb.AppendLine("leerzeichen_nach_satzzeichen = " + Yn(AutoSpaceAfterPunctuation));
            sb.AppendLine("an_maus_oeffnen = " + Yn(OpenAtMouse));
            sb.AppendLine("an_maus_lage = " + MousePlace + "   # unten-mitte, unten-rechts, unten-links, oben-mitte, oben-rechts, oben-links");
            sb.AppendLine("autostart = " + Yn(Autostart));
            sb.AppendLine("eingeklappt_starten = " + Yn(StartCollapsed));
            sb.AppendLine("mini_x = " + MiniX);
            sb.AppendLine("mini_y = " + MiniY);
            sb.AppendLine("farbschema = " + Theme);
            sb.AppendLine("mini_deckkraft_prozent = " + MiniOpacityPercent);
            sb.AppendLine("# Globale Kürzel, z. B. F13 oder Strg+Alt+Shift+G, leer = aus");
            sb.AppendLine("kuerzel_einblenden = " + HotkeyShow);
            sb.AppendLine("kuerzel_makros = " + HotkeyMacros);
            sb.AppendLine("kuerzel_sprache = " + HotkeyLanguage);
            sb.AppendLine("# Sprachen");
            sb.AppendLine("sprachen_aktiv = " + ActiveLanguages);
            sb.AppendLine("sprache = " + LanguageMode);
            sb.AppendLine("sprache_gemischt_anbieten = " + Yn(OfferMix));
            sb.AppendLine("sprache_pro_programm = " + Yn(PerProgramLanguage));
            sb.AppendLine("sicherung_ordner = " + BackupFolder);
            sb.AppendLine("taskleiste_knopf = " + Yn(TaskbarButton));
            sb.AppendLine("sonderzeichen_unten = " + BottomChars);
            sb.AppendLine("programm_sprachen = " + ProgramLanguages);
            sb.AppendLine("sprachen_quelle = " + LanguageSource);
            sb.AppendLine("# Sprache der Oberfläche: auto (wie Windows) oder de, en, nl, fr, da, pl, cs, it, es, tr, ru");
            sb.AppendLine("oberflaeche_sprache = " + UiLanguage);
            sb.AppendLine("einfuehrung_gesehen = " + Yn(TourSeen));
            sb.AppendLine("# Tastenlayout: sprache (passend zur Sprache), de oder en");
            sb.AppendLine("tastenlayout = " + KeyLayout);
            sb.AppendLine("# Lernen");
            sb.AppendLine("lernen = " + Yn(LearningEnabled));
            sb.AppendLine("lernen_neue_woerter_ab = " + LearnMinNew);
            sb.AppendLine("lernen_zahlen = " + Yn(LearnNumbers));
            sb.AppendLine("lernen_emails = " + Yn(LearnEmails));
            sb.AppendLine("lernen_iban = " + Yn(LearnIban));
            sb.AppendLine("lernen_pins_karten = " + Yn(LearnSecretNumbers));
            sb.AppendLine("email_felder = " + Yn(EmailFieldSuggest));
            sb.AppendLine("# Kaffee-Link (öffnet sich nur, wenn man darauf klickt)");
            sb.AppendLine("spenden_link = " + DonateLink);
            sb.AppendLine("# Eingabefeld oben links: zeigt, was man tippt");
            sb.AppendLine("eingabefeld = " + Yn(ShowInputField));
            sb.AppendLine("eingabefeld_breite = " + InputFieldWidth);
            sb.AppendLine("passwort_klartext = " + Yn(PasswordReveal));
            sb.AppendLine("klartext_sekunden = " + RevealSeconds);
            sb.AppendLine("# Echte Tastendrücke (z. B. vom Quadstick) anzeigen");
            sb.AppendLine("tasten_anzeigen = " + Yn(ShowPhysicalKeys));
            sb.AppendLine("tasten_nachleuchten_ms = " + PhysicalGlowMs);
            sb.AppendLine("# Wort pro Fenster merken: auch wenn das Fenster per Klick in den Inhalt zurückkommt");
            sb.AppendLine("weiterschreiben_nach_klick = " + Yn(ContinueAfterClick));
            sb.AppendLine("# Passwort-Tresor (die Passwörter selbst stehen verschlüsselt in tresor.dat)");
            sb.AppendLine("tresor = " + Yn(VaultEnabled));
            sb.AppendLine("tresor_automatisch_zeigen = " + Yn(VaultAutoShow));
            sb.AppendLine("tresor_speichern_anbieten = " + Yn(VaultOfferSave));
            sb.AppendLine("tresor_sperren_minuten = " + VaultAutoLockMinutes);
            sb.AppendLine("generator_laenge = " + GeneratorLength);
            sb.AppendLine("generator_sonderzeichen = " + Yn(GeneratorSymbols));
            sb.AppendLine("# Verschieben und Größe");
            sb.AppendLine("verschieben_lang_draufbleiben = " + Yn(HoldToDrag));
            sb.AppendLine("verschieben_gedrueckt_ms = " + LongPressMs);
            sb.AppendLine("# 0 = doppelte Verweilzeit");
            sb.AppendLine("verschieben_verweilen_ms = " + DwellDragMs);
            sb.AppendLine("absetzen_ms = " + DropMs);
            sb.AppendLine("rand_greifzone = " + ResizeBorder);
            try { File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false)); } catch { }
        }

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "NoHandsNeeded";
        private const string OldRunName = "Bildschirmtastatur";

        /// <summary>Autostart über HKCU\...\Run setzen oder entfernen (keine Adminrechte nötig).</summary>
        public static void ApplyAutostart(bool on)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (key.GetValue(OldRunName) != null) key.DeleteValue(OldRunName, false); // Name bis 1.01: Bildschirmtastatur
                    if (on)
                        key.SetValue(RunName, "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                    else if (key.GetValue(RunName) != null)
                        key.DeleteValue(RunName, false);
                }
            }
            catch { }
        }
    }
}
