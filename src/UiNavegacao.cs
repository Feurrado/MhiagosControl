using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>Uma entrada da barra lateral.</summary>
    public class NavItem
    {
        public string Text;
        public string Glyph;     // caractere de Segoe MDL2 Assets
        /// <summary>
        /// Icone desenhado a mao no lugar do glifo, quando a fonte nao tem um
        /// que sirva. Recebe o quadrado que o glifo ocuparia e a cor dele.
        /// </summary>
        public Action<Graphics, RectangleF, Color> Icone;
        public Control Page;

        /// <summary>Nome estavel da pagina, para gravar a ordem; nao muda com o idioma.</summary>
        public string Key;
        internal Rectangle Bounds;
    }

    /// <summary>
    /// Barra lateral de navegacao: cabecalho com o icone do aplicativo e a
    /// lista de secoes. Separar por secao evita a tela unica sobrecarregada.
    /// </summary>
    public class NavBar : Control
    {
        private readonly System.Collections.Generic.List<NavItem> _items = new System.Collections.Generic.List<NavItem>();
        private int _selected = 0, _hover = -1;
        private int _hoverVis = -1;
        private Transicao _tNav;
        private Transicao TNav { get { return _tNav ?? (_tNav = new Transicao(this, Motion.Rapida, AreaDoHover)); } }
        private Rectangle AreaDoHover() { return _hoverVis >= 0 && _hoverVis < _items.Count ? Rectangle.Inflate(_items[_hoverVis].Bounds, 1, 1) : Rectangle.Empty; }
        private readonly Dictionary<int, Bitmap> _logos = new Dictionary<int, Bitmap>();

        /// <summary>O logotipo ja no tamanho de tela: reduzir a imagem grande a cada pintura era o que mais pesava na barra.</summary>
        private Bitmap LogoEm(int lado)
        {
            Bitmap b;
            if (_logos.TryGetValue(lado, out b)) return b;
            b = new Bitmap(lado, lado, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics gb = Graphics.FromImage(b))
            {
                gb.InterpolationMode = InterpolationMode.HighQualityBicubic;
                gb.DrawImage(Logo, 0, 0, lado, lado);
            }
            _logos[lado] = b;
            return b;
        }
        public event EventHandler SelectionChanged;

        public Image Logo;
        public string AppName = "Mhiagos Control";

        /// <summary>Legenda curta acima do subtitulo - "PERFIL ATIVO".</summary>
        public string SubtitleCaption = "";

        /// <summary>
        /// Nome do perfil ativo.
        ///
        /// Ganha uma linha inteira do cabecalho, e nao o resto da linha do nome
        /// do aplicativo: ao lado do titulo sobravam 150 px e "GPU TEMP + GPU
        /// USAGE" era cortado no meio de uma letra, sem nem as reticencias que
        /// avisariam que havia mais texto.
        /// </summary>
        public string Subtitle = "";

        // O resumo da maquina saiu do rodape da barra. Processador, video e
        // memoria agora tem uma aba inteira - com fabricante, cache, VBIOS e o
        // resto - e repetir tres linhas na lateral era ocupar a coluna de
        // navegacao para dizer de novo, pior, o que esta a um clique.

        public NavBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Ui.FontBase;
            TabStop = true;
            AccessibleRole = AccessibleRole.PageTabList;
            AccessibleName = T.AppName;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End
                || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Ctrl+setas leva a pagina selecionada junto: o mesmo que arrastar,
            // para quem prefere o teclado.
            if (e.Control && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                int de = SelectedIndex;
                int para = e.KeyCode == Keys.Up ? de - 1 : de + 1;
                if (para >= 0 && para < _items.Count) MoverItem(de, para);
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Up) SelectedIndex = Math.Max(0, SelectedIndex - 1);
            else if (e.KeyCode == Keys.Down) SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex + 1);
            else if (e.KeyCode == Keys.Home) SelectedIndex = 0;
            else if (e.KeyCode == Keys.End) SelectedIndex = _items.Count - 1;
            else { base.OnKeyDown(e); return; }
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        public void Add(NavItem item) { _items.Add(item); Invalidate(); }

        /// <summary>As paginas na ordem em que aparecem.</summary>
        public System.Collections.Generic.IList<NavItem> Items { get { return _items.AsReadOnly(); } }

        /// <summary>A pessoa mudou a ordem, arrastando ou com Ctrl+setas.</summary>
        public event EventHandler OrderChanged;

        /// <summary>
        /// Leva o item da posicao "de" para a posicao "para" (ja contada sem
        /// ele). A selecao acompanha a PAGINA, e nao a posicao: arrastar a
        /// pagina aberta para baixo nao pode trocar o que esta na tela.
        /// </summary>
        public void MoverItem(int de, int para)
        {
            if (de < 0 || de >= _items.Count) return;
            if (para < 0) para = 0;
            if (para > _items.Count - 1) para = _items.Count - 1;
            if (para == de) return;

            NavItem selecionado = Selected;
            NavItem it = _items[de];
            _items.RemoveAt(de);
            _items.Insert(para, it);
            _selected = selecionado != null ? _items.IndexOf(selecionado) : 0;
            _hover = -1;
            Invalidate();
            if (OrderChanged != null) OrderChanged(this, EventArgs.Empty);
        }

        public NavItem Selected
        {
            get { return (_selected >= 0 && _selected < _items.Count) ? _items[_selected] : null; }
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value < 0 || value >= _items.Count || value == _selected) return;
                _selected = value;
                Invalidate();
                if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acessivel(this); }

        /// <summary>Lista de abas para o leitor de tela; o valor e a pagina aberta.</summary>
        private sealed class Acessivel : ControlAccessibleObject
        {
            private readonly NavBar _dono;
            public Acessivel(NavBar dono) : base(dono) { _dono = dono; }
            public override AccessibleRole Role { get { return AccessibleRole.PageTabList; } }
            public override string Value
            {
                get { return _dono.Selected != null ? _dono.Selected.Text : ""; }
                set { }
            }
        }

        private static readonly int HeaderH = Ui.S(116);
        private static readonly int RowH = Ui.S(40);

        public static readonly int LarguraRecolhida = Ui.S(56);
        private static readonly int LarguraMinima = Ui.S(210);
        private static readonly int LarguraMaxima = Ui.S(330);

        private int _larguraAberta = LarguraMinima;

        /// <summary>
        /// Ajusta a largura ao conteudo mais comprido da coluna.
        ///
        /// "Radeon RX 580: Sapphire Nitro+" nao cabe em 210 px e era cortado no
        /// modelo. Encurtar mais o nome nao resolve - o que sobra ja e o nome
        /// util da peca, e cortar dentro dele e perder informacao. Entao a
        /// coluna cede.
        ///
        /// Com teto: passando de 330 px a barra vira o assunto da janela em vez
        /// da moldura dela, e um nome absurdo de longo levaria a interface
        /// junto. Acima do teto, aí sim reticencias - o corte volta a ser a
        /// resposta certa, mas so no caso extremo.
        /// </summary>
        public void AjustarLargura()
        {
            int precisa = LarguraMinima;

            foreach (NavItem it in _items)
                precisa = Math.Max(precisa,
                    Ui.S(42) + TextRenderer.MeasureText(it.Text, Ui.FontMed).Width + Ui.S(16));

            precisa = Math.Max(precisa, Ui.S(60) + TextRenderer.MeasureText(AppName, Ui.FontMed).Width + Ui.S(46));
            if (!string.IsNullOrEmpty(Subtitle))
                precisa = Math.Max(precisa, Ui.S(18) + TextRenderer.MeasureText(Subtitle, Ui.FontBase).Width + Ui.S(18));

            // O nome das pecas nao entra mais na conta: com o resumo fora da
            // barra, "AMD Ryzen 5 5600X" alargava a coluna de navegacao para
            // acomodar um texto que nao esta mais nela.
            _larguraAberta = Math.Min(precisa, LarguraMaxima);
            if (!_collapsed && _alvo == 0) Width = _larguraAberta;
        }

        /// <summary>
        /// Recolhe a barra para uma faixa so de icones.
        ///
        /// Recolher em vez de sumir de vez: uma barra escondida por completo
        /// leva junto o caminho de volta, e sobra procurar onde clicar para
        /// traze-la. Com a faixa, o botao que reabre fica onde estava o que
        /// fechou, e navegar continua possivel sem reabrir nada.
        /// </summary>
        public bool Collapsed
        {
            get { return _collapsed; }
            set
            {
                if (_collapsed == value) return;
                _collapsed = value;
                _hover = -1;
                Animar(value ? LarguraRecolhida : _larguraAberta);
                if (CollapsedChanged != null) CollapsedChanged(this, EventArgs.Empty);
            }
        }
        private bool _collapsed = false;

        public event EventHandler CollapsedChanged;

        // ---------------- animacao ----------------

        private AnimacaoDeRecolher _anim;
        private int _alvo = 0, _origem = 0;

        /// <summary>
        /// Desliza a largura ate o alvo.
        ///
        /// Pelo mesmo relogio dos paineis que abrem e fecham (AnimacaoDeRecolher):
        /// 250 ms, saida suave, Stopwatch. Instantaneo, a coluna parece piscar e
        /// a pagina ao lado salta sem explicar de onde veio o espaco; muito mais
        /// lento, vira espera.
        ///
        /// Sem animacao nenhuma se a janela ainda nao tem alca, ou com os efeitos
        /// visuais desligados no Windows: vai direto a largura final.
        /// </summary>
        private void Animar(int alvo)
        {
            if (_anim == null)
            {
                _anim = new AnimacaoDeRecolher(false);
                _anim.Quadro += delegate
                {
                    if (_alvo == 0) return;
                    Width = _origem + (int)Math.Round((_alvo - _origem) * _anim.Valor);
                    Invalidate();
                };
                _anim.Terminou += delegate
                {
                    if (_alvo == 0) return;
                    Width = _alvo;
                    _alvo = 0;
                    Invalidate();
                };
            }

            // Recomeca do zero a partir da largura de agora: virar no meio do
            // deslize parte de onde a barra esta, sem salto.
            _alvo = 0;
            _anim.Ir(false, false);
            _origem = Width;
            _alvo = alvo;
            _anim.Ir(true, IsHandleCreated);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _anim != null) { _anim.Dispose(); _anim = null; }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Durante o deslize o desenho segue a LARGURA, e nao o estado alvo.
        ///
        /// Trocar o conteudo no clique e animar so a moldura faz o texto sumir
        /// de uma vez e a barra encolher depois, em dois tempos. Decidindo pela
        /// largura corrente, o conteudo cede quando o espaco acaba - que e o que
        /// a pessoa espera ver.
        /// </summary>
        private bool Estreita { get { return Width < Ui.S(150); } }

        private Rectangle _botao = Rectangle.Empty;
        private bool _sobreBotao = false;

        /// <summary>
        /// Quanto a barra esta aberta, de 0 (faixa de icones) a 1 (aberta), pela
        /// largura de agora.
        ///
        /// O desenho saltava entre dois arranjos quando a largura cruzava 150 px:
        /// os icones pulavam do centro da faixa para a esquerda e 12 px para
        /// baixo, o logotipo trocava de tamanho e o botao de lugar. Agora cada
        /// posicao vai de um arranjo ao outro na proporcao da largura, e o
        /// texto aparece e some aos poucos.
        /// </summary>
        internal float Abertura
        {
            get
            {
                int faixa = _larguraAberta - LarguraRecolhida;
                if (faixa <= 0) return Width > LarguraRecolhida ? 1f : 0f;
                return Math.Max(0f, Math.Min(1f, (Width - LarguraRecolhida) / (float)faixa));
            }
        }

        private static float Entre(float a, float b, float t) { return a + (b - a) * t; }
        private static int Entre(int a, int b, float t) { return (int)Math.Round(a + (b - a) * t); }

        /// <summary>A cor do texto a caminho do fundo: some sem pular.</summary>
        private static Color Esmaecer(Color cor, float t)
        {
            // o texto so comeca a aparecer depois da metade, quando ja ha espaco
            float a = Math.Max(0f, Math.Min(1f, (t - 0.45f) / 0.55f));
            Color f = Ui.Sidebar;
            return Color.FromArgb((int)(f.R + (cor.R - f.R) * a), (int)(f.G + (cor.G - f.G) * a), (int)(f.B + (cor.B - f.B) * a));
        }

        private int TopoDosItens { get { return Entre(Ui.S(104), HeaderH, Abertura); } }

        // ---------------- arrastar para reordenar ----------------

        /// <summary>Item pressionado, candidato a arraste; -1 sem botao apertado.</summary>
        private int _pressionado = -1;
        private Point _pontoDoClique;
        private bool _arrastando = false;

        /// <summary>Onde o item cairia se soltasse agora, contado como posicao de insercao (0..Count).</summary>
        private int _destino = -1;

        /// <summary>Px de folga antes de um clique virar arraste: mao tremula nao reordena nada.</summary>
        private static readonly int LimiarDeArraste = Ui.S(6);

        private int PosicaoDeInsercao(int y)
        {
            int pos = (int)Math.Floor((y - TopoDosItens + (RowH + Ui.S(2)) / 2.0) / (RowH + Ui.S(2)));
            return Math.Max(0, Math.Min(_items.Count, pos));
        }

        private void TerminarArraste(bool aplicar)
        {
            int de = _pressionado, ate = _destino;
            bool estava = _arrastando;
            _pressionado = -1; _arrastando = false; _destino = -1;
            Capture = false;
            Cursor = Cursors.Default;
            Invalidate();
            if (!estava || !aplicar || de < 0 || ate < 0) return;
            // posicao de insercao conta o proprio item; depois de tira-lo, quem
            // estava abaixo sobe uma casa
            int para = ate > de ? ate - 1 : ate;
            MoverItem(de, para);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) TerminarArraste(true);
            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (_arrastando && !Capture) TerminarArraste(false);
            base.OnMouseCaptureChanged(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_pressionado >= 0 && (e.Button & MouseButtons.Left) != 0)
            {
                if (!_arrastando && Math.Abs(e.Y - _pontoDoClique.Y) >= LimiarDeArraste)
                {
                    _arrastando = true;
                    Capture = true;
                    Cursor = Cursors.SizeNS;
                }
                if (_arrastando)
                {
                    int d = PosicaoDeInsercao(e.Y);
                    if (d != _destino) { _destino = d; Invalidate(); }
                    base.OnMouseMove(e);
                    return;
                }
            }

            bool nb = _botao.Contains(e.Location);
            int h = nb ? -1 : HitTest(e.Location);
            if (h != _hover || nb != _sobreBotao)
            {
                _hover = h; _sobreBotao = nb;
                if (h >= 0) { if (h != _hoverVis) TNav.Zerar(); _hoverVis = h; TNav.Ir(1f); }
                else TNav.Ir(0f);
                Cursor = (h >= 0 || nb) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1; _sobreBotao = false; TNav.Ir(0f); base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (_botao.Contains(e.Location)) { Collapsed = !Collapsed; base.OnMouseDown(e); return; }
            int h = HitTest(e.Location);
            if (h >= 0) SelectedIndex = h;
            if (h >= 0 && e.Button == MouseButtons.Left) { _pressionado = h; _pontoDoClique = e.Location; }
            base.OnMouseDown(e);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Bounds.Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            using (SolidBrush b = new SolidBrush(Ui.Sidebar)) g.FillRectangle(b, ClientRectangle);

            DesenharCabecalho(g);

            float t = Abertura;
            int margem = Entre(Ui.S(6), Ui.S(8), t);
            int y = TopoDosItens;
            for (int i = 0; i < _items.Count; i++)
            {
                NavItem it = _items[i];
                it.Bounds = new Rectangle(margem, y, Width - 2 * margem, RowH);

                bool sel = (i == _selected);
                if (sel)
                    using (GraphicsPath p = Ui.RoundRect(it.Bounds, Ui.RaioControle))
                    using (SolidBrush b = new SolidBrush(Ui.AccentSoft))
                        g.FillPath(b, p);
                else if (i == _hoverVis && TNav.Valor > 0f)
                    using (GraphicsPath p = Ui.RoundRect(it.Bounds, Ui.RaioControle))
                    using (SolidBrush b = new SolidBrush(Ui.Misturar(Color.FromArgb(0, Ui.Hover), Ui.Hover, TNav.Valor)))
                        g.FillPath(b, p);
                if (sel)
                {
                    using (GraphicsPath p = Ui.RoundRect(new Rectangle(it.Bounds.X, it.Bounds.Y + Ui.S(9), Ui.S(3), RowH - Ui.S(18)), Ui.S(2)))
                    using (SolidBrush b = new SolidBrush(Ui.Accent))
                        g.FillPath(b, p);
                }

                Color fore = sel ? Ui.Text : Ui.Muted;
                // Recolhida, o glifo fica no centro da faixa; aberta, encostado
                // a esquerda do texto. No caminho, entre os dois.
                float faixa = LarguraRecolhida - 2 * Ui.S(6);
                if (it.Icone != null)
                {
                    // o mesmo quadrado que um glifo de 11 pt ocuparia ali
                    float em = Ui.FontGlyph11.SizeInPoints * g.DpiY / 72f;
                    float gx = Entre(Ui.S(6) + (faixa - em) / 2f, Ui.S(8) + Ui.S(14) + em / 6f, t);
                    RectangleF cel = new RectangleF(gx, it.Bounds.Y + (RowH - em) / 2f, em, em);
                    it.Icone(g, cel, sel ? Ui.Accent : fore);
                }
                else if (!string.IsNullOrEmpty(it.Glyph))
                {
                    using (SolidBrush b = new SolidBrush(sel ? Ui.Accent : fore))
                    {
                        SizeF tg = g.MeasureString(it.Glyph, Ui.FontGlyph11);
                        float gx = Entre(Ui.S(6) + (faixa - tg.Width) / 2f, Ui.S(8) + Ui.S(14), t);
                        g.DrawString(it.Glyph, Ui.FontGlyph11, b, gx, it.Bounds.Y + Ui.S(11));
                    }
                }
                if (t > 0.45f)
                {
                    // No meio do deslize o texto e cortado pela borda, e nao
                    // trocado por reticencias que mudam a cada quadro.
                    bool deslizando = t < 1f;
                    int largura = deslizando ? _larguraAberta - 2 * Ui.S(8) - Ui.S(46) : it.Bounds.Width - Ui.S(46);
                    TextRenderer.DrawText(g, it.Text, sel ? Ui.FontMed : Ui.FontBase,
                        new Rectangle(it.Bounds.X + Ui.S(42), it.Bounds.Y, largura, RowH), Esmaecer(fore, t),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        (deslizando ? TextFormatFlags.SingleLine : TextFormatFlags.EndEllipsis));
                }

                y += RowH + Ui.S(2);
            }

            // Arrastando: uma linha marca onde a pagina vai cair.
            if (_arrastando && _destino >= 0)
            {
                int ly = TopoDosItens + _destino * (RowH + Ui.S(2)) - 1;
                int x0 = Ui.S(Estreita ? 6 : 8), x1 = Width - Ui.S(Estreita ? 6 : 8);
                using (Pen pen = new Pen(Ui.Accent, 2f))
                    g.DrawLine(pen, x0 + Ui.S(4), ly, x1 - Ui.S(4), ly);
                using (SolidBrush b = new SolidBrush(Ui.Accent))
                {
                    g.FillEllipse(b, x0, ly - Ui.S(3), Ui.S(6), Ui.S(6));
                    g.FillEllipse(b, x1 - Ui.S(6), ly - Ui.S(3), Ui.S(6), Ui.S(6));
                }
            }
        }

        /// <summary>
        /// Cabecalho: logotipo, nome, perfil ativo e o botao que recolhe.
        ///
        /// Recolhida, sobra o logotipo e o botao. Nome do aplicativo e perfil
        /// ativo nao encolhem para 56 px - virariam duas reticencias, que ocupam
        /// espaco sem informar.
        /// </summary>
        private void DesenharCabecalho(Graphics g)
        {
            // Logotipo e botao andam entre os dois arranjos na proporcao da
            // largura; nome e perfil aparecem aos poucos, depois da metade.
            float t = Abertura;
            int lado = Entre(Ui.S(30), Ui.S(34), t);
            int lx = Entre((LarguraRecolhida - Ui.S(30)) / 2, Ui.S(18), t);
            if (Logo != null) g.DrawImageUnscaled(LogoEm(lado), lx, Ui.S(18));

            if (t > 0.45f)
            {
                bool deslizando = t < 1f;
                TextFormatFlags corte = deslizando ? TextFormatFlags.SingleLine : TextFormatFlags.EndEllipsis;
                // Cortado antes do botao, que no meio do caminho passa por aqui.
                int xBotao = Entre((LarguraRecolhida - Ui.S(30)) / 2, Width - Ui.S(42), t);
                TextRenderer.DrawText(g, AppName ?? "", Ui.FontMed,
                    new Rectangle(Ui.S(60), Ui.S(26), Math.Max(0, xBotao - Ui.S(64)), Ui.S(20)), Esmaecer(Ui.Text, t),
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

                // O perfil ocupa a largura toda, abaixo do titulo. TextRenderer
                // com retangulo delimitado, e nao DrawString: DrawString corta
                // onde o controle acaba, sem reticencias.
                int w = (deslizando ? _larguraAberta : Width) - Ui.S(34);
                if (!string.IsNullOrEmpty(SubtitleCaption))
                    TextRenderer.DrawText(g, SubtitleCaption, Ui.FontSmall,
                        new Rectangle(Ui.S(18), Ui.S(62), w, Ui.S(14)), Esmaecer(Ui.Faint, t),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | corte);

                if (!string.IsNullOrEmpty(Subtitle))
                    TextRenderer.DrawText(g, Subtitle, Ui.FontBase,
                        new Rectangle(Ui.S(18), Ui.S(78), w, Ui.S(18)), Esmaecer(Ui.Text, t),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | corte);
            }

            _botao = new Rectangle(Entre((LarguraRecolhida - Ui.S(30)) / 2, Width - Ui.S(42), t),
                                   Entre(Ui.S(60), Ui.S(20), t), Ui.S(30), Ui.S(30));

            if (_sobreBotao)
            {
                using (GraphicsPath p = Ui.RoundRect(_botao, Ui.RaioControle))
                using (SolidBrush b = new SolidBrush(Ui.Hover))
                    g.FillPath(b, p);
            }

            // E700 e o "hamburguer" da Segoe MDL2 - escapado, e nao colado como
            // caractere, porque area de uso privado nao sobrevive a toda
            // ferramenta que passa pelo arquivo.
            using (SolidBrush b = new SolidBrush(_sobreBotao ? Ui.Text : Ui.Muted))
            {
                SizeF tm = g.MeasureString("", Ui.FontGlyph11);
                g.DrawString("", Ui.FontGlyph11, b,
                    _botao.X + (_botao.Width - tm.Width) / 2f,
                    _botao.Y + (_botao.Height - tm.Height) / 2f);
            }
        }

    }
}
