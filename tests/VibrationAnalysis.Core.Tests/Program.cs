// Pruebas del núcleo de cálculo. Se ejecutan con:  dotnet run --project tests/VibrationAnalysis.Core.Tests
// Con "--emit <archivo.json>" además se generan vectores de referencia para verificar que la versión web calcule lo mismo.
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using VibrationAnalysis.Core.Acquisition;
using VibrationAnalysis.Core.Analysis;
using VibrationAnalysis.Core.Balancing;
using VibrationAnalysis.Core.Dsp;
using VibrationAnalysis.Core.IO;
using VibrationAnalysis.Core.Model;
using VibrationAnalysis.Core.Network;
using VibrationAnalysis.Core.Simulation;
using VibrationAnalysis.Core.Trending;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var t = new TestRunner();

t.Run("FFT radix-2 coincide con la DFT directa", () =>
{
    var x = Enumerable.Range(0, 64).Select(i => Math.Sin(i * 0.37) + 0.3 * Math.Cos(i * 1.9)).ToArray();
    var a = Fft.Forward(x); var d = NaiveDft(x);
    for (int k = 0; k < x.Length; k++) T.Near((a[k] - d[k]).Magnitude, 0, 1e-9, $"bin {k}");
});
t.Run("FFT Bluestein (n=1000) coincide con la DFT directa", () =>
{
    var x = Enumerable.Range(0, 1000).Select(i => Math.Sin(i * 0.11) + 0.5 * Math.Cos(i * 0.73)).ToArray();
    var a = Fft.Forward(x); var d = NaiveDft(x);
    double err = 0; for (int k = 0; k < x.Length; k++) err = Math.Max(err, (a[k] - d[k]).Magnitude);
    T.Near(err, 0, 1e-7, "error máximo");
});
t.Run("FFT inversa recupera la señal", () =>
{
    var x = Enumerable.Range(0, 300).Select(i => Math.Sin(i * 0.2)).ToArray();
    var a = Fft.Forward(x); Fft.Transform(a, true);
    for (int i = 0; i < x.Length; i++) T.Near(a[i].Real, x[i], 1e-9, $"muestra {i}");
});
t.Run("Espectro: amplitud pico correcta con Hanning (frecuencia centrada)", () =>
{
    double fs = 1024; int n = 1024;
    var x = Enumerable.Range(0, n).Select(i => 3.0 * Math.Sin(2 * Math.PI * 50 * i / fs)).ToArray();
    var s = SpectrumAnalyzer.Compute(x, fs, WindowType.Hann);
    T.Near(s.PeakNear(50, 1).Amplitude, 3.0, 0.01, "amplitud");
    T.Near(s.Resolution, 1.0, 1e-12, "resolución");
});
t.Run("Espectro: flat-top mide bien la amplitud fuera de línea", () =>
{
    double fs = 1000; int n = 1000;
    var x = Enumerable.Range(0, n).Select(i => 2.0 * Math.Cos(2 * Math.PI * 50.5 * i / fs)).ToArray();
    var s = SpectrumAnalyzer.Compute(x, fs, WindowType.FlatTop);
    T.Near(s.PeakNear(50.5, 2).Amplitude, 2.0, 0.02, "amplitud");
});
t.Run("Ajuste sinusoidal: amplitud y retraso de fase exactos", () =>
{
    double fs = 2000, f = 24.7, amp = 4.2, lag = 133;
    var x = Enumerable.Range(0, 3001).Select(i => amp * Math.Cos(2 * Math.PI * f * (i / fs - 0.013) - lag * Math.PI / 180) + 0.5).ToArray();
    var (a, p) = SpectrumAnalyzer.FitSinusoid(x, fs, f, 0.013);
    T.Near(a, amp, 1e-9, "amplitud"); T.Near(p, lag, 1e-7, "fase");
});
t.Run("Integración: 1 g a 50 Hz ⇒ 31,2 mm/s pico", () =>
{
    double fs = 5000; int n = 5000;
    var acc = Enumerable.Range(0, n).Select(i => Math.Sin(2 * Math.PI * 50 * i / fs)).ToArray();
    var v = SignalTools.Convert(acc, fs, VibrationUnit.AccelerationG, VibrationUnit.VelocityMmS);
    double expected = SignalTools.G / (2 * Math.PI * 50) * 1000;
    T.Near(SignalTools.Statistics(v).Rms * Math.Sqrt(2), expected, expected * 0.01, "velocidad pico");
    var d = SignalTools.Convert(v, fs, VibrationUnit.VelocityMmS, VibrationUnit.DisplacementUm);
    T.Near(SignalTools.Statistics(d).Rms * Math.Sqrt(2), expected / (2 * Math.PI * 50) * 1000, expected / 314 * 1000 * 0.01, "desplazamiento pico (µm)");
});
t.Run("Tacómetro: detecta pulsos y rpm", () =>
{
    var sim = new RotorSimulator(new SimulatorSettings { Rpm = 1780 });
    var r = sim.Acquire();
    T.Near(r.MeasuredRpm(), 1780, 1.0, "rpm");
});
t.Run("Severidad ISO 10816-3 grupo 2 rígido", () =>
{
    T.Eq(Severity.Evaluate(1.0, MachineClass.Iso10816_3_Group2Rigid).Zone, SeverityZone.A, "1,0");
    T.Eq(Severity.Evaluate(2.0, MachineClass.Iso10816_3_Group2Rigid).Zone, SeverityZone.B, "2,0");
    T.Eq(Severity.Evaluate(3.5, MachineClass.Iso10816_3_Group2Rigid).Zone, SeverityZone.C, "3,5");
    T.Eq(Severity.Evaluate(4.5, MachineClass.Iso10816_3_Group2Rigid).Zone, SeverityZone.D, "4,5");
});
t.Run("Balanceo 1 plano: la corrección anula el desbalance", () =>
{
    var alpha = Complex.FromPolarCoordinates(0.004, 1.1); // mm/s por g
    var u = Complex.FromPolarCoordinates(60, 2.0);         // desbalance equivalente en g
    var o = Phasor.FromComplex(alpha * u);
    var trial = new Phasor(25, 30);
    var r = Phasor.FromComplex(alpha * (u + trial.ToComplex()));
    var res = InfluenceBalancer.SinglePlane(o, trial, r);
    T.Near((res.Correction.ToComplex() + u).Magnitude, 0, 1e-9, "W + U");
    T.Near(Phasor.FromComplex(res.InfluenceCoefficient - alpha).Magnitude, 0, 1e-12, "α");
    T.Near((res.CorrectionKeepingTrial.ToComplex() + trial.ToComplex() + u).Magnitude, 0, 1e-9, "dejando el de prueba");
    T.True(res.Check.Ok, "corrida válida");
});
t.Run("Balanceo 2 planos: la corrección anula ambos sensores", () =>
{
    var h = new Complex[2, 2] { { Complex.FromPolarCoordinates(0.003, 0.9), Complex.FromPolarCoordinates(0.001, 1.6) }, { Complex.FromPolarCoordinates(0.0012, 1.3), Complex.FromPolarCoordinates(0.0028, 1.0) } };
    var u1 = Complex.FromPolarCoordinates(80, 0.4); var u2 = Complex.FromPolarCoordinates(50, 3.9);
    Phasor V(int s, Complex a, Complex b) => Phasor.FromComplex(h[s, 0] * a + h[s, 1] * b);
    var t1 = new Phasor(30, 0); var t2 = new Phasor(30, 90);
    var res = InfluenceBalancer.TwoPlanes(V(0, u1, u2), V(1, u1, u2), t1, V(0, u1 + t1.ToComplex(), u2), V(1, u1 + t1.ToComplex(), u2), t2, V(0, u1, u2 + t2.ToComplex()), V(1, u1, u2 + t2.ToComplex()));
    T.Near((res.CorrectionPlane1.ToComplex() + u1).Magnitude, 0, 1e-8, "plano 1");
    T.Near((res.CorrectionPlane2.ToComplex() + u2).Magnitude, 0, 1e-8, "plano 2");
    T.True(res.ConditionNumber < 10, "condición");
});
t.Run("Simulador + sesión guiada en 2 planos reduce la vibración > 90 %", () => SimulatedSession(2, true));
t.Run("Simulador + sesión guiada en 1 plano reduce la vibración > 90 %", () => SimulatedSession(1, true));
t.Run("Ángulos en el sentido de giro también funcionan", () => SimulatedSession(2, false));
t.Run("La sesión impone el orden de los pasos", () =>
{
    var s = new BalancingSession(1);
    T.Throws<InvalidOperationException>(() => s.RecordTrial1(new Phasor(10, 0), new Phasor(1, 0), null, 1500), "prueba antes de inicial");
    s.RecordInitial(new Phasor(5, 10), null, 1500);
    T.Throws<InvalidOperationException>(() => s.RecordInitial(new Phasor(5, 10), null, 1500), "inicial dos veces");
    T.Throws<InvalidOperationException>(() => s.RecordVerification(new Phasor(1, 0), null, 1500), "verificación antes de corrección");
});
t.Run("Reparto entre posiciones fijas conserva el vector", () =>
{
    var w = new Phasor(37, 100);
    var sp = WeightSplitter.Split(w, 8, 0);
    var sum = new Phasor(sp.MassA, sp.AngleA) + new Phasor(sp.MassB, sp.AngleB);
    T.Near((sum - w).Magnitude, 0, 1e-9, "suma");
    T.Eq(sp.PosA, 2, "posición A"); T.Eq(sp.PosB, 3, "posición B");
});
t.Run("Perforación: el volumen quitado coincide con la masa", () =>
{
    double depth = DrillCalculator.DepthMm(12, 7850, 10);
    double r = 0.005, tip = r / Math.Tan(59 * Math.PI / 180), d = depth / 1000;
    double vol = Math.PI * r * r * tip / 3 + Math.PI * r * r * (d - tip);
    T.Near(vol * 7850 * 1000, 12, 1e-6, "gramos");
});
t.Run("ISO 1940: desbalance admisible G6,3, 100 kg, 1500 rpm", () =>
{
    T.Near(BalanceQuality.PermissibleUnbalanceGmm(6.3, 100, 1500), 9549 * 6.3 * 100 / 1500, 1e-9, "Uper");
    T.Near(BalanceQuality.PermissibleEccentricityUm(6.3, 1500), 40.1058, 1e-3, "eper");
});
t.Run("Peso de prueba sugerido (10 % del peso del rotor)", () =>
{
    double g = TrialWeight.SuggestGrams(100, 1500, 200);
    double w = 1500 * 2 * Math.PI / 60;
    T.Near(g / 1000 * 0.2 * w * w, 0.1 * 100 * SignalTools.G, 1e-6, "fuerza");
});
t.Run("CSV: lectura con tiempo y tacómetro, ida y vuelta", () =>
{
    var r = new RotorSimulator(new SimulatorSettings { Samples = 2048 }).Acquire();
    var text = CsvRecordingReader.Write(r);
    var back = CsvRecordingReader.Parse(text);
    T.Near(back.SampleRate, r.SampleRate, 0.5, "fs");
    T.Eq(back.Channels.Count, 2, "canales");
    T.True(back.Tach != null, "tacómetro");
    T.Near(back.Reading1X(0).Magnitude, r.Reading1X(0).Magnitude, 0.01, "1X");
});
t.Run("CSV: punto y coma con coma decimal y cabecera # fs", () =>
{
    var sb = new StringBuilder("# fs=1000 unit=g\nA;B\n");
    for (int i = 0; i < 100; i++) sb.Append($"{Math.Sin(i * 0.1).ToString("0.000", CultureInfo.GetCultureInfo("es-AR"))};0,5\n");
    var r = CsvRecordingReader.Parse(sb.ToString());
    T.Near(r.SampleRate, 1000, 1e-9, "fs"); T.Eq(r.Channels[0].Unit, VibrationUnit.AccelerationG, "unidad"); T.Near(r.Channels[1].Data[3], 0.5, 1e-12, "valor");
});
t.Run("WAV PCM 16 bits estéreo", () =>
{
    var wav = MakeWav16(8000, 2, 800, (c, i) => c == 0 ? 0.5 * Math.Sin(2 * Math.PI * 100 * i / 8000.0) : 0.25);
    var r = WavRecordingReader.Read(wav, 2.0, VibrationUnit.AccelerationG);
    T.Eq(r.SampleRate, 8000.0, "fs"); T.Eq(r.Channels.Count, 2, "canales");
    T.Near(r.Channels[1].Data[10], 0.5, 1e-3, "escala");
});
t.Run("Protocolo serie", () =>
{
    var p = new SerialStreamParser();
    p.Feed("basura\n#VA fs=2000 ch=2 unit=g tach=1\n0.1,0.2,0\n0.3,0.4,5\n0.5,");
    p.Feed("0.6,0\n");
    var r = p.ToRecording();
    T.Eq(r.SampleRate, 2000.0, "fs"); T.Eq(r.Length, 3, "muestras"); T.Near(r.Channels[1].Data[2], 0.6, 1e-12, "dato"); T.True(r.Tach != null, "tach");
});
t.Run("Diagnóstico: desbalance puro", () =>
{
    var r = new RotorSimulator(new SimulatorSettings { Sensors = 1 }).Acquire();
    var f = Diagnose(r);
    T.Eq(f[0].Type, FaultType.Unbalance, "primera conclusión");
});
t.Run("Diagnóstico: desalineación", () =>
{
    var s = new SimulatorSettings { Sensors = 1, Unbalance1 = new Phasor(300, 0), Unbalance2 = new Phasor(0, 0), Misalignment = 3 };
    var f = Diagnose(new RotorSimulator(s).Acquire());
    T.True(f.Any(x => x.Type == FaultType.Misalignment), "desalineación");
});
t.Run("Diagnóstico: holgura", () =>
{
    var s = new SimulatorSettings { Sensors = 1, Unbalance1 = new Phasor(500, 0), Unbalance2 = new Phasor(0, 0), Looseness = 2 };
    var f = Diagnose(new RotorSimulator(s).Acquire());
    T.True(f.Any(x => x.Type == FaultType.MechanicalLooseness), "holgura");
});
t.Run("Diagnóstico: rodamiento pista externa", () =>
{
    var s = new SimulatorSettings { Sensors = 1, Unbalance1 = new Phasor(400, 0), Unbalance2 = new Phasor(0, 0), BearingOuterRace = 2 };
    var r = new RotorSimulator(s).Acquire();
    var f = Diagnose(r, s.Bearing);
    T.True(f.Any(x => x.Type == FaultType.BearingOuterRace), "BPFO");
});
t.Run("Masa del rotor y recomendación de planos", () =>
{
    var m = new Machine { DiameterMm = 300, LengthMm = 100, Material = "Acero", Rpm = 3000 };
    T.Near(m.EstimatedMassKg(), Math.PI * 0.15 * 0.15 * 0.1 * 7850, 1e-9, "masa");
    T.Eq(m.RecommendedPlanes().Planes, 1, "disco");
    m.LengthMm = 900; T.Eq(m.RecommendedPlanes().Planes, 2, "largo");
});

