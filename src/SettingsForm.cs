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
        private NumberBox _alert1, _alert2;
        private Label _alertInfo1, _alertInfo2;

        // pagina Perfis
        private ProfileList _profileList;
        private PanelPreview _profilePreview;
        private Label _profileInfo;
        private FlatBtn _btnApply;

        // pagina Sobre
        private Toggle _tgAutostart;
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
            save.SetBounds(ClientSize.Width - 210, 14, 96, 32);
            save.Click += new EventHandler(OnSave);
            footer.Controls.Add(save);

            FlatBtn close = new FlatBtn();
            close.Text = T.Close;
            close.SetBounds(ClientSize.Width - 106, 14, 96, 32);
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
            _host.BringToFront();
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

            NavItem sobre = new NavItem();
            sobre.Text = T.NavAbout; sobre.Glyph = ""; sobre.Page = BuildPageSobre();

            foreach (NavItem it in new NavItem[] { paineis, alertas, perfis, sobre })
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
            if (sel != null && sel.Page != null) { sel.Page.Visible = true; sel.Page.BringToFront(); }
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

            c1.Controls.Add(MakeLabel(T.Unit, 14, 200, Ui.FontMed));
            _unit1 = new Segmented();
            _unit1.SetItems("°C", "°F");
            _unit1.SetBounds(14, 220, 140, 30);
            _unit1.SelectedIndexChanged += delegate { OnChanged(); };
            c1.Controls.Add(_unit1);

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

            c2.Controls.Add(MakeLabel(T.Unit, 14, 200, Ui.FontMed));
            _unit2 = new Segmented();
            _unit2.SetItems("%", "W");
            _unit2.SetBounds(14, 220, 140, 30);
            _unit2.SelectedIndexChanged += delegate { OnChanged(); };
            c2.Controls.Add(_unit2);


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
            c.SetBounds(0, 0, 756, 250);
            page.Controls.Add(c);

            _alert1 = MakeLimiar(c, T.Panel1, 16, 58, out _alertInfo1);
            _alert2 = MakeLimiar(c, T.Panel2, 386, 58, out _alertInfo2);

            Label note = MakeLabel(T.AlertsNote, 16, 186, Ui.FontSmall);
            note.Size = new Size(720, 52);
            note.ForeColor = Ui.Muted;
            c.Controls.Add(note);

            return page;
        }

        /// <summary>Rotulo, campo numerico e leitura atual de um dos limiares.</summary>
        private NumberBox MakeLimiar(Control host, string titulo, int x, int y, out Label info)
        {
            host.Controls.Add(MakeLabel(titulo, x, y, Ui.FontMed));

            Label cap = MakeLabel(T.WarnWhenReaching, x, y + 24, Ui.FontSmall);
            cap.Size = new Size(320, 18);
            cap.ForeColor = Ui.Muted;
            host.Controls.Add(cap);

            NumberBox n = new NumberBox();
            n.Minimum = 0; n.Maximum = 999;
            n.SetBounds(x, y + 48, 120, 34);
            n.ValueChanged += delegate { OnChanged(); };
            host.Controls.Add(n);

            info = MakeLabel("", x + 134, y + 48, Ui.FontSmall);
            info.Size = new Size(200, 34);
            info.TextAlign = ContentAlignment.MiddleLeft;   // alinha com o campo, nao com o topo
            info.ForeColor = Ui.Muted;
            host.Controls.Add(info);

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
            _profileList.SetBounds(12, 44, 306, 396);
            _profileList.Resolve = NomeDoSensor;
            _profileList.ActiveName = _cfg.ActiveName;
            _profileList.SelectionChanged += new EventHandler(OnProfileSelected);
            _profileList.ItemActivated += delegate { AplicarPerfil(); };
            c.Controls.Add(_profileList);

            c.Controls.Add(MakeSideButton(T.New, 12, 452, 145, new EventHandler(OnNewProfile)));
            c.Controls.Add(MakeSideButton(T.Rename, 173, 452, 145, new EventHandler(OnRenameProfile)));
            c.Controls.Add(MakeSideButton(T.Duplicate, 12, 492, 145, new EventHandler(OnDuplicateProfile)));
            FlatBtn del = MakeSideButton(T.Delete, 173, 492, 145, new EventHandler(OnDeleteProfile));
            del.Danger = true;
            c.Controls.Add(del);

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

            Label note = MakeLabel(T.ProfilesNote, 2, 578, Ui.FontSmall);
            note.Size = new Size(750, 40);
            note.ForeColor = Ui.Muted;
            page.Controls.Add(note);

            return page;
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
            _profilePreview.Alert1 = p.Alert1 > 0 && v1.Value.HasValue && v1.Value.Value >= p.Alert1;
            _profilePreview.Alert2 = p.Alert2 > 0 && v2.Value.HasValue && v2.Value.Value >= p.Alert2;
            _profilePreview.Invalidate();

            string n1 = NomeDoSensor(p.Panel1Id), n2 = NomeDoSensor(p.Panel2Id);
            _profileInfo.Text =
                T.PanelShort(1) + ":  " + (n1 ?? "—") + "   (" + (p.Fahrenheit ? "°F" : "°C") + ")\n" +
                T.PanelShort(2) + ":  " + (n2 ?? "—") + "   (" + (p.Percent ? "%" : "W") + ")\n" +
                T.Thresholds + ":  " + (p.Alert1 > 0 ? p.Alert1.ToString() : T.Off) +
                "   ·   " + (p.Alert2 > 0 ? p.Alert2.ToString() : T.Off);

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

        // ---------------- pagina: Sobre ----------------

        private Control BuildPageSobre()
        {
            Panel page = new Panel();
            page.BackColor = Color.Transparent;

            // A unica pagina que rola: identidade, idioma, fontes, creditos e
            // isencao passam do que cabe em 818 px, e cortar qualquer um deles
            // seria pior do que rolar.
            page.AutoScroll = true;

            Card c = new Card();
            c.Title = T.NavAbout;
            c.SetBounds(0, 0, 736, 456);
            page.Controls.Add(c);

            Label t = MakeLabel(T.AppName, 16, 50, Ui.FontTitle);
            t.Size = new Size(400, 30);
            c.Controls.Add(t);

            Label d = MakeLabel(T.AboutTagline, 16, 84, Ui.FontSmall);
            d.Size = new Size(700, 34);
            d.ForeColor = Ui.Muted;
            c.Controls.Add(d);

            c.Controls.Add(MakeLabel(T.Language_, 16, 128, Ui.FontMed));

            _lang = new Segmented();
            _lang.SetItems("Português (BR)", "English (US)");
            _lang.SetBounds(16, 148, 300, 30);
            _lang.SelectedIndex = T.Pt ? 0 : 1;
            // depois de fixar a posicao: o seletor avisa toda mudanca, e a
            // primeira seria a nossa - reabriria a janela ao abrir a janela
            _lang.SelectedIndexChanged += new EventHandler(OnLanguageChanged);
            c.Controls.Add(_lang);

            Label langNote = MakeLabel(T.LanguageNote, 328, 148, Ui.FontSmall);
            langNote.Size = new Size(400, 30);
            langNote.TextAlign = ContentAlignment.MiddleLeft;
            langNote.ForeColor = Ui.Muted;
            c.Controls.Add(langNote);

            _tgAutostart = new Toggle();
            _tgAutostart.Label = T.StartWithWindows;
            _tgAutostart.SetBounds(16, 190, 400, 26);
            _tgAutostart.Checked = Autostart.IsEnabled();
            _tgAutostart.CheckedChanged += new EventHandler(OnToggleAutostart);
            c.Controls.Add(_tgAutostart);

            Toggle tgAll = new Toggle();
            tgAll.Label = T.ShowAllSensors;
            tgAll.SetBounds(16, 222, 480, 26);
            tgAll.Checked = _cfg.ShowAllSensors;
            tgAll.CheckedChanged += new EventHandler(OnToggleShowAll);
            c.Controls.Add(tgAll);

            Label allNote = MakeLabel(T.ShowAllNote, 16, 250, Ui.FontSmall);
            allNote.Size = new Size(700, 30);
            allNote.ForeColor = Ui.Muted;
            c.Controls.Add(allNote);

            // Quais fontes responderam, contado da propria lista de sensores.
            // Antes, perder o HWiNFO so aparecia no log: o aplicativo seguia
            // sem temperatura nem potencia da CPU e nada em tela dizia por que.
            c.Controls.Add(MakeLabel(T.SensorSources, 16, 286, Ui.FontMed));

            // Altura fixa para as tres linhas do pior caso: com o aviso, a
            // contagem passa a dividir espaco com duas linhas de explicacao, e
            // dimensionar so para o caso bom cortava justamente o texto que
            // existe para ser lido.
            Label fontes = MakeLabel(TextoDasFontes(), 16, 306, Ui.FontSmall);
            fontes.Size = new Size(716, 52);
            fontes.ForeColor = SemHwInfo() ? Ui.Warn : Ui.Muted;
            c.Controls.Add(fontes);

            Label paths = MakeLabel(T.DataPathLabel + "\n" + Paths.DataDir, 16, 368, Ui.FontSmall);
            paths.Size = new Size(700, 34);
            paths.ForeColor = Ui.Muted;
            c.Controls.Add(paths);

            FlatBtn open = new FlatBtn();
            open.Text = T.OpenFolder;
            open.SetBounds(16, 408, 130, 32);
            open.Click += delegate
            {
                try { System.Diagnostics.Process.Start("explorer.exe", Paths.DataDir); }
                catch (Exception ex) { Log.Error("abrir pasta de dados", ex); }
            };
            c.Controls.Add(open);

            Card cr = new Card();
            cr.Title = T.ProjectAndCredits;
            cr.SetBounds(0, 468, 736, 140);
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
            cd.SetBounds(0, 620, 736, 278);
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
            _preview.Alert1 = _alert1.Value > 0 && v1.Value.HasValue && v1.Value.Value >= _alert1.Value;
            _preview.Alert2 = _alert2.Value > 0 && v2.Value.HasValue && v2.Value.Value >= _alert2.Value;
            _preview.Invalidate();

            if (_alertInfo1 != null)
            {
                _alertInfo1.Text = _alert1.Value > 0 ? T.Current + Show(v1) : T.Off;
                _alertInfo2.Text = _alert2.Value > 0 ? T.Current + Show(v2) : T.Off;
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
