using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MhiagosControl
{
    /// <summary>Uma descarga: do fim de uma carga ate o inicio da seguinte.</summary>
    public sealed class Descarga
    {
        public DateTime Inicio, Fim;
        public int PctInicio, PctFim;

        /// <summary>Tempo com o aparelho ligado e lendo; desligado na gaveta nao conta.</summary>
        public double UsoSegundos;

        /// <summary>Comecou sem uma carga observada - o app abriu no meio dela.</summary>
        public bool Parcial;

        public int Gasto { get { return Math.Max(0, PctInicio - PctFim); } }
    }

    /// <summary>Uma passagem pelo carregador.</summary>
    public sealed class Carga
    {
        public DateTime Inicio, Fim;
        public int PctInicio, PctFim;

        /// <summary>A carga aconteceu fora da vista: a porcentagem so apareceu maior.</summary>
        public bool Deduzida;
    }

    /// <summary>Um ponto do grafico: a carga num instante.</summary>
    public struct AmostraDeBateria
    {
        public DateTime Quando;
        public int Pct;
        public bool Carregando;
    }

    /// <summary>Tudo o que a pagina Baterias mostra de um aparelho, ja calculado.</summary>
    public sealed class AnaliseDeBateria
    {
        public string Chave, Nome;
        public TipoPeriferico Tipo;
        public FontePeriferico Fonte;

        public int? PctAtual;
        public bool Carregando;
        public DateTime? CarregandoDesde;
        public int CarregandoDe;

        public Descarga Atual;           // a descarga em andamento, ou nula
        public Descarga UltimaCompleta;  // a ultima encerrada por uma carga
        public Carga UltimaCarga;

        /// <summary>Horas de uso por 100% de bateria, das descargas medidas; nulo sem dado.</summary>
        public double? HorasPorCargaCheia;
        public int DescargasMedidas;

        /// <summary>Horas de uso que ainda restam, somando o ritmo de cada faixa abaixo da carga atual.</summary>
        public double? HorasRestantes;

        /// <summary>Todo o uso medido desde que o app comecou a acompanhar o aparelho.</summary>
        public double UsoTotalHoras;

        /// <summary>Ciclos equivalentes: os pontos gastos em degraus medidos, somados, divididos por 100.</summary>
        public double CiclosEquivalentes;

        /// <summary>Horas por carga cheia em cada mes com dado bastante, do mais antigo ao mais novo.</summary>
        public List<KeyValuePair<DateTime, double>> SaudePorMes = new List<KeyValuePair<DateTime, double>>();

        /// <summary>Horas que faltam para encher, quando esta no cabo e ja ha ritmo de carga medido.</summary>
        public double? HorasAteEncher;

        /// <summary>
        /// Horas por ponto percentual em cada faixa de 10% (0 = 1 a 10%, 9 =
        /// 91 a 100%); nulo onde ainda nao houve gasto medido.
        /// </summary>
        public double?[] HorasPorPontoNaFaixa;

        public AmostraDeBateria[] Amostras;
    }

    /// <summary>
    /// Historico de bateria dos perifericos: quanto cada carga dura, quando
    /// foi a ultima e como a porcentagem andou nos ultimos dias.
    ///
    /// Alimentado pela thread de atualizacao a cada ciclo, com o retrato de
    /// Perifericos.Atual. Mede USO, e nao calendario: so conta o tempo em que o
    /// aparelho estava ligado e respondendo. Um headset que passa a noite
    /// desligado nao gasta bateria, e contar essas horas faria a media dizer
    /// que a carga dura o dobro do que dura de fato.
    ///
    /// Grava em %LOCALAPPDATA%\MhiagosControl\baterias.dat, texto simples com
    /// uma linha por registro, a cada poucos minutos e em toda carga.
    /// </summary>
    public static class HistoricoDeBateria
    {
        /// <summary>Lacuna maior que esta nao e uso: o app estava fechado ou o aparelho sumiu.</summary>
        private const double LacunaMaxSeg = 30;

        /// <summary>
        /// Subir isto sem o aviso de carga conta como carga feita fora da vista.
        /// Alto de proposito: o G HUB da a carga do G325 em degraus de 10%, e
        /// a leitura volta um degrau sozinha quando o headset descansa.
        /// </summary>
        private const int SubidaDeCarga = 12;

        /// <summary>
        /// Logo depois de sair do cabo a porcentagem cai sozinha - o G325 foi
        /// de 90 para 80% em um minuto, conferido aqui. E a tensao assentando,
        /// e nao gasto: nos primeiros minutos o ponto de partida acompanha a
        /// leitura para baixo em vez de cobrar essa queda da bateria.
        /// </summary>
        private const double AssentamentoSeg = 10 * 60;

        /// <summary>Descarga menor que isto nao entra na media: o ruido da leitura domina.</summary>
        private const int GastoMinimo = 10;
        private const double UsoMinimoSeg = 30 * 60;

        private static readonly TimeSpan IntervaloDeAmostra = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan JanelaDeAmostras = TimeSpan.FromDays(7);
        private static readonly TimeSpan IntervaloDeGravacao = TimeSpan.FromMinutes(5);
        private const int DescargasGuardadas = 30;

        /// <summary>
        /// Esquecimento por ponto medido: o dado de 70 pontos atras vale a metade.
        /// A bateria envelhece e o jeito de usar muda; a media de toda a historia
        /// demora meses para acompanhar, e a previsao precisa seguir o presente.
        /// </summary>
        private const double Esquecimento = 0.99;

        /// <summary>
        /// Com pelo menos isto de pontos na faixa, um degrau que dure menos que
        /// 1/4 ou mais que 3x a media dela entra limitado a esse intervalo: o
        /// headset esquecido ligado a noite toda nao pode dobrar a previsao.
        /// </summary>
        private const double PontosParaLimitar = 15;
        private const double LimiteInferior = 0.25, LimiteSuperior = 3;

        /// <summary>Do degrau em andamento, no minimo esta fracao ainda por vir: passou do usual, mas nao acabou.</summary>
        private const double FracaoMinimaDoDegrau = 0.25;

        private sealed class Aparelho
        {
            public string Chave, Nome;
            public TipoPeriferico Tipo;
            public FontePeriferico Fonte;

            public int? PctAtual;
            public DateTime UltimaLeitura;
            public bool Carregando;
            public DateTime CargaInicio;
            public int CargaPctInicio;

            public Descarga Atual;
            public Carga UltimaCarga;
            public readonly List<Descarga> Descargas = new List<Descarga>();
            public readonly List<AmostraDeBateria> Amostras = new List<AmostraDeBateria>();

            /// <summary>Uso somado de toda a historia, sem limite de descargas.</summary>
            public double UsoTotalSeg;

            // Ritmo por faixa de 10%: quanto tempo de uso cada faixa durou e
            // quantos pontos ela perdeu. A bateria nao descarrega em linha
            // reta - costuma cair mais depressa perto do fim - e a media unica
            // erraria justamente quando a pessoa mais precisa da previsao.
            public readonly double[] FaixaSeg = new double[Faixas];
            public readonly double[] FaixaPts = new double[Faixas];

            // O degrau em andamento. A leitura grossa (o G325 da a carga de 10
            // em 10%) so diz quando o degrau MUDOU: o tempo entre duas mudancas
            // e a unica duracao exata. O primeiro degrau de cada descarga (e o
            // que atravessou uma lacuna) comeca num instante desconhecido e nao
            // entra na conta; o tempo dele so serve para descontar da previsao.
            public double PassoUso;
            public bool PassoValido;
            public double CargaPassoSeg;
            public bool CargaPassoValido;

            // O mesmo para a carga: quanto tempo no cabo cada faixa levou para
            // subir. Perto de 100% a carga desacelera, e e a faixa que mais
            // pesa no "cheio em".
            public readonly double[] CargaSeg = new double[Faixas];
            public readonly int[] CargaPts = new int[Faixas];

            /// <summary>Maior leitura desta passagem pelo cabo; so o que passa dela e carga nova.</summary>
            public int CargaTeto;

            /// <summary>
            /// Uso e gasto por mes (aaaaMM), para a saude da bateria: a mesma
            /// pessoa usando do mesmo jeito, uma carga que dura menos a cada mes
            /// e desgaste.
            /// </summary>
            public readonly SortedDictionary<string, double[]> Meses = new SortedDictionary<string, double[]>();
        }

        /// <summary>Mes com menos que isto gasto nao entra na comparacao: pouco dado e so ruido.</summary>
        private const int PontosPorMes = 20;

        private const int Faixas = 10;

        /// <summary>
        /// Peso da media geral dentro de cada faixa, em pontos. Faixa com
        /// poucos pontos medidos fica perto da media do aparelho; com muitos,
        /// passa a valer o que ela mesma mediu.
        /// </summary>
        private const double PesoDaMedia = 5;

        /// <summary>Faixa de um ponto: 1 a 10% e a 0, 91 a 100% e a 9.</summary>
        private static int Faixa(int pct) { return Math.Max(0, Math.Min(Faixas - 1, (pct - 1) / 10)); }

        private static readonly object _trava = new object();
        private static readonly Dictionary<string, Aparelho> _aparelhos = new Dictionary<string, Aparelho>();
        private static bool _carregado = false;
        private static bool _sujo = false;
        private static DateTime _ultimaGravacao = DateTime.MinValue;
        private static string _caminho;

        public static string Caminho
        {
            get { return _caminho ?? Path.Combine(Paths.DataDir, "baterias.dat"); }
            internal set { _caminho = value; }   // os testes gravam em pasta temporaria
        }

        public static string Chave(Periferico p) { return p.Fonte + "|" + p.Nome; }

        // ---------------- registro ----------------

        /// <summary>Um ciclo da thread de atualizacao. Barato quando nada mudou.</summary>
        public static void Registrar(Periferico[] atual, DateTime agora)
        {
            if (atual == null) return;
            bool gravar;
            lock (_trava)
            {
                CarregarSePreciso();
                if (!_carregado) return;   // arquivo ainda ilegivel: veja CarregarSePreciso
                foreach (Periferico p in atual)
                    if (p != null && p.Bateria.HasValue && p.Fonte != FontePeriferico.Bluetooth) Registrar(p, agora);
                gravar = _sujo && agora - _ultimaGravacao >= IntervaloDeGravacao;
            }
            if (gravar) Salvar(agora);
        }

        private static void Registrar(Periferico p, DateTime agora)
        {
            string chave = Chave(p);
            Aparelho a;
            if (!_aparelhos.TryGetValue(chave, out a))
            {
                a = new Aparelho();
                a.Chave = chave;
                _aparelhos[chave] = a;
            }
            a.Nome = p.Nome; a.Tipo = p.Tipo; a.Fonte = p.Fonte;

            int pct = p.Bateria.Value;
            double dt = a.PctAtual.HasValue ? (agora - a.UltimaLeitura).TotalSeconds : 0;
            bool continuo = dt > 0 && dt <= LacunaMaxSeg;

            if (p.Carregando)
            {
                if (!a.Carregando)
                {
                    FecharDescarga(a, agora);
                    a.Carregando = true;
                    a.CargaInicio = agora;
                    a.CargaPctInicio = pct;
                    a.CargaTeto = pct;
                    a.CargaPassoSeg = 0; a.CargaPassoValido = false;
                    Amostrar(a, agora, pct, true, true);
                    MarcarParaGravar(true);
                }
                else if (a.CargaTeto < 100)
                {
                    // O tempo entre duas subidas da leitura e o que vale; so
                    // pontos acima do teto, para uma leitura que oscila nao
                    // contar duas vezes. Veja Aparelho.PassoUso.
                    if (continuo) a.CargaPassoSeg += dt;
                    else a.CargaPassoValido = false;
                    if (pct > a.CargaTeto)
                    {
                        int n = pct - a.CargaTeto;
                        if (continuo && a.CargaPassoValido)
                            for (int q = a.CargaTeto + 1; q <= pct; q++)
                            {
                                a.CargaSeg[Faixa(q)] += a.CargaPassoSeg / n;
                                a.CargaPts[Faixa(q)]++;
                            }
                        a.CargaTeto = pct;
                        a.CargaPassoSeg = 0; a.CargaPassoValido = continuo;
                        MarcarParaGravar(false);
                    }
                }
            }
            else
            {
                if (a.Carregando)
                {
                    // saiu do carregador: a carga fica registrada e uma descarga nova comeca
                    a.Carregando = false;
                    a.UltimaCarga = new Carga
                    {
                        Inicio = a.CargaInicio, Fim = agora,
                        PctInicio = a.CargaPctInicio, PctFim = Math.Max(pct, a.PctAtual ?? pct)
                    };
                    a.Atual = NovaDescarga(agora, pct, false); a.PassoUso = 0; a.PassoValido = false;
                    Amostrar(a, agora, pct, false, true);
                    MarcarParaGravar(true);
                }
                else if (a.PctAtual.HasValue && pct >= a.PctAtual.Value + SubidaDeCarga)
                {
                    // carregou sem a gente ver: app fechado, ou fonte que nao avisa
                    FecharDescarga(a, agora);
                    a.UltimaCarga = new Carga
                    {
                        Inicio = a.UltimaLeitura, Fim = agora,
                        PctInicio = a.PctAtual.Value, PctFim = pct, Deduzida = true
                    };
                    a.Atual = NovaDescarga(agora, pct, false); a.PassoUso = 0; a.PassoValido = false;
                    MarcarParaGravar(true);
                }
                else if (a.Atual == null)
                {
                    a.Atual = NovaDescarga(agora, pct, true); a.PassoUso = 0; a.PassoValido = false;
                    MarcarParaGravar(false);
                }
                else if (continuo)
                {
                    bool assentando = !a.Atual.Parcial && a.Atual.UsoSegundos < AssentamentoSeg;
                    a.Atual.UsoSegundos += dt;
                    a.UsoTotalSeg += dt;

                    // O piso e a menor leitura desta descarga: o G325 volta um
                    // degrau sozinho em repouso, e contar a nova descida de novo
                    // cobraria os mesmos dez pontos duas vezes. Cada queda abaixo
                    // do piso fecha um degrau; veja Aparelho.PassoUso.
                    int piso = a.Atual.PctFim;
                    if (assentando) { a.PassoUso = 0; a.PassoValido = false; }
                    else
                    {
                        a.PassoUso += dt;
                        if (pct < piso)
                        {
                            if (a.PassoValido) CreditarDescarga(a, agora, piso, pct, a.PassoUso);
                            a.PassoUso = 0; a.PassoValido = true;
                        }
                    }

                    a.Atual.Fim = agora;
                    a.Atual.PctFim = Math.Min(a.Atual.PctFim, pct);
                    if (assentando) a.Atual.PctInicio = Math.Min(a.Atual.PctInicio, pct);
                    if (pct != a.PctAtual) MarcarParaGravar(false);
                }
                else if (dt > LacunaMaxSeg)
                {
                    // o aparelho sumiu por um tempo: o degrau em curso nao tem inicio nem fim conhecidos
                    a.PassoValido = false;
                    a.Atual.PctFim = Math.Min(a.Atual.PctFim, pct);
                }
            }

            a.PctAtual = pct;
            a.UltimaLeitura = agora;
            Amostrar(a, agora, pct, p.Carregando, false);
        }

        /// <summary>
        /// Um degrau completo: "seg" segundos de uso para ir de "de" a "ate".
        /// Cada ponto leva a parte dele, na faixa dele, depois de a faixa
        /// esquecer um pouco do que mediu antes e de o degrau fora do comum
        /// ser limitado.
        /// </summary>
        private static void CreditarDescarga(Aparelho a, DateTime agora, int de, int ate, double seg)
        {
            int n = de - ate;
            if (n <= 0 || seg <= 0) return;
            double porPonto = seg / n, soma = 0;
            for (int q = ate + 1; q <= de; q++)
            {
                int k = Faixa(q);
                double x = porPonto;
                if (a.FaixaPts[k] >= PontosParaLimitar)
                {
                    double media = a.FaixaSeg[k] / a.FaixaPts[k];
                    x = Math.Max(media * LimiteInferior, Math.Min(media * LimiteSuperior, x));
                }
                a.FaixaSeg[k] = a.FaixaSeg[k] * Esquecimento + x;
                a.FaixaPts[k] = a.FaixaPts[k] * Esquecimento + 1;
                soma += x;
            }

            double[] mes;
            string chaveMes = agora.ToString("yyyyMM", CultureInfo.InvariantCulture);
            if (!a.Meses.TryGetValue(chaveMes, out mes)) { mes = new double[2]; a.Meses[chaveMes] = mes; }
            mes[0] += soma;
            mes[1] += n;
        }

        private static Descarga NovaDescarga(DateTime agora, int pct, bool parcial)
        {
            return new Descarga { Inicio = agora, Fim = agora, PctInicio = pct, PctFim = pct, Parcial = parcial };
        }

        /// <summary>Encerra a descarga em andamento e guarda se ela disse alguma coisa.</summary>
        private static void FecharDescarga(Aparelho a, DateTime agora)
        {
            Descarga d = a.Atual;
            a.Atual = null;
            if (d == null || d.Gasto < 3) return;
            a.Descargas.Add(d);
            if (a.Descargas.Count > DescargasGuardadas) a.Descargas.RemoveAt(0);
        }

        private static void Amostrar(Aparelho a, DateTime agora, int pct, bool carregando, bool forcar)
        {
            int n = a.Amostras.Count;
            // Relogio que voltou (fim do horario de verao, ajuste manual): as
            // amostras "do futuro" saem. Antes a diferenca negativa calava a
            // amostragem ate a hora alcancar a ultima - uma hora sem curva.
            while (n > 0 && a.Amostras[n - 1].Quando > agora) { a.Amostras.RemoveAt(n - 1); n--; }
            if (!forcar && n > 0 && agora - a.Amostras[n - 1].Quando < IntervaloDeAmostra) return;
            a.Amostras.Add(new AmostraDeBateria { Quando = agora, Pct = pct, Carregando = carregando });
            DateTime corte = agora - JanelaDeAmostras;
            int velhas = 0;
            while (velhas < a.Amostras.Count && a.Amostras[velhas].Quando < corte) velhas++;
            if (velhas > 0) a.Amostras.RemoveRange(0, velhas);
            MarcarParaGravar(false);
        }

        private static void MarcarParaGravar(bool agora)
        {
            _sujo = true;
            if (agora) _ultimaGravacao = DateTime.MinValue;   // grava no fim deste ciclo
        }

        // ---------------- analise ----------------

        /// <summary>Os aparelhos com historico, os que estao vivos agora primeiro.</summary>
        public static List<AnaliseDeBateria> Analisar(Periferico[] atual)
        {
            List<AnaliseDeBateria> l = new List<AnaliseDeBateria>();
            lock (_trava)
            {
                CarregarSePreciso();
                Dictionary<string, Periferico> vivos = new Dictionary<string, Periferico>();
                if (atual != null)
                    foreach (Periferico p in atual)
                        if (p != null && p.Fonte != FontePeriferico.Bluetooth && (p.Bateria.HasValue || _aparelhos.ContainsKey(Chave(p))))
                            vivos[Chave(p)] = p;

                foreach (Aparelho a in _aparelhos.Values)
                    if (a.Fonte != FontePeriferico.Bluetooth) l.Add(Analisar(a, vivos));
            }
            l.Sort(delegate(AnaliseDeBateria x, AnaliseDeBateria y)
            {
                int vx = x.PctAtual.HasValue ? 0 : 1, vy = y.PctAtual.HasValue ? 0 : 1;
                if (vx != vy) return vx.CompareTo(vy);
                int f = x.Fonte.CompareTo(y.Fonte);
                if (f != 0) return f;
                int t = x.Tipo.CompareTo(y.Tipo);
                return t != 0 ? t : string.Compare(x.Nome, y.Nome, StringComparison.CurrentCultureIgnoreCase);
            });
            return l;
        }

        private static AnaliseDeBateria Analisar(Aparelho a, Dictionary<string, Periferico> vivos)
        {
            AnaliseDeBateria r = new AnaliseDeBateria();
            r.Chave = a.Chave; r.Nome = a.Nome; r.Tipo = a.Tipo; r.Fonte = a.Fonte;

            Periferico vivo;
            if (vivos.TryGetValue(a.Chave, out vivo) && vivo.Bateria.HasValue)
            {
                r.PctAtual = vivo.Bateria;
                r.Carregando = vivo.Carregando;
            }
            if (a.Carregando && r.Carregando)
            {
                r.CarregandoDesde = a.CargaInicio;
                r.CarregandoDe = a.CargaPctInicio;
            }

            r.Atual = Copia(a.Atual);
            r.UltimaCarga = a.UltimaCarga;
            for (int i = a.Descargas.Count - 1; i >= 0 && r.UltimaCompleta == null; i--)
                if (!a.Descargas[i].Parcial) r.UltimaCompleta = a.Descargas[i];
            if (r.UltimaCompleta == null && a.Descargas.Count > 0) r.UltimaCompleta = a.Descargas[a.Descargas.Count - 1];

            int n = 0;
            List<Descarga> todas = new List<Descarga>(a.Descargas);
            if (a.Atual != null) todas.Add(a.Atual);
            foreach (Descarga d in todas)
                if (d.Gasto >= GastoMinimo && d.UsoSegundos >= UsoMinimoSeg) n++;
            r.DescargasMedidas = n;
            r.UsoTotalHoras = a.UsoTotalSeg / 3600.0;

            double[] ritmo = RitmoPorPonto(a);
            if (ritmo != null)
            {
                r.HorasPorCargaCheia = Somar(ritmo, 100) / 3600.0;
                if (!r.Carregando && r.PctAtual.HasValue)
                    r.HorasRestantes = RestanteSeg(a, ritmo, r.PctAtual.Value) / 3600.0;
            }
            double gastos = 0;
            foreach (KeyValuePair<string, double[]> m in a.Meses) gastos += m.Value[1];
            r.CiclosEquivalentes = gastos / 100.0;
            foreach (KeyValuePair<string, double[]> m in a.Meses)
            {
                if (m.Value[1] < PontosPorMes) continue;
                DateTime quando = DateTime.ParseExact(m.Key, "yyyyMM", CultureInfo.InvariantCulture);
                r.SaudePorMes.Add(new KeyValuePair<DateTime, double>(quando, m.Value[0] / m.Value[1] * 100.0 / 3600.0));
            }
            if (r.Carregando && r.PctAtual.HasValue)
            {
                double[] carga = RitmoDeCarga(a);
                if (r.PctAtual.Value >= 100) r.HorasAteEncher = 0;
                else if (carga != null)
                {
                    r.HorasAteEncher = AteEncherSeg(a, carga, r.PctAtual.Value) / 3600.0;
                }
            }

            r.HorasPorPontoNaFaixa = new double?[Faixas];
            for (int k = 0; k < Faixas; k++)
                if (a.FaixaPts[k] > 0) r.HorasPorPontoNaFaixa[k] = a.FaixaSeg[k] / a.FaixaPts[k] / 3600.0;

            r.Amostras = a.Amostras.ToArray();
            return r;
        }

        /// <summary>
        /// Segundos de uso por ponto em cada faixa, com cada faixa puxada para a
        /// media do aparelho na medida em que mediu pouco. Nulo enquanto o
        /// aparelho inteiro nao gastou o bastante para dizer alguma coisa.
        /// </summary>
        private static double[] RitmoPorPonto(Aparelho a)
        {
            double seg = 0; double pts = 0;
            for (int k = 0; k < Faixas; k++) { seg += a.FaixaSeg[k]; pts += a.FaixaPts[k]; }

            double media;
            if (pts >= GastoMinimo && seg >= UsoMinimoSeg) media = seg / pts;
            else
            {
                // Historico gravado antes das faixas existirem: so as descargas.
                double uso = 0; int gasto = 0;
                List<Descarga> todas = new List<Descarga>(a.Descargas);
                if (a.Atual != null) todas.Add(a.Atual);
                foreach (Descarga d in todas)
                {
                    if (d.Gasto < GastoMinimo || d.UsoSegundos < UsoMinimoSeg) continue;
                    uso += d.UsoSegundos; gasto += d.Gasto;
                }
                if (gasto == 0) return null;
                media = uso / gasto;
            }

            double[] r = new double[Faixas];
            for (int k = 0; k < Faixas; k++)
                r[k] = (a.FaixaSeg[k] + media * PesoDaMedia) / (a.FaixaPts[k] + PesoDaMedia);
            return r;
        }

        /// <summary>Segundos para gastar do ponto "de" ate zero, faixa por faixa.</summary>
        private static double Somar(double[] ritmo, int de)
        {
            double s = 0;
            for (int q = Math.Min(100, de); q >= 1; q--) s += ritmo[Faixa(q)];
            return s;
        }

        /// <summary>
        /// Tamanho do degrau da leitura: 10 quando as amostras recentes so caem
        /// em multiplos de 10 (G325), 1 nas fontes que dao a porcentagem exata.
        /// </summary>
        private static int Granularidade(Aparelho a)
        {
            if (a.PctAtual.HasValue && a.PctAtual.Value % 10 != 0) return 1;
            HashSet<int> vistos = new HashSet<int>();
            foreach (AmostraDeBateria s in a.Amostras)
            {
                if (s.Pct % 10 != 0) return 1;
                vistos.Add(s.Pct);
            }
            return vistos.Count >= 3 ? 10 : 1;
        }

        /// <summary>
        /// Segundos de uso que restam a partir da leitura "pct". O degrau em
        /// que a leitura esta (10 pontos no G325) ja gastou parte do tempo: o
        /// que falta dele e o usual menos o decorrido, nunca menos que uma
        /// fracao do usual - passar do tempo normal nao quer dizer que acabou.
        /// </summary>
        private static double RestanteSeg(Aparelho a, double[] ritmo, int pct)
        {
            int r = pct;
            double decorrido = 0;
            Descarga d = a.Atual;
            if (d != null)
            {
                // leitura um degrau acima do piso e o aparelho descansando, e nao carga
                if (d.PctFim < pct && pct - d.PctFim < SubidaDeCarga) r = d.PctFim;
                if (r == d.PctFim) decorrido = a.PassoUso;
            }
            int topo = Math.Min(100, Math.Max(0, r));
            int baixo = Math.Max(0, topo - Granularidade(a));
            double degrau = Somar(ritmo, topo) - Somar(ritmo, baixo);
            return Somar(ritmo, baixo) + Math.Max(degrau - decorrido, degrau * FracaoMinimaDoDegrau);
        }

        /// <summary>Segundos no cabo ate 100% a partir da leitura "pct", com o degrau atual descontado como em RestanteSeg.</summary>
        private static double AteEncherSeg(Aparelho a, double[] carga, int pct)
        {
            int cima = Math.Min(100, pct + Granularidade(a));
            double degrau = 0, resto = 0;
            for (int q = pct + 1; q <= 100; q++)
            {
                if (q <= cima) degrau += carga[Faixa(q)]; else resto += carga[Faixa(q)];
            }
            double decorrido = pct == a.CargaTeto ? a.CargaPassoSeg : 0;
            return resto + Math.Max(degrau - decorrido, degrau * FracaoMinimaDoDegrau);
        }

        /// <summary>
        /// Segundos no cabo por ponto ganho em cada faixa, como RitmoPorPonto
        /// faz para a descarga. Nulo ate o aparelho ter subido 5 pontos em 5
        /// minutos de carga observada.
        /// </summary>
        private static double[] RitmoDeCarga(Aparelho a)
        {
            double seg = 0; int pts = 0;
            for (int k = 0; k < Faixas; k++) { seg += a.CargaSeg[k]; pts += a.CargaPts[k]; }
            if (pts < 5 || seg < 300) return null;
            double media = seg / pts;
            double[] r = new double[Faixas];
            for (int k = 0; k < Faixas; k++)
                r[k] = (a.CargaSeg[k] + media * PesoDaMedia) / (a.CargaPts[k] + PesoDaMedia);
            return r;
        }

        /// <summary>Horas que faltam para 100% no cabo; nulo fora do cabo ou enquanto mede.</summary>
        public static double? AteEncher(Periferico p)
        {
            if (p == null || !p.Bateria.HasValue || !p.Carregando) return null;
            if (p.Bateria.Value >= 100) return 0;
            lock (_trava)
            {
                CarregarSePreciso();
                Aparelho a;
                if (!_aparelhos.TryGetValue(Chave(p), out a)) return null;
                double[] r = RitmoDeCarga(a);
                if (r == null) return null;
                return AteEncherSeg(a, r, p.Bateria.Value) / 3600.0;
            }
        }

        /// <summary>Todo aparelho que o historico ja viu, como periferico desligado.</summary>
        public static List<Periferico> Conhecidos()
        {
            List<Periferico> l = new List<Periferico>();
            lock (_trava)
            {
                CarregarSePreciso();
                foreach (Aparelho a in _aparelhos.Values)
                    if (!string.IsNullOrEmpty(a.Nome))
                        l.Add(Periferico.Sem(a.Nome, a.Tipo, a.Fonte, EstadoPeriferico.Desligado));
            }
            return l;
        }

        /// <summary>Horas que ainda restam ao aparelho, para a faixa da Visao geral; nulo enquanto mede.</summary>
        public static double? Restante(Periferico p)
        {
            if (p == null || !p.Bateria.HasValue || p.Carregando) return null;
            lock (_trava)
            {
                CarregarSePreciso();
                Aparelho a;
                if (!_aparelhos.TryGetValue(Chave(p), out a)) return null;
                double[] ritmo = RitmoPorPonto(a);
                return ritmo != null ? (double?)(RestanteSeg(a, ritmo, p.Bateria.Value) / 3600.0) : null;
            }
        }

        private static Descarga Copia(Descarga d)
        {
            if (d == null) return null;
            return new Descarga { Inicio = d.Inicio, Fim = d.Fim, PctInicio = d.PctInicio, PctFim = d.PctFim,
                                  UsoSegundos = d.UsoSegundos, Parcial = d.Parcial };
        }

        /// <summary>
        /// Duracao de uma PREVISAO: "~41 h" diz o mesmo que "~40 h 41 min" sem
        /// fingir uma precisao que a estimativa nao tem. Abaixo de 10 h os
        /// minutos voltam, de 10 em 10; abaixo de 1 h, de 5 em 5.
        /// </summary>
        public static string DuracaoAproximada(double horas)
        {
            if (horas < 0) horas = 0;
            if (horas < 1) return Duracao(Math.Max(5, Math.Round(horas * 60 / 5) * 5) / 60.0);
            if (horas < 10) return Duracao(Math.Round(horas * 6) / 6.0);
            return Duracao(Math.Round(horas));
        }

        /// <summary>"45 min", "12 h 30 min", "3 d 4 h".</summary>
        public static string Duracao(double horas)
        {
            if (horas < 0) horas = 0;
            int min = (int)Math.Round(horas * 60);
            if (min < 60) return min + " min";
            if (min < 48 * 60)
            {
                int h = min / 60, m = min % 60;
                return m == 0 ? h + " h" : h + " h " + m + " min";
            }
            int dias = min / (24 * 60), resto = (min / 60) % 24;
            return resto == 0 ? dias + " d" : dias + " d " + resto + " h";
        }

        // ---------------- disco ----------------

        /// <summary>
        /// Le o arquivo uma vez. So conta como lido quando a leitura deu certo.
        ///
        /// Marcava lido ANTES de ler. Com o arquivo preso no logon - antivirus,
        /// sincronizador -, a leitura falhava, a memoria ficava vazia e a
        /// primeira gravacao trocava meses de historico pelo que tinha sido
        /// visto naquela sessao. Agora a falha deixa tudo como estava: nada e
        /// registrado nem gravado ate o arquivo abrir, o que o proximo ciclo
        /// tenta de novo.
        /// </summary>
        private static void CarregarSePreciso()
        {
            if (_carregado) return;
            try
            {
                if (File.Exists(Caminho))
                {
                    string[] linhas = File.ReadAllLines(Caminho, Encoding.UTF8);
                    _aparelhos.Clear();
                    foreach (string linha in linhas)
                        try { Ler(linha); } catch { /* uma linha estragada nao derruba o resto */ }

                    // arquivo de antes do uso total: soma o que as descargas guardaram
                    foreach (Aparelho a in _aparelhos.Values)
                        if (a.UsoTotalSeg <= 0)
                        {
                            foreach (Descarga d in a.Descargas) a.UsoTotalSeg += d.UsoSegundos;
                            if (a.Atual != null) a.UsoTotalSeg += a.Atual.UsoSegundos;
                        }
                }
                _carregado = true;
                _falhaAvisada = false;
            }
            catch (Exception ex)
            {
                _aparelhos.Clear();
                // um aviso por falha, e nao um por segundo enquanto durar
                if (!_falhaAvisada) Log.Error("leitura do historico de bateria; nada sera gravado ate ler", ex);
                _falhaAvisada = true;
            }
        }

        private static bool _falhaAvisada = false;

        private static DateTime D(string s) { return new DateTime(long.Parse(s, CultureInfo.InvariantCulture), DateTimeKind.Local); }
        private static int I(string s) { return int.Parse(s, CultureInfo.InvariantCulture); }
        private static string S(DateTime d) { return d.Ticks.ToString(CultureInfo.InvariantCulture); }
        private static string S(int i) { return i.ToString(CultureInfo.InvariantCulture); }

        private static Aparelho Do(string chave)
        {
            Aparelho a;
            if (!_aparelhos.TryGetValue(chave, out a))
            {
                a = new Aparelho();
                a.Chave = chave;
                _aparelhos[chave] = a;
            }
            return a;
        }

        private static void Ler(string linha)
        {
            string[] c = linha.Split('\t');
            if (c.Length < 2) return;
            Aparelho a = Do(c[1]);
            switch (c[0])
            {
                case "aparelho":
                    a.Nome = c[2];
                    a.Tipo = (TipoPeriferico)Enum.Parse(typeof(TipoPeriferico), c[3]);
                    a.Fonte = (FontePeriferico)Enum.Parse(typeof(FontePeriferico), c[4]);
                    a.PctAtual = c[5].Length > 0 ? (int?)I(c[5]) : null;
                    a.UltimaLeitura = D(c[6]);
                    a.Carregando = c[7] == "1";
                    a.CargaInicio = D(c[8]);
                    a.CargaPctInicio = I(c[9]);
                    break;
                case "atual":
                case "descarga":
                    Descarga d = new Descarga
                    {
                        Inicio = D(c[2]), Fim = D(c[3]), PctInicio = I(c[4]), PctFim = I(c[5]),
                        UsoSegundos = double.Parse(c[6], CultureInfo.InvariantCulture), Parcial = c[7] == "1"
                    };
                    if (c[0] == "atual") a.Atual = d; else a.Descargas.Add(d);
                    break;
                case "carga":
                    a.UltimaCarga = new Carga
                    {
                        Inicio = D(c[2]), Fim = D(c[3]), PctInicio = I(c[4]), PctFim = I(c[5]), Deduzida = c[6] == "1"
                    };
                    break;
                case "amostra":
                    a.Amostras.Add(new AmostraDeBateria { Quando = D(c[2]), Pct = I(c[3]), Carregando = c[4] == "1" });
                    break;
                case "faixas":
                    a.UsoTotalSeg = double.Parse(c[2], CultureInfo.InvariantCulture);
                    for (int k = 0; k < Faixas; k++)
                    {
                        a.FaixaSeg[k] = double.Parse(c[3 + k], CultureInfo.InvariantCulture);
                        a.FaixaPts[k] = double.Parse(c[3 + Faixas + k], CultureInfo.InvariantCulture);
                    }
                    break;
                case "mes":
                    a.Meses[c[2]] = new double[] {
                        double.Parse(c[3], CultureInfo.InvariantCulture), double.Parse(c[4], CultureInfo.InvariantCulture) };
                    break;
                case "carga_faixas":
                    a.CargaTeto = I(c[2]);
                    for (int k = 0; k < Faixas; k++)
                    {
                        a.CargaSeg[k] = double.Parse(c[3 + k], CultureInfo.InvariantCulture);
                        a.CargaPts[k] = I(c[3 + Faixas + k]);
                    }
                    break;
            }
        }

        private static void Escrever(StringBuilder sb, string tipo, string chave, Descarga d)
        {
            sb.Append(tipo).Append('\t').Append(chave).Append('\t').Append(S(d.Inicio)).Append('\t').Append(S(d.Fim))
              .Append('\t').Append(S(d.PctInicio)).Append('\t').Append(S(d.PctFim))
              .Append('\t').Append(d.UsoSegundos.ToString("0", CultureInfo.InvariantCulture))
              .Append('\t').Append(d.Parcial ? "1" : "0").Append('\n');
        }

        /// <summary>Grava agora, se houver o que gravar. Chamado tambem no encerramento.</summary>
        public static void Salvar() { Salvar(DateTime.Now); }

        private static void Salvar(DateTime agora)
        {
            string texto;
            lock (_trava)
            {
                // sem a leitura do arquivo, gravar seria apagar o que ele guarda
                if (!_sujo || !_carregado) return;
                StringBuilder sb = new StringBuilder();
                sb.Append("# historico de bateria do Mhiagos Control, v1\n");
                foreach (Aparelho a in _aparelhos.Values)
                {
                    if (string.IsNullOrEmpty(a.Nome)) continue;
                    sb.Append("aparelho\t").Append(a.Chave).Append('\t').Append(a.Nome.Replace('\t', ' '))
                      .Append('\t').Append(a.Tipo).Append('\t').Append(a.Fonte)
                      .Append('\t').Append(a.PctAtual.HasValue ? S(a.PctAtual.Value) : "")
                      .Append('\t').Append(S(a.UltimaLeitura)).Append('\t').Append(a.Carregando ? "1" : "0")
                      .Append('\t').Append(S(a.CargaInicio)).Append('\t').Append(S(a.CargaPctInicio)).Append('\n');
                    sb.Append("faixas\t").Append(a.Chave).Append('\t').Append(a.UsoTotalSeg.ToString("0", CultureInfo.InvariantCulture));
                    for (int k = 0; k < Faixas; k++) sb.Append('\t').Append(a.FaixaSeg[k].ToString("0.##", CultureInfo.InvariantCulture));
                    for (int k = 0; k < Faixas; k++) sb.Append('\t').Append(a.FaixaPts[k].ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append('\n');
                    foreach (KeyValuePair<string, double[]> m in a.Meses)
                        sb.Append("mes\t").Append(a.Chave).Append('\t').Append(m.Key)
                          .Append('\t').Append(m.Value[0].ToString("0.##", CultureInfo.InvariantCulture))
                          .Append('\t').Append(m.Value[1].ToString("0", CultureInfo.InvariantCulture)).Append('\n');
                    sb.Append("carga_faixas\t").Append(a.Chave).Append('\t').Append(S(a.CargaTeto));
                    for (int k = 0; k < Faixas; k++) sb.Append('\t').Append(a.CargaSeg[k].ToString("0.##", CultureInfo.InvariantCulture));
                    for (int k = 0; k < Faixas; k++) sb.Append('\t').Append(S(a.CargaPts[k]));
                    sb.Append('\n');
                    if (a.Atual != null) Escrever(sb, "atual", a.Chave, a.Atual);
                    foreach (Descarga d in a.Descargas) Escrever(sb, "descarga", a.Chave, d);
                    if (a.UltimaCarga != null)
                        sb.Append("carga\t").Append(a.Chave).Append('\t').Append(S(a.UltimaCarga.Inicio))
                          .Append('\t').Append(S(a.UltimaCarga.Fim)).Append('\t').Append(S(a.UltimaCarga.PctInicio))
                          .Append('\t').Append(S(a.UltimaCarga.PctFim)).Append('\t').Append(a.UltimaCarga.Deduzida ? "1" : "0").Append('\n');
                    foreach (AmostraDeBateria s in a.Amostras)
                        sb.Append("amostra\t").Append(a.Chave).Append('\t').Append(S(s.Quando))
                          .Append('\t').Append(S(s.Pct)).Append('\t').Append(s.Carregando ? "1" : "0").Append('\n');
                }
                texto = sb.ToString();
                _sujo = false;
                _ultimaGravacao = agora;
            }

            // temporario + troca: um desligamento no meio da escrita nao deixa
            // o historico pela metade
            try
            {
                GravacaoAtomica.Escrever(Caminho, texto, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Error("gravacao do historico de bateria", ex);
                lock (_trava) _sujo = true;
            }
        }

        /// <summary>Esquece tudo; so para os testes.</summary>
        internal static void Zerar()
        {
            lock (_trava)
            {
                _aparelhos.Clear();
                _carregado = true;
                _sujo = false;
                _ultimaGravacao = DateTime.MinValue;
            }
        }

        /// <summary>Relê do disco; so para os testes.</summary>
        internal static void Recarregar()
        {
            lock (_trava)
            {
                _aparelhos.Clear();
                _carregado = false;
                CarregarSePreciso();
            }
        }
    }
}
