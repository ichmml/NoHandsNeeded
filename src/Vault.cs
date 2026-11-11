using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Accessibility;

namespace Tastatur
{
    internal class VaultEntry
    {

        public string Name = "";
        public string User = "";
        public string Password = "";
        public string Match = "";      // Stichwörter für Fenstertitel/Programm, z. B. "gmail, google"
        public DateTime Created = DateTime.Now;
        public DateTime LastUsed = DateTime.MinValue;
        public DateTime Changed = DateTime.MinValue;   // Passwort zuletzt geändert (für den Abgleich zweier PCs)

        /// <summary>Jüngster Zeitpunkt, an dem der Eintrag angelegt, geändert oder benutzt wurde.</summary>
        public DateTime Newest { get { var t = Created > LastUsed ? Created : LastUsed; return Changed > t ? Changed : t; } }

        public VaultEntry Clone() { return (VaultEntry)MemberwiseClone(); }

        /// <summary>Wie gut passt der Eintrag zum aktiven Fenster? 0 = gar nicht.</summary>
        public int Score(string windowTitle, string process)
        {
            string t = ((windowTitle ?? "") + " " + (process ?? "")).ToLowerInvariant();
            int s = 0;
            foreach (var k in (Match ?? "").Split(','))
            {
                string kk = k.Trim().ToLowerInvariant();
                if (kk.Length >= 2 && t.Contains(kk)) s += 10;
            }
            foreach (var w in (Name ?? "").ToLowerInvariant().Split(' ', '-', '.', '/'))
                if (w.Length >= 3 && t.Contains(w)) s += 3;
            return s;
        }
    }

    /// <summary>
    /// Passwort-Tresor. Datei tresor.dat, verschlüsselt mit Windows DPAPI (an das Windows-Konto gebunden).
    /// Mit PIN zusätzlich mit einem aus der PIN abgeleiteten Schlüssel. Passwörter werden nie gelernt,
    /// nie vorgeschlagen und nie an Makros oder die KI gegeben.
    /// </summary>
    internal class Vault
    {
        /// <summary>Einträge, deren Name, Benutzername oder Programm die Buchstaben enthält (Groß/Klein egal).</summary>
        public static List<VaultEntry> Filter(List<VaultEntry> list, string q)
        {
            if (string.IsNullOrEmpty(q)) return list;
            Func<string, bool> has = s => !string.IsNullOrEmpty(s) && s.IndexOf(q.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
            return list.Where(e => has(e.Name) || has(e.User) || has(e.Match)).ToList();
        }

        private const string Magic = "TRESOR1";
        private static readonly byte[] BaseEntropy = Encoding.UTF8.GetBytes("Bildschirmtastatur-Tresor-v1"); // i18n-ok

        public List<VaultEntry> Entries = new List<VaultEntry>();
        public bool HasPin { get; private set; }
        public bool Locked { get; private set; }
        private byte[] _salt;
        private byte[] _pinKey;
        public DateTime LastActivity = DateTime.Now;
        public string FilePath;

        public Vault(string filePath) { FilePath = filePath; }

        public bool Exists { get { return File.Exists(FilePath); } }

        // ---------- Verschlüsselung ----------

        private static byte[] DeriveKey(string pin, byte[] salt)
        {
            using (var kdf = new Rfc2898DeriveBytes(pin, salt, 60000)) return kdf.GetBytes(32);
        }

        private byte[] Entropy()
        {
            if (!HasPin) return BaseEntropy;
            return BaseEntropy.Concat(_pinKey).ToArray();
        }

        private static string Esc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "");
        }

        private static string Unesc(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static string Serialize(List<VaultEntry> entries)
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
                sb.Append(Esc(e.Name)).Append('\t').Append(Esc(e.User)).Append('\t').Append(Esc(e.Password)).Append('\t')
                  .Append(Esc(e.Match)).Append('\t').Append(e.Created.Ticks).Append('\t').Append(e.LastUsed.Ticks).Append('\t').Append(e.Changed.Ticks).Append('\n');
            return sb.ToString();
        }

