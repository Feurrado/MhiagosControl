using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
    /// </summary>
    public class MetricCard : Control
    {
        public string Titulo = "";
        public string Sub = "";
        public string Unidade = "";

        /// <summary>Identificador da leitura, para gravar a escolha.</summary>
        public string SensorId = "";

        /// <summary>Acoes de edicao, disparadas pelos botoes do canto.</summary>
        public event EventHandler Remover, MoverEsquerda, MoverDireita, TrocarTamanho;

        private Rectangle _bRem, _bEsq, _bDir, _bTam;
        private int _sobre = -1;
        private bool _mouseDentro = false;

        /// <summary>Faixas de cor. Nulo pinta tudo com a cor de enfase.</summary>
        public float? Atencao, Perigo;

        /// <summary>
        /// Amostras guardadas por cartao. A 1 s por ciclo, sao 10 minutos.
        ///
        /// 90 amostras davam um minuto e meio - tempo suficiente para ver um
        /// pico, nao para ver se ele foi um evento ou o normal do dia. Dez
        /// minutos cabem em 2,4 KB por cartao e respondem essa segunda pergunta.
        /// </summary>
        private const int Historico = 600;
        private readonly float[] _serie = new float[Historico];
        private int _n = 0;
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

        public void Push(float? v)
        {
            _valor = v;
            if (v.HasValue)
            {
                if (_n < Historico) _serie[_n++] = v.Value;
                else
                {
                    Array.Copy(_serie, 1, _serie, 0, Historico - 1);
                    _serie[Historico - 1] = v.Value;
                }
            }
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
        private string Formatado()
        {
            if (!_valor.HasValue) return "--";
            float v = _valor.Value;
            if (Math.Abs(v) >= 100) return v.ToString("0");
            if (Math.Abs(v) >= 10) return v.ToString("0.0");
            return v.ToString("0.0");
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

            using (GraphicsPath p = Ui.RoundRect(r, Ui.Radius))
            {
                using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillPath(b, p);

                Region antigo = g.Clip;
                g.SetClip(p, CombineMode.Intersect);
                DesenharSerie(g, cor);
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
        /// A curva ocupa a metade de baixo e e escalada pelo proprio minimo e
        /// maximo da janela, com uma folga de 5%.
        ///
        /// Escalar de zero ao maximo achataria justamente o que interessa: uma
        /// CPU que oscila entre 38 e 42 graus viraria uma reta em cima do eixo,
        /// e a variacao - o unico motivo de existir a curva - desapareceria.
        /// </summary>
        private void DesenharSerie(Graphics g, Color cor)
        {
            if (_n < 2) return;

            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < _n; i++)
            {
                if (_serie[i] < min) min = _serie[i];
                if (_serie[i] > max) max = _serie[i];
            }
            float faixa = max - min;
            if (faixa < 0.001f) { min -= 1; max += 1; faixa = max - min; }
            else { min -= faixa * 0.05f; max += faixa * 0.05f; faixa = max - min; }

            int topo = Height / 2, alt = Height - topo;
            PointF[] pts = new PointF[_n];
            for (int i = 0; i < _n; i++)
            {
                float x = (_n == 1) ? 0 : (float)i / (_n - 1) * Width;
                float y = topo + alt - (_serie[i] - min) / faixa * alt;
                pts[i] = new PointF(x, y);
            }

            PointF[] area = new PointF[_n + 2];
            Array.Copy(pts, area, _n);
            area[_n] = new PointF(Width, Height);
            area[_n + 1] = new PointF(0, Height);

            using (SolidBrush b = new SolidBrush(Color.FromArgb(38, cor)))
                g.FillPolygon(b, area);
            using (Pen p = new Pen(Color.FromArgb(150, cor), 1.5f))
                g.DrawLines(p, pts);
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

        /// <summary>
        /// Rotulo do cartao: so a parte que nomeia a LEITURA.
        ///
        /// As fontes publicam "CPU [#0]: AMD Ryzen 5 5600X: Core Temperatures".
        /// Inteiro, o rotulo era cortado antes de chegar em "Core Temperatures"
        /// - o cartao dizia de qual peca e nunca dizia de que leitura, que e o
        /// contrario do util, porque a peca ja esta escrita embaixo.
        /// </summary>
        public static string Rotulo(SensorEntry s)
        {
            if (s == null || string.IsNullOrEmpty(s.Label)) return "";
            string t = s.Label;
            int i = t.LastIndexOf(':');
            return i >= 0 && i < t.Length - 1 ? t.Substring(i + 1).Trim() : t.Trim();
        }

        /// <summary>Linha de baixo: categoria e a peca, com o nome ja encurtado.</summary>
        public static string Rodape(SensorEntry s)
        {
            if (s == null) return "";
            string hw = SystemInfo.Limpar(s.Hardware);
            return string.IsNullOrEmpty(hw) ? s.Category : s.Category + "  ·  " + hw;
        }

        private static readonly string[] Ordem = { "CPU", "GPU", "Memória", "Placa-mãe", "Disco" };
        private static readonly string[] Grandezas = { "°C", "%", "MHz", "W", "RPM" };

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
                if (!string.Equals(s.Unit, unidade, StringComparison.OrdinalIgnoreCase)) continue;
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
            if (string.IsNullOrEmpty(unidade)) return;

            if (unidade == "°C") { atencao = 80; perigo = 90; }
            else if (unidade == "%") { atencao = 85; perigo = 95; }
        }
    }
}
