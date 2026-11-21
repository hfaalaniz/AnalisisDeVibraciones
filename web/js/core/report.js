// Informe técnico profesional en HTML autocontenido (A4, gráficos SVG). Mismo formato que el informe de la app de escritorio.
import { effectiveMass, permissibleUnbalance, MATERIALS } from './balance.js';
import { bearingOrders } from './analysis.js';
import { renderSvgChart } from './svgchart.js';
import { WINDOWS } from './dsp.js';
import { FEATURES } from './trend.js';

export const PRODUCT_VERSION = '3.1.0';
const E = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const F = (v, d = 2) => (Number.isFinite(v) ? v.toLocaleString('es-AR', { maximumFractionDigits: d, minimumFractionDigits: 0 }) : '—');
const P = (p) => (p ? `${F(p.mag, 2)} ∠ ${F(p.ang, 0)}°` : '—');
const LEVEL = ['<span class="lvl0">Normal</span>', '<span class="lvl1">Alerta</span>', '<span class="lvl2">Peligro</span>'];
const row = (k, v) => `<tr><th style="width:30%">${k}</th><td>${v}</td></tr>`;
const featureName = (k) => FEATURES[k] || k;

/** Estado general: 0 aceptable, 1 alerta, 2 peligro (peor de severidad, alarmas, tendencias y diagnóstico). */
export function reportStatus({ severity = null, alarms = [], trends = [], findings = [] }) {
  let st = 0;
  if (severity) st = Math.max(st, severity.zone === 'D' ? 2 : severity.zone === 'C' ? 1 : 0);
  for (const a of alarms) st = Math.max(st, a.level || 0);
  for (const t of trends) for (const a of t.alarms || []) st = Math.max(st, a.level || 0);
  if (findings.some((f) => f.confidence >= 0.6)) st = Math.max(st, 1);
  return st;
}

export function recommendationList({ recommendations = '', findings = [], alarms = [], trends = [] }) {
  const own = String(recommendations).split('\n').map((l) => l.trim().replace(/^[-•*]+/, '').trim()).filter(Boolean);
  if (own.length) return own;
  const auto = [...new Set(findings.slice().sort((a, b) => b.confidence - a.confidence).map((f) => f.recommendation).filter(Boolean))].slice(0, 4);
  for (const a of [...alarms, ...trends.flatMap((t) => t.alarms || [])].filter((q) => q.level === 2).slice(0, 2))
    auto.push(`Atender ${featureName(a.feature)} en nivel de peligro.`);
  if (!auto.length) auto.push('Continuar con el plan de monitoreo periódico.');
  return auto;
}

/** Gráfico de tendencia con límites y pronóstico. points: [{ time, value }]. */
export function trendSvg({ title = '', points, alert, danger, forecast = null }) {
  const day = (t) => new Date(t).toLocaleDateString('es-AR', { day: '2-digit', month: '2-digit', year: '2-digit' });
  const series = [{ x: points.map((p) => p.time), y: points.map((p) => p.value), color: '#0e7c86', label: 'Medición', points: true }];
  if (forecast?.x?.length) series.push({ x: forecast.x, y: forecast.y, color: '#7a5af8', label: 'Pronóstico' });
  const hlines = [];
  if (Number.isFinite(alert)) hlines.push({ y: alert, label: 'Alerta', color: '#d28a00' });
  if (Number.isFinite(danger)) hlines.push({ y: danger, label: 'Peligro', color: '#c62828' });
  return renderSvgChart({ title, series, hlines, xFormat: day, height: 230 });
}

/**
 * Datos: { machine, severity, findings, session, report: { tech, company, client, number, workOrder, notes, recommendations, logo, signatures },
 *          spectrum, waveform: { t, y, unit }, spectrumUnit, markers, alarms, indicators: { features }, trends: [{ title, points, alert, danger, alarms, forecast, forecastText }],
 *          figures: [{ title, svg | png, caption }], chartImages: [dataUri] (compatibilidad) }
 * También acepta technician/company/notes sueltos (versión anterior).
 */
