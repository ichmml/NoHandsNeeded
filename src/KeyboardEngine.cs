using System;
using System.Collections.Generic;
using System.Text;

namespace Tastatur
{
    internal enum ModState { Off, Once, Locked }

    internal interface IOutput
    {
        void SendText(string text);
        void SendVk(ushort vk, Modifiers mods);
        bool SendCharWithModifiers(char c, Modifiers mods);
        void SendBackspaces(int n);
    }

    internal class SystemOutput : IOutput
    {
        public void SendText(string text) { InputSender.SendText(text); }
        public void SendVk(ushort vk, Modifiers mods) { InputSender.SendVk(vk, mods); }
        public bool SendCharWithModifiers(char c, Modifiers mods) { return InputSender.SendCharWithModifiers(c, mods); }
        public void SendBackspaces(int n) { InputSender.SendBackspaces(n); }
    }

    /// <summary>Was die Tastatur über den Text in einem Fenster weiß (für "Wort pro Programm merken").</summary>
    internal class EngineState
    {
        public string Word, LastWord, Trailing, PrevWord, PrevBefore, Num, PendingPlz, LastNum, History;
        public bool SentenceStart, WordAtSentence, LastWordAtSentence, AutoSpaced, NumPhone, LastNumWord;
        public char LastChar;
        public int LastNumTrail;

        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(Word) && LastWord == null && string.IsNullOrEmpty(Num) && LastNum == null && PrevWord == null && string.IsNullOrEmpty(History); }
        }
    }

    /// <summary>
    /// Die komplette Tastaturlogik ohne Oberfläche: Einrast-Sondertasten, Großschreibung,
    /// aktuelles Wort, letztes Wort (für die Aa-Taste) und Vorschläge. Dadurch auch ohne Windows testbar.
    /// </summary>
    internal class KeyboardEngine
    {
        private const int MaxTrailing = 3;      // so viele Zeichen nach einem Wort kann Aa noch überspringen
        private const int MaxHistory = 2000;

        private readonly IOutput _out;
        private readonly ISuggester _predictor;
        private readonly Settings _settings;

        public ModState Shift, Ctrl, Alt, Win, AltGr;
        public bool Caps;
        public bool FnLayer;

        private readonly StringBuilder _word = new StringBuilder();
        private bool _sentenceStart;        // nächstes Wort beginnt einen Satz
        private bool _wordAtSentence;       // aktuelles Wort hat am Satzanfang begonnen
        private char _lastChar = '\0';

        // zuletzt fertig geschriebenes Wort und was danach kam (Leerzeichen, Satzzeichen)
        private string _lastWord;
        private bool _lastWordAtSentence;
        private readonly StringBuilder _trailing = new StringBuilder();

        // Leerzeichen kam automatisch nach einem Vorschlag: vor . , ! ? : ; wird es wieder entfernt
        private bool _autoSpaced;
        private char _punctSpaced;   // nach diesem Satzzeichen kam automatisch ein Leerzeichen
        /// <summary>Kein automatisches Leerzeichen nach Satzzeichen (E-Mail- oder IBAN-Feld).</summary>
        public bool NoPunctuationSpace;

        /// <summary>Soll nach diesem Satzzeichen von selbst ein Leerzeichen kommen? chunk = Getipptes davor seit dem letzten Leerzeichen.</summary>
        private bool WantsPunctuationSpace(char p, string chunk)
        {
            if (!_settings.AutoSpaceAfterPunctuation || _passwordMode || NoPunctuationSpace) return false;
            if (chunk.Length < 2) return false;   // "s!add", "z.B." bleiben, wie getippt
            char last = chunk[chunk.Length - 1];
            if (!(char.IsLetter(last) || last == ')' || last == '"' || last == '\'')) return false;   // "3.5", "10:30", "..." nicht
            string low = chunk.ToLowerInvariant();
            if (low.IndexOfAny(new[] { '@', '/', '\\', '.', ':', '_', '=' }) >= 0) return false;   // Adressen, Pfade, Dateinamen
            if (low.StartsWith("www") || low == "http" || low == "https" || low == "ftp" || low == "mailto") return false;
            if (p == '.')
            {
                // beim Punkt nur nach einem bekannten Wort, sonst ist es oft eine Webadresse ("udaption.com")
                string w = chunk.TrimEnd(')', '"', '\'');
                var ls = _predictor as LanguageSet;
                if (ls != null && !ls.Knows(w) && !ls.Knows(w.ToLowerInvariant())) return false;
            }
            return true;
        }
        private const string SnugPunctuation = ".,!?:;";

        // Lernen: Wort davor (für Wortfolgen) und das davor (zum Rückgängigmachen)
        private string _prevWord, _prevBefore;

        /// <summary>Ein Wort ist fertig: Wort, Wort davor (oder null), begann am Satzanfang.</summary>
        public event Action<string, string, bool> WordFinished;
        /// <summary>Ein fertiges Wort wurde per Rücktaste wieder geöffnet: Lernen zurücknehmen.</summary>
        public event Action<string, string> WordReopened;

        // Passwortfeld: nichts lernen, nichts vorschlagen, Getipptes nur für "Im Tresor speichern?" merken
        private bool _passwordMode;
        private readonly StringBuilder _secret = new StringBuilder();
        /// <summary>Für das Eingabefeld: so viele Zeichen wurden im Passwortfeld getippt.</summary>
        public int SecretLength { get { return _secretValid ? _secret.Length : 0; } }
        /// <summary>Klartext nur für das Eingabefeld (👁). Nie speichern, nie protokollieren.</summary>
        public string SecretText { get { return _secretValid ? _secret.ToString() : ""; } }

        /// <summary>Getipptes Wort (oder Zahl) aus dem Eingabefeld übernehmen: mit Leerzeichen abschließen.</summary>
        public string AcceptTyped()
        {
            if (_passwordMode) return null;
            string chunk = CurrentChunk;
            bool numeric = _num.Length > 0 && (_word.Length == 0 || AllDigits(_word));
            if (chunk.Length > 0 && chunk != _word.ToString() && !(numeric && chunk == _num.ToString()))
            {
                // mit Sonderzeichen (z. B. "s!add"): nur mit Leerzeichen abschließen, nichts extra lernen
                _deadPending = '\0';
                _out.SendText(" ");
                TrackText(" ");
                Fire();
                return chunk;
            }
            if (numeric)
            {
                string n = _num.ToString().TrimEnd();
                _deadPending = '\0';
                _out.SendText(" ");
                TrackText(" ");
                Fire();
                return n;
            }
            if (_word.Length == 0) return null;
            string w = _word.ToString();
            PressSuggestion(w);
            return w;
        }
        private bool _secretValid;
        /// <summary>Enter in einem Passwortfeld.</summary>
        public event Action SecretSubmitted;

        // Zahlen (Datum, Telefonnummer, Postleitzahl): laufen neben dem Wortpuffer mit
        private readonly StringBuilder _num = new StringBuilder();
        private bool _numPhone;             // beginnt mit 0, + oder (: Leerzeichen gehören dazu
        private string _pendingPlz;         // 5 Ziffern + Leerzeichen: erst beim nächsten Zeichen klar, ob ein Ort folgt
        private string _lastNum;            // zuletzt fertige Zahl (für Rücktaste)
        private bool _lastNumWord;
        private int _lastNumTrail;
        private List<string> _numSuggestions = new List<string>();
        /// <summary>Eine Zahl ist fertig: Text, danach folgte ein Wort.</summary>
        public event Action<string, bool> NumberFinished;
        /// <summary>Eine E-Mail-Adresse wurde fertig getippt (Leerzeichen, Enter, Tab oder Klick woanders).</summary>
        public event Action<string> EmailFinished;
        /// <summary>Eine gültige IBAN wurde fertig getippt (in Vierergruppen geschrieben).</summary>
        public event Action<string> IbanFinished;

        private static readonly System.Text.RegularExpressions.Regex IbanTailRe = new System.Text.RegularExpressions.Regex(
            @"(?<![A-Za-z0-9])([A-Za-z]{2}\d{2}(?: ?[A-Za-z0-9]){11,32})$");
        private static readonly System.Text.RegularExpressions.Regex IbanTypingRe = new System.Text.RegularExpressions.Regex(
            @"(?<![A-Za-z0-9])([A-Za-z]{2}(?:\d(?: ?[A-Za-z0-9]){0,33})?)$");

        /// <summary>Steht am Ende des Getippten eine gültige IBAN? Dann melden. Liefert true, wenn ja.</summary>
        private bool CheckIban(char ender = '\0')
        {
            if (_passwordMode) return false;
            string h = _history.ToString();
            if (ender != '\0' && h.Length > 0 && h[h.Length - 1] == ender) h = h.Substring(0, h.Length - 1);   // Zeichen, das die Zahl beendet hat
            int end = h.Length;
            while (end > 0 && !char.IsLetterOrDigit(h[end - 1])) end--;
            h = h.Substring(0, end);
            var m = IbanTailRe.Match(h);
            if (!m.Success) return false;
            string f = IbanRules.Format(m.Groups[1].Value);
            if (f == null) return false;
            var ev = IbanFinished; if (ev != null) ev(f);
            return true;
        }

        private static readonly System.Text.RegularExpressions.Regex IbanLabelRe = new System.Text.RegularExpressions.Regex(
            @"\bIBAN\s*:?\s+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        /// <summary>Direkt davor steht "IBAN" oder "IBAN:" mit Leerzeichen.</summary>
        public bool AfterIbanLabel { get { return !_passwordMode && IbanLabelRe.IsMatch(_history.ToString()); } }

        /// <summary>Der gerade getippte Anfang einer IBAN, auch über Leerzeichen hinweg ("DE89 37"), sonst "".</summary>
        public string CurrentIbanPart
        {
            get
            {
                if (_passwordMode) return "";
                var m = IbanTypingRe.Match(_history.ToString());
                return m.Success ? m.Groups[1].Value : "";
            }
        }

        /// <summary>Vorgeschlagene IBAN übernehmen: der getippte IBAN-Anfang wird ersetzt.</summary>
        public void CompleteIban(string iban)
        {
            if (_passwordMode || string.IsNullOrEmpty(iban)) return;
            string part = CurrentIbanPart;
            _deadPending = '\0';
            if (part.Length > 0) _out.SendBackspaces(part.Length);
            _out.SendText(iban);
            if (_history.Length >= part.Length) _history.Length -= part.Length;
            _history.Append(iban);
            _word.Clear(); _num.Clear(); _numIban = false; _lastNum = null;
            ForgetLastWord();
            var ev = IbanFinished; if (ev != null) ev(iban);   // zählt mit
            Fire();
        }

        private void CheckEmailChunk()
        {
            if (_passwordMode) return;
            string e = EmailRules.Extract(CurrentChunk);
            if (e == null) return;
            var h = EmailFinished; if (h != null) h(e);
        }

        /// <summary>Vorgeschlagene E-Mail-Adresse übernehmen: das Getippte seit dem letzten Leerzeichen wird ersetzt.</summary>
        public void CompleteChunk(string full)
        {
            if (_passwordMode || string.IsNullOrEmpty(full)) return;
            string chunk = CurrentChunk;
            _deadPending = '\0';
            if (chunk.Length > 0) _out.SendBackspaces(chunk.Length);
            _out.SendText(full);
            if (_history.Length >= chunk.Length) _history.Length -= chunk.Length;
            _history.Append(full);
            _word.Clear(); _num.Clear();
            ForgetLastWord();
            Fire();
        }
        /// <summary>Eine fertige Zahl wurde per Rücktaste wieder geöffnet.</summary>
        public event Action<string, bool> NumberReopened;

        // zuletzt Getipptes für "Als Makro speichern"
        private readonly StringBuilder _history = new StringBuilder();

        public event Action Changed;

        /// <summary>Wird ausgelöst, wenn die Aa-Taste eine Schreibweise gewählt hat (für das Lernen ab Stufe 2).</summary>
        public event Action<string, string> CaseChanged;

        public KeyboardEngine(IOutput output, ISuggester predictor, Settings settings)
        {
            _out = output; _predictor = predictor; _settings = settings;
        }

        public string CurrentWord { get { return _word.ToString(); } }

        // ---------- Tottasten (´ + e = é) ----------
        /// <summary>Tottasten des aktuellen Layouts. Leer = keine.</summary>
        public Dictionary<char, char> DeadKeys = new Dictionary<char, char>();
        /// <summary>Für Großbuchstaben (Türkisch: i → İ).</summary>
        public System.Globalization.CultureInfo LayoutCulture = System.Globalization.CultureInfo.InvariantCulture;
        private char _deadPending;
        public char DeadPending { get { return _deadPending; } }
        public string CurrentNumber { get { return _num.ToString(); } }

        /// <summary>Alles seit dem letzten Leerzeichen, Enter oder Tab, samt Sonderzeichen (für die Anzeige oben links).</summary>
        public string CurrentChunk
        {
            get
            {
                string h = _history.ToString();
                int i = h.LastIndexOfAny(new[] { ' ', '\n', '\t' });
                return i < 0 ? h : h.Substring(i + 1);
            }
        }
        public string PreviousWord { get { return _prevWord; } }
        public string LastWord { get { return _lastWord; } }
        public bool SentenceStart { get { return _sentenceStart; } }
        public string LastTyped { get { return _history.ToString(); } }

        private void Fire() { var h = Changed; if (h != null) h(); }

        // ---------- Zustand ----------

        public ModState GetMod(Modifiers m)
        {
            switch (m)
            {
                case Modifiers.Shift: return Shift;
                case Modifiers.Ctrl: return Ctrl;
                case Modifiers.Alt: return Alt;
                case Modifiers.Win: return Win;
            }
            return ModState.Off;
        }

        private static ModState Cycle(ModState s)
        {
            // 1x: für die nächste Taste, 2x: wieder aus. Feststellen gibt es nicht (auf Wunsch weg, rastete zu leicht ein).
            return s == ModState.Off ? ModState.Once : ModState.Off;
        }

        private Modifiers ActiveMods()
        {
            var m = Modifiers.None;
            if (Shift != ModState.Off) m |= Modifiers.Shift;
            if (Ctrl != ModState.Off) m |= Modifiers.Ctrl;
            if (Alt != ModState.Off) m |= Modifiers.Alt;
            if (Win != ModState.Off) m |= Modifiers.Win;
            return m;
        }

        private bool HasShortcutMods
        {
            get { return Ctrl != ModState.Off || Alt != ModState.Off || Win != ModState.Off; }
        }

        /// <summary>Einmal-Umschalter (Shift 1x usw.) zurücksetzen, z. B. nach einem abgefangenen Buchstaben.</summary>
        public void ClearOneShotMods() { ReleaseOnce(); Fire(); }

        private void ReleaseOnce()
        {
            if (Shift == ModState.Once) Shift = ModState.Off;
            if (Ctrl == ModState.Once) Ctrl = ModState.Off;
            if (Alt == ModState.Once) Alt = ModState.Off;
            if (Win == ModState.Once) Win = ModState.Off;
            if (AltGr == ModState.Once) AltGr = ModState.Off;
        }

        /// <summary>Automatische Großschreibung des nächsten Buchstabens am Satzanfang.</summary>
        public bool AutoShiftActive
        {
            get { return _settings.AutoCapitalize && _sentenceStart && _word.Length == 0 && Shift == ModState.Off && !Caps; }
        }

        /// <summary>Welches Zeichen eine Zeichentaste gerade schreiben würde.</summary>
        public string OutputFor(KeyDef k)
        {
            bool shift = Shift != ModState.Off;
            if (AltGr != ModState.Off)
            {
                string a = k.AltGr ?? "";
                // AltGr-Buchstaben (z. B. polnisch ą) mit Shift oder Feststelltaste groß
                if (a.Length == 1 && char.IsLetter(a[0]) && (shift ^ Caps)) return a.ToUpper(LayoutCulture);
                return a;
            }
            bool lowerLetter = k.Normal.Length == 1 && char.IsLetter(k.Normal[0]) && char.IsLower(k.Normal[0]);
            if (lowerLetter)
            {
                bool caseKey = k.Shifted.Length == 1 && char.IsUpper(k.Shifted[0]);   // Shift macht den Großbuchstaben
                if (caseKey)
                {
                    bool upper = shift ^ Caps;
                    if (AutoShiftActive) upper = true;
                    return upper ? k.Shifted : k.Normal;
                }
                // z. B. tschechisch ě/2, französisch é/2: Shift = Zweitzeichen, Feststelltaste/Satzanfang = groß
                if (shift) return k.Shifted;
                return Caps || AutoShiftActive ? k.Normal.ToUpper(LayoutCulture) : k.Normal;
            }
            return shift ? k.Shifted : k.Normal;
        }

        // ---------- Wort-Puffer ----------

        /// <summary>Cursor ist woanders (Klick, Fensterwechsel): nichts mehr über den Text davor bekannt.</summary>
        public void ResetWord(bool sentenceStart)
        {
            if (_word.Length > 0 && !_passwordMode) FinishWord();
            if (_passwordMode) DropNumber(); else CloseNumber();
            _prevWord = _prevBefore = null;
            _word.Clear();
            _sentenceStart = sentenceStart;
            _lastChar = '\0';
            ForgetLastWord();
            CheckEmailChunk();
            _history.Clear();
            Fire();
        }

        private void ForgetLastWord()
        {
            _lastWord = null;
            _trailing.Clear();
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '-' || c == '\'';
        }

        // ---------- Zahlen ----------

        private static bool IsDigit(char c) { return c >= '0' && c <= '9'; }

        private static bool AllDigits(StringBuilder sb)
        {
            for (int i = 0; i < sb.Length; i++) if (!IsDigit(sb[i])) return false;
            return true;
        }

        private bool _numIban;   // Zahl steht hinter "DE12" o. ä.: Teil einer IBAN, nie lernen

        /// <summary>Steht direkt vor der neuen Zahl der Anfang einer IBAN (zwei Buchstaben, zwei Ziffern)?</summary>
        private bool AfterIbanStart()
        {
            string h = _history.ToString();
            int end = h.Length - 1;                       // letztes Zeichen ist die neue Ziffer
            while (end > 0 && h[end - 1] == ' ') end--;
            if (end == h.Length - 1 || end < 4) return false;   // kein Leerzeichen dazwischen
            string t = h.Substring(end - 4, 4);
            bool before = end - 5 < 0 || !char.IsLetterOrDigit(h[end - 5]);
            return before && char.IsLetter(t[0]) && char.IsLetter(t[1]) && char.IsDigit(t[2]) && char.IsDigit(t[3]);
        }

        private void RaiseNumber(string n, bool followedByWord)
        {
            if (_numIban) return;
            _lastNumWord = followedByWord;
            var h = NumberFinished;
            if (h != null && !_passwordMode) h(n, followedByWord);
        }

        /// <summary>Zahl abschließen, c ist das Zeichen, das sie beendet hat.</summary>
        private void FinishNumber(char c)
        {
            string raw = _num.ToString();
            string cand = raw.TrimEnd('.', '/', '-', ' ', '(');
            _num.Clear();
            if (NumberRules.Digits(cand).Length == 0) { _lastNum = null; return; }
            if (CheckIban(c)) { _numIban = false; _lastNum = null; return; }   // ganze IBAN fertig, die Ziffern nicht einzeln lernen
            // Leerzeichen gehören mit zur Zahl ("512 345 678"). Erst am Ende wird entschieden,
            // ob das Ganze eine Nummer ist oder ob es mehrere einzelne Zahlen waren ("10.09.1991 15 Leute").
            bool beforeWord = raw.EndsWith(" ") && char.IsLetter(c) && cand.Length == 5 && AllDigits(new StringBuilder(cand));   // Postleitzahl vor einem Ort
            if (c == ' ' && cand.Length == raw.Length && cand.Length == 5 && AllDigits(new StringBuilder(cand)))
                _pendingPlz = cand;
            else if (NumberRules.Classify(cand, beforeWord) != null || cand.IndexOf(' ') < 0)
                RaiseNumber(cand, beforeWord);
            else
            {
                string[] parts = cand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i].TrimEnd('.', '/', '-', '(');
                    if (NumberRules.Digits(part).Length > 0) RaiseNumber(part, i == parts.Length - 1 && beforeWord);
                }
            }
            _lastNum = cand;
            _lastNumTrail = raw.Length - cand.Length + 1;
        }

        /// <summary>Zahlenpuffer ohne Lernen leeren (Tastenkürzel, Passwortfeld).</summary>
        private void DropNumber()
        {
            _num.Clear(); _pendingPlz = null; _lastNum = null;
        }

        /// <summary>Cursor ist woanders: angefangene Zahl lernen, aber nicht mehr zurücknehmbar.</summary>
        private void CloseNumber()
        {
            if (_pendingPlz != null) { _pendingPlz = null; }
            else if (_num.Length > 0) FinishNumber('\0');
            _num.Clear();
            _lastNum = null;
        }

        private int NumIndexOf(char ch) { for (int i = 0; i < _num.Length; i++) if (_num[i] == ch) return i; return -1; }

        private void TrackNumber(char c)
        {
            if (_pendingPlz != null)
            {
                string p = _pendingPlz;
                _pendingPlz = null;
                RaiseNumber(p, char.IsLetter(c));
            }
            if (_num.Length == 0)
            {
                if (_lastNum != null && ++_lastNumTrail > MaxTrailing) _lastNum = null;
                // neue Zahl nur, wenn sie nicht mitten in einem Wort steht (z. B. "DE12" einer IBAN)
                bool wordFree = _word.Length == 0 || AllDigits(_word);
                if (wordFree && (IsDigit(c) || c == '+' || c == '('))
                {
                    _num.Append(c);
                    _numPhone = c == '+' || c == '(' || c == '0';
                    _numIban = AfterIbanStart();
                    _lastNum = null;
                }
                return;
            }
            char last = _num[_num.Length - 1];
            bool ok;
            if (IsDigit(c)) ok = true;
            else if (c == '.' || c == '/' || c == '-') ok = IsDigit(last) || last == ')';
            else if (c == ')') ok = IsDigit(last);
            else if (c == '(') ok = last == ' ' || IsDigit(last) || last == '+';
            else if (c == ' ') ok = (IsDigit(last) || last == ')') && NumIndexOf('.') < 0;   // Nummern mit Leerzeichen, auch ohne 0 oder + vorne; Datum endet am Leerzeichen
            else ok = false;
            if (ok) _num.Append(c);
            else FinishNumber(c);
        }

        private void BackspaceNumber()
        {
            if (_num.Length > 0) { _num.Length--; return; }
            if (_lastNum == null) return;
            if (--_lastNumTrail > 0) return;
            if (_pendingPlz != null) _pendingPlz = null;   // noch nicht gelernt
            else
            {
                var h = NumberReopened;
                if (h != null && !_passwordMode) h(_lastNum, _lastNumWord);
            }
            _num.Append(_lastNum);
            _numPhone = _lastNum.Length > 0 && (_lastNum[0] == '+' || _lastNum[0] == '(' || _lastNum[0] == '0');
            _lastNum = null;
        }

        private void FinishWord()
        {
            string w = _word.ToString();
            if (w.Length >= 15 && char.IsLetter(w[0]) && char.IsLetter(w[1]) && char.IsDigit(w[2])) CheckIban();   // IBAN ohne Leerzeichen
            var h = WordFinished;
            if (h != null && !_passwordMode) h(w, _prevWord, _wordAtSentence);
            _prevBefore = _prevWord;
            _prevWord = w;
        }

        /// <summary>Zustand für das aktuelle Fenster abgeben (null im Passwortfeld).</summary>
        public EngineState SaveState()
        {
            if (_passwordMode) return null;
            return new EngineState
            {
                Word = _word.ToString(), SentenceStart = _sentenceStart, WordAtSentence = _wordAtSentence, LastChar = _lastChar,
                LastWord = _lastWord, LastWordAtSentence = _lastWordAtSentence, Trailing = _trailing.ToString(), AutoSpaced = _autoSpaced,
                PrevWord = _prevWord, PrevBefore = _prevBefore,
                Num = _num.ToString(), NumPhone = _numPhone, PendingPlz = _pendingPlz, LastNum = _lastNum, LastNumWord = _lastNumWord, LastNumTrail = _lastNumTrail,
                History = _history.ToString()
            };
        }

        /// <summary>Zustand eines Fensters laden. null = neues Fenster, nichts bekannt (ohne Lernen, das alte Fenster ist gespeichert).</summary>
        public void RestoreState(EngineState st, bool sentenceStartIfEmpty = false)
        {
            if (_passwordMode) st = null;
            _word.Clear(); _trailing.Clear(); _num.Clear(); _history.Clear();
            if (st == null)
            {
                _sentenceStart = sentenceStartIfEmpty; _wordAtSentence = false; _lastChar = '\0';
                _lastWord = null; _lastWordAtSentence = false; _autoSpaced = false;
                _prevWord = _prevBefore = null;
                _numPhone = false; _pendingPlz = null; _lastNum = null; _lastNumWord = false; _lastNumTrail = 0;
            }
            else
            {
                _word.Append(st.Word ?? ""); _sentenceStart = st.SentenceStart; _wordAtSentence = st.WordAtSentence; _lastChar = st.LastChar;
                _lastWord = st.LastWord; _lastWordAtSentence = st.LastWordAtSentence; _trailing.Append(st.Trailing ?? ""); _autoSpaced = st.AutoSpaced;
                _prevWord = st.PrevWord; _prevBefore = st.PrevBefore;
                _num.Append(st.Num ?? ""); _numPhone = st.NumPhone; _pendingPlz = st.PendingPlz; _lastNum = st.LastNum; _lastNumWord = st.LastNumWord; _lastNumTrail = st.LastNumTrail;
                _history.Append(st.History ?? "");
            }
            _numSuggestions = new List<string>();
            Fire();
        }

        public bool PasswordMode
        {
            get { return _passwordMode; }
            set
            {
                if (value == _passwordMode) return;
                if (value)
                {
                    if (_word.Length > 0) FinishWord();
                    CloseNumber();
                    _word.Clear(); ForgetLastWord(); _prevWord = _prevBefore = null;
                    _secret.Clear(); _secretValid = true;
                }
                _passwordMode = value;
                Fire();
            }
        }

        /// <summary>Im Passwortfeld Getipptes holen (null, wenn nichts oder nicht sicher bekannt) und vergessen.</summary>
        public string TakeSecret()
        {
            string r = _secretValid && _secret.Length > 0 ? _secret.ToString() : null;
            _secret.Clear();
            _secretValid = true;
            return r;
        }

        private void AddHistory(string s)
        {
            _history.Append(s);
            if (_history.Length > MaxHistory) _history.Remove(0, _history.Length - MaxHistory);
        }

        private void TrackText(string s)
        {
            if (s.IndexOfAny(new[] { ' ', '\n', '\t' }) >= 0) CheckEmailChunk();
            AddHistory(s);
            foreach (char c in s)
            {
                TrackNumber(c);
                if (IsWordChar(c) && !(c == '-' && _word.Length == 0))
                {
                    if (_word.Length == 0)
                    {
                        _wordAtSentence = _sentenceStart;
                        ForgetLastWord();
                    }
                    _word.Append(c);
                    _sentenceStart = false;
                }
                else
                {
                    if (_word.Length > 0)
                    {
                        FinishWord();
                        _lastWord = _word.ToString();
                        _lastWordAtSentence = _wordAtSentence;
                        _trailing.Clear();
                    }
                    if (c == '.' || c == '!' || c == '?' || c == '\n') _prevWord = null;   // neuer Satz: keine Wortfolge über Satzgrenzen
                    if (_lastWord != null)
                    {
                        if (c == '\n' || _trailing.Length >= MaxTrailing) ForgetLastWord();
                        else _trailing.Append(c);
                    }
                    _word.Clear();

                    if (c == ' ')
                        _sentenceStart = _lastChar == '.' || _lastChar == '!' || _lastChar == '?'
                            || (_lastChar == ' ' && _sentenceStart);
                    else
                        _sentenceStart = false;
                }
                _lastChar = c;
            }
        }

        public List<string> Suggestions()
        {
            string w = _word.ToString();
            bool cap = _settings.AutoCapitalize && (w.Length == 0 ? _sentenceStart : _wordAtSentence);
            if (_passwordMode) return new List<string>();
            int max = _settings.SuggestionCount * _settings.SuggestionRows;
            if (_num.Length > 0 && !_numIban && (_word.Length == 0 || AllDigits(_word)))
            {
                var ns = _predictor.SuggestNumbers(_num.ToString(), max);
                if (ns.Count > 0) { _numSuggestions = ns; return new List<string>(ns); }
            }
            _numSuggestions = new List<string>();
            return _predictor.Suggest(w, _prevWord, _settings.SuggestionCount * _settings.SuggestionRows, cap);
        }

        // ---------- Groß/Klein (Aa) ----------

        public bool CanToggleCase
        {
            get { return CaseDirection != 0; }
        }

        /// <summary>Wort, auf das die Aa-Taste gerade wirken würde (oder null).</summary>
        public string CaseTarget
        {
            get { return _word.Length > 0 ? _word.ToString() : _lastWord; }
        }

        /// <summary>+1 = Pfeil nach oben (wird groß), -1 = Pfeil nach unten (wird klein), 0 = nichts zu tun.</summary>
        public int CaseDirection
        {
            get
            {
                string w = CaseTarget;
                if (w == null) return 0;
                string nw = NextCase(w);
                if (nw == w) return 0;
                return HasUpper(w) ? -1 : 1;
            }
        }

        /// <summary>Beschriftung der Taste: "abc" (Pfeil hoch), "ABC" (Pfeil runter) oder "Aa" (grau).</summary>
        public string CaseLabel
        {
            get { int d = CaseDirection; return d > 0 ? "abc" : d < 0 ? "ABC" : "Aa"; }
        }

        private static bool HasUpper(string w)
        {
            foreach (char c in w) if (char.IsLetter(c) && char.IsUpper(c)) return true;
            return false;
        }

        /// <summary>
        /// Zwei Zustände: klein → erster Buchstabe groß, und alles mit Großbuchstaben → klein.
        /// "haus" → "Haus", "Haus" → "haus", "HAUS" → "haus", "iPhone" → "iphone".
        /// </summary>
        public static string NextCase(string w)
        {
            string lower = w.ToLowerInvariant();
            int first = -1;
            for (int i = 0; i < w.Length; i++) if (char.IsLetter(w[i])) { first = i; break; }
            if (first < 0) return w;
            if (HasUpper(w)) return lower;
            return lower.Substring(0, first) + char.ToUpperInvariant(lower[first]) + lower.Substring(first + 1);
        }

        public void ToggleCase()
        {
            string w, trail;
            bool current = _word.Length > 0;
            if (current) { w = _word.ToString(); trail = ""; }
            else if (_lastWord != null) { w = _lastWord; trail = _trailing.ToString(); }
            else return;

            string nw = NextCase(w);
            if (nw == w) return;

            // nur ab dem ersten geänderten Buchstaben neu tippen
            int p = 0;
            while (p < w.Length && p < nw.Length && w[p] == nw[p]) p++;
            _out.SendBackspaces(w.Length - p + trail.Length);
            _out.SendText(nw.Substring(p) + trail);

            if (_history.Length >= w.Length + trail.Length)
            {
                _history.Length -= w.Length + trail.Length;
                _history.Append(nw).Append(trail);
            }

            if (current) { _word.Clear(); _word.Append(nw); }
            else _lastWord = nw;

            var h = CaseChanged; if (h != null) h(w, nw);
            Fire();
        }

        // ---------- Tasten ----------

        private bool _shiftForNav;   // Shift 1x bleibt über mehrere Pfeiltasten hinweg an (Markieren)

        private static bool IsNavKey(KeyDef k)
        {
            if (k == null || k.Kind != KeyKind.Special) return false;
            ushort v = k.Vk;
            return v == 0x25 || v == 0x26 || v == 0x27 || v == 0x28 || v == 0x21 || v == 0x22 || v == 0x23 || v == 0x24;   // Pfeile, Bild auf/ab, Ende, Pos1
        }

        public void Press(KeyDef k)
        {
            // Nach dem Markieren mit Shift + Pfeilen: die nächste andere Taste kommt wieder ohne Shift
            if (_shiftForNav && !IsNavKey(k) && !(k.Kind == KeyKind.Modifier && k.Mod == Modifiers.Shift))
            {
                _shiftForNav = false;
                if (Shift == ModState.Once) Shift = ModState.Off;
            }
            if (_passwordMode && PressInPasswordField(k)) { ReleaseOnce(); Fire(); return; }
            switch (k.Kind)
            {
                case KeyKind.Modifier:
                    switch (k.Mod)
                    {
                        case Modifiers.Shift: Shift = Cycle(Shift); _shiftForNav = false; break;
                        case Modifiers.Ctrl: Ctrl = Cycle(Ctrl); break;
                        case Modifiers.Alt: Alt = Cycle(Alt); break;
                        case Modifiers.Win:
                            // 1x: Win+nächste Taste. 2x: Windows-Taste allein (Startmenü), wie auf der echten Tastatur
                            if (Win == ModState.Off) Win = ModState.Once;
                            else { Win = ModState.Off; _out.SendVk(0x5B, Modifiers.None); }
                            break;
                    }
                    Fire();
                    return;

                case KeyKind.AltGr:
                    AltGr = Cycle(AltGr);
                    Fire();
                    return;

                case KeyKind.Caps:
                    Caps = !Caps;
                    Fire();
                    return;

                case KeyKind.CaseToggle:
                    ToggleCase();
                    return;

                case KeyKind.Space:
                    if (_punctSpaced != '\0' && _autoSpaced && !HasShortcutMods && _deadPending == '\0')
                    {
                        // Leerzeichen kam schon automatisch nach dem Satzzeichen: kein doppeltes
                        _autoSpaced = false; _punctSpaced = '\0';
                        break;
                    }
                    _autoSpaced = false; _punctSpaced = '\0';
                    if (HasShortcutMods)
                    {
                        _out.SendVk(InputSender.VK_SPACE, ActiveMods());
                        if (_word.Length > 0) FinishWord();
                        _prevWord = null;
                        _word.Clear(); ForgetLastWord(); _history.Clear();
                        _sentenceStart = false;
                    }
                    else if (_deadPending != '\0')
                    {
                        string d = _deadPending.ToString();
                        _deadPending = '\0';
                        _out.SendText(d);
                        TrackText(d);
                    }
                    else
                    {
                        _out.SendText(" ");
                        TrackText(" ");
                    }
                    break;

                case KeyKind.Char:
                    bool wasAuto = _autoSpaced;
                    char punct = _punctSpaced;
                    _autoSpaced = false; _punctSpaced = '\0';
                    PressChar(k, wasAuto, punct);
                    break;

                case KeyKind.Special:
                    _autoSpaced = false;
                    _deadPending = '\0';
                    PressSpecial(k.Vk);
                    break;
            }
            bool keepShift = Shift == ModState.Once && IsNavKey(k);
            ReleaseOnce();
            if (keepShift) { Shift = ModState.Once; _shiftForNav = true; }
            Fire();
        }

        /// <summary>Tippen im Passwortfeld. true = erledigt.</summary>
        private bool PressInPasswordField(KeyDef k)
        {
            if (k.Kind == KeyKind.Char && !HasShortcutMods)
            {
                string s = OutputFor(k);
                if (s.Length == 0) return true;
                _out.SendText(s);
                _secret.Append(s);
                return true;
            }
            if (k.Kind == KeyKind.Space && !HasShortcutMods)
            {
                _out.SendText(" ");
                _secret.Append(' ');
                return true;
            }
            if (k.Kind == KeyKind.CaseToggle) return true;
            if (k.Kind == KeyKind.Special)
            {
                var mods = ActiveMods();
                _out.SendVk(k.Vk, mods);
                if (k.Vk == InputSender.VK_BACK && mods == Modifiers.None) { if (_secret.Length > 0) _secret.Length--; }
                else if (k.Vk == InputSender.VK_RETURN && mods == Modifiers.None) { var h = SecretSubmitted; if (h != null) h(); }
                else if (k.Vk != InputSender.VK_TAB || mods != Modifiers.None) _secretValid = false;
                return true;
            }
            if (k.Kind == KeyKind.Char || k.Kind == KeyKind.Space)
            {
                // Tastenkürzel wie Strg+V: Inhalt nicht mehr sicher bekannt
                _secretValid = false;
                return false;
            }
            return false;
        }

        private void PressChar(KeyDef k, bool afterAutoSpace, char punctBefore = '\0')
        {
            if (HasShortcutMods && AltGr == ModState.Off)
            {
                // Tastenkürzel wie Strg+C: echte Taste mit Sondertasten schicken
                char basis = k.Normal.Length > 0 ? k.Normal[0] : '\0';
                if (basis != '\0') _out.SendCharWithModifiers(basis, ActiveMods());
                if (_word.Length > 0) FinishWord();
                DropNumber();
                _prevWord = null;
                _word.Clear(); ForgetLastWord(); _history.Clear();
                _sentenceStart = false;
                return;
            }
            string s = OutputFor(k);
            if (s.Length == 0) return;
            if (_deadPending != '\0')
            {
                char d = _deadPending;
                _deadPending = '\0';
                char comb;
                s = DeadKeys.TryGetValue(d, out comb) ? KeyLayouts.Compose(d, comb, s) : d + s;
                if (Shift == ModState.Once) Shift = ModState.Off;
            }
            else if (s.Length == 1 && DeadKeys.ContainsKey(s[0]))
            {
                _deadPending = s[0];   // wartet auf den nächsten Buchstaben
                return;
            }
            if (afterAutoSpace && punctBefore != '\0' && s.Length == 1 && (char.IsDigit(s[0]) || "@/-_".IndexOf(s[0]) >= 0
                || (punctBefore == '.' || punctBefore == ':') && char.IsLower(s[0])))
            {
                // doch kein neuer Satz ("1,5", "Std.-Plan"): das automatische Leerzeichen wieder weg
                _out.SendBackspaces(1);
                if (_history.Length > 0) _history.Length--;
                TrackBackspace();
                afterAutoSpace = false;
            }
            if (afterAutoSpace && (_settings.AutoSpaceAfterSuggestion || punctBefore != '\0') && s.Length == 1 && SnugPunctuation.IndexOf(s[0]) >= 0)
            {
                // "Haus ." wird zu "Haus. " – das automatische Leerzeichen wandert hinter das Satzzeichen
                _out.SendBackspaces(1);
                if (_history.Length > 0) _history.Length--;
                TrackBackspace();
                _out.SendText(s + " ");
                TrackText(s + " ");
                _autoSpaced = true;
                _punctSpaced = s[0];
                return;
            }
            string chunkBefore = CurrentChunk;
            _out.SendText(s);
            TrackText(s);
            if (s.Length == 1 && SnugPunctuation.IndexOf(s[0]) >= 0 && WantsPunctuationSpace(s[0], chunkBefore))
            {
                _out.SendText(" ");
                TrackText(" ");
                _autoSpaced = true;
                _punctSpaced = s[0];
            }
        }

        /// <summary>Wortpuffer so anpassen, als wäre ein Zeichen gelöscht worden.</summary>
        private void TrackBackspace()
        {
            BackspaceNumber();
            if (_word.Length > 0)
            {
                _word.Length--;
                if (_word.Length == 0) _sentenceStart = _wordAtSentence;
                _lastChar = _word.Length > 0 ? _word[_word.Length - 1] : '\0';
            }
            else if (_lastWord != null && _trailing.Length > 0)
            {
                _trailing.Length--;
                _sentenceStart = false;
                if (_trailing.Length == 0)
                {
                    // wieder am Wortende: das Wort ist doch nicht fertig, Lernen zurücknehmen
                    var h = WordReopened;
                    if (h != null && !_passwordMode) h(_lastWord, _prevBefore);
                    _prevWord = _prevBefore;
                    _word.Append(_lastWord);
                    _wordAtSentence = _lastWordAtSentence;
                    _lastChar = _lastWord[_lastWord.Length - 1];
                    ForgetLastWord();
                }
                else _lastChar = _trailing[_trailing.Length - 1];
            }
            else
            {
                _sentenceStart = false;
                _lastChar = '\0';
            }
        }

        private void PressSpecial(ushort vk)
        {
            var mods = ActiveMods();
            _out.SendVk(vk, mods);
            if (vk == InputSender.VK_BACK && mods == Modifiers.None)
            {
                if (_history.Length > 0) _history.Length--;
                TrackBackspace();
            }
            else if (vk == InputSender.VK_RETURN && mods == Modifiers.None)
            {
                if (_word.Length > 0) FinishWord();
                TrackNumber('\n');
                _prevWord = null;
                _word.Clear();
                ForgetLastWord();
                CheckEmailChunk(); AddHistory("\n");
                _sentenceStart = true;
                _lastChar = '\n';
            }
            else if (vk == InputSender.VK_TAB && mods == Modifiers.None)
            {
                if (_word.Length > 0) FinishWord();
                TrackNumber('\t');
                _prevWord = null;
                _word.Clear();
                ForgetLastWord();
                CheckEmailChunk(); AddHistory("\t");
                _sentenceStart = false;
                _lastChar = '\t';
            }
            else
            {
                // Pfeile, Entf, Pos1 usw.: Cursor ist woanders
                if (_word.Length > 0) FinishWord();
                CloseNumber();
                _prevWord = null;
                _word.Clear();
                ForgetLastWord();
                _history.Clear();
                _sentenceStart = false;
                _lastChar = '\0';
            }
        }

        /// <summary>Vorschlag übernehmen: nur den fehlenden Rest tippen, wenn möglich.</summary>
        public void PressSuggestion(string word)
        {
            _deadPending = '\0';
            if (_num.Length > 0 && _numSuggestions.Contains(word)) { PressNumberSuggestion(word); return; }
            _num.Clear(); _pendingPlz = null; _lastNum = null;   // angefangene Zahl wird durch ein Wort ersetzt
            string typed = _word.ToString();
            if (typed.Length > 0 && word.StartsWith(typed, StringComparison.Ordinal))
            {
                _out.SendText(word.Substring(typed.Length));
            }
            else
            {
                if (typed.Length > 0) _out.SendBackspaces(typed.Length);
                _out.SendText(word);
            }
            if (_history.Length >= typed.Length) _history.Length -= typed.Length;
            AddHistory(word);
            if (typed.Length == 0) { _wordAtSentence = _sentenceStart; ForgetLastWord(); }
            _word.Clear();
            _word.Append(word);
            _sentenceStart = false;
            _lastChar = word.Length > 0 ? word[word.Length - 1] : '\0';
            if (_settings.AutoSpaceAfterSuggestion)
            {
                _out.SendText(" ");
                TrackText(" ");
                _autoSpaced = true;
            }
            if (Shift == ModState.Once) Shift = ModState.Off;
            Fire();
        }

        /// <summary>Gelernte Zahl übernehmen: Getipptes (mit Trennzeichen) ersetzen, Schreibweise wie gelernt.</summary>
        private void PressNumberSuggestion(string number)
        {
            string typed = _num.ToString();
            if (number.StartsWith(typed, StringComparison.Ordinal)) _out.SendText(number.Substring(typed.Length));
            else
            {
                _out.SendBackspaces(typed.Length);
                _out.SendText(number);
            }
            if (_history.Length >= typed.Length) _history.Length -= typed.Length;
            AddHistory(number);
            _word.Clear();
            ForgetLastWord();
            _num.Clear(); _num.Append(number);
            _numPhone = number[0] == '+' || number[0] == '(' || number[0] == '0';
            _numSuggestions = new List<string>();
            _sentenceStart = false;
            _lastChar = number[number.Length - 1];
            if (_settings.AutoSpaceAfterSuggestion)
            {
                _out.SendText(" ");
                TrackText(" ");
                _autoSpaced = true;
            }
            if (Shift == ModState.Once) Shift = ModState.Off;
            Fire();
        }
    }
}
