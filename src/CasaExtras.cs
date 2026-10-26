using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Cenas, timer de desligar e diagnostico: a faixa de acoes embaixo do
    /// painel do ar. Fica num controle proprio para o cartao do ar so ter de
    /// posiciona-lo.
    /// </summary>
    internal sealed class AcoesDoAr : Control
    {
        public event EventHandler<CenaEventArgs> Cena;
        public event EventHandler MudouAltura;

        private readonly Label _lbCenas, _lbTimer, _contagem, _diag;
        private readonly FlatBtn _dormir, _gelar, _eco, _t30, _t60, _t120, _cancelar, _btDiag;
        private readonly System.Windows.Forms.Timer _relogio;
        private Aparelho _a;
        private bool _diagAberto;

        public AcoesDoAr()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _lbCenas = Rotulo(T.CasaCenas, Ui.FontSmall, Ui.Muted);
            _dormir = Botao(T.CasaCenaDormir, delegate { Disparar(CenaDormir()); });
            _gelar = Botao(T.CasaCenaGelar, delegate { Disparar(CenaGelar()); });
            _eco = Botao(T.CasaCenaEco, delegate { Disparar(CenaEco()); });

            _lbTimer = Rotulo(T.CasaDesligarEm, Ui.FontSmall, Ui.Muted);
            _t30 = Botao("30 min", delegate { Timer(30); });
            _t60 = Botao("1 h", delegate { Timer(60); });
            _t120 = Botao("2 h", delegate { Timer(120); });
            _cancelar = Botao(T.Cancel, delegate { ServicoCasa.CancelarTimer(); });
            _contagem = Rotulo("", Ui.FontSmall, Ui.AccentText);

            _btDiag = Botao(T.CasaDiagnostico, delegate { _diagAberto = !_diagAberto; Preencher(); if (MudouAltura != null) MudouAltura(this, EventArgs.Empty); });
            _diag = Rotulo("", Ui.FontSmall, Ui.Text);

            // A contagem anda de segundo em segundo so enquanto ha timer.
            _relogio = new System.Windows.Forms.Timer();
            _relogio.Interval = 1000;
            _relogio.Tick += delegate { Contagem(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _relogio.Dispose();
            base.Dispose(disposing);
        }

        private Label Rotulo(string t, Font f, Color c)
        {
            Label l = new Label();
            l.Text = t; l.Font = f; l.ForeColor = c; l.AutoSize = false; l.BackColor = Color.Transparent;
            Controls.Add(l);
            return l;
        }

        private FlatBtn Botao(string t, EventHandler clique)
        {
            FlatBtn b = new FlatBtn();
            b.Text = t;
            b.Click += clique;
            Controls.Add(b);
            return b;
        }

        private void Timer(int min)
        {
            if (_a == null) return;
            ServicoCasa.DefinirTimer(_a.Puid, min);
        }

        private void Disparar(Dictionary<string, int> p)
        {
            if (p == null || Cena == null) return;
            Cena(this, new CenaEventArgs(p));
        }

        // ---------------- cenas ----------------

        private bool Aceita(string chave, int valor)
        {
            if (_a == null) return false;
            MetaPropriedade m;
            if (_a.Meta == null || !_a.Meta.TryGetValue(chave, out m)) return _a.Tem(chave);
            if (!m.Grava) return false;
            if (m.Valores.Count > 0) return m.Valores.Contains(valor);
            foreach (int[] f in m.Faixas) if (valor >= f[0] && valor <= f[1]) return true;
            return m.Faixas.Count == 0;
        }

        private int Extremo(string chave, bool maior)
        {
            MetaPropriedade m;
            if (_a == null || _a.Meta == null || !_a.Meta.TryGetValue(chave, out m) || m.Valores.Count == 0) return -1;
            List<int> v = new List<int>(m.Valores);
            v.Remove(0);   // 0 e Auto, nao velocidade
            if (v.Count == 0) return -1;
            v.Sort();
            return maior ? v[v.Count - 1] : v[0];
        }

        private int Grau(int celsius)
        {
            return _a != null && _a.Int("t_temp_type", 0) == 1 ? (int)Math.Round(celsius * 9.0 / 5 + 32) : celsius;
        }

        /// <summary>Noturno, 24 graus, ventilador no minimo. Nulo se o modelo nao tem as pecas.</summary>
        internal Dictionary<string, int> CenaDormir()
        {
            int vento = Extremo("t_fan_speed", false);
            if (!Aceita("t_sleep", 1) || !Aceita("t_temp", Grau(24))) return null;
            Dictionary<string, int> p = new Dictionary<string, int>();
            p["t_power"] = 1; p["t_sleep"] = 1; p["t_temp"] = Grau(24);
            if (vento >= 0) p["t_fan_speed"] = vento;
            return p;
        }

        /// <summary>Frio (2), 18 graus (ou o minimo do modelo), ventilador no maximo.</summary>
        internal Dictionary<string, int> CenaGelar()
        {
            if (!Aceita("t_work_mode", 2)) return null;
            int min = Grau(18);
            MetaPropriedade m;
            if (_a.Meta != null && _a.Meta.TryGetValue("t_temp", out m) && m.Faixas.Count > 0)
            {
                int[] f = m.Faixas[_a.Int("t_temp_type", 0) == 1 && m.Faixas.Count > 1 ? 1 : 0];
                min = Math.Max(f[0], min);
            }
            Dictionary<string, int> p = new Dictionary<string, int>();
            p["t_power"] = 1; p["t_work_mode"] = 2; p["t_temp"] = min;
            int vento = Extremo("t_fan_speed", true);
            if (vento >= 0) p["t_fan_speed"] = vento;
            if (Aceita("t_sleep", 0)) p["t_sleep"] = 0;
            return p;
        }

        /// <summary>ECO (5) a 25 graus. So aparece no modelo que tem ECO.</summary>
        internal Dictionary<string, int> CenaEco()
        {
            if (!Aceita("t_work_mode", 5) || !Aceita("t_temp", Grau(25))) return null;
            Dictionary<string, int> p = new Dictionary<string, int>();
            p["t_power"] = 1; p["t_work_mode"] = 5; p["t_temp"] = Grau(25);
            return p;
        }

        // ---------------- estado ----------------

        public void Mostrar(Aparelho a)
        {
            _a = a;
            Preencher();
        }

        internal bool DiagnosticoAberto { get { return _diagAberto; } }

        private void Preencher()
        {
            bool on = _a != null && _a.Online;
            _dormir.Visible = CenaDormir() != null;
            _gelar.Visible = CenaGelar() != null;
            _eco.Visible = CenaEco() != null;
            _lbCenas.Visible = _dormir.Visible || _gelar.Visible || _eco.Visible;
            foreach (FlatBtn b in new[] { _dormir, _gelar, _eco, _t30, _t60, _t120 }) b.Enabled = on;
            bool podeDesligar = _a != null && Aceita("t_power", 0);
            _lbTimer.Visible = _t30.Visible = _t60.Visible = _t120.Visible = podeDesligar;
            Contagem();

            _btDiag.Text = _diagAberto ? T.CasaDiagnosticoFechar : T.CasaDiagnostico;
            _diag.Visible = _diagAberto;
            if (_diagAberto) _diag.Text = TextoDoDiagnostico();
        }

        private void Contagem()
        {
            DateTime? em = ServicoCasa.DesligarEm;
            bool ativo = em.HasValue && _a != null && ServicoCasa.DesligarPuid == _a.Puid;
            _cancelar.Visible = ativo;
            _contagem.Visible = ativo;
            if (ativo)
            {
                TimeSpan falta = em.Value - DateTime.Now;
                if (falta < TimeSpan.Zero) falta = TimeSpan.Zero;
                _contagem.Text = T.CasaTimerContagem(em.Value.ToString("HH:mm"),
                    falta.TotalHours >= 1 ? string.Format("{0} h {1:00} min", (int)falta.TotalHours, falta.Minutes)
                                          : string.Format("{0}:{1:00}", falta.Minutes, falta.Seconds));
                if (!_relogio.Enabled) _relogio.Start();
            }
            else _relogio.Stop();
        }

        internal string TextoDoDiagnostico()
        {
            if (_a == null) return "";
            List<string> l = new List<string>();
            List<FalhaOficial> lista = Diagnostico.Lista(_a.Puid);
            if (lista == null || lista.Count == 0)
            {
                // Sem a lista oficial ainda: as f_e_* que o aparelho publica.
                foreach (KeyValuePair<string, string> kv in _a.Estado)
                    if (kv.Key.StartsWith("f_e_") && kv.Key != "f_e_push")
                        l.Add((_a.Int(kv.Key, 0) != 0 ? "●  " + T.CasaFalhaAtiva : "○  " + T.CasaFalhaOk) + "  ·  " + T.CasaFalha(kv.Key, null));
            }
            else
                foreach (FalhaOficial f in lista)
                {
                    bool ativa = _a.Tem(f.Chave) && _a.Estado[f.Chave] == f.ValorDeFalha;
                    string s = (ativa ? "●  " + T.CasaFalhaAtiva : "○  " + T.CasaFalhaOk) + "  ·  " + f.Nome;
                    if (f.Categoria.Length > 0) s += " (" + f.Categoria + ")";
                    if (ativa && f.Orientacao.Length > 0) s += "\n      " + f.Orientacao;
                    l.Add(s);
                }
            if (_a.Int("f_e_push", 0) != 0) l.Add("●  " + string.Format(T.CasaAlertaCodigo, _a.Int("f_e_push", 0)));
            DateTime ult = ServicoCasa.UltimaLeitura;
            l.Add("");
            l.Add(T.CasaUltimaLeitura(ult == DateTime.MinValue ? "--" : ult.ToString("HH:mm:ss")) + "  ·  " +
                (_a.Online ? T.CasaOnline : T.CasaOffline));
            if (lista != null && lista.Count > 0) l.Add(T.CasaDiagnosticoFonte);
            return string.Join("\n", l.ToArray());
        }

        /// <summary>Posiciona na largura dada e devolve a altura.</summary>
        public int Arranjar(int w)
        {
            int y = Ui.S(14);
            int h = Ui.S(32);
            int x = 0;
            bool umaLinha = w >= Ui.S(860);

            if (_lbCenas.Visible)
            {
                _lbCenas.SetBounds(0, y, Ui.S(200), Ui.S(18));
                x = 0;
                foreach (FlatBtn b in new[] { _dormir, _gelar, _eco })
                {
                    if (!b.Visible) continue;
                    int bw = Largura(b);
                    b.SetBounds(x, y + Ui.S(20), bw, h);
                    x += bw + Ui.S(8);
                }
            }
            int xt = umaLinha && _lbCenas.Visible ? Math.Max(x + Ui.S(24), w / 2) : 0;
            int yt = umaLinha || !_lbCenas.Visible ? y : y + Ui.S(20) + h + Ui.S(12);
            if (_lbTimer.Visible)
            {
                _lbTimer.SetBounds(xt, yt, Ui.S(200), Ui.S(18));
                int xx = xt;
                foreach (FlatBtn b in new[] { _t30, _t60, _t120, _cancelar })
                {
                    if (!b.Visible) continue;
                    int bw = Largura(b);
                    b.SetBounds(xx, yt + Ui.S(20), bw, h);
                    xx += bw + Ui.S(8);
                }
            }
            y = Math.Max(y + Ui.S(20) + h, yt + Ui.S(20) + h) + Ui.S(8);
            if (_contagem.Visible)
            {
                _contagem.SetBounds(xt, y, w - xt, Ui.S(36));
                Size s = TextRenderer.MeasureText(_contagem.Text, _contagem.Font, new Size(w - xt, 0), TextFormatFlags.WordBreak);
                _contagem.Height = s.Height + Ui.S(2);
                y += _contagem.Height + Ui.S(6);
            }

            int bd = Largura(_btDiag);
            _btDiag.SetBounds(0, y + Ui.S(4), bd, h);
            y += h + Ui.S(12);
            if (_diag.Visible)
            {
                Size s = TextRenderer.MeasureText(_diag.Text, _diag.Font, new Size(w, 0), TextFormatFlags.WordBreak);
                _diag.SetBounds(0, y, w, s.Height + Ui.S(2));
                y = _diag.Bottom + Ui.S(8);
            }
            return y;
        }

        private static int Largura(FlatBtn b) { return TextRenderer.MeasureText(b.Text, b.Font).Width + Ui.S(28); }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Ui.Surface)) e.Graphics.FillRectangle(b, ClientRectangle);
            using (Pen p = new Pen(Ui.Border)) e.Graphics.DrawLine(p, 0, 0, Width, 0);
        }
    }

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
        private readonly Label _lbConsumo, _nota, _lbAjustes, _lbAlertas;
        private readonly CampoDeTexto _wFrio, _wEco, _wVent, _tarifa;
        private readonly Label _lwFrio, _lwEco, _lwVent, _ltarifa;
        private readonly Toggle _alFalha, _alOffline, _alQuente;
        private readonly NumberBox _limite;
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
            _nota = Rotulo(T.CasaEstimativaNota, Ui.FontSmall, Ui.Muted);
            _lbAjustes = Rotulo(T.CasaAjustesConsumo, Ui.FontSmall, Ui.Muted);

            PreferenciasCasa p = ServicoCasa.Pref;
            _lwFrio = Rotulo(T.CasaPotenciaFrio, Ui.FontSmall, Ui.Text);
            _wFrio = Campo(p.PotenciaFrio.ToString(CultureInfo.InvariantCulture));
            _lwEco = Rotulo(T.CasaPotenciaEco, Ui.FontSmall, Ui.Text);
            _wEco = Campo(p.PotenciaEco.ToString(CultureInfo.InvariantCulture));
            _lwVent = Rotulo(T.CasaPotenciaVentilar, Ui.FontSmall, Ui.Text);
            _wVent = Campo(p.PotenciaVentilar.ToString(CultureInfo.InvariantCulture));
            _ltarifa = Rotulo(T.CasaTarifa, Ui.FontSmall, Ui.Text);
            _tarifa = Campo(p.Tarifa.ToString("0.00##", CultureInfo.CurrentCulture));

            _lbAlertas = Rotulo(T.CasaAlertasTitulo, Ui.FontSmall, Ui.Muted);
            _alFalha = Chave(T.CasaAlertaFalhaOpcao, p.AlertaFalha, delegate(bool v) { ServicoCasa.Pref.AlertaFalha = v; });
            _alOffline = Chave(T.CasaAlertaOfflineOpcao, p.AlertaDesconectado, delegate(bool v) { ServicoCasa.Pref.AlertaDesconectado = v; });
            _alQuente = Chave(T.CasaAlertaQuenteOpcao, p.AlertaQuente, delegate(bool v) { ServicoCasa.Pref.AlertaQuente = v; });
            _limite = new NumberBox();
            _limite.Minimum = 20; _limite.Maximum = 40;
            _limite.Value = p.LimiteQuente;
            _limite.ValueChanged += delegate { ServicoCasa.Pref.LimiteQuente = _limite.Value; ServicoCasa.Pref.Salvar(); };
            Controls.Add(_limite);
        }

        private Label Rotulo(string t, Font f, Color c)
        {
            Label l = new Label();
            l.Text = t; l.Font = f; l.ForeColor = c; l.AutoSize = false; l.BackColor = Color.Transparent;
            Controls.Add(l);
            return l;
        }

        private CampoDeTexto Campo(string valor)
        {
            CampoDeTexto c = new CampoDeTexto();
            c.Icone = null;
            c.Texto = valor;
            c.Confirmar += delegate { Gravar(); };
            c.Leave += delegate { Gravar(); };
            Controls.Add(c);
            return c;
        }

        private Toggle Chave(string rotulo, bool valor, Action<bool> grava)
        {
            Toggle t = new Toggle();
            t.Label = rotulo;
            t.Checked = valor;
            t.CheckedChanged += delegate { grava(t.Checked); ServicoCasa.Pref.Salvar(); };
            Controls.Add(t);
            return t;
        }

        /// <summary>Le os campos de potencia e tarifa; valor invalido volta ao anterior.</summary>
        private void Gravar()
        {
            PreferenciasCasa p = ServicoCasa.Pref;
            int w;
            if (int.TryParse(_wFrio.Texto.Trim(), out w) && w >= 0 && w <= 10000) p.PotenciaFrio = w;
            if (int.TryParse(_wEco.Texto.Trim(), out w) && w >= 0 && w <= 10000) p.PotenciaEco = w;
            if (int.TryParse(_wVent.Texto.Trim(), out w) && w >= 0 && w <= 10000) p.PotenciaVentilar = w;
            double t;
            string tt = _tarifa.Texto.Trim().Replace("R$", "").Trim();
            if (double.TryParse(tt.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out t) && t >= 0 && t < 100) p.Tarifa = t;
            p.Salvar();
            _wFrio.Texto = p.PotenciaFrio.ToString(CultureInfo.InvariantCulture);
            _wEco.Texto = p.PotenciaEco.ToString(CultureInfo.InvariantCulture);
            _wVent.Texto = p.PotenciaVentilar.ToString(CultureInfo.InvariantCulture);
            _tarifa.Texto = p.Tarifa.ToString("0.00##", CultureInfo.CurrentCulture);
            Mostrar(_a);
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

            // Ajustes: quatro campos rotulados, em uma ou duas linhas.
            _lbAjustes.SetBounds(M, y, iw, Ui.S(18));
            y += Ui.S(22);
            int col = iw >= Ui.S(760) ? 4 : 2;
            int cw = (iw - (col - 1) * Ui.S(16)) / col;
            Label[] rot = { _lwFrio, _lwEco, _lwVent, _ltarifa };
            CampoDeTexto[] cmp = { _wFrio, _wEco, _wVent, _tarifa };
            for (int i = 0; i < 4; i++)
            {
                int cx = M + (i % col) * (cw + Ui.S(16)), cy = y + (i / col) * Ui.S(62);
                rot[i].SetBounds(cx, cy, cw, Ui.S(18));
                cmp[i].SetBounds(cx, cy + Ui.S(20), cw, Ui.S(34));
            }
            y += ((4 + col - 1) / col) * Ui.S(62) + Ui.S(8);

            _lbAlertas.SetBounds(M, y, iw, Ui.S(18));
            y += Ui.S(22);
            int tc = iw >= Ui.S(760) ? 3 : 1;
            int tw = (iw - (tc - 1) * Ui.S(12)) / tc;
            Toggle[] ts = { _alFalha, _alOffline, _alQuente };
            for (int i = 0; i < 3; i++)
                ts[i].SetBounds(M + (i % tc) * (tw + Ui.S(12)), y + (i / tc) * Ui.S(38), tw - (i == 2 ? Ui.S(96) : 0), Ui.S(32));
            _limite.SetBounds(_alQuente.Right + Ui.S(6), _alQuente.Top, Ui.S(88), Ui.S(32));
            y += ((3 + tc - 1) / tc) * Ui.S(38) + Ui.S(10);
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
        public static string CasaAjustesConsumo { get { return P("Para a estimativa (valores padrão, editáveis)", "For the estimate (default values, editable)"); } }
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
