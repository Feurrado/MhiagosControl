using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Aba "Ajustes" da pagina Casa: o que se configura uma vez e nao precisa
    /// ficar no painel do dia a dia. As secoes Funcoes e Preferencias continuam
    /// sendo do cartao do ar (ele atualiza os interruptores a cada leitura e
    /// manda os comandos); aqui elas so sao hospedadas. Alertas, estimativa de
    /// consumo e conta sao desta aba.
    /// </summary>
    internal sealed class AjustesDaCasa : Control
    {
        public event EventHandler Sair;
        /// <summary>Potencia ou tarifa mudou: o cartao de consumo recalcula.</summary>
        public event EventHandler ConsumoMudou;

        private CartaoDoAr _ar;
        private Secao _funcoes, _pref;
        private readonly Secao _secAlertas, _secConsumo, _secConta;
        private readonly Toggle _alFalha, _alOffline, _alQuente, _alLavadora;
        private readonly NumberBox _limite;
        private readonly CampoDeTexto _wFrio, _wEco, _wVent, _tarifa;
        private readonly Label _lwFrio, _lwEco, _lwVent, _ltarifa, _nota, _conta;
        private readonly FlatBtn _btSair;
        private readonly List<Control> _extras = new List<Control>();

        internal Secao[] Secoes
        {
            get
            {
                List<Secao> l = new List<Secao>();
                foreach (Secao s in new[] { _funcoes, _pref, _secAlertas, _secConsumo, _secConta }) if (s != null) l.Add(s);
                foreach (Control c in _extras) { Secao s = c as Secao; if (s != null) l.Add(s); }
                return l.ToArray();
            }
        }

        public AjustesDaCasa()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            PreferenciasCasa p = ServicoCasa.Pref;

            _secAlertas = NovaSecao(T.CasaAlertasTitulo);
            _alFalha = Chave(_secAlertas, T.CasaAlertaFalhaOpcao, p.AlertaFalha, delegate(bool v) { ServicoCasa.Pref.AlertaFalha = v; });
            _alOffline = Chave(_secAlertas, T.CasaAlertaOfflineOpcao, p.AlertaDesconectado, delegate(bool v) { ServicoCasa.Pref.AlertaDesconectado = v; });
            _alQuente = Chave(_secAlertas, T.CasaAlertaQuenteOpcao, p.AlertaQuente, delegate(bool v) { ServicoCasa.Pref.AlertaQuente = v; });
            _alLavadora = Chave(_secAlertas, T.CasaAlertaLavadoraFimOpcao, p.AlertaLavadoraFim, delegate(bool v) { ServicoCasa.Pref.AlertaLavadoraFim = v; });
            _limite = new NumberBox();
            _limite.Minimum = 20; _limite.Maximum = 40;
            _limite.Value = p.LimiteQuente;
            _limite.ValueChanged += delegate { ServicoCasa.Pref.LimiteQuente = _limite.Value; ServicoCasa.Pref.Salvar(); };
            _secAlertas.Controls.Add(_limite);

            _secConsumo = NovaSecao(T.CasaEstimativaTitulo);
            _lwFrio = Rotulo(_secConsumo, T.CasaPotenciaFrio);
            _wFrio = Campo(_secConsumo, p.PotenciaFrio.ToString(CultureInfo.InvariantCulture));
            _lwEco = Rotulo(_secConsumo, T.CasaPotenciaEco);
            _wEco = Campo(_secConsumo, p.PotenciaEco.ToString(CultureInfo.InvariantCulture));
            _lwVent = Rotulo(_secConsumo, T.CasaPotenciaVentilar);
            _wVent = Campo(_secConsumo, p.PotenciaVentilar.ToString(CultureInfo.InvariantCulture));
            _ltarifa = Rotulo(_secConsumo, T.CasaTarifa);
            _tarifa = Campo(_secConsumo, p.Tarifa.ToString("0.00##", CultureInfo.CurrentCulture));
            _nota = Rotulo(_secConsumo, T.CasaAjustesConsumo);
            _nota.ForeColor = Ui.Muted;

            _secConta = NovaSecao(T.CasaConta);
            _conta = Rotulo(_secConta, T.CasaContaNota);
            _conta.ForeColor = Ui.Muted;
            _btSair = new FlatBtn();
            _btSair.Text = T.CasaSair;
            _btSair.Danger = true;
            _btSair.Click += delegate { if (Sair != null) Sair(this, EventArgs.Empty); };
            _secConta.Controls.Add(_btSair);
        }

        private Secao NovaSecao(string titulo)
        {
            Secao s = new Secao(titulo);
            Controls.Add(s);
            return s;
        }

        /// <summary>Secao extra de outro dono (o retorno das cenas, por exemplo), arranjada aqui.</summary>
        internal void AcrescentarSecao(Secao s)
        {
            _extras.Add(s);
            Controls.Add(s);
        }

        private static Label Rotulo(Control pai, string t)
        {
            Label l = new Label();
            l.Text = t; l.Font = Ui.FontSmall; l.ForeColor = Ui.Text; l.AutoSize = false; l.BackColor = Color.Transparent;
            pai.Controls.Add(l);
            return l;
        }

        private CampoDeTexto Campo(Control pai, string valor)
        {
            CampoDeTexto c = new CampoDeTexto();
            c.Icone = null;
            c.Texto = valor;
            c.Confirmar += delegate { Gravar(); };
            c.Leave += delegate { Gravar(); };
            pai.Controls.Add(c);
            return c;
        }

        private static Toggle Chave(Control pai, string rotulo, bool valor, Action<bool> grava)
        {
            Toggle t = new Toggle();
            t.Label = rotulo;
            t.Checked = valor;
            t.CheckedChanged += delegate { grava(t.Checked); ServicoCasa.Pref.Salvar(); };
            pai.Controls.Add(t);
            return t;
        }

        /// <summary>Le potencia e tarifa; valor invalido volta ao anterior.</summary>
        private void Gravar()
        {
            PreferenciasCasa p = ServicoCasa.Pref;
            int w;
            if (int.TryParse(_wFrio.Texto.Trim(), out w) && w >= 0 && w <= 10000) p.PotenciaFrio = w;
            if (int.TryParse(_wEco.Texto.Trim(), out w) && w >= 0 && w <= 10000) p.PotenciaEco = w;
            if (int.TryParse(_wVent.Texto.Trim(), out w) && w >= 0 && w <= 10000) p.PotenciaVentilar = w;
            double t;
            string tt = _tarifa.Texto.Trim().Replace("R$", "").Trim();
            if (double.TryParse(tt.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out t) && t >= 0 && t < 100) p.Tarifa = t;
            p.Salvar();
            _wFrio.Texto = p.PotenciaFrio.ToString(CultureInfo.InvariantCulture);
            _wEco.Texto = p.PotenciaEco.ToString(CultureInfo.InvariantCulture);
            _wVent.Texto = p.PotenciaVentilar.ToString(CultureInfo.InvariantCulture);
            _tarifa.Texto = p.Tarifa.ToString("0.00##", CultureInfo.CurrentCulture);
            if (ConsumoMudou != null) ConsumoMudou(this, EventArgs.Empty);
        }

        /// <summary>
        /// Hospeda as secoes Funcoes e Preferencias do cartao do ar. Chamado a
        /// cada cartao novo; as do cartao anterior saem daqui.
        /// </summary>
        public void Adotar(CartaoDoAr ar)
        {
            if (ar == _ar) return;
            foreach (Secao s in new[] { _funcoes, _pref })
                if (s != null && s.Parent == this) { Controls.Remove(s); s.Dispose(); }
            _ar = ar;
            _funcoes = ar != null ? ar.SecaoFuncoes : null;
            _pref = ar != null ? ar.SecaoPreferencias : null;
            if (_funcoes != null) Controls.Add(_funcoes);
            if (_pref != null) Controls.Add(_pref);
        }

        public int Arranjar(int w)
        {
            int gap = Ui.Gap;
            bool duas = w >= Ui.S(700);
            int colW = duas ? (w - gap) / 2 : w;
            List<Secao[]> linhas = new List<Secao[]>();
            linhas.Add(new[] { _funcoes, _pref });
            linhas.Add(new[] { _secAlertas, _secConsumo });
            List<Secao> extras = new List<Secao>();
            foreach (Control c in _extras) { Secao s = c as Secao; if (s != null) extras.Add(s); }
            for (int i = 0; i < extras.Count; i += 2)
                linhas.Add(new[] { extras[i], i + 1 < extras.Count ? extras[i + 1] : null });
            linhas.Add(new[] { _secConta, (Secao)null });

            int y = 0;
            foreach (Secao[] linha in linhas)
            {
                List<Secao> vis = new List<Secao>();
                foreach (Secao s in linha) if (s != null && s.Visible) vis.Add(s);
                if (vis.Count == 0) continue;
                if (duas)
                {
                    int h = 0;
                    foreach (Secao s in vis) h = Math.Max(h, Altura(s, colW));
                    for (int i = 0; i < vis.Count; i++)
                    {
                        vis[i].SetBounds(i * (colW + gap), y, colW, h);
                        Conteudo(vis[i], colW);
                    }
                    y += h + gap;
                }
                else
                    foreach (Secao s in vis)
                    {
                        int h = Altura(s, w);
                        s.SetBounds(0, y, w, h);
                        Conteudo(s, w);
                        y += h + gap;
                    }
            }
            return Math.Max(0, y - gap);
        }

        private int Altura(Secao s, int w) { return Secao.Topo + Conteudo(s, w) + Secao.Margem; }

        private int Conteudo(Secao s, int w)
        {
            int m = Secao.Margem, iw = w - 2 * m, y = Secao.Topo;
            s.PosicionarAcao();
            if (_ar != null && (s == _funcoes || s == _pref)) return _ar.ConteudoExterno(s, w);
            if (s == _secAlertas)
            {
                Toggle[] ts = { _alFalha, _alOffline, _alQuente, _alLavadora };
                for (int i = 0; i < ts.Length; i++)
                    ts[i].SetBounds(m, y + i * Ui.S(36), iw - (ts[i] == _alQuente ? Ui.S(96) : 0), Ui.S(32));
                _limite.SetBounds(m + iw - Ui.S(88), _alQuente.Top, Ui.S(88), Ui.S(32));
                return ts.Length * Ui.S(36) - Ui.S(4);
            }
            if (s == _secConsumo)
            {
                Label[] rot = { _lwFrio, _lwEco, _lwVent, _ltarifa };
                CampoDeTexto[] cmp = { _wFrio, _wEco, _wVent, _tarifa };
                int col = 2;
                int cw = (iw - Ui.S(12)) / col;
                for (int i = 0; i < 4; i++)
                {
                    int cx = m + (i % col) * (cw + Ui.S(12)), cy = y + (i / col) * Ui.S(60);
                    rot[i].SetBounds(cx, cy, cw, Ui.S(18));
                    cmp[i].SetBounds(cx, cy + Ui.S(20), cw, Ui.S(34));
                }
                int yn = y + 2 * Ui.S(60);
                Size t = TextRenderer.MeasureText(_nota.Text, _nota.Font, new Size(iw, 0), TextFormatFlags.WordBreak);
                _nota.SetBounds(m, yn, iw, t.Height + Ui.S(2));
                return _nota.Bottom - y;
            }
            if (s == _secConta)
            {
                int bw = TextRenderer.MeasureText(_btSair.Text, _btSair.Font).Width + Ui.S(28);
                Size t = TextRenderer.MeasureText(_conta.Text, _conta.Font, new Size(iw - bw - Ui.S(12), 0), TextFormatFlags.WordBreak);
                _conta.SetBounds(m, y, iw - bw - Ui.S(12), Math.Max(Ui.S(32), t.Height + Ui.S(2)));
                _btSair.SetBounds(m + iw - bw, y, bw, Ui.S(32));
                return Math.Max(Ui.S(32), _conta.Height);
            }
            foreach (Control c in _extras)
            {
                if (c != s) continue;
                IConteudoDeSecao conteudo = s.Tag as IConteudoDeSecao;
                if (conteudo != null) return conteudo.Arranjar(s, w);
            }
            return 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Ui.Window)) e.Graphics.FillRectangle(b, ClientRectangle);
        }
    }

    /// <summary>Quem poe uma secao na aba Ajustes diz como arranjar o conteudo dela.</summary>
    internal interface IConteudoDeSecao
    {
        /// <summary>Posiciona os filhos da secao na largura dada e devolve a altura do conteudo.</summary>
        int Arranjar(Secao s, int w);
    }

    public static partial class T
    {
        public static string CasaPainel { get { return P("Painel", "Dashboard"); } }
        public static string CasaAjustes { get { return P("Ajustes", "Settings"); } }
        public static string CasaConta { get { return P("Conta ConnectLife", "ConnectLife account"); } }
        public static string CasaContaNota { get { return P("Sair apaga o acesso guardado no Gerenciador de Credenciais. Para voltar, entre de novo com o Google.", "Signing out removes the access stored in Credential Manager. To come back, sign in with Google again."); } }
        public static string CasaEstimativaTitulo { get { return P("Estimativa de consumo do ar", "AC usage estimate"); } }
    }
}
