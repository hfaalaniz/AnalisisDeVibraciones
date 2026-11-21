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
    /// <summary>Proyecto .vaproj y exportación del espectro.</summary>
    public sealed partial class MainForm
    {
        // ───────────────────────────── Exportar espectro ─────────────────────────────
        private void ExportSpectrumPng()
        {
            using var sfd = new SaveFileDialog { Filter = "PNG|*.png", FileName = "espectro.png" };
            if (sfd.ShowDialog(this) == DialogResult.OK) spectrum.ExportToPng(sfd.FileName);
        }

        // ───────────────────────────── Proyecto ─────────────────────────────
        private sealed class ProjectFile
        {
            public int Version { get; set; } = 2;
            public Machine Machine { get; set; }
            public int Planes { get; set; }
            public bool AnglesAgainstRotation { get; set; }
            public bool KeepTrialWeights { get; set; }
            public double Tolerance { get; set; }
            public List<BalancingRun> Runs { get; set; }
            public string Technician { get; set; }
            public string Company { get; set; }
            public string Notes { get; set; }
        }

        private static readonly JsonSerializerOptions ProjOpts = new JsonSerializerOptions { WriteIndented = true };

        private void SaveProject(bool askPath)
        {
            if (askPath || projectPath == null)
            {
                using var sfd = new SaveFileDialog { Filter = "Proyecto de balanceo (*.vaproj)|*.vaproj", FileName = Safe(machine.Name) + ".vaproj" };
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                projectPath = sfd.FileName;
            }
            var p = new ProjectFile
            {
                Machine = machine, Planes = session.Planes, AnglesAgainstRotation = session.AnglesAgainstRotation, KeepTrialWeights = session.KeepTrialWeights,
                Tolerance = session.ToleranceVibration, Runs = session.Runs, Technician = txtTech.Text, Company = txtCompany.Text, Notes = txtNotes.Text,
            };
            File.WriteAllText(projectPath, JsonSerializer.Serialize(p, ProjOpts), new UTF8Encoding(false));
            SetStatus("Proyecto guardado: " + projectPath);
        }

        private void OpenProject() => OpenProject(null);

        private void OpenProject(string path)
        {
            if (path == null)
            {
                using var ofd = new OpenFileDialog { Filter = "Proyecto de balanceo (*.vaproj)|*.vaproj" };
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                path = ofd.FileName;
            }
            try
            {
                var p = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path), ProjOpts) ?? throw new InvalidDataException("Archivo vacío.");
                CopyMachine(p.Machine, machine);
                machineGrid.Refresh();
                cmbPlanes.SelectedIndex = p.Planes - 1;
                cmbAngles.SelectedIndex = p.AnglesAgainstRotation ? 0 : 1;
                chkKeepTrial.Checked = p.KeepTrialWeights;
                nudTolerance.Value = Clamp(nudTolerance, p.Tolerance);
                NewSession();
                foreach (var r in p.Runs ?? new List<BalancingRun>())
                {
                    if (session.Step == BalancingStep.Initial) session.RecordInitial(r.Sensor1, r.Sensor2, r.Rpm);
                    else if (session.Step == BalancingStep.Trial1 && r.TrialPlane == 1) session.RecordTrial1(r.Trial.Value, r.Sensor1, r.Sensor2, r.Rpm);
                    else if (session.Step == BalancingStep.Trial2 && r.TrialPlane == 2) session.RecordTrial2(r.Trial.Value, r.Sensor1, r.Sensor2 ?? default, r.Rpm);
                    else if (session.CanVerify) lastTrim = session.RecordVerification(r.Sensor1, r.Sensor2, r.Rpm).Trim;
                }
                txtTech.Text = p.Technician; txtCompany.Text = p.Company; txtNotes.Text = p.Notes;
                projectPath = path;
                tabs.SelectedIndex = tabs.TabPages.Count - 2;   // Balanceo
                UpdateMachineSummary(); RefreshRuns(); ShowResult(); UpdateStepUi();
                SetStatus("Proyecto abierto: " + projectPath);
            }
            catch (Exception ex) { Error("No se pudo abrir el proyecto", ex); }
        }

        private static void CopyMachine(Machine from, Machine to)
        {
            if (from == null) return;
            foreach (var prop in typeof(Machine).GetProperties().Where(p => p.CanWrite)) prop.SetValue(to, prop.GetValue(from));
        }
    }
}
