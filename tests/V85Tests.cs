using System;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Tastatur
{
    internal static class V85Tests
    {
        public static void Run(Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 8.5: Dialoge neben der Tastatur ---");
            var wa = new Rectangle(0, 0, 2048, 1112);           // 2560x1440 bei 125 %
            var kb = new Rectangle(800, 760, 1229, 345);        // unten rechts
            var dlg = new Rectangle(644, 256, 760, 600);        // mittig, ragt in die Tastatur
            var r = Geometry.AvoidKeyboard(dlg, kb, wa);
            Check(!r.IntersectsWith(kb) && wa.Contains(r), "Dialog überlappt die Tastatur nicht mehr: " + r);
            Check(r.Size == dlg.Size, "Größe bleibt, wenn oben Platz ist");
            var small = new Rectangle(0, 0, 1536, 824);         // 1920x1080 bei 100 %, Taskleiste
            var kb2 = new Rectangle(0, 300, 1536, 524);          // sehr große Tastatur
            var r2 = Geometry.AvoidKeyboard(new Rectangle(388, 112, 760, 600), kb2, small);
            Check(!r2.IntersectsWith(kb2) && r2.Height == 300, "zu wenig Platz: Dialog wird niedriger und scrollt: " + r2);
            var free = new Rectangle(10, 10, 300, 200);
            Check(Geometry.AvoidKeyboard(free, kb, wa) == free, "Dialog ohne Überlappung bleibt, wo er ist");
            Check(Geometry.AvoidKeyboard(dlg, Rectangle.Empty, wa) == dlg, "Tastatur eingeklappt: nichts verschieben");

            Console.WriteLine("\n--- 8.5: Untere Reihe ---");
            KeyLayouts.BottomChars = KeyLayouts.DefaultBottomChars;
            foreach (var code in KeyLayouts.Codes)
            {
                var bottom = KeyLayouts.Create(code).Last();
                int sp = bottom.FindIndex(k => k.Kind == KeyKind.Space);
                Check(sp >= 0 && bottom[sp + 1].Kind == KeyKind.AltGr && Math.Abs(bottom.Sum(k => k.Width) - 15f) < 0.01f, code + ": AltGr direkt rechts neben der Leertaste, 15 Einheiten");
            }
            var nav = KeyLayouts.CreateNav();
            Check(nav.All(row => Math.Abs(row.Sum(k => k.Width) - 15f) < 0.01f), "Fn-Ebene: 15 Einheiten");
            var de = KeyLayouts.Create("de").Last();
            var chars = de.Where(k => k.Kind == KeyKind.Char).Select(k => k.Normal).ToList();
            Check(string.Join("", chars) == "!?:-", "Standard unten: ! ? : - (" + string.Join("", chars) + ")");
            var order = de.Select(k => k.Kind == KeyKind.Char ? k.Normal : k.Label).ToList();
            Check(order.IndexOf("AltGr") < order.IndexOf("!") && order.IndexOf("!") < order.IndexOf("←"), "Reihenfolge: Leertaste, AltGr, …, ! ? : -, Pfeile");

            Console.WriteLine("\n--- 8.5: Sonderzeichen einstellbar ---");
            Check(KeyLayouts.NormalizeBottomChars("") == "! ? : -", "leer = Standard");
            Check(KeyLayouts.NormalizeBottomChars(".: ,; @ €") == ".: ,; @ €", "eigene Zeichen, mit Shift-Zeichen");
            Check(KeyLayouts.NormalizeBottomChars("abc \u0001 x") == "! ? x -", "ungültig oder fehlend = Standard an dieser Stelle");
            KeyLayouts.BottomChars = ".: ,; @ €";
            var b2 = KeyLayouts.Create("de").Last().Where(k => k.Kind == KeyKind.Char).ToList();
            Check(b2[0].Normal == "." && b2[0].Shifted == ":" && b2[2].Normal == "@" && b2[2].Shifted == "@" && b2[3].Normal == "€", "Tasten übernehmen die Einstellung");
            KeyLayouts.BottomChars = KeyLayouts.DefaultBottomChars;
            string tmp = Path.Combine(Path.GetTempPath(), "tastatur-v85-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            string f = Path.Combine(tmp, "einst.txt");
            var st = new Settings { BottomChars = "# ? : -" };
            st.SaveTo(f);
            Check(Settings.LoadFrom(f).BottomChars == "# ? : -", "wird in einst.txt gespeichert");
            File.WriteAllText(f, "einstellungen_version = 5\ntasten_statuszeile = ja\n");
            Check(Settings.LoadFrom(f).BottomChars == "! ? : -", "ältere Version bekommt die neuen Standardzeichen");
            Check(Settings.LoadFrom(f).TaskbarButton, "Taskleisten-Knopf ist von Haus aus an");
            var tb = new Settings { TaskbarButton = false }; tb.SaveTo(f);
            Check(!Settings.LoadFrom(f).TaskbarButton, "Taskleisten-Knopf abschaltbar und gespeichert");
            var gate = new ClickGate();
            var t0 = new DateTime(2026, 11, 8, 18, 0, 0);
            Check(gate.Accept(t0) && !gate.Accept(t0.AddMilliseconds(30)) && !gate.Accept(t0.AddMilliseconds(350)), "Taskleisten-Klick (doppelt gemeldet) zählt nur einmal");
            Check(gate.Accept(t0.AddMilliseconds(900)), "nächster Klick danach zählt wieder");

            Console.WriteLine("\n--- 8.5: Tresor speichern, auch mit PIN ---");
            try
            {
                string vp = Path.Combine(tmp, "tresor.dat");
                var v = new Vault(vp);
                v.Load();
                v.SetPin("1234");
                v.Entries.Add(new VaultEntry { Name = "Gmail", Password = Vault.Generate(16, true), Created = DateTime.Now });
                v.Save();
                string pw = v.Entries[0].Password;
                var v2 = new Vault(vp);
                v2.Load();
                Check(v2.Locked && !v2.Unlock("0000"), "mit PIN gesperrt, falsche PIN geht nicht");
                Check(v2.Unlock("1234") && v2.Entries.Count == 1 && v2.Entries[0].Password == pw, "nach PIN ist der Eintrag gespeichert");
                v2.Entries.Add(new VaultEntry { Name = "Bank", Password = "x9!kfL2p" });
                v2.Save();
                var v3 = new Vault(vp); v3.Load(); v3.Unlock("1234");
                Check(v3.Entries.Count == 2 && v3.ContainsPassword("x9!kfL2p"), "zweiter Eintrag nach Entsperren gespeichert");
            }
            catch (Exception ex) { Check(false, "Tresor-Test: " + ex.Message); }
            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
