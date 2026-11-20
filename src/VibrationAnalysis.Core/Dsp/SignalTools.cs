using System.Numerics;

namespace VibrationAnalysis.Core.Dsp;

/// <summary>Unidad física de una señal de vibración.</summary>
public enum VibrationUnit
{
    /// <summary>Aceleración en g (9,80665 m/s²).</summary>
    AccelerationG,
    /// <summary>Aceleración en m/s².</summary>
    AccelerationMs2,
    /// <summary>Velocidad en mm/s.</summary>
    VelocityMmS,
    /// <summary>Desplazamiento en µm.</summary>
    DisplacementUm,
}

public sealed record SignalStatistics(double Rms, double Peak, double PeakToPeak, double CrestFactor, double Kurtosis, double Mean);

public static class SignalTools
{
    public const double G = 9.80665;

    public static SignalStatistics Statistics(IReadOnlyList<double> x)
    {
        if (x.Count == 0) return new SignalStatistics(0, 0, 0, 0, 0, 0);
        double mean = x.Average();
        double s2 = 0, s4 = 0, max = double.MinValue, min = double.MaxValue, peak = 0;
        foreach (var v in x)
        {
            double d = v - mean;
            s2 += d * d; s4 += d * d * d * d;
            if (v > max) max = v;
            if (v < min) min = v;
            if (Math.Abs(d) > peak) peak = Math.Abs(d);
        }
        double var = s2 / x.Count;
        double rms = Math.Sqrt(var);
        double kurt = var > 0 ? (s4 / x.Count) / (var * var) : 0;
        return new SignalStatistics(rms, peak, max - min, rms > 0 ? peak / rms : 0, kurt, mean);
    }

    /// <summary>
    /// Convierte una señal entre aceleración, velocidad y desplazamiento integrando o derivando en el dominio de la frecuencia.
    /// Las componentes por debajo de <paramref name="highPassHz"/> se eliminan para evitar la deriva de la integración.
    /// </summary>
    public static double[] Convert(IReadOnlyList<double> x, double sampleRate, VibrationUnit from, VibrationUnit to, double highPassHz = 2.0)
    {
        if (from == to) return x.ToArray();
        int n = x.Count;
        double mean = x.Average();
        var a = new Complex[n];
        for (int i = 0; i < n; i++) a[i] = new Complex(x[i] - mean, 0);
        Fft.Transform(a, inverse: false);
        // Llevar todo a m/s² · (jω)^p:  orden p: acc=0, vel=-1, disp=-2
        int pFrom = Order(from), pTo = Order(to);
        double kFrom = ToSi(from), kTo = ToSi(to);
        for (int k = 0; k < n; k++)
        {
            int kk = k <= n / 2 ? k : k - n;
            double f = kk * sampleRate / n;
            if (Math.Abs(f) < highPassHz) { a[k] = Complex.Zero; continue; }
            var jw = new Complex(0, 2 * Math.PI * f);
            a[k] *= kFrom * Complex.Pow(jw, pTo - pFrom) / kTo;
        }
        Fft.Transform(a, inverse: true);
        var y = new double[n];
        for (int i = 0; i < n; i++) y[i] = a[i].Real;
        return y;
    }

    private static int Order(VibrationUnit u) => u switch
    {
        VibrationUnit.AccelerationG or VibrationUnit.AccelerationMs2 => 0,
        VibrationUnit.VelocityMmS => -1,
        _ => -2,
    };

    /// <summary>Factor para pasar la unidad a SI (m/s², m/s o m).</summary>
    private static double ToSi(VibrationUnit u) => u switch
    {
        VibrationUnit.AccelerationG => G,
        VibrationUnit.AccelerationMs2 => 1.0,
        VibrationUnit.VelocityMmS => 1e-3,
        _ => 1e-6,
    };

    public static string Symbol(VibrationUnit u) => u switch
    {
        VibrationUnit.AccelerationG => "g",
        VibrationUnit.AccelerationMs2 => "m/s²",
        VibrationUnit.VelocityMmS => "mm/s",
        _ => "µm",
    };
}

/// <summary>Detección de pulsos del tacómetro (marca de referencia de fase).</summary>
public static class Tachometer
{
    /// <summary>Instantes (s) de los flancos de subida que cruzan el umbral (por defecto, el punto medio de la señal).</summary>
    public static double[] DetectPulses(IReadOnlyList<double> tach, double sampleRate, double? threshold = null, double minPeriodS = 0.002)
    {
        if (tach.Count < 2) return Array.Empty<double>();
        double thr = threshold ?? (tach.Max() + tach.Min()) / 2;
        var res = new List<double>();
        double last = double.NegativeInfinity;
        for (int i = 1; i < tach.Count; i++)
        {
            if (tach[i - 1] < thr && tach[i] >= thr)
            {
                // Interpolación lineal del cruce
                double frac = (thr - tach[i - 1]) / (tach[i] - tach[i - 1]);
                double t = (i - 1 + frac) / sampleRate;
                if (t - last >= minPeriodS) { res.Add(t); last = t; }
            }
        }
        return res.ToArray();
    }

    /// <summary>Velocidad media en rpm a partir de los pulsos (1 pulso por vuelta salvo que se indique).</summary>
    public static double Rpm(IReadOnlyList<double> pulses, int pulsesPerRev = 1)
    {
        if (pulses.Count < 2) return 0;
        double period = (pulses[^1] - pulses[0]) / (pulses.Count - 1) * pulsesPerRev;
        return period > 0 ? 60.0 / period : 0;
    }
}

/// <summary>Utilidades de ángulos en grados.</summary>
public static class Angles
{
    public static double Normalize(double deg)
    {
        double r = deg % 360.0;
        if (r < 0) r += 360.0;
        if (r >= 360.0 - 1e-12) r = 0;
        return r;
    }

    /// <summary>Diferencia mínima con signo entre dos ángulos, en (−180, 180].</summary>
    public static double Difference(double a, double b)
    {
        double d = Normalize(a - b);
        return d > 180 ? d - 360 : d;
    }
}
