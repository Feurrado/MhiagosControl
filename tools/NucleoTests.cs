using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    /// <summary>
    /// O nucleo (sensores elevados atras de um named pipe): protocolo, ida e volta
    /// por um pipe de verdade com uma fonte falsa, queda e volta do nucleo, e quem
    /// pode abrir o pipe. Nenhuma fonte real e aberta.
    /// </summary>
    public static partial class Tests
    {
        private sealed class FonteDoNucleo : ISensorSource
        {
            public List<SensorEntry> Entradas = new List<SensorEntry>();
            public List<string> UltimoFoco;
            public bool FocoNulo;
            public int Aberturas, Atualizacoes;
            public bool Aberta, Completa = true, Abre = true;
            public string Name { get { return "Falsa"; } }
            public bool Available { get { return Aberta; } }
            public bool LastReadComplete { get { return Completa; } }
            public bool Open() { Aberturas++; Aberta = Abre; return Abre; }
            public void Refresh() { Atualizacoes++; }
            public List<SensorEntry> Read(IEnumerable<string> focusedIds)
            {
                FocoNulo = focusedIds == null;
                UltimoFoco = focusedIds == null ? null : new List<string>(focusedIds);
                return Entradas;
            }
            public void Dispose() { }
        }

        private static SensorEntry EntradaDoNucleo(string id, float? valor, string unidade)
        {
            SensorEntry e = new SensorEntry();
            e.Id = id; e.Hardware = "AMD Ryzen 5 5600X"; e.Name = "CPU (Tctl/Tdie)"; e.Category = "CPU";
            e.Label = "CPU · Tctl\tTdie \\ 1"; e.Type = SensorType.Temperature; e.Value = valor;
            e.Source = "HWiNFO"; e.Members = 3; e.Unit = unidade;
            return e;
        }

        private static void NucleoSeparado()
        {
            // --- protocolo: escape e a leitura numa linha ---
            string[] dificeis = { "", "a\tb", "barra\\invertida", "duas\nlinhas\r", "\\0", "\\t literal", "acentuação °C" };
            foreach (string s in dificeis)
                Igual(s, NucleoProtocolo.Desescapar(NucleoProtocolo.Escapar(s)), "nucleo: escape vai e volta");
            Verdade(NucleoProtocolo.Desescapar(NucleoProtocolo.Escapar(null)) == null, "nucleo: nulo continua nulo");
            Verdade(NucleoProtocolo.Escapar("a\tb\nc").IndexOf('\t') < 0 && NucleoProtocolo.Escapar("a\tb\nc").IndexOf('\n') < 0,
                "nucleo: campo escapado nao tem TAB nem quebra de linha");
            string[] campos = NucleoProtocolo.Campos(NucleoProtocolo.Linha("LER", "hwinfo", null, "x\ty"));
            Verdade(campos.Length == 4 && campos[0] == "LER" && campos[2] == null && campos[3] == "x\ty", "nucleo: linha em campos");

            SensorEntry ida = EntradaDoNucleo("hw:/amd/0/temp/1", 61.375f, null);
            SensorEntry volta = NucleoProtocolo.ParaEntrada(NucleoProtocolo.DeEntrada(ida));
            Verdade(volta != null, "nucleo: leitura valida volta");
            if (volta != null)
            {
                Igual(ida.Id, volta.Id, "nucleo: id");
                Igual(ida.Label, volta.Label, "nucleo: rotulo com TAB e barra");
                Igual(ida.Hardware, volta.Hardware, "nucleo: hardware");
                Igual(ida.Category, volta.Category, "nucleo: categoria");
                Verdade(volta.Type == SensorType.Temperature, "nucleo: tipo");
                Verdade(volta.Value.HasValue && volta.Value.Value == 61.375f, "nucleo: valor exato");
                Igual(3, volta.Members, "nucleo: membros");
                Verdade(volta.Unit == null, "nucleo: unidade nula continua nula");
                Igual("HWiNFO", volta.Source, "nucleo: origem");
            }
            SensorEntry semValor = NucleoProtocolo.ParaEntrada(NucleoProtocolo.DeEntrada(EntradaDoNucleo("x", null, "MB")));
            Verdade(semValor != null && !semValor.Value.HasValue && semValor.Unit == "MB", "nucleo: sem leitura continua sem leitura");
            Verdade(NucleoProtocolo.ParaEntrada("lixo") == null, "nucleo: linha invalida e recusada");
            Verdade(NucleoProtocolo.ParaEntrada(NucleoProtocolo.Linha("a", "b", "c", "d", "e", "nao-numero", "1", "f", "1", "g")) == null,
                "nucleo: tipo invalido e recusado");

            Igual("*", NucleoProtocolo.DeIds(null), "nucleo: lista nula quer dizer todos");
            Igual("-", NucleoProtocolo.DeIds(new string[0]), "nucleo: lista vazia quer dizer nenhum");
            Verdade(NucleoProtocolo.ParaIds(new[] { "LER", "f", "*" }, 2) == null, "nucleo: todos volta nulo");
            Igual(0, NucleoProtocolo.ParaIds(new[] { "LER", "f", "-" }, 2).Count, "nucleo: nenhum volta vazio");
            Igual(2, NucleoProtocolo.ParaIds(new[] { "LER", "f", "a", "b" }, 2).Count, "nucleo: dois ids");

            // --- quem pode abrir o pipe ---
            PipeSecurity seg = NucleoServidor.Seguranca();
            bool euPosso = false, redeNegada = false, outros = false;
            SecurityIdentifier eu;
            using (WindowsIdentity id = WindowsIdentity.GetCurrent()) eu = id.User;
            SecurityIdentifier rede = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
            foreach (PipeAccessRule r in seg.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                if (r.IdentityReference.Equals(eu) && r.AccessControlType == AccessControlType.Allow) euPosso = true;
                else if (r.IdentityReference.Equals(rede) && r.AccessControlType == AccessControlType.Deny) redeNegada = true;
                else outros = true;
            }
            Verdade(euPosso && redeNegada && !outros, "nucleo: pipe so para o proprio usuario, e nunca pela rede");
            Verdade(Nucleo.NomeDoPipe.EndsWith(eu.Value), "nucleo: um pipe por usuario");

            // --- ida e volta por um pipe de verdade ---
            string nome = "MhiagosControl.Teste." + Guid.NewGuid().ToString("N");
            FonteDoNucleo falsa = new FonteDoNucleo();
            falsa.Entradas.Add(EntradaDoNucleo("hw:1", 55.5f, "°C"));
            falsa.Entradas.Add(EntradaDoNucleo("hw:2", null, null));
            falsa.Entradas.Add(null);
            Dictionary<string, ISensorSource> fontes = new Dictionary<string, ISensorSource>();
            fontes["falsa"] = falsa;

            NucleoCliente cliente = new NucleoCliente(nome);
            cliente.LimiteDeConexaoMs = 300;
            FonteRemota remota = new FonteRemota(cliente, "falsa", "?");
            Verdade(!remota.Open() && !remota.Available, "nucleo: sem nucleo a fonte nao abre");
            Igual(0, remota.Read(null).Count, "nucleo: sem nucleo a leitura vem vazia");
            Verdade(!remota.LastReadComplete, "nucleo: sem nucleo a leitura nao e completa");

            cliente.LimiteDeConexaoMs = 3000;
            NucleoServidor servidor = new NucleoServidor(nome, fontes);
            servidor.Iniciar();
            try
            {
                Verdade(remota.Open() && remota.Available, "nucleo: fonte abre pelo pipe");
                Igual("Falsa", remota.Name, "nucleo: o nome vem da fonte de verdade");
                List<SensorEntry> l = remota.Read(new[] { "hw:1", "id com\ttab" });
                Igual(2, l.Count, "nucleo: duas leituras (a nula fica fora)");
                Verdade(l.Count == 2 && l[0].Id == "hw:1" && l[0].Value == 55.5f && l[0].Unit == "°C" && !l[1].Value.HasValue,
                    "nucleo: leituras chegam iguais");
                Verdade(remota.LastReadComplete, "nucleo: leitura completa");
                Verdade(falsa.UltimoFoco != null && falsa.UltimoFoco.Count == 2 && falsa.UltimoFoco[1] == "id com\ttab",
                    "nucleo: os ids em foco chegam a fonte");
                remota.Read(null);
                Verdade(falsa.FocoNulo, "nucleo: sem foco a fonte le tudo");
                remota.Read(new string[0]);
                Verdade(!falsa.FocoNulo && falsa.UltimoFoco.Count == 0, "nucleo: foco vazio chega vazio");
                remota.Refresh();
                Igual(1, falsa.Atualizacoes, "nucleo: atualizar chega a fonte");
                falsa.Completa = false;
                remota.Read(null);
                Verdade(!remota.LastReadComplete, "nucleo: leitura incompleta e informada");
                falsa.Completa = true;
                Verdade(remota.Open(), "nucleo: abrir de novo");
                Igual(1, falsa.Aberturas, "nucleo: abrir de novo nao reabre a fonte");

                // Pedido a uma fonte que o nucleo nao serve: recusa, e a conexao segue valendo.
                FonteRemota estranha = new FonteRemota(cliente, "..\\..\\outra", "?");
                Verdade(!estranha.Open(), "nucleo: fonte desconhecida e recusada");
                Igual(2, remota.Read(null).Count, "nucleo: depois da recusa a fonte certa continua lendo");
            }
            finally { servidor.Dispose(); }

            // --- o nucleo caiu e voltou: a fonte reabre sozinha ---
            Igual(0, remota.Read(null).Count, "nucleo: com o nucleo fora a leitura vem vazia");
            Verdade(!remota.LastReadComplete, "nucleo: com o nucleo fora a leitura nao e completa");
            FonteDoNucleo nova = new FonteDoNucleo();
            nova.Entradas.Add(EntradaDoNucleo("hw:9", 40f, null));
            Dictionary<string, ISensorSource> fontes2 = new Dictionary<string, ISensorSource>();
            fontes2["falsa"] = nova;
            NucleoServidor servidor2 = new NucleoServidor(nome, fontes2);
            servidor2.Iniciar();
            try
            {
                List<SensorEntry> l = remota.Read(null);
                Verdade(l.Count == 1 && l[0].Id == "hw:9", "nucleo: depois de voltar a leitura retoma");
                Igual(1, nova.Aberturas, "nucleo: a fonte e reaberta no nucleo novo");
            }
            finally { servidor2.Dispose(); cliente.Dispose(); }

            // --- o app de hoje nao muda: elevado e sem a variavel, le direto ---
            string antes = Environment.GetEnvironmentVariable(Nucleo.Variavel);
            try
            {
                Environment.SetEnvironmentVariable(Nucleo.Variavel, "1");
                Verdade(Nucleo.UsarRemoto, "nucleo: a variavel liga o modo separado");
                Environment.SetEnvironmentVariable(Nucleo.Variavel, null);
                Igual(!Nucleo.Elevado(), Nucleo.UsarRemoto, "nucleo: sem a variavel, so sem elevacao usa o nucleo");
            }
            finally { Environment.SetEnvironmentVariable(Nucleo.Variavel, antes); }
        }
    }
}
