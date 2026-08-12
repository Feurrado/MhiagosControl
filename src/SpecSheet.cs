using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Text;

namespace MhiagosControl
{
    /// <summary>Um bloco da folha: titulo e as linhas "rotulo / valor".</summary>
    public class SpecGrupo
    {
        public readonly string Titulo;
        public readonly List<string[]> Linhas = new List<string[]>();

        public SpecGrupo(string titulo) { Titulo = titulo; }

        /// <summary>Acrescenta a linha. Valor vazio nao entra.</summary>
        public void Por(string rotulo, string valor)
        {
            if (string.IsNullOrEmpty(valor)) return;
            Linhas.Add(new string[] { rotulo, valor.Trim() });
        }
    }

    /// <summary>
    /// A folha de especificacoes da maquina, no espirito do CPU-Z.
    ///
    /// Aqui - e SO aqui - o aplicativo usa WMI. O resto do programa evita de
    /// proposito: Win32_Processor sozinho custou 1427 ms medidos nesta maquina,
    /// e a barra lateral, que mostra tres linhas no arranque, nao pode pagar
    /// isso. Uma aba que a pessoa abre de vez em quando pode - desde que pague
    /// UMA vez, fora da thread da interface, e mostre que esta trabalhando.
    ///
    /// O que nao esta aqui, e nao por esquecimento: microcodigo, tensao de
    /// nucleo, timings de memoria (CL-tRCD-tRP-tRAS) e contagem de transistores
    /// nao existem em WMI. Sairiam de CPUID em assembly, de leitura do SPD pelo
    /// barramento SMBus e de uma tabela de modelos mantida a mao - tres
    /// dependencias novas, cada uma com o seu jeito de quebrar em hardware que
    /// eu nao tenho para testar.
    /// </summary>
    public static class SpecSheet
    {
        private static readonly object _trava = new object();
        private static List<SpecGrupo> _folha;

        /// <summary>A folha ja coletada, ou nulo enquanto ninguem coletou.</summary>
        public static List<SpecGrupo> Folha
        {
            get { lock (_trava) { return _folha; } }
        }

        /// <summary>
        /// Coleta tudo. LENTA - alguns segundos no pior caso.
        ///
        /// Quem chama e responsavel por nao estar na thread da interface. O
        /// resultado fica guardado: hardware nao muda enquanto a maquina esta
        /// ligada, e recoletar a cada visita a aba pagaria o pedagio de novo
        /// para redesenhar as mesmas linhas.
        /// </summary>
        public static List<SpecGrupo> Coletar(List<SensorEntry> sensores)
        {
            lock (_trava) { if (_folha != null) return _folha; }

            List<SpecGrupo> g = new List<SpecGrupo>();
            try
            {
                g.Add(Processador());
                g.Add(PlacaMae());
                g.Add(Memoria());
                g.Add(Video(sensores));
                g.Add(Armazenamento());
                g.Add(Rede());
                g.Add(Sistema());
            }
            catch (Exception ex) { Log.Error("folha de especificacoes", ex); }

            // Grupo sem nenhuma linha nao vira cartao vazio na tela.
            List<SpecGrupo> uteis = new List<SpecGrupo>();
            foreach (SpecGrupo x in g) if (x != null && x.Linhas.Count > 0) uteis.Add(x);

            lock (_trava) { _folha = uteis; }
            return uteis;
        }

        // ---------------- grupos ----------------

