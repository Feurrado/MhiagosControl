using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.Principal;
using System.Text;

namespace MhiagosRtssProbe
{
    /// <summary>
    /// Diagnostico da ponte com o RivaTuner Statistics Server.
    ///
    /// Existe porque "o FPS nao aparece" tem varias causas que se parecem: RTSS
    /// parado, jogo que o RTSS nao engancha, memoria compartilhada invisivel
    /// para o processo que le. Sem separar as tres, o conserto vira tentativa.
    ///
    /// Rode DUAS vezes - uma normal e uma como administrador. O aplicativo pede
    /// elevacao no manifesto, entao e o resultado elevado que descreve o que ele
    /// enxerga; a diferenca entre os dois, quando existe, e a resposta.
    /// </summary>
    public static class Probe
    {
        private const string Mapa = "RTSSSharedMemoryV2";

        private const int HdrVersao = 4;
        private const int HdrTamEntrada = 8;
        private const int HdrInicioVetor = 12;
        private const int HdrTamVetor = 16;

        private const int EntNome = 4;
        private const int EntNomeTam = 260;
        private const int EntTempo0 = 268;
        private const int EntTempo1 = 272;
        private const int EntQuadros = 276;
        private const int EntTempoDeQuadro = 280;

        public static int Main()
        {
            Console.WriteLine("== ponte com o RTSS ==");
            Console.WriteLine("processo    : " + (Environment.Is64BitProcess ? "64 bits" : "32 bits"));
            Console.WriteLine("elevado     : " + (Elevado() ? "SIM" : "nao"));
            Console.WriteLine("usuario     : " + Environment.UserDomainName + "\\" + Environment.UserName);
            Console.WriteLine();

            Tentar("Read     ", MemoryMappedFileRights.Read);
            Tentar("ReadWrite", MemoryMappedFileRights.ReadWrite);
            Console.WriteLine();

            Despejar();

            Console.WriteLine();
            Console.WriteLine("Enter para fechar.");
            Console.ReadLine();
            return 0;
        }

        private static bool Elevado()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static void Tentar(string rotulo, MemoryMappedFileRights direitos)
        {
            try
            {
                using (MemoryMappedFile m = MemoryMappedFile.OpenExisting(Mapa, direitos))
                    Console.WriteLine("abrir " + rotulo + " : ABRIU");
            }
            catch (Exception ex)
            {
                Console.WriteLine("abrir " + rotulo + " : " + ex.GetType().Name + " - " + ex.Message);
            }
        }

        /// <summary>Cabecalho e entradas, como o aplicativo os enxerga.</summary>
        private static void Despejar()
        {
            MemoryMappedFile mmf = null;
            MemoryMappedViewAccessor v = null;
            try
            {
                mmf = MemoryMappedFile.OpenExisting(Mapa, MemoryMappedFileRights.Read);
                v = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                string assinatura = "" + (char)v.ReadByte(0) + (char)v.ReadByte(1) +
                                         (char)v.ReadByte(2) + (char)v.ReadByte(3);
                uint versao = v.ReadUInt32(HdrVersao);
                uint tam = v.ReadUInt32(HdrTamEntrada);
                uint inicio = v.ReadUInt32(HdrInicioVetor);
                uint quantas = v.ReadUInt32(HdrTamVetor);

                // "SSTR" e o esperado: o literal multicaractere 'RTSS' do C cai
                // assim em little-endian. Ler "RTSS" aqui seria o inesperado.
                Console.WriteLine("assinatura  : " + assinatura +
                                  (assinatura == "SSTR" ? "   (correta)" : "   (ESPERADO SSTR)"));
                Console.WriteLine("versao      : 0x" + versao.ToString("X8"));
                Console.WriteLine("entrada     : " + tam + " bytes");
                Console.WriteLine("vetor       : " + quantas + " posicoes a partir de +" + inicio);
                Console.WriteLine();

                int vivos = 0;
                for (uint i = 0; i < quantas && i < 4096; i++)
                {
                    long b = inicio + (long)i * tam;
                    uint pid = v.ReadUInt32(b);
                    if (pid == 0) continue;

                    uint t0 = v.ReadUInt32(b + EntTempo0);
                    uint t1 = v.ReadUInt32(b + EntTempo1);
                    uint quadros = v.ReadUInt32(b + EntQuadros);
                    uint us = v.ReadUInt32(b + EntTempoDeQuadro);

                    string fps = (t1 > t0) ? (quadros * 1000f / (t1 - t0)).ToString("0.0") : "-";
                    Console.WriteLine(string.Format(
                        "  [{0}] pid {1,-6} {2,-28} t0={3} t1={4} quadros={5} ft={6}us  -> {7} FPS",
                        i, pid, Nome(v, b), t0, t1, quadros, us, fps));
                    vivos++;
                }

                if (vivos == 0)
                    Console.WriteLine("  nenhuma entrada preenchida - o RTSS esta no ar mas nao");
                Console.WriteLine(vivos == 0 ? "  esta enganchado em nada no momento." : "");
            }
            catch (Exception ex)
            {
                Console.WriteLine("nao deu para despejar: " + ex.GetType().Name + " - " + ex.Message);
            }
            finally
            {
                if (v != null) v.Dispose();
                if (mmf != null) mmf.Dispose();
            }
        }

        private static string Nome(MemoryMappedViewAccessor v, long b)
        {
            try
            {
                byte[] buf = new byte[EntNomeTam];
                v.ReadArray(b + EntNome, buf, 0, buf.Length);
                int fim = Array.IndexOf(buf, (byte)0);
                if (fim < 0) fim = buf.Length;
                string caminho = Encoding.Default.GetString(buf, 0, fim).Trim();
                int barra = caminho.LastIndexOf('\\');
                return barra >= 0 && barra < caminho.Length - 1
                     ? caminho.Substring(barra + 1) : caminho;
            }
            catch { return "?"; }
        }
    }
}
