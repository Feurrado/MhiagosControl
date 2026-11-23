// Script do painel da Casa. Fica fora do index.html para a politica da pagina
// recusar script embutido: um texto injetado na pagina nao roda e nao leva a chave.
"use strict";
let chave = "";
let ajSecao = "alertas", ajAberto = false;   // secao dos Ajustes; no celular, se o detalhe esta aberto
try { chave = localStorage.getItem("chave") || ""; } catch (e) {}
let estado = null, pref = null, abaAtual = "painel";
const pendente = {};          // puid -> {chave: valor} mandado e ainda nao confirmado pela leitura
const ui = {};                // puid -> {diag, props, consumo}: o que esta aberto em cada cartao
const nos = new Map();        // puid -> {sig, el}: so redesenha o cartao que mudou
const $ = id => document.getElementById(id);

// ---------------- base ----------------

/**
 * A barra de carregamento global, no alto da tela: aparece quando algum pedido passa de 300 ms
 * (a espera longa das mudancas nao conta) e some quando o ultimo termina.
 */
const carga = { n: 0, t: 0 };
function cargaMais() {
  if (carga.n++ === 0) carga.t = setTimeout(() => { const b = document.getElementById("carga"); if (b) b.classList.add("vis"); }, 300);
}
function cargaMenos() {
  if (--carga.n > 0) return;
  carga.n = 0; clearTimeout(carga.t);
  const b = document.getElementById("carga"); if (b) b.classList.remove("vis");
}

async function api(caminho, metodo, corpo) {
  const longa = caminho.startsWith("mudancas");
  if (!longa) cargaMais();
  try { return await apiDireto(caminho, metodo, corpo); } finally { if (!longa) cargaMenos(); }
}

async function apiDireto(caminho, metodo, corpo) {
  const r = await fetch("/api/" + caminho, { method: metodo || "GET",
    headers: { "Authorization": "Bearer " + chave, "Content-Type": "application/json" },
    body: corpo === undefined ? undefined : JSON.stringify(corpo) });
  if (r.status === 401) { pedirChave(); throw new Error("chave"); }
  const tipo = r.headers.get("content-type") || "";
  const j = tipo.includes("json") ? await r.json() : await r.text();
  if (!r.ok) throw new Error((j && (j.erro || j.detail)) || ("HTTP " + r.status));
  return j;
}

function el(tag, attrs, ...filhos) {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v == null) continue;
    if (k === "on") for (const [ev, f] of Object.entries(v)) e.addEventListener(ev, f);
    else if (k === "class") e.className = v;
    else if (k === "style") e.style.cssText = v;
    else if (k in e) e[k] = v; else e.setAttribute(k, v);
    if (k === "title" && tag === "button" && !(attrs && attrs["aria-label"])) e.setAttribute("aria-label", v);
  }
  for (const f of filhos.flat(Infinity)) if (f != null && f !== false) e.append(f);
  return e;
}

function svg(conteudo, viewBox) {
  const s = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  s.setAttribute("viewBox", viewBox || "0 0 24 24");
  s.innerHTML = conteudo;
  return s;
}

function aviso(t, acao) {
  const x = $("toast");
  x.textContent = t; x.classList.add("vis");
  x.classList.toggle("acao", !!acao);
  if (acao) x.append(el("button", { class: "b peq", style: "margin-left:12px", on: { click: () => { x.classList.remove("vis"); acao.fazer(); } } }, acao.texto));
  clearTimeout(aviso.t); aviso.t = setTimeout(() => x.classList.remove("vis"), acao ? 6000 : 3500);
}
/** Roda uma animacao de entrada (classe m3-*) de novo no mesmo elemento. */
function animar(e, classe) {
  if (!e) return;
  e.classList.remove("m3-aba", "m3-frente", "m3-tras", "m3-surge", "m3-abre");
  void e.offsetWidth;          // reinicia a animacao
  e.classList.add(classe);
  e.addEventListener("animationend", () => e.classList.remove(classe), { once: true });
}

const semMovimento = () => matchMedia("(prefers-reduced-motion: reduce)").matches;
/** Abre um bloco crescendo a altura (como o expand do Material). */
function expandir(e) {
  if (!e) return;
  if (semMovimento() || !e.animate) { mostrarInteiro(e); return; }
  const cs = getComputedStyle(e), h = e.offsetHeight;
  e.style.overflow = "hidden";
  e.animate([{ height: "0px", opacity: 0, paddingTop: "0px", paddingBottom: "0px", marginTop: "0px" },
             { height: h + "px", opacity: 1, paddingTop: cs.paddingTop, paddingBottom: cs.paddingBottom, marginTop: cs.marginTop }],
    { duration: 300, easing: "cubic-bezier(.05,.7,.1,1)" })
    .onfinish = () => { e.style.overflow = ""; mostrarInteiro(e); };
}
/** Rola a pagina so o necessario para o bloco aberto nao ficar escondido atras da barra de abas. */
function mostrarInteiro(e) {
  if (!e.isConnected) return;
  const r = e.getBoundingClientRect(), barra = matchMedia("(max-width:760px)").matches ? 84 : 0;
  const baixo = innerHeight - barra - 12;
  if (r.bottom <= baixo) return;
  const quanto = Math.min(r.bottom - baixo, r.top - 72);   // sem empurrar o topo do bloco para fora da tela
  if (quanto > 0) scrollBy({ top: quanto, behavior: semMovimento() ? "auto" : "smooth" });
}
/**
 * Troca o conteudo de uma caixa sem pulo: a altura vai suavemente da antiga para a nova,
 * em vez de encolher e crescer (o que fazia piscar tudo o que vem abaixo).
 */
function trocarConteudo(caixa, ...filhos) {
  // imediato: a troca e a primeira pintura de uma aba que acabou de abrir. A altura "de antes" e a de um conteudo
  // velho ou do "Carregando..." de reserva, e animar dela ate a nova faz a pagina crescer na frente de quem entrou.
  const imediato = trocarConteudo.imediato;
  trocarConteudo.imediato = false;
  const h0 = caixa.offsetHeight;
  caixa.style.minHeight = "";
  caixa.replaceChildren(...filhos);
  caixa.classList.remove("recarregando");
  let h1 = caixa.offsetHeight;
  // Encolher perto do fim da pagina obriga o navegador a puxar a rolagem a cada quadro,
  // e no Android a barra de abas fixa treme junto. Nesse caso a caixa guarda a altura antiga.
  const sobra = document.documentElement.scrollHeight - (scrollY + innerHeight);
  if (h1 < h0 && sobra < h0 - h1) { caixa.style.minHeight = h0 + "px"; h1 = h0; }
  if (!h0 || imediato || Math.abs(h1 - h0) < 3 || semMovimento() || !caixa.animate) return;
  caixa.style.overflow = "hidden";
  caixa.animate([{ height: h0 + "px" }, { height: h1 + "px" }], { duration: 260, easing: "cubic-bezier(.2,0,0,1)" })
    .onfinish = () => { caixa.style.overflow = ""; };
}
/** Fecha um bloco encolhendo a altura e so depois chama depois(). */
function recolher(e, depois) {
  if (!e || semMovimento() || !e.animate) { depois(); return; }
  e.style.overflow = "hidden";
  const an = e.animate([{ height: e.offsetHeight + "px", opacity: 1 }, { height: "0px", opacity: 0, paddingTop: "0px", paddingBottom: "0px", marginTop: "0px" }],
    { duration: 200, easing: "cubic-bezier(.3,0,.8,.15)", fill: "forwards" });
  an.onfinish = depois;
}

// Onda do toque (ripple do Android) nos botoes, so com o dedo: no mouse fica o hover de sempre.
const ALVOS_ONDA = ".b, .tile, .cena, .redondo, .aj-item, .colapsa-cab, .seg button, .liga, .chave-diag, .aj-voltar";
addEventListener("pointerdown", ev => {
  if (ev.pointerType === "mouse") return;
  const alvo = ev.target.closest(ALVOS_ONDA);
  if (!alvo || alvo.disabled) return;
  const r = alvo.getBoundingClientRect(), lado = Math.hypot(r.width, r.height) * 2;
  alvo.classList.add("m3-alvo");
  const o = document.createElement("span");
  o.className = "m3-onda";
  o.style.cssText = "width:" + lado + "px;height:" + lado + "px;left:" + (ev.clientX - r.left - lado / 2) + "px;top:" + (ev.clientY - r.top - lado / 2) + "px";
  alvo.appendChild(o);
  setTimeout(() => o.remove(), 550);
}, { passive: true });

const vibrar = ms => { try { if (navigator.vibrate) navigator.vibrate(ms); } catch (e) {} };

const hora = t => new Date(t * 1000).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
const dataHora = t => new Date(t * 1000).toLocaleString("pt-BR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });
const dur = min => min < 60 ? Math.max(0, Math.round(min)) + " min" : Math.floor(min / 60) + " h " + String(Math.round(min % 60)).padStart(2, "0");
const num = (v, c) => v == null ? "--" : Number(v).toLocaleString("pt-BR", { minimumFractionDigits: c, maximumFractionDigits: c });

function pedirChave() { $("chave").classList.remove("oculto"); }
$("salvarChave").onclick = () => {
  chave = $("ch").value.trim();
  try { localStorage.setItem("chave", chave); } catch (e) {}
  $("chave").classList.add("oculto");
  atualizar(); carregarPref();
};
$("abrirLogin").onclick = async () => {
  try { const r = await api("conta/login"); window.open(r.url, "_blank"); } catch (e) { aviso(e.message); }
};
$("enviarCodigo").onclick = async ev => {
  ev.target.disabled = true;
  try { await api("conta/codigo", "POST", { colado: $("colado").value }); atualizar(); }
  catch (e) { aviso("Falhou: " + e.message); }
  finally { ev.target.disabled = false; }
};
$("lerAgora").onclick = async ev => {
  ev.target.disabled = true;
  try { await api("aparelhos/ler", "POST"); await atualizar(); } catch (e) { aviso(e.message); }
  finally { ev.target.disabled = false; }
};
$("servidorNome").textContent = location.host;

/**
 * Aba e secao dos Ajustes ficam no endereco (#consumo, #ajustes-cenas): o voltar do Android
 * e do navegador navega dentro do painel em vez de fechar o app.
 */
const ABAS = ["painel", "clima", "consumo", "ajustes"];
function irPara(h, substituir) {
  if (("#" + h) === location.hash) { aplicarHash(); return; }
  if (substituir) { history.replaceState(null, "", "#" + h); aplicarHash(); }
  // pushState, nao location.hash: o hash "clima" (e "ajustes") e tambem o id de um bloco da pagina, e o navegador
  // rolava a Casa ate ele no quadro antes da troca de aba (era a Casa deslocada que aparecia por tras da animacao).
  else { history.pushState(null, "", "#" + h); aplicarHash(); }
}
function aplicarHash() {
  const h = location.hash.slice(1) || "painel";
  const [aba, sec, ancora] = h.split("-");
  if (aba === "ajustes" && sec === "metricas" && ancora) metAlvo = ancora;
  aplicarHash.daLista = aplicarHash.ultimo === "ajustes" && !!sec;   // o Voltar do detalhe pode usar o historico
  aplicarHash.ultimo = h;
  if (!ABAS.includes(aba)) { trocarAba("painel"); return; }
  if (aba === "ajustes") { ajAberto = !!sec; if (sec) ajSecao = sec; }
  trocarAba(aba);
}
addEventListener("hashchange", aplicarHash);
for (const b of document.querySelectorAll("nav .item")) b.onclick = () => irPara(b.dataset.aba);
function trocarAba(a) {
  const mudou = abaAtual !== a;
  abaAtual = a;
  for (const b of document.querySelectorAll("nav .item")) { const on = b.dataset.aba === a; b.classList.toggle("ativo", on); if (on) b.setAttribute("aria-current", "page"); else b.removeAttribute("aria-current"); }
  for (const s of ABAS) $("aba-" + s).classList.toggle("oculto", s !== a);
  if (a === "clima") {
    trocarConteudo.imediato = mudou;       // entrando na aba: a primeira pintura nao anima a altura (a propria aba ja entra com o fade)
    desenharClimaPagina();
    trocarConteudo.imediato = false;
    carregarClima();
  }
  if (a === "consumo") carregarConsumo();
  if (a === "ajustes") desenharAjustes();
  if (a === "painel") desenharCartoes();
  if (mudou) animar($("aba-" + a), "m3-aba");
  if (mudou || a === "ajustes") scrollTo(0, 0);
}

// ---------------- icones (como IconesDeCena e os desenhados no app) ----------------

const L = 'fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"';
const ICONES = {
  lua: '<path ' + L + ' d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z"/>',
  floco: '<g ' + L + '><path d="M12 2v20M3.3 7l17.4 10M3.3 17L20.7 7"/><path d="M9 4l3 2 3-2M9 20l3-2 3 2"/></g>',
  folha: '<path ' + L + ' d="M5 19C5 10 11 5 20 4c0 9-5 15-14 15zM5 19l8-8"/>',
  brilho: '<g ' + L + '><path d="M10 3.5l1.6 4.4L16 9.5l-4.4 1.6L10 15.5l-1.6-4.4L4 9.5l4.4-1.6z"/><path d="M17.5 14l.8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8z"/></g>',
  sol: '<g ' + L + '><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></g>',
  gota: '<path ' + L + ' d="M12 3c3 4 6 7.5 6 11a6 6 0 0 1-12 0c0-3.5 3-7 6-11z"/>',
  ventoinha: '<g ' + L + '><circle cx="12" cy="12" r="1.6"/><path d="M12 10c0-4 1-7 4-7 2 0 2 3 0 5l-4 2zM14 12c4 0 7 1 7 4 0 2-3 2-5 0l-2-4zM12 14c0 4-1 7-4 7-2 0-2-3 0-5l4-2zM10 12c-4 0-7-1-7-4 0-2 3-2 5 0l2 4z"/></g>',
  energia: '<path ' + L + ' d="M13 2L5 14h6l-1 8 8-12h-6z"/>',
  auto: '<g ' + L + '><circle cx="12" cy="12" r="9"/><path d="M8.5 16l3.5-9 3.5 9M9.8 13h4.4"/></g>',
};
const ICONE_MODO = { 0: "ventoinha", 1: "sol", 2: "floco", 3: "gota", 4: "auto", 5: "folha" };
const COR_MODO = { 0: "#8A93A6", 1: "#F08C3C", 2: "#2D7DF6", 3: "#00B0A9", 4: "#8B5CF6", 5: "#2EA043" };
const icone = n => svg(ICONES[n] || ICONES.floco);
const icTermometro = () => svg('<g ' + L + '><path d="M10 14.5V5a2 2 0 0 1 4 0v9.5a4 4 0 1 1-4 0z"/><path d="M12 9v7"/></g>');
const icAr = () => svg('<g ' + L + '><rect x="3" y="5" width="18" height="8.5" rx="2"/><path d="M6.5 10.5h11M8 16.5l-.8 2.5M12 16.5v3M16 16.5l.8 2.5"/></g>');   // o mesmo da Visao geral do PC
const icLavadora = () => svg('<g ' + L + '><rect x="4" y="3" width="16" height="18" rx="2.5"/><circle cx="12" cy="13" r="4.5"/><path d="M7.5 6.5h2M15 6.5h1.5"/></g>');
const icChave = () => svg('<path ' + L + ' d="M14.7 6.3a4 4 0 0 0-5.4 5.1L4 16.7V20h3.3l5.3-5.3a4 4 0 0 0 5.1-5.4l-2.5 2.5-2.3-.4-.4-2.3z"/>');
const icPower = () => svg('<g fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 3v8"/><path d="M6.6 6.6a7.5 7.5 0 1 0 10.8 0"/></g>');
const icSeta = () => svg('<path fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" d="M6 9l6 6 6-6"/>');
const icMenos = () => svg('<path fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" d="M6 12h12"/>');
const icMais = () => svg('<path fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" d="M6 12h12M12 6v12"/>');
function icBarras(nivel) {
  let s = "";
  for (let i = 0; i < 3; i++) s += '<rect x="' + (4 + i * 6) + '" y="' + (14 - i * 4) + '" width="4" height="' + (6 + i * 4) + '" rx="1" fill="currentColor" opacity="' + (i < nivel ? 1 : .3) + '"/>';
  return svg(s);
}

// ---------------- leitura ----------------

/** Controle segmentado com indicador que desliza (iOS): um span sob o botao "on" anda ate o novo item.
 *  Pilulas remontadas ao clicar (cidades, previsao) partem da posicao guardada pelo rotulo dos itens.
 *  .seg-nav: as abas da Casa quando a pagina esta embutida no app do PC (viram pilula la). */
const posDoSeg = {};
let cliqueNoSeg = 0;
const semMovimentoSeg = matchMedia("(prefers-reduced-motion: reduce)");
function posicionarSeg(semAnimar) {
  for (const g of document.querySelectorAll(".seg, .seg-nav")) {
    const bs = [...g.querySelectorAll("button")], on = g.querySelector("button.on, button.ativo");
    let i = g.querySelector(".ind");
    if (!i) { i = document.createElement("span"); i.className = "ind"; g.prepend(i); }
    if (!on || !on.offsetWidth) { i.style.opacity = "0"; continue; }
    const chave = bs.map(b => b.textContent).join("|"), novo = { x: on.offsetLeft, w: on.offsetWidth, t: g.scrollWidth };
    const antes = i._pos || (Date.now() - cliqueNoSeg < 1500 ? posDoSeg[chave] : null);
    i._pos = novo; posDoSeg[chave] = novo;
    // O indicador cobre a pilula toda e um recorte mostra so o item ativo: uma propriedade so anima, sem o
    // rastro que transform (compositor) e width (thread principal) deixavam fora de passo com a pagina ocupada.
    const recorte = p => "inset(0px " + Math.max(0, novo.t - p.x - p.w) + "px 0px " + p.x + "px round 999px)";
    i.style.opacity = "1"; i.style.width = novo.t + "px"; i.style.clipPath = recorte(novo);
    if (semAnimar !== true && antes && (antes.x !== novo.x || antes.w !== novo.w) && !semMovimentoSeg.matches && i.animate)
      i.animate([{ clipPath: recorte(antes) }, { clipPath: recorte(novo) }], { duration: 250, easing: "cubic-bezier(.4,0,.2,1)" });
  }
}
let posSegAgenda = 0;
document.addEventListener("click", e => { if (e.target.closest && e.target.closest(".seg, .seg-nav")) cliqueNoSeg = Date.now(); }, true);
new MutationObserver(() => {
  cancelAnimationFrame(posSegAgenda);
  // Logo depois de um clique a pilula parte no mesmo quadro em que o item muda.
  if (Date.now() - cliqueNoSeg < 500) { posicionarSeg(); return; }
  posSegAgenda = requestAnimationFrame(() => posicionarSeg());
})
  .observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ["class"] });
addEventListener("resize", () => requestAnimationFrame(() => posicionarSeg(true)));

/** O fundo ganha uma tinta suave do modo do ar: azul no frio, laranja no calor, verde no seco; sem tinta com o ar desligado. */
function tintaDoAr() {
  const ar = estado && estado.lista.find(ehAr);
  const m = ar && ar.estado && ar.estado.t_power === "1" ? { 2: "frio", 5: "eco", 1: "calor", 3: "seco" }[+ar.estado.t_work_mode] : null;
  if (m) document.body.dataset.modo = m; else delete document.body.dataset.modo;
}

async function atualizar() {
  if (!chave) { pedirChave(); return; }
  try {
    estado = await api("aparelhos");
    marcarConexao(false);
    $("login").classList.toggle("oculto", estado.logado);
    desenharSituacao();
    tintaDoAr();
    if (abaAtual === "painel") desenharCartoes();
    if (abaAtual === "painel" || abaAtual === "clima") atualizarSugestao();
  } catch (e) {
    if (e.message !== "chave") { $("situacao").textContent = "Sem resposta do servidor: " + e.message; marcarConexao(true); }
  }
}

let semConexao = false;
function marcarConexao(fora) {
  semConexao = fora;
  const b = $("semrede");
  if (fora) b.textContent = "Sem conexão com o servidor" + (estado && estado.ultimaLeitura ? ". Mostrando os dados de " + hora(estado.ultimaLeitura) + "." : ".");
  b.classList.toggle("oculto", !fora);
}
window.addEventListener("offline", () => marcarConexao(true));
window.addEventListener("online", () => { tudo(); });
const haQuanto = t => { const s = Math.max(0, Math.round(Date.now() / 1000 - t)); return s < 60 ? "há " + s + " s" : s < 3600 ? "há " + Math.floor(s / 60) + " min" : "às " + hora(t); };

