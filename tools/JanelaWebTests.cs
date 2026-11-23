using System;

namespace MhiagosControl
{
    /// <summary>Interface nova (WebView2): a janela so confia na origem do servidor da casa. Nao abre o WebView2.</summary>
    public static partial class Tests
    {
        private static void ProporcaoDaJanelaTeste()
        {
            // moldura de 16 x 39 sobre o cliente; razao 1,5
            int[] r = ProporcaoDaJanela.Ajustar(0, 0, 916, 639, 2, 16, 39, 1.5);        // arrastou a borda direita: 900 de cliente
            Igual("0,0,916,639", r[0] + "," + r[1] + "," + r[2] + "," + r[3], "proporcao: borda direita mantem a razao (900x600)");
            r = ProporcaoDaJanela.Ajustar(0, 0, 1000, 639, 2, 16, 39, 1.5);             // 984 de largura de cliente -> 656 de altura
            Igual(695, r[3], "proporcao: a altura acompanha a largura");
            r = ProporcaoDaJanela.Ajustar(0, 0, 1000, 639, 6, 16, 39, 1.5);             // arrastou a base: a largura acompanha a altura
            Igual(916, r[2], "proporcao: a largura acompanha a altura");
            r = ProporcaoDaJanela.Ajustar(0, 100, 1000, 700, 4, 16, 39, 1.5);           // canto de cima: o topo anda
            Igual(700, r[3], "proporcao: canto de cima nao mexe na base");
            Verdade(r[1] != 100, "proporcao: canto de cima move o topo");
            Igual(1.0, ProporcaoDaJanela.Limitar(0.2), "proporcao: piso");
            Igual(1.8, ProporcaoDaJanela.Limitar(9), "proporcao: teto");
            Igual(ProporcaoDaJanela.Padrao, ProporcaoDaJanela.Limitar(double.NaN), "proporcao: NaN vira o padrao");
            Igual(1.0, JanelaDoApp.ZoomDaCasa(1132, 0), "casa: na largura de projeto a escala e 1");
            Igual(0.8, JanelaDoApp.ZoomDaCasa(906, 0), "casa: area menor escala para baixo (arredonda para baixo)");
            Igual(1.0, JanelaDoApp.ZoomDaCasa(10, 0), "casa: area sem medida fica em 1");
            Igual(1.15, JanelaDoApp.ZoomDaCasa(2400, 1.15), "casa: tela cheia larga para no teto da Visao geral");
            Igual(0.8, JanelaDoApp.ZoomDaCasa(906, 1.15), "casa: o teto nao muda a escala de janela estreita");
        }

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
