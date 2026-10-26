using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Pagina Casa (ConnectLife): o code do login, o JSON da nuvem e o arranjo
    /// dos cartoes. Nada aqui fala com a nuvem nem le o cofre da maquina.
    /// </summary>
    public static partial class Tests
    {
        private static void RegressionCasa(string root)
        {
            CasaCodigoDoLogin();
            CasaJsonEAparelhos();
            CasaMetaDoModelo();
            CasaServicoOffline();
            CasaPagina(root);
        }

        private static void CasaCodigoDoLogin()
        {
            Secao("casa: code do login pelo navegador");
            string erro;
            Igual("ABC123", ConnectLifeNuvem.ExtrairCodigo(ConnectLifeNuvem.Retorno + "?code=ABC123&state=xyz", "xyz", out erro),
                "endereco final");
            // O formato que o navegador do Thiago mostrou de verdade.
            Igual("0a1b2c3d4e5f60718293a4b5c6d7e8f9", ConnectLifeNuvem.ExtrairCodigo(
                "https://my.home-assistant.io/redirect/_change/?redirect=oauth%2F%3Fcode%3D0a1b2c3d4e5f60718293a4b5c6d7e8f9%26state%3DXy_estadoDeTeste12345a",
                "Xy_estadoDeTeste12345a", out erro), "pagina intermediaria /redirect/_change/");
            Igual("CODIGO", ConnectLifeNuvem.ExtrairCodigo("  CODIGO ", "x", out erro), "codigo sozinho");
            Verdade(ConnectLifeNuvem.ExtrairCodigo(ConnectLifeNuvem.Retorno + "?code=X&state=outro", "esperado", out erro) == null && erro == T.CasaEstadoAntigo,
                "endereco de tentativa anterior e recusado");
            Verdade(ConnectLifeNuvem.ExtrairCodigo("https://oauth.hijuconn.com/login?response_type=code&client_id=1", "x", out erro) == null && erro == T.CasaSemCodigo,
                "a pagina de login colada por engano nao vira code");
            Verdade(ConnectLifeNuvem.ExtrairCodigo("", "x", out erro) == null && erro == T.CasaColeVazio, "vazio pede o endereco");

            string url = ConnectLifeNuvem.EnderecoDeLogin("est");
            Verdade(url.StartsWith("https://oauth.hijuconn.com/login?") && url.Contains("state=est") &&
                url.Contains("redirect_uri=https%3A%2F%2Fmy.home-assistant.io%2Fredirect%2Foauth"), "endereco de login montado");
            Igual("/clife-svc/pu/x?a=1", ConnectLifeNuvem.Caminho("https://juapi-3rd.hijuconn.com/clife-svc/pu/x?a=1"),
                "a assinatura usa o caminho com a consulta");
        }

        private static void CasaJsonEAparelhos()
        {
            Secao("casa: JSON da nuvem");
            string resposta = "{\"resultCode\":0,\"deviceList\":[{\"wifiId\":\"w\",\"deviceId\":\"d1\",\"puid\":\"p1\"," +
                "\"deviceNickName\":\"Ar do quarto\",\"deviceFeatureCode\":\"100\",\"deviceFeatureName\":\"AW-12CR2RWGU00\"," +
                "\"deviceTypeCode\":\"009\",\"deviceTypeName\":\"AC\",\"roomName\":\"Quarto\",\"offlineState\":1," +
                "\"statusList\":{\"t_power\":\"1\",\"t_temp\":\"23\",\"f_temp_in\":\"26.5\",\"t_work_mode\":\"2\"}}," +
                "{\"deviceId\":\"d2\",\"puid\":\"p2\",\"deviceTypeCode\":\"003\",\"offlineState\":0,\"statusList\":null}]," +
                "\"txt\":\"a\\u00e7\\n\\\"b\\\"\",\"n\":-1.5e2}";
            Dictionary<string, object> o = Json.Ler(resposta) as Dictionary<string, object>;
            Verdade(o != null, "le a resposta");
            if (o == null) return;
            Igual("0", Json.Texto(o, "resultCode"), "resultCode como texto");
            Igual("aç\n\"b\"", Json.Texto(o, "txt"), "escapes de texto");
            Igual(-150d, o["n"], "numero com expoente");
            List<object> lista = (List<object>)o["deviceList"];
            Aparelho ar = Aparelho.De((Dictionary<string, object>)lista[0]);
            Aparelho lav = Aparelho.De((Dictionary<string, object>)lista[1]);
            Verdade(ar.ArCondicionado && ar.Online && ar.Nome == "Ar do quarto", "ar-condicionado reconhecido e conectado");
            Igual(23, ar.Int("t_temp", 0), "alvo lido");
            Igual(26.5d, ar.Num("f_temp_in", 0), "ambiente com casa decimal");
            Igual(27, ar.Int("f_temp_in", 0), "inteiro arredonda para longe do zero");
            Verdade(lav.Lavadora && !lav.Online && lav.Estado.Count == 0, "lavadora sem estado nao quebra");

            Dictionary<string, object> volta = Json.Ler(Json.Escrever(o)) as Dictionary<string, object>;
            Verdade(volta != null && Json.Texto(volta, "txt") == Json.Texto(o, "txt"), "escrever e ler de novo da o mesmo");
        }

        private static void CasaPagina(string root)
        {
            Secao("casa: pagina, login e cartoes");
            PaginaCasa.IgnorarCofre = true;
            string pasta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mhiagos-casa-teste");
            System.IO.Directory.CreateDirectory(pasta);
            string prefAntes = PreferenciasCasa.Arquivo, histAntes = HistoricoDoAr.Arquivo;
            PreferenciasCasa.Arquivo = System.IO.Path.Combine(pasta, "casa.ini");
            HistoricoDoAr.Arquivo = System.IO.Path.Combine(pasta, "casa-historico.txt");
            System.IO.File.Delete(PreferenciasCasa.Arquivo);
            System.IO.File.Delete(HistoricoDoAr.Arquivo);
            ServicoCasa.Pref = new PreferenciasCasa();
            HistoricoDoAr.Limpar();
            SemearHistorico("p1");
            try
            {
                foreach (string language in new string[] { T.PtBr, T.EnUs })
                {
                    T.Language = language;
                    ServicoCasa.PararSimulacao();
                    Config config = AuditConfig(root, "casa-" + language);
                    using (SettingsForm form = BugForm(config, new FreshData()))
                    {
                        NavBar nav = (NavBar)Field(form, "_nav");
                        int idx = -1;
                        for (int i = 0; i < nav.Items.Count; i++) if (nav.Items[i].Key == "casa") idx = i;
                        Verdade(idx >= 0 && nav.Items[nav.Items.Count - 1].Key == "sobre", "Casa na barra, Sobre continua por ultimo " + language);
                        nav.SelectedIndex = idx;
                        PaginaCasa casa = (PaginaCasa)Field(form, "_pgCasa");

                        foreach (int largura in new int[] { 1300, 880 })
                        {
                            form.Size = new Size(largura, 760);
                            casa.Arranjar();
                            string sufixo = language + "-" + largura;
                            Verdade(!casa.Logado && casa.Login.Visible, "sem acesso guardado mostra o login " + sufixo);
                            Verdade(casa.Login.Left >= 0 && casa.Login.Right <= casa.Width, "login cabe na largura " + sufixo);
                            SemSobrepor(casa.Login, "login " + sufixo);
                            CaptureEvolution(form, "casa-login-" + sufixo);
                        }

                        casa.MostrarSemNuvem(Exemplo());
                        foreach (int largura in new int[] { 1300, 880 })
                        {
                            form.Size = new Size(largura, 760);
                            casa.Arranjar();
                            string sufixo = language + "-" + largura;
                            Verdade(!casa.Login.Visible && casa.Cartoes.Count == 2, "dois cartoes depois de entrar " + sufixo);
                            Verdade(casa.Cartoes[0] is CartaoDoAr && casa.Cartoes[1] is CartaoDaLavadora, "ar primeiro, lavadora depois");
                            foreach (CartaoDeAparelho c in casa.Cartoes)
                            {
                                Verdade(c.Right <= casa.Width - Pagina.LarguraDaBarra, "cartao cabe na largura " + sufixo);
                                SemSobrepor(c, c.GetType().Name + " " + sufixo);
                            }
                            Verdade(casa.Cartoes[1].Top > casa.Cartoes[0].Bottom, "o ar ocupa a largura toda e a lavadora vem embaixo");
                            CartaoDoAr ar = (CartaoDoAr)casa.Cartoes[0];
                            Blocos modos = (Blocos)Field(ar, "_modo"), ventos = (Blocos)Field(ar, "_vento");
                            Igual(4, modos.Itens.Count, "so os quatro modos do modelo " + sufixo);
                            Igual(5, modos.Selecionado, "modo atual aceso");
                            Igual(3, ventos.Itens.Count, "so as tres velocidades do modelo");
                            Mostrador dial = (Mostrador)Field(ar, "_dial");
                            Verdade(dial.Min == 16 && dial.Max == 30 && dial.Valor == 23, "mostrador na faixa do modelo");
                            List<string> ks = new List<string>();
                            foreach (Toggle t in (List<Toggle>)Field(ar, "_chaves")) ks.Add((string)t.Tag);
                            Verdade(ks.Contains("t_sleep") && !ks.Contains("t_beep") && !ks.Contains("t_device_info") && !ks.Contains("t_eco"),
                                "interruptores do aparelho: so o noturno; nada que o modelo nao tenha");
                            Verdade(((Toggle)Field(ar, "_semBipe")).Visible, "o bipe virou a preferencia Comandos sem bipe");

                            // Mostrador inteiro dentro do cartao, centrado na coluna, com o puxador cabendo.
                            RectangleF anel = dial.Anel();
                            float folga = Ui.S(14) + 1;
                            Verdade(anel.X - folga >= 0 && anel.Right + folga <= dial.Width && anel.Y - folga >= 0,
                                "anel e puxador cabem no mostrador " + sufixo);
                            Verdade(dial.Left >= Ui.S(18), "mostrador afastado da borda do cartao " + sufixo);
                            // Topo do puxador, com o contorno de foco, a pelo menos 20 px do divisor do cabecalho (64).
                            int topoPuxador = dial.Top + (int)anel.Y - Ui.S(14) - 3;
                            Verdade(topoPuxador - Ui.S(64) >= Ui.S(20), "respiro entre o puxador e o cabecalho " + sufixo +
                                " (" + (topoPuxador - Ui.S(64)) + " px)");
                            BotaoRedondo menos = (BotaoRedondo)Field(ar, "_menos"), mais = (BotaoRedondo)Field(ar, "_mais");
                            int centroDial = dial.Left + dial.Width / 2;
                            Verdade(Math.Abs((menos.Left + mais.Right) / 2 - centroDial) <= 1, "- e + centrados sob o mostrador " + sufixo);                            casa.SemBipe = true;
                            Verdade(((Toggle)Field(ar, "_semBipe")).Checked && ar.SemBipe, "a preferencia da pagina chega ao cartao");
                            casa.SemBipe = false;

                            // Faixa de acoes: as tres cenas e o timer neste modelo.
                            AcoesDoAr acoes = ar.Acoes;
                            Verdade(acoes.Visible && acoes.Top >= menos.Bottom && acoes.Bottom <= ar.Height, "acoes embaixo do painel, dentro do cartao " + sufixo);
                            SemSobrepor(acoes, "acoes " + sufixo);
                            CartaoHistoricoDoAr hist = casa.Historico;
                            Verdade(hist != null && hist.Visible && hist.Top > ar.Bottom && hist.Bottom < casa.Cartoes[1].Top,
                                "historico e consumo logo abaixo do ar " + sufixo);
                            if (hist != null) SemSobrepor(hist, "historico " + sufixo);
                            CaptureEvolution(form, "casa-aparelhos-" + sufixo);
                            if (largura == 1300 && hist != null)
                            {
                                CaptureEvolution(hist, "casa-historico-" + sufixo);
                                ServicoCasa.DefinirTimer("p1", 60);
                                acoes.Mostrar(ar.Aparelho);
                                typeof(AcoesDoAr).GetField("_diagAberto", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(acoes, true);
                                acoes.Mostrar(ar.Aparelho);
                                casa.Arranjar();
                                SemSobrepor(acoes, "acoes com timer e diagnostico " + sufixo);
                                CaptureEvolution(ar, "casa-ar-acoes-" + sufixo);
                                ServicoCasa.CancelarTimer();
                                typeof(AcoesDoAr).GetField("_diagAberto", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(acoes, false);
                                acoes.Mostrar(ar.Aparelho);
                                casa.Arranjar();
                            }
                        }
                    }
                }
            }
            finally
            {
                ServicoCasa.PararSimulacao();
                ServicoCasa.CancelarTimer();
                PaginaCasa.IgnorarCofre = false;
                PreferenciasCasa.Arquivo = prefAntes;
                HistoricoDoAr.Arquivo = histAntes;
                HistoricoDoAr.Limpar();
                T.Language = T.PtBr;
            }
        }

        /// <summary>Dez horas de leituras sinteticas: ligado de 2 h atras ate agora, com uma lacuna.</summary>
        private static void SemearHistorico(string puid)
        {
            DateTime ini = DateTime.Now.AddHours(-10);
            for (DateTime t = ini; t <= DateTime.Now; t = t.AddMinutes(1))
            {
                if (t > DateTime.Now.AddHours(-5) && t < DateTime.Now.AddHours(-4)) continue;   // app fechado
                AmostraAr s = new AmostraAr();
                s.Quando = t;
                bool ligado = t > DateTime.Now.AddHours(-2);
                double h = (DateTime.Now - t).TotalHours;
                s.Ambiente = (float)(ligado ? 24 + 3 * Math.Exp(-(2 - h) * 2) : 26 + Math.Sin(h / 2));
                s.Alvo = 23; s.Ligado = ligado; s.Modo = 2;
                HistoricoDoAr.Acrescentar(puid, s);
            }
        }

        private static void CasaServicoOffline()
        {
            Secao("casa: cenas, alertas, timer, consumo e diagnostico");
            string pasta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mhiagos-casa-teste2");
            System.IO.Directory.CreateDirectory(pasta);
            string prefAntes = PreferenciasCasa.Arquivo;
            PreferenciasCasa.Arquivo = System.IO.Path.Combine(pasta, "casa.ini");
            System.IO.File.Delete(PreferenciasCasa.Arquivo);
            Action<string, string> notifAntes = ServicoCasa.Notificar;
            List<string> avisos = new List<string>();
            ServicoCasa.Notificar = delegate(string t, string x) { avisos.Add(t + "|" + x); };
            try
            {
                Aparelho ar = Exemplo()[1];

                // Cenas: comando unico, so com valores do modelo.
                AcoesDoAr acoes = new AcoesDoAr();
                acoes.Mostrar(ar);
                Dictionary<string, int> dormir = acoes.CenaDormir(), gelar = acoes.CenaGelar(), eco = acoes.CenaEco();
                Verdade(dormir != null && dormir["t_sleep"] == 1 && dormir["t_temp"] == 24 && dormir["t_fan_speed"] == 2 && dormir["t_power"] == 1,
                    "Dormir: noturno, 24 graus, ventilador minimo, ligado");
                Verdade(gelar != null && gelar["t_work_mode"] == 2 && gelar["t_temp"] == 18 && gelar["t_fan_speed"] == 4,
                    "Gelar rapido: Frio, 18 graus, ventilador maximo");
                Verdade(eco != null && eco["t_work_mode"] == 5 && eco["t_temp"] == 25, "Economico: ECO a 25 graus");

                // O noturno vai num segundo comando: junto dos outros, o ar de janela ignorava.
                Dictionary<string, string> cmd = new Dictionary<string, string>();
                foreach (KeyValuePair<string, int> kv in dormir) cmd[kv.Key] = kv.Value.ToString();
                List<Dictionary<string, string>> partes = ServicoCasa.Partes(cmd);
                Verdade(partes.Count == 2 && !partes[0].ContainsKey("t_sleep") && partes[0].Count == 3 &&
                    partes[1].Count == 1 && partes[1]["t_sleep"] == "1", "Dormir: noturno sozinho, depois do resto");
                Dictionary<string, string> soNoturno = new Dictionary<string, string>();
                soNoturno["t_sleep"] = "0";
                Verdade(ServicoCasa.Partes(soNoturno).Count == 1, "noturno sozinho continua um comando so");
                Aparelho split = new Aparelho();
                split.Tipo = "009"; split.Online = true; split.Estado["t_power"] = "1"; split.Estado["t_work_mode"] = "2";
                split.Meta = MetaPropriedade.DaResposta((Dictionary<string, object>)Json.Ler(
                    "{\"properties\":[{\"propertyKey\":\"t_work_mode\",\"propertyValueList\":\"0,1,2,3,4\",\"propertyRW\":\"RW\"}," +
                    "{\"propertyKey\":\"t_temp\",\"propertyValueList\":\"16~32\",\"propertyRW\":\"RW\"}]}"));
                acoes.Mostrar(split);
                Verdade(acoes.CenaEco() == null, "sem ECO no modelo, a cena Economico nao aparece");
                acoes.Dispose();

                // Alertas: uma vez por passagem para o estado ruim.
                Alertas.Zerar();
                PreferenciasCasa p = new PreferenciasCasa();
                DateTime t0 = DateTime.Now;
                ar.Estado["f_e_intemp"] = "1";
                Alertas.Conferir(ar, p, t0);
                Alertas.Conferir(ar, p, t0.AddMinutes(1));
                Igual(1, avisos.Count, "falha avisa uma vez so");
                ar.Estado["f_e_intemp"] = "0";
                Alertas.Conferir(ar, p, t0.AddMinutes(2));
                ar.Estado["f_e_intemp"] = "1";
                Alertas.Conferir(ar, p, t0.AddMinutes(3));
                Igual(2, avisos.Count, "falha que volta depois de sumir avisa de novo");
                ar.Estado["f_e_intemp"] = "0";

                avisos.Clear();
                ar.Online = false;
                Alertas.Conferir(ar, p, t0);
                Alertas.Conferir(ar, p, t0.AddMinutes(4));
                Igual(0, avisos.Count, "desconectado ha 4 min ainda nao avisa");
                Alertas.Conferir(ar, p, t0.AddMinutes(6));
                Alertas.Conferir(ar, p, t0.AddMinutes(9));
                Igual(1, avisos.Count, "desconectado ha mais de 5 min avisa uma vez");
                ar.Online = true;

                avisos.Clear();
                ar.Estado["t_power"] = "0"; ar.Estado["f_temp_in"] = "29";
                Alertas.Conferir(ar, p, t0);
                Alertas.Conferir(ar, p, t0.AddMinutes(1));
                Igual(1, avisos.Count, "quente e desligado avisa uma vez");
                p.AlertaQuente = false;
                ar.Estado["f_temp_in"] = "26"; Alertas.Conferir(ar, p, t0.AddMinutes(2));
                ar.Estado["f_temp_in"] = "30"; Alertas.Conferir(ar, p, t0.AddMinutes(3));
                Igual(1, avisos.Count, "alerta desligado nas opcoes nao avisa");
                ar.Estado["t_power"] = "1"; ar.Estado["f_temp_in"] = "22";

                // Consumo estimado: 1 h ligado em Frio a 1100 W = 1,1 kWh.
                HistoricoDoAr.Limpar();
                DateTime ini = DateTime.Today.AddHours(10);
                for (int m = 0; m <= 60; m++)
                {
                    AmostraAr s = new AmostraAr();
                    s.Quando = ini.AddMinutes(m); s.Ligado = m < 60; s.Modo = 2; s.Ambiente = 24; s.Alvo = 23;
                    HistoricoDoAr.Acrescentar("pz", s);
                }
                SortedDictionary<DateTime, double[]> dias = HistoricoDoAr.PorDia("pz", DateTime.Today, DateTime.Today.AddHours(12), p, "008");
                Verdade(Math.Abs(dias[DateTime.Today][0] - 1) < 0.01 && Math.Abs(dias[DateTime.Today][1] - 1.1) < 0.01,
                    "1 h em Frio a 1100 W conta 1,1 kWh");
                HistoricoDoAr.Limpar();

                // Preferencias e timer persistem.
                ServicoCasa.Pref = new PreferenciasCasa();
                ServicoCasa.Pref.Tarifa = 0.92; ServicoCasa.Pref.LimiteQuente = 30;
                ServicoCasa.DefinirTimer("p1", 30);
                PreferenciasCasa lida = PreferenciasCasa.Carregar();
                Verdade(Math.Abs(lida.Tarifa - 0.92) < 1e-9 && lida.LimiteQuente == 30 && lida.DesligarPuid == "p1" &&
                    Math.Abs((new DateTime(lida.DesligarEmUtc, DateTimeKind.Utc) - DateTime.UtcNow).TotalMinutes - 30) < 1,
                    "tarifa, limite e horario do timer sobrevivem a reabrir");
                ServicoCasa.CancelarTimer();
                Verdade(PreferenciasCasa.Carregar().DesligarEmUtc == 0, "cancelar apaga o timer gravado");

                // Lista oficial do autodiagnostico.
                List<FalhaOficial> l = Diagnostico.Interpretar((Dictionary<string, object>)Json.Ler(
                    "{\"resultCode\":0,\"data\":{\"selfCheckList\":[{\"statusKey\":\"f_e_intemp\",\"statusValue\":\"1\",\"content\":\"Sensors\"," +
                    "\"fault\":\"Indoor room temperature sensor failure\",\"guideKey\":\"selfcheck_ac_solution_please_contact_after_sales_service_for_assistance\"," +
                    "\"guide\":\"Please contact after-sales service for assistance.\"}]}}"));
                Verdade(l.Count == 1 && l[0].Chave == "f_e_intemp" && l[0].Nome == T.CasaFalha("f_e_intemp", null) &&
                    l[0].Orientacao == T.CasaOrientacao("after_sales", ""), "falha oficial lida e traduzida");
            }
            finally
            {
                ServicoCasa.Notificar = notifAntes;
                ServicoCasa.Pref = new PreferenciasCasa();
                PreferenciasCasa.Arquivo = prefAntes;
                Alertas.Zerar();
            }
        }

        /// <summary>Recorte da resposta de get_property_list do ar 008/301 (docs/connectlife-ar-propriedades.json).</summary>
        private static Dictionary<string, MetaPropriedade> MetaDoArDeJanela()
        {
            string r = "{\"resultCode\":0,\"properties\":[" +
                "{\"propertyKey\":\"f_e_incom\",\"propertyValueList\":\"0,1\",\"propertyRW\":\"R\"}," +
                "{\"propertyKey\":\"f_temp_in\",\"propertyValueList\":\"0~37\uFF0C32~99\",\"propertyRW\":\"R\"}," +
                "{\"propertyKey\":\"t_beep\",\"propertyValueList\":\"0,1\",\"propertyRW\":\"RW\"}," +
                "{\"propertyKey\":\"t_device_info\",\"propertyValueList\":\"0,1\",\"propertyRW\":\"RW\"}," +
                "{\"propertyKey\":\"t_fan_speed\",\"propertyValueList\":\"2,3,4\",\"propertyRW\":\"RW\"}," +
                "{\"propertyKey\":\"t_power\",\"propertyValueList\":\"0,1\",\"propertyRW\":\"RW\"}," +
                "{\"propertyKey\":\"t_sleep\",\"propertyValueList\":\"0,1\",\"propertyRW\":\"RW\"}," +
                "{\"propertyKey\":\"t_temp\",\"propertyValueList\":\"16~30,61~86\",\"propertyRW\":\"RW\"}," +
                "{\"propertyKey\":\"t_temp_type\",\"propertyValueList\":\"0,1\",\"propertyRW\":\"RW\"}," +
                "{\"propertyKey\":\"t_work_mode\",\"propertyValueList\":\"0,2,3,5\",\"propertyRW\":\"RW\"}]}";
            return MetaPropriedade.DaResposta((Dictionary<string, object>)Json.Ler(r));
        }

        private static void CasaMetaDoModelo()
        {
            Secao("casa: o que o modelo declara");
            Dictionary<string, MetaPropriedade> m = MetaDoArDeJanela();
            Verdade(m["t_work_mode"].Grava && string.Join(",", m["t_work_mode"].Valores.ConvertAll<string>(delegate(int v) { return v.ToString(); }).ToArray()) == "0,2,3,5",
                "modos aceitos pelo modelo");
            Verdade(m["t_temp"].Faixas.Count == 2 && m["t_temp"].Faixas[0][0] == 16 && m["t_temp"].Faixas[0][1] == 30 && m["t_temp"].Faixas[1][1] == 86,
                "faixas de temperatura em °C e °F");
            Verdade(m["f_temp_in"].Faixas.Count == 2 && !m["f_temp_in"].Grava, "virgula chinesa separa faixas; f_ e so leitura");
            Verdade(MetaPropriedade.De((Dictionary<string, object>)Json.Ler("{\"propertyKey\":\"Actions\",\"propertyValueList\":\"0:reserved,1:poweroff,2:startrun\",\"propertyRW\":\"RW\"}")).Valores.Count == 3,
                "valores com nome (0:reserved) viram numeros");
            // Calibrado com o Thiago no ar de janela: 0 Ventilar, 2 Frio, 3 Desumidificar, 5 ECO.
            Igual(T.CasaVentilar, TabelaDoAr.Modo(0, "008"), "0 e Ventilar");
            Igual(T.CasaFrio, TabelaDoAr.Modo(2, "008"), "2 e Frio");
            Igual(T.CasaSeco, TabelaDoAr.Modo(3, "008"), "3 e Desumidificar");
            Igual(T.CasaEco, TabelaDoAr.Modo(5, "008"), "5 e ECO");
            Verdade(TabelaDoAr.IconeDoModo(5, "008") != null, "ECO tem icone proprio");

            Secao("casa: nome do modelo e icone centrado");
            Aparelho janela = new Aparelho();
            janela.Tipo = "008"; janela.CodigoDoModelo = "301"; janela.Modelo = "301单冷、无静音、无电功率显示";
            Igual("Hisense AW-12CR2RWGU00", janela.ModeloLegivel, "nome comercial do ar de janela do Thiago");
            janela.CodigoDoModelo = "999";
            Igual(T.CasaTipo("008", null), janela.ModeloLegivel, "modelo em chines sem nome conhecido vira o tipo em portugues");
            Aparelho lavadora = new Aparelho();
            lavadora.Tipo = "025"; lavadora.Modelo = "WD3S1114-LVT008-000";
            Igual("WD3S1114-LVT008-000", lavadora.ModeloLegivel, "modelo legivel da nuvem fica como veio");

            // O glifo de ligar centrado pelo desenho, nos tamanhos de 100%, 125% e 150%.
            foreach (float escala in new float[] { 1f, 1.25f, 1.5f })
                using (Font f = new Font(Ui.Tipo.Icones, 18f * escala))
                {
                    RectangleF area = new RectangleF(0, 0, 56 * escala, 56 * escala);
                    using (System.Drawing.Drawing2D.GraphicsPath gp = BotaoRedondo.Glifo("\uE7E8", f, area))
                    {
                        RectangleF b = gp.GetBounds();
                        float dx = (b.X + b.Width / 2) - area.Width / 2, dy = (b.Y + b.Height / 2) - area.Height / 2;
                        Verdade(Math.Abs(dx) < 0.5f && Math.Abs(dy) < 0.5f, "power centrado no circulo a " + (int)(escala * 100) + "%");
                    }
                }
            Igual(string.Format(T.CasaModoN, 7), TabelaDoAr.Modo(7, "008"), "valor desconhecido vai pelo numero");
            Igual(T.CasaVentoBaixo, TabelaDoAr.Ventilador(2, "008"), "ventilador 2 e o minimo no 008");
            Igual(T.CasaVentoAlto, TabelaDoAr.Ventilador(4, "008"), "ventilador 4 e o maximo no 008");
        }

        /// <summary>Filhos visiveis nao se cruzam e nao saem do cartao.</summary>
        private static void SemSobrepor(Control pai, string onde)
        {
            List<Control> vis = new List<Control>();
            foreach (Control c in pai.Controls) if (c.Visible) vis.Add(c);
            bool ok = true;
            for (int i = 0; i < vis.Count; i++)
            {
                if (vis[i].Right > pai.Width || vis[i].Bottom > pai.Height) ok = false;
                for (int j = i + 1; j < vis.Count; j++)
                    if (vis[i].Bounds.IntersectsWith(vis[j].Bounds)) ok = false;
            }
            Verdade(ok, "nada sobreposto nem cortado: " + onde);
        }

        private static List<Aparelho> Exemplo()
        {
            // O ar de janela do Thiago, como a nuvem o descreveu em 26/10/2026.
            Aparelho ar = new Aparelho();
            ar.Puid = "p1"; ar.DeviceId = "d1"; ar.Tipo = "008"; ar.CodigoDoModelo = "301"; ar.Apelido = "Gil do Frio";
            ar.Modelo = "301"; ar.Comodo = "Escritório"; ar.Online = true;
            string[] kv = { "t_power", "1", "t_temp", "23", "f_temp_in", "22", "t_work_mode", "5", "t_fan_speed", "2",
                "t_sleep", "0", "t_temp_type", "0", "f_e_incom", "0", "f_e_push", "0" };
            for (int i = 0; i < kv.Length; i += 2) ar.Estado[kv[i]] = kv[i + 1];
            ar.Meta = MetaDoArDeJanela();

            Aparelho lav = new Aparelho();
            lav.Puid = "p2"; lav.DeviceId = "d2"; lav.Tipo = "003"; lav.Apelido = "Lava e seca";
            lav.Modelo = "WD3S"; lav.Comodo = "Lavanderia"; lav.Online = true;
            string[] kl = { "DeviceStatus", "2", "CurrentProgramPhase", "2", "Remaining_time_in_minutes", "48",
                "Program", "Algodão", "Temperature", "40", "SpinSpeed", "1200", "DoorStatus", "0", "ChildLockActive", "1" };
            for (int i = 0; i < kl.Length; i += 2) lav.Estado[kl[i]] = kl[i + 1];

            return new List<Aparelho> { lav, ar };
        }
    }
}
