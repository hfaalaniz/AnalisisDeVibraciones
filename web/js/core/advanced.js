// Análisis avanzado: filtros Butterworth, espectro promediado, PSD, zoom FFT, cepstrum, envolvente, curtosis espectral
// y seguimiento de órdenes (espectro de órdenes, Bode/Nyquist, cascada, velocidades críticas).
// Equivalente a VibrationAnalysis.Core.Dsp.AdvancedSpectrum / Filters / OrderTracking (C#).
import { fft, windowFn, spectrum, Spectrum, normalizeAngle, nextPow2 } from './dsp.js';

// ───────── Filtros ─────────

class Biquad {
  constructor(b0, b1, b2, a1, a2) { Object.assign(this, { b0, b1, b2, a1, a2 }); }
  static create(highPass, fc, fs, q) {
    const w0 = 2 * Math.PI * fc / fs, cw = Math.cos(w0), alpha = Math.sin(w0) / (2 * q), a0 = 1 + alpha;
    const b1 = highPass ? -(1 + cw) : 1 - cw, b0 = highPass ? (1 + cw) / 2 : (1 - cw) / 2;
    return new Biquad(b0 / a0, b1 / a0, b0 / a0, -2 * cw / a0, (1 - alpha) / a0);
  }
  run(y) {
    let z1 = 0, z2 = 0;
    const { b0, b1, b2, a1, a2 } = this;
    for (let i = 0; i < y.length; i++) {
      const x = y[i], o = b0 * x + z1;
      z1 = b1 * x - a1 * o + z2; z2 = b2 * x - a2 * o; y[i] = o;
    }
  }
  gain(f, fs) {
    const w = -2 * Math.PI * f / fs, zr = Math.cos(w), zi = Math.sin(w), z2r = Math.cos(2 * w), z2i = Math.sin(2 * w);
    const nr = this.b0 + this.b1 * zr + this.b2 * z2r, ni = this.b1 * zi + this.b2 * z2i;
    const dr = 1 + this.a1 * zr + this.a2 * z2r, di = this.a1 * zi + this.a2 * z2i;
    return Math.hypot(nr, ni) / Math.hypot(dr, di);
  }
}

/** Filtro Butterworth en secciones de 2.º orden. kind: 'low' | 'high' | 'band'. Orden par 2–12. */
export function butterworth(kind, order, fs, f1, f2 = 0) {
  if (order < 2 || order > 12 || order % 2) throw new Error('El orden debe ser par, entre 2 y 12.');
  const nyq = fs / 2;
  const check = (f) => { if (!(f > 0 && f < nyq)) throw new Error(`La frecuencia debe estar entre 0 y ${nyq.toFixed(1)} Hz.`); };
  const sections = [];
  const add = (hp, fc) => {
    for (let k = 0; k < order / 2; k++) sections.push(Biquad.create(hp, fc, fs, 1 / (2 * Math.cos((2 * k + 1) * Math.PI / (2 * order)))));
  };
  check(f1);
  if (kind === 'low') add(false, f1);
  else if (kind === 'high') add(true, f1);
  else {
    check(f2);
    if (f2 <= f1) throw new Error('La frecuencia superior debe ser mayor que la inferior.');
    add(true, f1); add(false, f2);
  }
  const apply = (x) => { const y = Float64Array.from(x); for (const s of sections) s.run(y); return y; };
  return {
    sections, fs,
    gain: (f) => sections.reduce((g, s) => g * s.gain(f, fs), 1),
    apply,
    /** Filtrado ida y vuelta (fase cero) con extensión por reflexión. */
    filtfilt(x) {
      const n = x.length;
      if (!n) return new Float64Array(0);
      const pad = Math.trunc(Math.min(n - 1, Math.max(3 * (2 * sections.length + 1), 3 * fs / f1)));
      const ext = new Float64Array(n + 2 * pad);
      for (let i = 0; i < pad; i++) ext[i] = 2 * x[0] - x[pad - i];
      for (let i = 0; i < n; i++) ext[pad + i] = x[i];
      for (let i = 0; i < pad; i++) ext[pad + n + i] = 2 * x[n - 1] - x[n - 2 - i];
      let y = apply(ext).reverse();
      y = apply(y).reverse();
      return y.slice(pad, pad + n);
    },
  };
}

