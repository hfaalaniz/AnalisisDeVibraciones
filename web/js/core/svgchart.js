// Gráfico XY en SVG para informes (vectorial, nítido al imprimir). Equivalente a VibrationAnalysis.Core.Report.SvgChart (C#).
const N = (v) => String(Math.round(v * 100) / 100);
const E = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
let clipId = 0;

export function fmtNum(v) {
  const a = Math.abs(v);
  if (a === 0) return '0';
  if (a >= 100000 || a < 0.001) return v.toExponential(2).replace('.', ',').replace(/e\+?/, 'E');
  const d = a >= 100 ? 0 : a >= 10 ? 1 : a >= 1 ? 2 : 3;
  return v.toLocaleString('es-AR', { maximumFractionDigits: d });
}

function ticks(min, max, target) {
  const span = max - min, r = [];
  if (!(span > 0) || !Number.isFinite(span)) return r;
  const raw = span / target, mag = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 2.5, 5, 10].map((k) => k * mag).find((s) => s >= raw);
  for (let v = Math.ceil(min / step) * step; v <= max + step * 1e-9; v += step) r.push(Math.abs(v) < step * 1e-9 ? 0 : v);
  return r;
}

function reduce(s, xMin, xMax, width) {
  const idx = [];
  for (let i = 0; i < s.x.length; i++) if (s.x[i] >= xMin && s.x[i] <= xMax && Number.isFinite(s.y[i])) idx.push(i);
  if (idx.length <= width * 2) return idx.map((i) => [s.x[i], s.y[i]]);
  const r = [], bucket = Math.ceil(idx.length / width);
  for (let b = 0; b < idx.length; b += bucket) {
    let lo = idx[b], hi = idx[b];
    for (let k = b; k < Math.min(b + bucket, idx.length); k++) { if (s.y[idx[k]] < s.y[lo]) lo = idx[k]; if (s.y[idx[k]] > s.y[hi]) hi = idx[k]; }
    const a = [s.x[lo], s.y[lo]], c = [s.x[hi], s.y[hi]];
    if (lo <= hi) r.push(a, c); else r.push(c, a);
  }
  return r;
}

/**
 * spec: { title, xLabel, yLabel, series: [{ x, y, color, label, points }], markers: [{ x, label, color }],
 *         hlines: [{ y, label, color }], xFormat, xMin, xMax, yMin, width = 760, height = 260 }
 */
