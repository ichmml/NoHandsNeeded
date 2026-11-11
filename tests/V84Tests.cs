using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tastatur
{
    internal static class V84Tests
    {
        public static void Run(Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 8.4: Sichern und übertragen ---");
            var a = new LearningStore { Code = "de" };
            a.Learn("Quadstick", null, false); a.Learn("Quadstick", null, false); a.Learn("Kitzrettung", "Quadstick", false);
            var nums = new NumberStore(); nums.Learn("10.09.1991"); nums.Learn("10.09.1991");
            var vault = new List<VaultEntry> { new VaultEntry { Name = "Gmail", User = "morten", Password = "Geh3im!pw", Created = new DateTime(2026, 1, 1) } };

            var b = new Backup { Computer = "PC1" };
            b.Files[Backup.LearnFile("de")] = a.ToText();
            b.Files[Backup.NumbersFile] = nums.ToText();
            b.Files[Backup.CustomFile] = "# Kommentar\r\nMDM3D\r\nudaption\r\n";
            b.Files[Backup.MacroFile] = MacroStore.Serialize(MacroStore.Examples());
            b.Files[Backup.VaultFile] = Vault.Serialize(vault);

            bool threw = false;
            try { b.Write(""); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "Passwörter nie unverschlüsselt");

            string text = b.Write("sicher123");
            Check(!text.Contains("Geh3im") && !text.Contains("Quadstick") && text.StartsWith(Backup.Magic), "Datei verschlüsselt, nichts im Klartext");
            var h = Backup.ReadHeader(text);
            Check(h.Encrypted && h.Computer == "PC1", "Kopf: verschlüsselt, von PC1");
            Check(Backup.Open(h, "falsch") == null && Backup.Open(h, "") == null, "falsches Passwort wird erkannt");
            var r = Backup.Open(h, "sicher123");
            Check(r != null && r.LearnCodes.SequenceEqual(new[] { "de" }) && r.HasVault, "richtiges Passwort öffnet");
            var rv = Vault.Deserialize(r.Files[Backup.VaultFile]);
            Check(rv.Count == 1 && rv[0].Password == "Geh3im!pw" && rv[0].Name == "Gmail", "Passwort kommt heil an");
            Check(MacroStore.Parse(r.Files[Backup.MacroFile]).Count == MacroStore.Examples().Count, "Makros kommen heil an");

            var plain = new Backup();
            plain.Files[Backup.LearnFile("en")] = "w\thello\t3\n";
            var ph = Backup.ReadHeader(plain.Write(""));
            Check(!ph.Encrypted && Backup.Open(ph, null).LearnCodes.Contains("en"), "ohne Passwörter auch unverschlüsselt möglich");
            threw = false;
            try { Backup.ReadHeader("irgendwas\n"); } catch (InvalidDataException) { threw = true; }
            Check(threw, "fremde Datei wird abgelehnt");

            // Zusammenführen auf dem zweiten PC
            var pc2 = new LearningStore { Code = "de" };
            pc2.Learn("Assistenz", null, false);
            pc2.Learn("Quadstick", null, false);
            int added = pc2.MergeFrom(LearningStore.FromText(r.Files[Backup.LearnFile("de")], "de"));
            Check(added == 1 && pc2.WordCount == 3 && pc2.Get("Quadstick").Count == 2, "Wörter zusammengeführt, größerer Zähler gewinnt");
            Check(pc2.PairCountOf("quadstick", "kitzrettung") == 1, "Wortfolgen kommen mit");
            pc2.MergeFrom(LearningStore.FromText(r.Files[Backup.LearnFile("de")], "de"));
            Check(pc2.WordCount == 3 && pc2.Get("Quadstick").Count == 2, "zweimal importieren zählt nicht doppelt");

            var n2 = new NumberStore();
            Check(n2.MergeFrom(NumberStore.FromText(r.Files[Backup.NumbersFile])) == 1 && n2.Get("10.09.1991").Count == 2, "Zahlen zusammengeführt");

            int ca;
            string cw = Backup.MergeCustomWords("# eigene\r\nmdm3d\r\nFristingen\r\n", r.Files[Backup.CustomFile], out ca);
            Check(ca == 1 && cw.Contains("udaption") && cw.Contains("Fristingen") && cw.Split('\n').Count(l => l.Trim().Equals("MDM3D", StringComparison.OrdinalIgnoreCase)) == 1, "eigene Begriffe ohne Doppelte");

            int ma, mr;
            var local = new List<Macro> { new Macro { Name = "Gruß", Group = "" } };
            var merged = Backup.MergeMacros(local, MacroStore.Parse(r.Files[Backup.MacroFile]), out ma, out mr);
            Check(ma == MacroStore.Examples().Count && merged.Count == 1 + ma, "Makros dazu");
            Backup.MergeMacros(merged, MacroStore.Parse(r.Files[Backup.MacroFile]), out ma, out mr);
            Check(ma == 0 && mr == 0, "gleiche Makros nicht doppelt");

            var v2 = new List<VaultEntry>
            {
                new VaultEntry { Name = "gmail", User = "Morten", Password = "alt", Created = new DateTime(2025, 1, 1) },
                new VaultEntry { Name = "Bank", Password = "neuer", Created = new DateTime(2026, 1, 1), Changed = new DateTime(2026, 6, 1) },
            };
            int va, vu;
            Backup.MergeVault(v2, rv, out va, out vu);
            Check(va == 0 && vu == 1 && v2[0].Password == "Geh3im!pw", "neueres Passwort ersetzt das alte");
            Backup.MergeVault(v2, new List<VaultEntry> { new VaultEntry { Name = "Bank", Password = "uralt", Created = new DateTime(2024, 1, 1) } }, out va, out vu);
            Check(vu == 0 && v2[1].Password == "neuer", "älteres überschreibt nicht");
            Backup.MergeVault(v2, new List<VaultEntry> { new VaultEntry { Name = "Amazon", Password = "x" } }, out va, out vu);
            Check(va == 1 && v2.Count == 3, "neues Passwort kommt dazu");
            var ser = Vault.Deserialize(Vault.Serialize(v2));
            Check(ser[1].Changed == new DateTime(2026, 6, 1), "Änderungsdatum wird gespeichert");
            Check(Vault.Deserialize("a\tb\tc\td\t1\t2\n")[0].Changed == DateTime.MinValue, "alte Tresordateien gehen weiter");
            Check(Backup.DefaultFileName(new DateTime(2026, 11, 7)) == "NoHandsNeeded 2026-11-07.nohands", "Dateiname mit Datum");
        }
    }
}