// ───────── Espectros ─────────

export function segments(length, segment, overlap) {
  if (segment < 8 || segment > length) throw new Error('El bloque debe tener entre 8 muestras y el largo de la señal.');
  overlap = Math.min(0.9, Math.max(0, overlap));
  const step = Math.max(1, Math.round(segment * (1 - overlap)));
  const r = [];
  for (let s = 0; s + segment <= length; s += step) r.push(s);
  return r;
}

const KINDS = { linear: 'Espectro promediado', exponential: 'Espectro (promedio exponencial)', peak: 'Espectro (retención de picos)' };

/** Espectro promediado por bloques (lineal RMS, exponencial o retención de picos). */
export function averaged(x, fs, segment, overlap = 0.5, window = 'hann', mode = 'linear', expAverages = 8) {
  const starts = segments(x.length, segment, overlap);
  let acc = null, first = null, k = 0;
  for (const s of starts) {
    const sp = spectrum(x.subarray ? x.subarray(s, s + segment) : x.slice(s, s + segment), fs, window);
    first ??= sp; k++;
    if (!acc) { acc = mode === 'peak' ? Float64Array.from(sp.amps) : sp.amps.map((a) => a * a); continue; }
    for (let i = 0; i < acc.length; i++) {
      const a = sp.amps[i];
      acc[i] = mode === 'peak' ? Math.max(acc[i], a)
        : mode === 'exponential' ? acc[i] + (a * a - acc[i]) / Math.min(k, Math.max(1, expAverages))
          : acc[i] + (a * a - acc[i]) / k;
    }
  }
  const amps = mode === 'peak' ? acc : acc.map(Math.sqrt);
  const r = new Spectrum(first.freqs, amps, new Float64Array(amps.length), first.resolution, fs, window, null, KINDS[mode] || KINDS.linear);
  r.averages = k;
  return r;
}

/** Densidad espectral de potencia por Welch (unidades²/Hz, lado único). */
export function psd(x, fs, segment, overlap = 0.5, window = 'hann') {
  const starts = segments(x.length, segment, overlap);
  const w = windowFn(window, segment);
  let s2 = 0; for (const v of w) s2 += v * v;
  const half = Math.floor(segment / 2) + 1, acc = new Float64Array(half);
  const re = new Float64Array(segment), im = new Float64Array(segment);
  for (const s of starts) {
    let mean = 0; for (let i = 0; i < segment; i++) mean += x[s + i];
    mean /= segment;
    for (let i = 0; i < segment; i++) { re[i] = (x[s + i] - mean) * w[i]; im[i] = 0; }
    fft(re, im);
    for (let k = 0; k < half; k++) {
      const scale = (k === 0 || (segment % 2 === 0 && k === segment / 2)) ? 1 : 2;
      acc[k] += scale * (re[k] * re[k] + im[k] * im[k]) / (fs * s2);
    }
  }
  for (let k = 0; k < half; k++) acc[k] /= starts.length;
  const df = fs / segment;
  return {
    freqs: Float64Array.from({ length: half }, (_, k) => k * df), psd: acc, resolution: df, averages: starts.length,
    bandRms(f1, f2) { let s = 0; for (let i = 0; i < half; i++) if (this.freqs[i] >= f1 && this.freqs[i] <= f2) s += acc[i] * df; return Math.sqrt(s); },
  };
}

