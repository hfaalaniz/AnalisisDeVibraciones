using VibrationAnalysis.Core.Dsp;
using VibrationAnalysis.Core.IO;

namespace VibrationAnalysis.Core.Acquisition;

/// <summary>Tipo de transductor conectado a un canal analógico.</summary>
public enum SensorKind
{
    /// <summary>Acelerómetro IEPE/ICP (corriente constante 2–20 mA), sensibilidad en mV/g. Típico: 100 mV/g.</summary>
    AccelerometerIepe,
    /// <summary>Acelerómetro con salida de tensión (MEMS o con acondicionador externo), mV/g.</summary>
    Accelerometer,
    /// <summary>Sensor de velocidad (sísmico o IEPE de velocidad), mV/(mm/s). Típico: 4 mV/(mm/s) = 100 mV/(in/s).</summary>
    VelocitySensor,
    /// <summary>Sonda de proximidad (corrientes de Foucault) con acondicionador, mV/µm. Típico: 7,87 mV/µm = 200 mV/mil. Necesita entrada con acople DC.</summary>
    ProximityProbe,
    /// <summary>Tacómetro o keyphasor (pulso por vuelta), se usa como referencia de fase.</summary>
    Tachometer,
}

/// <summary>Configuración de un canal de entrada analógica.</summary>
public sealed class ChannelConfig
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "Canal";
    /// <summary>Canal físico del equipo: «Dev1/ai0» en NI-DAQmx, «0»/«1» (izquierdo/derecho) en la placa de sonido.</summary>
    public string Physical { get; set; } = "";
    public SensorKind Kind { get; set; } = SensorKind.AccelerometerIepe;
    /// <summary>Sensibilidad en mV por unidad (mV/g, mV/(mm/s) o mV/µm según el tipo).</summary>
    public double Sensitivity { get; set; } = 100;
    /// <summary>Corriente de excitación IEPE en mA (0 = sin excitación, la provee un acondicionador externo).</summary>
    public double IepeMilliamps { get; set; } = 4;
    /// <summary>Rango máximo esperado en las unidades del sensor (define la escala del DAQ).</summary>
    public double Range { get; set; } = 50;

    public VibrationUnit Unit => Kind switch
    {
        SensorKind.VelocitySensor => VibrationUnit.VelocityMmS,
        SensorKind.ProximityProbe => VibrationUnit.DisplacementUm,
        _ => VibrationUnit.AccelerationG,
    };

    public string UnitSymbol => Kind switch
    {
        SensorKind.VelocitySensor => "mV/(mm/s)",
        SensorKind.ProximityProbe => "mV/µm",
        SensorKind.Tachometer => "—",
        _ => "mV/g",
    };

    /// <summary>Tensión máxima esperada (V) para el rango configurado.</summary>
    public double RangeVolts => Kind == SensorKind.Tachometer ? 10 : Math.Max(0.1, Range * Sensitivity / 1000);
}

/// <summary>Configuración de una adquisición con placa de sonido o DAQ.</summary>
public sealed class AcquisitionSettings
{
    public string Device { get; set; } = "";
    public double SampleRate { get; set; } = 25600;
    /// <summary>Placa de sonido: tensión pico que corresponde al fondo de escala digital (medir o tomar de la hoja de datos).</summary>
    public double FullScaleVolts { get; set; } = 1.0;
    public int Bits { get; set; } = 16;
    /// <summary>Segundos de señal que se guardan en memoria mientras el equipo adquiere.</summary>
    public double BufferSeconds { get; set; } = 30;
    public List<ChannelConfig> Channels { get; set; } = new()
    {
        new ChannelConfig { Name = "Acelerómetro 1", Physical = "0" },
        new ChannelConfig { Name = "Tacómetro", Physical = "1", Kind = SensorKind.Tachometer, Sensitivity = 1000 },
    };
}

/// <summary>Búfer circular multicanal en volts: el equipo agrega bloques y la app toma los últimos segundos.</summary>
public sealed class StreamBuffer
{
    private readonly double[][] data;
    private long written;
    public int Channels { get; }
    public int Capacity { get; }
    public long TotalSamples { get { lock (data) return written; } }

    public StreamBuffer(int channels, int capacity)
    {
        if (channels < 1 || capacity < 16) throw new ArgumentOutOfRangeException(nameof(capacity));
        Channels = channels; Capacity = capacity;
        data = Enumerable.Range(0, channels).Select(_ => new double[capacity]).ToArray();
    }

