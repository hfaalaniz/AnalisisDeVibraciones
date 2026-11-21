using System.Globalization;
using System.Net;
using System.Text;

namespace VibrationAnalysis.Core.Report;

/// <summary>Serie de un gráfico SVG.</summary>
public sealed record SvgSeries(double[] X, double[] Y, string Color = "#0e7c86", string Label = "", bool Points = false);

/// <summary>Definición de un gráfico X-Y para el informe.</summary>
public sealed class SvgChartSpec
{
    public string Title { get; init; } = "";
    public string XLabel { get; init; } = "";
    public string YLabel { get; init; } = "";
    public List<SvgSeries> Series { get; init; } = new();
    /// <summary>Marcadores verticales (x, rótulo, color).</summary>
    public List<(double X, string Label, string Color)> Markers { get; init; } = new();
    /// <summary>Líneas horizontales (y, rótulo, color), p. ej. límites de alarma.</summary>
    public List<(double Y, string Label, string Color)> HLines { get; init; } = new();
    public Func<double, string>? XFormat { get; init; }
    public double? XMin { get; init; }
    public double? XMax { get; init; }
    public double? YMin { get; init; }
    public int Width { get; init; } = 760;
    public int Height { get; init; } = 260;
}

/// <summary>
/// Gráficos vectoriales (SVG) para el informe: se ven nítidos en pantalla y al imprimir o pasar a PDF, sin dependencias.
/// Las series largas se reducen por píxel (mínimo y máximo) para que el archivo quede liviano.
/// </summary>
public static class SvgChart
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");
    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string E(string s) => WebUtility.HtmlEncode(s);
    private static int clipId;

    public static string Render(SvgChartSpec c)
    {
        const int left = 64, right = 16, top = 30, bottom = 44;
        int w = c.Width, h = c.Height, pw = w - left - right, ph = h - top - bottom;
        var all = c.Series.Where(s => s.X.Length > 0 && s.X.Length == s.Y.Length).ToList();
        double xMin = c.XMin ?? (all.Count > 0 ? all.Min(s => s.X.Min()) : 0);
        double xMax = c.XMax ?? (all.Count > 0 ? all.Max(s => s.X.Max()) : 1);
        if (xMax <= xMin) xMax = xMin + 1;
        double yMin = double.MaxValue, yMax = double.MinValue;
        foreach (var s in all)
            for (int i = 0; i < s.X.Length; i++)
                if (s.X[i] >= xMin && s.X[i] <= xMax && double.IsFinite(s.Y[i])) { yMin = Math.Min(yMin, s.Y[i]); yMax = Math.Max(yMax, s.Y[i]); }
        foreach (var l in c.HLines) if (double.IsFinite(l.Y)) { yMin = Math.Min(yMin, l.Y); yMax = Math.Max(yMax, l.Y); }
        if (yMin > yMax) { yMin = 0; yMax = 1; }
        if (c.YMin.HasValue) yMin = c.YMin.Value; else if (yMin > 0) yMin = 0;
        double span = yMax - yMin;
        if (span <= 0) span = Math.Abs(yMax) > 0 ? Math.Abs(yMax) : 1;
        yMax += span * 0.08;
        double X(double v) => left + (v - xMin) / (xMax - xMin) * pw;
        double Y(double v) => top + ph - (v - yMin) / (yMax - yMin) * ph;

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {w} {h}\" width=\"100%\" role=\"img\" aria-label=\"{E(c.Title)}\" style=\"font-family:system-ui,Segoe UI,Arial,sans-serif;font-size:11px\">");
        sb.Append($"<rect x=\"0\" y=\"0\" width=\"{w}\" height=\"{h}\" fill=\"#fff\"/>");
        if (c.Title.Length > 0) sb.Append($"<text x=\"{left}\" y=\"18\" font-weight=\"600\" font-size=\"12\" fill=\"#1d2327\">{E(c.Title)}</text>");
        // Grilla y ejes
        foreach (var v in Ticks(xMin, xMax, Math.Max(2, pw / 95)))
        {
            double px = X(v);
            sb.Append($"<line x1=\"{N(px)}\" y1=\"{top}\" x2=\"{N(px)}\" y2=\"{top + ph}\" stroke=\"#e3e9eb\"/>");
            sb.Append($"<text x=\"{N(px)}\" y=\"{top + ph + 14}\" text-anchor=\"middle\" fill=\"#5f6b70\">{E(c.XFormat != null ? c.XFormat(v) : Fmt(v))}</text>");
        }
        foreach (var v in Ticks(yMin, yMax, Math.Max(3, ph / 45)))
        {
            double py = Y(v);
            sb.Append($"<line x1=\"{left}\" y1=\"{N(py)}\" x2=\"{left + pw}\" y2=\"{N(py)}\" stroke=\"#e3e9eb\"/>");
            sb.Append($"<text x=\"{left - 5}\" y=\"{N(py + 4)}\" text-anchor=\"end\" fill=\"#5f6b70\">{E(Fmt(v))}</text>");
        }
        sb.Append($"<rect x=\"{left}\" y=\"{top}\" width=\"{pw}\" height=\"{ph}\" fill=\"none\" stroke=\"#9aa7ac\"/>");
        if (c.XLabel.Length > 0) sb.Append($"<text x=\"{left + pw / 2}\" y=\"{h - 8}\" text-anchor=\"middle\" fill=\"#1d2327\">{E(c.XLabel)}</text>");
        if (c.YLabel.Length > 0) sb.Append($"<text transform=\"translate(14 {top + ph / 2}) rotate(-90)\" text-anchor=\"middle\" fill=\"#1d2327\">{E(c.YLabel)}</text>");
        int id = Interlocked.Increment(ref clipId);
        sb.Append($"<clipPath id=\"clip{id}\"><rect x=\"{left}\" y=\"{top}\" width=\"{pw}\" height=\"{ph}\"/></clipPath><g clip-path=\"url(#clip{id})\">");
        foreach (var (y, label, color) in c.HLines.Where(l => double.IsFinite(l.Y)))
        {
            double py = Y(y);
            sb.Append($"<line x1=\"{left}\" y1=\"{N(py)}\" x2=\"{left + pw}\" y2=\"{N(py)}\" stroke=\"{color}\" stroke-width=\"1.3\" stroke-dasharray=\"6 4\"/>");
            sb.Append($"<text x=\"{left + pw - 4}\" y=\"{N(py - 4)}\" text-anchor=\"end\" fill=\"{color}\">{E(label)}</text>");
        }
        foreach (var (x, label, color) in c.Markers.Where(m => m.X >= xMin && m.X <= xMax))
        {
            double px = X(x);
            sb.Append($"<line x1=\"{N(px)}\" y1=\"{top}\" x2=\"{N(px)}\" y2=\"{top + ph}\" stroke=\"{color}\" stroke-dasharray=\"4 3\" opacity=\"0.8\"/>");
            if (label.Length > 0) sb.Append($"<text x=\"{N(px + 3)}\" y=\"{top + 11}\" fill=\"{color}\">{E(label)}</text>");
        }
        foreach (var s in all)
        {
            var pts = Reduce(s, xMin, xMax, pw);
            if (s.Points)
                foreach (var (x, y) in pts) sb.Append($"<circle cx=\"{N(X(x))}\" cy=\"{N(Y(y))}\" r=\"3\" fill=\"{s.Color}\"/>");
            if (pts.Count > 1)
                sb.Append($"<polyline fill=\"none\" stroke=\"{s.Color}\" stroke-width=\"1.4\" stroke-linejoin=\"round\" points=\"{string.Join(" ", pts.Select(p => N(X(p.X)) + "," + N(Y(p.Y))))}\"/>");
        }
        sb.Append("</g>");
        // Leyenda
        int lx = left + 8;
        foreach (var s in all.Where(s => s.Label.Length > 0))
        {
            sb.Append($"<rect x=\"{lx}\" y=\"{top + ph - 16}\" width=\"10\" height=\"3\" fill=\"{s.Color}\"/><text x=\"{lx + 14}\" y=\"{top + ph - 11}\" fill=\"#1d2327\">{E(s.Label)}</text>");
            lx += 24 + s.Label.Length * 6;
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static List<(double X, double Y)> Reduce(SvgSeries s, double xMin, double xMax, int width)
    {
        var idx = Enumerable.Range(0, s.X.Length).Where(i => s.X[i] >= xMin && s.X[i] <= xMax && double.IsFinite(s.Y[i])).ToList();
        if (idx.Count <= width * 2) return idx.Select(i => (s.X[i], s.Y[i])).ToList();
        var r = new List<(double, double)>(width * 2);
        int bucket = (int)Math.Ceiling(idx.Count / (double)width);
        for (int b = 0; b < idx.Count; b += bucket)
        {
            int lo = idx[b], hi = idx[b];
            for (int k = b; k < Math.Min(b + bucket, idx.Count); k++)
            {
                if (s.Y[idx[k]] < s.Y[lo]) lo = idx[k];
                if (s.Y[idx[k]] > s.Y[hi]) hi = idx[k];
            }
            if (lo <= hi) { r.Add((s.X[lo], s.Y[lo])); r.Add((s.X[hi], s.Y[hi])); }
            else { r.Add((s.X[hi], s.Y[hi])); r.Add((s.X[lo], s.Y[lo])); }
        }
        return r;
    }

    private static string Fmt(double v)
    {
        double a = Math.Abs(v);
        if (a == 0) return "0";
        if (a >= 100000 || a < 0.001) return v.ToString("0.##E+0", Es);
        return v.ToString(a >= 100 ? "0" : a >= 10 ? "0.#" : a >= 1 ? "0.##" : "0.###", Es);
    }

    private static IEnumerable<double> Ticks(double min, double max, int target)
    {
        double span = max - min;
        if (span <= 0 || !double.IsFinite(span)) yield break;
        double raw = span / target, mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double step = new[] { 1, 2, 2.5, 5, 10 }.Select(k => k * mag).First(s => s >= raw);
        for (double v = Math.Ceiling(min / step) * step; v <= max + step * 1e-9; v += step) yield return Math.Abs(v) < step * 1e-9 ? 0 : v;
    }
}
