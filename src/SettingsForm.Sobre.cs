using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    public partial class SettingsForm : Form
    {
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
            _blocoPainel.Ponto = achou ? (Color?)Ui.Verde : Ui.Faint;
            _blocoPainel.Apoio = !achou ? "" : id == "VID 1A2C / PID 4984" ? T.PanelTested : T.PanelUntested;

            int hw = 0, lhm = 0;
            foreach (SensorEntry s in _sensors)
                if (s.Source == "HWiNFO") hw++; else lhm++;
            _blocoFontes.Valor = T.SensorsFrom("HWiNFO", hw);
            _blocoFontes.CorDoValor = hw == 0 ? (Color?)Ui.Warn : null;
            _blocoFontes.Ponto = hw == 0 ? Ui.Warn : Ui.Verde;
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
            return string.IsNullOrEmpty(id) ? T.PanelNone : id;
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
            IniciarPreAquecimento();

            if (_roda == null)
            {
                _roda = new RodaDoMouse(this);
                Application.AddMessageFilter(_roda);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_tick != null) { _tick.Stop(); _tick.Dispose(); }
            if (_aquecer != null) { _aquecer.Stop(); _aquecer.Dispose(); _aquecer = null; }
            if (_noteTimer != null) { _noteTimer.Stop(); _noteTimer.Dispose(); }
            _dicaDaConfig.Dispose();

            // Filtro de mensagens e do Application, e nao da janela: esquecer de
            // tirar deixaria um filtro morto examinando toda mensagem do processo
            // pelo resto da execucao, e a janela e reaberta a cada duplo clique
            // na bandeja.
            if (_roda != null) { Application.RemoveMessageFilter(_roda); _roda = null; }

            base.OnFormClosed(e);
        }

        // ---------------- pre-aquecimento das paginas ----------------

        private Timer _aquecer;
        private readonly HashSet<Control> _aquecidas = new HashSet<Control>();

        /// <summary>
        /// A primeira visita a uma pagina cria as janelas (HWND) de todos os controles dela,
        /// e na Casa isso passava de 200 ms na frente de quem clicou. Com a janela ja
        /// mostrada e a pagina de abertura pintada, as outras ganham o HWND aos poucos,
        /// uma por vez, enquanto o app esta ocioso - a Casa primeiro.
        /// </summary>
        private void IniciarPreAquecimento()
        {
            if (_aquecer != null) return;
            _aquecer = new Timer();
            _aquecer.Interval = 120;
            _aquecer.Tick += delegate { PreAquecerProxima(); };
            _aquecer.Start();
        }

        private void PreAquecerProxima()
        {
            try
            {
                Control sel = _nav != null && _nav.Selected != null ? _nav.Selected.Page : null;
                Control alvo = null;
                 
                    foreach (NavItem it in _nav.Items)
                        if (it.Page != null && it.Page != sel && !_aquecidas.Contains(it.Page)) { alvo = it.Page; break; }
                if (alvo == null || !Visible || WindowState == FormWindowState.Minimized) { if (alvo == null) { _aquecer.Stop(); } return; }
                _aquecidas.Add(alvo);
                if (alvo.IsHandleCreated) return;
                // CreateControl(true): cria o HWND dela e dos filhos mesmo com a pagina escondida
                System.Reflection.MethodInfo m = typeof(Control).GetMethod("CreateControl",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new Type[] { typeof(bool) }, null);
                if (m != null) m.Invoke(alvo, new object[] { true });
                Theme.ApplyScrollbars(alvo);
            }
            catch (Exception ex) { Log.Error("pre-aquecimento das paginas", ex); if (_aquecer != null) _aquecer.Stop(); }
        }
    }
}
