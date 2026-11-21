using VibrationAnalysis.Core.Dsp;

namespace VibrationAnalysis.Core.Analysis;

public sealed record SpectralPeak(double Frequency, double Amplitude, int Index)
{
    /// <summary>Orden respecto de la velocidad de giro (0 si no se indicó).</summary>
    public double Order { get; init; }
}

public sealed record HarmonicLine(double Order, double Frequency, double Amplitude);

public sealed record SidebandLine(int N, double Frequency, double Amplitude);

/// <summary>Extracción de rasgos del espectro: picos, armónicos, bandas laterales y nivel de piso.</summary>
public static class SpectralFeatures
{
    /// <summary>
    /// Picos (máximos locales) ordenados por amplitud, con frecuencia corregida por interpolación parabólica.
    /// </summary>
    public static IReadOnlyList<SpectralPeak> Peaks(Spectrum s, int max = 20, double minFrequency = 0, double minRelative = 0.01, double runningSpeedHz = 0)
    {
        var a = s.Amplitudes;
        double top = 0;
        for (int i = 1; i < a.Length; i++) if (s.Frequencies[i] >= minFrequency) top = Math.Max(top, a[i]);
        var list = new List<SpectralPeak>();
        for (int i = 1; i < a.Length - 1; i++)
        {
            if (s.Frequencies[i] < minFrequency || a[i] < top * minRelative) continue;
            if (a[i] <= a[i - 1] || a[i] < a[i + 1]) continue;
            double den = a[i - 1] - 2 * a[i] + a[i + 1];
            double d = Math.Abs(den) > 1e-300 ? Math.Clamp(0.5 * (a[i - 1] - a[i + 1]) / den, -0.5, 0.5) : 0;
            double f = s.Frequencies[i] + d * (s.Frequencies[i + 1] - s.Frequencies[i]);
            list.Add(new SpectralPeak(f, a[i], i) { Order = runningSpeedHz > 0 ? f / runningSpeedHz : 0 });
        }
        return list.OrderByDescending(p => p.Amplitude).Take(max).ToList();
    }

    /// <summary>Amplitud de los armónicos 1X…nX (o de cualquier frecuencia base).</summary>
    public static IReadOnlyList<HarmonicLine> Harmonics(Spectrum s, double fundamentalHz, int count = 10, double tolHz = 0)
    {
        var res = new List<HarmonicLine>();
        if (fundamentalHz <= 0) return res;
        double tol = tolHz > 0 ? tolHz : Math.Max(s.Resolution * 1.5, fundamentalHz * 0.015);
        for (int k = 1; k <= count; k++)
        {
            double f = k * fundamentalHz;
            if (f > s.Frequencies[^1]) break;
            var p = s.PeakNear(f, tol);
            res.Add(new HarmonicLine(k, p.Frequency, p.Amplitude));
        }
        return res;
    }

    /// <summary>Bandas laterales alrededor de una portadora (±1…±n veces la separación). N negativo = banda inferior.</summary>
    public static IReadOnlyList<SidebandLine> Sidebands(Spectrum s, double carrierHz, double spacingHz, int count = 3, double tolHz = 0)
    {
        var res = new List<SidebandLine>();
        if (spacingHz <= 0) return res;
        double tol = tolHz > 0 ? tolHz : Math.Max(s.Resolution * 1.5, spacingHz * 0.1);
        for (int n = -count; n <= count; n++)
        {
            double f = carrierHz + n * spacingHz;
            if (f <= 0 || f > s.Frequencies[^1]) continue;
            var p = s.PeakNear(f, tol);
            res.Add(new SidebandLine(n, p.Frequency, p.Amplitude));
        }
        return res;
    }

    /// <summary>Piso de ruido local: mediana de las amplitudes en ±halfWidthHz alrededor de f.</summary>
    public static double LocalFloor(Spectrum s, double f, double halfWidthHz)
    {
        var v = new List<double>();
        for (int i = 1; i < s.Frequencies.Length; i++)
            if (Math.Abs(s.Frequencies[i] - f) <= halfWidthHz) v.Add(s.Amplitudes[i]);
        if (v.Count == 0) return 0;
        v.Sort();
        return v[v.Count / 2];
    }

