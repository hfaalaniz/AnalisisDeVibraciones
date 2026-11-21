using VibrationAnalysis.Core.Dsp;

namespace VibrationAnalysis.Core.Analysis;

/// <summary>Resultado del análisis de la firma de corriente del motor (MCSA).</summary>
public sealed record McsaResult
{
    public double LineHz { get; init; }
    /// <summary>Amplitud pico de la componente de red (A).</summary>
    public double LineAmplitude { get; init; }
    public double Rpm { get; init; }
    /// <summary>Deslizamiento (0–1). NaN si no se pudo determinar la velocidad.</summary>
    public double Slip { get; init; }
    /// <summary>Banda lateral inferior f·(1−2s): dB por debajo de la línea (más alto = mejor rotor).</summary>
    public double LowerSidebandDb { get; init; }
    public double UpperSidebandDb { get; init; }
    /// <summary>0 excelente … 5 severo (tabla de Thomson/Fenger para barras rotas).</summary>
    public int RotorSeverity { get; init; }
    public string RotorCondition { get; init; } = "";
    /// <summary>Bandas f ± fr (excentricidad del entrehierro): dB por debajo de la línea.</summary>
    public double EccentricityDb { get; init; }
    public double ThdPercent { get; init; }
    public IReadOnlyList<(int Order, double Percent)> Harmonics { get; init; } = Array.Empty<(int, double)>();
    public Spectrum Zoom { get; init; } = null!;
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// MCSA: con una pinza amperométrica en una fase del motor de inducción se detectan barras de rotor rotas o fisuradas
/// (bandas laterales a f·(1 ± 2s)), excentricidad del entrehierro (f ± fr), armónicos de la alimentación y desbalance entre fases.
/// Requiere registros largos (≥ 10 s) para separar bandas a pocos décimos de Hz de la línea.
/// </summary>
public static class MotorCurrentAnalysis
{
    /// <summary>Severidad por diferencia en dB entre la línea y la banda lateral inferior (rotor).</summary>
    public static (int Level, string Text) RotorRating(double db) => db switch
    {
        >= 54 => (0, "Excelente: sin indicios de barras rotas."),
        >= 48 => (1, "Bueno."),
        >= 42 => (2, "Moderado: seguir la tendencia."),
        >= 36 => (3, "Barras fisuradas o uniones de alta resistencia: aumentar la frecuencia de control."),
        >= 30 => (4, "Varias barras rotas: planificar la reparación."),
        _ => (5, "Severo: muchas barras rotas o anillo de cortocircuito dañado. Riesgo de falla."),
    };

