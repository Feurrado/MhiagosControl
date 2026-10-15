using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MhiagosControl
{
    /// <summary>
    /// Confere um alias de execucao de app (o winget.exe de WindowsApps).
    ///
    /// O aplicativo e o instalador rodam elevados, e a pasta
    /// %LOCALAPPDATA%\Microsoft\WindowsApps e gravavel sem elevacao. So olhar
    /// o atributo de ponto de reparse deixava passar um ponto de reparse
    /// qualquer criado ali por um programa sem privilegio. Aqui o ponto tem de
    /// ser um alias de app (IO_REPARSE_TAG_APPEXECLINK) e o executavel que ele
    /// aponta tem de morar em Program Files\WindowsApps, pasta que so o
    /// sistema grava.
    /// </summary>
    internal static class AliasDoWindows
    {
        private const uint IO_REPARSE_TAG_APPEXECLINK = 0x8000001B;
        private const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;
        private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string nome, uint acesso, uint compartilhar, IntPtr seg,
                                                         uint disposicao, uint flags, IntPtr modelo);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle h, uint codigo, IntPtr entrada, int tamEntrada,
                                                   byte[] saida, int tamSaida, out int devolvidos, IntPtr sobreposto);

        /// <summary>O executavel de verdade por tras do alias, ou nulo se nao for um alias confiavel.</summary>
        public static string Alvo(string alias)
        {
            try
            {
                using (SafeFileHandle h = CreateFileW(alias, 0, 7, IntPtr.Zero, 3,
                           FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
                {
                    if (h.IsInvalid) return null;
                    byte[] b = new byte[16 * 1024];
                    int n;
                    if (!DeviceIoControl(h, FSCTL_GET_REPARSE_POINT, IntPtr.Zero, 0, b, b.Length, out n, IntPtr.Zero)) return null;
                    return AlvoDosDados(b, n);
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// Le o REPARSE_DATA_BUFFER de um alias: etiqueta, versao e tres textos
        /// (familia do pacote, AUMID e o executavel). Separado para os testes.
        /// </summary>
        internal static string AlvoDosDados(byte[] b, int n)
        {
            if (b == null || n < 12) return null;
            if (BitConverter.ToUInt32(b, 0) != IO_REPARSE_TAG_APPEXECLINK) return null;
            if (BitConverter.ToUInt32(b, 8) < 3) return null;
            string[] textos = Encoding.Unicode.GetString(b, 12, n - 12).Split('\0');
            if (textos.Length < 3) return null;
            string alvo = textos[2];
            return Confiavel(alvo) ? alvo : null;
        }

        /// <summary>So executavel instalado pelo sistema, em Program Files\WindowsApps.</summary>
        internal static bool Confiavel(string alvo)
        {
            if (string.IsNullOrEmpty(alvo) || alvo.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrEmpty(pf)) return false;
            string pasta = Path.Combine(pf, "WindowsApps") + "\\";
            return Path.GetFullPath(alvo).StartsWith(pasta, StringComparison.OrdinalIgnoreCase);
        }
    }
}
