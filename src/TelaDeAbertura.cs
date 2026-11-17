using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// O que a janela nova mostra enquanto a pagina ainda nao esta desenhada com os dados (a primeira
    /// abertura): o logo do ventilador, o nome, um arco girando e, se demorar, "Abrindo...". Fica no
    /// lugar do retangulo escuro vazio. A pagina entra por cima com a propria animacao dela, e esta
    /// tela some sem mudar nada de lugar: ocupa a janela inteira, com a mesma cor de fundo.
    /// </summary>
    internal sealed class TelaDeAbertura : Control
    {
        private readonly Timer _giro = new Timer();
        private readonly Image _logo;
        private float _angulo;
        private string _legenda = "";

        public TelaDeAbertura()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            _logo = Assets.AppImage(128);
            _giro.Interval = 33;
            _giro.Tick += delegate { _angulo = (_angulo + 9f) % 360f; Invalidate(); };
        }

        /// <summary>O texto discreto abaixo do arco (so aparece se a abertura demorar).</summary>
        public void MostrarLegenda(string texto)
        {
            _legenda = texto ?? "";
            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) _giro.Start(); else _giro.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int cx = Width / 2;
            // O bloco (logo, nome, arco, legenda) fica um pouco acima do centro, como o olho espera.
            int lado = Ui.S(72);
            int topo = Height / 2 - Ui.S(96);
            if (_logo != null) g.DrawImage(_logo, new Rectangle(cx - lado / 2, topo, lado, lado));
            using (StringFormat f = new StringFormat { Alignment = StringAlignment.Center })
            using (SolidBrush nome = new SolidBrush(Ui.Text))
                g.DrawString("Mhiagos Control", Ui.FontSemi, nome, new PointF(cx, topo + lado + Ui.S(14)), f);

            int d = Ui.S(22);
            Rectangle arco = new Rectangle(cx - d / 2, topo + lado + Ui.S(52), d, d);
            using (Pen fundo = new Pen(Ui.Border, Math.Max(2f, Ui.S(2))))
                g.DrawEllipse(fundo, arco);
            using (Pen ponta = new Pen(Ui.Accent, Math.Max(2f, Ui.S(2))))
            {
                ponta.StartCap = LineCap.Round; ponta.EndCap = LineCap.Round;
                g.DrawArc(ponta, arco, _angulo, 100f);
            }
            if (_legenda.Length > 0)
                using (StringFormat f = new StringFormat { Alignment = StringAlignment.Center })
                using (SolidBrush m = new SolidBrush(Ui.Muted))
                    g.DrawString(_legenda, Ui.FontSemi, m, new PointF(cx, arco.Bottom + Ui.S(12)), f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _giro.Stop();
                _giro.Dispose();
                if (_logo != null) _logo.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
