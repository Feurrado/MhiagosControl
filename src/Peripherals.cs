using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace MhiagosControl
{
    public enum TipoPeriferico { Headset, Mouse, Outro }

    /// <summary>De onde veio a leitura - e, por isso, que app um clique abre.</summary>
    public enum FontePeriferico { GHub, AttackShark, Bluetooth }

    /// <summary>
    /// Por que nao ha porcentagem. Fica como enum, e nao como texto pronto,
    /// porque quem le e uma thread de fundo e quem traduz e a interface.
    /// </summary>
    public enum EstadoPeriferico { Normal, Desligado, SemLeitura, NenhumConectado, RadioDesligado, AppFechado }

    /// <summary>Um periferico sem fio e a ultima carga lida dele.</summary>
    public sealed class Periferico
    {
        public readonly string Nome;
        public readonly TipoPeriferico Tipo;
        public readonly FontePeriferico Fonte;

        /// <summary>0 a 100; nulo quando nao ha leitura - Estado diz o motivo.</summary>
        public readonly int? Bateria;
        public readonly bool Carregando;
        public readonly EstadoPeriferico Estado;

        /// <summary>Horas restantes segundo o proprio fabricante (o "mileage" do G HUB); nulo sem isso.</summary>
        public readonly int? Autonomia;

        public Periferico(string nome, TipoPeriferico tipo, FontePeriferico fonte,
                          int? bateria, bool carregando, EstadoPeriferico estado)
            : this(nome, tipo, fonte, bateria, carregando, estado, null) { }

        public Periferico(string nome, TipoPeriferico tipo, FontePeriferico fonte,
                          int? bateria, bool carregando, EstadoPeriferico estado, int? autonomia)
        {
            Nome = nome; Tipo = tipo; Fonte = fonte;
            Bateria = bateria; Carregando = carregando; Estado = estado; Autonomia = autonomia;
        }

        public static Periferico Lido(string nome, TipoPeriferico tipo, FontePeriferico fonte, int pct, bool carregando)
        {
            return Lido(nome, tipo, fonte, pct, carregando, null);
        }

        public static Periferico Lido(string nome, TipoPeriferico tipo, FontePeriferico fonte, int pct, bool carregando, int? autonomia)
        {
            return new Periferico(nome, tipo, fonte, Math.Max(0, Math.Min(100, pct)), carregando,
                                  EstadoPeriferico.Normal, autonomia);
        }

        public static Periferico Sem(string nome, TipoPeriferico tipo, FontePeriferico fonte, EstadoPeriferico estado)
        {
            return new Periferico(nome, tipo, fonte, null, false, estado);
        }

        public override bool Equals(object obj)
        {
            Periferico o = obj as Periferico;
            return o != null && o.Nome == Nome && o.Tipo == Tipo && o.Fonte == Fonte &&
                   o.Bateria == Bateria && o.Carregando == Carregando && o.Estado == Estado &&
                   o.Autonomia == Autonomia;
        }

        public override int GetHashCode() { return (Nome ?? "").GetHashCode() ^ (Bateria ?? -1); }
    }

    /// <summary>
    /// Bateria dos perifericos sem fio, cada fonte numa thread propria.
    ///
    /// Tres fontes, porque cada fabricante expoe a carga de um jeito:
    ///
    ///   Logitech (G325 e o que mais o G HUB enxergar): o protocolo do headset
    ///   e o "Centurion", sem documentacao publica. O G HUB ja fala com ele e
    ///   publica o resultado num WebSocket local (ws://localhost:9010), o mesmo
    ///   que a interface dele usa. Sem o G HUB aberto nao ha leitura.
    ///
    ///   Attack Shark X11 (receptor 1D57:FA60): a colecao de usage page 0x000A
    ///   da interface 2 recebe sozinha, a cada 2 s, o report 03 55 40 01 NN, e
    ///   NN e a porcentagem - conferido contra o app do fabricante (0x5A = 90%).
    ///   O byte 3 vira 03 no cabo (bit 0x02 = carregando); o 40 segue sem uso conhecido.
    ///
    ///   Bluetooth: o proprio Windows guarda a carga numa propriedade do
    ///   dispositivo, a mesma que a tela de Configuracoes mostra.
    ///
    /// Nenhuma pede administrador nem escreve nada no aparelho.
    /// </summary>
    public static class Perifericos
    {
        private static Periferico[] _logitech = new Periferico[0];
        private static Periferico _shark;
        private static Periferico[] _bluetooth = new Periferico[0];
        private static readonly List<Thread> _threads = new List<Thread>();

        /// <summary>Retrato atual: os do G HUB e da Attack Shark, depois o Bluetooth.</summary>
        public static Periferico[] Atual
        {
            get
            {
                List<Periferico> l = new List<Periferico>(Volatile.Read(ref _logitech));
                Periferico s = Volatile.Read(ref _shark);
                if (s != null) l.Add(s);
                l.Sort(delegate(Periferico a, Periferico b) { return a.Tipo.CompareTo(b.Tipo); });
                l.AddRange(Volatile.Read(ref _bluetooth));
                l.RemoveAll(delegate(Periferico p) { return p != null && OcultoChave(p.Fonte + "|" + p.Nome); });
                return l.ToArray();
            }
        }

        /// <summary>
        /// Aparelhos escondidos pela pessoa, como "fonte|inicio do nome"
        /// (GHub|G325, GHub|PRO X). Sao detectados normalmente; so nao entram no
        /// retrato nem na pagina Baterias - assim um headset que saiu de casa nao
        /// fica para sempre como "desligado" na Visao geral. Vem da config
        /// (PerifericosOcultos); os testes deixam vazio.
        /// </summary>
        public static string[] Ocultos = new string[0];

        /// <summary>A lista da config ("GHub|G325,GHub|PRO X") em entradas.</summary>
        public static string[] LerOcultos(string texto)
        {
            List<string> l = new List<string>();
            foreach (string s in (texto ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (s.Trim().IndexOf('|') > 0) l.Add(s.Trim());
            return l.ToArray();
        }

        /// <summary>A chave "fonte|nome" casa com alguma entrada oculta (mesma fonte, nome comecando igual)?</summary>
        public static bool OcultoChave(string chave)
        {
            if (string.IsNullOrEmpty(chave)) return false;
            int b = chave.IndexOf('|');
            if (b <= 0) return false;
            string fonte = chave.Substring(0, b), nome = chave.Substring(b + 1);
            foreach (string o in Ocultos)
            {
                int ob = o.IndexOf('|');
                if (ob <= 0) continue;
                if (string.Equals(o.Substring(0, ob), fonte, StringComparison.OrdinalIgnoreCase) &&
                    nome.StartsWith(o.Substring(ob + 1).Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Nome de vitrine, so para a pagina Baterias: "Logitech G325
        /// Lightspeed" e "Attack Shark X11".
        ///
        /// So o nome EXIBIDO muda. O historico guarda cada aparelho pela chave
        /// fonte mais nome lido ("GHub|G325"), e trocar o nome lido zeraria as
        /// cargas ja medidas. O widget, a faixa da Visao geral e a dica da
        /// bandeja seguem com o nome curto, que e o que cabe neles.
        /// </summary>
        public static string NomeCompleto(string nome, FontePeriferico fonte)
        {
            if (string.IsNullOrEmpty(nome)) return nome;
            string n = nome.Trim();
            if (fonte == FontePeriferico.GHub && string.Equals(n, "G325", StringComparison.OrdinalIgnoreCase))
                return "Logitech G325 Lightspeed";
            if (fonte == FontePeriferico.GHub && n.StartsWith("PRO X Wireless", StringComparison.OrdinalIgnoreCase))
                return "Logitech PRO X Wireless";
            if (fonte == FontePeriferico.AttackShark && string.Equals(n, "X11", StringComparison.OrdinalIgnoreCase))
                return "Attack Shark X11";
            return nome;
        }

        /// <summary>
        /// Headset PRO X Wireless, que o G HUB lista ao lado do G325 com outro
        /// nome. O tipo (HEADSET) e igual para os dois, entao so o nome separa:
        /// "PRO X Wireless" e variantes como "PRO X Wireless Gaming Headset".
        /// </summary>
        public static bool EhProX(Periferico p)
        {
            return p != null && p.Fonte == FontePeriferico.GHub && p.Tipo == TipoPeriferico.Headset
                && p.Nome != null && ReProX.IsMatch(p.Nome);
        }

        private static readonly Regex ReProX = new Regex(@"\bPRO\s*-?\s*X\b", RegexOptions.IgnoreCase);

        /// <summary>"G325 70%  X11 90%", ou nulo quando nao ha nada para dizer.</summary>
        public static string Resumo()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Periferico p in Atual)
            {
                // a dica e curta: Bluetooth sem leitura e o marcador vazio ficam de fora
                if (p.Fonte == FontePeriferico.Bluetooth && !p.Bateria.HasValue) continue;
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(p.Nome).Append(' ');
                if (p.Bateria.HasValue)
                {
                    sb.Append(p.Bateria.Value.ToString(CultureInfo.InvariantCulture)).Append('%');
                    if (p.Carregando) sb.Append(" ⚡");
                }
                else sb.Append("--");
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        public static void Iniciar(WaitHandle parar)
        {
            lock (_threads)
            {
                if (_threads.Count > 0) return;
                _threads.Add(Iniciar("BateriaGHub", delegate { new LeitorGHub(parar).Rodar(); }));
                _threads.Add(Iniciar("BateriaAttackShark", delegate { new LeitorAttackShark(parar).Rodar(); }));
                _threads.Add(Iniciar("BateriaBluetooth", delegate { new LeitorBluetooth(parar).Rodar(); }));
            }
        }

        private static Thread Iniciar(string nome, ThreadStart corpo)
        {
            // Excecao escapando de thread de fundo derruba o processo inteiro;
            // bateria de mouse nao vale isso.
            Thread t = new Thread(delegate()
            {
                try { corpo(); }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { Log.Error("thread " + nome, ex); }
            });
            t.IsBackground = true;
            t.Name = nome;
            t.Start();
            return t;
        }

        /// <summary>Espera as threads, dentro do prazo do encerramento.</summary>
        public static bool Esperar(int ms)
        {
            if (ms < 1) ms = 1;
            // Um prazo para todas, e nao um para cada: com tres threads presas
            // o encerramento esperava o triplo do combinado.
            Stopwatch prazo = Stopwatch.StartNew();
            bool todas = true;
            lock (_threads)
                foreach (Thread t in _threads)
                    if (!t.Join(Math.Max(1, ms - (int)prazo.ElapsedMilliseconds))) todas = false;
            return todas;
        }

        internal static void PublicarLogitech(Periferico[] l) { Volatile.Write(ref _logitech, l); }
        internal static void PublicarShark(Periferico p) { Volatile.Write(ref _shark, p); }
        internal static void PublicarBluetooth(Periferico[] l) { Volatile.Write(ref _bluetooth, l); }

        // ---------------- atalhos ----------------

        /// <summary>
        /// Abre o app que cuida do periferico: G HUB, o da Attack Shark ou a
        /// painel rapido de Bluetooth da barra de tarefas.
        ///
        /// Pelo explorer.exe, e nao direto: o Mhiagos roda como administrador,
        /// e o filho herdaria isso. O G HUB elevado nao conversa com o agente
        /// dele, que roda sem elevacao; pelo explorer o app abre como o
        /// usuario, igual a um clique no menu Iniciar.
        /// </summary>
        public static void Abrir(Periferico p)
        {
            if (p == null) return;
            string alvo = null;
            try
            {
                switch (p.Fonte)
                {
                    case FontePeriferico.GHub: alvo = CaminhoDoGHub(); break;
                    case FontePeriferico.AttackShark: alvo = CaminhoDaAttackShark(); break;
                    // O painel rapido de Bluetooth da barra de tarefas, o mesmo
                    // do clique no icone das Configuracoes rapidas. Testado no
                    // Windows 11: devicediscovery abria o painel Transmitir, e
                    // ms-settings:bluetooth a pagina inteira das Configuracoes.
                    case FontePeriferico.Bluetooth: alvo = "ms-actioncenter:controlcenter/bluetooth"; break;
                }
                if (alvo == null) { Log.Write("app do periferico " + p.Nome + " nao encontrado"); return; }
                Process.Start("explorer.exe", "\"" + alvo + "\"");
            }
            catch (Exception ex) { Log.Error("abertura do app de " + p.Nome, ex); }
        }

        /// <summary>Nome do app para a dica do mouse sobre o periferico.</summary>
        public static string NomeDoApp(Periferico p)
        {
            if (p == null) return null;
            switch (p.Fonte)
            {
                case FontePeriferico.GHub: return "Logitech G HUB";
                case FontePeriferico.AttackShark: return "Attack Shark";
                default: return null;
            }
        }

        public static bool GHubInstalado() { return CaminhoDoGHub() != null; }

        /// <summary>Abre o G HUB como o usuario (pelo explorer), igual a um clique no menu Iniciar.</summary>
        public static void AbrirGHub()
        {
            try
            {
                string alvo = CaminhoDoGHub();
                if (alvo == null) { Log.Write("G HUB nao encontrado"); return; }
                Process.Start("explorer.exe", "\"" + alvo + "\"");
            }
            catch (Exception ex) { Log.Error("abertura do G HUB", ex); }
        }

        private static string CaminhoDoGHub()
        {
            string p = DoDesinstalador("Logitech G HUB", "DisplayIcon", null);
            if (p != null && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p)) return p;
            p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"LGHUB\lghub.exe");
            return File.Exists(p) ? p : null;
        }

        private static string CaminhoDaAttackShark()
        {
            string pasta = DoDesinstalador("Attack Shark", "InstallLocation", null);
            if (pasta != null)
            {
                string p = Path.Combine(pasta, "AttackShark.exe");
                if (File.Exists(p)) return p;
            }
            string padrao = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                         @"Attack Shark Software\AttackShark.exe");
            return File.Exists(padrao) ? padrao : null;
        }

        /// <summary>Um valor da entrada de desinstalacao cujo nome comeca com o dado.</summary>
        private static string DoDesinstalador(string nome, string valor, string padrao)
        {
            string[] raizes = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" };
            foreach (string raiz in raizes)
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(raiz))
                {
                    if (k == null) continue;
                    foreach (string sub in k.GetSubKeyNames())
                        using (RegistryKey e = k.OpenSubKey(sub))
                        {
                            string dn = e != null ? e.GetValue("DisplayName") as string : null;
                            if (dn == null || !dn.StartsWith(nome, StringComparison.OrdinalIgnoreCase)) continue;
                            string v = e.GetValue(valor) as string;
                            if (!string.IsNullOrEmpty(v)) return v.Trim('"');
                        }
                }
            }
            return padrao;
        }
    }

    /// <summary>
    /// Decide quando avisar que a bateria esta acabando: uma vez em 30% e
    /// outra em 10%, por aparelho.
    ///
    /// Cada limiar avisa uma vez so por descarga. Sem essa memoria a leitura
    /// oscilando entre 30 e 31 repetiria o aviso a cada ciclo. O aviso so
    /// rearma quando a carga sobe com folga (5 pontos acima) ou o aparelho vai
    /// para o carregador.
    /// </summary>
    internal sealed class AvisosDeBateria
    {
        public const int Baixa = 30;
        public const int Critica = 10;
        private const int Folga = 5;

        /// <summary>Menor limiar ja avisado de cada aparelho; ausente e nenhum.</summary>
        private readonly Dictionary<string, int> _avisado = new Dictionary<string, int>();

        /// <summary>Os aparelhos que acabaram de cruzar um limiar, com o limiar cruzado.</summary>
        public List<KeyValuePair<Periferico, int>> Verificar(Periferico[] itens)
        {
            List<KeyValuePair<Periferico, int>> novos = new List<KeyValuePair<Periferico, int>>();
            if (itens == null) return novos;
            foreach (Periferico p in itens)
            {
                if (p == null || !p.Bateria.HasValue) continue;   // desligado nao rearma nem avisa
                string chave = p.Fonte + "|" + p.Nome;
                int b = p.Bateria.Value;
                int ja;
                bool tem = _avisado.TryGetValue(chave, out ja);

                if (p.Carregando || b >= Baixa + Folga) { _avisado.Remove(chave); continue; }
                if (tem && ja == Critica && b >= Critica + Folga) { _avisado[chave] = Baixa; ja = Baixa; }

                if (b <= Critica && (!tem || ja > Critica))
                {
                    _avisado[chave] = Critica;
                    novos.Add(new KeyValuePair<Periferico, int>(p, Critica));
                }
                else if (b <= Baixa && !tem)
                {
                    _avisado[chave] = Baixa;
                    novos.Add(new KeyValuePair<Periferico, int>(p, Baixa));
                }
            }
            return novos;
        }

        // ---------------- carga completa e tempo restante ----------------

        /// <summary>Horas de uso restantes que disparam o aviso por tempo.</summary>
        public const double LimiteDeHoras = 2.0;

        /// <summary>O aviso por tempo rearma quando a previsao passa disto - ou no cabo.</summary>
        private const double RearmeDeHoras = 3.0;

        private readonly HashSet<string> _cheioAvisado = new HashSet<string>();
        private readonly HashSet<string> _tempoAvisado = new HashSet<string>();

        /// <summary>
        /// Aparelhos que chegaram a 100% no cabo, uma vez por passagem pelo
        /// carregador. Tirar do cabo rearma.
        /// </summary>
        public List<Periferico> VerificarCargaCompleta(Periferico[] itens)
        {
            List<Periferico> novos = new List<Periferico>();
            if (itens == null) return novos;
            foreach (Periferico p in itens)
            {
                if (p == null || !p.Bateria.HasValue) continue;
                string chave = p.Fonte + "|" + p.Nome;
                if (!p.Carregando) { _cheioAvisado.Remove(chave); continue; }
                if (p.Bateria.Value >= 100 && _cheioAvisado.Add(chave)) novos.Add(p);
            }
            return novos;
        }

        /// <summary>
        /// Aparelhos cuja previsao de uso acabou de cair a LimiteDeHoras ou
        /// menos, com a previsao. A previsao vem de fora (o historico) para a
        /// regra poder ser testada sem ele.
        /// </summary>
        public List<KeyValuePair<Periferico, double>> VerificarTempo(Periferico[] itens, Func<Periferico, double?> restante)
        {
            List<KeyValuePair<Periferico, double>> novos = new List<KeyValuePair<Periferico, double>>();
            if (itens == null || restante == null) return novos;
            foreach (Periferico p in itens)
            {
                if (p == null || !p.Bateria.HasValue) continue;
                string chave = p.Fonte + "|" + p.Nome;
                if (p.Carregando) { _tempoAvisado.Remove(chave); continue; }
                double? h = restante(p);
                if (!h.HasValue) continue;
                if (h.Value >= RearmeDeHoras) { _tempoAvisado.Remove(chave); continue; }
                if (h.Value <= LimiteDeHoras && _tempoAvisado.Add(chave))
                    novos.Add(new KeyValuePair<Periferico, double>(p, h.Value));
            }
            return novos;
        }
    }

    /// <summary>
    /// Cliente do WebSocket local do G HUB.
    ///
    /// GET /devices/list traz os aparelhos; GET /battery/{id}/state traz a
    /// carga de cada um. Entram na lista os headsets e mouses, e os demais que
    /// ja responderam a bateria nesta conexao - teclado e receptor com fio
    /// tambem aparecem em /devices/list e nao tem bateria nenhuma.
    ///
    /// A carga e perguntada a cada 2 s. A primeira versao perguntava a cada
    /// 30, e desligar o headset levava meio minuto para aparecer; a pergunta e
    /// local e pequena, entao o custo de ir mais depressa e nenhum.
    ///
    /// O JSON e lido por expressao regular: sao poucos campos rasos, e uma
    /// biblioteca de JSON seria a unica dependencia nova do projeto.
    /// </summary>
    internal sealed class LeitorGHub
    {
        private const int PeriodoMs = 2000;
        private const int EsperaFalhaMs = 10000;
        private const int ListaACadaCiclos = 15;

        private static readonly Regex ReId = new Regex("\"id\"\\s*:\\s*\"(dev[0-9a-fA-F]+)\"");
        private static readonly Regex ReNome = new Regex("\"displayName\"\\s*:\\s*\"([^\"]*)\"");
        private static readonly Regex ReTipo = new Regex("\"deviceType\"\\s*:\\s*\"([A-Z_]+)\"");
        private static readonly Regex ReEstado = new Regex("\"state\"\\s*:\\s*\"([A-Z_]+)\"");
        private static readonly Regex ReSucesso = new Regex("\"code\"\\s*:\\s*\"SUCCESS\"");
        private static readonly Regex RePct = new Regex("\"percentage\"\\s*:\\s*(\\d+)");
        private static readonly Regex ReCarga = new Regex("\"charging\"\\s*:\\s*(true|false)");

        /// <summary>
        /// "mileage": horas de uso que o G HUB estima para a carga atual. Subiu
        /// de 42 para 48 junto com a carga de 70 para 80% - e horas, nao km.
        /// </summary>
        private static readonly Regex ReAutonomia = new Regex("\"mileage\"\\s*:\\s*(\\d+)");

        private readonly WaitHandle _parar;
        private ClientWebSocket _ws;
        private bool _falhaAvisada = false;

        internal sealed class Aparelho
        {
            public string Id, Nome;
            public TipoPeriferico Tipo;
            public bool Ativo, TemBateria, Lido;
        }
        private List<Aparelho> _aparelhos = new List<Aparelho>();

        public LeitorGHub(WaitHandle parar) { _parar = parar; }

        /// <summary>
        /// A lista e a unica coisa que diz que um aparelho inativo voltou
        /// (a bateria so e lida dos ativos). Com algum aparelho relevante
        /// desligado ela vem a cada ciclo; antes, so a cada 30 s, e era esse
        /// o atraso para reconhecer o headset ligado de volta.
        /// </summary>
        internal static bool PrecisaListar(int ciclo, List<Aparelho> aparelhos)
        {
            if (ciclo % ListaACadaCiclos == 0) return true;
            foreach (Aparelho a in aparelhos)
                if (!a.Ativo && (a.Tipo != TipoPeriferico.Outro || a.TemBateria)) return true;
            return false;
        }

        public void Rodar()
        {
            int ciclo = 0;
            while (!_parar.WaitOne(0))
            {
                int espera = PeriodoMs;
                try
                {
                    if (_ws == null || _ws.State != WebSocketState.Open)
                    {
                        Conectar();
                        ciclo = 0;
                    }
                    if (PrecisaListar(ciclo, _aparelhos)) Listar();
                    ciclo++;

                    bool mudouAlgo = false;
                    List<Periferico> lidos = new List<Periferico>();
                    foreach (Aparelho a in _aparelhos)
                    {
                        bool antes = a.Lido;
                        Periferico p = LerBateria(a);
                        if (p != null) lidos.Add(p);
                        if (a.Lido != antes) mudouAlgo = true;
                    }
                    Perifericos.PublicarLogitech(lidos.ToArray());

                    // Ligou ou desligou: a lista diz o estado novo antes do
                    // proximo ciclo dela, e um aparelho que acabou de ligar
                    // ainda pode estar como inativo.
                    if (mudouAlgo) ciclo = 0;

                    if (_falhaAvisada) { Log.Write("G HUB: bateria voltou a ser lida"); _falhaAvisada = false; }
                }
                catch (Exception ex)
                {
                    // G HUB fechado e o caso comum, nao um erro: um aviso por queda basta
                    if (!_falhaAvisada) { Log.Write("G HUB indisponivel para bateria: " + ex.Message); _falhaAvisada = true; }
                    Perifericos.PublicarLogitech(ConhecidosSemGHub());
                    Fechar();
                    espera = EsperaFalhaMs;
                }
                if (_parar.WaitOne(espera)) break;
            }
            Fechar();
        }

        /// <summary>
        /// Sem G HUB, os aparelhos dele que o historico conhece continuam na
        /// tira avisando que o app esta fechado. Sumir com eles deixava a
        /// pessoa sem saber por que o headset tinha desaparecido.
        /// </summary>
        private static Periferico[] ConhecidosSemGHub()
        {
            List<Periferico> l = new List<Periferico>();
            try
            {
                foreach (Periferico p in HistoricoDeBateria.Conhecidos())
                    if (p.Fonte == FontePeriferico.GHub)
                        l.Add(Periferico.Sem(p.Nome, p.Tipo, FontePeriferico.GHub, EstadoPeriferico.AppFechado));
            }
            catch (Exception ex) { Log.Error("aparelhos do G HUB conhecidos", ex); }
            return l.ToArray();
        }

        private Periferico LerBateria(Aparelho a)
        {
            bool relevante = a.Tipo != TipoPeriferico.Outro || a.TemBateria;
            if (!a.Ativo)
            {
                a.Lido = false;
                return relevante ? Periferico.Sem(a.Nome, a.Tipo, FontePeriferico.GHub, EstadoPeriferico.Desligado) : null;
            }

            string r = Pedir("/battery/" + a.Id + "/state");
            Match pct = r != null && ReSucesso.IsMatch(r) ? RePct.Match(r) : Match.Empty;
            if (!pct.Success)
            {
                a.Lido = false;
                return relevante ? Periferico.Sem(a.Nome, a.Tipo, FontePeriferico.GHub, EstadoPeriferico.Desligado) : null;
            }

            a.TemBateria = true;
            a.Lido = true;
            Match c = ReCarga.Match(r);
            Match h = ReAutonomia.Match(r);
            return Periferico.Lido(a.Nome, a.Tipo, FontePeriferico.GHub,
                int.Parse(pct.Groups[1].Value, CultureInfo.InvariantCulture),
                c.Success && c.Groups[1].Value == "true",
                h.Success ? (int?)int.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture) : null);
        }

        private void Conectar()
        {
            Fechar();
            _ws = new ClientWebSocket();
            _ws.Options.AddSubProtocol("json");
            _ws.Options.SetRequestHeader("Origin", "file://");
            if (!_ws.ConnectAsync(new Uri("ws://localhost:9010"), CancellationToken.None).Wait(3000))
                throw new TimeoutException("sem resposta em localhost:9010");
            if (_ws.State != WebSocketState.Open) throw new IOException("conexao recusada");
        }

        private void Listar()
        {
            string r = Pedir("/devices/list");
            if (r == null) throw new IOException("lista de aparelhos sem resposta");

            Dictionary<string, Aparelho> antigos = new Dictionary<string, Aparelho>();
            foreach (Aparelho a in _aparelhos) antigos[a.Id] = a;

            List<Aparelho> novos = new List<Aparelho>();
            MatchCollection ids = ReId.Matches(r);
            for (int i = 0; i < ids.Count; i++)
            {
                // o trecho de um aparelho vai do id dele ate o id do proximo
                int ini = ids[i].Index;
                int fim = i + 1 < ids.Count ? ids[i + 1].Index : r.Length;
                string trecho = r.Substring(ini, fim - ini);

                Aparelho a;
                if (!antigos.TryGetValue(ids[i].Groups[1].Value, out a)) a = new Aparelho();
                a.Id = ids[i].Groups[1].Value;
                Match n = ReNome.Match(trecho);
                a.Nome = n.Success && n.Groups[1].Value.Length > 0 ? n.Groups[1].Value : a.Id;
                Match t = ReTipo.Match(trecho);
                string tipo = t.Success ? t.Groups[1].Value : "";
                a.Tipo = tipo == "HEADSET" ? TipoPeriferico.Headset
                       : tipo == "MOUSE" ? TipoPeriferico.Mouse : TipoPeriferico.Outro;
                Match e = ReEstado.Match(trecho);
                a.Ativo = !e.Success || e.Groups[1].Value == "ACTIVE";
                novos.Add(a);
            }
            _aparelhos = novos;
        }

        /// <summary>
        /// Um GET e a resposta dele. O G HUB manda mensagens por conta propria
        /// (o OPTIONS de boas-vindas, por exemplo), entao se espera a que traz
        /// o mesmo caminho.
        /// </summary>
        private string Pedir(string caminho)
        {
            string msg = "{\"msgId\":\"\",\"verb\":\"GET\",\"path\":\"" + caminho + "\"}";
            byte[] b = Encoding.UTF8.GetBytes(msg);
            if (!_ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).Wait(3000))
                throw new TimeoutException("envio ao G HUB");

            string marca = "\"path\": \"" + caminho + "\"";
            for (int i = 0; i < 6; i++)
            {
                string r = Receber();
                if (r == null) return null;
                if (r.Replace("\"path\":\"", "\"path\": \"").Contains(marca) && r.Contains("\"GET\"")) return r;
            }
            return null;
        }

        private string Receber()
        {
            byte[] buf = new byte[65536];
            MemoryStream ms = new MemoryStream();
            while (true)
            {
                var t = _ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                if (!t.Wait(3000)) throw new TimeoutException("G HUB nao respondeu");
                if (t.Result.MessageType == WebSocketMessageType.Close) throw new IOException("G HUB fechou a conexao");
                ms.Write(buf, 0, t.Result.Count);
                if (t.Result.EndOfMessage) break;
                if (ms.Length > 8 * 1024 * 1024) throw new IOException("resposta grande demais");
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private void Fechar()
        {
            if (_ws == null) return;
            try { _ws.Abort(); _ws.Dispose(); } catch { }
            _ws = null;
        }
    }

    /// <summary>
    /// Escuta o receptor do Attack Shark X11.
    ///
    /// So leitura: o receptor empurra o report sozinho, e abrir a colecao em
    /// modo compartilhado nao atrapalha o app do fabricante, que pode seguir
    /// aberto. Tres reports perdidos seguidos (6 s) contam como mouse
    /// desligado, e a carga passa a nula em vez de mostrar um numero velho.
    /// </summary>
    internal sealed class LeitorAttackShark
    {
        private const ushort Vid = 0x1D57;
        private const ushort Pid = 0xFA60;
        private const ushort UsagePage = 0x000A;
        private const int SilencioMs = 4500;
        private const int ProcuraMs = 2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr r; }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InLen, OutLen, FeatLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort a0, a1, a2, a3, a4, a5, a6, a7, a8, a9;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto)] private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA a);
        [DllImport("setupapi.dll", CharSet = CharSet.Auto)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA a, IntPtr d, int sz, out int req, IntPtr b);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(string n, uint acc, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        private readonly WaitHandle _parar;

        public LeitorAttackShark(WaitHandle parar) { _parar = parar; }

        private static Periferico Desligado()
        {
            return Periferico.Sem("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, EstadoPeriferico.Desligado);
        }

        public void Rodar()
        {
            bool avisado = false;
            while (!_parar.WaitOne(0))
            {
                SafeFileHandle h = Abrir();
                if (h == null)
                {
                    Perifericos.PublicarShark(null);
                    if (_parar.WaitOne(ProcuraMs)) break;
                    continue;
                }
                if (!avisado) { Log.Write("receptor Attack Shark encontrado"); avisado = true; }
                try { Escutar(h); }
                catch (Exception ex) { Log.Error("leitura do receptor Attack Shark", ex); }
                finally { h.Dispose(); }
                Perifericos.PublicarShark(null);
                if (_parar.WaitOne(1000)) break;
            }
        }

        private void Escutar(SafeFileHandle h)
        {
            byte[] buf = new byte[5];
            DateTime ultimo = DateTime.UtcNow;
            int? carga = null;
            bool carregando = false;
            Perifericos.PublicarShark(Desligado());

            using (FileStream fs = new FileStream(h, FileAccess.Read, buf.Length, true))
            {
                IAsyncResult ar = null;
                while (true)
                {
                    if (ar == null) ar = fs.BeginRead(buf, 0, buf.Length, null, null);
                    int qual = WaitHandle.WaitAny(new WaitHandle[] { ar.AsyncWaitHandle, _parar }, 500);
                    if (qual == 1) return;   // encerramento; o Dispose do handle cancela a leitura
                    if (qual == 0)
                    {
                        int n = fs.EndRead(ar);
                        ar = null;
                        if (n == 0) return;
                        if (n >= 5 && buf[0] == 0x03 && buf[1] == 0x55 && buf[4] <= 100)
                        {
                            ultimo = DateTime.UtcNow;
                            // byte 3: 0x01 no uso, 0x03 no cabo - o bit 0x02 e a
                            // carga. Conferido ligando e tirando o cabo, e o app
                            // da Attack Shark mostrando o mesmo.
                            bool cabo = (buf[3] & 0x02) != 0;
                            if (carga != buf[4] || carregando != cabo)
                            {
                                carga = buf[4];
                                carregando = cabo;
                                Perifericos.PublicarShark(Periferico.Lido("X11", TipoPeriferico.Mouse,
                                    FontePeriferico.AttackShark, buf[4], cabo));
                            }
                        }
                    }
                    if (carga.HasValue && (DateTime.UtcNow - ultimo).TotalMilliseconds > SilencioMs)
                    {
                        carga = null;
                        Perifericos.PublicarShark(Desligado());
                    }
                }
            }
        }

        /// <summary>A colecao 0x000A do receptor, aberta so para leitura.</summary>
        private static SafeFileHandle Abrir()
        {
            Guid g; HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (set.ToInt64() == -1) return null;
            try
            {
                for (int i = 0; ; i++)
                {
                    SP_DEVICE_INTERFACE_DATA d = new SP_DEVICE_INTERFACE_DATA();
                    d.cbSize = Marshal.SizeOf(d);
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) return null;

                    int req;
                    SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    if (req <= 0) continue;
                    IntPtr det = Marshal.AllocHGlobal(req);
                    string path = null;
                    try
                    {
                        Marshal.WriteInt32(det, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(set, ref d, det, req, out req, IntPtr.Zero))
                            path = Marshal.PtrToStringAuto(new IntPtr(det.ToInt64() + 4));
                    }
                    finally { Marshal.FreeHGlobal(det); }
                    if (path == null || path.IndexOf("vid_1d57&pid_fa60", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // GENERIC_READ, compartilhado, FILE_FLAG_OVERLAPPED
                    SafeFileHandle h = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                    if (h.IsInvalid) { h.Dispose(); continue; }

                    HIDD_ATTRIBUTES at = new HIDD_ATTRIBUTES();
                    at.Size = Marshal.SizeOf(at);
                    HIDP_CAPS caps = new HIDP_CAPS();
                    IntPtr pd;
                    bool ok = HidD_GetAttributes(h, ref at) && at.VendorID == Vid && at.ProductID == Pid;
                    if (ok && HidD_GetPreparsedData(h, out pd))
                    {
                        HidP_GetCaps(pd, out caps);
                        HidD_FreePreparsedData(pd);
                    }
                    if (ok && caps.UsagePage == UsagePage && caps.InLen == 5) return h;
                    h.Dispose();
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
    }

    /// <summary>
    /// Bateria dos aparelhos Bluetooth conectados, lida das propriedades que o
    /// Windows mantem para cada no de dispositivo.
    ///
    /// A carga NAO fica no no principal do aparelho (BTHENUM\DEV_..., BTHLE\DEV_...):
    /// num fone ela fica no filho do perfil Hands-Free, num mouse LE no filho
    /// do servico de bateria. Por isso os nos sao agrupados pelo ContainerId,
    /// que e o mesmo para todas as partes de um aparelho fisico. Nome e estado
    /// de conexao vem do no principal; a carga, de qualquer um do grupo.
    ///
    /// So entram os conectados: a lista de pareados inclui o que esta na
    /// gaveta, e a ultima carga que o Windows guardou deles e velha.
    /// </summary>
    internal sealed class LeitorBluetooth
    {
        private const int PeriodoMs = 3000;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY
        {
            public Guid fmtid; public int pid;
            public DEVPROPKEY(string g, int p) { fmtid = new Guid(g); pid = p; }
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll")]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA data, StringBuilder id, int size, out int req);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key,
            out int type, byte[] buf, int size, out int req, int flags);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [StructLayout(LayoutKind.Sequential)] private struct BLUETOOTH_FIND_RADIO_PARAMS { public int dwSize; }
        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS p, out IntPtr radio);
        [DllImport("bthprops.cpl")] private static extern bool BluetoothFindRadioClose(IntPtr busca);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        /// <summary>
        /// O radio esta ligado? Com o Bluetooth desligado pelas Configuracoes
        /// (ou sem adaptador), o Windows nao entrega radio nenhum aqui - conferido
        /// nesta maquina com o radio desligado: ERROR_NO_MORE_ITEMS.
        /// </summary>
        internal static bool RadioLigado()
        {
            BLUETOOTH_FIND_RADIO_PARAMS p = new BLUETOOTH_FIND_RADIO_PARAMS();
            p.dwSize = Marshal.SizeOf(p);
            IntPtr radio;
            IntPtr busca = BluetoothFindFirstRadio(ref p, out radio);
            if (busca == IntPtr.Zero) return false;
            CloseHandle(radio);
            BluetoothFindRadioClose(busca);
            return true;
        }

        private const int DIGCF_ALLCLASSES = 0x4;

        private static readonly DEVPROPKEY KeyBateria = new DEVPROPKEY("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
        private static readonly DEVPROPKEY KeyConectado = new DEVPROPKEY("83DA6326-97A6-4088-9453-A1923F573B29", 15);
        private static readonly DEVPROPKEY KeyContainer = new DEVPROPKEY("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C", 2);
        private static readonly DEVPROPKEY KeyNome = new DEVPROPKEY("A45C254E-DF1C-4EFD-8020-67D146A850E0", 14);

        private readonly WaitHandle _parar;
        private bool _falhaAvisada = false;

        public LeitorBluetooth(WaitHandle parar) { _parar = parar; }

        private sealed class Grupo { public string Nome; public bool Principal, Conectado; public int? Bateria; }

        public void Rodar()
        {
            while (!_parar.WaitOne(0))
            {
                try
                {
                    Perifericos.PublicarBluetooth(Ler());
                    _falhaAvisada = false;
                }
                catch (Exception ex)
                {
                    if (!_falhaAvisada) { Log.Error("leitura da bateria Bluetooth", ex); _falhaAvisada = true; }
                }
                if (_parar.WaitOne(PeriodoMs)) break;
            }
        }

        internal static Periferico[] Ler()
        {
            // Radio desligado: nada pode estar conectado, e a propriedade de
            // conexao dos nos ainda pode demorar a cair. O campo fica, com o
            // aviso, porque e por ele que se abre o painel para ligar.
            if (!RadioLigado())
                return new Periferico[] {
                    Periferico.Sem("Bluetooth", TipoPeriferico.Outro, FontePeriferico.Bluetooth, EstadoPeriferico.RadioDesligado) };

            Dictionary<Guid, Grupo> grupos = new Dictionary<Guid, Grupo>();
            foreach (string enumerador in new string[] { "BTHENUM", "BTHLE", "BTHLEDEVICE" })
                Varrer(enumerador, grupos);

            List<Periferico> l = new List<Periferico>();
            foreach (Grupo g in grupos.Values)
            {
                if (!g.Principal || !g.Conectado || string.IsNullOrEmpty(g.Nome)) continue;
                l.Add(g.Bateria.HasValue
                    ? Periferico.Lido(g.Nome, TipoPeriferico.Outro, FontePeriferico.Bluetooth, g.Bateria.Value, false)
                    : Periferico.Sem(g.Nome, TipoPeriferico.Outro, FontePeriferico.Bluetooth, EstadoPeriferico.SemLeitura));
            }
            l.Sort(delegate(Periferico a, Periferico b) { return string.Compare(a.Nome, b.Nome, StringComparison.CurrentCultureIgnoreCase); });

            // O campo existe mesmo sem nada conectado: e por ele que se abre o
            // painel de Bluetooth do Windows.
            if (l.Count == 0)
                l.Add(Periferico.Sem("Bluetooth", TipoPeriferico.Outro, FontePeriferico.Bluetooth, EstadoPeriferico.NenhumConectado));
            return l.ToArray();
        }

        private static void Varrer(string enumerador, Dictionary<Guid, Grupo> grupos)
        {
            // Sem DIGCF_PRESENT: para o Bluetooth "presente" quer dizer pareado
            // com o radio ligado, e nao conectado. Quem decide e KeyConectado.
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, enumerador, IntPtr.Zero, DIGCF_ALLCLASSES);
            if (set.ToInt64() == -1) return;
            try
            {
                byte[] buf = new byte[1024];
                for (int i = 0; ; i++)
                {
                    SP_DEVINFO_DATA d = new SP_DEVINFO_DATA();
                    d.cbSize = Marshal.SizeOf(d);
                    if (!SetupDiEnumDeviceInfo(set, i, ref d)) break;

                    Guid? container = LerGuid(set, ref d, KeyContainer, buf);
                    if (!container.HasValue) continue;

                    Grupo g;
                    if (!grupos.TryGetValue(container.Value, out g)) { g = new Grupo(); grupos[container.Value] = g; }

                    StringBuilder id = new StringBuilder(512);
                    int req;
                    string inst = SetupDiGetDeviceInstanceId(set, ref d, id, id.Capacity, out req) ? id.ToString() : "";
                    if (inst.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase) ||
                        inst.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase))
                    {
                        g.Principal = true;
                        g.Nome = LerTexto(set, ref d, KeyNome, buf) ?? g.Nome;
                        g.Conectado = LerBool(set, ref d, KeyConectado, buf);
                    }

                    int? pct = LerByte(set, ref d, KeyBateria, buf);
                    if (pct.HasValue && pct.Value <= 100) g.Bateria = pct;
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }

        private static bool Ler(IntPtr set, ref SP_DEVINFO_DATA d, DEVPROPKEY key, byte[] buf, out int tipo, out int tam)
        {
            return SetupDiGetDevicePropertyW(set, ref d, ref key, out tipo, buf, buf.Length, out tam, 0);
        }

        private static int? LerByte(IntPtr set, ref SP_DEVINFO_DATA d, DEVPROPKEY key, byte[] buf)
        {
            int tipo, tam;
            if (!Ler(set, ref d, key, buf, out tipo, out tam) || tam < 1) return null;
            return buf[0];
        }

        private static bool LerBool(IntPtr set, ref SP_DEVINFO_DATA d, DEVPROPKEY key, byte[] buf)
        {
            int tipo, tam;
            return Ler(set, ref d, key, buf, out tipo, out tam) && tam >= 1 && buf[0] != 0;
        }

        private static Guid? LerGuid(IntPtr set, ref SP_DEVINFO_DATA d, DEVPROPKEY key, byte[] buf)
        {
            int tipo, tam;
            if (!Ler(set, ref d, key, buf, out tipo, out tam) || tam < 16) return null;
            byte[] g = new byte[16];
            Array.Copy(buf, g, 16);
            return new Guid(g);
        }

        private static string LerTexto(IntPtr set, ref SP_DEVINFO_DATA d, DEVPROPKEY key, byte[] buf)
        {
            int tipo, tam;
            if (!Ler(set, ref d, key, buf, out tipo, out tam) || tam < 2) return null;
            return Encoding.Unicode.GetString(buf, 0, tam).TrimEnd('\0');
        }
    }
}
