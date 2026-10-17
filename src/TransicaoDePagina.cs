using System;
using System.Drawing;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// A entrada de pagina: uma cortina da cor da janela que se recolhe de cima
    /// para baixo e deixa a pagina de verdade aparecer. Sem fotografia da
    /// pagina, entao nada pula no fim - o que se ve e a propria pagina. A
    /// cortina e um retangulo liso (barato de pintar) e transparente ao mouse.
    /// Sem efeitos no Windows (reduzir movimento) ou janela ainda nao exibida,
    /// a pagina troca na hora.
    /// </summary>
    internal sealed class VeuDePagina : Control
    {
        private long _inicio;
        private float _t;
        private Rectangle _area;

        public VeuDePagina()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            Visible = false;
            TabStop = false;
        }

        public bool Ativo { get { return Visible; } }

        public void Iniciar()
        {
            Encerrar();
            if (Parent == null) return;
            _area = new Rectangle(Point.Empty, Parent.ClientSize);
            if (_area.Height <= 0) return;
            _t = 0f;
            _inicio = Motion.Agora;
            Bounds = _area;
            Visible = true;
            BringToFront();
            Update();
            Motion.Assinar(Passo);
        }

        /// <summary>Um quadro; separado para os testes. true enquanto a cortina corre.</summary>
        internal bool Passo(long agora)
        {
            if (IsDisposed || !Visible) return false;
            _t = Motion.Progresso(_inicio, agora, Motion.Media);
            if (_t >= 1f) { Encerrar(); return false; }
            int revelado = (int)Math.Round(_area.Height * Motion.Padrao(_t));
            Rectangle r = new Rectangle(_area.X, _area.Y + revelado, _area.Width, _area.Height - revelado);
            if (r.Height <= 0) { Encerrar(); return false; }
            Bounds = r;
            return true;
        }

        public void Encerrar()
        {
            Visible = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Ui.Window);
        }

        protected override void WndProc(ref Message m)
        {
            // Transparente ao mouse: o clique cai na pagina por baixo.
            if (m.Msg == 0x0084) { m.Result = (IntPtr)(-1); return; }
            base.WndProc(ref m);
        }
    }
}
