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

        /// <summary>Faixas de cor. Nulo pinta tudo com a cor de enfase.</summary>
        public float? Atencao, Perigo;

        private const int Historico = 90;
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

            TextRenderer.DrawText(g, Titulo, Ui.FontSmall, new Rectangle(12, 8, Width - 24, 15),
                Ui.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

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
