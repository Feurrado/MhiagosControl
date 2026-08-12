using System;
using System.Collections.Generic;
using System.IO;

namespace MhiagosControl
{
    /// <summary>
    /// Serie temporal das leituras da aba Metricas, com gravacao em disco.
    ///
    /// A primeira versao guardava o historico dentro de cada cartao: sumia ao
    /// fechar a janela, sumia ao reordenar a grade e nao existia enquanto o
    /// aplicativo rodava so na bandeja - ou seja, o grafico so sabia do periodo
    /// em que alguem estava olhando para ele, que e justamente quando ninguem
    /// precisa de historico. Aqui a serie vive fora da interface, e alimentada
    /// pelo ciclo de leitura e sobrevive ao fechamento.
    ///
    /// O tempo e dividido em baldes de duracao fixa. Cada balde guarda a media
    /// das leituras que cairam nele; baldes sem leitura ficam NaN e o grafico os
    /// desenha como falha, e nao como linha reta - o aplicativo desligado das
    /// 3h as 8h e um buraco, nao uma temperatura constante por cinco horas.
    /// </summary>
    public static class MetricHistory
    {
        /// <summary>
        /// Duracao do balde.
        ///
        /// Cinco segundos e o meio-termo entre resolucao e tamanho: guardar cada
        /// segundo por seis horas seriam 21600 amostras por leitura, 86 KB cada,
        /// para um grafico que raramente passa de 400 px de largura. O numero
        /// grande do cartao continua vindo da leitura instantanea; quem e
        /// amostrado aqui e so a curva.
        /// </summary>
        public const int PassoSeg = 5;

        /// <summary>Baldes guardados por leitura: seis horas.</summary>
        public const int Baldes = 4320;

        /// <summary>Janelas oferecidas na interface, em segundos.</summary>
        public static readonly int[] Janelas = { 600, 3600, 21600 };

        public const int JanelaPadrao = 600;

        private const string Magica = "MHIST1";

        private class Serie
        {
            public readonly float[] V = new float[Baldes];
            public double Soma;
            public int Cont;

            public Serie()
            {
                for (int i = 0; i < Baldes; i++) V[i] = float.NaN;
            }
        }

        private static readonly object _trava = new object();
        private static readonly Dictionary<string, Serie> _series = new Dictionary<string, Serie>();
        private static readonly List<string> _seguidos = new List<string>();

        /// <summary>Indice do balde corrente. Um so para todas as series.</summary>
        private static long _balde = 0;

        private static bool _sujo = false;
        private static DateTime _ultimaGravacao = DateTime.MinValue;

        private static string Arquivo
        {
            get { return Path.Combine(Paths.DataDir, "history.dat"); }
        }

        private static long BaldeAgora()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                   .TotalSeconds / PassoSeg;
        }

        /// <summary>
        /// Define quais leituras sao acompanhadas.
        ///
        /// Series de leituras que sairam da grade sao descartadas: manter o
        /// historico de um cartao removido faria o arquivo so crescer, e quem
        /// removeu o cartao removeu o interesse.
        /// </summary>
        public static void Seguir(IEnumerable<string> ids)
        {
            lock (_trava)
            {
                _seguidos.Clear();
                if (ids != null)
                    foreach (string id in ids)
                        if (!string.IsNullOrEmpty(id) && !_seguidos.Contains(id)) _seguidos.Add(id);

                List<string> sobrando = new List<string>();
                foreach (KeyValuePair<string, Serie> kv in _series)
                    if (!_seguidos.Contains(kv.Key)) sobrando.Add(kv.Key);
                foreach (string id in sobrando) _series.Remove(id);

                foreach (string id in _seguidos)
                    if (!_series.ContainsKey(id)) _series[id] = new Serie();
            }
        }

        public static List<string> Seguidos
        {
            get { lock (_trava) return new List<string>(_seguidos); }
        }

