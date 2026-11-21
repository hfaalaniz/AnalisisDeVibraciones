using System.Globalization;
using System.Net;
using System.Text;
using VibrationAnalysis.Core.Analysis;
using VibrationAnalysis.Core.Balancing;
using VibrationAnalysis.Core.Model;
using VibrationAnalysis.Core.Trending;

namespace VibrationAnalysis.Core.Report;

/// <summary>Figura del informe: SVG vectorial o imagen PNG (data URI), con su epígrafe.</summary>
public sealed record ReportFigure(string Title, string Svg = "", string PngDataUri = "", string Caption = "");

/// <summary>Tendencia de un indicador de un punto, para el informe.</summary>
public sealed record ReportTrend(string Point, string Feature, string Svg, string Forecast, IReadOnlyList<AlarmResult> Alarms);

public enum ReportStatus { Acceptable, Alert, Danger }

/// <summary>
/// Informe técnico profesional en HTML autocontenido (A4, listo para imprimir o convertir a PDF):
/// encabezado con logo y datos del trabajo, resumen ejecutivo con estado general, máquina, severidad, gráficos vectoriales,
/// diagnóstico, indicadores, alarmas y tendencias, MCSA, balanceo, recomendaciones, observaciones y firmas.
/// Cada bloque aparece solo si tiene datos.
/// </summary>
public sealed class HtmlReport
{
    /// <summary>Versión que figura al pie del informe (la app la completa con la del ejecutable).</summary>
    public static string ProductVersion { get; set; } = "";

    public required Machine Machine { get; init; }
    public string Technician { get; init; } = "";
    public string Company { get; init; } = "";
    public string Client { get; init; } = "";
    public string ReportNumber { get; init; } = "";
    public string WorkOrder { get; init; } = "";
    /// <summary>Logo como data URI (data:image/png;base64,…). Vacío = sin logo.</summary>
    public string LogoDataUri { get; init; } = "";
    public DateTime Date { get; init; } = DateTime.Now;
    public SeverityResult? Severity { get; init; }
    public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();
    public BalancingSession? Balancing { get; init; }
    public double Overall1X { get; init; }
    public string Notes { get; init; } = "";
    /// <summary>Recomendaciones escritas por el técnico (una por línea). Si está vacío se proponen desde el diagnóstico.</summary>
    public string Recommendations { get; init; } = "";
    public IReadOnlyList<ReportFigure> Figures { get; init; } = Array.Empty<ReportFigure>();
    /// <summary>Indicadores de la medición actual (nombre → valor).</summary>
    public MeasurementRecord? Indicators { get; init; }
    public IReadOnlyList<AlarmResult> Alarms { get; init; } = Array.Empty<AlarmResult>();
    public IReadOnlyList<ReportTrend> Trends { get; init; } = Array.Empty<ReportTrend>();
    public McsaResult? Mcsa { get; init; }
    public bool Signatures { get; init; } = true;

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");
    private static string E(string s) => WebUtility.HtmlEncode(s ?? "");
    private static string F(double v, string fmt = "0.##") => double.IsFinite(v) ? v.ToString(fmt, Es) : "—";

    /// <summary>Estado general: el peor entre severidad, alarmas, diagnóstico y MCSA.</summary>
    public ReportStatus Status
    {
        get
        {
            var st = ReportStatus.Acceptable;
            void Up(ReportStatus s) { if (s > st) st = s; }
            if (Severity != null) Up(Severity.Zone >= SeverityZone.D ? ReportStatus.Danger : Severity.Zone == SeverityZone.C ? ReportStatus.Alert : ReportStatus.Acceptable);
            foreach (var a in Alarms) Up(a.Level == AlarmLevel.Danger ? ReportStatus.Danger : a.Level == AlarmLevel.Alert ? ReportStatus.Alert : ReportStatus.Acceptable);
            foreach (var t in Trends) foreach (var a in t.Alarms) Up(a.Level == AlarmLevel.Danger ? ReportStatus.Danger : a.Level == AlarmLevel.Alert ? ReportStatus.Alert : ReportStatus.Acceptable);
            if (Findings.Any(f => f.Confidence >= 0.6)) Up(ReportStatus.Alert);
            if (Mcsa != null) Up(Mcsa.RotorSeverity >= 4 ? ReportStatus.Danger : Mcsa.RotorSeverity == 3 ? ReportStatus.Alert : ReportStatus.Acceptable);
            return st;
        }
    }

