using VibrationAnalysis.Core.Dsp;

namespace VibrationAnalysis.Core.Balancing;

/// <summary>Grados de calidad de balanceo G (ISO 1940-1 / ISO 21940-11), en mm/s.</summary>
public static class BalanceQuality
{
    public static readonly double[] Grades = { 0.4, 1, 2.5, 6.3, 16, 40, 100, 250, 630, 1600, 4000 };

    public static string Examples(double g) => g switch
    {
        <= 0.4 => "Giróscopos, husillos de precisión",
        <= 1 => "Rectificadoras, accionamientos de grabadores",
        <= 2.5 => "Turbinas de gas y vapor, turbocompresores, accionamientos de máquinas herramienta",
        <= 6.3 => "Ventiladores, bombas, rotores de motores eléctricos, volantes, maquinaria de proceso",
        <= 16 => "Ejes cardán, partes de máquinas agrícolas, cigüeñales de motores",
        <= 40 => "Llantas y ruedas de automóviles, cigüeñales de motores rápidos",
        <= 100 => "Motores diésel de alta velocidad completos",
        _ => "Cigüeñales de motores grandes y lentos",
    };

    /// <summary>Desbalance residual admisible total U_per (g·mm) = 9549 · G · M / n.</summary>
    public static double PermissibleUnbalanceGmm(double grade, double rotorMassKg, double rpm) =>
        rpm > 0 ? 9549.0 * grade * rotorMassKg / rpm : double.PositiveInfinity;

    /// <summary>Excentricidad admisible e_per (µm = g·mm/kg).</summary>
    public static double PermissibleEccentricityUm(double grade, double rpm) => rpm > 0 ? 9549.0 * grade / rpm : double.PositiveInfinity;

    /// <summary>
    /// Reparto del desbalance admisible a dos planos según la posición del centro de masa
    /// (distancias del centro de masa a cada plano). Si son iguales, se reparte por mitades.
    /// </summary>
    public static (double Plane1, double Plane2) SplitToPlanes(double uPer, double distCgToPlane1, double distCgToPlane2)
    {
        double l = distCgToPlane1 + distCgToPlane2;
        if (l <= 0) return (uPer / 2, uPer / 2);
        return (uPer * distCgToPlane2 / l, uPer * distCgToPlane1 / l);
    }

    /// <summary>Desbalance residual estimado (g·mm) a partir de la vibración residual y la sensibilidad (vibración por gramo) al radio de corrección.</summary>
    public static double ResidualUnbalanceGmm(double residualVibration, double sensitivityPerGram, double correctionRadiusMm) =>
        sensitivityPerGram > 0 ? residualVibration / sensitivityPerGram * correctionRadiusMm : double.PositiveInfinity;
}

public static class TrialWeight
{
    /// <summary>
    /// Peso de prueba sugerido (g) para que la fuerza centrífuga sea una fracción (por defecto 10 %) del peso del rotor:
    /// m = f · M · g / (r · ω²).
    /// </summary>
    public static double SuggestGrams(double rotorMassKg, double rpm, double radiusMm, double fraction = 0.10)
    {
        if (rpm <= 0 || radiusMm <= 0) return 0;
        double w = rpm * 2 * Math.PI / 60;
        double r = radiusMm / 1000;
        return fraction * rotorMassKg * SignalTools.G / (r * w * w) * 1000;
    }
}

/// <summary>Reparto de una masa de corrección entre posiciones fijas (álabes, agujeros, bulones).</summary>
public static class WeightSplitter
{
    /// <summary>
    /// Divide la masa W∠θ entre las dos posiciones vecinas de <paramref name="positions"/> posiciones equidistantes
    /// (la posición 0 está en <paramref name="firstPositionDeg"/>). Usa la ley de los senos.
    /// </summary>
    public static (int PosA, double AngleA, double MassA, int PosB, double AngleB, double MassB) Split(Phasor w, int positions, double firstPositionDeg = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(positions, 2);
        double step = 360.0 / positions;
        double rel = Angles.Normalize(w.AngleDeg - firstPositionDeg);
        int a = (int)Math.Floor(rel / step) % positions;
        int b = (a + 1) % positions;
        double angA = Angles.Normalize(firstPositionDeg + a * step);
        double angB = Angles.Normalize(firstPositionDeg + b * step);
        double alpha = (rel - a * step) * Math.PI / 180; // desde A hacia W
        double beta = step * Math.PI / 180 - alpha;      // desde W hacia B
        double sinStep = Math.Sin(step * Math.PI / 180);
        double mA = w.Magnitude * Math.Sin(beta) / sinStep;
        double mB = w.Magnitude * Math.Sin(alpha) / sinStep;
        return (a, angA, mA, b, angB, mB);
    }
}

/// <summary>Cálculo de perforaciones para quitar masa (la corrección se aplica a 180° del vector de agregado).</summary>
public static class DrillCalculator
{
    /// <summary>
    /// Profundidad (mm) del agujero cilíndrico (con punta cónica de 118°) para quitar <paramref name="massG"/> gramos.
    /// </summary>
    public static double DepthMm(double massG, double densityKgM3, double drillDiameterMm)
    {
        if (massG <= 0) return 0;
        double r = drillDiameterMm / 2 / 1000;
        double vol = massG / 1000 / densityKgM3; // m³
        double tipH = r / Math.Tan(59 * Math.PI / 180);
        double cone = Math.PI * r * r * tipH / 3;
        double cylArea = Math.PI * r * r;
        double depth = vol <= cone ? Math.Cbrt(vol * 3 * tipH * tipH / (Math.PI * r * r)) : tipH + (vol - cone) / cylArea;
        return depth * 1000;
    }

    /// <summary>Posición de la perforación: opuesta al vector de masa a agregar.</summary>
    public static double DrillAngle(double addAngleDeg) => Angles.Normalize(addAngleDeg + 180);
}
