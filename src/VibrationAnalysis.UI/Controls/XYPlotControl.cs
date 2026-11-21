using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace VibrationAnalysis.UI
{
    /// <summary>
    /// Gráfico X-Y genérico para el análisis avanzado: ejes con rango libre (no necesariamente desde 0), escala Y lineal o
    /// logarítmica (dB), marcadores verticales rotulados, rótulos de picos y cursor con lectura.
    /// Rueda del mouse: zoom en X alrededor del cursor. Arrastrar con botón izquierdo: desplazar. Doble clic: ver todo.
    /// </summary>
    public sealed class XYPlotControl : Control
    {
        public sealed record Marker(double X, string Label, Color Color);
        public sealed record PeakLabel(double X, double Y, string Text);

        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");
        private double[] xs = Array.Empty<double>(), ys = Array.Empty<double>();
        private double viewMin, viewMax;
        private Point? mouse;
        private Point? dragFrom;
        private double dragMin, dragMax;

        public string Title { get; set; } = "";
        public string XLabel { get; set; } = "Frecuencia (Hz)";
        public string YLabel { get; set; } = "Amplitud";
        public string XFormat { get; set; } = "0.##";
        /// <summary>Escala Y logarítmica (se muestra en dB respecto de <see cref="DbReference"/>).</summary>
        public bool LogY { get; set; }
        public double DbReference { get; set; } = 1.0;
        public bool Stems { get; set; }
        public List<Marker> Markers { get; } = new List<Marker>();
        public List<PeakLabel> Peaks { get; } = new List<PeakLabel>();
        public Color PlotBackColor { get; set; } = Color.White;
        public Color GridColor { get; set; } = Color.FromArgb(215, 222, 224);
        public Color LineColor { get; set; } = Color.FromArgb(14, 124, 134);
        public Color TextColor { get; set; } = Color.FromArgb(29, 35, 39);

        public XYPlotControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Font = new Font("Segoe UI", 8.5f);
        }

        public double[] XValues => xs;
        public double[] YValues => ys;

        public void SetData(double[] x, double[] y, bool resetView = true)
        {
            if (x == null || y == null || x.Length != y.Length) throw new ArgumentException("X e Y deben tener el mismo largo.");
            xs = x; ys = y;
            if (resetView || viewMax <= viewMin) ResetView();
            Invalidate();
        }

        public void ResetView()
        {
            if (xs.Length == 0) { viewMin = 0; viewMax = 1; return; }
            viewMin = xs[0]; viewMax = xs[xs.Length - 1];
            if (viewMax <= viewMin) viewMax = viewMin + 1;
            Invalidate();
        }

        public void SetView(double min, double max)
        {
            if (max <= min) return;
            viewMin = min; viewMax = max;
            Invalidate();
        }

        private Rectangle PlotArea => new Rectangle(64, string.IsNullOrEmpty(Title) ? 12 : 30, Math.Max(10, Width - 64 - 16), Math.Max(10, Height - (string.IsNullOrEmpty(Title) ? 12 : 30) - 44));

        private double Transform(double y) => LogY ? 20 * Math.Log10(Math.Max(Math.Abs(y), DbReference * 1e-9) / DbReference) : y;

        private (double Min, double Max) YRange()
        {
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = 0; i < xs.Length; i++)
            {
                if (xs[i] < viewMin || xs[i] > viewMax) continue;
                double v = Transform(ys[i]);
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                lo = Math.Min(lo, v); hi = Math.Max(hi, v);
            }
            if (lo > hi) return (0, 1);
            if (LogY) { lo = Math.Max(lo, hi - 100); return (Math.Floor(lo / 10) * 10, Math.Ceiling(hi / 10) * 10 + 5); }
            if (lo > 0) lo = 0;
            double span = hi - lo;
            if (span <= 0) span = Math.Abs(hi) > 0 ? Math.Abs(hi) : 1;
            return (lo, hi + span * 0.08);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(PlotBackColor);
            var r = PlotArea;
            using var text = new SolidBrush(TextColor);
            using var grid = new Pen(GridColor);
            using var axis = new Pen(TextColor);
            if (!string.IsNullOrEmpty(Title))
                using (var bold = new Font(Font, FontStyle.Bold)) g.DrawString(Title, bold, text, r.Left, 8);
            var (yMin, yMax) = YRange();
            float X(double v) => (float)(r.Left + (v - viewMin) / (viewMax - viewMin) * r.Width);
            float Y(double v) => (float)(r.Bottom - (v - yMin) / (yMax - yMin) * r.Height);

            // Grilla y rótulos
            foreach (var v in Ticks(viewMin, viewMax, Math.Max(2, r.Width / 90)))
            {
                float px = X(v);
                g.DrawLine(grid, px, r.Top, px, r.Bottom);
                var s = v.ToString(XFormat, Es);
                var sz = g.MeasureString(s, Font);
                g.DrawString(s, Font, text, px - sz.Width / 2, r.Bottom + 3);
            }
            foreach (var v in Ticks(yMin, yMax, Math.Max(2, r.Height / 50)))
            {
                float py = Y(v);
                g.DrawLine(grid, r.Left, py, r.Right, py);
                var s = LogY ? v.ToString("0", Es) : Fmt(v);
                var sz = g.MeasureString(s, Font);
                g.DrawString(s, Font, text, r.Left - sz.Width - 3, py - sz.Height / 2);
            }
            g.DrawRectangle(axis, r);
            var xl = g.MeasureString(XLabel, Font);
            g.DrawString(XLabel, Font, text, r.Left + r.Width / 2 - xl.Width / 2, r.Bottom + 20);
            var yLabel = LogY ? $"{YLabel} (dB ref {Fmt(DbReference)})" : YLabel;
            var state = g.Save();
            g.TranslateTransform(12, r.Top + r.Height / 2);
            g.RotateTransform(-90);
            var yl = g.MeasureString(yLabel, Font);
            g.DrawString(yLabel, Font, text, -yl.Width / 2, -yl.Height / 2);
            g.Restore(state);

            g.SetClip(r);
            // Marcadores
            foreach (var m in Markers)
            {
                if (m.X < viewMin || m.X > viewMax) continue;
                using var pen = new Pen(Color.FromArgb(170, m.Color)) { DashStyle = DashStyle.Dash };
                float px = X(m.X);
                g.DrawLine(pen, px, r.Top, px, r.Bottom);
                using var b = new SolidBrush(m.Color);
                g.DrawString(m.Label, Font, b, px + 2, r.Top + 2);
            }
            // Curva
            if (xs.Length > 1)
            {
                using var pen = new Pen(LineColor, 1.4f);
                var pts = new List<PointF>(Math.Min(xs.Length, r.Width * 2 + 2));
                // Reducción por píxel (mín/máx) para señales largas
                int i0 = Math.Max(0, LowerIndex(viewMin) - 1), i1 = Math.Min(xs.Length - 1, LowerIndex(viewMax) + 1);
                int count = i1 - i0 + 1;
                if (Stems)
                {
                    float y0 = Y(Math.Max(yMin, LogY ? yMin : 0));
                    for (int i = i0; i <= i1; i++) g.DrawLine(pen, X(xs[i]), y0, X(xs[i]), Y(Transform(ys[i])));
                }
                else if (count > r.Width * 2)
                {
                    int bucket = (int)Math.Ceiling(count / (double)r.Width);
                    for (int s = i0; s <= i1; s += bucket)
                    {
                        double lo = double.MaxValue, hi = double.MinValue;
                        for (int i = s; i < Math.Min(s + bucket, i1 + 1); i++) { double v = Transform(ys[i]); lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
                        float px = X(xs[s]);
                        pts.Add(new PointF(px, Y(lo))); pts.Add(new PointF(px, Y(hi)));
                    }
                    if (pts.Count > 1) g.DrawLines(pen, pts.ToArray());
                }
                else
                {
                    for (int i = i0; i <= i1; i++) pts.Add(new PointF(X(xs[i]), Y(Transform(ys[i]))));
                    if (pts.Count > 1) g.DrawLines(pen, pts.ToArray());
                }
            }
            // Rótulos de picos
            using (var pb = new SolidBrush(Color.FromArgb(200, 70, 20)))
                foreach (var p in Peaks)
                {
                    if (p.X < viewMin || p.X > viewMax) continue;
                    float px = X(p.X), py = Y(Transform(p.Y));
                    g.FillEllipse(pb, px - 3, py - 3, 6, 6);
                    if (!string.IsNullOrEmpty(p.Text)) g.DrawString(p.Text, Font, pb, px + 4, Math.Max(r.Top, py - 16));
                }
            // Cursor
            if (mouse is Point mp && r.Contains(mp) && xs.Length > 0)
            {
                double xv = viewMin + (mp.X - r.Left) / (double)r.Width * (viewMax - viewMin);
                int i = Math.Clamp(LowerIndex(xv), 0, xs.Length - 1);
                if (i + 1 < xs.Length && Math.Abs(xs[i + 1] - xv) < Math.Abs(xs[i] - xv)) i++;
                float px = X(xs[i]);
                using var cp = new Pen(Color.FromArgb(120, TextColor)) { DashStyle = DashStyle.Dot };
                g.DrawLine(cp, px, r.Top, px, r.Bottom);
                string s = $"{xs[i].ToString(XFormat, Es)} · {(LogY ? Transform(ys[i]).ToString("0.0", Es) + " dB" : Fmt(ys[i]))}";
                var sz = g.MeasureString(s, Font);
                float bx = Math.Min(px + 6, r.Right - sz.Width - 2);
                using var bg = new SolidBrush(Color.FromArgb(225, PlotBackColor));
                g.FillRectangle(bg, bx, r.Top + 18, sz.Width, sz.Height);
                g.DrawString(s, Font, text, bx, r.Top + 18);
            }
            g.ResetClip();
        }

        private int LowerIndex(double x)
        {
            int lo = 0, hi = xs.Length - 1;
            if (hi < 0) return 0;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (xs[mid] <= x) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        private static string Fmt(double v)
        {
            double a = Math.Abs(v);
            if (a == 0) return "0";
            if (a >= 1000 || a < 0.001) return v.ToString("0.##E+0", Es);
            return v.ToString(a >= 100 ? "0" : a >= 10 ? "0.#" : a >= 1 ? "0.##" : "0.####", Es);
        }

        private static IEnumerable<double> Ticks(double min, double max, int target)
        {
            double span = max - min;
            if (span <= 0 || double.IsNaN(span) || double.IsInfinity(span)) yield break;
            double raw = span / target, mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double step = new[] { 1, 2, 2.5, 5, 10 }.Select(k => k * mag).First(s => s >= raw);
            for (double v = Math.Ceiling(min / step) * step; v <= max + step * 1e-9; v += step) yield return Math.Abs(v) < step * 1e-9 ? 0 : v;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            mouse = e.Location;
            if (dragFrom is Point d)
            {
                double dx = (e.X - d.X) / (double)PlotArea.Width * (dragMax - dragMin);
                viewMin = dragMin - dx; viewMax = dragMax - dx;
            }
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); mouse = null; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left) { dragFrom = e.Location; dragMin = viewMin; dragMax = viewMax; }
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragFrom = null; }

        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); ResetView(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            var r = PlotArea;
            if (!r.Contains(e.Location) || xs.Length < 2) return;
            double xc = viewMin + (e.X - r.Left) / (double)r.Width * (viewMax - viewMin);
            double k = e.Delta > 0 ? 0.8 : 1.25;
            double min = xc - (xc - viewMin) * k, max = xc + (viewMax - xc) * k;
            double minSpan = (xs[xs.Length - 1] - xs[0]) / Math.Max(2, xs.Length) * 8;
            if (max - min < minSpan) return;
            viewMin = Math.Max(xs[0], min); viewMax = Math.Min(xs[xs.Length - 1], max);
            Invalidate();
        }

        public void ExportCsv(string path)
        {
            var sb = new StringBuilder();
            sb.Append(XLabel).Append(';').AppendLine(YLabel);
            for (int i = 0; i < xs.Length; i++)
                sb.Append(xs[i].ToString("R", CultureInfo.InvariantCulture)).Append(';').AppendLine(ys[i].ToString("R", CultureInfo.InvariantCulture));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public void ExportPng(string path)
        {
            using var bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
