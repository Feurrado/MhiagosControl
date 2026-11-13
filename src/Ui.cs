using System;
using System.Collections.Generic;
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
            float teste = 1f;
            try
            {
                object z = AppDomain.CurrentDomain.GetData("MhiagosControl.Zoom");
                if (z is float && (float)z >= 1f) teste = (float)z;
            }
            catch { }
            return teste * LerTamanhoDoTexto();
        }

        /// <summary>
        /// Configuracoes > Acessibilidade > Tamanho do texto (100% a 225%).
        /// O Windows guarda em HKCU\Software\Microsoft\Accessibility. A rampa
        /// tipografica e as medidas S() crescem juntas, como num DPI maior,
        /// para o texto maior nao cortar nem sair do cartao. Vale na proxima
        /// abertura do aplicativo: as fontes sao criadas uma vez.
        /// </summary>
        private static float LerTamanhoDoTexto()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(@"Software\Microsoft\Accessibility"))
                {
                    object v = k == null ? null : k.GetValue("TextScaleFactor");
                    return v is int ? FatorDoTexto((int)v) : 1f;
                }
            }
            catch { return 1f; }
        }

        /// <summary>Porcentagem do Windows para fator: limita a 100% a 225%, a faixa que as Configuracoes oferecem.</summary>
        internal static float FatorDoTexto(int porcentagem)
        {
            if (porcentagem < 100) porcentagem = 100;
            if (porcentagem > 225) porcentagem = 225;
            return porcentagem / 100f;
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

        // ---- tipografia ----
        // Rampa nomeada: os tamanhos moram aqui, e cada Font* so escolhe um
        // degrau. Familias com reserva: a Segoe UI Variable (Windows 11) se
        // houver, senao a Segoe UI; os icones pela Segoe Fluent Icons, senao a
        // Segoe MDL2 Assets (os pontos de codigo usados sao os mesmos).
        internal static class Tipo
        {
            public const float Legenda = 9f;     // nota, rotulo de apoio
            public const float Corpo = 10f;      // texto corrente
            public const float Subtitulo = 11f;  // secao, valor de bloco
            public const float Titulo = 14f;
            public const float Destaque = 16f;   // leitura em destaque
            public const float Nome = 18f;       // nome do aplicativo
            public const float Numero = 20f;     // porcentagem grande
            public const float MinimoDoSelo = 9f;

            public static readonly string Texto = Achar(new[] { "Segoe UI Variable Text", "Segoe UI" }, "Segoe UI");
            public static readonly string Icones = Achar(new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }, "Segoe MDL2 Assets");
            /// <summary>Corte optico: Small para legenda de 9 pt ou menos, Display de 16 pt para cima, Text no resto.</summary>
            public static readonly string Pequena = Achar(new[] { "Segoe UI Variable Small" }, Texto);
            public static readonly string Exibicao = Achar(new[] { "Segoe UI Variable Display" }, Texto);
            /// <summary>Valores ao vivo: a Segoe UI classica tem algarismos de largura fixa no GDI; o numero nao dança quando muda.</summary>
            public static readonly string Numeros = Achar(new[] { "Segoe UI" }, Texto);

            internal static string Achar(string[] candidatas, string reserva)
            {
                try
                {
                    foreach (string c in candidatas)
                        foreach (FontFamily f in FontFamily.Families)
                            if (string.Equals(f.Name, c, StringComparison.OrdinalIgnoreCase)) return c;
                }
                catch { }
                return reserva;
            }
        }

        public static readonly Font FontBase = new Font(Tipo.Texto, Pt(Tipo.Corpo));
        public static readonly Font FontMed = Semi(Pt(Tipo.Corpo));
        public static readonly Font FontTitle = new Font(Tipo.Texto, Pt(Tipo.Titulo), FontStyle.Regular);
        public static readonly Font FontSection = Semi(Pt(Tipo.Subtitulo));
        /// <summary>
        /// O corpo pequeno, usado em rotulo de apoio e nota.
        ///
        /// Era 8,25 pt. Nesse tamanho, em cinza, sobre fundo escuro, o texto nao
        /// e discreto - e fino: os tracos da Segoe UI ficam com menos de um pixel
        /// cheio e o antisserrilhado apaga o que sobra. Um ponto a mais devolve
        /// peso sem tirar a hierarquia, que continua vindo da COR.
        /// </summary>
        public static readonly Font FontSmall = new Font(Tipo.Pequena, Pt(Tipo.Legenda));

        /// <summary>
        /// Seminegrito, para o valor que precisa ter presenca sem virar titulo.
        ///
        /// "Segoe UI Semibold" e uma familia propria no Windows, e nao um estilo
        /// da "Segoe UI": pedir FontStyle.Bold daria o negrito cheio, que num
        /// rotulo de tres palavras pesa demais.
        /// </summary>
        public static readonly Font FontSemi = Semi(Pt(Tipo.Corpo));
        /// <summary>Valor dos blocos de resumo da pagina Baterias.</summary>
        public static readonly Font FontTileValue = Semi(Pt(Tipo.Subtitulo), Tipo.Numeros);

        /// <summary>O nome do aplicativo no alto do Sobre.</summary>
        public static readonly Font FontAppName = Semi(Pt(Tipo.Nome), Tipo.Exibicao);
        /// <summary>A porcentagem grande do cartao de bateria.</summary>
        public static readonly Font FontBigPct = Semi(Pt(Tipo.Numero), Tipo.Numeros);

        private const TextFormatFlags SemMargem = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        private static readonly Dictionary<Font, int> _celulas = new Dictionary<Font, int>();

        /// <summary>Largura da celula de um algarismo: a do mais largo, para todos caberem igual. Guardada por fonte (as fontes do Ui duram o processo).</summary>
        private static int CelulaDeDigito(Graphics g, Font f)
        {
            int w;
            if (_celulas.TryGetValue(f, out w)) return w;
            w = 0;
            for (char c = '0'; c <= '9'; c++)
                w = Math.Max(w, TextRenderer.MeasureText(g, c.ToString(), f, Size.Empty, SemMargem).Width);
            _celulas[f] = w;
            return w;
        }

        /// <summary>
        /// Valor ao vivo com algarismos de largura fixa. A Segoe UI no GDI tem
        /// o "1" mais estreito que o "0", e um numero que muda a cada segundo
        /// dancava de largura; aqui cada algarismo ocupa a mesma celula.
        /// </summary>
        public static Size MedirNumero(Graphics g, string s, Font f)
        {
            int cel = CelulaDeDigito(g, f), w = 0;
            foreach (char c in s)
                w += char.IsDigit(c) ? cel : TextRenderer.MeasureText(g, c.ToString(), f, Size.Empty, SemMargem).Width;
            int h = TextRenderer.MeasureText(g, "0", f, Size.Empty, SemMargem).Height;
            // O GDI mede o glifo sem o que ele passa da celula (o "0" e o "%"): sobra uma folga
            return new Size(w + Math.Max(Ui.S(2), h / 6), h);
        }

        public static void DesenharNumero(Graphics g, string s, Font f, Point p, Color cor)
        {
            int cel = CelulaDeDigito(g, f), x = p.X;
            foreach (char c in s)
            {
                string t = c.ToString();
                int w = TextRenderer.MeasureText(g, t, f, Size.Empty, SemMargem).Width;
                if (char.IsDigit(c)) { TextRenderer.DrawText(g, t, f, new Point(x + (cel - w) / 2, p.Y), cor, SemMargem); x += cel; }
                else { TextRenderer.DrawText(g, t, f, new Point(x, p.Y), cor, SemMargem); x += w; }
            }
        }

        internal static Font Semi(float tamanho) { return Semi(tamanho, Tipo.Texto); }

        internal static Font Semi(float tamanho, string familia)
        {
            foreach (string nome in new[] { familia + " Semibold", "Segoe UI Semibold" })
            {
                try
                {
                    Font f = new Font(nome, tamanho);
                    // Quando a familia nao existe, o GDI+ devolve a substituta e o
                    // nome nao bate: melhor cair no negrito do que num tipo alheio.
                    if (f.Name.IndexOf("Semibold", StringComparison.OrdinalIgnoreCase) >= 0) return f;
                    f.Dispose();
                }
                catch { }
            }
            return new Font(familia, tamanho, FontStyle.Bold);
        }

        /// <summary>Leitura em destaque - o numero e o assunto do cartao.</summary>
        public static readonly Font FontValue = Semi(Pt(Tipo.Destaque), Tipo.Numeros);
        public static readonly Font FontGlyph8 = new Font(Tipo.Icones, Pt(8f));
        public static readonly Font FontGlyph9 = new Font(Tipo.Icones, Pt(9f));
        public static readonly Font FontGlyph10 = new Font(Tipo.Icones, Pt(10f));
        public static readonly Font FontGlyph11 = new Font(Tipo.Icones, Pt(11f));
        public static readonly Font FontGlyph18 = new Font(Tipo.Icones, Pt(18f));
        public static readonly Font FontBadge = Semi(Pt(Tipo.MinimoDoSelo), Tipo.Pequena);


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
        /// <summary>Mistura de duas cores (alfa incluido), t de 0 a 1.</summary>
        public static Color Misturar(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb((int)Math.Round(a.A + (b.A - a.A) * t), (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// O anel de foco do teclado: 2 px no azul de enfase, por dentro do
        /// retangulo, com o raio do controle. Um so desenho para todos, no lugar
        /// do pontilhado do sistema.
        /// </summary>
        public static void AnelDeFoco(Graphics g, Rectangle r, int raio)
        {
            using (GraphicsPath p = RoundRect(Rectangle.Inflate(r, -1, -1), Math.Max(0, raio - 1)))
            using (Pen pen = new Pen(Accent, 2f))
                g.DrawPath(pen, p);
        }

        public static readonly int Radius = Ui.S(8);
        /// <summary>Raios da escala: 4 para controle (botao, linha, campo), 8 para cartao e janela.</summary>
        public static readonly int RaioControle = Ui.S(4);
        public static readonly int RaioCartao = Ui.S(8);
        public static readonly int Gap = Ui.S(16);

        /// <summary>
        /// Animacoes ligadas no Windows (Acessibilidade > Efeitos visuais).
        /// Desligadas, tudo que desliza ou pulsa vai direto ao estado final.
        /// </summary>
        public static bool Animacoes
        {
            get
            {
                if (AnimacoesForcadas.HasValue) return AnimacoesForcadas.Value;
                try { return SystemInformation.UIEffectsEnabled; }
                catch { return true; }
            }
        }

        /// <summary>
        /// Os testes fixam a resposta: a opcao do Windows varia de maquina para
        /// maquina, e o que eles conferem e o estado final ou o quadro pedido.
        /// </summary>
        internal static bool? AnimacoesForcadas;

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
}
