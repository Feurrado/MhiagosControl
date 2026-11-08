using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Secao "Servidor da casa" da aba Ajustes: endereco e chave do servidor
    /// (ServidorCasa), teste, sincronizacao manual e a pagina dele no navegador.
    /// </summary>
    internal sealed class SecaoDoServidor : IConteudoDeSecao
    {
        public readonly Secao Secao;
        private readonly Label _estado, _nota, _lExterno, _lInterno, _lChave;
        private readonly CampoDeTexto _externo, _interno, _chave;
        private readonly FlatBtn _btSalvar, _btSinc, _btPagina, _btEsquecer;
        private bool _testando;

        /// <summary>A secao mudou de altura.</summary>
        public Action Rearranjar;

        public SecaoDoServidor()
        {
            Secao = new Secao(T.ServidorSecao);
            Secao.Tag = this;
            _estado = Rotulo(Ui.FontMed, Ui.Text);
            _lExterno = Rotulo(Ui.FontSmall, Ui.Text);
            _lExterno.Text = T.ServidorEnderecoExterno;
            _externo = Campo();
            _externo.Texto = ServidorCasa.EnderecoExterno;
            _externo.Placeholder = "https://home01.suarede.ts.net";
            _lInterno = Rotulo(Ui.FontSmall, Ui.Text);
            _lInterno.Text = T.ServidorEnderecoInterno;
            _interno = Campo();
            _interno.Texto = ServidorCasa.Endereco;
            _interno.Placeholder = "192.168.1.100:8765";
            _lChave = Rotulo(Ui.FontSmall, Ui.Text);
            _lChave.Text = T.ServidorChave;
            _chave = Campo();
            _chave.Senha = true;
            _btSalvar = Botao(T.ServidorSalvar, Salvar);
            _btSinc = Botao(T.ServidorSincronizar, delegate { ServidorCasa.Sincronizar(); });
            _btPagina = Botao(T.ServidorPagina, AbrirPagina);
            _btEsquecer = Botao(T.ServidorEsquecer, delegate { ServidorCasa.Esquecer(); _externo.Texto = ""; _interno.Texto = ""; _chave.Texto = ""; Preencher(); });
            _btEsquecer.Danger = true;
            _nota = Rotulo(Ui.FontSmall, Ui.Muted);
            _nota.Text = T.ServidorNota;

            EventHandler mudou = delegate
            {
                MethodInvoker m = delegate { if (!Secao.IsDisposed) { Preencher(); if (Rearranjar != null) Rearranjar(); } };
                try
                {
                    if (Secao.IsHandleCreated && !Secao.IsDisposed) Secao.BeginInvoke(m);
                }
                catch (Exception ex) { Log.Error("casa: estado do servidor na interface", ex); }
            };
            ServidorCasa.Mudou += mudou;
            Secao.Disposed += delegate { ServidorCasa.Mudou -= mudou; };
            Preencher();
        }

        private Label Rotulo(Font f, Color c)
        {
            Label l = new Label();
            l.Font = f; l.ForeColor = c; l.AutoSize = false; l.BackColor = Color.Transparent;
            Secao.Controls.Add(l);
            return l;
        }

        private CampoDeTexto Campo()
        {
            CampoDeTexto c = new CampoDeTexto();
            c.Icone = null;
            c.Confirmar += delegate { Salvar(); };
            Secao.Controls.Add(c);
            return c;
        }

        private FlatBtn Botao(string texto, Action clique)
        {
            FlatBtn b = new FlatBtn();
            b.Text = texto;
            b.Click += delegate { clique(); };
            Secao.Controls.Add(b);
            return b;
        }

        private void Preencher()
        {
            if (_testando) return;
            string e = ServidorCasa.Estado;
            _estado.Text = !ServidorCasa.Configurado ? T.ServidorNaoConfigurado : e.Length > 0 ? e : T.ServidorConfigurado;
            _btSinc.Enabled = _btPagina.Enabled = _btEsquecer.Enabled = ServidorCasa.Configurado;
        }

        /// <summary>Testa no fundo; so grava se o servidor aceitou endereco e chave.</summary>
        private void Salvar()
        {
            if (_testando) return;
            string externo = _externo.Texto.Trim(), interno = _interno.Texto.Trim(), chave = _chave.Texto.Trim();
            if (externo.Length == 0 && interno.Length == 0) return;
            _testando = true;
            _btSalvar.Enabled = false;
            _estado.Text = T.ServidorTestando;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string erro;
                try { erro = ServidorCasa.TestarEnderecos(externo, interno, chave); }
                catch (Exception ex) { erro = ex.Message; }
                MethodInvoker volta = delegate
                {
                    try
                    {
                        _testando = false;
                        if (Secao.IsDisposed) return;
                        _btSalvar.Enabled = true;
                        if (erro != null) { _estado.Text = T.ServidorFalhou(erro); return; }
                        _chave.Texto = "";
                        ServidorCasa.Configurar(externo, interno, chave);
                        Preencher();
                    }
                    catch (Exception ex) { Log.Error("casa: salvar servidor", ex); }
                };
                try
                {
                    if (Secao.IsHandleCreated && !Secao.IsDisposed) Secao.BeginInvoke(volta);
                    else Motion.NaInterface(volta);
                }
                catch (Exception ex) { Log.Error("casa: voltar a interface", ex); }
            });
        }

        private void AbrirPagina()
        {
            string e = ServidorCasa.EnderecoDaPagina;
            if (e.Length == 0) return;
            if (e.IndexOf("://", StringComparison.Ordinal) < 0) e = "http://" + e;
            // So http(s), e sem a elevacao do app: o endereco vem do casa.ini.
            Navegador.Abrir(e);
        }

        public int Arranjar(Secao s, int w)
        {
            Preencher();
            int m = Secao.Margem, iw = w - 2 * m, y = Secao.Topo;
            _estado.SetBounds(m, y, iw, Ui.S(22));
            y += Ui.S(28);
            int cw = (iw - Ui.S(12)) / 2;
            _lExterno.SetBounds(m, y, cw, Ui.S(18));
            _lInterno.SetBounds(m + cw + Ui.S(12), y, cw, Ui.S(18));
            _externo.SetBounds(m, y + Ui.S(20), cw, Ui.S(34));
            _interno.SetBounds(m + cw + Ui.S(12), y + Ui.S(20), cw, Ui.S(34));
            y += Ui.S(62);
            _lChave.SetBounds(m, y, cw, Ui.S(18));
            _chave.SetBounds(m, y + Ui.S(20), cw, Ui.S(34));
            y += Ui.S(62);
            int x = m;
            foreach (FlatBtn b in new[] { _btSalvar, _btSinc, _btPagina, _btEsquecer })
            {
                int bw = TextRenderer.MeasureText(b.Text, b.Font).Width + Ui.S(28);
                if (x > m && x + bw > m + iw) { x = m; y += Ui.S(38); }
                b.SetBounds(x, y, bw, Ui.S(32));
                x += bw + Ui.S(8);
            }
            y += Ui.S(42);
            Size t = TextRenderer.MeasureText(_nota.Text, _nota.Font, new Size(iw, 0), TextFormatFlags.WordBreak);
            _nota.SetBounds(m, y, iw, t.Height + Ui.S(2));
            return _nota.Bottom - Secao.Topo;
        }
    }
}