    public static McsaResult Analyze(IReadOnlyList<double> current, double fs, double nominalLineHz, int poles, double rpm)
    {
        if (poles < 2 || poles % 2 != 0) throw new ArgumentOutOfRangeException(nameof(poles), "La cantidad de polos debe ser par (2, 4, 6…).");
        if (nominalLineHz <= 0 || nominalLineHz * 30 > fs) throw new ArgumentException("La frecuencia de muestreo debe ser al menos 30 veces la de red.");
        double T = current.Count / fs;
        var notes = new List<string>();
        if (T < 8) notes.Add($"Registro de {T:0.#} s: para separar bien las bandas laterales conviene ≥ 10 s.");

        // Frecuencia de red exacta (zoom ±3 Hz con interpolación)
        var coarse = AdvancedSpectrum.Zoom(current, fs, nominalLineHz, 6, WindowType.Hann);
        var peaks = SpectralFeatures.Peaks(coarse, 1);
        var top = peaks.Count > 0 ? peaks[0] : null;
        double f = top?.Frequency ?? nominalLineHz;
        double span = Math.Min(2 * f * 0.4, 30);
        var z = AdvancedSpectrum.Zoom(current, fs, f, span, WindowType.Hann);
        double line = z.PeakNear(f, Math.Max(z.Resolution * 2, 0.05)).Amplitude;
        double ns = 120 * f / poles;

        double res = z.Resolution;
        double Db(double a) => a > 0 && line > 0 ? 20 * Math.Log10(line / a) : double.PositiveInfinity;
        double floor = SpectralFeatures.LocalFloor(z, f, span / 2);
        double Side(double fc) => z.PeakNear(fc, Math.Max(res * 2, 0.04)).Amplitude;
        // Las bandas de excentricidad (f ± fr) quedan lejos de la línea: se miden en el espectro completo
        var wide = SpectrumAnalyzer.Compute(current, fs, WindowType.Hann);
        double lineWide = wide.PeakNear(f, Math.Max(wide.Resolution * 2, 0.1)).Amplitude;
        double WideDb(double fc) => fc <= 0 || fc >= fs / 2 ? double.PositiveInfinity : 20 * Math.Log10(lineWide / Math.Max(wide.PeakNear(fc, Math.Max(wide.Resolution * 2, 0.1)).Amplitude, 1e-300));
        double floorWide = SpectralFeatures.LocalFloor(wide, f + ns / 60, ns / 120);

        // Velocidad: la dada, o estimada por las bandas de excentricidad f ± fr
        double slip;
        if (rpm > 0 && rpm < ns) slip = (ns - rpm) / ns;
        else
        {
            slip = double.NaN;
            double best = 0;
            for (double s = 0.003; s <= 0.08; s += 0.0005)
            {
                double fr = (1 - s) * ns / 60;
                double a = wide.PeakNear(f + fr, wide.Resolution * 0.6).Amplitude + (f - fr > 0 ? wide.PeakNear(f - fr, wide.Resolution * 0.6).Amplitude : 0);
                if (a > best) { best = a; slip = s; }
            }
            if (best < floorWide * 6) { slip = double.NaN; notes.Add("No se pudo estimar la velocidad: cargá las rpm de la máquina (o usá el tacómetro)."); }
            else notes.Add("Velocidad estimada por las bandas de excentricidad.");
        }

        double lower = double.NaN, upper = double.NaN, ecc = double.NaN;
        int sev = -1; string cond = "Sin velocidad: no se evalúan las barras del rotor.";
        if (!double.IsNaN(slip))
        {
            double sb = 2 * slip * f;
            if (sb < 2 * res) notes.Add($"Las bandas laterales quedan a {sb:0.###} Hz de la línea: hace falta un registro más largo (resolución {res:0.###} Hz).");
            lower = Db(Side(f - sb));
            upper = Db(Side(f + sb));
            (sev, cond) = RotorRating(lower);
            double fr = (1 - slip) * ns / 60;
            ecc = Math.Min(WideDb(f - fr), WideDb(f + fr));
            if (ecc < 50) notes.Add($"Bandas de excentricidad a {ecc:0} dB de la línea: revisar entrehierro, rodamientos y alineación.");
        }

        // Armónicos de la alimentación (espectro completo con flat-top)
        var full = SpectrumAnalyzer.Compute(current, fs, WindowType.FlatTop);
        double a1 = full.PeakNear(f, 1).Amplitude;
        var harm = new List<(int, double)>();
        double sum2 = 0;
        for (int k = 2; k <= 25 && k * f < fs / 2; k++)
        {
            double a = full.PeakNear(k * f, 1).Amplitude;
            sum2 += a * a;
            if (k % 2 == 1) harm.Add((k, a1 > 0 ? a / a1 * 100 : 0));
        }
        double thd = a1 > 0 ? Math.Sqrt(sum2) / a1 * 100 : 0;
        if (thd > 8) notes.Add($"THD de corriente {thd:0.#} %: alimentación con armónicos (variador, cargas no lineales).");

        return new McsaResult
        {
            LineHz = f, LineAmplitude = line, Slip = slip, Rpm = double.IsNaN(slip) ? 0 : ns * (1 - slip),
            LowerSidebandDb = lower, UpperSidebandDb = upper, RotorSeverity = sev, RotorCondition = cond,
            EccentricityDb = ecc, ThdPercent = thd, Harmonics = harm, Zoom = z, Notes = notes,
        };
    }

    /// <summary>Desbalance de corrientes (NEMA): máxima desviación respecto del promedio, en %.</summary>
    public static double Unbalance(params double[] rms)
    {
        if (rms.Length < 2) return 0;
        double avg = rms.Average();
        return avg > 0 ? rms.Max(r => Math.Abs(r - avg)) / avg * 100 : 0;
    }

