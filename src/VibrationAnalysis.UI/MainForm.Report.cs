using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using VibrationAnalysis.Core.Analysis;
using VibrationAnalysis.Core.Dsp;
using VibrationAnalysis.Core.Report;
using VibrationAnalysis.Core.Trending;

namespace VibrationAnalysis.UI
{
    /// <summary>Datos del informe que se recuerdan entre sesiones.</summary>
    public sealed class ReportSettings
    {
        public string Technician { get; set; } = "";
        public string Company { get; set; } = "";
        public string LogoPath { get; set; } = "";
        public int Year { get; set; } = DateTime.Now.Year;
        public int NextNumber { get; set; } = 1;
        public bool[] Sections { get; set; } = { true, false, false, true, true, true, true, true };
    }

    public sealed partial class MainForm
    {
        private static readonly string[] ReportSections =
        {
            "Señal y espectro de la medición actual (pestaña 2)",
            "Gráfico actual del análisis avanzado (pestaña 4)",
            "Gráfico actual de órdenes y transitorios (pestaña 5)",
            "Indicadores y alarmas de la medición",
            "Tendencias del punto elegido en la pestaña 6",
            "Corriente del motor (MCSA), si la señal es de corriente",
            "Balanceo",
            "Firmas (técnico y cliente)",
        };

        private ReportSettings repSettings = new ReportSettings();
        private TextBox txtClient, txtReportNo, txtWorkOrder, txtRecommendations;
        private PictureBox picLogo;
        private CheckedListBox clbSections;

        private static string ReportSettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnalisisVibraciones", "informe.json");

