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
        private readonly Timer _relogio;
        private WebView2 _app, _casa;
        private CoreWebView2Environment _ambiente;
        private bool _pronta, _casaIniciada;
        private string _casaOrigem = "", _casaEndereco = "";
        private Rectangle _casaArea = Rectangle.Empty;
        private bool _casaVisivel;

        public JanelaDoApp(IAppNovo app)
        {
            _ponte = new PonteDoApp(app);
            _ponte.Enviar = EnviarDeFora;
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
            Controls.Add(_aviso);

            _relogio = new Timer();
            _relogio.Interval = 1000;
            _relogio.Tick += delegate { Publicar(); };

            try { Iniciar(); }
            catch (Exception ex) { Falhou(ex); }
        }

        // ---------------- recursos embutidos ----------------

        /// <summary>O nome do recurso de um caminho da origem do app; nulo se o caminho nao e um arquivo da interface.</summary>
        internal static string Recurso(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
            if (!string.Equals(u.GetLeftPart(UriPartial.Authority), OrigemDoApp, StringComparison.OrdinalIgnoreCase)) return null;
            string caminho = u.AbsolutePath.TrimStart('/');
            if (caminho.Length == 0) caminho = "app.html";
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
            _aviso.Text = T.CasaNovaSemWebView;
            _aviso.Visible = true;
            if (_app != null) _app.Visible = false;
            if (_casa != null) _casa.Visible = false;
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
                    _app.Visible = true; _aviso.Visible = false;
                    _pronta = true;
                    _relogio.Start();
                };
                c.Navigate(OrigemDoApp + "/app.html");
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

        // ---------------- ponte ----------------

        private void AoReceber(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // So a pagina do proprio app fala com a ponte.
                if (!JanelaWeb.MesmaOrigem(e.Source, OrigemDoApp)) return;
                Dictionary<string, object> pedido = Json.Ler(e.WebMessageAsJson) as Dictionary<string, object>;
                if (pedido == null) return;
                if (Json.Texto(pedido, "m") == "casa.area")
                {
                    object area;
                    pedido.TryGetValue("a", out area);
                    AreaDaCasa(area as Dictionary<string, object>);
                    return;
                }
                Dictionary<string, object> resposta = _ponte.Atender(pedido);
                if (resposta != null) Postar(resposta);
                if (Json.Texto(pedido, "m") == "iniciar") Publicar();
            }
            catch (Exception ex) { Log.Error("interface nova: mensagem da pagina", ex); }
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

        private void Publicar()
        {
            if (!_pronta || IsDisposed || WindowState == FormWindowState.Minimized) return;
            try { Postar(_ponte.Estado()); }
            catch (Exception ex) { Log.Error("interface nova: estado", ex); }
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
                if (_casa != null) _casa.Visible = false;
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
                c.Navigate(alvo);
                PosicionarCasa();
            }
            catch (Exception ex) { Log.Error("interface nova: Casa", ex); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _relogio.Stop();
            _relogio.Dispose();
            _ponte.Enviar = null;
            try { if (_casa != null) _casa.Dispose(); } catch { }
            try { if (_app != null) _app.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }

    public static partial class T
    {
        public static string NovoAbrindo { get { return P("Abrindo…", "Opening…"); } }
        public static string TrayAppNovo { get { return P("Abrir (interface nova)", "Open (new interface)"); } }
    }
}
