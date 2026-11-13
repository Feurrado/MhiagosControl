using System;

namespace MhiagosControl
{
    /// <summary>Interface nova (WebView2): a janela so confia na origem do servidor da casa. Nao abre o WebView2.</summary>
    public static partial class Tests
    {
        private static void JanelaWebOrigem()
        {
            Igual("https://home01.exemplo.ts.net", JanelaWeb.Origem("https://home01.exemplo.ts.net/#painel"), "interface nova: origem sem caminho");
            Igual("http://192.168.1.100:8765", JanelaWeb.Origem("192.168.1.100:8765"), "interface nova: sem esquema vale http");
            Igual("", JanelaWeb.Origem("file:///C:/Windows/win.ini"), "interface nova: file nao e origem");
            Igual("", JanelaWeb.Origem("javascript:alert(1)"), "interface nova: javascript nao e origem");
            Igual("", JanelaWeb.Origem(""), "interface nova: vazio nao e origem");
            string o = JanelaWeb.Origem("https://home01.exemplo.ts.net");
            Verdade(JanelaWeb.MesmaOrigem("https://HOME01.exemplo.ts.net/api/saude", o), "interface nova: mesma origem, outra pagina");
            Verdade(!JanelaWeb.MesmaOrigem("https://home01.exemplo.ts.net.evil.com/", o), "interface nova: dominio parecido e outra origem");
            Verdade(!JanelaWeb.MesmaOrigem("http://home01.exemplo.ts.net/", o), "interface nova: http no lugar de https e outra origem");
            Verdade(!JanelaWeb.MesmaOrigem("https://home01.exemplo.ts.net:8443/", o), "interface nova: outra porta e outra origem");
            Verdade(!JanelaWeb.MesmaOrigem("https://ntfy.sh/", o), "interface nova: outro site abre fora");
            Verdade(!JanelaWeb.MesmaOrigem("https://home01.exemplo.ts.net/", ""), "interface nova: sem origem nada e confiavel");
            string s = JanelaWeb.Script(o, "cha\"ve</script>");
            Verdade(s.Contains("location.origin===\"https://home01.exemplo.ts.net\""), "interface nova: a chave so vai para a origem do servidor");
            Verdade(s.Contains("cha\\\"ve") && !s.Contains("cha\"ve"), "interface nova: a chave entra escapada no script");
        }
    }
}
