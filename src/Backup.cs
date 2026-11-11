using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Tastatur
{
    /// <summary>
    /// Sicherungsdatei (.tastatur) zum Übertragen auf einen zweiten PC: gelernte Wörter und Wortfolgen,
    /// Zahlen, eigene Begriffe, Makros und auf Wunsch die Passwörter. Innen ein ZIP mit Textdateien.
    /// Mit Passwörtern ist die Datei immer verschlüsselt (AES-256, Schlüssel aus dem Sicherungspasswort).
    /// </summary>
    internal class Backup
    {
        public const string Magic = "TASTATUR-SICHERUNG 1";
        public const string Extension = ".nohands";
        public const string OldExtension = ".tastatur";   // bis 1.01, lässt sich weiter importieren
        private const int Iterations = 150000;

        public const string VaultFile = "tresor.txt";
        public const string NumbersFile = "lernen/zahlen.txt";
        public const string EmailsFile = "lernen/emails.txt";
        public const string CustomFile = "eigene_woerter.txt";
        public const string MacroFile = "makros.yaml";

        /// <summary>Dateiname im ZIP → Inhalt.</summary>
        public Dictionary<string, string> Files = new Dictionary<string, string>();
        public DateTime Created = DateTime.Now;
        public string Computer = "";

        public static string LearnFile(string code) { return "lernen/" + code + ".txt"; }

        public IEnumerable<string> LearnCodes
        {
            get
            {
                return Files.Keys.Where(k => k.StartsWith("lernen/") && k != NumbersFile && k != EmailsFile && k.EndsWith(".txt"))
                    .Select(k => k.Substring(7, k.Length - 11)).OrderBy(c => c);
            }
        }

        public bool HasVault { get { return Files.ContainsKey(VaultFile); } }

        // ---------- Datei schreiben ----------

        public string Write(string password)
        {
            if (HasVault && string.IsNullOrEmpty(password)) throw new InvalidOperationException(L.T("Passwörter nur verschlüsselt sichern"));
            byte[] zip = Zip();
            var sb = new StringBuilder();
            sb.Append(Magic).Append('\n');
            sb.Append("erstellt=").Append(Created.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("computer=").Append(Computer.Replace("\n", "")).Append('\n');
            try
            {
                if (string.IsNullOrEmpty(password))
                {
                    sb.Append("verschluesselt=0\n");
                    sb.Append("daten=").Append(Convert.ToBase64String(zip)).Append('\n');
                    return sb.ToString();
                }
                byte[] salt = Random(16), iv = Random(16);
                byte[] encKey, macKey;
                Keys(password, salt, out encKey, out macKey);
                byte[] cipher;
                using (var aes = Aes.Create())
                {
                    aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    using (var t = aes.CreateEncryptor()) cipher = t.TransformFinalBlock(zip, 0, zip.Length);
                }
                byte[] mac;
                using (var h = new HMACSHA256(macKey)) mac = h.ComputeHash(iv.Concat(cipher).ToArray());
                Array.Clear(encKey, 0, encKey.Length); Array.Clear(macKey, 0, macKey.Length);
                sb.Append("verschluesselt=1\n");
                sb.Append("salt=").Append(Convert.ToBase64String(salt)).Append('\n');
                sb.Append("iv=").Append(Convert.ToBase64String(iv)).Append('\n');
                sb.Append("mac=").Append(Convert.ToBase64String(mac)).Append('\n');
                sb.Append("daten=").Append(Convert.ToBase64String(cipher)).Append('\n');
                return sb.ToString();
            }
            finally { Array.Clear(zip, 0, zip.Length); }
        }

        public void Save(string path, string password)
        {
            string text = Write(password);
            string tmp = path + ".neu";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private byte[] Zip()
        {
            using (var ms = new MemoryStream())
            {
                using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (var kv in Files)
                    {
                        var e = z.CreateEntry(kv.Key, CompressionLevel.Optimal);
                        using (var s = e.Open())
                        {
                            var b = new UTF8Encoding(false).GetBytes(kv.Value);
                            s.Write(b, 0, b.Length);
                        }
                    }
                }
                return ms.ToArray();
            }
        }

        // ---------- Datei lesen ----------

        /// <summary>Kopfzeilen einer Sicherungsdatei. Wirft InvalidDataException bei fremden Dateien.</summary>
        public class Header
        {
            public bool Encrypted;
            public DateTime Created;
            public string Computer = "";
            internal Dictionary<string, string> Values;
        }

        public static Header ReadHeader(string text)
        {
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            if (lines.Count < 2 || lines[0] != Magic) throw new InvalidDataException(L.T("Das ist keine Sicherungsdatei der Tastatur."));
            var kv = new Dictionary<string, string>();
            foreach (var l in lines.Skip(1))
            {
                int i = l.IndexOf('=');
                if (i > 0) kv[l.Substring(0, i)] = l.Substring(i + 1);
            }
            if (!kv.ContainsKey("daten")) throw new InvalidDataException(L.T("Die Sicherungsdatei ist unvollständig."));
            var h = new Header { Values = kv, Encrypted = kv.ContainsKey("verschluesselt") && kv["verschluesselt"] == "1" };
            string c;
            if (kv.TryGetValue("computer", out c)) h.Computer = c;
            DateTime d;
            if (kv.TryGetValue("erstellt", out c) && DateTime.TryParseExact(c, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) h.Created = d;
            return h;
        }

        /// <summary>Inhalt entpacken. null = Passwort falsch.</summary>
        public static Backup Open(Header h, string password)
        {
            byte[] data = Convert.FromBase64String(h.Values["daten"]);
            if (h.Encrypted)
            {
                if (string.IsNullOrEmpty(password)) return null;
                byte[] salt = Convert.FromBase64String(h.Values["salt"]), iv = Convert.FromBase64String(h.Values["iv"]), mac = Convert.FromBase64String(h.Values["mac"]);
                byte[] encKey, macKey;
                Keys(password, salt, out encKey, out macKey);
                try
                {
                    byte[] check;
                    using (var hm = new HMACSHA256(macKey)) check = hm.ComputeHash(iv.Concat(data).ToArray());
                    if (!SameBytes(check, mac)) return null;
                    using (var aes = Aes.Create())
                    {
                        aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                        using (var t = aes.CreateDecryptor()) data = t.TransformFinalBlock(data, 0, data.Length);
                    }
                }
                finally { Array.Clear(encKey, 0, encKey.Length); Array.Clear(macKey, 0, macKey.Length); }
            }
            var b = new Backup { Created = h.Created, Computer = h.Computer };
            try
            {
                using (var ms = new MemoryStream(data))
                using (var z = new ZipArchive(ms, ZipArchiveMode.Read))
                {
                    foreach (var e in z.Entries)
                    {
                        if (e.Length > 50 * 1024 * 1024) continue;
                        using (var s = e.Open())
                        using (var r = new StreamReader(s, Encoding.UTF8))
                            b.Files[e.FullName.Replace('\\', '/')] = r.ReadToEnd();
                    }
                }
            }
            finally { Array.Clear(data, 0, data.Length); }
            return b;
        }

        public static Backup Load(string path, string password)
        {
            return Open(ReadHeader(File.ReadAllText(path, Encoding.UTF8)), password);
        }

        // ---------- Hilfen ----------

        private static void Keys(string password, byte[] salt, out byte[] encKey, out byte[] macKey)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, salt, Iterations))
            {
                var all = kdf.GetBytes(64);
                encKey = all.Take(32).ToArray();
                macKey = all.Skip(32).ToArray();
                Array.Clear(all, 0, all.Length);
            }
        }

        private static byte[] Random(int n)
        {
            var b = new byte[n];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            return b;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static string DefaultFileName(DateTime now)
        {
            return AppInfo.Name + " " + now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + Extension;
        }

        // ---------- Zusammenführen ----------

        /// <summary>Eigene Begriffe: neue Zeilen anhängen, vorhandene (ohne Groß/Klein) bleiben.</summary>
        public static string MergeCustomWords(string local, string imported, out int added)
        {
            added = 0;
            var lines = (local ?? "").Replace("\r", "").Split('\n').ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            var have = new HashSet<string>(lines.Where(l => !l.StartsWith("#")).Select(l => l.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (var raw in (imported ?? "").Replace("\r", "").Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#") || have.Contains(l)) continue;
                lines.Add(l); have.Add(l); added++;
            }
            return string.Join("\r\n", lines) + "\r\n";
        }

        /// <summary>Makros: gleicher Name in gleicher Gruppe wird ersetzt, neue kommen hinten dazu.</summary>
        public static List<Macro> MergeMacros(List<Macro> local, List<Macro> imported, out int added, out int replaced)
        {
            added = replaced = 0;
            var list = local.Select(m => m.Clone()).ToList();
            foreach (var m in imported)
            {
                int i = list.FindIndex(x => string.Equals(x.Name, m.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Group, m.Group, StringComparison.OrdinalIgnoreCase));
                if (i < 0) { list.Add(m.Clone()); added++; }
                else if (MacroStore.Serialize(new List<Macro> { list[i] }) != MacroStore.Serialize(new List<Macro> { m })) { list[i] = m.Clone(); replaced++; }
            }
            return list;
        }

        /// <summary>Passwörter: gleicher Name und Benutzer = derselbe Eintrag, der zuletzt geänderte oder benutzte gewinnt.</summary>
        public static void MergeVault(List<VaultEntry> local, List<VaultEntry> imported, out int added, out int updated)
        {
            added = updated = 0;
            foreach (var e in imported)
            {
                int i = local.FindIndex(x => string.Equals(x.Name, e.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(x.User, e.User, StringComparison.OrdinalIgnoreCase));
                if (i < 0) { local.Add(e.Clone()); added++; continue; }
                var l = local[i];
                if (l.Password == e.Password && l.Match == e.Match) continue;
                if (e.Newest > l.Newest) { local[i] = e.Clone(); updated++; }
            }
        }
    }
}
