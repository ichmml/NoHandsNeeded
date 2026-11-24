using System;
using System.Drawing;

namespace Tastatur
{
    /// <summary>1.2.03: Verweil-Ring darf mit kaputten Werten nicht abstürzen.</summary>
    internal static class V1203Tests
    {
        public static void Run(Action<bool, string> Check)
        {
            Console.WriteLine("\n--- 1.2.03: Sicherer Verweil-Ring ---");
            float s;
            var ok = new RectangleF(10, 10, 40, 40);
            Check(SafeArc.Prepare(ok, 180, out s) && s == 180, "normaler Bogen wird gezeichnet");
            Check(SafeArc.Prepare(ok, 720, out s) && s == 360, "zu großer Winkel auf 360 begrenzt");
            Check(!SafeArc.Prepare(ok, 0.2, out s), "winziger Bogen wird übersprungen (GDI+-Absturz)");
            Check(!SafeArc.Prepare(ok, double.NaN, out s), "NaN-Winkel wird übersprungen");
            Check(!SafeArc.Prepare(ok, double.PositiveInfinity, out s), "unendlicher Winkel wird übersprungen");
            Check(!SafeArc.Prepare(new RectangleF(0, 0, 0, 0), 180, out s), "leeres Rechteck wird übersprungen");
            Check(!SafeArc.Prepare(new RectangleF(0, 0, -5, 20), 180, out s), "negatives Rechteck wird übersprungen");
            Check(!SafeArc.Prepare(new RectangleF(float.NaN, 0, 20, 20), 180, out s), "Rechteck mit NaN wird übersprungen");

            Check(SafeArc.Fraction(double.NaN, 500) == 0 && SafeArc.Fraction(100, double.NaN) == 0, "Anteil: NaN wird 0");
            Check(SafeArc.Fraction(-1, 500) == 0 && SafeArc.Fraction(1000, 500) == 1, "Anteil bleibt zwischen 0 und 1");
            Check(SafeArc.Fraction(10, 0) == 1 && SafeArc.Fraction(10, -300) == 1, "Gesamtzeit 0 oder negativ teilt nicht durch 0");
            Check(Math.Abs(SafeArc.Fraction(250, 500) - 0.5) < 1e-9, "Anteil normal berechnet");

            // Echtes Zeichnen mit allen bösen Werten darf nicht werfen
            bool threw = false;
            try
            {
                using (var bmp = new Bitmap(60, 60))
                using (var g = Graphics.FromImage(bmp))
                using (var p = new Pen(Color.Red, 8f))
                {
                    foreach (var frac in new[] { double.NaN, -1, 0, 1e-9, 0.001, 0.5, 1, 2, double.PositiveInfinity })
                        foreach (var r in new[] { ok, new RectangleF(5, 5, 0, 0), new RectangleF(5, 5, -3, 10), new RectangleF(5, 5, 0.5f, 0.5f) })
                            SafeArc.Draw(g, p, r, -90, 360 * SafeArc.Fraction(frac, 1));
                }
            }
            catch (Exception ex) { threw = true; Console.WriteLine("  " + ex); }
            Check(!threw, "Bogen zeichnen mit NaN, -1, 0, winzig, 2 und leeren Rechtecken wirft nicht");
        }
    }
}
