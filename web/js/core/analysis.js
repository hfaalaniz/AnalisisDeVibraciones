// Severidad ISO 10816 y diagnóstico de fallas por reglas. Equivalente a VibrationAnalysis.Core.Analysis (C#).

export const MACHINE_CLASSES = {
  c1: { name: 'ISO 10816-1 · Clase I (hasta 15 kW)', limits: [0.71, 1.8, 4.5] },
  c2: { name: 'ISO 10816-1 · Clase II (15–75 kW)', limits: [1.12, 2.8, 7.1] },
  c3: { name: 'ISO 10816-1 · Clase III (grandes, base rígida)', limits: [1.8, 4.5, 11.2] },
  c4: { name: 'ISO 10816-1 · Clase IV (grandes, base flexible)', limits: [2.8, 7.1, 18.0] },
  g2r: { name: 'ISO 10816-3 · Grupo 2 (15–300 kW), base rígida', limits: [1.4, 2.8, 4.5] },
  g2f: { name: 'ISO 10816-3 · Grupo 2 (15–300 kW), base flexible', limits: [2.3, 4.5, 7.1] },
  g1r: { name: 'ISO 10816-3 · Grupo 1 (300 kW–50 MW), base rígida', limits: [2.3, 4.5, 7.1] },
  g1f: { name: 'ISO 10816-3 · Grupo 1 (300 kW–50 MW), base flexible', limits: [3.5, 7.1, 11.0] },
};

const ZONES = {
  A: ['Zona A · Buena', 'Vibración típica de máquinas nuevas o recién reparadas.'],
  B: ['Zona B · Satisfactoria', 'Aceptable para operación continua sin restricciones.'],
  C: ['Zona C · Insatisfactoria', 'No apta para operación continua prolongada: planificar la corrección.'],
  D: ['Zona D · Inaceptable', 'Vibración suficiente para dañar la máquina: intervenir cuanto antes.'],
};

export function evaluateSeverity(vRms, classKey) {
  const c = MACHINE_CLASSES[classKey] || MACHINE_CLASSES.g2r;
  const l = c.limits;
  const zone = vRms < l[0] ? 'A' : vRms < l[1] ? 'B' : vRms < l[2] ? 'C' : 'D';
  return { vRms, zone, label: ZONES[zone][0], description: ZONES[zone][1], limits: l, standard: c.name };
}

export function bearingOrders({ balls, ballD, pitchD, contactDeg = 0 }) {
  const r = ballD / pitchD * Math.cos(contactDeg * Math.PI / 180);
  return {
    bpfo: balls / 2 * (1 - r),
    bpfi: balls / 2 * (1 + r),
    bsf: pitchD / (2 * ballD) * (1 - r * r),
    ftf: 0.5 * (1 - r),
  };
}

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

/**
 * Diagnóstico por reglas sobre el espectro de velocidad (mm/s pico).
 * @param {object} p { spectrum, runningHz, bearing?, blades?, lineHz?, axialRatio?, kurtosis? }
 */