function desenharSituacao() {
  const velho = estado.logado && estado.ultimaLeitura && Date.now() / 1000 - estado.ultimaLeitura > Math.max(180, (estado.periodo || 60) * 3);
  const t = !estado.logado ? "Sem a conta ConnectLife"
    : (estado.ultimaLeitura ? "Atualizado " + haQuanto(estado.ultimaLeitura) + (velho ? " (dados antigos)" : "") : "Lendo…") + "  ·  ConnectLife" + (estado.erro ? "  ·  " + estado.erro : "");
  if ($("situacao").textContent !== t) $("situacao").textContent = t;
  const r = estado.logado ? "Lendo a nuvem a cada " + estado.periodo + " s" : "";
  if ($("rodape").textContent !== r) $("rodape").textContent = r;
}

const enviadoEm = {};

/** O botao muda na hora e gira ate a leitura da nuvem confirmar (ou 20 s), como no app. */
const DESFAZIVEL = ["t_power", "t_work_mode", "t_fan_speed"];
async function comando(a, p, opts, semDesfazer) {
  vibrar(12);
  const anterior = {};
  for (const k of Object.keys(p)) anterior[k] = String(valor(a, k));
  const desfazivel = !semDesfazer && Object.keys(p).every(k => DESFAZIVEL.includes(k) && anterior[k] !== String(p[k]));
  pendente[a.puid] = Object.assign(pendente[a.puid] || {}, p);
  const quando = enviadoEm[a.puid] = Date.now();
  desenharCartoes();
  try { await api("aparelhos/" + a.puid + "/comando", "POST", Object.assign({ props: p }, opts || {})); }
  catch (e) { aviso("Comando falhou: " + e.message); delete pendente[a.puid]; desenharCartoes(); return; }
  if (desfazivel) aviso("Comando enviado", { texto: "Desfazer", fazer: () => comando(a, anterior, opts, true) });
  setTimeout(() => { if (enviadoEm[a.puid] === quando && pendente[a.puid]) { delete pendente[a.puid]; desenharCartoes(); } }, 20000);
}

/** Tira do pendente o que a leitura ja mostra com o valor pedido. */
function conferirPendentes() {
  for (const a of estado.lista) {
    const p = pendente[a.puid];
    if (!p || Date.now() - enviadoEm[a.puid] < 1500) continue;
    for (const k of Object.keys(p)) if (String(a.estado[k]) === String(p[k])) delete p[k];
    if (!Object.keys(p).length) delete pendente[a.puid];
  }
}


function valor(a, k) {
  const p = pendente[a.puid];
  return p && k in p ? p[k] : a.estado[k];
}

const ehAr = a => a.tipo === "008" || a.tipo === "009";
const ehLavadora = a => ["003", "025", "027", "004"].includes(a.tipo);
function agendamento(puid, nome) { return (estado.agendamentos || []).find(x => x.puid === puid && x.nome === nome); }
let ciclos = null;            // puid -> ciclos gravados (recarregados a cada minuto)
const ciclosDe = puid => (ciclos || {})[puid] || [];
const U = puid => ui[puid] || (ui[puid] = { diag: false, consumo: false });

/**
 * Redesenha so o cartao cujo conteudo mudou. Antes a pagina inteira era
 * refeita a cada 4 s: piscava e fechava o que estava aberto.
 */
/** Arranjo salvo da Casa: ids na ordem escolhida (os novos vao ao fim) e ids ocultos. */
function arranjo() {
  const i = (pref && pref.dados.inicio) || { ordem: [], ocultos: [] };
  return { ordem: i.ordem || [], ocultos: i.ocultos || [] };
}
function aparelhosNaOrdem() {
  const { ordem, ocultos } = arranjo();
  return aparelhosNaOrdemTodos(ordem).filter(a => !ocultos.includes(a.puid));
}

let editandoInicio = false;
function desenharEdicaoInicio() {
  const caixa = $("editaInicio");
  caixa.classList.toggle("oculto", !editandoInicio || !estado || !pref);
  $("personalizar").classList.toggle("pri", editandoInicio);
  if (!editandoInicio || !estado || !pref) return;
  const { ordem, ocultos } = arranjo();
  const todos = [{ id: "clima", nome: "Clima e sugestão" }].concat(
    aparelhosNaOrdemTodos(ordem).map(a => ({ id: a.puid, nome: a.apelido })));
  const gravar = (o, h) => gravarPref({ inicio: { ordem: o, ocultos: h } });
  const ids = todos.map(x => x.id);
  trocarConteudo(caixa, el("div", { class: "cab" }, el("div", { class: "nomes" }, el("div", { class: "nome" }, "Personalizar a Casa"),
    el("div", { class: "det" }, "mude a ordem e esconda o que não usa; vale para todos os aparelhos"))),
    el("div", { class: "corpo" }, todos.map((x, n) => el("div", { class: "ordem" + (ocultos.includes(x.id) ? " fora" : "") },
      el("span", {}, x.nome),
      x.id === "clima" ? null : el("button", { class: "b", disabled: n <= 1, "aria-label": "Subir", on: { click: () => { const l = ids.filter(i => i !== "clima"); const k = l.indexOf(x.id); [l[k - 1], l[k]] = [l[k], l[k - 1]]; gravar(l, ocultos); } } }, "▲"),
      x.id === "clima" ? null : el("button", { class: "b", disabled: n === todos.length - 1, "aria-label": "Descer", on: { click: () => { const l = ids.filter(i => i !== "clima"); const k = l.indexOf(x.id); [l[k + 1], l[k]] = [l[k], l[k + 1]]; gravar(l, ocultos); } } }, "▼"),
      el("button", { class: "b", on: { click: () => gravar(ordem.length ? ordem : ids.filter(i => i !== "clima"), ocultos.includes(x.id) ? ocultos.filter(i => i !== x.id) : ocultos.concat(x.id)) } }, ocultos.includes(x.id) ? "Mostrar" : "Ocultar"))),
      el("div", { class: "linha", style: "margin-top:10px" }, el("button", { class: "b", on: { click: () => gravar([], []) } }, "Restaurar o padrão"))));
}
function aparelhosNaOrdemTodos(ordem) {
  const pos = id => { const n = ordem.indexOf(id); return n < 0 ? 999 : n; };
  return estado.lista.map((a, n) => [a, n]).sort((x, y) => pos(x[0].puid) - pos(y[0].puid) || x[1] - y[1]).map(x => x[0]);
}
$("personalizar").onclick = () => { editandoInicio = !editandoInicio; desenharEdicaoInicio(); };

function desenharCartoes() {
  if (!estado) return;
  desenharEdicaoInicio();
  conferirPendentes();
  const c = $("cartoes");
  const foco = document.activeElement && c.contains(document.activeElement) ? document.activeElement.dataset.id : null;
  const lista = [];
  for (const a of aparelhosNaOrdem()) {
    const ags = (estado.agendamentos || []).filter(x => x.puid === a.puid);
    const falhos = (estado.agendamentosFalhos || []).filter(x => x.puid === a.puid);
    const sig = JSON.stringify([a, pendente[a.puid] || null, pref && pref.versao, U(a.puid), ags, falhos, ciclosDe(a.puid).length,
      ags.length ? Math.floor(Date.now() / 60000) : 0]);
    let r = nos.get(a.puid);
    if (r && arrastando === a.puid) { lista.push(r.el); continue; }
    if (!r || r.sig !== sig) {
      const novo = ehAr(a) ? cartaoAr(a) : ehLavadora(a) ? cartaoLavadora(a) : cartaoGenerico(a);
      if (r) {
        r.el.replaceWith(novo);
      } else {
        novo.style.animationDelay = (lista.length * 60) + "ms";
        animar(novo, "m3-surge");
      }
      r = { sig, el: novo };
      nos.set(a.puid, r);
    }
    lista.push(r.el);
  }
  lista.forEach((e, i) => { if (c.children[i] !== e) c.insertBefore(e, c.children[i] || null); });
  while (c.children.length > lista.length) c.lastChild.remove();
  // O que precisa da largura real (miniatura) ou do quadro anterior (anel) roda ja no lugar.
  for (const e of lista) for (const x of [e, ...e.querySelectorAll(".anel")]) if (x.depois) { const f = x.depois; x.depois = null; f(); }
  if (foco) { const e = c.querySelector('[data-id="' + foco + '"]'); if (e && e !== document.activeElement) e.focus(); }
}

function cabecalho(a, ic) {
  const u = U(a.puid);
  const falha = (a.diagnostico && a.diagnostico.ativas || []).length > 0 || (a.info && a.info.falhas && a.info.falhas.length > 0);
  return el("div", { class: "cab" },
    el("div", { class: "ico" }, ic),
    el("div", { class: "nomes" }, el("div", { class: "nome" }, a.apelido || a.deviceId),
      el("div", { class: "det" }, [modeloLegivel(a), (a.comodo || "").trim()].filter(Boolean).join("  ·  "))),
    el("div", { class: "dir" },
      el("button", { class: "chave-diag" + (u.diag ? " on" : "") + (falha ? " falha" : ""), title: "Diagnóstico", "data-id": a.puid + "diag",
        on: { click: () => {
          const n = nos.get(a.puid), velho = n && n.el.querySelector(".diag");
          if (u.diag && velho) { recolher(velho, () => { u.diag = false; desenharCartoes(); }); return; }
          u.diag = !u.diag; desenharCartoes();
          const novo = nos.get(a.puid); expandir(novo && novo.el.querySelector(".diag"));
        } } }, icChave()),
      el("span", { class: "selo" + (a.online ? " ok" : "") }, a.online ? "Conectado" : "Desconectado")));
}

// Como Aparelho.ModeloLegivel no app: o nome comercial conhecido; descricao em chines vira o tipo.
const NOMES_COMERCIAIS = { "008-301": "Hisense AW-12CR2RWGU00" };
const TIPOS = { "008": "Ar de janela", "009": "Ar split", "025": "Lava e seca", "003": "Lavadora", "027": "Lava e seca", "004": "Secadora" };
function modeloLegivel(a) {
  const n = NOMES_COMERCIAIS[a.tipo + "-" + a.codigoDoModelo];
  if (n) return n;
  return /[⺀-鿿＀-￯]/.test(a.modelo || "") ? (TIPOS[a.tipo] || a.tipoNome) : a.modelo;
}

function cartaoGenerico(a) {
  return el("div", { class: "cartao" }, cabecalho(a, icTermometro()), el("div", { class: "corpo mu" }, a.tipoNome));
}

function blocoDiagnostico(a, extras) {
  const falhas = (extras || []).concat((a.diagnostico && a.diagnostico.ativas || []).map(x => x.nome + (x.orientacao ? ": " + x.orientacao : "")));
  return el("div", { class: "sub-cartao diag", style: "margin-top:14px" },
    el("div", { class: "tit" }, "Diagnóstico", el("span", { class: "mu", style: "font-weight:400" },
      a.diagnostico && a.diagnostico.conhecidas ? a.diagnostico.conhecidas + " falhas conhecidas pela Hisense" : "")),
    falhas.length ? falhas.map(t => el("div", { class: "aviso perigo" }, t)) : el("div", { class: "mu" }, "Nenhuma falha ativa."));
}

function chaveLinha(rotulo, ligado, desab, mudou) {
  const inp = el("input", { type: "checkbox", checked: ligado, disabled: desab, on: { change: e => mudou(e.target.checked) } });
  return el("label", { class: "chavinha" }, el("span", {}, rotulo), el("span", { class: "tg" }, inp, el("span")));
}

// ---------------- ar ----------------

const DIAL = { r: 100, cx: 125, cy: 118 };
let arrastando = null;        // puid cujo mostrador esta sendo arrastado: nao redesenha
const ultimoArco = {};        // puid -> fracao desenhada, para deslizar ate a nova

function dialConteudo(d, frac) {
  const { r, cx, cy } = DIAL, C = 2 * Math.PI * r, arco = C * .75;
  const ang = (135 + 270 * frac) * Math.PI / 180;
  const kx = cx + r * Math.cos(ang), ky = cy + r * Math.sin(ang);
  const corArco = d.ligado ? d.cor : "var(--faint)";
  return '<circle cx="' + cx + '" cy="' + cy + '" r="' + r + '" fill="none" stroke="var(--border)" stroke-width="14" stroke-linecap="round" stroke-dasharray="' + arco + ' ' + C + '" transform="rotate(135 ' + cx + ' ' + cy + ')"/>' +
    '<circle cx="' + cx + '" cy="' + cy + '" r="' + r + '" fill="none" stroke="' + corArco + '" stroke-width="14" stroke-linecap="round" stroke-dasharray="' + (arco * frac) + ' ' + C + '" transform="rotate(135 ' + cx + ' ' + cy + ')"/>' +
    '<circle cx="' + kx + '" cy="' + ky + '" r="13" fill="#fff" stroke="' + corArco + '" stroke-width="3"/>' +
    '<text x="' + cx + '" y="' + (cy - 40) + '" text-anchor="middle" class="t-mu">' + (d.ligado ? d.modoNome : "Desligado") + '</text>' +
    '<text x="' + (cx - 6) + '" y="' + (cy + 18) + '" text-anchor="middle" font-size="54" font-weight="300"' + (d.ligado ? "" : ' style="fill:var(--muted)"') + '>' + d.alvo + '</text>' +
    '<text x="' + (cx + 30) + '" y="' + (cy - 12) + '" font-size="20" class="t-mu">' + d.unidade + '</text>' +
    (d.ambiente != null ? '<text x="' + cx + '" y="' + (cy + 52) + '" text-anchor="middle" class="t-mu" style="font-size:14px">ambiente ' + num(d.ambiente, 0) + d.unidade + '</text>' : "") +
    '<text x="' + (cx - 58) + '" y="' + (cy + 112) + '" text-anchor="middle" class="t-mu">' + d.mn + '°</text>' +
    '<text x="' + (cx + 58) + '" y="' + (cy + 112) + '" text-anchor="middle" class="t-mu">' + d.mx + '°</text>';
}

const fracDe = d => Math.max(0, Math.min(1, (d.alvo - d.mn) / (d.mx - d.mn)));

/** Mostrador do ar. Arrastavel quando aceita temperatura; desliza do valor anterior ao novo. */
function dial(a, d, arrastavel, mudou) {
  const e = svg("", "0 0 250 236");
  e.classList.add("dial");
  const alvoFrac = d.ligado ? fracDe(d) : 0;
  const de = ultimoArco[a.puid];
  ultimoArco[a.puid] = alvoFrac;
  e.innerHTML = dialConteudo(d, de == null ? alvoFrac : de);
  if (de != null && Math.abs(de - alvoFrac) > .001) {
    const t0 = performance.now();
    const passo = t => {
      const k = Math.min(1, (t - t0) / 380), suave = 1 - Math.pow(1 - k, 3);
      e.innerHTML = dialConteudo(d, de + (alvoFrac - de) * suave);
      if (k < 1) requestAnimationFrame(passo);
    };
    requestAnimationFrame(passo);
  }
  if (!arrastavel) return e;
  e.classList.add("arrastavel");
  let valor = d.alvo, ativo = false;
  const ler = ev => {
    const b = e.getBoundingClientRect();
    const x = (ev.clientX - b.left) * 250 / b.width - DIAL.cx, y = (ev.clientY - b.top) * 236 / b.height - DIAL.cy;
    return { dist: Math.hypot(x, y), rel: ((Math.atan2(y, x) * 180 / Math.PI) - 135 + 720) % 360 };
  };
  const mover = ev => {
    let rel = ler(ev).rel;
    if (rel > 270) rel = rel > 315 ? 0 : 270;
    const t = Math.round(d.mn + rel / 270 * (d.mx - d.mn));
    if (t === valor) return;
    valor = t;
    const novo = Object.assign({}, d, { alvo: t });
    e.innerHTML = dialConteudo(novo, fracDe(novo));
  };
  e.addEventListener("pointerdown", ev => {
    const p = ler(ev);
    if (p.dist < DIAL.r - 32 || p.dist > DIAL.r + 30) return;   // so pelo arco
    ativo = true; arrastando = a.puid;
    e.classList.add("arrastando");
    e.setPointerCapture(ev.pointerId);
    mover(ev);
  });
  e.addEventListener("pointermove", ev => { if (ativo) mover(ev); });
  const soltar = () => {
    if (!ativo) return;
    ativo = false; arrastando = null;
    e.classList.remove("arrastando");
    ultimoArco[a.puid] = fracDe(Object.assign({}, d, { alvo: valor }));
    if (valor !== d.alvo) mudou(valor); else desenharCartoes();
  };
  e.addEventListener("pointerup", soltar);
  e.addEventListener("pointercancel", soltar);
  return e;
}

