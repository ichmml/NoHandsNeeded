using System.Collections.Generic;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Tastatur
{
    // Nur zur Sichtprüfung unter Linux: Tastatur in Bilder rendern.
    internal static class RenderTest
    {
        static Predictor P;

        static void Shot(string file, int w, int h, Action<Settings> cfg, Action<KeyboardForm, KeyboardEngine> act)
        {
            var settings = new Settings { Width = w, Height = h, X = 0, Y = 0, };
            cfg(settings);
            var e = new KeyboardEngine(new NullOut(), P, settings);
            var f = new KeyboardForm(settings, e);
            f.MacroSource = () => MacroStore.Examples();
            f.LanguageLabel = () => "DE+EN";
            f.ClientSize = new Size(w, h);
            var L = KeyLayouts.CreateGerman();
            foreach (char c in "hallo") foreach (var row in L) foreach (var k in row) if (k.Kind == KeyKind.Char && k.Normal == c.ToString()) e.Press(k);
            act(f, e);
            using (var bmp = new Bitmap(w, h))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                bmp.Save(file);
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            P = new Predictor();
            using (var s = File.OpenRead(args[0])) P.Load(s);
            string dir = args[1];
            Shot(Path.Combine(dir, "a_normal.png"), 1536, 432, s => { }, (f, e) => { });
            Shot(Path.Combine(dir, "b_zweizeilig.png"), 1100, 440, s => { s.SuggestionRows = 2; s.SuggestionFontPercent = 50; }, (f, e) => { });
            Shot(Path.Combine(dir, "c_makros.png"), 1100, 440, s => { s.SuggestionRows = 2; s.SuggestionFontPercent = 45; }, (f, e) => f.OpenMacroBar());
            Shot(Path.Combine(dir, "d_hell.png"), 1100, 380, s => { s.Theme = "hell"; }, (f, e) => e.ToggleCase());
            var gold = Color.FromArgb(230, 160, 40);
            Shot(Path.Combine(dir, "f_tresor.png"), 1100, 440, s => { s.SuggestionRows = 2; s.SuggestionFontPercent = 45; }, (f, e) =>
            {
                f.VaultShown = () => true;
                f.OverrideBand = () => new System.Collections.Generic.List<BandItem>
                {
                    new BandItem { Label = "⚿ Gmail", Color = gold }, new BandItem { Label = "⚿ Sparkasse", Color = gold },
                    new BandItem { Label = "⚿ Amazon", Color = gold }, new BandItem { Label = "+ Neues Passwort" }, new BandItem { Label = "✕ Ausblenden" }
                };
                f.UpdateBand();
            });
            Shot(Path.Combine(dir, "g_angebot.png"), 1100, 380, s => { }, (f, e) =>
            {
                f.OverrideBand = () => new System.Collections.Generic.List<BandItem>
                {
                    new BandItem { Label = "Im Tresor speichern?", Dim = true }, new BandItem { Label = "⚿ Speichern", Bold = true, Color = gold }, new BandItem { Label = "Nein" }
                };
                f.UpdateBand();
            });
            Shot(Path.Combine(dir, "h_echt.png"), 1536, 432, s => { }, (f, e) =>
            {
                var pk = new PhysicalKeys();
                var now = DateTime.UtcNow;
                pk.Down(0xA2, now); pk.Down(0xA0, now); pk.Down(0x56, now.AddSeconds(-1)); pk.Up(0x56, now.AddMilliseconds(-1000));
                f.Physical = pk;
                f.ShowStatus("Strg+Shift");
            });
            foreach (var lc in new[] { "fr", "ru", "cs", "es", "tr", "da" })
            {
                string code = lc;
                Shot(Path.Combine(dir, "l_" + code + ".png"), 1100, 380, s => { }, (f, e) => f.SetLayout(code, true));
            }
            Func<KeyboardEngine, string, int> typeIn = (en, str) =>
            {
                var ks = KeyLayouts.CreateGerman().SelectMany(r => r).ToList();
                foreach (char c in str) en.Press(ks.First(k => k.Kind == KeyKind.Char && k.Normal == c.ToString()));
                return 0;
            };
            Shot(Path.Combine(dir, "i_neu.png"), 1536, 432, s => { }, (f, e) => { f.KnownWord = w => false; typeIn(e, "morten"); f.UpdateBand(); });
            Shot(Path.Combine(dir, "m_mail.png"), 1536, 432, s => { }, (f, e) => { f.EmailSuggest = c => c.Length >= 3 ? new List<string> { "ich.mml@gmail.com" } : new List<string>(); e.ResetWord(false); typeIn(e, "ich"); f.UpdateBand(); });
            Shot(Path.Combine(dir, "m_vault.png"), 1536, 432, s => { }, (f, e) => { var blue = Color.FromArgb(52, 120, 246); f.OverrideBand = () => new List<BandItem> { new BandItem { Label = "⚿ Passwörter" }, new BandItem { Label = "✉ E-Mail", Bold = true, Color = blue }, new BandItem { Label = "✉ ich.mml@gmail.com", Color = blue }, new BandItem { Label = "✉ info@mdm3d.de", Color = blue }, new BandItem { Label = "✕ Ausblenden" } }; e.ResetWord(false); f.UpdateBand(); });
            Shot(Path.Combine(dir, "l_long.png"), 1536, 432, s => { }, (f, e) => { e.ResetWord(false); typeIn(e, "donaudampfschifffahrtsgesellschaftskapitaen"); f.UpdateBand(); });
            Shot(Path.Combine(dir, "l_mid.png"), 1536, 432, s => { }, (f, e) => { e.ResetWord(false); typeIn(e, "barrierefreiheit"); f.UpdateBand(); });
            Shot(Path.Combine(dir, "n_field.png"), 1536, 432, s => { }, (f, e) => { f.EmailFieldMode = true; f.EmailSuggest = c => new List<string>(); f.EmailFieldSuggest = c => new List<string> { "ich.mml@gmail.com", "info@mdm3d.de" }; e.ResetWord(false); f.UpdateBand(); });
            Shot(Path.Combine(dir, "n_vault.png"), 1536, 432, s => { }, (f, e) => { var gd = Color.FromArgb(230, 160, 40); f.OverrideBand = () => new List<BandItem> { new BandItem { Label = "⚿ Gmail", Color = gd }, new BandItem { Label = "⚿ Amazon", Color = gd }, new BandItem { Label = "+ Passwort hinzufügen", Bold = true }, new BandItem { Label = "🎲 Erzeugen und eintippen" }, new BandItem { Label = "✕ Ausblenden" } }; e.ResetWord(false); f.UpdateBand(); });
            Shot(Path.Combine(dir, "i_chunk.png"), 1536, 432, s => { }, (f, e) => { f.KnownWord = w => true; typeIn(e, "s"); e.Shift = ModState.Once; e.Press(KeyLayouts.CreateGerman().SelectMany(r => r).First(k => k.Shifted == "!")); typeIn(e, "add"); f.UpdateBand(); });
            Shot(Path.Combine(dir, "i_tresorklar.png"), 1536, 432, s => { }, (f, e) => { e.PasswordMode = true; f.PlainSecret = true; typeIn(e, "geheim7"); f.UpdateBand(); });
            Shot(Path.Combine(dir, "i_lang2.png"), 1536, 432, s => { s.SuggestionRows = 2; }, (f, e) => { f.KnownWord = w => true; typeIn(e, "donaudampfschifffahrtsgesellschaft"); f.UpdateBand(); });
            Shot(Path.Combine(dir, "i_pw.png"), 1536, 432, s => { }, (f, e) => { e.PasswordMode = true; typeIn(e, "geheim7"); f.OverrideBand = () => new System.Collections.Generic.List<BandItem> { new BandItem { Label = "⚿ Gmail", Color = Color.FromArgb(230,160,40) }, new BandItem { Label = "+ Neues Passwort" } }; f.UpdateBand(); });
            Shot(Path.Combine(dir, "i_pwklar.png"), 1536, 432, s => { }, (f, e) => { e.PasswordMode = true; typeIn(e, "geheim7"); f.ToggleReveal(); f.UpdateBand(); });
            Shot(Path.Combine(dir, "e_aufnahme.png"), 1100, 380, s => { }, (f, e) => { f.Recording = true; f.ShowStatus("Aufnahme läuft: einfach tippen, dann ■ Stopp"); });
        }
    }

    internal class NullOut : IOutput
    {
        public void SendText(string t) { }
        public void SendVk(ushort vk, Modifiers m) { }
        public bool SendCharWithModifiers(char c, Modifiers m) { return true; }
        public void SendBackspaces(int n) { }
    }
}
