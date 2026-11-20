using System.Numerics;
using VibrationAnalysis.Core.Dsp;

namespace VibrationAnalysis.Core.Balancing;

/// <summary>Advertencia sobre la calidad de una corrida de prueba.</summary>
public sealed record TrialCheck(bool Ok, double AmplitudeChangePercent, double PhaseChangeDeg, string Message);

/// <summary>Resultado del balanceo en un plano.</summary>
public sealed record SinglePlaneResult(
    Complex InfluenceCoefficient,
    Phasor Correction,
    Phasor CorrectionKeepingTrial,
    TrialCheck Check)
{
    /// <summary>Sensibilidad: vibración por gramo (unidad de vibración / g) y su ángulo de retraso.</summary>
    public Phasor Sensitivity => Phasor.FromComplex(InfluenceCoefficient);
    /// <summary>Vibración residual prevista si la corrección se aplica exactamente.</summary>
    public Phasor PredictResidual(Phasor initial, Phasor appliedCorrection) =>
        Phasor.FromComplex(initial.ToComplex() + InfluenceCoefficient * appliedCorrection.ToComplex());
}

/// <summary>Resultado del balanceo en dos planos.</summary>
public sealed record TwoPlaneResult(
    Complex[,] InfluenceMatrix,
    Phasor CorrectionPlane1,
    Phasor CorrectionPlane2,
    Phasor CorrectionPlane1KeepingTrial,
    Phasor CorrectionPlane2KeepingTrial,
    TrialCheck CheckPlane1,
    TrialCheck CheckPlane2,
    double ConditionNumber)
{
    public (Phasor Sensor1, Phasor Sensor2) PredictResidual(Phasor o1, Phasor o2, Phasor w1, Phasor w2)
    {
        var a = InfluenceMatrix;
        var r1 = o1.ToComplex() + a[0, 0] * w1.ToComplex() + a[0, 1] * w2.ToComplex();
        var r2 = o2.ToComplex() + a[1, 0] * w1.ToComplex() + a[1, 1] * w2.ToComplex();
        return (Phasor.FromComplex(r1), Phasor.FromComplex(r2));
    }
}

/// <summary>
/// Balanceo de campo por el método de coeficientes de influencia (ISO 21940 / práctica estándar):
/// 1) se mide la vibración original O (amplitud y fase a 1X),
/// 2) se coloca un peso de prueba T y se mide R,
/// 3) el coeficiente de influencia es α = (R − O) / T,
/// 4) la corrección es W = −O / α (en dos planos se resuelve el sistema [α]·W = −O).
/// Las fases y las posiciones angulares deben medirse con la misma convención (por ejemplo, ángulos en el sentido de giro desde la marca de referencia).
/// </summary>
public static class InfluenceBalancer
{
    /// <summary>Calidad mínima de la corrida de prueba: cambio de amplitud ≥ 30 % o de fase ≥ 30°.</summary>
    public static TrialCheck CheckTrial(Phasor original, Phasor withTrial)
    {
        double dA = original.Magnitude > 0 ? Math.Abs(withTrial.Magnitude - original.Magnitude) / original.Magnitude * 100 : 100;
        double dP = Math.Abs(Angles.Difference(withTrial.AngleDeg, original.AngleDeg));
        double vecChange = original.Magnitude > 0 ? (withTrial - original).Magnitude / original.Magnitude * 100 : 100;
        bool ok = dA >= 30 || dP >= 30 || vecChange >= 30;
        string msg = ok
            ? $"Corrida de prueba válida: cambio de amplitud {dA:F0} %, de fase {dP:F0}°."
            : $"El peso de prueba produjo poco efecto (amplitud {dA:F0} %, fase {dP:F0}°). Aumentarlo o cambiar su posición para mejorar la precisión.";
        if (ok && withTrial.Magnitude > original.Magnitude * 2.5)
            msg += " Atención: la vibración con peso de prueba subió mucho; usar un peso menor si la máquina lo exige.";
        return new TrialCheck(ok, dA, dP, msg);
    }