        public static List<VaultEntry> Deserialize(string text)
        {
            var list = new List<VaultEntry>();
            foreach (var line in text.Split('\n'))
            {
                if (line.Length == 0) continue;
                var p = line.Split('\t');
                if (p.Length < 3) continue;
                var e = new VaultEntry { Name = Unesc(p[0]), User = Unesc(p[1]), Password = Unesc(p[2]) };
                if (p.Length > 3) e.Match = Unesc(p[3]);
                long t;
                if (p.Length > 4 && long.TryParse(p[4], out t)) e.Created = new DateTime(t);
                if (p.Length > 5 && long.TryParse(p[5], out t)) e.LastUsed = new DateTime(t);
                if (p.Length > 6 && long.TryParse(p[6], out t)) e.Changed = new DateTime(t);
                list.Add(e);
            }
            return list;
        }

        /// <summary>Datei lesen. Mit PIN bleibt der Tresor gesperrt, bis Unlock gelingt.</summary>
        public void Load()
        {
            Entries = new List<VaultEntry>();
            HasPin = false; Locked = false; _salt = null; _pinKey = null;
            if (!Exists) return;
            var lines = File.ReadAllLines(FilePath);
            if (lines.Length < 1 || lines[0] != Magic) throw new InvalidDataException(L.T("tresor.dat hat ein unbekanntes Format."));
            var kv = lines.Skip(1).Where(l => l.Contains("=")).ToDictionary(l => l.Substring(0, l.IndexOf('=')), l => l.Substring(l.IndexOf('=') + 1));
            HasPin = kv.ContainsKey("pin") && kv["pin"] == "1";
            if (HasPin)
            {
                _salt = Convert.FromBase64String(kv["salt"]);
                Locked = true;
                return;
            }
            Decrypt(kv["data"]);
        }

        private void Decrypt(string data)
        {
            if (string.IsNullOrEmpty(data)) { Entries = new List<VaultEntry>(); return; }
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(data), Entropy(), DataProtectionScope.CurrentUser);
            try { Entries = Deserialize(Encoding.UTF8.GetString(plain)); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        /// <summary>Mit PIN entsperren. false = PIN falsch.</summary>
        public bool Unlock(string pin)
        {
            if (!HasPin) { Locked = false; return true; }
            var lines = File.ReadAllLines(FilePath);
            var data = lines.FirstOrDefault(l => l.StartsWith("data="));
            _pinKey = DeriveKey(pin, _salt);
            try
            {
                Decrypt(data == null ? "" : data.Substring(5));
                Locked = false;
                LastActivity = DateTime.Now;
                return true;
            }
            catch (CryptographicException)
            {
                _pinKey = null;
                return false;
            }
        }

        public void Lock()
        {
            if (!HasPin) return;
            foreach (var e in Entries) e.Password = null;
            Entries = new List<VaultEntry>();
            _pinKey = null;
            Locked = true;
        }

        public void Save()
        {
            if (Locked) throw new InvalidOperationException(L.T("Tresor ist gesperrt."));
            var plain = Encoding.UTF8.GetBytes(Serialize(Entries));
            byte[] enc;
            try { enc = ProtectedData.Protect(plain, Entropy(), DataProtectionScope.CurrentUser); }
            finally { Array.Clear(plain, 0, plain.Length); }
            var sb = new StringBuilder();
            sb.Append(Magic).Append('\n');
            sb.Append("pin=").Append(HasPin ? "1" : "0").Append('\n');
            if (HasPin) sb.Append("salt=").Append(Convert.ToBase64String(_salt)).Append('\n');
            sb.Append("data=").Append(Convert.ToBase64String(enc)).Append('\n');
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string tmp = FilePath + ".neu";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(FilePath))
            {
                string bak = FilePath + ".bak";
                if (File.Exists(bak)) File.Delete(bak);
                File.Replace(tmp, FilePath, bak);
            }
            else File.Move(tmp, FilePath);
        }