// ───────────── Procesamiento avanzado ─────────────
t.Run("Butterworth: −3 dB en el corte, pasabanda con ganancia 1 al centro", () =>
{
    var lp = IirFilter.Butterworth(FilterKind.LowPass, 4, 1000, 100);
    T.Near(lp.Gain(100), Math.Sqrt(0.5), 0.005, "corte"); T.Near(lp.Gain(10), 1, 0.001, "banda pasante"); T.True(lp.Gain(400) < 0.01, "rechazo");
    var bp = IirFilter.Butterworth(FilterKind.BandPass, 4, 20000, 2000, 4000);
    T.Near(bp.Gain(Math.Sqrt(2000 * 4000)), 1, 0.05, "centro"); T.True(bp.Gain(200) < 1e-3 && bp.Gain(9000) < 0.05, "fuera de banda");
});
t.Run("FiltFilt: fase cero en la banda pasante", () =>
{
    double fs = 1000; var x = Enumerable.Range(0, 2000).Select(i => Math.Sin(2 * Math.PI * 20 * i / fs)).ToArray();
    var y = IirFilter.Butterworth(FilterKind.LowPass, 4, fs, 200).FiltFilt(x);
    for (int i = 200; i < 1800; i++) T.Near(y[i], x[i], 0.01, $"muestra {i}");
});
t.Run("Ventanas Blackman-Harris y Kaiser: amplitud correcta", () =>
{
    double fs = 1024; var x = Enumerable.Range(0, 1024).Select(i => 3.0 * Math.Sin(2 * Math.PI * 50 * i / fs)).ToArray();
    foreach (var w in new[] { WindowType.BlackmanHarris, WindowType.Kaiser })
        T.Near(SpectrumAnalyzer.Compute(x, fs, w).PeakNear(50, 1).Amplitude, 3.0, 0.03, WindowFunctions.DisplayName(w));
});
t.Run("Welch: promedio lineal, exponencial y retención de picos", () =>
{
    var rnd = new Random(7); double fs = 2048;
    var x = Enumerable.Range(0, 16384).Select(i => 3.0 * Math.Sin(2 * Math.PI * 64 * i / fs) + 0.5 * Gauss(rnd)).ToArray();
    var lin = AdvancedSpectrum.Averaged(x, fs, 2048, 0.5, WindowType.Hann, AveragingMode.Linear);
    T.Eq(lin.Averages, 15, "promedios"); T.Near(lin.PeakNear(64, 1).Amplitude, 3.0, 0.08, "amplitud");
    var pk = AdvancedSpectrum.Averaged(x, fs, 2048, 0.5, WindowType.Hann, AveragingMode.PeakHold);
    T.True(pk.Amplitudes.Zip(lin.Amplitudes).All(p => p.First >= p.Second * 0.999), "pico ≥ promedio");
    var ex = AdvancedSpectrum.Averaged(x, fs, 2048, 0.5, WindowType.Hann, AveragingMode.Exponential);
    T.Near(ex.PeakNear(64, 1).Amplitude, 3.0, 0.1, "exponencial");
});
t.Run("PSD: ruido blanco integra a su varianza", () =>
{
    var rnd = new Random(3); double fs = 1000, sigma = 2;
    var x = Enumerable.Range(0, 65536).Select(_ => sigma * Gauss(rnd)).ToArray();
    var p = AdvancedSpectrum.Psd(x, fs, 1024, 0.5);
    T.Near(p.BandRms(0, fs / 2), sigma, 0.06, "RMS");
    T.Near(p.Psd.Skip(5).Take(400).Average(), 2 * sigma * sigma / fs, 2 * sigma * sigma / fs * 0.08, "nivel");
});
t.Run("Zoom FFT: amplitud exacta y separa líneas a 0,7 Hz", () =>
{
    double fs = 4096; int n = 65536;
    var x = Enumerable.Range(0, n).Select(i => 2.0 * Math.Cos(2 * Math.PI * 120.3 * i / fs) + 1.0 * Math.Cos(2 * Math.PI * 121.0 * i / fs + 1) + 0.8 * Math.Cos(2 * Math.PI * 25 * i / fs)).ToArray();
    var zf = AdvancedSpectrum.Zoom(x, fs, 120, 4, WindowType.FlatTop);
    T.Near(zf.PeakNear(120.3, 0.2).Amplitude, 2.0, 0.04, "amplitud 120,3");
    T.True(zf.Frequencies[0] >= 118 - 1e-9 && zf.Frequencies[^1] <= 122 + 1e-9, "banda");
    var zh = AdvancedSpectrum.Zoom(x, fs, 120, 4, WindowType.Hann);
    var pk = SpectralFeatures.Peaks(zh, 2);
    T.True(pk.Any(q => Math.Abs(q.Frequency - 120.3) < 0.05) && pk.Any(q => Math.Abs(q.Frequency - 121.0) < 0.05), "dos líneas");
});
t.Run("Cepstrum: familia de armónicos de 25 Hz ⇒ pico en 40 ms", () =>
{
    var rnd = new Random(5); double fs = 5000; int n = 16384;
    var x = new double[n];
    for (int k = 1; k <= 60; k++)
    {
        double g = 1 / (1 + Math.Pow((25 * k - 600) / 250.0, 2)), ph = rnd.NextDouble() * 6.28;
        for (int i = 0; i < n; i++) x[i] += g * Math.Cos(2 * Math.PI * 25 * k * i / fs + ph);
    }
    for (int i = 0; i < n; i++) x[i] += 0.01 * Gauss(rnd);
    var c = AdvancedSpectrum.Cepstrum(x, fs);
    T.Near(c.Dominant(0.005, 0.2).Quefrency, 0.04, 0.0005, "quefrencia");
    T.True(c.FamilyProminence(0.04) > 3, "prominencia");
    T.True(c.FamilyProminence(0.0317) < c.FamilyProminence(0.04) / 3, "período falso");
});
t.Run("Hilbert: envolvente de una señal modulada", () =>
{
    double fs = 4000; int n = 4000;
    var x = Enumerable.Range(0, n).Select(i => (1 + 0.5 * Math.Cos(2 * Math.PI * 5 * i / fs)) * Math.Cos(2 * Math.PI * 400 * i / fs)).ToArray();
    var a = AdvancedSpectrum.Analytic(x);
    for (int i = 200; i < n - 200; i += 7) T.Near(a[i].Magnitude, 1 + 0.5 * Math.Cos(2 * Math.PI * 5 * i / fs), 0.01, $"muestra {i}");
});
t.Run("Envolvente y curtosis espectral: defecto de rodamiento a 87,3 Hz", () =>
{
    var (x, fs) = BearingSignal(87.3);
    var sk = AdvancedSpectrum.SpectralKurtosis(x, fs);
    var band = sk.SuggestBand(1500, 500);
    double center = (band.Low + band.High) / 2;
    T.True(center > 2000 && center < 4000, $"banda sugerida {band.Low:0}–{band.High:0}");
    var env = AdvancedSpectrum.EnvelopeSpectrum(x, fs, band.Low, band.High);
    T.Near(SpectralFeatures.Peaks(env, 1, 5)[0].Frequency, 87.3, 0.5, "pico de la envolvente");
    T.True(SpectralFeatures.DefectHarmonics(env, 87.3).Count >= 3, "armónicos");
    var auto = AdvancedSpectrum.AutoEnvelopeBand(x, fs, 25, 87.3);
    T.True((auto.Low + auto.High) / 2 > 2000 && (auto.Low + auto.High) / 2 < 4000, $"banda automática {auto.Low:0}–{auto.High:0}");
    var plain = SpectrumAnalyzer.Compute(x, fs);
    T.True(SpectralFeatures.Peaks(plain, 1, 5)[0].Frequency < 30, "en el espectro común domina 1X");
});
t.Run("Armónicos, bandas laterales y picos", () =>
{
    double fs = 2000; int n = 4000;
    var x = Enumerable.Range(0, n).Select(i => { double t2 = i / fs; return 3 * Math.Cos(2 * Math.PI * 20 * t2) + 1 * Math.Cos(2 * Math.PI * 40 * t2) + 2 * Math.Cos(2 * Math.PI * 300 * t2) + 0.5 * Math.Cos(2 * Math.PI * 280 * t2) + 0.5 * Math.Cos(2 * Math.PI * 320 * t2); }).ToArray();
    var sp = SpectrumAnalyzer.Compute(x, fs, WindowType.FlatTop);
    var h = SpectralFeatures.Harmonics(sp, 20, 2);
    T.Near(h[0].Amplitude, 3, 0.05, "1X"); T.Near(h[1].Amplitude, 1, 0.05, "2X");
    var sb = SpectralFeatures.Sidebands(sp, 300, 20, 1);
    T.Near(sb.First(b => b.N == -1).Amplitude, 0.5, 0.03, "inferior"); T.Near(sb.First(b => b.N == 1).Amplitude, 0.5, 0.03, "superior");
    T.Near(SpectralFeatures.Peaks(sp, 1, 0, 0.01, 20)[0].Order, 1, 0.01, "orden del pico mayor");
});
t.Run("Diagnóstico: rodamiento confirmado por envolvente", () =>
{
    var s = new SimulatorSettings { Sensors = 1, Unbalance1 = new Phasor(400, 0), Unbalance2 = new Phasor(0, 0), BearingOuterRace = 0.8 };
    var r = new RotorSimulator(s).Acquire();
    var acc = SignalTools.Convert(r.Channels[0].Data, r.SampleRate, VibrationUnit.VelocityMmS, VibrationUnit.AccelerationG, 2);
    var band = AdvancedSpectrum.AutoEnvelopeBand(acc, r.SampleRate, 25, s.Bearing.BpfiOrder * 25);
    T.True(band.Low < 2100 && band.High > 1500, $"banda automática {band.Low:0}–{band.High:0} cerca de la resonancia");
    var env = AdvancedSpectrum.EnvelopeSpectrum(acc, r.SampleRate, band.Low, band.High);
    var sp = SpectrumAnalyzer.Compute(r.Channels[0].Data, r.SampleRate);
    var f = FaultDiagnosis.Diagnose(new DiagnosisInput { VelocitySpectrum = sp, RunningSpeedHz = r.MeasuredRpm() / 60, Bearing = s.Bearing, EnvelopeSpectrum = env });
    var b = f.FirstOrDefault(x => x.Type == FaultType.BearingOuterRace);
    T.True(b != null && b.Evidence.Contains("Envolvente"), "BPFO por envolvente");
    T.True(!f.Any(x => x.Type == FaultType.BearingInnerRace), "sin BPFI falso");
});
t.Run("Diagnóstico: engranaje con bandas laterales a ±1X", () =>
{
    var s = new SimulatorSettings { Sensors = 1, Unbalance1 = new Phasor(300, 0), Unbalance2 = new Phasor(0, 0), GearTeeth = 23, GearWear = 1.5 };
    var r = new RotorSimulator(s).Acquire();
    var sp = SpectrumAnalyzer.Compute(r.Channels[0].Data, r.SampleRate);
    var f = FaultDiagnosis.Diagnose(new DiagnosisInput { VelocitySpectrum = sp, RunningSpeedHz = r.MeasuredRpm() / 60, GearTeeth = 23 });
    T.True(f.Any(x => x.Type == FaultType.GearMesh), "GMF");
    var sp0 = SpectrumAnalyzer.Compute(new RotorSimulator(new SimulatorSettings { Sensors = 1 }).Acquire().Channels[0].Data, r.SampleRate);
    T.True(!FaultDiagnosis.Diagnose(new DiagnosisInput { VelocitySpectrum = sp0, RunningSpeedHz = 25, GearTeeth = 23 }).Any(x => x.Type == FaultType.GearMesh), "sin falso positivo");
});

