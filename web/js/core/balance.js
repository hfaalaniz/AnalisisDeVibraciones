// Balanceo de campo por coeficientes de influencia (1 y 2 planos), ISO 1940 y utilidades.
// Equivalente a VibrationAnalysis.Core.Balancing (C#). Un fasor es { mag, ang } (ángulo en grados).

import { normalizeAngle, angleDiff, G } from './dsp.js';

// ── Números complejos { re, im } ──
export const C = {
  polar: (mag, deg) => ({ re: mag * Math.cos(deg * Math.PI / 180), im: mag * Math.sin(deg * Math.PI / 180) }),
  add: (a, b) => ({ re: a.re + b.re, im: a.im + b.im }),
  sub: (a, b) => ({ re: a.re - b.re, im: a.im - b.im }),
  mul: (a, b) => ({ re: a.re * b.re - a.im * b.im, im: a.re * b.im + a.im * b.re }),
  div: (a, b) => { const d = b.re * b.re + b.im * b.im; return { re: (a.re * b.re + a.im * b.im) / d, im: (a.im * b.re - a.re * b.im) / d }; },
  neg: (a) => ({ re: -a.re, im: -a.im }),
  abs: (a) => Math.hypot(a.re, a.im),
};
export const toC = (p) => C.polar(p.mag, p.ang);
export const toP = (c) => ({ mag: C.abs(c), ang: normalizeAngle(Math.atan2(c.im, c.re) * 180 / Math.PI) });
export const padd = (a, b) => toP(C.add(toC(a), toC(b)));
export const psub = (a, b) => toP(C.sub(toC(a), toC(b)));

export function checkTrial(original, withTrial) {
  const dA = original.mag > 0 ? Math.abs(withTrial.mag - original.mag) / original.mag * 100 : 100;
  const dP = Math.abs(angleDiff(withTrial.ang, original.ang));
  const vec = original.mag > 0 ? psub(withTrial, original).mag / original.mag * 100 : 100;
  const ok = dA >= 30 || dP >= 30 || vec >= 30;
  let msg = ok
    ? `Corrida de prueba válida: cambio de amplitud ${Math.round(dA)} %, de fase ${Math.round(dP)}°.`
    : `El peso de prueba produjo poco efecto (amplitud ${Math.round(dA)} %, fase ${Math.round(dP)}°). Aumentarlo o cambiar su posición para mejorar la precisión.`;
  if (ok && withTrial.mag > original.mag * 2.5) msg += ' Atención: la vibración con peso de prueba subió mucho; usar un peso menor si la máquina lo exige.';
  return { ok, ampChange: dA, phaseChange: dP, message: msg };
}

export function singlePlane(o, t, r) {
  if (!(t.mag > 0)) throw new Error('El peso de prueba debe ser mayor que cero.');
  const alpha = C.div(C.sub(toC(r), toC(o)), toC(t));
  if (C.abs(alpha) < 1e-12) throw new Error('El peso de prueba no produjo ningún cambio medible.');
  const w = C.div(C.neg(toC(o)), alpha);
  return { alpha, correction: toP(w), correctionKeep: toP(C.sub(w, toC(t))), check: checkTrial(o, r) };
}

export function trimSingle(alpha, residual) { return toP(C.div(C.neg(toC(residual)), alpha)); }

export function twoPlanes(o1, o2, t1, r11, r21, t2, r12, r22) {
  if (!(t1.mag > 0 && t2.mag > 0)) throw new Error('Los pesos de prueba deben ser mayores que cero.');
  const O1 = toC(o1), O2 = toC(o2), T1 = toC(t1), T2 = toC(t2);
  const a11 = C.div(C.sub(toC(r11), O1), T1), a21 = C.div(C.sub(toC(r21), O2), T1);
  const a12 = C.div(C.sub(toC(r12), O1), T2), a22 = C.div(C.sub(toC(r22), O2), T2);
  const det = C.sub(C.mul(a11, a22), C.mul(a12, a21));
  if (C.abs(det) < 1e-14) throw new Error('La matriz de influencia es singular: los dos planos producen el mismo efecto.');
  const w1 = C.div(C.add(C.mul(C.neg(O1), a22), C.mul(O2, a12)), det);
  const w2 = C.div(C.add(C.mul(C.neg(O2), a11), C.mul(O1, a21)), det);
  const m = [[a11, a12], [a21, a22]];
  return {
    matrix: m, w1: toP(w1), w2: toP(w2), w1Keep: toP(C.sub(w1, T1)), w2Keep: toP(C.sub(w2, T2)),
    check1: checkTrial(o1, r11), check2: checkTrial(o2, r22), condition: condition(m),
  };
}

