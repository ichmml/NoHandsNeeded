using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Tastatur
{
    /// <summary>1.2.02: Einstellungen in der Sicherung, Sicherung vor dem Import, Alltagswörter und -wortfolgen.</summary>
    internal static class V1202Tests
    {
        public static void Run(string dePath, Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.02: Einstellungen sichern und Alltagssprache ---");

            // Einstellungen als Text und zurück
            var a = new Settings { AutoFadePercent = 55, Theme = "hell", DwellMs = 1234, HotkeyShow = "NumPlus", ActiveLanguages = "de,fr", WidthPercent = 44, X = 50000, Y = 50000, BackupFolder = @"C:\Fremd" };   // i18n-ok
            var b = Settings.FromText(a.ToText());
            Check(b.AutoFadePercent == 55 && b.Theme == "hell" && b.DwellMs == 1234 && b.HotkeyShow == "NumPlus" && b.ActiveLanguages == "de,fr" && Math.Abs(b.WidthPercent - 44) < 0.01, "Einstellungen: Text hin und zurück");   // i18n-ok
            Check(b.FileVersion == Settings.CurrentVersion, "Einstellungen aus der Sicherung: Version aktuell");
            var old = Settings.FromText("einstellungen_version = 5\nfenster_breite_prozent = 50\n");
            Check(Math.Abs(old.WidthPercent - 50) < 0.01 && old.DwellMs == new Settings().DwellMs, "alte Sicherung: fehlende Werte bekommen den Standard");
            var junk = Settings.FromText("Unsinn\n= =\nverweilzeit_ms = abc\n");
            Check(junk.DwellMs == new Settings().DwellMs, "kaputte Zeilen stören nicht");

            // Übernehmen: rechnerbezogene Werte bleiben
            var local = new Settings { BackupFolder = @"D:\Meine", Theme = "dunkel" };   // i18n-ok
            local.CopyFrom(b);
            Check(local.Theme == "hell" && local.DwellMs == 1234 && local.HotkeyShow == "NumPlus", "Import: Einstellungen übernommen");   // i18n-ok
            Check(local.BackupFolder == @"D:\Meine", "Import: Sicherungsordner dieses PCs bleibt");

            // Position außerhalb der Bildschirme: Hauptbildschirm
            var areas = new[] { new System.Drawing.Rectangle(0, 0, 1920, 1040) };
            var r = Geometry.Restore(local.X, local.Y, local.WidthPercent, local.HeightPercent, local.DockPosition, areas, 0, new System.Drawing.Size(480, 180));
            Check(areas[0].Contains(r), "Position vom anderen PC außerhalb: Tastatur landet auf dem Hauptbildschirm");

            // Sicherung mit Einstellungen
            var bk = new Backup();
            bk.Files[Backup.SettingsFile] = a.ToText();
            var h = Backup.ReadHeader(bk.Write(null));
            var opened = Backup.Open(h, null);
            Check(opened.HasSettings && Settings.FromText(opened.Files[Backup.SettingsFile]).DwellMs == 1234, "Sicherung enthält die Einstellungen");
            Check(!opened.LearnCodes.Any(), "Einstellungsdatei zählt nicht als Sprache");

            // automatische Sicherungen: nur die letzten drei
            string tmp = Path.Combine(Path.GetTempPath(), "nhn-1202-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(tmp, Backup.SafetyFileName(new DateTime(2026, 11, 20 + i, 10, 0, 0))), "x");
            File.WriteAllText(Path.Combine(tmp, "eigene" + Backup.Extension), "x");
            Backup.PruneSafety(tmp, Backup.SafetyKeep);
            var left = Directory.GetFiles(tmp).Select(Path.GetFileName).OrderBy(x => x).ToList();
            Check(left.Count == 4 && left.Contains(Backup.SafetyFileName(new DateTime(2026, 11, 24, 10, 0, 0))) && !left.Contains(Backup.SafetyFileName(new DateTime(2026, 11, 20, 10, 0, 0))),
                "vor dem Import: die neuesten 3 Sicherungen bleiben, eigene Dateien bleiben unangetastet");
            try { Directory.Delete(tmp, true); } catch { }

            // Alltagswörter und Wortfolgen in der deutschen Liste
            var p = new Predictor();
            using (var s = File.OpenRead(dePath)) p.Load(s);
            Scored sc;
            Check(p.TryGet("gehts", out sc) && p.TryGet("naja", out sc) && p.TryGet("Feierabend", out sc), "Alltagswörter in der Wortliste (gehts, naja, Feierabend)");
            Check(!p.TryGet("Morten", out sc), "keine persönlichen Namen dazugekommen");
            string gz = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dePath)), "wortfolgen_de.txt.gz");
            if (File.Exists(gz))
            {
                var bg = new Bigrams("de");
                using (var f = File.OpenRead(gz))
                using (var z = new GZipStream(f, CompressionMode.Decompress)) bg.Load(z);
                Check(bg.Of("wie", "gehts") > 0 && bg.Of("viel", "Spaß") > 0 && bg.Of("hab", "ich") > 0, "Alltags-Wortfolgen: wie gehts, viel Spaß, hab ich");
                Check(bg.NextOf("ich").Length <= 6, "höchstens 6 Folgewörter pro Wort");
            }
        }
    }
}