    public static SinglePlaneResult SinglePlane(Phasor original, Phasor trialMass, Phasor withTrial)
    {
        if (trialMass.Magnitude <= 0) throw new ArgumentException("El peso de prueba debe ser mayor que cero.", nameof(trialMass));
        var o = original.ToComplex();
        var t = trialMass.ToComplex();
        var r = withTrial.ToComplex();
        var alpha = (r - o) / t;
        if (alpha.Magnitude < 1e-12) throw new InvalidOperationException("El peso de prueba no produjo ningún cambio medible: no se puede calcular el coeficiente de influencia.");
        var w = -o / alpha;
        var wKeep = w - t;
        return new SinglePlaneResult(alpha, Phasor.FromComplex(w), Phasor.FromComplex(wKeep), CheckTrial(original, withTrial));
    }

    /// <summary>Corrección de ajuste (trim) usando un coeficiente ya conocido.</summary>
    public static Phasor Trim(Complex influence, Phasor residual) => Phasor.FromComplex(-residual.ToComplex() / influence);

    /// <summary>
    /// Dos planos. Corrida 0: O1, O2. Corrida 1: peso T1 en el plano 1 ⇒ R11 (sensor 1), R21 (sensor 2).
    /// Se retira T1. Corrida 2: peso T2 en el plano 2 ⇒ R12, R22.
    /// </summary>
    public static TwoPlaneResult TwoPlanes(Phasor o1, Phasor o2, Phasor t1, Phasor r11, Phasor r21, Phasor t2, Phasor r12, Phasor r22)
    {
        if (t1.Magnitude <= 0 || t2.Magnitude <= 0) throw new ArgumentException("Los pesos de prueba deben ser mayores que cero.");
        var O1 = o1.ToComplex(); var O2 = o2.ToComplex();
        var T1 = t1.ToComplex(); var T2 = t2.ToComplex();
        var a11 = (r11.ToComplex() - O1) / T1;
        var a21 = (r21.ToComplex() - O2) / T1;
        var a12 = (r12.ToComplex() - O1) / T2;
        var a22 = (r22.ToComplex() - O2) / T2;
        var det = a11 * a22 - a12 * a21;
        if (det.Magnitude < 1e-14) throw new InvalidOperationException("La matriz de influencia es singular: los dos planos producen el mismo efecto. Separar más los planos o repetir las corridas.");
        // [a11 a12; a21 a22]·[W1; W2] = −[O1; O2]
        var w1 = (-O1 * a22 + O2 * a12) / det;
        var w2 = (-O2 * a11 + O1 * a21) / det;
        var m = new Complex[2, 2] { { a11, a12 }, { a21, a22 } };
        return new TwoPlaneResult(m, Phasor.FromComplex(w1), Phasor.FromComplex(w2),
            Phasor.FromComplex(w1 - T1), Phasor.FromComplex(w2 - T2),
            CheckTrial(o1, r11), CheckTrial(o2, r22), Condition(m));
    }

    /// <summary>Corrección de ajuste en dos planos con la matriz ya conocida.</summary>
    public static (Phasor W1, Phasor W2) Trim(Complex[,] a, Phasor res1, Phasor res2)
    {
        var det = a[0, 0] * a[1, 1] - a[0, 1] * a[1, 0];
        var r1 = res1.ToComplex(); var r2 = res2.ToComplex();
        var w1 = (-r1 * a[1, 1] + r2 * a[0, 1]) / det;
        var w2 = (-r2 * a[0, 0] + r1 * a[1, 0]) / det;
        return (Phasor.FromComplex(w1), Phasor.FromComplex(w2));
    }

    /// <summary>Número de condición (norma de Frobenius) de la matriz 2×2: valores &gt; 10 indican planos poco independientes.</summary>
    public static double Condition(Complex[,] a)
    {
        double fro(Complex[,] m) => Math.Sqrt(m[0, 0].Magnitude * m[0, 0].Magnitude + m[0, 1].Magnitude * m[0, 1].Magnitude + m[1, 0].Magnitude * m[1, 0].Magnitude + m[1, 1].Magnitude * m[1, 1].Magnitude);
        var det = a[0, 0] * a[1, 1] - a[0, 1] * a[1, 0];
        var inv = new Complex[2, 2] { { a[1, 1] / det, -a[0, 1] / det }, { -a[1, 0] / det, a[0, 0] / det } };
        return fro(a) * fro(inv) / 2;
    }
}