export function diagnose({ spectrum: s, runningHz: f1, bearing = null, blades = 0, lineHz = 50, axialRatio = 0, kurtosis = 0 }) {
  if (!(f1 > 0)) return [];
  const tol = Math.max(s.resolution * 1.5, f1 * 0.02);
  const A = (o) => s.peakNear(f1 * o, tol).amp;
  const a1 = A(1), a2 = A(2), a3 = A(3), aHalf = A(0.5), aWhirl = Math.max(A(0.43), A(0.47));
  const overall = s.bandRms(Math.max(1, s.resolution), s.freqs[s.freqs.length - 1]) * Math.SQRT2;
  const floor = Math.max(overall * 0.04, 1e-6);
  const res = [];
  const fx = (v, d = 2) => v.toFixed(d).replace('.', ',');

  if (a1 > floor * 3) {
    const share = a1 / Math.max(overall, 1e-9), harm = (a2 + a3) / Math.max(a1, 1e-9);
    const conf = clamp(share * (1 - Math.min(harm, 1)) * 1.1, 0, 0.95);
    if (conf > 0.35) res.push({ type: 'unbalance', title: 'Desbalance', confidence: conf,
      evidence: `1X = ${fx(a1)} mm/s pico (${Math.round(share * 100)} % de la vibración global); 2X+3X = ${Math.round(harm * 100)} % de 1X.`,
      recommendation: 'Confirmar con fase radial estable y desfase horizontal–vertical ≈ 90°. Balancear en 1 o 2 planos.' });
  }
  if (a2 > floor * 3 && a2 > 0.5 * a1) {
    const conf = clamp(0.35 + 0.4 * Math.min(a2 / Math.max(a1, 1e-9), 1.5) / 1.5 + (axialRatio > 0.5 ? 0.2 : 0), 0, 0.95);
    res.push({ type: 'misalignment', title: 'Desalineación', confidence: conf,
      evidence: `2X = ${fx(a2)} mm/s (${Math.round(a2 / Math.max(a1, 1e-9) * 100)} % de 1X)${axialRatio > 0 ? `; axial/radial = ${fx(axialRatio)}` : ''}.`,
      recommendation: 'Verificar alineación del acople (láser o relojes), pata coja y tensiones de cañerías. Medir fase axial a ambos lados del acople.' });
  }
  let harmonics = 0;
  for (let k = 2; k <= 10; k++) if (A(k) > Math.max(floor * 3, a1 * 0.1)) harmonics++;
  let halfOrders = 0;
  for (const o of [1.5, 2.5, 3.5]) if (A(o) > Math.max(floor * 3, a1 * 0.05)) halfOrders++;
  if (harmonics >= 4 || (harmonics >= 3 && halfOrders >= 1) || (aHalf > Math.max(floor * 3, a1 * 0.2) && harmonics >= 2)) {
    res.push({ type: 'looseness', title: 'Holgura mecánica', confidence: clamp(0.3 + 0.07 * harmonics + 0.1 * halfOrders, 0, 0.9),
      evidence: `${harmonics} armónicos de 1X sobre el 10 % de 1X y ${halfOrders} medios órdenes.`,
      recommendation: 'Revisar torque de bulones de base y tapas de cojinetes, fisuras en la base y juego del rodamiento en su alojamiento.' });
  }
  if (aWhirl > Math.max(floor * 3, a1 * 0.3)) {
    res.push({ type: 'oilwhirl', title: 'Remolino de aceite (oil whirl)', confidence: clamp(aWhirl / Math.max(a1, 1e-9), 0.3, 0.85),
      evidence: `Pico subsíncrono a 0,43–0,47X = ${fx(aWhirl)} mm/s.`,
      recommendation: 'Solo en cojinetes de fricción: revisar holgura del cojinete, viscosidad y temperatura del aceite, y carga del eje.' });
  }
  if (bearing && bearing.balls > 0) {
    const b = bearingOrders(bearing);
    const check = (type, name, order, where) => {
      const fa = A(order), fb = A(order * 2);
      if (fa > floor * 2.5 || fb > floor * 2.5) {
        res.push({ type, title: name, confidence: clamp(0.35 + (fa + fb) / Math.max(overall, 1e-9) + (kurtosis > 4 ? 0.2 : 0), 0, 0.9),
          evidence: `Pico en ${fx(order)}X (${fx(order * f1, 1)} Hz) = ${fx(fa, 3)} mm/s; 2× = ${fx(fb, 3)} mm/s${kurtosis > 0 ? `; curtosis ${fx(kurtosis, 1)}` : ''}.`,
          recommendation: `Defecto probable en ${where}. Confirmar con espectro de envolvente en aceleración y planificar el cambio del rodamiento.` });
      }
    };
    check('bpfo', 'Rodamiento: pista externa (BPFO)', b.bpfo, 'la pista externa');
    check('bpfi', 'Rodamiento: pista interna (BPFI)', b.bpfi, 'la pista interna');
    check('bsf', 'Rodamiento: elementos rodantes (BSF)', b.bsf * 2, 'los elementos rodantes');
  }
  if (blades > 1) {
    const ab = A(blades);
    if (ab > Math.max(floor * 3, a1 * 0.5)) res.push({ type: 'bladepass', title: 'Paso de álabes elevado', confidence: clamp(ab / Math.max(a1, 1e-9) * 0.5, 0.3, 0.85),
      evidence: `${blades}X (${fx(blades * f1, 1)} Hz) = ${fx(ab)} mm/s.`,
      recommendation: 'Revisar huelgo entre rotor y carcasa/difusor, álabes dañados y condiciones de flujo (recirculación, cavitación).' });
  }
  const fe = 2 * lineHz;
  const pe = s.peakNear(fe, Math.max(s.resolution * 1.5, 0.5));
  if (pe.amp > Math.max(floor * 3, a1 * 0.3) && Math.abs(pe.freq - 2 * f1) > tol) {
    res.push({ type: 'electrical', title: 'Problema eléctrico (2× red)', confidence: 0.5,
      evidence: `Pico en ${fe} Hz = ${fx(pe.amp)} mm/s, separado de 2X (${fx(2 * f1, 1)} Hz).`,
      recommendation: 'Verificar entrehierro, desequilibrio de fases y barras del rotor. Si desaparece al cortar la alimentación, es eléctrico.' });
  }
  return res.sort((x, y) => y.confidence - x.confidence);
}
