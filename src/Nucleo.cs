using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    /// <summary>
    /// O nucleo: a unica parte do Mhiagos que precisa de administrador.
    ///
    /// So a leitura de sensores de baixo nivel (HWiNFO, PawnIO) exige elevacao;
    /// interface, navegador, ConnectLife, G HUB e o proprio HID do mostrador nao.
    /// O nucleo e o mesmo executavel aberto com "--nucleo": abre as fontes
    /// elevadas e as serve por um named pipe local, restrito ao usuario que o
    /// abriu. A interface (sem elevacao) fala com ele por FonteRemota, que e so
    /// mais uma ISensorSource: o resto do app nao sabe a diferenca.
    ///
    /// Protocolo: texto UTF-8, uma linha por pedido e por resposta, campos
    /// separados por TAB (NucleoProtocolo). Nada do que chega pelo pipe vira
    /// caminho, comando ou biblioteca: so nomes de fonte conhecidos e ids de
    /// sensor, que sao comparados e nunca executados.
    /// </summary>
    internal static class Nucleo
    {
        public const string Argumento = "--nucleo";
        /// <summary>Com MHIAGOS_NUCLEO=1 o app usa o nucleo mesmo estando elevado (para testar a separacao).</summary>
        public const string Variavel = "MHIAGOS_NUCLEO";
        public const int Versao = 1;
        public const string FonteHwInfo = "hwinfo";
        public const string FonteLibre = "libre";
        private const string MutexDoNucleo = "Local\\MhiagosControl_Nucleo";

        /// <summary>
        /// Nome do pipe: um por usuario. Dois usuarios na mesma maquina (troca
        /// rapida) nao disputam o nome, e o de um nao serve ao outro.
        /// </summary>
        public static string NomeDoPipe
        {
            get
            {
                string sid = "";
                try { using (WindowsIdentity eu = WindowsIdentity.GetCurrent()) sid = eu.User != null ? eu.User.Value : ""; }
                catch { }
                return "MhiagosControl.Nucleo." + sid;
            }
        }

        /// <summary>A interface deve ler os sensores pelo nucleo, e nao abrir as fontes ela mesma.</summary>
        public static bool UsarRemoto
        {
            get
            {
                try { if (Environment.GetEnvironmentVariable(Variavel) == "1") return true; } catch { }
                return !Elevado();
            }
        }

        public static bool Elevado()
        {
            try
            {
                using (WindowsIdentity eu = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(eu).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// <summary>As fontes que o app usa: remotas quando ha nucleo, diretas quando o proprio app e elevado.</summary>
        public static Sensors CriarSensores()
        {
            if (!UsarRemoto) return new Sensors();
            Log.Write("sensores pelo nucleo (" + NomeDoPipe + ")");
            NucleoCliente cliente = new NucleoCliente(NomeDoPipe);
            return new Sensors(new FonteRemota(cliente, FonteHwInfo, "HWiNFO"),
                               new FonteRemota(cliente, FonteLibre, "LibreHardwareMonitor"),
                               new RtssSensorSource(), new BateriaSensorSource());
        }

        /// <summary>O processo "--nucleo": serve as fontes elevadas ate receber o evento de saida do instalador.</summary>
        public static void Rodar()
        {
            bool novo;
            using (Mutex unico = new Mutex(true, MutexDoNucleo, out novo))
            {
                if (!novo) return;
                Log.Write("nucleo: iniciado" + (Elevado() ? "" : " SEM elevacao (os sensores de baixo nivel nao vao abrir)"));
                Dictionary<string, ISensorSource> fontes = new Dictionary<string, ISensorSource>();
                fontes[FonteHwInfo] = new HwInfoSensorSource();
                fontes[FonteLibre] = new LibreSensorSource();
                using (NucleoServidor servidor = new NucleoServidor(NomeDoPipe, fontes))
                {
                    servidor.Iniciar();
                    EventWaitHandle saida = null;
                    try { saida = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\MhiagosControl_Nucleo_Sair"); }
                    catch (Exception ex) { Log.Error("nucleo: evento de saida", ex); }
                    if (saida != null) { saida.WaitOne(); saida.Dispose(); }
                    else Thread.Sleep(Timeout.Infinite);
                }
                foreach (ISensorSource f in fontes.Values)
                {
                    try { f.Dispose(); } catch (Exception ex) { Log.Error("nucleo: fechar fonte", ex); }
                }
                Log.Write("nucleo: encerrado");
            }
        }
    }

    /// <summary>O texto que vai pelo pipe: campos por TAB, com escape, e a leitura de um sensor numa linha.</summary>
    internal static class NucleoProtocolo
    {
        public const string Ola = "OLA", Abrir = "ABRIR", Atualizar = "ATUALIZAR", Ler = "LER";
        public const string Ok = "OK", Erro = "ERRO";
        /// <summary>No lugar da lista de ids: "todos" (lista nula) e "nenhum" (lista vazia).</summary>
        public const string Todos = "*", Nenhum = "-";
        private const string Nulo = "\\0";

        public static string Escapar(string s)
        {
            if (s == null) return Nulo;
            StringBuilder sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        public static string Desescapar(string s)
        {
            if (s == Nulo) return null;
            if (s.IndexOf('\\') < 0) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char d = s[++i];
                switch (d)
                {
                    case 't': sb.Append('\t'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    default: sb.Append(d); break;
                }
            }
            return sb.ToString();
        }

        public static string Linha(params string[] campos)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < campos.Length; i++)
            {
                if (i > 0) sb.Append('\t');
                sb.Append(Escapar(campos[i]));
            }
            return sb.ToString();
        }

        public static string[] Campos(string linha)
        {
            string[] c = linha.Split('\t');
            for (int i = 0; i < c.Length; i++) c[i] = Desescapar(c[i]);
            return c;
        }

        public static string DeEntrada(SensorEntry e)
        {
            return Linha(e.Id, e.Hardware, e.Name, e.Category, e.Label,
                ((int)e.Type).ToString(CultureInfo.InvariantCulture),
                e.Value.HasValue ? e.Value.Value.ToString("R", CultureInfo.InvariantCulture) : null,
                e.Source, e.Members.ToString(CultureInfo.InvariantCulture), e.Unit);
        }

        /// <summary>Nulo se a linha nao e uma leitura valida.</summary>
        public static SensorEntry ParaEntrada(string linha)
        {
            string[] c = Campos(linha);
            if (c.Length < 10) return null;
            int tipo, membros;
            if (!int.TryParse(c[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out tipo)) return null;
            if (!int.TryParse(c[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out membros)) return null;
            SensorEntry e = new SensorEntry();
            e.Id = c[0]; e.Hardware = c[1]; e.Name = c[2]; e.Category = c[3]; e.Label = c[4];
            e.Type = (SensorType)tipo;
            float v;
            if (c[6] != null && float.TryParse(c[6], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) e.Value = v;
            e.Source = c[7]; e.Members = membros; e.Unit = c[9];
            return e;
        }

        public static string DeIds(IEnumerable<string> ids)
        {
            if (ids == null) return Todos;
            StringBuilder sb = new StringBuilder();
            foreach (string id in ids)
            {
                if (id == null) continue;
                if (sb.Length > 0) sb.Append('\t');
                sb.Append(Escapar(id));
            }
            return sb.Length == 0 ? Nenhum : sb.ToString();
        }

        /// <summary>Os ids a partir do campo "inicio" de um pedido ja separado; nulo quer dizer todos.</summary>
        public static List<string> ParaIds(string[] campos, int inicio)
        {
            if (campos.Length <= inicio || campos[inicio] == Todos) return null;
            List<string> l = new List<string>();
            if (campos[inicio] == Nenhum) return l;
            for (int i = inicio; i < campos.Length; i++) if (campos[i] != null) l.Add(campos[i]);
            return l;
        }
    }

    /// <summary>
    /// O lado elevado: atende um cliente por vez no pipe e repassa os pedidos as fontes.
    /// O pipe e aberto como assincrono para o Dispose (de outra thread) conseguir
    /// cancelar uma leitura parada; num handle sincrono o fechamento esperaria por ela.
    /// </summary>
    internal sealed class NucleoServidor : IDisposable
    {
        private readonly string _nome;
        private readonly Dictionary<string, ISensorSource> _fontes;
        private readonly object _trava = new object();
        private Thread _thread;
        private volatile bool _parar;
        private NamedPipeServerStream _atual;

        public NucleoServidor(string nome, Dictionary<string, ISensorSource> fontes)
        {
            _nome = nome; _fontes = fontes;
        }

        public void Iniciar()
        {
            _thread = new Thread(Laco);
            _thread.IsBackground = true;
            _thread.Name = "NucleoServidor";
            _thread.Start();
        }

        /// <summary>So o usuario que abriu o nucleo conversa com ele; ninguem pela rede.</summary>
        internal static PipeSecurity Seguranca()
        {
            PipeSecurity s = new PipeSecurity();
            using (WindowsIdentity eu = WindowsIdentity.GetCurrent())
            {
                s.AddAccessRule(new PipeAccessRule(eu.User, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                    AccessControlType.Allow));
            }
            s.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));
            return s;
        }

        private void Laco()
        {
            while (!_parar)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(_nome, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous, 4096, 65536, Seguranca());
                    lock (_trava) _atual = pipe;
                    pipe.WaitForConnection();
                    if (_parar) break;
                    Atender(pipe);
                }
                catch (Exception ex)
                {
                    if (_parar) break;
                    Log.Write("nucleo: conexao encerrada (" + ex.GetType().Name + ")");
                    Thread.Sleep(200);
                }
                finally
                {
                    lock (_trava) _atual = null;
                    if (pipe != null) { try { pipe.Dispose(); } catch { } }
                }
            }
        }

        private void Atender(NamedPipeServerStream pipe)
        {
            StreamReader leitor = new StreamReader(pipe, new UTF8Encoding(false), false, 4096);
            StreamWriter escritor = new StreamWriter(pipe, new UTF8Encoding(false), 65536);
            escritor.NewLine = "\n";
            string linha;
            while (!_parar && (linha = leitor.ReadLine()) != null)
            {
                try { Responder(NucleoProtocolo.Campos(linha), escritor); }
                catch (IOException) { throw; }
                catch (Exception ex)
                {
                    Log.Error("nucleo: pedido", ex);
                    escritor.WriteLine(NucleoProtocolo.Linha(NucleoProtocolo.Erro, ex.GetType().Name));
                }
                escritor.Flush();
            }
        }

        private void Responder(string[] c, StreamWriter w)
        {
            string pedido = c.Length > 0 ? c[0] : "";
            if (pedido == NucleoProtocolo.Ola)
            {
                w.WriteLine(NucleoProtocolo.Linha(NucleoProtocolo.Ok, Nucleo.Versao.ToString(CultureInfo.InvariantCulture)));
                return;
            }
            ISensorSource f = null;
            if (c.Length < 2 || c[1] == null || !_fontes.TryGetValue(c[1], out f))
            {
                w.WriteLine(NucleoProtocolo.Linha(NucleoProtocolo.Erro, "fonte desconhecida"));
                return;
            }
            if (pedido == NucleoProtocolo.Abrir)
            {
                // Abrir duas vezes (a interface reabriu) nao recarrega a fonte.
                bool aberta = f.Available || f.Open();
                w.WriteLine(NucleoProtocolo.Linha(NucleoProtocolo.Ok, aberta ? "1" : "0", f.Available ? "1" : "0", f.Name));
                return;
            }
            if (pedido == NucleoProtocolo.Atualizar)
            {
                LibreSensorSource libre = f as LibreSensorSource;
                if (libre != null) libre.Foco = NucleoProtocolo.ParaIds(c, 2);
                if (f.Available) f.Refresh();
                w.WriteLine(NucleoProtocolo.Linha(NucleoProtocolo.Ok, f.Available ? "1" : "0"));
                return;
            }
            if (pedido == NucleoProtocolo.Ler)
            {
                List<SensorEntry> l = f.Available ? f.Read(NucleoProtocolo.ParaIds(c, 2)) : new List<SensorEntry>();
                if (l == null) l = new List<SensorEntry>();
                int n = 0;
                foreach (SensorEntry e in l) if (e != null) n++;
                w.WriteLine(NucleoProtocolo.Linha(NucleoProtocolo.Ok, n.ToString(CultureInfo.InvariantCulture),
                    f.LastReadComplete ? "1" : "0", f.Available ? "1" : "0"));
                foreach (SensorEntry e in l) if (e != null) w.WriteLine(NucleoProtocolo.DeEntrada(e));
                return;
            }
            w.WriteLine(NucleoProtocolo.Linha(NucleoProtocolo.Erro, "pedido desconhecido"));
        }

        public void Dispose()
        {
            _parar = true;
            NamedPipeServerStream atual;
            lock (_trava) atual = _atual;
            // Solta o WaitForConnection: uma conexao vazia acorda a thread, que ve o _parar.
            try
            {
                using (NamedPipeClientStream c = new NamedPipeClientStream(".", _nome, PipeDirection.InOut))
                    c.Connect(200);
            }
            catch { }
            if (atual != null) { try { atual.Dispose(); } catch { } }
            if (_thread != null) _thread.Join(2000);
        }
    }

    /// <summary>O lado da interface: uma conexao com o nucleo, compartilhada pelas fontes remotas.</summary>
    internal sealed class NucleoCliente : IDisposable
    {
        private readonly string _nome;
        private readonly object _trava = new object();
        private NamedPipeClientStream _pipe;
        private StreamReader _leitor;
        private StreamWriter _escritor;
        public int LimiteDeConexaoMs = 1500;

        public NucleoCliente(string nome) { _nome = nome; }

        private void Conectar()
        {
            if (_pipe != null && _pipe.IsConnected) return;
            Fechar();
            NamedPipeClientStream p = new NamedPipeClientStream(".", _nome, PipeDirection.InOut, PipeOptions.None,
                TokenImpersonationLevel.Identification);
            try
            {
                p.Connect(LimiteDeConexaoMs);
                _leitor = new StreamReader(p, new UTF8Encoding(false), false, 65536);
                _escritor = new StreamWriter(p, new UTF8Encoding(false), 4096);
                _escritor.NewLine = "\n";
                _pipe = p;
                _escritor.WriteLine(NucleoProtocolo.Linha(NucleoProtocolo.Ola));
                _escritor.Flush();
                string[] r = Resposta();
                int versao;
                if (r.Length < 2 || !int.TryParse(r[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out versao) ||
                    versao != Nucleo.Versao)
                    throw new IOException("nucleo de outra versao");
            }
            catch
            {
                _pipe = p;
                Fechar();
                throw;
            }
        }

        private string[] Resposta()
        {
            string linha = _leitor.ReadLine();
            if (linha == null) throw new IOException("nucleo fechou a conexao");
            string[] c = NucleoProtocolo.Campos(linha);
            if (c.Length == 0 || c[0] != NucleoProtocolo.Ok)
                throw new IOException("nucleo recusou: " + (c.Length > 1 ? c[1] : linha));
            return c;
        }

        private void Fechar()
        {
            if (_pipe != null) { try { _pipe.Dispose(); } catch { } }
            _pipe = null; _leitor = null; _escritor = null;
        }

        /// <summary>
        /// Um pedido e a resposta (mais as linhas de leitura, quando ha). Lanca
        /// IOException sem nucleo; a conexao e refeita no proximo pedido.
        /// </summary>
        public string[] Pedir(string linha, List<string> leituras)
        {
            lock (_trava)
            {
                try
                {
                    Conectar();
                    _escritor.WriteLine(linha);
                    _escritor.Flush();
                    string[] r = Resposta();
                    if (leituras != null)
                    {
                        int n;
                        if (r.Length < 2 || !int.TryParse(r[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 0)
                            throw new IOException("resposta invalida do nucleo");
                        for (int i = 0; i < n; i++)
                        {
                            string l = _leitor.ReadLine();
                            if (l == null) throw new IOException("nucleo fechou a conexao");
                            leituras.Add(l);
                        }
                    }
                    return r;
                }
                catch (Exception ex)
                {
                    Fechar();
                    if (ex is IOException) throw;
                    throw new IOException("nucleo indisponivel: " + ex.GetType().Name, ex);
                }
            }
        }

        public void Dispose() { lock (_trava) Fechar(); }
    }

    /// <summary>Uma fonte elevada vista da interface: os mesmos pedidos de ISensorSource, feitos ao nucleo.</summary>
    internal sealed class FonteRemota : ISensorSource
    {
        private readonly NucleoCliente _cliente;
        private readonly string _fonte;
        private string _nome;
        private bool _disponivel, _completa = true, _avisou;
        /// <summary>O nucleo caiu ou reiniciou: do outro lado a fonte esta fechada, e preciso abrir de novo.</summary>
        private bool _reabrir;

        /// <summary>Os ids em uso, para a fonte do outro lado ler so os grupos deles (como LibreSensorSource.Foco).</summary>
        public ICollection<string> Foco;

        public FonteRemota(NucleoCliente cliente, string fonte, string nome)
        {
            _cliente = cliente; _fonte = fonte; _nome = nome;
        }

        public string Name { get { return _nome; } }
        public bool Available { get { return _disponivel; } }
        public bool LastReadComplete { get { return _completa; } }

        private void Falhou(Exception ex)
        {
            _completa = false;
            _reabrir = true;
            if (!_avisou) Log.Write("nucleo: " + _fonte + " sem resposta (" + ex.Message + ")");
            _avisou = true;
        }

        public bool Open()
        {
            try
            {
                string[] r = _cliente.Pedir(NucleoProtocolo.Linha(NucleoProtocolo.Abrir, _fonte), null);
                _disponivel = r.Length > 2 && r[2] == "1";
                if (r.Length > 3 && !string.IsNullOrEmpty(r[3])) _nome = r[3];
                _avisou = false;
                _reabrir = false;
                return r.Length > 1 && r[1] == "1";
            }
            catch (IOException ex) { Falhou(ex); _disponivel = false; return false; }
        }

        public void Refresh()
        {
            try
            {
                ICollection<string> foco = Foco;
                _cliente.Pedir(NucleoProtocolo.Escapar(NucleoProtocolo.Atualizar) + "\t" + NucleoProtocolo.Escapar(_fonte) + "\t" +
                    NucleoProtocolo.DeIds(foco), null);
            }
            catch (IOException ex) { Falhou(ex); }
        }

        public List<SensorEntry> Read(IEnumerable<string> focusedIds)
        {
            List<SensorEntry> l = new List<SensorEntry>();
            if (_reabrir && !Open()) return l;
            try
            {
                List<string> linhas = new List<string>();
                string[] r = _cliente.Pedir(NucleoProtocolo.Escapar(NucleoProtocolo.Ler) + "\t" +
                    NucleoProtocolo.Escapar(_fonte) + "\t" + NucleoProtocolo.DeIds(focusedIds), linhas);
                _completa = r.Length > 2 && r[2] == "1";
                // Nucleo novo no lugar do antigo: responde, mas com a fonte ainda fechada.
                if (r.Length > 3 && r[3] != "1") { _reabrir = true; _completa = false; }
                foreach (string linha in linhas)
                {
                    SensorEntry e = NucleoProtocolo.ParaEntrada(linha);
                    if (e != null) l.Add(e); else _completa = false;
                }
                _avisou = false;
            }
            catch (IOException ex) { Falhou(ex); }
            return l;
        }

        public void Dispose() { }
    }
}
