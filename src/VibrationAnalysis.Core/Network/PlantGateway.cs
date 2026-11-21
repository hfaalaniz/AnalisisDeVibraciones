using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using VibrationAnalysis.Core.Trending;

namespace VibrationAnalysis.Core.Network;

public sealed record RegisterInfo(int Address, string Name, string Type, string Unit);

/// <summary>
/// Puente con la planta: mensajes JSON para MQTT y mapa de registros Modbus de la app.
/// Mapa (registros de entrada, función 04, dirección base 0 = 30001 en notación Modicon):
/// 0–25 medición actual; 100 + 20·i: punto i de la base de tendencias (hasta 40 puntos).
/// Registro de retención 0 (40001): el PLC escribe 1 para pedir «adquirir y guardar».
/// </summary>
public static class PlantGateway
{
    public const int PointBase = 100, PointStride = 20, MaxPoints = 40;
    public const int CommandRegister = 0;

    private static readonly (int Offset, string Key, string Name, string Unit)[] Current =
    {
        (0, FeatureKeys.VelocityRms, "Velocidad RMS 10–1000 Hz", "mm/s"),
        (2, FeatureKeys.AccelerationRms, "Aceleración RMS", "g"),
        (4, FeatureKeys.AccelerationPeak, "Aceleración pico", "g"),
        (6, FeatureKeys.CrestFactor, "Factor de cresta", ""),
        (8, FeatureKeys.Kurtosis, "Curtosis", ""),
        (10, FeatureKeys.Amp1X, "1X", "mm/s pico"),
        (12, FeatureKeys.Phase1X, "Fase 1X", "°"),
        (14, FeatureKeys.Amp2X, "2X", "mm/s pico"),
        (16, FeatureKeys.EnvelopeRms, "Envolvente RMS", "g"),
    };

    private static readonly (int Offset, string Key, string Name, string Unit)[] PerPoint =
    {
        (0, FeatureKeys.VelocityRms, "velocidad RMS", "mm/s"),
        (2, FeatureKeys.AccelerationRms, "aceleración RMS", "g"),
        (4, FeatureKeys.EnvelopeRms, "envolvente RMS", "g"),
        (6, FeatureKeys.Amp1X, "1X", "mm/s pico"),
    };

    /// <summary>Descripción del mapa para mostrar o exportar (direcciones en notación 3xxxx/4xxxx).</summary>
    public static IReadOnlyList<RegisterInfo> Describe(IReadOnlyList<string>? pointNames = null)
    {
        var l = new List<RegisterInfo>();
        foreach (var c in Current) l.Add(new RegisterInfo(30001 + c.Offset, c.Name, "float32 ABCD", c.Unit));
        l.Add(new RegisterInfo(30019, "Velocidad de giro", "float32 ABCD", "rpm"));
        l.Add(new RegisterInfo(30021, "Zona ISO 20816 (0 = A … 3 = D)", "uint16", ""));
        l.Add(new RegisterInfo(30022, "Nivel de alarma (0 normal, 1 alerta, 2 peligro)", "uint16", ""));
        l.Add(new RegisterInfo(30023, "Fecha de la medición (Unix)", "uint32", "s"));
        l.Add(new RegisterInfo(30025, "Contador de mediciones", "uint16", ""));
        l.Add(new RegisterInfo(30026, "Latido (segundos, cíclico)", "uint16", "s"));
        int n = Math.Min(MaxPoints, pointNames?.Count ?? 0);
        for (int i = 0; i < n; i++)
        {
            int b = 30001 + PointBase + i * PointStride;
            foreach (var p in PerPoint) l.Add(new RegisterInfo(b + p.Offset, $"{pointNames![i]} · {p.Name}", "float32 ABCD", p.Unit));
            l.Add(new RegisterInfo(b + 8, $"{pointNames![i]} · nivel de alarma", "uint16", ""));
            l.Add(new RegisterInfo(b + 9, $"{pointNames![i]} · mediciones guardadas", "uint16", ""));
            l.Add(new RegisterInfo(b + 10, $"{pointNames![i]} · fecha de la última", "uint32", "s"));
        }
        l.Add(new RegisterInfo(40001, "Orden del PLC: 1 = adquirir y guardar (la app lo vuelve a 0)", "uint16", ""));
        return l;
    }

