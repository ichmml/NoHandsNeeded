using System.Drawing;

namespace Tastatur
{
    internal class Theme
    {
        public Color Background, Key, KeySpecial, KeyHover, Text, TextDim, Accent, Locked, Suggestion, Border;

        public static Theme Get(string name)
        {
            if (name == "hell") // i18n-ok
                return new Theme
                {
                    Background = Color.FromArgb(214, 218, 224),
                    Key = Color.FromArgb(255, 255, 255),
                    KeySpecial = Color.FromArgb(236, 238, 242),
                    KeyHover = Color.FromArgb(204, 226, 255),
                    Text = Color.FromArgb(20, 20, 24),
                    TextDim = Color.FromArgb(110, 114, 122),
                    Accent = Color.FromArgb(0, 110, 230),
                    Locked = Color.FromArgb(220, 110, 0),
                    Suggestion = Color.FromArgb(246, 248, 250),
                    Border = Color.FromArgb(180, 186, 196)
                };
            return new Theme
            {
                Background = Color.FromArgb(28, 29, 34),
                Key = Color.FromArgb(54, 57, 66),
                KeySpecial = Color.FromArgb(40, 42, 50),
                KeyHover = Color.FromArgb(78, 84, 98),
                Text = Color.FromArgb(245, 246, 248),
                TextDim = Color.FromArgb(150, 155, 165),
                Accent = Color.FromArgb(76, 154, 255),
                Locked = Color.FromArgb(255, 159, 67),
                Suggestion = Color.FromArgb(46, 49, 58),
                Border = Color.FromArgb(20, 21, 25)
            };
        }
    }
}
