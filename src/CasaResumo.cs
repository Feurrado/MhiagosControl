using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// A casa na Visao geral: o ar (ambiente, alvo com - e +, liga/desliga) e
    /// a lavadora (fase, tempo restante e termino). Le do ServicoCasa e manda
    /// pelo mesmo caminho da pagina Casa. Sem conta, um convite para entrar.
    /// </summary>
    internal sealed class ResumoDaCasa : Card
    {
        /// <summary>Clique no nome de um aparelho ou no convite: abrir a pagina Casa.</summary>
        public event EventHandler AbrirCasa;

        private readonly EventHandler _mudou;
        private Aparelho _ar, _lav;
        private readonly BotaoRedondo _menos, _mais, _liga;
        private readonly AnelDoCiclo _anel;
        private readonly FlatBtn _convite;
        private readonly System.Windows.Forms.Timer _espera, _relogio;
        private int _alvoPendente = -1;
        private LigadoAnimado _lig;
        private NumeroDeslizante _num;
        private LigadoAnimado Lig { get { return _lig ?? (_lig = new LigadoAnimado(this)); } }
        private NumeroDeslizante Num { get { return _num ?? (_num = new NumeroDeslizante(this)); } }
        private Rectangle _blocoAr, _blocoLav, _nomeAr, _nomeLav;

        public const int Altura = 104;

        internal Aparelho Ar { get { return _ar; } }
        internal Aparelho Lavadora { get { return _lav; } }
        internal BotaoRedondo BotaoLiga { get { return _liga; } }
        internal BotaoRedondo BotaoMais { get { return _mais; } }

        public ResumoDaCasa()
        {
            Title = null;
            _menos = new BotaoRedondo("\uE738"); _menos.AccessibleName = T.CasaMenosGrau; _menos.Click += delegate { Mudar(-1); };
            _mais = new BotaoRedondo("\uE710"); _mais.AccessibleName = T.CasaMaisGrau; _mais.Click += delegate { Mudar(+1); };
            _liga = new BotaoRedondo("\uE7E8"); _liga.AccessibleName = T.CasaLigaDesliga;
            _liga.Click += delegate { if (_ar != null) Mandar("t_power", _ar.Int("t_power", 0) != 0 ? 0 : 1); };
            Controls.Add(_menos); Controls.Add(_mais); Controls.Add(_liga);
            _anel = new AnelDoCiclo();
            Controls.Add(_anel);
            _convite = new FlatBtn();
            _convite.Text = T.CasaResumoConvite;
            _convite.Primary = true;
            _convite.Click += delegate { if (AbrirCasa != null) AbrirCasa(this, EventArgs.Empty); };
            Controls.Add(_convite);

            _espera = new System.Windows.Forms.Timer();
            _espera.Interval = 700;
            _espera.Tick += delegate
            {
                _espera.Stop();
                if (_alvoPendente < 0) return;
                int v = _alvoPendente;
                _alvoPendente = -1;
                Mandar("t_temp", v);
            };
            // O tempo restante da lavadora anda de minuto em minuto entre leituras.
            _relogio = new System.Windows.Forms.Timer();
            _relogio.Interval = 30000;
            _relogio.Tick += delegate { Invalidate(); };
            _relogio.Start();

            _mudou = delegate { NaInterface(Puxar); };
            ServicoCasa.Mudou += _mudou;
            Puxar();
        }

        private void NaInterface(MethodInvoker m)
        {
            try
            {
                if (IsDisposed) return;
                // sem handle ainda: nada de mexer no controle da thread do servico; vai para a da interface
                if (!IsHandleCreated) { Motion.NaInterface(m); return; }
                if (InvokeRequired) BeginInvoke(m); else m();
            }
            catch (InvalidOperationException) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServicoCasa.Mudou -= _mudou;
                _espera.Dispose();
                _relogio.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>Le o primeiro ar e a primeira lavadora do servico.</summary>
        internal void Puxar()
        {
            _ar = null; _lav = null;
            if (ServicoCasa.Logado)
                foreach (Aparelho a in ServicoCasa.Aparelhos)
                {
                    if (_ar == null && a.ArCondicionado) _ar = a;
                    // a lavadora so aparece online e em ciclo
                    if (_lav == null && a.Lavadora && ItensDaCasa.CicloAtivo(a)) _lav = a;
                }
            Preencher();
            Arranjar();
            Invalidate();
        }

        private bool Logado { get { return ServicoCasa.Logado; } }

        private void Preencher()
        {
            bool temAr = _ar != null;
            _menos.Visible = _mais.Visible = _liga.Visible = temAr;
            if (temAr)
            {
                bool on = _ar.Online, ligado = _ar.Int("t_power", 0) != 0;
                _liga.Aceso = ligado;
                _liga.Enabled = on;
                _menos.Enabled = _mais.Enabled = on && ligado && _ar.Tem("t_temp");
                _liga.Invalidate();
            }
            _anel.Visible = _lav != null;
            if (_lav != null)
            {
                CicloAtual c = CicloAtual.De(_lav);
                _anel.Progresso = c.Online ? c.Progresso : 0f;
                _anel.Apagado = !c.Online || !c.Rodando;
                _anel.Cor = c.Erro ? Ui.Danger : c.Concluido ? Color.FromArgb(0x2E, 0xA0, 0x43) : Ui.Accent;
                _anel.Grande = c.Online && c.Rodando && c.Restante > 0 ? Curto(c.Restante) : (c.Concluido && c.Online ? "✓" : "--");
                _anel.Pequeno = "";
                _anel.Rodape = "";
                _anel.Invalidate();
            }
            _convite.Visible = !Logado;
        }

        private bool Estreito { get { return _blocoAr.Width < Ui.S(440); } }

        private static string Curto(int min) { return min >= 60 ? string.Format("{0}h{1:00}", min / 60, min % 60) : min + "m"; }

        private void Mudar(int passo)
        {
            if (_ar == null || !_ar.Tem("t_temp")) return;
            int min, max;
            CenaCasa.FaixaC(_ar, out min, out max);
            if (_ar.Int("t_temp_type", 0) == 1) { min = (int)Math.Round(min * 9.0 / 5 + 32); max = (int)Math.Round(max * 9.0 / 5 + 32); }
            int atual = _alvoPendente >= 0 ? _alvoPendente : _ar.Int("t_temp", 24);
            _alvoPendente = Math.Max(min, Math.Min(max, atual + passo));
            _espera.Stop(); _espera.Start();
            Invalidate();
        }

        /// <summary>Manda pelo mesmo caminho da pagina, com a previa otimista; cancela o retorno de cena.</summary>
        private void Mandar(string chave, int valor)
        {
            if (_ar == null) return;
            Dictionary<string, string> p = new Dictionary<string, string>();
            p[chave] = valor.ToString(CultureInfo.InvariantCulture);
            _ar.Estado[chave] = p[chave];
            if (ServicoCasa.RetornoPuid == _ar.Puid) ServicoCasa.CancelarRetorno();
            Preencher();
            Invalidate();
            UltimoComando = p;
            ServicoCasa.Enviar(_ar.Puid, p, null);
        }

        /// <summary>Testes: o ultimo comando montado.</summary>
        internal Dictionary<string, string> UltimoComando;

        protected override void OnResize(EventArgs e) { base.OnResize(e); Arranjar(); }

        private void Arranjar()
        {
            if (_menos == null) return;
            int m = Ui.S(14), h = Height;
            int meio = Width / 2;
            bool duas = _ar != null && _lav != null;
            _blocoAr = new Rectangle(0, 0, duas ? meio : Width, h);
            _blocoLav = new Rectangle(duas ? meio : 0, 0, duas ? Width - meio : Width, h);
            int bt = Ui.S(34), lg = Ui.S(40);
            if (_ar != null)
            {
                int dir = _blocoAr.Right - m;
                // Estreito: nome em cima e controles embaixo, para o nome nao virar "Gil ...".
                int yc = Estreito ? h - Ui.S(10) - lg : (h - lg) / 2;
                _liga.SetBounds(dir - lg, yc, lg, lg);
                _mais.SetBounds(_liga.Left - Ui.S(14) - bt, yc + (lg - bt) / 2, bt, bt);
                _menos.SetBounds(_mais.Left - Ui.S(56) - bt, yc + (lg - bt) / 2, bt, bt);
            }
            if (_lav != null)
            {
                int a = Math.Min(h - Ui.S(16), Ui.S(84));
                _anel.SetBounds(_blocoLav.Right - m - a, (h - a) / 2, a, a);
            }
            int cw = TextRenderer.MeasureText(_convite.Text, _convite.Font).Width + Ui.S(32);
            _convite.SetBounds(Width - m - cw, (h - Ui.S(34)) / 2, cw, Ui.S(34));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = _nomeAr.Contains(e.Location) || _nomeLav.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if ((_nomeAr.Contains(e.Location) || _nomeLav.Contains(e.Location)) && AbrirCasa != null) AbrirCasa(this, EventArgs.Empty);
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            int m = Ui.S(14);
            _nomeAr = Rectangle.Empty; _nomeLav = Rectangle.Empty;
            if (!Logado)
            {
                Icone(g, new Rectangle(m, (Height - Ui.S(36)) / 2, Ui.S(36), Ui.S(36)), "\uE80F", null);
                TextRenderer.DrawText(g, T.NavCasa, Ui.FontSection, new Point(m + Ui.S(48), Height / 2 - Ui.S(22)), Ui.Text);
                TextRenderer.DrawText(g, T.CasaResumoSemConta, Ui.FontSmall, new Point(m + Ui.S(48), Height / 2 + Ui.S(2)), Ui.Muted);
                return;
            }
            if (_ar == null && _lav == null)
            {
                TextRenderer.DrawText(g, ServicoCasa.Buscando ? T.CasaCarregando : T.CasaSemAparelhos, Ui.FontBase,
                    new Rectangle(m, 0, Width - 2 * m, Height), Ui.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return;
            }
            if (_ar != null) DesenharAr(g, _blocoAr);
            if (_lav != null) DesenharLavadora(g, _blocoLav);
            if (_ar != null && _lav != null)
                using (Pen p = new Pen(Ui.Border)) g.DrawLine(p, _blocoLav.X, Ui.S(14), _blocoLav.X, Height - Ui.S(14));
        }

        private static void Icone(Graphics g, Rectangle r, string glifo, Action<Graphics, RectangleF, Color> desenho)
        {
            using (GraphicsPath p = Ui.RoundRect(r, Ui.S(10)))
            using (SolidBrush b = new SolidBrush(Ui.AccentSoft)) g.FillPath(b, p);
            if (desenho != null) desenho(g, RectangleF.Inflate(r, -Ui.S(9), -Ui.S(9)), Ui.AccentText);
            else IconesCasa.GlifoCentrado(g, glifo, Ui.FontGlyph11, r, Ui.AccentText);
        }

        private void DesenharAr(Graphics g, Rectangle b)
        {
            int m = Ui.S(14);
            bool f = _ar.Int("t_temp_type", 0) == 1, ligado = _ar.Int("t_power", 0) != 0;
            string u = f ? "°F" : "°C";
            bool estreito = Estreito;
            int yNome = estreito ? Ui.S(12) : Height / 2 - Ui.S(26);
            Rectangle ic = new Rectangle(b.X + m, estreito ? Ui.S(12) : (Height - Ui.S(36)) / 2, Ui.S(36), Ui.S(36));
            Icone(g, ic, "\uE9CA", null);
            int tx = ic.Right + Ui.S(12);
            int tw = Math.Max(Ui.S(40), (estreito ? b.Right - m : _menos.Left - Ui.S(10)) - tx);
            Size sn = TextRenderer.MeasureText(_ar.Nome, Ui.FontMed);
            _nomeAr = new Rectangle(tx, yNome, Math.Min(tw, sn.Width), Ui.S(22));
            TextRenderer.DrawText(g, _ar.Nome, Ui.FontMed, new Rectangle(tx, yNome, tw, Ui.S(22)), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            string linha = !_ar.Online ? T.CasaOffline :
                (_ar.Tem("f_temp_in") ? string.Format(T.CasaAmbienteCurto, _ar.Num("f_temp_in", 0).ToString("0.#") + u) : "") +
                (ligado ? "  ·  " + TabelaDoAr.Modo(_ar.Int("t_work_mode", -1), _ar.Tipo) : "  ·  " + T.CasaDesligado);
            TextRenderer.DrawText(g, linha, Ui.FontSmall, new Rectangle(tx, yNome + Ui.S(24), tw, Ui.S(18)), Ui.Muted,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            int alvo = _alvoPendente >= 0 ? _alvoPendente : _ar.Int("t_temp", 0);
            Rectangle num = new Rectangle(_menos.Right, _liga.Top, _mais.Left - _menos.Right, _liga.Height);
            // o alvo desliza ao mudar e passa do cinza a cor do texto ao ligar
            Lig.Definir(ligado);
            string ta = alvo > 0 ? alvo + "°" : "--";
            Size st = TextRenderer.MeasureText(g, ta, Ui.FontValue, Size.Empty, TextFormatFlags.NoPadding);
            Num.Desenhar(g, ta, Ui.FontValue, new Point(num.X + (num.Width - st.Width) / 2, num.Y + (num.Height - st.Height) / 2), num,
                Ui.Misturar(Ui.Muted, Ui.Text, Lig.Valor), Ui.Surface, TextFormatFlags.NoPadding);
        }

        private void DesenharLavadora(Graphics g, Rectangle b)
        {
            int m = Ui.S(14);
            CicloAtual c = CicloAtual.De(_lav);
            Rectangle ic = new Rectangle(b.X + m + (b.X > 0 ? Ui.S(6) : 0), (Height - Ui.S(36)) / 2, Ui.S(36), Ui.S(36));
            using (GraphicsPath p = Ui.RoundRect(ic, Ui.S(10)))
            using (SolidBrush br = new SolidBrush(Ui.AccentSoft)) g.FillPath(br, p);
            IconesCasa.Lavadora(g, Rectangle.Inflate(ic, -Ui.S(8), -Ui.S(8)), Ui.AccentText);
            int tx = ic.Right + Ui.S(12);
            int tw = Math.Max(Ui.S(40), _anel.Left - Ui.S(10) - tx);
            Size sn = TextRenderer.MeasureText(_lav.Nome, Ui.FontMed);
            _nomeLav = new Rectangle(tx, Height / 2 - Ui.S(26), Math.Min(tw, sn.Width), Ui.S(22));
            TextRenderer.DrawText(g, _lav.Nome, Ui.FontMed, new Rectangle(tx, Height / 2 - Ui.S(26), tw, Ui.S(22)), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            string linha = c.Titulo();
            if (c.Online && c.Rodando && c.Restante > 0)
            {
                DateTime leitura = ServicoCasa.UltimaLeitura == DateTime.MinValue ? DateTime.Now : ServicoCasa.UltimaLeitura;
                linha += "  ·  " + T.LavTerminaAs(leitura.AddMinutes(c.Restante).ToString("HH:mm"));
            }
            TextRenderer.DrawText(g, linha, Ui.FontSmall, new Rectangle(tx, Height / 2 - Ui.S(2), tw, Ui.S(18)),
                c.Erro ? Ui.Danger : Ui.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    public static partial class T
    {
        public static string CasaResumoConvite { get { return P("Abrir a página Casa", "Open the Home page"); } }
        public static string CasaResumoSemConta { get { return P("Conecte o ar-condicionado e a lava e seca pela conta ConnectLife.", "Connect your air conditioner and washer with your ConnectLife account."); } }
    }
}
