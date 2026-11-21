// Adquisiciones: estructura común, simulador, CSV, WAV y protocolo serie. Equivalente a Core.IO y Core.Simulation (C#).

import { fitSinusoid, detectPulses, rpmFromPulses, normalizeAngle } from './dsp.js';
import { bearingOrders } from './analysis.js';
import { C, toP } from './balance.js';

/** Una adquisición: canales { name, unit, data } y tacómetro opcional. */
export class Recording {
  constructor(fs, channels, tach = null, nominalRpm = 0, source = '') {
    Object.assign(this, { fs, channels, tach, nominalRpm, source });
  }
  get length() { return this.channels[0]?.data.length ?? 0; }
  get duration() { return this.length / this.fs; }
  pulses() { return this.tach ? detectPulses(this.tach, this.fs) : []; }
  get hasPhaseReference() { return this.pulses().length >= 2; }
  measuredRpm() {
    const r = rpmFromPulses(this.pulses());
    return r > 0 ? r : this.nominalRpm;
  }
  /** Lectura 1X { mag (pico), ang (retraso de fase respecto del pulso) }. */
  reading1X(ch, rpm = null) {
    const r = rpm ?? this.measuredRpm();
    if (!(r > 0)) throw new Error('No se conoce la velocidad de giro.');
    const p = this.pulses();
    const { amp, phase } = fitSinusoid(this.channels[ch].data, this.fs, r / 60, p.length ? p[0] : 0);
    return { mag: amp, ang: phase };
  }
}

// ── Simulador ──
function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export const defaultSimSettings = () => ({
  rpm: 1500, fs: 5120, samples: 8192, sensors: 2,
  u1: { mag: 2000, ang: 70 }, u2: { mag: 1400, ang: 160 },
  sensitivity: 2.6, lagDeg: 60, crossCoupling: 0.35, noise: 0.05,
  misalignment: 0, looseness: 0, bearing: 0, oilWhirl: 0, electrical: 0, lineHz: 50,
  bearingGeom: { balls: 9, ballD: 7.94, pitchD: 39, contactDeg: 0 },
  masses: [], seed: 12345,
  runUp: false, rpmStart: 300, rpmEnd: 3000, criticalRpm: 1800, damping: 0.08,
});

