using System.Net;
using System.Net.Sockets;

namespace VibrationAnalysis.Core.Network;

/// <summary>Utilidades Modbus: CRC de RTU, registros de 32 bits y códigos de excepción.</summary>
public static class ModbusCodec
{
    public const byte ReadHolding = 3, ReadInput = 4, WriteSingle = 6, WriteMultiple = 16;
    public const byte IllegalFunction = 1, IllegalAddress = 2, IllegalValue = 3;

    /// <summary>CRC-16/MODBUS (polinomio 0xA001 reflejado, inicial 0xFFFF). En la trama va primero el byte bajo.</summary>
    public static ushort Crc(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    /// <summary>Float de 32 bits en dos registros. Orden «ABCD» (palabra alta primero, el más común); <paramref name="swapWords"/> = «CDAB».</summary>
    public static (ushort Hi, ushort Lo) FromFloat(float v, bool swapWords = false)
    {
        uint u = BitConverter.SingleToUInt32Bits(v);
        ushort hi = (ushort)(u >> 16), lo = (ushort)u;
        return swapWords ? (lo, hi) : (hi, lo);
    }

    public static float ToFloat(ushort first, ushort second, bool swapWords = false)
    {
        uint u = swapWords ? (uint)second << 16 | first : (uint)first << 16 | second;
        return BitConverter.UInt32BitsToSingle(u);
    }

    public static string ExceptionText(int code) => code switch
    {
        1 => "función no soportada", 2 => "dirección fuera de rango", 3 => "valor inválido", 4 => "falla del equipo",
        6 => "equipo ocupado", 10 => "gateway sin ruta", 11 => "el equipo detrás del gateway no responde", _ => $"excepción {code}",
    };

    /// <summary>
    /// Atiende una PDU (función + datos) sobre los bancos de registros. Devuelve la PDU de respuesta (o de excepción).
    /// Lo usan el servidor TCP y las pruebas.
    /// </summary>
    public static byte[] Handle(ReadOnlySpan<byte> pdu, ushort[] holding, ushort[] input, object sync, Action<int, ushort>? onWrite = null)
    {
        byte fc = pdu[0];
        byte[] Ex(byte code) => new[] { (byte)(fc | 0x80), code };
        if (pdu.Length < 5) return Ex(IllegalValue);
        int addr = pdu[1] << 8 | pdu[2];
        int qty = pdu[3] << 8 | pdu[4];
        lock (sync)
        {
            switch (fc)
            {
                case ReadHolding:
                case ReadInput:
                {
                    var bank = fc == ReadHolding ? holding : input;
                    if (qty < 1 || qty > 125) return Ex(IllegalValue);
                    if (addr + qty > bank.Length) return Ex(IllegalAddress);
                    var r = new byte[2 + qty * 2];
                    r[0] = fc; r[1] = (byte)(qty * 2);
                    for (int i = 0; i < qty; i++) { r[2 + 2 * i] = (byte)(bank[addr + i] >> 8); r[3 + 2 * i] = (byte)bank[addr + i]; }
                    return r;
                }
                case WriteSingle:
                {
                    if (addr >= holding.Length) return Ex(IllegalAddress);
                    holding[addr] = (ushort)qty;
                    onWrite?.Invoke(addr, (ushort)qty);
                    return pdu[..5].ToArray();
                }
                case WriteMultiple:
                {
                    if (qty < 1 || qty > 123 || pdu.Length < 6 + qty * 2 || pdu[5] != qty * 2) return Ex(IllegalValue);
                    if (addr + qty > holding.Length) return Ex(IllegalAddress);
                    for (int i = 0; i < qty; i++)
                    {
                        holding[addr + i] = (ushort)(pdu[6 + 2 * i] << 8 | pdu[7 + 2 * i]);
                        onWrite?.Invoke(addr + i, holding[addr + i]);
                    }
                    return pdu[..5].ToArray();
                }
                default:
                    return Ex(IllegalFunction);
            }
        }
    }
}

/// <summary>
/// Servidor (esclavo) Modbus TCP: publica los resultados de la app en registros para un PLC o SCADA.
/// Registros de entrada (función 04) con las mediciones; registros de retención (03/06/16) para órdenes desde el PLC.
/// </summary>
public sealed class ModbusTcpServer : IDisposable
{
    private TcpListener? listener;
    private CancellationTokenSource? cts;
    private readonly object sync = new();
    public ushort[] Input { get; } = new ushort[1000];
    public ushort[] Holding { get; } = new ushort[100];
    public int Port { get; private set; }
    public bool IsRunning => listener != null;
    public int Clients;
    public long Requests;
    /// <summary>El PLC escribió un registro de retención (dirección base 0, valor).</summary>
    public event Action<int, ushort>? HoldingWritten;

