using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Pagina Casa: o ar-condicionado e a lava e seca da conta ConnectLife.
    ///
    /// Duas caras na mesma pagina. Sem acesso guardado, mostra o cartao de
    /// login; com ele, os cartoes dos aparelhos. Toda chamada a nuvem roda fora
    /// da thread da interface e volta por BeginInvoke - a nuvem leva de meio a
    /// dois segundos para responder, e a janela nao pode congelar esse tempo.
    ///
    /// So consulta a nuvem com a pagina a vista: abrir o Mhiagos para olhar a
    /// temperatura da CPU nao deve acordar o servidor da Hisense a cada 30 s.
    /// </summary>
    internal sealed class PaginaCasa : Pagina
    {
        private static readonly int LarguraMax = Ui.S(1100);
        private static readonly int LarguraDoLogin = Ui.S(520);

        private readonly CartaoDeLogin _login;
        private readonly Label _titulo, _situacao;
        private readonly FlatBtn _btAtualizar, _btSair;
        private readonly List<CartaoDeAparelho> _cartoes = new List<CartaoDeAparelho>();
        private CartaoHistoricoDoAr _historico;
        private readonly Segmented _abas;
        private readonly AjustesDaCasa _ajustes;
        private readonly Label _vazio;
        private readonly EventHandler _mudou;
        private bool _logado;

        /// <summary>Testes: nao le o acesso guardado na maquina, para nao falar com a nuvem.</summary>
        internal static bool IgnorarCofre
        {
            get { return ServicoCasa.IgnorarCofre; }
            set { ServicoCasa.IgnorarCofre = value; }
        }

        internal bool Logado { get { return _logado; } }
        internal CartaoDeLogin Login { get { return _login; } }
        internal List<CartaoDeAparelho> Cartoes { get { return _cartoes; } }
        internal CartaoHistoricoDoAr Historico { get { return _historico; } }
        internal AjustesDaCasa Ajustes { get { return _ajustes; } }
        internal Segmented Abas { get { return _abas; } }

        /// <summary>0 Painel, 1 Ajustes.</summary>
        internal int Aba
        {
            get { return _abas.SelectedIndex; }
            set { _abas.SelectedIndex = value; }
        }

        /// <summary>Testes: mostra os cartoes como se a nuvem tivesse respondido, sem nuvem.</summary>
        internal void MostrarSemNuvem(List<Aparelho> lista)
        {
            ServicoCasa.Simular(lista);
            PuxarDoServico();
        }

        public PaginaCasa()
        {
            RolarNaVertical();

            _login = new CartaoDeLogin();
            _login.Conectar += delegate(object s, EventArgs e) { Conectar(_login.Codigo); };
            Controls.Add(_login);

            _titulo = new Label();
            _titulo.Text = T.NavCasa;
            _titulo.Font = Ui.FontTitle;
            _titulo.AutoSize = false;
            _titulo.BackColor = Color.Transparent;
            Controls.Add(_titulo);

            _situacao = new Label();
            _situacao.Font = Ui.FontSmall;
            _situacao.AutoSize = false;
            _situacao.AutoEllipsis = true;
            _situacao.BackColor = Color.Transparent;
            _situacao.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_situacao);

            _btAtualizar = new FlatBtn();
            _btAtualizar.Text = T.CasaAtualizar;
            _btAtualizar.Glyph = "\uE72C";
            _btAtualizar.Click += delegate { ServicoCasa.Atualizar(); };
            Controls.Add(_btAtualizar);

            // Sair da conta mora na aba Ajustes; o botao do cabecalho fica so como
            // campo, escondido, para quem ainda o procura pelo nome.
            _btSair = new FlatBtn();
            _btSair.Text = T.CasaSair;
            _btSair.Click += delegate { ServicoCasa.Sair(); };
            _btSair.Visible = false;

            _abas = new Segmented();
            _abas.SetItems(T.CasaPainel, T.CasaAjustes);
            _abas.SelectedIndex = ServicoCasa.Pref.AbaCasa == 1 ? 1 : 0;
            _abas.SelectedIndexChanged += delegate
            {
                ServicoCasa.Pref.AbaCasa = _abas.SelectedIndex;
                ServicoCasa.Pref.Salvar();
                MostrarModo();
            };
            Controls.Add(_abas);

            _ajustes = new AjustesDaCasa();
            _ajustes.Sair += delegate { ServicoCasa.Sair(); };
            _ajustes.ConsumoMudou += delegate { if (_historico != null) _historico.Recalcular(); };
            Controls.Add(_ajustes);

            _vazio = new Label();
            _vazio.Font = Ui.FontBase;
            _vazio.AutoSize = false;
            _vazio.BackColor = Color.Transparent;
            _vazio.Visible = false;
            Controls.Add(_vazio);

            // O servico e ligado pela bandeja (TrayContext.IniciarCasa); nos testes
            // ele fica parado e a pagina mostra o que for simulado.
            _mudou = delegate { NaInterface(PuxarDoServico); };
            ServicoCasa.Mudou += _mudou;
            PuxarDoServico();
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

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            ServicoCasa.PaginaVisivel(Visible);
            _login.Vigiar(Visible && !_logado);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServicoCasa.Mudou -= _mudou;
                ServicoCasa.PaginaVisivel(false);
            }
            base.Dispose(disposing);
        }

        /// <summary>Puxa do servico a lista, o erro e o login, e redesenha.</summary>
        private void PuxarDoServico()
        {
            bool logado = ServicoCasa.Logado;
            if (logado != _logado && !logado)
            {
                foreach (CartaoDeAparelho c in _cartoes) { Controls.Remove(c); c.Dispose(); }
                _cartoes.Clear();
                if (_historico != null) { Controls.Remove(_historico); _historico.Dispose(); _historico = null; }
                string erro = ServicoCasa.Erro;
                if (!string.IsNullOrEmpty(erro)) _login.Mostrar(erro, true);
            }
            _logado = logado;
            if (logado) Receber(ServicoCasa.Aparelhos);
            MostrarModo();
        }

        private void MostrarModo()
        {
            _login.Visible = !_logado;
            bool painel = _abas.SelectedIndex == 0;
            _titulo.Visible = _situacao.Visible = _btAtualizar.Visible = _abas.Visible = _logado;
            _btSair.Visible = false;
            foreach (CartaoDeAparelho c in _cartoes) c.Visible = _logado && painel;
            if (_historico != null) _historico.Visible = _logado && painel;
            _ajustes.Visible = _logado && !painel;
            _vazio.Visible = _logado && painel && _cartoes.Count == 0 && !ServicoCasa.Buscando && ServicoCasa.UltimaLeitura != DateTime.MinValue;
            _vazio.Text = T.CasaSemAparelhos;
            _login.Vigiar(!_logado && Visible);
            AtualizarSituacao();
            Arranjar();
        }

        // ---------------- nuvem ----------------

        private void Conectar(string colado)
        {
            string erro;
            string code = ConnectLifeNuvem.ExtrairCodigo(colado, _login.Estado, out erro);
            if (code == null) { _login.Mostrar(erro, true); return; }

            _login.Ocupado(true);
            _login.Mostrar(T.CasaConectando, false);
            ServicoCasa.Entrar(code, delegate(Exception ex)
            {
                NaInterface(delegate
                {
                    _login.Ocupado(false);
                    if (ex != null) { _login.Mostrar(Mensagem(ex), true); return; }
                    _login.Limpar();
                    PuxarDoServico();
                });
            });
        }

        private void Receber(List<Aparelho> lista)
        {
            // Ar primeiro, lavadora depois, o resto no fim: e a ordem de uso.
            lista.Sort(delegate(Aparelho a, Aparelho b) { return Ordem(a).CompareTo(Ordem(b)); });

            SuspendLayout();
            try
            {
                bool mudou = lista.Count != _cartoes.Count;
                for (int i = 0; !mudou && i < lista.Count; i++)
                    mudou = _cartoes[i].Puid != lista[i].Puid || _cartoes[i].Tipo != Tipo(lista[i]);

                if (mudou)
                {
                    foreach (CartaoDeAparelho c in _cartoes) { Controls.Remove(c); c.Dispose(); }
                    _cartoes.Clear();
                    foreach (Aparelho a in lista)
                    {
                        CartaoDeAparelho c = Novo(a);
                        c.Comando += Enviar;
                        c.MudouAltura += delegate { Arranjar(); };
                        CartaoDoAr ar = c as CartaoDoAr;
                        if (ar != null)
                        {
                            ar.SemBipe = ServicoCasa.SemBipe;
                            ar.SemBipeMudou += delegate { DefinirSemBipe(ar.SemBipe); };
                        }
                        _cartoes.Add(c);
                        Controls.Add(c);
                    }
                }
                for (int i = 0; i < lista.Count; i++) _cartoes[i].Mostrar(lista[i]);
                CartaoDoAr primeiro = null;
                foreach (CartaoDeAparelho c in _cartoes) if (c is CartaoDoAr) { primeiro = (CartaoDoAr)c; break; }
                _ajustes.Adotar(primeiro);

                // Historico e consumo do primeiro ar da conta.
                Aparelho primeiroAr = null;
                foreach (Aparelho a in lista) if (a.ArCondicionado) { primeiroAr = a; break; }
                if (primeiroAr != null && _historico == null)
                {
                    _historico = new CartaoHistoricoDoAr();
                    _historico.MudouAltura += delegate { Arranjar(); };
                    Controls.Add(_historico);
                }
                else if (primeiroAr == null && _historico != null)
                {
                    Controls.Remove(_historico); _historico.Dispose(); _historico = null;
                }
                if (_historico != null) _historico.Mostrar(primeiroAr);
            }
            finally { ResumeLayout(); }
        }

        /// <summary>Preferencia "Comandos sem bipe"; quem guarda na config e a janela.</summary>
        internal bool SemBipe
        {
            get { return ServicoCasa.SemBipe; }
            set
            {
                ServicoCasa.SemBipe = value;
                foreach (CartaoDeAparelho c in _cartoes)
                {
                    CartaoDoAr ar = c as CartaoDoAr;
                    if (ar != null && ar.SemBipe != value) { ar.SemBipe = value; ar.Atualizar(); }
                }
            }
        }

        internal event EventHandler SemBipeMudou;

        private void DefinirSemBipe(bool valor)
        {
            if (valor == ServicoCasa.SemBipe) return;
            SemBipe = valor;
            if (SemBipeMudou != null) SemBipeMudou(this, EventArgs.Empty);
        }

        private static int Ordem(Aparelho a) { return a.ArCondicionado ? 0 : a.Lavadora ? 1 : 2; }
        private static int Tipo(Aparelho a) { return a.ArCondicionado ? 0 : a.Lavadora ? 1 : 2; }

        private static CartaoDeAparelho Novo(Aparelho a)
        {
            if (a.ArCondicionado) return new CartaoDoAr();
            if (a.Lavadora) return new CartaoDaLavadora();
            return new CartaoGenerico();
        }

        private void Enviar(object sender, ComandoEventArgs e)
        {
            CartaoDeAparelho cartao = (CartaoDeAparelho)sender;
            cartao.Enviando(true);
            ServicoCasa.Enviar(e.Puid, e.Propriedades, delegate(Exception ex)
            {
                NaInterface(delegate { cartao.Enviando(false); AtualizarSituacao(); });
            });
        }

        private static string Mensagem(Exception ex)
        {
            return ex is ConnectLifeErro ? ex.Message : T.CasaFalhaInesperada;
        }

        private void AtualizarSituacao()
        {
            string erro = ServicoCasa.Erro;
            if (erro != null)
            {
                _situacao.Text = erro;
                _situacao.ForeColor = Ui.Danger;
                return;
            }
            DateTime ult = ServicoCasa.UltimaLeitura;
            bool buscando = ServicoCasa.Buscando;
            _situacao.ForeColor = Ui.Muted;
            if (buscando && ult == DateTime.MinValue) _situacao.Text = T.CasaCarregando;
            else if (ult == DateTime.MinValue) _situacao.Text = "";
            else _situacao.Text = string.Format(T.CasaAtualizadoAs, ult.ToString("HH:mm")) +
                (buscando ? "  ·  " + T.CasaAtualizando : "");
        }
        // ---------------- arranjo ----------------

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Arranjar();
        }

        internal void Arranjar()
        {
            if (_login == null) return;
            int util = Width - LarguraDaBarra - Ui.S(4);
            if (util < Ui.S(200)) return;
            SuspendLayout();
            try
            {
                if (_login.Visible)
                {
                    int w = Math.Min(LarguraDoLogin, util);
                    int h = _login.Arranjar(w);
                    int y = Math.Max(0, (Height - h) / 3);
                    _login.SetBounds((util - w) / 2, y, w, h);
                }
                else
                {
                    int w = Math.Min(LarguraMax, util);
                    int x = (util - w) / 2;
                    int y = 0;

                    int ba = BotaoW(_btAtualizar), aw = Ui.S(200);
                    _btAtualizar.SetBounds(x + w - ba, y + Ui.S(4), ba, Ui.S(32));
                    _abas.SetBounds(_btAtualizar.Left - Ui.S(10) - aw, y + Ui.S(4), aw, Ui.S(32));
                    int th = TextRenderer.MeasureText(_titulo.Text, _titulo.Font).Height;
                    _titulo.SetBounds(x, y, Math.Max(Ui.S(60), _abas.Left - x - Ui.S(12)), th);
                    _situacao.SetBounds(x + Ui.S(1), y + th, Math.Max(Ui.S(60), _abas.Left - x - Ui.S(12)), Ui.S(20));
                    y += Math.Max(th + Ui.S(20), Ui.S(40)) + Ui.Gap;

                    if (_ajustes.Visible)
                    {
                        int ha = _ajustes.Arranjar(w);
                        _ajustes.SetBounds(x, y, w, ha);
                        y += ha + Ui.Gap;
                    }

                    _vazio.SetBounds(x, y, w, Ui.S(40));

                    // Duas colunas quando cabem dois cartoes de 460: ar e lavadora
                    // lado a lado, que e como se olha para os dois ao mesmo tempo.
                    // O ar tem o painel completo e ocupa a largura toda, no alto.
                    foreach (CartaoDeAparelho c in _cartoes)
                    {
                        if (!(c is CartaoDoAr)) continue;
                        int h = c.Arranjar(w);
                        c.SetBounds(x, y, w, h);
                        y += h + Ui.Gap;
                    }
                    if (_historico != null && _historico.Visible)
                    {
                        int h = _historico.Arranjar(w);
                        _historico.SetBounds(x, y, w, h);
                        y += h + Ui.Gap;
                    }
                    int resto = 0;
                    foreach (CartaoDeAparelho c in _cartoes) if (!(c is CartaoDoAr)) resto++;
                    int colunas = w >= Ui.S(940) && resto > 1 ? 2 : 1;
                    int cw = (w - (colunas - 1) * Ui.Gap) / colunas;
                    int[] topo = new int[colunas];
                    for (int i = 0; i < colunas; i++) topo[i] = y;
                    foreach (CartaoDeAparelho c in _cartoes)
                    {
                        if (c is CartaoDoAr) continue;
                        int col = 0;
                        for (int i = 1; i < colunas; i++) if (topo[i] < topo[col]) col = i;
                        int h = c.Arranjar(cw);
                        c.SetBounds(x + col * (cw + Ui.Gap), topo[col], cw, h);
                        topo[col] += h + Ui.Gap;
                    }
                }
            }
            finally { ResumeLayout(); }
            Sincronizar();
        }

        private static int BotaoW(FlatBtn b)
        {
            return TextRenderer.MeasureText(b.Text, b.Font).Width + Ui.S(32);
        }
    }

    // =====================================================================
    // Login
    // =====================================================================

    /// <summary>
    /// O cartao de entrada: faixa com a marca, tres passos e o campo do
    /// endereco. Vigia a area de transferencia enquanto espera: copiado o
    /// endereco da pagina final, conecta sozinho, sem precisar colar.
    /// </summary>
    internal sealed class CartaoDeLogin : Card
    {
        public event EventHandler Conectar;
        public string Estado { get; private set; }
        public string Codigo { get { return _campo.Texto; } }

        private readonly Label _titulo, _sub, _status, _nota;
        private readonly Passo[] _passos;
        private readonly FlatBtn _btGoogle, _btColar, _btConectar;
        private readonly CampoDeTexto _campo;
        private readonly System.Windows.Forms.Timer _vigia;
        private string _ultimoVisto;
        private bool _abriu;

        private static readonly int Faixa = Ui.S(112);
        private static readonly Font FonteMarca = new Font(Ui.Tipo.Exibicao, Ui.Pt(17f), FontStyle.Bold);
        private static readonly Font FonteSelo = new Font(Ui.Tipo.Icones, Ui.Pt(22f));

        public CartaoDeLogin()
        {
            Estado = ConnectLifeNuvem.NovoEstado();

            _titulo = Rotulo(T.CasaLoginTitulo, Ui.FontAppName, Ui.Text);
            _sub = Rotulo(T.CasaLoginSub, Ui.FontBase, Ui.Muted);

            _passos = new Passo[]
            {
                new Passo(1, T.CasaPasso1), new Passo(2, T.CasaPasso2), new Passo(3, T.CasaPasso3)
            };
            foreach (Passo p in _passos) Controls.Add(p);

            _btGoogle = new FlatBtn();
            _btGoogle.Primary = true;
            _btGoogle.Text = T.CasaEntrarGoogle;
            _btGoogle.Font = Ui.FontMed;
            _btGoogle.Click += delegate { AbrirNavegador(); };
            Controls.Add(_btGoogle);

            _campo = new CampoDeTexto();
            _campo.Placeholder = T.CasaColeAqui;
            _campo.Confirmar += delegate { if (Conectar != null) Conectar(this, EventArgs.Empty); };
            Controls.Add(_campo);

            _btColar = new FlatBtn();
            _btColar.Text = T.CasaColar;
            _btColar.Click += delegate
            {
                try { if (Clipboard.ContainsText()) _campo.Texto = Clipboard.GetText().Trim(); }
                catch (Exception ex) { Log.Error("colar endereco da ConnectLife", ex); }
            };
            Controls.Add(_btColar);

            _btConectar = new FlatBtn();
            _btConectar.Text = T.CasaConectar;
            _btConectar.Click += delegate { if (Conectar != null) Conectar(this, EventArgs.Empty); };
            Controls.Add(_btConectar);

            _status = Rotulo("", Ui.FontSmall, Ui.Muted);
            _nota = Rotulo(T.CasaNota, Ui.FontSmall, Ui.Faint);

            _vigia = new System.Windows.Forms.Timer();
            _vigia.Interval = 800;
            _vigia.Tick += delegate { OlharAreaDeTransferencia(); };
        }

        private Label Rotulo(string texto, Font f, Color cor)
        {
            Label l = new Label();
            l.Text = texto; l.Font = f; l.ForeColor = cor;
            l.AutoSize = false; l.BackColor = Color.Transparent;
            Controls.Add(l);
            return l;
        }

        private void AbrirNavegador()
        {
            Estado = ConnectLifeNuvem.NovoEstado();
            _abriu = true;
            // O que ja estava copiado antes de abrir nao vale como resposta.
            try { _ultimoVisto = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch { }
            try { Process.Start(ConnectLifeNuvem.EnderecoDeLogin(Estado)); }
            catch (Exception ex)
            {
                Log.Error("abrir o login da ConnectLife", ex);
                Mostrar(T.CasaSemNavegador, true);
                return;
            }
            Mostrar(T.CasaEsperando, false);
        }

        /// <summary>Liga ou desliga a vigia da area de transferencia.</summary>
        public void Vigiar(bool ligar)
        {
            if (ligar) _vigia.Start(); else _vigia.Stop();
        }

        private void OlharAreaDeTransferencia()
        {
            if (!_abriu || !_btConectar.Enabled) return;
            string texto;
            try
            {
                if (!Clipboard.ContainsText()) return;
                texto = Clipboard.GetText();
            }
            catch { return; }   // outro programa segurando a area de transferencia
            if (texto == _ultimoVisto) return;
            _ultimoVisto = texto;
            if (texto.IndexOf("code", StringComparison.Ordinal) < 0 ||
                texto.IndexOf("home-assistant", StringComparison.OrdinalIgnoreCase) < 0) return;

            _campo.Texto = texto.Trim();
            if (Conectar != null) Conectar(this, EventArgs.Empty);
        }

        public void Mostrar(string texto, bool erro)
        {
            _status.Text = texto ?? "";
            _status.ForeColor = erro ? Ui.Danger : Ui.AccentText;
            Relayout();
        }

        public void Ocupado(bool ocupado)
        {
            _btConectar.Enabled = _btGoogle.Enabled = _btColar.Enabled = !ocupado;
            _btConectar.Text = ocupado ? T.CasaConectandoCurto : T.CasaConectar;
        }

        public void Limpar()
        {
            _campo.Texto = "";
            _status.Text = "";
            _abriu = false;
        }

        private void Relayout()
        {
            PaginaCasa p = Parent as PaginaCasa;
            if (p != null) p.Arranjar();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _vigia.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Posiciona o conteudo na largura dada e devolve a altura.</summary>
        public int Arranjar(int w)
        {
            int m = Ui.S(28);
            int iw = w - 2 * m;
            int y = Faixa + Ui.S(22);

            y = Bloco(_titulo, m, y, iw) + Ui.S(4);
            y = Bloco(_sub, m, y, iw) + Ui.S(18);

            foreach (Passo p in _passos)
            {
                int h = p.Altura(iw);
                p.SetBounds(m, y, iw, h);
                y += h + Ui.S(8);
            }
            y += Ui.S(10);

            _btGoogle.SetBounds(m, y, iw, Ui.S(42));
            y += Ui.S(42) + Ui.S(16);

            int bc = TextRenderer.MeasureText(_btColar.Text, _btColar.Font).Width + Ui.S(28);
            _campo.SetBounds(m, y, iw - bc - Ui.S(8), Ui.S(36));
            _btColar.SetBounds(m + iw - bc, y, bc, Ui.S(36));
            y += Ui.S(36) + Ui.S(10);

            _btConectar.SetBounds(m, y, iw, Ui.S(36));
            y += Ui.S(36) + Ui.S(10);

            _status.Visible = _status.Text.Length > 0;
            if (_status.Visible) y = Bloco(_status, m, y, iw) + Ui.S(8);

            y += Ui.S(6);
            y = Bloco(_nota, m, y, iw);
            return y + Ui.S(24);
        }

        private static int Bloco(Label l, int x, int y, int w)
        {
            Size s = TextRenderer.MeasureText(l.Text.Length > 0 ? l.Text : " ", l.Font, new Size(w, 0), TextFormatFlags.WordBreak);
            l.SetBounds(x, y, w, s.Height + Ui.S(2));
            return y + s.Height + Ui.S(2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Ui.Smooth(g);

            // A faixa: gradiente do azul do Mhiagos ao verde-agua da ConnectLife,
            // presa ao topo arredondado do cartao.
            Rectangle faixa = new Rectangle(0, 0, Width - 1, Faixa);
            using (GraphicsPath p = TopoArredondado(faixa, Ui.Radius))
            using (LinearGradientBrush b = new LinearGradientBrush(faixa, Ui.Accent, Color.FromArgb(0x00, 0xB0, 0xA9), 20f))
                g.FillPath(b, p);

            // Desenho de linha: a casa com o floco do ar e a gota da lavadora.
            using (Pen ond = new Pen(Color.FromArgb(40, Color.White), Ui.S(2)))
                for (int i = 0; i < 3; i++)
                {
                    int r = Ui.S(70 + i * 34);
                    g.DrawEllipse(ond, Width - Ui.S(70) - r / 2, Faixa / 2 - r / 2, r, r);
                }

            int cx = Ui.S(28), cy = Ui.S(26), lado = Ui.S(60);
            Rectangle selo = new Rectangle(cx, cy, lado, lado);
            using (GraphicsPath p = Ui.RoundRect(selo, Ui.S(14)))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(46, Color.White)))
                g.FillPath(b, p);
            TextRenderer.DrawText(g, "\uE80F", FonteSelo, selo, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, "ConnectLife", FonteMarca, new Point(cx + lado + Ui.S(16), cy + Ui.S(6)), Color.White);
            TextRenderer.DrawText(g, T.CasaFaixaSub, Ui.FontBase, new Point(cx + lado + Ui.S(17), cy + Ui.S(36)),
                Color.FromArgb(230, Color.White));
        }

        private static GraphicsPath TopoArredondado(Rectangle r, int raio)
        {
            int d = raio * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>Um passo numerado: circulo com o numero e o texto ao lado.</summary>
    internal sealed class Passo : Control
    {
        private readonly int _n;

        public Passo(int n, string texto)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            _n = n;
            Text = texto;
            Font = Ui.FontBase;
        }

        private int Recuo { get { return Ui.S(36); } }

        public int Altura(int w)
        {
            Size s = TextRenderer.MeasureText(Text, Font, new Size(w - Recuo, 0), TextFormatFlags.WordBreak);
            return Math.Max(Ui.S(26), s.Height + Ui.S(4));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            Rectangle c = new Rectangle(0, 0, Ui.S(24), Ui.S(24));
            using (SolidBrush b = new SolidBrush(Ui.AccentSoft)) g.FillEllipse(b, c);
            TextRenderer.DrawText(g, _n.ToString(), Ui.FontMed, c, Ui.AccentText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Recuo, Ui.S(2), Width - Recuo, Height), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>Campo de texto com moldura arredondada e texto de exemplo.</summary>
    internal sealed class CampoDeTexto : Control
    {
        private readonly TextBox _box;
        public string Placeholder = "";
        /// <summary>Glifo a esquerda; nulo tira o glifo e o texto encosta na borda.</summary>
        public string Icone = "\uE71B";
        public event EventHandler Confirmar;

        public string Texto
        {
            get { return _box.Text; }
            set { _box.Text = value ?? ""; _box.SelectionStart = 0; Invalidate(); }
        }

        public CampoDeTexto()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            _box = new TextBox();
            _box.BorderStyle = BorderStyle.None;
            _box.Font = Ui.FontBase;
            _box.TextChanged += delegate { Invalidate(); };
            _box.GotFocus += delegate { Invalidate(); };
            _box.LostFocus += delegate { Invalidate(); };
            _box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.Handled = true; e.SuppressKeyPress = true;
                if (Confirmar != null) Confirmar(this, EventArgs.Empty);
            };
            Controls.Add(_box);
            Height = Ui.S(36);
            BackColor = Color.Transparent;
            Cursor = Cursors.IBeam;
        }

        protected override void OnClick(EventArgs e) { _box.Focus(); base.OnClick(e); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_box == null) return;
            int esq = Icone != null ? Ui.S(34) : Ui.S(12);
            _box.SetBounds(esq, (Height - _box.PreferredHeight) / 2 + 1, Math.Max(Ui.S(10), Width - esq - Ui.S(12)), _box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            _box.BackColor = Ui.SurfaceAlt;
            _box.ForeColor = Ui.Text;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            bool foco = _box.Focused;
            using (GraphicsPath p = Ui.RoundRect(r, Ui.RaioControle + Ui.S(2)))
            using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt))
            using (Pen pen = new Pen(foco ? Ui.Accent : Ui.Border, foco ? 2f : 1f))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            if (Icone != null)
                TextRenderer.DrawText(g, Icone, Ui.FontGlyph10, new Rectangle(Ui.S(4), 0, Ui.S(28), Height), Ui.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        /// <summary>
        /// O texto de exemplo e o do proprio controle de edicao (EM_SETCUEBANNER):
        /// pintado por baixo, como faz a caixa de busca, o TextBox opaco o cobria.
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _box.HandleCreated += delegate { Dica(); };
            if (_box.IsHandleCreated) Dica();
        }

        private void Dica()
        {
            SendMessage(_box.Handle, 0x1501, (IntPtr)1, Placeholder ?? "");
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }

    // =====================================================================
    // Aparelhos
    // =====================================================================

    internal sealed class ComandoEventArgs : EventArgs
    {
        public readonly string Puid;
        public readonly Dictionary<string, string> Propriedades;
        public ComandoEventArgs(string puid, Dictionary<string, string> p) { Puid = puid; Propriedades = p; }
    }

    /// <summary>Base dos cartoes: cabecalho com icone, nome, modelo e o selo de conexao.</summary>
    internal abstract class CartaoDeAparelho : Card
    {
        public event EventHandler<ComandoEventArgs> Comando;
        public event EventHandler MudouAltura;

        protected Aparelho A;
        protected bool Carregando;
        private bool _enviando;

        public string Puid { get { return A != null ? A.Puid : null; } }
        internal Aparelho Aparelho { get { return A; } }
        public abstract int Tipo { get; }
        protected abstract string Icone { get; }

        protected static readonly int Cabeca = Ui.S(64);
        protected static readonly int M = Ui.S(18);

        protected CartaoDeAparelho()
        {
            Font = Ui.FontBase;
        }

        public void Mostrar(Aparelho a)
        {
            A = a;
            Carregando = true;
            try { Preencher(); }
            finally { Carregando = false; }
            Invalidate();
        }

        protected abstract void Preencher();

        /// <summary>Remonta com o mesmo aparelho, depois de mudar uma preferencia do cartao.</summary>
        public void Atualizar() { if (A != null) Mostrar(A); }
        protected virtual void DesenharIcone(Graphics g, Rectangle r, Color cor) { }
        public abstract int Arranjar(int w);

        public void Enviando(bool sim) { _enviando = sim; Invalidate(new Rectangle(0, 0, Width, Cabeca)); }

        /// <summary>Manda o comando e ja mostra o valor novo, sem esperar a nuvem confirmar.</summary>
        protected void Mandar(string chave, int valor)
        {
            if (Carregando || A == null) return;
            Dictionary<string, string> p = new Dictionary<string, string>();
            p[chave] = valor.ToString();
            A.Estado[chave] = valor.ToString();
            Mostrar(A);
            if (Comando != null) Comando(this, new ComandoEventArgs(A.Puid, p));
        }

        /// <summary>Varias propriedades num comando so (as cenas), com a previa otimista.</summary>
        protected void MandarVarios(Dictionary<string, int> valores)
        {
            if (Carregando || A == null || valores == null || valores.Count == 0) return;
            Dictionary<string, string> p = new Dictionary<string, string>();
            foreach (KeyValuePair<string, int> kv in valores)
            {
                p[kv.Key] = kv.Value.ToString();
                A.Estado[kv.Key] = kv.Value.ToString();
            }
            Mostrar(A);
            if (Comando != null) Comando(this, new ComandoEventArgs(A.Puid, p));
        }

        protected void AvisarAltura() { if (MudouAltura != null) MudouAltura(this, EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (A == null) return;
            Graphics g = e.Graphics;
            Ui.Smooth(g);

            Rectangle ic = new Rectangle(M, Ui.S(16), Ui.S(36), Ui.S(36));
            using (GraphicsPath p = Ui.RoundRect(ic, Ui.S(10)))
            using (SolidBrush b = new SolidBrush(Ui.AccentSoft))
                g.FillPath(b, p);
            if (Icone != null)
                TextRenderer.DrawText(g, Icone, Ui.FontGlyph11, ic, Ui.AccentText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            else DesenharIcone(g, Rectangle.Inflate(ic, -Ui.S(9), -Ui.S(8)), Ui.AccentText);

            string selo = _enviando ? T.CasaEnviando : (A.Online ? T.CasaOnline : T.CasaOffline);
            Color cor = _enviando ? Ui.Accent : (A.Online ? Color.FromArgb(0x2E, 0xA0, 0x43) : Ui.Muted);
            Size ss = TextRenderer.MeasureText(selo, Ui.FontSmall);
            Rectangle sr = new Rectangle(Width - M - ss.Width - Ui.S(22), Ui.S(22), ss.Width + Ui.S(22), Ui.S(24));
            using (GraphicsPath p = Ui.RoundRect(sr, sr.Height / 2))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(Ui.Dark ? 50 : 28, cor)))
                g.FillPath(b, p);
            using (SolidBrush b = new SolidBrush(cor))
                g.FillEllipse(b, sr.X + Ui.S(9), sr.Y + sr.Height / 2 - Ui.S(3), Ui.S(6), Ui.S(6));
            TextRenderer.DrawText(g, selo, Ui.FontSmall, new Rectangle(sr.X + Ui.S(18), sr.Y, sr.Width - Ui.S(20), sr.Height), cor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            int tx = ic.Right + Ui.S(12);
            Rectangle nome = new Rectangle(tx, Ui.S(14), sr.X - tx - Ui.S(8), Ui.S(24));
            TextRenderer.DrawText(g, A.Nome, Ui.FontSection, nome, Ui.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            List<string> sub = new List<string>();
            string modelo = A.ModeloLegivel;
            if (!string.IsNullOrEmpty(modelo) && modelo != A.Nome) sub.Add(modelo);
            if (!string.IsNullOrEmpty(A.Comodo)) sub.Add(A.Comodo);
            if (sub.Count == 0) sub.Add(A.TipoNome);
            TextRenderer.DrawText(g, string.Join("  ·  ", sub.ToArray()), Ui.FontSmall,
                new Rectangle(tx, Ui.S(38), nome.Width, Ui.S(18)), Ui.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            using (Pen pen = new Pen(Ui.Border))
                g.DrawLine(pen, 1, Cabeca, Width - 2, Cabeca);
        }

        protected Label Rotulo(string texto, Font f, Color cor)
        {
            Label l = new Label();
            l.Text = texto; l.Font = f; l.ForeColor = cor;
            l.AutoSize = false; l.BackColor = Color.Transparent;
            Controls.Add(l);
            return l;
        }

        protected Segmented Opcoes(string[] rotulos, int[] valores, string chave)
        {
            Segmented s = new Segmented();
            s.SetItems(rotulos);
            s.Tag = valores;
            s.SelectedIndexChanged += delegate { Mandar(chave, valores[s.SelectedIndex]); };
            Controls.Add(s);
            return s;
        }

        protected static void Selecionar(Segmented s, int valor)
        {
            int[] valores = (int[])s.Tag;
            int i = Array.IndexOf(valores, valor);
            if (i >= 0) s.SelectedIndex = i;
        }
    }

    /// <summary>Qualquer outro aparelho da conta: so a lista do que ele publica.</summary>
    internal sealed class CartaoGenerico : CartaoDeAparelho
    {
        private readonly Label _lista;
        public override int Tipo { get { return 2; } }
        protected override string Icone { get { return "\uE950"; } }

        public CartaoGenerico() { _lista = Rotulo("", Ui.FontSmall, Ui.Muted); }

        protected override void Preencher()
        {
            List<string> keys = new List<string>(A.Estado.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            List<string> linhas = new List<string>();
            foreach (string k in keys) linhas.Add(k + " = " + A.Estado[k]);
            _lista.Text = linhas.Count > 0 ? string.Join("\n", linhas.ToArray()) : T.CasaSemEstado;
        }

        public override int Arranjar(int w)
        {
            Size s = TextRenderer.MeasureText(_lista.Text, _lista.Font, new Size(w - 2 * M, 0), TextFormatFlags.WordBreak);
            _lista.SetBounds(M, Cabeca + Ui.S(14), w - 2 * M, s.Height + Ui.S(2));
            return _lista.Bottom + Ui.S(16);
        }
    }

    /// <summary>Botao redondo com glifo; Grande + Aceso e o liga/desliga.</summary>
    internal sealed class BotaoRedondo : Control
    {
        public bool Grande, Aceso;
        private readonly string _glifo;
        private Transicao _t;
        private Transicao TH { get { return _t ?? (_t = new Transicao(this)); } }

        public BotaoRedondo(string glifo)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            _glifo = glifo;
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        protected override void OnMouseEnter(EventArgs e) { TH.Ir(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { TH.Ir(0f); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
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
            using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            Color fundo, frente, borda;
            if (!Enabled) { fundo = Ui.SurfaceAlt; frente = Ui.Faint; borda = Ui.Border; }
            else if (Aceso)
            {
                fundo = Ui.Misturar(Ui.Accent, Ui.AccentHover, TH.Valor);
                frente = Color.White; borda = fundo;
            }
            else
            {
                fundo = Ui.Misturar(Ui.SurfaceAlt, Ui.Hover, TH.Valor);
                frente = Grande ? Ui.Muted : Ui.Text; borda = Ui.Border;
            }
            using (SolidBrush b = new SolidBrush(fundo)) g.FillEllipse(b, r);
            using (Pen p = new Pen(borda)) g.DrawEllipse(p, r);
            if (Focused && ShowFocusCues)
                using (Pen p = new Pen(Ui.Accent, 2f)) g.DrawEllipse(p, Rectangle.Inflate(r, -2, -2));
            using (GraphicsPath gp = Glifo(_glifo, Grande ? Ui.FontGlyph18 : Ui.FontGlyph11, new RectangleF(0, 0, Width - 1, Height - 1)))
            using (SolidBrush b = new SolidBrush(frente))
                g.FillPath(b, gp);
        }

        /// <summary>
        /// O glifo como contorno, centrado pelo DESENHO e nao pela caixa da fonte:
        /// a caixa da Segoe inclui folga de linha, e o simbolo centrado por ela
        /// saia deslocado dentro do circulo.
        /// </summary>
        internal static GraphicsPath Glifo(string glifo, Font f, RectangleF area)
        {
            GraphicsPath gp = new GraphicsPath();
            float em = f.SizeInPoints * 96f / 72f;
            gp.AddString(glifo, f.FontFamily, (int)f.Style, em, PointF.Empty, StringFormat.GenericTypographic);
            RectangleF b = gp.GetBounds();
            using (Matrix m = new Matrix())
            {
                m.Translate(area.X + (area.Width - b.Width) / 2f - b.X, area.Y + (area.Height - b.Height) / 2f - b.Y);
                gp.Transform(m);
            }
            return gp;
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acessivel(this); }

        private sealed class Acessivel : ControlAccessibleObject
        {
            private readonly BotaoRedondo _dono;
            public Acessivel(BotaoRedondo dono) : base(dono) { _dono = dono; }
            public override string Name { get { return _dono.AccessibleName; } }
            public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
            public override void DoDefaultAction() { if (_dono.Enabled) _dono.OnClick(EventArgs.Empty); }
        }
    }

    /// <summary>Barra fina de progresso do ciclo.</summary>
    internal sealed class BarraDeProgresso : Control
    {
        private float _v;
        public float Valor { get { return _v; } set { _v = Math.Max(0f, Math.Min(1f, value)); Invalidate(); } }

        public BarraDeProgresso()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(r, r.Height / 2))
            using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt)) g.FillPath(b, p);
            int w = (int)Math.Round((Width - 1) * _v);
            if (w >= Height)
                using (GraphicsPath p = Ui.RoundRect(new Rectangle(0, 0, w, Height - 1), r.Height / 2))
                using (SolidBrush b = new SolidBrush(Ui.Accent)) g.FillPath(b, p);
        }
    }

    // =====================================================================
    // Textos
    // =====================================================================

    public static partial class T
    {
        public static string NavCasa { get { return P("Casa", "Home"); } }
        public static string CasaAtualizar { get { return P("Atualizar", "Refresh"); } }
        public static string CasaSair { get { return P("Sair da conta", "Sign out"); } }
        public static string CasaCarregando { get { return P("Buscando os aparelhos na nuvem...", "Loading appliances from the cloud..."); } }
        public static string CasaAtualizando { get { return P("atualizando", "refreshing"); } }
        public static string CasaAtualizadoAs { get { return P("Atualizado às {0}  ·  ConnectLife", "Updated at {0}  ·  ConnectLife"); } }
        public static string CasaSemAparelhos { get { return P("Nenhum aparelho nesta conta ConnectLife.", "No appliances in this ConnectLife account."); } }

        public static string CasaLoginTitulo { get { return P("Conecte sua casa", "Connect your home"); } }
        public static string CasaLoginSub { get { return P("Controle o ar-condicionado e a lava e seca Hisense daqui, com a mesma conta do app ConnectLife.", "Control your Hisense air conditioner and washer dryer from here, with the same account as the ConnectLife app."); } }
        public static string CasaFaixaSub { get { return P("Ar-condicionado e lava e seca", "Air conditioner and washer"); } }
        public static string CasaPasso1 { get { return P("Clique em Entrar com Google. Na página da ConnectLife, role até SIGN IN BY GOOGLE e escolha sua conta.", "Click Sign in with Google. On the ConnectLife page, scroll to SIGN IN BY GOOGLE and pick your account."); } }
        public static string CasaPasso2 { get { return P("No fim, o navegador abre uma página do Home Assistant. É esperado: copie o endereço da barra (Ctrl+L, Ctrl+C).", "At the end the browser opens a Home Assistant page. That is expected: copy the address bar (Ctrl+L, Ctrl+C)."); } }
        public static string CasaPasso3 { get { return P("O Mhiagos percebe o endereço copiado e conecta sozinho. Se não, cole abaixo.", "Mhiagos notices the copied address and connects by itself. If not, paste it below."); } }
        public static string CasaEntrarGoogle { get { return P("Entrar com Google", "Sign in with Google"); } }
        public static string CasaColeAqui { get { return P("Endereço da página final (my.home-assistant.io...)", "Final page address (my.home-assistant.io...)"); } }
        public static string CasaColar { get { return P("Colar", "Paste"); } }
        public static string CasaConectar { get { return P("Conectar", "Connect"); } }
        public static string CasaConectandoCurto { get { return P("Conectando...", "Connecting..."); } }
        public static string CasaConectando { get { return P("Conectando à ConnectLife...", "Connecting to ConnectLife..."); } }
        public static string CasaEsperando { get { return P("Esperando você entrar no navegador e copiar o endereço da página final.", "Waiting for you to sign in and copy the final page address."); } }
        public static string CasaNota { get { return P("O acesso fica guardado no Gerenciador de Credenciais do Windows. A página final é do Home Assistant porque foi para ele que a Hisense publicou esse login.", "Access is kept in Windows Credential Manager. The final page belongs to Home Assistant because that is who Hisense published this login for."); } }
        public static string CasaSemNavegador { get { return P("Não consegui abrir o navegador.", "Could not open the browser."); } }

        public static string CasaColeVazio { get { return P("Cole o endereço da página para onde o navegador foi levado.", "Paste the address of the page the browser ended on."); } }
        public static string CasaSemCodigo { get { return P("Esse endereço não tem o código de login. Copie o da página final, depois de entrar com o Google.", "That address has no login code. Copy the final page's address, after signing in with Google."); } }
        public static string CasaEstadoAntigo { get { return P("Esse endereço é de uma tentativa anterior. Clique em Entrar com Google de novo.", "That address is from an earlier attempt. Click Sign in with Google again."); } }
        public static string CasaLoginRecusado { get { return P("A ConnectLife recusou o login: o código já foi usado ou expirou. Entre com o Google de novo.", "ConnectLife refused the login: the code was used or expired. Sign in with Google again."); } }
        public static string CasaSessaoExpirou { get { return P("O acesso à ConnectLife expirou. Entre de novo.", "ConnectLife access expired. Please sign in again."); } }
        public static string CasaFalhaTemporaria { get { return P("A nuvem da ConnectLife não respondeu direito. Tento de novo em instantes.", "The ConnectLife cloud did not answer properly. Retrying shortly."); } }
        public static string CasaSemInternet { get { return P("Sem conexão com a nuvem da ConnectLife.", "No connection to the ConnectLife cloud."); } }
        public static string CasaRespostaEstranha { get { return P("Resposta inesperada da nuvem da ConnectLife.", "Unexpected answer from the ConnectLife cloud."); } }
        public static string CasaErroDaNuvem { get { return P("A ConnectLife recusou: {0} {1}", "ConnectLife refused: {0} {1}"); } }
        public static string CasaFalhaInesperada { get { return P("Algo deu errado. Detalhes no log do Mhiagos.", "Something went wrong. Details in the Mhiagos log."); } }

        public static string CasaOnline { get { return P("Conectado", "Online"); } }
        public static string CasaOffline { get { return P("Desconectado", "Offline"); } }
        public static string CasaOfflineLonga { get { return P("Sem conexão com a nuvem", "Not connected to the cloud"); } }
        public static string CasaEnviando { get { return P("Enviando", "Sending"); } }
        public static string CasaLigado { get { return P("Ligado", "On"); } }
        public static string CasaDesligado { get { return P("Desligado", "Off"); } }
        public static string CasaAlvo { get { return P("Temperatura desejada", "Target temperature"); } }
        public static string CasaAmbiente { get { return P("no ambiente", "in the room"); } }
        public static string CasaModo { get { return P("Modo", "Mode"); } }
        public static string CasaVentilador { get { return P("Ventilador", "Fan"); } }
        public static string CasaFuncoes { get { return P("Funções", "Features"); } }
        public static string CasaAuto { get { return P("Automático", "Auto"); } }
        public static string CasaFrio { get { return P("Frio", "Cool"); } }
        public static string CasaSeco { get { return P("Desumidificar", "Dry"); } }
        public static string CasaVentilar { get { return P("Ventilar", "Fan"); } }
        public static string CasaCalor { get { return P("Calor", "Heat"); } }
        public static string CasaUmidade { get { return P("umidade {0}%", "humidity {0}%"); } }
        public static string CasaFiltro { get { return P("limpar o filtro", "clean the filter"); } }

        public static string CasaFuncao(string chave)
        {
            switch (chave)
            {
                case "t_eco": return P("Econômico", "ECO");
                case "t_super": return P("Turbo", "Turbo");
                case "t_fan_mute": return P("Silencioso", "Quiet");
                case "t_sleep": return P("Modo noturno", "Sleep");
                case "t_up_down": return P("Oscilar na vertical", "Vertical swing");
                case "t_left_right": return P("Oscilar na horizontal", "Horizontal swing");
                case "t_8heat": return P("Antifrio 8 °C", "8 °C frost guard");
                case "t_purify": return P("Ionizador", "Ionizer");
                case "t_beep": return P("Bipe", "Beep");
                default: return chave;
            }
        }

        public static string CasaEstadoLavadora(int st)
        {
            switch (st)
            {
                case 0: return P("Desligada", "Off");
                case 1: return P("Em espera", "Standby");
                case 2: return P("Lavando", "Running");
                case 3: return P("Pausada", "Paused");
                case 4: return P("Ciclo concluído", "Cycle finished");
                case 5: return P("Com erro: veja o painel", "Error: check the panel");
                default: return P("Pronta", "Ready");
            }
        }

        public static string CasaFase(int f)
        {
            switch (f)
            {
                case 1: return P("lavagem", "washing");
                case 2: return P("enxágue", "rinsing");
                case 3: return P("centrifugação", "spinning");
                case 4: return P("secagem", "drying");
                default: return P("em andamento", "in progress");
            }
        }

        public static string CasaRestante { get { return P("{0} h {1:00} min restantes", "{0} h {1:00} min left"); } }
        public static string CasaRestanteMin { get { return P("{0} min restantes", "{0} min left"); } }
        public static string CasaPrograma { get { return P("Programa", "Program"); } }
        public static string CasaTemperatura { get { return P("Temperatura", "Temperature"); } }
        public static string CasaCentrifugacao { get { return P("Centrifugação", "Spin"); } }
        public static string CasaPorta { get { return P("Porta", "Door"); } }
        public static string CasaAberta { get { return P("aberta", "open"); } }
        public static string CasaFechada { get { return P("fechada", "closed"); } }
        public static string CasaTrava { get { return P("Trava de segurança", "Child lock"); } }
        public static string CasaTodas { get { return P("Todas as propriedades", "All properties"); } }
        public static string CasaMenos { get { return P("Esconder propriedades", "Hide properties"); } }
        /// <summary>Tipo do aparelho em portugues, quando o nome do modelo vem em chines.</summary>
        public static string CasaTipo(string tipo, string daNuvem)
        {
            switch (tipo)
            {
                case "008": return P("Ar-condicionado de janela", "Window air conditioner");
                case "009": return P("Ar-condicionado split", "Split air conditioner");
                case "003": case "025": case "027": return P("Máquina de lavar", "Washing machine");
                case "004": return P("Secadora", "Tumble dryer");
                default: return string.IsNullOrEmpty(daNuvem) ? P("Aparelho", "Appliance") : daNuvem;
            }
        }

        public static string CasaSemEstado { get { return P("A nuvem não mandou o estado deste aparelho.", "The cloud sent no state for this appliance."); } }
    }
}