/** Zoom FFT: desplazamiento a banda base, filtrado, diezmado y FFT. Alta resolución alrededor de fCenter. */
export function zoom(x, fs, fCenter, span, window = 'hann') {
  if (!(span > 0) || fCenter - span / 2 < 0 || fCenter + span / 2 > fs / 2) throw new Error('La banda de zoom debe quedar entre 0 y fs/2.');
  const n = x.length, dec = Math.max(1, Math.floor(fs / (1.28 * span))), fsd = fs / dec;
  let mean = 0; for (const v of x) mean += v; mean /= n;
  let re = new Float64Array(n), im = new Float64Array(n);
  for (let i = 0; i < n; i++) {
    const th = -2 * Math.PI * fCenter * i / fs, v = x[i] - mean;
    re[i] = v * Math.cos(th); im[i] = v * Math.sin(th);
  }
  if (dec > 1) {
    const lp = butterworth('low', 8, fs, Math.min(span / 2 * 1.15, fsd * 0.45));
    re = lp.filtfilt(re); im = lp.filtfilt(im);
  }
  const nd = Math.floor(n / dec);
  if (nd < 16) throw new Error('La señal es demasiado corta para ese zoom.');
  const w = windowFn(window, nd);
  let cg = 0; for (const v of w) cg += v; cg /= nd;
  const zr = new Float64Array(nd), zi = new Float64Array(nd);
  for (let i = 0; i < nd; i++) { zr[i] = re[i * dec] * w[i]; zi[i] = im[i * dec] * w[i]; }
  fft(zr, zi);
  const df = fsd / nd, rows = [];
  for (let j = 0; j < nd; j++) {
    const k = (j + Math.floor(nd / 2)) % nd, kk = k <= nd / 2 ? k : k - nd, f = fCenter + kk * df;
    if (f < fCenter - span / 2 - 1e-9 || f > fCenter + span / 2 + 1e-9) continue;
    rows.push([f, 2 * Math.hypot(zr[k], zi[k]) / (nd * cg), normalizeAngle(Math.atan2(zi[k], zr[k]) * 180 / Math.PI)]);
  }
  rows.sort((a, b) => a[0] - b[0]);
  return new Spectrum(Float64Array.from(rows, (r) => r[0]), Float64Array.from(rows, (r) => r[1]), Float64Array.from(rows, (r) => r[2]),
    df, fs, window, null, `Zoom ${+fCenter.toFixed(2)} ± ${+(span / 2).toFixed(2)} Hz`);
}

/** Cepstrum real: IFFT(log |X|²). Revela familias de armónicos y bandas laterales (engranajes, rodamientos). */
export function cepstrum(x, fs, window = 'hann') {
  const n = x.length;
  if (n < 16) throw new Error('Se necesitan al menos 16 muestras.');
  let mean = 0; for (const v of x) mean += v; mean /= n;
  const w = windowFn(window, n), re = new Float64Array(n), im = new Float64Array(n);
  for (let i = 0; i < n; i++) re[i] = (x[i] - mean) * w[i];
  fft(re, im);
  let maxP = 0;
  for (let k = 0; k < n; k++) maxP = Math.max(maxP, re[k] * re[k] + im[k] * im[k]);
  const eps = Math.max(maxP * 1e-12, 1e-300);
  for (let k = 0; k < n; k++) { re[k] = Math.log(re[k] * re[k] + im[k] * im[k] + eps); im[k] = 0; }
  fft(re, im, true);
  const half = Math.floor(n / 2);
  const q = Float64Array.from({ length: half }, (_, i) => i / fs), c = re.slice(0, half);
  const peakNear = (qq, tol) => {
    let best = -Infinity, bq = qq;
    for (let i = 1; i < half; i++) if (Math.abs(q[i] - qq) <= tol && c[i] > best) { best = c[i]; bq = q[i]; }
    return { quefrency: bq, value: best === -Infinity ? 0 : best };
  };
  return {
    quefrency: q, values: c, fs, peakNear,
    dominant(qMin, qMax) {
      let best = -Infinity, bq = 0;
      for (let i = 1; i < half; i++) if (q[i] >= qMin && q[i] <= qMax && c[i] > best) { best = c[i]; bq = q[i]; }
      return { quefrency: bq, value: best === -Infinity ? 0 : best };
    },
    familyProminence(period, rahmonics = 3) {
      if (!(period > 0)) return 0;
      const tol = Math.max(2 / fs, period * 0.02);
      let s = 0, m = 0;
      for (let k = 1; k <= rahmonics; k++) { if (k * period > q[half - 1]) break; s += Math.max(0, peakNear(k * period, tol).value); m++; }
      if (!m) return 0;
      const rest = Array.from(c.slice(Math.max(1, Math.trunc(0.002 * fs))), Math.abs).sort((a, b) => a - b);
      const typical = rest.length ? rest[Math.floor(rest.length / 2)] : 1e-12;
      return s / m / Math.max(typical, 1e-12);
    },
  };
}

