using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using VibrationAnalysis.Core.Analysis;
using VibrationAnalysis.Core.Balancing;
using VibrationAnalysis.Core.Model;
using VibrationAnalysis.Core.Simulation;

namespace VibrationAnalysis.UI
{
    /// <summary>Convierte un enum a textos en español para la grilla de propiedades.</summary>
    public class SpanishEnumConverter<T> : EnumConverter where T : struct, Enum
    {
        private readonly Func<T, string> name;
        public SpanishEnumConverter(Func<T, string> name) : base(typeof(T)) { this.name = name; }
        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType) =>
            destinationType == typeof(string) && value is T t ? name(t) : base.ConvertTo(context, culture, value, destinationType);
        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string s)
                foreach (T t in Enum.GetValues(typeof(T))) if (name(t) == s) return t;
            return base.ConvertFrom(context, culture, value);
        }
    }

    public sealed class MachineClassConverter : SpanishEnumConverter<MachineClass> { public MachineClassConverter() : base(Severity.Name) { } }

    public sealed class RotorTypeConverter : SpanishEnumConverter<RotorType>
    {
        public RotorTypeConverter() : base(t => t switch { RotorType.Solid => "Macizo", RotorType.Hollow => "Hueco (tubo)", _ => "Impulsor / turbina" }) { }
    }

    public sealed class MaterialConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context) => new StandardValuesCollection(Materials.Density.Keys.ToList());
    }

    public sealed class GradeConverter : DoubleConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context) => new StandardValuesCollection(BalanceQuality.Grades);
    }

    /// <summary>Vista editable de la máquina para la grilla de propiedades.</summary>
    public sealed class MachineView
    {
        private readonly Machine m;
        public MachineView(Machine machine) { m = machine; }
        [Browsable(false)] public Machine Model => m;

        [Category("1. Identificación"), DisplayName("Equipo"), Description("Nombre o TAG del equipo.")]
        public string Name { get => m.Name; set => m.Name = value; }
        [Category("1. Identificación"), DisplayName("Ubicación"), Description("Planta, sector o línea.")]
        public string Location { get => m.Location; set => m.Location = value; }

        [Category("2. Operación"), DisplayName("Velocidad (rpm)"), Description("Velocidad de operación nominal. Si hay tacómetro se usa la medida.")]
        public double Rpm { get => m.Rpm; set => m.Rpm = Math.Max(1, value); }
        [Category("2. Operación"), DisplayName("Criterio de severidad"), Description("Clase o grupo de máquina para los límites de severidad (velocidad RMS 10–1000 Hz)."), TypeConverter(typeof(MachineClassConverter))]
        public MachineClass MachineClass { get => m.MachineClass; set => m.MachineClass = value; }
        [Category("2. Operación"), DisplayName("Frecuencia de red (Hz)"), Description("50 Hz en Argentina; se usa para detectar problemas eléctricos (2× red).")]
        public double LineFrequencyHz { get => m.LineFrequencyHz; set => m.LineFrequencyHz = value; }

        [Category("3. Rotor"), DisplayName("Tipo de rotor"), TypeConverter(typeof(RotorTypeConverter))]
        public RotorType RotorType { get => m.RotorType; set => m.RotorType = value; }
        [Category("3. Rotor"), DisplayName("Material"), TypeConverter(typeof(MaterialConverter))]
        public string Material { get => m.Material; set => m.Material = value; }
        [Category("3. Rotor"), DisplayName("Diámetro (mm)")]
        public double DiameterMm { get => m.DiameterMm; set => m.DiameterMm = Math.Max(1, value); }
        [Category("3. Rotor"), DisplayName("Diámetro interior (mm)"), Description("Solo para rotores huecos.")]
        public double InnerDiameterMm { get => m.InnerDiameterMm; set => m.InnerDiameterMm = Math.Max(0, value); }
        [Category("3. Rotor"), DisplayName("Largo (mm)"), Description("Largo del rotor o distancia entre planos de corrección.")]
        public double LengthMm { get => m.LengthMm; set => m.LengthMm = Math.Max(1, value); }
        [Category("3. Rotor"), DisplayName("Masa (kg)"), Description("0 = estimar con la geometría y el material.")]
        public double MassKg { get => m.MassKg; set => m.MassKg = Math.Max(0, value); }
        [Category("3. Rotor"), DisplayName("Masa usada (kg)"), Description("Masa informada o estimada.")]
        public string EffectiveMass => m.EffectiveMassKg.ToString("0.00");
        [Category("3. Rotor"), DisplayName("Álabes / posiciones fijas"), Description("Cantidad de álabes o agujeros donde se pueden colocar pesos (0 = posición libre).")]
        public int Blades { get => m.Blades; set => m.Blades = Math.Max(0, value); }
        [Category("3. Rotor"), DisplayName("Polos del motor"), Description("Polos del motor de inducción (2, 4, 6…). Se usa en el análisis de corriente (MCSA): velocidad sincrónica = 120·f/polos.")]
        public int Poles { get => m.Poles; set => m.Poles = Math.Clamp(value / 2 * 2, 2, 48); }
        [Category("3. Rotor"), DisplayName("Dientes del engranaje"), Description("Dientes del engranaje montado en este eje (0 = sin engranaje). Se usa para la frecuencia de engrane (GMF).")]
        public int GearTeeth { get => m.GearTeeth; set => m.GearTeeth = Math.Max(0, value); }
        [Category("3. Rotor"), DisplayName("Eje vertical")]
        public bool VerticalShaft { get => m.VerticalShaft; set => m.VerticalShaft = value; }

        [Category("4. Balanceo"), DisplayName("Radio de corrección (mm)"), Description("Radio donde se colocan o perforan los pesos.")]
        public double CorrectionRadiusMm { get => m.CorrectionRadiusMm; set => m.CorrectionRadiusMm = Math.Max(1, value); }
        [Category("4. Balanceo"), DisplayName("Grado de calidad ISO 1940 (G)"), Description("G6,3 para ventiladores, bombas y motores; G2,5 para turbinas y máquinas herramienta."), TypeConverter(typeof(GradeConverter))]
        public double BalanceGrade { get => m.BalanceGrade; set => m.BalanceGrade = value; }

        [Category("5. Rodamiento (opcional)"), DisplayName("Cantidad de elementos rodantes"), Description("0 = sin datos de rodamiento.")]
        public int Balls { get => m.Bearing?.Balls ?? 0; set => m.Bearing = value <= 0 ? null : (m.Bearing ?? new BearingGeometry(9, 7.94, 39, 0)) with { Balls = value }; }
        [Category("5. Rodamiento (opcional)"), DisplayName("Diámetro de bola (mm)")]
        public double BallDiameter { get => m.Bearing?.BallDiameterMm ?? 0; set { if (m.Bearing != null) m.Bearing = m.Bearing with { BallDiameterMm = value }; } }
        [Category("5. Rodamiento (opcional)"), DisplayName("Diámetro primitivo (mm)")]
        public double PitchDiameter { get => m.Bearing?.PitchDiameterMm ?? 0; set { if (m.Bearing != null) m.Bearing = m.Bearing with { PitchDiameterMm = value }; } }
        [Category("5. Rodamiento (opcional)"), DisplayName("Ángulo de contacto (°)")]
        public double ContactAngle { get => m.Bearing?.ContactAngleDeg ?? 0; set { if (m.Bearing != null) m.Bearing = m.Bearing with { ContactAngleDeg = value }; } }
    }

    /// <summary>Vista editable del rotor virtual.</summary>
    public sealed class SimulatorView
    {
        private readonly SimulatorSettings s;
        public SimulatorView(SimulatorSettings settings) { s = settings; }
        [Category("Rotor virtual"), DisplayName("Velocidad (rpm)")] public double Rpm { get => s.Rpm; set => s.Rpm = Math.Clamp(value, 60, 60000); }
        [Category("Rotor virtual"), DisplayName("Sensores"), Description("1 o 2 sensores (uno por plano).")] public int Sensors { get => s.Sensors; set => s.Sensors = Math.Clamp(value, 1, 2); }
        [Category("Rotor virtual"), DisplayName("Desbalance plano 1 (g·mm)")] public double U1 { get => s.Unbalance1.Magnitude; set => s.Unbalance1 = s.Unbalance1 with { Magnitude = Math.Max(0, value) }; }
        [Category("Rotor virtual"), DisplayName("Ángulo desbalance plano 1 (°)")] public double U1Angle { get => s.Unbalance1.AngleDeg; set => s.Unbalance1 = s.Unbalance1 with { AngleDeg = value }; }
        [Category("Rotor virtual"), DisplayName("Desbalance plano 2 (g·mm)")] public double U2 { get => s.Unbalance2.Magnitude; set => s.Unbalance2 = s.Unbalance2 with { Magnitude = Math.Max(0, value) }; }
        [Category("Rotor virtual"), DisplayName("Ángulo desbalance plano 2 (°)")] public double U2Angle { get => s.Unbalance2.AngleDeg; set => s.Unbalance2 = s.Unbalance2 with { AngleDeg = value }; }
        [Category("Adquisición"), DisplayName("Frecuencia de muestreo (Hz)")] public double SampleRate { get => s.SampleRate; set => s.SampleRate = Math.Clamp(value, 256, 51200); }
        [Category("Adquisición"), DisplayName("Muestras")] public int Samples { get => s.Samples; set => s.Samples = Math.Clamp(value, 512, 262144); }
        [Category("Adquisición"), DisplayName("Ruido (mm/s)")] public double Noise { get => s.NoiseMmS; set => s.NoiseMmS = Math.Max(0, value); }
        [Category("Fallas"), DisplayName("Desalineación (mm/s)")] public double Misalignment { get => s.Misalignment; set => s.Misalignment = Math.Max(0, value); }
        [Category("Fallas"), DisplayName("Holgura mecánica (mm/s)")] public double Looseness { get => s.Looseness; set => s.Looseness = Math.Max(0, value); }
        [Category("Fallas"), DisplayName("Rodamiento pista externa (mm/s)")] public double Bearing { get => s.BearingOuterRace; set => s.BearingOuterRace = Math.Max(0, value); }
        [Category("Fallas"), DisplayName("Remolino de aceite (mm/s)")] public double OilWhirl { get => s.OilWhirl; set => s.OilWhirl = Math.Max(0, value); }
        [Category("Fallas"), DisplayName("Eléctrico 2× red (mm/s)")] public double Electrical { get => s.Electrical; set => s.Electrical = Math.Max(0, value); }
        [Category("Fallas"), DisplayName("Dientes del engranaje")] public int GearTeeth { get => s.GearTeeth; set => s.GearTeeth = Math.Clamp(value, 0, 500); }
        [Category("Fallas"), DisplayName("Desgaste de engranaje (mm/s)"), Description("Frecuencia de engrane con bandas laterales a ±1X.")] public double GearWear { get => s.GearWear; set => s.GearWear = Math.Max(0, value); }
        [Category("Arranque / parada"), DisplayName("Simular arranque o parada"), Description("Rampa lineal de velocidad durante el registro (usar 65536 muestras o más).")] public bool RunUp { get => s.RunUp; set => s.RunUp = value; }
        [Category("Arranque / parada"), DisplayName("rpm inicial")] public double RpmStart { get => s.RpmStart; set => s.RpmStart = Math.Clamp(value, 30, 60000); }
        [Category("Arranque / parada"), DisplayName("rpm final"), Description("Menor que la inicial = parada (coast-down).")] public double RpmEnd { get => s.RpmEnd; set => s.RpmEnd = Math.Clamp(value, 30, 60000); }
        [Category("Arranque / parada"), DisplayName("Velocidad crítica (rpm)")] public double CriticalRpm { get => s.CriticalRpm; set => s.CriticalRpm = Math.Clamp(value, 30, 60000); }
        [Category("Arranque / parada"), DisplayName("Amortiguamiento ζ"), Description("0,02 = poco amortiguado (pico agudo) · 0,2 = muy amortiguado.")] public double Damping { get => s.Damping; set => s.Damping = Math.Clamp(value, 0.005, 1); }
        [Category("Rotor virtual"), DisplayName("Sensores X-Y a 90° (órbita)"), Description("El sensor 2 se ubica a 90° del 1 en el mismo plano para ver la órbita (requiere 2 sensores).")] public bool ProbesXY { get => s.ProbesXY; set { s.ProbesXY = value; if (value) s.Sensors = 2; } }
        [Category("Pesos colocados"), DisplayName("Masas en el rotor"), Description("Pesos de prueba y de corrección colocados en el rotor virtual.")]
        public string Masses => s.AddedMasses.Count == 0 ? "ninguna" : string.Join("; ", s.AddedMasses.Select(m => $"P{m.Plane}: {m.Grams:0.##} g ∠ {m.AngleDeg:0}°"));
    }
}
