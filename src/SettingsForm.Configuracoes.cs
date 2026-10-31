using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    public partial class SettingsForm : Form
    {
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
    }
}