        private static SpecGrupo Processador()
        {
            SpecGrupo g = new SpecGrupo(T.SpecCpu);
            ManagementObject o = Primeiro("Win32_Processor");
            if (o == null) return g;

            g.Por(T.SpecModel, Texto(o, "Name"));
            g.Por(T.SpecVendor, Texto(o, "Manufacturer"));

            // "AMD64 Family 25 Model 33 Stepping 2" - a mesma linha que o CPU-Z
            // reparte em tres campos. Sai inteira e repartida: inteira e o que se
            // copia para um relatorio, repartida e o que se compara com uma
            // tabela de modelos.
            string desc = Texto(o, "Description");
            g.Por(T.SpecSpecification, desc);
            string fam, mod, step;
            if (Repartir(desc, out fam, out mod, out step))
            {
                g.Por(T.SpecFamily, fam);
                g.Por(T.SpecModelNum, mod);
                g.Por(T.SpecStepping, step);
            }

            g.Por(T.SpecSocket, Texto(o, "SocketDesignation"));

            int nucleos = Inteiro(o, "NumberOfCores");
            int threads = Inteiro(o, "NumberOfLogicalProcessors");
            if (nucleos > 0) g.Por(T.SpecCores, nucleos.ToString(CultureInfo.InvariantCulture));
            if (threads > 0) g.Por(T.SpecThreads, threads.ToString(CultureInfo.InvariantCulture));

            int maxClock = Inteiro(o, "MaxClockSpeed");
            if (maxClock > 0) g.Por(T.SpecMaxClock, maxClock + " MHz");

            int bus = Inteiro(o, "ExtClock");
            if (bus > 0) g.Por(T.SpecBusClock, bus + " MHz");

            // O WMI publica cache em KB. L3 de 32768 KB dito assim obriga quem le
            // a dividir de cabeca para reconhecer os 32 MB da peca.
            //
            // O L1 nao esta no Win32_Processor - so no Win32_CacheMemory, que e
            // uma consulta separada. E onde o CPU-Z mostra a hierarquia inteira,
            // faltar o primeiro nivel e a falha mais visivel.
            g.Por(T.SpecL1, EmMegabytes(Cache(3)));
            g.Por(T.SpecL2, EmMegabytes(Inteiro(o, "L2CacheSize")));
            g.Por(T.SpecL3, EmMegabytes(Inteiro(o, "L3CacheSize")));

            object virt = Campo(o, "VirtualizationFirmwareEnabled");
            if (virt is bool) g.Por(T.SpecVirtualization, ((bool)virt) ? T.On : T.Off);

            return g;
        }

        private static SpecGrupo PlacaMae()
        {
            SpecGrupo g = new SpecGrupo(T.SpecBoard);

            ManagementObject b = Primeiro("Win32_BaseBoard");
            if (b != null)
            {
                g.Por(T.SpecVendor, Texto(b, "Manufacturer"));
                g.Por(T.SpecModel, Texto(b, "Product"));

                // "Default string" e o texto de exemplo que o montador nao
                // preencheu; mostra-lo seria dar cara de dado a um campo vazio.
                string v = Texto(b, "Version");
                if (!string.Equals(v, "Default string", StringComparison.OrdinalIgnoreCase))
                    g.Por(T.SpecRevision, v);
            }

            ManagementObject bios = Primeiro("Win32_BIOS");
            if (bios != null)
            {
                g.Por(T.SpecBiosVendor, Texto(bios, "Manufacturer"));
                g.Por(T.SpecBiosVersion, Texto(bios, "SMBIOSBIOSVersion"));
                g.Por(T.SpecBiosDate, DataWmi(Texto(bios, "ReleaseDate")));
            }
            return g;
        }

