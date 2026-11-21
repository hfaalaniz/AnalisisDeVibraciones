// Gráficos en canvas: forma de onda, espectro y diagrama polar. Se redibujan solos al cambiar de tamaño o de tema.

// Resuelve variables CSS (incluidas las que usan light-dark()) a un color concreto para el canvas.
let probe;
const css = (el, name) => {
  if (!probe) { probe = document.createElement('span'); probe.style.display = 'none'; document.body.appendChild(probe); }
  probe.style.color = `var(${name})`;
  return getComputedStyle(probe).color || '#000';
};

function setup(canvas) {
  const dpr = window.devicePixelRatio || 1;
  const W = canvas.clientWidth, H = canvas.clientHeight;
  if (canvas.width !== Math.round(W * dpr) || canvas.height !== Math.round(H * dpr)) {
    canvas.width = Math.round(W * dpr); canvas.height = Math.round(H * dpr);
  }
  const g = canvas.getContext('2d');
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.clearRect(0, 0, W, H);
  return { g, W, H };
}

const nice = (v) => {
  if (!(v > 0)) return 1;
  const p = 10 ** Math.floor(Math.log10(v));
  for (const m of [1, 2, 2.5, 5, 10]) if (m * p >= v) return m * p;
  return 10 * p;
};
const fmt = (v) => (Math.abs(v) >= 100 ? v.toFixed(0) : Math.abs(v) >= 10 ? v.toFixed(1) : v.toFixed(2)).replace('.', ',');

class Chart {
  constructor(canvas) {
    this.c = canvas;
    this.ro = new ResizeObserver(() => this.draw());
    this.ro.observe(canvas);
    this.mo = new MutationObserver(() => this.draw());
    this.mo.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
    matchMedia('(prefers-color-scheme: dark)').addEventListener?.('change', () => this.draw());
  }
  colors() {
    return { ink: css(this.c, '--ink'), mute: css(this.c, '--mute'), grid: css(this.c, '--grid'), accent: css(this.c, '--accent'), bg: css(this.c, '--panel'), warn: css(this.c, '--warn'), alt: css(this.c, '--alt') };
  }
}

/** Forma de onda con marcas del tacómetro. */
export class WaveChart extends Chart {
  set(data) { this.data = data; this.draw(); }
  draw() {
    const { g, W, H } = setup(this.c);
    const d = this.data;
    const col = this.colors();
    const L = 52, R = 12, T = 24, B = 26, w = W - L - R, h = H - T - B;
    if (w < 40 || h < 40) return;
    g.font = '13px Inter, system-ui, sans-serif';
    if (!d || !d.y?.length) { g.fillStyle = col.mute; g.fillText('Sin señal: adquirí o importá una medición.', L, T + h / 2); return; }
    // Mostrar hasta 6 vueltas para que la forma se lea
    const fs = d.fs, nShow = Math.min(d.y.length, d.rpm > 0 ? Math.round(fs * 6 * 60 / d.rpm) : d.y.length);
    let mx = 0; for (let i = 0; i < nShow; i++) mx = Math.max(mx, Math.abs(d.y[i]));
    const top = nice(mx * 1.05);
    const X = (i) => L + i / (nShow - 1) * w, Y = (v) => T + h / 2 - v / top * h / 2;
    g.strokeStyle = col.grid; g.lineWidth = 1; g.fillStyle = col.mute; g.textAlign = 'right'; g.textBaseline = 'middle';
    for (const v of [-top, -top / 2, 0, top / 2, top]) { g.beginPath(); g.moveTo(L, Y(v)); g.lineTo(L + w, Y(v)); g.stroke(); g.fillText(fmt(v), L - 6, Y(v)); }
    g.textAlign = 'center'; g.textBaseline = 'top';
    const dur = nShow / fs;
    const step = nice(dur / 6);
    for (let t = 0; t <= dur + 1e-9; t += step) g.fillText(`${fmt(t * 1000)} ms`, L + t / dur * w, T + h + 6);
    if (d.pulses) {
      g.strokeStyle = col.alt; g.setLineDash([4, 4]);
      for (const p of d.pulses) { if (p > dur) break; const x = L + p / dur * w; g.beginPath(); g.moveTo(x, T); g.lineTo(x, T + h); g.stroke(); }
      g.setLineDash([]);
    }
    g.strokeStyle = col.accent; g.lineWidth = 1.6; g.beginPath();
    const stride = Math.max(1, Math.floor(nShow / (w * 2)));
    for (let i = 0; i < nShow; i += stride) { const x = X(i), y = Y(d.y[i]); i ? g.lineTo(x, y) : g.moveTo(x, y); }
    g.stroke();
    g.fillStyle = col.mute; g.textAlign = 'left'; g.textBaseline = 'top';
    g.fillText(d.unit, 4, 2);
  }
}

