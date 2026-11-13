using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MhiagosControl
{
    /// <summary>
    /// Primeira pagina da interface nova: a Casa desenhada em HTML dentro do
    /// WebView2, a mesma pagina que o servidor da casa serve ao celular. Uma
    /// interface so para PC, navegador e Android.
    ///
    /// A janela so navega na origem do servidor configurado: qualquer outro
    /// endereco abre no navegador do sistema (sem elevacao), e a chave de acesso
    /// so e entregue a pagina dessa origem.
    /// </summary>
    internal sealed class JanelaWeb : Form
    {
        private static JanelaWeb _aberta;
        private readonly Label _aviso;
        private WebView2 _web;
        private string _origem = "", _endereco = "";

        public static void Abrir()
        {
            if (_aberta != null && !_aberta.IsDisposed)
            {
                if (_aberta.WindowState == FormWindowState.Minimized) _aberta.WindowState = FormWindowState.Normal;
                _aberta.Activate();
                return;
            }
            _aberta = new JanelaWeb();
            _aberta.Show();
        }

        /// <summary>A origem (esquema, host e porta) de um endereco http(s); vazio se nao for um.</summary>
        internal static string Origem(string endereco)
        {
            endereco = (endereco ?? "").Trim();
            if (endereco.Length == 0) return "";
            if (endereco.IndexOf("://", StringComparison.Ordinal) < 0) endereco = "http://" + endereco;
            Uri u;
            if (!Navegador.Seguro(endereco, out u)) return "";
            return u.GetLeftPart(UriPartial.Authority);
        }

        internal static bool MesmaOrigem(string endereco, string origem)
        {
            return origem.Length > 0 && string.Equals(Origem(endereco), origem, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Roda antes de qualquer script da pagina: entrega a chave so na origem
        /// do servidor, para a pagina nao pedir de novo o que o app ja guarda.
        /// </summary>
        internal static string Script(string origem, string chave)
        {
            return "(function(){try{if(location.origin===" + Json.Escrever(origem) +
                   "){localStorage.setItem('chave'," + Json.Escrever(chave ?? "") + ");}}catch(e){}})();";
        }

        private JanelaWeb()
        {
            Text = T.CasaNovaTitulo;
            Icon = Assets.AppIcon;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.S(1120), Ui.S(780));
            MinimumSize = new Size(Ui.S(420), Ui.S(520));
            BackColor = Ui.Dark ? Color.FromArgb(0x12, 0x14, 0x18) : Color.White;

            _aviso = new Label();
            _aviso.Dock = DockStyle.Fill;
            _aviso.TextAlign = ContentAlignment.MiddleCenter;
            _aviso.ForeColor = Ui.Dark ? Color.Gainsboro : Color.FromArgb(0x30, 0x30, 0x30);
            _aviso.Font = Ui.FontSemi;
            _aviso.Text = T.CasaNovaAbrindo;
            Controls.Add(_aviso);

            _endereco = ServidorCasa.EnderecoDaPagina;
            _origem = Origem(_endereco);
            if (!ServidorCasa.Configurado || _origem.Length == 0) { _aviso.Text = T.CasaNovaSemServidor; return; }
            try { Iniciar(); }
            catch (Exception ex) { Falhou(ex); }
        }

        private void Falhou(Exception ex)
        {
            Log.Error("interface nova: WebView2", ex);
            _aviso.Text = T.CasaNovaSemWebView;
            _aviso.Visible = true;
            if (_web != null) _web.Visible = false;
        }

        private void Iniciar()
        {
            // Pasta de dados propria e gravavel: a padrao seria ao lado do exe, em Program Files.
            string pasta = Path.Combine(Paths.DataDir, "webview");
            _web = new WebView2();
            _web.Dock = DockStyle.Fill;
            _web.Visible = false;
            _web.CoreWebView2InitializationCompleted += AoIniciar;
            Controls.Add(_web);
            CoreWebView2Environment.CreateAsync(null, pasta, null).ContinueWith(delegate(Task<CoreWebView2Environment> t)
            {
                if (IsDisposed) return;
                if (t.IsFaulted || t.IsCanceled) { Falhou(t.Exception != null ? (Exception)t.Exception.GetBaseException() : new Exception("cancelado")); return; }
                try { _web.EnsureCoreWebView2Async(t.Result); }
                catch (Exception ex) { Falhou(ex); }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void AoIniciar(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (IsDisposed) return;
            if (!e.IsSuccess) { Falhou(e.InitializationException ?? new Exception("WebView2 nao iniciou")); return; }
            try
            {
                CoreWebView2 c = _web.CoreWebView2;
                c.Settings.AreDevToolsEnabled = false;
                c.Settings.AreDefaultContextMenusEnabled = false;
                c.Settings.IsStatusBarEnabled = false;
                c.Settings.IsPasswordAutosaveEnabled = false;
                try { c.Profile.PreferredColorScheme = Ui.Dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light; }
                catch (Exception ex) { Log.Write("interface nova: tema nao aplicado (" + ex.GetType().Name + ")"); }
                c.AddScriptToExecuteOnDocumentCreatedAsync(Script(_origem, ServidorCasa.ChaveDaPagina));
                c.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs n)
                {
                    if (MesmaOrigem(n.Uri, _origem)) return;
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
                    if (n.IsSuccess) { _web.Visible = true; _aviso.Visible = false; }
                    else { _aviso.Text = T.CasaNovaSemResposta; _aviso.Visible = true; _web.Visible = false; }
                };
                string alvo = _endereco.IndexOf("://", StringComparison.Ordinal) < 0 ? "http://" + _endereco : _endereco;
                c.Navigate(alvo);
            }
            catch (Exception ex) { Falhou(ex); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_aberta == this) _aberta = null;
            try { if (_web != null) _web.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }

    public static partial class T
    {
        public static string TrayCasaNova { get { return P("Casa (interface nova)", "Home (new interface)"); } }
        public static string CasaNovaTitulo { get { return P("Mhiagos Control: Casa", "Mhiagos Control: Home"); } }
        public static string CasaNovaAbrindo { get { return P("Abrindo a Casa…", "Opening Home…"); } }
        public static string CasaNovaSemServidor { get { return P("Configure o servidor da casa em Casa > Ajustes para usar a interface nova.", "Set up the home server in Home > Settings to use the new interface."); } }
        public static string CasaNovaSemWebView { get { return P("O WebView2 Runtime da Microsoft não foi encontrado ou não iniciou. Veja o log.", "Microsoft WebView2 Runtime was not found or did not start. See the log."); } }
        public static string CasaNovaSemResposta { get { return P("O servidor da casa não respondeu.", "The home server did not respond."); } }
    }
}