/** Señal analítica (Hilbert por FFT). Devuelve { re, im }. */
export function analytic(x) {
  const n = x.length, re = Float64Array.from(x), im = new Float64Array(n);
  fft(re, im);
  const half = Math.floor(n / 2);
  for (let k = 1; k < n; k++) {
    if (k < Math.floor((n + 1) / 2)) { re[k] *= 2; im[k] *= 2; }
    else if (!(n % 2 === 0 && k === half)) { re[k] = 0; im[k] = 0; }
  }
  fft(re, im, true);
  return { re, im };
}

/** Envolvente: pasa-banda Butterworth (fase cero) + módulo de la señal analítica. */
export function envelope(x, fs, low, high, order = 4) {
  const y = butterworth('band', order, fs, low, high).filtfilt(x);
  const a = analytic(y);
  return a.re.map((r, i) => Math.hypot(r, a.im[i]));
}

export function envelopeSpectrum(x, fs, low, high, window = 'hann', order = 4) {
  const sp = spectrum(envelope(x, fs, low, high, order), fs, window);
  sp.kind = `Envolvente ${Math.round(low)}–${Math.round(high)} Hz`;
  return sp;
}

/** Curtosis espectral (STFT): alta en las bandas excitadas por impactos. */
export function spectralKurtosis(x, fs, segment = 0, overlap = 0.75) {
  if (segment <= 0) segment = Math.min(Math.max(32, x.length / 8 | 0), Math.max(32, nextPow2(Math.trunc(fs * 0.003))));
  const starts = segments(x.length, segment, overlap), w = windowFn('hann', segment);
  const half = Math.floor(segment / 2) + 1, s2 = new Float64Array(half), s4 = new Float64Array(half);
  const re = new Float64Array(segment), im = new Float64Array(segment);
  for (const s of starts) {
    for (let i = 0; i < segment; i++) { re[i] = x[s + i] * w[i]; im[i] = 0; }
    fft(re, im);
    for (let k = 0; k < half; k++) { const p = re[k] * re[k] + im[k] * im[k]; s2[k] += p; s4[k] += p * p; }
  }
  const m = starts.length, sk = new Float64Array(half);
  for (let k = 0; k < half; k++) { const m2 = s2[k] / m, m4 = s4[k] / m; sk[k] = m2 > 0 ? m4 / (m2 * m2) - 2 : 0; }
  sk[0] = 0; sk[half - 1] = 0;
  const df = fs / segment, freqs = Float64Array.from({ length: half }, (_, k) => k * df);
  return {
    freqs, kurtosis: sk, segments: m,
    suggestBand(bandwidth, minFrequency) {
      const w2 = Math.max(1, Math.round(bandwidth / df));
      let best = -Infinity, bi = -1, sum = 0;
      for (let i = 0; i < half; i++) {
        sum += sk[i];
        if (i >= w2) sum -= sk[i - w2];
        if (i >= w2 - 1) {
          const lo = i - w2 + 1;
          if (freqs[lo] < minFrequency || freqs[i] >= freqs[half - 1]) continue;
          if (sum / w2 > best) { best = sum / w2; bi = lo; }
        }
      }
      if (bi < 0) return { low: Math.max(minFrequency, df), high: Math.min(freqs[half - 1] * 0.9, minFrequency + bandwidth), kurtosis: 0 };
      return { low: Math.max(freqs[bi], df), high: freqs[Math.min(bi + w2 - 1, half - 1)], kurtosis: best };
    },
  };
}