/** Espectro con marcas de órdenes (1X, 2X…) y frecuencias de rodamiento. */
export class SpectrumChart extends Chart {
  set(data) { this.data = data; this.draw(); }
  draw() {
    const { g, W, H } = setup(this.c);
    const d = this.data, col = this.colors();
    const L = 52, R = 14, T = 26, B = 28, w = W - L - R, h = H - T - B;
    if (w < 40 || h < 40) return;
    g.font = '13px Inter, system-ui, sans-serif';
    if (!d) { g.fillStyle = col.mute; g.fillText('Sin espectro.', L, T + h / 2); return; }
    const fmax = d.fmax, n = d.freqs.length;
    let amax = 0; for (let i = 1; i < n && d.freqs[i] <= fmax; i++) amax = Math.max(amax, d.amps[i]);
    const top = nice(amax * 1.12);
    const X = (f) => L + f / fmax * w, Y = (a) => T + h - a / top * h;
    g.strokeStyle = col.grid; g.lineWidth = 1; g.fillStyle = col.mute; g.textAlign = 'right'; g.textBaseline = 'middle';
    for (let k = 0; k <= 4; k++) { const v = top * k / 4; g.beginPath(); g.moveTo(L, Y(v)); g.lineTo(L + w, Y(v)); g.stroke(); g.fillText(fmt(v), L - 6, Y(v)); }
    g.textAlign = 'center'; g.textBaseline = 'top';
    const fstep = nice(fmax / (W < 520 ? 4 : 8));
    for (let f = 0; f <= fmax + 1e-9; f += fstep) g.fillText(`${fmt(f)}`, X(f), T + h + 6);
    g.textAlign = 'right'; g.fillText('Hz', L + w, T + h + 6);
    // Órdenes
    if (d.f1 > 0) {
      g.setLineDash([3, 4]); g.strokeStyle = col.alt; g.fillStyle = col.alt; g.textAlign = 'center'; g.textBaseline = 'bottom';
      for (let k = 1; k * d.f1 <= fmax && k <= 12; k++) {
        const x = X(k * d.f1); g.beginPath(); g.moveTo(x, T); g.lineTo(x, T + h); g.stroke();
        if (W >= 520 || k <= 4) g.fillText(`${k}X`, x, T - 4);
      }
      g.setLineDash([]);
    }
    for (const m of d.markers || []) {
      if (m.f > fmax) continue;
      g.strokeStyle = col.warn; g.fillStyle = col.warn; g.setLineDash([2, 3]);
      g.beginPath(); g.moveTo(X(m.f), T + 14); g.lineTo(X(m.f), T + h); g.stroke(); g.setLineDash([]);
      g.textAlign = 'left'; g.textBaseline = 'top'; g.fillText(m.label, X(m.f) + 3, T + 14);
    }
    // Barras/línea del espectro
    g.strokeStyle = col.accent; g.lineWidth = 1.4; g.beginPath();
    let first = true;
    for (let i = 0; i < n && d.freqs[i] <= fmax; i++) {
      const x = X(d.freqs[i]), y = Y(d.amps[i]);
      if (first) { g.moveTo(x, y); first = false; } else g.lineTo(x, y);
    }
    g.stroke();
    // Pico 1X
    if (d.peak) {
      const x = X(d.peak.freq), y = Y(d.peak.amp);
      g.fillStyle = col.ink; g.beginPath(); g.arc(x, y, 3.5, 0, 7); g.fill();
      g.textAlign = x > L + w * 0.7 ? 'right' : 'left'; g.textBaseline = 'bottom';
      g.fillText(`${fmt(d.peak.amp)} ${d.unit} @ ${fmt(d.peak.freq)} Hz`, x + (g.textAlign === 'left' ? 8 : -8), y - 4);
    }
    g.fillStyle = col.mute; g.textAlign = 'left'; g.textBaseline = 'top'; g.fillText(`${d.unit} pico`, 4, 4);
  }
}

