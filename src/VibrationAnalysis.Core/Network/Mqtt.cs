using System.Net.Sockets;
using System.Text;

namespace VibrationAnalysis.Core.Network;

/// <summary>Codificación de paquetes MQTT 3.1.1 (lo mínimo para publicar, suscribirse y mantener la sesión).</summary>
public static class MqttCodec
{
    public const byte Connect = 0x10, ConnAck = 0x20, Publish = 0x30, PubAck = 0x40, Subscribe = 0x82, SubAck = 0x90, PingReq = 0xC0, PingResp = 0xD0, Disconnect = 0xE0;

    public static void WriteRemainingLength(List<byte> b, int len)
    {
        do
        {
            byte d = (byte)(len % 128);
            len /= 128;
            if (len > 0) d |= 0x80;
            b.Add(d);
        } while (len > 0);
    }

    public static void WriteString(List<byte> b, string s)
    {
        var u = Encoding.UTF8.GetBytes(s);
        b.Add((byte)(u.Length >> 8)); b.Add((byte)u.Length);
        b.AddRange(u);
    }

    private static byte[] Packet(byte header, List<byte> body)
    {
        var p = new List<byte> { header };
        WriteRemainingLength(p, body.Count);
        p.AddRange(body);
        return p.ToArray();
    }

    public static byte[] BuildConnect(string clientId, string? user, string? password, ushort keepAliveS, bool cleanSession = true)
    {
        var b = new List<byte>();
        WriteString(b, "MQTT");
        b.Add(4);   // nivel de protocolo 3.1.1
        byte flags = (byte)((cleanSession ? 0x02 : 0) | (string.IsNullOrEmpty(user) ? 0 : 0x80) | (string.IsNullOrEmpty(password) ? 0 : 0x40));
        b.Add(flags);
        b.Add((byte)(keepAliveS >> 8)); b.Add((byte)keepAliveS);
        WriteString(b, clientId);
        if (!string.IsNullOrEmpty(user)) WriteString(b, user);
        if (!string.IsNullOrEmpty(password)) WriteString(b, password);
        return Packet(Connect, b);
    }

    public static byte[] BuildPublish(string topic, byte[] payload, int qos = 0, bool retain = false, ushort packetId = 0)
    {
        var b = new List<byte>();
        WriteString(b, topic);
        if (qos > 0) { b.Add((byte)(packetId >> 8)); b.Add((byte)packetId); }
        b.AddRange(payload);
        return Packet((byte)(Publish | (qos << 1) | (retain ? 1 : 0)), b);
    }

    public static byte[] BuildSubscribe(ushort packetId, string filter, int qos = 0)
    {
        var b = new List<byte> { (byte)(packetId >> 8), (byte)packetId };
        WriteString(b, filter);
        b.Add((byte)qos);
        return Packet(Subscribe, b);
    }

    public static byte[] BuildPubAck(ushort packetId) => new byte[] { PubAck, 2, (byte)(packetId >> 8), (byte)packetId };

    /// <summary>Lee un paquete completo del flujo: (cabecera, cuerpo). Lanza EndOfStreamException si se cerró la conexión.</summary>
    public static async Task<(byte Header, byte[] Body)> ReadPacketAsync(Stream s, CancellationToken ct)
    {
        var one = new byte[1];
        await ReadExactAsync(s, one, ct);
        byte header = one[0];
        int len = 0, mult = 1;
        for (int i = 0; i < 4; i++)
        {
            await ReadExactAsync(s, one, ct);
            len += (one[0] & 0x7F) * mult;
            if ((one[0] & 0x80) == 0) break;
            mult *= 128;
        }
        var body = new byte[len];
        if (len > 0) await ReadExactAsync(s, body, ct);
        return (header, body);
    }

    private static async Task ReadExactAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int off = 0;
        while (off < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(off), ct);
            if (n == 0) throw new EndOfStreamException("El broker cerró la conexión.");
            off += n;
        }
    }

    /// <summary>Tema y contenido de un PUBLISH recibido.</summary>
    public static (string Topic, byte[] Payload, int Qos, ushort PacketId) ParsePublish(byte header, byte[] body)
    {
        int qos = (header >> 1) & 3;
        int tl = body[0] << 8 | body[1];
        string topic = Encoding.UTF8.GetString(body, 2, tl);
        int o = 2 + tl;
        ushort id = 0;
        if (qos > 0) { id = (ushort)(body[o] << 8 | body[o + 1]); o += 2; }
        return (topic, body[o..], qos, id);
    }

    /// <summary>¿El tema coincide con el filtro? Admite los comodines «+» (un nivel) y «#» (el resto).</summary>
    public static bool TopicMatches(string filter, string topic)
    {
        var f = filter.Split('/');
        var t = topic.Split('/');
        for (int i = 0; i < f.Length; i++)
        {
            if (f[i] == "#") return true;
            if (i >= t.Length) return false;
            if (f[i] != "+" && f[i] != t[i]) return false;
        }
        return f.Length == t.Length;
    }
}

/// <summary>
/// Cliente MQTT 3.1.1 mínimo y sin dependencias (TCP, QoS 0 y 1, suscripciones con comodines, keep-alive).
/// Para integrar con SCADA o plataformas IIoT (Node-RED, Mosquitto, ThingsBoard, Ignition…).
/// </summary>
public sealed class MqttClient : IDisposable
{
    private TcpClient? tcp;
    private Stream? stream;
    private CancellationTokenSource? cts;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private ushort nextId = 1;
    private TaskCompletionSource<byte>? connAck;
    private readonly Dictionary<ushort, TaskCompletionSource<bool>> pending = new();

