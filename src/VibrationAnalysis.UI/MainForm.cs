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
using VibrationAnalysis.Core.Analysis;
using VibrationAnalysis.Core.Balancing;
using VibrationAnalysis.Core.Dsp;
using VibrationAnalysis.Core.IO;
using VibrationAnalysis.Core.Model;
using VibrationAnalysis.Core.Report;
using VibrationAnalysis.Core.Simulation;

namespace VibrationAnalysis.UI
{
    /// <summary>
    /// Ventana principal. Flujo en pestañas numeradas: 1 Máquina → 2 Adquisición → 3 Diagnóstico → 4 Análisis avanzado → 5 Balanceo → 6 Informe.
    /// Toda la matemática está en VibrationAnalysis.Core (con pruebas); acá solo hay interfaz.
    /// </summary>
    public sealed class MainForm : Form
    {
        private enum Source { Simulator, File, Serial, Manual }

        private const string WebUrl = "https://analisisdevibraciones.pages.dev/";
        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

        private readonly Machine machine = new Machine { Name = "Ventilador de prueba", Rpm = 1500, DiameterMm = 450, LengthMm = 220, CorrectionRadiusMm = 200, MassKg = 38 };
        private readonly SimulatorSettings simSettings = new SimulatorSettings();
        private RotorSimulator sim;
        private Recording recording;
        private BalancingSession session;
        private SerialPort port;
        private readonly SerialStreamParser serialParser = new SerialStreamParser();
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
            tabs.TabPages.Add(BuildBalancingTab());
            tabs.TabPages.Add(BuildReportTab());

            Controls.Add(tabs);
            Controls.Add(statusStrip);
            Controls.Add(menu);
            MainMenuStrip = menu;

            tabs.SelectedIndexChanged += (s, e) => { if (tabs.SelectedTab == advPage) RunAdvancedAnalysis(); };
            liveTimer.Tick += (s, e) => { if (chkLive.Checked) Acquire(silent: true); };
            FormClosing += (s, e) => { liveTimer.Stop(); ClosePort(); };

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
            cmbSource = Combo(new[] { "Simulador", "Archivo CSV / WAV", "Puerto serie (sensor)", "Ingreso manual (solo balanceo)" }, 170);
            cmbSource.SelectedIndexChanged += (s, e) => OnSourceChanged();
            btnAcquire = Btn("Adquirir", (s, e) => Acquire());
            chkLive = new CheckBox { Text = "En vivo (1 s)", AutoSize = true, Margin = new Padding(8, 8, 8, 0) };
            chkLive.CheckedChanged += (s, e) => { if (chkLive.Checked) liveTimer.Start(); else liveTimer.Stop(); };
            cmbPort = Combo(SerialPort.GetPortNames(), 90);
            cmbBaud = Combo(new[] { "115200", "230400", "460800", "921600", "500000", "1000000" }, 90);
            btnConnect = Btn("Conectar", (s, e) => ToggleSerial());
            cmbChannel = Combo(new string[0], 110);
            cmbChannel.SelectedIndexChanged += (s, e) => RefreshAnalysis();
            cmbUnitView = Combo(new[] { "Velocidad (mm/s)", "Aceleración (g)", "Desplazamiento (µm)" }, 150);
            cmbUnitView.SelectedIndexChanged += (s, e) => RefreshAnalysis();
            cmbWindow = Combo(new[] { "Hanning", "Flat-top", "Hamming", "Rectangular" }, 100);
            cmbWindow.SelectedIndexChanged += (s, e) => RefreshAnalysis();
            btnExportCsv = Btn("Exportar CSV", (s, e) => ExportCsv());
            top.Controls.AddRange(new Control[] { Lbl("Fuente:"), cmbSource, btnAcquire, chkLive, Lbl("Puerto:"), cmbPort, cmbBaud, btnConnect, Lbl("Canal:"), cmbChannel, Lbl("Ver:"), cmbUnitView, Lbl("Ventana:"), cmbWindow, btnExportCsv });

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
            cmbPort.Enabled = cmbBaud.Enabled = btnConnect.Enabled = src == Source.Serial;
            chkLive.Enabled = src is Source.Simulator or Source.Serial;
            btnAcquire.Text = src switch { Source.File => "Abrir archivo…", Source.Serial => "Capturar", Source.Manual => "—", _ => "Adquirir" };
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
                        r = serialParser.ToRecording(machine.Rpm);
                        if (r.Length < 256) { if (!silent) Warn("Todavía no llegaron suficientes muestras del sensor."); return false; }
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
                string u = SignalTools.Symbol(ViewUnit);
                string oneX = recording.HasPhaseReference ? recording.Reading1X(cmbChannel.SelectedIndex).ToString() + " (" + SignalTools.Symbol(ch.Unit) + " pico ∠ fase)" : "sin tacómetro: sin fase";
                lblStats.Text = $"RMS {st.Rms:0.000} {u}   Pico {st.Peak:0.000}   Pico-pico {st.PeakToPeak:0.000}   Factor de cresta {st.CrestFactor:0.00}   Curtosis {st.Kurtosis:0.00}\n" +
                                $"Velocidad {rpm:0.0} rpm ({rpm / 60:0.00} Hz)   1X: {oneX}   Resolución {sp.Resolution:0.###} Hz   {recording.Duration:0.00} s";
                UpdateDiagnosis();
                if (tabs.SelectedTab == advPage) RunAdvancedAnalysis();
            }
            catch (Exception ex)
            {
                Error("Error al analizar la señal", ex);
            }
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
                    try { var chunk = port.ReadExisting(); lock (serialParser) serialParser.Feed(chunk); } catch (Exception ex) { LogManager.Instance.LogError("Serie: " + ex.Message); }
                };
                port.Open();
                port.WriteLine("START");
                btnConnect.Text = "Desconectar";
                SetStatus($"Conectado a {port.PortName}. Esperando la cabecera #VA del sensor…");
            }
            catch (Exception ex) { Error("No se pudo abrir el puerto", ex); ClosePort(); }
        }

        private void ClosePort()
        {
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
        private static readonly string[] AdvModes = { "Espectro promediado", "Densidad espectral (PSD)", "Zoom FFT", "Envolvente (demodulación)", "Cepstrum", "Curtosis espectral" };

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
            top.Controls.AddRange(new Control[]
            {
                Lbl("Análisis:"), cmbAdvMode, Lbl("Magnitud:"), cmbAdvUnit, Lbl("Ventana:"), cmbAdvWindow, pnlAdvBlock, pnlAdvBand, pnlAdvZoom,
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
            cmbAdvUnit.Enabled = m is 0 or 1 or 2 or 4;
            chkAdvLog.Enabled = m is 0 or 1 or 2 or 3;
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

        // ───────────────────────────── 5. Balanceo ─────────────────────────────
        private TabPage BuildBalancingTab()
        {
            var page = new TabPage("5. Balanceo");
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
            var page = new TabPage("6. Informe");
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
            if (disposing) { liveTimer.Dispose(); port?.Dispose(); }
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
