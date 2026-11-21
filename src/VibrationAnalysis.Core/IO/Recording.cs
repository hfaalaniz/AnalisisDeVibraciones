using System.Globalization;
using System.Text;
using VibrationAnalysis.Core.Balancing;
using VibrationAnalysis.Core.Dsp;

namespace VibrationAnalysis.Core.IO;

public sealed record Channel(string Name, VibrationUnit Unit, double[] Data);

/// <summary>Una adquisición: uno o más canales de vibración y, opcionalmente, el canal del tacómetro.</summary>
public sealed record Recording(double SampleRate, IReadOnlyList<Channel> Channels, double[]? Tach, double NominalRpm, string Source)
{
    public int Length => Channels.Count > 0 ? Channels[0].Data.Length : 0;
    public double Duration => Length / SampleRate;

    /// <summary>Velocidad medida con el tacómetro, o la nominal si no hay tacómetro.</summary>
    public double MeasuredRpm()
    {
        if (Tach is { Length: > 0 })
        {
            var p = Tachometer.DetectPulses(Tach, SampleRate);
            double r = Tachometer.Rpm(p);
            if (r > 0) return r;
        }
        return NominalRpm;
    }

    /// <summary>Lectura 1X (amplitud pico ∠ retraso de fase respecto del pulso del tacómetro) de un canal.</summary>
    public Phasor Reading1X(int channel, double? rpm = null)
    {
        double r = rpm ?? MeasuredRpm();
        if (r <= 0) throw new InvalidOperationException("No se conoce la velocidad de giro.");
        double t0 = 0;
        if (Tach is { Length: > 0 })
        {
            var p = Tachometer.DetectPulses(Tach, SampleRate);
            if (p.Length > 0) t0 = p[0];
        }
        var (a, ph) = SpectrumAnalyzer.FitSinusoid(Channels[channel].Data, SampleRate, r / 60, t0);
        return new Phasor(a, ph);
    }

    public bool HasPhaseReference => Tach is { Length: > 0 } && Tachometer.DetectPulses(Tach, SampleRate).Length >= 2;
}

