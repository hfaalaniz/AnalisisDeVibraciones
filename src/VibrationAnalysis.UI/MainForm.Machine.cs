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
    /// <summary>Pestaña 1: datos de la máquina y resumen técnico.</summary>
    public sealed partial class MainForm
    {
        // ───────────────────────────── 1. Máquina ─────────────────────────────
        private TabPage BuildMachineTab()
        {
            var page = new TabPage("1. Máquina");
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 520, FixedPanel = FixedPanel.Panel1 };
            machineGrid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = new MachineView(machine), PropertySort = PropertySort.Categorized, ToolbarVisible = false };
            WideLabels(machineGrid, 0.6);
            machineGrid.PropertyValueChanged += (s, e) => { UpdateMachineSummary(); RefreshAnalysis(); };
            split.Panel1.Controls.Add(machineGrid);
            machineSummary = new Label { Dock = DockStyle.Fill, Padding = new Padding(16), Font = new Font("Segoe UI", 10.5f) };
            split.Panel2.Controls.Add(machineSummary);
            page.Controls.Add(split);
            return page;
        }

        private void UpdateMachineSummary()
        {
            var (planes, reason) = machine.RecommendedPlanes();
            double mass = machine.EffectiveMassKg;
            double uper = BalanceQuality.PermissibleUnbalanceGmm(machine.BalanceGrade, mass, machine.Rpm);
            double trial = TrialWeight.SuggestGrams(mass, machine.Rpm, machine.CorrectionRadiusMm);
            var sb = new StringBuilder();
            sb.AppendLine($"Masa del rotor: {mass:0.0} kg {(machine.MassKg > 0 ? "(informada)" : "(estimada por geometría)")}");
            sb.AppendLine($"Velocidad: {machine.Rpm:0} rpm = {machine.Rpm / 60:0.00} Hz (1X)");
            sb.AppendLine();
            sb.AppendLine($"Planos recomendados: {planes}");
            sb.AppendLine(reason);
            sb.AppendLine();
            sb.AppendLine($"Calidad de balanceo G{machine.BalanceGrade.ToString("0.#", Es)} (ISO 1940): {BalanceQuality.Examples(machine.BalanceGrade)}");
            sb.AppendLine($"Desbalance residual admisible: {uper:0} g·mm en total");
            sb.AppendLine($"  = {uper / machine.CorrectionRadiusMm:0.00} g a {machine.CorrectionRadiusMm:0} mm de radio");
            if (planes == 2) sb.AppendLine($"  = {uper / 2 / machine.CorrectionRadiusMm:0.00} g por plano (reparto simétrico)");
            sb.AppendLine($"Excentricidad admisible: {BalanceQuality.PermissibleEccentricityUm(machine.BalanceGrade, machine.Rpm):0.0} µm");
            sb.AppendLine();
            sb.AppendLine($"Peso de prueba sugerido: {trial:0.0} g a {machine.CorrectionRadiusMm:0} mm (fuerza ≈ 10 % del peso del rotor).");
            if (machine.Bearing is { } b)
            {
                double f = machine.Rpm / 60;
                sb.AppendLine();
                sb.AppendLine($"Frecuencias de rodamiento: BPFO {b.BpfoOrder:0.00}X ({b.BpfoOrder * f:0.0} Hz) · BPFI {b.BpfiOrder:0.00}X ({b.BpfiOrder * f:0.0} Hz) · BSF {b.BsfOrder:0.00}X · FTF {b.FtfOrder:0.000}X");
            }
            sb.AppendLine();
            sb.AppendLine($"Severidad: {Severity.Name(machine.MachineClass)}; límites A/B, B/C, C/D = {string.Join(" / ", Severity.Limits(machine.MachineClass).Select(v => v.ToString("0.0#", Es)))} mm/s RMS.");
            machineSummary.Text = sb.ToString();
            if (lblSuggest != null) lblSuggest.Text = $"Peso de prueba sugerido: {trial:0.0} g";
        }
    }
}