        /// <summary>
        /// Chegou a hora de mais uma amostra.
        ///
        /// Existe para o ciclo de leitura saber quando vale a pena abrir a
        /// leitura restrita: um balde guarda uma amostra, entao ler os sensores
        /// dos cartoes em todos os ciclos seria pagar cinco vezes o preco para
        /// jogar quatro leituras fora. Sem cartao nenhum devolve falso, e a
        /// maquina de quem nao usa a aba nao paga nada.
        /// </summary>
        public static bool HoraDeAmostrar()
        {
            lock (_trava)
            {
                if (_seguidos.Count == 0) return false;
                if (_balde == 0) return true;
                return BaldeAgora() > _balde;
            }
        }

        /// <summary>
        /// Um ciclo de leitura: acumula no balde corrente e fecha os que passaram.
        ///
        /// Chamada pela thread de atualizacao, que roda com a janela aberta ou
        /// fechada. O leitor recebe o identificador e devolve o valor, ou nulo
        /// quando a leitura nao esta disponivel neste ciclo.
        /// </summary>
        public static void Amostrar(Func<string, float?> ler)
        {
            if (ler == null) return;
            lock (_trava)
            {
                if (_seguidos.Count == 0) return;
                if (_balde == 0) _balde = BaldeAgora();

                foreach (string id in _seguidos)
                {
                    float? v;
                    try { v = ler(id); }
                    catch { v = null; }
                    if (!v.HasValue || float.IsNaN(v.Value) || float.IsInfinity(v.Value)) continue;

                    Serie s;
                    if (!_series.TryGetValue(id, out s)) { s = new Serie(); _series[id] = s; }
                    s.Soma += v.Value;
                    s.Cont++;
                }

                Fechar(BaldeAgora());
            }
        }

        /// <summary>Fecha os baldes ate o corrente, preenchendo com falha os vazios.</summary>
        private static void Fechar(long agora)
        {
            if (agora <= _balde) return;

            long faltando = agora - _balde;
            if (faltando > Baldes) faltando = Baldes;   // ficou desligado mais que a janela inteira

            for (long k = 0; k < faltando; k++)
            {
                bool primeiro = (k == 0);
                foreach (KeyValuePair<string, Serie> kv in _series)
                {
                    Serie s = kv.Value;
                    Array.Copy(s.V, 1, s.V, 0, Baldes - 1);

                    // Só o primeiro balde recebe o que foi acumulado; os
                    // seguintes sao tempo em que ninguem leu nada.
                    s.V[Baldes - 1] = (primeiro && s.Cont > 0) ? (float)(s.Soma / s.Cont) : float.NaN;
                    s.Soma = 0; s.Cont = 0;
                }
            }

            _balde = agora;
            _sujo = true;
        }

        /// <summary>
        /// Grava se ja passou tempo suficiente desde a ultima vez.
        ///
        /// Separada do ciclo de propósito: Amostrar roda com o lock dos sensores
        /// na mao, e uma gravacao de algumas centenas de kilobytes ali dentro
        /// seguraria o proximo envio ao mostrador. Aqui a chamada e feita depois
        /// que o lock foi solto.
        /// </summary>
        public static void SalvarSeVencido()
        {
            lock (_trava)
            {
                if (!_sujo) return;
                if ((DateTime.UtcNow - _ultimaGravacao).TotalMinutes < 5) return;
            }
            Salvar();
        }

        /// <summary>
        /// Copia a janela pedida para o vetor do chamador, do mais antigo ao mais novo.
        ///
        /// Devolve SEMPRE a janela inteira, com falha onde nao ha leitura. Uma
        /// serie de dois minutos vista em seis horas ocupa a ponta direita e
        /// deixa o resto em branco, que e a verdade; esticar dois minutos por
        /// toda a largura seria desenhar uma escala de tempo que nao existe.
        /// </summary>
        public static int Janela(string id, int segundos, ref float[] destino)
        {
            int n = segundos / PassoSeg;
            if (n < 2) n = 2;
            if (n > Baldes) n = Baldes;
            if (destino == null || destino.Length < n) destino = new float[n];

            lock (_trava)
            {
                Serie s;
                if (string.IsNullOrEmpty(id) || !_series.TryGetValue(id, out s))
                {
                    for (int i = 0; i < n; i++) destino[i] = float.NaN;
                    return n;
                }
                Array.Copy(s.V, Baldes - n, destino, 0, n);
            }
            return n;
        }

