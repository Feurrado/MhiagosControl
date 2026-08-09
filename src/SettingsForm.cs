using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

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

        // pagina Paineis
        private SensorPicker _pick1, _pick2;
        private Toggle _tgF, _tgPct;
        private FlatBtn[] _div1, _div2;
        private Label _exp1, _exp2;
        private PanelPreview _preview;

        // pagina Alertas
        private NumericUpDown _alert1, _alert2;
        private Label _alertInfo1, _alertInfo2;

        // pagina Perfis
        private ListBox _profileList;

        // pagina Sobre
        private Toggle _tgAutostart;

        private static readonly int[] DivValues = new int[] { 0, 1, 10, 100, 1000 };
        private static readonly string[] DivLabels = new string[] { "Auto", "÷1", "÷10", "÷100", "÷1000" };

        public SettingsForm(Config cfg, List<SensorEntry> sensors, Func<Dictionary<string, float>> snapshot, Func<List<SensorEntry>> relist)
        {
            _cfg = cfg;
            _sensors = sensors;
            _snapshot = snapshot;
            _relist = relist;
            _current = cfg.Active;

            Text = "Mhiagos Control";
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
            _nav.Subtitle = "perfil: " + _current.Name;
            _nav.SelectionChanged += delegate { ShowPage(); };
            Controls.Add(_nav);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 58;
            footer.BackColor = Ui.Window;
            Controls.Add(footer);

            FlatBtn save = new FlatBtn();
            save.Text = "Salvar";
            save.Primary = true;
            save.SetBounds(ClientSize.Width - 210, 14, 96, 32);
            save.Click += new EventHandler(OnOk);
            footer.Controls.Add(save);

            FlatBtn cancel = new FlatBtn();
            cancel.Text = "Cancelar";
            cancel.SetBounds(ClientSize.Width - 106, 14, 96, 32);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            footer.Controls.Add(cancel);

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
            paineis.Text = "Painéis"; paineis.Glyph = ""; paineis.Page = BuildPagePaineis();

            NavItem alertas = new NavItem();
            alertas.Text = "Alertas"; alertas.Glyph = ""; alertas.Page = BuildPageAlertas();

            NavItem perfis = new NavItem();
            perfis.Text = "Perfis"; perfis.Glyph = ""; perfis.Page = BuildPagePerfis();

            NavItem sobre = new NavItem();
            sobre.Text = "Sobre"; sobre.Glyph = ""; sobre.Page = BuildPageSobre();

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

        private Control BuildPagePaineis()
        {
            Panel page = new Panel();
            page.BackColor = Color.Transparent;

            Card c1 = new Card();
            c1.Title = "Painel 1  ·  esquerdo";
            c1.SetBounds(0, 0, 370, 344);
            page.Controls.Add(c1);

            _pick1 = new SensorPicker();
            _pick1.SetBounds(12, 44, 346, 180);
            _pick1.SetSensors(Clone(_sensors));
            _pick1.SelectionChanged += delegate { OnChanged(); };
            c1.Controls.Add(_pick1);

            c1.Controls.Add(MakeLabel("Escala", 14, 232, Ui.FontMed));
            _div1 = MakeDivisorRow(c1, 14, 252, 1);

            _tgF = new Toggle();
            _tgF.Label = "Temperatura em Fahrenheit";
            _tgF.SetBounds(14, 286, 330, 26);
            _tgF.CheckedChanged += delegate { OnChanged(); };
            c1.Controls.Add(_tgF);

            _exp1 = MakeLabel("", 14, 316, Ui.FontSmall);
            _exp1.Size = new Size(344, 20);
            _exp1.ForeColor = Ui.Muted;
            c1.Controls.Add(_exp1);

            Card c2 = new Card();
            c2.Title = "Painel 2  ·  direito";
            c2.SetBounds(386, 0, 370, 344);
            page.Controls.Add(c2);

            _pick2 = new SensorPicker();
            _pick2.SetBounds(12, 44, 346, 180);
            _pick2.SetSensors(Clone(_sensors));
            _pick2.SelectionChanged += delegate { OnChanged(); };
            c2.Controls.Add(_pick2);

            c2.Controls.Add(MakeLabel("Escala", 14, 232, Ui.FontMed));
            _div2 = MakeDivisorRow(c2, 14, 252, 2);

            _tgPct = new Toggle();
            _tgPct.Label = "Acender  %  (desligado acende  W)";
            _tgPct.SetBounds(14, 286, 330, 26);
            _tgPct.CheckedChanged += delegate { OnChanged(); };
            c2.Controls.Add(_tgPct);

            _exp2 = MakeLabel("", 14, 316, Ui.FontSmall);
            _exp2.Size = new Size(344, 20);
            _exp2.ForeColor = Ui.Muted;
            c2.Controls.Add(_exp2);

            Card cp = new Card();
            cp.Title = "Prévia";
            cp.SetBounds(0, 356, 756, 456);
            page.Controls.Add(cp);

            _preview = new PanelPreview();
            _preview.SetBounds(12, 44, 732, 400);
            cp.Controls.Add(_preview);

            return page;
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
                e.Label = s.Label; e.Type = s.Type; e.Value = s.Value;
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
            c.Title = "Limiares";
            c.SetBounds(0, 0, 756, 250);
            page.Controls.Add(c);

            c.Controls.Add(MakeLabel("Avisar quando o painel 1 atingir:", 16, 56, Ui.FontBase));
            _alert1 = MakeNumeric(16, 78);
            _alert1.ValueChanged += delegate { OnChanged(); };
            c.Controls.Add(_alert1);
            _alertInfo1 = MakeLabel("", 130, 82, Ui.FontSmall);
            _alertInfo1.Size = new Size(600, 18); _alertInfo1.ForeColor = Ui.Muted;
            c.Controls.Add(_alertInfo1);

            c.Controls.Add(MakeLabel("Avisar quando o painel 2 atingir:", 16, 126, Ui.FontBase));
            _alert2 = MakeNumeric(16, 148);
            _alert2.ValueChanged += delegate { OnChanged(); };
            c.Controls.Add(_alert2);
            _alertInfo2 = MakeLabel("", 130, 152, Ui.FontSmall);
            _alertInfo2.Size = new Size(600, 18); _alertInfo2.ForeColor = Ui.Muted;
            c.Controls.Add(_alertInfo2);

            Label note = MakeLabel(
                "Zero desliga o aviso. O alerta dispara na subida e só rearma quando o valor cai abaixo do limiar —\n" +
                "sem isso, um sensor oscilando no limite notificaria a cada ciclo. O ícone da bandeja ganha um ponto\n" +
                "vermelho enquanto o alerta estiver ativo. Mostrador apagado não dispara alerta.",
                16, 190, Ui.FontSmall);
            note.Size = new Size(720, 52);
            note.ForeColor = Ui.Muted;
            c.Controls.Add(note);

            return page;
        }

        private static NumericUpDown MakeNumeric(int x, int y)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = 0; n.Maximum = 999;
            n.SetBounds(x, y, 96, 26);
            n.Font = Ui.FontBase;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.BackColor = Ui.SurfaceAlt;
            n.ForeColor = Ui.Text;
            return n;
        }

        // ---------------- pagina: Perfis ----------------

        private Control BuildPagePerfis()
        {
            Panel page = new Panel();
            page.BackColor = Color.Transparent;

            Card c = new Card();
            c.Title = "Perfis salvos";
            c.SetBounds(0, 0, 756, 420);
            page.Controls.Add(c);

            _profileList = new ListBox();
            _profileList.SetBounds(16, 50, 420, 300);
            _profileList.BorderStyle = BorderStyle.FixedSingle;
            _profileList.Font = Ui.FontBase;
            _profileList.BackColor = Ui.SurfaceAlt;
            _profileList.ForeColor = Ui.Text;
            _profileList.SelectedIndexChanged += new EventHandler(OnProfileSelected);
            c.Controls.Add(_profileList);

            int bx = 452, by = 50;
            c.Controls.Add(MakeSideButton("Novo", bx, by, new EventHandler(OnNewProfile)));
            c.Controls.Add(MakeSideButton("Renomear", bx, by + 40, new EventHandler(OnRenameProfile)));
            c.Controls.Add(MakeSideButton("Duplicar", bx, by + 80, new EventHandler(OnDuplicateProfile)));
            FlatBtn del = MakeSideButton("Excluir", bx, by + 130, new EventHandler(OnDeleteProfile));
            del.Danger = true;
            c.Controls.Add(del);

            Label note = MakeLabel(
                "O perfil selecionado aqui é o que fica ativo ao salvar.\n" +
                "Todos aparecem no menu da bandeja para troca rápida, sem abrir esta janela.",
                16, 362, Ui.FontSmall);
            note.Size = new Size(700, 40);
            note.ForeColor = Ui.Muted;
            c.Controls.Add(note);

            return page;
        }

        private FlatBtn MakeSideButton(string text, int x, int y, EventHandler h)
        {
            FlatBtn b = new FlatBtn();
            b.Text = text;
            b.SetBounds(x, y, 130, 32);
            b.Click += h;
            return b;
        }

        private void RefreshProfileList()
        {
            if (_profileList == null) return;
            _loading = true;
            _profileList.Items.Clear();
            foreach (Profile p in _cfg.Profiles) _profileList.Items.Add(p);
            _profileList.SelectedItem = _current;
            _loading = false;
        }

        private void OnProfileSelected(object sender, EventArgs e)
        {
            if (_loading) return;
            Profile p = _profileList.SelectedItem as Profile;
            if (p == null || p == _current) return;
            SaveToProfile();
            _current = p;
            _nav.Subtitle = "perfil: " + _current.Name;
            _nav.Invalidate();
            LoadFromProfile();
        }

        private void OnNewProfile(object sender, EventArgs e)
        {
            string name = Prompt("Nome do novo perfil:", "Perfil " + (_cfg.Profiles.Count + 1));
            if (string.IsNullOrEmpty(name)) return;
            if (_cfg.NameExists(name)) { Warn("Já existe um perfil com esse nome."); return; }
            SaveToProfile();
            Profile np = new Profile();
            np.Name = name;
            _cfg.Profiles.Add(np);
            _current = np;
            RefreshProfileList(); LoadFromProfile();
        }

        private void OnDuplicateProfile(object sender, EventArgs e)
        {
            string name = Prompt("Nome da cópia:", _current.Name + " (cópia)");
            if (string.IsNullOrEmpty(name)) return;
            if (_cfg.NameExists(name)) { Warn("Já existe um perfil com esse nome."); return; }
            SaveToProfile();
            Profile np = _current.Clone();
            np.Name = name;
            _cfg.Profiles.Add(np);
            _current = np;
            RefreshProfileList(); LoadFromProfile();
        }

        private void OnRenameProfile(object sender, EventArgs e)
        {
            string name = Prompt("Novo nome:", _current.Name);
            if (string.IsNullOrEmpty(name) || name == _current.Name) return;
            if (_cfg.NameExists(name)) { Warn("Já existe um perfil com esse nome."); return; }
            _current.Name = name;
            _nav.Subtitle = "perfil: " + name;
            _nav.Invalidate();
            RefreshProfileList();
        }

        private void OnDeleteProfile(object sender, EventArgs e)
        {
            if (_cfg.Profiles.Count <= 1) { Warn("É preciso manter ao menos um perfil."); return; }
            if (MessageBox.Show("Excluir o perfil \"" + _current.Name + "\"?", "Mhiagos Control",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _cfg.Profiles.Remove(_current);
            _current = _cfg.Profiles[0];
            _nav.Subtitle = "perfil: " + _current.Name;
            _nav.Invalidate();
            RefreshProfileList(); LoadFromProfile();
        }

        private static void Warn(string msg)
        {
            MessageBox.Show(msg, "Mhiagos Control", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------------- pagina: Sobre ----------------

        private Control BuildPageSobre()
        {
            Panel page = new Panel();
            page.BackColor = Color.Transparent;

            Card c = new Card();
            c.Title = "Sobre";
            c.SetBounds(0, 0, 756, 350);
            page.Controls.Add(c);

            Label t = MakeLabel("Mhiagos Control", 16, 54, Ui.FontTitle);
            t.Size = new Size(400, 30);
            c.Controls.Add(t);

            Label d = MakeLabel(
                "Driver alternativo para o painel do cooler Rise Mode Temp 6 Pro.\n" +
                "Protocolo levantado por engenharia reversa; qualquer sensor pode ir para qualquer mostrador.",
                16, 88, Ui.FontSmall);
            d.Size = new Size(700, 40);
            d.ForeColor = Ui.Muted;
            c.Controls.Add(d);

            _tgAutostart = new Toggle();
            _tgAutostart.Label = "Iniciar junto com o Windows";
            _tgAutostart.SetBounds(16, 140, 400, 26);
            _tgAutostart.Checked = Autostart.IsEnabled();
            _tgAutostart.CheckedChanged += new EventHandler(OnToggleAutostart);
            c.Controls.Add(_tgAutostart);

            Toggle tgAll = new Toggle();
            tgAll.Label = "Mostrar todos os sensores (inclui um por núcleo)";
            tgAll.SetBounds(16, 174, 460, 26);
            tgAll.Checked = _cfg.ShowAllSensors;
            tgAll.CheckedChanged += new EventHandler(OnToggleShowAll);
            c.Controls.Add(tgAll);

            Label allNote = MakeLabel(
                "Desligado, dezenas de sensores por núcleo viram uma média por grupo — clock e temperatura gerais\n" +
                "deixam de ficar enterrados entre repetições.", 16, 202, Ui.FontSmall);
            allNote.Size = new Size(700, 34);
            allNote.ForeColor = Ui.Muted;
            c.Controls.Add(allNote);

            Label paths = MakeLabel("Configuração e registro em:\n" + Paths.DataDir, 16, 244, Ui.FontSmall);
            paths.Size = new Size(700, 40);
            paths.ForeColor = Ui.Muted;
            c.Controls.Add(paths);

            FlatBtn open = new FlatBtn();
            open.Text = "Abrir pasta";
            open.SetBounds(16, 292, 130, 32);
            open.Click += delegate
            {
                try { System.Diagnostics.Process.Start("explorer.exe", Paths.DataDir); }
                catch (Exception ex) { Log.Error("abrir pasta de dados", ex); }
            };
            c.Controls.Add(open);

            return page;
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
                Warn("Não foi possível alterar a inicialização automática.\nDetalhes em:\n" + Log.Path);
                _loading = true; _tgAutostart.Checked = !target; _loading = false;
            }
        }

        // ---------------- estado ----------------

        private void LoadFromProfile()
        {
            _loading = true;
            _pick1.SelectedId = _current.Panel1Id;
            _pick2.SelectedId = _current.Panel2Id;
            _tgF.Checked = _current.Fahrenheit;
            _tgPct.Checked = _current.Percent;
            _alert1.Value = Clamp(_current.Alert1);
            _alert2.Value = Clamp(_current.Alert2);
            _loading = false;
            Refresh0();
        }

        private static decimal Clamp(int v)
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
            _current.Fahrenheit = _tgF.Checked;
            _current.Percent = _tgPct.Checked;
            _current.Alert1 = (int)_alert1.Value;
            _current.Alert2 = (int)_alert2.Value;
        }

        private void OnChanged()
        {
            if (_loading) return;
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

            _exp1.Text = Scaling.Explain(s1, d1, _tgF.Checked);
            _exp2.Text = Scaling.Explain(s2, d2, false);

            PanelValue v1 = Scaling.Prepare(s1, d1, _tgF.Checked);
            PanelValue v2 = Scaling.Prepare(s2, d2, false);

            _preview.Value1 = v1.Value;
            _preview.Value2 = v2.Value;
            _preview.Fahrenheit = _tgF.Checked;
            _preview.Percent = _tgPct.Checked;
            _preview.Alert1 = _alert1.Value > 0 && v1.Value.HasValue && v1.Value.Value >= (int)_alert1.Value;
            _preview.Alert2 = _alert2.Value > 0 && v2.Value.HasValue && v2.Value.Value >= (int)_alert2.Value;
            _preview.Invalidate();

            if (_alertInfo1 != null)
            {
                _alertInfo1.Text = _alert1.Value > 0 ? "atual: " + Show(v1) : "desligado";
                _alertInfo2.Text = _alert2.Value > 0 ? "atual: " + Show(v2) : "desligado";
            }
        }

        private static string Show(PanelValue v)
        {
            return v.Value.HasValue ? v.Value.Value.ToString() : "sem leitura";
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
                Refresh0();
            }
            catch (Exception ex) { Log.Error("atualizacao da previa", ex); }
        }

        private string Prompt(string message, string initial)
        {
            using (Form f = new Form())
            {
                f.Text = "Mhiagos Control";
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
                ok.Text = "OK"; ok.Primary = true;
                ok.SetBounds(172, 86, 90, 30);
                ok.Click += delegate { f.DialogResult = DialogResult.OK; f.Close(); };

                FlatBtn ca = new FlatBtn();
                ca.Text = "Cancelar";
                ca.SetBounds(272, 86, 92, 30);
                ca.Click += delegate { f.DialogResult = DialogResult.Cancel; f.Close(); };

                f.Controls.AddRange(new Control[] { l, t, ok, ca });
                f.AcceptButton = null;
                Theme.Apply(f);

                return f.ShowDialog(this) == DialogResult.OK ? t.Text.Trim() : null;
            }
        }

        private void OnOk(object sender, EventArgs e)
        {
            SaveToProfile();
            _cfg.ActiveName = _current.Name;
            _cfg.Save();
            DialogResult = DialogResult.OK;
            Close();
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
            base.OnFormClosed(e);
        }
    }
}
