using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Quem entra e quem sai de uma lista de aparelhos, com um relogio so.
    ///
    /// Cada item tem uma presenca de 0 (ausente) a 1 (inteiro). Item novo sobe
    /// de 0 a 1; item que sumiu da leitura continua na lista, descendo de 1 a
    /// 0, e so depois e removido. Quem desenha usa Valor(i) para a transparencia
    /// e para a largura (ou altura) que o item ocupa: os vizinhos deslizam junto.
    ///
    /// Mesma duracao e curva das animacoes de recolher (AnimacaoDeRecolher) e,
    /// como la, o tempo vem do cronometro, nao da contagem de ticks.
    /// </summary>
    internal sealed class PresencaAnimada : IDisposable
    {
        private sealed class Entrada
        {
            public string Chave;
            public float De, Ate, Valor;
            public long Inicio;
            public long Duracao;
        }

        private readonly List<Entrada> _itens = new List<Entrada>();
        private readonly Timer _relogio = new Timer();
        private readonly Stopwatch _cronometro = new Stopwatch();
        private bool _descartado;

        /// <summary>Cada passo, inclusive o ultimo.</summary>
        public event EventHandler Quadro;

        /// <summary>Algum item saiu de vez (presenca zero) e foi tirado da lista.</summary>
        public event EventHandler Removido;

        public PresencaAnimada()
        {
            // 10 ms pelo mesmo motivo de AnimacaoDeRecolher: 16 cai em 31 ms no Windows
            _relogio.Interval = 10;
            _relogio.Tick += delegate { Avancar(_cronometro.ElapsedMilliseconds); };
        }

        public int Quantidade { get { return _itens.Count; } }
        public bool Animando { get { return _relogio.Enabled; } }
        public string Chave(int i) { return _itens[i].Chave; }
        public float Valor(int i) { return _itens[i].Valor; }
        /// <summary>Esta indo embora: nao esta na ultima leitura.</summary>
        public bool Saindo(int i) { return _itens[i].Ate <= 0f; }

        /// <summary>Soma das presencas: quantos itens "inteiros" a lista ocupa agora.</summary>
        public float Soma
        {
            get
            {
                float s = 0f;
                foreach (Entrada e in _itens) s += e.Valor;
                return s;
            }
        }

        /// <summary>
        /// Diz quem esta na leitura de agora, na ordem dela. Quem ja estava
        /// continua onde esta; quem sumiu fica logo depois do vizinho que o
        /// antecedia. Sem animar, a lista vira exatamente a leitura.
        /// </summary>
        public void Definir(IList<string> chaves, bool animar)
        {
            if (_descartado) return;
            HashSet<string> vivos = new HashSet<string>(chaves);
            long agora = _cronometro.IsRunning ? _cronometro.ElapsedMilliseconds : 0;

            if (!animar || !Ui.Animacoes)
            {
                _itens.Clear();
                foreach (string c in chaves) _itens.Add(Novo(c, 1f, 1f, agora));
                _relogio.Stop();
                _cronometro.Reset();
                Avisar();
                return;
            }

            Dictionary<string, Entrada> antigos = new Dictionary<string, Entrada>();
            foreach (Entrada e in _itens) antigos[e.Chave] = e;

            List<Entrada> novos = new List<Entrada>();
            foreach (string c in chaves)
            {
                Entrada e;
                if (antigos.TryGetValue(c, out e)) Mirar(e, 1f, agora);
                else e = Novo(c, 0f, 1f, agora);
                novos.Add(e);
            }

            // quem sumiu entra de volta na lista logo depois do que vinha antes dele
            int ultimoDoEixo = -1;
            foreach (Entrada e in _itens)
            {
                if (vivos.Contains(e.Chave)) { ultimoDoEixo = novos.IndexOf(antigos[e.Chave]); continue; }
                Mirar(e, 0f, agora);
                if (e.Valor <= 0f) continue;   // ja tinha ido: nada a animar
                novos.Insert(ultimoDoEixo + 1, e);
                ultimoDoEixo++;
            }

            _itens.Clear();
            _itens.AddRange(novos);

            bool algum = false;
            foreach (Entrada e in _itens) if (e.Valor != e.Ate) algum = true;
            if (algum)
            {
                if (!_cronometro.IsRunning) { _cronometro.Reset(); _cronometro.Start(); }
                _relogio.Start();
            }
            Avisar();
        }

        private static Entrada Novo(string chave, float de, float ate, long inicio)
        {
            Entrada e = new Entrada();
            e.Chave = chave; e.De = e.Valor = de; e.Ate = ate; e.Inicio = inicio;
            e.Duracao = AnimacaoDeRecolher.DuracaoMs;
            return e;
        }

        /// <summary>Muda o destino partindo de onde esta; o trecho que falta leva so a sua parte da duracao.</summary>
        private static void Mirar(Entrada e, float ate, long agora)
        {
            if (e.Ate == ate) return;
            e.De = e.Valor;
            e.Ate = ate;
            e.Inicio = agora;
            e.Duracao = Math.Max(1, (long)Math.Round(AnimacaoDeRecolher.DuracaoMs * Math.Abs(ate - e.De)));
        }

        /// <summary>Um passo, com o tempo do cronometro. Separado para os testes.</summary>
        internal void Avancar(long ms)
        {
            if (_descartado) return;
            bool pendente = false, tirou = false;
            for (int i = _itens.Count - 1; i >= 0; i--)
            {
                Entrada e = _itens[i];
                if (e.Valor == e.Ate) { if (e.Ate <= 0f) { _itens.RemoveAt(i); tirou = true; } continue; }
                float t = Math.Min(1f, (ms - e.Inicio) / (float)e.Duracao);
                if (t >= 1f) e.Valor = e.Ate;
                else e.Valor = e.De + (e.Ate - e.De) * AnimacaoDeRecolher.Suavizar(Math.Max(0f, t));
                if (e.Valor == e.Ate && e.Ate <= 0f) { _itens.RemoveAt(i); tirou = true; }
                else if (e.Valor != e.Ate) pendente = true;
            }
            if (!pendente) { _relogio.Stop(); _cronometro.Reset(); }
            Avisar();
            if (tirou && Removido != null) Removido(this, EventArgs.Empty);
        }

        private void Avisar()
        {
            if (Quadro != null) Quadro(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _descartado = true;
            _relogio.Stop();
            _relogio.Dispose();
        }
    }
}
