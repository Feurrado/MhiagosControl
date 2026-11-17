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
        /// <summary>A proxima abertura vai para a janela classica (item da bandeja ou botao da nova).</summary>
        private bool _classicaPedida;

        private void AbrirJanelaNova(string pagina)
        {
            if (IsStopping) return;
            if (IsStarting) { _openWhenReady = true; MostrarSplash(); return; }
            // As duas janelas nao editam a configuracao ao mesmo tempo: a antiga
            // trabalha num rascunho e gravaria por cima do que a nova mudou.
            if (_settingsForm != null && !_settingsForm.IsDisposed) { TrazerParaFrente(_settingsForm); return; }
            if (_janelaNova != null && !_janelaNova.IsDisposed)
            {
                if (pagina != null) _janelaNova.MostrarPagina(pagina);
                TrazerParaFrente(_janelaNova);
                return;
            }

            // Como na janela antiga: com a janela aberta o ciclo le todos os sensores.
            _janelaAberta = true;
            AjustarFoco();
            ReListSensors();
            _snapshotWanted = true;

            JanelaDoApp j = new JanelaDoApp(this, pagina);
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
            j.Shown += delegate { TrazerParaFrente(j); };
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
            Marshal(delegate { _paginaPedida = chave; _classicaPedida = true; PedirConfiguracao(); });
        }

        /// <summary>
        /// A pagina da interface nova para uma chave da janela classica (avisos,
        /// widget e Casa pedem paginas pelo nome antigo). Nulo abre a de sempre.
        /// </summary>
        internal static string PaginaNova(string chave)
        {
            switch (chave)
            {
                case "perfis": case "painel": case "paineis": return "painel";
                case "configuracoes": case "config": return "config";
                case "especificacoes": case "specs": return "specs";
                case "visao": case "metricas": case "baterias": case "casa": case "clima": case "sobre": return chave;
                case "casa:clima": return chave;   // a pagina Clima do app (o tempo no widget)
                default: return null;
            }
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

        void IAppNovo.TodosOsSensores(bool todos)
        {
            lock (_sensorLock) _sensors.ShowAll = todos;
            ReListSensors();
        }

        void IAppNovo.IdiomaMudou()
        {
            // O menu da bandeja e montado com os textos do idioma: refaz.
            ContextMenuStrip antigo = _icon.ContextMenuStrip;
            BuildMenu();
            if (_miPause != null) _miPause.Text = _paused ? T.TrayResume : T.TrayPause;
            if (_miAutostart != null) _miAutostart.Checked = Autostart.IsEnabled();
            if (antigo != null && antigo != _icon.ContextMenuStrip) antigo.Dispose();
            Publicar();
        }

        ConfigDoWidget IAppNovo.Widget { get { return _cfgWidget; } }
        void IAppNovo.LigarWidget(bool ligar) { ((IControleDoWidget)this).LigarWidget(ligar); }
        void IAppNovo.WidgetMudou() { ((IControleDoWidget)this).WidgetMudou(); }
        void IAppNovo.WidgetAoCanto() { ((IControleDoWidget)this).WidgetAoCanto(); }

        bool IAppNovo.InicioComWindows { get { return Autostart.IsEnabled(); } }

        bool IAppNovo.DefinirInicioComWindows(bool ligado)
        {
            bool ok = ligado ? Autostart.Enable() : Autostart.Disable();
            if (ok && _miAutostart != null) _miAutostart.Checked = ligado;
            return ok;
        }
    }
}