    public void Start(int port, IPAddress? bind = null)
    {
        Stop();
        cts = new CancellationTokenSource();
        listener = new TcpListener(bind ?? IPAddress.Any, port);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(() => AcceptLoop(cts.Token));
    }

    public void SetFloat(int address, float value)
    {
        var (hi, lo) = ModbusCodec.FromFloat(value);
        lock (sync) { Input[address] = hi; Input[address + 1] = lo; }
    }

    public void SetWord(int address, ushort value) { lock (sync) Input[address] = value; }

    public void SetUInt32(int address, uint value) { lock (sync) { Input[address] = (ushort)(value >> 16); Input[address + 1] = (ushort)value; } }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener != null)
        {
            TcpClient c;
            try { c = await listener.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Task.Run(() => Serve(c, ct), CancellationToken.None);   // cada cliente termina solo al cancelar ct
        }
    }

    private async Task Serve(TcpClient c, CancellationToken ct)
    {
        Interlocked.Increment(ref Clients);
        try
        {
            using (c)
            {
                var s = c.GetStream();
                var head = new byte[7];
                while (!ct.IsCancellationRequested)
                {
                    if (!await ReadExact(s, head, ct)) break;
                    int len = head[4] << 8 | head[5];               // unidad + PDU
                    if (len < 2 || len > 260) break;
                    var pdu = new byte[len - 1];
                    if (!await ReadExact(s, pdu, ct)) break;
                    var resp = ModbusCodec.Handle(pdu, Holding, Input, sync, (a, v) => HoldingWritten?.Invoke(a, v));
                    Interlocked.Increment(ref Requests);
                    var frame = new byte[7 + resp.Length];
                    frame[0] = head[0]; frame[1] = head[1];          // id de transacción
                    frame[4] = (byte)((resp.Length + 1) >> 8); frame[5] = (byte)(resp.Length + 1);
                    frame[6] = head[6];                               // unidad
                    resp.CopyTo(frame, 7);
                    await s.WriteAsync(frame, ct);
                }
            }
        }
        catch { /* cliente desconectado */ }
        finally { Interlocked.Decrement(ref Clients); }
    }

    private static async Task<bool> ReadExact(Stream s, byte[] buf, CancellationToken ct)
    {
        int off = 0;
        while (off < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(off), ct);
            if (n == 0) return false;
            off += n;
        }
        return true;
    }

    public void Stop()
    {
        cts?.Cancel();
        try { listener?.Stop(); } catch { }
        listener = null;
    }

    public void Dispose() => Stop();
}

/// <summary>
/// Cliente (maestro) Modbus por TCP o RTU (cualquier Stream: puerto serie, conversor USB-RS485, TCP).
/// Sirve para leer la velocidad de un variador, una temperatura o un estado del PLC.
/// </summary>
public sealed class ModbusClient : IDisposable
{
    private readonly Stream stream;
    private readonly bool rtu;
    private readonly IDisposable? owner;
    private ushort transaction;
    public int TimeoutMs { get; set; } = 1000;

    private ModbusClient(Stream s, bool isRtu, IDisposable? own) { stream = s; rtu = isRtu; owner = own; }

    public static async Task<ModbusClient> ConnectTcpAsync(string host, int port = 502, int timeoutMs = 3000)
    {
        var tcp = new TcpClient { NoDelay = true };
        using (var to = new CancellationTokenSource(timeoutMs)) await tcp.ConnectAsync(host, port, to.Token);
        return new ModbusClient(tcp.GetStream(), false, tcp) { TimeoutMs = timeoutMs };
    }

    /// <summary>RTU sobre un flujo ya abierto (por ejemplo SerialPort.BaseStream). El flujo no se cierra al liberar el cliente.</summary>
    public static ModbusClient OverRtu(Stream serial) => new(serial, true, null);

