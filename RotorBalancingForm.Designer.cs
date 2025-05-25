using System.Drawing;
using System.Windows.Forms;

namespace VibrationAnalysis.UI
{
    partial class RotorBalancingForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            toolTip = new ToolTip(components);
            groupBoxRotorConfig = new GroupBox();
            cmbRotorType = new ComboBox();
            nudRotorDiameter = new NumericUpDown();
            nudRotorLength = new NumericUpDown();
            cmbRotorMaterial = new ComboBox();
            nudBladesCount = new NumericUpDown();
            cmbShaftOrientation = new ComboBox();
            nudRotorMass = new NumericUpDown();
            btnCalculateMass = new Button();
            groupBoxBalancingConfig = new GroupBox();
            cmbBalancingMode = new ComboBox();
            nudFundamentalFrequency = new NumericUpDown();
            nudCorrectionRadius = new NumericUpDown();
            nudCorrectionFactor = new NumericUpDown();
            cmbCorrectionMethod = new ComboBox();
            btnAutoDetectMode = new Button();
            groupBoxVisualization = new GroupBox();
            polarGraphControl1 = new PolarGraphControl();
            polarGraphControl2 = new PolarGraphControl();
            signalControl = new SignalControl();
            spectrumPlotControl = new SpectrumPlotControl();
            groupBoxResults = new GroupBox();
            lblDiagnosis = new Label();
            lblCorrectionPlane1 = new Label();
            lblCorrectionPlane2 = new Label();
            btnApplyCorrection = new Button();
            btnSaveResults = new Button();
            groupBoxControls = new GroupBox();
            btnStart = new Button();
            btnStop = new Button();
            chkShowLegends = new CheckBox();
            cmbTheme = new ComboBox();
            btnSaveTheme = new Button();
            btnLoadTheme = new Button();
            lblStatus = new Label();
            groupBoxRotorConfig.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudRotorDiameter).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudRotorLength).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudBladesCount).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudRotorMass).BeginInit();
            groupBoxBalancingConfig.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudFundamentalFrequency).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudCorrectionRadius).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudCorrectionFactor).BeginInit();
            groupBoxVisualization.SuspendLayout();
            groupBoxResults.SuspendLayout();
            groupBoxControls.SuspendLayout();
            SuspendLayout();
            // 
            // groupBoxRotorConfig
            // 
            groupBoxRotorConfig.Controls.Add(cmbRotorType);
            groupBoxRotorConfig.Controls.Add(nudRotorDiameter);
            groupBoxRotorConfig.Controls.Add(nudRotorLength);
            groupBoxRotorConfig.Controls.Add(cmbRotorMaterial);
            groupBoxRotorConfig.Controls.Add(nudBladesCount);
            groupBoxRotorConfig.Controls.Add(cmbShaftOrientation);
            groupBoxRotorConfig.Controls.Add(nudRotorMass);
            groupBoxRotorConfig.Controls.Add(btnCalculateMass);
            groupBoxRotorConfig.Location = new Point(12, 12);
            groupBoxRotorConfig.Name = "groupBoxRotorConfig";
            groupBoxRotorConfig.Size = new Size(171, 300);
            groupBoxRotorConfig.TabIndex = 0;
            groupBoxRotorConfig.TabStop = false;
            groupBoxRotorConfig.Text = "Configuración del Rotor";
            // 
            // cmbRotorType
            // 
            cmbRotorType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbRotorType.Location = new Point(10, 30);
            cmbRotorType.Name = "cmbRotorType";
            cmbRotorType.Size = new Size(120, 23);
            cmbRotorType.TabIndex = 0;
            // 
            // nudRotorDiameter
            // 
            nudRotorDiameter.DecimalPlaces = 3;
            nudRotorDiameter.Location = new Point(10, 60);
            nudRotorDiameter.Maximum = new decimal(new int[] { 50, 0, 0, 65536 });
            nudRotorDiameter.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            nudRotorDiameter.Name = "nudRotorDiameter";
            nudRotorDiameter.Size = new Size(120, 23);
            nudRotorDiameter.TabIndex = 1;
            nudRotorDiameter.Value = new decimal(new int[] { 1, 0, 0, 131072 });
            // 
            // nudRotorLength
            // 
            nudRotorLength.DecimalPlaces = 3;
            nudRotorLength.Location = new Point(10, 90);
            nudRotorLength.Maximum = new decimal(new int[] { 100, 0, 0, 65536 });
            nudRotorLength.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            nudRotorLength.Name = "nudRotorLength";
            nudRotorLength.Size = new Size(120, 23);
            nudRotorLength.TabIndex = 2;
            nudRotorLength.Value = new decimal(new int[] { 1, 0, 0, 131072 });
            // 
            // cmbRotorMaterial
            // 
            cmbRotorMaterial.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbRotorMaterial.Location = new Point(10, 120);
            cmbRotorMaterial.Name = "cmbRotorMaterial";
            cmbRotorMaterial.Size = new Size(120, 23);
            cmbRotorMaterial.TabIndex = 3;
            // 
            // nudBladesCount
            // 
            nudBladesCount.Location = new Point(10, 150);
            nudBladesCount.Name = "nudBladesCount";
            nudBladesCount.Size = new Size(120, 23);
            nudBladesCount.TabIndex = 4;
            // 
            // cmbShaftOrientation
            // 
            cmbShaftOrientation.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbShaftOrientation.Location = new Point(10, 180);
            cmbShaftOrientation.Name = "cmbShaftOrientation";
            cmbShaftOrientation.Size = new Size(120, 23);
            cmbShaftOrientation.TabIndex = 5;
            // 
            // nudRotorMass
            // 
            nudRotorMass.DecimalPlaces = 2;
            nudRotorMass.Location = new Point(10, 210);
            nudRotorMass.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            nudRotorMass.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            nudRotorMass.Name = "nudRotorMass";
            nudRotorMass.Size = new Size(120, 23);
            nudRotorMass.TabIndex = 6;
            nudRotorMass.Value = new decimal(new int[] { 1, 0, 0, 65536 });
            // 
            // btnCalculateMass
            // 
            btnCalculateMass.Location = new Point(10, 240);
            btnCalculateMass.Name = "btnCalculateMass";
            btnCalculateMass.Size = new Size(120, 30);
            btnCalculateMass.TabIndex = 7;
            btnCalculateMass.Text = "Calcular Masa";
            btnCalculateMass.Click += BtnCalculateMass_Click;
            // 
            // groupBoxBalancingConfig
            // 
            groupBoxBalancingConfig.Controls.Add(cmbBalancingMode);
            groupBoxBalancingConfig.Controls.Add(nudFundamentalFrequency);
            groupBoxBalancingConfig.Controls.Add(nudCorrectionRadius);
            groupBoxBalancingConfig.Controls.Add(nudCorrectionFactor);
            groupBoxBalancingConfig.Controls.Add(cmbCorrectionMethod);
            groupBoxBalancingConfig.Controls.Add(btnAutoDetectMode);
            groupBoxBalancingConfig.Location = new Point(12, 320);
            groupBoxBalancingConfig.Name = "groupBoxBalancingConfig";
            groupBoxBalancingConfig.Size = new Size(171, 240);
            groupBoxBalancingConfig.TabIndex = 1;
            groupBoxBalancingConfig.TabStop = false;
            groupBoxBalancingConfig.Text = "Configuración de Balanceo";
            // 
            // cmbBalancingMode
            // 
            cmbBalancingMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbBalancingMode.Location = new Point(10, 30);
            cmbBalancingMode.Name = "cmbBalancingMode";
            cmbBalancingMode.Size = new Size(120, 23);
            cmbBalancingMode.TabIndex = 0;
            cmbBalancingMode.SelectedIndexChanged += CmbBalancingMode_SelectedIndexChanged;
            // 
            // nudFundamentalFrequency
            // 
            nudFundamentalFrequency.DecimalPlaces = 1;
            nudFundamentalFrequency.Location = new Point(10, 60);
            nudFundamentalFrequency.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            nudFundamentalFrequency.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            nudFundamentalFrequency.Name = "nudFundamentalFrequency";
            nudFundamentalFrequency.Size = new Size(120, 23);
            nudFundamentalFrequency.TabIndex = 1;
            nudFundamentalFrequency.Value = new decimal(new int[] { 10, 0, 0, 0 });
            // 
            // nudCorrectionRadius
            // 
            nudCorrectionRadius.DecimalPlaces = 3;
            nudCorrectionRadius.Location = new Point(10, 90);
            nudCorrectionRadius.Maximum = new decimal(new int[] { 10, 0, 0, 65536 });
            nudCorrectionRadius.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            nudCorrectionRadius.Name = "nudCorrectionRadius";
            nudCorrectionRadius.Size = new Size(120, 23);
            nudCorrectionRadius.TabIndex = 2;
            nudCorrectionRadius.Value = new decimal(new int[] { 1, 0, 0, 131072 });
            // 
            // nudCorrectionFactor
            // 
            nudCorrectionFactor.DecimalPlaces = 4;
            nudCorrectionFactor.Location = new Point(10, 120);
            nudCorrectionFactor.Maximum = new decimal(new int[] { 1, 0, 0, 65536 });
            nudCorrectionFactor.Minimum = new decimal(new int[] { 1, 0, 0, 196608 });
            nudCorrectionFactor.Name = "nudCorrectionFactor";
            nudCorrectionFactor.Size = new Size(120, 23);
            nudCorrectionFactor.TabIndex = 3;
            nudCorrectionFactor.Value = new decimal(new int[] { 1, 0, 0, 196608 });
            // 
            // cmbCorrectionMethod
            // 
            cmbCorrectionMethod.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbCorrectionMethod.Location = new Point(10, 150);
            cmbCorrectionMethod.Name = "cmbCorrectionMethod";
            cmbCorrectionMethod.Size = new Size(120, 23);
            cmbCorrectionMethod.TabIndex = 4;
            // 
            // btnAutoDetectMode
            // 
            btnAutoDetectMode.Location = new Point(10, 180);
            btnAutoDetectMode.Name = "btnAutoDetectMode";
            btnAutoDetectMode.Size = new Size(120, 30);
            btnAutoDetectMode.TabIndex = 5;
            btnAutoDetectMode.Text = "Detectar Modo";
            btnAutoDetectMode.Click += BtnAutoDetectMode_Click;
            // 
            // groupBoxVisualization
            // 
            groupBoxVisualization.Controls.Add(polarGraphControl1);
            groupBoxVisualization.Controls.Add(polarGraphControl2);
            groupBoxVisualization.Controls.Add(signalControl);
            groupBoxVisualization.Controls.Add(spectrumPlotControl);
            groupBoxVisualization.Location = new Point(189, 12);
            groupBoxVisualization.Name = "groupBoxVisualization";
            groupBoxVisualization.Size = new Size(781, 694);
            groupBoxVisualization.TabIndex = 2;
            groupBoxVisualization.TabStop = false;
            groupBoxVisualization.Text = "Visualización";
            // 
            // polarGraphControl1
            // 
            polarGraphControl1.BorderStyle = BorderStyle.FixedSingle;
            polarGraphControl1.CorrectionAngle = 0D;
            polarGraphControl1.CorrectionAnglePlane2 = 0D;
            polarGraphControl1.Location = new Point(36, 19);
            polarGraphControl1.MaxAmplitude = 2D;
            polarGraphControl1.Name = "polarGraphControl1";
            polarGraphControl1.ShowLegends = true;
            polarGraphControl1.Size = new Size(311, 306);
            polarGraphControl1.TabIndex = 0;
            polarGraphControl1.UnbalanceAmplitude = 0D;
            polarGraphControl1.UnbalanceAmplitudePlane2 = 0D;
            polarGraphControl1.UnbalanceAngle = 0D;
            polarGraphControl1.UnbalanceAnglePlane2 = 0D;
            // 
            // polarGraphControl2
            // 
            polarGraphControl2.BorderStyle = BorderStyle.FixedSingle;
            polarGraphControl2.CorrectionAngle = 0D;
            polarGraphControl2.CorrectionAnglePlane2 = 0D;
            polarGraphControl2.Location = new Point(436, 19);
            polarGraphControl2.MaxAmplitude = 2D;
            polarGraphControl2.Name = "polarGraphControl2";
            polarGraphControl2.ShowLegends = true;
            polarGraphControl2.Size = new Size(311, 306);
            polarGraphControl2.TabIndex = 1;
            polarGraphControl2.UnbalanceAmplitude = 0D;
            polarGraphControl2.UnbalanceAmplitudePlane2 = 0D;
            polarGraphControl2.UnbalanceAngle = 0D;
            polarGraphControl2.UnbalanceAnglePlane2 = 0D;
            // 
            // signalControl
            // 
            signalControl.BorderStyle = BorderStyle.FixedSingle;
            signalControl.FundamentalFrequency = 60D;
            signalControl.Location = new Point(10, 340);
            signalControl.Name = "signalControl";
            signalControl.ShowLegends = true;
            signalControl.Size = new Size(765, 141);
            signalControl.TabIndex = 2;
            signalControl.VisibleAmpMax = 2D;
            signalControl.ZoomLevel = 2D;
            // 
            // spectrumPlotControl
            // 
            spectrumPlotControl.BackColor = Color.FromArgb(30, 30, 30);
            spectrumPlotControl.BearingDefectFrequency = 4.5D;
            spectrumPlotControl.BorderStyle = BorderStyle.FixedSingle;
            spectrumPlotControl.ForeColor = Color.White;
            spectrumPlotControl.FundamentalFrequency = 60D;
            spectrumPlotControl.IsSelecting = false;
            spectrumPlotControl.Location = new Point(10, 488);
            spectrumPlotControl.Name = "spectrumPlotControl";
            spectrumPlotControl.ShowLegends = true;
            spectrumPlotControl.Size = new Size(765, 200);
            spectrumPlotControl.TabIndex = 3;
            spectrumPlotControl.VisibleAmpMax = 2D;
            spectrumPlotControl.VisibleAmpMin = 0D;
            spectrumPlotControl.VisibleFreqMax = 1000D;
            spectrumPlotControl.VisibleFreqMin = 0D;
            spectrumPlotControl.ZoomLevelX = 1D;
            spectrumPlotControl.ZoomLevelY = 1D;
            // 
            // groupBoxResults
            // 
            groupBoxResults.Controls.Add(lblDiagnosis);
            groupBoxResults.Controls.Add(lblCorrectionPlane1);
            groupBoxResults.Controls.Add(lblCorrectionPlane2);
            groupBoxResults.Controls.Add(btnApplyCorrection);
            groupBoxResults.Controls.Add(btnSaveResults);
            groupBoxResults.Location = new Point(1027, 12);
            groupBoxResults.Name = "groupBoxResults";
            groupBoxResults.Size = new Size(300, 240);
            groupBoxResults.TabIndex = 3;
            groupBoxResults.TabStop = false;
            groupBoxResults.Text = "Resultados";
            // 
            // lblDiagnosis
            // 
            lblDiagnosis.Location = new Point(10, 30);
            lblDiagnosis.Name = "lblDiagnosis";
            lblDiagnosis.Size = new Size(280, 40);
            lblDiagnosis.TabIndex = 0;
            lblDiagnosis.Text = "Diagnóstico: Pendiente";
            // 
            // lblCorrectionPlane1
            // 
            lblCorrectionPlane1.Location = new Point(10, 80);
            lblCorrectionPlane1.Name = "lblCorrectionPlane1";
            lblCorrectionPlane1.Size = new Size(280, 40);
            lblCorrectionPlane1.TabIndex = 1;
            lblCorrectionPlane1.Text = "Corrección Plano 1: Pendiente";
            // 
            // lblCorrectionPlane2
            // 
            lblCorrectionPlane2.Location = new Point(10, 130);
            lblCorrectionPlane2.Name = "lblCorrectionPlane2";
            lblCorrectionPlane2.Size = new Size(280, 40);
            lblCorrectionPlane2.TabIndex = 2;
            lblCorrectionPlane2.Text = "Corrección Plano 2: Pendiente";
            // 
            // btnApplyCorrection
            // 
            btnApplyCorrection.Location = new Point(10, 180);
            btnApplyCorrection.Name = "btnApplyCorrection";
            btnApplyCorrection.Size = new Size(120, 30);
            btnApplyCorrection.TabIndex = 3;
            btnApplyCorrection.Text = "Aplicar Corrección";
            btnApplyCorrection.Click += BtnApplyCorrection_Click;
            // 
            // btnSaveResults
            // 
            btnSaveResults.Location = new Point(140, 180);
            btnSaveResults.Name = "btnSaveResults";
            btnSaveResults.Size = new Size(120, 30);
            btnSaveResults.TabIndex = 4;
            btnSaveResults.Text = "Guardar Resultados";
            btnSaveResults.Click += BtnSaveResults_Click;
            // 
            // groupBoxControls
            // 
            groupBoxControls.Controls.Add(btnStart);
            groupBoxControls.Controls.Add(btnStop);
            groupBoxControls.Controls.Add(chkShowLegends);
            groupBoxControls.Controls.Add(cmbTheme);
            groupBoxControls.Controls.Add(btnSaveTheme);
            groupBoxControls.Controls.Add(btnLoadTheme);
            groupBoxControls.Location = new Point(1027, 260);
            groupBoxControls.Name = "groupBoxControls";
            groupBoxControls.Size = new Size(300, 300);
            groupBoxControls.TabIndex = 4;
            groupBoxControls.TabStop = false;
            groupBoxControls.Text = "Controles";
            // 
            // btnStart
            // 
            btnStart.Location = new Point(10, 30);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(120, 30);
            btnStart.TabIndex = 0;
            btnStart.Text = "Iniciar";
            btnStart.Click += BtnStart_Click;
            // 
            // btnStop
            // 
            btnStop.Location = new Point(140, 30);
            btnStop.Name = "btnStop";
            btnStop.Size = new Size(120, 30);
            btnStop.TabIndex = 1;
            btnStop.Text = "Detener";
            btnStop.Click += BtnStop_Click;
            // 
            // chkShowLegends
            // 
            chkShowLegends.Checked = true;
            chkShowLegends.CheckState = CheckState.Checked;
            chkShowLegends.Location = new Point(10, 70);
            chkShowLegends.Name = "chkShowLegends";
            chkShowLegends.Size = new Size(120, 30);
            chkShowLegends.TabIndex = 2;
            chkShowLegends.Text = "Mostrar Leyendas";
            chkShowLegends.CheckedChanged += ChkShowLegends_CheckedChanged;
            // 
            // cmbTheme
            // 
            cmbTheme.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbTheme.Location = new Point(10, 110);
            cmbTheme.Name = "cmbTheme";
            cmbTheme.Size = new Size(120, 23);
            cmbTheme.TabIndex = 3;
            cmbTheme.SelectedIndexChanged += CmbTheme_SelectedIndexChanged;
            // 
            // btnSaveTheme
            // 
            btnSaveTheme.Location = new Point(10, 140);
            btnSaveTheme.Name = "btnSaveTheme";
            btnSaveTheme.Size = new Size(120, 30);
            btnSaveTheme.TabIndex = 4;
            btnSaveTheme.Text = "Guardar Tema";
            btnSaveTheme.Click += BtnSaveTheme_Click;
            // 
            // btnLoadTheme
            // 
            btnLoadTheme.Location = new Point(140, 140);
            btnLoadTheme.Name = "btnLoadTheme";
            btnLoadTheme.Size = new Size(120, 30);
            btnLoadTheme.TabIndex = 5;
            btnLoadTheme.Text = "Cargar Tema";
            btnLoadTheme.Click += BtnLoadTheme_Click;
            // 
            // lblStatus
            // 
            lblStatus.Location = new Point(12, 570);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(171, 20);
            lblStatus.TabIndex = 5;
            lblStatus.Text = "Estado: Detenido";
            // 
            // RotorBalancingForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1339, 709);
            Controls.Add(groupBoxRotorConfig);
            Controls.Add(groupBoxBalancingConfig);
            Controls.Add(groupBoxVisualization);
            Controls.Add(groupBoxResults);
            Controls.Add(groupBoxControls);
            Controls.Add(lblStatus);
            Name = "RotorBalancingForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Balanceo de Rotores";
            groupBoxRotorConfig.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)nudRotorDiameter).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudRotorLength).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudBladesCount).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudRotorMass).EndInit();
            groupBoxBalancingConfig.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)nudFundamentalFrequency).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudCorrectionRadius).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudCorrectionFactor).EndInit();
            groupBoxVisualization.ResumeLayout(false);
            groupBoxResults.ResumeLayout(false);
            groupBoxControls.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private GroupBox groupBoxRotorConfig;
        private ComboBox cmbRotorType;
        private NumericUpDown nudRotorDiameter;
        private NumericUpDown nudRotorLength;
        private ComboBox cmbRotorMaterial;
        private NumericUpDown nudBladesCount;
        private ComboBox cmbShaftOrientation;
        private NumericUpDown nudRotorMass;
        private Button btnCalculateMass;
        private GroupBox groupBoxBalancingConfig;
        private ComboBox cmbBalancingMode;
        private NumericUpDown nudFundamentalFrequency;
        private NumericUpDown nudCorrectionRadius;
        private NumericUpDown nudCorrectionFactor;
        private ComboBox cmbCorrectionMethod;
        private Button btnAutoDetectMode;
        private GroupBox groupBoxVisualization;
        private PolarGraphControl polarGraphControl1;
        private PolarGraphControl polarGraphControl2;
        private SignalControl signalControl;
        private SpectrumPlotControl spectrumPlotControl;
        private GroupBox groupBoxResults;
        private Label lblDiagnosis;
        private Label lblCorrectionPlane1;
        private Label lblCorrectionPlane2;
        private Button btnApplyCorrection;
        private Button btnSaveResults;
        private GroupBox groupBoxControls;
        private Button btnStart;
        private Button btnStop;
        private CheckBox chkShowLegends;
        private ComboBox cmbTheme;
        private Button btnSaveTheme;
        private Button btnLoadTheme;
        private Label lblStatus;
        private ToolTip toolTip;
    }
}