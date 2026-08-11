using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    /// <summary>
    /// Taxa de quadros e tempo de quadro, lidos da memoria compartilhada do
    /// RivaTuner Statistics Server.
    ///
    /// Nenhuma das duas fontes de sensores sabe quantos quadros um jogo
    /// desenhou: isso nao esta em lugar nenhum do hardware, esta no processo que
    /// apresenta. Quem mede e o RTSS, que se enfia na cadeia de apresentacao do
    /// jogo e publica o resultado num mapeamento de memoria chamado
    /// RTSSSharedMemoryV2. E o mesmo caminho que o HWiNFO e o Afterburner usam,
    /// e nao exige privilegio nenhum para ler.
    ///
    /// Sem o RTSS instalado as leituras existem e ficam sem valor, em vez de
    /// sumirem da lista. Sumir faria "escolhi FPS e o mostrador apagou" parecer
    /// defeito do aplicativo, e o rodape do sensor diz exatamente o que falta.
    /// </summary>
    internal sealed class Rtss
    {
        public const string Prefixo = "rtss:";

        /// <summary>
        /// Janela das estatisticas derivadas, em segundos.
        ///
        /// Sao mínimo, média e pior caso sobre AS NOSSAS amostras - uma por
        /// segundo, cada uma ja sendo a media que o RTSS calculou no periodo
        /// dele. Nao e "1% low": isso exige o tempo de cada quadro, e nesta
        /// altura os quadros individuais nao existem mais. O rotulo diz a janela
        /// justamente para nao ser lido como outra coisa.
        /// </summary>
        public const int JanelaSeg = 60;

        private const string Mapa = "RTSSSharedMemoryV2";

        /// <summary>
        /// Assinatura do cabecalho: o literal multicaractere 'RTSS' do C.
        ///
        /// Em memoria little-endian ele fica como S, S, T, R - e nao na ordem em
        /// que se le o nome. A primeira versao comparava byte a byte com
        /// 'R','T','S','S' e desistia da leitura sempre, sem erro nenhum no
        /// registro: o aplicativo dizia "RTSS nao encontrado" com o RTSS aberto
        /// na bandeja.
        ///
        /// O teste nao pegou porque montava o mapeamento com a MESMA ordem
        /// errada. Um teste escrito a partir da suposicao confirma a suposicao;
        /// quem desempatou foi despejar o cabecalho da memoria de verdade, onde
        /// os quatro bytes se leem "SSTR".
        /// </summary>
        public const uint Assinatura = ('R' << 24) | ('T' << 16) | ('S' << 8) | 'S';

        // Cabecalho
        private const int HdrVersao = 4;
        private const int HdrTamEntrada = 8;
        private const int HdrInicioVetor = 12;
        private const int HdrTamVetor = 16;

        // Entrada de aplicativo
        private const int EntNome = 4;          // char[260], ANSI
        private const int EntNomeTam = 260;
        private const int EntTempo0 = 268;
        private const int EntTempo1 = 272;
        private const int EntQuadros = 276;
        private const int EntTempoDeQuadro = 280;   // microssegundos
        private const int EntMinimo = 284;          // tamanho minimo utilizavel

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        private DateTime _ultima = DateTime.MinValue;
        private DateTime _proximaTentativa = DateTime.MinValue;
        private bool _disponivel = false;
        private bool _avisado = false;

        private uint _pid = 0;
        private string _app = null;
        private float? _fps, _ft;

        /// <summary>
        /// Peca do rodape, como a ultima leitura a viu.
        ///
        /// Estatica de proposito. O jogo troca enquanto a janela esta aberta, e
        /// o rodape do cartao e montado uma vez so - dai um cartao marcar 757
        /// FPS e dizer "nenhum jogo em execucao" logo abaixo, que foi o texto
        /// verdadeiro no instante em que a grade nasceu. Quem le e quem desenha
        /// vivem no mesmo processo, entao um campo compartilhado resolve sem
        /// varrer a lista de sensores inteira a cada segundo por causa de um
        /// texto.
        /// </summary>
        private static volatile string _peca;

        public static string PecaAtual
        {
            get { string p = _peca; return string.IsNullOrEmpty(p) ? T.RtssMissing : p; }
        }

        private readonly float[] _serieFps = new float[JanelaSeg];
        private readonly float[] _serieFt = new float[JanelaSeg];
        private int _n = 0;

        /// <summary>
        /// Se a memoria compartilhada esta publicada NESTE instante.
        ///
        /// Sonda direta, para a interface poder dizer o estado sem depender do
        /// ciclo de leitura - inclusive logo depois de alguem instalar o RTSS,
        /// que e exatamente quando a resposta muda.
        /// </summary>
        public static bool Presente()
        {
            try
            {
                using (MemoryMappedFile m = MemoryMappedFile.OpenExisting(Mapa, MemoryMappedFileRights.Read))
                    return m != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// Caminho do winget, ou nulo quando ele nao existe na maquina.
        ///
        /// Windows 10 sem o App Installer nao tem winget, e nesse caso o botao
        /// de instalar precisa virar "abrir a pagina" em vez de falhar.
        /// </summary>
        public static string Winget()
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(path)) return null;

                foreach (string dir in path.Split(';'))
                {
                    string d = dir.Trim();
                    if (d.Length == 0) continue;
                    try
                    {
                        string alvo = Path.Combine(d, "winget.exe");
                        if (File.Exists(alvo)) return alvo;
                    }
                    catch { }   // entrada invalida no PATH nao derruba a busca
                }
            }
            catch { }
            return null;
        }

        /// <summary>Identificador do pacote oficial no repositorio do winget.</summary>
        public const string PacoteWinget = "Guru3D.RTSS";

        /// <summary>
        /// Runtime do Visual C++, que o RTSS exige e o pacote dele nao declara.
        ///
        /// Sem ele o RTSSHooksLoader64 nem chega a rodar: para com "VCRUNTIME140_1.dll
        /// nao foi encontrado" e o RTSS instalado nunca publica nada. Vai junto
        /// no mesmo comando porque instalar o RTSS sem isso e entregar meia
        /// instalacao. Numa maquina que ja tem, o winget so diz que ja tem.
        /// </summary>
        public const string PacoteRuntime = "Microsoft.VCRedist.2015+.x64";

        public const string Site = "https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/";

        // ---------------- iniciar com o Windows ----------------

        /// <summary>Argumento que poe o aplicativo no modo "so ajustar o RTSS".</summary>
        public const string ArgConfigurar = "--config-rtss";

        private const string ChaveInstalacao = @"SOFTWARE\WOW6432Node\Unwinder\RTSS";

        /// <summary>Onde o RTSS mora, segundo o proprio registro dele.</summary>
        public static string PastaDoRtss()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k =
                       Microsoft.Win32.Registry.LocalMachine.OpenSubKey(ChaveInstalacao))
                {
                    if (k != null)
                    {
                        string dir = k.GetValue("InstallDir") as string;
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                    }
                }
            }
            catch (Exception ex) { Log.Error("pasta do RTSS no registro", ex); }

            // Recuo para o caminho de fabrica: registro de 32 bits ausente nao
            // significa RTSS ausente.
            try
            {
                string padrao = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "RivaTuner Statistics Server");
                if (Directory.Exists(padrao)) return padrao;
            }
            catch { }
            return null;
        }

        public static string CaminhoDoConfig()
        {
            string dir = PastaDoRtss();
            return dir == null ? null : Path.Combine(dir, "Profiles", "Config");
        }

        /// <summary>
        /// Liga "iniciar com o Windows" e "iniciar minimizado" no Config do RTSS.
        ///
        /// Funcao pura, e o arquivo e preservado linha a linha. Nao e frescura:
        /// o Config guarda tambem o FnOffsetCache - os deslocamentos das funcoes
        /// de apresentacao que o RTSS descobriu sondando as DLLs do sistema.
        /// Reescrever o arquivo do zero faria ele redescobrir tudo, e nada disso
        /// e assunto nosso.
        /// </summary>
        public static string AjustarIni(string texto)
        {
            List<string> linhas = new List<string>(
                (texto ?? "").Replace("\r\n", "\n").Split('\n'));

            int secao = -1;
            for (int i = 0; i < linhas.Count; i++)
                if (EhSecao(linhas[i], "Settings")) { secao = i; break; }

            if (secao < 0)
            {
                // Sem a secao, ela nasce no fim. Nao no comeco: acima dela pode
                // estar uma secao sem cabecalho, e um par de chaves solto la em
                // cima seria lido como parte de outra coisa.
                if (linhas.Count > 0 && linhas[linhas.Count - 1].Trim().Length != 0) linhas.Add("");
                linhas.Add("[Settings]");
                linhas.Add("StartWithWindows=1");
                linhas.Add("StartMinimized=1");
                return string.Join("\r\n", linhas.ToArray());
            }

            int fim = linhas.Count;
            for (int i = secao + 1; i < linhas.Count; i++)
                if (EhSecaoQualquer(linhas[i])) { fim = i; break; }

            bool temInicio = false, temMin = false;
            for (int i = secao + 1; i < fim; i++)
            {
                string chave = Chave(linhas[i]);
                if (chave == "startwithwindows") { linhas[i] = "StartWithWindows=1"; temInicio = true; }
                else if (chave == "startminimized") { linhas[i] = "StartMinimized=1"; temMin = true; }
            }

            // O que faltar entra DENTRO da secao, antes do proximo cabecalho -
            // no fim do arquivo cairia em outra secao e nao valeria nada.
            int onde = fim;
            while (onde > secao + 1 && linhas[onde - 1].Trim().Length == 0) onde--;
            if (!temMin) linhas.Insert(onde, "StartMinimized=1");
            if (!temInicio) linhas.Insert(onde, "StartWithWindows=1");

            return string.Join("\r\n", linhas.ToArray());
        }

        private static bool EhSecaoQualquer(string linha)
        {
            string l = (linha ?? "").Trim();
            return l.Length >= 2 && l[0] == '[' && l[l.Length - 1] == ']';
        }

        private static bool EhSecao(string linha, string nome)
        {
            string l = (linha ?? "").Trim();
            return string.Equals(l, "[" + nome + "]", StringComparison.OrdinalIgnoreCase);
        }

        private static string Chave(string linha)
        {
            string l = (linha ?? "").Trim();
            int ig = l.IndexOf('=');
            return ig <= 0 ? "" : l.Substring(0, ig).Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Aplica o ajuste no arquivo, com o RTSS parado.
        ///
        /// A ordem importa: o RTSS reescreve o Config ao sair, entao editar com
        /// ele no ar seria escrever para ser desfeito no proximo fechamento.
        /// Por isso ele e encerrado antes e aberto de novo depois - e por isso
        /// esta operacao so acontece a pedido, nunca sozinha: derrubar o RTSS
        /// desengancha o jogo que estiver aberto.
        /// </summary>
        public static bool ConfigurarInicio(out string erro)
        {
            erro = null;
            string caminho = CaminhoDoConfig();
            if (caminho == null) { erro = "RTSS nao encontrado"; return false; }

            string exe = null;
            try
            {
                string dir = PastaDoRtss();
                if (dir != null) exe = Path.Combine(dir, "RTSS.exe");
            }
            catch { }

            bool estava = Encerrar();

            try
            {
                string antes = File.Exists(caminho) ? File.ReadAllText(caminho) : "";
                string depois = AjustarIni(antes);
                if (depois != antes) File.WriteAllText(caminho, depois);
                Log.Write("RTSS: inicio com o Windows ligado em " + caminho);
            }
            catch (Exception ex)
            {
                erro = ex.Message;
                Log.Error("ajuste do Config do RTSS", ex);
                return false;
            }
            finally
            {
                if (estava && exe != null && File.Exists(exe))
                {
                    try
                    {
                        System.Diagnostics.ProcessStartInfo psi =
                            new System.Diagnostics.ProcessStartInfo(exe);
                        psi.UseShellExecute = true;
                        psi.WorkingDirectory = Path.GetDirectoryName(exe);
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch (Exception ex) { Log.Error("religar o RTSS", ex); }
                }
            }
            return true;
        }

        /// <summary>Encerra o RTSS, com prazo. Devolve se ele estava rodando.</summary>
        private static bool Encerrar()
        {
            bool achou = false;
            try
            {
                foreach (System.Diagnostics.Process p in
                         System.Diagnostics.Process.GetProcessesByName("RTSS"))
                {
                    achou = true;
                    try
                    {
                        // Fechar pela janela primeiro: o RTSS grava o Config ao
                        // sair, e matar de saida perderia o que ele ainda nao
                        // tinha escrito - inclusive coisas que nao sao nossas.
                        if (!p.CloseMainWindow() || !p.WaitForExit(5000)) p.Kill();
                        p.WaitForExit(3000);
                    }
                    catch (Exception ex) { Log.Error("encerrar o RTSS", ex); }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex) { Log.Error("procurar o RTSS", ex); }
            return achou;
        }

        // ---------------- leitura ----------------

        /// <summary>
        /// Amostra no maximo uma vez por segundo.
        ///
        /// A cadencia nao pode vir de quantas vezes alguem chama: o BuildRaw
        /// roda por ciclo e tambem fora dele, quando a lista precisa ficar
        /// completa. Contar amostras por chamada encheria a janela de sessenta
        /// segundos com trinta segundos de leitura.
        /// </summary>
        private void Amostrar()
        {
            DateTime agora = DateTime.UtcNow;
            if ((agora - _ultima).TotalMilliseconds < 900) return;
            _ultima = agora;

            uint pid; string app; float? fps, ft;
            _disponivel = LerMemoria(out pid, out app, out fps, out ft);

            if (!_disponivel || pid == 0)
            {
                _pid = 0; _app = null; _fps = null; _ft = null; _n = 0;
                return;
            }

            // Trocou de jogo: a janela recomeca. Misturar o minimo de dois
            // programas diferentes daria um numero que nunca aconteceu.
            if (pid != _pid) { _pid = pid; _app = app; _n = 0; }

            _fps = fps; _ft = ft;
            if (fps.HasValue && ft.HasValue) Empurrar(fps.Value, ft.Value);
            else _n = 0;
        }

        private void Empurrar(float fps, float ft)
        {
            if (_n < JanelaSeg)
            {
                _serieFps[_n] = fps; _serieFt[_n] = ft; _n++;
                return;
            }
            Array.Copy(_serieFps, 1, _serieFps, 0, JanelaSeg - 1);
            Array.Copy(_serieFt, 1, _serieFt, 0, JanelaSeg - 1);
            _serieFps[JanelaSeg - 1] = fps;
            _serieFt[JanelaSeg - 1] = ft;
        }

        private bool LerMemoria(out uint pid, out string app, out float? fps, out float? ft)
        {
            pid = 0; app = null; fps = null; ft = null;

            // Sem o RTSS rodando, abrir o mapeamento lanca a cada tentativa.
            // Uma vez por segundo seria excecao de graca o dia inteiro.
            if (!_disponivel && DateTime.UtcNow < _proximaTentativa) return false;

            MemoryMappedFile mmf = null;
            MemoryMappedViewAccessor v = null;
            try
            {
                mmf = MemoryMappedFile.OpenExisting(Mapa, MemoryMappedFileRights.Read);
                v = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                if (v.ReadUInt32(0) != Assinatura) return false;
                if (v.ReadUInt32(HdrVersao) < 0x00020000) return false;

                uint tam = v.ReadUInt32(HdrTamEntrada);
                uint inicio = v.ReadUInt32(HdrInicioVetor);
                uint quantas = v.ReadUInt32(HdrTamVetor);

                // O tamanho da entrada vem do proprio cabecalho, e nao de uma
                // constante nossa: versoes do RTSS acrescentam campos no fim, e
                // um passo fixo passaria a ler o meio da entrada seguinte.
                if (tam < EntMinimo || quantas == 0 || quantas > 4096) return true;

                uint frente = PidEmPrimeiroPlano();
                int escolhida = -1;
                uint maisRecente = 0;

                for (uint i = 0; i < quantas; i++)
                {
                    long b = inicio + (long)i * tam;
                    uint p = v.ReadUInt32(b);
                    if (p == 0) continue;

                    uint t0 = v.ReadUInt32(b + EntTempo0);
                    uint t1 = v.ReadUInt32(b + EntTempo1);
                    if (t1 <= t0 || v.ReadUInt32(b + EntQuadros) == 0) continue;

                    // O jogo em primeiro plano ganha de qualquer outro; sem ele,
                    // vale quem desenhou por ultimo. Varios programas podem estar
                    // registrados ao mesmo tempo, e o que esta na frente e o que
                    // a pessoa esta olhando.
                    if (p == frente) { escolhida = (int)i; break; }
                    if (t1 >= maisRecente) { maisRecente = t1; escolhida = (int)i; }
                }
                if (escolhida < 0) return true;   // RTSS esta la, jogo nenhum esta

                long e = inicio + (long)escolhida * tam;
                pid = v.ReadUInt32(e);
                app = NomeDoApp(v, e);

                uint a0 = v.ReadUInt32(e + EntTempo0);
                uint a1 = v.ReadUInt32(e + EntTempo1);
                uint quadros = v.ReadUInt32(e + EntQuadros);
                uint us = v.ReadUInt32(e + EntTempoDeQuadro);

                if (a1 > a0) fps = quadros * 1000f / (a1 - a0);
                if (us > 0) ft = us / 1000f;
                return true;
            }
            catch (FileNotFoundException)
            {
                // RTSS nao esta rodando. Nao e erro, e ausencia.
                _proximaTentativa = DateTime.UtcNow.AddSeconds(10);
                return false;
            }
            catch (Exception ex)
            {
                _proximaTentativa = DateTime.UtcNow.AddSeconds(30);
                if (!_avisado) { _avisado = true; Log.Error("RTSS: leitura da memoria compartilhada", ex); }
                return false;
            }
            finally
            {
                if (v != null) v.Dispose();
                if (mmf != null) mmf.Dispose();
            }
        }

        private static string NomeDoApp(MemoryMappedViewAccessor v, long baseEntrada)
        {
            try
            {
                byte[] buf = new byte[EntNomeTam];
                v.ReadArray(baseEntrada + EntNome, buf, 0, buf.Length);

                int fim = Array.IndexOf(buf, (byte)0);
                if (fim < 0) fim = buf.Length;
                if (fim == 0) return null;

                string caminho = Encoding.Default.GetString(buf, 0, fim).Trim();
                if (caminho.Length == 0) return null;

                // So o executavel: o caminho inteiro nao cabe em rodape nenhum e
                // ainda exporia a pasta de instalacao numa captura de tela.
                int barra = caminho.LastIndexOf('\\');
                return barra >= 0 && barra < caminho.Length - 1
                     ? caminho.Substring(barra + 1) : caminho;
            }
            catch { return null; }
        }

        private static uint PidEmPrimeiroPlano()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return 0;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                return pid;
            }
            catch { return 0; }
        }

        // ---------------- publicacao ----------------

        /// <summary>
        /// As leituras desta fonte, sempre as mesmas seis.
        ///
        /// A lista nao encolhe quando nao ha jogo: um identificador que aparece
        /// e some conforme o que esta aberto quebraria qualquer perfil salvo, e
        /// o cartao da aba Metricas sumiria da grade ao fechar o jogo, levando o
        /// historico junto.
        /// </summary>
        public List<SensorEntry> Ler()
        {
            Amostrar();

            string peca = !_disponivel ? T.RtssMissing
                        : (string.IsNullOrEmpty(_app) ? T.RtssIdle : _app);
            _peca = peca;

            float? fpsMin = null, fpsMed = null, ftMed = null, ftPior = null;

            // Tres amostras e o minimo para a palavra "minimo" significar algo:
            // com uma so, o minimo e o valor atual com outro nome.
            if (_n >= 3)
            {
                float min = float.MaxValue, somaFps = 0, somaFt = 0, pior = float.MinValue;
                for (int i = 0; i < _n; i++)
                {
                    if (_serieFps[i] < min) min = _serieFps[i];
                    if (_serieFt[i] > pior) pior = _serieFt[i];
                    somaFps += _serieFps[i];
                    somaFt += _serieFt[i];
                }
                fpsMin = min; fpsMed = somaFps / _n;
                ftPior = pior; ftMed = somaFt / _n;
            }

            List<SensorEntry> saida = new List<SensorEntry>(6);
            Add(saida, "fps", T.MetricFps, "FPS", _fps, peca);
            Add(saida, "fps.min", T.MetricFpsMin, "FPS", fpsMin, peca);
            Add(saida, "fps.avg", T.MetricFpsAvg, "FPS", fpsMed, peca);
            Add(saida, "frametime", T.MetricFrametime, "ms", _ft, peca);
            Add(saida, "frametime.avg", T.MetricFrametimeAvg, "ms", ftMed, peca);
            Add(saida, "frametime.max", T.MetricFrametimeMax, "ms", ftPior, peca);
            return saida;
        }

        private static void Add(List<SensorEntry> destino, string sufixo, string nome,
                                string unidade, float? valor, string peca)
        {
            SensorEntry e = new SensorEntry();
            e.Id = Prefixo + sufixo;
            e.Hardware = peca;
            e.Category = Sensors.CategoriaJogos;
            e.Name = nome;
            e.Unit = unidade;

            // Factor nao carrega unidade propria, que e o que se quer aqui: a
            // unidade vem do campo acima. Temperatura ou carga dariam conversao
            // e simbolo que nao valem para quadro por segundo.
            e.Type = SensorType.Factor;
            e.Value = valor;
            e.Source = "RTSS";
            e.Label = nome + " - " + peca;
            destino.Add(e);
        }
    }
}
