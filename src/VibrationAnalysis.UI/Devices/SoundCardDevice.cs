using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using VibrationAnalysis.Core.Acquisition;

namespace VibrationAnalysis.UI.Devices
{
    /// <summary>
    /// Placa de sonido (o interfaz de audio USB con entradas IEPE) por la API waveIn de Windows, sin dependencias.
    /// Canal físico «0» = izquierdo, «1» = derecho. La escala se fija con el fondo de escala en volts (AcquisitionSettings.FullScaleVolts).
    /// La entrada de audio tiene acople AC: no mide por debajo de ≈ 5–20 Hz según la placa.
    /// </summary>
    public sealed class SoundCardDevice : StreamingDeviceBase
    {
        private const int CallbackEvent = 0x50000, WaveMapped = 0x0004, WhdrDone = 0x1, Buffers = 8;
        private IntPtr handle;
        private readonly IntPtr[] headers = new IntPtr[Buffers];
        private readonly IntPtr[] data = new IntPtr[Buffers];
        private int bufferBytes, inputChannels, bytesPerSample;
        private int[] map = Array.Empty<int>();
        private AutoResetEvent evt;
        private Thread worker;
        private volatile bool run;
        private string deviceName = "Placa de sonido";

        public override string Name => deviceName;
        protected override double InputLimitVolts(ChannelConfig c) => Settings?.FullScaleVolts ?? 0;

        public static IReadOnlyList<string> Devices()
        {
            var list = new List<string>();
            int n = WinMM.waveInGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                var caps = new WinMM.WAVEINCAPS();
                if (WinMM.waveInGetDevCaps((IntPtr)i, ref caps, Marshal.SizeOf<WinMM.WAVEINCAPS>()) == 0) list.Add($"{i}: {caps.szPname}");
            }
            return list;
        }

