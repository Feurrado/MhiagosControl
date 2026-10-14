using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Navegador de periodo da aba Metricas: as seis horas guardadas numa faixa
    /// fina, com uma selecao arrastavel que define o trecho que os cartoes
    /// desenham.
    ///
    /// Substitui os tres botoes de 10 min, 1 h e 6 h. Os botoes respondiam
    /// "quanto tempo", mas nao "qual tempo": um pico de duas horas atras so
    /// aparecia na janela de 6 h, espremido num pixel. Aqui as bordas mudam o
    /// tamanho e o meio leva a selecao para tras no tempo, sem perder de vista
    /// onde ela esta dentro do todo. Duplo clique volta para o ao vivo.
    /// </summary>
    public class NavegadorDeJanela : Control
    {
        /// <summary>Leitura desenhada na faixa, so como referencia de forma.</summary>
        public string SensorId = "";

        /// <summary>Tamanho da selecao, em segundos.</summary>
        public int Janela = MetricHistory.JanelaPadrao;

        /// <summary>Balde absoluto onde a selecao termina; zero e ao vivo.</summary>
        public long FimBalde = 0;

        /// <summary>A selecao mudou: os cartoes redesenham. Dispara durante o arraste.</summary>
        public event EventHandler Mudou;

        /// <summary>O arraste terminou: hora de gravar o tamanho escolhido.</summary>
        public event EventHandler Confirmado;

        private const int Topo = 22, Rodape = 16, Pega = 7;

        private enum Arraste { Nada, Esquerda, Direita, Meio }
        private Arraste _arraste = Arraste.Nada;
        private int _xInicio;
        private int _janelaInicio;
        private long _atrasInicio;
        private float[] _buf;
        private Rectangle _btVivo;
        private bool _sobreVivo;

        public NavegadorDeJanela()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Window;
            Font = Ui.FontSmall;
            Size = new Size(400, 70);
        }

        public bool AoVivo { get { return FimBalde == 0; } }

        private Rectangle Faixa
        {
            get { return new Rectangle(1, Topo, Math.Max(10, Width - 2), Math.Max(10, Height - Topo - Rodape)); }
        }

        /// <summary>Baldes entre o fim da selecao e o agora.</summary>
        private long Atras
        {
            get
            {
                if (FimBalde == 0) return 0;
                long a = MetricHistory.UltimoBalde - FimBalde;
                return a < 0 ? 0 : a;
            }
        }

        private int Baldes { get { return Janela / MetricHistory.PassoSeg; } }

        private float XDoIndice(double i)
        {
            Rectangle f = Faixa;
            return f.X + (float)(i / (MetricHistory.Baldes - 1) * f.Width);
        }

        private float XFim { get { return XDoIndice(MetricHistory.Baldes - 1 - Atras); } }
        private float XIni { get { return XDoIndice(MetricHistory.Baldes - Atras - Baldes); } }

        // ---------------- desenho ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle f = Faixa;

            // cabecalho: o trecho escolhido em horas de relogio
            long fim = FimBalde == 0 ? MetricHistory.UltimoBalde : FimBalde;
            DateTime tFim = MetricHistory.HoraDoBalde(fim);
            DateTime tIni = tFim.AddSeconds(-Janela);
            string fmt = Janela <= 600 ? "HH:mm:ss" : "HH:mm";
            string titulo = tIni.ToString(fmt) + " – " + (AoVivo ? T.NavNow : tFim.ToString(fmt)) +
                            "  ·  " + MetricHistory.NomeDaJanela(Janela);
            TextRenderer.DrawText(g, titulo, Ui.FontSemi, new Point(0, 2), Ui.Text, TextFormatFlags.NoPadding);

            // a direita: o estado, que vira botao quando ha para onde voltar
            string vivo = AoVivo ? T.NavLive : T.NavBackToLive;
            Size tv = TextRenderer.MeasureText(g, vivo, Ui.FontSmall);
            _btVivo = new Rectangle(Width - tv.Width - 18, 0, tv.Width + 16, 18);
            using (GraphicsPath gp = Ui.RoundRect(_btVivo, 9))
            {
                Color fundo = AoVivo ? Ui.AccentSoft : (_sobreVivo ? Ui.Hover : Ui.Surface);
                using (SolidBrush b = new SolidBrush(fundo)) g.FillPath(b, gp);
                if (!AoVivo) using (Pen p = new Pen(Ui.Border)) g.DrawPath(p, gp);
            }
            if (AoVivo)
                using (SolidBrush b = new SolidBrush(Ui.Accent))
                    g.FillEllipse(b, _btVivo.X + 6, _btVivo.Y + 7, 5, 5);
            TextRenderer.DrawText(g, vivo, Ui.FontSmall,
                new Rectangle(_btVivo.X + (AoVivo ? 6 : 0), _btVivo.Y, _btVivo.Width, _btVivo.Height),
                AoVivo ? Ui.Accent : Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // faixa com as seis horas
            using (GraphicsPath gp = Ui.RoundRect(f, 4))
            {
                using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillPath(b, gp);
                using (Pen p = new Pen(Ui.Border)) g.DrawPath(p, gp);
            }
            DesenharResumo(g, f);

            // o que esta fora da selecao fica esmaecido
            float x0 = Math.Max(f.X, XIni), x1 = Math.Min(f.Right, XFim);
            if (x1 - x0 < 4) { float m = (x0 + x1) / 2; x0 = m - 2; x1 = m + 2; }
            using (SolidBrush veu = new SolidBrush(Color.FromArgb(Ui.Dark ? 150 : 140, Ui.Window)))
            {
                if (x0 > f.X) g.FillRectangle(veu, f.X, f.Y + 1, x0 - f.X, f.Height - 1);
                if (x1 < f.Right) g.FillRectangle(veu, x1, f.Y + 1, f.Right - x1, f.Height - 1);
            }
            RectangleF sel = new RectangleF(x0, f.Y, x1 - x0, f.Height);
            using (Pen p = new Pen(Ui.Accent, 1.6f)) g.DrawRectangle(p, sel.X, sel.Y, sel.Width, sel.Height);
            using (SolidBrush b = new SolidBrush(Ui.Text))
                foreach (float x in new float[] { x0, x1 })
                    using (GraphicsPath gp = Ui.RoundRect(new Rectangle((int)Math.Round(x) - 3, f.Y + f.Height / 2 - 8, 6, 16), 2))
                        g.FillPath(b, gp);

            // eixo: as pontas e as horas cheias de duas em duas
            int yEixo = f.Bottom + 2;
            TextRenderer.DrawText(g, T.HoursAgo(6), Ui.FontSmall, new Point(f.X, yEixo), Ui.Faint, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, T.Now, Ui.FontSmall, new Rectangle(f.X, yEixo, f.Width, 14), Ui.Faint,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);
            foreach (int h in new int[] { 4, 2 })
                TextRenderer.DrawText(g, T.HoursAgo(h), Ui.FontSmall,
                    new Rectangle((int)XDoIndice(MetricHistory.Baldes - 1 - h * 3600 / MetricHistory.PassoSeg) - 40, yEixo, 80, 14),
                    Ui.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }

        /// <summary>
        /// A forma da leitura nas seis horas: minimo e maximo por coluna de
        /// pixel. Com 4320 baldes numa faixa de 800 px, uma linha ponto a ponto
        /// viraria um borrao; a coluna mostra a amplitude de cada trecho e
        /// deixa o pico visivel, que e o que se procura aqui.
        /// </summary>
        private void DesenharResumo(Graphics g, Rectangle f)
        {
            if (string.IsNullOrEmpty(SensorId)) return;
            int n = MetricHistory.Janela(SensorId, MetricHistory.JanelaMaxima, 0, ref _buf);
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float v = _buf[i];
                if (float.IsNaN(v)) continue;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (min > max) return;
            float faixa = Math.Max(0.001f, max - min);
            float alto = f.Y + 4, alt = f.Height - 8;

            using (Pen p = new Pen(Color.FromArgb(150, Ui.Accent)))
            {
                int colunas = Math.Max(1, f.Width - 2);
                for (int c = 0; c < colunas; c++)
                {
                    int a = (int)((long)c * n / colunas), b = (int)((long)(c + 1) * n / colunas);
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int i = a; i < b && i < n; i++)
                    {
                        float v = _buf[i];
                        if (float.IsNaN(v)) continue;
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                    if (lo > hi) continue;
                    float x = f.X + 1 + c;
                    float y0 = alto + alt - (hi - min) / faixa * alt;
                    float y1 = alto + alt - (lo - min) / faixa * alt;
                    if (y1 - y0 < 1) y1 = y0 + 1;
                    g.DrawLine(p, x, y0, x, y1);
                }
            }
        }

        // ---------------- mouse ----------------

        private Arraste Onde(int x, int y)
        {
            Rectangle f = Faixa;
            if (y < f.Y - 2 || y > f.Bottom + 2) return Arraste.Nada;
            if (Math.Abs(x - XIni) <= Pega) return Arraste.Esquerda;
            if (Math.Abs(x - XFim) <= Pega) return Arraste.Direita;
            if (x > XIni && x < XFim) return Arraste.Meio;
            return Arraste.Nada;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool sobre = !AoVivo && _btVivo.Contains(e.Location);
            if (sobre != _sobreVivo) { _sobreVivo = sobre; Invalidate(); }

            if (_arraste == Arraste.Nada)
            {
                Arraste a = Onde(e.X, e.Y);
                Cursor = sobre ? Cursors.Hand
                       : a == Arraste.Esquerda || a == Arraste.Direita ? Cursors.SizeWE
                       : a == Arraste.Meio ? Cursors.SizeAll
                       : Faixa.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
                return;
            }

            double porPixel = (double)(MetricHistory.Baldes - 1) / Faixa.Width;
            long delta = (long)Math.Round((e.X - _xInicio) * porPixel);
            int baldesIni = _janelaInicio / MetricHistory.PassoSeg;
            long atras = _atrasInicio;
            int baldes = baldesIni;

            if (_arraste == Arraste.Meio) atras = _atrasInicio - delta;
            else if (_arraste == Arraste.Esquerda) baldes = baldesIni - (int)delta;
            else if (_arraste == Arraste.Direita)
            {
                // a borda direita move o fim e deixa o inicio onde estava
                atras = _atrasInicio - delta;
                if (atras < 0) atras = 0;
                baldes = baldesIni + (int)(_atrasInicio - atras);
            }
            Aplicar(baldes, atras);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (!AoVivo && _btVivo.Contains(e.Location)) { VoltarAoVivo(); return; }
            if (!Faixa.Contains(e.Location)) return;

            Arraste a = Onde(e.X, e.Y);
            if (a == Arraste.Nada)
            {
                // clique fora da selecao: ela vai para la, centrada no ponto
                double i = (double)(e.X - Faixa.X) / Faixa.Width * (MetricHistory.Baldes - 1);
                long atras = (long)Math.Round(MetricHistory.Baldes - 1 - i - Baldes / 2.0);
                Aplicar(Baldes, atras);
                a = Arraste.Meio;
            }
            _arraste = a;
            _xInicio = e.X;
            _janelaInicio = Janela;
            _atrasInicio = Atras;
            Capture = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_arraste == Arraste.Nada) return;
            _arraste = Arraste.Nada;
            Capture = false;
            EventHandler h = Confirmado;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (Faixa.Contains(e.Location)) VoltarAoVivo();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_sobreVivo) { _sobreVivo = false; Invalidate(); }
        }

        /// <summary>A roda aproxima e afasta, com o fim parado no lugar.</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            double fator = e.Delta > 0 ? 1 / 1.25 : 1.25;
            Aplicar((int)Math.Round(Baldes * fator), Atras);
            EventHandler h = Confirmado;
            if (h != null) h(this, EventArgs.Empty);
        }

        public void VoltarAoVivo()
        {
            if (AoVivo) return;
            FimBalde = 0;
            Invalidate();
            EventHandler h = Mudou;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>Limita tamanho e posicao ao que esta guardado e avisa quem desenha.</summary>
        private void Aplicar(int baldes, long atras)
        {
            int minimo = MetricHistory.JanelaMinima / MetricHistory.PassoSeg;
            if (baldes < minimo) baldes = minimo;
            if (baldes > MetricHistory.Baldes) baldes = MetricHistory.Baldes;
            if (atras < 0) atras = 0;
            if (atras > MetricHistory.Baldes - baldes) atras = MetricHistory.Baldes - baldes;

            int janela = baldes * MetricHistory.PassoSeg;
            // encostar na ponta direita e voltar ao vivo: e onde a pessoa quer
            // estar quando arrasta tudo para o fim
            long fim = atras == 0 ? 0 : MetricHistory.UltimoBalde - atras;
            if (janela == Janela && fim == FimBalde) return;
            Janela = janela;
            FimBalde = fim;
            Invalidate();
            EventHandler h = Mudou;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
