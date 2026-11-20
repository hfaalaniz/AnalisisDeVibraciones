// Pruebas del motor web: node --test web/tests/
// Si existe vectors.json (generado por las pruebas de C# con --emit), se verifica que la web calcule lo mismo que la app de escritorio.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { fft, spectrum, fitSinusoid, convertUnits, statistics, G } from '../js/core/dsp.js';
import { evaluateSeverity, diagnose } from '../js/core/analysis.js';
import { singlePlane, twoPlanes, BalancingSession, splitWeight, drillDepthMm, suggestTrialGrams, permissibleUnbalance, padd, psub, C, toC, toP } from '../js/core/balance.js';
import { RotorSimulator, defaultSimSettings, parseCsv, writeCsv, parseWav, SerialStreamParser } from '../js/core/io.js';

const near = (a, b, tol, msg) => assert.ok(Math.abs(a - b) <= tol, `${msg}: ${a} ≠ ${b} (±${tol})`);
const here = dirname(fileURLToPath(import.meta.url));

function naiveDft(x) {
  const n = x.length, re = [], im = [];
  for (let k = 0; k < n; k++) { let r = 0, i = 0; for (let t = 0; t < n; t++) { const a = -2 * Math.PI * k * t / n; r += x[t] * Math.cos(a); i += x[t] * Math.sin(a); } re.push(r); im.push(i); }
  return { re, im };
}

test('FFT radix-2 y Bluestein coinciden con la DFT', () => {
  for (const n of [64, 1000]) {
    const x = Array.from({ length: n }, (_, i) => Math.sin(i * 0.11) + 0.5 * Math.cos(i * 0.73));
    const re = Float64Array.from(x), im = new Float64Array(n);
    fft(re, im);
    const d = naiveDft(x);
    let err = 0; for (let k = 0; k < n; k++) err = Math.max(err, Math.hypot(re[k] - d.re[k], im[k] - d.im[k]));
    assert.ok(err < 1e-7, `n=${n} error ${err}`);
  }
});

test('Espectro Hann y ajuste sinusoidal', () => {
  const fs = 1024, x = Array.from({ length: 1024 }, (_, i) => 3 * Math.sin(2 * Math.PI * 50 * i / fs));
  near(spectrum(x, fs, 'hann').peakNear(50, 1).amp, 3, 0.01, 'amplitud');
  const y = Array.from({ length: 3001 }, (_, i) => 4.2 * Math.cos(2 * Math.PI * 24.7 * (i / 2000 - 0.013) - 133 * Math.PI / 180) + 0.5);
  const f = fitSinusoid(y, 2000, 24.7, 0.013);
  near(f.amp, 4.2, 1e-9, 'amp'); near(f.phase, 133, 1e-7, 'fase');
});

test('Integración aceleración → velocidad', () => {
  const fs = 5000, acc = Array.from({ length: 5000 }, (_, i) => Math.sin(2 * Math.PI * 50 * i / fs));
  const v = convertUnits(acc, fs, 'g', 'mms');
  const exp = G / (2 * Math.PI * 50) * 1000;
  near(statistics(v).rms * Math.SQRT2, exp, exp * 0.01, 'velocidad pico');
});

test('Severidad', () => {
  assert.equal(evaluateSeverity(1.0, 'g2r').zone, 'A');
  assert.equal(evaluateSeverity(3.5, 'g2r').zone, 'C');
  assert.equal(evaluateSeverity(4.5, 'g2r').zone, 'D');
});

test('Balanceo 1 y 2 planos anulan el desbalance', () => {
  const alpha = C.polar(0.004, 63), u = C.polar(60, 115);
  const o = toP(C.mul(alpha, u)), t = { mag: 25, ang: 30 }, r = toP(C.mul(alpha, C.add(u, toC(t))));
  const s = singlePlane(o, t, r);
  near(C.abs(C.add(toC(s.correction), u)), 0, 1e-9, 'W+U');
  const h = [[C.polar(0.003, 52), C.polar(0.001, 92)], [C.polar(0.0012, 75), C.polar(0.0028, 57)]];
  const u1 = C.polar(80, 23), u2 = C.polar(50, 223);
  const V = (k, a, b) => toP(C.add(C.mul(h[k][0], a), C.mul(h[k][1], b)));
  const t1 = { mag: 30, ang: 0 }, t2 = { mag: 30, ang: 90 };
  const res = twoPlanes(V(0, u1, u2), V(1, u1, u2), t1, V(0, C.add(u1, toC(t1)), u2), V(1, C.add(u1, toC(t1)), u2), t2, V(0, u1, C.add(u2, toC(t2))), V(1, u1, C.add(u2, toC(t2))));
  near(C.abs(C.add(toC(res.w1), u1)), 0, 1e-8, 'plano 1');
  near(C.abs(C.add(toC(res.w2), u2)), 0, 1e-8, 'plano 2');
});

