using System;
using System.Drawing;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// O alvo do ar que desliza quando muda: ao subir, o numero velho sai para
    /// cima e o novo entra de baixo; ao descer, o contrario, com fade (~180 ms).
    ///
    /// Detecta a troca pelo proprio texto pintado, entao vale para qualquer
    /// origem da mudanca (o - e +, o arrasto no mostrador ou a leitura da
    /// nuvem). Clique seguido no meio do caminho recomeca do numero que estava
    /// na tela, sem perder valor. Com as animacoes do Windows desligadas a
    /// Transicao vai direto ao fim e o numero so troca.
    /// </summary>
    internal sealed class NumeroDeslizante
    {
        public const int DuracaoMs = 180;
        private readonly Transicao _t;
        private string _atual, _antigo;
        private int _direcao;

        public NumeroDeslizante(Control dono) { _t = new Transicao(dono, DuracaoMs); }

        /// <summary>Testes: no meio de uma troca.</summary>
        internal bool Animando { get { return _t.Animando && _antigo != null; } }
        internal int Direcao { get { return _direcao; } }
        internal string Antigo { get { return _antigo; } }

        /// <summary>Registra o texto a mostrar; se mudou, comeca a troca. Separado da pintura para os testes.</summary>
        internal void Definir(string texto)
        {
            if (texto == null) texto = "";
            if (_atual == null) { _atual = texto; _t.Fixar(1f); return; }
            if (texto == _atual) return;
            _antigo = _atual;
            _direcao = Comparar(texto, _atual);
            _atual = texto;
            _t.Zerar();
            _t.Ir(1f);
        }

        /// <summary>+1 quando o numero subiu, -1 quando desceu, 0 se nao da para comparar.</summary>
        internal static int Comparar(string novo, string velho)
        {
            int a, b;
            if (!Inteiro(novo, out a) || !Inteiro(velho, out b)) return 0;
            return a > b ? 1 : a < b ? -1 : 0;
        }

        private static bool Inteiro(string s, out int v)
        {
            v = 0;
            int i = 0, sinal = 1;
            if (string.IsNullOrEmpty(s)) return false;
            if (s[0] == '-') { sinal = -1; i = 1; }
            int ini = i;
            while (i < s.Length && char.IsDigit(s[i])) { v = v * 10 + (s[i] - '0'); i++; }
            v *= sinal;
            return i > ini;
        }

        /// <summary>Pinta o texto em origem; durante a troca, o velho e o novo deslizam dentro de area.</summary>
        public void Desenhar(Graphics g, string texto, Font f, Point origem, Rectangle area, Color cor, Color fundo, TextFormatFlags fl)
        {
            Definir(texto);
            float v = _t.Valor;
            if (!_t.Animando || _antigo == null || _direcao == 0 && v >= 1f)
            {
                TextRenderer.DrawText(g, texto, f, origem, cor, fl);
                return;
            }
            int curso = Math.Max(4, area.Height * 3 / 5);
            int dir = _direcao == 0 ? 1 : _direcao;
            Region antes = g.Clip;
            g.SetClip(area);
            // sai: anda para o lado oposto ao que o novo vem, apagando
            TextRenderer.DrawText(g, _antigo, f, new Point(origem.X, origem.Y - (int)Math.Round(dir * curso * v)), Ui.Misturar(cor, fundo, v), fl);
            TextRenderer.DrawText(g, texto, f, new Point(origem.X, origem.Y + (int)Math.Round(dir * curso * (1f - v))), Ui.Misturar(cor, fundo, 1f - v), fl);
            g.Clip = antes;
        }
    }

    /// <summary>
    /// Estado ligado do ar como um valor de 0 a 1 que anda em ~250 ms: as cores
    /// do botao, do icone e do alvo misturam pelo valor. A primeira leitura
    /// nao anima (a tela ja abre no estado certo); ao ligar, dispara tambem um
    /// pulso que cresce e some em volta do icone.
    /// </summary>
    internal sealed class LigadoAnimado
    {
        public const int DuracaoMs = 250, PulsoMs = 450;
        private readonly Transicao _t, _pulso;
        private bool _iniciado, _alvo;

        public LigadoAnimado(Control dono)
        {
            _t = new Transicao(dono, DuracaoMs);
            _pulso = new Transicao(dono, PulsoMs);
        }

        public float Valor { get { return _t.Valor; } }
        internal bool Animando { get { return _t.Animando; } }

        /// <summary>De 0 a 1 enquanto o pulso de ligar corre; -1 parado.</summary>
        public float Pulso { get { return _pulso.Animando ? _pulso.Valor : -1f; } }

        public void Definir(bool ligado)
        {
            if (!_iniciado) { _iniciado = true; _alvo = ligado; _t.Fixar(ligado ? 1f : 0f); return; }
            if (ligado == _alvo) return;
            _alvo = ligado;
            _t.Ir(ligado ? 1f : 0f);
            if (ligado) { _pulso.Zerar(); _pulso.Ir(1f); }
        }
    }

    /// <summary>Flash curto do botao pressionado (- e +): acende e apaga em ~220 ms.</summary>
    internal sealed class FlashDoBotao
    {
        private readonly Transicao _t;
        public FlashDoBotao(Control dono) { _t = new Transicao(dono, 220); }
        public void Disparar() { _t.Zerar(); _t.Ir(1f); }
        /// <summary>Intensidade agora (1 no clique, 0 parado).</summary>
        public float Intensidade { get { return _t.Animando ? 1f - _t.Valor : 0f; } }
    }
}
