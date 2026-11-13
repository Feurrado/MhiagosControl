using System;
using System.Collections.Generic;
using System.Globalization;

namespace MhiagosControl
{
    /// <summary>
    /// O que o modelo declara de cada propriedade (get_property_list): se e
    /// gravavel e quais valores aceita. E a fonte da verdade do painel - o ar
    /// so mostra os modos e velocidades que o proprio aparelho diz ter.
    /// </summary>
    internal sealed class MetaPropriedade
    {
        public string Chave, Nome;
        public bool Grava;
        public readonly List<int> Valores = new List<int>();
        /// <summary>Faixas "16~30,61~86": a primeira em °C, a segunda em °F.</summary>
        public readonly List<int[]> Faixas = new List<int[]>();

        public static MetaPropriedade De(Dictionary<string, object> d)
        {
            MetaPropriedade m = new MetaPropriedade();
            m.Chave = Json.Texto(d, "propertyKey") ?? "";
            m.Nome = Json.Texto(d, "propertyName") ?? "";
            m.Grava = (Json.Texto(d, "propertyRW") ?? "").IndexOf('W') >= 0;
            string lista = Json.Texto(d, "propertyValueList") ?? "";
            foreach (string parte in lista.Replace('，', ',').Replace('\n', ',').Split(','))
            {
                string p = parte.Trim();
                if (p.Length == 0) continue;
                int til = p.IndexOf('~');
                if (til > 0)
                {
                    int a, b;
                    if (int.TryParse(p.Substring(0, til).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out a) &&
                        int.TryParse(p.Substring(til + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out b))
                        m.Faixas.Add(new[] { a, b });
                    continue;
                }
                // "0:reserved" ou "0" - so o numero interessa aqui.
                int dois = p.IndexOf(':');
                int v;
                if (int.TryParse((dois > 0 ? p.Substring(0, dois) : p).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                    && !m.Valores.Contains(v))
                    m.Valores.Add(v);
            }
            return m;
        }

        public static Dictionary<string, MetaPropriedade> DaResposta(Dictionary<string, object> r)
        {
            Dictionary<string, MetaPropriedade> d = new Dictionary<string, MetaPropriedade>();
            List<object> l = r != null && r.ContainsKey("properties") ? r["properties"] as List<object> : null;
            if (l == null) return d;
            foreach (object o in l)
            {
                Dictionary<string, object> p = o as Dictionary<string, object>;
                if (p == null) continue;
                MetaPropriedade m = De(p);
                if (m.Chave.Length > 0) d[m.Chave] = m;
            }
            return d;
        }
    }

    /// <summary>
    /// Nomes dos valores de modo e de ventilador. ISOLADO AQUI de proposito:
    /// a nuvem diz QUAIS valores o modelo aceita, mas nao o que cada um
    /// significa. Os de modo 0 a 4 sao os do dicionario do tipo 009 (split) da
    /// integracao do Home Assistant; o que nao estiver confirmado aparece com o
    /// numero, sem inventar significado.
    ///
    /// Ar de janela do Thiago (tipo 008, feature 301): modos 0,2,3,5 e
    /// ventilador 2,3,4. Calibrado com ele em 26/10/2026: 0 Ventilar, 2 Frio,
    /// 3 Desumidificar e 5 ECO - os mesmos do 009 mais o ECO -; ventilador 2
    /// baixa, 3 media, 4 alta.
    /// </summary>
    internal static class TabelaDoAr
    {
        public static string Modo(int v, string tipo)
        {
            switch (v)
            {
                case 0: return T.CasaVentilar;
                case 1: return T.CasaCalor;
                case 2: return T.CasaFrio;
                case 3: return T.CasaSeco;
                case 4: return T.CasaAuto;
                case 5: return T.CasaEco;
                default: return string.Format(T.CasaModoN, v);
            }
        }

        /// <summary>Velocidades do tipo 009; as demais vao pelo numero.</summary>
        public static string Ventilador(int v, string tipo)
        {
            if (tipo == "009")
                switch (v)
                {
                    case 0: return "Auto";
                    case 5: return T.CasaVentoMinimo;
                    case 6: return T.CasaVentoBaixo;
                    case 7: return T.CasaVentoMedio;
                    case 8: return T.CasaVentoAlto;
                    case 9: return T.CasaVentoMaximo;
                }
            if (tipo == "008")
                switch (v)
                {
                    case 2: return T.CasaVentoBaixo;
                    case 3: return T.CasaVentoMedio;
                    case 4: return T.CasaVentoAlto;
                }
            if (v == 0) return "Auto";
            return string.Format(T.CasaVelocidadeN, v);
        }
    }
}
