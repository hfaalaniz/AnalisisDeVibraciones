// Pestañas profesionales de la versión web: análisis avanzado, órdenes/transitorios y tendencias con alarmas.
// Usa el estado de app.js a través de un "puente" (bridge) para no duplicar la adquisición.
import { convertUnits, UNITS, spectrum } from '../core/dsp.js';
import { bearingOrders } from '../core/analysis.js';
import {
  averaged, psd, zoom, cepstrum, envelopeSpectrum, spectralKurtosis, autoEnvelopeBand, butterworth,
  orderSpectrum, track, criticalSpeeds, spectrogram, unwrap,
} from '../core/advanced.js';
import { FEATURES, extractFeatures, evaluate, series, fit, worstLevel, TrendStore } from '../core/trend.js';
import { XYChart, HeatChart } from './charts.js';

export function initPro(b) {
  const $ = (id) => document.getElementById(id);
  const fx = b.fx;
  const charts = {
    adv: new XYChart($('cAdv')), ord: new XYChart($('cOrd')), ordPh: new XYChart($('cOrdPh')),
    heat: new HeatChart($('cOrdHeat')), trend: new XYChart($('cTrend')),
  };
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const LV = ['<span class="lvl0">Normal</span>', '<span class="lvl1">Alerta</span>', '<span class="lvl2">Peligro</span>'];
  const rec = () => b.recording();
  const ch = () => { const r = rec(); return r ? r.channels[Math.min(b.channel(), r.channels.length - 1)] : null; };
  const rpmNow = () => rec()?.measuredRpm() || b.machine().rpm;
  const signal = (unit) => { const r = rec(), c = ch(); return convertUnits(c.data, r.fs, c.unit, unit, UNITS[unit].order === UNITS[c.unit].order ? 0 : 2); };
  const show = (attr, mode) => document.querySelectorAll(`[data-${attr}]`).forEach((e) => (e.hidden = !e.dataset[attr].split(' ').includes(mode)));

  // ───────── Análisis avanzado ─────────
  let advLast = null;
  const machineMarkers = (f1) => {
    const m = b.machine(), mk = [];
    if (f1 > 0) for (let k = 1; k <= 3; k++) mk.push({ x: k * f1, label: `${k}X` });
    if (m.bearing?.balls > 0 && f1 > 0) { const o = bearingOrders(m.bearing); mk.push({ x: o.bpfo * f1, label: 'BPFO' }, { x: o.bpfi * f1, label: 'BPFI' }, { x: o.bsf * f1, label: 'BSF' }); }
    return mk;
  };
  function peaksTable(x, y, unit, n = 10) {
    const idx = [];
    for (let i = 1; i < y.length - 1; i++) if (y[i] > y[i - 1] && y[i] >= y[i + 1]) idx.push(i);
    idx.sort((a, c) => y[c] - y[a]);
    const f1 = rpmNow() / 60;
    $('aPeaks').innerHTML = `<thead><tr><th>${unit.x}</th><th>${unit.y}</th>${f1 > 0 && unit.orders ? '<th>Orden</th>' : ''}</tr></thead><tbody>` +
      idx.slice(0, n).map((i) => `<tr><td class="n">${fx(x[i], 3)}</td><td class="n">${fx(y[i], 4)}</td>${f1 > 0 && unit.orders ? `<td class="n">${fx(x[i] / f1, 2)}X</td>` : ''}</tr>`).join('') + '</tbody>';
  }
  function runAdvanced() {
    const r = rec();
    if (!r) { charts.adv.set({ empty: 'Adquirí o abrí una señal en la pestaña Análisis.' }); return; }
    const mode = $('aMode').value, unit = $('aUnit').value, win = $('aWin').value, fs = r.fs, f1 = rpmNow() / 60;
    const block = Math.min(+$('aBlock').value, r.length), ov = (+$('aOverlap').value || 0) / 100, fmaxIn = +$('aMax').value || 0;
    const sym = UNITS[unit].symbol, logY = $('aLog').checked;
    try {
      let x, y, title, sub, xLabel = 'Hz', yLabel = `${sym} pico`, markers = machineMarkers(f1), xMax = fmaxIn || null, orders = true;
      if (mode === 'avg') {
        const s = averaged(signal(unit), fs, block, ov, win, $('aAvg').value);
        x = s.freqs; y = s.amps; title = s.kind; sub = `${s.averages} promedios · resolución ${fx(s.resolution, 3)} Hz`;
      } else if (mode === 'psd') {
        const p = psd(signal(unit), fs, block, ov, win);
        x = p.freqs; y = p.psd; title = 'Densidad espectral de potencia'; yLabel = `${sym}²/Hz`;
        sub = `${p.averages} promedios · ${fx(p.resolution, 3)} Hz · RMS 10–1000 Hz = ${fx(p.bandRms(10, Math.min(1000, fs / 2)), 3)} ${sym}`;
      } else if (mode === 'zoom') {
        const z = zoom(signal(unit), fs, +$('aCenter').value, +$('aSpan').value, win);
        x = z.freqs; y = z.amps; title = z.kind; sub = `Resolución ${fx(z.resolution, 4)} Hz`; xMax = null;
      } else if (mode === 'env') {
        const e = envelopeSpectrum(signal('g'), fs, +$('aLow').value, +$('aHigh').value, win);
        x = e.freqs; y = e.amps; title = e.kind; yLabel = 'g pico'; sub = `Resolución ${fx(e.resolution, 3)} Hz`;
        xMax = fmaxIn || Math.min(fs / 2, Math.max(10 * f1, 500));
      } else if (mode === 'cep') {
        const c = cepstrum(signal(unit), fs, win);
        x = c.quefrency.map((q) => q * 1000); y = c.values; title = 'Cepstrum real'; xLabel = 'ms'; yLabel = 'cepstrum'; orders = false;
        markers = f1 > 0 ? [1, 2, 3].map((k) => ({ x: k * 1000 / f1, label: `${k}/1X` })) : [];
        xMax = fmaxIn ? 1000 / fmaxIn : Math.min(x[x.length - 1], f1 > 0 ? 4500 / f1 : 200);
        const dom = c.dominant(0.002, x[x.length - 1] / 1000);
        sub = `Quefrencia dominante ${fx(dom.quefrency * 1000, 2)} ms (${fx(1 / Math.max(dom.quefrency, 1e-9), 2)} Hz)` + (f1 > 0 ? ` · prominencia de la familia 1X: ${fx(c.familyProminence(1 / f1), 1)}` : '');
      } else {
        const k = spectralKurtosis(signal('g'), fs);
        x = k.freqs; y = k.kurtosis; title = 'Curtosis espectral'; yLabel = 'curtosis'; orders = false;
        const band = autoEnvelopeBand(signal('g'), fs, f1, b.machine().bearing?.balls > 0 ? bearingOrders(b.machine().bearing).bpfi * f1 : 6 * f1);
        sub = `${k.segments} segmentos · banda sugerida para envolvente ${fx(band.low, 0)}–${fx(band.high, 0)} Hz`; markers = [{ x: band.low, label: 'desde' }, { x: band.high, label: 'hasta' }];
      }
      advLast = { x, y, xLabel, yLabel, title };
      charts.adv.set({ series: [{ x, y }], xLabel, yLabel, logY, xMax, markers, yMin: mode === 'cep' || mode === 'sk' ? null : undefined });
      $('aTitle').textContent = title; $('aSub').textContent = sub;
      peaksTable(x, y, { x: xLabel, y: yLabel, orders });
    } catch (err) { charts.adv.set({ empty: 'No se pudo calcular: ' + err.message }); $('aPeaks').innerHTML = ''; }
  }
  $('aMode').addEventListener('change', () => { show('a', $('aMode').value); if (['env', 'sk'].includes($('aMode').value)) $('aUnit').value = 'g'; runAdvanced(); });
  ['aUnit', 'aWin', 'aBlock', 'aAvg', 'aLog'].forEach((id) => $(id).addEventListener('change', runAdvanced));
  $('aRun').addEventListener('click', runAdvanced);
  $('aSuggest').addEventListener('click', () => {
    const r = rec(); if (!r) return;
    const f1 = rpmNow() / 60, m = b.machine();
    const band = autoEnvelopeBand(signal('g'), r.fs, f1, m.bearing?.balls > 0 ? bearingOrders(m.bearing).bpfi * f1 : 6 * f1);
    $('aLow').value = Math.round(band.low); $('aHigh').value = Math.round(band.high); runAdvanced();
  });
  $('aCenter1X').addEventListener('click', () => { $('aCenter').value = (rpmNow() / 60).toFixed(2); runAdvanced(); });
  $('aCsv').addEventListener('click', () => {
    if (!advLast) return b.toast('Calculá un análisis primero.');
    const rows = [`${advLast.xLabel};${advLast.yLabel}`, ...Array.from(advLast.x, (v, i) => `${v};${advLast.y[i]}`)];
    b.download(`${advLast.title.replace(/\W+/g, '_')}.csv`, rows.join('\n'), 'text/csv');
  });
  show('a', 'avg');

  // ───────── Órdenes y transitorios ─────────
  function runOrders() {
    const r = rec();
    const mode = $('oMode').value, unit = $('oUnit').value, sym = UNITS[unit].symbol;
    $('oPhaseBox').hidden = mode !== 'bode';
    $('cOrdHeat').hidden = mode !== 'sgram'; $('cOrd').hidden = mode === 'sgram';
    $('oTitle').textContent = $('oMode').selectedOptions[0].textContent; $('oSub').textContent = '';
    if (!r) { charts.ord.set({ empty: 'Adquirí una señal en la pestaña Análisis.' }); return; }
    const p = r.pulses(), fs = r.fs, needTach = mode !== 'sgram' && mode !== 'orbit';
    if (needTach && p.length < 3) { charts.ord.set({ empty: 'Este análisis necesita tacómetro (al menos 3 pulsos). Usá el simulador o un CSV con columna de tacómetro.' }); $('oResults').textContent = '—'; return; }
    try {
      if (mode === 'orders') {
        const s = orderSpectrum(signal(unit), fs, p, +$('oMaxOrder').value || 20);
        charts.ord.set({ series: [{ x: s.freqs, y: s.amps }], xLabel: 'orden', yLabel: `${sym} pico` });
        $('oTitle').textContent = s.kind; $('oSub').textContent = `Resolución ${fx(s.resolution, 3)} órdenes`;
        const top = [1, 2, 3].map((k) => `${k}X: ${fx(s.peakNear(k, 0.1).amp, 3)} ${sym}`);
        $('oResults').innerHTML = top.join('<br>');
      } else if (mode === 'bode' || mode === 'nyquist') {
        const tr = track(signal(unit), fs, p, +$('oOrder').value || 1, +$('oRevs').value || 8);
        const pts = tr.byRpm(), rpm = pts.map((q) => q.rpm), amp = pts.map((q) => q.amp), ph = unwrap(pts.map((q) => q.phase));
        const crit = criticalSpeeds(tr);
        const mk = crit.map((c) => ({ x: c.rpm, label: `crítica ${fx(c.rpm, 0)}` }));
        if (mode === 'bode') {
          charts.ord.set({ series: [{ x: rpm, y: amp }], xLabel: 'rpm', yLabel: `${sym} pico ${tr.order}X`, markers: mk });
          charts.ordPh.set({ series: [{ x: rpm, y: ph, color: undefined }], xLabel: 'rpm', yLabel: '° retraso', markers: mk, yMin: Math.min(...ph) });
        } else {
          const xs = pts.map((q) => q.amp * Math.cos(-q.phase * Math.PI / 180)), ys = pts.map((q) => q.amp * Math.sin(-q.phase * Math.PI / 180));
          charts.ord.set({ series: [{ x: xs, y: ys, points: true, line: true }], xLabel: `${sym} (real)`, yLabel: `${sym} (imag.)`, equal: true });
        }
        $('oTitle').textContent = mode === 'bode' ? `Bode ${tr.order}X` : `Nyquist ${tr.order}X`;
        $('oSub').textContent = `${pts.length} bloques · ${fx(Math.min(...rpm), 0)}–${fx(Math.max(...rpm), 0)} rpm`;
        $('oResults').innerHTML = crit.length
          ? crit.map((c) => `<b>Velocidad crítica ${fx(c.rpm, 0)} rpm</b><br>amplitud ${fx(c.amp, 3)} ${sym} · giro de fase ${fx(c.phaseShift, 0)}° · Q ≈ ${fx(c.q, 1)}${c.q > 8 ? ' (poco amortiguada)' : ''}`).join('<br><br>')
          : 'Sin velocidades críticas en el barrido (hace falta un arranque o parada que pase por la resonancia).';
      } else if (mode === 'sgram') {
        const blk = Math.min(+$('oBlock').value, r.length);
        const sg = spectrogram(signal(unit), fs, blk, 0.75, 'hann', p.length >= 2 ? p : null, +$('oMax').value || Math.min(fs / 2, Math.max(500, 10 * rpmNow() / 60)));
        const hasRpm = sg.rpm.some((v) => v > 0);
        charts.heat.set({ amps: sg.amps, freqs: sg.freqs, y: hasRpm ? sg.rpm : sg.times, yLabel: hasRpm ? 'rpm' : 's' });
        $('oTitle').textContent = 'Espectrograma'; $('oSub').textContent = `${sg.times.length} espectros · bloque ${blk}`;
        $('oResults').textContent = 'Las líneas inclinadas que siguen la velocidad son órdenes; las horizontales que se iluminan al pasar son resonancias.';
      } else {
        if (r.channels.length < 2) throw new Error('La órbita necesita dos canales (sensores a 90°). En el simulador, elegí 2 planos en Balanceo.');
        let x = convertUnits(r.channels[0].data, fs, r.channels[0].unit, unit, 2), y = convertUnits(r.channels[1].data, fs, r.channels[1].unit, unit, 2);
        const f1 = rpmNow() / 60;
        if ($('oFilter').checked && f1 > 0) { const bp = butterworth('band', 4, fs, f1 * 0.7, Math.min(f1 * 1.3, fs * 0.45)); x = bp.filtfilt(x); y = bp.filtfilt(y); }
        const n = Math.min(x.length, f1 > 0 ? Math.round(fs * 4 / f1) : 4096), s0 = Math.floor(x.length / 2);
        charts.ord.set({ series: [{ x: x.slice(s0, s0 + n), y: y.slice(s0, s0 + n) }], xLabel: `X (${sym})`, yLabel: `Y (${sym})`, equal: true });
        $('oTitle').textContent = 'Órbita'; $('oSub').textContent = `${r.channels[0].name} / ${r.channels[1].name} · 4 vueltas`;
        $('oResults').textContent = 'Elíptica: desbalance o anisotropía de apoyos. Forma de 8 o banana: desalineación. Lazos internos: rozamiento o remolino de aceite.';
      }
    } catch (err) { charts.ord.set({ empty: 'No se pudo calcular: ' + err.message }); $('oResults').textContent = err.message; }
  }
  $('oMode').addEventListener('change', () => { show('o', $('oMode').value); runOrders(); });
  ['oUnit', 'oBlock', 'oFilter'].forEach((id) => $(id).addEventListener('change', runOrders));
  $('oRun').addEventListener('click', runOrders);
  $('oRunUp').addEventListener('click', () => { b.acquireRunUp(); if ($('oMode').value === 'orders') $('oMode').value = 'bode'; show('o', $('oMode').value); runOrders(); });
  show('o', 'orders');

  // ───────── Tendencias ─────────
  const store = new TrendStore();
  let selRec = null;
  const curPoint = () => store.point($('tPoint').value) || store.db.points[0];
  $('tFeature').innerHTML = Object.entries(FEATURES).map(([k, v]) => `<option value="${k}">${v}</option>`).join('');
  function fillPoints() {
    const id = $('tPoint').value;
    $('tPoint').innerHTML = store.db.points.map((p) => `<option value="${p.id}">${esc(p.name)}</option>`).join('');
    if (store.point(id)) $('tPoint').value = id;
  }
  function fillChannels() {
    const r = rec(), prev = $('tChannel').value;
    $('tChannel').innerHTML = r ? r.channels.map((c, i) => `<option value="${i}">${esc(c.name)}</option>`).join('') : '';
    if (prev && r && +prev < r.channels.length) $('tChannel').value = prev;
  }
  function lastAlarms(pt) {
    const h = store.history(pt.id);
    if (!h.length) return [];
    return evaluate(h[h.length - 1], pt, b.machine().machineClass, h);
  }
  function renderTrend() {
    const pt = curPoint();
    if (!pt) return;
    const h = store.history(pt.id), feat = $('tFeature').value, s = series(h, feat);
    const al = lastAlarms(pt), a = al.find((q) => q.feature === feat);
    const ft = fit(s);
    const ser = [{ x: s.map((q) => q.time), y: s.map((q) => q.value), points: true, line: true, label: 'Medición' }];
    let forecast = '';
    if (ft && s.length) {
      const t1 = s[s.length - 1].time, t2 = t1 + 90 * 86400000, xs = [];
      for (let k = 0; k <= 30; k++) xs.push(t1 + (t2 - t1) * k / 30);
      ser.push({ x: xs, y: xs.map(ft.valueAt), dash: true, label: 'Pronóstico 90 días' });
      const lim = a ? [['alerta', a.alert], ['peligro', a.danger]] : [];
      const cross = lim.map(([n, v]) => [n, ft.crossing(v)]).filter(([, t]) => t && t > t1);
      forecast = `Ajuste ${ft.exponential ? 'exponencial' : 'lineal'} (R² ${fx(ft.r2, 2)}).` + (cross.length ? ' ' + cross.map(([n, t]) => `Llega a ${n} ≈ ${new Date(t).toLocaleDateString('es-AR')}`).join(' · ') + '.' : '');
    }
    const day = (t) => new Date(t).toLocaleDateString('es-AR', { day: '2-digit', month: '2-digit' });
    charts.trend.set({
      series: s.length ? ser : [], empty: 'Sin mediciones de este indicador: guardá una medición o cargá la demo.', xFormat: day, yLabel: FEATURES[feat],
      hlines: a ? [{ y: a.alert, label: 'Alerta', color: '#d28a00' }, { y: a.danger, label: 'Peligro', color: '#c62828' }] : [],
    });
    $('tTitle').textContent = `${pt.name} · ${FEATURES[feat]}`;
    $('tSub').textContent = `${h.length} medición(es). ${forecast}`;
    $('tAlarms').innerHTML = al.length
      ? `<thead><tr><th>Indicador</th><th>Valor</th><th>Alerta</th><th>Peligro</th><th>Estado</th></tr></thead><tbody>${al.map((q) => `<tr><td>${esc(FEATURES[q.feature] || q.feature)}</td><td class="n">${fx(q.value, 3)}</td><td class="n">${fx(q.alert, 3)}</td><td class="n">${fx(q.danger, 3)}</td><td>${LV[q.level]}</td></tr>`).join('')}</tbody>`
      : '<tbody><tr><td class="muted">Sin mediciones.</td></tr></tbody>';
    const base = new Set(pt.baselineIds || []);
    $('tRecords').innerHTML = `<thead><tr><th>Fecha</th><th>rpm</th><th>Vel. RMS</th><th>1X</th><th>Env. RMS</th><th>Base</th></tr></thead><tbody>` +
      h.slice().reverse().map((q) => `<tr data-id="${q.id}" class="${q.id === selRec ? 'sel' : ''}" style="cursor:pointer"><td>${new Date(q.time).toLocaleString('es-AR', { dateStyle: 'short', timeStyle: 'short' })}</td><td class="n">${fx(q.rpm, 0)}</td><td class="n">${fx(q.features.vel_rms, 2)}</td><td class="n">${fx(q.features['1x'], 2)}</td><td class="n">${fx(q.features.env_rms, 3)}</td><td>${base.has(q.id) ? '✓' : ''}</td></tr>`).join('') + '</tbody>';
  }
  $('tRecords').addEventListener('click', (e) => { const tr = e.target.closest('tr[data-id]'); if (tr) { selRec = tr.dataset.id; renderTrend(); } });
  ['tPoint', 'tFeature'].forEach((id) => $(id).addEventListener('change', () => { selRec = null; renderTrend(); }));
  $('tAddPoint').addEventListener('click', () => {
    const n = prompt('Nombre del punto (p. ej. «Lado libre vertical»):', `Punto ${store.db.points.length + 1}`);
    if (!n) return; const p = store.addPoint(n.trim()); fillPoints(); $('tPoint').value = p.id; renderTrend();
  });
  $('tRenPoint').addEventListener('click', () => { const p = curPoint(), n = prompt('Nuevo nombre:', p.name); if (n) { p.name = n.trim(); store.save(); fillPoints(); renderTrend(); } });
  $('tDelPoint').addEventListener('click', () => {
    const p = curPoint();
    if (store.db.points.length < 2) return b.toast('Tiene que quedar al menos un punto.');
    if (confirm(`¿Eliminar «${p.name}» y sus ${store.history(p.id).length} mediciones?`)) { store.removePoint(p.id); fillPoints(); renderTrend(); }
  });
  $('tSave').addEventListener('click', () => {
    const r = rec(); if (!r) return b.toast('No hay señal adquirida.');
    const t = parseFloat($('tTemp').value);
    const recd = extractFeatures(r, +$('tChannel').value || 0, b.machine(), t);
    const pt = curPoint(), res = evaluate(recd, pt, b.machine().machineClass, store.history(pt.id).concat(recd));
    store.add(pt.id, recd); renderTrend();
    const lv = worstLevel(res);
    b.toast(lv === 2 ? 'Medición guardada · PELIGRO en ' + res.filter((q) => q.level === 2).map((q) => FEATURES[q.feature]).join(', ')
      : lv === 1 ? 'Medición guardada · ALERTA en ' + res.filter((q) => q.level === 1).map((q) => FEATURES[q.feature]).join(', ') : 'Medición guardada · sin alarmas.', 6000);
  });
  $('tBaseline').addEventListener('click', () => {
    const pt = curPoint(), h = store.history(pt.id);
    if (!h.length) return;
    pt.baselineIds = selRec ? [selRec] : h.slice(-Math.min(3, h.length)).map((q) => q.id);
    store.save(); renderTrend(); b.toast(`Línea base: ${pt.baselineIds.length} medición(es).`);
  });
  $('tDelRec').addEventListener('click', () => { if (!selRec) return b.toast('Elegí una medición de la tabla.'); store.remove(curPoint().id, selRec); selRec = null; renderTrend(); });
  $('tDemo').addEventListener('click', () => {
    const pt = curPoint(), base = Date.now() - 390 * 86400000;
    for (let i = 0; i < 13; i++) {
      const g = i < 7 ? 1 : Math.exp((i - 6) * 0.28), w = 1 + 0.04 * Math.sin(i * 2.1);
      store.add(pt.id, {
        id: `demo${Date.now()}${i}`, time: base + i * 30 * 86400000, rpm: 1500, fs: 5120, notes: 'demo',
        features: { vel_rms: 1.6 * w * (1 + (g - 1) * 0.35), '1x': 2.1 * w, '2x': 0.4 * w, acc_rms: 0.12 * w * g, acc_pico: 0.45 * w * g, curtosis: 3 + (g - 1) * 0.8, factor_cresta: 3.7 + (g - 1) * 0.5, env_rms: 0.012 * w * g, hf_rms: 0.05 * w * g, temperatura: 52 + i * 0.4 },
      });
    }
    pt.baselineIds = []; store.save(); $('tFeature').value = 'env_rms'; renderTrend();
    b.toast('Demo cargada: rodamiento que empieza a degradarse a los 6 meses.');
  });
  $('tCsv').addEventListener('click', () => b.download(`tendencia_${b.safe(curPoint().name)}.csv`, store.exportCsv(curPoint().id), 'text/csv'));
  $('tExport').addEventListener('click', () => b.download('tendencias.json', store.exportJson(), 'application/json'));
  $('tImport').addEventListener('click', () => $('tFile').click());
  $('tFile').addEventListener('change', async (e) => {
    const f = e.target.files[0]; e.target.value = ''; if (!f) return;
    try { store.importJson(await f.text()); fillPoints(); renderTrend(); b.toast('Base de tendencias importada.'); } catch (err) { b.toast('No se pudo importar: ' + err.message, 7000); }
  });

  fillPoints(); renderTrend();
  b.onRecording(() => {
    fillChannels();
    if ($('v-avanzado').classList.contains('on')) runAdvanced();
    if ($('v-ordenes').classList.contains('on')) runOrders();
  });
  b.onTab((id) => { if (id === 'tab-avanzado') runAdvanced(); else if (id === 'tab-ordenes') runOrders(); else if (id === 'tab-tendencias') { fillChannels(); renderTrend(); } });

  /** Datos de tendencia y alarmas del punto actual para el informe. */
  return {
    reportData() {
      const pt = curPoint(), h = store.history(pt.id);
      if (!h.length) return { alarms: [], indicators: null, trends: [] };
      const al = lastAlarms(pt), feat = $('tFeature').value, s = series(h, feat), a = al.find((q) => q.feature === feat), ft = fit(s);
      let fc = null, txt = '';
      if (ft && s.length) {
        const t1 = s[s.length - 1].time, xs = Array.from({ length: 31 }, (_, k) => t1 + k * 3 * 86400000);
        fc = { x: xs, y: xs.map(ft.valueAt) };
        const c = a ? ft.crossing(a.danger) : null;
        txt = `ajuste ${ft.exponential ? 'exponencial' : 'lineal'}, R² ${fx(ft.r2, 2)}` + (c && c > t1 ? `; peligro estimado ≈ ${new Date(c).toLocaleDateString('es-AR')}` : '');
      }
      return {
        alarms: al, indicators: h[h.length - 1],
        trends: s.length >= 2 ? [{ point: pt.name, title: FEATURES[feat], points: s, alert: a?.alert, danger: a?.danger, alarms: al, forecast: fc, forecastText: txt }] : [],
      };
    },
  };
}
