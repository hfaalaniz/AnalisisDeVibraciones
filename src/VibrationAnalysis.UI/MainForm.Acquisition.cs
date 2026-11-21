using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using VibrationAnalysis.Core.Acquisition;
using VibrationAnalysis.Core.Analysis;
using VibrationAnalysis.Core.Balancing;
using VibrationAnalysis.Core.Dsp;
using VibrationAnalysis.Core.IO;
using VibrationAnalysis.Core.Model;
using VibrationAnalysis.Core.Report;
using VibrationAnalysis.Core.Simulation;
using VibrationAnalysis.Core.Trending;
using VibrationAnalysis.UI.Devices;

namespace VibrationAnalysis.UI
{
    /// <summary>Pestaña 2: adquisición (simulador, archivos, sensor serie o de red, placa de sonido y NI-DAQmx).</summary>
    public sealed partial class MainForm
    {
        // ───────────────────────────── 2. Adquisición ─────────────────────────────
        private TabPage BuildAcquisitionTab()
        {
            var page = new TabPage("2. Adquisición");
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = true };
            cmbSource = Combo(new[] { "Simulador", "Archivo CSV / WAV", "Puerto serie (sensor)", "Ingreso manual (solo balanceo)", "Placa de sonido (IEPE)", "NI-DAQmx (IEPE)", "Sensor por red (WiFi/TCP)" }, 170);
            cmbSource.SelectedIndexChanged += (s, e) => OnSourceChanged();
            btnAcquire = Btn("Adquirir", (s, e) => Acquire());
            chkLive = new CheckBox { Text = "En vivo (1 s)", AutoSize = true, Margin = new Padding(8, 8, 8, 0) };
            chkLive.CheckedChanged += (s, e) => { if (chkLive.Checked) liveTimer.Start(); else liveTimer.Stop(); };
            cmbPort = Combo(SerialPort.GetPortNames(), 90);
            cmbBaud = Combo(new[] { "921600", "115200", "230400", "460800", "1000000", "2000000", "3000000" }, 90);
            cmbSerialMode = Combo(new[] { "Auto", "Texto", "Binario" }, 75);
            nudCapture = Nud(0.5m, 30, 2, 1); nudCapture.Width = 60;
            lblLink = new Label { AutoSize = true, Margin = new Padding(8, 8, 0, 0), ForeColor = Color.FromArgb(14, 124, 134) };
            linkTimer.Tick += (s, e) =>
            {
                if (port?.IsOpen == true || netSensor?.Connected == true) lock (serialParser) lblLink.Text = serialParser.Status + (serialParser.LastMessage.Length > 0 ? " · " + serialParser.LastMessage : "");
                else if (device != null)
                {
                    lblLink.Text = device.Status + (lastIssues.Count > 0 ? " · " + string.Join(" · ", lastIssues.Select(q => $"{q.Channel}: {q.Message}")) : "");
                    lblLink.ForeColor = lastIssues.Any(q => q.Level == IssueLevel.Error) ? Color.FromArgb(200, 40, 40) : lastIssues.Any(q => q.Level == IssueLevel.Warning) ? Color.FromArgb(210, 130, 0) : Color.FromArgb(14, 124, 134);
                    if (!device.IsRunning) btnConnect.Text = "Conectar";
                }
            };
            btnConnect = Btn("Conectar", (s, e) => { if (CurrentSource is Source.SoundCard or Source.Daq) ToggleDevice(); else if (CurrentSource == Source.Network) ToggleNetSensor(); else ToggleSerial(); });
            txtNetSensor = new TextBox { Width = 150, Text = "192.168.4.1:3333", Margin = new Padding(3, 5, 3, 3) };
            btnDaqSetup = Btn("Configurar equipo…", (s, e) => SetupDevice());
            cmbChannel = Combo(new string[0], 110);
            cmbChannel.SelectedIndexChanged += (s, e) => RefreshAnalysis();
            cmbUnitView = Combo(new[] { "Velocidad (mm/s)", "Aceleración (g)", "Desplazamiento (µm)" }, 150);
            cmbUnitView.SelectedIndexChanged += (s, e) => RefreshAnalysis();
            cmbWindow = Combo(new[] { "Hanning", "Flat-top", "Hamming", "Rectangular" }, 100);
            cmbWindow.SelectedIndexChanged += (s, e) => RefreshAnalysis();
            btnExportCsv = Btn("Exportar CSV", (s, e) => ExportCsv());
            top.Controls.AddRange(new Control[] { Lbl("Fuente:"), cmbSource, btnAcquire, chkLive, Lbl("Puerto:"), cmbPort, cmbBaud, txtNetSensor, Lbl("Modo:"), cmbSerialMode, Lbl("Capturar (s):"), nudCapture, btnConnect, btnDaqSetup, Btn("Info", (s, e) => { try { port?.WriteLine("INFO"); } catch { } }), Lbl("Canal:"), cmbChannel, Lbl("Ver:"), cmbUnitView, Lbl("Ventana:"), cmbWindow, btnExportCsv, lblLink });

