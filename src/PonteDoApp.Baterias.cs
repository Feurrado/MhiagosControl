using System;
using System.Collections.Generic;

namespace MhiagosControl
{
    /// <summary>
    /// A pagina Baterias da interface nova: o que a classica calcula por
    /// aparelho (selo, quatro blocos, quatro detalhes, a curva de 48 h),
    /// com os mesmos textos, porque eles saem do mesmo codigo.
    /// </summary>
    internal sealed partial class PonteDoApp
    {
        private const double JanelaDaCurvaEmHoras = 48;

        private static List<object> Baterias()
        {
            List<object> saida = new List<object>();
            Periferico[] vivos = Perifericos.Atual;
            List<AnaliseDeBateria> analises;
            try { analises = HistoricoDeBateria.Analisar(vivos); }
            catch (Exception ex) { Log.Error("interface nova: baterias", ex); return saida; }
            DateTime agora = DateTime.Now, inicio = agora.AddHours(-JanelaDaCurvaEmHoras);

            foreach (AnaliseDeBateria a in analises)
            {
                try
                {
                    int? autonomia = null;
                    foreach (Periferico p in vivos)
                        if (p != null && HistoricoDeBateria.Chave(p) == a.Chave) autonomia = p.Autonomia;
                    string selo; string[][] blocos, detalhes;
                    CartaoDeBateria.Resumo(a, autonomia, out selo, out blocos, out detalhes);

                    Periferico atual = a.PctAtual.HasValue
                        ? Periferico.Lido(a.Nome, a.Tipo, a.Fonte, a.PctAtual.Value, a.Carregando)
                        : Periferico.Sem(a.Nome, a.Tipo, a.Fonte, EstadoPeriferico.Desligado);

                    Dictionary<string, object> d = D();
                    d["chave"] = a.Chave ?? "";
                    d["nome"] = Perifericos.NomeCompleto(a.Nome, a.Fonte) ?? "";
                    d["pct"] = a.PctAtual.HasValue ? (object)a.PctAtual.Value : null;
                    d["carregando"] = a.Carregando && a.PctAtual.HasValue;
                    d["selo"] = selo;
                    d["imagem"] = a.Fonte == FontePeriferico.Bluetooth ? "bluetooth.png"
                        : Perifericos.EhProX(atual) ? "prox.png"
                        : a.Tipo == TipoPeriferico.Headset ? "g325.png"
                        : a.Tipo == TipoPeriferico.Mouse ? "x11.png" : "";
                    d["aberto"] = DetalhesDaBateria.Aberto(a.Chave);
                    d["blocos"] = Linhas(blocos);
                    d["detalhes"] = Linhas(detalhes);

                    // A curva: minutos atras, carga e se estava no cabo.
                    List<object> pontos = new List<object>();
                    AmostraDeBateria[] am = a.Amostras ?? new AmostraDeBateria[0];
                    foreach (AmostraDeBateria x in am)
                    {
                        if (x.Quando < inicio) continue;
                        List<object> ponto = new List<object>();
                        ponto.Add(Math.Round((agora - x.Quando).TotalMinutes, 1));
                        ponto.Add(x.Pct);
                        ponto.Add(x.Carregando ? 1 : 0);
                        pontos.Add(ponto);
                    }
                    d["pontos"] = pontos;
                    saida.Add(d);
                }
                catch (Exception ex) { Log.Error("interface nova: bateria " + a.Nome, ex); }
            }
            return saida;
        }

        private static List<object> Linhas(string[][] linhas)
        {
            List<object> r = new List<object>();
            foreach (string[] l in linhas)
            {
                List<object> x = new List<object>();
                for (int i = 0; i < 3; i++) x.Add(l != null && i < l.Length ? l[i] ?? "" : "");
                r.Add(x);
            }
            return r;
        }
    }
}
