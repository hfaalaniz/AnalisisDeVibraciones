// Tendencias y alarmas: indicadores por medición, límites (absolutos, ISO, línea base, estadísticos), ajuste y pronóstico.
// Equivalente a VibrationAnalysis.Core.Trending (C#). Las fechas son milisegundos (Date.now()).
import { spectrum, statistics, convertUnits, fitSinusoid } from './dsp.js';
import { MACHINE_CLASSES, bearingOrders } from './analysis.js';
import { autoEnvelopeBand, envelope } from './advanced.js';

export const FEATURES = {
  vel_rms: 'Velocidad RMS 10–1000 Hz (mm/s)',
  acc_rms: 'Aceleración RMS (g)',
  acc_pico: 'Aceleración pico (g)',
  desp_pp: 'Desplazamiento pico a pico (µm)',
  factor_cresta: 'Factor de cresta (aceleración)',
  curtosis: 'Curtosis (aceleración)',
  '1x': '1X (mm/s pico)',
  '1x_fase': 'Fase 1X (°)',
  '2x': '2X (mm/s pico)',
  '3x': '3X (mm/s pico)',
  subsincronico: 'Subsincrónico 0,3–0,8X (mm/s pico)',
  hf_rms: 'Alta frecuencia > 1 kHz (g RMS)',
  env_rms: 'Envolvente RMS (g)',
  env_bpfo: 'Envolvente en BPFO (g pico)',
  env_bpfi: 'Envolvente en BPFI (g pico)',
  temperatura: 'Temperatura (°C)',
};

export const ALARM_KINDS = {
  absolute: 'Absoluta',
  ratio: 'Relativa a la línea base (×)',
  statistical: 'Estadística (media + n·σ)',
  iso: 'Zonas ISO 10816',
  delta: 'Línea base + incremento',
};

export const BASELINE_COUNT = 3;
const LOWER_IS_WORSE = new Set(['mcsa_barras_db', 'mcsa_excentricidad_db']);
const uid = () => Math.random().toString(36).slice(2) + Date.now().toString(36);

export const defaultAlarms = () => [
  { feature: 'vel_rms', kind: 'iso', alert: 0, danger: 0, enabled: true },
  { feature: '1x', kind: 'ratio', alert: 2, danger: 4, enabled: true },
  { feature: '2x', kind: 'ratio', alert: 2.5, danger: 5, enabled: true },
  { feature: 'env_rms', kind: 'ratio', alert: 2, danger: 4, enabled: true },
  { feature: 'hf_rms', kind: 'ratio', alert: 2, danger: 4, enabled: true },
  { feature: 'curtosis', kind: 'absolute', alert: 4, danger: 6, enabled: true },
  { feature: 'factor_cresta', kind: 'absolute', alert: 5, danger: 7, enabled: true },
  { feature: 'acc_rms', kind: 'statistical', alert: 3, danger: 6, enabled: true },
  { feature: 'temperatura', kind: 'absolute', alert: 80, danger: 95, enabled: true },
];

/** Indicadores de un canal de vibración. channel = { unit, data }; machine = máquina de la app web. */
export function extractFeatures(rec, chIndex, machine, temperature = NaN) {
  const ch = rec.channels[chIndex], fs = rec.fs;
  const pulses = rec.pulses();
  const rpm = pulses.length >= 2 ? rec.measuredRpm() : (rec.nominalRpm > 0 ? rec.nominalRpm : machine.rpm);
  const f1 = rpm / 60, isAcc = ch.unit === 'g' || ch.unit === 'ms2';
  const vel = convertUnits(ch.data, fs, ch.unit, 'mms', 10);
  const acc = convertUnits(ch.data, fs, ch.unit, 'g', isAcc ? 0 : 2);
  const disp = convertUnits(ch.data, fs, ch.unit, 'um', Math.max(2, f1 * 0.3));
  const f = {};
  const sv = spectrum(vel, fs, 'hann');
  f.vel_rms = sv.bandRms(10, Math.min(1000, fs / 2));
  const sa = statistics(acc);
  f.acc_rms = sa.rms; f.acc_pico = sa.peak; f.factor_cresta = sa.crest; f.curtosis = sa.kurtosis;
  f.desp_pp = statistics(disp).p2p;
  if (f1 > 0) {
    const tol = Math.max(sv.resolution * 1.5, f1 * 0.02);
    if (pulses.length >= 2) { const r = fitSinusoid(vel, fs, f1, pulses[0]); f['1x'] = r.amp; f['1x_fase'] = r.phase; }
    else f['1x'] = sv.peakNear(f1, tol).amp;
    f['2x'] = sv.peakNear(2 * f1, tol).amp;
    f['3x'] = sv.peakNear(3 * f1, tol).amp;
    let sub = 0;
    for (let i = 0; i < sv.freqs.length; i++) if (sv.freqs[i] >= 0.3 * f1 && sv.freqs[i] <= 0.8 * f1) sub = Math.max(sub, sv.amps[i]);
    f.subsincronico = sub;
  }
  if (fs / 2 > 1200) f.hf_rms = spectrum(acc, fs, 'hann').bandRms(1000, fs / 2);
  const bearing = machine.bearing?.balls > 0 ? bearingOrders(machine.bearing) : null;
  try {
    const { low, high } = autoEnvelopeBand(acc, fs, f1, bearing ? bearing.bpfi * f1 : 6 * f1);
    const env = envelope(acc, fs, low, high);
    let m = 0; for (const v of env) m += v; m /= env.length;
    let s = 0; for (const v of env) s += (v - m) * (v - m);
    f.env_rms = Math.sqrt(s / env.length);
    if (bearing && f1 > 0) {
      const es = spectrum(env, fs, 'hann'), tol = Math.max(es.resolution * 1.5, f1 * 0.03);
      f.env_bpfo = es.peakNear(bearing.bpfo * f1, tol).amp;
      f.env_bpfi = es.peakNear(bearing.bpfi * f1, tol).amp;
    }
  } catch { /* señal corta o fs baja: sin envolvente */ }
  if (Number.isFinite(temperature)) f.temperatura = temperature;
  return { id: uid(), time: Date.now(), rpm, fs, features: f, notes: '' };
}

