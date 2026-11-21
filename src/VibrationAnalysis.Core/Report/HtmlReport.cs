using System.Globalization;
using System.Net;
using System.Text;
using VibrationAnalysis.Core.Analysis;
using VibrationAnalysis.Core.Balancing;
using VibrationAnalysis.Core.Model;

namespace VibrationAnalysis.Core.Report;

/// <summary>Informe técnico en HTML autocontenido (se imprime o se guarda como PDF desde el navegador).</summary>
public sealed class HtmlReport
{
    /// <summary>Versión que figura al pie del informe (la app la completa con la del ejecutable).</summary>
    public static string ProductVersion { get; set; } = "";

    public required Machine Machine { get; init; }
    public string Technician { get; init; } = "";
    public string Company { get; init; } = "";
    public SeverityResult? Severity { get; init; }
    public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();
    public BalancingSession? Balancing { get; init; }
    public double Overall1X { get; init; }
    public string Notes { get; init; } = "";

    private static string E(string s) => WebUtility.HtmlEncode(s);
    private static string F(double v, string fmt = "0.##") => v.ToString(fmt, CultureInfo.GetCultureInfo("es-AR"));

    public string Build()
    {
        var m = Machine;
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Informe de vibraciones – ").Append(E(m.Name)).Append("</title>");
        sb.Append("<style>body{font:14px/1.45 system-ui,Segoe UI,Arial,sans-serif;color:#1d2327;max-width:900px;margin:24px auto;padding:0 20px}h1{font-size:24px;margin:0}h2{font-size:17px;border-bottom:2px solid #0e7c86;padding-bottom:4px;margin-top:28px}table{border-collapse:collapse;width:100%;margin:8px 0}td,th{border:1px solid #cfd8dc;padding:5px 8px;text-align:left}th{background:#eef4f5}.z{display:inline-block;padding:3px 10px;border-radius:12px;font-weight:600}.zA{background:#d6f5df}.zB{background:#e8f5c8}.zC{background:#ffe8b3}.zD{background:#ffd0cc}.muted{color:#5f6b70}.big{font-size:20px;font-weight:700}@media print{body{margin:0}}</style></head><body>");
        sb.Append("<h1>Informe de análisis de vibraciones y balanceo</h1>");
        sb.Append($"<p class=\"muted\">{E(Company)}{(Company.Length > 0 ? " · " : "")}Fecha: {DateTime.Now:dd/MM/yyyy HH:mm}{(Technician.Length > 0 ? " · Técnico: " + E(Technician) : "")}</p>");

        sb.Append("<h2>Máquina</h2><table>");
        Row(sb, "Equipo", E(m.Name) + (m.Location.Length > 0 ? " · " + E(m.Location) : ""));
        Row(sb, "Velocidad", $"{F(m.Rpm, "0")} rpm ({F(m.Rpm / 60, "0.0")} Hz)");
        Row(sb, "Rotor", $"{E(m.Material)}, Ø {F(m.DiameterMm, "0")} mm × {F(m.LengthMm, "0")} mm, {F(m.EffectiveMassKg, "0.0")} kg{(m.Blades > 0 ? $", {m.Blades} álabes" : "")}");
        Row(sb, "Radio de corrección", $"{F(m.CorrectionRadiusMm, "0")} mm");
        Row(sb, "Criterio de severidad", E(Severity?.Standard ?? VibrationAnalysis.Core.Analysis.Severity.Name(m.MachineClass)));
        sb.Append("</table>");

        if (Severity != null)
        {
            sb.Append("<h2>Severidad de la vibración</h2>");
            sb.Append($"<p><span class=\"big\">{F(Severity.VelocityRmsMmS, "0.00")} mm/s RMS</span> &nbsp; <span class=\"z z{Severity.Zone}\">{E(Severity.Label)}</span></p><p>{E(Severity.Description)}</p>");
            sb.Append($"<p class=\"muted\">Límites A/B, B/C, C/D: {F(Severity.Limits[0])} / {F(Severity.Limits[1])} / {F(Severity.Limits[2])} mm/s RMS.</p>");
        }

        sb.Append("<h2>Diagnóstico</h2>");
        if (Findings.Count == 0) sb.Append("<p>No se detectaron patrones de falla significativos en el espectro.</p>");
        else
        {
            sb.Append("<table><tr><th>Falla probable</th><th>Confianza</th><th>Evidencia</th><th>Recomendación</th></tr>");
            foreach (var f in Findings)
                sb.Append($"<tr><td><b>{E(f.Title)}</b></td><td>{F(f.Confidence * 100, "0")} %</td><td>{E(f.Evidence)}</td><td>{E(f.Recommendation)}</td></tr>");
            sb.Append("</table>");
        }

        if (Balancing is { } b && b.Runs.Count > 0)
        {
            sb.Append($"<h2>Balanceo en {b.Planes} plano{(b.Planes > 1 ? "s" : "")}</h2>");
            sb.Append($"<p class=\"muted\">Ángulos medidos {(b.AnglesAgainstRotation ? "en sentido contrario al giro" : "en el sentido de giro")} desde la marca de referencia. Lecturas 1X: amplitud pico ∠ fase.</p>");
            sb.Append("<table><tr><th>Corrida</th><th>Peso de prueba</th><th>Sensor 1</th>");
            if (b.Planes == 2) sb.Append("<th>Sensor 2</th>");
            sb.Append("<th>rpm</th></tr>");
            foreach (var r in b.Runs)
            {
                sb.Append($"<tr><td>{E(r.Name)}</td><td>{(r.Trial is { } t ? $"{F(t.Magnitude)} g ∠ {F(t.AngleDeg, "0")}° (plano {r.TrialPlane})" : "—")}</td><td>{P(r.Sensor1)}</td>");
                if (b.Planes == 2) sb.Append($"<td>{(r.Sensor2 is { } s2 ? P(s2) : "—")}</td>");
                sb.Append($"<td>{F(r.Rpm, "0")}</td></tr>");
            }
            sb.Append("</table>");
            if (b.Corrections.Length > 0)
            {
                sb.Append("<table><tr><th>Plano</th><th>Corrección</th><th>Desbalance admisible (ISO 1940 G").Append(F(m.BalanceGrade)).Append(")</th></tr>");
                double uper = BalanceQuality.PermissibleUnbalanceGmm(m.BalanceGrade, m.EffectiveMassKg, m.Rpm);
                for (int i = 0; i < b.Corrections.Length; i++)
                {
                    double perPlane = b.Planes == 2 ? uper / 2 : uper;
                    sb.Append($"<tr><td>{i + 1}</td><td><b>{(KeepText(b))}{F(b.Corrections[i].Magnitude)} g ∠ {F(b.Corrections[i].AngleDeg, "0")}°</b> a {F(m.CorrectionRadiusMm, "0")} mm</td><td>{F(perPlane, "0")} g·mm ({F(perPlane / m.CorrectionRadiusMm)} g a {F(m.CorrectionRadiusMm, "0")} mm)</td></tr>");
                }
                sb.Append("</table>");
                var pred = b.PredictedResidual();
                if (pred.Length > 0) sb.Append($"<p>Vibración residual prevista: {string.Join(" · ", pred.Select((p, i) => $"sensor {i + 1}: {F(p.Magnitude, "0.00")}"))}.</p>");
            }
        }
        if (Notes.Length > 0) sb.Append("<h2>Observaciones</h2><p>").Append(E(Notes)).Append("</p>");
        sb.Append("<p class=\"muted\" style=\"margin-top:32px\">Generado con Análisis de Vibraciones y Balanceo " + ProductVersion + " — Fabian Alaniz · ElectroNet. Los diagnósticos automáticos son orientativos y deben ser validados por personal calificado.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string KeepText(BalancingSession b) => b.KeepTrialWeights ? "agregar (dejando el de prueba) " : "";
    private static string P(Phasor p) => $"{F(p.Magnitude, "0.00")} ∠ {F(p.AngleDeg, "0")}°";
    private static void Row(StringBuilder sb, string k, string v) => sb.Append($"<tr><th style=\"width:32%\">{k}</th><td>{v}</td></tr>");
}