        private TabPage BuildReportTab()
        {
            try { if (File.Exists(ReportSettingsPath)) repSettings = JsonSerializer.Deserialize<ReportSettings>(File.ReadAllText(ReportSettingsPath)) ?? new ReportSettings(); }
            catch (Exception ex) { LogManager.Instance.LogError("Config. del informe: " + ex.Message); }
            if (repSettings.Year != DateTime.Now.Year) { repSettings.Year = DateTime.Now.Year; repSettings.NextNumber = 1; }

            var page = new TabPage("9. Informe");
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2 };
            split.HandleCreated += (s, e) => { try { split.SplitterDistance = Math.Max(420, split.Width - 430); } catch { } };

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoScroll = true };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            txtTech = new TextBox { Dock = DockStyle.Fill, Text = repSettings.Technician };
            txtCompany = new TextBox { Dock = DockStyle.Fill, Text = repSettings.Company };
            txtClient = new TextBox { Dock = DockStyle.Fill };
            txtReportNo = new TextBox { Width = 140, Text = NextReportNumber() };
            txtWorkOrder = new TextBox { Width = 140 };
            txtNotes = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 110, ScrollBars = ScrollBars.Vertical };
            txtRecommendations = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 110, ScrollBars = ScrollBars.Vertical };
            picLogo = new PictureBox { Width = 180, Height = 60, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
            LoadLogoPreview();
            int r = 0;
            void Add(string label, Control c) { t.Controls.Add(Lbl(label), 0, r); t.Controls.Add(c, 1, r); r++; }
            Add("Técnico:", txtTech);
            Add("Empresa (emisor):", txtCompany);
            Add("Cliente:", txtClient);
            Add("N.º de informe:", Group(txtReportNo, Lbl("Orden de trabajo:"), txtWorkOrder));
            Add("Logo:", Group(picLogo, Btn("Elegir…", (s, e) => ChooseLogo()), Btn("Quitar", (s, e) => { repSettings.LogoPath = ""; LoadLogoPreview(); SaveReportSettings(); })));
            Add("Recomendaciones:", txtRecommendations);
            t.Controls.Add(Btn("Sugerir desde el diagnóstico", (s, e) => SuggestRecommendations()), 1, r++);
            Add("Observaciones:", txtNotes);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            buttons.Controls.Add(Btn("Vista previa", (s, e) => PreviewReport()));
            buttons.Controls.Add(Btn("Guardar PDF…", (s, e) => SaveReport(true)));
            buttons.Controls.Add(Btn("Guardar HTML…", (s, e) => SaveReport(false)));
            buttons.Controls.Add(Btn("Guardar proyecto…", (s, e) => SaveProject(true)));
            buttons.Controls.Add(Btn("Exportar espectro (PNG)…", (s, e) => ExportSpectrumPng()));
            t.Controls.Add(buttons, 1, r++);
            split.Panel1.Controls.Add(t);

            var box = new GroupBox { Text = "Secciones del informe", Dock = DockStyle.Fill, Padding = new Padding(8) };
            clbSections = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false };
            for (int i = 0; i < ReportSections.Length; i++) clbSections.Items.Add(ReportSections[i], i < repSettings.Sections.Length && repSettings.Sections[i]);
            var help = new Label
            {
                Dock = DockStyle.Bottom, Height = 120, Padding = new Padding(2, 8, 2, 2),
                Text = "Siempre incluye: encabezado, resumen con estado general y recomendaciones, máquina, severidad y diagnóstico.\n\n" +
                       "Los gráficos de las pestañas 4 y 5 salen tal como están en pantalla. Las tendencias usan el punto y el indicador elegidos en la pestaña 6, más los indicadores en alarma.\n\n" +
                       "«Guardar PDF» usa Microsoft Edge (incluido en Windows) sin abrir ventanas.",
            };
            box.Controls.Add(clbSections); box.Controls.Add(help);
            split.Panel2.Controls.Add(box);
            page.Controls.Add(split);
            return page;
        }

        private string NextReportNumber() => $"{repSettings.Year}-{repSettings.NextNumber:000}";

        private void SaveReportSettings()
        {
            repSettings.Technician = txtTech.Text.Trim();
            repSettings.Company = txtCompany.Text.Trim();
            repSettings.Sections = Enumerable.Range(0, ReportSections.Length).Select(i => clbSections.GetItemChecked(i)).ToArray();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportSettingsPath));
                File.WriteAllText(ReportSettingsPath, JsonSerializer.Serialize(repSettings, ProjOpts), new UTF8Encoding(false));
            }
            catch (Exception ex) { LogManager.Instance.LogError("Guardar config. del informe: " + ex.Message); }
        }

        private void ChooseLogo()
        {
            using var ofd = new OpenFileDialog { Filter = "Imagen (PNG, JPG)|*.png;*.jpg;*.jpeg" };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            if (new FileInfo(ofd.FileName).Length > 2_000_000) { Warn("El logo debe pesar menos de 2 MB."); return; }
            repSettings.LogoPath = ofd.FileName;
            LoadLogoPreview();
            SaveReportSettings();
        }

        private void LoadLogoPreview()
        {
            picLogo.Image?.Dispose();
            picLogo.Image = null;
            try { if (File.Exists(repSettings.LogoPath)) using (var img = Image.FromFile(repSettings.LogoPath)) picLogo.Image = new Bitmap(img); }
            catch (Exception ex) { LogManager.Instance.LogError("Logo: " + ex.Message); }
        }

        private void SuggestRecommendations()
        {
            var tmp = new HtmlReport { Machine = machine, Findings = lastFindings, Severity = lastSeverity };
            txtRecommendations.Text = string.Join(Environment.NewLine, tmp.RecommendationList().Select(x => "- " + x));
        }

        private bool Section(int i) => clbSections.GetItemChecked(i);

        private static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

        /// <summary>Arma el informe con las secciones elegidas.</summary>
        private HtmlReport BuildReport()
        {
            var figures = new List<ReportFigure>();
            MeasurementRecord indicators = null;
            IReadOnlyList<AlarmResult> alarms = Array.Empty<AlarmResult>();
            var trends = new List<ReportTrend>();
            McsaResult mcsa = null;
            var ch = recording != null && cmbChannel.SelectedIndex >= 0 ? recording.Channels[cmbChannel.SelectedIndex] : null;
            double f1 = CurrentRpm() / 60;

            if (ch != null && Section(0) && !SignalTools.IsElectrical(ch.Unit)) figures.AddRange(SignalFigures(ch, f1));
            if (Section(1) && advPlot != null && advPlot.XValues.Length > 1) figures.Add(PlotFigure(advPlot, "Análisis avanzado"));
            if (Section(2) && trHost != null)
            {
                var vis = trHost.Controls.Cast<Control>().FirstOrDefault(c => c.Visible);
                if (vis is XYPlotControl xy && xy.XValues.Length > 1) figures.Add(PlotFigure(xy, "Órdenes y transitorios"));
                else if (vis is TableLayoutPanel) { if (trBodeAmp.XValues.Length > 1) { figures.Add(PlotFigure(trBodeAmp, "Bode · amplitud")); figures.Add(PlotFigure(trBodePhase, "Bode · fase")); } }
                else if (vis != null && vis.Width > 10) figures.Add(new ReportFigure(lblTrSummary.Text.Split('\n')[0], PngDataUri: Snapshot(vis)));
            }

            MachineAsset asset = null; MeasurementPoint point = null;
            if (trendDb != null && tvAssets != null) { var sel = Selection(); asset = sel.Machine; point = sel.Point; }

            if (ch != null && Section(3))
            {
                indicators = FeatureExtractor.Extract(recording, cmbChannel.SelectedIndex, machine);
                var pt = point ?? new MeasurementPoint();
                var hist = point != null ? trendDb.Measurements(point.Id) : new List<MeasurementRecord>();
                alarms = TrendAnalysis.Evaluate(indicators, pt, machine.MachineClass, hist);
            }

            if (Section(4) && point != null)
            {
                var hist = trendDb.Measurements(point.Id);
                if (hist.Count > 0)
                {
                    var last = TrendAnalysis.Evaluate(hist[^1], point, asset.Machine.MachineClass, hist);
                    var keys = new List<string> { SelectedFeature, FeatureKeys.VelocityRms };
                    keys.AddRange(last.Where(a => a.Level != AlarmLevel.Normal).Select(a => a.Feature));
                    foreach (var key in keys.Distinct().Where(k => hist.Any(h => !double.IsNaN(h.Get(k)))).Take(4))
                        trends.Add(TrendSection(asset, point, hist, key, key == keys[0] ? last : Array.Empty<AlarmResult>()));
                }
            }

            if (ch != null && Section(5) && ch.Unit == VibrationUnit.CurrentA)
            {
                double rpm = recording.HasPhaseReference ? recording.MeasuredRpm() : machine.Rpm;
                try
                {
                    mcsa = MotorCurrentAnalysis.Analyze(ch.Data, recording.SampleRate, machine.LineFrequencyHz > 0 ? machine.LineFrequencyHz : 50, machine.Poles, rpm);
                    var z = mcsa.Zoom;
                    double refA = Math.Max(mcsa.LineAmplitude, 1e-12);
                    var spec = new SvgChartSpec
                    {
                        Title = $"MCSA · «{ch.Name}» · zoom alrededor de la red", XLabel = "Frecuencia (Hz)", YLabel = "dB respecto de la red",
                        Series = { new SvgSeries(z.Frequencies, z.Amplitudes.Select(a => 20 * Math.Log10(Math.Max(a, refA * 1e-6) / refA)).ToArray()) },
                        YMin = -100,
                    };
                    spec.Markers.Add((mcsa.LineHz, "red", "#c8960a"));
                    if (!double.IsNaN(mcsa.Slip)) { double sb = 2 * mcsa.Slip * mcsa.LineHz; spec.Markers.Add((mcsa.LineHz - sb, "f(1−2s)", "#c62828")); spec.Markers.Add((mcsa.LineHz + sb, "f(1+2s)", "#c62828")); }
                    figures.Add(new ReportFigure("Corriente del motor", SvgChart.Render(spec)));
                }
                catch (ArgumentException ex) { LogManager.Instance.LogError("MCSA en el informe: " + ex.Message); }
            }

            string logo = "";
            try { if (File.Exists(repSettings.LogoPath)) logo = HtmlReport.ToDataUri(File.ReadAllBytes(repSettings.LogoPath)); } catch { }

            return new HtmlReport
            {
                Machine = machine, Technician = txtTech.Text.Trim(), Company = txtCompany.Text.Trim(), Client = txtClient.Text.Trim(),
                ReportNumber = txtReportNo.Text.Trim(), WorkOrder = txtWorkOrder.Text.Trim(), LogoDataUri = logo,
                Severity = ch != null && !SignalTools.IsElectrical(ch.Unit) ? lastSeverity : null, Findings = lastFindings,
                Balancing = Section(6) ? session : null, Notes = txtNotes.Text.Trim(), Recommendations = txtRecommendations.Text,
                Figures = figures, Indicators = indicators, Alarms = alarms, Trends = trends, Mcsa = mcsa, Signatures = Section(7),
            };
        }

        private IEnumerable<ReportFigure> SignalFigures(VibrationAnalysis.Core.IO.Channel ch, double f1)
        {
            double fs = recording.SampleRate;
            var unit = ViewUnit;
            string u = SignalTools.Symbol(unit);
            bool fromAcc = ch.Unit is VibrationUnit.AccelerationG or VibrationUnit.AccelerationMs2;
            var x = SignalTools.Convert(ch.Data, fs, ch.Unit, unit, unit == VibrationUnit.AccelerationG && fromAcc ? 0 : 2.0);
            int n = Math.Min(x.Length, (int)(fs * Math.Max(0.2, Math.Min(1.0, f1 > 0 ? 8 / f1 : 0.5))));
            var tAxis = Enumerable.Range(0, n).Select(i => i / fs * 1000).ToArray();
            yield return new ReportFigure("Forma de onda", SvgChart.Render(new SvgChartSpec
            {
                Title = $"Forma de onda · «{ch.Name}»", XLabel = "Tiempo (ms)", YLabel = u, Series = { new SvgSeries(tAxis, x.Take(n).ToArray()) }, YMin = x.Take(n).DefaultIfEmpty(0).Min(), Height = 200,
            }), $"{n / fs * 1000:0} ms de {recording.Duration:0.0} s registrados a {fs:0} Hz");

            var sp = SpectrumAnalyzer.Compute(x, fs, ViewWindow);
            double maxF = Math.Min(fs / 2, Math.Max(500, 25 * f1));
            var spec = new SvgChartSpec { Title = $"Espectro · «{ch.Name}»", XLabel = "Frecuencia (Hz)", YLabel = $"{u} pico", Series = { new SvgSeries(sp.Frequencies, sp.Amplitudes) }, XMin = 0, XMax = maxF };
            var cat = FrequencyCatalog.Build(f1, machine.Bearing, machine.Blades, machine.GearTeeth, machine.LineFrequencyHz);
            foreach (var mk in cat.Where(c => c.Group is "Giro" or "Rodamiento" or "Engranaje" && c.Frequency <= maxF && !c.Label.Contains('×')).Take(14))
                spec.Markers.Add((mk.Frequency, mk.Label, Hex(MarkerColor(mk.Group))));
            yield return new ReportFigure("Espectro", SvgChart.Render(spec), $"Ventana {WindowFunctions.DisplayName(ViewWindow)}, resolución {sp.Resolution:0.###} Hz");

            if (machine.Bearing != null)
            {
                var acc = SignalTools.Convert(ch.Data, fs, ch.Unit, VibrationUnit.AccelerationG, fromAcc ? 0 : 2.0);
                Spectrum env = null;
                (double Low, double High) band = default;
                try { band = AdvancedSpectrum.AutoEnvelopeBand(acc, fs, f1, machine.Bearing.BpfiOrder * f1); env = AdvancedSpectrum.EnvelopeSpectrum(acc, fs, band.Low, band.High); }
                catch (ArgumentException) { }
                if (env != null)
                {
                    double maxE = Math.Min(fs / 2, Math.Max(300, 4.5 * machine.Bearing.BpfiOrder * f1));
                    var es = new SvgChartSpec { Title = "Espectro de envolvente (rodamientos)", XLabel = "Frecuencia (Hz)", YLabel = "g pico", Series = { new SvgSeries(env.Frequencies, env.Amplitudes, "#7b3fa0") }, XMin = 0, XMax = maxE };
                    foreach (var (name, o) in new[] { ("BPFO", machine.Bearing.BpfoOrder), ("BPFI", machine.Bearing.BpfiOrder), ("BSF", machine.Bearing.BsfOrder), ("FTF", machine.Bearing.FtfOrder) })
                        es.Markers.Add((o * f1, name, "#be3ca0"));
                    yield return new ReportFigure("Envolvente", SvgChart.Render(es), $"Banda {band.Low:0}–{band.High:0} Hz elegida por curtosis espectral");
                }
            }
        }

        private static ReportFigure PlotFigure(XYPlotControl p, string fallbackTitle)
        {
            var y = p.LogY ? p.YValues.Select(v => 20 * Math.Log10(Math.Max(Math.Abs(v), p.DbReference * 1e-9) / p.DbReference)).ToArray() : p.YValues;
            var spec = new SvgChartSpec
            {
                Title = string.IsNullOrEmpty(p.Title) ? fallbackTitle : p.Title, XLabel = p.XLabel, YLabel = p.LogY ? p.YLabel + " (dB)" : p.YLabel,
                Series = { new SvgSeries(p.XValues, y, Hex(p.LineColor), Points: p.ShowPoints) }, XMin = p.ViewMin, XMax = p.ViewMax, XFormat = p.XFormatter,
                YMin = p.LogY ? y.Where(double.IsFinite).DefaultIfEmpty(0).Min() : null,
            };
            foreach (var m in p.Markers) spec.Markers.Add((m.X, m.Label, Hex(m.Color)));
            foreach (var h in p.HorizontalLines) spec.HLines.Add((h.Y, h.Label, Hex(h.Color)));
            return new ReportFigure(spec.Title, SvgChart.Render(spec));
        }

        private ReportTrend TrendSection(MachineAsset asset, MeasurementPoint point, IReadOnlyList<MeasurementRecord> hist, string key, IReadOnlyList<AlarmResult> lastAlarms)
        {
            var series = TrendAnalysis.Series(hist, key);
            var spec = new SvgChartSpec
            {
                Title = FeatureKeys.Name(key), XLabel = "Fecha", YLabel = FeatureKeys.Name(key), Height = 220,
                Series = { new SvgSeries(series.Select(s => s.Time.ToOADate()).ToArray(), series.Select(s => s.Value).ToArray(), "#0e7c86", Points: true) },
                XFormat = d => DateTime.FromOADate(d).ToString("dd/MM/yy", Es),
            };
            if (series.Count == 1) spec = new SvgChartSpec { Title = spec.Title, XLabel = spec.XLabel, YLabel = spec.YLabel, Height = 220, Series = spec.Series, XFormat = spec.XFormat, XMin = series[0].Time.AddDays(-15).ToOADate(), XMax = series[0].Time.AddDays(15).ToOADate() };
            var rule = point.Alarms.FirstOrDefault(a => a.Enabled && a.Feature == key);
            string forecast = "";
            if (rule != null)
            {
                var (alert, danger) = TrendAnalysis.Limits(rule, asset.Machine.MachineClass, TrendAnalysis.Baseline(point, hist));
                if (double.IsFinite(alert)) spec.HLines.Add((alert, $"Alerta {FormatValue(alert)}", "#d28a00"));
                if (double.IsFinite(danger)) spec.HLines.Add((danger, $"Peligro {FormatValue(danger)}", "#c62828"));
                var fit = TrendAnalysis.Fit(series);
                if (fit != null && !FeatureKeys.LowerIsWorse(key))
                    foreach (var (lim, name) in new[] { (alert, "alerta"), (danger, "peligro") })
                        if (double.IsFinite(lim) && series[^1].Value < lim && fit.Crossing(lim) is DateTime when && when > DateTime.Now)
                        { forecast = $"al ritmo actual llega a {name} ≈ {when:dd/MM/yyyy}"; break; }
            }
            return new ReportTrend($"{asset.Name} · {point.Name}", key, SvgChart.Render(spec), forecast, lastAlarms);
        }

        private static string Snapshot(Control c)
        {
            using var bmp = new Bitmap(c.Width, c.Height);
            c.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return HtmlReport.ToDataUri(ms.ToArray());
        }

        private void PreviewReport()
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), $"Informe_vista_previa_{DateTime.Now:HHmmss}.html");
                File.WriteAllText(path, BuildReport().Build(), new UTF8Encoding(false));
                OpenUrl(path);
                SaveReportSettings();
            }
            catch (Exception ex) { Error("No se pudo armar el informe", ex); }
        }

        private void SaveReport(bool pdf)
        {
            string name = $"Informe_{Safe(txtReportNo.Text.Trim())}_{Safe(machine.Name)}";
            using var sfd = new SaveFileDialog { Filter = pdf ? "PDF|*.pdf" : "Informe HTML|*.html", FileName = name + (pdf ? ".pdf" : ".html") };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                var html = BuildReport().Build();
                if (!pdf) File.WriteAllText(sfd.FileName, html, new UTF8Encoding(false));
                else
                {
                    var tmp = Path.Combine(Path.GetTempPath(), $"informe_{Guid.NewGuid():N}.html");
                    File.WriteAllText(tmp, html, new UTF8Encoding(false));
                    try { HtmlToPdf(tmp, sfd.FileName); }
                    finally { try { File.Delete(tmp); } catch { } }
                }
                if (txtReportNo.Text.Trim() == NextReportNumber()) { repSettings.NextNumber++; txtReportNo.Text = NextReportNumber(); }
                SaveReportSettings();
                SetStatus((pdf ? "PDF" : "Informe") + " guardado: " + sfd.FileName);
                OpenUrl(sfd.FileName);
            }
            catch (Exception ex) { Error("No se pudo guardar el informe", ex); }
            finally { Cursor = Cursors.Default; }
        }

        /// <summary>Convierte el HTML a PDF con Microsoft Edge (o Chrome) en modo sin ventana.</summary>
        private static void HtmlToPdf(string htmlPath, string pdfPath)
        {
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var browser = new[]
            {
                Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"), Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
            }.FirstOrDefault(File.Exists);
            if (browser == null) throw new FileNotFoundException("No se encontró Microsoft Edge ni Chrome para generar el PDF. Usá «Guardar HTML» e imprimí a PDF desde el navegador.");
            if (File.Exists(pdfPath)) File.Delete(pdfPath);
            var profile = Path.Combine(Path.GetTempPath(), "AnalisisVibraciones_pdf");
            var psi = new ProcessStartInfo(browser)
            {
                UseShellExecute = false, CreateNoWindow = true,
                Arguments = $"--headless=new --disable-gpu --no-first-run --no-pdf-header-footer --print-to-pdf-no-header --user-data-dir=\"{profile}\" --print-to-pdf=\"{pdfPath}\" \"{new Uri(htmlPath).AbsoluteUri}\"",
            };
            using var p = Process.Start(psi);
            if (!p.WaitForExit(60000)) { try { p.Kill(true); } catch { } throw new TimeoutException("El navegador tardó demasiado en generar el PDF."); }
            for (int i = 0; i < 20 && (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length == 0); i++) System.Threading.Thread.Sleep(100);
            if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length == 0) throw new IOException("El navegador no generó el PDF.");
        }
    }
}
