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
            public bool Todos; public int Relistas, Idiomas, WidgetMudancas, Cantos;
            public ConfigDoWidget W = new ConfigDoWidget();
            public void TodosOsSensores(bool todos) { Todos = todos; Relistas++; }
            public void IdiomaMudou() { Idiomas++; }
            public ConfigDoWidget Widget { get { return W; } }
            public void LigarWidget(bool ligar) { W.Ativo = ligar; }
            public void WidgetMudou() { WidgetMudancas++; }
            public void WidgetAoCanto() { Cantos++; }
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
            Igual("painel", TrayContext.PaginaNova("perfis"), "interface nova padrao: perfis da classica abre o Painel CPU");
            Igual("baterias", TrayContext.PaginaNova("baterias"), "interface nova padrao: o widget abre Baterias");
            Igual("config", TrayContext.PaginaNova("configuracoes"), "interface nova padrao: configuracoes");
            Verdade(TrayContext.PaginaNova(null) == null && TrayContext.PaginaNova("x\"><") == null, "interface nova padrao: chave desconhecida abre a de sempre");

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

        private static void PonteDoAppAjustes()
        {
            string caixa = Path.Combine(Path.GetTempPath(), "MhiagosControlTeste" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(caixa);
            string arquivo = Path.Combine(caixa, "config.ini");
            string idiomaAntes = T.Language;
            try
            {
                Config semente = new Config();
                semente.Profiles.Add(Perfil("Padrão", "cpu:temp", "cpu:load", true, false, 0, 10));
                semente.Profiles.Add(Perfil("GPU", "gpu:temp", "gpu:pow", false, false, 0, 0));
                semente.ActiveName = "GPU";
                semente.DefaultProfileName = "GPU";
                semente.MapearJogo("cs2.exe", "GPU");
                Verdade(semente.SaveTo(arquivo), "ajustes: configuracao de teste gravada");

                AppFalso app = new AppFalso();
                app.Config = Config.LoadFrom(arquivo, false);
                app.Lista.Add(Sensor("cpu:temp", "CPU", SensorType.Temperature, "Temperatura", 54f));
                app.Lista.Add(Sensor("cpu:load", "CPU", SensorType.Load, "Uso", 12f));
                app.Lista.Add(Sensor("gpu:temp", "GPU", SensorType.Temperature, "Temperatura", 61f));
                app.Lista.Add(Sensor("gpu:pow", "GPU", SensorType.Power, "Potência", 130f));
                PonteDoApp ponte = new PonteDoApp(app);
                List<Dictionary<string, object>> enviadas = new List<Dictionary<string, object>>();
                ponte.Enviar = delegate(Dictionary<string, object> m) { enviadas.Add(m); };

                Dictionary<string, object> ini = ponte.Iniciar();
                Dictionary<string, object> jogos = ini["jogos"] as Dictionary<string, object>;
                Verdade(jogos != null && ((List<object>)jogos["vinculos"]).Count == 1, "ajustes: o vinculo do jogo vai para a pagina");
                Dictionary<string, object> casa = ini["casa"] as Dictionary<string, object>;
                Verdade(casa != null && casa.ContainsKey("externo") && !casa.ContainsKey("chave"), "ajustes: a chave do servidor nunca vai para a pagina");
                Verdade(ini.ContainsKey("widget") && ini.ContainsKey("motor") && ini.ContainsKey("todos"), "ajustes: widget, motor e sensores no iniciar");

                // renomear o ativo, o padrao e o do jogo leva os tres junto
                Dictionary<string, object> r = PedirNaPonte(ponte, "{\"id\":1,\"m\":\"perfil.renomear\",\"a\":{\"nome\":\"GPU\",\"novo\":\"Placa\"}}");
                Config lido = Config.LoadFrom(arquivo, false);
                Verdade(PonteOk(r) && lido.ActiveName == "Placa" && lido.DefaultProfileName == "Placa" && lido.PerfilDoJogo("cs2.exe") == "Placa",
                    "ajustes: renomear leva ativo, padrao e jogo junto");
                r = PedirNaPonte(ponte, "{\"id\":2,\"m\":\"perfil.renomear\",\"a\":{\"nome\":\"Placa\",\"novo\":\"padrão\"}}");
                Verdade(!PonteOk(r) && PonteErro(r) == T.NovoNomeRepetido, "ajustes: renomear para nome existente e recusado");
                r = PedirNaPonte(ponte, "{\"id\":3,\"m\":\"perfil.renomear\",\"a\":{\"nome\":\"Placa\",\"novo\":\"PLACA\"}}");
                Verdade(PonteOk(r) && PerfilDe(app.Config, "PLACA") != null && app.Config.ActiveName == "PLACA", "ajustes: so mudar maiusculas e permitido");
                r = PedirNaPonte(ponte, "{\"id\":4,\"m\":\"perfil.renomear\",\"a\":{\"nome\":\"PLACA\",\"novo\":\"a|b\"}}");
                Verdade(!PonteOk(r) && PerfilDe(app.Config, "PLACA") != null, "ajustes: nome invalido nao renomeia");

                // duplicar, padrao, excluir
                r = PedirNaPonte(ponte, "{\"id\":5,\"m\":\"perfil.duplicar\",\"a\":{\"nome\":\"Padrão\",\"novo\":\"Cópia\"}}");
                Profile copia = PerfilDe(app.Config, "Cópia");
                Verdade(PonteOk(r) && copia != null && copia.Panel1Id == "cpu:temp" && copia.Divisor2 == 10, "ajustes: duplicar copia o perfil");
                r = PedirNaPonte(ponte, "{\"id\":6,\"m\":\"perfil.padrao\",\"a\":{\"nome\":\"Cópia\"}}");
                Verdade(PonteOk(r) && Config.LoadFrom(arquivo, false).DefaultProfileName == "Cópia", "ajustes: perfil padrao gravado");
                r = PedirNaPonte(ponte, "{\"id\":7,\"m\":\"perfil.excluir\",\"a\":{\"nome\":\"PLACA\"}}");
                lido = Config.LoadFrom(arquivo, false);
                Verdade(PonteOk(r) && PerfilDe(lido, "PLACA") == null && lido.NameExists(lido.ActiveName) && lido.PerfilDoJogo("cs2.exe") == null,
                    "ajustes: excluir o ativo troca o ativo e solta o jogo");
                PedirNaPonte(ponte, "{\"id\":8,\"m\":\"perfil.excluir\",\"a\":{\"nome\":\"Cópia\"}}");
                Verdade(lido.NameExists(Config.LoadFrom(arquivo, false).DefaultProfileName), "ajustes: excluir o padrao escolhe outro padrao");
                r = PedirNaPonte(ponte, "{\"id\":9,\"m\":\"perfil.excluir\",\"a\":{\"nome\":\"Padrão\"}}");
                Verdade(!PonteOk(r) && PonteErro(r) == T.KeepOneProfile && app.Config.Profiles.Count == 1, "ajustes: o ultimo perfil fica");

                // jogos
                r = PedirNaPonte(ponte, "{\"id\":10,\"m\":\"jogos.ligar\",\"a\":{\"ligado\":true}}");
                Verdade(PonteOk(r) && Config.LoadFrom(arquivo, false).GameProfiles, "ajustes: perfil por jogo ligado");
                r = PedirNaPonte(ponte, "{\"id\":11,\"m\":\"jogo.vincular\",\"a\":{\"perfil\":\"Padrão\"}}");
                Verdade(Rtss.JogoAtual != null || (!PonteOk(r) && PonteErro(r) == T.NoGameToBind), "ajustes: sem jogo aberto nao ha o que vincular");
                app.Config.MapearJogo("x.exe", "Padrão");
                r = PedirNaPonte(ponte, "{\"id\":12,\"m\":\"jogo.desvincular\",\"a\":{\"chave\":\"x.exe\"}}");
                Verdade(PonteOk(r) && app.Config.PerfilDoJogo("x.exe") == null, "ajustes: desvincular solta o jogo");
                r = PedirNaPonte(ponte, "{\"id\":13,\"m\":\"jogo.desvincular\",\"a\":{\"chave\":\"nunca.exe\"}}");
                Verdade(!PonteOk(r), "ajustes: desvincular jogo sem vinculo e recusado");

                // sensores, idioma, widget
                r = PedirNaPonte(ponte, "{\"id\":14,\"m\":\"sensores.todos\",\"a\":{\"ligado\":true}}");
                Verdade(PonteOk(r) && app.Todos && app.Relistas == 1 && Config.LoadFrom(arquivo, false).ShowAllSensors, "ajustes: todos os sensores grava e relista");
                T.Language = T.PtBr;
                r = PedirNaPonte(ponte, "{\"id\":15,\"m\":\"idioma\",\"a\":{\"lingua\":\"en\"}}");
                Verdade(PonteOk(r) && T.Language == T.EnUs && app.Idiomas == 1 && Config.LoadFrom(arquivo, false).Language == T.EnUs,
                    "ajustes: idioma grava, vale e refaz o menu");
                r = PedirNaPonte(ponte, "{\"id\":16,\"m\":\"idioma\",\"a\":{\"lingua\":\"xx\"}}");
                Verdade(!PonteOk(r) && T.Language == T.EnUs, "ajustes: idioma desconhecido e recusado");
                T.Language = T.PtBr;
                r = PedirNaPonte(ponte, "{\"id\":17,\"m\":\"widget\",\"a\":{\"ativo\":true,\"layout\":\"lista\",\"vidro\":\"fosco\",\"cor\":\"escura\",\"frente\":true}}");
                Verdade(PonteOk(r) && app.W.Ativo && app.W.Layout == LayoutDoWidget.Lista && app.W.Fosco && !app.W.Nitido &&
                        app.W.Cor == CorDoVidro.Escura && app.W.NaFrente && app.WidgetMudancas == 1, "ajustes: widget");
                r = PedirNaPonte(ponte, "{\"id\":18,\"m\":\"widget\",\"a\":{\"layout\":\"pizza\"}}");
                Verdade(!PonteOk(r) && app.W.Layout == LayoutDoWidget.Lista && app.WidgetMudancas == 1, "ajustes: opcao de widget invalida e recusada");

                // arquivos: o dialogo devolve um caminho, ou nulo quando a pessoa desiste
                string escolhido = null;
                ponte.Arquivo = delegate(bool salvar, string tipo, string sugerido) { return escolhido; };
                Verdade(PedirNaPonte(ponte, "{\"id\":19,\"m\":\"perfil.exportar\",\"a\":{\"nome\":\"Padrão\"}}") == null, "ajustes: exportar responde depois");
                Verdade(enviadas.Count == 1 && PonteOk(enviadas[0]) && enviadas[0]["r"] == null, "ajustes: desistir do dialogo nao e erro");
                escolhido = Path.Combine(caixa, "perfil.ini");
                PedirNaPonte(ponte, "{\"id\":20,\"m\":\"perfil.exportar\",\"a\":{\"nome\":\"Padrão\"}}");
                Verdade(PonteOk(enviadas[1]) && File.Exists(escolhido), "ajustes: perfil exportado");
                PedirNaPonte(ponte, "{\"id\":21,\"m\":\"perfil.importar\"}");
                Dictionary<string, object> imp = PonteOk(enviadas[2]) ? enviadas[2]["r"] as Dictionary<string, object> : null;
                Verdade(imp != null && app.Config.Profiles.Count == 2 && Json.Texto(imp, "editar") != "Padrão" && imp.ContainsKey("aviso"),
                    "ajustes: perfil importado com nome unico");
                escolhido = arquivo;   // a propria configuracao: a exportacao recusa
                PedirNaPonte(ponte, "{\"id\":22,\"m\":\"perfil.exportar\",\"a\":{\"nome\":\"Padrão\"}}");
                Verdade(!PonteOk(enviadas[3]), "ajustes: exportar por cima da configuracao e recusado");

                escolhido = Path.Combine(caixa, "copia.mhiagos-backup");
                PedirNaPonte(ponte, "{\"id\":23,\"m\":\"backup.exportar\"}");
                Verdade(PonteOk(enviadas[4]) && File.Exists(escolhido), "ajustes: copia de seguranca criada");
                int perfisDaCopia = app.Config.Profiles.Count;
                PedirNaPonte(ponte, "{\"id\":24,\"m\":\"perfil.duplicar\",\"a\":{\"nome\":\"Padrão\",\"novo\":\"Depois\"}}");
                Verdade(PerfilDe(app.Config, "Depois") != null, "ajustes: mudanca depois da copia");
                r = PedirNaPonte(ponte, "{\"id\":25,\"m\":\"backup.aplicar\"}");
                Verdade(!PonteOk(r), "ajustes: aplicar sem ler a copia e recusado");
                PedirNaPonte(ponte, "{\"id\":26,\"m\":\"backup.ler\"}");
                Dictionary<string, object> resumo = PonteOk(enviadas[5]) ? enviadas[5]["r"] as Dictionary<string, object> : null;
                Verdade(resumo != null && Json.Texto(resumo, "perfis") == perfisDaCopia.ToString(), "ajustes: resumo da copia");
                r = PedirNaPonte(ponte, "{\"id\":27,\"m\":\"backup.aplicar\"}");
                Verdade(PonteOk(r) && PerfilDe(app.Config, "Depois") == null && PerfilDe(Config.LoadFrom(arquivo, false), "Depois") == null,
                    "ajustes: a copia restaurada vale e foi gravada");
                Verdade(Directory.GetFiles(caixa, "config.ini.before-restore-*").Length == 1, "ajustes: a configuracao de antes ficou guardada ao lado");

                Verdade(Json.Texto(PedirNaPonte(ponte, "{\"id\":28,\"m\":\"diagnostico\"}"), "r").Contains(AppInfo.Versao), "ajustes: diagnostico");
                Verdade(Instalador.Comando("w.exe", "a.b").Contains("install --id a.b -e"), "ajustes: comando do winget");

                T.Language = T.PtBr;   // a copia restaurada trouxe o ingles gravado antes
                // gravacao que falha nao deixa rastro
                AppFalso semDisco = new AppFalso();
                semDisco.Config = new Config();
                semDisco.Config.Profiles.Add(Perfil("A", "", "", true, false, 0, 0));
                semDisco.Config.Profiles.Add(Perfil("B", "", "", true, false, 0, 0));
                semDisco.Config.ActiveName = "A";
                PonteDoApp ponte2 = new PonteDoApp(semDisco);
                Verdade(!PonteOk(PedirNaPonte(ponte2, "{\"id\":1,\"m\":\"perfil.excluir\",\"a\":{\"nome\":\"A\"}}")) &&
                        semDisco.Config.Profiles.Count == 2 && semDisco.Config.ActiveName == "A", "ajustes: excluir que nao gravou volta atras");
                Verdade(!PonteOk(PedirNaPonte(ponte2, "{\"id\":2,\"m\":\"perfil.renomear\",\"a\":{\"nome\":\"A\",\"novo\":\"C\"}}")) &&
                        PerfilDe(semDisco.Config, "A") != null && semDisco.Config.ActiveName == "A", "ajustes: renomear que nao gravou volta atras");
                Verdade(!PonteOk(PedirNaPonte(ponte2, "{\"id\":3,\"m\":\"idioma\",\"a\":{\"lingua\":\"en\"}}")) && T.Language == T.PtBr,
                    "ajustes: idioma que nao gravou nao vale");
                Igual(0, semDisco.Aplicacoes + semDisco.Idiomas, "ajustes: o que nao gravou nao e aplicado");
            }
            finally
            {
                T.Language = idiomaAntes;
                try { Directory.Delete(caixa, true); } catch { }
            }
        }
    }
}