/** Rotor virtual lineal: vibración 1X = H·U. El balanceo debe "descubrir" H con las corridas de prueba. */
export class RotorSimulator {
  constructor(settings) { this.s = settings; this.rng = mulberry32(settings.seed ?? 1); }
  trueInfluence() {
    const s = this.s, scale = s.sensitivity / 1000 * (s.rpm / 1500) ** 2;
    const P = (mag, lag) => C.polar(scale * mag, lag);
    return [[P(1, s.lagDeg), P(s.crossCoupling, s.lagDeg + 35)], [P(s.crossCoupling * 0.9, s.lagDeg + 20), P(0.85, s.lagDeg + 10)]];
  }
  totalUnbalance() {
    let u1 = C.polar(this.s.u1.mag, this.s.u1.ang), u2 = C.polar(this.s.u2.mag, this.s.u2.ang);
    for (const m of this.s.masses) {
      const add = C.polar(m.grams * m.radiusMm, m.angle);
      if (m.plane === 1) u1 = C.add(u1, add); else u2 = C.add(u2, add);
    }
    if (this.s.sensors === 1) { u1 = C.add(u1, u2); u2 = { re: 0, im: 0 }; }
    return [u1, u2];
  }
  expected1X() {
    const h = this.trueInfluence(), [u1, u2] = this.totalUnbalance();
    if (this.s.sensors === 1) return [toP(C.mul(h[0][0], u1))];
    return [toP(C.add(C.mul(h[0][0], u1), C.mul(h[0][1], u2))), toP(C.add(C.mul(h[1][0], u1), C.mul(h[1][1], u2)))];
  }
  gauss() {
    const u1 = 1 - this.rng(), u2 = this.rng();
    return Math.sqrt(-2 * Math.log(u1)) * Math.cos(2 * Math.PI * u2);
  }
  /** Respuesta de velocidad de un rotor con desbalance (1 grado de libertad) y su retraso de fase. */
  resonance(hz) {
    const wc = Math.max(this.s.criticalRpm ?? 1800, 1) / 60, r = hz / wc, z = Math.max(this.s.damping ?? 0.08, 1e-3);
    return { gain: hz * r * r / Math.sqrt((1 - r * r) ** 2 + (2 * z * r) ** 2), lag: Math.atan2(2 * z * r, 1 - r * r) * 180 / Math.PI };
  }
  /** Arranque o parada: rampa lineal de velocidad que pasa por la velocidad crítica. */
  acquireRunUp() {
    const s = this.s, n = s.samples, fs = s.fs, T = n / fs;
    const fa = Math.max(s.rpmStart, 1) / 60, fb = Math.max(s.rpmEnd, 1) / 60;
    const v1x = this.expected1X(), ref = this.resonance(s.rpm / 60);
    const theta = new Float64Array(n);
    for (let i = 0; i < n; i++) { const t = i / fs; theta[i] = 2 * Math.PI * (fa * t + (fb - fa) * t * t / (2 * T)); }
    const channels = v1x.map((v, c) => {
      const x = new Float64Array(n);
      for (let i = 0; i < n; i++) {
        const hz = fa + (fb - fa) * i / fs / T, { gain, lag } = this.resonance(hz);
        const amp = v.mag * gain / Math.max(ref.gain, 1e-12), ph = (v.ang + lag - ref.lag) * Math.PI / 180;
        let y = amp * Math.cos(theta[i] - ph);
        if (s.misalignment > 0) y += s.misalignment * (hz / (s.rpm / 60)) * Math.cos(2 * theta[i] - 1.1);
        x[i] = y + s.noise * this.gauss();
      }
      return { name: `Sensor ${c + 1}`, unit: 'mms', data: x };
    });
    const tach = new Float64Array(n);
    for (let i = 0; i < n; i++) { const rev = theta[i] / (2 * Math.PI); tach[i] = rev - Math.floor(rev) < 0.03 ? 5 : 0; }
    return new Recording(fs, channels, tach, (s.rpmStart + s.rpmEnd) / 2, s.rpmEnd >= s.rpmStart ? 'Simulador (arranque)' : 'Simulador (parada)');
  }
  acquire() {
    if (this.s.runUp) return this.acquireRunUp();
    const s = this.s, n = s.samples, fs = s.fs, f1 = s.rpm / 60;
    const v1x = this.expected1X();
    const startAngle = this.rng() * 2 * Math.PI;
    const t0 = (2 * Math.PI - startAngle) / (2 * Math.PI * f1);
    const channels = v1x.map((v, c) => {
      const x = new Float64Array(n), side = c === 0 ? 1 : 0.8;
      for (let i = 0; i < n; i++) {
        const th = 2 * Math.PI * f1 * (i / fs - t0);
        let y = v.mag * Math.cos(th - v.ang * Math.PI / 180);
        if (s.misalignment > 0) y += s.misalignment * side * (Math.cos(2 * th - 1.1) + 0.35 * Math.cos(3 * th - 2.0));
        if (s.looseness > 0) {
          for (let k = 2; k <= 8; k++) y += s.looseness * side * 0.6 / Math.sqrt(k) * Math.cos(k * th - 0.7 * k);
          y += s.looseness * 0.35 * Math.cos(0.5 * th + 0.4) + s.looseness * 0.25 * Math.cos(1.5 * th + 1.2);
        }
        if (s.oilWhirl > 0) y += s.oilWhirl * Math.cos(0.45 * th + 0.3);
        if (s.electrical > 0) y += s.electrical * Math.cos(2 * Math.PI * 2 * s.lineHz * (i / fs));
        x[i] = y + s.noise * this.gauss();
      }
      if (s.bearing > 0) this.bearingImpacts(x, fs, f1 * bearingOrders(s.bearingGeom).bpfo, s.bearing * side);
      return { name: `Sensor ${c + 1}`, unit: 'mms', data: x };
    });
    const tach = new Float64Array(n), period = 1 / f1, width = Math.max(period * 0.03, 2 / fs);
    for (let i = 0; i < n; i++) {
      const t = i / fs - t0;
      tach[i] = t - Math.floor(t / period) * period < width ? 5 : 0;
    }
    return new Recording(fs, channels, tach, s.rpm, 'Simulador');
  }
  bearingImpacts(x, fs, fd, amp) {
    const fr = Math.min(1800, fs * 0.4), decay = 900, period = 1 / fd;
    for (let tk = this.rng() * period; tk < x.length / fs; tk += period * (1 + 0.01 * this.gauss())) {
      const i0 = Math.floor(tk * fs);
      for (let i = i0; i < x.length && i < i0 + Math.floor(fs * 0.01); i++) {
        const t = (i - i0) / fs;
        x[i] += amp * Math.exp(-decay * t) * Math.sin(2 * Math.PI * fr * t);
      }
    }
    for (let i = 0; i < x.length; i++) x[i] += amp * 0.25 * Math.cos(2 * Math.PI * fd * i / fs);
  }
}