    /// <summary>TCP sobre un flujo ya abierto (pruebas o túneles).</summary>
    public static ModbusClient OverTcp(Stream s) => new(s, false, null);

    public Task<ushort[]> ReadHoldingAsync(byte unit, int address, int count) => ReadAsync(unit, ModbusCodec.ReadHolding, address, count);
    public Task<ushort[]> ReadInputAsync(byte unit, int address, int count) => ReadAsync(unit, ModbusCodec.ReadInput, address, count);

    public async Task<ushort[]> ReadAsync(byte unit, byte function, int address, int count)
    {
        var pdu = new byte[] { function, (byte)(address >> 8), (byte)address, (byte)(count >> 8), (byte)count };
        var resp = await TransactAsync(unit, pdu);
        if (resp.Length < 2 || resp[1] != count * 2) throw new IOException("Respuesta Modbus con largo inesperado.");
        var r = new ushort[count];
        for (int i = 0; i < count; i++) r[i] = (ushort)(resp[2 + 2 * i] << 8 | resp[3 + 2 * i]);
        return r;
    }

    public async Task WriteSingleAsync(byte unit, int address, ushort value)
        => await TransactAsync(unit, new byte[] { ModbusCodec.WriteSingle, (byte)(address >> 8), (byte)address, (byte)(value >> 8), (byte)value });

    private async Task<byte[]> TransactAsync(byte unit, byte[] pdu)
    {
        using var to = new CancellationTokenSource(TimeoutMs);
        byte[] frame;
        if (rtu)
        {
            frame = new byte[pdu.Length + 3];
            frame[0] = unit; pdu.CopyTo(frame, 1);
            ushort crc = ModbusCodec.Crc(frame.AsSpan(0, pdu.Length + 1));
            frame[^2] = (byte)crc; frame[^1] = (byte)(crc >> 8);
        }
        else
        {
            transaction++;
            frame = new byte[7 + pdu.Length];
            frame[0] = (byte)(transaction >> 8); frame[1] = (byte)transaction;
            frame[4] = (byte)((pdu.Length + 1) >> 8); frame[5] = (byte)(pdu.Length + 1);
            frame[6] = unit; pdu.CopyTo(frame, 7);
        }
        await stream.WriteAsync(frame, to.Token);
        await stream.FlushAsync(to.Token);
        byte[] resp;
        if (rtu)
        {
            var head = await ReadExact(2, to.Token);                 // unidad + función
            if (head[0] != unit) throw new IOException($"Respondió la unidad {head[0]} en vez de la {unit}.");
            byte[] body;
            if ((head[1] & 0x80) != 0) body = await ReadExact(1, to.Token);                       // código de excepción
            else if (head[1] is ModbusCodec.ReadHolding or ModbusCodec.ReadInput)
            {
                var cnt = await ReadExact(1, to.Token);
                body = cnt.Concat(await ReadExact(cnt[0], to.Token)).ToArray();
            }
            else body = await ReadExact(4, to.Token);                                           // eco de dirección y valor/cantidad
            var crcB = await ReadExact(2, to.Token);
            var all = head.Concat(body).ToArray();
            ushort crc = ModbusCodec.Crc(all);
            if (crcB[0] != (byte)crc || crcB[1] != (byte)(crc >> 8)) throw new IOException("CRC Modbus inválido en la respuesta.");
            resp = all[1..];
        }
        else
        {
            var head = await ReadExact(7, to.Token);
            int len = head[4] << 8 | head[5];
            resp = await ReadExact(len - 1, to.Token);
        }
        if ((resp[0] & 0x80) != 0) throw new IOException("El equipo respondió: " + ModbusCodec.ExceptionText(resp[1]) + ".");
        return resp;
    }

    private async Task<byte[]> ReadExact(int n, CancellationToken ct)
    {
        var b = new byte[n];
        int off = 0;
        while (off < n)
        {
            int r = await stream.ReadAsync(b.AsMemory(off), ct);
            if (r == 0) throw new IOException("Se cortó la conexión Modbus.");
            off += r;
        }
        return b;
    }

    public void Dispose() => owner?.Dispose();
}
