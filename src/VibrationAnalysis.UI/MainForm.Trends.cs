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
    /// <summary>Pestaña 6: base de tendencias, alarmas y rutas.</summary>
    public sealed partial class MainForm
    {
        // ───────────────────────────── 6. Tendencias ─────────────────────────────
        private static string TrendPathFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnalisisVibraciones", "tendencias.ruta");
        private static string DefaultTrendFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnalisisVibraciones", "Tendencias");

        private TabPage BuildTrendTab()
        {
            trendPage = new TabPage("6. Tendencias");
            var bar1 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 4, 6, 0), WrapContents = true };
            bar1.Controls.AddRange(new Control[]
            {
                Btn("Carpeta de la base…", (s, e) => ChooseTrendFolder()),
                Btn("+ Planta", (s, e) => AddPlant()), Btn("+ Máquina", (s, e) => AddMachineAsset()), Btn("+ Punto", (s, e) => AddPoint()),
                Btn("Renombrar", (s, e) => RenameNode()), Btn("Eliminar", (s, e) => DeleteNode()),
                Btn("Máquina ← pestaña 1", (s, e) => CopyMachineToAsset()), Btn("Máquina → pestaña 1", (s, e) => LoadAssetMachine()),
                Btn("Alarmas…", (s, e) => EditAlarms()),
                Btn("Demo (12 meses)", (s, e) => CreateTrendDemo()),
            });
            var bar2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 0, 6, 4), WrapContents = true };
            cmbTrendFeature = Combo(FeatureKeys.Names.Values, 260);
            cmbTrendFeature.SelectedIndexChanged += (s, e) => ShowTrend();
            chkSaveWave = new CheckBox { Text = "Guardar también la señal", AutoSize = true, Checked = true, Margin = new Padding(8, 8, 4, 0) };
            chkTemp = new CheckBox { Text = "Temperatura (°C):", AutoSize = true, Margin = new Padding(8, 8, 0, 0) };
            nudTemp = Nud(-50, 400, 40, 1); nudTemp.Width = 65;
            nudTemp.ValueChanged += (s, e) => chkTemp.Checked = true;
            cmbRoute = Combo(new string[0], 180);
            cmbRoute.SelectedIndexChanged += (s, e) => { routeIndex = cmbRoute.SelectedIndex > 0 ? 0 : -1; UpdateRouteLabel(); };
            lblRoute = new Label { AutoSize = true, Margin = new Padding(6, 8, 0, 0), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            bar2.Controls.AddRange(new Control[]
            {
                Btn("Guardar medición actual", (s, e) => SaveCurrentMeasurement()), chkSaveWave, chkTemp, nudTemp,
                Lbl("Indicador:"), cmbTrendFeature,
                Btn("Marcar línea base", (s, e) => SetBaseline()), Btn("Eliminar medición", (s, e) => DeleteSelectedMeasurement()), Btn("Exportar CSV", (s, e) => ExportTrend()),
                Lbl("Ruta:"), cmbRoute, Btn("Nueva ruta…", (s, e) => NewRoute()), Btn("◀", (s, e) => MoveRoute(-1)), Btn("▶", (s, e) => MoveRoute(1)), lblRoute,
            });
            tvAssets = new TreeView { Dock = DockStyle.Fill, HideSelection = false, Font = new Font("Segoe UI", 10f) };
            tvAssets.AfterSelect += (s, e) => ShowTrend();
            lblDbPath = new Label { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(4), Font = new Font("Segoe UI", 8f) };
            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(tvAssets); left.Controls.Add(lblDbPath);

            lblTrendSummary = new Label { Dock = DockStyle.Top, Height = 52, Padding = new Padding(8, 4, 8, 4), Font = new Font("Consolas", 9.5f) };
            trendPlot = new XYPlotControl { Dock = DockStyle.Fill, XLabel = "Fecha", ShowPoints = true, XFormatter = d => DateTime.FromOADate(d).ToString("dd/MM/yy", Es) };
            lvMeas = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, MultiSelect = true };
            AddColumns(lvMeas, ("Fecha", 120), ("rpm", 60), ("Valor", 80), ("Nivel", 70), ("Señal", 50), ("Base", 45));
            lvMeas.DoubleClick += (s, e) => OpenMeasurementWaveform();
            lvAlarms = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            AddColumns(lvAlarms, ("Nivel", 70), ("Indicador", 230), ("Valor", 70), ("Alerta", 70), ("Peligro", 70));
            var lists = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
            var gm = new GroupBox { Text = "Mediciones (doble clic: abrir la señal)", Dock = DockStyle.Fill }; gm.Controls.Add(lvMeas);
            var ga = new GroupBox { Text = "Alarmas de la última medición", Dock = DockStyle.Fill }; ga.Controls.Add(lvAlarms);
            lists.Panel1.Controls.Add(gm); lists.Panel2.Controls.Add(ga);
            var center = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            center.Panel1.Controls.Add(trendPlot); center.Panel1.Controls.Add(lblTrendSummary);
            center.Panel2.Controls.Add(lists);
            center.HandleCreated += (s, e) => { try { center.SplitterDistance = Math.Max(150, center.Height * 3 / 5); } catch { } };
            var main = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel1 };
            main.Panel1.Controls.Add(left); main.Panel2.Controls.Add(center);
            main.HandleCreated += (s, e) => { try { main.SplitterDistance = 300; } catch { } };
            trendPage.Controls.Add(main);
            trendPage.Controls.Add(bar2);
            trendPage.Controls.Add(bar1);
            return trendPage;
        }

        private void EnsureTrendDb()
        {
            if (trendDb != null) return;
            try
            {
                string folder = File.Exists(TrendPathFile) ? File.ReadAllText(TrendPathFile).Trim() : DefaultTrendFolder;
                if (string.IsNullOrWhiteSpace(folder)) folder = DefaultTrendFolder;
                OpenTrendDb(folder);
            }
            catch (Exception ex) { Error("No se pudo abrir la base de tendencias", ex); }
        }

        private void OpenTrendDb(string folder)
        {
            trendDb = new TrendDatabase(folder);
            Directory.CreateDirectory(Path.GetDirectoryName(TrendPathFile));
            File.WriteAllText(TrendPathFile, folder);
            lblDbPath.Text = "Base: " + folder;
            RefreshTree();
            RefreshRoutes();
        }

        private void ChooseTrendFolder()
        {
            using var dlg = new FolderBrowserDialog { Description = "Carpeta de la base de mediciones (se crea si está vacía)", UseDescriptionForTitle = true, SelectedPath = trendDb?.Folder ?? DefaultTrendFolder };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try { OpenTrendDb(dlg.SelectedPath); SetStatus("Base de tendencias: " + dlg.SelectedPath); }
            catch (Exception ex) { Error("No se pudo abrir la carpeta", ex); }
        }

        private void RefreshTree(string selectId = null)
        {
            if (trendDb == null) return;
            selectId ??= (tvAssets.SelectedNode?.Tag as MeasurementPoint)?.Id ?? (tvAssets.SelectedNode?.Tag as MachineAsset)?.Id ?? (tvAssets.SelectedNode?.Tag as Plant)?.Id;
            tvAssets.BeginUpdate();
            tvAssets.Nodes.Clear();
            TreeNode select = null;
            foreach (var p in trendDb.Catalog.Plants)
            {
                var pn = new TreeNode("🏭 " + p.Name) { Tag = p };
                var worstPlant = AlarmLevel.Normal;
                foreach (var m in p.Machines)
                {
                    var mn = new TreeNode("⚙ " + m.Name + (string.IsNullOrEmpty(m.Area) ? "" : $" ({m.Area})")) { Tag = m };
                    var worstMachine = AlarmLevel.Normal;
                    foreach (var pt in m.Points)
                    {
                        var hist = trendDb.Measurements(pt.Id);
                        var lvl = hist.Count > 0 ? TrendAnalysis.WorstLevel(TrendAnalysis.Evaluate(hist[^1], pt, m.Machine.MachineClass, hist)) : AlarmLevel.Normal;
                        var tn = new TreeNode($"● {pt.Name} [{hist.Count}]") { Tag = pt, ForeColor = LevelColor(lvl) };
                        if (lvl > worstMachine) worstMachine = lvl;
                        mn.Nodes.Add(tn);
                        if (pt.Id == selectId) select = tn;
                    }
                    mn.ForeColor = LevelColor(worstMachine);
                    if (worstMachine > worstPlant) worstPlant = worstMachine;
                    pn.Nodes.Add(mn);
                    if (m.Id == selectId) select = mn;
                }
                pn.ForeColor = LevelColor(worstPlant);
                tvAssets.Nodes.Add(pn);
                if (p.Id == selectId) select = pn;
            }
            tvAssets.ExpandAll();
            tvAssets.EndUpdate();
            if (select != null) tvAssets.SelectedNode = select;
            else ShowTrend();
        }

        private static Color LevelColor(AlarmLevel l) => l switch
        {
            AlarmLevel.Danger => Color.FromArgb(200, 40, 40),
            AlarmLevel.Alert => Color.FromArgb(210, 130, 0),
            _ => Color.FromArgb(30, 140, 70),
        };

        private static string LevelText(AlarmLevel l) => l switch { AlarmLevel.Danger => "PELIGRO", AlarmLevel.Alert => "ALERTA", _ => "Normal" };

        private (Plant Plant, MachineAsset Machine, MeasurementPoint Point) Selection()
        {
            var n = tvAssets.SelectedNode;
            if (n == null) return (null, null, null);
            if (n.Tag is MeasurementPoint pt) return ((Plant)n.Parent.Parent.Tag, (MachineAsset)n.Parent.Tag, pt);
            if (n.Tag is MachineAsset m) return ((Plant)n.Parent.Tag, m, null);
            return ((Plant)n.Tag, null, null);
        }

        private string SelectedFeature => FeatureKeys.Names.Keys.ElementAt(Math.Max(0, cmbTrendFeature.SelectedIndex));

        private void AddPlant()
        {
            EnsureTrendDb();
            var name = PromptForm.Ask(this, "Nueva planta", "Nombre de la planta o cliente:", "Planta");
            if (name == null) return;
            var p = new Plant { Name = name };
            trendDb.Catalog.Plants.Add(p); trendDb.Save(); RefreshTree(p.Id);
        }

        private void AddMachineAsset()
        {
            EnsureTrendDb();
            var (plant, _, _) = Selection();
            if (plant == null) { if (trendDb.Catalog.Plants.Count == 0) AddPlant(); plant = Selection().Plant ?? trendDb.Catalog.Plants.FirstOrDefault(); }
            if (plant == null) return;
            using var dlg = new PromptForm("Nueva máquina", "Nombre de la máquina (p. ej. «Ventilador VT-01»):", machine.Name, "Área / sector:", new[] { "(sin área)", "Producción", "Servicios", "Mantenimiento" });
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Value.Length == 0) return;
            var m = new MachineAsset { Name = dlg.Value, Area = dlg.Option > 0 ? new[] { "", "Producción", "Servicios", "Mantenimiento" }[dlg.Option] : "" };
            CopyMachine(machine, m.Machine);
            m.Machine.Name = m.Name;
            plant.Machines.Add(m); trendDb.Save(); RefreshTree(m.Id);
            SetStatus($"Máquina «{m.Name}» creada con los datos técnicos de la pestaña 1.");
        }

        private static readonly string[] Directions = { "Horizontal", "Vertical", "Axial", "Radial" };

        private void AddPoint()
        {
            EnsureTrendDb();
            var (_, m, _) = Selection();
            if (m == null) { Warn("Seleccioná una máquina en el árbol."); return; }
            using var dlg = new PromptForm("Nuevo punto", "Nombre del punto (p. ej. «Motor lado acople»):", "Lado acople", "Dirección:", Directions);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Value.Length == 0) return;
            var pt = new MeasurementPoint { Name = $"{dlg.Value} · {Directions[Math.Max(0, dlg.Option)]}", Direction = (MeasureDirection)Math.Max(0, dlg.Option) };
            m.Points.Add(pt); trendDb.Save(); RefreshTree(pt.Id);
        }

        private void RenameNode()
        {
            var (p, m, pt) = Selection();
            if (p == null) return;
            string current = pt?.Name ?? m?.Name ?? p.Name;
            var name = PromptForm.Ask(this, "Renombrar", "Nuevo nombre:", current);
            if (name == null) return;
            if (pt != null) pt.Name = name; else if (m != null) { m.Name = name; m.Machine.Name = name; } else p.Name = name;
            trendDb.Save(); RefreshTree();
        }

        private void DeleteNode()
        {
            var (p, m, pt) = Selection();
            if (p == null) return;
            string what = pt != null ? $"el punto «{pt.Name}»" : m != null ? $"la máquina «{m.Name}»" : $"la planta «{p.Name}»";
            if (MessageBox.Show(this, $"¿Eliminar {what} con todas sus mediciones? No se puede deshacer.", "Eliminar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var points = pt != null ? new[] { pt } : m != null ? m.Points.ToArray() : p.Machines.SelectMany(x => x.Points).ToArray();
            foreach (var x in points) trendDb.DeletePoint(x.Id);
            if (pt == null && m != null) p.Machines.Remove(m);
            if (pt == null && m == null) trendDb.Catalog.Plants.Remove(p);
            trendDb.Save(); RefreshTree(); RefreshRoutes();
        }

        private void CopyMachineToAsset()
        {
            var (_, m, _) = Selection();
            if (m == null) { Warn("Seleccioná una máquina o un punto."); return; }
            CopyMachine(machine, m.Machine); m.Machine.Name = m.Name;
            trendDb.Save(); ShowTrend();
            SetStatus($"Datos técnicos de la pestaña 1 copiados a «{m.Name}» (velocidad, rodamiento, engranaje, clase ISO).");
        }

        private void LoadAssetMachine()
        {
            var (_, m, _) = Selection();
            if (m == null) { Warn("Seleccioná una máquina o un punto."); return; }
            CopyMachine(m.Machine, machine);
            machineGrid.Refresh(); UpdateMachineSummary();
            SetStatus($"Datos de «{m.Name}» cargados en la pestaña 1.");
        }

        private void EditAlarms()
        {
            var (_, _, pt) = Selection();
            if (pt == null) { Warn("Seleccioná un punto de medición."); return; }
            using var dlg = new AlarmEditorForm(pt.Name, pt.Alarms);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            pt.Alarms = dlg.Rules; trendDb.Save(); RefreshTree(pt.Id);
        }

        private void CreateTrendDemo()
        {
            EnsureTrendDb();
            Cursor = Cursors.WaitCursor;
            try { var p = trendDb.CreateDemo(); RefreshTree(p.Machines[0].Points[0].Id); RefreshRoutes(); SetStatus("Planta de demostración creada con 12 meses de mediciones simuladas."); }
            catch (Exception ex) { Error("No se pudo crear la demo", ex); }
            finally { Cursor = Cursors.Default; }
        }

        private MeasurementPoint TargetPoint(out MachineAsset asset)
        {
            asset = null;
            if (routeIndex >= 0 && cmbRoute.SelectedIndex > 0)
            {
                var route = trendDb.Catalog.Routes[cmbRoute.SelectedIndex - 1];
                if (routeIndex < route.PointIds.Count && trendDb.FindPoint(route.PointIds[routeIndex]) is { } f) { asset = f.Machine; return f.Point; }
            }
            var (_, m, pt) = Selection();
            asset = m;
            return pt;
        }

        private void SaveCurrentMeasurement()
        {
            EnsureTrendDb();
            if (recording == null || cmbChannel.SelectedIndex < 0) { Warn("Primero adquirí una señal (pestaña 2)."); return; }
            var pt = TargetPoint(out var asset);
            if (pt == null) { Warn("Seleccioná un punto de medición en el árbol (o una ruta)."); return; }
            try
            {
                var rec = FeatureExtractor.Extract(recording, cmbChannel.SelectedIndex, asset.Machine, pt.Id);
                rec.Notes = recording.Source;
                if (chkTemp.Checked) rec.Features[FeatureKeys.Temperature] = (double)nudTemp.Value;
                trendDb.AddMeasurement(rec, chkSaveWave.Checked ? recording : null);
                var hist = trendDb.Measurements(pt.Id);
                var lvl = TrendAnalysis.WorstLevel(TrendAnalysis.Evaluate(rec, pt, asset.Machine.MachineClass, hist));
                SetStatus($"Medición guardada en «{asset.Name} · {pt.Name}»: {LevelText(lvl)}.");
                OnMeasurementSaved(asset, pt, rec, lvl, TrendAnalysis.Evaluate(rec, pt, asset.Machine.MachineClass, hist));
                bool onRoute = routeIndex >= 0;
                RefreshTree(pt.Id);
                if (onRoute) MoveRoute(1);
            }
            catch (Exception ex) { Error("No se pudo guardar la medición", ex); }
        }

        private void ShowTrend()
        {
            if (trendDb == null || trendPlot == null) return;
            lvMeas.Items.Clear(); lvAlarms.Items.Clear();
            trendPlot.HorizontalLines.Clear(); trendPlot.Markers.Clear(); trendPlot.Peaks.Clear();
            var (plant, m, pt) = Selection();
            if (pt == null)
            {
                trendPlot.Title = "";
                trendPlot.SetData(Array.Empty<double>(), Array.Empty<double>());
                lblTrendSummary.Text = m != null ? $"«{m.Name}»: {m.Points.Count} puntos · {m.Machine.Rpm:0} rpm · {Severity.Name(m.Machine.MachineClass)}{(m.Machine.Bearing != null ? " · rodamiento cargado" : "")}.\nSeleccioná un punto para ver su tendencia."
                    : plant != null ? $"«{plant.Name}»: {plant.Machines.Count} máquinas. Seleccioná un punto." : "Creá una planta, una máquina y un punto, o tocá «Demo (12 meses)» para ver un ejemplo.";
                return;
            }
            var hist = trendDb.Measurements(pt.Id);
            string key = SelectedFeature;
            var series = TrendAnalysis.Series(hist, key);
            trendPlot.Title = $"{m.Name} · {pt.Name} · {FeatureKeys.Name(key)}";
            trendPlot.YLabel = FeatureKeys.Name(key);
            trendPlot.SetData(series.Select(p => p.Time.ToOADate()).ToArray(), series.Select(p => p.Value).ToArray());
            var baseline = TrendAnalysis.Baseline(pt, hist);
            var baseIds = baseline.Select(b => b.Id).ToHashSet();
            var rule = pt.Alarms.FirstOrDefault(a => a.Enabled && a.Feature == key);
            double alert = double.NaN, danger = double.NaN;
            if (rule != null)
            {
                (alert, danger) = TrendAnalysis.Limits(rule, m.Machine.MachineClass, baseline);
                if (!double.IsNaN(alert)) trendPlot.HorizontalLines.Add(new XYPlotControl.HLine(alert, $"Alerta {FormatValue(alert)}", Color.FromArgb(210, 130, 0)));
                if (!double.IsNaN(danger)) trendPlot.HorizontalLines.Add(new XYPlotControl.HLine(danger, $"Peligro {FormatValue(danger)}", Color.FromArgb(200, 40, 40)));
            }
            if (series.Count == 1) trendPlot.SetView(series[0].Time.AddDays(-15).ToOADate(), series[0].Time.AddDays(15).ToOADate());
            else if (series.Count > 1) { double span = series[^1].Time.ToOADate() - series[0].Time.ToOADate(); trendPlot.SetView(series[0].Time.ToOADate() - span * 0.03, series[^1].Time.ToOADate() + span * 0.03); }
            trendPlot.Invalidate();

            foreach (var r in hist.Reverse())
            {
                var lvl = TrendAnalysis.WorstLevel(TrendAnalysis.Evaluate(r, pt, m.Machine.MachineClass, hist));
                lvMeas.Items.Add(new ListViewItem(new[]
                {
                    r.Timestamp.ToString("dd/MM/yyyy HH:mm", Es), $"{r.Rpm:0}", double.IsNaN(r.Get(key)) ? "—" : FormatValue(r.Get(key)), LevelText(lvl),
                    string.IsNullOrEmpty(r.WaveformFile) ? "" : "sí", baseIds.Contains(r.Id) ? "✓" : "",
                }) { Tag = r, ForeColor = LevelColor(lvl) });
            }
            string forecast = "";
            if (hist.Count > 0)
            {
                foreach (var a in TrendAnalysis.Evaluate(hist[^1], pt, m.Machine.MachineClass, hist).OrderByDescending(a => a.Level))
                    lvAlarms.Items.Add(new ListViewItem(new[] { LevelText(a.Level), FeatureKeys.Name(a.Feature), FormatValue(a.Value), FormatValue(a.AlertLimit), FormatValue(a.DangerLimit) }) { ForeColor = LevelColor(a.Level) });
                var fit = TrendAnalysis.Fit(series);
                if (fit != null)
                {
                    string kind = fit.Exponential ? "exponencial" : "lineal";
                    string rate = fit.Exponential ? $"{(Math.Exp(fit.SlopePerDay * 30) - 1) * 100:+0;-0} %/mes" : $"{fit.SlopePerDay * 30:+0.###;-0.###}/mes";
                    forecast = $"Tendencia {kind} ({rate}, R² {fit.R2:0.00}).";
                    double last = series[^1].Value;
                    foreach (var (lim, name) in new[] { (alert, "alerta"), (danger, "peligro") })
                    {
                        if (double.IsNaN(lim) || last >= lim) continue;
                        var when = fit.Crossing(lim);
                        if (when.HasValue && when.Value > DateTime.Now) forecast += $"  Llega a {name} ≈ {when.Value:dd/MM/yyyy} ({(when.Value - DateTime.Now).TotalDays:0} días).";
                    }
                }
            }
            var worst = hist.Count > 0 ? TrendAnalysis.WorstLevel(TrendAnalysis.Evaluate(hist[^1], pt, m.Machine.MachineClass, hist)) : AlarmLevel.Normal;
            lblTrendSummary.Text = $"{hist.Count} mediciones · línea base: {baseline.Count} · estado actual: {LevelText(worst)}" +
                                   (hist.Count > 0 ? $" · última {hist[^1].Timestamp:dd/MM/yyyy}" : "") + "\n" + (forecast.Length > 0 ? forecast : "Se necesitan 3 mediciones o más para estimar la tendencia.");
            lblTrendSummary.ForeColor = worst == AlarmLevel.Normal ? lblTrendSummary.Parent?.ForeColor ?? Color.Black : LevelColor(worst);
        }

        private void SetBaseline()
        {
            var (_, _, pt) = Selection();
            if (pt == null || lvMeas.SelectedItems.Count == 0) { Warn("Seleccioná un punto y una o más mediciones de la lista (máquina en buen estado) para usarlas como línea base."); return; }
            pt.BaselineIds = lvMeas.SelectedItems.Cast<ListViewItem>().Select(i => ((MeasurementRecord)i.Tag).Id).ToList();
            trendDb.Save(); RefreshTree(pt.Id);
            SetStatus($"Línea base de «{pt.Name}»: {pt.BaselineIds.Count} medición(es). Las alarmas relativas y estadísticas se recalcularon.");
        }

        private void DeleteSelectedMeasurement()
        {
            var (_, _, pt) = Selection();
            if (pt == null || lvMeas.SelectedItems.Count == 0) { Warn("Seleccioná las mediciones a eliminar."); return; }
            if (MessageBox.Show(this, $"¿Eliminar {lvMeas.SelectedItems.Count} medición(es)?", "Eliminar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            foreach (ListViewItem i in lvMeas.SelectedItems) trendDb.DeleteMeasurement(pt.Id, ((MeasurementRecord)i.Tag).Id);
            RefreshTree(pt.Id);
        }

        private void OpenMeasurementWaveform()
        {
            if (lvMeas.SelectedItems.Count == 0) return;
            var r = (MeasurementRecord)lvMeas.SelectedItems[0].Tag;
            var w = trendDb.LoadWaveform(r);
            if (w == null) { Warn("Esa medición no tiene la señal guardada (solo indicadores)."); return; }
            cmbSource.SelectedIndex = (int)Source.File;
            SetRecording(w with { Source = $"Base · {r.Timestamp:dd/MM/yyyy HH:mm}" });
            tabs.SelectedIndex = 1;
            SetStatus($"Señal del {r.Timestamp:dd/MM/yyyy HH:mm} cargada: se puede analizar en las pestañas 2 a 5.");
        }

        private void ExportTrend()
        {
            var (_, m, pt) = Selection();
            if (pt == null) { Warn("Seleccioná un punto de medición."); return; }
            using var sfd = new SaveFileDialog { Filter = "CSV|*.csv", FileName = Safe($"{m.Name}_{pt.Name}") + ".csv" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(sfd.FileName, trendDb.ExportCsv(pt.Id), new UTF8Encoding(false));
            SetStatus("Tendencia exportada: " + sfd.FileName);
        }

        private void RefreshRoutes()
        {
            if (trendDb == null) return;
            int sel = cmbRoute.SelectedIndex;
            cmbRoute.Items.Clear();
            cmbRoute.Items.Add("(sin ruta)");
            foreach (var r in trendDb.Catalog.Routes) cmbRoute.Items.Add($"{r.Name} ({r.PointIds.Count})");
            cmbRoute.SelectedIndex = Math.Min(Math.Max(0, sel), cmbRoute.Items.Count - 1);
            UpdateRouteLabel();
        }

        private void NewRoute()
        {
            EnsureTrendDb();
            var (p, m, _) = Selection();
            if (p == null) { Warn("Seleccioná una planta o una máquina: la ruta incluye todos sus puntos en orden."); return; }
            var ids = (m != null ? m.Points : p.Machines.SelectMany(x => x.Points)).Select(x => x.Id).ToList();
            if (ids.Count == 0) { Warn("No hay puntos para la ruta."); return; }
            var name = PromptForm.Ask(this, "Nueva ruta", "Nombre de la ruta:", $"Ruta {(m?.Name ?? p.Name)}");
            if (name == null) return;
            trendDb.Catalog.Routes.Add(new MeasurementRoute { Name = name, PointIds = ids });
            trendDb.Save(); RefreshRoutes();
            cmbRoute.SelectedIndex = cmbRoute.Items.Count - 1;
        }

        private void MoveRoute(int delta)
        {
            if (cmbRoute.SelectedIndex <= 0) return;
            var route = trendDb.Catalog.Routes[cmbRoute.SelectedIndex - 1];
            routeIndex = Math.Clamp(routeIndex + delta, 0, Math.Max(0, route.PointIds.Count - 1));
            UpdateRouteLabel();
            if (routeIndex < route.PointIds.Count) RefreshTree(route.PointIds[routeIndex]);
        }

        private void UpdateRouteLabel()
        {
            if (lblRoute == null) return;
            if (trendDb == null || cmbRoute.SelectedIndex <= 0 || routeIndex < 0) { lblRoute.Text = ""; return; }
            var route = trendDb.Catalog.Routes[cmbRoute.SelectedIndex - 1];
            if (route.PointIds.Count == 0) { lblRoute.Text = "ruta vacía"; return; }
            var f = trendDb.FindPoint(route.PointIds[Math.Min(routeIndex, route.PointIds.Count - 1)]);
            lblRoute.Text = f is { } x ? $"Punto {routeIndex + 1}/{route.PointIds.Count}: {x.Machine.Name} · {x.Point.Name}" : "punto inexistente";
        }
    }
}
