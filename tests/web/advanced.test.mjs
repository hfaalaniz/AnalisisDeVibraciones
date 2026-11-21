// Pruebas del análisis avanzado, órdenes y tendencias de la versión web, con paridad numérica contra la app de escritorio.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { spectrum } from '../js/core/dsp.js';
import {
  butterworth, averaged, psd, zoom, cepstrum, envelopeSpectrum, spectralKurtosis, autoEnvelopeBand,
  orderSpectrum, track, criticalSpeeds, spectrogram, unwrap,
} from '../js/core/advanced.js';
import { extractFeatures, evaluate, fit, limits, newPoint, TrendStore, worstLevel } from '../js/core/trend.js';
import { RotorSimulator, defaultSimSettings } from '../js/core/io.js';
import { buildReport } from '../js/core/report.js';

const here = dirname(fileURLToPath(import.meta.url));
const vecPath = join(here, 'vectors.json');
const v = existsSync(vecPath) ? JSON.parse(readFileSync(vecPath, 'utf8')) : null;
const skip = !(v && v.advanced) && 'sin vectores avanzados';

/** Error relativo al máximo del vector de referencia. */
function close(actual, ref, tol, what) {
  assert.equal(actual.length >= ref.length, true, `${what}: largo ${actual.length} < ${ref.length}`);
  let mx = 0; for (const r of ref) mx = Math.max(mx, Math.abs(r));
  let err = 0, at = 0;
  for (let i = 0; i < ref.length; i++) { const e = Math.abs(actual[i] - ref[i]); if (e > err) { err = e; at = i; } }
  assert.ok(err <= tol * Math.max(mx, 1e-300), `${what}: error ${err.toExponential(2)} en ${at} (máx ${mx.toExponential(2)})`);
}
// Mismas señales sintéticas que genera la prueba de C# (Vectors.EmitAdvanced)
function advSignal() {
  const fs = 5120, n = 8192, x = new Float64Array(n);
  for (let i = 0; i < n; i++) {
    const t = i / fs;
    x[i] = Math.sin(2 * Math.PI * 25 * t) + 0.3 * Math.sin(2 * Math.PI * 50 * t + 0.4)
      + 0.2 * Math.sin(2 * Math.PI * 1800 * t) * (1 + Math.cos(2 * Math.PI * 87 * t)) + 0.05 * Math.sin(i * 1.37) * Math.cos(i * 0.21);
  }
  return x;
}
function runUpSignal() {
  const fs = 4096, n = 65536, T = n / fs, fa = 10, fb = 50, y = new Float64Array(n), pulses = [];
  for (let i = 0; i < n; i++) {
    const t = i / fs, th = 2 * Math.PI * (fa * t + (fb - fa) * t * t / (2 * T)), hz = fa + (fb - fa) * t / T, r = hz / 30, z = 0.08;
    const g = r * r / Math.sqrt((1 - r * r) ** 2 + (2 * z * r) ** 2), lag = Math.atan2(2 * z * r, 1 - r * r);
    y[i] = g * Math.cos(th - lag) + 0.2 * Math.cos(2 * th + 0.5);
  }
  const a = (fb - fa) / T;
  for (let k = 0; ; k++) { const t = (-fa + Math.sqrt(fa * fa + 2 * a * k)) / a; if (t >= T) break; pulses.push(t); }
  return { fs, y, pulses };
}
const near = (a, b, tol, what) => assert.ok(Math.abs(a - b) <= tol, `${what}: ${a} ≠ ${b} (±${tol})`);