        protected override void OnStart()
        {
            var s = Settings;
            int dev = int.TryParse((s.Device ?? "").Split(':')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 0;
            var devs = Devices();
            if (devs.Count == 0) throw new InvalidOperationException("No hay entradas de audio en este equipo.");
            deviceName = "Placa de sonido · " + (devs.FirstOrDefault(x => x.StartsWith(dev + ":", StringComparison.Ordinal))?.Substring(dev.ToString(CultureInfo.InvariantCulture).Length + 2) ?? dev.ToString(CultureInfo.InvariantCulture));
            map = Active.Select(c => int.TryParse(c.Physical, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? Math.Clamp(p, 0, 1) : 0).ToArray();
            inputChannels = map.Max() + 1;
            evt = new AutoResetEvent(false);
            // Primero el formato pedido con la conversión de Windows (WAVE_MAPPED); si falla, 16 bits y luego 48/44,1 kHz
            var tries = new List<(int Bits, uint Rate)> { (s.Bits >= 24 ? 24 : 16, (uint)s.SampleRate), (16, (uint)s.SampleRate), (16, 48000), (16, 44100) }.Distinct().ToList();
            WinMM.WAVEFORMATEX fmt = default;
            int result = -1;
            foreach (var (bits, rate) in tries)
            {
                bytesPerSample = bits / 8;
                fmt = new WinMM.WAVEFORMATEX
                {
                    wFormatTag = 1, nChannels = (ushort)inputChannels, nSamplesPerSec = rate,
                    wBitsPerSample = (ushort)bits, nBlockAlign = (ushort)(inputChannels * bytesPerSample),
                };
                fmt.nAvgBytesPerSec = fmt.nSamplesPerSec * fmt.nBlockAlign;
                result = WinMM.waveInOpen(out handle, (IntPtr)dev, ref fmt, evt.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent | WaveMapped);
                if (result == 0) break;
            }
            if (result == 0 && Math.Abs(fmt.nSamplesPerSec - s.SampleRate) > 0.5)
            {
                // La placa no aceptó la frecuencia pedida: se usa la que abrió (el búfer se dimensiona con ella)
                s.SampleRate = fmt.nSamplesPerSec;
                s.Bits = fmt.wBitsPerSample;
                ResizeBuffer(s);
            }
            if (result != 0 && inputChannels > 1)
                LastError = "Si el micrófono es mono, desactivá el canal de la entrada «1» (tacómetro) en «Configurar equipo…».";
            Check(result, "abrir la entrada de audio");
            bufferBytes = Math.Max(fmt.nBlockAlign * 256, (int)(fmt.nAvgBytesPerSec / 20) / fmt.nBlockAlign * fmt.nBlockAlign);   // 50 ms
            int hdrSize = Marshal.SizeOf<WinMM.WAVEHDR>();
            for (int i = 0; i < Buffers; i++)
            {
                data[i] = Marshal.AllocHGlobal(bufferBytes);
                headers[i] = Marshal.AllocHGlobal(hdrSize);
                Marshal.StructureToPtr(new WinMM.WAVEHDR { lpData = data[i], dwBufferLength = (uint)bufferBytes }, headers[i], false);
                Check(WinMM.waveInPrepareHeader(handle, headers[i], hdrSize), "preparar los búferes");
                Check(WinMM.waveInAddBuffer(handle, headers[i], hdrSize), "entregar los búferes");
            }
            run = true;
            worker = new Thread(Loop) { IsBackground = true, Name = "Placa de sonido" };
            worker.Start();
            Check(WinMM.waveInStart(handle), "iniciar la captura");
        }

        private void Loop()
        {
            int next = 0, hdrSize = Marshal.SizeOf<WinMM.WAVEHDR>();
            double scale = Settings.FullScaleVolts / (bytesPerSample == 3 ? 8388608.0 : 32768.0);
            var bytes = new byte[bufferBytes];
            try
            {
                while (run)
                {
                    evt.WaitOne(200);
                    for (int k = 0; k < Buffers && run; k++)
                    {
                        var h = Marshal.PtrToStructure<WinMM.WAVEHDR>(headers[next]);
                        if ((h.dwFlags & WhdrDone) == 0) break;
                        int got = (int)h.dwBytesRecorded;
                        Marshal.Copy(h.lpData, bytes, 0, got);
                        int frames = got / (inputChannels * bytesPerSample);
                        var block = new double[map.Length][];
                        for (int c = 0; c < map.Length; c++) block[c] = new double[frames];
                        for (int f = 0; f < frames; f++)
                            for (int c = 0; c < map.Length; c++)
                            {
                                int o = (f * inputChannels + map[c]) * bytesPerSample;
                                int v = bytesPerSample == 3 ? (bytes[o] | bytes[o + 1] << 8 | (sbyte)bytes[o + 2] << 16) : (short)(bytes[o] | bytes[o + 1] << 8);
                                block[c][f] = v * scale;
                            }
                        if (frames > 0) Push(block);
                        WinMM.waveInAddBuffer(handle, headers[next], hdrSize);
                        next = (next + 1) % Buffers;
                    }
                }
            }
            catch (Exception ex) { LastError = ex.Message; IsRunning = false; }
        }

        protected override void OnStop()
        {
            run = false;
            worker?.Join(1000);
            if (handle != IntPtr.Zero)
            {
                WinMM.waveInStop(handle);
                WinMM.waveInReset(handle);
                int hdrSize = Marshal.SizeOf<WinMM.WAVEHDR>();
                for (int i = 0; i < Buffers; i++) if (headers[i] != IntPtr.Zero) WinMM.waveInUnprepareHeader(handle, headers[i], hdrSize);
                WinMM.waveInClose(handle);
                handle = IntPtr.Zero;
            }
            for (int i = 0; i < Buffers; i++)
            {
                if (headers[i] != IntPtr.Zero) { Marshal.FreeHGlobal(headers[i]); headers[i] = IntPtr.Zero; }
                if (data[i] != IntPtr.Zero) { Marshal.FreeHGlobal(data[i]); data[i] = IntPtr.Zero; }
            }
            evt?.Dispose(); evt = null;
        }

        private void Check(int result, string what)
        {
            if (result == 0) return;
            var sb = new StringBuilder(256);
            WinMM.waveInGetErrorText(result, sb, sb.Capacity);
            string hint = result switch
            {
                1 => "\nCausa más probable: Windows bloquea el micrófono para aplicaciones de escritorio. Abrí Configuración → Privacidad y seguridad → Micrófono y activá «Permitir que las aplicaciones de escritorio accedan al micrófono».",
                4 => "\nOtra aplicación está usando la entrada en modo exclusivo: cerrala o desactivá el modo exclusivo en las propiedades del dispositivo.",
                32 => "\nLa entrada no acepta ese formato: probá 48000 Hz, 16 bits y un solo canal.",
                _ => "",
            };
            if (LastError.Length > 0 && result != 1) hint += "\n" + LastError;
            string msg = $"No se pudo {what}: {sb} (código {result}).{hint}";
            LastError = msg;
            try { OnStop(); } catch { }
            throw new InvalidOperationException(msg);
        }

        private static class WinMM
        {
            [StructLayout(LayoutKind.Sequential, Pack = 2)]
            public struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }

            [StructLayout(LayoutKind.Sequential)]
            public struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct WAVEINCAPS
            {
                public ushort wMid, wPid; public uint vDriverVersion;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
                public uint dwFormats; public ushort wChannels, wReserved1;
            }

            [DllImport("winmm.dll")] public static extern int waveInGetNumDevs();
            [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetDevCapsW")] public static extern int waveInGetDevCaps(IntPtr id, ref WAVEINCAPS caps, int size);
            [DllImport("winmm.dll")] public static extern int waveInOpen(out IntPtr h, IntPtr deviceId, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr instance, int flags);
            [DllImport("winmm.dll")] public static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
            [DllImport("winmm.dll")] public static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
            [DllImport("winmm.dll")] public static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
            [DllImport("winmm.dll")] public static extern int waveInStart(IntPtr h);
            [DllImport("winmm.dll")] public static extern int waveInStop(IntPtr h);
            [DllImport("winmm.dll")] public static extern int waveInReset(IntPtr h);
            [DllImport("winmm.dll")] public static extern int waveInClose(IntPtr h);
            [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetErrorTextW")] public static extern int waveInGetErrorText(int err, StringBuilder text, int size);
        }
    }
}
