using System.Numerics;
using VibrationAnalysis.Core.Balancing;
using VibrationAnalysis.Core.Dsp;

namespace VibrationAnalysis.Core.Analysis;

public enum Precession { Forward, Reverse, Linear }

/// <summary>Órbita del eje a partir de dos sensores a 90° (X e Y), con parámetros de la elipse 1X.</summary>
public sealed record OrbitResult(double[] X, double[] Y, int[] Keyphasor, double SemiMajor, double SemiMinor, double TiltDeg,
    Precession Precession, Phasor X1, Phasor Y1)
{
    public string PrecessionText => Precession switch
    {
        Precession.Forward => "directa (sentido X → Y)",
        Precession.Reverse => "inversa (sentido Y → X)",
        _ => "lineal (órbita plana)",
    };

    /// <summary>Relación de ejes (menor/mayor): ≈ 0 indica precarga o desalineación (órbita aplanada), ≈ 1 órbita circular.</summary>
    public double AxisRatio => SemiMajor > 0 ? SemiMinor / SemiMajor : 0;
}

public static class OrbitAnalysis
{
    /// <summary>
    /// Órbita de <paramref name="revolutions"/> vueltas desde el primer pulso. Con <paramref name="filter1X"/> se reconstruye solo la
    /// componente 1X (órbita filtrada); si no, la señal cruda sin componente continua.
    /// Los parámetros de la elipse salen siempre de los fasores 1X: x + jy = F·e^{jθ} + B·e^{−jθ}.
    /// </summary>
    public static OrbitResult Compute(IReadOnlyList<double> x, IReadOnlyList<double> y, double fs, IReadOnlyList<double> pulses,
        bool filter1X = true, int revolutions = 3)
    {
        if (pulses.Count < 2) throw new ArgumentException("La órbita necesita el tacómetro (al menos 2 pulsos).");
        double rpm = Tachometer.Rpm(pulses);
        double f1 = rpm / 60, t0 = pulses[0];
        var (ax, px) = SpectrumAnalyzer.FitSinusoid(x, fs, f1, t0);
        var (ay, py) = SpectrumAnalyzer.FitSinusoid(y, fs, f1, t0);
        // Fasores complejos con la convención A·cos(θ − φ) = Re{A·e^{−jφ}·e^{jθ}}
        var X = Complex.FromPolarCoordinates(ax, -px * Math.PI / 180);
        var Y = Complex.FromPolarCoordinates(ay, -py * Math.PI / 180);
        var fwd = (X + Complex.ImaginaryOne * Y) / 2;
        var bwd = (Complex.Conjugate(X) + Complex.ImaginaryOne * Complex.Conjugate(Y)) / 2;
        double rf = fwd.Magnitude, rb = bwd.Magnitude;
        double major = rf + rb, minor = Math.Abs(rf - rb);
        double tilt = (fwd.Phase + bwd.Phase) / 2 * 180 / Math.PI;
        tilt = ((tilt % 180) + 180) % 180;
        var prec = minor < major * 0.02 ? Precession.Linear : rf > rb ? Precession.Forward : Precession.Reverse;

        int i0 = Math.Max(0, (int)Math.Ceiling(t0 * fs));
        int revs = Math.Max(1, Math.Min(revolutions, pulses.Count - 1));
        int i1 = Math.Min(x.Count - 1, (int)Math.Floor(pulses[revs] * fs));
        if (i1 <= i0) i1 = Math.Min(x.Count - 1, i0 + (int)(fs / Math.Max(f1, 1e-6)));
        int n = i1 - i0 + 1;
        var ox = new double[n];
        var oy = new double[n];
        if (filter1X)
        {
            for (int i = 0; i < n; i++)
            {
                double th = 2 * Math.PI * f1 * ((i0 + i) / fs - t0);
                ox[i] = ax * Math.Cos(th - px * Math.PI / 180);
                oy[i] = ay * Math.Cos(th - py * Math.PI / 180);
            }
        }
        else
        {
            double mx = x.Average(), my = y.Average();
            for (int i = 0; i < n; i++) { ox[i] = x[i0 + i] - mx; oy[i] = y[i0 + i] - my; }
        }
        var kp = pulses.Where(p => p * fs >= i0 && p * fs <= i1).Select(p => (int)Math.Round(p * fs) - i0).Where(k => k >= 0 && k < n).ToArray();
        return new OrbitResult(ox, oy, kp, major, minor, tilt, prec, new Phasor(ax, px), new Phasor(ay, py));
    }
}
