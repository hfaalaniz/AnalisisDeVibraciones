using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;

namespace VibrationAnalysis.UI
{
    public partial class RotorBalancingForm : Form
    {
        private readonly Timer signalTimer;
        private bool isRunning;
        private readonly RotorModel rotorModel;
        private readonly ThemeManager themeManager;
        private Dictionary<string, object> parameters;

        private bool isCorrectionApplied; // Nueva variable para rastrear si la corrección está aplicada

        public RotorBalancingForm(Dictionary<string, object> initialParameters = null)
        {
            InitializeComponent();
            parameters = initialParameters ?? new Dictionary<string, object>();
            rotorModel = new RotorModel();
            themeManager = new ThemeManager();
            signalTimer = new Timer { Interval = 100 };
            signalTimer.Tick += SignalTimer_Tick;
            isRunning = false;
            isCorrectionApplied = false;  // para verificar si la correcion se ha realizado
            ConfigureControls();
            ApplyTheme(GetParameter<string>("cmbTheme", "Dark"));
            UpdateStatus();
            LogManager.Instance?.LogInfo("RotorBalancingForm inicializado.");
            this.Text = "Balanceo de Rotores en 1 y 2 planos - (C) 2025 por Fabian Alaniz - ElectroNet";
        }

        private void ConfigureControls()
        {
            // Configurar tooltips
            toolTip.SetToolTip(nudRotorDiameter, "Diámetro del rotor en metros para cálculos de masa y corrección.");
            toolTip.SetToolTip(nudRotorLength, "Longitud del rotor en metros para determinar el modo de balanceo.");
            toolTip.SetToolTip(cmbRotorMaterial, "Material del rotor para calcular su masa basada en la densidad.");
            toolTip.SetToolTip(nudBladesCount, "Número de álabes (solo para turbinas).");
            toolTip.SetToolTip(cmbShaftOrientation, "Orientación del eje (afecta cálculos de corrección).");
            toolTip.SetToolTip(nudCorrectionRadius, "Radio donde se coloca el peso de corrección o se perfora (m).");
            toolTip.SetToolTip(nudCorrectionFactor, "Factor de calibración empírico (kg·m).");
            toolTip.SetToolTip(cmbCorrectionMethod, "Método de corrección: añadir material o perforar.");

            // Configurar ComboBox
            cmbRotorType.Items.AddRange(new[] { "Macizo", "Turbina" });
            cmbRotorType.SelectedIndex = GetParameter<int>("cmbRotorType", 0);
            cmbRotorMaterial.Items.AddRange(new[] { "Acero", "Aluminio", "Hierro fundido", "Titanio", "Fibra de carbono" });
            cmbRotorMaterial.SelectedIndex = GetParameter<int>("cmbRotorMaterial", 0);
            cmbShaftOrientation.Items.AddRange(new[] { "Horizontal", "Vertical" });
            cmbShaftOrientation.SelectedIndex = GetParameter<int>("cmbShaftOrientation", 0);
            cmbBalancingMode.Items.AddRange(new[] { "Automático", "1 Plano", "2 Planos" });
            cmbBalancingMode.SelectedIndex = GetParameter<int>("cmbBalancingMode", 0);
            cmbCorrectionMethod.Items.AddRange(new[] { "Añadir material", "Perforar material" });
            cmbCorrectionMethod.SelectedIndex = GetParameter<int>("cmbCorrectionMethod", 0);
            cmbTheme.Items.AddRange(themeManager.GetAvailableThemes().ToArray());
            cmbTheme.SelectedIndex = cmbTheme.Items.IndexOf(GetParameter<string>("cmbTheme", "Dark"));

            // Configurar valores iniciales
            nudFundamentalFrequency.Value = GetParameter<decimal>("nudFundamentalFrequency", 60.0m);
            nudRotorDiameter.Value = GetParameter<decimal>("nudRotorDiameter", 0.2m);
            nudRotorLength.Value = GetParameter<decimal>("nudRotorLength", 0.5m);
            nudBladesCount.Value = GetParameter<decimal>("nudBladesCount", 0);
            nudCorrectionRadius.Value = GetParameter<decimal>("nudCorrectionRadius", 0.1m);
            nudCorrectionFactor.Value = GetParameter<decimal>("nudCorrectionFactor", 0.01m);

            // Configurar controles gráficos
            polarGraphControl1.ShowLegends = chkShowLegends.Checked;
            polarGraphControl2.ShowLegends = chkShowLegends.Checked;
            signalControl.ShowLegends = chkShowLegends.Checked;
            spectrumPlotControl.ShowLegends = chkShowLegends.Checked;
            polarGraphControl1.MaxAmplitude = 2.0;
            polarGraphControl2.MaxAmplitude = 2.0;

            UpdateBalancingModeVisibility();
        }