    /// <summary>Agrega un bloque <paramref name="block"/>[canal][muestra] (todos los canales con el mismo largo).</summary>
    public void Append(double[][] block, int count = -1)
    {
        if (block.Length != Channels) throw new ArgumentException("El bloque no tiene la cantidad de canales del búfer.");
        int n = count < 0 ? block[0].Length : count;
        lock (data)
        {
            for (int i = 0; i < n; i++)
            {
                int p = (int)((written + i) % Capacity);
                for (int c = 0; c < Channels; c++) data[c][p] = block[c][i];
            }
            written += n;
        }
    }

    /// <summary>Últimas <paramref name="samples"/> muestras de cada canal (menos si todavía no llegaron).</summary>
    public double[][] Last(int samples)
    {
        lock (data)
        {
            int n = (int)Math.Min(Math.Min(samples, Capacity), written);
            var r = new double[Channels][];
            for (int c = 0; c < Channels; c++)
            {
                r[c] = new double[n];
                for (int i = 0; i < n; i++) r[c][i] = data[c][(int)((written - n + i) % Capacity)];
            }
            return r;
        }
    }

    public void Clear() { lock (data) written = 0; }
}

/// <summary>Convierte volts a unidades de ingeniería según la configuración de cada canal y arma la grabación.</summary>
public static class ChannelAssembler
{
    public sealed record ChannelInfo(string Name, double DcVolts, double RmsVolts, double PeakVolts);

    /// <summary>
    /// <paramref name="volts"/>[i] corresponde a <paramref name="channels"/>[i] (solo los habilitados, en orden).
    /// Las sondas de proximidad pierden la componente continua (tensión de gap, informada aparte).
    /// </summary>
    public static (Recording Recording, IReadOnlyList<ChannelInfo> Info) Build(double[][] volts, IReadOnlyList<ChannelConfig> channels,
        double sampleRate, double nominalRpm, string source)
    {
        if (volts.Length != channels.Count) throw new ArgumentException("Cantidad de canales distinta de la configuración.");
        var list = new List<Channel>();
        var info = new List<ChannelInfo>();
        double[]? tach = null;
        for (int c = 0; c < channels.Count; c++)
        {
            var v = volts[c];
            var cfg = channels[c];
            double mean = v.Length > 0 ? v.Average() : 0;
            double rms = v.Length > 0 ? Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / v.Length) : 0;
            double peak = v.Length > 0 ? v.Max(x => Math.Abs(x)) : 0;
            info.Add(new ChannelInfo(cfg.Name, mean, rms, peak));
            if (cfg.Kind == SensorKind.Tachometer) { tach ??= v.ToArray(); continue; }
            double k = 1000.0 / Math.Max(cfg.Sensitivity, 1e-9);
            bool removeDc = cfg.Kind == SensorKind.ProximityProbe;
            list.Add(new Channel(cfg.Name, cfg.Unit, v.Select(x => ((removeDc ? x - mean : x)) * k).ToArray()));
        }
        if (list.Count == 0) throw new InvalidOperationException("No hay ningún canal de vibración habilitado.");
        return (new Recording(sampleRate, list, tach, nominalRpm, source), info);
    }
}

public enum IssueLevel { Info, Warning, Error }

public sealed record QualityIssue(string Channel, IssueLevel Level, string Message);

