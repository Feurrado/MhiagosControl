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
    public partial class SettingsForm : Form
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
        private FaixaDeAviso _faixa;
        private Profile _current;
        private Profile _selectionAtLastSave;
        private bool _loading = false;
        private Timer _tick;
        private DateTime _lastSlowUiTickUtc = DateTime.MinValue;
        private Label _footerNote;
        private Timer _noteTimer;
        private FlatBtn _btDesfazer;

        // pagina Paineis
        private Control _pgPaineis;
        private SensorPicker _pick1, _pick2;
        private Segmented _unit1, _unit2;
        private FlatBtn[] _div1, _div2;
        private PanelPreview _preview;
        private Card _panelPreviewCard;
        private Card _cardPainel1, _cardPainel2;
        private SensorSlot _slot1, _slot2;

        // pagina Perfis
        private Control _pgPerfis;
        private ProfileList _profileList;
        private PanelPreview _profilePreview;
        private FlatBtn _btnApply, _btnDefault, _profileSensor1, _profileSensor2;
        private Card _cardGame, _cardRotation, _profilesListCard, _profilesPreviewCard;
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
                if (value && !_salvando) ArmarSalvamento();
                if (_temEdicao == value) return;
                _temEdicao = value;
                AtualizarRodape();
            }
        }
        private bool _temEdicao = false;
        private bool _salvando;
        private bool _autoPausado;
        private Timer _autoSalvar;

        /// <summary>
        /// Falha de disco: avisa e pausa a gravacao automatica, para nao repetir
        /// o aviso a cada 350 ms. Fechar a janela tenta de novo.
        /// </summary>
        private void FalhaAoGravar(string erro)
        {
            _autoPausado = true;
            Warn(T.SaveFailed(erro), T.TryAgain, delegate { _autoPausado = false; GravarPendente(); });
        }

        /// <summary>
        /// Toda edicao vale na hora: um instante depois do ultimo gesto a
        /// janela grava sozinha. O atraso curto junta o arrasto de um controle
        /// numa gravacao so, em vez de uma por pixel.
        /// </summary>
        private void ArmarSalvamento()
        {
            if (_autoSalvar == null)
            {
                _autoSalvar = new Timer();
                _autoSalvar.Interval = 350;
                _autoSalvar.Tick += delegate
                {
                    _autoSalvar.Stop();
                    if (_temEdicao && !_autoPausado && !IsDisposed) OnSave(this, EventArgs.Empty);
                };
            }
            _autoSalvar.Stop();
            _autoSalvar.Start();
        }

        /// <summary>Grava agora o que estiver pendente (fechar, Ctrl+S).</summary>
        internal void GravarPendente()
        {
            if (_autoSalvar != null) _autoSalvar.Stop();
            if (_temEdicao) OnSave(this, EventArgs.Empty);
        }

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
            _selectionAtLastSave = _current;

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
            MinimumSize = new Size(Ui.S(880), Ui.S(660));

            Size guardado = TamanhoGuardado();
            ClientSize = FitInitialClient(guardado != Size.Empty ? guardado : new Size(Ui.S(1280), Ui.S(790)),
                Screen.FromPoint(Cursor.Position).WorkingArea.Size);
            Point? posicao = PosicaoGuardada();
            if (posicao.HasValue)
            {
                StartPosition = FormStartPosition.Manual;
                Location = posicao.Value;
            }
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
            _nav.Width = Ui.S(210);
            _nav.Logo = IconImage();
            _nav.SubtitleCaption = T.ActiveProfile;
            _nav.Subtitle = _session.ActiveProfile.Name;
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
            _nav.SelectionChanged += delegate { ShowPage(); GuardarPagina(); };
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
            _btDesfazer = new FlatBtn();
            _btDesfazer.Text = T.Undo;
            _btDesfazer.Glyph = "";        // seta de desfazer, Segoe MDL2
            _btDesfazer.Primary = true;
            _btDesfazer.Visible = false;
            _btDesfazer.Click += delegate { Desfazer(); };
            _nav.Controls.Add(_btDesfazer);


            _footerNote = MakeLabel("", 0, 0, Ui.FontSmall);
            _footerNote.ForeColor = Ui.AccentText;
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
            _host.Padding = new Padding(Ui.S(16), Ui.S(16), Ui.S(16), Ui.S(16));
            Controls.Add(_host);

            _faixa = new FaixaDeAviso();
            Controls.Add(_faixa);

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
            _faixa.SendToBack();
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

            // Icone desenhado (Ui.IconeDeBateria): o E83F da Segoe MDL2 saia
            // como um bloco cheio e nao parecia bateria. O glifo fica so como
            // reserva para quem nao desenha o Icone.
            // Entra antes de Sobre, e nao junto de Metricas: os atalhos Alt+1
            // a Alt+6 e os testes de navegacao contam as paginas pela posicao.
            NavItem baterias = new NavItem();
            baterias.Text = T.NavBatteries; baterias.Glyph = "\uE83F"; baterias.Icone = Ui.IconeDeBateria; baterias.Page = BuildPageBaterias();

            visao.Key = "visao"; paineis.Key = "paineis"; metricas.Key = "metricas"; specs.Key = "specs";
            perfis.Key = "perfis"; config.Key = "config"; baterias.Key = "baterias"; sobre.Key = "sobre";

            NavItem[] fabrica = { visao, paineis, metricas, specs, perfis, config, baterias, sobre };
            foreach (NavItem it in OrdemDaBarra(fabrica, _cfg.NavOrder))
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
            _nav.OrderChanged += delegate { GravarOrdemDaBarra(); };
            ShowPage();
        }

        /// <summary>
        /// A ordem de fabrica rearranjada pela ordem gravada. Chave que nao
        /// existe mais e ignorada; pagina que nao estava na lista gravada (uma
        /// aba nova de uma versao futura) entra no lugar dela na ordem de
        /// fabrica, para nao sumir de quem ja tinha reorganizado o menu.
        /// </summary>
        internal static List<NavItem> OrdemDaBarra(NavItem[] fabrica, string gravada)
        {
            List<NavItem> l = new List<NavItem>();
            if (!string.IsNullOrEmpty(gravada))
                foreach (string chave in gravada.Split(','))
                    foreach (NavItem it in fabrica)
                        if (it.Key == chave.Trim() && !l.Contains(it)) l.Add(it);

            for (int i = 0; i < fabrica.Length; i++)
            {
                if (l.Contains(fabrica[i])) continue;
                // logo depois de quem vinha antes dela na fabrica, ou no comeco
                int pos = 0;
                for (int j = i - 1; j >= 0; j--)
                {
                    int k = l.IndexOf(fabrica[j]);
                    if (k >= 0) { pos = k + 1; break; }
                }
                l.Insert(pos, fabrica[i]);
            }
            return l;
        }

        /// <summary>Pagina da primeira exibicao, pela chave; nulo abre a de sempre.</summary>
        internal string PaginaInicial;

        /// <summary>Leva a janela ja aberta para a pagina pedida de fora, como o clique no widget.</summary>
        internal void MostrarPagina(string chave)
        {
            if (!string.IsNullOrEmpty(chave)) AbrirPaginaPelaChave(chave);
        }

        /// <summary>Abre a pagina pela chave, onde quer que a pessoa a tenha posto na barra.</summary>
        private void AbrirPaginaPelaChave(string chave)
        {
            IList<NavItem> itens = _nav.Items;
            for (int i = 0; i < itens.Count; i++)
                if (itens[i].Key == chave) { _nav.SelectedIndex = i; return; }
        }

        private bool _paginaPronta;

        /// <summary>
        /// Lembra a pagina em que a janela estava, para reabrir nela. So depois
        /// de exibida: as trocas de montagem nao sao escolha de ninguem.
        /// </summary>
        private void GuardarPagina()
        {
            if (!_paginaPronta || _nav == null || _nav.Selected == null) return;
            string chave = _nav.Selected.Key;
            if (string.IsNullOrEmpty(chave) || chave == _cfg.LastPage) return;
            try
            {
                _cfg.LastPage = chave;
                string erro;
                if (!_session.TrySavePreferences(delegate(Config draft, Config target)
                {
                    target.LastPage = draft.LastPage;
                }, out erro)) Log.Write("ultima pagina nao persistiu: " + erro);
            }
            catch (Exception ex) { Log.Error("gravar ultima pagina", ex); }
        }

        /// <summary>Grava na hora, como a barra recolhida: e arrumacao da tela, nao edicao de perfil.</summary>
        private void GravarOrdemDaBarra()
        {
            try
            {
                List<string> chaves = new List<string>();
                foreach (NavItem it in _nav.Items) chaves.Add(it.Key);
                _cfg.NavOrder = string.Join(",", chaves.ToArray());
                string erro;
                if (!_session.TrySavePreferences(delegate(Config draft, Config target)
                {
                    target.NavOrder = draft.NavOrder;
                }, out erro)) Log.Write("ordem da barra lateral nao persistiu: " + erro);
            }
            catch (Exception ex) { Log.Error("gravar ordem da barra lateral", ex); }
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
            if (_nav == null || _btDesfazer == null) return;

            int Alt = Ui.S(32);
            int Esp = Ui.S(8);

            bool estreita = _nav.Width < Ui.S(120);
            // A margem acompanha a abertura da barra, em vez de saltar de 8 para
            // 18 px no meio do deslize.
            int margem = Ui.S(8) + (int)Math.Round((Ui.S(18) - Ui.S(8)) * _nav.Abertura);
            int larg = _nav.Width - margem * 2;

            // Uma nota de duas linhas exige largura; recolhida, a barra nao tem.
            bool cabeNota = !estreita;
            // O espaco da nota cresce com a barra: os botoes sobem aos poucos.
            int alturaNota = (int)Math.Round(Ui.S(32) * _nav.Abertura);

            int baixo = _nav.Height - Ui.S(18);
            int y = baixo - alturaNota - Alt - Esp;

            _btDesfazer.SetBounds(margem, y, larg, Alt);

            if (_footerNote != null)
            {
                _footerNote.Visible = _footerNote.Visible && cabeNota;
                _footerNote.SetBounds(margem, y + Alt + Esp + Ui.S(4), larg, alturaNota);
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
        /// Onde a janela estava da ultima vez, se ainda cabe em algum monitor.
        ///
        /// Monitor desligado ou resolucao menor deixaria a janela fora da tela,
        /// sem como alcancar a barra de titulo. Por isso a posicao so vale se
        /// uma faixa da barra de titulo (largura minima de 120 px) cai dentro
        /// da area de trabalho de algum monitor; vale entao a janela puxada
        /// para dentro dela. Do contrario, nulo: abre centralizada.
        /// </summary>
        private Point? PosicaoGuardada()
        {
            if (_cfg.WindowX == int.MinValue || _cfg.WindowY == int.MinValue) return null;
            Rectangle[] todas = new Rectangle[Screen.AllScreens.Length];
            for (int i = 0; i < todas.Length; i++) todas[i] = Screen.AllScreens[i].WorkingArea;
            return PosicaoValida(new Point(_cfg.WindowX, _cfg.WindowY), Size, todas);
        }

        /// <summary>A posicao puxada para dentro do monitor que a contem, ou nulo se nenhum a alcanca.</summary>
        internal static Point? PosicaoValida(Point p, Size tamanho, Rectangle[] areasDeTrabalho)
        {
            int faixaMin = 120, altBarra = 40;
            Rectangle barra = new Rectangle(p.X, p.Y, tamanho.Width, altBarra);
            foreach (Rectangle wa in areasDeTrabalho)
            {
                Rectangle comum = Rectangle.Intersect(barra, wa);
                if (comum.Width < Math.Min(faixaMin, tamanho.Width) || comum.Height < altBarra / 2) continue;

                int x = p.X, y = p.Y;
                if (x + tamanho.Width > wa.Right) x = wa.Right - tamanho.Width;
                if (x < wa.Left) x = wa.Left;
                if (y + tamanho.Height > wa.Bottom) y = wa.Bottom - tamanho.Height;
                if (y < wa.Top) y = wa.Top;
                return new Point(x, y);
            }
            return null;
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
            if (!IsHandleCreated) return;
            if (_cfg.WindowW == ClientSize.Width && _cfg.WindowH == ClientSize.Height
                && _cfg.WindowX == Location.X && _cfg.WindowY == Location.Y) return;
            try
            {
                _cfg.WindowW = ClientSize.Width;
                _cfg.WindowH = ClientSize.Height;
                _cfg.WindowX = Location.X;
                _cfg.WindowY = Location.Y;
                string erro;
                if (!_session.TrySavePreferences(delegate(Config draft, Config target)
                {
                    target.WindowW = draft.WindowW;
                    target.WindowH = draft.WindowH;
                    target.WindowX = draft.WindowX;
                    target.WindowY = draft.WindowY;
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
                p.RolarPor(delta > 0 ? -Ui.S(60) : Ui.S(60));
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
            else if (pagina == _pgBaterias) ArranjarBaterias();
            else if (pagina == _pgPaineis) ArranjarPaineis();
            else if (pagina == _pgConfig) ArrangeSettings();
            else if (pagina == _pgPerfis) ArrangeProfiles();
            else if (pagina == _pgSobre) ArranjarSobre();
            else ArrangeAboutBounds(pagina);
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
        private static readonly int LarguraMaxDaPagina = Ui.S(1100);

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

                // Pagina mais estreita que os dois cartoes lado a lado (janela no
                // tamanho minimo, ou barra lateral aberta): o segundo cartao e a
                // previa saiam cortados na borda direita, sem como rolar ate eles.
                // Empilha, e a rolagem vertical que a pagina ja tem cuida do resto.
                int disp = _pgPaineis.Width - Ui.S(4) - Pagina.LarguraDaBarra;
                if (_cardPainel1 != null && _cardPainel2 != null && disp < Ui.S(756))
                {
                    int w = Math.Max(Ui.S(370), disp);
                    int gap = Ui.S(12);
                    _cardPainel1.Bounds = new Rectangle(0, 0, w, _cardPainel1.Height);
                    _cardPainel2.Bounds = new Rectangle(0, _cardPainel1.Height + gap, w, _cardPainel2.Height);
                    _panelPreviewCard.Bounds = new Rectangle(0, _cardPainel2.Bottom + gap, w, _panelPreviewCard.Height);
                }

                // O cartao de previa ocupa o restante da pagina. Com a altura
                // fixa de projeto ele ultrapassava a area por poucos pixels e
                // exibia uma barra de rolagem para praticamente nenhum conteudo.
                // Pagina.Sincronizar reserva mais 8 px depois do ultimo filho.
                int altura = _pgPaineis.ClientSize.Height - _panelPreviewCard.Top - Ui.S(12);
                _panelPreviewCard.Height = Math.Max(Ui.S(400), altura);
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
            int disp = pagina.Width - Ui.S(4);
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
        private PeripheralStrip _cardPerif;
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

            // Carga do headset e do mouse, logo acima das especificacoes: muda
            // devagar e se consulta de passagem, entao fica na faixa de baixo,
            // longe das leituras que pedem o olho.
            _cardPerif = new PeripheralStrip();
            _cardPerif.BateriasPedidas += delegate { AbrirPaginaPelaChave("baterias"); };
            _pgVisao.Controls.Add(_cardPerif);

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
            c.Janela = MetricHistory.JanelaVisaoGeral;
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
                c.Unidade = s.EffectiveUnit;
                c.SensorId = s.Id;
                c.Janela = MetricHistory.JanelaVisaoGeral;
                float? at, pe;
                MetricPicker.Faixas(s.EffectiveUnit, s.Id, out at, out pe);
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

            int Esp = Ui.S(8);
            int AlturaBloco = Ui.S(104);
            int AlturaSpecs = Ui.S(56);
            int AlturaPerif = Ui.S(80);
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
                int yPerif = ySpecs - Esp - AlturaPerif;
                int hMeio = yPerif - Esp - yMeio;
                if (hMeio < MeioMin)
                {
                    hMeio = MeioMin;
                    yPerif = yMeio + hMeio + Esp;
                    ySpecs = yPerif + AlturaPerif + Esp;
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
                ySpecs = yPerif + AlturaPerif + Esp;

                _cardCooler.SetBounds(x0, yMeio, wEsq, hMeio);

                int hCurva = (hMeio - Esp) / 2;
                if (_vgP1 != null) _vgP1.SetBounds(x0 + wEsq + Esp, yMeio, wDir, hCurva);
                if (_vgP2 != null)
                    _vgP2.SetBounds(x0 + wEsq + Esp, yMeio + hCurva + Esp, wDir, hMeio - hCurva - Esp);

                _cardPerif.SetBounds(x0, yPerif, alvo, AlturaPerif);
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
                    _cardSpecs.Top += y0;
                }

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
            if (soma > disp && soma > 0)
                for (int i = 0; i < n; i++) larg[i] = larg[i] * disp / soma;

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
        private Label _lbBateriasVazio;
        private FlatBtn _btGHub;
        private bool? _temGHub;
        private readonly List<CartaoDeBateria> _cartoesDeBateria = new List<CartaoDeBateria>();
        private DateTime _bateriasLidas = DateTime.MinValue;

        /// <summary>
        /// Quanto dura a carga de cada periferico, quando foi a ultima e como a
        /// porcentagem andou. Os numeros vem de HistoricoDeBateria, que a thread
        /// de atualizacao alimenta mesmo com esta janela fechada.
        /// </summary>
        private Control BuildPageBaterias()
        {
            _pgBaterias = new Pagina();
            _pgBaterias.RolarNaVertical();

            _lbBateriasVazio = MakeLabel(T.BatNoDevices, 2, 4, Ui.FontBase);
            _lbBateriasVazio.ForeColor = Ui.Muted;
            _lbBateriasVazio.AutoSize = false;
            _lbBateriasVazio.Visible = false;
            _pgBaterias.Controls.Add(_lbBateriasVazio);

            _btGHub = new FlatBtn();
            _btGHub.Text = T.OpenGHub;
            _btGHub.Visible = false;
            _btGHub.Click += delegate { Perifericos.AbrirGHub(); };
            _pgBaterias.Controls.Add(_btGHub);

            // Os aparelhos vem primeiro. As opcoes do widget ficam atras de um
            // botao discreto no alto, fechado por padrao; se a pessoa deixar
            // aberto, ele volta aberto. Ligar e desligar segue tambem na bandeja.
            _widget = _data as IControleDoWidget;
            if (_widget != null)
            {
                _btWidget = new BotaoDoWidget();
                _btWidget.Text = T.WidgetButton;
                _btWidget.Aberto = DetalhesDaBateria.Aberto(ChaveDoPainelDoWidget);
                _btWidget.Click += delegate { AlternarPainelDoWidget(); };
                _dicaDoWidget.SetToolTip(_btWidget, T.WidgetButtonTip);
                _pgBaterias.Controls.Add(_btWidget);

                // O cartao mora num CollapsiblePanel: abrir e fechar desliza a
                // altura e os aparelhos de baixo acompanham, em vez de saltar.
                Card c = MontarCartaoDoWidget();
                _painelDoWidget = new CollapsiblePanel(_btWidget.Aberto);
                _painelDoWidget.AccessibleName = T.WidgetCard;
                _painelDoWidget.Conteudo = c;
                _painelDoWidget.AlturaAberta = AlturaDoCartaoDoWidget;
                _painelDoWidget.AlturaMudou += delegate { ArranjarBaterias(); };
                _pgBaterias.Controls.Add(_painelDoWidget);
            }
            return _pgBaterias;
        }

        private CollapsiblePanel _painelDoWidget;

        private BotaoDoWidget _btWidget;
        private readonly ToolTip _dicaDoWidget = new ToolTip();

        /// <summary>
        /// Painel aberto ou fechado, no mesmo arquivo do "Mais detalhes" de cada
        /// aparelho: abrir uma secao nao e mudanca de configuracao para pedir
        /// Salvar. As chaves dos aparelhos tem barra ("GHub|G325"), entao esta
        /// nao colide com nenhuma.
        /// </summary>
        internal const string ChaveDoPainelDoWidget = "painel-do-widget";

        private void AlternarPainelDoWidget()
        {
            if (_btWidget == null) return;
            DetalhesDaBateria.Alternar(ChaveDoPainelDoWidget);
            bool aberto = DetalhesDaBateria.Aberto(ChaveDoPainelDoWidget);
            // O botao muda de estado na hora; a seta dele gira com o painel,
            // pelo ArranjarBaterias de cada quadro.
            _btWidget.Aberto = aberto;
            if (aberto) SincronizarWidget();   // conteudo certo antes de aparecer
            if (_painelDoWidget != null) _painelDoWidget.Definir(aberto, true);
            ArranjarBaterias();
        }

        // ---------------- widget de bateria ----------------

        private IControleDoWidget _widget;
        private Card _cardWidget;
        private Toggle _tgWidget, _tgWidgetFrente, _tgWidgetTrava;
        private Segmented _sgWidgetLayout, _sgWidgetVidro, _sgWidgetCor;
        /// <summary>Preenchendo os controles com a configuracao: as mudancas nao sao do usuario.</summary>
        private bool _sincronizandoWidget;
        private static readonly int AlturaDoCartaoDoWidget = Ui.S(330);
        private static readonly int AlturaDoBotaoDoWidget = Ui.S(30);

        /// <summary>
        /// As mesmas opcoes do botao direito do widget, num lugar que se acha
        /// sem saber que o menu existe. Cada mudanca vale na hora.
        /// </summary>
        private Card MontarCartaoDoWidget()
        {
            Card c = new Card();
            c.Title = T.WidgetCard;
            _cardWidget = c;

            _tgWidget = new Toggle();
            _tgWidget.Label = T.WidgetShow;
            _tgWidget.SetBounds(Ui.S(16), Ui.S(50), Ui.S(480), Ui.S(26));
            c.Controls.Add(_tgWidget);

            c.Controls.Add(MakeLabel(T.WidgetLayout, 16, 96, Ui.FontMed));
            _sgWidgetLayout = new Segmented();
            _sgWidgetLayout.SetItems(T.WidgetRings, T.WidgetList);
            _sgWidgetLayout.SetBounds(Ui.S(16), Ui.S(120), Ui.S(220), Ui.S(30));
            c.Controls.Add(_sgWidgetLayout);

            c.Controls.Add(MakeLabel(T.WidgetGlass, 260, 96, Ui.FontMed));
            _sgWidgetVidro = new Segmented();
            _sgWidgetVidro.SetItems(T.WidgetSharpShort, T.WidgetClear, T.WidgetFrosted);
            _sgWidgetVidro.SetBounds(Ui.S(260), Ui.S(120), Ui.S(300), Ui.S(30));
            c.Controls.Add(_sgWidgetVidro);

            c.Controls.Add(MakeLabel(T.WidgetTint, 16, 166, Ui.FontMed));
            _sgWidgetCor = new Segmented();
            _sgWidgetCor.SetItems(T.WidgetTintAutoShort, T.WidgetTintDark, T.WidgetTintLight);
            _sgWidgetCor.SetBounds(Ui.S(16), Ui.S(190), Ui.S(450), Ui.S(30));
            c.Controls.Add(_sgWidgetCor);

            _tgWidgetFrente = new Toggle();
            _tgWidgetFrente.Label = T.WidgetOnTop;
            _tgWidgetFrente.SetBounds(Ui.S(16), Ui.S(238), Ui.S(300), Ui.S(26));
            c.Controls.Add(_tgWidgetFrente);

            _tgWidgetTrava = new Toggle();
            _tgWidgetTrava.Label = T.WidgetLock;
            _tgWidgetTrava.SetBounds(Ui.S(330), Ui.S(238), Ui.S(240), Ui.S(26));
            c.Controls.Add(_tgWidgetTrava);

            FlatBtn canto = new FlatBtn();
            canto.Text = T.WidgetBackToCorner;
            canto.SetBounds(Ui.S(16), Ui.S(280), Ui.S(220), Ui.S(32));
            canto.Click += delegate { _widget.WidgetAoCanto(); };
            c.Controls.Add(canto);

            Label nota = MakeLabel(T.WidgetCardNote, 250, 280, Ui.FontSmall);
            nota.AutoSize = false;
            nota.Size = new Size(Ui.S(480), Ui.S(34));
            nota.TextAlign = ContentAlignment.MiddleLeft;
            nota.ForeColor = Ui.Muted;
            c.Controls.Add(nota);

            // janela estreita: o que fica a direita encolhe em vez de passar da borda
            c.Resize += delegate
            {
                int livre = c.Width - Ui.S(16);
                _sgWidgetVidro.Width = Math.Max(Ui.S(180), Math.Min(Ui.S(300), livre - Ui.S(260)));
                _tgWidgetTrava.Width = Math.Max(Ui.S(140), Math.Min(Ui.S(240), livre - Ui.S(330)));
                nota.Width = Math.Max(Ui.S(120), Math.Min(Ui.S(480), livre - Ui.S(250)));
            };

            // preenche antes de ligar os eventos: cada controle avisa toda mudanca
            SincronizarWidget();

            _tgWidget.CheckedChanged += delegate { if (!_sincronizandoWidget) _widget.LigarWidget(_tgWidget.Checked); };
            _sgWidgetLayout.SelectedIndexChanged += delegate
            {
                MudarWidget(delegate(ConfigDoWidget w)
                {
                    w.Layout = _sgWidgetLayout.SelectedIndex == 1 ? LayoutDoWidget.Lista : LayoutDoWidget.Aneis;
                });
            };
            _sgWidgetVidro.SelectedIndexChanged += delegate
            {
                MudarWidget(delegate(ConfigDoWidget w)
                {
                    w.Nitido = _sgWidgetVidro.SelectedIndex == 0;
                    w.Fosco = _sgWidgetVidro.SelectedIndex == 2;
                });
            };
            _sgWidgetCor.SelectedIndexChanged += delegate
            {
                MudarWidget(delegate(ConfigDoWidget w)
                {
                    int i = _sgWidgetCor.SelectedIndex;
                    w.Cor = i == 1 ? CorDoVidro.Escura : i == 2 ? CorDoVidro.Clara : CorDoVidro.Automatica;
                });
            };
            _tgWidgetFrente.CheckedChanged += delegate { MudarWidget(delegate(ConfigDoWidget w) { w.NaFrente = _tgWidgetFrente.Checked; }); };
            _tgWidgetTrava.CheckedChanged += delegate { MudarWidget(delegate(ConfigDoWidget w) { w.Travado = _tgWidgetTrava.Checked; }); };
            return c;
        }

        private void MudarWidget(Action<ConfigDoWidget> mudanca)
        {
            if (_sincronizandoWidget || _widget == null) return;
            mudanca(_widget.ConfigDoWidget);
            _widget.WidgetMudou();
        }

        /// <summary>
        /// Traz para os controles o que esta valendo: o menu do proprio widget
        /// e o da bandeja tambem mudam a configuracao.
        /// </summary>
        private void SincronizarWidget()
        {
            if (_widget == null || _cardWidget == null) return;
            ConfigDoWidget w = _widget.ConfigDoWidget;
            _sincronizandoWidget = true;
            try
            {
                _tgWidget.Checked = w.Ativo;
                _sgWidgetLayout.SelectedIndex = w.Layout == LayoutDoWidget.Lista ? 1 : 0;
                _sgWidgetVidro.SelectedIndex = w.Nitido ? 0 : w.Fosco ? 2 : 1;
                _sgWidgetCor.SelectedIndex = w.Cor == CorDoVidro.Escura ? 1 : w.Cor == CorDoVidro.Clara ? 2 : 0;
                _tgWidgetFrente.Checked = w.NaFrente;
                _tgWidgetTrava.Checked = w.Travado;
            }
            finally { _sincronizandoWidget = false; }
        }

        private void AtualizarBaterias()
        {
            if (_pgBaterias == null) return;
            // o tick e de 250 ms; a bateria anda em minutos
            if ((DateTime.Now - _bateriasLidas).TotalMilliseconds < 1000) return;
            _bateriasLidas = DateTime.Now;

            Periferico[] vivos = Perifericos.Atual;
            List<AnaliseDeBateria> analises = HistoricoDeBateria.Analisar(vivos);

            // Os cartoes sao reaproveitados por chave: quem chega entra com fade e
            // expansao, quem some sai do mesmo jeito e so e removido ao fim.
            bool animar = _bateriasPopuladas && _pgBaterias.Visible;
            bool mudouLista = false;
            HashSet<string> vivas = new HashSet<string>();
            foreach (AnaliseDeBateria a in analises) vivas.Add(a.Chave);

            foreach (CartaoDeBateria c in _cartoesDeBateria)
                if (!c.Saindo && !vivas.Contains(c.Chave)) { c.Sair(); mudouLista = true; }

            List<CartaoDeBateria> nova = new List<CartaoDeBateria>();
            foreach (AnaliseDeBateria a in analises)
            {
                CartaoDeBateria achado = null;
                foreach (CartaoDeBateria c in _cartoesDeBateria) if (c.Chave == a.Chave) achado = c;
                if (achado == null)
                {
                    achado = new CartaoDeBateria();
                    achado.AlturaMudou += delegate { ArranjarBaterias(); };
                    CartaoDeBateria novo = achado;
                    achado.Saiu += delegate { RemoverCartaoDeBateria(novo); };
                    int? aut = null;
                    foreach (Periferico p in vivos) if (HistoricoDeBateria.Chave(p) == a.Chave) aut = p.Autonomia;
                    achado.Mostrar(a, aut);
                    _pgBaterias.Controls.Add(achado);
                    if (animar) achado.NascerEntrando();
                    mudouLista = true;
                }
                else if (achado.Saindo) { achado.Entrar(); mudouLista = true; }
                nova.Add(achado);
            }

            // quem saiu da leitura fica logo depois do cartao que vinha antes dele
            int ultimo = -1;
            foreach (CartaoDeBateria c in _cartoesDeBateria)
            {
                if (nova.Contains(c)) { ultimo = nova.IndexOf(c); continue; }
                nova.Insert(ultimo + 1, c);
                ultimo++;
            }
            for (int i = 0; !mudouLista && i < nova.Count; i++) mudouLista = !ReferenceEquals(nova[i], _cartoesDeBateria.Count > i ? _cartoesDeBateria[i] : null);
            _cartoesDeBateria.Clear();
            _cartoesDeBateria.AddRange(nova);

            foreach (CartaoDeBateria c in _cartoesDeBateria)
            {
                if (c.Saindo) continue;
                int? autonomia = null;
                foreach (Periferico p in vivos)
                    if (HistoricoDeBateria.Chave(p) == c.Chave) autonomia = p.Autonomia;
                foreach (AnaliseDeBateria a in analises)
                    if (a.Chave == c.Chave) c.Mostrar(a, autonomia);
            }
            _bateriasPopuladas = true;

            if (mudouLista) ArranjarBaterias();
            // o menu do widget e o da bandeja mudam a configuracao sem passar
            // por aqui: o cartao confere a cada leitura
            SincronizarWidget();
        }

        /// <summary>A primeira carga da pagina nao anima: os cartoes ja chegam inteiros.</summary>
        private bool _bateriasPopuladas;

        private void RemoverCartaoDeBateria(CartaoDeBateria c)
        {
            // fora do tick do relogio que acabou de terminar
            BeginInvoke(new Action(delegate
            {
                if (c.IsDisposed || !c.Saindo || !_cartoesDeBateria.Contains(c)) return;
                _pgBaterias.Controls.Remove(c);
                _cartoesDeBateria.Remove(c);
                c.Dispose();
                ArranjarBaterias();
            }));
        }

        private void ArranjarBaterias()
        {
            if (_pgBaterias == null) return;
            int Esp = Ui.S(12);

            int disp = _pgBaterias.Width - Ui.S(4) - Pagina.LarguraDaBarra;
            int larg = disp > LarguraMaxDaPagina ? LarguraMaxDaPagina : disp;
            if (larg < Ui.S(460)) larg = Ui.S(460);
            int x = Ui.S(2) + Math.Max(0, (disp - larg) / 2);

            // Um rearranjo por quadro de animacao: os filhos andam juntos e a
            // pagina so refaz o layout uma vez, no ResumeLayout.
            _pgBaterias.SuspendLayout();
            try
            {
                int y = Ui.S(2);
                if (_btWidget != null)
                {
                    int lb = _btWidget.LarguraDesejada();
                    _btWidget.SetBounds(x + larg - lb, y, lb, AlturaDoBotaoDoWidget);
                    y += AlturaDoBotaoDoWidget + Ui.S(8);
                }
                if (_painelDoWidget != null)
                {
                    // A cada quadro da animacao: a casca na altura do quadro, a
                    // seta do botao no mesmo ponto da curva e o vao abaixo
                    // crescendo junto, para os aparelhos descerem sem degrau.
                    if (_painelDoWidget.Expandido && !_painelDoWidget.Animando) SincronizarWidget();
                    int h = _painelDoWidget.AlturaAtual;
                    _painelDoWidget.SetBounds(x, y, larg, h);
                    if (_btWidget != null) _btWidget.Giro = _painelDoWidget.Progresso;
                    if (h > 0) y += h + (int)Math.Round(Esp * _painelDoWidget.Progresso);
                }

                _lbBateriasVazio.Visible = _cartoesDeBateria.Count == 0;
                _lbBateriasVazio.SetBounds(x, y + Ui.S(2), larg, Ui.S(40));
                if (_temGHub == null) _temGHub = Perifericos.GHubInstalado();
                _btGHub.Visible = _lbBateriasVazio.Visible && _temGHub.Value;
                _btGHub.SetBounds(x, y + Ui.S(46), Ui.S(180), Ui.S(32));

                foreach (CartaoDeBateria c in _cartoesDeBateria)
                {
                    int alt = c.AlturaDesejada(larg);
                    c.SetBounds(x, y, larg, alt);
                    y += alt + (int)Math.Round(Esp * c.Presenca);
                }
            }
            finally { _pgBaterias.ResumeLayout(); }
            _pgBaterias.Sincronizar();
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
            _btCopiarSpecs.SetBounds(Ui.S(2), Ui.S(2), Ui.S(150), Ui.S(30));
            _btCopiarSpecs.Enabled = false;
            _btCopiarSpecs.Click += delegate
            {
                // Depois de uma coleta que falhou, o mesmo botao tenta de novo.
                if (SpecSheet.Folha != null) { CopiarSpecs(); return; }
                _btCopiarSpecs.Text = T.SpecsCopy;
                _btCopiarSpecs.Enabled = false;
                _lbSpecsEstado.Text = T.SpecsLoading;
                _lbSpecsEstado.ForeColor = Ui.Muted;
                GarantirSpecs();
            };
            _pgSpecs.Controls.Add(_btCopiarSpecs);

            _lbSpecsEstado = MakeLabel(T.SpecsLoading, 166, 4, Ui.FontBase);
            _lbSpecsEstado.Size = new Size(Ui.S(560), Ui.S(26));
            _lbSpecsEstado.TextAlign = ContentAlignment.MiddleLeft;
            _lbSpecsEstado.ForeColor = Ui.Muted;
            _pgSpecs.Controls.Add(_lbSpecsEstado);

            _lbSpecsNota = MakeLabel(T.SpecsNote, 166, 4, Ui.FontSmall);
            _lbSpecsNota.Size = new Size(Ui.S(600), Ui.S(28));
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
            if (_pgSpecs == null) return;
            if (folha == null || folha.Count == 0)
            {
                // Estado de erro, e nao o "Consultando" parado para sempre.
                _lbSpecsEstado.Text = T.SpecsFailed;
                _lbSpecsEstado.ForeColor = Ui.Text;
                _lbSpecsEstado.Visible = true;
                _lbSpecsNota.Visible = false;
                _btCopiarSpecs.Text = T.SpecsRetry;
                _btCopiarSpecs.Enabled = true;
                return;
            }

            // Ja montada: a folha nao muda enquanto a maquina esta ligada.
            if (_specCards.Count > 0) return;

            _pgSpecs.SuspendLayout();
            try
            {
                _lbSpecsEstado.Visible = false;
                _lbSpecsNota.Visible = true;
                _btCopiarSpecs.Text = T.SpecsCopy;
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
                    c.Height = Ui.S(y + 10);   // y em medida de projeto
                    c.Tag = vals;
                    _specFullHeights[c] = c.Height;

                    BlocoDeSpec bloco = new BlocoDeSpec(g);
                    int categoria = _specBlocos.Count + 1;
                    bloco.Click += delegate { SelecionarCategoriaDeSpec(categoria); };
                    _pgSpecs.Controls.Add(bloco);
                    _specBlocos.Add(bloco);
                }

                MontarCategoriasDeSpecs(folha);
            }
            finally { _pgSpecs.ResumeLayout(); }

            ArranjarSpecsPagina();
        }

        private readonly List<Card> _specCards = new List<Card>();

        /// <summary>Os blocos do resumo, na mesma ordem de _specCards.</summary>
        private readonly List<BlocoDeSpec> _specBlocos = new List<BlocoDeSpec>();

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
                b.Height = Ui.S(32);
                b.Click += delegate(object sender, EventArgs e)
                {
                    FlatBtn clicked = sender as FlatBtn;
                    if (clicked == null) return;
                    SelecionarCategoriaDeSpec((int)clicked.Tag);
                };
                _pgSpecs.Controls.Add(b);
                _specCategoryButtons.Add(b);
            }
        }

        private void SelecionarCategoriaDeSpec(int categoria)
        {
            _specCategory = categoria;
            foreach (FlatBtn item in _specCategoryButtons)
            {
                item.Primary = (int)item.Tag == categoria;
                item.Invalidate();
            }
            ArranjarSpecsPagina();
            _pgSpecs.RolarPor(-100000);   // a ficha abre do topo
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

            int Esp = Ui.S(12);

            int disp = _pgSpecs.Width - Ui.S(4) - Pagina.LarguraDaBarra;
            if (disp > LarguraMaxDaPagina) disp = LarguraMaxDaPagina;
            if (disp < Ui.S(460)) disp = Ui.S(460);

            // Categorias em pastilhas que quebram de linha. Uma faixa dividida
            // em partes iguais esmagaria "Armazenamento" em janelas estreitas;
            // aqui cada uma ocupa o texto que realmente possui.
            int catX = Ui.S(2), catY = _btCopiarSpecs.Bottom + Ui.S(10);
            foreach (FlatBtn b in _specCategoryButtons)
            {
                int w = TextRenderer.MeasureText(b.Text, Ui.FontBase).Width + Ui.S(30);
                w = Math.Max(Ui.S(82), Math.Min(Ui.S(164), w));
                if (catX > Ui.S(2) && catX + w > disp) { catX = Ui.S(2); catY += Ui.S(36); }
                b.SetBounds(catX, catY, w, Ui.S(30));
                catX += w + Ui.S(6);
            }
            int topo = (_specCategoryButtons.Count > 0 ? catY + Ui.S(30) : _btCopiarSpecs.Bottom) + Ui.S(12);

            if (_specCategory > 0)
            {
                foreach (BlocoDeSpec bl in _specBlocos) bl.Visible = false;
                int escolhido = _specCategory - 1;
                for (int i = 0; i < _specCards.Count; i++)
                {
                    Card c = _specCards[i];
                    c.Visible = i == escolhido;
                    if (!c.Visible) continue;
                    MostrarLinhasDeSpec(c, int.MaxValue);
                    int detailWidth = Math.Min(Ui.S(820), disp);
                    int detailX = Ui.S(2) + (disp - detailWidth) / 2;
                    c.SetBounds(detailX, topo, detailWidth, _specFullHeights[c]);
                    ArranjarLinhasDeSpec(c);
                }
                _pgSpecs.Sincronizar();
                return;
            }

            // Resumo: um mosaico de blocos, cada peca com o que se procura nela.
            // A tabela completa de cada uma fica na categoria, a um clique.
            if (_specBlocos.Count == _specCards.Count && _specBlocos.Count > 0)
            {
                foreach (Card c in _specCards) c.Visible = false;
                ArranjarMosaicoDeSpecs(topo, disp, Esp);
                if (_lbSpecsNota != null)
                    _lbSpecsNota.Width = Math.Max(Ui.S(160), disp - _lbSpecsNota.Left);
                _pgSpecs.Sincronizar();
                return;
            }

            foreach (Card c in _specCards)
            {
                c.Visible = true;
                c.Height = Math.Min(Ui.S(128), _specFullHeights[c]);
                MostrarLinhasDeSpec(c, 3);
            }

            if (disp >= Ui.S(620) && _specCards.Count >= 7)
            {
                // Ordem visual estável: duas fileiras de componentes, discos
                // usando a largura toda e, por fim, rede/sistema. O antigo
                // empacotamento pela coluna mais curta deixava Armazenamento
                // sozinho diante de dois cartões empilhados e um grande vazio.
                int half = (disp - Esp) / 2;
                int y = topo;
                ArranjarParDeSpecs(0, 1, y, half, Esp); y += Ui.S(128) + Esp;
                ArranjarParDeSpecs(2, 3, y, half, Esp); y += Ui.S(128) + Esp;

                Card storage = _specCards[4];
                storage.SetBounds(Ui.S(2), y, disp, Ui.S(104));
                ArranjarLinhasDeSpecEmColunas(storage);
                y += storage.Height + Esp;

                ArranjarParDeSpecs(5, 6, y, half, Esp); y += Ui.S(128) + Esp;

                // Grupos futuros não somem: entram abaixo em largura inteira.
                for (int i = 7; i < _specCards.Count; i++)
                {
                    Card extra = _specCards[i];
                    extra.SetBounds(Ui.S(2), y, disp, Ui.S(128));
                    ArranjarLinhasDeSpec(extra);
                    y += extra.Height + Esp;
                }
            }
            else
            {
                int y = topo;
                foreach (Card c in _specCards)
                {
                    c.SetBounds(Ui.S(2), y, disp, Ui.S(128));
                    ArranjarLinhasDeSpec(c);
                    y += c.Height + Esp;
                }
            }

            // O aviso ao lado do botao acompanha a largura: fixo em 600 px, ele
            // saia cortado no meio de "nome da maquina" numa janela estreita.
            if (_lbSpecsNota != null)
                _lbSpecsNota.Width = Math.Max(Ui.S(160), disp - _lbSpecsNota.Left);

            _pgSpecs.Sincronizar();
        }

        /// <summary>
        /// O mosaico: processador e discos na largura inteira, o resto aos pares.
        ///
        /// A ordem segue o que se procura primeiro - processador, video e
        /// memoria - e nao a ordem da coleta. Os dois blocos de um par ficam com
        /// a altura do maior, para as bordas de baixo alinharem; um bloco que
        /// sobraria sozinho numa linha toma a largura inteira.
        /// </summary>
        private void ArranjarMosaicoDeSpecs(int topo, int disp, int esp)
        {
            string[] ordem = { T.SpecCpu, T.SpecGpu, T.SpecRam, T.SpecStorage, T.SpecBoard, T.SpecOs, T.SpecNetwork };
            List<BlocoDeSpec> fila = new List<BlocoDeSpec>();
            foreach (string t in ordem)
                foreach (BlocoDeSpec b in _specBlocos)
                    if (b.Grupo.Titulo == t && !fila.Contains(b)) fila.Add(b);
            foreach (BlocoDeSpec b in _specBlocos) if (!fila.Contains(b)) fila.Add(b);

            bool duas = disp >= Ui.S(620);
            int meia = (disp - esp) / 2;
            int y = topo;
            int i = 0;
            while (i < fila.Count)
            {
                BlocoDeSpec a = fila[i];
                a.Visible = true;
                BlocoDeSpec b = (duas && !a.Largo && i + 1 < fila.Count && !fila[i + 1].Largo) ? fila[i + 1] : null;
                if (b == null)
                {
                    int h = a.AlturaPara(disp);
                    a.SetBounds(Ui.S(2), y, disp, h);
                    y += h + esp;
                    i++;
                    continue;
                }
                b.Visible = true;
                int alt = Math.Max(a.AlturaPara(meia), b.AlturaPara(meia));
                a.SetBounds(Ui.S(2), y, meia, alt);
                b.SetBounds(Ui.S(2) + meia + esp, y, disp - meia - esp, alt);
                y += alt + esp;
                i += 2;
            }
        }

        private void ArranjarParDeSpecs(int left, int right, int y, int width, int gap)
        {
            Card a = _specCards[left], b = _specCards[right];
            a.SetBounds(Ui.S(2), y, width, Ui.S(128));
            b.SetBounds(Ui.S(2) + width + gap, y, width, Ui.S(128));
            ArranjarLinhasDeSpec(a);
            ArranjarLinhasDeSpec(b);
        }

        /// <summary>Três unidades de armazenamento lado a lado no resumo.</summary>
        private static void ArranjarLinhasDeSpecEmColunas(Card card)
        {
            List<Label> vals = card.Tag as List<Label>;
            if (vals == null || vals.Count == 0) return;
            int count = Math.Min(3, vals.Count);
            int gap = Ui.S(18);
            int width = (card.Width - Ui.S(32) - (count - 1) * gap) / count;
            for (int i = 0; i < vals.Count; i++)
            {
                bool visible = i < count;
                Label val = vals[i];
                Label cap = val.Tag as Label;
                val.Visible = visible;
                if (cap != null) cap.Visible = visible;
                if (!visible) continue;
                int x = Ui.S(16) + i * (width + gap);
                if (cap != null) cap.SetBounds(x, Ui.S(46), width, Ui.S(17));
                val.SetBounds(x, Ui.S(66), width, Ui.S(20));
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
            if (rotulo < Ui.S(88)) rotulo = Ui.S(88);
            if (rotulo > Ui.S(128)) rotulo = Ui.S(128);

            int xVal = Ui.S(16) + rotulo + Ui.S(8);
            int larg = c.Width - xVal - Ui.S(14);
            if (larg < Ui.S(60)) larg = Ui.S(60);

            for (int i = 0; i < vals.Count; i++)
            {
                Label val = vals[i];
                Label cap = val.Tag as Label;
                int y = Ui.S(46) + i * Ui.S(24);
                if (cap != null) cap.SetBounds(Ui.S(16), y + Ui.S(2), rotulo, Ui.S(17));
                val.SetBounds(xVal, y, larg, Ui.S(19));
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
            _cardPainel1 = c1;
            c1.Title = T.Panel1;
            c1.SetBounds(0, 0, Ui.S(370), Ui.S(282));
            page.Controls.Add(c1);

            _slot1 = new SensorSlot();
            _slot1.SetBounds(Ui.S(12), Ui.S(48), Ui.S(346), Ui.S(80));
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
            _unit1.SetBounds(Ui.S(14), Ui.S(216), Ui.S(140), Ui.S(30));
            _unit1.SelectedIndexChanged += delegate { OnChanged(); };
            c1.Controls.Add(_unit1);
            c1.Controls.Add(NotaDeUnidade(14, 249));

            Card c2 = new Card();
            _cardPainel2 = c2;
            c2.Title = T.Panel2;
            c2.SetBounds(Ui.S(386), 0, Ui.S(370), Ui.S(282));
            page.Controls.Add(c2);

            _slot2 = new SensorSlot();
            _slot2.SetBounds(Ui.S(12), Ui.S(48), Ui.S(346), Ui.S(80));
            _slot2.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _slot2.Button.Click += delegate { TrocarSensor(_pick2, T.PickSensor2); };
            c2.Controls.Add(_slot2);

            c2.Controls.Add(MakeLabel(T.Scale, 14, 162 - 20, Ui.FontMed));
            _div2 = MakeDivisorRow(c2, 14, 162, 2);

            c2.Controls.Add(MakeLabel(T.Unit, 14, 196, Ui.FontMed));
            _unit2 = new Segmented();
            _unit2.SetItems("%", "W");
            _unit2.SetBounds(Ui.S(14), Ui.S(216), Ui.S(140), Ui.S(30));
            _unit2.SelectedIndexChanged += delegate { OnChanged(); };
            c2.Controls.Add(_unit2);
            c2.Controls.Add(NotaDeUnidade(14, 249));


            _panelPreviewCard = new Card();
            _panelPreviewCard.Title = T.Preview;
            _panelPreviewCard.SetBounds(0, Ui.S(294), Ui.S(756), Ui.S(532));
            page.Controls.Add(_panelPreviewCard);

            _preview = new PanelPreview();
            _preview.SetBounds(Ui.S(12), Ui.S(44), Ui.S(732), Ui.S(476));
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
                b.SetBounds(Ui.S(x + i * 68), Ui.S(y), Ui.S(64), Ui.S(26));   // x e y de projeto
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
            l.SetBounds(Ui.S(x), Ui.S(y), Ui.S(340), Ui.S(15));
            l.ForeColor = Ui.Muted;
            return l;
        }

        private static Label MakeLabel(string text, int x, int y, Font f)
        {
            Label l = new Label();
            l.Text = text; l.Font = f; l.AutoSize = false;
            l.SetBounds(Ui.S(x), Ui.S(y), Ui.S(300), Ui.S(18));   // medidas de projeto, a 96 dpi
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

            // Duas colunas: a lista, com os seis botoes de perfil embaixo e o
            // Rodizio logo abaixo dela, ja que ele vale para o perfil
            // selecionado; do outro lado a previa, com sensores, Padrao e
            // Aplicar ao lado da imagem, e o Perfil por jogo embaixo.
            Card c = new Card();
            c.Title = T.SavedProfiles;
            _profilesListCard = c;
            c.SetBounds(0, 0, Ui.S(330), Ui.S(400));
            page.Controls.Add(c);

            _profileList = new ProfileList();
            _profileList.SetBounds(Ui.S(12), Ui.S(44), Ui.S(306), Ui.S(300));
            _profileList.Resolve = NomeDoSensor;
            _profileList.ActiveName = _cfg.ActiveName;
            _profileList.DefaultName = _cfg.DefaultProfile.Name;
            _profileList.SelectionChanged += new EventHandler(OnProfileSelected);
            _profileList.ItemActivated += delegate { AplicarPerfil(); };
            c.Controls.Add(_profileList);

            _botoesDePerfil.Clear();
            BotaoDePerfil(T.New, new EventHandler(OnNewProfile));
            BotaoDePerfil(T.Rename, new EventHandler(OnRenameProfile));
            BotaoDePerfil(T.Duplicate, new EventHandler(OnDuplicateProfile));
            BotaoDePerfil(T.Export, new EventHandler(OnExportProfile));
            BotaoDePerfil(T.Import, new EventHandler(OnImportProfile));
            BotaoDePerfil(T.Delete, new EventHandler(OnDeleteProfile)).Danger = true;

            Card cv = new Card();
            cv.Title = T.ProfilePreview;
            _profilesPreviewCard = cv;
            cv.SetBounds(Ui.S(346), 0, Ui.S(410), Ui.S(400));
            page.Controls.Add(cv);

            _profilePreview = new PanelPreview();
            _profilePreview.SetBounds(Ui.S(12), Ui.S(44), Ui.S(386), Ui.S(250));
            cv.Controls.Add(_profilePreview);

            _profileSensor1 = new FlatBtn();
            _profileSensor1.SetBounds(Ui.S(12), Ui.S(306), Ui.S(189), Ui.S(34));
            _profileSensor1.Click += delegate { TrocarSensorDoPerfil(1); };
            cv.Controls.Add(_profileSensor1);

            _profileSensor2 = new FlatBtn();
            _profileSensor2.SetBounds(Ui.S(209), Ui.S(306), Ui.S(189), Ui.S(34));
            _profileSensor2.Click += delegate { TrocarSensorDoPerfil(2); };
            cv.Controls.Add(_profileSensor2);

            _btnDefault = new FlatBtn();
            _btnDefault.Text = T.SetAsDefault;
            _btnDefault.Size = new Size(Ui.S(160), Ui.S(34));
            _btnDefault.Click += delegate { DefinirPerfilPadrao(); };
            cv.Controls.Add(_btnDefault);

            _btnApply = new FlatBtn();
            _btnApply.Text = T.ApplyProfile;
            _btnApply.Primary = true;
            _btnApply.Size = new Size(Ui.S(160), Ui.S(34));
            _btnApply.Click += delegate { AplicarPerfil(); };
            cv.Controls.Add(_btnApply);


            // A automacao por jogo vem antes do rodizio: ela e uma relacao entre
            // o perfil selecionado acima e um aplicativo real, e deixa-la no fim
            // escondia a acao principal atras de uma frase com apenas a contagem.
            _cardGame = new Card();
            _cardGame.Title = T.GameProfilesCard;
            _cardGame.SetBounds(0, Ui.S(512), Ui.S(756), Ui.S(400));
            page.Controls.Add(_cardGame);

            _tgJogo = new Toggle();
            _tgJogo.Label = T.GameProfilesOn;
            _tgJogo.SetBounds(Ui.S(16), Ui.S(48), Ui.S(560), Ui.S(26));
            _tgJogo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _tgJogo.CheckedChanged += new EventHandler(OnToggleJogo);
            _cardGame.Controls.Add(_tgJogo);

            Label atual = MakeLabel(T.CurrentGame, 16, 84, Ui.FontSmall);
            atual.ForeColor = Ui.Muted;
            _cardGame.Controls.Add(atual);

            _gameIcon = new GameIconView();
            // O centro coincide com o ícone das linhas de vínculos abaixo.
            _gameIcon.SetBounds(Ui.S(24), Ui.S(108), Ui.S(52), Ui.S(52));
            _cardGame.Controls.Add(_gameIcon);

            _lbJogoNome = MakeLabel(T.NoGameToBind, 82, 106, Ui.FontMed);
            _lbJogoNome.Size = new Size(Ui.S(400), Ui.S(26));
            _lbJogoNome.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _cardGame.Controls.Add(_lbJogoNome);

            _lbJogoAtual = MakeLabel("", 82, 132, Ui.FontSmall);
            _lbJogoAtual.Size = new Size(Ui.S(400), Ui.S(22));
            _lbJogoAtual.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lbJogoAtual.ForeColor = Ui.AccentText;
            _cardGame.Controls.Add(_lbJogoAtual);

            _btVincular = new FlatBtn();
            _btVincular.Text = T.BindGame;
            _btVincular.Primary = true;
            _btVincular.SetBounds(Ui.S(510), Ui.S(106), Ui.S(230), Ui.S(34));
            _btVincular.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btVincular.Click += delegate { VincularJogo(); };
            _cardGame.Controls.Add(_btVincular);

            _btDesvincular = new FlatBtn();
            _btDesvincular.Text = T.UnbindGame;
            _btDesvincular.SetBounds(Ui.S(510), Ui.S(146), Ui.S(230), Ui.S(30));
            _btDesvincular.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btDesvincular.Click += delegate { DesvincularJogo(); };
            _cardGame.Controls.Add(_btDesvincular);

            _lbJogoNota = MakeLabel(T.GameProfilesNote, 16, 177, Ui.FontSmall);
            _lbJogoNota.Size = new Size(Ui.S(724), Ui.S(38));
            _lbJogoNota.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lbJogoNota.ForeColor = Ui.Faint;
            _cardGame.Controls.Add(_lbJogoNota);

            FlatBtn browseGame = MakeSideButton(T.GameBrowse, 16, 220, 240, delegate { BrowseGame(); });
            _gameFileButton = browseGame;
            _cardGame.Controls.Add(browseGame);
            Label browseNote = MakeLabel(T.GameFileNote, 16, 260, Ui.FontSmall);
            _gameFileNote = browseNote;
            browseNote.Size = new Size(Ui.S(724), Ui.S(50));
            browseNote.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            browseNote.ForeColor = Ui.Muted;
            _cardGame.Controls.Add(browseNote);

            Label vinculos = MakeLabel(T.GameBindings, 16, 319, Ui.FontMed);
            _gameBindingsHeading = vinculos;
            _cardGame.Controls.Add(vinculos);

            _gameBindings = new GameBindingList();
            _gameBindings.EmptyText = T.NoGameBindings;
            _gameBindings.EmptyActionText = T.GameBrowse;
            _gameBindings.EmptyActionRequested += delegate { BrowseGame(); };
            _gameBindings.SetBounds(Ui.S(16), Ui.S(346), Ui.S(724), Ui.S(138));
            _gameBindings.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _gameBindings.RemoveRequested += delegate(string key) { RemoverVinculo(key); };
            _cardGame.Controls.Add(_gameBindings);

            // O rodizio continua junto dos perfis, mas depois da automacao mais
            // frequente e visualmente mais rica.
            _cardRotation = new Card();
            _cardRotation.Title = T.Rotation;
            _cardRotation.SetBounds(0, Ui.S(924), Ui.S(756), Ui.S(150));
            page.Controls.Add(_cardRotation);

            _tgRotate = new Toggle();
            _tgRotate.Label = T.IncludeInRotation;
            _tgRotate.SetBounds(Ui.S(16), Ui.S(48), Ui.S(460), Ui.S(26));
            _tgRotate.CheckedChanged += new EventHandler(OnToggleRotate);
            _cardRotation.Controls.Add(_tgRotate);

            _rotSecs = new NumberBox();
            _rotSecs.Minimum = 2; _rotSecs.Maximum = 999;
            _rotSecs.Value = _cfg.RotateSeconds > 0 ? _cfg.RotateSeconds : 20;
            _rotSecs.SetBounds(Ui.S(16), Ui.S(84), Ui.S(110), Ui.S(32));
            _rotSecs.ValueChanged += new EventHandler(OnRotateSeconds);
            _cardRotation.Controls.Add(_rotSecs);

            _rotNote = MakeLabel("", 134, 84, Ui.FontSmall);
            _rotNote.Size = new Size(Ui.S(600), Ui.S(32));
            _rotNote.TextAlign = ContentAlignment.MiddleLeft;
            _rotNote.ForeColor = Ui.Muted;
            _cardRotation.Controls.Add(_rotNote);

            _profilesNote = MakeLabel(T.ProfilesNote, 2, 1086, Ui.FontSmall);
            _profilesNote.Size = new Size(Ui.S(750), Ui.S(40));
            _profilesNote.ForeColor = Ui.Muted;
            page.Controls.Add(_profilesNote);

            AtualizarRodizio();
            AtualizarJogo();
            return page;
        }

        /// <summary>Os seis botoes de perfil, em grade de tres por dois embaixo da lista.</summary>
        private readonly List<FlatBtn> _botoesDePerfil = new List<FlatBtn>();

        private FlatBtn BotaoDePerfil(string texto, EventHandler h)
        {
            FlatBtn b = new FlatBtn();
            b.Text = texto;
            b.Size = new Size(Ui.S(100), Ui.S(32));
            b.Click += h;
            _profilesListCard.Controls.Add(b);
            _botoesDePerfil.Add(b);
            return b;
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
            _lbJogoAtual.ForeColor = !string.IsNullOrEmpty(casado) ? Ui.AccentText : Ui.Muted;
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
            _cardGame.Height = _gameBindings.Bottom + Ui.S(16);
            if (_pgPerfis != null && _pgPerfis.Visible) { ArrangeProfiles(); return; }
            _cardRotation.Top = _cardGame.Bottom + Ui.S(12);
            if (_profilesNote != null) _profilesNote.Top = _cardRotation.Bottom + Ui.S(12);
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
            bool salvo = _session.TrySaveAll(true, out erro);
            if (!salvo)
            {
                // Aplicar significa tambem sobreviver ao fechamento. Sem disco,
                // manter o novo perfil ativo no worker daria uma troca que some
                // no proximo arranque e contradiz o proprio botao.
                _cfg.ActiveName = anterior;
                _saved = false; _dirty = true;
                FalhaAoGravar(erro);
                return;
            }
            _saved = true; _dirty = false;

            _profileList.ActiveName = p.Name;
            _selectionAtLastSave = _current;
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
                FalhaAoGravar(erro);
                return;
            }

            _saved = true; _dirty = false;
            _profileList.DefaultName = p.Name;
            _selectionAtLastSave = _current;
            _profileList.Invalidate();
            AtualizarPreviaDoPerfil();
            Aviso(T.DefaultProfileSet(p.Name));
            if (Applied != null) Applied(this, EventArgs.Empty);
        }

        private FlatBtn MakeSideButton(string text, int x, int y, int w, EventHandler h)
        {
            FlatBtn b = new FlatBtn();
            b.Text = text;
            b.SetBounds(Ui.S(x), Ui.S(y), Ui.S(w), Ui.S(32));   // medidas de projeto
            b.Click += h;
            return b;
        }

        private void RefreshProfileList()
        {
            if (_profileList == null) return;
            _loading = true;
            _profileList.ActiveName = _session.ActiveProfile.Name;
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

            string activeName = _session.ActiveProfile.Name;
            _profileList.ActiveName = activeName;
            _profileList.Invalidate();
            bool ativo = string.Equals(p.Name, activeName, StringComparison.Ordinal);
            // como o Padrao ao lado: ja ativo, o botao diz isso, curto, para caber na barra
            _btnApply.Text = ativo ? T.AlreadyActive : T.ApplyProfile;
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
                s.Value = snap.TryGetValue(s.Id, out v) && !float.IsNaN(v) && !float.IsInfinity(v)
                    ? (float?)v : null;
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
            _nav.Subtitle = _session.ActiveProfile.Name;
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
            _nav.Subtitle = _session.ActiveProfile.Name; _nav.Invalidate();
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
            _nav.Subtitle = _session.ActiveProfile.Name; _nav.Invalidate();
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
                if (Config.ExportProfile(p, d.FileName, _cfg.SavePath, out erro)) Aviso(T.Exported(p.Name));
                else Warn(T.ExportFailed(erro), T.TryAgain, delegate { OnExportProfile(null, EventArgs.Empty); });
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
                if (p == null) { Warn(T.ImportFailed(erro), T.TryAgain, delegate { OnImportProfile(null, EventArgs.Empty); }); return; }

                SaveToProfile();
                p.Name = _cfg.UniqueName(p.Name);
                _cfg.Profiles.Add(p);
                _current = p;
                _dirty = true;
                _nav.Subtitle = _session.ActiveProfile.Name; _nav.Invalidate();
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

            if (alvo == _current) { _nav.Subtitle = _session.ActiveProfile.Name; _nav.Invalidate(); }
            _dirty = true;
            RefreshProfileList();
        }

        private void OnDeleteProfile(object sender, EventArgs e)
        {
            if (_cfg.Profiles.Count <= 1) { Warn(T.KeepOneProfile); return; }
            Profile alvo = _profileList != null && _profileList.Selected != null ? _profileList.Selected : _current;
            SaveToProfile();
            Config antes = _cfg.Clone();
            string nomeAlvo = alvo.Name;

            _cfg.RemoverPerfilDosJogos(alvo.Name);
            _cfg.Profiles.Remove(alvo);
            if (_current == alvo) _current = _cfg.Profiles[0];
            if (string.Equals(_cfg.ActiveName, alvo.Name, StringComparison.Ordinal))
                _cfg.ActiveName = _cfg.Profiles[0].Name;
            if (string.Equals(_cfg.DefaultProfileName, alvo.Name, StringComparison.Ordinal))
                _cfg.DefaultProfileName = _cfg.NameExists(_cfg.ActiveName)
                    ? _cfg.ActiveName : _cfg.Profiles[0].Name;
            _cfg.EnsureDefaultProfile();

            _nav.Subtitle = _session.ActiveProfile.Name;
            _nav.Invalidate();
            _dirty = true;
            RefreshProfileList(); LoadFromProfile();
            OferecerDesfazer(T.ProfileDeleted(nomeAlvo), delegate { RestaurarPerfis(antes, nomeAlvo); });
        }

        /// <summary>Falha recuperavel: faixa no alto da janela, com a acao que resolve quando ha.</summary>
        private void Warn(string msg) { Warn(msg, null, null); }

        private void Warn(string msg, string textoDaAcao, Action acao)
        {
            if (_faixa == null || IsDisposed) { Log.Write("aviso: " + msg); return; }
            _faixaDeSensores = false;
            _faixa.Mostrar(msg, textoDaAcao, acao);
        }

        /// <summary>Confirmacao discreta no rodape, que se apaga sozinha.</summary>
        private void Aviso(string texto)
        {
            if (_footerNote == null) return;
            _footerNote.Text = texto;
            _footerNote.ForeColor = Ui.AccentText;
            _footerNote.Visible = _nav == null || _nav.Width >= Ui.S(120);

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
            if (_footerNote == null) return;
            IReadingStatusData status = _data as IReadingStatusData;
            Control page = _nav == null || _nav.Selected == null ? null : _nav.Selected.Page;
            bool sensorPage = page == _pgVisao || page == _pgMetricas || page == _pgPaineis || page == _pgPerfis;
            AtualizarFaixaDeSensores(status, sensorPage);
            if (_noteTimer != null && _noteTimer.Enabled) return;

            // Mesmo texto do dialogo de fechar, de proposito: a barra avisa e o
            // dialogo cobra a mesma coisa, e duas redacoes para o mesmo estado
            // fariam parecer dois estados.
            string readingNote = status != null && sensorPage ? ReadingStateText(status.CurrentReadingState(null)) : "";
            // So avisa quando a gravacao falhou: com gravacao automatica, edicao
            // pendente por 350 ms nao e um estado que mereca aviso.
            bool naoGravou = _dirty && _autoPausado;
            _footerNote.Text = naoGravou ? T.UnsavedTitle : readingNote;
            _footerNote.ForeColor = Ui.Warn;
            _footerNote.Visible = (naoGravou || readingNote.Length > 0) && _nav != null && _nav.Width >= Ui.S(120);
        }

        // ---------------- sensores sem resposta ----------------

        private bool _faixaDeSensores;
        private ReadingState? _estadoAvisado;

        /// <summary>
        /// Sensores sem resposta viram faixa com "Tentar de novo", em vez de so
        /// uma nota no rodape. Dispensada no X, nao volta enquanto o estado for
        /// o mesmo; reaparece se a leitura voltar e cair de novo.
        /// </summary>
        private void AtualizarFaixaDeSensores(IReadingStatusData status, bool sensorPage)
        {
            if (_faixa == null || status == null) return;
            ReadingState estado = status.CurrentReadingState(null);
            bool caiu = estado == ReadingState.Failed || estado == ReadingState.Expired;
            if (!caiu)
            {
                _estadoAvisado = null;
                if (_faixaDeSensores) { _faixa.Ocultar(); _faixaDeSensores = false; }
                return;
            }
            if (!sensorPage)
            {
                if (_faixaDeSensores) { _faixa.Ocultar(); _faixaDeSensores = false; _estadoAvisado = null; }
                return;
            }
            if (_estadoAvisado == estado || (_faixa.Visible && !_faixaDeSensores)) return;
            _estadoAvisado = estado;
            _faixaDeSensores = true;
            _faixa.Mostrar(ReadingStateText(estado), T.TryAgain, delegate { TentarSensoresDeNovo(); });
        }

        private void TentarSensoresDeNovo()
        {
            _faixaDeSensores = false;
            _estadoAvisado = null;
            if (_data == null) return;
            try
            {
                _sensors = _data.RefreshSensorList();
                _pick1.SetSensors(Clone(_sensors));
                _pick2.SetSensors(Clone(_sensors));
                _pick1.SelectedId = _current.Panel1Id;
                _pick2.SelectedId = _current.Panel2Id;
                Refresh0();
            }
            catch (Exception ex) { Log.Error("tentar sensores de novo", ex); }
        }

        // ---------------- desfazer ----------------

        private Action _desfazer;
        private Timer _desfazerTimer;

        /// <summary>
        /// Oferece "Desfazer" por 6 s no rodape depois de um gesto destrutivo.
        /// Como tudo vale na hora, sem este recurso um clique errado em apagar
        /// seria definitivo.
        /// </summary>
        private void OferecerDesfazer(string texto, Action desfazer)
        {
            _desfazer = desfazer;
            Aviso(texto);
            if (_btDesfazer != null) { _btDesfazer.Visible = true; _btDesfazer.BringToFront(); }
            if (_desfazerTimer == null)
            {
                _desfazerTimer = new Timer();
                _desfazerTimer.Interval = 6000;
                _desfazerTimer.Tick += delegate { EncerrarDesfazer(); };
            }
            _desfazerTimer.Stop();
            _desfazerTimer.Start();
        }

        private void EncerrarDesfazer()
        {
            if (_desfazerTimer != null) _desfazerTimer.Stop();
            _desfazer = null;
            if (_btDesfazer != null) _btDesfazer.Visible = false;
        }

        private void Desfazer()
        {
            Action a = _desfazer;
            EncerrarDesfazer();
            if (a == null) return;
            a();
            Aviso(T.Undone);
        }

        /// <summary>Devolve os perfis e vinculos de jogo de uma copia anterior, sem tocar no resto.</summary>
        private void RestaurarPerfis(Config antes, string selecionado)
        {
            _cfg.Profiles.Clear();
            foreach (Profile p in antes.Profiles) _cfg.Profiles.Add(p.Clone());
            _cfg.ActiveName = antes.ActiveName;
            _cfg.DefaultProfileName = antes.DefaultProfileName;
            _cfg.GameKeys.Clear(); _cfg.GameKeys.AddRange(antes.GameKeys);
            _cfg.GameProfileNames.Clear(); _cfg.GameProfileNames.AddRange(antes.GameProfileNames);
            _cfg.GameDisplayNames.Clear(); _cfg.GameDisplayNames.AddRange(antes.GameDisplayNames);
            _cfg.GamePaths.Clear(); _cfg.GamePaths.AddRange(antes.GamePaths);
            Profile alvo = null;
            foreach (Profile p in _cfg.Profiles) if (p.Name == selecionado) alvo = p;
            _current = alvo ?? _cfg.Profiles[0];
            if (antes.ActiveName == selecionado) _selectionAtLastSave = null;   // o proximo gravar reativa
            _nav.Subtitle = _session.ActiveProfile.Name;
            _nav.Invalidate();
            _dirty = true;
            RefreshProfileList(); LoadFromProfile();
            AtualizarJogo();
        }

        private void RestaurarMetricas(Config antes)
        {
            _cfg.MetricIds.Clear(); _cfg.MetricIds.AddRange(antes.MetricIds);
            _cfg.MetricSizes.Clear(); _cfg.MetricSizes.AddRange(antes.MetricSizes);
            _cfg.MetricRange = antes.MetricRange;
            _cfg.MetricsChosen = antes.MetricsChosen;
            _cfg.MetricProfiles.Clear();
            foreach (MetricProfile p in antes.MetricProfiles) _cfg.MetricProfiles.Add(p.Clone());
            if (!GravarMetricas()) return;
            MontarMetricas();
            if (Applied != null) Applied(this, EventArgs.Empty);
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
            _itensDaConfig.Clear();

            // Linhas no estilo das Configuracoes do Windows 11, em quatro grupos.
            // Nenhuma opcao saiu nem mudou de comportamento: cada uma ganhou uma
            // faixa propria, com a descricao ao lado de onde se mexe.
            GrupoDaConfig(page, T.SettingsGeneral);

            _lang = new Segmented();
            _lang.SetItems("Português (BR)", "English (US)");
            _lang.Size = new Size(Ui.S(300), Ui.S(32));
            _lang.SelectedIndex = T.Pt ? 0 : 1;
            // depois de fixar a posicao: o seletor avisa toda mudanca, e a
            // primeira seria a nossa - reabriria a janela ao abrir a janela
            _lang.SelectedIndexChanged += new EventHandler(OnLanguageChanged);
            // E774 e o globo da Segoe MDL2. ESCAPADO, como todos.
            LinhaDaConfig(page, new LinhaDeOpcao("\uE774", T.Language_, T.LanguageNote)).Direita(_lang);

            _tgAutostart = new Toggle();
            _tgAutostart.Checked = Autostart.IsEnabled();
            _tgAutostart.CheckedChanged += new EventHandler(OnToggleAutostart);
            // E7E8: botao de ligar
            LinhaDaConfig(page, new LinhaDeOpcao("\uE7E8", T.StartWithWindows, T.StartWithWindowsNote)).Interruptor(_tgAutostart, true);

            GrupoDaConfig(page, T.PanelLabel);

            Toggle tgAll = new Toggle();
            tgAll.Checked = _cfg.ShowAllSensors;
            tgAll.CheckedChanged += new EventHandler(OnToggleShowAll);
            // E8FD: lista com marcadores
            LinhaDaConfig(page, new LinhaDeOpcao("\uE8FD", T.ShowAllSensors, T.ShowAllShort)).Interruptor(tgAll, true);

            _tgIdle = new Toggle();
            _tgIdle.Checked = _cfg.IdleBlankMinutes > 0;
            _tgIdle.CheckedChanged += new EventHandler(OnToggleIdle);

            _idleMin = new NumberBox();
            _idleMin.Minimum = 1; _idleMin.Maximum = 999;
            _idleMin.Value = _cfg.IdleBlankMinutes > 0 ? _cfg.IdleBlankMinutes : 15;
            _idleMin.Size = new Size(Ui.S(110), Ui.S(32));
            _idleMin.Enabled = _tgIdle.Checked;
            _idleMin.ValueChanged += new EventHandler(OnIdleMinutes);

            // "minutos parado ate apagar" ou "desligado": quem diz o estado da
            // linha e este rotulo, entao o interruptor dela vai sem o seu.
            _idleNote = MakeLabel("", 0, 0, Ui.FontSmall);
            _idleNote.Size = new Size(Math.Max(TextRenderer.MeasureText(T.MinutesIdle, Ui.FontSmall).Width,
                                               TextRenderer.MeasureText(T.Off, Ui.FontSmall).Width) + Ui.S(6), Ui.S(32));
            _idleNote.TextAlign = ContentAlignment.MiddleLeft;
            _idleNote.ForeColor = Ui.Muted;

            // E708: lua, o "nao perturbe" da Segoe MDL2
            LinhaDeOpcao ocioso = LinhaDaConfig(page, new LinhaDeOpcao("\uE708", T.BlankWhenIdle, T.BlankWhenIdleShort));
            ocioso.Direita(_idleMin);
            ocioso.Direita(_idleNote);
            ocioso.Interruptor(_tgIdle, false);
            _dicaDaConfig.SetToolTip(ocioso.Descricao, T.BlankWhenIdleNote);

            // A fonte de FPS tem grupo proprio: e a unica leitura do aplicativo
            // que depende de um programa de terceiro, e sem um lugar que diga o
            // estado, "escolhi FPS e nao aparece nada" nao tem onde ser
            // respondido.
            GrupoDaConfig(page, T.SettingsFps);

            _rtssEstado = LinhaDeOpcao.Rotulo("", Ui.FontSmall, Ui.Muted);
            // E7FC: controle de videogame
            LinhaDeOpcao rtss = LinhaDaConfig(page, new LinhaDeOpcao("\uE7FC", "RivaTuner Statistics Server", _rtssEstado));
            _dicaDaConfig.SetToolTip(rtss.Titulo, T.RtssNote);

            _btRtss = BotaoDaLinha(T.InstallRtss, delegate { InstalarRtss(); });
            rtss.Direita(_btRtss);
            rtss.Direita(BotaoDaLinha(T.RtssPage, delegate { AbrirNoNavegador(Rtss.Site); }));
            rtss.Direita(BotaoDaLinha(T.Recheck, delegate { AtualizarRtss(); }));

            GrupoDaConfig(page, T.SettingsData);

            Label onde = LinhaDeOpcao.Rotulo(Paths.DataDir, Ui.FontSmall, Ui.Muted);
            onde.Name = "dataFolder";
            onde.AutoEllipsis = true;
            _dicaDaConfig.SetToolTip(onde, Paths.DataDir);
            // ED25: pasta aberta
            LinhaDaConfig(page, new LinhaDeOpcao("\uED25", T.DataFolderTitle, onde)).Direita(BotaoDaLinha(T.OpenFolder, delegate
            {
                try { System.Diagnostics.Process.Start("explorer.exe", Paths.DataDir); }
                catch (Exception ex) { Log.Error("abrir pasta de dados", ex); }
            }));

            AddDataTools(page);

            AtualizarRtss();
            AtualizarOcioso();
            return page;
        }

        /// <summary>Ordem de cima para baixo da pagina de Configuracoes: titulos de grupo, linhas e a nota final.</summary>
        private readonly List<Control> _itensDaConfig = new List<Control>();
        private readonly ToolTip _dicaDaConfig = new ToolTip();

        private void GrupoDaConfig(Control page, string titulo)
        {
            Label l = MakeLabel(titulo, 0, 0, Ui.FontSection);
            l.Tag = "grupo";
            l.Height = Ui.S(24);
            page.Controls.Add(l);
            _itensDaConfig.Add(l);
        }

        private LinhaDeOpcao LinhaDaConfig(Control page, LinhaDeOpcao linha)
        {
            page.Controls.Add(linha);
            _itensDaConfig.Add(linha);
            return linha;
        }

        /// <summary>Botao na largura do proprio texto, para caber na linha ao lado de outros.</summary>
        private static FlatBtn BotaoDaLinha(string texto, EventHandler h)
        {
            FlatBtn b = new FlatBtn();
            b.Text = texto;
            b.Size = new Size(TextRenderer.MeasureText(texto, Ui.FontBase).Width + Ui.S(32), Ui.S(32));
            b.Click += h;
            return b;
        }

        private Label _rtssEstado;
        private FlatBtn _btRtss;

        private void AtualizarRtss()
        {
            if (_rtssEstado == null) return;

            _lastRtssCheckUtc = DateTime.UtcNow;
            bool ok = Rtss.Presente();
            _rtssEstado.Text = ok ? T.RtssActive : T.RtssAbsent;
            _rtssEstado.ForeColor = ok ? Ui.AccentText : Ui.Warn;

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
                    new System.Diagnostics.ProcessStartInfo(
                        System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/s /k \"" + linha + "\"");
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
        private NavegadorDeJanela _navJanela;

        /// <summary>Fim do trecho escolhido no navegador; sobrevive a remontagem da grade.</summary>
        private long _navFim = 0;
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
            // ContextMenuStrip com o renderizador do tema (MenuTema): o menu
            // nativo nao aceitava cor, fonte nem raio.
            ContextMenuStrip m = MenuTema.Novo();

            foreach (MetricPicker.Conjunto c in MetricPicker.Conjuntos)
            {
                MetricPicker.Conjunto alvo = c;   // copia local para o delegate
                m.Items.Add(MenuTema.Item(c.Nome, delegate { AplicarConjunto(alvo); }));
            }

            MostrarMenu(m, _btPadraoMetrica);
        }

        private ContextMenuStrip _menuAberto;

        /// <summary>
        /// Mostra o menu sob o botao. O anterior so e descartado agora: o
        /// clique numa opcao chega depois do fechamento, e descartar o menu no
        /// Closed perderia a acao.
        /// </summary>
        private void MostrarMenu(ContextMenuStrip m, Control ancora)
        {
            if (_menuAberto != null) _menuAberto.Dispose();
            _menuAberto = m;
            m.Show(ancora, new Point(0, ancora.Height));
        }

        /// <summary>
        /// Perfis de métricas ficam num menu hierárquico: a primeira ação cria
        /// um novo e cada perfil existente concentra aplicar/atualizar/renomear/
        /// excluir sem ocupar permanentemente a barra da grade.
        /// </summary>
        private void MenuDePerfisDeMetricas()
        {
            ContextMenuStrip menu = MenuTema.Novo();
            menu.Items.Add(MenuTema.Item(T.SaveMetricProfile,
                delegate { SalvarPerfilDeMetricas(); }));

            if (_cfg.MetricProfiles.Count > 0) MenuTema.Separador(menu.Items);
            foreach (MetricProfile item in _cfg.MetricProfiles)
            {
                MetricProfile profile = item;
                ToolStripMenuItem group = MenuTema.Submenu(profile.Name);
                group.DropDownItems.Add(MenuTema.Item(T.Apply,
                    delegate { AplicarPerfilDeMetricas(profile); }));
                group.DropDownItems.Add(MenuTema.Item(T.UpdateMetricProfile,
                    delegate { AtualizarPerfilDeMetricas(profile); }));
                group.DropDownItems.Add(MenuTema.Item(T.RenameMetricProfile,
                    delegate { RenomearPerfilDeMetricas(profile); }));
                MenuTema.Separador(group.DropDownItems);
                group.DropDownItems.Add(MenuTema.Item(T.DeleteMetricProfile,
                    delegate { ExcluirPerfilDeMetricas(profile); }));
                menu.Items.Add(group);
            }
            MostrarMenu(menu, _btPerfisMetrica);
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
            Config antes = _cfg.Clone();
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
            OferecerDesfazer(T.MetricProfileApplied(profile.Name), delegate { RestaurarMetricas(antes); });
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
            Config antes = _cfg.Clone();
            string nome = profile.Name;
            int index = _cfg.MetricProfiles.IndexOf(profile);
            if (index < 0) return;
            _cfg.MetricProfiles.RemoveAt(index);
            if (!GravarMetricas()) { _cfg.MetricProfiles.Insert(index, profile); return; }
            OferecerDesfazer(T.MetricProfileDeleted(nome), delegate { RestaurarMetricas(antes); });
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

            Config antes = _cfg.Clone();
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

            OferecerDesfazer(c != null ? T.PresetApplied(c.Nome, escolhidos.Count) : (T.Pt ? "Grade automática aplicada" : "Automatic grid applied"), delegate { RestaurarMetricas(antes); });
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
                    target.MetricNavId = draft.MetricNavId;
                    target.MetricProfiles.Clear();
                    foreach (MetricProfile profile in draft.MetricProfiles)
                        target.MetricProfiles.Add(profile.Clone());
                }, out erro);
                if (!salvo) { _dirty = true; FalhaAoGravar(erro); }
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
            foreach (Control c in _pgMetricas.Controls) if (!(c is ScrollFina)) velhos.Add(c);
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
            _navJanela = new NavegadorDeJanela();
            _navJanela.Janela = MetricHistory.JanelaValida(_cfg.MetricRange);
            _navJanela.FimBalde = _navFim;
            _navJanela.SensorId = LeituraDoNavegador();
            _navJanela.Mudou += delegate { TrocarJanela(); };
            _navJanela.Confirmado += delegate { GravarJanela(); };
            _pgMetricas.Controls.Add(_navJanela);

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
                MetricCard c = new MetricCard();
                // Um sensor ausente continua configurado e pode voltar a responder.
                // Inclui-lo na grade evita perde-lo ao persistir a ordem do arraste.
                c.SensorId = id;
                c.Titulo = s == null ? T.SensorUnavailable : MetricPicker.Rotulo(s);
                c.Sub = s == null ? id : MetricPicker.Rodape(s);
                c.Unidade = s == null ? "" : s.EffectiveUnit;
                c.Janela = janela;
                c.FimBalde = _navFim;
                float? at, pe;
                MetricPicker.Faixas(c.Unidade, c.SensorId, out at, out pe);
                c.Atencao = at; c.Perigo = pe;

                string alvo = id;
                c.Remover += delegate { MexerNaMetrica(alvo, 0); };
                c.MoverEsquerda += delegate { MexerNaMetrica(alvo, -1); };
                c.MoverDireita += delegate { MexerNaMetrica(alvo, +1); };
                c.TrocarTamanho += delegate { RedimensionarMetrica(alvo); };

                // Copia local: o delegate guarda a variavel do laco, e nao o
                // valor dela no instante em que foi escrito.
                MetricCard esteCartao = c;
                c.Arrastar += delegate { ComecarArrasteDeCartao(esteCartao); };
                c.Escolhido += delegate { EscolherLeituraDoNavegador(alvo); };
                c.Selecionado = id == _navJanela.SensorId;

                _pgMetricas.Controls.Add(c);
                _cards.Add(c);
                _cardIds.Add(id);
                _cardTamanhos.Add(_cfg.MetricSize(i));
            }

            // Quem alimenta a serie e a thread de leitura, que roda com a janela
            // fechada: e ela que precisa saber o que acompanhar.
            // A janela nao e dona das series de outros consumidores. A poda
            // fica com o runtime, ao fechar a sessao de edicao.
            MetricHistory.SeguirTambem(_cfg.MetricIds);

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
                int Esp = Ui.S(12);

                // A largura da barra de rolagem sai da conta SEMPRE, apareca ela
                // ou nao. Medir a area de cliente faria a grade encolher quando
                // a barra surge e alargar quando ela some, e cada troca dispara
                // um novo arranjo - dois estados que se chamam em circulo.
                int disp = _pgMetricas.Width - Pagina.LarguraDaBarra - Ui.S(4);
                if (disp < Ui.S(320)) disp = Ui.S(320);

                int porLinha = disp / Ui.S(200);
                if (porLinha < 4) porLinha = 4;
                if (porLinha > 12) porLinha = 12;
                porLinha -= porLinha % 4;

                // Mantem a distribuicao anterior quando cabe; reduz colunas
                // apenas quando os 140 px minimos ultrapassariam a pagina.
                porLinha = Math.Min(porLinha, Math.Max(1, (disp - Ui.S(4) + Esp) / (Ui.S(140) + Esp)));
                int larg = (disp - Ui.S(4) - (porLinha - 1) * Esp) / porLinha;
                int x = Ui.S(2), y = 0;
                foreach (Control button in new Control[] { _btAddMetrica, _btPadraoMetrica, _btPerfisMetrica })
                {
                    if (button == null) continue;
                    int width = Ui.S(150);
                    if (x + width > disp) { x = Ui.S(2); y += Ui.S(40); }
                    button.SetBounds(x, y, width, Ui.S(32));
                    x += width + Ui.S(6);
                }
                if (_lbDicaMetricas != null)
                {
                    int w = disp - x - Ui.S(6);
                    _lbDicaMetricas.Visible = w >= Ui.S(150);
                    _lbDicaMetricas.SetBounds(w >= Ui.S(150) ? x + Ui.S(6) : Ui.S(2), y + Ui.S(4), w >= Ui.S(150) ? w : 1, w >= Ui.S(150) ? Ui.S(30) : 1);
                }
                y += Ui.S(44);

                if (_navJanela != null)
                {
                    _navJanela.SetBounds(Ui.S(2), y, disp - Ui.S(4), Ui.S(72));
                    y += Ui.S(72) + Esp;
                }

                if (_lbSemMetricas != null) _lbSemMetricas.SetBounds(Ui.S(4), y + Ui.S(8), disp - Ui.S(8), Ui.S(40));

                int col = 0, alturaDaLinha = 0;
                for (int i = 0; i < _cards.Count; i++)
                {
                    int tam = i < _cardTamanhos.Count ? _cardTamanhos[i] : 0;
                    int cols = Math.Min(porLinha, MetricPicker.Colunas(tam));
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
                        _cards[i].SetBounds(Ui.S(2) + col * (larg + Esp), y,
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

        /// <summary>
        /// Troca a janela de tempo sem remontar a grade.
        ///
        /// A serie nao esta nos cartoes, entao mudar a escala e so redesenhar -
        /// nada do que foi registrado se perde ao alternar entre dez minutos e
        /// seis horas.
        /// </summary>
        private void TrocarJanela()
        {
            if (_navJanela == null) return;
            _navFim = _navJanela.FimBalde;
            foreach (MetricCard c in _cards)
            {
                c.Janela = _navJanela.Janela;
                c.FimBalde = _navJanela.FimBalde;
                c.Invalidate();
            }
            // A Visao geral fica de fora de proposito: tem janela propria,
            // curta, para os picos aparecerem nos blocos estreitos.
        }

        /// <summary>
        /// A leitura que a faixa do navegador desenha: a ultima escolhida, se o
        /// cartao dela ainda existe, ou o primeiro da grade.
        ///
        /// Antes era sempre o primeiro, e clicar no grafico da GPU deixava a
        /// faixa mostrando a CPU: a forma das seis horas ali em cima nao era a
        /// do grafico que se estava olhando.
        /// </summary>
        private string LeituraDoNavegador()
        {
            if (_cfg.MetricIds.Count == 0) return "";
            if (!string.IsNullOrEmpty(_cfg.MetricNavId) && _cfg.MetricIds.Contains(_cfg.MetricNavId))
                return _cfg.MetricNavId;
            // o cartao escolhido foi removido: esquece, para nao voltar marcado
            // se um dia a mesma leitura for adicionada de novo
            _cfg.MetricNavId = "";
            return _cfg.MetricIds[0];
        }

        /// <summary>
        /// Clique num cartao: a faixa passa a desenhar a leitura dele, na cor
        /// dele, e o cartao fica marcado. O trecho escolhido nao muda.
        /// </summary>
        private void EscolherLeituraDoNavegador(string id)
        {
            if (_navJanela == null || string.IsNullOrEmpty(id)) return;
            foreach (MetricCard c in _cards) c.Selecionado = c.SensorId == id;
            if (_navJanela.SensorId != id)
            {
                _navJanela.SensorId = id;
                AtualizarCorDoNavegador();
                _navJanela.Invalidate();
            }
            if (_cfg.MetricNavId == id) return;
            _cfg.MetricNavId = id;
            GravarMetricas();
        }

        private void AtualizarCorDoNavegador()
        {
            if (_navJanela == null) return;
            Color? cor = null;
            foreach (MetricCard c in _cards)
                if (c.SensorId == _navJanela.SensorId) { cor = c.CorAtual; break; }
            _navJanela.CorDaLinha = cor;
        }

        /// <summary>
        /// Grava o tamanho escolhido ao soltar o arraste, e nao a cada pixel.
        /// A posicao no tempo nao e gravada: reabrir a janela volta ao vivo.
        /// </summary>
        private void GravarJanela()
        {
            if (_navJanela == null || _cfg.MetricRange == _navJanela.Janela) return;
            _cfg.MetricRange = _navJanela.Janela;
            GravarMetricas();
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
            IReadingStatusData status = _data as IReadingStatusData;
            for (int i = 0; i < _cards.Count; i++)
            {
                string id = _cardIds[i];
                SensorEntry sensor = Achar(id);
                MetricCard card = _cards[i];
                string title = sensor == null ? T.SensorUnavailable : MetricPicker.Rotulo(sensor);
                string footer = sensor == null ? id : MetricPicker.Rodape(sensor);
                string unit = sensor == null ? "" : sensor.EffectiveUnit;
                if (id.StartsWith(Rtss.Prefixo, StringComparison.Ordinal)) footer = MetricPicker.RodapeJogos();
                if (status != null && sensor != null)
                {
                    ReadingState state = status.CurrentReadingState(id);
                    if (state != ReadingState.Live && !(state == ReadingState.Waiting &&
                        id.StartsWith(Rtss.Prefixo, StringComparison.Ordinal) && string.IsNullOrEmpty(Rtss.JogoAtual)))
                        footer = ReadingStateText(state);
                }
                if (card.Titulo != title || card.Sub != footer || card.Unidade != unit)
                {
                    card.Titulo = title; card.Sub = footer; card.Unidade = unit;
                    float? attention, danger; MetricPicker.Faixas(unit, id, out attention, out danger);
                    card.Atencao = attention; card.Perigo = danger; card.Invalidate();
                }
                float value;
                card.Push(snap.TryGetValue(id, out value) ? (float?)value : null);
            }
            if (_navJanela != null && _navJanela.Visible)
            {
                // a cor do cartao muda com o valor (atencao, perigo); a faixa acompanha
                AtualizarCorDoNavegador();
                _navJanela.Invalidate();
            }
        }

        private static string ReadingStateText(ReadingState state)
        {
            switch (state)
            {
                case ReadingState.Paused: return T.ReadingsPaused;
                case ReadingState.Failed: return T.ReadingsFailed;
                case ReadingState.Expired: return T.ReadingsExpired;
                case ReadingState.Waiting: return T.ReadingsWaiting;
                default: return "";
            }
        }

        private Control BuildPageSobre()
        {
            Pagina page = new Pagina();
            // Continua rolando: identidade, situacao e isencao passam da altura
            // de uma janela comum, e cortar a isencao para caber seria trocar o
            // que importa pelo que cabe.
            page.RolarNaVertical();
            _pgSobre = page;

            // Cartao de identidade: icone grande, nome, versao, a frase do que o
            // programa faz e o caminho para o codigo. A versao nao aparecia em
            // lugar nenhum da janela, e e a primeira coisa que se pergunta
            // quando alguem relata um problema.
            _cartaoIdentidade = new CartaoDeIdentidade();
            _cartaoIdentidade.Icone = ImagemDoIcone();
            page.Controls.Add(_cartaoIdentidade);

            _sobreFrase = MakeLabel(T.AboutTagline, 0, 0, Ui.FontBase);
            _sobreFrase.ForeColor = Ui.Muted;
            _cartaoIdentidade.Controls.Add(_sobreFrase);

            _sobreGitHub = new FlatBtn();
            _sobreGitHub.Text = T.OpenOnGitHub;
            _sobreGitHub.Primary = true;
            _sobreGitHub.Size = new Size(TextRenderer.MeasureText(T.OpenOnGitHub, Ui.FontBase).Width + Ui.S(36), Ui.S(32));
            _sobreGitHub.Click += delegate { Abrir(Repositorio); };
            _cartaoIdentidade.Controls.Add(_sobreGitHub);

            _sobreAutor = MakeLabel(T.CreatedBy + "  ·  " + Repositorio.Replace("https://", ""), 0, 0, Ui.FontSmall);
            _sobreAutor.ForeColor = Ui.Muted;
            _sobreAutor.AutoEllipsis = true;
            _sobreAutor.TextAlign = ContentAlignment.MiddleLeft;
            _cartaoIdentidade.Controls.Add(_sobreAutor);

            // Tres blocos de situacao: qual painel respondeu, quais fontes de
            // sensores responderam e de quem e o codigo que roda junto.
            _blocoPainel = new BlocoDeStatus();
            _blocoPainel.Rotulo = T.PanelLabel;
            page.Controls.Add(_blocoPainel);

            _blocoFontes = new BlocoDeStatus();
            _blocoFontes.Rotulo = T.SensorSources;
            page.Controls.Add(_blocoFontes);

            _blocoBibliotecas = new BlocoDeStatus();
            _blocoBibliotecas.Rotulo = T.Libraries;
            _blocoBibliotecas.Valor = "MIT  ·  MPL 2.0";
            _blocoBibliotecas.Apoio = T.LibsNote.Replace("\n", " ");
            page.Controls.Add(_blocoBibliotecas);

            // Sem o motor do HWiNFO, o aviso inteiro fica a mostra embaixo dos
            // blocos: antes perder o HWiNFO so aparecia no log, e o aplicativo
            // seguia sem temperatura nem potencia da CPU sem dizer por que.
            _sobreAvisoMotor = MakeLabel(T.EngineMissing, 0, 0, Ui.FontSmall);
            _sobreAvisoMotor.ForeColor = Ui.Warn;
            page.Controls.Add(_sobreAvisoMotor);

            AtualizarSituacaoDoSobre();

            Card cd = new Card();
            cd.Title = T.DisclaimerTitle;
            _cartaoIsencao = cd;
            page.Controls.Add(cd);

            // As quebras de linha do texto foram feitas para a largura antiga;
            // os paragrafos ficam e o resto reflui na largura da janela.
            string isencao = System.Text.RegularExpressions.Regex.Replace(T.Disclaimer, @"(?<!\n)\n(?!\n)", " ");
            _sobreIsencao = MakeLabel(isencao, 16, 46, Ui.FontSmall);
            _sobreIsencao.ForeColor = Ui.Muted;
            cd.Controls.Add(_sobreIsencao);

            return page;
        }

        private Pagina _pgSobre;
        private CartaoDeIdentidade _cartaoIdentidade;
        private Label _sobreFrase, _sobreAutor, _sobreAvisoMotor, _sobreIsencao;
        private FlatBtn _sobreGitHub;
        private BlocoDeStatus _blocoPainel, _blocoFontes, _blocoBibliotecas;
        private Card _cartaoIsencao;

        /// <summary>O icone em 128 px, e nao o da barra de tarefas esticado: a 88 px o de 32 borra.</summary>
        private static Image ImagemDoIcone()
        {
            Image grande = Assets.AppImage(128);
            if (grande != null) return grande;
            try { return Assets.AppIcon != null ? Assets.AppIcon.ToBitmap() : null; }
            catch (Exception ex) { Log.Error("icone do Sobre", ex); return null; }
        }

        /// <summary>
        /// Painel e fontes de sensores, contados de novo a cada arranjo da pagina.
        ///
        /// As fontes saem da propria lista de sensores em vez de perguntar ao
        /// motor: e a mesma informacao, vinda de onde ela e verdade - se a fonte
        /// nao publicou nada, ela nao esta ali, independentemente do que a camada
        /// de baixo ache que abriu.
        /// </summary>
        private void AtualizarSituacaoDoSobre()
        {
            if (_blocoPainel == null) return;
            string id = HidPanel.UltimoIdentificado;
            bool achou = !string.IsNullOrEmpty(id);
            _blocoPainel.Valor = achou ? id : T.PanelNone;
            _blocoPainel.Ponto = achou ? (Color?)PeripheralStrip.Verde : Ui.Faint;
            _blocoPainel.Apoio = !achou ? "" : id == "VID 1A2C / PID 4984" ? T.PanelTested : T.PanelUntested;

            int hw = 0, lhm = 0;
            foreach (SensorEntry s in _sensors)
                if (s.Source == "HWiNFO") hw++; else lhm++;
            _blocoFontes.Valor = T.SensorsFrom("HWiNFO", hw);
            _blocoFontes.CorDoValor = hw == 0 ? (Color?)Ui.Warn : null;
            _blocoFontes.Ponto = hw == 0 ? Ui.Warn : PeripheralStrip.Verde;
            _blocoFontes.Apoio = T.SensorsFrom("LibreHardwareMonitor", lhm);
            _sobreAvisoMotor.Visible = hw == 0;

            _blocoPainel.Invalidate();
            _blocoFontes.Invalidate();
        }

        /// <summary>
        /// Identidade em cima, os tres blocos lado a lado (empilhados em janela
        /// estreita), o aviso do motor quando falta e a isencao inteira no fim.
        /// </summary>
        private void ArranjarSobre()
        {
            if (_pgSobre == null || !_pgSobre.Visible) return;
            AtualizarSituacaoDoSobre();
            int disp = _pgSobre.Width - Pagina.LarguraDaBarra - Ui.S(4);
            int width = Math.Max(Ui.S(300), Math.Min(LarguraMaxDaPagina, disp));
            int x = Math.Max(0, (disp - width) / 2);

            // identidade
            int col = CartaoDeIdentidade.ColunaDoTexto;
            int texto = Math.Max(Ui.S(120), width - col - CartaoDeIdentidade.Pad);
            int y = CartaoDeIdentidade.Pad + Ui.S(38);
            int hFrase = _sobreFrase.GetPreferredSize(new Size(texto, 0)).Height;
            _sobreFrase.SetBounds(col, y, texto, hFrase);
            y = _sobreFrase.Bottom + Ui.S(14);
            _sobreGitHub.Location = new Point(col, y);
            _sobreAutor.SetBounds(_sobreGitHub.Right + Ui.S(14), y, Math.Max(Ui.S(40), width - _sobreGitHub.Right - Ui.S(14) - CartaoDeIdentidade.Pad), Ui.S(32));
            int alturaId = Math.Max(CartaoDeIdentidade.Pad * 2 + CartaoDeIdentidade.LadoDoIcone, _sobreGitHub.Bottom + CartaoDeIdentidade.Pad);
            _cartaoIdentidade.SetBounds(x, 0, width, alturaId);
            y = _cartaoIdentidade.Bottom + Ui.S(12);

            // blocos
            BlocoDeStatus[] blocos = { _blocoPainel, _blocoFontes, _blocoBibliotecas };
            if (width >= Ui.S(580))
            {
                int larg = (width - 2 * Ui.S(12)) / 3;
                int alto = 0;
                foreach (BlocoDeStatus b in blocos) alto = Math.Max(alto, b.AlturaPara(larg));
                for (int i = 0; i < 3; i++)
                    blocos[i].SetBounds(x + i * (larg + Ui.S(12)), y, i == 2 ? width - 2 * (larg + Ui.S(12)) : larg, alto);
                y += alto + Ui.S(12);
            }
            else
                foreach (BlocoDeStatus b in blocos)
                {
                    b.SetBounds(x, y, width, b.AlturaPara(width));
                    y = b.Bottom + Ui.S(12);
                }

            if (_sobreAvisoMotor.Visible)
            {
                _sobreAvisoMotor.SetBounds(x + Ui.S(2), y - Ui.S(4), width - Ui.S(4), _sobreAvisoMotor.GetPreferredSize(new Size(width - Ui.S(4), 0)).Height);
                y = _sobreAvisoMotor.Bottom + Ui.S(12);
            }

            // isencao
            int w = width - Ui.S(32);
            _sobreIsencao.SetBounds(Ui.S(16), Ui.S(46), w, _sobreIsencao.GetPreferredSize(new Size(w, 0)).Height);
            _cartaoIsencao.SetBounds(x, y, width, _sobreIsencao.Bottom + Ui.S(16));

            _pgSobre.Sincronizar();
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
                FalhaAoGravar(erro);
                return;
            }
            _saved = true; _dirty = false;
            T.Language = alvo;
            if (Applied != null) Applied(this, EventArgs.Empty);

            DialogResult = DialogResult.Retry;   // quem abriu reabre
            Close();
        }

        private const string Repositorio = "https://github.com/Feurrado/MhiagosControl";

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
                FalhaAoGravar(erro);
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
            // na largura do texto de agora, para o interruptor ficar colado nele
            _idleNote.Width = TextRenderer.MeasureText(_idleNote.Text, _idleNote.Font).Width + Ui.S(8);
            ArrangeSettings();
        }

        private void OnToggleAutostart(object sender, EventArgs e)
        {
            if (_loading) return;
            bool target = _tgAutostart.Checked;
            bool ok = target ? Autostart.Enable() : Autostart.Disable();
            if (!ok)
            {
                Warn(Autostart.UltimaRecusa ?? T.AutostartFailed + Log.Path, T.TryAgain, delegate { _tgAutostart.Checked = target; });
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
            // Minimizada, ninguem ve: os valores voltam no primeiro tick
            // depois de restaurar.
            if (WindowState == FormWindowState.Minimized) return;
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
            string activeName = _session.ActiveProfile.Name;
            if (_nav != null && _nav.Subtitle != activeName)
            {
                _nav.Subtitle = activeName;
                _nav.Invalidate();
            }
            Control ativa = _nav != null && _nav.Selected != null ? _nav.Selected.Page : null;
            bool usaSensores = ativa == _pgVisao || ativa == _pgMetricas
                            || ativa == _pgPaineis || ativa == _pgPerfis;
            Dictionary<string, float> snap = usaSensores && _data != null
                ? _data.CurrentSnapshot() : null;

            AtualizarRodape();
            if (snap != null) AtualizarValores(snap);

            if (snap != null && ativa == _pgVisao)
                AtualizarVisaoGeral(snap);

            if (ativa == _pgBaterias) AtualizarBaterias();

            if (snap != null && ativa == _pgMetricas)
                AtualizarMetricas(snap);

            if (snap != null && ativa == _pgPaineis)
            {
                _pick1.UpdateValues(snap);
                _pick2.UpdateValues(snap);
                Refresh0();
            }

            if (ativa == _pgPerfis)
            {
                AtualizarPreviaDoPerfil();
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
                f.ClientSize = new Size(Ui.S(380), Ui.S(130));
                f.BackColor = Ui.Window;
                f.Font = Ui.FontBase;

                Label l = MakeLabel(message, 16, 18, Ui.FontBase);
                l.Size = new Size(Ui.S(348), Ui.S(18));

                TextBox t = new TextBox();
                t.Text = initial;
                t.SetBounds(Ui.S(16), Ui.S(44), Ui.S(348), Ui.S(24));
                t.BorderStyle = BorderStyle.FixedSingle;
                t.BackColor = Ui.SurfaceAlt; t.ForeColor = Ui.Text;

                FlatBtn ok = new FlatBtn();
                ok.Text = T.Ok; ok.Primary = true;
                ok.SetBounds(Ui.S(172), Ui.S(86), Ui.S(90), Ui.S(30));
                ok.Click += delegate { f.DialogResult = DialogResult.OK; f.Close(); };

                FlatBtn ca = new FlatBtn();
                ca.Text = T.Cancel;
                ca.SetBounds(Ui.S(272), Ui.S(86), Ui.S(92), Ui.S(30));
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
            bool activateSelection = !ReferenceEquals(_current, _selectionAtLastSave);
            if (activateSelection) _cfg.ActiveName = _current.Name;
            string erro;
            _salvando = true;
            try
            {
                if (!_session.TrySaveAll(activateSelection, out erro))
                {
                    _dirty = true;
                    FalhaAoGravar(erro);
                    return;
                }
                _saved = true;
                _autoPausado = false;
                _dirty = false;
            }
            finally { _salvando = false; }
            _selectionAtLastSave = _current;

            if (_profileList != null)
            {
                _profileList.ActiveName = _cfg.ActiveName;
                _profileList.Invalidate();
                AtualizarPreviaDoPerfil();
            }
            if (Applied != null) Applied(this, EventArgs.Empty);
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
            if (_dirty && DialogResult != DialogResult.Retry && !_semPerguntar && !_fechamentoDecidido)
            {
                // Sem pergunta: a edicao ja vale sozinha, so resta o que ainda
                // esperava o atraso de gravacao. Falha de disco mantem a janela
                // aberta, porque fechar descartaria exatamente o que nao gravou.
                _autoPausado = false;
                GravarPendente();
                if (_dirty) { e.Cancel = true; base.OnFormClosing(e); return; }
            }

            GuardarTamanho();

            // OK significa "ha gravacao valida no disco"; quem abriu usa isso
            // para decidir se recarrega a configuracao e descarta a memoria
            if (DialogResult != DialogResult.Retry)
                DialogResult = _saved ? DialogResult.OK : DialogResult.Cancel;

            base.OnFormClosing(e);

            // A escolha vale para o resto deste fechamento. O Application.Exit
            // que vem depois de Sair dispara o FormClosing de novo, e perguntar
            // duas vezes a mesma coisa - ou pior, aceitar um Cancelar na
            // segunda - deixaria a saida pela metade.
            if (!e.Cancel) _fechamentoDecidido = true;
        }

        /// <summary>Quem fecha decidiu sem perguntar: instalador ou fim da sessao.</summary>
        private bool _semPerguntar;
        private bool _fechamentoDecidido;

        /// <summary>
        /// Fecha descartando a edicao pendente, sem dialogo. Para quando o
        /// aplicativo sai por motivo que ninguem pode cancelar - o instalador
        /// pedindo o arquivo ou a sessao do Windows terminando.
        /// </summary>
        internal void FecharSemPerguntar()
        {
            GravarPendente();
            _semPerguntar = true;
            Close();
        }

        /// <summary>A pagina de destino do atalho, ou -1 se a tecla nao e de navegacao.</summary>
        internal static int IndiceDoAtalho(Keys tecla, int atual, int total)
        {
            if (total <= 0) return -1;
            if (tecla == (Keys.Control | Keys.Tab)) return (atual + 1) % total;
            if (tecla == (Keys.Control | Keys.Shift | Keys.Tab)) return (atual - 1 + total) % total;
            if (tecla >= (Keys.Control | Keys.D1) && tecla <= (Keys.Control | Keys.D8))
            {
                int i = (int)(tecla & Keys.KeyCode) - (int)Keys.D1;
                return i < total ? i : -1;
            }
            return -1;
        }

        /// <summary>
        /// A pagina certa e o primeiro arranjo ANTES do primeiro quadro.
        ///
        /// Deixar isso para o Shown mostrava a janela aberta na Visao geral, com os
        /// cartoes empilhados no canto, e so depois a pagina de verdade aparecia
        /// (o botao do Widget, os aparelhos): a pessoa via a tela se montar. O Load
        /// roda com a janela ja marcada como visivel e ainda sem pintar.
        /// </summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            MostrarPagina(string.IsNullOrEmpty(PaginaInicial) ? _cfg.LastPage : PaginaInicial);
            AtualizarPaginaAtiva(true);   // o conteudo ja vem no primeiro quadro, e nao no primeiro tique
            ArranjarPaginaVisivel();
        }

        /// <summary>Mica na barra de titulo; o handle recriado (ShowInTaskbar, por exemplo) perde o atributo, entao reaplica.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Mica.Aplicar(this);
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
            // A pedida de fora (clique no widget) vence; senao, a da ultima vez.
            MostrarPagina(string.IsNullOrEmpty(PaginaInicial) ? _cfg.LastPage : PaginaInicial);
            _paginaPronta = true;
            GuardarPagina();
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
            _dicaDoWidget.Dispose();
            _dicaDaConfig.Dispose();

            // Filtro de mensagens e do Application, e nao da janela: esquecer de
            // tirar deixaria um filtro morto examinando toda mensagem do processo
            // pelo resto da execucao, e a janela e reaberta a cada duplo clique
            // na bandeja.
            if (_roda != null) { Application.RemoveMessageFilter(_roda); _roda = null; }

            base.OnFormClosed(e);
        }
    }
}
