using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using VibrationAnalysis.Core.Acquisition;

namespace VibrationAnalysis.UI.Devices
{
    /// <summary>
    /// Placas NI con entradas IEPE (NI 9234, 9230, 9231, USB-4431, PXIe-4497…) o de tensión, por la API C de NI-DAQmx (nicaiu.dll).
    /// No requiere referencias: si NI-DAQmx no está instalado, la opción avisa y no se usa.
    /// Los acelerómetros se crean como canales de aceleración con excitación IEPE interna y acople AC.
    /// </summary>
    public sealed class NiDaqmxDevice : StreamingDeviceBase
    {
        private IntPtr task;
        private Thread worker;
        private volatile bool run;
        public override string Name => "NI-DAQmx" + (Settings != null && Settings.Device.Length > 0 ? " · " + Settings.Device : "");
        protected override double InputLimitVolts(ChannelConfig c) => c.RangeVolts;

        public static bool IsInstalled()
        {
            if (!NativeLibrary.TryLoad("nicaiu.dll", out var h)) return false;
            NativeLibrary.Free(h);
            return true;
        }

        public static IReadOnlyList<string> Devices()
        {
            if (!IsInstalled()) return Array.Empty<string>();
            var sb = new StringBuilder(4096);
            return Nx.DAQmxGetSysDevNames(sb, (uint)sb.Capacity) < 0 ? Array.Empty<string>() : Split(sb.ToString());
        }

        public static IReadOnlyList<string> PhysicalChannels(string device)
        {
            if (!IsInstalled() || string.IsNullOrWhiteSpace(device)) return Array.Empty<string>();
            var sb = new StringBuilder(8192);
            return Nx.DAQmxGetDevAIPhysicalChans(device, sb, (uint)sb.Capacity) < 0 ? Array.Empty<string>() : Split(sb.ToString());
        }