/** Diagrama polar de balanceo: lecturas (flechas) y masas (círculos). 0° arriba. */
export class PolarChart extends Chart {
  set(data) { this.data = data; this.draw(); }
  draw() {
    const { g, W, H } = setup(this.c);
    const d = this.data || { readings: [], masses: [], ccw: true }, col = this.colors();
    const cx = W / 2, cy = H / 2, r = Math.min(W, H) / 2 - 30;
    if (r < 40) return;
    g.font = '13px Inter, system-ui, sans-serif';
    const maxR = Math.max(1e-6, ...d.readings.map((x) => x.p.mag)), top = nice(maxR);
    const maxM = Math.max(1e-6, ...d.masses.map((x) => x.p.mag));
    const P = (rr, deg) => { const a = deg * Math.PI / 180, sx = d.ccw ? -1 : 1; return [cx + sx * rr * Math.sin(a), cy - rr * Math.cos(a)]; };
    g.strokeStyle = col.grid; g.lineWidth = 1; g.fillStyle = col.mute; g.textAlign = 'center'; g.textBaseline = 'middle';
    for (let k = 1; k <= 4; k++) { g.beginPath(); g.arc(cx, cy, r * k / 4, 0, 7); g.stroke(); }
    for (let a = 0; a < 360; a += 30) {
      const [x, y] = P(r, a); g.beginPath(); g.moveTo(cx, cy); g.lineTo(x, y); g.stroke();
      const [lx, ly] = P(r + 16, a); g.fillText(`${a}°`, lx, ly);
    }
    g.textAlign = 'left'; g.fillText(fmt(top), cx + 3, cy - r + 8);
    const arrow = (len, deg, color, width, dashed) => {
      const [x, y] = P(len, deg);
      g.strokeStyle = color; g.fillStyle = color; g.lineWidth = width; g.setLineDash(dashed ? [6, 4] : []);
      g.beginPath(); g.moveTo(cx, cy); g.lineTo(x, y); g.stroke(); g.setLineDash([]);
      const ang = Math.atan2(y - cy, x - cx);
      g.beginPath(); g.moveTo(x, y); g.lineTo(x - 10 * Math.cos(ang - 0.4), y - 10 * Math.sin(ang - 0.4)); g.lineTo(x - 10 * Math.cos(ang + 0.4), y - 10 * Math.sin(ang + 0.4)); g.closePath(); g.fill();
      return [x, y];
    };
    for (const it of d.readings) arrow(r * it.p.mag / top, it.p.ang, it.color, 2.4, it.dashed);
    for (const it of d.masses) {
      const [x, y] = P(r * 0.85 * it.p.mag / maxM, it.p.ang);
      g.strokeStyle = it.color; g.lineWidth = 1.5; g.setLineDash([2, 3]); g.beginPath(); g.moveTo(cx, cy); g.lineTo(x, y); g.stroke(); g.setLineDash([]);
      g.fillStyle = it.color; g.beginPath(); g.arc(x, y, 8, 0, 7); g.fill();
      g.fillStyle = col.bg; g.font = '600 11px Inter, system-ui, sans-serif'; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText('W', x, y + 0.5);
      g.font = '13px Inter, system-ui, sans-serif';
    }
  }
}

/**
 * Gráfico XY genérico: varias series, escala logarítmica opcional, marcas verticales, líneas horizontales y puntos.
 * data: { series: [{ x, y, color?, points?, dash?, label? }], xLabel, yLabel, xMin, xMax, yMin, yMax, logY, equal, markers: [{ x, label }], hlines: [{ y, label, color }], xFormat, empty }
 */
