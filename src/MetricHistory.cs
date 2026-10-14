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

        /// <summary>Menor e maior janela que o navegador da aba Metricas aceita.</summary>
        public const int JanelaMinima = 60;
        public const int JanelaMaxima = Baldes * PassoSeg;

        public const int JanelaPadrao = 600;

        /// <summary>
        /// Janela fixa da Visao geral: cinco minutos.
        ///
        /// Ela seguia a janela da aba Metricas, e com uma hora escolhida la os
        /// blocos de 170 px recebiam 720 pontos - os picos viravam borrao. Aqui
        /// sao 60 pontos, cerca de 3 px cada, e um pico de 10 s aparece.
        /// </summary>
        public const int JanelaVisaoGeral = 300;

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

        /// <summary>Leituras feitas no balde corrente; ver HoraDeAmostrar.</summary>
        private static int _tentativas = 0;
        private const int TentativasPorBalde = 3;

        private static bool _sujo = false;
        private static bool _gravando = false;
        private static long _geracao = 0;
        private static DateTime _ultimaGravacao = DateTime.MinValue;
        private static DateTime _proximaTentativa = DateTime.MinValue;

        /// <summary>Versao das series finalizadas, usada para evitar repinturas vazias.</summary>
        public static long Revision
        {
            get { lock (_trava) return _geracao; }
        }

        private static string Arquivo
        {
            get { return Path.Combine(Paths.DataDir, "history.dat"); }
        }

        private static long BaldeAgora()
        {
            return BaldeEm(DateTime.UtcNow);
        }

        private static long BaldeEm(DateTime utcNow)
        {
            return (long)(utcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
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
                string antes = string.Join("\n", _seguidos.ToArray());
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
                if (antes != string.Join("\n", _seguidos.ToArray())) Alterado();
            }
        }

        /// <summary>
        /// Acrescenta leituras a lista, sem tirar as que ja estao.
        ///
        /// Existe por causa de uma diferenca de horario. A lista do arranque e
        /// montada assim que as fontes abrem, e nesse instante varios sensores
        /// ainda nao publicaram valor nenhum - a escolha dos destaques exige
        /// valor, entao ela pode cair num sensor diferente do que a janela vai
        /// desenhar minutos depois, com tudo aquecido. O sintoma era um bloco
        /// sem curva atras, e so um: os vizinhos, escolhidos igual nas duas
        /// horas, tinham a serie completa.
        ///
        /// Quem monta a tela chama isto com o que de fato pos na tela. O Seguir,
        /// que substitui a lista e poda o que sobrou, continua sendo chamado
        /// quando a janela fecha - e ai os valores estao quentes e as duas
        /// escolhas concordam.
        /// </summary>
        public static void SeguirTambem(IEnumerable<string> ids)
        {
            if (ids == null) return;
            lock (_trava)
            {
                foreach (string id in ids)
                {
                    if (string.IsNullOrEmpty(id) || _seguidos.Contains(id)) continue;
                    _seguidos.Add(id);
                    if (!_series.ContainsKey(id)) _series[id] = new Serie();
                    Alterado();
                }
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
                if (BaldeAgora() != _balde) return true;

                // Um sensor que nao respondeu no ciclo da amostra deixava o
                // balde inteiro vazio, e a linha quebrava ali. Mais duas
                // tentativas dentro do mesmo balde resolvem a falha passageira;
                // o limite existe porque o FPS sem jogo aberto nunca responde,
                // e insistir todo segundo seria pagar a leitura cheia a toa.
                if (_tentativas >= TentativasPorBalde) return false;
                foreach (string id in _seguidos)
                {
                    Serie s;
                    if (!_series.TryGetValue(id, out s) || s.Cont == 0) return true;
                }
                return false;
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
            AmostrarEm(ler, DateTime.UtcNow);
        }

        internal static void AmostrarEm(Func<string, float?> ler, DateTime utcNow)
        {
            if (ler == null) return;
            lock (_trava)
            {
                if (_seguidos.Count == 0) return;
                long agora = BaldeEm(utcNow);
                if (_balde == 0) _balde = agora;
                if (agora < _balde)
                {
                    // Nao apresentar dados do futuro depois de um ajuste de hora.
                    foreach (Serie s in _series.Values)
                    {
                        for (int i = 0; i < Baldes; i++) s.V[i] = float.NaN;
                        s.Soma = 0; s.Cont = 0;
                    }
                    _balde = agora;
                    Alterado();
                }
                Fechar(agora);
                _tentativas++;

                foreach (string id in _seguidos)
                {
                    Serie ja;
                    if (_series.TryGetValue(id, out ja) && ja.Cont > 0) continue;   // uma leitura por balde

                    float? v;
                    try { v = ler(id); }
                    catch { v = null; }
                    if (!v.HasValue || float.IsNaN(v.Value) || float.IsInfinity(v.Value)) continue;

                    Serie s;
                    if (!_series.TryGetValue(id, out s)) { s = new Serie(); _series[id] = s; }
                    s.Soma += v.Value;
                    s.Cont++;
                }

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
            _tentativas = 0;
            Alterado();
        }

        private static void Alterado()
        {
            _geracao++;
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
                DateTime agora = DateTime.UtcNow;
                if (agora < _proximaTentativa) return;
                if ((agora - _ultimaGravacao).TotalMinutes < 1) return;
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
            return Janela(id, segundos, 0, ref destino);
        }

        /// <summary>
        /// A janela que termina no balde fimBalde, ou agora quando ele e zero.
        ///
        /// O navegador da aba Metricas fixa o fim pelo indice absoluto do balde,
        /// e nao por "tantos minutos atras": assim o trecho escolhido fica parado
        /// enquanto o tempo anda, em vez de escorregar cinco segundos a cada
        /// balde fechado. O que ja saiu das seis horas guardadas volta como falha.
        /// </summary>
        public static int Janela(string id, int segundos, long fimBalde, ref float[] destino)
        {
            int n = segundos / PassoSeg;
            if (n < 2) n = 2;
            if (n > Baldes) n = Baldes;
            if (destino == null || destino.Length < n) destino = new float[n];

            lock (_trava)
            {
                Serie s;
                long atras = fimBalde > 0 ? (_balde - 1) - fimBalde : 0;
                if (atras < 0) atras = 0;
                if (string.IsNullOrEmpty(id) || !_series.TryGetValue(id, out s) || atras >= Baldes)
                {
                    for (int i = 0; i < n; i++) destino[i] = float.NaN;
                    return n;
                }
                // indice em V do ultimo ponto pedido, e do primeiro
                int ultimo = Baldes - 1 - (int)atras;
                int primeiro = ultimo - n + 1;
                int falta = primeiro < 0 ? -primeiro : 0;
                for (int i = 0; i < falta; i++) destino[i] = float.NaN;
                Array.Copy(s.V, primeiro + falta, destino, falta, n - falta);
            }
            return n;
        }

        /// <summary>Indice absoluto do ultimo balde fechado, o "agora" das curvas.</summary>
        public static long UltimoBalde
        {
            get { lock (_trava) return _balde > 0 ? _balde - 1 : BaldeAgora() - 1; }
        }

        /// <summary>Instante local do fim de um balde absoluto.</summary>
        public static DateTime HoraDoBalde(long balde)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                   .AddSeconds((double)balde * PassoSeg).ToLocalTime();
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

        /// <summary>Nome da janela para a interface: "10 min", "1 h 30 min", "6 h".</summary>
        public static string NomeDaJanela(int segundos)
        {
            if (segundos >= 3600)
            {
                int h = segundos / 3600;
                int m = (segundos % 3600) / 60;
                return m == 0 ? h + " h" : h + " h " + m + " min";
            }
            if (segundos < 60) return segundos + " s";
            return (segundos / 60) + " min";
        }

        /// <summary>
        /// A janela pedida, se estiver entre um minuto e seis horas, alinhada ao
        /// passo; o padrao para configuracao antiga ou adulterada.
        ///
        /// Antes so passavam 10 min, 1 h e 6 h, os tres botoes. O navegador
        /// escolhe qualquer tamanho, e uma configuracao de 25 minutos e
        /// legitima.
        /// </summary>
        public static int JanelaValida(int segundos)
        {
            if (segundos < JanelaMinima || segundos > JanelaMaxima) return JanelaPadrao;
            return segundos - segundos % PassoSeg;
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
            long geracao;
            long balde;
            string[] ids;
            float[][] valores;
            lock (_trava)
            {
                if (!_sujo && _ultimaGravacao != DateTime.MinValue) return;
                if (_gravando) return;
                _gravando = true;

                geracao = _geracao;
                balde = _balde;
                ids = new string[_series.Count];
                valores = new float[_series.Count][];
                int i = 0;
                foreach (KeyValuePair<string, Serie> kv in _series)
                {
                    ids[i] = kv.Key;
                    valores[i] = (float[])kv.Value.V.Clone();
                    i++;
                }
            }

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
                    w.Write(balde);
                    w.Write(ids.Length);
                    for (int i = 0; i < ids.Length; i++)
                    {
                        w.Write(ids[i]);
                        for (int k = 0; k < Baldes; k++) w.Write(valores[i][k]);
                    }
                }

                // Nunca apaga o arquivo anterior antes de o novo estar pronto.
                if (File.Exists(caminho)) File.Replace(temp, caminho, null);
                else File.Move(temp, caminho);

                lock (_trava)
                {
                    _ultimaGravacao = DateTime.UtcNow;
                    _proximaTentativa = DateTime.MinValue;
                    if (_geracao == geracao) _sujo = false;
                    _gravando = false;
                }
            }
            catch (Exception ex)
            {
                Log.Error("gravacao do historico de metricas", ex);
                lock (_trava)
                {
                    _proximaTentativa = DateTime.UtcNow.AddMinutes(1);
                    _sujo = true;
                    _gravando = false;
                }
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex2) { Log.Error("descarte do historico temporario", ex2); }
            }
        }
    }
}
