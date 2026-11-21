using System.Numerics;

namespace VibrationAnalysis.Core.Dsp;

public enum AveragingMode
{
    /// <summary>Promedio lineal de potencia (RMS): reduce la varianza del ruido.</summary>
    Linear,
    /// <summary>Promedio exponencial (los bloques recientes pesan más).</summary>
    Exponential,
    /// <summary>Retención de picos: el máximo de cada línea (transitorios, arranques).</summary>
    PeakHold,
}

/// <summary>Densidad espectral de potencia de un solo lado (unidades²/Hz).</summary>
public sealed record DensitySpectrum(double[] Frequencies, double[] Psd, double Resolution, int Averages)
{
    /// <summary>Valor RMS en la banda [fMin, fMax] (integral de la PSD).</summary>
    public double BandRms(double fMin, double fMax)
    {
        double s = 0;
        for (int i = 0; i < Frequencies.Length; i++)
            if (Frequencies[i] >= fMin && Frequencies[i] <= fMax) s += Psd[i] * Resolution;
        return Math.Sqrt(s);
    }

    /// <summary>Raíz de la PSD (unidades/√Hz), útil para graficar.</summary>
    public double[] Asd() => Psd.Select(Math.Sqrt).ToArray();
}

/// <summary>Cepstrum real de potencia: c(τ) = IFFT{ ln |X(f)|² }. Detecta familias de armónicos y bandas laterales.</summary>
public sealed record CepstrumResult(double[] Quefrency, double[] Values, double SampleRate)
{
    /// <summary>Pico más alto en ±tolerancia (s) alrededor de una quefrencia.</summary>
    public (double Quefrency, double Value) PeakNear(double q, double tol)
    {
        double best = double.NegativeInfinity, bq = q;
        for (int i = 1; i < Quefrency.Length; i++)
            if (Math.Abs(Quefrency[i] - q) <= tol && Values[i] > best) { best = Values[i]; bq = Quefrency[i]; }
        return (bq, double.IsNegativeInfinity(best) ? 0 : best);
    }

    /// <summary>Pico dominante en el rango de quefrencias [qMin, qMax] (s).</summary>
    public (double Quefrency, double Value) Dominant(double qMin, double qMax)
    {
        double best = double.NegativeInfinity, bq = 0;
        for (int i = 1; i < Quefrency.Length; i++)
            if (Quefrency[i] >= qMin && Quefrency[i] <= qMax && Values[i] > best) { best = Values[i]; bq = Quefrency[i]; }
        return (bq, double.IsNegativeInfinity(best) ? 0 : best);
    }

    /// <summary>
    /// Prominencia de una familia de periodo T (s): media de los rahmónicos T, 2T, 3T… dividida por el nivel típico del cepstrum.
    /// Valores &gt; 3 indican una familia clara de armónicos o bandas laterales separadas 1/T Hz.
    /// </summary>
    public double FamilyProminence(double period, int rahmonics = 3)
    {
        if (period <= 0) return 0;
        double tol = Math.Max(2 / SampleRate, period * 0.02);
        double s = 0; int n = 0;
        for (int k = 1; k <= rahmonics; k++)
        {
            if (k * period > Quefrency[^1]) break;
            s += Math.Max(0, PeakNear(k * period, tol).Value); n++;
        }
        if (n == 0) return 0;
        var rest = Values.Skip(Math.Max(1, (int)(0.002 * SampleRate))).Select(Math.Abs).OrderBy(v => v).ToArray();
        double typical = rest.Length > 0 ? rest[rest.Length / 2] : 1e-12;
        return s / n / Math.Max(typical, 1e-12);
    }
}

/// <summary>Curtosis espectral por línea (STFT). Indica en qué banda de frecuencia hay impulsos: la mejor banda para la envolvente.</summary>
public sealed record SpectralKurtosisResult(double[] Frequencies, double[] Kurtosis, int Segments)
{
    /// <summary>
    /// Banda de envolvente sugerida: la de ancho <paramref name="bandwidth"/> con mayor curtosis media,
    /// por encima de <paramref name="minFrequency"/> (evita la zona de 1X y armónicos).
    /// </summary>
    public (double Low, double High, double Kurtosis) SuggestBand(double bandwidth, double minFrequency)
    {
        double df = Frequencies.Length > 1 ? Frequencies[1] - Frequencies[0] : 1;
        int w = Math.Max(1, (int)Math.Round(bandwidth / df));
        double best = double.NegativeInfinity; int bi = -1;
        double sum = 0;
        for (int i = 0; i < Frequencies.Length; i++)
        {
            sum += Kurtosis[i];
            if (i >= w) sum -= Kurtosis[i - w];
            if (i >= w - 1)
            {
                int lo = i - w + 1;
                if (Frequencies[lo] < minFrequency || Frequencies[i] >= Frequencies[^1]) continue;
                if (sum / w > best) { best = sum / w; bi = lo; }
            }
        }
        if (bi < 0) return (Math.Max(minFrequency, df), Math.Min(Frequencies[^1] * 0.9, minFrequency + bandwidth), 0);
        return (Math.Max(Frequencies[bi], df), Frequencies[Math.Min(bi + w - 1, Frequencies.Length - 1)], best);
    }
}

