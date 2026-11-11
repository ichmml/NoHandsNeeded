using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Tastatur
{
    /// <summary>Erkennt E-Mail-Adressen.</summary>
    internal static class EmailRules
    {
        private static readonly Regex Pattern = new Regex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}$");

        /// <summary>Adresse aus einem getippten Stück holen (Satzzeichen am Ende weg), sonst null.</summary>
        public static string Extract(string chunk)
        {
            if (string.IsNullOrEmpty(chunk)) return null;
            string c = chunk.Trim().TrimEnd('.', ',', ';', ':', '!', '?', ')', '>', ']', '"', '\'').TrimStart('(', '<', '"', '\'');
            if (c.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) c = c.Substring(7);
            if (c.Length < 6 || c.Length > 100 || !Pattern.IsMatch(c)) return null;
            return c;
        }
    }

    /// <summary>Erkennt Eingabefelder für E-Mail oder Benutzername an Name, Beschriftung oder Platzhalter
    /// (so wie Windows sie über die Barrierefreiheit meldet), in allen 11 Sprachen.</summary>
    internal static class EmailFieldRules
    {
        // Wortteile, die auf ein E-Mail- oder Anmeldefeld hindeuten // i18n-ok
        private static readonly string[] Yes = {
            "e-mail", "email", "e-mailadres", "mail", "benutzername", "benutzer", "anmeldename", "anmelden", "login", "log-in", "konto", "empfänger", // i18n-ok
            "username", "user name", "user id", "userid", "sign in", "sign-in", "account", "recipient",
            "gebruikersnaam", "inloggen", "courriel", "identifiant", "nom d'utilisateur", "connexion", "compte",
            "brugernavn", "log ind", "nazwa użytkownika", "zaloguj", "użytkownik", "uživatelské jméno", "přihlásit", "přihlašovací", "účet", // i18n-ok
            "nome utente", "accedi", "correo", "usuario", "iniciar sesión", "cuenta", // i18n-ok
            "e-posta", "eposta", "kullanıcı adı", "giriş", "hesap", // i18n-ok
            "почта", "имя пользователя", "логин", "войти", "учётная запись", "учетная запись", "аккаунт" };
        // Felder, die zwar so klingen, aber etwas anderes wollen // i18n-ok
        private static readonly string[] No = {
            "such", "search", "zoek", "recherche", "søg", "szukaj", "hledat", "cerca", "buscar", "ara", "поиск", // i18n-ok
            "betreff", "subject", "onderwerp", "objet", "emne", "temat", "předmět", "oggetto", "asunto", "konu", "тема", // i18n-ok
            "nachricht", "message", "bericht", "besked", "wiadomość", "zpráva", "messaggio", "mensaje", "mesaj", "сообщение", // i18n-ok
            "passwort", "password", "kennwort", "wachtwoord", "mot de passe", "adgangskode", "hasło", "heslo", "contraseña", "parola", "пароль", // i18n-ok
            "nummer", "number", "iban", "bic", "telefon", "phone", "kommentar", "comment" };
        private static readonly string[] Strong = { "mail", "courriel", "correo", "e-posta", "eposta", "почта" }; // i18n-ok
        private static readonly string[] Password = { "passwort", "password", "kennwort", "wachtwoord", "mot de passe", "adgangskode", "hasło", "heslo", "contraseña", "parola", "пароль" }; // i18n-ok
        // ganze Feldnamen in Mailprogrammen (Empfänger)
        private static readonly string[] Exact = { "an", "to", "cc", "bcc", "aan", "à", "til", "do", "komu", "a", "para", "kime", "кому" }; // i18n-ok

        public static bool IsEmailField(params string[] texts)
        {
            bool yes = false;
            foreach (var raw in texts)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string t = raw.Trim().ToLowerInvariant().TrimEnd(':', '*', ' ');
                if (t.Length > 80) continue;   // lange Texte sind eher Inhalt als Beschriftung
                if (Password.Any(n => t.Contains(n))) return false;
                if (Strong.Any(m => t.Contains(m))) { yes = true; continue; }   // "E-Mail oder Telefonnummer" ist trotzdem ein E-Mail-Feld
                if (No.Any(n => t.Contains(n))) return false;
                if (Exact.Contains(t)) yes = true;
                else if (Yes.Any(y => t.Contains(y))) yes = true;
            }
            return yes;
        }
    }

    /// <summary>Gelernte E-Mail-Adressen (Datei Lernen\emails.txt). Vorgeschlagen werden sie beim Tippen,
    /// sobald das Getippte der Anfang einer Adresse ist, und im Tresor unter "✉ E-Mail".</summary>
    internal class EmailStore
    {
        private readonly Dictionary<string, LearnedWord> _items = new Dictionary<string, LearnedWord>();
        public bool Dirty;

        public int Count { get { return _items.Count; } }

        private static string Key(string s) { return (s ?? "").Trim().ToLowerInvariant(); }

        public void Learn(string email, int weight = 1)
        {
            string e = EmailRules.Extract(email);
            if (e == null) return;
            LearnedWord w;
            if (!_items.TryGetValue(Key(e), out w)) { w = new LearnedWord { Form = e }; _items[Key(e)] = w; }
            w.Count += weight;
            w.Form = e;
            Dirty = true;
        }

        public LearnedWord Get(string email)
        {
            LearnedWord w;
            return _items.TryGetValue(Key(email), out w) ? w : null;
        }

        /// <summary>Adressen, die so anfangen wie das Getippte (ab 3 Zeichen oder sobald ein @ dabei ist).</summary>
        public List<string> Suggest(string typed, int max, int minCount)
        {
            string k = Key(typed);
            if (k.Length < 3 && !k.Contains("@")) return new List<string>();
            return _items.Where(kv => kv.Value.Count >= minCount && kv.Key.StartsWith(k, StringComparison.Ordinal) && kv.Key != k)
                .OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(max).Select(kv => kv.Value.Form).ToList();
        }

        /// <summary>In einem E-Mail-Feld: alle passenden Adressen, auch ohne getippte Buchstaben.</summary>
        public List<string> SuggestAny(string typed, int max)
        {
            string k = Key(typed);
            return _items.Where(kv => kv.Key.StartsWith(k, StringComparison.Ordinal) && kv.Key != k)
                .OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(max).Select(kv => kv.Value.Form).ToList();
        }

        public List<LearnedWord> All()
        {
            return _items.Values.OrderByDescending(w => w.Count).ThenBy(w => w.Form, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public void Remove(string email) { if (_items.Remove(Key(email))) Dirty = true; }
        public void Clear() { if (_items.Count > 0) Dirty = true; _items.Clear(); }

        public int MergeFrom(EmailStore o)
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

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(L.T("Gelernte E-Mail-Adressen: Adresse<TAB>Anzahl")).Append('\n');
            foreach (var w in All()) sb.Append(w.Form).Append('\t').Append(w.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }

        public static EmailStore FromText(string text)
        {
            var st = new EmailStore();
            foreach (var raw in (text ?? "").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var p = line.Split('\t');
                int c;
                if (!int.TryParse(p.Length > 1 ? p[1] : "1", NumberStyles.Integer, CultureInfo.InvariantCulture, out c)) c = 1;
                st.Learn(p[0], Math.Max(1, c));
            }
            st.Dirty = false;
            return st;
        }

        public static EmailStore Load(string path)
        {
            return File.Exists(path) ? FromText(File.ReadAllText(path, Encoding.UTF8)) : new EmailStore();
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".neu";
            File.WriteAllText(tmp, ToText(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            Dirty = false;
        }
    }
}
