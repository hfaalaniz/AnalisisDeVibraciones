namespace VibrationAnalysis.Core.Dsp;

public enum FilterKind { LowPass, HighPass, BandPass }

/// <summary>Sección bicuadrática (forma directa II transpuesta), coeficientes normalizados con a0 = 1.</summary>
public sealed class Biquad
{
    public double B0, B1, B2, A1, A2;
    private double z1, z2;

    public void Reset() => z1 = z2 = 0;

    public double Process(double x)
    {
        double y = B0 * x + z1;
        z1 = B1 * x - A1 * y + z2;
        z2 = B2 * x - A2 * y;
        return y;
    }

    /// <summary>Pasabajos/pasaaltos de 2.º orden (R. Bristow-Johnson) con factor de calidad Q.</summary>
    public static Biquad Create(bool highPass, double fc, double fs, double q)
    {
        double w0 = 2 * Math.PI * fc / fs, cw = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q), a0 = 1 + alpha;
        double b1 = highPass ? -(1 + cw) : 1 - cw;
        double b0 = highPass ? (1 + cw) / 2 : (1 - cw) / 2;
        return new Biquad { B0 = b0 / a0, B1 = b1 / a0, B2 = b0 / a0, A1 = -2 * cw / a0, A2 = (1 - alpha) / a0 };
    }

    /// <summary>Ganancia (módulo) a la frecuencia f.</summary>
    public double Gain(double f, double fs)
    {
        var z = System.Numerics.Complex.FromPolarCoordinates(1, -2 * Math.PI * f / fs);
        var num = B0 + B1 * z + B2 * z * z;
        var den = 1 + A1 * z + A2 * z * z;
        return (num / den).Magnitude;
    }
}

/// <summary>
/// Filtro IIR Butterworth en cascada de secciones bicuadráticas.
/// Pasabanda = pasaaltos(fLow) + pasabajos(fHigh). Con <see cref="FiltFilt"/> el retardo de fase es nulo.
/// </summary>
public sealed class IirFilter
{
    public IReadOnlyList<Biquad> Sections { get; }
    public FilterKind Kind { get; }
    public int Order { get; }
    public double SampleRate { get; }

    private readonly double lowCorner;

    private IirFilter(FilterKind kind, int order, double fs, double lowCorner, List<Biquad> sections)
    {
        Kind = kind; Order = order; SampleRate = fs; Sections = sections; this.lowCorner = lowCorner;
    }

    /// <param name="order">Orden par (2, 4, 6, 8). En pasabanda es el orden de cada flanco.</param>
    public static IirFilter Butterworth(FilterKind kind, int order, double fs, double f1, double f2 = 0)
    {
        if (order < 2 || order > 12 || order % 2 != 0) throw new ArgumentOutOfRangeException(nameof(order), "El orden debe ser par, entre 2 y 12.");
        if (fs <= 0) throw new ArgumentOutOfRangeException(nameof(fs));
        double nyq = fs / 2;
        void CheckF(double f, string name) { if (!(f > 0 && f < nyq)) throw new ArgumentOutOfRangeException(name, $"La frecuencia debe estar entre 0 y {nyq:0.#} Hz."); }
        var list = new List<Biquad>();
        void Add(bool hp, double fc)
        {
            for (int k = 0; k < order / 2; k++)
            {
                double q = 1 / (2 * Math.Cos((2 * k + 1) * Math.PI / (2 * order)));
                list.Add(Biquad.Create(hp, fc, fs, q));
            }
        }
        switch (kind)
        {
            case FilterKind.LowPass: CheckF(f1, nameof(f1)); Add(false, f1); break;
            case FilterKind.HighPass: CheckF(f1, nameof(f1)); Add(true, f1); break;
            default:
                CheckF(f1, nameof(f1)); CheckF(f2, nameof(f2));
                if (f2 <= f1) throw new ArgumentException("La frecuencia superior debe ser mayor que la inferior.");
                Add(true, f1); Add(false, f2);
                break;
        }
        return new IirFilter(kind, order, fs, f1, list);
    }

    public double Gain(double f) => Sections.Aggregate(1.0, (g, s) => g * s.Gain(f, SampleRate));

    /// <summary>Filtrado causal (con retardo de fase).</summary>
    public double[] Apply(IReadOnlyList<double> x)
    {
        var y = x.ToArray();
        foreach (var s in Sections)
        {
            s.Reset();
            for (int i = 0; i < y.Length; i++) y[i] = s.Process(y[i]);
        }
        return y;
    }

    /// <summary>Filtrado ida y vuelta: fase cero y magnitud al cuadrado. Extiende los bordes por reflexión para reducir transitorios.</summary>
    public double[] FiltFilt(IReadOnlyList<double> x)
    {
        int n = x.Count;
        if (n == 0) return Array.Empty<double>();
        // Relleno: unos 3 ciclos de la frecuencia de corte más baja, sin exceder la señal
        int pad = (int)Math.Min(n - 1, Math.Max(3 * (2 * Sections.Count + 1), 3 * SampleRate / lowCorner));
        var ext = new double[n + 2 * pad];
        for (int i = 0; i < pad; i++) ext[i] = 2 * x[0] - x[pad - i];
        for (int i = 0; i < n; i++) ext[pad + i] = x[i];
        for (int i = 0; i < pad; i++) ext[pad + n + i] = 2 * x[n - 1] - x[n - 2 - i];
        var y = Apply(ext);
        Array.Reverse(y);
        y = Apply(y);
        Array.Reverse(y);
        return y.AsSpan(pad, n).ToArray();
    }
}
