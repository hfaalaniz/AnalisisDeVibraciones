using System.Numerics;

namespace VibrationAnalysis.Core.Dsp;

/// <summary>Punto de seguimiento de un orden: amplitud pico y retraso de fase respecto del tacómetro a una velocidad.</summary>
public sealed record TrackPoint(double TimeS, double Rpm, double Amplitude, double PhaseLagDeg);

public sealed record OrderTrack(double Order, IReadOnlyList<TrackPoint> Points)
{
    /// <summary>Puntos ordenados por velocidad (para Bode).</summary>
    public IReadOnlyList<TrackPoint> ByRpm() => Points.OrderBy(p => p.Rpm).ToList();
}

public sealed record CascadeSlice(double TimeS, double Rpm, Spectrum Spectrum);

/// <summary>Espectrograma: amplitud pico [bloque, línea].</summary>
public sealed record SpectrogramResult(double[] Times, double[] Rpm, double[] Frequencies, double[,] Amplitudes);

/// <summary>Velocidad crítica (resonancia) detectada en un arranque o parada.</summary>
public sealed record CriticalSpeed(double Rpm, double Amplitude, double PhaseShiftDeg, double AmplificationFactor);

/// <summary>
/// Análisis de órdenes y transitorios con tacómetro: remuestreo angular, espectro de órdenes, seguimiento de órdenes (Bode/Nyquist),
/// cascada, espectrograma y detección de velocidades críticas.
/// La fase sigue la misma convención que la lectura 1X: x = A·cos(n·θ − φ), con θ = 0 en cada pulso del tacómetro.
/// </summary>
public static class OrderTracking
{
    /// <summary>Velocidad (rpm) en cada intervalo entre pulsos, asignada al punto medio del intervalo.</summary>
    public static (double[] Times, double[] Rpm) InstantRpm(IReadOnlyList<double> pulses, int pulsesPerRev = 1)
    {
        int n = Math.Max(0, pulses.Count - 1);
        var t = new double[n];
        var r = new double[n];
        for (int i = 0; i < n; i++)
        {
            double dt = pulses[i + 1] - pulses[i];
            t[i] = (pulses[i] + pulses[i + 1]) / 2;
            r[i] = dt > 0 ? 60.0 / (dt * pulsesPerRev) : 0;
        }
        return (t, r);
    }

    /// <summary>Velocidad (rpm) interpolada en el instante t.</summary>
    public static double RpmAt(IReadOnlyList<double> pulses, double t, int pulsesPerRev = 1)
    {
        var (ts, rs) = InstantRpm(pulses, pulsesPerRev);
        if (ts.Length == 0) return 0;
        if (t <= ts[0]) return rs[0];
        if (t >= ts[^1]) return rs[^1];
        int i = Array.BinarySearch(ts, t);
        if (i >= 0) return rs[i];
        i = ~i;
        double f = (t - ts[i - 1]) / (ts[i] - ts[i - 1]);
        return rs[i - 1] + f * (rs[i] - rs[i - 1]);
    }

