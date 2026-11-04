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
                fora.Add(new KeyValuePair<string, AmostraAr>("A", Amostra(DateTime.Now.AddDays(-40), true)));   // velha demais
                Igual(2, HistoricoDoAr.Mesclar(fora), "servidor: entram so as amostras sem par a menos de 25 s");
                List<AmostraAr> l = HistoricoDoAr.Amostras("A", t0.AddMinutes(-1));
                Igual(4, l.Count, "servidor: quatro amostras depois da juncao");
                Verdade(l[1].Quando == t0.AddMinutes(5) && l[3].Quando == t0.AddMinutes(20), "servidor: amostras em ordem de tempo");
                HistoricoDoAr.Limpar();
                HistoricoDoAr.Carregar();
                Igual(4, HistoricoDoAr.Amostras("A", t0.AddMinutes(-1)).Count, "servidor: juncao regravada no arquivo");
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

        private static CiclosDaLavadora.Ciclo Ciclo(DateTime inicio, int minutos, int programa, double agua)
        {
            CiclosDaLavadora.Ciclo c = new CiclosDaLavadora.Ciclo();
            c.Inicio = inicio; c.Fim = inicio.AddMinutes(minutos); c.Programa = programa; c.Agua = agua; c.Concluido = true;
            return c;
        }
    }
}