        /// <summary>PIN festlegen, ändern (pin != null) oder entfernen (pin == null). Tresor muss entsperrt sein.</summary>
        public void SetPin(string pin)
        {
            if (Locked) throw new InvalidOperationException(L.T("Tresor ist gesperrt."));
            if (string.IsNullOrEmpty(pin)) { HasPin = false; _salt = null; _pinKey = null; }
            else
            {
                _salt = new byte[16];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(_salt);
                _pinKey = DeriveKey(pin, _salt);
                HasPin = true;
            }
            Save();
        }

        public bool ContainsPassword(string pw)
        {
            return !Locked && Entries.Any(e => e.Password == pw);
        }

        public List<VaultEntry> Sorted(string windowTitle, string process)
        {
            return Entries.OrderByDescending(e => e.Score(windowTitle, process)).ThenByDescending(e => e.LastUsed).ThenBy(e => e.Name).ToList();
        }

        // ---------- Generator ----------

        private const string Lower = "abcdefghijkmnopqrstuvwxyz";   // ohne l
        private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";    // ohne I, O
        private const string Digits = "23456789";                   // ohne 0, 1
        private const string Symbols = "!?#$%&*+-_=.";

        public static string Generate(int length, bool symbols)
        {
            length = Math.Max(8, Math.Min(64, length));
            var groups = new List<string> { Lower, Upper, Digits };
            if (symbols) groups.Add(Symbols);
            string all = string.Concat(groups);
            var chars = new char[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                Func<int, int> next = max =>
                {
                    var b = new byte[4];
                    while (true)
                    {
                        rng.GetBytes(b);
                        uint v = BitConverter.ToUInt32(b, 0);
                        uint limit = uint.MaxValue - uint.MaxValue % (uint)max;
                        if (v < limit) return (int)(v % (uint)max);
                    }
                };
                for (int i = 0; i < length; i++) chars[i] = all[next(all.Length)];
                // jede Gruppe mindestens einmal, an zufälliger Stelle
                var positions = Enumerable.Range(0, length).OrderBy(x => next(1000)).Take(groups.Count).ToList();
                for (int g = 0; g < groups.Count; g++) chars[positions[g]] = groups[g][next(groups[g].Length)];
            }
            return new string(chars);
        }

