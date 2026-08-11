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

        private Rectangle _bRem, _bEsq, _bDir, _bTam;
        private int _sobre = -1;
        private bool _mouseDentro = false;

        /// <summary>Faixas de cor. Nulo pinta tudo com a cor de enfase.</summary>
        public float? Atencao, Perigo;

        private float[] _buf;
        private float? _valor;

        public MetricCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Ui.FontBase;
            Size = new Size(172, 104);
        }

        /// <summary>Leitura do ciclo corrente. A serie e alimentada em outro lugar.</summary>
        public void Push(float? v)
        {
            _valor = v;
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
            _mouseDentro = false; _sobre = -1; Cursor = Cursors.Default;
            Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int s = -1;
            if (_bEsq.Contains(e.Location)) s = 0;
            else if (_bDir.Contains(e.Location)) s = 1;
            else if (_bRem.Contains(e.Location)) s = 2;
            else if (_bTam.Contains(e.Location)) s = 3;

            if (s != _sobre)
            {
                _sobre = s;
                Cursor = s >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_bEsq.Contains(e.Location)) Disparar(MoverEsquerda);
            else if (_bDir.Contains(e.Location)) Disparar(MoverDireita);
            else if (_bTam.Contains(e.Location)) Disparar(TrocarTamanho);
            else if (_bRem.Contains(e.Location)) Disparar(Remover);
            base.OnMouseDown(e);
        }

        private void Disparar(EventHandler h) { if (h != null) h(this, EventArgs.Empty); }

        private void DesenharBotoes(Graphics g)
        {
            int t = 20, y = 6, x = Width - 8 - t;
            _bRem = new Rectangle(x, y, t, t); x -= t + 2;
            _bTam = new Rectangle(x, y, t, t); x -= t + 2;
            _bDir = new Rectangle(x, y, t, t); x -= t + 2;
            _bEsq = new Rectangle(x, y, t, t);

            if (!_mouseDentro) return;

            // ESCAPADOS. Na primeira versao entraram como caracteres literais e
            // nao sobreviveram a gravacao do arquivo: os botoes apareciam como
            // caixas vazias. O aviso ja estava escrito no proprio codigo, em
            // SettingsForm, e mesmo assim foi o que aconteceu.
            //
            // E76B seta esquerda, E76C seta direita, E711 o "x" de remover.
            string[] glifos = { "\uE76B", "\uE76C", "\uE711", "\uE740" };
            Rectangle[] areas = { _bEsq, _bDir, _bRem, _bTam };

            using (Font f = new Font("Segoe MDL2 Assets", 8f))
                for (int i = 0; i < 4; i++)
                {
                    bool ativo = (_sobre == i);
                    if (ativo)
                        using (GraphicsPath p = Ui.RoundRect(areas[i], 4))
                        using (SolidBrush b = new SolidBrush(Ui.Hover))
                            g.FillPath(b, p);

                    Color c = ativo ? (i == 2 ? Ui.Danger : Ui.Text) : Ui.Faint;
                    SizeF ts = g.MeasureString(glifos[i], f);
                    using (SolidBrush b = new SolidBrush(c))
                        g.DrawString(glifos[i], f, b,
                            areas[i].X + (t - ts.Width) / 2f, areas[i].Y + (t - ts.Height) / 2f);
                }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
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
            int recuo = _mouseDentro ? 106 : 24;
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
            int lidos = 0;
            for (int i = 0; i < n; i++)
            {
                float v = _buf[i];
                if (float.IsNaN(v)) continue;
                lidos++;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (lidos < 2) return;

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

            if (detalhado) DesenharEixo(g, topo, alt, min, max);
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

        /// <summary>Extremos da janela, para a curva ter escala e nao so forma.</summary>
        private void DesenharEixo(Graphics g, int topo, int alt, float min, float max)
        {
            TextRenderer.DrawText(g, Formatar(max), Ui.FontSmall,
                new Rectangle(Width - 78, topo + 2, 70, 14), Ui.Faint,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g, Formatar(min), Ui.FontSmall,
                new Rectangle(Width - 78, topo + alt - 18, 70, 14), Ui.Faint,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);
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
            return Amigavel(bruto);
        }

        public static string Amigavel(string bruto)
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

            string[] par;
            if (Tabela.TryGetValue(n.ToLowerInvariant(), out par))
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
            "vcore",                  "Tensão do núcleo",           "Core voltage",
            "cpu vcore",              "Tensão do núcleo",           "Core voltage",
            "thermal throttling (prochot ext)", "Limitação térmica", "Thermal throttling",

            // video
            "gpu temperature",        "Temperatura da GPU",         "GPU temperature",
            "gpu thermal diode",      "Temperatura da GPU",         "GPU temperature",
            "gpu core",               "Temperatura da GPU",         "GPU temperature",
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
        /// Limiares por unidade, para o cartao mudar de cor sozinho.
        ///
        /// Sao os valores de senso comum de quem olha esses numeros: 80 °C
        /// incomoda, 90 preocupa. Nao substituem os alertas configuraveis da
        /// aba Alertas - aqui e leitura de relance, la e disparo.
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