    /// <summary>Simulador de corriente de un motor de inducción (una fase), para probar sin equipo.</summary>
    public static double[] Simulate(double fs, double seconds, double lineHz, double ampA, int poles, double rpm,
        double brokenBarDb = 0, double eccentricityDb = 0, double fifthPercent = 0, double noiseA = 0, double phaseDeg = 0, int seed = 1)
    {
        int n = (int)(fs * seconds);
        var rnd = new Random(seed);
        double ns = 120 * lineHz / poles, s = (ns - rpm) / ns, fr = rpm / 60, ph = phaseDeg * Math.PI / 180;
        double kb = brokenBarDb > 0 ? Math.Pow(10, -brokenBarDb / 20) : 0;
        double ke = eccentricityDb > 0 ? Math.Pow(10, -eccentricityDb / 20) : 0;
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / fs, w = 2 * Math.PI * lineHz * t;
            double v = Math.Cos(w + ph)
                       + kb * (Math.Cos(2 * Math.PI * lineHz * (1 - 2 * s) * t + ph + 0.4) + Math.Cos(2 * Math.PI * lineHz * (1 + 2 * s) * t + ph - 0.9))
                       + ke * (Math.Cos(2 * Math.PI * (lineHz - fr) * t + 0.3) + Math.Cos(2 * Math.PI * (lineHz + fr) * t + 1.7))
                       + fifthPercent / 100 * Math.Cos(5 * (w + ph));
            double g = Math.Sqrt(-2 * Math.Log(1 - rnd.NextDouble())) * Math.Cos(2 * Math.PI * rnd.NextDouble());
            x[i] = ampA * v + noiseA * g;
        }
        return x;
    }
}

/// <summary>Resultado del análisis de ultrasonido (sensor de 20–100 kHz por placa de sonido rápida o DAQ).</summary>
public sealed record UltrasoundResult(double RmsDbuV, double PeakDbuV, double CrestFactor, double BandLow, double BandHigh, double RmsVolts);

/// <summary>
/// Ultrasonido para mantenimiento: nivel en dBµV en la banda ultrasónica (lubricación de rodamientos, fugas, descargas, válvulas)
/// y heterodinado a audio para escucharlo. Los criterios por aumento sobre la línea base siguen la práctica habitual de la industria.
/// </summary>
public static class UltrasoundAnalysis
{
    public static UltrasoundResult Analyze(IReadOnlyList<double> volts, double fs, double low = 20000, double high = 0)
    {
        if (fs < 80000) throw new ArgumentException($"Para ultrasonido hace falta muestrear a 96 kHz o más (ahora {fs:0} Hz).");
        if (high <= 0) high = Math.Min(80000, fs * 0.45);
        if (high <= low) throw new ArgumentException("La banda de ultrasonido queda fuera del muestreo: subí la frecuencia de muestreo.");
        var y = IirFilter.Butterworth(FilterKind.BandPass, 4, fs, low, high).FiltFilt(volts);
        var st = SignalTools.Statistics(y);
        double Db(double v) => 20 * Math.Log10(Math.Max(v, 1e-12) / 1e-6);
        return new UltrasoundResult(Db(st.Rms), Db(st.Peak), st.CrestFactor, low, high, st.Rms);
    }

    /// <summary>Heterodinado: traslada la banda alrededor de <paramref name="carrierHz"/> al audio (0–3 kHz) y la decima.</summary>
    public static double[] Heterodyne(IReadOnlyList<double> volts, double fs, double carrierHz = 38000, int outRate = 8000)
    {
        if (carrierHz + 3000 >= fs / 2) throw new ArgumentException("La portadora queda fuera del muestreo.");
        var bp = IirFilter.Butterworth(FilterKind.BandPass, 4, fs, Math.Max(100, carrierHz - 3000), carrierHz + 3000).FiltFilt(volts);
        var mixed = new double[bp.Length];
        for (int i = 0; i < bp.Length; i++) mixed[i] = bp[i] * Math.Cos(2 * Math.PI * carrierHz * i / fs);
        var lp = IirFilter.Butterworth(FilterKind.LowPass, 6, fs, 3000).FiltFilt(mixed);
        int dec = Math.Max(1, (int)Math.Round(fs / outRate));
        return Enumerable.Range(0, lp.Length / dec).Select(i => lp[i * dec]).ToArray();
    }

    /// <summary>Estado por aumento (dB) respecto de la línea base del mismo punto.</summary>
    public static (int Level, string Text) Condition(double deltaDb) => deltaDb switch
    {
        < 8 => (0, "Normal."),
        < 12 => (1, "Falta de lubricación: lubricar y volver a medir."),
        < 16 => (2, "Falla incipiente del rodamiento."),
        < 35 => (3, "Falla del rodamiento: planificar el cambio."),
        _ => (4, "Falla catastrófica inminente."),
    };
}
