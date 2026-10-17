using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>Versao do aplicativo, mostrada no Sobre.</summary>
    ///
    /// O instalador tem a sua (Setup.Versao) porque e compilado a parte, sem
    /// os fontes do app; um teste confere que as duas andam juntas.
    public static class AppInfo
    {
        public const string Versao = "2.25.0";
    }

    /// <summary>
    /// Uma linha das Configuracoes, no estilo das do Windows 11: icone, nome,
    /// descricao curta e o controle a direita.
    ///
    /// O cartao unico de antes empilhava interruptor, nota e campo um abaixo
    /// do outro, cada um com o seu recuo, e a nota de cada opcao ficava mais
    /// longe do controle do que da opcao seguinte. Aqui cada opcao e uma faixa
    /// propria, e o que ela faz esta na mesma linha de onde se mexe.
    ///
    /// Em janela estreita os controles descem para baixo do texto em vez de
    /// espremer a descricao numa coluna de tres palavras.
    /// </summary>
    public class LinhaDeOpcao : Card
    {
        public readonly string Glyph;
        public readonly Label Titulo;
        public readonly Label Descricao;
        private readonly List<Control> _direita = new List<Control>();
        private bool _umaLinha = true;

        private static readonly int Margem = Ui.S(16), ColunaDoTexto = Ui.S(52), Espaco = Ui.S(8), AlturaMinima = Ui.S(62), TextoMinimo = Ui.S(220);

        public LinhaDeOpcao(string glyph, string titulo, string descricao)
            : this(glyph, titulo, Rotulo(descricao, Ui.FontSmall, Ui.Muted)) { }

        /// <summary>A descricao pode ser um rotulo de fora, que alguem atualiza depois (o estado do RTSS, a pasta).</summary>
        public LinhaDeOpcao(string glyph, string titulo, Label descricao)
        {
            Title = null;
            Glyph = glyph;
            Titulo = Rotulo(titulo, Ui.FontBase, Ui.Text);
            Descricao = descricao;
            Descricao.AutoSize = false;
            Controls.Add(Titulo);
            Controls.Add(Descricao);
            Height = AlturaMinima;
        }

        internal static Label Rotulo(string texto, Font fonte, Color cor)
        {
            Label l = new Label();
            l.AutoSize = false;
            l.Text = texto ?? "";
            l.Font = fonte;
            l.ForeColor = cor;
            l.BackColor = Color.Transparent;
            return l;
        }

        /// <summary>Um controle no canto direito, na ordem em que entra.</summary>
        public T Direita<T>(T c) where T : Control
        {
            _direita.Add(c);
            Controls.Add(c);
            return c;
        }

        /// <summary>
        /// Interruptor sem rotulo proprio. Com estado, ganha "Ligado" ou
        /// "Desligado" a esquerda, como no Windows 11; sem ele, quem diz o
        /// estado e outro controle da linha.
        /// </summary>
        public Toggle Interruptor(Toggle t, bool comEstado)
        {
            t.Label = "";
            t.Size = new Size(Ui.S(40), Ui.S(32));
            t.AccessibleName = Titulo.Text;
            if (comEstado)
            {
                Label estado = Rotulo("", Ui.FontBase, Ui.Muted);
                estado.TextAlign = ContentAlignment.MiddleRight;
                int larg = Math.Max(TextRenderer.MeasureText(T.StateOn, Ui.FontBase).Width,
                                    TextRenderer.MeasureText(T.StateOff, Ui.FontBase).Width) + Ui.S(6);
                estado.Size = new Size(larg, Ui.S(32));
                estado.Text = t.Checked ? T.StateOn : T.StateOff;
                t.CheckedChanged += delegate { estado.Text = t.Checked ? T.StateOn : T.StateOff; };
                Direita(estado);
            }
            Direita(t);
            return t;
        }

        public bool EmUmaLinha { get { return _umaLinha; } }

        /// <summary>Poe texto e controles na largura dada e devolve a altura que a linha ficou.</summary>
        public int Arranjar(int largura)
        {
            int direita = 0, alturaDosControles = 0;
            foreach (Control c in _direita)
            {
                direita += c.Width + Espaco;
                alturaDosControles = Math.Max(alturaDosControles, c.Height);
            }
            if (direita > 0) direita -= Espaco;

            _umaLinha = _direita.Count == 0 || ColunaDoTexto + TextoMinimo + Ui.S(16) + direita + Margem <= largura;
            int larguraDoTexto = Math.Max(Ui.S(60), (_umaLinha && direita > 0 ? largura - ColunaDoTexto - Ui.S(16) - direita - Margem
                                                                          : largura - ColunaDoTexto - Margem));

            int hTitulo = Math.Max(Ui.S(18), Titulo.GetPreferredSize(new Size(larguraDoTexto, 0)).Height);
            bool temDescricao = !string.IsNullOrEmpty(Descricao.Text);
            int hDescricao = !temDescricao ? 0
                           : Descricao.AutoEllipsis ? Ui.S(18)
                           : Math.Max(Ui.S(16), Descricao.GetPreferredSize(new Size(larguraDoTexto, 0)).Height);
            int alturaDoTexto = hTitulo + (temDescricao ? Ui.S(2) + hDescricao : 0);

            int altura;
            if (_umaLinha)
            {
                altura = Math.Max(AlturaMinima, Math.Max(alturaDoTexto, alturaDosControles) + Ui.S(24));
                int yTexto = (altura - alturaDoTexto) / 2;
                Titulo.SetBounds(ColunaDoTexto, yTexto, larguraDoTexto, hTitulo);
                Descricao.SetBounds(ColunaDoTexto, Titulo.Bottom + Ui.S(2), larguraDoTexto, hDescricao);
                int x = largura - Margem - direita;
                foreach (Control c in _direita)
                {
                    c.Location = new Point(x, (altura - c.Height) / 2);
                    x += c.Width + Espaco;
                }
            }
            else
            {
                Titulo.SetBounds(ColunaDoTexto, Ui.S(14), larguraDoTexto, hTitulo);
                Descricao.SetBounds(ColunaDoTexto, Titulo.Bottom + Ui.S(2), larguraDoTexto, hDescricao);
                int x = ColunaDoTexto, y = Ui.S(14) + alturaDoTexto + Ui.S(10), linha = 0;
                foreach (Control c in _direita)
                {
                    if (x > ColunaDoTexto && x + c.Width > largura - Margem) { x = ColunaDoTexto; y += linha + Espaco; linha = 0; }
                    c.Location = new Point(x, y);
                    // um controle mais largo que a linha inteira encolhe para caber
                    if (c.Right > largura - Margem) c.Width = Math.Max(Ui.S(40), largura - Margem - c.Left);
                    x += c.Width + Espaco;
                    linha = Math.Max(linha, c.Height);
                }
                altura = y + linha + Ui.S(14);
            }
            Size = new Size(largura, altura);
            Invalidate();
            return altura;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (string.IsNullOrEmpty(Glyph)) return;
            // em uma linha o icone fica no meio da faixa; em duas, alinhado ao nome
            Rectangle r = _umaLinha ? new Rectangle(Margem, 0, Ui.S(24), Height)
                                    : new Rectangle(Margem, Titulo.Top - Ui.S(2), Ui.S(24), Titulo.Height + Ui.S(4));
            TextRenderer.DrawText(e.Graphics, Glyph, Ui.FontGlyph11, r, Ui.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// O alto do Sobre: icone grande, nome e a versao numa pilula. O resto do
    /// cartao (descricao, GitHub, criador) sao controles filhos.
    /// </summary>
    public class CartaoDeIdentidade : Card
    {
        public Image Icone;
        public string Nome = T.AppName;
        public string Versao = T.VersionLabel(AppInfo.Versao);

        public static readonly int Pad = Ui.S(22), LadoDoIcone = Ui.S(88);
        public static int ColunaDoTexto { get { return Pad + LadoDoIcone + Ui.S(22); } }

        public CartaoDeIdentidade() { Title = null; }

        /// <summary>Onde o nome termina, para a pilula da versao e para os testes.</summary>
        public Rectangle AreaDaVersao { get; private set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            if (Icone != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(Icone, new Rectangle(Pad, Pad, LadoDoIcone, LadoDoIcone));
            }

            int x = ColunaDoTexto;
            Size nome = TextRenderer.MeasureText(g, Nome, Ui.FontAppName, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Nome, Ui.FontAppName, new Point(x, Pad - Ui.S(2)), Ui.Text, TextFormatFlags.NoPadding);

            Size v = TextRenderer.MeasureText(g, Versao, Ui.FontSemi, Size.Empty, TextFormatFlags.NoPadding);
            Rectangle pilula = new Rectangle(x + nome.Width + Ui.S(12), Pad - Ui.S(2) + (nome.Height - Ui.S(22)) / 2, v.Width + Ui.S(20), Ui.S(22));
            if (pilula.Right > Width - Pad) pilula.X = Math.Max(x, Width - Pad - pilula.Width);
            AreaDaVersao = pilula;
            using (GraphicsPath p = Ui.RoundRect(pilula, Ui.S(11)))
            using (SolidBrush b = new SolidBrush(Ui.AccentSoft))
                g.FillPath(b, p);
            TextRenderer.DrawText(g, Versao, Ui.FontSemi, pilula, Ui.AccentText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// Um bloco de situacao do Sobre: rotulo pequeno, valor em destaque com um
    /// ponto colorido opcional e uma linha de apoio que quebra quando precisa.
    /// Pintado inteiro, como os blocos do mosaico de Especificacoes.
    /// </summary>
    public class BlocoDeStatus : Control
    {
        public string Rotulo = "", Valor = "", Apoio = "";
        public Color? Ponto;
        public Color? CorDoValor;
        public Color? CorDoApoio;

        private static readonly int Pad = Ui.S(16);
        private const TextFormatFlags Quebra = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

        public BlocoDeStatus()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Window;
            Font = Ui.FontBase;
        }

        private int LarguraDoValor(int largura) { return Math.Max(Ui.S(40), largura - 2 * Pad - (Ponto.HasValue ? Ui.S(16) : 0)); }

        public int AlturaPara(int largura)
        {
            int w = Math.Max(Ui.S(40), largura - 2 * Pad);
            int h = Pad + Ui.S(16 + 6);
            h += Math.Max(Ui.S(20), TextRenderer.MeasureText(Valor ?? "", Ui.FontTileValue, new Size(LarguraDoValor(largura), 0), Quebra).Height);
            if (!string.IsNullOrEmpty(Apoio))
                h += Ui.S(4) + TextRenderer.MeasureText(Apoio, Ui.FontSmall, new Size(w, 0), Quebra).Height;
            return h + Pad - Ui.S(2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Window)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(r, Ui.Radius))
            {
                using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillPath(b, p);
                using (Pen pen = new Pen(Ui.Border)) g.DrawPath(pen, p);
            }

            int w = Math.Max(Ui.S(40), Width - 2 * Pad);
            int y = Pad - Ui.S(2);
            TextRenderer.DrawText(g, (Rotulo ?? "").ToUpperInvariant(), Ui.FontSmall, new Rectangle(Pad, y, w, Ui.S(16)), Ui.Muted,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            y += Ui.S(16 + 6);

            int x = Pad;
            if (Ponto.HasValue)
            {
                using (SolidBrush b = new SolidBrush(Ponto.Value)) g.FillEllipse(b, x, y + Ui.S(6), Ui.S(8), Ui.S(8));
                x += Ui.S(16);
            }
            int hValor = Math.Max(Ui.S(20), TextRenderer.MeasureText(g, Valor ?? "", Ui.FontTileValue, new Size(LarguraDoValor(Width), 0), Quebra).Height);
            TextRenderer.DrawText(g, Valor ?? "", Ui.FontTileValue, new Rectangle(x, y, LarguraDoValor(Width), hValor),
                CorDoValor ?? Ui.Text, Quebra);
            y += hValor + Ui.S(4);

            if (!string.IsNullOrEmpty(Apoio))
                TextRenderer.DrawText(g, Apoio, Ui.FontSmall, new Rectangle(Pad, y, w, Height - y - Ui.S(4)),
                    CorDoApoio ?? Ui.Muted, Quebra);
        }
    }
}
