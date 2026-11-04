using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Custo ESTIMADO do mes, do mes anterior inteiro e do anterior ate o mesmo
    /// dia, por aparelho: a mesma conta do /api/resumo-mes do servidor da casa.
    /// Ar pelo tempo ligado x potencia (HistoricoDoAr.PorDia); lavadora pela
    /// energia que a propria maquina informa em cada ciclo.
    /// </summary>
    internal static class ResumoDoMes
    {
        internal sealed class Valores
        {
            public double Kwh, Custo, Horas, Agua;
            public int Ciclos;

            public void Somar(Valores v) { Kwh += v.Kwh; Custo += v.Custo; Horas += v.Horas; Agua += v.Agua; Ciclos += v.Ciclos; }
        }

        internal sealed class DoAparelho
        {
            public Aparelho Aparelho;
            public Valores Atual = new Valores(), Anterior = new Valores(), AnteriorAteHoje = new Valores();
        }

        internal sealed class Resultado
        {
            public DateTime Mes, MesAnterior;
            public Valores Atual = new Valores(), Anterior = new Valores(), AnteriorAteHoje = new Valores();
            public readonly List<DoAparelho> Aparelhos = new List<DoAparelho>();
            public double Tarifa;
        }

        public static Resultado Calcular(List<Aparelho> lista, DateTime agora, PreferenciasCasa p)
        {
            Resultado r = new Resultado();
            r.Mes = new DateTime(agora.Year, agora.Month, 1);
            r.MesAnterior = r.Mes.AddMonths(-1);
            r.Tarifa = p.Tarifa;
            // "Ate hoje" no mes anterior: o mesmo dia, ou o ultimo do mes se ele for mais curto.
            DateTime mesmoDia = r.MesAnterior.AddDays(Math.Min(agora.Day, DateTime.DaysInMonth(r.MesAnterior.Year, r.MesAnterior.Month)) - 1);
            DateTime limite = mesmoDia.Date.AddDays(1);

            foreach (Aparelho a in lista)
            {
                DoAparelho d = new DoAparelho();
                d.Aparelho = a;
                if (a.ArCondicionado)
                {
                    foreach (KeyValuePair<DateTime, double[]> kv in HistoricoDoAr.PorDia(a.Puid, r.MesAnterior, agora, p, a.Tipo))
                    {
                        Valores v = new Valores();
                        v.Horas = kv.Value[0]; v.Kwh = kv.Value[1]; v.Custo = kv.Value[1] * p.Tarifa;
                        Somar(d, kv.Key >= r.Mes, kv.Key < limite, v);
                    }
                }
                else if (a.Lavadora)
                {
                    foreach (CiclosDaLavadora.Ciclo c in CiclosDaLavadora.Todos(a.Puid))
                    {
                        if (c.Inicio < r.MesAnterior || c.Inicio > agora) continue;
                        Valores v = new Valores();
                        v.Kwh = c.Energia; v.Custo = c.Energia * p.Tarifa; v.Agua = c.Agua; v.Ciclos = 1;
                        Somar(d, c.Inicio >= r.Mes, c.Inicio < limite, v);
                    }
                }
                else continue;
                r.Atual.Somar(d.Atual); r.Anterior.Somar(d.Anterior); r.AnteriorAteHoje.Somar(d.AnteriorAteHoje);
                r.Aparelhos.Add(d);
            }
            return r;
        }

        private static void Somar(DoAparelho d, bool atual, bool ateHoje, Valores v)
        {
            if (atual) { d.Atual.Somar(v); return; }
            d.Anterior.Somar(v);
            if (ateHoje) d.AnteriorAteHoje.Somar(v);
        }

        /// <summary>Variacao em % contra o mesmo ponto do mes anterior; null sem base.</summary>
        public static double? Variacao(Resultado r)
        {
            if (r.AnteriorAteHoje.Custo < 0.005) return null;
            return (r.Atual.Custo - r.AnteriorAteHoje.Custo) / r.AnteriorAteHoje.Custo * 100;
        }
    }

    /// <summary>
    /// Faixa do topo da Casa com o custo do mes: total com a comparacao, um bloco
    /// por aparelho e o mes anterior inteiro. Igual ao topo do painel web.
    /// </summary>
    internal sealed class CartaoDoMes : Control
    {
        private ResumoDoMes.Resultado _r;
        private string _assinatura = "";

        public CartaoDoMes()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Window;
        }

        internal ResumoDoMes.Resultado Resultado { get { return _r; } }

        /// <summary>Recalcula; redesenha so se algum numero mostrado mudou.</summary>
        public void Mostrar(List<Aparelho> lista)
        {
            _r = ResumoDoMes.Calcular(lista, DateTime.Now, ServicoCasa.Pref);
            string s = string.Join("|", Blocos().ConvertAll(b => b[0] + b[1] + b[2]).ToArray());
            if (s == _assinatura) return;
            _assinatura = s;
            Invalidate();
        }

        private static string Mes(DateTime d) { return d.ToString("MMMM", T.Pt ? new CultureInfo("pt-BR") : CultureInfo.InvariantCulture); }
        private static string Dinheiro(double v) { return T.CasaMoeda(v.ToString("N2", T.Pt ? new CultureInfo("pt-BR") : CultureInfo.InvariantCulture)); }

        /// <summary>Cada bloco: rotulo, valor, detalhe e a cor do detalhe (0 normal, 1 caiu, 2 subiu).</summary>
        internal List<string[]> Blocos()
        {
            List<string[]> l = new List<string[]>();
            if (_r == null) return l;
            double? v = ResumoDoMes.Variacao(_r);
            l.Add(new[] { T.CasaCustoDoMes(Mes(_r.Mes)), Dinheiro(_r.Atual.Custo),
                v.HasValue ? T.CasaComparacaoMes(Math.Abs(v.Value).ToString("0"), v.Value <= 0, Mes(_r.MesAnterior)) : T.CasaSemComparacao(Mes(_r.MesAnterior)),
                v.HasValue ? (v.Value <= 0 ? "1" : "2") : "0" });
            foreach (ResumoDoMes.DoAparelho d in _r.Aparelhos)
            {
                ResumoDoMes.Valores a = d.Atual;
                l.Add(new[] { T.CasaAparelhoNoMes(d.Aparelho.Nome, Mes(_r.Mes)), Dinheiro(a.Custo),
                    d.Aparelho.ArCondicionado ? T.CasaDetalheMesAr(a.Horas.ToString("0.0"), a.Kwh.ToString("0.0"))
                        : T.CasaDetalheMesLavadora(a.Ciclos, a.Agua.ToString("0"), a.Kwh.ToString("0.0")), "0" });
            }
            string ant = Mes(_r.MesAnterior);
            l.Add(new[] { T.CasaMesInteiro(char.ToUpper(ant[0]) + ant.Substring(1)), Dinheiro(_r.Anterior.Custo),
                T.CasaDetalheMesAnterior(_r.Anterior.Kwh.ToString("0.0"), _r.Tarifa.ToString("0.00")), "0" });
            return l;
        }

        private int Colunas(int w, int n) { return Math.Max(1, Math.Min(n, w / Ui.S(200))); }

        public int Arranjar(int w)
        {
            int n = Math.Max(1, Blocos().Count), c = Colunas(w, n);
            int linhas = (n + c - 1) / c;
            return linhas * Ui.S(88) + (linhas - 1) * Ui.S(10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Window)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            List<string[]> l = Blocos();
            if (l.Count == 0) return;
            int c = Colunas(Width, l.Count), gap = Ui.S(10);
            int bw = (Width - (c - 1) * gap) / c, bh = Ui.S(88);
            for (int i = 0; i < l.Count; i++)
            {
                Rectangle r = new Rectangle((i % c) * (bw + gap), (i / c) * (bh + gap), bw - 1, bh - 1);
                using (GraphicsPath p = Ui.RoundRect(r, Ui.RaioCartao))
                using (SolidBrush b = new SolidBrush(Ui.Surface))
                using (Pen pen = new Pen(Ui.Border))
                {
                    g.FillPath(b, p);
                    g.DrawPath(pen, p);
                }
                int x = r.X + Ui.S(16), w = r.Width - Ui.S(28);
                TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                TextRenderer.DrawText(g, l[i][0], Ui.FontSmall, new Rectangle(x, r.Y + Ui.S(12), w, Ui.S(18)), Ui.Muted, f);
                TextRenderer.DrawText(g, l[i][1], Ui.FontTitle, new Rectangle(x, r.Y + Ui.S(30), w, Ui.S(30)), Ui.Text, f);
                Color cd = l[i][3] == "1" ? Color.FromArgb(0x2E, 0xA0, 0x43) : l[i][3] == "2" ? Ui.Danger : Ui.Muted;
                TextRenderer.DrawText(g, l[i][2], Ui.FontSmall, new Rectangle(x, r.Y + Ui.S(62), w, Ui.S(18)), cd, f);
            }
        }
    }

    /// <summary>
    /// Numeros da lavadora sobre todo o historico gravado: ciclos, o programa
    /// mais usado, medias por programa e ciclos desde a limpeza do tambor.
    /// As medias contam so os ciclos concluidos, como no painel web.
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

        /// <summary>O texto da secao, linha a linha.</summary>
        public static string Texto(Aparelho a, DateTime agora)
        {
            List<CiclosDaLavadora.Ciclo> l = CiclosDaLavadora.Todos(a.Puid);
            if (l.Count == 0) return "";   // "Ultimos ciclos" ja diz que nao ha nenhum
            int doMes = 0;
            foreach (CiclosDaLavadora.Ciclo c in l) if (c.Inicio.Year == agora.Year && c.Inicio.Month == agora.Month) doMes++;
            List<PorPrograma> rk = Ranking(l);
            DateTime? ultima;
            int desde = DesdeALimpeza(l, out ultima);
            List<string> linhas = new List<string>();
            linhas.Add(T.LavEstatTotais(l.Count, doMes, rk.Count > 0 ? TabelaDaLavadora.Programa(rk[0].Programa) : "--", rk.Count > 0 ? rk[0].Ciclos : 0));
            linhas.Add(a.Int("DrumCleanFlag", 0) == 1 ? T.LavLimparTambor
                : ultima.HasValue ? T.LavTamborDesde(ultima.Value.ToString("dd/MM"), desde) : T.LavTamborNunca(desde));
            foreach (PorPrograma p in rk)
                linhas.Add(TabelaDaLavadora.Programa(p.Programa) + "  ·  " + p.Ciclos + "x  ·  " +
                    CiclosDaLavadora.Duracao(TimeSpan.FromMinutes(p.Minutos / p.Ciclos)) + "  ·  " +
                    (p.Agua / p.Ciclos).ToString("0") + " L  ·  " + (p.Energia / p.Ciclos).ToString("0.00") + " kWh");
            return string.Join("\n", linhas.ToArray());
        }
    }

    public static partial class T
    {
        public static string CasaMoeda(string v) { return P("R$ ", "R$ ") + v; }
        public static string CasaCustoDoMes(string mes) { return P("Custo estimado de " + mes, "Estimated cost in " + mes); }
        public static string CasaComparacaoMes(string pct, bool caiu, string mesAnterior)
        {
            return (caiu ? "▼ " : "▲ ") + pct + P("% que no mesmo dia de " + mesAnterior, "% vs. the same day of " + mesAnterior);
        }
        public static string CasaSemComparacao(string mesAnterior) { return P("sem dados de " + mesAnterior + " para comparar", "no " + mesAnterior + " data to compare"); }
        public static string CasaAparelhoNoMes(string nome, string mes) { return P(nome + " em " + mes, nome + " in " + mes); }
        public static string CasaDetalheMesAr(string horas, string kwh) { return P(horas + " h ligado · " + kwh + " kWh", horas + " h on · " + kwh + " kWh"); }
        public static string CasaDetalheMesLavadora(int ciclos, string agua, string kwh) { return ciclos + P(" ciclos · ", " cycles · ") + agua + " L · " + kwh + " kWh"; }
        public static string CasaMesInteiro(string mes) { return P(mes + " inteiro", "All of " + mes); }
        public static string CasaDetalheMesAnterior(string kwh, string tarifa) { return kwh + P(" kWh a R$ ", " kWh at R$ ") + tarifa + "/kWh"; }
        public static string LavEstatisticas { get { return P("Ciclos e programas (todo o histórico)", "Cycles and programs (whole history)"); } }
        public static string LavEstatTotais(int total, int mes, string programa, int vezes)
        {
            return P(total + " ciclos no total  ·  " + mes + " neste mês  ·  mais usado: " + programa + " (" + vezes + "x)",
                total + " cycles in total  ·  " + mes + " this month  ·  most used: " + programa + " (" + vezes + "x)");
        }
        public static string LavTamborDesde(string data, int ciclos) { return P("Tambor limpo em " + data + ", " + ciclos + " ciclos desde então", "Drum cleaned on " + data + ", " + ciclos + " cycles since"); }
        public static string LavTamborNunca(int ciclos) { return P("Nenhuma limpeza do tambor gravada (" + ciclos + " ciclos). A máquina avisa quando for a hora.", "No drum clean recorded (" + ciclos + " cycles). The machine warns when it's time."); }
    }
}
