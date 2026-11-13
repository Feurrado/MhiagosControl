using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MhiagosControl
{
    /// <summary>
    /// Contrato de /api/aparelhos com o servidor da casa (docs\CONTRATO-CASA.md):
    /// servidor\tests\contrato-aparelhos.json, embutido aqui, e conferido tambem por
    /// servidor/tests/test_contrato.py. Nada aqui fala com o servidor.
    /// </summary>
    public static partial class Tests
    {
        private static Dictionary<string, object> AmostraDoContrato()
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MhiagosControl.contrato-aparelhos.json");
            if (s == null) return null;
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                return Json.Ler(r.ReadToEnd()) as Dictionary<string, object>;
        }

        private static bool ContratoRecusa(Dictionary<string, object> r)
        {
            try { ServidorCasa.AparelhosDe(r); return false; }
            catch (IOException) { return true; }
        }

        private static void ContratoCasa()
        {
            Dictionary<string, object> r = AmostraDoContrato();
            Verdade(r != null, "contrato: a amostra esta embutida");
            if (r == null) return;

            List<Aparelho> l = ServidorCasa.AparelhosDe(r);
            Igual(2, l.Count, "contrato: dois aparelhos na amostra");
            Aparelho ar = l[0], lav = l[1];
            Igual("p-ar", ar.Puid, "contrato: puid");
            Igual("Gil do Frio", ar.Nome, "contrato: apelido vira o nome");
            Igual("Quarto", ar.Comodo, "contrato: comodo");
            Verdade(ar.ArCondicionado && ar.Online, "contrato: ar de janela conectado");
            Igual("23", ar.Estado["t_temp"], "contrato: estado como texto");
            Igual(5, ar.Estado.Count, "contrato: todas as propriedades do estado");
            Verdade(lav.Lavadora && !lav.Online, "contrato: lavadora desconectada");
            Igual("WD", lav.Nome, "contrato: sem apelido vale o modelo");
            Igual("1wj110009v0t", lav.CodigoDoModelo, "contrato: codigo do modelo");

            // O que nao serve como fonte: o app cai para a nuvem direta.
            r["contrato"] = 0L;
            Verdade(ContratoRecusa(r), "contrato: servidor antigo nao e fonte");
            r = AmostraDoContrato();
            r.Remove("contrato");
            Verdade(ContratoRecusa(r), "contrato: sem o campo contrato nao e fonte");
            r = AmostraDoContrato();
            r["logado"] = false;
            Verdade(ContratoRecusa(r), "contrato: servidor sem conta nao e fonte");
            r = AmostraDoContrato();
            r["ultimaLeitura"] = (long)1794600000 - ServidorCasa.FonteVelhaSeg - 1;
            Verdade(ContratoRecusa(r), "contrato: leitura parada ha mais de 5 min nao e fonte");
            r = AmostraDoContrato();
            r["ultimaLeitura"] = 0L;
            Verdade(ContratoRecusa(r), "contrato: servidor que nunca leu nao e fonte");
            Verdade(ContratoRecusa(null), "contrato: resposta vazia nao e fonte");

            // O comando do PC: tudo num pedido, sem marcar como manual.
            Dictionary<string, string> props = new Dictionary<string, string>();
            props["t_temp"] = "24";
            props["t_power"] = "1";
            Dictionary<string, object> carga = (Dictionary<string, object>)Json.Ler(Json.Escrever(ServidorCasa.CargaDoComando(props, true)));
            Igual("false", Json.Texto(carga, "manual"), "contrato: comando do PC nao e manual");
            Igual("true", Json.Texto(carga, "semBipe"), "contrato: sem bipe vai no pedido");
            Dictionary<string, object> p = carga["props"] as Dictionary<string, object>;
            Verdade(p != null && p.Count == 2 && Json.Texto(p, "t_temp") == "24", "contrato: propriedades como texto");

            // Sem servidor configurado (os testes o desligam) nunca e fonte.
            Verdade(!ServidorCasa.Fonte, "contrato: sem servidor a fonte e a nuvem");

            // A preferencia vai e volta do casa.ini.
            PreferenciasCasa pref = new PreferenciasCasa();
            Verdade(pref.ServidorFonte, "contrato: servidor como fonte vem ligado");
        }
    }
}