// ── CSV ──
export function parseUnit(s) {
  const t = String(s).trim().toLowerCase();
  if (t === 'g') return 'g';
  if (['m/s2', 'm/s²', 'ms2'].includes(t)) return 'ms2';
  if (['mm/s', 'mms'].includes(t)) return 'mms';
  if (['um', 'µm', 'micron'].includes(t)) return 'um';
  return null;
}

export function parseCsv(text, { fs: defFs = 0, unit = 'mms', rpm = 0 } = {}) {
  let fs = defFs, nominal = rpm, u = unit, header = null, sep = null;
  const rows = [];
  for (const raw of text.replace(/\r/g, '').split('\n')) {
    const line = raw.trim();
    if (!line) continue;
    if (line.startsWith('#')) {
      for (const kv of line.replace(/^#+/, '').split(/[\s,;\t]+/)) {
        const [k, v] = kv.split('=');
        if (v === undefined) continue;
        const key = k.trim().toLowerCase();
        if (key === 'fs' && isFinite(+v)) fs = +v;
        else if (key === 'rpm' && isFinite(+v)) nominal = +v;
        else if (key === 'unit') u = parseUnit(v) || u;
      }
      continue;
    }
    if (sep === null) sep = line.includes('\t') ? '\t' : line.includes(';') ? ';' : ',';
    const cells = line.split(sep).map((c) => c.trim().replace(/^"|"$/g, ''));
    const nums = cells.map((c) => (sep === ';' ? c.replace(',', '.') : c)).map(Number);
    if (nums.some((v) => !isFinite(v) || cells.some((c) => c === ''))) {
      if (!header && rows.length === 0) { header = cells; continue; }
      throw new Error(`Valor no numérico en la línea: ${line.slice(0, 80)}`);
    }
    rows.push(nums);
  }
  if (rows.length < 16) throw new Error('El archivo tiene muy pocas muestras (mínimo 16).');
  const cols = rows[0].length;
  const looksTime = () => { for (let i = 1; i < Math.min(rows.length, 50); i++) if (rows[i][0] <= rows[i - 1][0]) return false; return true; };
  header = header || Array.from({ length: cols }, (_, i) => (i === 0 && cols > 1 && looksTime() ? 't' : `Canal ${i + 1}`));
  const low = header.map((h) => h.toLowerCase());
  const tCol = low.findIndex((h) => h === 't' || h.startsWith('time') || h.startsWith('tiempo'));
  const tachCol = low.findIndex((h) => h.startsWith('tach') || h.startsWith('tacó') || h.startsWith('taco') || h === 'ref');
  if (tCol >= 0) {
    const dt = (rows[rows.length - 1][tCol] - rows[0][tCol]) / (rows.length - 1);
    if (dt > 0) fs = 1 / dt;
  }
  if (!(fs > 0)) throw new Error('No se pudo determinar la frecuencia de muestreo: agregar una columna de tiempo o la línea "# fs=...".');
  const channels = [];
  for (let c = 0; c < cols; c++) {
    if (c === tCol || c === tachCol) continue;
    channels.push({ name: header[c] || `Canal ${c + 1}`, unit: u, data: Float64Array.from(rows, (r) => r[c] ?? 0) });
  }
  if (!channels.length) throw new Error('No hay columnas de vibración.');
  const tach = tachCol >= 0 ? Float64Array.from(rows, (r) => r[tachCol]) : null;
  return new Recording(fs, channels, tach, nominal, 'CSV');
}

const UNIT_TOKEN = { g: 'g', ms2: 'm/s2', mms: 'mm/s', um: 'um' };

export function writeCsv(r) {
  const lines = [`# fs=${r.fs} rpm=${r.measuredRpm().toFixed(1)} unit=${UNIT_TOKEN[r.channels[0].unit]}`];
  lines.push(['t', ...r.channels.map((c) => c.name.replace(/,/g, ' ')), ...(r.tach ? ['tach'] : [])].join(','));
  for (let i = 0; i < r.length; i++) {
    const row = [(i / r.fs).toFixed(6), ...r.channels.map((c) => +c.data[i].toFixed(6))];
    if (r.tach) row.push(+r.tach[i].toFixed(3));
    lines.push(row.join(','));
  }
  return lines.join('\n') + '\n';
}

// ── WAV ──
export function parseWav(buffer, { scale = 1, unit = 'g', tachChannel = -1, rpm = 0 } = {}) {
  const dv = new DataView(buffer);
  const str = (o) => String.fromCharCode(dv.getUint8(o), dv.getUint8(o + 1), dv.getUint8(o + 2), dv.getUint8(o + 3));
  if (str(0) !== 'RIFF' || str(8) !== 'WAVE') throw new Error('No es un archivo WAV.');
  let o = 12, fmt = 0, ch = 0, rate = 0, bits = 0, dataOff = -1, dataLen = 0;
  while (o + 8 <= dv.byteLength) {
    const id = str(o), size = dv.getUint32(o + 4, true);
    if (id === 'fmt ') {
      fmt = dv.getUint16(o + 8, true); ch = dv.getUint16(o + 10, true); rate = dv.getUint32(o + 12, true); bits = dv.getUint16(o + 22, true);
      if (fmt === 0xfffe) fmt = bits === 32 && size >= 40 && dv.getUint16(o + 32, true) === 3 ? 3 : (bits === 32 ? 3 : 1);
    } else if (id === 'data') { dataOff = o + 8; dataLen = Math.min(size, dv.byteLength - dataOff); }
    o += 8 + size + (size & 1);
  }
  if (dataOff < 0 || !ch) throw new Error('El WAV no contiene datos.');
  const bps = bits / 8, frames = Math.floor(dataLen / (bps * ch));
  const out = Array.from({ length: ch }, () => new Float64Array(frames));
  for (let f = 0; f < frames; f++) {
    for (let c = 0; c < ch; c++) {
      const p = dataOff + (f * ch + c) * bps;
      let v;
      if (fmt === 3 && bits === 32) v = dv.getFloat32(p, true);
      else if (fmt === 3 && bits === 64) v = dv.getFloat64(p, true);
      else if (bits === 8) v = (dv.getUint8(p) - 128) / 128;
      else if (bits === 16) v = dv.getInt16(p, true) / 32768;
      else if (bits === 24) v = ((dv.getUint8(p) | (dv.getUint8(p + 1) << 8) | (dv.getInt8(p + 2) << 16))) / 8388608;
      else if (bits === 32) v = dv.getInt32(p, true) / 2147483648;
      else throw new Error(`Formato WAV no soportado (${bits} bits).`);
      out[c][f] = v;
    }
  }
  const channels = [];
  out.forEach((d, c) => { if (c !== tachChannel) channels.push({ name: `Canal ${c + 1}`, unit, data: d.map((v) => v * scale) }); });
  return new Recording(rate, channels, tachChannel >= 0 && tachChannel < ch ? out[tachChannel] : null, rpm, 'WAV');
}

// ── Protocolo serie (ver firmware/README): texto v1 o binario v2 con CRC ──
export function crc16(bytes, start = 0, end = bytes.length) {
  let crc = 0xffff;
  for (let i = start; i < end; i++) {
    crc ^= bytes[i] << 8;
    for (let k = 0; k < 8; k++) crc = crc & 0x8000 ? ((crc << 1) ^ 0x1021) & 0xffff : (crc << 1) & 0xffff;
  }
  return crc;
}

export class SerialStreamParser {
  constructor() {
    this.fs = 0; this.ch = 1; this.unit = 'g'; this.hasTach = false; this.binary = false; this.scale = 1; this.sensor = ''; this.names = [];
    this.buf = Array.from({ length: 17 }, () => []); this.raw = []; this.maxSeconds = 30;
    this.framesOk = 0; this.crcErrors = 0; this.lostFrames = 0; this.lastSeq = -1; this.lastMessage = '';
  }
  get count() { return this.buf[0].length; }
  get status() {
    if (!(this.fs > 0)) return 'esperando cabecera #VA…';
    return `${this.binary ? 'binario v2' : 'texto v1'} · ${this.ch} canal(es) · ${Math.round(this.fs)} Hz${this.sensor ? ' · ' + this.sensor : ''}` +
      (this.binary ? ` · tramas ${this.framesOk}, CRC mal ${this.crcErrors}, perdidas ${this.lostFrames}` : '');
  }
  feed(chunk) { this.feedBytes(new TextEncoder().encode(chunk)); }
  feedBytes(bytes) {
    for (const b of bytes) this.raw.push(b);
    const r = this.raw;
    let pos = 0;
    const indexOf = (v, from) => { for (let i = from; i < r.length; i++) if (r[i] === v) return i; return -1; };
    const lineAt = (a, b) => new TextDecoder().decode(Uint8Array.from(r.slice(a, b))).trim();
    while (pos < r.length) {
      if (this.binary) {
        if (r[pos] === 35 && pos + 2 < r.length && r[pos + 1] === 86 && r[pos + 2] === 65) {   // "#VA": nueva cabecera
          const nl = indexOf(10, pos); if (nl < 0) break;
          this.feedLine(lineAt(pos, nl)); pos = nl + 1; continue;
        }
        if (r[pos] !== 0xa5 || (pos + 1 < r.length && r[pos + 1] !== 0x5a)) { pos++; continue; }
        if (pos + 7 > r.length) break;
        const n = r[pos + 4] | (r[pos + 5] << 8), ch = r[pos + 6];
        if (ch !== this.ch || n === 0 || n > 4096) { pos++; this.crcErrors++; continue; }
        const tb = (n + 7) >> 3, len = 7 + n * ch * 2 + tb + 2;
        if (pos + len > r.length) break;
        const crc = r[pos + len - 2] | (r[pos + len - 1] << 8);
        if (crc16(r, pos + 2, pos + len - 2) !== crc) { pos++; this.crcErrors++; continue; }
        const seq = r[pos + 2] | (r[pos + 3] << 8);
        if (this.lastSeq >= 0) { const gap = (seq - this.lastSeq - 1 + 65536) % 65536; if (gap > 0 && gap < 30000) this.lostFrames += gap; }
        this.lastSeq = seq;
        let o = pos + 7;
        for (let i = 0; i < n; i++) for (let c = 0; c < ch; c++, o += 2) { let v = r[o] | (r[o + 1] << 8); if (v & 0x8000) v -= 0x10000; this.buf[c].push(v * this.scale); }
        if (this.hasTach) for (let i = 0; i < n; i++) this.buf[ch].push((r[o + (i >> 3)] >> (i & 7)) & 1);
        this.framesOk++;
        pos += len;
      } else {
        const nl = indexOf(10, pos);
        if (nl < 0) { if (r.length - pos > 4096) pos = r.length; break; }
        this.feedLine(lineAt(pos, nl)); pos = nl + 1;
      }
    }
    r.splice(0, Math.min(pos, r.length));
    this.trim();
  }
  feedLine(line) {
    if (!line) return;
    if (/^#VA/i.test(line)) {
      const v2 = /^#VA2/i.test(line);
      this.ch = 1; this.hasTach = false; this.scale = 1; this.sensor = ''; this.names = []; this.lastSeq = -1;
      for (const kv of line.slice(v2 ? 4 : 3).trim().split(/\s+/)) {
        const eq = kv.indexOf('='); if (eq < 0) continue;
        const k = kv.slice(0, eq).toLowerCase(), v = kv.slice(eq + 1);
        if (k === 'fs') this.fs = +v;
        else if (k === 'ch') this.ch = Math.max(1, Math.min(16, +v));
        else if (k === 'unit') this.unit = parseUnit(v) || this.unit;
        else if (k === 'tach') this.hasTach = v === '1';
        else if (k === 'scale') this.scale = +v;
        else if (k === 'sensor') this.sensor = v;
        else if (k === 'names') this.names = v.split(',');
      }
      this.binary = v2;
      this.buf.forEach((b) => (b.length = 0));
      return;
    }
    if (line.startsWith('#')) { this.lastMessage = line.replace(/^#\s*/, ''); return; }
    if (this.binary) return;
    const cells = line.split(',');
    const need = this.ch + (this.hasTach ? 1 : 0);
    if (cells.length < need) return;
    const vals = cells.slice(0, need).map(Number);
    if (vals.some((v) => !isFinite(v))) return;
    vals.forEach((v, i) => this.buf[i].push(v));
  }
  trim() {
    if (!(this.fs > 0)) return;
    const max = Math.floor(this.maxSeconds * this.fs), extra = this.buf[0].length - max;
    if (extra < max / 5) return;
    this.buf.forEach((b) => b.splice(0, Math.min(extra, b.length)));
  }
  clear() { this.buf.forEach((b) => (b.length = 0)); this.raw = []; this.lastSeq = -1; this.framesOk = this.crcErrors = this.lostFrames = 0; }
  toRecording(rpm = 0, last = 0) {
    if (!(this.fs > 0)) throw new Error('No se recibió la cabecera #VA con la frecuencia de muestreo.');
    const n = Math.min(...this.buf.slice(0, this.ch + (this.hasTach ? 1 : 0)).map((b) => b.length));
    const start = last > 0 && n > last ? n - last : 0;
    const take = (b) => Float64Array.from(b.slice(start, n));
    const channels = Array.from({ length: this.ch }, (_, i) => ({ name: this.names[i] || `Sensor ${i + 1}`, unit: this.unit, data: take(this.buf[i]) }));
    return new Recording(this.fs, channels, this.hasTach ? take(this.buf[this.ch]) : null, rpm, this.sensor ? 'Sensor ' + this.sensor : 'Puerto serie');
  }
}

export { normalizeAngle };
