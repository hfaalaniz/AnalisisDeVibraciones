namespace VibrationAnalysis.Core.Dsp;

public enum WindowType
{
    Rectangular,
    Hann,
    Hamming,
    FlatTop,
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
                _ => 1.0,
            };
        }
        return w;
    }

    /// <summary>Ganancia coherente (media de la ventana): divide la amplitud medida para recuperar la real.</summary>
    public static double CoherentGain(double[] w) => w.Average();

    public static string DisplayName(WindowType t) => t switch
    {
        WindowType.Rectangular => "Rectangular",
        WindowType.Hann => "Hanning",
        WindowType.Hamming => "Hamming",
        WindowType.FlatTop => "Flat-top",
        _ => t.ToString(),
    };
}
