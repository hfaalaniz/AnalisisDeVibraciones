using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VibrationAnalysis.Core.Analysis;
using VibrationAnalysis.Core.Network;
using VibrationAnalysis.Core.Trending;

namespace VibrationAnalysis.UI
{
    /// <summary>Configuración de red guardada (sin contraseñas).</summary>
    public sealed class NetworkSettings
    {
        public string MqttHost { get; set; } = "localhost";
        public int MqttPort { get; set; } = 1883;
        public string MqttUser { get; set; } = "";
        public string TopicPrefix { get; set; } = "vibraciones";
        public bool PublishMeasurements { get; set; } = true;
        public bool SubscribeSensors { get; set; } = true;
        public bool StoreSensorMessages { get; set; }
        /// <summary>Sensor de red (id) → punto de la base de tendencias.</summary>
        public Dictionary<string, string> SensorPoints { get; set; } = new();
        public int ModbusPort { get; set; } = 502;
        public string ClientMode { get; set; } = "TCP";
        public string ClientAddress { get; set; } = "192.168.1.20:502";
        public int ClientBaud { get; set; } = 19200;
        public int ClientUnit { get; set; } = 1;
        public int ClientFunction { get; set; } = 3;
        public int ClientRegister { get; set; }
        public int ClientType { get; set; }
        public double ClientScale { get; set; } = 1;
        public bool ClientAsRpm { get; set; }
    }

    public sealed partial class MainForm
    {
        // Sensor por red (pestaña 2)
        private TextBox txtNetSensor;
        private TcpClient netSensor;
        private CancellationTokenSource netCts;

        // Pestaña 7
        private NetworkSettings net = new NetworkSettings();
        private readonly MqttClient mqtt = new MqttClient();
        private readonly ModbusTcpServer modbus = new ModbusTcpServer();
        private readonly System.Windows.Forms.Timer netTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        private TextBox txtMqttHost, txtMqttUser, txtMqttPass, txtPrefix, txtClientAddr;
        private NumericUpDown nudMqttPort, nudModbusPort, nudClientBaud, nudClientUnit, nudClientReg, nudClientScale;
        private CheckBox chkPublish, chkSubscribe, chkStoreSensors, chkClientRpm, chkClientPoll;
        private ComboBox cmbClientMode, cmbClientFunc, cmbClientType;
        private Button btnMqtt, btnModbus;
        private Label lblMqtt, lblModbus, lblClient;
        private ListView lvSensors, lvRegisters;
        private ushort modbusCounter;
        private bool clientBusy;
        private readonly Dictionary<string, (DateTime Time, Dictionary<string, double> F, double Rpm)> sensorLast = new();

        private static string NetPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnalisisVibraciones", "red.json");

        // ───────────── Sensor por red (WiFi/TCP) ─────────────
        private void ToggleNetSensor()
        {
            if (netSensor?.Connected == true) { CloseNetSensor(); SetStatus("Sensor de red desconectado."); return; }
            var parts = txtNetSensor.Text.Trim().Split(':');
            string host = parts[0];
            int portNo = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 3333;
            try
            {
                CloseNetSensor();
                var c = new TcpClient { NoDelay = true };
                if (!c.ConnectAsync(host, portNo).Wait(4000)) throw new TimeoutException($"{host}:{portNo} no responde. Revisá que el ESP32 esté en la misma red.");
                netSensor = c;
                netCts = new CancellationTokenSource();
                lock (serialParser) serialParser.Clear();
                var s = c.GetStream();
                string pre = cmbSerialMode.SelectedIndex == 1 ? "TEXT\n" : cmbSerialMode.SelectedIndex == 2 ? "BIN\n" : "";
                var cmd = Encoding.ASCII.GetBytes(pre + "START\n");
                s.Write(cmd, 0, cmd.Length);
                var token = netCts.Token;
                Task.Run(async () =>
                {
                    var buf = new byte[65536];
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            int n = await s.ReadAsync(buf.AsMemory(0, buf.Length), token);
                            if (n == 0) break;
                            lock (serialParser) serialParser.Feed(buf, 0, n);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { LogManager.Instance.LogError("Sensor de red: " + ex.Message); }
                });
                btnConnect.Text = "Desconectar";
                linkTimer.Start();
                SetStatus($"Conectado al sensor {host}:{portNo}. Esperando la cabecera #VA…");
            }
            catch (Exception ex) { Error("No se pudo conectar al sensor de red", ex is AggregateException a ? a.InnerException ?? ex : ex); CloseNetSensor(); }
        }

