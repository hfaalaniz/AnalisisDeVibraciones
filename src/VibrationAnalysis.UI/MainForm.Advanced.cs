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
    /// <summary>Pestaña 4: análisis avanzado (promedios, PSD, zoom, envolvente, cepstrum, curtosis espectral, MCSA y ultrasonido).</summary>
    public sealed partial class MainForm
    {
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
            lblAdvSummary.Text = $"Rotor: {m.RotorCondition} (banda inferior a {(double.IsFinite(m.LowerSidebandDb) ? m.LowerSidebandDb.ToString("0.0", Es) + " dB" : "—")}) · THD {m.ThdPercent:0.0} %{unb}\n" +
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
            lblAdvSummary.Text = $"Ultrasonido {u.BandLow / 1000:0}–{u.BandHigh / 1000:0} kHz: {u.RmsDbuV:0.0} dBµV RMS, pico {u.PeakDbuV:0.0} dBµV, cresta {u.CrestFactor:0.0}.\n" +
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
    }
}
