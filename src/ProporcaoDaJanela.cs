using System;

namespace MhiagosControl
{
    /// <summary>
    /// Trava a razao largura/altura da area de cliente da janela nova ao arrastar as bordas (WM_SIZING),
    /// para a Visao geral, que e escalada como um palco fixo, nunca sobrar nem faltar espaco.
    /// A razao vem da propria pagina (o palco mais a barra lateral); estas contas sao puras para o teste.
    /// </summary>
    internal static class ProporcaoDaJanela
    {
        public const double Padrao = 1.30, Minima = 1.0, Maxima = 1.8;

        public static double Limitar(double razao)
        {
            if (double.IsNaN(razao) || double.IsInfinity(razao)) return Padrao;
            return Math.Max(Minima, Math.Min(Maxima, razao));
        }

        /// <summary>
        /// O retangulo (esq, topo, dir, baixo) da janela com a razao da area de cliente corrigida.
        /// borda: 1 esquerda, 2 direita, 3 topo, 4 topo-esq, 5 topo-dir, 6 base, 7 base-esq, 8 base-dir (WMSZ_*).
        /// fw, fh: o que a moldura soma a largura e a altura do cliente.
        /// </summary>
        public static int[] Ajustar(int esq, int topo, int dir, int baixo, int borda, int fw, int fh, double razao)
        {
            razao = Limitar(razao);
            int cw = (dir - esq) - fw, ch = (baixo - topo) - fh;
            if (cw < 1 || ch < 1) return new int[] { esq, topo, dir, baixo };
            if (borda == 3 || borda == 6)
            {
                // so a altura mexeu: a largura acompanha, para a direita
                dir = esq + (int)Math.Round(ch * razao) + fw;
            }
            else
            {
                // a largura manda; a altura acompanha, para baixo (ou para cima se a borda de cima e a que anda)
                int nova = (int)Math.Round(cw / razao) + fh;
                if (borda == 4 || borda == 5) topo = baixo - nova; else baixo = topo + nova;
            }
            return new int[] { esq, topo, dir, baixo };
        }

        /// <summary>A razao do cliente (largura/altura).</summary>
        public static double Razao(int largura, int altura) { return altura <= 0 ? Padrao : (double)largura / altura; }
    }
}
