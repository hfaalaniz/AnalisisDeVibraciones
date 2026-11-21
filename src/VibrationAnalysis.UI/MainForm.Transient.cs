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
    /// <summary>Pestaña 5: órdenes y transitorios (espectro de órdenes, Bode, Nyquist, cascada, espectrograma y órbita).</summary>
    public sealed partial class MainForm
    {
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
    }
}
