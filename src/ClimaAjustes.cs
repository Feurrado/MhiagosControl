using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Secao "Clima la fora" da aba Ajustes da Casa: busca a cidade pelo nome
    /// (geocodificacao da Open-Meteo, fora da thread da interface), mostra ate
    /// seis resultados com cidade, estado e pais, e grava a escolhida.
    /// </summary>
    internal sealed class SecaoDoClima : IConteudoDeSecao
    {
        public readonly Secao Secao;
        private readonly Label _atual, _nota, _estado;
        private readonly CampoDeTexto _busca;
        private readonly FlatBtn _btBuscar;
        private readonly List<FlatBtn> _resultados = new List<FlatBtn>();
        private List<LugarDoClima> _lugares = new List<LugarDoClima>();
        private int _pedido;

        /// <summary>A secao mudou de altura (resultados apareceram ou sumiram).</summary>
        public Action Rearranjar;

        /// <summary>Testes: os lugares da ultima busca.</summary>
        internal List<LugarDoClima> Lugares { get { return _lugares; } }

        public SecaoDoClima()
        {
            Secao = new Secao(T.ClimaSecao);
            Secao.Tag = this;
            _atual = Rotulo(Ui.FontMed, Ui.Text);
            _busca = new CampoDeTexto();
            _busca.Icone = null;
            _busca.Confirmar += delegate { Buscar(); };
            Secao.Controls.Add(_busca);
            _btBuscar = new FlatBtn();
            _btBuscar.Text = T.ClimaBuscar;
            _btBuscar.Click += delegate { Buscar(); };
            Secao.Controls.Add(_btBuscar);
            _estado = Rotulo(Ui.FontSmall, Ui.Muted);
            _nota = Rotulo(Ui.FontSmall, Ui.Muted);
            _nota.Text = T.ClimaNota;
            Preencher();
        }

        private Label Rotulo(Font f, Color c)
        {
            Label l = new Label();
            l.Font = f; l.ForeColor = c; l.AutoSize = false; l.BackColor = Color.Transparent;
            Secao.Controls.Add(l);
            return l;
        }

        private void Preencher()
        {
            _atual.Text = Clima.TemCidade ? T.ClimaAtualCidade(Clima.Cidade) + "  ·  " + Clima.Texto() : T.ClimaSemCidade;
        }

        /// <summary>Busca o texto do campo; os resultados chegam depois, na thread da interface.</summary>
        internal void Buscar()
        {
            string nome = _busca.Texto;
            if (string.IsNullOrEmpty(nome) || nome.Trim().Length < 2) return;
            int pedido = ++_pedido;
            _estado.Text = T.ClimaBuscando;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<LugarDoClima> r = null;
                bool falhou = false;
                try { r = Clima.BuscarCidade(nome); }
                catch (Exception ex) { falhou = true; Log.Write("clima: busca de cidade falhou (" + ex.GetType().Name + ")"); }
                Motion.NaInterface(delegate
                {
                    if (Secao.IsDisposed || pedido != _pedido) return;
                    Mostrar(r ?? new List<LugarDoClima>(), falhou);
                });
            });
        }

        /// <summary>Poe os resultados como botoes (tambem usado pelos testes, sem rede).</summary>
        internal void Mostrar(List<LugarDoClima> lugares, bool falhou)
        {
            foreach (FlatBtn b in _resultados) { Secao.Controls.Remove(b); b.Dispose(); }
            _resultados.Clear();
            _lugares = lugares;
            _estado.Text = falhou ? T.ClimaFalhaBusca : lugares.Count == 0 ? T.ClimaNenhuma : "";
            for (int i = 0; i < lugares.Count && i < 6; i++)
            {
                LugarDoClima l = lugares[i];
                FlatBtn b = new FlatBtn();
                b.Text = l.Descricao;
                b.Click += delegate { Escolher(l); };
                Secao.Controls.Add(b);
                _resultados.Add(b);
            }
            if (Rearranjar != null) Rearranjar();
        }

        internal void Escolher(LugarDoClima l)
        {
            Clima.DefinirCidade(l);
            _busca.Texto = "";
            Mostrar(new List<LugarDoClima>(), false);
            _estado.Text = "";
            Preencher();
            if (Rearranjar != null) Rearranjar();
        }

        public int Arranjar(Secao s, int w)
        {
            Preencher();
            int m = Secao.Margem, iw = w - 2 * m, y = Secao.Topo;
            _atual.SetBounds(m, y, iw, Ui.S(22));
            y += Ui.S(28);
            int bw = TextRenderer.MeasureText(_btBuscar.Text, _btBuscar.Font).Width + Ui.S(28);
            _busca.SetBounds(m, y, iw - bw - Ui.S(8), Ui.S(34));
            _btBuscar.SetBounds(m + iw - bw, y, bw, Ui.S(34));
            y += Ui.S(42);
            if (_estado.Text.Length > 0) { _estado.Visible = true; _estado.SetBounds(m, y, iw, Ui.S(20)); y += Ui.S(24); }
            else _estado.Visible = false;
            foreach (FlatBtn b in _resultados) { b.SetBounds(m, y, iw, Ui.S(32)); y += Ui.S(36); }
            Size t = TextRenderer.MeasureText(_nota.Text, _nota.Font, new Size(iw, 0), TextFormatFlags.WordBreak);
            _nota.SetBounds(m, y, iw, t.Height + Ui.S(2));
            return _nota.Bottom - Secao.Topo;
        }
    }
}
