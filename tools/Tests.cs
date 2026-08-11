using System;
using System.Collections.Generic;
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
                "AppName", "Ok", "PtBr", "EnUs", "Language"
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