/** Banda de demodulación sugerida a partir de la curtosis espectral. */
export function autoEnvelopeBand(x, fs, runningHz, maxDefectHz = 0) {
  const nyq = fs / 2;
  const bw = Math.min(nyq / 2, Math.max(fs / 40, Math.max(fs / 16, 2.5 * maxDefectHz)));
  const minF = Math.min(Math.max(10 * runningHz, 200), nyq - bw * 1.2);
  let { low, high } = spectralKurtosis(x, fs).suggestBand(bw, Math.max(minF, fs / 100));
  high = Math.min(high, nyq * 0.95);
  if (high - low < bw * 0.5) low = Math.max(fs / 100, high - bw);
  return { low, high };
}

// ───────── Órdenes y transitorios ─────────

export function instantRpm(pulses, ppr = 1) {
  const n = Math.max(0, pulses.length - 1), t = new Float64Array(n), r = new Float64Array(n);
  for (let i = 0; i < n; i++) {
    const dt = pulses[i + 1] - pulses[i];
    t[i] = (pulses[i] + pulses[i + 1]) / 2; r[i] = dt > 0 ? 60 / (dt * ppr) : 0;
  }
  return { times: t, rpm: r };
}

export function rpmAt(pulses, t, ppr = 1) {
  const { times: ts, rpm: rs } = instantRpm(pulses, ppr);
  if (!ts.length) return 0;
  if (t <= ts[0]) return rs[0];
  if (t >= ts[ts.length - 1]) return rs[rs.length - 1];
  let lo = 0, hi = ts.length - 1;
  while (hi - lo > 1) { const m = (lo + hi) >> 1; if (ts[m] <= t) lo = m; else hi = m; }
  return rs[lo] + (t - ts[lo]) / (ts[hi] - ts[lo]) * (rs[hi] - rs[lo]);
}

function pchipSlopes(x, y) {
  const n = x.length, d = new Float64Array(n), h = new Float64Array(n - 1), delta = new Float64Array(n - 1);
  for (let i = 0; i < n - 1; i++) { h[i] = x[i + 1] - x[i]; delta[i] = (y[i + 1] - y[i]) / h[i]; }
  if (n === 2) { d[0] = d[1] = delta[0]; return d; }
  for (let i = 1; i < n - 1; i++) {
    if (delta[i - 1] * delta[i] <= 0) { d[i] = 0; continue; }
    const w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1];
    d[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]);
  }
  d[0] = delta[0]; d[n - 1] = delta[n - 2];
  return d;
}

function hermite(x, y, d, i, a) {
  const h = x[i + 1] - x[i], t = (a - x[i]) / h, t2 = t * t, t3 = t2 * t;
  return (2 * t3 - 3 * t2 + 1) * y[i] + (t3 - 2 * t2 + t) * h * d[i] + (-2 * t3 + 3 * t2) * y[i + 1] + (t3 - t2) * h * d[i + 1];
}

function sampleAt(x, pos) {
  const n = x.length;
  if (pos <= 0) return x[0];
  if (pos >= n - 1) return x[n - 1];
  const i = Math.trunc(pos), f = pos - i;
  const p0 = x[Math.max(0, i - 1)], p1 = x[i], p2 = x[Math.min(n - 1, i + 1)], p3 = x[Math.min(n - 1, i + 2)];
  return p1 + 0.5 * f * (p2 - p0 + f * (2 * p0 - 5 * p1 + 4 * p2 - p3 + f * (3 * (p1 - p2) + p3 - p0)));
}

