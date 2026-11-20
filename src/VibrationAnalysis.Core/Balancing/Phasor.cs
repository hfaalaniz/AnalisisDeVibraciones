using System.Numerics;
using VibrationAnalysis.Core.Dsp;

namespace VibrationAnalysis.Core.Balancing;

/// <summary>
/// Vector (fasor) de amplitud y ángulo en grados. Se usa tanto para lecturas de vibración (amplitud ∠ fase)
/// como para masas (gramos ∠ posición angular).
/// </summary>
public readonly record struct Phasor(double Magnitude, double AngleDeg)
{
    public Complex ToComplex() => Complex.FromPolarCoordinates(Magnitude, AngleDeg * Math.PI / 180);

    public static Phasor FromComplex(Complex c) => new(c.Magnitude, Angles.Normalize(c.Phase * 180 / Math.PI));

    public static Phasor operator +(Phasor a, Phasor b) => FromComplex(a.ToComplex() + b.ToComplex());
    public static Phasor operator -(Phasor a, Phasor b) => FromComplex(a.ToComplex() - b.ToComplex());

    public override string ToString() => $"{Magnitude:0.###} ∠ {AngleDeg:0.#}°";
}
