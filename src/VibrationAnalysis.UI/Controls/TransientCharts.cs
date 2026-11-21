using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace VibrationAnalysis.UI
{
    /// <summary>Base común: colores de tema, título, exportación a PNG.</summary>
    public abstract class ChartBase : Control
    {
        protected static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");
        public string Title { get; set; } = "";
        public Color PlotBackColor { get; set; } = Color.White;
        public Color GridColor { get; set; } = Color.FromArgb(215, 222, 224);
        public Color LineColor { get; set; } = Color.FromArgb(14, 124, 134);
        public Color TextColor { get; set; } = Color.FromArgb(29, 35, 39);

        protected ChartBase()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Font = new Font("Segoe UI", 8.5f);
        }

        protected void DrawTitle(Graphics g)
        {
            if (string.IsNullOrEmpty(Title)) return;
            using var b = new SolidBrush(TextColor);
            using var f = new Font(Font, FontStyle.Bold);
            g.DrawString(Title, f, b, 8, 6);
        }

        protected static string Fmt(double v)
        {
            double a = Math.Abs(v);
            if (a == 0) return "0";
            if (a >= 10000 || a < 0.001) return v.ToString("0.##E+0", Es);
            return v.ToString(a >= 100 ? "0" : a >= 10 ? "0.#" : a >= 1 ? "0.##" : "0.####", Es);
        }

        protected static IEnumerable<double> Ticks(double min, double max, int target)
        {
            double span = max - min;
            if (span <= 0 || double.IsNaN(span) || double.IsInfinity(span)) yield break;
            double raw = span / Math.Max(1, target), mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double step = new[] { 1, 2, 2.5, 5, 10 }.Select(k => k * mag).First(s => s >= raw);
            for (double v = Math.Ceiling(min / step) * step; v <= max + step * 1e-9; v += step) yield return Math.Abs(v) < step * 1e-9 ? 0 : v;
        }

        public void ExportPng(string path)
        {
            using var bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            bmp.Save(path, ImageFormat.Png);
        }
    }

    /// <summary>
    /// Trayectoria X-Y con escala igual en ambos ejes: diagrama de Nyquist (grilla polar, 0° arriba y sentido horario)
    /// u órbita del eje (grilla cartesiana, marcas del tacómetro).
    /// </summary>
    public sealed class PathPlotControl : ChartBase
    {
        public sealed record PointLabel(int Index, string Text);
        private double[] xs = Array.Empty<double>(), ys = Array.Empty<double>();
        public bool PolarGrid { get; set; }
        public string XLabel { get; set; } = "X";
        public string YLabel { get; set; } = "Y";
        public string Unit { get; set; } = "";
        public List<PointLabel> Labels { get; } = new List<PointLabel>();
        public List<int> Dots { get; } = new List<int>();

        public void SetData(double[] x, double[] y)
        {
            if (x == null || y == null || x.Length != y.Length) throw new ArgumentException("X e Y deben tener el mismo largo.");
            xs = x; ys = y; Invalidate();
        }

        public double[] XValues => xs;
        public double[] YValues => ys;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(PlotBackColor);
            DrawTitle(g);
            using var text = new SolidBrush(TextColor);
            using var grid = new Pen(GridColor);
            int top = string.IsNullOrEmpty(Title) ? 10 : 28;
            int size = Math.Max(20, Math.Min(Width - 90, Height - top - 40));
            var r = new Rectangle((Width - size) / 2, top + 6, size, size);
            double maxAbs = 1e-12;
            for (int i = 0; i < xs.Length; i++) maxAbs = Math.Max(maxAbs, Math.Max(Math.Abs(xs[i]), Math.Abs(ys[i])));
            double scale = Ticks(0, maxAbs * 1.1, 4).DefaultIfEmpty(maxAbs * 1.1).Last();
            if (scale < maxAbs) scale = maxAbs * 1.1;
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, rad = r.Width / 2f;
            PointF P(double x, double y) => new PointF((float)(cx + x / scale * rad), (float)(cy - y / scale * rad));

            if (PolarGrid)
            {
                foreach (var v in Ticks(0, scale, 4).Where(v => v > 0))
                {
                    float rr = (float)(v / scale * rad);
                    g.DrawEllipse(grid, cx - rr, cy - rr, 2 * rr, 2 * rr);
                    g.DrawString(Fmt(v), Font, text, cx + 2, cy - rr);
                }
                for (int a = 0; a < 360; a += 30)
                {
                    double th = a * Math.PI / 180;
                    var end = new PointF((float)(cx + Math.Sin(th) * rad), (float)(cy - Math.Cos(th) * rad));
                    g.DrawLine(grid, cx, cy, end.X, end.Y);
                    var s = a + "°";
                    var sz = g.MeasureString(s, Font);
                    g.DrawString(s, Font, text, (float)(cx + Math.Sin(th) * (rad + 12)) - sz.Width / 2, (float)(cy - Math.Cos(th) * (rad + 12)) - sz.Height / 2);
                }
            }
            else
            {
                foreach (var v in Ticks(-scale, scale, 8))
                {
                    var p = P(v, 0); var q = P(0, v);
                    g.DrawLine(grid, p.X, r.Top, p.X, r.Bottom);
                    g.DrawLine(grid, r.Left, q.Y, r.Right, q.Y);
                    g.DrawString(Fmt(v), Font, text, p.X - 8, r.Bottom + 2);
                    var sz = g.MeasureString(Fmt(v), Font);
                    g.DrawString(Fmt(v), Font, text, r.Left - sz.Width - 2, q.Y - sz.Height / 2);
                }
                using var axis = new Pen(TextColor);
                g.DrawRectangle(axis, r);
                g.DrawString($"{XLabel} {Unit}", Font, text, r.Right - 70, r.Bottom + 16);
                var st = g.Save();
                g.TranslateTransform(r.Left - 46, r.Top + 60); g.RotateTransform(-90);
                g.DrawString($"{YLabel} {Unit}", Font, text, 0, 0);
                g.Restore(st);
            }
            if (xs.Length > 1)
            {
                using var pen = new Pen(LineColor, 1.6f);
                g.DrawLines(pen, Enumerable.Range(0, xs.Length).Select(i => P(xs[i], ys[i])).ToArray());
                using var start = new SolidBrush(Color.FromArgb(30, 150, 80));
                var p0 = P(xs[0], ys[0]);
                g.FillEllipse(start, p0.X - 4, p0.Y - 4, 8, 8);
            }
            using (var dot = new SolidBrush(Color.FromArgb(200, 70, 20)))
            {
                foreach (int i in Dots.Where(i => i >= 0 && i < xs.Length))
                {
                    var p = P(xs[i], ys[i]);
                    g.FillEllipse(dot, p.X - 4, p.Y - 4, 8, 8);
                }
                foreach (var l in Labels.Where(l => l.Index >= 0 && l.Index < xs.Length))
                {
                    var p = P(xs[l.Index], ys[l.Index]);
                    g.FillEllipse(dot, p.X - 2.5f, p.Y - 2.5f, 5, 5);
                    g.DrawString(l.Text, Font, text, p.X + 4, p.Y - 14);
                }
            }
        }
    }

    /// <summary>Mapa de color (espectrograma): filas = bloques en el tiempo (o rpm), columnas = frecuencia.</summary>
    public sealed class HeatmapControl : ChartBase
    {
        private double[,] data = new double[0, 0];
        private double[] cols = Array.Empty<double>(), rows = Array.Empty<double>();
        private Bitmap image;
        public bool LogScale { get; set; } = true;
        public double DynamicRangeDb { get; set; } = 60;
        public string XLabel { get; set; } = "Frecuencia (Hz)";
        public string YLabel { get; set; } = "Tiempo (s)";

        public void SetData(double[,] values, double[] columnValues, double[] rowValues)
        {
            data = values; cols = columnValues; rows = rowValues;
            image?.Dispose(); image = null;
            Invalidate();
        }

        private Bitmap Render()
        {
            int nr = data.GetLength(0), nc = data.GetLength(1);
            if (nr == 0 || nc == 0) return null;
            double max = 1e-300;
            foreach (var v in data) max = Math.Max(max, v);
            var bmp = new Bitmap(nc, nr, PixelFormat.Format32bppArgb);
            var px = new int[nc * nr];
            for (int i = 0; i < nr; i++)
                for (int k = 0; k < nc; k++)
                {
                    double v = data[i, k];
                    double t = LogScale ? 1 + 20 * Math.Log10(Math.Max(v, max * 1e-12) / max) / DynamicRangeDb : v / max;
                    px[(nr - 1 - i) * nc + k] = ColorMap(Math.Clamp(t, 0, 1)).ToArgb();
                }
            var bd = bmp.LockBits(new Rectangle(0, 0, nc, nr), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < nr; y++)
                    System.Runtime.InteropServices.Marshal.Copy(px, y * nc, bd.Scan0 + y * bd.Stride, nc);
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }

        /// <summary>Escala de color perceptual (azul oscuro → verde → amarillo).</summary>
        public static Color ColorMap(double t)
        {
            var stops = new[] { (0.0, 20, 20, 60), (0.25, 40, 70, 150), (0.5, 30, 150, 140), (0.75, 120, 200, 70), (1.0, 250, 230, 40) };
            for (int i = 1; i < stops.Length; i++)
            {
                if (t > stops[i].Item1) continue;
                var a = stops[i - 1]; var b = stops[i];
                double f = (t - a.Item1) / (b.Item1 - a.Item1);
                return Color.FromArgb((int)(a.Item2 + f * (b.Item2 - a.Item2)), (int)(a.Item3 + f * (b.Item3 - a.Item3)), (int)(a.Item4 + f * (b.Item4 - a.Item4)));
            }
            return Color.FromArgb(250, 230, 40);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PlotBackColor);
            DrawTitle(g);
            int top = string.IsNullOrEmpty(Title) ? 10 : 28;
            var r = new Rectangle(64, top, Math.Max(10, Width - 64 - 70), Math.Max(10, Height - top - 44));
            using var text = new SolidBrush(TextColor);
            image ??= Render();
            if (image != null)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(image, r);
            }
            using var axis = new Pen(TextColor);
            g.DrawRectangle(axis, r);
            if (cols.Length > 1)
                foreach (var v in Ticks(cols[0], cols[^1], Math.Max(2, r.Width / 90)))
                {
                    float px = (float)(r.Left + (v - cols[0]) / (cols[^1] - cols[0]) * r.Width);
                    g.DrawLine(axis, px, r.Bottom, px, r.Bottom + 3);
                    g.DrawString(Fmt(v), Font, text, px - 10, r.Bottom + 4);
                }
            if (rows.Length > 1)
            {
                double lo = rows.Min(), hi = rows.Max();
                foreach (var v in Ticks(lo, hi, Math.Max(2, r.Height / 45)))
                {
                    // Las filas pueden no ser monótonas (rpm): se ubica el rótulo en la fila más cercana
                    int idx = 0; double best = double.MaxValue;
                    for (int i = 0; i < rows.Length; i++) if (Math.Abs(rows[i] - v) < best) { best = Math.Abs(rows[i] - v); idx = i; }
                    float py = (float)(r.Bottom - (idx + 0.5) / rows.Length * r.Height);
                    var s = Fmt(v); var sz = g.MeasureString(s, Font);
                    g.DrawLine(axis, r.Left - 3, py, r.Left, py);
                    g.DrawString(s, Font, text, r.Left - sz.Width - 4, py - sz.Height / 2);
                }
            }
            g.DrawString(XLabel, Font, text, r.Left + r.Width / 2 - 40, r.Bottom + 20);
            var st = g.Save();
            g.TranslateTransform(10, r.Top + r.Height / 2 + 30); g.RotateTransform(-90);
            g.DrawString(YLabel, Font, text, 0, 0);
            g.Restore(st);
            // Barra de color
            var bar = new Rectangle(r.Right + 12, r.Top, 14, r.Height);
            for (int y = 0; y < bar.Height; y++)
                using (var p = new Pen(ColorMap(1 - y / (double)bar.Height))) g.DrawLine(p, bar.Left, bar.Top + y, bar.Right, bar.Top + y);
            g.DrawRectangle(axis, bar);
            g.DrawString(LogScale ? "0 dB" : "máx", Font, text, bar.Left - 2, bar.Bottom + 2);
            g.DrawString(LogScale ? $"−{DynamicRangeDb:0}" : "0", Font, text, bar.Left - 2, bar.Top - 14 < 0 ? bar.Top : bar.Top - 14);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) image?.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Cascada (waterfall) en perspectiva: un espectro por bloque, desplazado hacia arriba y a la derecha, con líneas ocultas.</summary>
    public sealed class WaterfallControl : ChartBase
    {
        private readonly List<(double Key, double[] X, double[] Y)> slices = new List<(double, double[], double[])>();
        public string XLabel { get; set; } = "Frecuencia (Hz)";
        public string KeyLabel { get; set; } = "rpm";
        public string YLabel { get; set; } = "Amplitud";
        public double MaxX { get; set; }

        public void Clear() { slices.Clear(); Invalidate(); }

        public void Add(double key, double[] x, double[] y) { slices.Add((key, x, y)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(PlotBackColor);
            DrawTitle(g);
            using var text = new SolidBrush(TextColor);
            using var axis = new Pen(TextColor);
            if (slices.Count == 0) return;
            int top = string.IsNullOrEmpty(Title) ? 10 : 28;
            float depthX = Math.Min(140, Width * 0.18f), depthY = Math.Min(220, Height * 0.5f);
            var r = new RectangleF(70, top + depthY, Math.Max(20, Width - 70 - 20 - depthX), Math.Max(20, Height - top - depthY - 40));
            double xMin = slices[0].X[0], xMax = MaxX > 0 ? MaxX : slices[0].X[^1];
            double yMax = 1e-12;
            foreach (var s in slices)
                for (int i = 0; i < s.X.Length && s.X[i] <= xMax; i++) yMax = Math.Max(yMax, s.Y[i]);
            int n = slices.Count;
            using var fill = new SolidBrush(PlotBackColor);
            using var line = new Pen(LineColor, 1.1f);
            // Del fondo hacia el frente: cada espectro tapa a los de atrás
            for (int j = n - 1; j >= 0; j--)
            {
                float ox = depthX * j / Math.Max(1, n - 1), oy = -depthY * j / Math.Max(1, n - 1);
                var s = slices[j];
                var pts = new List<PointF>();
                int step = Math.Max(1, s.X.Length / Math.Max(50, (int)r.Width));
                for (int i = 0; i < s.X.Length && s.X[i] <= xMax; i += step)
                {
                    float px = (float)(r.Left + ox + (s.X[i] - xMin) / (xMax - xMin) * r.Width);
                    float py = (float)(r.Bottom + oy - s.Y[i] / yMax * r.Height);
                    pts.Add(new PointF(px, py));
                }
                if (pts.Count < 2) continue;
                var poly = new List<PointF>(pts) { new PointF(pts[^1].X, r.Bottom + oy), new PointF(pts[0].X, r.Bottom + oy) };
                g.FillPolygon(fill, poly.ToArray());
                g.DrawLines(line, pts.ToArray());
                if (j % Math.Max(1, n / 8) == 0 || j == n - 1)
                    g.DrawString(Fmt(s.Key), Font, text, r.Left + ox - 48, r.Bottom + oy - 8);
            }
            g.DrawLine(axis, r.Left, r.Bottom, r.Right, r.Bottom);
            g.DrawLine(axis, r.Left, r.Bottom, r.Left + depthX, r.Bottom - depthY);
            foreach (var v in Ticks(xMin, xMax, Math.Max(2, (int)r.Width / 90)))
            {
                float px = (float)(r.Left + (v - xMin) / (xMax - xMin) * r.Width);
                g.DrawLine(axis, px, r.Bottom, px, r.Bottom + 3);
                g.DrawString(Fmt(v), Font, text, px - 10, r.Bottom + 4);
            }
            g.DrawString(XLabel, Font, text, r.Left + r.Width / 2 - 40, r.Bottom + 20);
            g.DrawString(KeyLabel, Font, text, r.Left + depthX - 50, r.Bottom - depthY - 18);
            g.DrawString($"{YLabel}: escala máx. {Fmt(yMax)}", Font, text, r.Left, top);
        }
    }
}