for (const [planes, against] of [[1, true], [2, true], [2, false]]) {
  test(`Simulador + sesión guiada (${planes} plano/s, ángulos ${against ? 'contra' : 'a favor del'} giro)`, () => {
    const set = { ...defaultSimSettings(), sensors: planes, noise: 0.03, masses: [] };
    const sim = new RotorSimulator(set);
    const read = () => { const r = sim.acquire(); return Array.from({ length: planes }, (_, c) => r.reading1X(c)); };
    const phys = (a) => (against ? a : -a);
    const s = new BalancingSession(planes, { againstRotation: against, tolerance: 0.2 });
    const o = read();
    s.recordInitial(o[0], o[1], set.rpm);
    const t1 = { mag: 40, ang: 30 };
    set.masses = [{ plane: 1, grams: 40, angle: phys(30), radiusMm: 140 }];
    const r1 = read(); s.recordTrial1(t1, r1[0], r1[1], set.rpm);
    set.masses = [];
    if (planes === 2) {
      set.masses = [{ plane: 2, grams: 40, angle: phys(200), radiusMm: 140 }];
      const r2 = read(); s.recordTrial2({ mag: 40, ang: 200 }, r2[0], r2[1], set.rpm);
      set.masses = [];
    }
    assert.equal(s.step, 'correction');
    set.masses = s.corrections.map((w, i) => ({ plane: i + 1, grams: w.mag, angle: phys(w.ang), radiusMm: 140 }));
    const after = read();
    const before = Math.max(...o.map((x) => x.mag)), now = Math.max(...after.map((x) => x.mag));
    assert.ok(now < before * 0.1, `residual ${now} vs ${before}`);
    const v = s.recordVerification(after[0], after[1], set.rpm);
    assert.ok(v.ok); assert.equal(s.step, 'done');
    const again = BalancingSession.fromRuns(JSON.parse(JSON.stringify(s.toJSON())));
    assert.equal(again.step, 'done');
  });
}

test('Orden de pasos impuesto', () => {
  const s = new BalancingSession(1);
  assert.throws(() => s.recordTrial1({ mag: 10, ang: 0 }, { mag: 1, ang: 0 }, null, 1500));
  s.recordInitial({ mag: 5, ang: 10 }, null, 1500);
  assert.throws(() => s.recordInitial({ mag: 5, ang: 10 }, null, 1500));
});

test('Utilidades: reparto, perforación, peso de prueba, ISO 1940', () => {
  const w = { mag: 37, ang: 100 }, sp = splitWeight(w, 8, 0);
  near(psub(padd({ mag: sp.massA, ang: sp.angA }, { mag: sp.massB, ang: sp.angB }), w).mag, 0, 1e-9, 'suma');
  const d = drillDepthMm(12, 7850, 10), r = 0.005, tip = r / Math.tan(59 * Math.PI / 180);
  near((Math.PI * r * r * tip / 3 + Math.PI * r * r * (d / 1000 - tip)) * 7850 * 1000, 12, 1e-6, 'gramos');
  const g = suggestTrialGrams(100, 1500, 200), wv = 1500 * 2 * Math.PI / 60;
  near(g / 1000 * 0.2 * wv * wv, 0.1 * 100 * G, 1e-6, 'fuerza');
  near(permissibleUnbalance(6.3, 100, 1500), 9549 * 6.3 * 100 / 1500, 1e-9, 'Uper');
});