export function trimTwo(m, res1, res2) {
  const det = C.sub(C.mul(m[0][0], m[1][1]), C.mul(m[0][1], m[1][0]));
  const r1 = toC(res1), r2 = toC(res2);
  return [toP(C.div(C.add(C.mul(C.neg(r1), m[1][1]), C.mul(r2, m[0][1])), det)), toP(C.div(C.add(C.mul(C.neg(r2), m[0][0]), C.mul(r1, m[1][0])), det))];
}

export function condition(a) {
  const fro = (m) => Math.sqrt(m.flat().reduce((s, c) => s + C.abs(c) ** 2, 0));
  const det = C.sub(C.mul(a[0][0], a[1][1]), C.mul(a[0][1], a[1][0]));
  const inv = [[C.div(a[1][1], det), C.div(C.neg(a[0][1]), det)], [C.div(C.neg(a[1][0]), det), C.div(a[0][0], det)]];
  return fro(a) * fro(inv) / 2;
}

// ── Sesión guiada ──
export const STEPS = ['initial', 'trial1', 'trial2', 'correction', 'verification', 'done'];

export class BalancingSession {
  constructor(planes, { againstRotation = true, keepTrial = false, tolerance = 1 } = {}) {
    if (planes !== 1 && planes !== 2) throw new Error('Planos: 1 o 2.');
    Object.assign(this, { planes, againstRotation, keepTrial, tolerance });
    this.step = 'initial';
    this.runs = [];
    this.corrections = [];
    this.trim = [];
    this.single = null; this.two = null;
  }
  _in(m) { return this.againstRotation ? m : { mag: m.mag, ang: normalizeAngle(-m.ang) }; }
  _out(m) { return this._in(m); }
  _add(name, s1, s2, rpm, trial = null, plane = 0) {
    const r = { name, s1, s2: s2 ?? null, rpm, trial, plane, time: new Date().toISOString() };
    this.runs.push(r); return r;
  }
  _need(ok, msg) { if (!ok) throw new Error(msg); }
  get initial() { return this.runs.find((r) => r.name === 'Original'); }
  get trial1Run() { return this.runs.find((r) => r.plane === 1); }
  get trial2Run() { return this.runs.find((r) => r.plane === 2); }

