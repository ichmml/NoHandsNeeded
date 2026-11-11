using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Tastatur
{
    internal enum StepKind { Text, Keys, Open, Wait }

    internal class MacroStep
    {
        public StepKind Kind;
        public string Value = "";

        public MacroStep() { }
        public MacroStep(StepKind k, string v) { Kind = k; Value = v ?? ""; }

        public static string KindKey(StepKind k)
        {
            switch (k)
            {
                case StepKind.Keys: return "tasten";
                case StepKind.Open: return "oeffnen";
                case StepKind.Wait: return "warten";
                default: return "text";
            }
        }

        public static string KindLabel(StepKind k)
        {
            switch (k)
            {
                case StepKind.Keys: return L.T("Tasten");
                case StepKind.Open: return L.T("Öffnen");
                case StepKind.Wait: return L.T("Warten");
                default: return L.T("Text");
            }
        }

        public override string ToString()
        {
            string v = Value.Replace("\n", " ⏎ ");
            if (v.Length > 60) v = v.Substring(0, 57) + "…";
            return KindLabel(Kind) + ": " + v + (Kind == StepKind.Wait ? " ms" : "");
        }
    }

    internal class Macro
    {
        public string Name = "";
        public string Group = "";
        public string Program = "";     // z. B. "chrome, msedge", leer = überall
        public string Color = "";       // blau, grün, rot, orange, lila, gelb, grau
        public string Hotkey = "";      // globales Kürzel, z. B. F16
        public List<MacroStep> Steps = new List<MacroStep>();
        public int SourceLine;

        public Macro Clone()
        {
            var m = (Macro)MemberwiseClone();
            m.Steps = new List<MacroStep>();
            foreach (var s in Steps) m.Steps.Add(new MacroStep(s.Kind, s.Value));
            return m;
        }

        public bool MatchesProgram(string processName)
        {
            if (string.IsNullOrWhiteSpace(Program) || string.IsNullOrEmpty(processName)) return false;
            foreach (var p in Program.Split(','))
            {
                string t = p.Trim();
                if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 4);
                if (t.Length > 0 && string.Equals(t, processName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public override string ToString()
        {
            return (Group.Length > 0 ? Group + " › " : "") + Name + (Hotkey.Length > 0 ? "   [" + Hotkey + "]" : "");
        }

        // Werte in makros.yaml (deutsch, englische Namen werden auch verstanden) // i18n-ok
        public static readonly string[] ColorNames = { "", "blau", "grün", "rot", "orange", "lila", "gelb", "grau" }; // i18n-ok

        /// <summary>Farbname in der Oberflächensprache.</summary>
        public static string ColorLabel(string name)
        {
            switch (name)
            {
                case "": return L.T("keine");
                case "blau": return L.T("blau");
                case "grün": return L.T("grün");
                case "rot": return L.T("rot");
                case "orange": return L.T("orange");
                case "lila": return L.T("lila");
                case "gelb": return L.T("gelb");
                case "grau": return L.T("grau");
            }
            return name;
        }

        public static System.Drawing.Color? ColorValue(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "blau": case "blue": return System.Drawing.Color.FromArgb(52, 120, 246);
                case "grün": case "gruen": case "green": return System.Drawing.Color.FromArgb(46, 160, 67); // i18n-ok
                case "rot": case "red": return System.Drawing.Color.FromArgb(220, 53, 69);
                case "orange": return System.Drawing.Color.FromArgb(240, 140, 30);
                case "lila": case "purple": return System.Drawing.Color.FromArgb(137, 87, 229);
                case "gelb": case "yellow": return System.Drawing.Color.FromArgb(230, 190, 30);
                case "grau": case "gray": case "grey": return System.Drawing.Color.FromArgb(128, 128, 128);
            }
            return null;
        }
    }

    internal class MacroFileException : Exception
    {
        public int Line;
        public MacroFileException(int line, string msg) : base(L.T("Zeile ") + line + ": " + msg) { Line = line; }
    }

    /// <summary>
    /// Liest und schreibt makros.yaml. Unterstützt den Teil von YAML, den die Datei braucht:
    /// eine Liste von Einträgen mit "schluessel: wert", Blocktext mit "|" und eine Liste "schritte".
    /// </summary>
    internal static class MacroStore
    {
        private class Node
        {
            public int Line;
            public List<KeyValuePair<string, object>> Fields = new List<KeyValuePair<string, object>>();
        }

        private static readonly Regex KeyRe = new Regex(@"^([A-Za-zÄÖÜäöüß_][A-Za-zÄÖÜäöüß0-9_\-]*)\s*:(?:\s+(.*)|)$"); // i18n-ok

        private static int Indent(string l)
        {
            int i = 0;
            while (i < l.Length && l[i] == ' ') i++;
            return i;
        }

        private static bool IsBlank(string l)
        {
            string t = l.Trim();
            return t.Length == 0 || t.StartsWith("#");
        }

        private static void SkipBlank(string[] lines, ref int i)
        {
            while (i < lines.Length && IsBlank(lines[i])) i++;
        }

        private static bool IsDash(string trimmed)
        {
            return trimmed == "-" || trimmed.StartsWith("- ");
        }

        public static List<Macro> Parse(string text)
        {
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int n = 0; n < lines.Length; n++)
                if (lines[n].IndexOf('\t') >= 0 && lines[n].TrimStart(' ').StartsWith("\t"))
                    throw new MacroFileException(n + 1, L.T("Bitte Leerzeichen statt Tabulator zum Einrücken verwenden."));

            int i = 0;
            SkipBlank(lines, ref i);
            var result = new List<Macro>();
            if (i >= lines.Length) return result;
            int top = Indent(lines[i]);
            var nodes = ParseList(lines, ref i, top);
            SkipBlank(lines, ref i);
            if (i < lines.Length) throw new MacroFileException(i + 1, L.T("Unerwarteter Text, Einrückung prüfen."));
            foreach (var n in nodes) result.Add(ToMacro(n));
            return result;
        }

        private static List<Node> ParseList(string[] lines, ref int i, int indent)
        {
            var list = new List<Node>();
            while (true)
            {
                SkipBlank(lines, ref i);
                if (i >= lines.Length) break;
                int ind = Indent(lines[i]);
                string t = lines[i].Trim();
                if (ind < indent) break;
                if (ind > indent) throw new MacroFileException(i + 1, L.T("Einrückung passt nicht."));
                if (!IsDash(t)) break;

                var node = new Node { Line = i + 1 };
                string afterDash = lines[i].Substring(ind + 1);
                string rest = afterDash.TrimStart(' ');
                int col = ind + 1 + (afterDash.Length - rest.Length);
                int dashLine = i;
                if (rest.Length > 0)
                {
                    ParseKeyValue(lines, ref i, rest, col, ind, node);
                }
                else
                {
                    i++;
                    SkipBlank(lines, ref i);
                    if (i >= lines.Length || Indent(lines[i]) <= ind)
                        throw new MacroFileException(dashLine + 1, "Leerer Eintrag.");
                    col = Indent(lines[i]);
                }
                while (true)
                {
                    SkipBlank(lines, ref i);
                    if (i >= lines.Length) break;
                    int fi = Indent(lines[i]);
                    if (fi != col || IsDash(lines[i].Trim())) break;
                    ParseKeyValue(lines, ref i, lines[i].Substring(fi), fi, fi, node);
                }
                list.Add(node);
            }
            return list;
        }

        private static void ParseKeyValue(string[] lines, ref int i, string text, int keyCol, int ownerIndent, Node node)
        {
            var m = KeyRe.Match(text.TrimEnd());
            if (!m.Success) throw new MacroFileException(i + 1, "Erwartet wird \"name: wert\".");
            string key = m.Groups[1].Value.ToLowerInvariant();
            string raw = m.Groups[2].Success ? m.Groups[2].Value : "";
            int line = i;
            i++;

            if (raw.Trim().Length == 0)
            {
                int j = i;
                SkipBlank(lines, ref j);
                if (j < lines.Length && IsDash(lines[j].Trim()) && Indent(lines[j]) >= keyCol)
                {
                    i = j;
                    var sub = ParseList(lines, ref i, Indent(lines[j]));
                    node.Fields.Add(new KeyValuePair<string, object>(key, sub));
                    return;
                }
                node.Fields.Add(new KeyValuePair<string, object>(key, ""));
                return;
            }

            string v = raw.Trim();
            if (v == "|" || v == "|-" || v == "|+")
            {
                node.Fields.Add(new KeyValuePair<string, object>(key, ReadBlock(lines, ref i, ownerIndent, v)));
                return;
            }
            if (v.StartsWith("\""))
            {
                node.Fields.Add(new KeyValuePair<string, object>(key, Unquote(v, line + 1)));
                return;
            }
            if (v.StartsWith("'"))
            {
                if (v.Length < 2 || !v.EndsWith("'")) throw new MacroFileException(line + 1, "Fehlendes ' am Ende.");
                node.Fields.Add(new KeyValuePair<string, object>(key, v.Substring(1, v.Length - 2).Replace("''", "'")));
                return;
            }
            node.Fields.Add(new KeyValuePair<string, object>(key, v));
        }

        private static string ReadBlock(string[] lines, ref int i, int ownerIndent, string style)
        {
            var parts = new List<string>();
            int blockIndent = -1;
            while (i < lines.Length)
            {
                string l = lines[i];
                if (l.Trim().Length == 0) { parts.Add(""); i++; continue; }
                int ind = Indent(l);
                if (blockIndent < 0)
                {
                    if (ind <= ownerIndent) break;
                    blockIndent = ind;
                }
                if (ind < blockIndent) break;
                parts.Add(l.Substring(blockIndent));
                i++;
            }
            // Leerzeilen am Ende gehören nicht zum nächsten Eintrag
            int trailingBlank = 0;
            while (parts.Count > 0 && parts[parts.Count - 1].Length == 0) { parts.RemoveAt(parts.Count - 1); trailingBlank++; }
            string text = string.Join("\n", parts);
            if (style == "|") text += parts.Count > 0 ? "\n" : "";
            else if (style == "|+") text += new string('\n', trailingBlank + (parts.Count > 0 ? 1 : 0));
            return text;
        }

        private static string Unquote(string v, int line)
        {
            var sb = new StringBuilder();
            int k = 1;
            for (; k < v.Length; k++)
            {
                char c = v[k];
                if (c == '\\' && k + 1 < v.Length)
                {
                    char n = v[++k];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append('\\').Append(n); break;
                    }
                }
                else if (c == '"') break;
                else sb.Append(c);
            }
            if (k >= v.Length) throw new MacroFileException(line, "Fehlendes \" am Ende.");
            if (v.Substring(k + 1).Trim().Length > 0) throw new MacroFileException(line, L.T("Text nach dem schließenden \"."));
            return sb.ToString();
        }

        private static StepKind? StepKindOf(string key)
        {
            switch (key)
            {
                case "text": return StepKind.Text;
                case "tasten": case "keys": return StepKind.Keys;
                case "oeffnen": case "öffnen": case "open": return StepKind.Open; // i18n-ok
                case "warten": case "wait": return StepKind.Wait;
            }
            return null;
        }

        private static MacroStep MakeStep(StepKind kind, object value, int line)
        {
            var s = value as string;
            if (s == null) throw new MacroFileException(line, L.T("Hier wird ein Wert erwartet, keine Liste."));
            if (kind == StepKind.Wait)
            {
                int ms;
                if (!int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ms) || ms < 0)
                    throw new MacroFileException(line, "\"warten\" braucht Millisekunden, z. B. warten: 500");
            }
            if (kind == StepKind.Keys)
            {
                string bad;
                if (KeyCombo.ParseSequence(s, out bad) == null)
                    throw new MacroFileException(line, L.F("Unbekannte Taste \"{0}\".", bad));
            }
            return new MacroStep(kind, s);
        }

        private static Macro ToMacro(Node n)
        {
            var m = new Macro { SourceLine = n.Line };
            foreach (var f in n.Fields)
            {
                var kind = StepKindOf(f.Key);
                if (kind.HasValue) { m.Steps.Add(MakeStep(kind.Value, f.Value, n.Line)); continue; }
                if (f.Key == "schritte" || f.Key == "steps")
                {
                    var list = f.Value as List<Node>;
                    if (list == null) { if ((f.Value as string) == "") continue; throw new MacroFileException(n.Line, L.F("\"{0}\" muss eine Liste sein.", f.Key)); }
                    foreach (var sn in list)
                    {
                        foreach (var sf in sn.Fields)
                        {
                            var sk = StepKindOf(sf.Key);
                            if (!sk.HasValue) throw new MacroFileException(sn.Line, L.F("Unbekannter Schritt \"{0}\". Erlaubt: text, tasten, oeffnen, warten (oder text, keys, open, wait)", sf.Key));
                            m.Steps.Add(MakeStep(sk.Value, sf.Value, sn.Line));
                        }
                    }
                    continue;
                }
                var sv = f.Value as string;
                if (sv == null) throw new MacroFileException(n.Line, L.F("\"{0}\" darf keine Liste sein.", f.Key));
                switch (f.Key)
                {
                    case "name": m.Name = sv.Trim(); break;
                    case "gruppe": case "group": m.Group = sv.Trim(); break;
                    case "programm": case "program": m.Program = sv.Trim(); break;
                    case "farbe": case "color": case "colour": m.Color = sv.Trim(); break;
                    case "kuerzel": case "kürzel": case "hotkey": // i18n-ok
                        if (sv.Trim().Length > 0 && KeyCombo.Parse(sv) == null)
                            throw new MacroFileException(n.Line, L.F("Unbekanntes Kürzel \"{0}\".", sv));
                        m.Hotkey = sv.Trim();
                        break;
                    default:
                        throw new MacroFileException(n.Line, L.F("Unbekannter Eintrag \"{0}\".", f.Key));
                }
            }
            if (m.Name.Length == 0) throw new MacroFileException(n.Line, L.T("Makro ohne \"name\"."));
            return m;
        }

        // ---------- Schreiben ----------

        private const string Header =
@"# Makros der Bildschirmtastatur
#
# Jedes Makro beginnt mit ""- name:"". Mögliche Einträge:
#   name:      Beschriftung des Knopfs (Pflicht)
#   gruppe:    Ordner in der Makroleiste, z. B. Shop, Verein, Privat
#   programm:  nur zuordnen, z. B. chrome oder ""chrome, msedge"" (passende Makros stehen vorne)
#   farbe:     blau, grün, rot, orange, lila, gelb, grau
#   kuerzel:   globales Kürzel, z. B. F16 oder Strg+Alt+Shift+1
#
# Aktionen (beliebig viele, werden der Reihe nach ausgeführt):
#   text:      Text einfügen. Platzhalter: {datum} {uhrzeit} {wochentag} {cursor}
#   tasten:    Tastenfolge, mit Komma getrennt, z. B. Strg+A, Strg+C, Enter
#   oeffnen:   Programm, Datei, Ordner oder Webseite
#   warten:    Pause in Millisekunden
#
# Mehrere Aktionen schreibt man als Liste unter ""schritte:"".
# Mehrzeiliger Text: ""text: |-"" und darunter die Zeilen eingerückt.
# Einrücken nur mit Leerzeichen. Kommentare nur in eigenen Zeilen mit #.

";

        private static bool NeedsQuote(string s)
        {
            if (s.Length == 0) return true;
            if (s != s.Trim()) return true;
            if ("-?:,[]{}#&*!|>'\"%@`".IndexOf(s[0]) >= 0) return true;
            if (s.Contains(": ") || s.EndsWith(":") || s.Contains(" #")) return true;
            if (s.IndexOf('\t') >= 0 || s.IndexOf('\n') >= 0) return true;
            return false;
        }

        private static string Quote(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        }

        private static void WriteValue(StringBuilder sb, string key, string value, int indent)
        {
            string pad = new string(' ', indent);
            if (value.IndexOf('\n') >= 0)
            {
                string body = value;
                string style = "|-";
                if (body.EndsWith("\n") && !body.EndsWith("\n\n")) { style = "|"; body = body.Substring(0, body.Length - 1); }
                var lines = body.Split('\n');
                bool blockOk = !body.EndsWith("\n") && lines[0].Length > 0 && lines[0][0] != ' '
                    && body.IndexOf('\t') < 0;
                foreach (var l in lines) if (l.Length > 0 && l != l.TrimEnd()) blockOk = false;
                if (blockOk)
                {
                    sb.Append(key).Append(": ").Append(style).Append('\n');
                    foreach (var l in lines)
                    {
                        if (l.Length == 0) sb.Append('\n');
                        else sb.Append(pad).Append("  ").Append(l).Append('\n');
                    }
                    return;
                }
                sb.Append(key).Append(": ").Append(Quote(value)).Append('\n');
                return;
            }
            sb.Append(key).Append(": ").Append(NeedsQuote(value) ? Quote(value) : value).Append('\n');
        }

        public static string Serialize(List<Macro> macros)
        {
            var sb = new StringBuilder(Header);
            foreach (var m in macros)
            {
                sb.Append("- ");
                WriteValue(sb, "name", m.Name, 2);
                if (m.Group.Length > 0) { sb.Append("  "); WriteValue(sb, "gruppe", m.Group, 2); }
                if (m.Program.Length > 0) { sb.Append("  "); WriteValue(sb, "programm", m.Program, 2); }
                if (m.Color.Length > 0) { sb.Append("  "); WriteValue(sb, "farbe", m.Color, 2); }
                if (m.Hotkey.Length > 0) { sb.Append("  "); WriteValue(sb, "kuerzel", m.Hotkey, 2); }
                if (m.Steps.Count == 1)
                {
                    sb.Append("  ");
                    WriteValue(sb, MacroStep.KindKey(m.Steps[0].Kind), m.Steps[0].Value, 2);
                }
                else if (m.Steps.Count > 1)
                {
                    sb.Append("  schritte:\n");
                    foreach (var s in m.Steps)
                    {
                        sb.Append("    - ");
                        WriteValue(sb, MacroStep.KindKey(s.Kind), s.Value, 6);
                    }
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string FilePath { get { return Path.Combine(Settings.DataDir, "makros.yaml"); } }

        /// <summary>Laden. Bei Fehlern bleibt die Datei unverändert, error enthält die Meldung.</summary>
        public static List<Macro> Load(out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(FilePath))
                {
                    var ex = Examples();
                    Save(ex);
                    return ex;
                }
                return Parse(File.ReadAllText(FilePath, Encoding.UTF8));
            }
            catch (MacroFileException e) { error = "makros.yaml, " + e.Message; }
            catch (Exception e) { error = L.T("makros.yaml konnte nicht gelesen werden: ") + e.Message; }
            return null;
        }

        public static void Save(List<Macro> macros)
        {
            string tmp = FilePath + ".neu";
            File.WriteAllText(tmp, Serialize(macros), new UTF8Encoding(false));
            if (File.Exists(FilePath))
            {
                string bak = FilePath + ".bak";
                if (File.Exists(bak)) File.Delete(bak);
                File.Replace(tmp, FilePath, bak);
            }
            else File.Move(tmp, FilePath);
        }

        public static List<Macro> Examples()
        {
            var l = new List<Macro>();
            Func<string, string, StepKind, string, string, Macro> mk = (name, group, kind, value, color) =>
            {
                var m = new Macro { Name = name, Group = group, Color = color };
                m.Steps.Add(new MacroStep(kind, value));
                return m;
            };
            // Beispiele beim ersten Start, in der Sprache der Oberfläche
            string ctrl = KeyLayouts.Label("Strg");
            string progs = L.T("Programme");
            l.Add(mk(L.T("Grüße"), "", StepKind.Text, L.T("Viele Grüße"), "blau"));
            l.Add(mk(L.T("Datum"), "", StepKind.Text, "{datum}", ""));
            l.Add(mk(L.T("Heute"), "", StepKind.Text, L.T("{wochentag}, {datum}, {uhrzeit} Uhr"), ""));
            l.Add(mk(L.T("Alles kopieren"), "", StepKind.Keys, ctrl + "+A, " + ctrl + "+C", "orange"));
            l.Add(mk(L.T("Desktop"), "", StepKind.Keys, "Win+D", "grau"));

            string browser = L.T("Browser");
            var tab = mk(L.T("Neuer Tab"), browser, StepKind.Keys, ctrl + "+T", "grün"); // i18n-ok
            tab.Program = "chrome, msedge, firefox";
            l.Add(tab);
            var close = mk(L.T("Tab zu"), browser, StepKind.Keys, ctrl + "+W", "rot");
            close.Program = "chrome, msedge, firefox";
            l.Add(close);
            var reopen = mk(L.T("Tab zurück"), browser, StepKind.Keys, ctrl + "+Shift+T", "");
            reopen.Program = "chrome, msedge, firefox";
            l.Add(reopen);

            l.Add(mk(L.T("Editor"), progs, StepKind.Open, "notepad.exe", ""));
            l.Add(mk(L.T("Rechner"), progs, StepKind.Open, "calc.exe", ""));

            var mail = new Macro { Name = L.T("Neue Mail"), Group = progs, Color = "lila" };
            mail.Steps.Add(new MacroStep(StepKind.Open, "https://mail.google.com/mail/?view=cm"));
            mail.Steps.Add(new MacroStep(StepKind.Wait, "4000"));
            mail.Steps.Add(new MacroStep(StepKind.Keys, "Tab, Tab"));
            mail.Steps.Add(new MacroStep(StepKind.Text, L.T("Hallo {cursor},\n\nViele Grüße")));
            l.Add(mail);
            return l;
        }
    }

    internal static class Placeholders
    {
        private static readonly CultureInfo De = new CultureInfo("de-DE");

        /// <summary>Ersetzt {datum}, {uhrzeit}, {wochentag}; {cursor} wird entfernt, cursorBack = Zeichen dahinter.</summary>
        public static string Expand(string text, DateTime now, out int cursorBack)
        {
            CultureInfo c = De;
            if (L.Lang != "de") { try { c = CultureInfo.GetCultureInfo(L.Lang); } catch { c = CultureInfo.InvariantCulture; } }
            string s = Regex.Replace(text, @"\{(datum|uhrzeit|wochentag|date|time|weekday)\}", m =>
            {
                switch (m.Groups[1].Value.ToLowerInvariant())
                {
                    case "datum": case "date": return c == De ? now.ToString("dd.MM.yyyy", De) : now.ToString(c.DateTimeFormat.ShortDatePattern, c);
                    case "uhrzeit": case "time": return c == De ? now.ToString("HH:mm", De) : now.ToString(c.DateTimeFormat.ShortTimePattern, c);
                    default: return c.DateTimeFormat.GetDayName(now.DayOfWeek);
                }
            }, RegexOptions.IgnoreCase);
            cursorBack = 0;
            var cm = Regex.Match(s, @"\{cursor\}", RegexOptions.IgnoreCase);
            if (cm.Success)
            {
                s = s.Remove(cm.Index, cm.Length);
                s = Regex.Replace(s, @"\{cursor\}", "", RegexOptions.IgnoreCase);
                string after = s.Substring(cm.Index).Replace("\r", "");
                cursorBack = after.Length;
            }
            return s;
        }
    }

    /// <summary>Leitet alles an die echte Ausgabe weiter und zeichnet bei Bedarf als Makro-Schritte auf.</summary>
    internal class RecordingOutput : IOutput
    {
        private readonly IOutput _inner;
        public bool Recording { get; private set; }
        /// <summary>Im Passwortfeld wird nichts aufgezeichnet.</summary>
        public bool Paused { get; set; }
        public List<MacroStep> Steps { get; private set; }

        public RecordingOutput(IOutput inner) { _inner = inner; Steps = new List<MacroStep>(); }

        public void Start() { Steps = new List<MacroStep>(); Recording = true; }
        public List<MacroStep> Stop() { Recording = false; var s = Steps; Steps = new List<MacroStep>(); return s; }

        private MacroStep Last { get { return Steps.Count > 0 ? Steps[Steps.Count - 1] : null; } }

        private void AddText(string t)
        {
            if (Last != null && Last.Kind == StepKind.Text) Last.Value += t;
            else Steps.Add(new MacroStep(StepKind.Text, t));
        }

        private void AddKey(string combo)
        {
            if (Last != null && Last.Kind == StepKind.Keys) Last.Value += ", " + combo;
            else Steps.Add(new MacroStep(StepKind.Keys, combo));
        }

        private void Back(int n)
        {
            for (int i = 0; i < n; i++)
            {
                var l = Last;
                if (l != null && l.Kind == StepKind.Text && l.Value.Length > 0)
                {
                    l.Value = l.Value.Substring(0, l.Value.Length - 1);
                    if (l.Value.Length == 0) Steps.RemoveAt(Steps.Count - 1);
                }
                else AddKey(KeyCombo.DisplayName(InputSender.VK_BACK));
            }
        }

        public void SendText(string text)
        {
            _inner.SendText(text);
            if (Recording && !Paused) AddText(text);
        }

        public void SendVk(ushort vk, Modifiers mods)
        {
            _inner.SendVk(vk, mods);
            if (!Recording || Paused) return;
            if (vk == InputSender.VK_BACK && mods == Modifiers.None) Back(1);
            else if (vk == InputSender.VK_RETURN && mods == Modifiers.None && Last != null && Last.Kind == StepKind.Text) AddText("\n");
            else AddKey(new KeyCombo { Vk = vk, Mods = mods }.ToString());
        }

        public bool SendCharWithModifiers(char c, Modifiers mods)
        {
            bool ok = _inner.SendCharWithModifiers(c, mods);
            if (Recording && !Paused)
            {
                var k = KeyCombo.Parse(c.ToString());
                if (k != null) { k.Mods = mods; AddKey(k.ToString()); }
            }
            return ok;
        }

        public void SendBackspaces(int n)
        {
            _inner.SendBackspaces(n);
            if (Recording && !Paused) Back(n);
        }
    }

    /// <summary>Führt Makros aus. Läuft asynchron, damit Pausen die Tastatur nicht blockieren.</summary>
    internal class MacroRunner
    {
        private readonly IOutput _out;
        private readonly Func<bool> _modifiersHeld;
        private readonly Func<int, Task> _delay;
        public bool Busy { get; private set; }
        public Action<string> OnError;

        public MacroRunner(IOutput output, Func<bool> modifiersHeld, Func<int, Task> delay = null)
        {
            _out = output;
            _modifiersHeld = modifiersHeld ?? (() => false);
            _delay = delay ?? (ms => Task.Delay(ms));
        }

        public static bool PhysicalModifiersHeld()
        {
            foreach (int vk in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
                if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) return true;
            return false;
        }

        public async Task Run(Macro m)
        {
            if (Busy) return;
            Busy = true;
            try
            {
                // Wurde das Makro per Kürzel ausgelöst, erst warten bis Strg/Alt/Shift/Win losgelassen sind
                for (int i = 0; i < 50 && _modifiersHeld(); i++) await _delay(30);

                for (int i = 0; i < m.Steps.Count; i++)
                {
                    var s = m.Steps[i];
                    switch (s.Kind)
                    {
                        case StepKind.Text:
                            int back;
                            string t = Placeholders.Expand(s.Value, DateTime.Now, out back);
                            _out.SendText(t);
                            for (int b = 0; b < back; b++) _out.SendVk(InputSender.VK_LEFT, Modifiers.None);
                            break;
                        case StepKind.Keys:
                            string bad;
                            var seq = KeyCombo.ParseSequence(s.Value, out bad);
                            if (seq == null) { Report(L.F("Unbekannte Taste \"{0}\" in Makro \"{1}\"", bad, m.Name)); return; }
                            foreach (var k in seq) { k.Send(_out); await _delay(20); }
                            break;
                        case StepKind.Open:
                            try
                            {
                                Process.Start(new ProcessStartInfo(s.Value.Trim()) { UseShellExecute = true });
                            }
                            catch (Exception e) { Report(L.F("\"{0}\" ließ sich nicht öffnen: ", s.Value) + e.Message); return; }
                            break;
                        case StepKind.Wait:
                            int ms;
                            int.TryParse(s.Value.Trim(), out ms);
                            await _delay(Math.Max(0, Math.Min(30000, ms)));
                            break;
                    }
                    if (i < m.Steps.Count - 1) await _delay(30);
                }
            }
            finally { Busy = false; }
        }

        private void Report(string msg)
        {
            var h = OnError; if (h != null) h(msg);
        }
    }
}
