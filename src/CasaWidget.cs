using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>Configuracao do widget da casa, em widget-casa.ini (separado do de bateria).</summary>
    internal sealed class ConfigDoWidgetCasa
    {
        public const int SemPosicao = int.MinValue;
        public bool Ativo, NaFrente = true, Travado;
        public int X = SemPosicao, Y = SemPosicao;

        internal static string Caminho
        {
            get { return _caminho ?? Path.Combine(Paths.DataDir, "widget-casa.ini"); }
            set { _caminho = value; }
        }
        private static string _caminho;

        public bool TemPosicao { get { return X != SemPosicao && Y != SemPosicao; } }

        public static ConfigDoWidgetCasa Carregar()
        {
            ConfigDoWidgetCasa c = new ConfigDoWidgetCasa();
            try
            {
                if (!File.Exists(Caminho)) return c;
                foreach (string linha in File.ReadAllLines(Caminho, Encoding.UTF8))
                {
                    int eq = linha.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = linha.Substring(0, eq).Trim().ToLowerInvariant(), v = linha.Substring(eq + 1).Trim();
                    int i;
                    bool n = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out i);
                    switch (k)
                    {
                        case "ativo": c.Ativo = v == "1"; break;
                        case "nafrente": c.NaFrente = v == "1"; break;
                        case "travado": c.Travado = v == "1"; break;
                        case "x": if (n) c.X = i; break;
                        case "y": if (n) c.Y = i; break;
                    }
                }
            }
            catch (Exception ex) { Log.Error("widget da casa: ler config", ex); }
            return c;
        }

        public void Salvar()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ativo=" + (Ativo ? "1" : "0"));
            sb.AppendLine("nafrente=" + (NaFrente ? "1" : "0"));
            sb.AppendLine("travado=" + (Travado ? "1" : "0"));
            if (TemPosicao)
            {
                sb.AppendLine("x=" + X.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("y=" + Y.ToString(CultureInfo.InvariantCulture));
            }
            try { GravacaoAtomica.Escrever(Caminho, sb.ToString(), new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Error("widget da casa: gravar config", ex); }
        }
    }

    /// <summary>
    /// Liga e desliga o widget da casa de qualquer lugar (bandeja, aba Ajustes).
    /// Tudo na thread da interface.
    /// </summary>
    internal static class GerenteDoWidgetCasa
    {
        private static WidgetDaCasa _widget;
        private static ConfigDoWidgetCasa _cfg;

        /// <summary>Mudou de ligado para desligado ou vice-versa (a bandeja marca o item do menu).</summary>
        public static event EventHandler Mudou;

        public static ConfigDoWidgetCasa Config
        {
            get { return _cfg ?? (_cfg = ConfigDoWidgetCasa.Carregar()); }
        }

        public static bool Ativo { get { return Config.Ativo; } }

        public static void Definir(bool ativo)
        {
            Config.Ativo = ativo;
            Config.Salvar();
            if (ativo) Mostrar(); else Esconder();
            if (Mudou != null) Mudou(null, EventArgs.Empty);
        }

        /// <summary>Abre se estiver ativo na config (no inicio do app).</summary>
        public static void Restaurar() { if (Config.Ativo) Mostrar(); }

        private static void Mostrar()
        {
            if (_widget != null && !_widget.IsDisposed) return;
            try
            {
                _widget = new WidgetDaCasa(Config);
                _widget.FormClosed += delegate { _widget = null; };
                _widget.Show();
            }
            catch (Exception ex) { Log.Error("widget da casa: abrir", ex); }
        }

        private static void Esconder()
        {
            if (_widget == null || _widget.IsDisposed) return;
            _widget.Close();
            _widget = null;
        }

        /// <summary>Pedido do widget para abrir a pagina Casa; a bandeja liga.</summary>
        public static Action AbrirCasa;

        internal static WidgetDaCasa Atual { get { return _widget; } }
    }

    /// <summary>
    /// Widget da casa na area de trabalho: o ar (ambiente, alvo com - e +,
    /// liga/desliga) e a lavadora (fase, tempo restante, termino e barra).
    /// Arrastar move (salvo), botao direito abre o menu.
    /// </summary>
    internal sealed class WidgetDaCasa : Form
    {
        private readonly ConfigDoWidgetCasa _cfg;
        private readonly EventHandler _mudou;
        private Aparelho _ar, _lav;
        private Rectangle _rMenos, _rMais, _rLiga, _rNomeAr, _rNomeLav;
        private int _hover = -1;
        private int _alvoPendente = -1;
        private readonly Timer _espera, _relogio;
        private Point _pegou;
        private bool _arrastando, _moveu;

        internal Aparelho Ar { get { return _ar; } }
        internal Aparelho Lavadora { get { return _lav; } }
        internal Dictionary<string, string> UltimoComando;

        public WidgetDaCasa(ConfigDoWidgetCasa cfg)
        {
            _cfg = cfg;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = cfg.NaFrente;
            Text = T.CasaWidgetTitulo;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(Ui.S(320), Ui.S(156));
            Posicionar();

            _espera = new Timer();
            _espera.Interval = 700;
            _espera.Tick += delegate
            {
                _espera.Stop();
                if (_alvoPendente < 0) return;
                int v = _alvoPendente;
                _alvoPendente = -1;
                Mandar("t_temp", v);
            };
            _relogio = new Timer();
            _relogio.Interval = 30000;
            _relogio.Tick += delegate { Invalidate(); };
            _relogio.Start();

            _mudou = delegate { NaInterface(Puxar); };
            ServicoCasa.Mudou += _mudou;
            Puxar();
        }

        /// <summary>Sem barra de titulo e sem Alt+Tab: e um enfeite da area de trabalho.</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= 0x80;   // WS_EX_TOOLWINDOW
                return p;
            }
        }

        private void NaInterface(MethodInvoker m)
        {
            try
            {
                if (IsDisposed) return;
                if (!IsHandleCreated) { m(); return; }
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

        private void Posicionar()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Point p = _cfg.TemPosicao ? new Point(_cfg.X, _cfg.Y) : new Point(area.Right - Width - Ui.S(24), area.Top + Ui.S(24));
            Rectangle r = new Rectangle(p, Size);
            bool visivel = false;
            foreach (Screen s in Screen.AllScreens) if (s.WorkingArea.IntersectsWith(r)) visivel = true;
            if (!visivel) p = new Point(area.Right - Width - Ui.S(24), area.Top + Ui.S(24));
            Location = p;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (GraphicsPath gp = Ui.RoundRect(new Rectangle(0, 0, Width, Height), Ui.S(14)))
                Region = new Region(gp);
        }

        internal void Puxar()
        {
            _ar = null; _lav = null;
            if (ServicoCasa.Logado)
                foreach (Aparelho a in ServicoCasa.Aparelhos)
                {
                    if (_ar == null && a.ArCondicionado) _ar = a;
                    if (_lav == null && a.Lavadora) _lav = a;
                }
            Invalidate();
        }

        private void Mudar(int passo)
        {
            if (_ar == null || !_ar.Online || _ar.Int("t_power", 0) == 0) return;
            int min, max;
            CenaCasa.FaixaC(_ar, out min, out max);
            if (_ar.Int("t_temp_type", 0) == 1) { min = (int)Math.Round(min * 9.0 / 5 + 32); max = (int)Math.Round(max * 9.0 / 5 + 32); }
            int atual = _alvoPendente >= 0 ? _alvoPendente : _ar.Int("t_temp", 24);
            _alvoPendente = Math.Max(min, Math.Min(max, atual + passo));
            _espera.Stop(); _espera.Start();
            Invalidate();
        }

        private void Mandar(string chave, int valor)
        {
            if (_ar == null) return;
            Dictionary<string, string> p = new Dictionary<string, string>();
            p[chave] = valor.ToString(CultureInfo.InvariantCulture);
            _ar.Estado[chave] = p[chave];
            if (ServicoCasa.RetornoPuid == _ar.Puid) ServicoCasa.CancelarRetorno();
            UltimoComando = p;
            Invalidate();
            ServicoCasa.Enviar(_ar.Puid, p, null);
        }

        // ---------------- mouse ----------------

        private int Zona(Point p)
        {
            if (_rMenos.Contains(p)) return 0;
            if (_rMais.Contains(p)) return 1;
            if (_rLiga.Contains(p)) return 2;
            if (_rNomeAr.Contains(p) || _rNomeLav.Contains(p)) return 3;
            return -1;
        }

        internal void Clicar(int zona)
        {
            switch (zona)
            {
                case 0: Mudar(-1); break;
                case 1: Mudar(+1); break;
                case 2: if (_ar != null && _ar.Online) Mandar("t_power", _ar.Int("t_power", 0) != 0 ? 0 : 1); break;
                case 3: if (GerenteDoWidgetCasa.AbrirCasa != null) GerenteDoWidgetCasa.AbrirCasa(); break;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pegou = e.Location; _arrastando = !_cfg.Travado && Zona(e.Location) < 0; _moveu = false; }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_arrastando && e.Button == MouseButtons.Left)
            {
                Point d = new Point(e.X - _pegou.X, e.Y - _pegou.Y);
                if (Math.Abs(d.X) + Math.Abs(d.Y) > 2) { _moveu = true; Location = new Point(Left + d.X, Top + d.Y); }
                return;
            }
            int z = Zona(e.Location);
            if (z != _hover) { _hover = z; Cursor = z >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (_arrastando && _moveu) { _cfg.X = Left; _cfg.Y = Top; _cfg.Salvar(); }
                else if (!_moveu) Clicar(Zona(e.Location));
                _arrastando = false;
            }
            else if (e.Button == MouseButtons.Right) MostrarMenu(e.Location);
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        /// <summary>Menu nativo, como o do widget de bateria (foreground do TrackPopupMenu).</summary>
        private void MostrarMenu(Point onde)
        {
            ContextMenu m = new ContextMenu();
            m.MenuItems.Add(T.CasaResumoConvite, delegate { Clicar(3); });
            MenuItem frente = new MenuItem(T.CasaWidgetFrente, delegate { _cfg.NaFrente = !_cfg.NaFrente; TopMost = _cfg.NaFrente; _cfg.Salvar(); });
            frente.Checked = _cfg.NaFrente;
            m.MenuItems.Add(frente);
            MenuItem trava = new MenuItem(T.CasaWidgetTravar, delegate { _cfg.Travado = !_cfg.Travado; _cfg.Salvar(); });
            trava.Checked = _cfg.Travado;
            m.MenuItems.Add(trava);
            m.MenuItems.Add("-");
            m.MenuItems.Add(T.CasaWidgetFechar, delegate { GerenteDoWidgetCasa.Definir(false); });
            m.Show(this, onde);
        }

        // ---------------- pintura ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Pintar(e.Graphics);
        }

        internal void Pintar(Graphics g)
        {
            Ui.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillRectangle(b, ClientRectangle);
            using (GraphicsPath p = Ui.RoundRect(r, Ui.S(14)))
            using (Pen pen = new Pen(Ui.Border)) g.DrawPath(pen, p);
            int m = Ui.S(14);
            _rMenos = _rMais = _rLiga = _rNomeAr = _rNomeLav = Rectangle.Empty;
            if (!ServicoCasa.Logado || (_ar == null && _lav == null))
            {
                TextRenderer.DrawText(g, T.NavCasa, Ui.FontSection, new Rectangle(m, m, Width - 2 * m, Ui.S(24)), Ui.Text, TextFormatFlags.Left);
                TextRenderer.DrawText(g, ServicoCasa.Logado ? T.CasaCarregando : T.CasaResumoSemConta, Ui.FontSmall,
                    new Rectangle(m, m + Ui.S(30), Width - 2 * m, Height - 2 * m - Ui.S(30)), Ui.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
                _rNomeAr = new Rectangle(m, m, Width - 2 * m, Ui.S(24));
                return;
            }
            int meio = Height / 2;
            Rectangle linhaAr = new Rectangle(m, 0, Width - 2 * m, meio);
            Rectangle linhaLav = new Rectangle(m, meio, Width - 2 * m, Height - meio);
            if (_ar != null && _lav != null)
                using (Pen p = new Pen(Ui.Border)) g.DrawLine(p, m, meio, Width - m, meio);
            if (_ar != null) PintarAr(g, _lav != null ? linhaAr : new Rectangle(m, 0, Width - 2 * m, Height));
            if (_lav != null) PintarLavadora(g, _ar != null ? linhaLav : new Rectangle(m, 0, Width - 2 * m, Height));
        }

        private void Botao(Graphics g, Rectangle r, string glifo, bool aceso, bool ativo, int zona)
        {
            Color fundo = !ativo ? Ui.SurfaceAlt : aceso ? Ui.Accent : (_hover == zona ? Ui.Hover : Ui.SurfaceAlt);
            Color frente = !ativo ? Ui.Faint : aceso ? Color.White : Ui.Text;
            using (SolidBrush b = new SolidBrush(fundo)) g.FillEllipse(b, r);
            using (Pen p = new Pen(aceso ? fundo : Ui.Border)) g.DrawEllipse(p, r);
            using (GraphicsPath gp = BotaoRedondo.Glifo(glifo, Ui.FontGlyph10, r))
            using (SolidBrush b = new SolidBrush(frente)) g.FillPath(b, gp);
        }

        private void PintarAr(Graphics g, Rectangle b)
        {
            bool on = _ar.Online, ligado = _ar.Int("t_power", 0) != 0;
            string u = _ar.Int("t_temp_type", 0) == 1 ? "°F" : "°C";
            int bt = Ui.S(28), lg = Ui.S(32);
            int yc = b.Y + (b.Height - lg) / 2;
            _rLiga = new Rectangle(b.Right - lg, yc, lg, lg);
            _rMais = new Rectangle(_rLiga.Left - Ui.S(10) - bt, yc + (lg - bt) / 2, bt, bt);
            _rMenos = new Rectangle(_rMais.Left - Ui.S(48) - bt, _rMais.Y, bt, bt);
            Botao(g, _rLiga, "\uE7E8", ligado, on, 2);
            Botao(g, _rMais, "\uE710", false, on && ligado, 1);
            Botao(g, _rMenos, "\uE738", false, on && ligado, 0);
            int alvo = _alvoPendente >= 0 ? _alvoPendente : _ar.Int("t_temp", 0);
            TextRenderer.DrawText(g, alvo > 0 ? alvo + "°" : "--", Ui.FontValue, new Rectangle(_rMenos.Right, b.Y, _rMais.Left - _rMenos.Right, b.Height),
                ligado ? Ui.Text : Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int tw = _rMenos.Left - Ui.S(8) - b.X;
            Size sn = TextRenderer.MeasureText(_ar.Nome, Ui.FontMed);
            _rNomeAr = new Rectangle(b.X, b.Y + b.Height / 2 - Ui.S(22), Math.Min(tw, sn.Width), Ui.S(20));
            TextRenderer.DrawText(g, _ar.Nome, Ui.FontMed, new Rectangle(b.X, b.Y + b.Height / 2 - Ui.S(22), tw, Ui.S(20)), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            string linha = !on ? T.CasaOffline : (_ar.Tem("f_temp_in") ? _ar.Num("f_temp_in", 0).ToString("0.#") + u + "  ·  " : "") +
                (ligado ? TabelaDoAr.Modo(_ar.Int("t_work_mode", -1), _ar.Tipo) : T.CasaDesligado);
            TextRenderer.DrawText(g, linha, Ui.FontSmall, new Rectangle(b.X, b.Y + b.Height / 2, tw, Ui.S(18)), Ui.Muted,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        private void PintarLavadora(Graphics g, Rectangle b)
        {
            CicloAtual c = CicloAtual.De(_lav);
            Size sn = TextRenderer.MeasureText(_lav.Nome, Ui.FontMed);
            int tw = b.Width;
            _rNomeLav = new Rectangle(b.X, b.Y + Ui.S(10), Math.Min(tw, sn.Width), Ui.S(20));
            string tempo = c.Online && c.Rodando && c.Restante > 0 ? CiclosDaLavadora.Duracao(TimeSpan.FromMinutes(c.Restante)) : "";
            int wt = tempo.Length > 0 ? TextRenderer.MeasureText(tempo, Ui.FontValue).Width : 0;
            TextRenderer.DrawText(g, _lav.Nome, Ui.FontMed, new Rectangle(b.X, b.Y + Ui.S(10), tw - wt - Ui.S(8), Ui.S(20)), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            if (tempo.Length > 0)
                TextRenderer.DrawText(g, tempo, Ui.FontValue, new Rectangle(b.Right - wt, b.Y + Ui.S(8), wt, Ui.S(26)), Ui.Text, TextFormatFlags.Right);
            string linha = c.Titulo();
            if (tempo.Length > 0)
            {
                DateTime leitura = ServicoCasa.UltimaLeitura == DateTime.MinValue ? DateTime.Now : ServicoCasa.UltimaLeitura;
                linha += "  ·  " + T.LavTerminaAs(leitura.AddMinutes(c.Restante).ToString("HH:mm"));
            }
            TextRenderer.DrawText(g, linha, Ui.FontSmall, new Rectangle(b.X, b.Y + Ui.S(32), tw, Ui.S(18)), c.Erro ? Ui.Danger : Ui.Muted,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            if (c.Rodando || c.Concluido)
            {
                Rectangle barra = new Rectangle(b.X, b.Bottom - Ui.S(18), b.Width, Ui.S(6));
                using (GraphicsPath p = Ui.RoundRect(barra, Ui.S(3)))
                using (SolidBrush br = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(br, p);
                int w = (int)Math.Round(barra.Width * (c.Online ? c.Progresso : 0f));
                if (w >= Ui.S(6))
                    using (GraphicsPath p = Ui.RoundRect(new Rectangle(barra.X, barra.Y, w, barra.Height), Ui.S(3)))
                    using (SolidBrush br = new SolidBrush(c.Erro ? Ui.Danger : c.Concluido ? Color.FromArgb(0x2E, 0xA0, 0x43) : Ui.Accent)) g.FillPath(br, p);
            }
        }
    }

    public static partial class T
    {
        public static string CasaWidgetTitulo { get { return P("Widget da casa", "Home widget"); } }
        public static string CasaWidgetFrente { get { return P("Sempre na frente", "Always on top"); } }
        public static string CasaWidgetTravar { get { return P("Travar posição", "Lock position"); } }
        public static string CasaWidgetFechar { get { return P("Fechar o widget", "Close widget"); } }
        public static string CasaWidgetOpcao { get { return P("Widget da casa na área de trabalho", "Home widget on the desktop"); } }
        public static string CasaWidgetNota { get { return P("Ar e lavadora num quadrinho que fica na área de trabalho. Arraste para mover; botão direito para opções.", "AC and washer in a small tile on the desktop. Drag to move; right-click for options."); } }
    }
}
