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
        // ---------------- posicao ----------------

        /// <summary>Volta onde estava; na primeira vez, ou se a tela sumiu, no canto de cima a direita.</summary>
        private void Posicionar()
        {
            Size tam = Medida();
            Point p;
            if (_cfg.TemPosicao)
            {
                Rectangle area = Screen.FromPoint(new Point(_cfg.X - 1, _cfg.Y - 1)).WorkingArea;
                p = DoCanto(new Point(_cfg.X, _cfg.Y), tam, area);
                if (!Visivel(new Rectangle(p, tam))) p = PosicaoPadrao(tam);
            }
            else p = PosicaoPadrao(tam);
            Location = p;
            ManterNaTela();
            Renderizar();
        }

        /// <summary>Canto inferior direito da area de trabalho, um pouco longe da borda.</summary>
        private Point PosicaoPadrao(Size tam)
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int m = (int)(48 * _escala);
            return DoCanto(new Point(area.Right - m, area.Bottom - m), tam, area);
        }

        /// <summary>
        /// O canto que fica parado quando o widget muda de tamanho: o mais
        /// proximo da borda da tela (direito se o centro esta na metade direita,
        /// de baixo se esta na metade de baixo). E ele que se guarda: assim o
        /// widget reabre no mesmo lugar mesmo com mais ou menos aparelhos.
        /// </summary>
        internal static Point CantoAncora(Rectangle b, Rectangle area)
        {
            bool dir = b.Left + b.Width / 2 > area.Left + area.Width / 2;
            bool baixo = b.Top + b.Height / 2 > area.Top + area.Height / 2;
            return new Point(dir ? b.Right : b.Left, baixo ? b.Bottom : b.Top);
        }

        /// <summary>Onde fica o canto de cima a esquerda, dado o canto guardado e o tamanho de agora.</summary>
        internal static Point DoCanto(Point canto, Size tam, Rectangle area)
        {
            bool dir = canto.X > area.Left + area.Width / 2;
            bool baixo = canto.Y > area.Top + area.Height / 2;
            return new Point(dir ? canto.X - tam.Width : canto.X, baixo ? canto.Y - tam.Height : canto.Y);
        }

        /// <summary>Visivel o bastante para alguem achar e arrastar de volta.</summary>
        internal static bool Visivel(Rectangle r)
        {
            foreach (Screen tela in Screen.AllScreens)
            {
                Rectangle i = Rectangle.Intersect(tela.WorkingArea, r);
                if (i.Width >= Math.Min(48, r.Width) && i.Height >= Math.Min(32, r.Height)) return true;
            }
            return false;
        }

        /// <summary>
        /// Encaixa a placa dentro da area util da tela em que esta. Com mais um
        /// aparelho ela cresce para a direita, e no canto direito sairia da tela.
        /// </summary>
        /// <summary>
        /// Onde fica o canto de cima a esquerda quando o widget muda de tamanho:
        /// na metade direita da tela a borda direita fica parada (cresce para a
        /// esquerda), na de baixo a borda de baixo; senao, a esquerda e o topo.
        /// </summary>
        internal static Point AncorarCrescimento(Rectangle antes, Size novo, Rectangle area)
        {
            int x = antes.Left, y = antes.Top;
            if (antes.Left + antes.Width / 2 > area.Left + area.Width / 2) x = antes.Right - novo.Width;
            if (antes.Top + antes.Height / 2 > area.Top + area.Height / 2) y = antes.Bottom - novo.Height;
            return new Point(x, y);
        }

        private void ManterNaTela()
        {
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            int x = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            int y = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            if (x != Left || y != Top) Location = new Point(x, y);
        }

        /// <summary>Gruda na borda da tela quando chega a poucos pixels dela, como os gadgets faziam.</summary>
        internal static Point Encaixar(Point p, Size tam, Rectangle area, int ima)
        {
            int x = p.X, y = p.Y;
            if (Math.Abs(x - area.Left) <= ima) x = area.Left;
            else if (Math.Abs(area.Right - (x + tam.Width)) <= ima) x = area.Right - tam.Width;
            if (Math.Abs(y - area.Top) <= ima) y = area.Top;
            else if (Math.Abs(area.Bottom - (y + tam.Height)) <= ima) y = area.Bottom - tam.Height;
            return new Point(x, y);
        }

        private void GuardarPosicao()
        {
            Point c = CantoAncora(Bounds, Screen.FromRectangle(Bounds).WorkingArea);
            _cfg.X = c.X;
            _cfg.Y = c.Y;
            _cfg.Salvar();
        }

        // ---------------- mouse ----------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _pressionado = true;
            _arrastando = false;
            _inicioDoArrasto = Cursor.Position;
            _origemDoArrasto = Location;
            int zc = _casa.Zona(e.Location);
            _noBotaoDaCasa = zc >= 0 && zc < 3;
            Capture = true;
        }
    }
}
