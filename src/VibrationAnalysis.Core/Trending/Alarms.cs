using VibrationAnalysis.Core.Analysis;

namespace VibrationAnalysis.Core.Trending;

public sealed record AlarmResult(string Feature, double Value, AlarmLevel Level, double AlertLimit, double DangerLimit, string Message);

public sealed record TrendFit(double SlopePerDay, double Intercept, double R2, bool Exponential, DateTime Origin)
{
    public double ValueAt(DateTime t)
    {
        double d = (t - Origin).TotalDays;
        return Exponential ? Math.Exp(Intercept + SlopePerDay * d) : Intercept + SlopePerDay * d;
    }

    /// <summary>Fecha en que la tendencia alcanza el valor dado, o null si no lo alcanza (pendiente nula o negativa).</summary>
    public DateTime? Crossing(double value)
    {
        if (SlopePerDay <= 0 || value <= 0 && Exponential) return null;
        double d = Exponential ? (Math.Log(value) - Intercept) / SlopePerDay : (value - Intercept) / SlopePerDay;
        if (double.IsNaN(d) || double.IsInfinity(d) || d > 365 * 30) return null;
        return Origin.AddDays(d);
    }
}

/// <summary>Evaluación de alarmas y análisis de tendencia de los indicadores de un punto.</summary>
public static class TrendAnalysis
{
    /// <summary>Mediciones de la línea base: las indicadas en el punto o, si no hay, las primeras <see cref="AlarmDefaults.BaselineCount"/>.</summary>
    public static IReadOnlyList<MeasurementRecord> Baseline(MeasurementPoint point, IReadOnlyList<MeasurementRecord> history)
    {
        if (point.BaselineIds.Count > 0)
        {
            var set = point.BaselineIds.ToHashSet();
            var sel = history.Where(h => set.Contains(h.Id)).ToList();
            if (sel.Count > 0) return sel;
        }
        return history.OrderBy(h => h.Timestamp).Take(AlarmDefaults.BaselineCount).ToList();
    }

    /// <summary>Límites de alerta y peligro de una regla para este punto (NaN si no se pueden calcular).</summary>
    public static (double Alert, double Danger) Limits(AlarmRule rule, MachineClass machineClass, IReadOnlyList<MeasurementRecord> baseline)
    {
        var vals = baseline.Select(b => b.Get(rule.Feature)).Where(v => !double.IsNaN(v)).ToList();
        switch (rule.Kind)
        {
            case AlarmKind.Iso:
            {
                var l = Severity.Limits(machineClass);
                return (l[1], l[2]);
            }
            case AlarmKind.BaselineRatio:
            {
                if (vals.Count == 0) return (double.NaN, double.NaN);
                double m = vals.Average();
                return m <= 0 ? (double.NaN, double.NaN) : (m * rule.Alert, m * rule.Danger);
            }
            case AlarmKind.Statistical:
            {
                if (vals.Count < 2) return (double.NaN, double.NaN);
                double m = vals.Average();
                double sd = Math.Sqrt(vals.Sum(v => (v - m) * (v - m)) / (vals.Count - 1));
                sd = Math.Max(sd, Math.Abs(m) * 0.05);   // evita límites pegados a la media con bases muy estables
                return (m + rule.Alert * sd, m + rule.Danger * sd);
            }
            default:
                return (rule.Alert, rule.Danger);
        }
    }

    /// <summary>Evalúa todas las reglas activas del punto sobre una medición.</summary>
    public static IReadOnlyList<AlarmResult> Evaluate(MeasurementRecord record, MeasurementPoint point, MachineClass machineClass,
        IReadOnlyList<MeasurementRecord> history)
    {
        var baseline = Baseline(point, history);
        var res = new List<AlarmResult>();
        foreach (var rule in point.Alarms.Where(a => a.Enabled))
        {
            double v = record.Get(rule.Feature);
            if (double.IsNaN(v)) continue;
            var (alert, danger) = Limits(rule, machineClass, baseline);
            if (double.IsNaN(alert)) continue;
            var lvl = v >= danger ? AlarmLevel.Danger : v >= alert ? AlarmLevel.Alert : AlarmLevel.Normal;
            string name = FeatureKeys.Name(rule.Feature);
            string msg = lvl switch
            {
                AlarmLevel.Danger => $"PELIGRO · {name} = {v:0.###} (límite {danger:0.###})",
                AlarmLevel.Alert => $"ALERTA · {name} = {v:0.###} (límite {alert:0.###})",
                _ => $"{name} = {v:0.###}",
            };
            res.Add(new AlarmResult(rule.Feature, v, lvl, alert, danger, msg));
        }
        return res;
    }

    public static AlarmLevel WorstLevel(IEnumerable<AlarmResult> results) =>
        results.Select(r => r.Level).DefaultIfEmpty(AlarmLevel.Normal).Max();

    /// <summary>Serie temporal de un indicador (fecha, valor), ordenada.</summary>
    public static IReadOnlyList<(DateTime Time, double Value)> Series(IEnumerable<MeasurementRecord> history, string feature) =>
        history.Select(h => (h.Timestamp, h.Get(feature))).Where(p => !double.IsNaN(p.Item2)).OrderBy(p => p.Timestamp).ToList();

    /// <summary>
    /// Ajusta una recta y una exponencial (recta sobre el logaritmo) a los últimos <paramref name="lastPoints"/> valores
    /// y devuelve la de mejor R². Con menos de 3 puntos devuelve null.
    /// </summary>
    public static TrendFit? Fit(IReadOnlyList<(DateTime Time, double Value)> series, int lastPoints = 8)
    {
        var s = series.Skip(Math.Max(0, series.Count - lastPoints)).ToList();
        if (s.Count < 3) return null;
        var origin = s[0].Time;
        var x = s.Select(p => (p.Time - origin).TotalDays).ToArray();
        if (x[^1] - x[0] <= 0) return null;
        var lin = Regress(x, s.Select(p => p.Value).ToArray());
        TrendFit best = new(lin.Slope, lin.Intercept, lin.R2, false, origin);
        if (s.All(p => p.Value > 0))
        {
            var ex = Regress(x, s.Select(p => Math.Log(p.Value)).ToArray());
            // R² de la exponencial medido en la escala original para comparar con justicia
            var y = s.Select(p => p.Value).ToArray();
            double mean = y.Average(), ssTot = y.Sum(v => (v - mean) * (v - mean)), ssRes = 0;
            for (int i = 0; i < y.Length; i++) { double e = y[i] - Math.Exp(ex.Intercept + ex.Slope * x[i]); ssRes += e * e; }
            double r2 = ssTot > 0 ? 1 - ssRes / ssTot : 0;
            if (r2 > lin.R2 + 0.02) best = new TrendFit(ex.Slope, ex.Intercept, r2, true, origin);
        }
        return best;
    }

    private static (double Slope, double Intercept, double R2) Regress(double[] x, double[] y)
    {
        int n = x.Length;
        double mx = x.Average(), my = y.Average(), sxx = 0, sxy = 0, syy = 0;
        for (int i = 0; i < n; i++) { sxx += (x[i] - mx) * (x[i] - mx); sxy += (x[i] - mx) * (y[i] - my); syy += (y[i] - my) * (y[i] - my); }
        double b = sxx > 0 ? sxy / sxx : 0, a = my - b * mx;
        double r2 = syy > 0 && sxx > 0 ? sxy * sxy / (sxx * syy) : 0;
        return (b, a, r2);
    }
}
