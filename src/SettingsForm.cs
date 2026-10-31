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
    {        private const int WS_EX_COMPOSITED = 0x02000000;

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
            ClientSize = FitInitialClient(guardado != Size.Empty ? guardado : TamanhoPadrao,
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
            // Um cooler desenhado: a fonte de icones nao tem ventoinha.
            paineis.Icone = IconesCasa.Cooler;
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


            visao.Key = "visao"; paineis.Key = "paineis"; metricas.Key = "metricas"; specs.Key = "specs";
            perfis.Key = "perfis"; config.Key = "config"; sobre.Key = "sobre";

            NavItem[] fabrica = { visao, paineis, metricas, specs, config, sobre };
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
            MontarPainelCpu(paineis);
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
            if (chave == "perfis") { AbrirPainelCpu(1); return; }
            if (chave == "paineis") { AbrirPainelCpu(0); return; }
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
            if (_faixaPainelCpu != null) _faixaPainelCpu.Visible = sel != null && sel == _itemPainelCpu;
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
                AjustarRelogio();
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
        /// <summary>
        /// Area cliente da primeira abertura: 1263 x 738 a 100% (a janela inteira
        /// com 1265 x 770, contando a moldura de 1 px e a barra de titulo de 31 px
        /// do Windows 11). A altura e a maior que as paginas principais pedem,
        /// medida: Paineis (as duas colunas e a Previa, a mais alta) e a Visao
        /// geral cabem sem rolar, com 8 px de folga. Perfis, Configuracoes e Casa rolam.
        /// </summary>
        internal static Size TamanhoPadrao { get { return new Size(Ui.S(1263), Ui.S(782)); } }

        /// <summary>
        /// Versao do tamanho padrao. Gravado com versao menor (os padroes
        /// anteriores, ou um tamanho de antes da Visao geral nova), a janela abre
        /// uma vez no padrao novo; dai em diante vale o que a pessoa ajustar.
        /// </summary>
        internal const int VersaoDaJanela = 3;

        private Size TamanhoGuardado()
        {
            int w = _cfg.WindowW, h = _cfg.WindowH;
            if (w <= 0 || h <= 0) return Size.Empty;
            if (_cfg.JanelaVersao < VersaoDaJanela) return Size.Empty;

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
        /// <summary>
        /// Cadencia do relogio da janela: 4 Hz nas paginas com leitura ao vivo
        /// (Visao geral, Metricas, Paineis), 1 Hz nas outras, e parado com a
        /// janela minimizada ou escondida - o historico das metricas continua sendo
        /// gravado por fora, e o primeiro tique ao voltar traz tudo em dia.
        /// </summary>
        private void AjustarRelogio()
        {
            if (_tick == null) return;
            Control ativa = _nav != null && _nav.Selected != null ? _nav.Selected.Page : null;
            bool aoVivo = ativa == _pgVisao || ativa == _pgMetricas || ativa == _pgPaineis;
            int intervalo = aoVivo ? 250 : 1000;
            if (_tick.Interval != intervalo) _tick.Interval = intervalo;
            bool parar = WindowState == FormWindowState.Minimized || !Visible;
            if (parar && _tick.Enabled) { _tick.Stop(); _relogioPausado = true; }
            else if (!parar && _relogioPausado)
            {
                // so religa o que esta pausa desligou (os testes param o relogio de proposito)
                _relogioPausado = false;
                _tick.Start();
                // voltou: a pagina recebe um retrato novo agora, e nao no proximo tique
                if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate { try { AtualizarPaginaAtiva(true); } catch (Exception ex) { Log.Error("retomar a janela", ex); } });
            }
        }

        private bool _relogioPausado;

        internal bool RelogioLigado { get { return _tick != null && _tick.Enabled; } }
        internal int IntervaloDoRelogio { get { return _tick != null ? _tick.Interval : 0; } }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            AjustarRelogio();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            AjustarRelogio();
        }

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
                && _cfg.WindowX == Location.X && _cfg.WindowY == Location.Y && _cfg.JanelaVersao == VersaoDaJanela) return;
            try
            {
                _cfg.WindowW = ClientSize.Width;
                _cfg.WindowH = ClientSize.Height;
                _cfg.WindowX = Location.X;
                _cfg.WindowY = Location.Y;
                _cfg.JanelaVersao = VersaoDaJanela;
                string erro;
                if (!_session.TrySavePreferences(delegate(Config draft, Config target)
                {
                    target.WindowW = draft.WindowW;
                    target.WindowH = draft.WindowH;
                    target.WindowX = draft.WindowX;
                    target.WindowY = draft.WindowY;
                    target.JanelaVersao = draft.JanelaVersao;
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

        private CartaoDoCooler _cardCooler;
        private Card _cardSpecs;
        // Visao geral nova (a padrao): cabecalho, casa em cartoes, caixas de
        // perifericos e eventos. A classica usa a faixa e o resumo acima.
        private readonly List<string> _vgTitulos = new List<string>(), _vgTitulosMono = new List<string>();
        private PanelPreview _vgPreview;
        private Label _vgPerfil, _vgPainel;
        private Label[] _vgSpecCap, _vgSpecVal;
        private MetricCard _vgP1, _vgP2;
    }
}
