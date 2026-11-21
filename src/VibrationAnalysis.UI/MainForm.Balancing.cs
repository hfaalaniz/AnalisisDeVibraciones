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
    /// <summary>Pestaña 8: balanceo de campo en 1 y 2 planos.</summary>
    public sealed partial class MainForm
    {
        // ───────────────────────────── 8. Balanceo ─────────────────────────────
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
    }
}
