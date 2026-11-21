using System.Globalization;
using System.Text;
using VibrationAnalysis.Core.Dsp;

namespace VibrationAnalysis.Core.IO;

/// <summary>
/// Protocolo del sensor USB/serie. Dos variantes, elegidas por la cabecera:
/// <list type="bullet">
/// <item><b>Texto (v1)</b>: <c>#VA fs=3200 ch=2 unit=g tach=1</c> y luego una línea por muestra <c>v1,v2,…,tach</c>.</item>
/// <item><b>Binario (v2)</b>: <c>#VA2 fs=26667 ch=4 unit=g tach=1 scale=0.000061 names=X,Y,Z,X2 sensor=IIS3DWB</c> seguida de tramas
/// <c>A5 5A | seq u16 | n u16 | ch u8 | n·ch × int16 | ⌈n/8⌉ bytes de tacómetro | CRC-16/CCITT u16</c> (little-endian; el CRC cubre
/// desde <c>seq</c> hasta el tacómetro). Permite 4 canales a 26,7 kHz y detecta tramas perdidas o corruptas.</item>
/// </list>
/// </summary>
public static class SensorProtocol
{
    public const byte Sync0 = 0xA5, Sync1 = 0x5A;
    public const int HeaderBytes = 7;   // sync(2) + seq(2) + n(2) + ch(1)

    /// <summary>CRC-16/CCITT-FALSE (polinomio 0x1021, valor inicial 0xFFFF).</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++) crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    /// <summary>Arma una trama binaria (lo mismo que hace el firmware). <paramref name="samples"/>[canal][muestra] en unidades crudas.</summary>
    public static byte[] EncodeFrame(ushort seq, short[][] samples, bool[]? tach)
    {
        int ch = samples.Length, n = samples[0].Length, tb = (n + 7) / 8;
        var f = new byte[HeaderBytes + n * ch * 2 + tb + 2];
        f[0] = Sync0; f[1] = Sync1;
        f[2] = (byte)seq; f[3] = (byte)(seq >> 8);
        f[4] = (byte)n; f[5] = (byte)(n >> 8);
        f[6] = (byte)ch;
        int o = HeaderBytes;
        for (int i = 0; i < n; i++)
            for (int c = 0; c < ch; c++) { short v = samples[c][i]; f[o++] = (byte)v; f[o++] = (byte)(v >> 8); }
        for (int i = 0; i < n; i++) if (tach != null && tach[i]) f[o + i / 8] |= (byte)(1 << (i % 8));
        o += tb;
        ushort crc = Crc16(f.AsSpan(2, o - 2));
        f[o] = (byte)crc; f[o + 1] = (byte)(crc >> 8);
        return f;
    }
}

/// <summary>
/// Intérprete del flujo del sensor (texto v1 o binario v2), tolerante a basura, cortes y reconexiones.
/// Conserva solo los últimos <see cref="MaxSeconds"/> segundos para uso continuo en vivo.
/// </summary>
public sealed class SerialStreamParser
{
    public const int MaxChannels = 16;
    public double SampleRate { get; private set; }
    public int ChannelCount { get; private set; } = 1;
    public VibrationUnit Unit { get; private set; } = VibrationUnit.AccelerationG;
    public bool HasTach { get; private set; }
    public bool Binary { get; private set; }
    /// <summary>Factor de las muestras binarias (unidades físicas por cuenta).</summary>
    public double Scale { get; private set; } = 1;
    public string SensorModel { get; private set; } = "";
    public IReadOnlyList<string> ChannelNames => names;
    public long FramesOk { get; private set; }
    public long CrcErrors { get; private set; }
    public long LostFrames { get; private set; }
    public long BytesReceived { get; private set; }
    /// <summary>Último mensaje informativo del sensor (líneas que empiezan con «# »: avisos, respuesta a INFO).</summary>
    public string LastMessage { get; private set; } = "";
    /// <summary>Segundos de señal que se conservan (lo más viejo se descarta).</summary>
    public double MaxSeconds { get; set; } = 30;

