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
  var casaAba = "";        // a aba da pagina da Casa a abrir na proxima vez (o cartao do tempo pede "clima")

  // ---------------- ponte ----------------

  function pedir(m, a) {
    return new Promise(function (ok, falha) {
      if (!ponte) { falha(new Error("sem ponte")); return; }
      var id = proximo++;
      pendentes[id] = { ok: ok, falha: falha };
      ponte.postMessage({ id: id, m: m, a: a || {} });
    });
  }

  /// "casa:clima" (o tempo no widget) e a pagina Casa ja na aba Clima.
  function paginaPedida(p) {
    var m = /^casa:(clima|consumo|ajustes)$/.exec(p || "");
    if (!m) return p;
    casaAba = m[1];
    return "casa";
  }

  function avisar(m, a) { if (ponte) ponte.postMessage({ m: m, a: a || {} }); }

  function aoReceber(ev) {
    var d = ev.data;
    if (!d || typeof d !== "object") return;
    if (d.ev === "estado") {
      E = d.d;
      if (aoPrimeiroEstado) { var f = aoPrimeiroEstado; aoPrimeiroEstado = null; f(); }
      aoEstado();
      return;
    }
    if (d.ev === "ir") {
      // A bandeja, um aviso ou o widget pediram uma pagina com a janela ja aberta.
      var ir = paginaPedida(d.p);
      if (S && MONTAR[ir]) mostrar(ir);
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

  // ---------------- movimento (Motion.cs) ----------------
  // As mesmas duracoes e curvas da classica. Com as animacoes do Windows
  // desligadas (prefers-reduced-motion no WebView2) tudo vai direto ao fim.
  var MOV = { rapida: 83, media: 167, lenta: 250, padrao: "cubic-bezier(.4,0,.2,1)", entrada: "cubic-bezier(0,0,0,1)", saida: "cubic-bezier(1,0,1,1)" };
  var movReduzido = window.matchMedia ? window.matchMedia("(prefers-reduced-motion: reduce)") : null;
  function semMovimento() { return !!(movReduzido && movReduzido.matches) || typeof Element.prototype.animate !== "function"; }
  function animar(elm, quadros, ms, curva) {
    if (semMovimento() || !elm || !elm.isConnected) return null;
    try { return elm.animate(quadros, { duration: ms, easing: curva || MOV.padrao }); } catch (e) { return null; }
  }

  /// Abre ou fecha deslizando a altura, como o CollapsiblePanel: 250 ms,
  /// entrada suave ao abrir, saida ao fechar; virar no meio parte de onde esta.
  function deslizarAltura(elm, abrir) {
    var de = elm.hidden ? 0 : elm.getBoundingClientRect().height;
    if (elm._anim) { elm._anim.cancel(); elm._anim = null; }
    elm.hidden = false;
    var ate = abrir ? elm.getBoundingClientRect().height : 0;
    if (semMovimento() || !elm.isConnected || de === ate) { elm.hidden = !abrir; return; }
    elm.style.overflow = "hidden";
    elm.style.boxSizing = "border-box";
    var a = elm.animate([{ height: de + "px" }, { height: ate + "px" }], { duration: MOV.lenta, easing: abrir ? MOV.entrada : MOV.saida });
    elm._anim = a;
    a.onfinish = function () {
      elm._anim = null; elm.style.overflow = ""; elm.style.boxSizing = "";
      if (!abrir) elm.hidden = true;
    };
  }

  /// Tamanho de layout (sem transform): os graficos medem por aqui para nao
  /// nascerem 4% menores durante a entrada da pagina, que usa escala.
  function medida(e) { return { width: e.clientWidth, height: e.clientHeight, left: 0, top: 0 }; }

  // ---------------- entrada no estilo da Casa (Material 3) ----------------
  // As mesmas classes e tempos da pagina da Casa (servidor/app/static):
  // a pagina troca com fade-through e os cartoes sobem um a um (60 ms).
  function animarEntrada(e, classe) {
    if (!e || semMovimento()) return;
    e.classList.remove("m3-aba", "m3-surge", "m3-abre");
    void e.offsetWidth;
    e.classList.add(classe);
    e.addEventListener("animationend", function fim(ev) {
      if (ev.target !== e) return;
      e.classList.remove(classe); e.style.animationDelay = "";
      e.removeEventListener("animationend", fim);
    });
  }
  function entradaDaPagina(raiz) {
    if (semMovimento()) return;
    animarEntrada(raiz, "m3-aba");
    // Na abertura da janela a pagina entra inteira, ja com tudo: os cartoes
    // subindo um a um ali pareciam carregamento.
    if (primeiraEntrada) return;
    // Todos os itens a vista entram, na ordem da tela, 20 ms um do outro e
    // no maximo 160 ms de espera: a pagina inteira assenta em ~0,35 s, sem
    // item ja pronto ao lado de item ainda escondido.
    var cartoes = raiz.querySelectorAll(".cartao, .opcao, .grupo, .vg-cab, .mt-barra, .bat-topo, .vg-tiles > *");
    var alto = window.innerHeight, lista = [];
    for (var i = 0; i < cartoes.length; i++) {
      var c = cartoes[i];
      if (c.parentElement && c.parentElement.closest(".cartao")) continue;   // so os de primeiro nivel
      var r = c.getBoundingClientRect();
      if (r.bottom < 0 || r.top > alto) continue;                             // fora da vista nao anima
      lista.push({ c: c, y: Math.round(r.top), x: Math.round(r.left) });
    }
    lista.sort(function (a, b) { return a.y - b.y || a.x - b.x; });
    lista.forEach(function (o, n) {
      o.c.style.animationDelay = Math.min(n * 20, 160) + "ms";
      animarEntrada(o.c, "m3-surge");
    });
  }

  var aoPrimeiroEstado = null, primeiraEntrada = false;

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
    var nova = f.hidden;
    f.className = erro ? "faixa erro" : "faixa";
    f.hidden = false;
    if (nova) animarEntrada(f, "m3-abre");
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
    // Como no seletor da classica (SensorPicker): pilulas de categoria acima
    // da lista, cabecalho por categoria e o nome curto do sensor na linha,
    // com a peca em cinza para separar os de mesmo nome.
    var pilulas = el("div", "cats-sensor");
    var catAtual = "";
    function grupoDe(x) { return x.grupo || x.categoria || x.hardware || t("Outros", "Other"); }
    var todasCats = [];
    S.sensores.forEach(function (x) { var g = grupoDe(x); if (todasCats.indexOf(g) < 0) todasCats.push(g); });
    function desenharPilulas() {
      limpar(pilulas);
      [""].concat(todasCats).forEach(function (g) {
        var b = botao(g || t("Todos", "All"), "chip" + (g === catAtual ? " on" : ""), function () { catAtual = g; desenharPilulas(); desenhar(); });
        b.setAttribute("aria-pressed", g === catAtual ? "true" : "false");
        pilulas.appendChild(b);
      });
    }
    var c = cartao(null);
    c.corpo.appendChild(busca);
    if (todasCats.length > 1) c.corpo.appendChild(pilulas);
    c.corpo.appendChild(lista);
    var linhas = [];
    function desenhar() {
      limpar(lista);
      linhas = [];
      var termos = busca.value.trim().toLowerCase().split(/\s+/).filter(function (x) { return x; });
      var grupos = {}, ordem = [];
      S.sensores.forEach(function (x) {
        var g = grupoDe(x);
        if (catAtual && g !== catAtual) return;
        var txt = (x.rotulo + " " + x.nome + " " + x.hardware + " " + (x.grupo || "") + " " + (x.categoria || "")).toLowerCase();
        for (var k = 0; k < termos.length; k++) if (txt.indexOf(termos[k]) < 0) return;
        if (!grupos[g]) { grupos[g] = []; ordem.push(g); }
        grupos[g].push(x);
      });
      todasCats.filter(function (g) { return ordem.indexOf(g) >= 0; }).forEach(function (g) {
        if (!catAtual) lista.appendChild(el("div", "cat", g));
        grupos[g].forEach(function (x) {
          var nome = el("span", "nm", null, document.createTextNode(x.nome || x.rotulo));
          if (x.hardware) nome.appendChild(el("small", "", x.hardware));
          var r = el("div", "sn" + (x.id === escolhido ? " sel" : ""), null, nome, el("span", "vl", comUnidade(x.id)));
          r.setAttribute("role", "option");
          r.title = x.rotulo;
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
    desenharPilulas();
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
    acompanharLargura();
    if (S.barra) S.barra.recolhida = v;
    pedir("barra", { recolhida: v }).catch(function () { });
  }

  /// Enquanto a barra desliza, a pagina ao lado muda de largura a cada
  /// quadro: graficos e a area da Casa sao refeitos junto (como na classica,
  /// que repinta as paginas no deslize), em vez de esticar e saltar no fim.
  var largura = 0;
  function acompanharLargura() {
    cancelAnimationFrame(largura);
    var fim = performance.now() + (semMovimento() ? 0 : 300);
    (function quadro() {
      window.dispatchEvent(new Event("resize"));
      if (performance.now() < fim) largura = requestAnimationFrame(quadro);
    })();
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
    alvo.className = pagina === "casa" ? "casa" : pagina === "visao" ? "visao" : "";
    if (mudou && !mesma) faixa("");
    if (pagina !== "casa") avisar("casa.area", { visivel: false, x: 0, y: 0, w: 0, h: 0 });
    var dentro = pagina === "casa" || pagina === "visao" ? alvo : el("div", "largura");
    if (dentro !== alvo) alvo.appendChild(dentro);
    vista = MONTAR[pagina](dentro) || {};
    alvo.scrollTop = rolagem;
    if (E && vista.atualizar) vista.atualizar();
    // A Casa e a pagina do servidor numa camada propria, com as animacoes dela.
    if (mudou && !mesma && pagina !== "casa") entradaDaPagina(alvo);
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
        var r = medida(caixa);
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
    var r = medida(canvas), dpr = window.devicePixelRatio || 1;
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
    // Como na classica: falha curta (ate 2 leituras) segue a linha entre as
    // vizinhas, sem quebrar; falha longa vira uma ponte tracejada e o
    // preenchimento para ali, em vez de a curva sumir e reaparecer.
    var trechos = [], atual = null, vazio = 0;
    for (var i = 0; i < n; i++) {
      if (pontos[i] === null || pontos[i] === undefined) { vazio++; continue; }
      if (atual && vazio <= 2) atual.push(i);
      else { atual = [i]; trechos.push(atual); }
      vazio = 0;
    }
    g.lineJoin = "round";
    trechos.forEach(function (tr) {
      if (tr.length < 2) return;
      g.beginPath();
      g.moveTo(x(tr[0]), base);
      tr.forEach(function (k) { g.lineTo(x(k), y(pontos[k])); });
      g.lineTo(x(tr[tr.length - 1]), base); g.closePath();
      g.fillStyle = cheio; g.fill();
      g.beginPath();
      tr.forEach(function (k, j) { if (j === 0) g.moveTo(x(k), y(pontos[k])); else g.lineTo(x(k), y(pontos[k])); });
      g.strokeStyle = cor; g.lineWidth = 1.2 * dpr; g.stroke();
    });
    for (var t2 = 1; t2 < trechos.length; t2++) {
      var de = trechos[t2 - 1][trechos[t2 - 1].length - 1], ate = trechos[t2][0];
      g.beginPath();
      g.setLineDash([3 * dpr, 3 * dpr]);
      g.moveTo(x(de), y(pontos[de])); g.lineTo(x(ate), y(pontos[ate]));
      g.strokeStyle = cor + "aa"; g.lineWidth = dpr; g.stroke();
      g.setLineDash([]);
    }
    g.beginPath();
    g.setLineDash([4 * dpr, 4 * dpr]);
    g.moveTo(0, y(media)); g.lineTo(w, y(media));
    g.strokeStyle = cor + "99"; g.lineWidth = dpr; g.stroke();
    g.setLineDash([]);
    return { max: max, media: media, lo: lo, hi: hi, topo: topo, base: base, n: n, w: w, h: h, dpr: dpr };
  }

  /// O ponto sob o ponteiro, como o DesenharDetalhe da classica: linha
  /// vertical, ponto na curva e o balao com o valor e a hora. Numa lacuna
  /// longa o balao diz quanto tempo ficou sem leitura em vez de inventar
  /// numero; numa curta, segue a linha entre as leituras vizinhas.
  function detalheNaCurva(canvas, pontos, corHex, geo, hx, fimMs, passoS, janelaS, unidade) {
    if (!geo || !pontos || geo.n < 2 || hx < 0) return;
    var g = canvas.getContext("2d"), dpr = geo.dpr, n = geo.n;
    var i = Math.round(hx * dpr / geo.w * (n - 1));
    i = Math.max(0, Math.min(n - 1, i));
    var v = pontos[i], semLeitura = null;
    if (v === null || v === undefined) {
      var a = i, b = i;
      while (a >= 0 && pontos[a] === null) a--;
      while (b < n && pontos[b] === null) b++;
      if (a < 0 || b >= n) return;
      if (b - a - 1 > 2) {
        var seg = (b - a - 1) * passoS;
        semLeitura = t("sem leitura por ", "no reading for ") + (seg >= 3600 ? (seg / 3600).toFixed(1).replace(".", S.pt ? "," : ".") + " h" : seg >= 60 ? Math.round(seg / 60) + " min" : seg + " s");
      }
      v = pontos[a] + (pontos[b] - pontos[a]) * (i - a) / (b - a);
    }
    var x = i * (geo.w - 1) / (n - 1), y = geo.base - (v - geo.lo) / (geo.hi - geo.lo) * (geo.base - geo.topo);
    g.save();
    g.strokeStyle = corHex + "46"; g.lineWidth = dpr;
    g.beginPath(); g.moveTo(x, geo.topo - 8 * dpr); g.lineTo(x, geo.base); g.stroke();
    g.beginPath(); g.arc(x, y, 3 * dpr, 0, Math.PI * 2);
    g.fillStyle = corHex; g.fill();
    g.strokeStyle = cor("--surface"); g.lineWidth = 1.5 * dpr; g.stroke();
    var quando = new Date(fimMs - (n - 1 - i) * passoS * 1000);
    var hora = quando.toLocaleTimeString(S.pt ? "pt-BR" : "en-US", janelaS <= 600 ? { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false } : { hour: "2-digit", minute: "2-digit", hour12: false });
    var txt = (semLeitura || numero(v) + (unidade ? " " + unidade : "")) + "   " + hora;
    g.font = (12 * dpr) + "px " + getComputedStyle(document.body).fontFamily;
    var bw = g.measureText(txt).width + 14 * dpr, bh = 22 * dpr;
    var bx = Math.round(x - bw / 2), by = Math.round(y - bh - 10 * dpr);
    if (by < 2 * dpr) by = Math.round(y + 10 * dpr);
    if (by + bh > geo.h - 2 * dpr) by = geo.h - 2 * dpr - bh;
    bx = Math.max(2 * dpr, Math.min(geo.w - 2 * dpr - bw, bx));
    var r = 4 * dpr;
    g.beginPath();
    g.moveTo(bx + r, by); g.arcTo(bx + bw, by, bx + bw, by + bh, r); g.arcTo(bx + bw, by + bh, bx, by + bh, r);
    g.arcTo(bx, by + bh, bx, by, r); g.arcTo(bx, by, bx + bw, by, r); g.closePath();
    g.fillStyle = cor("--window"); g.fill();
    g.strokeStyle = cor("--border"); g.lineWidth = dpr; g.stroke();
    g.fillStyle = cor("--text"); g.textBaseline = "middle";
    g.fillText(txt, bx + 7 * dpr, by + bh / 2 + 0.5 * dpr);
    g.restore();
  }

  function mm(f) { return f ? t("máx ", "max ") + numero(f.max) + " · " + t("méd ", "avg ") + numero(f.media) : ""; }

  MONTAR.visao = function (pagina) {
    // Como na classica: largura maxima de 1100, centrada, e no meio da
    // altura quando sobra espaco.
    var alvo = el("div", "vg");
    pagina.appendChild(alvo);
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
      var b = { id: d.id, d: d, canvas: canvas, valor: valor, faixa: faixa, cor: "", hx: -1 };
      seguirPonteiro(c, b);
      blocos.push(b);
    });
    alvo.appendChild(tiles);
    // Os destaques numa fileira so; com mais de 3 e pouca largura, em duas.
    function arranjarTiles() {
      var n = blocos.length;
      if (!n) { tiles.style.display = "none"; return; }
      var w = tiles.clientWidth || alvo.clientWidth;
      var porFila = n;
      if (n > 3 && (w - 12 * (n - 1)) / n < 150) porFila = Math.ceil(n / 2);
      tiles.style.gridTemplateColumns = "repeat(" + porFila + ", minmax(0, 1fr))";
    }

    var meio = el("div", "vg-meio"), esq = el("div", "vg-esq"), dir = el("div", "vg-dir");
    // A casa: o cartao do ar, o do tempo, o da lavadora durante um ciclo, ou o convite.
    // Ordem: ar, lavadora, tempo e proximos dias na coluna da esquerda; Perifericos e Eventos
    // ficam embaixo do cooler, na coluna da direita.
    var casa = cartoesDaCasa();
    esq.appendChild(casa.el);
    var duas = el("div", "vg-duas");
    var perLista = el("div");
    var per = el("div", "cartao vg-box vai", null, el("div", "cb", null, el("b", "", T("VisaoChipPerifericos", t("Periféricos", "Peripherals"))),
      botao(T("VisaoBaterias", t("Baterias ▸", "Batteries ▸")), "link", function () { mostrar("baterias"); })), perLista);
    aoClicar(per, function () { mostrar("baterias"); });
    var evLista = el("div", "vg-ev");
    var ev = el("div", "cartao vg-box", null, el("div", "cb", null, el("span", "mono", T("VisaoEventos", t("EVENTOS", "EVENTS")))), evLista);
    duas.appendChild(per); duas.appendChild(ev);
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
      var m = { n: n, canvas: canvas, tt: tt, valor: valor, faixa: faixa, id: "", hx: -1 };
      seguirPonteiro(c, m);
      return m;
    });
    dir.appendChild(cooler);
    dir.appendChild(duas);
    meio.appendChild(dir);
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

    // Ponteiro sobre um grafico: o valor e a hora daquele ponto, como nas Metricas.
    function seguirPonteiro(caixa, alvoDoPonto) {
      caixa.addEventListener("pointermove", function (e) {
        var hx = e.clientX - alvoDoPonto.canvas.getBoundingClientRect().left;
        if (hx === alvoDoPonto.hx) return;
        alvoDoPonto.hx = hx; pintar();
      });
      caixa.addEventListener("pointerleave", function () { alvoDoPonto.hx = -1; pintar(); });
    }
    function detalhe(x, unidade) {
      if (x.hx >= 0) detalheNaCurva(x.canvas, serie[x.id], x.hex, x.f, x.hx, Date.now(), S.passo || 5, 300, unidade);
    }

    function pintar() {
      blocos.forEach(function (b) {
        b.hex = corDoValor(b.d.unidade, leitura(b.id), b.d.atencao, b.d.perigo).replace("var(--danger)", "#EB5A4B").replace("var(--warn)", "#F0AF3C").replace("var(--quente)", cor("--quente")).replace("var(--accent-text)", cor("--accent-text"));
        b.f = desenharCurva(b.canvas, serie[b.id], b.hex);
        detalhe(b, b.d.unidade);
        caber(b);
      });
      mostradores.forEach(function (m) {
        var s = sensor(m.id);
        m.hex = m.n === 1 ? cor("--quente") : cor("--accent-text");
        m.f = desenharCurva(m.canvas, serie[m.id], m.hex);
        detalhe(m, s ? s.unidade : "");
        m.faixa.textContent = mm(m.f);
      });
    }

    // Como no cartao compacto da classica: "max · med" a direita do valor;
    // sem espaco, so o "max"; sem espaco nem para isso, nada.
    function caber(b) {
      var f = b.f, formas = f ? [mm(f), t("máx ", "max ") + numero(f.max)] : [];
      for (var i = 0; i < formas.length; i++) {
        b.faixa.textContent = formas[i];
        if (b.valor.offsetLeft + b.valor.offsetWidth + 10 <= b.faixa.offsetLeft) return;
      }
      b.faixa.textContent = "";
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
    var redesenhar = function () { arranjarTiles(); pintar(); previa.desenhar(); };
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
          caber(b);
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
        casa.definir(E.vcasa);
        arranjarTiles();
      },
      sair: function () {
        vivo = false; clearInterval(relogio);
        window.removeEventListener("resize", redesenhar);
        previa.sair();
        casa.sair();
      }
    };
  };

  /// A casa na Visao geral, igual a classica: o ar com - e + (espera 700 ms
  /// antes de enviar) e a pilula de liga/desliga; a lavadora so durante um
  /// ciclo, com o tempo, o fim e as etapas; o convite quando nao ha nada.
  // Icones do tempo em traco (os mesmos da pagina da Casa), com cor por parte: sol, lua, nuvem, gota, raio.
  var NUV = ["path", { "class": "n", d: "M7 18.5h10a4 4 0 0 0 .6-7.95A5.5 5.5 0 0 0 7 11.6 3.45 3.45 0 0 0 7 18.5z" }];
  var NUV_ALTA = ["path", { "class": "n", d: "M7 15h10a4 4 0 0 0 .6-7.95A5.5 5.5 0 0 0 7 8.1 3.45 3.45 0 0 0 7 15z" }];
  var NUV_BAIXA = ["path", { "class": "n", d: "M10.5 20.5h7a3.5 3.5 0 0 0 .5-6.96 4.8 4.8 0 0 0-9.1 1.06 2.95 2.95 0 0 0 1.6 5.9z" }];
  var ICONES_TEMPO = {
    sol: [["circle", { "class": "s", cx: 12, cy: 12, r: 4.2 }], ["path", { "class": "s", d: "M12 2.8v2.2M12 19v2.2M2.8 12H5M19 12h2.2M5.5 5.5 7 7M17 17l1.5 1.5M18.5 5.5 17 7M7 17l-1.5 1.5" }]],
    lua: [["path", { "class": "l", d: "M19.5 14.6A7.6 7.6 0 0 1 9.4 4.5a7.6 7.6 0 1 0 10.1 10.1z" }]],
    "sol-nuvem": [["circle", { "class": "s", cx: 8.5, cy: 8, r: 3 }], ["path", { "class": "s", d: "M8.5 2.2v1.2M2.7 8h1.2M4.4 3.9l.9.9M12.6 3.9l-.9.9M4.4 12.1l.9-.9" }], NUV_BAIXA],
    "lua-nuvem": [["path", { "class": "l", d: "M12.3 9.2A4.6 4.6 0 0 1 6.3 3.2a4.6 4.6 0 1 0 6 6z" }], NUV_BAIXA],
    nuvem: [NUV],
    neblina: [NUV_ALTA, ["path", { "class": "n", d: "M4 18h16M6 21h12" }]],
    garoa: [NUV_ALTA, ["path", { "class": "g", d: "M8.5 18.2v1M12 19v1M15.5 18.2v1" }]],
    chuva: [NUV_ALTA, ["path", { "class": "g", d: "M8.2 17.5l-1 2.6M12.2 17.5l-1 2.6M16.2 17.5l-1 2.6" }]],
    "chuva-forte": [NUV_ALTA, ["path", { "class": "g", d: "M7 17.5l-1.4 3.6M10.4 17.5 9 21.1M13.8 17.5l-1.4 3.6M17.2 17.5l-1.4 3.6" }]],
    tempestade: [NUV_ALTA, ["path", { "class": "r", d: "M12.8 15.5 10.6 19h3l-2.2 3.5" }]],
    neve: [NUV_ALTA, ["path", { "class": "g", d: "M8 18.5h.01M12 19.5h.01M16 18.5h.01M10 21.5h.01M14 21.5h.01" }]]
  };
  function iconeDoTempo(nome) {
    var s = svg(ICONES_TEMPO[nome] || ICONES_TEMPO.nuvem, "0 0 24 24");
    s.setAttribute("class", "tic");
    return s;
  }

  /// Um cartao inteiro leva a outra pagina; o clique num botao ou link dentro dele fica com o botao.
  function aoClicar(cartao, fazer) {
    cartao.addEventListener("click", function (e) { if (!e.target.closest("button, a, input")) fazer(); });
  }

  function cartoesDaCasa() {
    var raiz = el("div", "vg-casa-pilha");
    var ultimo = null, alvoLocal = null, espera = 0, ar = null;

    // O ar.
    var arNome = el("b"), arFora = el("small"), arNum = el("span", "alvo");
    var menos = botao("\uE738", "redondo icone", function () { piscar(menos); passo(-1); });
    var mais = botao("\uE710", "redondo icone", function () { piscar(mais); passo(1); });
    menos.setAttribute("aria-label", t("Diminuir", "Lower"));
    mais.setAttribute("aria-label", t("Aumentar", "Raise"));
    var pilula = botao("", "pilula", function () {
      if (!ar || !ar.online) return;
      var novo = !ar.ligado;
      ar.ligado = novo; ar.ajusta = novo && ar.alvo !== null;
      desenharAr();
      enviar("t_power", novo ? 1 : 0);
    });
    var anel = el("span", "anel"), arIc = el("span", "ic icone", null, document.createTextNode("\uE9CA"), anel);
    var cartaoAr = el("div", "cartao vg-ar vai", null, arIc,
      el("div", "ct", null, arNome, arFora), el("div", "ctl", null, menos, arNum, mais), pilula);

    // O - e o + piscam ao clique (FlashDoBotao, 150 ms).
    function piscar(b) {
      if (b.disabled) return;
      animar(b, [{ backgroundColor: cor("--accent"), color: "#fff" }, { backgroundColor: "transparent" }], 150, "linear");
    }
    // Ligar: um anel azul cresce ate 1,6x e some, e a borda do cartao acende
    // e esmaece (700 ms). Desligar: um anel cinza se fecha sobre o icone.
    var ligadoAntes = null, numAntes = null;
    function animarLigado(ligado) {
      if (ligadoAntes === null || ligadoAntes === ligado) { ligadoAntes = ligado; return; }
      ligadoAntes = ligado;
      // virar no meio recomeca do zero, como a classica
      [anel, cartaoAr].forEach(function (x) { if (x.getAnimations) x.getAnimations().forEach(function (y) { y.cancel(); }); });
      if (ligado) {
        animar(anel, [{ transform: "scale(1)", opacity: 0.7, borderColor: cor("--accent") }, { transform: "scale(1.6)", opacity: 0, borderColor: cor("--accent") }], 700, MOV.entrada);
        animar(cartaoAr, [{ boxShadow: "0 0 0 2.5px " + cor("--accent") }, { boxShadow: "0 0 0 2.5px transparent" }], 700, MOV.entrada);
      } else {
        animar(anel, [{ transform: "scale(1.6)", opacity: 0, borderColor: cor("--muted") }, { transform: "scale(1)", opacity: 0.66, borderColor: cor("--muted"), offset: 0.85 }, { transform: "scale(1)", opacity: 0, borderColor: cor("--muted") }], 450, MOV.padrao);
      }
    }
    // O alvo desliza ao mudar (AnimacaoDoAr, 300 ms): ao subir, o novo entra
    // por baixo; ao descer, por cima.
    function deslizarNumero(v) {
      if (numAntes === null || v === null || v === numAntes) { numAntes = v; return; }
      var dir = v > numAntes ? 1 : -1;
      numAntes = v;
      animar(arNum, [{ transform: "translateY(" + (dir * 45) + "%)", opacity: 0 }, { transform: "none", opacity: 1 }], 300, MOV.entrada);
    }

    function passo(d) {
      if (!ar || !ar.ajusta) return;
      var base = alvoLocal !== null ? alvoLocal : ar.alvo;
      if (base === null) return;
      alvoLocal = Math.max(ar.min, Math.min(ar.max, base + d));
      desenharAr();
      clearTimeout(espera);
      espera = setTimeout(function () {
        var v = alvoLocal; alvoLocal = null;
        if (ar) ar.alvo = v;
        enviar("t_temp", v);
      }, 700);
    }
    function enviar(chave, valor) {
      if (!ar) return;
      pedir("casa.ar", { puid: ar.puid, chave: chave, valor: valor }).then(function (v) { if (v) definir(v); }, function () { });
    }
    function desenharAr() {
      arNome.textContent = ar.nome;
      limpar(arFora);
      arFora.appendChild(document.createTextNode((ar.fora || "") + " "));
      if (ar.clima) arFora.appendChild(el("span", "quente", ar.clima));
      if (ar.origem) arFora.appendChild(document.createTextNode(" · " + ar.origem));
      var v = alvoLocal !== null ? alvoLocal : ar.alvo;
      cartaoAr.classList.toggle("on", !!(ar.online && ar.ligado));
      animarLigado(!!(ar.online && ar.ligado));
      deslizarNumero(v === undefined ? null : v);
      limpar(arNum);
      arNum.appendChild(document.createTextNode(v === null || v === undefined ? "--" : String(v)));
      arNum.appendChild(el("small", "", "°"));
      arNum.classList.toggle("apagado", !ar.ajusta);
      var podeMenos = ar.ajusta && v !== null && v > ar.min, podeMais = ar.ajusta && v !== null && v < ar.max;
      menos.disabled = !podeMenos; mais.disabled = !podeMais;
      pilula.textContent = ar.liga || "";
      pilula.disabled = !ar.online;
      pilula.className = "pilula" + (!ar.online ? " fora" : ar.ligado ? " on" : "");
    }

    // A lavadora.
    var lavNome = el("b"), lavLinha = el("small"), lavTempo = el("span", "tempo"), lavFim = el("small", "fim");
    var etapas = el("div", "etapas");
    var cartaoLav = el("div", "cartao vg-lav vai", null,
      el("div", "topo", null, el("span", "ic maquina", null, el("i")), el("div", "ct", null, lavNome, lavLinha),
        el("div", "rel", null, lavTempo, lavFim)), etapas);
    function desenharLav(l) {
      lavNome.textContent = l.nome;
      lavLinha.textContent = l.erro ? l.erro : l.linha;
      lavLinha.classList.toggle("erro", !!l.erro);
      lavTempo.textContent = l.tempo;
      lavFim.textContent = l.fim;
      // Um segmento por etapa, com o nome embaixo: as feitas cheias, a atual pela fracao do ciclo, as demais vazias.
      var n = (l.etapas || []).length || 1;
      etapas.className = "etapas" + (l.pausada ? " pausada" : "");
      etapas.style.gridTemplateColumns = "repeat(" + n + ", minmax(0, 1fr))";
      limpar(etapas);
      (l.etapas || []).forEach(function (e, i) {
        var f = i < l.atual ? 1 : i === l.atual ? Math.max(0.08, Math.min(1, (l.prog || 0) * n - i)) : 0;
        var seg = el("i"); seg.style.width = Math.round(f * 100) + "%";
        etapas.appendChild(el("div", "et " + (i < l.atual ? "feita" : i === l.atual ? "agora" : ""), null, el("span", "eseg", null, seg), el("small", "", e)));
      });
    }

    // A lavadora parada: ocupa o lugar do cartao de ciclo (mesma altura), com o ultimo ciclo e o mes.
    var lrNome = el("b"), lrLinha = el("small"), lrEstado = el("span", "tempo"), lrMes = el("div", "mes");
    var cartaoLavRepouso = el("div", "cartao vg-lav repouso vai", null,
      el("div", "topo", null, el("span", "ic maquina", null, el("i")), el("div", "ct", null, lrNome, lrLinha), el("div", "rel", null, lrEstado)), lrMes);
    aoClicar(cartaoLavRepouso, function () { mostrar("casa"); });
    function desenharLavRepouso(l) {
      lrNome.textContent = l.nome;
      lrEstado.textContent = l.online ? t("Parada", "Idle") : t("Offline", "Offline");
      lrEstado.classList.toggle("fora", !l.online);
      var u = l.ultimo;
      lrLinha.textContent = u ? t("Último ciclo: ", "Last cycle: ") + (u.programa ? u.programa + " · " : "") +
        (u.min < 60 ? u.min + " min" : Math.floor(u.min / 60) + " h " + ("0" + (u.min % 60)).slice(-2)) + " · " + u.fim + (u.concluido ? "" : t(" (interrompido)", " (interrupted)"))
        : t("Nenhum ciclo registrado ainda", "No cycle recorded yet");
      limpar(lrMes);
      [[l.mes[0], t("ciclos no mês", "cycles this month")], [l.mes[1] + " L", t("de água", "of water")], [String(l.mes[2]).replace(".", t(",", ".")) + " kWh", t("de energia", "of energy")]].forEach(function (c) {
        lrMes.appendChild(el("div", "", null, el("b", "", String(c[0])), el("small", "", c[1])));
      });
    }

    // O convite.
    var cvTitulo = el("b"), cvTexto = el("small");
    var convite = el("div", "cartao vg-casa", null, el("span", "ic icone", "\uE80F"), el("div", "ct", null, cvTitulo, cvTexto),
      botao(T("VisaoAbrirCasa", t("Abrir Casa ▸", "Open Home ▸")), "link", function () { mostrar("casa"); }));
    aoClicar(cartaoAr, function () { mostrar("casa"); });
    aoClicar(cartaoLav, function () { mostrar("casa"); });

    // A sugestao de conforto do servidor (clima e ASHRAE 55), com Aplicar quando ha o que mandar ao ar.
    var sugTitulo = el("b"), sugTexto = el("small");
    var sugAplicar = botao(t("Aplicar", "Apply"), "b", function () {
      sugAplicar.disabled = true;
      pedir("casa.sugestao", {}).then(function (v) { sugAplicar.disabled = false; if (v) definir(v); }, function () { sugAplicar.disabled = false; });
    });
    var sugOk = el("span", "sug-ok", t("✓ Aplicada", "✓ Applied"));
    var cartaoSug = el("div", "cartao vg-casa vg-sug vai", null, el("span", "ic icone", "\uE706"), el("div", "ct", null, sugTitulo, sugTexto), sugAplicar, sugOk);

    // O tempo da cidade da casa (do servidor): agora, maxima e minima, AQI e as proximas horas.
    // Com tempo, a sugestao entra no mesmo cartao, abaixo de uma linha, em vez de um cartao a parte.
    aoClicar(cartaoSug, function () { mostrar("casa"); });
    var tpIc = el("span", "ic"), tpTitulo = el("b"), tpLinha = el("small"), tpHoras = el("div", "horas"), tpDias = el("div", "dias");
    var tpSug = el("div", "sug");
    var cartaoTempo = el("div", "cartao vg-tempo vai", null,
      el("div", "topo", null, tpIc, el("div", "ct", null, tpTitulo, tpLinha), tpHoras), tpSug);
    var tpDiasTit = el("div", "tt");
    var cartaoDias = el("div", "cartao vg-dias vai", null, tpDiasTit, tpDias);
    function abrirClima() { casaAba = "clima"; mostrar("casa"); }
    cartaoTempo.title = cartaoDias.title = t("Abrir o clima", "Open weather");
    aoClicar(cartaoTempo, abrirClima);
    aoClicar(cartaoDias, abrirClima);
    var SEMANA = S && !S.pt ? ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"] : ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];
    function graus(v) { return v === null || v === undefined ? "--" : v + "°"; }
    // Aplicavel e ainda nao aplicada: o botao. Ja aplicada (o ar bate com a cena): so o indicador, sem botao.
    function mostrarAplicar(sug) {
      sugAplicar.style.display = sug.aplicavel && !sug.aplicada ? "" : "none";
      sugOk.style.display = sug.aplicavel && sug.aplicada ? "" : "none";
    }
    function desenharTempo(tp, sug) {
      limpar(tpIc); tpIc.appendChild(iconeDoTempo(tp.icone));
      tpTitulo.textContent = graus(tp.temp) + "  " + tp.texto;
      limpar(tpLinha);
      tpLinha.appendChild(document.createTextNode(tp.local + " · " + t("Máx ", "High ") + graus(tp.max) + t("  Mín ", "  Low ") + graus(tp.min)));
      if (tp.aqi !== null && tp.aqi !== undefined) {
        var p = el("span", "aqi", null, el("i"));
        p.firstChild.style.background = tp.aqiCor;
        p.appendChild(document.createTextNode("AQI " + tp.aqi + (tp.aqiTexto ? " " + tp.aqiTexto : "")));
        tpLinha.appendChild(document.createTextNode(" · "));
        tpLinha.appendChild(p);
      }
      tpLinha.title = tpLinha.textContent;
      limpar(tpHoras);
      (tp.horas || []).forEach(function (h) {
        tpHoras.appendChild(el("div", "", null, el("small", "", h.hora), iconeDoTempo(h.icone), el("b", "", graus(h.temp))));
      });
      // Os proximos dias com a barra de minima a maxima na mesma escala, como no app Tempo.
      limpar(tpDias);
      var dias = tp.dias || [], lo = Infinity, hi = -Infinity;
      dias.forEach(function (d) { lo = Math.min(lo, d.min); hi = Math.max(hi, d.max); });
      var faixa = Math.max(1, hi - lo);
      dias.forEach(function (d, i) {
        var p = d.dia.split("-"), dt = new Date(+p[0], +p[1] - 1, +p[2]);
        var barra = el("span", "bar", null, el("i"));
        barra.firstChild.style.left = ((d.min - lo) / faixa * 100) + "%";
        barra.firstChild.style.right = ((hi - d.max) / faixa * 100) + "%";
        tpDias.appendChild(el("div", "dia", null, el("span", "nome", i === 0 ? t("Hoje", "Today") : SEMANA[dt.getDay()]),
          el("span", "ico", null, iconeDoTempo(d.icone), d.prob >= 30 ? el("small", "", d.prob + "%") : null),
          el("span", "mn", graus(d.min)), barra, el("span", "mx", graus(d.max))));
      });
      tpDiasTit.textContent = t("PRÓXIMOS DIAS · ", "NEXT DAYS · ") + tp.local;
      limpar(tpSug);
      tpSug.style.display = sug ? "" : "none";
      if (sug) {
        tpSug.appendChild(el("div", "ct", null, el("b", "", sug.titulo), el("small", "", sug.texto)));
        tpSug.lastChild.lastChild.title = sug.texto;
        mostrarAplicar(sug);
        tpSug.appendChild(sugAplicar);
        tpSug.appendChild(sugOk);
      }
    }

    function definir(v) {
      v = v || { convite: { titulo: T("VisaoConecteCasa", t("Conecte a casa", "Connect your home")), texto: "" } };
      var chave = JSON.stringify(v);
      if (chave === ultimo) return;
      ultimo = chave;
      limpar(raiz);
      ar = v.ar ? JSON.parse(JSON.stringify(v.ar)) : null;
      if (ar) { desenharAr(); raiz.appendChild(cartaoAr); }
      var sug = ar ? ar.sug : null;
      if (v.tempo) desenharTempo(v.tempo, sug);
      if (!v.tempo && sug) {
        cartaoSug.appendChild(sugAplicar);
        sugTitulo.textContent = ar.sug.titulo;
        sugTexto.textContent = ar.sug.texto;
        sugTexto.title = ar.sug.texto;
        mostrarAplicar(ar.sug);
        cartaoSug.appendChild(sugOk);
        raiz.appendChild(cartaoSug);
      }
      if (v.lav) { desenharLav(v.lav); raiz.appendChild(cartaoLav); }
      else if (v.lavRepouso) { desenharLavRepouso(v.lavRepouso); raiz.appendChild(cartaoLavRepouso); }
      if (v.tempo) raiz.appendChild(cartaoTempo);
      if (v.tempo && v.tempo.dias && v.tempo.dias.length) raiz.appendChild(cartaoDias);
      if (v.convite || (!ar && !v.lav && !v.lavRepouso)) {
        var c = v.convite || {};
        cvTitulo.textContent = c.titulo || T("NavCasa", t("Casa", "Home"));
        cvTexto.textContent = c.texto || "";
        raiz.appendChild(convite);
      }
    }
    return { el: raiz, definir: function (v) { if (alvoLocal === null) definir(v); }, sair: function () { clearTimeout(espera); } };
  }

  // ---------------- Baterias ----------------

  var VERDE = "#2EA043";

  /// A cor do nivel: vermelho ate 10%, amarelo ate 30%, destaque acima; verde no cabo.
  function corDaBateria(pct, carregando) {
    if (pct === null || pct === undefined) return cor("--muted");
    if (carregando) return VERDE;
    return pct <= 10 ? "#EB5A4B" : pct <= 30 ? "#F0AF3C" : cor("--accent");
  }

  /// O icone real do jogo (vem do app como PNG) ou o glifo de controle.
  function iconeDoJogo(caixa, imagem) {
    limpar(caixa);
    caixa.classList.toggle("icone", !imagem);
    if (imagem) { var im = el("img"); im.alt = ""; im.src = imagem; caixa.appendChild(im); }
    else caixa.textContent = "";
    return caixa;
  }

  function pilhaDesenhada(pct, c, carregando) {
    var corpo = el("span", "pilha" + (carregando ? " carga" : ""));
    var nivel = el("i");
    nivel.style.width = Math.max(0, Math.min(100, pct)) + "%";
    nivel.style.background = c;
    corpo.appendChild(nivel);
    return el("span", "pilha-caixa", null, corpo, el("span", "pilha-ponta"));
  }

  /// A carga das ultimas 48 h: faixa verde nas horas no cabo, curva que se
  /// interrompe nas lacunas (com ponte tracejada) e um ponto em "agora".
  function desenharCurvaDaBateria(canvas, d, c) {
    var r = medida(canvas), dpr = window.devicePixelRatio || 1;
    var W = Math.max(10, Math.round(r.width)), H = Math.max(10, Math.round(r.height));
    if (canvas.width !== W * dpr) canvas.width = W * dpr;
    if (canvas.height !== H * dpr) canvas.height = H * dpr;
    var g = canvas.getContext("2d");
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.clearRect(0, 0, W, H);
    var ax = 40, ay = 6, aw = W - 40 - 6, ah = H - 6 - 20, janela = 48 * 60;
    g.font = "12px 'Segoe UI Variable Small','Segoe UI',sans-serif";
    g.fillStyle = cor("--faint"); g.textBaseline = "middle"; g.textAlign = "right";
    [0, 50, 100].forEach(function (n) {
      var y = Math.round(ay + ah - ah * n / 100) + 0.5;
      g.beginPath(); g.setLineDash(n === 50 ? [3, 4] : []);
      g.moveTo(ax, y); g.lineTo(ax + aw, y);
      g.strokeStyle = cor("--border"); g.lineWidth = 1; g.stroke();
      g.fillText(n + "%", ax - 6, y);
    });
    g.setLineDash([]);
    g.textBaseline = "top";
    g.textAlign = "left"; g.fillText(t("48 h atrás", "48 h ago"), ax, ay + ah + 5);
    g.textAlign = "center"; g.fillText(t("24 h atrás", "24 h ago"), ax + aw / 2, ay + ah + 5);
    g.textAlign = "right"; g.fillText(T("Now", t("agora", "now")), ax + aw, ay + ah + 5);
    function X(min) { return ax + (1 - min / janela) * aw; }
    function Y(p) { return ay + ah - ah * p / 100; }
    var am = d.pontos || [];
    g.fillStyle = "rgba(46,160,67," + (S.escuro ? 0.133 : 0.157) + ")";
    for (var i = 0; i < am.length; i++) {
      if (!am[i][2]) continue;
      var ate = i + 1 < am.length ? am[i + 1][0] : 0;
      var de = am[i][0];
      if (i > 0 && !am[i - 1][2] && (am[i - 1][0] - de) <= 30) de = am[i - 1][0];
      var x0 = Math.max(ax, X(de)), x1 = Math.min(ax + aw, X(ate));
      if (x1 > x0) g.fillRect(x0, ay, x1 - x0, ah);
    }
    var trechos = [], pts = [], anterior = null;
    am.forEach(function (s) {
      if (anterior !== null && (anterior - s[0]) > 30 && pts.length) { trechos.push(pts); pts = []; }
      pts.push([X(s[0]), Y(s[1])]);
      anterior = s[0];
    });
    var chegaAgora = d.pct !== null && anterior !== null && anterior <= 30;
    if (chegaAgora) pts.push([X(0), Y(d.pct)]);
    if (pts.length) trechos.push(pts);
    if (!trechos.length) return;
    trechos.forEach(function (tr) {
      if (tr.length < 2) return;
      var gr = g.createLinearGradient(0, ay, 0, ay + ah);
      gr.addColorStop(0, c + "55"); gr.addColorStop(1, c + "08");
      g.beginPath(); g.moveTo(tr[0][0], ay + ah);
      tr.forEach(function (p) { g.lineTo(p[0], p[1]); });
      g.lineTo(tr[tr.length - 1][0], ay + ah); g.closePath(); g.fillStyle = gr; g.fill();
    });
    g.lineJoin = "round"; g.lineWidth = 2; g.strokeStyle = c;
    trechos.forEach(function (tr) {
      g.beginPath();
      tr.forEach(function (p, k) { if (k) g.lineTo(p[0], p[1]); else g.moveTo(p[0], p[1]); });
      g.stroke();
    });
    g.setLineDash([4, 4]); g.strokeStyle = c + "78"; g.lineWidth = 1.6;
    for (i = 1; i < trechos.length; i++) {
      var a = trechos[i - 1][trechos[i - 1].length - 1], b = trechos[i][0];
      g.beginPath(); g.moveTo(a[0], a[1]); g.lineTo(b[0], b[1]); g.stroke();
    }
    g.setLineDash([]);
    if (chegaAgora) {
      var u = pts[pts.length - 1];
      g.beginPath(); g.arc(u[0], u[1], 3.5, 0, Math.PI * 2); g.fillStyle = c; g.fill();
    }
  }

  function cartaoDeBateria(d, aoMudarAltura) {
    var c = corDaBateria(d.pct, d.carregando);
    var card = el("div", "cartao bat-card");
    var img = el("img", "bat-img");
    if (d.imagem) { img.src = d.imagem; img.alt = ""; }
    var pct = el("span", "bat-pct", d.pct === null ? "--" : d.pct + "%");
    pct.style.color = c;
    var linha = el("div", "bat-linha", null, pct);
    if (d.pct !== null) linha.appendChild(pilhaDesenhada(d.pct, corDaBateria(d.pct, false) === c ? c : corDaBateria(d.pct, false), !!d.carregando));
    var selo = el("span", "bat-selo" + (d.carregando ? " carga" : d.pct === null ? " off" : ""), null, el("i"), el("span", "", d.selo));
    linha.appendChild(selo);
    card.appendChild(el("div", "bat-cab", null, img, el("div", "bat-id", null, el("b", "", d.nome), linha)));
    var blocos = el("div", "bat-blocos");
    d.blocos.forEach(function (b) {
      var x = el("div", "bat-bloco", null, el("small", "", b[0]), el("b", "", b[1]));
      if (b[2]) x.appendChild(el("span", "", b[2]));
      blocos.appendChild(x);
    });
    card.appendChild(blocos);
    var canvas = el("canvas");
    card.appendChild(el("div", "bat-curva", null, canvas));
    var det = el("div", "bat-det");
    d.detalhes.forEach(function (b) {
      var x = el("div", "", null, el("small", "", b[0]), el("b", "", b[1]));
      if (b[2]) x.appendChild(el("span", "", b[2]));
      det.appendChild(x);
    });
    var barra = botao("", "bat-mais", null);
    var seta = el("span", "seta icone", ""), rotulo = el("span");
    barra.appendChild(seta); barra.appendChild(rotulo);
    function rotular(animando) {
      seta.classList.toggle("aberta", !!d.aberto);
      rotulo.textContent = d.aberto ? T("LessDetails", t("Menos detalhes", "Less details")) : T("MoreDetails", t("Mais detalhes", "More details"));
      if (animando) deslizarAltura(det, !!d.aberto); else det.hidden = !d.aberto;
    }
    barra.addEventListener("click", function () {
      d.aberto = !d.aberto; rotular(true);
      pedir("bateria.detalhes", { chave: d.chave }).catch(function () { });
    });
    rotular();
    card.appendChild(barra);
    card.appendChild(det);
    card.desenhar = function () { desenharCurvaDaBateria(canvas, d, c); };
    return card;
  }

  MONTAR.baterias = function (alvo) {
    var vivo = true, cartoes = {}, lista = el("div", "bat-lista");
    var aberto = false;
    try { aberto = sessionStorage.getItem("widgetAberto") === "1"; } catch (e) { }

    var botaoWidget = el("button", "b bat-widget");
    botaoWidget.type = "button";
    var painel = el("div", "bat-painel");
    var setaW = el("span", "icone seta-w", "");
    botaoWidget.appendChild(el("span", "icone", ""));
    botaoWidget.appendChild(el("span", "", T("WidgetButton", "Widget")));
    botaoWidget.appendChild(setaW);
    botaoWidget.title = T("WidgetButtonTip");
    function desenharBotao(animando) {
      setaW.classList.toggle("aberta", aberto);
      if (animando) deslizarAltura(painel, aberto); else painel.hidden = !aberto;
    }
    botaoWidget.addEventListener("click", function () {
      aberto = !aberto;
      try { sessionStorage.setItem("widgetAberto", aberto ? "1" : "0"); } catch (e) { }
      desenharBotao(true);
    });
    alvo.appendChild(el("div", "bat-topo", null, botaoWidget));

    if (S.widget) {
      var w = S.widget;
      var cw = cartao(T("WidgetCard", t("Widget na área de trabalho", "Desktop widget")));
      var mw = function (a) { mudar("widget", a); };
      var linhaToggles = el("div", "wd-linha", null,
        trocaComRotulo(T("WidgetShow"), w.ativo, function (v) { mw({ ativo: v }); }),
        trocaComRotulo(T("CasaWidgetOpcao", t("Mostrar a casa no widget", "Show home in the widget")), w.casa, function (v) { mw({ casa: v }); }));
      cw.corpo.appendChild(linhaToggles);
      function grupoSeg(rotulo, opcoes, valor, larg, cb) {
        var s = segmentos(opcoes, valor, cb);
        if (larg) s.style.width = larg + "px";
        return el("div", "wd-grupo", null, el("div", "rotulo", rotulo), s);
      }
      cw.corpo.appendChild(el("div", "wd-linha", null,
        grupoSeg(T("WidgetLayout"), [["aneis", T("WidgetRings")], ["lista", T("WidgetList")]], w.layout, 216, function (v) { mw({ layout: v }); }),
        grupoSeg(T("WidgetGlass"), [["nitido", T("WidgetSharpShort")], ["claro", T("WidgetClear")], ["fosco", T("WidgetFrosted")]], w.vidro, 300, function (v) { mw({ vidro: v }); })));
      cw.corpo.appendChild(el("div", "wd-linha", null,
        grupoSeg(T("WidgetTint"), [["auto", T("WidgetTintAutoShort")], ["escura", T("WidgetTintDark")], ["clara", T("WidgetTintLight")]], w.cor, 450, function (v) { mw({ cor: v }); })));
      cw.corpo.appendChild(el("div", "wd-linha", null,
        trocaComRotulo(T("WidgetOnTop"), w.frente, function (v) { mw({ frente: v }); }),
        trocaComRotulo(T("WidgetLock"), w.travado, function (v) { mw({ travado: v }); })));
      cw.corpo.appendChild(el("div", "wd-linha", null,
        botao(T("WidgetBackToCorner"), "b", function () { pedir("widget.canto").catch(function (e) { faixa(e.message, true); }); }),
        el("span", "nota", T("WidgetCardNote"))));
      painel.appendChild(cw);
    }
    alvo.appendChild(painel);
    desenharBotao();
    alvo.appendChild(lista);

    var vazio = el("div", "vazio", T("BatNoDevices", t("Nenhum periférico com bateria encontrado.", "No battery-powered peripheral found.")));

    function montar(dados) {
      var chaves = dados.map(function (d) { return d.chave; });
      var igual = Object.keys(cartoes).join("|") === chaves.join("|");
      if (!igual) { limpar(lista); cartoes = {}; }
      dados.forEach(function (d) {
        var novo = cartaoDeBateria(d);
        if (cartoes[d.chave]) lista.replaceChild(novo, cartoes[d.chave]);
        else lista.appendChild(novo);
        cartoes[d.chave] = novo;
      });
      if (!dados.length) { limpar(lista); lista.appendChild(vazio); }
      Object.keys(cartoes).forEach(function (k) { cartoes[k].desenhar(); });
    }
    function buscar() {
      pedir("baterias").then(function (r) { if (vivo) montar(r || []); }, function () { });
    }
    buscar();
    var relogio = setInterval(buscar, 5000);
    var redesenhar = function () { Object.keys(cartoes).forEach(function (k) { cartoes[k].desenhar(); }); };
    window.addEventListener("resize", redesenhar);
    return { sair: function () { vivo = false; clearInterval(relogio); window.removeEventListener("resize", redesenhar); } };
  };

  // ---------------- Metricas ----------------

  function cor(nome) { return getComputedStyle(document.documentElement).getPropertyValue(nome).trim(); }

  // ---------------- menus (MenuTema) ----------------

  var menusAbertos = [];

  function fecharMenus() {
    menusAbertos.forEach(function (m) { if (m.parentNode) m.parentNode.removeChild(m); });
    menusAbertos = [];
    document.removeEventListener("mousedown", foraDoMenu, true);
    document.removeEventListener("keydown", teclaDoMenu, true);
  }
  function foraDoMenu(e) {
    for (var i = 0; i < menusAbertos.length; i++) if (menusAbertos[i].contains(e.target)) return;
    fecharMenus();
  }
  function teclaDoMenu(e) { if (e.key === "Escape") { e.stopPropagation(); fecharMenus(); } }

  /// Um menu ancorado num ponto; um item com "sub" abre outro ao lado.
  function desenharMenu(itens, x, y, nivel) {
    while (menusAbertos.length > nivel) { var velho = menusAbertos.pop(); if (velho.parentNode) velho.parentNode.removeChild(velho); }
    var m = el("div", "menu");
    itens.forEach(function (it) {
      if (it.sep) { m.appendChild(el("div", "sep")); return; }
      var b = el("button", "mi", it.texto);
      b.type = "button";
      if (it.sub) {
        b.appendChild(el("span", "icone seta-m", ""));
        b.addEventListener("click", function () {
          var r = b.getBoundingClientRect();
          desenharMenu(it.sub, r.right - 4, r.top - 5, nivel + 1);
        });
      } else {
        b.addEventListener("click", function () { fecharMenus(); it.ao(); });
      }
      m.appendChild(b);
    });
    document.body.appendChild(m);
    var r = m.getBoundingClientRect();
    m.style.left = Math.max(4, Math.min(x, window.innerWidth - r.width - 4)) + "px";
    m.style.top = Math.max(4, Math.min(y, window.innerHeight - r.height - 4)) + "px";
    menusAbertos.push(m);
    if (nivel === 0) {
      document.addEventListener("mousedown", foraDoMenu, true);
      document.addEventListener("keydown", teclaDoMenu, true);
    }
  }

  function abrirMenu(ancora, itens) {
    var jaAberto = menusAbertos.length > 0;
    fecharMenus();
    if (jaAberto && ancora.menuAberto) { ancora.menuAberto = false; return; }
    var r = ancora.getBoundingClientRect();
    desenharMenu(itens, r.left, r.bottom + 2, 0);
    ancora.menuAberto = true;
    setTimeout(function () { ancora.menuAberto = false; }, 300);
  }

  // ---------------- Metricas ----------------

  var BALDES_6H = 21600;

  function nomeDaJanela(s) {
    if (s >= 3600) { var h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60); return m === 0 ? h + " h" : h + " h " + m + " min"; }
    if (s < 60) return s + " s";
    return Math.floor(s / 60) + " min";
  }

  function hora2(n) { return ("0" + n).slice(-2); }
  function horaDe(d, comSegundos) { return hora2(d.getHours()) + ":" + hora2(d.getMinutes()) + (comSegundos ? ":" + hora2(d.getSeconds()) : ""); }

  function corResolvida(c) { return c.replace(/var\((--[a-z-]+)\)/g, function (m, n) { return cor(n); }); }

  function estatisticas(pontos) {
    var min = Infinity, max = -Infinity, soma = 0, n = 0;
    (pontos || []).forEach(function (v) { if (v === null) return; if (v < min) min = v; if (v > max) max = v; soma += v; n++; });
    return n ? { min: min, max: max, med: soma / n } : null;
  }

  /// O NavegadorDeJanela: as seis horas numa faixa, com o trecho escolhido
  /// (tamanho e posicao) e o botao "ao vivo".
  function navegadorDeJanela(estado, aoMudar, aoConfirmar) {
    var canvas = el("canvas", "nav-canvas");
    var serie = [], corLinha = "", btVivo = { x: 0, y: 0, w: 0, h: 0 }, sobreVivo = false, arraste = "";
    var xInicio = 0, janelaInicio = 0, atrasInicio = 0;
    var TOPO = 22, RODAPE = 16, PEGA = 7;

    function baldes() { return Math.max(2, Math.round(estado.janela / estado.passo)); }
    function total() { return Math.round(BALDES_6H / estado.passo); }
    function faixaR() { var r = medida(canvas); return { x: 1, y: TOPO, w: Math.max(10, r.width - 2), h: Math.max(10, r.height - TOPO - RODAPE) }; }
    function xDe(i) { var f = faixaR(); return f.x + i / (total() - 1) * f.w; }
    function xFim() { return xDe(total() - 1 - estado.atras); }
    function xIni() { return xDe(total() - estado.atras - baldes()); }

    function desenhar() {
      var r = medida(canvas), dpr = window.devicePixelRatio || 1;
      var W = Math.max(10, Math.round(r.width)), H = Math.max(10, Math.round(r.height));
      if (canvas.width !== W * dpr) canvas.width = W * dpr;
      if (canvas.height !== H * dpr) canvas.height = H * dpr;
      var g = canvas.getContext("2d");
      g.setTransform(dpr, 0, 0, dpr, 0, 0);
      g.clearRect(0, 0, W, H);
      var f = faixaR(), vivo = estado.atras === 0;
      var fam = "'Segoe UI Variable Small','Segoe UI',sans-serif";
      // cabecalho: o trecho escolhido em horas de relogio
      var tFim = new Date(Date.now() - estado.atras * estado.passo * 1000), tIni = new Date(tFim.getTime() - estado.janela * 1000);
      var curto = estado.janela <= 600;
      var titulo = horaDe(tIni, curto) + " – " + (vivo ? T("NavNow", t("agora", "now")) : horaDe(tFim, curto)) + "  ·  " + nomeDaJanela(estado.janela);
      g.textBaseline = "top"; g.textAlign = "left";
      g.font = "600 13.33px 'Segoe UI Variable Text','Segoe UI',sans-serif"; g.fillStyle = cor("--text");
      g.fillText(titulo, 0, 2);
      // a direita: o estado, que vira botao quando ha para onde voltar
      var rotulo = vivo ? T("NavLive", t("ao vivo", "live")) : T("NavBackToLive", t("Voltar ao vivo", "Back to live"));
      g.font = "12px " + fam;
      var tw = g.measureText(rotulo).width;
      btVivo = { x: W - tw - 18, y: 0, w: tw + 16, h: 18 };
      g.beginPath();
      if (g.roundRect) g.roundRect(btVivo.x, btVivo.y, btVivo.w, btVivo.h, 9); else g.rect(btVivo.x, btVivo.y, btVivo.w, btVivo.h);
      g.fillStyle = vivo ? cor("--accent-soft") : (sobreVivo ? cor("--hover") : cor("--surface"));
      g.fill();
      if (!vivo) { g.strokeStyle = cor("--border"); g.lineWidth = 1; g.stroke(); }
      if (vivo) { g.beginPath(); g.arc(btVivo.x + 8.5, btVivo.y + 9.5, 2.5, 0, Math.PI * 2); g.fillStyle = cor("--accent"); g.fill(); }
      g.fillStyle = vivo ? cor("--accent-text") : cor("--text");
      g.textAlign = "center"; g.textBaseline = "middle";
      g.fillText(rotulo, btVivo.x + btVivo.w / 2 + (vivo ? 6 : 0), btVivo.y + 10);
      // faixa com as seis horas
      g.beginPath();
      if (g.roundRect) g.roundRect(f.x, f.y, f.w, f.h, 4); else g.rect(f.x, f.y, f.w, f.h);
      g.fillStyle = cor("--surface"); g.fill(); g.strokeStyle = cor("--border"); g.lineWidth = 1; g.stroke();
      resumo(g, f);
      // o que esta fora da selecao fica esmaecido
      var x0 = Math.max(f.x, xIni()), x1 = Math.min(f.x + f.w, xFim());
      if (x1 - x0 < 4) { var mid = (x0 + x1) / 2; x0 = mid - 2; x1 = mid + 2; }
      g.fillStyle = S.escuro ? "rgba(28,28,31,0.59)" : "rgba(244,245,247,0.55)";
      if (x0 > f.x) g.fillRect(f.x, f.y + 1, x0 - f.x, f.h - 1);
      if (x1 < f.x + f.w) g.fillRect(x1, f.y + 1, f.x + f.w - x1, f.h - 1);
      g.strokeStyle = cor("--accent"); g.lineWidth = 1.6; g.strokeRect(x0, f.y, x1 - x0, f.h);
      g.fillStyle = cor("--text");
      [x0, x1].forEach(function (x) {
        g.beginPath();
        if (g.roundRect) g.roundRect(Math.round(x) - 3, f.y + f.h / 2 - 8, 6, 16, 2); else g.rect(Math.round(x) - 3, f.y + f.h / 2 - 8, 6, 16);
        g.fill();
      });
      // eixo: as pontas e as horas cheias de duas em duas
      var yE = f.y + f.h + 2;
      g.fillStyle = cor("--faint"); g.textBaseline = "top";
      g.textAlign = "left"; g.fillText(t("6 h atrás", "6 h ago"), f.x, yE);
      g.textAlign = "right"; g.fillText(T("Now", t("agora", "now")), f.x + f.w, yE);
      g.textAlign = "center";
      [4, 2].forEach(function (h) { g.fillText(t(h + " h atrás", h + " h ago"), xDe(total() - 1 - h * 3600 / estado.passo), yE); });
    }

    /// A forma da leitura nas seis horas: minimo e maximo por coluna de pixel.
    function resumo(g, f) {
      var n = serie.length, min = Infinity, max = -Infinity, i;
      for (i = 0; i < n; i++) { var v = serie[i]; if (v === null) continue; if (v < min) min = v; if (v > max) max = v; }
      if (min > max) return;
      var faixa = Math.max(0.001, max - min), alto = f.y + 4, alt = f.h - 8;
      g.strokeStyle = (corLinha || cor("--accent")) + "96"; g.lineWidth = 1;
      var colunas = Math.max(1, Math.floor(f.w - 2)), ultimo = null;
      for (var c = 0; c < colunas; c++) {
        var a = Math.floor(c * n / colunas), b = Math.floor((c + 1) * n / colunas), lo = Infinity, hi = -Infinity, fim = null;
        for (i = a; i < b && i < n; i++) { if (serie[i] === null) continue; if (serie[i] < lo) lo = serie[i]; if (serie[i] > hi) hi = serie[i]; fim = serie[i]; }
        if (lo > hi) { ultimo = null; continue; }
        if (ultimo !== null) { if (ultimo < lo) lo = ultimo; if (ultimo > hi) hi = ultimo; }
        ultimo = fim;
        var x = f.x + 1 + c + 0.5, y0 = alto + alt - (hi - min) / faixa * alt, y1 = alto + alt - (lo - min) / faixa * alt;
        if (y1 - y0 < 1) y1 = y0 + 1;
        g.beginPath(); g.moveTo(x, y0); g.lineTo(x, y1); g.stroke();
      }
    }

    function onde(x, y) {
      var f = faixaR();
      if (y < f.y - 2 || y > f.y + f.h + 2) return "";
      if (Math.abs(x - xIni()) <= PEGA) return "esq";
      if (Math.abs(x - xFim()) <= PEGA) return "dir";
      if (x > xIni() && x < xFim()) return "meio";
      return "";
    }
    function sobreBotao(x, y) { return estado.atras !== 0 && x >= btVivo.x && x <= btVivo.x + btVivo.w && y >= btVivo.y && y <= btVivo.y + btVivo.h; }

    function aplicar(nb, atras) {
      var minimo = Math.round(60 / estado.passo);
      if (nb < minimo) nb = minimo;
      if (nb > total()) nb = total();
      if (atras < 0) atras = 0;
      if (atras > total() - nb) atras = total() - nb;
      var janela = nb * estado.passo;
      if (janela === estado.janela && atras === estado.atras) return;
      estado.janela = janela; estado.atras = atras;
      desenhar(); aoMudar();
    }

    function ponto(e) { var r = canvas.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; }

    canvas.addEventListener("pointermove", function (e) {
      var p = ponto(e), sobre = sobreBotao(p.x, p.y);
      if (sobre !== sobreVivo) { sobreVivo = sobre; desenhar(); }
      if (!arraste) {
        var o = onde(p.x, p.y), f = faixaR();
        canvas.style.cursor = sobre ? "pointer" : (o === "esq" || o === "dir") ? "ew-resize" : o === "meio" ? "move" : (p.y >= f.y && p.y <= f.y + f.h) ? "pointer" : "default";
        return;
      }
      var porPixel = (total() - 1) / faixaR().w, delta = Math.round((p.x - xInicio) * porPixel);
      var nbIni = Math.round(janelaInicio / estado.passo), atras = atrasInicio, nb = nbIni;
      if (arraste === "meio") atras = atrasInicio - delta;
      else if (arraste === "esq") nb = nbIni - delta;
      else if (arraste === "dir") { atras = Math.max(0, atrasInicio - delta); nb = nbIni + (atrasInicio - atras); }
      aplicar(nb, atras);
    });
    canvas.addEventListener("pointerdown", function (e) {
      if (e.button !== 0) return;
      var p = ponto(e), f = faixaR();
      if (sobreBotao(p.x, p.y)) { voltarAoVivo(); return; }
      if (p.y < f.y || p.y > f.y + f.h) return;
      var a = onde(p.x, p.y);
      if (!a) {
        var i = (p.x - f.x) / f.w * (total() - 1);
        aplicar(baldes(), Math.round(total() - 1 - i - baldes() / 2));
        a = "meio";
      }
      arraste = a; xInicio = p.x; janelaInicio = estado.janela; atrasInicio = estado.atras;
      try { canvas.setPointerCapture(e.pointerId); } catch (x) { }
    });
    function soltar() { if (!arraste) return; arraste = ""; aoConfirmar(); }
    canvas.addEventListener("pointerup", soltar);
    canvas.addEventListener("pointercancel", soltar);
    canvas.addEventListener("pointerleave", function () { if (sobreVivo) { sobreVivo = false; desenhar(); } });
    canvas.addEventListener("dblclick", function () { voltarAoVivo(); });
    canvas.addEventListener("wheel", function (e) {
      e.preventDefault();
      aplicar(Math.round(baldes() * (e.deltaY < 0 ? 1 / 1.25 : 1.25)), estado.atras);
      aoConfirmar();
    }, { passive: false });
    function voltarAoVivo() { if (estado.atras === 0) return; estado.atras = 0; desenhar(); aoMudar(); aoConfirmar(); }

    return {
      canvas: canvas,
      desenhar: desenhar,
      definir: function (pontos, c) { serie = pontos || []; corLinha = c || ""; desenhar(); }
    };
  }

  /// A pagina Metricas: grade de cartoes, conjuntos, perfis de metricas e o navegador de janela.
  MONTAR.metricas = function (alvo) {
    var vivo = true, P = null, estado = { janela: 600, atras: 0, passo: S.passo || 5 };
    var series = {}, cartoes = [], seriesNav = null, pendente = 0;

    var btAdd = botao(T("AddMetric", t("Adicionar métrica", "Add metric")), "b p");
    var btConj = botao(T("DefaultMetrics", t("Conjuntos", "Presets")), "b");
    var btPerf = botao(T("MetricProfiles", t("Perfis de métricas", "Metric profiles")), "b");
    var dica = el("span", "mt-dica", T("MetricsHint", t("Passe o ponteiro sobre um cartão para mover, redimensionar ou remover.", "Hover a card to move, resize or remove it.")));
    alvo.appendChild(el("div", "mt-barra", null, btAdd, btConj, btPerf, dica));

    var nav = navegadorDeJanela(estado, function () { agendar(); }, function () { confirmarJanela(); });
    alvo.appendChild(el("div", "nav-caixa", null, nav.canvas));
    var vazio = el("div", "vazio", T("NoMetrics", t("Nenhuma métrica escolhida. Use “Adicionar métrica”.", "No metric chosen. Use “Add metric”.")));
    var grade = el("div", "mt-grade");
    alvo.appendChild(vazio);
    alvo.appendChild(grade);

    function acao(a) {
      return pedir("metricas.acao", a).then(function (r) {
        if (!vivo) return r;
        carregar(r);
        if (r.aviso) faixa(r.aviso);
        return r;
      }, function (e) { faixa(e.message, true); });
    }

    function carregar(r) {
      P = r;
      S.metricas = r.itens.map(function (i) { return i.id; });
      estado.passo = r.passo || estado.passo;
      estado.janela = r.janela;
      montarGrade();
      buscar();
    }

    function confirmarJanela() {
      if (!P || P.janela === estado.janela) return;
      P.janela = estado.janela;
      pedir("metricas.acao", { acao: "janela", segundos: estado.janela }).catch(function () { });
    }

    // ---- menus ----
    btAdd.addEventListener("click", function () {
      escolherSensor(T("AddMetric", t("Adicionar métrica", "Add metric")), "").then(function (id) {
        if (id) acao({ acao: "add", id: id });
      });
    });
    btConj.addEventListener("click", function () {
      if (!P) return;
      abrirMenu(btConj, P.conjuntos.map(function (c) { return { texto: c.nome, ao: function () { acao({ acao: "conjunto", chave: c.chave }); } }; }));
    });
    btPerf.addEventListener("click", function () {
      if (!P) return;
      var itens = [{ texto: T("SaveMetricProfile", t("Salvar perfil atual…", "Save current profile…")), ao: function () {
        perguntarNome(T("MetricProfileName", t("Nome do perfil de métricas", "Metric profile name")), Tm("DefaultMetricProfileName", P.perfis.length + 1) || (t("Perfil ", "Profile ") + (P.perfis.length + 1))).then(function (nome) {
          if (nome) acao({ acao: "perfil.salvar", nome: nome });
        });
      } }];
      if (P.perfis.length) itens.push({ sep: true });
      P.perfis.forEach(function (nome) {
        itens.push({ texto: nome, sub: [
          { texto: T("Apply", t("Aplicar", "Apply")), ao: function () { acao({ acao: "perfil.aplicar", nome: nome }); } },
          { texto: T("UpdateMetricProfile", t("Atualizar com a grade atual", "Update with current grid")), ao: function () { acao({ acao: "perfil.atualizar", nome: nome }); } },
          { texto: T("RenameMetricProfile", t("Renomear perfil…", "Rename profile…")), ao: function () {
            perguntarNome(T("MetricProfileName", t("Nome do perfil de métricas", "Metric profile name")), nome).then(function (novo) {
              if (novo && novo !== nome) acao({ acao: "perfil.renomear", nome: nome, novo: novo });
            });
          } },
          { sep: true },
          { texto: T("DeleteMetricProfile", t("Excluir perfil", "Delete profile")), ao: function () {
            confirmar(Tm("DeleteMetricProfileQ", nome) || (t("Excluir o perfil “", "Delete the profile “") + nome + "”?"), T("DeleteMetricProfile", t("Excluir perfil", "Delete profile")), "b p perigo").then(function (sim) {
              if (sim) acao({ acao: "perfil.excluir", nome: nome });
            });
          } }
        ] });
      });
      abrirMenu(btPerf, itens);
    });

    // ---- grade ----
    function colunas() {
      var disp = Math.max(320, grade.clientWidth || (alvo.clientWidth - 4));
      var por = Math.floor(disp / 200);
      por = Math.max(4, Math.min(12, por));
      por -= por % 4;
      return Math.min(por, Math.max(1, Math.floor((disp - 4 + 12) / (140 + 12))));
    }

    function iconeDoCartao(glifo, dica, ao) {
      var b = el("button", "mc-bt icone", glifo);
      b.type = "button"; b.title = dica;
      b.addEventListener("click", function (e) { e.stopPropagation(); ao(); });
      return b;
    }

    function montarGrade() {
      cartoes.forEach(function (c) { if (c.el.parentNode) c.el.parentNode.removeChild(c.el); });
      cartoes = [];
      limpar(grade);
      vazio.hidden = P.itens.length > 0;
      var por = colunas();
      grade.style.gridTemplateColumns = "repeat(" + por + ", minmax(0, 1fr))";
      P.itens.forEach(function (it, i) {
        var cols = Math.min(por, it.tam >= 2 ? 4 : it.tam === 1 ? 2 : 1);
        var canvas = el("canvas", "mc-curva");
        var titulo = el("div", "mc-t", it.titulo); titulo.title = it.titulo;
        var valor = el("div", "mc-v"), stats = el("div", "mc-s");
        var sub = el("div", "mc-f", it.sub);
        var barra = el("div", "mc-bts",  null,
          iconeDoCartao("", t("Mover para a esquerda", "Move left"), function () { acao({ acao: "esq", id: it.id }); }),
          iconeDoCartao("", t("Mover para a direita", "Move right"), function () { acao({ acao: "dir", id: it.id }); }),
          iconeDoCartao("", t("Mudar o tamanho", "Change size"), function () { acao({ acao: "tamanho", id: it.id }); }),
          iconeDoCartao("", t("Remover", "Remove"), function () { acao({ acao: "remover", id: it.id }); }));
        var c = el("div", "cartao mc" + (P.nav === it.id ? " sel" : ""), null, canvas, titulo, barra, valor, stats, sub);
        c.style.gridColumn = "span " + cols;
        c.style.height = (it.tam >= 2 ? 220 : it.tam === 1 ? 140 : 104) + "px";
        c.addEventListener("click", function () {
          if (P.nav === it.id) return;
          P.nav = it.id;
          cartoes.forEach(function (x) { x.el.classList.toggle("sel", x.it.id === it.id); });
          pedir("metricas.acao", { acao: "nav", id: it.id }).catch(function () { });
          buscarNav();
        });
        var obj = { it: it, el: c, canvas: canvas, valor: valor, stats: stats, corAtual: "", hx: -1 };
        // Ponteiro sobre o grafico: o valor naquele ponto (fora dos botoes).
        c.addEventListener("pointermove", function (e) {
          var r = canvas.getBoundingClientRect();
          var hx = e.target.closest && e.target.closest(".mc-bts") ? -1 : e.clientX - r.left;
          if (hx === obj.hx) return;
          obj.hx = hx; desenharCartao(obj);
        });
        c.addEventListener("pointerleave", function () { if (obj.hx < 0) return; obj.hx = -1; desenharCartao(obj); });
        cartoes.push(obj);
        grade.appendChild(c);
      });
      desenharCartoes();
      nav.desenhar();
    }

    function textoEstat(it, e) {
      if (!e) return "";
      var formas = [
        t("mín ", "min ") + numero(e.min) + "    " + t("méd ", "avg ") + numero(e.med) + "    " + t("máx ", "max ") + numero(e.max),
        t("máx ", "max ") + numero(e.max) + "  ·  " + t("méd ", "avg ") + numero(e.med),
        t("máx ", "max ") + numero(e.max)];
      return formas;
    }

    function desenharCartoes() { cartoes.forEach(desenharCartao); }
    function desenharCartao(c) {
      (function () {
        var pontos = series[c.it.id] || null;
        var v = leitura(c.it.id);
        // Na grade o numero e de enfase; so atencao e perigo mudam a cor (a Visao geral e que usa o tom quente).
        var hex = v !== null && c.it.perigo !== null && v >= c.it.perigo ? cor("--danger")
          : v !== null && c.it.atencao !== null && v >= c.it.atencao ? cor("--warn") : cor("--accent");
        c.corAtual = hex;
        c.valor.style.color = hex;
        limpar(c.valor);
        c.valor.appendChild(document.createTextNode(numero(v)));
        if (v !== null && c.it.unidade) c.valor.appendChild(el("small", "", c.it.unidade));
        var geo = desenharCurva(c.canvas, pontos, hex);
        if (c.hx >= 0) detalheNaCurva(c.canvas, pontos, hex, geo, c.hx, Date.now() - estado.atras * estado.passo * 1000, estado.passo, estado.janela, c.it.unidade);
        var e = estatisticas(pontos);
        c.estat = e;
        limpar(c.stats);
        if (e && c.it.tam > 0) {
          var formas = textoEstat(c.it, e), larg = c.el.clientWidth - 20, usado = "";
          for (var k = 0; k < formas.length; k++) { c.stats.textContent = formas[k]; if (c.stats.scrollWidth <= larg) { usado = formas[k]; break; } }
          c.stats.textContent = usado;
        }
      })();
    }

    // ---- dados ----
    function buscar() {
      if (!P || !P.itens.length) { series = {}; return; }
      var n = ++pendente;
      pedir("metricas", { ids: P.itens.map(function (i) { return i.id; }), segundos: estado.janela, atras: estado.atras }).then(function (r) {
        if (!vivo || n !== pendente) return;
        series = r.series || {};
        desenharCartoes();
        nav.desenhar();
      }, function () { });
      buscarNav();
    }
    function buscarNav() {
      if (!P || !P.nav) { nav.definir([], ""); return; }
      pedir("metricas", { ids: [P.nav], segundos: BALDES_6H, atras: 0 }).then(function (r) {
        if (!vivo) return;
        var c = ""; cartoes.forEach(function (x) { if (x.it.id === P.nav) c = x.corAtual; });
        nav.definir((r.series || {})[P.nav] || [], c);
      }, function () { });
    }
    var espera = null;
    function agendar() {
      if (espera) return;
      espera = setTimeout(function () { espera = null; buscar(); }, 120);
    }

    pedir("metricas.acao", { acao: "pagina" }).then(function (r) {
      if (!vivo) return;
      estado.atras = 0; estado.janela = r.janela;
      carregar(r);
    }, function (e) { faixa(e.message, true); });
    var relogio = setInterval(buscar, (S.passo || 5) * 1000);
    var redesenhar = function () { if (P) { montarGrade(); } };
    window.addEventListener("resize", redesenhar);

    return {
      atualizar: function () {
        if (P) {
          cartoes.forEach(function (c) {
            var v = leitura(c.it.id);
            limpar(c.valor);
            c.valor.appendChild(document.createTextNode(numero(v)));
            if (v !== null && c.it.unidade) c.valor.appendChild(el("small", "", c.it.unidade));
          });
        }
      },
      sair: function () { vivo = false; clearInterval(relogio); fecharMenus(); window.removeEventListener("resize", redesenhar); }
    };
  };

  // ---------------- Especificacoes ----------------

  MONTAR.specs = function (alvo) {
    var vivo = true, categoria = 0, estado = "carregando";
    var copiar = botao(T("SpecsCopy", t("Copiar tudo", "Copy all")), "b");
    copiar.disabled = true;
    var texto = el("span", "sp-estado", T("SpecsLoading", t("Consultando o sistema…", "Querying the system…")));
    alvo.appendChild(el("div", "sp-topo", null, copiar, texto));
    var pilulas = el("div", "sp-cats");
    var corpo = el("div", "sp-corpo");
    alvo.appendChild(pilulas);
    alvo.appendChild(corpo);

    copiar.addEventListener("click", function () {
      if (estado === "falhou") { coletar(); return; }
      pedir("specs.copiar").then(function (m) { faixa(typeof m === "string" ? m : T("SpecsCopied", t("Especificações copiadas", "Specs copied"))); },
        function (e) { faixa(e.message, true); });
    });

    function ordenados() {
      var ordem = [T("SpecCpu"), T("SpecGpu"), T("SpecRam"), T("SpecStorage"), T("SpecBoard"), T("SpecOs"), T("SpecNetwork")];
      var fila = [];
      ordem.forEach(function (n) { specs.forEach(function (g) { if (g.titulo === n && fila.indexOf(g) < 0) fila.push(g); }); });
      specs.forEach(function (g) { if (fila.indexOf(g) < 0) fila.push(g); });
      return fila;
    }

    function blocoDoResumo(g) {
      var b = g.bloco || { titulo: g.titulo, destaque: g.destaque || (g.linhas[0] ? g.linhas[0][1] : ""), sub: "", largo: false, etiquetas: [], slots: [], slotsTxt: "", discos: [] };
      var c = el("div", "cartao sp-bloco");
      c.appendChild(el("div", "sp-bt", null, el("span", "", g.titulo), el("span", "ver", T("SpecSeeAll", t("Ver tudo ›", "See all ›")))));
      var dest = el("div", "sp-dest", b.destaque); dest.title = b.destaque;
      c.appendChild(dest);
      if (b.sub) c.appendChild(el("div", "sp-sub", b.sub));
      if (b.etiquetas.length) {
        var et = el("div", "sp-tags");
        b.etiquetas.forEach(function (x) { et.appendChild(el("span", "", x)); });
        c.appendChild(et);
      }
      if (b.slots.length) {
        var ln = el("div", "sp-slots" + (b.slotsNomeados ? " nom" : ""));
        b.slots.forEach(function (s) {
          var q = el("span", "sl" + (s.cheio ? " cheio" : ""), null, el("i"), b.slotsNomeados ? el("em", "", s.nome) : null);
          if (s.dica) q.title = s.dica;
          ln.appendChild(q);
        });
        ln.appendChild(el("small", "", b.slotsTxt));
        c.appendChild(ln);
      }
      if (b.discos.length) {
        var ds = el("div", "sp-discos");
        b.discos.forEach(function (d) {
          var cab = el("div", "dl", null, el("b", "", d.nome), el("span", "", d.direita));
          ds.appendChild(cab);
          if (d.pct !== null && d.pct !== undefined) {
            var cheio = el("i"); cheio.style.width = Math.max(1, Math.round(d.pct * 1000) / 10) + "%";
            ds.appendChild(el("div", "trilho", null, cheio));
          }
        });
        c.appendChild(ds);
      }
      c.tabIndex = 0;
      c.addEventListener("click", function () { escolher(specs.indexOf(g) + 1); });
      c.addEventListener("keydown", function (e) { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); escolher(specs.indexOf(g) + 1); } });
      c.largo = !!b.largo;
      return c;
    }

    function ficha(g) {
      var c = cartao(g.titulo, "sp-ficha");
      g.linhas.forEach(function (l) {
        c.corpo.appendChild(el("div", "sp-lin", null, el("span", "r", l[0]), el("span", "v", l[1])));
      });
      return c;
    }

    function desenhar() {
      limpar(pilulas); limpar(corpo);
      var nomes = [T("SpecsOverview", t("Resumo", "Overview"))].concat(specs.map(function (g) { return g.titulo; }));
      nomes.forEach(function (n, i) {
        var b = botao(n, "b" + (i === categoria ? " p" : ""), function () { escolher(i); });
        pilulas.appendChild(b);
      });
      if (categoria > 0 && specs[categoria - 1]) { corpo.appendChild(ficha(specs[categoria - 1])); return; }
      var mosaico = el("div", "sp-mosaico");
      var fila = ordenados().map(blocoDoResumo), i = 0;
      while (i < fila.length) {
        var a = fila[i], b = (!a.largo && i + 1 < fila.length && !fila[i + 1].largo) ? fila[i + 1] : null;
        if (!b) { a.classList.add("inteiro"); mosaico.appendChild(a); i++; continue; }
        mosaico.appendChild(a); mosaico.appendChild(b); i += 2;
      }
      corpo.appendChild(mosaico);
    }

    function escolher(i) { categoria = i; desenhar(); var m = document.getElementById("main"); if (m) m.scrollTop = 0; }

    function pronto() {
      if (!vivo) return;
      if (!specs || !specs.length) {
        estado = "falhou";
        texto.textContent = T("SpecsFailed", t("Não foi possível consultar o sistema. Os detalhes estão no log.", "Couldn't query the system. Details are in the log."));
        texto.classList.add("claro");
        copiar.textContent = T("SpecsRetry", t("Tentar de novo", "Try again")); copiar.disabled = false;
        return;
      }
      estado = "pronto";
      texto.textContent = T("SpecsNote", ""); texto.classList.remove("claro"); texto.classList.add("nota");
      copiar.textContent = T("SpecsCopy", t("Copiar tudo", "Copy all")); copiar.disabled = false;
      desenhar();
    }
    function coletar() {
      estado = "carregando"; specs = null;
      copiar.disabled = true; copiar.textContent = T("SpecsCopy", t("Copiar tudo", "Copy all"));
      texto.textContent = T("SpecsLoading", t("Consultando o sistema…", "Querying the system…")); texto.className = "sp-estado";
      limpar(pilulas); limpar(corpo);
      pedir("specs").then(function (r) { specs = r || []; pronto(); }, function () { specs = []; pronto(); });
    }
    if (specs) pronto(); else coletar();
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
    var jiAgora = el("div", "ji");
    jogos.corpo.appendChild(el("div", "jogo", null, jiAgora, el("div", "", null, nomeJogo, subJogo)));
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
      vinculos.appendChild(el("div", "vinculo", null, iconeDoJogo(el("div", "ji"), v.imagem),
        el("div", "vt", null, el("b", "", v.nome), el("small", "", v.perfil)), x));
    });
    if (!S.jogos.vinculos.length) vinculos.appendChild(el("div", "nota", T("NoGameBindings")));
    jogos.corpo.appendChild(vinculos);
    direita.appendChild(jogos);

    var ultimo = "-";
    function atualizarJogo() {
      var j = E ? E.jogo : null;
      var marca = j ? j.chave + "|" + (j.perfil || "") + "|" + j.nome + "|" + (j.imagem ? j.imagem.length : 0) : "";
      if (marca === ultimo) return;
      ultimo = marca;
      iconeDoJogo(jiAgora, j ? j.imagem : null);
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

    // "Usar a Visao geral classica" so muda a janela classica: a interface
    // nova tem uma Visao geral so, entao a opcao nao aparece aqui.

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
    var aba = casaAba;
    casaAba = "";
    function medir() {
      var r = area.getBoundingClientRect();
      avisar("casa.area", { visivel: true, x: r.left, y: r.top, w: r.width, h: r.height, aba: aba });
      aba = "";
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
      var pedida = paginaPedida(decodeURIComponent((location.hash || "").replace(/^#/, "")));
      var primeira = MONTAR[pedida] ? pedida : guardada || "visao";
      // A primeira pagina so e montada com o primeiro retrato (que o app manda
      // junto com o "iniciar"): nada de cartoes vazios esperando o relogio.
      // Depois de desenhada, o app mostra a janela ("pronto").
      var montada = false;
      function montar() {
        if (montada) return;
        montada = true; aoPrimeiroEstado = null;
        primeiraEntrada = true;
        mostrar(primeira);
        primeiraEntrada = false;
        avisar("pronto");
      }
      if (E) montar();
      else { aoPrimeiroEstado = montar; setTimeout(montar, 1500); }
    }, function (e) {
      $("pagina").appendChild(el("div", "vazio", e.message));
      avisar("pronto");
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