t.Run("Catálogo de frecuencias: identificación de picos", () =>
{
    var b = new BearingGeometry(9, 7.94, 39.0, 0);
    var m = FrequencyCatalog.Build(25, b, 0, 23, 50);
    T.Eq(FrequencyCatalog.Identify(50.1, m, 0.5), "2X", "2X");
    T.Eq(FrequencyCatalog.Identify(25 * b.BpfoOrder, m, 0.5), "BPFO", "BPFO");
    T.Eq(FrequencyCatalog.Identify(23 * 25 + 25, m, 0.5), "GMF+1X", "banda lateral");
    T.Eq(FrequencyCatalog.Identify(333, m, 0.5), "", "sin rótulo");
});

// ───────────── Órdenes y transitorios ─────────────
t.Run("Espectro de órdenes con velocidad variable (10 → 30 Hz)", () =>
{
    var (x, tach, fs) = Chirp(10, 30, 4, 5000, (th, i) => 2 * Math.Cos(th) + Math.Cos(3 * th - 40 * Math.PI / 180));
    var pulses = Tachometer.DetectPulses(tach, fs);
    var os = OrderTracking.OrderSpectrum(x, fs, pulses, 10, WindowType.FlatTop);
    T.Near(os.PeakNear(1, 0.1).Amplitude, 2, 0.06, "orden 1"); T.Near(os.PeakNear(3, 0.1).Amplitude, 1, 0.04, "orden 3");
    var plain = SpectrumAnalyzer.Compute(x, fs, WindowType.FlatTop);
    T.True(plain.Amplitudes.Max() < 1.4, $"en frecuencia la energía se reparte: {plain.Amplitudes.Max():0.00}");
});
t.Run("Seguimiento 1X: amplitud y fase exactas en una rampa", () =>
{
    var (x, tach, fs) = Chirp(8, 25, 5, 5000, (th, i) => 2 * Math.Cos(th - 70 * Math.PI / 180) + 0.5 * Math.Cos(2 * th));
    var tr = OrderTracking.Track(x, fs, Tachometer.DetectPulses(tach, fs), 1, 4);
    T.True(tr.Points.Count > 20, "puntos");
    foreach (var p in tr.Points) { T.Near(p.Amplitude, 2, 0.03, $"amplitud a {p.Rpm:0} rpm"); T.Near(Angles.Difference(p.PhaseLagDeg, 70), 0, 1.5, $"fase a {p.Rpm:0} rpm"); }
    T.True(tr.Points[^1].Rpm > tr.Points[0].Rpm * 2.5, "la velocidad sube");
    var h2 = OrderTracking.Track(x, fs, Tachometer.DetectPulses(tach, fs), 2, 4);
    T.Near(h2.Points.Average(p => p.Amplitude), 0.5, 0.02, "2X");
});
t.Run("Seguimiento 1X coincide con la lectura 1X en régimen", () =>
{
    var r = new RotorSimulator(new SimulatorSettings { Sensors = 1, NoiseMmS = 0.02 }).Acquire();
    var tr = OrderTracking.Track(r.Channels[0].Data, r.SampleRate, Tachometer.DetectPulses(r.Tach!, r.SampleRate), 1, 8);
    var rd = r.Reading1X(0);
    T.Near(tr.Points.Average(p => p.Amplitude), rd.Magnitude, rd.Magnitude * 0.02, "amplitud");
    T.Near(Angles.Difference(tr.Points[0].PhaseLagDeg, rd.AngleDeg), 0, 3, "fase");
});
t.Run("Arranque simulado: velocidad crítica, giro de fase y factor Q", () =>
{
    var s = new SimulatorSettings { Sensors = 1, RunUp = true, RpmStart = 300, RpmEnd = 3000, CriticalRpm = 1800, Damping = 0.08, Samples = 65536, NoiseMmS = 0.02 };
    var r = new RotorSimulator(s).Acquire();
    var pulses = Tachometer.DetectPulses(r.Tach!, r.SampleRate);
    var tr = OrderTracking.Track(r.Channels[0].Data, r.SampleRate, pulses, 1, 4);
    var cs = OrderTracking.CriticalSpeeds(tr);
    T.Eq(cs.Count, 1, "una crítica");
    T.Near(cs[0].Rpm, 1800, 90, "rpm crítica");
    T.True(cs[0].PhaseShiftDeg > 60, $"giro de fase {cs[0].PhaseShiftDeg:0}°");
    T.Near(cs[0].AmplificationFactor, 1 / (2 * 0.08), 2.5, "Q");
    var cas = OrderTracking.Cascade(r.Channels[0].Data, r.SampleRate, 2048, 0.5, WindowType.Hann, pulses);
    T.True(cas.Count > 20 && cas[^1].Rpm > cas[0].Rpm * 3, "cascada con rpm creciente");
    var sg = OrderTracking.Spectrogram(r.Channels[0].Data, r.SampleRate, 1024, 0.75, WindowType.Hann, pulses, 200);
    T.Eq(sg.Amplitudes.GetLength(0), sg.Times.Length, "filas"); T.True(sg.Frequencies[^1] <= 200, "frecuencia máxima");
});
t.Run("Órbita X-Y: elipse 0,6 con precesión directa", () =>
{
    var r = new RotorSimulator(new SimulatorSettings { Sensors = 2, ProbesXY = true, NoiseMmS = 0.01 }).Acquire();
    var o = OrbitAnalysis.Compute(r.Channels[0].Data, r.Channels[1].Data, r.SampleRate, Tachometer.DetectPulses(r.Tach!, r.SampleRate));
    T.Near(o.AxisRatio, 0.6, 0.02, "relación de ejes"); T.Eq(o.Precession, Precession.Forward, "precesión");
    T.True(o.Keyphasor.Length >= 2 && o.X.Length == o.Y.Length, "marcas de fase");
    var inv = OrbitAnalysis.Compute(r.Channels[1].Data, r.Channels[0].Data, r.SampleRate, Tachometer.DetectPulses(r.Tach!, r.SampleRate));
    T.Eq(inv.Precession, Precession.Reverse, "al cambiar X por Y se invierte");
});
t.Run("Desenvolvimiento de fase", () =>
{
    var u = OrderTracking.Unwrap(new[] { 350.0, 5, 20, 355, 340 });
    T.Near(u[1], 365, 1e-9, "salto 350→5"); T.Near(u[3], 355, 1e-9, "vuelta"); T.Near(u[4], 340, 1e-9, "baja");
});

