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
        // ---------------- acoes ----------------

        private void OnConfig(object sender, EventArgs e)
        {
            _paginaPedida = null;
            PedirConfiguracao();
        }

        private void PedirConfiguracao()
        {
            if (IsStopping) return;

            // A lista so existe depois de a thread de inicializacao publicar as
            // fontes. Abrir antes montava uma janela incompleta e concorria com
            // o primeiro Refresh. O pedido fica pendente e abre a janela assim
            // que as fontes terminarem, em vez de se perder na tela de carga.
            if (IsStarting)
            {
                _openWhenReady = true;
                MostrarSplash();
                return;
            }

            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.MostrarPagina(_paginaPedida);
                _paginaPedida = null;
                TrazerParaFrente(_settingsForm);
                return;
            }

            // ShowDialog cria um laco de mensagens aninhado. Sem esta guarda,
            // o duplo clique seguinte reentra neste metodo antes de o primeiro
            // voltar e cria outra janela modal por cima da anterior.
            if (!_settingsGate.TryEnter())
            {
                if (_settingsForm != null && !_settingsForm.IsDisposed)
                    TrazerParaFrente(_settingsForm);
                return;
            }

            try
            {
                // Antes de qualquer leitura: com a janela aberta o ciclo volta a
                // ser completo, senao o seletor listaria so os sensores que ja
                // estao no mostrador.
                _janelaAberta = true;
                AjustarFoco();

                List<SensorEntry> list;
                lock (_sensorLock)
                {
                    _sensors.Refresh();
                    list = _sensors.List();
                    Interlocked.Exchange(ref _snapshot, ReadingSnapshot.Full(_sensors.Snapshot(), Stopwatch.GetTimestamp()));
                }
                _cache = list;
                _snapshotWanted = true;

                // A troca de idioma volta como Retry: a janela nao se
                // reetiqueta viva, ela e reconstruida. Relistar entre as duas
                // aberturas tambem traduz os nomes gerados, como as medias.
                DialogResult r;
                do
                {
                    using (SettingsForm f = new SettingsForm(_cfg, _cache, this))
                    {
                        _settingsForm = f;
                        f.PaginaInicial = _paginaPedida;
                        f.Shown += delegate { TrazerParaFrente(f); };
                        _paginaPedida = null;
                        // Aplicar dentro da janela ja vale no mostrador: sem
                        // isto o perfil so mudaria ao fechar, o que faria o
                        // botao parecer sem efeito.
                        f.Applied += delegate
                        {
                            if (_live != null && _live.Name != _cfg.ActiveName)
                                RegistrarEscolhaManual();
                            Publicar();
                        };
                        r = f.ShowDialog();
                        _settingsForm = null;
                    }
                    if (r == DialogResult.Retry && !IsStopping) _cache = ReListSensors();
                }
                while (r == DialogResult.Retry && !IsStopping);

                // Saiu pela bandeja com a janela aberta: sensores e menu ja
                // foram desmontados, nao ha o que republicar.
                if (IsStopping) return;

                _snapshotWanted = false;
                _janelaAberta = false;   // antes de Publicar, que reaplica o foco

                if (r != DialogResult.OK) _cfg = Config.Load();   // descarta edicoes
                MetricHistory.Seguir(IdsAcompanhados());
                Publicar();
                RebuildProfileMenu();
            }
            catch (Exception ex)
            {
                _snapshotWanted = false;
                _janelaAberta = false;
                AjustarFoco();
                Log.Error("janela de configuracao", ex);
                ErroInesperado.Mostrar(ex);
            }
            finally
            {
                _settingsForm = null;
                _settingsGate.Exit();
            }
        }

        private static void TrazerParaFrente(Form form)
        {
            if (form == null || form.IsDisposed) return;
            try
            {
                if (form.WindowState == FormWindowState.Minimized)
                    form.WindowState = FormWindowState.Normal;
                if (!form.Visible) form.Show();
                ShowWindowAsync(form.Handle, SwRestore);

                // O Windows so deixa chamar SetForegroundWindow quem tem a entrada do
                // momento: um processo de bandeja, chamado por outro executavel ou por
                // um clique no icone, abria a janela ATRAS dos outros programas. Ligar a
                // fila de entrada desta thread a da janela em primeiro plano vale a
                // permissao por um instante.
                IntPtr primeiro = GetForegroundWindow();
                uint pid;
                uint threadDele = primeiro != IntPtr.Zero ? GetWindowThreadProcessId(primeiro, out pid) : 0;
                uint minha = GetCurrentThreadId();
                bool ligou = threadDele != 0 && threadDele != minha && AttachThreadInput(minha, threadDele, true);
                try
                {
                    BringWindowToTop(form.Handle);
                    SetForegroundWindow(form.Handle);
                    form.Activate();
                }
                finally { if (ligou) AttachThreadInput(minha, threadDele, false); }

                // Reserva: se ainda assim ficou atras, o TopMost momentaneo a leva para cima.
                if (GetForegroundWindow() != form.Handle)
                {
                    bool antes = form.TopMost;
                    form.TopMost = true;
                    form.TopMost = antes;
                }
            }
            catch (Exception ex) { Log.Error("trazer configuracao para frente", ex); }
        }

        private Dictionary<string, float> GetSnapshot()
        {
            ReadingSnapshot current = Interlocked.CompareExchange(ref _snapshot, null, null) ?? ReadingSnapshot.Empty;
            return current.Read(Stopwatch.GetTimestamp(), _paused);
        }

        ReadingState IReadingStatusData.CurrentReadingState(string sensorId)
        {
            ReadingSnapshot current = Interlocked.CompareExchange(ref _snapshot, null, null) ?? ReadingSnapshot.Empty;
            return current.State(sensorId, Stopwatch.GetTimestamp(), _paused);
        }

        Dictionary<string, float> ISettingsData.CurrentSnapshot() { return GetSnapshot(); }

        PanelStatus IPanelStatusData.CurrentPanelStatus()
        {
            return new PanelStatus(_panelKeepalive.LastFrame, _panelKeepalive.LastSent, _paused);
        }

        /// <summary>Remonta a lista de sensores sob o lock, para a janela de configuracao.</summary>
        private List<SensorEntry> ReListSensors()
        {
            lock (_sensorLock)
            {
                _cache = _sensors.List();
                return _cache;
            }
        }

        List<SensorEntry> ISettingsData.RefreshSensorList() { return ReListSensors(); }

        void ISettingsData.SetShowAllSensors(bool showAll)
        {
            lock (_sensorLock) _sensors.ShowAll = showAll;
        }
    }
}