/// <summary>
/// Procesamiento espectral avanzado: promediado (Welch), PSD, zoom FFT, cepstrum, envolvente (demodulación) y curtosis espectral.
/// </summary>
public static class AdvancedSpectrum
{
    /// <summary>Divide la señal en bloques de <paramref name="segment"/> muestras con solape (0 a 0,9) y devuelve los inicios.</summary>
    public static int[] Segments(int length, int segment, double overlap)
    {
        if (segment < 8 || segment > length) throw new ArgumentOutOfRangeException(nameof(segment), "El bloque debe tener entre 8 muestras y el largo de la señal.");
        overlap = Math.Clamp(overlap, 0, 0.9);
        int step = Math.Max(1, (int)Math.Round(segment * (1 - overlap)));
        var list = new List<int>();
        for (int s = 0; s + segment <= length; s += step) list.Add(s);
        return list.ToArray();
    }

    /// <summary>Espectro de amplitud pico promediado por bloques (método de Welch).</summary>
    public static Spectrum Averaged(IReadOnlyList<double> x, double fs, int segment, double overlap = 0.5,
        WindowType window = WindowType.Hann, AveragingMode mode = AveragingMode.Linear, int exponentialAverages = 8)
    {
        var starts = Segments(x.Count, segment, overlap);
        double[]? acc = null;
        Spectrum? first = null;
        var buf = new double[segment];
        int k = 0;
        foreach (var s in starts)
        {
            for (int i = 0; i < segment; i++) buf[i] = x[s + i];
            var sp = SpectrumAnalyzer.Compute(buf, fs, window);
            first ??= sp;
            k++;
            if (acc == null)
            {
                acc = mode == AveragingMode.PeakHold ? sp.Amplitudes.ToArray() : sp.Amplitudes.Select(a => a * a).ToArray();
                continue;
            }
            for (int i = 0; i < acc.Length; i++)
            {
                double a = sp.Amplitudes[i];
                acc[i] = mode switch
                {
                    AveragingMode.PeakHold => Math.Max(acc[i], a),
                    AveragingMode.Exponential => acc[i] + (a * a - acc[i]) / Math.Min(k, Math.Max(1, exponentialAverages)),
                    _ => acc[i] + (a * a - acc[i]) / k,
                };
            }
        }
        var amp = mode == AveragingMode.PeakHold ? acc! : acc!.Select(Math.Sqrt).ToArray();
        return new Spectrum
        {
            Frequencies = first!.Frequencies, Amplitudes = amp, PhasesDeg = new double[amp.Length],
            Resolution = first.Resolution, SampleRate = fs, Window = window, Averages = k,
            Kind = mode switch { AveragingMode.PeakHold => "Espectro (retención de picos)", AveragingMode.Exponential => "Espectro (promedio exponencial)", _ => "Espectro promediado" },
        };
    }

    /// <summary>Densidad espectral de potencia de un solo lado por Welch: P = 2|X|² / (fs·Σw²).</summary>
    public static DensitySpectrum Psd(IReadOnlyList<double> x, double fs, int segment, double overlap = 0.5, WindowType window = WindowType.Hann)
    {
        var starts = Segments(x.Count, segment, overlap);
        var w = WindowFunctions.Create(window, segment);
        double s2 = w.Sum(v => v * v);
        int half = segment / 2 + 1;
        var acc = new double[half];
        var a = new Complex[segment];
        foreach (var s in starts)
        {
            double mean = 0;
            for (int i = 0; i < segment; i++) mean += x[s + i];
            mean /= segment;
            for (int i = 0; i < segment; i++) a[i] = new Complex((x[s + i] - mean) * w[i], 0);
            Fft.Transform(a, false);
            for (int k = 0; k < half; k++)
            {
                double scale = (k == 0 || (segment % 2 == 0 && k == segment / 2)) ? 1.0 : 2.0;
                acc[k] += scale * (a[k].Real * a[k].Real + a[k].Imaginary * a[k].Imaginary) / (fs * s2);
            }
        }
        for (int k = 0; k < half; k++) acc[k] /= starts.Length;
        double df = fs / segment;
        return new DensitySpectrum(Enumerable.Range(0, half).Select(k => k * df).ToArray(), acc, df, starts.Length);
    }

