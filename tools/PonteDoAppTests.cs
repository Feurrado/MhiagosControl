using System;
using System.Collections.Generic;
using System.IO;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    /// <summary>
    /// A ponte da interface nova (PonteDoApp) com um app falso: o que a pagina
    /// pede, o que e recusado, o que volta ao estado anterior quando a gravacao
    /// falha, e os arquivos da interface embutidos no executavel. Nenhuma janela
    /// e aberta.
    /// </summary>
    public static partial class Tests
    {
        private sealed class AppFalso : IAppNovo
        {
            public Config Config;
            public List<SensorEntry> Lista = new List<SensorEntry>();
            public Dictionary<string, float> Valores = new Dictionary<string, float>();
            public int Aplicacoes, Pausas;
            public bool Pausa, Inicio, InicioRecusa;
            public string Aberta = "(nada)";
            public Config Cfg { get { return Config; } }
            public List<SensorEntry> Sensores() { return Lista; }
            public Dictionary<string, float> Leituras() { return Valores; }
            public PanelStatus Painel()
            {
                return new PanelStatus(new PanelFrame(54, null, false, true, Config.Active), true, Pausa);
            }
            public bool Pausado { get { return Pausa; } }
            public void AlternarPausa() { Pausa = !Pausa; Pausas++; }
            public void Aplicar() { Aplicacoes++; }
            public void AbrirAntiga(string pagina) { Aberta = pagina; }
            public bool InicioComWindows { get { return Inicio; } }
            public bool DefinirInicioComWindows(bool ligado)
            {
                if (InicioRecusa) return false;
                Inicio = ligado;
                return true;
            }
        }

        private static Dictionary<string, object> PedirNaPonte(PonteDoApp p, string json)
        {
            return p.Atender(Json.Ler(json) as Dictionary<string, object>);
        }

        private static bool PonteOk(Dictionary<string, object> r)
        {
            return r != null && Json.Texto(r, "ok") == "true";
        }

        private static string PonteErro(Dictionary<string, object> r)
        {
            return r == null ? null : Json.Texto(r, "erro");
        }

        private static Profile PerfilDe(Config c, string nome)
        {
            foreach (Profile p in c.Profiles) if (p.Name == nome) return p;
            return null;
        }

        private static string LerRecursoDaInterface(string nome)
        {
            using (Stream s = JanelaDoApp.Abrir(JanelaDoApp.Recurso(JanelaDoApp.OrigemDoApp + "/" + nome)))
            {
                if (s == null) return null;
                using (StreamReader r = new StreamReader(s)) return r.ReadToEnd();
            }
        }

        private static void PonteDoAppNova()
        {
            // --- os arquivos da interface: so nomes simples, da propria origem ---
            Igual("MhiagosControl.ui.app.html", JanelaDoApp.Recurso(JanelaDoApp.OrigemDoApp + "/"), "interface nova: a raiz e a pagina do app");
            Igual("MhiagosControl.ui.app.js", JanelaDoApp.Recurso(JanelaDoApp.OrigemDoApp + "/app.js?v=1"), "interface nova: arquivo da interface");
            Verdade(JanelaDoApp.Recurso(JanelaDoApp.OrigemDoApp + "/a/b.js") == null, "interface nova: pasta e recusada");
            Verdade(JanelaDoApp.Recurso(JanelaDoApp.OrigemDoApp + "/a%20b.js") == null, "interface nova: nome com escape e recusado");
            Verdade(JanelaDoApp.Recurso("https://app.mhiagos.invalid.evil.com/app.js") == null, "interface nova: outra origem nao e servida");
            Verdade(JanelaDoApp.Recurso("http://app.mhiagos.invalid/app.js") == null, "interface nova: http nao e a origem do app");
            Verdade(JanelaDoApp.Recurso("file:///C:/Windows/win.ini") == null, "interface nova: file nao e servido");
            Verdade(JanelaDoApp.Recurso("lixo") == null, "interface nova: endereco invalido nao e servido");
            Verdade(JanelaDoApp.Tipo("x.html").StartsWith("text/html") && JanelaDoApp.Tipo("x.css").StartsWith("text/css") &&
                    JanelaDoApp.Tipo("x.JS").StartsWith("text/javascript") && JanelaDoApp.Tipo("x.exe") == "application/octet-stream",
                    "interface nova: tipo de cada arquivo");
            Verdade(JanelaDoApp.Abrir(JanelaDoApp.Recurso(JanelaDoApp.OrigemDoApp + "/nao-existe.js")) == null, "interface nova: arquivo que nao existe da 404");

            string html = LerRecursoDaInterface("app.html"), js = LerRecursoDaInterface("app.js"), css = LerRecursoDaInterface("app.css");
            Verdade(html != null && js != null && css != null, "interface nova: pagina, script e estilo embutidos no executavel");
            if (html != null && js != null)
            {
                // A politica da pagina recusa script e estilo embutidos: se alguem puser, a tela quebra em silencio.
                Verdade(html.IndexOf("style=", StringComparison.OrdinalIgnoreCase) < 0 && html.IndexOf("<style", StringComparison.OrdinalIgnoreCase) < 0,
                    "interface nova: a pagina nao tem estilo embutido");
                Verdade(html.IndexOf("onclick", StringComparison.OrdinalIgnoreCase) < 0 && html.Contains("<script src=\"app.js\"></script>"),
                    "interface nova: a pagina so carrega script de arquivo");
                Verdade(js.IndexOf("innerHTML", StringComparison.Ordinal) < 0 && js.IndexOf("eval(", StringComparison.Ordinal) < 0 &&
                        js.IndexOf("setAttribute(\"style\"", StringComparison.Ordinal) < 0,
                    "interface nova: o script monta a tela pelo DOM, sem HTML em texto");
            }
            Verdade(JanelaDoApp.Csp.Contains("default-src 'none'") && JanelaDoApp.Csp.Contains("script-src 'self'") &&
                    !JanelaDoApp.Csp.Contains("unsafe"), "interface nova: politica sem excecoes");

            Igual("perfis", TrayContext.PaginaAntiga("painel"), "interface nova: Painel CPU abre os perfis da janela classica");
            Igual("casa", TrayContext.PaginaAntiga("casa"), "interface nova: pagina conhecida passa");
            Verdade(TrayContext.PaginaAntiga("..\\x") == null && TrayContext.PaginaAntiga(null) == null, "interface nova: pagina desconhecida abre a de sempre");

            // --- a ponte, com configuracao num arquivo temporario ---
            string caixa = Path.Combine(Path.GetTempPath(), "MhiagosControlTeste" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(caixa);
            string arquivo = Path.Combine(caixa, "config.ini");
            try
            {
                Config semente = new Config();
                semente.Profiles.Add(Perfil("Padrão", "cpu:temp", "cpu:load", true, false, 0, 10));
                semente.Profiles.Add(Perfil("GPU", "gpu:temp", "gpu:pow", false, false, 0, 0));
                semente.ActiveName = "Padrão";
                semente.DefaultProfileName = "Padrão";
                semente.MetricsChosen = true;
                semente.MetricIds.Add("gpu:temp"); semente.MetricSizes.Add(2);
                Verdade(semente.SaveTo(arquivo), "interface nova: configuracao de teste gravada");

                AppFalso app = new AppFalso();
                app.Config = Config.LoadFrom(arquivo, false);
                app.Lista.Add(Sensor("cpu:temp", "CPU", SensorType.Temperature, "Temperatura", 54.3f));
                app.Lista.Add(Sensor("cpu:load", "CPU", SensorType.Load, "Uso", 12f));
                app.Lista.Add(Sensor("gpu:temp", "GPU", SensorType.Temperature, "Temperatura", 61f));
                app.Lista.Add(Sensor("gpu:pow", "GPU", SensorType.Power, "Potência", 130f));
                app.Lista.Add(null);
                app.Lista.Add(new SensorEntry());
                app.Valores["cpu:temp"] = 54.3f;
                app.Valores["quebrado"] = float.NaN;
                PonteDoApp ponte = new PonteDoApp(app);

                Dictionary<string, object> r = PedirNaPonte(ponte, "{\"id\":7,\"m\":\"iniciar\"}");
                Verdade(PonteOk(r) && Json.Texto(r, "id") == "7", "ponte: iniciar responde com o id do pedido");
                Dictionary<string, object> ini = r != null && r.ContainsKey("r") ? r["r"] as Dictionary<string, object> : null;
                Verdade(ini != null, "ponte: iniciar devolve o estado");
                if (ini != null)
                {
                    Igual(AppInfo.Versao, Json.Texto(ini, "versao"), "ponte: versao");
                    Igual(4, ((List<object>)ini["sensores"]).Count, "ponte: sensor nulo ou sem id fica fora");
                    Igual(2, ((List<object>)ini["perfis"]).Count, "ponte: dois perfis");
                    Igual("Padrão", Json.Texto(ini, "ativo"), "ponte: perfil ativo");
                    Dictionary<string, object> volta = Json.Ler(Json.Escrever(r)) as Dictionary<string, object>;
                    Verdade(volta != null && PonteOk(volta), "ponte: a resposta vira JSON valido");
                }

                // perfil.salvar
                r = PedirNaPonte(ponte, "{\"id\":1,\"m\":\"perfil.salvar\",\"a\":{\"nome\":\"GPU\",\"p1\":\"cpu:temp\",\"p2\":\"\",\"fahrenheit\":true,\"percent\":true,\"div1\":10,\"div2\":0,\"rodizio\":true}}");
                Verdade(PonteOk(r), "ponte: perfil salvo");
                Igual(1, app.Aplicacoes, "ponte: a alteracao e aplicada uma vez");
                Config lido = Config.LoadFrom(arquivo, false);
                Profile gpu = PerfilDe(lido, "GPU");
                Verdade(gpu != null && gpu.Panel1Id == "cpu:temp" && string.IsNullOrEmpty(gpu.Panel2Id) && gpu.Fahrenheit && gpu.Percent &&
                        gpu.Divisor1 == 10 && gpu.Rotate, "ponte: o perfil salvo chegou ao disco");

                r = PedirNaPonte(ponte, "{\"id\":2,\"m\":\"perfil.salvar\",\"a\":{\"nome\":\"GPU\",\"p1\":\"C:\\\\Windows\\\\win.ini\"}}");
                Verdade(!PonteOk(r) && PonteErro(r) == T.NovoSensorInexistente, "ponte: sensor fora da lista e recusado");
                r = PedirNaPonte(ponte, "{\"id\":3,\"m\":\"perfil.salvar\",\"a\":{\"nome\":\"GPU\",\"div1\":7}}");
                Verdade(!PonteOk(r) && PonteErro(r) == T.NovoValorInvalido, "ponte: divisor invalido e recusado");
                r = PedirNaPonte(ponte, "{\"id\":4,\"m\":\"perfil.salvar\",\"a\":{\"nome\":\"Nenhum\"}}");
                Verdade(!PonteOk(r) && PonteErro(r) == T.NovoPerfilInexistente, "ponte: perfil que nao existe e recusado");
                Igual(1, app.Aplicacoes, "ponte: pedido recusado nao aplica nada");
                Igual("cpu:temp", PerfilDe(app.Config, "GPU").Panel1Id, "ponte: pedido recusado nao muda o perfil");

                // perfil.criar
                r = PedirNaPonte(ponte, "{\"id\":5,\"m\":\"perfil.criar\",\"a\":{\"nome\":\"  Jogos 144 \"}}");
                Profile novo = PerfilDe(app.Config, "Jogos 144");
                Verdade(PonteOk(r) && app.Config.Profiles.Count == 3 && novo != null,
                    "ponte: perfil novo criado, sem espacos nas pontas");
                Verdade(novo != null && novo.Panel1Id == app.Config.Active.Panel1Id && !novo.Rotate,
                    "ponte: o perfil novo copia o ativo e nao entra no rodizio");
                r = PedirNaPonte(ponte, "{\"id\":6,\"m\":\"perfil.criar\",\"a\":{\"nome\":\"jogos 144\"}}");
                Verdade(!PonteOk(r) && PonteErro(r) == T.NovoNomeRepetido, "ponte: nome repetido e recusado");
                string[] ruins = { "", "   ", "..\\\\..\\\\x", "a|b", "nome=valor", "[secao]", "um nome comprido demais para caber" };
                foreach (string ruim in ruins)
                {
                    r = PedirNaPonte(ponte, "{\"id\":6,\"m\":\"perfil.criar\",\"a\":{\"nome\":\"" + ruim + "\"}}");
                    Verdade(!PonteOk(r) && PonteErro(r) == T.NovoNomeInvalido, "ponte: nome invalido e recusado (" + ruim + ")");
                }
                Igual(3, app.Config.Profiles.Count, "ponte: nome recusado nao cria perfil");

                // perfil.ativar
                r = PedirNaPonte(ponte, "{\"id\":8,\"m\":\"perfil.ativar\",\"a\":{\"nome\":\"GPU\"}}");
                Verdade(PonteOk(r) && app.Config.ActiveName == "GPU" && Config.LoadFrom(arquivo, false).ActiveName == "GPU",
                    "ponte: perfil ativado e gravado");
                r = PedirNaPonte(ponte, "{\"id\":9,\"m\":\"perfil.ativar\",\"a\":{\"nome\":\"Nenhum\"}}");
                Verdade(!PonteOk(r) && app.Config.ActiveName == "GPU", "ponte: ativar perfil que nao existe nao muda nada");

                // config.salvar: os mesmos limites da gravacao
                r = PedirNaPonte(ponte, "{\"id\":10,\"m\":\"config.salvar\",\"a\":{\"ocioso\":15,\"rodizio\":20}}");
                Verdade(PonteOk(r) && app.Config.IdleBlankMinutes == 15 && app.Config.RotateSeconds == 20, "ponte: tempos salvos");
                string[] temposRuins = { "{\"ocioso\":-1}", "{\"ocioso\":1000}", "{\"rodizio\":1}", "{\"rodizio\":1000}", "{\"rodizio\":-5}" };
                foreach (string tempo in temposRuins)
                {
                    r = PedirNaPonte(ponte, "{\"id\":11,\"m\":\"config.salvar\",\"a\":" + tempo + "}");
                    Verdade(!PonteOk(r) && PonteErro(r) == T.NovoValorInvalido, "ponte: tempo fora da faixa e recusado " + tempo);
                }
                Verdade(app.Config.IdleBlankMinutes == 15 && app.Config.RotateSeconds == 20, "ponte: tempo recusado nao muda a configuracao");
                r = PedirNaPonte(ponte, "{\"id\":12,\"m\":\"config.salvar\",\"a\":{\"rodizio\":0}}");
                Verdade(PonteOk(r) && app.Config.RotateSeconds == 0 && app.Config.IdleBlankMinutes == 15, "ponte: campo ausente fica como estava");

                // metricas.definir: o tamanho do cartao acompanha o sensor
                r = PedirNaPonte(ponte, "{\"id\":13,\"m\":\"metricas.definir\",\"a\":{\"ids\":[\"cpu:temp\",\"gpu:temp\",\"cpu:temp\"]}}");
                Verdade(PonteOk(r) && app.Config.MetricIds.Count == 2 && app.Config.MetricIds[0] == "cpu:temp" && app.Config.MetricIds[1] == "gpu:temp",
                    "ponte: sensores acompanhados, sem repetir");
                Verdade(app.Config.MetricSizes.Count == 2 && app.Config.MetricSizes[0] == 0 && app.Config.MetricSizes[1] == 2,
                    "ponte: o tamanho do cartao segue o sensor");
                r = PedirNaPonte(ponte, "{\"id\":14,\"m\":\"metricas.definir\",\"a\":{\"ids\":[\"cpu:temp\",\"nao:existe\"]}}");
                Verdade(!PonteOk(r) && PonteErro(r) == T.NovoSensorInexistente && app.Config.MetricIds.Count == 2, "ponte: sensor desconhecido nao e acompanhado");
                System.Text.StringBuilder muitos = new System.Text.StringBuilder();
                for (int i = 0; i <= PonteDoApp.MaximoDeMetricas; i++) muitos.Append(i == 0 ? "" : ",").Append("\"s").Append(i).Append("\"");
                r = PedirNaPonte(ponte, "{\"id\":15,\"m\":\"metricas.definir\",\"a\":{\"ids\":[" + muitos + "]}}");
                Verdade(!PonteOk(r) && PonteErro(r) == T.NovoValorInvalido, "ponte: sensores demais sao recusados");

                r = PedirNaPonte(ponte, "{\"id\":16,\"m\":\"metricas\",\"a\":{\"ids\":[\"cpu:temp\",\"gpu:temp\"],\"segundos\":600}}");
                Dictionary<string, object> met = PonteOk(r) ? r["r"] as Dictionary<string, object> : null;
                Dictionary<string, object> series = met != null && met.ContainsKey("series") ? met["series"] as Dictionary<string, object> : null;
                Verdade(series != null && series.ContainsKey("cpu:temp") && series.ContainsKey("gpu:temp") && series["cpu:temp"] is List<object>,
                    "ponte: uma serie por sensor pedido");
                Verdade(met != null && Json.Ler(Json.Escrever(r)) != null, "ponte: as series viram JSON valido");

                // pausar, inicio, abrir
                r = PedirNaPonte(ponte, "{\"id\":17,\"m\":\"pausar\"}");
                Verdade(PonteOk(r) && app.Pausa && app.Pausas == 1 && Json.Texto(r, "r") == "true", "ponte: pausar alterna e informa");
                r = PedirNaPonte(ponte, "{\"id\":18,\"m\":\"inicio\",\"a\":{\"ligado\":true}}");
                Verdade(PonteOk(r) && app.Inicio, "ponte: iniciar com o Windows ligado");
                app.InicioRecusa = true;
                r = PedirNaPonte(ponte, "{\"id\":19,\"m\":\"inicio\",\"a\":{\"ligado\":false}}");
                Verdade(!PonteOk(r) && app.Inicio, "ponte: recusa do Windows e informada");
                r = PedirNaPonte(ponte, "{\"id\":20,\"m\":\"abrir\",\"a\":{\"pagina\":\"painel\"}}");
                Verdade(PonteOk(r) && app.Aberta == "painel", "ponte: abrir a janela classica");

                // pedidos estranhos
                Verdade(!PonteOk(PedirNaPonte(ponte, "{\"id\":21,\"m\":\"apagar.tudo\"}")), "ponte: pedido desconhecido e recusado");
                Verdade(!PonteOk(PedirNaPonte(ponte, "{\"id\":22}")), "ponte: pedido sem metodo e recusado");
                Verdade(!PonteOk(ponte.Atender(null)), "ponte: pedido nulo e recusado");
                Verdade(!PonteOk(PedirNaPonte(ponte, "{\"id\":23,\"m\":\"perfil.salvar\",\"a\":\"texto\"}")), "ponte: argumento que nao e objeto e recusado");

                // o retrato de cada segundo
                Dictionary<string, object> ev = ponte.Estado();
                Dictionary<string, object> d = ev["d"] as Dictionary<string, object>;
                Dictionary<string, object> painel = d != null ? d["painel"] as Dictionary<string, object> : null;
                Dictionary<string, object> v = d != null ? d["v"] as Dictionary<string, object> : null;
                Verdade(Json.Texto(ev, "ev") == "estado" && painel != null && v != null, "ponte: retrato com painel e leituras");
                if (painel != null && v != null)
                {
                    Verdade(Json.Texto(painel, "p1") == "54" && painel["p2"] == null, "ponte: mostrador sem leitura vai nulo");
                    Verdade(Json.Texto(painel, "pausado") == "true" && Json.Texto(painel, "conectado") == "true" && Json.Texto(painel, "perfil") == "GPU",
                        "ponte: estado do painel");
                    Verdade(v.ContainsKey("cpu:temp") && !v.ContainsKey("quebrado"), "ponte: leitura que nao e numero fica fora");
                    Igual("GPU", Json.Texto(d, "ativo"), "ponte: o retrato diz o perfil ativo");
                }
                Verdade(Json.Ler(Json.Escrever(ev)) != null, "ponte: o retrato vira JSON valido");

                // gravacao que falha: a memoria volta a ser o que esta no disco
                AppFalso semDisco = new AppFalso();
                semDisco.Config = new Config();
                semDisco.Config.Profiles.Add(Perfil("Padrão", "cpu:temp", "cpu:load", true, false, 0, 0));
                semDisco.Config.ActiveName = "Padrão";
                semDisco.Config.IdleBlankMinutes = 5;
                semDisco.Lista.AddRange(app.Lista);
                PonteDoApp ponte2 = new PonteDoApp(semDisco);
                r = PedirNaPonte(ponte2, "{\"id\":1,\"m\":\"perfil.salvar\",\"a\":{\"nome\":\"Padrão\",\"p1\":\"gpu:temp\",\"fahrenheit\":true,\"div1\":100,\"rodizio\":true}}");
                Profile p0 = PerfilDe(semDisco.Config, "Padrão");
                int perfisAntes = semDisco.Config.Profiles.Count;
                Verdade(!PonteOk(r) && p0.Panel1Id == "cpu:temp" && !p0.Fahrenheit && p0.Divisor1 == 0 && !p0.Rotate,
                    "ponte: perfil que nao gravou volta ao que era");
                r = PedirNaPonte(ponte2, "{\"id\":2,\"m\":\"perfil.criar\",\"a\":{\"nome\":\"Novo\"}}");
                Verdade(!PonteOk(r) && semDisco.Config.Profiles.Count == perfisAntes, "ponte: perfil que nao gravou nao fica na lista");
                r = PedirNaPonte(ponte2, "{\"id\":3,\"m\":\"config.salvar\",\"a\":{\"ocioso\":30}}");
                Verdade(!PonteOk(r) && semDisco.Config.IdleBlankMinutes == 5, "ponte: tempo que nao gravou volta ao que era");
                r = PedirNaPonte(ponte2, "{\"id\":4,\"m\":\"metricas.definir\",\"a\":{\"ids\":[\"cpu:temp\"]}}");
                Verdade(!PonteOk(r) && semDisco.Config.MetricIds.Count == 0 && !semDisco.Config.MetricsChosen, "ponte: metricas que nao gravaram voltam ao que eram");
                Igual(0, semDisco.Aplicacoes, "ponte: o que nao gravou nao e aplicado");
            }
            finally
            {
                try { Directory.Delete(caixa, true); } catch { }
            }
        }
    }
}
