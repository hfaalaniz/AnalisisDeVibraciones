using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace VibrationAnalysis.UI
{
    public partial class SpectrumPlotControl : UserControl
    {
        private double[] frequencies;
        private double[] amplitudes;
        private readonly List<(double Frequency, double Amplitude)> peaks;
        private double rms;
        private double fundamentalFrequency = 60.0;     // Frecuencia principal (Hz)
        private double bearingDefectFrequency = 4.5;    // Frecuencia no armónica de rodamiento (en múltiplos de fundamental)
        private readonly List<double> harmonics;
        private bool isPainting;
        private double zoomLevelX = 1.0;                // Zoom en eje X (frecuencias)
        private double zoomLevelY = 1.0;                // Zoom en eje Y (amplitudes)
        private double visibleFreqMin;                  // Rango visible de frecuencias
        private double visibleFreqMax;
        private double visibleAmpMin;                   // Rango visible de amplitudes
        private double visibleAmpMax = 2.0;             // Valor inicial seguro
        private Point? dragStartPoint;                  // Para desplazamiento
        private Point? selectionStartPoint;             // Para selección de recuadro
        private Point? selectionEndPoint;               // Para selección de recuadro
        private bool isSelecting;                       // Indica si está activo el modo de selección
        private readonly ContextMenuStrip contextMenu;  // Menú contextual
        private int padding = 50;                       // Margen del área de dibujo
        private bool showLegends = true;                // Controla visibilidad de etiquetas
        private readonly ToolTip toolTip;               // Para modo interactivo

        public SpectrumPlotControl()
        {
            InitializeComponent();
            DoubleBuffered = true;
            peaks = new List<(double Frequency, double Amplitude)>();
            harmonics = new List<double>();
            isPainting = false;
            isSelecting = false;

            // Configurar menú contextual
            contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Zoom por selección", null, (s, e) => ToggleSelectionMode());
            contextMenu.Items.Add("Restablecer zoom", null, (s, e) => ResetZoom());
            ContextMenuStrip = contextMenu;

            // Configurar ToolTip para modo interactivo
            toolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 500,
                ReshowDelay = 500,
                ShowAlways = true
            };

            // Habilitar eventos de mouse
            MouseDown += SpectrumPlotControl_MouseDown;
            MouseMove += SpectrumPlotControl_MouseMove;
            MouseUp += SpectrumPlotControl_MouseUp;

            // Inicializar rangos visibles con valores seguros
            visibleFreqMin = 0;
            visibleFreqMax = 1000.0; // Valor inicial razonable
            visibleAmpMin = 0;
            visibleAmpMax = 2.0; // Asegura compatibilidad con amplitudes esperadas
            ApplyDarkTheme();
        }

        private void ApplyDarkTheme()
        {
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
        }

        #region Propiedades públicas
        [Category("Spectrum")]
        [Description("Frecuencia principal en Hz para calcular armónicos.")]
        public double FundamentalFrequency
        {
            get => fundamentalFrequency;
            set
            {
                if (value > 0)
                {
                    fundamentalFrequency = value;
                    CalculateHarmonics();
                    Invalidate();
                    LogManager.Instance.LogInfo($"FundamentalFrequency establecida: {value:F1} Hz");
                }
                else
                {
                    LogManager.Instance.LogError("FundamentalFrequency debe ser mayor que 0.");
                    throw new ArgumentException("FundamentalFrequency debe ser mayor que 0.");
                }
            }
        }

        [Category("Spectrum")]
        [Description("Frecuencia de defecto de rodamiento en múltiplos de la frecuencia principal.")]
        public double BearingDefectFrequency
        {
            get => bearingDefectFrequency;
            set
            {
                if (value > 0)
                {
                    bearingDefectFrequency = value;
                    Invalidate();
                    LogManager.Instance.LogInfo($"BearingDefectFrequency establecida: {value:F1}x");
                }
                else
                {
                    LogManager.Instance.LogError("BearingDefectFrequency debe ser mayor que 0.");
                    throw new ArgumentException("BearingDefectFrequency debe ser mayor que 0.");
                }
            }
        }

        [Category("Spectrum")]
        [Description("Nivel de zoom en el eje X (frecuencias).")]
        public double ZoomLevelX
        {
            get => zoomLevelX;
            set
            {
                if (value >= 1.0 && value <= 100)
                {
                    zoomLevelX = value;
                    Invalidate();
                    LogManager.Instance.LogInfo($"ZoomLevelX establecido: {value:F1}");
                }
                else
                {
                    LogManager.Instance.LogError("ZoomLevelX debe estar entre 1.0 y 100.");
                    throw new ArgumentException("ZoomLevelX debe estar entre 1.0 y 100.");
                }
            }
        }

        [Category("Spectrum")]
        [Description("Nivel de zoom en el eje Y (amplitudes).")]
        public double ZoomLevelY
        {
            get => zoomLevelY;
            set
            {
                if (value >= 1.0 && value <= 100)
                {
                    zoomLevelY = value;
                    Invalidate();
                    LogManager.Instance.LogInfo($"ZoomLevelY establecido: {value:F1}");
                }
                else
                {
                    LogManager.Instance.LogError("ZoomLevelY debe estar entre 1.0 y 100.");
                    throw new ArgumentException("ZoomLevelY debe estar entre 1.0 y 100.");
                }
            }
        }

        [Category("Spectrum")]
        [Description("Frecuencia mínima visible en Hz.")]
        public double VisibleFreqMin
        {
            get => visibleFreqMin;
            set
            {
                if (value >= 0 && value < visibleFreqMax)
                {
                    visibleFreqMin = value;
                    Invalidate();
                    LogManager.Instance.LogInfo($"VisibleFreqMin establecido: {value:F1} Hz");
                }
                else
                {
                    LogManager.Instance.LogError("VisibleFreqMin debe ser mayor o igual a 0 y menor que VisibleFreqMax.");
                    throw new ArgumentException("VisibleFreqMin debe ser mayor o igual a 0 y menor que VisibleFreqMax.");
                }
            }
        }

        [Category("Spectrum")]
        [Description("Frecuencia máxima visible en Hz.")]
        public double VisibleFreqMax
        {
            get => visibleFreqMax;
            set
            {
                double maxFrequency = frequencies?.Length > 0 ? frequencies.Max() : 1000.0;
                if (value > visibleFreqMin && value <= maxFrequency)
                {
                    visibleFreqMax = value;
                    Invalidate();
                    LogManager.Instance.LogInfo($"VisibleFreqMax establecido: {value:F1} Hz");
                }
                else
                {
                    LogManager.Instance.LogError($"VisibleFreqMax debe ser mayor que VisibleFreqMin y menor o igual a {maxFrequency:F1}.");
                    throw new ArgumentException($"VisibleFreqMax debe ser mayor que VisibleFreqMin y menor o igual a {maxFrequency:F1}.");
                }
            }
        }

        [Category("Spectrum")]
        [Description("Amplitud mínima visible.")]
        public double VisibleAmpMin
        {
            get => visibleAmpMin;
            set
            {
                if (value >= 0 && value < visibleAmpMax)
                {
                    visibleAmpMin = value;
                    Invalidate();
                    LogManager.Instance.LogInfo($"VisibleAmpMin establecido: {value:F3}");
                }
                else
                {
                    LogManager.Instance.LogError("VisibleAmpMin debe ser mayor o igual a 0 y menor que VisibleAmpMax.");
                    throw new ArgumentException("VisibleAmpMin debe ser mayor o igual a 0 y menor que VisibleAmpMax.");
                }
            }
        }

        [Category("Spectrum")]
        [Description("Amplitud máxima visible.")]
        public double VisibleAmpMax
        {
            get => visibleAmpMax;
            set
            {
                double maxAmplitude = amplitudes?.Length > 0 ? amplitudes.Max() : 2.0;
                if (value > visibleAmpMin && value <= maxAmplitude)
                {
                    visibleAmpMax = value;
                    Invalidate();
                    LogManager.Instance.LogInfo($"VisibleAmpMax establecido: {value:F3}");
                }
                else
                {
                    LogManager.Instance.LogError($"VisibleAmpMax debe ser mayor que {visibleAmpMin:F3} y menor o igual a {maxAmplitude:F3}.");
                    double? newValue = ShowInputValueForm(
                        $"VisibleAmpMax inválido: debe ser mayor que {visibleAmpMin:F3} y menor o igual a {maxAmplitude:F3}.",
                        visibleAmpMin,
                        maxAmplitude
                    );

                    if (newValue.HasValue)
                    {
                        visibleAmpMax = newValue.Value;
                        Invalidate();
                        LogManager.Instance.LogInfo($"VisibleAmpMax corregido por el usuario: {newValue.Value:F3}");
                    }
                    else
                    {
                        visibleAmpMax = maxAmplitude;
                        Invalidate();
                        LogManager.Instance.LogWarning($"Usuario canceló la corrección de VisibleAmpMax. Usando valor por defecto: {maxAmplitude:F3}");
                    }
                }
            }
        }

        [Category("Spectrum")]
        [Description("Indica si el modo de selección de zoom está activo.")]
        public bool IsSelecting
        {
            get => isSelecting;
            set
            {
                isSelecting = value;
                Cursor = isSelecting ? Cursors.Cross : Cursors.Default;
                selectionStartPoint = null;
                selectionEndPoint = null;
                Invalidate();
                LogManager.Instance.LogInfo($"IsSelecting establecido: {value}");
            }
        }

        [Category("Appearance")]
        [Description("Margen en píxeles alrededor del área de dibujo.")]
        public new int Padding
        {
            get => padding;
            set
            {
                if (value >= 0)
                {
                    padding = value;
                    Invalidate();
                    LogManager.Instance.LogInfo($"Padding establecido: {value} píxeles");
                }
                else
                {
                    LogManager.Instance.LogError("Padding debe ser mayor o igual a 0.");
                    throw new ArgumentException("Padding debe ser mayor o igual a 0.");
                }
            }
        }

        [Category("Spectrum")]
        [Description("Indica si se muestran las leyendas y etiquetas.")]
        public bool ShowLegends
        {
            get => showLegends;
            set
            {
                showLegends = value;
                Invalidate();
                LogManager.Instance.LogInfo($"ShowLegends establecido: {value}");
            }
        }

        [Category("Spectrum")]
        [Description("Arreglo de frecuencias del espectro (solo lectura).")]
        public double[] Frequencies
        {
            get => frequencies;
        }

        [Category("Spectrum")]
        [Description("Arreglo de amplitudes del espectro (solo lectura).")]
        public double[] Amplitudes
        {
            get => amplitudes;
        }

        [Category("Spectrum")]
        [Description("Valor RMS calculado del espectro (solo lectura).")]
        public double RMS
        {
            get => rms;
        }
        #endregion

        private static double? ShowInputValueForm(string errorMessage, double minValue, double maxValue)
        {
            using var form = new InputValueForm(errorMessage, minValue, maxValue);
            if (form.ShowDialog() == DialogResult.OK)
            {
                return form.SelectedValue;
            }
            return null;
        }

        public void SetData(double[] frequencies, double[] amplitudes)
        {
            if (frequencies == null || amplitudes == null || frequencies.Length != amplitudes.Length)
            {
                LogManager.Instance.LogError("Datos de espectro inválidos en SpectrumPlotControl.");
                throw new ArgumentException("Las frecuencias y amplitudes deben ser no nulas y de igual longitud.");
            }

            this.frequencies = frequencies;
            this.amplitudes = amplitudes;

            double newMaxFreq = frequencies.Length > 0 ? frequencies.Max() : 1000.0;
            double newMaxAmp = amplitudes.Length > 0 ? amplitudes.Max() : 2.0;

            if (zoomLevelX == 1.0 && zoomLevelY == 1.0)
            {
                visibleFreqMin = 0;
                visibleFreqMax = newMaxFreq;
                visibleAmpMin = 0;
                visibleAmpMax = newMaxAmp;
            }
            else
            {
                double currentFreqRange = visibleFreqMax - visibleFreqMin;
                visibleFreqMin = Math.Max(0, visibleFreqMin);
                visibleFreqMax = Math.Min(newMaxFreq, visibleFreqMin + currentFreqRange);
                if (visibleFreqMax <= visibleFreqMin)
                {
                    visibleFreqMax = newMaxFreq;
                    visibleFreqMin = Math.Max(0, newMaxFreq - currentFreqRange);
                }

                double currentAmpRange = visibleAmpMax - visibleAmpMin;
                visibleAmpMin = Math.Max(0, visibleAmpMin);
                visibleAmpMax = Math.Min(newMaxAmp, visibleAmpMin + currentAmpRange);
                if (visibleAmpMax <= visibleAmpMin)
                {
                    visibleAmpMax = newMaxAmp;
                    visibleAmpMin = Math.Max(0, newMaxAmp - currentAmpRange);
                }
            }

            CalculatePeaks();
            CalculateRMS();
            CalculateHarmonics();

            Invalidate();
            LogManager.Instance.LogInfo($"SetData: {frequencies.Length} puntos, MaxFreq={frequencies.Max():F1} Hz, MaxAmp={amplitudes.Max():F3}");
        }

        private void CalculatePeaks()
        {
            // Detecta picos significativos en el espectro (amplitud > 5% del máximo)
            peaks.Clear();
            if (amplitudes == null || amplitudes.Length == 0) return;

            double maxAmplitude = amplitudes.Max();
            if (maxAmplitude == 0)
            {
                LogManager.Instance.LogWarning("MaxAmplitude es 0; no se detectan picos.");
                return;
            }

            for (int i = 1; i < amplitudes.Length - 1; i++)
            {
                if (amplitudes[i] > 0.05 * maxAmplitude &&
                    amplitudes[i] > amplitudes[i - 1] &&
                    amplitudes[i] > amplitudes[i + 1])
                {
                    peaks.Add((frequencies[i], amplitudes[i]));
                }
            }

            peaks.Sort((a, b) => b.Amplitude.CompareTo(a.Amplitude));
            if (peaks.Count > 10)
                peaks.RemoveRange(10, peaks.Count - 10);

            LogManager.Instance.LogInfo($"Detectados {peaks.Count} picos en el espectro.");
        }

        private void CalculateRMS()
        {
            // Calcula el valor RMS (raíz del promedio de cuadrados) del espectro
            rms = 0.0;
            if (amplitudes == null || amplitudes.Length == 0) return;

            double sumSquared = amplitudes.Sum(a => a * a);
            rms = Math.Sqrt(sumSquared / amplitudes.Length);

            LogManager.Instance.LogInfo($"RMS calculado: {rms:F3}");
        }

        private void CalculateHarmonics()
        {
            // Calcula frecuencias armónicas (2X a 6X) basadas en FundamentalFrequency
            harmonics.Clear();
            if (frequencies == null || frequencies.Length == 0) return;

            for (int i = 2; i <= 6; i++)
            {
                double harmonicFreq = fundamentalFrequency * i;
                if (harmonicFreq <= frequencies.Max())
                {
                    harmonics.Add(harmonicFreq);
                }
            }

            LogManager.Instance.LogInfo($"Frecuencia principal: {fundamentalFrequency:F1} Hz, {harmonics.Count} armónicos detectados.");
        }

        private void ToggleSelectionMode()
        {
            // Alterna el modo de selección de zoom
            IsSelecting = !IsSelecting;
            LogManager.Instance.LogInfo($"Modo de selección: {(isSelecting ? "Activado" : "Desactivado")}");
        }

        private void ResetZoom()
        {
            // Restablece el zoom a los valores originales
            if (frequencies == null || frequencies.Length == 0) return;
            zoomLevelX = 1.0;
            zoomLevelY = 1.0;
            visibleFreqMin = 0;
            visibleFreqMax = frequencies.Max();
            visibleAmpMin = 0;
            visibleAmpMax = amplitudes.Max();
            isSelecting = false;
            selectionStartPoint = null;
            selectionEndPoint = null;
            Cursor = Cursors.Default;
            Invalidate();
            LogManager.Instance.LogInfo("Zoom restablecido al estado original.");
        }

        private void SpectrumPlotControl_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (isSelecting)
                {
                    if (e.X >= padding && e.X <= Width - padding && e.Y >= padding && e.Y <= Height - padding)
                    {
                        selectionStartPoint = e.Location;
                        selectionEndPoint = e.Location;
                        Invalidate();
                    }
                }
                else
                {
                    dragStartPoint = e.Location;
                    Cursor = Cursors.SizeAll;
                }
            }
        }

        private void SpectrumPlotControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (isSelecting && selectionStartPoint.HasValue)
                {
                    int x = Math.Max(padding, Math.Min(e.X, Width - padding));
                    int y = Math.Max(padding, Math.Min(e.Y, Height - padding));
                    selectionEndPoint = new Point(x, y);
                    Invalidate();
                    LogManager.Instance.LogDebug($"Dibujando rectángulo de selección: Start=({selectionStartPoint.Value.X}, {selectionStartPoint.Value.Y}), End=({x}, {y})");
                }
                else if (dragStartPoint.HasValue)
                {
                    int deltaX = e.X - dragStartPoint.Value.X;
                    double maxFrequency = frequencies?.Length > 0 ? frequencies.Max() : 1000.0;
                    double freqRange = visibleFreqMax - visibleFreqMin;
                    double freqPerPixel = freqRange / (Width - 2 * padding);

                    double freqShift = -deltaX * freqPerPixel;
                    visibleFreqMin += freqShift;
                    visibleFreqMax += freqShift;

                    if (visibleFreqMin < 0)
                    {
                        visibleFreqMin = 0;
                        visibleFreqMax = freqRange;
                    }
                    if (visibleFreqMax > maxFrequency)
                    {
                        visibleFreqMax = maxFrequency;
                        visibleFreqMin = maxFrequency - freqRange;
                    }

                    dragStartPoint = e.Location;
                    Invalidate();
                    LogManager.Instance.LogDebug($"Desplazamiento: FreqRange=[{visibleFreqMin:F1}, {visibleFreqMax:F1}]");
                }
            }
            else if (showLegends)
            {
                // Mostrar ToolTip al pasar el mouse sobre un pico
                double freqRange = visibleFreqMax - visibleFreqMin;
                double ampRange = visibleAmpMax - visibleAmpMin;
                int plotWidth = Width - 2 * padding;
                int plotHeight = Height - 2 * padding;

                double mouseFreq = visibleFreqMin + (e.X - padding) * freqRange / plotWidth;
                double mouseAmp = visibleAmpMin + (1.0 - (double)(e.Y - padding) / plotHeight) * ampRange;

                var (Frequency, Amplitude) = peaks
                    .Where(p => p.Frequency >= visibleFreqMin && p.Frequency <= visibleFreqMax && p.Amplitude >= visibleAmpMin && p.Amplitude <= visibleAmpMax)
                    .OrderBy(p => Math.Abs(p.Frequency - mouseFreq) + Math.Abs(p.Amplitude - mouseAmp) * freqRange / ampRange)
                    .FirstOrDefault();

                if (Frequency != 0 && Math.Abs(Frequency - mouseFreq) < 5.0 && Math.Abs(Amplitude - mouseAmp) < 0.1)
                {
                    string faultLabel = GetFaultLabel(Frequency, Amplitude);
                    string tooltipText = $"{faultLabel}\nFrecuencia: {Frequency:F1} Hz\nAmplitud: {Amplitude:F3}\n{GetFaultDescription(faultLabel)}";
                    toolTip.SetToolTip(this, tooltipText);
                }
                else
                {
                    toolTip.SetToolTip(this, string.Empty);
                }
            }
        }

        private void SpectrumPlotControl_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (isSelecting && selectionStartPoint.HasValue && selectionEndPoint.HasValue)
                {
                    int xMin = Math.Min(selectionStartPoint.Value.X, selectionEndPoint.Value.X);
                    int xMax = Math.Max(selectionStartPoint.Value.X, selectionEndPoint.Value.X);
                    int yMin = Math.Min(selectionStartPoint.Value.Y, selectionEndPoint.Value.Y);
                    int yMax = Math.Max(selectionStartPoint.Value.Y, selectionEndPoint.Value.Y);

                    xMin = Math.Max(padding, xMin);
                    xMax = Math.Min(Width - padding, xMax);
                    yMin = Math.Max(padding, yMin);
                    yMax = Math.Min(Height - padding, yMax);

                    if (xMax > xMin && yMax > yMin)
                    {
                        double maxFrequency = frequencies?.Length > 0 ? frequencies.Max() : 1000.0;
                        double maxAmplitude = amplitudes?.Length > 0 ? amplitudes.Max() : 2.0;
                        if (maxAmplitude == 0) maxAmplitude = 2.0;

                        double freqRange = visibleFreqMax - visibleFreqMin;
                        double freqPerPixel = freqRange / (Width - 2 * padding);
                        double newFreqMin = visibleFreqMin + (xMin - padding) * freqPerPixel;
                        double newFreqMax = visibleFreqMin + (xMax - padding) * freqPerPixel;

                        double ampRange = visibleAmpMax - visibleAmpMin;
                        double ampPerPixel = ampRange / (Height - 2 * padding);
                        double newAmpMax = visibleAmpMin + (1.0 - (double)(yMin - padding) / (Height - 2 * padding)) * ampRange;
                        double newAmpMin = visibleAmpMin + (1.0 - (double)(yMax - padding) / (Height - 2 * padding)) * ampRange;

                        newFreqMin = Math.Max(0, newFreqMin);
                        newFreqMax = Math.Min(maxFrequency, newFreqMax);
                        if (newFreqMin >= newFreqMax)
                        {
                            newFreqMin = newFreqMax - 0.1;
                        }

                        newAmpMin = Math.Max(0, newAmpMin);
                        newAmpMax = Math.Min(maxAmplitude, newAmpMax);
                        if (newAmpMin >= newAmpMax)
                        {
                            newAmpMin = newAmpMax - 0.001;
                        }

                        zoomLevelX = maxFrequency / (newFreqMax - newFreqMin);
                        if (zoomLevelX < 1.0) zoomLevelX = 1.0;
                        if (zoomLevelX > 100) zoomLevelX = 100;

                        zoomLevelY = maxAmplitude / (newAmpMax - newAmpMin);
                        if (zoomLevelY < 1.0) zoomLevelY = 1.0;
                        if (zoomLevelY > 100) zoomLevelY = 100;

                        visibleFreqMin = newFreqMin;
                        visibleFreqMax = newFreqMax;
                        visibleAmpMin = newAmpMin;
                        visibleAmpMax = newAmpMax;

                        LogManager.Instance.LogInfo($"Zoom aplicado: X={zoomLevelX:F1}, Y={zoomLevelY:F1}, FreqRange=[{visibleFreqMin:F1}, {visibleFreqMax:F1}], AmpRange=[{visibleAmpMin:F3}, {visibleAmpMax:F3}]");
                    }

                    isSelecting = false;
                    selectionStartPoint = null;
                    selectionEndPoint = null;
                    Cursor = Cursors.Default;
                    Invalidate();
                }
                else
                {
                    dragStartPoint = null;
                    Cursor = Cursors.Default;
                }
            }
        }

        private string GetFaultLabel(double frequency, double amplitude)
        {
            // Determina la etiqueta de falla según la frecuencia y amplitud
            const double threshold = 0.3; // Umbral para picos significativos
            const double freqTolerance = 5.0; // Tolerancia en Hz para asociar picos
            if (amplitude < threshold) return $"Pico ({frequency:F1}, {amplitude:F3})";

            var keyFrequencies = new (double Multiplier, string Fault, string Pattern)[]
            {
                (1.0, "Desbalance", "Pico dominante en 1X"),
                (2.0, "Desalineamiento", "Picos en 1X y 2X"),
                (3.0, "Diente Agrietado", "Pico en 3X"),
                (4.0, "Holgura Mecánica", "Pico en 4X"),
                (5.0, "Rodamiento", "Picos en 5X o 7X"),
                (6.0, "Resonancia", "Pico en 6X"),
                (7.0, "Rodamiento", "Picos en 5X o 7X"),
                (1.5, "Falla en Correas", "Pico en 1.5X"),
                (2.5, "Falla en Engranajes", "Pico en 2.5X"),
                (bearingDefectFrequency, "Rodamiento", "Pico en frecuencia no armónica"),
                (10.0, "Cavitación", "Pico en 10X")
            };

            foreach (var (multiplier, fault, _) in keyFrequencies)
            {
                double targetFreq = fundamentalFrequency * multiplier;
                if (Math.Abs(frequency - targetFreq) < freqTolerance)
                {
                    return fault;
                }
            }

            return $"Pico ({frequency:F1}, {amplitude:F3})";
        }

        private static string GetFaultDescription(string fault)
        {
            // Proporciona una descripción educativa para cada falla
            return fault switch
            {
                "Desbalance" => "Causado por una distribución desigual de masa, genera vibraciones en la frecuencia fundamental (1X).",
                "Desalineamiento" => "Ocurre por ejes mal alineados, produce picos en 1X y 2X.",
                "Diente Agrietado" => "Indica daño en engranajes, genera un pico característico en 3X.",
                "Holgura Mecánica" => "Asociada a desgaste en soportes o engranajes, aparece en 4X.",
                "Rodamiento" => "Defectos en rodamientos causan picos en 5X, 7X o frecuencias no armónicas.",
                "Resonancia" => "Amplificación de vibraciones en 6X debido a frecuencias naturales del sistema.",
                "Falla en Correas" => "Problemas en poleas o correas generan picos en 1.5X.",
                "Falla en Engranajes" => "Desgaste o daño en dientes de engranajes, aparece en 2.5X.",
                "Cavitación" => "Típica en bombas, genera vibraciones de alta frecuencia en 10X.",
                _ => "Pico no asociado a una falla conocida."
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (isPainting)
            {
                LogManager.Instance.LogWarning("OnPaint llamado recursivamente; abortando.");
                return;
            }

            isPainting = true;
            try
            {
                Graphics g = e.Graphics;
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using Font labelFont = new("Arial", 8);
                using Font titleFont = new("Arial", 10, FontStyle.Bold);
                using SolidBrush textBrush = new(Color.Black);

                if (frequencies == null || amplitudes == null || frequencies.Length < 2)
                {
                    g.DrawString("No hay datos para mostrar.", labelFont, textBrush, 10, 10);
                    LogManager.Instance.LogWarning("No hay datos válidos para dibujar.");
                    return;
                }

                int plotWidth = Width - 2 * padding;
                int plotHeight = Height - 2 * padding;

                if (plotWidth <= 0 || plotHeight <= 0)
                {
                    LogManager.Instance.LogWarning("Dimensiones del gráfico inválidas.");
                    return;
                }

                double maxAmplitude = amplitudes.Length > 0 ? amplitudes.Max() : 2.0;
                double maxFrequency = frequencies.Length > 0 ? frequencies.Max() : 1000.0;
                if (maxAmplitude == 0) maxAmplitude = 2.0;
                if (maxFrequency == 0) maxFrequency = 1000.0;

                double freqRange = visibleFreqMax - visibleFreqMin;
                double ampRange = visibleAmpMax - visibleAmpMin;

                // Dibujar cuadrícula
                using Pen gridPen = new(Color.LightGray, 1);
                double freqStep = freqRange / 10;
                double ampStep = ampRange / 10;
                for (double f = Math.Ceiling(visibleFreqMin / freqStep) * freqStep; f <= visibleFreqMax; f += freqStep)
                {
                    float x = padding + (float)((f - visibleFreqMin) / freqRange * plotWidth);
                    g.DrawLine(gridPen, x, padding, x, Height - padding);
                }
                for (double a = visibleAmpMin; a <= visibleAmpMax; a += ampStep)
                {
                    float y = (Height - padding) - (float)((a - visibleAmpMin) / ampRange * plotHeight);
                    g.DrawLine(gridPen, padding, y, Width - padding, y);
                }

                // Dibujar ejes
                using Pen axisPen = new(Color.Black, 2);
                g.DrawLine(axisPen, padding, padding, padding, Height - padding);
                g.DrawLine(axisPen, padding, Height - padding, Width - padding, Height - padding);

                // Etiquetas de ejes
                g.DrawString("Frecuencia (Hz)", labelFont, textBrush, Width - padding - 50, Height - padding + 20);
                g.DrawString("Amplitud", labelFont, textBrush, padding - 40, padding - 30);

                // Etiquetas numéricas
                for (double f = Math.Ceiling(visibleFreqMin / freqStep) * freqStep; f <= visibleFreqMax; f += freqStep)
                {
                    float x = padding + (float)((f - visibleFreqMin) / freqRange * plotWidth);
                    g.DrawString(f.ToString("F0"), labelFont, textBrush, x - 10, Height - padding + 5);
                }
                for (double a = visibleAmpMin; a <= visibleAmpMax; a += ampStep)
                {
                    float y = (Height - padding) - (float)((a - visibleAmpMin) / ampRange * plotHeight);
                    g.DrawString(a.ToString("F3"), labelFont, textBrush, padding - 40, y - 5);
                }

                // Dibujar datos
                using Pen dataPen = new(Color.Blue, 2);
                for (int i = 1; i < frequencies.Length; i++)
                {
                    if (frequencies[i - 1] < visibleFreqMin || frequencies[i] > visibleFreqMax) continue;

                    float x1 = padding + (float)((frequencies[i - 1] - visibleFreqMin) / freqRange * plotWidth);
                    float y1 = (Height - padding) - (float)((amplitudes[i - 1] - visibleAmpMin) / ampRange * plotHeight);
                    float x2 = padding + (float)((frequencies[i] - visibleFreqMin) / freqRange * plotWidth);
                    float y2 = (Height - padding) - (float)((amplitudes[i] - visibleAmpMin) / ampRange * plotHeight);

                    if (float.IsNaN(y1) || float.IsNaN(y2) || float.IsInfinity(y1) || float.IsInfinity(y2))
                    {
                        LogManager.Instance.LogWarning($"Coordenadas inválidas en i={i}: y1={y1}, y2={y2}");
                        continue;
                    }

                    g.DrawLine(dataPen, x1, y1, x2, y2);
                }

                // Dibujar picos con etiquetas de tipo de falla
                using Pen peakPen = new(Color.Red, 1);
                using SolidBrush peakBrush = new(Color.Red);
                if (showLegends)
                {
                    const double threshold = 0.3; // Umbral para picos significativos
                    var keyFrequencies = new (double Multiplier, string Fault, string Pattern)[]
                    {
                        (1.0, "Desbalance", "Pico dominante en 1X"),
                        (2.0, "Desalineamiento", "Picos en 1X y 2X"),
                        (3.0, "Diente Agrietado", "Pico en 3X"),
                        (4.0, "Holgura Mecánica", "Pico en 4X"),
                        (5.0, "Rodamiento", "Picos en 5X o 7X"),
                        (6.0, "Resonancia", "Pico en 6X"),
                        (7.0, "Rodamiento", "Picos en 5X o 7X"),
                        (1.5, "Falla en Correas", "Pico en 1.5X"),
                        (2.5, "Falla en Engranajes", "Pico en 2.5X"),
                        (bearingDefectFrequency, "Rodamiento", "Pico en frecuencia no armónica"),
                        (10.0, "Cavitación", "Pico en 10X")
                    };

                    foreach (var (Frequency, Amplitude) in peaks)
                    {
                        if (Frequency < visibleFreqMin || Frequency > visibleFreqMax || Amplitude < visibleAmpMin || Amplitude > visibleAmpMax || Amplitude < threshold) continue;

                        float x = padding + (float)((Frequency - visibleFreqMin) / freqRange * plotWidth);
                        float y = (Height - padding) - (float)((Amplitude - visibleAmpMin) / ampRange * plotHeight);
                        if (float.IsNaN(y) || float.IsInfinity(y))
                        {
                            LogManager.Instance.LogWarning($"Coordenada inválida para pico: Freq={Frequency}, Amp={Amplitude}");
                            continue;
                        }
                        g.DrawEllipse(peakPen, x - 4, y - 4, 8, 8);

                        string faultLabel = GetFaultLabel(Frequency, Amplitude);
                        float labelX = x + 5;
                        float labelY = y - 15;
                        if (labelX + 100 > Width - padding) labelX = x - 105;
                        if (labelY < padding) labelY = y + 5;
                        g.DrawString(faultLabel, labelFont, peakBrush, labelX, labelY);
                    }

                    // Dibujar líneas de referencia para patrones complejos
                    using Pen patternPen = new(Color.Orange, 1) { DashStyle = DashStyle.Dot };
                    foreach (var (multiplier, _, pattern) in keyFrequencies)
                    {
                        double freq = fundamentalFrequency * multiplier;
                        if (freq < visibleFreqMin || freq > visibleFreqMax) continue;
                        float x = padding + (float)((freq - visibleFreqMin) / freqRange * plotWidth);
                        g.DrawLine(patternPen, x, padding, x, Height - padding);
                    }
                }
                else
                {
                    foreach (var (Frequency, Amplitude) in peaks)
                    {
                        if (Frequency < visibleFreqMin || Frequency > visibleFreqMax || Amplitude < visibleAmpMin || Amplitude > visibleAmpMax) continue;

                        float x = padding + (float)((Frequency - visibleFreqMin) / freqRange * plotWidth);
                        float y = (Height - padding) - (float)((Amplitude - visibleAmpMin) / ampRange * plotHeight);
                        if (float.IsNaN(y) || float.IsInfinity(y))
                        {
                            LogManager.Instance.LogWarning($"Coordenada inválida para pico: Freq={Frequency}, Amp={Amplitude}");
                            continue;
                        }
                        g.DrawEllipse(peakPen, x - 4, y - 4, 8, 8);
                    }
                }

                // Dibujar armónicos
                using Pen harmonicPen = new(Color.Green, 1) { DashStyle = DashStyle.Dash };
                if (showLegends)
                {
                    foreach (var harmonic in harmonics)
                    {
                        if (harmonic < visibleFreqMin || harmonic > visibleFreqMax) continue;

                        float x = padding + (float)((harmonic - visibleFreqMin) / freqRange * plotWidth);
                        g.DrawLine(harmonicPen, x, padding, x, Height - padding);
                        g.DrawString($"{harmonic / fundamentalFrequency:F0}X", labelFont, Brushes.Green, x + 5, padding);
                    }
                }

                // Dibujar rectángulo de selección
                if (isSelecting && selectionStartPoint.HasValue && selectionEndPoint.HasValue)
                {
                    int x = Math.Min(selectionStartPoint.Value.X, selectionEndPoint.Value.X);
                    int y = Math.Min(selectionStartPoint.Value.Y, selectionEndPoint.Value.Y);
                    int width = Math.Abs(selectionEndPoint.Value.X - selectionStartPoint.Value.X);
                    int height = Math.Abs(selectionEndPoint.Value.Y - selectionEndPoint.Value.Y);

                    using SolidBrush selectionBrush = new(Color.FromArgb(100, 0, 0, 255));
                    using Pen selectionPen = new(Color.Black, 1) { DashStyle = DashStyle.DashDot };
                    g.FillRectangle(selectionBrush, x, y, width, height);
                    g.DrawRectangle(selectionPen, x, y, width, height);
                    LogManager.Instance.LogDebug($"Rectángulo dibujado: x={x}, y={y}, width={width}, height={height}");
                }

                // Mostrar RMS, frecuencia principal y frecuencia de rodamiento
                if (showLegends)
                {
                    string infoText = $"RMS: {rms:F3} | Frec. Principal: {fundamentalFrequency:F1} Hz | Frec. Rodamiento: {fundamentalFrequency * bearingDefectFrequency:F1} Hz";
                    g.DrawString(infoText, labelFont, textBrush, padding, padding - 20);
                }

                // Título
                g.DrawString("Espectro de Frecuencia", titleFont, textBrush, padding + 20, 10);

                LogManager.Instance.LogInfo("Espectro dibujado correctamente en SpectrumPlotControl.");
            }
            catch (Exception ex)
            {
                LogManager.Instance.LogError($"Error al dibujar SpectrumPlotControl: {ex.Message}");
                using Font errorFont = new("Arial", 10);
                using SolidBrush errorBrush = new(Color.Red);
                e.Graphics.DrawString("Error al dibujar el espectro.", errorFont, errorBrush, 10, 10);
            }
            finally
            {
                isPainting = false;
            }
        }

        public Bitmap GetBitmap()
        {
            try
            {
                Bitmap bitmap = new(Width, Height);
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                LogManager.Instance.LogInfo("Bitmap generado para SpectrumPlotControl.");
                return bitmap;
            }
            catch (Exception ex)
            {
                LogManager.Instance.LogError($"Error al generar Bitmap: {ex.Message}");
                throw;
            }
        }

        public void ExportToPng(string filePath)
        {
            try
            {
                using Bitmap bitmap = new(Width, Height);
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
                LogManager.Instance.LogInfo($"Espectro exportado a PNG: {filePath}");
            }
            catch (Exception ex)
            {
                LogManager.Instance.LogError($"Error al exportar PNG: {ex.Message}");
                throw;
            }
        }

        public void ExportToCsv(string filePath)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                using StreamWriter writer = new(filePath, false, Encoding.UTF8);
                writer.WriteLine("Frecuencia (Hz),Amplitud,EsPico,Falla");
                for (int i = 0; i < frequencies.Length; i++)
                {
                    bool isPeak = peaks.Any(p => Math.Abs(p.Frequency - frequencies[i]) < 0.1);
                    string fault = isPeak ? GetFaultLabel(frequencies[i], amplitudes[i]) : "";
                    writer.WriteLine($"{frequencies[i]:F1},{amplitudes[i]:F3},{(isPeak ? "Sí" : "No")},{fault}");
                }
                writer.WriteLine($"RMS,{rms:F3},,");
                writer.WriteLine($"Frecuencia Principal,{fundamentalFrequency:F1},,");
                writer.WriteLine($"Frecuencia Rodamiento,{fundamentalFrequency * bearingDefectFrequency:F1},,");
                LogManager.Instance.LogInfo($"Espectro exportado a CSV: {filePath}");
            }
            catch (Exception ex)
            {
                LogManager.Instance.LogError($"Error al exportar CSV: {ex.Message}");
                throw;
            }
        }
    }
}


