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

async function api(caminho, metodo, corpo) {
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
  const h0 = caixa.offsetHeight;
  caixa.style.minHeight = "";
  caixa.replaceChildren(...filhos);
  caixa.classList.remove("recarregando");
  let h1 = caixa.offsetHeight;
  // Encolher perto do fim da pagina obriga o navegador a puxar a rolagem a cada quadro,
  // e no Android a barra de abas fixa treme junto. Nesse caso a caixa guarda a altura antiga.
  const sobra = document.documentElement.scrollHeight - (scrollY + innerHeight);
  if (h1 < h0 && sobra < h0 - h1) { caixa.style.minHeight = h0 + "px"; h1 = h0; }
  if (!h0 || Math.abs(h1 - h0) < 3 || semMovimento() || !caixa.animate) return;
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
const ABAS = ["painel", "consumo", "ajustes"];
function irPara(h, substituir) {
  if (("#" + h) === location.hash) { aplicarHash(); return; }
  if (substituir) { history.replaceState(null, "", "#" + h); aplicarHash(); }
  else location.hash = h;
}
function aplicarHash() {
  const h = location.hash.slice(1) || "painel";
  const [aba, sec] = h.split("-");
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
  for (const s of ["painel", "consumo", "ajustes"]) $("aba-" + s).classList.toggle("oculto", s !== a);
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

/** Controle segmentado com indicador que desliza (iOS): um span sob o botao "on". */
function posicionarSeg() {
  for (const g of document.querySelectorAll(".seg")) {
    const on = g.querySelector("button.on");
    let i = g.querySelector(".ind");
    if (!i) { i = document.createElement("span"); i.className = "ind"; g.prepend(i); }
    if (!on || !on.offsetWidth) { i.style.opacity = "0"; continue; }
    i.style.opacity = "1"; i.style.width = on.offsetWidth + "px"; i.style.transform = "translateX(" + on.offsetLeft + "px)";
    if (!i.dataset.pronto) requestAnimationFrame(() => requestAnimationFrame(() => { i.dataset.pronto = "1"; }));
  }
}
let posSegAgenda = 0;
new MutationObserver(() => { cancelAnimationFrame(posSegAgenda); posSegAgenda = requestAnimationFrame(posicionarSeg); })
  .observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ["class"] });
