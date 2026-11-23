namespace MhiagosControl
{
    /// <summary>
    /// A edicao do executavel. A enxuta (build.ps1 -Edicao cpu, simbolo EDICAO_CPU) e o mesmo codigo
    /// com so o Painel CPU, Perfis, Metricas, Especificacoes, Visao geral, Configuracoes e Sobre:
    /// sem Casa, Clima, baterias dos perifericos e widget, e sem os servicos deles rodando.
    /// </summary>
    internal static class Edicao
    {
#if EDICAO_CPU
        public static readonly bool Enxuta = true;
#else
        public static readonly bool Enxuta = false;
#endif

        /// <summary>O nome curto que a interface recebe ("cpu" ou "completa").</summary>
        public static string Nome { get { return Enxuta ? "cpu" : "completa"; } }
    }
}