export function renderSvgChart(c) {
  const left = 64, right = 16, top = 30, bottom = 44, w = c.width || 760, h = c.height || 260, pw = w - left - right, ph = h - top - bottom;
  const all = (c.series || []).filter((s) => s.x.length && s.x.length === s.y.length);
  let xMin = c.xMin ?? (all.length ? Math.min(...all.map((s) => Math.min(...s.x))) : 0);
  let xMax = c.xMax ?? (all.length ? Math.max(...all.map((s) => Math.max(...s.x))) : 1);
  if (xMax <= xMin) xMax = xMin + 1;
  let yMin = Infinity, yMax = -Infinity;
  for (const s of all) for (let i = 0; i < s.x.length; i++) if (s.x[i] >= xMin && s.x[i] <= xMax && Number.isFinite(s.y[i])) { yMin = Math.min(yMin, s.y[i]); yMax = Math.max(yMax, s.y[i]); }
  for (const l of c.hlines || []) if (Number.isFinite(l.y)) { yMin = Math.min(yMin, l.y); yMax = Math.max(yMax, l.y); }
  if (yMin > yMax) { yMin = 0; yMax = 1; }
  if (c.yMin != null) yMin = c.yMin; else if (yMin > 0) yMin = 0;
  let span = yMax - yMin;
  if (span <= 0) span = Math.abs(yMax) > 0 ? Math.abs(yMax) : 1;
  yMax += span * 0.08;
  const X = (v) => left + (v - xMin) / (xMax - xMin) * pw, Y = (v) => top + ph - (v - yMin) / (yMax - yMin) * ph;
  let s = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${w} ${h}" width="100%" role="img" aria-label="${E(c.title)}" style="font-family:system-ui,Segoe UI,Arial,sans-serif;font-size:11px">`;
  s += `<rect x="0" y="0" width="${w}" height="${h}" fill="#fff"/>`;
  if (c.title) s += `<text x="${left}" y="18" font-weight="600" font-size="12" fill="#1d2327">${E(c.title)}</text>`;
  for (const v of ticks(xMin, xMax, Math.max(2, Math.floor(pw / 95)))) {
    const px = X(v);
    s += `<line x1="${N(px)}" y1="${top}" x2="${N(px)}" y2="${top + ph}" stroke="#e3e9eb"/><text x="${N(px)}" y="${top + ph + 14}" text-anchor="middle" fill="#5f6b70">${E(c.xFormat ? c.xFormat(v) : fmtNum(v))}</text>`;
  }
  for (const v of ticks(yMin, yMax, Math.max(3, Math.floor(ph / 45)))) {
    const py = Y(v);
    s += `<line x1="${left}" y1="${N(py)}" x2="${left + pw}" y2="${N(py)}" stroke="#e3e9eb"/><text x="${left - 5}" y="${N(py + 4)}" text-anchor="end" fill="#5f6b70">${E(fmtNum(v))}</text>`;
  }
  s += `<rect x="${left}" y="${top}" width="${pw}" height="${ph}" fill="none" stroke="#9aa7ac"/>`;
  if (c.xLabel) s += `<text x="${left + pw / 2}" y="${h - 8}" text-anchor="middle" fill="#1d2327">${E(c.xLabel)}</text>`;
  if (c.yLabel) s += `<text transform="translate(14 ${top + ph / 2}) rotate(-90)" text-anchor="middle" fill="#1d2327">${E(c.yLabel)}</text>`;
  const id = ++clipId;
  s += `<clipPath id="vclip${id}"><rect x="${left}" y="${top}" width="${pw}" height="${ph}"/></clipPath><g clip-path="url(#vclip${id})">`;
  for (const l of (c.hlines || []).filter((q) => Number.isFinite(q.y))) {
    const py = Y(l.y);
    s += `<line x1="${left}" y1="${N(py)}" x2="${left + pw}" y2="${N(py)}" stroke="${l.color}" stroke-width="1.3" stroke-dasharray="6 4"/><text x="${left + pw - 4}" y="${N(py - 4)}" text-anchor="end" fill="${l.color}">${E(l.label)}</text>`;
  }
  for (const m of (c.markers || []).filter((q) => q.x >= xMin && q.x <= xMax)) {
    const px = X(m.x);
    s += `<line x1="${N(px)}" y1="${top}" x2="${N(px)}" y2="${top + ph}" stroke="${m.color}" stroke-dasharray="4 3" opacity="0.8"/>`;
    if (m.label) s += `<text x="${N(px + 3)}" y="${top + 11}" fill="${m.color}">${E(m.label)}</text>`;
  }
  for (const se of all) {
    const pts = reduce(se, xMin, xMax, pw), color = se.color || '#0e7c86';
    if (se.points) for (const [x, y] of pts) s += `<circle cx="${N(X(x))}" cy="${N(Y(y))}" r="3" fill="${color}"/>`;
    if (pts.length > 1) s += `<polyline fill="none" stroke="${color}" stroke-width="1.4" stroke-linejoin="round" points="${pts.map(([x, y]) => `${N(X(x))},${N(Y(y))}`).join(' ')}"/>`;
  }
  s += '</g>';
  let lx = left + 8;
  for (const se of all.filter((q) => q.label)) {
    s += `<rect x="${lx}" y="${top + ph - 16}" width="10" height="3" fill="${se.color || '#0e7c86'}"/><text x="${lx + 14}" y="${top + ph - 11}" fill="#1d2327">${E(se.label)}</text>`;
    lx += 24 + se.label.length * 6;
  }
  return s + '</svg>';
}
