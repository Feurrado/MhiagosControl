using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>Icones de linha desenhados: a Segoe nao tem floco, gota nem folha.</summary>
    internal static partial class IconesCasa
    {
        private static Pen Caneta(Color c, RectangleF r) { Pen p = new Pen(c, Math.Max(1.5f, r.Width / 12f)); p.StartCap = p.EndCap = LineCap.Round; return p; }

        public static void Floco(Graphics g, RectangleF r, Color c)
        {
            using (Pen p = Caneta(c, r))
            {
                PointF m = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
                float raio = Math.Min(r.Width, r.Height) / 2;
                for (int i = 0; i < 3; i++)
                {
                    double a = Math.PI / 2 + i * Math.PI / 3;
                    PointF a1 = new PointF(m.X + (float)Math.Cos(a) * raio, m.Y - (float)Math.Sin(a) * raio);
                    PointF a2 = new PointF(m.X - (float)Math.Cos(a) * raio, m.Y + (float)Math.Sin(a) * raio);
                    g.DrawLine(p, a1, a2);
                    foreach (PointF ponta in new[] { a1, a2 })
                    {
                        double b = Math.Atan2(m.Y - ponta.Y, ponta.X - m.X);
                        PointF base1 = new PointF(m.X + (ponta.X - m.X) * 0.62f, m.Y + (ponta.Y - m.Y) * 0.62f);
                        float t = raio * 0.3f;
                        g.DrawLine(p, base1, new PointF(base1.X + (float)Math.Cos(b + 0.8) * t, base1.Y - (float)Math.Sin(b + 0.8) * t));
                        g.DrawLine(p, base1, new PointF(base1.X + (float)Math.Cos(b - 0.8) * t, base1.Y - (float)Math.Sin(b - 0.8) * t));
                    }
                }
            }
        }

        public static void Gota(Graphics g, RectangleF r, Color c)
        {
            using (Pen p = Caneta(c, r))
            using (GraphicsPath gp = new GraphicsPath())
            {
                float w = r.Width * 0.7f, x = r.X + (r.Width - w) / 2;
                float d = w;
                gp.AddArc(x, r.Bottom - d, d, d, -30, 240);
                gp.AddLine(x + d * 0.07f, r.Bottom - d * 0.75f, x + d / 2, r.Y);
                gp.CloseFigure();
                g.DrawPath(p, gp);
            }
        }

        public static void Ventoinha(Graphics g, RectangleF r, Color c)
        {
            using (Pen p = Caneta(c, r))
            {
                PointF m = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
                float raio = Math.Min(r.Width, r.Height) / 2;
                for (int i = 0; i < 3; i++)
                {
                    GraphicsState st = g.Save();
                    g.TranslateTransform(m.X, m.Y);
                    g.RotateTransform(i * 120f);
                    g.DrawEllipse(p, -raio * 0.28f, -raio, raio * 0.56f, raio * 0.9f);
                    g.Restore(st);
                }
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, m.X - raio * 0.14f, m.Y - raio * 0.14f, raio * 0.28f, raio * 0.28f);
            }
        }

        public static void Sol(Graphics g, RectangleF r, Color c)
        {
            using (Pen p = Caneta(c, r))
            {
                PointF m = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
                float raio = Math.Min(r.Width, r.Height) / 2;
                g.DrawEllipse(p, m.X - raio * 0.45f, m.Y - raio * 0.45f, raio * 0.9f, raio * 0.9f);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(p, m.X + (float)Math.Cos(a) * raio * 0.7f, m.Y + (float)Math.Sin(a) * raio * 0.7f,
                        m.X + (float)Math.Cos(a) * raio, m.Y + (float)Math.Sin(a) * raio);
                }
            }
        }

        public static void Automatico(Graphics g, RectangleF r, Color c)
        {
            using (Pen p = Caneta(c, r))
            {
                g.DrawArc(p, r.X, r.Y, r.Width, r.Height, 200, 280);
                using (Font f = new Font(Ui.Tipo.Texto, Math.Max(6f, r.Height * 0.42f), FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, "A", f, Rectangle.Round(r), c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        public static void Folha(Graphics g, RectangleF r, Color c)
        {
            using (Pen p = Caneta(c, r))
            using (GraphicsPath gp = new GraphicsPath())
            {
                PointF baixo = new PointF(r.X + r.Width * 0.12f, r.Bottom - r.Height * 0.08f);
                PointF alto = new PointF(r.Right - r.Width * 0.08f, r.Y + r.Height * 0.08f);
                gp.AddBezier(baixo, new PointF(r.X, r.Y + r.Height * 0.3f), new PointF(r.X + r.Width * 0.5f, r.Y), alto);
                gp.AddBezier(alto, new PointF(r.Right, r.Y + r.Height * 0.55f), new PointF(r.X + r.Width * 0.6f, r.Bottom), baixo);
                gp.CloseFigure();
                g.DrawPath(p, gp);
                g.DrawLine(p, baixo, new PointF(r.X + r.Width * 0.62f, r.Y + r.Height * 0.38f));
            }
        }

        /// <summary>n barras de altura crescente, as primeiras "cheias".</summary>
        public static void Barras(Graphics g, RectangleF r, Color c, int cheias, int total)
        {
            if (total <= 0) return;
            float gap = r.Width * 0.12f;
            float w = (r.Width - gap * (total - 1)) / total;
            for (int i = 0; i < total; i++)
            {
                float h = r.Height * (0.35f + 0.65f * (i + 1) / total);
                RectangleF b = new RectangleF(r.X + i * (w + gap), r.Bottom - h, w, h);
                using (SolidBrush br = new SolidBrush(i < cheias ? c : Color.FromArgb(70, c)))
                    g.FillRectangle(br, b);
            }
        }
    }

    /// <summary>
    /// Mostrador de termostato: arco de 270 graus da minima a maxima, com o
    /// alvo grande no meio e a temperatura do ambiente embaixo. Arrastar no
    /// arco (ou setas do teclado) escolhe a temperatura; solta e envia.
    /// </summary>
    internal sealed class Mostrador : Control
    {
        public int Min = 16, Max = 30, Valor = 23;
        public double? Ambiente;
        public string Unidade = "°C", Legenda = "";
        public bool Ligado = true;
        private LigadoAnimado _lig;
        private NumeroDeslizante _num;
        private LigadoAnimado Lig { get { return _lig ?? (_lig = new LigadoAnimado(this)); } }
        private NumeroDeslizante Num { get { return _num ?? (_num = new NumeroDeslizante(this)); } }
        public Color Cor = Ui.Accent;
        public event EventHandler Escolheu;
        public event EventHandler Arrastando;
        private bool _arrastando;

        private const float Inicio = 135f, Varredura = 270f;
        private static readonly Font FonteAlvo = new Font(Ui.Tipo.Exibicao, Ui.Pt(40f));
        private static readonly Font FonteUnidade = new Font(Ui.Tipo.Exibicao, Ui.Pt(16f));

        public Mostrador()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        private int Espessura { get { return Ui.S(14); } }

        internal RectangleF Anel()
        {
            // Folga para o puxador inteiro (raio ~ espessura) e o anel de foco em volta dele.
            float lado = Math.Min(Width, Height) - 2 * (Espessura + Ui.S(6));
            return new RectangleF((Width - lado) / 2f, (Height - lado) / 2f, lado, lado);
        }

        private float Fracao(int v) { return Max > Min ? (float)(v - Min) / (Max - Min) : 0f; }

        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int passo = e.KeyCode == Keys.Up || e.KeyCode == Keys.Right ? 1 : e.KeyCode == Keys.Down || e.KeyCode == Keys.Left ? -1 : 0;
            if (passo != 0 && Enabled)
            {
                Valor = Math.Max(Min, Math.Min(Max, Valor + passo));
                Invalidate();
                if (Escolheu != null) Escolheu(this, EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left && NoAnel(e.Location)) { _arrastando = true; Mover(e.Location); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_arrastando) Mover(e.Location);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_arrastando)
            {
                _arrastando = false;
                if (Escolheu != null) Escolheu(this, EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }

        private bool NoAnel(Point p)
        {
            RectangleF a = Anel();
            float dx = p.X - (a.X + a.Width / 2), dy = p.Y - (a.Y + a.Height / 2);
            double d = Math.Sqrt(dx * dx + dy * dy);
            return Math.Abs(d - a.Width / 2) <= Espessura * 1.6;
        }

        private void Mover(Point p)
        {
            RectangleF a = Anel();
            double ang = Math.Atan2(p.Y - (a.Y + a.Height / 2), p.X - (a.X + a.Width / 2)) * 180 / Math.PI;
            double rel = ang - Inicio;
            while (rel < 0) rel += 360;
            // Na lacuna de baixo: vai para a ponta mais proxima.
            if (rel > Varredura) rel = rel - Varredura < (360 - Varredura) / 2 ? Varredura : 0;
            int v = Min + (int)Math.Round(rel / Varredura * (Max - Min));
            if (v == Valor) return;
            Valor = v;
            Invalidate();
            if (Arrastando != null) Arrastando(this, EventArgs.Empty);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            RectangleF a = Anel();
            // ligar e desligar fazem fade (~250 ms) do cinza para a cor do modo
            Lig.Definir(Ligado);
            float lig = Enabled ? Lig.Valor : 0f;
            Color cor = Ui.Misturar(Ui.Faint, Cor, lig);

            using (Pen trilho = new Pen(Ui.SurfaceAlt, Espessura))
            {
                trilho.StartCap = trilho.EndCap = LineCap.Round;
                g.DrawArc(trilho, a, Inicio, Varredura);
            }
            float f = Fracao(Valor);
            if (f > 0.001f)
                using (Pen arco = new Pen(cor, Espessura))
                {
                    arco.StartCap = arco.EndCap = LineCap.Round;
                    g.DrawArc(arco, a, Inicio, Varredura * f);
                }

            // Botao do arco.
            double ang = (Inicio + Varredura * f) * Math.PI / 180;
            float cx = a.X + a.Width / 2 + (float)Math.Cos(ang) * a.Width / 2;
            float cy = a.Y + a.Height / 2 + (float)Math.Sin(ang) * a.Height / 2;
            float k = Espessura * 0.95f;
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, cx - k, cy - k, 2 * k, 2 * k);
            using (Pen p = new Pen(cor, Ui.S(3))) g.DrawEllipse(p, cx - k, cy - k, 2 * k, 2 * k);
            if (Focused && ShowFocusCues)
                using (Pen p = new Pen(Ui.Accent, 2f)) g.DrawEllipse(p, cx - k - 3, cy - k - 3, 2 * k + 6, 2 * k + 6);

            // Pontas: minima e maxima.
            Rectangle meio = Rectangle.Round(a);
            TextRenderer.DrawText(g, Min + "°", Ui.FontSmall, new Rectangle(meio.X, meio.Bottom - Ui.S(16), meio.Width / 2 - Ui.S(24), Ui.S(18)), Ui.Muted,
                TextFormatFlags.Right);
            TextRenderer.DrawText(g, Max + "°", Ui.FontSmall, new Rectangle(meio.X + meio.Width / 2 + Ui.S(24), meio.Bottom - Ui.S(16), meio.Width / 2 - Ui.S(24), Ui.S(18)), Ui.Muted,
                TextFormatFlags.Left);

            // Centro: legenda, alvo e ambiente.
            int cyMeio = meio.Y + meio.Height / 2;
            TextRenderer.DrawText(g, Legenda, Ui.FontSmall, new Rectangle(meio.X, cyMeio - Ui.S(58), meio.Width, Ui.S(18)),
                Ui.Misturar(Ui.Faint, Ui.Muted, lig), TextFormatFlags.HorizontalCenter);
            string alvo = Valor.ToString(CultureInfo.InvariantCulture);
            Size sa = TextRenderer.MeasureText(g, alvo, FonteAlvo, Size.Empty, TextFormatFlags.NoPadding);
            Size su = TextRenderer.MeasureText(g, Unidade, FonteUnidade, Size.Empty, TextFormatFlags.NoPadding);
            int x0 = meio.X + (meio.Width - sa.Width - su.Width) / 2;
            int ya = cyMeio - sa.Height / 2 - Ui.S(4);
            // o alvo desliza ao mudar: sobe saindo por cima, desce saindo por baixo
            Num.Desenhar(g, alvo, FonteAlvo, new Point(x0, ya), new Rectangle(meio.X, ya - Ui.S(4), meio.Width, sa.Height + Ui.S(8)),
                Ui.Misturar(Ui.Muted, Ui.Text, lig), Ui.Surface, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Unidade, FonteUnidade, new Point(x0 + sa.Width, ya + Ui.S(6)), Ui.Muted, TextFormatFlags.NoPadding);
            if (Ambiente.HasValue)
                TextRenderer.DrawText(g, string.Format(T.CasaAmbienteCurto, Ambiente.Value.ToString("0.#") + Unidade), Ui.FontBase,
                    new Rectangle(meio.X, ya + sa.Height + Ui.S(4), meio.Width, Ui.S(22)), Ui.Muted, TextFormatFlags.HorizontalCenter);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acessivel(this); }

        private sealed class Acessivel : ControlAccessibleObject
        {
            private readonly Mostrador _d;
            public Acessivel(Mostrador d) : base(d) { _d = d; }
            public override AccessibleRole Role { get { return AccessibleRole.Dial; } }
            public override string Name { get { return T.CasaAlvo; } }
            public override string Value { get { return _d.Valor + _d.Unidade; } set { } }
        }
    }

    /// <summary>Uma fila de blocos com icone e rotulo; o escolhido fica aceso.</summary>
    internal sealed class Blocos : Control
    {
        public sealed class Item
        {
            public int Valor;
            public string Rotulo;
            public Action<Graphics, RectangleF, Color> Icone;
        }

        public readonly List<Item> Itens = new List<Item>();
        public int Selecionado = int.MinValue;
        public Color Cor = Ui.Accent;
        /// <summary>Barras crescentes no lugar do icone (velocidade do ventilador).</summary>
        public bool ComBarras;
        public event EventHandler Escolheu;
        private int _hover = -1, _foco;

        public Blocos()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        private Rectangle Caixa(int i)
        {
            int n = Math.Max(1, Itens.Count), gap = Ui.S(8);
            int w = (Width - gap * (n - 1)) / n;
            return new Rectangle(i * (w + gap), 0, w - 1, Height - 1);
        }

        private int Hit(Point p) { for (int i = 0; i < Itens.Count; i++) if (Caixa(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != _hover) { _hover = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { int h = Hit(e.Location); if (h >= 0 && Enabled) Escolher(h); base.OnMouseDown(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            {
                _foco = Math.Max(0, Math.Min(Itens.Count - 1, _foco + (e.KeyCode == Keys.Left ? -1 : 1)));
                Invalidate(); e.Handled = true;
            }
            else if ((e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && _foco < Itens.Count) { Escolher(_foco); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

        private void Escolher(int i)
        {
            _foco = i;
            if (Itens[i].Valor == Selecionado) return;
            Selecionado = Itens[i].Valor;
            Invalidate();
            if (Escolheu != null) Escolheu(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            bool alto = Height >= Ui.S(64);
            for (int i = 0; i < Itens.Count; i++)
            {
                Item it = Itens[i];
                Rectangle r = Caixa(i);
                bool sel = it.Valor == Selecionado;
                Color fundo = !Enabled ? Ui.SurfaceAlt : sel ? Cor : (i == _hover ? Ui.Hover : Ui.SurfaceAlt);
                Color frente = !Enabled ? Ui.Faint : sel ? Color.White : Ui.Text;
                using (GraphicsPath p = Ui.RoundRect(r, Ui.S(10)))
                {
                    using (SolidBrush b = new SolidBrush(fundo)) g.FillPath(b, p);
                    if (!sel) using (Pen pen = new Pen(Ui.Border)) g.DrawPath(pen, p);
                }
                if (Focused && ShowFocusCues && i == _foco)
                    using (GraphicsPath p = Ui.RoundRect(Rectangle.Inflate(r, -2, -2), Ui.S(8)))
                    using (Pen pen = new Pen(sel ? Color.White : Ui.Accent, 2f)) g.DrawPath(pen, p);

                int ic = Ui.S(alto ? 24 : 18);
                if (alto)
                {
                    RectangleF ri = new RectangleF(r.X + (r.Width - ic) / 2f, r.Y + Ui.S(12), ic, ic);
                    DesenharIcone(g, it, ri, frente, i);
                    TextRenderer.DrawText(g, it.Rotulo, sel ? Ui.FontMed : Ui.FontBase,
                        new Rectangle(r.X + Ui.S(4), r.Bottom - Ui.S(28), r.Width - Ui.S(8), Ui.S(22)), frente,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                }
                else
                {
                    Size t = TextRenderer.MeasureText(it.Rotulo, sel ? Ui.FontMed : Ui.FontBase);
                    bool temIcone = ComBarras || it.Icone != null;
                    int total = t.Width + (temIcone ? ic + Ui.S(8) : 0);
                    int x = r.X + Math.Max(Ui.S(6), (r.Width - total) / 2);
                    if (temIcone)
                    {
                        DesenharIcone(g, it, new RectangleF(x, r.Y + (r.Height - ic) / 2f, ic, ic), frente, i);
                        x += ic + Ui.S(8);
                    }
                    TextRenderer.DrawText(g, it.Rotulo, sel ? Ui.FontMed : Ui.FontBase,
                        new Rectangle(x, r.Y, r.Right - x - Ui.S(4), r.Height), frente,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private void DesenharIcone(Graphics g, Item it, RectangleF r, Color c, int i)
        {
            if (ComBarras) { IconesCasa.Barras(g, r, c, i + 1, Itens.Count); return; }
            if (it.Icone != null) { it.Icone(g, r, c); return; }
            // Sem significado conhecido: o numero do valor, num circulo.
            using (Pen p = new Pen(c, Math.Max(1.5f, r.Width / 12f))) g.DrawEllipse(p, r);
            TextRenderer.DrawText(g, it.Valor.ToString(), Ui.FontSmall, Rectangle.Round(r), c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acessivel(this); }

        private sealed class Acessivel : ControlAccessibleObject
        {
            private readonly Blocos _d;
            public Acessivel(Blocos d) : base(d) { _d = d; }
            public override AccessibleRole Role { get { return AccessibleRole.List; } }
            public override string Value
            {
                get { foreach (Item i in _d.Itens) if (i.Valor == _d.Selecionado) return i.Rotulo; return ""; }
                set { }
            }
        }
    }

    /// <summary>
    /// Como cada modo do ar aparece: icone e cor. Fica separado da TabelaDoAr
    /// (nomes, sem desenho) para a tabela servir a qualquer interface.
    /// </summary>
    internal static class VisualDoAr
    {
        /// <summary>Desenho do modo; null quando o significado nao e conhecido.</summary>
        public static Action<Graphics, RectangleF, Color> IconeDoModo(int v, string tipo)
        {
            switch (v)
            {
                case 0: return IconesCasa.Ventoinha;
                case 1: return IconesCasa.Sol;
                case 2: return IconesCasa.Floco;
                case 3: return IconesCasa.Gota;
                case 4: return IconesCasa.Automatico;
                case 5: return IconesCasa.Folha;
                default: return null;
            }
        }

        /// <summary>Cor do mostrador conforme o modo.</summary>
        public static Color CorDoModo(int v, string tipo)
        {
            switch (v)
            {
                case 0: return Color.FromArgb(0x8A, 0x93, 0xA6);
                case 1: return Color.FromArgb(0xF0, 0x8C, 0x3C);
                case 2: return Ui.Accent;
                case 3: return Color.FromArgb(0x00, 0xB0, 0xA9);
                case 4: return Color.FromArgb(0x8B, 0x5C, 0xF6);
                default: return Color.FromArgb(0x2E, 0xA0, 0x43);
            }
        }
    }
}