    /// <summary>Recomendaciones del informe: las del técnico o, si no hay, las del diagnóstico automático.</summary>
    public IReadOnlyList<string> RecommendationList()
    {
        var own = (Recommendations ?? "").Split('\n').Select(l => l.Trim().TrimStart('-', '•', '*').Trim()).Where(l => l.Length > 0).ToList();
        if (own.Count > 0) return own;
        var auto = Findings.OrderByDescending(f => f.Confidence).Select(f => f.Recommendation).Where(r => r.Length > 0).Distinct().Take(4).ToList();
        if (Mcsa != null && Mcsa.RotorSeverity >= 3) auto.Add("Rotor: " + Mcsa.RotorCondition);
        foreach (var a in Alarms.Concat(Trends.SelectMany(t => t.Alarms)).Where(a => a.Level == AlarmLevel.Danger).Take(2))
            auto.Add($"Atender {FeatureKeys.Name(a.Feature)} en nivel de peligro.");
        if (auto.Count == 0) auto.Add("Continuar con el plan de monitoreo periódico.");
        return auto;
    }

    public string Build()
    {
        var m = Machine;
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>Informe de vibraciones – ").Append(E(m.Name)).Append("</title><style>");
        sb.Append("@page{size:A4;margin:14mm 14mm 16mm}*{box-sizing:border-box}body{font:13px/1.45 system-ui,'Segoe UI',Arial,sans-serif;color:#1d2327;max-width:920px;margin:20px auto;padding:0 18px}");
        sb.Append("header{display:flex;align-items:center;gap:18px;border-bottom:3px solid #0e7c86;padding-bottom:10px}header img{max-height:64px;max-width:200px}header .t{flex:1}h1{font-size:21px;margin:0}");
        sb.Append(".meta{color:#5f6b70;font-size:12px;margin-top:3px}h2{font-size:15px;color:#0e7c86;border-bottom:1px solid #cfd8dc;padding-bottom:3px;margin:22px 0 8px;break-after:avoid}");
        sb.Append("table{border-collapse:collapse;width:100%;margin:6px 0}td,th{border:1px solid #cfd8dc;padding:4px 7px;text-align:left;vertical-align:top}th{background:#eef4f5;font-weight:600}tr{break-inside:avoid}");
        sb.Append(".z{display:inline-block;padding:2px 10px;border-radius:12px;font-weight:600}.zA{background:#d6f5df}.zB{background:#e8f5c8}.zC{background:#ffe8b3}.zD{background:#ffd0cc}");
        sb.Append(".st{display:flex;align-items:center;gap:14px;padding:10px 14px;border-radius:8px;margin:10px 0}.st b{font-size:17px}.s0{background:#e3f6e8;border-left:6px solid #1e8c46}.s1{background:#fff3d6;border-left:6px solid #d28a00}.s2{background:#ffe1de;border-left:6px solid #c62828}");
        sb.Append(".lvl0{color:#1e8c46}.lvl1{color:#b26f00;font-weight:600}.lvl2{color:#c62828;font-weight:700}.muted{color:#5f6b70}.big{font-size:19px;font-weight:700}");
        sb.Append("figure{margin:8px 0 14px;break-inside:avoid}figure img{width:100%;border:1px solid #e3e9eb}figcaption{color:#5f6b70;font-size:11.5px;margin-top:2px}");
        sb.Append(".sign{display:flex;gap:40px;margin-top:46px;break-inside:avoid}.sign div{flex:1;border-top:1px solid #1d2327;padding-top:4px;text-align:center;font-size:12px}");
        sb.Append("footer{margin-top:28px;border-top:1px solid #cfd8dc;padding-top:6px;color:#5f6b70;font-size:11px}@media print{body{margin:0;max-width:none}a{color:inherit;text-decoration:none}}");
        sb.Append("</style></head><body>");

        // Encabezado
        sb.Append("<header>");
        if (LogoDataUri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) sb.Append($"<img src=\"{E(LogoDataUri)}\" alt=\"Logo\">");
        sb.Append("<div class=\"t\"><h1>Informe de análisis de vibraciones</h1><div class=\"meta\">");
        var meta = new List<string>();
        if (Company.Length > 0) meta.Add(E(Company));
        if (Client.Length > 0) meta.Add("Cliente: " + E(Client));
        if (ReportNumber.Length > 0) meta.Add("Informe N.º " + E(ReportNumber));
        if (WorkOrder.Length > 0) meta.Add("OT " + E(WorkOrder));
        meta.Add("Fecha: " + Date.ToString("dd/MM/yyyy HH:mm", Es));
        if (Technician.Length > 0) meta.Add("Técnico: " + E(Technician));
        sb.Append(string.Join(" · ", meta)).Append("</div></div></header>");

        // Resumen ejecutivo
        var status = Status;
        string stText = status switch { ReportStatus.Danger => "PELIGRO: intervenir a la brevedad", ReportStatus.Alert => "ALERTA: planificar acciones", _ => "ACEPTABLE: sin acciones urgentes" };
        sb.Append("<h2>Resumen</h2>");
        sb.Append($"<div class=\"st s{(int)status}\"><b>{stText}</b><span>{E(m.Name)}{(m.Location.Length > 0 ? " · " + E(m.Location) : "")}</span></div>");
        var summary = new List<string>();
        if (Severity != null) summary.Add($"Vibración global {F(Severity.VelocityRmsMmS, "0.00")} mm/s RMS ({E(Severity.Label)}).");
        var top = Findings.OrderByDescending(f => f.Confidence).FirstOrDefault();
        if (top != null) summary.Add($"Falla más probable: {E(top.Title)} ({F(top.Confidence * 100, "0")} %).");
        if (Mcsa != null && Mcsa.RotorSeverity >= 0) summary.Add($"Rotor (MCSA): {E(Mcsa.RotorCondition)}");
        int nAl = Alarms.Count(a => a.Level != AlarmLevel.Normal) + Trends.Sum(t => t.Alarms.Count(a => a.Level != AlarmLevel.Normal));
        if (nAl > 0) summary.Add($"{nAl} indicador(es) en alarma.");
        if (summary.Count > 0) sb.Append("<p>").Append(string.Join(" ", summary)).Append("</p>");
        sb.Append("<p><b>Recomendaciones</b></p><ol>");
        foreach (var r in RecommendationList()) sb.Append("<li>").Append(E(r)).Append("</li>");
        sb.Append("</ol>");

        // Máquina
        sb.Append("<h2>Máquina</h2><table>");
        Row(sb, "Equipo", E(m.Name) + (m.Location.Length > 0 ? " · " + E(m.Location) : ""));
        Row(sb, "Velocidad", $"{F(m.Rpm, "0")} rpm ({F(m.Rpm / 60, "0.0")} Hz)");
        Row(sb, "Rotor", $"{E(m.Material)}, Ø {F(m.DiameterMm, "0")} mm × {F(m.LengthMm, "0")} mm, {F(m.EffectiveMassKg, "0.0")} kg{(m.Blades > 0 ? $", {m.Blades} álabes" : "")}{(m.GearTeeth > 0 ? $", engranaje de {m.GearTeeth} dientes" : "")}");
        if (m.Bearing != null) Row(sb, "Rodamiento", $"{m.Bearing.Balls} elementos, bola {F(m.Bearing.BallDiameterMm)} mm, primitivo {F(m.Bearing.PitchDiameterMm)} mm · BPFO {F(m.Bearing.BpfoOrder, "0.00")}X, BPFI {F(m.Bearing.BpfiOrder, "0.00")}X");
        Row(sb, "Criterio de severidad", E(Severity?.Standard ?? VibrationAnalysis.Core.Analysis.Severity.Name(m.MachineClass)));
        sb.Append("</table>");

        if (Severity != null)
        {
            sb.Append("<h2>Severidad de la vibración</h2>");
            sb.Append($"<p><span class=\"big\">{F(Severity.VelocityRmsMmS, "0.00")} mm/s RMS</span> &nbsp; <span class=\"z z{Severity.Zone}\">{E(Severity.Label)}</span></p><p>{E(Severity.Description)}</p>");
            sb.Append($"<p class=\"muted\">Límites A/B, B/C, C/D: {F(Severity.Limits[0])} / {F(Severity.Limits[1])} / {F(Severity.Limits[2])} mm/s RMS.</p>");
        }

        if (Figures.Count > 0)
        {
            sb.Append("<h2>Gráficos</h2>");
            foreach (var f in Figures)
            {
                sb.Append("<figure>");
                if (f.Svg.Length > 0) sb.Append(f.Svg);
                else if (f.PngDataUri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) sb.Append($"<img src=\"{E(f.PngDataUri)}\" alt=\"{E(f.Title)}\">");
                if (f.Caption.Length > 0 || f.Title.Length > 0) sb.Append($"<figcaption>{E(f.Title)}{(f.Caption.Length > 0 ? " — " + E(f.Caption) : "")}</figcaption>");
                sb.Append("</figure>");
            }
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

        if (Indicators != null && Indicators.Features.Count > 0)
        {
            var byKey = Alarms.ToDictionary(a => a.Feature, a => a);
            sb.Append("<h2>Indicadores de la medición</h2><table><tr><th>Indicador</th><th>Valor</th><th>Alerta</th><th>Peligro</th><th>Estado</th></tr>");
            foreach (var kv in Indicators.Features.Where(k => double.IsFinite(k.Value)).OrderBy(k => FeatureKeys.Names.Keys.ToList().IndexOf(k.Key) is var i && i < 0 ? 999 : i))
            {
                byKey.TryGetValue(kv.Key, out var a);
                sb.Append($"<tr><td>{E(FeatureKeys.Name(kv.Key))}</td><td>{F(kv.Value, "0.###")}</td><td>{(a != null ? F(a.AlertLimit, "0.###") : "")}</td><td>{(a != null ? F(a.DangerLimit, "0.###") : "")}</td><td>{Level(a?.Level)}</td></tr>");
            }
            sb.Append("</table>");
        }

        foreach (var t in Trends)
        {
            sb.Append($"<h2>Tendencia · {E(t.Point)}</h2><figure>{t.Svg}<figcaption>{E(FeatureKeys.Name(t.Feature))}{(t.Forecast.Length > 0 ? " — " + E(t.Forecast) : "")}</figcaption></figure>");
            var on = t.Alarms.Where(a => a.Level != AlarmLevel.Normal).ToList();
            if (on.Count > 0)
            {
                sb.Append("<table><tr><th>Indicador en alarma (última medición)</th><th>Valor</th><th>Límite</th><th>Estado</th></tr>");
                foreach (var a in on) sb.Append($"<tr><td>{E(FeatureKeys.Name(a.Feature))}</td><td>{F(a.Value, "0.###")}</td><td>{F(a.Level == AlarmLevel.Danger ? a.DangerLimit : a.AlertLimit, "0.###")}</td><td>{Level(a.Level)}</td></tr>");
                sb.Append("</table>");
            }
        }

        if (Mcsa != null)
        {
            sb.Append("<h2>Corriente del motor (MCSA)</h2><table>");
            Row(sb, "Frecuencia de red", $"{F(Mcsa.LineHz, "0.000")} Hz · {F(Mcsa.LineAmplitude, "0.##")} A pico");
            Row(sb, "Velocidad / deslizamiento", double.IsNaN(Mcsa.Slip) ? "—" : $"{F(Mcsa.Rpm, "0")} rpm · s = {F(Mcsa.Slip * 100, "0.00")} %");
            Row(sb, "Barras del rotor", $"banda f(1−2s) a {F(Mcsa.LowerSidebandDb, "0.0")} dB · {E(Mcsa.RotorCondition)}");
            Row(sb, "Excentricidad", $"{F(Mcsa.EccentricityDb, "0.0")} dB bajo la línea");
            Row(sb, "THD de corriente", $"{F(Mcsa.ThdPercent, "0.0")} %");
            sb.Append("</table>");
            if (Mcsa.Notes.Count > 0) sb.Append("<p class=\"muted\">").Append(E(string.Join(" ", Mcsa.Notes))).Append("</p>");
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
                    sb.Append($"<tr><td>{i + 1}</td><td><b>{KeepText(b)}{F(b.Corrections[i].Magnitude)} g ∠ {F(b.Corrections[i].AngleDeg, "0")}°</b> a {F(m.CorrectionRadiusMm, "0")} mm</td><td>{F(perPlane, "0")} g·mm ({F(perPlane / m.CorrectionRadiusMm)} g a {F(m.CorrectionRadiusMm, "0")} mm)</td></tr>");
                }
                sb.Append("</table>");
                var pred = b.PredictedResidual();
                if (pred.Length > 0) sb.Append($"<p>Vibración residual prevista: {string.Join(" · ", pred.Select((p, i) => $"sensor {i + 1}: {F(p.Magnitude, "0.00")}"))}.</p>");
            }
        }

        if (Notes.Length > 0) sb.Append("<h2>Observaciones</h2><p>").Append(E(Notes).Replace("\n", "<br>")).Append("</p>");
        if (Signatures)
            sb.Append($"<div class=\"sign\"><div>{(Technician.Length > 0 ? E(Technician) + "<br>" : "")}Técnico responsable</div><div>{(Client.Length > 0 ? E(Client) + "<br>" : "")}Recibido por (cliente)</div></div>");
        sb.Append($"<footer>Generado con Análisis de Vibraciones y Balanceo {E(ProductVersion)} — Fabian Alaniz · ElectroNet · <a href=\"https://www.developers-soft.com/\">www.developers-soft.com</a>. Los diagnósticos automáticos son orientativos y deben ser validados por personal calificado.</footer>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string Level(AlarmLevel? l) => l switch
    {
        AlarmLevel.Danger => "<span class=\"lvl2\">Peligro</span>",
        AlarmLevel.Alert => "<span class=\"lvl1\">Alerta</span>",
        AlarmLevel.Normal => "<span class=\"lvl0\">Normal</span>",
        _ => "",
    };

    private static string KeepText(BalancingSession b) => b.KeepTrialWeights ? "agregar (dejando el de prueba) " : "";
    private static string P(Phasor p) => $"{F(p.Magnitude, "0.00")} ∠ {F(p.AngleDeg, "0")}°";
    private static void Row(StringBuilder sb, string k, string v) => sb.Append($"<tr><th style=\"width:30%\">{k}</th><td>{v}</td></tr>");

    /// <summary>Imagen (PNG/JPG) a data URI para el logo o una figura.</summary>
    public static string ToDataUri(byte[] image)
    {
        string mime = image.Length > 3 && image[0] == 0xFF && image[1] == 0xD8 ? "image/jpeg" : "image/png";
        return $"data:{mime};base64,{Convert.ToBase64String(image)}";
    }
}
