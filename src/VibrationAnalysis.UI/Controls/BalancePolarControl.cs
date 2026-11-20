using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using VibrationAnalysis.Core.Balancing;

namespace VibrationAnalysis.UI
{
    /// <summary>Diagrama polar de balanceo: lecturas (vectores de vibración) y correcciones (masas).</summary>
    public class BalancePolarControl : Control
    {
        public sealed class Item
        {
            public string Label { get; set; }
            public Phasor Value { get; set; }
            public Color Color { get; set; }
            public bool Dashed { get; set; }
        }

        private readonly List<Item> readings = new List<Item>();
        private readonly List<Item> masses = new List<Item>();
        public string Title { get; set; } = "";
        public string Unit { get; set; } = "mm/s";
        /// <summary>true: 0° arriba y ángulos en sentido antihorario (contrario al giro horario visto desde el lado de la marca).</summary>
        public bool CounterClockwise { get; set; } = true;
        public Color GridColor { get; set; } = Color.FromArgb(205, 214, 216);
        public Color PlotBackColor { get; set; } = Color.White;
        public Color TextColor { get; set; } = Color.FromArgb(29, 35, 39);

        public BalancePolarControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            MinimumSize = new Size(180, 180);
        }

        public void Clear() { readings.Clear(); masses.Clear(); Invalidate(); }
        public void AddReading(string label, Phasor p, Color c, bool dashed = false) { readings.Add(new Item { Label = label, Value = p, Color = c, Dashed = dashed }); Invalidate(); }
        public void AddMass(string label, Phasor p, Color c) { masses.Add(new Item { Label = label, Value = p, Color = c }); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(PlotBackColor);
            int legendH = 18 * Math.Max(1, readings.Count + masses.Count) + 24;
            var area = new Rectangle(8, 26, Width - 16, Height - 34 - legendH);
            int size = Math.Min(area.Width, area.Height);
            if (size < 60) return;
            var c = new PointF(area.X + area.Width / 2f, area.Y + area.Height / 2f);
            float r = size / 2f - 18;
            using var font = new Font("Segoe UI", 8.5f);
            using var bold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            using var text = new SolidBrush(TextColor);
            g.DrawString(Title, bold, text, 8, 4);

            double maxR = Math.Max(readings.Select(i => i.Value.Magnitude).DefaultIfEmpty(0).Max(), 1e-6);
            double maxM = Math.Max(masses.Select(i => i.Value.Magnitude).DefaultIfEmpty(0).Max(), 1e-6);
            double scaleR = NiceCeil(maxR);
            using (var grid = new Pen(GridColor, 1))
            {
                for (int k = 1; k <= 4; k++)
                {
                    float rr = r * k / 4;
                    g.DrawEllipse(grid, c.X - rr, c.Y - rr, 2 * rr, 2 * rr);
                    g.DrawString((scaleR * k / 4).ToString("0.##"), font, text, c.X + 2, c.Y - rr);
                }
                for (int a = 0; a < 360; a += 30)
                {
                    var p = Point(c, r, a);
                    g.DrawLine(grid, c, p);
                    var lp = Point(c, r + 10, a);
                    string s = a + "°";
                    var sz = g.MeasureString(s, font);
                    g.DrawString(s, font, text, lp.X - sz.Width / 2, lp.Y - sz.Height / 2);
                }
            }
            foreach (var it in readings) DrawVector(g, c, (float)(r * it.Value.Magnitude / scaleR), it, 2.6f);
            foreach (var it in masses) DrawVector(g, c, (float)(r * 0.85 * it.Value.Magnitude / maxM), it, 3.4f, mass: true);

            float ly = area.Bottom + 10;
            foreach (var it in readings.Concat(masses))
            {
                using var b = new SolidBrush(it.Color);
                g.FillRectangle(b, 10, ly + 4, 14, 8);
                string unit = masses.Contains(it) ? "g" : Unit;
                g.DrawString($"{it.Label}: {it.Value.Magnitude:0.##} {unit} ∠ {it.Value.AngleDeg:0}°", font, text, 30, ly);
                ly += 18;
            }
            if (masses.Count > 0) g.DrawString("Masas a escala propia", font, text, Width - 140, ly - 18);
        }

        private PointF Point(PointF c, float rr, double angleDeg)
        {
            // 0° arriba; antihorario: x = −sen, horario: x = +sen
            double a = angleDeg * Math.PI / 180;
            double sx = CounterClockwise ? -1 : 1;
            return new PointF((float)(c.X + sx * rr * Math.Sin(a)), (float)(c.Y - rr * Math.Cos(a)));
        }

        private void DrawVector(Graphics g, PointF c, float len, Item it, float width, bool mass = false)
        {
            var end = Point(c, len, it.Value.AngleDeg);
            using var pen = new Pen(it.Color, width) { CustomEndCap = new AdjustableArrowCap(4, 5) };
            if (it.Dashed) pen.DashStyle = DashStyle.Dash;
            g.DrawLine(pen, c, end);
            if (mass)
            {
                using var b = new SolidBrush(it.Color);
                g.FillEllipse(b, end.X - 6, end.Y - 6, 12, 12);
            }
        }

        private static double NiceCeil(double v)
        {
            double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
            foreach (var m in new[] { 1, 2, 2.5, 5, 10 }) if (m * p >= v) return m * p;
            return 10 * p;
        }
    }
}
