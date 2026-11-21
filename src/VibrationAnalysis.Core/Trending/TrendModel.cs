using VibrationAnalysis.Core.Analysis;
using VibrationAnalysis.Core.Dsp;
using VibrationAnalysis.Core.Model;

namespace VibrationAnalysis.Core.Trending;

public enum MeasureDirection { Horizontal, Vertical, Axial, Radial }

public enum AlarmLevel { Normal, Alert, Danger }

/// <summary>Tipo de límite de una regla de alarma.</summary>
public enum AlarmKind
{
    /// <summary>Valores fijos de alerta y peligro.</summary>
    Absolute,
    /// <summary>Múltiplos de la línea base del punto (p. ej. ×2 alerta, ×4 peligro).</summary>
    BaselineRatio,
    /// <summary>Límites estadísticos: media + k·σ de las mediciones de la línea base.</summary>
    Statistical,
    /// <summary>Zonas de severidad ISO 20816 de la clase de máquina (B/C = alerta, C/D = peligro). Solo para velocidad RMS.</summary>
    Iso,
}

public sealed class AlarmRule
{
    public string Feature { get; set; } = FeatureKeys.VelocityRms;
    public AlarmKind Kind { get; set; } = AlarmKind.Absolute;
    /// <summary>Alerta (valor absoluto, múltiplo de la base o k de sigma según el tipo).</summary>
    public double Alert { get; set; }
    /// <summary>Peligro (valor absoluto, múltiplo de la base o k de sigma según el tipo).</summary>
    public double Danger { get; set; }
    public bool Enabled { get; set; } = true;

    public AlarmRule() { }
    public AlarmRule(string feature, AlarmKind kind, double alert, double danger) { Feature = feature; Kind = kind; Alert = alert; Danger = danger; }
}

/// <summary>Punto de medición: ubicación y dirección del sensor en una máquina.</summary>
public sealed class MeasurementPoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Punto";
    public MeasureDirection Direction { get; set; } = MeasureDirection.Horizontal;
    public List<AlarmRule> Alarms { get; set; } = AlarmDefaults.Create();
    /// <summary>Ids de las mediciones que forman la línea base (vacío = las primeras <see cref="AlarmDefaults.BaselineCount"/>).</summary>
    public List<string> BaselineIds { get; set; } = new();
    public string Notes { get; set; } = "";
}

public sealed class MachineAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Máquina";
    public string Area { get; set; } = "";
    /// <summary>Datos técnicos (velocidad, rodamiento, engranaje, clase ISO) usados para extraer los indicadores.</summary>
    public Machine Machine { get; set; } = new();
    public List<MeasurementPoint> Points { get; set; } = new();
}

public sealed class Plant
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Planta";
    public List<MachineAsset> Machines { get; set; } = new();
}

/// <summary>Ruta de medición: lista ordenada de puntos para recorrer en planta.</summary>
public sealed class MeasurementRoute
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Ruta";
    public List<string> PointIds { get; set; } = new();
}

/// <summary>Medición guardada: indicadores escalares (para tendencias) y, opcionalmente, la señal completa.</summary>
public sealed class MeasurementRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PointId { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public double Rpm { get; set; }
    public double SampleRate { get; set; }
    public Dictionary<string, double> Features { get; set; } = new();
    /// <summary>Archivo CSV con la señal (relativo a la carpeta de la base), vacío si no se guardó.</summary>
    public string WaveformFile { get; set; } = "";
    public string Notes { get; set; } = "";

    public double Get(string key) => Features.TryGetValue(key, out var v) ? v : double.NaN;
}

/// <summary>Nombres de los indicadores que se guardan en cada medición.</summary>
public static class FeatureKeys
{
    public const string VelocityRms = "vel_rms";
    public const string AccelerationRms = "acc_rms";
    public const string AccelerationPeak = "acc_pico";
    public const string DisplacementPp = "desp_pp";
    public const string CrestFactor = "factor_cresta";
    public const string Kurtosis = "curtosis";
    public const string Amp1X = "1x";
    public const string Phase1X = "1x_fase";
    public const string Amp2X = "2x";
    public const string Amp3X = "3x";
    public const string SubSync = "subsincronico";
    public const string HighFrequency = "hf_rms";
    public const string EnvelopeRms = "env_rms";
    public const string Bpfo = "env_bpfo";
    public const string Bpfi = "env_bpfi";
    public const string Gmf = "gmf";
    public const string Temperature = "temperatura";

    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        [VelocityRms] = "Velocidad RMS 10–1000 Hz (mm/s)",
        [AccelerationRms] = "Aceleración RMS (g)",
        [AccelerationPeak] = "Aceleración pico (g)",
        [DisplacementPp] = "Desplazamiento pico a pico (µm)",
        [CrestFactor] = "Factor de cresta (aceleración)",
        [Kurtosis] = "Curtosis (aceleración)",
        [Amp1X] = "1X (mm/s pico)",
        [Phase1X] = "Fase 1X (°)",
        [Amp2X] = "2X (mm/s pico)",
        [Amp3X] = "3X (mm/s pico)",
        [SubSync] = "Subsincrónico 0,3–0,8X (mm/s pico)",
        [HighFrequency] = "Alta frecuencia > 1 kHz (g RMS)",
        [EnvelopeRms] = "Envolvente RMS (g)",
        [Bpfo] = "Envolvente en BPFO (g pico)",
        [Bpfi] = "Envolvente en BPFI (g pico)",
        [Gmf] = "Frecuencia de engrane (mm/s pico)",
        [Temperature] = "Temperatura (°C)",
    };

    public static string Name(string key) => Names.TryGetValue(key, out var n) ? n : key;
}

public static class AlarmDefaults
{
    public const int BaselineCount = 3;

    /// <summary>Reglas por defecto: ISO para la velocidad global, base ×2/×4 para 1X, envolvente y AF, absolutas para curtosis y factor de cresta.</summary>
    public static List<AlarmRule> Create() => new()
    {
        new AlarmRule(FeatureKeys.VelocityRms, AlarmKind.Iso, 0, 0),
        new AlarmRule(FeatureKeys.Amp1X, AlarmKind.BaselineRatio, 2, 4),
        new AlarmRule(FeatureKeys.Amp2X, AlarmKind.BaselineRatio, 2.5, 5),
        new AlarmRule(FeatureKeys.EnvelopeRms, AlarmKind.BaselineRatio, 2, 4),
        new AlarmRule(FeatureKeys.HighFrequency, AlarmKind.BaselineRatio, 2, 4),
        new AlarmRule(FeatureKeys.Kurtosis, AlarmKind.Absolute, 4, 6),
        new AlarmRule(FeatureKeys.CrestFactor, AlarmKind.Absolute, 5, 7),
        new AlarmRule(FeatureKeys.AccelerationRms, AlarmKind.Statistical, 3, 6),
    };
}
