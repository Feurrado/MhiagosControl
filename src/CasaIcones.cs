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
        /// Lua crescente limpa, cheia: o circulo menos outro circulo deslocado
        /// para o alto a direita. Cheia, e nao em contorno: a 16 px dois arcos
        /// paralelos viravam um borrao; a forma chapada se le de longe.
        /// </summary>
        public static void Lua(Graphics g, RectangleF r, Color c)
        {
            PointF o = new PointF(0, 0), i = new PointF(0.46f, -0.34f);
            float ro = 1f, ri = 0.80f;
            // Intersecao dos dois circulos.
            double dx = i.X - o.X, dy = i.Y - o.Y, d = Math.Sqrt(dx * dx + dy * dy);
            double a = (ro * ro - ri * ri + d * d) / (2 * d), h = Math.Sqrt(Math.Max(0, ro * ro - a * a));
            double px = o.X + a * dx / d, py = o.Y + a * dy / d;
            PointF p1 = new PointF((float)(px + h * dy / d), (float)(py - h * dx / d));
            PointF p2 = new PointF((float)(px - h * dy / d), (float)(py + h * dx / d));
            double rumo = Math.Atan2(dy, dx) * 180 / Math.PI;

            using (GraphicsPath gp = new GraphicsPath())
            {
                // por fora: o lado do circulo grande longe do recorte; por dentro: o recorte de volta
                Arco(gp, o, ro, p1, p2, rumo + 180);
                Arco(gp, i, ri, p2, p1, rumo + 180);
                gp.CloseFigure();
                Encaixar(r, 0.96f, gp);
                // A massa do crescente fica a esquerda e embaixo: meio passo para la e o centro optico.
                using (Matrix m = new Matrix()) { m.Translate(r.Width * 0.04f, -r.Height * 0.02f); gp.Transform(m); }
                using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, gp);
            }
        }

        /// <summary>
        /// Chave de boca cheia: cabeca redonda com a boca aberta no alto a
        /// direita e o cabo com ponta arredondada descendo para a esquerda, a
        /// 45 graus. Montada deitada (cabeca a direita) e girada no fim.
        /// </summary>
        public static void ChaveDeBoca(Graphics g, RectangleF r, Color c)
        {
            const float R = 1f, boca = 0.40f, fundo = 0.05f, cabo = 0.34f, fim = -3.0f;
            using (GraphicsPath cab = new GraphicsPath())
            using (GraphicsPath haste = new GraphicsPath())
            {
                // Cabeca: o circulo inteiro menos a fenda da boca (fundo arredondado).
                float xc = (float)Math.Sqrt(R * R - boca * boca);
                double ang = Math.Atan2(boca, xc) * 180 / Math.PI;
                cab.AddArc(-R, -R, 2 * R, 2 * R, (float)ang, (float)(360 - 2 * ang));
                cab.AddLine(xc, -boca, fundo, -boca);
                cab.AddArc(fundo - boca, -boca, 2 * boca, 2 * boca, 270, -180);
                cab.AddLine(fundo, boca, xc, boca);
                cab.CloseFigure();

                // Cabo: de dentro da cabeca ate a ponta, com a ponta em meia-lua.
                haste.AddLine(-0.6f, -cabo, fim, -cabo);
                haste.AddArc(fim - cabo, -cabo, 2 * cabo, 2 * cabo, 270, -180);
                haste.AddLine(fim, cabo, -0.6f, cabo);
                haste.CloseFigure();

                using (Matrix m = new Matrix()) { m.Rotate(-45f); cab.Transform(m); haste.Transform(m); }
                Encaixar(r, 1f, cab, haste);
                using (SolidBrush b = new SolidBrush(c))
                {
                    g.FillPath(b, haste);
                    g.FillPath(b, cab);
                }
            }
        }

        /// <summary>
        /// A lavadora de frente: gabinete arredondado, faixa do painel com o
        /// botao e a gaveta, e a porta redonda no meio do que sobra. Um so
        /// desenho para a pagina Casa, a Visao geral e o widget, centrado em r.
        /// </summary>
        public static void Lavadora(Graphics g, RectangleF r, Color c)
        {
            float s = Math.Min(r.Width, r.Height);
            float w = s * 0.80f, h = s * 0.96f;
            float esp = Math.Max(1.4f, s / 11f);
            RectangleF corpo = new RectangleF(r.X + (r.Width - w) / 2f, r.Y + (r.Height - h) / 2f, w, h);
            // o traco cresce para os dois lados: recua meia espessura para nao vazar da caixa
            corpo.Inflate(-esp / 2f, -esp / 2f);
            float painel = corpo.Y + corpo.Height * 0.24f;
            using (Pen p = new Pen(c, esp))
            using (SolidBrush b = new SolidBrush(c))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                using (GraphicsPath gp = Ui.RoundRect(Rectangle.Round(corpo), Math.Max(2, (int)Math.Round(s * 0.16f))))
                    g.DrawPath(p, gp);
                g.DrawLine(p, corpo.X, painel, corpo.Right, painel);
                // gaveta a esquerda e o botao a direita, no meio da faixa do painel
                float yp = (corpo.Y + painel) / 2f;
                g.DrawLine(p, corpo.X + corpo.Width * 0.20f, yp, corpo.X + corpo.Width * 0.40f, yp);
                float k = esp * 1.25f;
                g.FillEllipse(b, corpo.Right - corpo.Width * 0.22f - k / 2f, yp - k / 2f, k, k);
                // porta: centrada no espaco abaixo do painel
                float dp = Math.Min(corpo.Width * 0.62f, (corpo.Bottom - painel) * 0.74f);
                float cy = (painel + corpo.Bottom) / 2f;
                g.DrawEllipse(p, corpo.X + (corpo.Width - dp) / 2f, cy - dp / 2f, dp, dp);
            }
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

        /// <summary>
        /// O - ou o + de um botao redondo, desenhado como tracos e nao como glifo:
        /// as metricas da Segoe deixavam o sinal um ou dois pixels fora do meio.
        /// Centro exato do circulo em float, pontas redondas, ~45% do diametro.
        /// </summary>
        public static void Sinal(Graphics g, RectangleF circulo, bool mais, Color c, float espessura)
        {
            float cx = circulo.X + circulo.Width / 2f, cy = circulo.Y + circulo.Height / 2f;
            float meio = Math.Min(circulo.Width, circulo.Height) * 0.45f / 2f;
            SmoothingMode antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(c, espessura))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawLine(p, cx - meio, cy, cx + meio, cy);
                if (mais) g.DrawLine(p, cx, cy - meio, cx, cy + meio);
            }
            g.SmoothingMode = antes;
        }

        /// <summary>O glifo e o menos ou o mais da Segoe (E738 ou E710)? Entao vai como Sinal.</summary>
        public static bool EhSinal(string glifo, out bool mais)
        {
            mais = false;
            if (string.IsNullOrEmpty(glifo) || glifo.Length != 1) return false;
            if (glifo[0] == (char)0xE710) { mais = true; return true; }
            return glifo[0] == (char)0xE738;
        }

        /// <summary>Glifo da Segoe centrado pelo desenho (a caixa da fonte tem folga de linha e descia o icone).</summary>
        public static void GlifoCentrado(Graphics g, string glifo, Font f, RectangleF area, Color c)
        {
            using (GraphicsPath gp = BotaoRedondo.Glifo(glifo, f, area))
            using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, gp);
        }
    }
}
