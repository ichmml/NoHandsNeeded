using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Tastatur
{
    /// <summary>
    /// Tastatur auf dem Windows-Sperrbildschirm. Windows erlaubt das nur, wenn die Tastatur signiert,
    /// unter "Programme" installiert und als Hilfsmittel (Ease of Access) registriert ist und uiAccess hat.
    /// Siehe Microsoft-Doku "Ease of Access Assistive Technology Registration".
    /// </summary>
    internal static class LockScreen
    {
        public const string AtKey = "MDM3D_Bildschirmtastatur_v1";
        private const string AtsPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\" + AtKey;
        private const string ConfigPath = @"Software\Microsoft\Windows NT\CurrentVersion\Accessibility";
        private const string TempPath = @"Software\Microsoft\Windows NT\CurrentVersion\AccessibilityTemp";

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseDesktop(IntPtr h);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr h, int index, [Out] char[] info, int length, out int needed);

        /// <summary>Läuft die Tastatur gerade auf dem Sperrbildschirm (sicherer Desktop "Winlogon")?</summary>
        public static bool OnSecureDesktop()
        {
            try
            {
                IntPtr d = OpenInputDesktop(0, false, 0x0001);
                if (d == IntPtr.Zero) return false;
                try
                {
                    var buf = new char[256]; int n;
                    if (!GetUserObjectInformation(d, 2, buf, buf.Length * 2, out n)) return false;
                    string name = new string(buf).TrimEnd('\0');
                    return name.Equals("Winlogon", StringComparison.OrdinalIgnoreCase);
                }
                finally { CloseDesktop(d); }
            }
            catch { return false; }
        }

        /// <summary>Sperrbildschirm-Modus: Tastatur läuft auf dem sicheren Desktop.</summary>
        public static bool IsLockMode(string[] args)
        {
            // Windows startet registrierte Hilfsmittel auch nach der Anmeldung (mit -easeofaccessname).
            // Entscheidend ist deshalb, ob wir wirklich auf dem sicheren Desktop laufen.
            if (args != null && args.Any(x => x == "/sperrbildschirm")) return true;   // zum Testen
            return OnSecureDesktop();
        }

        public static string ExePath { get { return Application.ExecutablePath; } }

        public static bool InProgramFiles
        {
            get
            {
                try
                {
                    string exe = Path.GetFullPath(ExePath);
                    foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                        if (!string.IsNullOrEmpty(pf) && exe.StartsWith(pf + "\\", StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { }
                return false;
            }
        }

        public static bool IsSigned
        {
            get
            {
                try { X509Certificate.CreateFromSignedFile(ExePath); return true; }
                catch { return false; }
            }
        }

        public static bool IsRegistered
        {
            get
            {
                try { using (var k = Registry.LocalMachine.OpenSubKey(AtsPath)) return k != null; }
                catch { return false; }
            }
        }

        public static bool Ready { get { return InProgramFiles && IsSigned && IsRegistered; } }

        public static bool Enabled
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(ConfigPath))
                    {
                        string v = k == null ? null : k.GetValue("Configuration") as string;
                        return v != null && v.Split(',').Any(x => x.Trim() == AtKey);
                    }
                }
                catch { return false; }
            }
        }

        /// <summary>Text für die Einstellungen.</summary>
        public static string Status()
        {
            if (!Ready)
            {
                var missing = new List<string>();
                if (!InProgramFiles) missing.Add(L.T("nicht mit dem Installer unter \"Programme\" installiert"));
                if (!IsSigned) missing.Add(L.T("nicht signiert"));
                if (!IsRegistered) missing.Add(L.T("nicht als Hilfsmittel bei Windows angemeldet"));
                return L.T("Geht erst mit der installierten, signierten Version.") + " (" + string.Join(", ", missing) + ")\n" +
                       L.T("Bis dahin: Auf dem Sperrbildschirm unten rechts das Barrierefreiheit-Symbol anklicken und die Windows-Bildschirmtastatur nehmen. Oder Windows Hello (Gesicht) einrichten, dann braucht es keine PIN.");
            }
            return Enabled ? L.T("Die Tastatur erscheint auf dem Sperrbildschirm.") : L.T("Bereit. Mit dem Schalter erscheint die Tastatur auch auf dem Sperrbildschirm.");
        }

        /// <summary>Auf dem Sperrbildschirm anzeigen an/aus (HKCU ...\Accessibility\Configuration).</summary>
        public static string SetEnabled(bool on)
        {
            if (!Ready) return Status();
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(ConfigPath))
                {
                    var list = ((k.GetValue("Configuration") as string) ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0 && x != AtKey).ToList();
                    if (on) list.Add(AtKey);
                    k.SetValue("Configuration", string.Join(",", list), RegistryValueKind.String);
                }
                Log.Write("Sperrbildschirm: " + (on ? "an" : "aus"));
                return on ? L.T("Die Tastatur erscheint jetzt auf dem Sperrbildschirm.") : L.T("Die Tastatur erscheint nicht mehr auf dem Sperrbildschirm.");
            }
            catch (Exception e) { return L.T("Hat nicht geklappt: ") + e.Message; }
        }

        /// <summary>Ease of Access Bescheid geben: 3 = gestartet, 2 = beendet, dann Win+U.</summary>
        public static void Notify(bool started)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(TempPath))
                    k.SetValue(AtKey, started ? 3 : 2, RegistryValueKind.DWord);
                InputSender.SendVk(0x55, Modifiers.Win);   // Win+U
            }
            catch (Exception e) { Log.Write("Ease of Access benachrichtigen: " + e.Message); }
        }
    }

    /// <summary>Leere Vorschläge: auf dem Sperrbildschirm soll nichts Gelerntes sichtbar werden.</summary>
    internal class NoSuggestions : ISuggester
    {
        public List<string> Suggest(string typed, string prev, int max, bool capitalize) { return new List<string>(); }
        public List<string> SuggestNumbers(string typed, int max) { return new List<string>(); }
    }

    /// <summary>
    /// Nur die Tastatur, für den Sperrbildschirm: kein Tresor, keine Makros, kein Lernen,
    /// keine Vorschläge, kein Tray-Symbol, keine Einstellungen.
    /// </summary>
    internal class LockScreenContext : ApplicationContext
    {
        public LockScreenContext()
        {
            var settings = new Settings();   // Standardwerte, nichts wird gespeichert
            settings.SuggestionRows = 1;
            L.Init("auto");
            var engine = new KeyboardEngine(new SystemOutput(), new NoSuggestions(), settings);
            var kb = new KeyboardForm(settings, engine) { LockMode = true };
            kb.MacroSource = () => new List<Macro>();
            kb.LanguageLabel = () => "";
            kb.SetLayout(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant(), true);
            kb.FormClosed += (o, e) => { LockScreen.Notify(false); ExitThread(); };
            Log.Write("Start auf dem Sperrbildschirm");
            LockScreen.Notify(true);
            kb.Show();
        }
    }
}
