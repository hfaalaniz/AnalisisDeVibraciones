using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using VibrationAnalysis.Core.Acquisition;

namespace VibrationAnalysis.UI.Devices
{
    /// <summary>Configuración del equipo (placa de sonido o NI-DAQmx) y de sus canales: tipo de sensor, sensibilidad, IEPE, rango y calibración.</summary>
    public sealed class DaqSetupForm : Form
    {
        private static readonly string[] Kinds = { "Acelerómetro IEPE (mV/g)", "Acelerómetro de tensión (mV/g)", "Sensor de velocidad (mV/(mm/s))", "Sonda de proximidad (mV/µm)", "Tacómetro / keyphasor", "Pinza de corriente (mV/A)", "Sensor de ultrasonido (mV/V)" };
        private readonly bool soundCard;
        private readonly Func<IAcquisitionDevice> factory;
        private readonly ComboBox cmbDevice = new ComboBox { Width = 320 };
        private readonly ComboBox cmbRate = new ComboBox { Width = 100 };
        private readonly ComboBox cmbBits = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
        private readonly NumericUpDown nudFull = new NumericUpDown { DecimalPlaces = 3, Minimum = 0.01m, Maximum = 50, Value = 1, Width = 80 };
        private readonly NumericUpDown nudBuffer = new NumericUpDown { Minimum = 2, Maximum = 600, Value = 30, Width = 70 };
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersWidth = 28, BackgroundColor = SystemColors.Window };
        public AcquisitionSettings Result { get; private set; }

        public DaqSetupForm(AcquisitionSettings current, bool isSoundCard, Func<IAcquisitionDevice> deviceFactory)
        {
            soundCard = isSoundCard; factory = deviceFactory;
            Text = isSoundCard ? "Placa de sonido · canales" : "NI-DAQmx · canales";
            Size = new Size(980, 520); StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9.5f); MinimizeBox = false;