// ───────────── Tendencias y alarmas ─────────────
t.Run("Indicadores de una medición", () =>
{
    var r = new RotorSimulator(new SimulatorSettings { Sensors = 1 }).Acquire();
    var m = FeatureExtractor.Extract(r, 0, new Machine { Rpm = 1500, Bearing = new BearingGeometry(9, 7.94, 39, 0) }, "p1");
    T.Near(m.Get(FeatureKeys.Amp1X), r.Reading1X(0).Magnitude, r.Reading1X(0).Magnitude * 0.02, "1X");
    T.Near(m.Rpm, 1500, 1, "rpm");
    foreach (var k in new[] { FeatureKeys.VelocityRms, FeatureKeys.AccelerationRms, FeatureKeys.Kurtosis, FeatureKeys.EnvelopeRms, FeatureKeys.Bpfo, FeatureKeys.HighFrequency })
        T.True(!double.IsNaN(m.Get(k)), k);
    var sp = SpectrumAnalyzer.Compute(SignalTools.Convert(r.Channels[0].Data, r.SampleRate, VibrationUnit.VelocityMmS, VibrationUnit.VelocityMmS, 10), r.SampleRate);
    T.Near(m.Get(FeatureKeys.VelocityRms), sp.BandRms(10, 1000, sp.NoiseBandwidthBins), 0.01, "velocidad RMS");
});
t.Run("Alarmas: base ×2/×4, ISO 20816 y estadísticas", () =>
{
    var pt = new MeasurementPoint();
    MeasurementRecord R(double v1x, double vel, double acc, int day) => new() { PointId = pt.Id, Timestamp = new DateTime(2026, 1, 1).AddDays(day), Features = { [FeatureKeys.Amp1X] = v1x, [FeatureKeys.VelocityRms] = vel, [FeatureKeys.AccelerationRms] = acc } };
    var hist = new List<MeasurementRecord> { R(1.0, 1.0, 0.10, 0), R(1.1, 1.1, 0.11, 30), R(0.9, 1.0, 0.09, 60) };
    AlarmLevel L(MeasurementRecord r, string k) => TrendAnalysis.Evaluate(r, pt, MachineClass.Iso10816_3_Group2Rigid, hist).First(a => a.Feature == k).Level;
    T.Eq(L(R(1.5, 1, 0.1, 90), FeatureKeys.Amp1X), AlarmLevel.Normal, "1X normal");
    T.Eq(L(R(2.5, 1, 0.1, 90), FeatureKeys.Amp1X), AlarmLevel.Alert, "1X alerta");
    T.Eq(L(R(4.5, 1, 0.1, 90), FeatureKeys.Amp1X), AlarmLevel.Danger, "1X peligro");
    T.Eq(L(R(1, 3.0, 0.1, 90), FeatureKeys.VelocityRms), AlarmLevel.Alert, "ISO zona C");
    T.Eq(L(R(1, 4.6, 0.1, 90), FeatureKeys.VelocityRms), AlarmLevel.Danger, "ISO zona D");
    T.Eq(L(R(1, 1, 0.14, 90), FeatureKeys.AccelerationRms), AlarmLevel.Alert, "estadística (media + 3σ)");
    var worst = TrendAnalysis.WorstLevel(TrendAnalysis.Evaluate(R(4.5, 3, 0.1, 90), pt, MachineClass.Iso10816_3_Group2Rigid, hist));
    T.Eq(worst, AlarmLevel.Danger, "peor nivel");
});
t.Run("Tendencia: ajuste exponencial y pronóstico de cruce", () =>
{
    var t0 = new DateTime(2026, 1, 1);
    var exp = Enumerable.Range(0, 8).Select(i => (t0.AddDays(30 * i), 0.5 * Math.Exp(0.01 * 30 * i))).ToList();
    var fit = TrendAnalysis.Fit(exp)!;
    T.True(fit.Exponential, "exponencial");
    T.Near((fit.Crossing(4.0)!.Value - t0).TotalDays, Math.Log(8) / 0.01, 2, "días hasta 4,0");
    var lin = Enumerable.Range(0, 6).Select(i => (t0.AddDays(10 * i), 1.0 + 0.05 * i)).ToList();
    var fl = TrendAnalysis.Fit(lin)!;
    T.True(!fl.Exponential, "lineal"); T.Near(fl.SlopePerDay, 0.005, 1e-9, "pendiente");
    T.True(TrendAnalysis.Fit(lin.Take(2).ToList()) == null, "pocos puntos");
});
t.Run("Base de mediciones: demo, persistencia, CSV y borrado", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "va_tend_" + Guid.NewGuid().ToString("N"));
    try
    {
        var db = new TrendDatabase(dir);
        var plant = db.CreateDemo(new DateTime(2026, 11, 1));
        var db2 = new TrendDatabase(dir);
        T.Eq(db2.Catalog.Plants.Count, 1, "plantas"); T.Eq(db2.AllPoints().Count(), 3, "puntos"); T.Eq(db2.Catalog.Routes[0].PointIds.Count, 3, "ruta");
        var fan = db2.Catalog.Plants[0].Machines[0];
        T.True(fan.Machine.Bearing != null && fan.Machine.Bearing.Balls == 9, "rodamiento persistido");
        var fanH = fan.Points[0];
        var hist = db2.Measurements(fanH.Id);
        T.Eq(hist.Count, 12, "mediciones");
        T.True(hist[^1].Get(FeatureKeys.Bpfo) > 3 * hist[0].Get(FeatureKeys.Bpfo), "el rodamiento empeora");
        var lvl = TrendAnalysis.WorstLevel(TrendAnalysis.Evaluate(hist[^1], fanH, fan.Machine.MachineClass, hist));
        T.True(lvl >= AlarmLevel.Alert, "la última medición del ventilador está en alarma");
        T.True(TrendAnalysis.WorstLevel(TrendAnalysis.Evaluate(hist[1], fanH, fan.Machine.MachineClass, hist)) == AlarmLevel.Normal, "al principio estaba normal");
        T.Eq(db2.ExportCsv(fanH.Id).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length, 13, "CSV");
        db2.DeleteMeasurement(fanH.Id, hist[0].Id);
        T.Eq(new TrendDatabase(dir).Measurements(fanH.Id).Count, 11, "borrado persistido");
        var rec = FeatureExtractor.Extract(new RotorSimulator(new SimulatorSettings { Sensors = 1 }).Acquire(), 0, fan.Machine, fanH.Id);
        db2.AddMeasurement(rec, new RotorSimulator(new SimulatorSettings { Sensors = 1 }).Acquire());
        var back = new TrendDatabase(dir);
        var w = back.LoadWaveform(back.Measurements(fanH.Id).First(m => m.Id == rec.Id));
        T.True(w != null && w.Length == 8192, "señal guardada");
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
});

