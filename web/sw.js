// Service worker: la app funciona sin conexión (en planta, sin señal).
// Red primero para que cada publicación se vea al instante; si no hay red, se usa la copia guardada.
const VERSION = '3.1.0';
const CACHE = `va-${VERSION}`;
const FONTS = 'va-fuentes';
const PRECACHE = [
  '/', '/app/', '/404.html', '/manifest.webmanifest',
  '/img/icon.svg', '/img/icon-192.png', '/img/icon-512.png',
  '/js/landing.js',
  '/js/core/advanced.js', '/js/core/analysis.js', '/js/core/balance.js', '/js/core/dsp.js',
  '/js/core/io.js', '/js/core/report.js', '/js/core/svgchart.js', '/js/core/trend.js',
  '/js/ui/app.js', '/js/ui/charts.js', '/js/ui/pro.js',
];

self.addEventListener('install', (e) => {
  e.waitUntil(caches.open(CACHE).then((c) => c.addAll(PRECACHE)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (e) => {
  e.waitUntil(caches.keys()
    .then((keys) => Promise.all(keys.filter((k) => k !== CACHE && k !== FONTS).map((k) => caches.delete(k))))
    .then(() => self.clients.claim()));
});

self.addEventListener('fetch', (e) => {
  const req = e.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);
  // Fuentes de Google: copia guardada primero (no cambian)
  if (url.hostname === 'fonts.googleapis.com' || url.hostname === 'fonts.gstatic.com') {
    e.respondWith(caches.open(FONTS).then(async (c) => {
      const hit = await c.match(req);
      if (hit) return hit;
      const res = await fetch(req);
      if (res.ok || res.type === 'opaque') c.put(req, res.clone());
      return res;
    }).catch(() => new Response('', { status: 504 })));
    return;
  }
  if (url.origin !== self.location.origin) return;
  e.respondWith((async () => {
    try {
      const res = await fetch(req);
      if (res.ok) { const c = await caches.open(CACHE); c.put(req, res.clone()); }
      return res;
    } catch {
      const hit = await caches.match(req, { ignoreSearch: true });
      if (hit) return hit;
      if (req.mode === 'navigate') return (await caches.match(url.pathname.startsWith('/app') ? '/app/' : '/')) || caches.match('/404.html');
      return new Response('Sin conexión', { status: 503, headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
    }
  })());
});
