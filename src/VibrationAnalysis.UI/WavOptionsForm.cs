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