// ───────────── Sensor multicanal (protocolo v2) ─────────────
t.Run("Protocolo binario v2: 4 canales, tacómetro y trozos al azar", () =>
{
    var rnd = new Random(9);
    var head = Encoding.ASCII.GetBytes("ruido\n#VA2 fs=26667 ch=4 unit=g tach=1 scale=0.000061 names=X,Y,Z,X2 sensor=IIS3DWB\n");
    var stream = new List<byte>(head);
    var expect = new List<short>[4].Select(_ => new List<short>()).ToArray();
    var tachExp = new List<bool>();
    for (ushort seq = 0; seq < 20; seq++)
    {
        var smp = Enumerable.Range(0, 4).Select(c => Enumerable.Range(0, 64).Select(i => (short)rnd.Next(-30000, 30000)).ToArray()).ToArray();
        var tb = Enumerable.Range(0, 64).Select(i => i == 10).ToArray();
        for (int c = 0; c < 4; c++) expect[c].AddRange(smp[c]);
        tachExp.AddRange(tb);
        stream.AddRange(SensorProtocol.EncodeFrame(seq, smp, tb));
    }
    var p = new SerialStreamParser();
    var all = stream.ToArray();
    for (int i = 0; i < all.Length;) { int k = Math.Min(all.Length - i, rnd.Next(1, 300)); p.Feed(all, i, k); i += k; }
    var r = p.ToRecording();
    T.True(p.Binary, "modo binario"); T.Eq(p.FramesOk, 20L, "tramas"); T.Eq(p.CrcErrors, 0L, "sin errores"); T.Eq(r.Length, 1280, "muestras");
    T.Eq(r.Channels[3].Name, "X2", "nombres"); T.Near(r.SampleRate, 26667, 1e-9, "fs");
    T.Near(r.Channels[2].Data[700], expect[2][700] * 0.000061, 1e-12, "valor escalado");
    T.Eq(r.Tach!.Count(v => v > 0), 20, "pulsos"); T.True(r.Tach[10] > 0 && r.Tach[74] > 0, "posición del pulso");
});
t.Run("Protocolo binario v2: CRC, tramas perdidas y reconexión", () =>
{
    short[][] Block(int v) => Enumerable.Range(0, 2).Select(c => Enumerable.Repeat((short)v, 32).ToArray()).ToArray();
    var p = new SerialStreamParser();
    p.Feed("#VA2 fs=4000 ch=2 unit=g tach=0 scale=0.001\n");
    var f0 = SensorProtocol.EncodeFrame(0, Block(1), null);
    var f1 = SensorProtocol.EncodeFrame(1, Block(2), null); f1[20] ^= 0xFF;      // corrupta
    var f3 = SensorProtocol.EncodeFrame(3, Block(4), null);                       // falta la 2
    foreach (var f in new[] { f0, f1, f3 }) p.Feed(f, 0, f.Length);
    T.Eq(p.FramesOk, 2L, "tramas buenas"); T.True(p.CrcErrors >= 1, "CRC detectado"); T.Eq(p.LostFrames, 2L, "perdidas (1 corrupta + 1 faltante)");
    T.Eq(p.ToRecording().Length, 64, "muestras válidas");
    p.Feed("#VA fs=1000 ch=1 unit=g tach=0\n0.5\n0.6\n");
    T.True(!p.Binary, "volvió a texto"); T.Eq(p.ToRecording().Length, 2, "datos nuevos");
    T.Eq(SensorProtocol.Crc16(Encoding.ASCII.GetBytes("123456789")), (ushort)0x29B1, "CRC-16/CCITT-FALSE de referencia");
});
t.Run("Memoria acotada en vivo y últimos segundos", () =>
{
    var p = new SerialStreamParser { MaxSeconds = 2 };
    var sb = new StringBuilder("#VA fs=1000 ch=1 unit=g tach=0\n");
    for (int i = 0; i < 5000; i++) sb.Append(i).Append('\n');
    p.Feed(sb.ToString());
    T.True(p.Count <= 2400 && p.Count >= 2000, $"conserva ≈ 2 s ({p.Count})");
    var r = p.ToRecording(0, 1);
    T.Eq(r.Length, 1000, "último segundo"); T.Near(r.Channels[0].Data[^1], 4999, 1e-9, "dato más reciente");
});

// ───────────── IEPE / DAQ ─────────────
t.Run("Búfer circular multicanal", () =>
{
    var b = new StreamBuffer(2, 100);
    for (int k = 0; k < 5; k++) b.Append(new[] { Enumerable.Range(k * 50, 50).Select(i => (double)i).ToArray(), Enumerable.Range(k * 50, 50).Select(i => -(double)i).ToArray() });
    var last = b.Last(50);
    T.Eq(last[0].Length, 50, "largo"); T.Near(last[0][0], 200, 0, "primera"); T.Near(last[1][49], -249, 0, "última"); T.Eq(b.TotalSamples, 250L, "total");
    T.Eq(b.Last(500)[0].Length, 100, "como mucho la capacidad");
});
t.Run("Conversión de volts a unidades: IEPE, sonda de proximidad y tacómetro", () =>
{
    double fs = 10000; int n = 10000;
    var acc = Enumerable.Range(0, n).Select(i => 0.05 * Math.Cos(2 * Math.PI * 25 * i / fs)).ToArray();          // 0,5 g con 100 mV/g
    var prox = Enumerable.Range(0, n).Select(i => -10 + 0.0787 * Math.Cos(2 * Math.PI * 25 * i / fs)).ToArray(); // 10 µm con 7,87 mV/µm, gap −10 V
    var tach = Enumerable.Range(0, n).Select(i => (25.0 * i / fs) % 1 < 0.05 ? 5.0 : 0).ToArray();
    var cfg = new List<ChannelConfig>
    {
        new() { Name = "A", Kind = SensorKind.AccelerometerIepe, Sensitivity = 100 },
        new() { Name = "P", Kind = SensorKind.ProximityProbe, Sensitivity = 7.87 },
        new() { Name = "T", Kind = SensorKind.Tachometer },
    };
    var (r, info) = ChannelAssembler.Build(new[] { acc, prox, tach }, cfg, fs, 0, "prueba");
    T.Eq(r.Channels.Count, 2, "canales de vibración"); T.True(r.Tach != null, "tacómetro");
    T.Near(r.Reading1X(0).Magnitude, 0.5, 1e-6, "aceleración (g)"); T.Eq(r.Channels[1].Unit, VibrationUnit.DisplacementUm, "unidad");
    T.Near(r.Reading1X(1).Magnitude, 10, 1e-4, "desplazamiento (µm)"); T.Near(r.Channels[1].Data.Average(), 0, 1e-9, "sin gap");
    T.Near(info[1].DcVolts, -10, 1e-9, "gap informado"); T.Near(r.MeasuredRpm(), 1500, 1, "rpm");
});
t.Run("Calidad de señal: saturación, señal plana, polarización IEPE y tacómetro", () =>
{
    double fs = 5000; int n = 5000;
    var sine = Enumerable.Range(0, n).Select(i => 0.5 * Math.Sin(2 * Math.PI * 50 * i / fs)).ToArray();
    T.Eq(SignalQuality.Assess("a", sine, fs, SensorKind.AccelerometerIepe, 1.0).Count, 0, "señal buena");
    var clip = sine.Select(v => Math.Clamp(v * 3, -1.0, 1.0)).ToArray();
    T.True(SignalQuality.Assess("a", clip, fs, SensorKind.AccelerometerIepe, 1.0).Any(q => q.Level == IssueLevel.Error && q.Message.Contains("Saturación")), "saturación");
    T.True(SignalQuality.Assess("a", new double[n], fs, SensorKind.AccelerometerIepe).Any(q => q.Message.Contains("plana")), "plana");
    T.True(SignalQuality.Assess("a", sine.Select(v => v + 23).ToArray(), fs, SensorKind.AccelerometerIepe, 0, true).Any(q => q.Message.Contains("abierto")), "circuito abierto");
    T.True(SignalQuality.Assess("a", sine.Select(v => v * 0.01 + 0.5).ToArray(), fs, SensorKind.AccelerometerIepe, 0, true).Any(q => q.Message.Contains("cortocircuito")), "cortocircuito");
    T.True(!SignalQuality.Assess("a", sine.Select(v => v + 11).ToArray(), fs, SensorKind.AccelerometerIepe, 0, true).Any(q => q.Level >= IssueLevel.Warning), "bias normal");
    T.True(SignalQuality.Assess("t", new double[n], fs, SensorKind.Tachometer).Any(q => q.Message.Contains("pulsos")), "tacómetro sin pulsos");
    var ski = Enumerable.Range(0, 20000).Select(i => 0.8 * Math.Exp(-i / 4000.0) + 0.05 * Math.Sin(2 * Math.PI * 50 * i / fs)).ToArray();
    T.True(SignalQuality.Assess("a", ski, fs, SensorKind.AccelerometerIepe).Any(q => q.Message.Contains("esquí")), "bajada de esquí");
});
t.Run("Calibración con referencia de 1 g a 159,2 Hz", () =>
{
    double fs = 25600, sens = 98.5;
    var v = Enumerable.Range(0, 25600).Select(i => Math.Sqrt(2) * 1.0 * sens / 1000 * Math.Sin(2 * Math.PI * 159.2 * i / fs + 0.3)).ToArray();
    T.Near(SensorCalibration.SensitivityFromReference(v, fs, 159.2, 1.0), sens, 0.01, "mV/g");
});
t.Run("Equipo simulado: adquisición continua, instantánea y calidad", () =>
{
    using var dev = new SimulatedDevice { Rpm = 1500, AmplitudeG = 0.5 };
    dev.Start(new AcquisitionSettings { SampleRate = 25600, BufferSeconds = 4 });
    for (int k = 0; k < 10; k++) dev.Generate(12800);
    var (r, issues) = dev.Snapshot(2, 0);
    T.Eq(r.Length, 51200, "2 s"); T.Near(r.MeasuredRpm(), 1500, 1, "rpm"); T.Near(r.Reading1X(0).Magnitude, 0.5, 0.01, "1X");
    T.Eq(issues.Count, 0, "sin problemas");
    dev.Stop(); T.True(!dev.IsRunning, "detenido");
});