/// <summary>Controles de calidad de la señal antes de confiar en una medición (saturación, cable cortado, señal plana, deriva).</summary>
public static class SignalQuality
{
    /// <summary>
    /// Evalúa un canal en volts. <paramref name="fullScaleVolts"/> es el límite de la entrada (0 = desconocido).
    /// <paramref name="dcCoupled"/>: con acople DC y sensor IEPE se puede verificar la tensión de polarización (bias).
    /// </summary>
    public static IReadOnlyList<QualityIssue> Assess(string channel, IReadOnlyList<double> volts, double sampleRate, SensorKind kind,
        double fullScaleVolts = 0, bool dcCoupled = false)
    {
        var res = new List<QualityIssue>();
        int n = volts.Count;
        if (n < 16) { res.Add(new QualityIssue(channel, IssueLevel.Error, "Muy pocas muestras.")); return res; }
        double mean = volts.Average(), rms = Math.Sqrt(volts.Sum(x => (x - mean) * (x - mean)) / n);
        if (kind == SensorKind.Tachometer)
        {
            if (Tachometer.DetectPulses(volts, sampleRate).Length < 2)
                res.Add(new QualityIssue(channel, IssueLevel.Warning, "No se detectan pulsos del tacómetro: revisar la cinta reflectiva, la distancia o el umbral."));
            return res;
        }
        if (rms < 1e-5)
            res.Add(new QualityIssue(channel, IssueLevel.Error, "Señal plana: sensor desconectado, sin excitación IEPE o canal equivocado."));
        if (fullScaleVolts > 0)
        {
            double lim = fullScaleVolts * 0.995;
            int clipped = volts.Count(x => Math.Abs(x) >= lim);
            if (clipped > Math.Max(2, n / 1000))
                res.Add(new QualityIssue(channel, IssueLevel.Error, $"Saturación: {clipped} muestras en el fondo de escala (±{fullScaleVolts:0.###} V). Bajar la ganancia o subir el rango."));
            else if (volts.Max(x => Math.Abs(x)) < fullScaleVolts * 0.01 && rms > 1e-5)
                res.Add(new QualityIssue(channel, IssueLevel.Info, "Nivel muy bajo respecto del fondo de escala: se puede subir la ganancia para mejorar la resolución."));
        }
        if (kind == SensorKind.AccelerometerIepe && dcCoupled)
        {
            if (mean > 18) res.Add(new QualityIssue(channel, IssueLevel.Error, $"Polarización IEPE {mean:0.0} V: circuito abierto (cable cortado o sensor desconectado)."));
            else if (mean < 2) res.Add(new QualityIssue(channel, IssueLevel.Error, $"Polarización IEPE {mean:0.0} V: cortocircuito en el cable o el conector."));
            else if (mean < 7 || mean > 14) res.Add(new QualityIssue(channel, IssueLevel.Warning, $"Polarización IEPE {mean:0.0} V fuera de lo habitual (8–14 V): revisar el sensor."));
        }
        else if (kind != SensorKind.ProximityProbe && rms > 1e-5 && Math.Abs(mean) > 5 * rms && Math.Abs(mean) > 0.05)
            res.Add(new QualityIssue(channel, IssueLevel.Warning, $"Componente continua alta ({mean:0.###} V): entrada con acople DC o sensor con deriva."));
        if (kind == SensorKind.ProximityProbe && dcCoupled && (mean > -2 || mean < -18))
            res.Add(new QualityIssue(channel, IssueLevel.Warning, $"Gap de la sonda {mean:0.00} V fuera del rango lineal típico (−2 a −18 V)."));
        if (rms > 1e-5 && (kind == SensorKind.AccelerometerIepe || kind == SensorKind.Accelerometer) && sampleRate > 50)
        {
            // Bajada de esquí: mucha energía por debajo de 2 Hz en la señal de aceleración = saturación del amplificador o golpe térmico
            var sp = SpectrumAnalyzer.Compute(volts, sampleRate, WindowType.Hann);
            double low = sp.BandRms(sp.Resolution, 2), total = sp.BandRms(sp.Resolution, sampleRate / 2);
            if (total > 0 && low / total > 0.5 && sp.Resolution < 1)
                res.Add(new QualityIssue(channel, IssueLevel.Warning, "Mucha energía por debajo de 2 Hz («bajada de esquí»): posible saturación del sensor, golpe o fijación floja."));
        }
        return res;
    }
}

/// <summary>Calibración con una fuente de referencia (calibrador de mano, típicamente 1 g RMS o 10 mm/s RMS a 159,2 Hz).</summary>
public static class SensorCalibration
{
    /// <summary>Sensibilidad (mV por unidad) a partir de la señal medida en volts con una referencia de amplitud RMS conocida.</summary>
    public static double SensitivityFromReference(IReadOnlyList<double> volts, double sampleRate, double referenceHz, double referenceRms)
    {
        if (referenceRms <= 0) throw new ArgumentOutOfRangeException(nameof(referenceRms));
        var (a, _) = SpectrumAnalyzer.FitSinusoid(volts, sampleRate, referenceHz);
        return a / Math.Sqrt(2) * 1000 / referenceRms;
    }
}

/// <summary>Equipo de adquisición en continuo (placa de sonido, DAQ o simulado).</summary>
public interface IAcquisitionDevice : IDisposable
{
    string Name { get; }
    bool IsRunning { get; }
    void Start(AcquisitionSettings settings);
    void Stop();
    /// <summary>Últimos <paramref name="seconds"/> segundos convertidos a unidades, más los controles de calidad.</summary>
    (Recording Recording, IReadOnlyList<QualityIssue> Issues) Snapshot(double seconds, double nominalRpm);
    string Status { get; }
}

