// Comprobaciones del sitio publicado en Cloudflare Pages (carpeta web/).
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative } from 'node:path';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const web = join(root, 'web');
const read = (p) => readFileSync(join(root, p), 'utf8');
const walk = (d) => readdirSync(d).flatMap((f) => (statSync(join(d, f)).isDirectory() ? walk(join(d, f)) : [join(d, f)]));

test('El service worker guarda archivos que existen y la versión coincide en todo el producto', () => {
  const sw = read('web/sw.js');
  const list = JSON.parse(sw.match(/const PRECACHE = (\[[\s\S]*?\]);/)[1].replace(/'/g, '"').replace(/,\s*\]/, ']'));
  for (const p of list) {
    const f = p.endsWith('/') ? join(web, p, 'index.html') : join(web, p);
    assert.ok(existsSync(f), `falta ${p}`);
  }
  const jsFiles = walk(join(web, 'js')).map((f) => '/' + relative(web, f).replace(/\\/g, '/'));
  for (const f of jsFiles) assert.ok(list.includes(f), `${f} no está en la lista del service worker (no andaría sin conexión)`);
  const v = sw.match(/const VERSION = '([^']+)'/)[1];
  assert.equal(read('src/VibrationAnalysis.UI/VibrationAnalysis.UI.csproj').match(/<Version>([^<]+)<\/Version>/)[1], v, 'versión del escritorio');
  assert.equal(read('web/js/core/report.js').match(/PRODUCT_VERSION = '([^']+)'/)[1], v, 'versión del informe web');
});

test('Sin scripts en línea (la política de seguridad solo permite archivos propios)', () => {
  const csp = read('web/_headers').match(/Content-Security-Policy: (.*)/)[1];
  assert.match(csp, /script-src 'self';/);
  for (const f of walk(web).filter((x) => x.endsWith('.html'))) {
    const html = readFileSync(f, 'utf8');
    for (const tag of html.match(/<script\b[^>]*>/g) || []) assert.match(tag, /\bsrc=/, `${relative(root, f)}: ${tag}`);
    assert.doesNotMatch(html, /\son[a-z]+="/i, `${relative(root, f)}: manejador de evento en línea`);
  }
});

test('No se publican archivos de desarrollo', () => {
  const bad = walk(web).map((f) => relative(web, f).replace(/\\/g, '/')).filter((f) => /(^|\/)(tests?|node_modules)\/|\.test\.|package-lock|\.map$/.test(f));
  assert.deepEqual(bad, []);
});

test('Manifiesto de la app instalable', () => {
  const m = JSON.parse(read('web/manifest.webmanifest'));
  assert.equal(m.start_url, '/app/');
  for (const i of m.icons) assert.ok(existsSync(join(web, i.src)), i.src);
  assert.ok(m.icons.some((i) => i.sizes === '512x512' && i.purpose === 'maskable'));
});
