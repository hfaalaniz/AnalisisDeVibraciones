using VibrationAnalysis.Core.Dsp;

namespace VibrationAnalysis.Core.Analysis;

/// <summary>Geometría de un rodamiento para calcular sus frecuencias de defecto.</summary>
public sealed record BearingGeometry(int Balls, double BallDiameterMm, double PitchDiameterMm, double ContactAngleDeg = 0)
{
    private double Ratio => BallDiameterMm / PitchDiameterMm * Math.Cos(ContactAngleDeg * Math.PI / 180);
    /// <summary>Frecuencia de defecto en pista externa, en órdenes de la velocidad de giro.</summary>
    public double BpfoOrder => Balls / 2.0 * (1 - Ratio);
    /// <summary>Frecuencia de defecto en pista interna, en órdenes.</summary>
    public double BpfiOrder => Balls / 2.0 * (1 + Ratio);
    /// <summary>Frecuencia de giro de los elementos rodantes, en órdenes.</summary>
    public double BsfOrder => PitchDiameterMm / (2 * BallDiameterMm) * (1 - Ratio * Ratio);
    /// <summary>Frecuencia de la jaula, en órdenes.</summary>
    public double FtfOrder => 0.5 * (1 - Ratio);
}

public enum FaultType
{
    Unbalance,
    Misalignment,
    MechanicalLooseness,
    OilWhirl,
    BearingOuterRace,
    BearingInnerRace,
    BearingBall,
    BladePass,
    Electrical,
}

public sealed record Finding(FaultType Type, string Title, double Confidence, string Evidence, string Recommendation);

public sealed class DiagnosisInput
{
    /// <summary>Espectro de velocidad (mm/s pico).</summary>
    public required Spectrum VelocitySpectrum { get; init; }
    public required double RunningSpeedHz { get; init; }
    public BearingGeometry? Bearing { get; init; }
    public int Blades { get; init; }
    /// <summary>Frecuencia de red (50 Hz en Argentina).</summary>
    public double LineFrequencyHz { get; init; } = 50;
    /// <summary>Amplitud 1X axial relativa a la radial, si se midió (0 si no).</summary>
    public double AxialToRadialRatio { get; init; }
    /// <summary>Curtosis de la señal de aceleración (impactos ⇒ valores &gt; 4).</summary>
    public double AccelerationKurtosis { get; init; }
}

