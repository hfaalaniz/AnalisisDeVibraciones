using VibrationAnalysis.Core.Analysis;

namespace VibrationAnalysis.Core.Model;

public enum RotorType { Solid, Hollow, Impeller }

public static class Materials
{
    public static readonly IReadOnlyDictionary<string, double> Density = new Dictionary<string, double>
    {
        ["Acero"] = 7850,
        ["Acero inoxidable"] = 8000,
        ["Hierro fundido"] = 7200,
        ["Aluminio"] = 2700,
        ["Bronce"] = 8800,
        ["Titanio"] = 4500,
        ["Fibra de carbono"] = 1600,
    };
}

/// <summary>Datos de la máquina y del rotor a balancear.</summary>
public sealed class Machine
{
    public string Name { get; set; } = "Máquina";
    public string Location { get; set; } = "";
    public RotorType RotorType { get; set; } = RotorType.Solid;
    public string Material { get; set; } = "Acero";
    public double DiameterMm { get; set; } = 300;
    public double InnerDiameterMm { get; set; }
    public double LengthMm { get; set; } = 150;
    /// <summary>Masa del rotor (kg). Si es 0 se estima con la geometría.</summary>
    public double MassKg { get; set; }
    public double Rpm { get; set; } = 1500;
    public int Blades { get; set; }
    public bool VerticalShaft { get; set; }
    public double CorrectionRadiusMm { get; set; } = 140;
    public double BalanceGrade { get; set; } = 6.3;
    public MachineClass MachineClass { get; set; } = MachineClass.Iso10816_3_Group2Rigid;
    public BearingGeometry? Bearing { get; set; }
    public double LineFrequencyHz { get; set; } = 50;

    public double Density => Materials.Density.TryGetValue(Material, out var d) ? d : 7850;

    /// <summary>Masa estimada por geometría: cilindro macizo, tubo, o impulsor (≈45 % del cilindro lleno).</summary>
    public double EstimatedMassKg()
    {
        double r = DiameterMm / 2000, ri = InnerDiameterMm / 2000, l = LengthMm / 1000;
        double vol = Math.PI * (r * r - (RotorType == RotorType.Hollow ? ri * ri : 0)) * l;
        if (RotorType == RotorType.Impeller) vol *= 0.45;
        return vol * Density;
    }

    public double EffectiveMassKg => MassKg > 0 ? MassKg : EstimatedMassKg();

    /// <summary>
    /// Recomendación de planos (criterio práctico para rotores rígidos): L/D &lt; 0,5 ⇒ un plano;
    /// 0,5 ≤ L/D ≤ 2 ⇒ un plano solo por debajo de 1000 rpm; L/D &gt; 2 ⇒ dos planos.
    /// </summary>
    public (int Planes, string Reason) RecommendedPlanes()
    {
        if (DiameterMm <= 0) return (2, "Sin diámetro: usar dos planos.");
        double ld = LengthMm / DiameterMm;
        if (ld < 0.5) return (1, $"Rotor tipo disco (L/D = {ld:F2} < 0,5): alcanza con un plano.");
        if (ld <= 2 && Rpm < 1000) return (1, $"L/D = {ld:F2} y {Rpm:F0} rpm < 1000: un plano suele alcanzar; usar dos si queda desbalance de par.");
        return (2, $"L/D = {ld:F2}{(Rpm >= 1000 ? $" a {Rpm:F0} rpm" : "")}: hay desbalance de par, conviene balancear en dos planos.");
    }
}
