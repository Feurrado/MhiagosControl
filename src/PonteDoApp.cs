using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>O que a janela nova precisa do app em execucao. A TrayContext implementa; os testes, uma falsa.</summary>
    internal interface IAppNovo
    {
        Config Cfg { get; }
        List<SensorEntry> Sensores();
        Dictionary<string, float> Leituras();
        PanelStatus Painel();
        bool Pausado { get; }
        void AlternarPausa();
        /// <summary>A configuracao mudou e ja foi gravada: vale no mostrador, no menu e no historico.</summary>
        void Aplicar();
        void AbrirAntiga(string pagina);
        bool InicioComWindows { get; }
        bool DefinirInicioComWindows(bool ligado);
    }

    /// <summary>
    /// A ponte entre a pagina (HTML no WebView2) e o app. A pagina manda pedidos
    /// {id, m, a} e recebe {id, ok, r} ou {id, ok:false, erro}; uma vez por segundo
    /// recebe {ev:"estado", d}. Nao depende do WebView2: e so dicionario para
    /// dicionario, o que deixa tudo testavel sem abrir janela.
    ///
    /// Tudo o que chega da pagina e dado: nomes sao validados, ids de sensor so
    /// valem se existem na lista, e nada vira caminho ou comando.
    /// </summary>
    internal sealed class PonteDoApp
    {
        private readonly IAppNovo _app;
        /// <summary>Entrega uma resposta que ficou pronta fora da thread da interface (as especificacoes).</summary>
        public Action<Dictionary<string, object>> Enviar;
        internal static readonly Regex NomeDePerfil = new Regex(@"^[\p{L}\p{N}][\p{L}\p{N} _\-\.]{0,23}$");
        internal const int MaximoDeMetricas = 12;

        public PonteDoApp(IAppNovo app) { _app = app; }

        // ---------------- utilitarios de dicionario ----------------

        private static Dictionary<string, object> D() { return new Dictionary<string, object>(); }

        private static string Texto(Dictionary<string, object> d, string k)
        {
            return d == null ? null : Json.Texto(d, k);
        }

        private static bool Bool(Dictionary<string, object> d, string k, bool padrao)
        {
            string s = Texto(d, k);
            return s == "true" ? true : s == "false" ? false : padrao;
        }

        private static int Int(Dictionary<string, object> d, string k, int padrao)
        {
            double v;
            string s = Texto(d, k);
            return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) &&
                   v > -1e9 && v < 1e9 ? (int)Math.Round(v) : padrao;
        }

        private static Dictionary<string, object> Falha(object id, string erro)
        {
            Dictionary<string, object> r = D();
            r["id"] = id; r["ok"] = false; r["erro"] = erro;
            return r;
        }

        private static Dictionary<string, object> Feito(object id, object resultado)
        {
            Dictionary<string, object> r = D();
            r["id"] = id; r["ok"] = true; r["r"] = resultado;
            return r;
        }

        // ---------------- pedidos ----------------

        /// <summary>Atende um pedido. Nulo quando a resposta vai depois, por Enviar.</summary>
        public Dictionary<string, object> Atender(Dictionary<string, object> pedido)
        {
            object id = null;
            if (pedido != null) pedido.TryGetValue("id", out id);
            try
            {
                string m = Texto(pedido, "m") ?? "";
                Dictionary<string, object> a = pedido != null && pedido.ContainsKey("a") ? pedido["a"] as Dictionary<string, object> : null;
                if (a == null) a = D();
                switch (m)
                {
                    case "iniciar": return Feito(id, Iniciar());
                    case "perfil.ativar": return Resultado(id, AtivarPerfil(Texto(a, "nome")));
                    case "perfil.salvar": return Resultado(id, SalvarPerfil(a));
                    case "perfil.criar": return Resultado(id, CriarPerfil(Texto(a, "nome")));
                    case "config.salvar": return Resultado(id, SalvarConfig(a));
                    case "metricas.definir": return Resultado(id, DefinirMetricas(a));
                    case "metricas": return Feito(id, Metricas(a));
                    case "pausar": _app.AlternarPausa(); return Feito(id, _app.Pausado);
                    case "inicio":
                        if (!_app.DefinirInicioComWindows(Bool(a, "ligado", false))) return Falha(id, T.AutostartFailed);
                        return Feito(id, _app.InicioComWindows);
                    case "abrir": _app.AbrirAntiga(Texto(a, "pagina")); return Feito(id, true);
                    case "specs": Specs(id); return null;
                    default: return Falha(id, "pedido desconhecido");
                }
            }
            catch (Exception ex)
            {
                Log.Error("interface nova: pedido", ex);
                return Falha(id, T.CasaFalhaInesperada);
            }
        }

        private Dictionary<string, object> Resultado(object id, string erro)
        {
            return erro == null ? Feito(id, Iniciar()) : Falha(id, erro);
        }

        private static Dictionary<string, object> DePerfil(Profile p)
        {
            Dictionary<string, object> d = D();
            d["nome"] = p.Name; d["p1"] = p.Panel1Id ?? ""; d["p2"] = p.Panel2Id ?? "";
            d["fahrenheit"] = p.Fahrenheit; d["percent"] = p.Percent;
            d["div1"] = p.Divisor1; d["div2"] = p.Divisor2; d["rodizio"] = p.Rotate;
            return d;
        }

        /// <summary>Tudo o que a pagina precisa para se montar; devolvido tambem depois de cada alteracao.</summary>
        internal Dictionary<string, object> Iniciar()
        {
            Config c = _app.Cfg;
            Dictionary<string, object> r = D();
            r["versao"] = AppInfo.Versao;
            r["pt"] = T.Pt;
            r["escuro"] = Ui.Dark;
            List<object> sensores = new List<object>();
            foreach (SensorEntry e in _app.Sensores())
            {
                if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                Dictionary<string, object> s = D();
                s["id"] = e.Id; s["nome"] = e.Name ?? ""; s["hardware"] = e.Hardware ?? "";
                s["categoria"] = e.Category ?? ""; s["rotulo"] = e.Label ?? e.Name ?? e.Id;
                s["unidade"] = e.EffectiveUnit ?? "";
                sensores.Add(s);
            }
            r["sensores"] = sensores;
            List<object> perfis = new List<object>();
            foreach (Profile p in c.Profiles) perfis.Add(DePerfil(p));
            r["perfis"] = perfis;
            r["ativo"] = c.Active.Name;
            r["padrao"] = c.DefaultProfile.Name;
            Dictionary<string, object> cfg = D();
            cfg["ocioso"] = c.IdleBlankMinutes; cfg["rodizio"] = c.RotateSeconds;
            cfg["inicio"] = _app.InicioComWindows;
            r["config"] = cfg;
            List<object> metricas = new List<object>();
            foreach (string id in c.MetricIds) metricas.Add(id);
            r["metricas"] = metricas;
            r["passo"] = MetricHistory.PassoSeg;
            r["maximo"] = ConfigTiming.Maximum;
            Dictionary<string, object> casa = D();
            casa["servidor"] = ServidorCasa.Configurado;
            r["casa"] = casa;
            return r;
        }

        private Profile Achar(string nome)
        {
            if (nome == null) return null;
            foreach (Profile p in _app.Cfg.Profiles) if (p.Name == nome) return p;
            return null;
        }

        private string Gravar()
        {
            string erro;
            if (!_app.Cfg.Save(out erro)) return T.SaveFailed(erro);
            _app.Aplicar();
            return null;
        }

        private string AtivarPerfil(string nome)
        {
            if (Achar(nome) == null) return T.NovoPerfilInexistente;
            string erro;
            if (!ProfileActivation.TryActivate(_app.Cfg, nome, out erro)) return T.SaveFailed(erro);
            _app.Aplicar();
            return null;
        }

        private bool SensorExiste(string id)
        {
            if (string.IsNullOrEmpty(id)) return true;   // vazio = mostrador sem sensor
            foreach (SensorEntry e in _app.Sensores()) if (e != null && e.Id == id) return true;
            return false;
        }

        private static bool DivisorValido(int d) { return d == 0 || d == 1 || d == 10 || d == 100 || d == 1000; }

        private string SalvarPerfil(Dictionary<string, object> a)
        {
            Profile p = Achar(Texto(a, "nome"));
            if (p == null) return T.NovoPerfilInexistente;
            string p1 = Texto(a, "p1") ?? p.Panel1Id, p2 = Texto(a, "p2") ?? p.Panel2Id;
            int d1 = Int(a, "div1", p.Divisor1), d2 = Int(a, "div2", p.Divisor2);
            if (!SensorExiste(p1) || !SensorExiste(p2)) return T.NovoSensorInexistente;
            if (!DivisorValido(d1) || !DivisorValido(d2)) return T.NovoValorInvalido;
            Profile antes = p.Clone();
            p.Panel1Id = p1; p.Panel2Id = p2; p.Divisor1 = d1; p.Divisor2 = d2;
            p.Fahrenheit = Bool(a, "fahrenheit", p.Fahrenheit);
            p.Percent = Bool(a, "percent", p.Percent);
            p.Rotate = Bool(a, "rodizio", p.Rotate);
            string erro = Gravar();
            if (erro != null)
            {
                // Nao gravou: a memoria volta a ser o que esta no disco.
                p.Panel1Id = antes.Panel1Id; p.Panel2Id = antes.Panel2Id; p.Divisor1 = antes.Divisor1; p.Divisor2 = antes.Divisor2;
                p.Fahrenheit = antes.Fahrenheit; p.Percent = antes.Percent; p.Rotate = antes.Rotate;
            }
            return erro;
        }

        private string CriarPerfil(string nome)
        {
            nome = (nome ?? "").Trim();
            if (!NomeDePerfil.IsMatch(nome)) return T.NovoNomeInvalido;
            if (_app.Cfg.NameExists(nome)) return T.NovoNomeRepetido;
            if (_app.Cfg.Profiles.Count >= 32) return T.NovoValorInvalido;
            Profile novo = _app.Cfg.Active.Clone();
            novo.Name = nome;
            novo.Rotate = false;
            _app.Cfg.Profiles.Add(novo);
            string erro = Gravar();
            if (erro != null) _app.Cfg.Profiles.Remove(novo);
            return erro;
        }

        private string SalvarConfig(Dictionary<string, object> a)
        {
            Config c = _app.Cfg;
            int ocioso = Int(a, "ocioso", c.IdleBlankMinutes), rodizio = Int(a, "rodizio", c.RotateSeconds);
            // Os mesmos limites que a gravacao exige (ConfigTiming): rodizio de 1 s nao existe.
            if (ocioso < 0 || ocioso > ConfigTiming.Maximum || rodizio < 0 || rodizio == 1 || rodizio > ConfigTiming.Maximum)
                return T.NovoValorInvalido;
            int ociosoAntes = c.IdleBlankMinutes, rodizioAntes = c.RotateSeconds;
            c.IdleBlankMinutes = ocioso; c.RotateSeconds = rodizio;
            string erro = Gravar();
            if (erro != null) { c.IdleBlankMinutes = ociosoAntes; c.RotateSeconds = rodizioAntes; }
            return erro;
        }

        private static List<string> Ids(Dictionary<string, object> a)
        {
            List<string> l = new List<string>();
            List<object> lista = a.ContainsKey("ids") ? a["ids"] as List<object> : null;
            if (lista == null) return l;
            foreach (object o in lista)
            {
                string s = o as string;
                if (!string.IsNullOrEmpty(s) && s.Length <= 200 && !l.Contains(s)) l.Add(s);
            }
            return l;
        }

        private string DefinirMetricas(Dictionary<string, object> a)
        {
            List<string> ids = Ids(a);
            if (ids.Count > MaximoDeMetricas) return T.NovoValorInvalido;
            foreach (string id in ids) if (!SensorExiste(id)) return T.NovoSensorInexistente;
            Config c = _app.Cfg;
            List<string> antes = c.MetricIds;
            List<int> tamanhosAntes = c.MetricSizes;
            bool escolhidasAntes = c.MetricsChosen;
            // O tamanho de cada cartao (da janela classica) acompanha o seu sensor.
            List<int> tamanhos = new List<int>();
            foreach (string id in ids)
            {
                int i = antes.IndexOf(id);
                tamanhos.Add(i >= 0 && i < tamanhosAntes.Count ? tamanhosAntes[i] : 0);
            }
            c.MetricIds = ids; c.MetricSizes = tamanhos; c.MetricsChosen = true;
            string erro = Gravar();
            if (erro != null) { c.MetricIds = antes; c.MetricSizes = tamanhosAntes; c.MetricsChosen = escolhidasAntes; }
            return erro;
        }

        /// <summary>As series do historico: um vetor por id, nulo onde nao houve amostra.</summary>
        private Dictionary<string, object> Metricas(Dictionary<string, object> a)
        {
            int segundos = MetricHistory.JanelaValida(Int(a, "segundos", MetricHistory.JanelaPadrao));
            Dictionary<string, object> series = D();
            float[] destino = null;
            foreach (string id in Ids(a))
            {
                if (series.Count >= MaximoDeMetricas) break;
                int n = MetricHistory.Janela(id, segundos, ref destino);
                List<object> pontos = new List<object>(n);
                for (int i = 0; i < n && destino != null && i < destino.Length; i++)
                {
                    float v = destino[i];
                    pontos.Add(float.IsNaN(v) || float.IsInfinity(v) ? null : (object)(double)v);
                }
                series[id] = pontos;
            }
            Dictionary<string, object> r = D();
            r["segundos"] = segundos; r["passo"] = MetricHistory.PassoSeg; r["series"] = series;
            return r;
        }

        private void Specs(object id)
        {
            List<SensorEntry> copia = new List<SensorEntry>(_app.Sensores());
            ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, object> resposta;
                try
                {
                    List<object> grupos = new List<object>();
                    foreach (SpecGrupo g in SpecSheet.Coletar(copia))
                    {
                        Dictionary<string, object> d = D();
                        d["titulo"] = g.Titulo ?? ""; d["destaque"] = g.Destaque;
                        List<object> linhas = new List<object>();
                        foreach (string[] l in g.Linhas)
                        {
                            if (l == null || l.Length < 2) continue;
                            List<object> par = new List<object>();
                            par.Add(l[0] ?? ""); par.Add(l[1] ?? "");
                            linhas.Add(par);
                        }
                        d["linhas"] = linhas;
                        grupos.Add(d);
                    }
                    resposta = Feito(id, grupos);
                }
                catch (Exception ex)
                {
                    Log.Error("interface nova: especificacoes", ex);
                    resposta = Falha(id, T.CasaFalhaInesperada);
                }
                Action<Dictionary<string, object>> enviar = Enviar;
                if (enviar != null) enviar(resposta);
            });
        }

        // ---------------- o retrato de cada segundo ----------------

        public Dictionary<string, object> Estado()
        {
            Dictionary<string, object> d = D();
            PanelStatus s = _app.Painel();
            Dictionary<string, object> painel = D();
            PanelFrame f = s != null ? s.Frame : null;
            painel["p1"] = f != null && f.Panel1.HasValue ? (object)f.Panel1.Value : null;
            painel["p2"] = f != null && f.Panel2.HasValue ? (object)f.Panel2.Value : null;
            painel["fahrenheit"] = f != null && f.Fahrenheit;
            painel["percent"] = f == null || f.Percent;
            painel["perfil"] = f != null && f.Profile != null ? f.Profile.Name : _app.Cfg.Active.Name;
            painel["conectado"] = s != null && s.Connected;
            painel["pausado"] = _app.Pausado;
            d["painel"] = painel;

            Dictionary<string, object> v = D();
            foreach (KeyValuePair<string, float> kv in _app.Leituras())
                if (!float.IsNaN(kv.Value) && !float.IsInfinity(kv.Value)) v[kv.Key] = (double)kv.Value;
            d["v"] = v;

            List<object> bat = new List<object>();
            foreach (Periferico p in Perifericos.Atual)
            {
                if (p == null) continue;
                Dictionary<string, object> b = D();
                b["nome"] = p.Nome ?? ""; b["tipo"] = p.Tipo.ToString(); b["fonte"] = p.Fonte.ToString();
                b["bateria"] = p.Bateria.HasValue ? (object)p.Bateria.Value : null;
                b["carregando"] = p.Carregando; b["estado"] = p.Estado.ToString();
                double? resta = null;
                try { resta = HistoricoDeBateria.Restante(p); } catch (Exception ex) { Log.Error("interface nova: estimativa", ex); }
                b["restante"] = resta.HasValue && !double.IsNaN(resta.Value) ? HistoricoDeBateria.DuracaoAproximada(resta.Value) : null;
                bat.Add(b);
            }
            d["baterias"] = bat;

            List<object> casa = new List<object>();
            foreach (Aparelho a in ServicoCasa.Aparelhos)
            {
                if (a == null) continue;
                Dictionary<string, object> c = D();
                c["nome"] = a.Nome ?? ""; c["online"] = a.Online;
                c["tipo"] = a.ArCondicionado ? "ar" : a.Lavadora ? "lavadora" : "outro";
                if (a.ArCondicionado)
                {
                    c["ligado"] = a.Int("t_power", 0) != 0;
                    c["ambiente"] = a.Tem("f_temp_in") ? (object)a.Num("f_temp_in", 0) : null;
                    c["alvo"] = a.Tem("t_temp") ? (object)a.Num("t_temp", 0) : null;
                    c["modo"] = TabelaDoAr.Modo(a.Int("t_work_mode", -1), a.Tipo);
                }
                casa.Add(c);
            }
            d["casa"] = casa;
            d["ativo"] = _app.Cfg.Active.Name;

            Dictionary<string, object> evento = D();
            evento["ev"] = "estado"; evento["d"] = d;
            return evento;
        }
    }

    public static partial class T
    {
        public static string NovoPerfilInexistente { get { return P("esse perfil não existe mais", "that profile no longer exists"); } }
        public static string NovoSensorInexistente { get { return P("esse sensor não está na lista", "that sensor is not in the list"); } }
        public static string NovoValorInvalido { get { return P("valor fora do permitido", "value out of range"); } }
        public static string NovoNomeInvalido { get { return P("use de 1 a 24 letras, números, espaço, ponto, hífen ou sublinhado", "use 1 to 24 letters, digits, space, dot, hyphen or underscore"); } }
        public static string NovoNomeRepetido { get { return P("já existe um perfil com esse nome", "a profile with that name already exists"); } }
    }
}
