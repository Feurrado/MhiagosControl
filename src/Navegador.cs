using System;
using System.Diagnostics;
using System.IO;

namespace MhiagosControl
{
    /// <summary>
    /// Enderecos abertos ou pedidos pelo app, que roda como administrador.
    ///
    /// Process.Start(url) num processo elevado abria o navegador elevado
    /// tambem (qualquer falha de pagina viraria administrador), e com o
    /// endereco do servidor da casa lido do casa.ini - gravavel sem elevacao -
    /// um "file:" ou outro esquema ia direto ao ShellExecute. Agora so http e
    /// https com host, e a abertura passa pelo explorer.exe, que entrega o
    /// endereco ao shell da sessao, sem elevacao (o mesmo caminho dos
    /// perifericos).
    /// </summary>
    internal static class Navegador
    {
        /// <summary>So http ou https, absoluto e com host.</summary>
        internal static bool Seguro(string endereco, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(endereco)) return false;
            Uri u;
            if (!Uri.TryCreate(endereco.Trim(), UriKind.Absolute, out u)) return false;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
            if (string.IsNullOrEmpty(u.Host) || u.UserInfo.Length > 0) return false;
            uri = u;
            return true;
        }

        /// <summary>Testes: recebe o endereco em vez de abrir o navegador.</summary>
        internal static Action<string> Interceptar;

        /// <summary>Abre no navegador padrao, sem a elevacao do app. Falso se o endereco foi recusado ou nao abriu.</summary>
        public static bool Abrir(string endereco)
        {
            Uri u;
            if (!Seguro(endereco, out u))
            {
                Log.Write("navegador: endereco recusado (" + (endereco ?? "") + ")");
                return false;
            }
            try
            {
                if (Interceptar != null) { Interceptar(u.AbsoluteUri); return true; }
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                Process.Start(explorer, "\"" + u.AbsoluteUri + "\"");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("abrir " + u.AbsoluteUri, ex);
                return false;
            }
        }
    }
}