export function baseline(point, history) {
  if (point.baselineIds?.length) {
    const set = new Set(point.baselineIds), sel = history.filter((h) => set.has(h.id));
    if (sel.length) return sel;
  }
  return history.slice().sort((a, b) => a.time - b.time).slice(0, BASELINE_COUNT);
}

export function limits(rule, machineClass, base) {
  const vals = base.map((b) => b.features[rule.feature]).filter(Number.isFinite);
  const mean = () => vals.reduce((s, v) => s + v, 0) / vals.length;
  switch (rule.kind) {
    case 'iso': { const l = (MACHINE_CLASSES[machineClass] || MACHINE_CLASSES.g2r).limits; return { alert: l[1], danger: l[2] }; }
    case 'ratio': { if (!vals.length) return null; const m = mean(); return m <= 0 ? null : { alert: m * rule.alert, danger: m * rule.danger }; }
    case 'statistical': {
      if (vals.length < 2) return null;
      const m = mean();
      let sd = Math.sqrt(vals.reduce((s, v) => s + (v - m) * (v - m), 0) / (vals.length - 1));
      sd = Math.max(sd, Math.abs(m) * 0.05);
      return { alert: m + rule.alert * sd, danger: m + rule.danger * sd };
    }
    case 'delta': { if (!vals.length) return null; const m = mean(); return { alert: m + rule.alert, danger: m + rule.danger }; }
    default: return { alert: rule.alert, danger: rule.danger };
  }
}

const fmt = (v) => (+v.toFixed(3)).toLocaleString('es-AR');

/** Evalúa las alarmas habilitadas de un punto. level: 0 normal, 1 alerta, 2 peligro. */
export function evaluate(record, point, machineClass, history) {
  const base = baseline(point, history), res = [];
  for (const rule of point.alarms.filter((a) => a.enabled)) {
    const v = record.features[rule.feature];
    if (!Number.isFinite(v)) continue;
    const l = limits(rule, machineClass, base);
    if (!l || !Number.isFinite(l.alert)) continue;
    const low = LOWER_IS_WORSE.has(rule.feature);
    const level = low ? (v <= l.danger ? 2 : v <= l.alert ? 1 : 0) : (v >= l.danger ? 2 : v >= l.alert ? 1 : 0);
    const name = FEATURES[rule.feature] || rule.feature;
    const msg = level === 2 ? `PELIGRO · ${name} = ${fmt(v)} (límite ${fmt(l.danger)})`
      : level === 1 ? `ALERTA · ${name} = ${fmt(v)} (límite ${fmt(l.alert)})` : `${name} = ${fmt(v)}`;
    res.push({ feature: rule.feature, value: v, level, alert: l.alert, danger: l.danger, message: msg });
  }
  return res;
}

export const worstLevel = (results) => results.reduce((m, r) => Math.max(m, r.level), 0);

export const series = (history, feature) =>
  history.filter((h) => Number.isFinite(h.features[feature])).sort((a, b) => a.time - b.time).map((h) => ({ time: h.time, value: h.features[feature] }));

