using System;
using System.IO;
using System.Text;

namespace MhiagosControl
{
    /// <summary>
    /// Grava ao lado e so entao troca: uma queda no meio deixa o arquivo
    /// anterior inteiro, e nunca um pela metade.
    ///
    /// Cada arquivo do aplicativo tinha a sua copia disto, com detalhes
    /// diferentes - nome temporario fixo em uns, unico em outros, e o estado
    /// "Mais detalhes" gravado direto, sem troca nenhuma. Nome fixo deixava
    /// duas gravacoes simultaneas disputarem o mesmo .tmp. Aqui o temporario
    /// e unico e e apagado se algo falhar.
    /// </summary>
    internal static class GravacaoAtomica
    {
        public static void Escrever(string caminho, string texto, Encoding codificacao)
        {
            Escrever(caminho, delegate(Stream s)
            {
                byte[] b = codificacao.GetBytes(texto);
                byte[] bom = codificacao.GetPreamble();
                s.Write(bom, 0, bom.Length);
                s.Write(b, 0, b.Length);
            });
        }

        public static void Escrever(string caminho, Action<Stream> conteudo)
        {
            string pasta = Path.GetDirectoryName(Path.GetFullPath(caminho));
            if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
            string temp = caminho + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream fs = File.Create(temp)) conteudo(fs);
                if (File.Exists(caminho)) File.Replace(temp, caminho, null);
                else File.Move(temp, caminho);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) { Log.Error("descarte de temporario", ex); }
            }
        }
    }
}
