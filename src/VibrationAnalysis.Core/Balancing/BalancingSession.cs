using System.Numerics;
using VibrationAnalysis.Core.Dsp;

namespace VibrationAnalysis.Core.Balancing;

public enum BalancingStep
{
    /// <summary>Medir la vibración original (sin pesos).</summary>
    Initial,
    /// <summary>Colocar el peso de prueba en el plano 1 y medir.</summary>
    Trial1,
    /// <summary>Retirar el peso del plano 1, colocar el de prueba en el plano 2 y medir.</summary>
    Trial2,
    /// <summary>Corrección calculada: aplicarla.</summary>
    Correction,
    /// <summary>Medir con la corrección aplicada (verificación y ajuste).</summary>
    Verification,
    Done,
}

/// <summary>Una corrida registrada.</summary>
public sealed record BalancingRun(string Name, Phasor Sensor1, Phasor? Sensor2, double Rpm, Phasor? Trial, int TrialPlane, DateTime Time);

/// <summary>
/// Flujo guiado de balanceo de campo. Garantiza el orden de los pasos (cada acción solo se habilita cuando corresponde)
/// y calcula la corrección y el ajuste (trim) con el método de coeficientes de influencia.
/// </summary>
public sealed class BalancingSession
{
    public int Planes { get; }
    /// <summary>
    /// true: las posiciones de los pesos se miden en sentido contrario al giro (convención de retraso de fase, la habitual).
    /// false: se miden en el sentido de giro; internamente se invierte el signo del ángulo.
    /// </summary>
    public bool AnglesAgainstRotation { get; set; } = true;
    public double ToleranceVibration { get; set; } = 1.0;
    public BalancingStep Step { get; private set; } = BalancingStep.Initial;
    public List<BalancingRun> Runs { get; } = new();

    public BalancingRun? Initial { get; private set; }
    public BalancingRun? Trial1Run { get; private set; }
    public BalancingRun? Trial2Run { get; private set; }
    public SinglePlaneResult? Single { get; private set; }
    public TwoPlaneResult? Two { get; private set; }
    /// <summary>Correcciones a aplicar (en la convención del usuario).</summary>
    public Phasor[] Corrections { get; private set; } = Array.Empty<Phasor>();
    public bool KeepTrialWeights { get; set; }
    public int TrimCount { get; private set; }

    public BalancingSession(int planes)
    {
        if (planes is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(planes));
        Planes = planes;
    }

    private Phasor In(Phasor userMass) => AnglesAgainstRotation ? userMass : userMass with { AngleDeg = Angles.Normalize(-userMass.AngleDeg) };
    private Phasor Out(Phasor internalMass) => AnglesAgainstRotation ? internalMass : internalMass with { AngleDeg = Angles.Normalize(-internalMass.AngleDeg) };

    public bool CanRecordInitial => Step == BalancingStep.Initial;
    public bool CanRecordTrial1 => Step == BalancingStep.Trial1;
    public bool CanRecordTrial2 => Step == BalancingStep.Trial2;
    public bool CanVerify => Step is BalancingStep.Correction or BalancingStep.Verification;

    public void RecordInitial(Phasor s1, Phasor? s2, double rpm)
    {
        Require(CanRecordInitial, "La medición inicial ya se registró.");
        RequireSensors(s2);
        Initial = Add("Original", s1, s2, rpm, null, 0);
        Step = BalancingStep.Trial1;
    }

    public TrialCheck RecordTrial1(Phasor trialMass, Phasor s1, Phasor? s2, double rpm)
    {
        Require(CanRecordTrial1, "Primero registrar la medición inicial.");
        RequireSensors(s2);
        Trial1Run = Add("Prueba plano 1", s1, s2, rpm, trialMass, 1);
        if (Planes == 1)
        {
            Single = InfluenceBalancer.SinglePlane(Initial!.Sensor1, In(trialMass), s1);
            Corrections = new[] { Out(KeepTrialWeights ? Single.CorrectionKeepingTrial : Single.Correction) };
            Step = BalancingStep.Correction;
            return Single.Check;
        }
        Step = BalancingStep.Trial2;
        return InfluenceBalancer.CheckTrial(Initial!.Sensor1, s1);
    }

