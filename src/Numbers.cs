using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Tastatur
{
    /// <summary>
    /// Welche Zahlen dürfen gelernt werden? Datum, Telefonnummer, Postleitzahl und lange Nummern ab 9 Ziffern.
    /// PINs und Kartennummern nur, wenn AllowSecret an ist (Einstellung), dann verschlüsselt gespeichert.
    /// </summary>
    internal static class NumberRules
    {
        /// <summary>PINs (4 bis 8 Ziffern ohne Trennzeichen) und Kartennummern auch lernen.</summary>
        public static bool AllowSecret = true;

        /// <summary>Kartennummer (13 bis 19 Ziffern, Luhn-Prüfsumme stimmt).</summary>
        public static bool IsCard(string s)
        {
            string t = (s ?? "").Trim();
            string d = Digits(t);
            return d.Length >= 13 && d.Length <= 19 && LuhnValid(d) && !t.StartsWith("+") && !t.StartsWith("0") && !t.StartsWith("(") && Regex.IsMatch(t, @"^[\d \-]+$");
        }

        /// <summary>PIN oder Code: 4 bis 8 Ziffern ohne Trennzeichen.</summary>
        public static bool IsPin(string s)
        {
            string t = (s ?? "").Trim();
            return t.Length >= 4 && t.Length <= 8 && t.All(char.IsDigit);
        }

        /// <summary>Wird verschlüsselt gespeichert und nur mit Passwort exportiert.</summary>
        public static bool IsSecret(string s) { return IsCard(s) || IsPin(s); }
        private static readonly Regex DateRe = new Regex(@"^(\d{1,2})([./-])(\d{1,2})\2(\d{2}|\d{4})$", RegexOptions.Compiled);
        private static readonly Regex IsoDateRe = new Regex(@"^(\d{4})-(\d{2})-(\d{2})$", RegexOptions.Compiled);
        private static readonly Regex PhoneRe = new Regex(@"^(\+|0|\(0)[\d ()/\-]+$", RegexOptions.Compiled);
        private static readonly Regex LongRe = new Regex(@"^\+?[\d ()./\-]+$", RegexOptions.Compiled);
        private static readonly Regex PlzRe = new Regex(@"^\d{5}$", RegexOptions.Compiled);

        public static string Digits(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) if (char.IsDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Schlüssel zum Vergleichen: nur Ziffern, ein + am Anfang bleibt.</summary>
        public static string Key(string s)
        {
            string t = s.TrimStart();
            if (t.Length >= 2 && char.IsLetter(t[0]) && char.IsLetter(t[1])) return "IBAN:" + IbanRules.Compact(t);
            return (t.StartsWith("+") ? "+" : "") + Digits(t);
        }

        public static bool LuhnValid(string digits)
        {
            int sum = 0; bool dbl = false;
            for (int i = digits.Length - 1; i >= 0; i--)
            {
                int d = digits[i] - '0';
                if (dbl) { d *= 2; if (d > 9) d -= 9; }
                sum += d; dbl = !dbl;
            }
            return digits.Length > 0 && sum % 10 == 0;
        }

        /// <summary>
        /// Gibt die zu lernende Schreibweise zurück oder null.
        /// followedByWord: direkt danach kam ein Leerzeichen und ein Wort (für Postleitzahlen).
        /// </summary>
        public static string Classify(string raw, bool followedByWord)
        {
            if (raw == null) return null;
            string s = raw.Trim().TrimEnd('.', '/', '-', ' ');
            if (s.Length == 0) return null;
            string digits = Digits(s);
            if (digits.Length == 0) return null;

            // Kartennummern nur mit AllowSecret
            if (digits.Length >= 13 && digits.Length <= 19 && LuhnValid(digits) && !s.StartsWith("+") && !s.StartsWith("0") && !s.StartsWith("("))
                return AllowSecret && IsCard(s) ? Regex.Replace(s, @"\s+", " ") : null;

            var m = DateRe.Match(s);
            if (m.Success)
            {
                int d = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[3].Value);
                return d >= 1 && d <= 31 && mo >= 1 && mo <= 12 ? s : null;
            }
            m = IsoDateRe.Match(s);
            if (m.Success)
            {
                int mo = int.Parse(m.Groups[2].Value), d = int.Parse(m.Groups[3].Value);
                return d >= 1 && d <= 31 && mo >= 1 && mo <= 12 ? s : null;
            }

            if (PlzRe.IsMatch(s) && followedByWord) return s;

            // reine Ziffern ohne Trennzeichen mit 4 bis 8 Stellen: PIN, TAN, Code
            if (s == digits && digits.Length >= 4 && digits.Length <= 8) return AllowSecret ? s : null;
            if (PlzRe.IsMatch(s)) return null;

            if (PhoneRe.IsMatch(s) && digits.Length >= 6 && digits.Length <= 15 && s.Count(c => c == '(') == s.Count(c => c == ')'))
                return Regex.Replace(s, @"\s+", " ");

            // Lange Nummern ab 9 Ziffern (Kunden-, Vertrags-, Versicherungsnummer, Handynummer ohne 0)
            if (digits.Length >= 9 && digits.Length <= 20 && LongRe.IsMatch(s))
                return Regex.Replace(s, @"\s+", " ");

            return null;
        }
    }

    /// <summary>IBAN erkennen, prüfen (Prüfziffer nach ISO 13616) und einheitlich schreiben.</summary>
    internal static class IbanRules
    {
        private static readonly Regex IbanRe = new Regex(@"^[A-Z]{2}\d{2}[A-Z0-9]{11,30}$", RegexOptions.Compiled);

        /// <summary>Leerzeichen raus, Großbuchstaben.</summary>
        public static string Compact(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
            return sb.ToString();
        }

        public static bool IsValid(string s)
        {
            string c = Compact(s);
            if (!IbanRe.IsMatch(c)) return false;
            string r = c.Substring(4) + c.Substring(0, 4);
            int mod = 0;
            foreach (char ch in r)
            {
                int v = char.IsDigit(ch) ? ch - '0' : ch - 'A' + 10;
                mod = v >= 10 ? (mod * 100 + v) % 97 : (mod * 10 + v) % 97;
            }
            return mod == 1;
        }

        /// <summary>Gültige IBAN in Vierergruppen ("DE89 3704 0044 0532 0130 00"), sonst null.</summary>
        public static string Format(string s)
        {
            if (!IsValid(s)) return null;
            string c = Compact(s);
            var sb = new StringBuilder();
            for (int i = 0; i < c.Length; i++) { if (i > 0 && i % 4 == 0) sb.Append(' '); sb.Append(c[i]); }
            return sb.ToString();
        }

        /// <summary>Sieht der Anfang nach einer IBAN aus (zwei Buchstaben, dann Ziffern)?</summary>
        public static bool LooksLike(string s)
        {
            string c = Compact(s);
            return c.Length >= 2 && char.IsLetter(c[0]) && char.IsLetter(c[1]) && (c.Length == 2 || char.IsDigit(c[2]));
        }
    }

    /// <summary>Gelernte Zahlen, für alle Sprachen gemeinsam (Datei Lernen\zahlen.txt).</summary>
    internal class NumberStore
    {
        private readonly Dictionary<string, LearnedWord> _items = new Dictionary<string, LearnedWord>();
        public bool Dirty;

        public int Count { get { return _items.Count; } }

        public void Learn(string form, int weight = 1)
        {
            string k = NumberRules.Key(form);
            if (k.Length == 0) return;
            LearnedWord w;
            if (!_items.TryGetValue(k, out w)) { w = new LearnedWord { Form = form }; _items[k] = w; }
            w.Count += weight;
            w.Form = form;   // zuletzt benutzte Schreibweise
            Dirty = true;
        }

        public void Unlearn(string form)
        {
            string k = NumberRules.Key(form);
            LearnedWord w;
            if (!_items.TryGetValue(k, out w)) return;
            if (--w.Count <= 0) _items.Remove(k);
            Dirty = true;
        }

        public LearnedWord Get(string form)
        {
            LearnedWord w;
            return _items.TryGetValue(NumberRules.Key(form), out w) ? w : null;
        }

        /// <summary>Zahlen, deren Ziffern so anfangen wie das Getippte. Trennzeichen zählen nicht.</summary>
        public List<string> Suggest(string typed, int max, int minCount)
        {
            string k = NumberRules.Key(typed);
            if (NumberRules.Digits(k).Length == 0) return new List<string>();
            return _items.Where(kv => kv.Value.Count >= minCount && kv.Key.StartsWith(k, StringComparison.Ordinal) && kv.Value.Form != typed.Trim()
                    && (NumberRules.AllowSecret || !NumberRules.IsSecret(kv.Value.Form)))
                .OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(max).Select(kv => kv.Value.Form).ToList();
        }

        /// <summary>Gelernte IBANs, die mit dem Getippten anfangen (Leerzeichen und Groß/Klein egal).</summary>
        public List<string> SuggestIbans(string typed, int max)
        {
            string k = "IBAN:" + IbanRules.Compact(typed);
            return _items.Where(kv => kv.Key.StartsWith(k, StringComparison.Ordinal) && kv.Key != k)
                .OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(max).Select(kv => kv.Value.Form).ToList();
        }

        public List<LearnedWord> All()
        {
            return _items.Values.OrderByDescending(w => w.Count).ThenBy(w => w.Form, StringComparer.Ordinal).ToList();
        }

        public void Remove(string form) { if (_items.Remove(NumberRules.Key(form))) Dirty = true; }
        public void Clear() { if (_items.Count > 0) Dirty = true; _items.Clear(); }

        /// <summary>Alle Zahlen als Text (für Sicherung mit Passwort und Tests).</summary>
        public string ToText() { return ToText(true, true); }

        /// <summary>plain: normale Zahlen, secret: PINs und Kartennummern.</summary>
        public string ToText(bool plain, bool secret)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(L.T("Gelernte Zahlen (Datum, Telefonnummern, Postleitzahlen): Schreibweise<TAB>Anzahl")).Append("\n");
            foreach (var w in All())
            {
                bool s = NumberRules.IsSecret(w.Form);
                if (s ? secret : plain) sb.Append(w.Form).Append('\t').Append(w.Count).Append('\n');
            }
            return sb.ToString();
        }

        public int SecretCount { get { return _items.Values.Count(w => NumberRules.IsSecret(w.Form)); } }

        /// <summary>Datei für PINs und Kartennummern neben zahlen.txt, mit Windows (DPAPI) verschlüsselt.</summary>
        public static string SecretPath(string path) { return Path.Combine(Path.GetDirectoryName(path), "zahlen_geheim.dat"); }

        private static readonly byte[] SecretEntropy = Encoding.UTF8.GetBytes("NoHandsNeeded-Zahlen-v1");

        /// <summary>Zahlen vom anderen PC dazunehmen, der größere Zähler gewinnt. Liefert die Zahl neuer Einträge.</summary>
        public int MergeFrom(NumberStore o)
        {
            int added = 0;
            foreach (var kv in o._items)
            {
                LearnedWord w;
                if (!_items.TryGetValue(kv.Key, out w)) { _items[kv.Key] = new LearnedWord { Form = kv.Value.Form, Count = kv.Value.Count }; added++; }
                else if (kv.Value.Count > w.Count) { w.Count = kv.Value.Count; w.Form = kv.Value.Form; }
            }
            if (o._items.Count > 0) Dirty = true;
            return added;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".neu";
            File.WriteAllText(tmp, ToText(true, false), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            // PINs und Kartennummern nie im Klartext auf die Platte
            string sp = SecretPath(path);
            string secret = ToText(false, true);
            if (SecretCount == 0) { if (File.Exists(sp)) File.Delete(sp); }
            else
            {
                byte[] enc = System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), SecretEntropy,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                string stmp = sp + ".neu";
                File.WriteAllText(stmp, Convert.ToBase64String(enc), Encoding.ASCII);
                if (File.Exists(sp)) File.Replace(stmp, sp, null); else File.Move(stmp, sp);
            }
            Dirty = false;
        }

        public static NumberStore Load(string path)
        {
            var st = File.Exists(path) ? FromText(File.ReadAllText(path, Encoding.UTF8)) : new NumberStore();
            string sp = SecretPath(path);
            if (File.Exists(sp))
            {
                try
                {
                    byte[] plain = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(File.ReadAllText(sp).Trim()), SecretEntropy,
                        System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    st.MergeFrom(FromText(Encoding.UTF8.GetString(plain)));
                    st.Dirty = false;
                }
                catch (Exception ex) { Log.Write("zahlen_geheim.dat nicht lesbar: " + ex.Message); }
            }
            return st;
        }

        public static NumberStore FromText(string text)
        {
            var st = new NumberStore();
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var p = line.Split('\t');
                int n;
                if (p.Length < 2 || !int.TryParse(p[1], out n) || n <= 0) continue;
                st._items[NumberRules.Key(p[0])] = new LearnedWord { Form = p[0], Count = n };
            }
            st.Dirty = false;
            return st;
        }
    }
}
