using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
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
        public static readonly int LarguraDaBarra = Ui.S(10);

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

            _barra.Definir(conteudo + Ui.S(8), Height);
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
            Width = Pagina.LarguraDaBarra;
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
            int h = Math.Max(Ui.S(28), (int)((long)Height * Janela / Maximo));
            int curso = Height - h;
            int teto = Maximo - Janela;
            int y = teto <= 0 ? 0 : (int)((long)curso * Valor / teto);
            return new Rectangle(Ui.S(2), y, Width - Ui.S(4), h);
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
        public int TitleHeight = Ui.S(34);

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
                    g.DrawString(Title, Ui.FontSection, b, Ui.S(14), Ui.S(10));
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

        private Transicao _tHover, _tDown;
        private Transicao TH { get { return _tHover ?? (_tHover = new Transicao(this)); } }
        private Transicao TD { get { return _tDown ?? (_tDown = new Transicao(this)); } }


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
            Size = new Size(Ui.S(96), Ui.S(32));
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

        protected override void OnMouseEnter(EventArgs e) { TH.Ir(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { TH.Ir(0f); TD.Ir(0f); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { TD.Ir(1f); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { TD.Ir(0f); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { TD.Ir(0f); base.OnLostFocus(e); }
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
                fill = Ui.Misturar(Ui.Misturar(Ui.Accent, Ui.AccentHover, TH.Valor), Ui.Misturar(Ui.Accent, Color.Black, 0.18f), TD.Valor);
                fore = Color.White;
                border = fill;
            }
            else if (Danger)
            {
                fill = Ui.Misturar(Ui.Misturar(Color.FromArgb(0, Ui.Danger), Color.FromArgb(60, Ui.Danger), TH.Valor), Color.FromArgb(90, Ui.Danger), TD.Valor);
                fore = Ui.Danger;
                border = Ui.Danger;
            }
            else
            {
                fill = Ui.Misturar(Ui.Misturar(Ui.Surface, Ui.Hover, TH.Valor), Ui.SurfaceAlt, TD.Valor);
                fore = Enabled ? Ui.Text : Ui.Muted;
                border = Ui.Border;
            }

            using (GraphicsPath p = Ui.RoundRect(r, Ui.RaioControle))
            {
                if (fill != Color.Transparent)
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }

            // Estreito demais para o texto: vale o glifo, quando ha um. Sem ele,
            // o rotulo sairia como "Sa..." - tres pixels de informacao.
            bool cabe = TextRenderer.MeasureText(g, Text, Font).Width <= Width - Ui.S(12);
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

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acessivel(this); }

        /// <summary>Botao para o leitor de tela, acionavel pela acao padrao.</summary>
        private sealed class Acessivel : ControlAccessibleObject
        {
            private readonly FlatBtn _dono;
            public Acessivel(FlatBtn dono) : base(dono) { _dono = dono; }
            public override string Name { get { return _dono.AccessibleName ?? _dono.Text; } }
            public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
            public override void DoDefaultAction() { if (_dono.Enabled) _dono.OnClick(EventArgs.Empty); }
        }

        private void DrawFocus(Graphics g, Rectangle r, Color fore)
        {
            if (Focused && ShowFocusCues)
                Ui.AnelDeFoco(g, new Rectangle(0, 0, Width, Height), Ui.RaioControle);
        }
    }

    /// <summary>Interruptor de duas posicoes, no lugar da caixa de selecao.</summary>
    public class Toggle : Control
    {
        private bool _on;
        private Transicao _tHover, _tPos;
        private Transicao TH { get { return _tHover ?? (_tHover = new Transicao(this)); } }
        private Transicao TP { get { return _tPos ?? (_tPos = new Transicao(this, Motion.Media)); } }
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _on; }
            set
            {
                if (_on == value) return;
                _on = value;
                TP.Ir(value ? 1f : 0f);
                Invalidate();
                if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
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
            Size = new Size(Ui.S(240), Ui.S(32));
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        protected override void OnMouseEnter(EventArgs e) { TH.Ir(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { TH.Ir(0f); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Enter tambem: e o que se tenta primeiro num interruptor.
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                Checked = !Checked;
                e.Handled = true; e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acessivel(this); }

        /// <summary>Caixa de selecao para o leitor de tela: o rotulo e se esta ligado.</summary>
        private sealed class Acessivel : ControlAccessibleObject
        {
            private readonly Toggle _dono;
            public Acessivel(Toggle dono) : base(dono) { _dono = dono; }
            public override string Name { get { return _dono.AccessibleName ?? _dono.Label; } }
            public override AccessibleRole Role { get { return AccessibleRole.CheckButton; } }
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates s = base.State & ~AccessibleStates.Checked;
                    return _dono.Checked ? s | AccessibleStates.Checked : s;
                }
            }
            public override void DoDefaultAction() { if (_dono.Enabled) _dono.Checked = !_dono.Checked; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);

            int h = Ui.S(20), w = Ui.S(38);
            int top = (Height - h) / 2;
            Rectangle track = new Rectangle(0, top, w, h);

            Color desl = Ui.Misturar(Ui.Dark ? Ui.SurfaceAlt : Color.FromArgb(0xCF, 0xD3, 0xDA), Ui.Border, Enabled ? TH.Valor : 0f);
            Color lig = Ui.Misturar(Ui.Accent, Ui.AccentHover, Enabled ? TH.Valor : 0f);
            Color fill = Ui.Misturar(desl, lig, TP.Valor);
            // Desabilitado parecia igual ao habilitado: o interruptor de perfil
            // por jogo, sem RTSS, era clicavel a vista e nao respondia.
            if (!Enabled) fill = _on ? Color.FromArgb(110, Ui.Accent) : Ui.SurfaceAlt;
            Color texto = Enabled ? Ui.Text : Ui.Faint;

            using (GraphicsPath p = Ui.RoundRect(track, h / 2))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);
            if (!Enabled)
                using (GraphicsPath p = Ui.RoundRect(track, h / 2))
                using (Pen pen = new Pen(Ui.Border))
                    g.DrawPath(pen, p);

            if (Focused && ShowFocusCues)
                using (GraphicsPath p = Ui.RoundRect(Rectangle.Inflate(track, 1, 1), h / 2 + 1))
                using (Pen pen = new Pen(Ui.Accent, 2f))
                    g.DrawPath(pen, p);

            int folga = Ui.S(3);
            int knob = h - 2 * folga;
            int kOff = track.X + folga, kOn = track.Right - knob - folga;
            int kx = kOff + (int)Math.Round((kOn - kOff) * TP.Valor);
            using (SolidBrush b = new SolidBrush(Enabled ? Color.White : (Ui.Dark ? Ui.Faint : Color.FromArgb(0xF0, 0xF1, 0xF4))))
                g.FillEllipse(b, kx, top + folga, knob, knob);

            if (!string.IsNullOrEmpty(Label))
            {
                Rectangle tr = new Rectangle(w + Ui.S(10), 0, Width - w - Ui.S(10), Height);
                if (Height >= Ui.S(36))
                {
                    // Alto o bastante para duas linhas: quebra em vez de cortar
                    // com reticencias, que num cartao estreito comia o fim da frase.
                    Size t = TextRenderer.MeasureText(g, Label, Font, new Size(tr.Width, 0), TextFormatFlags.WordBreak);
                    tr = new Rectangle(tr.X, Math.Max(0, (Height - t.Height) / 2), tr.Width, Math.Min(Height, t.Height));
                    TextRenderer.DrawText(g, Label, Font, tr, texto, TextFormatFlags.Left | TextFormatFlags.WordBreak);
                }
                else
                    TextRenderer.DrawText(g, Label, Font, tr, texto,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
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
            Size = new Size(Ui.S(160), Ui.S(32));
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
                if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
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
                                     Focused && ShowFocusCues ? 2f : 1f))
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
                    Rectangle fill = new Rectangle(seg.X + Ui.S(3), Ui.S(3), seg.Width - Ui.S(6), Height - Ui.S(7));
                    using (GraphicsPath p = Ui.RoundRect(fill, fill.Height / 2))
                    using (SolidBrush b = new SolidBrush(Enabled ? Ui.Accent : Ui.Border))
                        g.FillPath(b, p);
                }
                else if (i == _hover && Enabled)
                {
                    Rectangle fill = new Rectangle(seg.X + Ui.S(3), Ui.S(3), seg.Width - Ui.S(6), Height - Ui.S(7));
                    using (GraphicsPath p = Ui.RoundRect(fill, fill.Height / 2))
                    using (SolidBrush b = new SolidBrush(Ui.Hover))
                        g.FillPath(b, p);
                }

                Color cor = !Enabled ? Ui.Faint : (sel ? Color.White : Ui.Text);
                TextRenderer.DrawText(g, _items[i], sel ? Ui.FontMed : Ui.FontBase, seg, cor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            if (!Enabled) _hover = -1;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acessivel(this); }

        /// <summary>Lista de opcoes para o leitor de tela; o valor e a opcao escolhida.</summary>
        private sealed class Acessivel : ControlAccessibleObject
        {
            private readonly Segmented _dono;
            public Acessivel(Segmented dono) : base(dono) { _dono = dono; }
            public override AccessibleRole Role { get { return AccessibleRole.List; } }
            public override string Value
            {
                get { return _dono._sel >= 0 && _dono._sel < _dono._items.Count ? _dono._items[_dono._sel] : ""; }
                set { }
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
            Size = new Size(Ui.S(120), Ui.S(32));
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

        private int BtnW { get { return Ui.S(30); } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_box == null) return;
            _box.SetBounds(BtnW, (Height - _box.PreferredHeight) / 2, Math.Max(Ui.S(10), Width - BtnW * 2), _box.PreferredHeight);
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
            _box.ForeColor = Enabled ? Ui.Text : Ui.Faint;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(r, Ui.RaioCartao))
            using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt))
            using (Pen pen = new Pen(Ui.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            DrawSign(g, new Rectangle(0, 0, BtnW, Height), "−", _hover == 1, Enabled && Value > Minimum);
            DrawSign(g, new Rectangle(Width - BtnW, 0, BtnW, Height), "+", _hover == 2, Enabled && Value < Maximum);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            if (!Enabled) _hover = 0;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acessivel(this); }

        /// <summary>Campo numerico com setas para o leitor de tela.</summary>
        private sealed class Acessivel : ControlAccessibleObject
        {
            private readonly NumberBox _dono;
            public Acessivel(NumberBox dono) : base(dono) { _dono = dono; }
            public override AccessibleRole Role { get { return AccessibleRole.SpinButton; } }
            public override string Value
            {
                get { return _dono.Value.ToString(System.Globalization.CultureInfo.CurrentCulture); }
                set { int v; if (int.TryParse(value, out v)) _dono.Value = v; }
            }
        }

        private static void DrawSign(Graphics g, Rectangle area, string sign, bool hover, bool enabled)
        {
            if (hover && enabled)
                using (GraphicsPath p = Ui.RoundRect(new Rectangle(area.X + Ui.S(3), Ui.S(3), area.Width - Ui.S(6), area.Height - Ui.S(7)), Ui.RaioControle))
                using (SolidBrush b = new SolidBrush(Ui.Hover))
                    g.FillPath(b, p);

            TextRenderer.DrawText(g, sign, Ui.FontMed, area, enabled ? Ui.Text : Ui.Border,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