    public bool IsConnected { get; private set; }
    public string LastError { get; private set; } = "";
    public long Received { get; private set; }
    public long Sent { get; private set; }

    /// <summary>Mensaje recibido (tema, contenido). Se invoca desde un hilo de fondo.</summary>
    public event Action<string, byte[]>? MessageReceived;
    public event Action<string>? Disconnected;

    public async Task ConnectAsync(string host, int port, string clientId, string? user = null, string? password = null, ushort keepAliveS = 30, int timeoutMs = 5000)
    {
        Dispose();
        cts = new CancellationTokenSource();
        tcp = new TcpClient { NoDelay = true };
        using (var to = new CancellationTokenSource(timeoutMs))
            await tcp.ConnectAsync(host, port, to.Token);
        stream = tcp.GetStream();
        connAck = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() => ReadLoop(cts.Token));
        await SendAsync(MqttCodec.BuildConnect(clientId, user, password, keepAliveS));
        byte rc;
        try { rc = await connAck.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)); }
        catch (TimeoutException) { throw new TimeoutException("El broker no respondió al CONNECT."); }
        if (rc != 0)
            throw new InvalidOperationException(rc switch
            {
                1 => "El broker no acepta MQTT 3.1.1.", 2 => "Identificador de cliente rechazado.", 3 => "Broker no disponible.",
                4 => "Usuario o contraseña incorrectos.", 5 => "No autorizado.", _ => $"CONNACK con código {rc}.",
            });
        IsConnected = true;
        _ = Task.Run(() => KeepAlive(keepAliveS, cts.Token));
    }

    public async Task PublishAsync(string topic, string payload, int qos = 0, bool retain = false)
    {
        if (!IsConnected) throw new InvalidOperationException("No hay conexión con el broker.");
        var data = Encoding.UTF8.GetBytes(payload);
        if (qos == 0) { await SendAsync(MqttCodec.BuildPublish(topic, data, 0, retain)); return; }
        ushort id = NextId();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (pending) pending[id] = tcs;
        await SendAsync(MqttCodec.BuildPublish(topic, data, 1, retain, id));
        try { await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException)
        {
            lock (pending) pending.Remove(id);
            throw new TimeoutException("El broker no confirmó la publicación (PUBACK).");
        }
    }

    public async Task SubscribeAsync(string filter, int qos = 0)
    {
        if (!IsConnected) throw new InvalidOperationException("No hay conexión con el broker.");
        ushort id = NextId();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (pending) pending[id] = tcs;
        await SendAsync(MqttCodec.BuildSubscribe(id, filter, qos));
        bool granted;
        try { granted = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { throw new TimeoutException("El broker no confirmó la suscripción (SUBACK)."); }
        if (!granted) throw new InvalidOperationException($"El broker rechazó la suscripción a «{filter}».");
    }

    private ushort NextId() { lock (pending) { if (nextId == 0) nextId = 1; return nextId++; } }

    private async Task SendAsync(byte[] p)
    {
        var s = stream ?? throw new InvalidOperationException("No hay conexión.");
        await writeLock.WaitAsync();
        try { await s.WriteAsync(p); await s.FlushAsync(); Sent++; }
        finally { writeLock.Release(); }
    }

    private async Task KeepAlive(int seconds, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, seconds * 0.75)), ct);
                await SendAsync(new byte[] { MqttCodec.PingReq, 0 });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Fail(ex.Message); }
    }

    private async Task ReadLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var (h, body) = await MqttCodec.ReadPacketAsync(stream!, ct);
                switch (h & 0xF0)
                {
                    case MqttCodec.ConnAck: connAck?.TrySetResult(body.Length >= 2 ? body[1] : (byte)255); break;
                    case MqttCodec.PubAck:
                    case MqttCodec.SubAck:
                    {
                        ushort id = (ushort)(body[0] << 8 | body[1]);
                        TaskCompletionSource<bool>? t;
                        lock (pending) { pending.TryGetValue(id, out t); pending.Remove(id); }
                        t?.TrySetResult((h & 0xF0) == MqttCodec.PubAck || body.Length < 3 || body[2] != 0x80);
                        break;
                    }
                    case MqttCodec.Publish:
                    {
                        var (topic, payload, qos, id) = MqttCodec.ParsePublish(h, body);
                        if (qos == 1) await SendAsync(MqttCodec.BuildPubAck(id));
                        Received++;
                        MessageReceived?.Invoke(topic, payload);
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Fail(ex.Message); }
    }

    private void Fail(string msg)
    {
        if (!IsConnected && connAck?.Task.IsCompleted == false) connAck.TrySetException(new IOException(msg));
        bool was = IsConnected;
        IsConnected = false;
        LastError = msg;
        if (was) Disconnected?.Invoke(msg);
    }

    public void Dispose()
    {
        if (IsConnected) { try { stream?.Write(new byte[] { MqttCodec.Disconnect, 0 }); } catch { } }
        IsConnected = false;
        cts?.Cancel();
        try { tcp?.Close(); } catch { }
        tcp = null; stream = null;
    }
}