function cartaoAr(a) {
  const i = a.info, ok = a.online, u = U(a.puid);
  const ligado = String(valor(a, "t_power")) === "1";
  const alvo = parseInt(valor(a, "t_temp") ?? i.alvo);
  const modo = parseInt(valor(a, "t_work_mode") ?? i.modo.valor);
  const vento = parseInt(valor(a, "t_fan_speed") ?? i.vento.valor);
  const noturno = String(valor(a, "t_sleep")) === "1";
  const cor = COR_MODO[modo] || "#2D7DF6";
  const [mn, mx] = i.faixa;
  const modoNome = (i.modos.find(m => m.valor === modo) || i.modo).nome || "";
  const unidade = i.unidade || "°C";

  const mudarTemp = d => comando(a, { t_temp: String(Math.max(mn, Math.min(mx, alvo + d))) });
  const podeTemp = ok && ligado && !!i.grava.t_temp;
  const esquerda = el("div", { class: "dialbox" },
    dial(a, { ligado, alvo, mn, mx, cor, modoNome, ambiente: i.ambiente, unidade }, podeTemp, t => comando(a, { t_temp: String(t) })),
    el("div", { class: "linha" },
      el("button", { class: "redondo", title: "Diminuir", "data-id": a.puid + "menos", disabled: !ok || !ligado || !i.grava.t_temp || alvo <= mn, on: { click: () => mudarTemp(-1) } }, icMenos()),
      el("button", { class: "redondo", title: "Aumentar", "data-id": a.puid + "mais", disabled: !ok || !ligado || !i.grava.t_temp || alvo >= mx, on: { click: () => mudarTemp(1) } }, icMais())));

  const tiles = (lista, atual, k, grava, horiz, ic) => el("div", { class: "tiles", style: "grid-template-columns:repeat(" + lista.length + ",1fr)" },
    lista.map((m, n) => {
      const on = ligado && m.valor === atual;
      return el("button", { class: "tile" + (horiz ? " horiz" : "") + (on ? " on" : ""), style: on ? "background:" + cor + ";border-color:" + cor : null,
        disabled: !ok || !ligado || !grava, "data-id": a.puid + k + m.valor, on: { click: () => comando(a, { [k]: String(m.valor) }) } }, ic(m, n), el("span", {}, m.nome));
    }));

  // O ar automatico do servidor, ao lado dos modos: acende com borda e fundo suaves (nao e um modo do aparelho).
  const auto = Object.assign({ ligado: false }, (pref && pref.dados.auto) || {});
  const btAuto = el("button", { class: "tile tile-auto" + (auto.ligado ? " on-auto" : ""), "aria-pressed": String(auto.ligado), disabled: !ok || !pref,
    title: auto.ligado ? "Ar automático ligado: o servidor ajusta modo e temperatura pelo conforto. Toque para desligar."
      : "Ligar o ar automático: o servidor ajusta modo e temperatura pelo conforto (Ajustes › Ar automático).",
    "data-id": a.puid + "auto", on: { click: async () => {
      await gravarPref({ auto: Object.assign({}, auto, { ligado: !auto.ligado }) });
      aviso(!auto.ligado ? "Ar automático ligado" : "Ar automático desligado");
    } } }, icone("brilho"), el("span", {}, i.modos.some(m => m.valor === 4) ? "Auto conforto" : "Auto"));
  const linhaModos = () => {
    const t = tiles(i.modos, modo, "t_work_mode", i.grava.t_work_mode, false, m => icone(ICONE_MODO[m.valor]));
    t.appendChild(btAuto);
    t.style.gridTemplateColumns = "repeat(" + (i.modos.length + 1) + ",1fr)";
    return t;
  };

  const extras = [i.umidade != null ? "Umidade " + num(i.umidade, 0) + "%" : null, i.potencia != null ? "Potência " + num(i.potencia, 0) + " W" : null,
    i.filtro ? "Limpar o filtro" : null].filter(Boolean);
  const direita = el("div", {},
    el("div", { class: "liga-linha" },
      el("button", { class: "liga" + (ligado ? " on" : ""), title: "Ligar ou desligar", "data-id": a.puid + "pw",
        disabled: !ok || !i.grava.t_power, on: { click: () => comando(a, { t_power: ligado ? "0" : "1" }) } }, icPower()),
      el("span", { class: "liga-txt" + (ligado ? " on" : "") }, ok ? (ligado ? "Ligado" : "Desligado") : "Desconectado"),
      i.noturnoBotao ? el("div", { class: "noturno" }, noturno && ligado ? "LEDs apagados" : "Apagar LEDs",
        el("button", { class: "redondo" + (noturno && ligado ? " on" : ""), title: "Apagar LEDs (liga o modo noturno do aparelho)", "data-id": a.puid + "lua", disabled: !ok || !ligado,
          on: { click: () => comando(a, { t_sleep: noturno ? "0" : "1" }) } }, icone("lua"))) : null),
    extras.length ? el("div", { class: "mu", style: "margin-top:8px" }, extras.join("  ·  ")) : null,
    i.modos.length ? [el("div", { class: "rot" }, "Modo"), linhaModos()] : null,
    i.ventos.length ? [el("div", { class: "rot" }, "Ventilador"), tiles(i.ventos, vento, "t_fan_speed", i.grava.t_fan_speed, true, (m, n) => icBarras(n + 1))] : null);

  // Cenas e timer lado a lado, como no app
  const cenas = el("div", { class: "sub-cartao" }, el("div", { class: "tit" }, "Cenas",
      el("button", { class: "b peq", on: { click: () => irPara("ajustes-cenas") } }, "Editar")),
    pref && pref.dados.cenas.length ? el("div", { class: "cenas" }, pref.dados.cenas.map((c, n) => el("button", { class: "cena", disabled: !ok,
      "data-id": a.puid + "cena" + n, on: { click: () => rodarCena(a, n, c) } },
      el("div", { class: "bola" }, icone(c.icone)), el("span", {}, el("b", {}, c.nome), el("small", {}, resumoCena(c, a))))))
      : el("div", { class: "mu" }, "Nenhuma cena."));
  const tm = agendamento(a.puid, "desligar"), rt = agendamento(a.puid, "retorno");
  // O servidor desistiu depois das tentativas (a nuvem nao respondeu): avisa aqui, onde o timer foi pedido.
  const falho = (estado.agendamentosFalhos || []).find(x => x.puid === a.puid);
  const textoFalho = falho ? (falho.nome === "retorno" ? "A volta ao padrão" : "O timer de desligar") + " das " + hora(falho.quando) +
    " falhou depois de " + falho.tentativas + (falho.tentativas === 1 ? " tentativa" : " tentativas") +
    (falho.detalhe ? ": " + falho.detalhe : ".") : null;
  const timer = el("div", { class: "sub-cartao" }, el("div", { class: "tit" }, "Desligar automaticamente"),
    el("div", { class: "linha" }, [["30 min", 30], ["1 h", 60], ["2 h", 120]].map(([t, m]) =>
      el("button", { class: "b", style: "min-width:72px", disabled: !ok || !ligado, on: { click: () => definirTimer(a, m) } }, t)),
      tm ? el("button", { class: "b perigo", on: { click: () => cancelarAg(a, "timer") } }, "Cancelar") : null),
    el("div", { class: "estado-timer" }, tm ? "Desliga às " + hora(tm.quando) + " (em " + dur((tm.quando - Date.now() / 1000) / 60) + ")" : "Nenhum timer ativo."),
    textoFalho ? el("div", { class: "aviso perigo", style: "margin:0 0 8px" }, textoFalho) : null,
    rt ? el("div", { class: "linha", style: "margin-bottom:6px" }, el("span", {}, "Volta ao padrão às " + hora(rt.quando)),
      el("button", { class: "b peq", on: { click: () => cancelarAg(a, "retorno") } }, "Manter a cena")) : null,
    el("div", { class: "mu" }, "O servidor dispara o timer mesmo com o PC desligado."));

  let funcoes = null;
  if (i.funcoes.length) {
    funcoes = el("div", { class: "sub-cartao", style: "margin-top:14px" }, el("div", { class: "tit" }, "Funções"),
      i.funcoes.map(x => chaveLinha(x.nome, String(valor(a, x.chave)) === "1", !ok || !ligado || !x.grava, v => comando(a, { [x.chave]: v ? "1" : "0" }))));
  }

  const cartao = el("div", { class: "cartao" }, cabecalho(a, icAr()),
    el("div", { class: "corpo" },
      el("div", { class: "ar" }, esquerda, direita),
      painelAuxiliar(a, u, cenas, timer, tm, rt, falho),
      funcoes,
      u.diag ? blocoDiagnostico(a, i.falhas.map(x => x.nome)) : null));
  return cartao;
}

/** Cenas e desligar automaticamente num painel so, fechado por padrao; o resumo mostra o que importa com ele fechado. */
function painelAuxiliar(a, u, cenas, timer, tm, rt, falho) {
  const aberto = !!u.aux, mexeu = Date.now() - (painelAuxiliar.mexeu[a.puid] || 0) < 400;
  const resumo = falho ? ["Timer falhou", true] : tm ? ["Desliga às " + hora(tm.quando), false] : rt ? ["Volta ao padrão às " + hora(rt.quando), false]
    : [(pref ? pref.dados.cenas.length : 0) + " cenas", false];
  return el("div", { class: "sub-cartao colapsavel" + (aberto ? " aberto" : "") + (mexeu ? " m3-mexeu" : ""), style: "margin-top:18px" },
    el("button", { class: "colapsa-cab", "aria-expanded": String(aberto), "data-id": a.puid + "aux",
      on: { click: ev => {
        const trocar = () => { u.aux = !u.aux; painelAuxiliar.mexeu[a.puid] = Date.now(); desenharCartoes(); };
        if (aberto) { recolher(ev.currentTarget.parentElement.querySelector(".baixo"), trocar); return; }
        trocar();
        const n = nos.get(a.puid);
        expandir(n && n.el.querySelector(".colapsavel > .baixo"));
      } } },
      el("span", { class: "tit-c" }, "Cenas e desligar automaticamente"),
      el("span", { class: "res" + (resumo[1] ? " perigo" : "") }, resumo[0]), icSeta()),
    aberto ? el("div", { class: "baixo", style: "margin-top:12px" }, cenas, timer) : null);
}
painelAuxiliar.mexeu = {};

function resumoCena(c, a) {
  if (c.desligar) return "Só desliga";
  const l = [];
  if (c.modo >= 0) l.push(({ 0: "Ventilar", 1: "Calor", 2: "Frio", 3: "Desumidificar", 4: "Automático", 5: "ECO" })[c.modo] || "Modo " + c.modo);
  if (c.temp > 0) l.push(c.temp + "°C");
  if (c.vento === -2) l.push("Mínima"); else if (c.vento === -3) l.push("Máxima");
  else if (c.vento >= 0) l.push(((a && a.info.ventos.find(v => v.valor === c.vento)) || { nome: "Vel. " + c.vento }).nome);
  if (c.noturno === 1) l.push("noturno");
  if (c.duracao > 0) l.push("por " + dur(c.duracao));
  return l.join(" · ");
}

async function rodarCena(a, n, c) {
  try { await api("aparelhos/" + a.puid + "/cena", "POST", { indice: n }); aviso("Cena " + c.nome + " enviada"); setTimeout(atualizar, 3500); }
  catch (e) { aviso("Cena falhou: " + e.message); }
}
async function definirTimer(a, m) {
  try { await api("aparelhos/" + a.puid + "/timer", "POST", { minutos: m }); atualizar(); } catch (e) { aviso(e.message); }
}
async function cancelarAg(a, qual) {
  try { await api("aparelhos/" + a.puid + "/" + qual, "DELETE"); atualizar(); } catch (e) { aviso(e.message); }
}

// ---------------- lavadora ----------------

// As mesmas etapas do app (EtapasDoCiclo.Montar): uma por fase da maquina.
const FASES = [[1, "Pesar"], [2, "Pré"], [3, "Lavar"], [4, "Enxaguar"], [5, "Amaciar"], [7, "Centrifugar"], [8, "Secar"], [10, "Fim"]];

const ultimoAnel = {};   // puid -> progresso desenhado, para o anel deslizar

function cartaoLavadora(a) {
  const i = a.info, u = U(a.puid);
  const cor = i.erro ? "var(--danger)" : i.pausada ? "var(--warn)" : i.concluido ? "var(--ok)" : "var(--accent)";
  const ciclos = ciclosDe(a.puid);
  const emCiclo = a.online && i.rodando;
  const partes = [cabecalho(a, icLavadora())];

  if (emCiclo) {
    const C = 2 * Math.PI * 88;
    const anel = el("div", { class: "anel" });
    anel.append(svg('<circle cx="100" cy="100" r="88" fill="none" stroke="var(--border)" stroke-width="12"/>' +
      '<circle class="prog" cx="100" cy="100" r="88" fill="none" stroke="' + cor + '" stroke-width="12" stroke-linecap="round" stroke-dasharray="' + (C * (ultimoAnel[a.puid] ?? i.progresso)) + ' ' + C + '" transform="rotate(-90 100 100)"/>', "0 0 200 200"));
    ultimoAnel[a.puid] = i.progresso;
    anel.depois = () => requestAnimationFrame(() => requestAnimationFrame(() => {
      const c = anel.querySelector(".prog");
      if (c) c.setAttribute("stroke-dasharray", (C * i.progresso) + " " + C);
    }));
    anel.append(el("div", { class: "val" }, el("b", {}, i.restante > 0 ? dur(i.restante) : "--"), el("span", {}, "restantes"),
      i.terminaEm ? el("span", { class: "fim" }, "termina às " + hora(i.terminaEm)) : null));

    const seca = (a.estado.dry_time && a.estado.dry_time !== "0") || a.estado.DryingSwitch === "1" || i.fase.id === 8 || i.fase.id === 9;
    const passos = FASES.filter(([f]) => f !== 8 || seca);
    let atual = -1;
    passos.forEach(([f], n) => { if (f <= i.fase.id) atual = n; });
    const etapas = i.programado ? null : el("div", { class: "passos", style: "--cor:" + cor },
      passos.map(([, nome], n) => el("div", { class: "passo" + (n === atual ? " atual" : n < atual ? " feita" : "") }, el("i"), el("span", {}, nome))));

    partes.push(el("div", { class: "corpo" },
      el("div", { class: "lav" }, anel, el("div", {},
        el("h2", { style: i.erro ? "color:var(--danger)" : null }, i.titulo),
        el("div", {}, i.programa.id ? i.programa.nome : ""),
        el("div", { class: "mu", style: "font-size:13px;margin-top:4px" }, i.total ? "Ciclo de " + dur(i.total) + "  ·  " + Math.round(i.progresso * 100) + "% concluído" : ""))),
      etapas,
      i.avisos.map(t => el("div", { class: "aviso" }, "⚠  " + t)),
      [el("div", { class: "rot", style: "margin-top:20px" }, "Este ciclo"),
        el("div", { class: "params" }, i.parametros.concat([["Água", i.agua != null ? num(i.agua, 1) + " L" : "--"], ["Energia", i.energia != null ? num(i.energia, 2) + " kWh" : "--"]])
          .map(([k, v]) => el("div", {}, el("small", {}, k), el("b", {}, v))))],
      u.diag ? blocoDiagnostico(a) : null));
  } else {
    partes.push(el("div", { class: "corpo" },
      el("div", { style: "font-weight:600;font-size:15px" + (i.erro ? ";color:var(--danger)" : "") }, i.titulo),
      i.avisos.map(t => el("div", { class: "aviso" }, "⚠  " + t)),
      u.diag ? blocoDiagnostico(a) : null));
  }

  return el("div", { class: "cartao" }, partes);
}

async function carregarCiclos() {
  try {
    const r = await api("historico/ciclos?desde=" + (Math.floor(Date.now() / 1000) - 120 * 86400));
    const novo = {};
    for (const c of r.ciclos) (novo[c.puid] = novo[c.puid] || []).push(c);
    ciclos = novo;
  } catch (e) {}
}

// ---------------- consumo: tudo num lugar so ----------------

let periodoConsumo = "30";
const COR_AR = "#2D7DF6", COR_LAV = "#00B0A9";
const NOME_MES = ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];
const cap = t => t.charAt(0).toUpperCase() + t.slice(1);
const isoDia = d => d.getFullYear() + "-" + String(d.getMonth() + 1).padStart(2, "0") + "-" + String(d.getDate()).padStart(2, "0");

for (const b of document.querySelectorAll("#periodo button")) b.onclick = () => {
  periodoConsumo = b.dataset.p;
  for (const x of document.querySelectorAll("#periodo button")) x.classList.toggle("on", x === b);
  for (const id of ["consResumo", "consLav"]) $(id).classList.add("recarregando");
  carregarConsumo();
};

async function baixar(caminho, nome) {
  try {
    const r = await fetch("/api/" + caminho, { headers: { "Authorization": "Bearer " + chave } });
    if (!r.ok) throw new Error("HTTP " + r.status);
    const blob = await r.blob();
    const arq = new File([blob], nome, { type: "text/csv" });
    if (navigator.canShare && navigator.canShare({ files: [arq] }) && matchMedia("(max-width:760px)").matches) {
      try { await navigator.share({ files: [arq], title: nome }); return; } catch (e) { if (e.name === "AbortError") return; }
    }
    const url = URL.createObjectURL(blob);
    el("a", { href: url, download: nome }).click();
    setTimeout(() => URL.revokeObjectURL(url), 5000);
  } catch (e) { aviso("Exportar falhou: " + e.message); }
}
$("csvAr").onclick = () => baixar("exportar/ar.csv", "historico-ar.csv");
$("csvCiclos").onclick = () => baixar("exportar/ciclos.csv", "ciclos-lavadora.csv");

/** Texto explicativo escondido num (i): aparece ao passar o mouse, focar (Tab) ou tocar. */
function dica(texto) {
  const b = el("button", { class: "dica", type: "button", "aria-label": "Mais informações", "data-t": texto,
    on: { click: e => { e.stopPropagation(); b.classList.toggle("aberta"); } }, }, "i");
  b.addEventListener("blur", () => b.classList.remove("aberta"));
  return b;
}

function cartaoSimples(titulo, det, ic, ...corpo) {
  let info = null;
  if (corpo.length && corpo[corpo.length - 1] && corpo[corpo.length - 1].dica) info = dica(corpo.pop().dica);
  return el("div", { class: "cartao" },
    el("div", { class: "cab" }, ic ? el("div", { class: "ico" }, ic) : null,
      el("div", { class: "nomes" }, el("div", { class: "nome" }, titulo), det ? el("div", { class: "det" }, det) : null), info),
    el("div", { class: "corpo" }, corpo));
}

/**
 * O periodo escolhido e o anterior equivalente (mesma regra do app):
 * 7 e 30 dias contra os 7 e 30 de antes; o mes contra o mesmo ponto do anterior.
 */
function janelas() {
  const hoje = new Date(); hoje.setHours(0, 0, 0, 0);
  const menos = (d, n) => { const x = new Date(d); x.setDate(x.getDate() - n); return x; };
  if (periodoConsumo === "mes") {
    const ini = new Date(hoje.getFullYear(), hoje.getMonth(), 1);
    const antIni = new Date(hoje.getFullYear(), hoje.getMonth() - 1, 1);
    const dm = new Date(hoje.getFullYear(), hoje.getMonth(), 0).getDate();
    const antFim = new Date(hoje.getFullYear(), hoje.getMonth() - 1, Math.min(hoje.getDate(), dm));
    return { ini: isoDia(ini), fim: isoDia(hoje), antIni: isoDia(antIni), antFim: isoDia(antFim),
      nome: cap(NOME_MES[hoje.getMonth()]) + " até hoje", contra: "o mesmo dia de " + NOME_MES[antIni.getMonth()], sem: "de " + NOME_MES[antIni.getMonth()] };
  }
  const n = +periodoConsumo, ini = menos(hoje, n - 1);
  return { ini: isoDia(ini), fim: isoDia(hoje), antIni: isoDia(menos(ini, n)), antFim: isoDia(menos(ini, 1)),
    nome: "Últimos " + n + " dias", contra: "os " + n + " dias anteriores", sem: "dos " + n + " dias anteriores" };
}

async function carregarConsumo() {
  desenharGrafico();
  carregarLavadoraConsumo();
  try {
    const [r, m] = await Promise.all([api("consumo?dias=62&meses=2"), api("resumo-mes").catch(() => null)]);
    desenharResumo(r, m);
  }
  catch (e) { trocarConteudo($("consResumo"), el("div", { class: "corpo mu" }, "Erro: " + e.message)); }
}

function desenharResumo(r, mes) {
  const j = janelas(), p = r.preferencias, lista = estado ? estado.lista : [];
  const soma = (l, k) => l.reduce((t, d) => t + (d[k] || 0), 0);
  const ap = Object.entries(r.aparelhos).map(([puid, c]) => {
    const a = lista.find(x => x.puid === puid);
    const dias = c.dias.filter(d => d.dia >= j.ini && d.dia <= j.fim), ant = c.dias.filter(d => d.dia >= j.antIni && d.dia <= j.antFim);
    return { tipo: c.tipo, nome: a ? a.apelido : c.tipo === "ar" ? "Ar" : "Lavadora", cor: c.tipo === "ar" ? COR_AR : COR_LAV, dias,
      kwh: soma(dias, "kwh"), custo: soma(dias, "custo"), horas: soma(dias, "horas"), agua: soma(dias, "agua"), ciclos: soma(dias, "ciclos"),
      custoAnt: soma(ant, "custo") };
  }).sort((x, y) => (x.tipo === "ar" ? 0 : 1) - (y.tipo === "ar" ? 0 : 1));
  const total = soma(ap, "custo"), kwh = soma(ap, "kwh"), agua = soma(ap, "agua"), ant = soma(ap, "custoAnt");
  const v = ant > 0.005 ? (total - ant) / ant * 100 : null;
  const bloco = (rot, val, det, cls) => el("div", {}, el("small", {}, rot), el("b", {}, val), el("span", { class: cls || "" }, det));

  const tabela = el("table", { class: "aparelhos" },
    el("tr", {}, el("th", {}, "Aparelho"), el("th", { class: "n" }, "Energia"), el("th", { class: "n" }, "Custo"), el("th", { class: "n" }, "Uso"), el("th", { class: "parcela" }, "Parte do custo")),
    ap.map(x => el("tr", {},
      el("td", {}, el("div", { class: "nome" }, el("i", { style: "background:" + x.cor }),
        el("div", {}, x.nome, el("small", {}, x.tipo === "ar" ? "estimado pelo tempo ligado" : "medido pela máquina")))),
      el("td", { class: "n" }, num(x.kwh, 1) + " kWh"),
      el("td", { class: "n" }, "R$ " + num(x.custo, 2)),
      el("td", { class: "n" }, x.tipo === "ar" ? num(x.horas, 1) + " h ligado" : x.ciclos + (x.ciclos === 1 ? " ciclo" : " ciclos") + " · " + num(x.agua, 0) + " L"),
      el("td", { class: "parcela" }, el("div", {}, el("i", { style: "width:" + (total > 0 ? x.custo / total * 100 : 0) + "%;background:" + x.cor }))))));

  trocarConteudo($("consResumo"),
    el("div", { class: "cab" }, el("div", { class: "ico" }, svg('<path fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" d="M4 20h16M7 16V11M12 16V6M17 16v-7"/>')),
      el("div", { class: "nomes" }, el("div", { class: "nome" }, j.nome), el("div", { class: "det" }, "ar + lavadora, a R$ " + num(p.tarifa, 2) + "/kWh"))),
    el("div", { class: "corpo" },
      el("div", { class: "destaques" },
        bloco("Custo estimado", "R$ " + num(total, 2), v == null ? "sem dados " + j.sem + " para comparar" : (v <= 0 ? "▼ " : "▲ ") + num(Math.abs(v), 0) + "% contra " + j.contra, v == null ? "" : v <= 0 ? "menos" : "mais"),
        bloco("Energia", num(kwh, 1) + " kWh", "média de " + num(kwh / Math.max(1, ap[0] ? ap[0].dias.length : 1), 2) + " kWh por dia"),
        bloco("Água", num(agua, 0) + " L", "só a lavadora"),
        mes && mes.previsao ? bloco("Previsão de " + NOME_MES[+mes.mes.slice(5) - 1], "R$ " + num(mes.previsao.custo, 2),
          mes.previsao.diasComUso < 3 ? "poucos dias de uso para prever" : num(mes.previsao.kwh, 0) + " kWh, a " + num(mes.previsao.mediaDia, 1) + " kWh por dia") : null),
      tabela,
      el("div", { class: "rot", style: "margin-top:18px" }, "Energia por dia"),
      empilhadas(ap)));
}