    /// <summary>
    /// Remuestreo angular: devuelve la señal con <paramref name="samplesPerRev"/> muestras por vuelta, desde el primer pulso
    /// hasta el último (vueltas completas). El tiempo de cada ángulo se interpola con Hermite monótono (PCHIP) sobre los pulsos
    /// y la señal con interpolación cúbica de Catmull-Rom.
    /// </summary>
    public static (double[] Signal, double[] Times, int Revolutions) AngularResample(IReadOnlyList<double> x, double fs, IReadOnlyList<double> pulses,
        int samplesPerRev, int pulsesPerRev = 1, int firstRev = 0, int revolutions = -1)
    {
        if (pulses.Count < 2) throw new ArgumentException("Se necesitan al menos 2 pulsos de tacómetro.");
        ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerRev, 4);
        int totalRevs = (pulses.Count - 1) / pulsesPerRev;
        if (revolutions < 0) revolutions = totalRevs - firstRev;
        revolutions = Math.Min(revolutions, totalRevs - firstRev);
        if (revolutions <= 0) throw new ArgumentException("No hay vueltas completas en el rango pedido.");
        var knotsA = new double[pulses.Count];
        var knotsT = new double[pulses.Count];
        for (int i = 0; i < pulses.Count; i++) { knotsA[i] = (double)i / pulsesPerRev; knotsT[i] = pulses[i]; }
        var slopes = PchipSlopes(knotsA, knotsT);
        int m = revolutions * samplesPerRev;
        var y = new double[m];
        var tt = new double[m];
        int seg = firstRev * pulsesPerRev;
        for (int j = 0; j < m; j++)
        {
            double a = firstRev + (double)j / samplesPerRev;
            while (seg < knotsA.Length - 2 && a > knotsA[seg + 1]) seg++;
            double t = Hermite(knotsA, knotsT, slopes, seg, a);
            tt[j] = t;
            y[j] = SampleAt(x, t * fs);
        }
        return (y, tt, revolutions);
    }

    /// <summary>
    /// Espectro de órdenes de todas las vueltas completas: el eje X está en órdenes (múltiplos de la velocidad de giro),
    /// por lo que las componentes síncronas quedan en una sola línea aunque la velocidad varíe.
    /// </summary>
    public static Spectrum OrderSpectrum(IReadOnlyList<double> x, double fs, IReadOnlyList<double> pulses, double maxOrder = 20,
        WindowType window = WindowType.Hann, int pulsesPerRev = 1)
    {
        int spr = Fft.NextPowerOfTwo((int)Math.Ceiling(2.56 * maxOrder));
        var anti = AntiAlias(x, fs, pulses, spr, pulsesPerRev);
        var (y, _, revs) = AngularResample(anti, fs, pulses, spr, pulsesPerRev);
        var sp = SpectrumAnalyzer.Compute(y, spr, window);
        int keep = sp.Frequencies.TakeWhile(o => o <= maxOrder + 1e-9).Count();
        return new Spectrum
        {
            Frequencies = sp.Frequencies.Take(keep).ToArray(), Amplitudes = sp.Amplitudes.Take(keep).ToArray(), PhasesDeg = sp.PhasesDeg.Take(keep).ToArray(),
            Resolution = 1.0 / revs, SampleRate = spr, Window = window, Kind = $"Espectro de órdenes ({revs} vueltas)",
        };
    }

    /// <summary>
    /// Seguimiento de un orden: por cada bloque de <paramref name="revsPerBlock"/> vueltas calcula la amplitud y el retraso de fase
    /// del orden (DFT síncrona, sin fuga porque el bloque tiene un número entero de vueltas). Ideal para arranques y paradas.
    /// </summary>
    public static OrderTrack Track(IReadOnlyList<double> x, double fs, IReadOnlyList<double> pulses, double order = 1, int revsPerBlock = 4,
        int stepRevs = 0, int pulsesPerRev = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(order);
        if (stepRevs <= 0) stepRevs = Math.Max(1, revsPerBlock / 2);
        // El orden fraccionario necesita un número de vueltas que lo haga entero (p. ej. 0,5X ⇒ bloques pares)
        if (Math.Abs(order * revsPerBlock - Math.Round(order * revsPerBlock)) > 1e-6)
            throw new ArgumentException("El orden por las vueltas del bloque debe dar un número entero (p. ej. 0,5X con 2, 4, 6… vueltas).");
        int spr = Math.Max(16, Fft.NextPowerOfTwo((int)Math.Ceiling(4 * order + 4)));
        int totalRevs = (pulses.Count - 1) / pulsesPerRev;
        var pts = new List<TrackPoint>();
        for (int r0 = 0; r0 + revsPerBlock <= totalRevs; r0 += stepRevs)
        {
            var (y, tt, revs) = AngularResample(x, fs, pulses, spr, pulsesPerRev, r0, revsPerBlock);
            int m = y.Length;
            double mean = y.Average();
            Complex acc = Complex.Zero;
            for (int j = 0; j < m; j++)
            {
                double th = 2 * Math.PI * order * j / spr;
                acc += (y[j] - mean) * new Complex(Math.Cos(th), -Math.Sin(th));
            }
            double amp = 2 * acc.Magnitude / m;
            double lag = Angles.Normalize(-acc.Phase * 180 / Math.PI);
            double tStart = pulses[r0 * pulsesPerRev], tEnd = pulses[(r0 + revs) * pulsesPerRev];
            pts.Add(new TrackPoint((tStart + tEnd) / 2, 60.0 * revs / (tEnd - tStart), amp, lag));
        }
        return new OrderTrack(order, pts);
    }

    /// <summary>Cascada (waterfall): espectros sucesivos de bloques de la señal, con la velocidad de cada bloque si hay tacómetro.</summary>
    public static IReadOnlyList<CascadeSlice> Cascade(IReadOnlyList<double> x, double fs, int block, double overlap = 0.5,
        WindowType window = WindowType.Hann, IReadOnlyList<double>? pulses = null, int maxSlices = 200)
    {
        var starts = AdvancedSpectrum.Segments(x.Count, block, overlap);
        if (starts.Length > maxSlices)
        {
            double k = (starts.Length - 1) / (double)(maxSlices - 1);
            starts = Enumerable.Range(0, maxSlices).Select(i => starts[(int)Math.Round(i * k)]).Distinct().ToArray();
        }
        var buf = new double[block];
        var res = new List<CascadeSlice>();
        foreach (var s in starts)
        {
            for (int i = 0; i < block; i++) buf[i] = x[s + i];
            double tm = (s + block / 2.0) / fs;
            double rpm = pulses is { Count: >= 2 } ? RpmAt(pulses, tm) : 0;
            res.Add(new CascadeSlice(tm, rpm, SpectrumAnalyzer.Compute(buf, fs, window)));
        }
        return res;
    }

    /// <summary>Espectrograma (STFT) de amplitud pico, hasta <paramref name="maxFrequency"/> Hz (0 = Nyquist).</summary>
    public static SpectrogramResult Spectrogram(IReadOnlyList<double> x, double fs, int block, double overlap = 0.75,
        WindowType window = WindowType.Hann, IReadOnlyList<double>? pulses = null, double maxFrequency = 0, int maxSlices = 400)
    {
        var slices = Cascade(x, fs, block, overlap, window, pulses, maxSlices);
        var f = slices[0].Spectrum.Frequencies;
        int nf = maxFrequency > 0 ? f.TakeWhile(v => v <= maxFrequency).Count() : f.Length;
        var a = new double[slices.Count, nf];
        for (int i = 0; i < slices.Count; i++)
            for (int k = 0; k < nf; k++) a[i, k] = slices[i].Spectrum.Amplitudes[k];
        return new SpectrogramResult(slices.Select(s => s.TimeS).ToArray(), slices.Select(s => s.Rpm).ToArray(), f.Take(nf).ToArray(), a);
    }

    /// <summary>
    /// Velocidades críticas en un seguimiento 1X: máximos de amplitud con giro de fase. Factor de amplificación Q por el método
    /// de media potencia (API 684): Q = N_c / (N₂ − N₁), con N₁ y N₂ donde la amplitud cae a pico/√2.
    /// </summary>
    public static IReadOnlyList<CriticalSpeed> CriticalSpeeds(OrderTrack track, double minPhaseShiftDeg = 45)
    {
        var p = track.ByRpm();
        int n = p.Count;
        var res = new List<CriticalSpeed>();
        if (n < 7) return res;
        var amp = Smooth(p.Select(q => q.Amplitude).ToArray(), 2);
        var ph = Unwrap(p.Select(q => q.PhaseLagDeg).ToArray());
        double maxAmp = amp.Max();
        for (int i = 2; i < n - 2; i++)
        {
            if (!(amp[i] >= amp[i - 1] && amp[i] > amp[i + 1] && amp[i] >= amp[i - 2] && amp[i] > amp[i + 2])) continue;
            if (amp[i] < maxAmp * 0.25) continue;
            double half = amp[i] / Math.Sqrt(2);
            int a = i; while (a > 0 && amp[a] > half) a--;
            int b = i; while (b < n - 1 && amp[b] > half) b++;
            if (amp[a] > half || amp[b] > half) continue;   // el pico no baja a media potencia dentro del barrido
            double n1 = Lerp(p[a].Rpm, p[a + 1].Rpm, amp[a], amp[a + 1], half);
            double n2 = Lerp(p[b - 1].Rpm, p[b].Rpm, amp[b - 1], amp[b], half);
            double shift = Math.Abs(ph[b] - ph[a]);
            if (shift < minPhaseShiftDeg) continue;
            double q = n2 > n1 ? p[i].Rpm / (n2 - n1) : 0;
            res.Add(new CriticalSpeed(p[i].Rpm, amp[i], shift, q));
        }
        return res;
    }

    // ───────── utilidades ─────────

    private static double[] AntiAlias(IReadOnlyList<double> x, double fs, IReadOnlyList<double> pulses, int spr, int ppr)
    {
        // Filtro antialias para la menor velocidad del registro: corta por debajo de spr/2.56 órdenes
        var (_, rpm) = InstantRpm(pulses, ppr);
        double minHz = rpm.Length > 0 ? rpm.Min() / 60 : 0;
        double fc = minHz * spr / 2.56;
        if (minHz <= 0 || fc >= fs * 0.45) return x as double[] ?? x.ToArray();
        return IirFilter.Butterworth(FilterKind.LowPass, 8, fs, fc).FiltFilt(x);
    }

    private static double SampleAt(IReadOnlyList<double> x, double pos)
    {
        int n = x.Count;
        if (pos <= 0) return x[0];
        if (pos >= n - 1) return x[n - 1];
        int i = (int)pos;
        double f = pos - i;
        double p0 = x[Math.Max(0, i - 1)], p1 = x[i], p2 = x[Math.Min(n - 1, i + 1)], p3 = x[Math.Min(n - 1, i + 2)];
        return p1 + 0.5 * f * (p2 - p0 + f * (2 * p0 - 5 * p1 + 4 * p2 - p3 + f * (3 * (p1 - p2) + p3 - p0)));
    }

    private static double[] PchipSlopes(double[] x, double[] y)
    {
        int n = x.Length;
        var d = new double[n];
        var h = new double[n - 1];
        var delta = new double[n - 1];
        for (int i = 0; i < n - 1; i++) { h[i] = x[i + 1] - x[i]; delta[i] = (y[i + 1] - y[i]) / h[i]; }
        if (n == 2) { d[0] = d[1] = delta[0]; return d; }
        for (int i = 1; i < n - 1; i++)
        {
            if (delta[i - 1] * delta[i] <= 0) { d[i] = 0; continue; }
            double w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1];
            d[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]);
        }
        d[0] = delta[0]; d[n - 1] = delta[n - 2];
        return d;
    }

    private static double Hermite(double[] x, double[] y, double[] d, int i, double a)
    {
        double h = x[i + 1] - x[i], t = (a - x[i]) / h, t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * y[i] + (t3 - 2 * t2 + t) * h * d[i] + (-2 * t3 + 3 * t2) * y[i + 1] + (t3 - t2) * h * d[i + 1];
    }

    private static double[] Smooth(double[] v, int half)
    {
        var r = new double[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            int a = Math.Max(0, i - half), b = Math.Min(v.Length - 1, i + half);
            double s = 0; for (int k = a; k <= b; k++) s += v[k];
            r[i] = s / (b - a + 1);
        }
        return r;
    }

    /// <summary>Desenvuelve una secuencia de fases (°) para que no salte de 359° a 0°.</summary>
    public static double[] Unwrap(double[] deg)
    {
        var r = new double[deg.Length];
        if (deg.Length == 0) return r;
        r[0] = deg[0];
        for (int i = 1; i < deg.Length; i++)
        {
            double d = deg[i] - deg[i - 1];
            d -= 360 * Math.Round(d / 360);
            r[i] = r[i - 1] + d;
        }
        return r;
    }

    private static double Lerp(double x0, double x1, double y0, double y1, double y)
        => Math.Abs(y1 - y0) < 1e-300 ? (x0 + x1) / 2 : x0 + (y - y0) / (y1 - y0) * (x1 - x0);
}
