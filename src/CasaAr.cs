using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// O que o modelo declara de cada propriedade (get_property_list): se e
    /// gravavel e quais valores aceita. E a fonte da verdade do painel - o ar
    /// so mostra os modos e velocidades que o proprio aparelho diz ter.
    /// </summary>
    internal sealed class MetaPropriedade
    {
        public string Chave, Nome;
        public bool Grava;
        public readonly List<int> Valores = new List<int>();
        /// <summary>Faixas "16~30,61~86": a primeira em °C, a segunda em °F.</summary>
        public readonly List<int[]> Faixas = new List<int[]>();

        public static MetaPropriedade De(Dictionary<string, object> d)
        {
            MetaPropriedade m = new MetaPropriedade();
            m.Chave = Json.Texto(d, "propertyKey") ?? "";
            m.Nome = Json.Texto(d, "propertyName") ?? "";
            m.Grava = (Json.Texto(d, "propertyRW") ?? "").IndexOf('W') >= 0;
            string lista = Json.Texto(d, "propertyValueList") ?? "";
            foreach (string parte in lista.Replace('，', ',').Replace('\n', ',').Split(','))
            {
                string p = parte.Trim();
                if (p.Length == 0) continue;
                int til = p.IndexOf('~');
                if (til > 0)
                {
                    int a, b;
                    if (int.TryParse(p.Substring(0, til).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out a) &&
                        int.TryParse(p.Substring(til + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out b))
                        m.Faixas.Add(new[] { a, b });
                    continue;
                }
                // "0:reserved" ou "0" - so o numero interessa aqui.
                int dois = p.IndexOf(':');
                int v;
                if (int.TryParse((dois > 0 ? p.Substring(0, dois) : p).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                    && !m.Valores.Contains(v))
                    m.Valores.Add(v);
            }
            return m;
        }

        public static Dictionary<string, MetaPropriedade> DaResposta(Dictionary<string, object> r)
        {
            Dictionary<string, MetaPropriedade> d = new Dictionary<string, MetaPropriedade>();
            List<object> l = r != null && r.ContainsKey("properties") ? r["properties"] as List<object> : null;
            if (l == null) return d;
            foreach (object o in l)
            {
                Dictionary<string, object> p = o as Dictionary<string, object>;
                if (p == null) continue;
                MetaPropriedade m = De(p);
                if (m.Chave.Length > 0) d[m.Chave] = m;
            }
            return d;
        }
    }

    /// <summary>
    /// Nomes dos valores de modo e de ventilador. ISOLADO AQUI de proposito:
    /// a nuvem diz QUAIS valores o modelo aceita, mas nao o que cada um
    /// significa. Os de modo 0 a 4 sao os do dicionario do tipo 009 (split) da
    /// integracao do Home Assistant; o que nao estiver confirmado aparece com o
    /// numero, sem inventar significado.
    ///
    /// Ar de janela do Thiago (tipo 008, feature 301): modos 0,2,3,5 e
    /// ventilador 2,3,4. Calibrado com ele em 26/10/2026: 0 Ventilar, 2 Frio,
    /// 3 Desumidificar e 5 ECO - os mesmos do 009 mais o ECO -; ventilador 2
    /// baixa, 3 media, 4 alta.
    /// </summary>
    internal static class TabelaDoAr
    {
        public static string Modo(int v, string tipo)
        {
            switch (v)
            {
                case 0: return T.CasaVentilar;
                case 1: return T.CasaCalor;
                case 2: return T.CasaFrio;
                case 3: return T.CasaSeco;
                case 4: return T.CasaAuto;
                case 5: return T.CasaEco;
                default: return string.Format(T.CasaModoN, v);
            }
        }

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

        /// <summary>Velocidades do tipo 009; as demais vao pelo numero.</summary>
        public static string Ventilador(int v, string tipo)
        {
            if (tipo == "009")
                switch (v)
                {
                    case 0: return "Auto";
                    case 5: return T.CasaVentoMinimo;
                    case 6: return T.CasaVentoBaixo;
                    case 7: return T.CasaVentoMedio;
                    case 8: return T.CasaVentoAlto;
                    case 9: return T.CasaVentoMaximo;
                }
            if (tipo == "008")
                switch (v)
                {
                    case 2: return T.CasaVentoBaixo;
                    case 3: return T.CasaVentoMedio;
                    case 4: return T.CasaVentoAlto;
                }
            if (v == 0) return "Auto";
            return string.Format(T.CasaVelocidadeN, v);
        }
    }

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
        public static void Lua(Graphics g, RectangleF r, Color c)
        {
            using (Pen p = Caneta(c, r))
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddArc(r.X, r.Y, r.Width, r.Height, 60, 240);
                gp.AddArc(r.X + r.Width * 0.3f, r.Y - r.Height * 0.05f, r.Width * 0.75f, r.Height * 0.75f, 300, -240);
                gp.CloseFigure();
                g.DrawPath(p, gp);
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
            Color cor = Ligado && Enabled ? Cor : Ui.Faint;

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
                Ligado ? Ui.Muted : Ui.Faint, TextFormatFlags.HorizontalCenter);
            string alvo = Valor.ToString(CultureInfo.InvariantCulture);
            Size sa = TextRenderer.MeasureText(g, alvo, FonteAlvo, Size.Empty, TextFormatFlags.NoPadding);
            Size su = TextRenderer.MeasureText(g, Unidade, FonteUnidade, Size.Empty, TextFormatFlags.NoPadding);
            int x0 = meio.X + (meio.Width - sa.Width - su.Width) / 2;
            int ya = cyMeio - sa.Height / 2 - Ui.S(4);
            TextRenderer.DrawText(g, alvo, FonteAlvo, new Point(x0, ya), Ligado ? Ui.Text : Ui.Muted, TextFormatFlags.NoPadding);
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
    /// Painel completo do ar-condicionado (tipos 008 de janela e 009 split).
    /// Montado pelo que o modelo declara (get_property_list); sem a lista,
    /// pelo que o estado traz. So o que o aparelho tem aparece.
    /// </summary>
    internal sealed class CartaoDoAr : CartaoDeAparelho
    {
        /// <summary>Interruptores conhecidos, na ordem de exibicao. t_sleep entra aqui so quando e 0/1.</summary>
        private static readonly string[] Interruptores =
        {
            "t_sleep", "t_eco", "t_super", "t_fan_mute", "t_up_down", "t_left_right",
            "t_8heat", "t_purify", "t_fresh_air", "t_tms", "t_dimmer"
        };

        /// <summary>Gravaveis que nao sao controle de uso: comandos internos do modulo.</summary>
        /// t_beep tambem: nao e estado do aparelho, e parametro que vai junto de
        /// cada comando (testado no ar do Thiago: aceito, nunca volta no estado).
        private static readonly string[] Internas = { "t_device_info", "t_temp_type", "t_power", "t_temp", "t_work_mode", "t_fan_speed", "t_beep" };

        private readonly Mostrador _dial;
        private readonly BotaoRedondo _menos, _mais, _liga, _noturno;
        private readonly Label _lbNoturno;
        private readonly ToolTip _dicaNoturno = new ToolTip();
        internal BotaoRedondo BotaoNoturno { get { return _noturno; } }
        private readonly Label _lbModo, _lbVento, _lbAlerta, _lbLiga;
        private readonly Blocos _modo, _vento;
        private readonly Segmented _unidade;
        private readonly Label _lbUnidade;
        private readonly List<Toggle> _chaves = new List<Toggle>();
        private readonly System.Windows.Forms.Timer _espera;
        private int _alvoPendente = -1;
        private string _assinatura = "";
        private readonly Toggle _semBipe;
        private bool _ajustando;

        // Secoes de baixo: Funcoes, Preferencias, Cenas, Timer; editor e diagnostico por cima de tudo.
        private readonly Secao _secFuncoes, _secPref, _secCenas, _secTimer, _secEditor, _secDiag;
        private readonly PainelDeCenas _cenas;
        private readonly EditorDeCena _editor;
        private readonly PainelDoTimer _timer;
        private readonly Label _diag;
        private readonly BotaoChave _chave;
        private readonly ToolTip _dica = new ToolTip();
        private int _editando = -2;   // -2 nenhuma, -1 nova, >=0 indice
        private bool _diagAberto;

        internal PainelDeCenas Cenas { get { return _cenas; } }
        internal EditorDeCena Editor { get { return _editor; } }
        internal BotaoChave Chave { get { return _chave; } }
        internal Secao[] Secoes { get { return new[] { _secFuncoes, _secPref, _secCenas, _secTimer, _secEditor, _secDiag }; } }

        /// <summary>Funcoes e Preferencias moram na aba Ajustes; o cartao continua dono delas.</summary>
        internal Secao SecaoFuncoes { get { return _secFuncoes; } }
        internal Secao SecaoPreferencias { get { return _secPref; } }
        internal Secao SecaoRetorno { get { return _secRetorno; } }

        // Padrao para onde o ar volta depois de uma cena com tempo (aba Ajustes).
        private readonly Secao _secRetorno;
        private readonly Blocos _rtModo;
        private readonly NumberBox _rtTemp;
        private readonly Segmented _rtVento;
        private readonly Label _lbRtModo, _lbRtTemp, _lbRtVento, _rtNota;
        private int[] _rtVentos = new int[0];
        private string _rtAssinatura = "";

        /// <summary>Arranja o conteudo de uma secao hospedada fora do cartao e devolve a altura dele.</summary>
        internal int ConteudoExterno(Secao s, int w) { return Conteudo(s, w); }

        /// <summary>Preferencia do app, nao do aparelho: a pagina guarda na config.</summary>
        public bool SemBipe;
        public bool AceitaBipe { get { return Grava("t_beep"); } }
        public event EventHandler SemBipeMudou;

        public override int Tipo { get { return 0; } }
        protected override string Icone { get { return "\uE9CA"; } }

        public CartaoDoAr()
        {
            _dial = new Mostrador();
            _dial.Arrastando += delegate { _alvoPendente = _dial.Valor; };
            _dial.Escolheu += delegate { _alvoPendente = _dial.Valor; _espera.Stop(); _espera.Start(); };
            Controls.Add(_dial);

            _menos = new BotaoRedondo("\uE738"); _menos.AccessibleName = T.CasaMenosGrau; _menos.Click += delegate { Mudar(-1); };
            _mais = new BotaoRedondo("\uE710"); _mais.AccessibleName = T.CasaMaisGrau; _mais.Click += delegate { Mudar(+1); };
            _liga = new BotaoRedondo("\uE7E8"); _liga.Grande = true; _liga.AccessibleName = T.CasaLigaDesliga;
            _liga.Click += delegate { Mandar("t_power", Ligado ? 0 : 1); };
            Controls.Add(_menos); Controls.Add(_mais); Controls.Add(_liga);
            _lbLiga = Rotulo("", Ui.FontMed, Ui.Text);
            _lbLiga.TextAlign = ContentAlignment.MiddleLeft;

            // Noturno em um clique. t_sleep vai sozinho no comando (ServicoCasa.Partes).
            _noturno = new BotaoRedondo("");
            _noturno.Desenho = IconesCasa.Lua;
            _noturno.AccessibleName = T.CasaFuncao("t_sleep");
            _noturno.Click += delegate { Mandar("t_sleep", A != null && A.Int("t_sleep", 0) != 0 ? 0 : 1); };
            _dicaNoturno.SetToolTip(_noturno, T.CasaFuncao("t_sleep"));
            Controls.Add(_noturno);
            _lbNoturno = Rotulo("", Ui.FontSmall, Ui.Muted);
            _lbNoturno.TextAlign = ContentAlignment.MiddleRight;

            _lbModo = Rotulo(T.CasaModo, Ui.FontSmall, Ui.Muted);
            _modo = new Blocos();
            _modo.Escolheu += delegate { Mandar("t_work_mode", _modo.Selecionado); };
            Controls.Add(_modo);

            _lbVento = Rotulo(T.CasaVentilador, Ui.FontSmall, Ui.Muted);
            _vento = new Blocos();
            _vento.ComBarras = true;
            _vento.Escolheu += delegate { Mandar("t_fan_speed", _vento.Selecionado); };
            Controls.Add(_vento);

            _lbAlerta = Rotulo("", Ui.FontSmall, Ui.Danger);

            // --- Funcoes
            _secFuncoes = NovaSecao(T.CasaFuncoes);
            _semBipe = new Toggle();
            _semBipe.Label = T.CasaSemBipe;
            _semBipe.CheckedChanged += delegate
            {
                if (_ajustando || Carregando) return;
                SemBipe = _semBipe.Checked;
                if (SemBipeMudou != null) SemBipeMudou(this, EventArgs.Empty);
            };
            _secFuncoes.Controls.Add(_semBipe);

            // --- Preferencias: a unidade, rotulo a esquerda e seletor a direita
            _secPref = NovaSecao(T.CasaPreferencias);
            _lbUnidade = new Label();
            _lbUnidade.Text = T.CasaUnidade; _lbUnidade.Font = Ui.FontBase; _lbUnidade.ForeColor = Ui.Text;
            _lbUnidade.AutoSize = false; _lbUnidade.BackColor = Color.Transparent; _lbUnidade.TextAlign = ContentAlignment.MiddleLeft;
            _secPref.Controls.Add(_lbUnidade);
            _unidade = new Segmented();
            _unidade.SetItems("°C", "°F");
            _unidade.SelectedIndexChanged += delegate { Mandar("t_temp_type", _unidade.SelectedIndex); };
            _secPref.Controls.Add(_unidade);

            // --- Padrao depois das cenas (mora na aba Ajustes)
            _secRetorno = NovaSecao(T.CasaRetornoSecao);
            _lbRtModo = RotuloEm(_secRetorno, T.CasaModo);
            _rtModo = new Blocos();
            _rtModo.Escolheu += delegate { if (Carregando) return; ServicoCasa.Pref.RetornoModo = _rtModo.Selecionado; ServicoCasa.Pref.Salvar(); };
            _secRetorno.Controls.Add(_rtModo);
            _lbRtTemp = RotuloEm(_secRetorno, T.CasaTemperatura + " (°C)");
            _rtTemp = new NumberBox();
            _rtTemp.Minimum = 16; _rtTemp.Maximum = 30;
            _rtTemp.Value = Math.Max(16, Math.Min(30, ServicoCasa.Pref.RetornoTemp));
            _rtTemp.ValueChanged += delegate { if (Carregando) return; ServicoCasa.Pref.RetornoTemp = _rtTemp.Value; ServicoCasa.Pref.Salvar(); };
            _secRetorno.Controls.Add(_rtTemp);
            _lbRtVento = RotuloEm(_secRetorno, T.CasaVentilador);
            _rtVento = new Segmented();
            _rtVento.SelectedIndexChanged += delegate
            {
                if (Carregando || _rtVento.SelectedIndex >= _rtVentos.Length) return;
                ServicoCasa.Pref.RetornoVento = _rtVentos[_rtVento.SelectedIndex];
                ServicoCasa.Pref.Salvar();
            };
            _secRetorno.Controls.Add(_rtVento);
            _rtNota = RotuloEm(_secRetorno, T.CasaRetornoNota);
            _rtNota.ForeColor = Ui.Muted;

            // --- Cenas
            _secCenas = NovaSecao(T.CasaCenas);
            _secCenas.Acao.Text = T.CasaEditar;
            _secCenas.Acao.Visible = true;
            _secCenas.Acao.Click += delegate { AlternarEdicao(); };
            _cenas = new PainelDeCenas();
            _cenas.Rodar += delegate(object s, CenaEventArgs e)
            {
                MandarVarios(e.Propriedades, true);
                if (A == null) return;
                if (e.Cena != null && e.Cena.Duracao > 0 && !e.Cena.Desligar) ServicoCasa.AgendarRetorno(A.Puid, e.Cena.Duracao);
                else ServicoCasa.CancelarRetorno();
            };
            _cenas.Editar += AbrirEditor;
            _secCenas.Controls.Add(_cenas);

            _secEditor = NovaSecao(T.CasaEditarCena);
            _secEditor.Acao.Text = T.CasaRestaurarPadroes;
            _secEditor.Acao.Visible = true;
            _secEditor.Acao.Click += delegate { RestaurarPadroes(); };
            _editor = new EditorDeCena();
            _editor.Salvar += delegate { SalvarCena(); };
            _editor.Cancelar += delegate { FecharEditor(); };
            _editor.Excluir += delegate { ExcluirCena(); };
            _editor.Antes += delegate { MoverCena(-1); };
            _editor.Depois += delegate { MoverCena(+1); };
            _secEditor.Controls.Add(_editor);
            _secEditor.Visible = false;

            // --- Timer
            _secTimer = NovaSecao(T.CasaTimer);
            _timer = new PainelDoTimer();
            _secTimer.Controls.Add(_timer);

            // --- Diagnostico: aberto pela chave do cabecalho
            _secDiag = NovaSecao(T.CasaDiagnostico);
            _secDiag.Acao.Text = T.Close;
            _secDiag.Acao.Visible = true;
            _secDiag.Acao.Click += delegate { AlternarDiagnostico(); };
            _diag = new Label();
            _diag.Font = Ui.FontSmall; _diag.ForeColor = Ui.Text; _diag.AutoSize = false; _diag.BackColor = Color.Transparent;
            _secDiag.Controls.Add(_diag);
            _secDiag.Visible = false;

            _chave = new BotaoChave();
            _chave.Click += delegate { AlternarDiagnostico(); };
            _dica.SetToolTip(_chave, T.CasaDiagnostico);
            Controls.Add(_chave);

            // A temperatura vai em lote: tres cliques em + viram um comando so.
            _espera = new System.Windows.Forms.Timer();
            _espera.Interval = 700;
            _espera.Tick += delegate
            {
                _espera.Stop();
                if (_alvoPendente < 0) return;
                int v = _alvoPendente;
                _alvoPendente = -1;
                Mandar("t_temp", v);
            };
        }

        private static Label RotuloEm(Control pai, string t)
        {
            Label l = new Label();
            l.Text = t; l.Font = Ui.FontSmall; l.ForeColor = Ui.Text; l.AutoSize = false; l.BackColor = Color.Transparent;
            pai.Controls.Add(l);
            return l;
        }

        /// <summary>Opcoes do padrao de retorno conforme o modelo (modos e velocidades aceitos).</summary>
        private void PreencherRetorno()
        {
            List<int> modos = Valores("t_work_mode", new[] { 4, 2, 3, 0, 1 });
            List<int> ventos = CenaCasa.Velocidades(A);
            ventos.Remove(0);
            string ass = string.Join(",", modos.ConvertAll<string>(delegate(int v) { return v.ToString(); }).ToArray()) + "|" +
                string.Join(",", ventos.ConvertAll<string>(delegate(int v) { return v.ToString(); }).ToArray());
            if (ass != _rtAssinatura)
            {
                _rtAssinatura = ass;
                _rtModo.Itens.Clear();
                foreach (int v in modos)
                {
                    Blocos.Item it = new Blocos.Item();
                    it.Valor = v; it.Rotulo = TabelaDoAr.Modo(v, A.Tipo); it.Icone = TabelaDoAr.IconeDoModo(v, A.Tipo);
                    _rtModo.Itens.Add(it);
                }
                _rtVentos = ventos.ToArray();
                List<string> rot = new List<string>();
                foreach (int v in _rtVentos) rot.Add(TabelaDoAr.Ventilador(v, A.Tipo));
                _rtVento.SetItems(rot.ToArray());
            }
            int min, max;
            CenaCasa.FaixaC(A, out min, out max);
            _rtTemp.Minimum = min; _rtTemp.Maximum = max;
            _rtTemp.Value = Math.Max(min, Math.Min(max, ServicoCasa.Pref.RetornoTemp));
            _rtModo.Selecionado = ServicoCasa.Pref.RetornoModo;
            _rtModo.Invalidate();
            int iv = Array.IndexOf(_rtVentos, ServicoCasa.Pref.RetornoVento);
            if (iv >= 0) _rtVento.SelectedIndex = iv;
            _secRetorno.Visible = _rtModo.Itens.Count > 0 && Grava("t_temp");
        }

        private Secao NovaSecao(string titulo)
        {
            Secao s = new Secao(titulo);
            Controls.Add(s);
            return s;
        }

        private bool Ligado { get { return A != null && A.Int("t_power", 0) != 0; } }
        private bool Fahrenheit { get { return A != null && A.Int("t_temp_type", 0) == 1; } }

        private MetaPropriedade Meta(string chave)
        {
            MetaPropriedade m;
            return A != null && A.Meta != null && A.Meta.TryGetValue(chave, out m) ? m : null;
        }

        /// <summary>Gravavel segundo o modelo; sem a lista do modelo, vale ter a propriedade no estado.</summary>
        private bool Grava(string chave)
        {
            if (A == null) return false;
            if (A.Meta != null && A.Meta.Count > 0) { MetaPropriedade m = Meta(chave); return m != null && m.Grava; }
            return A.Tem(chave);
        }

        private List<int> Valores(string chave, int[] padrao)
        {
            MetaPropriedade m = Meta(chave);
            if (m != null && m.Valores.Count > 0) return m.Valores;
            List<int> l = new List<int>(padrao);
            if (A != null && A.Tem(chave) && !l.Contains(A.Int(chave, 0))) l.Add(A.Int(chave, 0));
            return l;
        }

        private void Faixa(out int min, out int max)
        {
            min = Fahrenheit ? 61 : 16; max = Fahrenheit ? 86 : 30;
            MetaPropriedade m = Meta("t_temp");
            if (m == null || m.Faixas.Count == 0) return;
            int[] f = m.Faixas[Fahrenheit && m.Faixas.Count > 1 ? 1 : 0];
            min = f[0]; max = f[1];
        }

        private void Mudar(int passo)
        {
            if (A == null || !Grava("t_temp")) return;
            int min, max;
            Faixa(out min, out max);
            int atual = _alvoPendente >= 0 ? _alvoPendente : A.Int("t_temp", 24);
            _alvoPendente = Math.Max(min, Math.Min(max, atual + passo));
            _dial.Valor = _alvoPendente;
            _dial.Invalidate();
            _espera.Stop(); _espera.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _espera.Dispose(); _dica.Dispose(); _dicaNoturno.Dispose(); }
            base.Dispose(disposing);
        }

        // ---------------- cenas: edicao ----------------

        private static List<CenaCasa> Lista { get { return ServicoCasa.Pref.Cenas; } }

        private void AlternarEdicao()
        {
            _cenas.Editando = !_cenas.Editando;
            _secCenas.Acao.Text = _cenas.Editando ? T.CasaConcluir : T.CasaEditar;
            _secCenas.PosicionarAcao();
            if (!_cenas.Editando) FecharEditor();
            else AvisarAltura();
            _cenas.Invalidate();
        }

        internal void AbrirEditor(int indice)
        {
            if (!_cenas.Editando) AlternarEdicao();
            _editando = indice;
            _editor.Nova = indice < 0;
            CenaCasa c = indice >= 0 && indice < Lista.Count ? Lista[indice] : NovaCena();
            _editor.Carregar(c, A, Lista.Count > 1);
            _cenas.Selecionada = indice >= 0 ? indice : Lista.Count;
            _secEditor.Titulo = indice >= 0 ? T.CasaEditarCena + ": " + c.Nome : T.CasaNovaCena;
            _secEditor.Visible = true;
            _secEditor.Invalidate();
            AvisarAltura();
        }

        private static CenaCasa NovaCena()
        {
            CenaCasa c = new CenaCasa();
            c.Nome = T.CasaNovaCena;
            c.Icone = "floco";
            c.Temp = 24;
            return c;
        }

        private void FecharEditor()
        {
            _editando = -2;
            _cenas.Selecionada = -1;
            _secEditor.Visible = false;
            AvisarAltura();
        }

        private void Gravar()
        {
            ServicoCasa.Pref.Salvar();
            PreencherRetorno();
            _cenas.Mostrar(A, Lista);
        }

        private void SalvarCena()
        {
            CenaCasa c = _editor.Ler();
            if (_editando >= 0 && _editando < Lista.Count) Lista[_editando] = c;
            else if (Lista.Count < CenaCasa.MaxCenas) Lista.Add(c);
            Gravar();
            FecharEditor();
        }

        private void ExcluirCena()
        {
            if (_editando >= 0 && _editando < Lista.Count) Lista.RemoveAt(_editando);
            Gravar();
            FecharEditor();
        }

        private void MoverCena(int passo)
        {
            int de = _editando, para = _editando + passo;
            if (de < 0 || para < 0 || para >= Lista.Count) return;
            CenaCasa c = Lista[de];
            Lista.RemoveAt(de);
            Lista.Insert(para, c);
            _editando = para;
            _cenas.Selecionada = para;
            Gravar();
        }

        private void RestaurarPadroes()
        {
            ServicoCasa.Pref.Cenas = CenaCasa.Padrao();
            Gravar();
            FecharEditor();
        }

        internal void AlternarDiagnostico()
        {
            _diagAberto = !_diagAberto;
            _chave.Aberto = _diagAberto;
            _chave.Invalidate();
            _secDiag.Visible = _diagAberto;
            if (_diagAberto) _diag.Text = Diagnostico.Texto(A);
            AvisarAltura();
        }

        // ---------------- estado ----------------

        protected override void Preencher()
        {
            bool on = A.Online;
            bool ligado = Ligado;
            int modo = A.Int("t_work_mode", -1);
            Color cor = TabelaDoAr.CorDoModo(modo, A.Tipo);

            // --- mostrador
            int min, max;
            Faixa(out min, out max);
            _dial.Min = min; _dial.Max = max;
            if (_alvoPendente < 0) _dial.Valor = Math.Max(min, Math.Min(max, A.Int("t_temp", min)));
            _dial.Unidade = Fahrenheit ? "°F" : "°C";
            _dial.Ambiente = A.Tem("f_temp_in") ? (double?)A.Num("f_temp_in", 0) : null;
            _dial.Ligado = ligado;
            _dial.Cor = cor;
            _dial.Legenda = !on ? T.CasaOfflineLonga : ligado ? TabelaDoAr.Modo(modo, A.Tipo) : T.CasaDesligado;
            _dial.Enabled = on && Grava("t_temp");
            _dial.Invalidate();
            _menos.Enabled = _mais.Enabled = on && Grava("t_temp");

            _liga.Aceso = ligado;
            _liga.Enabled = on && Grava("t_power");
            _lbLiga.Text = ligado ? T.CasaLigado : T.CasaDesligado;
            _lbLiga.ForeColor = ligado ? Ui.AccentText : Ui.Muted;

            // --- modo e ventilador: so os valores que o modelo aceita
            List<int> modos = Valores("t_work_mode", new[] { 4, 2, 3, 0, 1 });
            List<int> ventos = Valores("t_fan_speed", new int[0]);
            string assinatura = string.Join(",", modos.ConvertAll<string>(delegate(int v) { return v.ToString(); }).ToArray()) + "|" +
                string.Join(",", ventos.ConvertAll<string>(delegate(int v) { return v.ToString(); }).ToArray()) + "|" + Fahrenheit;
            if (assinatura != _assinatura)
            {
                _assinatura = assinatura;
                _modo.Itens.Clear();
                foreach (int v in modos)
                {
                    Blocos.Item it = new Blocos.Item();
                    it.Valor = v; it.Rotulo = TabelaDoAr.Modo(v, A.Tipo); it.Icone = TabelaDoAr.IconeDoModo(v, A.Tipo);
                    _modo.Itens.Add(it);
                }
                _vento.Itens.Clear();
                List<int> ordem = new List<int>(ventos);
                ordem.Sort();
                // Auto (0) vai por ultimo nas barras: nao e uma velocidade.
                if (ordem.Remove(0)) ordem.Add(0);
                foreach (int v in ordem)
                {
                    Blocos.Item it = new Blocos.Item();
                    it.Valor = v; it.Rotulo = TabelaDoAr.Ventilador(v, A.Tipo);
                    _vento.Itens.Add(it);
                }
            }
            _modo.Selecionado = modo;
            _modo.Cor = cor;
            _modo.Visible = _lbModo.Visible = A.Tem("t_work_mode") && _modo.Itens.Count > 0;
            _modo.Enabled = on && ligado && Grava("t_work_mode");
            _modo.Invalidate();
            _vento.Selecionado = A.Int("t_fan_speed", int.MinValue);
            _vento.Cor = cor;
            _vento.Visible = _lbVento.Visible = A.Tem("t_fan_speed") && _vento.Itens.Count > 0;
            _vento.Enabled = on && ligado && Grava("t_fan_speed");
            _vento.Invalidate();

            // --- interruptores: os conhecidos e qualquer outro 0/1 gravavel que o modelo declarar
            List<string> chaves = new List<string>();
            // O noturno tem botao proprio no painel; nao repete como interruptor.
            bool botaoNoturno = CenaCasa.Aceita(A, "t_sleep", 1);
            foreach (string k in Interruptores)
                if ((A.Tem(k) || (Meta(k) != null && Meta(k).Grava)) && !(k == "t_sleep" && botaoNoturno)) chaves.Add(k);
            if (A.Meta != null)
                foreach (MetaPropriedade m in A.Meta.Values)
                    if (m.Grava && m.Valores.Count == 2 && m.Valores.Contains(0) && m.Valores.Contains(1) &&
                        !chaves.Contains(m.Chave) && Array.IndexOf(Internas, m.Chave) < 0 && !(m.Chave == "t_sleep" && botaoNoturno))
                        chaves.Add(m.Chave);
            SincronizarChaves(chaves);
            foreach (Toggle t in _chaves)
            {
                t.Checked = A.Int((string)t.Tag, 0) != 0;
                t.Enabled = on && Grava((string)t.Tag);
            }
            bool noturno = A.Int("t_sleep", 0) != 0;
            _noturno.Visible = _lbNoturno.Visible = botaoNoturno;
            _noturno.Aceso = noturno;
            _noturno.Enabled = on && ligado;
            _noturno.Invalidate();
            _lbNoturno.Text = noturno ? T.CasaNoturnoLigado : T.CasaFuncao("t_sleep");
            _lbNoturno.ForeColor = noturno ? Ui.AccentText : Ui.Muted;
            _semBipe.Visible = Grava("t_beep");
            _ajustando = true;
            try { _semBipe.Checked = SemBipe; }
            finally { _ajustando = false; }
            _semBipe.Enabled = on;
            // Visible de um filho reflete o pai escondido (aba Painel): decide pelo modelo.
            _secFuncoes.Visible = _chaves.Count > 0 || Grava("t_beep");

            bool unidade = Grava("t_temp_type") || A.Tem("t_temp_type");
            _secPref.Visible = unidade;
            _unidade.SelectedIndex = Fahrenheit ? 1 : 0;
            _unidade.Enabled = on && Grava("t_temp_type");

            // --- alertas: as falhas f_e_* que o aparelho acusar
            List<string> alertas = new List<string>();
            foreach (KeyValuePair<string, string> kv in A.Estado)
            {
                if (!kv.Key.StartsWith("f_e_")) continue;
                int v = A.Int(kv.Key, 0);
                if (v == 0) continue;
                alertas.Add(kv.Key == "f_e_push" ? string.Format(T.CasaAlertaCodigo, v) : T.CasaFalha(kv.Key, Meta(kv.Key)));
            }
            if (A.Int("f_filter", 0) != 0) alertas.Add(T.CasaFiltro);
            _lbAlerta.Text = alertas.Count > 0 ? "\u26A0  " + string.Join("  ·  ", alertas.ToArray()) : "";
            _lbAlerta.Visible = alertas.Count > 0;
            _chave.Falha = Diagnostico.TemFalha(A);
            _chave.Invalidate();
            if (_diagAberto) _diag.Text = Diagnostico.Texto(A);

            // Extras de leitura ao lado do liga/desliga: umidade e potencia, se houver.
            List<string> leituras = new List<string>();
            if (A.Tem("f_humidity")) leituras.Add(string.Format(T.CasaUmidade, A.Int("f_humidity", 0)));
            if (A.Tem("f_power_display")) leituras.Add(A.Int("f_power_display", 0) + " W");
            _lbLiga.Tag = leituras.Count > 0 ? string.Join("  ·  ", leituras.ToArray()) : null;

            PreencherRetorno();
            _cenas.Mostrar(A, Lista);
            _secCenas.Visible = Lista.Count > 0 || _cenas.Editando || true;
            _timer.Mostrar(A);
            _secTimer.Visible = _timer.Disponivel;
        }

        private void SincronizarChaves(List<string> chaves)
        {
            bool igual = chaves.Count == _chaves.Count;
            for (int i = 0; igual && i < chaves.Count; i++) igual = (string)_chaves[i].Tag == chaves[i];
            if (igual) return;
            foreach (Toggle t in _chaves) { _secFuncoes.Controls.Remove(t); t.Dispose(); }
            _chaves.Clear();
            foreach (string k in chaves)
            {
                Toggle t = new Toggle();
                string chave = k;
                MetaPropriedade m = Meta(k);
                t.Label = T.CasaFuncao(k, m != null ? m.Nome : null);
                t.Tag = k;
                t.CheckedChanged += delegate { Mandar(chave, t.Checked ? 1 : 0); };
                _chaves.Add(t);
                _secFuncoes.Controls.Add(t);
            }
            AvisarAltura();
        }

        // ---------------- arranjo ----------------

        public override int Arranjar(int w)
        {
            int iw = w - 2 * M;
            int y0 = Cabeca + Ui.S(16);
            bool lado = iw >= Ui.S(640);
            // Empilhado, o mostrador abre a coluna: respiro extra entre o puxador e o divisor.
            int y0Dial = lado ? y0 : Cabeca + Ui.S(28);
            int dialLado = Ui.S(250);

            // --- chave do diagnostico, no cabecalho, a esquerda do selo
            int seloW = 0;
            foreach (string s in new[] { T.CasaOnline, T.CasaOffline, T.CasaEnviando })
                seloW = Math.Max(seloW, TextRenderer.MeasureText(s, Ui.FontSmall).Width + Ui.S(22));
            int kc = Ui.S(32);
            _chave.SetBounds(w - M - seloW - Ui.S(10) - kc, (Cabeca - kc) / 2, kc, kc);

            // --- coluna do mostrador: o mostrador centrado nela, com folga igual
            // dos dois lados; o anel inteiro (com o puxador) cabe dentro dele.
            int colDial = dialLado + 2 * Ui.S(12);
            int dx = lado ? M + (colDial - dialLado) / 2 : M + (iw - dialLado) / 2;
            _dial.SetBounds(dx, y0Dial, dialLado, dialLado);
            int bt = Ui.S(40);
            int yb = y0Dial + dialLado + Ui.S(4);
            int centro = dx + dialLado / 2;
            _menos.SetBounds(centro - Ui.S(16) - bt, yb, bt, bt);
            _mais.SetBounds(centro + Ui.S(16), yb, bt, bt);
            int fimDial = yb + bt + Ui.S(4);

            // --- coluna dos controles: liga, modo e ventilador
            int cx = lado ? M + colDial + Ui.S(20) : M;
            int cw = lado ? w - M - cx : iw;
            int y = lado ? y0 : fimDial + Ui.S(8);

            int lw = Ui.S(56);
            _liga.SetBounds(cx, y, lw, lw);
            int nw = Ui.S(44);
            _noturno.SetBounds(cx + cw - nw, y + (lw - nw) / 2, nw, nw);
            _lbNoturno.SetBounds(_noturno.Left - Ui.S(150) - Ui.S(10), y + (lw - Ui.S(20)) / 2, Ui.S(150), Ui.S(20));
            int fimLiga = _noturno.Visible ? _lbNoturno.Left - Ui.S(8) : cx + cw;
            _lbLiga.SetBounds(cx + lw + Ui.S(12), y + (lw - Ui.S(24)) / 2 - (_lbLiga.Tag != null ? Ui.S(8) : 0), Math.Max(Ui.S(60), fimLiga - cx - lw - Ui.S(12)), Ui.S(24));
            y += lw + Ui.S(14);

            if (_modo.Visible)
            {
                _lbModo.SetBounds(cx, y, cw, Ui.S(18));
                _modo.SetBounds(cx, y + Ui.S(22), cw, Ui.S(72));
                y += Ui.S(22) + Ui.S(72) + Ui.S(14);
            }
            if (_vento.Visible)
            {
                _lbVento.SetBounds(cx, y, cw, Ui.S(18));
                _vento.SetBounds(cx, y + Ui.S(22), cw, Ui.S(44));
                y += Ui.S(22) + Ui.S(44) + Ui.S(14);
            }
            if (_lbAlerta.Visible)
            {
                Size s = TextRenderer.MeasureText(_lbAlerta.Text, _lbAlerta.Font, new Size(cw, 0), TextFormatFlags.WordBreak);
                _lbAlerta.SetBounds(cx, y + Ui.S(4), cw, s.Height + Ui.S(2));
                y = _lbAlerta.Bottom + Ui.S(6);
            }
            // Lado a lado, o mostrador (com - e +) desce para o meio da altura da
            // coluna de controles: no alto, o puxador ficava colado ao cabecalho.
            if (lado)
            {
                int bloco = fimDial - y0;
                int desce = Math.Max(Ui.S(12), (y - y0 - bloco) / 2);
                foreach (Control c in new Control[] { _dial, _menos, _mais }) c.Top += desce;
                fimDial += desce;
            }
            int fim = Math.Max(lado ? fimDial : 0, y) + Ui.S(10);
            return ArranjarSecoes(M, fim, iw) + Ui.S(16);
        }

        /// <summary>
        /// Grade das secoes: duas colunas na janela larga (Funcoes | Preferencias,
        /// Cenas | Timer, com alturas iguais por linha) e uma na estreita. O editor
        /// de cenas e o diagnostico ocupam a largura toda, embaixo.
        /// </summary>
        private int ArranjarSecoes(int x, int y, int w)
        {
            int gap = Ui.Gap;
            bool duas = w >= Ui.S(700);
            int colW = duas ? (w - gap) / 2 : w;
            Secao[][] linhas = { new[] { _secFuncoes, _secPref }, new[] { _secCenas, _secTimer } };
            foreach (Secao[] linha in linhas)
            {
                List<Secao> vis = new List<Secao>();
                // Secao hospedada na aba Ajustes nao entra no grid do cartao.
                foreach (Secao s in linha) if (s.Visible && s.Parent == this) vis.Add(s);
                if (vis.Count == 0) continue;
                if (duas)
                {
                    int sw = vis.Count == 1 ? w : colW;
                    int h = 0;
                    foreach (Secao s in vis) h = Math.Max(h, Altura(s, sw));
                    for (int i = 0; i < vis.Count; i++)
                    {
                        vis[i].SetBounds(x + i * (colW + gap), y, sw, h);
                        Conteudo(vis[i], sw);
                    }
                    y += h + gap;
                }
                else
                    foreach (Secao s in vis)
                    {
                        int h = Altura(s, w);
                        s.SetBounds(x, y, w, h);
                        Conteudo(s, w);
                        y += h + gap;
                    }
            }
            foreach (Secao s in new[] { _secEditor, _secDiag })
            {
                if (!s.Visible) continue;
                int h = Altura(s, w);
                s.SetBounds(x, y, w, h);
                Conteudo(s, w);
                y += h + gap;
            }
            return y - gap;
        }

        /// <summary>Altura da secao para a largura dada (titulo + conteudo + margem).</summary>
        private int Altura(Secao s, int w)
        {
            return Secao.Topo + Conteudo(s, w) + Secao.Margem;
        }

        /// <summary>Posiciona o conteudo da secao e devolve a altura dele.</summary>
        private int Conteudo(Secao s, int w)
        {
            int m = Secao.Margem, iw = w - 2 * m, y = Secao.Topo;
            s.PosicionarAcao();
            if (s == _secFuncoes)
            {
                List<Toggle> todas = new List<Toggle>(_chaves);
                if (Grava("t_beep")) todas.Add(_semBipe);
                for (int i = 0; i < todas.Count; i++) todas[i].SetBounds(m, y + i * Ui.S(36), iw, Ui.S(32));
                return Math.Max(Ui.S(32), todas.Count * Ui.S(36) - Ui.S(4));
            }
            if (s == _secPref)
            {
                int uw = Ui.S(120);
                _lbUnidade.SetBounds(m, y, iw - uw - Ui.S(12), Ui.S(32));
                _unidade.SetBounds(m + iw - uw, y, uw, Ui.S(32));
                return Ui.S(32);
            }
            if (s == _secRetorno)
            {
                int lw = Ui.S(110);
                // Modo com a largura toda: em meia janela, "Desumidificar" nao cabia ao lado do rotulo.
                _lbRtModo.SetBounds(m, y, iw, Ui.S(18));
                _rtModo.SetBounds(m, y + Ui.S(22), iw, Ui.S(64));
                int y2 = y + Ui.S(22) + Ui.S(72);
                _lbRtTemp.SetBounds(m, y2 + Ui.S(7), lw, Ui.S(18));
                _rtTemp.SetBounds(m + lw, y2, Ui.S(110), Ui.S(32));
                int y3 = y2 + Ui.S(40);
                _lbRtVento.SetBounds(m, y3 + Ui.S(7), lw, Ui.S(18));
                _rtVento.SetBounds(m + lw, y3, Math.Min(iw - lw, Ui.S(300)), Ui.S(32));
                int y4 = y3 + Ui.S(42);
                Size tn = TextRenderer.MeasureText(_rtNota.Text, _rtNota.Font, new Size(iw, 0), TextFormatFlags.WordBreak);
                _rtNota.SetBounds(m, y4, iw, tn.Height + Ui.S(2));
                return _rtNota.Bottom - y;
            }
            if (s == _secCenas)
            {
                int h = _cenas.Altura(iw);
                _cenas.SetBounds(m, y, iw, h);
                return h;
            }
            if (s == _secTimer)
            {
                int h = _timer.Arranjar(iw);
                _timer.SetBounds(m, y, iw, h);
                return h;
            }
            if (s == _secEditor)
            {
                int h = _editor.Arranjar(iw);
                _editor.SetBounds(m, y, iw, h);
                return h;
            }
            if (s == _secDiag)
            {
                Size t = TextRenderer.MeasureText(_diag.Text.Length > 0 ? _diag.Text : " ", _diag.Font, new Size(iw, 0), TextFormatFlags.WordBreak);
                _diag.SetBounds(m, y, iw, t.Height + Ui.S(2));
                return _diag.Height;
            }
            return 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            string leituras = _lbLiga.Tag as string;
            if (A == null || leituras == null) return;
            TextRenderer.DrawText(e.Graphics, leituras, Ui.FontSmall,
                new Rectangle(_lbLiga.Left, _lbLiga.Bottom, _lbLiga.Width, Ui.S(18)), Ui.Muted, TextFormatFlags.Left);
        }
    }
    public static partial class T
    {
        public static string CasaModoN { get { return P("Modo {0}", "Mode {0}"); } }
        public static string CasaNoturnoLigado { get { return P("Noturno ligado", "Sleep on"); } }
        public static string CasaEco { get { return P("ECO", "Eco"); } }
        public static string CasaSemBipe { get { return P("Comandos sem bipe", "Silent commands (no beep)"); } }
        public static string CasaVelocidadeN { get { return P("Nível {0}", "Level {0}"); } }
        public static string CasaVentoMinimo { get { return P("Mínima", "Lowest"); } }
        public static string CasaVentoBaixo { get { return P("Baixa", "Low"); } }
        public static string CasaVentoMedio { get { return P("Média", "Medium"); } }
        public static string CasaVentoAlto { get { return P("Alta", "High"); } }
        public static string CasaVentoMaximo { get { return P("Máxima", "Highest"); } }
        public static string CasaUnidade { get { return P("Unidade de temperatura", "Temperature unit"); } }
        public static string CasaAmbienteCurto { get { return P("ambiente {0}", "room {0}"); } }
        public static string CasaMenosGrau { get { return P("Diminuir temperatura", "Lower temperature"); } }
        public static string CasaMaisGrau { get { return P("Aumentar temperatura", "Raise temperature"); } }
        public static string CasaLigaDesliga { get { return P("Ligar ou desligar", "Turn on or off"); } }
        public static string CasaAlertaCodigo { get { return P("alerta do aparelho (código {0})", "appliance alert (code {0})"); } }

        internal static string CasaFalha(string chave, MetaPropriedade m)
        {
            switch (chave)
            {
                case "f_e_incoiltemp": return P("falha no sensor da serpentina", "coil sensor fault");
                case "f_e_incom": return P("falha de comunicação interna", "internal communication fault");
                case "f_e_ineeprom": return P("falha na memória da placa", "control board memory fault");
                case "f_e_intemp": return P("falha no sensor de temperatura", "temperature sensor fault");
                case "f_e_over_cold": return P("proteção contra congelamento", "over-cooling protection");
                case "f_e_over_hot": return P("proteção contra superaquecimento", "overheating protection");
                default: return m != null && m.Nome.Length > 0 ? m.Nome : chave;
            }
        }

        /// <summary>Nome do interruptor; sem nome conhecido, o que a nuvem mandou, ou a chave.</summary>
        public static string CasaFuncao(string chave, string nomeDaNuvem)
        {
            switch (chave)
            {
                case "t_fresh_air": return P("Ar externo", "Fresh air");
                case "t_tms": return P("Modo inteligente", "Smart mode");
                case "t_dimmer": return P("Luz do painel", "Display light");
                case "t_beep": return P("Bipe ao receber comando", "Beep on command");
            }
            string conhecido = CasaFuncao(chave);
            if (conhecido != chave) return conhecido;
            return !string.IsNullOrEmpty(nomeDaNuvem) ? nomeDaNuvem : chave;
        }
    }
}
