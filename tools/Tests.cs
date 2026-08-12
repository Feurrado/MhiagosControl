using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    /// <summary>
    /// Testes do que nao se conserta olhando a tela.
    ///
    /// Sem framework de propósito: o projeto compila com o csc.exe que vem no
    /// Windows, sem SDK e sem NuGet, e uma dependencia de teste custaria
    /// exatamente a propriedade que torna este projeto facil de compilar. O que
    /// sobra e um console com assercoes a mao, codigo de saida e nada mais.
    ///
    /// O foco e o que a interface nao denuncia: a montagem do quadro do
    /// protocolo, a preparacao do valor para tres digitos, a ida e volta da
    /// configuracao e a completude das traducoes.
    ///
    ///   powershell -ExecutionPolicy Bypass -File .\tools\build-tests.ps1
    /// </summary>
    public static class Tests
    {
        private static int _ok, _falhas;

        public static int Main()
        {
            QuadroDoPainel();
            PreparoDoValor();
            Formatacao();
            IdaEVoltaDaConfiguracao();
            TodosOsCamposDoPerfil();
            RodizioDePerfis();
            NomesDoSistema();
            NomesAmigaveis();
            LarguraDasPaginas();
            ConjuntosDeMetricas();
            RetratoDaMaquina();
            FolhaDeEspecificacoes();
            HistoricoDasMetricas();
            QuadrosPorSegundo();
            CompletudeDoIdioma();

            Console.WriteLine();
            Console.WriteLine(_falhas == 0
                ? string.Format("{0} verificacoes, todas passaram", _ok)
                : string.Format("{0} verificacoes, {1} FALHARAM", _ok + _falhas, _falhas));
            return _falhas == 0 ? 0 : 1;
        }

        // ---------------- protocolo ----------------

        /// <summary>
        /// O quadro de 64 bytes e a unica coisa aqui que o hardware le. Um
        /// digito no lugar errado nao quebra nada em tela - so mostra o numero
        /// errado no cooler, que e onde ninguem esta olhando quando programa.
        /// </summary>
        private static void QuadroDoPainel()
        {
            Secao("quadro do painel");

            byte[] q = Montar(51, 16, false, true);
            Igual(0x07, q[0], "ReportID");
            Igual(0, q[1], "centena do painel 1 em 51");
            Igual(5, q[2], "dezena do painel 1 em 51");
            Igual(1, q[3], "unidade do painel 1 em 51");
            Igual(0, q[5], "centena do painel 2 em 16");
            Igual(1, q[6], "dezena do painel 2 em 16");
            Igual(6, q[7], "unidade do painel 2 em 16");

            // As flags sao independentes: as quatro combinacoes sao validas
            Igual(0x00, Montar(1, 1, false, false)[4], "flags sem Fahrenheit nem porcentagem");
            Igual(0x01, Montar(1, 1, true, false)[4], "flag de Fahrenheit");
            Igual(0x10, Montar(1, 1, false, true)[4], "flag de porcentagem");
            Igual(0x11, Montar(1, 1, true, true)[4], "as duas flags juntas");

            // Apagado e diferente de zero: zero passaria por medicao legitima
            byte[] vazio = Montar(null, null, false, false);
            Igual(HidPanel.DIGIT_BLANK, vazio[1], "painel 1 apagado");
            Igual(HidPanel.DIGIT_BLANK, vazio[7], "painel 2 apagado");
            Verdade(vazio[1] != 0, "apagado nao pode ser o digito zero");

            Igual(9, Montar(1500, 0, false, false)[1], "acima de 999 limita na centena");
            Igual(9, Montar(1500, 0, false, false)[3], "acima de 999 limita na unidade");
            Igual(0, Montar(-40, 0, false, false)[3], "negativo vira zero");

            byte[] cheio = Montar(999, 999, false, false);
            for (int i = 8; i < 64; i++) Igual(0, cheio[i], "cauda zerada no byte " + i);
            Igual(64, cheio.Length, "tamanho do quadro");
        }

        /// <summary>Monta o quadro pelo mesmo caminho do aplicativo, sem hardware.</summary>
        private static byte[] Montar(int? p1, int? p2, bool fahrenheit, bool percent)
        {
            byte[] b = new byte[64];
            b[0] = 0x07;
            Escrever(b, 1, p1);
            byte flags = 0;
            if (fahrenheit) flags |= HidPanel.FLAG_FAHRENHEIT;
            if (percent) flags |= HidPanel.FLAG_PERCENT;
            b[4] = flags;
            Escrever(b, 5, p2);
            return b;
        }

        private static void Escrever(byte[] b, int off, int? valor)
        {
            MethodInfo m = typeof(HidPanel).GetMethod("WriteField",
                BindingFlags.NonPublic | BindingFlags.Static);
            m.Invoke(null, new object[] { b, off, valor });
        }

        // ---------------- escala ----------------

        private static void PreparoDoValor()
        {
            Secao("preparo do valor");

            Igual(51, Preparar(SensorType.Temperature, 51.3f, 1, false), "arredonda para baixo");
            Igual(52, Preparar(SensorType.Temperature, 51.6f, 1, false), "arredonda para cima");

            // Fahrenheit so em temperatura: o bit do protocolo apenas acende o
            // simbolo, entao converter uma carga daria um numero sem sentido
            Igual(124, Preparar(SensorType.Temperature, 51.3f, 1, true), "51,3 C vira 124 F");
            Igual(51, Preparar(SensorType.Load, 51.3f, 1, true), "carga nao vira Fahrenheit");
            Igual(51, Preparar(SensorType.Power, 51.3f, 1, true), "potencia nao vira Fahrenheit");

            Igual(370, Preparar(SensorType.Clock, 3700f, 10, false), "clock dividido por 10");
            Igual(48, Preparar(SensorType.Throughput, 48000f, 1000, false), "throughput em KB/s");

            PanelValue estourado = Scaling.Prepare(Sensor(SensorType.Clock, 5000f), 1, false);
            Igual(999, estourado.Value.Value, "acima de 999 limita");
            Verdade(estourado.Clamped, "limite marcado em Clamped");

            SemLeitura(Scaling.Prepare(null, 1, false), "sensor ausente");
            SemLeitura(Scaling.Prepare(Sensor(SensorType.Temperature, float.NaN), 1, false), "NaN");
            SemLeitura(Scaling.Prepare(Sensor(SensorType.Temperature, float.PositiveInfinity), 1, false), "infinito");
            SemLeitura(Scaling.Prepare(new SensorEntry(), 1, false), "sem valor");

            Igual(10, Scaling.Effective(0, Sensor(SensorType.Clock, 1f)), "divisor automatico do clock");
            Igual(1, Scaling.Effective(0, Sensor(SensorType.Temperature, 1f)), "divisor automatico da temperatura");
            Igual(100, Scaling.Effective(100, Sensor(SensorType.Clock, 1f)), "divisor do perfil vence o automatico");
        }

        private static int Preparar(SensorType t, float v, int divisor, bool fahrenheit)
        {
            PanelValue p = Scaling.Prepare(Sensor(t, v), divisor, fahrenheit);
            return p.Value.HasValue ? p.Value.Value : -1;
        }

        private static void SemLeitura(PanelValue p, string caso)
        {
            Verdade(!p.Value.HasValue, "mostrador apagado: " + caso);
        }

        private static SensorEntry Sensor(SensorType t, float v)
        {
            SensorEntry e = new SensorEntry();
            e.Id = "teste"; e.Name = "teste"; e.Type = t; e.Value = v;
            return e;
        }

        // ---------------- formatacao ----------------

        private static void Formatacao()
        {
            Secao("formatacao da leitura");

            // Gigabyte sempre com uma casa: "12 GB" e "12,4 GB" lado a lado na
            // mesma lista fazem a coluna dancar
            Igual("11.6 GB", Formatar(SensorType.Data, 11.64f, "GB"), "GB com uma casa");
            Igual("12.0 GB", Formatar(SensorType.Data, 12f, "GB"), "GB inteiro mantem a casa");
            Igual("51.3 C", Formatar(SensorType.Temperature, 51.3f, "C"), "temperatura");
            Igual("1600 MHz", Formatar(SensorType.Clock, 1600f, "MHz"), "clock sem casa desnecessaria");

            // Sem unidade da fonte, o tipo decide
            Igual("58.2 %", Formatar(SensorType.Load, 58.2f, null), "unidade vinda do tipo");

            SensorEntry vazio = Sensor(SensorType.Temperature, float.NaN);
            Igual("-", vazio.Formatted, "NaN nao vira numero");
        }

        private static string Formatar(SensorType t, float v, string unidade)
        {
            SensorEntry e = Sensor(t, v);
            e.Unit = unidade;
            return e.Formatted;
        }

        // ---------------- configuracao ----------------

        /// <summary>
        /// A configuracao e um INI escrito e lido a mao. Um perfil cujo nome
        /// tem "=" ou acento e o caso que quebraria em silencio: o usuario
        /// perderia o perfil e nao saberia por que.
        /// </summary>
        /// <summary>
        /// O encurtamento dos nomes na barra lateral.
        ///
        /// Existe porque a primeira versao errou: removia "AMD " do comeco, mas
        /// o HWiNFO publica "CPU [#0]: AMD Ryzen 5 5600X" e o comeco era o
        /// prefixo de enumeracao. Nada era removido, e a coluna mostrava
        /// "CPU [#0]: AMD Ryzen 5 ..." - a linha inteira gasta antes do modelo.
        /// </summary>
        private static void NomesDoSistema()
        {
            Secao("nomes do sistema na barra lateral");

            Igual("Ryzen 5 5600X", SystemInfo.Limpar("CPU [#0]: AMD Ryzen 5 5600X"),
                  "prefixo de enumeracao do HWiNFO e fabricante");
            Igual("Radeon RX 7800 XT", SystemInfo.Limpar("GPU [#0]: AMD Radeon RX 7800 XT"),
                  "o mesmo na placa de video");
            Igual("Ryzen 5 5600X", SystemInfo.Limpar("AMD Ryzen 5 5600X 6-Core Processor"),
                  "formato da LibreHardwareMonitor");
            Igual("Core i7-12700K", SystemInfo.Limpar("Intel(R) Core i7-12700K CPU @ 3.60GHz"),
                  "sufixo de frequencia da Intel");
            Igual("GeForce RTX 3060", SystemInfo.Limpar("NVIDIA GeForce RTX 3060"),
                  "fabricante da placa de video");
            Igual("Radeon RX 580", SystemInfo.Limpar("GPU [#0]: AMD Radeon RX 580: Sapphire RX 580 Pulse"),
                  "chip e placa do parceiro repetiam o modelo");

            // Nao inventar conteudo: nome que nao casa com nenhum padrao passa
            // inteiro, porque um nome estranho ainda identifica a peca melhor
            // do que um pedaco escolhido por chute.
            Igual("Alguma Peca Exotica", SystemInfo.Limpar("Alguma Peca Exotica"),
                  "nome fora dos padroes passa inteiro");
            Igual(null, SystemInfo.Limpar("   "), "so espaco vira nulo");
            Igual(null, SystemInfo.Limpar(null), "nulo continua nulo");

            // A selecao automatica exigia "°C" literal e voltava sem nenhuma
            // temperatura - a leitura mais basica faltando na grade que existe
            // para dar o basico. As fontes escrevem o grau de formas diferentes.
            Igual("C", MetricPicker.Normalizar("°C"), "grau com simbolo");
            Igual("C", MetricPicker.Normalizar("C"), "grau sem simbolo");
            Igual("C", MetricPicker.Normalizar(" degC "), "grau por extenso");
            Igual("C", MetricPicker.Normalizar("Â°C"), "grau mastigado pela pagina de codigo");
            Igual("%", MetricPicker.Normalizar("%"), "porcentagem");
            Igual("MHZ", MetricPicker.Normalizar("MHz"), "frequencia ignora caixa");
            Igual("RPM", MetricPicker.Normalizar("rpm"), "rotacao ignora caixa");
            Igual("", MetricPicker.Normalizar(null), "nulo vira vazio");

            float? at, pe;
            MetricPicker.Faixas("C", out at, out pe);
            Verdade(at.HasValue && at.Value == 80, "faixa de atencao vale para C sem simbolo");
        }

        /// <summary>
        /// Titulo do cartao de metrica.
        ///
        /// O cartao mostrava o rotulo completo, que traz o dispositivo junto:
        /// em 176 px sobrava "Enhanced - CPU (Tctl/Tdie) ...", o pedaco do meio
        /// de uma frase. O que importa aqui, alem da traducao, e que nada
        /// dependa de constar da tabela - a maquina de outra pessoa tem sensores
        /// que ninguem previu.
        /// </summary>
        private static void NomesAmigaveis()
        {
            Secao("nomes amigaveis das metricas");

            string antes = T.Language;
            try
            {
                T.Language = T.PtBr;

                SensorEntry s = new SensorEntry();
                s.Name = "CPU (Tctl/Tdie)";
                s.Hardware = "CPU [#0]: AMD Ryzen 5 5600X: Enhanced";
                s.Label = "CPU [#0]: AMD Ryzen 5 5600X: Enhanced - CPU (Tctl/Tdie) (Temperature, C)";
                s.Category = "CPU";

                Igual("Temperatura do processador", MetricPicker.Rotulo(s),
                      "vale o nome da leitura, e nao o rotulo inteiro");
                Verdade(MetricPicker.Rodape(s).Contains("Ryzen 5 5600X"),
                        "a peca fica no rodape, encurtada");

                Igual("Ventoinha da GPU", MetricPicker.Amigavel("GPU Fan (ODN)"),
                      "ventoinha da placa de video");
                Igual("Temperatura do disco", MetricPicker.Amigavel("Drive Temperature"), "disco");
                Igual("Taxa de download", MetricPicker.Amigavel("Current DL rate"),
                      "a tabela ignora a caixa");
                Igual("Clock do barramento", MetricPicker.Amigavel("CPU [#0]: AMD Ryzen 5 5600X: Bus Clock"),
                      "enumeracao e grupo saem antes da busca");

                // O sufixo do agregado sai da busca e volta no fim; sem isso
                // nenhuma media acharia a tabela.
                string media = MetricPicker.Amigavel("CPU (Tctl/Tdie) · média de 6");
                Verdade(media.StartsWith("Temperatura do processador"), "o agregado acha a tabela");
                Verdade(media.EndsWith("média de 6"), "e conserva o sufixo do agregado");

                // O indice do nucleo sai; o substantivo fica. Antes disso o ramo
                // "Core \d+" da CoreIndex levava a palavra Core junto, e o
                // agregado do HWiNFO saia como "Clock - media de 6": um clock de
                // coisa nenhuma, que ainda por cima nao achava a tabela.
                Igual("Core Clock", Sensors.NomeExibido("Core 0 Clock"),
                      "o indice sai e Core fica");
                Igual("Core Clock", Sensors.NomeExibido("Core 12 Clock (perf #3/4)"),
                      "o sufixo de perf sai junto");
                Igual("Core Usage", Sensors.NomeExibido("Core 0 T1 Usage"),
                      "a marca de thread sai e nao deixa espaco duplo");
                Igual("CPU Core", Sensors.NomeExibido("CPU Core #3"),
                      "a nomenclatura da LibreHardwareMonitor ja preservava Core");

                Igual("Clock dos núcleos", MetricPicker.Amigavel("Core Clock"),
                      "e com o substantivo de volta a tabela acha");
                Igual("Uso dos núcleos", MetricPicker.Amigavel("Core Usage"), "uso por nucleo");

                // Desambiguacao pelo tipo. A Radeon publica DUAS leituras
                // chamadas "GPU Core": temperatura e clock. Sem isto, o clock
                // aparecia na lista com nome de temperatura - pior do que
                // faltar, porque quem procurava desistia depois de achar.
                Igual("Temperatura da GPU", MetricPicker.Amigavel("GPU Core", "Temperature"),
                      "GPU Core como temperatura");
                Igual("Clock da GPU", MetricPicker.Amigavel("GPU Core", "Clock"),
                      "e o MESMO nome como clock");
                Igual("Clock da memória de vídeo", MetricPicker.Amigavel("GPU Memory", "Clock"),
                      "idem para a memoria de video");

                // A LibreHardwareMonitor nomeia o grupo por nucleo so de "Core":
                // o indice e um "#1" que sai inteiro, e sobra uma palavra que
                // nao diz que grandeza e. Aqui quem diz e o tipo.
                Igual("Clock dos núcleos", MetricPicker.Amigavel("Core", "Clock"),
                      "Core solto, desambiguado por clock");
                Igual("Multiplicador dos núcleos", MetricPicker.Amigavel("Core", "Factor"),
                      "o mesmo Core solto como multiplicador");
                Igual("Uso dos núcleos", MetricPicker.Amigavel("CPU Core", "Load"),
                      "CPU Core como uso, e nao como temperatura");
                Igual("Temperatura dos núcleos", MetricPicker.Amigavel("CPU Core", "Temperature"),
                      "CPU Core como temperatura");

                // Tipo desconhecido nao pode atrapalhar: cai na busca pelo nome.
                Igual("Temperatura do disco", MetricPicker.Amigavel("Drive Temperature", "Temperature"),
                      "chave sem tipo continua valendo quando o tipo nao casa");

                // A trava da migracao. Normalize alimenta o GroupKey, que vira o
                // Id sintetico gravado no perfil e a chave da serie no
                // history.dat. Se alguem "consertar" a Normalize junto com a
                // NomeExibido, todo perfil salvo perde os sensores em silencio -
                // cartao em branco, painel vazio, nenhum erro na tela. Estas
                // quatro linhas sao o alarme.
                Igual("Clock", Sensors.Normalize("Core 0 Clock"),
                      "a chave de agrupamento NAO muda");
                Igual("Clock", Sensors.Normalize("Core 12 Clock (perf #3/4)"),
                      "membros do mesmo grupo continuam caindo na mesma chave");
                Igual("Usage", Sensors.Normalize("Core 0 T1 Usage"), "idem para uso");
                Igual("CPU Core", Sensors.Normalize("CPU Core #3"), "idem para a LHM");

                Igual("Sensor Exotico X9", MetricPicker.Amigavel("Sensor Exotico X9"),
                      "nome fora da tabela passa inteiro");
                Igual("", MetricPicker.Amigavel(null), "nulo vira vazio");
                Igual("", MetricPicker.Rotulo(null), "sensor nulo vira vazio");

                T.Language = T.EnUs;
                Igual("CPU temperature", MetricPicker.Amigavel("CPU (Tctl/Tdie)"),
                      "a mesma leitura no outro idioma");
            }
            finally { T.Language = antes; }
        }

        /// <summary>
        /// Serie temporal das metricas.
        ///
        /// Cobre o que nao depende do relogio: o fechamento de baldes precisa de
        /// cinco segundos reais para acontecer, e uma suite que dorme para
        /// verificar deixa de ser rodada.
        /// </summary>
        /// <summary>
        /// A esticada das paginas.
        ///
        /// Erro de geometria nao aparece na maquina de quem escreveu: aparece na
        /// janela do tamanho que ninguem testou. Estes casos sao os que passam
        /// despercebidos no olho - a borda direita depois da divisao inteira, a
        /// pagina que nao pode encolher, a fileira de larguras desiguais.
        /// </summary>
        private static void LarguraDasPaginas()
        {
            Secao("largura das paginas");

            // A pagina de Paineis: dois cartoes em cima, um atravessado embaixo.
            Rectangle[] paineis = new Rectangle[]
            {
                new Rectangle(0,   0, 370, 268),
                new Rectangle(386, 0, 370, 268),
                new Rectangle(0, 280, 756, 532),
            };

            Rectangle[] r = SettingsForm.Esticar(paineis, 920, 1100);
            Igual(920, r[2].Right, "o cartao de baixo alcanca a borda");
            Igual(920, r[1].Right, "e a fileira de cima termina junto com ele");
            Igual(0, r[0].Left, "a primeira coluna continua encostada na esquerda");
            Igual(16, r[1].Left - r[0].Right, "o vao entre os dois nao estica junto");
            Igual(r[0].Width, r[1].Width, "cartoes iguais crescem igual");
            Igual(268, r[0].Height, "a altura nao e assunto desta conta");

            // Larguras desiguais: a lista de perfis e menor que a previa, e tem
            // de continuar menor - a diferenca de tamanho e o que diz onde olhar.
            Rectangle[] perfis = new Rectangle[]
            {
                new Rectangle(0,   0, 330, 566),
                new Rectangle(346, 0, 410, 566),
            };
            r = SettingsForm.Esticar(perfis, 916, 1100);
            Igual(916, r[1].Right, "a fileira desigual tambem fecha na borda");
            Verdade(r[1].Width > r[0].Width, "a previa continua maior que a lista");
            Igual(16, r[1].Left - r[0].Right, "vao preservado");

            // Estreita demais: nunca encolher. Os controles dentro dos cartoes
            // estao em coordenadas fixas e seriam cortados na borda direita.
            r = SettingsForm.Esticar(paineis, 500, 1100);
            Igual(756, r[2].Width, "abaixo do projeto a pagina para de encolher");
            Igual(0, r[0].Left, "e nao ganha margem negativa");

            // Acima do maximo: para de crescer e passa a centralizar.
            r = SettingsForm.Esticar(paineis, 1900, 1100);
            Igual(1100, r[2].Width, "o conteudo trava na largura maxima");
            Igual(400, r[2].Left, "e a sobra vira margem dos dois lados");
            Igual(1500, r[2].Right, "com o mesmo tanto sobrando de cada lado");

            // Larguras que nao dividem redondo. Sem o ultimo absorvendo o resto,
            // a fileira fecharia um ou dois pixels antes da borda e o cartao de
            // baixo apareceria desalinhado dos de cima.
            Rectangle[] tres = new Rectangle[]
            {
                new Rectangle(0,   0, 100, 50),
                new Rectangle(107, 0, 100, 50),
                new Rectangle(214, 0, 100, 50),
                new Rectangle(0,  60, 314, 50),
            };
            for (int disp = 314; disp <= 360; disp++)
            {
                Rectangle[] t = SettingsForm.Esticar(tres, disp, 1100);
                Igual(t[3].Right, t[2].Right,
                      "em " + disp + " px as duas fileiras terminam no mesmo x");
            }

            Verdade(SettingsForm.Esticar(null, 900, 1100) == null, "sem retangulos, nada a fazer");
            Verdade(SettingsForm.Esticar(new Rectangle[0], 900, 1100) == null, "vetor vazio idem");
        }

        /// <summary>
        /// O retrato da maquina na tela de bordo.
        ///
        /// Sao dados derivados, e derivacao errada aqui nao quebra nada: so
        /// mostra a maquina errada, calada, para sempre.
        /// </summary>
        /// <summary>
        /// Os conjuntos prontos, contra uma maquina inventada.
        ///
        /// Inventada de proposito: o que precisa ser verificado e a REGRA, e uma
        /// lista real so tem os sensores desta maquina. Aqui da para montar a
        /// maquina sem ventoinha, a sem RTSS e a de trinta temperaturas - que sao
        /// justamente os casos onde a selecao erra.
        /// </summary>
        private static void ConjuntosDeMetricas()
        {
            Secao("conjuntos de metricas");

            List<SensorEntry> m = new List<SensorEntry>();
            m.Add(Sensor("cpu.t", "CPU", SensorType.Temperature, "CPU (Tctl/Tdie)", 55));
            m.Add(Sensor("cpu.l", "CPU", SensorType.Load, "CPU Total", 30));
            m.Add(Sensor("cpu.w", "CPU", SensorType.Power, "CPU Package Power", 47));
            m.Add(Sensor("gpu.t", "GPU", SensorType.Temperature, "GPU Core", 44));
            m.Add(Sensor("gpu.l", "GPU", SensorType.Load, "GPU Core", 12));
            m.Add(Sensor("gpu.c", "GPU", SensorType.Clock, "GPU Core", 1340));
            m.Add(Sensor("gpu.w", "GPU", SensorType.Power, "GPU Power", 120));
            m.Add(Sensor("mb.t", "Placa-mãe", SensorType.Temperature, "Motherboard", 38));
            m.Add(Sensor("f1", "Placa-mãe", SensorType.Fan, "CPU Fan", 900));
            m.Add(Sensor("f2", "Placa-mãe", SensorType.Fan, "System Fan", 700));
            m.Add(Sensor("pwm", "Placa-mãe", SensorType.Control, "CPU Fan PWM", 40));
            m.Add(Sensor(Rtss.Prefixo + "fps", Sensors.CategoriaJogos, SensorType.Factor, "FPS", 144));
            m.Add(Sensor(Rtss.Prefixo + "frametime", Sensors.CategoriaJogos, SensorType.Factor, "Frametime", 6.9f));

            Igual(4, MetricPicker.Conjuntos.Length, "quatro pontos de partida");

            List<SensorEntry> jogos = MetricPicker.Montar(Achar2("jogos"), m);
            Verdade(Tem(jogos, Rtss.Prefixo + "fps"), "jogos comeca pela taxa de quadros");
            Verdade(Tem(jogos, "gpu.l") && Tem(jogos, "gpu.t"), "e traz uso e temperatura da GPU");
            Verdade(!Tem(jogos, "f1"), "ventoinha nao e assunto de jogo");

            List<SensorEntry> termico = MetricPicker.Montar(Achar2("termico"), m);
            Verdade(Tem(termico, "cpu.t") && Tem(termico, "gpu.t") && Tem(termico, "mb.t"),
                    "termico pega TODAS as temperaturas, inclusive a da placa");
            Verdade(Tem(termico, "cpu.w"), "e a potencia, que e a causa delas");
            Verdade(!Tem(termico, "cpu.l"), "uso nao entra: nao e temperatura nem energia");

            List<SensorEntry> silencioso = MetricPicker.Montar(Achar2("silencioso"), m);
            Verdade(Tem(silencioso, "f1") && Tem(silencioso, "f2"), "silencioso pega as ventoinhas");
            Verdade(Tem(silencioso, "pwm"), "e o controle em porcentagem");
            Verdade(Tem(silencioso, "cpu.t"), "com a temperatura que as comanda");

            // Cada conjunto e um recorte diferente. Dois que devolvem a mesma
            // lista nao sao dois conjuntos.
            Verdade(!MesmaLista(jogos, termico), "jogos e termico nao se confundem");
            Verdade(!MesmaLista(termico, silencioso), "termico e silencioso tambem nao");

            // Maquina sem RTSS: o conjunto de jogos perde as leituras de quadro e
            // continua util com o que sobrou, em vez de vir vazio.
            List<SensorEntry> semRtss = new List<SensorEntry>();
            foreach (SensorEntry s in m)
                if (!s.Id.StartsWith(Rtss.Prefixo, StringComparison.Ordinal)) semRtss.Add(s);
            List<SensorEntry> jogos2 = MetricPicker.Montar(Achar2("jogos"), semRtss);
            Verdade(jogos2.Count >= 4, "sem RTSS o conjunto de jogos ainda traz o hardware");
            Verdade(!Tem(jogos2, Rtss.Prefixo + "fps"), "e nao inventa a leitura que falta");

            // O teto: uma regra "todas as temperaturas" numa maquina com muitos
            // discos devolveria uma lista que nao e painel, e sim a lista de
            // sempre com cores.
            List<SensorEntry> muitos = new List<SensorEntry>(m);
            for (int i = 0; i < 40; i++)
                muitos.Add(Sensor("d" + i, "Disco", SensorType.Temperature, "Drive Temperature", 40));
            List<SensorEntry> cheio = MetricPicker.Montar(Achar2("termico"), muitos);
            Verdade(cheio.Count <= MetricPicker.TetoDoConjunto,
                    "o teto de " + MetricPicker.TetoDoConjunto + " segura (obtido: " + cheio.Count + ")");

            // Sem sensor nenhum, nada e devolvido - e quem chama decide nao
            // apagar a grade que ja existe.
            Igual(0, MetricPicker.Montar(Achar2("silencioso"), new List<SensorEntry>()).Count,
                  "maquina sem sensores devolve lista vazia");

            // Repetido nunca entra duas vezes: "GPU Core" existe como
            // temperatura, uso e clock, e as tres regras poderiam pescar a mesma.
            foreach (List<SensorEntry> lst in new List<SensorEntry>[] { jogos, termico, silencioso })
            {
                List<string> vistos = new List<string>();
                foreach (SensorEntry s in lst)
                {
                    Verdade(!vistos.Contains(s.Id), "sem cartao repetido: " + s.Id);
                    vistos.Add(s.Id);
                }
            }
        }

        private static SensorEntry Sensor(string id, string cat, SensorType tipo, string nome, float v)
        {
            SensorEntry s = new SensorEntry();
            s.Id = id; s.Category = cat; s.Type = tipo; s.Name = nome;
            s.Hardware = cat; s.Label = nome; s.Value = v; s.Unit = "";
            return s;
        }

        private static MetricPicker.Conjunto Achar2(string chave)
        {
            foreach (MetricPicker.Conjunto c in MetricPicker.Conjuntos)
                if (c.Chave == chave) return c;
            return null;
        }

        private static bool Tem(List<SensorEntry> l, string id)
        {
            foreach (SensorEntry s in l) if (s.Id == id) return true;
            return false;
        }

        private static bool MesmaLista(List<SensorEntry> a, List<SensorEntry> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i].Id != b[i].Id) return false;
            return true;
        }

        private static void RetratoDaMaquina()
        {
            Secao("retrato da maquina");

            SystemInfo s = SystemInfo.From(null);

            // Sem lista de sensores nao ha peca nenhuma, mas o que vem do
            // proprio sistema tem de vir assim mesmo.
            Verdade(s.Cpu == null, "sem sensores, nenhum processador nomeado");
            Verdade(!string.IsNullOrEmpty(s.Ram), "a memoria instalada nao depende de sensor");
            Verdade(!string.IsNullOrEmpty(s.CpuNucleos), "a contagem de threads tambem nao");
            Verdade(s.CpuNucleos.IndexOf(Environment.ProcessorCount.ToString()) >= 0,
                    "e ela traz o numero que o proprio .NET responde");
            Verdade(s.Placa == null, "sem sensores da placa-mae, a linha nao existe");

            // A placa-mae so entra quando o nome identifica alguma placa. "ACPI"
            // e o nome do BARRAMENTO onde a LibreHardwareMonitor achou o sensor,
            // e "To Be Filled By O.E.M." e o texto de exemplo que o montador nao
            // preencheu - os dois parecem informacao e nao sao.
            Igual(null, SystemInfo.NomeDePlaca("ACPI"), "o barramento nao e a placa");
            Igual(null, SystemInfo.NomeDePlaca("To Be Filled By O.E.M."),
                  "o formulario em branco tambem nao");
            Igual(null, SystemInfo.NomeDePlaca("LPC"), "nem o nome do controlador");
            Igual(null, SystemInfo.NomeDePlaca("X570"), "nome curto demais nao passa");
            Igual("B550M Steel Legend", SystemInfo.NomeDePlaca("B550M Steel Legend"),
                  "uma placa de verdade passa");

            // A memoria de video: publicada em MB pelas fontes conhecidas, e em
            // bytes por alguma. Sem a segunda escala, uma RTX de 12 GB apareceria
            // com cinco digitos de gigabyte.
            Igual("8 GB", SystemInfo.EmGigabytes(8192), "8192 MB viram 8 GB");
            Igual("12 GB", SystemInfo.EmGigabytes(12288), "12288 MB viram 12 GB");
            Igual("24 GB", SystemInfo.EmGigabytes(25769803776d),
                  "o total vindo em bytes tambem e reconhecido");

            // O ProductName do registro responde "Windows 10" numa maquina com
            // build 26200. Quem le so ele mostra a versao errada; o numero da
            // compilacao e que decide.
            if (!string.IsNullOrEmpty(s.Sistema))
            {
                Verdade(s.Sistema.IndexOf("Windows") >= 0, "o sistema se identifica");

                int build = 0;
                try
                {
                    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine
                        .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                        if (k != null)
                            int.TryParse(Convert.ToString(k.GetValue("CurrentBuild")), out build);
                }
                catch { }

                if (build >= 22000)
                    Verdade(s.Sistema.IndexOf("Windows 10") < 0,
                            "build " + build + " nao pode se chamar Windows 10 (obtido: " + s.Sistema + ")");
                if (build > 0)
                    Verdade(s.Sistema.IndexOf(build.ToString()) >= 0, "a compilacao aparece");
            }
        }

        /// <summary>
        /// A decodificacao do que o WMI devolve.
        ///
        /// Tudo aqui e string crua virando numero ou data. E o tipo de codigo que
        /// erra em silencio: uma data fora de ordem vira outra data valida, um
        /// campo hexadecimal lido errado vira outro hexadecimal - nada explode,
        /// so fica errado na tela para sempre.
        /// </summary>
        private static void FolhaDeEspecificacoes()
        {
            Secao("folha de especificacoes");

            // "AMD64 Family 25 Model 33 Stepping 2", o que o Win32_Processor
            // devolve nesta maquina.
            string fam, mod, step;
            Verdade(SpecSheet.Repartir("AMD64 Family 25 Model 33 Stepping 2",
                                       out fam, out mod, out step), "a descricao se reparte");
            Igual("25", fam, "familia");
            Igual("33", mod, "modelo");
            Igual("2", step, "stepping");

            Verdade(SpecSheet.Repartir("Intel64 Family 6 Model 158 Stepping 10",
                                       out fam, out mod, out step), "e a da Intel tambem");
            Igual("6", fam, "familia Intel");
            Igual("158", mod, "modelo Intel");

            Verdade(!SpecSheet.Repartir("processador exotico", out fam, out mod, out step),
                    "descricao sem os campos nao inventa numeros");
            Verdade(!SpecSheet.Repartir(null, out fam, out mod, out step), "nulo idem");

            // "PCI\VEN_1002&DEV_67DF&SUBSYS_E3531DA2&REV_E7"
            string pnp = @"PCI\VEN_1002&DEV_67DF&SUBSYS_E3531DA2&REV_E7\4&1FC990D7&0&0019";
            Igual("67DF", SpecSheet.IdDePci(pnp, "DEV"), "id do dispositivo");
            Igual("1002", SpecSheet.IdDePci(pnp, "VEN"), "id do fabricante");
            Igual("E7", SpecSheet.IdDePci(pnp, "REV"), "revisao");
            Igual(null, SpecSheet.IdDePci(pnp, "XYZ"), "campo ausente devolve nulo");
            Igual(null, SpecSheet.IdDePci(null, "DEV"), "sem identificador, nada");

            // "20251028000000.000000+000" - ano, mes e dia grudados.
            Igual(new DateTime(2025, 10, 28).ToShortDateString(),
                  SpecSheet.DataWmi("20251028000000.000000+000"), "data da BIOS");
            Igual(new DateTime(2026, 5, 20).ToShortDateString(),
                  SpecSheet.DataWmi("20260520000000.000000-000"), "data do driver");
            Igual(null, SpecSheet.DataWmi("20251345000000.000000+000"), "mes 13 nao passa");
            Igual(null, SpecSheet.DataWmi("abc"), "texto curto nao passa");
            Igual(null, SpecSheet.DataWmi(null), "nulo nao passa");

            // O WMI publica cache em KB. L3 de 32768 KB dito assim obriga quem le
            // a dividir de cabeca para reconhecer os 32 MB da peca.
            Igual("32 MB", SpecSheet.EmMegabytes(32768), "L3 de um 5600X");
            Igual("3 MB", SpecSheet.EmMegabytes(3072), "L2 de um 5600X");
            Igual("512 KB", SpecSheet.EmMegabytes(512), "abaixo de um mega continua em KB");
            Igual(null, SpecSheet.EmMegabytes(0), "zero nao vira linha");

            Igual("8 GB", SpecSheet.EmGigabytes(8589934592UL),
                  "um pente de 8 GB, sem a decimal vazia");
            Igual("16 GB", SpecSheet.EmGigabytes(17179869184UL), "acima de dez, sem decimal");
            Igual("932 GB", SpecSheet.EmGigabytes(1000202273280UL),
                  "o disco de 1 TB comercial tem 932 GiB de verdade");
            Igual(null, SpecSheet.EmGigabytes(0), "tamanho zero nao vira linha");

            // "20260808204115.694344-180" - o instante tem hora, ao contrario da
            // data da BIOS. Sem a hora, "ligado ha" erraria por ate um dia.
            DateTime b = SpecSheet.InstanteWmi("20260808204115.694344-180");
            Igual(new DateTime(2026, 8, 8, 20, 41, 15), b, "instante do ultimo boot");
            Igual(DateTime.MinValue, SpecSheet.InstanteWmi("20260899204115.694344-180"),
                  "dia 99 nao passa");
            Igual(DateTime.MinValue, SpecSheet.InstanteWmi("20260808994115.000000-180"),
                  "hora 99 nao passa");
            Igual(DateTime.MinValue, SpecSheet.InstanteWmi("2026"), "texto curto nao passa");

            // A duracao mostra a maior unidade e a seguinte: "3 d 2 h" responde
            // melhor que "74 h" e que "3 d 2 h 17 min 4 s".
            string antesIdioma = T.Language;
            try
            {
                T.Language = T.PtBr;
                Igual("3 d 2 h", T.Duracao(new TimeSpan(3, 2, 17, 4)), "dias e horas");
                Igual("4 h 12 min", T.Duracao(new TimeSpan(0, 4, 12, 30)), "horas e minutos");
                Igual("7 min", T.Duracao(new TimeSpan(0, 0, 7, 30)), "so minutos");
            }
            finally { T.Language = antesIdioma; }

            // Razao social nao identifica nada e nao cabe na coluna. Estes dois
            // sao os nomes reais que o WMI devolve nesta maquina.
            Igual("American Megatrends", SpecSheet.Fabricante("American Megatrends International, LLC."),
                  "a forma juridica sai");
            Igual("Gigabyte", SpecSheet.Fabricante("Gigabyte Technology Co., Ltd."),
                  "duas caudas na mesma linha saem as duas");
            Igual("AuthenticAMD", SpecSheet.Fabricante("AuthenticAMD"), "sem cauda, passa inteiro");
            Igual("Kingston", SpecSheet.Fabricante("Kingston"), "nome simples nao e mexido");
            Igual(null, SpecSheet.Fabricante(null), "nulo continua nulo");

            // Um nome que e SO cauda nao pode virar vazio: melhor o original.
            Igual("Technology", SpecSheet.Fabricante("Technology"), "so cauda devolve o original");

            Igual("DDR4", SpecSheet.TipoDeMemoria(26), "codigo SMBIOS do DDR4");
            Igual("DDR5", SpecSheet.TipoDeMemoria(34), "e do DDR5");
            Igual(null, SpecSheet.TipoDeMemoria(0), "codigo desconhecido nao inventa tipo");

            // Grupo descarta linha vazia: campo que o WMI nao preencheu nao pode
            // virar um rotulo apontando para nada.
            SpecGrupo g = new SpecGrupo("teste");
            g.Por("cheio", "valor");
            g.Por("vazio", null);
            g.Por("branco", "");
            Igual(1, g.Linhas.Count, "so a linha com valor entra");
            Igual("valor", g.Linhas[0][1], "e com o valor certo");
        }

        private static void HistoricoDasMetricas()
        {
            Secao("historico das metricas");

            Igual("10 min", MetricHistory.NomeDaJanela(600), "janela curta em minutos");
            Igual("1 h", MetricHistory.NomeDaJanela(3600), "janela de uma hora");
            Igual("6 h", MetricHistory.NomeDaJanela(21600), "janela longa");

            Igual(3600, MetricHistory.JanelaValida(3600), "valor da lista passa");
            Igual(MetricHistory.JanelaPadrao, MetricHistory.JanelaValida(12345),
                  "valor fora da lista cai no padrao");
            Igual(MetricHistory.JanelaPadrao, MetricHistory.JanelaValida(0),
                  "configuracao antiga sem a chave cai no padrao");

            // Serie que nao existe volta como falha, e nao como zero: um grafico
            // colado no eixo afirma uma leitura que ninguem fez.
            float[] buf = null;
            int n = MetricHistory.Janela("nao-existe", 600, ref buf);
            Igual(600 / MetricHistory.PassoSeg, n, "um ponto por balde");
            Verdade(float.IsNaN(buf[0]) && float.IsNaN(buf[n - 1]), "serie ausente vem toda como falha");

            MetricHistory.Seguir(new string[] { "a", "b", "a", "", null });
            Igual(2, MetricHistory.Seguidos.Count, "repetido e vazio nao entram na lista");

            MetricHistory.Seguir(new string[] { "b" });
            Igual(1, MetricHistory.Seguidos.Count, "quem sai da grade sai da lista");

            MetricHistory.Seguir(null);
            Igual(0, MetricHistory.Seguidos.Count, "lista vazia nao acompanha nada");

            // O relogio da janela, que e o que o balao do cartao mostra. Um erro
            // de um balde aqui vira um horario errado por cinco segundos - nada
            // que salte aos olhos numa curva, e por isso mesmo o tipo de coisa
            // que fica errada para sempre.
            DateTime fim = MetricHistory.FimDaJanela();
            Verdade(fim.Kind == DateTimeKind.Local, "hora local, que e a do relogio de quem le");

            double atraso = (DateTime.Now - fim).TotalSeconds;
            Verdade(atraso >= 0, "o ultimo balde ja fechou, entao nao esta no futuro");
            Verdade(atraso < 2 * MetricHistory.PassoSeg + 5,
                    "e nao esta mais que um passo atras (obtido: " + (int)atraso + " s)");

            // O balde alinha no passo: sem isso o horario do balao andaria
            // sozinho conforme a hora em que a janela foi aberta.
            long seg = (long)(fim.ToUniversalTime() -
                              new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            Igual(0L, seg % MetricHistory.PassoSeg, "o instante cai na grade dos baldes");
        }

        /// <summary>
        /// Leitura da memoria compartilhada do RTSS, contra um mapeamento nosso.
        ///
        /// Nenhuma maquina de desenvolvimento tem o RTSS rodando com um jogo
        /// aberto na hora do build, entao o teste monta o mapeamento com o
        /// layout documentado e confere o que o leitor faz dele. Isso cobre o
        /// que pode estar errado no nosso lado - deslocamento de campo, passo da
        /// entrada, escolha de qual programa vale, conversao das unidades. O que
        /// nao cobre, e nao ha como cobrir aqui, e o RTSS de verdade preencher
        /// esses campos como se espera.
        /// </summary>
        private static void QuadrosPorSegundo()
        {
            Secao("quadros por segundo");

            // A ordem dos bytes da assinatura esta AFERIDA contra a memoria de
            // uma maquina com o RTSS 2.21 no ar, onde o cabecalho se le "SSTR".
            // O literal multicaractere 'RTSS' do C cai assim em little-endian.
            // A primeira versao comparava na ordem em que se le o nome e
            // desistia de toda leitura, sem erro nenhum no registro.
            byte[] assinatura = BitConverter.GetBytes(Rtss.Assinatura);
            Igual((byte)'S', assinatura[0], "primeiro byte da assinatura");
            Igual((byte)'S', assinatura[1], "segundo byte da assinatura");
            Igual((byte)'T', assinatura[2], "terceiro byte da assinatura");
            Igual((byte)'R', assinatura[3], "quarto byte da assinatura");

            List<SensorEntry> lista = new Rtss().Ler();
            Igual(6, lista.Count, "a fonte publica sempre as mesmas seis leituras");
            Igual(Sensors.CategoriaJogos, lista[0].Category, "categoria propria");

            // O rodape vivo e o da leitura tem de dizer a mesma coisa. Era
            // exatamente a divergencia entre os dois - um seguindo o jogo, o
            // outro congelado na montagem da grade - que punha "nenhum jogo em
            // execucao" debaixo de 757 FPS.
            Igual(lista[0].Hardware, Rtss.PecaAtual, "o rodape vivo concorda com a leitura");
            Verdade(MetricPicker.RodapeJogos().Contains(Rtss.PecaAtual),
                    "e e ele que entra no rodape do cartao");

            if (Rtss.Presente())
            {
                // Esta maquina tem o RTSS no ar: entao o leitor TEM de reconhecer
                // o cabecalho. E exatamente a afirmacao que quebrou quando o
                // teste so sabia conversar com um mapeamento montado por ele
                // mesmo.
                Verdade(lista[0].Hardware != T.RtssMissing,
                        "com o RTSS no ar, o leitor reconhece o cabecalho");
            }
            else
            {
                // Sem mapeamento: as leituras existem e vem sem valor, em vez de
                // sumirem da lista e levarem junto o perfil de quem as escolheu.
                bool todasVazias = true;
                foreach (SensorEntry s in lista) if (s.Value.HasValue) todasVazias = false;
                Verdade(todasVazias, "sem RTSS, nenhuma leitura inventa valor");
                Igual(T.RtssMissing, lista[0].Hardware, "e o rodape diz o que falta");
            }

            AjusteDoConfigDoRtss();

            const int TamEntrada = 328, Inicio = 64, Quantas = 3;
            MemoryMappedFile mmf = null;
            try
            {
                try
                {
                    mmf = MemoryMappedFile.CreateNew("RTSSSharedMemoryV2",
                                                     Inicio + TamEntrada * Quantas);
                }
                catch (Exception)
                {
                    // Ja existe: o RTSS de verdade esta rodando nesta maquina.
                    // Escrever por cima da memoria dele seria muito pior do que
                    // deixar de rodar esta parte do teste.
                    Console.WriteLine("  (pulado: RTSSSharedMemoryV2 ja existe nesta maquina)");
                    return;
                }

                using (MemoryMappedViewAccessor v = mmf.CreateViewAccessor())
                {
                    v.Write(0, Rtss.Assinatura);
                    v.Write(4, (uint)0x00020000);
                    v.Write(8, (uint)TamEntrada);
                    v.Write(12, (uint)Inicio);
                    v.Write(16, (uint)Quantas);

                    // Entrada 0 vazia, para o leitor ter de pular buraco.
                    Entrada(v, Inicio + TamEntrada * 1, 1111, @"C:\Jogos\alpha.exe", 1000, 2000, 120, 8333);
                    Entrada(v, Inicio + TamEntrada * 2, 2222, @"D:\Jogos\beta.exe", 1000, 3000, 120, 16666);

                    List<SensorEntry> lidas = new Rtss().Ler();
                    Igual(6, lidas.Count, "seis leituras tambem com o RTSS presente");

                    SensorEntry fps = Achar(lidas, "rtss:fps");
                    SensorEntry ft = Achar(lidas, "rtss:frametime");

                    // Nenhuma das duas entradas e o processo em primeiro plano,
                    // entao vale quem desenhou por ultimo: a beta, com t1 maior.
                    Verdade(fps.Value.HasValue && Math.Abs(fps.Value.Value - 60f) < 0.01f,
                            "120 quadros em 2000 ms dao 60 FPS");
                    Verdade(ft.Value.HasValue && Math.Abs(ft.Value.Value - 16.666f) < 0.01f,
                            "tempo de quadro vem em microssegundos e sai em ms");
                    Igual("beta.exe", fps.Hardware, "vence a entrada com o quadro mais recente");
                    Igual("FPS", fps.Unit, "unidade da taxa");
                    Igual("ms", ft.Unit, "unidade do tempo de quadro");

                    // Uma amostra so nao autoriza falar em minimo nem em pior caso.
                    Verdade(!Achar(lidas, "rtss:fps.min").Value.HasValue,
                            "minimo espera a janela ter amostras suficientes");
                    Verdade(!Achar(lidas, "rtss:frametime.max").Value.HasValue,
                            "pior caso espera a mesma coisa");
                }
            }
            finally { if (mmf != null) mmf.Dispose(); }
        }

        /// <summary>
        /// Ajuste do Config do RTSS.
        ///
        /// A funcao e pura, entao aqui da para cobrir de verdade o que no resto
        /// da ponte depende da maquina. E o que precisa ser coberto: o arquivo
        /// nao e nosso, guarda tambem o cache de deslocamentos que o RTSS levou
        /// tempo descobrindo, e uma linha perdida ali custa a ele redescobrir
        /// tudo.
        /// </summary>
        private static void AjusteDoConfigDoRtss()
        {
            // Caso real desta maquina: as duas chaves existem, uma desligada.
            string real =
                "[FnOffsetCache]\r\nDDRAW.DLL=00083C00 4E70C42F\r\nVersion=0000000A\r\n" +
                "[Settings]\r\nSkin=default.usf\r\nStartMinimized=1\r\nStartWithWindows=0\r\nShowTooltips=1\r\n" +
                "[Shared]\r\nFlags=00000001\r\n";

            string saida = Rtss.AjustarIni(real);
            Verdade(saida.Contains("StartWithWindows=1"), "liga o inicio com o Windows");
            Verdade(saida.Contains("StartMinimized=1"), "e conserva o inicio minimizado");
            Verdade(!saida.Contains("StartWithWindows=0"), "o valor antigo nao fica para tras");
            Verdade(saida.Contains("DDRAW.DLL=00083C00 4E70C42F"), "o cache de deslocamentos sobrevive");
            Verdade(saida.Contains("Skin=default.usf") && saida.Contains("ShowTooltips=1"),
                    "as outras preferencias sobrevivem");
            Verdade(saida.Contains("[Shared]") && saida.Contains("Flags=00000001"),
                    "as outras secoes sobrevivem");

            // Idempotente: rodar de novo nao duplica nem muda mais nada.
            Igual(saida, Rtss.AjustarIni(saida), "aplicar duas vezes da o mesmo arquivo");

            // Chaves ausentes entram DENTRO da secao, e nao no fim do arquivo -
            // no fim cairiam em [Shared] e nao valeriam nada.
            string semChaves = "[Settings]\r\nSkin=default.usf\r\n\r\n[Shared]\r\nFlags=1\r\n";
            string comChaves = Rtss.AjustarIni(semChaves);
            int posInicio = comChaves.IndexOf("StartWithWindows=1", StringComparison.Ordinal);
            int posShared = comChaves.IndexOf("[Shared]", StringComparison.Ordinal);
            Verdade(posInicio > 0 && posShared > posInicio, "a chave nova fica antes da proxima secao");
            Verdade(comChaves.Contains("StartMinimized=1"), "as duas chaves entram");

            // Sem a secao, ela nasce - arquivo novo ou truncado nao pode virar
            // um ajuste que nao acontece.
            string semSecao = Rtss.AjustarIni("[Outra]\r\nX=1\r\n");
            Verdade(semSecao.Contains("[Settings]"), "a secao nasce quando falta");
            Verdade(semSecao.Contains("StartWithWindows=1") && semSecao.Contains("StartMinimized=1"),
                    "com as duas chaves");
            Verdade(semSecao.Contains("[Outra]") && semSecao.Contains("X=1"), "sem perder o que havia");

            string vazio = Rtss.AjustarIni("");
            Verdade(vazio.Contains("[Settings]") && vazio.Contains("StartWithWindows=1"),
                    "arquivo vazio tambem vira um Config valido");

            // Nome de secao com caixa diferente continua sendo a mesma secao.
            string caixa = Rtss.AjustarIni("[SETTINGS]\r\nstartwithwindows=0\r\n");
            Verdade(caixa.Contains("StartWithWindows=1"), "a secao e a chave ignoram a caixa");
            Verdade(!caixa.Contains("startwithwindows=0"), "e o valor antigo sai");
        }

        private static void Entrada(MemoryMappedViewAccessor v, long b, uint pid, string nome,
                                    uint t0, uint t1, uint quadros, uint us)
        {
            v.Write(b, pid);
            byte[] bytes = Encoding.Default.GetBytes(nome);
            v.WriteArray(b + 4, bytes, 0, bytes.Length);
            v.Write(b + 4 + bytes.Length, (byte)0);
            v.Write(b + 268, t0);
            v.Write(b + 272, t1);
            v.Write(b + 276, quadros);
            v.Write(b + 280, us);
        }

        private static SensorEntry Achar(List<SensorEntry> lista, string id)
        {
            foreach (SensorEntry s in lista) if (s.Id == id) return s;
            return new SensorEntry();
        }

        private static void IdaEVoltaDaConfiguracao()
        {
            Secao("ida e volta da configuracao");

            // Caminho explicito, nunca o real. A primeira versao deste teste
            // redirecionava %LOCALAPPDATA%, e nao funcionou:
            // Environment.GetFolderPath consulta o shell, nao a variavel de
            // ambiente. O isolamento nao isolava e a suite gravou por cima da
            // configuracao da maquina.
            string caixa = Path.Combine(Path.GetTempPath(), "MhiagosControlTeste" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(caixa);
            string arquivo = Path.Combine(caixa, "config.ini");

            try
            {
                Config c = new Config();
                c.Profiles.Add(Perfil("Padrão", "cpu:temp", "cpu:load", 85, 0, true, false, 0, 10));
                c.Profiles.Add(Perfil("CPU = GPU", "gpu:temp", "gpu:pow", 0, 120, false, true, 100, 1000));
                c.ActiveName = "CPU = GPU";
                c.ShowAllSensors = true;
                c.Language = T.EnUs;
                c.IdleBlankMinutes = 15;
                c.RotateSeconds = 20;
                c.SidebarCollapsed = true;
                c.MetricsChosen = true;
                c.MetricRange = 3600;
                c.MetricIds.Add("cpu:temp"); c.MetricSizes.Add(2);
                c.MetricIds.Add("gpu:load"); c.MetricSizes.Add(1);
                c.WindowW = 1280; c.WindowH = 940;
                c.GameProfiles = true;
                c.MapearJogo("cyberpunk2077.exe", "CPU = GPU");
                c.MapearJogo("LeagueClientUxRender.exe", "Padrão");
                c.SaveTo(arquivo);

                Verdade(File.Exists(arquivo), "gravou no arquivo do teste, e nao no real");

                Config lido = Config.LoadFrom(arquivo, false);
                Igual(2, lido.Profiles.Count, "quantidade de perfis");
                Igual("CPU = GPU", lido.ActiveName, "perfil ativo");
                Verdade(lido.ShowAllSensors, "preferencia de mostrar todos");
                Igual(T.EnUs, lido.Language, "idioma");

                Profile a = lido.Profiles[0], b = lido.Profiles[1];
                Igual("Padrão", a.Name, "acento no nome sobrevive");
                Igual("CPU = GPU", b.Name, "igual no nome sobrevive");
                Igual("cpu:temp", a.Panel1Id, "sensor do painel 1");
                Igual(85, a.Alert1, "limiar 1");
                Igual(120, b.Alert2, "limiar 2");
                Verdade(a.Percent, "porcentagem");
                Verdade(b.Fahrenheit, "Fahrenheit");
                Igual(10, a.Divisor2, "divisor 2");
                Igual(100, b.Divisor1, "divisor 1");
                Igual("CPU = GPU", lido.Active.Name, "Active resolve pelo nome");
                Verdade(lido.NameExists("padrão"), "nome existente ignora caixa");
                Igual(15, lido.IdleBlankMinutes, "minutos ate apagar");
                Igual(20, lido.RotateSeconds, "segundos do rodizio");
                Verdade(lido.SidebarCollapsed, "barra lateral recolhida");
                Verdade(lido.MetricsChosen, "marca de grade ja escolhida");
                Igual(3600, lido.MetricRange, "janela dos graficos");
                Igual(2, lido.MetricIds.Count, "cartoes gravados");
                Igual("cpu:temp", lido.MetricIds[0], "identificador do primeiro cartao");
                Igual(2, lido.MetricSize(0), "tamanho do primeiro cartao");
                Igual(1, lido.MetricSize(1), "tamanho do segundo cartao");

                Igual(1280, lido.WindowW, "largura da janela");
                Igual(940, lido.WindowH, "altura da janela");

                // O mapa de jogos: e o unico ajuste que faz o aplicativo AGIR
                // sozinho, entao perde-lo em silencio na gravacao seria trocar o
                // mostrador sem motivo aparente - ou deixar de trocar.
                Verdade(lido.GameProfiles, "perfil por jogo ligado");
                Igual(2, lido.GameKeys.Count, "dois jogos vinculados");
                Igual("CPU = GPU", lido.PerfilDoJogo("cyberpunk2077.exe"), "casamento do primeiro");
                Igual("Padrão", lido.PerfilDoJogo("LeagueClientUxRender.exe"), "casamento do segundo");

                // O executavel vem do RTSS e a caixa nao e garantida.
                Igual("CPU = GPU", lido.PerfilDoJogo("Cyberpunk2077.EXE"),
                      "o casamento ignora a caixa das letras");
                Igual(null, lido.PerfilDoJogo("outro.exe"), "jogo sem vinculo nao casa");
                Igual(null, lido.PerfilDoJogo(null), "nulo nao casa");

                // Vincular de novo TROCA, e nao duplica: duas linhas para o mesmo
                // executavel fariam o resultado depender da ordem de leitura.
                lido.MapearJogo("cyberpunk2077.exe", "Padrão");
                Igual(2, lido.GameKeys.Count, "revincular nao cria linha nova");
                Igual("Padrão", lido.PerfilDoJogo("cyberpunk2077.exe"), "e o vinculo novo vale");

                lido.DesmapearJogo("cyberpunk2077.exe");
                Igual(1, lido.GameKeys.Count, "desvincular remove");
                Igual(1, lido.GameProfileNames.Count, "e as duas listas continuam do mesmo tamanho");
                Igual(null, lido.PerfilDoJogo("cyberpunk2077.exe"), "e o casamento some");

                // A roda e derivada da marca de cada perfil, e nao uma segunda
                // lista: duas listas para a mesma coisa saem de sincronia
                // assim que alguem excluir um perfil.
                Igual(0, lido.Rotation.Count, "sem marca, a roda esta vazia");
                lido.Profiles[0].Rotate = true;
                Igual(1, lido.Rotation.Count, "um perfil marcado");
                lido.Profiles[1].Rotate = true;
                Igual(2, lido.Rotation.Count, "dois perfis marcados");
                lido.Profiles.RemoveAt(1);
                Igual(1, lido.Rotation.Count, "excluir o perfil tira ele da roda");
            }
            finally { try { Directory.Delete(caixa, true); } catch { } }
        }

        /// <summary>
        /// Confere Clone e a ida e volta pelo INI campo a campo, por reflexao.
        ///
        /// Escrito assim porque o jeito de errar aqui e sempre o mesmo:
        /// acrescentar um campo ao Profile e esquecer de uma das duas pontas.
        /// Um teste que listasse os campos a mao esqueceria junto - foi
        /// exatamente o que quase aconteceu com os limiares inferiores.
        /// </summary>
        private static void TodosOsCamposDoPerfil()
        {
            Secao("todos os campos do perfil");

            FieldInfo[] campos = typeof(Profile).GetFields(BindingFlags.Instance | BindingFlags.Public);
            Verdade(campos.Length > 0, "o perfil expoe campos a conferir");

            // Cada campo recebe um valor diferente do padrao: um Clone que
            // esquecesse de copiar passaria se o valor de teste coincidisse
            // com o que o construtor ja poe.
            Profile p = new Profile();
            int n = 1;
            foreach (FieldInfo f in campos)
            {
                if (f.FieldType == typeof(string)) f.SetValue(p, "campo" + n);
                else if (f.FieldType == typeof(int)) f.SetValue(p, n * 7);
                else if (f.FieldType == typeof(bool)) f.SetValue(p, !(bool)f.GetValue(p));
                else { Verdade(false, "tipo nao previsto em " + f.Name + ": ajuste o teste"); continue; }
                n++;
            }

            Profile copia = p.Clone();
            foreach (FieldInfo f in campos)
                Igual(f.GetValue(p), f.GetValue(copia), "Clone copia " + f.Name);

            string caixa = Path.Combine(Path.GetTempPath(), "MhiagosControlTeste" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(caixa);
            string arquivo = Path.Combine(caixa, "config.ini");
            try
            {
                Config c = new Config();
                c.Profiles.Add(p);
                c.ActiveName = p.Name;
                c.SaveTo(arquivo);

                Config lido = Config.LoadFrom(arquivo, false);
                Igual(1, lido.Profiles.Count, "gravou e leu um perfil");
                if (lido.Profiles.Count == 1)
                    foreach (FieldInfo f in campos)
                        Igual(f.GetValue(p), f.GetValue(lido.Profiles[0]), "o INI preserva " + f.Name);

                // exportar e importar e a terceira ponta do mesmo problema
                string erro;
                string exportado = Path.Combine(caixa, "perfil.ini");
                Verdade(Config.ExportProfile(p, exportado, out erro), "exportou o perfil");
                Profile volta = Config.ImportProfile(exportado, out erro);
                Verdade(volta != null, "importou o perfil de volta");
                if (volta != null)
                    foreach (FieldInfo f in campos)
                        Igual(f.GetValue(p), f.GetValue(volta), "a exportacao preserva " + f.Name);

                // Lixo nao pode virar perfil: LoadFrom nunca falha, entao sem a
                // conferencia de ImportProfile um arquivo qualquer entraria na
                // lista do usuario como um "Padrao" mudo.
                string lixo = Path.Combine(caixa, "lixo.txt");
                File.WriteAllText(lixo, "isto nao e um perfil\nnem de longe\n");
                Verdade(Config.ImportProfile(lixo, out erro) == null, "recusa arquivo sem perfil");
                Verdade(Config.ImportProfile(Path.Combine(caixa, "nao-existe.ini"), out erro) == null,
                        "recusa arquivo inexistente");

                // nome livre: importar duas vezes nao pode gerar dois perfis iguais
                Config d = new Config();
                d.Profiles.Clear();
                d.Profiles.Add(Perfil("Jogos", "a", "b", 0, 0, true, false, 0, 0));
                Igual("Jogos (2)", d.UniqueName("Jogos"), "primeiro nome livre");
                d.Profiles.Add(Perfil("Jogos (2)", "a", "b", 0, 0, true, false, 0, 0));
                Igual("Jogos (3)", d.UniqueName("Jogos"), "segundo nome livre");
                Igual("Vazio", d.UniqueName("Vazio"), "nome inedito passa inteiro");
            }
            finally { try { Directory.Delete(caixa, true); } catch { } }
        }

        private static Profile Perfil(string nome, string p1, string p2, int a1, int a2,
                                      bool pct, bool f, int d1, int d2)
        {
            Profile p = new Profile();
            p.Name = nome; p.Panel1Id = p1; p.Panel2Id = p2;
            p.Alert1 = a1; p.Alert2 = a2; p.Percent = pct; p.Fahrenheit = f;
            p.Divisor1 = d1; p.Divisor2 = d2;
            return p;
        }

        // ---------------- rodizio ----------------

        /// <summary>
        /// A posicao na roda, que so se ve depois de esperar um minuto olhando
        /// o cooler. Os numeros aqui sao em milissegundos, com periodo de 20 s
        /// e tres perfis - a configuracao que o usuario de fato monta.
        /// </summary>
        private static void RodizioDePerfis()
        {
            Secao("rodizio de perfis");

            Igual(0, TrayContext.IndiceDoRodizio(0, 20000, 3), "comeca no primeiro");
            Igual(0, TrayContext.IndiceDoRodizio(19999, 20000, 3), "fica ate o limite do periodo");
            Igual(1, TrayContext.IndiceDoRodizio(20000, 20000, 3), "vira no periodo exato");
            Igual(2, TrayContext.IndiceDoRodizio(40000, 20000, 3), "segue para o terceiro");
            Igual(0, TrayContext.IndiceDoRodizio(60000, 20000, 3), "da a volta");
            Igual(1, TrayContext.IndiceDoRodizio(80000, 20000, 3), "e continua girando");

            // Perder ciclos nao pode desalinhar: a posicao vem do relogio, e
            // nao de um contador que soma um "quando der". Meia hora depois a
            // posicao ainda e a que o relogio manda.
            Igual(0, TrayContext.IndiceDoRodizio(1800000, 20000, 3), "meia hora depois, sem deriva");

            // Um perfil so nao gira - e o caso de quem marcou so um.
            Igual(0, TrayContext.IndiceDoRodizio(999999, 20000, 1), "roda de um fica parada");

            // Entradas degeneradas nao podem estourar indice: quem chama usa o
            // resultado para indexar um vetor.
            Igual(0, TrayContext.IndiceDoRodizio(1000, 0, 3), "periodo zero nao divide por zero");
            Igual(0, TrayContext.IndiceDoRodizio(1000, -5, 3), "periodo negativo nao gira");
            Igual(0, TrayContext.IndiceDoRodizio(-1, 20000, 3), "tempo negativo nao gira");
            Igual(0, TrayContext.IndiceDoRodizio(1000, 20000, 0), "roda vazia devolve zero");

            for (long t = 0; t < 200000; t += 137)
            {
                int i = TrayContext.IndiceDoRodizio(t, 20000, 3);
                if (i < 0 || i > 2) { Verdade(false, "indice fora da roda em t=" + t); break; }
            }
            Verdade(true, "nenhum instante devolve indice fora da roda");
        }

        // ---------------- idioma ----------------

        /// <summary>
        /// Percorre todo texto de interface nos dois idiomas.
        ///
        /// E o teste que motivou a suite: uma traducao esquecida so aparece se
        /// alguem abrir a tela certa no idioma certo. Aqui, um texto que volte
        /// vazio ou identico nos dois idiomas aparece na hora - identico e
        /// suspeito, nao erro, entao so os que sabidamente nao mudam ficam de
        /// fora.
        /// </summary>
        private static void CompletudeDoIdioma()
        {
            Secao("completude do idioma");

            // Textos que sao iguais nos dois idiomas por serem nome proprio,
            // sigla ou simbolo - listados para que o resto seja cobrado.
            List<string> iguais = new List<string>(new string[]
            {
                // "Cooler" e a palavra usada nos dois idiomas: em portugues e
                // emprestimo consagrado, e traduzir por "resfriador" nomearia a
                // peca de um jeito que ninguem usa para procura-la.
                "AppName", "Ok", "PtBr", "EnUs", "Language", "CoolerCard",

                // Folha de especificacoes: sigla (BIOS), cognato exato (Total) e
                // emprestimos que o portugues tecnico usa sem traduzir. Traduzir
                // "Stepping" por "passo" ou "Threads" por "linhas de execucao"
                // afastaria o rotulo do termo que a pessoa vai procurar em
                // qualquer outra ferramenta.
                "SpecStepping", "SpecThreads", "SpecBiosVendor", "SpecTotal", "SpecDriver",
                "SpecVbios", "SpecSlots", "SpecTpm",
            });

            string antes = T.Language;
            try
            {
                int comparados = 0;
                foreach (PropertyInfo p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Static))
                {
                    if (p.PropertyType != typeof(string)) continue;

                    T.Language = T.PtBr;
                    string pt = (string)p.GetValue(null, null);
                    T.Language = T.EnUs;
                    string en = (string)p.GetValue(null, null);

                    Verdade(!string.IsNullOrEmpty(pt), "T." + p.Name + " tem texto em portugues");
                    Verdade(!string.IsNullOrEmpty(en), "T." + p.Name + " tem texto em ingles");

                    if (iguais.Contains(p.Name)) continue;
                    Verdade(pt != en, "T." + p.Name + " difere entre os idiomas");
                    comparados++;
                }
                Verdade(comparados > 40, "a varredura encontrou textos suficientes (" + comparados + ")");

                // Categoria e chave de agrupamento: o valor guardado nao muda,
                // so o rotulo de tela
                T.Language = T.PtBr;
                Igual("Memória", T.Category("Memória"), "categoria em portugues");
                T.Language = T.EnUs;
                Igual("Memory", T.Category("Memória"), "categoria em ingles");
                Igual("CPU", T.Category("CPU"), "CPU nao muda");
                Igual("", T.Category(""), "categoria vazia atravessa");
            }
            finally { T.Language = antes; }
        }

        // ---------------- infraestrutura ----------------

        private static void Secao(string nome)
        {
            Console.WriteLine();
            Console.WriteLine("== " + nome);
        }

        private static void Verdade(bool condicao, string descricao)
        {
            if (condicao) { _ok++; return; }
            _falhas++;
            Console.WriteLine("  FALHOU: " + descricao);
        }

        private static void Igual(object esperado, object obtido, string descricao)
        {
            string e = Convert.ToString(esperado, CultureInfo.InvariantCulture);
            string o = Convert.ToString(obtido, CultureInfo.InvariantCulture);
            if (e == o) { _ok++; return; }
            _falhas++;
            Console.WriteLine("  FALHOU: " + descricao + "  (esperado " + e + ", obtido " + o + ")");
        }
    }
}