export function buildReport(d) {
  const m = d.machine;
  const r = { tech: d.technician ?? '', company: d.company ?? '', notes: d.notes ?? '', ...(d.report || {}) };
  const findings = d.findings || [], alarms = d.alarms || [], trends = d.trends || [], sev = d.severity || null;
  const mass = m.massKg != null || m.diameterMm != null ? effectiveMass(m) : NaN;
  const status = reportStatus({ severity: sev, alarms, trends, findings });

  let h = `<!DOCTYPE html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Informe de vibraciones – ${E(m.name)}</title><style>`;
  h += "@page{size:A4;margin:14mm 14mm 16mm}*{box-sizing:border-box}body{font:13px/1.45 system-ui,'Segoe UI',Arial,sans-serif;color:#1d2327;max-width:920px;margin:20px auto;padding:0 18px}";
  h += 'header{display:flex;align-items:center;gap:18px;border-bottom:3px solid #0e7c86;padding-bottom:10px}header img{max-height:64px;max-width:200px}header .t{flex:1}h1{font-size:21px;margin:0}';
  h += '.meta{color:#5f6b70;font-size:12px;margin-top:3px}h2{font-size:15px;color:#0e7c86;border-bottom:1px solid #cfd8dc;padding-bottom:3px;margin:22px 0 8px;break-after:avoid}';
  h += '.tw{overflow-x:auto}table{border-collapse:collapse;width:100%;margin:6px 0}td,th{border:1px solid #cfd8dc;padding:4px 7px;text-align:left;vertical-align:top}th{background:#eef4f5;font-weight:600}tr{break-inside:avoid}';
  h += '.z{display:inline-block;padding:2px 10px;border-radius:12px;font-weight:600}.zA{background:#d6f5df}.zB{background:#e8f5c8}.zC{background:#ffe8b3}.zD{background:#ffd0cc}';
  h += '.st{display:flex;align-items:center;gap:14px;flex-wrap:wrap;padding:10px 14px;border-radius:8px;margin:10px 0}.st b{font-size:17px}.s0{background:#e3f6e8;border-left:6px solid #1e8c46}.s1{background:#fff3d6;border-left:6px solid #d28a00}.s2{background:#ffe1de;border-left:6px solid #c62828}';
  h += '.lvl0{color:#1e8c46}.lvl1{color:#b26f00;font-weight:600}.lvl2{color:#c62828;font-weight:700}.muted{color:#5f6b70}.big{font-size:19px;font-weight:700}';
  h += 'figure{margin:8px 0 14px;break-inside:avoid}figure img{width:100%;border:1px solid #e3e9eb}figcaption{color:#5f6b70;font-size:11.5px;margin-top:2px}';
  h += '.sign{display:flex;gap:40px;margin-top:46px;break-inside:avoid}.sign div{flex:1;border-top:1px solid #1d2327;padding-top:4px;text-align:center;font-size:12px}';
  h += 'footer{margin-top:28px;border-top:1px solid #cfd8dc;padding-top:6px;color:#5f6b70;font-size:11px}@media print{body{margin:0;max-width:none}a{color:inherit;text-decoration:none}}';
  h += '</style></head><body><header>';
  if (/^data:image\//i.test(r.logo || '')) h += `<img src="${E(r.logo)}" alt="Logo">`;
  const meta = [];
  if (r.company) meta.push(E(r.company));
  if (r.client) meta.push('Cliente: ' + E(r.client));
  if (r.number) meta.push('Informe N.º ' + E(r.number));
  if (r.workOrder) meta.push('OT ' + E(r.workOrder));
  meta.push('Fecha: ' + new Date(d.date ?? Date.now()).toLocaleString('es-AR', { dateStyle: 'short', timeStyle: 'short' }));
  if (r.tech) meta.push('Técnico: ' + E(r.tech));
  h += `<div class="t"><h1>Informe de análisis de vibraciones</h1><div class="meta">${meta.join(' · ')}</div></div></header>`;

  // Resumen
  const stText = ['ACEPTABLE: sin acciones urgentes', 'ALERTA: planificar acciones', 'PELIGRO: intervenir a la brevedad'][status];
  h += `<h2>Resumen</h2><div class="st s${status}"><b>${stText}</b><span>${E(m.name)}${m.location ? ' · ' + E(m.location) : ''}</span></div>`;
  const sum = [];
  if (sev) sum.push(`Vibración global ${F(sev.vRms, 2)} mm/s RMS (${E(sev.label)}).`);
  const top = findings.slice().sort((a, b) => b.confidence - a.confidence)[0];
  if (top) sum.push(`Falla más probable: ${E(top.title)} (${Math.round(top.confidence * 100)} %).`);
  const nAl = alarms.filter((a) => a.level > 0).length + trends.reduce((s, t) => s + (t.alarms || []).filter((a) => a.level > 0).length, 0);
  if (nAl) sum.push(`${nAl} indicador(es) en alarma.`);
  if (sum.length) h += `<p>${sum.join(' ')}</p>`;
  h += `<p><b>Recomendaciones</b></p><ol>${recommendationList({ recommendations: r.recommendations, findings, alarms, trends }).map((x) => `<li>${E(x)}</li>`).join('')}</ol>`;

  // Máquina
  h += '<h2>Máquina</h2><div class="tw"><table>';
  h += row('Equipo', E(m.name) + (m.location ? ' · ' + E(m.location) : ''));
  h += row('Velocidad', `${F(m.rpm, 0)} rpm (${F(m.rpm / 60, 1)} Hz)`);
  if (m.diameterMm != null) h += row('Rotor', `${E(m.material)}, Ø ${F(m.diameterMm, 0)} mm × ${F(m.lengthMm, 0)} mm, ${F(mass, 1)} kg${m.blades > 0 ? `, ${m.blades} álabes` : ''}`);
  if (m.bearing?.balls > 0) {
    const b = bearingOrders(m.bearing);
    h += row('Rodamiento', `${m.bearing.balls} elementos, bola ${F(m.bearing.ballD)} mm, primitivo ${F(m.bearing.pitchD)} mm · BPFO ${F(b.bpfo)}X, BPFI ${F(b.bpfi)}X`);
  }
  if (m.correctionRadiusMm) h += row('Radio de corrección', `${F(m.correctionRadiusMm, 0)} mm`);
  if (sev) h += row('Criterio de severidad', E(sev.standard));
  h += '</table></div>';

  if (sev) {
    h += `<h2>Severidad de la vibración</h2><p><span class="big">${F(sev.vRms, 2)} mm/s RMS</span> &nbsp; <span class="z z${sev.zone}">${E(sev.label)}</span></p><p>${E(sev.description)}</p>`;
    h += `<p class="muted">Límites A/B, B/C, C/D: ${sev.limits.map((v) => F(v)).join(' / ')} mm/s RMS.</p>`;
  }

  // Gráficos
  const figs = [...(d.figures || [])];
  if (d.waveform?.y?.length) figs.unshift({ title: 'Forma de onda', svg: renderSvgChart({ title: 'Forma de onda', xLabel: 'Tiempo (s)', yLabel: d.waveform.unit || '', series: [{ x: d.waveform.t, y: d.waveform.y }], height: 200 }) });
  if (d.spectrum) {
    const sp = d.spectrum, fmax = d.spectrumMaxHz || sp.freqs[sp.freqs.length - 1];
    figs.unshift({
      title: 'Espectro', caption: `Resolución ${F(sp.resolution, 3)} Hz · ventana ${E(WINDOWS[sp.window]?.name || sp.window)}`,
      svg: renderSvgChart({ title: 'Espectro', xLabel: 'Frecuencia (Hz)', yLabel: d.spectrumUnit || 'mm/s pico', xMax: fmax, series: [{ x: Array.from(sp.freqs), y: Array.from(sp.amps) }], markers: (d.markers || []).map((q) => ({ x: q.f, label: q.label, color: '#c2410c' })) }),
    });
  }
  for (const src of d.chartImages || []) figs.push({ title: '', png: src });
  if (figs.length) {
    h += '<h2>Gráficos</h2>';
    for (const f of figs) {
      h += '<figure>' + (f.svg || (/^data:image\//i.test(f.png || '') ? `<img src="${E(f.png)}" alt="${E(f.title)}">` : ''));
      if (f.title || f.caption) h += `<figcaption>${E(f.title)}${f.caption ? ' — ' + E(f.caption) : ''}</figcaption>`;
      h += '</figure>';
    }
  }

  h += '<h2>Diagnóstico</h2>';
  h += findings.length
    ? `<div class="tw"><table><tr><th>Falla probable</th><th>Confianza</th><th>Evidencia</th><th>Recomendación</th></tr>${findings.map((f) => `<tr><td><b>${E(f.title)}</b></td><td>${Math.round(f.confidence * 100)} %</td><td>${E(f.evidence)}</td><td>${E(f.recommendation)}</td></tr>`).join('')}</table></div>`
    : '<p>No se detectaron patrones de falla significativos en el espectro.</p>';

  if (d.indicators?.features) {
    const byKey = Object.fromEntries(alarms.map((a) => [a.feature, a])), order = Object.keys(FEATURES);
    const keys = Object.keys(d.indicators.features).filter((k) => Number.isFinite(d.indicators.features[k]))
      .sort((a, b) => (order.indexOf(a) + 1 || 999) - (order.indexOf(b) + 1 || 999));
    h += '<h2>Indicadores de la medición</h2><div class="tw"><table><tr><th>Indicador</th><th>Valor</th><th>Alerta</th><th>Peligro</th><th>Estado</th></tr>';
    for (const k of keys) {
      const a = byKey[k];
      h += `<tr><td>${E(featureName(k))}</td><td>${F(d.indicators.features[k], 3)}</td><td>${a ? F(a.alert, 3) : ''}</td><td>${a ? F(a.danger, 3) : ''}</td><td>${a ? LEVEL[a.level] : ''}</td></tr>`;
    }
    h += '</table></div>';
  }

  for (const t of trends) {
    const svg = t.svg || trendSvg(t);
    h += `<h2>Tendencia · ${E(t.point || t.title)}</h2><figure>${svg}<figcaption>${E(t.title)}${t.forecastText ? ' — ' + E(t.forecastText) : ''}</figcaption></figure>`;
    const on = (t.alarms || []).filter((a) => a.level > 0);
    if (on.length) {
      h += '<div class="tw"><table><tr><th>Indicador en alarma (última medición)</th><th>Valor</th><th>Límite</th><th>Estado</th></tr>';
      h += on.map((a) => `<tr><td>${E(featureName(a.feature))}</td><td>${F(a.value, 3)}</td><td>${F(a.level === 2 ? a.danger : a.alert, 3)}</td><td>${LEVEL[a.level]}</td></tr>`).join('');
      h += '</table></div>';
    }
  }

  const session = d.session;
  if (session && session.runs.length) {
    const two = session.planes === 2;
    h += `<h2>Balanceo en ${session.planes} plano${two ? 's' : ''}</h2><p class="muted">Ángulos medidos ${session.againstRotation ? 'en sentido contrario al giro' : 'en el sentido de giro'} desde la marca de referencia. Lecturas 1X: amplitud pico ∠ fase.</p>`;
    h += `<div class="tw"><table><tr><th>Corrida</th><th>Peso de prueba</th><th>Sensor 1</th>${two ? '<th>Sensor 2</th>' : ''}<th>rpm</th></tr>`;
    h += session.runs.map((q) => `<tr><td>${E(q.name)}</td><td>${q.trial ? `${F(q.trial.mag)} g ∠ ${F(q.trial.ang, 0)}° (plano ${q.plane})` : '—'}</td><td>${P(q.s1)}</td>${two ? `<td>${P(q.s2)}</td>` : ''}<td>${F(q.rpm, 0)}</td></tr>`).join('');
    h += '</table></div>';
    if (session.corrections.length) {
      const uper = permissibleUnbalance(m.balanceGrade, mass, m.rpm) / session.planes;
      h += `<div class="tw"><table><tr><th>Plano</th><th>Corrección</th><th>Desbalance admisible (ISO 1940 G${F(m.balanceGrade, 1)})</th></tr>`;
      h += session.corrections.map((w, i) => `<tr><td>${i + 1}</td><td><b>${session.keepTrial ? 'agregar (dejando el de prueba) ' : ''}${F(w.mag)} g ∠ ${F(w.ang, 0)}°</b> a ${F(m.correctionRadiusMm, 0)} mm</td><td>${F(uper, 0)} g·mm (${F(uper / m.correctionRadiusMm)} g a ${F(m.correctionRadiusMm, 0)} mm)</td></tr>`).join('');
      h += '</table></div>';
      const pred = session.predictedResidual();
      if (pred.length) h += `<p>Vibración residual prevista: ${pred.map((p, i) => `sensor ${i + 1}: ${F(p.mag, 2)}`).join(' · ')}.</p>`;
    }
  }

  if (r.notes) h += `<h2>Observaciones</h2><p>${E(r.notes).replace(/\n/g, '<br>')}</p>`;
  if (r.signatures !== false)
    h += `<div class="sign"><div>${r.tech ? E(r.tech) + '<br>' : ''}Técnico responsable</div><div>${r.client ? E(r.client) + '<br>' : ''}Recibido por (cliente)</div></div>`;
  h += `<footer>Generado con Análisis de Vibraciones y Balanceo ${PRODUCT_VERSION} (web) — Fabian Alaniz · ElectroNet · <a href="https://www.developers-soft.com/">www.developers-soft.com</a>. Los diagnósticos automáticos son orientativos y deben ser validados por personal calificado.</footer></body></html>`;
  return h;
}

export { MATERIALS };
