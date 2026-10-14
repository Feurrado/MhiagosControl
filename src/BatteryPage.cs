using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Um cartao da pagina Baterias, com o resumo primeiro: a carga agora num
    /// numero grande e num selo, quatro blocos com o que se procura (quanto
    /// falta, quanto dura uma carga, desde quando, saude), a curva das ultimas
    /// 48 horas e, recolhido sob "Mais detalhes", o resto da historia.
    ///
    /// Pintado inteiro, como a tira da Visao geral: sao textos que mudam juntos
    /// a cada leitura, e rotulos filhos so trariam piscada e mais arranjo.
    /// </summary>
    public class CartaoDeBateria : Card
    {
        private const int Margem = 18;
        private const int Icone = 48;
        private const int AlturaDoCabecalho = 88;
        private const int AlturaDoBloco = 68;
        private const int EspacoEntreBlocos = 10;
        private const int AlturaDaCurva = 132;
        private const int AlturaDaBarraDeDetalhes = 40;
        private const int AlturaDaLinhaDeDetalhe = 54;
        private static readonly TimeSpan JanelaDoGrafico = TimeSpan.FromHours(48);

        private AnaliseDeBateria _a;
        private int? _autonomia;
        private string _assinatura;

        private readonly AnimadorDePilhas _animador;

        /// <summary>Abrir ou fechar os detalhes muda a altura: a pagina rearranja.</summary>
        public event EventHandler AlturaMudou;

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

        // ---------------- altura ----------------

        /// <summary>Quatro blocos lado a lado quando cabem; senao, dois por linha.</summary>
        private static int ColunasDeBlocos(int largura)
        {
            return largura - 2 * Margem >= 600 ? 4 : 2;
        }

        private static int AlturaDosBlocos(int largura)
        {
            int linhas = 4 / ColunasDeBlocos(largura);
            return linhas * AlturaDoBloco + (linhas - 1) * EspacoEntreBlocos;
        }

        private static int ColunasDeDetalhe(int largura)
        {
            return largura - 2 * Margem >= 440 ? 2 : 1;
        }

        public static int AlturaPara(int largura, bool detalhes)
        {
            int h = AlturaDoCabecalho + AlturaDosBlocos(largura) + 14 + AlturaDaCurva + 8 + AlturaDaBarraDeDetalhes;
            if (detalhes) h += (4 / ColunasDeDetalhe(largura)) * AlturaDaLinhaDeDetalhe + 8;
            return h;
        }

        public bool DetalhesAbertos { get { return _a != null && DetalhesDaBateria.Aberto(_a.Chave); } }

        public int AlturaDesejada(int largura) { return AlturaPara(largura, DetalhesAbertos); }

        // ---------------- clique no icone e nos detalhes ----------------

        /// <summary>A imagem do aparelho, com folga: e ela que abre o app dele.</summary>
        private static readonly Rectangle ZonaDoIcone = new Rectangle(10, 10, Icone + 16, Icone + 16);
        private readonly ToolTip _dica = new ToolTip();
        private bool _sobreIcone = false;
        private bool _sobreDetalhes = false;
        private Rectangle _zonaDosDetalhes = Rectangle.Empty;

        private Periferico ComoPeriferico()
        {
            return _a == null ? null : Periferico.Sem(_a.Nome, _a.Tipo, _a.Fonte, EstadoPeriferico.Normal);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool icone = _a != null && ZonaDoIcone.Contains(e.Location);
            bool detalhes = _a != null && _zonaDosDetalhes.Contains(e.Location);
            if (detalhes != _sobreDetalhes)
            {
                _sobreDetalhes = detalhes;
                Invalidate(Rectangle.Inflate(_zonaDosDetalhes, 4, 4));
            }
            Cursor = icone || detalhes ? Cursors.Hand : Cursors.Default;
            if (icone == _sobreIcone) return;
            _sobreIcone = icone;
            Periferico p = ComoPeriferico();
            _dica.SetToolTip(this, !icone || p == null ? null
                : p.Fonte == FontePeriferico.Bluetooth ? T.OpenBluetooth
                : T.OpenApp(Perifericos.NomeDoApp(p)));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _sobreIcone = false;
            if (_sobreDetalhes) { _sobreDetalhes = false; Invalidate(Rectangle.Inflate(_zonaDosDetalhes, 4, 4)); }
            Cursor = Cursors.Default;
            _dica.SetToolTip(this, null);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || _a == null) return;
            if (ZonaDoIcone.Contains(e.Location)) Perifericos.Abrir(ComoPeriferico());
            else if (_zonaDosDetalhes.Contains(e.Location)) AlternarDetalhes();
        }

        /// <summary>Abre ou fecha "Mais detalhes" e guarda a escolha por aparelho.</summary>
        public void AlternarDetalhes()
        {
            if (_a == null) return;
            DetalhesDaBateria.Alternar(_a.Chave);
            Invalidate();
            EventHandler h = AlturaMudou;
            if (h != null) h(this, EventArgs.Empty);
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

            // a mesma passagem suave da Visao geral ao entrar e sair do cabo
            bool animando;
            float carga = TransicaoDeCarga.Valor(_a.Chave, _a.Carregando && _a.PctAtual.HasValue, out animando);
            if (animando) _animador.Transicao();
            Color cor = PeripheralStrip.Cor(p, carga);

            DesenharCabecalho(g, p, cor, carga);

            int y = AlturaDoCabecalho;
            DesenharBlocos(g, new Rectangle(Margem, y, Width - 2 * Margem, AlturaDosBlocos(Width)));
            y += AlturaDosBlocos(Width) + 14;

            DesenharCurva(g, new Rectangle(Margem, y, Width - 2 * Margem, AlturaDaCurva), cor);
            y += AlturaDaCurva + 8;

            DesenharBarraDeDetalhes(g, y);
            y += AlturaDaBarraDeDetalhes;
            if (DetalhesAbertos) DesenharDetalhes(g, new Rectangle(Margem, y, Width - 2 * Margem, Height - y - 8));
        }

        // ---------------- cabecalho: imagem, nome, carga e selo ----------------

        private void DesenharCabecalho(Graphics g, Periferico p, Color cor, float carga)
        {
            Image img = PeripheralStrip.Imagem(p);
            if (img != null) g.DrawImage(img, new Rectangle(Margem, 18, Icone, Icone));

            // O nome completo tem linha propria, acima da porcentagem: em janela
            // estreita ele ganha reticencias e nunca empurra o numero.
            int tx = Margem + Icone + 14;
            TextRenderer.DrawText(g, Perifericos.NomeCompleto(_a.Nome, _a.Fonte), Ui.FontSection,
                new Rectangle(tx, 14, Math.Max(0, Width - Margem - tx), 20), Ui.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            const int yVal = 36;
            string valor = _a.PctAtual.HasValue ? _a.PctAtual.Value + "%" : "--";
            Size tam = TextRenderer.MeasureText(g, valor, Ui.FontBigPct, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, valor, Ui.FontBigPct, new Point(tx, yVal), cor, TextFormatFlags.NoPadding);
            int meio = yVal + tam.Height / 2;
            int fim = tx + tam.Width;

            if (_a.PctAtual.HasValue)
            {
                Size ps = PeripheralStrip.TamanhoDaPilha;
                Rectangle pilha = new Rectangle(new Point(fim + 10, meio - ps.Height / 2), ps);
                PeripheralStrip.DesenharPilha(g, pilha, _a.PctAtual.Value, PeripheralStrip.CorFora(p), carga);
                if (_a.Carregando) _animador.Registrar(pilha);
                fim += 10 + ps.Width;
            }

            DesenharSelo(g, Selo(), new Point(fim + 14, meio), cor, carga);
        }

        /// <summary>Uma frase com o estado: carregando, na bateria ou desligado.</summary>
        private string Selo()
        {
            if (!_a.PctAtual.HasValue) return T.StatusOff;
            if (_a.Carregando)
            {
                string cheio = AteEncher(_a.HorasAteEncher);
                return cheio != null ? T.StatusCharging + "  ·  " + cheio : T.StatusCharging;
            }
            return _a.HorasRestantes.HasValue
                ? T.StatusOnBattery + "  ·  " + T.HoursLeft("~" + HistoricoDeBateria.DuracaoAproximada(_a.HorasRestantes.Value))
                : T.StatusOnBattery;
        }

        /// <summary>
        /// Pilula com bolinha e texto. No cabo ganha o verde, na mesma passagem
        /// suave do numero; fora dele fica neutra, para nao gritar sem motivo.
        /// </summary>
        private void DesenharSelo(Graphics g, string texto, Point esquerdaMeio, Color cor, float carga)
        {
            Size t = TextRenderer.MeasureText(g, texto, Ui.FontSmall, Size.Empty, TextFormatFlags.NoPadding);
            int maxLargura = Width - Margem - esquerdaMeio.X;
            int larg = Math.Min(t.Width + 34, maxLargura);
            if (larg < 60) return;
            Rectangle r = new Rectangle(esquerdaMeio.X, esquerdaMeio.Y - 12, larg, 24);

            bool ligado = _a.PctAtual.HasValue;
            Color fundo = PeripheralStrip.Misturar(Ui.SurfaceAlt, Color.FromArgb(Ui.Dark ? 40 : 34, PeripheralStrip.Verde), carga);
            Color letra = PeripheralStrip.Misturar(ligado ? Ui.Text : Ui.Muted, cor, carga);
            Color bolinha = PeripheralStrip.Misturar(ligado ? Ui.Muted : Ui.Faint, cor, carga);

            using (GraphicsPath gp = Ui.RoundRect(r, r.Height / 2))
            {
                using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(b, gp);
                using (SolidBrush b = new SolidBrush(fundo)) g.FillPath(b, gp);
            }
            using (SolidBrush b = new SolidBrush(bolinha))
                g.FillEllipse(b, r.X + 11, r.Y + r.Height / 2 - 3.5f, 7, 7);
            TextRenderer.DrawText(g, texto, Ui.FontSmall, new Rectangle(r.X + 24, r.Y, r.Width - 32, r.Height), letra,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        // ---------------- os quatro blocos ----------------

        private void DesenharBlocos(Graphics g, Rectangle r)
        {
            string[][] blocos = Blocos();
            int colunas = ColunasDeBlocos(Width);
            int larg = (r.Width - (colunas - 1) * EspacoEntreBlocos) / colunas;
            for (int i = 0; i < blocos.Length; i++)
            {
                int col = i % colunas, lin = i / colunas;
                Rectangle b = new Rectangle(r.X + col * (larg + EspacoEntreBlocos), r.Y + lin * (AlturaDoBloco + EspacoEntreBlocos),
                                            larg, AlturaDoBloco);
                using (GraphicsPath gp = Ui.RoundRect(b, 6))
                {
                    using (SolidBrush br = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(br, gp);
                    // no claro o fundo do bloco quase some no branco do cartao
                    if (!Ui.Dark) using (Pen pen = new Pen(Ui.Border)) g.DrawPath(pen, gp);
                }
                int x = b.X + 12, w = b.Width - 24;
                const TextFormatFlags Uma = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(g, blocos[i][0], Ui.FontSmall, new Rectangle(x, b.Y + 9, w, 16), Ui.Muted, Uma);
                TextRenderer.DrawText(g, blocos[i][1], Ui.FontTileValue, new Rectangle(x, b.Y + 25, w, 22), Ui.Text, Uma);
                if (!string.IsNullOrEmpty(blocos[i][2]))
                    TextRenderer.DrawText(g, blocos[i][2], Ui.FontSmall, new Rectangle(x, b.Y + 47, w, 16), Ui.Muted, Uma);
            }
        }

        /// <summary>Rotulo, valor e linha de apoio de cada bloco.</summary>
        internal string[][] Blocos()
        {
            return new string[][] { BlocoRestante(), BlocoPorCarga(), BlocoDesde(), BlocoSaude() };
        }

        private string[] BlocoRestante()
        {
            string ghub = _a.Fonte == FontePeriferico.GHub
                ? T.GHubSays(_a.Carregando ? T.PeripheralCharging : _autonomia.HasValue ? _autonomia.Value + " h" : "--")
                : null;
            if (!_a.PctAtual.HasValue) return new string[] { T.TileRemaining, "--", ghub ?? "" };
            if (_a.Carregando)
                return new string[] { T.TileRemaining, AteEncher(_a.HorasAteEncher) ?? T.PeripheralCharging, ghub ?? "" };
            if (_a.HorasRestantes.HasValue)
                return new string[] { T.TileRemaining, "~" + HistoricoDeBateria.DuracaoAproximada(_a.HorasRestantes.Value),
                                      ghub ?? T.EstimatedFromUse };
            // Sem ritmo medido ainda, o G HUB ja tem um palpite: melhor que nada.
            return new string[] { T.TileRemaining, T.Measuring, ghub ?? T.Needs10Used };
        }

        private string[] BlocoPorCarga()
        {
            if (!_a.HorasPorCargaCheia.HasValue) return new string[] { T.TileFullCharge, T.Measuring, T.Needs10Used };
            return new string[] { T.TileFullCharge,
                T.UseHours("~" + HistoricoDeBateria.DuracaoAproximada(_a.HorasPorCargaCheia.Value)),
                _a.DescargasMedidas > 0 ? T.AverageOfDischarges(_a.DescargasMedidas) : "" };
        }

        private string[] BlocoDesde()
        {
            if (_a.Carregando && _a.PctAtual.HasValue)
            {
                if (_a.CarregandoDesde.HasValue)
                    return new string[] { T.TileChargingSince, Quando(_a.CarregandoDesde.Value), T.FromPct(_a.CarregandoDe) };
                return new string[] { T.TileChargingSince, T.Now, "" };
            }
            Descarga d = _a.Atual;
            if (d == null) return new string[] { T.BatSinceCharge, "--", T.BatNotSeenYet };
            string sub = d.PctInicio + "% → " + d.PctFim + "%";
            if (d.Parcial) sub += "  ·  " + T.SinceTracking(Quando(d.Inicio));
            return new string[] { T.BatSinceCharge, HistoricoDeBateria.Duracao(d.UsoSegundos / 3600.0), sub };
        }

        /// <summary>
        /// Saude da bateria: horas por carga cheia no mes mais recente com
        /// dado, contra o primeiro mes medido. Um mes so ainda nao compara.
        /// </summary>
        private string[] BlocoSaude()
        {
            List<KeyValuePair<DateTime, double>> m = _a.SaudePorMes;
            if (m == null || m.Count == 0) return new string[] { T.TileHealth, T.Measuring, T.NeedsWeeks };
            KeyValuePair<DateTime, double> ultimo = m[m.Count - 1];
            string valor = T.PerChargeShort("~" + HistoricoDeBateria.DuracaoAproximada(ultimo.Value));
            if (m.Count == 1) return new string[] { T.TileHealth, valor, Mes(ultimo.Key) + "  ·  " + T.HealthFirstMonth };
            KeyValuePair<DateTime, double> primeiro = m[0];
            int variacao = (int)Math.Round((ultimo.Value / primeiro.Value - 1) * 100);
            string sinal = variacao > 0 ? "+" : variacao < 0 ? "−" : "";
            return new string[] { T.TileHealth, valor,
                T.HealthVersus(HistoricoDeBateria.DuracaoAproximada(primeiro.Value), Mes(primeiro.Key), sinal + Math.Abs(variacao) + "%") };
        }

        /// <summary>"cheio em ~40 min", "carga completa" em 100%, nulo enquanto mede.</summary>
        internal static string AteEncher(double? horas)
        {
            if (!horas.HasValue) return null;
            if (horas.Value <= 0) return T.FullyCharged;
            return T.FullIn(HistoricoDeBateria.DuracaoAproximada(horas.Value));
        }

        private static string Mes(DateTime d)
        {
            return d.ToString("MMM/yy", T.Pt ? new CultureInfo("pt-BR") : CultureInfo.InvariantCulture).Replace(".", "");
        }

        // ---------------- mais detalhes ----------------

        private void DesenharBarraDeDetalhes(Graphics g, int y)
        {
            using (Pen pen = new Pen(Ui.Border))
                g.DrawLine(pen, 1, y, Width - 2, y);

            bool aberto = DetalhesAbertos;
            string texto = aberto ? T.LessDetails : T.MoreDetails;
            Color cor = _sobreDetalhes ? Ui.AccentHover : Ui.Accent;
            Size t = TextRenderer.MeasureText(g, texto, Ui.FontMed, Size.Empty, TextFormatFlags.NoPadding);
            int meio = y + AlturaDaBarraDeDetalhes / 2;

            // seta desenhada: aponta para a direita fechado, para baixo aberto
            using (Pen seta = new Pen(cor, 1.6f))
            {
                seta.StartCap = LineCap.Round; seta.EndCap = LineCap.Round; seta.LineJoin = LineJoin.Round;
                float cx = Margem + 4, cy = meio;
                PointF[] pts = aberto
                    ? new PointF[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) }
                    : new PointF[] { new PointF(cx - 2, cy - 4), new PointF(cx + 2, cy), new PointF(cx - 2, cy + 4) };
                g.DrawLines(seta, pts);
            }
            int tx = Margem + 14;
            TextRenderer.DrawText(g, texto, Ui.FontMed, new Point(tx, meio - t.Height / 2), cor, TextFormatFlags.NoPadding);
            _zonaDosDetalhes = new Rectangle(Margem - 6, y + 4, tx + t.Width + 8 - (Margem - 6), AlturaDaBarraDeDetalhes - 8);
        }

        /// <summary>O que nao cabe nos blocos, em pares rotulo e valor.</summary>
        internal string[][] Detalhes()
        {
            string[] descarga;
            Descarga d = _a.UltimaCompleta;
            if (d == null) descarga = new string[] { T.BatLastDischarge, T.BatNotSeenYet, "" };
            else descarga = new string[] { T.BatLastDischarge,
                T.UseHours(HistoricoDeBateria.Duracao(d.UsoSegundos / 3600.0)) + ", " + d.PctInicio + "% → " + d.PctFim + "%",
                T.EndedAt(Quando(d.Fim)) };
            return new string[][] {
                new string[] { T.BatPace, Ritmo(), "" },
                new string[] { T.BatLastCharge, UltimaCarga(), "" },
                descarga,
                new string[] { T.BatTotalUse, HistoricoDeBateria.Duracao(_a.UsoTotalHoras), "" } };
        }

        private void DesenharDetalhes(Graphics g, Rectangle r)
        {
            string[][] itens = Detalhes();
            int colunas = ColunasDeDetalhe(Width);
            const int Vao = 28;
            int larg = (r.Width - (colunas - 1) * Vao) / colunas;
            const TextFormatFlags Uma = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            for (int i = 0; i < itens.Length; i++)
            {
                int col = i % colunas, lin = i / colunas;
                int x = r.X + col * (larg + Vao), y = r.Y + lin * AlturaDaLinhaDeDetalhe;
                TextRenderer.DrawText(g, itens[i][0], Ui.FontSmall, new Rectangle(x, y, larg, 16), Ui.Muted, Uma);
                TextRenderer.DrawText(g, itens[i][1], Ui.FontSemi, new Rectangle(x, y + 17, larg, 18), Ui.Text, Uma);
                if (!string.IsNullOrEmpty(itens[i][2]))
                    TextRenderer.DrawText(g, itens[i][2], Ui.FontSmall, new Rectangle(x, y + 35, larg, 16), Ui.Muted, Uma);
            }
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

        /// <summary>"hoje 13:42", "ontem 22:10" ou "10/10 08:15".</summary>
        private static string Quando(DateTime d)
        {
            DateTime hoje = DateTime.Today;
            if (d.Date == hoje) return T.Today + " " + d.ToString("HH:mm");
            if (d.Date == hoje.AddDays(-1)) return T.Yesterday + " " + d.ToString("HH:mm");
            return d.ToString("dd/MM HH:mm");
        }

        // ---------------- curva ----------------

        /// <summary>
        /// A carga nas ultimas 48 h, na cor do aparelho, com area esmaecida
        /// por baixo; as horas no carregador ficam numa faixa verde e a ponta
        /// de agora ganha um ponto.
        /// </summary>
        private void DesenharCurva(Graphics g, Rectangle r, Color cor)
        {
            DateTime agora = DateTime.Now, inicio = agora - JanelaDoGrafico;
            Rectangle area = new Rectangle(r.X + 34, r.Y + 6, r.Width - 34 - 6, r.Height - 6 - 20);

            using (Pen solida = new Pen(Ui.Border))
            using (Pen tracejada = new Pen(Ui.Border))
            {
                tracejada.DashStyle = DashStyle.Dash;
                tracejada.DashPattern = new float[] { 3, 4 };
                foreach (int nivel in new int[] { 0, 50, 100 })
                {
                    int y = area.Bottom - area.Height * nivel / 100;
                    g.DrawLine(nivel == 50 ? tracejada : solida, area.X, y, area.Right, y);
                    TextRenderer.DrawText(g, nivel + "%", Ui.FontSmall, new Rectangle(r.X - 4, y - 8, 32, 16), Ui.Faint,
                        TextFormatFlags.Right | TextFormatFlags.NoPadding);
                }
            }
            int yEixo = area.Bottom + 5;
            TextRenderer.DrawText(g, T.HoursAgo(48), Ui.FontSmall, new Point(area.X, yEixo), Ui.Faint, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, T.HoursAgo(24), Ui.FontSmall, new Rectangle(area.X, yEixo, area.Width, 16), Ui.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, T.Now, Ui.FontSmall, new Rectangle(area.X, yEixo, area.Width, 16), Ui.Faint,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);

            AmostraDeBateria[] am = _a.Amostras ?? new AmostraDeBateria[0];
            Func<DateTime, float> X = delegate(DateTime t)
            {
                return area.X + (float)((t - inicio).TotalSeconds / JanelaDoGrafico.TotalSeconds) * area.Width;
            };
            Func<int, float> Y = delegate(int pct) { return area.Bottom - area.Height * pct / 100f; };

            using (SolidBrush sombra = new SolidBrush(Color.FromArgb(Ui.Dark ? 34 : 40, PeripheralStrip.Verde)))
                for (int i = 0; i < am.Length; i++)
                {
                    if (!am[i].Carregando) continue;
                    DateTime ate = i + 1 < am.Length ? am[i + 1].Quando : agora;
                    if (ate < inicio) continue;
                    // a primeira amostra no cabo ja chega com a carga subindo:
                    // o cabo entrou no trecho anterior, que a faixa tambem cobre
                    DateTime de = am[i].Quando;
                    if (i > 0 && !am[i - 1].Carregando && (de - am[i - 1].Quando).TotalMinutes <= 30) de = am[i - 1].Quando;
                    float x0 = Math.Max(area.X, X(de)), x1 = Math.Min(area.Right, X(ate));
                    if (x1 > x0) g.FillRectangle(sombra, x0, area.Y, x1 - x0, area.Height);
                }

            // A curva cheia quebra onde houve lacuna grande - app fechado ou
            // aparelho desligado - e a lacuna ganha uma ponte tracejada logo
            // abaixo, para nao passar por medida.
            List<List<PointF>> trechos = new List<List<PointF>>();
            List<PointF> pts = new List<PointF>();
            DateTime? anterior = null;
            foreach (AmostraDeBateria s in am)
            {
                if (s.Quando < inicio) continue;
                if (anterior.HasValue && (s.Quando - anterior.Value).TotalMinutes > 30 && pts.Count > 0)
                {
                    trechos.Add(pts);
                    pts = new List<PointF>();
                }
                pts.Add(new PointF(X(s.Quando), Y(s.Pct)));
                anterior = s.Quando;
            }
            bool chegaAgora = _a.PctAtual.HasValue && anterior.HasValue && (agora - anterior.Value).TotalMinutes <= 30;
            if (chegaAgora) pts.Add(new PointF(X(agora), Y(_a.PctAtual.Value)));
            if (pts.Count > 0) trechos.Add(pts);

            using (Pen linha = new Pen(cor, 2f))
            {
                linha.LineJoin = LineJoin.Round;
                foreach (List<PointF> t in trechos) Tracar(g, linha, t, area);
            }

            // Entre dois trechos, uma ponte tracejada e apagada. Quase sempre e
            // o aparelho dormindo: o receptor do X11 nao informa bateria com o
            // mouse parado, e a carga antes e depois e a mesma. Deixar o buraco
            // fazia parecer que o grafico tinha perdido dados.
            using (Pen ponte = new Pen(Color.FromArgb(120, cor), 1.6f))
            {
                ponte.DashPattern = new float[] { 3f, 3f };
                for (int k = 0; k + 1 < trechos.Count; k++)
                {
                    List<PointF> a = trechos[k], b = trechos[k + 1];
                    if (a.Count == 0 || b.Count == 0) continue;
                    g.DrawLine(ponte, a[a.Count - 1], b[0]);
                }
            }

            if (chegaAgora)
            {
                PointF ponta = pts[pts.Count - 1];
                using (SolidBrush aro = new SolidBrush(Ui.Surface)) g.FillEllipse(aro, ponta.X - 6, ponta.Y - 6, 12, 12);
                using (SolidBrush b = new SolidBrush(cor)) g.FillEllipse(b, ponta.X - 4, ponta.Y - 4, 8, 8);
            }

            if (am.Length == 0)
                TextRenderer.DrawText(g, T.BatNoCurve, Ui.FontSmall, area, Ui.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        /// <summary>Um trecho da curva com a area esmaecendo ate o zero; um ponto so vira bolinha.</summary>
        private static void Tracar(Graphics g, Pen pen, List<PointF> pts, Rectangle area)
        {
            if (pts.Count == 1)
            {
                using (SolidBrush b = new SolidBrush(pen.Color))
                    g.FillEllipse(b, pts[0].X - 2.5f, pts[0].Y - 2.5f, 5, 5);
                return;
            }
            if (pts.Count < 2) return;

            List<PointF> poligono = new List<PointF>(pts);
            poligono.Add(new PointF(pts[pts.Count - 1].X, area.Bottom));
            poligono.Add(new PointF(pts[0].X, area.Bottom));
            Rectangle grad = new Rectangle(area.X, area.Y - 1, Math.Max(1, area.Width), area.Height + 2);
            using (LinearGradientBrush b = new LinearGradientBrush(grad,
                       Color.FromArgb(Ui.Dark ? 60 : 50, pen.Color), Color.FromArgb(0, pen.Color), LinearGradientMode.Vertical))
                g.FillPolygon(b, poligono.ToArray());
            g.DrawLines(pen, pts.ToArray());
        }
    }

    /// <summary>
    /// "Widget" com engrenagem, no alto da pagina Baterias: abre e fecha o
    /// painel com as opcoes do widget.
    ///
    /// Discreto de proposito, sem borda nem fundo ate o mouse passar: a pagina
    /// e dos aparelhos, e o widget se ajusta uma vez e se esquece. O cartao de
    /// opcoes ocupava 330 px acima do G325 e do X11, e quem abria a aba para
    /// ver a bateria rolava por ele todas as vezes.
    /// </summary>
    public class BotaoDoWidget : Control
    {
        private bool _hover, _aberto;

        // ESCAPADOS: E713 e a engrenagem, E70D e E70E as setas para baixo e
        // para cima da Segoe MDL2.
        private const string Engrenagem = "\uE713", Abrir = "\uE70D", Fechar = "\uE70E";

        public BotaoDoWidget()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Font = Ui.FontBase;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
        }

        /// <summary>O painel esta aberto: a seta aponta para cima e o texto fica na cor de enfase.</summary>
        public bool Aberto
        {
            get { return _aberto; }
            set
            {
                if (_aberto == value) return;
                _aberto = value;
                AccessibleDescription = value ? "aberto" : "fechado";
                Invalidate();
            }
        }

        /// <summary>Engrenagem, texto e seta, com folga dos dois lados.</summary>
        public int LarguraDesejada()
        {
            return 10 + 16 + 6 + TextRenderer.MeasureText(Text ?? "", Font, Size.Empty, TextFormatFlags.NoPadding).Width + 8 + 12 + 10;
        }

        protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; Invalidate(); base.OnTextChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true; e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_hover || _aberto)
                using (GraphicsPath p = Ui.RoundRect(r, 6))
                using (SolidBrush b = new SolidBrush(_aberto && !_hover ? Ui.AccentSoft : Ui.Hover))
                    g.FillPath(b, p);

            Color cor = _aberto ? Ui.Accent : (_hover ? Ui.Text : Ui.Muted);
            const TextFormatFlags Meio = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            int x = 10;
            TextRenderer.DrawText(g, Engrenagem, Ui.FontGlyph10, new Rectangle(x, 0, 16, Height), cor, Meio | TextFormatFlags.HorizontalCenter);
            x += 16 + 6;
            int lt = TextRenderer.MeasureText(g, Text ?? "", Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, Text ?? "", Font, new Rectangle(x, 0, lt + 2, Height), cor, Meio);
            x += lt + 8;
            TextRenderer.DrawText(g, _aberto ? Fechar : Abrir, Ui.FontGlyph8, new Rectangle(x, 1, 12, Height), cor, Meio | TextFormatFlags.HorizontalCenter);

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(r, -3, -3), cor, Color.Transparent);
        }
    }

    /// <summary>
    /// Quais aparelhos ficam com "Mais detalhes" aberto, guardado fora do
    /// config.ini: abrir uma secao nao e uma mudanca de configuracao para
    /// pedir Salvar. Uma chave por linha em baterias-detalhes.txt.
    /// </summary>
    public static class DetalhesDaBateria
    {
        private static HashSet<string> _abertos;
        internal static string _caminho;

        public static string Caminho
        {
            get { return _caminho ?? Path.Combine(Paths.DataDir, "baterias-detalhes.txt"); }
        }

        private static HashSet<string> Abertos
        {
            get
            {
                if (_abertos != null) return _abertos;
                _abertos = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    if (File.Exists(Caminho))
                        foreach (string l in File.ReadAllLines(Caminho))
                            if (l.Trim().Length > 0) _abertos.Add(l.Trim());
                }
                catch (Exception ex) { Log.Write("detalhes da bateria: " + ex.Message); }
                return _abertos;
            }
        }

        public static bool Aberto(string chave)
        {
            return !string.IsNullOrEmpty(chave) && Abertos.Contains(chave);
        }

        public static void Alternar(string chave)
        {
            if (string.IsNullOrEmpty(chave)) return;
            if (!Abertos.Remove(chave)) Abertos.Add(chave);
            try
            {
                string pasta = Path.GetDirectoryName(Caminho);
                if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
                File.WriteAllLines(Caminho, new List<string>(Abertos).ToArray());
            }
            catch (Exception ex) { Log.Write("detalhes da bateria: " + ex.Message); }
        }

        /// <summary>Esquece o que foi lido, para os testes trocarem o arquivo.</summary>
        internal static void Recarregar() { _abertos = null; }
    }
}
