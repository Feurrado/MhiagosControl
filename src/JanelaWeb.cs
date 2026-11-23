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

        /// <summary>
        /// A mesma pagina do servidor, para ir dentro da janela do app: sem a
        /// barra lateral dela (a do app ja existe) e com as tres abas (Casa,
        /// Consumo, Ajustes) em pilulas no alto. Entra como folha de estilo
        /// adotada, que a politica de conteudo do servidor nao barra, e vale em
        /// qualquer largura. Nao muda nada no servidor.
        /// </summary>
        /// <summary>
        /// O mesmo padrao de movimento da janela nova (app.css): as animacoes
        /// proprias da pagina da Casa ficam com os tempos curtos, e a cascata
        /// de 60 ms por cartao vira a da janela (abaixo).
        /// </summary>
        private const string Movimento =
            " .m3-aba { animation-duration: .12s !important; } .m3-surge { animation-duration: .2s !important; animation-delay: 0s !important; }" +
            " .m3-abre { animation-duration: .15s !important; } .m3-frente, .m3-tras { animation-duration: .2s !important; }" +
            " @keyframes mhg-entra { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: none; } }" +
            " @keyframes mhg-esmaece { from { opacity: 0; } to { opacity: 1; } }" +
            " .mhg-pag { animation: mhg-esmaece .12s linear both; } .mhg-item { animation: mhg-entra .2s cubic-bezier(.05,.7,.1,1) both; }" +
            " @media (prefers-reduced-motion: reduce) { .mhg-pag, .mhg-item { animation: none !important; } }";

        /// <summary>
        /// A aba Casa entra como as outras paginas da janela. A janela nao esconde a
        /// camada da Casa na hora: tira da area, chama mhgOcultar (apaga o conteudo) e
        /// so esconde de vez depois que esse quadro apagado foi desenhado. Assim, ao
        /// voltar, o WebView nao mostra o quadro antigo cheio antes da animacao (era o
        /// "piscar"). mhgMostrar esmaece a pagina e sobe os cartoes a vista 20 ms um do
        /// outro (no maximo 160 ms); cartao que a pagina troca durante a entrada (a
        /// Casa redesenha ao voltar a ficar visivel) entra no mesmo ponto da animacao
        /// do que ele substituiu, em vez de aparecer pronto no meio dos outros.
        /// Na primeira abertura a pagina nasce apagada (ScriptOcultoAoCarregar) e a
        /// entrada so roda quando os cartoes de verdade substituiram o esqueleto
        /// (ou depois de 2 s, se o servidor nao responder).
        /// </summary>
        private const string Entrada =
            "var an=function(e,c,d){e.classList.remove(c,'m3-surge','m3-aba','m3-abre');void e.offsetWidth;e.style.animationDelay=d||'';e.classList.add(c);" +
            "e.addEventListener('animationend',function f(v){if(v.target!==e)return;e.classList.remove(c);e.style.animationDelay='';e.removeEventListener('animationend',f);});};" +
            "var topo=function(m){var h=innerHeight,l=[];m.querySelectorAll('.cartao').forEach(function(c){if(c.parentElement&&c.parentElement.closest('.cartao'))return;" +
            "var r=c.getBoundingClientRect();if(r.width===0||r.bottom<0||r.top>h)return;l.push({c:c,y:r.top,x:r.left});});" +
            "l.sort(function(a,b){return a.y-b.y||a.x-b.x;});return l;};" +
            "var obs=null,fim=function(){if(obs){obs.disconnect();obs=null;}};" +
            "var oc=new CSSStyleSheet();oc.replaceSync('.app, main { opacity: 0 !important; }');oc.disabled=!window.mhgNasceOculto;" +
            "document.adoptedStyleSheets=document.adoptedStyleSheets.concat([oc]);var quer=true,pronta=false,t1=Date.now();" +
            "var carregou=function(){var m=document.querySelector('main');if(!m)return false;" +
            "var e=m.querySelectorAll('.esqueleto');for(var i=0;i<e.length;i++)if(e[i].getBoundingClientRect().width>0)return false;return true;};" +
            "window.mhgOcultar=function(){quer=false;fim();oc.disabled=false;};" +
            "window.mhgMostrar=function(){quer=true;if(!pronta)return;try{var oculto=!oc.disabled;oc.disabled=true;fim();" +
            "var m=document.querySelector('main');if(!oculto||!m||matchMedia('(prefers-reduced-motion: reduce)').matches)return;" +
            "an(document.querySelector('.app')||m,'mhg-pag');var t0=performance.now(),feito=new WeakSet();" +
            "topo(m).forEach(function(o,n){feito.add(o.c);an(o.c,'mhg-item',Math.min(n*20,160)+'ms');});" +
            "obs=new MutationObserver(function(){var p=performance.now()-t0;if(p>360){fim();return;}" +
            "topo(m).forEach(function(o,n){if(feito.has(o.c))return;feito.add(o.c);an(o.c,'mhg-item',Math.round(Math.min(n*20,160)-p)+'ms');});});" +
            "obs.observe(m,{childList:true,subtree:true});setTimeout(fim,400);}catch(e){}};" +
            "(function espera(){if(carregou()||Date.now()-t1>2000){pronta=true;if(quer&&document.visibilityState==='visible')mhgMostrar();return;}setTimeout(espera,40);})();";

        /// <summary>Chamados pela janela ao tirar e ao trazer de volta a camada da Casa.</summary>
        internal const string ScriptOcultar = "window.mhgOcultar&&mhgOcultar();";
        internal const string ScriptMostrar = "window.mhgMostrar&&mhgMostrar();";
        /// <summary>So na janela do app: a Casa nasce apagada e espera mhgMostrar (ver Entrada).</summary>
        internal const string ScriptOcultoAoCarregar = "window.mhgNasceOculto=1;";

        internal static string ScriptEmbutido(string origem)
        {
            // Antes de a folha da pagina definir --window o fundo ficava sem cor e o WebView2 mostrava, por um quadro, o escuro
            // padrao dele (18,18,18): o piscar da primeira entrada. A cor de reserva e a mesma do app, no tema atual.
            string fundo = Ui.Dark ? "#1C1C1F" : "#F4F5F7";
            string css = "html, body { background: var(--window, " + fundo + ") !important; } .app { flex-direction: column !important; min-height: 0 !important; } nav { width: auto !important; height: auto !important; position: relative !important; flex-direction: row !important; align-items: center !important; flex-wrap: wrap !important; align-self: flex-start !important; flex-wrap: nowrap !important; background: var(--surface-alt) !important; border: 1px solid var(--border) !important; border-radius: 999px !important; margin: 16px 16px 12px !important; padding: 3px !important; gap: 2px !important; } nav .marca, nav .perfil, nav .rodape { display: none !important; } nav .item { flex: none !important; flex-direction: row !important; justify-content: center !important; gap: 0 !important; margin: 0 !important; padding: 0 16px !important; height: 24px !important; min-width: 82px !important; font-size: 13px !important; background: transparent !important; border: 0 !important; border-radius: 999px !important; box-sizing: border-box !important; } nav .item[data-aba=\"clima\"] { display: none !important; } .app:has(#aba-clima:not(.oculto)) > nav { display: none !important; } .app:has(#aba-clima:not(.oculto)) main { padding-top: 16px !important; } nav .item svg { display: none !important; } nav .item:hover { background: var(--hover) !important; } nav .item { z-index: 1 !important; } nav.seg-nav .item.ativo { background: transparent !important; } nav .item.ativo { background: var(--accent) !important; border-color: var(--accent) !important; color: #fff !important; font-weight: 600 !important; } nav .item.ativo::before { display: none !important; } .app { width: 100% !important; max-width: 1132px !important; margin: 0 auto !important; } main { padding: 0 16px 24px !important; } .topo h1 { display: none !important; }";
            return "(function(){try{if(location.origin!==" + Json.Escrever(origem) + ")return;" +
                   "var s=new CSSStyleSheet();s.replaceSync(" + Json.Escrever(css + Movimento) + ");" +
                   "document.adoptedStyleSheets=document.adoptedStyleSheets.concat([s]);" +
                   // As abas viram pilula com o indicador que desliza (o posicionarSeg da pagina cuida de .seg-nav).
                   "document.addEventListener('DOMContentLoaded',function(){var n=document.querySelector('nav');if(n)n.classList.add('seg-nav');});" +
                   Entrada + "}catch(e){}})();";
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

        /// <summary>Barra de titulo escura no tema escuro e com Mica, como a janela classica; o handle recriado perde os atributos.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.BarraDeTitulo(this);
            Mica.Aplicar(this);
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