test('CSV ida y vuelta, WAV y protocolo serie', () => {
  const r = new RotorSimulator({ ...defaultSimSettings(), samples: 2048 }).acquire();
  const back = parseCsv(writeCsv(r));
  near(back.fs, r.fs, 0.5, 'fs'); assert.equal(back.channels.length, 2); assert.ok(back.tach);
  near(back.reading1X(0).mag, r.reading1X(0).mag, 0.01, '1X');
  // WAV 16 bits estéreo
  const frames = 800, buf = new ArrayBuffer(44 + frames * 4), dv = new DataView(buf);
  const w = (o, s) => [...s].forEach((c, i) => dv.setUint8(o + i, c.charCodeAt(0)));
  w(0, 'RIFF'); dv.setUint32(4, 36 + frames * 4, true); w(8, 'WAVE'); w(12, 'fmt '); dv.setUint32(16, 16, true); dv.setUint16(20, 1, true); dv.setUint16(22, 2, true);
  dv.setUint32(24, 8000, true); dv.setUint32(28, 32000, true); dv.setUint16(32, 4, true); dv.setUint16(34, 16, true); w(36, 'data'); dv.setUint32(40, frames * 4, true);
  for (let i = 0; i < frames; i++) { dv.setInt16(44 + i * 4, Math.round(0.5 * Math.sin(i / 10) * 32767), true); dv.setInt16(46 + i * 4, Math.round(0.25 * 32767), true); }
  const wr = parseWav(buf, { scale: 2, unit: 'g' });
  assert.equal(wr.fs, 8000); near(wr.channels[1].data[10], 0.5, 1e-3, 'escala');
  const p = new SerialStreamParser();
  p.feed('basura\n#VA fs=2000 ch=2 unit=g tach=1\n0.1,0.2,0\n0.3,0.4,5\n0.5,'); p.feed('0.6,0\n');
  const sr = p.toRecording(); assert.equal(sr.length, 3); near(sr.channels[1].data[2], 0.6, 1e-12, 'dato');
});

test('Diagnóstico: desbalance, desalineación y rodamiento', () => {
  const run = (over) => {
    const set = { ...defaultSimSettings(), sensors: 1, ...over };
    const r = new RotorSimulator(set).acquire();
    return diagnose({ spectrum: spectrum(r.channels[0].data, r.fs, 'hann'), runningHz: r.measuredRpm() / 60, bearing: over.bearing ? set.bearingGeom : null });
  };
  assert.equal(run({})[0].type, 'unbalance');
  assert.ok(run({ u1: { mag: 300, ang: 0 }, u2: { mag: 0, ang: 0 }, misalignment: 3 }).some((f) => f.type === 'misalignment'));
  assert.ok(run({ u1: { mag: 500, ang: 0 }, u2: { mag: 0, ang: 0 }, looseness: 2 }).some((f) => f.type === 'looseness'));
  assert.ok(run({ u1: { mag: 400, ang: 0 }, u2: { mag: 0, ang: 0 }, bearing: 2 }).some((f) => f.type === 'bpfo'));
});

const vecPath = join(here, 'vectors.json');
test('Paridad con la app de escritorio (C#)', { skip: !existsSync(vecPath) && 'sin vectors.json' }, () => {
  const v = JSON.parse(readFileSync(vecPath, 'utf8'));
  const sp = spectrum(v.signal, 1000, 'hann');
  near(sp.resolution, v.spectrumHann.Resolution, 1e-12, 'resolución');
  v.spectrumHann.amplitudes.forEach((a, i) => near(sp.amps[i], a, 1e-9, `línea ${i}`));
  const f = fitSinusoid(v.signal, 1000, v.fit.freq, v.fit.t0);
  near(f.amp, v.fit.amplitude, 1e-9, 'fit amp'); near(f.phase, v.fit.phase, 1e-7, 'fit fase');
  const P = (a) => ({ mag: a[0], ang: a[1] });
  const one = singlePlane(P(v.single.o), P(v.single.t), P(v.single.r));
  near(one.correction.mag, v.single.w[0], 1e-9, 'W'); near(one.correction.ang, v.single.w[1], 1e-7, 'W ang');
  near(one.correctionKeep.mag, v.single.keep[0], 1e-9, 'W keep');
  const two = twoPlanes(...v.two.input.map(P));
  near(two.w1.mag, v.two.w1[0], 1e-9, 'W1'); near(two.w1.ang, v.two.w1[1], 1e-7, 'W1 ang');
  near(two.w2.mag, v.two.w2[0], 1e-9, 'W2'); near(two.w2.ang, v.two.w2[1], 1e-7, 'W2 ang');
  near(two.condition, v.two.cond, 1e-9, 'condición');
  const s = splitWeight(P(v.split.w), v.split.n, v.split.first);
  assert.equal(s.posA, v.split.a[0]); near(s.massA, v.split.a[1], 1e-9, 'masa A'); near(s.massB, v.split.b[1], 1e-9, 'masa B');
  near(drillDepthMm(v.drill.mass, v.drill.density, v.drill.d), v.drill.depth, 1e-9, 'perforación');
  near(suggestTrialGrams(v.trial.mass, v.trial.rpm, v.trial.r), v.trial.g, 1e-9, 'peso de prueba');
  near(statistics(convertUnits(v.integration.accFull, v.integration.fs, 'g', 'mms')).rms, v.integration.velRms, 1e-9, 'integración');
});
