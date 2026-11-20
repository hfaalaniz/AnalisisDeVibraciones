using System.Numerics;
using VibrationAnalysis.Core.Analysis;
using VibrationAnalysis.Core.Balancing;
using VibrationAnalysis.Core.IO;

namespace VibrationAnalysis.Core.Simulation;

/// <summary>Masa agregada (positiva) o quitada (negativa) en un plano.</summary>
public sealed record PlaneMass(int Plane, double Grams, double AngleDeg, double RadiusMm);

/// <summary>Configuración del rotor virtual.</summary>
public sealed class SimulatorSettings
{
    public double Rpm { get; set; } = 1500;
    public double SampleRate { get; set; } = 5120;
    public int Samples { get; set; } = 8192;
    public int Sensors { get; set; } = 2;
    /// <summary>Desbalance inicial en el plano 1 (g·mm ∠ grados).</summary>
    public Phasor Unbalance1 { get; set; } = new(1800, 70);
    /// <summary>Desbalance inicial en el plano 2 (g·mm ∠ grados).</summary>
    public Phasor Unbalance2 { get; set; } = new(1100, 250);
    /// <summary>Sensibilidad directa (mm/s pico por cada 1000 g·mm) a 1500 rpm.</summary>
    public double Sensitivity { get; set; } = 1.6;
    /// <summary>Retraso de fase mecánico entre punto pesado y punto alto (grados).</summary>
    public double LagDeg { get; set; } = 60;
    /// <summary>Acoplamiento entre planos (0 = independientes, 1 = iguales).</summary>
    public double CrossCoupling { get; set; } = 0.35;
    public double NoiseMmS { get; set; } = 0.05;
    public double Misalignment { get; set; }
    public double Looseness { get; set; }
    public double BearingOuterRace { get; set; }
    public double OilWhirl { get; set; }
    public double Electrical { get; set; }
    public double LineFrequencyHz { get; set; } = 50;
    public BearingGeometry Bearing { get; set; } = new(9, 7.94, 39.0, 0);
    public int Seed { get; set; } = 12345;
    public List<PlaneMass> AddedMasses { get; } = new();
}

/// <summary>
/// Rotor virtual con comportamiento lineal: la vibración 1X de cada sensor es H·U, donde U es el desbalance de cada plano
/// (incluidos los pesos agregados) y H una matriz de influencia "oculta" que el balanceo debe descubrir con las corridas de prueba.
/// Así el procedimiento de balanceo se puede practicar de punta a punta y la corrección calculada realmente reduce la vibración.
/// </summary>
public sealed class RotorSimulator
{
    public SimulatorSettings Settings { get; }
    private readonly Random rng;

    public RotorSimulator(SimulatorSettings settings)
    {
        Settings = settings;
        rng = new Random(settings.Seed);
    }

    /// <summary>Matriz de influencia real (mm/s pico por g·mm) a la velocidad configurada.</summary>
    public Complex[,] TrueInfluence()
    {
        var s = Settings;
        double scale = s.Sensitivity / 1000 * Math.Pow(s.Rpm / 1500, 2);
        Complex P(double mag, double lag) => Complex.FromPolarCoordinates(scale * mag, lag * Math.PI / 180);
        return new Complex[2, 2]
        {
            { P(1, s.LagDeg), P(s.CrossCoupling, s.LagDeg + 35) },
            { P(s.CrossCoupling * 0.9, s.LagDeg + 20), P(0.85, s.LagDeg + 10) },
        };
    }

    /// <summary>Desbalance total de cada plano (g·mm) incluyendo pesos agregados.</summary>
    public (Complex U1, Complex U2) TotalUnbalance()
    {
        var u1 = Settings.Unbalance1.ToComplex();
        var u2 = Settings.Unbalance2.ToComplex();
        foreach (var m in Settings.AddedMasses)
        {
            var add = Complex.FromPolarCoordinates(m.Grams * m.RadiusMm, m.AngleDeg * Math.PI / 180);
            if (m.Plane == 1) u1 += add; else u2 += add;
        }
        if (Settings.Sensors == 1) { u1 += u2; u2 = Complex.Zero; }
        return (u1, u2);
    }