test('Paridad: filtros, promedios, PSD, zoom, cepstrum, envolvente y curtosis espectral', { skip }, () => {
  const a = v.advanced, x = advSignal(), fs = a.fs;
  const bp = butterworth('band', 4, fs, 500, 1500);
  close(bp.filtfilt(x), a.filter.y, 1e-9, 'filtfilt');
  near(bp.gain(1000), a.filter.gain1000, 1e-12, 'ganancia 1 kHz'); near(bp.gain(300), a.filter.gain300, 1e-12, 'ganancia 300 Hz');
  const av = averaged(x, fs, 1024, 0.5, 'hann');
  assert.equal(av.averages, a.avg.Averages); close(av.amps, a.avg.amps, 1e-10, 'promedio lineal');
  close(averaged(x, fs, 1024, 0.5, 'blackmanharris', 'peak').amps, a.peak, 1e-10, 'retención de picos');
  const p = psd(x, fs, 1024, 0.5, 'kaiser');
  close(p.psd, a.psd.v, 1e-10, 'PSD'); near(p.bandRms(10, 1000), a.psd.rms, 1e-10, 'RMS de banda');
  const z = zoom(x, fs, 25, 20);
  close(z.freqs, a.zoom.f, 1e-12, 'zoom f'); close(z.amps, a.zoom.a, 1e-8, 'zoom amplitud');
  close(cepstrum(x, fs).values, a.cep, 1e-9, 'cepstrum');
  close(envelopeSpectrum(x, fs, 1500, 2100).amps, a.env, 1e-8, 'envolvente');
  close(spectralKurtosis(x, fs).kurtosis, a.sk, 1e-9, 'curtosis espectral');
  const b = autoEnvelopeBand(x, fs, 25, 150);
  near(b.low, a.band[0], 1e-9, 'banda baja'); near(b.high, a.band[1], 1e-9, 'banda alta');
});

test('Paridad: espectro de órdenes, Bode y velocidad crítica', { skip }, () => {
  const o = { ...v.orders, ...runUpSignal() };
  assert.equal(o.pulses.length, v.orders.pulses);
  close(orderSpectrum(o.y, o.fs, o.pulses, 10).amps, o.spectrum, 1e-8, 'espectro de órdenes');
  const tr = track(o.y, o.fs, o.pulses, 1, 8);
  assert.equal(tr.points.length, o.track.length);
  close(tr.points.map((p) => p.rpm), o.track.map((p) => p[0]), 1e-10, 'rpm');
  close(tr.points.map((p) => p.amp), o.track.map((p) => p[1]), 1e-8, 'amplitud 1X');
  const crit = criticalSpeeds(tr);
  assert.equal(crit.length, o.critical.length);
  if (crit.length) near(crit[0].rpm, o.critical[0][0], 1e-6, 'velocidad crítica');
});

test('Paridad: ajuste de tendencia y límites estadísticos', { skip }, () => {
  const t = v.trend, DAY = 86400000, t0 = Date.UTC(2026, 0, 1);
  const f = fit(t.days.map((d, i) => ({ time: t0 + d * DAY, value: t.values[i] })));
  assert.equal(f.exponential, t.Exponential);
  near(f.slopePerDay, t.slope, 1e-12, 'pendiente'); near(f.intercept, t.Intercept, 1e-10, 'ordenada'); near(f.r2, t.R2, 1e-10, 'R²');
  near((f.crossing(4.5) - t0) / DAY, t.crossDays, 1e-6, 'cruce');
  const hist = t.stat.map((val, i) => ({ id: `r${i}`, time: t0 + i * DAY, features: { acc_rms: val } }));
  const l = limits({ feature: 'acc_rms', kind: 'statistical', alert: 3, danger: 6 }, 'g2r', hist);
  near(l.alert, t.limits[0], 1e-12, 'alerta'); near(l.danger, t.limits[1], 1e-12, 'peligro');
});

test('Zoom FFT resuelve dos líneas a 0,25 Hz', () => {
  const fs = 1024, n = 65536, x = Float64Array.from({ length: n }, (_, i) => Math.sin(2 * Math.PI * 25 * i / fs) + 0.5 * Math.sin(2 * Math.PI * 25.25 * i / fs));
  const z = zoom(x, fs, 25.1, 2, 'flattop');
  near(z.peakNear(25, 0.05).amp, 1, 0.03, '25 Hz'); near(z.peakNear(25.25, 0.05).amp, 0.5, 0.03, '25,25 Hz');
});

