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
        private const string ManualUrl = "https://github.com/hfaalaniz/AnalisisDeVibraciones/blob/main/docs/MANUAL.md";
        private const string ManualBaseUrl = "https://github.com/hfaalaniz/AnalisisDeVibraciones/blob/main/docs";
        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

        /// <summary>Versión de la app tomada del ensamblado (sin el hash del commit), p. ej. «3.0.0».</summary>
        private static string AppVersion => Application.ProductVersion.Split('+')[0];

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

        public MainForm(string startupFile = null)
        {
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* sin ícono embebido */ }
            if (!string.IsNullOrWhiteSpace(startupFile) && File.Exists(startupFile))
                Shown += (s, e) => { if (startupFile.EndsWith(".vaproj", StringComparison.OrdinalIgnoreCase)) OpenProject(startupFile); };
            Text = $"Análisis de Vibraciones y Balanceo {AppVersion} · Fabian Alaniz – ElectroNet";
            HtmlReport.ProductVersion = AppVersion;
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
            file.DropDownItems.Add("Guardar informe PDF…", null, (s, e) => SaveReport(true));
            file.DropDownItems.Add("Vista previa del informe", null, (s, e) => PreviewReport());
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
            help.DropDownItems.Add("Manual de usuario", null, (s, e) => OpenManual());
            help.DropDownItems.Add("Versión web", null, (s, e) => OpenUrl(WebUrl));
            help.DropDownItems.Add("Carpeta de registros (logs)", null, (s, e) => OpenUrl(Path.GetDirectoryName(LogManager.Instance.LogFilePath)));
            help.DropDownItems.Add("Acerca de…", null, (s, e) => MessageBox.Show(this,
                $"Análisis de Vibraciones y Balanceo {AppVersion}\n\nAutor: Fabian Alaniz – ElectroNet\n+54 9 351 284-8802 · www.developers-soft.com\n\n" +
                "• Espectro, severidad ISO 20816 y diagnóstico de fallas (desbalance, desalineación, holgura, rodamientos por envolvente, engranajes).\n" +
                "• Análisis avanzado: PSD, zoom FFT, cepstrum, envolvente, curtosis espectral.\n" +
                "• Órdenes y transitorios: Bode, Nyquist, cascada, espectrograma, órbitas y velocidades críticas.\n" +
                "• Tendencias y alarmas con pronóstico, rutas de medición.\n" +
                "• Corriente del motor (MCSA), ultrasonido y temperatura.\n" +
                "• Sensores USB/WiFi (ESP32), placa de sonido y NI-DAQmx con IEPE; MQTT y Modbus TCP/RTU.\n" +
                "• Balanceo de campo en 1 y 2 planos por coeficientes de influencia (ISO 1940/21940).\n\n" +
                $"Versión completa: {Application.ProductVersion}",
                "Acerca de", MessageBoxButtons.OK, MessageBoxIcon.Information));
            m.Items.AddRange(new ToolStripItem[] { file, view, help });
            return m;
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

        /// <summary>Abre el manual instalado junto al programa (convertido a HTML); si no está, el de GitHub.</summary>
        private static void OpenManual()
        {
            try
            {
                string md = Path.Combine(AppContext.BaseDirectory, "MANUAL.md");
                if (File.Exists(md))
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnalisisVibraciones");
                    Directory.CreateDirectory(dir);
                    string html = Path.Combine(dir, "manual.html");
                    File.WriteAllText(html, MarkdownHtml.ToPage(File.ReadAllText(md), "Manual de usuario — Análisis de Vibraciones", ManualBaseUrl), new UTF8Encoding(false));
                    OpenUrl(html);
                    return;
                }
            }
            catch (Exception ex) { LogManager.Instance.LogError("No se pudo preparar el manual local: " + ex.Message); }
            OpenUrl(ManualUrl);
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
}
