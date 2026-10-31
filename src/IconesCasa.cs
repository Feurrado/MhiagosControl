using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MhiagosControl
{
    /// <summary>
    /// Os icones desenhados da casa que pedem mais cuidado que um traco solto:
    /// a lua do modo noturno, a chave de boca do diagnostico e a lavadora.
    ///
    /// Os tres sao montados numa grade propria (unidades, nao pixels) e so no
    /// fim encaixados no retangulo pedido, centrados pelo DESENHO e nao pela
    /// caixa. Assim ficam no meio do botao ou da bolha em qualquer escala, e
    /// a proporcao nao muda de 100% para 150%.
    /// </summary>
    internal static partial class IconesCasa
    {
        /// <summary>Encaixa os caminhos em r: mesma escala para todos, centrados pelo contorno somado.</summary>
        private static void Encaixar(RectangleF r, float ocupar, params GraphicsPath[] caminhos)
        {
            RectangleF b = RectangleF.Empty;
            foreach (GraphicsPath p in caminhos)
            {
                RectangleF pb = p.GetBounds();
                b = b.IsEmpty ? pb : RectangleF.Union(b, pb);
            }
            if (b.Width <= 0 || b.Height <= 0) return;
            float k = Math.Min(r.Width / b.Width, r.Height / b.Height) * ocupar;
            using (Matrix m = new Matrix())
            {
                m.Translate(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                m.Scale(k, k);
                m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
                foreach (GraphicsPath p in caminhos) p.Transform(m);
            }
        }

        /// <summary>
        /// Arco de um circulo (centro c, raio rr) do ponto a ao ponto b, pelo
        /// lado que passa mais perto da direcao "rumo" (em graus).
        /// </summary>
        private static void Arco(GraphicsPath gp, PointF c, float rr, PointF a, PointF b, double rumo)
        {
            double ia = Math.Atan2(a.Y - c.Y, a.X - c.X) * 180 / Math.PI;
            double ib = Math.Atan2(b.Y - c.Y, b.X - c.X) * 180 / Math.PI;
            double horario = ((ib - ia) % 360 + 360) % 360;
            double meio = ia + horario / 2;
            double dif = Math.Abs(((meio - rumo) % 360 + 540) % 360 - 180);
            double varrer = dif <= 90 ? horario : horario - 360;
            gp.AddArc(c.X - rr, c.Y - rr, 2 * rr, 2 * rr, (float)ia, (float)varrer);
        }

        /// <summary>
        /// O cooler do menu (pagina Paineis): moldura quadrada arredondada com a
        /// ventoinha dentro - aro, quatro pas curvas e o cubo. Traco fino como o
        /// dos glifos de 11 pt do menu, para ficar no mesmo peso dos vizinhos.
        /// </summary>
        public static void Cooler(Graphics g, RectangleF r, Color c)
        {
            float s = Math.Min(r.Width, r.Height);
            float esp = Math.Max(1.1f, s / 14f);
            PointF m = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            SmoothingMode antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(c, esp))
            using (SolidBrush b = new SolidBrush(c))
            {
                p.LineJoin = LineJoin.Round;
                p.StartCap = p.EndCap = LineCap.Round;
                float lado = s * 0.92f - esp;
                RectangleF moldura = new RectangleF(m.X - lado / 2f, m.Y - lado / 2f, lado, lado);
                using (GraphicsPath gp = new GraphicsPath())
                {
                    float rr = lado * 0.22f;
                    gp.AddArc(moldura.X, moldura.Y, 2 * rr, 2 * rr, 180, 90);
                    gp.AddArc(moldura.Right - 2 * rr, moldura.Y, 2 * rr, 2 * rr, 270, 90);
                    gp.AddArc(moldura.Right - 2 * rr, moldura.Bottom - 2 * rr, 2 * rr, 2 * rr, 0, 90);
                    gp.AddArc(moldura.X, moldura.Bottom - 2 * rr, 2 * rr, 2 * rr, 90, 90);
                    gp.CloseFigure();
                    g.DrawPath(p, gp);
                }
                float aro = lado * 0.40f;
                g.DrawEllipse(p, m.X - aro, m.Y - aro, 2 * aro, 2 * aro);
                // quatro pas: cada uma sai do cubo e encosta no aro, curvada no sentido do giro
                float cubo = aro * 0.24f;
                for (int i = 0; i < 4; i++)
                {
                    double a = i * Math.PI / 2;
                    using (GraphicsPath pa = new GraphicsPath())
                    {
                        PointF ini = Polar(m, cubo, a - 0.5), fim = Polar(m, aro * 0.92f, a + 0.55);
                        pa.AddBezier(ini, Polar(m, aro * 0.55f, a - 0.35), Polar(m, aro * 0.95f, a + 0.05), fim);
                        pa.AddBezier(fim, Polar(m, aro * 0.55f, a + 0.6), Polar(m, cubo * 1.6f, a + 0.55), Polar(m, cubo, a + 0.6));
                        pa.CloseFigure();
                        g.FillPath(b, pa);
                    }
                }
                g.FillEllipse(b, m.X - cubo, m.Y - cubo, 2 * cubo, 2 * cubo);
            }
            g.SmoothingMode = antes;
        }

        private static PointF Polar(PointF c, float raio, double ang)
        {
            return new PointF(c.X + raio * (float)Math.Cos(ang), c.Y + raio * (float)Math.Sin(ang));
        }
    }
}
