using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
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
    /// Widget da casa na area de trabalho, da mesma familia do widget de
    /// bateria: a mesma placa de vidro (PintorDoWidget.Vidro, com a cor e o
    /// desfoque escolhidos para o de bateria em widget.ini), as mesmas fontes,
    /// bolhas, texto com halo e janela em camadas.
    ///
    /// Uma linha para o ar (ambiente, modo, alvo com - e +, liga/desliga) e,
    /// so com ciclo ativo, uma linha enxuta da lavadora (fase, tempo restante
    /// e barra fina). Muda de tamanho pelo canto mais perto da borda da tela,
    /// como o de bateria: nada pula de lugar.
    /// </summary>
    internal sealed class WidgetDaCasa : Form
    {
        // medidas em pixels de 96 dpi, multiplicadas pela escala
        internal const float Largura = 264f, LarguraVazia = 220f;
        private const float Margem = 12f, LinhaAr = 40f, LinhaLav = 36f, Vao = 10f, Bolha = 32f;
        private const float BotaoPeq = 26f, BotaoLiga = 30f, Alvo = 40f;

        private readonly ConfigDoWidgetCasa _cfg;
        private ConfigDoWidget _vidro;
        private readonly EventHandler _mudou;
        private Aparelho _ar, _lav;
        private RectangleF _rMenos, _rMais, _rLiga;
        private int _hover = -1;
        private int _alvoPendente = -1;
        private readonly Timer _espera, _relogio;
        private readonly ToolTip _dica = new ToolTip();
        private ContextMenu _menu;
        private Bitmap _quadro;
        private float _escala = 1f;
        private bool _sobre, _pressionado, _arrastando;
        private Point _inicioDoArrasto, _origemDoArrasto;

        internal Aparelho Ar { get { return _ar; } }
        internal Aparelho Lavadora { get { return _lav; } }
        internal Dictionary<string, string> UltimoComando;

        /// <summary>Escala da tela; os testes trocam para conferir 125% e 150% sem abrir janela.</summary>
        internal float Escala { get { return _escala; } set { _escala = Math.Max(1f, value); } }

        public WidgetDaCasa(ConfigDoWidgetCasa cfg)
        {
            _cfg = cfg;
            _vidro = ConfigDoWidget.Carregar();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ControlBox = false; MinimizeBox = false; MaximizeBox = false;
            Text = T.CasaWidgetTitulo;

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
            // o relogio refaz o "termina as" e acompanha a cor do vidro do widget de bateria
            _relogio = new Timer();
            _relogio.Interval = 30000;
            _relogio.Tick += delegate { _vidro = ConfigDoWidget.Carregar(); Renderizar(); };
            _relogio.Start();

            _mudou = delegate { NaInterface(Puxar); };
            ServicoCasa.Mudou += _mudou;
            Puxar();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= 0x80000 | 0x80 | 0x8000000;   // WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                if (_cfg != null && _cfg.NaFrente) p.ExStyle |= 0x8;   // WS_EX_TOPMOST
                return p;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE: MA_NOACTIVATE
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (Graphics g = Graphics.FromHwnd(Handle)) _escala = Math.Max(1f, g.DpiX / 96f);
            WidgetDeBateria.ArredondarCantos(Handle);
            WidgetDeBateria.AplicarDesfoque(Handle, _vidro);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Posicionar();
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
                _dica.Dispose();
                if (_menu != null) { _menu.Dispose(); _menu = null; }
                if (_quadro != null) { _quadro.Dispose(); _quadro = null; }
            }
            base.Dispose(disposing);
        }

        // ---------------- dados ----------------

        internal void Puxar()
        {
            _ar = null; _lav = null;
            if (ServicoCasa.Logado)
                foreach (Aparelho a in ServicoCasa.Aparelhos)
                {
                    if (_ar == null && a.ArCondicionado) _ar = a;
                    if (_lav == null && a.Lavadora) _lav = a;
                }
            Renderizar();
        }

        /// <summary>
        /// A lavadora so entra com ciclo ativo: em funcionamento, pausada,
        /// aguardando o inicio programado ou com erro. Parada, concluida,
        /// desligada ou desconectada, a linha some.
        /// </summary>
        internal static bool CicloAtivo(Aparelho lav)
        {
            if (lav == null) return false;
            CicloAtual c = CicloAtual.De(lav);
            return c.Online && (c.Rodando || c.Erro);
        }

        internal bool MostraLavadora { get { return CicloAtivo(_lav); } }

        /// <summary>Tamanho do widget: ar numa linha e a lavadora embaixo so com ciclo ativo.</summary>
        internal static Size Medir(bool ar, bool lavadora, float escala)
        {
            float w, h;
            if (!ar && !lavadora) { w = LarguraVazia; h = 2 * Margem + LinhaAr; }
            else
            {
                w = Largura;
                h = 2 * Margem + (ar ? LinhaAr : 0f) + (ar && lavadora ? Vao : 0f) + (lavadora ? LinhaLav : 0f);
            }
            return new Size((int)Math.Ceiling(w * escala), (int)Math.Ceiling(h * escala));
        }

        internal Size Medida() { return Medir(_ar != null, MostraLavadora, _escala); }

        private void Mudar(int passo)
        {
            if (_ar == null || !_ar.Online || _ar.Int("t_power", 0) == 0) return;
            int min, max;
            CenaCasa.FaixaC(_ar, out min, out max);
            if (_ar.Int("t_temp_type", 0) == 1) { min = (int)Math.Round(min * 9.0 / 5 + 32); max = (int)Math.Round(max * 9.0 / 5 + 32); }
            int atual = _alvoPendente >= 0 ? _alvoPendente : _ar.Int("t_temp", 24);
            _alvoPendente = Math.Max(min, Math.Min(max, atual + passo));
            _espera.Stop(); _espera.Start();
            Renderizar();
        }

        private void Mandar(string chave, int valor)
        {
            if (_ar == null) return;
            Dictionary<string, string> p = new Dictionary<string, string>();
            p[chave] = valor.ToString(CultureInfo.InvariantCulture);
            _ar.Estado[chave] = p[chave];
            if (ServicoCasa.RetornoPuid == _ar.Puid) ServicoCasa.CancelarRetorno();
            UltimoComando = p;
            Renderizar();
            ServicoCasa.Enviar(_ar.Puid, p, null);
        }

        // ---------------- janela ----------------

        private void Posicionar()
        {
            Size tam = Medida();
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int m = (int)(24 * _escala);
            Point p = WidgetDeBateria.DoCanto(new Point(area.Right - m, area.Top + m), tam, area);
            if (_cfg.TemPosicao)
            {
                Rectangle a2 = Screen.FromPoint(new Point(_cfg.X - 1, _cfg.Y - 1)).WorkingArea;
                Point q = WidgetDeBateria.DoCanto(new Point(_cfg.X, _cfg.Y), tam, a2);
                if (WidgetDeBateria.Visivel(new Rectangle(q, tam))) p = q;
            }
            Bounds = new Rectangle(p, tam);
            Renderizar();
        }

        internal void Renderizar()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                Size tam = Medida();
                if (tam != Size)
                {
                    // cresce e encolhe pelo canto mais perto da borda: a lavadora
                    // entra e sai sem o widget pular de lugar
                    Rectangle antes = Bounds;
                    Rectangle area = Screen.FromRectangle(antes).WorkingArea;
                    Point onde = WidgetDeBateria.AncorarCrescimento(antes, tam, area);
                    onde = new Point(Math.Max(area.Left, Math.Min(onde.X, area.Right - tam.Width)),
                                     Math.Max(area.Top, Math.Min(onde.Y, area.Bottom - tam.Height)));
                    Bounds = new Rectangle(onde, tam);
                    WidgetDeBateria.AplicarRegiao(Handle, tam, _escala);
                }
                if (_quadro == null || _quadro.Size != tam)
                {
                    if (_quadro != null) _quadro.Dispose();
                    _quadro = new Bitmap(tam.Width, tam.Height, PixelFormat.Format32bppArgb);
                }
                using (Graphics g = Graphics.FromImage(_quadro))
                {
                    g.Clear(Color.Transparent);
                    Pintar(g, tam);
                }
                WidgetDeBateria.AplicarBitmap(this, _quadro);
            }
            catch (Exception ex) { Log.Error("desenho do widget da casa", ex); }
        }

        private void GuardarPosicao()
        {
            Point c = WidgetDeBateria.CantoAncora(Bounds, Screen.FromRectangle(Bounds).WorkingArea);
            _cfg.X = c.X; _cfg.Y = c.Y;
            _cfg.Salvar();
        }

        // ---------------- mouse ----------------

        /// <summary>0 menos, 1 mais, 2 liga/desliga, 3 abrir a pagina Casa (o resto da placa).</summary>
        private int Zona(Point p)
        {
            if (_rMenos.Contains(p)) return 0;
            if (_rMais.Contains(p)) return 1;
            if (_rLiga.Contains(p)) return 2;
            return 3;
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
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _pressionado = true;
            _arrastando = false;
            _inicioDoArrasto = Cursor.Position;
            _origemDoArrasto = Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool redesenhar = false;
            if (!_sobre) { _sobre = true; redesenhar = true; }
            if (_pressionado && !_cfg.Travado && Zona(PontoDoInicio()) == 3)
            {
                Point agora = Cursor.Position;
                int dx = agora.X - _inicioDoArrasto.X, dy = agora.Y - _inicioDoArrasto.Y;
                if (!_arrastando && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4)) { _arrastando = true; _dica.Hide(this); }
                if (_arrastando)
                {
                    Rectangle area = Screen.FromPoint(agora).WorkingArea;
                    Location = WidgetDeBateria.Encaixar(new Point(_origemDoArrasto.X + dx, _origemDoArrasto.Y + dy), Size, area, (int)(12 * _escala));
                    return;
                }
            }
            int z = Zona(e.Location);
            int h = z < 3 ? z : -1;
            if (h != _hover)
            {
                _hover = h;
                Cursor = Cursors.Hand;
                _dica.SetToolTip(this, h == 0 ? T.CasaMenosGrau : h == 1 ? T.CasaMaisGrau : h == 2 ? T.CasaLigaDesliga : T.CasaResumoConvite);
                redesenhar = true;
            }
            if (redesenhar) Renderizar();
        }

        private Point PontoDoInicio() { return PointToClient(_inicioDoArrasto); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { MostrarMenu(e.Location); return; }
            if (e.Button != MouseButtons.Left || !_pressionado) return;
            _pressionado = false;
            Capture = false;
            if (_arrastando) { _arrastando = false; GuardarPosicao(); return; }
            Clicar(Zona(e.Location));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_arrastando) return;
            _sobre = false; _hover = -1;
            Renderizar();
        }

        /// <summary>Menu nativo, como o do widget de bateria (foreground do TrackPopupMenu).</summary>
        private void MostrarMenu(Point onde)
        {
            ContextMenu m = new ContextMenu();
            m.MenuItems.Add(T.CasaResumoConvite, delegate { Clicar(3); });
            m.MenuItems.Add("-");
            MenuItem frente = new MenuItem(T.CasaWidgetFrente, delegate
            {
                _cfg.NaFrente = !_cfg.NaFrente; _cfg.Salvar();
                SetWindowPos(Handle, _cfg.NaFrente ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
            });
            frente.Checked = _cfg.NaFrente;
            m.MenuItems.Add(frente);
            MenuItem trava = new MenuItem(T.CasaWidgetTravar, delegate { _cfg.Travado = !_cfg.Travado; _cfg.Salvar(); });
            trava.Checked = _cfg.Travado;
            m.MenuItems.Add(trava);
            m.MenuItems.Add("-");
            m.MenuItems.Add(T.CasaWidgetFechar, delegate { GerenteDoWidgetCasa.Definir(false); });
            SetForegroundWindow(Handle);
            _dica.Hide(this);
            if (_menu != null) _menu.Dispose();
            _menu = m;
            m.Show(this, onde);
        }

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // ---------------- pintura ----------------

        /// <summary>Pinta no tamanho medido (os testes pintam num bitmap, sem janela).</summary>
        internal void Pintar(Graphics g) { Pintar(g, Medida()); }

        internal void Pintar(Graphics g, Size tam)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;   // ClearType sobre transparente deixa franja
            float s = _escala;
            bool escuro = WidgetDeBateria.EscuroDe(_vidro);
            PintorDoWidget.Vidro(g, new RectangleF(0, 0, tam.Width, tam.Height), s, escuro, _vidro.Fosco && !_vidro.Nitido, _vidro.Nitido, _sobre);
            _rMenos = _rMais = _rLiga = RectangleF.Empty;
            using (PintorDoWidget.Fontes f = new PintorDoWidget.Fontes(s))
            using (Font glifo = new Font("Segoe MDL2 Assets", 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                float x = Margem * s, w = tam.Width - 2 * Margem * s, y = Margem * s;
                bool lav = MostraLavadora;
                if (_ar == null && !lav)
                {
                    RectangleF b = new RectangleF(x, y + (LinhaAr - Bolha) / 2f * s, Bolha * s, Bolha * s);
                    PintorDoWidget.Bolha(g, b, escuro);
                    Glifo(g, "\uE80F", glifo, b, PintorDoWidget.Primario(escuro));
                    string msg = !ServicoCasa.Logado ? T.VisaoSemConta : ServicoCasa.Buscando ? T.CasaCarregando : T.CasaSemAparelhos;
                    float tx = b.Right + 10f * s;
                    PintorDoWidget.Texto(g, T.NavCasa, f.Nome, PintorDoWidget.Primario(escuro), escuro,
                        new RectangleF(tx, y, x + w - tx, 21f * s), StringAlignment.Near, StringAlignment.Center);
                    PintorDoWidget.Texto(g, msg, f.Nota, PintorDoWidget.Secundario(escuro), escuro,
                        new RectangleF(tx, y + 20f * s, x + w - tx, 19f * s), StringAlignment.Near, StringAlignment.Center);
                    return;
                }
                if (_ar != null) { PintarAr(g, new RectangleF(x, y, w, LinhaAr * s), f, glifo, escuro); y += (LinhaAr + Vao) * s; }
                if (lav) PintarLavadora(g, new RectangleF(x, y, w, LinhaLav * s), f, glifo, escuro);
            }
        }

        private static void Glifo(Graphics g, string texto, Font fonte, RectangleF area, Color cor)
        {
            using (GraphicsPath gp = BotaoRedondo.Glifo(texto, fonte, area))
            using (SolidBrush b = new SolidBrush(cor)) g.FillPath(b, gp);
        }

        /// <summary>Botao redondo no vidro: a bolha do widget de bateria; aceso, na cor de destaque.</summary>
        private void Botao(Graphics g, RectangleF r, string glifo, Font fonte, bool aceso, bool ativo, int zona, bool escuro)
        {
            if (aceso)
                using (SolidBrush b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, r);
            else
            {
                PintorDoWidget.Bolha(g, r, escuro);
                if (ativo && _hover == zona)
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(escuro ? 40 : 60, 255, 255, 255))) g.FillEllipse(b, r);
            }
            Color frente = aceso ? Color.White : ativo ? PintorDoWidget.Primario(true) : Color.FromArgb(90, 255, 255, 255);
            Glifo(g, glifo, fonte, r, frente);
        }

        private void PintarAr(Graphics g, RectangleF b, PintorDoWidget.Fontes f, Font glifo, bool escuro)
        {
            float s = _escala;
            bool on = _ar.Online, ligado = on && _ar.Int("t_power", 0) != 0;
            float cy = b.Y + b.Height / 2f;
            RectangleF bolha = new RectangleF(b.X, cy - Bolha * s / 2f, Bolha * s, Bolha * s);
            PintorDoWidget.Bolha(g, bolha, escuro);
            Glifo(g, "\uE9CA", glifo, bolha, Color.White);

            _rLiga = new RectangleF(b.Right - BotaoLiga * s, cy - BotaoLiga * s / 2f, BotaoLiga * s, BotaoLiga * s);
            _rMais = new RectangleF(_rLiga.X - 8f * s - BotaoPeq * s, cy - BotaoPeq * s / 2f, BotaoPeq * s, BotaoPeq * s);
            RectangleF alvo = new RectangleF(_rMais.X - Alvo * s, b.Y, Alvo * s, b.Height);
            _rMenos = new RectangleF(alvo.X - BotaoPeq * s, _rMais.Y, BotaoPeq * s, BotaoPeq * s);
            using (Font peq = new Font(glifo.FontFamily, 10f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                Botao(g, _rLiga, "\uE7E8", glifo, ligado, on, 2, escuro);
                Botao(g, _rMais, "\uE710", peq, false, ligado, 1, escuro);
                Botao(g, _rMenos, "\uE738", peq, false, ligado, 0, escuro);
            }
            int t = _alvoPendente >= 0 ? _alvoPendente : _ar.Int("t_temp", 0);
            PintorDoWidget.Texto(g, t > 0 ? t + "°" : "--", f.Pct, ligado ? PintorDoWidget.Primario(escuro) : PintorDoWidget.Secundario(escuro), escuro,
                alvo, StringAlignment.Center, StringAlignment.Center);

            float tx = bolha.Right + 10f * s, tw = _rMenos.X - 6f * s - tx;
            string u = _ar.Int("t_temp_type", 0) == 1 ? "°F" : "°C";
            string amb = _ar.Tem("f_temp_in") ? _ar.Num("f_temp_in", 0).ToString("0.#") + u : _ar.Nome;
            string modo = !on ? T.CasaOffline : ligado ? TabelaDoAr.Modo(_ar.Int("t_work_mode", -1), _ar.Tipo) : T.CasaDesligado;
            PintorDoWidget.Texto(g, amb, f.Nome, PintorDoWidget.Primario(escuro), escuro,
                new RectangleF(tx, cy - 19f * s, tw, 19f * s), StringAlignment.Near, StringAlignment.Center);
            PintorDoWidget.Texto(g, modo, f.Nota, PintorDoWidget.Secundario(escuro), escuro,
                new RectangleF(tx, cy, tw, 18f * s), StringAlignment.Near, StringAlignment.Center);
        }

        private void PintarLavadora(Graphics g, RectangleF b, PintorDoWidget.Fontes f, Font glifo, bool escuro)
        {
            float s = _escala;
            CicloAtual c = CicloAtual.De(_lav);
            float cy = b.Y + b.Height / 2f;
            RectangleF bolha = new RectangleF(b.X, cy - Bolha * s / 2f, Bolha * s, Bolha * s);
            PintorDoWidget.Bolha(g, bolha, escuro);
            IconeDaLavadora(g, bolha, s);

            float tx = bolha.Right + 10f * s, tw = b.Right - tx;
            string tempo = c.Restante > 0 && !c.Erro ? CiclosDaLavadora.Duracao(TimeSpan.FromMinutes(c.Restante)) : "";
            float wt = 0f;
            if (tempo.Length > 0)
                using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
                    wt = g.MeasureString(tempo, f.Pct, PointF.Empty, sf).Width + 2f * s;
            RectangleF linha = new RectangleF(tx, b.Y + 1f * s, tw, 21f * s);
            if (wt > 0)
                PintorDoWidget.Texto(g, tempo, f.Pct, PintorDoWidget.Primario(escuro), escuro,
                    new RectangleF(b.Right - wt, linha.Y, wt, linha.Height), StringAlignment.Far, StringAlignment.Center);
            string fase = c.Titulo();
            if (tempo.Length > 0 && !c.Pausada && !c.Programado)
            {
                DateTime leitura = ServicoCasa.UltimaLeitura == DateTime.MinValue ? DateTime.Now : ServicoCasa.UltimaLeitura;
                fase += " · " + leitura.AddMinutes(c.Restante).ToString("HH:mm");
            }
            PintorDoWidget.Texto(g, fase, f.Nota, c.Erro ? Color.FromArgb(255, 255, 120, 110) : PintorDoWidget.Primario(escuro), escuro,
                new RectangleF(tx, linha.Y, Math.Max(0f, tw - wt - 6f * s), linha.Height), StringAlignment.Near, StringAlignment.Center);

            // barra fina, com a mesma ponta redonda da capsula do widget de bateria
            float h = 4f * s, by = b.Bottom - 7f * s;
            Color cor = c.Erro ? Ui.Danger : c.Pausada ? Ui.Warn : Ui.Accent;
            using (Pen trilho = PintorDoWidget.Redonda(PintorDoWidget.Trilho(escuro), h)) g.DrawLine(trilho, tx + h / 2f, by, b.Right - h / 2f, by);
            float wp = (tw - h) * Math.Max(0f, Math.Min(1f, c.Progresso));
            if (wp > 0.5f)
                using (Pen pen = PintorDoWidget.Redonda(cor, h)) g.DrawLine(pen, tx + h / 2f, by, tx + h / 2f + wp, by);
        }

        /// <summary>A maquininha (caixa com a porta redonda), em branco como os icones do widget de bateria.</summary>
        private static void IconeDaLavadora(Graphics g, RectangleF bolha, float s)
        {
            RectangleF r = RectangleF.Inflate(bolha, -9.5f * s, -8.5f * s);
            using (Pen pen = new Pen(Color.White, 1.6f * s))
            {
                using (GraphicsPath gp = PintorDoWidget.Arredondado(r, 2.5f * s)) g.DrawPath(pen, gp);
                float d = r.Width * 0.58f;
                g.DrawEllipse(pen, r.X + (r.Width - d) / 2f, r.Bottom - d - 2f * s, d, d);
                g.DrawLine(pen, r.X + 2.5f * s, r.Y + 3f * s, r.X + 5f * s, r.Y + 3f * s);
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
