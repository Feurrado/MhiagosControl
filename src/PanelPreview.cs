using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Previa dos mostradores sobre a vista superior do cooler.
    ///
    /// A imagem da peca e mostrada INTEIRA, encaixada no controle, e o mostrador
    /// e desenhado por cima na posicao em que fica no aparelho - no terco
    /// superior da chapa. Versoes anteriores recortavam uma faixa da chapa com a
    /// proporcao do controle; como o miolo dela e liso, o resultado era um
    /// retangulo escuro em que ninguem reconhecia o cooler.
    ///
    /// Os digitos sao sete segmentos monocromaticos, como no aparelho, e cada
    /// mostrador tem DOIS indicadores de unidade empilhados - um aceso, o outro
    /// apagado. Estado (sem leitura ou estouro) e sinalizado FORA do
    /// mostrador: o hardware so acende e apaga segmentos, entao colorir os
    /// digitos ensinaria algo falso sobre o que ele consegue fazer.
    /// </summary>
    public class PanelPreview : Control
    {
        // mapa dos segmentos por digito: a b c d e f g
        private static readonly bool[][] Digits = new bool[][]
        {
            new bool[]{true ,true ,true ,true ,true ,true ,false}, // 0
            new bool[]{false,true ,true ,false,false,false,false}, // 1
            new bool[]{true ,true ,false,true ,true ,false,true }, // 2
            new bool[]{true ,true ,true ,true ,false,false,true }, // 3
            new bool[]{false,true ,true ,false,false,true ,true }, // 4
            new bool[]{true ,false,true ,true ,false,true ,true }, // 5
            new bool[]{true ,false,true ,true ,true ,true ,true }, // 6
            new bool[]{true ,true ,true ,false,false,false,false}, // 7
            new bool[]{true ,true ,true ,true ,true ,true ,true }, // 8
            new bool[]{true ,true ,true ,true ,false,true ,true }, // 9
        };

        /// <summary>null representa o mostrador apagado (sem leitura).</summary>
        public int? Value1 { get; set; }
        public int? Value2 { get; set; }
        public bool Fahrenheit { get; set; }
        public bool Percent { get; set; }

        /// <summary>Desenha a foto do cooler atras dos mostradores.</summary>
        public bool ShowCooler = true;

        // Chapa superior, em fracao da imagem: define onde o mostrador pousa.
        // A vista tem grelhas vazadas em cima e embaixo e uma tira lateral a
        // direita, que ficam fora dessa regiao.
        private const float PlateL = 0.075f, PlateR = 0.765f;
        private const float PlateT = 0.190f, PlateB = 0.815f;

        // Num modulo de LED real o segmento apagado quase desaparece no vidro:
        // nao e cinza medio, e uma sombra. Desenhar como cinza dava aparencia de
        // LCD, que e justamente o que destoava da peca.
        private static readonly Color SegOn = Color.FromArgb(240, 245, 252);
        private static readonly Color SegOff = Color.FromArgb(42, 105, 116, 132);

        public PanelPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Value1 = 0; Value2 = 0; Percent = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 40 || Height < 30) return;

            Graphics g = e.Graphics;
            Render(g);

            // Arredondar por mascara de anel ou por recorte pintava meia-tinta
            // do cartao em TODA a volta: a aresta pousa exatamente no limite do
            // controle e o antisserrilhado lhe da cobertura parcial. Repintar
            // so os quatro cantos, com as cunhas avancando para FORA, deixa a
            // suavizacao restrita a curva - que e onde ela e desejada.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Parent != null ? Parent.BackColor : Ui.Surface))
            using (GraphicsPath gp = CornerWedges(Width, Height, 8))
                g.FillPath(b, gp);
        }

        /// <summary>As sobras entre o retangulo do controle e os cantos arredondados.</summary>
        private static GraphicsPath CornerWedges(int w, int h, int r)
        {
            const int o = 2;          // quanto cada cunha avanca para fora
            int d = r * 2;
            GraphicsPath p = new GraphicsPath();

            p.StartFigure();                              // superior esquerdo
            p.AddArc(0, 0, d, d, 180, 90);
            p.AddLine(r, -o, -o, -o);
            p.AddLine(-o, -o, -o, r);
            p.CloseFigure();

            p.StartFigure();                              // superior direito
            p.AddArc(w - d, 0, d, d, 270, 90);
            p.AddLine(w + o, r, w + o, -o);
            p.AddLine(w + o, -o, w - r, -o);
            p.CloseFigure();

            p.StartFigure();                              // inferior direito
            p.AddArc(w - d, h - d, d, d, 0, 90);
            p.AddLine(w - r, h + o, w + o, h + o);
            p.AddLine(w + o, h + o, w + o, h - r);
            p.CloseFigure();

            p.StartFigure();                              // inferior esquerdo
            p.AddArc(0, h - d, d, d, 90, 90);
            p.AddLine(-o, h - r, -o, h + o);
            p.AddLine(-o, h + o, r, h + o);
            p.CloseFigure();

            return p;
        }

        private Image _fonteDoCache;
        private Bitmap _cache;
        private int _cacheW, _cacheH;
        private Font _unitFont;
        private float _unitFontSize;

        private Font UnitFont(float size)
        {
            if (_unitFont == null || Math.Abs(_unitFontSize - size) > 0.05f)
            {
                if (_unitFont != null) _unitFont.Dispose();
                _unitFont = new Font("Segoe UI", size, FontStyle.Bold);
                _unitFontSize = size;
            }
            return _unitFont;
        }

        /// <summary>
        /// A foto do cooler ja no tamanho em que vai ser desenhada.
        ///
        /// O arquivo tem 900x900 e chega na tela com menos de metade disso. Feita
        /// a cada pintura, essa reducao em bicubica de alta qualidade custa mais
        /// do que todo o resto do desenho junto - e a pintura acontece a cada
        /// mudanca de tamanho, por causa do ResizeRedraw.
        ///
        /// Enquanto a barra lateral desliza, ela roda uma vez a cada 15 ms e a
        /// animacao engasga. O que salva o cache e o encaixe ser pela ALTURA:
        /// recolher a lateral muda a largura do controle, nao a altura, entao o
        /// bitmap reduzido e identico em todos os quadros - so o X em que ele e
        /// pousado muda. Cem por cento de acerto justamente quando importa.
        /// </summary>
        private Bitmap Escalado(Image origem, int w, int h)
        {
            if (_cache != null && _fonteDoCache == origem && _cacheW == w && _cacheH == h)
                return _cache;

            if (_cache != null) { _cache.Dispose(); _cache = null; }

            Bitmap b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics gb = Graphics.FromImage(b))
            {
                gb.InterpolationMode = InterpolationMode.HighQualityBicubic;
                gb.PixelOffsetMode = PixelOffsetMode.HighQuality;
                gb.CompositingQuality = CompositingQuality.HighQuality;
                gb.DrawImage(origem, new Rectangle(0, 0, w, h));
            }

            _cache = b; _fonteDoCache = origem; _cacheW = w; _cacheH = h;
            return _cache;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_cache != null) { _cache.Dispose(); _cache = null; }
                if (_unitFont != null) { _unitFont.Dispose(); _unitFont = null; }
            }
            base.Dispose(disposing);
        }

        private void Render(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            Image cooler = ShowCooler ? Assets.Cooler : null;

            // O fundo usa a cor de fundo DA IMAGEM. Com um cinza proprio, as
            // faixas que sobram dos lados do encaixe viravam duas listras
            // visiveis contra o preto da foto.
            //
            // Clear, e nao FillRectangle: preenchendo um retangulo do tamanho
            // exato do controle com antisserrilhado ligado, a fileira da borda
            // recebe cobertura parcial e deixa meia-tinta do cartao em volta.
            g.Clear(Backdrop(cooler));

            RectangleF plate;

            if (cooler != null && cooler.Width > 0 && cooler.Height > 0)
            {
                // encaixe por conteudo: a peca inteira cabe, sem corte nem distorcao
                float ratio = (float)cooler.Width / cooler.Height;
                int h = Height, w = (int)Math.Round(h * ratio);
                if (w > Width) { w = Width; h = (int)Math.Round(w / ratio); }
                Rectangle img = new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
                g.DrawImageUnscaled(Escalado(cooler, w, h), img.X, img.Y);

                plate = new RectangleF(
                    img.X + img.Width * PlateL,
                    img.Y + img.Height * PlateT,
                    img.Width * (PlateR - PlateL),
                    img.Height * (PlateB - PlateT));
            }
            else
            {
                plate = new RectangleF(Width * 0.1f, Height * 0.1f, Width * 0.8f, Height * 0.8f);
            }

            // Os dois mostradores ficam EMPILHADOS, temperatura em cima, como no
            // aparelho - nao lado a lado. Empilhar tambem devolve largura a cada
            // um, entao o digito sai maior do que na disposicao horizontal.
            float dispW = plate.Width * 0.62f;
            float dispH = dispW * 0.43f;
            float vgap = dispH * 0.18f;
            float total = dispH * 2f + vgap;

            float x = plate.X + (plate.Width - dispW) / 2f;
            float y = plate.Y + (plate.Height - total) / 2f;

            DrawDisplay(g, new RectangleF(x, y, dispW, dispH),
                        Value1, "°C", "°F", !Fahrenheit, true);
            DrawDisplay(g, new RectangleF(x, y + dispH + vgap, dispW, dispH),
                        Value2, "%", "W", Percent, false);
        }

        /// <summary>
        /// Cor de fundo da foto, amostrada de um canto. Assumir preto puro
        /// deixaria emenda se a imagem tivesse qualquer desvio.
        /// </summary>
        private static Color? _backdrop;
        private static Color Backdrop(Image cooler)
        {
            if (_backdrop.HasValue) return _backdrop.Value;
            Color c = Color.FromArgb(16, 17, 20);
            Bitmap bmp = cooler as Bitmap;
            if (bmp != null && bmp.Width > 4 && bmp.Height > 4)
            {
                try { c = bmp.GetPixel(2, 2); }
                catch (Exception ex) { Log.Error("previa: amostragem do fundo", ex); }
            }
            _backdrop = c;
            return c;
        }

        private void DrawDisplay(Graphics g, RectangleF area, int? value,
                                 string topLabel, string bottomLabel, bool topLit,
                                 bool badgeAbove)
        {
            bool blank = !value.HasValue;
            bool over = value.HasValue && (value.Value > 999 || value.Value < 0);
            int shown = value.HasValue ? Math.Min(Math.Max(value.Value, 0), 999) : 0;

            // Proporcao do modulo real: digito alto e estreito, traco fino.
            float dh = area.Height * 0.68f;
            float t = Math.Max(1.6f, dh / 14f);
            float gap = t * 0.40f;                    // recorte entre segmentos
            float top = area.Y + (area.Height - dh) / 2f;

            // A largura da coluna de unidades e MEDIDA, nao estimada: com fracao
            // fixa da area o simbolo transbordava para o mostrador vizinho,
            // porque a fonte cresce com a altura e a reserva nao acompanhava.
            float ufs = Math.Max(5.5f, dh * 0.30f);
            Font uf = UnitFont(ufs);
            float unitW;
            unitW = Math.Max(g.MeasureString(topLabel, uf).Width,
                             g.MeasureString(bottomLabel, uf).Width) + 3f;

            float cell = (area.Width - unitW) / 3f;
            float dw = cell * 0.54f;
            float left = area.X + (cell - dw) / 2f;

            int hundreds = shown / 100;
            int tens = (shown / 10) % 10;
            int units = shown % 10;

            // zeros a esquerda ficam apagados, como no hardware
            DrawDigit(g, left, top, dw, dh, t, gap, hundreds, !blank && hundreds > 0);
            DrawDigit(g, left + cell, top, dw, dh, t, gap, tens, !blank && shown >= 10);
            DrawDigit(g, left + cell * 2, top, dw, dh, t, gap, units, !blank);

            // Dois indicadores empilhados, como no aparelho: °C sobre °F, % sobre W.
            // O hardware acende um dos dois; o outro fica apagado, nao ausente.
            {
                float x = area.Right - unitW + 1f;
                SizeF sb = g.MeasureString(bottomLabel, uf);
                using (SolidBrush b = new SolidBrush(!blank && topLit ? SegOn : SegOff))
                    g.DrawString(topLabel, uf, b, x, top - 1f);
                using (SolidBrush b = new SolidBrush(!blank && !topLit ? SegOn : SegOff))
                    g.DrawString(bottomLabel, uf, b, x, top + dh - sb.Height + 1f);
            }

            string badge = null;
            Color mark = Color.Empty;
            if (blank) { badge = T.BadgeNoReading; mark = Color.FromArgb(165, 170, 180); }
            else if (over) { badge = T.BadgeOver999; mark = Ui.Warn; }

            if (badge != null)
            {
                Rectangle ring = Rectangle.Round(RectangleF.Inflate(area, 5f, 4f));
                using (Pen p = new Pen(mark, 1.4f))
                using (GraphicsPath gp = Rounded(ring, 6))
                    g.DrawPath(p, gp);

                {
                    SizeF sz = g.MeasureString(badge, Ui.FontBadge);
                    // Empilhados, o rotulo do de cima nao pode cair sobre o de
                    // baixo: ele sobe, o do mostrador inferior desce.
                    float ty = badgeAbove ? ring.Top - sz.Height - 7f : ring.Bottom + 5f;
                    RectangleF tag = new RectangleF(area.X + 4f, ty, sz.Width + 10f, sz.Height + 2f);
                    using (GraphicsPath gp = Rounded(Rectangle.Round(tag), 4))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(224, 15, 16, 20)))
                        g.FillPath(b, gp);
                    using (SolidBrush b = new SolidBrush(mark))
                        g.DrawString(badge, Ui.FontBadge, b, tag.X + 5f, tag.Y + 1f);
                }
            }
        }

        private void DrawDigit(Graphics g, float x, float y, float w, float h,
                               float t, float gap, int digit, bool lit)
        {
            bool[] seg = Digits[Math.Min(Math.Max(digit, 0), 9)];
            float mid = y + h / 2f;

            for (int i = 0; i < 7; i++)
            {
                bool active = lit && seg[i];
                PointF[] poly;
                switch (i)
                {
                    case 0: poly = Horizontal(x, x + w, y, t, gap); break;         // a
                    case 1: poly = Vertical(x + w, y, mid, t, gap); break;         // b
                    case 2: poly = Vertical(x + w, mid, y + h, t, gap); break;     // c
                    case 3: poly = Horizontal(x, x + w, y + h, t, gap); break;     // d
                    case 4: poly = Vertical(x, mid, y + h, t, gap); break;         // e
                    case 5: poly = Vertical(x, y, mid, t, gap); break;             // f
                    default: poly = Horizontal(x, x + w, mid, t, gap); break;      // g
                }

                // O LED aceso sangra luz no vidro em volta. Sem esse halo o
                // desenho fica chapado, com cara de vetor e nao de mostrador.
                if (active)
                {
                    using (Pen glow = new Pen(Color.FromArgb(34, SegOn), t * 1.6f))
                    {
                        glow.LineJoin = LineJoin.Round;
                        g.DrawPolygon(glow, poly);
                    }
                }

                using (SolidBrush b = new SolidBrush(active ? SegOn : SegOff))
                    g.FillPolygon(b, poly);
            }
        }

        // Os segmentos nao se encostam: cada um recua 'gap' nas duas pontas,
        // deixando o entalhe em V dos cantos que o modulo real tem.
        private static PointF[] Horizontal(float x1, float x2, float y, float t, float gap)
        {
            float h = t / 2f;
            x1 += gap; x2 -= gap;
            return new PointF[]
            {
                new PointF(x1 + h, y - h), new PointF(x2 - h, y - h), new PointF(x2, y),
                new PointF(x2 - h, y + h), new PointF(x1 + h, y + h), new PointF(x1, y)
            };
        }

        private static PointF[] Vertical(float x, float y1, float y2, float t, float gap)
        {
            float h = t / 2f;
            y1 += gap; y2 -= gap;
            return new PointF[]
            {
                new PointF(x - h, y1 + h), new PointF(x, y1), new PointF(x + h, y1 + h),
                new PointF(x + h, y2 - h), new PointF(x, y2), new PointF(x - h, y2 - h)
            };
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