    /// <summary>
    /// Zoom FFT: espectro detallado de la banda [fCenter ± span/2] por mezcla compleja, filtrado y decimación.
    /// Usa toda la señal (resolución = 1/duración) pero con muy pocas líneas, ideal para separar 1X de 2× red o bandas laterales.
    /// </summary>
    public static Spectrum Zoom(IReadOnlyList<double> x, double fs, double fCenter, double span, WindowType window = WindowType.Hann)
    {
        if (span <= 0 || fCenter - span / 2 < 0 || fCenter + span / 2 > fs / 2) throw new ArgumentOutOfRangeException(nameof(span), "La banda de zoom debe quedar entre 0 y fs/2.");
        int n = x.Count;
        int dec = Math.Max(1, (int)Math.Floor(fs / (1.28 * span)));
        double fsd = fs / dec;
        double mean = x.Average();
        var re = new double[n];
        var im = new double[n];
        for (int i = 0; i < n; i++)
        {
            double th = -2 * Math.PI * fCenter * i / fs;
            double v = x[i] - mean;
            re[i] = v * Math.Cos(th); im[i] = v * Math.Sin(th);
        }
        if (dec > 1)
        {
            double fc = Math.Min(span / 2 * 1.15, fsd * 0.45);
            var lp = IirFilter.Butterworth(FilterKind.LowPass, 8, fs, fc);
            re = lp.FiltFilt(re); im = lp.FiltFilt(im);
        }
        int nd = n / dec;
        if (nd < 16) throw new ArgumentException("La señal es demasiado corta para ese zoom.");
        var w = WindowFunctions.Create(window, nd);
        double cg = WindowFunctions.CoherentGain(w);
        var z = new Complex[nd];
        for (int i = 0; i < nd; i++) z[i] = new Complex(re[i * dec], im[i * dec]) * w[i];
        Fft.Transform(z, false);
        double df = fsd / nd;
        var freqs = new List<double>(); var amps = new List<double>(); var ph = new List<double>();
        for (int j = 0; j < nd; j++)
        {
            int k = (j + nd / 2) % nd;         // reordena de −fsd/2 a +fsd/2
            int kk = k <= nd / 2 ? k : k - nd;
            double f = fCenter + kk * df;
            if (f < fCenter - span / 2 - 1e-9 || f > fCenter + span / 2 + 1e-9) continue;
            freqs.Add(f);
            amps.Add(2 * z[k].Magnitude / (nd * cg));
            ph.Add(Angles.Normalize(z[k].Phase * 180 / Math.PI));
        }
        var order = Enumerable.Range(0, freqs.Count).OrderBy(i => freqs[i]).ToArray();
        return new Spectrum
        {
            Frequencies = order.Select(i => freqs[i]).ToArray(), Amplitudes = order.Select(i => amps[i]).ToArray(),
            PhasesDeg = order.Select(i => ph[i]).ToArray(), Resolution = df, SampleRate = fs, Window = window,
            Kind = $"Zoom {fCenter:0.##} ± {span / 2:0.##} Hz",
        };
    }

    /// <summary>Cepstrum real de potencia (con ventana). La quefrencia está en segundos: un pico en τ indica una familia separada 1/τ Hz.</summary>
    public static CepstrumResult Cepstrum(IReadOnlyList<double> x, double fs, WindowType window = WindowType.Hann)
    {
        int n = x.Count;
        if (n < 16) throw new ArgumentException("Se necesitan al menos 16 muestras.");
        double mean = x.Average();
        var w = WindowFunctions.Create(window, n);
        var a = new Complex[n];
        for (int i = 0; i < n; i++) a[i] = new Complex((x[i] - mean) * w[i], 0);
        Fft.Transform(a, false);
        double maxP = 0;
        for (int k = 0; k < n; k++) maxP = Math.Max(maxP, a[k].Real * a[k].Real + a[k].Imaginary * a[k].Imaginary);
        double eps = Math.Max(maxP * 1e-12, 1e-300);
        for (int k = 0; k < n; k++)
            a[k] = new Complex(Math.Log(a[k].Real * a[k].Real + a[k].Imaginary * a[k].Imaginary + eps), 0);
        Fft.Transform(a, true);
        int half = n / 2;
        var q = new double[half];
        var c = new double[half];
        for (int i = 0; i < half; i++) { q[i] = i / fs; c[i] = a[i].Real; }
        return new CepstrumResult(q, c, fs);
    }