  recordInitial(s1, s2, rpm) {
    this._need(this.step === 'initial', 'La medición inicial ya se registró.');
    this._need(this.planes === 1 || s2, 'El balanceo en dos planos necesita la lectura de los dos sensores.');
    this._add('Original', s1, s2, rpm);
    this.step = 'trial1';
  }
  recordTrial1(t, s1, s2, rpm) {
    this._need(this.step === 'trial1', 'Primero registrar la medición inicial.');
    this._need(this.planes === 1 || s2, 'Faltan las lecturas del sensor 2.');
    this._add('Prueba plano 1', s1, s2, rpm, t, 1);
    if (this.planes === 1) {
      this.single = singlePlane(this.initial.s1, this._in(t), s1);
      this.corrections = [this._out(this.keepTrial ? this.single.correctionKeep : this.single.correction)];
      this.step = 'correction';
      return this.single.check;
    }
    this.step = 'trial2';
    return checkTrial(this.initial.s1, s1);
  }
  recordTrial2(t, s1, s2, rpm) {
    this._need(this.step === 'trial2', 'Primero registrar la corrida de prueba del plano 1.');
    this._add('Prueba plano 2', s1, s2, rpm, t, 2);
    const i = this.initial, t1 = this.trial1Run;
    this.two = twoPlanes(i.s1, i.s2, this._in(t1.trial), t1.s1, t1.s2, this._in(t), s1, s2);
    this.corrections = this.keepTrial ? [this._out(this.two.w1Keep), this._out(this.two.w2Keep)] : [this._out(this.two.w1), this._out(this.two.w2)];
    this.step = 'correction';
    return this.two.check2;
  }
  predictedResidual() {
    const i = this.initial;
    if (!i) return [];
    if (this.single) {
      let w = this._in(this.corrections[0]);
      if (this.keepTrial) w = padd(w, this._in(this.trial1Run.trial));
      return [toP(C.add(toC(i.s1), C.mul(this.single.alpha, toC(w))))];
    }
    if (this.two) {
      let w1 = this._in(this.corrections[0]), w2 = this._in(this.corrections[1]);
      if (this.keepTrial) { w1 = padd(w1, this._in(this.trial1Run.trial)); w2 = padd(w2, this._in(this.trial2Run.trial)); }
      const m = this.two.matrix;
      const r = (k, O) => toP(C.add(C.add(toC(O), C.mul(m[k][0], toC(w1))), C.mul(m[k][1], toC(w2))));
      return [r(0, i.s1), r(1, i.s2)];
    }
    return [];
  }
  get canVerify() { return this.step === 'correction' || this.step === 'verification'; }
  recordVerification(s1, s2, rpm) {
    this._need(this.canVerify, 'Primero calcular la corrección.');
    const n = this.runs.filter((r) => r.name.startsWith('Verificación')).length;
    this._add(n === 0 ? 'Verificación' : `Verificación ${n + 1}`, s1, s2, rpm);
    const worst = Math.max(s1.mag, s2?.mag ?? 0);
    if (worst <= this.tolerance) { this.step = 'done'; this.trim = []; return { ok: true, trim: [] }; }
    this.step = 'verification';
    this.trim = this.planes === 1 ? [this._out(trimSingle(this.single.alpha, s1))] : trimTwo(this.two.matrix, s1, s2).map((w) => this._out(w));
    return { ok: false, trim: this.trim };
  }
  sensitivities() {
    if (this.single) return [C.abs(this.single.alpha)];
    if (this.two) return [C.abs(this.two.matrix[0][0]), C.abs(this.two.matrix[1][1])];
    return [];
  }
  toJSON() {
    return { planes: this.planes, againstRotation: this.againstRotation, keepTrial: this.keepTrial, tolerance: this.tolerance, runs: this.runs };
  }
  /** Reconstruye la sesión reproduciendo las corridas guardadas (formato compatible con el .vaproj de escritorio). */
  static fromRuns(data) {
    const s = new BalancingSession(data.planes, data);
    for (const r of data.runs || []) {
      if (s.step === 'initial') s.recordInitial(r.s1, r.s2, r.rpm);
      else if (s.step === 'trial1' && r.plane === 1) s.recordTrial1(r.trial, r.s1, r.s2, r.rpm);
      else if (s.step === 'trial2' && r.plane === 2) s.recordTrial2(r.trial, r.s1, r.s2, r.rpm);
      else if (s.canVerify) s.recordVerification(r.s1, r.s2, r.rpm);
    }
    return s;
  }
}