    private readonly List<double>[] buf = Enumerable.Range(0, MaxChannels + 1).Select(_ => new List<double>()).ToArray();
    private readonly List<string> names = new();
    private readonly List<byte> raw = new();
    private int lastSeq = -1;

    public int Count => buf[0].Count;

    public string Status => SampleRate <= 0 ? "esperando cabecera #VA…" :
        $"{(Binary ? "binario v2" : "texto v1")} · {ChannelCount} canal(es) · {SampleRate:0} Hz{(string.IsNullOrEmpty(SensorModel) ? "" : " · " + SensorModel)}" +
        (Binary ? $" · tramas {FramesOk}, CRC mal {CrcErrors}, perdidas {LostFrames}" : "") + $" · {Count / Math.Max(1, SampleRate):0.0} s en memoria";

    /// <summary>Texto (compatibilidad): equivale a alimentar los bytes UTF-8.</summary>
    public void Feed(string chunk)
    {
        var b = Encoding.UTF8.GetBytes(chunk);
        Feed(b, 0, b.Length);
    }

    public void Feed(byte[] data, int offset, int count)
    {
        BytesReceived += count;
        for (int i = 0; i < count; i++) raw.Add(data[offset + i]);
        Process();
    }

    private void Process()
    {
        int pos = 0;
        while (pos < raw.Count)
        {
            if (Binary)
            {
                // Una cabecera de texto nueva (reinicio del sensor) vuelve al modo texto
                if (raw[pos] == (byte)'#' && pos + 2 < raw.Count && raw[pos + 1] == (byte)'V' && raw[pos + 2] == (byte)'A') { int nl = IndexOf((byte)'\n', pos); if (nl < 0) break; FeedLine(Encoding.ASCII.GetString(raw.GetRange(pos, nl - pos).ToArray()).Trim()); pos = nl + 1; continue; }
                if (raw[pos] != SensorProtocol.Sync0 || pos + 1 < raw.Count && raw[pos + 1] != SensorProtocol.Sync1) { pos++; continue; }
                if (pos + SensorProtocol.HeaderBytes > raw.Count) break;
                int n = raw[pos + 4] | raw[pos + 5] << 8, ch = raw[pos + 6];
                if (ch != ChannelCount || n == 0 || n > 4096) { pos++; CrcErrors++; continue; }
                int tb = (n + 7) / 8, len = SensorProtocol.HeaderBytes + n * ch * 2 + tb + 2;
                if (pos + len > raw.Count) break;
                var frame = raw.GetRange(pos, len).ToArray();
                ushort crc = (ushort)(frame[len - 2] | frame[len - 1] << 8);
                if (SensorProtocol.Crc16(frame.AsSpan(2, len - 4)) != crc) { pos++; CrcErrors++; continue; }
                int seq = frame[2] | frame[3] << 8;
                if (lastSeq >= 0) { int gap = (seq - lastSeq - 1 + 65536) % 65536; if (gap > 0 && gap < 30000) LostFrames += gap; }
                lastSeq = seq;
                int o = SensorProtocol.HeaderBytes;
                for (int i = 0; i < n; i++)
                    for (int c = 0; c < ch; c++, o += 2) buf[c].Add((short)(frame[o] | frame[o + 1] << 8) * Scale);
                if (HasTach)
                    for (int i = 0; i < n; i++) buf[ch].Add((frame[o + i / 8] >> (i % 8) & 1) != 0 ? 1.0 : 0.0);
                FramesOk++;
                pos += len;
            }
            else
            {
                int nl = IndexOf((byte)'\n', pos);
                if (nl < 0) { if (raw.Count - pos > 4096) pos = raw.Count; break; }   // sin fin de línea: basura
                FeedLine(Encoding.UTF8.GetString(raw.GetRange(pos, nl - pos).ToArray()).Trim());
                pos = nl + 1;
            }
        }
        raw.RemoveRange(0, Math.Min(pos, raw.Count));
        Trim();
    }

