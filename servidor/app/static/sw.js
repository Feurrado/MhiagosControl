// Service worker minimo para o painel instalar como app. Nao guarda nada em cache:
// sem o servidor nao ha o que mostrar, e a API sempre vai para a rede.
self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", e => e.waitUntil(self.clients.claim()));
self.addEventListener("fetch", () => {});
