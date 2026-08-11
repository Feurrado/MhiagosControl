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
    /// sensores, unidades, escala, alertas e perfis, tudo junto ficava denso
    /// demais para encontrar qualquer coisa.
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly Config _cfg;
        private readonly Func<Dictionary<string, float>> _snapshot;
        private List<SensorEntry> _sensors;
        private readonly Func<List<SensorEntry>> _relist;

        private NavBar _nav;
        private Panel _host;
        private Profile _current;
        private bool _loading = false;
        private Timer _tick;
        private Label _footerNote;
        private Timer _noteTimer;

        // pagina Paineis
        private SensorPicker _pick1, _pick2;
        private Segmented _unit1, _unit2;
        private FlatBtn[] _div1, _div2;
        private PanelPreview _preview;
        private SensorSlot _slot1, _slot2;

        // pagina Alertas
        private NumberBox _alert1, _alert2, _alert1Low, _alert2Low;
        private Label _alertInfo1, _alertInfo2, _alertCross;

        // pagina Perfis
        private ProfileList _profileList;
        private PanelPreview _profilePreview;
        private Label _profileInfo;
        private FlatBtn _btnApply;

        // pagina Sobre
        private Toggle _tgAutostart, _tgIdle, _tgRotate;
        private NumberBox _idleMin, _rotSecs;
        private Label _idleNote, _rotNote;
        private Segmented _lang;

        /// <summary>Ha edicao ainda nao gravada. Governa o aviso ao fechar.</summary>
        private bool _dirty = false;

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

        public SettingsForm(Config cfg, List<SensorEntry> sensors, Func<Dictionary<string, float>> snapshot, Func<List<SensorEntry>> relist)
        {
            _cfg = cfg;
            _sensors = sensors;
            _snapshot = snapshot;
            _relist = relist;
            _current = cfg.Active;

            Text = T.AppName;
            Icon = Assets.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1010, 900);
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
            _tick.Interval = 1000;
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
            _nav.SpecsCaption = T.SystemCaption;
            _nav.Specs = SystemInfo.From(_sensors);
            _nav.Collapsed = _cfg.SidebarCollapsed;
            _nav.CollapsedChanged += delegate
            {
                // Grava na hora, e nao no Salvar: recolher a barra e escolha de
                // espaco de tela, nao edicao de perfil, e nao deve ficar refem
                // de uma gravacao que a pessoa talvez nem faca.
                try { _cfg.SidebarCollapsed = _nav.Collapsed; _cfg.Save(); }
                catch (Exception ex) { Log.Error("gravar estado da barra lateral", ex); }
            };
            _nav.SelectionChanged += delegate { ShowPage(); };
            Controls.Add(_nav);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 58;
            footer.BackColor = Ui.Window;
            Controls.Add(footer);

            // Salvar grava e FICA. Fechar a janela a cada gravacao obrigava a
            // reabri-la para o ajuste seguinte, e um ajuste de painel quase
            // nunca vem sozinho.
            FlatBtn save = new FlatBtn();
            save.Text = T.Save;
            save.Primary = true;
            // Coordenadas relativas ao RODAPE, que agora comeca depois da barra
            // lateral - e nao a janela. Medir pela janela punha os botoes 210 px
            // alem da borda direita do proprio rodape.
            int rodape = ClientSize.Width - _nav.Width;
            save.SetBounds(rodape - 210, 14, 96, 32);
            // Ancorados a direita: recolher a barra alarga o rodape, e sem a
            // ancora os botoes ficariam parados no meio dele.
            save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            save.Click += new EventHandler(OnSave);
            footer.Controls.Add(save);

            FlatBtn close = new FlatBtn();
            close.Text = T.Close;
            close.SetBounds(rodape - 106, 14, 96, 32);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Click += delegate { Close(); };
            footer.Controls.Add(close);

            _footerNote = MakeLabel("", 18, 22, Ui.FontSmall);
            _footerNote.Size = new Size(320, 18);
            _footerNote.ForeColor = Ui.Accent;
            _footerNote.Visible = false;
            footer.Controls.Add(_footerNote);

            _host = new Panel();
            _host.Dock = DockStyle.Fill;
            _host.BackColor = Ui.Window;
            _host.Padding = new Padding(18, 16, 18, 8);
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
            NavItem paineis = new NavItem();
            paineis.Text = T.NavPanels; paineis.Glyph = ""; paineis.Page = BuildPagePaineis();

            NavItem alertas = new NavItem();
            alertas.Text = T.NavAlerts; alertas.Glyph = ""; alertas.Page = BuildPageAlertas();

            NavItem perfis = new NavItem();
            perfis.Text = T.NavProfiles; perfis.Glyph = ""; perfis.Page = BuildPagePerfis();

            // Glifo escapado, e nao o caractere solto como os de cima: E713 e a
            // engrenagem da Segoe MDL2, e um caractere da area de uso privado
            // colado no fonte depende de sobreviver a toda ferramenta que passar
            // pelo arquivo.
            NavItem config = new NavItem();
            config.Text = T.NavSettings; config.Glyph = ""; config.Page = BuildPageConfig();

            NavItem metricas = new NavItem();
            metricas.Text = T.NavMetrics;
            metricas.Glyph = "\uE9D9";            // grafico de area, Segoe MDL2
            metricas.Page = BuildPageMetricas();

            NavItem sobre = new NavItem();
            sobre.Text = T.NavAbout; sobre.Glyph = ""; sobre.Page = BuildPageSobre();

            foreach (NavItem it in new NavItem[] { paineis, alertas, metricas, perfis, config, sobre })
            {
                _nav.Add(it);
                it.Page.Dock = DockStyle.Fill;
                it.Page.Visible = false;
                _host.Controls.Add(it.Page);
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
            }
            if (_profileList != null) RefreshProfileList();
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
            Panel page = new Panel();
            page.BackColor = Color.Transparent;

            // Os seletores continuam sendo o estado da escolha; so nao aparecem
            // na pagina. Quem os edita e o dialogo.
            _pick1 = new SensorPicker();
            _pick1.SetSensors(Clone(_sensors));
            _pick2 = new SensorPicker();
            _pick2.SetSensors(Clone(_sensors));

            Card c1 = new Card();
            c1.Title = T.Panel1;
            c1.SetBounds(0, 0, 370, 268);
            page.Controls.Add(c1);

            _slot1 = new SensorSlot();
            _slot1.SetBounds(12, 48, 346, 80);
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
            c2.SetBounds(386, 0, 370, 268);
            page.Controls.Add(c2);

            _slot2 = new SensorSlot();
            _slot2.SetBounds(12, 48, 346, 80);
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


            Card cp = new Card();
            cp.Title = T.Preview;
            cp.SetBounds(0, 280, 756, 532);
            page.Controls.Add(cp);

            _preview = new PanelPreview();
            _preview.SetBounds(12, 44, 732, 476);
            cp.Controls.Add(_preview);

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

        // ---------------- pagina: Alertas ----------------

        private Control BuildPageAlertas()
        {
            Panel page = new Panel();
            page.BackColor = Color.Transparent;

            Card c = new Card();
            c.Title = T.Thresholds;
            c.SetBounds(0, 0, 756, 288);
            page.Controls.Add(c);

            MakeLimiar(c, T.Panel1, 16, 58, out _alert1, out _alert1Low, out _alertInfo1);
            MakeLimiar(c, T.Panel2, 386, 58, out _alert2, out _alert2Low, out _alertInfo2);

            // Aviso de faixa impossivel. Fica sempre no mesmo lugar, e nao num
            // balao ao salvar: e uma observacao sobre o que esta na tela, e
            // interromper para dize-la seria desproporcional - a configuracao
            // e valida, so nao faz o que parece fazer.
            _alertCross = MakeLabel("", 16, 182, Ui.FontSmall);
            _alertCross.Size = new Size(720, 20);
            _alertCross.ForeColor = Ui.Warn;
            _alertCross.Visible = false;
            c.Controls.Add(_alertCross);

            Label note = MakeLabel(T.AlertsNote, 16, 210, Ui.FontSmall);
            note.Size = new Size(720, 56);
            note.ForeColor = Ui.Muted;
            c.Controls.Add(note);

            return page;
        }

        /// <summary>Titulo, leitura atual e os dois limiares de um mostrador.</summary>
        private void MakeLimiar(Control host, string titulo, int x, int y,
                                out NumberBox alto, out NumberBox baixo, out Label info)
        {
            host.Controls.Add(MakeLabel(titulo, x, y, Ui.FontMed));

            info = MakeLabel("", x, y + 24, Ui.FontSmall);
            info.Size = new Size(330, 18);
            info.ForeColor = Ui.Muted;
            host.Controls.Add(info);

            alto = MakeCampoLimiar(host, T.AboveOf, x, y + 50);
            baixo = MakeCampoLimiar(host, T.BelowOf, x + 152, y + 50);
        }

        private NumberBox MakeCampoLimiar(Control host, string legenda, int x, int y)
        {
            Label cap = MakeLabel(legenda, x, y, Ui.FontSmall);
            cap.Size = new Size(140, 18);
            cap.ForeColor = Ui.Muted;
            host.Controls.Add(cap);

            NumberBox n = new NumberBox();
            n.Minimum = 0; n.Maximum = 999;
            n.SetBounds(x, y + 22, 120, 34);
            n.ValueChanged += delegate { OnChanged(); };
            host.Controls.Add(n);
            return n;
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
            Panel page = new Panel();
            page.BackColor = Color.Transparent;

            Card c = new Card();
            c.Title = T.SavedProfiles;
            c.SetBounds(0, 0, 330, 566);
            page.Controls.Add(c);

            _profileList = new ProfileList();
            _profileList.SetBounds(12, 44, 306, 350);
            _profileList.Resolve = NomeDoSensor;
            _profileList.ActiveName = _cfg.ActiveName;
            _profileList.SelectionChanged += new EventHandler(OnProfileSelected);
            _profileList.ItemActivated += delegate { AplicarPerfil(); };
            c.Controls.Add(_profileList);

            c.Controls.Add(MakeSideButton(T.New, 12, 406, 145, new EventHandler(OnNewProfile)));
            c.Controls.Add(MakeSideButton(T.Rename, 173, 406, 145, new EventHandler(OnRenameProfile)));
            c.Controls.Add(MakeSideButton(T.Duplicate, 12, 446, 145, new EventHandler(OnDuplicateProfile)));
            FlatBtn del = MakeSideButton(T.Delete, 173, 446, 145, new EventHandler(OnDeleteProfile));
            del.Danger = true;
            c.Controls.Add(del);
            c.Controls.Add(MakeSideButton(T.Export, 12, 486, 145, new EventHandler(OnExportProfile)));
            c.Controls.Add(MakeSideButton(T.Import, 173, 486, 145, new EventHandler(OnImportProfile)));

            Card cv = new Card();
            cv.Title = T.ProfilePreview;
            cv.SetBounds(346, 0, 410, 566);
            page.Controls.Add(cv);

            _profilePreview = new PanelPreview();
            _profilePreview.SetBounds(12, 44, 386, 372);
            cv.Controls.Add(_profilePreview);

            _profileInfo = MakeLabel("", 16, 428, Ui.FontSmall);
            _profileInfo.Size = new Size(378, 66);
            _profileInfo.ForeColor = Ui.Muted;
            cv.Controls.Add(_profileInfo);

            _btnApply = new FlatBtn();
            _btnApply.Text = T.ApplyProfile;
            _btnApply.Primary = true;
            _btnApply.SetBounds(12, 508, 386, 40);
            _btnApply.Click += delegate { AplicarPerfil(); };
            cv.Controls.Add(_btnApply);

            // O rodizio mora aqui, e nao junto das outras preferencias, porque
            // o que ele gira sao perfis: o toggle age sobre o que esta
            // selecionado na lista ao lado, e ver as duas coisas juntas e o que
            // torna a marcacao compreensivel.
            Card cg = new Card();
            cg.Title = T.Rotation;
            cg.SetBounds(0, 578, 756, 150);
            page.Controls.Add(cg);

            _tgRotate = new Toggle();
            _tgRotate.Label = T.IncludeInRotation;
            _tgRotate.SetBounds(16, 48, 460, 26);
            _tgRotate.CheckedChanged += new EventHandler(OnToggleRotate);
            cg.Controls.Add(_tgRotate);

            _rotSecs = new NumberBox();
            _rotSecs.Minimum = 2; _rotSecs.Maximum = 999;
            _rotSecs.Value = _cfg.RotateSeconds > 0 ? _cfg.RotateSeconds : 20;
            _rotSecs.SetBounds(16, 84, 110, 32);
            _rotSecs.ValueChanged += new EventHandler(OnRotateSeconds);
            cg.Controls.Add(_rotSecs);

            _rotNote = MakeLabel("", 134, 84, Ui.FontSmall);
            _rotNote.Size = new Size(600, 32);
            _rotNote.TextAlign = ContentAlignment.MiddleLeft;
            _rotNote.ForeColor = Ui.Muted;
            cg.Controls.Add(_rotNote);

            Label note = MakeLabel(T.ProfilesNote, 2, 740, Ui.FontSmall);
            note.Size = new Size(750, 40);
            note.ForeColor = Ui.Muted;
            page.Controls.Add(note);

            AtualizarRodizio();
            return page;
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
            _cfg.ActiveName = p.Name;
            _cfg.Save();
            _saved = true; _dirty = false;

            _profileList.ActiveName = p.Name;
            _profileList.Invalidate();
            AtualizarPreviaDoPerfil();
            if (Applied != null) Applied(this, EventArgs.Empty);
            Aviso(T.ApplyProfile + ": " + p.Name);
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
            _profilePreview.Alert1 = Fora(v1, p.Alert1, p.Alert1Low);
            _profilePreview.Alert2 = Fora(v2, p.Alert2, p.Alert2Low);
            _profilePreview.Invalidate();

            string n1 = NomeDoSensor(p.Panel1Id), n2 = NomeDoSensor(p.Panel2Id);
            _profileInfo.Text =
                T.PanelShort(1) + ":  " + (n1 ?? "—") + "   (" + (p.Fahrenheit ? "°F" : "°C") + ")\n" +
                T.PanelShort(2) + ":  " + (n2 ?? "—") + "   (" + (p.Percent ? "%" : "W") + ")\n" +
                T.Thresholds + ":  " + Faixa(p.Alert1, p.Alert1Low) +
                "   ·   " + Faixa(p.Alert2, p.Alert2Low);

            AtualizarRodizio();

            bool ativo = string.Equals(p.Name, _cfg.ActiveName, StringComparison.Ordinal);
            _btnApply.Text = ativo ? T.ApplyProfile + "  (" + T.AlreadyActive + ")" : T.ApplyProfile;
            _btnApply.Enabled = !ativo;
            _btnApply.Invalidate();
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
        /// de mexer num limiar exportaria o valor antigo, e o arquivo sairia
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
            alvo.Name = name;
            if (eraAtivo) _cfg.ActiveName = name;

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

            _cfg.Profiles.Remove(alvo);
            if (_current == alvo) _current = _cfg.Profiles[0];
            if (string.Equals(_cfg.ActiveName, alvo.Name, StringComparison.Ordinal))
                _cfg.ActiveName = _cfg.Profiles[0].Name;

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
            _footerNote.Visible = true;

            if (_noteTimer == null)
            {
                _noteTimer = new Timer();
                _noteTimer.Interval = 2600;
                _noteTimer.Tick += delegate { _noteTimer.Stop(); _footerNote.Visible = false; };
            }
            _noteTimer.Stop();
            _noteTimer.Start();
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
            Panel page = new Panel();
            page.BackColor = Color.Transparent;

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
            onde.TextAlign = ContentAlignment.MiddleLeft;
            onde.ForeColor = Ui.Faint;
            c.Controls.Add(onde);

            FlatBtn abrir = new FlatBtn();
            abrir.Text = T.OpenFolder;
            abrir.SetBounds(600, 354, 140, 32);
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

        private FlatBtn _btAddMetrica, _btPadraoMetrica;
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
        private Panel _pgMetricas;

        private Control BuildPageMetricas()
        {
            _pgMetricas = new Panel();
            _pgMetricas.BackColor = Color.Transparent;

            // Rolagem so na vertical.
            //
            // A barra horizontal aparecia porque a dica tinha 600 px fixos de
            // largura numa pagina de 780, e uma grade que nunca precisou rolar
            // de lado ganhava uma barra clara atravessada no rodape. A ordem
            // abaixo e a que funciona: desligar, zerar a rolagem horizontal e
            // so entao religar - com AutoScroll ligado, o painel restaura os
            // valores sozinho.
            _pgMetricas.AutoScroll = false;
            _pgMetricas.HorizontalScroll.Enabled = false;
            _pgMetricas.HorizontalScroll.Visible = false;
            _pgMetricas.HorizontalScroll.Maximum = 0;
            _pgMetricas.AutoScroll = true;

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
            _cfg.MetricIds.Clear();
            _cfg.MetricSizes.Clear();
            foreach (SensorEntry s in MetricPicker.Escolher(_sensors, 5))
            {
                _cfg.MetricIds.Add(s.Id);
                _cfg.MetricSizes.Add(0);
            }
            _cfg.MetricsChosen = true;
            GravarMetricas();
        }

        private void GravarMetricas()
        {
            try { _cfg.Save(); }
            catch (Exception ex) { Log.Error("gravar cartoes de metricas", ex); }
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
            _btPadraoMetrica.Click += delegate { SelecaoPadrao(); MontarMetricas(); };
            _pgMetricas.Controls.Add(_btPadraoMetrica);

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
            _arranjando = true;
            _pgMetricas.SuspendLayout();
            try
            {
                const int Esp = 12;

                // A largura da barra de rolagem sai da conta SEMPRE, apareca ela
                // ou nao. Medir a area de cliente faria a grade encolher quando
                // a barra surge e alargar quando ela some, e cada troca dispara
                // um novo arranjo - dois estados que se chamam em circulo.
                int disp = _pgMetricas.Width -
                           System.Windows.Forms.SystemInformation.VerticalScrollBarWidth - 4;
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
                if (_segJanela != null) _segJanela.SetBounds(314, y, 186, 32);

                if (_lbDicaMetricas != null)
                {
                    const int x = 512;
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

                    _cards[i].SetBounds(2 + col * (larg + Esp), y,
                                        cols * larg + (cols - 1) * Esp, alt);

                    col += cols;
                    if (alt > alturaDaLinha) alturaDaLinha = alt;
                }
            }
            finally
            {
                _pgMetricas.ResumeLayout();
                _arranjando = false;
            }
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
                    _cards[i].Sub = MetricPicker.RodapeJogos();

                float v;
                _cards[i].Push(snap.TryGetValue(_cardIds[i], out v) ? (float?)v : null);
            }
        }

        private Control BuildPageSobre()
        {
            Panel page = new Panel();
            page.BackColor = Color.Transparent;

            // Continua rolando: mesmo sem as preferencias, identidade, fontes,
            // creditos e isencao passam dos 818 px, e cortar a isencao para
            // caber seria trocar o que importa pelo que cabe.
            page.AutoScroll = true;

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
            _cfg.Language = alvo;
            _cfg.Save();
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
            if (t == null || _relist == null) return;

            _cfg.ShowAllSensors = t.Checked;
            Sensors.ShowAll = t.Checked;

            // relista na hora: esperar a proxima abertura da janela seria
            // confuso, o interruptor pareceria nao ter efeito
            _sensors = _relist();
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
            _alert1.Value = Clamp(_current.Alert1);
            _alert2.Value = Clamp(_current.Alert2);
            _alert1Low.Value = Clamp(_current.Alert1Low);
            _alert2Low.Value = Clamp(_current.Alert2Low);
            _loading = false;
            Refresh0();
        }

        /// <summary>Fahrenheit e a segunda pastilha do seletor de unidade.</summary>
        private bool Fahrenheit { get { return _unit1.SelectedIndex == 1; } }

        /// <summary>Porcentagem e a PRIMEIRA pastilha; a segunda acende W.</summary>
        private bool Percent { get { return _unit2.SelectedIndex == 0; } }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 999) return 999;
            return v;
        }

        private void SaveToProfile()
        {
            if (_current == null) return;
            if (_pick1.SelectedId.Length > 0) _current.Panel1Id = _pick1.SelectedId;
            if (_pick2.SelectedId.Length > 0) _current.Panel2Id = _pick2.SelectedId;
            _current.Fahrenheit = Fahrenheit;
            _current.Percent = Percent;
            _current.Alert1 = _alert1.Value;
            _current.Alert2 = _alert2.Value;
            _current.Alert1Low = _alert1Low.Value;
            _current.Alert2Low = _alert2Low.Value;
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
            _preview.Alert1 = Fora(v1, _alert1.Value, _alert1Low.Value);
            _preview.Alert2 = Fora(v2, _alert2.Value, _alert2Low.Value);
            _preview.Invalidate();

            if (_alertInfo1 != null)
            {
                _alertInfo1.Text = T.Current + Show(v1) + Desligado(_alert1.Value, _alert1Low.Value);
                _alertInfo2.Text = T.Current + Show(v2) + Desligado(_alert2.Value, _alert2Low.Value);
                _alertCross.Visible = Cruzado(_alert1.Value, _alert1Low.Value)
                                   || Cruzado(_alert2.Value, _alert2Low.Value);
                if (_alertCross.Visible) _alertCross.Text = T.ThresholdsCross;
            }

            if (_slot1 != null)
            {
                _slot1.Entry = s1; _slot1.Invalidate();
                _slot2.Entry = s2; _slot2.Invalidate();
            }
        }

        private static string Show(PanelValue v)
        {
            return v.Value.HasValue ? v.Value.Value.ToString() : T.NoReading;
        }

        /// <summary>Mesma regra do TrayApp: zero desliga, sem leitura nao dispara.</summary>
        private static bool Fora(PanelValue v, int alto, int baixo)
        {
            if (!v.Value.HasValue) return false;
            return (alto > 0 && v.Value.Value >= alto) || (baixo > 0 && v.Value.Value <= baixo);
        }

        private static string Desligado(int alto, int baixo)
        {
            return (alto == 0 && baixo == 0) ? "   ·   " + T.Off : "";
        }

        private static bool Cruzado(int alto, int baixo)
        {
            return alto > 0 && baixo > 0 && baixo >= alto;
        }

        /// <summary>Resumo dos dois limiares de um mostrador: "&lt;30 &gt;85".</summary>
        private static string Faixa(int alto, int baixo)
        {
            if (alto == 0 && baixo == 0) return T.Off;
            string s = "";
            if (baixo > 0) s = "<" + baixo;
            if (alto > 0) s += (s.Length > 0 ? " " : "") + ">" + alto;
            return s;
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
                Dictionary<string, float> snap = _snapshot != null ? _snapshot() : null;
                if (snap == null) return;
                _pick1.UpdateValues(snap);
                _pick2.UpdateValues(snap);
                AtualizarValores(snap);
                AtualizarMetricas(snap);
                Refresh0();
                AtualizarPreviaDoPerfil();
            }
            catch (Exception ex) { Log.Error("atualizacao da previa", ex); }
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
            _cfg.Save();
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
                if (r == DialogResult.Yes) OnSave(this, EventArgs.Empty);
            }

            // OK significa "ha gravacao valida no disco"; quem abriu usa isso
            // para decidir se recarrega a configuracao e descarta a memoria
            if (DialogResult != DialogResult.Retry)
                DialogResult = _saved ? DialogResult.OK : DialogResult.Cancel;

            base.OnFormClosing(e);
        }

        /// <summary>
        /// As barras de rolagem so aceitam tema depois que o controle tem
        /// handle - por isso aqui, e nao no construtor.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Theme.ApplyScrollbars(this);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_tick != null) { _tick.Stop(); _tick.Dispose(); }
            if (_noteTimer != null) { _noteTimer.Stop(); _noteTimer.Dispose(); }
            base.OnFormClosed(e);
        }
    }
}
