// Procesamiento de señales: FFT, ventanas, espectro, ajuste sinusoidal, estadísticas, integración y tacómetro.
// Equivalente a VibrationAnalysis.Core.Dsp (C#). Los números complejos se representan como arreglos re/im separados.

export const G = 9.80665;

export const normalizeAngle = (deg) => {
  let r = deg % 360;
  if (r < 0) r += 360;
  if (r >= 360 - 1e-12) r = 0;
  return r;
};

export const angleDiff = (a, b) => {
  const d = normalizeAngle(a - b);
  return d > 180 ? d - 360 : d;
};

const isPow2 = (n) => n > 0 && (n & (n - 1)) === 0;
export const nextPow2 = (n) => { let p = 1; while (p < n) p <<= 1; return p; };

function radix2(re, im, inverse) {
  const n = re.length;
  for (let i = 1, j = 0; i < n; i++) {
    let bit = n >> 1;
    for (; j & bit; bit >>= 1) j ^= bit;
    j ^= bit;
    if (i < j) { [re[i], re[j]] = [re[j], re[i]]; [im[i], im[j]] = [im[j], im[i]]; }
  }
  for (let len = 2; len <= n; len <<= 1) {
    const ang = (2 * Math.PI / len) * (inverse ? 1 : -1);
    const wr = Math.cos(ang), wi = Math.sin(ang);
    const half = len >> 1;
    for (let i = 0; i < n; i += len) {
      let cr = 1, ci = 0;
      for (let j = 0; j < half; j++) {
        const a = i + j, b = a + half;
        const vr = re[b] * cr - im[b] * ci, vi = re[b] * ci + im[b] * cr;
        re[b] = re[a] - vr; im[b] = im[a] - vi;
        re[a] += vr; im[a] += vi;
        const t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
      }
    }
  }
  if (inverse) for (let i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
}

function bluestein(re, im, inverse) {
  const n = re.length, m = nextPow2(2 * n - 1), sign = inverse ? 1 : -1;
  const wr = new Float64Array(n), wi = new Float64Array(n);
  for (let k = 0; k < n; k++) {
    const kk = (k * k) % (2 * n);
    const a = sign * Math.PI * kk / n;
    wr[k] = Math.cos(a); wi[k] = Math.sin(a);
  }
  const ar = new Float64Array(m), ai = new Float64Array(m), br = new Float64Array(m), bi = new Float64Array(m);
  for (let k = 0; k < n; k++) { ar[k] = re[k] * wr[k] - im[k] * wi[k]; ai[k] = re[k] * wi[k] + im[k] * wr[k]; }
  br[0] = wr[0]; bi[0] = -wi[0];
  for (let k = 1; k < n; k++) { br[k] = br[m - k] = wr[k]; bi[k] = bi[m - k] = -wi[k]; }
  radix2(ar, ai, false); radix2(br, bi, false);
  for (let i = 0; i < m; i++) { const r = ar[i] * br[i] - ai[i] * bi[i]; ai[i] = ar[i] * bi[i] + ai[i] * br[i]; ar[i] = r; }
  radix2(ar, ai, true);
  for (let k = 0; k < n; k++) {
    let r = ar[k] * wr[k] - ai[k] * wi[k], i = ar[k] * wi[k] + ai[k] * wr[k];
    if (inverse) { r /= n; i /= n; }
    re[k] = r; im[k] = i;
  }
}

/** FFT en el lugar. Radix-2 o Bluestein según el largo. Convención X[k] = Σ x[n]·e^(−j2πkn/N). */
export function fft(re, im, inverse = false) {
  if (re.length <= 1) return;
  if (isPow2(re.length)) radix2(re, im, inverse); else bluestein(re, im, inverse);
}

export const WINDOWS = {
  rect: { name: 'Rectangular', enbw: 1.0 },
  hann: { name: 'Hanning', enbw: 1.5 },
  hamming: { name: 'Hamming', enbw: 1.363 },
  flattop: { name: 'Flat-top', enbw: 3.77 },
  blackmanharris: { name: 'Blackman-Harris', enbw: null },
  kaiser: { name: 'Kaiser (β 8,6)', enbw: null },
};
const enbwCache = {};
/** ENBW en bins, igual que la app de escritorio (tabla o ventana de 4096 puntos). */
export function windowEnbw(type) {
  const t = WINDOWS[type] ? type : 'rect';
  if (WINDOWS[t].enbw != null) return WINDOWS[t].enbw;
  return (enbwCache[t] ??= enbwOf(windowFn(t, 4096)));
}

function besselI0(x) {
  let sum = 1, term = 1;
  const y = x * x / 4;
  for (let k = 1; k < 60 && term > 1e-16 * sum; k++) { term *= y / (k * k); sum += term; }
  return sum;
}

/** Ancho de banda de ruido equivalente (en bins) de una ventana. */
export function enbwOf(w) {
  let s1 = 0, s2 = 0;
  for (const v of w) { s1 += v; s2 += v * v; }
  return w.length * s2 / (s1 * s1);
}

export function windowFn(type, n) {
  const w = new Float64Array(n);
  if (n === 1) { w[0] = 1; return w; }
  for (let i = 0; i < n; i++) {
    const x = 2 * Math.PI * i / (n - 1);
    switch (type) {
      case 'hann': w[i] = 0.5 - 0.5 * Math.cos(x); break;
      case 'hamming': w[i] = 0.54 - 0.46 * Math.cos(x); break;
      case 'flattop': w[i] = 0.21557895 - 0.41663158 * Math.cos(x) + 0.277263158 * Math.cos(2 * x) - 0.083578947 * Math.cos(3 * x) + 0.006947368 * Math.cos(4 * x); break;
      case 'blackmanharris': w[i] = 0.35875 - 0.48829 * Math.cos(x) + 0.14128 * Math.cos(2 * x) - 0.01168 * Math.cos(3 * x); break;
      case 'kaiser': { const r = 2 * i / (n - 1) - 1; w[i] = besselI0(8.6 * Math.sqrt(Math.max(0, 1 - r * r))) / besselI0(8.6); break; }
      default: w[i] = 1;
    }
  }
  return w;
}

/** Espectro de amplitud pico (lado único). */
export function spectrum(signal, fs, window = 'hann', removeMean = true) {
  const n = signal.length;
  if (n < 8) throw new Error('Se necesitan al menos 8 muestras.');
  let mean = 0;
  if (removeMean) { for (const v of signal) mean += v; mean /= n; }
  const w = windowFn(window, n);
  let cg = 0; for (const v of w) cg += v; cg /= n;
  const re = new Float64Array(n), im = new Float64Array(n);
  for (let i = 0; i < n; i++) re[i] = (signal[i] - mean) * w[i];
  fft(re, im);
  const half = Math.floor(n / 2) + 1, df = fs / n;
  const freqs = new Float64Array(half), amps = new Float64Array(half), phases = new Float64Array(half);
  for (let k = 0; k < half; k++) {
    freqs[k] = k * df;
    const scale = (k === 0 || (n % 2 === 0 && k === n / 2)) ? 1 : 2;
    amps[k] = Math.hypot(re[k], im[k]) * scale / (n * cg);
    phases[k] = normalizeAngle(Math.atan2(im[k], re[k]) * 180 / Math.PI);
  }
  return new Spectrum(freqs, amps, phases, df, fs, window);
}

export class Spectrum {
  constructor(freqs, amps, phases, resolution, fs, window, enbw = null, kind = 'Espectro') {
    Object.assign(this, { freqs, amps, phases, resolution, fs, window, kind });
    this._enbw = enbw;
  }
  get enbw() { return this._enbw ?? windowEnbw(this.window); }
  peakNear(freq, tol) {
    let best = -1, amp = 0;
    for (let i = 0; i < this.freqs.length; i++) {
      if (Math.abs(this.freqs[i] - freq) <= tol && this.amps[i] > amp) { amp = this.amps[i]; best = i; }
    }
    return best < 0 ? { freq, amp: 0, index: -1 } : { freq: this.freqs[best], amp, index: best };
  }
  bandRms(fMin, fMax) {
    let s = 0;
    for (let i = 0; i < this.freqs.length; i++) if (this.freqs[i] >= fMin && this.freqs[i] <= fMax) s += this.amps[i] * this.amps[i] / 2;
    return Math.sqrt(s / this.enbw);
  }
}

/** Ajuste por mínimos cuadrados de A·cos(2πf(t − t0) − φ) + c. Devuelve { amp, phase } (φ = retraso en grados). */
export function fitSinusoid(signal, fs, freq, t0 = 0) {
  let scc = 0, sss = 0, scs = 0, sc = 0, ss = 0, sxc = 0, sxs = 0, sx = 0;
  const n = signal.length;
  for (let i = 0; i < n; i++) {
    const th = 2 * Math.PI * freq * (i / fs - t0);
    const c = Math.cos(th), s = Math.sin(th), x = signal[i];
    scc += c * c; sss += s * s; scs += c * s; sc += c; ss += s; sxc += x * c; sxs += x * s; sx += x;
  }
  const [a, b] = solve3([[scc, scs, sc], [scs, sss, ss], [sc, ss, n]], [sxc, sxs, sx]);
  return { amp: Math.hypot(a, b), phase: normalizeAngle(Math.atan2(b, a) * 180 / Math.PI) };
}

function solve3(m, r) {
  const a = m.map((row) => row.slice()), b = r.slice();
  for (let col = 0; col < 3; col++) {
    let piv = col;
    for (let i = col + 1; i < 3; i++) if (Math.abs(a[i][col]) > Math.abs(a[piv][col])) piv = i;
    if (Math.abs(a[piv][col]) < 1e-15) return [0, 0, 0];
    [a[col], a[piv]] = [a[piv], a[col]]; [b[col], b[piv]] = [b[piv], b[col]];
    for (let i = col + 1; i < 3; i++) {
      const f = a[i][col] / a[col][col];
      for (let j = col; j < 3; j++) a[i][j] -= f * a[col][j];
      b[i] -= f * b[col];
    }
  }
  const x = [0, 0, 0];
  for (let i = 2; i >= 0; i--) {
    let s = b[i];
    for (let j = i + 1; j < 3; j++) s -= a[i][j] * x[j];
    x[i] = s / a[i][i];
  }
  return x;
}

export function statistics(x) {
  const n = x.length;
  if (!n) return { rms: 0, peak: 0, p2p: 0, crest: 0, kurtosis: 0, mean: 0 };
  let mean = 0; for (const v of x) mean += v; mean /= n;
  let s2 = 0, s4 = 0, max = -Infinity, min = Infinity, peak = 0;
  for (const v of x) {
    const d = v - mean;
    s2 += d * d; s4 += d * d * d * d;
    if (v > max) max = v; if (v < min) min = v;
    if (Math.abs(d) > peak) peak = Math.abs(d);
  }
  const variance = s2 / n, rms = Math.sqrt(variance);
  return { rms, peak, p2p: max - min, crest: rms > 0 ? peak / rms : 0, kurtosis: variance > 0 ? (s4 / n) / (variance * variance) : 0, mean };
}

export const UNITS = {
  g: { label: 'Aceleración', symbol: 'g', order: 0, si: G },
  ms2: { label: 'Aceleración', symbol: 'm/s²', order: 0, si: 1 },
  mms: { label: 'Velocidad', symbol: 'mm/s', order: -1, si: 1e-3 },
  um: { label: 'Desplazamiento', symbol: 'µm', order: -2, si: 1e-6 },
};

/** Convierte entre aceleración, velocidad y desplazamiento en el dominio de la frecuencia (con filtro pasa-altos). */
export function convertUnits(x, fs, from, to, highPassHz = 2) {
  if (from === to) return Float64Array.from(x);
  const n = x.length;
  let mean = 0; for (const v of x) mean += v; mean /= n;
  const re = new Float64Array(n), im = new Float64Array(n);
  for (let i = 0; i < n; i++) re[i] = x[i] - mean;
  fft(re, im);
  const p = UNITS[to].order - UNITS[from].order, k0 = UNITS[from].si / UNITS[to].si;
  for (let k = 0; k < n; k++) {
    const kk = k <= n / 2 ? k : k - n;
    const f = kk * fs / n;
    if (Math.abs(f) < highPassHz) { re[k] = 0; im[k] = 0; continue; }
    // multiplicar por (jω)^p
    const w = 2 * Math.PI * f;
    const mag = Math.pow(Math.abs(w), p) * k0;
    const ang = p * (w >= 0 ? Math.PI / 2 : -Math.PI / 2);
    const cr = mag * Math.cos(ang), ci = mag * Math.sin(ang);
    const r = re[k] * cr - im[k] * ci; im[k] = re[k] * ci + im[k] * cr; re[k] = r;
  }
  fft(re, im, true);
  return re;
}

/** Instantes (s) de los flancos de subida del tacómetro. */
export function detectPulses(tach, fs, threshold = null, minPeriod = 0.002) {
  if (!tach || tach.length < 2) return [];
  let mx = -Infinity, mn = Infinity;
  for (const v of tach) { if (v > mx) mx = v; if (v < mn) mn = v; }
  const thr = threshold ?? (mx + mn) / 2;
  const res = []; let last = -Infinity;
  for (let i = 1; i < tach.length; i++) {
    if (tach[i - 1] < thr && tach[i] >= thr) {
      const t = (i - 1 + (thr - tach[i - 1]) / (tach[i] - tach[i - 1])) / fs;
      if (t - last >= minPeriod) { res.push(t); last = t; }
    }
  }
  return res;
}

export function rpmFromPulses(pulses, ppr = 1) {
  if (pulses.length < 2) return 0;
  const period = (pulses[pulses.length - 1] - pulses[0]) / (pulses.length - 1) * ppr;
  return period > 0 ? 60 / period : 0;
}
