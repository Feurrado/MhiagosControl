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
        /// <summary>
        /// Escala extra so para a captura de telas dos testes, que simula 150%
        /// e 200% numa maquina a 100%: as fontes crescem como o Windows faria e
        /// S() cresce junto. Fora dos testes vale sempre 1. Declarada antes das
        /// fontes porque a ordem dos campos estaticos e a ordem do arquivo.
        /// </summary>
        private static readonly float _zoom = LerZoom();

        private static float LerZoom()
        {
            try
            {
                object z = AppDomain.CurrentDomain.GetData("MhiagosControl.Zoom");
                return z is float && (float)z >= 1f ? (float)z : 1f;
            }
            catch { return 1f; }
        }

        internal static float Pt(float pontos) { return pontos * _zoom; }

        public static bool Dark { get { return Theme.IsDark; } }

        /// <summary>
        /// As cores que mudam com o tema, uma linha por papel: escuro, claro.
        ///
        /// Numa tabela, e nao em cada propriedade, para a troca de tema poder
        /// perguntar "esta cor era qual papel no tema anterior?" e trazer o
        /// mesmo papel no novo. Rotulos montados com a cor fixa na construcao
        /// ficavam no tema velho - texto quase branco sobre fundo claro.
        /// </summary>
        private enum Papel { Window, Sidebar, Surface, SurfaceAlt, Border, Text, Muted, Faint, Thumb, AccentSoft, Hover, AccentText }

        private static readonly int[][] Tons =
        {
            new int[] { 0x1C1C1F, 0xF4F5F7 },   // Window
            new int[] { 0x17171A, 0xEAECF0 },   // Sidebar
            new int[] { 0x26262A, 0xFFFFFF },   // Surface
            new int[] { 0x2E2E33, 0xF7F8FA },   // SurfaceAlt
            new int[] { 0x38383E, 0xDADDE3 },   // Border
            new int[] { 0xF0F0F3, 0x1B1D21 },   // Text
            // Muted e Faint claros escureceram um degrau: o Faint antigo
            // (7C828D) dava 3,5:1 sobre o fundo da janela, abaixo dos 4,5:1
            // que texto pequeno pede, e ele aparece em eixo, nota e nome de
            // aparelho. Os dois descem juntos para a hierarquia continuar.
            new int[] { 0x9699A3, 0x5F646E },   // Muted
            new int[] { 0x8A8E99, 0x6B707B },   // Faint
            new int[] { 0x555862, 0xB4BAC4 },   // Thumb
            new int[] { 0x1E3352, 0xDEEAFF },   // AccentSoft
            new int[] { 0x323238, 0xECEFF4 },   // Hover
            new int[] { 0x6AA6FF, 0x1F6AE0 },   // AccentText
        };

        private static Color De(Papel p, bool escuro)
        {
            return Color.FromArgb(unchecked((int)0xFF000000) | Tons[(int)p][escuro ? 0 : 1]);
        }

        /// <summary>
        /// A mesma cor no outro tema, quando ela e um papel da paleta; se nao
        /// for, volta igual. Para recolorir o que foi montado no tema anterior.
        /// </summary>
        internal static Color NoOutroTema(Color cor, bool eraEscuro)
        {
            int argb = cor.ToArgb();
            for (int i = 0; i < Tons.Length; i++)
                if (De((Papel)i, eraEscuro).ToArgb() == argb) return De((Papel)i, !eraEscuro);
            return cor;
        }

        // superficies
        public static Color Window { get { return De(Papel.Window, Dark); } }
        public static Color Sidebar { get { return De(Papel.Sidebar, Dark); } }
        public static Color Surface { get { return De(Papel.Surface, Dark); } }
        public static Color SurfaceAlt { get { return De(Papel.SurfaceAlt, Dark); } }
        public static Color Border { get { return De(Papel.Border, Dark); } }

        // texto
        public static Color Text { get { return De(Papel.Text, Dark); } }
        public static Color Muted { get { return De(Papel.Muted, Dark); } }

        /// <summary>
        /// Um degrau abaixo de Muted, para nota de rodape sem sumir.
        ///
        /// Estava em 6C6F79 no tema escuro - perto demais do fundo. Somado ao
        /// corpo de 8,25 pt, o resultado nao lia como "secundario", lia como
        /// apagado. Hierarquia se faz com contraste suficiente para SER LIDO,
        /// e nao com texto no limite de desaparecer.
        /// </summary>
        public static Color Faint { get { return De(Papel.Faint, Dark); } }

        /// <summary>Polegar da barra de rolagem. Precisa se destacar do trilho
        /// sem competir com o texto: Border era escuro demais e sumia nele.</summary>
        public static Color Thumb { get { return De(Papel.Thumb, Dark); } }

        // enfase - azul do icone do aplicativo
        public static Color Accent { get { return C(0x2D, 0x7D, 0xF6); } }
        public static Color AccentHover { get { return C(0x4A, 0x91, 0xFF); } }
        public static Color AccentSoft { get { return De(Papel.AccentSoft, Dark); } }

        /// <summary>
        /// O azul quando ele E o texto: link, aviso do rodape. O Accent como
        /// letra dava 3,9:1 nos dois temas; este passa de 4,5:1 sobre a
        /// superficie e sobre a janela. Preenchimento continua no Accent.
        /// </summary>
        public static Color AccentText { get { return De(Papel.AccentText, Dark); } }

        // estados
        public static Color Warn { get { return C(0xF0, 0xAF, 0x3C); } }
        public static Color Danger { get { return C(0xEB, 0x5A, 0x4B); } }
        public static Color Hover { get { return De(Papel.Hover, Dark); } }

        public static readonly Font FontBase = new Font("Segoe UI", Pt(9f));
        public static readonly Font FontMed = new Font("Segoe UI", Pt(9f), FontStyle.Bold);
        public static readonly Font FontTitle = new Font("Segoe UI", Pt(14f), FontStyle.Regular);
        public static readonly Font FontSection = new Font("Segoe UI", Pt(10.5f), FontStyle.Bold);
        /// <summary>
        /// O corpo pequeno, usado em rotulo de apoio e nota.
        ///
        /// Era 8,25 pt. Nesse tamanho, em cinza, sobre fundo escuro, o texto nao
        /// e discreto - e fino: os tracos da Segoe UI ficam com menos de um pixel
        /// cheio e o antisserrilhado apaga o que sobra. Um ponto a mais devolve
        /// peso sem tirar a hierarquia, que continua vindo da COR.
        /// </summary>
        public static readonly Font FontSmall = new Font("Segoe UI", Pt(9f));

        /// <summary>
        /// Seminegrito, para o valor que precisa ter presenca sem virar titulo.
        ///
        /// "Segoe UI Semibold" e uma familia propria no Windows, e nao um estilo
        /// da "Segoe UI": pedir FontStyle.Bold daria o negrito cheio, que num
        /// rotulo de tres palavras pesa demais.
        /// </summary>
        public static readonly Font FontSemi = Semi(Pt(9f));
        /// <summary>Valor dos blocos de resumo da pagina Baterias.</summary>
        public static readonly Font FontTileValue = Semi(Pt(11f));

        /// <summary>O nome do aplicativo no alto do Sobre.</summary>
        public static readonly Font FontAppName = Semi(Pt(18f));
        /// <summary>A porcentagem grande do cartao de bateria.</summary>
        public static readonly Font FontBigPct = new Font("Segoe UI", Pt(20f), FontStyle.Bold);

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
        public static readonly Font FontValue = new Font("Segoe UI", Pt(16f), FontStyle.Bold);
        public static readonly Font FontGlyph8 = new Font("Segoe MDL2 Assets", Pt(8f));
        public static readonly Font FontGlyph9 = new Font("Segoe MDL2 Assets", Pt(9f));
        public static readonly Font FontGlyph10 = new Font("Segoe MDL2 Assets", Pt(10f));
        public static readonly Font FontGlyph11 = new Font("Segoe MDL2 Assets", Pt(11f));
        public static readonly Font FontGlyph18 = new Font("Segoe MDL2 Assets", Pt(18f));
        public static readonly Font FontBadge = new Font("Segoe UI", Pt(7.5f), FontStyle.Bold);


        /// <summary>
        /// Escala da tela: 1 a 100%, 1,5 a 150%.
        ///
        /// As fontes sao em pontos e ja crescem com o DPI; as medidas em pixel
        /// do layout nao cresciam. A 150% o titulo de 10,5 pt de um cartao
        /// passava da linha desenhada em 34 px, e um rotulo de 18 px cortava
        /// a base do texto de 9 pt. Medida de layout que convive com texto
        /// passa por S().
        /// </summary>
        public static readonly float Escala = LerEscala();

        private static float LerEscala()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    return Math.Max(1f, g.DpiX / 96f) * _zoom;
            }
            catch { return 1f; }
        }

        /// <summary>
        /// Pixels de projeto (a 96 dpi) na escala da tela.
        /// Arredonda para cima: com 150% um rotulo de 15 px daria 22,5, e o
        /// arredondamento comum (22) cortava a perna do "g" de 13,5 pt.
        /// </summary>
        public static int S(int px) { return (int)Math.Ceiling(px * Escala - 0.001f); }

        // Depois de Escala: campo estatico se inicializa na ordem do arquivo.
        public static readonly int Radius = Ui.S(8);
        public static readonly int Gap = Ui.S(12);

        /// <summary>
        /// Animacoes ligadas no Windows (Acessibilidade > Efeitos visuais).
        /// Desligadas, tudo que desliza ou pulsa vai direto ao estado final.
        /// </summary>
        public static bool Animacoes
        {
            get
            {
                try { return SystemInformation.UIEffectsEnabled; }
                catch { return true; }
            }
        }

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        /// <summary>
        /// Pilha deitada em traco fino, no peso dos glifos da Segoe MDL2: o
        /// E83F da fonte, todo cheio, virava um bloco que nao lia como bateria.
        /// </summary>
        public static void IconeDeBateria(Graphics g, RectangleF em, Color cor)
        {
            SmoothingMode antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                float st = Math.Max(1.2f, em.Width * 0.085f);
                float h = em.Height * 0.56f, polo = em.Width * 0.1f;
                float w = em.Width - polo - st;
                RectangleF corpo = new RectangleF(em.X + st / 2f, em.Y + (em.Height - h) / 2f, w, h);
                using (GraphicsPath p = Arredondar(corpo, h * 0.22f))
                using (Pen pen = new Pen(cor, st))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, p);
                }
                RectangleF bico = new RectangleF(corpo.Right + st * 0.5f, corpo.Y + h * 0.3f, polo, h * 0.4f);
                using (GraphicsPath p = Arredondar(bico, polo * 0.45f))
                using (SolidBrush b = new SolidBrush(cor))
                    g.FillPath(b, p);
                // nivel em dois tercos, com folga do contorno, para ler como carga
                float folga = st * 1.4f;
                RectangleF dentro = RectangleF.Inflate(corpo, -folga, -folga);
                RectangleF nivel = new RectangleF(dentro.X, dentro.Y, dentro.Width * 0.66f, dentro.Height);
                using (GraphicsPath p = Arredondar(nivel, Math.Min(nivel.Height, nivel.Width) * 0.2f))
                using (SolidBrush b = new SolidBrush(cor))
                    g.FillPath(b, p);
            }
            finally { g.SmoothingMode = antes; }
        }

        private static GraphicsPath Arredondar(RectangleF r, float raio)
        {
            float d = Math.Min(raio * 2, Math.Min(r.Width, r.Height));
            GraphicsPath p = new GraphicsPath();
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

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

            using (GraphicsPath p = Ui.RoundRect(r, Ui.S(6)))
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
            Size = new Size(Ui.S(240), Ui.S(26));
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

            int h = Ui.S(20), w = Ui.S(38);
            int top = (Height - h) / 2;
            Rectangle track = new Rectangle(0, top, w, h);

            bool hover = _hover && Enabled;
            Color fill = _on ? (hover ? Ui.AccentHover : Ui.Accent)
                             : (hover ? Ui.Border : (Ui.Dark ? Ui.SurfaceAlt : Color.FromArgb(0xCF, 0xD3, 0xDA)));
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
                using (Pen pen = new Pen(Ui.Accent, 1.5f))
                    g.DrawPath(pen, p);

            int folga = Ui.S(3);
            int knob = h - 2 * folga;
            int kx = _on ? track.Right - knob - folga : track.X + folga;
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
            Size = new Size(Ui.S(160), Ui.S(30));
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
            using (GraphicsPath p = Ui.RoundRect(r, Ui.S(8)))
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

        private static void DrawSign(Graphics g, Rectangle area, string sign, bool hover, bool enabled)
        {
            if (hover && enabled)
                using (GraphicsPath p = Ui.RoundRect(new Rectangle(area.X + Ui.S(3), Ui.S(3), area.Width - Ui.S(6), area.Height - Ui.S(7)), Ui.S(6)))
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
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
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
            // Sem handle, ou com as animacoes do Windows desligadas: direto ao fim.
            if (!IsHandleCreated || !Ui.Animacoes)
            {
                if (_anim != null) _anim.Stop();
                Width = alvo; _alvo = 0; Invalidate();
                return;
            }

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
        private bool Estreita { get { return Width < Ui.S(150); } }

        private Rectangle _botao = Rectangle.Empty;
        private bool _sobreBotao = false;

        private int TopoDosItens { get { return Estreita ? Ui.S(104) : HeaderH; } }

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

            int y = TopoDosItens;
            for (int i = 0; i < _items.Count; i++)
            {
                NavItem it = _items[i];
                it.Bounds = new Rectangle(Ui.S(Estreita ? 6 : 8), y, Width - Ui.S(Estreita ? 12 : 16), RowH);

                bool sel = (i == _selected);
                if (sel || i == _hover)
                {
                    using (GraphicsPath p = Ui.RoundRect(it.Bounds, Ui.S(6)))
                    using (SolidBrush b = new SolidBrush(sel ? Ui.AccentSoft : Ui.Hover))
                        g.FillPath(b, p);
                }
                if (sel)
                {
                    using (GraphicsPath p = Ui.RoundRect(new Rectangle(it.Bounds.X, it.Bounds.Y + Ui.S(9), Ui.S(3), RowH - Ui.S(18)), Ui.S(2)))
                    using (SolidBrush b = new SolidBrush(Ui.Accent))
                        g.FillPath(b, p);
                }

                Color fore = sel ? Ui.Text : Ui.Muted;
                if (it.Icone != null)
                {
                    // o mesmo quadrado que um glifo de 11 pt ocuparia ali
                    float em = Ui.FontGlyph11.SizeInPoints * g.DpiY / 72f;
                    float gx = Estreita ? it.Bounds.X + (it.Bounds.Width - em) / 2f : it.Bounds.X + Ui.S(14) + em / 6f;
                    RectangleF cel = new RectangleF(gx, it.Bounds.Y + (RowH - em) / 2f, em, em);
                    it.Icone(g, cel, sel ? Ui.Accent : fore);
                }
                else if (!string.IsNullOrEmpty(it.Glyph))
                {
                    using (SolidBrush b = new SolidBrush(sel ? Ui.Accent : fore))
                    {
                        // Recolhida, o glifo e a unica coisa que resta do item:
                        // centraliza, senao fica encostado na esquerda da faixa.
                        if (Estreita)
                        {
                            SizeF t = g.MeasureString(it.Glyph, Ui.FontGlyph11);
                            g.DrawString(it.Glyph, Ui.FontGlyph11, b,
                                it.Bounds.X + (it.Bounds.Width - t.Width) / 2f, it.Bounds.Y + Ui.S(11));
                        }
                        else g.DrawString(it.Glyph, Ui.FontGlyph11, b, it.Bounds.X + Ui.S(14), it.Bounds.Y + Ui.S(11));
                    }
                }
                if (!Estreita)
                    TextRenderer.DrawText(g, it.Text, sel ? Ui.FontMed : Ui.FontBase,
                        new Rectangle(it.Bounds.X + Ui.S(42), it.Bounds.Y, it.Bounds.Width - Ui.S(46), RowH), fore,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

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
            if (Estreita)
            {
                if (Logo != null) g.DrawImage(Logo, (Width - Ui.S(30)) / 2, Ui.S(18), Ui.S(30), Ui.S(30));
                _botao = new Rectangle((Width - Ui.S(30)) / 2, Ui.S(60), Ui.S(30), Ui.S(30));
            }
            else
            {
                if (Logo != null) g.DrawImage(Logo, Ui.S(18), Ui.S(18), Ui.S(34), Ui.S(34));

                using (SolidBrush b = new SolidBrush(Ui.Text))
                    g.DrawString(AppName, Ui.FontMed, b, Ui.S(60), Ui.S(26));

                // O perfil ocupa a largura toda, abaixo do titulo. TextRenderer
                // com retangulo delimitado, e nao DrawString: DrawString corta
                // onde o controle acaba, sem reticencias.
                int w = Width - Ui.S(34);
                if (!string.IsNullOrEmpty(SubtitleCaption))
                    TextRenderer.DrawText(g, SubtitleCaption, Ui.FontSmall,
                        new Rectangle(Ui.S(18), Ui.S(62), w, Ui.S(14)), Ui.Faint,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                if (!string.IsNullOrEmpty(Subtitle))
                    TextRenderer.DrawText(g, Subtitle, Ui.FontBase,
                        new Rectangle(Ui.S(18), Ui.S(78), w, Ui.S(18)), Ui.Text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                _botao = new Rectangle(Width - Ui.S(42), Ui.S(20), Ui.S(30), Ui.S(30));
            }

            if (_sobreBotao)
            {
                using (GraphicsPath p = Ui.RoundRect(_botao, Ui.S(6)))
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
