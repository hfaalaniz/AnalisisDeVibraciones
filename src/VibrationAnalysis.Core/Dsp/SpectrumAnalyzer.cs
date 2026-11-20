using System.Numerics;

namespace VibrationAnalysis.Core.Dsp;

/// <summary>Espectro de amplitud de una señal real (lado único, amplitud pico).</summary>
public sealed class Spectrum
{
    public required double[] Frequencies { get; init; }
    /// <summary>Amplitud pico de cada línea, en las unidades de la señal.</summary>
    public required double[] Amplitudes { get; init; }
    /// <summary>Fase de cada línea en grados (convención coseno).</summary>
    public required double[] PhasesDeg { get; init; }
    public double Resolution { get; init; }
    public double SampleRate { get; init; }
    public WindowType Window { get; init; }

    /// <summary>Busca el pico más alto en ±tolerancia (Hz) alrededor de una frecuencia.</summary>
    public (double Frequency, double Amplitude, int Index) PeakNear(double freq, double tolHz)
    {
        int best = -1; double amp = 0;
        for (int i = 0; i < Frequencies.Length; i++)
        {
            if (Math.Abs(Frequencies[i] - freq) <= tolHz && Amplitudes[i] > amp)
            {
                amp = Amplitudes[i]; best = i;
            }
        }
        return best < 0 ? (freq, 0, -1) : (Frequencies[best], amp, best);
    }

    /// <summary>Valor RMS global en la banda [fMin, fMax] (suma de energía de las líneas, corregida por la ventana).</summary>
    public double BandRms(double fMin, double fMax, double enbwBins = 1.0)
    {
        double s = 0;
        for (int i = 0; i < Frequencies.Length; i++)
            if (Frequencies[i] >= fMin && Frequencies[i] <= fMax)
                s += Amplitudes[i] * Amplitudes[i] / 2.0;
        return Math.Sqrt(s / enbwBins);
    }

    /// <summary>Ancho de banda equivalente de ruido en líneas para la ventana usada.</summary>
    public double NoiseBandwidthBins => Window switch
    {
        WindowType.Rectangular => 1.0,
        WindowType.Hann => 1.5,
        WindowType.Hamming => 1.363,
        WindowType.FlatTop => 3.77,
        _ => 1.0,
    };
}

public static class SpectrumAnalyzer
{
    /// <summary>Calcula el espectro de amplitud pico de una señal con la ventana indicada.</summary>
    public static Spectrum Compute(IReadOnlyList<double> signal, double sampleRate, WindowType window = WindowType.Hann, bool removeMean = true)
    {
        int n = signal.Count;
        if (n < 8) throw new ArgumentException("Se necesitan al menos 8 muestras.", nameof(signal));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        double mean = removeMean ? signal.Average() : 0;
        var w = WindowFunctions.Create(window, n);
        double cg = WindowFunctions.CoherentGain(w);
        var a = new Complex[n];
        for (int i = 0; i < n; i++) a[i] = new Complex((signal[i] - mean) * w[i], 0);
        Fft.Transform(a, inverse: false);
        int half = n / 2 + 1;
        var f = new double[half];
        var amp = new double[half];
        var ph = new double[half];
        double df = sampleRate / n;
        for (int k = 0; k < half; k++)
        {
            f[k] = k * df;
            double scale = (k == 0 || (n % 2 == 0 && k == n / 2)) ? 1.0 : 2.0;
            amp[k] = a[k].Magnitude * scale / (n * cg);
            ph[k] = Angles.Normalize(a[k].Phase * 180 / Math.PI);
        }
        return new Spectrum { Frequencies = f, Amplitudes = amp, PhasesDeg = ph, Resolution = df, SampleRate = sampleRate, Window = window };
    }

    /// <summary>
    /// Ajuste por mínimos cuadrados de una sinusoide de frecuencia conocida: x(t) ≈ A·cos(2πf(t − t0) − φ) + c.
    /// Devuelve amplitud pico A y retraso de fase φ en grados [0,360). Es exacto aunque no haya un número entero de ciclos.
    /// </summary>
    public static (double Amplitude, double PhaseLagDeg) FitSinusoid(IReadOnlyList<double> signal, double sampleRate, double freq, double t0 = 0)
    {
        // Sistema normal 3×3 para [a, b, c] en a·cos + b·sin + c
        double scc = 0, sss = 0, scs = 0, sc = 0, ss = 0, n = signal.Count, sxc = 0, sxs = 0, sx = 0;
        for (int i = 0; i < signal.Count; i++)
        {
            double t = i / sampleRate - t0;
            double th = 2 * Math.PI * freq * t;
            double c = Math.Cos(th), s = Math.Sin(th), x = signal[i];
            scc += c * c; sss += s * s; scs += c * s; sc += c; ss += s;
            sxc += x * c; sxs += x * s; sx += x;
        }
        var m = new double[3, 3] { { scc, scs, sc }, { scs, sss, ss }, { sc, ss, n } };
        var r = new[] { sxc, sxs, sx };
        var sol = Solve3(m, r);
        double a = sol[0], b = sol[1];
        // a·cos θ + b·sin θ = A·cos(θ − φ) con A = √(a²+b²), φ = atan2(b, a)
        double amp = Math.Sqrt(a * a + b * b);
        double phi = Angles.Normalize(Math.Atan2(b, a) * 180 / Math.PI);
        return (amp, phi);
    }

    private static double[] Solve3(double[,] m, double[] r)
    {
        var a = (double[,])m.Clone();
        var b = (double[])r.Clone();
        for (int col = 0; col < 3; col++)
        {
            int piv = col;
            for (int i = col + 1; i < 3; i++) if (Math.Abs(a[i, col]) > Math.Abs(a[piv, col])) piv = i;
            if (Math.Abs(a[piv, col]) < 1e-15) return new double[3];
            if (piv != col)
            {
                for (int j = 0; j < 3; j++) (a[col, j], a[piv, j]) = (a[piv, j], a[col, j]);
                (b[col], b[piv]) = (b[piv], b[col]);
            }
            for (int i = col + 1; i < 3; i++)
            {
                double f = a[i, col] / a[col, col];
                for (int j = col; j < 3; j++) a[i, j] -= f * a[col, j];
                b[i] -= f * b[col];
            }
        }
        var x = new double[3];
        for (int i = 2; i >= 0; i--)
        {
            double s = b[i];
            for (int j = i + 1; j < 3; j++) s -= a[i, j] * x[j];
            x[i] = s / a[i, i];
        }
        return x;
    }
}
