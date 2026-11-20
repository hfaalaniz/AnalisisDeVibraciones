using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VibrationAnalysis.UI
{
    public partial class SignalControl : UserControl
    {
        private double fundamentalFrequency = 60.0;
        private double visibleAmpMax = 2.0;
        private double zoomLevel = 2.0;
        private bool showLegends = true;
        private double[] times = [];
        private double[] amplitudes = [];
        private readonly double maxAmplitude = 2.0;

        public SignalControl()
        {
            InitializeComponent();
            DoubleBuffered = true;
        }

        public double FundamentalFrequency
        {
            get => fundamentalFrequency;
            set
            {
                if (value < 10.0 || value > 1000.0)
                {
                    LogManager.Instance.LogError($"Frecuencia fundamental inválida: {value}. Debe estar entre 10,0 y 1000,0 Hz.");
                    throw new ArgumentException("La frecuencia fundamental debe estar entre 10,0 y 1000,0 Hz.");
                }
                fundamentalFrequency = value;
                Invalidate();
            }
        }

        public double VisibleAmpMax
        {
            get => visibleAmpMax;
            set
            {
                if (value <= 0.0 || value > maxAmplitude)
                {
                    LogManager.Instance.LogError($"VisibleAmpMax inválido: {value}. Debe ser mayor que 0,0 y menor o igual a {maxAmplitude}.");
                    var form = new InputValueForm(
                        $"VisibleAmpMax inválido: debe ser mayor que 0,000 y menor o igual a {maxAmplitude:F3}.",
                        0.0,
                        maxAmplitude
                    );
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        visibleAmpMax = form.SelectedValue;
                    }
                    else
                    {
                        visibleAmpMax = maxAmplitude;
                    }
                }
                else
                {
                    visibleAmpMax = value;
                }
                Invalidate();
            }
        }

        public double ZoomLevel
        {
            get => zoomLevel;
            set
            {
                if (value <= 0.1 || value > 10.0)
                {
                    LogManager.Instance.LogError($"ZoomLevel inválido: {value}. Debe estar entre 0,1 y 10,0.");
                    throw new ArgumentException("ZoomLevel debe estar entre 0,1 y 10,0.");
                }
                zoomLevel = value;
                Invalidate();
            }
        }

        public bool ShowLegends
        {
            get => showLegends;
            set
            {
                showLegends = value;
                Invalidate();
            }
        }

        public double PeakAmplitude
        {
            get
            {
                if (amplitudes.Length == 0) return 0.0;
                return amplitudes.Max(a => Math.Abs(a));
            }
        }

        public double RMS
        {
            get
            {
                if (amplitudes.Length == 0) return 0.0;
                double sumSquared = amplitudes.Sum(a => a * a);
                return Math.Sqrt(sumSquared / amplitudes.Length);
            }
        }

        public double MaxAmplitude => maxAmplitude;

        public void SetData(double[] times, double[] amplitudes)
        {
            if (times == null || amplitudes == null)
            {
                LogManager.Instance.LogError("Los arreglos de tiempos o amplitudes no pueden ser nulos.");
                throw new ArgumentException("Los arreglos de tiempos o amplitudes no pueden ser nulos.");
            }
            if (times.Length != amplitudes.Length)
            {
                LogManager.Instance.LogError("Los arreglos de tiempos y amplitudes deben tener la misma longitud.");
                throw new ArgumentException("Los arreglos de tiempos y amplitudes deben tener la misma longitud.");
            }

            this.times = times;
            this.amplitudes = amplitudes;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Área de dibujo
            int padding = 10;
            int plotWidth = Width - 2 * padding;
            int plotHeight = Height - 2 * padding;
            if (plotWidth <= 0 || plotHeight <= 0) return;

            // Fondo blanco
            g.FillRectangle(Brushes.White, padding, padding, plotWidth, plotHeight);

            // Dibujar rejilla
            using (Pen gridPen = new(Color.LightGray, 1))
            {
                for (int i = -5; i <= 5; i++)
                {
                    float y = padding + plotHeight / 2 - (i * plotHeight / 10.0f * (float)visibleAmpMax / (float)maxAmplitude);
                    g.DrawLine(gridPen, padding, y, padding + plotWidth, y);
                }
                for (int i = 0; i <= 10; i++)
                {
                    float x = padding + i * plotWidth / 10.0f;
                    g.DrawLine(gridPen, x, padding, x, padding + plotHeight);
                }
            }

            // Dibujar ejes
            using (Pen axisPen = new(Color.Black, 2))
            {
                g.DrawLine(axisPen, padding, padding + plotHeight / 2, padding + plotWidth, padding + plotHeight / 2);
                g.DrawLine(axisPen, padding, padding, padding, padding + plotHeight);
            }

            // Etiquetas de ejes
            using (Font font = new("Arial", 8))
            using (Brush brush = new SolidBrush(Color.Black))
            {
                for (int i = -5; i <= 5; i++)
                {
                    float amplitude = i * (float)visibleAmpMax / 5.0f;
                    float y = padding + plotHeight / 2 - (i * plotHeight / 10.0f * (float)visibleAmpMax / (float)maxAmplitude);
                    g.DrawString($"{amplitude:F2}", font, brush, padding - 40, y - 6);
                }
                if (times.Length > 0)
                {
                    double maxTime = times[^1];
                    for (int i = 0; i <= 10; i++)
                    {
                        float x = padding + i * plotWidth / 10.0f;
                        double time = i * maxTime / 10.0;
                        g.DrawString($"{time:F3}", font, brush, x - 10, padding + plotHeight + 5);
                    }
                }
            }

            // Dibujar la señal
            if (times.Length > 1 && amplitudes.Length == times.Length)
            {
                using Pen signalPen = new(Color.Blue, 2);
                PointF[] points = new PointF[times.Length];
                for (int i = 0; i < times.Length; i++)
                {
                    float x = padding + (float)(times[i] / times[^1]) * plotWidth;
                    float y = padding + plotHeight / 2 - (float)(amplitudes[i] / visibleAmpMax) * (plotHeight / 2);
                    points[i] = new PointF(x, y);
                }
                g.DrawLines(signalPen, points);

                // Resaltar pico máximo y añadir leyenda dinámica
                if (showLegends)
                {
                    int maxIndex = Array.IndexOf(amplitudes, amplitudes.Max(a => Math.Abs(a)));
                    if (maxIndex >= 0)
                    {
                        float xPeak = padding + (float)(times[maxIndex] / times[^1]) * plotWidth;
                        float yPeak = padding + plotHeight / 2 - (float)(amplitudes[maxIndex] / visibleAmpMax) * (plotHeight / 2);

                        // Dibujar círculo rojo
                        using (Brush peakBrush = new SolidBrush(Color.Red))
                        {
                            g.FillEllipse(peakBrush, xPeak - 5, yPeak - 5, 10, 10);
                        }

                        // Calcular fase
                        double timeAtPeak = times[maxIndex];
                        double phaseRadians = 2 * Math.PI * fundamentalFrequency * timeAtPeak;
                        double phaseDegrees = (phaseRadians * 180 / Math.PI) % 360;
                        if (phaseDegrees < 0) phaseDegrees += 360;

                        // Lista simplificada de frecuencias dominantes
                        string dominantFreqs = "1X, 2X, 5X"; // Simulación sin FFT

                        // Dibujar leyenda dinámica
                        using Font font = new("Arial", 8);
                        using Brush brush = new SolidBrush(Color.Black);
                        string dynamicLegend = $"Frec: {fundamentalFrequency:F1} Hz\n" +
                                              $"Amp: {amplitudes[maxIndex]:F3}\n" +
                                              $"Fase: {phaseDegrees:F1}°\n" +
                                              $"Dominantes: {dominantFreqs}";
                        float legendX = xPeak + 10;
                        float legendY = yPeak - 40;
                        if (legendX + 120 > padding + plotWidth)
                        {
                            legendX = xPeak - 130;
                        }
                        if (legendY < padding)
                        {
                            legendY = yPeak + 10;
                        }
                        g.DrawString(dynamicLegend, font, brush, legendX, legendY);
                    }
                }
            }

            // Dibujar leyendas estáticas
            if (showLegends)
            {
                using Font font = new("Arial", 10);
                using Brush brush = new SolidBrush(Color.Black);
                string legendText = $"Frecuencia: {fundamentalFrequency:F1} Hz\n" +
                                   $"Pico: {PeakAmplitude:F3}\n" +
                                   $"RMS: {RMS:F3}";
                g.DrawString(legendText, font, brush, padding + plotWidth - 100, padding + 10);
            }
        }
    }
}