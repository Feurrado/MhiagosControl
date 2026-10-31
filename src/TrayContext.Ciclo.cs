using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    public partial class TrayContext : ApplicationContext, ISettingsData, IPanelStatusData, IReadingStatusData, IControleDoWidget
    {
        /// <summary>
        /// Entrega a thread de atualizacao uma copia do perfil ativo.
        ///
        /// Chamar sempre que a configuracao mudar. E o unico ponto em que a
        /// thread de fundo passa a enxergar edicoes - o que tambem quer dizer
        /// que esquecer de chamar aqui deixa o mostrador desatualizado, e nao
        /// corrompido.
        /// </summary>
        private void Publicar()
        {
            try
            {
                _live = _cfg.Active.Clone();
                _idleBlankMs = ConfigTiming.IdleMilliseconds(_cfg.IdleBlankMinutes);
                _rotateMs = ConfigTiming.RotationMilliseconds(_cfg.RotateSeconds);

                List<Profile> roda = _cfg.Rotation;
                if (roda.Count < 2 || _rotateMs <= 0) _rotation = null;
                else
                {
                    Profile[] copia = new Profile[roda.Count];
                    for (int i = 0; i < roda.Count; i++) copia[i] = roda[i].Clone();
                    _rotation = copia;
                }

                AjustarFoco();
                MetricHistory.SeguirTambem(IdsAcompanhados());
            }
            catch (Exception ex) { Log.Error("publicacao do perfil ativo", ex); }
        }

        /// <summary>
        /// Diz a camada de sensores de quais grupos o mostrador depende.
        ///
        /// Medido nesta maquina: o ciclo com os 19 grupos custa 54 ms e o
        /// mesmo ciclo com os 2 grupos que vao ao mostrador custa 3,6 ms,
        /// porque o caro na biblioteca do HWiNFO e preparar cada grupo, e nao
        /// ler dele. O conjunto e o perfil ativo mais todos os do rodizio.
        ///
        /// Com a janela de configuracao aberta o foco sai: ali o seletor
        /// precisa da lista inteira, e um ciclo de 54 ms enquanto alguem mexe
        /// nos controles nao incomoda ninguem.
        /// </summary>
        private void AjustarFoco()
        {
            try
            {
                lock (_sensorLock) { _sensors.Focar(Foco(false)); }
            }
            catch (Exception ex) { Log.Error("foco da leitura de sensores", ex); }
        }

        /// <summary>
        /// Conjunto de identificadores que a leitura precisa cobrir.
        ///
        /// Nulo quer dizer "leia tudo", que e o caso com a janela aberta.
        ///
        /// Os cartoes da aba Metricas so entram quando o historico vai tirar uma
        /// amostra. Deixa-los no foco o tempo todo seria voltar ao ciclo
        /// completo para sempre - a grade padrao toca em processador, video,
        /// memoria, placa-mae e disco, ou seja, quase todos os grupos - e o
        /// atalho existe justamente porque o caro na biblioteca e preparar cada
        /// grupo. Uma vez a cada balde a leitura se abre, tira a amostra e volta
        /// a fechar.
        /// </summary>
        /// <summary>
        /// Tudo que precisa de historico: os cartoes da aba Metricas MAIS os
        /// blocos da tela de bordo.
        ///
        /// Os segundos nao sao escolhidos por ninguem - saem de uma regra fixa -
        /// e por isso era facil esquece-los aqui. Foi o que aconteceu: os blocos
        /// abriam sem curva, porque a serie deles nunca chegou a ser gravada.
        /// A gravacao acontece no ciclo do mostrador, com a janela fechada, entao
        /// nao adianta a janela saber quais sao; quem tem de saber e isto.
        /// </summary>
        private List<string> IdsAcompanhados()
        {
            List<string> ids = new List<string>(_cfg.MetricIds);

            foreach (SensorEntry s in MetricPicker.Destaques(_cache))
                if (s != null && !string.IsNullOrEmpty(s.Id) && !ids.Contains(s.Id))
                    ids.Add(s.Id);

            // Inclui os perfis que podem entrar por jogo ou rodizio. Assim uma
            // troca nao remove a curva anterior nem estreia sem historico.
            foreach (Profile a in _cfg.Profiles)
                foreach (string id in new string[] { a.Panel1Id, a.Panel2Id })
                    if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id);

            return ids;
        }

        private List<string> Foco(bool comMetricas)
        {
            if (_janelaAberta) return null;

            List<string> ids = new List<string>();
            Profile a = _live;
            if (a != null) { ids.Add(a.Panel1Id); ids.Add(a.Panel2Id); }

            Profile[] roda = _rotation;
            if (roda != null)
                foreach (Profile p in roda) { ids.Add(p.Panel1Id); ids.Add(p.Panel2Id); }

            if (comMetricas) ids.AddRange(MetricHistory.Seguidos);
            return ids;
        }

        private volatile bool _janelaAberta = false;

        private void OnToggleAutostart(object sender, EventArgs e)
        {
            bool target = !_miAutostart.Checked;
            bool ok = target ? Autostart.Enable() : Autostart.Disable();
            if (ok) _miAutostart.Checked = target;
            else NotificarComAcao(T.AppName, Autostart.UltimaRecusa ?? T.AutostartFailed + Log.Path,
                    delegate { OnToggleAutostart(null, EventArgs.Empty); });
        }

        /// <summary>Os dialogos do arranque, ja com o mostrador funcionando.</summary>
        private void AvisosDoArranque(Exception falha)
        {
            if (IsStopping) return;
            if (falha != null)
                Avisar(T.SensorInitFailed(falha.Message, Log.Path),
                    T.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            WarnAboutOriginalTask();
        }

        /// <summary>Substitui o MessageBox do arranque; os testes respondem por aqui.</summary>
        internal Func<string, string, MessageBoxButtons, MessageBoxIcon, DialogResult> MostrarAviso;

        private DialogResult Avisar(string texto, string titulo, MessageBoxButtons botoes, MessageBoxIcon icone)
        {
            if (MostrarAviso != null) return MostrarAviso(texto, titulo, botoes, icone);
            // So informa: vira notificacao da bandeja, sem travar o arranque.
            if (botoes == MessageBoxButtons.OK) { NotificarComAcao(titulo, texto, null); return DialogResult.OK; }
            return MessageBox.Show(texto, titulo, botoes, icone);
        }

        /// <summary>A tarefa do software de fabrica disputaria o painel a cada logon.</summary>
        private void WarnAboutOriginalTask()
        {
            try
            {
                if (!Autostart.OriginalAppTaskEnabled()) return;
                DialogResult r = Avisar(T.OriginalTaskWarning,
                    T.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r == DialogResult.Yes) Autostart.DisableOriginalAppTask();
            }
            catch (Exception ex) { Log.Error("verificacao da tarefa original", ex); }
        }

        /// <summary>
        /// Preenche so o painel vazio. Trocar os dois quando faltava um apagava
        /// a escolha do usuario no outro.
        /// </summary>
        private void PickDefaults(Profile p)
        {
            if (string.IsNullOrEmpty(p.Panel1Id))
                foreach (SensorEntry s in _cache)
                    if (s.Type == SensorType.Temperature && s.Label.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0)
                    { p.Panel1Id = s.Id; break; }

            if (string.IsNullOrEmpty(p.Panel2Id))
                foreach (SensorEntry s in _cache)
                    if (s.Type == SensorType.Load && s.Label.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0)
                    { p.Panel2Id = s.Id; break; }
        }

        // ---------------- thread de atualizacao ----------------

        private void WorkerLoop()
        {
            Stopwatch clock = Stopwatch.StartNew();
            long nextFull = 0;
            long nextFast = 0;
            while (!_stop.WaitOne(0))
            {
                long now = clock.ElapsedMilliseconds;
                bool fast = PrecisaDoCaminhoRapido();
                bool full = now >= nextFull;
                if (!_paused)
                {
                    try
                    {
                        if (full)
                        {
                            UpdateOnce(true);
                            nextFull = ProximoInstante(nextFull, clock.ElapsedMilliseconds, FullPeriodMs);
                            nextFast = clock.ElapsedMilliseconds + FastPeriodMs;
                        }
                        else if (fast && now >= nextFast)
                        {
                            UpdateOnce(false);
                            nextFast = ProximoInstante(nextFast, clock.ElapsedMilliseconds, FastPeriodMs);
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Exchange(ref _snapshot, ReadingSnapshot.Failed);
                        // Avancar o prazo mesmo quando o ciclo falha. Um prazo
                        // vencido faria o laço repetir a excecao a cada 1 ms.
                        nextFull = ProximoInstante(nextFull, clock.ElapsedMilliseconds, FullPeriodMs);
                        // O caminho rapido reutiliza hardware do ultimo ciclo
                        // completo. Nao renovar a validade desses dados na falha.
                        nextFast = nextFull;
                        Log.Error("ciclo de atualizacao", ex);
                        SetTooltip("Mhiagos Control - erro: " + ex.Message);
                    }
                }
                else
                {
                    if (full) nextFull = ProximoInstante(nextFull, now, FullPeriodMs);
                    nextFast = now + FastPeriodMs;
                }

                now = clock.ElapsedMilliseconds;
                long target = fast ? Math.Min(nextFull, nextFast) : nextFull;
                long wait = target - now;
                if (_stop.WaitOne((int)Math.Max(1, wait))) break;
            }
            Log.Write("thread de atualizacao encerrada");
        }

        internal static long ProximoInstante(long previous, long now, int period)
        {
            long next = previous + period;
            if (next <= now) next += ((now - next) / period + 1) * period;
            return next;
        }

        internal static bool PerfilUsaRtss(Profile profile)
        {
            return profile != null &&
                (EhRtssVivo(profile.Panel1Id) || EhRtssVivo(profile.Panel2Id));
        }

        private static bool EhRtssVivo(string id)
        {
            return string.Equals(id, Rtss.Prefixo + "fps", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(id, Rtss.Prefixo + "frametime", StringComparison.OrdinalIgnoreCase);
        }

        private static bool EhRtss(string id)
        {
            return !string.IsNullOrEmpty(id) &&
                   id.StartsWith(Rtss.Prefixo, StringComparison.OrdinalIgnoreCase);
        }

        private bool PrecisaDoCaminhoRapido()
        {
            if (_fastSensors == null) return false;
            // Com a janela aberta, cartões e prévias também recebem RTSS vivo,
            // mesmo que o perfil atualmente enviado ao LCD use só hardware.
            if (_snapshotWanted) return true;
            if (PerfilUsaRtss(_live)) return true;
            Profile[] rotation = _rotation;
            if (rotation != null)
                foreach (Profile profile in rotation)
                    if (PerfilUsaRtss(profile)) return true;
            return false;
        }

        private void UpdateOnce(bool full)
        {
            Profile vigiar = _live;
            if (vigiar == null) return;   // ainda nao publicado

            // O que vai ao mostrador pode não ser o perfil ativo: o rodízio
            // gira o quadro, enquanto a configuração ativa continua intacta.
            Profile cfg = Rodar(vigiar);
            bool mesmo = ReferenceEquals(cfg, vigiar) || cfg.Name == vigiar.Name;

            MonitorReadings readings;
            lock (_sensorLock)
            {
                if (full)
                {
                    // Ciclo de amostra: a leitura se abre para os grupos dos cartoes
                    // antes do Refresh, senao os sensores fora do foco viriam com o
                    // valor do ultimo ciclo em que foram lidos - historico de mentira.
                    bool amostrar = MetricHistory.HoraDeAmostrar();
                    if (amostrar) _sensors.Focar(Foco(true));

                    readings = _sensorCycle.Refresh(cfg);
                    if (_snapshotWanted)
                        Interlocked.Exchange(ref _snapshot, ReadingSnapshot.Full(_sensors.Snapshot(), Stopwatch.GetTimestamp()));

                    if (amostrar)
                    {
                        MetricHistory.Amostrar(Ler);
                        _sensors.Focar(Foco(false));
                    }
                }
                else
                {
                    _fastSensors.RefreshFast();
                    readings = _sensorCycle.Read(cfg);
                    if (_snapshotWanted) PublicarSnapshotRapido();
                }
            }

            if (full) MetricHistory.SalvarSeVencido();   // fora do lock: escreve em disco

            bool ocioso = Ocioso();
            PanelDispatch envio = _panelCycle.Prepare(cfg, readings.Display1, readings.Display2, ocioso);
            _panelKeepalive.Publish(envio.Frame);
            PanelValue v1 = envio.Panel1;
            PanelValue v2 = envio.Panel2;

            bool ok = _panelKeepalive.LastSent;

            if (!full) return;

            string text = string.Format(CultureInfo.InvariantCulture, "Mhiagos Control  {0}{1} / {2}{3}",
                v1.Value.HasValue ? v1.Value.Value.ToString(CultureInfo.InvariantCulture) : "--",
                cfg.Fahrenheit ? "F" : "C",
                v2.Value.HasValue ? v2.Value.Value.ToString(CultureInfo.InvariantCulture) : "--",
                cfg.Percent ? "%" : "W");
            if (!mesmo) text += "  " + cfg.Name;
            if (ocioso) text += T.TagIdle;
            if (v1.Clamped || v2.Clamped) text += T.TagOver;
            if (!ok) text += T.TagNoDevice;
            SetTooltip(ComBateria(text, Perifericos.Resumo()));

            Periferico[] perifericos = Perifericos.Atual;
            foreach (KeyValuePair<Periferico, int> aviso in _avisosDeBateria.Verificar(perifericos))
                AvisarBateria(aviso.Key, aviso.Value);
            try { HistoricoDeBateria.Registrar(perifericos, DateTime.Now); }
            catch (Exception ex) { Log.Error("historico de bateria", ex); }

            foreach (Periferico cheio in _avisosDeBateria.VerificarCargaCompleta(perifericos))
                Notificar(T.BatteryFullTitle(cheio.Nome), T.BatteryFullText, ToolTipIcon.Info, "carga completa: " + cheio.Nome);
            foreach (KeyValuePair<Periferico, double> pouco in _avisosDeBateria.VerificarTempo(perifericos, HistoricoDeBateria.Restante))
                Notificar(T.BatteryTimeTitle(pouco.Key.Nome, HistoricoDeBateria.DuracaoAproximada(pouco.Value)),
                          T.BatteryTimeText, ToolTipIcon.Warning, "pouco tempo de bateria: " + pouco.Key.Nome);

            AtualizarIconeDaBandeja(perifericos);
        }

        /// <summary>Mescla somente FPS/frametime no instantâneo da janela.</summary>
        private void PublicarSnapshotRapido()
        {
            ReadingSnapshot previous = Interlocked.CompareExchange(ref _snapshot, null, null) ?? ReadingSnapshot.Empty;
            Interlocked.Exchange(ref _snapshot, previous.WithFast(_fastSensors.FastSnapshot(), Stopwatch.GetTimestamp()));
        }

        private void OnPanelConnectionChanged(bool connected)
        {
            Log.Write(connected ? "painel conectado" : "painel ausente ou envio falhou");
        }

        /// <summary>
        /// Le um sensor para o historico. Chamada ja sob _sensorLock.
        ///
        /// Um identificador que sumiu - placa trocada, disco removido, sensor
        /// que a fonte parou de publicar - devolve nulo, e o balde daquele
        /// instante fica marcado como falha. Nao e erro, e ausencia.
        /// </summary>
        private float? Ler(string id)
        {
            SensorEntry e = _sensors.ReadEntry(id);
            return e != null ? e.Value : null;
        }

        /// <summary>
        /// Perfil que vai ao mostrador neste ciclo.
        ///
        /// Quando o rodizio esta ligado ele avanca sozinho pela roda; quando
        /// nao esta, devolve o ativo e zera o indice, para que ligar o rodizio
        /// mais tarde comece do inicio em vez de saltar para o meio.
        ///
        /// Nao usa o relogio de parede para contar: DateTime.UtcNow anda com o
        /// horario de verao e com a sincronizacao de rede, e um ajuste de meia
        /// hora congelaria o mostrador nesse perfil ate a hora passar.
        /// </summary>
        private Profile Rodar(Profile ativo)
        {
            Profile[] roda = _rotation;
            int periodo = _rotateMs;
            if (roda == null || roda.Length < 2 || periodo <= 0)
            {
                _rotIndex = 0;
                _rotTicks = 0;
                return ativo;
            }

            long agora = Stopwatch.GetTimestamp();
            if (_rotTicks == 0) _rotTicks = agora;

            long periodoTicks = (long)periodo * Stopwatch.Frequency / 1000;
            _rotIndex = IndiceDoRodizio(agora - _rotTicks, periodoTicks, roda.Length);
            return roda[_rotIndex];
        }

        /// <summary>
        /// Posicao na roda a partir do tempo decorrido.
        ///
        /// Derivar do relogio em vez de somar um a cada volta e o que impede a
        /// deriva: o ciclo completo dura 1 s mais o que a varredura do hardware levar,
        /// e um contador incrementado "quando der" acumularia esse resto ate o
        /// rodizio de 20 s virar de 23. Aqui cada instante tem uma posicao so,
        /// e perder um ciclo nao desalinha nada.
        /// </summary>
        internal static int IndiceDoRodizio(long decorrido, long periodo, int tamanho)
        {
            if (tamanho <= 0) return 0;
            if (periodo <= 0 || decorrido < 0) return 0;
            return (int)((decorrido / periodo) % tamanho);
        }

        /// <summary>
        /// Ha quanto tempo ninguem toca no teclado nem no mouse.
        ///
        /// GetLastInputInfo mede a sessao inteira, e nao so este processo -
        /// e o que se quer: o computador estar em uso, e nao esta janela.
        /// Vale notar o que ele NAO ve: assistir video ou esperar uma
        /// renderizacao demorada conta como ocioso, porque ninguem digita.
        /// </summary>
        private bool Ocioso()
        {
            int limite = _idleBlankMs;
            if (limite <= 0) { Registrar(false); return false; }

            bool ocioso;
            try
            {
                LASTINPUTINFO li = new LASTINPUTINFO();
                // qualificado: esta classe tem um metodo Marshal proprio
                li.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(LASTINPUTINFO));
                if (!GetLastInputInfo(ref li)) { Registrar(false); return false; }

                // Aritmetica sem sinal de proposito: GetTickCount da a volta a
                // cada 49,7 dias, e a subtracao em uint continua certa na volta.
                uint parado = (uint)Environment.TickCount - li.dwTime;
                ocioso = parado >= (uint)limite;
            }
            catch (Exception ex) { Log.Error("leitura da ociosidade", ex); return false; }

            Registrar(ocioso);
            return ocioso;
        }

        private void Registrar(bool ocioso)
        {
            if (ocioso == _apagado) return;
            _apagado = ocioso;
            Log.Write(ocioso ? "mostrador apagado por ociosidade" : "mostrador religado");
        }

        /// <summary>
        /// Acrescenta a linha da bateria dos perifericos a dica do icone.
        ///
        /// O NotifyIcon do .NET Framework recusa mais de 63 caracteres. Quando
        /// nao cabe, quem encolhe e a primeira linha: as etiquetas do fim dela
        /// ([ocioso], [painel ausente]) valem menos que a carga, que e o que a
        /// pessoa veio olhar ao passar o mouse.
        /// </summary>
        /// <summary>So a thread de atualizacao toca nele.</summary>
        private readonly AvisosDeBateria _avisosDeBateria = new AvisosDeBateria();
    }
}