/** Uma coluna por dia com o ar e a lavadora empilhados; tocar ou passar o mouse mostra o dia. */
function empilhadas(ap) {
  const n = ap.length ? ap[0].dias.length : 0;
  if (!n) return el("div", { class: "mu" }, "Sem dados no período.");
  const totais = Array.from({ length: n }, (_, k) => ap.reduce((t, x) => t + (x.dias[k] ? x.dias[k].kwh : 0), 0));
  const mx = Math.max(0.01, ...totais);
  const info = el("div", { class: "info-dia" });
  const nomeDia = k => { const [y, m, d] = ap[0].dias[k].dia.split("-").map(Number); return new Date(y, m - 1, d); };
  const mostrar = k => {
    if (k == null) { info.replaceChildren("Toque numa barra para ver o dia."); return; }
    const dt = nomeDia(k), custo = ap.reduce((t, x) => t + (x.dias[k] ? x.dias[k].custo : 0), 0);
    info.replaceChildren(el("b", {}, dt.toLocaleDateString("pt-BR", { weekday: "short", day: "2-digit", month: "2-digit" })),
      ap.map(x => "  ·  " + x.nome + " " + num(x.dias[k] ? x.dias[k].kwh : 0, 2) + " kWh"), "  ·  R$ " + num(custo, 2));
  };
  const passo = n <= 7 ? 1 : Math.ceil(n / 8);
  const colunas = Array.from({ length: n }, (_, k) => {
    const col = el("div", { class: "dia", on: { mouseenter: () => mostrar(k), click: () => mostrar(k) } },
      ap.slice().reverse().map(x => el("i", { style: "background:" + x.cor + ";height:" + ((x.dias[k] ? x.dias[k].kwh : 0) / mx * 100) + "%" })));
    return col;
  });
  const eixo = el("div", { class: "eixo-dias" }, Array.from({ length: n }, (_, k) => el("span", {},
    k === n - 1 ? "hoje" : (n - 1 - k) % passo ? "" : n <= 7 ? nomeDia(k).toLocaleDateString("pt-BR", { weekday: "short" }).replace(".", "") : String(nomeDia(k).getDate()).padStart(2, "0") + "/" + String(nomeDia(k).getMonth() + 1).padStart(2, "0"))));
  mostrar(null);
  return el("div", {}, el("div", { class: "empilhadas", on: { mouseleave: () => mostrar(null) } }, colunas), eixo, info,
    el("div", { class: "legenda" }, ap.map(x => el("span", {}, el("i", { class: "faixa", style: "background:" + x.cor + ";opacity:1" }), x.nome))));
}

// ---------------- grafico da temperatura ambiente (Consumo) ----------------

let diaGrafico = 0;   // 0 = hoje, 1 = ontem...

async function desenharGrafico() {
  const caixa = $("grafico");
  const ar = estado && estado.lista.find(ehAr);
  if (!ar) { caixa.classList.add("oculto"); return; }
  caixa.classList.remove("oculto");
  const d0 = new Date(); d0.setHours(0, 0, 0, 0); d0.setDate(d0.getDate() - diaGrafico);
  const ini = Math.floor(d0.getTime() / 1000), fim = ini + 86400;
  const titulo = diaGrafico === 0 ? "Hoje" : diaGrafico === 1 ? "Ontem" : d0.toLocaleDateString("pt-BR", { weekday: "short", day: "2-digit", month: "2-digit" }).replace(".", "");
  const nav = el("div", { class: "linha" },
    el("button", { class: "b peq", title: "Dia anterior", on: { click: () => { diaGrafico++; desenharGrafico.dir = "m3-tras"; desenharGrafico(); } } }, "◀"),
    el("span", { style: "width:96px;text-align:center;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-variant-numeric:tabular-nums" }, titulo),
    el("button", { class: "b peq", title: "Dia seguinte", disabled: diaGrafico === 0, on: { click: () => { diaGrafico--; desenharGrafico.dir = "m3-frente"; desenharGrafico(); } } }, "▶"));
  const vez = desenharGrafico.vez = (desenharGrafico.vez || 0) + 1;   // cliques rapidos: vale so a ultima busca
  let corpo = caixa.querySelector(":scope > .corpo");
  const novoCorpo = !corpo;
  if (novoCorpo) corpo = el("div", { class: "corpo" }, el("div", { class: "mu" }, "Carregando…"));
  else corpo.classList.add("recarregando");
  const cab = el("div", { class: "cab cab-nav" }, el("div", { class: "ico" }, icTermometro()),
    el("div", { class: "nomes" }, el("div", { class: "nome" }, (ar.apelido || "Ar") + ": temperatura do dia"), el("div", { class: "det" }, "faixas coloridas: ar ligado, na cor do modo")),
    el("div", { class: "dir" }, nav));
  if (novoCorpo) caixa.replaceChildren(cab, corpo); else caixa.firstElementChild.replaceWith(cab);
  let am;
  try { am = (await api("historico/ar?puid=" + encodeURIComponent(ar.puid) + "&desde=" + (ini - 1) + "&ate=" + fim)).amostras; }
  catch (e) { if (vez === desenharGrafico.vez) trocarConteudo(corpo, el("div", { class: "mu" }, "Erro: " + e.message)); return; }
  if (vez !== desenharGrafico.vez) return;
  const dir = desenharGrafico.dir; desenharGrafico.dir = null;
  if (!am.length) { trocarConteudo(corpo, el("div", { class: "mu" }, "Nenhuma leitura neste dia.")); return; }
  const graf = graficoDoDia(am, ini, fim, (corpo.clientWidth || 936) - 36);
  trocarConteudo(corpo, graf, resumoDoDia(am));
  if (dir) animar(graf, dir);   // so o grafico desliza; o resumo abaixo troca no lugar
}

function graficoDoDia(am, ini, fim, largura, opcoes) {
  const op = opcoes || {};
  const W = Math.max(260, largura), H = op.H || 240, ml = 34, mr = 10, mt = 10, mb = 24;
  const temps = am.flatMap(s => [s.amb, s.ligado ? s.alvo : null]).filter(v => v != null);
  let lo = Math.floor(Math.min(...temps) - 1), hi = Math.ceil(Math.max(...temps) + 1);
  if (hi - lo < 4) { lo -= 1; hi += 1; }
  const X = t => ml + (t - ini) / (fim - ini) * (W - ml - mr);
  const Y = v => mt + (hi - v) / (hi - lo) * (H - mt - mb);
  let s = "";
  // grade
  const passoY = hi - lo > 8 ? 2 : 1;
  for (let v = lo; v <= hi; v += passoY)
    s += '<line x1="' + ml + '" x2="' + (W - mr) + '" y1="' + Y(v) + '" y2="' + Y(v) + '" stroke="var(--border)" stroke-width="1"/><text x="' + (ml - 6) + '" y="' + (Y(v) + 4) + '" text-anchor="end">' + v + '°</text>';
  const horas = (fim - ini) / 3600, passoH = horas > 12 ? 3 : 1;
  for (let t = Math.ceil(ini / 3600) * 3600; t <= fim; t += 3600) {
    const hh = new Date(t * 1000).getHours();
    if (hh % passoH) continue;
    const x = X(t), ancora = x - ml < 14 ? "start" : W - mr - x < 14 ? "end" : "middle";
    s += '<text x="' + x + '" y="' + (H - 6) + '" text-anchor="' + ancora + '">' + String(t === fim && horas >= 24 ? 24 : hh).padStart(2, "0") + 'h</text>';
  }
  // faixas com o ar ligado (lacuna maior que 3 min nao conta, como o consumo)
  // trechos seguidos no mesmo modo viram uma faixa so (varias finas deixam riscos)
  let faixa = null;
  const fechar = () => { if (faixa) s += '<rect x="' + X(faixa.a) + '" y="' + mt + '" width="' + Math.max(1, X(faixa.b) - X(faixa.a)) + '" height="' + (H - mt - mb) + '" fill="' + (COR_MODO[faixa.modo] || "#2D7DF6") + '" opacity=".18"/>'; faixa = null; };
  for (let i = 0; i < am.length; i++) {
    if (!am[i].ligado) { fechar(); continue; }
    const t1 = Math.min(i + 1 < am.length ? am[i + 1].t : am[i].t + 60, am[i].t + 180, fim);
    if (faixa && faixa.modo === am[i].modo && am[i].t <= faixa.b) faixa.b = t1;
    else { fechar(); faixa = { a: am[i].t, b: t1, modo: am[i].modo }; }
  }
  fechar();
  // linhas: ambiente continua, alvo tracejado so com o ar ligado
  const linha = (f, filtro) => {
    let d = "", ant = null;
    for (const a of am) {
      const v = f(a);
      if (v == null || !filtro(a)) { ant = null; continue; }
      d += (ant && a.t - ant.t <= 600 ? "L" : "M") + X(a.t).toFixed(1) + " " + Y(v).toFixed(1);
      ant = a;
    }
    return d;
  };
  s += '<path d="' + linha(a => a.alvo, a => a.ligado) + '" fill="none" stroke="var(--muted)" stroke-width="1.5" stroke-dasharray="4 4"/>';
  s += '<path d="' + linha(a => a.amb, () => true) + '" fill="none" stroke="var(--accent)" stroke-width="2.2" stroke-linejoin="round"/>';
  s += '<line class="cursor" x1="0" x2="0" y1="' + mt + '" y2="' + (H - mb) + '" stroke="var(--text)" stroke-width="1" opacity="0"/>';
  const g = svg(s, "0 0 " + W + " " + H);
  g.setAttribute("width", W); g.setAttribute("height", H);
  const dica = el("div", { class: "dica oculto" });
  const box = el("div", { class: "graf" }, g, dica);
  const cursor = g.querySelector(".cursor");
  g.addEventListener("pointermove", ev => {
    const r = g.getBoundingClientRect();
    const t = ini + (ev.clientX - r.left - ml) / (W - ml - mr) * (fim - ini);
    let m = am[0];
    for (const a of am) if (Math.abs(a.t - t) < Math.abs(m.t - t)) m = a;
    cursor.setAttribute("x1", X(m.t)); cursor.setAttribute("x2", X(m.t)); cursor.setAttribute("opacity", ".35");
    dica.textContent = hora(m.t) + "  ·  ambiente " + (m.amb != null ? num(m.amb, 0) + "°" : "--") +
      "  ·  " + (m.ligado ? "ligado, " + (({ 0: "Ventilar", 2: "Frio", 3: "Desumidificar", 5: "ECO", 1: "Calor", 4: "Automático" })[m.modo] || "modo " + m.modo) + " " + num(m.alvo, 0) + "°" : "desligado");
    dica.classList.remove("oculto");
    dica.style.left = Math.min(Math.max(0, X(m.t) - dica.offsetWidth / 2), W - dica.offsetWidth) + "px";
  });
  // No toque a dica fica ate o proximo toque (o pointerleave vem logo depois de soltar o dedo).
  g.addEventListener("pointerleave", ev => { if (ev.pointerType === "mouse") { dica.classList.add("oculto"); cursor.setAttribute("opacity", "0"); } });
  g.addEventListener("pointerdown", ev => { if (ev.pointerType !== "mouse") g.dispatchEvent(new PointerEvent("pointermove", ev)); });
  return el("div", { style: "overflow-x:auto" }, box,
    op.legenda === false ? null : el("div", { class: "legenda" }, el("span", {}, el("i", { style: "background:var(--accent)" }), "ambiente"),
      el("span", {}, el("i", { style: "background:var(--muted)" }), "temperatura pedida"),
      el("span", {}, el("i", { class: "faixa", style: "background:#2D7DF6" }), "ligado em Frio"),
      el("span", {}, el("i", { class: "faixa", style: "background:#2EA043" }), "ECO"),
      el("span", {}, el("i", { class: "faixa", style: "background:#00B0A9" }), "Desumidificar"),
      el("span", {}, el("i", { class: "faixa", style: "background:#8A93A6" }), "Ventilar")));
}

function resumoDoDia(am) {
  let lig = 0;
  const com = [], sem = [];
  for (let i = 0; i < am.length; i++) {
    if (am[i].amb != null) (am[i].ligado ? com : sem).push(am[i].amb);
    if (am[i].ligado) lig += Math.min((i + 1 < am.length ? am[i + 1].t : am[i].t + 60) - am[i].t, 180);
  }
  const media = l => l.length ? num(l.reduce((a, b) => a + b, 0) / l.length, 1) + "°" : "--";
  const todas = com.concat(sem);
  return el("div", { class: "totais", style: "margin:14px 0 0" },
    el("div", {}, el("b", {}, dur(lig / 60)), el("span", { class: "mu" }, "ligado no dia")),
    el("div", {}, el("b", {}, media(com)), el("span", { class: "mu" }, "média com o ar ligado")),
    el("div", {}, el("b", {}, media(sem)), el("span", { class: "mu" }, "média com o ar desligado")),
    el("div", {}, el("b", {}, todas.length ? num(Math.min(...todas), 0) + "° / " + num(Math.max(...todas), 0) + "°" : "--"), el("span", { class: "mu" }, "mínima / máxima")));
}

// ---------------- lavadora: ciclos e programas (Consumo) ----------------

const PROGRAMA_LIMPEZA = 6;   // Limpeza do tambor

async function carregarLavadoraConsumo() {
  const caixa = $("consLav");
  const lav = estado && estado.lista.find(ehLavadora);
  if (!lav) { caixa.classList.add("oculto"); caixa.classList.remove("recarregando"); return; }
  let l;
  try { l = (await api("historico/ciclos?desde=0")).ciclos.filter(c => c.puid === lav.puid); } catch (e) { caixa.classList.remove("recarregando"); return; }
  caixa.classList.remove("oculto");
  const por = {};
  for (const c of l.filter(c => c.concluido)) {
    const p = por[c.programa] || (por[c.programa] = { nome: c.programaNome, n: 0, seg: 0, agua: 0, energia: 0 });
    p.n++; p.seg += c.fim - c.inicio; p.agua += c.agua || 0; p.energia += c.energia || 0;
  }
  const ranking = Object.values(por).sort((a, b) => b.n - a.n);
  let ult = -1;
  l.forEach((c, n) => { if (c.programa === PROGRAMA_LIMPEZA && c.concluido) ult = n; });
  const desde = ult < 0 ? l.length : l.length - 1 - ult;
  const pede = String(lav.estado.DrumCleanFlag) === "1";
  trocarConteudo(caixa,
    el("div", { class: "cab" }, el("div", { class: "ico" }, icLavadora()),
      el("div", { class: "nomes" }, el("div", { class: "nome" }, (lav.apelido || "Lavadora") + ": ciclos"),
        el("div", { class: "det" }, l.length + (l.length === 1 ? " ciclo gravado" : " ciclos gravados") + (ranking.length ? "  ·  mais usado: " + ranking[0].nome : "")))),
    el("div", { class: "corpo" },
      pede ? el("div", { class: "aviso", style: "margin-top:0" }, "⚠  A máquina está pedindo a limpeza do tambor (programa Limpeza do tambor, sem roupa).")
        : el("div", { class: "mu" }, "Tambor: " + (ult < 0 ? "nenhuma limpeza gravada ainda" : "limpo em " + dataHora(l[ult].inicio) + ", " + desde + (desde === 1 ? " ciclo" : " ciclos") + " desde então") + ". A máquina avisa quando for a hora."),
      el("div", { class: "rot", style: "margin-top:16px" }, "Últimos ciclos"),
      l.length ? el("table", {}, el("tr", {}, el("th", {}, "Início"), el("th", {}, "Programa"), el("th", { class: "n" }, "Duração"), el("th", { class: "n" }, "Água"), el("th", { class: "n" }, "Energia")),
        l.slice().reverse().slice(0, 8).map(c => el("tr", {}, el("td", {}, dataHora(c.inicio)), el("td", {}, c.programaNome + (c.concluido ? "" : " (interrompido)")),
          el("td", { class: "n" }, dur((c.fim - c.inicio) / 60)), el("td", { class: "n" }, num(c.agua, 0) + " L"), el("td", { class: "n" }, num(c.energia, 2) + " kWh"))))
        : el("div", { class: "mu" }, "Nenhum ciclo gravado ainda."),
      ranking.length ? [el("div", { class: "rot", style: "margin-top:16px" }, "Média por programa (ciclos concluídos)"),
        el("table", {}, el("tr", {}, el("th", {}, "Programa"), el("th", { class: "n" }, "Ciclos"), el("th", { class: "n" }, "Duração"), el("th", { class: "n" }, "Água"), el("th", { class: "n" }, "Energia")),
          ranking.map(p => el("tr", {}, el("td", {}, p.nome), el("td", { class: "n" }, p.n), el("td", { class: "n" }, dur(p.seg / p.n / 60)),
            el("td", { class: "n" }, num(p.agua / p.n, 0) + " L"), el("td", { class: "n" }, num(p.energia / p.n, 2) + " kWh"))))] : null));
}

// ---------------- historico de eventos ----------------

const evFiltro = { puid: "", dias: "7", origem: "" };
const ORIGEM_NOME = { manual: "Manual", recomendacao: "Recomendação", automacao: "Automação", sistema: "Sistema" };

function linhaDeEvento(e) {
  return el("div", { class: "ev-lin" },
    el("div", { class: "ev-q" }, dataHora(e.t)),
    el("div", { class: "ev-c" }, el("b", {}, e.descricao), el("span", { class: "mu" }, e.dispositivo + " · " + e.tipoNome)),
    el("span", { class: "ev-o o-" + e.origem }, ORIGEM_NOME[e.origem] || e.origem));
}

/** Eventos do ar e da lavadora guardados no servidor: filtro por aparelho, origem e periodo, do mais novo ao mais antigo, 30 por vez. */
function blocoEventos(d) {
  const lista = el("div", { class: "ev-lista" });
  const mais = el("button", { class: "b oculto", type: "button" }, "Carregar mais");
  const info = el("div", { class: "mu", style: "margin-top:10px" }, "Carregando…");
  let proximo = null, geracao = 0;
  async function carregar(zerar) {
    const minha = ++geracao;
    if (zerar) { proximo = null; lista.replaceChildren(); mais.classList.add("oculto"); info.textContent = "Carregando…"; }
    mais.disabled = true;
    const q = new URLSearchParams({ limite: "30" });
    if (evFiltro.puid) q.set("puid", evFiltro.puid);
    if (evFiltro.origem) q.set("origem", evFiltro.origem);
    if (evFiltro.dias !== "0") q.set("de", String(Math.floor(Date.now() / 1000) - Number(evFiltro.dias) * 86400));
    if (proximo) q.set("antes", String(proximo));
    try {
      const r = await api("historico/eventos?" + q);
      if (minha !== geracao) return;
      lista.append(...r.eventos.map(linhaDeEvento));
      proximo = r.proximo;
      mais.classList.toggle("oculto", !proximo);
      info.textContent = lista.children.length ? "" : "Nenhum evento neste filtro.";
    } catch (e) { if (minha === geracao) info.textContent = "Não carregou: " + e.message; }
    mais.disabled = false;
  }
  mais.onclick = () => carregar(false);
  const seletor = (rotulo, chave, opcoes) => el("label", { class: "campo" }, rotulo, el("select", { on: { change: e => { evFiltro[chave] = e.target.value; carregar(true); } } },
    opcoes.map(([v, t]) => el("option", { value: v, selected: v === evFiltro[chave] }, t))));
  const aparelhos = ((estado && estado.lista) || []).map(a => [a.puid, a.apelido || a.deviceId]);
  const dias = d.retencaoEventosDias || 90;
  const guardar = [30, 60, 90, 180, 365].includes(dias) ? [30, 60, 90, 180, 365] : [30, 60, 90, 180, 365, dias].sort((a, b) => a - b);
  if (ajSecao === "eventos") carregar(true); else info.textContent = "";   // so busca com a secao aberta
  return cartaoSimples("Histórico de eventos", "ar e lavadora, guardados no servidor", null,
    el("div", { class: "form" },
      seletor("Aparelho", "puid", [["", "Todos"], ...aparelhos]),
      seletor("Período", "dias", [["1", "Últimas 24 h"], ["7", "7 dias"], ["30", "30 dias"], ["0", "Tudo guardado"]]),
      seletor("Origem", "origem", [["", "Todas"], ...Object.entries(ORIGEM_NOME)])),
    lista, info, el("div", { class: "linha", style: "margin-top:8px" }, mais),
    el("label", { class: "campo", style: "margin-top:14px" }, "Guardar por",
      el("select", { on: { change: e => gravarPref({ retencaoEventosDias: Number(e.target.value) }) } },
        guardar.map(n => el("option", { value: n, selected: n === dias }, n + " dias")))),
    el("div", { class: "mu" }, "Eventos mais antigos que isso são apagados pelo servidor."));
}

