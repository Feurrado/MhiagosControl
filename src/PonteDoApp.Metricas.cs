using System;
using System.Collections.Generic;

namespace MhiagosControl
{
    /// <summary>
    /// A pagina Metricas da interface nova: a grade de cartoes, os conjuntos,
    /// os perfis de metricas e a janela de tempo, com a mesma configuracao e
    /// as mesmas regras da pagina classica (SettingsForm.Metricas).
    /// </summary>
    internal sealed partial class PonteDoApp
    {
        private SensorEntry SensorDaMetrica(string id)
        {
            foreach (SensorEntry e in _app.Sensores()) if (e != null && e.Id == id) return e;
            return null;
        }

        /// <summary>Tudo o que a pagina precisa para desenhar a grade; devolvido depois de cada acao.</summary>
        private Dictionary<string, object> PaginaDeMetricas(string aviso)
        {
            Config c = _app.Cfg;
            Dictionary<string, object> r = D();
            List<object> itens = new List<object>();
            for (int i = 0; i < c.MetricIds.Count; i++)
            {
                string id = c.MetricIds[i];
                SensorEntry s = SensorDaMetrica(id);
                Dictionary<string, object> d = D();
                d["id"] = id;
                d["tam"] = Math.Max(0, Math.Min(2, c.MetricSize(i)));
                d["titulo"] = s == null ? T.SensorUnavailable : MetricPicker.Rotulo(s);
                d["sub"] = s == null ? id : id.StartsWith(Rtss.Prefixo, StringComparison.Ordinal) ? MetricPicker.RodapeJogos() : MetricPicker.Rodape(s);
                string unidade = s == null ? "" : s.EffectiveUnit;
                d["unidade"] = unidade ?? "";
                float? atencao, perigo;
                MetricPicker.Faixas(unidade, id, out atencao, out perigo);
                d["atencao"] = atencao.HasValue ? (object)(double)atencao.Value : null;
                d["perigo"] = perigo.HasValue ? (object)(double)perigo.Value : null;
                itens.Add(d);
            }
            r["itens"] = itens;
            r["janela"] = MetricHistory.JanelaValida(c.MetricRange);
            r["passo"] = MetricHistory.PassoSeg;
            r["nav"] = !string.IsNullOrEmpty(c.MetricNavId) && c.MetricIds.Contains(c.MetricNavId) ? c.MetricNavId
                : c.MetricIds.Count > 0 ? c.MetricIds[0] : "";

            List<object> conjuntos = new List<object>();
            foreach (MetricPicker.Conjunto cj in MetricPicker.Conjuntos)
            {
                Dictionary<string, object> d = D();
                d["chave"] = cj.Chave; d["nome"] = cj.Nome;
                conjuntos.Add(d);
            }
            r["conjuntos"] = conjuntos;
            List<object> perfis = new List<object>();
            foreach (MetricProfile p in c.MetricProfiles) perfis.Add(p.Name);
            r["perfis"] = perfis;
            r["aviso"] = aviso ?? "";
            return r;
        }

        private static void Trocar2<TT>(List<TT> lista, int i, int j)
        {
            TT x = lista[i]; lista[i] = lista[j]; lista[j] = x;
        }

        private static MetricProfile CapturaDaGrade(Config c, string nome)
        {
            MetricProfile p = new MetricProfile();
            p.Name = nome;
            p.Range = MetricHistory.JanelaValida(c.MetricRange);
            p.Ids.AddRange(c.MetricIds);
            p.Sizes.AddRange(c.MetricSizes);
            return p;
        }