export class XYChart extends Chart {
  set(data) { this.data = data; this.draw(); }
  draw() {
    const { g, W, H } = setup(this.c);
    const d = this.data, col = this.colors();
    const L = 58, R = 14, T = 24, B = 34, w = W - L - R, h = H - T - B;
    if (w < 40 || h < 40) return;
    g.font = '13px Inter, system-ui, sans-serif';
    const series = (d?.series || []).filter((s) => s.x?.length);
    if (!series.length) { g.fillStyle = col.mute; g.fillText(d?.empty || 'Sin datos.', L, T + h / 2); return; }
    const tf = d.logY ? (v) => (v > 0 ? Math.log10(v) : NaN) : (v) => v;
    let x0 = d.xMin ?? Infinity, x1 = d.xMax ?? -Infinity, y0 = Infinity, y1 = -Infinity;
    for (const s of series) for (let i = 0; i < s.x.length; i++) {
      if (d.xMin == null) x0 = Math.min(x0, s.x[i]);
      if (d.xMax == null) x1 = Math.max(x1, s.x[i]);
    }
    for (const s of series) for (let i = 0; i < s.x.length; i++) {
      if (s.x[i] < x0 || s.x[i] > x1) continue;
      const v = tf(s.y[i]); if (Number.isFinite(v)) { y0 = Math.min(y0, v); y1 = Math.max(y1, v); }
    }
    for (const l of d.hlines || []) { const v = tf(l.y); if (Number.isFinite(v)) { y0 = Math.min(y0, v); y1 = Math.max(y1, v); } }
    if (!(y1 >= y0)) { y0 = 0; y1 = 1; }
    if (d.yMin != null) y0 = tf(d.yMin); else if (!d.logY && y0 > 0 && !d.equal) y0 = 0;
    if (d.yMax != null) y1 = tf(d.yMax);
    if (d.logY) { y0 = Math.max(y0, y1 - 8); }
    let sp = y1 - y0 || Math.abs(y1) || 1; y1 += sp * 0.06; if (d.equal || d.yMin == null && y0 < 0) y0 -= sp * 0.06;
    if (x1 <= x0) x1 = x0 + 1;
    if (d.equal) { // misma escala en X e Y (órbitas, Nyquist)
      const cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, ppu = Math.min(w / (x1 - x0), h / (y1 - y0));
      x0 = cx - w / ppu / 2; x1 = cx + w / ppu / 2; y0 = cy - h / ppu / 2; y1 = cy + h / ppu / 2;
    }
    const X = (v) => L + (v - x0) / (x1 - x0) * w, Y = (v) => T + h - (tf(v) - y0) / (y1 - y0) * h;
    g.strokeStyle = col.grid; g.lineWidth = 1; g.fillStyle = col.mute;
    // Eje Y
    g.textAlign = 'right'; g.textBaseline = 'middle';
    if (d.logY) {
      for (let e = Math.ceil(y0); e <= y1; e++) { const py = T + h - (e - y0) / (y1 - y0) * h; g.beginPath(); g.moveTo(L, py); g.lineTo(L + w, py); g.stroke(); g.fillText(`1e${e}`, L - 6, py); }
    } else {
      const st = nice((y1 - y0) / 5);
      for (let v = Math.ceil(y0 / st) * st; v <= y1 + 1e-12; v += st) { const py = T + h - (v - y0) / (y1 - y0) * h; g.beginPath(); g.moveTo(L, py); g.lineTo(L + w, py); g.stroke(); g.fillText(fmt(Math.abs(v) < st * 1e-9 ? 0 : v), L - 6, py); }
    }
    // Eje X
    g.textAlign = 'center'; g.textBaseline = 'top';
    const sx = nice((x1 - x0) / (W < 520 ? 4 : 8));
    for (let v = Math.ceil(x0 / sx) * sx; v <= x1 + 1e-12; v += sx) {
      const px = X(v); g.beginPath(); g.moveTo(px, T); g.lineTo(px, T + h); g.stroke();
      g.fillText(d.xFormat ? d.xFormat(v) : fmt(Math.abs(v) < sx * 1e-9 ? 0 : v), px, T + h + 6);
    }
    if (d.xLabel) { g.textAlign = 'right'; g.fillText(d.xLabel, L + w, T + h + 20); }
    if (d.yLabel) { g.textAlign = 'left'; g.fillText(d.yLabel, 4, 4); }
    g.save(); g.beginPath(); g.rect(L, T, w, h); g.clip();
    for (const m of d.markers || []) {
      if (m.x < x0 || m.x > x1) continue;
      g.strokeStyle = col.warn; g.fillStyle = col.warn; g.setLineDash([2, 3]);
      g.beginPath(); g.moveTo(X(m.x), T); g.lineTo(X(m.x), T + h); g.stroke(); g.setLineDash([]);
      g.textAlign = 'left'; g.textBaseline = 'top'; g.fillText(m.label, X(m.x) + 3, T + 2);
    }
    for (const l of d.hlines || []) {
      g.strokeStyle = l.color || col.warn; g.fillStyle = g.strokeStyle; g.setLineDash([6, 4]); g.lineWidth = 1.3;
      g.beginPath(); g.moveTo(L, Y(l.y)); g.lineTo(L + w, Y(l.y)); g.stroke(); g.setLineDash([]);
      g.textAlign = 'right'; g.textBaseline = 'bottom'; g.fillText(l.label || '', L + w - 4, Y(l.y) - 2);
    }
    series.forEach((s, k) => {
      const c = s.color || [col.accent, col.alt, col.warn, col.ink][k % 4];
      g.strokeStyle = c; g.fillStyle = c; g.lineWidth = 1.5; g.setLineDash(s.dash ? [6, 4] : []);
      const n = s.x.length, stride = s.points ? 1 : Math.max(1, Math.floor(n / (w * 3)));
      if (!s.points || s.line) {
        g.beginPath(); let first = true;
        for (let i = 0; i < n; i += stride) {
          const yy = Y(s.y[i]); if (!Number.isFinite(yy)) { first = true; continue; }
          first ? g.moveTo(X(s.x[i]), yy) : g.lineTo(X(s.x[i]), yy); first = false;
        }
        g.stroke();
      }
      g.setLineDash([]);
      if (s.points) for (let i = 0; i < n; i++) { const yy = Y(s.y[i]); if (Number.isFinite(yy)) { g.beginPath(); g.arc(X(s.x[i]), yy, 3, 0, 7); g.fill(); } }
    });
    g.restore();
    const labeled = series.filter((s) => s.label);
    if (labeled.length > 1 || labeled.some((s) => s.points)) {
      let lx = L + 8; g.textAlign = 'left'; g.textBaseline = 'middle';
      labeled.forEach((s, k) => {
        const c = s.color || [col.accent, col.alt, col.warn, col.ink][series.indexOf(s) % 4];
        g.fillStyle = c; g.fillRect(lx, T + 8, 12, 3); g.fillStyle = col.ink; g.fillText(s.label, lx + 16, T + 9); lx += 28 + g.measureText(s.label).width;
      });
    }
  }
}