        /// <summary>
        /// Instante local do ultimo balde devolvido por Janela.
        ///
        /// Depois de Fechar, V[Baldes-1] guarda a media do balde que acabou de
        /// se encerrar - o de indice _balde - 1, e nao o corrente, que ainda
        /// esta acumulando. Quem desenha precisa disto para dizer a que horas
        /// aconteceu o que esta sob o ponteiro: contar para tras a partir de
        /// DateTime.Now erraria por ate um passo, e erraria mais ainda quando o
        /// ciclo de leitura atrasa.
        /// </summary>
        public static DateTime FimDaJanela()
        {
            long b;
            lock (_trava) { b = _balde > 0 ? _balde - 1 : BaldeAgora() - 1; }
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                   .AddSeconds((double)b * PassoSeg).ToLocalTime();
        }

        /// <summary>Nome da janela para a interface: "10 min", "1 h", "6 h".</summary>
        public static string NomeDaJanela(int segundos)
        {
            if (segundos >= 3600)
            {
                int h = segundos / 3600;
                return h + " h";
            }
            return (segundos / 60) + " min";
        }

        /// <summary>A janela valida mais proxima da pedida, para configuracao antiga ou adulterada.</summary>
        public static int JanelaValida(int segundos)
        {
            foreach (int j in Janelas) if (j == segundos) return j;
            return JanelaPadrao;
        }

        // ---------------- disco ----------------

        public static void Carregar()
        {
            lock (_trava)
            {
                _series.Clear();
                _balde = BaldeAgora();

                string caminho = Arquivo;
                if (!File.Exists(caminho)) return;

                try
                {
                    using (FileStream fs = File.OpenRead(caminho))
                    using (BinaryReader r = new BinaryReader(fs))
                    {
                        // Formato e passo conferidos antes de qualquer leitura: um
                        // arquivo de outra versao lido como se fosse deste daria
                        // uma curva plausivel e errada, que e pior que nenhuma.
                        if (r.ReadString() != Magica) return;
                        if (r.ReadInt32() != PassoSeg) return;
                        if (r.ReadInt32() != Baldes) return;

                        long balde = r.ReadInt64();
                        int n = r.ReadInt32();
                        if (n < 0 || n > 4096) return;

                        for (int i = 0; i < n; i++)
                        {
                            string id = r.ReadString();
                            Serie s = new Serie();
                            for (int k = 0; k < Baldes; k++) s.V[k] = r.ReadSingle();
                            _series[id] = s;
                        }

                        _balde = balde;

                        // O tempo passou com o aplicativo fechado: avanca a serie
                        // com falha ate agora, senao a leitura de ontem apareceria
                        // colada na de hoje.
                        Fechar(BaldeAgora());
                        _sujo = false;
                        _ultimaGravacao = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("leitura do historico de metricas", ex);
                    _series.Clear();
                    _balde = BaldeAgora();
                }
            }
        }

        public static void Salvar()
        {
            lock (_trava)
            {
                if (!_sujo && _ultimaGravacao != DateTime.MinValue) return;
                _ultimaGravacao = DateTime.UtcNow;
                _sujo = false;

                string caminho = Arquivo;
                string temp = caminho + ".tmp";
                try
                {
                    using (FileStream fs = File.Create(temp))
                    using (BinaryWriter w = new BinaryWriter(fs))
                    {
                        w.Write(Magica);
                        w.Write(PassoSeg);
                        w.Write(Baldes);
                        w.Write(_balde);
                        w.Write(_series.Count);
                        foreach (KeyValuePair<string, Serie> kv in _series)
                        {
                            w.Write(kv.Key);
                            for (int k = 0; k < Baldes; k++) w.Write(kv.Value.V[k]);
                        }
                    }

                    // Grava em arquivo temporario e troca: um desligamento no meio
                    // da escrita perde o historico, nao o arquivo.
                    if (File.Exists(caminho)) File.Delete(caminho);
                    File.Move(temp, caminho);
                }
                catch (Exception ex)
                {
                    Log.Error("gravacao do historico de metricas", ex);
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch (Exception ex2) { Log.Error("descarte do historico temporario", ex2); }
                }
            }
        }
    }
}
