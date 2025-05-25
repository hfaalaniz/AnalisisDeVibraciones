using System;
using System.Globalization; // Para manejar la cultura regional
using System.Windows.Forms;

namespace VibrationAnalysis.UI
{
    public partial class InputValueForm : Form
    {
        public double SelectedValue { get; private set; }

        public InputValueForm(string errorMessage, double minValue, double maxValue)
        {
            // Configurar la cultura para usar coma como separador decimal
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("es-AR"); // Ajusta a tu región, por ejemplo, "es-ES", "es-MX"
            System.Threading.Thread.CurrentThread.CurrentUICulture = new CultureInfo("es-AR");

            InitializeComponent();

            // Configurar mensaje y rango
            lblError.Text = errorMessage;
            lblRange.Text = $"Ingrese un valor entre {minValue:F3} y {maxValue:F3}:";

            // Configurar NumericUpDown
            nudValue.DecimalPlaces = 3;
            nudValue.Increment = 0.001m; // Incremento de 0,001
            nudValue.Minimum = (decimal)(minValue + 0.001);
            nudValue.Maximum = (decimal)maxValue;
            nudValue.Value = (decimal)Math.Min(maxValue, Math.Max(minValue + 0.001, minValue + (maxValue - minValue) / 2));
        }

        private void BtnAccept_Click(object sender, EventArgs e)
        {
            SelectedValue = (double)nudValue.Value;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}