// ---------------- metricas do servidor (Prometheus, pelo proprio servidor) ----------------

const metFiltro = { horas: 6 };
let metAlvo = "";        // o grafico a mostrar ao abrir a secao (vem da Saude do servidor ou de #ajustes-metricas-cpu)
const MET_GRAFICOS = [["cpu", "CPU", "%"], ["ram", "Memória", "%"], ["disco", "Disco", "%"], ["temp", "Temperatura", "C"], ["rede", "Rede", "B/s"], ["carga", "Carga", ""]];
// Limites (aviso, perigo) para colorir o valor de agora; o que nao esta aqui fica neutro.
const MET_LIMITES = { cpu: [75, 90], ram: [80, 92], disco: [80, 92], temp: [75, 85] };
const bps = v => v == null ? "--" : v >= 1e6 ? num(v / 1e6, 1) + " MB/s" : v >= 1e3 ? num(v / 1e3, 0) + " KB/s" : num(v, 0) + " B/s";
const metFmt = (v, u) => v == null ? "--" : u === "%" ? num(v, 0) + "%" : u === "B/s" ? bps(v) : u === "C" ? num(v, 0) + " °C" : num(v, 2);
const duracaoLonga = s => s >= 86400 ? num(s / 86400, 1) + " dias" : s >= 3600 ? num(s / 3600, 1) + " h" : Math.round(s / 60) + " min";
const metNivel = (k, v) => { const l = MET_LIMITES[k]; return v == null || !l ? "" : v >= l[1] ? " perigo" : v >= l[0] ? " aviso" : ""; };

/** A escala do eixo y: de 0 a um teto "redondo" (a temperatura anda por uma faixa, nao parte do zero). */
function escalaMetrica(valores, unidade) {
  const mx = Math.max(...valores), mn = Math.min(...valores);
  if (unidade === "C") { const lo = Math.floor((mn - 5) / 5) * 5, hi = Math.max(lo + 10, Math.ceil((mx + 5) / 5) * 5); return [Math.max(0, lo), hi]; }
  if (unidade === "%") return [0, Math.min(100, Math.max(20, Math.ceil(mx * 1.3 / 10) * 10))];
  const hi = mx * 1.2 || 1, p = Math.pow(10, Math.floor(Math.log10(hi))), f = hi / p;
  return [0, (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * p];
}

/** A curva de uma ou duas series, com as cores do tema e o valor sob o ponteiro; os rotulos ficam em HTML por fora (o svg estica sem deformar texto). */
function curvaDeMetrica(series, unidade) {
  const W = 600, H = 96, todos = series.flatMap(s => s.pontos);
  if (!todos.length) return el("div", { class: "mu met-vazio" }, "Sem dados neste período.");
  const t0 = Math.min(...todos.map(p => p[0])), t1 = Math.max(...todos.map(p => p[0])) || t0 + 1;
  const [y0, ymax] = escalaMetrica(todos.map(p => p[1]), unidade);
  const x = t => ((t - t0) / Math.max(1, t1 - t0) * W).toFixed(1), y = v => (H - Math.max(0, Math.min(1, (v - y0) / (ymax - y0))) * H).toFixed(1);
  const marcas = [0.5].map(f => '<line class="met-g" x1="0" x2="' + W + '" y1="' + (H * f) + '" y2="' + (H * f) + '"/>').join("");
  const linhas = series.map((s, i) => {
    const d = s.pontos.map(p => x(p[0]) + "," + y(p[1])).join(" ");
    return s.pontos.length ? (i === 0 ? '<polygon class="met-a s0" points="0,' + H + " " + d + " " + x(s.pontos[s.pontos.length - 1][0]) + "," + H + '"/>' : "") +
      '<polyline class="met-l s' + i + '" points="' + d + '"/>' : "";
  }).join("");
  const g = svg(marcas + linhas, "0 0 " + W + " " + H);
  g.setAttribute("preserveAspectRatio", "none");
  g.setAttribute("class", "met-svg");
  const fmtT = (t, curto) => new Date(t * 1000).toLocaleString("pt-BR", metFiltro.horas > 24 ? { day: "2-digit", month: "2-digit", hour: "2-digit", minute: curto ? undefined : "2-digit" } : { hour: "2-digit", minute: "2-digit" });
  const linha = el("div", { class: "met-mira" }), dica = el("div", { class: "met-dica" });
  const area = el("div", { class: "met-area" }, g, linha, dica);
  const sair = () => { linha.style.display = dica.style.display = "none"; };
  area.addEventListener("pointermove", e => {
    const r = area.getBoundingClientRect(), f = Math.max(0, Math.min(1, (e.clientX - r.left) / r.width)), t = t0 + f * (t1 - t0);
    const achar = pts => pts.reduce((m, p) => Math.abs(p[0] - t) < Math.abs(m[0] - t) ? p : m, pts[0]);
    const ps = series.filter(s => s.pontos.length).map(s => achar(s.pontos));
    const px = (ps[0][0] - t0) / Math.max(1, t1 - t0) * r.width;
    linha.style.display = dica.style.display = "block"; linha.style.left = px + "px";
    dica.textContent = ps.map(p => metFmt(p[1], unidade)).join("  ·  ") + "  " + fmtT(ps[0][0]);
    dica.style.left = Math.min(Math.max(px, 70), r.width - 70) + "px";
  });
  area.addEventListener("pointerleave", sair); sair();
  return el("div", { class: "met-curva" },
    el("div", { class: "met-eixo" }, el("span", {}, metFmt(ymax, unidade)), el("span", {}, metFmt(y0, unidade))), area,
    el("div", { class: "met-x" }, el("span", {}, fmtT(t0, true)), el("span", {}, "agora")));
}

/** A pagina de metricas: um cartao por metrica (valor de agora em destaque, media e pico, curva). Tudo vem de /api/metricas, que consulta o Prometheus por dentro. */
function blocoMetricas() {
  const raiz = el("div", { class: "pilha" });
  const faixa = el("div", { class: "met-faixa" }), grade = el("div", { class: "met-grade" });
  const aviso1 = el("div", { class: "mu" }, "Carregando…");
  const periodo = el("div", { class: "seg" }, [[1, "1 h"], [6, "6 h"], [24, "24 h"], [168, "7 dias"]].map(([h, t]) =>
    el("button", { class: metFiltro.horas === h ? "on" : "", type: "button", on: { click: () => { metFiltro.horas = h; for (const b of periodo.children) b.classList.toggle("on", b.textContent === t); atualizar(); } } }, t)));
  const caixas = {};
  let grafLink = "";

  async function atualizar() {
    if (!raiz.isConnected) return;
    let r;
    try { r = await api("metricas"); } catch (e) { aviso1.textContent = "Métricas indisponíveis: " + e.message; return; }
    if (!r.disponivel) { aviso1.textContent = "Métricas desligadas. Defina PROMETHEUS_URL no .env do servidor (e GRAFANA_URL para o link do painel completo) e reinicie o contêiner."; periodo.classList.add("oculto"); return; }
    aviso1.textContent = "";
    periodo.classList.remove("oculto");
    const v = r.valores;
    const chips = [["Ligado há", v.uptime == null ? null : duracaoLonga(v.uptime)], ["Carga", v.carga == null ? null : num(v.carga, 2)]].filter(c => c[1] != null);
    if (r.grafana && !grafLink) grafLink = r.grafana;
    faixa.replaceChildren(...chips.map(([n, t]) => el("span", { class: "met-chip" }, el("span", { class: "mu" }, n + " "), el("b", {}, t))),
      ...(grafLink ? [el("a", { class: "b", href: grafLink, target: "_blank", rel: "noopener", title: "Abre o Grafana direto, só na sua rede" }, "Abrir no Grafana")] : []));
    const visiveis = MET_GRAFICOS.filter(([k]) => k !== "carga" && (k === "rede" ? v.rede_rx != null : v[k] != null));
    for (const [k, nome, un] of visiveis) {
      if (!caixas[k]) { caixas[k] = el("div", { class: "met-card" + (k === "rede" ? " larga" : ""), id: "met-" + k }); grade.append(caixas[k]); }
      carregarGrafico(k, nome, un, v);
    }
    if (metAlvo && caixas[metAlvo]) { const c = caixas[metAlvo]; metAlvo = ""; setTimeout(() => c.scrollIntoView({ behavior: "smooth", block: "start" }), 50); }
    clearTimeout(atualizar.t); atualizar.t = setTimeout(atualizar, 30000);
  }
  async function carregarGrafico(k, nome, un, v) {
    const nomes = k === "rede" ? ["rede_rx", "rede_tx"] : [k];
    try {
      const rs = await Promise.all(nomes.map(n => api("metricas/serie?nome=" + n + "&horas=" + metFiltro.horas)));
      const vals = rs[0].pontos.map(p => p[1]);
      const sub = !vals.length ? "" : k === "rede" ? "↓ recebido · ↑ enviado" : "média " + metFmt(vals.reduce((a, b) => a + b, 0) / vals.length, un) + " · pico " + metFmt(Math.max(...vals), un);
      const grande = k === "rede" ? el("div", { class: "met-valor" }, el("span", { class: "met-rx" }, "↓ " + bps(v.rede_rx)), el("span", { class: "met-tx" }, "↑ " + bps(v.rede_tx)))
        : el("div", { class: "met-valor" + metNivel(k, v[k]) }, metFmt(v[k], un));
      caixas[k].replaceChildren(
        el("div", { class: "met-cab" }, el("div", { class: "nomes" }, el("div", { class: "nome" }, nome), el("div", { class: "det" }, sub)), grande),
        curvaDeMetrica(rs.map(s => ({ pontos: s.pontos })), un));
    } catch (e) { caixas[k].replaceChildren(el("div", { class: "mu" }, nome + ": " + e.message)); }
  }
  raiz.append(el("div", { class: "linha", style: "justify-content:space-between" }, el("div", { class: "mu" }, "Do servidor da casa, atualizado a cada 30 s."), periodo), aviso1, faixa, grade);
  setTimeout(atualizar, 0);   // depois de entrar na pagina (atualizar para quando a secao sai do DOM)
  return cartaoSimples("Métricas do servidor", "CPU, memória, disco, rede e uptime", null, raiz);
}

// ---------------- saude do servidor ----------------

async function carregarSistema(caixa) {
  try {
    const x = await api("sistema");
    const mb = v => v >= 1024 ? num(v / 1024, 1) + " GB" : v + " MB";
    const linhas = [];
    // O quarto valor de cada linha e o grafico da secao Metricas para onde o clique leva (#ajustes-metricas-cpu...).
    if (x.carga) linhas.push(["Carga", x.carga.join(" · ") + " (" + x.cpus + " núcleos)", x.carga[0] > x.cpus * 1.5, "cpu"]);
    if (x.memoria) linhas.push(["Memória livre", mb(x.memoria.livreMB) + " de " + mb(x.memoria.totalMB), x.memoria.livreMB < x.memoria.totalMB * 0.1, "ram"]);
    if (x.disco) linhas.push(["Disco livre", num(x.disco.livreGB, 1) + " GB de " + num(x.disco.totalGB, 0) + " GB", x.disco.livreGB < 2, "disco"]);
    if (x.temperatura != null) linhas.push(["Temperatura", num(x.temperatura, 0) + " °C", x.temperatura >= 80, "temp"]);
    if (x.ligadoHoras != null) linhas.push(["Ligado há", x.ligadoHoras >= 48 ? num(x.ligadoHoras / 24, 1) + " dias" : num(x.ligadoHoras, 1) + " h", false]);
    if (x.banco) linhas.push(["Banco da Casa", num(x.banco.MB, 1) + " MB", false]);
    if (x.backup) linhas.push(["Último backup", x.backup.quantidade ? (x.backup.idadeHoras < 1 ? "há menos de 1 h" : "há " + num(x.backup.idadeHoras, 0) + " h") + " (" + x.backup.quantidade + " guardados)" : "nenhum encontrado", x.backup.velho]);
    const alerta = linhas.some(l => l[2]);
    caixa.replaceChildren(
      el("div", { class: "cab" }, el("div", { class: "nomes" }, el("div", { class: "nome" }, "Saúde do servidor"),
        el("div", { class: "det" }, alerta ? "algo pede atenção" : "tudo em ordem"))),
      el("div", { class: "corpo" }, el("table", { class: "aparelhos" }, linhas.map(l => el("tr", l[3] ? { class: "vai-met", tabindex: "0", role: "link", title: "Ver o gráfico em Métricas",
        on: { click: () => { metAlvo = l[3]; irPara("ajustes-metricas"); }, keydown: e => { if (e.key === "Enter") { metAlvo = l[3]; irPara("ajustes-metricas"); } } } } : {},
        el("td", {}, l[0]), el("td", { class: "n" + (l[2] ? " mais" : "") }, l[2] ? "⚠ " + l[1] : l[1]))))));
  } catch (e) { caixa.replaceChildren(el("div", { class: "corpo mu" }, "Saúde do servidor indisponível: " + e.message)); }
}

// ---------------- clima e sugestao ----------------

// Icones do tempo em traco, com cor por parte como no app Tempo: sol amarelo, lua clara, gota azul, raio amarelo.
const NUV_ALTA = '<g class="n"><path d="M7 15h10a4 4 0 0 0 .6-7.95A5.5 5.5 0 0 0 7 8.1 3.45 3.45 0 0 0 7 15z"/></g>';
const ICONES_CLIMA = {
  sol: '<g class="s"><circle cx="12" cy="12" r="4.2"/><path d="M12 2.8v2.2M12 19v2.2M2.8 12H5M19 12h2.2M5.5 5.5 7 7M17 17l1.5 1.5M18.5 5.5 17 7M7 17l-1.5 1.5"/></g>',
  lua: '<g class="l"><path d="M19.5 14.6A7.6 7.6 0 0 1 9.4 4.5a7.6 7.6 0 1 0 10.1 10.1z"/></g>',
  "sol-nuvem": '<g class="s"><circle cx="8.5" cy="8" r="3"/><path d="M8.5 2.2v1.2M2.7 8h1.2M4.4 3.9l.9.9M12.6 3.9l-.9.9M4.4 12.1l.9-.9"/></g><g class="n"><path d="M10.5 20.5h7a3.5 3.5 0 0 0 .5-6.96 4.8 4.8 0 0 0-9.1 1.06 2.95 2.95 0 0 0 1.6 5.9z"/></g>',
  "lua-nuvem": '<g class="l"><path d="M12.3 9.2A4.6 4.6 0 0 1 6.3 3.2a4.6 4.6 0 1 0 6 6z"/></g><g class="n"><path d="M10.5 20.5h7a3.5 3.5 0 0 0 .5-6.96 4.8 4.8 0 0 0-9.1 1.06 2.95 2.95 0 0 0 1.6 5.9z"/></g>',
  nuvem: '<g class="n"><path d="M7 18.5h10a4 4 0 0 0 .6-7.95A5.5 5.5 0 0 0 7 11.6 3.45 3.45 0 0 0 7 18.5z"/></g>',
  neblina: NUV_ALTA + '<g class="n"><path d="M4 18h16M6 21h12"/></g>',
  garoa: NUV_ALTA + '<g class="g"><path d="M8.5 18.2v1M12 19v1M15.5 18.2v1"/></g>',
  chuva: NUV_ALTA + '<g class="g"><path d="M8.2 17.5l-1 2.6M12.2 17.5l-1 2.6M16.2 17.5l-1 2.6"/></g>',
  "chuva-forte": NUV_ALTA + '<g class="g"><path d="M7 17.5l-1.4 3.6M10.4 17.5 9 21.1M13.8 17.5l-1.4 3.6M17.2 17.5l-1.4 3.6"/></g>',
  tempestade: NUV_ALTA + '<g class="r"><path d="M12.8 15.5 10.6 19h3l-2.2 3.5"/></g>',
  neve: NUV_ALTA + '<g class="g"><path d="M8 18.5h.01M12 19.5h.01M16 18.5h.01M10 21.5h.01M14 21.5h.01"/></g>',
  nascer: '<g class="s"><path d="M7 17a5 5 0 0 1 10 0M12 3v5M9.6 5.4 12 3l2.4 2.4"/></g><path d="M3 17h18M6 20.5h12"/>',
  por: '<g class="s"><path d="M7 17a5 5 0 0 1 10 0M12 3v5M9.6 5.6 12 8l2.4-2.4"/></g><path d="M3 17h18M6 20.5h12"/>'
};
// Icones pequenos dos cabecalhos das secoes (monocromaticos).
const ICONES_SEC = {
  relogio: '<circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/>',
  calendario: '<rect x="4" y="5" width="16" height="15" rx="2.5"/><path d="M4 10h16M8.5 3v4M15.5 3v4"/>',
  termometro: '<path d="M10 14.5V5a2 2 0 0 1 4 0v9.5a4 4 0 1 1-4 0z"/><path d="M12 9v7"/>',
  gota: '<path d="M12 3.5s6 6.6 6 10.6a6 6 0 0 1-12 0c0-4 6-10.6 6-10.6z"/>',
  vento: '<path d="M3 8h11a3 3 0 1 0-3-3M3 12h16a3 3 0 1 1-3 3M3 16h8"/>',
  uv: '<circle cx="12" cy="12" r="4"/><path d="M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6 7 7M17 17l1.4 1.4M18.4 5.6 17 7M7 17l-1.4 1.4"/>',
  chuva: '<path d="M7 14h10a4 4 0 0 0 .6-7.95A5.5 5.5 0 0 0 7 7.1 3.45 3.45 0 0 0 7 14z"/><path d="M8.2 17l-1 2.6M12.2 17l-1 2.6M16.2 17l-1 2.6"/>',
  pressao: '<circle cx="12" cy="12" r="8.5"/><path d="M12 12l4-4M7.5 15.5h.01M16.5 15.5h.01M12 5.5v1.5"/>',
  sol: '<path d="M4 17a8 8 0 0 1 16 0M2.5 17h19"/><path d="M12 6V3"/>',
  nuvens: '<path d="M7 18.5h10a4 4 0 0 0 .6-7.95A5.5 5.5 0 0 0 7 11.6 3.45 3.45 0 0 0 7 18.5z"/>',
  local: '<path d="M12 21s-6.5-6.2-6.5-11a6.5 6.5 0 0 1 13 0c0 4.8-6.5 11-6.5 11z"/><circle cx="12" cy="10" r="2.3"/>',
  casa: '<path d="M4 11l8-7 8 7v9h-5v-6H9v6H4z"/>'
};
function icClima(n, cls) { const s = svg(ICONES_CLIMA[n] || ICONES_CLIMA.nuvem); s.setAttribute("class", "tic" + (cls ? " " + cls : "")); return s; }
function icSec(n) { const s = svg(ICONES_SEC[n]); s.setAttribute("class", "tsi"); return s; }
const SEMANA = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];
const diaDe = iso => { const [a, m, d] = iso.split("-").map(Number); return new Date(a, m - 1, d); };
let climaAba = "futuro";

let climaDados = null, climaEm = 0;
// Cidade aberta na aba Clima (so neste aparelho): 0 e a casa; as outras vem dos Ajustes.
let climaIdx = 0, climaPag = null;
// As cidades ja abertas ficam guardadas: voltar a uma delas e instantaneo, e a leitura nova vem por tras.
const climaCache = {};
try { climaIdx = Math.max(0, parseInt(localStorage.getItem("climaCidade"), 10) || 0); } catch (e) {}

