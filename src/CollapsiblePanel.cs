using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// O relogio de abrir e fechar, separado de quem desenha.
    ///
    /// Um so lugar decide duracao, curva e quando nao animar: o painel do
    /// widget, o "Mais detalhes" de cada aparelho e a barra lateral andavam
    /// cada um no seu ritmo - a barra a 160 ms com curva cubica, os outros
    /// sem animacao nenhuma. Aqui o valor vai de 0 (recolhido) a 1 (aberto)
    /// e quem usa so le Valor a cada Quadro.
    ///
    /// Tempo pelo Stopwatch, e nao pela contagem de ticks: o Timer do Windows
    /// Forms atrasa sob carga, e contar ticks faria a animacao demorar mais
    /// quando a maquina esta ocupada. Com o relogio de verdade ela termina no
    /// tempo, so com menos quadros.
    /// </summary>
    internal sealed class AnimacaoDeRecolher : IDisposable
    {
        /// <summary>Entre os 200 e 300 ms combinados: rapido sem virar salto.</summary>
        public const int DuracaoMs = Motion.Lenta;

        private readonly int _duracao;
        private readonly Timer _relogio = new Timer();
        private readonly Stopwatch _cronometro = new Stopwatch();
        private float _de, _ate, _valor;
        private long _msDoTrecho;
        private bool _descartado;

        /// <summary>Cada passo da animacao, inclusive o ultimo.</summary>
        public event EventHandler Quadro;

        /// <summary>Chegou ao destino, animando ou nao.</summary>
        public event EventHandler Terminou;

        public AnimacaoDeRecolher(bool aberto) : this(aberto, DuracaoMs) { }

        public AnimacaoDeRecolher(bool aberto, int duracaoMs)
        {
            _duracao = Math.Max(1, duracaoMs);
            _de = _ate = _valor = aberto ? 1f : 0f;
            // 10, e nao 16. O relogio do Windows anda em passos de 15,6 ms, e um
            // intervalo de 16 cai no segundo passo: 31 ms, ~32 quadros por
            // segundo, e a animacao travava. Abaixo de um passo, o timer
            // dispara a cada passo (~64 por segundo, medido com
            // Tests.exe --medir-animacao). O valor vem do cronometro, nao daqui.
            _relogio.Interval = 10;
            _relogio.Tick += delegate { Avancar(_cronometro.ElapsedMilliseconds); };
        }

        /// <summary>0 recolhido, 1 aberto, ja passado pela curva.</summary>
        public float Valor { get { return _valor; } }

        /// <summary>Para onde vai: o estado que vale para quem pergunta "esta aberto?".</summary>
        public bool Aberto { get { return _ate >= 1f; } }

        public bool Animando { get { return _relogio.Enabled; } }

        /// <summary>
        /// Vai para aberto ou fechado. Sem animar - janela que ainda nao
        /// apareceu, ou efeitos desligados no Windows - chega no mesmo instante.
        ///
        /// Virar no meio do caminho parte de onde esta, e o trecho que falta
        /// leva so a sua parte da duracao: fechar com o painel quase aberto nao
        /// pode demorar o mesmo que fechar com ele todo aberto.
        /// </summary>
        public void Ir(bool aberto, bool animar)
        {
            if (_descartado) return;
            float alvo = aberto ? 1f : 0f;
            if (alvo == _ate && (Animando || _valor == alvo)) return;

            if (!animar || !Ui.Animacoes)
            {
                _relogio.Stop();
                _cronometro.Reset();
                _de = _ate = _valor = alvo;
                Avisar(true);
                return;
            }

            _de = _valor;
            _ate = alvo;
            _msDoTrecho = Math.Max(1, (long)Math.Round(_duracao * Math.Abs(_ate - _de)));
            _cronometro.Reset();
            _cronometro.Start();
            _relogio.Start();
            Avisar(false);
        }

        /// <summary>Um passo, com o tempo decorrido desde o inicio do trecho. Separado para os testes.</summary>
        internal void Avancar(long ms)
        {
            if (_descartado) return;
            float t = _msDoTrecho <= 0 ? 1f : Math.Min(1f, ms / (float)_msDoTrecho);
            // A curva vale para o trecho, e nao para o valor: de 0,3 a 1 a
            // saida suave acontece entre 0,3 e 1, sem degrau no comeco.
            _valor = _de + (_ate - _de) * Suavizar(t);
            bool fim = t >= 1f;
            if (fim)
            {
                _valor = _ate;
                _relogio.Stop();
                _cronometro.Reset();
            }
            Avisar(fim);
        }

        private void Avisar(bool fim)
        {
            if (Quadro != null) Quadro(this, EventArgs.Empty);
            if (fim && Terminou != null) Terminou(this, EventArgs.Empty);
        }

        /// <summary>cubic-bezier(0.4, 0, 0.2, 1): sai rapido e assenta devagar (ver Motion).</summary>
        internal static float Suavizar(float t) { return Motion.Padrao(t); }

        public void Dispose()
        {
            if (_descartado) return;
            _descartado = true;
            _relogio.Stop();
            _relogio.Dispose();
            _cronometro.Reset();
        }
    }

    /// <summary>
    /// Painel que abre e fecha deslizando a altura.
    ///
    /// Hospeda um controle (o Conteudo) e anima a propria altura entre zero e
    /// AlturaAberta. A ordem e a combinada: ao fechar, o conteudo some primeiro
    /// e so a casca encolhe; ao abrir, a casca cresce vazia e o conteudo aparece
    /// no fim. Desenhar o conteudo inteiro sendo cortado quadro a quadro
    /// custaria uma pintura completa dos filhos por quadro e se leria como
    /// tremor, nao como movimento.
    ///
    /// Quem esta embaixo acompanha pelo AlturaMudou: o dono rearranja a pagina a
    /// cada quadro, entre SuspendLayout e ResumeLayout, e os vizinhos andam
    /// junto com a casca em vez de saltar no fim.
    /// </summary>
    public class CollapsiblePanel : Panel
    {
        private readonly AnimacaoDeRecolher _animacao;
        private Control _conteudo;
        private int _alturaAberta;

        /// <summary>A altura mudou neste quadro: quem arranja a pagina reposiciona os vizinhos.</summary>
        public event EventHandler AlturaMudou;

        /// <summary>Aberto ou fechado, quando o destino muda (antes da animacao).</summary>
        public event EventHandler ExpandidoMudou;

        public CollapsiblePanel() : this(false) { }

        public CollapsiblePanel(bool aberto)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Window;
            AccessibleRole = AccessibleRole.Grouping;
            _animacao = new AnimacaoDeRecolher(aberto);
            _animacao.Quadro += delegate { AplicarQuadro(); };
            _animacao.Terminou += delegate { AplicarQuadro(); };
        }

        /// <summary>O controle mostrado quando aberto, preso ao topo e na largura do painel.</summary>
        public Control Conteudo
        {
            get { return _conteudo; }
            set
            {
                if (_conteudo == value) return;
                if (_conteudo != null) Controls.Remove(_conteudo);
                _conteudo = value;
                if (_conteudo != null)
                {
                    _conteudo.Location = Point.Empty;
                    _conteudo.Width = Width;
                    _conteudo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                    Controls.Add(_conteudo);
                }
                AplicarQuadro();
            }
        }

        /// <summary>Altura com o painel todo aberto; o dono define, em geral a do conteudo.</summary>
        public int AlturaAberta
        {
            get { return _alturaAberta; }
            set
            {
                if (_alturaAberta == value) return;
                _alturaAberta = Math.Max(0, value);
                if (_conteudo != null) _conteudo.Height = _alturaAberta;
                AplicarQuadro();
            }
        }

        /// <summary>O destino: aberto ou fechado. Durante a animacao, para onde ela vai.</summary>
        public bool Expandido
        {
            get { return _animacao.Aberto; }
            set { Definir(value, true); }
        }

        /// <summary>0 fechado, 1 aberto, no quadro corrente; para girar a seta de quem abre.</summary>
        public float Progresso { get { return _animacao.Valor; } }

        public bool Animando { get { return _animacao.Animando; } }

        /// <summary>A altura que este quadro ocupa.</summary>
        public int AlturaAtual { get { return (int)Math.Round(_alturaAberta * _animacao.Valor); } }

        public void Alternar() { Definir(!Expandido, true); }

        /// <summary>
        /// Abre ou fecha. Anima so com a janela na tela: antes de ela aparecer
        /// - montagem, testes - nao ha quem veja, e o estado final e o que conta.
        /// </summary>
        public void Definir(bool aberto, bool animar)
        {
            if (aberto == Expandido && !Animando) return;
            bool mudou = aberto != Expandido;
            // Fechar: o conteudo sai ANTES de a casca comecar a encolher.
            if (!aberto && _conteudo != null) _conteudo.Visible = false;
            _animacao.Ir(aberto, animar && PodeAnimar());
            if (mudou)
            {
                AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
                if (ExpandidoMudou != null) ExpandidoMudou(this, EventArgs.Empty);
            }
        }

        private bool PodeAnimar()
        {
            if (!IsHandleCreated || !Visible) return false;
            Form f = FindForm();
            return f != null && f.Visible && f.WindowState != FormWindowState.Minimized;
        }

        private void AplicarQuadro()
        {
            int alvo = AlturaAtual;
            // Abrir: o conteudo so aparece com a casca toda aberta.
            if (_conteudo != null)
                _conteudo.Visible = _animacao.Aberto && !_animacao.Animando && _animacao.Valor >= 1f;
            if (Height != alvo) Height = alvo;
            Invalidate();
            if (AlturaMudou != null) AlturaMudou(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_conteudo != null && _conteudo.Width != Width) _conteudo.Width = Width;
        }

        /// <summary>A casca: o retangulo do cartao, na altura do quadro, enquanto o conteudo esta fora.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush fundo = new SolidBrush(Ui.Window))
                g.FillRectangle(fundo, e.ClipRectangle);
            if (_conteudo != null && _conteudo.Visible) return;
            if (Height < 2 || Width < 2) return;

            Ui.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            int raio = Math.Min(Ui.Radius, Height / 2);
            using (GraphicsPath p = Ui.RoundRect(r, raio))
            using (SolidBrush b = new SolidBrush(Ui.Surface))
            using (Pen pen = new Pen(Ui.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new Acessivel(this);
        }

        /// <summary>Leitor de tela ouve "expandido" ou "recolhido", e nao so "grupo".</summary>
        private sealed class Acessivel : ControlAccessibleObject
        {
            private readonly CollapsiblePanel _dono;
            public Acessivel(CollapsiblePanel dono) : base(dono) { _dono = dono; }
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates s = base.State & ~(AccessibleStates.Expanded | AccessibleStates.Collapsed);
                    return s | (_dono.Expandido ? AccessibleStates.Expanded : AccessibleStates.Collapsed);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animacao.Dispose();
            base.Dispose(disposing);
        }
    }
}
