using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Consumo do ar e da lavadora num periodo, dia a dia, e o mesmo do periodo
    /// anterior para comparar. A mesma conta da aba Consumo do painel web:
    /// 7 e 30 dias contra os 7 e 30 de antes; o mes contra o mesmo dia do anterior.
    /// Ar ESTIMADO pelo tempo ligado x potencia; a lavadora mede a propria energia.
    /// </summary>
    internal static class ConsumoDoPeriodo
    {
        internal enum Periodo { Dias7 = 0, Dias30 = 1, Mes = 2 }

        internal sealed class Janela
        {
            public DateTime Ini, Fim, AntIni, AntFim;
            public string Nome, Contra, Sem;
        }

        /// <summary>Um dia de um aparelho: kWh, custo, horas ligado, agua e ciclos.</summary>
        internal sealed class Dia
        {
            public DateTime Data;
            public double Kwh, Custo, Horas, Agua;
            public int Ciclos;
        }

        internal sealed class DoAparelho
        {
            public Aparelho Aparelho;
            public readonly List<Dia> Dias = new List<Dia>();
            public double Kwh, Custo, Horas, Agua, CustoAnterior;
            public int Ciclos;
            public Color Cor { get { return Aparelho.ArCondicionado ? CorAr : CorLavadora; } }
        }

        internal sealed class Resultado
        {
            public Janela Janela;
            public readonly List<DoAparelho> Aparelhos = new List<DoAparelho>();
            public double Kwh, Custo, Agua, CustoAnterior, Tarifa;
            public int NumDias;
            /// <summary>Variacao em % do custo contra o periodo anterior; null sem base.</summary>
            public double? Variacao { get { return CustoAnterior > 0.005 ? (Custo - CustoAnterior) / CustoAnterior * 100 : (double?)null; } }
        }

        public static readonly Color CorAr = Color.FromArgb(0x2D, 0x7D, 0xF6), CorLavadora = Color.FromArgb(0x00, 0xB0, 0xA9);

        private static string Mes(DateTime d) { return d.ToString("MMMM", T.Pt ? new CultureInfo("pt-BR") : CultureInfo.InvariantCulture); }

        public static Janela JanelaDe(Periodo p, DateTime agora)
        {
            DateTime hoje = agora.Date;
            Janela j = new Janela();
            j.Fim = hoje;
            if (p == Periodo.Mes)
            {
                j.Ini = new DateTime(hoje.Year, hoje.Month, 1);
                j.AntIni = j.Ini.AddMonths(-1);
                j.AntFim = j.AntIni.AddDays(Math.Min(hoje.Day, DateTime.DaysInMonth(j.AntIni.Year, j.AntIni.Month)) - 1);
                string m = Mes(hoje), ma = Mes(j.AntIni);
                j.Nome = T.ConsMesAteHoje(char.ToUpper(m[0]) + m.Substring(1));
                j.Contra = T.ConsContraMes(ma);
                j.Sem = T.ConsSemMes(ma);
            }
            else
            {
                int n = p == Periodo.Dias7 ? 7 : 30;
                j.Ini = hoje.AddDays(-(n - 1));
                j.AntFim = j.Ini.AddDays(-1);
                j.AntIni = j.Ini.AddDays(-n);
                j.Nome = T.ConsUltimosDias(n);
                j.Contra = T.ConsContraDias(n);
                j.Sem = T.ConsSemDias(n);
            }
            return j;
        }

        public static Resultado Calcular(List<Aparelho> lista, Periodo periodo, DateTime agora, PreferenciasCasa p)
        {
            Resultado r = new Resultado();
            Janela j = r.Janela = JanelaDe(periodo, agora);
            r.Tarifa = p.Tarifa;
            r.NumDias = (int)(j.Fim - j.Ini).TotalDays + 1;
            List<Aparelho> ordem = new List<Aparelho>(lista);
            ordem.Sort(delegate(Aparelho a, Aparelho b) { return (a.ArCondicionado ? 0 : 1).CompareTo(b.ArCondicionado ? 0 : 1); });
            foreach (Aparelho a in ordem)
            {
                if (!a.ArCondicionado && !a.Lavadora) continue;
                Dictionary<DateTime, Dia> dias = new Dictionary<DateTime, Dia>();
                for (DateTime d = j.AntIni; d <= j.Fim; d = d.AddDays(1)) { Dia x = new Dia(); x.Data = d; dias[d] = x; }
                if (a.ArCondicionado)
                {
                    foreach (KeyValuePair<DateTime, double[]> kv in HistoricoDoAr.PorDia(a.Puid, j.AntIni, agora, p, a.Tipo))
                    {
                        Dia x;
                        if (!dias.TryGetValue(kv.Key, out x)) continue;
                        x.Horas = kv.Value[0]; x.Kwh = kv.Value[1]; x.Custo = kv.Value[1] * p.Tarifa;
                    }
                }
                else
                {
                    foreach (CiclosDaLavadora.Ciclo c in CiclosDaLavadora.Todos(a.Puid))
                    {
                        Dia x;
                        if (!dias.TryGetValue(c.Inicio.Date, out x)) continue;
                        x.Kwh += c.Energia; x.Custo += c.Energia * p.Tarifa; x.Agua += c.Agua; x.Ciclos++;
                    }
                }
                DoAparelho da = new DoAparelho();
                da.Aparelho = a;
                for (DateTime d = j.AntIni; d <= j.Fim; d = d.AddDays(1))
                {
                    Dia x = dias[d];
                    if (d >= j.Ini)
                    {
                        da.Dias.Add(x);
                        da.Kwh += x.Kwh; da.Custo += x.Custo; da.Horas += x.Horas; da.Agua += x.Agua; da.Ciclos += x.Ciclos;
                    }
                    else if (d <= j.AntFim) da.CustoAnterior += x.Custo;
                }
                r.Aparelhos.Add(da);
                r.Kwh += da.Kwh; r.Custo += da.Custo; r.Agua += da.Agua; r.CustoAnterior += da.CustoAnterior;
            }
            return r;
        }
    }

    /// <summary>Cabecalho dos cartoes da aba Consumo, igual ao dos aparelhos: icone, nome, detalhe e a linha.</summary>
    internal static class CabecalhoDeConsumo
    {
        public static readonly int Altura = Ui.S(64);

        public static void Desenhar(Graphics g, int largura, string titulo, string detalhe, Action<Graphics, Rectangle, Color> icone, int direita)
        {
            int m = Ui.S(18);
            Rectangle ic = new Rectangle(m, Ui.S(14), Ui.S(36), Ui.S(36));
            using (GraphicsPath p = Ui.RoundRect(ic, Ui.S(10)))
            using (SolidBrush b = new SolidBrush(Ui.AccentSoft))
                g.FillPath(b, p);
            icone(g, Rectangle.Inflate(ic, -Ui.S(8), -Ui.S(8)), Ui.AccentText);
            int tx = ic.Right + Ui.S(12), tw = Math.Max(Ui.S(40), direita - tx - Ui.S(8));
            TextRenderer.DrawText(g, titulo, Ui.FontSection, new Rectangle(tx, Ui.S(12), tw, Ui.S(24)), Ui.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, detalhe, Ui.FontSmall, new Rectangle(tx, Ui.S(36), tw, Ui.S(18)), Ui.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            using (Pen pen = new Pen(Ui.Border)) g.DrawLine(pen, 1, Altura, largura - 2, Altura);
        }

        /// <summary>Tres barras crescentes: o icone do consumo, como na barra lateral do painel web.</summary>
        public static void IconeBarras(Graphics g, Rectangle r, Color c)
        {
            using (Pen p = new Pen(c, Ui.S(2)))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                float y0 = r.Bottom - Ui.S(1);
                g.DrawLine(p, r.X, y0, r.Right, y0);
                g.DrawLine(p, r.X + r.Width * .25f, y0 - Ui.S(4), r.X + r.Width * .25f, r.Y + r.Height * .55f);
                g.DrawLine(p, r.X + r.Width * .5f, y0 - Ui.S(4), r.X + r.Width * .5f, r.Y + r.Height * .1f);
                g.DrawLine(p, r.X + r.Width * .75f, y0 - Ui.S(4), r.X + r.Width * .75f, r.Y + r.Height * .35f);
            }
        }
    }

    /// <summary>
    /// O resumo da aba Consumo: o periodo (7 dias, 30 dias, este mes), custo,
    /// energia e agua com a comparacao, uma linha por aparelho e a energia por
    /// dia com o ar e a lavadora empilhados. Igual ao primeiro cartao da aba
    /// Consumo do painel web.
    /// </summary>
    internal sealed class ResumoDoConsumo : Card
    {
        private readonly Segmented _periodo;
        private List<Aparelho> _lista = new List<Aparelho>();
        private ConsumoDoPeriodo.Resultado _r;
        private Rectangle _barras;
        private int _sel = -1;
        private static readonly int M = Ui.S(18);
        private const int AlturaBarras = 160;

        internal ConsumoDoPeriodo.Resultado Resultado { get { return _r; } }
        internal Segmented Periodo { get { return _periodo; } }

        public ResumoDoConsumo()
        {
            _periodo = new Segmented();
            _periodo.SetItems(T.ConsSeteDias, T.ConsTrintaDias, T.ConsEsteMes);
            _periodo.SelectedIndex = Math.Max(0, Math.Min(2, ServicoCasa.Pref.PeriodoConsumo));
            _periodo.SelectedIndexChanged += delegate
            {
                ServicoCasa.Pref.PeriodoConsumo = _periodo.SelectedIndex;
                ServicoCasa.Pref.Salvar();
                Mostrar(_lista);
            };
            Controls.Add(_periodo);
        }

        public void Mostrar(List<Aparelho> lista)
        {
            _lista = lista ?? new List<Aparelho>();
            _r = ConsumoDoPeriodo.Calcular(_lista, (ConsumoDoPeriodo.Periodo)_periodo.SelectedIndex, DateTime.Now, ServicoCasa.Pref);
            _sel = -1;
            Invalidate();
        }

        private int LinhasDeAparelho { get { return _r == null ? 0 : _r.Aparelhos.Count; } }

        public int Arranjar(int w)
        {
            int pw = Ui.S(300);
            _periodo.SetBounds(w - M - pw, (CabecalhoDeConsumo.Altura - Ui.S(32)) / 2, pw, Ui.S(32));
            int y = CabecalhoDeConsumo.Altura + Ui.S(18);
            y += Ui.S(84) + Ui.S(18);                                  // destaques
            y += Ui.S(26) + LinhasDeAparelho * Ui.S(48) + Ui.S(18);    // tabela
            y += Ui.S(24);                                             // "Energia por dia"
            _barras = new Rectangle(M, y, w - 2 * M, Ui.S(AlturaBarras));
            y = _barras.Bottom + Ui.S(20) + Ui.S(26) + Ui.S(24);        // eixo, dia escolhido, legenda
            return y + Ui.S(10);
        }

        private int Indice(Point p)
        {
            if (_r == null || _r.NumDias == 0 || !Rectangle.Inflate(_barras, 0, Ui.S(4)).Contains(p)) return -1;
            return Math.Max(0, Math.Min(_r.NumDias - 1, (p.X - _barras.X) * _r.NumDias / Math.Max(1, _barras.Width)));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = Indice(e.Location);
            if (i != _sel) { _sel = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_sel != -1) { _sel = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        private static string R(double v) { return "R$ " + v.ToString("N2", T.Pt ? new CultureInfo("pt-BR") : CultureInfo.InvariantCulture); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_r == null) return;
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            ConsumoDoPeriodo.Janela j = _r.Janela;
            CabecalhoDeConsumo.Desenhar(g, Width, j.Nome, T.ConsDetalheResumo(_r.Tarifa.ToString("0.00")), CabecalhoDeConsumo.IconeBarras, _periodo.Left);
            TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            int iw = Width - 2 * M, y = CabecalhoDeConsumo.Altura + Ui.S(18);

            // --- destaques: custo (com a comparacao), energia, agua
            double? v = _r.Variacao;
            string[][] d = {
                new[] { T.ConsCusto, R(_r.Custo), v.HasValue ? T.ConsVariacao(Math.Abs(v.Value).ToString("0"), v.Value <= 0, j.Contra) : T.ConsSemDados(j.Sem), v.HasValue ? (v.Value <= 0 ? "1" : "2") : "0" },
                new[] { T.ConsEnergia, _r.Kwh.ToString("0.0") + " kWh", T.ConsMediaDia((_r.Kwh / Math.Max(1, _r.NumDias)).ToString("0.00")), "0" },
                new[] { T.ConsAgua, _r.Agua.ToString("0") + " L", T.ConsSoLavadora, "0" } };
            int gap = Ui.S(10), bw = (iw - 2 * gap) / 3;
            for (int i = 0; i < 3; i++)
            {
                Rectangle r = new Rectangle(M + i * (bw + gap), y, bw - 1, Ui.S(84) - 1);
                using (GraphicsPath p = Ui.RoundRect(r, Ui.RaioCartao))
                using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt))
                using (Pen pen = new Pen(Ui.Border)) { g.FillPath(b, p); g.DrawPath(pen, p); }
                int x = r.X + Ui.S(14), w = r.Width - Ui.S(24);
                TextRenderer.DrawText(g, d[i][0], Ui.FontSmall, new Rectangle(x, r.Y + Ui.S(10), w, Ui.S(18)), Ui.Muted, f);
                TextRenderer.DrawText(g, d[i][1], Ui.FontTitle, new Rectangle(x, r.Y + Ui.S(28), w, Ui.S(30)), Ui.Text, f);
                Color cd = d[i][3] == "1" ? Color.FromArgb(0x2E, 0xA0, 0x43) : d[i][3] == "2" ? Ui.Danger : Ui.Muted;
                TextRenderer.DrawText(g, d[i][2], Ui.FontSmall, new Rectangle(x, r.Y + Ui.S(60), w, Ui.S(18)), cd, f);
            }
            y += Ui.S(84) + Ui.S(18);

            // --- uma linha por aparelho: energia, custo, uso e a parte do custo
            int cNome = M, cEn = M + iw * 38 / 100, cCu = M + iw * 52 / 100, cUso = M + iw * 66 / 100, cParte = M + iw * 80 / 100;
            TextFormatFlags dir = f | TextFormatFlags.Right;
            TextRenderer.DrawText(g, T.ConsAparelho, Ui.FontSmall, new Rectangle(cNome, y, cEn - cNome, Ui.S(18)), Ui.Muted, f);
            TextRenderer.DrawText(g, T.ConsEnergia, Ui.FontSmall, new Rectangle(cEn, y, cCu - cEn - Ui.S(8), Ui.S(18)), Ui.Muted, dir);
            TextRenderer.DrawText(g, T.ConsCustoCurto, Ui.FontSmall, new Rectangle(cCu, y, cUso - cCu - Ui.S(8), Ui.S(18)), Ui.Muted, dir);
            TextRenderer.DrawText(g, T.ConsUso, Ui.FontSmall, new Rectangle(cUso, y, cParte - cUso - Ui.S(8), Ui.S(18)), Ui.Muted, dir);
            TextRenderer.DrawText(g, T.ConsParte, Ui.FontSmall, new Rectangle(cParte + Ui.S(8), y, M + iw - cParte - Ui.S(8), Ui.S(18)), Ui.Muted, f);
            y += Ui.S(26);
            using (Pen linha = new Pen(Ui.Border))
                foreach (ConsumoDoPeriodo.DoAparelho a in _r.Aparelhos)
                {
                    g.DrawLine(linha, M, y, M + iw, y);
                    int ym = y + Ui.S(24);
                    using (SolidBrush b = new SolidBrush(a.Cor)) g.FillRectangle(b, cNome, ym - Ui.S(13), Ui.S(10), Ui.S(10));
                    TextRenderer.DrawText(g, a.Aparelho.Nome, Ui.FontMed, new Rectangle(cNome + Ui.S(18), ym - Ui.S(19), cEn - cNome - Ui.S(24), Ui.S(20)), Ui.Text, f);
                    TextRenderer.DrawText(g, a.Aparelho.ArCondicionado ? T.ConsEstimado : T.ConsMedido, Ui.FontSmall,
                        new Rectangle(cNome + Ui.S(18), ym + Ui.S(1), cEn - cNome - Ui.S(24), Ui.S(18)), Ui.Muted, f);
                    TextRenderer.DrawText(g, a.Kwh.ToString("0.0") + " kWh", Ui.FontBase, new Rectangle(cEn, ym - Ui.S(10), cCu - cEn - Ui.S(8), Ui.S(20)), Ui.Text, dir);
                    TextRenderer.DrawText(g, R(a.Custo), Ui.FontBase, new Rectangle(cCu, ym - Ui.S(10), cUso - cCu - Ui.S(8), Ui.S(20)), Ui.Text, dir);
                    string uso = a.Aparelho.ArCondicionado ? T.ConsHorasLigado(a.Horas.ToString("0.0")) : T.ConsCiclosAgua(a.Ciclos, a.Agua.ToString("0"));
                    TextRenderer.DrawText(g, uso, Ui.FontBase, new Rectangle(cUso, ym - Ui.S(10), cParte - cUso - Ui.S(8), Ui.S(20)), Ui.Text, dir);
                    Rectangle trilho = new Rectangle(cParte + Ui.S(8), ym - Ui.S(3), M + iw - cParte - Ui.S(8), Ui.S(6));
                    using (GraphicsPath p = Ui.RoundRect(trilho, Ui.S(3)))
                    using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(b, p);
                    int pw = _r.Custo > 0 ? (int)Math.Round(trilho.Width * a.Custo / _r.Custo) : 0;
                    if (pw >= Ui.S(6))
                        using (GraphicsPath p = Ui.RoundRect(new Rectangle(trilho.X, trilho.Y, pw, trilho.Height), Ui.S(3)))
                        using (SolidBrush b = new SolidBrush(a.Cor)) g.FillPath(b, p);
                    y += Ui.S(48);
                }
            y += Ui.S(18);

            // --- energia por dia, ar e lavadora empilhados
            TextRenderer.DrawText(g, T.LavEnergiaPorDia, Ui.FontSmall, new Point(M, y), Ui.Muted, TextFormatFlags.NoPadding);
            Rectangle area = _barras;
            int n = _r.NumDias;
            double maior = 0.01;
            for (int k = 0; k < n; k++)
            {
                double t = 0;
                foreach (ConsumoDoPeriodo.DoAparelho a in _r.Aparelhos) t += a.Dias[k].Kwh;
                maior = Math.Max(maior, t);
            }
            float col = area.Width / (float)Math.Max(1, n), esp = Math.Min(Ui.S(3), col * .2f);
            for (int k = 0; k < n; k++)
            {
                float x0 = area.X + k * col, cw = col - esp;
                if (k == _sel)
                    using (SolidBrush b = new SolidBrush(Ui.Hover)) g.FillRectangle(b, x0, area.Y, cw, area.Height);
                float yb = area.Bottom;
                // ar embaixo, lavadora em cima (a mesma ordem da web)
                foreach (ConsumoDoPeriodo.DoAparelho a in _r.Aparelhos)
                {
                    float h = (float)(area.Height * a.Dias[k].Kwh / maior);
                    if (h < 0.5f) continue;
                    using (SolidBrush b = new SolidBrush(a.Cor)) g.FillRectangle(b, x0, yb - h, cw, h);
                    yb -= h;
                }
            }
            // eixo: dias da semana com 7 dias; datas espacadas com mais
            int passo = n <= 7 ? 1 : (int)Math.Ceiling(n / 8.0);
            for (int k = 0; k < n; k++)
            {
                if (k != n - 1 && (n - 1 - k) % passo != 0) continue;
                DateTime dia = _r.Aparelhos.Count > 0 ? _r.Aparelhos[0].Dias[k].Data : j.Ini.AddDays(k);
                string rot = k == n - 1 ? T.Today : n <= 7 ? dia.ToString("ddd", CultureInfo.CurrentCulture) : dia.ToString("dd/MM");
                int cx = (int)(area.X + k * col + (col - esp) / 2);
                TextRenderer.DrawText(g, rot, Ui.FontSmall, new Rectangle(cx - Ui.S(40), area.Bottom + Ui.S(4), Ui.S(80), Ui.S(16)), Ui.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }
            // o dia apontado, ou a dica
            int yi = area.Bottom + Ui.S(24);
            string info = T.ConsApontar;
            if (_sel >= 0 && _sel < n && _r.Aparelhos.Count > 0)
            {
                DateTime dia = _r.Aparelhos[0].Dias[_sel].Data;
                List<string> l = new List<string>();
                l.Add(dia.ToString("ddd dd/MM", CultureInfo.CurrentCulture));
                double custo = 0;
                foreach (ConsumoDoPeriodo.DoAparelho a in _r.Aparelhos)
                {
                    l.Add(a.Aparelho.Nome + " " + a.Dias[_sel].Kwh.ToString("0.00") + " kWh");
                    custo += a.Dias[_sel].Custo;
                }
                l.Add(R(custo));
                info = string.Join("  ·  ", l.ToArray());
            }
            TextRenderer.DrawText(g, info, Ui.FontSmall, new Rectangle(M, yi, iw, Ui.S(18)), _sel >= 0 ? Ui.Text : Ui.Muted, f);
            // legenda
            int lx = M, ly = yi + Ui.S(26);
            foreach (ConsumoDoPeriodo.DoAparelho a in _r.Aparelhos)
            {
                using (SolidBrush b = new SolidBrush(a.Cor)) g.FillRectangle(b, lx, ly + Ui.S(3), Ui.S(10), Ui.S(10));
                lx += Ui.S(16);
                TextRenderer.DrawText(g, a.Aparelho.Nome, Ui.FontSmall, new Point(lx, ly), Ui.Muted, TextFormatFlags.NoPadding);
                lx += TextRenderer.MeasureText(a.Aparelho.Nome, Ui.FontSmall).Width + Ui.S(16);
            }
        }
    }

    /// <summary>
    /// Os ciclos da lavadora na aba Consumo: tambor, os ultimos ciclos e a media
    /// por programa. Igual ao ultimo cartao da aba Consumo do painel web.
    /// </summary>
    internal sealed class CartaoCiclosDaLavadora : Card
    {
        private Aparelho _a;
        private List<CiclosDaLavadora.Ciclo> _todos = new List<CiclosDaLavadora.Ciclo>();
        private List<EstatisticasDaLavadora.PorPrograma> _rank = new List<EstatisticasDaLavadora.PorPrograma>();
        private static readonly int M = Ui.S(18);
        private const int MaxUltimos = 8;

        public string Puid { get { return _a != null ? _a.Puid : null; } }

        public void Mostrar(Aparelho a)
        {
            _a = a;
            _todos = a != null ? CiclosDaLavadora.Todos(a.Puid) : new List<CiclosDaLavadora.Ciclo>();
            _rank = EstatisticasDaLavadora.Ranking(_todos);
            Invalidate();
        }

        private int Ultimos { get { return Math.Min(MaxUltimos, _todos.Count); } }

        public int Arranjar(int w)
        {
            int y = CabecalhoDeConsumo.Altura + Ui.S(16) + Ui.S(22);           // tambor
            y += Ui.S(30) + Ui.S(26) + Math.Max(1, Ultimos) * Ui.S(30);          // ultimos ciclos
            if (_rank.Count > 0) y += Ui.S(30) + Ui.S(26) + _rank.Count * Ui.S(30);
            return y + Ui.S(16);
        }

        private static readonly int[] Colunas = { 0, 34, 62, 77, 89 };   // % da largura

        private void Linha(Graphics g, int y, string[] v, Font fonte, Color cor, int iw)
        {
            TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            for (int c = 0; c < v.Length; c++)
            {
                int x0 = M + iw * Colunas[c] / 100, x1 = c + 1 < Colunas.Length ? M + iw * Colunas[c + 1] / 100 - Ui.S(8) : M + iw;
                TextRenderer.DrawText(g, v[c], fonte, new Rectangle(x0, y, x1 - x0, Ui.S(20)), cor, c >= 2 ? f | TextFormatFlags.Right : f);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_a == null) return;
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            CabecalhoDeConsumo.Desenhar(g, Width, T.ConsCiclosDe(_a.Nome),
                T.ConsCiclosGravados(_todos.Count) + (_rank.Count > 0 ? "  ·  " + T.ConsMaisUsado(TabelaDaLavadora.Programa(_rank[0].Programa)) : ""),
                delegate(Graphics gg, Rectangle r, Color c) { IconesCasa.Lavadora(gg, r, c); }, Width - M);
            int iw = Width - 2 * M, y = CabecalhoDeConsumo.Altura + Ui.S(16);
            TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

            DateTime? ultima;
            int desde = EstatisticasDaLavadora.DesdeALimpeza(_todos, out ultima);
            bool pede = _a.Int("DrumCleanFlag", 0) == 1;
            string tambor = pede ? "⚠  " + T.LavLimparTambor : ultima.HasValue ? T.LavTamborDesde(ultima.Value.ToString("dd/MM"), desde) : T.ConsTamborNunca;
            TextRenderer.DrawText(g, tambor, Ui.FontSmall, new Rectangle(M, y, iw, Ui.S(18)), pede ? Ui.Warn : Ui.Muted, f);
            y += Ui.S(22) + Ui.S(10);

            TextRenderer.DrawText(g, T.LavUltimosCiclos, Ui.FontSmall, new Rectangle(M, y, iw, Ui.S(18)), Ui.Muted, f);
            y += Ui.S(26);
            Linha(g, y, new[] { T.ConsInicio, T.ConsPrograma, T.ConsDuracao, T.ConsAgua, T.ConsEnergia }, Ui.FontSmall, Ui.Faint, iw);
            y += Ui.S(26);
            using (Pen linha = new Pen(Ui.Border))
            {
                if (_todos.Count == 0)
                {
                    TextRenderer.DrawText(g, T.LavSemCiclosGravados, Ui.FontSmall, new Rectangle(M, y, iw, Ui.S(18)), Ui.Muted, f);
                    y += Ui.S(30);
                }
                for (int i = 0; i < Ultimos; i++)
                {
                    CiclosDaLavadora.Ciclo c = _todos[_todos.Count - 1 - i];
                    g.DrawLine(linha, M, y - Ui.S(5), M + iw, y - Ui.S(5));
                    Linha(g, y, new[] { c.Inicio.ToString("dd/MM HH:mm"), TabelaDaLavadora.Programa(c.Programa) + (c.Concluido ? "" : " (" + T.LavInterrompido + ")"),
                        CiclosDaLavadora.Duracao(c.Fim - c.Inicio), c.Agua.ToString("0") + " L", c.Energia.ToString("0.00") + " kWh" }, Ui.FontBase, Ui.Text, iw);
                    y += Ui.S(30);
                }
                if (_rank.Count == 0) return;
                y += Ui.S(4);
                TextRenderer.DrawText(g, T.ConsMediaPorPrograma, Ui.FontSmall, new Rectangle(M, y, iw, Ui.S(18)), Ui.Muted, f);
                y += Ui.S(26);
                Linha(g, y, new[] { T.ConsPrograma, T.ConsCiclos, T.ConsDuracao, T.ConsAgua, T.ConsEnergia }, Ui.FontSmall, Ui.Faint, iw);
                y += Ui.S(26);
                foreach (EstatisticasDaLavadora.PorPrograma p in _rank)
                {
                    g.DrawLine(linha, M, y - Ui.S(5), M + iw, y - Ui.S(5));
                    Linha(g, y, new[] { TabelaDaLavadora.Programa(p.Programa), p.Ciclos.ToString(), CiclosDaLavadora.Duracao(TimeSpan.FromMinutes(p.Minutos / p.Ciclos)),
                        (p.Agua / p.Ciclos).ToString("0") + " L", (p.Energia / p.Ciclos).ToString("0.00") + " kWh" }, Ui.FontBase, Ui.Text, iw);
                    y += Ui.S(30);
                }
            }
        }
    }

    /// <summary>
    /// Numeros da lavadora sobre todo o historico gravado: medias por programa
    /// (so os ciclos concluidos, como no painel web) e ciclos desde a limpeza do tambor.
    /// </summary>
    internal static class EstatisticasDaLavadora
    {
        /// <summary>Programa "Limpeza do tambor" no modelo do Thiago.</summary>
        internal const int ProgramaLimpeza = 6;

        internal sealed class PorPrograma
        {
            public int Programa, Ciclos;
            public double Minutos, Agua, Energia;
        }

        public static List<PorPrograma> Ranking(List<CiclosDaLavadora.Ciclo> l)
        {
            Dictionary<int, PorPrograma> d = new Dictionary<int, PorPrograma>();
            foreach (CiclosDaLavadora.Ciclo c in l)
            {
                if (!c.Concluido) continue;
                PorPrograma p;
                if (!d.TryGetValue(c.Programa, out p)) { p = new PorPrograma(); p.Programa = c.Programa; d[c.Programa] = p; }
                p.Ciclos++; p.Minutos += (c.Fim - c.Inicio).TotalMinutes; p.Agua += c.Agua; p.Energia += c.Energia;
            }
            List<PorPrograma> r = new List<PorPrograma>(d.Values);
            r.Sort(delegate(PorPrograma a, PorPrograma b) { return a.Ciclos != b.Ciclos ? b.Ciclos.CompareTo(a.Ciclos) : a.Programa.CompareTo(b.Programa); });
            return r;
        }

        /// <summary>Ciclos depois da ultima limpeza do tambor concluida; todos, se nao houve nenhuma.</summary>
        public static int DesdeALimpeza(List<CiclosDaLavadora.Ciclo> l, out DateTime? ultima)
        {
            ultima = null;
            int idx = -1;
            for (int i = 0; i < l.Count; i++) if (l[i].Programa == ProgramaLimpeza && l[i].Concluido) idx = i;
            if (idx >= 0) ultima = l[idx].Inicio;
            return idx < 0 ? l.Count : l.Count - 1 - idx;
        }
    }

    public static partial class T
    {
        public static string CasaConsumoAba { get { return P("Consumo", "Usage"); } }
        public static string CasaFaixasDoModo { get { return P("faixas: ar ligado, na cor do modo", "bands: AC on, in the mode color"); } }
        public static string CasaTemperaturaDe(string nome) { return P(nome + ": temperatura", nome + ": temperature"); }
        public static string ConsSeteDias { get { return P("7 dias", "7 days"); } }
        public static string ConsTrintaDias { get { return P("30 dias", "30 days"); } }
        public static string ConsEsteMes { get { return P("Este mês", "This month"); } }
        public static string ConsUltimosDias(int n) { return P("Últimos " + n + " dias", "Last " + n + " days"); }
        public static string ConsMesAteHoje(string mes) { return P(mes + " até hoje", mes + " so far"); }
        public static string ConsContraDias(int n) { return P("os " + n + " dias anteriores", "the previous " + n + " days"); }
        public static string ConsSemDias(int n) { return P("dos " + n + " dias anteriores", "for the previous " + n + " days"); }
        public static string ConsContraMes(string mes) { return P("o mesmo dia de " + mes, "the same day of " + mes); }
        public static string ConsSemMes(string mes) { return P("de " + mes, "for " + mes); }
        public static string ConsDetalheResumo(string tarifa) { return P("ar + lavadora, a R$ " + tarifa + "/kWh", "AC + washer, at R$ " + tarifa + "/kWh"); }
        public static string ConsEnergia { get { return P("Energia", "Energy"); } }
        public static string ConsAgua { get { return P("Água", "Water"); } }
        public static string ConsCusto { get { return P("Custo estimado", "Estimated cost"); } }
        public static string ConsCustoCurto { get { return P("Custo", "Cost"); } }
        public static string ConsVariacao(string pct, bool caiu, string contra) { return (caiu ? "▼ " : "▲ ") + pct + P("% contra ", "% vs. ") + contra; }
        public static string ConsSemDados(string sem) { return P("sem dados " + sem + " para comparar", "no data " + sem + " to compare"); }
        public static string ConsMediaDia(string kwh) { return P("média de " + kwh + " kWh por dia", kwh + " kWh per day on average"); }
        public static string ConsSoLavadora { get { return P("só a lavadora", "washer only"); } }
        public static string ConsAparelho { get { return P("Aparelho", "Device"); } }
        public static string ConsUso { get { return P("Uso", "Use"); } }
        public static string ConsParte { get { return P("Parte do custo", "Share of cost"); } }
        public static string ConsEstimado { get { return P("estimado pelo tempo ligado", "estimated from time on"); } }
        public static string ConsMedido { get { return P("medido pela máquina", "measured by the machine"); } }
        public static string ConsHorasLigado(string h) { return P(h + " h ligado", h + " h on"); }
        public static string ConsCiclosAgua(int n, string agua) { return n + (n == 1 ? P(" ciclo", " cycle") : P(" ciclos", " cycles")) + " · " + agua + " L"; }
        public static string ConsApontar { get { return P("Passe o mouse numa barra para ver o dia.", "Hover a bar to see the day."); } }
        public static string LavEnergiaPorDia { get { return P("Energia por dia", "Energy per day"); } }
        public static string ConsCiclosDe(string nome) { return P(nome + ": ciclos", nome + ": cycles"); }
        public static string ConsCiclosGravados(int n) { return n + (n == 1 ? P(" ciclo gravado", " cycle recorded") : P(" ciclos gravados", " cycles recorded")); }
        public static string ConsMaisUsado(string p) { return P("mais usado: ", "most used: ") + p; }
        public static string ConsTamborNunca { get { return P("Tambor: nenhuma limpeza gravada ainda. A máquina avisa quando for a hora.", "Drum: no cleaning recorded yet. The machine warns when it's time."); } }
        public static string LavTamborDesde(string data, int ciclos) { return P("Tambor limpo em " + data + ", " + ciclos + " ciclos desde então. A máquina avisa quando for a hora.", "Drum cleaned on " + data + ", " + ciclos + " cycles since. The machine warns when it's time."); }
        public static string ConsInicio { get { return P("Início", "Start"); } }
        public static string ConsPrograma { get { return P("Programa", "Program"); } }
        public static string ConsDuracao { get { return P("Duração", "Duration"); } }
        public static string ConsCiclos { get { return P("Ciclos", "Cycles"); } }
        public static string ConsMediaPorPrograma { get { return P("Média por programa (ciclos concluídos)", "Average per program (completed cycles)"); } }
    }
}