            var devices = isSoundCard ? SoundCardDevice.Devices() : NiDaqmxDevice.Devices();
            cmbDevice.Items.AddRange(devices.Cast<object>().ToArray());
            cmbDevice.Text = string.IsNullOrEmpty(current.Device) && devices.Count > 0 ? devices[0] : current.Device;
            cmbRate.Items.AddRange((isSoundCard ? new[] { "44100", "48000", "96000", "192000" } : new[] { "2560", "5120", "10240", "12800", "25600", "51200" }).Cast<object>().ToArray());
            cmbRate.Text = current.SampleRate.ToString(CultureInfo.InvariantCulture);
            cmbBits.Items.AddRange(new object[] { "16", "24" });
            cmbBits.SelectedIndex = current.Bits >= 24 ? 1 : 0;
            nudFull.Value = (decimal)Math.Clamp(current.FullScaleVolts, 0.01, 50);
            nudBuffer.Value = (decimal)Math.Clamp(current.BufferSeconds, 2, 600);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
            top.Controls.AddRange(new Control[] { L("Equipo:"), cmbDevice, L("Muestreo (Hz):"), cmbRate });
            if (isSoundCard) top.Controls.AddRange(new Control[] { L("Bits:"), cmbBits, L("Fondo de escala (V pico):"), nudFull });
            top.Controls.AddRange(new Control[] { L("Memoria (s):"), nudBuffer });
            if (!isSoundCard && devices.Count == 0)
                top.Controls.Add(new Label { AutoSize = true, ForeColor = Color.FromArgb(200, 40, 40), Margin = new Padding(8, 8, 0, 0), Text = NiDaqmxDevice.IsInstalled() ? "NI-DAQmx no encuentra placas conectadas." : "NI-DAQmx no está instalado en este equipo." });

            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Activo", FillWeight = 7 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nombre", FillWeight = 20 });
            var phys = new DataGridViewComboBoxColumn { HeaderText = isSoundCard ? "Entrada" : "Canal físico", FillWeight = 16, FlatStyle = FlatStyle.Flat };
            if (isSoundCard) phys.Items.AddRange("0", "1");
            else foreach (var p in devices.SelectMany(d => NiDaqmxDevice.PhysicalChannels(d)).Concat(current.Channels.Select(c => c.Physical)).Where(p => p.Length > 0).Distinct()) phys.Items.Add(p);
            grid.Columns.Add(phys);
            var kind = new DataGridViewComboBoxColumn { HeaderText = "Tipo de sensor", FillWeight = 28, FlatStyle = FlatStyle.Flat };
            kind.Items.AddRange(Kinds.Cast<object>().ToArray());
            grid.Columns.Add(kind);
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sensibilidad", FillWeight = 11 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "IEPE (mA)", FillWeight = 9 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rango (unid.)", FillWeight = 10 });
            grid.DataError += (s, e) => e.ThrowException = false;
            foreach (var c in current.Channels)
                grid.Rows.Add(c.Enabled, c.Name, isSoundCard ? (c.Physical == "1" ? "1" : "0") : c.Physical, Kinds[(int)c.Kind], N(c.Sensitivity), N(c.IepeMilliamps), N(c.Range));

            var help = new Label
            {
                Dock = DockStyle.Top, Height = 58, Padding = new Padding(8, 0, 8, 4),
                Text = isSoundCard
                    ? "Placa de sonido: la entrada «0» es el canal izquierdo y «1» el derecho. Los acelerómetros IEPE necesitan un acondicionador (fuente de corriente) entre el sensor y la placa.\nEl fondo de escala es la tensión pico que satura la entrada: medirlo con un generador o calibrar con una referencia. La placa no mide por debajo de ≈ 10 Hz (acople AC)."
                    : "NI-DAQmx: los acelerómetros IEPE se excitan desde la placa (2–4 mA). Sensibilidad en mV/g, mV/(mm/s) o mV/µm según el tipo; está en el certificado del sensor.\nLas sondas de proximidad necesitan una entrada con acople DC (NI 9215, 9205…); su gap se informa aparte.",
            };
            var ok = new Button { Text = "Aceptar", AutoSize = true };
            ok.Click += (s, e) => { var r = Collect(); if (r != null) { Result = r; DialogResult = DialogResult.OK; Close(); } };
            var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
            var calib = new Button { Text = "Calibrar canal seleccionado…", AutoSize = true };
            calib.Click += (s, e) => Calibrate();
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            bottom.Controls.AddRange(new Control[] { cancel, ok, calib });
            Controls.Add(grid);
            Controls.Add(help);
            Controls.Add(top);
            Controls.Add(bottom);
            CancelButton = cancel;
        }

        private static Label L(string t) => new Label { Text = t, AutoSize = true, Margin = new Padding(6, 8, 0, 0) };
        private static string N(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);

        private static double Num(object o, double def)
        {
            var s = Convert.ToString(o, CultureInfo.CurrentCulture) ?? "";
            return double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) || double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        private AcquisitionSettings Collect()
        {
            if (!double.TryParse(cmbRate.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) || rate < 100 || rate > 1_000_000)
            {
                MessageBox.Show(this, "Frecuencia de muestreo inválida.", "Equipo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            var set = new AcquisitionSettings
            {
                Device = cmbDevice.Text.Trim(), SampleRate = rate, Bits = cmbBits.SelectedIndex == 1 ? 24 : 16,
                FullScaleVolts = (double)nudFull.Value, BufferSeconds = (double)nudBuffer.Value, Channels = new List<ChannelConfig>(),
            };
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                int k = Array.IndexOf(Kinds, row.Cells[3].Value as string);
                var c = new ChannelConfig
                {
                    Enabled = row.Cells[0].Value is bool b && b,
                    Name = (row.Cells[1].Value as string ?? "").Trim(),
                    Physical = (row.Cells[2].Value as string ?? "").Trim(),
                    Kind = (SensorKind)Math.Max(0, k),
                    Sensitivity = Num(row.Cells[4].Value, 100),
                    IepeMilliamps = Num(row.Cells[5].Value, 4),
                    Range = Num(row.Cells[6].Value, 50),
                };
                if (c.Name.Length == 0) c.Name = $"Canal {set.Channels.Count + 1}";
                if (c.Enabled && c.Kind != SensorKind.Tachometer && c.Sensitivity <= 0)
                {
                    MessageBox.Show(this, $"«{c.Name}»: la sensibilidad debe ser mayor que cero.", "Equipo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                set.Channels.Add(c);
            }
            if (!set.Channels.Any(c => c.Enabled && c.Kind != SensorKind.Tachometer))
            {
                MessageBox.Show(this, "Activá al menos un canal de vibración.", "Equipo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            return set;
        }

        /// <summary>Mide 3 s con el calibrador colocado en el sensor del canal elegido y recalcula su sensibilidad.</summary>
        private void Calibrate()
        {
            if (grid.CurrentRow == null || grid.CurrentRow.IsNewRow) { MessageBox.Show(this, "Seleccioná la fila del canal a calibrar."); return; }
            var set = Collect();
            if (set == null) return;
            int row = grid.CurrentRow.Index;
            var cfg = set.Channels[row];
            if (cfg.Kind == SensorKind.Tachometer) { MessageBox.Show(this, "El tacómetro no se calibra."); return; }
            using var p = new PromptForm("Calibración", $"Colocá «{cfg.Name}» en el calibrador y escribí la referencia como «frecuencia;nivel RMS»\n(1 g RMS a 159,2 Hz para aceleración, 10 mm/s para velocidad):", "159.2;1");
            if (p.ShowDialog(this) != DialogResult.OK) return;
            var parts = p.Value.Split(';');
            if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var f) || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lvl))
            { MessageBox.Show(this, "Formato: 159.2;1"); return; }
            foreach (var c in set.Channels) c.Enabled = false;
            cfg.Enabled = true;
            set.BufferSeconds = 5;
            Cursor = Cursors.WaitCursor;
            try
            {
                using var dev = factory();
                dev.Start(set);
                Thread.Sleep(3500);
                var (rec, _) = dev.Snapshot(3, 0);
                dev.Stop();
                var volts = rec.Channels[0].Data.Select(v => v * cfg.Sensitivity / 1000).ToArray();
                double sens = SensorCalibration.SensitivityFromReference(volts, rec.SampleRate, f, lvl);
                if (MessageBox.Show(this, $"Sensibilidad medida: {sens:0.###} {cfg.UnitSymbol} (antes {cfg.Sensitivity:0.###}).\n¿Usar este valor?", "Calibración", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    grid.Rows[row].Cells[4].Value = N(sens);
            }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo calibrar: " + ex.Message, "Calibración", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { Cursor = Cursors.Default; }
        }
    }
}