// ───────────── Red y planta (MQTT, Modbus) ─────────────
t.Run("MQTT: longitud variable y comodines de tema", () =>
{
    foreach (var (len, bytes) in new[] { (0, 1), (127, 1), (128, 2), (16383, 2), (16384, 3), (2097151, 3), (2097152, 4) })
    { var b = new List<byte>(); MqttCodec.WriteRemainingLength(b, len); T.Eq(b.Count, bytes, $"largo {len}"); }
    T.True(MqttCodec.TopicMatches("vib/+/indicadores", "vib/vt01/indicadores"), "+");
    T.True(MqttCodec.TopicMatches("vib/#", "vib/a/b/c"), "#");
    T.True(!MqttCodec.TopicMatches("vib/+/indicadores", "vib/vt01/x/indicadores"), "+ es un nivel");
    T.True(!MqttCodec.TopicMatches("vib/a", "vib/a/b"), "más niveles");
    var p = MqttCodec.BuildPublish("a/b", Encoding.UTF8.GetBytes("hola"), 1, false, 7);
    var (topic, payload, qos, id) = MqttCodec.ParsePublish(p[0], p[2..]);
    T.Eq(topic, "a/b", "tema"); T.Eq(Encoding.UTF8.GetString(payload), "hola", "contenido"); T.Eq(qos, 1, "QoS"); T.Eq(id, (ushort)7, "id");
});
t.Run("MQTT: conexión, suscripción y publicación con un broker de prueba", () =>
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
    int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    var broker = Task.Run(async () =>
    {
        using var c = await listener.AcceptTcpClientAsync();
        var s = c.GetStream(); var ct = CancellationToken.None;
        var (h, _) = await MqttCodec.ReadPacketAsync(s, ct); if (h != MqttCodec.Connect) throw new Exception("CONNECT");
        await s.WriteAsync(new byte[] { MqttCodec.ConnAck, 2, 0, 0 });
        for (int k = 0; k < 2; k++)
        {
            var (hh, body) = await MqttCodec.ReadPacketAsync(s, ct);
            if ((hh & 0xF0) == 0x80) await s.WriteAsync(new byte[] { MqttCodec.SubAck, 3, body[0], body[1], 0 });
            else if ((hh & 0xF0) == MqttCodec.Publish)
            {
                var (tp, pl, q, pid) = MqttCodec.ParsePublish(hh, body);
                if (q == 1) await s.WriteAsync(MqttCodec.BuildPubAck(pid));
                await s.WriteAsync(MqttCodec.BuildPublish(tp, pl));
            }
        }
        await Task.Delay(300);
    });
    using var cli = new MqttClient();
    var got = new TaskCompletionSource<string>();
    cli.MessageReceived += (tp, pl) => got.TrySetResult(tp + "=" + Encoding.UTF8.GetString(pl));
    cli.ConnectAsync("127.0.0.1", port, "prueba").Wait(5000);
    cli.SubscribeAsync("vib/#").Wait(5000);
    cli.PublishAsync("vib/vt01/indicadores", "{\"id\":\"vt01\"}", 1).Wait(5000);
    T.True(got.Task.Wait(5000), "mensaje recibido");
    T.Eq(got.Task.Result, "vib/vt01/indicadores={\"id\":\"vt01\"}", "eco del broker");
    broker.Wait(5000); listener.Stop();
});
t.Run("Modbus: CRC, float32 y servidor TCP con cliente", () =>
{
    T.Eq(ModbusCodec.Crc(new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A }), (ushort)0xCDC5, "CRC de referencia (C5 CD en la trama)");
    var (hi, lo) = ModbusCodec.FromFloat(2.5f); T.Near(ModbusCodec.ToFloat(hi, lo), 2.5, 0, "float ABCD");
    var (a, b) = ModbusCodec.FromFloat(-1.25f, true); T.Near(ModbusCodec.ToFloat(a, b, true), -1.25, 0, "float CDAB");
    using var srv = new ModbusTcpServer();
    srv.Start(0, System.Net.IPAddress.Loopback);
    srv.SetFloat(0, 3.75f); srv.SetWord(21, 2);
    int written = -1; srv.HoldingWritten += (adr, v) => written = adr * 1000 + v;
    using var cli = ModbusClient.ConnectTcpAsync("127.0.0.1", srv.Port).Result;
    var r = cli.ReadInputAsync(1, 0, 2).Result;
    T.Near(ModbusCodec.ToFloat(r[0], r[1]), 3.75, 0, "registro de entrada");
    T.Eq(cli.ReadInputAsync(1, 21, 1).Result[0], (ushort)2, "nivel de alarma");
    cli.WriteSingleAsync(1, 0, 1).Wait();
    T.Eq(written, 1, "orden del PLC"); T.Eq(cli.ReadHoldingAsync(1, 0, 1).Result[0], (ushort)1, "registro de retención");
    try { cli.ReadInputAsync(1, 999, 5).Wait(); throw new Exception("debía fallar"); }
    catch (AggregateException ex) { T.True(ex.InnerException!.Message.Contains("dirección"), "excepción 02"); }
});
t.Run("Modbus RTU: cliente con esclavo simulado", () =>
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
    int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    var holding = new ushort[10]; holding[3] = 1490; var input = new ushort[1]; var sync = new object();
    var slave = Task.Run(async () =>
    {
        using var c = await listener.AcceptTcpClientAsync(); var s = c.GetStream();
        var req = new byte[8]; int off = 0;
        while (off < 8) off += await s.ReadAsync(req.AsMemory(off));
        if (ModbusCodec.Crc(req.AsSpan(0, 6)) != (ushort)(req[6] | req[7] << 8)) throw new Exception("CRC del pedido");
        var pdu = ModbusCodec.Handle(req.AsSpan(1, 5), holding, input, sync);
        var resp = new byte[pdu.Length + 3]; resp[0] = req[0]; pdu.CopyTo(resp, 1);
        ushort crc = ModbusCodec.Crc(resp.AsSpan(0, pdu.Length + 1)); resp[^2] = (byte)crc; resp[^1] = (byte)(crc >> 8);
        await s.WriteAsync(resp);
        await Task.Delay(200);
    });
    using var tcp = new System.Net.Sockets.TcpClient(); tcp.Connect("127.0.0.1", port);
    using var cli = ModbusClient.OverRtu(tcp.GetStream());
    T.Eq(cli.ReadHoldingAsync(5, 3, 1).Result[0], (ushort)1490, "velocidad del variador por RTU");
    slave.Wait(3000); listener.Stop();
});
t.Run("Puente con la planta: registros, JSON y mensajes de sensores", () =>
{
    using var srv = new ModbusTcpServer();
    var m = new MeasurementRecord { PointId = "p1", Rpm = 1490, Timestamp = new DateTime(2026, 11, 21, 10, 0, 0), Features = { [FeatureKeys.VelocityRms] = 4.2, [FeatureKeys.Amp1X] = 3.9 } };
    PlantGateway.PublishCurrent(srv, m, 2, AlarmLevel.Alert, 7);
    PlantGateway.PublishPoint(srv, 1, m, AlarmLevel.Danger, 12);
    T.Near(ModbusCodec.ToFloat(srv.Input[0], srv.Input[1]), 4.2, 1e-6, "vel RMS en 30001");
    T.Near(ModbusCodec.ToFloat(srv.Input[18], srv.Input[19]), 1490, 1e-3, "rpm en 30019");
    T.Eq(srv.Input[21], (ushort)1, "alerta en 30022");
    T.Eq(srv.Input[PlantGateway.PointBase + PlantGateway.PointStride + 8], (ushort)2, "peligro del punto 2");
    T.True(PlantGateway.Describe(new[] { "VT-01 LA" }).Any(r => r.Address == 30101 && r.Name.Contains("VT-01")), "mapa descripto");
    var json = PlantGateway.MeasurementJson(m, "Planta", "VT-01", "LA H", AlarmLevel.Alert);
    using var doc = JsonDocument.Parse(json);
    T.Eq(doc.RootElement.GetProperty("nivel").GetString(), "alerta", "nivel"); T.Near(doc.RootElement.GetProperty("indicadores").GetProperty("vel_rms").GetDouble(), 4.2, 1e-9, "indicador");
    var sm = PlantGateway.ParseSensorMessage("{\"id\":\"vt01\",\"vel_rms\":2.3,\"temperatura\":56,\"rpm\":1490,\"fs\":3200}")!.Value;
    T.Eq(sm.Id, "vt01", "id"); T.Near(sm.Rpm, 1490, 0, "rpm"); T.True(sm.Features.ContainsKey("temperatura") && !sm.Features.ContainsKey("fs"), "indicadores");
    T.True(PlantGateway.ParseSensorMessage("no json") == null, "basura");
    T.Eq(PlantGateway.TopicPart("Lado acople / H"), "Lado_acople___H", "tema seguro");
});