/** Mapa de color (espectrograma): amps[fila tiempo][columna frecuencia]. */
export class HeatChart extends Chart {
  set(data) { this.data = data; this.draw(); }
  draw() {
    const { g, W, H } = setup(this.c);
    const d = this.data, col = this.colors();
    const L = 58, R = 14, T = 24, B = 34, w = W - L - R, h = H - T - B;
    if (w < 40 || h < 40) return;
    g.font = '13px Inter, system-ui, sans-serif';
    if (!d?.amps?.length) { g.fillStyle = col.mute; g.fillText(d?.empty || 'Sin datos.', L, T + h / 2); return; }
    const rows = d.amps.length, cols = d.freqs.length;
    let mx = 0; for (const r of d.amps) for (const v of r) mx = Math.max(mx, v);
    const img = g.createImageData(cols, rows), lo = mx * 1e-3;
    for (let i = 0; i < rows; i++) for (let k = 0; k < cols; k++) {
      const t = Math.max(0, Math.min(1, Math.log10(Math.max(d.amps[i][k], lo) / lo) / 3)), o = ((rows - 1 - i) * cols + k) * 4;
      // viridis aproximado
      img.data[o] = Math.round(255 * Math.min(1, Math.max(0, -0.3 + 1.6 * t * t))); img.data[o + 1] = Math.round(255 * (0.05 + 0.85 * t)); img.data[o + 2] = Math.round(255 * (0.35 + 0.4 * Math.sin(Math.PI * t) - 0.2 * t)); img.data[o + 3] = 255;
    }
    const off = document.createElement('canvas'); off.width = cols; off.height = rows; off.getContext('2d').putImageData(img, 0, 0);
    g.imageSmoothingEnabled = false; g.drawImage(off, L, T, w, h);
    g.fillStyle = col.mute; g.textAlign = 'center'; g.textBaseline = 'top';
    const fmax = d.freqs[cols - 1], sx = nice(fmax / (W < 520 ? 4 : 8));
    for (let f = 0; f <= fmax + 1e-9; f += sx) g.fillText(fmt(f), L + f / fmax * w, T + h + 6);
    g.textAlign = 'right'; g.fillText('Hz', L + w, T + h + 20);
    g.textBaseline = 'middle';
    const yv = d.y, yl = yv[0], yh = yv[yv.length - 1];
    for (let k = 0; k <= 4; k++) { const v = yl + (yh - yl) * k / 4; g.fillText(fmt(v), L - 6, T + h - k / 4 * h); }
    g.textAlign = 'left'; g.textBaseline = 'top'; g.fillText(d.yLabel || '', 4, 4);
  }
}