    /// <summary>Carga la medición actual en los registros del servidor.</summary>
    public static void PublishCurrent(ModbusTcpServer s, MeasurementRecord m, int isoZone, AlarmLevel level, ushort counter)
    {
        foreach (var c in Current) s.SetFloat(c.Offset, Finite(m.Get(c.Key)));
        s.SetFloat(18, (float)m.Rpm);
        s.SetWord(20, (ushort)Math.Clamp(isoZone, 0, 3));
        s.SetWord(21, (ushort)level);
        s.SetUInt32(22, (uint)new DateTimeOffset(m.Timestamp).ToUnixTimeSeconds());
        s.SetWord(24, counter);
    }

    /// <summary>Carga el último estado de un punto de la base (índice 0…39).</summary>
    public static void PublishPoint(ModbusTcpServer s, int index, MeasurementRecord? last, AlarmLevel level, int count)
    {
        if (index < 0 || index >= MaxPoints) return;
        int b = PointBase + index * PointStride;
        foreach (var p in PerPoint) s.SetFloat(b + p.Offset, last == null ? 0 : Finite(last.Get(p.Key)));
        s.SetWord(b + 8, (ushort)level);
        s.SetWord(b + 9, (ushort)Math.Min(count, ushort.MaxValue));
        s.SetUInt32(b + 10, last == null ? 0 : (uint)new DateTimeOffset(last.Timestamp).ToUnixTimeSeconds());
    }

    public static void Heartbeat(ModbusTcpServer s) => s.SetWord(25, (ushort)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 65536));

    private static float Finite(double v) => double.IsNaN(v) || double.IsInfinity(v) ? 0f : (float)v;

    /// <summary>Mensaje JSON de una medición, para publicar por MQTT.</summary>
    public static string MeasurementJson(MeasurementRecord m, string plant, string machine, string point, AlarmLevel level, IEnumerable<AlarmResult>? alarms = null)
    {
        var ind = new JsonObject();
        foreach (var kv in m.Features.Where(kv => double.IsFinite(kv.Value))) ind[kv.Key] = Math.Round(kv.Value, 6);
        var o = new JsonObject
        {
            ["planta"] = plant, ["maquina"] = machine, ["punto"] = point, ["punto_id"] = m.PointId,
            ["fecha"] = m.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            ["rpm"] = Math.Round(m.Rpm, 2), ["nivel"] = level switch { AlarmLevel.Danger => "peligro", AlarmLevel.Alert => "alerta", _ => "normal" },
            ["indicadores"] = ind,
        };
        if (alarms != null)
        {
            var arr = new JsonArray();
            foreach (var a in alarms.Where(a => a.Level != AlarmLevel.Normal))
                arr.Add(new JsonObject { ["indicador"] = a.Feature, ["valor"] = Math.Round(a.Value, 6), ["nivel"] = a.Level == AlarmLevel.Danger ? "peligro" : "alerta", ["limite"] = Math.Round(a.Level == AlarmLevel.Danger ? a.DangerLimit : a.AlertLimit, 6) });
            o["alarmas"] = arr;
        }
        return o.ToJsonString();
    }

    /// <summary>Tema MQTT seguro a partir de nombres (sin «/», «+» ni «#»).</summary>
    public static string TopicPart(string s)
    {
        var t = new string((s ?? "").Trim().Select(c => c is '/' or '+' or '#' ? '_' : char.IsWhiteSpace(c) ? '_' : c).ToArray());
        return t.Length == 0 ? "_" : t;
    }

    /// <summary>
    /// Interpreta el mensaje de indicadores de un sensor de red, por ejemplo
    /// <c>{"id":"vt01","vel_rms":2.3,"acc_rms":0.41,"acc_pico":1.8,"factor_cresta":4.4,"temperatura":56,"rpm":1490}</c>.
    /// Devuelve null si no es un JSON con «id».
    /// </summary>
    public static (string Id, Dictionary<string, double> Features, double Rpm)? ParseSensorMessage(string json)
    {
        try
        {
            var node = JsonNode.Parse(json) as JsonObject;
            if (node == null || node["id"] is not JsonValue idv) return null;
            string id = idv.TryGetValue<string>(out var sid) ? sid : idv.ToJsonString();
            var f = new Dictionary<string, double>();
            double rpm = 0;
            foreach (var kv in node)
            {
                if (kv.Value is not JsonValue v || !v.TryGetValue<double>(out var d) || !double.IsFinite(d)) continue;
                if (kv.Key == "rpm") rpm = d;
                else if (kv.Key is not ("fs" or "n" or "seq")) f[kv.Key] = d;
            }
            return (id, f, rpm);
        }
        catch (JsonException) { return null; }
    }
}
