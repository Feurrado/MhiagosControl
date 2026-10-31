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
    public partial class TrayContext : ApplicationContext, ISettingsData, IPanelStatusData, IReadingStatusData
    {
        private void OnPause(object sender, EventArgs e)
        {
            _paused = !_paused;
            Interlocked.Exchange(ref _snapshot, ReadingSnapshot.Empty);
            _panelKeepalive.Enabled = !_paused;
            _miPause.Text = _paused ? T.TrayResume : T.TrayPause;
            Log.Write(_paused ? "pausado" : "retomado");
            if (_paused) _icon.Text = T.TrayPaused;
        }

        private void OnOpenData(object sender, EventArgs e)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", Paths.DataDir); }
            catch (Exception ex) { Log.Error("abrir pasta de dados", ex); }
        }

        /// <summary>
        /// SessionEnded, e nao SessionEnding: o logoff ainda pode ser cancelado
        /// depois do SessionEnding, por outro programa ou pelo usuario. Parar
        /// tudo ali deixava o processo vivo sem icone, sem threads e dono do
        /// mutex - o painel apagava e abrir o app de novo nao fazia nada.
        /// </summary>
        private void OnSessionEnded(object sender, Microsoft.Win32.SessionEndedEventArgs e)
        {
            Log.Write("sessao do Windows encerrando");
            Sair(false);
        }

        private void OnExit(object sender, EventArgs e)
        {
            Sair(true);
        }

        /// <summary>
        /// Sai do aplicativo, fechando a configuracao ANTES de desmontar.
        ///
        /// A ordem era a inversa: Shutdown parava threads, painel, sensores e
        /// icone, e so depois o Application.Exit perguntava a janela aberta se
        /// podia fechar. Com edicao pendente ela oferecia Cancelar - e o
        /// Cancelar cancelava a saida inteira, deixando um processo sem icone,
        /// com o mostrador parado e dono do mutex. Abrir o app de novo so
        /// acendia o evento de ativacao, cuja thread ja tinha terminado.
        ///
        /// Perguntar = falso e para quem nao pode ouvir um nao: o instalador e o
        /// fim da sessao do Windows. Ai a edicao pendente e descartada calada.
        /// </summary>
        internal bool Sair(bool perguntar)
        {
            if (IsStopping) return true;
            if (!FecharConfiguracao(perguntar))
            {
                Log.Write("saida cancelada na janela de configuracao");
                return false;
            }
            Shutdown();
            Application.Exit();
            return true;
        }

        /// <summary>Fecha a janela de configuracao, se houver; devolve se ela aceitou.</summary>
        internal bool FecharConfiguracao(bool perguntar)
        {
            SettingsForm f = _settingsForm;
            if (f == null || f.IsDisposed) return true;
            if (perguntar) f.Close();
            else f.FecharSemPerguntar();

            // Modal, a janela aceita marcando o DialogResult e some quando o
            // ShowDialog voltar; recusando, o WinForms o devolve a None. Sem
            // ser modal, aceitar ja a descarta.
            return f.IsDisposed || f.DialogResult != DialogResult.None;
        }

        private void Shutdown()
        {
            int previous = Interlocked.Exchange(ref _runtimeState, (int)RuntimeState.Stopping);
            if (previous >= (int)RuntimeState.Stopping) return;

            try { Microsoft.Win32.SystemEvents.SessionEnded -= new Microsoft.Win32.SessionEndedEventHandler(OnSessionEnded); } catch { }

            if (_relogioDoJogo != null)
            {
                _relogioDoJogo.Stop();
                _relogioDoJogo.Tick -= new EventHandler(OnVigiaDeJogo);
                _relogioDoJogo.Dispose();
                _relogioDoJogo = null;
            }
            if (_demora != null) { _demora.Stop(); _demora.Dispose(); _demora = null; }

            _stop.Set();
            _panelKeepalive.Stop();
            Stopwatch budget = Stopwatch.StartNew();
            bool workerParou = EsperarThread(_worker, "atualizacao", budget);
            bool painelParou = EsperarKeepalive(budget);
            bool aberturaParou = EsperarThread(_sensorInit, "abertura de sensores", budget);
            bool ativacaoParou = EsperarThread(_activationWatcher, "ativacao", budget);

            // Depois de a thread parar: gravar com ela viva copiaria uma serie
            // no meio de um balde sendo fechado. Pelo mesmo motivo, se uma
            // thread resistir ao timeout, nao fechamos painel ou sensores sob os
            // pes dela: o processo esta saindo e o Windows libera os handles.
            // Os leitores de bateria nao tocam no painel nem nos sensores: um G HUB
            // lento para responder nao pode impedir o painel de ser fechado. So o
            // _stop, em que eles esperam, depende de terem parado.
            bool podeDescartarRecursos = workerParou && painelParou && aberturaParou && ativacaoParou;
            try { MetricHistory.Salvar(); } catch (Exception ex) { Log.Error("gravacao do historico", ex); }
            if (!podeDescartarRecursos)
                Log.Write("encerramento: recursos de hardware ficarao para o termino do processo");

            if (_icon != null) { _icon.Visible = false; _icon.Dispose(); }
            // _iconNormal e o Assets.TrayIcon, guardado e compartilhado (pode ser o
            // icone de reserva das janelas): nao e deste dono para descartar.

            if (podeDescartarRecursos)
            {
                try { _panel.Close(); } catch (Exception ex) { Log.Error("fechamento do painel", ex); }
                lock (_sensorLock)
                {
                    try { _sensors.Dispose(); } catch (Exception ex) { Log.Error("fechamento dos sensores", ex); }
                }
            }
            _panelKeepalive.Dispose();
            if (_marshal != null) _marshal.Dispose();
            if (podeDescartarRecursos) _stop.Dispose();

            Volatile.Write(ref _runtimeState, (int)RuntimeState.Stopped);
            Log.Write("=== encerrado ===");
        }

        /// <summary>
        /// Espera uma thread cooperativa sem permitir que um driver lento vire
        /// descarte concorrente de handles. Threads do aplicativo sao de fundo;
        /// no raro timeout, o termino do processo faz a limpeza final.
        /// </summary>
        private static bool EsperarThread(Thread thread, string nome, Stopwatch budget)
        {
            if (thread == null || !thread.IsAlive) return true;
            int remaining = ShutdownTimeoutMs - (int)budget.ElapsedMilliseconds;
            if (remaining > 0 && thread.Join(remaining)) return true;
            Log.Write("thread de " + nome + " nao encerrou a tempo");
            return false;
        }

        private bool EsperarKeepalive(Stopwatch budget)
        {
            int remaining = ShutdownTimeoutMs - (int)budget.ElapsedMilliseconds;
            if (_panelKeepalive.Wait(Math.Max(0, remaining))) return true;
            Log.Write("thread de keepalive nao encerrou a tempo");
            return false;
        }
    }
}
