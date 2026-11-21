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
    /// <summary>
    /// Ventana principal. Flujo en pestañas numeradas: 1 Máquina → 2 Adquisición → 3 Diagnóstico → 4 Análisis avanzado → 5 Órdenes y transitorios → 6 Tendencias → 7 Red y planta → 8 Balanceo → 9 Informe.
    /// Toda la matemática está en VibrationAnalysis.Core (con pruebas); acá solo hay interfaz.
    /// </summary>
    public sealed partial class MainForm : Form
    {
        private enum Source { Simulator, File, Serial, Manual, SoundCard, Daq, Network }

        private const string WebUrl = "https://analisisdevibraciones.pages.dev/";
        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

        private readonly Machine machine = new Machine { Name = "Ventilador de prueba", Rpm = 1500, DiameterMm = 450, LengthMm = 220, CorrectionRadiusMm = 200, MassKg = 38 };
        private readonly SimulatorSettings simSettings = new SimulatorSettings();
        private RotorSimulator sim;
        private Recording recording;
        private BalancingSession session;
        private SerialPort port;
        private readonly SerialStreamParser serialParser = new SerialStreamParser();
        private IAcquisitionDevice device;
        private AcquisitionSettings soundSettings, daqSettings;
        private Button btnDaqSetup;
        private IReadOnlyList<QualityIssue> lastIssues = Array.Empty<QualityIssue>();
        private readonly ThemeManager themes = new ThemeManager();
        private Theme theme;
        private string projectPath;

        // Controles
        private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        private readonly ToolStripStatusLabel status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolStripStatusLabel stepStatus = new ToolStripStatusLabel();
        private PropertyGrid machineGrid, simGrid;
        private Label machineSummary;
        private ComboBox cmbSource, cmbChannel, cmbUnitView, cmbWindow, cmbPort, cmbBaud;
        private Button btnAcquire, btnConnect, btnExportCsv;
        private CheckBox chkLive;
        private Label lblStats;
        private SignalControl signal;
        private SpectrumPlotControl spectrum;
        private readonly Timer liveTimer = new Timer { Interval = 1000 };
        private readonly Timer linkTimer = new Timer { Interval = 500 };
        private ComboBox cmbSerialMode;
        private NumericUpDown nudCapture;
        private Label lblLink;
        private Label lblSeverity, lblSeverityDesc;
        private Panel severityBar;
        private ListView lvFindings;
        private ComboBox cmbPlanes, cmbAngles;
        private CheckBox chkKeepTrial, chkAutoPlace;
        private NumericUpDown nudTolerance, nudT1g, nudT1a, nudT2g, nudT2a, nudM1a, nudM1p, nudM2a, nudM2p, nudPositions, nudDrill;
        private RadioButton rbFromAcq, rbManual;
        private ListBox lstSteps;
        private Label lblInstruction, lblSuggest;
        private Button btnMeasure, btnNewSession;
        private RichTextBox txtResult;
        private ListView lvRuns;
        private BalancePolarControl polar1, polar2;
        private TextBox txtTech, txtCompany, txtNotes;
        private TabPage advPage;
        private XYPlotControl advPlot;
        private ComboBox cmbAdvMode, cmbAdvUnit, cmbAdvWindow, cmbAdvBlock, cmbAdvAvg;
        private NumericUpDown nudAdvOverlap, nudBandLow, nudBandHigh, nudZoomCenter, nudZoomSpan, nudAdvMaxHz;
        private CheckBox chkAdvLog, chkAdvMarkers;
        private ListView lvAdvPeaks;
        private Label lblAdvSummary;
        private FlowLayoutPanel pnlAdvBand, pnlAdvZoom, pnlAdvBlock;
        private TabPage trPage;
        private ComboBox cmbTrMode, cmbTrUnit, cmbTrBlock, cmbTrY;
        private NumericUpDown nudTrOrder, nudTrMaxOrder, nudTrRevs, nudTrMaxHz;
        private CheckBox chkTrFilter;
        private Panel trHost;
        private XYPlotControl trOrderPlot, trBodeAmp, trBodePhase;
        private PathPlotControl trNyquist, trOrbit;
        private WaterfallControl trWaterfall;
        private HeatmapControl trHeatmap;
        private ListView lvTr;
        private Label lblTrSummary;
        private string trCsv = "";
        private TabPage trendPage;
        private TrendDatabase trendDb;
        private TreeView tvAssets;
        private XYPlotControl trendPlot;
        private ComboBox cmbTrendFeature, cmbRoute;
        private ListView lvMeas, lvAlarms;
        private Label lblTrendSummary, lblRoute, lblDbPath;
        private CheckBox chkSaveWave;
        private int routeIndex = -1;
        private CheckBox chkTemp;
        private NumericUpDown nudTemp;

        public MainForm()
        {
            Text = "Análisis de Vibraciones y Balanceo · Fabian Alaniz – ElectroNet";
            Font = new Font("Segoe UI", 9.5f);
            MinimumSize = new Size(1100, 720);
            Size = new Size(1360, 860);
            StartPosition = FormStartPosition.CenterScreen;
            sim = new RotorSimulator(simSettings);

            var menu = BuildMenu();
            var statusStrip = new StatusStrip();
            statusStrip.Items.Add(status);
            statusStrip.Items.Add(stepStatus);

            tabs.TabPages.Add(BuildMachineTab());
            tabs.TabPages.Add(BuildAcquisitionTab());
            tabs.TabPages.Add(BuildDiagnosisTab());
            tabs.TabPages.Add(BuildAnalysisTab());
            tabs.TabPages.Add(BuildTransientTab());
            tabs.TabPages.Add(BuildTrendTab());
            tabs.TabPages.Add(BuildNetworkTab());
            tabs.TabPages.Add(BuildBalancingTab());
            tabs.TabPages.Add(BuildReportTab());

            Controls.Add(tabs);
            Controls.Add(statusStrip);
            Controls.Add(menu);
            MainMenuStrip = menu;

            tabs.SelectedIndexChanged += (s, e) => { if (tabs.SelectedTab == advPage) RunAdvancedAnalysis(); else if (tabs.SelectedTab == trPage) RunTransientAnalysis(); else if (tabs.SelectedTab == trendPage) EnsureTrendDb(); };
            liveTimer.Tick += (s, e) => { if (chkLive.Checked) Acquire(silent: true); };
            FormClosing += (s, e) => { liveTimer.Stop(); ClosePort(); StopDevice(); CloseNetSensor(); ShutdownNetwork(); };

            ApplyTheme("Claro");
            NewSession();
            UpdateMachineSummary();
            SetStatus("Listo. Completá los datos de la máquina y adquirí una señal (el simulador está seleccionado).");
            Shown += (s, e) => { if (CurrentSource == Source.Simulator && recording == null) Acquire(silent: true); };
            LogManager.Instance.LogInfo("Aplicación iniciada.");
        }

        // ───────────────────────────── Menú ─────────────────────────────
        private MenuStrip BuildMenu()
        {
            var m = new MenuStrip();
            var file = new ToolStripMenuItem("&Archivo");
            file.DropDownItems.Add("Nuevo proyecto", null, (s, e) => { NewSession(); projectPath = null; SetStatus("Proyecto nuevo."); });
            file.DropDownItems.Add("Abrir proyecto…", null, (s, e) => OpenProject());
            file.DropDownItems.Add("Guardar proyecto", null, (s, e) => SaveProject(false));
            file.DropDownItems.Add("Guardar proyecto como…", null, (s, e) => SaveProject(true));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("Importar señal (CSV / WAV)…", null, (s, e) => { cmbSource.SelectedIndex = (int)Source.File; Acquire(); });
            file.DropDownItems.Add("Exportar señal a CSV…", null, (s, e) => ExportCsv());
            file.DropDownItems.Add("Generar informe…", null, (s, e) => GenerateReport());
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("Salir", null, (s, e) => Close());
            var view = new ToolStripMenuItem("&Ver");
            foreach (var t in themes.GetAvailableThemes())
            {
                var name = t;
                view.DropDownItems.Add("Tema: " + name, null, (s, e) => ApplyTheme(name));
            }
            view.DropDownItems.Add("Cargar tema desde archivo…", null, (s, e) => LoadThemeFile());
            var help = new ToolStripMenuItem("A&yuda");
            help.DropDownItems.Add("Versión web y manual", null, (s, e) => OpenUrl(WebUrl));
            help.DropDownItems.Add("Carpeta de registros (logs)", null, (s, e) => OpenUrl(Path.GetDirectoryName(LogManager.Instance.LogFilePath)));
            help.DropDownItems.Add("Acerca de…", null, (s, e) => MessageBox.Show(this,
                "Análisis de Vibraciones y Balanceo 2.0\n\nAutor: Fabian Alaniz – ElectroNet\n+54 9 351 284-8802 · www.developers-soft.com\n\nFFT, severidad ISO 10816, diagnóstico de fallas y balanceo de campo en 1 y 2 planos por coeficientes de influencia (ISO 1940/21940).",
                "Acerca de", MessageBoxButtons.OK, MessageBoxIcon.Information));
            m.Items.AddRange(new ToolStripItem[] { file, view, help });
            return m;
        }

        // ───────────────────────────── 1. Máquina ─────────────────────────────
        private TabPage BuildMachineTab()
        {
            var page = new TabPage("1. Máquina");
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 520, FixedPanel = FixedPanel.Panel1 };
            machineGrid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = new MachineView(machine), PropertySort = PropertySort.Categorized, ToolbarVisible = false };
            WideLabels(machineGrid, 0.6);
            machineGrid.PropertyValueChanged += (s, e) => { UpdateMachineSummary(); RefreshAnalysis(); };
            split.Panel1.Controls.Add(machineGrid);
            machineSummary = new Label { Dock = DockStyle.Fill, Padding = new Padding(16), Font = new Font("Segoe UI", 10.5f) };
            split.Panel2.Controls.Add(machineSummary);
            page.Controls.Add(split);
            return page;
        }

        private void UpdateMachineSummary()
        {
            var (planes, reason) = machine.RecommendedPlanes();
            double mass = machine.EffectiveMassKg;
            double uper = BalanceQuality.PermissibleUnbalanceGmm(machine.BalanceGrade, mass, machine.Rpm);
            double trial = TrialWeight.SuggestGrams(mass, machine.Rpm, machine.CorrectionRadiusMm);
            var sb = new StringBuilder();
            sb.AppendLine($"Masa del rotor: {mass:0.0} kg {(machine.MassKg > 0 ? "(informada)" : "(estimada por geometría)")}");
            sb.AppendLine($"Velocidad: {machine.Rpm:0} rpm = {machine.Rpm / 60:0.00} Hz (1X)");
            sb.AppendLine();
            sb.AppendLine($"Planos recomendados: {planes}");
            sb.AppendLine(reason);
            sb.AppendLine();
            sb.AppendLine($"Calidad de balanceo G{machine.BalanceGrade.ToString("0.#", Es)} (ISO 1940): {BalanceQuality.Examples(machine.BalanceGrade)}");
            sb.AppendLine($"Desbalance residual admisible: {uper:0} g·mm en total");
            sb.AppendLine($"  = {uper / machine.CorrectionRadiusMm:0.00} g a {machine.CorrectionRadiusMm:0} mm de radio");
            if (planes == 2) sb.AppendLine($"  = {uper / 2 / machine.CorrectionRadiusMm:0.00} g por plano (reparto simétrico)");
            sb.AppendLine($"Excentricidad admisible: {BalanceQuality.PermissibleEccentricityUm(machine.BalanceGrade, machine.Rpm):0.0} µm");
            sb.AppendLine();
            sb.AppendLine($"Peso de prueba sugerido: {trial:0.0} g a {machine.CorrectionRadiusMm:0} mm (fuerza ≈ 10 % del peso del rotor).");
            if (machine.Bearing is { } b)
            {
                double f = machine.Rpm / 60;
                sb.AppendLine();
                sb.AppendLine($"Frecuencias de rodamiento: BPFO {b.BpfoOrder:0.00}X ({b.BpfoOrder * f:0.0} Hz) · BPFI {b.BpfiOrder:0.00}X ({b.BpfiOrder * f:0.0} Hz) · BSF {b.BsfOrder:0.00}X · FTF {b.FtfOrder:0.000}X");
            }
            sb.AppendLine();
            sb.AppendLine($"Severidad: {Severity.Name(machine.MachineClass)}; límites A/B, B/C, C/D = {string.Join(" / ", Severity.Limits(machine.MachineClass).Select(v => v.ToString("0.0#", Es)))} mm/s RMS.");
            machineSummary.Text = sb.ToString();
            if (lblSuggest != null) lblSuggest.Text = $"Peso de prueba sugerido: {trial:0.0} g";
        }

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

        // ───────────────────────────── 3. Diagnóstico ─────────────────────────────
        private TabPage BuildDiagnosisTab()
        {
            var page = new TabPage("3. Diagnóstico");
            var head = new Panel { Dock = DockStyle.Top, Height = 120, Padding = new Padding(12) };
            lblSeverity = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 40, Font = new Font("Segoe UI", 18f, FontStyle.Bold), Text = "Sin datos", Tag = "keepColor" };
            severityBar = new Panel { Dock = DockStyle.Top, Height = 10, Tag = "keepColor" };
            lblSeverityDesc = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f) };
            head.Controls.Add(lblSeverityDesc);
            head.Controls.Add(severityBar);
            head.Controls.Add(lblSeverity);
            lvFindings = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            lvFindings.Columns.Add("Falla probable", 230);
            lvFindings.Columns.Add("Confianza", 80);
            lvFindings.Columns.Add("Evidencia", 420);
            lvFindings.Columns.Add("Recomendación", 520);
            var note = new Label { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(8), Text = "El diagnóstico automático es orientativo: confirmar con mediciones en las direcciones horizontal, vertical y axial, y con la fase." };
            page.Controls.Add(lvFindings);
            page.Controls.Add(note);
            page.Controls.Add(head);
            return page;
        }

        private void UpdateDiagnosis()
        {
            if (recording == null || cmbChannel.SelectedIndex < 0) return;
            var ch = recording.Channels[cmbChannel.SelectedIndex];
            double fs = recording.SampleRate;
            if (SignalTools.IsElectrical(ch.Unit))
            {
                lblSeverity.Text = $"Canal «{ch.Name}» en {SignalTools.Symbol(ch.Unit)}: no es vibración";
                lblSeverity.ForeColor = SystemColors.ControlText;
                severityBar.BackColor = SystemColors.Control;
                lblSeverityDesc.Text = ch.Unit == VibrationUnit.CurrentA
                    ? "Es una corriente: el análisis de barras del rotor, excentricidad, armónicos y desbalance está en la pestaña 4 → «Corriente del motor (MCSA)»."
                    : "Es una tensión: si es un sensor de ultrasonido, analizalo en la pestaña 4 → «Ultrasonido».";
                lvFindings.Items.Clear();
                return;
            }
            var vel = SignalTools.Convert(ch.Data, fs, ch.Unit, VibrationUnit.VelocityMmS, 10);
            var sp = SpectrumAnalyzer.Compute(vel, fs, WindowType.Hann);
            double vrms = sp.BandRms(10, Math.Min(1000, fs / 2), sp.NoiseBandwidthBins);
            var sev = Severity.Evaluate(vrms, machine.MachineClass);
            lblSeverity.Text = $"{vrms:0.00} mm/s RMS · {sev.Label}";
            lblSeverity.ForeColor = ZoneColor(sev.Zone);
            severityBar.BackColor = ZoneColor(sev.Zone);
            lblSeverityDesc.Text = $"{sev.Description}\n{sev.Standard}: A/B {sev.Limits[0]:0.0#} · B/C {sev.Limits[1]:0.0#} · C/D {sev.Limits[2]:0.0#} mm/s (banda 10–1000 Hz, canal «{ch.Name}»).";
            double kurt = ch.Unit is VibrationUnit.AccelerationG or VibrationUnit.AccelerationMs2 ? SignalTools.Statistics(ch.Data).Kurtosis : 0;
            Spectrum envelope = null;
            if (machine.Bearing != null)
            {
                try
                {
                    double f1 = CurrentRpm() / 60;
                    var acc = SignalTools.Convert(ch.Data, fs, ch.Unit, VibrationUnit.AccelerationG, ch.Unit is VibrationUnit.AccelerationG or VibrationUnit.AccelerationMs2 ? 0 : 2.0);
                    var band = AdvancedSpectrum.AutoEnvelopeBand(acc, fs, f1, machine.Bearing.BpfiOrder * f1);
                    envelope = AdvancedSpectrum.EnvelopeSpectrum(acc, fs, band.Low, band.High);
                }
                catch (Exception ex) { LogManager.Instance.LogError("Envolvente automática: " + ex.Message); }
            }
            var findings = FaultDiagnosis.Diagnose(new DiagnosisInput
            {
                EnvelopeSpectrum = envelope,
                GearTeeth = machine.GearTeeth,
                VelocitySpectrum = sp,
                RunningSpeedHz = CurrentRpm() / 60,
                Bearing = machine.Bearing,
                Blades = machine.Blades,
                LineFrequencyHz = machine.LineFrequencyHz,
                AccelerationKurtosis = kurt,
            });
            lvFindings.Items.Clear();
            if (findings.Count == 0) lvFindings.Items.Add(new ListViewItem(new[] { "Sin patrones de falla", "", "No hay picos significativos en 1X, armónicos ni frecuencias características.", "" }));
            foreach (var f in findings)
                lvFindings.Items.Add(new ListViewItem(new[] { f.Title, $"{f.Confidence * 100:0} %", f.Evidence, f.Recommendation }));
            lastSeverity = sev;
            lastFindings = findings;
        }

        private SeverityResult lastSeverity;
        private IReadOnlyList<Finding> lastFindings = Array.Empty<Finding>();

        private static Color ZoneColor(SeverityZone z) => z switch
        {
            SeverityZone.A => Color.FromArgb(30, 150, 80),
            SeverityZone.B => Color.FromArgb(120, 160, 30),
            SeverityZone.C => Color.FromArgb(220, 140, 0),
            _ => Color.FromArgb(200, 40, 40),
        };

        // ───────────────────────────── 4. Análisis avanzado ─────────────────────────────
        private static readonly string[] AdvModes = { "Espectro promediado", "Densidad espectral (PSD)", "Zoom FFT", "Envolvente (demodulación)", "Cepstrum", "Curtosis espectral", "Corriente del motor (MCSA)", "Ultrasonido" };
        private FlowLayoutPanel pnlAdvMcsa, pnlAdvUs;
        private CheckBox chkSimBroken;
        private NumericUpDown nudUsCarrier;
        private byte[] lastUsWav;

        private TabPage BuildAnalysisTab()
        {
            advPage = new TabPage("4. Análisis avanzado");
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = true };
            cmbAdvMode = Combo(AdvModes, 190);
            cmbAdvUnit = Combo(new[] { "Aceleración (g)", "Velocidad (mm/s)", "Desplazamiento (µm)" }, 150);
            cmbAdvWindow = Combo(new[] { "Hanning", "Flat-top", "Hamming", "Blackman-Harris", "Kaiser", "Rectangular" }, 120);
            cmbAdvBlock = Combo(new[] { "512", "1024", "2048", "4096", "8192", "16384", "32768", "65536" }, 80);
            cmbAdvBlock.SelectedIndex = 3;
            cmbAdvAvg = Combo(new[] { "Lineal", "Exponencial", "Retención de picos" }, 140);
            nudAdvOverlap = Nud(0, 90, 50, 0); nudAdvOverlap.Width = 60;
            nudBandLow = Nud(1, 100000, 1000, 0); nudBandHigh = Nud(2, 100000, 2400, 0);
            nudZoomCenter = Nud(0.1m, 100000, 25, 2); nudZoomSpan = Nud(0.5m, 20000, 20, 2);
            nudAdvMaxHz = Nud(0, 100000, 0, 0);
            chkAdvLog = new CheckBox { Text = "Escala dB", AutoSize = true, Margin = new Padding(8, 8, 4, 0) };
            chkAdvMarkers = new CheckBox { Text = "Frecuencias de la máquina", AutoSize = true, Checked = true, Margin = new Padding(8, 8, 4, 0) };
            pnlAdvBlock = Group(Lbl("Bloque:"), cmbAdvBlock, Lbl("Solape %:"), nudAdvOverlap, Lbl("Promedio:"), cmbAdvAvg);
            pnlAdvBand = Group(Lbl("Banda (Hz):"), nudBandLow, Lbl("–"), nudBandHigh, Btn("Sugerir banda", (s, e) => SuggestEnvelopeBand()));
            pnlAdvZoom = Group(Lbl("Centro (Hz):"), nudZoomCenter, Lbl("Ancho (Hz):"), nudZoomSpan, Btn("Centrar en 1X", (s, e) => { nudZoomCenter.Value = Clamp(nudZoomCenter, CurrentRpm() / 60); RunAdvancedAnalysis(); }));
            chkSimBroken = new CheckBox { Text = "con barras rotas", AutoSize = true, Checked = true, Margin = new Padding(4, 8, 4, 0) };
            pnlAdvMcsa = Group(Btn("Simular corriente del motor (3 fases)", (s, e) => SimulateMotorCurrent()), chkSimBroken);
            nudUsCarrier = Nud(20000, 90000, 38000, 0); nudUsCarrier.Width = 75;
            pnlAdvUs = Group(Lbl("Portadora (Hz):"), nudUsCarrier, Btn("Escuchar (heterodino)", (s, e) => PlayUltrasound()), Btn("Guardar WAV", (s, e) => SaveUltrasoundWav()));
            top.Controls.AddRange(new Control[]
            {
                Lbl("Análisis:"), cmbAdvMode, Lbl("Magnitud:"), cmbAdvUnit, Lbl("Ventana:"), cmbAdvWindow, pnlAdvBlock, pnlAdvBand, pnlAdvZoom, pnlAdvMcsa, pnlAdvUs,
                Lbl("Hasta (Hz, 0 = auto):"), nudAdvMaxHz, chkAdvLog, chkAdvMarkers,
                Btn("Calcular", (s, e) => RunAdvancedAnalysis()),
                Btn("Exportar CSV", (s, e) => ExportAdvanced(false)),
                Btn("Exportar PNG", (s, e) => ExportAdvanced(true)),
            });
            foreach (var c in new Control[] { cmbAdvMode, cmbAdvUnit, cmbAdvWindow, cmbAdvBlock, cmbAdvAvg })
                ((ComboBox)c).SelectedIndexChanged += (s, e) => RunAdvancedAnalysis();
            foreach (var c in new[] { chkAdvLog, chkAdvMarkers }) c.CheckedChanged += (s, e) => RunAdvancedAnalysis();

            lblAdvSummary = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 48, Padding = new Padding(8, 4, 8, 4), Font = new Font("Consolas", 9.5f) };
            advPlot = new XYPlotControl { Dock = DockStyle.Fill };
            lvAdvPeaks = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            lvAdvPeaks.Columns.Add("Frecuencia", 90);
            lvAdvPeaks.Columns.Add("Valor", 90);
            lvAdvPeaks.Columns.Add("Orden", 60);
            lvAdvPeaks.Columns.Add("Identificación", 130);
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2 };
            split.Panel1.Controls.Add(advPlot);
            var peaksBox = new GroupBox { Text = "Picos principales", Dock = DockStyle.Fill };
            peaksBox.Controls.Add(lvAdvPeaks);
            split.Panel2.Controls.Add(peaksBox);
            split.HandleCreated += (s, e) => { try { split.SplitterDistance = Math.Max(200, split.Width - 400); } catch { } };
            var help = new Label
            {
                Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(8),
                Text = "Rueda: zoom · arrastrar: desplazar · doble clic: ver todo. La envolvente y la curtosis espectral usan aceleración; la banda debe contener una resonancia excitada por los impactos.",
            };
            advPage.Controls.Add(split);
            advPage.Controls.Add(help);
            advPage.Controls.Add(lblAdvSummary);
            advPage.Controls.Add(top);
            UpdateAdvancedControls();
            cmbAdvMode.SelectedIndexChanged += (s, e) => UpdateAdvancedControls();
            return advPage;
        }

        private static FlowLayoutPanel Group(params Control[] controls)
        {
            var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
            p.Controls.AddRange(controls);
            return p;
        }

        private void UpdateAdvancedControls()
        {
            int m = cmbAdvMode.SelectedIndex;
            pnlAdvBlock.Visible = m is 0 or 1;
            cmbAdvAvg.Enabled = m == 0;
            pnlAdvBand.Visible = m is 3 or 5;
            pnlAdvZoom.Visible = m == 2;
            pnlAdvMcsa.Visible = m == 6;
            pnlAdvUs.Visible = m == 7;
            cmbAdvUnit.Enabled = m is 0 or 1 or 2 or 4;
            chkAdvLog.Enabled = m is 0 or 1 or 2 or 3 or 7;
        }

        private VibrationUnit AdvUnit => cmbAdvUnit.SelectedIndex switch { 1 => VibrationUnit.VelocityMmS, 2 => VibrationUnit.DisplacementUm, _ => VibrationUnit.AccelerationG };

        private WindowType AdvWindow => cmbAdvWindow.SelectedIndex switch
        {
            1 => WindowType.FlatTop, 2 => WindowType.Hamming, 3 => WindowType.BlackmanHarris, 4 => WindowType.Kaiser, 5 => WindowType.Rectangular, _ => WindowType.Hann,
        };

        private double[] AdvSignal(VibrationUnit unit, out Channel ch)
        {
            ch = recording.Channels[cmbChannel.SelectedIndex];
            bool fromAcc = ch.Unit is VibrationUnit.AccelerationG or VibrationUnit.AccelerationMs2;
            return SignalTools.Convert(ch.Data, recording.SampleRate, ch.Unit, unit, unit == VibrationUnit.AccelerationG && fromAcc ? 0 : 2.0);
        }

        private void SuggestEnvelopeBand()
        {
            if (recording == null || cmbChannel.SelectedIndex < 0) { Warn("Adquirí primero una señal (pestaña 2)."); return; }
            try
            {
                var acc = AdvSignal(VibrationUnit.AccelerationG, out _);
                double f1 = CurrentRpm() / 60;
                double maxDefect = machine.Bearing != null ? machine.Bearing.BpfiOrder * f1 : 6 * f1;
                var (lo, hi) = AdvancedSpectrum.AutoEnvelopeBand(acc, recording.SampleRate, f1, maxDefect);
                nudBandLow.Value = Clamp(nudBandLow, lo);
                nudBandHigh.Value = Clamp(nudBandHigh, hi);
                SetStatus($"Banda de envolvente sugerida por curtosis espectral: {lo:0}–{hi:0} Hz.");
                if (cmbAdvMode.SelectedIndex != 3) cmbAdvMode.SelectedIndex = 3; else RunAdvancedAnalysis();
            }
            catch (Exception ex) { Error("No se pudo sugerir la banda", ex); }
        }

        private void RunAdvancedAnalysis()
        {
            if (advPlot == null) return;
            if (recording == null || cmbChannel.SelectedIndex < 0)
            {
                lblAdvSummary.Text = "Adquirí o abrí una señal en la pestaña 2 para analizarla.";
                return;
            }
            try
            {
                int mode = cmbAdvMode.SelectedIndex;
                double fs = recording.SampleRate, nyq = fs / 2, f1 = CurrentRpm() / 60;
                var unit = mode is 3 or 5 ? VibrationUnit.AccelerationG : AdvUnit;
                var x = AdvSignal(unit, out var ch);
                string u = SignalTools.Symbol(unit);
                var window = AdvWindow;
                int block = int.Parse(cmbAdvBlock.SelectedItem.ToString(), CultureInfo.InvariantCulture);
                while (block > x.Length && block > 64) block /= 2;
                double overlap = (double)nudAdvOverlap.Value / 100;
                var catalog = FrequencyCatalog.Build(f1, machine.Bearing, machine.Blades, machine.GearTeeth, machine.LineFrequencyHz);

                advPlot.Markers.Clear(); advPlot.Peaks.Clear(); lvAdvPeaks.Items.Clear();
                advPlot.Stems = false; advPlot.XFormat = "0.##"; advPlot.XLabel = "Frecuencia (Hz)";
                advPlot.LogY = chkAdvLog.Checked && chkAdvLog.Enabled;
                advPlot.Title = $"{AdvModes[mode]} · canal «{ch.Name}» · {f1 * 60:0} rpm";

                if (mode == 4) { ShowCepstrum(x, fs, f1, window); return; }
                if (mode == 6) { ShowMcsa(); return; }
                if (mode == 7) { ShowUltrasound(); return; }
                if (mode == 5) { ShowSpectralKurtosis(x, fs, f1); return; }

                Spectrum sp;
                string summary;
                double maxShow;
                string[] groups;
                switch (mode)
                {
                    case 1:
                    {
                        var psd = AdvancedSpectrum.Psd(x, fs, block, overlap, window);
                        sp = new Spectrum { Frequencies = psd.Frequencies, Amplitudes = psd.Psd, PhasesDeg = new double[psd.Psd.Length], Resolution = psd.Resolution, SampleRate = fs, Window = window, Averages = psd.Averages, Kind = "PSD" };
                        advPlot.YLabel = $"PSD ({u}²/Hz)";
                        double hi = nudAdvMaxHz.Value > 0 ? (double)nudAdvMaxHz.Value : nyq;
                        summary = $"PSD (Welch): {psd.Averages} promedios, bloque {block}, Δf {psd.Resolution:0.###} Hz.  RMS 0–{hi:0} Hz = {psd.BandRms(0, hi):0.####} {u}";
                        maxShow = Math.Min(nyq, Math.Max(1000, 40 * f1));
                        groups = new[] { "Giro", "Rodamiento", "Engranaje", "Álabes", "Eléctrico" };
                        break;
                    }
                    case 2:
                    {
                        double center = (double)nudZoomCenter.Value, span = (double)nudZoomSpan.Value;
                        span = Math.Min(span, 2 * Math.Min(center, nyq - center) - 1e-6);
                        sp = AdvancedSpectrum.Zoom(x, fs, center, span, window);
                        advPlot.YLabel = $"Amplitud ({u} pico)";
                        summary = $"{sp.Kind}: {sp.Frequencies.Length} líneas, Δf {sp.Resolution:0.####} Hz (registro de {recording.Duration:0.0} s).";
                        maxShow = 0;
                        groups = new[] { "Subarmónicos", "Giro", "Rodamiento", "Engranaje", "Álabes", "Eléctrico" };
                        break;
                    }
                    case 3:
                    {
                        double lo = (double)nudBandLow.Value, hi = Math.Min((double)nudBandHigh.Value, nyq * 0.95);
                        if (hi <= lo) { Warn($"La banda de envolvente debe estar entre 0 y {nyq * 0.95:0} Hz con inferior < superior."); return; }
                        sp = AdvancedSpectrum.EnvelopeSpectrum(x, fs, lo, hi, window);
                        advPlot.YLabel = $"Envolvente ({u} pico)";
                        double maxDefect = machine.Bearing != null ? machine.Bearing.BpfiOrder * f1 : 10 * f1;
                        maxShow = Math.Min(nyq, Math.Max(500, 4.5 * maxDefect));
                        summary = $"Envolvente en {lo:0}–{hi:0} Hz, Δf {sp.Resolution:0.###} Hz." + BearingEnvelopeSummary(sp, f1);
                        groups = new[] { "Giro", "Rodamiento" };
                        break;
                    }
                    default:
                    {
                        sp = AdvancedSpectrum.Averaged(x, fs, block, overlap, window, (AveragingMode)Math.Max(0, cmbAdvAvg.SelectedIndex));
                        advPlot.YLabel = $"Amplitud ({u} pico)";
                        summary = $"{sp.Kind}: {sp.Averages} promedios, bloque {block} ({block / fs:0.###} s), Δf {sp.Resolution:0.###} Hz, ventana {WindowFunctions.DisplayName(window)}.";
                        maxShow = Math.Min(nyq, Math.Max(1000, 40 * f1));
                        groups = new[] { "Subarmónicos", "Giro", "Rodamiento", "Engranaje", "Álabes", "Eléctrico" };
                        break;
                    }
                }
                if (nudAdvMaxHz.Value > 0 && mode != 2) maxShow = Math.Min(nyq, (double)nudAdvMaxHz.Value);
                advPlot.DbReference = mode == 1 ? 1e-6 : unit == VibrationUnit.AccelerationG ? 1e-3 : 1e-2;
                advPlot.SetData(sp.Frequencies, sp.Amplitudes);
                if (maxShow > 0) advPlot.SetView(sp.Frequencies[0], maxShow);
                double view = maxShow > 0 ? maxShow : sp.Frequencies[^1];
                if (chkAdvMarkers.Checked) AddMarkers(catalog.Where(m => groups.Contains(m.Group)), sp.Frequencies[0], view, v => v);
                double minF = mode == 2 ? 0 : Math.Max(sp.Resolution * 2, Math.Min(f1 * 0.3, 2));
                var trimmed = new Spectrum { Frequencies = sp.Frequencies.Where(f => f <= view).ToArray(), Amplitudes = sp.Amplitudes.Take(sp.Frequencies.Count(f => f <= view)).ToArray(), PhasesDeg = new double[0], Resolution = sp.Resolution, SampleRate = fs, Window = window };
                var peaks = SpectralFeatures.Peaks(trimmed, 15, minF, 0.02, f1);
                double tol = Math.Max(sp.Resolution * 2, f1 * 0.02);
                foreach (var p in peaks)
                {
                    string id = FrequencyCatalog.Identify(p.Frequency, catalog, tol);
                    advPlot.Peaks.Add(new XYPlotControl.PeakLabel(p.Frequency, p.Amplitude, id));
                    lvAdvPeaks.Items.Add(new ListViewItem(new[] { p.Frequency.ToString("0.###", Es) + " Hz", FormatValue(p.Amplitude), p.Order > 0 ? p.Order.ToString("0.00", Es) + "X" : "", id }));
                }
                lblAdvSummary.Text = summary;
                advPlot.Invalidate();
            }
            catch (Exception ex)
            {
                lblAdvSummary.Text = "No se pudo calcular: " + ex.Message;
                LogManager.Instance.LogError("Análisis avanzado: " + ex);
            }
        }

        // ───────── Corriente del motor (MCSA) y ultrasonido ─────────
        private void ShowMcsa()
        {
            var ch = recording.Channels[cmbChannel.SelectedIndex];
            if (ch.Unit != VibrationUnit.CurrentA)
            {
                lblAdvSummary.Text = "Elegí un canal de corriente (A): de la placa/DAQ con tipo «Pinza de corriente», de un CSV con unidad A, o tocá «Simular corriente del motor».";
                advPlot.SetData(Array.Empty<double>(), Array.Empty<double>());
                return;
            }
            double fs = recording.SampleRate;
            double rpm = recording.HasPhaseReference ? recording.MeasuredRpm() : machine.Rpm;
            var m = MotorCurrentAnalysis.Analyze(ch.Data, fs, machine.LineFrequencyHz > 0 ? machine.LineFrequencyHz : 50, machine.Poles, rpm);
            var z = m.Zoom;
            advPlot.XLabel = "Frecuencia (Hz)"; advPlot.YLabel = "Corriente"; advPlot.XFormat = "0.##";
            advPlot.LogY = true; advPlot.DbReference = Math.Max(m.LineAmplitude, 1e-9);
            advPlot.Title = $"MCSA · «{ch.Name}» · {machine.Poles} polos";
            advPlot.SetData(z.Frequencies, z.Amplitudes);
            advPlot.Markers.Add(new XYPlotControl.Marker(m.LineHz, $"red {m.LineHz:0.###}", Color.FromArgb(200, 150, 0)));
            if (!double.IsNaN(m.Slip))
            {
                double sb = 2 * m.Slip * m.LineHz;
                advPlot.Markers.Add(new XYPlotControl.Marker(m.LineHz - sb, "f(1−2s)", Color.FromArgb(200, 40, 40)));
                advPlot.Markers.Add(new XYPlotControl.Marker(m.LineHz + sb, "f(1+2s)", Color.FromArgb(200, 40, 40)));
            }
            var others = recording.Channels.Where(c => c.Unit == VibrationUnit.CurrentA).Select(c => SignalTools.Statistics(c.Data).Rms).ToArray();
            string unb = others.Length >= 3 ? $" · desbalance {MotorCurrentAnalysis.Unbalance(others.Take(3).ToArray()):0.0} %" : "";
            void Row(string a, string b, string c = "", string d = "") => lvAdvPeaks.Items.Add(new ListViewItem(new[] { a, b, c, d }));
            Row("Red", $"{m.LineHz:0.###} Hz", $"{m.LineAmplitude:0.###} A pico");
            Row("Velocidad", double.IsNaN(m.Slip) ? "—" : $"{m.Rpm:0} rpm", double.IsNaN(m.Slip) ? "" : $"s = {m.Slip * 100:0.00} %");
            Row("Barras (banda inf.)", double.IsFinite(m.LowerSidebandDb) ? $"{m.LowerSidebandDb:0.0} dB" : "—", "", m.RotorSeverity >= 0 ? $"nivel {m.RotorSeverity}/5" : "");
            Row("Barras (banda sup.)", double.IsFinite(m.UpperSidebandDb) ? $"{m.UpperSidebandDb:0.0} dB" : "—");
            Row("Excentricidad", double.IsFinite(m.EccentricityDb) ? $"{m.EccentricityDb:0.0} dB" : "—");
            Row("THD", $"{m.ThdPercent:0.0} %");
            foreach (var (o, p) in m.Harmonics.Where(h => h.Percent >= 0.5)) Row($"Armónico {o}", $"{p:0.0} %");
            lblAdvSummary.Text = $"Rotor: {m.RotorCondition} (banda inferior a {(double.IsFinite(m.LowerSidebandDb) ? m.LowerSidebandDb.ToString("0.0", Es) + " dB" : "—")}) · THD {m.ThdPercent:0.0} %{unb}
" +
                                 (m.Notes.Count > 0 ? string.Join(" ", m.Notes) : $"Resolución {z.Resolution:0.###} Hz con {recording.Duration:0.#} s de registro. Escala en dB respecto de la línea.");
            advPlot.Invalidate();
        }

        private void SimulateMotorCurrent()
        {
            double fs = 5000, secs = 20, line = machine.LineFrequencyHz > 0 ? machine.LineFrequencyHz : 50;
            double ns = 120 * line / machine.Poles;
            double rpm = machine.Rpm > 0 && machine.Rpm < ns ? machine.Rpm : ns * 0.97;
            double bb = chkSimBroken.Checked ? 38 : 0;
            var chans = new List<Channel>
            {
                new Channel("Corriente R", VibrationUnit.CurrentA, MotorCurrentAnalysis.Simulate(fs, secs, line, 14, machine.Poles, rpm, bb, 48, 3, 0.01, 0, 1)),
                new Channel("Corriente S", VibrationUnit.CurrentA, MotorCurrentAnalysis.Simulate(fs, secs, line, 14.6, machine.Poles, rpm, bb, 48, 3, 0.01, -120, 2)),
                new Channel("Corriente T", VibrationUnit.CurrentA, MotorCurrentAnalysis.Simulate(fs, secs, line, 13.7, machine.Poles, rpm, bb, 48, 3, 0.01, 120, 3)),
            };
            cmbSource.SelectedIndex = (int)Source.File;
            SetRecording(new Recording(fs, chans, null, rpm, $"Motor simulado ({machine.Poles} polos, {rpm:0} rpm{(bb > 0 ? ", barras rotas" : "")})"));
            if (cmbAdvMode.SelectedIndex != 6) cmbAdvMode.SelectedIndex = 6; else RunAdvancedAnalysis();
            SetStatus("Corriente de motor simulada (3 fases, 20 s a 5 kHz) cargada como señal actual.");
        }

        private void ShowUltrasound()
        {
            var ch = recording.Channels[cmbChannel.SelectedIndex];
            double fs = recording.SampleRate;
            if (fs < 80000)
            {
                lblAdvSummary.Text = $"El ultrasonido necesita muestrear a 96 kHz o más (la señal actual está a {fs:0} Hz). Usá la placa de sonido a 192 kHz o un DAQ con un sensor de ultrasonido.";
                advPlot.SetData(Array.Empty<double>(), Array.Empty<double>());
                lastUsWav = null;
                return;
            }
            var u = UltrasoundAnalysis.Analyze(ch.Data, fs);
            var sp = AdvancedSpectrum.Averaged(ch.Data, fs, 8192, 0.5, WindowType.Hann);
            advPlot.XLabel = "Frecuencia (Hz)"; advPlot.YLabel = "Amplitud (V)"; advPlot.XFormat = "0";
            advPlot.LogY = chkAdvLog.Checked; advPlot.DbReference = 1e-6;
            advPlot.Title = $"Ultrasonido · «{ch.Name}»";
            advPlot.SetData(sp.Frequencies, sp.Amplitudes);
            advPlot.SetView(Math.Max(0, u.BandLow - 5000), fs / 2);
            advPlot.Markers.Add(new XYPlotControl.Marker(u.BandLow, "banda", Color.FromArgb(200, 70, 20)));
            advPlot.Markers.Add(new XYPlotControl.Marker(u.BandHigh, "", Color.FromArgb(200, 70, 20)));
            advPlot.Markers.Add(new XYPlotControl.Marker((double)nudUsCarrier.Value, "portadora", Color.FromArgb(40, 110, 200)));
            lvAdvPeaks.Items.Add(new ListViewItem(new[] { "RMS", $"{u.RmsDbuV:0.0} dBµV", "", $"{u.RmsVolts * 1000:0.###} mV" }));
            lvAdvPeaks.Items.Add(new ListViewItem(new[] { "Pico", $"{u.PeakDbuV:0.0} dBµV" }));
            lvAdvPeaks.Items.Add(new ListViewItem(new[] { "Factor de cresta", u.CrestFactor.ToString("0.00", Es) }));
            lastUsWav = WavWriter.Mono16(UltrasoundAnalysis.Heterodyne(ch.Data, fs, (double)nudUsCarrier.Value, 8000), 8000);
            lblAdvSummary.Text = $"Ultrasonido {u.BandLow / 1000:0}–{u.BandHigh / 1000:0} kHz: {u.RmsDbuV:0.0} dBµV RMS, pico {u.PeakDbuV:0.0} dBµV, cresta {u.CrestFactor:0.0}.
" +
                                 "Guardalo en Tendencias y compará con la línea base: +8 dB falta lubricación · +12 falla incipiente · +16 falla · +35 catastrófica.";
            advPlot.Invalidate();
        }

        private void PlayUltrasound()
        {
            if (lastUsWav == null) { Warn("Calculá primero el análisis de ultrasonido (señal muestreada a 96 kHz o más)."); return; }
            try { using var ms = new MemoryStream(lastUsWav); using var player = new System.Media.SoundPlayer(ms); player.Play(); }
            catch (Exception ex) { Error("No se pudo reproducir", ex); }
        }

        private void SaveUltrasoundWav()
        {
            if (lastUsWav == null) { Warn("Calculá primero el análisis de ultrasonido."); return; }
            using var sfd = new SaveFileDialog { Filter = "WAV|*.wav", FileName = $"ultrasonido_{DateTime.Now:yyyyMMdd_HHmmss}.wav" };
            if (sfd.ShowDialog(this) == DialogResult.OK) File.WriteAllBytes(sfd.FileName, lastUsWav);
        }

        private string BearingEnvelopeSummary(Spectrum env, double f1)
        {
            if (machine.Bearing == null) return "  (cargá la geometría del rodamiento en la pestaña 1 para identificar BPFO/BPFI/BSF/FTF)";
            var b = machine.Bearing;
            var parts = new List<string>();
            foreach (var (name, order) in new[] { ("BPFO", b.BpfoOrder), ("BPFI", b.BpfiOrder), ("BSF", b.BsfOrder), ("FTF", b.FtfOrder) })
            {
                var (cnt, _, snr) = SpectralFeatures.DefectHarmonics(env, order * f1);
                parts.Add($"{name} {order * f1:0.0} Hz: {snr:0.0}× piso, {cnt} arm.");
            }
            return "\n" + string.Join(" · ", parts);
        }

        private void ShowCepstrum(double[] x, double fs, double f1, WindowType window)
        {
            var c = AdvancedSpectrum.Cepstrum(x, fs, window);
            double qMax = Math.Min(c.Quefrency[^1], Math.Max(0.1, 3.5 / Math.Max(f1, 0.5)));
            if (nudAdvMaxHz.Value > 0) qMax = Math.Min(c.Quefrency[^1], 1.0 / (double)nudAdvMaxHz.Value * 1000);
            int skip = Math.Max(1, (int)(0.001 * fs));
            advPlot.XLabel = "Quefrencia (ms)"; advPlot.YLabel = "Cepstrum real"; advPlot.XFormat = "0.#"; advPlot.LogY = false;
            var qs = c.Quefrency.Skip(skip).Select(q => q * 1000).ToArray();
            advPlot.SetData(qs, c.Values.Skip(skip).ToArray());
            advPlot.SetView(qs[0], qMax * 1000);
            var fams = new List<(string Name, double Hz)> { ("1X", f1) };
            if (machine.Bearing != null)
            {
                var b = machine.Bearing;
                fams.AddRange(new[] { ("BPFO", b.BpfoOrder * f1), ("BPFI", b.BpfiOrder * f1), ("BSF", b.BsfOrder * f1), ("FTF", b.FtfOrder * f1) });
            }
            if (machine.LineFrequencyHz > 0) fams.Add(("Red", machine.LineFrequencyHz));
            if (chkAdvMarkers.Checked)
                foreach (var (name, hz) in fams)
                    if (hz > 0) advPlot.Markers.Add(new XYPlotControl.Marker(1000 / hz, name, MarkerColor(name == "1X" ? "Giro" : name == "Red" ? "Eléctrico" : "Rodamiento")));
            var dom = c.Dominant(skip / fs, qMax);
            double domHz = dom.Quefrency > 0 ? 1 / dom.Quefrency : 0;
            advPlot.Peaks.Add(new XYPlotControl.PeakLabel(dom.Quefrency * 1000, dom.Value, $"{domHz:0.##} Hz"));
            foreach (var (name, hz) in fams)
            {
                if (hz <= 0) continue;
                double prom = c.FamilyProminence(1 / hz);
                lvAdvPeaks.Items.Add(new ListViewItem(new[] { $"{1000 / hz:0.##} ms", prom.ToString("0.0", Es), (hz / f1).ToString("0.00", Es) + "X", $"Familia {name} ({hz:0.##} Hz){(prom > 3 ? " ← presente" : "")}" }));
            }
            string ident = FrequencyCatalog.Identify(domHz, FrequencyCatalog.Build(f1, machine.Bearing, machine.Blades, machine.GearTeeth, machine.LineFrequencyHz), Math.Max(domHz * 0.03, 0.2));
            lblAdvSummary.Text = $"Rahmónico dominante: τ = {dom.Quefrency * 1000:0.##} ms → familia de armónicos/bandas laterales separadas {domHz:0.##} Hz ({domHz / Math.Max(f1, 1e-9):0.00}X){(ident != "" ? " = " + ident : "")}.\n" +
                                 "En la tabla, «Valor» es la prominencia de cada familia (> 3 = familia clara).";
            advPlot.Invalidate();
        }

        private void ShowSpectralKurtosis(double[] x, double fs, double f1)
        {
            var sk = AdvancedSpectrum.SpectralKurtosis(x, fs);
            advPlot.YLabel = "Curtosis espectral"; advPlot.LogY = false;
            advPlot.SetData(sk.Frequencies, sk.Kurtosis);
            double maxDefect = machine.Bearing != null ? machine.Bearing.BpfiOrder * f1 : 6 * f1;
            var (lo, hi) = AdvancedSpectrum.AutoEnvelopeBand(x, fs, f1, maxDefect);
            advPlot.Markers.Add(new XYPlotControl.Marker(lo, "inicio", Color.FromArgb(200, 70, 20)));
            advPlot.Markers.Add(new XYPlotControl.Marker(hi, "fin", Color.FromArgb(200, 70, 20)));
            foreach (var p in SpectralFeatures.Peaks(new Spectrum { Frequencies = sk.Frequencies, Amplitudes = sk.Kurtosis, PhasesDeg = new double[0], Resolution = sk.Frequencies[1], SampleRate = fs }, 8, f1 * 5, 0.05))
                lvAdvPeaks.Items.Add(new ListViewItem(new[] { p.Frequency.ToString("0", Es) + " Hz", p.Amplitude.ToString("0.00", Es), "", p.Frequency >= lo && p.Frequency <= hi ? "banda sugerida" : "" }));
            lblAdvSummary.Text = $"Curtosis espectral ({sk.Segments} bloques). Valores altos = bandas con impulsos (rodamientos, engranajes).\n" +
                                 $"Banda sugerida para la envolvente: {lo:0}–{hi:0} Hz  (botón «Sugerir banda» la aplica).";
            advPlot.Invalidate();
        }

        private void AddMarkers(IEnumerable<FrequencyMarker> markers, double min, double max, Func<double, double> map)
        {
            foreach (var m in markers)
                if (m.Frequency >= min && m.Frequency <= max)
                    advPlot.Markers.Add(new XYPlotControl.Marker(map(m.Frequency), m.Label, MarkerColor(m.Group)));
        }

        private static Color MarkerColor(string group) => group switch
        {
            "Rodamiento" => Color.FromArgb(190, 60, 160),
            "Engranaje" => Color.FromArgb(40, 110, 200),
            "Eléctrico" => Color.FromArgb(200, 150, 0),
            "Álabes" => Color.FromArgb(30, 150, 80),
            "Subarmónicos" => Color.FromArgb(150, 150, 150),
            _ => Color.FromArgb(120, 120, 120),
        };

        private static string FormatValue(double v)
        {
            double a = Math.Abs(v);
            return a == 0 ? "0" : a >= 0.01 ? v.ToString("0.####", Es) : v.ToString("0.###E+0", Es);
        }

        private void ExportAdvanced(bool png)
        {
            if (advPlot.XValues.Length == 0) { Warn("Calculá primero un análisis."); return; }
            string name = $"{Safe(AdvModes[cmbAdvMode.SelectedIndex])}_{DateTime.Now:yyyyMMdd_HHmmss}";
            using var sfd = new SaveFileDialog { Filter = png ? "Imagen PNG|*.png" : "CSV|*.csv", FileName = name + (png ? ".png" : ".csv") };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                if (png) advPlot.ExportPng(sfd.FileName); else advPlot.ExportCsv(sfd.FileName);
                SetStatus("Exportado: " + sfd.FileName);
            }
            catch (Exception ex) { Error("No se pudo exportar", ex); }
        }

        // ───────────────────────────── 5. Órdenes y transitorios ─────────────────────────────
        private static readonly string[] TrModes = { "Espectro de órdenes", "Bode (seguimiento de orden)", "Nyquist (polar)", "Cascada (waterfall)", "Espectrograma", "Órbita (X-Y)" };

        private TabPage BuildTransientTab()
        {
            trPage = new TabPage("5. Órdenes y transitorios");
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = true };
            cmbTrMode = Combo(TrModes, 200);
            cmbTrUnit = Combo(new[] { "Velocidad (mm/s)", "Aceleración (g)", "Desplazamiento (µm)" }, 150);
            cmbTrBlock = Combo(new[] { "512", "1024", "2048", "4096", "8192", "16384" }, 80);
            cmbTrBlock.SelectedIndex = 2;
            cmbTrY = Combo(new string[0], 130);
            nudTrOrder = Nud(0.5m, 50, 1, 1); nudTrOrder.Width = 60; nudTrOrder.Increment = 0.5m;
            nudTrMaxOrder = Nud(2, 200, 20, 0); nudTrMaxOrder.Width = 60;
            nudTrRevs = Nud(1, 64, 4, 0); nudTrRevs.Width = 55;
            nudTrMaxHz = Nud(0, 100000, 0, 0); nudTrMaxHz.Width = 80;
            chkTrFilter = new CheckBox { Text = "Filtrar 1X", AutoSize = true, Checked = true, Margin = new Padding(8, 8, 4, 0) };
            top.Controls.AddRange(new Control[]
            {
                Lbl("Análisis:"), cmbTrMode, Lbl("Magnitud:"), cmbTrUnit,
                Group(Lbl("Orden:"), nudTrOrder, Lbl("Vueltas/bloque:"), nudTrRevs),
                Group(Lbl("Máx. orden:"), nudTrMaxOrder),
                Group(Lbl("Bloque:"), cmbTrBlock, Lbl("Hasta (Hz, 0 = auto):"), nudTrMaxHz),
                Group(Lbl("Canal Y:"), cmbTrY, chkTrFilter),
                Btn("Calcular", (s, e) => RunTransientAnalysis()),
                Btn("Exportar CSV", (s, e) => ExportTransient(false)),
                Btn("Exportar PNG", (s, e) => ExportTransient(true)),
            });
            foreach (var c in new[] { cmbTrMode, cmbTrUnit, cmbTrBlock, cmbTrY }) c.SelectedIndexChanged += (s, e) => RunTransientAnalysis();
            chkTrFilter.CheckedChanged += (s, e) => RunTransientAnalysis();
            cmbTrMode.SelectedIndexChanged += (s, e) => UpdateTransientControls();

            lblTrSummary = new Label { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 4, 8, 4), Font = new Font("Consolas", 9.5f) };
            trHost = new Panel { Dock = DockStyle.Fill };
            trOrderPlot = new XYPlotControl { Dock = DockStyle.Fill, XLabel = "Orden (× velocidad de giro)", XFormat = "0.##" };
            trBodeAmp = new XYPlotControl { Dock = DockStyle.Fill, XLabel = "Velocidad (rpm)", XFormat = "0" };
            trBodePhase = new XYPlotControl { Dock = DockStyle.Fill, XLabel = "Velocidad (rpm)", YLabel = "Retraso de fase (°)", XFormat = "0" };
            var bode = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            bode.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); bode.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            bode.Controls.Add(trBodeAmp, 0, 0); bode.Controls.Add(trBodePhase, 0, 1);
            trNyquist = new PathPlotControl { Dock = DockStyle.Fill, PolarGrid = true };
            trOrbit = new PathPlotControl { Dock = DockStyle.Fill, XLabel = "X", YLabel = "Y" };
            trWaterfall = new WaterfallControl { Dock = DockStyle.Fill };
            trHeatmap = new HeatmapControl { Dock = DockStyle.Fill };
            foreach (var c in new Control[] { trOrderPlot, bode, trNyquist, trWaterfall, trHeatmap, trOrbit }) { c.Visible = false; trHost.Controls.Add(c); }

            lvTr = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2 };
            split.Panel1.Controls.Add(trHost);
            var box = new GroupBox { Text = "Resultados", Dock = DockStyle.Fill };
            box.Controls.Add(lvTr);
            split.Panel2.Controls.Add(box);
            split.HandleCreated += (s, e) => { try { split.SplitterDistance = Math.Max(200, split.Width - 420); } catch { } };
            var help = new Label
            {
                Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(8),
                Text = "Órdenes, Bode, Nyquist y órbita necesitan tacómetro. Para un arranque en el simulador: «Arranque / parada» → Simular = Sí y 65536 muestras o más.",
            };
            trPage.Controls.Add(split);
            trPage.Controls.Add(help);
            trPage.Controls.Add(lblTrSummary);
            trPage.Controls.Add(top);
            UpdateTransientControls();
            return trPage;
        }

        private void UpdateTransientControls()
        {
            int m = cmbTrMode.SelectedIndex;
            nudTrOrder.Parent.Visible = m is 1 or 2;
            nudTrMaxOrder.Parent.Visible = m == 0;
            cmbTrBlock.Parent.Visible = m is 3 or 4;
            cmbTrY.Parent.Visible = m == 5;
        }

        private void ShowTrChart(Control c)
        {
            foreach (Control x in trHost.Controls) x.Visible = x == c;
        }

        private VibrationUnit TrUnit => cmbTrUnit.SelectedIndex switch { 1 => VibrationUnit.AccelerationG, 2 => VibrationUnit.DisplacementUm, _ => VibrationUnit.VelocityMmS };

        private double[] TrSignal(int channel, VibrationUnit unit)
        {
            var ch = recording.Channels[channel];
            bool fromAcc = ch.Unit is VibrationUnit.AccelerationG or VibrationUnit.AccelerationMs2;
            return SignalTools.Convert(ch.Data, recording.SampleRate, ch.Unit, unit, unit == VibrationUnit.AccelerationG && fromAcc ? 0 : 2.0);
        }

        private void RunTransientAnalysis()
        {
            if (trHost == null) return;
            if (recording == null || cmbChannel.SelectedIndex < 0) { lblTrSummary.Text = "Adquirí o abrí una señal en la pestaña 2."; return; }
            // Lista de canales para Y (órbita)
            if (cmbTrY.Items.Count != recording.Channels.Count)
            {
                cmbTrY.Items.Clear();
                foreach (var c in recording.Channels) cmbTrY.Items.Add(c.Name);
                if (cmbTrY.Items.Count > 0) cmbTrY.SelectedIndex = Math.Min(1, cmbTrY.Items.Count - 1);
            }
            lvTr.Items.Clear(); lvTr.Columns.Clear();
            try
            {
                int mode = cmbTrMode.SelectedIndex, chi = cmbChannel.SelectedIndex;
                double fs = recording.SampleRate;
                var unit = TrUnit;
                string u = SignalTools.Symbol(unit);
                var x = TrSignal(chi, unit);
                var pulses = recording.Tach is { Length: > 0 } ? Tachometer.DetectPulses(recording.Tach, fs) : Array.Empty<double>();
                bool needsTach = mode is 0 or 1 or 2 or 5;
                if (needsTach && pulses.Length < 3)
                {
                    lblTrSummary.Text = "Este análisis necesita la señal del tacómetro (al menos 3 pulsos). Usá el simulador, un CSV con columna de tacómetro o el sensor con tacómetro óptico.";
                    ShowTrChart(null);
                    return;
                }
                string chName = recording.Channels[chi].Name;
                var sb = new StringBuilder();
                switch (mode)
                {
                    case 0:
                    {
                        var os = OrderTracking.OrderSpectrum(x, fs, pulses, (double)nudTrMaxOrder.Value);
                        trOrderPlot.Title = $"{os.Kind} · «{chName}»"; trOrderPlot.YLabel = $"Amplitud ({u} pico)";
                        trOrderPlot.Markers.Clear(); trOrderPlot.Peaks.Clear();
                        trOrderPlot.SetData(os.Frequencies, os.Amplitudes);
                        AddColumns(lvTr, ("Orden", 70), ("Amplitud", 90), ("Identificación", 140));
                        double f1m = recording.MeasuredRpm() / 60;
                        var catalog = FrequencyCatalog.Build(1, machine.Bearing, machine.Blades, machine.GearTeeth, f1m > 0 ? machine.LineFrequencyHz / f1m : 0);
                        foreach (var p in SpectralFeatures.Peaks(os, 15, 0.3, 0.02))
                        {
                            string id = FrequencyCatalog.Identify(p.Frequency, catalog, Math.Max(os.Resolution * 2, 0.03));
                            trOrderPlot.Peaks.Add(new XYPlotControl.PeakLabel(p.Frequency, p.Amplitude, id));
                            lvTr.Items.Add(new ListViewItem(new[] { p.Frequency.ToString("0.###", Es), FormatValue(p.Amplitude), id }));
                        }
                        sb.AppendLine("orden;amplitud");
                        for (int i = 0; i < os.Frequencies.Length; i++) sb.AppendLine(Inv(os.Frequencies[i]) + ";" + Inv(os.Amplitudes[i]));
                        lblTrSummary.Text = $"Espectro de órdenes: las componentes síncronas quedan en una sola línea aunque la velocidad varíe. Resolución {os.Resolution:0.####} órdenes.\n" +
                                            $"Velocidad: {pulses.Length - 1} vueltas, {OrderTracking.InstantRpm(pulses).Rpm.DefaultIfEmpty(0).Min():0}–{OrderTracking.InstantRpm(pulses).Rpm.DefaultIfEmpty(0).Max():0} rpm.";
                        trOrderPlot.Invalidate();
                        ShowTrChart(trOrderPlot);
                        break;
                    }
                    case 1:
                    case 2:
                    {
                        double order = (double)nudTrOrder.Value;
                        int revs = (int)nudTrRevs.Value;
                        if (Math.Abs(order * revs - Math.Round(order * revs)) > 1e-6) revs = (int)Math.Ceiling(1 / (order - Math.Floor(order))) * revs;
                        var tr = OrderTracking.Track(x, fs, pulses, order, revs);
                        var pts = tr.ByRpm();
                        if (pts.Count < 2) { lblTrSummary.Text = "Muy pocas vueltas para el seguimiento: usá un registro más largo o menos vueltas por bloque."; return; }
                        var crit = order == 1 ? OrderTracking.CriticalSpeeds(tr) : new List<CriticalSpeed>();
                        string ord = order.ToString("0.#", Es) + "X";
                        if (mode == 1)
                        {
                            var ph = OrderTracking.Unwrap(pts.Select(p => p.PhaseLagDeg).ToArray());
                            double off = Math.Floor(ph.Min() / 360) * 360;
                            trBodeAmp.Title = $"Bode {ord} · «{chName}»"; trBodeAmp.YLabel = $"Amplitud {ord} ({u} pico)";
                            trBodeAmp.Markers.Clear(); trBodePhase.Markers.Clear(); trBodeAmp.Peaks.Clear();
                            trBodeAmp.SetData(pts.Select(p => p.Rpm).ToArray(), pts.Select(p => p.Amplitude).ToArray());
                            trBodePhase.SetData(pts.Select(p => p.Rpm).ToArray(), ph.Select(v => v - off).ToArray());
                            foreach (var c in crit)
                            {
                                trBodeAmp.Markers.Add(new XYPlotControl.Marker(c.Rpm, $"crítica {c.Rpm:0}", Color.FromArgb(200, 40, 40)));
                                trBodePhase.Markers.Add(new XYPlotControl.Marker(c.Rpm, "", Color.FromArgb(200, 40, 40)));
                            }
                            trBodeAmp.Invalidate(); trBodePhase.Invalidate();
                            ShowTrChart(trBodeAmp.Parent);
                        }
                        else
                        {
                            trNyquist.Title = $"Nyquist {ord} · «{chName}» (0° arriba, sentido horario = retraso)";
                            trNyquist.Labels.Clear(); trNyquist.Dots.Clear();
                            trNyquist.SetData(pts.Select(p => p.Amplitude * Math.Sin(p.PhaseLagDeg * Math.PI / 180)).ToArray(),
                                              pts.Select(p => p.Amplitude * Math.Cos(p.PhaseLagDeg * Math.PI / 180)).ToArray());
                            int every = Math.Max(1, pts.Count / 8);
                            for (int i = 0; i < pts.Count; i += every) trNyquist.Labels.Add(new PathPlotControl.PointLabel(i, $"{pts[i].Rpm:0}"));
                            foreach (var c in crit)
                            {
                                int idx = Enumerable.Range(0, pts.Count).OrderBy(i => Math.Abs(pts[i].Rpm - c.Rpm)).First();
                                trNyquist.Labels.Add(new PathPlotControl.PointLabel(idx, $"crítica {c.Rpm:0} rpm"));
                            }
                            trNyquist.Invalidate();
                            ShowTrChart(trNyquist);
                        }
                        AddColumns(lvTr, ("rpm", 70), ("Amplitud", 85), ("Fase (°)", 70), ("Tiempo (s)", 75));
                        foreach (var c in crit)
                            lvTr.Items.Add(new ListViewItem(new[] { $"{c.Rpm:0}", FormatValue(c.Amplitude), $"Δ{c.PhaseShiftDeg:0}°", $"CRÍTICA · Q = {c.AmplificationFactor:0.0}" }) { ForeColor = Color.FromArgb(200, 40, 40) });
                        int stepRows = Math.Max(1, pts.Count / 60);
                        for (int i = 0; i < pts.Count; i += stepRows)
                            lvTr.Items.Add(new ListViewItem(new[] { $"{pts[i].Rpm:0}", FormatValue(pts[i].Amplitude), $"{pts[i].PhaseLagDeg:0}", $"{pts[i].TimeS:0.00}" }));
                        sb.AppendLine("tiempo_s;rpm;amplitud;fase_grados");
                        foreach (var p in tr.Points) sb.AppendLine($"{Inv(p.TimeS)};{Inv(p.Rpm)};{Inv(p.Amplitude)};{Inv(p.PhaseLagDeg)}");
                        string critText = crit.Count == 0 ? (order == 1 ? "No se detectaron velocidades críticas en el barrido." : "")
                            : "Velocidades críticas: " + string.Join(" · ", crit.Select(c => $"{c.Rpm:0} rpm (Q {c.AmplificationFactor:0.0}, giro de fase {c.PhaseShiftDeg:0}°)"));
                        string margin = crit.Count > 0 ? $"  Margen respecto de {machine.Rpm:0} rpm: {crit.Min(c => Math.Abs(machine.Rpm - c.Rpm) / c.Rpm * 100):0} % (API 684 pide ≥ 15–26 % según Q)." : "";
                        lblTrSummary.Text = $"Seguimiento de {ord}: {tr.Points.Count} bloques de {revs} vueltas, {pts[0].Rpm:0}–{pts[^1].Rpm:0} rpm.\n{critText}{margin}";
                        break;
                    }
                    case 3:
                    case 4:
                    {
                        int block = int.Parse(cmbTrBlock.SelectedItem.ToString(), CultureInfo.InvariantCulture);
                        while (block > x.Length / 2 && block > 256) block /= 2;
                        double rpmNow = recording.MeasuredRpm();
                        double maxHz = nudTrMaxHz.Value > 0 ? (double)nudTrMaxHz.Value : Math.Min(fs / 2, Math.Max(200, 12 * Math.Max(rpmNow, (pulses.Length > 2 ? OrderTracking.InstantRpm(pulses).Rpm.Max() : 0)) / 60));
                        bool byRpm = pulses.Length > 2;
                        if (mode == 3)
                        {
                            var cas = OrderTracking.Cascade(x, fs, block, 0.5, WindowType.Hann, byRpm ? pulses : null, 60);
                            trWaterfall.Title = $"Cascada · «{chName}»"; trWaterfall.KeyLabel = byRpm ? "rpm" : "tiempo (s)";
                            trWaterfall.YLabel = $"Amplitud ({u} pico)"; trWaterfall.MaxX = maxHz;
                            trWaterfall.Clear();
                            foreach (var c in cas) trWaterfall.Add(byRpm ? c.Rpm : c.TimeS, c.Spectrum.Frequencies, c.Spectrum.Amplitudes);
                            trWaterfall.Invalidate();
                            ShowTrChart(trWaterfall);
                            AddColumns(lvTr, (byRpm ? "rpm" : "Tiempo (s)", 80), ("Pico (Hz)", 80), ("Amplitud", 90));
                            sb.AppendLine("tiempo_s;rpm;frecuencia_pico_hz;amplitud_pico");
                            foreach (var c in cas)
                            {
                                var pk = SpectralFeatures.Peaks(c.Spectrum, 1, 0.5).FirstOrDefault();
                                if (pk == null) continue;
                                lvTr.Items.Add(new ListViewItem(new[] { byRpm ? $"{c.Rpm:0}" : $"{c.TimeS:0.00}", pk.Frequency.ToString("0.##", Es), FormatValue(pk.Amplitude) }));
                                sb.AppendLine($"{Inv(c.TimeS)};{Inv(c.Rpm)};{Inv(pk.Frequency)};{Inv(pk.Amplitude)}");
                            }
                            lblTrSummary.Text = $"Cascada: {cas.Count} espectros de {block} muestras (Δf {fs / block:0.##} Hz). Las líneas que se mueven con la velocidad son órdenes; las fijas son resonancias.";
                        }
                        else
                        {
                            var sg = OrderTracking.Spectrogram(x, fs, block, 0.75, WindowType.Hann, byRpm ? pulses : null, maxHz);
                            trHeatmap.Title = $"Espectrograma · «{chName}»";
                            trHeatmap.YLabel = "Tiempo (s)";
                            trHeatmap.SetData(sg.Amplitudes, sg.Frequencies, sg.Times);
                            ShowTrChart(trHeatmap);
                            AddColumns(lvTr, ("Tiempo (s)", 80), ("rpm", 70), ("Pico (Hz)", 80));
                            sb.AppendLine("tiempo_s;rpm;" + string.Join(";", sg.Frequencies.Select(Inv)));
                            int stepRows = Math.Max(1, sg.Times.Length / 40);
                            for (int i = 0; i < sg.Times.Length; i++)
                            {
                                int best = 1; for (int k = 1; k < sg.Frequencies.Length; k++) if (sg.Amplitudes[i, k] > sg.Amplitudes[i, best]) best = k;
                                if (i % stepRows == 0) lvTr.Items.Add(new ListViewItem(new[] { $"{sg.Times[i]:0.00}", byRpm ? $"{sg.Rpm[i]:0}" : "", sg.Frequencies[best].ToString("0.##", Es) }));
                                sb.Append(Inv(sg.Times[i])).Append(';').Append(Inv(sg.Rpm[i]));
                                for (int k = 0; k < sg.Frequencies.Length; k++) sb.Append(';').Append(Inv(sg.Amplitudes[i, k]));
                                sb.AppendLine();
                            }
                            lblTrSummary.Text = $"Espectrograma: {sg.Times.Length} bloques × {sg.Frequencies.Length} líneas hasta {maxHz:0} Hz (escala dB, 60 dB de rango).";
                        }
                        break;
                    }
                    default:
                    {
                        int yi = cmbTrY.SelectedIndex;
                        if (recording.Channels.Count < 2 || yi < 0 || yi == chi) { lblTrSummary.Text = "La órbita necesita dos canales distintos (X = canal de la pestaña 2, Y = canal elegido acá) montados a 90°."; ShowTrChart(null); return; }
                        var y = TrSignal(yi, unit);
                        var o = OrbitAnalysis.Compute(x, y, fs, pulses, chkTrFilter.Checked, 3);
                        trOrbit.Title = $"Órbita {(chkTrFilter.Checked ? "filtrada 1X" : "sin filtrar")} · X «{chName}» · Y «{recording.Channels[yi].Name}»";
                        trOrbit.Unit = "(" + u + ")"; trOrbit.Labels.Clear(); trOrbit.Dots.Clear();
                        trOrbit.Dots.AddRange(o.Keyphasor);
                        trOrbit.SetData(o.X, o.Y);
                        ShowTrChart(trOrbit);
                        AddColumns(lvTr, ("Parámetro", 160), ("Valor", 160));
                        void Row(string a, string b) => lvTr.Items.Add(new ListViewItem(new[] { a, b }));
                        Row("1X en X", o.X1.ToString()); Row("1X en Y", o.Y1.ToString());
                        Row("Semieje mayor", FormatValue(o.SemiMajor) + " " + u); Row("Semieje menor", FormatValue(o.SemiMinor) + " " + u);
                        Row("Relación de ejes", o.AxisRatio.ToString("0.00", Es)); Row("Inclinación", $"{o.TiltDeg:0}°"); Row("Precesión", o.PrecessionText);
                        sb.AppendLine("x;y");
                        for (int i = 0; i < o.X.Length; i++) sb.AppendLine(Inv(o.X[i]) + ";" + Inv(o.Y[i]));
                        string hint = o.Precession == Precession.Reverse ? "Precesión inversa: posible roce, desalineación severa o sensores intercambiados." :
                                      o.AxisRatio < 0.3 ? "Órbita muy aplanada: precarga radial o desalineación." : "Órbita elíptica normal de desbalance.";
                        lblTrSummary.Text = $"Órbita de 3 vueltas (puntos rojos = pulso del tacómetro, verde = inicio). Elipse 1X: {FormatValue(o.SemiMajor)} × {FormatValue(o.SemiMinor)} {u}, relación {o.AxisRatio:0.00}, precesión {o.PrecessionText}.\n{hint}";
                        break;
                    }
                }
                trCsv = sb.ToString();
            }
            catch (Exception ex)
            {
                lblTrSummary.Text = "No se pudo calcular: " + ex.Message;
                LogManager.Instance.LogError("Transitorios: " + ex);
            }
        }

        private static string Inv(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static void AddColumns(ListView lv, params (string Text, int Width)[] cols)
        {
            foreach (var (t, w) in cols) lv.Columns.Add(t, w);
        }

        private void ExportTransient(bool png)
        {
            Control visible = trHost.Controls.Cast<Control>().FirstOrDefault(c => c.Visible);
            if (visible == null || string.IsNullOrEmpty(trCsv)) { Warn("Calculá primero un análisis."); return; }
            string name = $"{Safe(TrModes[cmbTrMode.SelectedIndex])}_{DateTime.Now:yyyyMMdd_HHmmss}";
            using var sfd = new SaveFileDialog { Filter = png ? "Imagen PNG|*.png" : "CSV|*.csv", FileName = name + (png ? ".png" : ".csv") };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                if (png)
                {
                    using var bmp = new Bitmap(Math.Max(1, visible.Width), Math.Max(1, visible.Height));
                    visible.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    bmp.Save(sfd.FileName, System.Drawing.Imaging.ImageFormat.Png);
                }
                else File.WriteAllText(sfd.FileName, trCsv, new UTF8Encoding(false));
                SetStatus("Exportado: " + sfd.FileName);
            }
            catch (Exception ex) { Error("No se pudo exportar", ex); }
        }

        // ───────────────────────────── 6. Tendencias ─────────────────────────────
        private static string TrendPathFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnalisisVibraciones", "tendencias.ruta");
        private static string DefaultTrendFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnalisisVibraciones", "Tendencias");

        private TabPage BuildTrendTab()
        {
            trendPage = new TabPage("6. Tendencias");
            var bar1 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 4, 6, 0), WrapContents = true };
            bar1.Controls.AddRange(new Control[]
            {
                Btn("Carpeta de la base…", (s, e) => ChooseTrendFolder()),
                Btn("+ Planta", (s, e) => AddPlant()), Btn("+ Máquina", (s, e) => AddMachineAsset()), Btn("+ Punto", (s, e) => AddPoint()),
                Btn("Renombrar", (s, e) => RenameNode()), Btn("Eliminar", (s, e) => DeleteNode()),
                Btn("Máquina ← pestaña 1", (s, e) => CopyMachineToAsset()), Btn("Máquina → pestaña 1", (s, e) => LoadAssetMachine()),
                Btn("Alarmas…", (s, e) => EditAlarms()),
                Btn("Demo (12 meses)", (s, e) => CreateTrendDemo()),
            });
            var bar2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 0, 6, 4), WrapContents = true };
            cmbTrendFeature = Combo(FeatureKeys.Names.Values, 260);
            cmbTrendFeature.SelectedIndexChanged += (s, e) => ShowTrend();
            chkSaveWave = new CheckBox { Text = "Guardar también la señal", AutoSize = true, Checked = true, Margin = new Padding(8, 8, 4, 0) };
            chkTemp = new CheckBox { Text = "Temperatura (°C):", AutoSize = true, Margin = new Padding(8, 8, 0, 0) };
            nudTemp = Nud(-50, 400, 40, 1); nudTemp.Width = 65;
            nudTemp.ValueChanged += (s, e) => chkTemp.Checked = true;
            cmbRoute = Combo(new string[0], 180);
            cmbRoute.SelectedIndexChanged += (s, e) => { routeIndex = cmbRoute.SelectedIndex > 0 ? 0 : -1; UpdateRouteLabel(); };
            lblRoute = new Label { AutoSize = true, Margin = new Padding(6, 8, 0, 0), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            bar2.Controls.AddRange(new Control[]
            {
                Btn("Guardar medición actual", (s, e) => SaveCurrentMeasurement()), chkSaveWave, chkTemp, nudTemp,
                Lbl("Indicador:"), cmbTrendFeature,
                Btn("Marcar línea base", (s, e) => SetBaseline()), Btn("Eliminar medición", (s, e) => DeleteSelectedMeasurement()), Btn("Exportar CSV", (s, e) => ExportTrend()),
                Lbl("Ruta:"), cmbRoute, Btn("Nueva ruta…", (s, e) => NewRoute()), Btn("◀", (s, e) => MoveRoute(-1)), Btn("▶", (s, e) => MoveRoute(1)), lblRoute,
            });
            tvAssets = new TreeView { Dock = DockStyle.Fill, HideSelection = false, Font = new Font("Segoe UI", 10f) };
            tvAssets.AfterSelect += (s, e) => ShowTrend();
            lblDbPath = new Label { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(4), Font = new Font("Segoe UI", 8f) };
            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(tvAssets); left.Controls.Add(lblDbPath);

            lblTrendSummary = new Label { Dock = DockStyle.Top, Height = 52, Padding = new Padding(8, 4, 8, 4), Font = new Font("Consolas", 9.5f) };
            trendPlot = new XYPlotControl { Dock = DockStyle.Fill, XLabel = "Fecha", ShowPoints = true, XFormatter = d => DateTime.FromOADate(d).ToString("dd/MM/yy", Es) };
            lvMeas = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, MultiSelect = true };
            AddColumns(lvMeas, ("Fecha", 120), ("rpm", 60), ("Valor", 80), ("Nivel", 70), ("Señal", 50), ("Base", 45));
            lvMeas.DoubleClick += (s, e) => OpenMeasurementWaveform();
            lvAlarms = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            AddColumns(lvAlarms, ("Nivel", 70), ("Indicador", 230), ("Valor", 70), ("Alerta", 70), ("Peligro", 70));
            var lists = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
            var gm = new GroupBox { Text = "Mediciones (doble clic: abrir la señal)", Dock = DockStyle.Fill }; gm.Controls.Add(lvMeas);
            var ga = new GroupBox { Text = "Alarmas de la última medición", Dock = DockStyle.Fill }; ga.Controls.Add(lvAlarms);
            lists.Panel1.Controls.Add(gm); lists.Panel2.Controls.Add(ga);
            var center = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            center.Panel1.Controls.Add(trendPlot); center.Panel1.Controls.Add(lblTrendSummary);
            center.Panel2.Controls.Add(lists);
            center.HandleCreated += (s, e) => { try { center.SplitterDistance = Math.Max(150, center.Height * 3 / 5); } catch { } };
            var main = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel1 };
            main.Panel1.Controls.Add(left); main.Panel2.Controls.Add(center);
            main.HandleCreated += (s, e) => { try { main.SplitterDistance = 300; } catch { } };
            trendPage.Controls.Add(main);
            trendPage.Controls.Add(bar2);
            trendPage.Controls.Add(bar1);
            return trendPage;
        }

        private void EnsureTrendDb()
        {
            if (trendDb != null) return;
            try
            {
                string folder = File.Exists(TrendPathFile) ? File.ReadAllText(TrendPathFile).Trim() : DefaultTrendFolder;
                if (string.IsNullOrWhiteSpace(folder)) folder = DefaultTrendFolder;
                OpenTrendDb(folder);
            }
            catch (Exception ex) { Error("No se pudo abrir la base de tendencias", ex); }
        }

        private void OpenTrendDb(string folder)
        {
            trendDb = new TrendDatabase(folder);
            Directory.CreateDirectory(Path.GetDirectoryName(TrendPathFile));
            File.WriteAllText(TrendPathFile, folder);
            lblDbPath.Text = "Base: " + folder;
            RefreshTree();
            RefreshRoutes();
        }

        private void ChooseTrendFolder()
        {
            using var dlg = new FolderBrowserDialog { Description = "Carpeta de la base de mediciones (se crea si está vacía)", UseDescriptionForTitle = true, SelectedPath = trendDb?.Folder ?? DefaultTrendFolder };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try { OpenTrendDb(dlg.SelectedPath); SetStatus("Base de tendencias: " + dlg.SelectedPath); }
            catch (Exception ex) { Error("No se pudo abrir la carpeta", ex); }
        }

        private void RefreshTree(string selectId = null)
        {
            if (trendDb == null) return;
            selectId ??= (tvAssets.SelectedNode?.Tag as MeasurementPoint)?.Id ?? (tvAssets.SelectedNode?.Tag as MachineAsset)?.Id ?? (tvAssets.SelectedNode?.Tag as Plant)?.Id;
            tvAssets.BeginUpdate();
            tvAssets.Nodes.Clear();
            TreeNode select = null;
            foreach (var p in trendDb.Catalog.Plants)
            {
                var pn = new TreeNode("🏭 " + p.Name) { Tag = p };
                var worstPlant = AlarmLevel.Normal;
                foreach (var m in p.Machines)
                {
                    var mn = new TreeNode("⚙ " + m.Name + (string.IsNullOrEmpty(m.Area) ? "" : $" ({m.Area})")) { Tag = m };
                    var worstMachine = AlarmLevel.Normal;
                    foreach (var pt in m.Points)
                    {
                        var hist = trendDb.Measurements(pt.Id);
                        var lvl = hist.Count > 0 ? TrendAnalysis.WorstLevel(TrendAnalysis.Evaluate(hist[^1], pt, m.Machine.MachineClass, hist)) : AlarmLevel.Normal;
                        var tn = new TreeNode($"● {pt.Name} [{hist.Count}]") { Tag = pt, ForeColor = LevelColor(lvl) };
                        if (lvl > worstMachine) worstMachine = lvl;
                        mn.Nodes.Add(tn);
                        if (pt.Id == selectId) select = tn;
                    }
                    mn.ForeColor = LevelColor(worstMachine);
                    if (worstMachine > worstPlant) worstPlant = worstMachine;
                    pn.Nodes.Add(mn);
                    if (m.Id == selectId) select = mn;
                }
                pn.ForeColor = LevelColor(worstPlant);
                tvAssets.Nodes.Add(pn);
                if (p.Id == selectId) select = pn;
            }
            tvAssets.ExpandAll();
            tvAssets.EndUpdate();
            if (select != null) tvAssets.SelectedNode = select;
            else ShowTrend();
        }

        private static Color LevelColor(AlarmLevel l) => l switch
        {
            AlarmLevel.Danger => Color.FromArgb(200, 40, 40),
            AlarmLevel.Alert => Color.FromArgb(210, 130, 0),
            _ => Color.FromArgb(30, 140, 70),
        };

        private static string LevelText(AlarmLevel l) => l switch { AlarmLevel.Danger => "PELIGRO", AlarmLevel.Alert => "ALERTA", _ => "Normal" };

        private (Plant Plant, MachineAsset Machine, MeasurementPoint Point) Selection()
        {
            var n = tvAssets.SelectedNode;
            if (n == null) return (null, null, null);
            if (n.Tag is MeasurementPoint pt) return ((Plant)n.Parent.Parent.Tag, (MachineAsset)n.Parent.Tag, pt);
            if (n.Tag is MachineAsset m) return ((Plant)n.Parent.Tag, m, null);
            return ((Plant)n.Tag, null, null);
        }

        private string SelectedFeature => FeatureKeys.Names.Keys.ElementAt(Math.Max(0, cmbTrendFeature.SelectedIndex));

        private void AddPlant()
        {
            EnsureTrendDb();
            var name = PromptForm.Ask(this, "Nueva planta", "Nombre de la planta o cliente:", "Planta");
            if (name == null) return;
            var p = new Plant { Name = name };
            trendDb.Catalog.Plants.Add(p); trendDb.Save(); RefreshTree(p.Id);
        }

        private void AddMachineAsset()
        {
            EnsureTrendDb();
            var (plant, _, _) = Selection();
            if (plant == null) { if (trendDb.Catalog.Plants.Count == 0) AddPlant(); plant = Selection().Plant ?? trendDb.Catalog.Plants.FirstOrDefault(); }
            if (plant == null) return;
            using var dlg = new PromptForm("Nueva máquina", "Nombre de la máquina (p. ej. «Ventilador VT-01»):", machine.Name, "Área / sector:", new[] { "(sin área)", "Producción", "Servicios", "Mantenimiento" });
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Value.Length == 0) return;
            var m = new MachineAsset { Name = dlg.Value, Area = dlg.Option > 0 ? new[] { "", "Producción", "Servicios", "Mantenimiento" }[dlg.Option] : "" };
            CopyMachine(machine, m.Machine);
            m.Machine.Name = m.Name;
            plant.Machines.Add(m); trendDb.Save(); RefreshTree(m.Id);
            SetStatus($"Máquina «{m.Name}» creada con los datos técnicos de la pestaña 1.");
        }

        private static readonly string[] Directions = { "Horizontal", "Vertical", "Axial", "Radial" };

        private void AddPoint()
        {
            EnsureTrendDb();
            var (_, m, _) = Selection();
            if (m == null) { Warn("Seleccioná una máquina en el árbol."); return; }
            using var dlg = new PromptForm("Nuevo punto", "Nombre del punto (p. ej. «Motor lado acople»):", "Lado acople", "Dirección:", Directions);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Value.Length == 0) return;
            var pt = new MeasurementPoint { Name = $"{dlg.Value} · {Directions[Math.Max(0, dlg.Option)]}", Direction = (MeasureDirection)Math.Max(0, dlg.Option) };
            m.Points.Add(pt); trendDb.Save(); RefreshTree(pt.Id);
        }

        private void RenameNode()
        {
            var (p, m, pt) = Selection();
            if (p == null) return;
            string current = pt?.Name ?? m?.Name ?? p.Name;
            var name = PromptForm.Ask(this, "Renombrar", "Nuevo nombre:", current);
            if (name == null) return;
            if (pt != null) pt.Name = name; else if (m != null) { m.Name = name; m.Machine.Name = name; } else p.Name = name;
            trendDb.Save(); RefreshTree();
        }

        private void DeleteNode()
        {
            var (p, m, pt) = Selection();
            if (p == null) return;
            string what = pt != null ? $"el punto «{pt.Name}»" : m != null ? $"la máquina «{m.Name}»" : $"la planta «{p.Name}»";
            if (MessageBox.Show(this, $"¿Eliminar {what} con todas sus mediciones? No se puede deshacer.", "Eliminar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var points = pt != null ? new[] { pt } : m != null ? m.Points.ToArray() : p.Machines.SelectMany(x => x.Points).ToArray();
            foreach (var x in points) trendDb.DeletePoint(x.Id);
            if (pt == null && m != null) p.Machines.Remove(m);
            if (pt == null && m == null) trendDb.Catalog.Plants.Remove(p);
            trendDb.Save(); RefreshTree(); RefreshRoutes();
        }

        private void CopyMachineToAsset()
        {
            var (_, m, _) = Selection();
            if (m == null) { Warn("Seleccioná una máquina o un punto."); return; }
            CopyMachine(machine, m.Machine); m.Machine.Name = m.Name;
            trendDb.Save(); ShowTrend();
            SetStatus($"Datos técnicos de la pestaña 1 copiados a «{m.Name}» (velocidad, rodamiento, engranaje, clase ISO).");
        }

        private void LoadAssetMachine()
        {
            var (_, m, _) = Selection();
            if (m == null) { Warn("Seleccioná una máquina o un punto."); return; }
            CopyMachine(m.Machine, machine);
            machineGrid.Refresh(); UpdateMachineSummary();
            SetStatus($"Datos de «{m.Name}» cargados en la pestaña 1.");
        }

        private void EditAlarms()
        {
            var (_, _, pt) = Selection();
            if (pt == null) { Warn("Seleccioná un punto de medición."); return; }
            using var dlg = new AlarmEditorForm(pt.Name, pt.Alarms);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            pt.Alarms = dlg.Rules; trendDb.Save(); RefreshTree(pt.Id);
        }

        private void CreateTrendDemo()
        {
            EnsureTrendDb();
            Cursor = Cursors.WaitCursor;
            try { var p = trendDb.CreateDemo(); RefreshTree(p.Machines[0].Points[0].Id); RefreshRoutes(); SetStatus("Planta de demostración creada con 12 meses de mediciones simuladas."); }
            catch (Exception ex) { Error("No se pudo crear la demo", ex); }
            finally { Cursor = Cursors.Default; }
        }

        private MeasurementPoint TargetPoint(out MachineAsset asset)
        {
            asset = null;
            if (routeIndex >= 0 && cmbRoute.SelectedIndex > 0)
            {
                var route = trendDb.Catalog.Routes[cmbRoute.SelectedIndex - 1];
                if (routeIndex < route.PointIds.Count && trendDb.FindPoint(route.PointIds[routeIndex]) is { } f) { asset = f.Machine; return f.Point; }
            }
            var (_, m, pt) = Selection();
            asset = m;
            return pt;
        }

        private void SaveCurrentMeasurement()
        {
            EnsureTrendDb();
            if (recording == null || cmbChannel.SelectedIndex < 0) { Warn("Primero adquirí una señal (pestaña 2)."); return; }
            var pt = TargetPoint(out var asset);
            if (pt == null) { Warn("Seleccioná un punto de medición en el árbol (o una ruta)."); return; }
            try
            {
                var rec = FeatureExtractor.Extract(recording, cmbChannel.SelectedIndex, asset.Machine, pt.Id);
                rec.Notes = recording.Source;
                if (chkTemp.Checked) rec.Features[FeatureKeys.Temperature] = (double)nudTemp.Value;
                trendDb.AddMeasurement(rec, chkSaveWave.Checked ? recording : null);
                var hist = trendDb.Measurements(pt.Id);
                var lvl = TrendAnalysis.WorstLevel(TrendAnalysis.Evaluate(rec, pt, asset.Machine.MachineClass, hist));
                SetStatus($"Medición guardada en «{asset.Name} · {pt.Name}»: {LevelText(lvl)}.");
                OnMeasurementSaved(asset, pt, rec, lvl, TrendAnalysis.Evaluate(rec, pt, asset.Machine.MachineClass, hist));
                bool onRoute = routeIndex >= 0;
                RefreshTree(pt.Id);
                if (onRoute) MoveRoute(1);
            }
            catch (Exception ex) { Error("No se pudo guardar la medición", ex); }
        }

        private void ShowTrend()
        {
            if (trendDb == null || trendPlot == null) return;
            lvMeas.Items.Clear(); lvAlarms.Items.Clear();
            trendPlot.HorizontalLines.Clear(); trendPlot.Markers.Clear(); trendPlot.Peaks.Clear();
            var (plant, m, pt) = Selection();
            if (pt == null)
            {
                trendPlot.Title = "";
                trendPlot.SetData(Array.Empty<double>(), Array.Empty<double>());
                lblTrendSummary.Text = m != null ? $"«{m.Name}»: {m.Points.Count} puntos · {m.Machine.Rpm:0} rpm · {Severity.Name(m.Machine.MachineClass)}{(m.Machine.Bearing != null ? " · rodamiento cargado" : "")}.\nSeleccioná un punto para ver su tendencia."
                    : plant != null ? $"«{plant.Name}»: {plant.Machines.Count} máquinas. Seleccioná un punto." : "Creá una planta, una máquina y un punto, o tocá «Demo (12 meses)» para ver un ejemplo.";
                return;
            }
            var hist = trendDb.Measurements(pt.Id);
            string key = SelectedFeature;
            var series = TrendAnalysis.Series(hist, key);
            trendPlot.Title = $"{m.Name} · {pt.Name} · {FeatureKeys.Name(key)}";
            trendPlot.YLabel = FeatureKeys.Name(key);
            trendPlot.SetData(series.Select(p => p.Time.ToOADate()).ToArray(), series.Select(p => p.Value).ToArray());
            var baseline = TrendAnalysis.Baseline(pt, hist);
            var baseIds = baseline.Select(b => b.Id).ToHashSet();
            var rule = pt.Alarms.FirstOrDefault(a => a.Enabled && a.Feature == key);
            double alert = double.NaN, danger = double.NaN;
            if (rule != null)
            {
                (alert, danger) = TrendAnalysis.Limits(rule, m.Machine.MachineClass, baseline);
                if (!double.IsNaN(alert)) trendPlot.HorizontalLines.Add(new XYPlotControl.HLine(alert, $"Alerta {FormatValue(alert)}", Color.FromArgb(210, 130, 0)));
                if (!double.IsNaN(danger)) trendPlot.HorizontalLines.Add(new XYPlotControl.HLine(danger, $"Peligro {FormatValue(danger)}", Color.FromArgb(200, 40, 40)));
            }
            if (series.Count == 1) trendPlot.SetView(series[0].Time.AddDays(-15).ToOADate(), series[0].Time.AddDays(15).ToOADate());
            else if (series.Count > 1) { double span = series[^1].Time.ToOADate() - series[0].Time.ToOADate(); trendPlot.SetView(series[0].Time.ToOADate() - span * 0.03, series[^1].Time.ToOADate() + span * 0.03); }
            trendPlot.Invalidate();

            foreach (var r in hist.Reverse())
            {
                var lvl = TrendAnalysis.WorstLevel(TrendAnalysis.Evaluate(r, pt, m.Machine.MachineClass, hist));
                lvMeas.Items.Add(new ListViewItem(new[]
                {
                    r.Timestamp.ToString("dd/MM/yyyy HH:mm", Es), $"{r.Rpm:0}", double.IsNaN(r.Get(key)) ? "—" : FormatValue(r.Get(key)), LevelText(lvl),
                    string.IsNullOrEmpty(r.WaveformFile) ? "" : "sí", baseIds.Contains(r.Id) ? "✓" : "",
                }) { Tag = r, ForeColor = LevelColor(lvl) });
            }
            string forecast = "";
            if (hist.Count > 0)
            {
                foreach (var a in TrendAnalysis.Evaluate(hist[^1], pt, m.Machine.MachineClass, hist).OrderByDescending(a => a.Level))
                    lvAlarms.Items.Add(new ListViewItem(new[] { LevelText(a.Level), FeatureKeys.Name(a.Feature), FormatValue(a.Value), FormatValue(a.AlertLimit), FormatValue(a.DangerLimit) }) { ForeColor = LevelColor(a.Level) });
                var fit = TrendAnalysis.Fit(series);
                if (fit != null)
                {
                    string kind = fit.Exponential ? "exponencial" : "lineal";
                    string rate = fit.Exponential ? $"{(Math.Exp(fit.SlopePerDay * 30) - 1) * 100:+0;-0} %/mes" : $"{fit.SlopePerDay * 30:+0.###;-0.###}/mes";
                    forecast = $"Tendencia {kind} ({rate}, R² {fit.R2:0.00}).";
                    double last = series[^1].Value;
                    foreach (var (lim, name) in new[] { (alert, "alerta"), (danger, "peligro") })
                    {
                        if (double.IsNaN(lim) || last >= lim) continue;
                        var when = fit.Crossing(lim);
                        if (when.HasValue && when.Value > DateTime.Now) forecast += $"  Llega a {name} ≈ {when.Value:dd/MM/yyyy} ({(when.Value - DateTime.Now).TotalDays:0} días).";
                    }
                }
            }
            var worst = hist.Count > 0 ? TrendAnalysis.WorstLevel(TrendAnalysis.Evaluate(hist[^1], pt, m.Machine.MachineClass, hist)) : AlarmLevel.Normal;
            lblTrendSummary.Text = $"{hist.Count} mediciones · línea base: {baseline.Count} · estado actual: {LevelText(worst)}" +
                                   (hist.Count > 0 ? $" · última {hist[^1].Timestamp:dd/MM/yyyy}" : "") + "\n" + (forecast.Length > 0 ? forecast : "Se necesitan 3 mediciones o más para estimar la tendencia.");
            lblTrendSummary.ForeColor = worst == AlarmLevel.Normal ? lblTrendSummary.Parent?.ForeColor ?? Color.Black : LevelColor(worst);
        }

        private void SetBaseline()
        {
            var (_, _, pt) = Selection();
            if (pt == null || lvMeas.SelectedItems.Count == 0) { Warn("Seleccioná un punto y una o más mediciones de la lista (máquina en buen estado) para usarlas como línea base."); return; }
            pt.BaselineIds = lvMeas.SelectedItems.Cast<ListViewItem>().Select(i => ((MeasurementRecord)i.Tag).Id).ToList();
            trendDb.Save(); RefreshTree(pt.Id);
            SetStatus($"Línea base de «{pt.Name}»: {pt.BaselineIds.Count} medición(es). Las alarmas relativas y estadísticas se recalcularon.");
        }

        private void DeleteSelectedMeasurement()
        {
            var (_, _, pt) = Selection();
            if (pt == null || lvMeas.SelectedItems.Count == 0) { Warn("Seleccioná las mediciones a eliminar."); return; }
            if (MessageBox.Show(this, $"¿Eliminar {lvMeas.SelectedItems.Count} medición(es)?", "Eliminar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            foreach (ListViewItem i in lvMeas.SelectedItems) trendDb.DeleteMeasurement(pt.Id, ((MeasurementRecord)i.Tag).Id);
            RefreshTree(pt.Id);
        }

        private void OpenMeasurementWaveform()
        {
            if (lvMeas.SelectedItems.Count == 0) return;
            var r = (MeasurementRecord)lvMeas.SelectedItems[0].Tag;
            var w = trendDb.LoadWaveform(r);
            if (w == null) { Warn("Esa medición no tiene la señal guardada (solo indicadores)."); return; }
            cmbSource.SelectedIndex = (int)Source.File;
            SetRecording(w with { Source = $"Base · {r.Timestamp:dd/MM/yyyy HH:mm}" });
            tabs.SelectedIndex = 1;
            SetStatus($"Señal del {r.Timestamp:dd/MM/yyyy HH:mm} cargada: se puede analizar en las pestañas 2 a 5.");
        }

        private void ExportTrend()
        {
            var (_, m, pt) = Selection();
            if (pt == null) { Warn("Seleccioná un punto de medición."); return; }
            using var sfd = new SaveFileDialog { Filter = "CSV|*.csv", FileName = Safe($"{m.Name}_{pt.Name}") + ".csv" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(sfd.FileName, trendDb.ExportCsv(pt.Id), new UTF8Encoding(false));
            SetStatus("Tendencia exportada: " + sfd.FileName);
        }

        private void RefreshRoutes()
        {
            if (trendDb == null) return;
            int sel = cmbRoute.SelectedIndex;
            cmbRoute.Items.Clear();
            cmbRoute.Items.Add("(sin ruta)");
            foreach (var r in trendDb.Catalog.Routes) cmbRoute.Items.Add($"{r.Name} ({r.PointIds.Count})");
            cmbRoute.SelectedIndex = Math.Min(Math.Max(0, sel), cmbRoute.Items.Count - 1);
            UpdateRouteLabel();
        }

        private void NewRoute()
        {
            EnsureTrendDb();
            var (p, m, _) = Selection();
            if (p == null) { Warn("Seleccioná una planta o una máquina: la ruta incluye todos sus puntos en orden."); return; }
            var ids = (m != null ? m.Points : p.Machines.SelectMany(x => x.Points)).Select(x => x.Id).ToList();
            if (ids.Count == 0) { Warn("No hay puntos para la ruta."); return; }
            var name = PromptForm.Ask(this, "Nueva ruta", "Nombre de la ruta:", $"Ruta {(m?.Name ?? p.Name)}");
            if (name == null) return;
            trendDb.Catalog.Routes.Add(new MeasurementRoute { Name = name, PointIds = ids });
            trendDb.Save(); RefreshRoutes();
            cmbRoute.SelectedIndex = cmbRoute.Items.Count - 1;
        }

        private void MoveRoute(int delta)
        {
            if (cmbRoute.SelectedIndex <= 0) return;
            var route = trendDb.Catalog.Routes[cmbRoute.SelectedIndex - 1];
            routeIndex = Math.Clamp(routeIndex + delta, 0, Math.Max(0, route.PointIds.Count - 1));
            UpdateRouteLabel();
            if (routeIndex < route.PointIds.Count) RefreshTree(route.PointIds[routeIndex]);
        }

        private void UpdateRouteLabel()
        {
            if (lblRoute == null) return;
            if (trendDb == null || cmbRoute.SelectedIndex <= 0 || routeIndex < 0) { lblRoute.Text = ""; return; }
            var route = trendDb.Catalog.Routes[cmbRoute.SelectedIndex - 1];
            if (route.PointIds.Count == 0) { lblRoute.Text = "ruta vacía"; return; }
            var f = trendDb.FindPoint(route.PointIds[Math.Min(routeIndex, route.PointIds.Count - 1)]);
            lblRoute.Text = f is { } x ? $"Punto {routeIndex + 1}/{route.PointIds.Count}: {x.Machine.Name} · {x.Point.Name}" : "punto inexistente";
        }

        // ───────────────────────────── 7. Balanceo ─────────────────────────────
        private TabPage BuildBalancingTab()
        {
            var page = new TabPage("8. Balanceo");
            var cfg = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(6) };
            cmbPlanes = Combo(new[] { "1 plano", "2 planos" }, 90);
            cmbAngles = Combo(new[] { "Ángulos contrarios al giro", "Ángulos a favor del giro" }, 190);
            chkKeepTrial = new CheckBox { Text = "Dejar los pesos de prueba", AutoSize = true, Margin = new Padding(8, 8, 8, 0) };
            nudTolerance = Nud(0.01m, 100m, 1.0m, 2);
            chkAutoPlace = new CheckBox { Text = "Colocar los pesos en el simulador", AutoSize = true, Checked = true, Margin = new Padding(8, 8, 8, 0) };
            btnNewSession = Btn("Nueva sesión", (s, e) => NewSession());
            cfg.Controls.AddRange(new Control[] { Lbl("Planos:"), cmbPlanes, cmbAngles, chkKeepTrial, Lbl("Tolerancia (vibración 1X):"), nudTolerance, chkAutoPlace, btnNewSession });

            var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Columna 1: pasos e instrucciones
            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            lstSteps = new ListBox { Dock = DockStyle.Top, Height = 170, Font = new Font("Segoe UI", 10f), SelectionMode = SelectionMode.None };
            lblInstruction = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10.5f), Padding = new Padding(0, 10, 0, 0) };
            left.Controls.Add(lblInstruction);
            left.Controls.Add(lstSteps);

            // Columna 2: datos de la corrida
            var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoScroll = true, Padding = new Padding(6) };
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            nudT1g = Nud(0.01m, 100000m, 20m, 2); nudT1a = Nud(0m, 359.9m, 0m, 1);
            nudT2g = Nud(0.01m, 100000m, 20m, 2); nudT2a = Nud(0m, 359.9m, 0m, 1);
            nudM1a = Nud(0m, 100000m, 0m, 3); nudM1p = Nud(0m, 359.9m, 0m, 1);
            nudM2a = Nud(0m, 100000m, 0m, 3); nudM2p = Nud(0m, 359.9m, 0m, 1);
            nudPositions = Nud(0m, 64m, 0m, 0); nudDrill = Nud(1m, 50m, 10m, 1);
            rbFromAcq = new RadioButton { Text = "Medir con la adquisición (1X con tacómetro)", AutoSize = true, Checked = true };
            rbManual = new RadioButton { Text = "Ingresar lectura manual", AutoSize = true };
            lblSuggest = new Label { AutoSize = true, ForeColor = Color.FromArgb(14, 124, 134) };
            btnMeasure = Btn("Medir y registrar", (s, e) => MeasureStep());
            btnMeasure.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            btnMeasure.Height = 40;
            int row = 0;
            void AddRow(string label, Control a, Control b = null)
            {
                mid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, 0, row);
                if (a != null) mid.Controls.Add(a, 1, row);
                if (b != null) mid.Controls.Add(b, 2, row);
                row++;
            }
            AddRow("", new Label { Text = "Masa (g)", AutoSize = true }, new Label { Text = "Ángulo (°)", AutoSize = true });
            AddRow("Peso de prueba plano 1", nudT1g, nudT1a);
            AddRow("Peso de prueba plano 2", nudT2g, nudT2a);
            mid.Controls.Add(lblSuggest, 0, row); mid.SetColumnSpan(lblSuggest, 3); row++;
            mid.Controls.Add(rbFromAcq, 0, row); mid.SetColumnSpan(rbFromAcq, 3); row++;
            mid.Controls.Add(rbManual, 0, row); mid.SetColumnSpan(rbManual, 3); row++;
            AddRow("", new Label { Text = "Amplitud", AutoSize = true }, new Label { Text = "Fase (°)", AutoSize = true });
            AddRow("Lectura sensor 1", nudM1a, nudM1p);
            AddRow("Lectura sensor 2", nudM2a, nudM2p);
            mid.Controls.Add(btnMeasure, 0, row); mid.SetColumnSpan(btnMeasure, 3); btnMeasure.Dock = DockStyle.Fill; row++;
            AddRow("Posiciones fijas (álabes)", nudPositions);
            AddRow("Broca para quitar (mm)", nudDrill);
            txtResult = new RichTextBox { ReadOnly = true, Height = 220, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10f) };
            mid.Controls.Add(txtResult, 0, row); mid.SetColumnSpan(txtResult, 3); mid.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
            nudPositions.ValueChanged += (s, e) => ShowResult();
            nudDrill.ValueChanged += (s, e) => ShowResult();
            rbManual.CheckedChanged += (s, e) => UpdateStepUi();

            // Columna 3: diagramas y corridas
            var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 430 };
            var polars = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            polars.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            polars.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            polar1 = new BalancePolarControl { Dock = DockStyle.Fill, Title = "Sensor / plano 1" };
            polar2 = new BalancePolarControl { Dock = DockStyle.Fill, Title = "Sensor / plano 2" };
            polars.Controls.Add(polar1, 0, 0);
            polars.Controls.Add(polar2, 1, 0);
            lvRuns = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            lvRuns.Columns.Add("Corrida", 140); lvRuns.Columns.Add("Peso", 150); lvRuns.Columns.Add("Sensor 1", 130); lvRuns.Columns.Add("Sensor 2", 130); lvRuns.Columns.Add("rpm", 70);
            right.Panel1.Controls.Add(polars);
            right.Panel2.Controls.Add(lvRuns);

            main.Controls.Add(left, 0, 0);
            main.Controls.Add(mid, 1, 0);
            main.Controls.Add(right, 2, 0);
            page.Controls.Add(main);
            page.Controls.Add(cfg);

            cmbPlanes.SelectedIndexChanged += (s, e) => { if (session != null && session.Runs.Count == 0) NewSession(); };
            cmbAngles.SelectedIndexChanged += (s, e) => { if (session != null) { session.AnglesAgainstRotation = cmbAngles.SelectedIndex == 0; foreach (var p in new[] { polar1, polar2 }) p.CounterClockwise = session.AnglesAgainstRotation; ShowResult(); } };
            chkKeepTrial.CheckedChanged += (s, e) => { if (session != null && session.Runs.Count == 0) session.KeepTrialWeights = chkKeepTrial.Checked; };
            nudTolerance.ValueChanged += (s, e) => { if (session != null) session.ToleranceVibration = (double)nudTolerance.Value; };
            return page;
        }

        private int Planes => cmbPlanes.SelectedIndex == 1 ? 2 : 1;

        private void NewSession()
        {
            if (cmbPlanes.SelectedIndex < 0) cmbPlanes.SelectedIndex = machine.RecommendedPlanes().Planes - 1;
            if (cmbAngles.SelectedIndex < 0) cmbAngles.SelectedIndex = 0;
            session = new BalancingSession(Planes)
            {
                AnglesAgainstRotation = cmbAngles.SelectedIndex == 0,
                KeepTrialWeights = chkKeepTrial.Checked,
                ToleranceVibration = (double)nudTolerance.Value,
            };
            simSettings.Sensors = Planes;
            simSettings.AddedMasses.Clear();
            sim = new RotorSimulator(simSettings);
            simGrid?.Refresh();
            double trial = Math.Round(TrialWeight.SuggestGrams(machine.EffectiveMassKg, machine.Rpm, machine.CorrectionRadiusMm), 1);
            if (trial > 0) { nudT1g.Value = Clamp(nudT1g, trial); nudT2g.Value = Clamp(nudT2g, trial); }
            lvRuns.Items.Clear();
            txtResult.Clear();
            polar1.Clear(); polar2.Clear();
            polar2.Visible = Planes == 2;
            UpdateStepUi();
        }

        private void UpdateStepUi()
        {
            if (session == null) return;
            bool two = session.Planes == 2;
            var steps = new List<(BalancingStep Step, string Text)> { (BalancingStep.Initial, "1. Medición original (sin pesos)"), (BalancingStep.Trial1, "2. Peso de prueba en el plano 1") };
            if (two) steps.Add((BalancingStep.Trial2, "3. Peso de prueba en el plano 2"));
            steps.Add((BalancingStep.Correction, $"{(two ? 4 : 3)}. Colocar la corrección"));
            steps.Add((BalancingStep.Verification, $"{(two ? 5 : 4)}. Verificar y ajustar"));
            steps.Add((BalancingStep.Done, "✓ Balanceo terminado"));
            lstSteps.Items.Clear();
            foreach (var (st, text) in steps)
            {
                string mark = Order(st) < Order(session.Step) ? "✓ " : st == session.Step ? "▶ " : "   ";
                lstSteps.Items.Add(mark + text);
            }
            string angles = session.AnglesAgainstRotation ? "en sentido CONTRARIO al giro" : "en el sentido de giro";
            lblInstruction.Text = session.Step switch
            {
                BalancingStep.Initial => $"Con la máquina a su velocidad de trabajo, medí la vibración 1X (amplitud y fase) {(two ? "en los dos sensores" : "")} sin ningún peso agregado.\n\nMarcá una referencia en el rotor (cinta reflectiva para el tacómetro): todos los ángulos se miden desde ahí, {angles}.",
                BalancingStep.Trial1 => $"Detené la máquina y colocá el peso de prueba del plano 1 ({nudT1g.Value:0.##} g a {nudT1a.Value:0}°, a {machine.CorrectionRadiusMm:0} mm de radio). Arrancá y medí.\n\nEl peso debe cambiar la vibración al menos un 30 % o 30°.",
                BalancingStep.Trial2 => $"Retirá el peso del plano 1{(session.KeepTrialWeights ? " (o dejalo si elegiste dejarlos)" : "")} y colocá el peso de prueba del plano 2 ({nudT2g.Value:0.##} g a {nudT2a.Value:0}°). Arrancá y medí.",
                BalancingStep.Correction => $"Retirá los pesos de prueba{(session.KeepTrialWeights ? " (no: elegiste dejarlos)" : "")} y colocá la corrección calculada. Luego medí para verificar.",
                BalancingStep.Verification => "La vibración todavía supera la tolerancia: colocá además la corrección de ajuste (trim) indicada y volvé a medir.",
                _ => "La vibración quedó dentro de la tolerancia. Generá el informe en la pestaña 5.",
            };
            btnMeasure.Enabled = session.Step != BalancingStep.Done;
            btnMeasure.Text = session.Step switch
            {
                BalancingStep.Initial => "Medir y registrar: original",
                BalancingStep.Trial1 => "Medir con peso de prueba 1",
                BalancingStep.Trial2 => "Medir con peso de prueba 2",
                BalancingStep.Correction or BalancingStep.Verification => "Medir verificación",
                _ => "Terminado",
            };
            nudT1g.Enabled = nudT1a.Enabled = session.Step == BalancingStep.Trial1;
            nudT2g.Enabled = nudT2a.Enabled = two && session.Step == BalancingStep.Trial2;
            nudM1a.Enabled = nudM1p.Enabled = rbManual.Checked;
            nudM2a.Enabled = nudM2p.Enabled = rbManual.Checked && two;
            cmbPlanes.Enabled = session.Runs.Count == 0;
            chkKeepTrial.Enabled = session.Runs.Count == 0;
            stepStatus.Text = $"Balanceo: {steps.FirstOrDefault(s => s.Step == session.Step).Text ?? ""}";
        }

        private static int Order(BalancingStep s) => (int)s;

        /// <summary>Obtiene las lecturas 1X del paso actual (adquisición o manual) y las registra en la sesión.</summary>
        private void MeasureStep()
        {
            try
            {
                bool two = session.Planes == 2;
                var t1 = new Phasor((double)nudT1g.Value, (double)nudT1a.Value);
                var t2 = new Phasor((double)nudT2g.Value, (double)nudT2a.Value);
                if (chkAutoPlace.Checked && CurrentSource == Source.Simulator) PlaceInSimulator(t1, t2);

                Phasor s1; Phasor? s2 = null; double rpm;
                if (rbManual.Checked || CurrentSource == Source.Manual)
                {
                    s1 = new Phasor((double)nudM1a.Value, (double)nudM1p.Value);
                    if (two) s2 = new Phasor((double)nudM2a.Value, (double)nudM2p.Value);
                    rpm = machine.Rpm;
                    if (s1.Magnitude <= 0) { Warn("Ingresá la amplitud leída en el sensor 1."); return; }
                }
                else
                {
                    if (CurrentSource != Source.File && !Acquire(silent: true)) { Warn("No se pudo obtener una adquisición nueva."); return; }
                    if (CurrentSource == Source.File && !Acquire()) return;
                    if (!recording.HasPhaseReference) { Warn("La señal no tiene canal de tacómetro: sin referencia no se puede medir la fase. Usá «Ingresar lectura manual» con la lectura de tu analizador."); return; }
                    if (two && recording.Channels.Count < 2) { Warn("El balanceo en 2 planos necesita dos canales de vibración."); return; }
                    rpm = recording.MeasuredRpm();
                    s1 = recording.Reading1X(0, rpm);
                    if (two) s2 = recording.Reading1X(1, rpm);
                    nudM1a.Value = Clamp(nudM1a, s1.Magnitude); nudM1p.Value = Clamp(nudM1p, s1.AngleDeg);
                    if (s2 is Phasor q) { nudM2a.Value = Clamp(nudM2a, q.Magnitude); nudM2p.Value = Clamp(nudM2p, q.AngleDeg); }
                }

                string msg = null;
                switch (session.Step)
                {
                    case BalancingStep.Initial:
                        session.RecordInitial(s1, s2, rpm);
                        msg = "Medición original registrada.";
                        break;
                    case BalancingStep.Trial1:
                        msg = session.RecordTrial1(t1, s1, s2, rpm).Message;
                        break;
                    case BalancingStep.Trial2:
                        msg = session.RecordTrial2(t2, s1, s2 ?? default, rpm).Message;
                        break;
                    case BalancingStep.Correction:
                    case BalancingStep.Verification:
                        var (ok, trim) = session.RecordVerification(s1, s2, rpm);
                        lastTrim = trim;
                        msg = ok ? "Vibración dentro de la tolerancia: balanceo terminado." : "Todavía fuera de tolerancia: se calculó una corrección de ajuste.";
                        break;
                }
                RefreshRuns();
                ShowResult();
                UpdateStepUi();
                SetStatus(msg);
                if (msg != null && msg.Contains("poco efecto")) Warn(msg);
            }
            catch (Exception ex)
            {
                Error("No se pudo registrar la medición", ex);
            }
        }

        private Phasor[] lastTrim = Array.Empty<Phasor>();

        /// <summary>En el simulador coloca automáticamente los pesos que corresponden al paso actual.</summary>
        private void PlaceInSimulator(Phasor t1, Phasor t2)
        {
            double r = machine.CorrectionRadiusMm;
            double Phys(double a) => session.AnglesAgainstRotation ? a : -a;
            var list = simSettings.AddedMasses;
            switch (session.Step)
            {
                case BalancingStep.Initial: list.Clear(); break;
                case BalancingStep.Trial1: list.Clear(); list.Add(new PlaneMass(1, t1.Magnitude, Phys(t1.AngleDeg), r)); break;
                case BalancingStep.Trial2:
                    if (!session.KeepTrialWeights) list.Clear();
                    list.Add(new PlaneMass(2, t2.Magnitude, Phys(t2.AngleDeg), r));
                    break;
                case BalancingStep.Correction:
                    if (!session.KeepTrialWeights) list.Clear();
                    for (int p = 0; p < session.Corrections.Length; p++) list.Add(new PlaneMass(p + 1, session.Corrections[p].Magnitude, Phys(session.Corrections[p].AngleDeg), r));
                    break;
                case BalancingStep.Verification:
                    for (int p = 0; p < lastTrim.Length; p++) list.Add(new PlaneMass(p + 1, lastTrim[p].Magnitude, Phys(lastTrim[p].AngleDeg), r));
                    break;
            }
            sim = new RotorSimulator(simSettings);
            simGrid?.Refresh();
        }

        private void RefreshRuns()
        {
            lvRuns.Items.Clear();
            foreach (var r in session.Runs)
                lvRuns.Items.Add(new ListViewItem(new[] { r.Name, r.Trial is Phasor t ? $"{t.Magnitude:0.##} g ∠ {t.AngleDeg:0}° (P{r.TrialPlane})" : "—", r.Sensor1.ToString(), r.Sensor2?.ToString() ?? "—", r.Rpm.ToString("0") }));
            var colors = new[] { Color.FromArgb(90, 90, 90), Color.FromArgb(220, 120, 0), Color.FromArgb(130, 60, 200), Color.FromArgb(30, 150, 80), Color.FromArgb(14, 124, 134) };
            polar1.Clear(); polar2.Clear();
            for (int i = 0; i < session.Runs.Count; i++)
            {
                var r = session.Runs[i];
                var c = colors[Math.Min(i, colors.Length - 1)];
                polar1.AddReading(r.Name, r.Sensor1, c, i > 0 && r.Trial == null);
                if (r.Sensor2 is Phasor s2) polar2.AddReading(r.Name, s2, c, i > 0 && r.Trial == null);
            }
            for (int p = 0; p < session.Corrections.Length; p++)
                (p == 0 ? polar1 : polar2).AddMass($"Corrección P{p + 1}", session.Corrections[p], Color.FromArgb(200, 40, 40));
        }

        private void ShowResult()
        {
            if (session == null) return;
            txtResult.Clear();
            if (session.Corrections.Length == 0)
            {
                txtResult.AppendText(session.Runs.Count == 0 ? "Todavía no hay mediciones." : "Completá las corridas de prueba para calcular la corrección.");
                return;
            }
            var sb = new StringBuilder();
            string verb = session.KeepTrialWeights ? "Agregar (además del peso de prueba)" : "Agregar";
            int positions = (int)nudPositions.Value;
            for (int p = 0; p < session.Corrections.Length; p++)
            {
                var w = session.Corrections[p];
                sb.AppendLine($"Plano {p + 1}: {verb} {w.Magnitude:0.00} g a {w.AngleDeg:0.0}°  (radio {machine.CorrectionRadiusMm:0} mm)");
                if (positions >= 2)
                {
                    var sp = WeightSplitter.Split(w, positions);
                    sb.AppendLine($"   en posiciones fijas: {sp.MassA:0.00} g en la n.º {sp.PosA + 1} ({sp.AngleA:0}°) + {sp.MassB:0.00} g en la n.º {sp.PosB + 1} ({sp.AngleB:0}°)");
                }
                double depth = DrillCalculator.DepthMm(w.Magnitude, machine.Density, (double)nudDrill.Value);
                sb.AppendLine($"   o quitar: perforar {depth:0.0} mm con broca de {nudDrill.Value:0.#} mm a {DrillCalculator.DrillAngle(w.AngleDeg):0.0}°");
            }
            var pred = session.PredictedResidual();
            if (pred.Length > 0) sb.AppendLine($"Vibración residual prevista: {string.Join(" · ", pred.Select((x, i) => $"S{i + 1} {x.Magnitude:0.000}"))}");
            if (session.Two != null) sb.AppendLine($"Condición de la matriz: {session.Two.ConditionNumber:0.0} {(session.Two.ConditionNumber > 10 ? "(planos poco independientes: resultado sensible)" : "(buena)")}");
            var sens = session.Sensitivities();
            double uper = BalanceQuality.PermissibleUnbalanceGmm(machine.BalanceGrade, machine.EffectiveMassKg, machine.Rpm) / session.Planes;
            if (lastTrim.Length > 0 && session.Step == BalancingStep.Verification)
                sb.AppendLine("Ajuste (trim): " + string.Join(" · ", lastTrim.Select((x, i) => $"P{i + 1} {x.Magnitude:0.00} g ∠ {x.AngleDeg:0}°")));
            var last = session.Runs.LastOrDefault(r => r.Trial == null && r.Name.StartsWith("Verificación", StringComparison.Ordinal));
            if (last != null && sens.Length > 0)
            {
                double res1 = BalanceQuality.ResidualUnbalanceGmm(last.Sensor1.Magnitude, sens[0], machine.CorrectionRadiusMm);
                sb.AppendLine($"Desbalance residual estimado P1: {res1:0} g·mm (admisible G{machine.BalanceGrade:0.#}: {uper:0} g·mm) {(res1 <= uper ? "✓ cumple" : "✗ no cumple")}");
            }
            txtResult.AppendText(sb.ToString());
        }

        // ───────────────────────────── 5. Informe ─────────────────────────────
        private TabPage BuildReportTab()
        {
            var page = new TabPage("9. Informe");
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            txtTech = new TextBox { Dock = DockStyle.Fill };
            txtCompany = new TextBox { Dock = DockStyle.Fill };
            txtNotes = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 160, ScrollBars = ScrollBars.Vertical };
            t.Controls.Add(Lbl("Técnico:"), 0, 0); t.Controls.Add(txtTech, 1, 0);
            t.Controls.Add(Lbl("Empresa / cliente:"), 0, 1); t.Controls.Add(txtCompany, 1, 1);
            t.Controls.Add(Lbl("Observaciones:"), 0, 2); t.Controls.Add(txtNotes, 1, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            buttons.Controls.Add(Btn("Generar informe (HTML / PDF)…", (s, e) => GenerateReport()));
            buttons.Controls.Add(Btn("Guardar proyecto…", (s, e) => SaveProject(true)));
            buttons.Controls.Add(Btn("Exportar espectro (PNG)…", (s, e) => ExportSpectrumPng()));
            t.Controls.Add(buttons, 1, 3);
            t.Controls.Add(new Label { Text = "El informe se abre en el navegador: desde ahí se imprime o se guarda como PDF.", AutoSize = true }, 1, 4);
            page.Controls.Add(t);
            return page;
        }

        private void GenerateReport()
        {
            try
            {
                var rep = new HtmlReport
                {
                    Machine = machine,
                    Technician = txtTech.Text,
                    Company = txtCompany.Text,
                    Severity = lastSeverity,
                    Findings = lastFindings,
                    Balancing = session,
                    Notes = txtNotes.Text,
                };
                using var sfd = new SaveFileDialog { Filter = "Informe HTML|*.html", FileName = $"Informe_{Safe(machine.Name)}_{DateTime.Now:yyyyMMdd}.html" };
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(sfd.FileName, rep.Build(), new UTF8Encoding(false));
                OpenUrl(sfd.FileName);
                SetStatus("Informe generado: " + sfd.FileName);
            }
            catch (Exception ex) { Error("No se pudo generar el informe", ex); }
        }

        private void ExportSpectrumPng()
        {
            using var sfd = new SaveFileDialog { Filter = "PNG|*.png", FileName = "espectro.png" };
            if (sfd.ShowDialog(this) == DialogResult.OK) spectrum.ExportToPng(sfd.FileName);
        }

        // ───────────────────────────── Proyecto ─────────────────────────────
        private sealed class ProjectFile
        {
            public int Version { get; set; } = 2;
            public Machine Machine { get; set; }
            public int Planes { get; set; }
            public bool AnglesAgainstRotation { get; set; }
            public bool KeepTrialWeights { get; set; }
            public double Tolerance { get; set; }
            public List<BalancingRun> Runs { get; set; }
            public string Technician { get; set; }
            public string Company { get; set; }
            public string Notes { get; set; }
        }

        private static readonly JsonSerializerOptions ProjOpts = new JsonSerializerOptions { WriteIndented = true };

        private void SaveProject(bool askPath)
        {
            if (askPath || projectPath == null)
            {
                using var sfd = new SaveFileDialog { Filter = "Proyecto de balanceo (*.vaproj)|*.vaproj", FileName = Safe(machine.Name) + ".vaproj" };
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                projectPath = sfd.FileName;
            }
            var p = new ProjectFile
            {
                Machine = machine, Planes = session.Planes, AnglesAgainstRotation = session.AnglesAgainstRotation, KeepTrialWeights = session.KeepTrialWeights,
                Tolerance = session.ToleranceVibration, Runs = session.Runs, Technician = txtTech.Text, Company = txtCompany.Text, Notes = txtNotes.Text,
            };
            File.WriteAllText(projectPath, JsonSerializer.Serialize(p, ProjOpts), new UTF8Encoding(false));
            SetStatus("Proyecto guardado: " + projectPath);
        }

        private void OpenProject()
        {
            using var ofd = new OpenFileDialog { Filter = "Proyecto de balanceo (*.vaproj)|*.vaproj" };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var p = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(ofd.FileName), ProjOpts) ?? throw new InvalidDataException("Archivo vacío.");
                CopyMachine(p.Machine, machine);
                machineGrid.Refresh();
                cmbPlanes.SelectedIndex = p.Planes - 1;
                cmbAngles.SelectedIndex = p.AnglesAgainstRotation ? 0 : 1;
                chkKeepTrial.Checked = p.KeepTrialWeights;
                nudTolerance.Value = Clamp(nudTolerance, p.Tolerance);
                NewSession();
                foreach (var r in p.Runs ?? new List<BalancingRun>())
                {
                    if (session.Step == BalancingStep.Initial) session.RecordInitial(r.Sensor1, r.Sensor2, r.Rpm);
                    else if (session.Step == BalancingStep.Trial1 && r.TrialPlane == 1) session.RecordTrial1(r.Trial.Value, r.Sensor1, r.Sensor2, r.Rpm);
                    else if (session.Step == BalancingStep.Trial2 && r.TrialPlane == 2) session.RecordTrial2(r.Trial.Value, r.Sensor1, r.Sensor2 ?? default, r.Rpm);
                    else if (session.CanVerify) lastTrim = session.RecordVerification(r.Sensor1, r.Sensor2, r.Rpm).Trim;
                }
                txtTech.Text = p.Technician; txtCompany.Text = p.Company; txtNotes.Text = p.Notes;
                projectPath = ofd.FileName;
                UpdateMachineSummary(); RefreshRuns(); ShowResult(); UpdateStepUi();
                SetStatus("Proyecto abierto: " + projectPath);
            }
            catch (Exception ex) { Error("No se pudo abrir el proyecto", ex); }
        }

        private static void CopyMachine(Machine from, Machine to)
        {
            if (from == null) return;
            foreach (var prop in typeof(Machine).GetProperties().Where(p => p.CanWrite)) prop.SetValue(to, prop.GetValue(from));
        }

        // ───────────────────────────── Utilidades ─────────────────────────────
        private void ApplyTheme(string name)
        {
            theme = themes.GetTheme(name) ?? themes.GetTheme("Claro");
            ThemeManager.Apply(this, theme);
            foreach (var c in new ChartBase[] { trNyquist, trOrbit, trWaterfall, trHeatmap })
            {
                if (c == null) continue;
                bool dark = theme.BackColor.GetBrightness() < 0.4f;
                c.PlotBackColor = dark ? theme.ControlBackColor : Color.White;
                c.TextColor = dark ? theme.ForeColor : Color.FromArgb(29, 35, 39);
                c.GridColor = dark ? Color.FromArgb(80, 90, 95) : Color.FromArgb(215, 222, 224);
                c.LineColor = theme.AccentColor;
                c.Invalidate();
            }
            foreach (var c in new[] { trOrderPlot, trBodeAmp, trBodePhase, trendPlot })
            {
                if (c == null) continue;
                bool dark = theme.BackColor.GetBrightness() < 0.4f;
                c.PlotBackColor = dark ? theme.ControlBackColor : Color.White;
                c.TextColor = dark ? theme.ForeColor : Color.FromArgb(29, 35, 39);
                c.GridColor = dark ? Color.FromArgb(80, 90, 95) : Color.FromArgb(215, 222, 224);
                c.LineColor = theme.AccentColor;
                c.Invalidate();
            }
            if (advPlot != null)
            {
                bool dark = theme.BackColor.GetBrightness() < 0.4f;
                advPlot.PlotBackColor = dark ? theme.ControlBackColor : Color.White;
                advPlot.TextColor = dark ? theme.ForeColor : Color.FromArgb(29, 35, 39);
                advPlot.GridColor = dark ? Color.FromArgb(80, 90, 95) : Color.FromArgb(215, 222, 224);
                advPlot.LineColor = theme.AccentColor;
                advPlot.Invalidate();
            }
            foreach (var p in new[] { polar1, polar2 })
            {
                if (p == null) continue;
                bool dark = theme.BackColor.GetBrightness() < 0.4f;
                p.PlotBackColor = dark ? theme.ControlBackColor : Color.White;
                p.TextColor = dark ? theme.ForeColor : Color.FromArgb(29, 35, 39);
                p.GridColor = dark ? Color.FromArgb(80, 90, 95) : Color.FromArgb(205, 214, 216);
                p.Invalidate();
            }
        }

        private void LoadThemeFile()
        {
            using var ofd = new OpenFileDialog { Filter = "Tema (*.json)|*.json" };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            try { var t = themes.LoadTheme(ofd.FileName); themes.SaveTheme(t); ApplyTheme(t.Name); }
            catch (Exception ex) { Error("No se pudo cargar el tema", ex); }
        }

        /// <summary>Ensancha la columna de nombres de un PropertyGrid (fracción del ancho) para que no se corten los rótulos.</summary>
        private static void WideLabels(PropertyGrid grid, double fraction)
        {
            void Apply()
            {
                try
                {
                    var field = typeof(PropertyGrid).GetField("_gridView", BindingFlags.NonPublic | BindingFlags.Instance)
                             ?? typeof(PropertyGrid).GetField("gridView", BindingFlags.NonPublic | BindingFlags.Instance);
                    var view = field?.GetValue(grid);
                    view?.GetType().GetMethod("MoveSplitterTo", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.Invoke(view, new object[] { (int)(grid.Width * fraction) });
                }
                catch (Exception ex) { LogManager.Instance.LogError("PropertyGrid: " + ex.Message); }
            }
            grid.HandleCreated += (s, e) => Apply();
            grid.SizeChanged += (s, e) => Apply();
            grid.SelectedObjectsChanged += (s, e) => Apply();
        }

        private static ComboBox Combo(IEnumerable<string> items, int width)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(3, 4, 3, 3) };
            c.Items.AddRange(items.Cast<object>().ToArray());
            if (c.Items.Count > 0) c.SelectedIndex = 0;
            return c;
        }

        private static Button Btn(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 30, Padding = new Padding(8, 0, 8, 0), Margin = new Padding(3, 2, 3, 3) };
            b.Click += click;
            return b;
        }

        private static Label Lbl(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(6, 8, 0, 0) };

        private static NumericUpDown Nud(decimal min, decimal max, decimal value, int decimals) =>
            new NumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, Width = 95, Increment = decimals == 0 ? 1 : (decimal)Math.Pow(10, -Math.Min(decimals, 2)) * 10 };

        private static decimal Clamp(NumericUpDown n, double v) => Math.Max(n.Minimum, Math.Min(n.Maximum, (decimal)Math.Round(v, n.DecimalPlaces)));

        private static string Safe(string s) => string.Concat((s ?? "equipo").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        private void SetStatus(string s)
        {
            status.Text = s;
            LogManager.Instance.LogInfo(s);
        }

        private void Warn(string s)
        {
            SetStatus(s);
            MessageBox.Show(this, s, "Atención", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void Error(string what, Exception ex)
        {
            LogManager.Instance.LogError($"{what}: {ex}");
            status.Text = $"{what}: {ex.Message}";
            MessageBox.Show(this, $"{what}.\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { LogManager.Instance.LogError("No se pudo abrir " + url + ": " + ex.Message); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { liveTimer.Dispose(); linkTimer.Dispose(); port?.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>Opciones para interpretar un archivo WAV (escala del sensor y canal del tacómetro).</summary>
    public sealed class WavOptionsForm : Form
    {
        private readonly NumericUpDown nudScale = new NumericUpDown { DecimalPlaces = 3, Minimum = 0.001m, Maximum = 100000m, Value = 1m, Width = 120 };
        private readonly ComboBox cmbUnit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly NumericUpDown nudTach = new NumericUpDown { Minimum = 0, Maximum = 8, Value = 0, Width = 60 };
        public double ScaleFactor => (double)nudScale.Value;
        public VibrationUnit Unit => cmbUnit.SelectedIndex switch { 0 => VibrationUnit.AccelerationG, 1 => VibrationUnit.VelocityMmS, _ => VibrationUnit.DisplacementUm };
        public int TachChannel => (int)nudTach.Value - 1;

        public WavOptionsForm()
        {
            Text = "Opciones del archivo WAV";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(12); Font = new Font("Segoe UI", 9.5f);
            cmbUnit.Items.AddRange(new object[] { "Aceleración (g)", "Velocidad (mm/s)", "Desplazamiento (µm)" });
            cmbUnit.SelectedIndex = 0;
            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            t.Controls.Add(new Label { Text = "Unidades a fondo de escala (valor físico para ±1,0):", AutoSize = true, MaximumSize = new Size(260, 0) }, 0, 0); t.Controls.Add(nudScale, 1, 0);
            t.Controls.Add(new Label { Text = "Unidad:", AutoSize = true }, 0, 1); t.Controls.Add(cmbUnit, 1, 1);
            t.Controls.Add(new Label { Text = "Canal del tacómetro (0 = ninguno):", AutoSize = true }, 0, 2); t.Controls.Add(nudTach, 1, 2);
            var ok = new Button { Text = "Aceptar", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
            var f = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            f.Controls.Add(cancel); f.Controls.Add(ok);
            t.Controls.Add(f, 0, 3); t.SetColumnSpan(f, 2);
            Controls.Add(t);
            AcceptButton = ok; CancelButton = cancel;
        }
    }
}
