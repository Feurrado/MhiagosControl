using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// O cross-fade entre paginas: a pagina que sai e a que entra, fotografadas,
    /// misturadas num veu por cima do anfitriao durante 167 ms. A entrada sobe
    /// alguns pixels com a curva de desaceleracao. Acabou o tempo, o veu some e
    /// a pagina de verdade, ja pronta por baixo, aparece no lugar - nenhuma
    /// pagina e redesenhada quadro a quadro. Sem efeitos no Windows (reduzir
    /// movimento), ou janela ainda nao exibida, nada disso acontece.
    /// </summary>
    internal sealed class VeuDePagina : Control
    {
        private Bitmap _velha, _nova;
        private long _inicio;
        private float _t;

        public VeuDePagina()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            Visible = false;
            TabStop = false;
        }

        public bool Ativo { get { return Visible; } }

        /// <summary>Retrato de um controle, ou nulo se nao der para fotografar.</summary>
        public static Bitmap Fotografar(Control c)
        {
            try
            {
                if (c == null || c.Width <= 0 || c.Height <= 0) return null;
                Bitmap b = new Bitmap(c.Width, c.Height, PixelFormat.Format32bppPArgb);
                c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
                return b;
            }
            catch { return null; }
        }

        public void Iniciar(Bitmap velha, Bitmap nova)
        {
            Encerrar();
            if (velha == null || nova == null) { if (velha != null) velha.Dispose(); if (nova != null) nova.Dispose(); return; }
            _velha = velha; _nova = nova;
            _t = 0f;
            _inicio = Motion.Agora;
            Bounds = new Rectangle(Point.Empty, Parent.ClientSize);
            Visible = true;
            BringToFront();
            Motion.Assinar(Passo);
        }

        /// <summary>Um quadro; separado para os testes. true enquanto o fade corre.</summary>
        internal bool Passo(long agora)
        {
            if (IsDisposed || !Visible) return false;
            _t = Motion.Progresso(_inicio, agora, Motion.Media);
            if (_t >= 1f) { Encerrar(); return false; }
            Invalidate();
            return true;
        }

        public void Encerrar()
        {
            Visible = false;
            if (_velha != null) { _velha.Dispose(); _velha = null; }
            if (_nova != null) { _nova.Dispose(); _nova = null; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (_velha == null || _nova == null) { g.Clear(Ui.Window); return; }
            g.Clear(Ui.Window);
            g.DrawImage(_velha, 0, 0);
            float a = Motion.Entrada(_t);
            int sobe = (int)Math.Round(Ui.S(8) * (1f - a));
            using (ImageAttributes ia = new ImageAttributes())
            {
                ColorMatrix m = new ColorMatrix();
                m.Matrix33 = a;
                ia.SetColorMatrix(m);
                g.DrawImage(_nova, new Rectangle(0, sobe, _nova.Width, _nova.Height),
                            0, 0, _nova.Width, _nova.Height, GraphicsUnit.Pixel, ia);
            }
        }

        protected override void WndProc(ref Message m)
        {
            // Transparente ao mouse: o clique cai na pagina por baixo.
            if (m.Msg == 0x0084) { m.Result = (IntPtr)(-1); return; }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Encerrar();
            base.Dispose(disposing);
        }
    }
}
