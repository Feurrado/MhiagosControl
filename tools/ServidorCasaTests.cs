using System;
using System.Collections.Generic;
using System.IO;

namespace MhiagosControl
{
    /// <summary>
    /// Servidor da casa: a juncao do historico e dos ciclos que vem dele. Nada
    /// aqui fala com o servidor (ServidorCasa.Desligado).
    /// </summary>
    public static partial class Tests
    {
        private static void CasaServidor()
        {
            ServidorCasa.Desligado = true;
            string pasta = Path.Combine(Path.GetTempPath(), "mhiagos-servidor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pasta);
            string histAntes = HistoricoDoAr.Arquivo, cicAntes = CiclosDaLavadora.Arquivo;
            HistoricoDoAr.Arquivo = Path.Combine(pasta, "hist.txt");
            CiclosDaLavadora.Arquivo = Path.Combine(pasta, "ciclos.txt");
            try
            {
                HistoricoDoAr.Limpar();
                DateTime t0 = DateTime.Now.AddHours(-2);
                t0 = new DateTime(t0.Year, t0.Month, t0.Day, t0.Hour, t0.Minute, t0.Second);
                HistoricoDoAr.Acrescentar("A", Amostra(t0, true));
                HistoricoDoAr.Acrescentar("A", Amostra(t0.AddMinutes(10), true));

                List<KeyValuePair<string, AmostraAr>> fora = new List<KeyValuePair<string, AmostraAr>>();
                fora.Add(new KeyValuePair<string, AmostraAr>("A", Amostra(t0.AddSeconds(10), false)));   // mesma leitura: fica fora
                fora.Add(new KeyValuePair<string, AmostraAr>("A", Amostra(t0.AddMinutes(5), true)));     // lacuna do PC: entra no meio
                fora.Add(new KeyValuePair<string, AmostraAr>("A", Amostra(t0.AddMinutes(20), true)));    // depois: entra no fim
                fora.Add(new KeyValuePair<string, AmostraAr>("A", Amostra(DateTime.Now.AddDays(-70), true)));   // velha demais
                Igual(2, HistoricoDoAr.Mesclar(fora), "servidor: entram so as amostras sem par a menos de 25 s");
                List<AmostraAr> l = HistoricoDoAr.Amostras("A", t0.AddMinutes(-1));
                Igual(4, l.Count, "servidor: quatro amostras depois da juncao");
                Verdade(l[1].Quando == t0.AddMinutes(5) && l[3].Quando == t0.AddMinutes(20), "servidor: amostras em ordem de tempo");
                HistoricoDoAr.Limpar();
                HistoricoDoAr.Carregar();
                Igual(4, HistoricoDoAr.Amostras("A", t0.AddMinutes(-1)).Count, "servidor: juncao regravada no arquivo");

                // So no fim (aqui, outro ar): acrescenta ao arquivo em vez de regravar tudo.
                long antesBytes = new FileInfo(HistoricoDoAr.Arquivo).Length;
                List<KeyValuePair<string, AmostraAr>> fim = new List<KeyValuePair<string, AmostraAr>>();
                fim.Add(new KeyValuePair<string, AmostraAr>("B", Amostra(t0.AddMinutes(1), true)));
                Igual(1, HistoricoDoAr.Mesclar(fim), "servidor: amostra depois da ultima entra");
                Verdade(new FileInfo(HistoricoDoAr.Arquivo).Length > antesBytes, "servidor: amostra do fim acrescentada ao arquivo");
                HistoricoDoAr.Limpar();
                HistoricoDoAr.Carregar();
                Verdade(HistoricoDoAr.Amostras("B", t0.AddMinutes(-1)).Count == 1 && HistoricoDoAr.Amostras("A", t0.AddMinutes(-1)).Count == 4,
                    "servidor: acrescimo relido sem perder as outras");
                Igual(2, HistoricoDoAr.Desde(t0.AddMinutes(6)).Count, "servidor: amostras para enviar desde a data");

                CiclosDaLavadora.Zerar();
                List<KeyValuePair<string, CiclosDaLavadora.Ciclo>> cf = new List<KeyValuePair<string, CiclosDaLavadora.Ciclo>>();
                cf.Add(new KeyValuePair<string, CiclosDaLavadora.Ciclo>("L", Ciclo(t0, 60, 7, 40)));
                cf.Add(new KeyValuePair<string, CiclosDaLavadora.Ciclo>("L", Ciclo(t0.AddHours(-30), 90, 3, 55)));
                Igual(2, CiclosDaLavadora.Mesclar(cf), "servidor: dois ciclos novos");
                Igual(0, CiclosDaLavadora.Mesclar(cf), "servidor: o mesmo ciclo de novo nao duplica");

                // O PC ligou no meio do ciclo e viu so o fim: e o mesmo ciclo, fica o inicio do servidor.
                List<KeyValuePair<string, CiclosDaLavadora.Ciclo>> pc = new List<KeyValuePair<string, CiclosDaLavadora.Ciclo>>();
                pc.Add(new KeyValuePair<string, CiclosDaLavadora.Ciclo>("L", Ciclo(t0.AddMinutes(30), 32, 7, 42)));
                Igual(0, CiclosDaLavadora.Mesclar(pc), "servidor: ciclo sobreposto junta, nao soma");
                List<CiclosDaLavadora.Ciclo> u = CiclosDaLavadora.Ultimos("L", 10);
                Igual(2, u.Count, "servidor: continuam dois ciclos");
                Verdade(u[0].Inicio == t0 && u[0].Fim == t0.AddMinutes(62) && Math.Abs(u[0].Agua - 42) < 0.01,
                    "servidor: ciclo junto vai do inicio mais cedo ao fim mais tarde");
                Verdade(u[1].Inicio == t0.AddHours(-30), "servidor: ciclos em ordem");
                Igual(1, CiclosDaLavadora.TerminadosDesde(t0.AddMinutes(1)).Count, "servidor: ciclos para enviar desde a data");

                Igual(2, File.ReadAllLines(CiclosDaLavadora.Arquivo).Length, "servidor: arquivo de ciclos regravado com dois");

                // Preferencias: o que sobe para o servidor volta igual.
                PreferenciasCasa p = new PreferenciasCasa();
                p.PotenciaFrio = 950; p.Tarifa = 0.91; p.RetornoTemp = 23; p.AlertaQuente = false;
                p.Cenas[0].Nome = "Noite"; p.Cenas[0].Duracao = 60;
                string json = Json.Escrever(ServidorCasa.Compartilhadas(p));
                PreferenciasCasa q = new PreferenciasCasa();
                ServidorCasa.Aplicar(q, (Dictionary<string, object>)Json.Ler(json));
                Igual(json, Json.Escrever(ServidorCasa.Compartilhadas(q)), "servidor: preferencias voltam iguais do servidor");
                Verdade(q.PotenciaFrio == 950 && Math.Abs(q.Tarifa - 0.91) < 1e-6 && !q.AlertaQuente && q.Cenas[0].Nome == "Noite" && q.Cenas[0].Duracao == 60,
                    "servidor: campos das preferencias aplicados");
                Dictionary<string, object> estranho = new Dictionary<string, object>();
                estranho["limiteQuente"] = 99L; estranho["cenas"] = new List<object> { new Dictionary<string, object> { { "nome", "" } } };
                ServidorCasa.Aplicar(q, estranho);
                Verdade(q.LimiteQuente == 45 && q.Cenas.Count == 0, "servidor: valores fora da faixa limitados e cena sem nome ignorada");

                // Custo do mes: lavadora pela energia dos ciclos, mes atual x anterior ate o mesmo dia.
                CiclosDaLavadora.Zerar();
                DateTime agora = new DateTime(2026, 11, 4, 12, 0, 0);
                List<KeyValuePair<string, CiclosDaLavadora.Ciclo>> mes = new List<KeyValuePair<string, CiclosDaLavadora.Ciclo>>();
                mes.Add(new KeyValuePair<string, CiclosDaLavadora.Ciclo>("L", CicloE(new DateTime(2026, 11, 2, 9, 0, 0), 7, 2.0)));
                mes.Add(new KeyValuePair<string, CiclosDaLavadora.Ciclo>("L", CicloE(new DateTime(2026, 10, 3, 9, 0, 0), 8, 1.0)));
                mes.Add(new KeyValuePair<string, CiclosDaLavadora.Ciclo>("L", CicloE(new DateTime(2026, 10, 20, 9, 0, 0), 8, 3.0)));
                mes.Add(new KeyValuePair<string, CiclosDaLavadora.Ciclo>("L", CicloE(new DateTime(2026, 10, 25, 9, 0, 0), 6, 0.5)));
                CiclosDaLavadora.Mesclar(mes);
                Aparelho lav = new Aparelho(); lav.Puid = "L"; lav.Tipo = "025"; lav.Apelido = "Josefina";
                PreferenciasCasa pm = new PreferenciasCasa(); pm.Tarifa = 1.0;
                ConsumoDoPeriodo.Resultado rm = ConsumoDoPeriodo.Calcular(new List<Aparelho> { lav }, ConsumoDoPeriodo.Periodo.Mes, agora, pm);
                Verdade(Math.Abs(rm.Custo - 2.0) < 1e-6 && rm.Aparelhos[0].Ciclos == 1 && rm.NumDias == 4, "consumo: este mes so com o ciclo de novembro, 4 dias");
                Verdade(Math.Abs(rm.CustoAnterior - 1.0) < 1e-6, "consumo: outubro ate o dia 4");
                Verdade(Math.Abs(rm.Variacao.Value - 100) < 1e-6, "consumo: variacao contra o mesmo dia do mes anterior");
                ConsumoDoPeriodo.Resultado r30 = ConsumoDoPeriodo.Calcular(new List<Aparelho> { lav }, ConsumoDoPeriodo.Periodo.Dias30, agora, pm);
                Verdade(r30.NumDias == 30 && r30.Aparelhos[0].Ciclos == 3 && Math.Abs(r30.Custo - 5.5) < 1e-6 && Math.Abs(r30.CustoAnterior - 1.0) < 1e-6,
                    "consumo: 30 dias (06/10 a 04/11) contra os 30 anteriores");
                Verdade(Math.Abs(r30.Aparelhos[0].Dias[r30.NumDias - 3].Kwh - 2.0) < 1e-6, "consumo: energia no dia do ciclo");

                // Estatisticas: medias so dos concluidos, mais usado e ciclos desde a limpeza do tambor.
                List<CiclosDaLavadora.Ciclo> todos = CiclosDaLavadora.Todos("L");
                List<EstatisticasDaLavadora.PorPrograma> rk = EstatisticasDaLavadora.Ranking(todos);
                Verdade(rk.Count == 3 && rk[0].Programa == 8 && rk[0].Ciclos == 2 && Math.Abs(rk[0].Energia / rk[0].Ciclos - 2.0) < 1e-6,
                    "lavadora: Sintetico e o mais usado, com a media de energia");
                DateTime? ult;
                Igual(1, EstatisticasDaLavadora.DesdeALimpeza(todos, out ult), "lavadora: um ciclo depois da limpeza do tambor");
                Verdade(ult.HasValue && ult.Value.Day == 25, "lavadora: data da ultima limpeza");
            }
            finally
            {
                HistoricoDoAr.Arquivo = histAntes; CiclosDaLavadora.Arquivo = cicAntes;
                HistoricoDoAr.Limpar();
                CiclosDaLavadora.Zerar();
                try { Directory.Delete(pasta, true); } catch (IOException) { }
            }
        }

        private static AmostraAr Amostra(DateTime q, bool ligado)
        {
            AmostraAr a = new AmostraAr();
            a.Quando = q; a.Ambiente = 27; a.Alvo = 24; a.Ligado = ligado; a.Modo = 2;
            return a;
        }

        private static CiclosDaLavadora.Ciclo CicloE(DateTime inicio, int programa, double kwh)
        {
            CiclosDaLavadora.Ciclo c = Ciclo(inicio, 60, programa, 50);
            c.Energia = kwh;
            return c;
        }

        private static CiclosDaLavadora.Ciclo Ciclo(DateTime inicio, int minutos, int programa, double agua)
        {
            CiclosDaLavadora.Ciclo c = new CiclosDaLavadora.Ciclo();
            c.Inicio = inicio; c.Fim = inicio.AddMinutes(minutos); c.Programa = programa; c.Agua = agua; c.Concluido = true;
            return c;
        }
    }
}