    /// <summary>
    /// Cuenta cuántos armónicos de una frecuencia de defecto sobresalen del piso local (relación señal/piso ≥ <paramref name="snr"/>).
    /// Se usa sobre el espectro de envolvente para confirmar defectos de rodamientos.
    /// </summary>
    public static (int Count, double FirstAmplitude, double FirstSnr) DefectHarmonics(Spectrum s, double defectHz, int harmonics = 4, double snr = 3)
    {
        if (defectHz <= 0) return (0, 0, 0);
        int count = 0; double a1 = 0, snr1 = 0;
        double tol = Math.Max(s.Resolution * 1.5, defectHz * 0.02);
        for (int k = 1; k <= harmonics; k++)
        {
            double f = k * defectHz;
            if (f > s.Frequencies[^1]) break;
            var p = s.PeakNear(f, tol);
            double floor = Math.Max(LocalFloor(s, f, Math.Max(defectHz * 0.4, s.Resolution * 20)), 1e-12);
            double r = p.Amplitude / floor;
            if (k == 1) { a1 = p.Amplitude; snr1 = r; }
            if (r >= snr) count++;
        }
        return (count, a1, snr1);
    }
}

public sealed record FrequencyMarker(string Label, double Frequency, string Group);

/// <summary>Frecuencias características de la máquina para rotular picos y dibujar marcadores.</summary>
public static class FrequencyCatalog
{
    public static IReadOnlyList<FrequencyMarker> Build(double runningSpeedHz, BearingGeometry? bearing = null, int blades = 0,
        int gearTeeth = 0, double lineFrequencyHz = 50, int harmonics = 10)
    {
        var m = new List<FrequencyMarker>();
        double f1 = runningSpeedHz;
        if (f1 <= 0) return m;
        foreach (var o in new[] { 0.5, 1.5, 2.5 }) m.Add(new FrequencyMarker($"{o.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("es-AR"))}X", o * f1, "Subarmónicos"));
        for (int k = 1; k <= harmonics; k++) m.Add(new FrequencyMarker($"{k}X", k * f1, "Giro"));
        if (bearing != null)
        {
            void B(string name, double order, int n)
            {
                for (int k = 1; k <= n; k++) m.Add(new FrequencyMarker(k == 1 ? name : $"{k}×{name}", k * order * f1, "Rodamiento"));
            }
            B("BPFO", bearing.BpfoOrder, 4); B("BPFI", bearing.BpfiOrder, 3); B("BSF", bearing.BsfOrder, 2); B("FTF", bearing.FtfOrder, 2);
        }
        if (blades > 1) { m.Add(new FrequencyMarker($"Álabes ({blades}X)", blades * f1, "Álabes")); m.Add(new FrequencyMarker($"2× álabes", 2 * blades * f1, "Álabes")); }
        if (gearTeeth > 1)
        {
            double g = gearTeeth * f1;
            m.Add(new FrequencyMarker("GMF", g, "Engranaje"));
            for (int k = 1; k <= 2; k++) { m.Add(new FrequencyMarker($"GMF−{k}X", g - k * f1, "Engranaje")); m.Add(new FrequencyMarker($"GMF+{k}X", g + k * f1, "Engranaje")); }
            m.Add(new FrequencyMarker("2×GMF", 2 * g, "Engranaje"));
        }
        if (lineFrequencyHz > 0) { m.Add(new FrequencyMarker("Red", lineFrequencyHz, "Eléctrico")); m.Add(new FrequencyMarker("2× red", 2 * lineFrequencyHz, "Eléctrico")); }
        return m;
    }

    /// <summary>Rótulo de la frecuencia característica más cercana dentro de la tolerancia, o cadena vacía.</summary>
    public static string Identify(double f, IReadOnlyList<FrequencyMarker> markers, double tolHz)
    {
        FrequencyMarker? best = null; double d = double.MaxValue;
        foreach (var mk in markers)
        {
            double e = Math.Abs(mk.Frequency - f);
            if (e <= tolHz && e < d) { d = e; best = mk; }
        }
        return best?.Label ?? "";
    }
}