        /// <summary>Namensvorschlag aus dem Fenstertitel, z. B. "Gmail - Anmelden - Google Chrome" → "Gmail".</summary>
        public static string SuggestName(string windowTitle, string process)
        {
            string t = windowTitle ?? "";
            foreach (var suffix in new[] { " - Google Chrome", " - Microsoft Edge", " — Mozilla Firefox", " - Mozilla Firefox", " - Opera", " - Brave" })
                if (t.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - suffix.Length);
            var parts = t.Split(new[] { " - ", " | ", " – ", " — ", ": " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var generic = new[] { // i18n-ok Titelwörter in mehreren Sprachen
                "anmelden", "login", "log in", "sign in", "einloggen", "passwort", "password", "konto", "account", "willkommen", "welcome" };
            var good = parts.Where(x => !generic.Any(g => x.ToLowerInvariant().Contains(g))).ToList();
            string name = good.Count > 0 ? good.OrderBy(x => x.Length).First() : (parts.Count > 0 ? parts[0] : "");
            if (name.Length == 0) name = process ?? L.T("Passwort");
            if (name.Length > 40) name = name.Substring(0, 40).TrimEnd();
            return name;
        }
    }

    /// <summary>
    /// Erkennt, ob das Feld mit dem Fokus ein Passwortfeld ist. Nutzt die Windows-Bedienungshilfen (MSAA):
    /// Passwortfelder melden den Zustand "geschützt". Funktioniert in normalen Windows-Programmen und in
    /// Browsern (Chrome, Edge, Firefox), sobald dort die Bedienungshilfen aktiv sind.
    /// </summary>
    internal class PasswordWatcher : IDisposable
    {
        private const uint EVENT_OBJECT_FOCUS = 0x8005;
        private const int STATE_SYSTEM_PROTECTED = 0x20000000;
        private const int ES_PASSWORD = 0x20;

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromEvent(IntPtr hwnd, int dwObjectID, int dwChildID,
            [MarshalAs(UnmanagedType.Interface)] out IAccessible ppacc, [MarshalAs(UnmanagedType.Struct)] out object pvarChild);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);

        private readonly Native.WinEventProc _proc;
        private IntPtr _hook;

        public bool IsPassword { get; private set; }
        public string WindowTitle { get; private set; }
        public string Process { get; private set; }
        /// <summary>Fenster und Programm des letzten Passwortfelds (für "Im Tresor speichern?").</summary>
        public string FieldTitle { get; private set; }
        public string FieldProcess { get; private set; }

        /// <summary>Wird bei jedem Wechsel zwischen Passwortfeld und normalem Feld ausgelöst.</summary>
        public event Action<bool> Changed;

        public bool IsEmailField { get; private set; }
        /// <summary>Fokus kommt in ein E-Mail- bzw. Benutzername-Feld oder geht wieder raus.</summary>
        public event Action<bool> EmailFieldChanged;
        public bool IsIbanField { get; private set; }
        public event Action<bool> IbanFieldChanged;

        public PasswordWatcher() { _proc = OnEvent; }

        public void Start()
        {
            try
            {
                _hook = Native.SetWinEventHook(EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS, IntPtr.Zero, _proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
                if (_hook == IntPtr.Zero) Log.Write("Passwortfeld-Erkennung konnte nicht gestartet werden");
            }
            catch (Exception e) { Log.Write("Passwortfeld-Erkennung: " + e.Message); }
        }

        public static bool IsProtectedState(int state) { return (state & STATE_SYSTEM_PROTECTED) != 0; }

        private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            bool pw = false, mail = false, iban = false;
            try
            {
                // klassisches Windows-Eingabefeld mit Passwort-Stil
                var cls = new StringBuilder(64);
                GetClassName(hwnd, cls, 64);
                if (cls.ToString().IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0
                    && (Native.GetWindowLong(hwnd, -16) & ES_PASSWORD) != 0) pw = true;

                if (!pw)
                {
                    IAccessible acc; object child;
                    if (AccessibleObjectFromEvent(hwnd, idObject, idChild, out acc, out child) == 0 && acc != null)
                    {
                        object st = acc.get_accState(child);
                        if (st is int && IsProtectedState((int)st)) pw = true;
                        if (!pw)
                        {
                            object role = null; string name = null, desc = null, help = null;
                            try { role = acc.get_accRole(child); } catch { }
                            int r = role is int ? (int)role : 0;
                            if (r == 42 || r == 46)   // ROLE_SYSTEM_TEXT, ROLE_SYSTEM_COMBOBOX: nur Eingabefelder
                            {
                                try { name = acc.get_accName(child); } catch { }
                                try { desc = acc.get_accDescription(child); } catch { }
                                try { help = acc.get_accHelp(child); } catch { }
                                mail = EmailFieldRules.IsEmailField(name, desc, help);
                                iban = (name + " " + desc + " " + help).IndexOf("IBAN", StringComparison.OrdinalIgnoreCase) >= 0;
                            }
                        }
                        Marshal.ReleaseComObject(acc);
                    }
                }
            }
            catch { }

            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                var sb = new StringBuilder(512);
                GetWindowText(fg, sb, 512);
                WindowTitle = sb.ToString();
                uint pid;
                Native.GetWindowThreadProcessId(fg, out pid);
                using (var p = System.Diagnostics.Process.GetProcessById((int)pid)) Process = p.ProcessName;
            }
            catch { }
            if (pw) { FieldTitle = WindowTitle; FieldProcess = Process; }

            if (pw != IsPassword)
            {
                IsPassword = pw;
                var h = Changed; if (h != null) h(pw);
            }
            if (iban != IsIbanField)
            {
                IsIbanField = iban;
                var hi = IbanFieldChanged; if (hi != null) hi(iban);
            }
            if (mail != IsEmailField)
            {
                IsEmailField = mail;
                var hm = EmailFieldChanged; if (hm != null) hm(mail);
            }
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