/** Remuestreo angular: samplesPerRev muestras por vuelta usando los pulsos del tacómetro. */
export function angularResample(x, fs, pulses, samplesPerRev, ppr = 1, firstRev = 0, revolutions = -1) {
  if (pulses.length < 2) throw new Error('Se necesitan al menos 2 pulsos de tacómetro.');
  const totalRevs = Math.floor((pulses.length - 1) / ppr);
  if (revolutions < 0) revolutions = totalRevs - firstRev;
  revolutions = Math.min(revolutions, totalRevs - firstRev);
  if (revolutions <= 0) throw new Error('No hay vueltas completas en el rango pedido.');
  const ka = Float64Array.from(pulses, (_, i) => i / ppr), kt = Float64Array.from(pulses);
  const slopes = pchipSlopes(ka, kt);
  const m = revolutions * samplesPerRev, y = new Float64Array(m), tt = new Float64Array(m);
  let seg = firstRev * ppr;
  for (let j = 0; j < m; j++) {
    const a = firstRev + j / samplesPerRev;
    while (seg < ka.length - 2 && a > ka[seg + 1]) seg++;
    const t = hermite(ka, kt, slopes, seg, a);
    tt[j] = t; y[j] = sampleAt(x, t * fs);
  }
  return { signal: y, times: tt, revolutions };
}

function antiAlias(x, fs, pulses, spr, ppr) {
  const { rpm } = instantRpm(pulses, ppr);
  const minHz = rpm.length ? Math.min(...rpm) / 60 : 0, fc = minHz * spr / 2.56;
  if (minHz <= 0 || fc >= fs * 0.45) return x;
  return butterworth('low', 8, fs, fc).filtfilt(x);
}

/** Espectro de órdenes (eje X en órdenes de la velocidad de giro). */
export function orderSpectrum(x, fs, pulses, maxOrder = 20, window = 'hann', ppr = 1) {
  const spr = nextPow2(Math.ceil(2.56 * maxOrder));
  const { signal, revolutions } = angularResample(antiAlias(x, fs, pulses, spr, ppr), fs, pulses, spr, ppr);
  const sp = spectrum(signal, spr, window);
  let keep = 0; while (keep < sp.freqs.length && sp.freqs[keep] <= maxOrder + 1e-9) keep++;
  return new Spectrum(sp.freqs.slice(0, keep), sp.amps.slice(0, keep), sp.phases.slice(0, keep), 1 / revolutions, spr, window, null,
    `Espectro de órdenes (${revolutions} vueltas)`);
}

/** Seguimiento de un orden (Bode/Nyquist): amplitud y retraso de fase por bloques de vueltas. */
export function track(x, fs, pulses, order = 1, revsPerBlock = 4, stepRevs = 0, ppr = 1) {
  if (!(order > 0)) throw new Error('El orden debe ser positivo.');
  if (stepRevs <= 0) stepRevs = Math.max(1, Math.floor(revsPerBlock / 2));
  if (Math.abs(order * revsPerBlock - Math.round(order * revsPerBlock)) > 1e-6)
    throw new Error('El orden por las vueltas del bloque debe dar un número entero (p. ej. 0,5X con 2, 4, 6… vueltas).');
  const spr = Math.max(16, nextPow2(Math.ceil(4 * order + 4)));
  const totalRevs = Math.floor((pulses.length - 1) / ppr), pts = [];
  for (let r0 = 0; r0 + revsPerBlock <= totalRevs; r0 += stepRevs) {
    const { signal: y, revolutions: revs } = angularResample(x, fs, pulses, spr, ppr, r0, revsPerBlock);
    const m = y.length;
    let mean = 0; for (const v of y) mean += v; mean /= m;
    let ar = 0, ai = 0;
    for (let j = 0; j < m; j++) { const th = 2 * Math.PI * order * j / spr, v = y[j] - mean; ar += v * Math.cos(th); ai -= v * Math.sin(th); }
    const tS = pulses[r0 * ppr], tE = pulses[(r0 + revs) * ppr];
    pts.push({ time: (tS + tE) / 2, rpm: 60 * revs / (tE - tS), amp: 2 * Math.hypot(ar, ai) / m, phase: normalizeAngle(-Math.atan2(ai, ar) * 180 / Math.PI) });
  }
  return { order, points: pts, byRpm: () => pts.slice().sort((a, b) => a.rpm - b.rpm) };
}