        private void CloseNetSensor()
        {
            try { if (netSensor?.Connected == true) netSensor.GetStream().Write(Encoding.ASCII.GetBytes("STOP\n")); } catch { }
            netCts?.Cancel();
            try { netSensor?.Close(); } catch { }
            netSensor = null;
            if (btnConnect != null && CurrentSource == Source.Network) btnConnect.Text = "Conectar";
        }

        // ───────────── Pestaña 7: red y planta ─────────────
        private TabPage BuildNetworkTab()
        {
            LoadNetSettings();
            var page = new TabPage("7. Red y planta");
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(6) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

            // MQTT
            var gM = new GroupBox { Text = "MQTT (SCADA, Node-RED, plataformas IIoT)", Dock = DockStyle.Fill };
            var fM = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            txtMqttHost = new TextBox { Width = 140, Text = net.MqttHost };
            nudMqttPort = Nud(1, 65535, net.MqttPort, 0); nudMqttPort.Width = 70;
            txtMqttUser = new TextBox { Width = 90, Text = net.MqttUser };
            txtMqttPass = new TextBox { Width = 90, UseSystemPasswordChar = true };
            txtPrefix = new TextBox { Width = 110, Text = net.TopicPrefix };
            btnMqtt = Btn("Conectar", async (s, e) => await ToggleMqtt());
            chkPublish = new CheckBox { Text = "Publicar cada medición guardada", AutoSize = true, Checked = net.PublishMeasurements, Margin = new Padding(6, 6, 6, 0) };
            chkSubscribe = new CheckBox { Text = "Recibir indicadores de sensores", AutoSize = true, Checked = net.SubscribeSensors, Margin = new Padding(6, 6, 6, 0) };
            chkStoreSensors = new CheckBox { Text = "Guardar en la base los de sensores asociados", AutoSize = true, Checked = net.StoreSensorMessages, Margin = new Padding(6, 6, 6, 0) };
            lblMqtt = new Label { AutoSize = true, Margin = new Padding(6, 6, 0, 0), ForeColor = Color.FromArgb(14, 124, 134) };
            fM.Controls.AddRange(new Control[]
            {
                Lbl("Broker:"), txtMqttHost, nudMqttPort, Lbl("Usuario:"), txtMqttUser, Lbl("Clave:"), txtMqttPass, Lbl("Prefijo:"), txtPrefix, btnMqtt,
                chkPublish, chkSubscribe, chkStoreSensors, Btn("Asociar sensor → punto de Tendencias", (s, e) => LinkSensorToPoint()), lblMqtt,
            });
            lvSensors = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            AddColumns(lvSensors, ("Sensor", 80), ("Hora", 70), ("vel RMS", 70), ("acc RMS", 70), ("Cresta", 60), ("rpm", 60), ("Otros", 140), ("Punto", 180));
            var help = new Label { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(4), Text = "Publica en «<prefijo>/<planta>/<máquina>/<punto>/medicion» (JSON, retenido) y escucha «<prefijo>/+/indicadores» (firmware con USE_MQTT=1)." };
            gM.Controls.Add(lvSensors); gM.Controls.Add(help); gM.Controls.Add(fM);

            // Servidor Modbus
            var gS = new GroupBox { Text = "Servidor Modbus TCP (el PLC lee los resultados)", Dock = DockStyle.Fill };
            var fS = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            nudModbusPort = Nud(1, 65535, net.ModbusPort, 0); nudModbusPort.Width = 70;
            btnModbus = Btn("Iniciar", (s, e) => ToggleModbus());
            lblModbus = new Label { AutoSize = true, Margin = new Padding(6, 8, 0, 0), ForeColor = Color.FromArgb(14, 124, 134) };
            fS.Controls.AddRange(new Control[] { Lbl("Puerto:"), nudModbusPort, btnModbus, Btn("Exportar mapa CSV", (s, e) => ExportRegisterMap()), lblModbus });
            lvRegisters = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            AddColumns(lvRegisters, ("Registro", 70), ("Descripción", 260), ("Tipo", 90), ("Unidad", 70), ("Valor", 90));
            gS.Controls.Add(lvRegisters); gS.Controls.Add(fS);

            // Cliente Modbus
            var gC = new GroupBox { Text = "Leer de un PLC o variador (Modbus TCP / RTU)", Dock = DockStyle.Fill };
            var fC = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
            cmbClientMode = Combo(new[] { "TCP", "RTU (serie/RS-485)" }, 140); cmbClientMode.SelectedIndex = net.ClientMode == "RTU" ? 1 : 0;
            txtClientAddr = new TextBox { Width = 150, Text = net.ClientAddress };
            nudClientBaud = Nud(1200, 921600, net.ClientBaud, 0); nudClientBaud.Width = 80;
            nudClientUnit = Nud(0, 247, net.ClientUnit, 0); nudClientUnit.Width = 55;
            cmbClientFunc = Combo(new[] { "03 retención", "04 entrada" }, 110); cmbClientFunc.SelectedIndex = net.ClientFunction == 4 ? 1 : 0;
            nudClientReg = Nud(0, 65535, net.ClientRegister, 0); nudClientReg.Width = 75;
            cmbClientType = Combo(new[] { "uint16", "int16", "float32 ABCD", "float32 CDAB" }, 110); cmbClientType.SelectedIndex = Math.Clamp(net.ClientType, 0, 3);
            nudClientScale = Nud(-100000, 100000, (decimal)net.ClientScale, 4); nudClientScale.Width = 85;
            chkClientRpm = new CheckBox { Text = "Usar como velocidad de giro (rpm)", AutoSize = true, Checked = net.ClientAsRpm, Margin = new Padding(6, 6, 6, 0) };
            chkClientPoll = new CheckBox { Text = "Leer cada 2 s", AutoSize = true, Margin = new Padding(6, 6, 6, 0) };
            lblClient = new Label { AutoSize = true, Margin = new Padding(6, 8, 0, 0), Font = new Font("Consolas", 10f, FontStyle.Bold) };
            fC.Controls.AddRange(new Control[]
            {
                Lbl("Modo:"), cmbClientMode, Lbl("Dirección (IP:puerto o COMx):"), txtClientAddr, Lbl("Baudios:"), nudClientBaud, Lbl("Unidad:"), nudClientUnit,
                Lbl("Función:"), cmbClientFunc, Lbl("Registro (base 0):"), nudClientReg, Lbl("Tipo:"), cmbClientType, Lbl("Escala:"), nudClientScale,
                chkClientRpm, chkClientPoll, Btn("Leer", async (s, e) => await ReadClient(true)), lblClient,
            });
            gC.Controls.Add(fC);

            // Ayuda
            var gH = new GroupBox { Text = "Integración", Dock = DockStyle.Fill };
            gH.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, Padding = new Padding(8),
                Text = "• Sensor por red: elegí «Sensor por red (WiFi/TCP)» en la pestaña 2 con la IP del ESP32 (firmware con USE_WIFI=1).\n" +
                       "• MQTT: cada medición guardada en Tendencias se publica con sus indicadores y alarmas. Los sensores con USE_MQTT=1 envían indicadores cada 10 s: asociá cada uno a un punto para guardarlos y ver su tendencia.\n" +
                       "• Modbus TCP: 30001… medición actual (float32, palabra alta primero); 30101 + 20·i el punto i de la base; escribir 1 en 40001 pide «adquirir y guardar» en el punto elegido en Tendencias.\n" +
                       "• Cliente Modbus: lee la velocidad del variador o una temperatura; con «usar como rpm» reemplaza la velocidad nominal de la máquina cuando no hay tacómetro.",
            });

