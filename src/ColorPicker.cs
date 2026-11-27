using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tastatur
{
    /// <summary>
    /// Farbwahl mit großen Feldern zum Anklicken (gut mit Quadstick und Verweilklick).
    /// "Weitere Farben" öffnet den normalen Windows-Farbdialog.
    /// </summary>
    internal class ColorPickerDialog : Form
    {
        public Color Result { get; private set; }
        private const int Cols = 6, Cell = 76;

        public ColorPickerDialog(string what, Color current)
        {
            DpiInfo.Attach(this);
            Result = current;
            Text = L.T("Farbe wählen") + ": " + what;
            Font = new Font("Segoe UI", 12f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            TopMost = true;
            ShowInTaskbar = false;

            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(12) };
            Controls.Add(panel);

            var grid = new TableLayoutPanel { ColumnCount = Cols, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
            for (int i = 0; i < Theme.Palette.Length; i++)
            {
                var c = Theme.Palette[i];
                var b = new Button
                {
                    Width = Cell, Height = Cell - 12, BackColor = c, FlatStyle = FlatStyle.Flat, Margin = new Padding(4), Text = "",
                    AccessibleName = Theme.ToHex(c)
                };
                b.FlatAppearance.BorderSize = ColorsEqual(c, current) ? 5 : 1;
                b.FlatAppearance.BorderColor = ColorsEqual(c, current) ? Color.FromArgb(255, 170, 0) : Color.Gray;
                b.Click += (o, e) => { Result = c; DialogResult = DialogResult.OK; Close(); };
                grid.Controls.Add(b, i % Cols, i / Cols);
            }
            panel.Controls.Add(grid);

            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            var more = new Button { Text = L.T("Weitere Farben …"), AutoSize = true, MinimumSize = new Size(230, 52), Margin = new Padding(4) };
            more.Click += (o, e) =>
            {
                using (var d = new ColorDialog { Color = Result, FullOpen = true, AnyColor = true })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    Result = Color.FromArgb(d.Color.R, d.Color.G, d.Color.B);
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };
            var cancel = new Button { Text = L.T("Abbrechen"), AutoSize = true, MinimumSize = new Size(180, 52), Margin = new Padding(4), DialogResult = DialogResult.Cancel };
            row.Controls.Add(more);
            row.Controls.Add(cancel);
            panel.Controls.Add(row);
            CancelButton = cancel;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        private static bool ColorsEqual(Color a, Color b) { return a.R == b.R && a.G == b.G && a.B == b.B; }

        public static bool Pick(IWin32Window owner, string what, Color current, out Color result)
        {
            using (var d = new ColorPickerDialog(what, current))
            {
                bool ok = d.ShowDialog(owner) == DialogResult.OK;
                result = d.Result;
                return ok;
            }
        }
    }
}
