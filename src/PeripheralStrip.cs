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
        internal static readonly Font FontPct = Ui.Semi(Ui.Pt(12f), Ui.Tipo.Numeros);

        private static readonly int Icone = Ui.S(40);
        private static readonly int Vao = Ui.S(36);
        private static readonly int Barra = Ui.S(32);   // TamanhoDaPilha.Width, com o polo

        /// <summary>O que a faixa desenha agora: a ultima leitura mais quem ainda esta saindo.</summary>
        private Periferico[] _itens = new Periferico[0];
        private float[] _presenca = new float[0];
        private bool[] _saindo = new bool[0];
        private Periferico[] _reais = new Periferico[0];
        private string[] _previsoesReais = new string[0];
        private readonly PresencaAnimada _entradas = new PresencaAnimada();
        private readonly Dictionary<string, Periferico> _ultimo = new Dictionary<string, Periferico>();
        private readonly Dictionary<string, string> _ultimaPrevisao = new Dictionary<string, string>();
        private bool _lido;

        /// <summary>Area de cada item no ultimo desenho, para o clique.</summary>
        private readonly List<KeyValuePair<Rectangle, Periferico>> _zonas = new List<KeyValuePair<Rectangle, Periferico>>();
        private readonly ToolTip _dica = new ToolTip();
        private Periferico _sobre;

        private readonly AnimadorDePilhas _animador;

        public PeripheralStrip()
        {
            Title = null;
            _animador = new AnimadorDePilhas(this);
            _entradas.Quadro += delegate { Reconstruir(); };
            _entradas.Removido += AoPassarPresenca;
        }

        /// <summary>Duracao estimada de cada item, ao lado da porcentagem; nulo enquanto mede.</summary>
        private string[] _previsoes = new string[0];

        /// <summary>Troca a lista; so redesenha quando algo mudou de fato.</summary>
        public void Atualizar(Periferico[] itens)
        {
            if (itens == null) itens = new Periferico[0];
            string[] previsoes = new string[itens.Length];
            for (int i = 0; i < itens.Length; i++) previsoes[i] = PrevisaoCurta(itens[i]);

            bool igual = itens.Length == _reais.Length;
            for (int i = 0; igual && i < itens.Length; i++)
                igual = itens[i].Equals(_reais[i]) && previsoes[i] == _previsoesReais[i];
            if (igual) return;
            foreach (Periferico p in itens)
                TransicaoDeCarga.Informar(HistoricoDeBateria.Chave(p), p.Carregando && p.Bateria.HasValue);
            _reais = itens;
            _previsoesReais = previsoes;
            string[] chaves = WidgetDeBateria.Chaves(itens);
            for (int i = 0; i < itens.Length; i++) { _ultimo[chaves[i]] = itens[i]; _ultimaPrevisao[chaves[i]] = previsoes[i]; }
            // sem janela na tela nao ha o que animar: a lista ja chega no estado final
            bool animar = _lido && IsHandleCreated && Visible;
            _lido = true;
            _entradas.Definir(chaves, animar);
            Reconstruir();
        }

        /// <summary>Monta a lista desenhada a partir da presenca: quem esta saindo continua nela, esmaecendo.</summary>
        private void Reconstruir()
        {
            List<Periferico> itens = new List<Periferico>();
            List<string> previsoes = new List<string>();
            List<float> pres = new List<float>();
            List<bool> saindo = new List<bool>();
            for (int i = 0; i < _entradas.Quantidade; i++)
            {
                string k = _entradas.Chave(i);
                Periferico p;
                if (!_ultimo.TryGetValue(k, out p)) continue;
                string prev;
                _ultimaPrevisao.TryGetValue(k, out prev);
                itens.Add(p); previsoes.Add(prev); pres.Add(_entradas.Valor(i)); saindo.Add(_entradas.Saindo(i));
            }
            _itens = itens.ToArray();
            _previsoes = previsoes.ToArray();
            _presenca = pres.ToArray();
            _saindo = saindo.ToArray();
            Invalidate();
        }

        private void AoPassarPresenca(object sender, EventArgs e)
        {
            HashSet<string> ficam = new HashSet<string>();
            for (int i = 0; i < _entradas.Quantidade; i++) ficam.Add(_entradas.Chave(i));
            foreach (string k in new List<string>(_ultimo.Keys))
                if (!ficam.Contains(k)) { _ultimo.Remove(k); _ultimaPrevisao.Remove(k); }
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
            if (disposing) { _dica.Dispose(); _animador.Dispose(); _entradas.Dispose(); }
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

            // Cada aparelho que entra ou sai abre (ou fecha) a sua vaga e depois
            // esmaece nela: de 0 a 0,5 da presenca a vaga, de 0,5 a 1 o fade, para
            // o texto nunca ser cortado nem se sobrepor. Quando um sai e outro
            // entra ao mesmo tempo (o marcador "nenhum conectado" cedendo lugar
            // ao aparelho que conectou), os dois dividem a MESMA vaga: ela vai
            // da largura de um a do outro, um esmaece e o outro aparece no lugar,
            // sem abrir buraco nem varrer os vizinhos de um lado ao outro.
            int n = _itens.Length;
            int[] larg = new int[n];
            for (int i = 0; i < n; i++)
                larg[i] = Icone + Ui.S(10) + LarguraDoTexto(g, _itens[i], _previsoes[i]);

            List<int[]> vagas = new List<int[]>();   // { quem sai (ou -1), quem entra ou fica (ou -1) }
            for (int i = 0; i < n; i++)
            {
                if (i + 1 < n && _saindo[i] != _saindo[i + 1] && _presenca[i] < 1f && _presenca[i + 1] < 1f)
                {
                    vagas.Add(_saindo[i] ? new int[] { i, i + 1 } : new int[] { i + 1, i });
                    i++;
                }
                else vagas.Add(_saindo[i] ? new int[] { i, -1 } : new int[] { -1, i });
            }

            float[] largura = new float[vagas.Count];
            float total = 0f, soma = 0f;
            for (int k = 0; k < vagas.Count; k++)
            {
                int sai = vagas[k][0], entra = vagas[k][1];
                if (sai >= 0 && entra >= 0)
                {
                    float u = _presenca[entra];
                    largura[k] = (1f - u) * (larg[sai] + Vao) + u * (larg[entra] + Vao);
                    soma += 1f;
                }
                else
                {
                    int i = sai >= 0 ? sai : entra;
                    float abertura = Math.Min(1f, _presenca[i] / 0.5f);
                    largura[k] = abertura * (larg[i] + Vao);
                    soma += abertura;
                }
                total += largura[k];
            }
            total -= Vao * Math.Min(1f, soma);

            int x = (int)Math.Round((Width - total) / 2f);
            if (x < Ui.S(16)) x = Ui.S(16);
            for (int k = 0; k < vagas.Count; k++)
            {
                int vaga = (int)Math.Ceiling(largura[k]);
                int sai = vagas[k][0], entra = vagas[k][1];
                if (sai >= 0 && entra >= 0)
                {
                    float u = _presenca[entra];
                    Item(g, sai, x, larg[sai], Math.Max(0f, 1f - u / 0.5f));
                    Item(g, entra, x, larg[entra], Math.Max(0f, (u - 0.5f) / 0.5f));
                }
                else
                {
                    int i = sai >= 0 ? sai : entra;
                    Item(g, i, x, larg[i], Math.Max(0f, Math.Min(1f, (_presenca[i] - 0.5f) / 0.5f)));
                }
                x += vaga;
            }
        }

        /// <summary>Um aparelho na posicao x com a opacidade pedida (a area de clique so vale daquele que se le).</summary>
        private void Item(Graphics g, int i, int x, int largura, float alfa)
        {
            if (alfa >= 0.999f) Desenhar(g, _itens[i], x, _previsoes[i]);
            else if (alfa > 0.001f) Esmaecido(g, x, largura, alfa, delegate { Desenhar(g, _itens[i], x, _previsoes[i]); });
            if (alfa > 0.5f)
                _zonas.Add(new KeyValuePair<Rectangle, Periferico>(
                    new Rectangle(x - Ui.S(6), Ui.S(4), largura + Ui.S(12), Height - Ui.S(8)), _itens[i]));
        }

        /// <summary>
        /// Fade do item que entra ou sai: recortado na fatia que ocupa, para nao
        /// apagar o vizinho, e coberto pela cor do cartao com o que falta de
        /// opacidade. TextRenderer nao aceita alfa, entao o esmaecer e por cima.
        /// </summary>
        private void Esmaecido(Graphics g, int x, int largura, float v, Action desenha)
        {
            Region antes = g.Clip;
            try
            {
                g.SetClip(new Rectangle(x, 0, Math.Max(1, largura), Height), CombineMode.Intersect);
                desenha();
                int a = (int)Math.Round(255 * (1f - v));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(a, Ui.Surface)))
                    g.FillRectangle(b, new Rectangle(x, 0, Math.Max(1, largura), Height));
            }
            finally { g.Clip = antes; antes.Dispose(); }
        }

        private static string Valor(Periferico p)
        {
            return p.Bateria.HasValue ? p.Bateria.Value + "%" : "--";
        }

        internal static string Nota(Periferico p, string previsao)
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
            // medido como e desenhado (sem a folga do GDI): com a folga o grupo saia
            // alguns pixels para a esquerda do centro do cartao
            int nome = TextRenderer.MeasureText(g, p.Nome, Ui.FontSmall, Size.Empty, TextFormatFlags.NoPadding).Width;
            string nota = Nota(p, previsao);
            if (SemNumero(p))
                return Math.Max(nome, TextRenderer.MeasureText(g, nota, Ui.FontSmall, Size.Empty, TextFormatFlags.NoPadding).Width);

            int linha = TextRenderer.MeasureText(g, Valor(p), FontPct, Size.Empty, TextFormatFlags.NoPadding).Width;
            if (p.Bateria.HasValue) linha += Ui.S(8) + Barra;
            int baixo = nota != null ? TextRenderer.MeasureText(g, nota, Ui.FontSmall, Size.Empty, TextFormatFlags.NoPadding).Width : 0;
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
            // consultada mesmo sem numero na tela, para o estado nao ficar
            // velho e animar do nada quando a leitura voltar
            bool animando;
            float carga = TransicaoDeCarga.Valor(HistoricoDeBateria.Chave(p), p.Carregando && p.Bateria.HasValue, out animando);
            if (animando) _animador.Transicao();

            int tx = x + Icone + Ui.S(10);
            int yNome = Height / 2 - Ui.S(27);
            TextRenderer.DrawText(g, p.Nome, Ui.FontSmall, new Point(tx, yNome), Ui.Faint, TextFormatFlags.NoPadding);

            int yVal = Height / 2 - Ui.S(10);
            if (SemNumero(p))
            {
                TextRenderer.DrawText(g, Nota(p, null), Ui.FontSmall, new Point(tx, yVal + Ui.S(3)), Ui.Muted, TextFormatFlags.NoPadding);
                return;
            }

            string valor = Valor(p);
            TextRenderer.DrawText(g, valor, FontPct, new Point(tx, yVal), Cor(p, carga), TextFormatFlags.NoPadding);
            int fim = tx + TextRenderer.MeasureText(g, valor, FontPct, Size.Empty, TextFormatFlags.NoPadding).Width;

            if (p.Bateria.HasValue)
            {
                fim += Ui.S(8);
                Rectangle pilha = new Rectangle(fim, yVal + Ui.S(4), Barra, TamanhoDaPilha.Height);
                DesenharPilha(g, pilha, p.Bateria.Value, CorFora(p), carga);
                if (p.Carregando) _animador.Registrar(pilha);
                fim += Barra;
            }

            string nota = Nota(p, previsao);
            if (nota != null)
                TextRenderer.DrawText(g, nota, Ui.FontSmall, new Point(tx, yVal + Ui.S(23)), Ui.Muted, TextFormatFlags.NoPadding);
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
        internal static readonly Size TamanhoDaPilha = new Size(Ui.S(32), Ui.S(14));

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
            DesenharPilha(g, r, pct, cor, carregando ? 1f : 0f);
        }

        /// <summary>
        /// A pilha no meio do caminho entre fora e dentro do cabo: carga 0 e a
        /// pilha comum na cor <paramref name="cor"/>, 1 e a de carregando, e o
        /// que fica entre as duas mistura as cores e acende o raio aos poucos
        /// (ver <see cref="TransicaoDeCarga"/>).
        /// </summary>
        internal static void DesenharPilha(Graphics g, Rectangle r, int pct, Color cor, float carga)
        {
            SmoothingMode antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                pct = Math.Max(0, Math.Min(100, pct));
                carga = Math.Max(0f, Math.Min(1f, carga));
                bool carregando = carga > 0.01f;
                Color traco = Misturar(Ui.Muted, Verde, carga);
                Color nivel = Misturar(cor, Verde, carga);
                float u = Ui.Escala;   // tracos e folgas na escala da tela

                RectangleF corpo = new RectangleF(r.X + 0.75f * u, r.Y + 0.75f * u, r.Width - 5f * u, r.Height - 1.5f * u);
                using (GraphicsPath p = Arredondado(corpo, 3.5f * u))
                using (Pen pen = new Pen(traco, 1.5f * u))
                    g.DrawPath(pen, p);

                RectangleF polo = new RectangleF(corpo.Right + 1.25f * u, r.Y + r.Height * 0.32f, 2.25f * u, r.Height * 0.36f);
                using (GraphicsPath p = Arredondado(polo, u))
                using (SolidBrush b = new SolidBrush(traco))
                    g.FillPath(b, p);

                RectangleF dentro = RectangleF.Inflate(corpo, -2.5f * u, -2.5f * u);
                float w = dentro.Width * pct / 100f;
                if (pct > 0 && w < 2f * u) w = 2f * u;
                if (w > 0)
                {
                    RectangleF cheio = new RectangleF(dentro.X, dentro.Y, w, dentro.Height);
                    using (GraphicsPath p = Arredondado(cheio, 1.5f * u))
                    {
                        using (SolidBrush b = new SolidBrush(nivel)) g.FillPath(b, p);
                        if (carregando) Brilho(g, p, dentro, carga);
                    }
                }

                if (carregando) Raio(g, corpo, carga);
            }
            finally { g.SmoothingMode = antes; }
        }

        /// <summary>Faixa clara que atravessa o nivel a cada ciclo do animador.</summary>
        private static void Brilho(Graphics g, GraphicsPath nivel, RectangleF dentro, float opacidade)
        {
            float faixa = Math.Max(6f, dentro.Width * 0.5f);
            float x = dentro.X - faixa + (dentro.Width + faixa) * AnimadorDePilhas.Fase;
            RectangleF area = new RectangleF(x, dentro.Y, faixa, dentro.Height);
            using (LinearGradientBrush b = new LinearGradientBrush(area, Color.Transparent, Color.Transparent, 0f))
            {
                ColorBlend cb = new ColorBlend(3);
                cb.Colors = new Color[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb((int)(120 * opacidade), 255, 255, 255), Color.FromArgb(0, 255, 255, 255) };
                cb.Positions = new float[] { 0f, 0.5f, 1f };
                b.InterpolationColors = cb;
                Region antes = g.Clip;
                g.SetClip(nivel, CombineMode.Intersect);
                g.FillRectangle(b, area);
                g.Clip = antes;
            }
        }

        /// <summary>O raio no meio da pilha, branco com contorno escuro para ler sobre o verde e sobre o vazio.</summary>
        internal static void Raio(Graphics g, RectangleF corpo)
        {
            Raio(g, corpo, 1f);
        }

        /// <summary>O raio meio apagado, enquanto entra ou sai da carga.</summary>
        internal static void Raio(Graphics g, RectangleF corpo, float opacidade)
        {
            opacidade = Math.Max(0f, Math.Min(1f, opacidade));
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
                using (Pen contorno = new Pen(Color.FromArgb((int)(200 * opacidade), 0x14, 0x3D, 0x22), 1.6f))
                {
                    contorno.LineJoin = LineJoin.Round;
                    g.DrawPath(contorno, p);
                }
                using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(255 * opacidade), Color.White))) g.FillPath(b, p);
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
            return Cor(p, p.Bateria.HasValue && p.Carregando ? 1f : 0f);
        }

        /// <summary>A cor do nivel a caminho do verde: 0 fora do cabo, 1 carregando.</summary>
        internal static Color Cor(Periferico p, float carga)
        {
            if (!p.Bateria.HasValue) return Ui.Muted;
            return Misturar(CorFora(p), Verde, carga);
        }

        /// <summary>A cor do nivel ignorando o cabo: vermelho em 10%, amarelo em 30%.</summary>
        internal static Color CorFora(Periferico p)
        {
            if (!p.Bateria.HasValue) return Ui.Muted;
            if (p.Bateria.Value <= 10) return Ui.Danger;
            if (p.Bateria.Value <= 30) return Ui.Warn;
            return Ui.Accent;
        }

        /// <summary>De a ate b, alfa incluido; t fora de 0 a 1 fica na ponta.</summary>
        internal static Color Misturar(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }

    /// <summary>
    /// A passagem suave entre fora e dentro do cabo, igual em todo lugar que
    /// mostra o aparelho: tira da Visao geral, pagina Baterias e widget.
    ///
    /// Cada aparelho tem um valor de 0 (fora do cabo) a 1 (carregando) que
    /// corre ate o alvo em <see cref="DuracaoMs"/>, com saida e chegada
    /// suaves. Quem desenha pergunta o valor a cada quadro e, enquanto ele
    /// anda, continua pedindo quadros. A primeira vez que um aparelho aparece
    /// ja entra no estado certo, sem animar: abrir o app com o mouse no cabo
    /// nao e uma transicao.
    ///
    /// O alvo so muda por Informar, chamado quando chega leitura nova. O
    /// desenho nao muda o alvo: o widget le a cada 2 s e a Visao geral a cada
    /// 1 s, e se cada um puxasse o alvo para o que viu por ultimo, os dois
    /// discordariam por ate 2 s e a animacao recomecaria a cada quadro.
    /// </summary>
    internal static class TransicaoDeCarga
    {
        internal const int DuracaoMs = 700;

        private sealed class Estado { public float De, Alvo; public int Inicio; }

        private static readonly Dictionary<string, Estado> _estados = new Dictionary<string, Estado>();
        private static readonly object _trava = new object();

        /// <summary>Leitura nova do aparelho: se o cabo mudou, comeca a transicao.</summary>
        public static void Informar(string chave, bool carregando)
        {
            Informar(chave, carregando, Environment.TickCount);
        }

        internal static void Informar(string chave, bool carregando, int agora)
        {
            if (chave == null) return;
            float alvo = carregando ? 1f : 0f;
            lock (_trava)
            {
                Estado e = Obter(chave, alvo, agora);
                if (e.Alvo == alvo) return;
                // virou no meio do caminho: parte de onde estava, sem salto
                e.De = Em(e, agora);
                e.Alvo = alvo;
                e.Inicio = agora;
            }
        }

        public static float Valor(string chave, bool carregando)
        {
            bool animando;
            return Valor(chave, carregando, out animando);
        }

        /// <summary>
        /// O valor agora. <paramref name="carregando"/> so vale para um
        /// aparelho que ninguem informou ainda; o alvo vem de Informar.
        /// </summary>
        public static float Valor(string chave, bool carregando, out bool animando)
        {
            // Sem animacoes no Windows, a cor ja esta no fim da transicao.
            int agora = Environment.TickCount;
            return Valor(chave, carregando, out animando, Ui.Animacoes ? agora : agora + 2 * DuracaoMs);
        }

        /// <summary>O valor em <paramref name="agora"/> (TickCount); separado para os testes.</summary>
        internal static float Valor(string chave, bool carregando, out bool animando, int agora)
        {
            float alvo = carregando ? 1f : 0f;
            animando = false;
            if (chave == null) return alvo;
            lock (_trava)
            {
                Estado e = Obter(chave, alvo, agora);
                float atual = Em(e, agora);
                animando = atual != e.Alvo;
                return atual;
            }
        }

        /// <summary>O estado do aparelho; o primeiro ja nasce parado no alvo.</summary>
        private static Estado Obter(string chave, float alvo, int agora)
        {
            Estado e;
            if (!_estados.TryGetValue(chave, out e))
            {
                e = new Estado();
                e.De = e.Alvo = alvo;
                e.Inicio = agora - DuracaoMs;
                _estados[chave] = e;
            }
            return e;
        }

        private static float Em(Estado e, int agora)
        {
            int passou = unchecked(agora - e.Inicio);
            if (passou < 0 || passou >= DuracaoMs) return e.Alvo;
            float u = passou / (float)DuracaoMs;
            u = u * u * (3f - 2f * u);
            return e.De + (e.Alvo - e.De) * u;
        }

        /// <summary>Esquece tudo; so para os testes.</summary>
        internal static void Limpar()
        {
            lock (_trava) _estados.Clear();
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
        private bool _transicao;

        /// <summary>De 0 a 1 ao longo do ciclo; o mesmo para todas as pilhas da tela.</summary>
        public static float Fase
        {
            // Sem animacoes no Windows, a faixa fica parada fora da pilha.
            get { return Ui.Animacoes ? (Environment.TickCount & int.MaxValue) % CicloMs / (float)CicloMs : 0f; }
        }

        public AnimadorDePilhas(Control dono)
        {
            _dono = dono;
            _relogio.Interval = 50;
            _relogio.Tick += delegate
            {
                if (!_dono.Visible) { _relogio.Stop(); return; }
                // na transicao muda tambem a cor do numero: vai o controle todo
                if (_transicao) { _dono.Invalidate(); return; }
                foreach (Rectangle r in _pilhas) _dono.Invalidate(Rectangle.Inflate(r, 2, 2));
            };
        }

        public void ComecarQuadro() { _pilhas.Clear(); _transicao = false; }

        public void Registrar(Rectangle r) { _pilhas.Add(r); }

        /// <summary>Algum aparelho deste quadro esta entrando ou saindo da carga.</summary>
        public void Transicao() { _transicao = true; }

        /// <summary>Liga o relogio se algo carrega na tela, desliga se nada carrega.</summary>
        public void Ajustar()
        {
            bool ligar = (_pilhas.Count > 0 || _transicao) && _dono.Visible && Ui.Animacoes;
            if (ligar && !_relogio.Enabled) _relogio.Start();
            else if (!ligar && _relogio.Enabled) _relogio.Stop();
        }

        public void Dispose() { _relogio.Stop(); _relogio.Dispose(); }
    }
}