async function carregarClima(forcar) {
  if (!forcar && Date.now() - climaEm < 5 * 60 * 1000) return;
  climaEm = Date.now();
  const fotografia = () => JSON.stringify([climaDados, climaPag], (k, v) => k === "atualizado" || k === "em" ? undefined : v);
  const antes = fotografia();
  try {
    const [c, s] = await Promise.all([api("clima"), api("sugestao")]);
    climaDados = { c, s: s.sugestao };
  } catch (e) { climaDados = { erro: e.message }; }
  if (climaIdx > 0) await carregarCidade(climaIdx, true);
  // A previsao e a sugestao mudam pouco: se vieram iguais, nada e refeito (reconstruir a pagina inteira era o piscar ao entrar).
  const mudou = fotografia() !== antes;
  if (!mudou && !forcar) return;
  desenharClima();
  if (abaAtual === "clima") desenharClimaPagina();
}

async function carregarCidade(i, quieto) {
  climaIdx = i;
  try { localStorage.setItem("climaCidade", String(i)); } catch (e) {}
  if (i === 0) { climaPag = null; if (!quieto) desenharClimaPagina(); return; }
  const guardado = climaCache[i];
  if (!quieto) { climaPag = guardado || { carregando: true }; desenharClimaPagina(); }
  if (guardado && Date.now() - guardado.em < 10 * 60 * 1000) return;
  let novo;
  try { novo = { c: await api("clima?i=" + i), em: Date.now() }; climaCache[i] = novo; }
  catch (e) { novo = guardado || { erro: e.message }; }
  if (climaIdx !== i) return;          // trocou de cidade enquanto esta carregava
  climaPag = novo;
  if (novo.c) climaIdx = novo.c.i || 0;
  if (!quieto || abaAtual === "clima") desenharClimaPagina();
}

/** A sugestao muda quando o ar muda (inclusive por outro cliente): relida a cada leitura nova, e so redesenha se mudou. */
let sugestaoLendo = false;
async function atualizarSugestao() {
  if (sugestaoLendo || !climaDados || climaDados.erro || !climaDados.c.local) return;
  sugestaoLendo = true;
  try {
    const nova = (await api("sugestao")).sugestao;
    if (JSON.stringify(nova) !== JSON.stringify(climaDados.s)) {
      climaDados.s = nova;
      desenharClima();
      if (abaAtual === "clima") desenharClimaPagina();
    }
  } catch (e) { /* fica a anterior */ }
  sugestaoLendo = false;
}

async function aplicarSugestao(botao) {
  botao.disabled = true;
  try {
    await api("sugestao/aplicar", "POST"); aviso("Sugestão enviada ao ar");
    if (climaDados && climaDados.s) { climaDados.s = Object.assign({}, climaDados.s, { aplicada: true, aplicadaEm: Math.floor(Date.now() / 1000) }); desenharClima(); if (abaAtual === "clima") desenharClimaPagina(); }
    setTimeout(atualizar, 3500);
  }
  catch (e) { aviso("Não deu: " + e.message); }
  botao.disabled = false;
}

/** Cor de uma temperatura, do azul (frio) ao vermelho (quente), para as barras de minima e maxima. */
function corTemp(t) {
  const P = [[0, [91, 141, 239]], [12, [79, 195, 247]], [18, [110, 214, 160]], [23, [247, 209, 84]], [28, [245, 163, 66]], [34, [239, 107, 74]]];
  if (t <= P[0][0]) return "rgb(" + P[0][1] + ")";
  for (let i = 1; i < P.length; i++) if (t <= P[i][0]) {
    const [t0, c0] = P[i - 1], [t1, c1] = P[i], f = (t - t0) / (t1 - t0);
    return "rgb(" + c0.map((v, k) => Math.round(v + (c1[k] - v) * f)).join(",") + ")";
  }
  return "rgb(" + P[P.length - 1][1] + ")";
}

/** A barra de minima a maxima do dia, na escala da lista inteira (como no iPhone); hoje leva um ponto na temperatura atual. */
function barraTemp(min, max, lo, hi, atual) {
  const pct = v => ((v - lo) / Math.max(1, hi - lo)) * 100;
  const seg = el("i");
  seg.style.left = pct(min) + "%";
  seg.style.width = Math.max(3, pct(max) - pct(min)) + "%";
  seg.style.background = "linear-gradient(90deg," + corTemp(min) + "," + corTemp(max) + ")";
  const trilho = el("div", { class: "tbar" }, seg);
  if (atual != null) { const p = el("b"); p.style.left = pct(Math.max(min, Math.min(max, atual))) + "%"; trilho.appendChild(p); }
  return trilho;
}

function nomeDoDia(iso) {
  const dif = Math.round((diaDe(iso) - diaDe(isoDia(new Date()))) / 864e5);
  if (dif === 0) return "Hoje";
  if (dif === -1) return "Ontem";
  return SEMANA[diaDe(iso).getDay()] + (dif < 0 ? " " + diaDe(iso).getDate() : "");
}

const graus = v => v == null ? "--" : num(v, 0) + "°";
const minutos = hm => { const [h, m] = (hm || "0:0").split(":").map(Number); return h * 60 + m; };

function secao(icone, titulo, ...corpo) {
  return el("div", { class: "tsec" }, el("div", { class: "tsec-tit" }, icSec(icone), titulo), ...corpo);
}

/** Um quadradinho de detalhe: titulo, valor grande, algo visual opcional e uma frase. */
function tile(icone, titulo, valor, extra, frase) {
  return el("div", { class: "tsec ttile" }, el("div", { class: "tsec-tit" }, icSec(icone), titulo),
    el("div", { class: "tval" }, valor), extra || null, frase ? el("div", { class: "tfrase" }, frase) : null);
}

function rotuloUV(v) { return v == null ? "" : v < 3 ? "Baixo" : v < 6 ? "Moderado" : v < 8 ? "Alto" : v < 11 ? "Muito alto" : "Extremo"; }
function direcao(g) { return g == null ? "" : ["N", "NE", "L", "SE", "S", "SO", "O", "NO"][Math.round(g / 45) % 8]; }
function orvalho(t, ur) {
  if (t == null || !ur) return null;
  const a = 17.27, b = 237.7, al = Math.log(ur / 100) + a * t / (b + t);
  return b * al / (a - al);
}

function detalhesDoTempo(c, ar) {
  const ag = c.agora, hoje = c.dias[0] || {}, amanha = c.dias[1];
  const sens = ag.sensacao, dif = sens != null && ag.temp != null ? sens - ag.temp : 0;
  const po = orvalho(ag.temp, ag.umidade);

  const seta = svg('<path d="M12 3l4 9h-3v9h-2v-9H8z"/>');
  seta.setAttribute("class", "tseta");
  seta.style.transform = "rotate(" + (((ag.direcao || 0) + 180) % 360) + "deg)";   // a seta aponta para onde o vento vai

  const uvMax = hoje.uvMax, uv = ag.uv != null ? ag.uv : 0;
  const uvBar = el("div", { class: "tuv" }, el("b"));
  uvBar.firstChild.style.left = Math.min(100, uv / 11 * 100) + "%";

  // Arco do sol: posicao entre o nascer e o por de hoje.
  const agoraMin = new Date().getHours() * 60 + new Date().getMinutes();
  const n0 = minutos(hoje.nascer), p0 = minutos(hoje.por);
  const f = agoraMin > p0 ? 0 : Math.max(0, Math.min(1, (agoraMin - n0) / Math.max(1, p0 - n0)));
  const ang = Math.PI * (1 - f), cx = 50 + 40 * Math.cos(ang), cy = 44 - 36 * Math.sin(ang);
  const arco = svg('<path class="tarco" d="M10 44a40 36 0 0 1 80 0"/><path class="tarco-feito" d="M10 44a40 36 0 0 1 ' + (cx - 10).toFixed(1) + ' ' + (cy - 44).toFixed(1) + '"/>' +
    '<line x1="2" y1="44" x2="98" y2="44" class="thor"/>' + (f > 0 && f < 1 ? '<circle cx="' + cx.toFixed(1) + '" cy="' + cy.toFixed(1) + '" r="4.5" class="tsolp"/>' : ''), "0 0 100 50");
  arco.setAttribute("class", "tarco-svg");

  const pressao = ag.pressao;
  return el("div", { class: "tdet" }, tileAr(ar),
    tile("termometro", "Sensação", graus(sens), null,
      Math.abs(dif) < 1.5 ? "Parecida com a temperatura real." : dif > 0 ? "A umidade faz parecer mais quente." : "O vento faz parecer mais frio."),
    tile("gota", "Umidade", ag.umidade != null ? num(ag.umidade, 0) + "%" : "--", null, po != null ? "Ponto de orvalho em " + num(po, 0) + "° agora." : ""),
    tile("vento", "Vento", el("span", {}, num(ag.vento, 0), el("small", {}, " km/h")), el("div", { class: "tvento" }, seta, el("span", {}, direcao(ag.direcao))),
      ag.rajada != null ? "Rajadas de até " + num(ag.rajada, 0) + " km/h." : ""),
    tile("uv", "Índice UV", el("span", {}, num(uv, 0), el("small", {}, " " + rotuloUV(uv))), uvBar,
      uvMax != null ? "Máximo de hoje: " + num(uvMax, 0) + " (" + rotuloUV(uvMax).toLowerCase() + ")." : ""),
    tile("chuva", "Chuva", el("span", {}, num(hoje.chuvaMm || 0, 1), el("small", {}, " mm")), null,
      "Hoje" + (hoje.prob != null ? ", " + num(hoje.prob, 0) + "% de chance" : "") + "." + (amanha && amanha.chuvaMm ? " Amanhã " + num(amanha.chuvaMm, 1) + " mm." : "")),
    tile("pressao", "Pressão", el("span", {}, pressao != null ? String(Math.round(pressao)) : "--", el("small", {}, " hPa")), null,
      pressao == null ? "" : pressao < 1005 ? "Baixa: tempo instável." : pressao > 1020 ? "Alta: tempo firme." : "Normal."),
    tile("sol", agoraMin >= n0 && agoraMin <= p0 ? "Pôr do sol" : "Nascer do sol", agoraMin >= n0 && agoraMin <= p0 ? hoje.por || "--" : (agoraMin > p0 && c.dias[1] ? c.dias[1].nascer : hoje.nascer) || "--", arco,
      agoraMin >= n0 && agoraMin <= p0 ? "Nasceu às " + hoje.nascer + "." : "Pôr do sol às " + (hoje.por || "--") + "."),
    tile("nuvens", "Nuvens", ag.nuvens != null ? num(ag.nuvens, 0) + "%" : "--", null,
      ag.nuvens == null ? "" : ag.nuvens < 20 ? "Céu aberto." : ag.nuvens < 60 ? "Algumas nuvens." : ag.nuvens < 90 ? "Bastante nublado." : "Céu encoberto."));
}

function listaDeDias(c) {
  const todos = c.dias.concat(c.passado || []);
  const lo = Math.min(...todos.map(x => x.min).filter(v => v != null)), hi = Math.max(...todos.map(x => x.max).filter(v => v != null));
  const futuro = climaAba === "futuro";
  const lista = futuro ? c.dias : (c.passado || []).slice().reverse();
  const linhas = lista.map((x, i) => el("div", { class: "tdia" },
    el("span", { class: "tnome" }, nomeDoDia(x.dia)),
    el("span", { class: "tic-col" }, icClima(x.icone),
      futuro ? (x.prob >= 20 ? el("small", { class: "azul" }, num(x.prob, 0) + "%") : null)
        : (x.chuvaMm > 0.1 ? el("small", { class: "azul" }, num(x.chuvaMm, x.chuvaMm < 10 ? 1 : 0) + " mm") : null)),
    el("span", { class: "tmin" }, graus(x.min)),
    barraTemp(x.min, x.max, lo, hi, futuro && i === 0 ? c.agora.temp : null),
    el("span", { class: "tmax" }, graus(x.max))));
  const seg = el("div", { class: "seg tseg" },
    el("button", { class: futuro ? "on" : "", on: { click: () => { climaAba = "futuro"; desenharClimaPagina(); } } }, (c.dias.length) + " dias"),
    el("button", { class: futuro ? "" : "on", on: { click: () => { climaAba = "passado"; desenharClimaPagina(); } } }, "Últimos " + (c.passado || []).length));
  let resumo = null;
  if (!futuro && c.passado && c.passado.length) {
    const mx = c.passado.map(x => x.max).filter(v => v != null), chuva = c.passado.reduce((t, x) => t + (x.chuvaMm || 0), 0);
    const dias = c.passado.filter(x => (x.chuvaMm || 0) > 0.1).length;
    resumo = el("div", { class: "tresumo" }, "Máxima média de " + num(mx.reduce((a, b) => a + b, 0) / mx.length, 0) + "°, " +
      num(chuva, 0) + " mm de chuva em " + dias + (dias === 1 ? " dia." : " dias."));
  }
  return secao("calendario", futuro ? "Previsão" : "Histórico", seg, resumo, el("div", { class: "tdias" }, linhas));
}

function faixaPorHora(c) {
  const ag = c.agora;
  const itens = [el("div", { class: "th-h agora" }, el("small", {}, "Agora"), icClima(ag.icone), el("span", { class: "azul" }, ""), el("b", {}, graus(ag.temp)))]
    .concat(c.horas.map(h => h.tipo
      ? el("div", { class: "th-h evento" }, el("small", {}, h.hora), icClima(h.tipo), el("span"), el("b", {}, h.tipo === "nascer" ? "Nascer" : "Pôr do sol"))
      : el("div", { class: "th-h" }, el("small", {}, h.hora), icClima(h.icone), el("span", { class: "azul" }, h.prob >= 20 ? num(h.prob, 0) + "%" : ""), el("b", {}, graus(h.temp)))));
  const frase = (c.destaques || []).join(" ");
  return secao("relogio", "Próximas 24 horas", frase ? el("div", { class: "tresumo" }, frase) : null, el("div", { class: "thoras" }, itens));
}

/** Maximas e minimas dos ultimos dias e dos proximos numa faixa so, com o hoje marcado. */
function graficoDoTempo(c, largura) {
  const dias = (c.passado || []).concat(c.dias).filter(x => x.max != null && x.min != null);
  if (dias.length < 3) return null;
  const W = Math.max(300, Math.round(largura || 680)), H = 170, ml = 28, mr = 10, mt = 14, mb = 26;
  const lo = Math.floor(Math.min(...dias.map(x => x.min)) - 1), hi = Math.ceil(Math.max(...dias.map(x => x.max)) + 1);
  const X = i => ml + i * (W - ml - mr) / (dias.length - 1), Y = v => mt + (hi - v) / Math.max(1, hi - lo) * (H - mt - mb);
  const linha = k => dias.map((x, i) => (i ? "L" : "M") + X(i).toFixed(1) + " " + Y(x[k]).toFixed(1)).join("");
  const faixa = linha("max") + dias.slice().reverse().map((x, j) => "L" + X(dias.length - 1 - j).toFixed(1) + " " + Y(x.min).toFixed(1)).join("") + "Z";
  const iHoje = dias.findIndex(x => x.dia === (c.dias[0] || {}).dia);
  let g = '<path d="' + faixa + '" class="tg-faixa"/>';
  for (let v = Math.ceil(lo / 5) * 5; v <= hi; v += 5) g += '<line x1="' + ml + '" x2="' + (W - mr) + '" y1="' + Y(v).toFixed(1) + '" y2="' + Y(v).toFixed(1) + '" class="tg-grade"/><text x="' + (ml - 6) + '" y="' + (Y(v) + 4).toFixed(1) + '" class="tg-eixo" text-anchor="end">' + v + '°</text>';
  if (iHoje > 0) g += '<rect x="' + ml + '" y="' + mt + '" width="' + (X(iHoje) - ml).toFixed(1) + '" height="' + (H - mt - mb) + '" class="tg-passado"/><line x1="' + X(iHoje).toFixed(1) + '" x2="' + X(iHoje).toFixed(1) + '" y1="' + mt + '" y2="' + (H - mb) + '" class="tg-hoje"/>';
  g += '<path d="' + linha("max") + '" class="tg-max"/><path d="' + linha("min") + '" class="tg-min"/>';
  dias.forEach((x, i) => {
    g += '<circle cx="' + X(i).toFixed(1) + '" cy="' + Y(x.max).toFixed(1) + '" r="2.6" class="tg-pmax"/><circle cx="' + X(i).toFixed(1) + '" cy="' + Y(x.min).toFixed(1) + '" r="2.6" class="tg-pmin"/>';
    const passo = W < 520 ? 3 : W < 800 ? 2 : 1;
    if ((i - iHoje) % passo === 0) g += '<text x="' + X(i).toFixed(1) + '" y="' + (H - 8) + '" class="tg-eixo" text-anchor="middle">' + (i === iHoje ? "Hoje" : SEMANA[diaDe(x.dia).getDay()] + " " + diaDe(x.dia).getDate()) + '</text>';
  });
  // O ponteiro: uma linha no dia mais proximo, os dois pontos em destaque e um balao com os valores.
  g += '<g class="tg-mira" style="display:none"><line y1="' + mt + '" y2="' + (H - mb) + '"/><circle r="4.5" class="tg-pmax"/><circle r="4.5" class="tg-pmin"/></g>';
  const s = svg(g, "0 0 " + W + " " + H);
  s.setAttribute("class", "tgraf");
  s.setAttribute("role", "img");
  s.setAttribute("aria-label", "Máximas e mínimas dos últimos " + (c.passado || []).length + " dias e dos próximos " + c.dias.length);
  const balao = el("div", { class: "tg-balao oculto" });
  const caixa = el("div", { class: "tg-caixa" }, s, balao);
  const mira = s.querySelector(".tg-mira"), [linhaM, pMax, pMin] = mira.children;
  const mover = e => {
    const r = s.getBoundingClientRect(), k = W / r.width;
    const i = Math.max(0, Math.min(dias.length - 1, Math.round(((e.clientX - r.left) * k - ml) / ((W - ml - mr) / (dias.length - 1)))));
    const x = dias[i], xi = X(i);
    mira.style.display = "";
    linhaM.setAttribute("x1", xi); linhaM.setAttribute("x2", xi);
    pMax.setAttribute("cx", xi); pMax.setAttribute("cy", Y(x.max)); pMin.setAttribute("cx", xi); pMin.setAttribute("cy", Y(x.min));
    const nome = i === iHoje ? "Hoje" : SEMANA[diaDe(x.dia).getDay()] + " " + diaDe(x.dia).getDate() + "/" + (diaDe(x.dia).getMonth() + 1);
    const extra = i < iHoje ? (x.chuvaMm > 0.1 ? num(x.chuvaMm, 1) + " mm de chuva" : "sem chuva") : (x.prob != null ? "chuva " + num(x.prob, 0) + "%" : "");
    balao.replaceChildren(el("b", {}, nome + (i < iHoje ? " · medido" : "")),
      el("div", {}, el("span", { class: "tg-lmax" }, "máx " + graus(x.max)), "  ", el("span", { class: "tg-lmin" }, "mín " + graus(x.min))),
      extra ? el("small", {}, x.texto ? x.texto + " · " + extra : extra) : null);
    balao.classList.remove("oculto");
    const px = xi / k, lb = balao.offsetWidth;
    balao.style.left = Math.max(0, Math.min(r.width - lb, px - lb / 2)) + "px";
    balao.style.top = Math.max(0, Y(x.max) / k - balao.offsetHeight - 12) + "px";
  };
  s.addEventListener("pointermove", mover);
  s.addEventListener("pointerdown", mover);
  s.addEventListener("pointerleave", () => { mira.style.display = "none"; balao.classList.add("oculto"); });
  return secao("termometro", "Temperatura: últimos " + (c.passado || []).length + " dias e próximos " + c.dias.length,
    el("div", { class: "tg-legenda" }, el("span", { class: "tg-lmax" }, "máxima"), el("span", { class: "tg-lmin" }, "mínima"), el("span", { class: "tg-lpas" }, "medido")), caixa);
}

