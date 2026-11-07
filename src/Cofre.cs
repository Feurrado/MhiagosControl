using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MhiagosControl
{
    /// <summary>
    /// Segredos do app (acesso da ConnectLife, chave do servidor da casa) num
    /// arquivo por alvo em DataDir, cifrado com a DPAPI do usuario.
    ///
    /// Ate a 2.30 eles iam para o Gerenciador de Credenciais. Com o app subindo
    /// elevado pela tarefa de logon, o CredWrite falhava com o erro 1312
    /// (ERROR_NO_SUCH_LOGON_SESSION) a cada renovacao: o refresh token novo se
    /// perdia e o login so sobrevivia porque a nuvem ainda aceitava o velho. A
    /// DPAPI so precisa do perfil do usuario carregado, o que vale em qualquer
    /// sessao de logon dele. O Gerenciador fica so como leitura de migracao.
    ///
    /// Protecao igual a do Gerenciador para segredo generico: qualquer processo
    /// do mesmo usuario consegue ler. O arquivo nao sai desta maquina nem deste
    /// usuario (a chave da DPAPI e dele).
    /// </summary>
    internal static class Cofre
    {
        /// <summary>Pasta dos arquivos; nulo e a pasta de dados. Os testes trocam.</summary>
        internal static string Pasta;

        /// <summary>Testes: nao toca no Gerenciador de Credenciais da maquina.</summary>
        internal static bool SemGerenciador;

        /// <summary>A ultima gravacao falhou: quem mostra estado pode avisar.</summary>
        public static string UltimaFalha { get; private set; }

        internal static string Arquivo(string alvo)
        {
            StringBuilder nome = new StringBuilder("cofre-");
            int barra = alvo.LastIndexOf('/');
            foreach (char c in barra >= 0 ? alvo.Substring(barra + 1) : alvo)
                if (char.IsLetterOrDigit(c)) nome.Append(c);
            return Path.Combine(Pasta ?? Paths.DataDir, nome + ".dat");
        }

        public static string Ler(string alvo)
        {
            string arquivo = Arquivo(alvo);
            try
            {
                if (File.Exists(arquivo))
                {
                    byte[] claro = Dpapi(File.ReadAllBytes(arquivo), Entropia(alvo), false);
                    try { return Encoding.UTF8.GetString(claro); }
                    finally { Array.Clear(claro, 0, claro.Length); }
                }
            }
            catch (Exception ex) { Log.Error("cofre: ler " + alvo, ex); }

            // Migracao: quem vem da 2.30 tem o segredo so no Gerenciador.
            string antigo = SemGerenciador ? null : LerDoGerenciador(alvo);
            if (!string.IsNullOrEmpty(antigo))
            {
                Log.Write("cofre: " + alvo + " migrado do Gerenciador de Credenciais");
                Gravar(alvo, antigo);
            }
            return antigo;
        }

        public static bool Gravar(string alvo, string segredo)
        {
            if (string.IsNullOrEmpty(segredo)) { Apagar(alvo); return true; }
            byte[] claro = Encoding.UTF8.GetBytes(segredo);
            try
            {
                byte[] cifrado = Dpapi(claro, Entropia(alvo), true);
                GravacaoAtomica.Escrever(Arquivo(alvo), delegate(Stream s) { s.Write(cifrado, 0, cifrado.Length); });
                UltimaFalha = null;
                return true;
            }
            catch (Exception ex)
            {
                UltimaFalha = ex.Message;
                Log.Error("cofre: gravar " + alvo, ex);
                return false;
            }
            finally { Array.Clear(claro, 0, claro.Length); }
        }

        public static void Apagar(string alvo)
        {
            try { string a = Arquivo(alvo); if (File.Exists(a)) File.Delete(a); }
            catch (Exception ex) { Log.Error("cofre: apagar " + alvo, ex); }
            // Uma copia antiga no Gerenciador voltaria pela migracao.
            if (!SemGerenciador && !CredDelete(alvo, 1, 0) && Marshal.GetLastWin32Error() != 1168)
                Log.Write("cofre: CredDelete falhou " + Marshal.GetLastWin32Error() + " em " + alvo);
        }

        private static byte[] Entropia(string alvo) { return Encoding.UTF8.GetBytes("Mhiagos|" + alvo); }

        private static string LerDoGerenciador(string alvo)
        {
            IntPtr p;
            if (!CredRead(alvo, 1, 0, out p)) return null;
            try
            {
                CREDENTIAL c = (CREDENTIAL)Marshal.PtrToStructure(p, typeof(CREDENTIAL));
                if (c.CredentialBlobSize == 0 || c.CredentialBlob == IntPtr.Zero) return null;
                return Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2);
            }
            catch (Exception ex) { Log.Error("cofre: ler do Gerenciador " + alvo, ex); return null; }
            finally { CredFree(p); }
        }

        // ---------------- DPAPI ----------------

        private const int SemInterface = 0x1;   // CRYPTPROTECT_UI_FORBIDDEN

        internal static byte[] Dpapi(byte[] dados, byte[] entropia, bool proteger)
        {
            DATA_BLOB entrada = new DATA_BLOB(), extra = new DATA_BLOB(), saida = new DATA_BLOB();
            GCHandle hDados = GCHandle.Alloc(dados, GCHandleType.Pinned);
            GCHandle hExtra = GCHandle.Alloc(entropia, GCHandleType.Pinned);
            try
            {
                entrada.cbData = dados.Length; entrada.pbData = hDados.AddrOfPinnedObject();
                extra.cbData = entropia.Length; extra.pbData = hExtra.AddrOfPinnedObject();
                bool ok = proteger
                    ? CryptProtectData(ref entrada, "Mhiagos Control", ref extra, IntPtr.Zero, IntPtr.Zero, SemInterface, ref saida)
                    : CryptUnprotectData(ref entrada, IntPtr.Zero, ref extra, IntPtr.Zero, IntPtr.Zero, SemInterface, ref saida);
                if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                byte[] r = new byte[saida.cbData];
                Marshal.Copy(saida.pbData, r, 0, saida.cbData);
                return r;
            }
            finally
            {
                if (saida.pbData != IntPtr.Zero) LocalFree(saida.pbData);
                hDados.Free();
                hExtra.Free();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DATA_BLOB dados, string descricao, ref DATA_BLOB entropia,
            IntPtr reservado, IntPtr prompt, int flags, ref DATA_BLOB saida);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB dados, IntPtr descricao, ref DATA_BLOB entropia,
            IntPtr reservado, IntPtr prompt, int flags, ref DATA_BLOB saida);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memoria);

        // ---------------- Gerenciador de Credenciais (so leitura de migracao e limpeza) ----------------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);
    }
}
