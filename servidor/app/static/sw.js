// Service worker do painel: guarda so a pagina (rede primeiro) para abrir mesmo sem o servidor
// e mostrar o aviso de sem conexao. A API nunca vai para o cache.
const CACHE = "casa-pagina-1";
self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", e => e.waitUntil(
  caches.keys().then(ks => Promise.all(ks.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim())));
self.addEventListener("fetch", e => {
  const r = e.request;
  if (r.method !== "GET" || r.mode !== "navigate") return;
  e.respondWith(fetch(r).then(res => {
    if (res.ok) { const c = res.clone(); caches.open(CACHE).then(k => k.put("/", c)); }
    return res;
  }).catch(() => caches.match("/")));
});
