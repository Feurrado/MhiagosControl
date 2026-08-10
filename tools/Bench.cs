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

        // ---- acesso ao que e privado, so para atribuir custo ----

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