/// <summary>Base común: búfer, conversión de unidades y calidad. Los equipos solo empujan bloques en volts.</summary>
public abstract class StreamingDeviceBase : IAcquisitionDevice
{
    protected AcquisitionSettings? Settings { get; private set; }
    protected StreamBuffer? Buffer { get; private set; }
    protected List<ChannelConfig> Active { get; private set; } = new();
    public abstract string Name { get; }
    public bool IsRunning { get; protected set; }
    public string LastError { get; protected set; } = "";
    /// <summary>Tensión del límite de la entrada para detectar saturación (0 = desconocido).</summary>
    protected virtual double InputLimitVolts(ChannelConfig c) => 0;
    protected virtual bool DcCoupled => false;

    public void Start(AcquisitionSettings settings)
    {
        if (IsRunning) Stop();
        Settings = settings;
        Active = settings.Channels.Where(c => c.Enabled).ToList();
        if (Active.Count == 0) throw new InvalidOperationException("No hay canales habilitados.");
        Buffer = new StreamBuffer(Active.Count, (int)Math.Max(1024, settings.BufferSeconds * settings.SampleRate));
        LastError = "";
        OnStart();
        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        OnStop();
    }

    protected abstract void OnStart();
    protected abstract void OnStop();

    /// <summary>Lo llama el equipo con cada bloque nuevo: [canal habilitado][muestra] en volts.</summary>
    protected void Push(double[][] volts, int count = -1) => Buffer?.Append(volts, count);

    /// <summary>Rehace el búfer si el equipo terminó usando otra frecuencia de muestreo que la pedida.</summary>
    protected void ResizeBuffer(AcquisitionSettings s) => Buffer = new StreamBuffer(Active.Count, (int)Math.Max(1024, s.BufferSeconds * s.SampleRate));

    public (Recording Recording, IReadOnlyList<QualityIssue> Issues) Snapshot(double seconds, double nominalRpm)
    {
        if (Buffer == null || Settings == null) throw new InvalidOperationException("El equipo no está adquiriendo.");
        var v = Buffer.Last((int)(seconds * Settings.SampleRate));
        if (v[0].Length < 256) throw new InvalidOperationException("Todavía no llegaron suficientes muestras del equipo.");
        var (rec, _) = ChannelAssembler.Build(v, Active, Settings.SampleRate, nominalRpm, Name);
        var issues = new List<QualityIssue>();
        for (int c = 0; c < Active.Count; c++)
            issues.AddRange(SignalQuality.Assess(Active[c].Name, v[c], Settings.SampleRate, Active[c].Kind, InputLimitVolts(Active[c]), DcCoupled));
        return (rec, issues);
    }

    public virtual string Status => !IsRunning ? (LastError.Length > 0 ? "Error: " + LastError : "detenido")
        : $"{Name} · {Active.Count} canal(es) · {Settings!.SampleRate:0} Hz · {Buffer!.TotalSamples / Settings.SampleRate:0.0} s recibidos";

    public virtual void Dispose() { Stop(); GC.SuppressFinalize(this); }
}

/// <summary>Equipo simulado: genera tensiones de un acelerómetro IEPE (1X + ruido) y un tacómetro, para probar sin hardware.</summary>
public sealed class SimulatedDevice : StreamingDeviceBase
{
    public override string Name => "Equipo simulado";
    public double Rpm { get; set; } = 1500;
    public double AmplitudeG { get; set; } = 0.5;
    public double FullScale { get; set; } = 5;
    private long t0;
    private readonly Random rng = new(1);

    protected override double InputLimitVolts(ChannelConfig c) => FullScale;
    protected override void OnStart() { t0 = 0; }
    protected override void OnStop() { }

    /// <summary>Genera <paramref name="samples"/> muestras (en una app real lo haría el hilo del equipo).</summary>
    public void Generate(int samples)
    {
        if (!IsRunning) return;
        double fs = Settings!.SampleRate, f1 = Rpm / 60;
        var block = new double[Active.Count][];
        for (int c = 0; c < Active.Count; c++)
        {
            block[c] = new double[samples];
            for (int i = 0; i < samples; i++)
            {
                double t = (t0 + i) / fs;
                double ph = f1 * t - Math.Floor(f1 * t);
                block[c][i] = Active[c].Kind == SensorKind.Tachometer
                    ? (ph < 0.05 ? 5 : 0)
                    : Math.Clamp((AmplitudeG * Math.Cos(2 * Math.PI * f1 * t - 1.0) + 0.01 * (rng.NextDouble() - 0.5)) * Active[c].Sensitivity / 1000, -FullScale, FullScale);
            }
        }
        t0 += samples;
        Push(block);
    }
}
