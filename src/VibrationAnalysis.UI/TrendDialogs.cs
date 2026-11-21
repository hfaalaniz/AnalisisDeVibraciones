using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using VibrationAnalysis.Core.Trending;

namespace VibrationAnalysis.UI
{
    /// <summary>Diálogo simple para pedir un texto y, opcionalmente, elegir una opción.</summary>
    public sealed class PromptForm : Form
    {
        private readonly TextBox txt = new TextBox { Width = 320 };
        private readonly ComboBox cmb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        public string Value => txt.Text.Trim();
        public int Option => cmb.SelectedIndex;

        public PromptForm(string title, string label, string value, string optionLabel = null, string[] options = null, int option = 0)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(12); Font = new Font("Segoe UI", 9.5f);
            var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
            t.Controls.Add(new System.Windows.Forms.Label { Text = label, AutoSize = true });
            txt.Text = value ?? "";
            t.Controls.Add(txt);
            if (options != null)
            {
                t.Controls.Add(new System.Windows.Forms.Label { Text = optionLabel ?? "", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
                cmb.Items.AddRange(options);
                cmb.SelectedIndex = Math.Max(0, Math.Min(option, options.Length - 1));
                t.Controls.Add(cmb);
            }
            var ok = new Button { Text = "Aceptar", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
            var f = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            f.Controls.Add(cancel); f.Controls.Add(ok);
            t.Controls.Add(f);
            Controls.Add(t);
            AcceptButton = ok; CancelButton = cancel;
        }

        public static string Ask(IWin32Window owner, string title, string label, string value)
        {
            using var p = new PromptForm(title, label, value);
            return p.ShowDialog(owner) == DialogResult.OK && p.Value.Length > 0 ? p.Value : null;
        }
    }

    /// <summary>Editor de las reglas de alarma de un punto de medición.</summary>
    public sealed class AlarmEditorForm : Form
    {
        private static readonly string[] Kinds = { "Valor absoluto", "Múltiplo de la línea base", "Estadístico (media + k·σ)", "Zonas ISO 20816", "Diferencia con la línea base (dB)" };
        private readonly DataGridView grid = new DataGridView
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersWidth = 28, BackgroundColor = SystemColors.Window,
        };
        private readonly List<string> keys = FeatureKeys.Names.Keys.ToList();
        public List<AlarmRule> Rules { get; private set; }

        public AlarmEditorForm(string pointName, IEnumerable<AlarmRule> rules)
        {
            Text = "Alarmas · " + pointName;
            Size = new Size(860, 420); StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9.5f); MinimizeBox = false;
            var colF = new DataGridViewComboBoxColumn { HeaderText = "Indicador", FillWeight = 45, FlatStyle = FlatStyle.Flat };
            colF.Items.AddRange(keys.Select(FeatureKeys.Name).Cast<object>().ToArray());
            var colK = new DataGridViewComboBoxColumn { HeaderText = "Tipo de límite", FillWeight = 28, FlatStyle = FlatStyle.Flat };
            colK.Items.AddRange(Kinds.Cast<object>().ToArray());
            grid.Columns.Add(colF);
            grid.Columns.Add(colK);
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Alerta", FillWeight = 10 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Peligro", FillWeight = 10 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Activa", FillWeight = 7 });
            foreach (var r in rules)
                grid.Rows.Add(FeatureKeys.Name(r.Feature), Kinds[(int)r.Kind], r.Alert.ToString("0.###", CultureInfo.CurrentCulture), r.Danger.ToString("0.###", CultureInfo.CurrentCulture), r.Enabled);
            grid.DataError += (s, e) => e.ThrowException = false;
            var help = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Top, Height = 54, Padding = new Padding(8),
                Text = "Absoluto: valores en las unidades del indicador (en «MCSA … dB» menos es peor: alerta 42, peligro 36). Múltiplo de la base: 2 y 4 = el doble y el cuádruple.\n" +
                       "Estadístico: media + k·σ de la base (3 y 6). ISO 20816: clase de la máquina. Diferencia en dB: base + 8 y + 16 (ultrasonido).",
            };
            var ok = new Button { Text = "Guardar", AutoSize = true };
            ok.Click += (s, e) => { if (Collect()) { DialogResult = DialogResult.OK; Close(); } };
            var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
            var reset = new Button { Text = "Valores por defecto", AutoSize = true };
            reset.Click += (s, e) =>
            {
                grid.Rows.Clear();
                foreach (var r in AlarmDefaults.Create())
                    grid.Rows.Add(FeatureKeys.Name(r.Feature), Kinds[(int)r.Kind], r.Alert.ToString("0.###", CultureInfo.CurrentCulture), r.Danger.ToString("0.###", CultureInfo.CurrentCulture), r.Enabled);
            };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            bottom.Controls.AddRange(new Control[] { cancel, ok, reset });
            Controls.Add(grid);
            Controls.Add(help);
            Controls.Add(bottom);
            CancelButton = cancel;
        }

        private bool Collect()
        {
            var list = new List<AlarmRule>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                string fname = row.Cells[0].Value as string;
                string kname = row.Cells[1].Value as string;
                if (string.IsNullOrEmpty(fname)) continue;
                int fi = keys.Select(FeatureKeys.Name).ToList().IndexOf(fname);
                int ki = Array.IndexOf(Kinds, kname);
                if (fi < 0) continue;
                if (ki < 0) ki = 0;
                double Num(object o)
                {
                    var str = Convert.ToString(o, CultureInfo.CurrentCulture) ?? "";
                    return double.TryParse(str, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ||
                           double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
                }
                double a = Num(row.Cells[2].Value), d = Num(row.Cells[3].Value);
                bool lower = FeatureKeys.LowerIsWorse(keys[fi]) && (AlarmKind)ki == AlarmKind.Absolute;
                if ((AlarmKind)ki != AlarmKind.Iso && (double.IsNaN(a) || double.IsNaN(d) || (lower ? d > a : d < a)))
                {
                    MessageBox.Show(this, $"Revisá «{fname}»: alerta y peligro deben ser números y peligro {(lower ? "≤" : "≥")} alerta{(lower ? " (en este indicador, menos es peor)" : "")}.", "Alarmas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                list.Add(new AlarmRule(keys[fi], (AlarmKind)ki, double.IsNaN(a) ? 0 : a, double.IsNaN(d) ? 0 : d) { Enabled = row.Cells[4].Value is bool b && b });
            }
            Rules = list;
            return true;
        }
    }
}
