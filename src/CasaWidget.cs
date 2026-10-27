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
        /// <summary>Como o de bateria: fica na area de trabalho; na frente so se pedir.</summary>
        public bool Ativo, NaFrente, Travado;
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
                        // "nafrente" era gravado ligado por padrao e prendia o widget na frente: ignorado
                        case "frente": c.NaFrente = v == "1"; break;
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
            sb.AppendLine("frente=" + (NaFrente ? "1" : "0"));
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
    /// bateria: a mesma placa de vidro, as mesmas medidas (PintorDoWidget),
    /// as mesmas fontes, bolhas e aneis, no formato escolhido para o de
    /// bateria (aneis ou lista) e com a cor e o desfoque dele (widget.ini).
    /// O ar e um item; a lavadora, so com ciclo ativo, e mais um.
    ///
    /// Fica na area de trabalho como o de bateria (WidgetDeBateria.Camada):
    /// abaixo das janelas, sem roubar o foco; na frente so com a opcao.
    /// </summary>
    internal sealed class WidgetDaCasa : Form
    {
        /// <summary>Bolhas de - e + (e do liga/desliga na lista): do tamanho do selo de carga do widget de bateria.</summary>
        private const float Mini = 22f, VaoMini = 6f;

        private readonly ConfigDoWidgetCasa _cfg;
        private ConfigDoWidget _vidro;
        private readonly EventHandler _mudou;
        private readonly WidgetDeBateria.Camada _camada;
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

        /// <summary>O formato do widget de bateria (aneis ou lista); os testes trocam.</summary>
        internal LayoutDoWidget Formato { get { return _vidro.Layout; } set { _vidro.Layout = value; } }

        public WidgetDaCasa(ConfigDoWidgetCasa cfg)
        {
            _cfg = cfg;
            _vidro = ConfigDoWidget.Carregar();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ControlBox = false; MinimizeBox = false; MaximizeBox = false;
            Text = T.CasaWidgetTitulo;
            _camada = new WidgetDeBateria.Camada(this, delegate { return _cfg.NaFrente; });

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
            // o relogio refaz o "termina as" e acompanha o vidro e o formato do widget de bateria
            _relogio = new Timer();
            _relogio.Interval = 30000;
            _relogio.Tick += delegate
            {
                ConfigDoWidget v = ConfigDoWidget.Carregar();
                bool desfoque = v.Nitido != _vidro.Nitido || v.Fosco != _vidro.Fosco;
                _vidro = v;
                if (desfoque && IsHandleCreated) WidgetDeBateria.AplicarDesfoque(Handle, _vidro);
                Renderizar();
            };
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
                if (_cfg != null && _cfg.NaFrente) p.ExStyle |= 0x8;   // WS_EX_TOPMOST, so com a opcao
                return p;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            if (_camada != null && _camada.Mensagem(ref m)) return;
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
            _camada.Ligar();
        }

        /// <summary>A janela esta de fato acima das outras (WS_EX_TOPMOST)? Para os testes.</summary>
        internal bool NaFrenteAgora
        {
            get { return IsHandleCreated && (GetWindowLong(Handle, -20) & 0x8) != 0; }
        }

        /// <summary>Liga ou desliga o "sempre na frente" e grava em widget-casa.ini.</summary>
        internal void DefinirNaFrente(bool frente)
        {
            _cfg.NaFrente = frente;
            _cfg.Salvar();
            _camada.AplicarFrente();
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
                _camada.Dispose();
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
        /// desligada ou desconectada, ela sai.
        /// </summary>
        internal static bool CicloAtivo(Aparelho lav)
        {
            if (lav == null) return false;
            CicloAtual c = CicloAtual.De(lav);
            return c.Online && (c.Rodando || c.Erro);
        }

        internal bool MostraLavadora { get { return CicloAtivo(_lav); } }

        /// <summary>Coluna do ar nos aneis: o anel com uma bolha de - e de + de cada lado.</summary>
        private static float ColunaDoAr { get { return PintorDoWidget.Anel + 2f * (Mini + 4f); } }

        /// <summary>
        /// Tamanho do widget, nas medidas do de bateria: cada aparelho e um item
        /// (uma linha da lista ou uma coluna de anel). Sem nada, o mesmo vazio dele.
        /// </summary>
        internal static Size Medir(LayoutDoWidget formato, bool ar, bool lavadora, float escala)
        {
            float m = PintorDoWidget.Margem, w, h;
            int n = (ar ? 1 : 0) + (lavadora ? 1 : 0);
            if (n == 0)
                return PintorDoWidget.Medir(formato, 0, escala, false);
            if (formato == LayoutDoWidget.Lista)
            {
                w = PintorDoWidget.LarguraDaLista;
                h = 2 * m + n * PintorDoWidget.Linha + (n - 1) * PintorDoWidget.VaoDeLinha;
            }
            else
            {
                w = 2 * m + (ar ? ColunaDoAr : 0f) + (lavadora ? PintorDoWidget.Coluna : 0f) + (n - 1) * PintorDoWidget.VaoDeColuna;
                h = 2 * m + PintorDoWidget.Anel + PintorDoWidget.VaoDoValor + PintorDoWidget.LinhaDoValor;
            }
            return new Size((int)Math.Ceiling(w * escala), (int)Math.Ceiling(h * escala));
        }

        internal Size Medida() { return Medir(_vidro.Layout, _ar != null, MostraLavadora, _escala); }

        private void Mudar(int passo)
        {
            if (_ar == null || !_ar.Online || _ar.Int("t_power", 0) == 0) return;
            int min, max;
            FaixaDoAlvo(out min, out max);
            int atual = _alvoPendente >= 0 ? _alvoPendente : _ar.Int("t_temp", 24);
            _alvoPendente = Math.Max(min, Math.Min(max, atual + passo));
            _espera.Stop(); _espera.Start();
            Renderizar();
        }

        private void FaixaDoAlvo(out int min, out int max)
        {
            CenaCasa.FaixaC(_ar, out min, out max);
            if (_ar.Int("t_temp_type", 0) == 1) { min = (int)Math.Round(min * 9.0 / 5 + 32); max = (int)Math.Round(max * 9.0 / 5 + 32); }
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
            int m = (int)(48 * _escala);
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
            if (_pressionado && !_cfg.Travado && Zona(PointToClient(_inicioDoArrasto)) == 3)
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
                _dica.SetToolTip(this, h == 0 ? T.CasaMenosGrau : h == 1 ? T.CasaMaisGrau : h == 2 ? T.CasaLigaDesliga : Dica());
                redesenhar = true;
            }
            if (redesenhar) Renderizar();
        }

        /// <summary>O que nao cabe na placa: nome, ambiente e modo do ar, fase e termino da lavadora.</summary>
        private string Dica()
        {
            StringBuilder sb = new StringBuilder();
            if (_ar != null) sb.Append(_ar.Nome).Append("  ").Append(LinhaDoAr()).Append('\n');
            if (MostraLavadora) sb.Append(_lav.Nome).Append("  ").Append(LinhaDaLavadora(CicloAtual.De(_lav))).Append('\n');
            return sb.Append(T.CasaResumoConvite).ToString();
        }

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
            MenuItem frente = new MenuItem(T.CasaWidgetFrente, delegate { DefinirNaFrente(!_cfg.NaFrente); });
            frente.Checked = _cfg.NaFrente;
            m.MenuItems.Add(frente);
            MenuItem trava = new MenuItem(T.CasaWidgetTravar, delegate { _cfg.Travado = !_cfg.Travado; _cfg.Salvar(); });
            trava.Checked = _cfg.Travado;
            m.MenuItems.Add(trava);
            m.MenuItems.Add("-");
            m.MenuItems.Add(T.CasaWidgetFechar, delegate { GerenteDoWidgetCasa.Definir(false); });
            // sem o primeiro plano o menu nao fecha ao clicar fora; a camada devolve o widget ao fundo
            SetForegroundWindow(Handle);
            _dica.Hide(this);
            if (_menu != null) _menu.Dispose();
            _menu = m;
            m.Show(this, onde);
        }

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int indice);

        // ---------------- pintura ----------------

        /// <summary>Pinta no tamanho medido (os testes pintam num bitmap, sem janela).</summary>
        internal void Pintar(Graphics g) { Pintar(g, Medida()); }

        internal void Pintar(Graphics g, Size tam)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;   // ClearType sobre transparente deixa franja
            float s = _escala, m = PintorDoWidget.Margem * s;
            bool escuro = WidgetDeBateria.EscuroDe(_vidro);
            PintorDoWidget.Vidro(g, new RectangleF(0, 0, tam.Width, tam.Height), s, escuro, _vidro.Fosco && !_vidro.Nitido, _vidro.Nitido, _sobre);
            _rMenos = _rMais = _rLiga = RectangleF.Empty;
            bool lav = MostraLavadora;
            using (PintorDoWidget.Fontes f = new PintorDoWidget.Fontes(s))
            using (Font glifo = new Font("Segoe MDL2 Assets", 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font peq = new Font("Segoe MDL2 Assets", 9f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                if (_ar == null && !lav)
                {
                    // como o "nenhum aparelho" do widget de bateria: uma nota no meio da placa
                    string msg = !ServicoCasa.Logado ? T.VisaoSemConta : ServicoCasa.Buscando ? T.CasaCarregando : T.CasaSemAparelhos;
                    PintorDoWidget.Texto(g, T.NavCasa + " · " + msg, f.Nota, PintorDoWidget.Primario(escuro), escuro,
                        new RectangleF(m / 2f, 0, tam.Width - m, tam.Height), StringAlignment.Center, StringAlignment.Center);
                    return;
                }
                if (_vidro.Layout == LayoutDoWidget.Lista)
                {
                    float w = tam.Width - 2 * m, y = m, linha = PintorDoWidget.Linha * s;
                    if (_ar != null) { ArNaLista(g, new RectangleF(m, y, w, linha), f, glifo, peq, escuro); y += linha + PintorDoWidget.VaoDeLinha * s; }
                    if (lav) LavadoraNaLista(g, new RectangleF(m, y, w, linha), f, escuro);
                }
                else
                {
                    float x = m, h = tam.Height - 2 * m;
                    if (_ar != null) { ArNoAnel(g, new RectangleF(x, m, ColunaDoAr * s, h), f, glifo, peq, escuro); x += (ColunaDoAr + PintorDoWidget.VaoDeColuna) * s; }
                    if (lav) LavadoraNoAnel(g, new RectangleF(x, m, PintorDoWidget.Coluna * s, h), f, escuro);
                }
            }
        }

        private string LinhaDoAr()
        {
            bool on = _ar.Online, ligado = on && _ar.Int("t_power", 0) != 0;
            string u = _ar.Int("t_temp_type", 0) == 1 ? "°F" : "°C";
            string amb = _ar.Tem("f_temp_in") ? _ar.Num("f_temp_in", 0).ToString("0.#") + u + " · " : "";
            return amb + (!on ? T.CasaOffline : ligado ? TabelaDoAr.Modo(_ar.Int("t_work_mode", -1), _ar.Tipo) : T.CasaDesligado);
        }

        private static string LinhaDaLavadora(CicloAtual c)
        {
            string fase = c.Titulo();
            if (c.Restante > 0 && !c.Erro && !c.Pausada && !c.Programado)
            {
                DateTime leitura = ServicoCasa.UltimaLeitura == DateTime.MinValue ? DateTime.Now : ServicoCasa.UltimaLeitura;
                fase += " · " + leitura.AddMinutes(c.Restante).ToString("HH:mm");
            }
            return fase;
        }

        private static string Tempo(CicloAtual c)
        {
            return c.Restante > 0 && !c.Erro ? CiclosDaLavadora.Duracao(TimeSpan.FromMinutes(c.Restante)) : "--";
        }

        private static Color CorDoCiclo(CicloAtual c)
        {
            return c.Erro ? Ui.Danger : c.Pausada ? Ui.Warn : Ui.Accent;
        }

        private static void Glifo(Graphics g, string texto, Font fonte, RectangleF area, Color cor)
        {
            using (GraphicsPath gp = BotaoRedondo.Glifo(texto, fonte, area))
            using (SolidBrush b = new SolidBrush(cor)) g.FillPath(b, gp);
        }

        /// <summary>Bolha pequena de vidro; acesa (o ar ligado), na cor de destaque.</summary>
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
            Glifo(g, glifo, fonte, r, ativo || aceso ? Color.White : Color.FromArgb(110, 255, 255, 255));
        }

        /// <summary>O anel do widget de bateria: trilho apagado na cor, arco com brilho suave por baixo.</summary>
        private static void Anel(Graphics g, RectangleF anel, float st, float fracao, Color cor, bool aceso, bool escuro)
        {
            Color trilho = aceso ? Color.FromArgb(escuro ? 70 : 60, cor) : PintorDoWidget.Trilho(escuro);
            using (Pen pen = new Pen(trilho, st)) g.DrawEllipse(pen, anel);
            if (!aceso || fracao <= 0f) return;
            float arco = 360f * Math.Min(1f, fracao);
            using (Pen luz = PintorDoWidget.Redonda(Color.FromArgb(55, cor), st * 2f)) g.DrawArc(luz, anel, -90f, arco);
            using (Pen pen = PintorDoWidget.Redonda(cor, st)) g.DrawArc(pen, anel, -90f, arco);
        }

        // ---------------- formato aneis ----------------

        /// <summary>
        /// O ar como um item de anel: o arco e o alvo dentro da faixa do aparelho,
        /// o termometro na bolha, o alvo embaixo (onde o de bateria poe a
        /// porcentagem), - e + dos lados e o liga/desliga no lugar do selo de carga.
        /// </summary>
        private void ArNoAnel(Graphics g, RectangleF z, PintorDoWidget.Fontes f, Font glifo, Font peq, bool escuro)
        {
            float s = _escala, d = PintorDoWidget.Anel * s, st = PintorDoWidget.Traco * s;
            bool on = _ar.Online, ligado = on && _ar.Int("t_power", 0) != 0;
            float cx = z.X + z.Width / 2f;
            RectangleF anel = new RectangleF(cx - d / 2f + st / 2f, z.Y + st / 2f, d - st, d - st);
            int t = _alvoPendente >= 0 ? _alvoPendente : _ar.Int("t_temp", 0), min, max;
            FaixaDoAlvo(out min, out max);
            float fracao = max > min && t > 0 ? 0.15f + 0.85f * (t - min) / (float)(max - min) : 0f;
            Anel(g, anel, st, fracao, Ui.Accent, ligado, escuro);
            float db = d - 2f * st - 6f * s;
            RectangleF bolha = new RectangleF(cx - db / 2f, anel.Y + anel.Height / 2f - db / 2f, db, db);
            PintorDoWidget.Bolha(g, bolha, escuro);
            Glifo(g, "\uE9CA", glifo, bolha, ligado ? Color.White : Color.FromArgb(150, 255, 255, 255));

            float mini = Mini * s, cy = anel.Y + anel.Height / 2f;
            _rMenos = new RectangleF(z.X, cy - mini / 2f, mini, mini);
            _rMais = new RectangleF(z.Right - mini, cy - mini / 2f, mini, mini);
            float r = anel.Width / 2f, ds = 19f * s;   // o selo do widget de bateria
            _rLiga = new RectangleF(cx + r * 0.72f - ds / 2f, cy + r * 0.72f - ds / 2f, ds, ds);
            Botao(g, _rMenos, "\uE738", peq, false, ligado, 0, escuro);
            Botao(g, _rMais, "\uE710", peq, false, ligado, 1, escuro);
            Botao(g, _rLiga, "\uE7E8", peq, ligado, on, 2, escuro);

            RectangleF lValor = new RectangleF(z.X, z.Y + d + 7f * s, z.Width, PintorDoWidget.LinhaDoValor * s);
            PintorDoWidget.Texto(g, t > 0 ? t + "°" : "--", f.Pct, ligado ? PintorDoWidget.Primario(escuro) : PintorDoWidget.Secundario(escuro), escuro,
                lValor, StringAlignment.Center, StringAlignment.Center);
        }

        /// <summary>A lavadora como um item de anel: o arco e o progresso do ciclo, o tempo restante embaixo.</summary>
        private void LavadoraNoAnel(Graphics g, RectangleF z, PintorDoWidget.Fontes f, bool escuro)
        {
            float s = _escala, d = PintorDoWidget.Anel * s, st = PintorDoWidget.Traco * s;
            CicloAtual c = CicloAtual.De(_lav);
            float cx = z.X + z.Width / 2f;
            RectangleF anel = new RectangleF(cx - d / 2f + st / 2f, z.Y + st / 2f, d - st, d - st);
            Anel(g, anel, st, c.Erro ? 1f : c.Progresso, CorDoCiclo(c), true, escuro);
            float db = d - 2f * st - 6f * s;
            RectangleF bolha = new RectangleF(cx - db / 2f, anel.Y + anel.Height / 2f - db / 2f, db, db);
            PintorDoWidget.Bolha(g, bolha, escuro);
            IconeDaLavadora(g, bolha, s);
            RectangleF lValor = new RectangleF(z.X - 4f * s, z.Y + d + 7f * s, z.Width + 8f * s, PintorDoWidget.LinhaDoValor * s);
            PintorDoWidget.Texto(g, Tempo(c), f.Pct, PintorDoWidget.Primario(escuro), escuro, lValor, StringAlignment.Center, StringAlignment.Center);
        }

        // ---------------- formato lista ----------------

        /// <summary>
        /// O ar como uma linha da lista: nome e alvo na linha de cima (como nome
        /// e porcentagem), ambiente e modo embaixo, e - + e liga/desliga em bolhas pequenas.
        /// </summary>
        private void ArNaLista(Graphics g, RectangleF z, PintorDoWidget.Fontes f, Font glifo, Font peq, bool escuro)
        {
            float s = _escala;
            bool on = _ar.Online, ligado = on && _ar.Int("t_power", 0) != 0;
            float db = PintorDoWidget.BolhaDaLista * s;
            RectangleF bolha = new RectangleF(z.X, z.Y + (z.Height - db) / 2f, db, db);
            PintorDoWidget.Bolha(g, bolha, escuro);
            Glifo(g, "\uE9CA", glifo, bolha, ligado ? Color.White : Color.FromArgb(150, 255, 255, 255));

            float x = bolha.Right + 12f * s;
            int t = _alvoPendente >= 0 ? _alvoPendente : _ar.Int("t_temp", 0);
            string valor = t > 0 ? t + "°" : "--";
            float largValor;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
                largValor = g.MeasureString(valor, f.Pct, PointF.Empty, sf).Width + 2f * s;
            RectangleF lValor = new RectangleF(z.Right - largValor, z.Y, largValor, 21f * s);
            PintorDoWidget.Texto(g, valor, f.Pct, ligado ? PintorDoWidget.Primario(escuro) : PintorDoWidget.Secundario(escuro), escuro,
                lValor, StringAlignment.Far, StringAlignment.Center);
            PintorDoWidget.Texto(g, _ar.Nome, f.Nome, PintorDoWidget.Primario(escuro), escuro,
                new RectangleF(x, z.Y, Math.Max(0f, lValor.X - x - 6f * s), 21f * s), StringAlignment.Near, StringAlignment.Center);

            float mini = Mini * s, vao = VaoMini * s, yb = z.Bottom - mini;
            _rLiga = new RectangleF(z.Right - mini, yb, mini, mini);
            _rMais = new RectangleF(_rLiga.X - vao - mini, yb, mini, mini);
            _rMenos = new RectangleF(_rMais.X - vao - mini, yb, mini, mini);
            Botao(g, _rMenos, "\uE738", peq, false, ligado, 0, escuro);
            Botao(g, _rMais, "\uE710", peq, false, ligado, 1, escuro);
            Botao(g, _rLiga, "\uE7E8", peq, ligado, on, 2, escuro);
            PintorDoWidget.Texto(g, LinhaDoAr(), f.Nota, PintorDoWidget.Secundario(escuro), escuro,
                new RectangleF(x, yb, Math.Max(0f, _rMenos.X - x - 6f * s), mini), StringAlignment.Near, StringAlignment.Center);
        }

        /// <summary>A lavadora como uma linha da lista: fase e tempo, a capsula do progresso e o termino embaixo.</summary>
        private void LavadoraNaLista(Graphics g, RectangleF z, PintorDoWidget.Fontes f, bool escuro)
        {
            float s = _escala;
            CicloAtual c = CicloAtual.De(_lav);
            float db = PintorDoWidget.BolhaDaLista * s;
            RectangleF bolha = new RectangleF(z.X, z.Y + (z.Height - db) / 2f, db, db);
            PintorDoWidget.Bolha(g, bolha, escuro);
            IconeDaLavadora(g, bolha, s);

            float x = bolha.Right + 12f * s, w = z.Right - x;
            string valor = Tempo(c);
            float largValor;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
                largValor = g.MeasureString(valor, f.Pct, PointF.Empty, sf).Width + 2f * s;
            RectangleF lValor = new RectangleF(z.Right - largValor, z.Y, largValor, 21f * s);
            PintorDoWidget.Texto(g, valor, f.Pct, PintorDoWidget.Primario(escuro), escuro, lValor, StringAlignment.Far, StringAlignment.Center);
            PintorDoWidget.Texto(g, _lav.Nome, f.Nome, PintorDoWidget.Primario(escuro), escuro,
                new RectangleF(x, z.Y, Math.Max(0f, lValor.X - x - 6f * s), 21f * s), StringAlignment.Near, StringAlignment.Center);

            // a mesma capsula do widget de bateria, na espessura do traco dos aneis
            float h = PintorDoWidget.Traco * s, cy = z.Y + 29f * s;
            Color cor = CorDoCiclo(c);
            using (Pen trilho = PintorDoWidget.Redonda(Color.FromArgb(escuro ? 70 : 60, cor), h)) g.DrawLine(trilho, x + h / 2f, cy, z.Right - h / 2f, cy);
            float wp = (w - h) * (c.Erro ? 1f : Math.Max(0f, Math.Min(1f, c.Progresso)));
            if (wp > 0.5f)
            {
                using (Pen luz = PintorDoWidget.Redonda(Color.FromArgb(55, cor), h * 2f)) g.DrawLine(luz, x + h / 2f, cy, x + h / 2f + wp, cy);
                using (Pen pen = PintorDoWidget.Redonda(cor, h)) g.DrawLine(pen, x + h / 2f, cy, x + h / 2f + wp, cy);
            }
            PintorDoWidget.Texto(g, LinhaDaLavadora(c), f.Nota, c.Erro ? Color.FromArgb(255, 255, 120, 110) : PintorDoWidget.Primario(escuro), escuro,
                new RectangleF(x, cy + h / 2f + 2f * s, w, 17f * s), StringAlignment.Near, StringAlignment.Center);
        }

        /// <summary>A maquininha (caixa com a porta redonda), em branco como os icones do widget de bateria.</summary>
        private static void IconeDaLavadora(Graphics g, RectangleF bolha, float s)
        {
            float lado = bolha.Width * 0.46f;
            RectangleF r = new RectangleF(bolha.X + (bolha.Width - lado * 0.86f) / 2f, bolha.Y + (bolha.Height - lado) / 2f, lado * 0.86f, lado);
            using (Pen pen = new Pen(Color.White, 1.6f * s))
            {
                using (GraphicsPath gp = PintorDoWidget.Arredondado(r, 2.5f * s)) g.DrawPath(pen, gp);
                float d = r.Width * 0.62f;
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
