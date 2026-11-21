// Interfaz de la versión web. Toda la matemática está en ../core (con pruebas de paridad con la app de escritorio).
import { spectrum, statistics, convertUnits, UNITS, normalizeAngle } from '../core/dsp.js';
import { MACHINE_CLASSES, evaluateSeverity, diagnose, bearingOrders } from '../core/analysis.js';
import {
  BalancingSession, GRADES, gradeExamples, permissibleUnbalance, permissibleEccentricity, residualUnbalance,
  suggestTrialGrams, splitWeight, drillDepthMm, MATERIALS, estimatedMass, effectiveMass, recommendedPlanes,
} from '../core/balance.js';
import { RotorSimulator, defaultSimSettings, parseCsv, parseWav, writeCsv, SerialStreamParser } from '../core/io.js';
import { buildReport } from '../core/report.js';
import { WaveChart, SpectrumChart, PolarChart } from './charts.js';
import { initPro } from './pro.js';

{
  const $ = (id) => document.getElementById(id);
  const fx = (v, d = 2) => (Number.isFinite(v) ? v.toLocaleString('es-AR', { minimumFractionDigits: d, maximumFractionDigits: d }) : '—');
  const STORE = 'va.proyecto.v2';
  const CLASS_KEYS = Object.keys(MACHINE_CLASSES);
  const TYPE_KEYS = ['solid', 'hollow', 'impeller'];

  // ── Estado ──
  const defaultMachine = () => ({
    name: 'Ventilador de tiro inducido VT-02', location: 'Planta 1 · Caldera', rpm: 1500, lineHz: 50, machineClass: 'g2r',
    rotorType: 'impeller', material: 'Acero', massKg: 38, diameterMm: 450, innerDiameterMm: 0, lengthMm: 220, blades: 8,
    correctionRadiusMm: 200, balanceGrade: 6.3, bearing: { balls: 9, ballD: 7.94, pitchD: 39, contactDeg: 0 },
  });
  let machine = defaultMachine();
  let simSettings = defaultSimSettings();
  let sim = new RotorSimulator(simSettings);
  let source = 'sim';
  let recording = null;
  let analysis = null;
  let session = null;
  let report = { tech: '', company: '', client: '', number: '', workOrder: '', recommendations: '', notes: '', logo: '', sections: { charts: true, ind: true, trend: true, bal: true, sign: true } };
  const recordingListeners = [], tabListeners = [];
  let pro = null;
  const serial = { port: null, reader: null, parser: new SerialStreamParser(), running: false };
  let liveTimer = null;

  const charts = {
    spec: new SpectrumChart($('cSpec')), wave: new WaveChart($('cWave')),
    pol1: new PolarChart($('cPol1')), pol2: new PolarChart($('cPol2')),
  };

  // ── Utilidades de interfaz ──
  let toastTimer;
  function toast(msg, ms = 4200) {
    const t = $('toast'); t.textContent = msg; t.hidden = false;
    clearTimeout(toastTimer); toastTimer = setTimeout(() => (t.hidden = true), ms);
  }
  function save() {
    try { localStorage.setItem(STORE, JSON.stringify({ machine, sim: { ...simSettings, masses: [] }, session: session?.toJSON(), report })); } catch { /* sin almacenamiento */ }
  }
  function load() {
    try {
      const d = JSON.parse(localStorage.getItem(STORE) || 'null');
      if (!d) return;
      machine = { ...defaultMachine(), ...d.machine };
      if (d.sim) simSettings = { ...defaultSimSettings(), ...d.sim, masses: [] };
      if (d.report) report = { ...report, ...d.report };
      if (d.session) session = BalancingSession.fromRuns(d.session);
    } catch { /* datos viejos o corruptos: se ignoran */ }
  }

  // ── Pestañas ──
  const tabs = [...document.querySelectorAll('nav.tabs button')];
  function showTab(id) {
    tabs.forEach((b) => b.setAttribute('aria-selected', String(b.id === id)));
    document.querySelectorAll('section.view').forEach((s) => s.classList.toggle('on', s.getAttribute('aria-labelledby') === id));
    if (id === 'tab-informe') renderReport();
    tabListeners.forEach((f) => f(id));
    history.replaceState(null, '', '#' + id.slice(4));
  }
  tabs.forEach((b) => b.addEventListener('click', () => showTab(b.id)));

  // ── Fuente de datos ──
  const SRC_NOTES = {
    sim: 'Rotor virtual con desbalance en dos planos y tacómetro. Ideal para practicar.',
    file: 'CSV con columnas t, canales y "tach" (o línea "# fs=…"), o WAV de un colector.',
    serial: 'Sensor por USB (ESP32 + acelerómetro + tacómetro óptico) con el protocolo #VA. Requiere Chrome o Edge.',
    manual: 'Para balancear con las lecturas de amplitud y fase de otro analizador.',
  };
  function setSource(s) {
    source = s;
    document.querySelectorAll('#srcSeg button').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.src === s)));
    $('srcNote').textContent = SRC_NOTES[s];
    $('simPanel').hidden = s !== 'sim';
    $('btnAcquire').textContent = { sim: 'Adquirir', file: 'Abrir archivo', serial: serial.running ? 'Desconectar' : 'Conectar sensor', manual: 'Ir a balanceo' }[s];
    $('chkLive').disabled = !(s === 'sim' || s === 'serial');
    $('chkManual').checked = s === 'manual' || $('chkManual').checked && s !== 'sim';
    renderBalance();
  }
  document.querySelectorAll('#srcSeg button').forEach((b) => b.addEventListener('click', () => setSource(b.dataset.src)));

  $('btnAcquire').addEventListener('click', async () => {
    if (source === 'sim') acquireSim();
    else if (source === 'file') $('fileInput').click();
    else if (source === 'serial') serial.running ? stopSerial() : startSerial();
    else showTab('tab-balanceo');
  });
  $('chkLive').addEventListener('change', (e) => {
    clearInterval(liveTimer);
    if (e.target.checked) liveTimer = setInterval(() => (source === 'sim' ? acquireSim() : takeSerial(true)), 1000);
  });

  function acquireSim() {
    if (sim.s !== simSettings) sim = new RotorSimulator(simSettings);
    setRecording(sim.acquire());
  }
  /** Arranque simulado (600 → 3000 rpm, crítica a 1800 rpm) para Bode, Nyquist y espectrograma. */
  function acquireRunUp() {
    setSource('sim');
    const s = { ...simSettings, runUp: true, rpmStart: 600, rpmEnd: 3000, criticalRpm: 1800, fs: 2048, samples: 65536, masses: [] };
    setRecording(new RotorSimulator(s).acquire());
    toast('Arranque simulado de 600 a 3000 rpm (velocidad crítica en 1800 rpm).');
  }

  $('fileInput').addEventListener('change', async (e) => {
    const f = e.target.files[0];
    e.target.value = '';
    if (!f) return;
    try {
      if (/\.wav$/i.test(f.name)) {
        const scale = parseFloat(prompt('Valor físico que corresponde a ±1,0 del WAV (según la sensibilidad del sensor y la placa):', '1') || '1');
        const unit = (prompt('Unidad de la señal: g, mm/s o um', 'g') || 'g').trim();
        const tach = parseInt(prompt('Canal del tacómetro (1, 2…) o 0 si no hay:', '0') || '0', 10);
        setRecording(parseWav(await f.arrayBuffer(), { scale: isFinite(scale) ? scale : 1, unit: { 'mm/s': 'mms', um: 'um', 'µm': 'um' }[unit] || 'g', tachChannel: tach - 1, rpm: machine.rpm }));
      } else {
        setRecording(parseCsv(await f.text(), { rpm: machine.rpm }));
      }
      toast(`Archivo «${f.name}» cargado: ${recording.channels.length} canal(es), ${recording.length} muestras a ${fx(recording.fs, 0)} Hz.`);
    } catch (err) { toast('No se pudo leer el archivo: ' + err.message, 7000); }
  });

  // Web Serial
  async function startSerial() {
    if (!('serial' in navigator)) { toast('Este navegador no tiene acceso a puertos USB/serie. Usá Chrome o Edge en una computadora.', 7000); return; }
    try {
      serial.port = await navigator.serial.requestPort();
      await serial.port.open({ baudRate: 921600 });
      serial.parser.clear();
      serial.running = true;
      const enc = new TextEncoder(), w = serial.port.writable.getWriter();
      await w.write(enc.encode('START\n')); w.releaseLock();
      setSource('serial');
      toast('Sensor conectado. Esperando datos…');
      readSerialLoop();
      setTimeout(() => takeSerial(), 2500);
    } catch (err) { serial.running = false; if (err.name !== 'NotFoundError') toast('No se pudo conectar: ' + err.message, 7000); }
  }
  async function readSerialLoop() {
    while (serial.running && serial.port?.readable) {
      serial.reader = serial.port.readable.getReader();
      try {
        for (;;) {
          const { value, done } = await serial.reader.read();
          if (done) break;
          serial.parser.feedBytes(value);
        }
      } catch { /* desconexión */ } finally { serial.reader.releaseLock(); }
    }
  }
  async function stopSerial() {
    serial.running = false;
    try { const w = serial.port.writable.getWriter(); await w.write(new TextEncoder().encode('STOP\n')); w.releaseLock(); } catch { }
    try { await serial.reader?.cancel(); await serial.port?.close(); } catch { }
    serial.port = null; setSource('serial'); toast('Sensor desconectado.');
  }
  function takeSerial(silent = false) {
    try {
      const r = serial.parser.toRecording(machine.rpm, 16384);
      if (r.length < 512) { if (!silent) toast('Todavía no llegaron suficientes muestras.'); return false; }
      setRecording(r); return true;
    } catch (err) { if (!silent) toast(err.message); return false; }
  }

  // ── Análisis ──
  function setRecording(r) {
    recording = r;
    const sel = $('selChannel'), prev = sel.value;
    sel.innerHTML = r.channels.map((c, i) => `<option value="${i}">${c.name}</option>`).join('');
    if (prev && +prev < r.channels.length) sel.value = prev;
    analyze();
    recordingListeners.forEach((f) => f(r));
  }
  ['selChannel', 'selUnit', 'selWindow', 'selFmax'].forEach((id) => $(id).addEventListener('change', analyze));

  function analyze() {
    if (!recording) return;
    const chIdx = +$('selChannel').value || 0, ch = recording.channels[chIdx], fs = recording.fs;
    const viewUnit = $('selUnit').value, win = $('selWindow').value;
    const rpm = recording.measuredRpm() || machine.rpm, f1 = rpm / 60;
    const view = convertUnits(ch.data, fs, ch.unit, viewUnit, UNITS[viewUnit].order === UNITS[ch.unit].order ? 0 : 2);
    const sp = spectrum(view, fs, win);
    const vel = convertUnits(ch.data, fs, ch.unit, 'mms', 10);
    const spv = spectrum(vel, fs, 'hann');
    const vRms = spv.bandRms(10, Math.min(1000, fs / 2));
    const sev = evaluateSeverity(vRms, machine.machineClass);
    const st = statistics(view);
    const kurt = UNITS[ch.unit].order === 0 ? statistics(ch.data).kurtosis : 0;
    const bearing = machine.bearing?.balls > 0 ? machine.bearing : null;
    const findings = diagnose({ spectrum: spv, runningHz: f1, bearing, blades: machine.blades, lineHz: machine.lineHz, kurtosis: kurt });
    const one = recording.hasPhaseReference ? recording.reading1X(chIdx, rpm) : null;
    analysis = { sev, findings, vRms, rpm, one, st, unit: viewUnit };

    // Banda de lectura
    $('vrms').innerHTML = `${fx(vRms, 2)} <small>mm/s RMS</small>`;
    $('zone').innerHTML = `<i style="background:var(--z${sev.zone})"></i><span>${sev.label}</span>`;
    const l = sev.limits, maxS = l[2] * 1.4, seg = (a, b, z) => `<div style="width:${(b - a) / maxS * 100}%;background:var(--z${z})"></div>`;
    $('sevscale').innerHTML = seg(0, l[0], 'A') + seg(l[0], l[1], 'B') + seg(l[1], l[2], 'C') + seg(l[2], maxS, 'D') + `<span class="mk" style="left:calc(${Math.min(vRms / maxS, 1) * 100}% - 1px)"></span>`;
    $('sevscale').title = `${sev.standard}: A/B ${fx(l[0])} · B/C ${fx(l[1])} · C/D ${fx(l[2])} mm/s`;
    const u = UNITS[viewUnit].symbol;
    $('figs').innerHTML = [
      ['1X (amplitud ∠ fase)', one ? `${fx(one.mag)} ∠ ${Math.round(one.ang)}°` : 'sin tacómetro'],
      ['Velocidad de giro', `${fx(rpm, 0)} rpm`],
      [`Pico (${u})`, fx(st.peak, 3)],
      ['Factor de cresta', fx(st.crest, 2)],
      ['Curtosis', fx(st.kurtosis, 2)],
      ['Diagnóstico', findings[0]?.title ?? 'Sin fallas'],
    ].map(([k, v]) => `<div><span>${k}</span><b>${v}</b></div>`).join('');

    // Gráficos
    const fmaxSel = $('selFmax').value;
    const fmax = Math.min(fs / 2, fmaxSel === 'auto' ? Math.max(12.5 * f1, bearing ? bearingOrders(bearing).bpfi * f1 * 2.2 : 0, 200) : +fmaxSel);
    const markers = [];
    if (bearing) { const b = bearingOrders(bearing); markers.push({ f: b.bpfo * f1, label: 'BPFO' }, { f: b.bpfi * f1, label: 'BPFI' }); }
    if (machine.blades > 1) markers.push({ f: machine.blades * f1, label: 'Álabes' });
    const pk = sp.peakNear(f1, Math.max(sp.resolution * 1.5, f1 * 0.02));
    charts.spec.set({ freqs: sp.freqs, amps: sp.amps, fmax, f1, unit: u, markers, peak: pk.index >= 0 ? pk : null });
    charts.wave.set({ y: view, fs, rpm, unit: u, pulses: recording.pulses() });
    const top = findings[0];
    $('specTitle').textContent = top ? `${top.title}: ${top.type === 'unbalance' ? `1X domina el espectro` : top.evidence.split(';')[0]}` : 'Espectro sin picos de falla';
    $('specSub').textContent = `${recording.source} · ${ch.name} · ${WINDOWS_NAME[win]} · resolución ${fx(sp.resolution, 3)} Hz · ${fx(recording.duration, 2)} s`;
    $('findings').innerHTML = findings.length
      ? findings.map((f) => `<div class="finding" style="border-color:${f.confidence > 0.6 ? 'var(--zD)' : f.confidence > 0.45 ? 'var(--zC)' : 'var(--accent)'}"><b>${f.title} <span class="conf">· ${Math.round(f.confidence * 100)} %</span></b><p>${f.evidence}</p><p>${f.recommendation}</p></div>`).join('')
      : '<p class="muted">No hay picos significativos en 1X, sus armónicos ni frecuencias características.</p>';
    renderBalance();
  }
  const WINDOWS_NAME = { hann: 'Hanning', flattop: 'Flat-top', hamming: 'Hamming', rect: 'Rectangular' };

  // ── Controles del simulador ──
  const SIM_CTRLS = [
    ['rpm', 'Velocidad (rpm)', 300, 6000, 10, (v) => fx(v, 0)],
    ['u1mag', 'Desbalance plano 1 (g·mm)', 0, 6000, 50, (v) => fx(v, 0)],
    ['u2mag', 'Desbalance plano 2 (g·mm)', 0, 6000, 50, (v) => fx(v, 0)],
    ['misalignment', 'Desalineación (mm/s)', 0, 5, 0.1, (v) => fx(v, 1)],
    ['looseness', 'Holgura mecánica (mm/s)', 0, 5, 0.1, (v) => fx(v, 1)],
    ['bearing', 'Rodamiento pista ext. (mm/s)', 0, 5, 0.1, (v) => fx(v, 1)],
    ['oilWhirl', 'Remolino de aceite (mm/s)', 0, 5, 0.1, (v) => fx(v, 1)],
    ['electrical', 'Eléctrico 2× red (mm/s)', 0, 5, 0.1, (v) => fx(v, 1)],
    ['noise', 'Ruido (mm/s)', 0, 1, 0.01, (v) => fx(v, 2)],
  ];
  const simGet = (k) => (k === 'u1mag' ? simSettings.u1.mag : k === 'u2mag' ? simSettings.u2.mag : simSettings[k]);
  const simSet = (k, v) => { if (k === 'u1mag') simSettings.u1 = { ...simSettings.u1, mag: v }; else if (k === 'u2mag') simSettings.u2 = { ...simSettings.u2, mag: v }; else simSettings[k] = v; };
  function buildSimControls() {
    $('simControls').innerHTML = SIM_CTRLS.map(([k, label, min, max, step]) =>
      `<label class="f" for="sim-${k}" style="margin-top:6px">${label}<span class="slider"><input type="range" id="sim-${k}" min="${min}" max="${max}" step="${step}"><output id="out-${k}"></output></span></label>`).join('');
    for (const [k, , , , , f] of SIM_CTRLS) {
      const el = $(`sim-${k}`);
      el.value = simGet(k); $(`out-${k}`).textContent = f(+el.value);
      el.addEventListener('input', () => { simSet(k, +el.value); $(`out-${k}`).textContent = f(+el.value); if (k === 'rpm') machine.rpm = +el.value; });
      el.addEventListener('change', () => { if (k === 'rpm') renderMachine(); acquireSim(); save(); });
    }
  }

  // ── Máquina ──
  function buildMachineForm() {
    $('mClass').innerHTML = CLASS_KEYS.map((k) => `<option value="${k}">${MACHINE_CLASSES[k].name}</option>`).join('');
    $('mMaterial').innerHTML = Object.keys(MATERIALS).map((k) => `<option>${k}</option>`).join('');
    $('mGrade').innerHTML = GRADES.map((g) => `<option value="${g}">G${String(g).replace('.', ',')}</option>`).join('');
    const map = { mName: 'name', mLocation: 'location', mRpm: 'rpm', mLine: 'lineHz', mClass: 'machineClass', mType: 'rotorType', mMaterial: 'material', mMass: 'massKg', mDiam: 'diameterMm', mInner: 'innerDiameterMm', mLen: 'lengthMm', mBlades: 'blades', mRadius: 'correctionRadiusMm', mGrade: 'balanceGrade' };
    const bmap = { bBalls: 'balls', bBall: 'ballD', bPitch: 'pitchD', bAngle: 'contactDeg' };
    for (const [id, key] of Object.entries(map)) {
      $(id).addEventListener('input', () => {
        const el = $(id), num = el.type === 'number' || ['mLine', 'mGrade'].includes(id);
        machine[key] = num ? Math.max(0, parseFloat(el.value) || 0) : el.value;
        if (key === 'rpm' && machine.rpm > 0) { simSettings.rpm = machine.rpm; $('sim-rpm').value = machine.rpm; $('out-rpm').textContent = fx(machine.rpm, 0); }
        renderMachine(false); save();
      });
    }
    for (const [id, key] of Object.entries(bmap)) {
      $(id).addEventListener('input', () => { machine.bearing = { ...machine.bearing, [key]: parseFloat($(id).value) || 0 }; renderMachine(false); save(); });
    }
    $('machineForm').addEventListener('change', () => analyze());
  }
  function fillMachineForm() {
    const set = (id, v) => ($(id).value = v);
    set('mName', machine.name); set('mLocation', machine.location); set('mRpm', machine.rpm); set('mLine', machine.lineHz); set('mClass', machine.machineClass);
    set('mType', machine.rotorType); set('mMaterial', machine.material); set('mMass', machine.massKg); set('mDiam', machine.diameterMm); set('mInner', machine.innerDiameterMm);
    set('mLen', machine.lengthMm); set('mBlades', machine.blades); set('mRadius', machine.correctionRadiusMm); set('mGrade', machine.balanceGrade);
    const b = machine.bearing || {};
    set('bBalls', b.balls || 0); set('bBall', b.ballD || 0); set('bPitch', b.pitchD || 0); set('bAngle', b.contactDeg || 0);
  }
  function renderMachine(fill = true) {
    if (fill) fillMachineForm();
    const mass = effectiveMass(machine), rec = recommendedPlanes(machine);
    const uper = permissibleUnbalance(machine.balanceGrade, mass, machine.rpm);
    const trial = suggestTrialGrams(mass, machine.rpm, machine.correctionRadiusMm);
    const rows = [
      ['Masa del rotor', `${fx(mass, 1)} kg${machine.massKg > 0 ? '' : ' (estimada)'}`],
      ['1X', `${fx(machine.rpm / 60, 2)} Hz`],
      ['Planos recomendados', String(rec.planes)],
      [`Desbalance admisible G${String(machine.balanceGrade).replace('.', ',')}`, `${fx(uper, 0)} g·mm`],
      [`… a ${fx(machine.correctionRadiusMm, 0)} mm de radio`, `${fx(uper / machine.correctionRadiusMm, 2)} g`],
      ['Excentricidad admisible', `${fx(permissibleEccentricity(machine.balanceGrade, machine.rpm), 1)} µm`],
      ['Peso de prueba sugerido', `${fx(trial, 1)} g`],
    ];
    if (machine.bearing?.balls > 0) {
      const b = bearingOrders(machine.bearing), f = machine.rpm / 60;
      rows.push(['BPFO', `${fx(b.bpfo)}X · ${fx(b.bpfo * f, 1)} Hz`], ['BPFI', `${fx(b.bpfi)}X · ${fx(b.bpfi * f, 1)} Hz`], ['BSF / FTF', `${fx(b.bsf)}X / ${fx(b.ftf, 3)}X`]);
    }
    $('machSummary').innerHTML = rows.map(([k, v]) => `<dt>${k}</dt><dd>${v}</dd>`).join('');
    $('machRec').textContent = `${rec.reason} Calidad G${String(machine.balanceGrade).replace('.', ',')}: ${gradeExamples(machine.balanceGrade)}. ${machine.massKg > 0 ? '' : `Masa estimada con geometría y densidad: ${fx(estimatedMass(machine), 1)} kg.`}`;
    $('machineChip').textContent = `${machine.name} · ${fx(machine.rpm, 0)} rpm`;
    $('trialSug').value = `${fx(trial, 1)} g`;
    $('balRec').textContent = `Recomendado: ${rec.planes} plano${rec.planes > 1 ? 's' : ''}. ${rec.reason}`;
  }

  // ── Balanceo ──
  const STEP_TEXT = { initial: 'Medición original', trial1: 'Peso de prueba en el plano 1', trial2: 'Peso de prueba en el plano 2', correction: 'Colocar la corrección', verification: 'Verificar y ajustar', done: 'Balanceo terminado' };
  const COLORS = ['#55605b', '#e08e00', '#6b7cff', '#00a862', '#0b7a8a', '#9c4dcc'];

  function newSession() {
    const planes = +$('balPlanes').value;
    session = new BalancingSession(planes, { againstRotation: $('balAngles').value === 'against', keepTrial: $('balKeep').checked, tolerance: Math.max(0.01, +$('balTol').value || 1) });
    simSettings.sensors = planes; simSettings.masses = [];
    $('trialG').value = (Math.round(suggestTrialGrams(effectiveMass(machine), machine.rpm, machine.correctionRadiusMm) * 10) / 10) || 20;
    $('trialA').value = 0;
    if (source === 'sim') acquireSim();
    renderBalance(); save();
  }
  $('btnNewSession').addEventListener('click', () => { if (!session.runs.length || confirm('¿Descartar las corridas registradas y empezar de nuevo?')) newSession(); });
  $('balPlanes').addEventListener('change', () => { if (!session.runs.length) newSession(); else { $('balPlanes').value = session.planes; toast('Para cambiar los planos empezá una nueva sesión.'); } });
  $('balAngles').addEventListener('change', () => { if (!session.runs.length) newSession(); else { $('balAngles').value = session.againstRotation ? 'against' : 'with'; toast('El sentido de los ángulos no se puede cambiar a mitad de una sesión.'); } });
  $('balKeep').addEventListener('change', () => { if (!session.runs.length) newSession(); else { $('balKeep').checked = session.keepTrial; toast('Esta opción se elige antes de la primera corrida.'); } });
  $('balTol').addEventListener('change', () => { session.tolerance = Math.max(0.01, +$('balTol').value || 1); save(); });
  $('chkManual').addEventListener('change', renderBalance);

  function placeInSimulator(t) {
    const r = machine.correctionRadiusMm, phys = (a) => (session.againstRotation ? a : -a), m = simSettings.masses;
    switch (session.step) {
      case 'initial': m.length = 0; break;
      case 'trial1': m.length = 0; m.push({ plane: 1, grams: t.mag, angle: phys(t.ang), radiusMm: r }); break;
      case 'trial2': if (!session.keepTrial) m.length = 0; m.push({ plane: 2, grams: t.mag, angle: phys(t.ang), radiusMm: r }); break;
      case 'correction': if (!session.keepTrial) m.length = 0; session.corrections.forEach((w, i) => m.push({ plane: i + 1, grams: w.mag, angle: phys(w.ang), radiusMm: r })); break;
      case 'verification': session.trim.forEach((w, i) => m.push({ plane: i + 1, grams: w.mag, angle: phys(w.ang), radiusMm: r })); break;
    }
  }

  $('btnMeasure').addEventListener('click', () => {
    try {
      const two = session.planes === 2;
      const t = { mag: Math.max(0, +$('trialG').value || 0), ang: normalizeAngle(+$('trialA').value || 0) };
      if ((session.step === 'trial1' || session.step === 'trial2') && !(t.mag > 0)) { toast('Indicá la masa del peso de prueba.'); return; }
      let s1, s2 = null, rpm = machine.rpm;
      if ($('chkManual').checked || source === 'manual') {
        s1 = { mag: +$('m1a').value || 0, ang: normalizeAngle(+$('m1p').value || 0) };
        if (two) s2 = { mag: +$('m2a').value || 0, ang: normalizeAngle(+$('m2p').value || 0) };
        if (!(s1.mag > 0) || (two && !(s2.mag > 0))) { toast('Ingresá la amplitud y la fase leídas en cada sensor.'); return; }
      } else {
        if (source === 'sim') { placeInSimulator(t); acquireSim(); }
        else if (source === 'serial' && !takeSerial()) return;
        else if (source === 'file' && !recording) { toast('Primero abrí el archivo de esta corrida en la pestaña Análisis.'); return; }
        if (!recording.hasPhaseReference) { toast('La señal no tiene tacómetro: sin referencia no hay fase. Usá «Ingresar la lectura a mano».', 7000); return; }
        if (two && recording.channels.length < 2) { toast('El balanceo en 2 planos necesita dos canales de vibración.'); return; }
        rpm = recording.measuredRpm();
        s1 = recording.reading1X(0, rpm); if (two) s2 = recording.reading1X(1, rpm);
        $('m1a').value = s1.mag.toFixed(3); $('m1p').value = s1.ang.toFixed(1);
        if (s2) { $('m2a').value = s2.mag.toFixed(3); $('m2p').value = s2.ang.toFixed(1); }
      }
      let msg = '';
      switch (session.step) {
        case 'initial': session.recordInitial(s1, s2, rpm); msg = 'Medición original registrada. Ahora el peso de prueba.'; break;
        case 'trial1': msg = session.recordTrial1(t, s1, s2, rpm).message; if (two) $('trialA').value = 0; break;
        case 'trial2': msg = session.recordTrial2(t, s1, s2, rpm).message; break;
        default: { const v = session.recordVerification(s1, s2, rpm); msg = v.ok ? 'Vibración dentro de la tolerancia: balanceo terminado.' : 'Todavía fuera de tolerancia: colocá el ajuste indicado y volvé a medir.'; }
      }
      toast(msg, 6000); renderBalance(); save();
    } catch (err) { toast(err.message, 7000); }
  });

  $('btnUndo').addEventListener('click', () => {
    if (!session.runs.length) return;
    const d = session.toJSON(); d.runs = d.runs.slice(0, -1);
    session = BalancingSession.fromRuns(d);
    simSettings.masses = [];
    renderBalance(); save(); toast('Se deshizo la última corrida.');
  });

  function renderBalance() {
    if (!session) return;
    const two = session.planes === 2, manual = $('chkManual').checked || source === 'manual';
    $('balPlanes').value = session.planes; $('balAngles').value = session.againstRotation ? 'against' : 'with'; $('balKeep').checked = session.keepTrial; $('balTol').value = session.tolerance;
    const order = ['initial', 'trial1', ...(two ? ['trial2'] : []), 'correction', 'verification'];
    const cur = session.step === 'done' ? order.length : order.indexOf(session.step === 'verification' ? 'verification' : session.step);
    $('steps').innerHTML = order.map((s, i) => `<li class="${i < cur ? 'done' : i === cur ? 'cur' : ''}">${STEP_TEXT[s]}</li>`).join('');
    const angles = session.againstRotation ? 'en sentido contrario al giro' : 'en el sentido de giro';
    const r = machine.correctionRadiusMm;
    const instr = {
      initial: `Con la máquina a su velocidad de trabajo y sin pesos agregados, medí la vibración 1X${two ? ' en los dos sensores' : ''}. Marcá una referencia en el rotor (cinta reflectiva del tacómetro): todos los ángulos se miden desde ahí, ${angles}.`,
      trial1: `Detené la máquina y colocá el peso de prueba en el plano 1, a ${fx(r, 0)} mm de radio. Arrancá y medí. Debe cambiar la vibración al menos un 30 % o 30°.`,
      trial2: `Retirá el peso del plano 1${session.keepTrial ? ' (o dejalo: elegiste dejar los pesos)' : ''} y colocá el peso de prueba en el plano 2. Arrancá y medí.`,
      correction: `Retirá los pesos de prueba${session.keepTrial ? ' (no: elegiste dejarlos)' : ''}, colocá la corrección calculada y medí para verificar.`,
      verification: 'Todavía supera la tolerancia: agregá también el ajuste (trim) indicado y volvé a medir.',
      done: 'La vibración quedó dentro de la tolerancia. Generá el informe en la pestaña Informe.',
    }[session.step];
    $('stepTitle').textContent = session.step === 'done' ? 'Balanceo terminado' : `Paso ${cur + 1}: ${STEP_TEXT[session.step]}`;
    $('stepInstr').textContent = instr;
    $('stepSource').textContent = manual ? 'Lectura manual' : source === 'sim' ? 'Simulador: los pesos se colocan solos' : source === 'serial' ? 'Sensor USB' : 'Archivo';
    $('trialFields').hidden = !(session.step === 'trial1' || session.step === 'trial2');
    $('manualFields').hidden = !manual;
    document.querySelectorAll('#manualFields .s2').forEach((e) => (e.hidden = !two));
    $('btnMeasure').disabled = session.step === 'done';
    $('btnMeasure').textContent = { initial: 'Medir original', trial1: 'Medir con peso de prueba 1', trial2: 'Medir con peso de prueba 2', correction: 'Medir verificación', verification: 'Medir verificación', done: 'Terminado' }[session.step];
    $('btnUndo').disabled = !session.runs.length;
    $('pol2box').hidden = !two;
    $('pol2box').parentElement.style.gridTemplateColumns = two ? '' : 'minmax(0,1fr)';

    // Resultado
    const box = $('resultBox');
    if (!session.corrections.length) {
      box.innerHTML = `<h3>Corrección</h3><p class="muted">${session.runs.length ? 'Completá las corridas de prueba para calcularla.' : 'Todavía no hay mediciones.'}</p>`;
    } else {
      const mass = effectiveMass(machine), uper = permissibleUnbalance(machine.balanceGrade, mass, machine.rpm) / session.planes;
      const density = MATERIALS[machine.material] || 7850, sens = session.sensitivities();
      const lastVer = [...session.runs].reverse().find((x) => x.name.startsWith('Verificación'));
      let h = `<h3>${session.step === 'done' ? 'Corrección aplicada' : session.keepTrial ? 'Agregar además de los pesos de prueba' : 'Corrección a colocar'}</h3><div class="polars">`;
      session.corrections.forEach((w, i) => {
        h += `<div><div class="muted">Plano ${i + 1}</div><div class="corr">${fx(w.mag, 2)} g ∠ ${fx(w.ang, 1)}°</div><p class="muted" style="margin:4px 0">a ${fx(r, 0)} mm · ${angles}</p>`;
        if (machine.blades >= 2) { const sp = splitWeight(w, machine.blades); h += `<p style="margin:4px 0">En posiciones fijas: <b>${fx(sp.massA)} g</b> en la n.º ${sp.posA + 1} (${fx(sp.angA, 0)}°) + <b>${fx(sp.massB)} g</b> en la n.º ${sp.posB + 1} (${fx(sp.angB, 0)}°)</p>`; }
        h += `<p style="margin:4px 0">O quitar: perforar <b>${fx(drillDepthMm(w.mag, density, 10), 1)} mm</b> con broca de 10 mm a ${fx(normalizeAngle(w.ang + 180), 0)}°</p></div>`;
      });
      h += '</div>';
      const pred = session.predictedResidual();
      if (pred.length) h += `<p>Vibración residual prevista: ${pred.map((p, i) => `sensor ${i + 1}: <b>${fx(p.mag, 3)}</b>`).join(' · ')}</p>`;
      if (session.two) h += `<p class="muted">Condición de la matriz de influencia: ${fx(session.two.condition, 1)} ${session.two.condition > 10 ? '(planos poco independientes: el resultado es sensible al ruido)' : '(buena)'}.</p>`;
      if (session.step === 'verification' && session.trim.length) h += `<p><b>Ajuste (trim):</b> ${session.trim.map((w, i) => `plano ${i + 1}: ${fx(w.mag)} g ∠ ${fx(w.ang, 0)}°`).join(' · ')}</p>`;
      if (lastVer && sens.length) {
        const res = residualUnbalance(lastVer.s1.mag, sens[0], r);
        h += `<p>Desbalance residual estimado (plano 1): <b>${fx(res, 0)} g·mm</b> · admisible G${String(machine.balanceGrade).replace('.', ',')}: ${fx(uper, 0)} g·mm → ${res <= uper ? '<b style="color:var(--zA)">cumple</b>' : '<b style="color:var(--zD)">no cumple</b>'}</p>`;
      }
      box.innerHTML = h;
    }

    // Diagramas y tabla
    const reads = (k) => session.runs.map((x, i) => ({ p: k === 1 ? x.s1 : x.s2, color: COLORS[Math.min(i, COLORS.length - 1)], dashed: x.name.startsWith('Verificación'), name: x.name })).filter((x) => x.p);
    const masses = (k) => (session.corrections[k - 1] ? [{ p: session.corrections[k - 1], color: '#d6362b' }] : []);
    charts.pol1.set({ readings: reads(1), masses: masses(1), ccw: session.againstRotation });
    charts.pol2.set({ readings: reads(2), masses: masses(2), ccw: session.againstRotation });
    const leg = (k) => reads(k).map((x) => `<span><i style="background:${x.color}"></i>${x.name}: ${fx(x.p.mag)} ∠ ${Math.round(x.p.ang)}°</span>`).join('') + (masses(k).length ? `<span><i style="background:#d6362b;border-radius:50%"></i>Corrección: ${fx(masses(k)[0].p.mag)} g ∠ ${Math.round(masses(k)[0].p.ang)}°</span>` : '');
    $('leg1').innerHTML = leg(1); $('leg2').innerHTML = leg(2);
    $('runsTable').innerHTML = `<thead><tr><th>Corrida</th><th>Peso</th><th>Sensor 1</th>${two ? '<th>Sensor 2</th>' : ''}<th>rpm</th></tr></thead><tbody>` +
      (session.runs.length ? session.runs.map((x) => `<tr><td>${x.name}</td><td class="n">${x.trial ? `${fx(x.trial.mag)} g ∠ ${Math.round(x.trial.ang)}° (P${x.plane})` : '—'}</td><td class="n">${fx(x.s1.mag, 3)} ∠ ${Math.round(x.s1.ang)}°</td>${two ? `<td class="n">${x.s2 ? `${fx(x.s2.mag, 3)} ∠ ${Math.round(x.s2.ang)}°` : '—'}</td>` : ''}<td class="n">${fx(x.rpm, 0)}</td></tr>`).join('') : `<tr><td colspan="${two ? 5 : 4}" class="muted">Sin corridas.</td></tr>`) + '</tbody>';
  }

  // ── Informe ──
  const RFIELDS = { rTech: 'tech', rCompany: 'company', rClient: 'client', rNumber: 'number', rWorkOrder: 'workOrder', rRecs: 'recommendations', rNotes: 'notes' };
  const RSECT = { rsCharts: 'charts', rsInd: 'ind', rsTrend: 'trend', rsBal: 'bal', rsSign: 'sign' };
  const refreshReport = () => { save(); clearTimeout(renderReport.t); renderReport.t = setTimeout(renderReport, 400); };
  for (const [id, k] of Object.entries(RFIELDS)) $(id).addEventListener('input', () => { report[k] = $(id).value; refreshReport(); });
  for (const [id, k] of Object.entries(RSECT)) $(id).addEventListener('change', () => { report.sections = { ...report.sections, [k]: $(id).checked }; refreshReport(); });
  function fillReportForm() {
    for (const [id, k] of Object.entries(RFIELDS)) $(id).value = report[k] || '';
    for (const [id, k] of Object.entries(RSECT)) $(id).checked = report.sections?.[k] !== false;
    $('rLogoDel').disabled = !report.logo;
  }
  $('rLogoBtn').addEventListener('click', () => $('rLogo').click());
  $('rLogo').addEventListener('change', (e) => {
    const f = e.target.files[0]; e.target.value = ''; if (!f) return;
    if (f.size > 400000) return toast('El logo debe pesar menos de 400 kB.');
    const rd = new FileReader(); rd.onload = () => { report.logo = rd.result; fillReportForm(); refreshReport(); }; rd.readAsDataURL(f);
  });
  $('rLogoDel').addEventListener('click', () => { report.logo = ''; fillReportForm(); refreshReport(); });
  $('rSuggest').addEventListener('click', () => {
    const recs = [...new Set((analysis?.findings || []).map((f) => f.recommendation))];
    $('rRecs').value = report.recommendations = (recs.length ? recs : ['Continuar con el plan de monitoreo periódico.']).map((r) => '- ' + r).join('\n');
    refreshReport();
  });
  function reportHtml() {
    const sec = report.sections || {};
    const extra = pro ? pro.reportData() : { alarms: [], indicators: null, trends: [] };
    const d = { machine, report: { ...report, signatures: sec.sign !== false }, severity: analysis?.sev, findings: analysis?.findings || [], session: sec.bal !== false ? session : null };
    if (sec.ind !== false) { d.alarms = extra.alarms; d.indicators = extra.indicators; }
    if (sec.trend !== false) d.trends = extra.trends;
    if (sec.charts !== false && recording && analysis) {
      const ch = recording.channels[+$('selChannel').value || 0], u = analysis.unit, f1 = analysis.rpm / 60;
      const view = convertUnits(ch.data, recording.fs, ch.unit, u, UNITS[u].order === UNITS[ch.unit].order ? 0 : 2);
      d.spectrum = spectrum(view, recording.fs, $('selWindow').value);
      d.spectrumUnit = `${UNITS[u].symbol} pico`;
      d.spectrumMaxHz = Math.min(recording.fs / 2, Math.max(12.5 * f1, 200));
      d.markers = [1, 2, 3].map((k) => ({ f: k * f1, label: `${k}X` }));
      if (machine.bearing?.balls > 0) { const b = bearingOrders(machine.bearing); d.markers.push({ f: b.bpfo * f1, label: 'BPFO' }, { f: b.bpfi * f1, label: 'BPFI' }); }
      const n = Math.min(view.length, Math.round(recording.fs * 6 / Math.max(f1, 1)));
      d.waveform = { t: Array.from({ length: n }, (_, i) => i / recording.fs), y: Array.from(view.slice(0, n)), unit: UNITS[u].symbol };
    }
    return buildReport(d);
  }
  function renderReport() { $('reportFrame').srcdoc = reportHtml(); }
  $('btnPrint').addEventListener('click', () => { renderReport(); setTimeout(() => $('reportFrame').contentWindow.print(), 400); });
  const download = (name, text, type) => {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([text], { type }));
    a.download = name; document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 2000);
  };
  const safe = (s) => (s || 'equipo').replace(/[^\w\-áéíóúñÁÉÍÓÚÑ]+/g, '_');
  $('btnHtml').addEventListener('click', () => download(`Informe_${safe(machine.name)}.html`, reportHtml(), 'text/html'));
  $('btnExportCsv').addEventListener('click', () => recording ? download(`senal_${safe(machine.name)}.csv`, writeCsv(recording), 'text/csv') : toast('No hay señal adquirida.'));

  // Proyecto compatible con la app de escritorio (.vaproj)
  const P2 = (p) => (p ? { Magnitude: p.mag, AngleDeg: p.ang } : null);
  const P1 = (p) => (p ? { mag: p.Magnitude, ang: p.AngleDeg } : null);
  $('btnExportProj').addEventListener('click', () => {
    const m = machine;
    const proj = {
      Version: 2,
      Machine: { Name: m.name, Location: m.location, RotorType: TYPE_KEYS.indexOf(m.rotorType), Material: m.material, DiameterMm: m.diameterMm, InnerDiameterMm: m.innerDiameterMm, LengthMm: m.lengthMm, MassKg: m.massKg, Rpm: m.rpm, Blades: m.blades, VerticalShaft: false, CorrectionRadiusMm: m.correctionRadiusMm, BalanceGrade: m.balanceGrade, MachineClass: CLASS_KEYS.indexOf(m.machineClass), Bearing: m.bearing?.balls > 0 ? { Balls: m.bearing.balls, BallDiameterMm: m.bearing.ballD, PitchDiameterMm: m.bearing.pitchD, ContactAngleDeg: m.bearing.contactDeg } : null, LineFrequencyHz: m.lineHz },
      Planes: session.planes, AnglesAgainstRotation: session.againstRotation, KeepTrialWeights: session.keepTrial, Tolerance: session.tolerance,
      Runs: session.runs.map((r) => ({ Name: r.name, Sensor1: P2(r.s1), Sensor2: P2(r.s2), Rpm: r.rpm, Trial: P2(r.trial), TrialPlane: r.plane, Time: r.time })),
      Technician: report.tech, Company: report.company, Notes: report.notes, Client: report.client, ReportNumber: report.number, WorkOrder: report.workOrder, Recommendations: report.recommendations,
    };
    download(`${safe(m.name)}.vaproj`, JSON.stringify(proj, null, 2), 'application/json');
  });
  $('btnImportProj').addEventListener('click', () => $('projInput').click());
  $('projInput').addEventListener('change', async (e) => {
    const f = e.target.files[0]; e.target.value = '';
    if (!f) return;
    try {
      const p = JSON.parse(await f.text()), M = p.Machine || {};
      machine = { ...defaultMachine(), name: M.Name, location: M.Location || '', rotorType: TYPE_KEYS[M.RotorType] || 'solid', material: M.Material || 'Acero', diameterMm: M.DiameterMm, innerDiameterMm: M.InnerDiameterMm || 0, lengthMm: M.LengthMm, massKg: M.MassKg || 0, rpm: M.Rpm, blades: M.Blades || 0, correctionRadiusMm: M.CorrectionRadiusMm, balanceGrade: M.BalanceGrade, machineClass: CLASS_KEYS[M.MachineClass] || 'g2r', lineHz: M.LineFrequencyHz || 50, bearing: M.Bearing ? { balls: M.Bearing.Balls, ballD: M.Bearing.BallDiameterMm, pitchD: M.Bearing.PitchDiameterMm, contactDeg: M.Bearing.ContactAngleDeg } : { balls: 0, ballD: 0, pitchD: 0, contactDeg: 0 } };
      session = BalancingSession.fromRuns({ planes: p.Planes || 1, againstRotation: p.AnglesAgainstRotation !== false, keepTrial: !!p.KeepTrialWeights, tolerance: p.Tolerance || 1, runs: (p.Runs || []).map((r) => ({ name: r.Name, s1: P1(r.Sensor1), s2: P1(r.Sensor2), rpm: r.Rpm, trial: P1(r.Trial), plane: r.TrialPlane, time: r.Time })) });
      report = { ...report, tech: p.Technician || '', company: p.Company || '', notes: p.Notes || '', client: p.Client || '', number: p.ReportNumber || '', workOrder: p.WorkOrder || '', recommendations: p.Recommendations || '' };
      fillReportForm();
      simSettings.rpm = machine.rpm; simSettings.sensors = session.planes; simSettings.masses = [];
      renderMachine(); renderBalance(); renderReport(); save();
      toast(`Proyecto «${machine.name}» importado con ${session.runs.length} corrida(s).`);
    } catch (err) { toast('No se pudo importar el proyecto: ' + err.message, 7000); }
  });

  // ── Inicio ──
  load();
  buildMachineForm();
  buildSimControls();
  fillReportForm();
  pro = initPro({
    fx, toast, download, safe, acquireRunUp,
    recording: () => recording, machine: () => machine, channel: () => +$('selChannel').value || 0,
    onRecording: (f) => recordingListeners.push(f), onTab: (f) => tabListeners.push(f),
  });
  if (!session) {
    $('balPlanes').value = String(recommendedPlanes(machine).planes);
    session = new BalancingSession(+$('balPlanes').value, { tolerance: 1 });
    $('trialG').value = Math.round(suggestTrialGrams(effectiveMass(machine), machine.rpm, machine.correctionRadiusMm) * 10) / 10;
  }
  simSettings.sensors = session.planes; simSettings.rpm = machine.rpm;
  renderMachine();
  setSource('sim');
  acquireSim();
  const start = { analisis: 'tab-analisis', avanzado: 'tab-avanzado', ordenes: 'tab-ordenes', tendencias: 'tab-tendencias', balanceo: 'tab-balanceo', maquina: 'tab-maquina', informe: 'tab-informe' }[location.hash.slice(1)];
  if (start) showTab(start);
  // Sin conexión: el service worker guarda la app para usarla en planta sin señal
  if ('serviceWorker' in navigator && (location.protocol === 'https:' || location.hostname === 'localhost')) navigator.serviceWorker.register('/sw.js').catch(() => {});
}
