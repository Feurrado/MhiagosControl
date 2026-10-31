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
        /// Notificacao do Windows pela bandeja. No Windows 10 e 11 o balao do
        /// NotifyIcon vira uma notificacao comum, com o nome do app e lugar
        /// na central de notificacoes - sem registrar AUMID nem atalho.
        /// </summary>
        private void AvisarBateria(Periferico p, int limiar)
        {
            string titulo = T.BatteryLowTitle(p.Nome, p.Bateria ?? limiar);
            string texto = limiar <= AvisosDeBateria.Critica ? T.BatteryCriticalText : T.BatteryLowText;
            Notificar(titulo, texto, ToolTipIcon.Warning, "aviso de bateria: " + p.Nome + " " + p.Bateria + "%");
        }

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

        /// <summary>
        /// A pagina Casa em segundo plano: alertas, timer de desligar e historico
        /// do ar correm com a janela fechada. So o executavel liga isto; os
        /// testes montam o TrayContext sem falar com a nuvem.
        /// </summary>
        internal void IniciarCasa()
        {
            try
            {
                ServicoCasa.SemBipe = _cfg.CasaSemBipe;
                // So o executavel esconde perifericos: os testes contam com o G325.
                Perifericos.Ocultos = Perifericos.LerOcultos(_cfg.PerifericosOcultos);
                ServicoCasa.Notificar = delegate(string titulo, string texto)
                {
                    Notificar(titulo, texto, ToolTipIcon.Warning, "casa: " + titulo + " - " + texto);
                };
                ServicoCasa.Iniciar();
                // Os eventos da Visao geral contam desde o arranque, com a janela fechada tambem.
                EventosDaVisao.Iniciar();
                // A temperatura la fora (Open-Meteo), se houver cidade escolhida.
                Clima.Iniciar();
                // A casa no widget de bateria: o clique num item dela abre a pagina Casa, fora do clique (a janela e modal).
                CasaNoWidget.AbrirCasa = delegate { Marshal(delegate { _paginaPedida = "casa"; PedirConfiguracao(); }); };
            }
            catch (Exception ex) { Log.Error("iniciar a pagina Casa", ex); }
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

        private Icon _iconBaixa;
        private IntPtr _hIconBaixa = IntPtr.Zero;
        private bool _mostrandoBaixa = false;

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        /// <summary>
        /// Algum aparelho fora do cabo com 30% ou menos? O mesmo limiar do
        /// primeiro aviso: a notificacao passa, a pilhinha no icone fica.
        /// </summary>
        internal static bool AlgumBaixo(Periferico[] itens)
        {
            if (itens == null) return false;
            foreach (Periferico p in itens)
                if (p != null && p.Bateria.HasValue && !p.Carregando && p.Bateria.Value <= AvisosDeBateria.Baixa)
                    return true;
            return false;
        }

        /// <summary>Troca o icone so na mudanca de estado; o desenho e feito uma vez.</summary>
        private void AtualizarIconeDaBandeja(Periferico[] itens)
        {
            bool baixo = AlgumBaixo(itens);
            if (baixo == _mostrandoBaixa) return;
            _mostrandoBaixa = baixo;
            Marshal(delegate
            {
                try
                {
                    if (_icon == null || IsStopping) return;
                    if (baixo && _iconBaixa == null) _iconBaixa = IconeComPilha(_iconNormal);
                    _icon.Icon = baixo && _iconBaixa != null ? _iconBaixa : _iconNormal;
                }
                catch (Exception ex) { Log.Error("icone da bandeja com alerta", ex); }
            });
        }

        /// <summary>O icone do app com uma pilhinha vermelha no canto de baixo.</summary>
        private Icon IconeComPilha(Icon baseIcon)
        {
            if (baseIcon == null) return null;
            using (Bitmap bmp = DesenharIconeComPilha(baseIcon, System.Windows.Forms.SystemInformation.SmallIconSize))
            {
                _hIconBaixa = bmp.GetHicon();
                return Icon.FromHandle(_hIconBaixa);
            }
        }

        internal static Bitmap DesenharIconeComPilha(Icon baseIcon, Size tam)
        {
            Bitmap bmp = new Bitmap(tam.Width, tam.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawIcon(baseIcon, new Rectangle(0, 0, tam.Width, tam.Height));

                // Pilha deitada na metade de baixo, sobre um fundo escuro para
                // ler em qualquer cor de barra de tarefas. Em 16 px cada pixel
                // conta: corpo, polo e um nivel curto, nada mais.
                float u = tam.Width / 16f;
                RectangleF corpo = new RectangleF(3 * u, tam.Height - 8 * u, 10.5f * u, 7 * u);
                using (SolidBrush fundo = new SolidBrush(Color.FromArgb(240, 24, 24, 24)))
                    g.FillRectangle(fundo, corpo.X - u, corpo.Y - u, corpo.Width + 3.5f * u, corpo.Height + 2 * u);
                using (Pen pen = new Pen(Ui.Danger, 1.2f * u))
                    g.DrawRectangle(pen, corpo.X, corpo.Y, corpo.Width, corpo.Height);
                using (SolidBrush b = new SolidBrush(Ui.Danger))
                {
                    g.FillRectangle(b, corpo.Right + 0.6f * u, corpo.Y + 2 * u, 1.4f * u, corpo.Height - 4 * u);
                    g.FillRectangle(b, corpo.X + 1.8f * u, corpo.Y + 1.8f * u, 2.6f * u, corpo.Height - 3.6f * u);
                }
            }
            return bmp;
        }

        internal static string ComBateria(string text, string bateria)
        {
            if (string.IsNullOrEmpty(bateria)) return text;
            const int Max = 63;
            if (bateria.Length > Max - 10) bateria = bateria.Substring(0, Max - 13) + "...";
            int sobra = Max - 1 - bateria.Length;
            if (text.Length > sobra) text = text.Substring(0, sobra - 3) + "...";
            return text + "\n" + bateria;
        }

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
