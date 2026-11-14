// A interface nova do Mhiagos Control. Roda dentro do WebView2, numa origem
// que nao existe na rede, e so conversa com o app pela ponte (PonteDoApp.cs):
// pedidos {id, m, a} e respostas {id, ok, r | erro}; uma vez por segundo chega
// {ev:"estado", d}. A politica da pagina nao aceita script nem estilo embutido:
// tudo aqui monta a arvore pelo DOM e mexe em estilo so pelo CSSOM.
//
// O desenho segue a janela classica (SettingsForm): a mesma barra lateral, os
// mesmos cartoes, botoes, interruptores e segmentados, e os mesmos textos (T.*),
// que vem do app no idioma escolhido pelo pedido "textos".
(function () {
  "use strict";

  var S = null;            // o que "iniciar" devolveu: sensores, perfis, configuracao
  var E = null;            // o retrato de cada segundo
  var TX = {};             // os textos do app (T.*) no idioma de agora
  var atual = "";          // pagina a vista
  var vista = null;        // {atualizar(), sair()} da pagina a vista
  var editando = "";       // perfil selecionado no Painel CPU (o _current da classica)
  var aba = "paineis";     // aba do Painel CPU: paineis ou perfis
  var specs = null;        // especificacoes ja coletadas
  var janela = 600;        // segundos a vista nas Metricas
  var pendentes = {};
  var proximo = 1;
  var ponte = window.chrome && window.chrome.webview;

  // ---------------- ponte ----------------

  function pedir(m, a) {
    return new Promise(function (ok, falha) {
      if (!ponte) { falha(new Error("sem ponte")); return; }
      var id = proximo++;
      pendentes[id] = { ok: ok, falha: falha };
      ponte.postMessage({ id: id, m: m, a: a || {} });
    });
  }

  function avisar(m, a) { if (ponte) ponte.postMessage({ m: m, a: a || {} }); }

  function aoReceber(ev) {
    var d = ev.data;
    if (!d || typeof d !== "object") return;
    if (d.ev === "estado") {
      E = d.d;
      aoEstado();
      return;
    }
    if (d.ev === "ir") {
      // A bandeja, um aviso ou o widget pediram uma pagina com a janela ja aberta.
      if (S && MONTAR[d.p]) mostrar(d.p);
      return;
    }
    var p = pendentes[d.id];
    if (!p) return;
    delete pendentes[d.id];
    if (d.ok) p.ok(d.r); else p.falha(new Error(d.erro || "falha"));
  }

  // ---------------- utilitarios ----------------

  function t(pt, en) { return !S || S.pt ? pt : en; }
  /// Um texto do app (T.Nome); o reserva vale so enquanto os textos nao chegam.
  function T(k, reserva) { var v = TX[k]; return typeof v === "string" && v ? v : (reserva || ""); }
  /// Um texto com um nome dentro (T.BindGameTo(x)): o app manda o molde com {0}.
  function Tm(k, x) { return T(k).split("{0}").join(x); }
  function $(id) { return document.getElementById(id); }

  function el(tag, classe, texto) {
    var e = document.createElement(tag);
    if (classe) e.className = classe;
    if (texto !== undefined && texto !== null) e.textContent = texto;
    for (var i = 3; i < arguments.length; i++) if (arguments[i]) e.appendChild(arguments[i]);
    return e;
  }

  function limpar(e) { while (e.firstChild) e.removeChild(e.firstChild); }

  function glifo(cp, classe) { return el("span", "icone" + (classe ? " " + classe : ""), cp); }

  function svg(partes, caixa) {
    var ns = "http://www.w3.org/2000/svg";
    var s = document.createElementNS(ns, "svg");
    s.setAttribute("viewBox", caixa || "0 0 16 16");
    s.setAttribute("aria-hidden", "true");
    partes.forEach(function (p) {
      var e = document.createElementNS(ns, p[0]);
      for (var k in p[1]) if (Object.prototype.hasOwnProperty.call(p[1], k)) e.setAttribute(k, p[1][k]);
      s.appendChild(e);
    });
    return s;
  }

  var faixaTimer = 0;
  /// A faixa de aviso no alto do conteudo (FaixaDeAviso), com acao opcional.
  function faixa(texto, erro, acaoTexto, acao) {
    var f = $("faixa");
    clearTimeout(faixaTimer);
    if (!texto) { f.hidden = true; return; }
    $("faixa-msg").textContent = texto;
    var lugar = $("faixa-acao");
    limpar(lugar);
    if (acaoTexto && acao) lugar.appendChild(botao(acaoTexto, "b p", function () { f.hidden = true; acao(); }));
    f.className = erro ? "faixa erro" : "faixa";
    f.hidden = false;
    faixaTimer = setTimeout(function () { f.hidden = true; }, erro ? 10000 : 5000);
  }

  function aplicarTema() {
    document.documentElement.setAttribute("data-tema", S.escuro ? "escuro" : "claro");
    document.documentElement.lang = S.pt ? "pt-BR" : "en";
  }

  function buscarTextos() {
    return pedir("textos").then(function (r) { TX = r || {}; }, function () { });
  }

  /// Um pedido que muda a configuracao: a resposta e o "iniciar" novo.
  function mudar(m, a, feito) {
    return pedir(m, a).then(function (r) {
      if (r === null || r === undefined) return false;   // desistiu do dialogo
      if (typeof r === "string") { faixa(r); return r; }
      var idiomaAntes = S.pt;
      S = r;
      if (r.editar) editando = r.editar;
      aplicarTema();
      var depois = function () {
        faixa(r.aviso || feito || "");
        mostrar(atual, true);
      };
      if (idiomaAntes !== S.pt) return buscarTextos().then(function () { montarNavegacao(); depois(); return r; });
      depois();
      return r;
    }, function (e) {
      faixa(e.message, true);
      return false;
    });
  }

  function sensor(id) {
    if (!S || !id) return null;
    for (var i = 0; i < S.sensores.length; i++) if (S.sensores[i].id === id) return S.sensores[i];
    return null;
  }

  function perfil(nome) {
    for (var i = 0; S && i < S.perfis.length; i++) if (S.perfis[i].nome === nome) return S.perfis[i];
    return null;
  }

  function numero(v) {
    if (v === null || v === undefined || isNaN(v)) return "--";
    var a = Math.abs(v);
    var casas = a >= 100 || v === Math.round(v) ? 0 : 1;
    return v.toLocaleString(S && !S.pt ? "en-US" : "pt-BR", { minimumFractionDigits: casas, maximumFractionDigits: casas });
  }

  function leitura(id) {
    var v = E && E.v ? E.v[id] : undefined;
    return v === undefined ? null : v;
  }

  function comUnidade(id) {
    var s = sensor(id), v = leitura(id);
    if (v === null) return "--";
    return numero(v) + (s && s.unidade ? " " + s.unidade : "");
  }

  // ---------------- componentes da classica ----------------

  function botao(texto, classe, aoClicar) {
    var b = el("button", classe || "b", texto);
    b.type = "button";
    if (aoClicar) b.addEventListener("click", aoClicar);
    return b;
  }

  /// Card: titulo a 14 px, linha embaixo dele e o corpo.
  function cartao(titulo, classe) {
    var c = el("div", "cartao" + (classe ? " " + classe : ""));
    if (titulo) c.appendChild(el("div", "titulo", titulo));
    var corpo = el("div", "corpo");
    c.appendChild(corpo);
    c.corpo = corpo;
    return c;
  }

  /// O cartao das paginas que ainda nao passaram para o desenho da classica.
  function cartaoSimples(titulo, classe) {
    var c = el("div", "cartao simples" + (classe ? " " + classe : ""));
    if (titulo) c.appendChild(el("h2", "", titulo));
    return c;
  }

  function linha(k, v) {
    var valor = el("span", "v", v);
    var l = el("div", "linha", null, el("span", "k", k), valor);
    l.valor = valor;
    return l;
  }

  /// Toggle: trilho de 38 x 20 com o botao branco.
  function troca(ligado, aoMudar) {
    var b = el("button", "troca");
    b.type = "button";
    b.setAttribute("role", "switch");
    b.setAttribute("aria-checked", ligado ? "true" : "false");
    b.addEventListener("click", function () {
      var novo = b.getAttribute("aria-checked") !== "true";
      b.setAttribute("aria-checked", novo ? "true" : "false");
      if (aoMudar) aoMudar(novo, b);
    });
    b.definir = function (v) { b.setAttribute("aria-checked", v ? "true" : "false"); };
    return b;
  }

  /// O interruptor com rotulo ao lado, como o Toggle.Label da classica.
  function trocaComRotulo(texto, ligado, aoMudar) {
    var tg = troca(ligado, aoMudar);
    var r = el("span", "troca-rotulo", texto);
    r.addEventListener("click", function () { if (!tg.disabled) tg.click(); });
    var caixa = el("span", "troca-caixa", null, tg, r);
    caixa.troca = tg;
    return caixa;
  }

  function chave(titulo, nota, controle) {
    var texto = el("div", "", null, el("span", "", titulo));
    if (nota) texto.appendChild(el("small", "", nota));
    return el("div", "chave", null, texto, controle);
  }

  /// Segmented: pilula com os segmentos de mesma largura.
  function segmentos(opcoes, valor, aoMudar) {
    var s = el("div", "seg");
    s.setAttribute("role", "group");
    s.valor = valor;
    opcoes.forEach(function (o) {
      var b = el("button", "", o[1]);
      b.type = "button";
      b.setAttribute("aria-pressed", o[0] === valor ? "true" : "false");
      b.addEventListener("click", function () {
        if (s.valor === o[0]) return;
        s.valor = o[0];
        Array.prototype.forEach.call(s.children, function (x) { x.setAttribute("aria-pressed", x === b ? "true" : "false"); });
        if (aoMudar) aoMudar(o[0]);
      });
      s.appendChild(b);
    });
    return s;
  }

  /// NumberBox: menos, o numero e mais; muda ao sair do campo, no Enter ou nos botoes.
  function caixaNumero(valor, min, max, aoMudar) {
    var c = el("div", "num");
    var menos = botao("−", "", null), mais = botao("+", "", null);
    var i = el("input");
    i.type = "text"; i.inputMode = "numeric"; i.value = String(valor);
    i.setAttribute("aria-valuemin", String(min)); i.setAttribute("aria-valuemax", String(max));
    c.appendChild(menos); c.appendChild(i); c.appendChild(mais);
    c.valor = valor;
    function por(v) {
      v = Math.round(Number(v));
      if (isNaN(v)) v = c.valor;
      v = Math.max(min, Math.min(max, v));
      i.value = String(v);
      if (v === c.valor) return;
      c.valor = v;
      if (aoMudar) aoMudar(v);
    }
    menos.addEventListener("click", function () { por(c.valor - 1); });
    mais.addEventListener("click", function () { por(c.valor + 1); });
    i.addEventListener("change", function () { por(i.value); });
    i.addEventListener("keydown", function (e) {
      if (e.key === "Enter") por(i.value);
      else if (e.key === "ArrowUp") { e.preventDefault(); por(c.valor + 1); }
      else if (e.key === "ArrowDown") { e.preventDefault(); por(c.valor - 1); }
    });
    c.ligar = function (v) { c.classList.toggle("desligado", !v); i.disabled = !v; menos.disabled = !v; mais.disabled = !v; };
    return c;
  }

  function campo(rotulo, controle) {
    return el("label", "campo", null, el("span", "", rotulo), controle);
  }

  /// LinhaDeOpcao: glifo a esquerda, titulo e descricao, controles a direita.
  function opcao(cp, titulo, descricao, controles) {
    var desc = typeof descricao === "string" ? el("small", "", descricao) : descricao;
    var oc = el("div", "oc");
    (controles || []).forEach(function (c) { if (c) oc.appendChild(c); });
    var l = el("div", "opcao", null, el("span", "og icone", cp), el("div", "ot", null, el("b", "", titulo), desc), oc);
    l.desc = desc;
    return l;
  }

  /// O interruptor das linhas de configuracao: "Ligado"/"Desligado" a esquerda.
  function interruptor(ligado, aoMudar) {
    var estado = el("span", "estado");
    function rotular(v) { estado.textContent = v ? T("StateOn", t("Ligado", "On")) : T("StateOff", t("Desligado", "Off")); }
    var tg = troca(ligado, function (v, b) { rotular(v); aoMudar(v, b, rotular); });
    rotular(ligado);
    return [estado, tg];
  }

  function grupo(titulo) { return el("div", "grupo", titulo); }

  /// Uma lista com os sensores agrupados por categoria (Metricas).
  function seletorDeSensor(valor, vazio) {
    var s = el("select");
    if (vazio !== null) {
      var o = el("option", "", vazio);
      o.value = "";
      s.appendChild(o);
    }
    var grupos = {}, ordem = [];
    S.sensores.forEach(function (x) {
      var c = x.grupo || x.categoria || t("Outros", "Other");
      if (!grupos[c]) { grupos[c] = []; ordem.push(c); }
      grupos[c].push(x);
    });
    ordem.forEach(function (c) {
      var g = el("optgroup");
      g.label = c;
      grupos[c].forEach(function (x) {
        var o = el("option", "", x.rotulo);
        o.value = x.id;
        g.appendChild(o);
      });
      s.appendChild(g);
    });
    s.value = valor || "";
    if (valor && s.value !== valor) {
      var falta = el("option", "", t("(sensor ausente)", "(missing sensor)"));
      falta.value = valor;
      s.insertBefore(falta, s.firstChild);
      s.value = valor;
    }
    return s;
  }

  // ---------------- dialogos ----------------

  /// Uma janela por cima da pagina, no lugar das caixas de dialogo da classica.
  /// Esc cancela; Enter aciona o botao principal.
  function dialogo(titulo, classe, corpo, botoes) {
    return new Promise(function (resolver) {
      var antes = document.activeElement;
      var fundo = el("div", "fundo");
      var caixa = el("div", "dialogo" + (classe ? " " + classe : ""));
      caixa.setAttribute("role", "dialog");
      caixa.setAttribute("aria-modal", "true");
      if (titulo) caixa.appendChild(el("h2", "", titulo));
      if (corpo) caixa.appendChild(corpo);
      var pe = el("div", "pe");
      var principal = null;
      function fechar(v) {
        document.removeEventListener("keydown", tecla, true);
        if (fundo.parentNode) fundo.parentNode.removeChild(fundo);
        if (antes && antes.focus) try { antes.focus(); } catch (e) { }
        resolver(v);
      }
      botoes.forEach(function (b) {
        var x = botao(b[0], b[2] || "b", function () { fechar(typeof b[1] === "function" ? b[1]() : b[1]); });
        if (b[2] && b[2].indexOf(" p") >= 0) principal = x;
        pe.appendChild(x);
      });
      caixa.appendChild(pe);
      fundo.appendChild(caixa);
      fundo.addEventListener("mousedown", function (e) { if (e.target === fundo) fechar(null); });
      function tecla(e) {
        if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); fechar(null); }
        else if (e.key === "Enter" && principal && !principal.disabled && e.target.tagName !== "BUTTON" && e.target.tagName !== "TEXTAREA") {
          e.preventDefault(); e.stopPropagation(); principal.click();
        }
      }
      document.addEventListener("keydown", tecla, true);
      caixa.fechar = fechar;
      caixa.principal = principal;
      document.body.appendChild(fundo);
      var foco = caixa.querySelector("input") || principal || caixa.querySelector("button");
      if (foco) foco.focus();
      if (caixa.aoAbrir) caixa.aoAbrir();
      dialogo.ultima = caixa;
    });
  }

  function perguntarNome(titulo, valor) {
    var i = el("input");
    i.type = "text"; i.maxLength = 40; i.value = valor || "";
    var p = dialogo(titulo, "", i, [[T("Cancel", "Cancelar"), null], [T("Ok", "OK"), function () { return i.value.trim() || null; }, "b p"]]);
    i.select();
    return p;
  }

  function confirmar(texto, sim, classe) {
    return dialogo(null, "", el("p", "", texto), [[T("Cancel", "Cancelar"), false], [sim, true, classe || "b p"]]);
  }

  /// TextReviewDialog: o texto para conferir antes de seguir.
  function revisar(titulo, texto, acao) {
    var pre = el("pre", "", texto);
    return dialogo(titulo, "texto", pre, [[T("Cancel", "Cancelar"), false], [acao, true, "b p"]]);
  }

  /// SensorDialog: busca e lista agrupada por categoria; duplo clique usa.
  function escolherSensor(titulo, atualId) {
    var escolhido = atualId || "";
    var busca = el("input");
    busca.type = "search";
    busca.placeholder = T("SearchSensor", t("Buscar sensor...", "Search sensor..."));
    var lista = el("div", "lista-sensores");
    lista.setAttribute("role", "listbox");
    var c = cartao(null);
    c.corpo.appendChild(busca);
    c.corpo.appendChild(lista);
    var linhas = [];
    function desenhar() {
      limpar(lista);
      linhas = [];
      var q = busca.value.trim().toLowerCase();
      var grupos = {}, ordem = [];
      S.sensores.forEach(function (x) {
        var txt = (x.rotulo + " " + x.nome + " " + x.hardware + " " + (x.grupo || "")).toLowerCase();
        if (q && txt.indexOf(q) < 0) return;
        var g = x.grupo || x.categoria || t("Outros", "Other");
        if (!grupos[g]) { grupos[g] = []; ordem.push(g); }
        grupos[g].push(x);
      });
      ordem.forEach(function (g) {
        lista.appendChild(el("div", "cat", g));
        grupos[g].forEach(function (x) {
          var r = el("div", "sn" + (x.id === escolhido ? " sel" : ""), null, el("span", "", x.rotulo), el("span", "", comUnidade(x.id)));
          r.setAttribute("role", "option");
          r.title = x.hardware;
          r.addEventListener("click", function () { escolhido = x.id; marcar(); });
          r.addEventListener("dblclick", function () { escolhido = x.id; if (dialogo.ultima && dialogo.ultima.principal) dialogo.ultima.principal.click(); });
          r.idSensor = x.id;
          linhas.push(r);
          lista.appendChild(r);
        });
      });
      if (!linhas.length) lista.appendChild(el("div", "vazio", t("Nenhum sensor encontrado.", "No sensor found.")));
    }
    function marcar() {
      linhas.forEach(function (r) { r.classList.toggle("sel", r.idSensor === escolhido); });
      var sel = null;
      linhas.forEach(function (r) { if (r.idSensor === escolhido) sel = r; });
      if (sel && sel.scrollIntoView) sel.scrollIntoView({ block: "nearest" });
    }
    busca.addEventListener("input", desenhar);
    busca.addEventListener("keydown", function (e) {
      if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
      e.preventDefault();
      var i = -1;
      linhas.forEach(function (r, k) { if (r.idSensor === escolhido) i = k; });
      i = Math.max(0, Math.min(linhas.length - 1, i + (e.key === "ArrowDown" ? 1 : -1)));
      if (linhas[i]) { escolhido = linhas[i].idSensor; marcar(); }
    });
    desenhar();
    var p = dialogo(titulo, "largo", c, [[T("Cancel", "Cancelar"), null], [T("Use", "Usar"), function () { return escolhido || null; }, "b p"]]);
    marcar();
    return p;
  }

  // ---------------- navegacao (NavBar) ----------------

  var LEQUE = [["rect", { x: "1.5", y: "1.5", width: "13", height: "13", rx: "3" }],
    ["circle", { cx: "8", cy: "8", r: "1.2" }],
    ["path", { d: "M8 6.8C7.4 4.8 8 3.4 9.6 3.6c1.4.3 1 2-1.6 3.2" }],
    ["path", { d: "M9.2 8c2-.6 3.4 0 3.2 1.6-.3 1.4-2 1-3.2-1.6" }],
    ["path", { d: "M8 9.2c.6 2 0 3.4-1.6 3.2-1.4-.3-1-2 1.6-3.2" }],
    ["path", { d: "M6.8 8c-2 .6-3.4 0-3.2-1.6.3-1.4 2-1 3.2 1.6" }]];
  var PILHA = [["rect", { x: "1", y: "4.5", width: "12.5", height: "7", rx: "1.6" }],
    ["path", { d: "M15 7v2" }], ["rect", { x: "3", y: "6.5", width: "3.4", height: "3", rx: ".5", fill: "currentColor", stroke: "none" }]];

  /// As chaves da classica (cfg.NavOrder) e a pagina de cada uma.
  var BARRA = {
    visao: ["visao", "NavOverview", "Visão geral", "Overview", ""],
    paineis: ["painel", "NavPanels", "Painel CPU", "CPU Panel", LEQUE],
    metricas: ["metricas", "NavMetrics", "Métricas", "Metrics", ""],
    specs: ["specs", "NavSpecs", "Especificações", "Specifications", ""],
    config: ["config", "NavSettings", "Configurações", "Settings", ""],
    baterias: ["baterias", "NavBatteries", "Baterias", "Batteries", PILHA],
    casa: ["casa", "NavCasa", "Casa", "Home", ""],
    sobre: ["sobre", "NavAbout", "Sobre", "About", ""]
  };
  var PADRAO = ["visao", "paineis", "metricas", "specs", "config", "baterias", "casa", "sobre"];

  function ordemDaBarra() {
    var r = [];
    ((S.barra && S.barra.ordem) || "").split(",").forEach(function (k) {
      k = k.trim();
      if (BARRA[k] && r.indexOf(k) < 0) r.push(k);
    });
    PADRAO.forEach(function (k) { if (r.indexOf(k) < 0) r.push(k); });
    return r;
  }

  function montarNavegacao() {
    var itens = $("itens");
    limpar(itens);
    ordemDaBarra().forEach(function (k) {
      var d = BARRA[k];
      var g = el("span", "gl");
      if (typeof d[4] === "string") { g.className = "gl icone"; g.textContent = d[4]; }
      else g.appendChild(svg(d[4]));
      var texto = T(d[1], t(d[2], d[3]));
      var b = el("button", "item", null, g, el("span", "tx", texto));
      b.type = "button";
      b.title = texto;
      b.setAttribute("role", "tab");
      b.dataset.pagina = d[0];
      b.dataset.chave = k;
      b.addEventListener("click", function () { if (!arrasto.houve) mostrar(d[0]); });
      b.addEventListener("pointerdown", function (e) { comecarArrasto(e, b); });
      itens.appendChild(b);
    });
    $("perfil-rotulo").textContent = T("ActiveProfile", t("PERFIL ATIVO", "ACTIVE PROFILE"));
    $("nav").classList.toggle("recolhida", !!(S.barra && S.barra.recolhida));
    $("recolher").setAttribute("aria-label", t("Recolher a barra (Ctrl+B)", "Collapse the sidebar (Ctrl+B)"));
    marcarNavegacao();
  }

  function marcarNavegacao() {
    Array.prototype.forEach.call($("itens").children, function (b) {
      b.setAttribute("aria-selected", b.dataset.pagina === atual ? "true" : "false");
    });
    $("perfil-ativo").textContent = S.ativo;
  }

  function recolher() {
    var nav = $("nav");
    var v = !nav.classList.contains("recolhida");
    nav.classList.toggle("recolhida", v);
    if (S.barra) S.barra.recolhida = v;
    pedir("barra", { recolhida: v }).catch(function () { });
  }

  /// Arrastar um item muda a ordem da barra, como na classica.
  var arrasto = { b: null, y0: 0, ativo: false, houve: false, destino: -1 };
  function comecarArrasto(e, b) {
    if (e.button !== 0) return;
    arrasto = { b: b, y0: e.clientY, ativo: false, houve: false, destino: -1 };
    document.addEventListener("pointermove", moverArrasto);
    document.addEventListener("pointerup", soltarArrasto);
  }
  function moverArrasto(e) {
    if (!arrasto.b) return;
    if (!arrasto.ativo && Math.abs(e.clientY - arrasto.y0) < 6) return;
    arrasto.ativo = true;
    arrasto.b.classList.add("arrastando");
    var itens = Array.prototype.slice.call($("itens").children);
    var destino = itens.length;
    for (var i = 0; i < itens.length; i++) {
      var r = itens[i].getBoundingClientRect();
      if (e.clientY < r.top + r.height / 2) { destino = i; break; }
    }
    arrasto.destino = destino;
    itens.forEach(function (x, k) {
      x.classList.toggle("alvo-antes", k === destino);
      x.classList.toggle("alvo-depois", destino === itens.length && k === itens.length - 1);
    });
  }
  function soltarArrasto() {
    document.removeEventListener("pointermove", moverArrasto);
    document.removeEventListener("pointerup", soltarArrasto);
    var a = arrasto;
    if (!a.b) return;
    var itens = Array.prototype.slice.call($("itens").children);
    itens.forEach(function (x) { x.classList.remove("arrastando", "alvo-antes", "alvo-depois"); });
    if (!a.ativo) { arrasto.b = null; return; }
    arrasto.houve = true;
    setTimeout(function () { arrasto.houve = false; }, 0);
    var ordem = itens.map(function (x) { return x.dataset.chave; });
    var de = ordem.indexOf(a.b.dataset.chave);
    var para = a.destino > de ? a.destino - 1 : a.destino;
    if (para === de || para < 0) return;
    ordem.splice(de, 1);
    ordem.splice(para, 0, a.b.dataset.chave);
    S.barra.ordem = ordem.join(",");
    montarNavegacao();
    pedir("barra", { ordem: S.barra.ordem }).catch(function (e) { faixa(e.message, true); });
  }

  var MONTAR = {};

  function mostrar(pagina, mesma) {
    if (!MONTAR[pagina]) pagina = "visao";
    if (vista && vista.sair) vista.sair();
    vista = null;
    var mudou = pagina !== atual;
    atual = pagina;
    try { sessionStorage.setItem("pagina", pagina); } catch (e) { }
    marcarNavegacao();
    var alvo = $("pagina");
    var rolagem = mesma ? alvo.scrollTop : 0;
    limpar(alvo);
    alvo.className = pagina === "casa" ? "casa" : "";
    if (mudou && !mesma) faixa("");
    if (pagina !== "casa") avisar("casa.area", { visivel: false, x: 0, y: 0, w: 0, h: 0 });
    var dentro = pagina === "casa" ? alvo : el("div", "largura");
    if (dentro !== alvo) alvo.appendChild(dentro);
    vista = MONTAR[pagina](dentro) || {};
    alvo.scrollTop = rolagem;
    if (E && vista.atualizar) vista.atualizar();
  }

  function aoEstado() {
    if (!S) return;
    if (E.ativo && E.ativo !== S.ativo) {
      // O perfil mudou por fora (menu da bandeja, rodizio, jogo): a pagina acompanha.
      S.ativo = E.ativo;
      $("perfil-ativo").textContent = S.ativo;
      if (atual === "painel") { mostrar("painel", true); return; }
    }
    if (vista && vista.atualizar) vista.atualizar();
  }

  // ---------------- previa do cooler (PanelPreview) ----------------

  var DIGITOS = [
    [1, 1, 1, 1, 1, 1, 0], [0, 1, 1, 0, 0, 0, 0], [1, 1, 0, 1, 1, 0, 1], [1, 1, 1, 1, 0, 0, 1], [0, 1, 1, 0, 0, 1, 1],
    [1, 0, 1, 1, 0, 1, 1], [1, 0, 1, 1, 1, 1, 1], [1, 1, 1, 0, 0, 0, 0], [1, 1, 1, 1, 1, 1, 1], [1, 1, 1, 1, 0, 1, 1]
  ];
  var SEG_ON = "rgb(240,245,252)", SEG_OFF = "rgba(105,116,132,0.165)", SEG_BRILHO = "rgba(240,245,252,0.133)";
  var cooler = new Image();
  var fundoDoCooler = "rgb(16,17,20)";
  var previas = [];
  cooler.addEventListener("load", function () {
    try {
      var c = document.createElement("canvas");
      c.width = 4; c.height = 4;
      var g = c.getContext("2d");
      g.drawImage(cooler, -2, -2);
      var p = g.getImageData(0, 0, 1, 1).data;
      fundoDoCooler = "rgb(" + p[0] + "," + p[1] + "," + p[2] + ")";
    } catch (e) { }
    previas.forEach(function (p) { p.desenhar(); });
  });
  cooler.src = "cooler.png";

  function horizontal(x1, x2, y, t, gap) {
    var h = t / 2; x1 += gap; x2 -= gap;
    return [[x1 + h, y - h], [x2 - h, y - h], [x2, y], [x2 - h, y + h], [x1 + h, y + h], [x1, y]];
  }
  function vertical(x, y1, y2, t, gap) {
    var h = t / 2; y1 += gap; y2 -= gap;
    return [[x - h, y1 + h], [x, y1], [x + h, y1 + h], [x + h, y2 - h], [x, y2], [x - h, y2 - h]];
  }
  function poligono(g, pts) {
    g.beginPath();
    g.moveTo(pts[0][0], pts[0][1]);
    for (var i = 1; i < pts.length; i++) g.lineTo(pts[i][0], pts[i][1]);
    g.closePath();
  }
  function digito(g, x, y, w, h, t, gap, d, aceso) {
    var seg = DIGITOS[Math.min(Math.max(d, 0), 9)], meio = y + h / 2;
    for (var i = 0; i < 7; i++) {
      var on = aceso && seg[i], p;
      switch (i) {
        case 0: p = horizontal(x, x + w, y, t, gap); break;
        case 1: p = vertical(x + w, y, meio, t, gap); break;
        case 2: p = vertical(x + w, meio, y + h, t, gap); break;
        case 3: p = horizontal(x, x + w, y + h, t, gap); break;
        case 4: p = vertical(x, meio, y + h, t, gap); break;
        case 5: p = vertical(x, y, meio, t, gap); break;
        default: p = horizontal(x, x + w, meio, t, gap); break;
      }
      poligono(g, p);
      if (on) { g.strokeStyle = SEG_BRILHO; g.lineWidth = t * 1.6; g.lineJoin = "round"; g.stroke(); }
      g.fillStyle = on ? SEG_ON : SEG_OFF;
      g.fill();
    }
  }
  function arredondado(g, x, y, w, h, r) {
    g.beginPath();
    g.moveTo(x + r, y); g.arcTo(x + w, y, x + w, y + h, r); g.arcTo(x + w, y + h, x, y + h, r);
    g.arcTo(x, y + h, x, y, r); g.arcTo(x, y, x + w, y, r); g.closePath();
  }
  function mostradorDoCooler(g, ax, ay, aw, ah, valor, cima, baixo, cimaAceso, seloAcima) {
    var vazio = valor === null || valor === undefined;
    var excede = !vazio && (valor > 999 || valor < 0);
    var mostrado = vazio ? 0 : Math.min(Math.max(valor, 0), 999);
    var dh = ah * 0.68, esp = Math.max(1.6, dh / 14), gap = esp * 0.4, topo = ay + (ah - dh) / 2;
    var fonte = Math.max(5.5, dh * 0.30) * 4 / 3;
    g.font = "bold " + fonte + "px 'Segoe UI', sans-serif";
    g.textBaseline = "top";
    var uw = Math.max(g.measureText(cima).width, g.measureText(baixo).width) + fonte * 0.35 + 3;
    var cel = (aw - uw) / 3, dw = cel * 0.54, esq = ax + (cel - dw) / 2;
    var c = Math.floor(mostrado / 100), d = Math.floor(mostrado / 10) % 10, u = mostrado % 10;
    digito(g, esq, topo, dw, dh, esp, gap, c, !vazio && c > 0);
    digito(g, esq + cel, topo, dw, dh, esp, gap, d, !vazio && mostrado >= 10);
    digito(g, esq + cel * 2, topo, dw, dh, esp, gap, u, !vazio);
    var x = ax + aw - uw + 1 + fonte * 0.17;
    g.fillStyle = !vazio && cimaAceso ? SEG_ON : SEG_OFF;
    g.fillText(cima, x, topo - 1 + fonte * 0.12);
    g.fillStyle = !vazio && !cimaAceso ? SEG_ON : SEG_OFF;
    g.textBaseline = "bottom";
    g.fillText(baixo, x, topo + dh + 1 - fonte * 0.12);
    var selo = null, cor = null;
    if (vazio) { selo = T("BadgeNoReading", t("sem leitura", "no reading")); cor = "rgb(165,170,180)"; }
    else if (excede) { selo = T("BadgeOver999", t("excede 999", "over 999")); cor = "#F0AF3C"; }
    if (!selo) return;
    var rx = Math.round(ax - 5), ry = Math.round(ay - 4), rw = Math.round(aw + 10), rh = Math.round(ah + 8);
    g.strokeStyle = cor; g.lineWidth = 1.4;
    arredondado(g, rx, ry, rw, rh, 6); g.stroke();
    g.font = "600 11px 'Segoe UI Variable Small','Segoe UI',sans-serif";
    g.textBaseline = "top";
    var tw = g.measureText(selo).width, th = 15;
    var ty = seloAcima ? ry - th - 7 : ry + rh + 5;
    g.fillStyle = "rgba(15,16,20,0.88)";
    arredondado(g, ax + 4, ty, tw + 10, th + 2, 4); g.fill();
    g.fillStyle = cor;
    g.fillText(selo, ax + 9, ty + 2);
  }

  /// A previa do display: a foto do cooler com os dois mostradores de 7 segmentos.
  function previaDoCooler() {
    var caixa = el("div", "previa");
    var canvas = el("canvas");
    caixa.appendChild(canvas);
    var estado = { v1: null, v2: null, f: false, p: true };
    var p = {
      el: caixa,
      definir: function (v1, v2, fahrenheit, percent) {
        if (estado.v1 === v1 && estado.v2 === v2 && estado.f === fahrenheit && estado.p === percent && canvas.width) return;
        estado = { v1: v1, v2: v2, f: fahrenheit, p: percent };
        p.desenhar();
      },
      desenhar: function () {
        var r = caixa.getBoundingClientRect();
        var dpr = window.devicePixelRatio || 1;
        var W = Math.max(1, Math.round(r.width)), H = Math.max(1, Math.round(r.height));
        if (W < 40 || H < 30) return;
        if (canvas.width !== W * dpr) canvas.width = W * dpr;
        if (canvas.height !== H * dpr) canvas.height = H * dpr;
        var g = canvas.getContext("2d");
        g.setTransform(dpr, 0, 0, dpr, 0, 0);
        g.fillStyle = fundoDoCooler;
        g.fillRect(0, 0, W, H);
        var placa;
        if (cooler.complete && cooler.naturalWidth > 0) {
          var razao = cooler.naturalWidth / cooler.naturalHeight;
          var h = H, w = Math.round(h * razao);
          if (w > W) { w = W; h = Math.round(w / razao); }
          var ix = Math.floor((W - w) / 2), iy = Math.floor((H - h) / 2);
          g.imageSmoothingQuality = "high";
          g.drawImage(cooler, ix, iy, w, h);
          placa = [ix + w * 0.075, iy + h * 0.190, w * (0.765 - 0.075), h * (0.815 - 0.190)];
        } else placa = [W * 0.1, H * 0.1, W * 0.8, H * 0.8];
        var dw = placa[2] * 0.62, dh = dw * 0.43, vg = dh * 0.18, total = dh * 2 + vg;
        var x = placa[0] + (placa[2] - dw) / 2, y = placa[1] + (placa[3] - total) / 2;
        mostradorDoCooler(g, x, y, dw, dh, estado.v1, "°C", "°F", !estado.f, true);
        mostradorDoCooler(g, x, y + dh + vg, dw, dh, estado.v2, "%", "W", estado.p, false);
      }
    };
    previas.push(p);
    var obs = window.ResizeObserver ? new ResizeObserver(function () { p.desenhar(); }) : null;
    if (obs) obs.observe(caixa);
    p.sair = function () {
      if (obs) obs.disconnect();
      var i = previas.indexOf(p);
      if (i >= 0) previas.splice(i, 1);
    };
    return p;
  }

  // ---------------- o valor que vai ao mostrador (Scaling) ----------------

  function sugerido(tipo) {
    return tipo === "Clock" || tipo === "Fan" || tipo === "Frequency" ? 10 : tipo === "Throughput" ? 1000 : 1;
  }
  function divisorEfetivo(configurado, s) {
    if (configurado > 0) return configurado;
    if (s && s.tipo === "Throughput" && s.unidade && s.unidade.toLowerCase() !== "b/s") return 1;
    return s ? sugerido(s.tipo) : 1;
  }
  function valorDoPainel(id, divisor, fahrenheit) {
    var s = sensor(id), v = leitura(id);
    if (!s || v === null) return null;
    if (fahrenheit && s.tipo === "Temperature") v = v * 9 / 5 + 32;
    var d = divisorEfetivo(divisor, s);
    if (d > 1) v /= d;
    v = Math.round(v);
    return Math.max(0, Math.min(999, v));
  }
  function previaDoPerfil(pv, p) {
    if (!p) { pv.definir(null, null, false, true); return; }
    pv.definir(valorDoPainel(p.p1, p.div1, p.fahrenheit), valorDoPainel(p.p2, p.div2, false), !!p.fahrenheit, !!p.percent);
  }

  // ---------------- Visao geral ----------------

  function nomeDoSensor(id) {
    var s = sensor(id);
    return s ? s.rotulo : id ? t("sensor ausente", "missing sensor") : t("nenhum", "none");
  }

  /// "SÁB 14/11 · 19:05", como Visao.Data.
  function dataDeAgora() {
    var d = new Date(), loc = S.pt ? "pt-BR" : "en-US";
    var dia = d.toLocaleDateString(loc, { weekday: "short" }).replace(".", "").toUpperCase();
    var dm = S.pt ? ("0" + d.getDate()).slice(-2) + "/" + ("0" + (d.getMonth() + 1)).slice(-2)
                  : ("0" + (d.getMonth() + 1)).slice(-2) + "/" + ("0" + d.getDate()).slice(-2);
    return dia + " " + dm + " · " + ("0" + d.getHours()).slice(-2) + ":" + ("0" + d.getMinutes()).slice(-2);
  }

  function corDoValor(unidade, v, atencao, perigo) {
    if (v !== null && perigo !== null && perigo !== undefined && v >= perigo) return "var(--danger)";
    if (v !== null && atencao !== null && atencao !== undefined && v >= atencao) return "var(--warn)";
    return unidade === "°C" || unidade === "°F" ? "var(--quente)" : "var(--accent-text)";
  }

  /// A curva de fundo de um bloco: area sob a linha e a media tracejada, como o MetricCard.
  function desenharCurva(canvas, pontos, cor) {
    var r = canvas.getBoundingClientRect(), dpr = window.devicePixelRatio || 1;
    var w = Math.max(10, Math.round(r.width * dpr)), h = Math.max(10, Math.round(r.height * dpr));
    if (canvas.width !== w) canvas.width = w;
    if (canvas.height !== h) canvas.height = h;
    var g = canvas.getContext("2d");
    g.clearRect(0, 0, w, h);
    var vals = (pontos || []).filter(function (v) { return v !== null; });
    if (vals.length < 2) return null;
    var min = Math.min.apply(null, vals), max = Math.max.apply(null, vals);
    var soma = 0; vals.forEach(function (v) { soma += v; });
    var media = soma / vals.length;
    var topo = h * 0.55, base = h - 2 * dpr;
    var folga = (max - min) * 0.1 || Math.max(1, Math.abs(max) * 0.05);
    var lo = min - folga, hi = max + folga, n = pontos.length;
    function x(k) { return k * (w - 1) / (n - 1); }
    function y(v) { return base - (v - lo) / (hi - lo) * (base - topo); }
    var cheio = g.createLinearGradient(0, topo, 0, base);
    cheio.addColorStop(0, cor + "55"); cheio.addColorStop(1, cor + "11");
    g.beginPath();
    var aberto = false, ultimo = 0, primeiro = 0;
    for (var i = 0; i < n; i++) {
      if (pontos[i] === null) continue;
      if (!aberto) { g.moveTo(x(i), base); g.lineTo(x(i), y(pontos[i])); aberto = true; primeiro = i; }
      else g.lineTo(x(i), y(pontos[i]));
      ultimo = i;
    }
    g.lineTo(x(ultimo), base); g.closePath();
    g.fillStyle = cheio; g.fill();
    g.beginPath();
    var solto = true;
    for (i = 0; i < n; i++) {
      if (pontos[i] === null) { solto = true; continue; }
      if (solto) g.moveTo(x(i), y(pontos[i])); else g.lineTo(x(i), y(pontos[i]));
      solto = false;
    }
    g.strokeStyle = cor; g.lineWidth = 1.2 * dpr; g.lineJoin = "round"; g.stroke();
    g.beginPath();
    g.setLineDash([4 * dpr, 4 * dpr]);
    g.moveTo(0, y(media)); g.lineTo(w, y(media));
    g.strokeStyle = cor + "99"; g.lineWidth = dpr; g.stroke();
    g.setLineDash([]);
    return { max: max, media: media };
  }

  function mm(f) { return f ? t("máx ", "max ") + numero(f.max) + " · " + t("méd ", "avg ") + numero(f.media) : ""; }

  MONTAR.visao = function (alvo) {
    var vivo = true, serie = {}, previa = previaDoCooler();
    var dataEl = el("div", "vg-data"), fraseEl = el("div", "vg-frase");
    alvo.appendChild(el("div", "vg-cab", null, dataEl, fraseEl));

    // Os destaques: temperatura e uso das pecas que aquecem, e a memoria.
    var tiles = el("div", "vg-tiles");
    var blocos = [];
    (S.destaques || []).forEach(function (d) {
      var canvas = el("canvas"), valor = el("div", "vv"), faixa = el("div", "mm");
      var c = el("div", "cartao vg-tile", null, canvas, el("div", "tt", d.titulo), valor, faixa);
      c.title = d.sub;
      c.addEventListener("click", function () { mostrar("metricas"); });
      tiles.appendChild(c);
      blocos.push({ id: d.id, d: d, canvas: canvas, valor: valor, faixa: faixa, cor: "" });
    });
    alvo.appendChild(tiles);

    var meio = el("div", "vg-meio"), esq = el("div", "vg-esq");
    // A casa: convite ou o resumo dos aparelhos.
    var casaTexto = el("div", "ct");
    var casa = el("div", "cartao vg-casa", null, el("span", "ic icone", ""), casaTexto,
      botao(T("VisaoAbrirCasa", t("Abrir Casa ▸", "Open Home ▸")), "link", function () { mostrar("casa"); }));
    esq.appendChild(casa);
    var duas = el("div", "vg-duas");
    var perLista = el("div");
    var per = el("div", "cartao vg-box", null, el("div", "cb", null, el("b", "", T("VisaoChipPerifericos", t("Periféricos", "Peripherals"))),
      botao(T("VisaoBaterias", t("Baterias ▸", "Batteries ▸")), "link", function () { mostrar("baterias"); })), perLista);
    var evLista = el("div", "vg-ev");
    var ev = el("div", "cartao vg-box", null, el("div", "cb", null, el("span", "mono", T("VisaoEventos", t("EVENTOS", "EVENTS")))), evLista);
    duas.appendChild(per); duas.appendChild(ev);
    esq.appendChild(duas);
    meio.appendChild(esq);

    // O display do cooler: a previa, o perfil e as curvas dos dois mostradores.
    var stEl = el("span", "st"), perfilEl = el("div", "val"), painelEl = el("div", "val");
    var cima = el("div", "cima", null, previa.el, el("div", "info", null,
      el("div", "cap", T("ActiveProfile", t("PERFIL ATIVO", "ACTIVE PROFILE"))), perfilEl,
      el("div", "cap", T("PanelLabel", t("Painel", "Panel"))), painelEl,
      botao(T("VisaoAbrirPaineis", t("Abrir Painéis ▸", "Open Panels ▸")), "link", function () { mostrar("painel"); })));
    var cooler = el("div", "cartao vg-cooler", null, el("div", "titulo", null, el("span", "", T("VisaoChipCooler", t("Display do cooler", "Cooler display"))), stEl), cima);
    var mostradores = [1, 2].map(function (n) {
      var canvas = el("canvas"), tt = el("div", "tt"), valor = el("div", "vv"), faixa = el("div", "mm");
      var c = el("div", "vg-mostrador", null, canvas, tt, valor, faixa);
      c.addEventListener("click", function () { mostrar("painel"); });
      cooler.appendChild(c);
      return { n: n, canvas: canvas, tt: tt, valor: valor, faixa: faixa, id: "" };
    });
    meio.appendChild(cooler);
    alvo.appendChild(meio);

    // A tira de especificacoes.
    var tira = el("div", "cartao vg-specs");
    alvo.appendChild(tira);
    if (S.sistema) desenharTira(tira, S.sistema);
    else pedir("visao.sistema").then(function (l) { S.sistema = l || []; if (vivo) desenharTira(tira, S.sistema); }, function () { });

    function desenharTira(onde, linhas) {
      limpar(onde);
      linhas.forEach(function (l) { onde.appendChild(el("div", "", null, el("small", "", l[0]), el("b", "", l[1]))); });
    }

    function pintar() {
      blocos.forEach(function (b) {
        b.f = desenharCurva(b.canvas, serie[b.id], corDoValor(b.d.unidade, leitura(b.id), b.d.atencao, b.d.perigo).replace("var(--danger)", "#EB5A4B").replace("var(--warn)", "#F0AF3C").replace("var(--quente)", cor("--quente")).replace("var(--accent-text)", cor("--accent-text")));
        b.faixa.textContent = mm(b.f);
      });
      mostradores.forEach(function (m) {
        var s = sensor(m.id), c = m.n === 1 ? cor("--quente") : cor("--accent-text");
        var f = desenharCurva(m.canvas, serie[m.id], c);
        m.faixa.textContent = mm(f);
      });
    }

    var ids = [];
    function buscar() {
      var quais = ids.slice();
      if (!quais.length) return;
      pedir("metricas", { ids: quais, segundos: 300 }).then(function (r) {
        if (!vivo) return;
        for (var k in r.series) if (Object.prototype.hasOwnProperty.call(r.series, k)) serie[k] = r.series[k];
        pintar();
      }, function () { });
    }
    function conferirIds() {
      var p = perfil(E ? E.painel.perfil : S.ativo) || perfil(S.ativo);
      var novos = (S.destaques || []).map(function (d) { return d.id; });
      mostradores[0].id = p ? p.p1 : ""; mostradores[1].id = p ? p.p2 : "";
      novos.push(mostradores[0].id, mostradores[1].id);
      novos = novos.filter(function (x, i) { return x && novos.indexOf(x) === i; });
      if (novos.join("|") !== ids.join("|")) { ids = novos; buscar(); }
    }
    var relogio = setInterval(buscar, (S.passo || 5) * 1000);
    var redesenhar = function () { pintar(); previa.desenhar(); };
    window.addEventListener("resize", redesenhar);

    return {
      atualizar: function () {
        dataEl.textContent = dataDeAgora();
        fraseEl.textContent = E.frase || "";
        fraseEl.className = "vg-frase" + (E.alerta ? " alerta" : "");
        blocos.forEach(function (b) {
          var v = leitura(b.id);
          limpar(b.valor);
          b.valor.style.color = v === null ? "var(--faint)" : corDoValor(b.d.unidade, v, b.d.atencao, b.d.perigo);
          b.valor.appendChild(document.createTextNode(v === null ? "--" : v.toLocaleString(S.pt ? "pt-BR" : "en-US", { minimumFractionDigits: 1, maximumFractionDigits: 1 })));
          if (v !== null && b.d.unidade) b.valor.appendChild(el("small", "", b.d.unidade));
        });
        var p = E.painel;
        stEl.textContent = p.conectado ? T("VisaoConectado", t("conectado", "connected")) : T("VisaoDesconectado", t("desconectado", "disconnected"));
        stEl.className = "st" + (p.conectado ? " ok" : "");
        perfilEl.textContent = p.perfil;
        painelEl.textContent = S.sobre && S.sobre.painel ? S.sobre.painel : T("PanelNone", t("nenhum encontrado", "none found"));
        previa.definir(p.p1, p.p2, !!p.fahrenheit, !!p.percent);
        conferirIds();
        mostradores.forEach(function (m, i) {
          var v = leitura(m.id), s = sensor(m.id);
          m.tt.textContent = (i === 0 ? t("mostrador de cima", "top display") : t("mostrador de baixo", "bottom display")) + " · " + (s ? s.nome : "");
          limpar(m.valor);
          m.valor.style.color = i === 0 ? "var(--quente)" : "var(--accent-text)";
          m.valor.appendChild(document.createTextNode(v === null ? "--" : v.toLocaleString(S.pt ? "pt-BR" : "en-US", { minimumFractionDigits: 1, maximumFractionDigits: 1 })));
          if (v !== null && s && s.unidade) m.valor.appendChild(el("small", "", s.unidade));
        });
        // periféricos
        limpar(perLista);
        if (!E.baterias.length) perLista.appendChild(el("div", "nota", T("VisaoNenhum", t("nenhum conectado", "none connected"))));
        E.baterias.forEach(function (b) {
          var tem = b.bateria !== null && b.bateria !== undefined;
          var pct = el("span", !tem ? "sem" : b.carregando ? "carga" : b.bateria <= 20 ? "baixa" : "", tem ? b.bateria + "%" : "--");
          perLista.appendChild(el("div", "vg-per", null, el("span", "", b.nome), pct));
        });
        // eventos
        limpar(evLista);
        if (!E.eventos || !E.eventos.length) evLista.appendChild(el("div", "nota", T("VisaoSemEventos", t("Nada novo desde que o app abriu.", "Nothing new since the app started."))));
        (E.eventos || []).slice(0, 8).forEach(function (x) { evLista.appendChild(el("div", "", null, el("span", "", x.h), document.createTextNode(x.t))); });
        // casa
        limpar(casaTexto);
        if (!S.casa.servidor || !E.casa.length) {
          casaTexto.appendChild(el("b", "", T("VisaoConecteCasa", t("Conecte a casa", "Connect your home"))));
          casaTexto.appendChild(el("small", "", T("CasaResumoSemConta", t("Conecte o ar-condicionado e a lava e seca pela conta ConnectLife.", "Connect your air conditioner and washer with your ConnectLife account."))));
        } else {
          var ap = el("div", "ap");
          E.casa.forEach(function (a) { ap.appendChild(el("span", "", null, el("b", "", a.nome), document.createTextNode(resumoDoAparelho(a)))); });
          casaTexto.appendChild(ap);
        }
      },
      sair: function () {
        vivo = false; clearInterval(relogio);
        window.removeEventListener("resize", redesenhar);
        previa.sair();
      }
    };
  };

  function resumoDoAparelho(a) {
    if (!a.online) return t("fora do ar", "offline");
    if (a.tipo !== "ar") return t("online", "online");
    if (!a.ligado) return t("desligado", "off") + (a.ambiente !== null ? " · " + numero(a.ambiente) + " °C" : "");
    return (a.modo || t("ligado", "on")) + (a.alvo !== null ? " · " + numero(a.alvo) + " °C" : "") +
      (a.ambiente !== null ? " (" + t("ambiente", "room") + " " + numero(a.ambiente) + " °C)" : "");
  }

  // ---------------- Baterias ----------------

  var ESTADOS = {
    Desligado: ["desligado", "off"], Desconectado: ["desconectado", "disconnected"],
    SemLeitura: ["sem leitura", "no reading"], Dormindo: ["dormindo", "asleep"]
  };

  function desenharBaterias(alvo, curto) {
    limpar(alvo);
    var lista = E ? E.baterias : [];
    if (!lista.length) {
      alvo.appendChild(el("div", "vazio", t("Nenhum periférico com bateria encontrado.", "No battery-powered peripheral found.")));
      return;
    }
    lista.forEach(function (b) {
      var tem = b.bateria !== null && b.bateria !== undefined;
      var nota = "";
      if (b.carregando) nota = t("carregando", "charging");
      else if (!tem) { var e = ESTADOS[b.estado]; nota = e ? t(e[0], e[1]) : b.estado; }
      else if (b.restante) nota = "~" + b.restante;
      var nome = el("div", "nome", null, el("span", "", b.nome));
      if (nota) nome.appendChild(el("small", "", nota));
      var barra = el("div", "barra"), nivel = el("i");
      nivel.style.width = (tem ? Math.max(0, Math.min(100, b.bateria)) : 0) + "%";
      if (tem && b.bateria <= 15) nivel.className = "baixa"; else if (tem && b.bateria <= 35) nivel.className = "media";
      barra.appendChild(nivel);
      var linhaDaBateria = el("div", "bat", null, nome, barra, el("span", "pct", tem ? b.bateria + "%" : "--"));
      if (!curto) linhaDaBateria.title = b.fonte;
      alvo.appendChild(linhaDaBateria);
    });
  }


  /// Um botao que pede confirmacao no segundo clique (a pagina nao usa caixas de dialogo).
  function botaoPerigoso(texto, confirmar, aoConfirmar) {
    var armado = false, relogio = 0;
    var b = botao(texto, "b", function () {
      if (!armado) {
        armado = true;
        b.textContent = confirmar;
        b.classList.add("perigo");
        relogio = setTimeout(function () { armado = false; b.textContent = texto; b.classList.remove("perigo"); }, 4000);
        return;
      }
      clearTimeout(relogio);
      aoConfirmar();
    });
    return b;
  }

  MONTAR.baterias = function (alvo) {
    var grade = el("div", "grade larga");
    var c = cartaoSimples(t("Periféricos", "Peripherals"), "duplo");
    var lista = el("div");
    c.appendChild(lista);
    grade.appendChild(c);

    if (S.widget) {
      var w = S.widget;
      var wc = cartaoSimples(t("Widget na área de trabalho", "Desktop widget"));
      function mudarWidget(a) { mudar("widget", a); }
      wc.appendChild(chave(t("Mostrar o widget", "Show the widget"), null, troca(w.ativo, function (v) { mudarWidget({ ativo: v }); })));
      wc.appendChild(chave(t("Mostrar a Casa no widget", "Show Home on the widget"), null, troca(w.casa, function (v) { mudarWidget({ casa: v }); })));
      wc.appendChild(chave(t("Sempre na frente", "Always on top"), null, troca(w.frente, function (v) { mudarWidget({ frente: v }); })));
      wc.appendChild(chave(t("Travar a posição", "Lock position"), null, troca(w.travado, function (v) { mudarWidget({ travado: v }); })));
      wc.appendChild(campo(t("Arranjo", "Layout"), segmentos([["aneis", t("Anéis", "Rings")], ["lista", t("Lista", "List")]], w.layout,
        function (v) { mudarWidget({ layout: v }); })));
      wc.appendChild(campo(t("Vidro", "Glass"), segmentos([["nitido", t("Nítido", "Sharp")], ["claro", t("Claro", "Clear")], ["fosco", t("Fosco", "Frosted")]], w.vidro,
        function (v) { mudarWidget({ vidro: v }); })));
      wc.appendChild(campo(t("Tom", "Tint"), segmentos([["auto", t("Automático", "Automatic")], ["escura", t("Escuro", "Dark")], ["clara", t("Claro", "Light")]], w.cor,
        function (v) { mudarWidget({ cor: v }); })));
      wc.appendChild(botao(t("Voltar ao canto", "Back to the corner"), "b", function () {
        pedir("widget.canto").catch(function (e) { faixa(e.message, true); });
      }));
      grade.appendChild(wc);
    }
    alvo.appendChild(grade);
    return { atualizar: function () { desenharBaterias(lista, false); } };
  };

  function botaoClassica(pagina) {
    var b = el("button", "b", t("Abrir a janela clássica", "Open the classic window"));
    b.type = "button";
    b.addEventListener("click", function () { pedir("abrir", { pagina: pagina || "" }).catch(function (e) { faixa(e.message, true); }); });
    return b;
  }


  // ---------------- Metricas ----------------

  function cor(nome) { return getComputedStyle(document.documentElement).getPropertyValue(nome).trim(); }

  function desenharGrafico(canvas, pontos) {
    var r = canvas.getBoundingClientRect();
    var dpr = window.devicePixelRatio || 1;
    var w = Math.max(10, Math.round(r.width * dpr)), h = Math.max(10, Math.round(r.height * dpr));
    if (canvas.width !== w) canvas.width = w;
    if (canvas.height !== h) canvas.height = h;
    var g = canvas.getContext("2d");
    g.clearRect(0, 0, w, h);
    var min = Infinity, max = -Infinity, n = pontos ? pontos.length : 0, i;
    for (i = 0; i < n; i++) if (pontos[i] !== null) { if (pontos[i] < min) min = pontos[i]; if (pontos[i] > max) max = pontos[i]; }
    g.strokeStyle = cor("--border");
    g.lineWidth = dpr;
    g.beginPath();
    for (i = 1; i < 4; i++) { var y0 = Math.round(h * i / 4) + 0.5; g.moveTo(0, y0); g.lineTo(w, y0); }
    g.stroke();
    if (min === Infinity || n < 2) return null;
    var folga = (max - min) * 0.12 || Math.max(1, Math.abs(max) * 0.05);
    var lo = min - folga, hi = max + folga;
    function x(k) { return k * (w - 1) / (n - 1); }
    function y(v) { return h - 2 * dpr - (v - lo) / (hi - lo) * (h - 4 * dpr); }
    // Onde nao houve amostra a linha se interrompe: nao ha reta inventada por cima da falha.
    g.strokeStyle = cor("--accent");
    g.lineWidth = 1.8 * dpr;
    g.lineJoin = "round";
    g.beginPath();
    var solto = true;
    for (i = 0; i < n; i++) {
      if (pontos[i] === null) { solto = true; continue; }
      if (solto) g.moveTo(x(i), y(pontos[i])); else g.lineTo(x(i), y(pontos[i]));
      solto = false;
    }
    g.stroke();
    return { min: min, max: max };
  }

  MONTAR.metricas = function (alvo) {
    var barra = el("div", "barra-de-acoes");
    var seletor = seletorDeSensor("", t("Acompanhar um sensor…", "Track a sensor…"));
    seletor.setAttribute("aria-label", t("Acompanhar um sensor", "Track a sensor"));
    seletor.addEventListener("change", function () {
      var id = seletor.value;
      if (!id || S.metricas.indexOf(id) >= 0) { seletor.value = ""; return; }
      mudar("metricas.definir", { ids: S.metricas.concat([id]) });
    });
    barra.appendChild(seletor);
    barra.appendChild(segmentos([[600, "10 min"], [3600, "1 h"], [21600, "6 h"]], janela, function (v) { janela = v; buscar(); }));
    alvo.appendChild(barra);

    var grade = el("div", "grade larga");
    var cartoes = [];
    S.metricas.forEach(function (id) {
      var s = sensor(id);
      var c = el("div", "cartao simples");
      var fechar = el("button", "x", "✕");
      fechar.type = "button";
      fechar.setAttribute("aria-label", t("Deixar de acompanhar", "Stop tracking"));
      fechar.addEventListener("click", function () {
        mudar("metricas.definir", { ids: S.metricas.filter(function (x) { return x !== id; }) });
      });
      var titulo = el("span", "t", s ? s.rotulo : t("sensor ausente", "missing sensor"));
      titulo.title = titulo.textContent;
      c.appendChild(el("div", "cab", null, titulo, fechar));
      var valor = el("div", "valor", "--");
      var faixaDoGrafico = el("div", "legenda", " ");
      var canvas = el("canvas", "grafico");
      c.appendChild(valor); c.appendChild(canvas); c.appendChild(faixaDoGrafico);
      grade.appendChild(c);
      cartoes.push({ id: id, s: s, valor: valor, canvas: canvas, faixa: faixaDoGrafico, pontos: null });
    });
    if (!cartoes.length)
      grade.appendChild(el("div", "vazio", t("Nenhum sensor acompanhado. Escolha um na lista acima.", "No tracked sensor. Pick one from the list above.")));
    alvo.appendChild(grade);

    function desenhar() {
      cartoes.forEach(function (c) {
        var f = desenharGrafico(c.canvas, c.pontos);
        var u = c.s && c.s.unidade ? " " + c.s.unidade : "";
        c.faixa.textContent = f ? t("mín ", "min ") + numero(f.min) + u + "  ·  " + t("máx ", "max ") + numero(f.max) + u
          : t("sem amostras neste período", "no samples in this period");
      });
    }

    var vivo = true;
    function buscar() {
      if (!cartoes.length) return;
      pedir("metricas", { ids: S.metricas, segundos: janela }).then(function (r) {
        if (!vivo) return;
        cartoes.forEach(function (c) { c.pontos = r.series[c.id] || null; });
        desenhar();
      }, function () { });
    }
    buscar();
    var relogio = setInterval(buscar, (S.passo || 5) * 1000);
    window.addEventListener("resize", desenhar);

    return {
      atualizar: function () {
        cartoes.forEach(function (c) {
          var v = leitura(c.id);
          limpar(c.valor);
          c.valor.appendChild(document.createTextNode(numero(v)));
          if (v !== null && c.s && c.s.unidade) c.valor.appendChild(el("small", "", c.s.unidade));
        });
      },
      sair: function () { vivo = false; clearInterval(relogio); window.removeEventListener("resize", desenhar); }
    };
  };

  // ---------------- Especificacoes ----------------

  MONTAR.specs = function (alvo) {
    var grade = el("div", "grade larga");
    alvo.appendChild(grade);
    var vivo = true;
    function desenhar() {
      limpar(grade);
      specs.forEach(function (g) {
        var c = cartaoSimples(g.titulo);
        if (g.destaque) {
          var d = el("div", "valor", g.destaque);
          d.style.fontSize = "17px"; d.style.marginBottom = "8px"; d.style.userSelect = "text";
          c.appendChild(d);
        }
        var ls = el("div", "linhas");
        g.linhas.forEach(function (l) { ls.appendChild(linha(l[0], l[1])); });
        c.appendChild(ls);
        grade.appendChild(c);
      });
      if (!specs.length) grade.appendChild(el("div", "vazio", t("Nada para mostrar.", "Nothing to show.")));
    }
    function coletar() {
      limpar(grade);
      grade.appendChild(el("div", "vazio", t("Lendo o hardware…", "Reading the hardware…")));
      pedir("specs").then(function (r) { specs = r || []; if (vivo) desenhar(); },
        function (e) { if (vivo) { limpar(grade); grade.appendChild(el("div", "vazio", e.message)); } });
    }
    var de = el("button", "b", t("Ler de novo", "Read again"));
    de.type = "button";
    de.addEventListener("click", coletar);
    var barraDeSpecs = el("div", "barra-de-acoes", null, de);
    alvo.insertBefore(barraDeSpecs, grade);
    if (specs) desenhar(); else coletar();
    return { sair: function () { vivo = false; } };
  };


  // ---------------- Painel CPU: Paineis e Perfis ----------------

  function perfilEditado() {
    var p = perfil(editando) || perfil(S.ativo) || S.perfis[0] || null;
    editando = p ? p.nome : "";
    return p;
  }

  /// Grava o perfil com as mudancas por cima do que ele ja tem (a classica grava na hora).
  function salvarPerfil(p, mudancas) {
    var a = { nome: p.nome, p1: p.p1, p2: p.p2, fahrenheit: !!p.fahrenheit, percent: !!p.percent, div1: p.div1, div2: p.div2, rodizio: !!p.rodizio };
    for (var k in mudancas) if (Object.prototype.hasOwnProperty.call(mudancas, k)) a[k] = mudancas[k];
    return mudar("perfil.salvar", a);
  }

  /// Trocar o sensor de um painel; no de baixo a unidade acompanha (W para potencia, % para carga).
  function trocarSensor(p, n) {
    escolherSensor(T(n === 1 ? "PickSensor1" : "PickSensor2", t("Sensor do painel " + n, "Panel " + n + " sensor")), n === 1 ? p.p1 : p.p2)
      .then(function (id) {
        if (!id) return;
        var m = {};
        m[n === 1 ? "p1" : "p2"] = id;
        if (n === 2) {
          var s = sensor(id);
          if (s && s.tipo === "Power") m.percent = false;
          else if (s && (s.tipo === "Load" || s.tipo === "Level")) m.percent = true;
        }
        salvarPerfil(p, m);
      });
  }

  function nomeCurto(id) {
    var s = sensor(id);
    return s ? s.nome : id ? t("sensor ausente", "missing sensor") : "—";
  }

  function textoDoSensor(id, fahrenheit) {
    var s = sensor(id), v = leitura(id);
    if (v === null) return null;
    if (fahrenheit && s && s.tipo === "Temperature") return numero(v * 9 / 5 + 32) + " °F";
    return comUnidade(id);
  }

  /// SensorSlot: nome, "categoria · fonte", a leitura em destaque e Trocar.
  function slotDeSensor(p, n) {
    var id = n === 1 ? p.p1 : p.p2;
    var s = sensor(id);
    var valor = el("span", "sv");
    var st = el("div", "st");
    if (s) {
      st.appendChild(el("b", "", s.nome));
      st.appendChild(el("small", "", [s.grupo || s.categoria, s.fonte].filter(function (x) { return x; }).join("  ·  ")));
    } else st.appendChild(el("span", "nota", id ? t("sensor ausente", "missing sensor") : T("NoSensorChosen", t("nenhum sensor escolhido", "no sensor chosen"))));
    var slot = el("div", "slot", null, st, valor, botao(T("Change", t("Trocar", "Change")), "b", function () { trocarSensor(p, n); }));
    slot.atualizar = function () {
      var txt = s ? textoDoSensor(id, n === 1 && p.fahrenheit) : null;
      valor.textContent = txt || (s ? T("NoReading", t("sem leitura", "no reading")) : "");
      valor.className = txt ? "sv" : "sv sem";
    };
    return slot;
  }

  function cartaoDoPainel(p, n) {
    var c = cartao(T(n === 1 ? "Panel1" : "Panel2", n === 1 ? t("Painel 1  ·  cima", "Panel 1  ·  top") : t("Painel 2  ·  baixo", "Panel 2  ·  bottom")));
    var slot = slotDeSensor(p, n);
    c.corpo.appendChild(slot);
    c.corpo.appendChild(el("div", "rotulo", T("Scale", t("Escala", "Scale"))));
    var escala = el("div", "escala");
    var div = n === 1 ? p.div1 : p.div2;
    [[0, "Auto"], [1, "÷1"], [10, "÷10"], [100, "÷100"], [1000, "÷1000"]].forEach(function (d) {
      var b = botao(d[1], "b pequeno" + (d[0] === div ? " sel" : ""), function () {
        if (d[0] === div) return;
        var m = {}; m[n === 1 ? "div1" : "div2"] = d[0];
        salvarPerfil(p, m);
      });
      b.setAttribute("aria-pressed", d[0] === div ? "true" : "false");
      escala.appendChild(b);
    });
    c.corpo.appendChild(escala);
    c.corpo.appendChild(el("div", "rotulo", T("Unit", t("Unidade", "Unit"))));
    var unidade = n === 1
      ? segmentos([[false, "°C"], [true, "°F"]], !!p.fahrenheit, function (v) { salvarPerfil(p, { fahrenheit: v }); })
      : segmentos([[true, "%"], [false, "W"]], !!p.percent, function (v) { salvarPerfil(p, { percent: v }); });
    unidade.style.width = "140px";
    unidade.style.height = "30px";
    c.corpo.appendChild(unidade);
    var nota = el("div", "nota", T("UnitAlwaysOn", t("O cooler acende sempre um dos dois, não há como apagar.", "The cooler always lights one of the two, it cannot be blanked.")));
    nota.style.marginTop = "4px";
    c.corpo.appendChild(nota);
    c.vaga = slot;
    return c;
  }

  function abaPaineis(alvo, p) {
    var c1 = cartaoDoPainel(p, 1), c2 = cartaoDoPainel(p, 2);
    alvo.appendChild(el("div", "dois", null, c1, c2));
    var pc = cartao(T("Preview", t("Prévia", "Preview")), "grande");
    var pv = previaDoCooler();
    pc.corpo.appendChild(pv.el);
    alvo.appendChild(pc);
    return {
      atualizar: function () { c1.vaga.atualizar(); c2.vaga.atualizar(); previaDoPerfil(pv, p); },
      sair: pv.sair
    };
  }

  function rotuloDoBotaoDePainel(p, n) {
    var id = n === 1 ? p.p1 : p.p2;
    var un = n === 1 ? (p.fahrenheit ? "°F" : "°C") : (p.percent ? "%" : "W");
    return t("Painel ", "Panel ") + n + "  ·  " + nomeCurto(id) + "  (" + un + ")";
  }

  function abaPerfis(alvo, p) {
    var esquerda = el("div", "coluna"), direita = el("div", "coluna");
    alvo.appendChild(el("div", "lado", null, esquerda, direita));

    // Perfis salvos
    var salvos = cartao(T("SavedProfiles", t("Perfis salvos", "Saved profiles")), "estica");
    var lista = el("div", "perfis");
    lista.setAttribute("role", "listbox");
    S.perfis.forEach(function (x) {
      var nome = el("b", "", null, el("span", "", x.nome));
      if (x.nome === S.padrao) nome.appendChild(el("span", "selo", T("DefaultBadge", t("PADRÃO", "DEFAULT"))));
      if (x.nome === S.ativo) nome.appendChild(el("span", "selo cheio", T("ActiveBadge", t("ATIVO", "ACTIVE"))));
      var r = el("div", "pf" + (x.nome === p.nome ? " sel" : ""), null, nome, el("small", "", nomeCurto(x.p1) + "  ·  " + nomeCurto(x.p2)));
      r.setAttribute("role", "option");
      r.setAttribute("aria-selected", x.nome === p.nome ? "true" : "false");
      r.tabIndex = x.nome === p.nome ? 0 : -1;
      r.addEventListener("click", function () { if (editando !== x.nome) { editando = x.nome; mostrar("painel", true); } });
      r.addEventListener("dblclick", function () { if (x.nome !== S.ativo) mudar("perfil.ativar", { nome: x.nome }); });
      lista.appendChild(r);
    });
    lista.addEventListener("keydown", function (e) {
      if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
      e.preventDefault();
      var i = 0;
      S.perfis.forEach(function (x, k) { if (x.nome === p.nome) i = k; });
      var j = Math.max(0, Math.min(S.perfis.length - 1, i + (e.key === "ArrowDown" ? 1 : -1)));
      if (j !== i) { editando = S.perfis[j].nome; mostrar("painel", true); var s = $("pagina").querySelector(".pf.sel"); if (s) s.focus(); }
    });
    salvos.corpo.appendChild(lista);
    var acoes = el("div", "grade3");
    acoes.appendChild(botao(T("New", t("Novo", "New")), "b", function () {
      perguntarNome(T("NewProfileName", t("Nome do novo perfil:", "Name of the new profile:")), t("Perfil ", "Profile ") + (S.perfis.length + 1)).then(function (n) {
        if (!n) return;
        if (perfil(n)) { faixa(T("NameTaken"), true); return; }
        editando = n;
        mudar("perfil.criar", { nome: n });
      });
    }));
    acoes.appendChild(botao(T("Rename", t("Renomear", "Rename")), "b", function () {
      perguntarNome(T("NewName", t("Novo nome:", "New name:")), p.nome).then(function (n) {
        if (!n || n === p.nome) return;
        if (perfil(n)) { faixa(T("NameTaken"), true); return; }
        editando = n;
        mudar("perfil.renomear", { nome: p.nome, novo: n });
      });
    }));
    acoes.appendChild(botao(T("Duplicate", t("Duplicar", "Duplicate")), "b", function () {
      perguntarNome(T("CopyName", t("Nome da cópia:", "Name of the copy:")), p.nome + T("CopySuffix", t(" (cópia)", " (copy)"))).then(function (n) {
        if (!n) return;
        if (perfil(n)) { faixa(T("NameTaken"), true); return; }
        editando = n;
        mudar("perfil.duplicar", { nome: p.nome, novo: n });
      });
    }));
    acoes.appendChild(botao(T("Export", t("Exportar", "Export")), "b", function () { mudar("perfil.exportar", { nome: p.nome }); }));
    acoes.appendChild(botao(T("Import", t("Importar", "Import")), "b", function () { mudar("perfil.importar"); }));
    acoes.appendChild(botao(T("Delete", t("Excluir", "Delete")), "b perigo", function () {
      if (S.perfis.length <= 1) { faixa(T("KeepOneProfile"), true); return; }
      confirmar(t("Excluir o perfil " + p.nome + "?", "Delete the profile " + p.nome + "?"), T("Delete", t("Excluir", "Delete")), "b perigo").then(function (sim) {
        if (!sim) return;
        editando = "";
        mudar("perfil.excluir", { nome: p.nome });
      });
    }));
    salvos.corpo.appendChild(acoes);
    esquerda.appendChild(salvos);

    // Rodizio
    var rod = cartao(T("Rotation", t("Rodízio", "Rotation")));
    var segundos = S.rodizio && S.rodizio.segundos > 0 ? S.rodizio.segundos : 20;
    var num = caixaNumero(segundos, 2, S.maximo || 999, function (v) { mudar("rodizio", { nome: p.nome, ligado: !!p.rodizio, segundos: v }); });
    var tg = trocaComRotulo(T("IncludeInRotation", t("Incluir o perfil selecionado no rodízio", "Include the selected profile in the rotation")), !!p.rodizio,
      function (v) { mudar("rodizio", { nome: p.nome, ligado: v, segundos: num.valor }); });
    num.ligar(!!p.rodizio);
    tg.style.marginBottom = "12px";
    rod.corpo.appendChild(tg);
    var linhaNum = el("div", "inline", null, num, el("span", "nota", S.rodizio ? S.rodizio.nota : ""));
    linhaNum.style.gap = "12px";
    rod.corpo.appendChild(linhaNum);
    esquerda.appendChild(rod);

    // Previa do perfil selecionado
    var prev = cartao(T("ProfilePreview", t("Prévia do perfil selecionado", "Preview of the selected profile")));
    var pv = previaDoCooler();
    var col = el("div", "botoes-col");
    col.appendChild(botao(rotuloDoBotaoDePainel(p, 1), "b", function () { trocarSensor(p, 1); }));
    col.appendChild(botao(rotuloDoBotaoDePainel(p, 2), "b", function () { trocarSensor(p, 2); }));
    var padrao = botao(p.nome === S.padrao ? T("AlreadyDefault", t("Perfil padrão", "Default profile")) : T("SetAsDefault", t("Definir como padrão", "Set as default")), "b",
      function () { mudar("perfil.padrao", { nome: p.nome }); });
    padrao.disabled = p.nome === S.padrao;
    col.appendChild(padrao);
    var aplicar = botao(p.nome === S.ativo ? T("AlreadyActive", t("Perfil ativo", "Active profile")) : T("ApplyProfile", t("Aplicar perfil", "Apply profile")),
      p.nome === S.ativo ? "b" : "b p", function () { mudar("perfil.ativar", { nome: p.nome }); });
    aplicar.disabled = p.nome === S.ativo;
    col.appendChild(aplicar);
    prev.corpo.appendChild(el("div", "previa-lado", null, pv.el, col));
    direita.appendChild(prev);

    // Perfil por jogo
    var jogos = cartao(T("GameProfilesCard", t("Perfil por jogo", "Profile per game")), "estica");
    var tgJogo = trocaComRotulo(T("GameProfilesOn"), !!S.jogos.ligado, function (v) { mudar("jogos.ligar", { ligado: v }); });
    tgJogo.troca.disabled = !S.jogos.rtss;
    tgJogo.style.marginBottom = "10px";
    jogos.corpo.appendChild(tgJogo);
    var legenda = el("div", "nota", T("CurrentGame", t("Jogo detectado agora", "Game detected now")));
    legenda.style.marginLeft = "4px";
    jogos.corpo.appendChild(legenda);
    var nomeJogo = el("b"), subJogo = el("small");
    jogos.corpo.appendChild(el("div", "jogo", null, el("div", "ji icone", ""), el("div", "", null, nomeJogo, subJogo)));
    var vincular = botao("", "b p", function () { mudar("jogo.vincular", { perfil: p.nome }); });
    var desvincular = botao(T("UnbindGame", t("Desvincular", "Unbind")), "b", function () {
      var j = E && E.jogo;
      if (j) mudar("jogo.desvincular", { chave: j.chave });
    });
    jogos.corpo.appendChild(el("div", "par", null, vincular, desvincular));
    var n1 = el("div", "nota", T("GameProfilesNote"));
    n1.style.marginBottom = "10px";
    jogos.corpo.appendChild(n1);
    var procurar = botao(T("GameBrowse", t("Adicionar executável…", "Add executable…")), "b", function () {
      mudar("jogo.adicionar", { perfil: p.nome }).then(function (r) {
        if (!r || !r.pergunta) return;
        confirmar(r.pergunta, t("Substituir", "Replace")).then(function (sim) {
          if (sim) mudar("jogo.substituir", { sim: true });
          else pedir("jogo.substituir", { sim: false }).catch(function () { });
        });
      });
    });
    procurar.style.width = "240px";
    procurar.style.alignSelf = "flex-start";
    procurar.style.marginBottom = "8px";
    jogos.corpo.appendChild(procurar);
    jogos.corpo.appendChild(el("div", "nota", T("GameFileNote")));
    var titulo = el("div", "rotulo", T("GameBindings", t("Vínculos", "Bindings")));
    titulo.style.margin = "20px 0 4px 4px";
    jogos.corpo.appendChild(titulo);
    var vinculos = el("div", "vinculos");
    S.jogos.vinculos.forEach(function (v) {
      var x = botao("", "x icone", function () { mudar("jogo.desvincular", { chave: v.chave }); });
      x.setAttribute("aria-label", t("Desvincular ", "Unbind ") + v.nome);
      vinculos.appendChild(el("div", "vinculo", null, el("div", "ji icone", ""),
        el("div", "vt", null, el("b", "", v.nome), el("small", "", v.perfil)), x));
    });
    if (!S.jogos.vinculos.length) vinculos.appendChild(el("div", "nota", T("NoGameBindings")));
    jogos.corpo.appendChild(vinculos);
    direita.appendChild(jogos);

    var ultimo = "-";
    function atualizarJogo() {
      var j = E ? E.jogo : null;
      var marca = j ? j.chave + "|" + (j.perfil || "") + "|" + j.nome : "";
      if (marca === ultimo) return;
      ultimo = marca;
      var rtss = S.rtss ? S.rtss.presente : S.jogos.rtss;
      nomeJogo.textContent = !rtss ? T("RtssMissing", t("RTSS não encontrado", "RTSS not found")) : j ? j.nome : T("NoGameToBind");
      nomeJogo.style.color = rtss ? "" : "var(--warn)";
      subJogo.textContent = !j ? (S.jogosContagem || "") : j.perfil ? Tm("BoundProfile", j.perfil) : Tm("SelectedProfile", p.nome);
      subJogo.className = j && j.perfil ? "ok" : "";
      vincular.textContent = Tm("BindGameTo", p.nome);
      vincular.disabled = !S.jogos.ligado || !j;
      desvincular.disabled = !S.jogos.ligado || !j || !j.perfil;
    }

    return {
      atualizar: function () { previaDoPerfil(pv, p); atualizarJogo(); },
      sair: pv.sair
    };
  }

  MONTAR.painel = function (alvo) {
    try { aba = sessionStorage.getItem("aba") || aba; } catch (e) { }
    var abas = el("div", "abas", null, segmentos([["paineis", T("PanelsTab", t("Painéis", "Panels"))], ["perfis", T("NavProfiles", t("Perfis", "Profiles"))]], aba, function (v) {
      aba = v;
      try { sessionStorage.setItem("aba", v); } catch (e) { }
      mostrar("painel", true);
    }));
    alvo.appendChild(abas);
    var p = perfilEditado();
    if (!p) return {};
    return aba === "perfis" ? abaPerfis(alvo, p) : abaPaineis(alvo, p);
  };

  // ---------------- Configuracoes ----------------

  MONTAR.config = function (alvo) {
    alvo.appendChild(grupo(T("SettingsGeneral", t("Geral", "General"))));

    var idioma = segmentos([["pt", "Português (BR)"], ["en", "English (US)"]], S.pt ? "pt" : "en", function (v) { mudar("idioma", { lingua: v }); });
    idioma.style.width = "300px";
    alvo.appendChild(opcao("", T("Language_", t("Idioma", "Language")), T("LanguageNote"), [idioma]));

    alvo.appendChild(opcao("", T("StartWithWindows"), T("StartWithWindowsNote"), interruptor(S.config.inicio, function (v, b, rotular) {
      pedir("inicio", { ligado: v }).then(function (r) {
        S.config.inicio = !!r;
        b.definir(!!r); rotular(!!r);
      }, function (e) {
        b.definir(S.config.inicio); rotular(S.config.inicio);
        faixa(e.message, true);
      });
    })));

    alvo.appendChild(opcao("", T("VisaoNovaOpcao"), T("VisaoNovaNota"), interruptor(!S.visaoNova, function (v) {
      mudar("visao.classica", { ligado: v });
    })));

    alvo.appendChild(grupo(T("PanelLabel", t("Painel", "Panel"))));
    alvo.appendChild(opcao("", T("ShowAllSensors"), T("ShowAllShort"), interruptor(S.todos, function (v) {
      mudar("sensores.todos", { ligado: v });
    })));

    var ocioso = S.config.ocioso;
    var num = caixaNumero(ocioso > 0 ? ocioso : 15, 1, S.maximo || 999, function (v) {
      mudar("config.salvar", { ocioso: v, rodizio: S.config.rodizio });
    });
    num.ligar(ocioso > 0);
    var notaOcioso = el("span", "nota", ocioso > 0 ? T("MinutesIdle") : T("Off", t("desligado", "off")));
    notaOcioso.style.minWidth = "84px";
    var tgOcioso = troca(ocioso > 0, function (v) {
      mudar("config.salvar", { ocioso: v ? num.valor : 0, rodizio: S.config.rodizio });
    });
    var linhaOcioso = opcao("", T("BlankWhenIdle"), T("BlankWhenIdleShort"), [num, notaOcioso, tgOcioso]);
    linhaOcioso.desc.title = T("BlankWhenIdleNote");
    alvo.appendChild(linhaOcioso);

    alvo.appendChild(grupo(T("SettingsFps", "FPS")));
    var rtss = S.rtss || { presente: S.jogos.rtss, winget: false };
    var estadoRtss = el("small", rtss.presente ? "ok" : "aviso", rtss.presente ? T("RtssActive") : T("RtssAbsent"));
    var instalar = botao(T("InstallRtss"), "b", function () { mudar("rtss.instalar"); });
    instalar.disabled = rtss.presente || !rtss.winget;
    var linhaRtss = opcao("", "RivaTuner Statistics Server", estadoRtss, [instalar,
      botao(T("RtssPage"), "b", function () { pedir("rtss.site").catch(function () { }); }),
      botao(T("Recheck"), "b", function () {
        pedir("iniciar").then(function (r) { S = r; mostrar(atual, true); }, function (e) { faixa(e.message, true); });
      })]);
    linhaRtss.querySelector(".ot b").title = T("RtssNote");
    alvo.appendChild(linhaRtss);

    alvo.appendChild(grupo(T("SettingsData", t("Dados", "Data"))));
    var onde = el("small", "", S.pasta || "");
    onde.style.userSelect = "text";
    alvo.appendChild(opcao("", T("DataFolderTitle"), onde, [botao(T("OpenFolder"), "b", function () { pedir("pasta").catch(function () { }); })]));

    var linhaBackup = opcao("", T("BackupTitle"), T("BackupShort"), [
      botao(T("BackupCreate"), "b", function () { mudar("backup.exportar"); }),
      botao(T("BackupRestore"), "b", function () {
        pedir("backup.ler").then(function (r) {
          if (!r) return;
          var resumo = r.nota + "\n\n" +
            T("SavedProfiles", t("Perfis salvos", "Saved profiles")) + ": " + r.perfis + "\n" +
            T("GameBindings", t("Vínculos", "Bindings")) + ": " + r.jogos + "\n" +
            T("NavMetrics", t("Métricas", "Metrics")) + ": " + r.metricas + "\n" +
            T("ActiveProfile", t("PERFIL ATIVO", "ACTIVE PROFILE")) + ": " + r.ativo + "\n" +
            t("Sensores não encontrados: ", "Sensors not found: ") + r.faltando + "\n\n" +
            t("Sensores ausentes não serão substituídos automaticamente.", "Missing sensors will not be replaced automatically.");
          revisar(T("RestorePreview", t("Revisar restauração", "Review restore")), resumo, T("RestoreApply", t("Restaurar", "Restore"))).then(function (sim) {
            if (sim) mudar("backup.aplicar");
          });
        }, function (e) { faixa(e.message, true); });
      })]);
    linhaBackup.desc.title = T("BackupNote");
    alvo.appendChild(linhaBackup);

    var linhaDiag = opcao("", T("DiagnosticTitle"), T("DiagnosticShort"), [botao(T("DiagnosticOpen"), "b", function () {
      pedir("diagnostico").then(function (texto) {
        return revisar(T("DiagnosticTitle"), texto, T("SaveFile", t("Salvar arquivo…", "Save file…"))).then(function (sim) {
          if (!sim) return;
          return pedir("diagnostico.salvar").then(function (r) { if (r) faixa(r); });
        });
      }).catch(function (e) { faixa(e.message, true); });
    })]);
    linhaDiag.desc.title = T("DiagnosticNote");
    alvo.appendChild(linhaDiag);

    alvo.appendChild(el("div", "atalhos", T("KeyboardHelp")));
    return {};
  };

  // ---------------- Sobre ----------------

  MONTAR.sobre = function (alvo) {
    var so = S.sobre || {};
    var id = el("div", "cartao");
    var img = el("img");
    img.src = "app.ico";
    img.alt = "";
    var h1 = el("h1", "", null, el("span", "", "Mhiagos Control"), el("span", "chip", so.versao || ("v" + S.versao)));
    var github = botao(T("OpenOnGitHub", "GitHub"), "b p", function () { pedir("github").catch(function () { }); });
    var autor = el("span", "nota", so.autor || "");
    var bt = el("div", "botoes", null, github, autor);
    var it = el("div", "it", null, h1, el("p", "", T("AboutTagline")), bt);
    var ident = el("div", "identidade", null, img, it);
    id.appendChild(ident);
    alvo.appendChild(id);

    function bloco(rotulo, valor, ponto, apoio, aviso) {
      var b = el("b", aviso ? "aviso" : "");
      if (ponto) b.appendChild(el("span", "ponto " + ponto));
      b.appendChild(el("span", "", valor));
      var x = el("div", "bloco", null, el("small", "", rotulo), b);
      if (apoio) x.appendChild(el("p", "", apoio));
      return x;
    }
    var achou = !!so.painel;
    var blocos = el("div", "blocos");
    blocos.appendChild(bloco(T("PanelLabel", t("Painel", "Panel")), achou ? so.painel : T("PanelNone"), achou ? "verde" : "cinza",
      !achou ? "" : so.testado ? T("PanelTested") : T("PanelUntested")));
    blocos.appendChild(bloco(T("SensorSources"), so.fontes || "", so.alerta ? "aviso" : "verde", so.fontesApoio || "", so.alerta));
    blocos.appendChild(bloco(T("Libraries"), "MIT  ·  MPL 2.0", null, so.bibliotecas || ""));
    alvo.appendChild(blocos);
    if (!so.hw) {
      var motor = el("div", "motor" + (so.pawnio ? "" : " aviso"), so.avisoMotor || "");
      if (!so.pawnio) motor.addEventListener("click", function () { mudar("pawnio.instalar"); });
      alvo.appendChild(motor);
    }
    var isencao = cartao(T("DisclaimerTitle"));
    isencao.corpo.appendChild(el("div", "isencao", so.isencao || ""));
    alvo.appendChild(isencao);
    return {};
  };

  // ---------------- Casa ----------------

  /// O servidor da Casa (endereco externo, interno e chave), com teste ao salvar.
  function formularioDoServidor(aoSalvar) {
    var externo = el("input"), interno = el("input"), chaveDoServidor = el("input");
    externo.type = interno.type = "text"; chaveDoServidor.type = "password";
    externo.value = S.casa.externo || ""; interno.value = S.casa.interno || "";
    externo.placeholder = "https://home01.suarede.ts.net"; interno.placeholder = "192.168.1.100:8765";
    chaveDoServidor.placeholder = S.casa.servidor ? t("vazio mantém a guardada", "empty keeps the stored one") : "";
    chaveDoServidor.autocomplete = "off";
    var f = el("div", "", null,
      campo(t("Endereço externo", "External address"), externo), campo(t("Endereço interno", "Internal address"), interno),
      campo(t("Chave de acesso", "Access key"), chaveDoServidor),
      el("div", "nota", t("Basta um endereço responder; o outro fica de reserva.", "One responding address is enough; the other is a fallback.")));
    f.salvar = function () {
      faixa(t("Testando o servidor…", "Testing the server…"));
      return mudar("casa.servidor", { externo: externo.value, interno: interno.value, chave: chaveDoServidor.value }).then(function (r) {
        if (aoSalvar) aoSalvar(r);
        return r;
      });
    };
    return f;
  }

  MONTAR.casa = function (alvo) {
    if (!S.casa.servidor) {
      var largura = el("div", "largura");
      largura.style.padding = "16px";
      var c = cartao(t("Conectar a Casa", "Connect Home"));
      c.style.maxWidth = "560px";
      var f = formularioDoServidor();
      c.corpo.appendChild(f);
      var b = botao(t("Salvar e testar", "Save and test"), "b p", function () { b.disabled = true; f.salvar().then(function () { b.disabled = false; }); });
      b.style.alignSelf = "flex-start";
      b.style.marginTop = "12px";
      c.corpo.appendChild(b);
      largura.appendChild(c);
      alvo.className = "";
      alvo.appendChild(largura);
      return {};
    }
    var area = el("div");
    area.id = "casa-area";
    alvo.appendChild(area);
    area.appendChild(el("span", "", t("Abrindo a Casa…", "Opening Home…")));
    function medir() {
      var r = area.getBoundingClientRect();
      avisar("casa.area", { visivel: true, x: r.left, y: r.top, w: r.width, h: r.height });
    }
    var obs = null;
    if (window.ResizeObserver) { obs = new ResizeObserver(medir); obs.observe(area); }
    window.addEventListener("resize", medir);
    medir();
    return {
      sair: function () {
        if (obs) obs.disconnect();
        window.removeEventListener("resize", medir);
        avisar("casa.area", { visivel: false, x: 0, y: 0, w: 0, h: 0 });
      }
    };
  };

  // ---------------- partida ----------------

  function partir() {
    if (!ponte) {
      $("pagina").appendChild(el("div", "vazio", "Esta página só funciona dentro do Mhiagos Control."));
      return;
    }
    ponte.addEventListener("message", aoReceber);
    $("recolher").addEventListener("click", recolher);
    $("faixa-fechar").addEventListener("click", function () { faixa(""); });
    Promise.all([pedir("iniciar"), buscarTextos()]).then(function (r) {
      S = r[0];
      aplicarTema();
      montarNavegacao();
      var guardada = "";
      try { guardada = sessionStorage.getItem("pagina") || ""; } catch (e) { }
      // A pagina pedida por quem abriu a janela vem no fragmento (#baterias, #casa...).
      var pedida = decodeURIComponent((location.hash || "").replace(/^#/, ""));
      mostrar(MONTAR[pedida] ? pedida : guardada || "visao");
    }, function (e) {
      $("pagina").appendChild(el("div", "vazio", e.message));
    });
  }

  /// Os atalhos da classica: Ctrl+B recolhe, Ctrl+1..8 e Alt+1..9 vao a pagina
  /// daquela posicao na barra, Ctrl+Tab percorre as paginas.
  document.addEventListener("keydown", function (e) {
    if (!S || document.querySelector(".fundo")) return;
    var itens = Array.prototype.slice.call($("itens").children);
    if (e.ctrlKey && !e.altKey && (e.key === "b" || e.key === "B")) { e.preventDefault(); recolher(); return; }
    if (e.ctrlKey && e.key === "Tab") {
      e.preventDefault();
      var i = 0;
      itens.forEach(function (b, k) { if (b.dataset.pagina === atual) i = k; });
      i = (i + (e.shiftKey ? itens.length - 1 : 1)) % itens.length;
      mostrar(itens[i].dataset.pagina);
      return;
    }
    if ((e.ctrlKey && !e.altKey) || (e.altKey && !e.ctrlKey)) {
      var n = Number(e.key);
      if (n >= 1 && n <= itens.length) { e.preventDefault(); mostrar(itens[n - 1].dataset.pagina); }
    }
  });

  partir();
})();
