using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace VibrationAnalysis.UI
{
    /// <summary>
    /// Control personalizado para visualizar vectores de desbalance y corrección en un gráfico polar,
    /// con soporte para uno o dos planos de balanceo.
    /// </summary>
    public partial class PolarGraphControl : UserControl
    {
        // Propiedades para Plano 1
        private double unbalanceAmplitude;
        private double unbalanceAngle;
        private double correctionAngle;
        // Propiedades para Plano 2
        private double unbalanceAmplitudePlane2;
        private double unbalanceAnglePlane2;
        private double correctionAnglePlane2;
        // Propiedades generales
        private double maxAmplitude = 2.0;
        private bool showLegends = true;
        private int padding = 20;
        private readonly ToolTip toolTip;

        [Category("Polar Graph")]
        [Description("Amplitud del desbalance en el Plano 1 (en mm/s).")]
        public double UnbalanceAmplitude
        {
            get => unbalanceAmplitude;
            set
            {
                if (value >= 0)
                {
                    unbalanceAmplitude = value;
                    Invalidate();
                }
                else
                    throw new ArgumentException("UnbalanceAmplitude debe ser mayor o igual a 0.");
            }
        }

        [Category("Polar Graph")]
        [Description("Ángulo del desbalance en el Plano 1 (en grados, 0-360).")]
        public double UnbalanceAngle
        {
            get => unbalanceAngle;
            set
            {
                if (value >= 0 && value <= 360)
                {
                    unbalanceAngle = value;
                    Invalidate();
                }
                else
                    throw new ArgumentException("UnbalanceAngle debe estar entre 0 y 360 grados.");
            }
        }

        [Category("Polar Graph")]
        [Description("Ángulo de corrección en el Plano 1 (en grados, 0-360).")]
        public double CorrectionAngle
        {
            get => correctionAngle;
            set
            {
                if (value >= 0 && value <= 360)
                {
                    correctionAngle = value;
                    Invalidate();
                }
                else
                    throw new ArgumentException("CorrectionAngle debe estar entre 0 y 360 grados.");
            }
        }

        [Category("Polar Graph")]
        [Description("Amplitud del desbalance en el Plano 2 (en mm/s).")]
        public double UnbalanceAmplitudePlane2
        {
            get => unbalanceAmplitudePlane2;
            set
            {
                if (value >= 0)
                {
                    unbalanceAmplitudePlane2 = value;
                    Invalidate();
                }
                else
                    throw new ArgumentException("UnbalanceAmplitudePlane2 debe ser mayor o igual a 0.");
            }
        }

        [Category("Polar Graph")]
        [Description("Ángulo del desbalance en el Plano 2 (en grados, 0-360).")]
        public double UnbalanceAnglePlane2
        {
            get => unbalanceAnglePlane2;
            set
            {
                if (value >= 0 && value <= 360)
                {
                    unbalanceAnglePlane2 = value;
                    Invalidate();
                }
                else
                    throw new ArgumentException("UnbalanceAnglePlane2 debe estar entre 0 y 360 grados.");
            }
        }

        [Category("Polar Graph")]
        [Description("Ángulo de corrección en el Plano 2 (en grados, 0-360).")]
        public double CorrectionAnglePlane2
        {
            get => correctionAnglePlane2;
            set
            {
                if (value >= 0 && value <= 360)
                {
                    correctionAnglePlane2 = value;
                    Invalidate();
                }
                else
                    throw new ArgumentException("CorrectionAnglePlane2 debe estar entre 0 y 360 grados.");
            }
        }

        [Category("Polar Graph")]
        [Description("Amplitud máxima del gráfico polar (escala fija, en mm/s).")]
        public double MaxAmplitude
        {
            get => maxAmplitude;
            set
            {
                if (value > 0)
                {
                    maxAmplitude = value;
                    Invalidate();
                }
                else
                    throw new ArgumentException("MaxAmplitude debe ser mayor que 0.");
            }
        }

        [Category("Polar Graph")]
        [Description("Indica si se muestran las leyendas y etiquetas.")]
        public bool ShowLegends
        {
            get => showLegends;
            set
            {
                showLegends = value;
                Invalidate();
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
                }
                else
                    throw new ArgumentException("Padding debe ser mayor o igual a 0.");
            }
        }

        /// <summary>
        /// Constructor del control.
        /// </summary>
        public PolarGraphControl()
        {
            InitializeComponent();
            DoubleBuffered = true;

            // Configurar tooltip
            toolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 500,
                ReshowDelay = 500,
                ShowAlways = true
            };

            // Registrar eventos
            MouseMove += PolarGraphControl_MouseMove;
            Resize += PolarGraphControl_Resize;
        }

        private void PolarGraphControl_Resize(object sender, EventArgs e)
        {
            Invalidate();
        }

        private void PolarGraphControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (!showLegends) return;

            int centerX = Width / 2;
            int centerY = Height / 2;
            double radius = Math.Min(Width, Height) / 2.0 - padding;

            double dx = e.X - centerX;
            double dy = -(e.Y - centerY);
            double mouseRadius = Math.Sqrt(dx * dx + dy * dy) / radius * maxAmplitude;
            double mouseAngle = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (mouseAngle < 0) mouseAngle += 360;

            // Verificar proximidad al vector de desbalance (Plano 1)
            double angleDiffUnbalance = Math.Abs(mouseAngle - unbalanceAngle);
            if (angleDiffUnbalance > 180) angleDiffUnbalance = 360 - angleDiffUnbalance;
            if (unbalanceAmplitude > 0 && mouseRadius >= unbalanceAmplitude * 0.8 && mouseRadius <= unbalanceAmplitude * 1.2 && angleDiffUnbalance < 10)
            {
                toolTip.SetToolTip(this, $"Desbalance Plano 1\nÁngulo: {unbalanceAngle:F1}°\nAmplitud: {unbalanceAmplitude:F3} mm/s\nCausado por masa desigual, genera vibración en 1X.");
                return;
            }

            // Verificar proximidad al vector de corrección (Plano 1)
            double angleDiffCorrection = Math.Abs(mouseAngle - correctionAngle);
            if (angleDiffCorrection > 180) angleDiffCorrection = 360 - angleDiffCorrection;
            if (unbalanceAmplitude > 0 && mouseRadius >= 0.8 * maxAmplitude && mouseRadius <= 1.2 * maxAmplitude && angleDiffCorrection < 10)
            {
                toolTip.SetToolTip(this, $"Corrección Plano 1\nÁngulo: {correctionAngle:F1}°\nColoque el peso de corrección en este ángulo.");
                return;
            }

            // Verificar proximidad al vector de desbalance (Plano 2)
            double angleDiffUnbalance2 = Math.Abs(mouseAngle - unbalanceAnglePlane2);
            if (angleDiffUnbalance2 > 180) angleDiffUnbalance2 = 360 - angleDiffUnbalance2;
            if (unbalanceAmplitudePlane2 > 0 && mouseRadius >= unbalanceAmplitudePlane2 * 0.8 && mouseRadius <= unbalanceAmplitudePlane2 * 1.2 && angleDiffUnbalance2 < 10)
            {
                toolTip.SetToolTip(this, $"Desbalance Plano 2\nÁngulo: {unbalanceAnglePlane2:F1}°\nAmplitud: {unbalanceAmplitudePlane2:F3} mm/s\nCausado por masa desigual, genera vibración en 1X.");
                return;
            }

            // Verificar proximidad al vector de corrección (Plano 2)
            double angleDiffCorrection2 = Math.Abs(mouseAngle - correctionAnglePlane2);
            if (angleDiffCorrection2 > 180) angleDiffCorrection2 = 360 - angleDiffCorrection2;
            if (unbalanceAmplitudePlane2 > 0 && mouseRadius >= 0.8 * maxAmplitude && mouseRadius <= 1.2 * maxAmplitude && angleDiffCorrection2 < 10)
            {
                toolTip.SetToolTip(this, $"Corrección Plano 2\nÁngulo: {correctionAnglePlane2:F1}°\nColoque el peso de corrección en este ángulo.");
                return;
            }

            // Mostrar posición del mouse si no está cerca de ningún vector
            toolTip.SetToolTip(this, $"Posición\nÁngulo: {mouseAngle:F1}°\nAmplitud: {mouseRadius:F3} mm/s");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Configurar colores según el tema
            Color backgroundColor = BackColor;
            Color textColor = ForeColor;
            Color gridColor = Color.LightGray;
            Color axisColor = ForeColor;

            // Dibujar fondo
            g.Clear(backgroundColor);

            using Font labelFont = new("Arial", 8);
            using Font titleFont = new("Arial", 10, FontStyle.Bold);
            using SolidBrush textBrush = new(textColor);
            using Pen gridPen = new(gridColor, 1);
            using Pen axisPen = new(axisColor, 2);
            using Pen unbalancePen = new(Color.Red, 2);
            using SolidBrush unbalanceBrush = new(Color.Red);
            using Pen correctionPen = new(Color.Green, 2);
            using SolidBrush correctionBrush = new(Color.Green);
            using Pen unbalancePenPlane2 = new(Color.Blue, 2);
            using SolidBrush unbalanceBrushPlane2 = new(Color.Blue);
            using Pen correctionPenPlane2 = new(Color.Yellow, 2);
            using SolidBrush correctionBrushPlane2 = new(Color.Yellow);

            int centerX = Width / 2;
            int centerY = Height / 2;
            double radius = Math.Min(Width, Height) / 2.0 - padding;

            if (radius <= 0)
            {
                g.DrawString("Área de dibujo demasiado pequeña.", labelFont, textBrush, 10, 10);
                return;
            }

            // Dibujar círculos concéntricos
            int numCircles = 5;
            for (int i = 1; i <= numCircles; i++)
            {
                float circleRadius = (float)(radius * i / numCircles);
                g.DrawEllipse(gridPen, centerX - circleRadius, centerY - circleRadius, circleRadius * 2, circleRadius * 2);
                if (showLegends)
                {
                    double ampValue = maxAmplitude * i / numCircles;
                    g.DrawString(ampValue.ToString("F3"), labelFont, textBrush, centerX - 15, centerY - circleRadius - 10);
                }
            }

            // Dibujar ejes (0°, 90°, 180°, 270°)
            for (int angle = 0; angle < 360; angle += 90)
            {
                double rad = angle * Math.PI / 180;
                float x = (float)(centerX + radius * Math.Cos(rad));
                float y = (float)(centerY - radius * Math.Sin(rad));
                g.DrawLine(axisPen, centerX, centerY, x, y);
                if (showLegends)
                {
                    float labelX = (float)(centerX + (radius + 10) * Math.Cos(rad));
                    float labelY = (float)(centerY - (radius + 10) * Math.Sin(rad));
                    string label = $"{angle}°";
                    SizeF labelSize = g.MeasureString(label, labelFont);
                    g.DrawString(label, labelFont, textBrush, labelX - labelSize.Width / 2, labelY - labelSize.Height / 2);
                }
            }

            // Dibujar vector de desbalance (Plano 1)
            if (unbalanceAmplitude > 0)
            {
                double unbalanceRad = unbalanceAngle * Math.PI / 180;
                float unbalanceRadius = (float)(radius * unbalanceAmplitude / maxAmplitude);
                float unbalanceX = (float)(centerX + unbalanceRadius * Math.Cos(unbalanceRad));
                float unbalanceY = (float)(centerY - unbalanceRadius * Math.Sin(unbalanceRad));
                g.DrawLine(unbalancePen, centerX, centerY, unbalanceX, unbalanceY);
                DrawArrowHead(g, unbalancePen, unbalanceX, unbalanceY, unbalanceRad);
                g.FillEllipse(unbalanceBrush, unbalanceX - 5, unbalanceY - 5, 10, 10);
            }

            // Dibujar vector de corrección (Plano 1)
            if (unbalanceAmplitude > 0)
            {
                double correctionRad = correctionAngle * Math.PI / 180;
                float correctionRadius = (float)radius;
                float correctionX = (float)(centerX + correctionRadius * Math.Cos(correctionRad));
                float correctionY = (float)(centerY - correctionRadius * Math.Sin(correctionRad));
                g.DrawLine(correctionPen, centerX, centerY, correctionX, correctionY);
                DrawArrowHead(g, correctionPen, correctionX, correctionY, correctionRad);
                g.FillEllipse(correctionBrush, correctionX - 5, correctionY - 5, 10, 10);
            }

            // Dibujar vector de desbalance (Plano 2)
            if (unbalanceAmplitudePlane2 > 0)
            {
                double unbalanceRad2 = unbalanceAnglePlane2 * Math.PI / 180;
                float unbalanceRadius2 = (float)(radius * unbalanceAmplitudePlane2 / maxAmplitude);
                float unbalanceX2 = (float)(centerX + unbalanceRadius2 * Math.Cos(unbalanceRad2));
                float unbalanceY2 = (float)(centerY - unbalanceRadius2 * Math.Sin(unbalanceRad2));
                g.DrawLine(unbalancePenPlane2, centerX, centerY, unbalanceX2, unbalanceY2);
                DrawArrowHead(g, unbalancePenPlane2, unbalanceX2, unbalanceY2, unbalanceRad2);
                g.FillEllipse(unbalanceBrushPlane2, unbalanceX2 - 5, unbalanceY2 - 5, 10, 10);
            }

            // Dibujar vector de corrección (Plano 2)
            if (unbalanceAmplitudePlane2 > 0)
            {
                double correctionRad2 = correctionAnglePlane2 * Math.PI / 180;
                float correctionRadius2 = (float)radius;
                float correctionX2 = (float)(centerX + correctionRadius2 * Math.Cos(correctionRad2));
                float correctionY2 = (float)(centerY - correctionRadius2 * Math.Sin(correctionRad2));
                g.DrawLine(correctionPenPlane2, centerX, centerY, correctionX2, correctionY2);
                DrawArrowHead(g, correctionPenPlane2, correctionX2, correctionY2, correctionRad2);
                g.FillEllipse(correctionBrushPlane2, correctionX2 - 5, correctionY2 - 5, 10, 10);
            }

            // Dibujar leyendas
            if (showLegends)
            {
                int legendY = 10;
                if (unbalanceAmplitude > 0)
                {
                    DrawLegend(g, Color.Red, $"Desbalance Plano 1: {unbalanceAngle:F1}°", 10, legendY, labelFont, textBrush);
                    DrawLegend(g, Color.Green, $"Corrección Plano 1: {correctionAngle:F1}°", 10, legendY + 15, labelFont, textBrush);
                    legendY += 30;
                }
                if (unbalanceAmplitudePlane2 > 0)
                {
                    DrawLegend(g, Color.Blue, $"Desbalance Plano 2: {unbalanceAnglePlane2:F1}°", 10, legendY, labelFont, textBrush);
                    DrawLegend(g, Color.Yellow, $"Corrección Plano 2: {correctionAnglePlane2:F1}°", 10, legendY + 15, labelFont, textBrush);
                }
            }
        }

        /// <summary>
        /// Dibuja una punta de flecha en el extremo de un vector.
        /// </summary>
        private static void DrawArrowHead(Graphics g, Pen pen, float x, float y, double angleRad)
        {
            const int arrowSize = 8;
            double angle1 = angleRad + Math.PI - Math.PI / 6; // 30° hacia atrás
            double angle2 = angleRad + Math.PI + Math.PI / 6;

            PointF p1 = new(
                x + (float)(arrowSize * Math.Cos(angle1)),
                y - (float)(arrowSize * Math.Sin(angle1)));
            PointF p2 = new(
                x + (float)(arrowSize * Math.Cos(angle2)),
                y - (float)(arrowSize * Math.Sin(angle2)));

            g.DrawLine(pen, x, y, p1.X, p1.Y);
            g.DrawLine(pen, x, y, p2.X, p2.Y);
        }

        /// <summary>
        /// Dibuja una entrada de leyenda con un color y texto.
        /// </summary>
        private static void DrawLegend(Graphics g, Color color, string text, int x, int y, Font font, SolidBrush brush)
        {
            using (SolidBrush legendBrush = new(color))
            {
                g.FillRectangle(legendBrush, x, y, 10, 10);
            }
            g.DrawString(text, font, brush, x + 15, y);
        }
    }
}
























