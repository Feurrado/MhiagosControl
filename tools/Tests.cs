using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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