            layout.Controls.Add(gM, 0, 0); layout.Controls.Add(gS, 1, 0);
            layout.Controls.Add(gC, 0, 1); layout.Controls.Add(gH, 1, 1);
            page.Controls.Add(layout);

            mqtt.MessageReceived += (topic, payload) => { try { BeginInvoke(new Action(() => OnMqttMessage(topic, payload))); } catch (InvalidOperationException) { } };
            mqtt.Disconnected += msg => { try { BeginInvoke(new Action(() => { btnMqtt.Text = "Conectar"; lblMqtt.Text = "Desconectado: " + msg; })); } catch (InvalidOperationException) { } };
            modbus.HoldingWritten += (addr, val) => { if (addr == PlantGateway.CommandRegister && val == 1) { try { BeginInvoke(new Action(OnPlcCommand)); } catch (InvalidOperationException) { } } };
            netTimer.Tick += async (s, e) =>
            {
                if (modbus.IsRunning) { PlantGateway.Heartbeat(modbus); lblModbus.Text = $"Escuchando en el puerto {modbus.Port} · clientes {modbus.Clients} · pedidos {modbus.Requests}"; }
                if (mqtt.IsConnected) lblMqtt.Text = $"Conectado · enviados {mqtt.Sent} · recibidos {mqtt.Received}";
                if (chkClientPoll.Checked && DateTime.Now.Second % 2 == 0) await ReadClient(false);
            };
            netTimer.Start();
            RefreshRegisterList();
            return page;
        }

        private void LoadNetSettings()
        {
            try { if (File.Exists(NetPath)) net = JsonSerializer.Deserialize<NetworkSettings>(File.ReadAllText(NetPath)) ?? new NetworkSettings(); }
            catch (Exception ex) { LogManager.Instance.LogError("Config. de red: " + ex.Message); }
        }

        private void SaveNetSettings()
        {
            net.MqttHost = txtMqttHost.Text.Trim(); net.MqttPort = (int)nudMqttPort.Value; net.MqttUser = txtMqttUser.Text.Trim();
            net.TopicPrefix = txtPrefix.Text.Trim().Trim('/'); net.PublishMeasurements = chkPublish.Checked; net.SubscribeSensors = chkSubscribe.Checked;
            net.StoreSensorMessages = chkStoreSensors.Checked; net.ModbusPort = (int)nudModbusPort.Value;
            net.ClientMode = cmbClientMode.SelectedIndex == 1 ? "RTU" : "TCP"; net.ClientAddress = txtClientAddr.Text.Trim(); net.ClientBaud = (int)nudClientBaud.Value;
            net.ClientUnit = (int)nudClientUnit.Value; net.ClientFunction = cmbClientFunc.SelectedIndex == 1 ? 4 : 3; net.ClientRegister = (int)nudClientReg.Value;
            net.ClientType = cmbClientType.SelectedIndex; net.ClientScale = (double)nudClientScale.Value; net.ClientAsRpm = chkClientRpm.Checked;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(NetPath));
                File.WriteAllText(NetPath, JsonSerializer.Serialize(net, ProjOpts), new UTF8Encoding(false));
            }
            catch (Exception ex) { LogManager.Instance.LogError("Guardar config. de red: " + ex.Message); }
        }

        private string Prefix => string.IsNullOrWhiteSpace(txtPrefix?.Text) ? "vibraciones" : txtPrefix.Text.Trim().Trim('/');

        private async Task ToggleMqtt()
        {
            if (mqtt.IsConnected) { mqtt.Dispose(); btnMqtt.Text = "Conectar"; lblMqtt.Text = "Desconectado."; return; }
            SaveNetSettings();
            btnMqtt.Enabled = false;
            try
            {
                await mqtt.ConnectAsync(net.MqttHost, net.MqttPort, $"analisis-vib-{Environment.MachineName}-{Environment.ProcessId}", net.MqttUser, txtMqttPass.Text);
                if (chkSubscribe.Checked) await mqtt.SubscribeAsync($"{Prefix}/+/indicadores");
                btnMqtt.Text = "Desconectar";
                SetStatus($"MQTT conectado a {net.MqttHost}:{net.MqttPort}.");
            }
            catch (Exception ex) { Error("No se pudo conectar al broker MQTT", ex); }
            finally { btnMqtt.Enabled = true; }
        }

        private void OnMqttMessage(string topic, byte[] payload)
        {
            if (!MqttCodec.TopicMatches($"{Prefix}/+/indicadores", topic)) return;
            var parsed = PlantGateway.ParseSensorMessage(Encoding.UTF8.GetString(payload));
            if (parsed == null) return;
            var (id, f, rpm) = parsed.Value;
            sensorLast[id] = (DateTime.Now, f, rpm);
            RefreshSensorList();
            if (!chkStoreSensors.Checked || !net.SensorPoints.TryGetValue(id, out var pointId)) return;
            EnsureTrendDb();
            var found = trendDb?.FindPoint(pointId);
            if (found is not { } fp) return;
            var rec = new MeasurementRecord { PointId = pointId, Rpm = rpm > 0 ? rpm : fp.Machine.Machine.Rpm, Features = f, Timestamp = DateTime.Now, Notes = "MQTT " + id };
            trendDb.AddMeasurement(rec);
            var hist = trendDb.Measurements(pointId);
            var alarms = TrendAnalysis.Evaluate(rec, fp.Point, fp.Machine.Machine.MachineClass, hist);
            var lvl = TrendAnalysis.WorstLevel(alarms);
            UpdateModbusPoints();
            if (lvl != AlarmLevel.Normal) SetStatus($"{LevelText(lvl)} en «{fp.Machine.Name} · {fp.Point.Name}» (sensor {id}).");
            if (tabs.SelectedTab == trendPage) RefreshTree(pointId);
        }

        private void RefreshSensorList()
        {
            lvSensors.BeginUpdate();
            lvSensors.Items.Clear();
            foreach (var kv in sensorLast.OrderBy(k => k.Key))
            {
                string V(string k) => kv.Value.F.TryGetValue(k, out var v) ? FormatValue(v) : "";
                string others = string.Join(" ", kv.Value.F.Where(x => x.Key is not ("vel_rms" or "acc_rms" or "factor_cresta")).Take(3).Select(x => $"{x.Key}={FormatValue(x.Value)}"));
                string point = net.SensorPoints.TryGetValue(kv.Key, out var pid) && trendDb?.FindPoint(pid) is { } fp ? $"{fp.Machine.Name} · {fp.Point.Name}" : "";
                lvSensors.Items.Add(new ListViewItem(new[] { kv.Key, kv.Value.Time.ToString("HH:mm:ss", Es), V("vel_rms"), V("acc_rms"), V("factor_cresta"), kv.Value.Rpm > 0 ? kv.Value.Rpm.ToString("0", Es) : "", others, point }) { Tag = kv.Key });
            }
            lvSensors.EndUpdate();
        }

        private void LinkSensorToPoint()
        {
            if (lvSensors.SelectedItems.Count == 0) { Warn("Elegí un sensor de la lista (llegan cuando publican por MQTT)."); return; }
            EnsureTrendDb();
            var (_, _, pt) = Selection();
            if (pt == null) { Warn("En la pestaña 6 (Tendencias) seleccioná el punto donde se guardan sus mediciones."); return; }
            string id = (string)lvSensors.SelectedItems[0].Tag;
            net.SensorPoints[id] = pt.Id;
            SaveNetSettings();
            RefreshSensorList();
            SetStatus($"Sensor «{id}» asociado a «{pt.Name}».");
        }

        private void OnMeasurementSaved(MachineAsset asset, MeasurementPoint pt, MeasurementRecord rec, AlarmLevel lvl, IReadOnlyList<AlarmResult> alarms)
        {
            UpdateModbusPoints();
            if (!mqtt.IsConnected || !chkPublish.Checked) return;
            var plant = trendDb.Catalog.Plants.FirstOrDefault(p => p.Machines.Contains(asset))?.Name ?? "planta";
            string topic = $"{Prefix}/{PlantGateway.TopicPart(plant)}/{PlantGateway.TopicPart(asset.Name)}/{PlantGateway.TopicPart(pt.Name)}/medicion";
            string json = PlantGateway.MeasurementJson(rec, plant, asset.Name, pt.Name, lvl, alarms);
            _ = mqtt.PublishAsync(topic, json, 1, retain: true).ContinueWith(t => { if (t.IsFaulted) LogManager.Instance.LogError("MQTT: " + t.Exception?.InnerException?.Message); });
        }

        private void ToggleModbus()
        {
            if (modbus.IsRunning) { modbus.Stop(); btnModbus.Text = "Iniciar"; lblModbus.Text = "Detenido."; return; }
            SaveNetSettings();
            try
            {
                modbus.Start(net.ModbusPort);
                btnModbus.Text = "Detener";
                PublishCurrentToModbus();
                UpdateModbusPoints();
                SetStatus($"Servidor Modbus TCP en el puerto {modbus.Port}. Permití la app en el firewall de Windows si el PLC no conecta.");
            }
            catch (Exception ex) { Error($"No se pudo abrir el puerto {net.ModbusPort} (¿otro programa lo usa?)", ex); }
        }

        private void PublishCurrentToModbus()
        {
            if (!modbus.IsRunning || recording == null || cmbChannel.SelectedIndex < 0) return;
            try
            {
                var rec = FeatureExtractor.Extract(recording, cmbChannel.SelectedIndex, machine);
                int zone = (int)Severity.Evaluate(rec.Get(FeatureKeys.VelocityRms), machine.MachineClass).Zone;
                var lvl = zone >= 3 ? AlarmLevel.Danger : zone == 2 ? AlarmLevel.Alert : AlarmLevel.Normal;
                PlantGateway.PublishCurrent(modbus, rec, zone, lvl, ++modbusCounter);
                RefreshRegisterList();
            }
            catch (Exception ex) { LogManager.Instance.LogError("Modbus: " + ex.Message); }
        }

        private void UpdateModbusPoints()
        {
            if (!modbus.IsRunning || trendDb == null) return;
            int i = 0;
            foreach (var (_, m, pt) in trendDb.AllPoints().Take(PlantGateway.MaxPoints))
            {
                var hist = trendDb.Measurements(pt.Id);
                var last = hist.Count > 0 ? hist[^1] : null;
                var lvl = last == null ? AlarmLevel.Normal : TrendAnalysis.WorstLevel(TrendAnalysis.Evaluate(last, pt, m.Machine.MachineClass, hist));
                PlantGateway.PublishPoint(modbus, i++, last, lvl, hist.Count);
            }
            RefreshRegisterList();
        }

        private void RefreshRegisterList()
        {
            if (lvRegisters == null) return;
            var names = trendDb?.AllPoints().Take(PlantGateway.MaxPoints).Select(x => $"{x.Machine.Name} · {x.Point.Name}").ToList() ?? new List<string>();
            lvRegisters.BeginUpdate();
            lvRegisters.Items.Clear();
            foreach (var r in PlantGateway.Describe(names))
            {
                string val = "";
                if (r.Address >= 30001 && r.Address < 31000)
                {
                    int a = r.Address - 30001;
                    val = r.Type.StartsWith("float32") ? FormatValue(ModbusCodec.ToFloat(modbus.Input[a], modbus.Input[a + 1]))
                        : r.Type == "uint32" ? ((uint)modbus.Input[a] << 16 | modbus.Input[a + 1]).ToString(CultureInfo.InvariantCulture)
                        : modbus.Input[a].ToString(CultureInfo.InvariantCulture);
                }
                else if (r.Address >= 40001) val = modbus.Holding[r.Address - 40001].ToString(CultureInfo.InvariantCulture);
                lvRegisters.Items.Add(new ListViewItem(new[] { r.Address.ToString(CultureInfo.InvariantCulture), r.Name, r.Type, r.Unit, val }));
            }
            lvRegisters.EndUpdate();
        }

        private void ExportRegisterMap()
        {
            using var sfd = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "mapa_modbus_vibraciones.csv" };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            var names = trendDb?.AllPoints().Take(PlantGateway.MaxPoints).Select(x => $"{x.Machine.Name} · {x.Point.Name}").ToList() ?? new List<string>();
            var sb = new StringBuilder("registro;descripcion;tipo;unidad\n");
            foreach (var r in PlantGateway.Describe(names)) sb.AppendLine($"{r.Address};{r.Name};{r.Type};{r.Unit}");
            File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(true));
            SetStatus("Mapa de registros exportado: " + sfd.FileName);
        }

        /// <summary>El PLC escribió 1 en 40001: adquirir con la fuente actual, guardar en el punto elegido y volver el registro a 0.</summary>
        private void OnPlcCommand()
        {
            modbus.Holding[PlantGateway.CommandRegister] = 0;
            SetStatus("Orden del PLC: adquirir y guardar.");
            if (Acquire(silent: true)) SaveCurrentMeasurement();
        }

        private async Task ReadClient(bool interactive)
        {
            if (clientBusy) return;
            clientBusy = true;
            SerialPort sp = null;
            try
            {
                SaveNetSettings();
                ModbusClient cli;
                if (cmbClientMode.SelectedIndex == 1)
                {
                    sp = new SerialPort(net.ClientAddress, net.ClientBaud, Parity.None, 8, StopBits.One) { ReadTimeout = 1000, WriteTimeout = 1000 };
                    sp.Open();
                    cli = ModbusClient.OverRtu(sp.BaseStream);
                }
                else
                {
                    var parts = net.ClientAddress.Split(':');
                    cli = await ModbusClient.ConnectTcpAsync(parts[0], parts.Length > 1 && int.TryParse(parts[1], out var pn) ? pn : 502);
                }
                using (cli)
                {
                    bool f32 = net.ClientType >= 2;
                    var regs = await cli.ReadAsync((byte)net.ClientUnit, (byte)net.ClientFunction, net.ClientRegister, f32 ? 2 : 1);
                    double raw = net.ClientType switch { 1 => (short)regs[0], 2 => ModbusCodec.ToFloat(regs[0], regs[1]), 3 => ModbusCodec.ToFloat(regs[0], regs[1], true), _ => regs[0] };
                    double val = raw * net.ClientScale;
                    lblClient.Text = $"= {val.ToString("0.###", Es)}  (crudo {raw.ToString("0.###", Es)}) · {DateTime.Now:HH:mm:ss}";
                    lblClient.ForeColor = Color.FromArgb(14, 124, 134);
                    if (net.ClientAsRpm && val > 0)
                    {
                        machine.Rpm = val;
                        machineGrid?.Refresh();
                        UpdateMachineSummary();
                    }
                }
            }
            catch (Exception ex)
            {
                lblClient.Text = "Error: " + (ex is AggregateException a ? a.InnerException?.Message : ex.Message);
                lblClient.ForeColor = Color.FromArgb(200, 40, 40);
                if (interactive) LogManager.Instance.LogError("Modbus cliente: " + ex);
            }
            finally
            {
                try { sp?.Close(); } catch { }
                clientBusy = false;
            }
        }

        private void ShutdownNetwork()
        {
            netTimer.Stop();
            try { mqtt.Dispose(); } catch { }
            try { modbus.Stop(); } catch { }
        }
    }
}