function heroiDoTempo(c, local, ar, compacto) {
  const ag = c.agora, hoje = c.dias[0] || {};
  const ceu = ({ sol: "limpo", "sol-nuvem": "limpo", lua: "noite", "lua-nuvem": "noite", tempestade: "tempestade",
    garoa: "chuva", chuva: "chuva", "chuva-forte": "chuva" })[ag.icone] || "nublado";
  const atual = c.atualizado ? Math.max(0, Math.round((Date.now() / 1000 - c.atualizado) / 60)) : null;
  const chipAr = ar ? el("span", { class: "th-aqi", title: "Qualidade do ar (AQI americano)" }, el("i", { style: "background:" + ar.cor }), "AQI " + ar.aqi + " · " + ar.categoria) : null;
  const mini = compacto ? el("div", { class: "th-mini" }, c.horas.filter(h => !h.tipo).slice(0, 6).map(h =>
    el("div", {}, el("small", {}, h.hora.slice(0, 2) + "h"), icClima(h.icone), el("b", {}, graus(h.temp))))) : null;
  return el("div", { class: "theroi ceu-" + ceu + (ag.dia === false ? " escuro" : "") + (compacto ? " compacto" : "") },
    el("div", { class: "th-topo" }, el("span", { class: "th-local" }, icSec("local"), local.nome.split(",")[0]),
      el("span", { class: "th-at" }, c.velho ? "dados antigos" : atual == null ? "" : atual < 2 ? "agora mesmo" : "há " + atual + " min")),
    el("div", { class: "th-meio" },
      el("div", {}, el("div", { class: "th-temp" }, num(ag.temp, 0) + "°"),
        el("div", { class: "th-cond" }, ag.texto),
        el("div", { class: "th-mm" }, "Máx " + graus(hoje.max) + "  Mín " + graus(hoje.min) + (ag.sensacao != null ? "  ·  Sensação " + graus(ag.sensacao) : "")),
        chipAr),
      compacto ? mini : icClima(ag.icone, "th-ic")));
}

function blocoSugestao(s) {
  const auto = pref && pref.dados.auto && pref.dados.auto.ligado;
  return s ? el("div", { class: "sugestao" },
    el("div", {}, el("b", {}, s.titulo), el("span", {}, s.texto),
      auto ? el("small", { class: "auto-nota" }, "Ar automático ligado: o servidor aplica sozinho.") : null),
    s.cena ? (s.aplicada ? el("span", { class: "sug-ok", title: s.aplicadaEm ? "Aplicada às " + hora(s.aplicadaEm) : "O ar já está assim" }, "✓ Aplicada")
      : el("button", { class: "b pri", on: { click: e => aplicarSugestao(e.currentTarget) } }, "Aplicar")) : null) : null;
}

/** Qualidade do ar: valor, faixa colorida (0 a 300) e os poluentes principais. */
function tileAr(ar) {
  if (!ar) return null;
  const barra = el("div", { class: "tuv taqi" }, el("b"));
  barra.firstChild.style.left = Math.min(100, ar.aqi / 300 * 100) + "%";
  const partes = [];
  if (ar.pm25 != null) partes.push("PM2,5 " + num(ar.pm25, 0) + " µg/m³");
  if (ar.pm10 != null) partes.push("PM10 " + num(ar.pm10, 0));
  if (ar.o3 != null) partes.push("O₃ " + num(ar.o3, 0));
  const pior = ar.pior24 != null && ar.pior24 > ar.aqi + 10 ? " Pior nas próximas 24 h: " + ar.pior24 + " (" + ar.categoriaPior.toLowerCase() + ")." : "";
  const t = tile("vento", "Qualidade do ar", el("span", {}, String(ar.aqi), el("small", { style: "color:" + ar.cor }, " " + ar.categoria)), barra,
    partes.join(" · ") + "." + pior);
  t.classList.add("tlarga");
  return t;
}

/** O cartao do clima na Casa: compacto, do tamanho dos outros, depois dos aparelhos; o resto fica na aba Clima. */
function desenharClima() {
  const caixa = $("clima");
  if (!caixa) return;
  if (arranjo().ocultos.includes("clima")) { caixa.classList.add("oculto"); return; }
  const d = climaDados;
  if (!d || d.erro || !d.c.local || !d.c.clima) {
    const msg = !d ? "Carregando…" : d.erro ? "Clima indisponível: " + d.erro : !d.c.local ? "Escolha a cidade nos Ajustes para ver a previsão e a sugestão para o ar."
      : "A Open-Meteo não respondeu agora.";
    caixa.classList.remove("tempo");
    trocarConteudo(caixa, el("div", { class: "cab" }, el("div", { class: "ico" }, icClima("sol-nuvem")),
      el("div", { class: "nomes" }, el("div", { class: "nome" }, "Clima"), el("div", { class: "det" }, msg))),
      ...(d && !d.erro && !d.c.local ? [el("div", { class: "corpo" }, el("button", { class: "b", on: { click: () => irPara("ajustes-clima") } }, "Escolher cidade"))] : []));
    caixa.classList.toggle("oculto", !d);
    return;
  }
  const c = d.c.clima;
  caixa.classList.add("tempo");
  trocarConteudo(caixa, heroiDoTempo(c, d.c.local, d.c.ar, true),
    el("div", { class: "corpo tcorpo" }, blocoSugestao(d.s),
      el("button", { class: "b tmais", on: { click: () => irPara("clima") } }, "Previsão de " + c.dias.length + " dias, histórico e detalhes", icSeta())));
  caixa.classList.remove("oculto");
}

/** A aba Clima: cidades para trocar rapido, e tudo do tempo da cidade escolhida. */
function desenharClimaPagina() {
  const caixa = $("climaPagina"), chips = $("cidadesChips");
  if (!caixa) return;
  // Saindo do esqueleto o conteudo so troca de lugar: a altura nao anima (o esqueleto e so um molde).
  const eraEsqueleto = caixa.classList.contains("esqueleto");
  const casaD = climaDados;
  if (!casaD) { chips.replaceChildren(); esqueletoDoClima(caixa, null); return; }      // o desenho da pagina em brilho, nao um "Carregando" de outra altura
  if (casaD.erro || !casaD.c.local) {
    chips.replaceChildren();
    trocarConteudo(caixa, el("div", { class: "corpo mu" }, casaD.erro ? "Clima indisponível: " + casaD.erro : "Escolha a cidade da casa nos Ajustes."),
      ...(casaD && !casaD.erro ? [el("div", { class: "corpo", style: "padding-top:0" }, el("button", { class: "b", on: { click: () => irPara("ajustes-clima") } }, "Escolher cidade"))] : []));
    caixa.classList.remove("tempo");
    return;
  }
  const lista = casaD.c.locais || [];
  if (climaIdx >= lista.length) climaIdx = 0;
  // As cidades no mesmo segmentado das outras alternancias (Painéis | Perfis, 10 dias | Últimos 7).
  chips.replaceChildren(el("div", { class: "seg" }, ...lista.map((x, i) => el("button", { class: i === climaIdx ? "on" : "", type: "button",
    on: { click: () => { if (i !== climaIdx) carregarCidade(i); } } }, x.casa ? icSec("casa") : null, x.nome.split(",")[0]))),
    el("button", { class: "b peq", type: "button", title: "Adicionar cidade", on: { click: () => irPara("ajustes-clima") } }, "+ Cidade"));
  const pag = climaIdx === 0 ? { c: casaD.c, s: casaD.s } : climaPag || { carregando: true };
  if (pag.carregando) { esqueletoDoClima(caixa, lista[climaIdx]); return; }
  if (pag.erro || !pag.c.clima) {
    caixa.classList.remove("tempo", "esqueleto");
    if (eraEsqueleto) trocarConteudo.imediato = true;
    trocarConteudo(caixa, el("div", { class: "corpo mu" }, pag.erro ? "Clima indisponível: " + pag.erro : "A Open-Meteo não respondeu agora."));
    return;
  }
  caixa.classList.remove("esqueleto");
  const c = pag.c.clima;
  const rolagem = caixa.querySelector(".thoras") ? caixa.querySelector(".thoras").scrollLeft : 0;
  const largura = caixa.clientWidth - (matchMedia("(max-width:760px)").matches ? 54 : 66);
  caixa.classList.add("tempo");
  if (eraEsqueleto) trocarConteudo.imediato = true;
  trocarConteudo(caixa, heroiDoTempo(c, pag.c.local, pag.c.ar, false),
    el("div", { class: "corpo tcorpo" }, climaIdx === 0 ? blocoSugestao(pag.s) : null, faixaPorHora(c), graficoDoTempo(c, largura),
      el("div", { class: "tgrade" }, listaDeDias(c), detalhesDoTempo(c, pag.c.ar)),
      el("div", { class: "tfonte" }, pag.c.local.nome + " · dados da Open-Meteo")));
  const h = caixa.querySelector(".thoras");
  if (h) h.scrollLeft = rolagem;
}

/** A cidade ainda sem dados: o desenho da pagina em brilho, no lugar do "Carregando". */
function esqueletoDoClima(caixa, cidade) {
  caixa.classList.add("tempo", "esqueleto");
  const barras = (...l) => l.map(w => el("i", { style: "width:" + w }));
  trocarConteudo(caixa,
    el("div", { class: "theroi ceu-nublado esq-heroi" },
      el("div", { class: "th-topo" }, el("span", { class: "th-local" }, icSec("local"), cidade ? cidade.nome.split(",")[0] : ""), el("span", { class: "th-at" }, el("span", { class: "gira" }), "carregando")),
      ...barras("120px", "180px", "220px")),
    el("div", { class: "corpo tcorpo" },
      el("div", { class: "tsec" }, ...barras("30%", "100%", "100%")),
      el("div", { class: "tsec" }, el("i", { style: "width:100%;height:150px" })),
      el("div", { class: "tgrade" }, el("div", { class: "tsec" }, ...barras("40%", "100%", "100%", "100%", "100%")), el("div", { class: "tsec" }, ...barras("40%", "100%", "100%", "100%", "100%")))));
}

/** O modo automatico do ar: liga, limites e o que ele fez por ultimo (GET /api/auto). */
function blocoAuto(d) {
  const a = Object.assign({ ligado: false, podeLigar: false, podeDesligar: true, pausaMin: 120, inicio: 0, fim: 24 }, d.auto || {});
  const mudar = x => gravarPref({ auto: Object.assign({}, a, x) });
  const horas = (de, ate, v) => Array.from({ length: ate - de + 1 }, (_, i) => de + i).map(h => el("option", { value: h, selected: h === v }, String(h).padStart(2, "0") + ":00"));
  const situacao = el("div", { class: "auto-sit mu" }, "Carregando…");
  (async () => {
    try {
      const s = await api("auto");
      const linhas = [];
      if (s.pausaAte) linhas.push(el("div", { class: "auto-pausa" }, el("span", {}, "Pausado até " + hora(s.pausaAte) + " porque o ar foi mudado à mão."),
        el("button", { class: "b peq", on: { click: async () => { await api("auto/retomar", "POST"); desenharAjustes(); } } }, "Retomar agora")));
      linhas.push(el("div", { class: "rot", style: "margin-top:12px" }, "Últimas ações"));
      linhas.push(...(s.registro.length ? s.registro.map(r => el("div", { class: "auto-reg" }, el("span", { class: "mu" }, dataHora(r.t)), el("span", {}, r.texto)))
        : [el("div", { class: "mu" }, a.ligado ? "Nada ainda: ele olha a cada 5 minutos e só muda o que precisa." : "Desligado.")]));
      situacao.className = "auto-sit";
      situacao.replaceChildren(...linhas);
    } catch (e) { situacao.textContent = "Sem situação: " + e.message; }
  })();
  return el("div", {},
    chaveLinha("Modo automático", a.ligado, false, v => mudar({ ligado: v })),
    el("div", { class: "mu", style: "margin:2px 0 10px" }, "A cada 5 minutos o servidor olha a temperatura de dentro e de fora, a umidade (a de dentro, quando o ar mede), a média dos últimos dias e a máxima de hoje, e põe o ar no modo e na temperatura de conforto. Cada modo tem um limite para entrar e outro para sair, para o ar não ficar trocando: o frio entra com o quarto 1,5 °C acima do conforto e só sai 1 °C abaixo dele; o desumidificar só é usado quando o ar mede a umidade de dentro (a da rua não serve para isso): entra com 70% e sai com 60% ou quando o quarto esfria 1,5 °C abaixo do conforto (nesse modo o aparelho trava em 22 °C). Entre duas mudanças ele espera pelo menos 15 minutos."),
    chaveLinha("Pode ligar o ar sozinho", a.podeLigar, !a.ligado, v => mudar({ podeLigar: v })),
    chaveLinha("Pode desligar quando abrir a janela resolve", a.podeDesligar, !a.ligado, v => mudar({ podeDesligar: v })),
    el("div", { class: "form", style: "margin-top:10px" },
      el("label", { class: "campo" }, "Das", el("select", { disabled: !a.ligado, on: { change: e => mudar({ inicio: +e.target.value }) } }, horas(0, 23, a.inicio))),
      el("label", { class: "campo" }, "Até", el("select", { disabled: !a.ligado, on: { change: e => mudar({ fim: +e.target.value }) } }, horas(1, 24, a.fim))),
      el("label", { class: "campo" }, "Pausa ao mexer à mão", el("select", { disabled: !a.ligado, on: { change: e => mudar({ pausaMin: +e.target.value }) } },
        [[30, "30 min"], [60, "1 hora"], [120, "2 horas"], [240, "4 horas"], [480, "8 horas"]].map(([v, t]) => el("option", { value: v, selected: v === a.pausaMin }, t))))),
    situacao);
}

/** Cidade do clima: busca pelo nome na Open-Meteo (pelo servidor) e grava as coordenadas. */
function blocoLocal(d) {
  const achados = el("div", { class: "cidades" });
  const entrada = el("input", { placeholder: "Buscar cidade", style: "flex:1;min-width:0", "aria-label": "Buscar cidade" });
  const outras = (d.locais || []).slice();
  const salvarOutras = async l => { await gravarPref({ locais: l }); climaEm = 0; carregarClima(true); };
  let t = null;
  const buscar = async () => {
    const q = entrada.value.trim();
    if (q.length < 2) { achados.replaceChildren(); return; }
    try {
      const r = await api("cidades?q=" + encodeURIComponent(q));
      achados.replaceChildren(...(r.cidades.length ? r.cidades.map(c => el("div", { class: "cidade-achada" }, el("span", {}, c.nome),
        el("button", { class: "b peq", type: "button", on: { click: async () => { await gravarPref({ local: c }); climaEm = 0; carregarClima(true); } } }, "Usar na casa"),
        el("button", { class: "b peq", type: "button", disabled: outras.length >= 8 || outras.some(x => x.nome === c.nome),
          on: { click: () => { entrada.value = ""; achados.replaceChildren(); salvarOutras(outras.concat(c)); } } }, "+ Outra cidade")))
        : [el("div", { class: "mu" }, "Nenhuma cidade com esse nome.")]));
    } catch (e) { achados.replaceChildren(el("div", { class: "mu" }, "Busca falhou: " + e.message)); }
  };
  entrada.addEventListener("input", () => { clearTimeout(t); t = setTimeout(buscar, 350); });
  return el("div", {},
    el("div", { class: "rot" }, "Casa"),
    el("div", { class: "cidade-linha" }, el("span", {}, d.local ? d.local.nome : "Nenhuma cidade. Sem ela o painel não mostra clima nem sugestão."),
      d.local ? el("button", { class: "b peq", on: { click: async () => { await gravarPref({ local: null }); climaDados = null; desenharClima(); } } }, "Remover") : null),
    el("div", { class: "rot", style: "margin-top:14px" }, "Outras cidades (troca rápida na aba Clima)"),
    outras.length ? outras.map((x, i) => el("div", { class: "cidade-linha" }, el("span", {}, x.nome),
      el("button", { class: "b peq", "aria-label": "Subir", disabled: i === 0, on: { click: () => { const l = outras.slice(); [l[i - 1], l[i]] = [l[i], l[i - 1]]; salvarOutras(l); } } }, "▲"),
      el("button", { class: "b peq", on: { click: () => salvarOutras(outras.filter((_, k) => k !== i)) } }, "Remover")))
      : el("div", { class: "mu" }, "Nenhuma. Busque abaixo e toque em “+ Outra cidade”."),
    el("div", { class: "linha", style: "margin-top:14px" }, entrada),
    achados,
    el("div", { class: "mu", style: "margin-top:10px" }, "Previsão e qualidade do ar da Open-Meteo (gratuita). A sugestão para o ar usa só a cidade da casa e o conforto adaptativo (ASHRAE 55)."));
}

// ---------------- ajustes ----------------

async function carregarPref() {
  try {
    const p = await api("preferencias");
    const mudou = !pref || p.versao !== pref.versao;
    pref = p;
    if (!mudou) return;
    if (abaAtual === "ajustes") desenharAjustes(); else { desenharCartoes(); desenharClima(); }
  } catch (e) {}
}

async function gravarPref(dados) {
  try { pref = await api("preferencias", "PUT", { dados, origem: "web" }); aviso("Salvo"); if (abaAtual === "ajustes") desenharAjustes(); else { desenharCartoes(); desenharClima(); } }
  catch (e) { aviso("Não salvou: " + e.message); }
}

function campoNum(rotulo, k, passo) {
  return el("label", { class: "campo" }, rotulo, el("input", { type: "number", step: passo || 1, value: pref.dados[k],
    on: { change: e => { const v = parseFloat(String(e.target.value).replace(",", ".")); if (!isNaN(v) && v >= 0) gravarPref({ [k]: v }); } } }));
}

function opcoesModo(sel, comNenhum) {
  const l = (comNenhum ? [[-1, "Manter"]] : []).concat([[2, "Frio"], [5, "ECO"], [3, "Desumidificar"], [0, "Ventilar"]]);
  return l.map(([v, n]) => el("option", { value: v, selected: v === sel }, n));
}

const AJ_ICONES = {
  alertas: '<path d="M6 16V11a6 6 0 1 1 12 0v5l1.5 2h-15zM10 20a2 2 0 0 0 4 0"/>',
  ar: '<path d="M3 8h12a3 3 0 1 0-3-3M3 12h16a3 3 0 1 1-3 3M3 16h8"/>',
  consumo: '<path d="M13 3 5 14h6l-1 7 8-11h-6z"/>',
  retorno: '<path d="M4 12a8 8 0 1 0 3-6.2M4 4v4h4"/>',
  cenas: '<rect x="4" y="4" width="7" height="7" rx="1.5"/><rect x="13" y="4" width="7" height="7" rx="1.5"/><rect x="4" y="13" width="7" height="7" rx="1.5"/><rect x="13" y="13" width="7" height="7" rx="1.5"/>',
  clima: '<circle cx="9" cy="9" r="3.2"/><path d="M9 3v1.5M3 9h1.5M4.8 4.8l1 1M13.2 4.8l-1 1"/><path d="M8 20h9a3.5 3.5 0 0 0 .4-7A5 5 0 0 0 8 13.6 3.2 3.2 0 0 0 8 20z"/>',
  auto: '<path d="M12 3v2M12 19v2M5.6 5.6l1.4 1.4M17 17l1.4 1.4M3 12h2M19 12h2"/><path d="M9 15.5 12 8l3 7.5M10.1 13h3.8"/>',
  metricas: '<path d="M4 4v16h16"/><path d="m7.5 14 3.5-4 3 2.5 4.5-6"/><path d="M18.5 6.5h-2.2M18.5 6.5v2.2"/>',
  eventos: '<circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/><path d="M4 4.5v3.2h3.2"/>',
  servidor: '<rect x="3" y="4" width="18" height="6" rx="1.5"/><rect x="3" y="14" width="18" height="6" rx="1.5"/><path d="M7 7h.01M7 17h.01"/>',
  seta: '<path d="m9 6 6 6-6 6"/>', voltar: '<path d="m15 6-6 6 6 6"/>'
};
const AJ_COR = { alertas: "#EB5A4B", ar: "#2D7DF6", auto: "#0E9F9A", consumo: "#F08A24", retorno: "#2EA043", cenas: "#7B61E0", clima: "#1FA3C7", metricas: "#D6538A", eventos: "#C79A1B", servidor: "#7C808B" };
function ajSvg(n, cls) {
  const s = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  s.setAttribute("viewBox", "0 0 24 24"); s.setAttribute("fill", "none"); s.setAttribute("stroke", "currentColor");
  s.setAttribute("stroke-width", "1.8"); s.setAttribute("stroke-linecap", "round"); s.setAttribute("stroke-linejoin", "round");
  if (cls) s.setAttribute("class", cls);
  s.innerHTML = AJ_ICONES[n]; return s;
}

