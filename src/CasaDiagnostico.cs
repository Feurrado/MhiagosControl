using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>Uma falha da lista oficial do autodiagnostico (basic/self_check/info).</summary>
    internal sealed class FalhaOficial
    {
        public string Chave, ValorDeFalha, Nome, Categoria, Orientacao;
    }

    /// <summary>
    /// A lista oficial de falhas do modelo, consultada uma vez por hora por
    /// aparelho. O estado de cada falha agora vem das f_e_* da leitura comum.
    /// </summary>
    internal static class Diagnostico
    {
        private static readonly object _trava = new object();
        private static readonly Dictionary<string, List<FalhaOficial>> _listas = new Dictionary<string, List<FalhaOficial>>();
        private static readonly Dictionary<string, DateTime> _quando = new Dictionary<string, DateTime>();

        public static List<FalhaOficial> Lista(string puid)
        {
            lock (_trava)
            {
                List<FalhaOficial> l;
                return _listas.TryGetValue(puid, out l) ? new List<FalhaOficial>(l) : null;
            }
        }

        public static FalhaOficial Falha(string puid, string chave)
        {
            List<FalhaOficial> l = Lista(puid);
            if (l != null) foreach (FalhaOficial f in l) if (f.Chave == chave) return f;
            return null;
        }

        public static void Atualizar(ConnectLifeNuvem nuvem, Aparelho a)
        {
            lock (_trava)
            {
                DateTime q;
                if (_quando.TryGetValue(a.Puid, out q) && (DateTime.Now - q).TotalHours < 1) return;
                _quando[a.Puid] = DateTime.Now;
            }
            try
            {
                List<FalhaOficial> l = Interpretar(nuvem.AutoDiagnostico(a.Puid));
                lock (_trava) _listas[a.Puid] = l;
            }
            catch (Exception ex) { Log.Write("ConnectLife: autodiagnostico indisponivel: " + ex.Message); }
        }

        internal static void Definir(string puid, List<FalhaOficial> l) { lock (_trava) _listas[puid] = l; }

        /// <summary>Alguma falha ativa agora (f_e_* acusando, error_code diferente de 0 ou item da lista oficial)?</summary>
        public static bool TemFalha(Aparelho a)
        {
            if (a == null) return false;
            foreach (KeyValuePair<string, string> kv in a.Estado)
                if (kv.Key.StartsWith("f_e_", StringComparison.Ordinal) && a.Int(kv.Key, 0) != 0) return true;
            if (a.Int("error_code", 0) != 0) return true;
            List<FalhaOficial> lista = Lista(a.Puid);
            if (lista != null)
                foreach (FalhaOficial f in lista) if (Ativa(a, f)) return true;
            return false;
        }

        /// <summary>A falha da lista oficial esta acusando: o estado tem o valor de falha.</summary>
        internal static bool Ativa(Aparelho a, FalhaOficial f)
        {
            string v;
            return a.Estado.TryGetValue(f.Chave, out v) && v == f.ValorDeFalha;
        }

        /// <summary>
        /// Lista que e um dicionario de codigos (a da lavadora: todos os itens com
        /// statusKey error_code, cada um com um statusValue): mostra so o codigo atual.
        /// </summary>
        private static bool PorCodigo(List<FalhaOficial> l) { return ChaveDeCodigo(l) != null; }

        /// <summary>A chave que se repete em 3 ou mais itens (error_code na lavadora), ou nulo.</summary>
        private static string ChaveDeCodigo(List<FalhaOficial> l)
        {
            if (l == null) return null;
            Dictionary<string, int> n = new Dictionary<string, int>();
            foreach (FalhaOficial f in l) { int k; n.TryGetValue(f.Chave, out k); n[f.Chave] = k + 1; }
            foreach (KeyValuePair<string, int> kv in n) if (kv.Value >= 3) return kv.Key;
            return null;
        }

        /// <summary>O diagnostico em texto: cada falha da lista oficial e o estado dela agora.</summary>
        public static string Texto(Aparelho a)
        {
            if (a == null) return "";
            List<string> l = new List<string>();
            List<FalhaOficial> lista = Lista(a.Puid);
            if (lista == null || lista.Count == 0)
            {
                // Sem a lista oficial ainda: as f_e_* que o aparelho publica.
                foreach (KeyValuePair<string, string> kv in a.Estado)
                    if (kv.Key.StartsWith("f_e_", StringComparison.Ordinal) && kv.Key != "f_e_push")
                        l.Add((a.Int(kv.Key, 0) != 0 ? "\u25CF  " + T.CasaFalhaAtiva : "\u25CB  " + T.CasaFalhaOk) + "  ·  " + T.CasaFalha(kv.Key, null));                // Lavadora (e quem mais publicar error_code) sem a lista oficial ainda.
                if (a.Tem("error_code"))
                    l.Add(a.Int("error_code", 0) == 0 ? "\u25CB  " + T.CasaSemErro : "\u25CF  " + string.Format(T.CasaErroCodigo, a.Int("error_code", 0)));
            }
            else if (PorCodigo(lista))
            {
                string chave = ChaveDeCodigo(lista);
                int atual = a.Int(chave, 0);
                FalhaOficial achada = null;
                int codigos = 0;
                foreach (FalhaOficial f in lista)
                {
                    if (f.Chave != chave) continue;
                    codigos++;
                    if (Ativa(a, f)) achada = f;
                }
                if (atual == 0) l.Add("\u25CB  " + T.CasaSemErro);
                else
                {
                    l.Add("\u25CF  " + (achada != null ? achada.Nome + (achada.Categoria.Length > 0 ? " (" + achada.Categoria + ")" : "") : string.Format(T.CasaErroCodigo, atual)));
                    if (achada != null && achada.Orientacao.Length > 0) l.Add("      " + achada.Orientacao);
                }
                // Os demais itens (sabao, amaciante) so aparecem quando acusam.
                foreach (FalhaOficial f in lista)
                {
                    if (f.Chave == chave || !Ativa(a, f)) continue;
                    l.Add("\u25CF  " + f.Nome + (f.Orientacao.Length > 0 ? "\n      " + f.Orientacao : ""));
                }
                l.Add(T.CasaCodigosConhecidos(codigos));
            }
            else
                foreach (FalhaOficial f in lista)
                {
                    bool ativa = Ativa(a, f);
                    string s = (ativa ? "\u25CF  " + T.CasaFalhaAtiva : "\u25CB  " + T.CasaFalhaOk) + "  ·  " + f.Nome;
                    if (f.Categoria.Length > 0) s += " (" + f.Categoria + ")";
                    if (ativa && f.Orientacao.Length > 0) s += "\n      " + f.Orientacao;
                    l.Add(s);
                }
            if (a.Int("f_e_push", 0) != 0) l.Add("\u25CF  " + string.Format(T.CasaAlertaCodigo, a.Int("f_e_push", 0)));
            DateTime ult = ServicoCasa.UltimaLeitura;
            l.Add("");
            l.Add(T.CasaUltimaLeitura(ult == DateTime.MinValue ? "--" : ult.ToString("HH:mm:ss")) + "  ·  " +
                (a.Online ? T.CasaOnline : T.CasaOffline));
            if (lista != null && lista.Count > 0) l.Add(T.CasaDiagnosticoFonte);
            return string.Join("\n", l.ToArray());
        }

        internal static List<FalhaOficial> Interpretar(Dictionary<string, object> r)
        {
            List<FalhaOficial> l = new List<FalhaOficial>();
            Dictionary<string, object> d = r != null && r.ContainsKey("data") ? r["data"] as Dictionary<string, object> : null;
            List<object> itens = d != null && d.ContainsKey("selfCheckList") ? d["selfCheckList"] as List<object> : null;
            if (itens == null) return l;
            foreach (object o in itens)
            {
                Dictionary<string, object> i = o as Dictionary<string, object>;
                if (i == null) continue;
                FalhaOficial f = new FalhaOficial();
                f.Chave = Json.Texto(i, "statusKey") ?? "";
                f.ValorDeFalha = Json.Texto(i, "statusValue") ?? "1";
                // Os textos oficiais vem em ingles; os conhecidos sao traduzidos.
                string falha = Json.Texto(i, "fault") ?? "";
                f.Nome = falha.Length > 0 ? T.CasaFalhaOficial(f.Chave, falha) :
                    (f.Chave == "error_code" ? string.Format(T.CasaErroCodigo, f.ValorDeFalha) : T.CasaFalhaOficial(f.Chave, f.Chave));
                f.Categoria = T.CasaCategoria(Json.Texto(i, "content") ?? "");
                f.Orientacao = T.CasaOrientacao(Json.Texto(i, "guideKey") ?? "", Json.Texto(i, "guide") ?? "");
                if (f.Chave.Length > 0) l.Add(f);
            }
            return l;
        }
    }
}
