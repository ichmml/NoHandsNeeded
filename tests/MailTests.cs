using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tastatur
{
    internal static class MailTests
    {
        public static void Run(string dePath, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- E-Mail-Adressen ---");
            Check(EmailRules.Extract("ich.mml@gmail.com") == "ich.mml@gmail.com", "Adresse erkannt");
            Check(EmailRules.Extract("<info@mdm3d.de>,") == "info@mdm3d.de" && EmailRules.Extract("mailto:a.b@c.org") == "a.b@c.org", "Klammern, Komma, mailto");
            Check(EmailRules.Extract("ich@gmail") == null && EmailRules.Extract("s!add") == null && EmailRules.Extract("@foo.de") == null, "keine halben Adressen");

            var st = new EmailStore();
            st.Learn("Ich.MML@gmail.com"); st.Learn("ich.mml@gmail.com"); st.Learn("info@mdm3d.de");
            Check(st.Count == 2 && st.Get("ICH.mml@gmail.com").Count == 2, "Groß/Klein egal, gezählt");
            Check(st.Suggest("ich", 3, 1).SequenceEqual(new[] { "ich.mml@gmail.com" }) && st.Suggest("in", 3, 1).Count == 0 && st.Suggest("i", 3, 1).Count == 0, "Vorschlag ab 3 Zeichen");
            Check(st.Suggest("info@", 3, 1).Contains("info@mdm3d.de") && st.Suggest("info@mdm3d.de", 3, 1).Count == 0, "mit @, fertige Adresse nicht nochmal");
            var back = EmailStore.FromText(st.ToText());
            Check(back.Count == 2 && back.Get("info@mdm3d.de").Count == 1, "Datei speichern und laden");
            var other = new EmailStore(); other.Learn("neu@web.de");
            Check(back.MergeFrom(other) == 1 && back.Count == 3, "Import führt zusammen");

            // Engine: Adresse tippen, lernen, wieder vorschlagen
            string resDir = Path.GetDirectoryName(Path.GetFullPath(dePath));
            Func<string, Stream> res = rn => { string fp = Path.Combine(resDir, rn); return File.Exists(fp) ? File.OpenRead(fp) : null; };
            string tmp = Path.Combine(Path.GetTempPath(), "tastatur-mail-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var lm = new LanguageManager(tmp, res);
            var set = new LanguageSet();
            set.SetActive(new[] { lm.Get("de") }, null);
            set.SetStore("de", new LearningStore { Code = "de" });
            var o = new FakeOutput();
            var e = new KeyboardEngine(o, set, new Settings { AutoCapitalize = false });
            var learned = new List<string>();
            e.EmailFinished += m => { learned.Add(m); set.LearnEmail(m); };
            var keys = KeyLayouts.CreateGerman().SelectMany(x => x).ToList();
            var space = keys.First(k => k.Kind == KeyKind.Space);
            Action<string> type = s =>
            {
                foreach (char c in s)
                {
                    if (c == ' ') { e.Press(space); continue; }
                    string cs = c.ToString();
                    var k = keys.FirstOrDefault(x => x.Kind == KeyKind.Char && x.Normal == cs);
                    if (k == null) k = keys.FirstOrDefault(x => x.Kind == KeyKind.Char && x.Shifted == cs);
                    if (k != null && k.Normal != cs) e.Shift = ModState.Once;
                    if (k == null) { k = keys.First(x => x.Kind == KeyKind.Char && x.AltGr == cs); e.AltGr = ModState.Once; }
                    e.Press(k);
                }
            };
            type("ich.mml@gmail.com ");
            Check(learned.Count == 1 && learned[0] == "ich.mml@gmail.com", "fertig getippte Adresse wird gelernt: " + string.Join(",", learned));
            type("info@mdm3d.de");
            e.ResetWord(false);
            Check(learned.Count == 2 && learned[1] == "info@mdm3d.de", "auch beim Klick woanders (ohne Leerzeichen)");
            o.Doc.Length = 0;
            type("ich.m");
            var sug = set.SuggestEmails(e.CurrentChunk, 2);
            Check(sug.Contains("ich.mml@gmail.com"), "beim nächsten Mal vorgeschlagen");
            e.CompleteChunk("ich.mml@gmail.com");
            Check(o.Doc.ToString() == "ich.mml@gmail.com", "Klick ersetzt das Getippte durch die Adresse: " + o.Doc);
            e.PasswordMode = true;
            type("geheim@pw.de ");
            e.PasswordMode = false;
            Check(learned.Count == 2, "in Passwortfeldern wird nichts gelernt");
            set.EmailsEnabled = false;
            Check(set.SuggestEmails("ich", 2).Count == 0, "abschaltbar");
            var b = new Backup();
            b.Files[Backup.EmailsFile] = set.Emails.ToText();
            b.Files[Backup.LearnFile("de")] = "w\thallo\t2\n";
            Check(b.LearnCodes.SequenceEqual(new[] { "de" }), "emails.txt zählt im Export nicht als Sprache");
            Console.WriteLine("\n--- E-Mail-Felder erkennen ---");
            var yes = new[] { "E-Mail-Adresse", "Email", "E-Mail oder Telefon", "Benutzername", "Username", "Login", "Anmeldename", "Adresse e-mail", "Courriel",
                "Correo electrónico", "Nome utente", "Gebruikersnaam", "Brugernavn", "Nazwa użytkownika", "Uživatelské jméno", "E-posta", "Kullanıcı adı",
                "Электронная почта", "Логин", "An", "To", "Cc", "Empfänger", "Benutzername oder E-Mail" };
            foreach (var y in yes) Check(EmailFieldRules.IsEmailField(y), "E-Mail-Feld: " + y);
            var no = new[] { "Suche", "Search Google or type a URL", "Betreff", "Subject", "Nachricht", "Message", "Passwort", "Kennwort", "Telefonnummer",
                "Kontonummer", "IBAN", "Passwort oder PIN", "Mail-Passwort", "Vorname", "Kommentar", "Поиск", "Тема", "", null, "Postleitzahl" };
            foreach (var n in no) Check(!EmailFieldRules.IsEmailField(n), "kein E-Mail-Feld: " + (n ?? "(leer)"));
            Check(EmailFieldRules.IsEmailField("", "E-Mail", ""), "Beschreibung (Platzhalter) zählt auch");
            var es = new EmailStore(); es.Learn("ich.mml@gmail.com"); es.Learn("ich.mml@gmail.com"); es.Learn("info@mdm3d.de");
            Check(es.SuggestAny("", 4).SequenceEqual(new[] { "ich.mml@gmail.com", "info@mdm3d.de" }), "im E-Mail-Feld sofort alle Adressen, meistbenutzte vorne");
            Check(es.SuggestAny("in", 4).SequenceEqual(new[] { "info@mdm3d.de" }), "beim Tippen gefiltert");
            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