/** Os mesmos alertas no celular, pelo app gratuito ntfy: o servidor publica num topico que so voce conhece. */
function blocoCelular(d) {
  const topico = d.ntfyTopico || "";
  const novo = () => "casa-" + Array.from(crypto.getRandomValues(new Uint8Array(16)), b => "abcdefghjkmnpqrstuvwxyz23456789"[b % 31]).join("");
  const inp = el("input", { value: topico, maxLength: 64, placeholder: "nenhum", style: "flex:1;min-width:0", "aria-label": "Tópico do ntfy",
    on: { change: e => gravarPref({ ntfyTopico: e.target.value.trim() }) } });
  return el("div", { class: "sub-cartao", style: "margin-top:14px" },
    el("div", { class: "tit" }, "No celular", el("span", { class: "mu", style: "font-weight:400" }, topico ? "ligado" : "desligado")),
    el("div", { class: "mu", style: "margin-bottom:10px" }, "Instale o app ntfy (Android ou iOS), toque em + e assine o tópico abaixo. " +
      "O servidor manda os alertas ligados acima, mesmo com o PC desligado. Quem souber o tópico recebe os avisos, então use um aleatório."),
    el("div", { class: "linha" }, inp,
      el("button", { class: "b", on: { click: () => gravarPref({ ntfyTopico: novo() }) } }, topico ? "Trocar" : "Gerar tópico")),
    topico && topico.length < 16 ? el("div", { class: "mu", style: "margin-top:8px;color:var(--danger)" },
      "Tópico curto: no ntfy.sh público ele pode ser adivinhado. Toque em Trocar para gerar um mais longo e assine o novo no celular.") : null,
    topico ? el("div", { class: "linha", style: "margin-top:8px" },
      el("button", { class: "b", on: { click: async () => { try { await navigator.clipboard.writeText(topico); aviso("Tópico copiado"); } catch (e) { inp.select(); } } } }, "Copiar"),
      el("button", { class: "b", on: { click: async () => { try { await api("avisos/teste", "POST"); aviso("Teste enviado"); } catch (e) { aviso("Não foi: " + e.message); } } } }, "Enviar teste"),
      el("button", { class: "b perigo", on: { click: () => gravarPref({ ntfyTopico: "" }) } }, "Desligar")) : null);
}

function desenharAjustes() {
  const g = $("ajustes");
  if (!pref) { g.replaceChildren(el("div", { class: "mu" }, "Carregando…")); carregarPref(); return; }
  const d = pref.dados;
  const ar = estado && estado.lista.find(ehAr);
  const ventos = ar ? ar.info.ventos : [{ valor: 2, nome: "Baixa" }, { valor: 3, nome: "Média" }, { valor: 4, nome: "Alta" }];

  const alertas = cartaoSimples("Alertas", "no Windows, pelo Mhiagos Control no PC, e no celular se ligado abaixo", null,
    chaveLinha("Falha no aparelho", d.alertaFalha, false, v => gravarPref({ alertaFalha: v })),
    chaveLinha("Aparelho desconectado", d.alertaDesconectado, false, v => gravarPref({ alertaDesconectado: v })),
    chaveLinha("Ambiente quente", d.alertaQuente, false, v => gravarPref({ alertaQuente: v })),
    el("div", { class: "form", style: "margin:4px 0 8px" }, campoNum("Limite quente (°C)", "limiteQuente")),
    chaveLinha("Fim do ciclo da lavadora", d.alertaLavadoraFim, false, v => gravarPref({ alertaLavadoraFim: v })),
    blocoCelular(d));

  const consumo = cartaoSimples("Estimativa de consumo do ar", "o ar não informa o consumo", null,
    el("div", { class: "form" }, campoNum("Frio (W)", "potenciaFrio"), campoNum("ECO (W)", "potenciaEco"),
      campoNum("Desumidificar (W)", "potenciaSeco"), campoNum("Ventilar (W)", "potenciaVentilar"), campoNum("Tarifa (R$/kWh)", "tarifa", 0.01)),
    { dica: "A estimativa usa o tempo ligado em cada modo vezes a potência." });

  const retorno = cartaoSimples("Padrão de retorno das cenas", "para onde uma cena com duração volta", null,
    el("div", { class: "form" },
      el("label", { class: "campo" }, "Modo", el("select", { on: { change: e => gravarPref({ retornoModo: +e.target.value }) } }, opcoesModo(d.retornoModo))),
      el("label", { class: "campo" }, "Temperatura (°C)", el("input", { type: "number", min: 16, max: 32, value: d.retornoTemp, on: { change: e => gravarPref({ retornoTemp: +e.target.value }) } })),
      el("label", { class: "campo" }, "Ventilador", el("select", { on: { change: e => gravarPref({ retornoVento: +e.target.value }) } },
        ventos.map(v => el("option", { value: v.valor, selected: v.valor === d.retornoVento }, v.nome))))),
    { dica: "Mudar o ar à mão cancela a volta." });

  const lista = d.cenas.map(c => Object.assign({}, c));
  const salvarCenas = () => gravarPref({ cenas: lista });
  const editores = lista.map((c, n) => {
    const set = (k, v) => { c[k] = v; salvarCenas(); };
    return el("div", { class: "editor-cena" },
      el("div", { class: "form" },
        el("label", { class: "campo" }, "Nome", el("input", { value: c.nome, maxLength: 30, on: { change: e => e.target.value.trim() && set("nome", e.target.value.trim()) } })),
        el("label", { class: "campo" }, "Ícone", el("select", { on: { change: e => set("icone", e.target.value) } },
          Object.keys(ICONES).filter(k => k !== "auto").map(k => el("option", { value: k, selected: k === c.icone }, k)))),
        el("label", { class: "campo" }, "Ação", el("select", { on: { change: e => set("desligar", e.target.value === "1") } },
          el("option", { value: "0", selected: !c.desligar }, "Ajustar"), el("option", { value: "1", selected: c.desligar }, "Desligar")))),
      c.desligar ? null : el("div", { class: "form", style: "margin-top:8px" },
        el("label", { class: "campo" }, "Modo", el("select", { on: { change: e => set("modo", +e.target.value) } }, opcoesModo(c.modo, true))),
        el("label", { class: "campo" }, "Temperatura", el("select", { on: { change: e => set("temp", +e.target.value) } },
          [[-1, "Manter"]].concat(Array.from({ length: 15 }, (_, k) => [16 + k, (16 + k) + " °C"])).map(([v, t]) => el("option", { value: v, selected: v === c.temp }, t)))),
        el("label", { class: "campo" }, "Ventilador", el("select", { on: { change: e => set("vento", +e.target.value) } },
          [[-1, "Manter"], [-2, "Mínima"], [-3, "Máxima"]].concat(ventos.map(v => [v.valor, v.nome])).map(([v, t]) => el("option", { value: v, selected: v === c.vento }, t)))),
        el("label", { class: "campo" }, "Noturno", el("select", { on: { change: e => set("noturno", +e.target.value) } },
          [[-1, "Manter"], [1, "Ligar"], [0, "Desligar"]].map(([v, t]) => el("option", { value: v, selected: v === c.noturno }, t)))),
        el("label", { class: "campo" }, "Volta ao padrão", el("select", { on: { change: e => set("duracao", +e.target.value) } },
          [[0, "Nunca"], [15, "15 min"], [30, "30 min"], [60, "1 h"], [120, "2 h"]].map(([v, t]) => el("option", { value: v, selected: v === c.duracao }, t))))),
      el("div", { class: "linha", style: "margin-top:8px" },
        el("button", { class: "b peq", disabled: n === 0, on: { click: () => { lista.splice(n - 1, 0, lista.splice(n, 1)[0]); salvarCenas(); } } }, "◀ Antes"),
        el("button", { class: "b peq", disabled: n === lista.length - 1, on: { click: () => { lista.splice(n + 1, 0, lista.splice(n, 1)[0]); salvarCenas(); } } }, "Depois ▶"),
        el("button", { class: "b peq perigo", on: { click: ev => recolher(ev.currentTarget.closest(".editor-cena"), () => { lista.splice(n, 1); salvarCenas(); }) } }, "Excluir")));
  });
  const cenas = cartaoSimples("Cenas", "até 8, na ordem dos botões", null, editores,
    el("button", { class: "b pri", disabled: lista.length >= 8, on: { click: () => { desenharAjustes.novaCena = true; lista.push({ nome: "Nova cena", icone: "floco", desligar: false, modo: -1, temp: 24, vento: -1, noturno: -1, duracao: 0 }); salvarCenas(); } } }, "Nova cena"));

  const conta = cartaoSimples("Servidor", "ajustes salvos " + (pref.atualizado ? "em " + dataHora(pref.atualizado) + " pelo " + ({ web: "painel web", pc: "Mhiagos no PC" }[pref.origem] || pref.origem || "?") : "nunca"), null,
    el("div", { class: "linha" },
      el("button", { class: "b", on: { click: () => { try { localStorage.removeItem("chave"); } catch (e) {} chave = ""; pedirChave(); } } }, "Trocar a chave deste navegador"),
      el("button", { class: "b perigo", on: { click: async () => { if (confirm("Desconectar o servidor da conta ConnectLife?")) { await api("conta", "DELETE"); atualizar(); } } } }, "Sair da conta ConnectLife")));

  const saude = el("div", { class: "cartao" }, el("div", { class: "corpo mu" }, "Carregando…"));
  carregarSistema(saude);

  // Ar-condicionado: bipe dos comandos (preferencia do servidor) e unidade (propriedade do aparelho)
  const ehF = ar && String(valor(ar, "t_temp_type")) === "1";
  const arCartao = cartaoSimples("Ar-condicionado", ar ? ar.apelido : "nenhum ar conectado", null,
    chaveLinha("Comandos sem bipe", !!d.semBipe, false, v => gravarPref({ semBipe: v })),
    el("div", { class: "mu", style: "margin:2px 0 10px" }, "Os comandos do painel e das cenas pedem ao ar para não apitar."),
    el("div", { class: "chavinha" }, el("span", {}, "Unidade"),
      el("div", { class: "seg" }, ["°C", "°F"].map((t, n) => el("button", { class: (ehF ? "°F" : "°C") === t ? "on" : "", disabled: !ar || !ar.online || !ar.info.unidadeGrava,
        on: { click: () => { comando(ar, { t_temp_type: String(n) }); desenharAjustes(); } } }, t)))),
    !ar || !ar.info.unidadeGrava ? el("div", { class: "mu", style: "margin-top:8px" }, "A unidade só muda com o ar conectado e se ele aceitar.") : null);

  const clima = cartaoSimples("Clima e local", "a cidade usada na previsão e na sugestão para o ar", null, blocoLocal(d));
  const auto = cartaoSimples("Ar automático", "o servidor ajusta o ar sozinho pelo conforto", null, blocoAuto(d),
    { dica: "Usa a mesma conta da sugestão: conforto adaptativo da ASHRAE 55 com a média dos últimos dias." });

  const eventos = blocoEventos(d);
  const metricas = ajSecao === "metricas" ? blocoMetricas() : cartaoSimples("Métricas do servidor", "", null, el("div", { class: "mu" }, "Abra a seção para ver."));
  const sim = b => b ? 1 : 0;
  const secoes = [
    ["alertas", "Alertas", sim(d.alertaFalha) + sim(d.alertaDesconectado) + sim(d.alertaQuente) + sim(d.alertaLavadoraFim) + " de 4 ligados" + (d.ntfyTopico ? " · no celular" : ""), alertas],
    ["ar", "Ar-condicionado", (d.semBipe ? "Sem bipe" : "Com bipe") + " · " + (ehF ? "°F" : "°C"), arCartao],
    ["auto", "Ar automático", !d.auto || !d.auto.ligado ? "Desligado" : "Ligado" + (d.auto.inicio === 0 && d.auto.fim === 24 ? "" : " · " + d.auto.inicio + "h às " + d.auto.fim + "h"), auto],
    ["consumo", "Consumo do ar", "Tarifa R$ " + String(d.tarifa).replace(".", ","), consumo],
    ["clima", "Clima e local", d.local ? d.local.nome : "Nenhuma cidade", clima],
    ["retorno", "Retorno das cenas", (({ 0: "Ventilar", 2: "Frio", 3: "Desumidificar", 5: "ECO" })[d.retornoModo] || "Manter") + " · " + d.retornoTemp + " °C", retorno],
    ["cenas", "Cenas", lista.length + " de 8", cenas],
    ["metricas", "Métricas", "CPU, memória, disco e rede", metricas],
    ["eventos", "Eventos", "Histórico no servidor · " + (d.retencaoEventosDias || 90) + " dias", eventos],
    ["servidor", "Servidor", pref.atualizado ? "Salvo " + dataHora(pref.atualizado) : "Nada salvo", el("div", { class: "pilha" }, conta, saude)]
  ];
  if (!secoes.some(x => x[0] === ajSecao)) ajSecao = "alertas";
  const atual = secoes.find(x => x[0] === ajSecao);
  const itens = secoes.map(([k, t, r]) => el("button", { class: "aj-item" + (k === ajSecao ? " sel" : ""), type: "button",
    on: { click: () => irPara("ajustes-" + k, matchMedia("(min-width:761px)").matches) } },
    (() => { const i = el("div", { class: "aj-ic" }); i.style.background = AJ_COR[k]; i.appendChild(ajSvg(k)); return i; })(),
    el("div", { class: "aj-tx" }, el("b", {}, t), el("span", {}, r)), ajSvg("seta", "aj-seta")));
  const voltar = el("button", { class: "aj-voltar", type: "button", on: { click: () => { if (aplicarHash.daLista) history.back(); else irPara("ajustes", true); } } }, ajSvg("voltar"), "Ajustes");
  g.className = "aj" + (ajAberto ? " det" : "");
  // Celular: lista -> detalhe entra pela direita, detalhe -> lista pela esquerda (eixo compartilhado do Material).
  const trocou = desenharAjustes.aberto !== undefined && desenharAjustes.aberto !== ajAberto;
  const secaoNova = desenharAjustes.secao !== undefined && desenharAjustes.secao !== ajSecao;
  desenharAjustes.aberto = ajAberto; desenharAjustes.secao = ajSecao;
  g.replaceChildren(el("div", { class: "aj-coluna" }, el("div", { class: "aj-lista" }, itens), versaoAjustes()), el("div", { class: "aj-detalhe" }, voltar, atual[3]));
  if (desenharAjustes.novaCena) {
    desenharAjustes.novaCena = false;
    const ed = g.querySelectorAll(".editor-cena");
    if (ed.length) { expandir(ed[ed.length - 1]); ed[ed.length - 1].scrollIntoView({ block: "nearest", behavior: semMovimento() ? "auto" : "smooth" }); }
  }
  if (trocou && matchMedia("(max-width:760px)").matches) animar(ajAberto ? g.lastChild : g.firstChild, ajAberto ? "m3-frente" : "m3-tras");
  else if (secaoNova && !matchMedia("(max-width:760px)").matches) animar(g.lastChild, "m3-aba");
}

// Versao no rodape do Ajustes: a marca desta pagina contra a que o servidor entrega agora.
// Se forem diferentes, o app esta mostrando uma pagina guardada de antes do ultimo deploy.
const PAGINA = (document.querySelector('meta[name="pagina"]') || {}).content || "";
let saudeServidor = null;
async function conferirVersao() {
  try {
    const r = await fetch("/api/saude", { cache: "no-store" });
    if (r.ok) saudeServidor = await r.json();
  } catch (e) { /* sem servidor: o rodape fica so com a marca da pagina */ }
  if (abaAtual === "ajustes") { const v = document.querySelector(".aj-versao"); if (v) v.replaceWith(versaoAjustes()); }
}
function versaoAjustes() {
  const s = saudeServidor;
  const velha = s && s.pagina && s.pagina !== PAGINA;
  return el("div", { class: "aj-versao" + (velha ? " velha" : "") },
    el("div", {}, "Servidor " + (s ? s.versao : "?") + " · página " + PAGINA),
    velha ? el("div", {}, "Há uma versão nova (" + s.pagina + "). ",
      el("button", { class: "b peq", type: "button", on: { click: () => location.reload() } }, "Atualizar"))
      : s ? el("div", {}, "Você está na versão mais recente.") : null);
}

// ---------------- laco ----------------

// O servidor segura /api/mudancas ate algo mudar (leitura nova da nuvem, timer,
// ajustes) e a pagina busca o estado na hora. Sem pergunta a cada 4 s.
const espera = ms => new Promise(r => setTimeout(r, ms));

async function tudo() {
  await Promise.all([atualizar(), carregarPref()]);
  if (estado && (!ciclos || Date.now() - (tudo.ciclos || 0) > 60000)) {
    tudo.ciclos = Date.now();
    await carregarCiclos();
  }
  if (abaAtual === "painel") { desenharCartoes(); carregarClima(); }
}

async function escutar() {
  let versao = -1;
  for (;;) {
    if (document.hidden || !chave) { await espera(1500); continue; }
    try {
      const r = await api("mudancas?desde=" + versao);
      if (r.versao !== versao) { versao = r.versao; await tudo(); }
    } catch (e) { await espera(5000); }
  }
}

if (location.hash) aplicarHash(); else history.replaceState(null, "", location.pathname + location.search + "#painel");
tudo().then(atalho);
escutar();

/** Atalhos da tela inicial (manifest): /?acao=ligar, desligar, timer60 ou /?aba=consumo. */
function atalho() {
  const q = new URLSearchParams(location.search);
  if (![...q.keys()].length) return;
  history.replaceState(null, "", "/" + (location.hash || "#painel"));
  if (ABAS.includes(q.get("aba"))) irPara(q.get("aba"), true);
  const ar = estado && estado.lista.find(ehAr), ac = q.get("acao");
  if (!ar || !ac) return;
  let rotulo, fazer;
  if (ac === "ligar") { rotulo = "Ligar o ar"; fazer = () => comando(ar, { t_power: "1" }); }
  else if (ac === "desligar") { rotulo = "Desligar o ar"; fazer = () => comando(ar, { t_power: "0" }); }
  else if (/^timer\d+$/.test(ac)) { rotulo = "Desligar o ar em " + dur(parseInt(ac.slice(5))); fazer = () => definirTimer(ar, parseInt(ac.slice(5))); }
  else return;
  // App Android (WebView) ou app instalado: veio do atalho do icone. Aberto por um link qualquer, confirma antes.
  if (/; wv\)/.test(navigator.userAgent) || matchMedia("(display-mode: standalone)").matches) { fazer(); return; }
  aviso(rotulo + "?", { texto: "Confirmar", fazer });
}

// Puxar para atualizar (no topo da pagina, so no toque).
(function () {
  let y0 = null, d = 0;
  const p = $("ptr");
  document.addEventListener("touchstart", e => { y0 = scrollY === 0 && e.touches.length === 1 ? e.touches[0].clientY : null; d = 0; }, { passive: true });
  document.addEventListener("touchmove", e => {
    if (y0 == null) return;
    d = e.touches[0].clientY - y0;
    if (d > 0 && scrollY === 0) { p.style.height = Math.min(56, d / 2) + "px"; p.classList.toggle("pronto", d > 110); }
  }, { passive: true });
  document.addEventListener("touchend", () => {
    if (y0 == null) return;
    const ir = d > 110; y0 = null; p.style.height = ""; p.classList.remove("pronto");
    if (ir) { vibrar(10); $("lerAgora").click(); }
  });
})();
// Rede reserva: se a espera longa cair sem erro, a pagina ainda se atualiza.
setInterval(() => { if (!document.hidden) tudo(); }, 30000);
// O relogio do timer e o tempo restante andam sem leitura nova.
setInterval(() => { if (!document.hidden && abaAtual === "painel") desenharCartoes(); if (!document.hidden && estado) desenharSituacao(); }, 20000);
document.addEventListener("visibilitychange", () => { if (!document.hidden) { tudo(); conferirVersao(); } });
conferirVersao();
window.addEventListener("resize", () => {
  clearTimeout(desenharGrafico.t);
  desenharGrafico.t = setTimeout(() => {
    if (abaAtual === "consumo") desenharGrafico();
    else if (abaAtual === "clima") desenharClimaPagina();
    else if (abaAtual === "painel") { for (const r of nos.values()) r.sig = ""; desenharCartoes(); }
  }, 250);
});
if ("serviceWorker" in navigator && window.isSecureContext) navigator.serviceWorker.register("/sw.js").catch(() => {});