test('Arranque simulado: Bode encuentra la crítica y el espectrograma sigue la rampa', () => {
  const s = { ...defaultSimSettings(), sensors: 1, runUp: true, rpmStart: 600, rpmEnd: 3000, criticalRpm: 1800, fs: 2048, samples: 65536, noise: 0.01 };
  const r = new RotorSimulator(s).acquire(), p = r.pulses();
  assert.ok(p.length > 100);
  const crit = criticalSpeeds(track(r.channels[0].data, r.fs, p, 1, 8));
  assert.ok(crit.length >= 1 && Math.abs(crit[0].rpm - 1800) < 120, `crítica ${crit[0]?.rpm}`);
  const sg = spectrogram(r.channels[0].data, r.fs, 2048, 0.5, 'hann', p, 100);
  assert.ok(sg.rpm[sg.rpm.length - 1] > sg.rpm[0] + 1500);
  assert.deepEqual(Array.from(unwrap([350, 10, 30])), [350, 370, 390]);
});

test('Tendencias: indicadores, alarmas con línea base y almacenamiento', () => {
  const machine = { rpm: 1500, bearing: { balls: 9, ballD: 7.94, pitchD: 39, contactDeg: 0 } };
  const mk = (u) => new RotorSimulator({ ...defaultSimSettings(), sensors: 1, u1: { mag: u, ang: 0 }, u2: { mag: 0, ang: 0 }, bearing: 0.5 }).acquire();
  const store = new TrendStore(null);
  const pt = store.db.points[0];
  for (let i = 0; i < 3; i++) { const r = extractFeatures(mk(500), 0, machine, 40); r.time -= (10 - i) * 86400000; store.add(pt.id, r); }
  const rec = extractFeatures(mk(3000), 0, machine, 41);
  for (const k of ['vel_rms', '1x', '1x_fase', 'env_rms', 'env_bpfo', 'hf_rms', 'temperatura']) assert.ok(Number.isFinite(rec.features[k]), k);
  const res = evaluate(rec, pt, 'g2r', store.history(pt.id));
  assert.equal(res.find((a) => a.feature === '1x').level, 2);
  assert.equal(worstLevel(res), 2);
  store.add(pt.id, rec);
  assert.ok(store.exportCsv(pt.id).split('\n').length === 5);
  const copy = new TrendStore(null); copy.importJson(store.exportJson());
  assert.equal(copy.history(pt.id).length, 4);
  assert.ok(newPoint('X').alarms.length > 5);
});

test('Informe web: estado general, figuras y tendencias', () => {
  const r = new RotorSimulator({ ...defaultSimSettings(), sensors: 1 }).acquire();
  const sp = spectrum(r.channels[0].data, r.fs, 'hann');
  const html = buildReport({
    machine: { name: 'VT <02>', location: 'P1', rpm: 1500, machineClass: 'g2r' },
    severity: { vRms: 6, zone: 'D', label: 'Zona D', description: 'x', limits: [1.4, 2.8, 4.5], standard: 'ISO' },
    findings: [{ title: 'Desbalance', confidence: 0.9, evidence: 'e', recommendation: 'Balancear' }],
    report: { tech: 'F', company: 'E', client: 'C', number: 'VA-2026-001', notes: '' },
    spectrum: sp, alarms: [{ feature: '1x', value: 3, level: 1, alert: 2, danger: 4, message: 'ALERTA · 1X' }],
    trends: [{ title: '1X', points: [{ time: 0, value: 1 }, { time: 86400000, value: 2 }], alert: 3, danger: 4 }],
  });
  assert.ok(html.includes('VT &lt;02&gt;'));
  assert.ok(html.includes('PELIGRO'));
  assert.ok(html.includes('<li>Balancear</li>'));
  assert.ok(html.includes('<svg'));
  assert.ok(html.includes('www.developers-soft.com'));
});