        private T GetParameter<T>(string key, T defaultValue)
        {
            if (parameters.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            LogManager.Instance?.LogWarning($"Parámetro '{key}' no encontrado o tipo incorrecto. Usando valor por defecto: {defaultValue}");
            return defaultValue;
        }

        private void UpdateBalancingModeVisibility()
        {
            bool isTwoPlanes = cmbBalancingMode.SelectedItem?.ToString() == "2 Planos" ||
                              (cmbBalancingMode.SelectedItem?.ToString() == "Automático" && rotorModel.RequiresTwoPlanes());
            polarGraphControl2.Visible = isTwoPlanes;
            lblCorrectionPlane2.Visible = isTwoPlanes;
            lblCorrectionPlane2.Text = isTwoPlanes ? "Corrección Plano 2: Pendiente" : "";
        }

        private void BtnCalculateMass_Click(object sender, EventArgs e)
        {
            try
            {
                rotorModel.Type = cmbRotorType.SelectedItem?.ToString();
                rotorModel.Diameter = (double)nudRotorDiameter.Value;
                rotorModel.Length = (double)nudRotorLength.Value;
                rotorModel.Material = cmbRotorMaterial.SelectedItem?.ToString();
                rotorModel.BladesCount = (int)nudBladesCount.Value;
                double mass = rotorModel.CalculateMass();
                nudRotorMass.Value = (decimal)mass;
                LogManager.Instance?.LogInfo($"Masa del rotor calculada: {mass:F2} kg");
            }
            catch (Exception ex)
            {
                LogManager.Instance?.LogError($"Error al calcular masa: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnAutoDetectMode_Click(object sender, EventArgs e)
        {
            rotorModel.Type = cmbRotorType.SelectedItem?.ToString();
            rotorModel.Diameter = (double)nudRotorDiameter.Value;
            rotorModel.Length = (double)nudRotorLength.Value;
            rotorModel.Mass = (double)nudRotorMass.Value;
            rotorModel.BladesCount = (int)nudBladesCount.Value;
            rotorModel.ShaftOrientation = cmbShaftOrientation.SelectedItem?.ToString();

            bool requiresTwoPlanes = rotorModel.RequiresTwoPlanes();
            cmbBalancingMode.SelectedIndex = requiresTwoPlanes ? 2 : 1;
            UpdateBalancingModeVisibility();
            LogManager.Instance?.LogInfo($"Modo de balanceo detectado: {(requiresTwoPlanes ? "2 Planos" : "1 Plano")}");
        }

        private void BtnStart_Click(object sender, EventArgs e)
        {
            if (!isRunning)
            {
                isRunning = true;
                signalTimer.Start();
                spectrumPlotControl.FundamentalFrequency = (double)nudFundamentalFrequency.Value;
                signalControl.FundamentalFrequency = (double)nudFundamentalFrequency.Value;
                signalControl.ZoomLevel = 2.0;
                LogManager.Instance?.LogInfo("Simulación de balanceo iniciada.");
                UpdateStatus();
            }
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            if (isRunning)
            {
                isRunning = false;
                signalTimer.Stop();
                LogManager.Instance?.LogInfo("Simulación de balanceo detenida.");
                UpdateStatus();
            }
        }

        private void SignalTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                var (frequencies, freqAmplitudes, times, timeAmplitudes, faultAmp, faultPhase) = GenerateTestSignal();
                spectrumPlotControl.SetData(frequencies, freqAmplitudes);
                signalControl.SetData(times, timeAmplitudes);

                bool isTwoPlanes = cmbBalancingMode.SelectedItem?.ToString() == "2 Planos" ||
                                  (cmbBalancingMode.SelectedItem?.ToString() == "Automático" && rotorModel.RequiresTwoPlanes());

                if (isTwoPlanes)
                {
                    double amp1 = faultAmp;
                    double phase1 = faultPhase;
                    double amp2 = faultAmp * 0.7; // Simulación simplificada
                    double phase2 = (faultPhase + 90) % 360;

                    polarGraphControl1.UnbalanceAmplitude = amp1;
                    polarGraphControl1.UnbalanceAngle = phase1;
                    polarGraphControl1.CorrectionAngle = isCorrectionApplied ? 0 : (phase1 + 180) % 360;
                    polarGraphControl2.UnbalanceAmplitude = amp2;
                    polarGraphControl2.UnbalanceAngle = phase2;
                    polarGraphControl2.CorrectionAngle = isCorrectionApplied ? 0 : (phase2 + 180) % 360;
                    polarGraphControl1.MaxAmplitude = 2.0 * Math.Max(amp1, amp2);
                    polarGraphControl2.MaxAmplitude = 2.0 * Math.Max(amp1, amp2);

                    if (!isCorrectionApplied)
                    {
                        var (weight1, angle1, weight2, angle2) = CalculateCorrectionTwoPlanes(amp1, phase1, amp2, phase2);
                        lblCorrectionPlane1.Text = FormatCorrection(weight1, angle1, cmbCorrectionMethod.SelectedItem?.ToString());
                        lblCorrectionPlane2.Text = FormatCorrection(weight2, angle2, cmbCorrectionMethod.SelectedItem?.ToString());
                    }
                    else
                    {
                        lblCorrectionPlane1.Text = "Corrección aplicada.";
                        lblCorrectionPlane2.Text = "Corrección aplicada.";
                    }
                }
                else
                {
                    polarGraphControl1.UnbalanceAmplitude = faultAmp;
                    polarGraphControl1.UnbalanceAngle = faultPhase;
                    polarGraphControl1.CorrectionAngle = isCorrectionApplied ? 0 : (faultPhase + 180) % 360;
                    polarGraphControl1.MaxAmplitude = 2.0 * faultAmp;
                    polarGraphControl2.UnbalanceAmplitude = 0;
                    polarGraphControl2.UnbalanceAngle = 0;
                    polarGraphControl2.CorrectionAngle = 0;

                    if (!isCorrectionApplied)
                    {
                        var (weight, angle) = CalculateCorrectionOnePlane(faultAmp, faultPhase);
                        lblCorrectionPlane1.Text = FormatCorrection(weight, angle, cmbCorrectionMethod.SelectedItem?.ToString());
                        lblCorrectionPlane2.Text = "";
                    }
                    else
                    {
                        lblCorrectionPlane1.Text = "Corrección aplicada.";
                        lblCorrectionPlane2.Text = "";
                    }
                }

                polarGraphControl1.Invalidate();
                polarGraphControl2.Invalidate();
                lblDiagnosis.Text = isCorrectionApplied
                    ? (faultAmp < 0.05 ? "Diagnóstico: Rotor balanceado correctamente." : $"Diagnóstico: Desbalance residual detectado ({faultAmp:F3} mm/s).")
                    : $"Diagnóstico: Desbalance detectado en {(isTwoPlanes ? "2 planos" : "1 plano")}.";

                LogManager.Instance?.LogDebug($"Señal actualizada: Amplitud={faultAmp:F3}, Fase={faultPhase:F1}°");
            }
            catch (Exception ex)
            {
                LogManager.Instance?.LogError($"Error al actualizar señal: {ex.Message}");
                isRunning = false;
                signalTimer.Stop();
                UpdateStatus();
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private (double[] frequencies, double[] freqAmplitudes, double[] times, double[] timeAmplitudes, double faultAmp, double faultPhase) GenerateTestSignal()
        {
            const int numFreqPoints = 1000;
            const int numTimePoints = 1000;
            double fundamentalFrequency = (double)nudFundamentalFrequency.Value;
            double maxFreq = fundamentalFrequency * 10;
            double period = 1.0 / fundamentalFrequency;
            double maxTime = 2.0 * period;

            double[] frequencies = new double[numFreqPoints];
            double[] freqAmplitudes = new double[numFreqPoints];
            for (int i = 0; i < numFreqPoints; i++)
            {
                frequencies[i] = i * maxFreq / numFreqPoints;
            }
            Array.Clear(freqAmplitudes, 0, freqAmplitudes.Length);

            double[] times = new double[numTimePoints];
            double[] timeAmplitudes = new double[numTimePoints];
            for (int i = 0; i < numTimePoints; i++)
            {
                times[i] = i * maxTime / numTimePoints;
            }
            Array.Clear(timeAmplitudes, 0, timeAmplitudes.Length);

            // Usar amplitud reducida si la corrección está aplicada
            double faultAmp = isCorrectionApplied
                ? 0.02 // Amplitud residual baja para simular rotor balanceado
                : (double)GetParameter<decimal>("nudUnbalanceAmplitude", 0.7m);
            double faultPhase = isCorrectionApplied
                ? 0.0 // Fase irrelevante si la amplitud es muy baja
                : CalculatePhaseUsingFFT(timeAmplitudes, times, fundamentalFrequency);

            AddSignalComponent(frequencies, freqAmplitudes, fundamentalFrequency, faultAmp);
            for (int i = 0; i < numTimePoints; i++)
            {
                double t = times[i];
                timeAmplitudes[i] += faultAmp * Math.Sin(2 * Math.PI * fundamentalFrequency * t);
            }

            Random rand = new Random();
            for (int i = 0; i < freqAmplitudes.Length; i++)
            {
                freqAmplitudes[i] += rand.NextDouble() * 0.05;
            }
            for (int i = 0; i < timeAmplitudes.Length; i++)
            {
                timeAmplitudes[i] += rand.NextDouble() * 0.05;
            }

            double maxFreqAmp = freqAmplitudes.Max();
            if (maxFreqAmp > 2.0)
            {
                for (int i = 0; i < freqAmplitudes.Length; i++)
                {
                    freqAmplitudes[i] = freqAmplitudes[i] * 2.0 / maxFreqAmp;
                }
                faultAmp = faultAmp * 2.0 / maxFreqAmp;
            }

            // Recalcular la fase solo si no está corregido
            if (!isCorrectionApplied)
            {
                faultPhase = CalculatePhaseUsingFFT(timeAmplitudes, times, fundamentalFrequency);
            }

            return (frequencies, freqAmplitudes, times, timeAmplitudes, faultAmp, faultPhase);
        }

        private double CalculatePhaseUsingFFT(double[] timeAmplitudes, double[] times, double fundamentalFrequency)
        {
            var samples = timeAmplitudes.Select(x => new Complex32((float)x, 0)).ToArray();
            Fourier.Forward(samples, FourierOptions.Default);

            double sampleRate = 1.0 / (times[1] - times[0]);
            int n = samples.Length;
            double frequencyResolution = sampleRate / n;
            int fundamentalIndex = (int)Math.Round(fundamentalFrequency / frequencyResolution);

            if (fundamentalIndex >= 0 && fundamentalIndex < samples.Length)
            {
                float phaseRad = samples[fundamentalIndex].Phase;
                double phaseDeg = phaseRad * 180 / Math.PI;
                if (phaseDeg < 0) phaseDeg += 360;
                return phaseDeg;
            }
            return 0.0;
        }

        private void AddSignalComponent(double[] frequencies, double[] amplitudes, double targetFreq, double amplitude)
        {
            int index = Array.FindIndex(frequencies, f => f >= targetFreq);
            if (index >= 0 && index < amplitudes.Length)
            {
                amplitudes[index] += amplitude;
                if (index > 0) amplitudes[index - 1] += amplitude * 0.3;
                if (index < amplitudes.Length - 1) amplitudes[index + 1] += amplitude * 0.3;
            }
        }

        private (double weight, double angle) CalculateCorrectionOnePlane(double faultAmp, double faultPhase)
        {
            double correctionFactor = (double)nudCorrectionFactor.Value;
            double correctionRadius = (double)nudCorrectionRadius.Value;
            double rotorMass = (double)nudRotorMass.Value;
            double weight = (correctionFactor * faultAmp) / correctionRadius * 1000;
            weight = Math.Max(0, Math.Min(weight, 0.01 * rotorMass * 1000));
            double correctionAngle = (faultPhase + 180) % 360;
            return (weight, correctionAngle);
        }

        private (double weight1, double angle1, double weight2, double angle2) CalculateCorrectionTwoPlanes(double amp1, double phase1, double amp2, double phase2)
        {
            double correctionFactor = (double)nudCorrectionFactor.Value;
            double correctionRadius = (double)nudCorrectionRadius.Value;
            double planeDistance = (double)nudRotorLength.Value / 2;
            double rotorMass = (double)nudRotorMass.Value;

            Complex vibration1 = new Complex(amp1 * Math.Cos(phase1 * Math.PI / 180), amp1 * Math.Sin(phase1 * Math.PI / 180));
            Complex vibration2 = new Complex(amp2 * Math.Cos(phase2 * Math.PI / 180), amp2 * Math.Sin(phase2 * Math.PI / 180));

            double k = correctionFactor / correctionRadius;
            Complex a11 = new Complex(k, 0);
            Complex a12 = new Complex(k * 0.5 / planeDistance, 0);
            Complex a21 = new Complex(k * 0.5 / planeDistance, 0);
            Complex a22 = new Complex(k, 0);

            Complex[] A = new[] { a11, a12, a21, a22 };
            Complex[] V = new[] { vibration1, vibration2 };
            Complex[] W = SolveLinearSystem(A, V);

            double weight1 = Math.Abs(W[0].Magnitude) * 1000;
            double angle1 = (W[0].Phase * 180 / Math.PI + 180) % 360;
            double weight2 = Math.Abs(W[1].Magnitude) * 1000;
            double angle2 = (W[1].Phase * 180 / Math.PI + 180) % 360;

            weight1 = Math.Min(weight1, 0.01 * rotorMass * 1000);
            weight2 = Math.Min(weight2, 0.01 * rotorMass * 1000);

            return (weight1, angle1, weight2, angle2);
        }

        private Complex[] SolveLinearSystem(Complex[] A, Complex[] V)
        {
            Complex a11 = A[0], a12 = A[1], a21 = A[2], a22 = A[3];
            Complex v1 = V[0], v2 = V[1];
            Complex det = a11 * a22 - a12 * a21;

            if (det.Magnitude < 1e-10)
            {
                LogManager.Instance?.LogError("Matriz singular. Usando solución aproximada.");
                return new[] { Complex.Zero, Complex.Zero };
            }

            Complex w1 = (v1 * a22 - v2 * a12) / det;
            Complex w2 = (v2 * a11 - v1 * a21) / det;
            return new[] { w1, w2 };
        }

        private string FormatCorrection(double weight, double angle, string method)
        {
            if (method == "Perforar material")
            {
                double density = rotorModel.GetMaterialDensity();
                double volume = weight / 1000 / density; // m³
                double drillDiameter = 0.01; // 10 mm
                double depth = volume / (Math.PI * Math.Pow(drillDiameter / 2, 2)) * 1000; // mm
                return $"Perforar {depth:F1} mm a {angle:F1}°";
            }
            return $"Añadir {weight:F2} g a {angle:F1}°";
        }

        private void BtnApplyCorrection_Click(object sender, EventArgs e)
        {
            try
            {
                isCorrectionApplied = true; // Marcar que la corrección está aplicada
                LogManager.Instance?.LogInfo("Corrección aplicada. Generando nueva señal para verificar balanceo.");

                // Forzar una actualización de la señal para simular el rotor corregido
                SignalTimer_Tick(sender, e);

                // Actualizar diagnóstico
                bool isTwoPlanes = cmbBalancingMode.SelectedItem?.ToString() == "2 Planos" ||
                                  (cmbBalancingMode.SelectedItem?.ToString() == "Automático" && rotorModel.RequiresTwoPlanes());
                double residualAmp = isTwoPlanes
                    ? Math.Max(polarGraphControl1.UnbalanceAmplitude, polarGraphControl2.UnbalanceAmplitude)
                    : polarGraphControl1.UnbalanceAmplitude;

                lblDiagnosis.Text = residualAmp < 0.05
                    ? "Diagnóstico: Rotor balanceado correctamente."
                    : $"Diagnóstico: Desbalance residual detectado ({residualAmp:F3} mm/s).";

                polarGraphControl1.Invalidate();
                polarGraphControl2.Invalidate();
            }
            catch (Exception ex)
            {
                LogManager.Instance?.LogError($"Error al aplicar corrección: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSaveResults_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog { Filter = "CSV Files|*.csv", FileName = "BalancingResults.csv" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var csv = new List<string> { "Parameter,Value" };
                        csv.Add($"RotorType,{cmbRotorType.SelectedItem}");
                        csv.Add($"RotorMass,{nudRotorMass.Value:F2}");
                        csv.Add($"BalancingMode,{cmbBalancingMode.SelectedItem}");
                        csv.Add($"CorrectionPlane1,{lblCorrectionPlane1.Text}");
                        csv.Add($"CorrectionPlane2,{lblCorrectionPlane2.Text}");
                        System.IO.File.WriteAllLines(sfd.FileName, csv);
                        LogManager.Instance?.LogInfo($"Resultados guardados en {sfd.FileName}");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Instance?.LogError($"Error al guardar resultados: {ex.Message}");
                        MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void CmbRotorType_SelectedIndexChanged(object sender, EventArgs e)
        {
            nudBladesCount.Enabled = cmbRotorType.SelectedItem?.ToString() == "Turbina";
            BtnCalculateMass_Click(sender, e);
        }

        private void ChkShowLegends_CheckedChanged(object sender, EventArgs e)
        {
            polarGraphControl1.ShowLegends = chkShowLegends.Checked;
            polarGraphControl2.ShowLegends = chkShowLegends.Checked;
            signalControl.ShowLegends = chkShowLegends.Checked;
            spectrumPlotControl.ShowLegends = chkShowLegends.Checked;
            polarGraphControl1.Invalidate();
            polarGraphControl2.Invalidate();
            signalControl.Invalidate();
            spectrumPlotControl.Invalidate();
        }

        private void CmbBalancingMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateBalancingModeVisibility();
        }

        private void CmbTheme_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbTheme.SelectedItem != null)
            {
                ApplyTheme(cmbTheme.SelectedItem.ToString());
            }
        }

        private void BtnSaveTheme_Click(object sender, EventArgs e)
        {
            try
            {
                var theme = new Theme
                {
                    Name = $"Custom_{DateTime.Now:yyyyMMddHHmmss}",
                    BackColor = this.BackColor,
                    ForeColor = this.ForeColor,
                    ControlBackColor = nudRotorDiameter.BackColor,
                    LineColor = Color.Blue // Ejemplo
                };
                themeManager.SaveTheme(theme);
                cmbTheme.Items.Add(theme.Name);
                cmbTheme.SelectedItem = theme.Name;
                LogManager.Instance?.LogInfo($"Tema guardado: {theme.Name}");
            }
            catch (Exception ex)
            {
                LogManager.Instance?.LogError($"Error al guardar tema: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnLoadTheme_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Filter = "JSON Files|*.json" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var theme = themeManager.LoadTheme(ofd.FileName);
                        themeManager.SaveTheme(theme);
                        if (!cmbTheme.Items.Contains(theme.Name))
                        {
                            cmbTheme.Items.Add(theme.Name);
                        }
                        cmbTheme.SelectedItem = theme.Name;
                        ApplyTheme(theme.Name);
                        LogManager.Instance?.LogInfo($"Tema cargado: {theme.Name}");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Instance?.LogError($"Error al cargar tema: {ex.Message}");
                        MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ApplyTheme(string themeName)
        {
            var theme = themeManager.GetTheme(themeName) ?? themeManager.GetTheme("Dark");
            this.BackColor = theme.BackColor;
            this.ForeColor = theme.ForeColor;
            foreach (System.Windows.Forms.Control control in Controls)
            {
                if (control is System.Windows.Forms.Button || control is NumericUpDown || control is System.Windows.Forms.ComboBox)
                {
                    control.BackColor = theme.ControlBackColor;
                    control.ForeColor = theme.ForeColor;
                }
                else if (control is GroupBox gb)
                {
                    gb.ForeColor = theme.ForeColor;
                    foreach (System.Windows.Forms.Control inner in gb.Controls)
                    {
                        inner.BackColor = theme.ControlBackColor;
                        inner.ForeColor = theme.ForeColor;
                    }
                }
            }
            //polarGraphControl1.LineColor = theme.LineColor;
            //polarGraphControl2.LineColor = theme.LineColor;
            //signalControl.LineColor = theme.LineColor;
            //spectrumPlotControl.LineColor = theme.LineColor;
            Invalidate();
        }

        private void UpdateStatus()
        {
            lblStatus.Text = $"Estado: {(isRunning ? "Ejecutando" : "Detenido")}";
        }
    }
}
