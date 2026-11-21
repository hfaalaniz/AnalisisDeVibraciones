// Gráficos de la página del producto (espectro y diagrama polar de ejemplo).
{
  // Espectro de ejemplo (1X dominante y ruido) y diagrama polar, dibujados a tamaño real.
  const spec = document.getElementById('specFig'), pol = document.getElementById('polFig');
  let seed = 7; const rnd = () => (seed = seed * 16807 % 2147483647) / 2147483647;
  const noise = Array.from({ length: 240 }, () => 0.03 + rnd() * 0.06);
  const peaks = [[25, 6.35], [50, 0.42], [75, 0.21], [100, 0.12]];
  const drawSpec = () => {
    const W = spec.clientWidth, H = spec.clientHeight, L = 44, R = 12, T = 22, B = 28, w = W - L - R, h = H - T - B, fmax = 300, top = 7.5;
    const X = (f) => L + f / fmax * w, Y = (a) => T + h - a / top * h;
    let s = `<svg viewBox="0 0 ${W} ${H}" role="img" aria-label="Espectro con pico de 6,35 mm/s a 25 Hz">`;
    for (const v of [0, 2.5, 5, 7.5]) s += `<line x1="${L}" x2="${L + w}" y1="${Y(v)}" y2="${Y(v)}" stroke="var(--line)"/><text x="${L - 6}" y="${Y(v) + 4}" text-anchor="end">${String(v).replace('.', ',')}</text>`;
    for (let f = 0; f < fmax; f += W < 520 ? 100 : 50) s += `<text x="${X(f)}" y="${T + h + 18}" text-anchor="middle">${f}</text>`;
    for (let k = 1; k * 25 <= fmax; k++) if (W >= 520 || k <= 4) s += `<line x1="${X(k * 25)}" x2="${X(k * 25)}" y1="${T}" y2="${T + h}" stroke="var(--alt)" stroke-dasharray="3 4" opacity=".6"/><text x="${X(k * 25)}" y="${T - 6}" text-anchor="middle">${k}X</text>`;
    let d = '';
    for (let i = 0; i < noise.length; i++) {
      const f = i * fmax / noise.length;
      let a = noise[i];
      for (const [pf, pa] of peaks) a += pa * Math.exp(-((f - pf) ** 2) / 1.2);
      d += `${i ? 'L' : 'M'}${X(f).toFixed(1)},${Y(a).toFixed(1)}`;
    }
    s += `<path d="${d}" fill="none" stroke="var(--accent)" stroke-width="2"/>`;
    s += `<circle cx="${X(25)}" cy="${Y(6.35)}" r="4" fill="var(--ink)"/><text x="${X(25) + 10}" y="${Y(6.35) + 4}" style="fill:var(--ink)">6,35 mm/s a 25 Hz (1X)</text>`;
    spec.innerHTML = s + `<text x="${L + w}" y="${T + h + 18}" text-anchor="end">Hz</text></svg>`;
  };
  const drawPol = () => {
    const W = pol.clientWidth, H = pol.clientHeight, cx = W / 2, cy = H / 2, r = Math.min(W, H) / 2 - 26;
    const P = (rr, a) => [cx - rr * Math.sin(a * Math.PI / 180), cy - rr * Math.cos(a * Math.PI / 180)];
    let s = `<svg viewBox="0 0 ${W} ${H}" role="img" aria-label="Diagrama polar de balanceo">`;
    for (let k = 1; k <= 4; k++) s += `<circle cx="${cx}" cy="${cy}" r="${r * k / 4}" fill="none" stroke="var(--line)"/>`;
    for (let a = 0; a < 360; a += 30) { const [x, y] = P(r, a), [lx, ly] = P(r + 14, a); s += `<line x1="${cx}" y1="${cy}" x2="${x}" y2="${y}" stroke="var(--line)"/><text x="${lx}" y="${ly + 4}" text-anchor="middle">${a}°</text>`; }
    const arrow = (m, a, c, dash = '') => { const [x, y] = P(r * m / 7.5, a); return `<line x1="${cx}" y1="${cy}" x2="${x}" y2="${y}" stroke="${c}" stroke-width="3" ${dash} marker-end="url(#ah)" style="color:${c}"/>`; };
    s += `<defs><marker id="ah" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse"><path d="M0 0L10 5L0 10z" fill="context-stroke"/></marker></defs>`;
    s += arrow(6.35, 165, 'var(--ink)') + arrow(6.55, 130, '#e08e00') + arrow(0.6, 327, 'var(--alt)');
    const [wx, wy] = P(r * 0.85, 285);
    s += `<line x1="${cx}" y1="${cy}" x2="${wx}" y2="${wy}" stroke="var(--red)" stroke-dasharray="3 4" stroke-width="2"/><circle cx="${wx}" cy="${wy}" r="11" fill="var(--red)"/><text x="${wx}" y="${wy + 4}" text-anchor="middle" style="fill:#fff;stroke:none;font-weight:600">W</text>`;
    pol.innerHTML = s + '</svg>';
  };
  const ro1 = new ResizeObserver(drawSpec); ro1.observe(spec);
  const ro2 = new ResizeObserver(drawPol); ro2.observe(pol);
}

if ('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(() => {});