    public TrialCheck RecordTrial2(Phasor trialMass, Phasor s1, Phasor s2, double rpm)
    {
        Require(CanRecordTrial2, "Primero registrar la corrida de prueba del plano 1.");
        Trial2Run = Add("Prueba plano 2", s1, s2, rpm, trialMass, 2);
        var i = Initial!; var t1 = Trial1Run!;
        Two = InfluenceBalancer.TwoPlanes(i.Sensor1, i.Sensor2!.Value, In(t1.Trial!.Value), t1.Sensor1, t1.Sensor2!.Value, In(trialMass), s1, s2);
        Corrections = KeepTrialWeights
            ? new[] { Out(Two.CorrectionPlane1KeepingTrial), Out(Two.CorrectionPlane2KeepingTrial) }
            : new[] { Out(Two.CorrectionPlane1), Out(Two.CorrectionPlane2) };
        Step = BalancingStep.Correction;
        return Two.CheckPlane2;
    }

    /// <summary>Vibración prevista tras aplicar las correcciones exactas.</summary>
    public Phasor[] PredictedResidual()
    {
        if (Planes == 1 && Single != null && Initial != null)
        {
            var w = In(Corrections[0]);
            if (KeepTrialWeights) w = w + In(Trial1Run!.Trial!.Value);
            return new[] { Single.PredictResidual(Initial.Sensor1, w) };
        }
        if (Two != null && Initial != null)
        {
            var w1 = In(Corrections[0]); var w2 = In(Corrections[1]);
            if (KeepTrialWeights) { w1 = w1 + In(Trial1Run!.Trial!.Value); w2 = w2 + In(Trial2Run!.Trial!.Value); }
            var (a, b) = Two.PredictResidual(Initial.Sensor1, Initial.Sensor2!.Value, w1, w2);
            return new[] { a, b };
        }
        return Array.Empty<Phasor>();
    }

    /// <summary>
    /// Registra la verificación con la corrección colocada. Si la vibración supera la tolerancia,
    /// calcula una corrección de ajuste (trim) con los coeficientes ya obtenidos.
    /// </summary>
    public (bool WithinTolerance, Phasor[] Trim) RecordVerification(Phasor s1, Phasor? s2, double rpm)
    {
        Require(CanVerify, "Primero calcular la corrección.");
        Add(TrimCount == 0 ? "Verificación" : $"Verificación {TrimCount + 1}", s1, s2, rpm, null, 0);
        double worst = Math.Max(s1.Magnitude, s2?.Magnitude ?? 0);
        if (worst <= ToleranceVibration)
        {
            Step = BalancingStep.Done;
            return (true, Array.Empty<Phasor>());
        }
        TrimCount++;
        Step = BalancingStep.Verification;
        if (Planes == 1) return (false, new[] { Out(InfluenceBalancer.Trim(Single!.InfluenceCoefficient, s1)) });
        var (w1, w2) = InfluenceBalancer.Trim(Two!.InfluenceMatrix, s1, s2!.Value);
        return (false, new[] { Out(w1), Out(w2) });
    }

    public void Finish() => Step = BalancingStep.Done;

    /// <summary>Sensibilidad directa (vibración por gramo) de cada plano.</summary>
    public double[] Sensitivities()
    {
        if (Single != null) return new[] { Single.InfluenceCoefficient.Magnitude };
        if (Two != null) return new[] { Two.InfluenceMatrix[0, 0].Magnitude, Two.InfluenceMatrix[1, 1].Magnitude };
        return Array.Empty<double>();
    }

    private BalancingRun Add(string name, Phasor s1, Phasor? s2, double rpm, Phasor? trial, int plane)
    {
        var r = new BalancingRun(name, s1, s2, rpm, trial, plane, DateTime.Now);
        Runs.Add(r);
        return r;
    }

    private void RequireSensors(Phasor? s2)
    {
        if (Planes == 2 && s2 == null) throw new ArgumentException("El balanceo en dos planos necesita la lectura de los dos sensores.");
    }

    private static void Require(bool ok, string msg)
    {
        if (!ok) throw new InvalidOperationException(msg);
    }
}
