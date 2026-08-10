using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace MhiagosControl
{
    /// <summary>
    /// Banco de provas do ciclo de atualizacao.
    ///
    /// Existe porque a alternativa era otimizar por leitura de codigo. Da para
    /// contar as alocacoes de BuildRaw com o dedo na tela e chegar a um numero
    /// grande; o que a conta nao diz e se esse numero importa perto do que a
    /// biblioteca de sensores aloca sozinha ao percorrer o hardware. Sem essa
    /// comparacao, "otimizacao" e so reescrita.
    ///
    /// Mede com AppDomain.MonitoringTotalAllocatedMemorySize, que conta bytes
    /// alocados de verdade - GC.GetTotalMemory devolve o que sobreviveu, e lixo
    /// de vida curta, que e exatamente o caso aqui, nao aparece nele.
    ///
    /// Precisa de privilegio administrativo, como o aplicativo. Sem ele as
    /// fontes abrem pela metade e a medicao mede outra maquina.
    /// </summary>
    public static class Bench
    {
        private const int Warmup = 15;
        private const int Cycles = 300;

        /// <summary>Cadencia real do aplicativo, para converter por-ciclo em por-hora.</summary>
        private const double CyclesPerHour = 3600000.0 / 1100.0;

        public static int Main(string[] args)
        {
            AppDomain.MonitoringIsEnabled = true;

            Console.WriteLine("Mhiagos Control - banco de provas do ciclo");
            Console.WriteLine("privilegio administrativo: " + (Elevado() ? "sim" : "NAO (medicao nao vale)"));
            Console.WriteLine();

            Sensors s = new Sensors();
            try { s.Open(); }
            catch (Exception ex) { Console.WriteLine("falha ao abrir fontes: " + ex.Message); return 1; }

            using (s)
            {
                List<SensorEntry> lista = s.List();
                Console.WriteLine("fontes: HWiNFO " + (s.HwInfoActive ? "ativo" : "ausente"));
                Console.WriteLine("sensores crus: " + Crus(s).Count + "   apos resumo: " + lista.Count);
                Console.WriteLine();

                string id1, id2;
                Escolher(lista, out id1, out id2);
                Console.WriteLine("painel 1: " + id1);
                Console.WriteLine("painel 2: " + id2);
                Console.WriteLine();

                for (int i = 0; i < Warmup; i++) { s.Refresh(); s.ReadEntry(id1); s.ReadEntry(id2); }

                Medir("ciclo completo (Refresh + 2x ReadEntry + Prepare)", delegate
                {
                    s.Refresh();
                    SensorEntry e1 = s.ReadEntry(id1);
                    SensorEntry e2 = s.ReadEntry(id2);
                    Scaling.Prepare(e1, Scaling.Effective(0, e1), false);
                    Scaling.Prepare(e2, Scaling.Effective(0, e2), false);
                });

                Medir("  so Refresh (biblioteca + BuildRaw)", delegate { s.Refresh(); });
                Medir("    so a varredura da biblioteca", delegate { Varrer(s); });
                Medir("    so BuildRaw", delegate { BuildRaw(s); });
                Medir("  so 2x ReadEntry", delegate { s.ReadEntry(id1); s.ReadEntry(id2); });
                Medir("Snapshot (so com a janela aberta)", delegate { s.Snapshot(); });
                Medir("List (so ao abrir a janela)", delegate { s.List(); });

                if (s.HwInfoActive) DentroDoHwInfo(s);
            }
            return 0;
        }

        private static void Medir(string nome, Action passo)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();

            long b0 = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < Cycles; i++) passo();

            sw.Stop();
            long bytes = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - b0;
            int c0 = GC.CollectionCount(0) - g0;
            int c1 = GC.CollectionCount(1) - g1;
            int c2 = GC.CollectionCount(2) - g2;

            double porCiclo = (double)bytes / Cycles;
            double gen0Hora = (double)c0 / Cycles * CyclesPerHour;

            Console.WriteLine(nome);
            Console.WriteLine(string.Format(
                "   {0,9:n0} B/ciclo   {1,7:n2} ms/ciclo   gen0 {2}/{3} gen1 {4} gen2 {5}   ~{6:n0} gen0/hora",
                porCiclo, sw.Elapsed.TotalMilliseconds / Cycles, c0, Cycles, c1, c2, gen0Hora));
            Console.WriteLine();
        }

        /// <summary>
        /// Escolhe dois sensores como o aplicativo escolheria: um de temperatura
        /// e um de carga. Ler o config real seria mais fiel, mas o banco tem de
        /// rodar em qualquer maquina, inclusive sem configuracao gravada.
        /// </summary>
        private static void Escolher(List<SensorEntry> lista, out string id1, out string id2)
        {
            id1 = null; id2 = null;
            foreach (SensorEntry e in lista)
                if (e.Type == LibreHardwareMonitor.Hardware.SensorType.Temperature) { id1 = e.Id; break; }
            foreach (SensorEntry e in lista)
                if (e.Type == LibreHardwareMonitor.Hardware.SensorType.Load) { id2 = e.Id; break; }

            // um agregado, quando houver: ReadSynthetic percorre a lista inteira
            // por membro, e e o caminho caro
            foreach (SensorEntry e in lista)
                if (e.Members > 1) { id2 = e.Id; break; }

            if (id1 == null && lista.Count > 0) id1 = lista[0].Id;
            if (id2 == null && lista.Count > 1) id2 = lista[1].Id;
        }

        /// <summary>
        /// Abre o ReadAll do HWiNFO, que e onde o ciclo inteiro mora.
        ///
        /// A pergunta e uma so: os 57 ms estao na chamada que manda a biblioteca
        /// reler o hardware, ou na varredura que reenumera tudo em seguida? A
        /// primeira e o preco do dado; a segunda seria nossa e teria conserto.
        ///
        /// As funcoes da biblioteca sao chamadas por DynamicInvoke, que custa
        /// microssegundos - irrelevante contra dezenas de milissegundos, e o
        /// unico jeito de alcanca-las sem abrir os delegates privados so para
        /// poder medir.
        /// </summary>
        private static void DentroDoHwInfo(Sensors s)
        {
            object hw = FiHw.GetValue(s);
            if (hw == null) return;

            Delegate count = (Delegate)Campo(hw, "_count");
            Delegate poll = (Delegate)Campo(hw, "_poll");
            if (count == null || poll == null) return;

            int grupos = (int)count.DynamicInvoke(null);
            int clsMin = (int)Constante(typeof(HwInfo), "CLS_MIN");
            int clsMax = (int)Constante(typeof(HwInfo), "CLS_MAX");

            HwInfo tipado = (HwInfo)hw;
            int leituras = tipado.ReadAll().Count;

            // Cada par (grupo, classe) gasta pelo menos uma chamada a mais: a
            // que devolve 0 e encerra a serie. Sao elas as candidatas a sobrar.
            int sondas = grupos * (clsMax - clsMin + 1);
            Console.WriteLine("--- por dentro do ReadAll ---");
            Console.WriteLine(string.Format(
                "   {0} grupos x {1} classes = {2} sondas de fim de serie, para {3} leituras uteis",
                grupos, clsMax - clsMin + 1, sondas, leituras));
            Console.WriteLine();

            Medir("ReadAll inteiro", delegate { tipado.ReadAll(); });
            Medir("  so a chamada que rele o hardware (263)", delegate { poll.DynamicInvoke(null); });
            Medir("  so a contagem de grupos (156)", delegate { count.DynamicInvoke(null); });

            PorChamada(hw, grupos, clsMin, clsMax);
        }

        /// <summary>
        /// Custo de cada funcao da biblioteca, uma chamada por vez.
        ///
        /// A conta que importa: se a sonda que encerra a serie custar o mesmo
        /// que uma leitura util, quase metade das chamadas do ciclo nao traz
        /// dado nenhum, e aprender a forma uma vez as elimina.
        ///
        /// DynamicInvoke acrescenta alguns microssegundos por chamada. A
        /// contagem de grupos, medida logo acima pelo mesmo caminho, saiu em
        /// 0,00 ms - entao o acrescimo esta abaixo da resolucao daqui e nao
        /// atrapalha numeros na casa das dezenas de microssegundos.
        /// </summary>
        private static void PorChamada(object hw, int grupos, int clsMin, int clsMax)
        {
            Delegate select = (Delegate)Campo(hw, "_select");
            Delegate nome = (Delegate)Campo(hw, "_groupName");
            Delegate read = (Delegate)Campo(hw, "_read");
            if (select == null || nome == null || read == null) return;

            int elem = (int)Constante(typeof(HwInfo), "ELEM");
            byte[] buf = new byte[elem];
            byte[] nomeBuf = new byte[256];

            // Forma real: quantas leituras cada par (grupo, classe) tem.
            int uteis = 0, vazios = 0;
            int hitCls = -1, hitGrupo = -1, hitJ = -1;
            int missCls = -1, missGrupo = -1, missJ = -1;

            for (int i = 0; i < grupos; i++)
            {
                select.DynamicInvoke(i);
                for (int cls = clsMin; cls <= clsMax; cls++)
                {
                    int j = 0;
                    while (j < 256 && (int)read.DynamicInvoke(cls, i, j, buf) != 0)
                    {
                        if (hitCls < 0) { hitCls = cls; hitGrupo = i; hitJ = j; }
                        uteis++; j++;
                    }
                    if (j == 0) vazios++;
                    if (missCls < 0) { missCls = cls; missGrupo = i; missJ = j; }
                }
            }

            Console.WriteLine(string.Format(
                "   forma: {0} leituras uteis, {1} pares (grupo,classe) totalmente vazios",
                uteis, vazios));
            Console.WriteLine();

            if (hitCls >= 0)
            {
                int hc = hitCls, hg = hitGrupo, hj = hitJ;
                select.DynamicInvoke(hg);
                Medir("  uma leitura util (641 devolvendo dado)",
                      delegate { read.DynamicInvoke(hc, hg, hj, buf); });
            }

            if (missCls >= 0)
            {
                int mc = missCls, mg = missGrupo, mj = missJ;
                select.DynamicInvoke(mg);
                Medir("  uma sonda de fim de serie (641 devolvendo 0)",
                      delegate { read.DynamicInvoke(mc, mg, mj, buf); });
            }

            Medir("  preparar um grupo (678)", delegate { select.DynamicInvoke(0); });
            Medir("  nome de um grupo (952)", delegate { nome.DynamicInvoke(0, nomeBuf, nomeBuf.Length); });
        }

        private static object Campo(object alvo, string nome)
        {
            FieldInfo f = alvo.GetType().GetField(nome, BindingFlags.Instance | BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(alvo);
        }

        private static object Constante(Type t, string nome)
        {
            FieldInfo f = t.GetField(nome, BindingFlags.Static | BindingFlags.NonPublic);
            return f == null ? 0 : f.GetRawConstantValue();
        }

        // ---- acesso ao que e privado, so para atribuir custo ----

        private static readonly FieldInfo FiHw =
            typeof(Sensors).GetField("_hw", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo MiBuildRaw =
            typeof(Sensors).GetMethod("BuildRaw", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FiComputer =
            typeof(Sensors).GetField("_computer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FiVisitor =
            typeof(Sensors).GetField("_visitor", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FiRaw =
            typeof(Sensors).GetField("_raw", BindingFlags.Instance | BindingFlags.NonPublic);

        private static List<SensorEntry> BuildRaw(Sensors s)
        {
            return (List<SensorEntry>)MiBuildRaw.Invoke(s, null);
        }

        private static List<SensorEntry> Crus(Sensors s)
        {
            return (List<SensorEntry>)FiRaw.GetValue(s);
        }

        private static void Varrer(Sensors s)
        {
            LibreHardwareMonitor.Hardware.Computer c =
                (LibreHardwareMonitor.Hardware.Computer)FiComputer.GetValue(s);
            if (c == null) return;
            c.Accept((LibreHardwareMonitor.Hardware.IVisitor)FiVisitor.GetValue(s));
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
    }
}