// ───────────── Otras magnitudes: corriente del motor, ultrasonido, temperatura ─────────────
t.Run("MCSA: barras rotas, excentricidad, armónicos y deslizamiento", () =>
{
    double fs = 5000;
    var broken = MotorCurrentAnalysis.Simulate(fs, 20, 50, 10, 4, 1455, brokenBarDb: 38, eccentricityDb: 45, fifthPercent: 4, noiseA: 0.002);
    var m = MotorCurrentAnalysis.Analyze(broken, fs, 50, 4, 1455);
    T.Near(m.LineHz, 50, 0.01, "red"); T.Near(m.Slip, 0.03, 1e-6, "deslizamiento");
    T.Near(m.LowerSidebandDb, 38, 1.5, "banda inferior"); T.Eq(m.RotorSeverity, 3, "barras fisuradas");
    T.Near(m.EccentricityDb, 45, 1.5, "excentricidad"); T.Near(m.ThdPercent, 4, 0.3, "THD");
    T.Near(m.Harmonics.First(h => h.Order == 5).Percent, 4, 0.3, "5.º armónico");
    var healthy = MotorCurrentAnalysis.Analyze(MotorCurrentAnalysis.Simulate(fs, 20, 50, 10, 4, 1455, noiseA: 0.002), fs, 50, 4, 1455);
    T.True(healthy.LowerSidebandDb > 54 && healthy.RotorSeverity == 0, $"rotor sano ({healthy.LowerSidebandDb:0} dB)");
    var est = MotorCurrentAnalysis.Analyze(MotorCurrentAnalysis.Simulate(fs, 20, 50, 10, 4, 1455, eccentricityDb: 40, noiseA: 0.002), fs, 50, 4, 0);
    T.Near(est.Slip, 0.03, 0.002, "deslizamiento estimado sin rpm");
    T.Near(MotorCurrentAnalysis.Unbalance(10, 10.5, 9.5), 5, 1e-9, "desbalance NEMA");
});
t.Run("Ultrasonido: dBµV, heterodinado y criterios", () =>
{
    double fs = 192000;
    var x = Enumerable.Range(0, 96000).Select(i => 1e-3 * Math.Sin(2 * Math.PI * 40000 * i / fs) + 1.0 * Math.Sin(2 * Math.PI * 50 * i / fs)).ToArray();
    var u = UltrasoundAnalysis.Analyze(x, fs);
    T.Near(u.RmsDbuV, 20 * Math.Log10(1e-3 / Math.Sqrt(2) / 1e-6), 0.3, "nivel en banda (sin los 50 Hz)");
    var audio = UltrasoundAnalysis.Heterodyne(x, fs, 38000, 8000);
    var sp = SpectrumAnalyzer.Compute(audio, 8000);
    T.Near(SpectralFeatures.Peaks(sp, 1, 100)[0].Frequency, 2000, 5, "40 kHz se escucha a 2 kHz");
    T.Eq(UltrasoundAnalysis.Condition(10).Level, 1, "lubricar"); T.Eq(UltrasoundAnalysis.Condition(20).Level, 3, "falla");
    try { UltrasoundAnalysis.Analyze(x, 48000); throw new Exception("debía fallar"); } catch (ArgumentException) { }
    var wav = WavWriter.Mono16(audio, 8000);
    T.Eq(wav.Length, 44 + audio.Length * 2, "WAV"); T.Eq(WavRecordingReader.Read(wav, 1, VibrationUnit.VoltageV).SampleRate, 8000.0, "WAV legible");
});
t.Run("Alarmas en dB: diferencia con la base e indicadores donde menos es peor", () =>
{
    var pt = new MeasurementPoint();
    MeasurementRecord R(string k, double v, int d) => new() { PointId = pt.Id, Timestamp = new DateTime(2026, 1, 1).AddDays(d), Features = { [k] = v } };
    var hist = new List<MeasurementRecord> { R(FeatureKeys.UltrasoundDb, 40, 0), R(FeatureKeys.UltrasoundDb, 41, 1), R(FeatureKeys.UltrasoundDb, 39, 2) };
    AlarmLevel L(MeasurementRecord r) => TrendAnalysis.Evaluate(r, pt, MachineClass.Iso10816_3_Group2Rigid, hist).First().Level;
    T.Eq(L(R(FeatureKeys.UltrasoundDb, 45, 9)), AlarmLevel.Normal, "+5 dB"); T.Eq(L(R(FeatureKeys.UltrasoundDb, 49, 9)), AlarmLevel.Alert, "+9 dB");
    T.Eq(L(R(FeatureKeys.UltrasoundDb, 57, 9)), AlarmLevel.Danger, "+17 dB");
    T.Eq(L(R(FeatureKeys.BrokenBarDb, 50, 9)), AlarmLevel.Normal, "rotor bien"); T.Eq(L(R(FeatureKeys.BrokenBarDb, 40, 9)), AlarmLevel.Alert, "rotor alerta");
    T.Eq(L(R(FeatureKeys.BrokenBarDb, 30, 9)), AlarmLevel.Danger, "rotor peligro"); T.Eq(L(R(FeatureKeys.Temperature, 97, 9)), AlarmLevel.Danger, "temperatura");
});
t.Run("Indicadores de canales de corriente (3 fases) y unidades eléctricas", () =>
{
    double fs = 5000;
    var ia = MotorCurrentAnalysis.Simulate(fs, 12, 50, 10, 4, 1455, brokenBarDb: 40, phaseDeg: 0);
    var ib = MotorCurrentAnalysis.Simulate(fs, 12, 50, 10.8, 4, 1455, phaseDeg: -120, seed: 2);
    var ic = MotorCurrentAnalysis.Simulate(fs, 12, 50, 9.6, 4, 1455, phaseDeg: 120, seed: 3);
    var rec = new Recording(fs, new[] { new Channel("R", VibrationUnit.CurrentA, ia), new Channel("S", VibrationUnit.CurrentA, ib), new Channel("T", VibrationUnit.CurrentA, ic) }, null, 1455, "prueba");
    var m = FeatureExtractor.Extract(rec, 0, new Machine { Rpm = 1455, Poles = 4, LineFrequencyHz = 50 });
    T.Near(m.Get(FeatureKeys.CurrentRms), 10 / Math.Sqrt(2), 0.05, "corriente RMS");
    T.Near(m.Get(FeatureKeys.CurrentUnbalance), MotorCurrentAnalysis.Unbalance(10, 10.8, 9.6), 0.3, "desbalance");
    T.Near(m.Get(FeatureKeys.BrokenBarDb), 40, 1.5, "barras"); T.True(double.IsNaN(m.Get(FeatureKeys.VelocityRms)), "sin indicadores de vibración");
    T.Eq(CsvRecordingReader.ParseUnit("A"), VibrationUnit.CurrentA, "unidad A");
    T.Near(SignalTools.Convert(ia, fs, VibrationUnit.CurrentA, VibrationUnit.VelocityMmS)[100], ia[100], 0, "la corriente no se convierte");
});

int failed = t.Summary();
var emitIdx = Array.IndexOf(args, "--emit");
if (emitIdx >= 0 && emitIdx + 1 < args.Length) Vectors.Emit(args[emitIdx + 1]);
return failed == 0 ? 0 : 1;

static double Gauss(Random r) { double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble(); return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }

