using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Um cartao da pagina Baterias: o estado agora, o que a historia diz da
    /// duracao da carga e a curva das ultimas 48 horas.
    ///
    /// Pintado inteiro, como a tira da Visao geral: sao textos que mudam juntos
    /// a cada leitura, e rotulos filhos so trariam piscada e mais arranjo.
    /// </summary>
    public class CartaoDeBateria : Card
    {
        public const int Altura = 352;

        /// <summary>Altura de cada par rotulo e valor; cabe um valor em duas linhas.</summary>
        private const int Linha = 48;
        private const int Icone = 56;
        private const int LarguraMinimaDoGrafico = 300;
        private static readonly TimeSpan JanelaDoGrafico = TimeSpan.FromHours(48);

        private AnaliseDeBateria _a;
        private int? _autonomia;
        private string _assinatura;

        private readonly AnimadorDePilhas _animador;

        public CartaoDeBateria()
        {
            Title = null;
            _animador = new AnimadorDePilhas(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _animador.Dispose(); _dica.Dispose(); }
            base.Dispose(disposing);
        }

        // ---------------- clique no icone ----------------

        /// <summary>A imagem do aparelho, com folga: e ela que abre o app dele.</summary>
        private static readonly Rectangle ZonaDoIcone = new Rectangle(10, 8, Icone + 16, Icone + 16);
        private readonly ToolTip _dica = new ToolTip();
        private bool _sobreIcone = false;

        private Periferico ComoPeriferico()
        {
            return _a == null ? null : Periferico.Sem(_a.Nome, _a.Tipo, _a.Fonte, EstadoPeriferico.Normal);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool sobre = _a != null && ZonaDoIcone.Contains(e.Location);
            if (sobre == _sobreIcone) return;
            _sobreIcone = sobre;
            Cursor = sobre ? Cursors.Hand : Cursors.Default;
            Periferico p = ComoPeriferico();
            _dica.SetToolTip(this, !sobre || p == null ? null
                : p.Fonte == FontePeriferico.Bluetooth ? T.OpenBluetooth
                : T.OpenApp(Perifericos.NomeDoApp(p)));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _sobreIcone = false;
            Cursor = Cursors.Default;
            _dica.SetToolTip(this, null);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && _a != null && ZonaDoIcone.Contains(e.Location))
                Perifericos.Abrir(ComoPeriferico());
        }

        public string Chave { get { return _a != null ? _a.Chave : null; } }

        /// <summary>Troca os dados; so redesenha quando o texto ou a curva mudaram.</summary>
        public void Mostrar(AnaliseDeBateria a, int? autonomia)
        {
            string ass = Assinatura(a, autonomia);
            if (ass == _assinatura) return;
            _a = a; _autonomia = autonomia; _assinatura = ass;
            if (a != null) TransicaoDeCarga.Informar(a.Chave, a.Carregando && a.PctAtual.HasValue);
            Invalidate();
        }

        private static string Assinatura(AnaliseDeBateria a, int? autonomia)
        {
            if (a == null) return "";
            return string.Join("|", new string[] {
                a.Nome, a.PctAtual.ToString(), a.Carregando.ToString(), Texto(a.HorasRestantes),
                Texto(a.HorasPorCargaCheia), a.DescargasMedidas.ToString(), Texto(a.HorasAteEncher),
                a.SaudePorMes != null ? a.SaudePorMes.Count.ToString() : "0",
                a.Atual != null ? a.Atual.PctFim + "/" + (int)(a.Atual.UsoSegundos / 60) : "",
                a.UltimaCarga != null ? a.UltimaCarga.Fim.Ticks.ToString() : "",
                a.UltimaCompleta != null ? a.UltimaCompleta.Fim.Ticks.ToString() : "",
                a.Amostras != null ? a.Amostras.Length.ToString() : "0",
                ((int)(a.UsoTotalHoras * 60)).ToString(),
                autonomia.ToString(), DateTime.Now.ToString("HHmm") });   // a curva anda com o relogio
        }

        private static string Texto(double? v) { return v.HasValue ? v.Value.ToString("0.0", CultureInfo.InvariantCulture) : ""; }

        // ---------------- desenho ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _animador.ComecarQuadro();
            try { Pintar(e.Graphics); }
            finally { _animador.Ajustar(); }
        }

        private void Pintar(Graphics g)
        {
            if (_a == null) return;
            Ui.Smooth(g);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            Periferico p = _a.PctAtual.HasValue
                ? Periferico.Lido(_a.Nome, _a.Tipo, _a.Fonte, _a.PctAtual.Value, _a.Carregando)
                : Periferico.Sem(_a.Nome, _a.Tipo, _a.Fonte, EstadoPeriferico.Desligado);

            // cabecalho: imagem, nome, carga agora
            Image img = PeripheralStrip.Imagem(p);
            if (img != null) g.DrawImage(img, new Rectangle(18, 16, Icone, Icone));

            int tx = 18 + Icone + 14;
            TextRenderer.DrawText(g, _a.Nome, Ui.FontSection, new Point(tx, 16), Ui.Text, TextFormatFlags.NoPadding);

            int yVal = 44;
            string valor = _a.PctAtual.HasValue ? _a.PctAtual.Value + "%" : "--";
            // a mesma passagem suave da Visao geral ao entrar e sair do cabo
            bool animando;
            float carga = TransicaoDeCarga.Valor(_a.Chave, _a.Carregando && _a.PctAtual.HasValue, out animando);
            if (animando) _animador.Transicao();
            TextRenderer.DrawText(g, valor, PeripheralStrip.FontPct, new Point(tx, yVal), PeripheralStrip.Cor(p, carga), TextFormatFlags.NoPadding);
            int fim = tx + TextRenderer.MeasureText(g, valor, PeripheralStrip.FontPct, Size.Empty, TextFormatFlags.NoPadding).Width;
            if (_a.PctAtual.HasValue)
            {
                Rectangle pilha = new Rectangle(new Point(fim + 8, yVal + 4), PeripheralStrip.TamanhoDaPilha);
                PeripheralStrip.DesenharPilha(g, pilha, _a.PctAtual.Value, PeripheralStrip.CorFora(p), carga);
                if (_a.Carregando) _animador.Registrar(pilha);
                fim += 8 + PeripheralStrip.TamanhoDaPilha.Width;
            }
            TextRenderer.DrawText(g, Situacao(), Ui.FontSmall, new Point(fim + 10, yVal + 4), Ui.Muted, TextFormatFlags.NoPadding);

            using (Pen pen = new Pen(Ui.Border))
                g.DrawLine(pen, 1, 88, Width - 2, 88);

            // numeros a esquerda, curva a direita quando houver largura
            int larguraDados = Width >= 760 ? Width * 56 / 100 : Width - 16;
            DesenharDados(g, new Rectangle(18, 100, larguraDados - 18, Height - 112));
            if (Width - larguraDados - 24 >= LarguraMinimaDoGrafico)
                DesenharCurva(g, new Rectangle(larguraDados + 8, 104, Width - larguraDados - 26, Height - 124));
        }

        /// <summary>O que vai ao lado da porcentagem: carregando, desligado ou nada.</summary>
        private string Situacao()
        {
            if (!_a.PctAtual.HasValue) return T.PeripheralOff;
            if (_a.Carregando)
            {
                string s = _a.CarregandoDesde.HasValue
                    ? T.ChargingSince(_a.CarregandoDesde.Value.ToString("HH:mm"), _a.CarregandoDe)
                    : T.PeripheralCharging;
                string cheio = AteEncher(_a.HorasAteEncher);
                return cheio != null ? s + "  ·  " + cheio : s;
            }
            return "";
        }

        /// <summary>"cheio em ~40 min", "carga completa" em 100%, nulo enquanto mede.</summary>
        internal static string AteEncher(double? horas)
        {
            if (!horas.HasValue) return null;
            if (horas.Value <= 0) return T.FullyCharged;
            return T.FullIn(HistoricoDeBateria.DuracaoAproximada(horas.Value));
        }

        /// <summary>
        /// Saude da bateria: horas por carga cheia no mes mais recente com
        /// dado, contra o primeiro mes medido. Um mes so ainda nao compara.
        /// </summary>
        private string Saude()
        {
            List<KeyValuePair<DateTime, double>> m = _a.SaudePorMes;
            if (m == null || m.Count == 0) return T.HealthMeasuring;
            KeyValuePair<DateTime, double> ultimo = m[m.Count - 1];
            string agora = T.HealthPerCharge("~" + HistoricoDeBateria.DuracaoAproximada(ultimo.Value), Mes(ultimo.Key));
            if (m.Count == 1) return agora + "  ·  " + T.HealthFirstMonth;
            KeyValuePair<DateTime, double> primeiro = m[0];
            int variacao = (int)Math.Round((ultimo.Value / primeiro.Value - 1) * 100);
            string sinal = variacao > 0 ? "+" : variacao < 0 ? "−" : "";
            return agora + "  ·  " + T.HealthVersus(HistoricoDeBateria.DuracaoAproximada(primeiro.Value), Mes(primeiro.Key),
                                                    sinal + Math.Abs(variacao) + "%");
        }

        private static string Mes(DateTime d)
        {
            return d.ToString("MMM/yy", T.Pt ? new CultureInfo("pt-BR") : CultureInfo.InvariantCulture).Replace(".", "");
        }

        private void DesenharDados(Graphics g, Rectangle r)
        {
            List<string[]> itens = new List<string[]>();
            itens.Add(new string[] { T.BatRemainingOurs, Restante() });
            // No headset as duas previsoes lado a lado: a do G HUB vem do
            // proprio aparelho, a nossa do uso medido aqui - quando divergem, e
            // isso que a pessoa quer ver.
            if (_a.Fonte == FontePeriferico.GHub) itens.Add(new string[] { T.BatRemainingGHub, RestanteDoGHub() });
            itens.Add(new string[] { T.BatPerCharge, PorCarga() });
            itens.Add(new string[] { T.BatPace, Ritmo() });
            itens.Add(new string[] { T.BatLastCharge, UltimaCarga() });
            itens.Add(new string[] { T.BatSinceCharge, DesdeACarga() });
            itens.Add(new string[] { T.BatLastDischarge, UltimaDescarga() });
            itens.Add(new string[] { T.BatTotalUse, HistoricoDeBateria.Duracao(_a.UsoTotalHoras) });
            itens.Add(new string[] { T.BatHealth, Saude() });

            int colunas = r.Width >= 440 ? 2 : 1;
            int larg = (r.Width - (colunas - 1) * 18) / colunas;
            for (int i = 0; i < itens.Count; i++)
            {
                int col = i % colunas, lin = i / colunas;
                int x = r.X + col * (larg + 18), y = r.Y + lin * Linha;
                if (y + 36 > r.Bottom + 6) break;
                TextRenderer.DrawText(g, itens[i][0], Ui.FontSmall, new Rectangle(x, y, larg, 16), Ui.Faint,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

                // Valor que nao cabe numa linha quebra em duas, em letra menor,
                // em vez de sumir atras de reticencias: e justamente o texto
                // longo ("medindo, leva algumas semanas") que explica o que falta.
                string valor = itens[i][1];
                if (TextRenderer.MeasureText(g, valor, Ui.FontSemi, Size.Empty, TextFormatFlags.NoPadding).Width <= larg)
                    TextRenderer.DrawText(g, valor, Ui.FontSemi, new Rectangle(x, y + 17, larg, 19), Ui.Text,
                        TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                else
                    TextRenderer.DrawText(g, valor, Ui.FontSmall, new Rectangle(x, y + 17, larg, Linha - 19), Ui.Text,
                        TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            }
        }

        private string Restante()
        {
            if (_a.Carregando) return AteEncher(_a.HorasAteEncher) ?? T.PeripheralCharging;
            if (!_a.PctAtual.HasValue) return "--";
            return _a.HorasRestantes.HasValue ? "~" + HistoricoDeBateria.DuracaoAproximada(_a.HorasRestantes.Value) : T.BatMeasuring;
        }

        private string RestanteDoGHub()
        {
            if (_a.Carregando) return T.PeripheralCharging;
            return _autonomia.HasValue ? _autonomia.Value + " h" : "--";
        }

        private string PorCarga()
        {
            if (!_a.HorasPorCargaCheia.HasValue) return T.BatMeasuring;
            string s = T.UseHours("~" + HistoricoDeBateria.DuracaoAproximada(_a.HorasPorCargaCheia.Value));
            return _a.DescargasMedidas > 0 ? s + "  ·  " + T.Discharges(_a.DescargasMedidas) : s;
        }

        /// <summary>
        /// Onde a bateria gasta mais depressa, das faixas ja medidas. Precisa
        /// de duas faixas com gasto para comparar; diferenca abaixo de 25% e
        /// ruido de leitura, e nao ritmo.
        /// </summary>
        private string Ritmo()
        {
            double?[] f = _a.HorasPorPontoNaFaixa;
            if (f == null) return T.BatMeasuring;
            int rapida = -1, lenta = -1, medidas = 0;
            for (int k = 0; k < f.Length; k++)
            {
                if (!f[k].HasValue) continue;
                medidas++;
                if (rapida < 0 || f[k] < f[rapida]) rapida = k;
                if (lenta < 0 || f[k] > f[lenta]) lenta = k;
            }
            if (medidas < 2) return T.BatMeasuring;
            if (f[lenta].Value < f[rapida].Value * 1.25) return T.PaceEven;
            return T.PaceFastest(rapida * 10, rapida * 10 + 10);
        }

        private string UltimaCarga()
        {
            Carga c = _a.UltimaCarga;
            if (c == null) return T.BatNotSeenYet;
            string s = Quando(c.Fim) + ", " + T.UpTo(c.PctFim);
            if (!c.Deduzida && c.Fim > c.Inicio)
                s += "  ·  " + T.TookTime(HistoricoDeBateria.Duracao((c.Fim - c.Inicio).TotalHours));
            return s;
        }

        private string DesdeACarga()
        {
            Descarga d = _a.Atual;
            if (d == null) return _a.Carregando ? T.PeripheralCharging : "--";
            string s = T.UseHours(HistoricoDeBateria.Duracao(d.UsoSegundos / 3600.0)) + ", " + d.PctInicio + "% → " + d.PctFim + "%";
            if (d.Parcial) s += "  ·  " + T.SinceTracking(Quando(d.Inicio));
            return s;
        }

        private string UltimaDescarga()
        {
            Descarga d = _a.UltimaCompleta;
            if (d == null) return T.BatNotSeenYet;
            return T.UseHours(HistoricoDeBateria.Duracao(d.UsoSegundos / 3600.0)) + ", " + d.PctInicio + "% → " + d.PctFim + "%"
                 + "  ·  " + T.EndedAt(Quando(d.Fim));
        }

        /// <summary>"hoje 13:42", "ontem 22:10" ou "10/10 08:15".</summary>
        private static string Quando(DateTime d)
        {
            DateTime hoje = DateTime.Today;
            if (d.Date == hoje) return T.Today + " " + d.ToString("HH:mm");
            if (d.Date == hoje.AddDays(-1)) return T.Yesterday + " " + d.ToString("HH:mm");
            return d.ToString("dd/MM HH:mm");
        }

        /// <summary>A carga nas ultimas 48 h; as horas no carregador ficam sombreadas.</summary>
        private void DesenharCurva(Graphics g, Rectangle r)
        {
            DateTime agora = DateTime.Now, inicio = agora - JanelaDoGrafico;
            Rectangle area = new Rectangle(r.X + 30, r.Y, r.Width - 30, r.Height - 18);

            using (Pen grade = new Pen(Ui.Border))
            {
                grade.DashStyle = DashStyle.Dot;
                foreach (int nivel in new int[] { 0, 50, 100 })
                {
                    int y = area.Bottom - area.Height * nivel / 100;
                    g.DrawLine(grade, area.X, y, area.Right, y);
                    TextRenderer.DrawText(g, nivel + "%", Ui.FontSmall, new Rectangle(r.X - 4, y - 8, 32, 16), Ui.Faint,
                        TextFormatFlags.Right | TextFormatFlags.NoPadding);
                }
            }
            TextRenderer.DrawText(g, T.HoursAgo(48), Ui.FontSmall, new Point(area.X, area.Bottom + 3), Ui.Faint, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, T.Now, Ui.FontSmall, new Rectangle(area.X, area.Bottom + 3, area.Width, 16), Ui.Faint,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);

            List<PointF> pts = new List<PointF>();
            AmostraDeBateria[] am = _a.Amostras ?? new AmostraDeBateria[0];
            Func<DateTime, float> X = delegate(DateTime t)
            {
                return area.X + (float)((t - inicio).TotalSeconds / JanelaDoGrafico.TotalSeconds) * area.Width;
            };
            Func<int, float> Y = delegate(int pct) { return area.Bottom - area.Height * pct / 100f; };

            using (SolidBrush sombra = new SolidBrush(Color.FromArgb(70, Ui.Warn)))
                for (int i = 0; i < am.Length; i++)
                {
                    if (!am[i].Carregando || am[i].Quando < inicio) continue;
                    DateTime ate = i + 1 < am.Length ? am[i + 1].Quando : agora;
                    float x0 = Math.Max(area.X, X(am[i].Quando)), x1 = Math.Min(area.Right, X(ate));
                    if (x1 > x0) g.FillRectangle(sombra, x0, area.Y, x1 - x0, area.Height);
                }

            // A curva quebra onde houve lacuna grande: app fechado ou aparelho
            // desligado nao e uma reta ligando dois pontos distantes.
            using (Pen linha = new Pen(Ui.Accent, 2f))
            {
                linha.LineJoin = LineJoin.Round;
                DateTime? anterior = null;
                foreach (AmostraDeBateria s in am)
                {
                    if (s.Quando < inicio) continue;
                    if (anterior.HasValue && (s.Quando - anterior.Value).TotalMinutes > 30 && pts.Count > 0)
                    {
                        Tracar(g, linha, pts);
                        pts.Clear();
                    }
                    pts.Add(new PointF(X(s.Quando), Y(s.Pct)));
                    anterior = s.Quando;
                }
                if (_a.PctAtual.HasValue && anterior.HasValue && (agora - anterior.Value).TotalMinutes <= 30)
                    pts.Add(new PointF(X(agora), Y(_a.PctAtual.Value)));
                Tracar(g, linha, pts);
            }

            if (am.Length == 0)
                TextRenderer.DrawText(g, T.BatNoCurve, Ui.FontSmall, area, Ui.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static void Tracar(Graphics g, Pen pen, List<PointF> pts)
        {
            if (pts.Count >= 2) g.DrawLines(pen, pts.ToArray());
            else if (pts.Count == 1)
                using (SolidBrush b = new SolidBrush(pen.Color))
                    g.FillEllipse(b, pts[0].X - 2.5f, pts[0].Y - 2.5f, 5, 5);
        }
    }
}