/// <summary>Lectura de archivos CSV/TXT con columnas de tiempo y canales.</summary>
public static class CsvRecordingReader
{
    /// <summary>
    /// Formatos aceptados:
    /// <list type="bullet">
    /// <item>Cabecera opcional con nombres. Una columna llamada "t", "time" o "tiempo" (en s) define la frecuencia de muestreo.</item>
    /// <item>Línea de comentario "# fs=5120" o "# fs=5120 rpm=1480 unit=g" si no hay columna de tiempo.</item>
    /// <item>Una columna llamada "tach", "tacometro" o "tacómetro" se usa como referencia de fase.</item>
    /// <item>Separador coma, punto y coma o tabulación; decimales con punto (o coma si el separador es punto y coma).</item>
    /// </list>
    /// </summary>
    public static Recording Parse(string text, double defaultSampleRate = 0, VibrationUnit unit = VibrationUnit.VelocityMmS, double nominalRpm = 0)
    {
        var lines = text.Replace("\r", "").Split('\n');
        double fs = defaultSampleRate, rpm = nominalRpm;
        var u = unit;
        string[]? header = null;
        var rows = new List<double[]>();
        char sep = ',';
        bool sepKnown = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#'))
            {
                foreach (var kv in line.TrimStart('#').Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = kv.Split('=');
                    if (p.Length != 2) continue;
                    var key = p[0].Trim().ToLowerInvariant();
                    if (key == "fs" && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) fs = v;
                    else if (key == "rpm" && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) rpm = r;
                    else if (key == "unit") u = ParseUnit(p[1]) ?? u;
                }
                continue;
            }
            if (!sepKnown)
            {
                sep = line.Contains('\t') ? '\t' : line.Contains(';') ? ';' : ',';
                sepKnown = true;
            }
            var cells = line.Split(sep);
            var nums = new double[cells.Length];
            bool numeric = true;
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i].Trim().Trim('"');
                if (sep == ';') c = c.Replace(',', '.');
                if (!double.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out nums[i])) { numeric = false; break; }
            }
            if (!numeric)
            {
                if (header == null && rows.Count == 0) { header = cells.Select(c => c.Trim().Trim('"')).ToArray(); continue; }
                throw new FormatException($"Valor no numérico en la línea: {line}");
            }
            rows.Add(nums);
        }
        if (rows.Count < 16) throw new FormatException("El archivo tiene muy pocas muestras (mínimo 16).");
        int cols = rows[0].Length;
        header ??= Enumerable.Range(0, cols).Select(i => i == 0 && cols > 1 && LooksLikeTime(rows, 0) ? "t" : $"Canal {i + 1}").ToArray();
        int tCol = Array.FindIndex(header, h => h.Equals("t", StringComparison.OrdinalIgnoreCase) || h.StartsWith("time", StringComparison.OrdinalIgnoreCase) || h.StartsWith("tiempo", StringComparison.OrdinalIgnoreCase));
        int tachCol = Array.FindIndex(header, h => h.StartsWith("tach", StringComparison.OrdinalIgnoreCase) || h.StartsWith("tacó", StringComparison.OrdinalIgnoreCase) || h.StartsWith("taco", StringComparison.OrdinalIgnoreCase) || h.Equals("ref", StringComparison.OrdinalIgnoreCase));
        if (tCol >= 0)
        {
            double dt = (rows[^1][tCol] - rows[0][tCol]) / (rows.Count - 1);
            if (dt > 0) fs = 1 / dt;
        }
        if (fs <= 0) throw new FormatException("No se pudo determinar la frecuencia de muestreo: agregar una columna de tiempo o la línea \"# fs=...\".");
        var chans = new List<Channel>();
        for (int c = 0; c < cols; c++)
        {
            if (c == tCol || c == tachCol) continue;
            var d = new double[rows.Count];
            for (int i = 0; i < rows.Count; i++) d[i] = c < rows[i].Length ? rows[i][c] : 0;
            chans.Add(new Channel(header.Length > c ? header[c] : $"Canal {c + 1}", u, d));
        }
        if (chans.Count == 0) throw new FormatException("No hay columnas de vibración.");
        double[]? tach = tachCol >= 0 ? rows.Select(r => r[tachCol]).ToArray() : null;
        return new Recording(fs, chans, tach, rpm, "CSV");
    }

    private static bool LooksLikeTime(List<double[]> rows, int col)
    {
        for (int i = 1; i < Math.Min(rows.Count, 50); i++) if (rows[i][col] <= rows[i - 1][col]) return false;
        return true;
    }

    public static VibrationUnit? ParseUnit(string s) => s.Trim().ToLowerInvariant() switch
    {
        "g" => VibrationUnit.AccelerationG,
        "m/s2" or "m/s²" or "ms2" => VibrationUnit.AccelerationMs2,
        "mm/s" or "mms" => VibrationUnit.VelocityMmS,
        "um" or "µm" or "micron" => VibrationUnit.DisplacementUm,
        _ => null,
    };

    public static string Write(Recording r)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"# fs={r.SampleRate} rpm={r.MeasuredRpm():F1} unit={UnitToken(r.Channels[0].Unit)}\n");
        sb.Append("t");
        foreach (var c in r.Channels) sb.Append(',').Append(c.Name.Replace(',', ' '));
        if (r.Tach != null) sb.Append(",tach");
        sb.Append('\n');
        for (int i = 0; i < r.Length; i++)
        {
            sb.Append((i / r.SampleRate).ToString("0.######", CultureInfo.InvariantCulture));
            foreach (var c in r.Channels) sb.Append(',').Append(c.Data[i].ToString("0.######", CultureInfo.InvariantCulture));
            if (r.Tach != null) sb.Append(',').Append(r.Tach[i].ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static string UnitToken(VibrationUnit u) => u switch
    {
        VibrationUnit.AccelerationG => "g",
        VibrationUnit.AccelerationMs2 => "m/s2",
        VibrationUnit.VelocityMmS => "mm/s",
        _ => "um",
    };
}

/// <summary>Lectura de archivos WAV PCM (8/16/24/32 bits) o IEEE float.</summary>
public static class WavRecordingReader
{
    /// <param name="scale">Factor para pasar de la escala normalizada (±1) a unidades físicas (según la sensibilidad del sensor y la placa).</param>
    /// <param name="tachChannel">Índice del canal que contiene el tacómetro, o −1 si no hay.</param>
    public static Recording Read(byte[] data, double scale, VibrationUnit unit, int tachChannel = -1, double nominalRpm = 0)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        if (new string(br.ReadChars(4)) != "RIFF") throw new FormatException("No es un archivo WAV (falta RIFF).");
        br.ReadInt32();
        if (new string(br.ReadChars(4)) != "WAVE") throw new FormatException("No es un archivo WAV (falta WAVE).");
        int fmt = 0, channels = 0, rate = 0, bits = 0;
        byte[]? pcm = null;
        while (ms.Position + 8 <= ms.Length)
        {
            var id = new string(br.ReadChars(4));
            int size = br.ReadInt32();
            if (id == "fmt ")
            {
                fmt = br.ReadInt16(); channels = br.ReadInt16(); rate = br.ReadInt32();
                br.ReadInt32(); br.ReadInt16(); bits = br.ReadInt16();
                if (size > 16) br.ReadBytes(size - 16);
                if (fmt == 0xFFFE && size >= 40) fmt = bits == 32 ? 3 : 1; // WAVE_FORMAT_EXTENSIBLE (aproximación)
            }
            else if (id == "data") { pcm = br.ReadBytes(Math.Min(size, (int)(ms.Length - ms.Position))); }
            else br.ReadBytes(Math.Min(size + (size & 1), (int)(ms.Length - ms.Position)));
        }
        if (pcm == null || channels == 0) throw new FormatException("El WAV no contiene datos.");
        int bps = bits / 8;
        int frames = pcm.Length / (bps * channels);
        var ch = Enumerable.Range(0, channels).Select(_ => new double[frames]).ToArray();
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < channels; c++)
            {
                int o = (f * channels + c) * bps;
                double v = (fmt, bits) switch
                {
                    (3, 32) => BitConverter.ToSingle(pcm, o),
                    (3, 64) => BitConverter.ToDouble(pcm, o),
                    (_, 8) => (pcm[o] - 128) / 128.0,
                    (_, 16) => BitConverter.ToInt16(pcm, o) / 32768.0,
                    (_, 24) => ((pcm[o] | (pcm[o + 1] << 8) | ((sbyte)pcm[o + 2] << 16))) / 8388608.0,
                    (_, 32) => BitConverter.ToInt32(pcm, o) / 2147483648.0,
                    _ => throw new FormatException($"Formato WAV no soportado ({bits} bits)."),
                };
                ch[c][f] = v;
            }
        var list = new List<Channel>();
        for (int c = 0; c < channels; c++)
            if (c != tachChannel) list.Add(new Channel($"Canal {c + 1}", unit, ch[c].Select(v => v * scale).ToArray()));
        return new Recording(rate, list, tachChannel >= 0 && tachChannel < channels ? ch[tachChannel] : null, nominalRpm, "WAV");
    }
}
