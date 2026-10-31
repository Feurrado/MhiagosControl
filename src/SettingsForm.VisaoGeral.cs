using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    public partial class SettingsForm : Form
    {
        /// <summary>
        /// A tela que abre: o que e a maquina, como ela esta e o que o cooler
        /// esta mostrando.
        ///
        /// Ate aqui a janela abria em Paineis, que e a pagina de AJUSTAR o
        /// aparelho - e ajustar e coisa que se faz uma vez. O que se olha todo
        /// dia e o estado, e para chegar nele era preciso navegar. A ordem agora
        /// e a do uso: primeiro o retrato, e a configuracao a um clique.
        ///
        /// Nada aqui e editavel de proposito. Cada bloco aponta para a pagina que
        /// o edita, e uma tela que so responde perguntas nao precisa ser
        /// defendida de cliques errados.
        /// </summary>
        private Control BuildPageVisaoGeral()
        {
            _pgVisao = new Pagina();

            // Sem rolagem: esta pagina e desenhada para caber, e a altura que
            // sobra vai para o cartao do cooler em vez de virar vao. Uma barra de
            // rolagem aqui so reservaria a largura dela sem nunca aparecer.
            MontarDestaques();

            // Rolagem so para a Visao geral nova na janela estreita (as colunas
            // viram uma); a classica continua desenhada para caber.
            _pgVisao.RolarNaVertical();

            _cardCooler = new CartaoDoCooler();
            _cardCooler.Title = T.CoolerCard;
            _pgVisao.Controls.Add(_cardCooler);

            // A previa DE VERDADE, a mesma peca que a pagina de Paineis usa.
            //
            // E a unica coisa nesta tela que nenhum outro monitor de hardware
            // mostra, e por isso ela e o centro: responde "esta funcionando?" sem
            // ler uma palavra. A primeira versao desta pagina resumia o cooler a
            // uma linha de texto com VID e PID - o dado mais tecnico e menos util
            // que havia para escolher.
            _vgPreview = new PanelPreview();
            _cardCooler.Controls.Add(_vgPreview);

            // Visao geral nova: atalho para a pagina que ajusta o display.
            _vgAbrirPaineis = MakeLabel(T.VisaoAbrirPaineis, 0, 0, Ui.FontSmall);
            _vgAbrirPaineis.ForeColor = Ui.AccentText;
            _vgAbrirPaineis.Cursor = Cursors.Hand;
            _vgAbrirPaineis.Visible = false;
            _vgAbrirPaineis.Click += delegate { AbrirPaginaPelaChave("paineis"); };
            _cardCooler.Controls.Add(_vgAbrirPaineis);

            // Duas linhas, e nao quatro.
            //
            // "Mostrador de cima" e "de baixo" estavam aqui E na coluna da
            // direita, onde cada um tem um grafico inteiro com nome, valor e
            // historico. Repetidos numa coluna de 320 px eles nao cabiam - saiam
            // como "Temperatura do..." - e o que se perdia no corte era
            // exatamente o que o cartao ao lado ja dizia por extenso.
            _vgPerfil = LinhaDoCooler(T.ActiveProfile);
            _vgPainel = LinhaDoCooler(T.PanelLabel);

            // A HISTORIA dos dois mostradores, ao lado do estado deles.
            //
            // A coluna da direita existia como sobra: o cartao do cooler crescia
            // para ocupar a altura e o conteudo dele ficava boiando no meio, o
            // que so mudou o vazio de lugar. Duas curvas grandes das leituras que
            // vao para a peca preenchem com dado, nao com ar - e respondem o que
            // a linha "Mostrador de cima - 54,5 C" nao responde: se 54,5 e o de
            // sempre ou se subiu agora.
            _vgP1 = MostradorEmCurva();
            _vgP2 = MostradorEmCurva();

            // Carga do headset e do mouse, logo acima das especificacoes: muda
            // devagar e se consulta de passagem, entao fica na faixa de baixo,
            // longe das leituras que pedem o olho.
            _cardPerif = new PeripheralStrip();
            _cardPerif.BateriasPedidas += delegate { AbrirPaginaPelaChave("baterias"); };
            _pgVisao.Controls.Add(_cardPerif);

            // A casa (ar e lavadora) entre os perifericos e as especificacoes:
            // estado e controle basico sem trocar de pagina.
            _cardCasa = new ResumoDaCasa();
            _cardCasa.AbrirCasa += delegate { AbrirPaginaPelaChave("casa"); };
            _pgVisao.Controls.Add(_cardCasa);

            // Visao geral nova (a padrao): cabecalho, casa e as caixas de baixo.
            EventosDaVisao.Iniciar();
            _vgCabecalho = new CabecalhoDaVisao();
            _vgAr = new CartaoDoArVisao();
            _vgLav = new CartaoDaLavadoraVisao();
            _vgConvite = new ConviteDaCasa();
            EventHandler casa = delegate { AbrirPaginaPelaChave("casa"); };
            _vgAr.AbrirCasa += casa; _vgLav.AbrirCasa += casa; _vgConvite.AbrirCasa += casa;
            _vgPerif = new CaixaDePerifericos();
            _vgPerif.BateriasPedidas += delegate { AbrirPaginaPelaChave("baterias"); };
            _vgEventos = new CaixaDeEventos();
            foreach (Control c in new Control[] { _vgCabecalho, _vgAr, _vgLav, _vgConvite, _vgPerif, _vgEventos })
            {
                c.Visible = false;
                _pgVisao.Controls.Add(c);
            }
            // A pagina se rearranja quando a casa muda: o cartao da lavadora so
            // existe com ciclo em andamento, e o convite troca de lugar com o ar.
            EventHandler casaMudou = delegate { Visao.NaInterface(_pgVisao, delegate { if (_cfg.VisaoGeralNova && _pgVisao.Visible) ArranjarVisaoGeral(); }); };
            ServicoCasa.Mudou += casaMudou;
            _pgVisao.Disposed += delegate { ServicoCasa.Mudou -= casaMudou; };

            _cardSpecs = new Card();
            _cardSpecs.Title = null;    // tira sozinho: e uma tira de referencia
            _pgVisao.Controls.Add(_cardSpecs);

            MontarSpecs();

            // O cartao do cooler (fora das curvas, que abrem Metricas) leva a Paineis;
            // a faixa de especificacoes, a pagina Especificacoes.
            LevarAPagina(_cardCooler, "paineis");
            LevarAPagina(_cardSpecs, "specs");

            AtualizarVisaoGeral(null);
            return _pgVisao;
        }

        /// <summary>Clique no controle ou nos filhos dele (menos curvas e o link que ja tem destino) abre a pagina.</summary>
        private void LevarAPagina(Control c, string chave)
        {
            EventHandler abrir = delegate { AbrirPaginaPelaChave(chave); };
            c.Cursor = Cursors.Hand;
            c.Click += abrir;
            foreach (Control f in c.Controls)
            {
                if (f is MetricCard || f == _vgAbrirPaineis) continue;
                f.Cursor = Cursors.Hand;
                f.Click += abrir;
            }
        }

        /// <summary>
        /// Uma linha do cartao do cooler: rotulo a esquerda, valor a direita.
        ///
        /// Devolve o rotulo do VALOR. O do titulo vai no Tag, para os dois
        /// sumirem juntos quando nao ha o que dizer - e a posicao vertical sai do
        /// arranjo, e nao da ordem de criacao: na primeira versao as linhas
        /// nasciam com valor nulo, nao avancavam o y, e as quatro acabavam
        /// empilhadas na mesma altura.
        /// </summary>
        private Label LinhaDoCooler(string titulo)
        {
            Label cap = MakeLabel(titulo, 0, 0, Ui.FontSmall);
            cap.ForeColor = Ui.Muted;
            cap.Tag = titulo;
            _cardCooler.Controls.Add(cap);

            Label val = MakeLabel("", 0, 0, Ui.FontMed);
            val.TextAlign = ContentAlignment.MiddleRight;
            val.Tag = cap;
            _cardCooler.Controls.Add(val);
            return val;
        }

        private MetricCard MostradorEmCurva()
        {
            MetricCard c = new MetricCard();
            c.Editavel = false;
            c.Janela = MetricHistory.JanelaVisaoGeral;
            ClicarAbreMetricas(c);
            _pgVisao.Controls.Add(c);
            return c;
        }

        /// <summary>Clique numa curva da Visao geral: abre Metricas com essa leitura na faixa de cima, se ela estiver na grade.</summary>
        private void ClicarAbreMetricas(MetricCard c)
        {
            c.Clicavel = true;
            c.Cursor = Cursors.Hand;
            c.Click += delegate { AbrirLeituraEmMetricas(c.SensorId); };
        }

        internal void AbrirLeituraEmMetricas(string id)
        {
            AbrirPaginaPelaChave("metricas");
            if (string.IsNullOrEmpty(id)) return;
            foreach (MetricCard m in _cards)
                if (m.SensorId == id) { EscolherLeituraDoNavegador(id); return; }
        }

        /// <summary>Aponta um cartao de curva para o sensor que esta num mostrador.</summary>
        private void ApontarMostrador(MetricCard c, string id, string titulo)
        {
            if (c == null) return;

            SensorEntry s = null;
            if (!string.IsNullOrEmpty(id) && _sensors != null)
                foreach (SensorEntry e in _sensors)
                    if (e != null && e.Id == id) { s = e; break; }

            if (s == null)
            {
                c.Visible = true;
                c.SensorId = "";
                c.Titulo = c.Compacto ? Visao.Maiusculas(titulo) : titulo;
                c.Sub = T.NoSensorChosen;
                c.Unidade = "";
                c.Atencao = null; c.Perigo = null;
                c.Push(null);
                return;
            }

            // Trocar de perfil troca o sensor do mostrador, e o novo pode nao
            // estar sendo acompanhado - o cartao abriria sem curva.
            if (c.SensorId != s.Id)
                MetricHistory.SeguirTambem(new string[] { s.Id });

            c.Visible = true;
            c.SensorId = s.Id;
            c.Titulo = c.Compacto ? Visao.Maiusculas(titulo + " · " + MetricPicker.RotuloCurto(s)) : titulo + "  ·  " + MetricPicker.RotuloCurto(s);
            c.Unidade = s.EffectiveUnit;
            c.Sub = SystemInfo.Limpar(s.Hardware);

            float? at, pe;
            MetricPicker.Faixas(s.EffectiveUnit, s.Id, out at, out pe);
            c.Atencao = at; c.Perigo = pe;
        }

        /// <summary>
        /// A tira de especificacoes, embaixo e discreta.
        ///
        /// Especificacao e material de REFERENCIA: consultada uma vez, nao
        /// monitorada. Ela abria esta pagina, no topo e em corpo grande, que e
        /// onde o olho cai primeiro - o lugar do que muda, nao do que e fixo
        /// desde que a maquina foi montada.
        /// </summary>
        private void MontarSpecs()
        {
            SystemInfo info = SystemInfo.From(_sensors);

            List<string[]> linhas = new List<string[]>();
            linhas.Add(new string[] { T.SpecCpu, Junta(info.Cpu, info.CpuNucleos) });
            linhas.Add(new string[] { T.SpecGpu, Junta(info.Gpu, info.GpuMemoria) });
            linhas.Add(new string[] { T.SpecRam, info.Ram });
            if (!string.IsNullOrEmpty(info.Placa))
                linhas.Add(new string[] { T.SpecBoard, info.Placa });
            linhas.Add(new string[] { T.SpecOs, info.Sistema });

            List<Label> caps = new List<Label>();
            List<Label> vals = new List<Label>();
            foreach (string[] l in linhas)
            {
                if (string.IsNullOrEmpty(l[1])) continue;

                Label cap = MakeLabel(l[0], 0, 0, Ui.FontSmall);
                cap.ForeColor = Ui.Faint;
                cap.AutoEllipsis = true;
                _cardSpecs.Controls.Add(cap);
                caps.Add(cap);

                Label val = MakeLabel(l[1], 0, 0, Ui.FontSemi);
                val.AutoEllipsis = true;
                _cardSpecs.Controls.Add(val);
                vals.Add(val);
            }
            _vgSpecCap = caps.ToArray();
            _vgSpecVal = vals.ToArray();
        }

        /// <summary>
        /// As leituras em destaque: temperatura e uso das duas pecas que aquecem,
        /// mais o uso da memoria.
        ///
        /// Escolha fixa e automatica, e nao o conjunto que a pessoa montou na aba
        /// Metricas. Sao perguntas diferentes: la e "o que EU quero acompanhar",
        /// aqui e "esta tudo bem?" - e essa se responde com as mesmas cinco
        /// leituras em qualquer maquina.
        ///
        /// Os blocos tem 140 px de altura de proposito: dai para cima o MetricCard
        /// desenha grade e a linha de maximo e media. Um numero sozinho nao diz se
        /// e alto - 72 graus e rotina sob carga e alarme em repouso - e a serie
        /// atras e a linha embaixo sao o "comparado a que" que falta a ele.
        /// </summary>
        private void MontarDestaques()
        {
            int indice = 0;
            foreach (SensorEntry s in MetricPicker.Destaques(_sensors))
            {
                MetricCard c = new MetricCard();
                c.Editavel = false;
                c.Titulo = T.DashboardMetric(indice, MetricPicker.RotuloCurto(s));
                _vgTitulos.Add(c.Titulo);
                _vgTitulosMono.Add(T.DashboardMetricMono(indice, MetricPicker.RotuloCurto(s)));
                c.Sub = SystemInfo.Limpar(s.Hardware);
                c.Unidade = s.EffectiveUnit;
                c.SensorId = s.Id;
                c.Janela = MetricHistory.JanelaVisaoGeral;
                float? at, pe;
                MetricPicker.Faixas(s.EffectiveUnit, s.Id, out at, out pe);
                c.Atencao = at; c.Perigo = pe;

                _pgVisao.Controls.Add(c);
                _vgTiles.Add(c);
                ClicarAbreMetricas(c);
                _vgIds.Add(s.Id);
                indice++;
            }

            // O que foi POSTO na tela e o que precisa de historico. A lista do
            // arranque foi montada com os sensores frios e pode ter escolhido
            // outro sensor para o mesmo lugar.
            MetricHistory.SeguirTambem(_vgIds);
        }

        /// <summary>
        /// Arranja a Visao geral: a nova (padrao) ou a classica, se escolhida
        /// em Configuracoes. As duas usam os mesmos destaques, previa e curvas;
        /// cada uma esconde o que e so da outra.
        /// </summary>
        private void ArranjarVisaoGeral()
        {
            if (_pgVisao == null || _cardCooler == null) return;
            if (_pgVisao.Width <= 0 || _pgVisao.Height <= 0) return;
            bool nova = _cfg.VisaoGeralNova;
            EstiloDaVisao(nova);
            if (nova) ArranjarVisaoGeralNova(); else ArranjarVisaoGeralClassica();
            _pgVisao.Sincronizar();
        }

        /// <summary>
        /// Troca o que difere entre as duas: quem aparece, o estilo dos
        /// cartoes de curva e onde moram as curvas dos mostradores (dentro do
        /// cartao do cooler na nova, soltas na pagina na classica).
        /// </summary>
        private void EstiloDaVisao(bool nova)
        {
            foreach (Control c in new Control[] { _vgCabecalho, _vgAr, _vgLav, _vgConvite, _vgPerif, _vgEventos })
                if (c != null && !nova) c.Visible = false;
            _cardPerif.Visible = !nova;
            if (_cardCasa != null) _cardCasa.Visible = !nova;
            _vgAbrirPaineis.Visible = nova;
            _vgPreview.Visible = true;
            _cardCooler.Nova = nova;
            _cardCooler.Title = nova ? T.VisaoChipCooler : T.CoolerCard;
            if (!nova) _cardCooler.Linha = -1;

            for (int i = 0; i < _vgTiles.Count; i++)
            {
                MetricCard t = _vgTiles[i];
                t.Compacto = nova;
                t.Quente = nova && (t.Unidade ?? "").IndexOf('°') >= 0;
                if (i < _vgTitulos.Count) t.Titulo = nova ? _vgTitulosMono[i] : _vgTitulos[i];
                t.Invalidate();
            }
            Control casaDasCurvas = nova ? (Control)_cardCooler : _pgVisao;
            foreach (MetricCard c in new MetricCard[] { _vgP1, _vgP2 })
            {
                if (c == null) continue;
                if (c.Parent != casaDasCurvas)
                {
                    if (c.Parent != null) c.Parent.Controls.Remove(c);
                    casaDasCurvas.Controls.Add(c);
                }
                c.Compacto = nova;
                c.SemMoldura = nova;
                c.Quente = nova && (c.Unidade ?? "").IndexOf('°') >= 0;
                c.Invalidate();
            }
            foreach (Label val in new Label[] { _vgPerfil, _vgPainel })
                if (val != null)
                {
                    val.TextAlign = nova ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight;
                    Label cap = val.Tag as Label;
                    string t = cap != null ? cap.Tag as string : null;
                    if (t != null) cap.Text = nova ? Visao.Maiusculas(t) : t;
                }
            _cardCooler.Invalidate();
            AtualizarVisaoGeral(null);
        }

        private void ArranjarVisaoGeralClassica()
        {

            int Esp = Ui.S(8);
            int AlturaBloco = Ui.S(104);
            int AlturaSpecs = Ui.S(56);
            int AlturaPerif = Ui.S(80);
            int AlturaCasa = _cardCasa != null ? Ui.S(ResumoDaCasa.Altura) : 0;
            int EspCasa = _cardCasa != null ? Esp : 0;
            int MeioMin = Ui.S(240);

            int disp = _pgVisao.Width - Ui.S(4);
            int alvo = disp > LarguraMaxDaPagina ? LarguraMaxDaPagina : disp;
            if (alvo < Ui.S(560)) alvo = Ui.S(560);
            int x0 = (disp - alvo) / 2;
            if (x0 < 0) x0 = 0;

            _pgVisao.SuspendLayout();
            try
            {
                int n = _vgTiles.Count;
                if (n > 0)
                {
                    int larg = (alvo - (n - 1) * Esp) / n;
                    int usado = 0;
                    for (int i = 0; i < n; i++)
                    {
                        int w = (i == n - 1) ? alvo - usado : larg;
                        _vgTiles[i].SetBounds(x0 + usado, 0, w, AlturaBloco);
                        usado += w + Esp;
                    }
                }

                int yMeio = (n > 0 ? AlturaBloco + Esp : 0);
                int ySpecs = _pgVisao.Height - AlturaSpecs;
                int yPerif = ySpecs - Esp - AlturaCasa - EspCasa - AlturaPerif;
                int hMeio = yPerif - Esp - yMeio;
                if (hMeio < MeioMin)
                {
                    hMeio = MeioMin;
                    yPerif = yMeio + hMeio + Esp;
                    ySpecs = yPerif + AlturaPerif + Esp + AlturaCasa + EspCasa;
                }

                // Duas colunas na faixa do meio. A esquerda e mais estreita: o
                // que ela guarda e uma foto quadrada e quatro linhas curtas, e
                // dar-lhe metade da tela era o que espalhava tudo - a previa
                // ficava perdida no meio de um cartao vazio, com o texto a meio
                // metro de distancia.
                int wEsq = alvo * 42 / 100;
                if (wEsq < Ui.S(300)) wEsq = Ui.S(300);
                if (wEsq > alvo - Ui.S(260)) wEsq = alvo - Ui.S(260);
                // A divisoria das duas colunas cai na divisa entre dois blocos da
                // fileira de cima: com o corte solto (42%) as bordas dos cartoes
                // de cima e as do meio nunca se encontravam. Vale a divisa mais
                // proxima do ideal que deixe as duas colunas com largura util.
                int melhor = -1;
                for (int i = 0; i < n - 1; i++)
                {
                    int fim = _vgTiles[i].Right - x0;
                    if (fim < Ui.S(280) || alvo - fim - Esp < Ui.S(170)) continue;
                    if (melhor < 0 || Math.Abs(fim - wEsq) < Math.Abs(melhor - wEsq)) melhor = fim;
                }
                if (melhor >= 0) wEsq = melhor;
                int wDir = alvo - wEsq - Esp;

                // A altura do meio e a da FOTO, e nao o que sobrou da janela: o
                // cartao abraca a previa quadrada (titulo + foto + linhas de perfil)
                // e os dois graficos acompanham essa altura.
                int linhasCooler = 0;
                if (_vgPerfil != null && _vgPerfil.Visible) linhasCooler++;
                if (_vgPainel != null && _vgPainel.Visible) linhasCooler++;
                int hIdeal = Ui.S(46) + (wEsq - Ui.S(32)) + Ui.S(14) + linhasCooler * Ui.S(26) + Ui.S(14);
                if (hIdeal < hMeio) hMeio = Math.Max(hIdeal, MeioMin);
                yPerif = yMeio + hMeio + Esp;
                ySpecs = yPerif + AlturaPerif + Esp + AlturaCasa + EspCasa;

                _cardCooler.SetBounds(x0, yMeio, wEsq, hMeio);

                int hCurva = (hMeio - Esp) / 2;
                if (_vgP1 != null) _vgP1.SetBounds(x0 + wEsq + Esp, yMeio, wDir, hCurva);
                if (_vgP2 != null)
                    _vgP2.SetBounds(x0 + wEsq + Esp, yMeio + hCurva + Esp, wDir, hMeio - hCurva - Esp);

                _cardPerif.SetBounds(x0, yPerif, alvo, AlturaPerif);
                if (_cardCasa != null) _cardCasa.SetBounds(x0, yPerif + AlturaPerif + Esp, alvo, AlturaCasa);
                _cardSpecs.SetBounds(x0, ySpecs, alvo, AlturaSpecs);

                // O bloco inteiro centrado tambem na vertical: na horizontal ja
                // sobra margem igual dos dois lados, e o vao so embaixo fazia a
                // pagina parecer cortada. Sem folga (janela baixa) fica no topo.
                int y0 = (_pgVisao.Height - (ySpecs + AlturaSpecs)) / 2;
                if (y0 > 0)
                {
                    foreach (Control c in _vgTiles) c.Top += y0;
                    _cardCooler.Top += y0;
                    if (_vgP1 != null) _vgP1.Top += y0;
                    if (_vgP2 != null) _vgP2.Top += y0;
                    _cardPerif.Top += y0;
                    if (_cardCasa != null) _cardCasa.Top += y0;
                    _cardSpecs.Top += y0;
                }

                ArranjarCooler();
                ArranjarSpecs();
            }
            finally { _pgVisao.ResumeLayout(); }
        }

        /// <summary>
        /// A Visao geral nova, de cima para baixo: cabecalho; as cinco leituras
        /// em destaque; duas colunas (casa, perifericos e eventos a esquerda,
        /// o display do cooler com as curvas dos mostradores a direita); a
        /// tira de especificacoes. Estreita, as colunas viram uma e as cinco
        /// leituras quebram em 3 + 2, com rolagem vertical se precisar.
        /// </summary>
        private void ArranjarVisaoGeralNova()
        {
            int Esp = Ui.S(12);
            int AlturaBloco = Ui.S(96);
            int AlturaSpecs = Ui.S(56);

            int disp = _pgVisao.Width - Pagina.LarguraDaBarra - Ui.S(4);
            int alvo = Math.Min(disp, LarguraMaxDaPagina);
            if (alvo < Ui.S(480)) alvo = Ui.S(480);
            int x0 = Math.Max(0, (disp - alvo) / 2);

            _pgVisao.SuspendLayout();
            try
            {
                int y = 0;
                int hc = _vgCabecalho.AlturaPara(alvo);
                _vgCabecalho.Visible = true;
                _vgCabecalho.SetBounds(x0, y, alvo, hc);
                y += hc + Esp;

                // As cinco leituras numa fileira, ou 3 + 2 quando cada uma
                // ficaria estreita demais para o numero e o "max · med".
                int n = _vgTiles.Count;
                int porLinha = n;
                if (n > 3 && (alvo - (n - 1) * Esp) / n < Ui.S(150)) porLinha = (n + 1) / 2;
                for (int ini = 0; ini < n; ini += porLinha)
                {
                    int k = Math.Min(porLinha, n - ini);
                    int larg = (alvo - (k - 1) * Esp) / k, usado = 0;
                    for (int i = 0; i < k; i++)
                    {
                        int w = (i == k - 1) ? alvo - usado : larg;
                        _vgTiles[ini + i].SetBounds(x0 + usado, y, w, AlturaBloco);
                        usado += w + Esp;
                    }
                    y += AlturaBloco + Esp;
                }

                bool duas = alvo >= Ui.S(860);
                int wEsq = duas ? (alvo - Esp) * 5 / 9 : alvo;
                int wDir = duas ? alvo - wEsq - Esp : alvo;

                // Esquerda: o ar, a lavadora so com ciclo, ou o convite sem conta.
                // Le de novo: sair da conta nem sempre passa pelo aviso do servico.
                _vgAr.Puxar(); _vgLav.Puxar();
                int ye = y;
                bool temAr = _vgAr.Ar != null, temCiclo = _vgLav.TemCiclo;
                bool convite = !temAr && !temCiclo && (!ServicoCasa.Logado || ServicoCasa.Aparelhos.Count == 0);
                _vgAr.Visible = temAr;
                _vgLav.Visible = temCiclo;
                _vgConvite.Visible = convite;
                if (temAr) { _vgAr.SetBounds(x0, ye, wEsq, Ui.S(CartaoDoArVisao.Altura)); ye = _vgAr.Bottom + Esp; }
                if (temCiclo) { _vgLav.SetBounds(x0, ye, wEsq, Ui.S(CartaoDaLavadoraVisao.Altura)); ye = _vgLav.Bottom + Esp; }
                if (convite) { _vgConvite.SetBounds(x0, ye, wEsq, Ui.S(ConviteDaCasa.Altura)); ye = _vgConvite.Bottom + Esp; }
                int wc = (wEsq - Esp) / 2, hCaixa = Ui.S(CaixaDePerifericos.Altura);
                _vgPerif.Visible = _vgEventos.Visible = true;
                _vgPerif.SetBounds(x0, ye, wc, hCaixa);
                _vgEventos.SetBounds(x0 + wc + Esp, ye, wEsq - wc - Esp, hCaixa);
                ye += hCaixa;

                // Direita (ou embaixo, na estreita): o display do cooler.
                int xd = duas ? x0 + wEsq + Esp : x0, yd = duas ? y : ye + Esp;
                _cardCooler.SetBounds(xd, yd, wDir, AlturaDoCoolerNovo(wDir));
                // As duas colunas terminam juntas: a mais curta estica a ultima peca.
                if (duas && _cardCooler.Bottom < ye) _cardCooler.Height = ye - _cardCooler.Top;
                else if (duas && ye < _cardCooler.Bottom) { _vgPerif.Height = _vgEventos.Height = _cardCooler.Bottom - _vgPerif.Top; ye = _cardCooler.Bottom; }
                ArranjarCoolerNovo();

                y = Math.Max(ye, _cardCooler.Bottom) + Esp;
                _cardSpecs.SetBounds(x0, y, alvo, AlturaSpecs);
                y += AlturaSpecs;

                // Centrado na vertical quando sobra altura, como a classica.
                int y0 = (_pgVisao.Height - y) / 2;
                if (y0 > 0)
                    foreach (Control c in _pgVisao.Controls)
                        if (c.Visible && c.Dock == DockStyle.None) c.Top += y0;

                ArranjarSpecs();
            }
            finally { _pgVisao.ResumeLayout(); }
        }

        private static int LadoDaPrevia(int w) { return Math.Max(Ui.S(96), Math.Min(Ui.S(150), (w - Ui.S(48)) * 45 / 100)); }

        private int AlturaDoCoolerNovo(int w)
        {
            return _cardCooler.TitleHeight + Ui.S(14) + LadoDaPrevia(w) + Ui.S(14) + Ui.S(6) + 2 * Ui.S(84) + Ui.S(4) + Ui.S(10);
        }

        /// <summary>
        /// O cartao do cooler da Visao geral nova: a previa real a esquerda,
        /// perfil, painel e o atalho para Paineis a direita; embaixo da linha
        /// fina, as curvas dos mostradores de cima e de baixo.
        /// </summary>
        private void ArranjarCoolerNovo()
        {
            int w = _cardCooler.Width, h = _cardCooler.Height, m = Ui.S(16);
            int lado = LadoDaPrevia(w), topo = _cardCooler.TitleHeight + Ui.S(14);
            _vgPreview.SetBounds(m, topo, lado, lado);
            int x = m + lado + Ui.S(18), wt = Math.Max(Ui.S(60), w - x - m), y = topo;
            foreach (Label val in new Label[] { _vgPerfil, _vgPainel })
            {
                if (val == null || !val.Visible) continue;
                Label cap = val.Tag as Label;
                if (cap != null) cap.SetBounds(x, y, wt, Ui.S(17));
                val.SetBounds(x, y + Ui.S(17), wt, Ui.S(22));
                y += Ui.S(48);
            }
            _vgAbrirPaineis.SetBounds(x, Math.Max(y, topo + lado - Ui.S(18)), wt, Ui.S(18));
            int linha = topo + lado + Ui.S(14);
            _cardCooler.Linha = linha;
            int yc = linha + Ui.S(6), hc = (h - yc - Ui.S(10) - Ui.S(4)) / 2;
            if (_vgP1 != null) _vgP1.SetBounds(1, yc, w - 2, hc);
            if (_vgP2 != null) _vgP2.SetBounds(1, yc + hc + Ui.S(4), w - 2, h - Ui.S(10) - (yc + hc + Ui.S(4)));
            _cardCooler.Invalidate();
        }

        /// <summary>
        /// Previa em cima, as quatro linhas embaixo.
        ///
        /// Empilhado, e nao lado a lado: numa coluna estreita a foto quadrada nao
        /// deixa largura util para o texto ao lado, e foi assim que "Perfil
        /// ativo" acabou encostado na borda direita da janela, longe da peca que
        /// ele descreve. Um embaixo do outro os dois ficam no mesmo eixo.
        /// </summary>
        private void ArranjarCooler()
        {
            int w = _cardCooler.Width, h = _cardCooler.Height;

            Label[] linhas = { _vgPerfil, _vgPainel };
            int vis = 0;
            foreach (Label l in linhas) if (l != null && l.Visible) vis++;

            int AlturaLinha = Ui.S(26);
            int hTexto = vis * AlturaLinha;

            // A previa fica com o que sobra, sempre quadrada: ela encaixa a foto
            // pela altura, entao largura a mais so viraria fundo preto ao lado.
            int lado = Math.Min(w - Ui.S(32), h - Ui.S(52) - hTexto - Ui.S(12));
            if (lado < Ui.S(80)) lado = Ui.S(80);

            // O BLOCO INTEIRO centrado na altura util, e nao encostado no topo.
            //
            // Numa coluna estreita a previa e limitada pela LARGURA, nao pela
            // altura: sobrava um palmo de fundo embaixo das duas linhas de
            // texto, e a peca - que e o assunto do cartao - ficava empurrada
            // para cima como se o resto tivesse sido cortado.
            int bloco = lado + Ui.S(14) + hTexto;
            int topo = Ui.S(46) + (h - Ui.S(46) - bloco) / 2;
            if (topo < Ui.S(46)) topo = Ui.S(46);

            _vgPreview.SetBounds((w - lado) / 2, topo, lado, lado);

            int y = topo + lado + Ui.S(14);
            foreach (Label val in linhas)
            {
                if (val == null || !val.Visible) continue;
                Label cap = val.Tag as Label;
                // Rotulo curto a esquerda, valor com TODO o resto: com 150 px
                // reservados a um rotulo de uma palavra, o que sobrava para o
                // valor nao dava nem para o nome do perfil.
                if (cap != null) cap.SetBounds(Ui.S(16), y + Ui.S(3), Ui.S(92), Ui.S(17));
                val.SetBounds(Ui.S(112), y, w - Ui.S(128), Ui.S(20));
                y += AlturaLinha;
            }
        }

        /// <summary>
        /// A tira de referencia, com as colunas coladas ao conteudo.
        ///
        /// Antes a largura era repartida em partes iguais: "16 GB" recebia o
        /// mesmo espaco que "Ryzen 5 5600X - 6 nucleos - 12 threads", e o que
        /// sobrava virava um vao entre os dois. Quatro dados curtos espalhados
        /// por novecentos pixels nao se leem como uma tira - leem-se como quatro
        /// coisas soltas. Cada coluna toma o que precisa, e o resto fica no fim,
        /// onde e margem em vez de buraco.
        /// </summary>
        private void ArranjarSpecs()
        {
            if (_vgSpecVal == null || _vgSpecVal.Length == 0) return;

            int Vao = Ui.S(26);
            int n = _vgSpecVal.Length;

            int[] larg = new int[n];
            int soma = 0;
            for (int i = 0; i < n; i++)
            {
                int a = TextRenderer.MeasureText(_vgSpecCap[i].Text, Ui.FontSmall).Width;
                int b = TextRenderer.MeasureText(_vgSpecVal[i].Text, Ui.FontSemi).Width;
                larg[i] = Math.Max(a, b) + Ui.S(14);
                soma += larg[i];
            }

            // Se nao couber, todas encolhem na proporcao: cortar so a ultima
            // deixaria justamente o sistema operacional sem nome.
            int disp = _cardSpecs.Width - Ui.S(32) - (n - 1) * Vao;
            if (soma > disp)
            {
                // Aperto: o vao encolhe antes de qualquer texto.
                Vao = Ui.S(12);
                disp = _cardSpecs.Width - Ui.S(32) - (n - 1) * Vao;
            }
            if (soma > disp && soma > 0)
            {
                // Quem e curto ("16 GB", o sistema) fica inteiro; o que sobra e dividido
                // entre os longos, que terminam em reticencias. Repartir na proporcao
                // cortava todos, ate o rotulo ("Memo").
                bool[] fixa = new bool[n];
                int restante = disp, livres = n;
                bool mudou = true;
                while (mudou && livres > 0)
                {
                    mudou = false;
                    int parte = restante / livres;
                    for (int i = 0; i < n; i++)
                        if (!fixa[i] && larg[i] <= parte) { fixa[i] = true; restante -= larg[i]; livres--; mudou = true; }
                }
                if (livres > 0)
                    for (int i = 0; i < n; i++)
                        if (!fixa[i])
                        {
                            int minimo = TextRenderer.MeasureText(_vgSpecCap[i].Text, Ui.FontSmall).Width + Ui.S(14);
                            larg[i] = Math.Max(minimo, restante / livres);
                        }
            }

            // Centralizada, e nao encostada a esquerda.
            //
            // Com as colunas coladas ao conteudo, a sobra ia toda para o fim - e
            // quatro dados curtos alinhados a esquerda, com um vao de trezentos
            // pixels do lado direito, se leem como uma tira que ficou pela
            // metade. Centralizada, o que sobra vira margem dos dois lados, que e
            // a mesma decisao que a pagina inteira ja toma acima de 1100 px.
            // A folga de 14 px da ultima coluna nao e texto: contada, a tira inteira
            // saia deslocada para a esquerda do centro do cartao.
            int total = (n - 1) * Vao - Ui.S(14);
            for (int i = 0; i < n; i++) total += larg[i];

            int x = (_cardSpecs.Width - total) / 2;
            if (x < Ui.S(16)) x = Ui.S(16);

            for (int i = 0; i < n; i++)
            {
                int hCap = Ui.FontSmall.Height, hVal = Ui.FontSemi.Height;
                int topoSpec = (_cardSpecs.Height - (hCap + hVal)) / 2;
                _vgSpecCap[i].SetBounds(x, topoSpec, larg[i], hCap);
                _vgSpecVal[i].SetBounds(x, topoSpec + hCap, larg[i], hVal);
                x += larg[i] + Vao;
            }
        }

        /// <summary>Estado do cooler e das duas leituras que ele mostra.</summary>
        private void AtualizarVisaoGeral(Dictionary<string, float> snap)
        {
            if (_cardCooler == null) return;

            IPanelStatusData source = _data as IPanelStatusData;
            PanelStatus status = source != null ? source.CurrentPanelStatus() : null;
            PanelFrame frame = status != null ? status.Frame : null;
            Profile displayed = frame != null && frame.Profile != null
                ? frame.Profile : _session.ActiveProfile;
            string id1 = displayed.Panel1Id;
            string id2 = displayed.Panel2Id;

            // Previa sem runtime (testes e ferramentas) usa o perfil salvo.
            if (source == null)
            {
                SensorEntry s1 = Achar(id1), s2 = Achar(id2);
                frame = new PanelFrame(
                    Scaling.Prepare(s1, Scaling.Effective(displayed.Divisor1, s1), displayed.Fahrenheit).Value,
                    Scaling.Prepare(s2, Scaling.Effective(displayed.Divisor2, s2), false).Value,
                    displayed.Fahrenheit, displayed.Percent, displayed);
            }
            bool showing = frame != null && (status == null || (status.Connected && !status.Paused));
            _vgPreview.Value1 = showing ? frame.Panel1 : null;
            _vgPreview.Value2 = showing ? frame.Panel2 : null;
            _vgPreview.Fahrenheit = frame != null ? frame.Fahrenheit : displayed.Fahrenheit;
            _vgPreview.Percent = frame != null ? frame.Percent : displayed.Percent;
            _vgPreview.Invalidate();

            PorLinha(_vgPerfil, displayed.Name);
            PorLinha(_vgPainel, IdDoPainel());

            if (_cardPerif != null) _cardPerif.Atualizar(Perifericos.Atual);
            if (_vgPerif != null) _vgPerif.Atualizar(Perifericos.Atual);
            EventosDaVisao.ObservarPerifericos(Perifericos.Atual);
            _cardCooler.Conectado = status != null ? status.Connected : !string.IsNullOrEmpty(HidPanel.UltimoIdentificado);

            ApontarMostrador(_vgP1, id1, T.OnPanel(T.Top));
            ApontarMostrador(_vgP2, id2, T.OnPanel(T.Bottom));
            if (snap != null)
            {
                float pv;
                if (_vgP1 != null && _vgP1.Visible)
                    _vgP1.Push(snap.TryGetValue(_vgP1.SensorId, out pv) ? (float?)pv : null);
                if (_vgP2 != null && _vgP2.Visible)
                    _vgP2.Push(snap.TryGetValue(_vgP2.SensorId, out pv) ? (float?)pv : null);
            }

            if (snap == null) return;
            for (int i = 0; i < _vgTiles.Count; i++)
            {
                float v;
                _vgTiles[i].Push(snap.TryGetValue(_vgIds[i], out v) ? (float?)v : null);
            }
        }

        private static void PorLinha(Label val, string texto)
        {
            if (val == null) return;
            bool tem = !string.IsNullOrEmpty(texto);
            val.Text = texto ?? "";
            val.Visible = tem;
            Label cap = val.Tag as Label;
            if (cap != null) cap.Visible = tem;
        }

        private static string Junta(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + "  ·  " + b;
        }

        /// <summary>"Temperatura do processador  ·  56 °C", ou so o nome sem leitura.</summary>
        private string LeituraDoMostrador(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            SensorEntry achado = null;
            if (_sensors != null)
                foreach (SensorEntry s in _sensors)
                    if (s != null && s.Id == id) { achado = s; break; }

            if (achado == null) return NomeDoSensor(id);

            string nome = MetricPicker.Rotulo(achado);
            if (!achado.Value.HasValue || float.IsNaN(achado.Value.Value)) return nome;

            string v = Math.Abs(achado.Value.Value) >= 100
                ? achado.Value.Value.ToString("0")
                : achado.Value.Value.ToString("0.0");
            return nome + "  ·  " + v + (string.IsNullOrEmpty(achado.Unit) ? "" : " " + achado.Unit);
        }

        // ---------------- pagina: Baterias ----------------

        private Pagina _pgBaterias;
        private PaginaCasa _pgCasa;
        private Label _lbBateriasVazio;
        private FlatBtn _btGHub;
        private bool? _temGHub;
        private readonly List<CartaoDeBateria> _cartoesDeBateria = new List<CartaoDeBateria>();
        private DateTime _bateriasLidas = DateTime.MinValue;
    }
}
