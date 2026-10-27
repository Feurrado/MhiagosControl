using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MhiagosControl
{
    internal sealed class CenaEventArgs : EventArgs
    {
        public readonly Dictionary<string, int> Propriedades;
        public CenaEventArgs(Dictionary<string, int> p) { Propriedades = p; }
    }

    /// <summary>
    /// Historico e consumo do ar: curva de ambiente e alvo, horas ligado por
    /// dia, consumo ESTIMADO e as preferencias de potencia, tarifa e alertas.
    /// </summary>
    internal sealed class CartaoHistoricoDoAr : Card
    {
        private Aparelho _a;
        private readonly Segmented _periodo;
        private readonly Label _lbConsumo, _nota;
        private Rectangle _grafico, _barras;
        private static readonly int[] Horas = { 6, 24, 24 * 7 };
        private static readonly int M = Ui.S(18);

        public string Puid { get { return _a != null ? _a.Puid : null; } }

        public CartaoHistoricoDoAr()
        {
            Title = T.CasaHistoricoTitulo;
            TitleHeight = Ui.S(42);
            _periodo = new Segmented();
            _periodo.SetItems("6 h", "24 h", T.CasaSeteDias);
            _periodo.SelectedIndex = 1;
            _periodo.SelectedIndexChanged += delegate { Invalidate(); };
            Controls.Add(_periodo);

            _lbConsumo = Rotulo("", Ui.FontBase, Ui.Text);
            _nota = Rotulo(T.CasaEstimativaNotaAjustes, Ui.FontSmall, Ui.Muted);
        }

        /// <summary>Recalcula com a potencia e a tarifa novas (vem da aba Ajustes).</summary>
        public void Recalcular() { Mostrar(_a); }

        private Label Rotulo(string t, Font f, Color c)
        {
            Label l = new Label();
            l.Text = t; l.Font = f; l.ForeColor = c; l.AutoSize = false; l.BackColor = Color.Transparent;
            Controls.Add(l);
            return l;
        }

        public void Mostrar(Aparelho a)
        {
            _a = a;
            if (a == null) return;
            SortedDictionary<DateTime, double[]> mes = HistoricoDoAr.PorDia(a.Puid,
                new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Now, ServicoCasa.Pref, a.Tipo);
            double[] hoje = mes.ContainsKey(DateTime.Today) ? mes[DateTime.Today] : new double[2];
            double hMes = 0, kMes = 0;
            foreach (double[] v in mes.Values) { hMes += v[0]; kMes += v[1]; }
            double tarifa = ServicoCasa.Pref.Tarifa;
            _lbConsumo.Text = T.CasaConsumoHoje(Horas2(hoje[0]), hoje[1].ToString("0.0"), (hoje[1] * tarifa).ToString("0.00")) + "\n" +
                T.CasaConsumoMes(Horas2(hMes), kMes.ToString("0.0"), (kMes * tarifa).ToString("0.00"));
            Invalidate();
        }

        private static string Horas2(double h)
        {
            int min = (int)Math.Round(h * 60);
            return min < 60 ? min + " min" : string.Format("{0} h {1:00}", min / 60, min % 60);
        }

        public int Arranjar(int w)
        {
            int iw = w - 2 * M;
            int pw = Ui.S(220);
            _periodo.SetBounds(w - M - pw, Ui.S(6), pw, Ui.S(30));
            int y = TitleHeight + Ui.S(12);
            _grafico = new Rectangle(M, y, iw, Ui.S(170));
            y = _grafico.Bottom + Ui.S(16);
            _barras = new Rectangle(M, y, iw, Ui.S(96));
            y = _barras.Bottom + Ui.S(14);

            Size s = TextRenderer.MeasureText(_lbConsumo.Text.Length > 0 ? _lbConsumo.Text : " ", _lbConsumo.Font, new Size(iw, 0), TextFormatFlags.WordBreak);
            _lbConsumo.SetBounds(M, y, iw, s.Height + Ui.S(2));
            y = _lbConsumo.Bottom + Ui.S(2);
            s = TextRenderer.MeasureText(_nota.Text, _nota.Font, new Size(iw, 0), TextFormatFlags.WordBreak);
            _nota.SetBounds(M, y, iw, s.Height + Ui.S(2));
            y = _nota.Bottom + Ui.S(14);
            return y;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_a == null) return;
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            DesenharCurva(g, _grafico);
            DesenharBarras(g, _barras);
        }

        /// <summary>
        /// Ambiente (linha cheia) e alvo (so com o ar ligado), na janela escolhida.
        /// Faixa clara onde o ar estava ligado. Lacuna de leitura vira ponte
        /// tracejada e apagada, como no grafico de bateria.
        /// </summary>
        private void DesenharCurva(Graphics g, Rectangle r)
        {
            DateTime agora = DateTime.Now;
            TimeSpan janela = TimeSpan.FromHours(Horas[Math.Max(0, Math.Min(2, _periodo.SelectedIndex))]);
            DateTime inicio = agora - janela;
            List<AmostraAr> am = HistoricoDoAr.Amostras(_a.Puid, inicio);
            Rectangle area = new Rectangle(r.X + Ui.S(36), r.Y + Ui.S(6), r.Width - Ui.S(36 + 6), r.Height - Ui.S(6 + 20));

            // Escala: das leituras, com folga, sem passar de 10 a 40.
            float min = 18, max = 30;
            bool algum = false;
            foreach (AmostraAr s in am)
                foreach (float v in new[] { s.Ambiente, s.Ligado ? s.Alvo : float.NaN })
                {
                    if (float.IsNaN(v)) continue;
                    if (!algum) { min = v; max = v; algum = true; }
                    min = Math.Min(min, v); max = Math.Max(max, v);
                }
            min = (float)Math.Floor(min - 1); max = (float)Math.Ceiling(max + 1);
            if (max - min < 4) { float m = (min + max) / 2; min = m - 2; max = m + 2; }

            using (Pen grade = new Pen(Ui.Border))
            {
                grade.DashPattern = new float[] { 3, 4 };
                for (int i = 0; i <= 2; i++)
                {
                    float v = min + (max - min) * i / 2f;
                    int y = area.Bottom - (int)(area.Height * i / 2f);
                    g.DrawLine(grade, area.X, y, area.Right, y);
                    TextRenderer.DrawText(g, v.ToString("0") + "°", Ui.FontSmall, new Rectangle(r.X - Ui.S(2), y - Ui.S(8), Ui.S(32), Ui.S(16)),
                        Ui.Faint, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                }
            }
            int yEixo = area.Bottom + Ui.S(5);
            string ini = janela.TotalHours > 24 ? inicio.ToString("dd/MM") : inicio.ToString("HH:mm");
            TextRenderer.DrawText(g, ini, Ui.FontSmall, new Point(area.X, yEixo), Ui.Faint, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, T.Now, Ui.FontSmall, new Rectangle(area.X, yEixo, area.Width, Ui.S(16)), Ui.Faint,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);

            Func<DateTime, float> X = delegate(DateTime t) { return area.X + (float)((t - inicio).TotalSeconds / janela.TotalSeconds) * area.Width; };
            Func<float, float> Y = delegate(float v) { return area.Bottom - area.Height * (v - min) / (max - min); };

            // Faixas do ar ligado.
            using (SolidBrush faixa = new SolidBrush(Color.FromArgb(Ui.Dark ? 28 : 34, Ui.Accent)))
                for (int i = 0; i < am.Count; i++)
                {
                    if (!am[i].Ligado) continue;
                    DateTime ate = i + 1 < am.Count ? am[i + 1].Quando : am[i].Quando.AddMinutes(1);
                    if (ate - am[i].Quando > HistoricoDoAr.Continuo) ate = am[i].Quando + HistoricoDoAr.Continuo;
                    float x0 = Math.Max(area.X, X(am[i].Quando)), x1 = Math.Min(area.Right, X(ate));
                    if (x1 > x0) g.FillRectangle(faixa, x0, area.Y, x1 - x0, area.Height);
                }

            Linha(g, am, X, Y, delegate(AmostraAr s) { return s.Ambiente; }, Color.FromArgb(0xF0, 0x8C, 0x3C), 2f);
            Linha(g, am, X, Y, delegate(AmostraAr s) { return s.Ligado ? s.Alvo : float.NaN; }, Ui.Accent, 1.6f);

            // Legenda.
            int lx = area.X + Ui.S(6), ly = area.Y + Ui.S(2);
            Legenda(g, ref lx, ly, Color.FromArgb(0xF0, 0x8C, 0x3C), T.CasaLegendaAmbiente);
            Legenda(g, ref lx, ly, Ui.Accent, T.CasaLegendaAlvo);

            if (am.Count == 0)
                TextRenderer.DrawText(g, T.CasaSemHistorico, Ui.FontSmall, area, Ui.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        private static void Legenda(Graphics g, ref int x, int y, Color c, string t)
        {
            using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, x, y + Ui.S(6), Ui.S(12), Ui.S(3));
            x += Ui.S(16);
            Size s = TextRenderer.MeasureText(t, Ui.FontSmall);
            TextRenderer.DrawText(g, t, Ui.FontSmall, new Point(x, y), Ui.Muted, TextFormatFlags.NoPadding);
            x += s.Width + Ui.S(10);
        }

        private delegate float Valor(AmostraAr s);

        private static void Linha(Graphics g, List<AmostraAr> am, Func<DateTime, float> X, Func<float, float> Y, Valor v, Color cor, float esp)
        {
            List<List<PointF>> trechos = new List<List<PointF>>();
            List<PointF> pts = new List<PointF>();
            DateTime? anterior = null;
            foreach (AmostraAr s in am)
            {
                float val = v(s);
                bool quebra = float.IsNaN(val) || (anterior.HasValue && s.Quando - anterior.Value > HistoricoDoAr.Continuo);
                if (quebra && pts.Count > 0) { trechos.Add(pts); pts = new List<PointF>(); }
                if (float.IsNaN(val)) { anterior = null; continue; }
                pts.Add(new PointF(X(s.Quando), Y(val)));
                anterior = s.Quando;
            }
            if (pts.Count > 0) trechos.Add(pts);

            using (Pen p = new Pen(cor, esp * Ui.Escala))
            {
                p.LineJoin = LineJoin.Round;
                foreach (List<PointF> t in trechos)
                {
                    if (t.Count == 1) using (SolidBrush b = new SolidBrush(cor)) g.FillEllipse(b, t[0].X - 2, t[0].Y - 2, 4, 4);
                    else g.DrawLines(p, t.ToArray());
                }
            }
            // Ponte tracejada entre trechos: tempo sem leitura, nao medida.
            using (Pen ponte = new Pen(Color.FromArgb(120, cor), 1.4f))
            {
                ponte.DashPattern = new float[] { 3f, 3f };
                for (int k = 0; k + 1 < trechos.Count; k++)
                    g.DrawLine(ponte, trechos[k][trechos[k].Count - 1], trechos[k + 1][0]);
            }
        }

        /// <summary>Horas ligado em cada um dos ultimos 7 dias.</summary>
        private void DesenharBarras(Graphics g, Rectangle r)
        {
            SortedDictionary<DateTime, double[]> d = HistoricoDoAr.PorDia(_a.Puid, DateTime.Today.AddDays(-6), DateTime.Now, ServicoCasa.Pref, _a.Tipo);
            TextRenderer.DrawText(g, T.CasaHorasLigado, Ui.FontSmall, new Point(r.X, r.Y), Ui.Muted, TextFormatFlags.NoPadding);
            Rectangle area = new Rectangle(r.X, r.Y + Ui.S(22), r.Width, r.Height - Ui.S(22 + 18));
            double maior = 1;
            foreach (double[] v in d.Values) maior = Math.Max(maior, v[0]);
            int n = d.Count, gap = Ui.S(10);
            int bw = (area.Width - gap * (n - 1)) / Math.Max(1, n);
            int i = 0;
            foreach (KeyValuePair<DateTime, double[]> kv in d)
            {
                int x = area.X + i * (bw + gap);
                int h = (int)Math.Round(area.Height * kv.Value[0] / maior);
                Rectangle trilho = new Rectangle(x, area.Y, bw, area.Height);
                using (GraphicsPath p = Ui.RoundRect(trilho, Ui.S(4)))
                using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(b, p);
                if (h > Ui.S(3))
                    using (GraphicsPath p = Ui.RoundRect(new Rectangle(x, area.Bottom - h, bw, h), Ui.S(4)))
                    using (SolidBrush b = new SolidBrush(kv.Key == DateTime.Today ? Ui.Accent : Color.FromArgb(150, Ui.Accent))) g.FillPath(b, p);
                string rot = kv.Key == DateTime.Today ? T.Today : kv.Key.ToString("ddd", CultureInfo.CurrentCulture);
                TextRenderer.DrawText(g, rot, Ui.FontSmall, new Rectangle(x, area.Bottom + Ui.S(3), bw, Ui.S(16)), Ui.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                if (kv.Value[0] > 0.01)
                    TextRenderer.DrawText(g, kv.Value[0].ToString("0.#") + " h", Ui.FontSmall,
                        new Rectangle(x, Math.Max(area.Y, area.Bottom - h - Ui.S(16)), bw, Ui.S(16)), Ui.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                i++;
            }
        }
    }

    public static partial class T
    {
        public static string CasaAr { get { return P("Ar-condicionado", "Air conditioner"); } }
        public static string CasaCenas { get { return P("Cenas", "Scenes"); } }
        public static string CasaCenaDormir { get { return P("Dormir", "Sleep"); } }
        public static string CasaCenaGelar { get { return P("Gelar rápido", "Quick cool"); } }
        public static string CasaCenaEco { get { return P("Econômico", "Economy"); } }
        public static string CasaDesligarEm { get { return P("Desligar em", "Turn off in"); } }
        public static string CasaDiagnostico { get { return P("Diagnóstico", "Diagnostics"); } }
        public static string CasaDiagnosticoFechar { get { return P("Fechar diagnóstico", "Close diagnostics"); } }
        public static string CasaFalhaAtiva { get { return P("FALHA", "FAULT"); } }
        public static string CasaFalhaOk { get { return P("normal", "ok"); } }
        public static string CasaDiagnosticoFonte { get { return P("Lista oficial de falhas do modelo, do autodiagnóstico da Hisense.", "Official fault list for this model, from Hisense self-check."); } }
        public static string CasaUltimaLeitura(string h) { return P("Última leitura da nuvem: " + h, "Last cloud reading: " + h); }
        public static string CasaTimerContagem(string hora, string falta)
        {
            return P("Desliga às " + hora + " (em " + falta + "). Só funciona com o PC ligado e o Mhiagos rodando.",
                     "Turns off at " + hora + " (in " + falta + "). Only works with the PC on and Mhiagos running.");
        }
        public static string CasaTimerTitulo(string nome) { return P(nome + ": timer", nome + ": timer"); }
        public static string CasaTimerDesligou { get { return P("Desliguei o ar, como programado.", "Turned the AC off, as scheduled."); } }
        public static string CasaTimerFalhou(string erro) { return P("Não consegui desligar o ar: " + erro, "Could not turn the AC off: " + erro); }
        public static string CasaAlertaFalhaTitulo(string nome) { return P(nome + ": falha", nome + ": fault"); }
        public static string CasaAlertaOfflineTitulo(string nome) { return P(nome + " desconectado", nome + " offline"); }
        public static string CasaAlertaOfflineTexto(int min) { return P("Sem contato com a nuvem há mais de " + min + " minutos.", "No contact with the cloud for over " + min + " minutes."); }
        public static string CasaAlertaQuenteTitulo(string nome) { return P(nome + ": está quente", nome + ": it's hot"); }
        public static string CasaAlertaQuenteTexto(string t) { return P("O ambiente chegou a " + t + " e o ar está desligado.", "The room reached " + t + " and the AC is off."); }

        public static string CasaHistoricoTitulo { get { return P("Histórico e consumo", "History and usage"); } }
        public static string CasaSeteDias { get { return P("7 dias", "7 days"); } }
        public static string CasaLegendaAmbiente { get { return P("ambiente", "room"); } }
        public static string CasaLegendaAlvo { get { return P("desejada (ligado)", "target (on)"); } }
        public static string CasaSemHistorico { get { return P("Ainda sem leituras guardadas. O histórico começa a ser gravado agora, enquanto o Mhiagos estiver rodando.", "No readings stored yet. History is recorded from now on while Mhiagos is running."); } }
        public static string CasaHorasLigado { get { return P("Horas ligado por dia", "Hours on per day"); } }
        public static string CasaConsumoHoje(string h, string kwh, string reais) { return P("Hoje: ligado " + h + "  ·  ~" + kwh + " kWh  ·  ~R$ " + reais, "Today: on " + h + "  ·  ~" + kwh + " kWh  ·  ~R$ " + reais); }
        public static string CasaConsumoMes(string h, string kwh, string reais) { return P("Este mês: ligado " + h + "  ·  ~" + kwh + " kWh  ·  ~R$ " + reais, "This month: on " + h + "  ·  ~" + kwh + " kWh  ·  ~R$ " + reais); }
        public static string CasaEstimativaNota { get { return P("Consumo ESTIMADO: tempo ligado em cada modo × a potência abaixo. Este modelo não mede consumo. Só conta o tempo com o Mhiagos rodando.", "ESTIMATED usage: time on in each mode × the power below. This model does not measure usage. Only counts time while Mhiagos is running."); } }
        public static string CasaAjustesConsumo { get { return P("Valores padrão, editáveis. O modelo não mede consumo: a estimativa é tempo ligado em cada modo × potência.", "Default values, editable. This model does not measure usage: the estimate is time on in each mode × power."); } }
        public static string CasaEstimativaNotaAjustes { get { return P("Consumo ESTIMADO: tempo ligado em cada modo × a potência definida em Ajustes. Este modelo não mede consumo. Só conta o tempo com o Mhiagos rodando.", "ESTIMATED usage: time on in each mode × the power set in Settings. This model does not measure usage. Only counts time while Mhiagos is running."); } }
        public static string CasaPotenciaFrio { get { return P("Frio (W)", "Cool (W)"); } }
        public static string CasaPotenciaEco { get { return P("ECO (W)", "Eco (W)"); } }
        public static string CasaPotenciaVentilar { get { return P("Ventilar (W)", "Fan (W)"); } }
        public static string CasaTarifa { get { return P("Tarifa (R$/kWh)", "Rate (R$/kWh)"); } }
        public static string CasaAlertasTitulo { get { return P("Alertas no Windows", "Windows alerts"); } }
        public static string CasaAlertaFalhaOpcao { get { return P("Falha do aparelho", "Appliance fault"); } }
        public static string CasaAlertaOfflineOpcao { get { return P("Desconectado da nuvem", "Offline from the cloud"); } }
        public static string CasaAlertaQuenteOpcao { get { return P("Quente e desligado (°C)", "Hot while off (°C)"); } }

        /// <summary>Nome da falha oficial: os conhecidos traduzidos, os outros como a Hisense mandou.</summary>
        public static string CasaFalhaOficial(string chave, string ingles)
        {
            string conhecido = CasaFalha(chave, null);
            return conhecido != chave ? conhecido : ingles;
        }

        public static string CasaCategoria(string ingles)
        {
            switch (ingles)
            {
                case "Sensors": return P("sensores", "sensors");
                case "Communications": return P("comunicação", "communications");
                case "Others": return P("outros", "other");
                default: return ingles;
            }
        }

        public static string CasaOrientacao(string chave, string ingles)
        {
            if (chave.IndexOf("after_sales", StringComparison.OrdinalIgnoreCase) >= 0)
                return P("Procure a assistência técnica.", "Please contact after-sales service for assistance.");
            return ingles;
        }
    }
}