    /// <summary>Vibración 1X teórica (sin ruido) en cada sensor.</summary>
    public Phasor[] Expected1X()
    {
        var h = TrueInfluence();
        var (u1, u2) = TotalUnbalance();
        var v1 = h[0, 0] * u1 + h[0, 1] * u2;
        var v2 = h[1, 0] * u1 + h[1, 1] * u2;
        return Settings.Sensors == 1 ? new[] { Phasor.FromComplex(h[0, 0] * u1) } : new[] { Phasor.FromComplex(v1), Phasor.FromComplex(v2) };
    }

    /// <summary>Genera una adquisición de velocidad (mm/s) con canal de tacómetro (1 pulso por vuelta, en la marca de 0°).</summary>
    public Recording Acquire()
    {
        var s = Settings;
        int n = s.Samples;
        double fs = s.SampleRate;
        double f1 = s.Rpm / 60;
        var v1x = Expected1X();
        var chans = new List<Channel>();
        double startAngle = rng.NextDouble() * 2 * Math.PI; // la adquisición arranca en cualquier punto de la vuelta
        double t0 = (2 * Math.PI - startAngle) / (2 * Math.PI * f1); // primer pulso del tacómetro
        for (int c = 0; c < v1x.Length; c++)
        {
            var x = new double[n];
            double sideFactor = c == 0 ? 1 : 0.8;
            for (int i = 0; i < n; i++)
            {
                double t = i / fs - t0;
                double th = 2 * Math.PI * f1 * t;
                double v = v1x[c].Magnitude * Math.Cos(th - v1x[c].AngleDeg * Math.PI / 180);
                if (s.Misalignment > 0) v += s.Misalignment * sideFactor * (Math.Cos(2 * th - 1.1) + 0.35 * Math.Cos(3 * th - 2.0));
                if (s.Looseness > 0)
                {
                    for (int k = 2; k <= 8; k++) v += s.Looseness * sideFactor * 0.6 / Math.Sqrt(k) * Math.Cos(k * th - 0.7 * k);
                    v += s.Looseness * 0.35 * Math.Cos(0.5 * th + 0.4) + s.Looseness * 0.25 * Math.Cos(1.5 * th + 1.2);
                }
                if (s.OilWhirl > 0) v += s.OilWhirl * Math.Cos(0.45 * th + 0.3);
                if (s.Electrical > 0) v += s.Electrical * Math.Cos(2 * Math.PI * 2 * s.LineFrequencyHz * (i / fs));
                x[i] = v + s.NoiseMmS * Gauss();
            }
            if (s.BearingOuterRace > 0) AddBearingImpacts(x, fs, f1 * s.Bearing.BpfoOrder, s.BearingOuterRace * sideFactor);
            chans.Add(new Channel($"Sensor {c + 1}", Dsp.VibrationUnit.VelocityMmS, x));
        }
        var tach = new double[n];
        double period = 1 / f1, width = Math.Max(period * 0.03, 2 / fs);
        for (int i = 0; i < n; i++)
        {
            double t = i / fs - t0;
            double ph = t - Math.Floor(t / period) * period;
            tach[i] = ph < width ? 5.0 : 0.0;
        }
        return new Recording(fs, chans, tach, s.Rpm, "Simulador");
    }

    private void AddBearingImpacts(double[] x, double fs, double fDefect, double amp)
    {
        // Impactos periódicos que excitan una resonancia de 1,8 kHz amortiguada (en velocidad, amplitud reducida)
        double fr = Math.Min(1800, fs * 0.4), decay = 900;
        double period = 1 / fDefect;
        for (double tk = rng.NextDouble() * period; tk < x.Length / fs; tk += period * (1 + 0.01 * Gauss()))
        {
            int i0 = (int)(tk * fs);
            for (int i = i0; i < x.Length && i < i0 + (int)(fs * 0.01); i++)
            {
                double t = (i - i0) / fs;
                x[i] += amp * Math.Exp(-decay * t) * Math.Sin(2 * Math.PI * fr * t);
            }
        }
        // Componente discreta a BPFO (modulación), visible en el espectro de velocidad
        for (int i = 0; i < x.Length; i++) x[i] += amp * 0.25 * Math.Cos(2 * Math.PI * fDefect * i / fs);
    }

    private double Gauss()
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