/// <summary>
/// Diagnóstico por reglas sobre el espectro, según los patrones clásicos de análisis de vibraciones
/// (1X desbalance, 2X desalineación, armónicos y subarmónicos de holgura, frecuencias de rodamientos, etc.).
/// Las conclusiones son orientativas y deben confirmarse con mediciones en las tres direcciones y fase.
/// </summary>
public static class FaultDiagnosis
{
    public static IReadOnlyList<Finding> Diagnose(DiagnosisInput input)
    {
        var s = input.VelocitySpectrum;
        double f1 = input.RunningSpeedHz;
        if (f1 <= 0) return Array.Empty<Finding>();
        double tol = Math.Max(s.Resolution * 1.5, f1 * 0.02);
        double A(double order) => s.PeakNear(f1 * order, tol).Amplitude;
        double a1 = A(1), a2 = A(2), a3 = A(3), aHalf = A(0.5), aWhirl = Math.Max(A(0.43), A(0.47));
        double overall = s.BandRms(Math.Max(1, s.Resolution), s.Frequencies[^1], s.NoiseBandwidthBins) * Math.Sqrt(2);
        double floor = Math.Max(overall * 0.04, 1e-6);
        var res = new List<Finding>();

        // Desbalance: 1X dominante, armónicos bajos
        if (a1 > floor * 3)
        {
            double share = a1 / Math.Max(overall, 1e-9);
            double harm = (a2 + a3) / Math.Max(a1, 1e-9);
            double conf = Math.Clamp(share * (1 - Math.Min(harm, 1)) * 1.1, 0, 0.95);
            if (conf > 0.35)
                res.Add(new Finding(FaultType.Unbalance, "Desbalance", conf,
                    $"1X = {a1:F2} mm/s pico ({share * 100:F0} % de la vibración global); 2X+3X = {harm * 100:F0} % de 1X.",
                    "Confirmar con fase radial estable y desfase horizontal–vertical ≈ 90°. Balancear en 1 o 2 planos."));
        }

        // Desalineación: 2X alto respecto de 1X, axial alto
        if (a2 > floor * 3 && a2 > 0.5 * a1)
        {
            double conf = Math.Clamp(0.35 + 0.4 * Math.Min(a2 / Math.Max(a1, 1e-9), 1.5) / 1.5 + (input.AxialToRadialRatio > 0.5 ? 0.2 : 0), 0, 0.95);
            res.Add(new Finding(FaultType.Misalignment, "Desalineación", conf,
                $"2X = {a2:F2} mm/s ({a2 / Math.Max(a1, 1e-9) * 100:F0} % de 1X){(input.AxialToRadialRatio > 0 ? $"; axial/radial = {input.AxialToRadialRatio:F2}" : "")}.",
                "Verificar alineación del acople (láser o relojes), pata coja y tensiones de cañerías. Medir fase axial a ambos lados del acople."));
        }

        // Holgura mecánica: muchos armónicos y medios órdenes
        int harmonics = 0;
        for (int k = 2; k <= 10; k++) if (A(k) > Math.Max(floor * 3, a1 * 0.1)) harmonics++;
        int halfOrders = 0;
        foreach (var o in new[] { 1.5, 2.5, 3.5 }) if (A(o) > Math.Max(floor * 3, a1 * 0.05)) halfOrders++;
        if (harmonics >= 4 || (harmonics >= 3 && halfOrders >= 1) || aHalf > Math.Max(floor * 3, a1 * 0.2) && harmonics >= 2)
        {
            double conf = Math.Clamp(0.3 + 0.07 * harmonics + 0.1 * halfOrders, 0, 0.9);
            res.Add(new Finding(FaultType.MechanicalLooseness, "Holgura mecánica", conf,
                $"{harmonics} armónicos de 1X sobre el 10 % de 1X y {halfOrders} medios órdenes.",
                "Revisar torque de bulones de base y tapas de cojinetes, fisuras en la base y juego del rodamiento en su alojamiento."));
        }

        // Remolino de aceite (cojinetes de fricción)
        if (aWhirl > Math.Max(floor * 3, a1 * 0.3))
            res.Add(new Finding(FaultType.OilWhirl, "Remolino de aceite (oil whirl)", Math.Clamp(aWhirl / Math.Max(a1, 1e-9), 0.3, 0.85),
                $"Pico subsíncrono a 0,43–0,47X = {aWhirl:F2} mm/s.",
                "Solo en cojinetes de fricción: revisar holgura del cojinete, viscosidad y temperatura del aceite, y carga del eje."));

        // Rodamientos
        if (input.Bearing is { } b)
        {
            void Check(FaultType t, string name, double order, string where)
            {
                double fa = A(order), fb = A(order * 2);
                if (fa > floor * 2.5 || fb > floor * 2.5)
                {
                    double conf = Math.Clamp(0.35 + (fa + fb) / Math.Max(overall, 1e-9) + (input.AccelerationKurtosis > 4 ? 0.2 : 0), 0, 0.9);
                    res.Add(new Finding(t, name, conf,
                        $"Pico en {order:F2}X ({order * f1:F1} Hz) = {fa:F3} mm/s; 2× = {fb:F3} mm/s{(input.AccelerationKurtosis > 0 ? $"; curtosis {input.AccelerationKurtosis:F1}" : "")}.",
                        $"Defecto probable en {where}. Confirmar con espectro de envolvente en aceleración y planificar el cambio del rodamiento."));
                }
            }
            Check(FaultType.BearingOuterRace, "Rodamiento: pista externa (BPFO)", b.BpfoOrder, "la pista externa");
            Check(FaultType.BearingInnerRace, "Rodamiento: pista interna (BPFI)", b.BpfiOrder, "la pista interna");
            Check(FaultType.BearingBall, "Rodamiento: elementos rodantes (BSF)", b.BsfOrder * 2, "los elementos rodantes");
        }

        // Paso de álabes
        if (input.Blades > 1)
        {
            double ab = A(input.Blades);
            if (ab > Math.Max(floor * 3, a1 * 0.5))
                res.Add(new Finding(FaultType.BladePass, "Paso de álabes elevado", Math.Clamp(ab / Math.Max(a1, 1e-9) * 0.5, 0.3, 0.85),
                    $"{input.Blades}X ({input.Blades * f1:F1} Hz) = {ab:F2} mm/s.",
                    "Revisar huelgo entre rotor y carcasa/difusor, álabes dañados y condiciones de flujo (recirculación, cavitación)."));
        }

        // Eléctrico: 2× frecuencia de red
        double fe = 2 * input.LineFrequencyHz;
        {
            var pe = s.PeakNear(fe, Math.Max(s.Resolution * 1.5, 0.5));
            if (pe.Amplitude > Math.Max(floor * 3, a1 * 0.3) && Math.Abs(pe.Frequency - 2 * f1) > tol)
                res.Add(new Finding(FaultType.Electrical, "Problema eléctrico (2× red)", 0.5,
                    $"Pico en {fe:F0} Hz = {pe.Amplitude:F2} mm/s, separado de 2X ({2 * f1:F1} Hz).",
                    "Verificar entrehierro, desequilibrio de fases y barras del rotor. Si desaparece al cortar la alimentación, es eléctrico."));
        }

        return res.OrderByDescending(r => r.Confidence).ToList();
    }
}
