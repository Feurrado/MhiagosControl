using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Cartao de leitura no estilo painel de estatistica: rotulo pequeno em
    /// cima, valor grande, e o historico recente desenhado no fundo.
    ///
    /// O historico e o que separa isto de uma lista de numeros. Um numero
    /// sozinho responde "quanto agora"; a curva atras responde "isso e normal?",
    /// que e a pergunta que leva alguem a abrir um monitor de hardware. Fica
    /// atras do valor, em area preenchida de baixa opacidade, porque e contexto
    /// e nao pode competir com a leitura.
    ///
    /// A serie nao mora aqui: vem do MetricHistory, que continua registrando com
    /// a janela fechada e sobrevive ao fechamento do aplicativo.
    /// </summary>
    public class MetricCard : Control
    {
        public string Titulo = "";
        public string Sub = "";
        public string Unidade = "";

        /// <summary>Identificador da leitura, para gravar a escolha.</summary>
        public string SensorId = "";

        /// <summary>Janela de tempo desenhada, em segundos.</summary>
        public int Janela = MetricHistory.JanelaPadrao;

        /// <summary>Acoes de edicao, disparadas pelos botoes do canto.</summary>
        public event EventHandler Remover, MoverEsquerda, MoverDireita, TrocarTamanho;

        /// <summary>
        /// O cartao aceita ser movido, redimensionado e removido.
        ///
        /// Falso na tela de bordo, onde a selecao e automatica: os botoes
        /// apareceriam sob o ponteiro e nao fariam nada, que e pior do que nao
        /// existirem - um controle que nao responde ensina que o programa esta
        /// quebrado.
        /// </summary>
        public bool Editavel = true;

        private Rectangle _bRem, _bEsq, _bDir, _bTam;
        private int _sobre = -1;
        private bool _mouseDentro = false;

        /// <summary>Coluna sob o ponteiro, ou -1 quando nao ha o que detalhar.</summary>
        private int _hoverX = -1;

        /// <summary>Faixas de cor. Nulo pinta tudo com a cor de enfase.</summary>
        public float? Atencao, Perigo;

        private float[] _buf;
        private float? _valor;
        private long _historyRevision = -1;

        public MetricCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // Opaco: o cartao ja pinta cada pixel seu, e a transparencia so
            // servia para obrigar a pagina a redesenhar o fundo aqui dentro a
            // cada pintura - numa grade de doze cartoes, doze vezes.
            BackColor = Ui.Window;
            Font = Ui.FontBase;
            Size = new Size(172, 104);
        }

        /// <summary>Leitura do ciclo corrente. A serie e alimentada em outro lugar.</summary>
        public void Push(float? v)
        {
            long revision = MetricHistory.Revision;
            bool mesmoValor = (!_valor.HasValue && !v.HasValue)
                           || (_valor.HasValue && v.HasValue && _valor.Value.Equals(v.Value));
            if (mesmoValor && revision == _historyRevision) return;

            _valor = v;
            _historyRevision = revision;
            Invalidate();
        }

        private Color Cor
        {
            get
            {
                if (!_valor.HasValue) return Ui.Faint;
                if (Perigo.HasValue && _valor.Value >= Perigo.Value) return Ui.Danger;
                if (Atencao.HasValue && _valor.Value >= Atencao.Value) return Ui.Warn;
                return Ui.Accent;
            }
        }

        /// <summary>
        /// Casas decimais pelo tamanho do numero, e nao pela unidade.
        ///
        /// 39,8 °C precisa da decimal - meio grau importa. 3.847 MHz com uma
        /// decimal seria ruido de cinco digitos, e a decimal de uma rotacao de
        /// ventoinha nao existe no sensor.
        /// </summary>
        private static string Formatar(float v)
        {
            if (Math.Abs(v) >= 100) return v.ToString("0");
            return v.ToString("0.0");
        }

        private string Formatado()
        {
            return _valor.HasValue ? Formatar(_valor.Value) : "--";
        }

        // ---------------- edicao ----------------

        /// <summary>
        /// Os botoes so aparecem sob o ponteiro.
        ///
        /// Tres icones fixos em cada cartao transformariam a grade num painel de
        /// controle: numa tela com doze cartoes seriam trinta e seis alvos
        /// disputando atencao com doze numeros, que sao o conteudo. Sob o
        /// ponteiro, aparecem exatamente onde a pessoa ja esta olhando.
        /// </summary>
        protected override void OnMouseEnter(EventArgs e)
        {
            _mouseDentro = true; Invalidate(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _mouseDentro = false; _sobre = -1; _hoverX = -1; _armado = false;
            Cursor = Cursors.Default;
            Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _armado = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int s = -1;
            if (Editavel)
            {
                if (_bEsq.Contains(e.Location)) s = 0;
                else if (_bDir.Contains(e.Location)) s = 1;
                else if (_bRem.Contains(e.Location)) s = 2;
                else if (_bTam.Contains(e.Location)) s = 3;
            }

            // Detalhe so no cartao alto, e so fora dos botoes.
            //
            // No pequeno o balao cobriria o proprio numero que ele explica, e a
            // curva ali tem quatro pixels de altura util - apontar um instante
            // nela seria adivinhacao. Sobre um botao, quem esta mirando o botao
            // nao quer uma leitura de dois minutos atras no caminho.
            // Botao segurado e ponteiro andou: vira arraste, e quem cuida disso e
            // a grade - so ela sabe onde ficam os vizinhos.
            if (_armado && e.Button == MouseButtons.Left && PassouDoLimiar(e.Location))
            {
                _armado = false;
                _hoverX = -1;
                Disparar(Arrastar);
                return;
            }

            int hx = (s < 0 && Height >= 140) ? e.X : -1;

            if (s != _sobre || hx != _hoverX)
            {
                _sobre = s;
                _hoverX = hx;
                Cursor = s >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!Editavel) { base.OnMouseDown(e); return; }
            if (_bEsq.Contains(e.Location)) Disparar(MoverEsquerda);
            else if (_bDir.Contains(e.Location)) Disparar(MoverDireita);
            else if (_bTam.Contains(e.Location)) Disparar(TrocarTamanho);
            else if (_bRem.Contains(e.Location)) Disparar(Remover);
            else if (e.Button == MouseButtons.Left) ComecarArraste(e.Location);
            base.OnMouseDown(e);
        }

        private void Disparar(EventHandler h) { if (h != null) h(this, EventArgs.Empty); }

        // ---------------- arraste ----------------

        /// <summary>Pedido de arraste: o cartao quer ir para onde o ponteiro esta.</summary>
        public event EventHandler Arrastar;

        private Point _pegou = Point.Empty;
        private bool _armado = false;

        /// <summary>Quantos pixels o ponteiro anda antes de virar arraste.</summary>
        private const int Limiar = 6;

        /// <summary>
        /// Onde o cartao foi pego, em coordenadas dele.
        ///
        /// Quem move precisa disso para o cartao nao dar um salto ao colar no
        /// ponteiro: sem o deslocamento, ele pularia com o canto superior
        /// esquerdo sob o cursor, e a pessoa perderia de vista o ponto que
        /// escolheu segurar.
        /// </summary>
        public Point Pegada { get { return _pegou; } }

        private void ComecarArraste(Point p)
        {
            _pegou = p;
            _armado = true;
        }

        /// <summary>
        /// Um clique so nao e um arraste.
        ///
        /// Sem o limiar, qualquer clique no cartao - inclusive o que a pessoa deu
        /// para ver o detalhe da curva - iniciaria uma movimentacao, e a grade se
        /// reorganizaria sozinha por causa de um tremor de mao de dois pixels.
        /// </summary>
        private bool PassouDoLimiar(Point p)
        {
            return Math.Abs(p.X - _pegou.X) >= Limiar || Math.Abs(p.Y - _pegou.Y) >= Limiar;
        }

        private void DesenharBotoes(Graphics g)
        {
            int t = 20, y = 6, x = Width - 8 - t;
            _bRem = new Rectangle(x, y, t, t); x -= t + 2;
            _bTam = new Rectangle(x, y, t, t); x -= t + 2;
            _bDir = new Rectangle(x, y, t, t); x -= t + 2;
            _bEsq = new Rectangle(x, y, t, t);

            if (!_mouseDentro || !Editavel) return;

            // ESCAPADOS. Na primeira versao entraram como caracteres literais e
            // nao sobreviveram a gravacao do arquivo: os botoes apareciam como
            // caixas vazias. O aviso ja estava escrito no proprio codigo, em
            // SettingsForm, e mesmo assim foi o que aconteceu.
            //
            // E76B seta esquerda, E76C seta direita, E711 o "x" de remover.
            string[] glifos = { "\uE76B", "\uE76C", "\uE711", "\uE740" };
            Rectangle[] areas = { _bEsq, _bDir, _bRem, _bTam };

            for (int i = 0; i < 4; i++)
                {
                    bool ativo = (_sobre == i);
                    if (ativo)
                        using (GraphicsPath p = Ui.RoundRect(areas[i], 4))
                        using (SolidBrush b = new SolidBrush(Ui.Hover))
                            g.FillPath(b, p);

                    Color c = ativo ? (i == 2 ? Ui.Danger : Ui.Text) : Ui.Faint;
                    SizeF ts = g.MeasureString(glifos[i], Ui.FontGlyph8);
                    using (SolidBrush b = new SolidBrush(c))
                        g.DrawString(glifos[i], Ui.FontGlyph8, b,
                            areas[i].X + (t - ts.Width) / 2f, areas[i].Y + (t - ts.Height) / 2f);
                }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            // Os cantos, que ficam fora do retangulo arredondado. Lido do Ui na
            // hora, para acompanhar troca de tema.
            using (SolidBrush fundo = new SolidBrush(Ui.Window))
                g.FillRectangle(fundo, e.ClipRectangle);

            Ui.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color cor = Cor;

            // Cartao alto sobra area util abaixo do valor: a curva desce e ganha
            // eixo e grade. No pequeno ela continua sendo pano de fundo.
            bool detalhado = Height >= 140;
            int topo = detalhado ? 66 : Height / 2;

            using (GraphicsPath p = Ui.RoundRect(r, Ui.Radius))
            {
                using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillPath(b, p);

                Region antigo = g.Clip;
                g.SetClip(p, CombineMode.Intersect);
                DesenharSerie(g, cor, topo, detalhado);
                g.Clip = antigo;

                using (Pen pen = new Pen(Ui.Border)) g.DrawPath(pen, p);
            }

            // O rotulo cede espaco quando os botoes estao a mostra, em vez de
            // ficar por baixo deles.
            int recuo = (_mouseDentro && Editavel) ? 106 : 24;
            TextRenderer.DrawText(g, Titulo, Ui.FontSmall, new Rectangle(12, 8, Width - recuo, 15),
                Ui.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            DesenharBotoes(g);

            if (!string.IsNullOrEmpty(Sub))
                TextRenderer.DrawText(g, Sub, Ui.FontSmall, new Rectangle(12, Height - 20, Width - 24, 15),
                    Ui.Faint, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            string txt = Formatado();
            Size ts = TextRenderer.MeasureText(g, txt, Ui.FontValue);
            int y = 28;
            TextRenderer.DrawText(g, txt, Ui.FontValue, new Point(10, y), cor);

            if (!string.IsNullOrEmpty(Unidade))
                TextRenderer.DrawText(g, Unidade, Ui.FontSmall,
                    new Point(10 + ts.Width - 4, y + ts.Height - 15), Ui.Muted);
        }

        /// <summary>
        /// A curva, escalada pelo proprio minimo e maximo da janela com folga de 5%.
        ///
        /// Escalar de zero ao maximo achataria justamente o que interessa: uma
        /// CPU que oscila entre 38 e 42 graus viraria uma reta em cima do eixo,
        /// e a variacao - o unico motivo de existir a curva - desapareceria.
        ///
        /// O eixo do tempo e proporcional: dez minutos de serie vistos numa
        /// janela de seis horas ocupam a ponta direita e deixam o resto vazio.
        /// Esticar o que existe por toda a largura mostraria uma escala de tempo
        /// que nao e a escolhida.
        /// </summary>
        private void DesenharSerie(Graphics g, Color cor, int topo, bool detalhado)
        {
            int n = MetricHistory.Janela(SensorId, Janela, ref _buf);

            float min = float.MaxValue, max = float.MinValue;
            double soma = 0;
            int lidos = 0;
            for (int i = 0; i < n; i++)
            {
                float v = _buf[i];
                if (float.IsNaN(v)) continue;
                lidos++;
                soma += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (lidos < 2) return;
            float media = (float)(soma / lidos);

            float faixa = max - min;
            float baixo = min, alto = max;
            if (faixa < 0.001f) { baixo -= 1; alto += 1; }
            else { baixo -= faixa * 0.05f; alto += faixa * 0.05f; }
            faixa = alto - baixo;

            int alt = Height - topo;
            if (alt < 8) return;

            if (detalhado) DesenharGrade(g, topo, alt);

            // Segmentos separados nas falhas: com o aplicativo desligado nao ha
            // leitura, e ligar os dois lados do buraco desenharia uma variacao
            // que ninguem mediu.
            List<PointF> seg = new List<PointF>(n);
            for (int i = 0; i < n; i++)
            {
                float v = _buf[i];
                if (float.IsNaN(v)) { Traco(g, seg, cor); seg.Clear(); continue; }
                float x = (float)i / (n - 1) * Width;
                float yv = topo + alt - (v - baixo) / faixa * alt;
                seg.Add(new PointF(x, yv));
            }
            Traco(g, seg, cor);

            if (detalhado) DesenharEixo(g, topo, alt, min, media, max);
            if (detalhado && _hoverX >= 0)
                DesenharDetalhe(g, cor, topo, alt, n, baixo, faixa);
        }

        /// <summary>
        /// O ponto sob o ponteiro: valor e hora do balde apontado.
        ///
        /// A curva responde "isso e normal?" pela forma. A partir dai vem sempre
        /// a segunda pergunta - "normal quando, e quanto exatamente?" - e ate
        /// aqui ela nao tinha resposta: dava para ver que houve um pico, nao a
        /// que horas nem de quanto.
        ///
        /// Balde sem leitura nao mostra nada. A falha ja aparece como quebra na
        /// linha, e inventar um numero para ela seria desmentir o proprio
        /// desenho.
        /// </summary>
        private void DesenharDetalhe(Graphics g, Color cor, int topo, int alt,
                                     int n, float baixo, float faixa)
        {
            if (n < 2) return;

            int i = (int)Math.Round((double)_hoverX / Width * (n - 1));
            if (i < 0) i = 0;
            if (i > n - 1) i = n - 1;

            float v = _buf[i];
            if (float.IsNaN(v)) return;

            float x = (float)i / (n - 1) * Width;
            float y = topo + alt - (v - baixo) / faixa * alt;

            using (Pen p = new Pen(Color.FromArgb(70, cor)))
                g.DrawLine(p, x, topo, x, topo + alt);

            using (SolidBrush b = new SolidBrush(cor))
                g.FillEllipse(b, x - 3f, y - 3f, 6f, 6f);
            using (Pen p = new Pen(Ui.Surface, 1.5f))
                g.DrawEllipse(p, x - 3f, y - 3f, 6f, 6f);

            // Segundos so na janela curta: em seis horas cada balde e um ponto de
            // menos de um pixel, e anunciar o segundo exato prometeria uma
            // precisao que a largura da tela nao tem.
            DateTime t = MetricHistory.FimDaJanela().AddSeconds(-(double)(n - 1 - i) * MetricHistory.PassoSeg);
            string txt = Formatar(v) + (string.IsNullOrEmpty(Unidade) ? "" : " " + Unidade) +
                         "   " + t.ToString(Janela <= 600 ? "HH:mm:ss" : "HH:mm");

            Size ts = TextRenderer.MeasureText(g, txt, Ui.FontSmall);
            int w = ts.Width + 14, h = ts.Height + 8;

            // Acima do ponto por padrao; abaixo quando nao cabe. Grudado na
            // borda de cima, o balao sairia do desenho e tamparia o rotulo.
            int bx = (int)Math.Round(x) - w / 2;
            int by = (int)Math.Round(y) - h - 10;
            if (by < topo + 2) by = (int)Math.Round(y) + 10;
            // A faixa de baixo e do rodape do cartao, que e desenhado DEPOIS
            // desta rotina: encostar ali poria o rodape por cima do balao.
            if (by + h > Height - 22) by = Height - 22 - h;
            if (bx < 2) bx = 2;
            if (bx + w > Width - 2) bx = Width - 2 - w;

            Rectangle balao = new Rectangle(bx, by, w, h);
            using (GraphicsPath gp = Ui.RoundRect(balao, 4))
            {
                using (SolidBrush b = new SolidBrush(Ui.Window)) g.FillPath(b, gp);
                using (Pen p = new Pen(Ui.Border)) g.DrawPath(p, gp);
            }
            TextRenderer.DrawText(g, txt, Ui.FontSmall, balao, Ui.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void Traco(Graphics g, List<PointF> seg, Color cor)
        {
            if (seg.Count < 2) return;
            PointF[] pts = seg.ToArray();

            PointF[] area = new PointF[pts.Length + 2];
            Array.Copy(pts, area, pts.Length);
            area[pts.Length] = new PointF(pts[pts.Length - 1].X, Height);
            area[pts.Length + 1] = new PointF(pts[0].X, Height);

            using (SolidBrush b = new SolidBrush(Color.FromArgb(38, cor)))
                g.FillPolygon(b, area);
            using (Pen p = new Pen(Color.FromArgb(150, cor), 1.5f))
                g.DrawLines(p, pts);
        }

        /// <summary>Tres linhas de apoio, para ler altura sem contar pixels.</summary>
        private void DesenharGrade(Graphics g, int topo, int alt)
        {
            using (Pen p = new Pen(Color.FromArgb(40, Ui.Border)))
                for (int k = 1; k <= 3; k++)
                {
                    int y = topo + alt * k / 4;
                    g.DrawLine(p, 1, y, Width - 2, y);
                }
        }

        /// <summary>
        /// Minimo, media e maximo da janela desenhada.
        ///
        /// Antes eram dois numeros soltos, um em cada ponta do eixo, que serviam
        /// de escala: diziam ate onde a curva sobe, nao o que aconteceu. A media
        /// e o que separa "chegou a 84" de "vive em 84" - e a diferenca entre um
        /// pico e um problema.
        ///
        /// Numa linha so, e nao nas duas pontas: os tres numeros juntos se leem
        /// de uma vez e se comparam entre si, que e para o que servem. Como min
        /// e max continuam ali, a escala do desenho tambem continua legivel - a
        /// curva vai de um ao outro, com 5% de folga.
        ///
        /// Somem quando nao cabem. Meia linha de estatistica nao informa nada e
        /// ainda atravessa o desenho.
        /// </summary>
        private void DesenharEixo(Graphics g, int topo, int alt, float min, float media, float max)
        {
            // Tres formas, da mais completa a mais curta. Um bloco de 140 px na
            // tela de bordo nao comporta os tres numeros, e desistir de todos
            // por causa disso seria jogar fora o que caberia: o maximo e a media
            // sozinhos ja respondem "chegou aonde" e "vive onde".
            string[] formas = new string[]
            {
                T.StatMin + " " + Formatar(min) + "    " +
                T.StatAvg + " " + Formatar(media) + "    " +
                T.StatMax + " " + Formatar(max),

                T.StatMax + " " + Formatar(max) + "  ·  " + T.StatAvg + " " + Formatar(media),

                T.StatMax + " " + Formatar(max),
            };

            foreach (string txt in formas)
            {
                if (TextRenderer.MeasureText(g, txt, Ui.FontSmall).Width > Width - 20) continue;
                TextRenderer.DrawText(g, txt, Ui.FontSmall,
                    new Rectangle(10, topo + 2, Width - 20, 14), Ui.Faint,
                    TextFormatFlags.Right | TextFormatFlags.NoPadding);
                return;
            }
        }
    }

    /// <summary>
    /// Escolha das leituras que vao para a aba Metricas.
    ///
    /// Nao e "todos os sensores": uma maquina publica centenas, e uma grade com
    /// 300 cartoes nao e um painel, e a mesma lista de sempre com cores. A
    /// selecao pega, por categoria de hardware e na ordem que interessa, uma
    /// leitura de cada grandeza - temperatura, uso, frequencia, potencia,
    /// rotacao. Da o retrato da maquina em uma tela.
    /// </summary>
    public static class MetricPicker
    {
        /// <summary>
        /// Larguras em colunas e alturas dos tres tamanhos de cartao.
        ///
        /// O grande e mais alto, e nao so mais largo: esticar a curva na
        /// horizontal aumenta a resolucao no tempo mas achata a variacao, que e
        /// justamente o que alguem quer ver de perto ao aumentar um grafico.
        /// </summary>
        public static int Colunas(int tamanho)
        {
            if (tamanho >= 2) return 4;
            if (tamanho == 1) return 2;
            return 1;
        }

        public static int Altura(int tamanho)
        {
            if (tamanho >= 2) return 220;
            if (tamanho == 1) return 140;
            return 104;
        }

        // ---------------- nomes ----------------

        private static readonly Regex Enumeracao = new Regex(@"^[A-Za-z0-9 ]{1,12}\[#\d+\]\s*:\s*");
        private static readonly Regex Espacos = new Regex(@"\s{2,}");

        /// <summary>
        /// Rotulo do cartao, com o nome que a fonte da a LEITURA.
        ///
        /// A primeira versao usava o rotulo completo, que traz o dispositivo
        /// junto: sobrava "Enhanced - CPU (Tctl/Tdie) ..." num cartao de 176 px,
        /// ou seja, o pedaco do meio de uma frase. Aqui vale o nome do sensor,
        /// limpo do que a fonte acrescenta, e traduzido quando e uma leitura
        /// conhecida. O que nao esta na tabela passa limpo e inteiro - nenhuma
        /// maquina depende de constar dela.
        /// </summary>
        public static string Rotulo(SensorEntry s)
        {
            if (s == null) return "";
            string bruto = string.IsNullOrEmpty(s.Name) ? s.Label : s.Name;
            return Amigavel(bruto, s.Type.ToString());
        }

        /// <summary>
        /// As leituras em destaque da tela de bordo.
        ///
        /// Mora aqui, e nao na janela, porque quem precisa saber quais sao NAO e
        /// so quem desenha: o historico e alimentado pelo ciclo do mostrador, com
        /// a janela fechada, e so grava serie de leitura acompanhada. Enquanto a
        /// escolha viveu dentro do formulario, os blocos abriam sem curva atras -
        /// ninguem tinha dito ao MetricHistory que aquelas leituras interessavam,
        /// e o "comparado a que" que justifica a tela inteira nao existia.
        ///
        /// A ordem das preferencias importa mais que parece. Pegar a primeira do
        /// tipo dava resultado errado onde mais doi: numa Radeon a primeira
        /// leitura de uso e "D3D 3D", que mede o que o Direct3D pediu, nao o que
        /// a placa fez.
        /// </summary>
        private static readonly string[][] Preferencias = new string[][]
        {
            new string[] { "CPU",     "Temperature", "tctl", "package", "core" },
            new string[] { "CPU",     "Load",        "total", "cpu core" },
            new string[] { "GPU",     "Temperature", "gpu core", "gpu temperature", "hot spot" },

            // "GPU Core" e a leitura certa - a atividade do nucleo, que a NVIDIA
            // publica bem. Em Radeon antiga ela sai zerada quando a camada ADL
            // nao responde, e ai quem mede o trabalho e o contador do Direct3D.
            new string[] { "GPU",     "Load",        "gpu core", "d3d 3d", "gpu utilization" },

            // Categoria "*": vale qualquer uma. As duas fontes classificam a
            // memoria em grupos diferentes, e exigir "Memória" fazia o bloco
            // simplesmente nao existir quando o HWiNFO estava no comando.
            new string[] { "*",       "Load",        "physical memory", "memory load", "memory" },
        };

        public static List<SensorEntry> Destaques(List<SensorEntry> lista)
        {
            List<SensorEntry> saida = new List<SensorEntry>();
            if (lista == null) return saida;

            foreach (string[] alvo in Preferencias)
            {
                SensorEntry achado = Melhor(lista, alvo, saida);
                if (achado != null) saida.Add(achado);
            }
            return saida;
        }

        private static SensorEntry Melhor(List<SensorEntry> lista, string[] alvo,
                                          List<SensorEntry> jaEscolhidos)
        {
            // Duas passadas pela lista de preferencia: a primeira so aceita quem
            // esta REPORTANDO, a segunda aceita zero.
            //
            // "GPU Core" e a leitura certa do uso da placa, e numa Radeon antiga
            // ela sai zerada porque a camada ADL nao responde - enquanto o
            // contador do Direct3D, logo atras na lista, marcava 32%. Preferir
            // cegamente a primeira punha um bloco parado em 0,0% no lugar mais
            // visivel da tela, ao lado de quatro que se mexiam.
            //
            // Se TUDO estiver zerado a placa esta mesmo parada, e ai a primeira
            // preferencia volta a valer - que e o comportamento certo.
            for (int exigente = 1; exigente >= 0; exigente--)
                for (int p = 2; p < alvo.Length; p++)
                    foreach (SensorEntry s in lista)
                    {
                        if (!Casa(s, alvo[0], alvo[1]) || jaEscolhidos.Contains(s)) continue;
                        if (s.Name == null || !s.Value.HasValue) continue;
                        if (exigente == 1 && s.Value.Value == 0f) continue;
                        if (s.Name.IndexOf(alvo[p], StringComparison.OrdinalIgnoreCase) < 0) continue;
                        return s;
                    }

            foreach (SensorEntry s in lista)
                if (Casa(s, alvo[0], alvo[1]) && !jaEscolhidos.Contains(s) && s.Value.HasValue)
                    return s;

            return null;
        }

        private static bool Casa(SensorEntry s, string categoria, string tipo)
        {
            if (s == null) return false;
            if (categoria != "*" && s.Category != categoria) return false;
            return string.Equals(s.Type.ToString(), tipo, StringComparison.Ordinal);
        }

        // ---------------- conjuntos prontos ----------------

        /// <summary>
        /// Uma regra de conjunto: o que pescar da lista de sensores.
        ///
        /// Tres formas, porque as perguntas sao de tres naturezas. "A taxa de
        /// quadros" e uma leitura especifica, com identificador fixo. "A
        /// temperatura da CPU" e uma entre varias candidatas, e a preferencia
        /// decide qual. "Todas as ventoinhas" nao tem candidata: sao todas.
        /// </summary>
        public class Regra
        {
            public string Id;            // leitura fixa, como rtss:fps
            public string Categoria;     // "CPU", "GPU", "*"
            public string Tipo;          // "Temperature", "Fan", ...
            public bool Todas;           // pega todas as que casarem
            public string[] Preferidas;  // desempate quando ha varias

            public static Regra Fixa(string id)
            {
                Regra r = new Regra(); r.Id = id; return r;
            }

            public static Regra Uma(string cat, string tipo, params string[] pref)
            {
                Regra r = new Regra();
                r.Categoria = cat; r.Tipo = tipo; r.Preferidas = pref;
                return r;
            }

            public static Regra Todos(string cat, string tipo)
            {
                Regra r = new Regra();
                r.Categoria = cat; r.Tipo = tipo; r.Todas = true;
                return r;
            }
        }

        /// <summary>Um ponto de partida com nome.</summary>
        public class Conjunto
        {
            public readonly string Chave;
            public readonly Regra[] Regras;
            public Conjunto(string chave, Regra[] regras) { Chave = chave; Regras = regras; }
            public string Nome { get { return T.NomeDoConjunto(Chave); } }
        }

        /// <summary>
        /// Os conjuntos oferecidos, cada um respondendo a uma pergunta diferente.
        ///
        /// Nao sao variacoes de gosto: sao recortes. "Jogos" pergunta se o jogo
        /// esta fluido e o que esta segurando; "Termico" pergunta o que esquenta
        /// e quanto; "Silencioso" pergunta por que a ventoinha esta acelerando.
        /// Dois conjuntos que respondem a mesma pergunta com a mesma lista nao
        /// sao dois conjuntos - sao um, com dois nomes.
        /// </summary>
        public static readonly Conjunto[] Conjuntos = new Conjunto[]
        {
            new Conjunto("auto", null),   // a selecao automatica, por peca

            new Conjunto("jogos", new Regra[]
            {
                Regra.Fixa(Rtss.Prefixo + "fps"),
                Regra.Fixa(Rtss.Prefixo + "fps.min"),
                Regra.Fixa(Rtss.Prefixo + "frametime"),
                Regra.Uma("GPU", "Load", "gpu core", "d3d 3d"),
                Regra.Uma("GPU", "Temperature", "gpu core", "hot spot"),
                Regra.Uma("GPU", "Clock", "gpu core"),
                Regra.Uma("CPU", "Load", "total"),
                Regra.Uma("CPU", "Temperature", "tctl", "package"),
            }),

            // Termico: o que esquenta e o quanto de energia entra para tanto. A
            // potencia esta aqui porque e a CAUSA da temperatura - ver os dois
            // juntos e o que distingue "esquentou porque esta trabalhando" de
            // "esquentou parado", que e defeito.
            new Conjunto("termico", new Regra[]
            {
                Regra.Todos("*", "Temperature"),
                Regra.Uma("CPU", "Power", "package", "core"),
                Regra.Uma("GPU", "Power"),
            }),

            // Silencioso: a ventoinha e o que ela obedece. Sem as temperaturas
            // que a comandam, a rotacao e um numero sem causa.
            new Conjunto("silencioso", new Regra[]
            {
                Regra.Todos("*", "Fan"),
                Regra.Todos("*", "Control"),
                Regra.Uma("CPU", "Temperature", "tctl", "package"),
                Regra.Uma("GPU", "Temperature", "gpu core"),
            }),
        };

        /// <summary>Quantos cartoes um conjunto pode trazer.</summary>
        public const int TetoDoConjunto = 12;

        /// <summary>
        /// Aplica um conjunto sobre a lista de sensores desta maquina.
        ///
        /// O teto existe porque uma regra "todas as temperaturas" numa maquina
        /// com quatro discos e duas placas devolve trinta cartoes, e trinta
        /// cartoes nao sao um painel - sao a mesma lista de sempre, com cores.
        /// </summary>
        public static List<SensorEntry> Montar(Conjunto c, List<SensorEntry> lista)
        {
            List<SensorEntry> saida = new List<SensorEntry>();
            if (lista == null) return saida;
            if (c == null || c.Regras == null) return Escolher(lista, 5);

            foreach (Regra r in c.Regras)
            {
                if (saida.Count >= TetoDoConjunto) break;

                if (!string.IsNullOrEmpty(r.Id))
                {
                    foreach (SensorEntry s in lista)
                        if (s != null && s.Id == r.Id && !saida.Contains(s)) { saida.Add(s); break; }
                    continue;
                }

                if (r.Todas)
                {
                    foreach (SensorEntry s in lista)
                    {
                        if (saida.Count >= TetoDoConjunto) break;
                        if (Casa(s, r.Categoria, r.Tipo) && !saida.Contains(s) && s.Value.HasValue)
                            saida.Add(s);
                    }
                    continue;
                }

                string[] alvo = new string[2 + (r.Preferidas == null ? 0 : r.Preferidas.Length)];
                alvo[0] = r.Categoria; alvo[1] = r.Tipo;
                if (r.Preferidas != null) r.Preferidas.CopyTo(alvo, 2);

                SensorEntry achado = Melhor(lista, alvo, saida);
                if (achado != null) saida.Add(achado);
            }
            return saida;
        }

        /// <summary>
        /// Rotulo sem o sufixo do agregado, para cartao estreito.
        ///
        /// Num bloco de 140 px, "Uso dos nucleos · media de 12" sai cortado em
        /// "Uso dos nucleos · media d..." - o pedaco que ficou e o que menos
        /// importa, e o que foi cortado tambem. Sem o sufixo o nome inteiro
        /// cabe, e quantos nucleos foram somados nao e o que se pergunta numa
        /// tela de relance.
        /// </summary>
        public static string RotuloCurto(SensorEntry s)
        {
            string r = Rotulo(s);
            int i = r.IndexOf(" · ", StringComparison.Ordinal);
            return i > 0 ? r.Substring(0, i) : r;
        }

        /// <summary>Sem o tipo em maos: so a busca pelo nome.</summary>
        public static string Amigavel(string bruto)
        {
            return Amigavel(bruto, null);
        }

        /// <summary>
        /// Nome amigavel, desambiguado pelo tipo da leitura.
        ///
        /// O nome sozinho nao identifica a grandeza. Uma Radeon publica DUAS
        /// leituras chamadas "GPU Core" - a temperatura do nucleo e o clock dele
        /// - e a tabela, consultada so pelo nome, respondia "Temperatura da GPU"
        /// para as duas. O clock da GPU estava na lista o tempo todo, com nome
        /// de temperatura, o que e pior do que faltar: quem procurava desistia
        /// depois de achar.
        ///
        /// A busca tenta primeiro a chave com o tipo na frente ("clock:gpu
        /// core") e so entao a chave nua. Assim o caso ambiguo se resolve sem
        /// obrigar as outras cinquenta entradas a declarar um tipo que nunca
        /// precisaram.
        /// </summary>
        public static string Amigavel(string bruto, string tipo)
        {
            if (string.IsNullOrEmpty(bruto)) return "";

            // "CPU [#0]: ..." - a enumeracao da fonte, que nao nomeia nada.
            string n = Enumeracao.Replace(bruto.Trim(), "");

            // "Grupo: Subgrupo: Leitura" - fica a leitura.
            int dp = n.LastIndexOf(':');
            if (dp >= 0 && dp < n.Length - 1) n = n.Substring(dp + 1);

            n = Espacos.Replace(n, " ").Trim();
            if (n.Length == 0) return bruto.Trim();

            // Sufixo do agregado: "Core Temperatures · média de 6". Sai da busca
            // e volta no fim, senao nenhum agregado acharia a tabela.
            string sufixo = "";
            int sep = n.IndexOf(" · ", StringComparison.Ordinal);
            if (sep > 0) { sufixo = n.Substring(sep); n = n.Substring(0, sep); }

            string chave = n.ToLowerInvariant();
            string[] par;

            if (!string.IsNullOrEmpty(tipo) &&
                Tabela.TryGetValue(tipo.ToLowerInvariant() + ":" + chave, out par))
                return (T.Pt ? par[0] : par[1]) + sufixo;

            if (Tabela.TryGetValue(chave, out par))
                return (T.Pt ? par[0] : par[1]) + sufixo;

            return n + sufixo;
        }

        /// <summary>
        /// Nomes conhecidos, em portugues e ingles.
        ///
        /// Cobre o que as duas fontes publicam nas maquinas comuns; o que faltar
        /// aparece com o nome original, que ja e legivel. Nao ha aqui nenhuma
        /// suposicao sobre a maquina - a tabela e um enfeite sobre um caminho
        /// que funciona sem ela.
        /// </summary>
        private static readonly string[] Bruta =
        {
            // processador
            "cpu (tctl/tdie)",        "Temperatura do processador", "CPU temperature",
            "core (tctl/tdie)",       "Temperatura do processador", "CPU temperature",
            "cpu package",            "Temperatura do pacote",      "Package temperature",
            "core max",               "Núcleo mais quente",         "Hottest core",
            "core average",           "Média dos núcleos",          "Core average",
            "cpu die (average)",      "Média do die",               "Die average",
            "cpu ccd1 (tdie)",        "Temperatura do CCD1",        "CCD1 temperature",
            "cpu ccd2 (tdie)",        "Temperatura do CCD2",        "CCD2 temperature",
            "total cpu usage",        "Uso do processador",         "CPU usage",
            "cpu total",              "Uso do processador",         "CPU usage",
            "max cpu/thread usage",   "Uso máximo de thread",       "Max thread usage",
            "cpu package power",      "Consumo do processador",     "CPU power",
            "core+soc power",         "Consumo núcleos + SoC",      "Core+SoC power",
            "bus clock",              "Clock do barramento",        "Bus clock",
            "core clocks",            "Clock dos núcleos",          "Core clock",
            "core clock",             "Clock dos núcleos",          "Core clock",
            "cpu clock",              "Clock do processador",       "CPU clock",

            // Nucleos ja agregados. O Condense junta as dezenas de leituras por
            // nucleo numa media so, e o nome que sobra e o do grupo, sem indice.
            //
            // Quase todos precisam do tipo na chave: a LibreHardwareMonitor
            // chama de "Core" tanto o clock quanto o multiplicador, e de "CPU
            // Core" tanto a temperatura quanto o uso. Sem o tipo, a primeira
            // entrada da tabela responderia por todas - foi assim que um clock
            // de GPU virou temperatura.
            "clock:core",             "Clock dos núcleos",          "Core clock",
            "clock:cpu core",         "Clock dos núcleos",          "Core clock",
            "factor:core",            "Multiplicador dos núcleos",  "Core ratio",
            "load:core",              "Uso dos núcleos",            "Core usage",
            "load:cpu core",          "Uso dos núcleos",            "Core usage",
            "temperature:core",       "Temperatura dos núcleos",    "Core temperature",
            "temperature:cpu core",   "Temperatura dos núcleos",    "Core temperature",
            "power:core",             "Consumo dos núcleos",        "Core power",
            "power:core (smu)",       "Consumo dos núcleos",        "Core power",
            "voltage:core",           "Tensão dos núcleos",         "Core voltage",
            "voltage:core (smu)",     "Tensão dos núcleos",         "Core voltage",
            "voltage:core vid",       "VID dos núcleos",            "Core VID",

            "core effective clock",   "Clock efetivo dos núcleos",  "Core effective clock",
            "core temperature",       "Temperatura dos núcleos",    "Core temperature",
            "core temperatures",      "Temperatura dos núcleos",    "Core temperature",
            "core usage",             "Uso dos núcleos",            "Core usage",
            "core utility",           "Utilização dos núcleos",     "Core utility",
            "core power",             "Consumo dos núcleos",        "Core power",
            "core voltage",           "Tensão dos núcleos",         "Core voltage",
            "core vid",               "VID dos núcleos",            "Core VID",
            "core ratio",             "Multiplicador dos núcleos",  "Core ratio",
            "core distance to tjmax", "Distância do TjMAX",         "Distance to TjMAX",
            "cpu core distance to tjmax", "Distância do TjMAX",     "Distance to TjMAX",
            "vcore",                  "Tensão do núcleo",           "Core voltage",
            "cpu vcore",              "Tensão do núcleo",           "Core voltage",
            "thermal throttling (prochot ext)", "Limitação térmica", "Thermal throttling",

            // video
            "gpu temperature",        "Temperatura da GPU",         "GPU temperature",
            "gpu thermal diode",      "Temperatura da GPU",         "GPU temperature",
            // "GPU Core" e "GPU Memory" saem duas vezes cada na Radeon: uma como
            // temperatura, outra como clock. Sem o tipo na chave o clock herdava
            // o nome da temperatura e sumia da lista sem sair dela.
            "temperature:gpu core",   "Temperatura da GPU",         "GPU temperature",
            "clock:gpu core",         "Clock da GPU",               "GPU clock",
            "load:gpu core",          "Uso da GPU",                 "GPU usage",
            "load:d3d 3d",            "Uso da GPU (3D)",            "GPU usage (3D)",
            "temperature:gpu memory", "Temperatura da memória de vídeo", "GPU memory temperature",
            "clock:gpu memory",       "Clock da memória de vídeo",  "GPU memory clock",
            "gpu hot spot temperature", "Ponto quente da GPU",      "GPU hot spot",
            "gpu memory temperature", "Temperatura da memória de vídeo", "GPU memory temperature",
            "gpu clock",              "Clock da GPU",               "GPU clock",
            "gpu core clock",         "Clock da GPU",               "GPU clock",
            "gpu memory clock",       "Clock da memória de vídeo",  "GPU memory clock",
            "gpu utilization",        "Uso da GPU",                 "GPU usage",
            "gpu core load",          "Uso da GPU",                 "GPU usage",
            "gpu d3d usage",          "Uso 3D",                     "3D usage",
            "gpu memory controller utilization", "Uso do controlador de memória", "Memory controller usage",
            "gpu i/o utilization",    "Uso de entrada e saída",     "I/O usage",
            "gpu fan",                "Ventoinha da GPU",           "GPU fan",
            "gpu fan (odn)",          "Ventoinha da GPU",           "GPU fan",
            "gpu fan speed",          "Ventoinha da GPU",           "GPU fan",
            "gpu fan pwm",            "Ventoinha da GPU (PWM)",     "GPU fan (PWM)",
            "gpu power",              "Consumo da GPU",             "GPU power",
            "gpu core power",         "Consumo da GPU",             "GPU power",
            "gpu core voltage (vddc)", "Tensão do núcleo da GPU",   "GPU core voltage",
            "gpu core current (vddcr_gfx)", "Corrente do núcleo da GPU", "GPU core current",
            "gpu memory used",        "Memória de vídeo em uso",    "GPU memory used",
            "gpu memory usage",       "Uso da memória de vídeo",    "GPU memory usage",

            // memoria
            "load:memory",            "Uso da memória",             "Memory usage",
            "load:virtual memory",    "Uso da memória virtual",     "Virtual memory usage",
            "memory clock",           "Clock da memória",           "Memory clock",
            "memory used",            "Memória em uso",             "Memory used",
            "memory available",       "Memória livre",              "Memory available",
            "physical memory used",   "Memória em uso",             "Memory used",
            "physical memory available", "Memória livre",           "Memory available",
            "physical memory load",   "Uso da memória",             "Memory load",
            "virtual memory committed", "Memória virtual em uso",   "Virtual memory used",

            // placa-mae
            "cpu fan",                "Ventoinha do processador",   "CPU fan",
            "system fan",             "Ventoinha do gabinete",      "Case fan",
            "chassis fan",            "Ventoinha do gabinete",      "Case fan",
            "motherboard",            "Temperatura da placa-mãe",   "Motherboard temperature",
            "vrm mos",                "Temperatura do VRM",         "VRM temperature",

            // armazenamento
            "drive temperature",      "Temperatura do disco",       "Drive temperature",
            "read activity",          "Atividade de leitura",       "Read activity",
            "write activity",         "Atividade de escrita",       "Write activity",
            "total activity",         "Atividade do disco",         "Drive activity",
            "used space",             "Espaço usado",               "Used space",
            "total host writes",      "Total gravado",              "Total host writes",
            "total nand writes",      "Total gravado na NAND",      "Total NAND writes",
            "drive failure",          "Falha do disco",             "Drive failure",
            "drive warning",          "Aviso do disco",             "Drive warning",

            // rede
            "current dl rate",        "Taxa de download",           "Download rate",
            "current up rate",        "Taxa de upload",             "Upload rate",
            "total dl",               "Total baixado",              "Total downloaded",
            "total up",               "Total enviado",              "Total uploaded",
            "total errors",           "Erros no total",             "Total errors",
        };

        private static Dictionary<string, string[]> _tabela;

        private static Dictionary<string, string[]> Tabela
        {
            get
            {
                if (_tabela == null)
                {
                    Dictionary<string, string[]> d = new Dictionary<string, string[]>();
                    for (int i = 0; i + 2 < Bruta.Length; i += 3)
                        d[Bruta[i]] = new string[] { Bruta[i + 1], Bruta[i + 2] };
                    _tabela = d;
                }
                return _tabela;
            }
        }

        /// <summary>Linha de baixo: categoria e a peca, com o nome ja encurtado.</summary>
        public static string Rodape(SensorEntry s)
        {
            if (s == null) return "";
            string cat = T.Category(string.IsNullOrEmpty(s.Category) ? "Outros" : s.Category);
            string hw = SystemInfo.Limpar(s.Hardware);
            return string.IsNullOrEmpty(hw) ? cat : cat + "  ·  " + hw;
        }

        /// <summary>
        /// Rodape das leituras de quadro, refeito a cada ciclo.
        ///
        /// A "peca" destas leituras e o jogo, e ele abre e fecha com a janela
        /// aberta. Todo o resto da grade mede hardware, que nao troca de nome no
        /// meio da sessao - por isso so esta categoria precisa de rodape vivo.
        /// </summary>
        public static string RodapeJogos()
        {
            return T.Category(Sensors.CategoriaJogos) + "  ·  " + Rtss.PecaAtual;
        }

        // ---------------- selecao automatica ----------------

        private static readonly string[] Ordem = { "CPU", "GPU", "Memória", "Placa-mãe", "Disco" };

        /// <summary>
        /// Grandezas na ordem em que interessam, com a temperatura primeiro.
        ///
        /// Comparadas pela forma NORMALIZADA. A primeira versao exigia "°C"
        /// literal, e o resultado foi uma grade automatica sem nenhuma
        /// temperatura - a leitura mais basica de todas faltando justamente na
        /// selecao que existe para dar o basico. As fontes publicam o grau ora
        /// como "°C", ora como "C", ora com o simbolo mastigado pela conversao
        /// de pagina de codigo, e exigir uma das formas perde as outras duas.
        /// </summary>
        private static readonly string[] Grandezas = { "C", "%", "MHZ", "W", "RPM" };

        /// <summary>Reduz a unidade a uma chave comparavel: "°C" e "C" batem.</summary>
        public static string Normalizar(string unidade)
        {
            if (string.IsNullOrEmpty(unidade)) return "";
            string s = unidade.Trim().ToUpperInvariant();

            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c) || c == '%' || c == '/') sb.Append(c);

            s = sb.ToString();
            if (s == "DEGC" || s == "ÂC") return "C";
            if (s == "DEGF") return "F";
            return s;
        }

        public static List<SensorEntry> Escolher(List<SensorEntry> todos, int porCategoria)
        {
            List<SensorEntry> saida = new List<SensorEntry>();
            if (todos == null) return saida;

            foreach (string cat in Ordem)
            {
                int n = 0;
                foreach (string unidade in Grandezas)
                {
                    if (n >= porCategoria) break;
                    SensorEntry achado = Primeiro(todos, cat, unidade);
                    if (achado != null) { saida.Add(achado); n++; }
                }
            }
            return saida;
        }

        private static SensorEntry Primeiro(List<SensorEntry> todos, string categoria, string unidade)
        {
            foreach (SensorEntry s in todos)
            {
                if (s == null || s.Category != categoria) continue;
                if (Normalizar(s.Unit) != unidade) continue;
                return s;
            }
            return null;
        }

        /// <summary>
        /// Faixas visuais por unidade, para o cartão mudar de cor sozinho.
        ///
        /// Sao os valores de senso comum de quem olha esses numeros: 80 °C
        /// incomoda, 90 preocupa. São apenas uma leitura de relance do cartão;
        /// não disparam notificações.
        /// </summary>
        public static void Faixas(string unidade, out float? atencao, out float? perigo)
        {
            atencao = null; perigo = null;
            string u = Normalizar(unidade);
            if (u.Length == 0) return;

            if (u == "C") { atencao = 80; perigo = 90; }
            else if (u == "F") { atencao = 176; perigo = 194; }
            else if (u == "%") { atencao = 85; perigo = 95; }
        }
    }
}
