using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// A tira de perifericos da Visao geral: imagem, nome e carga de cada um.
    ///
    /// Pintada inteira aqui, sem rotulos filhos: a lista muda de tamanho
    /// quando um aparelho liga ou desliga, e recriar controles a cada mudanca
    /// so para mostrar tres textos curtos seria mais codigo e mais piscada.
    ///
    /// Cada item e clicavel e abre o app que cuida dele - G HUB, o da Attack
    /// Shark ou o painel rapido de Bluetooth da barra de tarefas.
    /// </summary>
    public class PeripheralStrip : Card
    {
        internal static readonly Font FontPct = new Font("Segoe UI", 12f, FontStyle.Bold);

        private const int Icone = 40;
        private const int Vao = 36;
        private const int Barra = 32;   // TamanhoDaPilha.Width, com o polo

        private Periferico[] _itens = new Periferico[0];

        /// <summary>Area de cada item no ultimo desenho, para o clique.</summary>
        private readonly List<KeyValuePair<Rectangle, Periferico>> _zonas = new List<KeyValuePair<Rectangle, Periferico>>();
        private readonly ToolTip _dica = new ToolTip();
        private Periferico _sobre;

        private readonly AnimadorDePilhas _animador;

        public PeripheralStrip()
        {
            Title = null;
            _animador = new AnimadorDePilhas(this);
        }

        /// <summary>Duracao estimada de cada item, ao lado da porcentagem; nulo enquanto mede.</summary>
        private string[] _previsoes = new string[0];

        /// <summary>Troca a lista; so redesenha quando algo mudou de fato.</summary>
        public void Atualizar(Periferico[] itens)
        {
            if (itens == null) itens = new Periferico[0];
            string[] previsoes = new string[itens.Length];
            for (int i = 0; i < itens.Length; i++) previsoes[i] = PrevisaoCurta(itens[i]);

            bool igual = itens.Length == _itens.Length;
            for (int i = 0; igual && i < itens.Length; i++)
                igual = itens[i].Equals(_itens[i]) && previsoes[i] == _previsoes[i];
            if (igual) return;
            _itens = itens;
            _previsoes = previsoes;
            Invalidate();
        }

        /// <summary>
        /// "~25 h", e no headset tambem a do G HUB: "~25 h · G HUB 48 h".
        /// Carregando ou desligado nao ha duracao a mostrar.
        /// </summary>
        internal static string PrevisaoCurta(Periferico p)
        {
            if (p == null || !p.Bateria.HasValue) return null;
            if (p.Carregando)
            {
                // no cabo a previsao que interessa e quando enche
                try { return CartaoDeBateria.AteEncher(HistoricoDeBateria.AteEncher(p)); }
                catch (Exception ex) { Log.Error("previsao de carga", ex); return null; }
            }
            string nossa = null;
            try
            {
                double? h = HistoricoDeBateria.Restante(p);
                if (h.HasValue) nossa = "~" + HistoricoDeBateria.DuracaoAproximada(h.Value);
            }
            catch (Exception ex) { Log.Error("previsao de bateria", ex); }
            string ghub = p.Fonte == FontePeriferico.GHub && p.Autonomia.HasValue ? "G HUB " + p.Autonomia.Value + " h" : null;
            if (nossa != null && ghub != null) return nossa + "  ·  " + ghub;
            return nossa ?? ghub;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _dica.Dispose(); _animador.Dispose(); }
            base.Dispose(disposing);
        }

        // ---------------- clique ----------------

        private Periferico Em(Point p)
        {
            foreach (KeyValuePair<Rectangle, Periferico> z in _zonas)
                if (z.Key.Contains(p)) return z.Value;
            return null;
        }

        /// <summary>
        /// Clique num aparelho com bateria pede a pagina Baterias. Quem hospeda
        /// a tira decide o que fazer; sem ninguem ouvindo, o clique abre o app
        /// do aparelho, como antes.
        /// </summary>
        public event Action<Periferico> BateriasPedidas;

        /// <summary>
        /// Vai para Baterias o que tem historico la: todo aparelho do G HUB e
        /// da Attack Shark, e o Bluetooth que ja deu leitura. O campo Bluetooth
        /// vazio (desligado, nada conectado, sem leitura) nao tem o que mostrar
        /// na pagina e continua abrindo o painel de Bluetooth do Windows.
        /// </summary>
        private bool VaiParaBaterias(Periferico p)
        {
            if (BateriasPedidas == null || p == null) return false;
            if (p.Estado == EstadoPeriferico.AppFechado) return false;   // o texto pede para abrir o G HUB
            return p.Fonte != FontePeriferico.Bluetooth || p.Bateria.HasValue;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Periferico p = Em(e.Location);
            Cursor = p != null ? Cursors.Hand : Cursors.Default;
            if (ReferenceEquals(p, _sobre)) return;
            _sobre = p;
            if (p == null) { _dica.SetToolTip(this, null); return; }
            string acao = VaiParaBaterias(p) ? T.OpenBatteriesPage
                        : p.Fonte == FontePeriferico.Bluetooth ? T.OpenBluetooth
                        : T.OpenApp(Perifericos.NomeDoApp(p));
            string previsao = Previsao(p);
            _dica.SetToolTip(this, previsao != null ? previsao + "\n" + acao : acao);
        }

        /// <summary>"Restante estimado: ~25 h", para a dica do mouse; nulo enquanto mede.</summary>
        private static string Previsao(Periferico p)
        {
            string curta = PrevisaoCurta(p);
            if (curta == null) return null;
            return p.Carregando ? curta : T.BatRemaining + ": " + curta;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _sobre = null;
            Cursor = Cursors.Default;
            _dica.SetToolTip(this, null);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            Periferico p = Em(e.Location);
            if (p == null) return;
            _sobre = null;   // a dica muda de sentido depois da navegacao
            if (VaiParaBaterias(p)) BateriasPedidas(p);
            else Perifericos.Abrir(p);
        }

        // ---------------- desenho ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            _zonas.Clear();
            _animador.ComecarQuadro();
            try { Pintar(g); }
            finally { _animador.Ajustar(); }
        }

        private void Pintar(Graphics g)
        {
            if (_itens.Length == 0)
            {
                TextRenderer.DrawText(g, T.NoPeripherals, Ui.FontSmall, ClientRectangle, Ui.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int[] larg = new int[_itens.Length];
            int total = (_itens.Length - 1) * Vao;
            for (int i = 0; i < _itens.Length; i++)
            {
                larg[i] = Icone + 10 + LarguraDoTexto(g, _itens[i], _previsoes[i]);
                total += larg[i];
            }

            int x = (Width - total) / 2;
            if (x < 16) x = 16;
            for (int i = 0; i < _itens.Length; i++)
            {
                Desenhar(g, _itens[i], x, _previsoes[i]);
                _zonas.Add(new KeyValuePair<Rectangle, Periferico>(
                    new Rectangle(x - 6, 4, larg[i] + 12, Height - 8), _itens[i]));
                x += larg[i] + Vao;
            }
        }

        private static string Valor(Periferico p)
        {
            return p.Bateria.HasValue ? p.Bateria.Value + "%" : "--";
        }

        private static string Nota(Periferico p, string previsao)
        {
            if (p.Bateria.HasValue)
            {
                if (!p.Carregando) return previsao;
                return previsao != null ? T.PeripheralCharging + "  ·  " + previsao : T.PeripheralCharging;
            }
            switch (p.Estado)
            {
                case EstadoPeriferico.SemLeitura: return T.PeripheralNoReading;
                case EstadoPeriferico.NenhumConectado: return T.BluetoothNone;
                case EstadoPeriferico.RadioDesligado: return T.BluetoothOff;
                case EstadoPeriferico.AppFechado: return T.OpenGHubToRead;
                default: return T.PeripheralOff;
            }
        }

        /// <summary>O marcador do Bluetooth vazio nao tem numero: so o nome e a nota.</summary>
        private static bool SemNumero(Periferico p)
        {
            return p.Estado == EstadoPeriferico.NenhumConectado || p.Estado == EstadoPeriferico.SemLeitura ||
                   p.Estado == EstadoPeriferico.RadioDesligado || p.Estado == EstadoPeriferico.AppFechado;
        }

        private static int LarguraDoTexto(Graphics g, Periferico p, string previsao)
        {
            int nome = TextRenderer.MeasureText(g, p.Nome, Ui.FontSmall).Width;
            string nota = Nota(p, previsao);
            if (SemNumero(p))
                return Math.Max(nome, TextRenderer.MeasureText(g, nota, Ui.FontSmall).Width);

            int linha = TextRenderer.MeasureText(g, Valor(p), FontPct).Width;
            if (p.Bateria.HasValue) linha += 8 + Barra;
            int baixo = nota != null ? TextRenderer.MeasureText(g, nota, Ui.FontSmall).Width : 0;
            return Math.Max(nome, Math.Max(linha, baixo));
        }

        internal static Image Imagem(Periferico p)
        {
            if (p.Fonte == FontePeriferico.Bluetooth) return Assets.Bluetooth;
            if (p.Tipo == TipoPeriferico.Headset) return Assets.Headset;
            if (p.Tipo == TipoPeriferico.Mouse) return Assets.Mouse;
            return null;
        }

        private void Desenhar(Graphics g, Periferico p, int x, string previsao)
        {
            Image img = Imagem(p);
            int topo = (Height - Icone) / 2;
            if (img != null)
            {
                Rectangle r = new Rectangle(x, topo, Icone, Icone);
                if (p.Estado == EstadoPeriferico.RadioDesligado) Apagado(g, img, r);
                else g.DrawImage(img, r);
            }

            // Tres linhas empilhadas: nome, porcentagem com a pilha, e embaixo
            // a previsao (ou o "carregando"). Ao lado da pilha ela esticava o
            // item e abria buracos entre os aparelhos.
            int tx = x + Icone + 10;
            int yNome = Height / 2 - 27;
            TextRenderer.DrawText(g, p.Nome, Ui.FontSmall, new Point(tx, yNome), Ui.Faint, TextFormatFlags.NoPadding);

            int yVal = Height / 2 - 10;
            if (SemNumero(p))
            {
                TextRenderer.DrawText(g, Nota(p, null), Ui.FontSmall, new Point(tx, yVal + 3), Ui.Muted, TextFormatFlags.NoPadding);
                return;
            }

            string valor = Valor(p);
            Color cor = Cor(p);
            TextRenderer.DrawText(g, valor, FontPct, new Point(tx, yVal), cor, TextFormatFlags.NoPadding);
            int fim = tx + TextRenderer.MeasureText(g, valor, FontPct, Size.Empty, TextFormatFlags.NoPadding).Width;

            if (p.Bateria.HasValue)
            {
                fim += 8;
                Rectangle pilha = new Rectangle(fim, yVal + 4, Barra, TamanhoDaPilha.Height);
                DesenharPilha(g, pilha, p.Bateria.Value, cor, p.Carregando);
                if (p.Carregando) _animador.Registrar(pilha);
                fim += Barra;
            }

            string nota = Nota(p, previsao);
            if (nota != null)
                TextRenderer.DrawText(g, nota, Ui.FontSmall, new Point(tx, yVal + 23), Ui.Muted, TextFormatFlags.NoPadding);
        }

        /// <summary>O icone esmaecido, para o radio desligado se ler de longe.</summary>
        internal static void Apagado(Graphics g, Image img, Rectangle r)
        {
            ColorMatrix m = new ColorMatrix();
            m.Matrix33 = 0.4f;
            using (ImageAttributes a = new ImageAttributes())
            {
                a.SetColorMatrix(m);
                g.DrawImage(img, r, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, a);
            }
        }

        /// <summary>Contorno de pilha com o nivel preenchido.</summary>
        internal static void DesenharPilha(Graphics g, Rectangle r, int pct, Color cor)
        {
            DesenharPilha(g, r, pct, cor, false);
        }

        /// <summary>Verde de carregando: o mesmo nos dois temas, legivel no claro e no escuro.</summary>
        internal static readonly Color Verde = Color.FromArgb(0x2F, 0xBF, 0x5A);

        /// <summary>Tamanho da pilha, com o polo: larga o bastante para o raio caber dentro.</summary>
        internal static readonly Size TamanhoDaPilha = new Size(32, 14);

        /// <summary>
        /// A pilha: corpo arredondado, polo a direita e o nivel por dentro.
        ///
        /// Carregando, tudo fica verde, um raio branco vai no meio e um brilho
        /// atravessa o nivel da esquerda para a direita. A versao anterior
        /// fazia uma faixa clara crescer ate o topo; numa pilha de 26 px aquilo
        /// lia como um nivel tremendo, e nao como carga.
        /// </summary>
        internal static void DesenharPilha(Graphics g, Rectangle r, int pct, Color cor, bool carregando)
        {
            SmoothingMode antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                pct = Math.Max(0, Math.Min(100, pct));
                Color traco = carregando ? Verde : Ui.Muted;
                Color nivel = carregando ? Verde : cor;

                RectangleF corpo = new RectangleF(r.X + 0.75f, r.Y + 0.75f, r.Width - 5f, r.Height - 1.5f);
                using (GraphicsPath p = Arredondado(corpo, 3.5f))
                using (Pen pen = new Pen(traco, 1.5f))
                    g.DrawPath(pen, p);

                RectangleF polo = new RectangleF(corpo.Right + 1.25f, r.Y + r.Height * 0.32f, 2.25f, r.Height * 0.36f);
                using (GraphicsPath p = Arredondado(polo, 1f))
                using (SolidBrush b = new SolidBrush(traco))
                    g.FillPath(b, p);

                RectangleF dentro = RectangleF.Inflate(corpo, -2.5f, -2.5f);
                float w = dentro.Width * pct / 100f;
                if (pct > 0 && w < 2f) w = 2f;
                if (w > 0)
                {
                    RectangleF cheio = new RectangleF(dentro.X, dentro.Y, w, dentro.Height);
                    using (GraphicsPath p = Arredondado(cheio, 1.5f))
                    {
                        using (SolidBrush b = new SolidBrush(nivel)) g.FillPath(b, p);
                        if (carregando) Brilho(g, p, dentro);
                    }
                }

                if (carregando) Raio(g, corpo);
            }
            finally { g.SmoothingMode = antes; }
        }

        /// <summary>Faixa clara que atravessa o nivel a cada ciclo do animador.</summary>
        private static void Brilho(Graphics g, GraphicsPath nivel, RectangleF dentro)
        {
            float faixa = Math.Max(6f, dentro.Width * 0.5f);
            float x = dentro.X - faixa + (dentro.Width + faixa) * AnimadorDePilhas.Fase;
            RectangleF area = new RectangleF(x, dentro.Y, faixa, dentro.Height);
            using (LinearGradientBrush b = new LinearGradientBrush(area, Color.Transparent, Color.Transparent, 0f))
            {
                ColorBlend cb = new ColorBlend(3);
                cb.Colors = new Color[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) };
                cb.Positions = new float[] { 0f, 0.5f, 1f };
                b.InterpolationColors = cb;
                Region antes = g.Clip;
                g.SetClip(nivel, CombineMode.Intersect);
                g.FillRectangle(b, area);
                g.Clip = antes;
            }
        }

        /// <summary>O raio no meio da pilha, branco com contorno escuro para ler sobre o verde e sobre o vazio.</summary>
        private static void Raio(Graphics g, RectangleF corpo)
        {
            float h = corpo.Height + 1f, w = h * 0.62f;
            float x = corpo.X + (corpo.Width - w) / 2f, y = corpo.Y + (corpo.Height - h) / 2f;
            PointF[] pts = {
                new PointF(x + w * 0.62f, y),
                new PointF(x + w * 0.05f, y + h * 0.58f),
                new PointF(x + w * 0.45f, y + h * 0.58f),
                new PointF(x + w * 0.36f, y + h),
                new PointF(x + w * 0.95f, y + h * 0.40f),
                new PointF(x + w * 0.55f, y + h * 0.40f) };
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddPolygon(pts);
                using (Pen contorno = new Pen(Color.FromArgb(200, 0x14, 0x3D, 0x22), 1.6f))
                {
                    contorno.LineJoin = LineJoin.Round;
                    g.DrawPath(contorno, p);
                }
                using (SolidBrush b = new SolidBrush(Color.White)) g.FillPath(b, p);
            }
        }

        private static GraphicsPath Arredondado(RectangleF r, float raio)
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

        internal static Color Cor(Periferico p)
        {
            if (!p.Bateria.HasValue) return Ui.Muted;
            if (p.Carregando) return Verde;
            if (p.Bateria.Value <= 10) return Ui.Danger;
            if (p.Bateria.Value <= 30) return Ui.Warn;
            return Ui.Accent;
        }
    }

    /// <summary>
    /// Redesenha so as pilhas que estao carregando, umas 20 vezes por segundo.
    ///
    /// O dono registra o retangulo de cada pilha animada enquanto pinta; o
    /// relogio so corre enquanto houver alguma, e invalida so esses
    /// retangulos - o resto do cartao, com textos e grafico, fica parado.
    /// </summary>
    internal sealed class AnimadorDePilhas : IDisposable
    {
        private const int CicloMs = 1500;

        private readonly Control _dono;
        private readonly Timer _relogio = new Timer();
        private readonly List<Rectangle> _pilhas = new List<Rectangle>();

        /// <summary>De 0 a 1 ao longo do ciclo; o mesmo para todas as pilhas da tela.</summary>
        public static float Fase
        {
            get { return (Environment.TickCount & int.MaxValue) % CicloMs / (float)CicloMs; }
        }

        public AnimadorDePilhas(Control dono)
        {
            _dono = dono;
            _relogio.Interval = 50;
            _relogio.Tick += delegate
            {
                if (!_dono.Visible) { _relogio.Stop(); return; }
                foreach (Rectangle r in _pilhas) _dono.Invalidate(Rectangle.Inflate(r, 2, 2));
            };
        }

        public void ComecarQuadro() { _pilhas.Clear(); }

        public void Registrar(Rectangle r) { _pilhas.Add(r); }

        /// <summary>Liga o relogio se algo carrega na tela, desliga se nada carrega.</summary>
        public void Ajustar()
        {
            bool ligar = _pilhas.Count > 0 && _dono.Visible;
            if (ligar && !_relogio.Enabled) _relogio.Start();
            else if (!ligar && _relogio.Enabled) _relogio.Stop();
        }

        public void Dispose() { _relogio.Stop(); _relogio.Dispose(); }
    }
}