    private int IndexOf(byte b, int from)
    {
        for (int i = from; i < raw.Count; i++) if (raw[i] == b) return i;
        return -1;
    }

    public void FeedLine(string line)
    {
        if (line.Length == 0) return;
        if (line.StartsWith("#VA", StringComparison.OrdinalIgnoreCase))
        {
            bool v2 = line.StartsWith("#VA2", StringComparison.OrdinalIgnoreCase);
            ChannelCount = 1; HasTach = false; Scale = 1; SensorModel = ""; names.Clear(); lastSeq = -1;
            foreach (var kv in line[(v2 ? 4 : 3)..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = kv.Split('=', 2);
                if (p.Length != 2) continue;
                switch (p[0].ToLowerInvariant())
                {
                    case "fs": SampleRate = double.Parse(p[1], CultureInfo.InvariantCulture); break;
                    case "ch": ChannelCount = Math.Clamp(int.Parse(p[1], CultureInfo.InvariantCulture), 1, MaxChannels); break;
                    case "unit": Unit = CsvRecordingReader.ParseUnit(p[1]) ?? Unit; break;
                    case "tach": HasTach = p[1] == "1"; break;
                    case "scale": Scale = double.Parse(p[1], CultureInfo.InvariantCulture); break;
                    case "sensor": SensorModel = p[1]; break;
                    case "names": names.AddRange(p[1].Split(',').Select(x => x.Trim())); break;
                }
            }
            Binary = v2;
            foreach (var b in buf) b.Clear();
            return;
        }
        if (line.StartsWith('#')) { LastMessage = line.TrimStart('#', ' '); return; }
        if (Binary) return;
        var cells = line.Split(',');
        int need = ChannelCount + (HasTach ? 1 : 0);
        if (cells.Length < need) return;
        var vals = new double[need];
        for (int i = 0; i < need; i++)
            if (!double.TryParse(cells[i], NumberStyles.Float, CultureInfo.InvariantCulture, out vals[i])) return;   // línea incompleta: se descarta entera
        for (int i = 0; i < need; i++) buf[i].Add(vals[i]);
    }

    private void Trim()
    {
        if (SampleRate <= 0) return;
        int max = (int)(MaxSeconds * SampleRate);
        int extra = buf[0].Count - max;
        if (extra < max / 5) return;
        foreach (var b in buf) if (b.Count > extra) b.RemoveRange(0, extra); else b.Clear();
    }

    public void Clear() { foreach (var b in buf) b.Clear(); raw.Clear(); lastSeq = -1; FramesOk = CrcErrors = LostFrames = 0; }

    /// <summary>Señal recibida (los últimos <paramref name="seconds"/> segundos, o todo si es 0).</summary>
    public Recording ToRecording(double nominalRpm = 0, double seconds = 0)
    {
        if (SampleRate <= 0) throw new InvalidOperationException("No se recibió la cabecera #VA con la frecuencia de muestreo.");
        int total = Enumerable.Range(0, ChannelCount + (HasTach ? 1 : 0)).Min(i => buf[i].Count);
        int n = seconds > 0 ? Math.Min(total, (int)(seconds * SampleRate)) : total;
        int start = total - n;
        string Name(int i) => i < names.Count && names[i].Length > 0 ? names[i] : $"Sensor {i + 1}";
        var chans = Enumerable.Range(0, ChannelCount).Select(i => new Channel(Name(i), Unit, buf[i].GetRange(start, n).ToArray())).ToList();
        double[]? tach = HasTach ? buf[ChannelCount].GetRange(start, n).ToArray() : null;
        return new Recording(SampleRate, chans, tach, nominalRpm, string.IsNullOrEmpty(SensorModel) ? "Puerto serie" : "Sensor " + SensorModel);
    }
}
