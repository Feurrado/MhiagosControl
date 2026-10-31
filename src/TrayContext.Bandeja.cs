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

        private void Notificar(string titulo, string texto, ToolTipIcon icone, string registro)
        {
            Log.Write(registro);
            Marshal(delegate
            {
                try
                {
                    // com o icone do app no lugar do triangulo/i do Windows;
                    // se o caminho nativo falhar, cai no balao comum
                    if (_icon != null && _icon.Visible && !AvisoComIcone.Mostrar(_icon, titulo, texto))
                        _icon.ShowBalloonTip(10000, titulo, texto, icone);
                }
                catch (Exception ex) { Log.Error("notificacao de bateria", ex); }
            });
        }

        private Action _acaoDoBalao;

        /// <summary>
        /// Notificacao de falha recuperavel: clicar nela refaz o que falhou.
        /// O balao do Windows nao tem botoes proprios, entao o clique no balao
        /// e a acao, e o texto diz isso.
        /// </summary>
        private void NotificarComAcao(string titulo, string texto, Action acao)
        {
            _acaoDoBalao = acao;
            string dica = acao == null ? "" : "\n" + T.ClickToRetry;
            Notificar(titulo, texto + dica, ToolTipIcon.Warning, "aviso com acao: " + texto);
        }

        private void ExecutarAcaoDoBalao()
        {
            Action a = _acaoDoBalao;
            _acaoDoBalao = null;
            if (a == null) return;
            try { a(); }
            catch (Exception ex) { Log.Error("acao do balao", ex); }
        }

        // ---------------- icone da bandeja com alerta ----------------

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private void SetTooltip(string text)
        {
            if (text.Length > 63) text = text.Substring(0, 60) + "...";
            // Uma dica enfileirada pelo worker antes da pausa nao pode apagar o
            // "Pausado" que o menu acabou de escrever.
            Marshal(delegate { try { if (!_paused) _icon.Text = text; } catch { } });
        }

        /// <summary>NotifyIcon so pode ser tocado na thread de UI.</summary>
        private void Marshal(MethodInvoker action)
        {
            try
            {
                if (_marshal != null && _marshal.IsHandleCreated && !_marshal.IsDisposed)
                    _marshal.BeginInvoke(action);
            }
            catch (Exception ex) { Log.Error("marshaling para a UI", ex); }
        }
    }
}
