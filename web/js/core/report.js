// Informe técnico en HTML autocontenido (mismo contenido que el informe de la app de escritorio).
import { effectiveMass, permissibleUnbalance, MATERIALS } from './balance.js';

const E = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const F = (v, d = 2) => (Number.isFinite(v) ? v.toLocaleString('es-AR', { maximumFractionDigits: d, minimumFractionDigits: 0 }) : '—');
const P = (p) => (p ? `${F(p.mag, 2)} ∠ ${F(p.ang, 0)}°` : '—');

export function buildReport({ machine: m, technician = '', company = '', notes = '', severity = null, findings = [], session = null, chartImages = [] }) {
  const mass = effectiveMass(m);
  const rows = [
    ['Equipo', E(m.name) + (m.location ? ' · ' + E(m.location) : '')],
    ['Velocidad', `${F(m.rpm, 0)} rpm (${F(m.rpm / 60, 1)} Hz)`],
    ['Rotor', `${E(m.material)}, Ø ${F(m.diameterMm, 0)} mm × ${F(m.lengthMm, 0)} mm, ${F(mass, 1)} kg${m.blades > 0 ? `, ${m.blades} álabes` : ''}`],
    ['Radio de corrección', `${F(m.correctionRadiusMm, 0)} mm`],
  ];
  let h = `<!DOCTYPE html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Informe de vibraciones – ${E(m.name)}</title>
<style>body{font:14px/1.45 Inter,system-ui,Segoe UI,Arial,sans-serif;color:#0b0b10;max-width:900px;margin:24px auto;padding:0 20px}h1{font-size:26px;margin:0}h2{font-size:17px;border-bottom:2px solid #00a862;padding-bottom:4px;margin-top:28px}.t{overflow-x:auto}table{border-collapse:collapse;width:100%;margin:8px 0}td,th{border:1px solid #d6dcd9;padding:5px 8px;text-align:left;vertical-align:top}th{background:#eafaf2}.z{display:inline-block;padding:3px 10px;border-radius:12px;font-weight:600}.zA{background:#c9f2db}.zB{background:#e6f3c2}.zC{background:#ffe3a8}.zD{background:#ffc9c2}.m{color:#55605b}.big{font-size:22px;font-weight:700}img{max-width:100%;border:1px solid #d6dcd9;margin:6px 0}@media print{body{margin:0}h2{break-after:avoid}table,img{break-inside:avoid}}</style></head><body>`;
  h += `<h1>Informe de análisis de vibraciones y balanceo</h1><p class="m">${E(company)}${company ? ' · ' : ''}Fecha: ${new Date().toLocaleString('es-AR')}${technician ? ' · Técnico: ' + E(technician) : ''}</p>`;
  h += `<h2>Máquina</h2><div class="t"><table>${rows.map(([k, v]) => `<tr><th style="width:32%">${k}</th><td>${v}</td></tr>`).join('')}</table></div>`;
  if (severity) {
    h += `<h2>Severidad de la vibración</h2><p><span class="big">${F(severity.vRms, 2)} mm/s RMS</span> &nbsp; <span class="z z${severity.zone}">${E(severity.label)}</span></p><p>${E(severity.description)}</p>`;
    h += `<p class="m">${E(severity.standard)} · límites A/B, B/C, C/D: ${severity.limits.map((v) => F(v)).join(' / ')} mm/s RMS.</p>`;
  }
  if (chartImages.length) h += chartImages.map((src) => `<img alt="" src="${src}">`).join('');
  h += '<h2>Diagnóstico</h2>';
  h += findings.length
    ? `<div class="t"><table><tr><th>Falla probable</th><th>Confianza</th><th>Evidencia</th><th>Recomendación</th></tr>${findings.map((f) => `<tr><td><b>${E(f.title)}</b></td><td>${Math.round(f.confidence * 100)} %</td><td>${E(f.evidence)}</td><td>${E(f.recommendation)}</td></tr>`).join('')}</table></div>`
    : '<p>No se detectaron patrones de falla significativos en el espectro.</p>';
  if (session && session.runs.length) {
    const two = session.planes === 2;
    h += `<h2>Balanceo en ${session.planes} plano${two ? 's' : ''}</h2><p class="m">Ángulos medidos ${session.againstRotation ? 'en sentido contrario al giro' : 'en el sentido de giro'} desde la marca de referencia. Lecturas 1X: amplitud pico ∠ fase.</p>`;
    h += `<div class="t"><table><tr><th>Corrida</th><th>Peso de prueba</th><th>Sensor 1</th>${two ? '<th>Sensor 2</th>' : ''}<th>rpm</th></tr>`;
    h += session.runs.map((r) => `<tr><td>${E(r.name)}</td><td>${r.trial ? `${F(r.trial.mag)} g ∠ ${F(r.trial.ang, 0)}° (plano ${r.plane})` : '—'}</td><td>${P(r.s1)}</td>${two ? `<td>${P(r.s2)}</td>` : ''}<td>${F(r.rpm, 0)}</td></tr>`).join('');
    h += '</table></div>';
    if (session.corrections.length) {
      const uper = permissibleUnbalance(m.balanceGrade, mass, m.rpm) / session.planes;
      h += `<div class="t"><table><tr><th>Plano</th><th>Corrección</th><th>Admisible ISO 1940 G${F(m.balanceGrade, 1)}</th></tr>`;
      h += session.corrections.map((w, i) => `<tr><td>${i + 1}</td><td><b>${session.keepTrial ? 'agregar (dejando el de prueba) ' : ''}${F(w.mag)} g ∠ ${F(w.ang, 0)}°</b> a ${F(m.correctionRadiusMm, 0)} mm</td><td>${F(uper, 0)} g·mm (${F(uper / m.correctionRadiusMm)} g a ${F(m.correctionRadiusMm, 0)} mm)</td></tr>`).join('');
      h += '</table></div>';
      const pred = session.predictedResidual();
      if (pred.length) h += `<p>Vibración residual prevista: ${pred.map((p, i) => `sensor ${i + 1}: ${F(p.mag, 2)}`).join(' · ')}.</p>`;
    }
  }
  if (notes) h += `<h2>Observaciones</h2><p>${E(notes).replace(/\n/g, '<br>')}</p>`;
  h += '<p class="m" style="margin-top:32px">Generado con Análisis de Vibraciones y Balanceo — Fabian Alaniz · ElectroNet. Los diagnósticos automáticos son orientativos y deben ser validados por personal calificado.</p></body></html>';
  return h;
}

export { MATERIALS };
