using VibrationAnalysis.Core.Analysis;
using VibrationAnalysis.Core.Dsp;
using VibrationAnalysis.Core.IO;
using VibrationAnalysis.Core.Model;

namespace VibrationAnalysis.Core.Trending;

/// <summary>Calcula los indicadores escalares de una medición para guardarlos y seguir su tendencia.</summary>
public static class FeatureExtractor
{
    public static MeasurementRecord Extract(Recording r, int channel, Machine machine, string pointId = "")
    {
        if (channel < 0 || channel >= r.Channels.Count) throw new ArgumentOutOfRangeException(nameof(channel));
        var ch = r.Channels[channel];
        double fs = r.SampleRate;
        double rpm = r.HasPhaseReference ? r.MeasuredRpm() : (r.NominalRpm > 0 ? r.NominalRpm : machine.Rpm);
        double f1 = rpm / 60;
        bool isAcc = ch.Unit is VibrationUnit.AccelerationG or VibrationUnit.AccelerationMs2;
        var vel = SignalTools.Convert(ch.Data, fs, ch.Unit, VibrationUnit.VelocityMmS, 10);
        var acc = SignalTools.Convert(ch.Data, fs, ch.Unit, VibrationUnit.AccelerationG, isAcc ? 0 : 2);
        var disp = SignalTools.Convert(ch.Data, fs, ch.Unit, VibrationUnit.DisplacementUm, Math.Max(2, f1 * 0.3));
        var f = new Dictionary<string, double>();

        var sv = SpectrumAnalyzer.Compute(vel, fs, WindowType.Hann);
        f[FeatureKeys.VelocityRms] = sv.BandRms(10, Math.Min(1000, fs / 2), sv.NoiseBandwidthBins);
        var sa = SignalTools.Statistics(acc);
        f[FeatureKeys.AccelerationRms] = sa.Rms;
        f[FeatureKeys.AccelerationPeak] = sa.Peak;
        f[FeatureKeys.CrestFactor] = sa.CrestFactor;
        f[FeatureKeys.Kurtosis] = sa.Kurtosis;
        f[FeatureKeys.DisplacementPp] = SignalTools.Statistics(disp).PeakToPeak;

        if (f1 > 0)
        {
            double tol = Math.Max(sv.Resolution * 1.5, f1 * 0.02);
            if (r.HasPhaseReference)
            {
                var p = Tachometer.DetectPulses(r.Tach!, fs);
                var (a, ph) = SpectrumAnalyzer.FitSinusoid(vel, fs, f1, p[0]);
                f[FeatureKeys.Amp1X] = a; f[FeatureKeys.Phase1X] = ph;
            }
            else f[FeatureKeys.Amp1X] = sv.PeakNear(f1, tol).Amplitude;
            f[FeatureKeys.Amp2X] = sv.PeakNear(2 * f1, tol).Amplitude;
            f[FeatureKeys.Amp3X] = sv.PeakNear(3 * f1, tol).Amplitude;
            double sub = 0;
            for (int i = 0; i < sv.Frequencies.Length; i++)
                if (sv.Frequencies[i] >= 0.3 * f1 && sv.Frequencies[i] <= 0.8 * f1) sub = Math.Max(sub, sv.Amplitudes[i]);
            f[FeatureKeys.SubSync] = sub;
            if (machine.GearTeeth > 1 && machine.GearTeeth * f1 < fs / 2) f[FeatureKeys.Gmf] = sv.PeakNear(machine.GearTeeth * f1, tol).Amplitude;
        }

        if (fs / 2 > 1200)
        {
            var sacc = SpectrumAnalyzer.Compute(acc, fs, WindowType.Hann);
            f[FeatureKeys.HighFrequency] = sacc.BandRms(1000, fs / 2, sacc.NoiseBandwidthBins);
        }

        try
        {
            double maxDefect = machine.Bearing != null ? machine.Bearing.BpfiOrder * f1 : 6 * f1;
            var (lo, hi) = AdvancedSpectrum.AutoEnvelopeBand(acc, fs, f1, maxDefect);
            var env = AdvancedSpectrum.Envelope(acc, fs, lo, hi);
            double m = env.Average(), s = 0;
            foreach (var v in env) s += (v - m) * (v - m);
            f[FeatureKeys.EnvelopeRms] = Math.Sqrt(s / env.Length);
            if (machine.Bearing != null && f1 > 0)
            {
                var es = SpectrumAnalyzer.Compute(env, fs, WindowType.Hann);
                double tol = Math.Max(es.Resolution * 1.5, f1 * 0.03);
                f[FeatureKeys.Bpfo] = es.PeakNear(machine.Bearing.BpfoOrder * f1, tol).Amplitude;
                f[FeatureKeys.Bpfi] = es.PeakNear(machine.Bearing.BpfiOrder * f1, tol).Amplitude;
            }
        }
        catch (ArgumentException) { /* señal demasiado corta o fs baja: sin envolvente */ }

        return new MeasurementRecord { PointId = pointId, Rpm = rpm, SampleRate = fs, Features = f, Timestamp = DateTime.Now };
    }
}
