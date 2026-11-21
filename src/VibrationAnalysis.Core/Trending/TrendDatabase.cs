using System.Globalization;
using System.Text;
using System.Text.Json;
using VibrationAnalysis.Core.IO;
using VibrationAnalysis.Core.Simulation;

namespace VibrationAnalysis.Core.Trending;

/// <summary>Contenido del archivo <c>base.json</c>: jerarquía de activos y rutas.</summary>
public sealed class TrendCatalog
{
    public int Version { get; set; } = 1;
    public List<Plant> Plants { get; set; } = new();
    public List<MeasurementRoute> Routes { get; set; } = new();
}

/// <summary>
/// Base de datos de mediciones sin dependencias, en una carpeta:
/// <c>base.json</c> (plantas → máquinas → puntos, alarmas y rutas), <c>mediciones/&lt;punto&gt;.jsonl</c> (una medición por línea,
/// solo se agrega) y <c>senales/&lt;medición&gt;.csv</c> (señal completa opcional). Se puede copiar, respaldar o versionar como una carpeta.
/// </summary>
public sealed class TrendDatabase
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions Line = new() { WriteIndented = false };
    private readonly Dictionary<string, List<MeasurementRecord>> cache = new();

    public string Folder { get; }
    public TrendCatalog Catalog { get; private set; } = new();

    public TrendDatabase(string folder)
    {
        Folder = folder;
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "mediciones"));
        Directory.CreateDirectory(Path.Combine(folder, "senales"));
        Load();
    }

    private string CatalogPath => Path.Combine(Folder, "base.json");
    private string MeasurementsPath(string pointId) => Path.Combine(Folder, "mediciones", Safe(pointId) + ".jsonl");

    public void Load()
    {
        cache.Clear();
        Catalog = File.Exists(CatalogPath)
            ? JsonSerializer.Deserialize<TrendCatalog>(File.ReadAllText(CatalogPath), Opts) ?? new TrendCatalog()
            : new TrendCatalog();
    }

    /// <summary>Guarda el catálogo de forma atómica (archivo temporal + reemplazo) para no corromperlo ante un corte.</summary>
    public void Save()
    {
        string tmp = CatalogPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Catalog, Opts), new UTF8Encoding(false));
        File.Move(tmp, CatalogPath, overwrite: true);
    }

    public IEnumerable<(Plant Plant, MachineAsset Machine, MeasurementPoint Point)> AllPoints() =>
        from p in Catalog.Plants from m in p.Machines from pt in m.Points select (p, m, pt);

    public (Plant Plant, MachineAsset Machine, MeasurementPoint Point)? FindPoint(string pointId) =>
        AllPoints().Where(x => x.Point.Id == pointId).Select(x => ((Plant, MachineAsset, MeasurementPoint)?)x).FirstOrDefault();

    public IReadOnlyList<MeasurementRecord> Measurements(string pointId)
    {
        if (cache.TryGetValue(pointId, out var list)) return list;
        list = new List<MeasurementRecord>();
        var path = MeasurementsPath(pointId);
        if (File.Exists(path))
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { var r = JsonSerializer.Deserialize<MeasurementRecord>(line, Line); if (r != null) list.Add(r); }
                catch (JsonException) { /* línea dañada (corte de energía): se ignora */ }
            }
        list.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        cache[pointId] = list;
        return list;
    }

    /// <summary>Agrega una medición al punto. Si se pasa la señal, se guarda en CSV junto a la medición.</summary>
    public MeasurementRecord AddMeasurement(MeasurementRecord record, Recording? waveform = null)
    {
        if (string.IsNullOrEmpty(record.PointId)) throw new ArgumentException("La medición no tiene punto asignado.");
        if (waveform != null)
        {
            string rel = Path.Combine("senales", record.Id + ".csv");
            File.WriteAllText(Path.Combine(Folder, rel), CsvRecordingReader.Write(waveform), new UTF8Encoding(false));
            record.WaveformFile = rel;
        }
        File.AppendAllText(MeasurementsPath(record.PointId), JsonSerializer.Serialize(record, Line) + "\n", new UTF8Encoding(false));
        var list = (List<MeasurementRecord>)Measurements(record.PointId);
        list.Add(record);
        list.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return record;
    }

    /// <summary>Borra una medición (reescribe el archivo del punto).</summary>
    public void DeleteMeasurement(string pointId, string recordId)
    {
        var list = (List<MeasurementRecord>)Measurements(pointId);
        var r = list.FirstOrDefault(x => x.Id == recordId);
        if (r == null) return;
        list.Remove(r);
        if (!string.IsNullOrEmpty(r.WaveformFile)) { try { File.Delete(Path.Combine(Folder, r.WaveformFile)); } catch (IOException) { } }
        RewritePoint(pointId, list);
    }

    /// <summary>Elimina un punto con todas sus mediciones.</summary>
    public void DeletePoint(string pointId)
    {
        foreach (var r in Measurements(pointId).Where(r => !string.IsNullOrEmpty(r.WaveformFile)))
            try { File.Delete(Path.Combine(Folder, r.WaveformFile)); } catch (IOException) { }
        try { File.Delete(MeasurementsPath(pointId)); } catch (IOException) { }
        cache.Remove(pointId);
        foreach (var m in Catalog.Plants.SelectMany(p => p.Machines)) m.Points.RemoveAll(p => p.Id == pointId);
        foreach (var rt in Catalog.Routes) rt.PointIds.Remove(pointId);
    }

    public Recording? LoadWaveform(MeasurementRecord r, double nominalRpm = 0)
    {
        if (string.IsNullOrEmpty(r.WaveformFile)) return null;
        var path = Path.Combine(Folder, r.WaveformFile);
        return File.Exists(path) ? CsvRecordingReader.Parse(File.ReadAllText(path), r.SampleRate, Dsp.VibrationUnit.VelocityMmS, nominalRpm > 0 ? nominalRpm : r.Rpm) : null;
    }

    /// <summary>Exporta la tendencia de un punto (todas las mediciones y sus indicadores) a CSV con separador «;».</summary>
    public string ExportCsv(string pointId)
    {
        var list = Measurements(pointId);
        var keys = list.SelectMany(r => r.Features.Keys).Distinct().ToList();
        var sb = new StringBuilder();
        sb.Append("fecha;rpm;").AppendLine(string.Join(";", keys));
        foreach (var r in list)
        {
            sb.Append(r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(';').Append(r.Rpm.ToString("0.##", CultureInfo.InvariantCulture));
            foreach (var k in keys) sb.Append(';').Append(r.Features.TryGetValue(k, out var v) ? v.ToString("G6", CultureInfo.InvariantCulture) : "");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private void RewritePoint(string pointId, IEnumerable<MeasurementRecord> list)
    {
        var path = MeasurementsPath(pointId);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, string.Concat(list.Select(r => JsonSerializer.Serialize(r, Line) + "\n")), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    /// <summary>
    /// Crea una planta de demostración con historial simulado de 12 meses: un ventilador con un rodamiento que se degrada
    /// y una bomba estable con desbalance creciente, para ver tendencias, pronósticos y alarmas.
    /// </summary>
    public Plant CreateDemo(DateTime? end = null)
    {
        var stop = end ?? DateTime.Now;
        var plant = new Plant { Name = "Planta de demostración" };
        var fan = new MachineAsset { Name = "Ventilador VT-01", Area = "Secado" };
        fan.Machine = new Model.Machine { Name = fan.Name, Rpm = 1500, Bearing = new Analysis.BearingGeometry(9, 7.94, 39.0, 0), MachineClass = Analysis.MachineClass.Iso10816_3_Group2Rigid };
        var pump = new MachineAsset { Name = "Bomba BC-02", Area = "Agua de proceso" };
        pump.Machine = new Model.Machine { Name = pump.Name, Rpm = 2950, MachineClass = Analysis.MachineClass.Iso10816_3_Group2Rigid };
        var fanH = new MeasurementPoint { Name = "Lado acople · Horizontal", Direction = MeasureDirection.Horizontal };
        var fanA = new MeasurementPoint { Name = "Lado acople · Axial", Direction = MeasureDirection.Axial };
        var pumpH = new MeasurementPoint { Name = "Rodamiento LA · Horizontal", Direction = MeasureDirection.Horizontal };
        fan.Points.AddRange(new[] { fanH, fanA });
        pump.Points.Add(pumpH);
        plant.Machines.AddRange(new[] { fan, pump });
        Catalog.Plants.Add(plant);
        Catalog.Routes.Add(new MeasurementRoute { Name = "Ruta mensual demo", PointIds = new List<string> { fanH.Id, fanA.Id, pumpH.Id } });

        for (int k = 0; k < 12; k++)
        {
            var date = stop.AddDays(-30.4 * (11 - k)).Date.AddHours(9 + k % 3);
            double life = k / 11.0;
            // Rodamiento: sin defecto los primeros meses, luego crece de forma exponencial
            double bearing = life < 0.4 ? 0 : 0.05 * Math.Exp(4.2 * (life - 0.4));
            foreach (var (pt, axial) in new[] { (fanH, false), (fanA, true) })
            {
                var s = new SimulatorSettings
                {
                    Rpm = 1500, Sensors = 1, Seed = 100 + k + (axial ? 50 : 0), NoiseMmS = 0.04, SampleRate = 5120, Samples = 8192,
                    Unbalance1 = new Balancing.Phasor(axial ? 500 : 900, 70), Unbalance2 = new Balancing.Phasor(0, 0),
                    BearingOuterRace = bearing * (axial ? 0.6 : 1), Misalignment = axial ? 0.4 : 0.1,
                };
                var rec = FeatureExtractor.Extract(new RotorSimulator(s).Acquire(), 0, fan.Machine, pt.Id);
                rec.Timestamp = date;
                AddMeasurement(rec);
            }
            var sp = new SimulatorSettings
            {
                Rpm = 2950, Sensors = 1, Seed = 300 + k, NoiseMmS = 0.04, SampleRate = 5120, Samples = 8192,
                Unbalance1 = new Balancing.Phasor(250 + 25 * k, 30), Unbalance2 = new Balancing.Phasor(0, 0),
            };
            var pr = FeatureExtractor.Extract(new RotorSimulator(sp).Acquire(), 0, pump.Machine, pumpH.Id);
            pr.Timestamp = date.AddMinutes(20);
            AddMeasurement(pr);
        }
        Save();
        return plant;
    }
}
