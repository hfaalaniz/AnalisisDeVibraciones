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
    /// <summary>Pestaña 3: severidad ISO y diagnóstico de fallas.</summary>
    public sealed partial class MainForm
    {
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
    }
}