    /// <summary>Señal analítica por transformada de Hilbert (vía FFT).</summary>
    public static Complex[] Analytic(IReadOnlyList<double> x)
    {
        int n = x.Count;
        var a = new Complex[n];
        for (int i = 0; i < n; i++) a[i] = new Complex(x[i], 0);
        Fft.Transform(a, false);
        int half = n / 2;
        for (int k = 1; k < n; k++)
        {
            if (k < (n + 1) / 2) a[k] *= 2;
            else if (!(n % 2 == 0 && k == half)) a[k] = Complex.Zero;
        }
        Fft.Transform(a, true);
        return a;
    }

    /// <summary>Envolvente (módulo de la señal analítica) de la señal filtrada en la banda [low, high].</summary>
    public static double[] Envelope(IReadOnlyList<double> x, double fs, double low, double high, int order = 4)
    {
        var bp = IirFilter.Butterworth(FilterKind.BandPass, order, fs, low, high);
        var y = bp.FiltFilt(x);
        return Analytic(y).Select(c => c.Magnitude).ToArray();
    }

    /// <summary>
    /// Espectro de envolvente (demodulación de amplitud). Con la banda en una resonancia excitada por los impactos,
    /// muestra las frecuencias de defecto de rodamientos (BPFO, BPFI, BSF, FTF) y sus armónicos.
    /// </summary>
    public static Spectrum EnvelopeSpectrum(IReadOnlyList<double> x, double fs, double low, double high,
        WindowType window = WindowType.Hann, int order = 4)
    {
        var env = Envelope(x, fs, low, high, order);
        var sp = SpectrumAnalyzer.Compute(env, fs, window);
        return new Spectrum
        {
            Frequencies = sp.Frequencies, Amplitudes = sp.Amplitudes, PhasesDeg = sp.PhasesDeg, Resolution = sp.Resolution,
            SampleRate = fs, Window = window, Kind = $"Envolvente {low:0}–{high:0} Hz",
        };
    }

    /// <summary>
    /// Curtosis espectral por STFT: SK(f) = ⟨|X|⁴⟩ / ⟨|X|²⟩² − 2 (≈ 0 para ruido gaussiano, alta en bandas con impulsos).
    /// El bloque debe ser más corto que el intervalo entre impactos; con <paramref name="segment"/> = 0 se usan ≈ 3 ms.
    /// </summary>
    public static SpectralKurtosisResult SpectralKurtosis(IReadOnlyList<double> x, double fs, int segment = 0, double overlap = 0.75)
    {
        if (segment <= 0) segment = Math.Clamp(Fft.NextPowerOfTwo((int)(fs * 0.003)), 32, Math.Max(32, x.Count / 8));
        var starts = Segments(x.Count, segment, overlap);
        var w = WindowFunctions.Create(WindowType.Hann, segment);
        int half = segment / 2 + 1;
        var s2 = new double[half];
        var s4 = new double[half];
        var a = new Complex[segment];
        foreach (var s in starts)
        {
            for (int i = 0; i < segment; i++) a[i] = new Complex(x[s + i] * w[i], 0);
            Fft.Transform(a, false);
            for (int k = 0; k < half; k++)
            {
                double p = a[k].Real * a[k].Real + a[k].Imaginary * a[k].Imaginary;
                s2[k] += p; s4[k] += p * p;
            }
        }
        int m = starts.Length;
        var sk = new double[half];
        for (int k = 0; k < half; k++)
        {
            double m2 = s2[k] / m, m4 = s4[k] / m;
            sk[k] = m2 > 0 ? m4 / (m2 * m2) - 2 : 0;
        }
        sk[0] = 0; sk[half - 1] = 0;
        double df = fs / segment;
        return new SpectralKurtosisResult(Enumerable.Range(0, half).Select(k => k * df).ToArray(), sk, m);
    }

    /// <summary>
    /// Banda de envolvente automática por curtosis espectral: ancho ≥ 2,5 veces la mayor frecuencia de defecto
    /// (para contener sus bandas laterales) y por encima de 10X para esquivar el giro y sus armónicos.
    /// </summary>
    public static (double Low, double High) AutoEnvelopeBand(IReadOnlyList<double> x, double fs, double runningSpeedHz, double maxDefectHz = 0)
    {
        double nyq = fs / 2;
        double bw = Math.Clamp(Math.Max(fs / 16, 2.5 * maxDefectHz), fs / 40, nyq / 2);
        double minF = Math.Min(Math.Max(10 * runningSpeedHz, 200), nyq - bw * 1.2);
        var sk = SpectralKurtosis(x, fs);
        var (lo, hi, _) = sk.SuggestBand(bw, Math.Max(minF, fs / 100));
        hi = Math.Min(hi, nyq * 0.95);
        if (hi - lo < bw * 0.5) lo = Math.Max(fs / 100, hi - bw);
        return (lo, hi);
    }
}
