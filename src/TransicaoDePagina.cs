using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Troca de pagina com um fade curto (~150 ms): por cima da pagina nova, ja
    /// arranjada e no lugar, uma folha mostra o retrato da nova com o da antiga
    /// sumindo. A pagina de verdade esta pronta por baixo desde o primeiro
    /// quadro; um clique ou a roda durante o fade encerra a folha na hora.
    /// Com as animacoes do Windows desligadas, nao existe: troca direta.
    /// </summary>
    internal sealed class TransicaoDePagina : Control
    {
        public const int DuracaoMs = 150;
        private readonly Bitmap _antes, _depois;
        private readonly Transicao _t;
        private bool _saindo;
        private Timer _fim;

        /// <summary>Testes: a folha que esta na tela agora, se houver.</summary>
        internal static TransicaoDePagina Atual;

        private TransicaoDePagina(Bitmap antes, Bitmap depois)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            _antes = antes;
            _depois = depois;
            _t = new Transicao(this, DuracaoMs);
        }

        internal float Valor { get { return _t.Valor; } }

        /// <summary>
        /// O retrato de um controle: copiado da tela quando ele esta nela (um
        /// BitBlt, ~1 ms, e o que a pessoa esta vendo), senao pintado de novo
        /// (DrawToBitmap, que repinta tudo e custa dezenas de ms - so nos testes,
        /// com a janela fora da tela).
        /// </summary>
        private static Bitmap Retrato(Control c)
        {
            Rectangle tela = c.RectangleToScreen(c.ClientRectangle);
            bool naTela = false;
            foreach (Screen s in Screen.AllScreens) if (s.Bounds.IntersectsWith(tela)) { naTela = true; break; }
            // Na tela mas com outra janela por cima (um jogo, um overlay, o widget):
            // a copia mostraria a outra janela. Nesse caso, troca direta, sem fade.
            if (naTela && !Desimpedido(c, tela)) return null;
            Bitmap b = new Bitmap(c.Width, c.Height, PixelFormat.Format32bppPArgb);
            if (naTela)
                using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(tela.Location, Point.Empty, tela.Size);
            else
                c.DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height));
            return b;
        }

        /// <summary>O controle inteiro esta a vista: o centro e os quatro cantos caem em janelas do proprio formulario.</summary>
        private static bool Desimpedido(Control c, Rectangle tela)
        {
            Form f = c.FindForm();
            if (f == null || !f.IsHandleCreated) return false;
            bool inteiro = false;
            foreach (Screen s in Screen.AllScreens) if (s.Bounds.Contains(tela)) inteiro = true;
            if (!inteiro) return false;
            int m = 4;
            Point[] pts = { new Point(tela.X + tela.Width / 2, tela.Y + tela.Height / 2), new Point(tela.X + m, tela.Y + m),
                new Point(tela.Right - m, tela.Y + m), new Point(tela.X + m, tela.Bottom - m), new Point(tela.Right - m, tela.Bottom - m) };
            foreach (Point p in pts)
            {
                IntPtr h = WindowFromPoint(new PONTO(p.X, p.Y));
                if (h == IntPtr.Zero || GetAncestor(h, 2) != f.Handle) return false;
            }
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PONTO { public int X, Y; public PONTO(int x, int y) { X = x; Y = y; } }

        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PONTO p);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        /// <summary>
        /// Fotografa a pagina que sai (antes de esconder) e devolve o retrato, ou
        /// nulo quando nao ha o que animar.
        /// </summary>
        public static Bitmap Fotografar(Control saindo)
        {
            if (!Ui.Animacoes || saindo == null || !saindo.Visible || !saindo.IsHandleCreated || saindo.Width <= 0 || saindo.Height <= 0)
                return null;
            try
            {
                return Retrato(saindo);
            }
            catch (Exception ex) { Log.Error("transicao de pagina", ex); return null; }
        }

        /// <summary>Comeca o fade sobre a pagina que entrou (ja visivel e arranjada).</summary>
        public static void Comecar(Control entrando, Bitmap antes)
        {
            if (Atual != null) Atual.Encerrar();
            if (antes == null) return;
            if (entrando == null || entrando.Parent == null || entrando.Width <= 0 || entrando.Height <= 0) { antes.Dispose(); return; }
            Bitmap depois = null;
            try
            {
                // a pagina nova pinta de verdade agora (o que ia acontecer de qualquer jeito) e o retrato vem da tela
                entrando.Update();
                depois = Retrato(entrando);
                if (depois == null) { antes.Dispose(); return; }
            }
            catch (Exception ex) { Log.Error("transicao de pagina", ex); antes.Dispose(); if (depois != null) depois.Dispose(); return; }
            TransicaoDePagina f = new TransicaoDePagina(antes, depois);
            f.Bounds = entrando.Bounds;
            entrando.Parent.Controls.Add(f);
            f.BringToFront();
            Atual = f;
            f.Update();
            f._t.Ir(1f);
            if (!f._t.Animando) f.Encerrar();
            else
            {
                // o fim nao depende de pintura (janela coberta ou fora da tela nao repinta)
                f._fim = new Timer();
                f._fim.Interval = DuracaoMs + 60;
                f._fim.Tick += delegate { f.Encerrar(); };
                f._fim.Start();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float v = _t.Valor;
            // primeiro quadro: so o retrato da antiga, igual ao que ja estava na tela (barato, sem mistura)
            if (v <= 0.001f) { g.DrawImageUnscaled(_antes, 0, 0); return; }
            g.DrawImageUnscaled(_depois, 0, 0);
            if (v < 1f)
            {
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ColorMatrix cm = new ColorMatrix();
                    cm.Matrix33 = 1f - Motion.Padrao(v);
                    ia.SetColorMatrix(cm);
                    g.DrawImage(_antes, new Rectangle(0, 0, Width, Height), 0, 0, _antes.Width, _antes.Height, GraphicsUnit.Pixel, ia);
                }
            }
            else if (!_saindo)
            {
                _saindo = true;
                BeginInvoke((MethodInvoker)Encerrar);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e) { Encerrar(); }
        protected override void OnMouseWheel(MouseEventArgs e) { Encerrar(); }

        internal void Encerrar()
        {
            if (IsDisposed) return;
            if (Atual == this) Atual = null;
            if (Parent != null) Parent.Controls.Remove(this);
            Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _antes.Dispose(); _depois.Dispose(); if (_fim != null) _fim.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
