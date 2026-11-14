using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// A janela nova (JanelaDoApp) vista da TrayContext: abrir, fechar e o que a
    /// ponte precisa do app. Diferente da janela antiga, ela nao e modal e edita
    /// a configuracao viva: cada alteracao e gravada e publicada na hora.
    /// </summary>
    public partial class TrayContext : IAppNovo
    {
        private JanelaDoApp _janelaNova;

        private void OnAppNovo(object sender, EventArgs e)
        {
            try { AbrirJanelaNova(); }
            catch (Exception ex) { Log.Error("interface nova: abrir", ex); }
        }

        private void AbrirJanelaNova()
        {
            if (IsStopping) return;
            if (IsStarting) { MostrarSplash(); return; }
            // As duas janelas nao editam a configuracao ao mesmo tempo: a antiga
            // trabalha num rascunho e gravaria por cima do que a nova mudou.
            if (_settingsForm != null && !_settingsForm.IsDisposed) { TrazerParaFrente(_settingsForm); return; }
            if (_janelaNova != null && !_janelaNova.IsDisposed)
            {
                if (_janelaNova.WindowState == FormWindowState.Minimized) _janelaNova.WindowState = FormWindowState.Normal;
                _janelaNova.Activate();
                return;
            }

            // Como na janela antiga: com a janela aberta o ciclo le todos os sensores.
            _janelaAberta = true;
            AjustarFoco();
            ReListSensors();
            _snapshotWanted = true;

            JanelaDoApp j = new JanelaDoApp(this);
            _janelaNova = j;
            j.FormClosed += delegate
            {
                if (_janelaNova == j) _janelaNova = null;
                if (IsStopping) return;
                if (_settingsForm != null && !_settingsForm.IsDisposed) return;   // a antiga assumiu
                _snapshotWanted = false;
                _janelaAberta = false;
                Publicar();
            };
            j.Show();
        }

        /// <summary>A janela antiga vai abrir: a nova sai, para so uma editar a configuracao.</summary>
        private void FecharJanelaNova()
        {
            JanelaDoApp j = _janelaNova;
            if (j == null || j.IsDisposed) return;
            try { j.Close(); } catch (Exception ex) { Log.Error("interface nova: fechar", ex); }
        }

        Config IAppNovo.Cfg { get { return _cfg; } }

        List<SensorEntry> IAppNovo.Sensores() { return _cache ?? new List<SensorEntry>(); }

        Dictionary<string, float> IAppNovo.Leituras() { return GetSnapshot(); }

        PanelStatus IAppNovo.Painel() { return ((IPanelStatusData)this).CurrentPanelStatus(); }

        bool IAppNovo.Pausado { get { return _paused; } }

        void IAppNovo.AlternarPausa() { OnPause(null, EventArgs.Empty); }

        void IAppNovo.Aplicar()
        {
            if (_live != null && _live.Name != _cfg.ActiveName) RegistrarEscolhaManual();
            MetricHistory.Seguir(IdsAcompanhados());
            Publicar();
            RebuildProfileMenu();
        }

        void IAppNovo.AbrirAntiga(string pagina)
        {
            // O pedido chega de dentro de um evento do WebView2, e a janela antiga
            // e modal: abrir aqui fecharia a janela nova no meio do proprio evento.
            // Fica para a proxima volta da fila de mensagens.
            string chave = PaginaAntiga(pagina);
            Marshal(delegate { _paginaPedida = chave; PedirConfiguracao(); });
        }

        /// <summary>So chaves conhecidas da janela antiga; qualquer outra coisa abre a pagina de sempre.</summary>
        internal static string PaginaAntiga(string pagina)
        {
            switch (pagina)
            {
                case "painel": return "perfis";
                case "baterias": case "casa": case "sobre": return pagina;
                default: return null;
            }
        }

        bool IAppNovo.InicioComWindows { get { return Autostart.IsEnabled(); } }

        bool IAppNovo.DefinirInicioComWindows(bool ligado)
        {
            bool ok = ligado ? Autostart.Enable() : Autostart.Disable();
            if (ok && _miAutostart != null) _miAutostart.Checked = ligado;
            return ok;
        }
    }
}