            lblStats = new Label { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 4, 8, 4), Font = new Font("Consolas", 9.5f) };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2 };
            split.HandleCreated += (s, e) => { try { split.SplitterDistance = Math.Max(300, split.Width - 380); } catch { } };
            var charts = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            charts.HandleCreated += (s, e) => { try { charts.SplitterDistance = Math.Max(120, charts.Height * 2 / 5); } catch { } };
            signal = new SignalControl { Dock = DockStyle.Fill };
            spectrum = new SpectrumPlotControl { Dock = DockStyle.Fill };
            charts.Panel1.Controls.Add(signal);
            charts.Panel2.Controls.Add(spectrum);
            split.Panel1.Controls.Add(charts);
            simGrid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = new SimulatorView(simSettings), ToolbarVisible = false, HelpVisible = true };
            WideLabels(simGrid, 0.62);
            simGrid.PropertyValueChanged += (s, e) => { sim = new RotorSimulator(simSettings); Acquire(silent: true); };
            var simBox = new GroupBox { Text = "Rotor virtual (simulador)", Dock = DockStyle.Fill };
            simBox.Controls.Add(simGrid);
            split.Panel2.Controls.Add(simBox);
            page.Controls.Add(split);
            page.Controls.Add(lblStats);
            page.Controls.Add(top);
            OnSourceChanged();
            return page;
        }

        private Source CurrentSource => (Source)Math.Max(0, cmbSource.SelectedIndex);

        private void OnSourceChanged()
        {
            var src = CurrentSource;
            bool dev = src is Source.SoundCard or Source.Daq;
            cmbPort.Enabled = cmbBaud.Enabled = src == Source.Serial;
            cmbSerialMode.Enabled = src is Source.Serial or Source.Network;
            txtNetSensor.Enabled = src == Source.Network;
            btnConnect.Enabled = nudCapture.Enabled = src is Source.Serial or Source.Network || dev;
            if (netSensor != null && src != Source.Network) CloseNetSensor();
            btnDaqSetup.Enabled = dev;
            if (device != null && !dev) StopDevice();
            chkLive.Enabled = src is Source.Simulator or Source.Serial or Source.SoundCard or Source.Daq or Source.Network;
            btnAcquire.Text = src switch { Source.File => "Abrir archivo…", Source.Serial or Source.SoundCard or Source.Daq or Source.Network => "Capturar", Source.Manual => "—", _ => "Adquirir" };
            btnAcquire.Enabled = src != Source.Manual;
            if (simGrid != null) simGrid.Parent.Visible = src == Source.Simulator;
            if (chkAutoPlace != null) { chkAutoPlace.Enabled = src == Source.Simulator; chkAutoPlace.Checked = src == Source.Simulator; }
            if (rbManual != null && src == Source.Manual) rbManual.Checked = true;
        }

        /// <summary>Obtiene una nueva adquisición desde la fuente seleccionada. Devuelve false si no hay datos.</summary>
        private bool Acquire(bool silent = false)
        {
            try
            {
                Recording r = null;
                switch (CurrentSource)
                {
                    case Source.Simulator:
                        r = sim.Acquire();
                        break;
                    case Source.File:
                        r = OpenRecordingFile();
                        if (r == null) return false;
                        break;
                    case Source.Serial:
                        if (port == null || !port.IsOpen) { if (!silent) Warn("Conectá primero el puerto serie."); return false; }
                        lock (serialParser) r = serialParser.ToRecording(machine.Rpm, (double)nudCapture.Value);
                        if (r.Length < 256) { if (!silent) Warn("Todavía no llegaron suficientes muestras del sensor."); return false; }
                        break;
                    case Source.Network:
                        if (netSensor?.Connected != true) { if (!silent) Warn("Conectá primero el sensor de red."); return false; }
                        lock (serialParser) r = serialParser.ToRecording(machine.Rpm, (double)nudCapture.Value);
                        if (r.Length < 256) { if (!silent) Warn("Todavía no llegaron suficientes muestras del sensor."); return false; }
                        break;
                    case Source.SoundCard:
                    case Source.Daq:
                        if (device == null || !device.IsRunning) { if (!silent) Warn("Conectá primero el equipo (botón «Conectar»)."); return false; }
                        (r, lastIssues) = device.Snapshot((double)nudCapture.Value, machine.Rpm);
                        if (!silent && lastIssues.Any(q => q.Level == IssueLevel.Error))
                            SetStatus("Atención: " + string.Join(" · ", lastIssues.Where(q => q.Level == IssueLevel.Error).Select(q => $"{q.Channel}: {q.Message}")));
                        break;
                    default:
                        return false;
                }
                SetRecording(r);
                if (!silent) SetStatus($"Adquisición: {r.Source}, {r.Channels.Count} canal(es), {r.Length} muestras a {r.SampleRate:0} Hz ({r.Duration:0.00} s).");
                return true;
            }
            catch (Exception ex)
            {
                Error("No se pudo adquirir la señal", ex);
                return false;
            }
        }

        private Recording OpenRecordingFile()
        {
            using var ofd = new OpenFileDialog { Filter = "Señales (*.csv;*.txt;*.wav)|*.csv;*.txt;*.wav|Todos los archivos|*.*", Title = "Abrir señal de vibración" };
            if (ofd.ShowDialog(this) != DialogResult.OK) return null;
            if (Path.GetExtension(ofd.FileName).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                using var dlg = new WavOptionsForm();
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                return WavRecordingReader.Read(File.ReadAllBytes(ofd.FileName), dlg.ScaleFactor, dlg.Unit, dlg.TachChannel, machine.Rpm);
            }
            return CsvRecordingReader.Parse(File.ReadAllText(ofd.FileName), 0, VibrationUnit.VelocityMmS, machine.Rpm);
        }

        private void SetRecording(Recording r)
        {
            recording = r;
            int sel = Math.Max(0, cmbChannel.SelectedIndex);
            cmbChannel.Items.Clear();
            foreach (var c in r.Channels) cmbChannel.Items.Add(c.Name);
            cmbChannel.SelectedIndex = Math.Min(sel, r.Channels.Count - 1);
            RefreshAnalysis();
        }

        private VibrationUnit ViewUnit => cmbUnitView.SelectedIndex switch { 1 => VibrationUnit.AccelerationG, 2 => VibrationUnit.DisplacementUm, _ => VibrationUnit.VelocityMmS };
        private WindowType ViewWindow => cmbWindow.SelectedIndex switch { 1 => WindowType.FlatTop, 2 => WindowType.Hamming, 3 => WindowType.Rectangular, _ => WindowType.Hann };

        private double CurrentRpm() => recording?.MeasuredRpm() is double r && r > 0 ? r : machine.Rpm;

        /// <summary>Recalcula gráficos, estadísticas, severidad y diagnóstico con la adquisición actual.</summary>
        private void RefreshAnalysis()
        {
            if (recording == null || cmbChannel.SelectedIndex < 0) return;
            try
            {
                var ch = recording.Channels[cmbChannel.SelectedIndex];
                double fs = recording.SampleRate;
                bool electrical = SignalTools.IsElectrical(ch.Unit);
                var view = SignalTools.Convert(ch.Data, fs, ch.Unit, ViewUnit, ViewUnit == VibrationUnit.AccelerationG && ch.Unit is VibrationUnit.AccelerationG or VibrationUnit.AccelerationMs2 ? 0 : 2.0);
                var times = Enumerable.Range(0, view.Length).Select(i => i / fs).ToArray();
                double rpm = CurrentRpm();
                signal.FundamentalFrequency = rpm / 60;
                signal.SetData(times, view);
                var sp = SpectrumAnalyzer.Compute(view, fs, ViewWindow);
                int maxBin = Math.Min(sp.Frequencies.Length, (int)Math.Ceiling(Math.Min(fs / 2, Math.Max(20 * rpm / 60, 1000)) / sp.Resolution) + 1);
                spectrum.FundamentalFrequency = rpm / 60;
                if (machine.Bearing != null) spectrum.BearingDefectFrequency = machine.Bearing.BpfoOrder * rpm / 60;
                spectrum.SetData(sp.Frequencies.Take(maxBin).ToArray(), sp.Amplitudes.Take(maxBin).ToArray());
                var st = SignalTools.Statistics(view);
                string u = SignalTools.Symbol(electrical ? ch.Unit : ViewUnit);
                string oneX = recording.HasPhaseReference ? recording.Reading1X(cmbChannel.SelectedIndex).ToString() + " (" + SignalTools.Symbol(ch.Unit) + " pico ∠ fase)" : "sin tacómetro: sin fase";
                lblStats.Text = $"RMS {st.Rms:0.000} {u}   Pico {st.Peak:0.000}   Pico-pico {st.PeakToPeak:0.000}   Factor de cresta {st.CrestFactor:0.00}   Curtosis {st.Kurtosis:0.00}\n" +
                                $"Velocidad {rpm:0.0} rpm ({rpm / 60:0.00} Hz)   1X: {oneX}   Resolución {sp.Resolution:0.###} Hz   {recording.Duration:0.00} s";
                UpdateDiagnosis();
                if (!electrical) PublishCurrentToModbus();
                if (tabs.SelectedTab == advPage) RunAdvancedAnalysis();
                else if (tabs.SelectedTab == trPage) RunTransientAnalysis();
            }
            catch (Exception ex)
            {
                Error("Error al analizar la señal", ex);
            }
        }

        // ───────── Placa de sonido y NI-DAQmx ─────────
        private static string DevicePath(bool sound) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnalisisVibraciones", sound ? "placa_sonido.json" : "ni_daqmx.json");

        private AcquisitionSettings DeviceSettings(bool sound)
        {
            var slot = sound ? soundSettings : daqSettings;
            if (slot != null) return slot;
            try { if (File.Exists(DevicePath(sound))) slot = JsonSerializer.Deserialize<AcquisitionSettings>(File.ReadAllText(DevicePath(sound))); } catch (Exception ex) { LogManager.Instance.LogError("Config. del equipo: " + ex.Message); }
            slot ??= sound
                ? new AcquisitionSettings { SampleRate = 48000, FullScaleVolts = 1.0 }
                : new AcquisitionSettings
                {
                    SampleRate = 25600, Device = NiDaqmxDevice.Devices().FirstOrDefault() ?? "Dev1",
                    Channels = new List<ChannelConfig>
                    {
                        new ChannelConfig { Name = "Acelerómetro 1", Physical = "ai0" },
                        new ChannelConfig { Name = "Acelerómetro 2", Physical = "ai1", Enabled = false },
                        new ChannelConfig { Name = "Tacómetro", Physical = "ai2", Kind = SensorKind.Tachometer, IepeMilliamps = 0 },
                    },
                };
            if (sound) soundSettings = slot; else daqSettings = slot;
            return slot;
        }

        private static IAcquisitionDevice NewDevice(bool sound) => sound ? new SoundCardDevice() : new NiDaqmxDevice();

        private void SetupDevice()
        {
            bool sound = CurrentSource == Source.SoundCard;
            using var dlg = new DaqSetupForm(DeviceSettings(sound), sound, () => NewDevice(sound));
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (sound) soundSettings = dlg.Result; else daqSettings = dlg.Result;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DevicePath(sound)));
                File.WriteAllText(DevicePath(sound), JsonSerializer.Serialize(dlg.Result, ProjOpts), new UTF8Encoding(false));
            }
            catch (Exception ex) { LogManager.Instance.LogError("Guardar config. del equipo: " + ex.Message); }
            if (device != null) { StopDevice(); ToggleDevice(); }
        }

        private void ToggleDevice()
        {
            if (device != null && device.IsRunning) { StopDevice(); SetStatus("Equipo detenido."); return; }
            bool sound = CurrentSource == Source.SoundCard;
            if (!sound && !NiDaqmxDevice.IsInstalled()) { Warn("NI-DAQmx no está instalado en este equipo. Instalá el controlador de NI o usá la placa de sonido."); return; }
            try
            {
                StopDevice();
                device = NewDevice(sound);
                device.Start(DeviceSettings(sound));
                btnConnect.Text = "Desconectar";
                linkTimer.Start();
                SetStatus($"Adquiriendo con {device.Name}. «Capturar» toma los últimos {nudCapture.Value:0.#} s.");
            }
            catch (Exception ex) { Error("No se pudo iniciar el equipo", ex); StopDevice(); }
        }

        private void StopDevice()
        {
            try { device?.Dispose(); } catch (Exception ex) { LogManager.Instance.LogError("Detener equipo: " + ex.Message); }
            device = null;
            lastIssues = Array.Empty<QualityIssue>();
            if (btnConnect != null) btnConnect.Text = "Conectar";
            if (lblLink != null) { lblLink.Text = ""; lblLink.ForeColor = Color.FromArgb(14, 124, 134); }
            if (port?.IsOpen != true) linkTimer.Stop();
        }

        private void ToggleSerial()
        {
            if (port != null && port.IsOpen) { ClosePort(); btnConnect.Text = "Conectar"; SetStatus("Puerto cerrado."); return; }
            if (cmbPort.SelectedItem == null)
            {
                cmbPort.Items.Clear(); cmbPort.Items.AddRange(SerialPort.GetPortNames());
                Warn("Elegí el puerto COM del sensor (si no aparece, conectalo y volvé a intentar).");
                return;
            }
            try
            {
                port = new SerialPort(cmbPort.SelectedItem.ToString(), int.Parse(cmbBaud.SelectedItem?.ToString() ?? "115200", CultureInfo.InvariantCulture)) { NewLine = "\n", ReadTimeout = 500 };
                serialParser.Clear();
                port.DataReceived += (s, e) =>
                {
                    try
                    {
                        var sp = (SerialPort)s;
                        int n = sp.BytesToRead;
                        if (n <= 0) return;
                        var data = new byte[n];
                        int got = sp.Read(data, 0, n);
                        lock (serialParser) serialParser.Feed(data, 0, got);
                    }
                    catch (Exception ex) { LogManager.Instance.LogError("Serie: " + ex.Message); }
                };
                port.ReadBufferSize = 1 << 20;
                port.Open();
                if (cmbSerialMode.SelectedIndex == 1) port.WriteLine("TEXT");
                else if (cmbSerialMode.SelectedIndex == 2) port.WriteLine("BIN");
                port.WriteLine("START");
                linkTimer.Start();
                btnConnect.Text = "Desconectar";
                SetStatus($"Conectado a {port.PortName}. Esperando la cabecera #VA del sensor…");
            }
            catch (Exception ex) { Error("No se pudo abrir el puerto", ex); ClosePort(); }
        }

        private void ClosePort()
        {
            linkTimer.Stop();
            if (lblLink != null) lblLink.Text = "";
            try { if (port?.IsOpen == true) { port.WriteLine("STOP"); port.Close(); } } catch { }
            port?.Dispose();
            port = null;
        }

        private void ExportCsv()
        {
            if (recording == null) { Warn("No hay ninguna señal adquirida."); return; }
            using var sfd = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"senal_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(sfd.FileName, CsvRecordingReader.Write(recording), new UTF8Encoding(false));
            SetStatus("Señal exportada: " + sfd.FileName);
        }
    }
}