        private static SpecGrupo Memoria()
        {
            SpecGrupo g = new SpecGrupo(T.SpecRam);

            List<ManagementObject> mods = Todos("Win32_PhysicalMemory");
            if (mods.Count == 0) return g;

            ulong total = 0;
            int velocidade = 0;
            string tipo = null;
            List<string> canais = new List<string>();

            foreach (ManagementObject m in mods)
            {
                total += Longo(m, "Capacity");

                int vel = Inteiro(m, "ConfiguredClockSpeed");
                if (vel <= 0) vel = Inteiro(m, "Speed");
                if (vel > velocidade) velocidade = vel;

                if (tipo == null) tipo = TipoDeMemoria(Inteiro(m, "SMBIOSMemoryType"));

                string banco = Texto(m, "BankLabel");
                if (!string.IsNullOrEmpty(banco) && !canais.Contains(banco)) canais.Add(banco);
            }

            if (total > 0) g.Por(T.SpecTotal, EmGigabytes(total));
            g.Por(T.SpecType, tipo);
            if (velocidade > 0) g.Por(T.SpecMemSpeed, velocidade + " MT/s");
            if (canais.Count > 0)
                g.Por(T.SpecChannels, canais.Count.ToString(CultureInfo.InvariantCulture));
            g.Por(T.SpecModules, mods.Count.ToString(CultureInfo.InvariantCulture));

            // Slots ocupados e o teto da placa: juntos respondem "da para pôr
            // mais memoria?", que e a unica pergunta que se faz olhando isto.
            ManagementObject arranjo = Primeiro("Win32_PhysicalMemoryArray");
            if (arranjo != null)
            {
                int slots = Inteiro(arranjo, "MemoryDevices");
                if (slots > 0) g.Por(T.SpecSlots, T.SpecSlotsUsed(mods.Count, slots));

                // MaxCapacityEx vem em KB.
                ulong teto = Longo(arranjo, "MaxCapacityEx");
                if (teto > 0) g.Por(T.SpecMaxRam, EmGigabytes(teto * 1024UL));
            }

            // Um pente por linha, como a aba SPD do CPU-Z. O numero de peca e o
            // que se procura para comprar o pente igual.
            int i = 1;
            foreach (ManagementObject m in mods)
            {
                string slot = Texto(m, "BankLabel");
                if (string.IsNullOrEmpty(slot)) slot = Texto(m, "DeviceLocator");
                if (string.IsNullOrEmpty(slot)) slot = i.ToString(CultureInfo.InvariantCulture);

                StringBuilder sb = new StringBuilder();
                sb.Append(EmGigabytes(Longo(m, "Capacity")));

                string fab = Texto(m, "Manufacturer");
                if (!string.IsNullOrEmpty(fab)) sb.Append("  ·  ").Append(fab);

                string peca = Texto(m, "PartNumber");
                if (!string.IsNullOrEmpty(peca)) sb.Append("  ·  ").Append(peca.Trim());

                g.Por(slot, sb.ToString());
                i++;
            }
            return g;
        }

        private static SpecGrupo Video(List<SensorEntry> sensores)
        {
            SpecGrupo g = new SpecGrupo(T.SpecGpu);
            ManagementObject o = Primeiro("Win32_VideoController");
            if (o == null) return g;

            g.Por(T.SpecModel, Texto(o, "Name"));

            using (Microsoft.Win32.RegistryKey k = ChaveDaGpu())
            {
                g.Por(T.SpecChip, TextoBinario(k, "HardwareInformation.ChipType")
                                  ?? Texto(o, "VideoProcessor"));

                // A VRAM sai do REGISTRO, e nao de AdapterRAM nem de MemorySize.
                //
                // Os dois sao uint32: numa placa de 8 GB ambos devolvem
                // 4293918720, que sao os 4 GiB onde o campo estourou. Medido
                // nesta maquina - e o numero errado e plausivel, que e o pior
                // tipo, porque passa despercebido. O qwMemorySize, ao lado deles
                // na mesma chave, e de 64 bits e devolve 8589934592.
                //
                // O sensor fica de reserva: em placa cujo driver nao grava a
                // chave, ele ainda responde.
                ulong vram = Longo64(k, "HardwareInformation.qwMemorySize");
                g.Por(T.SpecVram, vram > 0 ? EmGigabytes(vram)
                                           : SystemInfo.From(sensores).GpuMemoria);

                // "113-4E3531U-O4V" - o numero da VBIOS, que e o que se compara
                // com a pagina do fabricante para saber se ha atualizacao.
                g.Por(T.SpecVbios, TextoBinario(k, "HardwareInformation.BiosString"));
            }

            // "PCI\VEN_1002&DEV_67DF&SUBSYS_E3531DA2&REV_E7" - os mesmos campos
            // que o GPU-Z mostra, so que crus.
            string pnp = Texto(o, "PNPDeviceID");
            g.Por(T.SpecDeviceId, IdDePci(pnp, "DEV"));
            g.Por(T.SpecVendorId, IdDePci(pnp, "VEN"));
            g.Por(T.SpecRevision, IdDePci(pnp, "REV"));

            g.Por(T.SpecDriver, Texto(o, "DriverVersion"));
            g.Por(T.SpecDriverDate, DataWmi(Texto(o, "DriverDate")));

            int lx = Inteiro(o, "CurrentHorizontalResolution");
            int ly = Inteiro(o, "CurrentVerticalResolution");
            int hz = Inteiro(o, "CurrentRefreshRate");
            if (lx > 0 && ly > 0)
                g.Por(T.SpecResolution, lx + " × " + ly + (hz > 0 ? "  ·  " + hz + " Hz" : ""));

            foreach (string mon in Monitores()) g.Por(T.SpecMonitor, mon);

            return g;
        }

