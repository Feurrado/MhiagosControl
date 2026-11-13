using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace MhiagosControl
{
    /// <summary>
    /// Casos compartilhados com o servidor da casa: servidor\tests\casos-tabelas.txt
    /// (embutido aqui como recurso) e lido tambem por servidor/tests/test_casos.py.
    /// As tabelas da Casa existem em C# e em Python; uma que mude so de um lado
    /// falha aqui ou la.
    /// </summary>
    public static partial class Tests
    {
        private static void CasosCompartilhados()
        {
            string antes = T.Language;
            int n = 0;
            try
            {
                T.Language = T.PtBr;
                Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MhiagosControl.casos-tabelas.txt");
                Verdade(s != null, "casos compartilhados: o arquivo de casos esta embutido");
                if (s == null) return;
                using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                {
                    string linha;
                    while ((linha = r.ReadLine()) != null)
                    {
                        if (linha.Length == 0 || linha[0] == '#') continue;
                        string[] c = linha.Split(new[] { '|' }, 3);
                        if (c.Length < 3) { Verdade(false, "casos compartilhados: linha invalida: " + linha); continue; }
                        string[] a = c[1].Split(',');
                        Igual(c[2], CasoCompartilhado(c[0], a), "casos compartilhados: " + c[0] + "(" + c[1] + ")");
                        n++;
                    }
                }
                Verdade(n > 100, "casos compartilhados: mais de 100 casos lidos");
            }
            finally { T.Language = antes; }
        }

        private static string CasoCompartilhado(string nome, string[] a)
        {
            int v = int.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture);
            switch (nome)
            {
                case "programa": return TabelaDaLavadora.Programa(v);
                case "modo": return TabelaDoAr.Modo(v, "009");
                case "ventilador": return TabelaDoAr.Ventilador(v, a[1]);
                case "fase": return TabelaDaLavadora.Fase(v);
                case "porta": return TabelaDaLavadora.Porta(v);
                case "temperatura": return TabelaDaLavadora.Temperatura(v);
                case "centrifugacao": return TabelaDaLavadora.Centrifugacao(v);
                case "secagem": return TabelaDaLavadora.Secagem(v);
                case "sujeira": return TabelaDaLavadora.Sujeira(v);
                default: return "funcao desconhecida: " + nome;
            }
        }
    }
}
