using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Tokens visuais e componentes desenhados.
    ///
    /// O WinForms nao oferece nada com acabamento moderno: botoes, listas e
    /// caixas de selecao carregam o visual do Windows 2000. Aqui definimos
    /// uma paleta unica e desenhamos os controles, o que da consistencia e
    /// permite acompanhar o tema claro/escuro do sistema.
    /// </summary>
    public static class Ui
    {
        public static bool Dark { get { return Theme.IsDark; } }

        // superficies
        public static Color Window { get { return Dark ? C(0x1C, 0x1C, 0x1F) : C(0xF4, 0xF5, 0xF7); } }
        public static Color Sidebar { get { return Dark ? C(0x17, 0x17, 0x1A) : C(0xEA, 0xEC, 0xF0); } }
        public static Color Surface { get { return Dark ? C(0x26, 0x26, 0x2A) : C(0xFF, 0xFF, 0xFF); } }
        public static Color SurfaceAlt { get { return Dark ? C(0x2E, 0x2E, 0x33) : C(0xF7, 0xF8, 0xFA); } }
        public static Color Border { get { return Dark ? C(0x38, 0x38, 0x3E) : C(0xDA, 0xDD, 0xE3); } }

        // texto
        public static Color Text { get { return Dark ? C(0xF0, 0xF0, 0xF3) : C(0x1B, 0x1D, 0x21); } }
        public static Color Muted { get { return Dark ? C(0x96, 0x99, 0xA3) : C(0x6B, 0x70, 0x7B); } }

        /// <summary>
        /// Um degrau abaixo de Muted, para nota de rodape sem sumir.
        ///
        /// Estava em 6C6F79 no tema escuro - perto demais do fundo. Somado ao
        /// corpo de 8,25 pt, o resultado nao lia como "secundario", lia como
        /// apagado. Hierarquia se faz com contraste suficiente para SER LIDO,
        /// e nao com texto no limite de desaparecer.
        /// </summary>
        public static Color Faint { get { return Dark ? C(0x8A, 0x8E, 0x99) : C(0x7C, 0x82, 0x8D); } }

        /// <summary>Polegar da barra de rolagem. Precisa se destacar do trilho
        /// sem competir com o texto: Border era escuro demais e sumia nele.</summary>
        public static Color Thumb { get { return Dark ? C(0x55, 0x58, 0x62) : C(0xB4, 0xBA, 0xC4); } }

        // enfase - azul do icone do aplicativo
        public static Color Accent { get { return C(0x2D, 0x7D, 0xF6); } }
        public static Color AccentHover { get { return C(0x4A, 0x91, 0xFF); } }
        public static Color AccentSoft { get { return Dark ? C(0x1E, 0x33, 0x52) : C(0xDE, 0xEA, 0xFF); } }

        // estados
        public static Color Warn { get { return C(0xF0, 0xAF, 0x3C); } }
        public static Color Danger { get { return C(0xEB, 0x5A, 0x4B); } }
        public static Color Hover { get { return Dark ? C(0x32, 0x32, 0x38) : C(0xEC, 0xEF, 0xF4); } }

        public static readonly Font FontBase = new Font("Segoe UI", 9f);
        public static readonly Font FontMed = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font FontTitle = new Font("Segoe UI", 14f, FontStyle.Regular);
        public static readonly Font FontSection = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        /// <summary>
        /// O corpo pequeno, usado em rotulo de apoio e nota.
        ///
        /// Era 8,25 pt. Nesse tamanho, em cinza, sobre fundo escuro, o texto nao
        /// e discreto - e fino: os tracos da Segoe UI ficam com menos de um pixel
        /// cheio e o antisserrilhado apaga o que sobra. Um ponto a mais devolve
        /// peso sem tirar a hierarquia, que continua vindo da COR.
        /// </summary>
        public static readonly Font FontSmall = new Font("Segoe UI", 9f);

        /// <summary>
        /// Seminegrito, para o valor que precisa ter presenca sem virar titulo.
        ///
        /// "Segoe UI Semibold" e uma familia propria no Windows, e nao um estilo
        /// da "Segoe UI": pedir FontStyle.Bold daria o negrito cheio, que num
        /// rotulo de tres palavras pesa demais.
        /// </summary>
        public static readonly Font FontSemi = Semi(9f);

        private static Font Semi(float tamanho)
        {
            try
            {
                Font f = new Font("Segoe UI Semibold", tamanho);
                // Quando a familia nao existe, o GDI+ devolve a substituta e o
                // nome nao bate: melhor cair no negrito do que num tipo alheio.
                if (f.Name.IndexOf("Semibold", StringComparison.OrdinalIgnoreCase) >= 0) return f;
                f.Dispose();
            }
            catch { }
            return new Font("Segoe UI", tamanho, FontStyle.Bold);
        }

        /// <summary>Leitura em destaque - o numero e o assunto do cartao.</summary>
        public static readonly Font FontValue = new Font("Segoe UI", 16f, FontStyle.Bold);
        public static readonly Font FontGlyph8 = new Font("Segoe MDL2 Assets", 8f);
        public static readonly Font FontGlyph9 = new Font("Segoe MDL2 Assets", 9f);
        public static readonly Font FontGlyph10 = new Font("Segoe MDL2 Assets", 10f);
        public static readonly Font FontGlyph11 = new Font("Segoe MDL2 Assets", 11f);
        public static readonly Font FontGlyph18 = new Font("Segoe MDL2 Assets", 18f);
        public static readonly Font FontBadge = new Font("Segoe UI", 7.5f, FontStyle.Bold);

        public const int Radius = 8;
        public const int Gap = 12;

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }
    }

    /// <summary>
    /// Painel de pagina, com buffer duplo.
    ///
    /// As paginas mudam de largura enquanto a barra lateral desliza, e a cada
    /// quadro os cartoes sao reposicionados. Sem o buffer, cada movimento pinta
    /// o fundo e so depois o cartao no lugar novo, e o intervalo entre as duas
    /// coisas aparece como tremor - dez vezes em 160 ms.
    /// </summary>
    public class Pagina : Panel
    {
        public Pagina()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
            BackColor = Ui.Window;
        }

        /// <summary>
        /// Fundo opaco, pintado aqui e nao herdado do hospedeiro.
        ///
        /// Com BackColor transparente o Windows Forms nao tem transparencia de
        /// verdade: antes de cada pintura ele manda o PAI desenhar o fundo dentro
        /// do filho. Como os cartoes tambem eram transparentes, cada cartao
        /// repintado custava tres desenhos - hospedeiro, pagina e cartao - e na
        /// animacao da lateral isso acontecia com todos eles, dez vezes.
        ///
        /// A cor sai do Ui na hora da pintura, e nao do BackColor guardado na
        /// construcao: assim uma troca de tema nao deixa a pagina com o fundo
        /// antigo.
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Ui.Window))
                e.Graphics.FillRectangle(b, e.ClipRectangle);
            base.OnPaint(e);
        }

        /// <summary>Largura da barra propria. Entra na conta de quem arranja.</summary>
        public const int LarguraDaBarra = 10;

        private ScrollFina _barra;
        private int _aplicado = 0;

        public bool Rolavel { get { return _barra != null; } }

        /// <summary>
        /// Liga a rolagem vertical, com barra PROPRIA.
        ///
        /// Sem AutoScroll de proposito. O AutoScroll traz as barras nativas, que
        /// nao aceitam tema: sao area nao-cliente, pintadas pelo Windows antes de
        /// qualquer coisa nossa, e saiam claras atravessando uma janela escura.
        /// Rolar na mao custa este punhado de linhas e devolve o controle da
        /// aparencia.
        /// </summary>
        public void RolarNaVertical()
        {
            if (_barra != null) return;

            AutoScroll = false;
            _barra = new ScrollFina();
            _barra.Dock = DockStyle.Right;
            _barra.Visible = false;
            _barra.ValorMudou += delegate { AplicarRolagem(); };
            Controls.Add(_barra);
        }

        /// <summary>
        /// Recalcula o alcance da barra e reaplica o deslocamento.
        ///
        /// Chamado depois de cada arranjo. Os arranjos posicionam como se nao
        /// houvesse rolagem - e assim tem de ser, senao cada um deles precisaria
        /// saber quanto a pagina esta rolada - entao e aqui que o deslocamento
        /// volta.
        /// </summary>
        public void Sincronizar()
        {
            if (_barra == null) return;

            // Os filhos acabaram de ser posicionados sem rolagem nenhuma.
            _aplicado = 0;

            int conteudo = 0;
            foreach (Control c in Controls)
            {
                if (c == _barra || !c.Visible) continue;
                if (c.Bottom > conteudo) conteudo = c.Bottom;
            }

            _barra.Definir(conteudo + 8, Height);
            _barra.Visible = _barra.Precisa;
            AplicarRolagem();
        }

        private void AplicarRolagem()
        {
            if (_barra == null) return;

            int delta = _aplicado - _barra.Valor;
            if (delta == 0) return;

            SuspendLayout();
            try
            {
                foreach (Control c in Controls)
                    if (c != _barra) c.Top += delta;
            }
            finally { ResumeLayout(); }

            _aplicado = _barra.Valor;
        }

        /// <summary>Rola por um empurrao da roda do mouse.</summary>
        public void RolarPor(int pixels)
        {
            if (_barra == null || !_barra.Precisa) return;
            _barra.Ajustar(_barra.Valor + pixels);
        }
    }

    /// <summary>
    /// Barra de rolagem vertical desenhada por nos.
    ///
    /// A nativa nao aceita o tema escuro. "DarkMode_Explorer" funciona em
    /// ListBox, ListView e TreeView - controles que o Explorer usa - mas nao nas
    /// barras de um painel com AutoScroll: aquelas sao area NAO-CLIENTE da
    /// janela, pintadas pelo Windows antes de qualquer coisa nossa. O resultado
    /// era um trilho claro atravessando a lateral direita de uma janela escura.
    ///
    /// Fina e sem setas. As setas de ponta somam 34 px de alvo que ninguem usa
    /// desde que a roda do mouse existe, e o trilho estreito devolve essa
    /// largura para o conteudo.
    /// </summary>
    public class ScrollFina : Control
    {
        public int Maximo = 0;      // altura total do conteudo
        public int Janela = 0;      // altura visivel
        public int Valor = 0;       // topo da janela dentro do conteudo

        public event EventHandler ValorMudou;

        private bool _arrastando = false;
        private int _pegouEm = 0;
        private bool _sobre = false;

        public ScrollFina()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = 10;
        }

        /// <summary>Ha o que rolar?</summary>
        public bool Precisa { get { return Maximo > Janela && Janela > 0; } }

        public void Definir(int maximo, int janela)
        {
            Maximo = maximo; Janela = janela;
            Ajustar(Valor);
            Invalidate();
        }

        /// <summary>Move mantendo o valor dentro do que existe para rolar.</summary>
        public void Ajustar(int novo)
        {
            int teto = Math.Max(0, Maximo - Janela);
            if (novo < 0) novo = 0;
            if (novo > teto) novo = teto;
            if (novo == Valor) return;

            Valor = novo;
            Invalidate();
            if (ValorMudou != null) ValorMudou(this, EventArgs.Empty);
        }

        /// <summary>Retangulo do polegar. Vazio quando nao ha o que rolar.</summary>
        private Rectangle Polegar()
        {
            if (!Precisa) return Rectangle.Empty;

            // Minimo de 28 px: proporcional puro, um conteudo de dez telas daria
            // um polegar de seis pixels, que nao da para pegar com o ponteiro.
            int h = Math.Max(28, (int)((long)Height * Janela / Maximo));
            int curso = Height - h;
            int teto = Maximo - Janela;
            int y = teto <= 0 ? 0 : (int)((long)curso * Valor / teto);
            return new Rectangle(2, y, Width - 4, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Parent != null ? Parent.BackColor : Ui.Window))
                g.FillRectangle(b, ClientRectangle);

            Rectangle p = Polegar();
            if (p.IsEmpty) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color cor = (_arrastando || _sobre) ? Ui.Muted : Ui.Thumb;
            using (GraphicsPath gp = Ui.RoundRect(p, (p.Width - 1) / 2))
            using (SolidBrush b = new SolidBrush(cor))
                g.FillPath(b, gp);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Rectangle p = Polegar();
            if (p.IsEmpty) { base.OnMouseDown(e); return; }

            if (p.Contains(e.Location))
            {
                _arrastando = true;
                _pegouEm = e.Y - p.Y;
            }
            else
            {
                // Clique no trilho salta uma tela, como manda o costume - e nao
                // pula direto para o ponto clicado, que perderia o contexto.
                Ajustar(Valor + (e.Y < p.Y ? -Janela : Janela));
            }
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_arrastando && Precisa)
            {
                int h = Polegar().Height;
                int curso = Height - h;
                int teto = Maximo - Janela;
                if (curso > 0) Ajustar((int)((long)(e.Y - _pegouEm) * teto / curso));
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _arrastando = false; Invalidate(); base.OnMouseUp(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _sobre = true; Invalidate(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _sobre = false; Invalidate(); base.OnMouseLeave(e);
        }
    }

    /// <summary>Painel com cantos arredondados e borda sutil.</summary>
    public class Card : Panel
    {
        public string Title = null;
        public int TitleHeight = 34;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // Opaco, e nao Color.Transparent.
            //
            // O cartao pinta cada pixel seu - os cantos com a cor da pagina, o
            // miolo com a da superficie - entao a transparencia nunca chegou a
            // aparecer. O que ela fazia era obrigar o PAI a redesenhar o fundo
            // dentro do cartao a cada pintura, e com o ResizeRedraw isso e uma
            // vez por quadro de animacao, por cartao.
            BackColor = Ui.Window;
            Font = Ui.FontBase;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            // Os cantos: o que fica FORA do retangulo arredondado. Antes vinha
            // da transparencia; agora e pintado aqui, com a cor lida do Ui na
            // hora para acompanhar troca de tema.
            using (SolidBrush b = new SolidBrush(Ui.Window))
                g.FillRectangle(b, e.ClipRectangle);

            Ui.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(r, Ui.Radius))
            using (SolidBrush b = new SolidBrush(Ui.Surface))
            using (Pen pen = new Pen(Ui.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            if (!string.IsNullOrEmpty(Title))
            {
                using (SolidBrush b = new SolidBrush(Ui.Text))
                    g.DrawString(Title, Ui.FontSection, b, 14, 10);
                using (Pen pen = new Pen(Ui.Border))
                    g.DrawLine(pen, 1, TitleHeight, Width - 2, TitleHeight);
            }
        }
    }

    /// <summary>Botao plano com estados de foco e realce opcional.</summary>
    public class FlatBtn : Control
    {
        public bool Primary = false;
        public bool Danger = false;

        /// <summary>
        /// Glifo da Segoe MDL2 usado quando o botao fica estreito demais para o
        /// texto - na barra lateral recolhida, por exemplo.
        ///
        /// ESCAPADO no chamador, sempre. Caractere da area de uso privado colado
        /// no fonte depende de sobreviver a toda ferramenta que passar pelo
        /// arquivo, e neste projeto ja nao sobreviveu quatro vezes.
        /// </summary>
        public string Glyph = null;

        private bool _hover, _down;

        public FlatBtn()
        {
            // SupportsTransparentBackColor e obrigatorio: sem ele, atribuir
            // Color.Transparent lanca "o controle nao da suporte a cores da
            // tela de fundo transparente".
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Ui.FontBase;
            Size = new Size(96, 32);
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            // O botão é desenhado à mão; alterar Text não repinta esse tipo de
            // controle automaticamente em todas as versões do WinForms. Sem
            // isto, o novo sensor só aparecia quando o hover forçava um paint.
            Invalidate();
            base.OnTextChanged(e);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _down = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true; e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill, fore, border;
            if (!Enabled)
            {
                // Antes o ramo Primary ignorava Enabled: o botao desligado
                // continuava azul solido, com o texto claro por cima. Parecia
                // ativo e ilegivel ao mesmo tempo.
                fill = Ui.SurfaceAlt;
                fore = Ui.Faint;
                border = Ui.Border;
            }
            else if (Primary)
            {
                fill = _down ? Ui.Accent : (_hover ? Ui.AccentHover : Ui.Accent);
                fore = Color.White;
                border = fill;
            }
            else if (Danger)
            {
                fill = _hover ? Color.FromArgb(60, Ui.Danger) : Color.Transparent;
                fore = Ui.Danger;
                border = Ui.Danger;
            }
            else
            {
                fill = _down ? Ui.SurfaceAlt : (_hover ? Ui.Hover : Ui.Surface);
                fore = Enabled ? Ui.Text : Ui.Muted;
                border = Ui.Border;
            }

            using (GraphicsPath p = Ui.RoundRect(r, 6))
            {
                if (fill != Color.Transparent)
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }

            // Estreito demais para o texto: vale o glifo, quando ha um. Sem ele,
            // o rotulo sairia como "Sa..." - tres pixels de informacao.
            bool cabe = TextRenderer.MeasureText(g, Text, Font).Width <= Width - 12;
            if (!cabe && !string.IsNullOrEmpty(Glyph))
            {
                TextRenderer.DrawText(g, Glyph, Ui.FontGlyph10, r, fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                DrawFocus(g, r, fore);
                return;
            }

            TextRenderer.DrawText(g, Text, Font, r, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            DrawFocus(g, r, fore);
        }

        private void DrawFocus(Graphics g, Rectangle r, Color fore)
        {
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(r, -4, -4), fore, Color.Transparent);
        }
    }

    /// <summary>Interruptor de duas posicoes, no lugar da caixa de selecao.</summary>
    public class Toggle : Control
    {
        private bool _on;
        private bool _hover;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _on; }
            set
            {
                if (_on == value) return;
                _on = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>Rotulo desenhado a direita do interruptor.</summary>
        public string Label = "";

        public Toggle()
        {
            // SupportsTransparentBackColor e obrigatorio: sem ele, atribuir
            // Color.Transparent lanca "o controle nao da suporte a cores da
            // tela de fundo transparente".
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Ui.FontBase;
            Size = new Size(240, 26);
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                e.Handled = true; e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);

            int h = 20, w = 38;
            int top = (Height - h) / 2;
            Rectangle track = new Rectangle(0, top, w, h);

            Color fill = _on ? (_hover ? Ui.AccentHover : Ui.Accent)
                             : (_hover ? Ui.Border : (Ui.Dark ? Ui.SurfaceAlt : Color.FromArgb(0xCF, 0xD3, 0xDA)));

            using (GraphicsPath p = Ui.RoundRect(track, h / 2))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);

            if (Focused && ShowFocusCues)
                using (GraphicsPath p = Ui.RoundRect(Rectangle.Inflate(track, 1, 1), h / 2 + 1))
                using (Pen pen = new Pen(Ui.Accent, 1.5f))
                    g.DrawPath(pen, p);

            int knob = h - 6;
            int kx = _on ? track.Right - knob - 3 : track.X + 3;
            using (SolidBrush b = new SolidBrush(Color.White))
                g.FillEllipse(b, kx, top + 3, knob, knob);

            if (!string.IsNullOrEmpty(Label))
            {
                Rectangle tr = new Rectangle(w + 10, 0, Width - w - 10, Height);
                TextRenderer.DrawText(g, Label, Font, tr, Ui.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>
    /// Seletor de opcoes mutuamente exclusivas, em pilulas lado a lado.
    ///
    /// Substitui o interruptor onde a escolha nao e "ligado/desligado" mas
    /// "isto OU aquilo": um interruptor rotulado "Fahrenheit" nao diz o que
    /// acontece quando esta desligado, enquanto "°C | °F" mostra as duas
    /// opcoes e qual esta valendo.
    /// </summary>
    public class Segmented : Control
    {
        private readonly System.Collections.Generic.List<string> _items = new System.Collections.Generic.List<string>();
        private int _sel = 0, _hover = -1;

        public event EventHandler SelectedIndexChanged;

        public Segmented()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Ui.FontBase;
            Size = new Size(160, 30);
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        public void SetItems(params string[] items) { _items.Clear(); _items.AddRange(items); Invalidate(); }

        public int SelectedIndex
        {
            get { return _sel; }
            set
            {
                int v = Math.Max(0, Math.Min(value, Math.Max(0, _items.Count - 1)));
                if (v == _sel) return;
                _sel = v;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        private int SegW { get { return _items.Count > 0 ? Width / _items.Count : Width; } }

        private int Hit(Point p)
        {
            if (_items.Count == 0 || p.Y < 0 || p.Y > Height) return -1;
            int i = p.X / Math.Max(1, SegW);
            return (i >= 0 && i < _items.Count) ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h >= 0) SelectedIndex = h;
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            {
                SelectedIndex += e.KeyCode == Keys.Left ? -1 : 1;
                e.Handled = true; e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            if (_items.Count == 0) return;

            Rectangle outer = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Height / 2;
            using (GraphicsPath p = Ui.RoundRect(outer, radius))
            using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt))
            using (Pen pen = new Pen(Focused && ShowFocusCues ? Ui.Accent : Ui.Border,
                                     Focused && ShowFocusCues ? 1.5f : 1f))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            int w = SegW;
            for (int i = 0; i < _items.Count; i++)
            {
                Rectangle seg = new Rectangle(i * w, 0, w, Height);
                bool sel = i == _sel;

                if (sel)
                {
                    // A pastilha acesa recua 3 px para que a borda externa
                    // continue visivel em volta dela.
                    Rectangle fill = new Rectangle(seg.X + 3, 3, seg.Width - 6, Height - 7);
                    using (GraphicsPath p = Ui.RoundRect(fill, fill.Height / 2))
                    using (SolidBrush b = new SolidBrush(Ui.Accent))
                        g.FillPath(b, p);
                }
                else if (i == _hover)
                {
                    Rectangle fill = new Rectangle(seg.X + 3, 3, seg.Width - 6, Height - 7);
                    using (GraphicsPath p = Ui.RoundRect(fill, fill.Height / 2))
                    using (SolidBrush b = new SolidBrush(Ui.Hover))
                        g.FillPath(b, p);
                }

                TextRenderer.DrawText(g, _items[i], sel ? Ui.FontMed : Ui.FontBase, seg,
                    sel ? Color.White : Ui.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>
    /// Campo numerico desenhado pelo kit.
    ///
    /// O NumericUpDown nativo entra branco numa janela escura e nao aceita
    /// borda arredondada. Aqui a moldura e nossa e so a area de digitacao e um
    /// TextBox.
    /// </summary>
    public class NumberBox : Control
    {
        private readonly TextBox _box;
        private bool _guard = false;
        private int _hover = 0;          // 0 nenhum, 1 menos, 2 mais

        public int Minimum = 0;
        public int Maximum = 999;
        public event EventHandler ValueChanged;

        public NumberBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);

            _box = new TextBox();
            _box.BorderStyle = BorderStyle.None;
            _box.Font = Ui.FontBase;
            _box.TextAlign = HorizontalAlignment.Center;
            _box.Text = "0";
            _box.TextChanged += delegate
            {
                if (_guard) return;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            };
            _box.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                // so digitos, Backspace e Delete
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
            };
            _box.LostFocus += delegate { Normalizar(); };
            Controls.Add(_box);

            BackColor = Color.Transparent;
            Font = Ui.FontBase;
            Size = new Size(120, 32);
        }

        public int Value
        {
            get
            {
                int v;
                if (!int.TryParse(_box.Text, out v)) return Minimum;
                return Math.Max(Minimum, Math.Min(Maximum, v));
            }
            set
            {
                int v = Math.Max(Minimum, Math.Min(Maximum, value));
                if (v == Value && _box.Text.Length > 0) return;
                _guard = true;
                _box.Text = v.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _guard = false;
                Invalidate();
            }
        }

        /// <summary>Devolve o campo a um numero valido depois da digitacao livre.</summary>
        private void Normalizar()
        {
            int v = Value;
            string t = v.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_box.Text != t) { _guard = true; _box.Text = t; _guard = false; Invalidate(); }
        }

        private int BtnW { get { return 30; } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_box == null) return;
            _box.SetBounds(BtnW, (Height - _box.PreferredHeight) / 2, Math.Max(10, Width - BtnW * 2), _box.PreferredHeight);
        }

        private int HitBtn(Point p)
        {
            if (p.X < BtnW) return 1;
            if (p.X > Width - BtnW) return 2;
            return 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitBtn(e.Location);
            if (h != _hover) { _hover = h; Cursor = h == 0 ? Cursors.IBeam : Cursors.Hand; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = 0; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = HitBtn(e.Location);
            if (h == 1) Value = Value - 1;
            else if (h == 2) Value = Value + 1;
            else _box.Focus();
            if (h != 0 && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Value = Value + (e.Delta > 0 ? 1 : -1);
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            _box.BackColor = Ui.SurfaceAlt;
            _box.ForeColor = Ui.Text;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(r, 8))
            using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt))
            using (Pen pen = new Pen(Ui.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            DrawSign(g, new Rectangle(0, 0, BtnW, Height), "−", _hover == 1, Value > Minimum);
            DrawSign(g, new Rectangle(Width - BtnW, 0, BtnW, Height), "+", _hover == 2, Value < Maximum);
        }

        private static void DrawSign(Graphics g, Rectangle area, string sign, bool hover, bool enabled)
        {
            if (hover && enabled)
                using (GraphicsPath p = Ui.RoundRect(new Rectangle(area.X + 3, 3, area.Width - 6, area.Height - 7), 6))
                using (SolidBrush b = new SolidBrush(Ui.Hover))
                    g.FillPath(b, p);

            TextRenderer.DrawText(g, sign, Ui.FontMed, area, enabled ? Ui.Text : Ui.Border,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Uma entrada da barra lateral.</summary>
    public class NavItem
    {
        public string Text;
        public string Glyph;     // caractere de Segoe MDL2 Assets
        public Control Page;
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
        }

        public void Add(NavItem item) { _items.Add(item); Invalidate(); }

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
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        private const int HeaderH = 116;
        private const int RowH = 40;

        public const int LarguraRecolhida = 56;
        private const int LarguraMinima = 210;
        private const int LarguraMaxima = 330;

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
                    42 + TextRenderer.MeasureText(it.Text, Ui.FontMed).Width + 16);

            precisa = Math.Max(precisa, 60 + TextRenderer.MeasureText(AppName, Ui.FontMed).Width + 46);
            if (!string.IsNullOrEmpty(Subtitle))
                precisa = Math.Max(precisa, 18 + TextRenderer.MeasureText(Subtitle, Ui.FontBase).Width + 18);

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

        private Timer _anim;
        private int _alvo = 0, _origem = 0;
        private DateTime _inicio;
        private const int DuracaoMs = 160;

        /// <summary>
        /// Desliza a largura ate o alvo.
        ///
        /// 160 ms com desaceleracao. Instantaneo, a coluna parece piscar e a
        /// pagina ao lado salta sem explicar de onde veio o espaco; muito mais
        /// lento, vira espera. Com a desaceleracao o movimento comeca rapido e
        /// assenta - e o que faz parecer que a barra tem peso em vez de teleporte.
        ///
        /// Sem animacao nenhuma se a janela ainda nao tem alca: antes disso
        /// mudar largura por temporizador nao pinta nada, so atrasa o arranque.
        /// </summary>
        private void Animar(int alvo)
        {
            if (!IsHandleCreated) { Width = alvo; Invalidate(); return; }

            _origem = Width; _alvo = alvo; _inicio = DateTime.UtcNow;

            if (_anim == null)
            {
                _anim = new Timer();
                _anim.Interval = 15;
                _anim.Tick += new EventHandler(OnAnim);
            }
            _anim.Start();
        }

        private void OnAnim(object sender, EventArgs e)
        {
            double t = (DateTime.UtcNow - _inicio).TotalMilliseconds / DuracaoMs;
            if (t >= 1)
            {
                _anim.Stop();
                Width = _alvo;
                _alvo = 0;
                Invalidate();
                return;
            }

            // desaceleracao cubica: rapido no comeco, assenta no fim
            double e3 = 1 - Math.Pow(1 - t, 3);
            Width = _origem + (int)Math.Round((_alvo - _origem) * e3);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _anim != null) { _anim.Stop(); _anim.Dispose(); _anim = null; }
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
        private bool Estreita { get { return Width < 150; } }

        private Rectangle _botao = Rectangle.Empty;
        private bool _sobreBotao = false;

        private int TopoDosItens { get { return Estreita ? 104 : HeaderH; } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool nb = _botao.Contains(e.Location);
            int h = nb ? -1 : HitTest(e.Location);
            if (h != _hover || nb != _sobreBotao)
            {
                _hover = h; _sobreBotao = nb;
                Cursor = (h >= 0 || nb) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1; _sobreBotao = false; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_botao.Contains(e.Location)) { Collapsed = !Collapsed; base.OnMouseDown(e); return; }
            int h = HitTest(e.Location);
            if (h >= 0) SelectedIndex = h;
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

            int y = TopoDosItens;
            for (int i = 0; i < _items.Count; i++)
            {
                NavItem it = _items[i];
                it.Bounds = new Rectangle(Estreita ? 6 : 8, y, Width - (Estreita ? 12 : 16), RowH);

                bool sel = (i == _selected);
                if (sel || i == _hover)
                {
                    using (GraphicsPath p = Ui.RoundRect(it.Bounds, 6))
                    using (SolidBrush b = new SolidBrush(sel ? Ui.AccentSoft : Ui.Hover))
                        g.FillPath(b, p);
                }
                if (sel)
                {
                    using (GraphicsPath p = Ui.RoundRect(new Rectangle(it.Bounds.X, it.Bounds.Y + 9, 3, RowH - 18), 2))
                    using (SolidBrush b = new SolidBrush(Ui.Accent))
                        g.FillPath(b, p);
                }

                Color fore = sel ? Ui.Text : Ui.Muted;
                if (!string.IsNullOrEmpty(it.Glyph))
                {
                    using (SolidBrush b = new SolidBrush(sel ? Ui.Accent : fore))
                    {
                        // Recolhida, o glifo e a unica coisa que resta do item:
                        // centraliza, senao fica encostado na esquerda da faixa.
                        if (Estreita)
                        {
                            SizeF t = g.MeasureString(it.Glyph, Ui.FontGlyph11);
                            g.DrawString(it.Glyph, Ui.FontGlyph11, b,
                                it.Bounds.X + (it.Bounds.Width - t.Width) / 2f, it.Bounds.Y + 11);
                        }
                        else g.DrawString(it.Glyph, Ui.FontGlyph11, b, it.Bounds.X + 14, it.Bounds.Y + 11);
                    }
                }
                if (!Estreita)
                    TextRenderer.DrawText(g, it.Text, sel ? Ui.FontMed : Ui.FontBase,
                        new Rectangle(it.Bounds.X + 42, it.Bounds.Y, it.Bounds.Width - 46, RowH), fore,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                y += RowH + 2;
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
            if (Estreita)
            {
                if (Logo != null) g.DrawImage(Logo, (Width - 30) / 2, 18, 30, 30);
                _botao = new Rectangle((Width - 30) / 2, 60, 30, 30);
            }
            else
            {
                if (Logo != null) g.DrawImage(Logo, 18, 18, 34, 34);

                using (SolidBrush b = new SolidBrush(Ui.Text))
                    g.DrawString(AppName, Ui.FontMed, b, 60, 26);

                // O perfil ocupa a largura toda, abaixo do titulo. TextRenderer
                // com retangulo delimitado, e nao DrawString: DrawString corta
                // onde o controle acaba, sem reticencias.
                int w = Width - 34;
                if (!string.IsNullOrEmpty(SubtitleCaption))
                    TextRenderer.DrawText(g, SubtitleCaption, Ui.FontSmall,
                        new Rectangle(18, 62, w, 14), Ui.Faint,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                if (!string.IsNullOrEmpty(Subtitle))
                    TextRenderer.DrawText(g, Subtitle, Ui.FontBase,
                        new Rectangle(18, 78, w, 18), Ui.Text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                _botao = new Rectangle(Width - 42, 20, 30, 30);
            }

            if (_sobreBotao)
            {
                using (GraphicsPath p = Ui.RoundRect(_botao, 6))
                using (SolidBrush b = new SolidBrush(Ui.Hover))
                    g.FillPath(b, p);
            }

            // E700 e o "hamburguer" da Segoe MDL2 - escapado, e nao colado como
            // caractere, porque area de uso privado nao sobrevive a toda
            // ferramenta que passa pelo arquivo.
            using (SolidBrush b = new SolidBrush(_sobreBotao ? Ui.Text : Ui.Muted))
            {
                SizeF t = g.MeasureString("", Ui.FontGlyph11);
                g.DrawString("", Ui.FontGlyph11, b,
                    _botao.X + (_botao.Width - t.Width) / 2f,
                    _botao.Y + (_botao.Height - t.Height) / 2f);
            }
        }

    }
}
