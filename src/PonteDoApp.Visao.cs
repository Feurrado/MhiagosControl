using System;
using System.Collections.Generic;
using System.Globalization;

namespace MhiagosControl
{
    /// <summary>
    /// A casa na Visao geral da interface nova, com as mesmas pecas da
    /// classica (VisaoNova.cs): o cartao do ar com - e + e o liga/desliga, o
    /// da lavadora so durante um ciclo, e o convite quando nao ha nada.
    /// </summary>
    internal sealed partial class PonteDoApp
    {
        private static Dictionary<string, object> VisaoDaCasa()
        {
            Dictionary<string, object> r = D();
            Aparelho ar = null, lav = null;
            if (ServicoCasa.Logado)
                foreach (Aparelho a in ServicoCasa.Aparelhos)
                {
                    if (a == null) continue;
                    if (ar == null && a.ArCondicionado) ar = a;
                    if (lav == null && a.Lavadora) lav = a;
                }

            if (ar != null)
            {
                Dictionary<string, object> x = D();
                bool on = ar.Online, ligado = ar.Int("t_power", 0) != 0;
                int min, max;
                CenaCasa.FaixaC(ar, out min, out max);
                if (ar.Int("t_temp_type", 0) == 1) { min = (int)Math.Round(min * 9.0 / 5 + 32); max = (int)Math.Round(max * 9.0 / 5 + 32); }
                x["puid"] = ar.Puid ?? "";
                x["nome"] = ar.Nome ?? "";
                x["online"] = on;
                x["ligado"] = on && ligado;
                x["alvo"] = ar.Tem("t_temp") ? (object)ar.Int("t_temp", 0) : null;
                x["min"] = min; x["max"] = max;
                x["ajusta"] = on && ligado && ar.Tem("t_temp");
                x["liga"] = !on ? T.CasaOffline : ligado ? T.CasaLigado : T.CasaDesligado;
                x["fora"] = T.ClimaLaFora;
                x["clima"] = Clima.Texto();
                x["origem"] = Clima.Origem();
                x["sug"] = SugestaoCasa.Atual();
                r["ar"] = x;
            }
            else r["ar"] = null;

            CicloAtual c = lav != null ? CicloAtual.De(lav) : null;
            bool temCiclo = c != null && c.Online && c.Rodando;
            if (temCiclo)
            {
                Dictionary<string, object> x = D();
                List<string> l = new List<string>();
                if (c.Programa > 0) l.Add(TabelaDaLavadora.Programa(c.Programa));
                l.Add(c.Titulo());
                if (lav.Tem("temperature")) l.Add(TabelaDaLavadora.Temperatura(lav.Int("temperature", 0)));
                if (lav.Tem("Spin_speed_rpm")) l.Add(TabelaDaLavadora.Centrifugacao(lav.Int("Spin_speed_rpm", 0)));
                List<string> etapas = new List<string>();
                int atual = EtapasDoCiclo.Montar(lav, c, etapas);
                float prog = c.Progresso;
                if (c.Concluido) prog = 1f;
                else if (atual >= 0 && etapas.Count > 0) prog = Math.Max(prog, (atual + 0.5f) / etapas.Count);
                x["nome"] = lav.Nome ?? "";
                x["linha"] = string.Join(" · ", l.ToArray());
                x["erro"] = c.Erro;
                x["tempo"] = CartaoDaLavadoraVisao.Relogio(c.Restante);
                x["fim"] = T.VisaoFim(Visao.FimDoCiclo(c).ToString("HH:mm"));
                x["prog"] = (double)Math.Max(0f, Math.Min(1f, prog));
                x["pausada"] = c.Pausada;
                List<object> et = new List<object>();
                foreach (string s in etapas) et.Add(s);
                x["etapas"] = et;
                x["atual"] = atual;
                r["lav"] = x;
            }
            else r["lav"] = null;

            if (ar == null && !temCiclo && (!ServicoCasa.Logado || ServicoCasa.Aparelhos.Count == 0))
            {
                Dictionary<string, object> x = D();
                x["titulo"] = ServicoCasa.Logado ? T.NavCasa : T.VisaoConecteCasa;
                x["texto"] = !ServicoCasa.Logado ? T.CasaResumoSemConta : ServicoCasa.Buscando ? T.CasaCarregando : T.CasaSemAparelhos;
                r["convite"] = x;
            }
            else r["convite"] = null;
            return r;
        }

        /// <summary>O botao Aplicar da sugestao: o servidor refaz a sugestao e manda ao ar; o cartao volta com o estado novo.</summary>
        private void AplicarSugestaoDoAr(object id)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, object> resposta;
                try
                {
                    SugestaoCasa.Aplicar();
                    resposta = Feito(id, VisaoDaCasa());
                }
                catch (Exception ex)
                {
                    Log.Error("interface nova: aplicar sugestao", ex);
                    resposta = Falha(id, T.CasaFalhaInesperada);
                }
                Action<Dictionary<string, object>> enviar = Enviar;
                if (enviar != null) enviar(resposta);
            });
        }

        /// <summary>O - e + e o liga/desliga do cartao do ar, como na classica: o estado muda na hora e o comando segue.</summary>
        private Dictionary<string, object> ComandoDoAr(object id, Dictionary<string, object> a)
        {
            string puid = Texto(a, "puid"), chave = Texto(a, "chave");
            if (chave != "t_power" && chave != "t_temp") return Falha(id, T.NovoValorInvalido);
            Aparelho ar = null;
            foreach (Aparelho x in ServicoCasa.Aparelhos) if (x != null && x.Puid == puid && x.ArCondicionado) ar = x;
            if (ar == null || !ar.Online) return Falha(id, T.CasaOffline);
            int valor = Int(a, "valor", -1);
            if (chave == "t_power") valor = valor != 0 ? 1 : 0;
            else
            {
                int min, max;
                CenaCasa.FaixaC(ar, out min, out max);
                if (ar.Int("t_temp_type", 0) == 1) { min = (int)Math.Round(min * 9.0 / 5 + 32); max = (int)Math.Round(max * 9.0 / 5 + 32); }
                valor = Math.Max(min, Math.Min(max, valor));
            }
            Dictionary<string, string> p = new Dictionary<string, string>();
            p[chave] = valor.ToString(CultureInfo.InvariantCulture);
            ar.Estado[chave] = p[chave];
            if (ServicoCasa.RetornoPuid == ar.Puid) ServicoCasa.CancelarRetorno();
            ServicoCasa.Enviar(ar.Puid, p, null);
            return Feito(id, VisaoDaCasa());
        }
    }
}