function regress(x, y) {
  const n = x.length, mx = x.reduce((s, v) => s + v, 0) / n, my = y.reduce((s, v) => s + v, 0) / n;
  let sxx = 0, sxy = 0, syy = 0;
  for (let i = 0; i < n; i++) { sxx += (x[i] - mx) ** 2; sxy += (x[i] - mx) * (y[i] - my); syy += (y[i] - my) ** 2; }
  const b = sxx > 0 ? sxy / sxx : 0;
  return { slope: b, intercept: my - b * mx, r2: syy > 0 && sxx > 0 ? sxy * sxy / (sxx * syy) : 0 };
}

const DAY = 86400000;

/** Ajuste lineal o exponencial de los últimos puntos (el de mejor R²). Pendiente por día. */
export function fit(points, lastPoints = 8) {
  const s = points.slice(Math.max(0, points.length - lastPoints));
  if (s.length < 3) return null;
  const origin = s[0].time, x = s.map((p) => (p.time - origin) / DAY), y = s.map((p) => p.value);
  if (x[x.length - 1] - x[0] <= 0) return null;
  const lin = regress(x, y);
  let best = { slopePerDay: lin.slope, intercept: lin.intercept, r2: lin.r2, exponential: false, origin };
  if (y.every((v) => v > 0)) {
    const ex = regress(x, y.map(Math.log)), mean = y.reduce((a, b) => a + b, 0) / y.length;
    let ssTot = 0, ssRes = 0;
    for (let i = 0; i < y.length; i++) { ssTot += (y[i] - mean) ** 2; ssRes += (y[i] - Math.exp(ex.intercept + ex.slope * x[i])) ** 2; }
    const r2 = ssTot > 0 ? 1 - ssRes / ssTot : 0;
    if (r2 > lin.r2 + 0.02) best = { slopePerDay: ex.slope, intercept: ex.intercept, r2, exponential: true, origin };
  }
  best.valueAt = (t) => { const d = (t - origin) / DAY; return best.exponential ? Math.exp(best.intercept + best.slopePerDay * d) : best.intercept + best.slopePerDay * d; };
  best.crossing = (value) => {
    if (best.slopePerDay <= 0 || (value <= 0 && best.exponential)) return null;
    const d = best.exponential ? (Math.log(value) - best.intercept) / best.slopePerDay : (value - best.intercept) / best.slopePerDay;
    return !Number.isFinite(d) || d > 365 * 30 ? null : origin + d * DAY;
  };
  return best;
}

// ───────── Base de datos local (navegador) ─────────

export const newPoint = (name = 'Punto 1', direction = 'H') => ({ id: uid(), name, direction, alarms: defaultAlarms(), baselineIds: [] });

export function emptyDb() {
  return { version: 1, points: [newPoint('Lado acople horizontal')], records: {} };
}

export class TrendStore {
  constructor(storage = globalThis.localStorage, key = 'va.tendencias.v1') {
    this.storage = storage; this.key = key;
    try { this.db = JSON.parse(storage?.getItem(key) || 'null') || emptyDb(); } catch { this.db = emptyDb(); }
  }
  save() { try { this.storage?.setItem(this.key, JSON.stringify(this.db)); } catch { /* sin espacio */ } }
  point(id) { return this.db.points.find((p) => p.id === id); }
  history(pointId) { return (this.db.records[pointId] || []).slice().sort((a, b) => a.time - b.time); }
  add(pointId, record) { (this.db.records[pointId] ||= []).push(record); this.save(); return record; }
  remove(pointId, recordId) { this.db.records[pointId] = (this.db.records[pointId] || []).filter((r) => r.id !== recordId); this.save(); }
  addPoint(name, direction) { const p = newPoint(name, direction); this.db.points.push(p); this.save(); return p; }
  removePoint(id) { this.db.points = this.db.points.filter((p) => p.id !== id); delete this.db.records[id]; this.save(); }
  exportJson() { return JSON.stringify(this.db, null, 1); }
  importJson(text) {
    const d = JSON.parse(text);
    if (!Array.isArray(d.points) || typeof d.records !== 'object') throw new Error('Archivo de tendencias no válido.');
    this.db = d; this.save();
  }
  /** CSV con una fila por medición y una columna por indicador. */
  exportCsv(pointId) {
    const h = this.history(pointId), keys = [...new Set(h.flatMap((r) => Object.keys(r.features)))];
    const rows = [['fecha', 'rpm', ...keys].join(';')];
    for (const r of h) rows.push([new Date(r.time).toISOString(), r.rpm.toFixed(1), ...keys.map((k) => Number.isFinite(r.features[k]) ? r.features[k].toFixed(5) : '')].join(';'));
    return rows.join('\n');
  }
}