        /// <summary>Uma acao da pagina Metricas; devolve a pagina refeita ou o erro.</summary>
        private Dictionary<string, object> AcaoDeMetricas(object id, Dictionary<string, object> a)
        {
            string acao = Texto(a, "acao");
            string alvo = Texto(a, "id");
            Config c = _app.Cfg;
            string aviso = null;
            string erro;

            if (acao == "pagina") return Feito(id, PaginaDeMetricas(null));

            erro = Transacao(delegate
            {
                int i = c.MetricIds.IndexOf(alvo);
                switch (acao)
                {
                    case "add":
                        if (string.IsNullOrEmpty(alvo) || c.MetricIds.Contains(alvo)) return null;
                        if (!SensorExiste(alvo)) return T.NovoSensorInexistente;
                        if (c.MetricIds.Count >= MaximoDeMetricas) return T.NovoValorInvalido;
                        while (c.MetricSizes.Count < c.MetricIds.Count) c.MetricSizes.Add(0);
                        c.MetricIds.Add(alvo); c.MetricSizes.Add(0); c.MetricsChosen = true;
                        return null;
                    case "remover":
                        if (i < 0) return null;
                        c.MetricIds.RemoveAt(i);
                        if (i < c.MetricSizes.Count) c.MetricSizes.RemoveAt(i);
                        c.MetricsChosen = true;
                        return null;
                    case "esq":
                    case "dir":
                        {
                            int j = i + (acao == "esq" ? -1 : 1);
                            if (i < 0 || j < 0 || j >= c.MetricIds.Count) return null;
                            while (c.MetricSizes.Count < c.MetricIds.Count) c.MetricSizes.Add(0);
                            Trocar2(c.MetricIds, i, j); Trocar2(c.MetricSizes, i, j);
                            return null;
                        }
                    case "tamanho":
                        if (i < 0) return null;
                        while (c.MetricSizes.Count <= i) c.MetricSizes.Add(0);
                        c.MetricSizes[i] = (c.MetricSizes[i] + 1) % 3;
                        return null;
                    case "janela":
                        c.MetricRange = MetricHistory.JanelaValida(Int(a, "segundos", MetricHistory.JanelaPadrao));
                        return null;
                    case "nav":
                        if (i >= 0) c.MetricNavId = alvo;
                        return null;
                    case "conjunto":
                        {
                            MetricPicker.Conjunto cj = null;
                            string chave = Texto(a, "chave");
                            foreach (MetricPicker.Conjunto x in MetricPicker.Conjuntos) if (x.Chave == chave) cj = x;
                            if (cj == null) return T.NovoValorInvalido;
                            List<SensorEntry> escolhidos = MetricPicker.Montar(cj, new List<SensorEntry>(_app.Sensores()));
                            if (escolhidos.Count == 0) { aviso = T.PresetEmpty; return null; }
                            c.MetricIds.Clear(); c.MetricSizes.Clear();
                            foreach (SensorEntry s in escolhidos) { c.MetricIds.Add(s.Id); c.MetricSizes.Add(0); }
                            c.MetricsChosen = true;
                            aviso = T.PresetApplied(cj.Nome, escolhidos.Count);
                            return null;
                        }
                    case "perfil.salvar":
                        {
                            string nome = (Texto(a, "nome") ?? "").Trim();
                            if (nome.Length == 0) return T.NovoNomeInvalido;
                            if (c.MetricProfileNameExists(nome)) return T.NameTaken;
                            c.MetricProfiles.Add(CapturaDaGrade(c, nome));
                            aviso = T.MetricProfileSaved(nome);
                            return null;
                        }
                    case "perfil.aplicar":
                        {
                            MetricProfile p = c.MetricProfileByName(Texto(a, "nome"));
                            if (p == null) return T.NovoValorInvalido;
                            c.MetricIds.Clear(); c.MetricIds.AddRange(p.Ids);
                            c.MetricSizes.Clear(); c.MetricSizes.AddRange(p.Sizes);
                            c.MetricRange = MetricHistory.JanelaValida(p.Range);
                            c.MetricsChosen = true;
                            aviso = T.MetricProfileApplied(p.Name);
                            return null;
                        }
                    case "perfil.atualizar":
                        {
                            MetricProfile p = c.MetricProfileByName(Texto(a, "nome"));
                            if (p == null) return T.NovoValorInvalido;
                            MetricProfile nova = CapturaDaGrade(c, p.Name);
                            p.Range = nova.Range;
                            p.Ids.Clear(); p.Ids.AddRange(nova.Ids);
                            p.Sizes.Clear(); p.Sizes.AddRange(nova.Sizes);
                            aviso = T.MetricProfileSaved(p.Name);
                            return null;
                        }
                    case "perfil.renomear":
                        {
                            MetricProfile p = c.MetricProfileByName(Texto(a, "nome"));
                            string novo = (Texto(a, "novo") ?? "").Trim();
                            if (p == null || novo.Length == 0) return T.NovoNomeInvalido;
                            MetricProfile outro = c.MetricProfileByName(novo);
                            if (outro != null && !ReferenceEquals(outro, p)) return T.NameTaken;
                            p.Name = novo;
                            return null;
                        }
                    case "perfil.excluir":
                        {
                            MetricProfile p = c.MetricProfileByName(Texto(a, "nome"));
                            if (p != null) c.MetricProfiles.Remove(p);
                            return null;
                        }
                }
                return T.NovoValorInvalido;
            });
            if (erro != null) return Falha(id, erro);
            MetricHistory.SeguirTambem(c.MetricIds);
            return Feito(id, PaginaDeMetricas(aviso));
        }
    }
}