// ── ISO 1940, peso de prueba, reparto y perforación ──
export const GRADES = [0.4, 1, 2.5, 6.3, 16, 40, 100, 250, 630, 1600, 4000];
export function gradeExamples(g) {
  if (g <= 0.4) return 'Giróscopos, husillos de precisión';
  if (g <= 1) return 'Rectificadoras, accionamientos de grabadores';
  if (g <= 2.5) return 'Turbinas de gas y vapor, turbocompresores, accionamientos de máquinas herramienta';
  if (g <= 6.3) return 'Ventiladores, bombas, rotores de motores eléctricos, volantes, maquinaria de proceso';
  if (g <= 16) return 'Ejes cardán, partes de máquinas agrícolas, cigüeñales de motores';
  if (g <= 40) return 'Llantas y ruedas de automóviles, cigüeñales de motores rápidos';
  if (g <= 100) return 'Motores diésel de alta velocidad completos';
  return 'Cigüeñales de motores grandes y lentos';
}
export const permissibleUnbalance = (grade, massKg, rpm) => rpm > 0 ? 9549 * grade * massKg / rpm : Infinity;
export const permissibleEccentricity = (grade, rpm) => rpm > 0 ? 9549 * grade / rpm : Infinity;
export const residualUnbalance = (vib, sensPerGram, radiusMm) => sensPerGram > 0 ? vib / sensPerGram * radiusMm : Infinity;

export function suggestTrialGrams(massKg, rpm, radiusMm, fraction = 0.1) {
  if (!(rpm > 0 && radiusMm > 0)) return 0;
  const w = rpm * 2 * Math.PI / 60;
  return fraction * massKg * G / (radiusMm / 1000 * w * w) * 1000;
}

export function splitWeight(w, positions, first = 0) {
  if (positions < 2) throw new Error('Se necesitan al menos 2 posiciones.');
  const step = 360 / positions;
  const rel = normalizeAngle(w.ang - first);
  const a = Math.floor(rel / step) % positions, b = (a + 1) % positions;
  const alpha = (rel - a * step) * Math.PI / 180, beta = step * Math.PI / 180 - alpha, s = Math.sin(step * Math.PI / 180);
  return {
    posA: a, angA: normalizeAngle(first + a * step), massA: w.mag * Math.sin(beta) / s,
    posB: b, angB: normalizeAngle(first + b * step), massB: w.mag * Math.sin(alpha) / s,
  };
}

export function drillDepthMm(massG, density, drillMm) {
  if (massG <= 0) return 0;
  const r = drillMm / 2 / 1000, vol = massG / 1000 / density;
  const tip = r / Math.tan(59 * Math.PI / 180), cone = Math.PI * r * r * tip / 3;
  const depth = vol <= cone ? Math.cbrt(vol * 3 * tip * tip / (Math.PI * r * r)) : tip + (vol - cone) / (Math.PI * r * r);
  return depth * 1000;
}

// ── Máquina ──
export const MATERIALS = { 'Acero': 7850, 'Acero inoxidable': 8000, 'Hierro fundido': 7200, 'Aluminio': 2700, 'Bronce': 8800, 'Titanio': 4500, 'Fibra de carbono': 1600 };

export function estimatedMass(m) {
  const r = m.diameterMm / 2000, ri = (m.innerDiameterMm || 0) / 2000, l = m.lengthMm / 1000;
  let vol = Math.PI * (r * r - (m.rotorType === 'hollow' ? ri * ri : 0)) * l;
  if (m.rotorType === 'impeller') vol *= 0.45;
  return vol * (MATERIALS[m.material] || 7850);
}
export const effectiveMass = (m) => (m.massKg > 0 ? m.massKg : estimatedMass(m));

export function recommendedPlanes(m) {
  if (!(m.diameterMm > 0)) return { planes: 2, reason: 'Sin diámetro: usar dos planos.' };
  const ld = m.lengthMm / m.diameterMm, f = (v) => v.toFixed(2).replace('.', ',');
  if (ld < 0.5) return { planes: 1, reason: `Rotor tipo disco (L/D = ${f(ld)} < 0,5): alcanza con un plano.` };
  if (ld <= 2 && m.rpm < 1000) return { planes: 1, reason: `L/D = ${f(ld)} y ${Math.round(m.rpm)} rpm < 1000: un plano suele alcanzar; usar dos si queda desbalance de par.` };
  return { planes: 2, reason: `L/D = ${f(ld)}${m.rpm >= 1000 ? ` a ${Math.round(m.rpm)} rpm` : ''}: hay desbalance de par, conviene balancear en dos planos.` };
}
