namespace VibrationAnalysis.Core.Analysis;

public enum SeverityZone { A, B, C, D }

/// <summary>Clase de máquina para evaluar la severidad por velocidad RMS (10–1000 Hz).</summary>
public enum MachineClass
{
    /// <summary>ISO 10816-1 Clase I: máquinas pequeñas, hasta 15 kW.</summary>
    Iso10816_1_ClassI,
    /// <summary>ISO 10816-1 Clase II: medianas, 15–75 kW sin fundación especial (o hasta 300 kW en fundación especial).</summary>
    Iso10816_1_ClassII,
    /// <summary>ISO 10816-1 Clase III: grandes en fundación rígida.</summary>
    Iso10816_1_ClassIII,
    /// <summary>ISO 10816-1 Clase IV: grandes en fundación flexible.</summary>
    Iso10816_1_ClassIV,
    /// <summary>ISO 10816-3 Grupo 2 (15–300 kW), fundación rígida.</summary>
    Iso10816_3_Group2Rigid,
    /// <summary>ISO 10816-3 Grupo 2 (15–300 kW), fundación flexible.</summary>
    Iso10816_3_Group2Flexible,
    /// <summary>ISO 10816-3 Grupo 1 (300 kW–50 MW), fundación rígida.</summary>
    Iso10816_3_Group1Rigid,
    /// <summary>ISO 10816-3 Grupo 1 (300 kW–50 MW), fundación flexible.</summary>
    Iso10816_3_Group1Flexible,
}

public sealed record SeverityResult(double VelocityRmsMmS, SeverityZone Zone, string Label, string Description, double[] Limits, string Standard);

/// <summary>Evaluación de severidad según los límites de zona A/B, B/C y C/D (mm/s RMS).</summary>
public static class Severity
{
    public static double[] Limits(MachineClass c) => c switch
    {
        MachineClass.Iso10816_1_ClassI => new[] { 0.71, 1.8, 4.5 },
        MachineClass.Iso10816_1_ClassII => new[] { 1.12, 2.8, 7.1 },
        MachineClass.Iso10816_1_ClassIII => new[] { 1.8, 4.5, 11.2 },
        MachineClass.Iso10816_1_ClassIV => new[] { 2.8, 7.1, 18.0 },
        MachineClass.Iso10816_3_Group2Rigid => new[] { 1.4, 2.8, 4.5 },
        MachineClass.Iso10816_3_Group2Flexible => new[] { 2.3, 4.5, 7.1 },
        MachineClass.Iso10816_3_Group1Rigid => new[] { 2.3, 4.5, 7.1 },
        MachineClass.Iso10816_3_Group1Flexible => new[] { 3.5, 7.1, 11.0 },
        _ => new[] { 1.4, 2.8, 4.5 },
    };

    public static string Name(MachineClass c) => c switch
    {
        MachineClass.Iso10816_1_ClassI => "ISO 20816-1 (ex 10816-1) · Clase I (hasta 15 kW)",
        MachineClass.Iso10816_1_ClassII => "ISO 20816-1 (ex 10816-1) · Clase II (15–75 kW)",
        MachineClass.Iso10816_1_ClassIII => "ISO 20816-1 (ex 10816-1) · Clase III (grandes, base rígida)",
        MachineClass.Iso10816_1_ClassIV => "ISO 20816-1 (ex 10816-1) · Clase IV (grandes, base flexible)",
        MachineClass.Iso10816_3_Group2Rigid => "ISO 20816-3 (ex 10816-3) · Grupo 2 (15–300 kW), base rígida",
        MachineClass.Iso10816_3_Group2Flexible => "ISO 20816-3 (ex 10816-3) · Grupo 2 (15–300 kW), base flexible",
        MachineClass.Iso10816_3_Group1Rigid => "ISO 20816-3 (ex 10816-3) · Grupo 1 (300 kW–50 MW), base rígida",
        MachineClass.Iso10816_3_Group1Flexible => "ISO 20816-3 (ex 10816-3) · Grupo 1 (300 kW–50 MW), base flexible",
        _ => c.ToString(),
    };

    public static SeverityResult Evaluate(double velocityRmsMmS, MachineClass machineClass)
    {
        var l = Limits(machineClass);
        var zone = velocityRmsMmS < l[0] ? SeverityZone.A : velocityRmsMmS < l[1] ? SeverityZone.B : velocityRmsMmS < l[2] ? SeverityZone.C : SeverityZone.D;
        var (label, desc) = zone switch
        {
            SeverityZone.A => ("Zona A · Buena", "Vibración típica de máquinas nuevas o recién reparadas."),
            SeverityZone.B => ("Zona B · Satisfactoria", "Aceptable para operación continua sin restricciones."),
            SeverityZone.C => ("Zona C · Insatisfactoria", "No apta para operación continua prolongada: planificar la corrección."),
            _ => ("Zona D · Inaceptable", "Vibración suficiente para dañar la máquina: intervenir cuanto antes."),
        };
        return new SeverityResult(velocityRmsMmS, zone, label, desc, l, Name(machineClass));
    }
}