export function unwrap(deg) {
  const r = new Float64Array(deg.length);
  if (!deg.length) return r;
  r[0] = deg[0];
  for (let i = 1; i < deg.length; i++) { let d = deg[i] - deg[i - 1]; d -= 360 * Math.round(d / 360); r[i] = r[i - 1] + d; }
  return r;
}

/** Velocidades críticas: pico de amplitud con caída a media potencia y giro de fase ≥ minPhaseShift. */
export function criticalSpeeds(tr, minPhaseShift = 45) {
  const p = tr.byRpm(), n = p.length, res = [];
  if (n < 7) return res;
  const raw = p.map((q) => q.amp), amp = raw.map((_, i) => {
    const a = Math.max(0, i - 2), b = Math.min(n - 1, i + 2);
    let s = 0; for (let k = a; k <= b; k++) s += raw[k];
    return s / (b - a + 1);
  });
  const ph = unwrap(p.map((q) => q.phase)), maxAmp = Math.max(...amp);
  const lerp = (x0, x1, y0, y1, y) => Math.abs(y1 - y0) < 1e-300 ? (x0 + x1) / 2 : x0 + (y - y0) / (y1 - y0) * (x1 - x0);
  for (let i = 2; i < n - 2; i++) {
    if (!(amp[i] >= amp[i - 1] && amp[i] > amp[i + 1] && amp[i] >= amp[i - 2] && amp[i] > amp[i + 2])) continue;
    if (amp[i] < maxAmp * 0.25) continue;
    const half = amp[i] / Math.SQRT2;
    let a = i; while (a > 0 && amp[a] > half) a--;
    let b = i; while (b < n - 1 && amp[b] > half) b++;
    if (amp[a] > half || amp[b] > half) continue;
    const n1 = lerp(p[a].rpm, p[a + 1].rpm, amp[a], amp[a + 1], half), n2 = lerp(p[b - 1].rpm, p[b].rpm, amp[b - 1], amp[b], half);
    const shift = Math.abs(ph[b] - ph[a]);
    if (shift < minPhaseShift) continue;
    res.push({ rpm: p[i].rpm, amp: amp[i], phaseShift: shift, q: n2 > n1 ? p[i].rpm / (n2 - n1) : 0 });
  }
  return res;
}

/** Espectrograma (STFT): matriz [tiempo][frecuencia] de amplitudes pico. */
export function spectrogram(x, fs, block, overlap = 0.75, window = 'hann', pulses = null, maxFrequency = 0, maxSlices = 300) {
  let starts = segments(x.length, block, overlap);
  if (starts.length > maxSlices) {
    const k = (starts.length - 1) / (maxSlices - 1);
    starts = [...new Set(Array.from({ length: maxSlices }, (_, i) => starts[Math.round(i * k)]))];
  }
  const times = [], rpm = [], rows = [];
  let freqs = null, nf = 0;
  for (const s of starts) {
    const sp = spectrum(x.slice(s, s + block), fs, window);
    if (!freqs) { nf = maxFrequency > 0 ? sp.freqs.findIndex((f) => f > maxFrequency) : -1; if (nf < 0) nf = sp.freqs.length; freqs = sp.freqs.slice(0, nf); }
    const tm = (s + block / 2) / fs;
    times.push(tm); rpm.push(pulses && pulses.length >= 2 ? rpmAt(pulses, tm) : 0); rows.push(sp.amps.slice(0, nf));
  }
  return { times, rpm, freqs, amps: rows };
}
