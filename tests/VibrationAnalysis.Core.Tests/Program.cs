// Pruebas del núcleo de cálculo. Se ejecutan con:  dotnet run --project tests/VibrationAnalysis.Core.Tests
// Con "--emit <archivo.json>" además se generan vectores de referencia para verificar que la versión web calcule lo mismo.
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using VibrationAnalysis.Core.Analysis;
using VibrationAnalysis.Core.Balancing;
using VibrationAnalysis.Core.Dsp;
using VibrationAnalysis.Core.IO;
using VibrationAnalysis.Core.Model;
using VibrationAnalysis.Core.Simulation;

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