/// Impactos a fDefect que excitan una resonancia de 3 kHz, con desbalance fuerte a 25 Hz y ruido.
static (double[] X, double Fs) BearingSignal(double fDefect)
{
    var rnd = new Random(11); double fs = 20000; int n = 32768;
    var x = new double[n];
    for (int i = 0; i < n; i++) x[i] = 3.0 * Math.Sin(2 * Math.PI * 25 * i / fs) + 0.3 * Gauss(rnd);
    for (double tk = 0.003; tk < n / fs; tk += 1 / fDefect)
    {
        int i0 = (int)(tk * fs);
        for (int i = i0; i < n && i < i0 + 200; i++) { double tt = (i - i0) / fs; x[i] += 1.5 * Math.Exp(-700 * tt) * Math.Sin(2 * Math.PI * 3000 * tt); }
    }
    return (x, fs);
}

/// Señal de velocidad variable (rampa lineal de f0 a f1 Hz) con su tacómetro de 1 pulso por vuelta.
static (double[] X, double[] Tach, double Fs) Chirp(double f0, double f1, double seconds, double fs, Func<double, int, double> gen)
{
    int n = (int)(seconds * fs);
    var x = new double[n]; var tach = new double[n];
    for (int i = 0; i < n; i++)
    {
        double t2 = i / fs, th = 2 * Math.PI * (f0 * t2 + (f1 - f0) * t2 * t2 / (2 * seconds));
        x[i] = gen(th, i);
        double rev = th / (2 * Math.PI);
        tach[i] = rev - Math.Floor(rev) < 0.05 ? 5 : 0;
    }
    return (x, tach, fs);
}

static IReadOnlyList<Finding> Diagnose(Recording r, BearingGeometry? b = null)
{
    var sp = SpectrumAnalyzer.Compute(r.Channels[0].Data, r.SampleRate, WindowType.Hann);
    return FaultDiagnosis.Diagnose(new DiagnosisInput { VelocitySpectrum = sp, RunningSpeedHz = r.MeasuredRpm() / 60, Bearing = b });
}

static void SimulatedSession(int planes, bool against)
{
    var set = new SimulatorSettings { Sensors = planes, NoiseMmS = 0.03 };
    var sim = new RotorSimulator(set);
    double radius = 140;
    Phasor[] Read()
    {
        var rec = sim.Acquire();
        return Enumerable.Range(0, planes).Select(c => rec.Reading1X(c)).ToArray();
    }
    // En el simulador los ángulos internos siguen la convención "contraria al giro";
    // si el técnico mide a favor del giro, el ángulo físico es −θ.
    double Phys(double userAngle) => against ? userAngle : -userAngle;
    var s = new BalancingSession(planes) { AnglesAgainstRotation = against, ToleranceVibration = 0.2 };
    var o = Read();
    s.RecordInitial(o[0], planes == 2 ? o[1] : null, set.Rpm);
    var t1 = new Phasor(40, 30);
    set.AddedMasses.Add(new PlaneMass(1, t1.Magnitude, Phys(t1.AngleDeg), radius));
    var r1 = Read();
    s.RecordTrial1(t1, r1[0], planes == 2 ? r1[1] : null, set.Rpm);
    set.AddedMasses.Clear();
    if (planes == 2)
    {
        var t2 = new Phasor(40, 200);
        set.AddedMasses.Add(new PlaneMass(2, t2.Magnitude, Phys(t2.AngleDeg), radius));
        var r2 = Read();
        s.RecordTrial2(t2, r2[0], r2[1], set.Rpm);
        set.AddedMasses.Clear();
    }
    T.Eq(s.Step, BalancingStep.Correction, "paso");
    for (int p = 0; p < planes; p++)
        set.AddedMasses.Add(new PlaneMass(p + 1, s.Corrections[p].Magnitude, Phys(s.Corrections[p].AngleDeg), radius));
    var after = Read();
    double before = o.Max(x => x.Magnitude), now = after.Max(x => x.Magnitude);
    T.True(now < before * 0.1, $"residual {now:F3} vs original {before:F3}");
    var pred = s.PredictedResidual();
    T.True(pred.Max(x => x.Magnitude) < before * 0.05, "predicción");
    var (ok, _) = s.RecordVerification(after[0], planes == 2 ? after[1] : null, set.Rpm);
    T.True(ok, "dentro de tolerancia");
    T.Eq(s.Step, BalancingStep.Done, "terminado");
}

static Complex[] NaiveDft(double[] x)
{
    int n = x.Length; var r = new Complex[n];
    for (int k = 0; k < n; k++) { Complex s = 0; for (int i = 0; i < n; i++) s += x[i] * Complex.FromPolarCoordinates(1, -2 * Math.PI * k * i / n); r[k] = s; }
    return r;
}

static byte[] MakeWav16(int rate, int ch, int frames, Func<int, int, double> f)
{
    using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
    int data = frames * ch * 2;
    w.Write("RIFF"u8.ToArray()); w.Write(36 + data); w.Write("WAVE"u8.ToArray());
    w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(rate); w.Write(rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
    w.Write("data"u8.ToArray()); w.Write(data);
    for (int i = 0; i < frames; i++) for (int c = 0; c < ch; c++) w.Write((short)Math.Round(f(c, i) * 32767));
    return ms.ToArray();
}

/// <summary>Vectores de referencia para comparar con la implementación web (JavaScript).</summary>
static class Vectors
{
    public static void Emit(string path)
    {
        var o = new Dictionary<string, object>();
        var x = Enumerable.Range(0, 1000).Select(i => Math.Sin(i * 0.11) + 0.5 * Math.Cos(i * 0.73) + 0.1 * Math.Sin(i * 2.9)).ToArray();
        o["signal"] = x;
        var sp = SpectrumAnalyzer.Compute(x, 1000, WindowType.Hann);
        o["spectrumHann"] = new { sp.Resolution, amplitudes = sp.Amplitudes.Take(200).ToArray() };
        var fit = SpectrumAnalyzer.FitSinusoid(x, 1000, 17.5, 0.004);
        o["fit"] = new { freq = 17.5, t0 = 0.004, amplitude = fit.Amplitude, phase = fit.PhaseLagDeg };
        var one = InfluenceBalancer.SinglePlane(new Phasor(5.2, 47), new Phasor(30, 120), new Phasor(3.1, 101));
        o["single"] = new { o = new[] { 5.2, 47 }, t = new[] { 30.0, 120 }, r = new[] { 3.1, 101 }, w = new[] { one.Correction.Magnitude, one.Correction.AngleDeg }, keep = new[] { one.CorrectionKeepingTrial.Magnitude, one.CorrectionKeepingTrial.AngleDeg } };
        var two = InfluenceBalancer.TwoPlanes(new Phasor(6, 30), new Phasor(4, 200), new Phasor(25, 0), new Phasor(8.5, 60), new Phasor(4.6, 215), new Phasor(25, 90), new Phasor(6.7, 40), new Phasor(6.2, 250));
        o["two"] = new
        {
            input = new[] { new[] { 6.0, 30 }, new[] { 4.0, 200 }, new[] { 25.0, 0 }, new[] { 8.5, 60 }, new[] { 4.6, 215 }, new[] { 25.0, 90 }, new[] { 6.7, 40 }, new[] { 6.2, 250 } },
            w1 = new[] { two.CorrectionPlane1.Magnitude, two.CorrectionPlane1.AngleDeg },
            w2 = new[] { two.CorrectionPlane2.Magnitude, two.CorrectionPlane2.AngleDeg },
            cond = two.ConditionNumber,
        };
        var split = WeightSplitter.Split(new Phasor(37, 100), 7, 10);
        o["split"] = new { w = new[] { 37.0, 100 }, n = 7, first = 10.0, a = new[] { split.PosA, split.MassA }, b = new[] { split.PosB, split.MassB } };
        o["drill"] = new { mass = 12.0, density = 7850.0, d = 10.0, depth = DrillCalculator.DepthMm(12, 7850, 10) };
        o["trial"] = new { mass = 100.0, rpm = 1500.0, r = 200.0, g = TrialWeight.SuggestGrams(100, 1500, 200) };
        var acc = Enumerable.Range(0, 2000).Select(i => Math.Sin(2 * Math.PI * 50 * i / 2000.0) + 0.2 * Math.Sin(2 * Math.PI * 130 * i / 2000.0)).ToArray();
        var vel = SignalTools.Convert(acc, 2000, VibrationUnit.AccelerationG, VibrationUnit.VelocityMmS);
        o["integration"] = new { fs = 2000.0, acc = acc.Take(64).ToArray(), velRms = SignalTools.Statistics(vel).Rms, accFull = acc };
        File.WriteAllText(path, JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = false }));
        Console.WriteLine($"Vectores escritos en {path}");
    }
}

sealed class TestRunner
{
    private int pass, fail;
    public void Run(string name, Action a)
    {
        try { a(); pass++; Console.WriteLine($"  ✓ {name}"); }
        catch (Exception e) { fail++; Console.WriteLine($"  ✗ {name}\n      {e.Message}"); }
    }
    public int Summary()
    {
        Console.WriteLine($"\n{pass} pruebas correctas, {fail} con errores.");
        return fail;
    }
}

static class T
{
    public static void Near(double a, double b, double tol, string what)
    {
        if (!(Math.Abs(a - b) <= tol)) throw new Exception($"{what}: {a} ≠ {b} (tolerancia {tol})");
    }
    public static void Eq<TV>(TV a, TV b, string what)
    {
        if (!EqualityComparer<TV>.Default.Equals(a, b)) throw new Exception($"{what}: {a} ≠ {b}");
    }
    public static void True(bool c, string what)
    {
        if (!c) throw new Exception($"{what}: falso");
    }
    public static void Throws<TE>(Action a, string what) where TE : Exception
    {
        try { a(); } catch (TE) { return; }
        throw new Exception($"{what}: no lanzó {typeof(TE).Name}");
    }
}
