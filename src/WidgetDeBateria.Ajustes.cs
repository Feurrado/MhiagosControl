using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    public sealed partial class WidgetDeBateria : Form
    {
        private void TrocarVidro(bool nitido, bool fosco)
        {
            if (_cfg.Nitido == nitido && _cfg.Fosco == fosco) return;
            _cfg.Nitido = nitido;
            _cfg.Fosco = fosco;
            _cfg.Salvar();
            AplicarDesfoque();
            Renderizar();
        }

        private void TrocarCor(CorDoVidro cor)
        {
            if (_cfg.Cor == cor) return;
            _cfg.Cor = cor;
            _cfg.Salvar();
            Renderizar();
        }

        /// <summary>Liga e desliga a casa no widget; a pagina Baterias e os Ajustes da Casa acompanham.</summary>
        internal void TrocarCasa(bool casa)
        {
            _cfg.Casa = casa;
            _cfg.Salvar();
            _casa.Ativa = casa;
            _casa.Puxar();
            _zonaDaDica = -1;
            Renderizar();
            ManterNaTela();
            CasaNoWidget.Avisar();
        }

        private void TrocarFrente(bool frente)
        {
            _cfg.NaFrente = frente;
            _cfg.Salvar();
            AplicarFrente();
        }

        /// <summary>
        /// Reaplica a configuracao inteira, ja salva por quem mudou: a pagina
        /// Baterias mexe nos campos e chama isto.
        /// </summary>
        internal void AplicarConfig()
        {
            if (IsDisposed || !IsHandleCreated) return;
            _zonaDaDica = -1;
            _casa.Ativa = _cfg.Casa;
            _casa.Puxar();
            AplicarDesfoque();
            AplicarFrente();
            Renderizar();
            ManterNaTela();
        }

        /// <summary>Volta para o canto de cima a direita da tela principal.</summary>
        internal void VoltarAoCanto()
        {
            if (IsDisposed || !IsHandleCreated) return;
            Posicionar();
        }

        private void AplicarFrente() { _camada.AplicarFrente(); }

        protected override void WndProc(ref Message m)
        {
            if (_camada != null && _camada.Mensagem(ref m)) return;
            base.WndProc(ref m);
        }

        private void Afundar() { _camada.Afundar(); }

        /// <summary>
        /// A janela dos icones da area de trabalho: o Progman, ou o WorkerW que
        /// o Explorer cria por cima dele quando ha apresentacao de papel de parede.
        /// </summary>
        private static IntPtr AreaDeTrabalho()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero && FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                return progman;
            IntPtr w = IntPtr.Zero;
            while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
                if (FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return w;
            return progman;
        }
    }
}