addEventListener("resize", () => requestAnimationFrame(posicionarSeg));

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
function desenharCartoes() {
  if (!estado) return;
  conferirPendentes();
  const c = $("cartoes");
  const foco = document.activeElement && c.contains(document.activeElement) ? document.activeElement.dataset.id : null;
  const lista = [];
  for (const a of estado.lista) {
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
    i.modos.length ? [el("div", { class: "rot" }, "Modo"), tiles(i.modos, modo, "t_work_mode", i.grava.t_work_mode, false, m => icone(ICONE_MODO[m.valor]))] : null,
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

  const cartao = el("div", { class: "cartao" }, cabecalho(a, icTermometro()),
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

// ---------------- saude do servidor ----------------

async function carregarSistema(caixa) {
  try {
    const x = await api("sistema");
    const mb = v => v >= 1024 ? num(v / 1024, 1) + " GB" : v + " MB";
    const linhas = [];
    if (x.carga) linhas.push(["Carga", x.carga.join(" · ") + " (" + x.cpus + " núcleos)", x.carga[0] > x.cpus * 1.5]);
    if (x.memoria) linhas.push(["Memória livre", mb(x.memoria.livreMB) + " de " + mb(x.memoria.totalMB), x.memoria.livreMB < x.memoria.totalMB * 0.1]);
    if (x.disco) linhas.push(["Disco livre", num(x.disco.livreGB, 1) + " GB de " + num(x.disco.totalGB, 0) + " GB", x.disco.livreGB < 2]);
    if (x.temperatura != null) linhas.push(["Temperatura", num(x.temperatura, 0) + " °C", x.temperatura >= 80]);
    if (x.ligadoHoras != null) linhas.push(["Ligado há", x.ligadoHoras >= 48 ? num(x.ligadoHoras / 24, 1) + " dias" : num(x.ligadoHoras, 1) + " h", false]);
    if (x.banco) linhas.push(["Banco da Casa", num(x.banco.MB, 1) + " MB", false]);
    if (x.backup) linhas.push(["Último backup", x.backup.quantidade ? (x.backup.idadeHoras < 1 ? "há menos de 1 h" : "há " + num(x.backup.idadeHoras, 0) + " h") + " (" + x.backup.quantidade + " guardados)" : "nenhum encontrado", x.backup.velho]);
    const alerta = linhas.some(l => l[2]);
    caixa.replaceChildren(
      el("div", { class: "cab" }, el("div", { class: "nomes" }, el("div", { class: "nome" }, "Saúde do servidor"),
        el("div", { class: "det" }, alerta ? "algo pede atenção" : "tudo em ordem"))),
      el("div", { class: "corpo" }, el("table", { class: "aparelhos" }, linhas.map(l => el("tr", {}, el("td", {}, l[0]),
        el("td", { class: "n" + (l[2] ? " mais" : "") }, l[2] ? "⚠ " + l[1] : l[1]))))));
  } catch (e) { caixa.replaceChildren(el("div", { class: "corpo mu" }, "Saúde do servidor indisponível: " + e.message)); }
}

// ---------------- clima e sugestao ----------------

const ICONES_CLIMA = {
  sol: '<circle cx="12" cy="12" r="4"/><path d="M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6l1.4 1.4M17 17l1.4 1.4M18.4 5.6 17 7M7 17l-1.4 1.4"/>',
  "sol-nuvem": '<circle cx="8.5" cy="8.5" r="3"/><path d="M8.5 3v1M3 8.5h1M4.6 4.6l.7.7M12.4 4.6l-.7.7"/><path d="M9 20h8a3.2 3.2 0 0 0 .4-6.4A4.6 4.6 0 0 0 9 14.6 2.7 2.7 0 0 0 9 20z"/>',
  nuvem: '<path d="M7 19h10a4 4 0 0 0 .5-8A5.5 5.5 0 0 0 7 12.5 3.3 3.3 0 0 0 7 19z"/>',
  chuva: '<path d="M7 15h10a4 4 0 0 0 .5-8A5.5 5.5 0 0 0 7 8.5 3.3 3.3 0 0 0 7 15z"/><path d="M9 18l-1 2.5M13 18l-1 2.5M17 18l-1 2.5"/>'
};
function icClima(n) {
  const s = ajSvg("seta"); s.removeAttribute("class"); s.innerHTML = ICONES_CLIMA[n] || ICONES_CLIMA.nuvem; return s;
}
const DIA_SEMANA = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];
const diaDe = iso => { const [a, m, d] = iso.split("-").map(Number); return new Date(a, m - 1, d); };

let climaDados = null, climaEm = 0;

async function carregarClima(forcar) {
  if (!forcar && Date.now() - climaEm < 5 * 60 * 1000) return;
  climaEm = Date.now();
  try {
    const [c, s] = await Promise.all([api("clima"), api("sugestao")]);
    climaDados = { c, s: s.sugestao };
  } catch (e) { climaDados = { erro: e.message }; }
  desenharClima();
}

async function aplicarSugestao(botao) {
  botao.disabled = true;
  try { await api("sugestao/aplicar", "POST"); aviso("Sugestão enviada ao ar"); setTimeout(atualizar, 3500); }
  catch (e) { aviso("Não deu: " + e.message); }
  botao.disabled = false;
}

function desenharClima() {
  const caixa = $("clima");
  if (!caixa) return;
  const d = climaDados;
  if (!d || d.erro || !d.c.local || !d.c.clima) {
    const msg = !d ? "Carregando…" : d.erro ? "Clima indisponível: " + d.erro : !d.c.local ? "Escolha a cidade nos Ajustes para ver a previsão e a sugestão para o ar."
      : "A Open-Meteo não respondeu agora.";
    trocarConteudo(caixa, el("div", { class: "cab" }, el("div", { class: "ico" }, icClima("sol-nuvem")),
      el("div", { class: "nomes" }, el("div", { class: "nome" }, "Clima"), el("div", { class: "det" }, msg))),
      d && !d.erro && !d.c.local ? el("div", { class: "corpo" }, el("button", { class: "b", on: { click: () => irPara("ajustes-clima") } }, "Escolher cidade")) : null);
    caixa.classList.toggle("oculto", !d);
    return;
  }
  const c = d.c.clima, ag = c.agora, s = d.s;
  const sug = s ? el("div", { class: "sugestao" },
    el("div", {}, el("b", {}, s.titulo), el("span", {}, s.texto)),
    s.cena ? el("button", { class: "b pri", on: { click: e => aplicarSugestao(e.currentTarget) } }, "Aplicar") : null) : null;
  trocarConteudo(caixa,
    el("div", { class: "cab" }, el("div", { class: "ico" }, icClima(ag.icone)),
      el("div", { class: "nomes" }, el("div", { class: "nome" }, d.c.local.nome), el("div", { class: "det" }, ag.texto + (c.velho ? " · dados antigos" : "")))),
    el("div", { class: "corpo" },
      el("div", { class: "clima-agora" }, el("b", {}, num(ag.temp, 0) + " °C"), el("span", {}, ag.umidade != null ? "umidade " + num(ag.umidade, 0) + "%" : "")),
      sug,
      el("div", { class: "clima-horas" }, c.horas.map(h => el("div", {}, el("small", {}, h.hora), icClima(h.icone), el("b", {}, num(h.temp, 0) + "°")))),
      el("div", { class: "clima-dias" }, c.dias.map((x, i) => el("div", {}, el("small", {}, i === 0 ? "hoje" : DIA_SEMANA[diaDe(x.dia).getDay()]), icClima(x.icone),
        el("b", {}, num(x.max, 0) + "°"), el("span", {}, num(x.min, 0) + "°"))))));
  caixa.classList.remove("oculto");
}

/** Cidade do clima: busca pelo nome na Open-Meteo (pelo servidor) e grava as coordenadas. */
function blocoLocal(d) {
  const achados = el("div", { class: "cidades" });
  const entrada = el("input", { placeholder: "Buscar cidade", style: "flex:1;min-width:0", "aria-label": "Buscar cidade" });
  let t = null;
  const buscar = async () => {
    const q = entrada.value.trim();
    if (q.length < 2) { achados.replaceChildren(); return; }
    try {
      const r = await api("cidades?q=" + encodeURIComponent(q));
      achados.replaceChildren(...(r.cidades.length ? r.cidades.map(c => el("button", { class: "b", type: "button",
        on: { click: async () => { await gravarPref({ local: c }); climaEm = 0; carregarClima(true); } } }, c.nome)) : [el("div", { class: "mu" }, "Nenhuma cidade com esse nome.")]));
    } catch (e) { achados.replaceChildren(el("div", { class: "mu" }, "Busca falhou: " + e.message)); }
  };
  entrada.addEventListener("input", () => { clearTimeout(t); t = setTimeout(buscar, 350); });
  return el("div", {},
    el("div", { class: "mu", style: "margin-bottom:8px" }, d.local ? "Agora: " + d.local.nome : "Nenhuma cidade escolhida. Sem ela o painel não mostra clima nem sugestão."),
    el("div", { class: "linha" }, entrada, d.local ? el("button", { class: "b", on: { click: async () => { await gravarPref({ local: null }); climaDados = null; desenharClima(); } } }, "Remover") : null),
    achados,
    el("div", { class: "mu", style: "margin-top:10px" }, "Previsão da Open-Meteo (gratuita). A sugestão usa o conforto adaptativo (ASHRAE 55): a temperatura ideal sobe e desce com a média dos últimos dias lá fora."));
}

// ---------------- ajustes ----------------

async function carregarPref() {
  try {
    const p = await api("preferencias");
    const mudou = !pref || p.versao !== pref.versao;
    pref = p;
    if (!mudou) return;
    if (abaAtual === "ajustes") desenharAjustes(); else desenharCartoes();
  } catch (e) {}
}

async function gravarPref(dados) {
  try { pref = await api("preferencias", "PUT", { dados, origem: "web" }); aviso("Salvo"); if (abaAtual === "ajustes") desenharAjustes(); else desenharCartoes(); }
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
  servidor: '<rect x="3" y="4" width="18" height="6" rx="1.5"/><rect x="3" y="14" width="18" height="6" rx="1.5"/><path d="M7 7h.01M7 17h.01"/>',
  seta: '<path d="m9 6 6 6-6 6"/>', voltar: '<path d="m15 6-6 6 6 6"/>'
};
const AJ_COR = { alertas: "#EB5A4B", ar: "#2D7DF6", consumo: "#F08A24", retorno: "#2EA043", cenas: "#7B61E0", clima: "#1FA3C7", servidor: "#7C808B" };
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

  const sim = b => b ? 1 : 0;
  const secoes = [
    ["alertas", "Alertas", sim(d.alertaFalha) + sim(d.alertaDesconectado) + sim(d.alertaQuente) + sim(d.alertaLavadoraFim) + " de 4 ligados" + (d.ntfyTopico ? " · no celular" : ""), alertas],
    ["ar", "Ar-condicionado", (d.semBipe ? "Sem bipe" : "Com bipe") + " · " + (ehF ? "°F" : "°C"), arCartao],
    ["consumo", "Consumo do ar", "Tarifa R$ " + String(d.tarifa).replace(".", ","), consumo],
    ["clima", "Clima e local", d.local ? d.local.nome : "Nenhuma cidade", clima],
    ["retorno", "Retorno das cenas", (({ 0: "Ventilar", 2: "Frio", 3: "Desumidificar", 5: "ECO" })[d.retornoModo] || "Manter") + " · " + d.retornoTemp + " °C", retorno],
    ["cenas", "Cenas", lista.length + " de 8", cenas],
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
    else if (abaAtual === "painel") { for (const r of nos.values()) r.sig = ""; desenharCartoes(); }
  }, 250);
});
if ("serviceWorker" in navigator && window.isSecureContext) navigator.serviceWorker.register("/sw.js").catch(() => {});