        private static string[] Split(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        protected override void OnStart()
        {
            if (!IsInstalled()) throw new InvalidOperationException("NI-DAQmx no está instalado (falta nicaiu.dll). Instalar el controlador de NI.");
            var s = Settings;
            Check(Nx.DAQmxCreateTask("", out task));
            foreach (var c in Active)
            {
                string phys = c.Physical.Contains('/') ? c.Physical : $"{s.Device}/{c.Physical}";
                if (c.Kind is SensorKind.AccelerometerIepe or SensorKind.Accelerometer)
                {
                    bool iepe = c.Kind == SensorKind.AccelerometerIepe && c.IepeMilliamps > 0;
                    Check(Nx.DAQmxCreateAIAccelChan(task, phys, "", Nx.CfgDefault, -c.Range, c.Range, Nx.AccelUnitG, c.Sensitivity, Nx.MVoltsPerG,
                        iepe ? Nx.Internal : Nx.None, iepe ? c.IepeMilliamps / 1000 : 0, null));
                }
                else
                {
                    double v = c.RangeVolts;
                    Check(Nx.DAQmxCreateAIVoltageChan(task, phys, "", Nx.CfgDefault, -v, v, Nx.Volts, null));
                    if (c.Kind == SensorKind.VelocitySensor && c.IepeMilliamps > 0)
                    {
                        Check(Nx.DAQmxSetAIExcitSrc(task, phys, Nx.Internal));
                        Check(Nx.DAQmxSetAIExcitVal(task, phys, c.IepeMilliamps / 1000));
                        Check(Nx.DAQmxSetAICoupling(task, phys, Nx.Ac));
                    }
                }
            }
            Check(Nx.DAQmxCfgSampClkTiming(task, "", s.SampleRate, Nx.Rising, Nx.ContSamps, (ulong)Math.Max(1000, s.SampleRate * 4)));
            Check(Nx.DAQmxStartTask(task));
            run = true;
            worker = new Thread(Loop) { IsBackground = true, Name = "NI-DAQmx" };
            worker.Start();
        }

        private void Loop()
        {
            int n = Active.Count, per = Math.Max(64, (int)(Settings.SampleRate / 10));
            var buf = new double[n * per];
            try
            {
                while (run)
                {
                    int err = Nx.DAQmxReadAnalogF64(task, per, 2.0, Nx.GroupByChannel, buf, (uint)buf.Length, out int read, IntPtr.Zero);
                    if (err < 0) { LastError = ErrorText(err); IsRunning = false; return; }
                    if (read <= 0) continue;
                    var block = new double[n][];
                    for (int c = 0; c < n; c++)
                    {
                        block[c] = new double[read];
                        // Los canales de aceleración llegan en g: se pasan a volts para que la conversión sea la misma que en los demás equipos
                        double k = Active[c].Kind is SensorKind.AccelerometerIepe or SensorKind.Accelerometer ? Active[c].Sensitivity / 1000 : 1;
                        for (int i = 0; i < read; i++) block[c][i] = buf[c * per + i] * k;
                    }
                    Push(block);
                }
            }
            catch (Exception ex) { LastError = ex.Message; IsRunning = false; }
        }

        protected override void OnStop()
        {
            run = false;
            worker?.Join(3000);
            if (task != IntPtr.Zero)
            {
                Nx.DAQmxStopTask(task);
                Nx.DAQmxClearTask(task);
                task = IntPtr.Zero;
            }
        }

        private void Check(int err)
        {
            if (err >= 0) return;
            string msg = ErrorText(err);
            LastError = msg;
            try { OnStop(); } catch { }
            throw new InvalidOperationException(msg);
        }

        private static string ErrorText(int err)
        {
            var sb = new StringBuilder(2048);
            Nx.DAQmxGetExtendedErrorInfo(sb, (uint)sb.Capacity);
            return $"NI-DAQmx {err}: {sb}";
        }

        private static class Nx
        {
            public const int CfgDefault = -1, AccelUnitG = 10186, MVoltsPerG = 12509, Internal = 10200, None = 10230, Volts = 10348;
            public const int Rising = 10280, ContSamps = 10123, Ac = 10045;
            public const uint GroupByChannel = 0;
            private const string Dll = "nicaiu.dll";
            [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern int DAQmxCreateTask(string name, out IntPtr task);
            [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern int DAQmxCreateAIAccelChan(IntPtr task, string physical, string name, int terminalConfig, double min, double max, int units, double sensitivity, int sensitivityUnits, int excitSource, double excitVal, string customScale);
            [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern int DAQmxCreateAIVoltageChan(IntPtr task, string physical, string name, int terminalConfig, double min, double max, int units, string customScale);
            [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern int DAQmxSetAIExcitSrc(IntPtr task, string channel, int source);
            [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern int DAQmxSetAIExcitVal(IntPtr task, string channel, double value);
            [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern int DAQmxSetAICoupling(IntPtr task, string channel, int coupling);
            [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern int DAQmxCfgSampClkTiming(IntPtr task, string source, double rate, int activeEdge, int sampleMode, ulong samplesPerChannel);
            [DllImport(Dll)] public static extern int DAQmxStartTask(IntPtr task);
            [DllImport(Dll)] public static extern int DAQmxStopTask(IntPtr task);
            [DllImport(Dll)] public static extern int DAQmxClearTask(IntPtr task);
            [DllImport(Dll)] public static extern int DAQmxReadAnalogF64(IntPtr task, int samplesPerChannel, double timeout, uint fillMode, [Out] double[] data, uint arraySize, out int read, IntPtr reserved);
            [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern int DAQmxGetExtendedErrorInfo(StringBuilder buffer, uint size);
            [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern int DAQmxGetSysDevNames(StringBuilder buffer, uint size);
            [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern int DAQmxGetDevAIPhysicalChans(string device, StringBuilder buffer, uint size);
        }
    }
}