/*using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VibrationAnalysis.Core.Utilities;

namespace VibrationAnalysis.UI
{
    public partial class PolarGraphControl : UserControl
    {
        private double unbalanceAmplitude; // Amplitud del desbalance
        private double unbalanceAngle; // Ángulo del desbalance en grados (0-360)
        private double correctionAngle; // Ángulo de corrección en grados (0-360)
        private bool showLegends = true; // Controla visibilidad de leyendas
        private ToolTip toolTip; // Para explicaciones interactivas
        private int padding = 20; // Margen del área de dibujo
        private double maxAmplitude = 2.0; // Amplitud máxima fija (valor inicial)

        public PolarGraphControl()
        {
            InitializeComponent();
            DoubleBuffered = true;

            // Configurar ToolTip para modo interactivo
            toolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 500,
                ReshowDelay = 500,
                ShowAlways = true
            };

            // Habilitar eventos
            MouseMove += PolarGraphControl_MouseMove;
            Resize += PolarGraphControl_Resize;

            LogManager.Instance.LogInfo("PolarGraphControl inicializado.");
        }

        #region Propiedades públicas
        [Category("Polar Graph")]
        [Description("Amplitud del desbalance (radio en el gráfico polar).")]
        public double UnbalanceAmplitude
        {
            get => unbalanceAmplitude;
            set
            {
                if (value >= 0)
                {
                    unbalanceAmplitude = value;
                    Invalidate();
                    LogManager.Instance.LogDebug($"UnbalanceAmplitude actualizada: {value:F3}");
                }
                else
                {
                    LogManager.Instance.LogError("UnbalanceAmplitude debe ser mayor o igual a 0.");
                    throw new ArgumentException("UnbalanceAmplitude debe ser mayor o igual a 0.");
                }
            }
        }

        [Category("Polar Graph")]
        [Description("Ángulo del desbalance en grados (0-360).")]
        public double UnbalanceAngle
        {
            get => unbalanceAngle;
            set
            {
                if (value >= 0 && value <= 360)
                {
                    unbalanceAngle = value;
                    Invalidate();
                    LogManager.Instance.LogDebug($"UnbalanceAngle actualizado: {value:F1}°");
                }
                else
                {
                    LogManager.Instance.LogError("UnbalanceAngle debe estar entre 0 y 360 grados.");
                    throw new ArgumentException("UnbalanceAngle debe estar entre 0 y 360 grados.");
                }
            }
        }

        [Category("Polar Graph")]
        [Description("Ángulo de corrección en grados (0-360).")]
        public double CorrectionAngle
        {
            get => correctionAngle;
            set
            {
                if (value >= 0 && value <= 360)
                {
                    correctionAngle = value;
                    Invalidate();
                    LogManager.Instance.LogDebug($"CorrectionAngle actualizado: {value:F1}°");
                }
                else
                {
                    LogManager.Instance.LogError("CorrectionAngle debe estar entre 0 y 360 grados.");
                    throw new ArgumentException("CorrectionAngle debe estar entre 0 y 360 grados.");
                }
            }
        }

        [Category("Polar Graph")]
        [Description("Indica si se muestran las leyendas y etiquetas.")]
        public bool ShowLegends
        {
            get => showLegends;
            set
            {
                showLegends = value;
                Invalidate();
                LogManager.Instance.LogDebug($"ShowLegends actualizado: {value}");
            }
        }

        [Category("Appearance")]
        [Description("Margen en píxeles alrededor del área de dibujo.")]
        public int Padding
        {
            get => padding;
            set
            {
                if (value >= 0)
                {
                    padding = value;
                    Invalidate();
                    LogManager.Instance.LogDebug($"Padding actualizado: {value} píxeles");
                }
                else
                {
                    LogManager.Instance.LogError("Padding debe ser mayor o igual a 0.");
                    throw new ArgumentException("Padding debe ser mayor o igual a 0.");
                }
            }
        }

        [Category("Polar Graph")]
        [Description("Amplitud máxima del gráfico polar (escala fija).")]
        public double MaxAmplitude
        {
            get => maxAmplitude;
            set
            {
                if (value > 0)
                {
                    maxAmplitude = value;
                    Invalidate();
                    LogManager.Instance.LogDebug($"MaxAmplitude actualizada: {value:F3}");
                }
                else
                {
                    LogManager.Instance.LogError("MaxAmplitude debe ser mayor que 0.");
                    throw new ArgumentException("MaxAmplitude debe ser mayor que 0.");
                }
            }
        }
        #endregion

        private void PolarGraphControl_Resize(object sender, EventArgs e)
        {
            Invalidate(); // Redibujar al cambiar el tamaño
            LogManager.Instance.LogDebug($"PolarGraphControl redimensionado: {Width}x{Height}");
        }

        private void PolarGraphControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (!showLegends) return;

            // Calcular la posición del mouse en coordenadas polares
            int centerX = Width / 2;
            int centerY = Height / 2;
            double radius = Math.Min(Width, Height) / 2.0 - padding;

            double dx = e.X - centerX;
            double dy = -(e.Y - centerY); // Invertir Y para orientación estándar
            double mouseRadius = Math.Sqrt(dx * dx + dy * dy) / radius * maxAmplitude;
            double mouseAngle = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (mouseAngle < 0) mouseAngle += 360;

            // Mostrar tooltip si el mouse está cerca del punto de desbalance
            double angleDiffUnbalance = Math.Abs(mouseAngle - unbalanceAngle);
            if (angleDiffUnbalance > 180) angleDiffUnbalance = 360 - angleDiffUnbalance;
            if (unbalanceAmplitude > 0 && mouseRadius >= unbalanceAmplitude * 0.8 && mouseRadius <= unbalanceAmplitude * 1.2 && angleDiffUnbalance < 10)
            {
                string tooltipText = $"Desbalance\nÁngulo: {unbalanceAngle:F1}°\nAmplitud: {unbalanceAmplitude:F3}\nCausado por masa desigual, genera vibración en 1X.";
                toolTip.SetToolTip(this, tooltipText);
            }
            // Mostrar tooltip si el mouse está cerca del ángulo de corrección
            else
            {
                double angleDiffCorrection = Math.Abs(mouseAngle - correctionAngle);
                if (angleDiffCorrection > 180) angleDiffCorrection = 360 - angleDiffCorrection;
                if (mouseRadius >= 0.8 * maxAmplitude && mouseRadius <= 1.2 * maxAmplitude && angleDiffCorrection < 10)
                {
                    string tooltipText = $"Corrección\nÁngulo: {correctionAngle:F1}°\nColoque el peso de corrección en este ángulo.";
                    toolTip.SetToolTip(this, tooltipText);
                }
                else
                {
                    toolTip.SetToolTip(this, string.Empty);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using Font labelFont = new Font("Arial", 8);
            using Font titleFont = new Font("Arial", 10, FontStyle.Bold);
            using SolidBrush textBrush = new SolidBrush(Color.Black);
            using Pen gridPen = new Pen(Color.LightGray, 1);
            using Pen axisPen = new Pen(Color.Black, 2);
            using Pen unbalancePen = new Pen(Color.Red, 2);
            using SolidBrush unbalanceBrush = new SolidBrush(Color.Red);
            using Pen correctionPen = new Pen(Color.Green, 2);
            using SolidBrush correctionBrush = new SolidBrush(Color.Green);

            int centerX = Width / 2;
            int centerY = Height / 2;
            double radius = Math.Min(Width, Height) / 2.0 - padding;

            if (radius <= 0)
            {
                g.DrawString("Área de dibujo demasiado pequeña.", labelFont, textBrush, 10, 10);
                LogManager.Instance.LogWarning("Dimensiones del gráfico polar inválidas.");
                return;
            }

            // Dibujar círculos concéntricos
            int numCircles = 5;
            for (int i = 1; i <= numCircles; i++)
            {
                float circleRadius = (float)(radius * i / numCircles);
                g.DrawEllipse(gridPen, centerX - circleRadius, centerY - circleRadius, circleRadius * 2, circleRadius * 2);
                if (showLegends)
                {
                    double ampValue = maxAmplitude * i / numCircles;
                    g.DrawString(ampValue.ToString("F3"), labelFont, textBrush, centerX - 15, centerY - circleRadius - 10);
                }
            }

            // Dibujar ejes angulares (0°, 90°, 180°, 270°)
            for (int angle = 0; angle < 360; angle += 90)
            {
                double rad = angle * Math.PI / 180;
                float x = (float)(centerX + radius * Math.Cos(rad));
                float y = (float)(centerY - radius * Math.Sin(rad));
                g.DrawLine(axisPen, centerX, centerY, x, y);
                if (showLegends)
                {
                    float labelX = (float)(centerX + (radius + 10) * Math.Cos(rad));
                    float labelY = (float)(centerY - (radius + 10) * Math.Sin(rad));
                    g.DrawString($"{angle}°", labelFont, textBrush, labelX - 10, labelY - 5);
                }
            }

            // Dibujar el desbalance
            if (unbalanceAmplitude > 0)
            {
                double rad = unbalanceAngle * Math.PI / 180;
                float endX = (float)(centerX + radius * unbalanceAmplitude / maxAmplitude * Math.Cos(rad));
                float endY = (float)(centerY - radius * unbalanceAmplitude / maxAmplitude * Math.Sin(rad));
                g.DrawLine(unbalancePen, centerX, centerY, endX, endY);
                g.FillEllipse(unbalanceBrush, endX - 4, endY - 4, 8, 8);

                // Dibujar leyenda
                if (showLegends)
                {
                    string legend = $"Desbalance: {unbalanceAngle:F1}°, {unbalanceAmplitude:F3}";
                    float labelX = endX + 5;
                    float labelY = endY - 5;
                    if (labelX + 100 > Width - padding) labelX = endX - 105;
                    if (labelY < padding) labelY = endY + 5;
                    g.DrawString(legend, labelFont, unbalanceBrush, labelX, labelY);
                }
            }

            // Dibujar el ángulo de corrección
            double correctionRad = correctionAngle * Math.PI / 180;
            float correctionEndX = (float)(centerX + radius * Math.Cos(correctionRad));
            float correctionEndY = (float)(centerY - radius * Math.Sin(correctionRad));
            g.DrawLine(correctionPen, centerX, centerY, correctionEndX, correctionEndY);
            g.FillEllipse(correctionBrush, correctionEndX - 4, correctionEndY - 4, 8, 8);

            // Dibujar leyenda de corrección
            if (showLegends)
            {
                string correctionLegend = $"Corrección: {correctionAngle:F1}°";
                float labelX = correctionEndX + 5;
                float labelY = correctionEndY - 5;
                if (labelX + 100 > Width - padding) labelX = correctionEndX - 105;
                if (labelY < padding) labelY = correctionEndY + 5;
                g.DrawString(correctionLegend, labelFont, correctionBrush, labelX, labelY);
            }

            // Título
            if (showLegends)
            {
                g.DrawString("Gráfico Polar de Desbalance", titleFont, textBrush, padding, 10);
            }

            LogManager.Instance.LogDebug("Gráfico polar dibujado correctamente.");
        }
    }
}*/