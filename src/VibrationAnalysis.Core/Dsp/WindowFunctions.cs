namespace VibrationAnalysis.Core.Dsp;

public enum WindowType
{
    Rectangular,
    Hann,
    Hamming,
    FlatTop,
    BlackmanHarris,
    Kaiser,
}

/// <summary>Ventanas espectrales y sus factores de corrección de amplitud.</summary>
public static class WindowFunctions
{
    public static double[] Create(WindowType type, int n)
    {
        var w = new double[n];
        if (n == 1) { w[0] = 1; return w; }
        for (int i = 0; i < n; i++)
        {
            double x = 2 * Math.PI * i / (n - 1);
            w[i] = type switch
            {
                WindowType.Rectangular => 1.0,
                WindowType.Hann => 0.5 - 0.5 * Math.Cos(x),
                WindowType.Hamming => 0.54 - 0.46 * Math.Cos(x),
                WindowType.FlatTop => 0.21557895 - 0.41663158 * Math.Cos(x) + 0.277263158 * Math.Cos(2 * x)
                                      - 0.083578947 * Math.Cos(3 * x) + 0.006947368 * Math.Cos(4 * x),
                WindowType.BlackmanHarris => 0.35875 - 0.48829 * Math.Cos(x) + 0.14128 * Math.Cos(2 * x) - 0.01168 * Math.Cos(3 * x),
                WindowType.Kaiser => Kaiser(i, n, 8.6),
                _ => 1.0,
            };
        }
        return w;
    }

    private static double Kaiser(int i, int n, double beta)
    {
        double r = 2.0 * i / (n - 1) - 1;
        return BesselI0(beta * Math.Sqrt(Math.Max(0, 1 - r * r))) / BesselI0(beta);
    }

    private static double BesselI0(double x)
    {
        double sum = 1, term = 1, y = x * x / 4;
        for (int k = 1; k < 60 && term > 1e-16 * sum; k++) { term *= y / (k * k); sum += term; }
        return sum;
    }

    /// <summary>Ancho de banda equivalente de ruido en líneas: N·Σw² / (Σw)².</summary>
    public static double Enbw(double[] w)
    {
        double s1 = 0, s2 = 0;
        foreach (var v in w) { s1 += v; s2 += v * v; }
        return w.Length * s2 / (s1 * s1);
    }

    /// <summary>Ganancia coherente (media de la ventana): divide la amplitud medida para recuperar la real.</summary>
    public static double CoherentGain(double[] w) => w.Average();

    public static string DisplayName(WindowType t) => t switch
    {
        WindowType.Rectangular => "Rectangular",
        WindowType.Hann => "Hanning",
        WindowType.Hamming => "Hamming",
        WindowType.FlatTop => "Flat-top",
        WindowType.BlackmanHarris => "Blackman-Harris",
        WindowType.Kaiser => "Kaiser (β 8,6)",
        _ => t.ToString(),
    };
}
