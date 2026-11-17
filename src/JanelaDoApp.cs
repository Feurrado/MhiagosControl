using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MhiagosControl
{
    /// <summary>
    /// A janela nova do app: todas as paginas em HTML dentro do WebView2.
    ///
    /// Duas vistas na mesma janela. A do app carrega as paginas embutidas no
    /// executavel (recursos MhiagosControl.ui.*), servidas numa origem propria
    /// que nao existe na rede; ela fala com o app pela PonteDoApp. A da Casa
    /// mostra a pagina do servidor da casa por cima da area de conteudo, na
    /// origem do servidor, e nao tem acesso a ponte.
    ///
    /// Nenhuma das duas navega para fora da propria origem: outro endereco abre
    /// no navegador do sistema.
    /// </summary>
    internal sealed class JanelaDoApp : Form
    {
        internal const string OrigemDoApp = "https://app.mhiagos.invalid";
        private const string Prefixo = "MhiagosControl.ui.";
        /// <summary>
        /// So scripts e estilos da propria origem: nada embutido na pagina, nada de fora,
        /// nenhuma conexao de rede. A pagina so conversa com o app pela ponte.
        /// </summary>
        internal const string Csp = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
                                    "font-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

        private readonly PonteDoApp _ponte;
        private readonly Label _aviso;
        private readonly TelaDeAbertura _abertura;
        private readonly Timer _relogio, _reserva, _textoDoAviso;
        private WebView2 _app, _casa;
        private CoreWebView2Environment _ambiente;
        private bool _pronta, _casaIniciada;
        private string _casaOrigem = "", _casaEndereco = "";
        /// <summary>A aba da pagina da Casa a abrir (o cartao do tempo da Visao geral pede "clima").</summary>
        private string _casaAba = "";
        private Rectangle _casaArea = Rectangle.Empty;
        private bool _casaVisivel, _casaAVista;
        private readonly Timer _casaRecolhe;

        private static int _webView = -1;

        /// <summary>O WebView2 Runtime esta instalado? Sem ele o app abre a janela classica.</summary>
        public static bool WebViewDisponivel()
        {
            if (_webView < 0)
            {
                try { _webView = string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()) ? 0 : 1; }
                catch (Exception ex) { Log.Write("interface nova: WebView2 Runtime ausente (" + ex.GetType().Name + ")"); _webView = 0; }
            }
            return _webView == 1;
        }

        private readonly string _paginaInicial;

        /// <summary>Leva a janela ja aberta a uma pagina (aviso da bandeja, widget).</summary>
        public void MostrarPagina(string pagina)
        {
            if (string.IsNullOrEmpty(pagina) || !_pronta) return;
            Dictionary<string, object> m = new Dictionary<string, object>();
            m["ev"] = "ir";
            m["p"] = pagina;
            Postar(m);
        }

        public JanelaDoApp(IAppNovo app, string pagina)
        {
            _paginaInicial = pagina;
            _ponte = new PonteDoApp(app);
            _ponte.Enviar = EnviarDeFora;
            _ponte.Adiar = delegate(Action acao)
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(acao);
            };
            _ponte.Arquivo = EscolherArquivo;
            Text = T.AppName;
            Icon = Assets.AppIcon;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.S(1180), Ui.S(800));
            MinimumSize = new Size(Ui.S(760), Ui.S(520));
            BackColor = Ui.Dark ? Color.FromArgb(0x1C, 0x1C, 0x1F) : Color.FromArgb(0xF4, 0xF5, 0xF7);

            _aviso = new Label();
            _aviso.Dock = DockStyle.Fill;
            _aviso.TextAlign = ContentAlignment.MiddleCenter;
            _aviso.ForeColor = Ui.Dark ? Color.Gainsboro : Color.FromArgb(0x30, 0x30, 0x30);
            _aviso.Font = Ui.FontSemi;
            _aviso.Text = T.NovoAbrindo;
            _aviso.Visible = false;   // so para o erro do WebView2; a abertura normal e a TelaDeAbertura
            Controls.Add(_aviso);
            _abertura = new TelaDeAbertura();
            _abertura.Dock = DockStyle.Fill;
            _abertura.BackColor = BackColor;
            Controls.Add(_abertura);
            _abertura.BringToFront();

            _relogio = new Timer();
            _relogio.Interval = 1000;
            _relogio.Tick += delegate { Publicar(); };

            // A pagina so aparece quando ja esta desenhada com os valores (aviso
            // "pronto" da pagina), para nao mostrar os cartoes vazios da primeira
            // abertura. Ate la fica a cor da janela; o "Abrindo..." so se demorar.
            _aviso.Text = "";
            _textoDoAviso = new Timer();
            _textoDoAviso.Interval = 900;
            _textoDoAviso.Tick += delegate { _textoDoAviso.Stop(); if (!IsDisposed && _abertura.Visible) _abertura.MostrarLegenda(T.NovoAbrindo); };
            _textoDoAviso.Start();
            _casaRecolhe = new Timer();
            _casaRecolhe.Interval = 400;
            _casaRecolhe.Tick += delegate
            {
                _casaRecolhe.Stop();
                if (!IsDisposed && _casa != null && !_casaAVista) _casa.Visible = false;
            };
            _reserva = new Timer();
            _reserva.Interval = 2000;
            _reserva.Tick += delegate { MostrarPagina(); };

            try { Iniciar(); }
            catch (Exception ex) { Falhou(ex); }
        }

        // ---------------- recursos embutidos ----------------

        internal static readonly string[] ImagensDoApp = { "cooler.png", "g325.png", "prox.png", "x11.png", "bluetooth.png", "app.ico" };

        /// <summary>O nome do recurso de um caminho da origem do app; nulo se o caminho nao e um arquivo da interface.</summary>
        internal static string Recurso(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
            if (!string.Equals(u.GetLeftPart(UriPartial.Authority), OrigemDoApp, StringComparison.OrdinalIgnoreCase)) return null;
            string caminho = u.AbsolutePath.TrimStart('/');
            if (caminho.Length == 0) caminho = "app.html";
            // Imagens que o executavel ja leva (cooler, aparelhos, icone): servidas pelo nome, sem pastas.
            if (Array.IndexOf(ImagensDoApp, caminho) >= 0) return "MhiagosControl." + caminho;
            // So nomes simples: sem pastas, sem "..", sem nada que nao seja um arquivo da interface.
            foreach (char c in caminho)
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')) return null;
            if (caminho.Contains("..")) return null;
            return Prefixo + caminho;
        }

        internal static string Tipo(string recurso)
        {
            if (recurso.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) return "text/html; charset=utf-8";
            if (recurso.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) return "text/css; charset=utf-8";
            if (recurso.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) return "text/javascript; charset=utf-8";
            if (recurso.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return "image/svg+xml";
            if (recurso.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return "image/png";
            if (recurso.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) return "image/x-icon";
            return "application/octet-stream";
        }

        internal static Stream Abrir(string recurso)
        {
            return recurso == null ? null : Assembly.GetExecutingAssembly().GetManifestResourceStream(recurso);
        }

        // ---------------- WebView2 ----------------

        private void Falhou(Exception ex)
        {
            Log.Error("interface nova: WebView2", ex);
            _reserva.Stop(); _textoDoAviso.Stop();
            _aviso.Text = T.CasaNovaSemWebView;
            _abertura.Visible = false;
            _aviso.Visible = true;
            _aviso.BringToFront();
            if (_app != null) _app.Visible = false;
            if (_casa != null) _casa.Visible = false;
            _casaAVista = false;
        }

        private void Iniciar()
        {
            string pasta = Path.Combine(Paths.DataDir, "webview");
            _app = new WebView2();
            _app.Dock = DockStyle.Fill;
            _app.Visible = false;
            _app.DefaultBackgroundColor = BackColor;
            _app.CoreWebView2InitializationCompleted += AoIniciarApp;
            Controls.Add(_app);
            CoreWebView2Environment.CreateAsync(null, pasta, null).ContinueWith(delegate(Task<CoreWebView2Environment> t)
            {
                if (IsDisposed) return;
                if (t.IsFaulted || t.IsCanceled) { Falhou(t.Exception != null ? t.Exception.GetBaseException() : new Exception("cancelado")); return; }
                try { _ambiente = t.Result; _app.EnsureCoreWebView2Async(_ambiente); }
                catch (Exception ex) { Falhou(ex); }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static void Endurecer(CoreWebView2 c)
        {
            c.Settings.AreDevToolsEnabled = false;
            c.Settings.AreDefaultContextMenusEnabled = false;
            c.Settings.IsStatusBarEnabled = false;
            c.Settings.IsPasswordAutosaveEnabled = false;
            c.Settings.IsGeneralAutofillEnabled = false;
            c.Settings.IsZoomControlEnabled = false;
            try { c.Profile.PreferredColorScheme = Ui.Dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light; }
            catch (Exception ex) { Log.Write("interface nova: tema nao aplicado (" + ex.GetType().Name + ")"); }
        }

        private void AoIniciarApp(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (IsDisposed) return;
            if (!e.IsSuccess) { Falhou(e.InitializationException ?? new Exception("WebView2 nao iniciou")); return; }
            try
            {
                CoreWebView2 c = _app.CoreWebView2;
                Endurecer(c);
                c.AddWebResourceRequestedFilter(OrigemDoApp + "/*", CoreWebView2WebResourceContext.All);
                c.WebResourceRequested += Servir;
                c.WebMessageReceived += AoReceber;
                c.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs n)
                {
                    if (JanelaWeb.MesmaOrigem(n.Uri, OrigemDoApp)) return;
                    n.Cancel = true;
                    Navegador.Abrir(n.Uri);
                };
                c.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs n)
                {
                    n.Handled = true;
                    Navegador.Abrir(n.Uri);
                };
                c.NavigationCompleted += delegate(object s, CoreWebView2NavigationCompletedEventArgs n)
                {
                    if (!n.IsSuccess) { Falhou(new Exception("pagina do app nao carregou: " + n.WebErrorStatus)); return; }
                    _pronta = true;
                    _relogio.Start();
                    Publicar();
                    // A pagina avisa "pronto" depois de desenhar com os dados; se o
                    // aviso nao vier, mostra assim mesmo em 2 s.
                    _reserva.Start();
                };
                // A pagina inicial vai no fragmento; app.js so aceita nomes de pagina conhecidos.
                c.Navigate(OrigemDoApp + "/app.html" + (string.IsNullOrEmpty(_paginaInicial) ? "" : "#" + Uri.EscapeDataString(_paginaInicial)));
            }
            catch (Exception ex) { Falhou(ex); }
        }

        private void Servir(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                string recurso = Recurso(e.Request.Uri);
                Stream s = Abrir(recurso);
                if (s == null)
                {
                    e.Response = _ambiente.CreateWebResourceResponse(null, 404, "Not Found", "Content-Type: text/plain");
                    return;
                }
                e.Response = _ambiente.CreateWebResourceResponse(s, 200, "OK",
                    "Content-Type: " + Tipo(recurso) + "\r\nContent-Security-Policy: " + Csp +
                    "\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store");
            }
            catch (Exception ex) { Log.Error("interface nova: servir recurso", ex); }
        }

        private void MostrarPagina()
        {
            _reserva.Stop();
            _textoDoAviso.Stop();
            if (IsDisposed || _app == null || _app.Visible) return;
            _app.Visible = true;
            _aviso.Visible = false;
            _abertura.Visible = false;   // a pagina cobre a janela inteira: nada muda de lugar
        }

        // ---------------- ponte ----------------

        private void AoReceber(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // So a pagina do proprio app fala com a ponte.
                if (!JanelaWeb.MesmaOrigem(e.Source, OrigemDoApp)) return;
                Dictionary<string, object> pedido = Json.Ler(e.WebMessageAsJson) as Dictionary<string, object>;
                if (pedido == null) return;
                if (Json.Texto(pedido, "m") == "pronto") { MostrarPagina(); return; }
                if (Json.Texto(pedido, "m") == "janela.proporcao")
                {
                    object pa;
                    pedido.TryGetValue("a", out pa);
                    Dictionary<string, object> da = pa as Dictionary<string, object>;
                    double r;
                    if (da != null && da.ContainsKey("r") && double.TryParse(Convert.ToString(da["r"], System.Globalization.CultureInfo.InvariantCulture),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r))
                        DefinirProporcao(r);
                    return;
                }
                if (Json.Texto(pedido, "m") == "casa.area")
                {
                    object area;
                    pedido.TryGetValue("a", out area);
                    AreaDaCasa(area as Dictionary<string, object>);
                    return;
                }
                Dictionary<string, object> resposta = _ponte.Atender(pedido);
                if (resposta != null) Postar(resposta);
                // O primeiro retrato vai junto com o "iniciar": a pagina abre ja com
                // os valores, sem esperar o relogio de 1 s (antes ficava vazia).
                if (Json.Texto(pedido, "m") == "iniciar") Publicar(true);
            }
            catch (Exception ex) { Log.Error("interface nova: mensagem da pagina", ex); }
        }

        private double _proporcao = ProporcaoDaJanela.Padrao;
        private bool _dimensionada;
        private const int WM_SIZING = 0x214;

        /// <summary>
        /// A pagina diz a razao do palco da Visao geral mais a barra lateral. A janela passa a manter essa razao
        /// ao ser redimensionada; o tamanho minimo acompanha, e na primeira vez a janela ja abre nela.
        /// </summary>
        private void DefinirProporcao(double r)
        {
            if (IsDisposed) return;
            _proporcao = ProporcaoDaJanela.Limitar(r);
            int fw = Width - ClientSize.Width, fh = Height - ClientSize.Height;
            int minimoW = Ui.S(900);
            MinimumSize = new Size(fw + minimoW, fh + (int)Math.Round(minimoW / _proporcao));
            if (_dimensionada || WindowState != FormWindowState.Normal) return;
            _dimensionada = true;
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int cw = Math.Min(Ui.S(1290), Math.Min(area.Width - fw - Ui.S(24), (int)Math.Round((area.Height - fh - Ui.S(24)) * _proporcao)));
            cw = Math.Max(cw, minimoW);
            Size novo = new Size(cw + fw, (int)Math.Round(cw / _proporcao) + fh);
            Bounds = new Rectangle(area.Left + (area.Width - novo.Width) / 2, area.Top + (area.Height - novo.Height) / 2, novo.Width, novo.Height);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RetanguloNativo { public int Esq, Topo, Dir, Baixo; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SIZING && WindowState == FormWindowState.Normal && m.LParam != IntPtr.Zero)
            {
                RetanguloNativo r = (RetanguloNativo)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(RetanguloNativo));
                int[] n = ProporcaoDaJanela.Ajustar(r.Esq, r.Topo, r.Dir, r.Baixo, m.WParam.ToInt32(), Width - ClientSize.Width, Height - ClientSize.Height, _proporcao);
                r.Esq = n[0]; r.Topo = n[1]; r.Dir = n[2]; r.Baixo = n[3];
                System.Runtime.InteropServices.Marshal.StructureToPtr(r, m.LParam, false);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        private void Postar(Dictionary<string, object> mensagem)
        {
            if (IsDisposed || _app == null || _app.CoreWebView2 == null) return;
            try { _app.CoreWebView2.PostWebMessageAsJson(Json.Escrever(mensagem)); }
            catch (Exception ex) { Log.Error("interface nova: resposta para a pagina", ex); }
        }

        private void EnviarDeFora(Dictionary<string, object> mensagem)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(delegate { Postar(mensagem); }));
            }
            catch (Exception ex) { Log.Error("interface nova: resposta de fundo", ex); }
        }

        private void Publicar() { Publicar(false); }

        private void Publicar(bool mesmoAntesDePronta)
        {
            if ((!_pronta && !mesmoAntesDePronta) || IsDisposed || WindowState == FormWindowState.Minimized) return;
            try { Postar(_ponte.Estado()); }
            catch (Exception ex) { Log.Error("interface nova: estado", ex); }
        }

        /// <summary>O dialogo de arquivo do Windows, dono desta janela. Nulo se a pessoa desistiu.</summary>
        private string EscolherArquivo(bool salvar, string tipo, string sugerido)
        {
            if (IsDisposed) return null;
            bool backup = tipo == "backup", exe = tipo == "exe", txt = tipo == "txt";
            string filtro = backup ? "Mhiagos backup (*.mhiagos-backup)|*.mhiagos-backup"
                          : exe ? "Executable (*.exe)|*.exe"
                          : txt ? "Text (*.txt)|*.txt" : T.ProfileFilter;
            FileDialog d = salvar ? (FileDialog)new SaveFileDialog() : new OpenFileDialog();
            using (d)
            {
                d.Filter = filtro;
                d.Title = backup ? (salvar ? T.BackupCreate : T.BackupRestore)
                        : exe ? T.GameBrowse : txt ? T.DiagnosticTitle
                        : (salvar ? T.ExportProfile : T.ImportProfile);
                if (salvar)
                {
                    d.AddExtension = true;
                    d.DefaultExt = backup ? "mhiagos-backup" : txt ? "txt" : "ini";
                    d.FileName = sugerido ?? "";
                }
                else ((OpenFileDialog)d).CheckFileExists = true;
                return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
            }
        }

        // ---------------- a Casa por cima da area de conteudo ----------------

        private static int Int(Dictionary<string, object> d, string k)
        {
            double v;
            string s = d == null ? null : Json.Texto(d, k);
            return s != null && double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v) && v > -100000 && v < 100000 ? (int)Math.Round(v) : 0;
        }

        /// <summary>A pagina diz onde fica a area de conteudo (em pixels CSS) e se a aba Casa esta a vista.</summary>
        private void AreaDaCasa(Dictionary<string, object> a)
        {
            if (a == null) return;
            _casaVisivel = Json.Texto(a, "visivel") == "true";
            string aba = Json.Texto(a, "aba");
            if (aba == "painel" || aba == "clima" || aba == "consumo" || aba == "ajustes" || aba == "ajustes-eventos") _casaAba = aba;
            // A mesma escala do resto do app (DPI do sistema), que e a que o WebView2 usa para o pixel CSS.
            double escala = Ui.S(9600) / 9600.0;
            _casaArea = new Rectangle((int)Math.Round(Int(a, "x") * escala), (int)Math.Round(Int(a, "y") * escala),
                (int)Math.Round(Int(a, "w") * escala), (int)Math.Round(Int(a, "h") * escala));
            PosicionarCasa();
        }

        private void PosicionarCasa()
        {
            if (!_casaVisivel || _casaArea.Width < 50 || _casaArea.Height < 50)
            {
                EsconderCasa();
                return;
            }
            if (_casa == null)
            {
                _casaEndereco = ServidorCasa.EnderecoDaPagina;
                _casaOrigem = JanelaWeb.Origem(_casaEndereco);
                if (!ServidorCasa.Configurado || _casaOrigem.Length == 0 || _ambiente == null) return;
                _casa = new WebView2();
                _casa.Visible = false;
                _casa.DefaultBackgroundColor = BackColor;
                _casa.CoreWebView2InitializationCompleted += AoIniciarCasa;
                Controls.Add(_casa);
                _casa.BringToFront();
                try { _casa.EnsureCoreWebView2Async(_ambiente); }
                catch (Exception ex) { Log.Error("interface nova: Casa", ex); }
            }
            _casa.Bounds = _casaArea;
            _casa.Visible = _casaIniciada;
            _casa.BringToFront();
            if (_casaIniciada && _casaAba.Length > 0)
            {
                RoteiroDaCasa("window.irPara&&irPara(" + Json.Escrever(_casaAba) + ");");
                _casaAba = "";
            }
            if (_casaIniciada && !_casaAVista)
            {
                _casaAVista = true;
                _casaRecolhe.Stop();
                RoteiroDaCasa(JanelaWeb.ScriptMostrar);
            }
        }

        /// <summary>
        /// Tira a Casa da area sem esconder o WebView na hora: ele continua desenhando,
        /// apaga o conteudo (mhgOcultar) e so depois e escondido. Escondido direto, ele
        /// guardaria o quadro cheio e, ao voltar, mostraria esse quadro antes da entrada.
        /// </summary>
        private void EsconderCasa()
        {
            if (_casa == null || !_casaAVista) return;
            _casaAVista = false;
            if (!_casa.Visible) return;
            _casa.Location = new Point(-_casa.Width - 100, -_casa.Height - 100);
            RoteiroDaCasa(JanelaWeb.ScriptOcultar);
            _casaRecolhe.Stop();
            _casaRecolhe.Start();
        }

        private void RoteiroDaCasa(string js)
        {
            try
            {
                if (_casa == null || _casa.CoreWebView2 == null) return;
                _casa.CoreWebView2.ExecuteScriptAsync(js).ContinueWith(delegate(Task<string> t)
                {
                    if (t.Exception != null) Log.Write("interface nova: Casa, roteiro falhou (" + t.Exception.InnerException.GetType().Name + ")");
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex) { Log.Error("interface nova: Casa", ex); }
        }

        private void AoIniciarCasa(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (IsDisposed || _casa == null) return;
            if (!e.IsSuccess) { Log.Error("interface nova: Casa", e.InitializationException ?? new Exception("WebView2 nao iniciou")); return; }
            try
            {
                CoreWebView2 c = _casa.CoreWebView2;
                Endurecer(c);
                c.AddScriptToExecuteOnDocumentCreatedAsync(JanelaWeb.Script(_casaOrigem, ServidorCasa.ChaveDaPagina));
                c.AddScriptToExecuteOnDocumentCreatedAsync(JanelaWeb.ScriptOcultoAoCarregar);
                c.AddScriptToExecuteOnDocumentCreatedAsync(JanelaWeb.ScriptEmbutido(_casaOrigem));
                c.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs n)
                {
                    if (JanelaWeb.MesmaOrigem(n.Uri, _casaOrigem)) return;
                    n.Cancel = true;
                    Navegador.Abrir(n.Uri);
                };
                c.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs n)
                {
                    n.Handled = true;
                    Navegador.Abrir(n.Uri);
                };
                _casaIniciada = true;
                string alvo = _casaEndereco.IndexOf("://", StringComparison.Ordinal) < 0 ? "http://" + _casaEndereco : _casaEndereco;
                // A primeira abertura ja vai direto a aba pedida (o roteiro abaixo ainda nao acharia a pagina).
                if (_casaAba.Length > 0) { alvo += "#" + _casaAba; _casaAba = ""; }
                c.Navigate(alvo);
                PosicionarCasa();
            }
            catch (Exception ex) { Log.Error("interface nova: Casa", ex); }
        }

        /// <summary>Barra de titulo escura no tema escuro e com Mica, como a janela classica; o handle recriado perde os atributos.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.BarraDeTitulo(this);
            Mica.Aplicar(this);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _relogio.Stop();
            _relogio.Dispose();
            _reserva.Stop(); _reserva.Dispose();
            _casaRecolhe.Stop(); _casaRecolhe.Dispose();
            _textoDoAviso.Stop(); _textoDoAviso.Dispose();
            _ponte.Enviar = null;
            _ponte.Adiar = null;
            _ponte.Arquivo = null;
            try { if (_casa != null) _casa.Dispose(); } catch { }
            try { if (_app != null) _app.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }

    public static partial class T
    {
        public static string NovoAbrindo { get { return P("Abrindo…", "Opening…"); } }
        public static string TrayAppClassica { get { return P("Interface clássica…", "Classic interface…"); } }
    }
}
