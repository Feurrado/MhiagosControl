using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Management;
using System.Threading;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Pecas da Visao geral nova: fontes monoespacadas, as cores quente e fria,
    /// o nome de quem usa o Windows e o texto do cabecalho.
    ///
    /// Numeros, rotulos tecnicos e graficos vao em Cascadia Mono (Consolas se
    /// nao houver); o resto continua na fonte do app. Valores frios (uso, alvo
    /// do ar, barras) na cor de destaque; quentes (temperaturas, tempo da
    /// lavadora, bateria) em ambar, com tom proprio em cada tema para o
    /// contraste nao cair no claro.
    /// </summary>
    internal static class Visao
    {
        public static readonly string FamiliaMono = Ui.Tipo.Achar(new[] { "Cascadia Mono", "Consolas" }, "Consolas");
        public static readonly Font MonoPequena = new Font(FamiliaMono, Ui.Pt(8f));
        public static readonly Font MonoRotulo = new Font(FamiliaMono, Ui.Pt(8f));
        public static readonly Font MonoTexto = MonoSemi(Ui.Pt(9.5f));
        public static readonly Font MonoValor = MonoSemi(Ui.Pt(15f));
        public static readonly Font MonoGrande = MonoSemi(Ui.Pt(21f));
        public static readonly Font FonteFrase = Ui.Semi(Ui.Pt(17f), Ui.Tipo.Exibicao);

        private static Font MonoSemi(float tamanho)
        {
            try
            {
                Font f = new Font(FamiliaMono + " SemiBold", tamanho);
                // Familia inexistente volta como outra: o negrito da mono e melhor que uma sem serifa.
                if (f.Name.IndexOf(FamiliaMono, StringComparison.OrdinalIgnoreCase) >= 0) return f;
                f.Dispose();
            }
            catch { }
            return new Font(FamiliaMono, tamanho, FontStyle.Bold);
        }

        /// <summary>Ambar dos valores quentes: #f2b84b no escuro, #b35c00 no claro.</summary>
        public static Color Quente { get { return Ui.Dark ? Color.FromArgb(0xF2, 0xB8, 0x4B) : Color.FromArgb(0xB3, 0x5C, 0x00); } }

        public static readonly Color Verde = Color.FromArgb(0x2E, 0xA0, 0x43);

        /// <summary>Roda na thread da interface (o ServicoCasa avisa de outra thread).</summary>
        public static void NaInterface(Control c, MethodInvoker m)
        {
            try
            {
                if (c.IsDisposed) return;
                // sem handle ainda: nada de mexer no controle da thread do servico; vai para a da interface
                if (!c.IsHandleCreated) { Motion.NaInterface(delegate { if (!c.IsDisposed) m(); }); return; }
                if (c.InvokeRequired) c.BeginInvoke(m); else m();
            }
            catch (InvalidOperationException) { }
        }

        public static string Maiusculas(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.ToUpper(T.Pt ? new CultureInfo("pt-BR") : CultureInfo.InvariantCulture);
        }

        /// <summary>"SEG 26/10 · 22:20" (en: "MON 10/26 · 22:20").</summary>
        public static string Data(DateTime d)
        {
            CultureInfo c = new CultureInfo(T.Pt ? "pt-BR" : "en-US");
            string dia = c.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek).TrimEnd('.').ToUpper(c);
            return dia + " " + d.ToString(T.Pt ? "dd/MM" : "MM/dd", c) + " · " + d.ToString("HH:mm", c);
        }

        // ---------------- nome de quem usa ----------------

        private static string _nome;
        private static bool _buscou;

        /// <summary>O nome completo chegou da consulta (em segundo plano): o cabecalho repinta na hora, sem esperar o relogio de 15 s.</summary>
        public static event EventHandler NomeChegou;

        /// <summary>
        /// O primeiro nome do usuario do Windows. A conta local guarda o nome
        /// completo (numa conta Microsoft, o da conta); o nome de login fica
        /// como reserva enquanto a consulta, em segundo plano, nao volta.
        /// </summary>
        public static string NomeDoUsuario
        {
            get
            {
                if (!_buscou)
                {
                    _buscou = true;
                    _nome = PrimeiroNome(Environment.UserName);
                    Thread t = new Thread(BuscarNome);
                    t.IsBackground = true;
                    t.Start();
                }
                return _nome;
            }
        }

        private static void BuscarNome()
        {
            try
            {
                string login = Environment.UserName.Replace("\\", "\\\\").Replace("'", "\\'");
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT FullName FROM Win32_UserAccount WHERE LocalAccount=True AND Name='" + login + "'"))
                    foreach (ManagementBaseObject o in s.Get())
                    {
                        string f = o["FullName"] as string;
                        if (!string.IsNullOrEmpty(f) && f.Trim().Length > 0) { _nome = PrimeiroNome(f); EventHandler h = NomeChegou; if (h != null) h(null, EventArgs.Empty); break; }
                    }
            }
            catch (Exception ex) { Log.Error("nome do usuario", ex); }
        }

        internal static string PrimeiroNome(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string p = s.Trim().Split(new[] { ' ', '.', '_' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return p.Length == 0 ? "" : char.ToUpper(p[0]) + p.Substring(1);
        }

        // ---------------- textos do cabecalho ----------------

        /// <summary>A frase de atencao, ou nulo quando esta tudo em ordem.</summary>
        public static string Alerta()
        {
            if (ServicoCasa.Logado)
                foreach (Aparelho a in ServicoCasa.Aparelhos)
                {
                    if (a.Lavadora && a.Online && CicloAtual.De(a).Erro) return T.VisaoAtencaoLavadora;
                    if (a.ArCondicionado && a.Online && Diagnostico.TemFalha(a)) return T.VisaoAtencaoAr;
                }
            foreach (Periferico p in Perifericos.Atual)
                if (p != null && p.Bateria.HasValue && p.Bateria.Value <= EventosDaVisao.BateriaBaixa && !p.Carregando)
                    return T.VisaoAtencaoBateria(p.Nome);
            return null;
        }

        /// <summary>"A roupa fica pronta as 22:45" com ciclo em andamento; a temperatura do escritorio fica no cartao do ar.</summary>
        public static string Resumo()
        {
            // Nada por ora: a temperatura do escritorio esta no cartao do ar e a hora
            // em que a roupa fica pronta, no cartao da lavadora (e no widget).
            return "";
        }

        public static DateTime FimDoCiclo(CicloAtual c)
        {
            DateTime leitura = ServicoCasa.UltimaLeitura == DateTime.MinValue ? DateTime.Now : ServicoCasa.UltimaLeitura;
            return leitura.AddMinutes(c.Restante);
        }

        public static string Temperatura(Aparelho ar, double v)
        {
            return v.ToString("0.#", CultureInfo.CurrentCulture) + (ar.Int("t_temp_type", 0) == 1 ? " °F" : " °C");
        }
    }

    /// <summary>
    /// Os ultimos acontecimentos da casa e dos perifericos, em memoria: mudancas
    /// no ar, fases da lavadora e bateria baixa. A nuvem nao da historico de
    /// eventos, entao a lista nasce aqui, comparando cada leitura com a
    /// anterior; a primeira leitura de cada aparelho so serve de base.
    /// </summary>
    internal static class EventosDaVisao
    {
        internal sealed class Evento
        {
            public DateTime Hora;
            public string Texto;
        }

        private sealed class Visto
        {
            public bool Online, Rodando, Concluido, Erro;
            public int Power, Temp, Modo, Fase, Codigo;
        }

        public const int BateriaBaixa = 20;
        private const int Maximo = 30;

        private static readonly object _trava = new object();
        private static readonly List<Evento> _lista = new List<Evento>();
        private static readonly Dictionary<string, Visto> _vistos = new Dictionary<string, Visto>();
        private static readonly Dictionary<string, int> _baterias = new Dictionary<string, int>();
        private static bool _iniciado;

        /// <summary>Lista mudou. Pode vir de outra thread.</summary>
        public static event EventHandler Mudou;

        /// <summary>Passa a acompanhar a casa. Chamado pela bandeja e pela janela; so assina uma vez.</summary>
        public static void Iniciar()
        {
            lock (_trava) { if (_iniciado) return; _iniciado = true; }
            ServicoCasa.Mudou += delegate { ObservarCasa(ServicoCasa.Logado ? ServicoCasa.Aparelhos : new List<Aparelho>()); };
            if (ServicoCasa.Logado) ObservarCasa(ServicoCasa.Aparelhos);
        }

        public static Evento[] Recentes(int n)
        {
            lock (_trava)
            {
                int k = Math.Min(n, _lista.Count);
                Evento[] r = new Evento[k];
                for (int i = 0; i < k; i++) r[i] = _lista[_lista.Count - 1 - i];
                return r;
            }
        }

        /// <summary>Testes: comeca do zero.</summary>
        internal static void Limpar()
        {
            lock (_trava) { _lista.Clear(); _vistos.Clear(); _baterias.Clear(); }
        }

        internal static void Acrescentar(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return;
            lock (_trava)
            {
                Evento e = new Evento();
                e.Hora = DateTime.Now; e.Texto = texto;
                _lista.Add(e);
                if (_lista.Count > Maximo) _lista.RemoveAt(0);
            }
            EventHandler h = Mudou;
            if (h != null) h(null, EventArgs.Empty);
        }

        internal static void ObservarCasa(List<Aparelho> aparelhos)
        {
            if (aparelhos == null) return;
            List<string> novos = new List<string>();
            lock (_trava)
            {
                foreach (Aparelho a in aparelhos)
                {
                    if (a == null || string.IsNullOrEmpty(a.Puid)) continue;
                    Visto v = new Visto();
                    v.Online = a.Online;
                    CicloAtual c = null;
                    if (a.ArCondicionado)
                    {
                        v.Power = a.Int("t_power", 0); v.Temp = a.Int("t_temp", 0); v.Modo = a.Int("t_work_mode", -1);
                    }
                    else if (a.Lavadora)
                    {
                        c = CicloAtual.De(a);
                        v.Rodando = c.Rodando; v.Concluido = c.Concluido; v.Erro = c.Erro; v.Fase = c.Fase; v.Codigo = c.CodigoDeErro;
                    }
                    else continue;

                    Visto antes;
                    bool tinha = _vistos.TryGetValue(a.Puid, out antes);
                    _vistos[a.Puid] = v;
                    if (!tinha) continue;

                    if (antes.Online && !v.Online) { novos.Add(a.ArCondicionado ? T.EvArOffline : T.EvLavOffline); continue; }
                    if (!v.Online) continue;
                    string u = a.Int("t_temp_type", 0) == 1 ? "°F" : "°C";
                    if (a.ArCondicionado)
                    {
                        if (antes.Power != v.Power) novos.Add(v.Power != 0 ? T.EvArLigado(v.Temp + " " + u) : T.EvArDesligado);
                        else if (v.Power != 0 && antes.Temp != v.Temp) novos.Add(T.EvArTemp(v.Temp + " " + u));
                        else if (v.Power != 0 && antes.Modo != v.Modo) novos.Add(T.EvArModo(TabelaDoAr.Modo(v.Modo, a.Tipo)));
                    }
                    else
                    {
                        if (v.Erro && !antes.Erro) novos.Add(T.EvLavErro(v.Codigo));
                        else if (v.Rodando && !antes.Rodando) novos.Add(T.EvLavInicio(TabelaDaLavadora.Programa(c.Programa)));
                        else if (antes.Rodando && !v.Rodando) novos.Add(T.EvLavFim);
                        else if (v.Rodando && antes.Fase != v.Fase && TabelaDaLavadora.Fase(v.Fase).Length > 0)
                            novos.Add(T.EvLavFase(TabelaDaLavadora.Fase(v.Fase)));
                    }
                }
            }
            foreach (string s in novos) Acrescentar(s);
        }

        internal static void ObservarPerifericos(Periferico[] lista)
        {
            if (lista == null) return;
            List<string> novos = new List<string>();
            lock (_trava)
            {
                foreach (Periferico p in lista)
                {
                    if (p == null || !p.Bateria.HasValue) continue;
                    int antes;
                    bool tinha = _baterias.TryGetValue(p.Nome, out antes);
                    _baterias[p.Nome] = p.Bateria.Value;
                    if (tinha && antes > BateriaBaixa && p.Bateria.Value <= BateriaBaixa && !p.Carregando)
                        novos.Add(T.EvBateria(p.Nome, p.Bateria.Value));
                }
            }
            foreach (string s in novos) Acrescentar(s);
        }
    }

    /// <summary>
    /// Data e hora, a frase grande ("Tudo em ordem, Thiago.") e, a direita,
    /// o resumo do que a casa esta fazendo. Sem lugar ao lado, o resumo desce
    /// para baixo da frase.
    /// </summary>
    internal sealed class CabecalhoDaVisao : Control
    {
        private readonly System.Windows.Forms.Timer _relogio;
        private readonly EventHandler _mudou;

        public CabecalhoDaVisao()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _relogio = new System.Windows.Forms.Timer();
            _relogio.Interval = 15000;
            _relogio.Tick += delegate { Invalidate(); };
            _relogio.Start();
            _mudou = delegate { Visao.NaInterface(this, Invalidate); };
            ServicoCasa.Mudou += _mudou;
            Visao.NomeChegou += _mudou;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { ServicoCasa.Mudou -= _mudou; Visao.NomeChegou -= _mudou; _relogio.Dispose(); }
            base.Dispose(disposing);
        }

        public string Frase
        {
            get
            {
                string a = Visao.Alerta();
                return a ?? T.VisaoTudoEmOrdem(Visao.NomeDoUsuario);
            }
        }

        private int AlturaDaFrase { get { return TextRenderer.MeasureText("Ág", Visao.FonteFrase).Height; } }

        /// <summary>O resumo cabe ao lado da frase nesta largura?</summary>
        internal bool ResumoAoLado(int largura)
        {
            string r = Visao.Resumo();
            if (r.Length == 0) return true;
            int f = TextRenderer.MeasureText(Frase, Visao.FonteFrase).Width;
            int s = TextRenderer.MeasureText(r, Ui.FontSmall).Width;
            return f + s + Ui.S(32) <= largura;
        }

        public int AlturaPara(int largura)
        {
            int h = Ui.S(18) + AlturaDaFrase;
            if (!ResumoAoLado(largura)) h += Ui.FontSmall.Height + Ui.S(4);
            return h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Window)) g.FillRectangle(b, ClientRectangle);
            TextRenderer.DrawText(g, Visao.Data(DateTime.Now), Visao.MonoRotulo, new Point(Ui.S(2), 0), Ui.Muted, TextFormatFlags.NoPadding);
            bool alerta = Visao.Alerta() != null;
            int yf = Ui.S(16), hf = AlturaDaFrase;
            string resumo = Visao.Resumo();
            bool lado = ResumoAoLado(Width);
            int wf = Width;
            if (lado && resumo.Length > 0) wf = Width - TextRenderer.MeasureText(resumo, Ui.FontSmall).Width - Ui.S(24);
            TextRenderer.DrawText(g, Frase, Visao.FonteFrase, new Rectangle(0, yf, wf, hf), alerta ? Visao.Quente : Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (resumo.Length == 0) return;
            if (lado)
                TextRenderer.DrawText(g, resumo, Ui.FontSmall, new Rectangle(0, yf + hf - Ui.FontSmall.Height - Ui.S(5), Width, Ui.FontSmall.Height),
                    Ui.Muted, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            else
                TextRenderer.DrawText(g, resumo, Ui.FontSmall, new Rectangle(0, yf + hf + Ui.S(2), Width, Ui.FontSmall.Height),
                    Ui.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// Botao da Visao geral: redondo com contorno na cor de destaque (- e +) ou
    /// pilula de estado ("Ligado"/"Desligado"), que acende quando Aceso.
    /// </summary>
    internal sealed class BotaoDaVisao : Control
    {
        public bool Pilula, Aceso;
        private readonly string _glifo;
        private bool _sobre;
        private LigadoAnimado _aceso;
        private FlashDoBotao _flash;
        private LigadoAnimado TA { get { return _aceso ?? (_aceso = new LigadoAnimado(this)); } }
        private FlashDoBotao Flash { get { return _flash ?? (_flash = new FlashDoBotao(this)); } }

        /// <summary>O - e o + piscam ao clique; a pilula anima pela troca de Aceso.</summary>
        protected override void OnClick(EventArgs e) { if (Enabled && !Pilula) Flash.Disparar(); base.OnClick(e); }

        public BotaoDaVisao(string glifo)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            _glifo = glifo;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { _sobre = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _sobre = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; e.SuppressKeyPress = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            if (Pilula)
            {
                TA.Definir(Aceso);
                float a = Enabled ? TA.Valor : 0f;
                using (GraphicsPath p = Ui.RoundRect(r, r.Height / 2))
                {
                    Color fundo = !Enabled ? Ui.SurfaceAlt : Ui.Misturar(_sobre ? Ui.Hover : Ui.Surface, Ui.AccentSoft, a);
                    using (SolidBrush b = new SolidBrush(fundo)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Ui.Misturar(Ui.Border, Ui.AccentSoft, a))) g.DrawPath(pen, p);
                }
                TextRenderer.DrawText(g, Text, Ui.FontMed, r, !Enabled ? Ui.Faint : Ui.Misturar(Ui.Muted, Ui.AccentText, a),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }
            if (_sobre && Enabled) using (SolidBrush b = new SolidBrush(Ui.AccentSoft)) g.FillEllipse(b, r);
            float fl = Flash.Intensidade;
            if (fl > 0f) using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(210 * fl), Ui.Accent))) g.FillEllipse(b, r);
            using (Pen pen = new Pen(Enabled ? Ui.Accent : Ui.Border, 1.5f)) g.DrawEllipse(pen, r);
            TextRenderer.DrawText(g, _glifo, Ui.FontGlyph9, r, Enabled ? Ui.AccentText : Ui.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused && ShowFocusCues)
                using (Pen pen = new Pen(Ui.AccentText)) { pen.DashStyle = DashStyle.Dot; g.DrawEllipse(pen, Rectangle.Inflate(r, -3, -3)); }
        }
    }

    /// <summary>Base dos cartoes da casa na Visao geral: assina o ServicoCasa e abre a pagina Casa pelo nome.</summary>
    internal abstract class CartaoDaCasaVisao : Card
    {
        public event EventHandler AbrirCasa;
        private readonly EventHandler _mudou;
        protected Rectangle Nome;

        protected CartaoDaCasaVisao()
        {
            Title = null;
            _mudou = delegate { Visao.NaInterface(this, Puxar); };
            ServicoCasa.Mudou += _mudou;
        }

        internal abstract void Puxar();

        protected override void Dispose(bool disposing)
        {
            if (disposing) ServicoCasa.Mudou -= _mudou;
            base.Dispose(disposing);
        }

        protected void PedirCasa() { if (AbrirCasa != null) AbrirCasa(this, EventArgs.Empty); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = Nome.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Nome.Contains(e.Location)) PedirCasa();
            base.OnMouseDown(e);
        }

        /// <summary>Quadrado arredondado do icone, no tom suave do destaque.</summary>
        protected static Rectangle Icone(Graphics g, int x, int y)
        {
            Rectangle r = new Rectangle(x, y, Ui.S(40), Ui.S(40));
            using (GraphicsPath p = Ui.RoundRect(r, Ui.S(11)))
            using (SolidBrush b = new SolidBrush(Ui.AccentSoft)) g.FillPath(b, p);
            return r;
        }

        /// <summary>Nome em seminegrito e a linha de apoio embaixo; guarda a area do nome para o clique.</summary>
        protected void NomeELinha(Graphics g, string nome, string linha, int x, int y, int largura, Color corLinha)
        {
            Size sn = TextRenderer.MeasureText(nome, Ui.FontSection);
            Nome = new Rectangle(x, y, Math.Min(largura, sn.Width), sn.Height);
            TextRenderer.DrawText(g, nome, Ui.FontSection, new Rectangle(x, y, largura, sn.Height), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, linha, Ui.FontSmall, new Rectangle(x, y + sn.Height + Ui.S(2), largura, Ui.FontSmall.Height), corLinha,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// O ar na Visao geral: nome, "Escritorio 23,0 °C · leitura as 13:13", - e +
    /// em volta do alvo grande e a pilula Ligado/Desligado. Os comandos vao
    /// pelo ServicoCasa.Enviar, com a mesma previa otimista da pagina Casa.
    /// </summary>
    internal sealed class CartaoDoArVisao : CartaoDaCasaVisao
    {
        public const int Altura = 92;
        private Aparelho _ar;
        private readonly BotaoDaVisao _menos, _mais, _liga;
        private readonly System.Windows.Forms.Timer _espera;
        private int _alvoPendente = -1;
        private LigadoAnimado _lig;
        private NumeroDeslizante _num;
        internal LigadoAnimado Lig { get { return _lig ?? (_lig = new LigadoAnimado(this)); } }
        internal NumeroDeslizante Num { get { return _num ?? (_num = new NumeroDeslizante(this)); } }

        internal Aparelho Ar { get { return _ar; } }
        internal BotaoDaVisao BotaoLiga { get { return _liga; } }
        internal BotaoDaVisao BotaoMais { get { return _mais; } }
        internal BotaoDaVisao BotaoMenos { get { return _menos; } }
        /// <summary>Testes: o ultimo comando montado.</summary>
        internal Dictionary<string, string> UltimoComando;

        public CartaoDoArVisao()
        {
            _menos = new BotaoDaVisao("\uE738"); _menos.AccessibleName = T.CasaMenosGrau; _menos.Click += delegate { Mudar(-1); };
            _mais = new BotaoDaVisao("\uE710"); _mais.AccessibleName = T.CasaMaisGrau; _mais.Click += delegate { Mudar(+1); };
            _liga = new BotaoDaVisao(null); _liga.Pilula = true; _liga.AccessibleName = T.CasaLigaDesliga;
            _liga.Click += delegate { if (_ar != null) Mandar("t_power", _ar.Int("t_power", 0) != 0 ? 0 : 1); };
            Controls.Add(_menos); Controls.Add(_mais); Controls.Add(_liga);
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
            Puxar();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _espera.Dispose();
            base.Dispose(disposing);
        }

        internal override void Puxar()
        {
            _ar = null;
            if (ServicoCasa.Logado)
                foreach (Aparelho a in ServicoCasa.Aparelhos)
                    if (a.ArCondicionado) { _ar = a; break; }
            Preencher();
            Invalidate();
        }

        private void Preencher()
        {
            if (_ar == null) return;
            bool on = _ar.Online, ligado = _ar.Int("t_power", 0) != 0;
            _liga.Aceso = on && ligado;
            _liga.Text = !on ? T.CasaOffline : ligado ? T.CasaLigado : T.CasaDesligado;
            _liga.Enabled = on;
            _menos.Enabled = _mais.Enabled = on && ligado && _ar.Tem("t_temp");
            _liga.Invalidate();
            Arranjar();
        }

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

        /// <summary>Testes: manda o alvo pendente sem esperar o timer.</summary>
        internal void MandarPendente()
        {
            _espera.Stop();
            if (_alvoPendente < 0) return;
            int v = _alvoPendente; _alvoPendente = -1;
            Mandar("t_temp", v);
        }

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

        protected override void OnResize(EventArgs e) { base.OnResize(e); Arranjar(); }

        private int LarguraDoNumero { get { return Ui.S(64); } }

        private void Arranjar()
        {
            if (_liga == null) return;
            int m = Ui.S(16), bt = Ui.S(32), ph = Ui.S(30);
            int pw = Math.Max(TextRenderer.MeasureText(T.CasaLigado, Ui.FontMed).Width,
                     Math.Max(TextRenderer.MeasureText(T.CasaDesligado, Ui.FontMed).Width, TextRenderer.MeasureText(T.CasaOffline, Ui.FontMed).Width)) + Ui.S(30);
            _liga.SetBounds(Width - m - pw, (Height - ph) / 2, pw, ph);
            _mais.SetBounds(_liga.Left - Ui.S(18) - bt, (Height - bt) / 2, bt, bt);
            _menos.SetBounds(_mais.Left - LarguraDoNumero - bt, (Height - bt) / 2, bt, bt);
        }

        /// <summary>Comodo, temperatura do escritorio agora e hora da leitura (sem leitura: "--" e "sem leitura").</summary>
        internal string[] Partes()
        {
            if (_ar == null) return new string[] { "", "", "" };
            string comodo = string.IsNullOrEmpty(_ar.Comodo) ? T.VisaoAmbienteNome : _ar.Comodo;
            bool tem = _ar.Online && _ar.Tem("f_temp_in");
            string temp = tem ? _ar.Num("f_temp_in", 0).ToString("0.0", new CultureInfo(T.Pt ? "pt-BR" : "en-US"))
                + (_ar.Int("t_temp_type", 0) == 1 ? " °F" : " °C") : "--";
            DateTime l = ServicoCasa.UltimaLeitura;
            string leitura = !tem || l == DateTime.MinValue ? T.VisaoSemLeitura : T.VisaoLeituraAs(l.ToString("HH:mm"));
            return new string[] { comodo, temp, leitura };
        }

        /// <summary>A linha como texto corrido (testes e acessibilidade).</summary>
        internal string Linha()
        {
            string[] p = Partes();
            return p[0] + " " + p[1] + " · " + p[2];
        }

        /// <summary>Fora dos botoes, o cartao inteiro abre a pagina Casa.</summary>
        protected override void OnMouseMove(MouseEventArgs e) { Cursor = Cursors.Hand; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) PedirCasa();
        }

        /// <summary>
        /// "Escritorio 23,0 °C · leitura as 13:13": a temperatura em ambar e
        /// seminegrito; o estado ja esta na pilula, nao se repete. Sem lugar, a
        /// hora da leitura sai antes da temperatura.
        /// </summary>
        private void DesenharLinha(Graphics g, int x, int y, int largura)
        {
            string[] p = Partes();
            TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            x += Ui.S(3);   // o nome acima tem a margem interna do TextRenderer; a linha alinha com ele
            largura -= Ui.S(3);
            Font fl = Ui.FontSmall, ft = Visao.MonoTexto;
            int w0 = TextRenderer.MeasureText(g, p[0] + " ", fl, Size.Empty, f).Width;
            int w1 = TextRenderer.MeasureText(g, p[1], ft, Size.Empty, f).Width;
            // Sem lugar para "leitura as 13:13", fica so a hora; sem lugar nem para ela, nada.
            string resto = "";
            DateTime lida = ServicoCasa.UltimaLeitura;
            string curta = p[1] != "--" && lida != DateTime.MinValue ? "  ·  " + lida.ToString("HH:mm") : "";
            foreach (string op in new string[] { "  ·  " + p[2], curta })
                if (op.Length > 0 && w0 + w1 + TextRenderer.MeasureText(g, op, fl, Size.Empty, f).Width <= largura) { resto = op; break; }
            int hl = fl.Height, ht = ft.Height;
            TextRenderer.DrawText(g, p[0] + " ", fl, new Rectangle(x, y, Math.Min(w0, largura), hl), Ui.Muted, f | TextFormatFlags.EndEllipsis);
            if (w0 >= largura) return;
            bool tem = p[1] != "--";
            TextRenderer.DrawText(g, p[1], ft, new Point(x + w0, y + hl - ht), tem ? Visao.Quente : Ui.Faint, f);
            if (resto.Length > 0) TextRenderer.DrawText(g, resto, fl, new Point(x + w0 + w1, y), Ui.Muted, f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_ar == null) return;
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            int m = Ui.S(16);
            // Ligar e desligar: o icone vai do cinza ao azul (e volta) em ~450 ms. Ao ligar, um anel
            // azul cresce ate 1,6x e some, e a borda do cartao acende e esmaece; ao desligar, um anel
            // cinza se fecha sobre o icone, como quem apaga.
            bool ligadoAgora = _ar.Online && _ar.Int("t_power", 0) != 0;
            Lig.Definir(ligadoAgora);
            float lig = Lig.Valor;
            Rectangle ic = new Rectangle(m, (Height - Ui.S(40)) / 2, Ui.S(40), Ui.S(40));
            float pu = Lig.Pulso, ap = Lig.Apagando;
            if (pu >= 0f)
            {
                using (GraphicsPath borda = Ui.RoundRect(new Rectangle(1, 1, Width - 3, Height - 3), Ui.Radius))
                using (Pen pen = new Pen(Color.FromArgb((int)(220 * (1f - pu)), Ui.Accent), 2.5f)) g.DrawPath(pen, borda);
                int cresce = (int)(ic.Width * 0.3f * pu);
                using (GraphicsPath pp = Ui.RoundRect(Rectangle.Inflate(ic, cresce, cresce), Ui.S(11) + cresce))
                using (Pen pen = new Pen(Color.FromArgb((int)(180 * (1f - pu)), Ui.Accent), 3f)) g.DrawPath(pen, pp);
            }
            if (ap >= 0f)
            {
                int fecha = (int)(ic.Width * 0.3f * (1f - ap));
                using (GraphicsPath pp = Ui.RoundRect(Rectangle.Inflate(ic, fecha, fecha), Ui.S(11) + fecha))
                using (Pen pen = new Pen(Color.FromArgb((int)(170 * (1f - ap)), Ui.Muted), 3f)) g.DrawPath(pen, pp);
            }
            using (GraphicsPath gi = Ui.RoundRect(ic, Ui.S(11)))
            using (SolidBrush bi = new SolidBrush(Ui.Misturar(Ui.SurfaceAlt, Ui.AccentSoft, lig))) g.FillPath(bi, gi);
            IconesCasa.GlifoCentrado(g, "\uE9CA", Ui.FontGlyph11, ic, Ui.Misturar(Ui.Faint, Ui.AccentText, lig));
            int tx = ic.Right + Ui.S(14);
            int alto = Ui.FontSection.Height + Ui.S(2) + Ui.FontSmall.Height;
            int wt = Math.Max(Ui.S(40), _menos.Left - Ui.S(12) - tx), yt = (Height - alto) / 2;
            NomeELinha(g, _ar.Nome, "", tx, yt, wt, Ui.Muted);
            DesenharLinha(g, tx, yt + Ui.FontSection.Height + Ui.S(2), wt);

            int alvo = _alvoPendente >= 0 ? _alvoPendente : _ar.Int("t_temp", 0);
            Rectangle num = new Rectangle(_menos.Right, 0, _mais.Left - _menos.Right, Height);
            // o alvo desliza ao subir ou descer, e passa do cinza a cor ativa ao ligar
            string ta = alvo > 0 ? alvo + "°" : "--";
            Size st = TextRenderer.MeasureText(g, ta, Visao.MonoGrande, Size.Empty, TextFormatFlags.NoPadding);
            Num.Desenhar(g, ta, Visao.MonoGrande, new Point(num.X + (num.Width - st.Width) / 2, (Height - st.Height) / 2),
                new Rectangle(num.X, Ui.S(8), num.Width, Height - Ui.S(16)), Ui.Misturar(Ui.Faint, Ui.Accent, lig), Ui.Surface, TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// A lavadora na Visao geral, so com ciclo em andamento: programa, fase,
    /// temperatura e centrifugacao; o tempo que falta em ambar com a hora do
    /// fim; a barra de progresso e as etapas, com a atual em negrito.
    /// </summary>
    internal sealed class CartaoDaLavadoraVisao : CartaoDaCasaVisao
    {
        public const int Altura = 128;
        private Aparelho _lav;
        private readonly System.Windows.Forms.Timer _relogio;
        private readonly List<string> _etapas = new List<string>();

        internal Aparelho Lavadora { get { return _lav; } }

        public CartaoDaLavadoraVisao()
        {
            _relogio = new System.Windows.Forms.Timer();
            _relogio.Interval = 30000;
            _relogio.Tick += delegate { Invalidate(); };
            _relogio.Start();
            Puxar();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _relogio.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Ha ciclo em andamento para mostrar.</summary>
        public bool TemCiclo
        {
            get
            {
                if (_lav == null) return false;
                CicloAtual c = CicloAtual.De(_lav);
                return c.Online && c.Rodando;
            }
        }

        internal override void Puxar()
        {
            _lav = null;
            if (ServicoCasa.Logado)
                foreach (Aparelho a in ServicoCasa.Aparelhos)
                    if (a.Lavadora) { _lav = a; break; }
            Invalidate();
        }

        internal string Linha()
        {
            if (_lav == null) return "";
            CicloAtual c = CicloAtual.De(_lav);
            List<string> l = new List<string>();
            if (c.Programa > 0) l.Add(TabelaDaLavadora.Programa(c.Programa));
            l.Add(c.Titulo());
            if (_lav.Tem("temperature")) l.Add(TabelaDaLavadora.Temperatura(_lav.Int("temperature", 0)));
            if (_lav.Tem("Spin_speed_rpm")) l.Add(TabelaDaLavadora.Centrifugacao(_lav.Int("Spin_speed_rpm", 0)));
            return string.Join(" · ", l.ToArray());
        }

        internal static string Relogio(int minutos)
        {
            if (minutos < 0) minutos = 0;
            return string.Format("{0:00}:{1:00}", minutos / 60, minutos % 60);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_lav == null) return;
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            CicloAtual c = CicloAtual.De(_lav);
            int m = Ui.S(16);

            Rectangle ic = Icone(g, m, Ui.S(14));
            IconesCasa.Lavadora(g, Rectangle.Inflate(ic, -Ui.S(9), -Ui.S(9)), Ui.AccentText);

            // o tempo que falta, a direita, e a hora do fim embaixo
            string tempo = Relogio(c.Restante);
            Size st = TextRenderer.MeasureText(g, tempo, Visao.MonoGrande, Size.Empty, TextFormatFlags.NoPadding);
            int xt = Width - m - st.Width;
            TextRenderer.DrawText(g, tempo, Visao.MonoGrande, new Point(xt, Ui.S(12)), Visao.Quente, TextFormatFlags.NoPadding);
            string fim = T.VisaoFim(Visao.FimDoCiclo(c).ToString("HH:mm"));
            Size sf = TextRenderer.MeasureText(g, fim, Visao.MonoPequena, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, fim, Visao.MonoPequena, new Point(Width - m - sf.Width, Ui.S(14) + st.Height + Ui.S(2)), Ui.Muted, TextFormatFlags.NoPadding);

            int tx = ic.Right + Ui.S(14);
            int alto = Ui.FontSection.Height + Ui.S(2) + Ui.FontSmall.Height;
            NomeELinha(g, _lav.Nome, Linha(), tx, ic.Top + (ic.Height - alto) / 2, Math.Max(Ui.S(40), Math.Min(xt, Width - m - sf.Width) - Ui.S(14) - tx),
                c.Erro ? Ui.Danger : Ui.Muted);

            // barra fina de progresso
            int yb = Ui.S(72);
            Rectangle trilho = new Rectangle(m, yb, Width - 2 * m, Ui.S(4));
            using (GraphicsPath p = Ui.RoundRect(trilho, trilho.Height / 2))
            using (SolidBrush b = new SolidBrush(Ui.Border)) g.FillPath(b, p);
            int w = (int)Math.Round(trilho.Width * Math.Max(0f, Math.Min(1f, c.Progresso)));
            if (w >= trilho.Height)
                using (GraphicsPath p = Ui.RoundRect(new Rectangle(trilho.X, trilho.Y, w, trilho.Height), trilho.Height / 2))
                using (SolidBrush b = new SolidBrush(c.Pausada ? Ui.Warn : Ui.Accent)) g.FillPath(b, p);

            // etapas, a atual em negrito
            int atual = EtapasDoCiclo.Montar(_lav, c, _etapas);
            int n = _etapas.Count;
            if (n == 0) return;
            int y = yb + Ui.S(16), larg = (Width - 2 * m) / n;
            for (int i = 0; i < n; i++)
            {
                bool essa = i == atual;
                Font f = essa ? Ui.FontMed : Ui.FontSmall;
                Color cor = essa ? Ui.Text : (atual >= 0 && i < atual) ? Ui.Muted : Ui.Faint;
                TextFormatFlags al = i == 0 ? TextFormatFlags.Left : i == n - 1 ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter;
                Rectangle rr = i == 0 ? new Rectangle(m, y, larg, Ui.S(20))
                    : i == n - 1 ? new Rectangle(Width - m - larg, y, larg, Ui.S(20))
                    : new Rectangle(m + (Width - 2 * m) * i / (n - 1) - larg / 2, y, larg, Ui.S(20));
                TextRenderer.DrawText(g, _etapas[i], f, rr, cor, al | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>Sem conta da casa (ou sem aparelhos): convite discreto que abre a pagina Casa.</summary>
    internal sealed class ConviteDaCasa : CartaoDaCasaVisao
    {
        public const int Altura = 76;

        public ConviteDaCasa() { Cursor = Cursors.Hand; }

        internal override void Puxar() { Invalidate(); }

        protected override void OnMouseMove(MouseEventArgs e) { Cursor = Cursors.Hand; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) PedirCasa();
        }

        internal string Titulo { get { return ServicoCasa.Logado ? T.NavCasa : T.VisaoConecteCasa; } }

        internal string Texto
        {
            get
            {
                if (!ServicoCasa.Logado) return T.CasaResumoSemConta;
                return ServicoCasa.Buscando ? T.CasaCarregando : T.CasaSemAparelhos;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            int m = Ui.S(16);
            Rectangle ic = Icone(g, m, (Height - Ui.S(40)) / 2);
            IconesCasa.GlifoCentrado(g, "\uE80F", Ui.FontGlyph11, ic, Ui.AccentText);
            string link = T.VisaoAbrirCasa;
            Size sl = TextRenderer.MeasureText(link, Ui.FontMed);
            TextRenderer.DrawText(g, link, Ui.FontMed, new Point(Width - m - sl.Width, (Height - sl.Height) / 2), Ui.AccentText, TextFormatFlags.NoPrefix);
            int tx = ic.Right + Ui.S(14);
            int alto = Ui.FontSection.Height + Ui.S(2) + Ui.FontSmall.Height;
            NomeELinha(g, Titulo, Texto, tx, (Height - alto) / 2, Math.Max(Ui.S(40), Width - m - sl.Width - Ui.S(16) - tx), Ui.Muted);
        }
    }

    /// <summary>Caixa compacta dos perifericos: nome e carga, com "Baterias ▸" para a pagina.</summary>
    internal sealed class CaixaDePerifericos : Card
    {
        public event EventHandler BateriasPedidas;
        private Periferico[] _itens = new Periferico[0];
        private Rectangle _link;
        public const int Altura = 132;
        private const int MaxLinhas = 4;

        public CaixaDePerifericos() { Title = null; }

        public void Atualizar(Periferico[] itens)
        {
            _itens = itens ?? new Periferico[0];
            Invalidate();
        }

        internal int Linhas
        {
            get { int n = 0; foreach (Periferico p in _itens) if (p != null) n++; return Math.Min(n, MaxLinhas); }
        }

        protected override void OnMouseMove(MouseEventArgs e) { Cursor = _link.Contains(e.Location) ? Cursors.Hand : Cursors.Default; base.OnMouseMove(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _link.Contains(e.Location) && BateriasPedidas != null) BateriasPedidas(this, EventArgs.Empty);
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int m = Ui.S(14);
            TextRenderer.DrawText(g, T.VisaoChipPerifericos, Ui.FontMed, new Point(m, Ui.S(12)), Ui.Text, TextFormatFlags.NoPrefix);
            Size sl = TextRenderer.MeasureText(T.VisaoBaterias, Ui.FontSmall);
            _link = new Rectangle(Width - m - sl.Width, Ui.S(13), sl.Width, sl.Height);
            TextRenderer.DrawText(g, T.VisaoBaterias, Ui.FontSmall, _link.Location, Ui.AccentText, TextFormatFlags.NoPrefix);

            int y = Ui.S(42), passo = Ui.S(21), linhas = 0;
            foreach (Periferico p in _itens)
            {
                if (p == null || linhas >= MaxLinhas) continue;
                string pct = p.Bateria.HasValue ? p.Bateria.Value + "%" : "--";
                Color cor = !p.Bateria.HasValue ? Ui.Faint : p.Carregando ? Ui.AccentText
                    : p.Bateria.Value <= EventosDaVisao.BateriaBaixa ? Ui.Danger : Visao.Quente;
                Size sp = TextRenderer.MeasureText(pct, Visao.MonoTexto);
                TextRenderer.DrawText(g, pct, Visao.MonoTexto, new Point(Width - m - sp.Width, y), cor, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, p.Nome, Ui.FontBase, new Rectangle(m, y, Width - 2 * m - sp.Width - Ui.S(8), passo), Ui.Text,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                y += passo;
                linhas++;
            }
            if (linhas == 0)
                TextRenderer.DrawText(g, T.VisaoNenhum, Ui.FontSmall, new Point(m, y), Ui.Muted, TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Os ultimos eventos (EventosDaVisao), em monoespacada, do mais novo ao mais velho.</summary>
    internal sealed class CaixaDeEventos : Card
    {
        private readonly EventHandler _mudou;
        private const int MaxLinhas = 5;

        public CaixaDeEventos()
        {
            Title = null;
            _mudou = delegate { Visao.NaInterface(this, Invalidate); };
            EventosDaVisao.Mudou += _mudou;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) EventosDaVisao.Mudou -= _mudou;
            base.Dispose(disposing);
        }

        private int Passo { get { return Ui.S(19); } }

        /// <summary>Quantas linhas cabem nesta altura (3 a 5).</summary>
        internal int Cabem { get { return Math.Max(1, Math.Min(MaxLinhas, (Height - Ui.S(46)) / Passo)); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int m = Ui.S(14);
            TextRenderer.DrawText(g, T.VisaoEventos, Visao.MonoRotulo, new Point(m, Ui.S(15)), Ui.Muted, TextFormatFlags.NoPrefix);
            EventosDaVisao.Evento[] ev = EventosDaVisao.Recentes(Cabem);
            int y = Ui.S(42);
            if (ev.Length == 0)
            {
                TextRenderer.DrawText(g, T.VisaoSemEventos, Ui.FontSmall, new Rectangle(m, y, Width - 2 * m, Ui.S(36)), Ui.Muted,
                    TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            int wh = TextRenderer.MeasureText("00:00", Visao.MonoPequena).Width + Ui.S(4);
            foreach (EventosDaVisao.Evento x in ev)
            {
                TextRenderer.DrawText(g, x.Hora.ToString("HH:mm"), Visao.MonoPequena, new Point(m, y), Ui.Faint, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, x.Texto, Visao.MonoPequena, new Rectangle(m + wh, y, Width - 2 * m - wh, Passo), Ui.Text,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                y += Passo;
            }
        }
    }

    /// <summary>
    /// O cartao do cooler. Na Visao geral nova pinta tambem "● conectado" no
    /// titulo e a linha fina que separa a previa das duas curvas.
    /// </summary>
    internal sealed class CartaoDoCooler : Card
    {
        public bool Nova, Conectado;
        public int Linha = -1;

        internal string Estado { get { return "● " + (Conectado ? T.VisaoConectado : T.VisaoDesconectado); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!Nova) return;
            Graphics g = e.Graphics;
            int m = Ui.S(14);
            string txt = Conectado ? T.VisaoConectado : T.VisaoDesconectado;
            Size st = TextRenderer.MeasureText(txt, Ui.FontSmall);
            Size sp = TextRenderer.MeasureText("●", Ui.FontSmall);
            int y = (TitleHeight - st.Height) / 2;
            TextRenderer.DrawText(g, txt, Ui.FontSmall, new Point(Width - m - st.Width, y), Ui.Muted, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "●", Ui.FontSmall, new Point(Width - m - st.Width - sp.Width + Ui.S(4), y), Conectado ? Visao.Verde : Ui.Faint, TextFormatFlags.NoPrefix);
            if (Linha > 0)
                using (Pen p = new Pen(Ui.Border)) g.DrawLine(p, m, Linha, Width - m, Linha);
        }
    }



    public static partial class T
    {
        public static string VisaoNovaOpcao { get { return P("Usar a Visão geral clássica", "Use the classic overview"); } }
        public static string VisaoNovaNota { get { return P("A de antes: o display do cooler em destaque e a casa numa faixa embaixo.", "The previous layout: the cooler display up front and the home in a strip below."); } }
        public static string VisaoChipPerifericos { get { return P("Periféricos", "Peripherals"); } }
        public static string VisaoChipCooler { get { return P("Display do cooler", "Cooler display"); } }
        public static string VisaoNenhum { get { return P("nenhum conectado", "none connected"); } }
        public static string VisaoTudoEmOrdem(string nome) { return string.IsNullOrEmpty(nome) ? P("Tudo em ordem.", "All good.") : string.Format(P("Tudo em ordem, {0}.", "All good, {0}."), nome); }
        public static string VisaoAtencaoLavadora { get { return P("A lavadora acusou erro", "The washer reported an error"); } }
        public static string VisaoAtencaoAr { get { return P("O ar-condicionado acusou falha", "The air conditioner reported a fault"); } }
        public static string VisaoAtencaoBateria(string nome) { return string.Format(P("Bateria baixa no {0}", "{0} battery is low"), nome); }
        public static string VisaoRoupaPronta(string h) { return string.Format(P("A roupa fica pronta às {0}", "Laundry done at {0}"), h); }
        public static string VisaoAmbienteNome { get { return P("ambiente", "indoors"); } }
        public static string VisaoEventos { get { return P("EVENTOS", "EVENTS"); } }
        public static string VisaoSemEventos { get { return P("Nada novo desde que o app abriu.", "Nothing new since the app started."); } }
        public static string VisaoBaterias { get { return P("Baterias ▸", "Batteries ▸"); } }
        public static string VisaoAbrirPaineis { get { return P("Abrir Painéis ▸", "Open Panels ▸"); } }
        public static string VisaoAbrirCasa { get { return P("Abrir Casa ▸", "Open Home ▸"); } }
        public static string VisaoConectado { get { return P("conectado", "connected"); } }
        public static string VisaoDesconectado { get { return P("desconectado", "disconnected"); } }
        public static string VisaoConecteCasa { get { return P("Conecte a casa", "Connect your home"); } }
        public static string VisaoSemLeitura { get { return P("sem leitura", "no reading"); } }
        public static string VisaoLeituraAs(string h) { return P("leitura às ", "read at ") + h; }
        public static string VisaoFim(string h) { return P("fim ", "ends ") + h; }

        public static string DashboardMetricMono(int index, string fallback)
        {
            switch (index)
            {
                case 0: return P("CPU · TEMPERATURA", "CPU · TEMPERATURE");
                case 1: return P("CPU · USO", "CPU · USAGE");
                case 2: return P("GPU · TEMPERATURA", "GPU · TEMPERATURE");
                case 3: return P("GPU · USO", "GPU · USAGE");
                case 4: return P("MEMÓRIA · USO", "MEMORY · USAGE");
                default: return Visao.Maiusculas(fallback);
            }
        }

        public static string EvArLigado(string t) { return P("Ar ligado · ", "AC on · ") + t; }
        public static string EvArDesligado { get { return P("Ar desligado", "AC off"); } }
        public static string EvArTemp(string t) { return P("Ar ajustado para ", "AC set to ") + t; }
        public static string EvArModo(string m) { return P("Ar no modo ", "AC mode: ") + m; }
        public static string EvArOffline { get { return P("Ar desconectado", "AC went offline"); } }
        public static string EvLavOffline { get { return P("Lavadora desconectada", "Washer went offline"); } }
        public static string EvLavInicio(string p) { return P("Lavadora começou: ", "Washer started: ") + p; }
        public static string EvLavFase(string f) { return P("Lavadora: ", "Washer: ") + f; }
        public static string EvLavFim { get { return P("Lavadora terminou o ciclo", "Washer finished"); } }
        public static string EvLavErro(int c) { return P("Lavadora com erro ", "Washer error ") + c; }
        public static string EvBateria(string n, int p) { return P("Bateria baixa: ", "Low battery: ") + n + " " + p + "%"; }
    }
}