        /// <summary>
        /// Nome comercial de cada monitor ligado.
        ///
        /// Vem do EDID, publicado em root\wmi - o Win32_DesktopMonitor devolve
        /// "Monitor PnP Generico" para praticamente tudo desde o Windows 8. Aqui
        /// sai "LG ULTRAWIDE", que e o que a pessoa reconhece.
        /// </summary>
        private static List<string> Monitores()
        {
            List<string> saida = new List<string>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "root\\wmi", "SELECT * FROM WmiMonitorID"))
                using (ManagementObjectCollection c = s.Get())
                    foreach (ManagementObject o in c)
                    {
                        string n = DeCodigos(Campo(o, "UserFriendlyName") as ushort[]);
                        if (!string.IsNullOrEmpty(n) && !saida.Contains(n)) saida.Add(n);
                    }
            }
            catch (Exception ex) { Log.Error("consulta de monitores", ex); }
            return saida;
        }

        /// <summary>O EDID guarda o nome como vetor de codigos, terminado em zero.</summary>
        private static string DeCodigos(ushort[] v)
        {
            if (v == null) return null;
            StringBuilder sb = new StringBuilder();
            foreach (ushort c in v)
            {
                if (c == 0) break;
                sb.Append((char)c);
            }
            return sb.ToString().Trim();
        }

        private static SpecGrupo Armazenamento()
        {
            SpecGrupo g = new SpecGrupo(T.SpecStorage);

            // O tipo NAO vem do Win32_DiskDrive.
            //
            // O MediaType dele responde "Fixed hard disk media" para os tres
            // discos desta maquina, dois dos quais sao SSD - e o InterfaceType
            // chama de "IDE" um SATA e de "SCSI" um NVMe. Quem sabe a verdade e o
            // MSFT_PhysicalDisk, no espaco de nomes de armazenamento.
            Dictionary<string, string> tipos = TiposDeDisco();

            foreach (ManagementObject d in Todos("Win32_DiskDrive"))
            {
                string modelo = Texto(d, "Model");
                if (string.IsNullOrEmpty(modelo)) continue;

                string tam = EmGigabytes(Longo(d, "Size"));
                string extra;
                tipos.TryGetValue(modelo.Trim(), out extra);

                g.Por(tam, modelo.Trim() + (string.IsNullOrEmpty(extra) ? "" : "  ·  " + extra));
            }
            return g;
        }

        /// <summary>Modelo -> "SSD - NVMe", pelo espaco de nomes de armazenamento.</summary>
        private static Dictionary<string, string> TiposDeDisco()
        {
            Dictionary<string, string> mapa = new Dictionary<string, string>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "root\\Microsoft\\Windows\\Storage", "SELECT * FROM MSFT_PhysicalDisk"))
                using (ManagementObjectCollection c = s.Get())
                    foreach (ManagementObject o in c)
                    {
                        string nome = Texto(o, "FriendlyName");
                        if (string.IsNullOrEmpty(nome)) continue;

                        string t = Midia(Inteiro(o, "MediaType"));
                        string b = Barramento(Inteiro(o, "BusType"));
                        string v = Junta(t, b);
                        if (!string.IsNullOrEmpty(v)) mapa[nome.Trim()] = v;
                    }
            }
            catch (Exception ex) { Log.Error("tipos de disco", ex); }
            return mapa;
        }

        private static string Midia(int c)
        {
            if (c == 3) return "HDD";
            if (c == 4) return "SSD";
            if (c == 5) return "SCM";
            return null;
        }

        private static string Barramento(int c)
        {
            if (c == 11) return "SATA";
            if (c == 17) return "NVMe";
            if (c == 7) return "USB";
            if (c == 8) return "RAID";
            if (c == 10) return "SAS";
            return null;
        }

        private static string Junta(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + "  ·  " + b;
        }

        /// <summary>
        /// Adaptadores de rede fisicos e ligados.
        ///
        /// SEM o endereco MAC, embora ele esteja a um campo de distancia: o MAC
        /// identifica a maquina de forma unica e permanente, e esta tela existe
        /// para ser colada em relatorio publico. Vale a mesma regra do nome de
        /// usuario - o que identifica a pessoa fica de fora.
        /// </summary>
        private static SpecGrupo Rede()
        {
            SpecGrupo g = new SpecGrupo(T.SpecNetwork);
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True AND NetEnabled=True"))
                using (ManagementObjectCollection c = s.Get())
                    foreach (ManagementObject o in c)
                    {
                        string nome = Texto(o, "Name");
                        if (string.IsNullOrEmpty(nome)) continue;

                        // O nome do adaptador vai no VALOR, e nao no rotulo:
                        // "Realtek Gaming GbE Family Controller" tem 36
                        // caracteres e nao cabe na coluna de rotulos, que e
                        // estreita de proposito para os valores se alinharem.
                        g.Por(T.SpecModel, nome);
                        g.Por(T.SpecLinkSpeed, EmBitsPorSegundo(Longo(o, "Speed")));
                    }
            }
            catch (Exception ex) { Log.Error("adaptadores de rede", ex); }
            return g;
        }

        private static string EmBitsPorSegundo(ulong bps)
        {
            if (bps >= 1000000000UL)
            {
                double gb = bps / 1000000000.0;
                return (gb == Math.Floor(gb) ? gb.ToString("0") : gb.ToString("0.0")) + " Gb/s";
            }
            if (bps >= 1000000UL) return (bps / 1000000UL) + " Mb/s";
            return null;
        }

        private static SpecGrupo Sistema()
        {
            SpecGrupo g = new SpecGrupo(T.SpecOs);

            // Sem nome de maquina nem de usuario, de proposito: esta tela vai
            // parar em captura de tela dentro de issue, e nada aqui vale expor
            // quem e a pessoa.
            SystemInfo info = SystemInfo.From(null);
            g.Por(T.SpecVersion, info.Sistema);
            g.Por(T.SpecArch, Environment.Is64BitOperatingSystem ? "x64" : "x86");

            ManagementObject os = Primeiro("Win32_OperatingSystem");
            if (os != null)
            {
                DateTime bota = InstanteWmi(Texto(os, "LastBootUpTime"));
                if (bota != DateTime.MinValue && bota <= DateTime.Now)
                    g.Por(T.SpecUptime, T.Duracao(DateTime.Now - bota));

                g.Por(T.SpecInstalled, DataWmi(Texto(os, "InstallDate")));
            }

            g.Por(T.SpecSecureBoot, InicializacaoSegura());
            g.Por(T.SpecTpm, Tpm());
            g.Por(".NET", Environment.Version.ToString());
            return g;
        }

        /// <summary>
        /// Estado da inicializacao segura, pelo registro.
        ///
        /// Pelo registro e nao por WMI porque a leitura e imediata: e um DWORD
        /// numa chave conhecida. A alternativa seria mais uma consulta de
        /// centenas de milissegundos para saber um sim ou nao.
        /// </summary>
        private static string InicializacaoSegura()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
                {
                    if (k == null) return null;
                    object v = k.GetValue("UEFISecureBootEnabled");
                    if (v == null) return null;
                    return Convert.ToInt32(v, CultureInfo.InvariantCulture) == 1 ? T.On : T.Off;
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// Versao do TPM, quando o Windows deixa consultar.
        ///
        /// Este e o unico campo da folha que exige privilegio: sem elevacao a
        /// consulta e negada, e a negativa demora - 5,1 s medidos. O aplicativo
        /// roda elevado, entao o caminho normal e o rapido; a captura existe para
        /// que um ambiente que negue nao leve a folha inteira junto.
        /// </summary>
        private static string Tpm()
        {
            // Nao tenta sem privilegio.
            //
            // A consulta ao TPM e a unica da folha que exige elevacao, e a
            // NEGATIVA e cara: 5,1 s medidos, contra 1,4 s de tudo o mais junto.
            // Perguntar sabendo que a resposta sera "acesso negado" so gasta o
            // tempo de quem esta esperando a tela.
            if (!Elevado()) return null;

            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "root\\cimv2\\security\\microsofttpm", "SELECT * FROM Win32_Tpm"))
                using (ManagementObjectCollection c = s.Get())
                    foreach (ManagementObject o in c)
                    {
                        object lig = Campo(o, "IsEnabled_InitialValue");
                        bool ativo = (lig is bool) && (bool)lig;

                        string versao = Texto(o, "SpecVersion");
                        if (!string.IsNullOrEmpty(versao))
                        {
                            // "2.0, 0, 1.38" - so a primeira parte e a versao.
                            int v = versao.IndexOf(',');
                            if (v > 0) versao = versao.Substring(0, v).Trim();
                        }

                        if (string.IsNullOrEmpty(versao)) return ativo ? T.On : T.Off;
                        return versao + "  ·  " + (ativo ? T.On : T.Off);
                    }
            }
            catch (Exception ex) { Log.Error("consulta do TPM", ex); }
            return null;
        }

        private static bool Elevado()
        {
            try
            {
                System.Security.Principal.WindowsPrincipal p =
                    new System.Security.Principal.WindowsPrincipal(
                        System.Security.Principal.WindowsIdentity.GetCurrent());
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// <summary>"20260808204115.694344-180" -> data e hora locais.</summary>
        internal static DateTime InstanteWmi(string bruto)
        {
            if (string.IsNullOrEmpty(bruto) || bruto.Length < 14) return DateTime.MinValue;
            try
            {
                int ano = int.Parse(bruto.Substring(0, 4), CultureInfo.InvariantCulture);
                int mes = int.Parse(bruto.Substring(4, 2), CultureInfo.InvariantCulture);
                int dia = int.Parse(bruto.Substring(6, 2), CultureInfo.InvariantCulture);
                int hh = int.Parse(bruto.Substring(8, 2), CultureInfo.InvariantCulture);
                int mm = int.Parse(bruto.Substring(10, 2), CultureInfo.InvariantCulture);
                int ss = int.Parse(bruto.Substring(12, 2), CultureInfo.InvariantCulture);
                if (ano < 1980 || mes < 1 || mes > 12 || dia < 1 || dia > 31) return DateTime.MinValue;
                if (hh > 23 || mm > 59 || ss > 59) return DateTime.MinValue;
                return new DateTime(ano, mes, dia, hh, mm, ss);
            }
            catch { return DateTime.MinValue; }
        }

        // ---------------- WMI e formatacao ----------------

        private static List<ManagementObject> Todos(string classe)
        {
            List<ManagementObject> saida = new List<ManagementObject>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM " + classe))
                using (ManagementObjectCollection c = s.Get())
                    foreach (ManagementObject o in c) saida.Add(o);
            }
            catch (Exception ex) { Log.Error("consulta WMI " + classe, ex); }
            return saida;
        }

        private static ManagementObject Primeiro(string classe)
        {
            List<ManagementObject> l = Todos(classe);
            return l.Count > 0 ? l[0] : null;
        }

        /// <summary>Tamanho instalado do nivel de cache pedido, em KB.</summary>
        private static int Cache(int nivel)
        {
            foreach (ManagementObject o in Todos("Win32_CacheMemory"))
                if (Inteiro(o, "Level") == nivel)
                    return Inteiro(o, "InstalledSize");
            return 0;
        }

        /// <summary>
        /// A chave do adaptador de video no registro.
        ///
        /// Fica sob a classe de dispositivos de tela, numerada 0000, 0001... Uma
        /// maquina com video integrado e placa dedicada tem as duas, e vale a
        /// primeira que declara memoria: a integrada costuma nao declarar.
        /// </summary>
        private static Microsoft.Win32.RegistryKey ChaveDaGpu()
        {
            const string Classe =
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            try
            {
                using (Microsoft.Win32.RegistryKey raiz =
                    Microsoft.Win32.Registry.LocalMachine.OpenSubKey(Classe))
                {
                    if (raiz == null) return null;

                    Microsoft.Win32.RegistryKey reserva = null;
                    foreach (string nome in raiz.GetSubKeyNames())
                    {
                        if (nome.Length != 4) continue;

                        Microsoft.Win32.RegistryKey k = raiz.OpenSubKey(nome);
                        if (k == null) continue;

                        if (Longo64(k, "HardwareInformation.qwMemorySize") > 0) return k;
                        if (reserva == null) reserva = k; else k.Close();
                    }
                    return reserva;
                }
            }
            catch (Exception ex) { Log.Error("chave da placa de video", ex); return null; }
        }

        private static ulong Longo64(Microsoft.Win32.RegistryKey k, string nome)
        {
            if (k == null) return 0;
            try
            {
                object v = k.GetValue(nome);
                if (v == null) return 0;
                return Convert.ToUInt64(v, CultureInfo.InvariantCulture);
            }
            catch { return 0; }
        }

        /// <summary>
        /// Valor binario que na verdade e texto em UTF-16.
        ///
        /// O driver grava ChipType e BiosString como REG_BINARY, e nao como
        /// string: sao bytes de caracteres de dois em dois, terminados em zero.
        /// Lidos como bytes crus viram "65 0 77 0 68 0..." na tela.
        /// </summary>
        private static string TextoBinario(Microsoft.Win32.RegistryKey k, string nome)
        {
            if (k == null) return null;
            try
            {
                byte[] b = k.GetValue(nome) as byte[];
                if (b == null || b.Length < 2) return null;

                string s = Encoding.Unicode.GetString(b);
                int fim = s.IndexOf('\0');
                if (fim >= 0) s = s.Substring(0, fim);
                s = s.Trim();
                return s.Length == 0 ? null : s;
            }
            catch { return null; }
        }

        private static object Campo(ManagementObject o, string nome)
        {
            try { return o[nome]; }
            catch { return null; }
        }

        private static string Texto(ManagementObject o, string nome)
        {
            object v = Campo(o, nome);
            return v == null ? null : Convert.ToString(v).Trim();
        }

        private static int Inteiro(ManagementObject o, string nome)
        {
            object v = Campo(o, nome);
            if (v == null) return 0;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static ulong Longo(ManagementObject o, string nome)
        {
            object v = Campo(o, nome);
            if (v == null) return 0;
            try { return Convert.ToUInt64(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        /// <summary>"AMD64 Family 25 Model 33 Stepping 2" -> 25, 33, 2.</summary>
        internal static bool Repartir(string desc, out string fam, out string mod, out string step)
        {
            fam = mod = step = null;
            if (string.IsNullOrEmpty(desc)) return false;

            fam = Depois(desc, "Family");
            mod = Depois(desc, "Model");
            step = Depois(desc, "Stepping");
            return fam != null || mod != null || step != null;
        }

        private static string Depois(string texto, string chave)
        {
            int i = texto.IndexOf(chave, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;

            int j = i + chave.Length;
            while (j < texto.Length && texto[j] == ' ') j++;

            int k = j;
            while (k < texto.Length && char.IsLetterOrDigit(texto[k])) k++;
            return k > j ? texto.Substring(j, k - j) : null;
        }

        /// <summary>"PCI\VEN_1002&amp;DEV_67DF&amp;..." -> "67DF".</summary>
        internal static string IdDePci(string pnp, string campo)
        {
            if (string.IsNullOrEmpty(pnp)) return null;

            string marca = campo + "_";
            int i = pnp.IndexOf(marca, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;

            int j = i + marca.Length;
            int k = j;
            while (k < pnp.Length && Uri.IsHexDigit(pnp[k])) k++;
            return k > j ? pnp.Substring(j, k - j).ToUpperInvariant() : null;
        }

        /// <summary>"20251028000000.000000+000" -> "28/10/2025".</summary>
        internal static string DataWmi(string bruto)
        {
            if (string.IsNullOrEmpty(bruto) || bruto.Length < 8) return null;
            try
            {
                int ano = int.Parse(bruto.Substring(0, 4), CultureInfo.InvariantCulture);
                int mes = int.Parse(bruto.Substring(4, 2), CultureInfo.InvariantCulture);
                int dia = int.Parse(bruto.Substring(6, 2), CultureInfo.InvariantCulture);
                if (ano < 1980 || mes < 1 || mes > 12 || dia < 1 || dia > 31) return null;
                return new DateTime(ano, mes, dia).ToShortDateString();
            }
            catch { return null; }
        }

        internal static string EmMegabytes(int kb)
        {
            if (kb <= 0) return null;
            if (kb < 1024) return kb + " KB";
            double mb = kb / 1024.0;
            return (mb == Math.Floor(mb) ? mb.ToString("0") : mb.ToString("0.0")) + " MB";
        }

        internal static string EmGigabytes(ulong bytes)
        {
            if (bytes == 0) return null;
            double gb = bytes / 1073741824.0;
            if (gb >= 1000) return (gb / 1024.0).ToString("0.0") + " TB";

            // Sem decimal quando ela e zero: um pente de 8 GB e "8 GB", nao
            // "8,0 GB" - a casa vazia sugere uma precisao que a peca nao tem.
            if (gb >= 10) return Math.Round(gb).ToString("0") + " GB";
            if (Math.Abs(gb - Math.Round(gb)) < 0.05) return Math.Round(gb).ToString("0") + " GB";
            return gb.ToString("0.0") + " GB";
        }

        /// <summary>Codigo SMBIOS do tipo de memoria.</summary>
        internal static string TipoDeMemoria(int codigo)
        {
            switch (codigo)
            {
                case 20: return "DDR";
                case 21: return "DDR2";
                case 24: return "DDR3";
                case 26: return "DDR4";
                case 34: return "DDR5";
                case 35: return "LPDDR4";
                case 37: return "LPDDR5";
            }
            return null;
        }

        /// <summary>A folha inteira em texto, para colar num relatorio.</summary>
        public static string EmTexto()
        {
            List<SpecGrupo> f = Folha;
            if (f == null) return "";

            StringBuilder sb = new StringBuilder();
            foreach (SpecGrupo g in f)
            {
                sb.AppendLine("== " + g.Titulo);
                foreach (string[] l in g.Linhas)
                    sb.AppendLine("   " + l[0] + ": " + l[1]);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
