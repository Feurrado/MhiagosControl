using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    /// <summary>
    /// Janela principal de configuracao.
    ///
    /// Organizada em secoes na barra lateral em vez de uma tela unica: com
    /// sensores, unidades, escala e perfis, tudo junto ficava denso
    /// demais para encontrar qualquer coisa.
    /// </summary>
    public class SettingsForm : Form
    {
        private const int WS_EX_COMPOSITED = 0x02000000;

        /// <summary>
        /// Compoe a janela inteira fora da tela antes de mostra-la.
        ///
        /// Cada controle daqui ja tem buffer duplo proprio, e isso resolve o
        /// tremor DE UM controle se repintando. Nao resolve o de varios se
        /// movendo juntos: sem composicao, cada um limpa o seu retangulo e
        /// desenha no lugar novo por conta propria, e o intervalo entre um e
        /// outro fica visivel. Enquanto a barra lateral desliza sao tres
        /// cartoes, a previa e os filhos ancorados de cada um, todos mudando de
        /// posicao no mesmo quadro - e o que se via nao era falta de quadros,
        /// era a janela sendo remontada em pedacos na frente de quem olha.
        ///
        /// Aqui a montagem acontece num buffer so e a tela recebe o resultado
        /// pronto.
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_COMPOSITED;
                return cp;
            }
        }

        private readonly Config _cfg;
        private readonly SettingsSession _session;
        private readonly ISettingsData _data;
        private List<SensorEntry> _sensors;

        private NavBar _nav;
        private Panel _host;
        private Profile _current;
        private bool _loading = false;
        private Timer _tick;
        private DateTime _lastSlowUiTickUtc = DateTime.MinValue;
        private Label _footerNote;
        private Timer _noteTimer;
        private FlatBtn _btSave, _btClose;

        // pagina Paineis
        private Control _pgPaineis;
        private SensorPicker _pick1, _pick2;
        private Segmented _unit1, _unit2;
        private FlatBtn[] _div1, _div2;
        private PanelPreview _preview;
        private Card _panelPreviewCard;
        private SensorSlot _slot1, _slot2;

        // pagina Perfis
        private Control _pgPerfis;
        private ProfileList _profileList;
        private PanelPreview _profilePreview;
        private FlatBtn _btnApply, _btnDefault, _profileSensor1, _profileSensor2;
        private Card _cardGame, _cardRotation;
        private Label _profilesNote;

        // pagina Sobre
        private Control _pgConfig;
        private Toggle _tgAutostart, _tgIdle, _tgRotate;
        private NumberBox _idleMin, _rotSecs;
        private Label _idleNote, _rotNote;
        private Segmented _lang;
        private DateTime _lastRtssCheckUtc = DateTime.MinValue;

        /// <summary>
        /// Ha edicao ainda nao gravada. Governa o aviso ao fechar E o rodape.
        ///
        /// Propriedade, e nao campo, porque o valor e atribuido em uma duzia de
        /// lugares - renomear perfil, importar, trocar sensor ou unidade. Com
        /// campo, cada um deles teria de lembrar de avisar o rodape,
        /// e o que fosse esquecido deixaria o botao Salvar mentindo sobre haver
        /// ou nao o que gravar. Aqui nao ha o que esquecer.
        /// </summary>
        private bool _dirty
        {
            get { return _temEdicao; }
            set
            {
                if (_temEdicao == value) return;
                _temEdicao = value;
                AtualizarRodape();
            }
        }
        private bool _temEdicao = false;

        /// <summary>
        /// Alguma gravacao ja aconteceu nesta sessao da janela.
        ///
        /// Com Salvar deixando a janela aberta, o resultado do dialogo nao pode
        /// mais ser lido como "clicou em Salvar": quem chama usa DialogResult
        /// para decidir se recarrega a configuracao do disco, e recarregar
        /// depois de uma gravacao valida jogaria fora o que acabou de ser salvo.
        /// </summary>
        private bool _saved = false;

        /// <summary>
        /// Gravou no disco e o perfil ativo pode ter mudado.
        ///
        /// Quem abriu a janela usa isto para republicar o perfil que a thread
        /// do mostrador le. Sem o aviso, aplicar um perfil so teria efeito ao
        /// fechar a janela - e o botao pareceria nao fazer nada.
        /// </summary>
        public event EventHandler Applied;

        private static readonly int[] DivValues = new int[] { 0, 1, 10, 100, 1000 };
        private static readonly string[] DivLabels = new string[] { "Auto", "÷1", "÷10", "÷100", "÷1000" };

        public SettingsForm(Config cfg, List<SensorEntry> sensors, ISettingsData data)
        {
            _session = new SettingsSession(cfg);
            _cfg = _session.Draft;
            _sensors = sensors;
            _data = data;
            // Toda a janela edita o rascunho da sessao. Misturar aqui o perfil
            // da configuracao viva fazia a primeira troca de aba escrever parte
            // do formulario no objeto errado antes de Salvar.
            _current = _cfg.Active;

            Text = T.AppName;
            Icon = Assets.AppIcon;
            // Redimensionavel.
            //
            // Era FixedSingle desde quando cada pagina vivia em coordenadas
            // absolutas: deixar arrastar a borda teria mostrado cartoes parados
            // no canto de uma janela maior. Agora que as paginas repartem a
            // largura - e a de bordo reparte tambem a altura - a janela fixa e
            // que virou a limitacao, num aplicativo cuja tela principal e um
            // painel de leituras que so melhora com espaco.
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;

            // O minimo e onde o projeto ainda fecha: 756 px de conteudo, a barra
            // lateral recolhida e as margens do hospedeiro. Abaixo disso os
            // cartoes parariam de encolher e passariam a ser cortados.
            MinimumSize = new Size(880, 660);

            Size guardado = TamanhoGuardado();
            ClientSize = guardado != Size.Empty ? guardado : new Size(1010, 900);
            BackColor = Ui.Window;
            Font = Ui.FontBase;

            BuildChrome();
            BuildPages();
            LoadFromProfile();

            // Depois de BuildPages: a largura leva em conta o texto dos itens de
            // navegacao, que so existem a partir dali.
            _nav.AjustarLargura();

            Theme.Apply(this);

            _tick = new Timer();
            // A interface acompanha a mesma cadência do LCD. Assim não redesenha
            // quatro vezes um valor que ainda não teve oportunidade de mudar.
            _tick.Interval = 250;
            _tick.Tick += new EventHandler(OnTick);
            _tick.Start();
        }

        // ---------------- estrutura ----------------

        private void BuildChrome()
        {
            _nav = new NavBar();
            _nav.Dock = DockStyle.Left;
            _nav.Width = 210;
            _nav.Logo = IconImage();
            _nav.SubtitleCaption = T.ActiveProfile;
            _nav.Subtitle = _current.Name;
            _nav.Collapsed = _cfg.SidebarCollapsed;
            _nav.CollapsedChanged += delegate
            {
                // Grava na hora, e nao no Salvar: recolher a barra e escolha de
                // espaco de tela, nao edicao de perfil, e nao deve ficar refem
                // de uma gravacao que a pessoa talvez nem faca.
                try
                {
                    _cfg.SidebarCollapsed = _nav.Collapsed;
                    string erro;
                    if (!_session.TrySavePreferences(delegate(Config draft, Config target)
                    {
                        target.SidebarCollapsed = draft.SidebarCollapsed;
                    }, out erro)) Log.Write("barra lateral nao persistiu: " + erro);
                }
                catch (Exception ex) { Log.Error("gravar estado da barra lateral", ex); }
            };
            _nav.SelectionChanged += delegate { ShowPage(); };
            Controls.Add(_nav);

            // Salvar e Fechar moram na BARRA LATERAL.
            //
            // Ja passaram pelo rodape e por uma barra no alto. O problema dos
            // dois lugares era o mesmo: uma faixa horizontal atravessando a
            // janela inteira, gasta com dois botoes, num aplicativo cuja tela
            // principal e um painel de leituras que so melhora com espaco. A
            // lateral ja existe, ja e a coluna de comandos - e tinha um vao
            // vazio entre a navegacao e o resumo do sistema exatamente do
            // tamanho deles.
            //
            // Recolhida, os dois viram icone. O glifo so entra quando o texto
            // nao cabe, entao expandida a barra continua mostrando as palavras.
            _btSave = new FlatBtn();
            _btSave.Text = T.Save;
            _btSave.Glyph = "\uE74E";          // disquete, Segoe MDL2
            _btSave.Primary = true;
            _btSave.Enabled = false;
            _btSave.Click += new EventHandler(OnSave);
            _nav.Controls.Add(_btSave);

            _btClose = new FlatBtn();
            _btClose.Text = T.Close;
            _btClose.Glyph = "\uE711";         // o mesmo "x" ja usado nos cartoes
            _btClose.Click += delegate { Close(); };
            _nav.Controls.Add(_btClose);

            _footerNote = MakeLabel("", 0, 0, Ui.FontSmall);
            _footerNote.ForeColor = Ui.Accent;
            _footerNote.Visible = false;
            _nav.Controls.Add(_footerNote);

            _nav.Resize += delegate { ArranjarAcoes(); };
            ArranjarAcoes();
            AtualizarRodape();

            _host = new Panel();
            _host.Dock = DockStyle.Fill;
            _host.BackColor = Ui.Window;
            // Afasta o primeiro cartao da barra de titulo. Quatro pixels faziam
            // o conteudo parecer colado ao topo, sobretudo nas paginas sem
            // titulo proprio, como Visao geral e Metricas.
            _host.Padding = new Padding(18, 12, 18, 14);
            Controls.Add(_host);

            // O Windows Forms encaixa os controles do fundo da ordem Z para a
            // frente: quem esta atras reivindica a borda inteira primeiro. Com
            // a lateral adicionada ANTES do rodape, era o rodape que atravessava
            // a janela toda, e a lateral parava logo acima dele - a faixa clara
            // no canto inferior esquerdo.
            //
            // Mandar a lateral para o fundo inverte a ordem: ela toma a coluna
            // esquerda de cima a baixo, e o rodape se encaixa no que sobra. O
            // Fill vem na frente de todos, senao nao ganha o espaco restante.
            _host.BringToFront();
            _nav.SendToBack();
        }

        private Image IconImage()
        {
            try { if (Assets.AppIcon != null) return Assets.AppIcon.ToBitmap(); }
            catch (Exception ex) { Log.Error("logo da barra lateral", ex); }
            return null;
        }

        private void BuildPages()
        {
            // Glifo ESCAPADO, como o da engrenagem logo abaixo: E80F e a "casa"
            // da Segoe MDL2, um caractere da area de uso privado, e colado no
            // fonte ele depende de sobreviver a toda ferramenta que passar pelo
            // arquivo. Ja nao sobreviveu antes.
            NavItem visao = new NavItem();
            visao.Text = T.NavOverview; visao.Glyph = "\uE80F"; visao.Page = BuildPageVisaoGeral();
            NavItem paineis = new NavItem();
            paineis.Text = T.NavPanels; paineis.Glyph = ""; paineis.Page = BuildPagePaineis();
            _pgPaineis = paineis.Page;

            NavItem perfis = new NavItem();
            perfis.Text = T.NavProfiles; perfis.Glyph = ""; perfis.Page = BuildPagePerfis();
            _pgPerfis = perfis.Page;

            // Glifo escapado, e nao o caractere solto como os de cima: E713 e a
            // engrenagem da Segoe MDL2, e um caractere da area de uso privado
            // colado no fonte depende de sobreviver a toda ferramenta que passar
            // pelo arquivo.
            NavItem config = new NavItem();
            config.Text = T.NavSettings; config.Glyph = ""; config.Page = BuildPageConfig();
            _pgConfig = config.Page;

            NavItem metricas = new NavItem();
            metricas.Text = T.NavMetrics;
            metricas.Glyph = "\uE9D9";            // grafico de area, Segoe MDL2
            metricas.Page = BuildPageMetricas();

            // E7C1 e a "etiqueta" da Segoe MDL2. ESCAPADO, como todos.
            NavItem specs = new NavItem();
            specs.Text = T.NavSpecs; specs.Glyph = "\uE7C1"; specs.Page = BuildPageSpecs();
            NavItem sobre = new NavItem();
            sobre.Text = T.NavAbout; sobre.Glyph = ""; sobre.Page = BuildPageSobre();

            foreach (NavItem it in new NavItem[] { visao, paineis, metricas, specs, perfis, config, sobre })
            {
                _nav.Add(it);
                it.Page.Dock = DockStyle.Fill;
                it.Page.Visible = false;
                _host.Controls.Add(it.Page);

                // Copia local: o delegate guarda a variavel, e nao o valor dela
                // no instante em que foi escrito.
                Control pg = it.Page;
                pg.Resize += delegate { ArranjarPagina(pg); };
            }
            ShowPage();
        }

        private void ShowPage()
        {
            NavItem sel = _nav.Selected;
            foreach (Control c in _host.Controls) c.Visible = false;
            if (sel != null && sel.Page != null)
            {
                sel.Page.Visible = true;
                sel.Page.BringToFront();

                // Uma pagina que nunca apareceu nao tem HWND, e SetWindowTheme
                // sem handle nao faz efeito: a varredura feita na exibicao da
                // janela pulava justamente as paginas de tras, e a barra de
                // rolagem delas saia branca na primeira visita.
                Theme.ApplyScrollbars(sel.Page);

                // Pelo mesmo motivo: sem HWND nao houve Resize, e a pagina
                // estreava com a largura de projeto e a faixa morta do lado.
                //
                // E tambem porque os dois arranjos desistem de pagina escondida:
                // o que foi pulado enquanto ela estava atras e cobrado aqui,
                // agora que ela e a que aparece.
                ArranjarPagina(sel.Page);

                // A coleta so comeca quando a aba estreia: quem nunca abrir as
                // especificacoes nao paga os segundos de WMI.
                if (sel.Page == _pgSpecs) GarantirSpecs();

                // Paginas ocultas nao recebem trabalho de desenho a cada segundo;
                // quem acaba de aparecer recebe um retrato novo imediatamente.
                AtualizarPaginaAtiva(true);
            }
            if (_profileList != null) RefreshProfileList();
        }

        /// <summary>
        /// Salvar, Fechar e o aviso, encaixados no vao da barra lateral.
        ///
        /// Ancorados na BASE, logo acima do resumo do sistema: a navegacao cresce
        /// para baixo conforme as abas, e presos ao topo eles seriam empurrados
        /// no dia em que uma pagina nova entrasse. Embaixo, ficam onde estao.
        /// </summary>
        private void ArranjarAcoes()
        {
            if (_nav == null || _btSave == null || _btClose == null) return;

            const int Alt = 32;
            const int Esp = 8;

            bool estreita = _nav.Width < 120;
            int margem = estreita ? 8 : 18;
            int larg = _nav.Width - margem * 2;

            // Uma nota de duas linhas exige largura; recolhida, a barra nao tem.
            bool cabeNota = !estreita;
            int alturaNota = cabeNota ? 32 : 0;

            int baixo = _nav.Height - 18;
            int y = baixo - alturaNota - Alt - Esp - Alt;

            _btSave.SetBounds(margem, y, larg, Alt);
            _btClose.SetBounds(margem, y + Alt + Esp, larg, Alt);

            if (_footerNote != null)
            {
                _footerNote.Visible = _footerNote.Visible && cabeNota;
                _footerNote.SetBounds(margem, y + (Alt + Esp) * 2 + 4, larg, alturaNota);
                _footerNote.TextAlign = ContentAlignment.TopLeft;
            }
        }

        /// <summary>
        /// O tamanho da ultima vez, se couber nesta tela.
        ///
        /// Guardar tamanho tem uma armadilha: quem gravou numa tela de 2560 e
        /// depois abre num notebook de 1366 receberia uma janela maior que o
        /// monitor, com o rodape e os botoes fora do alcance. Por isso a
        /// conferencia contra a area de trabalho atual, e nao so contra o minimo.
        /// </summary>
        private Size TamanhoGuardado()
        {
            int w = _cfg.WindowW, h = _cfg.WindowH;
            if (w <= 0 || h <= 0) return Size.Empty;

            Rectangle tela = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (w > tela.Width - 40) w = tela.Width - 40;
            if (h > tela.Height - 40) h = tela.Height - 40;
            if (w < MinimumSize.Width || h < MinimumSize.Height) return Size.Empty;

            return new Size(w, h);
        }

        /// <summary>
        /// Grava o tamanho ao terminar o arraste, e nao a cada pixel.
        ///
        /// O Resize dispara dezenas de vezes durante um arraste de borda, e cada
        /// gravacao e um arquivo escrito em disco.
        /// </summary>
        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            GuardarTamanho();
        }

        private void GuardarTamanho()
        {
            if (WindowState != FormWindowState.Normal) return;
            if (_cfg.WindowW == ClientSize.Width && _cfg.WindowH == ClientSize.Height) return;
            try
            {
                _cfg.WindowW = ClientSize.Width;
                _cfg.WindowH = ClientSize.Height;
                string erro;
                if (!_session.TrySavePreferences(delegate(Config draft, Config target)
                {
                    target.WindowW = draft.WindowW;
                    target.WindowH = draft.WindowH;
                }, out erro)) Log.Write("tamanho da janela nao persistiu: " + erro);
            }
            catch (Exception ex) { Log.Error("gravar tamanho da janela", ex); }
        }

        /// <summary>
        /// A roda do mouse rola a pagina, esteja o ponteiro sobre o que estiver.
        ///
        /// Filtro de mensagens porque o WM_MOUSEWHEEL vai para o controle sob o
        /// ponteiro, e sobre um cartao - que nao rola nada - ele morria ali. Era
        /// o comportamento que o AutoScroll dava de graca e que se perdeu junto
        /// com ele. Aqui a mensagem e interceptada antes de ser entregue, e a
        /// pagina visivel rola independentemente de quem estava embaixo.
        /// </summary>
        private class RodaDoMouse : IMessageFilter
        {
            private const int WM_MOUSEWHEEL = 0x020A;
            private readonly SettingsForm _dono;

            public RodaDoMouse(SettingsForm dono) { _dono = dono; }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_MOUSEWHEEL) return false;
                if (_dono == null || _dono.IsDisposed || !_dono.ContainsFocus) return false;

                NavItem sel = _dono._nav != null ? _dono._nav.Selected : null;
                Pagina p = sel != null ? sel.Page as Pagina : null;
                if (p == null || !p.Rolavel || !p.Visible) return false;

                // Listas internas possuem alcance proprio. Roubar a roda delas
                // para mover a pagina deixaria perfis e vinculos alem da segunda
                // linha inacessiveis justamente quando ha muitos itens.
                Control alvo = Control.FromHandle(m.HWnd);
                for (Control c = alvo; c != null && c != p; c = c.Parent)
                    if (c is ProfileList || c is GameBindingList) return false;

                // So quando o ponteiro esta sobre a pagina: fora dela, a roda
                // pertence a quem estiver embaixo - uma lista, um seletor.
                if (!p.RectangleToScreen(p.ClientRectangle).Contains(Cursor.Position)) return false;

                int delta = (short)((long)m.WParam >> 16);
                p.RolarPor(delta > 0 ? -60 : 60);
                return true;
            }
        }

        private RodaDoMouse _roda;

        /// <summary>Rearranja a pagina que esta a mostra, seja ela qual for.</summary>
        private void ArranjarPaginaVisivel()
        {
            NavItem sel = _nav != null ? _nav.Selected : null;
            if (sel == null || sel.Page == null) return;
            ArranjarPagina(sel.Page);
        }

        /// <summary>
        /// Cada pagina pelo seu arranjo.
        ///
        /// Metricas e Visao geral tem o seu proprio - a primeira porque empacota
        /// cartoes em fluxo, a segunda porque reparte a ALTURA, coisa que o
        /// Elastico nao faz. Deixa-las passar pelo Elastico junto seria duas
        /// rotinas disputando os mesmos controles a cada Resize.
        /// </summary>
        private void ArranjarPagina(Control pagina)
        {
            if (pagina == null) return;
            // Cada arranjo termina chamando a Sincronizar da sua pagina, e nao
            // este metodo depois deles.
            //
            // Parece rodeio e nao e: a Sincronizar NAO e idempotente - ela parte
            // do principio de que os filhos acabaram de ser posicionados sem
            // rolagem nenhuma, e chamada duas vezes seguidas aplicaria o
            // deslocamento em dobro. Deixar a responsabilidade com quem arranja
            // garante uma chamada por arranjo, inclusive nos arranjos que
            // acontecem sem passar por aqui - foi assim que a barra da folha de
            // especificacoes nunca chegou a existir: o MostrarSpecs arranjava
            // direto.
            if (pagina == _pgMetricas) ArranjarMetricas();
            else if (pagina == _pgVisao) { if (pagina.Visible) ArranjarVisaoGeral(); }
            else if (pagina == _pgSpecs) { if (pagina.Visible) ArranjarSpecsPagina(); }
            else if (pagina == _pgPaineis) ArranjarPaineis();
            else Elastico(pagina);
        }


        // ---------------- largura das paginas ----------------

        /// <summary>Largura util maxima de uma pagina.</summary>
        ///
        /// Passando disto o conteudo para de crescer e passa a ser centralizado.
        /// Uma pagina de 1900 px nao fica melhor por espalhar seis campos de
        /// formulario por toda a mesa: a linha de texto vira uma travessia e o
        /// olho perde o comeco da seguinte. Centralizado, o que sobra virou
        /// margem dos dois lados - que se le como decisao, e nao como o vao
        /// torto de um lado so.
        private const int LarguraMaxDaPagina = 1100;

        /// <summary>Medidas de projeto dos cartoes, antes de qualquer esticada.</summary>
        ///
        /// Recalcular a partir do que esta na tela acumularia erro: a segunda
        /// esticada partiria da largura ja esticada, e a pagina cresceria a cada
        /// arrasto da borda.
        private readonly Dictionary<Control, Rectangle> _projeto =
            new Dictionary<Control, Rectangle>();

        private Rectangle Projeto(Control c)
        {
            Rectangle r;
            if (!_projeto.TryGetValue(c, out r)) { r = c.Bounds; _projeto[c] = r; }
            return r;
        }

        /// <summary>
        /// Estica os cartoes de uma pagina para ocupar a largura que sobrou.
        ///
        /// As paginas nasceram em coordenadas absolutas medidas para 756 px, que
        /// era a largura util com a barra lateral aberta. Recolher a barra - ou
        /// so alargar a janela - passou a deixar uma faixa morta a direita,
        /// exatamente do tamanho do que a lateral devolveu.
        ///
        /// So cresce, nunca encolhe. Os controles DENTRO de cada cartao seguem
        /// em coordenadas fixas, e uma pagina mais estreita que o projeto
        /// cortaria o que esta encostado na borda direita deles.
        ///
        /// Os vaos entre cartoes ficam constantes e a sobra e repartida na
        /// proporcao das larguras. Dividir a sobra em partes iguais engordaria o
        /// cartao estreito no mesmo tanto que o largo, e a pagina de Perfis
        /// existe justamente com a lista menor que a previa - em duas ou tres
        /// esticadas as duas teriam quase o mesmo tamanho e a hierarquia entre
        /// elas, que e o que diz onde olhar primeiro, se perderia.
        /// </summary>
        private void Elastico(Control pagina)
        {
            Elastico(pagina, LarguraMaxDaPagina, null);
        }

        private void ArranjarPaineis()
        {
            Elastico(_pgPaineis, LarguraMaxDaPagina, delegate
            {
                if (_panelPreviewCard == null) return;

                // O cartao de previa ocupa o restante da pagina. Com a altura
                // fixa de projeto ele ultrapassava a area por poucos pixels e
                // exibia uma barra de rolagem para praticamente nenhum conteudo.
                // Pagina.Sincronizar reserva mais 8 px depois do ultimo filho.
                int altura = _pgPaineis.ClientSize.Height - _panelPreviewCard.Top - 12;
                _panelPreviewCard.Height = Math.Max(400, altura);
            });
        }

        private void Elastico(Control pagina, int larguraMaxima, Action depoisDeArranjar)
        {
            if (pagina == null || pagina.Width <= 0) return;

            // Ja foi tentado adiar o arranjo para o fim do deslize da lateral -
            // um reposicionamento em vez de dez. Ficou pior: o conteudo parado
            // durante a animacao e um salto no fim incomoda mais que acompanhar
            // com tremor. A pagina segue a largura quadro a quadro.

            // Pagina escondida nao se arruma agora.
            //
            // As seis paginas sao filhas do mesmo hospedeiro, todas em
            // DockStyle.Fill: o Windows Forms redimensiona TODAS a cada mudanca
            // de largura, apareca ou nao. Enquanto a barra lateral desliza isso
            // era seis arranjos completos por quadro, com a cascata de layout de
            // cada cartao e de cada filho ancorado - e cinco deles para paginas
            // que ninguem esta vendo. Quem estreia entra pelo ShowPage, que
            // chama isto de novo.
            if (!pagina.Visible) return;

            // O Card e a assinatura de uma pagina em coordenadas de projeto. A de
            // Metricas nao tem nenhum - ela ja se arranja sozinha, e as duas
            // rotinas mexendo nos mesmos controles brigariam a cada Resize.
            bool temCartao = false;
            foreach (Control c in pagina.Controls)
                if (c is Card) { temCartao = true; break; }
            if (!temCartao) return;

            // Daqui em diante vale todo filho direto, e nao so os cartoes: o
            // rodape da pagina de Perfis e um rotulo solto, e deixa-lo parado
            // enquanto os cartoes se deslocam so trocaria de lugar o
            // desalinhamento. Ancorado ou encaixado, o proprio Windows Forms ja
            // cuida - mexer seria desfazer o que a ancora acabou de fazer.
            List<Control> alvos = new List<Control>();
            foreach (Control c in pagina.Controls)
            {
                if (c.Dock != DockStyle.None) continue;
                if ((c.Anchor & AnchorStyles.Right) != 0) continue;
                alvos.Add(c);
            }
            if (alvos.Count == 0) return;

            Rectangle[] projeto = new Rectangle[alvos.Count];
            for (int i = 0; i < alvos.Count; i++) projeto[i] = Projeto(alvos[i]);

            // A largura da barra propria sai da conta sempre que a pagina possa
            // rolar, apareca a barra ou nao.
            //
            // Reservar incondicionalmente evita um circulo: medir a area de
            // cliente faria o conteudo encolher quando a barra surge e alargar
            // quando ela some, e cada troca dispara um novo arranjo. Com a barra
            // nativa isso custava dezessete pixels de faixa morta; com a nossa,
            // que tem dez e some quando nao ha o que rolar, o preco cabe.
            int disp = pagina.Width - 4;
            Pagina pg = pagina as Pagina;
            if (pg != null && pg.Rolavel) disp -= Pagina.LarguraDaBarra;

            Rectangle[] destino = Esticar(projeto, disp, larguraMaxima);
            if (destino == null) return;

            pagina.SuspendLayout();
            try
            {
                for (int i = 0; i < alvos.Count; i++) alvos[i].Bounds = destino[i];
                if (depoisDeArranjar != null) depoisDeArranjar();
            }
            finally { pagina.ResumeLayout(); }

            if (pg != null && pg.Rolavel) pg.Sincronizar();
        }

        /// <summary>
        /// A conta da esticada, sem tocar em controle nenhum.
        ///
        /// Separada porque geometria e o tipo de coisa que erra por um pixel e
        /// so aparece na tela de quem tem a janela do tamanho errado. Aqui ela e
        /// entrada e saida, e a suite verifica os casos que ninguem repara
        /// olhando: a borda direita depois da divisao inteira, a pagina estreita
        /// que nao pode encolher, a fileira de larguras desiguais.
        ///
        /// Devolve os retangulos na mesma ordem em que entraram, ou nulo quando
        /// nao ha o que fazer.
        /// </summary>
        internal static Rectangle[] Esticar(Rectangle[] projeto, int disponivel, int maximo)
        {
            if (projeto == null || projeto.Length == 0) return null;

            // A largura de projeto sai do proprio conteudo: a borda direita mais
            // distante. Guardar 756 aqui e 736 ali como constante daria uma
            // pagina desalinhada no dia em que um cartao mudasse de tamanho e
            // ninguem se lembrasse deste metodo.
            int larguraBase = 0;
            foreach (Rectangle p in projeto)
                if (p.Right > larguraBase) larguraBase = p.Right;
            if (larguraBase <= 0) return null;

            int alvo = disponivel;
            if (alvo > maximo) alvo = maximo;
            if (alvo < larguraBase) alvo = larguraBase;

            int margem = (disponivel - alvo) / 2;
            if (margem < 0) margem = 0;

            int extra = alvo - larguraBase;

            // Fileiras pelo topo: mesma coordenada de topo, mesma fileira.
            Dictionary<int, List<int>> linhas = new Dictionary<int, List<int>>();
            for (int i = 0; i < projeto.Length; i++)
            {
                int topo = projeto[i].Top;
                if (!linhas.ContainsKey(topo)) linhas[topo] = new List<int>();
                linhas[topo].Add(i);
            }

            Rectangle[] destino = new Rectangle[projeto.Length];
            Array.Copy(projeto, destino, projeto.Length);

            foreach (KeyValuePair<int, List<int>> kv in linhas)
            {
                List<int> fila = kv.Value;
                Rectangle[] pr = projeto;
                fila.Sort(delegate(int a, int b) { return pr[a].Left.CompareTo(pr[b].Left); });

                int somaW = 0;
                foreach (int i in fila) somaW += projeto[i].Width;
                if (somaW <= 0) continue;

                int x = margem + projeto[fila[0]].Left;
                int dado = 0;
                for (int k = 0; k < fila.Count; k++)
                {
                    Rectangle p = projeto[fila[k]];

                    // O ultimo absorve o resto da divisao inteira, senao a borda
                    // direita ficaria a um ou dois pixels do lugar - e um cartao
                    // desalinhado dos de cima e visivel.
                    int cresce = (k == fila.Count - 1)
                        ? extra - dado
                        : (int)((long)extra * p.Width / somaW);
                    dado += cresce;

                    destino[fila[k]] = new Rectangle(x, p.Top, p.Width + cresce, p.Height);

                    if (k + 1 < fila.Count)
                        x += p.Width + cresce + (projeto[fila[k + 1]].Left - p.Right);
                }
            }
            return destino;
        }

        // ---------------- pagina: Visao geral ----------------

        private Pagina _pgVisao;
        private readonly List<MetricCard> _vgTiles = new List<MetricCard>();
        private readonly List<string> _vgIds = new List<string>();

        private Card _cardCooler, _cardSpecs;
        private PanelPreview _vgPreview;
        private Label _vgPerfil, _vgPainel;
        private Label[] _vgSpecCap, _vgSpecVal;
        private MetricCard _vgP1, _vgP2;

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

            _cardCooler = new Card();
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

            _cardSpecs = new Card();
            _cardSpecs.Title = null;    // tira sozinho: e uma tira de referencia
            _pgVisao.Controls.Add(_cardSpecs);

            MontarSpecs();

            AtualizarVisaoGeral(null);
            return _pgVisao;
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
            c.Janela = MetricHistory.JanelaValida(_cfg.MetricRange);
            _pgVisao.Controls.Add(c);
            return c;
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
                c.Titulo = titulo;
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
            c.Titulo = titulo + "  ·  " + MetricPicker.RotuloCurto(s);
            c.Unidade = s.Unit;
            c.Sub = SystemInfo.Limpar(s.Hardware);

            float? at, pe;
            MetricPicker.Faixas(s.Unit, out at, out pe);
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
                _cardSpecs.Controls.Add(cap);
                caps.Add(cap);

                Label val = MakeLabel(l[1], 0, 0, Ui.FontSemi);
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
                c.Sub = SystemInfo.Limpar(s.Hardware);
                c.Unidade = s.Unit;
                c.SensorId = s.Id;
                c.Janela = MetricHistory.JanelaValida(_cfg.MetricRange);
                float? at, pe;
                MetricPicker.Faixas(s.Unit, out at, out pe);
                c.Atencao = at; c.Perigo = pe;

                _pgVisao.Controls.Add(c);
                _vgTiles.Add(c);
                _vgIds.Add(s.Id);
                indice++;
            }

            // O que foi POSTO na tela e o que precisa de historico. A lista do
            // arranque foi montada com os sensores frios e pode ter escolhido
            // outro sensor para o mesmo lugar.
            MetricHistory.SeguirTambem(_vgIds);
        }

        /// <summary>
        /// O arranjo desta pagina, largura E altura.
        ///
        /// A altura entra na conta porque foi a falta dela que deixou a primeira
        /// versao "vazia": tres cartoes de altura fixa acabavam no meio da tela e
        /// o resto era fundo. Aqui as duas pontas sao fixas - a fileira de blocos
        /// em cima, a tira de especificacoes embaixo - e o cartao do cooler fica
        /// com tudo que sobrar. Assim nao existe sobra, em nenhum tamanho de
        /// janela.
        /// </summary>
        private void ArranjarVisaoGeral()
        {
            if (_pgVisao == null || _cardCooler == null) return;
            if (_pgVisao.Width <= 0 || _pgVisao.Height <= 0) return;

            const int Esp = 10;
            const int AlturaBloco = 148;
            const int AlturaSpecs = 56;
            const int MeioMin = 240;

            int disp = _pgVisao.Width - 4;
            int alvo = disp > LarguraMaxDaPagina ? LarguraMaxDaPagina : disp;
            if (alvo < 560) alvo = 560;
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
                int hMeio = ySpecs - Esp - yMeio;
                if (hMeio < MeioMin) { hMeio = MeioMin; ySpecs = yMeio + hMeio + Esp; }

                // Duas colunas na faixa do meio. A esquerda e mais estreita: o
                // que ela guarda e uma foto quadrada e quatro linhas curtas, e
                // dar-lhe metade da tela era o que espalhava tudo - a previa
                // ficava perdida no meio de um cartao vazio, com o texto a meio
                // metro de distancia.
                int wEsq = alvo * 42 / 100;
                if (wEsq < 300) wEsq = 300;
                if (wEsq > alvo - 260) wEsq = alvo - 260;
                int wDir = alvo - wEsq - Esp;

                _cardCooler.SetBounds(x0, yMeio, wEsq, hMeio);

                int hCurva = (hMeio - Esp) / 2;
                if (_vgP1 != null) _vgP1.SetBounds(x0 + wEsq + Esp, yMeio, wDir, hCurva);
                if (_vgP2 != null)
                    _vgP2.SetBounds(x0 + wEsq + Esp, yMeio + hCurva + Esp, wDir, hMeio - hCurva - Esp);

                _cardSpecs.SetBounds(x0, ySpecs, alvo, AlturaSpecs);

                ArranjarCooler();
                ArranjarSpecs();
            }
            finally { _pgVisao.ResumeLayout(); }
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

            const int AlturaLinha = 26;
            int hTexto = vis * AlturaLinha;

            // A previa fica com o que sobra, sempre quadrada: ela encaixa a foto
            // pela altura, entao largura a mais so viraria fundo preto ao lado.
            int lado = Math.Min(w - 32, h - 52 - hTexto - 12);
            if (lado < 80) lado = 80;

            // O BLOCO INTEIRO centrado na altura util, e nao encostado no topo.
            //
            // Numa coluna estreita a previa e limitada pela LARGURA, nao pela
            // altura: sobrava um palmo de fundo embaixo das duas linhas de
            // texto, e a peca - que e o assunto do cartao - ficava empurrada
            // para cima como se o resto tivesse sido cortado.
            int bloco = lado + 14 + hTexto;
            int topo = 46 + (h - 46 - bloco) / 2;
            if (topo < 46) topo = 46;

            _vgPreview.SetBounds((w - lado) / 2, topo, lado, lado);

            int y = topo + lado + 14;
            foreach (Label val in linhas)
            {
                if (val == null || !val.Visible) continue;
                Label cap = val.Tag as Label;
                // Rotulo curto a esquerda, valor com TODO o resto: com 150 px
                // reservados a um rotulo de uma palavra, o que sobrava para o
                // valor nao dava nem para o nome do perfil.
                if (cap != null) cap.SetBounds(16, y + 3, 92, 17);
                val.SetBounds(112, y, w - 128, 20);
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

            const int Vao = 26;
            int n = _vgSpecVal.Length;

            int[] larg = new int[n];
            int soma = 0;
            for (int i = 0; i < n; i++)
            {
                int a = TextRenderer.MeasureText(_vgSpecCap[i].Text, Ui.FontSmall).Width;
                int b = TextRenderer.MeasureText(_vgSpecVal[i].Text, Ui.FontSemi).Width;
                larg[i] = Math.Max(a, b) + 4;
                soma += larg[i];
            }

            // Se nao couber, todas encolhem na proporcao: cortar so a ultima
            // deixaria justamente o sistema operacional sem nome.
            int disp = _cardSpecs.Width - 32 - (n - 1) * Vao;
            if (soma > disp && soma > 0)
                for (int i = 0; i < n; i++) larg[i] = larg[i] * disp / soma;

            // Centralizada, e nao encostada a esquerda.
            //
            // Com as colunas coladas ao conteudo, a sobra ia toda para o fim - e
            // quatro dados curtos alinhados a esquerda, com um vao de trezentos
            // pixels do lado direito, se leem como uma tira que ficou pela
            // metade. Centralizada, o que sobra vira margem dos dois lados, que e
            // a mesma decisao que a pagina inteira ja toma acima de 1100 px.
            int total = (n - 1) * Vao;
            for (int i = 0; i < n; i++) total += larg[i];

            int x = (_cardSpecs.Width - total) / 2;
            if (x < 16) x = 16;

            for (int i = 0; i < n; i++)
            {
                _vgSpecCap[i].SetBounds(x, 11, larg[i], 15);
                _vgSpecVal[i].SetBounds(x, 28, larg[i], 18);
                x += larg[i] + Vao;
            }
        }

        /// <summary>Estado do cooler e das duas leituras que ele mostra.</summary>
        private void AtualizarVisaoGeral(Dictionary<string, float> snap)
        {
            if (_cardCooler == null) return;

            string id1 = _current != null ? _current.Panel1Id : null;
            string id2 = _current != null ? _current.Panel2Id : null;

            PorLinha(_vgPerfil, _current != null ? _current.Name : null);
            PorLinha(_vgPainel, IdDoPainel());

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

        // ---------------- pagina: Especificacoes ----------------

        private Pagina _pgSpecs;
        private Label _lbSpecsEstado, _lbSpecsNota;
        private FlatBtn _btCopiarSpecs;
        private bool _coletando = false;
        private readonly List<FlatBtn> _specCategoryButtons = new List<FlatBtn>();
        private readonly Dictionary<Card, int> _specFullHeights = new Dictionary<Card, int>();
        private int _specCategory = 0; // zero = resumo; os demais seguem _specCards

        /// <summary>
        /// A folha de especificacoes, no espirito do CPU-Z.
        ///
        /// Nasce vazia, com um aviso de que esta consultando: a coleta usa WMI e
        /// custa segundos - o Win32_Processor sozinho levou 1427 ms medidos - e
        /// montar isso no construtor faria a janela inteira demorar para abrir
        /// por causa de uma aba que a pessoa talvez nem visite.
        /// </summary>
        private Control BuildPageSpecs()
        {
            _pgSpecs = new Pagina();
            _pgSpecs.RolarNaVertical();

            // Cabecalho de UMA fileira: botao a esquerda, aviso ao lado.
            //
            // Antes eram tres linhas empilhadas - estado, botao, nota - e o
            // estado ficava invisivel depois da coleta, deixando a altura dele
            // como buraco entre o botao e o primeiro cartao. O estado agora
            // divide a linha com o botao e some sem levar altura junto.
            _btCopiarSpecs = new FlatBtn();
            _btCopiarSpecs.Text = T.SpecsCopy;
            _btCopiarSpecs.SetBounds(2, 2, 150, 30);
            _btCopiarSpecs.Enabled = false;
            _btCopiarSpecs.Click += delegate { CopiarSpecs(); };
            _pgSpecs.Controls.Add(_btCopiarSpecs);

            _lbSpecsEstado = MakeLabel(T.SpecsLoading, 166, 4, Ui.FontBase);
            _lbSpecsEstado.Size = new Size(300, 26);
            _lbSpecsEstado.TextAlign = ContentAlignment.MiddleLeft;
            _lbSpecsEstado.ForeColor = Ui.Muted;
            _pgSpecs.Controls.Add(_lbSpecsEstado);

            _lbSpecsNota = MakeLabel(T.SpecsNote, 166, 4, Ui.FontSmall);
            _lbSpecsNota.Size = new Size(600, 28);
            _lbSpecsNota.TextAlign = ContentAlignment.MiddleLeft;
            _lbSpecsNota.ForeColor = Ui.Faint;
            _lbSpecsNota.Visible = false;
            _pgSpecs.Controls.Add(_lbSpecsNota);

            return _pgSpecs;
        }

        /// <summary>
        /// Dispara a coleta, uma vez, quando a aba estreia.
        ///
        /// Em thread propria e com retorno por BeginInvoke: WMI na thread da
        /// interface trava a bomba de mensagens, e alguns segundos travado sao
        /// suficientes para o Windows escurecer a janela e chama-la de "nao
        /// responde".
        /// </summary>
        private void GarantirSpecs()
        {
            if (_pgSpecs == null || _coletando) return;
            if (SpecSheet.Folha != null) { MostrarSpecs(); return; }

            _coletando = true;
            List<SensorEntry> copia = _sensors;

            // Qualificado: importar System.Threading deixaria "Timer" ambiguo
            // entre o de threading e o do Windows Forms, e esta classe usa o
            // segundo. O TrayApp resolve a mesma colisao do mesmo jeito.
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                try { SpecSheet.Coletar(copia); }
                catch (Exception ex) { Log.Error("coleta de especificacoes", ex); }

                try
                {
                    if (IsHandleCreated) BeginInvoke(new MethodInvoker(MostrarSpecs));
                }
                catch (Exception ex) { Log.Error("retorno da coleta", ex); }
            });
            t.IsBackground = true;
            t.Name = "SpecSheet";
            t.Start();
        }

        private void MostrarSpecs()
        {
            _coletando = false;

            List<SpecGrupo> folha = SpecSheet.Folha;
            if (_pgSpecs == null || folha == null) return;

            // Ja montada: a folha nao muda enquanto a maquina esta ligada.
            if (_specCards.Count > 0) return;

            _pgSpecs.SuspendLayout();
            try
            {
                _lbSpecsEstado.Visible = false;
                _lbSpecsNota.Visible = true;
                _btCopiarSpecs.Enabled = true;

                foreach (SpecGrupo g in folha)
                {
                    Card c = new Card();
                    c.Title = g.Titulo;
                    _pgSpecs.Controls.Add(c);
                    _specCards.Add(c);

                    List<Label> vals = new List<Label>();
                    int y = 46;
                    foreach (string[] l in g.Linhas)
                    {
                        Label cap = MakeLabel(l[0], 16, y + 2, Ui.FontSmall);
                        cap.ForeColor = Ui.Muted;
                        c.Controls.Add(cap);

                        Label val = MakeLabel(l[1], 0, y, Ui.FontSemi);
                        // Reticencias em vez de corte seco: "American Megatrends
                        // International, LLC." nao cabe numa coluna estreita, e
                        // sem elas o texto acabava no meio de uma letra, sem nada
                        // avisando que havia mais.
                        val.AutoEllipsis = true;
                        val.Tag = cap;
                        c.Controls.Add(val);
                        vals.Add(val);

                        y += 24;
                    }
                    c.Height = y + 10;
                    c.Tag = vals;
                    _specFullHeights[c] = c.Height;
                }

                MontarCategoriasDeSpecs(folha);
            }
            finally { _pgSpecs.ResumeLayout(); }

            ArranjarSpecsPagina();
        }

        private readonly List<Card> _specCards = new List<Card>();

        private void MontarCategoriasDeSpecs(List<SpecGrupo> folha)
        {
            if (_specCategoryButtons.Count > 0) return;
            string[] nomes = new string[folha.Count + 1];
            nomes[0] = T.SpecsOverview;
            for (int i = 0; i < folha.Count; i++) nomes[i + 1] = folha[i].Titulo;

            for (int i = 0; i < nomes.Length; i++)
            {
                FlatBtn b = new FlatBtn();
                b.Text = nomes[i];
                b.Tag = i;
                b.Primary = i == _specCategory;
                b.Height = 30;
                b.Click += delegate(object sender, EventArgs e)
                {
                    FlatBtn clicked = sender as FlatBtn;
                    if (clicked == null) return;
                    _specCategory = (int)clicked.Tag;
                    foreach (FlatBtn item in _specCategoryButtons)
                    {
                        item.Primary = ReferenceEquals(item, clicked);
                        item.Invalidate();
                    }
                    ArranjarSpecsPagina();
                };
                _pgSpecs.Controls.Add(b);
                _specCategoryButtons.Add(b);
            }
        }

        /// <summary>
        /// Duas colunas de cartoes, empacotadas na mais curta.
        ///
        /// Empilhar tudo numa coluna daria uma pagina de tres metros com metade
        /// da largura vazia. Duas colunas com o proximo cartao indo sempre para a
        /// mais BAIXA e o que mantem as duas parelhas quando os cartoes tem
        /// alturas muito diferentes - e tem: "Sistema" sao tres linhas e
        /// "Processador" sao treze.
        /// </summary>
        private void ArranjarSpecsPagina()
        {
            if (_pgSpecs == null || _specCards.Count == 0) return;

            const int Esp = 12;

            int disp = _pgSpecs.Width - 4 - Pagina.LarguraDaBarra;
            if (disp > LarguraMaxDaPagina) disp = LarguraMaxDaPagina;
            if (disp < 460) disp = 460;

            // Categorias em pastilhas que quebram de linha. Uma faixa dividida
            // em partes iguais esmagaria "Armazenamento" em janelas estreitas;
            // aqui cada uma ocupa o texto que realmente possui.
            int catX = 2, catY = _btCopiarSpecs.Bottom + 10;
            foreach (FlatBtn b in _specCategoryButtons)
            {
                int w = TextRenderer.MeasureText(b.Text, Ui.FontBase).Width + 30;
                w = Math.Max(82, Math.Min(164, w));
                if (catX > 2 && catX + w > disp) { catX = 2; catY += 36; }
                b.SetBounds(catX, catY, w, 30);
                catX += w + 6;
            }
            int topo = (_specCategoryButtons.Count > 0 ? catY + 30 : _btCopiarSpecs.Bottom) + 12;

            if (_specCategory > 0)
            {
                int escolhido = _specCategory - 1;
                for (int i = 0; i < _specCards.Count; i++)
                {
                    Card c = _specCards[i];
                    c.Visible = i == escolhido;
                    if (!c.Visible) continue;
                    MostrarLinhasDeSpec(c, int.MaxValue);
                    int detailWidth = Math.Min(820, disp);
                    int detailX = 2 + (disp - detailWidth) / 2;
                    c.SetBounds(detailX, topo, detailWidth, _specFullHeights[c]);
                    ArranjarLinhasDeSpec(c);
                }
                _pgSpecs.Sincronizar();
                return;
            }

            // Resumo: todos os grupos viram cartoes compactos com as primeiras
            // informacoes. A categoria abre a ficha inteira sem repetir dados.
            foreach (Card c in _specCards)
            {
                c.Visible = true;
                c.Height = Math.Min(128, _specFullHeights[c]);
                MostrarLinhasDeSpec(c, 3);
            }

            if (disp >= 620 && _specCards.Count >= 7)
            {
                // Ordem visual estável: duas fileiras de componentes, discos
                // usando a largura toda e, por fim, rede/sistema. O antigo
                // empacotamento pela coluna mais curta deixava Armazenamento
                // sozinho diante de dois cartões empilhados e um grande vazio.
                int half = (disp - Esp) / 2;
                int y = topo;
                ArranjarParDeSpecs(0, 1, y, half, Esp); y += 128 + Esp;
                ArranjarParDeSpecs(2, 3, y, half, Esp); y += 128 + Esp;

                Card storage = _specCards[4];
                storage.SetBounds(2, y, disp, 104);
                ArranjarLinhasDeSpecEmColunas(storage);
                y += storage.Height + Esp;

                ArranjarParDeSpecs(5, 6, y, half, Esp); y += 128 + Esp;

                // Grupos futuros não somem: entram abaixo em largura inteira.
                for (int i = 7; i < _specCards.Count; i++)
                {
                    Card extra = _specCards[i];
                    extra.SetBounds(2, y, disp, 128);
                    ArranjarLinhasDeSpec(extra);
                    y += extra.Height + Esp;
                }
            }
            else
            {
                int y = topo;
                foreach (Card c in _specCards)
                {
                    c.SetBounds(2, y, disp, 128);
                    ArranjarLinhasDeSpec(c);
                    y += c.Height + Esp;
                }
            }

            // O aviso ao lado do botao acompanha a largura: fixo em 600 px, ele
            // saia cortado no meio de "nome da maquina" numa janela estreita.
            if (_lbSpecsNota != null)
                _lbSpecsNota.Width = Math.Max(160, disp - _lbSpecsNota.Left);

            _pgSpecs.Sincronizar();
        }

        private void ArranjarParDeSpecs(int left, int right, int y, int width, int gap)
        {
            Card a = _specCards[left], b = _specCards[right];
            a.SetBounds(2, y, width, 128);
            b.SetBounds(2 + width + gap, y, width, 128);
            ArranjarLinhasDeSpec(a);
            ArranjarLinhasDeSpec(b);
        }

        /// <summary>Três unidades de armazenamento lado a lado no resumo.</summary>
        private static void ArranjarLinhasDeSpecEmColunas(Card card)
        {
            List<Label> vals = card.Tag as List<Label>;
            if (vals == null || vals.Count == 0) return;
            int count = Math.Min(3, vals.Count);
            int gap = 18;
            int width = (card.Width - 32 - (count - 1) * gap) / count;
            for (int i = 0; i < vals.Count; i++)
            {
                bool visible = i < count;
                Label val = vals[i];
                Label cap = val.Tag as Label;
                val.Visible = visible;
                if (cap != null) cap.Visible = visible;
                if (!visible) continue;
                int x = 16 + i * (width + gap);
                if (cap != null) cap.SetBounds(x, 46, width, 17);
                val.SetBounds(x, 66, width, 20);
            }
        }

        private static void MostrarLinhasDeSpec(Card card, int quantidade)
        {
            List<Label> vals = card.Tag as List<Label>;
            if (vals == null) return;
            for (int i = 0; i < vals.Count; i++)
            {
                bool visible = i < quantidade;
                vals[i].Visible = visible;
                Label cap = vals[i].Tag as Label;
                if (cap != null) cap.Visible = visible;
            }
        }

        /// <summary>
        /// As linhas de um cartao de especificacao, medidas pela largura dele.
        ///
        /// Explicito, e nao por ancora. A ancora calcula o deslocamento a partir
        /// do tamanho que o cartao tinha quando o filho entrou - e aqui os filhos
        /// entram antes de o cartao ser dimensionado, entao a conta partia de um
        /// tamanho que nunca foi o de verdade e o valor saia cortado.
        ///
        /// A coluna do rotulo e proporcional, com teto: num cartao estreito ela
        /// cede espaco para o valor, que e a parte que muda de tamanho. Fixa em
        /// 150 px, ela comia mais da metade de uma coluna de tres.
        /// </summary>
        private static void ArranjarLinhasDeSpec(Card c)
        {
            List<Label> vals = c.Tag as List<Label>;
            if (vals == null) return;

            int rotulo = c.Width * 30 / 100;
            if (rotulo < 88) rotulo = 88;
            if (rotulo > 128) rotulo = 128;

            int xVal = 16 + rotulo + 8;
            int larg = c.Width - xVal - 14;
            if (larg < 60) larg = 60;

            for (int i = 0; i < vals.Count; i++)
            {
                Label val = vals[i];
                Label cap = val.Tag as Label;
                int y = 46 + i * 24;
                if (cap != null) cap.SetBounds(16, y + 2, rotulo, 17);
                val.SetBounds(xVal, y, larg, 19);
            }
        }

        private void CopiarSpecs()
        {
            try
            {
                string t = SpecSheet.EmTexto();
                if (string.IsNullOrEmpty(t)) return;
                Clipboard.SetText(t);
                Aviso(T.SpecsCopied);
            }
            catch (Exception ex) { Log.Error("copiar especificacoes", ex); }
        }

        // ---------------- pagina: Paineis ----------------

        /// <summary>
        /// O cartao de cada painel mostra apenas o sensor ja escolhido; a
        /// escolha acontece numa janela dedicada.
        ///
        /// Embutida na pagina, a lista ficava com 142 px - cinco linhas e meia
        /// para varias dezenas de sensores, parte delas cabecalhos de grupo -
        /// porque disputava altura com escala, unidade e previa. Num dialogo
        /// ela nao disputa com nada.
        /// </summary>
        private Control BuildPagePaineis()
        {
            Panel page = new Pagina();
            ((Pagina)page).RolarNaVertical();
            // fundo opaco vem da propria Pagina

            // Os seletores continuam sendo o estado da escolha; so nao aparecem
            // na pagina. Quem os edita e o dialogo.
            _pick1 = new SensorPicker();
            _pick1.SetSensors(Clone(_sensors));
            _pick2 = new SensorPicker();
            _pick2.SetSensors(Clone(_sensors));

            Card c1 = new Card();
            c1.Title = T.Panel1;
            c1.SetBounds(0, 0, 370, 282);
            page.Controls.Add(c1);

            _slot1 = new SensorSlot();
            _slot1.SetBounds(12, 48, 346, 80);
            // Ancorado nos dois lados: com o cartao esticado, o nome do sensor e
            // a leitura ganham a largura junto, em vez de deixar um degrau branco
            // dentro de um cartao maior.
            _slot1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _slot1.Button.Click += delegate { TrocarSensor(_pick1, T.PickSensor1); };
            c1.Controls.Add(_slot1);

            c1.Controls.Add(MakeLabel(T.Scale, 14, 142, Ui.FontMed));
            _div1 = MakeDivisorRow(c1, 14, 162, 1);

            c1.Controls.Add(MakeLabel(T.Unit, 14, 196, Ui.FontMed));
            _unit1 = new Segmented();
            _unit1.SetItems("°C", "°F");
            _unit1.SetBounds(14, 216, 140, 30);
            _unit1.SelectedIndexChanged += delegate { OnChanged(); };
            c1.Controls.Add(_unit1);
            c1.Controls.Add(NotaDeUnidade(14, 249));

            Card c2 = new Card();
            c2.Title = T.Panel2;
            c2.SetBounds(386, 0, 370, 282);
            page.Controls.Add(c2);

            _slot2 = new SensorSlot();
            _slot2.SetBounds(12, 48, 346, 80);
            _slot2.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _slot2.Button.Click += delegate { TrocarSensor(_pick2, T.PickSensor2); };
            c2.Controls.Add(_slot2);

            c2.Controls.Add(MakeLabel(T.Scale, 14, 162 - 20, Ui.FontMed));
            _div2 = MakeDivisorRow(c2, 14, 162, 2);

            c2.Controls.Add(MakeLabel(T.Unit, 14, 196, Ui.FontMed));
            _unit2 = new Segmented();
            _unit2.SetItems("%", "W");
            _unit2.SetBounds(14, 216, 140, 30);
            _unit2.SelectedIndexChanged += delegate { OnChanged(); };
            c2.Controls.Add(_unit2);
            c2.Controls.Add(NotaDeUnidade(14, 249));


            _panelPreviewCard = new Card();
            _panelPreviewCard.Title = T.Preview;
            _panelPreviewCard.SetBounds(0, 294, 756, 532);
            page.Controls.Add(_panelPreviewCard);

            _preview = new PanelPreview();
            _preview.SetBounds(12, 44, 732, 476);
            _preview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                              AnchorStyles.Left | AnchorStyles.Right;
            _panelPreviewCard.Controls.Add(_preview);

            return page;
        }

        private void TrocarSensor(SensorPicker alvo, string titulo)
        {
            using (SensorDialog d = new SensorDialog(Clone(_sensors), alvo.SelectedId, titulo))
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    alvo.SelectedId = d.SelectedId;
                    if (alvo == _pick2) AjustarUnidade2();
                    OnChanged();
                }
        }

        /// <summary>
        /// Acende o simbolo certo do painel 2 conforme o sensor escolhido.
        ///
        /// O hardware so tem esses dois: potencia acende W, carga e nivel
        /// acendem %. Nos demais tipos - clock, tensao, RPM - nenhum dos dois
        /// descreve a leitura, entao a escolha anterior fica de pe em vez de
        /// mudar sozinha para algo igualmente errado. Continua editavel: o
        /// ajuste automatico so acontece quando o sensor troca.
        /// </summary>
        private void AjustarUnidade2()
        {
            SensorEntry s = _pick2.Selected;
            if (s == null) return;

            if (s.Type == SensorType.Power) _unit2.SelectedIndex = 1;
            else if (s.Type == SensorType.Load || s.Type == SensorType.Level) _unit2.SelectedIndex = 0;
        }

        private FlatBtn[] MakeDivisorRow(Control parent, int x, int y, int which)
        {
            FlatBtn[] btns = new FlatBtn[DivValues.Length];
            for (int i = 0; i < DivValues.Length; i++)
            {
                FlatBtn b = new FlatBtn();
                b.Text = DivLabels[i];
                b.Font = Ui.FontSmall;
                b.SetBounds(x + i * 68, y, 64, 26);
                b.Tag = DivValues[i];
                int captured = DivValues[i];
                int target = which;
                b.Click += delegate
                {
                    if (target == 1) _current.Divisor1 = captured; else _current.Divisor2 = captured;
                    OnChanged();
                };
                parent.Controls.Add(b);
                btns[i] = b;
            }
            return btns;
        }

        /// <summary>
        /// Nota de rodape do seletor de unidade. Cabe nos 268 px do cartao sem
        /// mexer no resto da pagina: o rotulo e o seletor sobem 4 px e a nota
        /// ocupa a folga que sobrava embaixo.
        /// </summary>
        private static Label NotaDeUnidade(int x, int y)
        {
            Label l = MakeLabel(T.UnitAlwaysOn, x, y, Ui.FontSmall);
            l.SetBounds(x, y, 340, 15);
            l.ForeColor = Ui.Muted;
            return l;
        }

        private static Label MakeLabel(string text, int x, int y, Font f)
        {
            Label l = new Label();
            l.Text = text; l.Font = f; l.AutoSize = false;
            l.SetBounds(x, y, 300, 18);
            l.BackColor = Color.Transparent;
            l.ForeColor = Ui.Text;
            return l;
        }

        private static List<SensorEntry> Clone(List<SensorEntry> src)
        {
            List<SensorEntry> copy = new List<SensorEntry>(src.Count);
            foreach (SensorEntry s in src)
            {
                SensorEntry e = new SensorEntry();
                e.Id = s.Id; e.Hardware = s.Hardware; e.Name = s.Name;
                e.Category = s.Category;
                e.Label = s.Label; e.Type = s.Type; e.Value = s.Value; e.Unit = s.Unit;
                // Source e Members faltavam: a lista declarava tudo como
                // LibreHardwareMonitor, o que nao aparecia enquanto a
                // procedencia nao era exibida na linha do sensor.
                e.Source = s.Source; e.Members = s.Members;
                copy.Add(e);
            }
            return copy;
        }

        /// <summary>Gruda o controle na borda direita do que o hospeda.</summary>
        private static FlatBtn ADireita(FlatBtn b)
        {
            b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            return b;
        }

        // ---------------- pagina: Perfis ----------------

        /// <summary>
        /// Perfis: a lista de um lado, o que o perfil selecionado poe no
        /// mostrador do outro.
        ///
        /// A previa vem para ca porque a pergunta que se faz nesta pagina e
        /// "qual deles eu quero agora", e ela nao se responde com uma lista de
        /// nomes. Antes era preciso selecionar um perfil, ir ate a pagina de
        /// paineis para ver o que ele mostra, voltar e repetir com o proximo.
        /// </summary>
        private Control BuildPagePerfis()
        {
            Panel page = new Pagina();
            ((Pagina)page).RolarNaVertical();
            // fundo opaco vem da propria Pagina

            Card c = new Card();
            c.Title = T.SavedProfiles;
            c.SetBounds(0, 0, 330, 500);
            page.Controls.Add(c);

            _profileList = new ProfileList();
            _profileList.SetBounds(12, 44, 306, 300);
            _profileList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _profileList.Resolve = NomeDoSensor;
            _profileList.ActiveName = _cfg.ActiveName;
            _profileList.DefaultName = _cfg.DefaultProfile.Name;
            _profileList.SelectionChanged += new EventHandler(OnProfileSelected);
            _profileList.ItemActivated += delegate { AplicarPerfil(); };
            c.Controls.Add(_profileList);

            // Grade de dois por tres. A coluna da esquerda fica na esquerda e a
            // da direita na direita, senao o cartao esticado abre um vao a
            // direita dos seis botoes e a grade some para um canto.
            c.Controls.Add(MakeSideButton(T.New, 12, 356, 145, new EventHandler(OnNewProfile)));
            c.Controls.Add(ADireita(MakeSideButton(T.Rename, 173, 356, 145, new EventHandler(OnRenameProfile))));
            c.Controls.Add(MakeSideButton(T.Duplicate, 12, 396, 145, new EventHandler(OnDuplicateProfile)));
            FlatBtn del = ADireita(MakeSideButton(T.Delete, 173, 396, 145, new EventHandler(OnDeleteProfile)));
            del.Danger = true;
            c.Controls.Add(del);
            c.Controls.Add(MakeSideButton(T.Export, 12, 436, 145, new EventHandler(OnExportProfile)));
            c.Controls.Add(ADireita(MakeSideButton(T.Import, 173, 436, 145, new EventHandler(OnImportProfile))));

            Card cv = new Card();
            cv.Title = T.ProfilePreview;
            cv.SetBounds(346, 0, 410, 500);
            page.Controls.Add(cv);

            _profilePreview = new PanelPreview();
            _profilePreview.SetBounds(12, 44, 386, 250);
            _profilePreview.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cv.Controls.Add(_profilePreview);

            _profileSensor1 = new FlatBtn();
            _profileSensor1.SetBounds(12, 306, 386, 34);
            _profileSensor1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _profileSensor1.Click += delegate { TrocarSensorDoPerfil(1); };
            cv.Controls.Add(_profileSensor1);

            _profileSensor2 = new FlatBtn();
            _profileSensor2.SetBounds(12, 346, 386, 34);
            _profileSensor2.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _profileSensor2.Click += delegate { TrocarSensorDoPerfil(2); };
            cv.Controls.Add(_profileSensor2);

            _btnDefault = new FlatBtn();
            _btnDefault.Text = T.SetAsDefault;
            _btnDefault.SetBounds(12, 416, 150, 40);
            _btnDefault.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _btnDefault.Click += delegate { DefinirPerfilPadrao(); };
            cv.Controls.Add(_btnDefault);

            _btnApply = new FlatBtn();
            _btnApply.Text = T.ApplyProfile;
            _btnApply.Primary = true;
            _btnApply.SetBounds(168, 416, 230, 40);
            _btnApply.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _btnApply.Click += delegate { AplicarPerfil(); };
            cv.Controls.Add(_btnApply);

            // A automacao por jogo vem antes do rodizio: ela e uma relacao entre
            // o perfil selecionado acima e um aplicativo real, e deixa-la no fim
            // escondia a acao principal atras de uma frase com apenas a contagem.
            _cardGame = new Card();
            _cardGame.Title = T.GameProfilesCard;
            _cardGame.SetBounds(0, 512, 756, 400);
            page.Controls.Add(_cardGame);

            _tgJogo = new Toggle();
            _tgJogo.Label = T.GameProfilesOn;
            _tgJogo.SetBounds(16, 48, 560, 26);
            _tgJogo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _tgJogo.CheckedChanged += new EventHandler(OnToggleJogo);
            _cardGame.Controls.Add(_tgJogo);

            Label atual = MakeLabel(T.CurrentGame, 16, 84, Ui.FontSmall);
            atual.ForeColor = Ui.Muted;
            _cardGame.Controls.Add(atual);

            _gameIcon = new GameIconView();
            // O centro coincide com o ícone das linhas de vínculos abaixo.
            _gameIcon.SetBounds(24, 108, 52, 52);
            _cardGame.Controls.Add(_gameIcon);

            _lbJogoNome = MakeLabel(T.NoGameToBind, 82, 106, Ui.FontMed);
            _lbJogoNome.Size = new Size(400, 26);
            _lbJogoNome.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _cardGame.Controls.Add(_lbJogoNome);

            _lbJogoAtual = MakeLabel("", 82, 132, Ui.FontSmall);
            _lbJogoAtual.Size = new Size(400, 22);
            _lbJogoAtual.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lbJogoAtual.ForeColor = Ui.Accent;
            _cardGame.Controls.Add(_lbJogoAtual);

            _btVincular = new FlatBtn();
            _btVincular.Text = T.BindGame;
            _btVincular.Primary = true;
            _btVincular.SetBounds(510, 106, 230, 34);
            _btVincular.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btVincular.Click += delegate { VincularJogo(); };
            _cardGame.Controls.Add(_btVincular);

            _btDesvincular = new FlatBtn();
            _btDesvincular.Text = T.UnbindGame;
            _btDesvincular.SetBounds(510, 146, 230, 30);
            _btDesvincular.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btDesvincular.Click += delegate { DesvincularJogo(); };
            _cardGame.Controls.Add(_btDesvincular);

            _lbJogoNota = MakeLabel(T.GameProfilesNote, 16, 177, Ui.FontSmall);
            _lbJogoNota.Size = new Size(724, 38);
            _lbJogoNota.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lbJogoNota.ForeColor = Ui.Faint;
            _cardGame.Controls.Add(_lbJogoNota);

            Label vinculos = MakeLabel(T.GameBindings, 16, 219, Ui.FontMed);
            _cardGame.Controls.Add(vinculos);

            _gameBindings = new GameBindingList();
            _gameBindings.EmptyText = T.NoGameBindings;
            _gameBindings.SetBounds(16, 246, 724, 138);
            _gameBindings.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _gameBindings.RemoveRequested += delegate(string key) { RemoverVinculo(key); };
            _cardGame.Controls.Add(_gameBindings);

            // O rodizio continua junto dos perfis, mas depois da automacao mais
            // frequente e visualmente mais rica.
            _cardRotation = new Card();
            _cardRotation.Title = T.Rotation;
            _cardRotation.SetBounds(0, 924, 756, 150);
            page.Controls.Add(_cardRotation);

            _tgRotate = new Toggle();
            _tgRotate.Label = T.IncludeInRotation;
            _tgRotate.SetBounds(16, 48, 460, 26);
            _tgRotate.CheckedChanged += new EventHandler(OnToggleRotate);
            _cardRotation.Controls.Add(_tgRotate);

            _rotSecs = new NumberBox();
            _rotSecs.Minimum = 2; _rotSecs.Maximum = 999;
            _rotSecs.Value = _cfg.RotateSeconds > 0 ? _cfg.RotateSeconds : 20;
            _rotSecs.SetBounds(16, 84, 110, 32);
            _rotSecs.ValueChanged += new EventHandler(OnRotateSeconds);
            _cardRotation.Controls.Add(_rotSecs);

            _rotNote = MakeLabel("", 134, 84, Ui.FontSmall);
            _rotNote.Size = new Size(600, 32);
            _rotNote.TextAlign = ContentAlignment.MiddleLeft;
            _rotNote.ForeColor = Ui.Muted;
            _cardRotation.Controls.Add(_rotNote);

            _profilesNote = MakeLabel(T.ProfilesNote, 2, 1086, Ui.FontSmall);
            _profilesNote.Size = new Size(750, 40);
            _profilesNote.ForeColor = Ui.Muted;
            page.Controls.Add(_profilesNote);

            AtualizarRodizio();
            AtualizarJogo();
            return page;
        }

        /// <summary>Edita o sensor do perfil sem obrigar a ida ate Paineis.</summary>
        private void TrocarSensorDoPerfil(int painel)
        {
            Profile p = _profileList != null && _profileList.Selected != null
                      ? _profileList.Selected : _current;
            if (p == null) return;

            string atual = painel == 1 ? p.Panel1Id : p.Panel2Id;
            string titulo = painel == 1 ? T.PickSensor1 : T.PickSensor2;
            using (SensorDialog d = new SensorDialog(Clone(_sensors), atual, titulo))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (painel == 1) p.Panel1Id = d.SelectedId;
                else
                {
                    p.Panel2Id = d.SelectedId;
                    SensorEntry s = Achar(d.SelectedId);
                    p.Percent = s == null || s.Type != SensorType.Power;
                }
            }

            _dirty = true;
            LoadFromProfile();
            _profileList.Invalidate();
            AtualizarPreviaDoPerfil();
        }

        /// <summary>
        /// Marca o perfil selecionado como participante do rodizio.
        ///
        /// A marca e do perfil, e o periodo e da maquina: dois perfis nao
        /// podem discordar sobre de quanto em quanto tempo a roda gira.
        /// </summary>
        private void OnToggleRotate(object sender, EventArgs e)
        {
            if (_loading) return;
            Profile p = _profileList != null ? _profileList.Selected : _current;
            if (p == null) return;
            p.Rotate = _tgRotate.Checked;
            _cfg.RotateSeconds = _rotSecs.Value;
            _dirty = true;
            _profileList.Invalidate();
            AtualizarRodizio();
        }

        private void OnRotateSeconds(object sender, EventArgs e)
        {
            if (_loading) return;
            _cfg.RotateSeconds = _rotSecs.Value;
            _dirty = true;
            AtualizarRodizio();
        }

        // ---------------- perfil por jogo ----------------

        private Toggle _tgJogo;
        private Label _lbJogoAtual, _lbJogoNome, _lbJogoNota;
        private FlatBtn _btVincular, _btDesvincular;
        private GameIconView _gameIcon;
        private GameBindingList _gameBindings;
        private readonly Dictionary<string, GameIdentityInfo> _gameIdentityCache =
            new Dictionary<string, GameIdentityInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _gameIdentityRetry =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private string _gameBindingsSignature = null;

        private void OnToggleJogo(object sender, EventArgs e)
        {
            if (_loading) return;
            _cfg.GameProfiles = _tgJogo.Checked;
            _dirty = true;
            AtualizarJogo();
        }

        /// <summary>
        /// O jogo detectado agora e o vinculo dele, se houver.
        ///
        /// Diz na cara quando o RTSS nao esta publicando. Um interruptor ligado
        /// que nunca age e a pior forma de recurso quebrado: parece configurado,
        /// nao reclama, e so nao funciona.
        /// </summary>
        private void AtualizarJogo()
        {
            if (_lbJogoAtual == null) return;

            _loading = true;
            _tgJogo.Checked = _cfg.GameProfiles;
            _loading = false;

            string jogo = Rtss.JogoAtual;
            bool temJogo = !string.IsNullOrEmpty(jogo);
            bool temRtss = Rtss.Presente();

            GameIdentityInfo identity = temJogo ? IdentidadeDoJogo(jogo, Rtss.JogoAtualPid) : null;
            string casado = temJogo ? _cfg.PerfilDoJogo(jogo) : null;
            Profile selecionado = _profileList != null && _profileList.Selected != null
                ? _profileList.Selected : _current;

            _lbJogoNome.Text = !temRtss ? T.RtssMissing
                              : (identity != null ? identity.DisplayName : T.NoGameToBind);
            _lbJogoNome.ForeColor = temRtss ? Ui.Text : Ui.Warn;
            _lbJogoAtual.Text = !temJogo ? T.GamesBoundCount(_cfg.GameKeys.Count)
                               : (!string.IsNullOrEmpty(casado)
                                  ? T.BoundProfile(casado)
                                  : T.SelectedProfile(selecionado != null ? selecionado.Name : "—"));
            _lbJogoAtual.ForeColor = !string.IsNullOrEmpty(casado) ? Ui.Accent : Ui.Muted;
            _gameIcon.GameIcon = identity != null ? identity.Icon : null;

            _btVincular.Enabled = _cfg.GameProfiles && temJogo;
            _btVincular.Text = selecionado != null ? T.BindGameTo(selecionado.Name) : T.BindGame;
            _btDesvincular.Enabled = _cfg.GameProfiles && temJogo &&
                                     !string.IsNullOrEmpty(casado);
            _tgJogo.Enabled = temRtss;

            AtualizarListaDeJogos(jogo);
        }

        private GameIdentityInfo IdentidadeDoJogo(string key, int pid)
        {
            if (string.IsNullOrEmpty(key)) return null;
            GameIdentityInfo info;
            if (_gameIdentityCache.TryGetValue(key, out info))
            {
                DateTime retry;
                bool waiting = _gameIdentityRetry.TryGetValue(key, out retry) &&
                               DateTime.UtcNow < retry;
                // Uma identidade completa e imutavel durante a sessao. Quando
                // falta o icone, tenta novamente com baixa frequencia em vez de
                // consultar processo, versao e Registro a cada tick da tela.
                if (info.Icon != null || waiting || pid <= 0) return info;
            }

            info = GameIdentity.Resolve(key, pid, _cfg.NomeDoJogo(key), _cfg.CaminhoDoJogo(key));
            _gameIdentityCache[key] = info;
            _gameIdentityRetry[key] = DateTime.UtcNow.AddSeconds(info.Icon != null ? 300 : 20);
            // Guarda no rascunho SEM sujar a janela. Descobrir o nome e o icone de
            // um jogo ja vinculado e trabalho do tick, nao edicao de ninguem:
            // marcar pendencia aqui acendia o botao Salvar e fazia o aviso de
            // "alteracoes nao salvas" aparecer ao fechar uma janela em que a
            // pessoa nao tocou. A identidade acompanha a proxima gravacao de
            // verdade e, ate la, sai do cache a cada abertura.
            if (_cfg.PerfilDoJogo(key) != null)
                _cfg.IdentificarJogo(key, info.DisplayName, info.ExecutablePath);
            return info;
        }

        private void AtualizarListaDeJogos(string atual)
        {
            if (_gameBindings == null) return;
            List<GameBindingView> items = new List<GameBindingView>();
            System.Text.StringBuilder signature = new System.Text.StringBuilder();
            for (int i = 0; i < _cfg.GameKeys.Count && i < _cfg.GameProfileNames.Count; i++)
            {
                string key = _cfg.GameKeys[i];
                GameIdentityInfo identity = IdentidadeDoJogo(key,
                    string.Equals(key, atual, StringComparison.OrdinalIgnoreCase) ? Rtss.JogoAtualPid : 0);
                GameBindingView item = new GameBindingView();
                item.Key = key;
                item.Name = identity != null ? identity.DisplayName : GameIdentity.Humanize(key);
                item.Profile = _cfg.GameProfileNames[i];
                item.Icon = identity != null ? identity.Icon : null;
                item.Current = string.Equals(key, atual, StringComparison.OrdinalIgnoreCase);
                items.Add(item);
                signature.Append(key).Append('|').Append(item.Name).Append('|')
                         .Append(item.Profile).Append('|').Append(item.Current).Append('|')
                         .Append(item.Icon != null).Append('\n');
            }
            string next = signature.ToString();
            if (next == _gameBindingsSignature) return;
            _gameBindingsSignature = next;
            _gameBindings.SetItems(items);
            ArranjarSecaoDeJogos(items.Count);
        }

        private void ArranjarSecaoDeJogos(int quantidade)
        {
            if (_cardGame == null || _gameBindings == null || _cardRotation == null) return;
            _gameBindings.Height = GameBindingList.PreferredHeight(quantidade);
            _cardGame.Height = _gameBindings.Bottom + 16;
            _cardRotation.Top = _cardGame.Bottom + 12;
            if (_profilesNote != null) _profilesNote.Top = _cardRotation.Bottom + 12;
            Pagina pagina = _pgPerfis as Pagina;
            if (pagina != null) pagina.Sincronizar();
        }

        /// <summary>Casa o jogo detectado com o perfil SELECIONADO na lista.</summary>
        private void VincularJogo()
        {
            string jogo = Rtss.JogoAtual;
            if (string.IsNullOrEmpty(jogo)) return;

            Profile p = _profileList != null && _profileList.Selected != null
                      ? _profileList.Selected : _current;
            if (p == null) return;

            GameIdentityInfo identity = IdentidadeDoJogo(jogo, Rtss.JogoAtualPid);
            _cfg.MapearJogo(jogo, p.Name);
            if (identity != null)
                _cfg.IdentificarJogo(jogo, identity.DisplayName, identity.ExecutablePath);
            _gameIdentityCache[jogo] = identity;
            _gameBindingsSignature = null;
            _dirty = true;
            AtualizarJogo();
            Aviso(T.GameBound(identity != null ? identity.DisplayName : GameIdentity.Humanize(jogo), p.Name));
        }

        private void DesvincularJogo()
        {
            string jogo = Rtss.JogoAtual;
            if (string.IsNullOrEmpty(jogo)) return;
            _cfg.DesmapearJogo(jogo);
            _gameBindingsSignature = null;
            _dirty = true;
            AtualizarJogo();
        }

        private void RemoverVinculo(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _cfg.DesmapearJogo(key);
            _gameBindingsSignature = null;
            _dirty = true;
            AtualizarJogo();
        }

        private void AtualizarRodizio()
        {
            if (_rotNote == null) return;

            Profile p = _profileList != null ? _profileList.Selected : _current;
            _loading = true;
            _tgRotate.Checked = p != null && p.Rotate;
            _loading = false;

            int n = _cfg.Rotation.Count;
            bool ativo = n >= 2 && _cfg.RotateSeconds > 0;
            _rotSecs.Enabled = n >= 2;
            _rotNote.Text = ativo ? T.RotationOn(n) : T.RotationOff;
            _rotNote.ForeColor = ativo ? Ui.Muted : Ui.Faint;
        }

        /// <summary>Nome curto de um sensor, para o resumo de cada perfil.</summary>
        private string NomeDoSensor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (SensorEntry s in _sensors) if (s.Id == id) return s.Name;
            return null;
        }

        /// <summary>
        /// Torna ativo o perfil selecionado e grava.
        ///
        /// Aplicar e gravar sao o mesmo gesto de proposito: um perfil "ativo"
        /// que nao sobreviveria a fechar a janela seria uma promessa falsa, e a
        /// thread de atualizacao le o mesmo objeto de configuracao - o
        /// mostrador muda no ciclo seguinte.
        /// </summary>
        private void AplicarPerfil()
        {
            Profile p = _profileList != null ? _profileList.Selected : _current;
            if (p == null) return;

            SaveToProfile();
            string anterior = _cfg.ActiveName;
            _cfg.ActiveName = p.Name;
            string erro;
            bool salvo = _session.TrySaveAll(out erro);
            if (!salvo)
            {
                // Aplicar significa tambem sobreviver ao fechamento. Sem disco,
                // manter o novo perfil ativo no worker daria uma troca que some
                // no proximo arranque e contradiz o proprio botao.
                _cfg.ActiveName = anterior;
                _saved = false; _dirty = true;
                Aviso(T.SaveFailed(erro));
                return;
            }
            _saved = true; _dirty = false;

            _profileList.ActiveName = p.Name;
            _profileList.Invalidate();
            AtualizarPreviaDoPerfil();
            if (Applied != null) Applied(this, EventArgs.Empty);
            Aviso(T.ApplyProfile + ": " + p.Name);
        }

        /// <summary>Escolhe e persiste o destino usado quando um jogo termina.</summary>
        private void DefinirPerfilPadrao()
        {
            Profile p = _profileList != null ? _profileList.Selected : _current;
            if (p == null) return;

            SaveToProfile();
            string anterior = _cfg.DefaultProfileName;
            _cfg.DefaultProfileName = p.Name;
            string erro;
            if (!_session.TrySaveAll(out erro))
            {
                _cfg.DefaultProfileName = anterior;
                _saved = false; _dirty = true;
                Aviso(T.SaveFailed(erro));
                return;
            }

            _saved = true; _dirty = false;
            _profileList.DefaultName = p.Name;
            _profileList.Invalidate();
            AtualizarPreviaDoPerfil();
            Aviso(T.DefaultProfileSet(p.Name));
        }

        private FlatBtn MakeSideButton(string text, int x, int y, int w, EventHandler h)
        {
            FlatBtn b = new FlatBtn();
            b.Text = text;
            b.SetBounds(x, y, w, 32);
            b.Click += h;
            return b;
        }

        private void RefreshProfileList()
        {
            if (_profileList == null) return;
            _loading = true;
            _profileList.ActiveName = _cfg.ActiveName;
            _profileList.DefaultName = _cfg.DefaultProfile.Name;
            _profileList.SetItems(_cfg.Profiles, _current);
            _loading = false;
            AtualizarPreviaDoPerfil();
        }

        /// <summary>Espelha o perfil selecionado no mostrador da direita.</summary>
        private void AtualizarPreviaDoPerfil()
        {
            if (_profilePreview == null) return;
            Profile p = _profileList != null && _profileList.Selected != null ? _profileList.Selected : _current;
            if (p == null) return;

            SensorEntry s1 = Achar(p.Panel1Id), s2 = Achar(p.Panel2Id);
            PanelValue v1 = Scaling.Prepare(s1, Scaling.Effective(p.Divisor1, s1), p.Fahrenheit);
            PanelValue v2 = Scaling.Prepare(s2, Scaling.Effective(p.Divisor2, s2), false);

            _profilePreview.Value1 = v1.Value;
            _profilePreview.Value2 = v2.Value;
            _profilePreview.Fahrenheit = p.Fahrenheit;
            _profilePreview.Percent = p.Percent;
            _profilePreview.Invalidate();

            string n1 = NomeDoSensor(p.Panel1Id), n2 = NomeDoSensor(p.Panel2Id);
            _profileSensor1.Text = T.PanelShort(1) + "  ·  " + (n1 ?? T.NoSensorChosen) +
                                   "  (" + (p.Fahrenheit ? "°F" : "°C") + ")";
            _profileSensor2.Text = T.PanelShort(2) + "  ·  " + (n2 ?? T.NoSensorChosen) +
                                   "  (" + (p.Percent ? "%" : "W") + ")";
            AtualizarRodizio();

            bool ativo = string.Equals(p.Name, _cfg.ActiveName, StringComparison.Ordinal);
            _btnApply.Text = ativo ? T.ApplyProfile + "  (" + T.AlreadyActive + ")" : T.ApplyProfile;
            _btnApply.Enabled = !ativo;
            _btnApply.Invalidate();

            bool padrao = string.Equals(p.Name, _cfg.DefaultProfile.Name,
                                        StringComparison.Ordinal);
            _btnDefault.Text = padrao ? T.AlreadyDefault : T.SetAsDefault;
            _btnDefault.Enabled = !padrao;
            _btnDefault.Invalidate();
        }

        /// <summary>
        /// Leva o ciclo tambem para a lista bruta.
        ///
        /// Os seletores trabalham sobre copias; a previa de perfil consulta a
        /// lista original, que sem isto ficaria congelada no instante em que a
        /// janela abriu.
        /// </summary>
        private void AtualizarValores(Dictionary<string, float> snap)
        {
            if (snap == null || _sensors == null) return;
            foreach (SensorEntry s in _sensors)
            {
                float v;
                if (snap.TryGetValue(s.Id, out v)) s.Value = v;
            }
        }

        private SensorEntry Achar(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (SensorEntry s in _sensors) if (s.Id == id) return s;
            return null;
        }

        private void OnProfileSelected(object sender, EventArgs e)
        {
            if (_loading) return;
            Profile p = _profileList.Selected;
            if (p == null || p == _current) return;
            SaveToProfile();
            _current = p;
            _nav.Subtitle = _current.Name;
            _nav.Invalidate();
            LoadFromProfile();
            AtualizarPreviaDoPerfil();
            AtualizarJogo();
        }

        private void OnNewProfile(object sender, EventArgs e)
        {
            string name = Prompt(T.NewProfileName, T.DefaultProfileName(_cfg.Profiles.Count + 1));
            if (string.IsNullOrEmpty(name)) return;
            if (_cfg.NameExists(name)) { Warn(T.NameTaken); return; }
            SaveToProfile();
            Profile np = new Profile();
            np.Name = name;
            _cfg.Profiles.Add(np);
            _current = np;
            _dirty = true;
            _nav.Subtitle = name; _nav.Invalidate();
            RefreshProfileList(); LoadFromProfile();
        }

        private void OnDuplicateProfile(object sender, EventArgs e)
        {
            string name = Prompt(T.CopyName, _current.Name + T.CopySuffix);
            if (string.IsNullOrEmpty(name)) return;
            if (_cfg.NameExists(name)) { Warn(T.NameTaken); return; }
            SaveToProfile();
            Profile np = _current.Clone();
            np.Name = name;
            _cfg.Profiles.Add(np);
            _current = np;
            _dirty = true;
            _nav.Subtitle = name; _nav.Invalidate();
            RefreshProfileList(); LoadFromProfile();
        }

        /// <summary>
        /// Grava o perfil selecionado num arquivo.
        ///
        /// SaveToProfile antes de tudo porque o perfil em edicao mora nos
        /// controles ate alguem mandar grava-lo: sem isso, exportar logo depois
        /// de trocar um sensor exportaria o valor antigo, e o arquivo sairia
        /// diferente do que esta na tela sem nenhum aviso.
        /// </summary>
        private void OnExportProfile(object sender, EventArgs e)
        {
            SaveToProfile();
            Profile p = _profileList != null && _profileList.Selected != null ? _profileList.Selected : _current;
            if (p == null) return;

            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = T.ExportProfile;
                d.Filter = T.ProfileFilter;
                d.AddExtension = true;
                d.DefaultExt = "ini";
                d.FileName = NomeDeArquivo(p.Name) + ".ini";
                if (d.ShowDialog(this) != DialogResult.OK) return;

                string erro;
                if (Config.ExportProfile(p, d.FileName, out erro)) Aviso(T.Exported(p.Name));
                else Warn(T.ExportFailed(erro));
            }
        }

        private void OnImportProfile(object sender, EventArgs e)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = T.ImportProfile;
                d.Filter = T.ProfileFilter;
                d.CheckFileExists = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;

                string erro;
                Profile p = Config.ImportProfile(d.FileName, out erro);
                if (p == null) { Warn(T.ImportFailed(erro)); return; }

                SaveToProfile();
                p.Name = _cfg.UniqueName(p.Name);
                _cfg.Profiles.Add(p);
                _current = p;
                _dirty = true;
                _nav.Subtitle = p.Name; _nav.Invalidate();
                RefreshProfileList(); LoadFromProfile();

                // Identificador de sensor nao viaja entre maquinas: ele carrega
                // o modelo do hardware. Um perfil vindo de outro computador
                // entra com o mostrador em branco, e calar sobre isso deixaria
                // o usuario procurando defeito onde nao ha.
                if (NomeDoSensor(p.Panel1Id) == null || NomeDoSensor(p.Panel2Id) == null)
                    Warn(T.ImportedUnknownSensor);
                else
                    Aviso(T.Imported(p.Name));
            }
        }

        /// <summary>Nome de perfil reduzido ao que o Windows aceita num arquivo.</summary>
        private static string NomeDeArquivo(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return "perfil";
            char[] proibidos = System.IO.Path.GetInvalidFileNameChars();
            System.Text.StringBuilder sb = new System.Text.StringBuilder(nome.Length);
            foreach (char c in nome)
                sb.Append(Array.IndexOf(proibidos, c) >= 0 ? '_' : c);
            string s = sb.ToString().Trim();
            return s.Length == 0 ? "perfil" : s;
        }

        private void OnRenameProfile(object sender, EventArgs e)
        {
            Profile alvo = _profileList != null && _profileList.Selected != null ? _profileList.Selected : _current;
            string name = Prompt(T.NewName, alvo.Name);
            if (string.IsNullOrEmpty(name) || name == alvo.Name) return;
            if (_cfg.NameExists(name)) { Warn(T.NameTaken); return; }

            // Renomear o perfil ativo tem de levar o ponteiro junto: ActiveName
            // guarda o nome, e nao a referencia, entao o antigo deixaria de
            // casar com qualquer perfil e o primeiro da lista viraria o ativo.
            bool eraAtivo = string.Equals(alvo.Name, _cfg.ActiveName, StringComparison.Ordinal);
            bool eraPadrao = string.Equals(alvo.Name, _cfg.DefaultProfile.Name, StringComparison.Ordinal);
            string nomeAnterior = alvo.Name;
            alvo.Name = name;
            _cfg.RenomearPerfilNosJogos(nomeAnterior, name);
            if (eraAtivo) _cfg.ActiveName = name;
            if (eraPadrao) _cfg.DefaultProfileName = name;

            if (alvo == _current) { _nav.Subtitle = name; _nav.Invalidate(); }
            _dirty = true;
            RefreshProfileList();
        }

        private void OnDeleteProfile(object sender, EventArgs e)
        {
            if (_cfg.Profiles.Count <= 1) { Warn(T.KeepOneProfile); return; }
            Profile alvo = _profileList != null && _profileList.Selected != null ? _profileList.Selected : _current;
            if (MessageBox.Show(T.DeleteProfileQ(alvo.Name), T.AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            _cfg.RemoverPerfilDosJogos(alvo.Name);
            _cfg.Profiles.Remove(alvo);
            if (_current == alvo) _current = _cfg.Profiles[0];
            if (string.Equals(_cfg.ActiveName, alvo.Name, StringComparison.Ordinal))
                _cfg.ActiveName = _cfg.Profiles[0].Name;
            if (string.Equals(_cfg.DefaultProfileName, alvo.Name, StringComparison.Ordinal))
                _cfg.DefaultProfileName = _cfg.NameExists(_cfg.ActiveName)
                    ? _cfg.ActiveName : _cfg.Profiles[0].Name;
            _cfg.EnsureDefaultProfile();

            _nav.Subtitle = _current.Name;
            _nav.Invalidate();
            _dirty = true;
            RefreshProfileList(); LoadFromProfile();
        }

        private static void Warn(string msg)
        {
            MessageBox.Show(msg, T.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Confirmacao discreta no rodape, que se apaga sozinha.</summary>
        private void Aviso(string texto)
        {
            if (_footerNote == null) return;
            _footerNote.Text = texto;
            _footerNote.ForeColor = Ui.Accent;
            _footerNote.Visible = _nav == null || _nav.Width >= 120;

            if (_noteTimer == null)
            {
                _noteTimer = new Timer();
                _noteTimer.Interval = 2600;
                // Volta ao estado, e nao ao vazio: se o aviso apareceu por causa
                // de algo que deixou edicao pendente, quem some e a confirmacao,
                // nao o alerta de que ainda ha o que gravar.
                _noteTimer.Tick += delegate { _noteTimer.Stop(); AtualizarRodape(); };
            }
            _noteTimer.Stop();
            _noteTimer.Start();
        }

        /// <summary>
        /// Poe o rodape de acordo com haver ou nao edicao pendente.
        ///
        /// Um aviso no ar tem precedencia: ele dura 2,6 s e ja esta dizendo algo
        /// mais especifico ("Salvo", "Perfil aplicado"). Sobrescreve-lo aqui
        /// apagaria a confirmacao no instante em que ela apareceu, e o clique
        /// pareceria nao ter feito nada. Quando o tempo dele vence, o Tick chama
        /// este metodo e o estado volta.
        /// </summary>
        private void AtualizarRodape()
        {
            if (_btSave != null) _btSave.Enabled = _dirty;
            if (_footerNote == null) return;
            if (_noteTimer != null && _noteTimer.Enabled) return;

            // Mesmo texto do dialogo de fechar, de proposito: a barra avisa e o
            // dialogo cobra a mesma coisa, e duas redacoes para o mesmo estado
            // fariam parecer dois estados.
            _footerNote.Text = _dirty ? T.UnsavedTitle : "";
            _footerNote.ForeColor = Ui.Warn;
            _footerNote.Visible = _dirty && _nav != null && _nav.Width >= 120;
        }

        // ---------------- pagina: Configuracoes ----------------

        /// <summary>
        /// Preferencias da maquina, em pagina propria.
        ///
        /// Elas moravam dentro do Sobre, entre a identidade do programa e a
        /// isencao de responsabilidade, e isso as escondia duas vezes: quem
        /// procura por uma preferencia nao clica em "Sobre", e quem clica em
        /// "Sobre" quer creditos, nao um formulario. Sao de outra natureza que
        /// o resto - valem para o programa todo, e nao para o perfil.
        /// </summary>
        private Control BuildPageConfig()
        {
            Panel page = new Pagina();
            ((Pagina)page).RolarNaVertical();
            // fundo opaco vem da propria Pagina

            Card c = new Card();
            c.Title = T.NavSettings;
            c.SetBounds(0, 0, 756, 402);
            page.Controls.Add(c);

            c.Controls.Add(MakeLabel(T.Language_, 16, 50, Ui.FontMed));

            _lang = new Segmented();
            _lang.SetItems("Português (BR)", "English (US)");
            _lang.SetBounds(16, 74, 300, 30);
            _lang.SelectedIndex = T.Pt ? 0 : 1;
            // depois de fixar a posicao: o seletor avisa toda mudanca, e a
            // primeira seria a nossa - reabriria a janela ao abrir a janela
            _lang.SelectedIndexChanged += new EventHandler(OnLanguageChanged);
            c.Controls.Add(_lang);

            Label langNote = MakeLabel(T.LanguageNote, 328, 74, Ui.FontSmall);
            langNote.Size = new Size(400, 30);
            langNote.TextAlign = ContentAlignment.MiddleLeft;
            langNote.ForeColor = Ui.Muted;
            c.Controls.Add(langNote);

            _tgAutostart = new Toggle();
            _tgAutostart.Label = T.StartWithWindows;
            _tgAutostart.SetBounds(16, 128, 400, 26);
            _tgAutostart.Checked = Autostart.IsEnabled();
            _tgAutostart.CheckedChanged += new EventHandler(OnToggleAutostart);
            c.Controls.Add(_tgAutostart);

            Toggle tgAll = new Toggle();
            tgAll.Label = T.ShowAllSensors;
            tgAll.SetBounds(16, 170, 480, 26);
            tgAll.Checked = _cfg.ShowAllSensors;
            tgAll.CheckedChanged += new EventHandler(OnToggleShowAll);
            c.Controls.Add(tgAll);

            Label allNote = MakeLabel(T.ShowAllNote, 16, 198, Ui.FontSmall);
            allNote.Size = new Size(720, 30);
            allNote.ForeColor = Ui.Muted;
            c.Controls.Add(allNote);

            _tgIdle = new Toggle();
            _tgIdle.Label = T.BlankWhenIdle;
            _tgIdle.SetBounds(16, 244, 480, 26);
            _tgIdle.Checked = _cfg.IdleBlankMinutes > 0;
            _tgIdle.CheckedChanged += new EventHandler(OnToggleIdle);
            c.Controls.Add(_tgIdle);

            _idleMin = new NumberBox();
            _idleMin.Minimum = 1; _idleMin.Maximum = 999;
            _idleMin.Value = _cfg.IdleBlankMinutes > 0 ? _cfg.IdleBlankMinutes : 15;
            _idleMin.SetBounds(16, 274, 110, 32);
            _idleMin.Enabled = _tgIdle.Checked;
            _idleMin.ValueChanged += new EventHandler(OnIdleMinutes);
            c.Controls.Add(_idleMin);

            _idleNote = MakeLabel("", 134, 274, Ui.FontSmall);
            _idleNote.Size = new Size(600, 32);
            _idleNote.TextAlign = ContentAlignment.MiddleLeft;
            _idleNote.ForeColor = Ui.Muted;
            c.Controls.Add(_idleNote);

            Label idleHint = MakeLabel(T.BlankWhenIdleNote, 16, 312, Ui.FontSmall);
            idleHint.Size = new Size(720, 34);
            idleHint.ForeColor = Ui.Muted;
            c.Controls.Add(idleHint);

            Label onde = MakeLabel(T.DataPathLabel + "  " + Paths.DataDir, 16, 356, Ui.FontSmall);
            onde.Size = new Size(560, 30);
            onde.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            onde.TextAlign = ContentAlignment.MiddleLeft;
            onde.ForeColor = Ui.Faint;
            c.Controls.Add(onde);

            FlatBtn abrir = new FlatBtn();
            abrir.Text = T.OpenFolder;
            abrir.SetBounds(600, 354, 140, 32);
            abrir.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            abrir.Click += delegate
            {
                try { System.Diagnostics.Process.Start("explorer.exe", Paths.DataDir); }
                catch (Exception ex) { Log.Error("abrir pasta de dados", ex); }
            };
            c.Controls.Add(abrir);

            // Cartao proprio para a fonte de FPS: e a unica leitura do aplicativo
            // que depende de um programa de terceiro, e sem um lugar que diga o
            // estado, "escolhi FPS e nao aparece nada" nao tem onde ser
            // respondido.
            Card fps = new Card();
            fps.Title = T.FramesCard;
            fps.SetBounds(0, 414, 756, 172);
            page.Controls.Add(fps);

            _rtssEstado = MakeLabel("", 16, 48, Ui.FontMed);
            _rtssEstado.Size = new Size(720, 24);
            fps.Controls.Add(_rtssEstado);

            Label rtssNota = MakeLabel(T.RtssNote, 16, 76, Ui.FontSmall);
            rtssNota.Size = new Size(720, 48);
            rtssNota.ForeColor = Ui.Muted;
            fps.Controls.Add(rtssNota);

            _btRtss = new FlatBtn();
            _btRtss.Text = T.InstallRtss;
            _btRtss.Primary = true;
            _btRtss.SetBounds(16, 128, 180, 32);
            _btRtss.Click += delegate { InstalarRtss(); };
            fps.Controls.Add(_btRtss);

            FlatBtn rtssPagina = new FlatBtn();
            rtssPagina.Text = T.RtssPage;
            rtssPagina.SetBounds(202, 128, 180, 32);
            rtssPagina.Click += delegate { AbrirNoNavegador(Rtss.Site); };
            fps.Controls.Add(rtssPagina);

            FlatBtn rtssConferir = new FlatBtn();
            rtssConferir.Text = T.Recheck;
            rtssConferir.SetBounds(388, 128, 160, 32);
            rtssConferir.Click += delegate { AtualizarRtss(); };
            fps.Controls.Add(rtssConferir);

            AtualizarRtss();
            AtualizarOcioso();
            return page;
        }

        private Label _rtssEstado;
        private FlatBtn _btRtss;

        private void AtualizarRtss()
        {
            if (_rtssEstado == null) return;

            _lastRtssCheckUtc = DateTime.UtcNow;
            bool ok = Rtss.Presente();
            _rtssEstado.Text = ok ? T.RtssActive : T.RtssAbsent;
            _rtssEstado.ForeColor = ok ? Ui.Accent : Ui.Warn;

            // Instalado, nao ha o que instalar. Sem winget, o caminho e a pagina.
            if (_btRtss != null) _btRtss.Enabled = !ok && Rtss.Winget() != null;
        }

        /// <summary>
        /// Instala o RTSS pelo winget, numa janela visivel.
        ///
        /// Visivel de proposito, e com o comando a mostra: e software de outra
        /// gente entrando na maquina a pedido de um clique nosso, e o minimo e
        /// que se veja o que foi executado e o que respondeu. Nada acontece sem
        /// o clique, e o UAC ainda pergunta depois.
        /// </summary>
        private void InstalarRtss()
        {
            string winget = Rtss.Winget();
            if (winget == null)
            {
                _rtssEstado.Text = T.RtssNoWinget;
                _rtssEstado.ForeColor = Ui.Warn;
                return;
            }

            try
            {
                // "/s /k" e a forma que aguenta duas chamadas com caminho entre
                // aspas: com /s o cmd tira a primeira e a ultima aspa e leva o
                // resto ao pe da letra. Sem isso, a regra de aspas do /k embaralha
                // a linha assim que aparece a segunda.
                // Runtime, RTSS e por fim o nosso proprio executavel ligando o
                // "iniciar com o Windows" do RTSS. Encadeado aqui, e nao feito
                // em codigo depois: o winget roda numa janela a parte, e daqui
                // nao ha como saber quando ele terminou. O "&" do cmd sabe.
                string linha = Winget(winget, Rtss.PacoteRuntime) + " & " +
                               Winget(winget, Rtss.PacoteWinget) + " & " +
                               "\"" + Application.ExecutablePath + "\" " + Rtss.ArgConfigurar;

                System.Diagnostics.ProcessStartInfo psi =
                    new System.Diagnostics.ProcessStartInfo("cmd.exe", "/s /k \"" + linha + "\"");
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);

                _rtssEstado.Text = T.RtssInstalling;
                _rtssEstado.ForeColor = Ui.Muted;
            }
            catch (Exception ex)
            {
                Log.Error("instalacao do RTSS pelo winget", ex);
                AbrirNoNavegador(Rtss.Site);
            }
        }

        private static string Winget(string exe, string pacote)
        {
            return "\"" + exe + "\" install --id " + pacote +
                   " -e --source winget --accept-source-agreements";
        }

        private static void AbrirNoNavegador(string url)
        {
            try { System.Diagnostics.Process.Start(url); }
            catch (Exception ex) { Log.Error("abrir " + url, ex); }
        }

        // ---------------- pagina: Sobre ----------------

        private readonly List<MetricCard> _cards = new List<MetricCard>();
        private readonly List<string> _cardIds = new List<string>();
        private readonly List<int> _cardTamanhos = new List<int>();

        private FlatBtn _btAddMetrica, _btPadraoMetrica, _btPerfisMetrica;
        private Segmented _segJanela;
        private Label _lbDicaMetricas, _lbSemMetricas;
        private bool _arranjando = false;

        /// <summary>
        /// Grade de cartoes com as leituras principais da maquina.
        ///
        /// A grade e montada uma vez, no arranque, e depois so recebe valores.
        /// Reconstruir controles a cada segundo pisca a tela inteira e joga fora
        /// o historico de cada cartao, que e justamente o que da sentido a ela.
        /// </summary>
        private Pagina _pgMetricas;

        private Control BuildPageMetricas()
        {
            _pgMetricas = new Pagina();
            // fundo opaco vem da propria Pagina

            // Rolagem so na vertical - e com a barra propria, que nao precisa da
            // ginastica de desligar e religar o AutoScroll para nao trazer a
            // horizontal junto: ela simplesmente nao existe.
            _pgMetricas.RolarNaVertical();

            // A grade se refaz com a janela: as larguras saem da area
            // disponivel, e nao de um numero fixo que so serve para o tamanho
            // em que foi escrito.
            _pgMetricas.Resize += delegate { ArranjarMetricas(); };

            // Primeira abertura: monta uma selecao automatica e grava. A partir
            // dai a lista e do usuario, inclusive vazia - por isso o marcador
            // separado, e nao "lista vazia significa recomecar".
            if (!_cfg.MetricsChosen) SelecaoPadrao();

            MontarMetricas();
            return _pgMetricas;
        }

        /// <summary>
        /// Repoe o conjunto basico: uma leitura de cada grandeza por peca.
        ///
        /// Roda sozinha na primeira abertura e fica disponivel num botao. O
        /// botao nao e conveniencia: quem ja abriu o aplicativo antes tem
        /// "escolhido" gravado, e sem ele uma correcao na selecao automatica -
        /// como a temperatura que faltava - nunca chegaria a quem ja usa.
        /// </summary>
        private void SelecaoPadrao()
        {
            AplicarConjunto(null);
        }

        /// <summary>
        /// O menu de conjuntos, aberto sob o botao.
        ///
        /// Menu, e nao quatro botoes na fileira: aplicar um conjunto e um ato
        /// raro - dia da instalacao, e de vez em quando - e quatro alvos
        /// permanentes disputando a barra com "Adicionar metrica" e a escolha da
        /// janela dariam a esse ato o peso de uma acao cotidiana.
        /// </summary>
        private void MenuDeConjuntos()
        {
            // ContextMenu, e nao ContextMenuStrip: e o mesmo tipo que o menu da
            // bandeja ja usa. Um menu nativo nao aceita o tema desenhado do
            // aplicativo, mas dois menus com aparencias diferentes no mesmo
            // programa e pior do que um so, coerente com o sistema.
            ContextMenu m = new ContextMenu();

            foreach (MetricPicker.Conjunto c in MetricPicker.Conjuntos)
            {
                MetricPicker.Conjunto alvo = c;   // copia local para o delegate
                m.MenuItems.Add(new MenuItem(c.Nome, delegate { AplicarConjunto(alvo); }));
            }

            m.Show(_btPadraoMetrica, new Point(0, _btPadraoMetrica.Height));
        }

        /// <summary>
        /// Perfis de métricas ficam num menu hierárquico: a primeira ação cria
        /// um novo e cada perfil existente concentra aplicar/atualizar/renomear/
        /// excluir sem ocupar permanentemente a barra da grade.
        /// </summary>
        private void MenuDePerfisDeMetricas()
        {
            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add(new MenuItem(T.SaveMetricProfile,
                delegate { SalvarPerfilDeMetricas(); }));

            if (_cfg.MetricProfiles.Count > 0) menu.MenuItems.Add("-");
            foreach (MetricProfile item in _cfg.MetricProfiles)
            {
                MetricProfile profile = item;
                MenuItem group = new MenuItem(profile.Name);
                group.MenuItems.Add(new MenuItem(T.Apply,
                    delegate { AplicarPerfilDeMetricas(profile); }));
                group.MenuItems.Add(new MenuItem(T.UpdateMetricProfile,
                    delegate { AtualizarPerfilDeMetricas(profile); }));
                group.MenuItems.Add(new MenuItem(T.RenameMetricProfile,
                    delegate { RenomearPerfilDeMetricas(profile); }));
                group.MenuItems.Add("-");
                group.MenuItems.Add(new MenuItem(T.DeleteMetricProfile,
                    delegate { ExcluirPerfilDeMetricas(profile); }));
                menu.MenuItems.Add(group);
            }
            menu.Show(_btPerfisMetrica, new Point(0, _btPerfisMetrica.Height));
        }

        private MetricProfile CapturarPerfilDeMetricas(string nome)
        {
            MetricProfile profile = new MetricProfile();
            profile.Name = nome;
            profile.Range = MetricHistory.JanelaValida(_cfg.MetricRange);
            profile.Ids.AddRange(_cfg.MetricIds);
            profile.Sizes.AddRange(_cfg.MetricSizes);
            return profile;
        }

        private static void CopiarPerfilDeMetricas(MetricProfile source, MetricProfile target)
        {
            target.Range = source.Range;
            target.Ids.Clear(); target.Ids.AddRange(source.Ids);
            target.Sizes.Clear(); target.Sizes.AddRange(source.Sizes);
        }

        private void SalvarPerfilDeMetricas()
        {
            string nome = Prompt(T.MetricProfileName,
                T.DefaultMetricProfileName(_cfg.MetricProfiles.Count + 1));
            if (string.IsNullOrWhiteSpace(nome)) return;
            if (_cfg.MetricProfileNameExists(nome)) { Warn(T.NameTaken); return; }

            MetricProfile profile = CapturarPerfilDeMetricas(nome);
            _cfg.MetricProfiles.Add(profile);
            if (!GravarMetricas()) { _cfg.MetricProfiles.Remove(profile); return; }
            Aviso(T.MetricProfileSaved(profile.Name));
        }

        private void AplicarPerfilDeMetricas(MetricProfile profile)
        {
            if (profile == null) return;
            List<string> oldIds = new List<string>(_cfg.MetricIds);
            List<int> oldSizes = new List<int>(_cfg.MetricSizes);
            int oldRange = _cfg.MetricRange;

            _cfg.MetricIds.Clear(); _cfg.MetricIds.AddRange(profile.Ids);
            _cfg.MetricSizes.Clear(); _cfg.MetricSizes.AddRange(profile.Sizes);
            _cfg.MetricRange = MetricHistory.JanelaValida(profile.Range);
            _cfg.MetricsChosen = true;
            if (!GravarMetricas())
            {
                _cfg.MetricIds.Clear(); _cfg.MetricIds.AddRange(oldIds);
                _cfg.MetricSizes.Clear(); _cfg.MetricSizes.AddRange(oldSizes);
                _cfg.MetricRange = oldRange;
                return;
            }
            MontarMetricas();
            if (Applied != null) Applied(this, EventArgs.Empty);
            Aviso(T.MetricProfileApplied(profile.Name));
        }

        private void AtualizarPerfilDeMetricas(MetricProfile profile)
        {
            if (profile == null) return;
            MetricProfile backup = profile.Clone();
            CopiarPerfilDeMetricas(CapturarPerfilDeMetricas(profile.Name), profile);
            if (!GravarMetricas()) CopiarPerfilDeMetricas(backup, profile);
            else Aviso(T.MetricProfileSaved(profile.Name));
        }

        private void RenomearPerfilDeMetricas(MetricProfile profile)
        {
            if (profile == null) return;
            string nome = Prompt(T.MetricProfileName, profile.Name);
            if (string.IsNullOrWhiteSpace(nome) || nome == profile.Name) return;
            MetricProfile existing = _cfg.MetricProfileByName(nome);
            if (existing != null && !ReferenceEquals(existing, profile)) { Warn(T.NameTaken); return; }
            string old = profile.Name;
            profile.Name = nome;
            if (!GravarMetricas()) profile.Name = old;
        }

        private void ExcluirPerfilDeMetricas(MetricProfile profile)
        {
            if (profile == null) return;
            if (MessageBox.Show(T.DeleteMetricProfileQ(profile.Name), T.AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int index = _cfg.MetricProfiles.IndexOf(profile);
            if (index < 0) return;
            _cfg.MetricProfiles.RemoveAt(index);
            if (!GravarMetricas()) _cfg.MetricProfiles.Insert(index, profile);
        }

        /// <summary>
        /// Troca a grade pelo conjunto escolhido. Nulo significa o automatico.
        ///
        /// Conjunto que nao acha nada NAO limpa a grade: numa maquina sem RTSS o
        /// conjunto de jogos traria duas leituras, e numa sem sensor de ventoinha
        /// o silencioso traria zero - e trocar o que a pessoa montou por uma tela
        /// vazia e destruir trabalho para nao entregar nada.
        /// </summary>
        private void AplicarConjunto(MetricPicker.Conjunto c)
        {
            List<SensorEntry> escolhidos = MetricPicker.Montar(c, _sensors);
            if (escolhidos.Count == 0) { Aviso(T.PresetEmpty); return; }

            _cfg.MetricIds.Clear();
            _cfg.MetricSizes.Clear();
            foreach (SensorEntry s in escolhidos)
            {
                _cfg.MetricIds.Add(s.Id);
                _cfg.MetricSizes.Add(0);
            }
            _cfg.MetricsChosen = true;
            if (!GravarMetricas()) return;
            MontarMetricas();

            // A grade nova precisa de historico: sem avisar quem grava, os
            // cartoes recem-criados ficariam sem curva ate alguem reabrir o
            // aplicativo.
            if (Applied != null) Applied(this, EventArgs.Empty);

            if (c != null) Aviso(T.PresetApplied(c.Nome, escolhidos.Count));
        }

        private bool GravarMetricas()
        {
            try
            {
                string erro;
                bool salvo = _session.TrySavePreferences(delegate(Config draft, Config target)
                {
                    target.MetricIds.Clear(); target.MetricIds.AddRange(draft.MetricIds);
                    target.MetricSizes.Clear(); target.MetricSizes.AddRange(draft.MetricSizes);
                    target.MetricsChosen = draft.MetricsChosen;
                    target.MetricRange = draft.MetricRange;
                    target.MetricProfiles.Clear();
                    foreach (MetricProfile profile in draft.MetricProfiles)
                        target.MetricProfiles.Add(profile.Clone());
                }, out erro);
                if (!salvo) { _dirty = true; Aviso(T.SaveFailed(erro)); }
                return salvo;
            }
            catch (Exception ex) { Log.Error("gravar cartoes de metricas", ex); return false; }
        }

        // ---------------- arrastar cartoes ----------------

        private MetricCard _arrastado;
        private int _origemDoArraste = -1;
        private int _destinoDoArraste = -1;
        private Point _pegadaNoCartao;

        /// <summary>
        /// Segue o cartao arrastado ate a soltura.
        ///
        /// O cartao sai do fluxo e passa a acompanhar o ponteiro; a grade se
        /// reorganiza ao vivo em volta dele, mostrando onde ele vai cair. Um
        /// indicador fino no lugar de destino resolveria menos: a pergunta que se
        /// faz arrastando nao e "onde ele entra", e sim "como a grade fica" - e
        /// isso so se responde mostrando a grade como ficaria.
        ///
        /// Capture no proprio formulario, e nao no cartao: solto sobre outro
        /// controle, o cartao pararia de receber o movimento no meio do gesto.
        /// </summary>
        private void ComecarArrasteDeCartao(MetricCard c)
        {
            int i = _cards.IndexOf(c);
            if (i < 0) return;

            _arrastado = c;
            _origemDoArraste = i;
            _destinoDoArraste = i;
            _pegadaNoCartao = c.Pegada;

            c.BringToFront();
            Cursor = Cursors.SizeAll;
            Capture = true;

            _pgMetricas.MouseMove += new MouseEventHandler(OnArrastando);
            _pgMetricas.MouseUp += new MouseEventHandler(OnSoltouCartao);
            MouseMove += new MouseEventHandler(OnArrastando);
            MouseUp += new MouseEventHandler(OnSoltouCartao);
        }

        private void OnArrastando(object sender, MouseEventArgs e)
        {
            if (_arrastado == null) return;

            Point p = _pgMetricas.PointToClient(Cursor.Position);
            _arrastado.Location = new Point(p.X - _pegadaNoCartao.X, p.Y - _pegadaNoCartao.Y);

            int alvo = IndiceSob(p);
            if (alvo >= 0 && alvo != _destinoDoArraste)
            {
                _destinoDoArraste = alvo;
                ReordenarPara(alvo);
            }
        }

        /// <summary>
        /// Qual posicao da grade esta sob o ponteiro.
        ///
        /// Mede pelo CENTRO de cada cartao, e nao pela area dele: com areas, o
        /// cartao arrastado - que esta por cima e no lugar do ponteiro - seria
        /// sempre o alvo de si mesmo, e nada trocaria de lugar.
        /// </summary>
        private int IndiceSob(Point p)
        {
            int melhor = -1;
            long menor = long.MaxValue;

            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] == _arrastado) continue;
                Rectangle r = _cards[i].Bounds;
                long dx = p.X - (r.Left + r.Width / 2);
                long dy = p.Y - (r.Top + r.Height / 2);
                long d = dx * dx + dy * dy;
                if (d < menor) { menor = d; melhor = i; }
            }

            if (melhor < 0) return -1;

            // So troca quando o ponteiro passa do centro do vizinho, senao a
            // grade ficaria oscilando entre duas ordens a cada pixel.
            Rectangle rv = _cards[melhor].Bounds;
            if (!rv.Contains(p)) return -1;
            return melhor;
        }

        /// <summary>Move o cartao arrastado para a posicao alvo e rearranja.</summary>
        private void ReordenarPara(int destino)
        {
            int atual = _cards.IndexOf(_arrastado);
            if (atual < 0 || destino < 0 || destino >= _cards.Count || destino == atual) return;

            MetricCard c = _cards[atual];
            string id = _cardIds[atual];
            int tam = _cardTamanhos[atual];

            _cards.RemoveAt(atual); _cardIds.RemoveAt(atual); _cardTamanhos.RemoveAt(atual);
            _cards.Insert(destino, c); _cardIds.Insert(destino, id); _cardTamanhos.Insert(destino, tam);

            ArranjarMetricas();
            _arrastado.BringToFront();
        }

        private void OnSoltouCartao(object sender, MouseEventArgs e)
        {
            if (_arrastado == null) return;

            _pgMetricas.MouseMove -= new MouseEventHandler(OnArrastando);
            _pgMetricas.MouseUp -= new MouseEventHandler(OnSoltouCartao);
            MouseMove -= new MouseEventHandler(OnArrastando);
            MouseUp -= new MouseEventHandler(OnSoltouCartao);

            Capture = false;
            Cursor = Cursors.Default;

            bool mudou = _destinoDoArraste != _origemDoArraste;
            _arrastado = null;
            _origemDoArraste = -1;
            _destinoDoArraste = -1;

            ArranjarMetricas();

            // Grava so quando a ordem mudou de fato: um arraste que voltou ao
            // lugar nao e uma edicao, e marcar a configuracao como suja obrigaria
            // a pessoa a salvar algo que ela nao fez.
            if (!mudou) return;

            _cfg.MetricIds.Clear();
            _cfg.MetricSizes.Clear();
            for (int i = 0; i < _cardIds.Count; i++)
            {
                _cfg.MetricIds.Add(_cardIds[i]);
                _cfg.MetricSizes.Add(_cardTamanhos[i]);
            }
            GravarMetricas();
        }

        /// <summary>
        /// (Re)monta a grade a partir da lista gravada.
        ///
        /// Remontar zera o historico dos cartoes, entao so acontece quando a
        /// composicao muda - adicionar, remover, mover. O ciclo de um segundo
        /// nunca passa por aqui: ele so empurra valores.
        /// </summary>
        private void MontarMetricas()
        {
            _pgMetricas.SuspendLayout();
            List<Control> velhos = new List<Control>();
            foreach (Control c in _pgMetricas.Controls) velhos.Add(c);
            foreach (Control c in velhos) { _pgMetricas.Controls.Remove(c); c.Dispose(); }
            _cards.Clear();
            _cardIds.Clear();
            _cardTamanhos.Clear();
            _lbSemMetricas = null;

            _btAddMetrica = new FlatBtn();
            _btAddMetrica.Text = T.AddMetric;
            _btAddMetrica.Primary = true;
            _btAddMetrica.Click += delegate { AdicionarMetrica(); };
            _pgMetricas.Controls.Add(_btAddMetrica);

            _btPadraoMetrica = new FlatBtn();
            _btPadraoMetrica.Text = T.DefaultMetrics;
            _btPadraoMetrica.Click += delegate { MenuDeConjuntos(); };
            _pgMetricas.Controls.Add(_btPadraoMetrica);

            _btPerfisMetrica = new FlatBtn();
            _btPerfisMetrica.Text = T.MetricProfiles;
            _btPerfisMetrica.Click += delegate { MenuDePerfisDeMetricas(); };
            _pgMetricas.Controls.Add(_btPerfisMetrica);

            // Janela de tempo dos graficos. Vale para a grade inteira: comparar
            // dois cartoes em escalas de tempo diferentes nao diz nada, e e
            // exatamente o que uma escolha por cartao permitiria fazer sem
            // perceber.
            _segJanela = new Segmented();
            string[] nomes = new string[MetricHistory.Janelas.Length];
            for (int i = 0; i < nomes.Length; i++)
                nomes[i] = MetricHistory.NomeDaJanela(MetricHistory.Janelas[i]);
            _segJanela.SetItems(nomes);
            _segJanela.SelectedIndex = IndiceDaJanela();
            _segJanela.SelectedIndexChanged += delegate { TrocarJanela(); };
            _pgMetricas.Controls.Add(_segJanela);

            _lbDicaMetricas = MakeLabel(T.MetricsHint, 0, 0, Ui.FontSmall);
            _lbDicaMetricas.ForeColor = Ui.Faint;
            _pgMetricas.Controls.Add(_lbDicaMetricas);

            if (_cfg.MetricIds.Count == 0)
            {
                _lbSemMetricas = MakeLabel(T.NoMetrics, 4, 52, Ui.FontBase);
                _lbSemMetricas.ForeColor = Ui.Muted;
                _pgMetricas.Controls.Add(_lbSemMetricas);
            }

            int janela = MetricHistory.JanelaValida(_cfg.MetricRange);
            for (int i = 0; i < _cfg.MetricIds.Count; i++)
            {
                string id = _cfg.MetricIds[i];
                SensorEntry s = Achar(id);
                if (s == null) continue;   // sensor sumiu entre sessoes

                MetricCard c = new MetricCard();
                c.SensorId = s.Id;
                c.Titulo = MetricPicker.Rotulo(s);
                c.Sub = MetricPicker.Rodape(s);
                c.Unidade = s.Unit;
                c.Janela = janela;
                float? at, pe;
                MetricPicker.Faixas(s.Unit, out at, out pe);
                c.Atencao = at; c.Perigo = pe;

                string alvo = s.Id;
                c.Remover += delegate { MexerNaMetrica(alvo, 0); };
                c.MoverEsquerda += delegate { MexerNaMetrica(alvo, -1); };
                c.MoverDireita += delegate { MexerNaMetrica(alvo, +1); };
                c.TrocarTamanho += delegate { RedimensionarMetrica(alvo); };

                // Copia local: o delegate guarda a variavel do laco, e nao o
                // valor dela no instante em que foi escrito.
                MetricCard esteCartao = c;
                c.Arrastar += delegate { ComecarArrasteDeCartao(esteCartao); };

                _pgMetricas.Controls.Add(c);
                _cards.Add(c);
                _cardIds.Add(s.Id);
                _cardTamanhos.Add(_cfg.MetricSize(i));
            }

            // Quem alimenta a serie e a thread de leitura, que roda com a janela
            // fechada: e ela que precisa saber o que acompanhar.
            MetricHistory.Seguir(_cfg.MetricIds);

            _pgMetricas.ResumeLayout();
            ArranjarMetricas();
        }

        /// <summary>
        /// Posiciona a barra e a grade na largura que a pagina tem agora.
        ///
        /// Empacotamento por fluxo: cada cartao ocupa 1, 2 ou 4 colunas e entra
        /// na linha corrente se couber; senao a linha fecha e ele comeca a
        /// proxima. A altura da linha e a do cartao mais alto dela, que e o que
        /// evita sobreposicao ao misturar tamanhos.
        ///
        /// O numero de colunas cresce com a largura, em multiplos de quatro: sem
        /// isso, uma janela maximizada em 1920 px daria quatro cartoes de 460 px
        /// - um numero pequeno perdido em cada tapete de cor.
        /// </summary>
        private void ArranjarMetricas()
        {
            if (_pgMetricas == null || _arranjando) return;

            // Mesmo motivo do Elastico: escondida, ela ainda recebe Resize a cada
            // quadro da animacao da lateral, e reposicionar dezenas de cartoes
            // que ninguem ve custa o mesmo que reposicionar os que se ve.
            if (!_pgMetricas.Visible) return;
            _arranjando = true;
            _pgMetricas.SuspendLayout();
            try
            {
                const int Esp = 12;

                // A largura da barra de rolagem sai da conta SEMPRE, apareca ela
                // ou nao. Medir a area de cliente faria a grade encolher quando
                // a barra surge e alargar quando ela some, e cada troca dispara
                // um novo arranjo - dois estados que se chamam em circulo.
                int disp = _pgMetricas.Width - Pagina.LarguraDaBarra - 4;
                if (disp < 320) disp = 320;

                int porLinha = disp / 200;
                if (porLinha < 4) porLinha = 4;
                if (porLinha > 12) porLinha = 12;
                porLinha -= porLinha % 4;

                int larg = (disp - (porLinha - 1) * Esp) / porLinha;
                if (larg < 140) larg = 140;

                int y = 0;
                if (_btAddMetrica != null) _btAddMetrica.SetBounds(2, y, 150, 32);
                if (_btPadraoMetrica != null) _btPadraoMetrica.SetBounds(158, y, 150, 32);
                if (_btPerfisMetrica != null) _btPerfisMetrica.SetBounds(314, y, 150, 32);
                if (_segJanela != null) _segJanela.SetBounds(470, y, 186, 32);

                if (_lbDicaMetricas != null)
                {
                    const int x = 668;
                    int w = disp - x;
                    // Some quando nao cabe, em vez de ser cortada: uma dica pela
                    // metade nao ensina nada e ainda ocupa a fileira. Recolhida,
                    // volta para dentro da area - um controle fora dela, mesmo
                    // invisivel, e um convite a barra horizontal de volta.
                    bool cabe = w >= 150;
                    _lbDicaMetricas.Visible = cabe;
                    if (cabe) _lbDicaMetricas.SetBounds(x, y + 4, w, 30);
                    else _lbDicaMetricas.SetBounds(2, y + 4, 1, 1);
                }
                y += 44;

                if (_lbSemMetricas != null) _lbSemMetricas.SetBounds(4, y + 8, disp - 8, 40);

                int col = 0, alturaDaLinha = 0;
                for (int i = 0; i < _cards.Count; i++)
                {
                    int tam = i < _cardTamanhos.Count ? _cardTamanhos[i] : 0;
                    int cols = MetricPicker.Colunas(tam);
                    int alt = MetricPicker.Altura(tam);

                    if (col + cols > porLinha && col > 0)
                    {
                        y += alturaDaLinha + Esp;
                        col = 0; alturaDaLinha = 0;
                    }

                    // O cartao na mao consome o lugar dele na conta, mas NAO e
                    // reposicionado: quem manda na posicao dele e o ponteiro.
                    // Sem esta excecao ele voltaria para a grade a cada
                    // movimento, e o arraste viraria um piscar.
                    if (_cards[i] != _arrastado)
                        _cards[i].SetBounds(2 + col * (larg + Esp), y,
                                            cols * larg + (cols - 1) * Esp, alt);
                    else
                        _cards[i].Size = new Size(cols * larg + (cols - 1) * Esp, alt);

                    col += cols;
                    if (alt > alturaDaLinha) alturaDaLinha = alt;
                }
            }
            finally
            {
                _pgMetricas.ResumeLayout();
                _arranjando = false;
            }

            _pgMetricas.Sincronizar();
        }

        private int IndiceDaJanela()
        {
            int j = MetricHistory.JanelaValida(_cfg.MetricRange);
            for (int i = 0; i < MetricHistory.Janelas.Length; i++)
                if (MetricHistory.Janelas[i] == j) return i;
            return 0;
        }

        /// <summary>
        /// Troca a janela de tempo sem remontar a grade.
        ///
        /// A serie nao esta nos cartoes, entao mudar a escala e so redesenhar -
        /// nada do que foi registrado se perde ao alternar entre dez minutos e
        /// seis horas.
        /// </summary>
        private void TrocarJanela()
        {
            if (_segJanela == null) return;
            int i = _segJanela.SelectedIndex;
            if (i < 0 || i >= MetricHistory.Janelas.Length) return;

            _cfg.MetricRange = MetricHistory.Janelas[i];
            GravarMetricas();
            foreach (MetricCard c in _cards) { c.Janela = _cfg.MetricRange; c.Invalidate(); }
            // Os destaques da tela de bordo desenham a mesma serie e tem de
            // mostrar a mesma janela: dois graficos do mesmo sensor em escalas de
            // tempo diferentes, na mesma janela, e um convite a comparar o que
            // nao se compara.
            foreach (MetricCard c in _vgTiles) { c.Janela = _cfg.MetricRange; c.Invalidate(); }
        }

        private void AdicionarMetrica()
        {
            using (SensorDialog d = new SensorDialog(Clone(_sensors), null, T.AddMetric))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string id = d.SelectedId;
                if (string.IsNullOrEmpty(id) || _cfg.MetricIds.Contains(id)) return;
                _cfg.MetricIds.Add(id);
                _cfg.MetricSizes.Add(0);
                GravarMetricas();
                MontarMetricas();
            }
        }

        /// <summary>Remove (passo 0) ou desloca o cartao uma posicao.</summary>
        private void MexerNaMetrica(string id, int passo)
        {
            int i = _cfg.MetricIds.IndexOf(id);
            if (i < 0) return;

            if (passo == 0)
            {
                _cfg.MetricIds.RemoveAt(i);
                if (i < _cfg.MetricSizes.Count) _cfg.MetricSizes.RemoveAt(i);
            }
            else
            {
                int j = i + passo;
                if (j < 0 || j >= _cfg.MetricIds.Count) return;
                Trocar(_cfg.MetricIds, i, j);
                if (j < _cfg.MetricSizes.Count) Trocar(_cfg.MetricSizes, i, j);
            }
            GravarMetricas();
            MontarMetricas();
        }

        private static void Trocar<T>(List<T> lista, int i, int j)
        {
            T t = lista[i]; lista[i] = lista[j]; lista[j] = t;
        }

        /// <summary>
        /// Percorre pequeno, medio e grande e volta ao pequeno.
        ///
        /// Um botao ciclico em vez de tres opcoes: sao tres estados e a ordem e
        /// obvia, entao um menu para escolher entre eles gastaria dois cliques
        /// para o que um resolve.
        /// </summary>
        private void RedimensionarMetrica(string id)
        {
            int i = _cfg.MetricIds.IndexOf(id);
            if (i < 0) return;
            while (_cfg.MetricSizes.Count <= i) _cfg.MetricSizes.Add(0);
            _cfg.MetricSizes[i] = (_cfg.MetricSizes[i] + 1) % 3;
            GravarMetricas();
            MontarMetricas();
        }

        private void AtualizarMetricas(Dictionary<string, float> snap)
        {
            if (snap == null) return;
            for (int i = 0; i < _cards.Count; i++)
            {
                // O rodape das leituras de quadro segue o jogo. Montado uma vez,
                // ele guardava o texto verdadeiro no instante em que a grade
                // nasceu - e um cartao chegava a marcar 757 FPS com "nenhum jogo
                // em execucao" escrito logo abaixo.
                if (_cardIds[i].StartsWith(Rtss.Prefixo, StringComparison.Ordinal))
                {
                    string rodape = MetricPicker.RodapeJogos();
                    if (_cards[i].Sub != rodape)
                    {
                        _cards[i].Sub = rodape;
                        _cards[i].Invalidate();
                    }
                }

                float v;
                _cards[i].Push(snap.TryGetValue(_cardIds[i], out v) ? (float?)v : null);
            }
        }

        private Control BuildPageSobre()
        {
            Panel page = new Pagina();
            // fundo opaco vem da propria Pagina

            // Continua rolando: mesmo sem as preferencias, identidade, fontes,
            // creditos e isencao passam dos 818 px, e cortar a isencao para
            // caber seria trocar o que importa pelo que cabe.
            ((Pagina)page).RolarNaVertical();

            Card c = new Card();
            c.Title = T.NavAbout;
            c.SetBounds(0, 0, 736, 250);
            page.Controls.Add(c);

            Label t = MakeLabel(T.AppName, 16, 50, Ui.FontTitle);
            t.Size = new Size(400, 30);
            c.Controls.Add(t);

            Label d = MakeLabel(T.AboutTagline, 16, 84, Ui.FontSmall);
            d.Size = new Size(700, 34);
            d.ForeColor = Ui.Muted;
            c.Controls.Add(d);

            // Quais fontes responderam, contado da propria lista de sensores.
            // Antes, perder o HWiNFO so aparecia no log: o aplicativo seguia
            // sem temperatura nem potencia da CPU e nada em tela dizia por que.
            c.Controls.Add(MakeLabel(T.SensorSources, 16, 134, Ui.FontMed));

            // Altura fixa para as tres linhas do pior caso: com o aviso, a
            // contagem passa a dividir espaco com duas linhas de explicacao, e
            // dimensionar so para o caso bom cortava justamente o texto que
            // existe para ser lido.
            Label fontes = MakeLabel(TextoDasFontes(), 16, 154, Ui.FontSmall);
            fontes.Size = new Size(716, 52);
            fontes.ForeColor = SemHwInfo() ? Ui.Warn : Ui.Muted;
            c.Controls.Add(fontes);

            // Qual painel respondeu. So o Temp 6 Pro Black foi testado; nos
            // outros modelos do fabricante o aplicativo comanda assim mesmo, e
            // esta linha e o que a pessoa copia para relatar que funcionou.
            Label painel = MakeLabel(TextoDoPainel(), 16, 212, Ui.FontSmall);
            painel.Size = new Size(716, 20);
            painel.ForeColor = Ui.Muted;
            c.Controls.Add(painel);

            Card cr = new Card();
            cr.Title = T.ProjectAndCredits;
            cr.SetBounds(0, 262, 736, 140);
            page.Controls.Add(cr);

            Label autor = MakeLabel(T.CreatedBy, 16, 48, Ui.FontMed);
            autor.Size = new Size(400, 22);
            cr.Controls.Add(autor);

            Label repo = MakeLabel(Repositorio, 16, 70, Ui.FontSmall);
            repo.Size = new Size(500, 20);
            repo.ForeColor = Ui.Muted;
            cr.Controls.Add(repo);

            FlatBtn git = new FlatBtn();
            git.Text = T.OpenOnGitHub;
            git.SetBounds(16, 96, 150, 32);
            git.Click += delegate { Abrir(Repositorio); };
            cr.Controls.Add(git);

            // duas linhas, e nao tres: uma terceira invade o rotulo do
            // repositorio, que fica logo acima e atravessa esta coluna
            Label libs = MakeLabel(T.LibsNote, 190, 92, Ui.FontSmall);
            libs.Size = new Size(552, 40);
            libs.ForeColor = Ui.Muted;
            cr.Controls.Add(libs);

            Card cd = new Card();
            cd.Title = T.DisclaimerTitle;
            cd.SetBounds(0, 414, 736, 278);
            page.Controls.Add(cd);

            Label disc = MakeLabel(T.Disclaimer, 16, 46, Ui.FontSmall);
            disc.Size = new Size(724, 224);
            disc.ForeColor = Ui.Muted;
            cd.Controls.Add(disc);

            return page;
        }

        /// <summary>
        /// Troca de idioma: grava a escolha e reabre a janela.
        ///
        /// Reetiquetar tudo em pe exigiria que cada controle guardasse a chave
        /// do seu texto e soubesse se retraduzir - dezenas de pontos, e o que
        /// escapasse ficaria no idioma antigo sem ninguem notar. Reconstruir
        /// nao deixa canto por traduzir. As edicoes pendentes vao para o perfil
        /// antes, entao nada se perde.
        /// </summary>
        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            string alvo = _lang.SelectedIndex == 1 ? T.EnUs : T.PtBr;
            if (alvo == T.Language) return;

            SaveToProfile();
            string anterior = _cfg.Language;
            _cfg.Language = alvo;
            string erro;
            if (!_session.TrySaveAll(out erro))
            {
                _cfg.Language = anterior;
                _loading = true;
                _lang.SelectedIndex = T.Pt ? 0 : 1;
                _loading = false;
                _saved = false; _dirty = true;
                Aviso(T.SaveFailed(erro));
                return;
            }
            _saved = true; _dirty = false;
            T.Language = alvo;

            DialogResult = DialogResult.Retry;   // quem abriu reabre
            Close();
        }

        private const string Repositorio = "https://github.com/Feurrado/MhiagosControl";

        /// <summary>
        /// Resumo de quem respondeu, contado pela procedencia das entradas.
        ///
        /// Sai da propria lista de sensores em vez de perguntar ao motor: e a
        /// mesma informacao, vinda de onde ela e verdade - se a fonte nao
        /// publicou nada, ela nao esta ali, independentemente do que a camada
        /// de baixo ache que abriu.
        /// </summary>
        private string TextoDasFontes()
        {
            int hw = 0, lhm = 0;
            foreach (SensorEntry s in _sensors)
                if (s.Source == "HWiNFO") hw++; else lhm++;

            string linha = T.SensorsFrom("HWiNFO", hw) + "   ·   " + T.SensorsFrom("LibreHardwareMonitor", lhm);
            return hw == 0 ? linha + "\n" + T.EngineMissing : linha;
        }

        private static string TextoDoPainel()
        {
            string id = HidPanel.UltimoIdentificado;
            if (string.IsNullOrEmpty(id)) return T.PanelNotFound;
            return T.PanelFound(id) + (id == "VID 1A2C / PID 4984" ? "" : "   ·   " + T.PanelUntested);
        }

        /// <summary>
        /// So o identificador, sem o prefixo "Painel:".
        ///
        /// Na tela de bordo o rotulo da linha ja diz "Painel", e o texto vinha
        /// com o prefixo embutido: sobrava "Painel  Painel: VID 1A2C / PID 4984",
        /// que ainda por cima nao cabia e saia cortado no meio do PID.
        /// </summary>
        private static string IdDoPainel()
        {
            string id = HidPanel.UltimoIdentificado;
            return string.IsNullOrEmpty(id) ? T.PanelNotFound : id;
        }

        private bool SemHwInfo()
        {
            foreach (SensorEntry s in _sensors) if (s.Source == "HWiNFO") return false;
            return true;
        }

        private static void Abrir(string url)
        {
            try { System.Diagnostics.Process.Start(url); }
            catch (Exception ex) { Log.Error("abrir " + url, ex); }
        }

        private void OnToggleShowAll(object sender, EventArgs e)
        {
            if (_loading) return;
            Toggle t = sender as Toggle;
            if (t == null || _data == null) return;

            bool anterior = _cfg.ShowAllSensors;
            _cfg.ShowAllSensors = t.Checked;
            string erro;
            if (!_session.TrySavePreferences(delegate(Config draft, Config target)
            {
                target.ShowAllSensors = draft.ShowAllSensors;
            }, out erro))
            {
                _cfg.ShowAllSensors = anterior;
                _loading = true; t.Checked = anterior; _loading = false;
                Aviso(T.SaveFailed(erro));
                return;
            }
            _data.SetShowAllSensors(t.Checked);

            // relista na hora: esperar a proxima abertura da janela seria
            // confuso, o interruptor pareceria nao ter efeito
            _sensors = _data.RefreshSensorList();
            _pick1.SetSensors(Clone(_sensors));
            _pick2.SetSensors(Clone(_sensors));
            _pick1.SelectedId = _current.Panel1Id;
            _pick2.SelectedId = _current.Panel2Id;
            Refresh0();
        }

        /// <summary>
        /// Liga e desliga o apagamento por ociosidade.
        ///
        /// O campo de minutos guarda o valor mesmo desligado, e por isso o
        /// desligamento grava zero em vez de zerar o campo: quem desliga e
        /// religa no minuto seguinte encontra o numero que tinha escolhido.
        /// </summary>
        private void OnToggleIdle(object sender, EventArgs e)
        {
            if (_loading) return;
            _idleMin.Enabled = _tgIdle.Checked;
            _cfg.IdleBlankMinutes = _tgIdle.Checked ? _idleMin.Value : 0;
            _dirty = true;
            AtualizarOcioso();
        }

        private void OnIdleMinutes(object sender, EventArgs e)
        {
            if (_loading) return;
            if (!_tgIdle.Checked) return;
            _cfg.IdleBlankMinutes = _idleMin.Value;
            _dirty = true;
            AtualizarOcioso();
        }

        private void AtualizarOcioso()
        {
            if (_idleNote == null) return;
            _idleNote.Text = _tgIdle.Checked ? T.MinutesIdle : T.Off;
        }

        private void OnToggleAutostart(object sender, EventArgs e)
        {
            if (_loading) return;
            bool target = _tgAutostart.Checked;
            bool ok = target ? Autostart.Enable() : Autostart.Disable();
            if (!ok)
            {
                Warn(T.AutostartFailed + Log.Path);
                _loading = true; _tgAutostart.Checked = !target; _loading = false;
            }
        }

        // ---------------- estado ----------------

        private void LoadFromProfile()
        {
            _loading = true;
            _pick1.SelectedId = _current.Panel1Id;
            _pick2.SelectedId = _current.Panel2Id;
            _unit1.SelectedIndex = _current.Fahrenheit ? 1 : 0;
            _unit2.SelectedIndex = _current.Percent ? 0 : 1;
            _loading = false;
            Refresh0();
        }

        /// <summary>Fahrenheit e a segunda pastilha do seletor de unidade.</summary>
        private bool Fahrenheit { get { return _unit1.SelectedIndex == 1; } }

        /// <summary>Porcentagem e a PRIMEIRA pastilha; a segunda acende W.</summary>
        private bool Percent { get { return _unit2.SelectedIndex == 0; } }

        private void SaveToProfile()
        {
            if (_current == null) return;
            if (_pick1.SelectedId.Length > 0) _current.Panel1Id = _pick1.SelectedId;
            if (_pick2.SelectedId.Length > 0) _current.Panel2Id = _pick2.SelectedId;
            _current.Fahrenheit = Fahrenheit;
            _current.Percent = Percent;
        }

        private void OnChanged()
        {
            if (_loading) return;
            _dirty = true;
            SaveToProfile();
            Refresh0();
        }

        /// <summary>Reflete o estado atual em todos os elementos derivados.</summary>
        private void Refresh0()
        {
            SensorEntry s1 = _pick1.Selected;
            SensorEntry s2 = _pick2.Selected;

            HighlightDivisor(_div1, _current.Divisor1);
            HighlightDivisor(_div2, _current.Divisor2);

            int d1 = Scaling.Effective(_current.Divisor1, s1);
            int d2 = Scaling.Effective(_current.Divisor2, s2);

            PanelValue v1 = Scaling.Prepare(s1, d1, Fahrenheit);
            PanelValue v2 = Scaling.Prepare(s2, d2, false);

            _preview.Value1 = v1.Value;
            _preview.Value2 = v2.Value;
            _preview.Fahrenheit = Fahrenheit;
            _preview.Percent = Percent;
            _preview.Invalidate();

            // A previa da tela de bordo e a MESMA leitura, e nao um calculo
            // paralelo: duas contas para o mesmo mostrador acabariam divergindo
            // no dia em que alguem mexesse numa e esquecesse a outra.
            if (_vgPreview != null)
            {
                _vgPreview.Value1 = v1.Value;
                _vgPreview.Value2 = v2.Value;
                _vgPreview.Fahrenheit = Fahrenheit;
                _vgPreview.Percent = Percent;
                _vgPreview.Invalidate();
            }

            if (_slot1 != null)
            {
                _slot1.Entry = s1; _slot1.Invalidate();
                _slot2.Entry = s2; _slot2.Invalidate();
            }
        }

        private void HighlightDivisor(FlatBtn[] row, int value)
        {
            if (row == null) return;
            foreach (FlatBtn b in row)
            {
                bool on = ((int)b.Tag) == value;
                if (b.Primary != on) { b.Primary = on; b.Invalidate(); }
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Control ativa = _nav != null && _nav.Selected != null ? _nav.Selected.Page : null;
                bool sensorPage = ativa == _pgVisao || ativa == _pgMetricas ||
                                  ativa == _pgPaineis;
                DateTime now = DateTime.UtcNow;
                if (!sensorPage && now - _lastSlowUiTickUtc < TimeSpan.FromMilliseconds(900))
                    return;
                if (!sensorPage) _lastSlowUiTickUtc = now;
                AtualizarPaginaAtiva(false);
            }
            catch (Exception ex) { Log.Error("atualizacao da previa", ex); }
        }

        /// <summary>
        /// Atualiza somente o que esta visivel. A janela construi todas as abas
        /// para manter a navegacao imediata, mas redesenhar pickers, graficos e
        /// checagens de RTSS atras de uma pagina escondida gastava CPU sem mudar
        /// um pixel. ShowPage tambem chama este metodo para evitar tela antiga na
        /// primeira exibicao.
        /// </summary>
        private void AtualizarPaginaAtiva(bool force)
        {
            Control ativa = _nav != null && _nav.Selected != null ? _nav.Selected.Page : null;
            bool usaSensores = ativa == _pgVisao || ativa == _pgMetricas
                            || ativa == _pgPaineis;
            Dictionary<string, float> snap = usaSensores && _data != null
                ? _data.CurrentSnapshot() : null;

            if (snap != null && ativa == _pgVisao)
                AtualizarVisaoGeral(snap);

            if (snap != null && ativa == _pgMetricas)
                AtualizarMetricas(snap);

            if (snap != null && ativa == _pgPaineis)
            {
                _pick1.UpdateValues(snap);
                _pick2.UpdateValues(snap);
                AtualizarValores(snap);
                Refresh0();
            }

            if (ativa == _pgPerfis)
            {
                if (force) AtualizarPreviaDoPerfil();
                AtualizarJogo();
            }

            if (ativa == _pgConfig && (force ||
                DateTime.UtcNow - _lastRtssCheckUtc >= TimeSpan.FromSeconds(10)))
                AtualizarRtss();
        }

        private string Prompt(string message, string initial)
        {
            using (Form f = new Form())
            {
                f.Text = T.AppName;
                f.Icon = Assets.AppIcon;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(380, 130);
                f.BackColor = Ui.Window;
                f.Font = Ui.FontBase;

                Label l = MakeLabel(message, 16, 18, Ui.FontBase);
                l.Size = new Size(348, 18);

                TextBox t = new TextBox();
                t.Text = initial;
                t.SetBounds(16, 44, 348, 24);
                t.BorderStyle = BorderStyle.FixedSingle;
                t.BackColor = Ui.SurfaceAlt; t.ForeColor = Ui.Text;

                FlatBtn ok = new FlatBtn();
                ok.Text = T.Ok; ok.Primary = true;
                ok.SetBounds(172, 86, 90, 30);
                ok.Click += delegate { f.DialogResult = DialogResult.OK; f.Close(); };

                FlatBtn ca = new FlatBtn();
                ca.Text = T.Cancel;
                ca.SetBounds(272, 86, 92, 30);
                ca.Click += delegate { f.DialogResult = DialogResult.Cancel; f.Close(); };

                f.Controls.AddRange(new Control[] { l, t, ok, ca });
                f.AcceptButton = null;
                Theme.Apply(f);

                return f.ShowDialog(this) == DialogResult.OK ? t.Text.Trim() : null;
            }
        }

        /// <summary>
        /// Grava e continua aberto.
        ///
        /// Fechar a cada gravacao obrigava a reabrir a janela para o proximo
        /// ajuste, e ajuste de mostrador raramente vem sozinho: troca-se o
        /// sensor, olha-se a previa, corrige-se a escala. A confirmacao vai
        /// para o rodape em vez de uma caixa de dialogo, que seria mais um
        /// clique para dizer o que ja se sabe.
        /// </summary>
        private void OnSave(object sender, EventArgs e)
        {
            SaveToProfile();
            _cfg.ActiveName = _current.Name;
            string erro;
            if (!_session.TrySaveAll(out erro))
            {
                _dirty = true;
                Aviso(T.SaveFailed(erro));
                return;
            }
            _saved = true;
            _dirty = false;

            if (_profileList != null)
            {
                _profileList.ActiveName = _cfg.ActiveName;
                _profileList.Invalidate();
                AtualizarPreviaDoPerfil();
            }
            if (Applied != null) Applied(this, EventArgs.Empty);
            Aviso(T.Saved);
        }

        /// <summary>
        /// Nao deixa sair calado com edicao pendente.
        ///
        /// Com Salvar sem fechar, "Fechar" passou a ser o unico caminho de
        /// saida - inclusive para quem so mexeu e nao gravou. Sem esta
        /// pergunta, o descarte seria silencioso.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_dirty && DialogResult != DialogResult.Retry)
            {
                DialogResult r = MessageBox.Show(T.UnsavedQuestion, T.UnsavedTitle,
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                if (r == DialogResult.Cancel) { e.Cancel = true; base.OnFormClosing(e); return; }
                if (r == DialogResult.Yes)
                {
                    OnSave(this, EventArgs.Empty);
                    // Falha de disco mantem a janela aberta. Fechar logo depois
                    // do aviso descartaria exatamente a edicao que nao gravou.
                    if (_dirty) { e.Cancel = true; base.OnFormClosing(e); return; }
                }
            }

            // OK significa "ha gravacao valida no disco"; quem abriu usa isso
            // para decidir se recarrega a configuracao e descarta a memoria
            if (DialogResult != DialogResult.Retry)
                DialogResult = _saved ? DialogResult.OK : DialogResult.Cancel;

            base.OnFormClosing(e);
        }

        /// <summary>
        /// O que so pode acontecer depois que a janela esta de fato na tela.
        ///
        /// As barras de rolagem so aceitam tema depois que o controle tem handle
        /// - por isso aqui, e nao no construtor.
        ///
        /// E o primeiro arranjo de largura, pelo mesmo tipo de motivo:
        /// Control.Visible nao devolve o sinalizador do proprio controle, devolve
        /// se ele esta EFETIVAMENTE visivel, e um filho de janela que ainda nao
        /// apareceu responde falso mesmo tendo sido marcado como visivel. Como o
        /// arranjo desiste de pagina escondida - para nao refazer cinco paginas
        /// de fundo a cada quadro da animacao da lateral - o unico arranjo
        /// anterior a qualquer interacao era justamente o que se perdia, e a
        /// pagina estreava na largura de projeto com a faixa morta ao lado.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Theme.ApplyScrollbars(this);
            ArranjarPaginaVisivel();

            if (_roda == null)
            {
                _roda = new RodaDoMouse(this);
                Application.AddMessageFilter(_roda);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_tick != null) { _tick.Stop(); _tick.Dispose(); }
            if (_noteTimer != null) { _noteTimer.Stop(); _noteTimer.Dispose(); }

            // Filtro de mensagens e do Application, e nao da janela: esquecer de
            // tirar deixaria um filtro morto examinando toda mensagem do processo
            // pelo resto da execucao, e a janela e reaberta a cada duplo clique
            // na bandeja.
            if (_roda != null) { Application.RemoveMessageFilter(_roda); _roda = null; }

            base.OnFormClosed(e);
        }
    }
}
