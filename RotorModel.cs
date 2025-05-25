using System;

namespace VibrationAnalysis.UI
{
    public class RotorModel
    {
        public string Type { get; set; } // Macizo, Turbina
        public double Diameter { get; set; } // m
        public double Length { get; set; } // m
        public string Material { get; set; } // Acero, Aluminio, etc.
        public int BladesCount { get; set; } // Solo para turbinas
        public string ShaftOrientation { get; set; } // Horizontal, Vertical
        public double Mass { get; set; } // kg

        public double CalculateMass()
        {
            double density = GetMaterialDensity();
            double volume;
            if (Type == "Turbina")
            {
                // Estimación simplificada: volumen reducido por álabes
                volume = Math.PI * Math.Pow(Diameter / 2, 2) * Length * (0.5 + BladesCount * 0.01);
            }
            else // Macizo
            {
                volume = Math.PI * Math.Pow(Diameter / 2, 2) * Length;
            }
            Mass = density * volume;
            return Mass;
        }

        public double GetMaterialDensity()
        {
            return Material switch
            {
                "Acero" => 7850,
                "Aluminio" => 2700,
                "Hierro fundido" => 7200,
                "Titanio" => 4500,
                "Fibra de carbono" => 1600,
                _ => 7850
            };
        }

        public bool RequiresTwoPlanes()
        {
            if (Length / Diameter > 2 || Mass > 100 || BladesCount > 10 || ShaftOrientation == "Vertical")
            {
                return true;
            }
            return false;
        }
    }
}