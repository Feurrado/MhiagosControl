// A interface nova do Mhiagos Control. Roda dentro do WebView2, numa origem
// que nao existe na rede, e so conversa com o app pela ponte (PonteDoApp.cs):
// pedidos {id, m, a} e respostas {id, ok, r | erro}; uma vez por segundo chega
// {ev:"estado", d}. A politica da pagina nao aceita script nem estilo embutido:
// tudo aqui monta a arvore pelo DOM e mexe em estilo so pelo CSSOM.
(function () {
  "use strict";

  var S = null;            // o que "iniciar" devolveu: sensores, perfis, configuracao
  var E = null;            // o retrato de cada segundo
  var atual = "";          // pagina a vista
  var vista = null;        // {atualizar(), sair()} da pagina a vista
  var editando = "";       // perfil aberto no Painel CPU
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
    var p = pendentes[d.id];
    if (!p) return;
    delete pendentes[d.id];
    if (d.ok) p.ok(d.r); else p.falha(new Error(d.erro || "falha"));
  }

  // ---------------- utilitarios ----------------

  function t(pt, en) { return !S || S.pt ? pt : en; }
  function $(id) { return document.getElementById(id); }

  function el(tag, classe, texto) {
    var e = document.createElement(tag);
    if (classe) e.className = classe;
    if (texto !== undefined && texto !== null) e.textContent = texto;
    for (var i = 3; i < arguments.length; i++) if (arguments[i]) e.appendChild(arguments[i]);
    return e;
  }

  function limpar(e) { while (e.firstChild) e.removeChild(e.firstChild); }

  function icone(d) {
    var ns = "http://www.w3.org/2000/svg";
    var s = document.createElementNS(ns, "svg");
    s.setAttribute("viewBox", "0 0 24 24");
    s.setAttribute("aria-hidden", "true");
    d.split("|").forEach(function (parte) {
      var p = document.createElementNS(ns, "path");
      p.setAttribute("d", parte);
      s.appendChild(p);
    });
    return s;
  }

  var faixaTimer = 0;
  function faixa(texto, erro) {
    var f = $("faixa");
    clearTimeout(faixaTimer);
    if (!texto) { f.hidden = true; return; }
    f.textContent = texto;
    f.className = erro ? "faixa erro" : "faixa";
    f.hidden = false;
    faixaTimer = setTimeout(function () { f.hidden = true; }, erro ? 8000 : 3000);
  }

  /// Um pedido que muda a configuracao: a resposta e o "iniciar" novo.
  function mudar(m, a, feito) {
    return pedir(m, a).then(function (r) {
      S = r;
      if (feito) faixa(feito);
      mostrar(atual, true);
      return true;
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

  function cartao(titulo, classe) {
    var c = el("div", "cartao" + (classe ? " " + classe : ""));
    if (titulo) c.appendChild(el("h2", "", titulo));
    return c;
  }

  function linha(k, v) {
    var valor = el("span", "v", v);
    var l = el("div", "linha", null, el("span", "k", k), valor);
    l.valor = valor;
    return l;
  }

  function troca(ligado, aoMudar) {
    var b = el("button", "troca");
    b.type = "button";
    b.setAttribute("role", "switch");
    b.setAttribute("aria-checked", ligado ? "true" : "false");
    b.addEventListener("click", function () {
      var novo = b.getAttribute("aria-checked") !== "true";
      b.setAttribute("aria-checked", novo ? "true" : "false");
      aoMudar(novo, b);
    });
    return b;
  }

  function chave(titulo, nota, controle) {
    var texto = el("div", "", null, el("span", "", titulo));
    if (nota) texto.appendChild(el("small", "", nota));
    return el("div", "chave", null, texto, controle);
  }

  function segmentos(opcoes, valor, aoMudar) {
    var s = el("div", "seg");
    s.setAttribute("role", "group");
    s.valor = valor;
    opcoes.forEach(function (o) {
      var b = el("button", "", o[1]);
      b.type = "button";
      b.setAttribute("aria-pressed", o[0] === valor ? "true" : "false");
      b.addEventListener("click", function () {
        s.valor = o[0];
        Array.prototype.forEach.call(s.children, function (x) { x.setAttribute("aria-pressed", x === b ? "true" : "false"); });
        if (aoMudar) aoMudar(o[0]);
      });
      s.appendChild(b);
    });
    return s;
  }

  function campo(rotulo, controle) {
    var c = el("label", "campo", null, el("span", "", rotulo), controle);
    return c;
  }

  /// Uma lista com os sensores agrupados por categoria.
  function seletorDeSensor(valor, vazio) {
    var s = el("select");
    if (vazio !== null) {
      var o = el("option", "", vazio);
      o.value = "";
      s.appendChild(o);
    }
    var grupos = {}, ordem = [];
    S.sensores.forEach(function (x) {
      var c = x.categoria || t("Outros", "Other");
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
    // O perfil pode apontar para um sensor que nao esta na lista de agora (programa fechado, fonte fora).
    if (valor && s.value !== valor) {
      var falta = el("option", "", t("(sensor ausente)", "(missing sensor)"));
      falta.value = valor;
      s.insertBefore(falta, s.firstChild);
      s.value = valor;
    }
    return s;
  }

  // ---------------- navegacao ----------------

  var PAGINAS = [
    ["visao", "Visão geral", "Overview", "M3 12 12 4l9 8|M5 10v10h14V10"],
    ["painel", "Painel CPU", "CPU panel", "M4 6h16v9H4z|M9 19h6|M12 15v4"],
    ["metricas", "Métricas", "Metrics", "M4 19V5|M4 19h16|M7 15l4-5 3 3 5-7"],
    ["specs", "Especificações", "Specifications", "M7 3h10v18H7z|M10 7h4|M10 11h4|M10 15h4"],
    ["baterias", "Baterias", "Batteries", "M3 8h15v8H3z|M21 10.5v3|M6 11v2"],
    ["casa", "Casa", "Home", "M4 11 12 4l8 7v9H4z|M10 20v-6h4v6"],
    ["config", "Configurações", "Settings", "M4 7h10|M18 7h2|M14 5v4|M4 17h2|M10 17h10|M6 15v4"],
    ["sobre", "Sobre", "About", "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z|M12 11v6|M12 7.5v.5"]
  ];

  function montarNavegacao() {
    var itens = $("itens");
    limpar(itens);
    PAGINAS.forEach(function (p) {
      var b = el("button", "item", null, icone(p[3]), el("span", "", t(p[1], p[2])));
      b.type = "button";
      b.setAttribute("role", "tab");
      b.dataset.pagina = p[0];
      b.addEventListener("click", function () { mostrar(p[0]); });
      itens.appendChild(b);
    });
    $("perfil-rotulo").textContent = t("PERFIL ATIVO", "ACTIVE PROFILE");
    $("rodape").textContent = "v" + S.versao;
    document.documentElement.lang = S.pt ? "pt-BR" : "en";
  }

  var MONTAR = {};

  function mostrar(pagina, mesma) {
    if (!MONTAR[pagina]) pagina = "visao";
    if (vista && vista.sair) vista.sair();
    vista = null;
    var mudou = pagina !== atual;
    atual = pagina;
    try { sessionStorage.setItem("pagina", pagina); } catch (e) { }
    var def = null;
    PAGINAS.forEach(function (p) { if (p[0] === pagina) def = p; });
    Array.prototype.forEach.call($("itens").children, function (b) {
      b.setAttribute("aria-selected", b.dataset.pagina === pagina ? "true" : "false");
    });
    $("titulo").textContent = t(def[1], def[2]);
    limpar($("acoes"));
    var alvo = $("pagina");
    var rolagem = mesma ? alvo.scrollTop : 0;
    limpar(alvo);
    alvo.className = pagina === "casa" ? "casa" : (mudou && !mesma ? "entra" : "");
    if (mudou) faixa("");
    if (pagina !== "casa") avisar("casa.area", { visivel: false, x: 0, y: 0, w: 0, h: 0 });
    vista = MONTAR[pagina](alvo) || {};
    alvo.scrollTop = rolagem;
    if (E && vista.atualizar) vista.atualizar();
    $("perfil-ativo").textContent = S.ativo;
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

  // ---------------- Visao geral ----------------

  function mostradores() {
    var n1 = el("span", "n"), u1 = el("span", "u"), n2 = el("span", "n"), u2 = el("span", "u");
    var caixa = el("div", "mostradores", null,
      el("div", "mostrador", null, n1, u1), el("div", "mostrador", null, n2, u2));
    caixa.atualizar = function () {
      var p = E ? E.painel : null;
      function por(n, v) { n.textContent = v === null || v === undefined ? "--" : String(v); n.classList.toggle("sem", v === null || v === undefined); }
      por(n1, p ? p.p1 : null);
      por(n2, p ? p.p2 : null);
      u1.textContent = p && p.fahrenheit ? "°F" : "°C";
      u2.textContent = p && !p.percent ? "W" : "%";
    };
    return caixa;
  }

  function seloDoPainel() {
    var s = el("span", "selo");
    s.atualizar = function () {
      var p = E ? E.painel : null;
      if (!p) { s.className = "selo"; s.textContent = "…"; return; }
      if (p.pausado) { s.className = "selo aviso"; s.textContent = t("Pausado", "Paused"); }
      else if (p.conectado) { s.className = "selo ok"; s.textContent = t("Conectado", "Connected"); }
      else { s.className = "selo erro"; s.textContent = t("Painel não encontrado", "Panel not found"); }
    };
    return s;
  }

  function nomeDoSensor(id) {
    var s = sensor(id);
    return s ? s.rotulo : id ? t("sensor ausente", "missing sensor") : t("nenhum", "none");
  }

  MONTAR.visao = function (alvo) {
    var grade = el("div", "grade larga");
    var m = mostradores(), selo = seloDoPainel();
    var legenda = el("div", "legenda");
    var painel = cartao(t("Painel do cooler", "Cooler panel"), "duplo");
    painel.appendChild(m);
    painel.appendChild(legenda);
    var pe = el("div", "inline");
    pe.style.marginTop = "12px";
    pe.appendChild(selo);
    var pausar = el("button", "b");
    pausar.type = "button";
    pausar.addEventListener("click", function () { pedir("pausar").catch(function (e) { faixa(e.message, true); }); });
    pe.appendChild(pausar);
    painel.appendChild(pe);
    grade.appendChild(painel);

    var destaque = cartao(t("Sensores acompanhados", "Tracked sensors"));
    var linhasDeSensor = [];
    if (S.metricas.length === 0)
      destaque.appendChild(el("div", "vazio", t("Escolha sensores na página Métricas.", "Pick sensors on the Metrics page.")));
    else {
      var ls = el("div", "linhas");
      S.metricas.forEach(function (id) {
        var l = linha(nomeDoSensor(id), "--");
        l.sensor = id;
        l.firstChild.className = "k livre";
        linhasDeSensor.push(l);
        ls.appendChild(l);
      });
      destaque.appendChild(ls);
    }
    grade.appendChild(destaque);

    var bat = cartao(t("Baterias", "Batteries"));
    var batLista = el("div");
    bat.appendChild(batLista);
    grade.appendChild(bat);

    var casa = cartao(t("Casa", "Home"));
    var casaLista = el("div", "linhas");
    casa.appendChild(casaLista);
    grade.appendChild(casa);
    alvo.appendChild(grade);

    return {
      atualizar: function () {
        m.atualizar(); selo.atualizar();
        var p = E.painel, perfil = null;
        S.perfis.forEach(function (x) { if (x.nome === p.perfil) perfil = x; });
        legenda.textContent = perfil
          ? nomeDoSensor(perfil.p1) + "  ·  " + nomeDoSensor(perfil.p2)
          : p.perfil;
        pausar.textContent = p.pausado ? t("Retomar", "Resume") : t("Pausar", "Pause");
        linhasDeSensor.forEach(function (l) { l.valor.textContent = comUnidade(l.sensor); });
        desenharBaterias(batLista, true);
        limpar(casaLista);
        if (!E.casa.length) casaLista.appendChild(el("div", "vazio", S.casa.servidor
          ? t("Nenhum aparelho por enquanto.", "No appliances yet.")
          : t("A Casa não está configurada.", "Home is not set up.")));
        E.casa.forEach(function (a) { casaLista.appendChild(linha(a.nome, resumoDoAparelho(a))); });
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

  MONTAR.baterias = function (alvo) {
    var c = cartao(t("Periféricos", "Peripherals"));
    var lista = el("div");
    c.appendChild(lista);
    alvo.appendChild(c);
    var nota = el("p", "vazio", t(
      "O widget da área de trabalho e os avisos de bateria são ajustados na janela clássica.",
      "The desktop widget and battery alerts are set up in the classic window."));
    alvo.appendChild(nota);
    alvo.appendChild(botaoClassica("baterias"));
    return { atualizar: function () { desenharBaterias(lista, false); } };
  };

  function botaoClassica(pagina) {
    var b = el("button", "b", t("Abrir a janela clássica", "Open the classic window"));
    b.type = "button";
    b.addEventListener("click", function () { pedir("abrir", { pagina: pagina || "" }).catch(function (e) { faixa(e.message, true); }); });
    return b;
  }

  // ---------------- Painel CPU e perfis ----------------

  MONTAR.painel = function (alvo) {
    var perfil = null;
    S.perfis.forEach(function (p) { if (p.nome === editando) perfil = p; });
    if (!perfil) S.perfis.forEach(function (p) { if (p.nome === S.ativo) perfil = p; });
    if (!perfil) perfil = S.perfis[0];
    editando = perfil ? perfil.nome : "";

    var chips = el("div", "chips");
    S.perfis.forEach(function (p) {
      var c = el("button", "chip");
      c.type = "button";
      if (p.nome === S.ativo) {
        var ponto = el("span", "ponto");
        ponto.title = t("ativo", "active");
        c.appendChild(ponto);
      }
      c.appendChild(document.createTextNode(p.nome));
      c.setAttribute("aria-pressed", p.nome === editando ? "true" : "false");
      c.addEventListener("click", function () { editando = p.nome; mostrar("painel", true); });
      chips.appendChild(c);
    });
    alvo.appendChild(chips);
    if (!perfil) return {};

    var grade = el("div", "grade larga");
    var previa = cartao(t("No mostrador agora", "On the display now"));
    var m = mostradores(), selo = seloDoPainel();
    previa.appendChild(m);
    var ativoAgora = el("div", "legenda");
    previa.appendChild(ativoAgora);
    var pe = el("div", "inline");
    pe.style.marginTop = "12px";
    pe.appendChild(selo);
    if (perfil.nome !== S.ativo) {
      var ativar = el("button", "b p", t("Ativar este perfil", "Activate this profile"));
      ativar.type = "button";
      ativar.addEventListener("click", function () { mudar("perfil.ativar", { nome: perfil.nome }); });
      pe.appendChild(ativar);
    }
    previa.appendChild(pe);
    grade.appendChild(previa);

    var edicao = cartao(t("Perfil", "Profile") + " " + perfil.nome, "duplo");
    var s1 = seletorDeSensor(perfil.p1, t("(nenhum)", "(none)"));
    var s2 = seletorDeSensor(perfil.p2, t("(nenhum)", "(none)"));
    var v1 = el("span", "legenda"), v2 = el("span", "legenda");
    var divisores = [[0, t("Automático", "Automatic")], [1, "÷ 1"], [10, "÷ 10"], [100, "÷ 100"], [1000, "÷ 1000"]];
    function seletorDeDivisor(valor) {
      var s = el("select");
      divisores.forEach(function (d) { var o = el("option", "", d[1]); o.value = String(d[0]); s.appendChild(o); });
      s.value = String(valor);
      return s;
    }
    var d1 = seletorDeDivisor(perfil.div1), d2 = seletorDeDivisor(perfil.div2);
    var grau = segmentos([[false, "°C"], [true, "°F"]], !!perfil.fahrenheit);
    var pct = segmentos([[true, "%"], [false, "W"]], !!perfil.percent);
    var rodizio = !!perfil.rodizio;

    var duas = el("div", "duas");
    var esq = el("div"), dir = el("div");
    esq.appendChild(campo(t("Mostrador de cima", "Top display"), s1));
    esq.appendChild(v1);
    esq.appendChild(campo(t("Indicador de cima", "Top indicator"), grau));
    esq.appendChild(campo(t("Divisor de cima", "Top divisor"), d1));
    dir.appendChild(campo(t("Mostrador de baixo", "Bottom display"), s2));
    dir.appendChild(v2);
    dir.appendChild(campo(t("Indicador de baixo", "Bottom indicator"), pct));
    dir.appendChild(campo(t("Divisor de baixo", "Bottom divisor"), d2));
    v1.style.margin = "-8px 0 14px"; v2.style.margin = "-8px 0 14px";
    v1.style.display = "block"; v2.style.display = "block";
    duas.appendChild(esq); duas.appendChild(dir);
    edicao.appendChild(duas);
    edicao.appendChild(chave(t("Entra no rodízio", "Part of the rotation"),
      t("O rodízio troca entre os perfis marcados; o tempo fica em Configurações.",
        "The rotation switches between the marked profiles; the time is under Settings."),
      troca(rodizio, function (v) { rodizio = v; })));
    var salvar = el("button", "b p", t("Salvar", "Save"));
    salvar.type = "button";
    salvar.addEventListener("click", function () {
      salvar.disabled = true;
      mudar("perfil.salvar", {
        nome: perfil.nome, p1: s1.value, p2: s2.value, fahrenheit: grau.valor, percent: pct.valor,
        div1: Number(d1.value), div2: Number(d2.value), rodizio: rodizio
      }, t("Perfil salvo.", "Profile saved.")).then(function () { salvar.disabled = false; });
    });
    var acoes = el("div", "inline");
    acoes.style.marginTop = "14px";
    acoes.appendChild(salvar);
    edicao.appendChild(acoes);
    grade.appendChild(edicao);

    var novo = cartao(t("Novo perfil", "New profile"));
    var nome = el("input");
    nome.type = "text"; nome.maxLength = 24;
    nome.placeholder = t("Nome", "Name");
    var criar = el("button", "b", t("Criar", "Create"));
    criar.type = "button";
    function criarPerfil() {
      var n = nome.value.trim();
      if (!n) return;
      editando = n;
      mudar("perfil.criar", { nome: n }, t("Perfil criado a partir do ativo.", "Profile created from the active one."));
    }
    criar.addEventListener("click", criarPerfil);
    nome.addEventListener("keydown", function (e) { if (e.key === "Enter") criarPerfil(); });
    novo.appendChild(el("div", "inline", null, nome, criar));
    var nota = el("p", "vazio", t(
      "Renomear, excluir, importar, exportar e o perfil por jogo ficam na janela clássica.",
      "Rename, delete, import, export and per-game profiles live in the classic window."));
    novo.appendChild(nota);
    novo.appendChild(botaoClassica("painel"));
    grade.appendChild(novo);
    alvo.appendChild(grade);

    return {
      atualizar: function () {
        m.atualizar(); selo.atualizar();
        ativoAgora.textContent = t("Perfil ativo: ", "Active profile: ") + E.painel.perfil;
        v1.textContent = s1.value ? t("Agora: ", "Now: ") + comUnidade(s1.value) : "";
        v2.textContent = s2.value ? t("Agora: ", "Now: ") + comUnidade(s2.value) : "";
      }
    };
  };

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
      var c = el("div", "cartao");
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
        var c = cartao(g.titulo);
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
    $("acoes").appendChild(de);
    if (specs) desenhar(); else coletar();
    return { sair: function () { vivo = false; } };
  };

  // ---------------- Casa ----------------

  MONTAR.casa = function (alvo) {
    var area = el("div");
    area.id = "casa-area";
    alvo.appendChild(area);
    if (!S.casa.servidor) {
      area.appendChild(el("div", "", null,
        el("p", "", t("O servidor da Casa não está configurado neste computador.", "The Home server is not set up on this computer.")),
        botaoClassica("casa")));
      return {};
    }
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

  // ---------------- Configuracoes ----------------

  function campoNumero(valor, min, max) {
    var i = el("input");
    i.type = "number"; i.min = String(min); i.max = String(max); i.step = "1";
    i.value = String(valor);
    return i;
  }

  MONTAR.config = function (alvo) {
    var grade = el("div", "grade larga");
    var geral = cartao(t("Geral", "General"));
    geral.appendChild(chave(t("Iniciar com o Windows", "Start with Windows"), null,
      troca(S.config.inicio, function (v, b) {
        pedir("inicio", { ligado: v }).then(function (r) {
          S.config.inicio = !!r;
          b.setAttribute("aria-checked", r ? "true" : "false");
        }, function (e) {
          b.setAttribute("aria-checked", S.config.inicio ? "true" : "false");
          faixa(e.message, true);
        });
      })));
    var pausa = troca(false, function () { pedir("pausar").catch(function (e) { faixa(e.message, true); }); });
    geral.appendChild(chave(t("Pausar o mostrador", "Pause the display"),
      t("O app para de enviar e o painel apaga os dígitos.", "The app stops sending and the panel blanks its digits."), pausa));
    grade.appendChild(geral);

    var painel = cartao(t("Mostrador", "Display"));
    var maximo = S.maximo || 999;
    var ocioso = campoNumero(S.config.ocioso, 0, maximo), rodizio = campoNumero(S.config.rodizio, 0, maximo);
    painel.appendChild(chave(t("Apagar com o PC ocioso", "Blank when the PC is idle"),
      t("Minutos sem uso até apagar; 0 nunca apaga.", "Idle minutes before blanking; 0 never blanks."), ocioso));
    painel.appendChild(chave(t("Rodízio de perfis", "Profile rotation"),
      t("Segundos em cada perfil marcado, a partir de 2; 0 desliga.", "Seconds on each marked profile, from 2 up; 0 turns it off."), rodizio));
    var salvar = el("button", "b p", t("Salvar", "Save"));
    salvar.type = "button";
    salvar.addEventListener("click", function () {
      var o = Number(ocioso.value), r = Number(rodizio.value);
      if (ocioso.value === "" || rodizio.value === "" || !(o >= 0 && o <= maximo) || !(r === 0 || (r >= 2 && r <= maximo))) {
        faixa(t("Use de 0 a " + maximo + " minutos, e 0 ou de 2 a " + maximo + " segundos.",
          "Use 0 to " + maximo + " minutes, and 0 or 2 to " + maximo + " seconds."), true);
        return;
      }
      mudar("config.salvar", { ocioso: Math.round(o), rodizio: Math.round(r) }, t("Configurações salvas.", "Settings saved."));
    });
    var acoes = el("div", "inline");
    acoes.style.marginTop = "12px";
    acoes.appendChild(salvar);
    painel.appendChild(acoes);
    grade.appendChild(painel);

    var resto = cartao(t("Janela clássica", "Classic window"));
    resto.appendChild(el("p", "vazio", t(
      "Idioma, tema, ordem do menu, widget de baterias, RTSS, cópia de segurança e a conta da Casa ainda são ajustados na janela clássica.",
      "Language, theme, menu order, battery widget, RTSS, backup and the Home account are still set up in the classic window.")));
    resto.appendChild(botaoClassica("config"));
    grade.appendChild(resto);
    alvo.appendChild(grade);

    return { atualizar: function () { pausa.setAttribute("aria-checked", E.painel.pausado ? "true" : "false"); } };
  };

  // ---------------- Sobre ----------------

  MONTAR.sobre = function (alvo) {
    var c = cartao("Mhiagos Control", "sobre");
    c.appendChild(el("p", "", t("Versão ", "Version ") + S.versao));
    c.appendChild(el("p", "", t(
      "Driver alternativo para o painel de 3 dígitos dos air coolers Rise Mode Temp 6, 6 Pro e 8.",
      "Alternative driver for the 3-digit panel of the Rise Mode Temp 6, 6 Pro and 8 air coolers.")));
    var a = el("a", "", "github.com/Feurrado/MhiagosControl");
    a.href = "https://github.com/Feurrado/MhiagosControl";
    c.appendChild(el("p", "", null, a));
    c.appendChild(el("p", "", t(
      "Sensores por LibreHardwareMonitor. Interface em Microsoft Edge WebView2.",
      "Sensors by LibreHardwareMonitor. Interface on Microsoft Edge WebView2.")));
    alvo.appendChild(c);
    var d = el("div", "inline");
    d.style.marginTop = "16px";
    d.appendChild(botaoClassica("sobre"));
    alvo.appendChild(d);
    return {};
  };

  // ---------------- partida ----------------

  function partir() {
    if (!ponte) {
      $("pagina").appendChild(el("div", "vazio", "Esta página só funciona dentro do Mhiagos Control."));
      return;
    }
    ponte.addEventListener("message", aoReceber);
    pedir("iniciar").then(function (r) {
      S = r;
      montarNavegacao();
      var guardada = "";
      try { guardada = sessionStorage.getItem("pagina") || ""; } catch (e) { }
      mostrar(guardada || "visao");
    }, function (e) {
      $("pagina").appendChild(el("div", "vazio", e.message));
    });
  }

  document.addEventListener("keydown", function (e) {
    if (!e.ctrlKey || e.altKey || !S) return;
    var n = Number(e.key);
    if (n >= 1 && n <= PAGINAS.length) { e.preventDefault(); mostrar(PAGINAS[n - 1][0]); }
  });

  partir();
})();
