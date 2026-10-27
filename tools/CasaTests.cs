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
            CasaLavadora(root);
            PerifericosOcultos(root);
            CasaWidget();
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
            CasaNoWidget.CaminhoAntigo = System.IO.Path.Combine(pasta, "widget-casa.ini");
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
                            Verdade(!ks.Contains("t_sleep") && !ks.Contains("t_beep") && !ks.Contains("t_device_info") && !ks.Contains("t_eco"),
                                "interruptores do aparelho: nenhum repetido; nada que o modelo nao tenha");
                            // Item 3: cena com tempo agenda o retorno; mexer na mao cancela.
                            ServicoCasa.CancelarRetorno();
                            CenaCasa ct = CenaCasa.Padrao()[1]; ct.Duracao = 30;
                            typeof(CartaoDeAparelho).GetMethod("MandarVarios", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
                                new Type[] { typeof(Dictionary<string, int>), typeof(bool) }, null).Invoke(ar, new object[] { ct.Comando(ar.Aparelho), true });
                            ServicoCasa.AgendarRetorno(ar.Aparelho.Puid, ct.Duracao);
                            ar.Atualizar();
                            casa.Arranjar();
                            PainelDoTimer pt = (PainelDoTimer)Field(ar, "_timer");
                            Verdade(pt.RetornoAtivo && ((Label)Field(pt, "_retorno")).Visible, "contagem do retorno aparece no painel " + sufixo);
                            foreach (Secao s in ar.Secoes) if (s.Visible && s.Parent == ar) SemSobrepor(s, "com retorno " + s.Titulo + " " + sufixo);
                            InvokePrivate(ar, "Mandar", "t_temp", 25);
                            Verdade(ServicoCasa.RetornoEm == null, "mexer na mao cancela o retorno " + sufixo);
                            ar.Aparelho.Estado["t_temp"] = "23"; ar.Aparelho.Estado["t_work_mode"] = "5"; ar.Aparelho.Estado["t_fan_speed"] = "2"; ar.Aparelho.Estado["t_sleep"] = "0";
                            ar.Atualizar();
                            casa.Arranjar();                            // Item 2: botao de noturno no painel, na linha do liga/desliga.
                            BotaoRedondo bn = ar.BotaoNoturno;
                            Verdade(bn.Visible && !bn.Aceso && bn.Right <= ar.Width - Ui.S(18) && bn.Bottom < ((Blocos)Field(ar, "_modo")).Top,
                                "botao de noturno visivel, apagado, na linha do liga/desliga " + sufixo);
                            List<ComandoEventArgs> cmds = new List<ComandoEventArgs>();
                            EventHandler<ComandoEventArgs> pega = delegate(object s2, ComandoEventArgs e2) { cmds.Add(e2); };
                            ar.Comando += pega;
                            InvokePrivate(bn, "OnClick", EventArgs.Empty);
                            ar.Comando -= pega;
                            Verdade(cmds.Count == 1 && cmds[0].Propriedades.Count == 1 && cmds[0].Propriedades["t_sleep"] == "1" && bn.Aceso,
                                "clique manda so t_sleep=1 e acende " + sufixo);
                            ar.Aparelho.Estado["t_sleep"] = "0";
                            ar.Atualizar();
                            Verdade(((Toggle)Field(ar, "_semBipe")).Parent == ar.Secoes[0] && ar.Secoes[0].Parent == casa.Ajustes, "o bipe virou a preferencia Comandos sem bipe, em Ajustes");

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

                            // Painel: no cartao ficam Cenas e Timer, lado a lado na janela larga;
                            // Funcoes e Preferencias moram na aba Ajustes.
                            Secao[] secoes = ar.Secoes;
                            foreach (Secao s in secoes)
                                if (s.Visible) SemSobrepor(s, "secao " + s.Titulo + " " + sufixo);
                            Verdade(secoes[0].Parent == casa.Ajustes && secoes[1].Parent == casa.Ajustes,
                                "Funcoes e Preferencias hospedadas na aba Ajustes " + sufixo);
                            Verdade(secoes[2].Visible && secoes[3].Visible && secoes[2].Parent == ar && secoes[3].Parent == ar,
                                "Cenas e Timer no painel do ar " + sufixo);
                            Verdade(secoes[2].Top >= menos.Bottom && secoes[3].Bottom <= ar.Height, "secoes embaixo do painel, dentro do cartao " + sufixo);
                            if (largura == 1300)
                                Verdade(secoes[2].Top == secoes[3].Top && secoes[2].Height == secoes[3].Height, "Cenas e Timer alinhados na janela larga");
                            else
                                Verdade(secoes[3].Left == secoes[2].Left && secoes[3].Top > secoes[2].Bottom, "uma coluna na janela estreita");
                            Verdade(!casa.Ajustes.Visible, "aba Painel nao mostra Ajustes");
                            Verdade(ar.SecaoRetorno.Parent == casa.Ajustes, "padrao das cenas mora em Ajustes " + sufixo);

                            // Aba Ajustes: Funcoes, Preferencias, Alertas, Estimativa e Conta.
                            casa.Aba = 1;
                            casa.Arranjar();
                            Verdade(casa.Ajustes.Visible && !ar.Visible && !casa.Cartoes[1].Visible && (casa.Historico == null || !casa.Historico.Visible),
                                "aba Ajustes esconde os cartoes " + sufixo);
                            foreach (Secao s in casa.Ajustes.Secoes)
                                if (s.Visible) SemSobrepor(s, "ajustes " + s.Titulo + " " + sufixo);
                            SemSobrepor(casa.Ajustes, "aba Ajustes " + sufixo);
                            Verdade(casa.Ajustes.Right <= casa.Width - Pagina.LarguraDaBarra, "ajustes cabem na largura " + sufixo);
                            Segmented unidade = (Segmented)Field(ar, "_unidade");
                            Label lbUnidade = (Label)Field(ar, "_lbUnidade");
                            Verdade(unidade.Parent == secoes[1] && unidade.Top == lbUnidade.Top && unidade.Height == lbUnidade.Height &&
                                unidade.Right == secoes[1].Width - MhiagosControl.Secao.Margem && lbUnidade.Left == MhiagosControl.Secao.Margem,
                                "unidade: rotulo a esquerda e seletor a direita, mesma linha " + sufixo);
                            if (largura == 1300)
                                Verdade(secoes[0].Top == secoes[1].Top && secoes[0].Height == secoes[1].Height, "Funcoes e Preferencias alinhadas");
                            CaptureEvolution(form, "casa-ajustes-" + sufixo);
                            casa.Aba = 0;
                            casa.Arranjar();
                            Verdade(ar.Visible && !casa.Ajustes.Visible, "volta ao Painel " + sufixo);
                            BotaoChave chave = ar.Chave;
                            Verdade(chave.Visible && chave.Bottom <= Ui.S(64) && chave.Right < ar.Width - Ui.S(18) - Ui.S(60),
                                "chave do diagnostico no cabecalho, antes do selo " + sufixo);
                            CartaoHistoricoDoAr hist = casa.Historico;
                            Verdade(hist != null && hist.Visible && hist.Top > ar.Bottom && hist.Bottom < casa.Cartoes[1].Top,
                                "historico e consumo logo abaixo do ar " + sufixo);
                            if (hist != null) SemSobrepor(hist, "historico " + sufixo);
                            CaptureEvolution(form, "casa-aparelhos-" + sufixo);
                            if (largura == 1300 && hist != null)
                            {
                                // Item 4: o cartao de consumo comeca fechado, abre e lembra.
                                Verdade(!hist.Aberto && hist.Height <= hist.TitleHeight + 2, "consumo do ar comeca fechado " + sufixo);
                                hist.Alternar(false);
                                casa.Arranjar();
                                Verdade(hist.Aberto && hist.Height > Ui.S(300) && ServicoCasa.Pref.ConsumoArAberto, "consumo abre e lembra " + sufixo);
                                SemSobrepor(hist, "historico aberto " + sufixo);
                                Verdade(hist.Bottom < casa.Cartoes[1].Top, "lavadora desce com o consumo aberto " + sufixo);
                                CaptureEvolution(hist, "casa-historico-" + sufixo);
                                hist.Alternar(false);
                                casa.Arranjar();
                                Verdade(!hist.Aberto && !ServicoCasa.Pref.ConsumoArAberto, "fecha de novo " + sufixo);
                                ServicoCasa.DefinirTimer("p1", 60);
                                ar.Atualizar();
                                ar.AlternarDiagnostico();
                                casa.Arranjar();
                                foreach (Secao s in ar.Secoes) if (s.Visible) SemSobrepor(s, "com timer e diagnostico " + s.Titulo);
                                Verdade(ar.Chave.Aberto && ar.Secoes[5].Visible, "a chave abre o diagnostico");
                                CaptureEvolution(ar, "casa-ar-secoes-" + sufixo);
                                ServicoCasa.CancelarTimer();
                                ar.AlternarDiagnostico();

                                // Editor de cenas: abre a primeira, sem sobrepor, e fecha.
                                ar.AbrirEditor(0);
                                casa.Arranjar();
                                Verdade(ar.Secoes[4].Visible && ar.Cenas.Editando, "editor de cena aberto");
                                SemSobrepor(ar.Editor, "editor de cena " + sufixo);
                                SemSobrepor(ar.Secoes[4], "secao do editor " + sufixo);
                                CaptureEvolution(ar, "casa-ar-editor-" + sufixo);
                                ar.Editor.GetType();
                                InvokePrivate(ar, "AlternarEdicao");
                                ar.Atualizar();
                                casa.Arranjar();
                                Verdade(!ar.Secoes[4].Visible && !ar.Cenas.Editando, "concluir fecha o editor");
                            }
                        }

                        // Lava e seca: secando, concluida e desconectada, nas duas larguras.
                        foreach (string estado in new string[] { "secando", "concluido", "offline" })
                        {
                            List<Aparelho> l = Exemplo();
                            l[0] = Lavadora(estado);
                            casa.MostrarSemNuvem(l);
                            foreach (int largura in new int[] { 1300, 880 })
                            {
                                form.Size = new Size(largura, 760);
                                casa.Arranjar();
                                string sufixo = estado + "-" + language + "-" + largura;
                                CartaoDaLavadora lv = null;
                                foreach (CartaoDeAparelho cd in casa.Cartoes) if (cd is CartaoDaLavadora) lv = (CartaoDaLavadora)cd;
                                Verdade(lv != null && lv.Right <= casa.Width - Pagina.LarguraDaBarra, "lavadora cabe na largura " + sufixo);
                                if (lv == null) continue;
                                SemSobrepor(lv, "lavadora " + sufixo);
                                AnelDoCiclo anel = (AnelDoCiclo)Field(lv, "_anel");
                                Label titulo = (Label)Field(lv, "_titulo");
                                if (estado == "secando")
                                    Verdade(anel.Grande == "1 h 25" && titulo.Text == T.LavFaseSecagem && ((EtapasDoCiclo)Field(lv, "_etapas")).Atual == 3,
                                        "secando: 1 h 25 restantes, etapa Secar " + sufixo);
                                EtapasDoCiclo et = (EtapasDoCiclo)Field(lv, "_etapas");
                                if (et.Visible)
                                {
                                    Rectangle[] rr = et.Rotulos();
                                    float[] cx = et.Centros();
                                    bool cabe = true;
                                    for (int k = 0; k < rr.Length; k++)
                                    {
                                        int largura2 = TextRenderer.MeasureText(et.Nomes[k], k == et.Atual ? Ui.FontMed : Ui.FontSmall, Size.Empty, TextFormatFlags.NoPadding).Width;
                                        if (rr[k].Left < 0 || rr[k].Right > et.Width || largura2 > rr[k].Width) cabe = false;
                                        if (cx[k] - Ui.S(7) - Ui.S(6) < 0 || cx[k] + Ui.S(7) + Ui.S(6) > et.Width) cabe = false;
                                    }
                                    Verdade(cabe && et.Right <= lv.Width - Ui.S(18), "etapas: marcadores e rotulos inteiros dentro do cartao " + sufixo);
                                    if (estado == "secando" && largura == 1300)
                                        using (Bitmap bm = new Bitmap(et.Width, et.Height))
                                        {
                                            et.DrawToBitmap(bm, new Rectangle(0, 0, et.Width, et.Height));
                                            string dir = Environment.GetEnvironmentVariable("MHIAGOS_VISUAL_DIR");
                                            if (!string.IsNullOrEmpty(dir)) bm.Save(System.IO.Path.Combine(dir, "casa-etapas-" + sufixo + ".png"));
                                        }
                                }
                                if (estado == "offline")
                                    Verdade(anel.Apagado && titulo.Text == T.LavDesconectada, "desconectada: anel apagado " + sufixo);
                                // Item 7: chave do diagnostico no cabecalho da lavadora.
                                Verdade(lv.Chave.Visible && lv.Chave.Bottom <= Ui.S(64) && lv.Chave.Right < lv.Width - Ui.S(18) - Ui.S(60),
                                    "chave do diagnostico na lavadora " + sufixo);
                                lv.AlternarDiagnostico();
                                casa.Arranjar();
                                SemSobrepor(lv, "lavadora com diagnostico " + sufixo);
                                Verdade(((Label)Field(lv, "_diag")).Visible && ((Label)Field(lv, "_diag")).Text.Length > 0, "diagnostico aberto " + sufixo);
                                if (estado == "secando" && largura == 1300) CaptureEvolution(lv, "casa-lavadora-diagnostico-" + sufixo);
                                lv.AlternarDiagnostico();
                                casa.Arranjar();                                // Item 5: consumo e historico da lavadora, recolhivel, comeca fechado.
                                Verdade(!lv.ConsumoAberto && !((Label)Field(lv, "_consumo")).Visible && lv.CabecalhoDoConsumo.Visible,
                                    "consumo da lavadora comeca fechado " + sufixo);
                                CaptureEvolution(lv, "casa-lavadora-" + sufixo);
                                lv.AlternarConsumo(false);
                                casa.Arranjar();
                                Verdade(lv.ConsumoAberto && ((Label)Field(lv, "_consumo")).Visible && ServicoCasa.Pref.ConsumoLavadoraAberto,
                                    "consumo da lavadora abre e lembra " + sufixo);
                                SemSobrepor(lv, "lavadora com consumo aberto " + sufixo);
                                if (estado == "secando") Verdade(((Label)Field(lv, "_consumo")).Text.Contains("168"), "agua do ciclo na secao de consumo");
                                CaptureEvolution(lv, "casa-lavadora-consumo-" + sufixo);
                                lv.AlternarConsumo(false);
                                casa.Arranjar();
                            }
                        }
                        casa.MostrarSemNuvem(Exemplo());

                        // Item 8: a casa na Visao geral, entre perifericos e especificacoes.
                        for (int i = 0; i < nav.Items.Count; i++) if (nav.Items[i].Key == "visao") nav.SelectedIndex = i;
                        foreach (int largura in new int[] { 1300, 880 })
                        {
                            form.Size = new Size(largura, 900);
                            InvokePrivate(form, "ArranjarVisaoGeral");
                            string sufixo = "visao-" + language + "-" + largura;
                            ResumoDaCasa rc = (ResumoDaCasa)Field(form, "_cardCasa");
                            Control perif = (Control)Field(form, "_cardPerif"), specs = (Control)Field(form, "_cardSpecs");
                            rc.Puxar();
                            Verdade(rc.Visible && rc.Ar != null && rc.Lavadora != null, "resumo com o ar e a lavadora " + sufixo);
                            Verdade(rc.Top >= perif.Bottom && rc.Bottom <= specs.Top && rc.Left == perif.Left && rc.Width == perif.Width,
                                "resumo entre perifericos e especificacoes, mesma largura " + sufixo);
                            SemSobrepor(rc, "resumo da casa " + sufixo);
                            CaptureEvolution(form, "casa-" + sufixo);
                            InvokePrivate(rc.BotaoLiga, "OnClick", EventArgs.Empty);
                            Verdade(rc.UltimoComando != null && rc.UltimoComando.Count == 1 && rc.UltimoComando["t_power"] == "0",
                                "liga/desliga no resumo manda t_power " + sufixo);
                            rc.Ar.Estado["t_power"] = "1";
                            rc.Puxar();
                        }
                        // Item 10: Visao geral nova, atras da opcao (desligada por padrao).
                        Config cfgForm = (Config)Field(form, "_cfg");
                        Verdade(!cfgForm.VisaoGeralNova && !((Control)Field(form, "_faixaStatus")).Visible, "Visao geral nova desligada por padrao " + language);
                        cfgForm.VisaoGeralNova = true;
                        foreach (int largura in new int[] { 1300, 880 })
                        {
                            form.Size = new Size(largura, 900);
                            InvokePrivate(form, "ArranjarVisaoGeral");
                            string sufixo = "nova-" + language + "-" + largura;
                            Control pg = (Control)Field(form, "_pgVisao");
                            FaixaDeStatus fx = (FaixaDeStatus)Field(form, "_faixaStatus");
                            Verdade(fx.Visible && fx.Chips.Count == 4, "faixa de status com quatro chips " + sufixo);
                            ResumoDaCasa rcn = (ResumoDaCasa)Field(form, "_cardCasa");
                            Verdade(rcn.Top > fx.Bottom && rcn.Top < ((Control)Field(form, "_cardCooler")).Top, "casa logo abaixo da faixa, acima do cooler " + sufixo);
                            List<Control> vis = new List<Control>();
                            foreach (Control ctl in pg.Controls) if (ctl.Visible) vis.Add(ctl);
                            bool ok = true;
                            for (int i = 0; i < vis.Count; i++)
                            {
                                if (vis[i].Right > pg.Width || vis[i].Left < 0) ok = false;
                                for (int j = i + 1; j < vis.Count; j++) if (vis[i].Bounds.IntersectsWith(vis[j].Bounds)) ok = false;
                            }
                            Verdade(ok, "Visao geral nova sem sobreposicao " + sufixo);
                            for (int i = 0; i < fx.Chips.Count; i++)
                            {
                                string v = fx.Chips[i].Valor();
                                Verdade(v != null && v.Length > 0, "chip " + fx.Chips[i].Chave + " com texto " + sufixo);
                            }
                            CaptureEvolution(form, "visao-" + sufixo);
                            // Cooler recolhido: vira barra de titulo na largura toda, sem sobrepor nada.
                            Card cooler = (Card)Field(form, "_cardCooler");
                            cfgForm.VisaoCoolerRecolhido = true;
                            InvokePrivate(form, "ArranjarVisaoGeral");
                            Verdade(cooler.Height <= cooler.TitleHeight + 2 && !((Control)Field(form, "_vgPreview")).Visible, "cooler recolhido so com o titulo " + sufixo);
                            Verdade(((Control)Field(form, "_vgRecolher")).Visible, "seta de recolher visivel " + sufixo);
                            vis.Clear();
                            foreach (Control ctl in pg.Controls) if (ctl.Visible) vis.Add(ctl);
                            ok = true;
                            for (int i = 0; i < vis.Count; i++)
                            {
                                if (vis[i].Right > pg.Width || vis[i].Left < 0) ok = false;
                                for (int j = i + 1; j < vis.Count; j++) if (vis[i].Bounds.IntersectsWith(vis[j].Bounds)) ok = false;
                            }
                            Verdade(ok, "cooler recolhido sem sobreposicao " + sufixo);
                            CaptureEvolution(form, "visao-" + sufixo + "-cooler-recolhido");
                            cfgForm.VisaoCoolerRecolhido = false;
                            InvokePrivate(form, "ArranjarVisaoGeral");
                            Verdade(cooler.Height > cooler.TitleHeight * 3 && ((Control)Field(form, "_vgPreview")).Visible, "cooler reaberto " + sufixo);
                        }
                        cfgForm.VisaoGeralNova = false;
                        InvokePrivate(form, "ArranjarVisaoGeral");
                        Verdade(!((Control)Field(form, "_faixaStatus")).Visible, "desligar volta a Visao geral classica " + language);
                        Verdade(!((Control)Field(form, "_vgRecolher")).Visible && ((Control)Field(form, "_vgPreview")).Visible, "classica sem seta de recolher " + language);
                        ServicoCasa.PararSimulacao();
                        ResumoDaCasa rs = (ResumoDaCasa)Field(form, "_cardCasa");
                        rs.Puxar();
                        Verdade(rs.Ar == null && ((FlatBtn)Field(rs, "_convite")).Visible, "sem conta: convite para a pagina Casa " + language);
                        SemSobrepor(rs, "resumo sem conta " + language);
                        CaptureEvolution(form, "casa-visao-sem-conta-" + language);
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
                List<CenaCasa> padrao = CenaCasa.Padrao();
                Dictionary<string, int> dormir = padrao[0].Comando(ar), gelar = padrao[1].Comando(ar), eco = padrao[2].Comando(ar);
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
                Verdade(padrao[2].Comando(split) == null, "sem ECO no modelo, a cena Economico fica indisponivel");

                // Cenas editadas: gravar, ler de novo, manter, desligar e em °F.
                CenaCasa minha = new CenaCasa();
                minha.Nome = "Leitura | noite"; minha.Icone = "lua"; minha.Modo = 3; minha.Temp = 26; minha.Vento = 3; minha.Noturno = -1;
                CenaCasa volta = CenaCasa.DeLinha(minha.Linha());
                Verdade(volta != null && volta.Nome == "Leitura / noite" && volta.Icone == "lua" && volta.Modo == 3 && volta.Temp == 26 &&
                    volta.Vento == 3 && volta.Noturno == -1 && !volta.Desligar, "cena editada sobrevive a gravacao (| no nome vira /)");
                Dictionary<string, int> pm = minha.Comando(ar);
                Verdade(pm != null && pm["t_work_mode"] == 3 && pm["t_temp"] == 26 && pm["t_fan_speed"] == 3 && !pm.ContainsKey("t_sleep"),
                    "Manter deixa o noturno de fora do comando");
                CenaCasa desliga = new CenaCasa(); desliga.Nome = "Sair"; desliga.Desligar = true;
                Dictionary<string, int> pd = desliga.Comando(ar);
                Verdade(pd != null && pd.Count == 1 && pd["t_power"] == 0, "cena de desligar manda so t_power=0");
                CenaCasa invalida = new CenaCasa(); invalida.Nome = "X"; invalida.Modo = 4;
                Verdade(invalida.Comando(ar) == null, "modo que o modelo nao tem deixa a cena indisponivel");
                ar.Estado["t_temp_type"] = "1";
                Dictionary<string, int> pf = padrao[0].Comando(ar);
                Verdade(pf != null && pf["t_temp"] == 75, "em °F, 24 °C vira 75 °F");
                ar.Estado["t_temp_type"] = "0";

                ServicoCasa.Pref = new PreferenciasCasa();
                ServicoCasa.Pref.Cenas = new List<CenaCasa> { volta, desliga };
                ServicoCasa.Pref.Salvar();
                List<CenaCasa> lidas = PreferenciasCasa.Carregar().Cenas;
                Verdade(lidas.Count == 2 && lidas[0].Nome == "Leitura / noite" && lidas[1].Desligar, "cenas gravadas em casa.ini, na ordem");
                System.IO.File.Delete(PreferenciasCasa.Arquivo);
                Verdade(PreferenciasCasa.Carregar().Cenas.Count == 3, "sem cenas gravadas, as de fabrica");

                // Item 3: cena por tempo e retorno ao padrao.
                CenaCasa porTempo = padrao[1].Copia(); porTempo.Duracao = 30;
                CenaCasa voltaT = CenaCasa.DeLinha(porTempo.Linha());
                Verdade(voltaT != null && voltaT.Duracao == 30, "duracao da cena gravada (8o campo)");
                Verdade(CenaCasa.DeLinha("Velha|lua|0|-1|24|-2|1").Duracao == 0, "linha antiga, de 7 campos, fica sem retorno");
                Verdade(porTempo.Resumo(ar).Contains("30"), "resumo mostra a duracao");
                PreferenciasCasa pr = new PreferenciasCasa();
                Dictionary<string, int> rc = ServicoCasa.PadraoDeRetorno(pr).Comando(ar);
                Verdade(rc != null && rc["t_power"] == 1 && rc["t_work_mode"] == 2 && rc["t_temp"] == 24 && rc["t_fan_speed"] == 2 && rc["t_sleep"] == 0,
                    "padrao de retorno: 24 °C, Frio, ventilador baixo, sem noturno");
                Dictionary<string, string> rcs = new Dictionary<string, string>();
                foreach (KeyValuePair<string, int> kv in rc) rcs[kv.Key] = kv.Value.ToString();
                List<Dictionary<string, string>> rp = ServicoCasa.Partes(rcs);
                Verdade(rp.Count == 2 && rp[1].Count == 1 && rp[1].ContainsKey("t_sleep"), "no retorno o noturno tambem vai sozinho");
                ServicoCasa.Pref = new PreferenciasCasa();
                ServicoCasa.AgendarRetorno("p1", 30);
                PreferenciasCasa lr = PreferenciasCasa.Carregar();
                Verdade(lr.RetornoPuid == "p1" && Math.Abs((new DateTime(lr.RetornoEmUtc, DateTimeKind.Utc) - DateTime.UtcNow).TotalMinutes - 30) < 1,
                    "retorno agendado sobrevive a reabrir");
                ServicoCasa.DefinirTimer("p1", 60);
                Verdade(ServicoCasa.RetornoEm == null, "timer de desligar cancela o retorno");
                ServicoCasa.CancelarTimer();
                ServicoCasa.AgendarRetorno("p1", 15);
                ServicoCasa.CancelarRetorno();
                Verdade(PreferenciasCasa.Carregar().RetornoEmUtc == 0, "cancelar o retorno apaga o gravado");

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

            return new List<Aparelho> { Lavadora("secando"), ar };
        }

        /// <summary>
        /// A lava e seca do Thiago (025), com os valores lidos em 26/10/2026 as
        /// 21:20 ("secando": programa 7, fase 8, 85 de 389 min), ou o ciclo
        /// concluido, ou desconectada.
        /// </summary>
        private static Aparelho Lavadora(string como)
        {
            Aparelho lav = new Aparelho();
            lav.Puid = "p2"; lav.DeviceId = "d2"; lav.Tipo = "025"; lav.Apelido = "Josefina Roupa Limpa";
            lav.Modelo = "WD3S1114-LVT008-000"; lav.Comodo = "Lavanderia"; lav.Online = como != "offline";
            string[] kl = { "machine_status", "2", "Current_program_phase", "8", "Selected_program_ID", "7",
                "Selected_program_remaining_time_in_minutes", "85", "Selected_program_total_running_time_in_minutes", "389",
                "Selected_program_total_time_in_minutes", "389", "temperature", "9", "Spin_speed_rpm", "14", "RinseNum", "3",
                "ExtraRinseNum", "1", "dry_time", "2", "Soil_Lever", "0", "SoilLeverFlag", "0", "Door_status", "3", "Child_lock", "0",
                "Water_consumption_int", "168", "Water_consumption_decimal", "13", "Electricit_consumption_int", "2",
                "Electricit_consumption_decimal", "48", "error_code", "0", "DrumCleanFlag", "0", "DelayEndTime", "0", "DelayEndTime_Minute", "0" };
            for (int i = 0; i < kl.Length; i += 2) lav.Estado[kl[i]] = kl[i + 1];
            if (como == "concluido")
            {
                lav.Estado["machine_status"] = "1"; lav.Estado["Current_program_phase"] = "10";
                lav.Estado["Selected_program_remaining_time_in_minutes"] = "0";
            }
            return lav;
        }

        /// <summary>Item 9: widget da casa (configuracao, pintura e cliques), sem mostrar janela.</summary>
        private static void CasaWidget()
        {
            Secao("casa: dentro do widget de bateria");
            string pasta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mhiagos-widget-casa-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(pasta);
            string antesVidro = ConfigDoWidget.Caminho, antesCasa = CasaNoWidget.CaminhoAntigo;
            ConfigDoWidget.Caminho = System.IO.Path.Combine(pasta, "widget.ini");
            CasaNoWidget.CaminhoAntigo = System.IO.Path.Combine(pasta, "widget-casa.ini");
            try
            {
                Verdade(new ConfigDoWidget().Casa && ConfigDoWidget.DeTexto("ativo=1").Casa, "casa no widget ligada por padrao (tambem num widget.ini antigo)");
                Verdade(!ConfigDoWidget.DeTexto("casa=0").Casa, "casa desligada fica desligada");
                Verdade(!new ConfigDoWidget().NaFrente, "widget fora do sempre na frente por padrao");

                // migracao do widget separado
                string antigo = CasaNoWidget.CaminhoAntigo;
                System.IO.File.WriteAllText(antigo, "ativo=1\nnafrente=1\nx=2517\ny=847\n");
                ConfigDoWidget m = new ConfigDoWidget(); m.Ativo = false; m.Casa = false;
                Verdade(CasaNoWidget.Migrar(m) && m.Ativo && m.Casa && !m.NaFrente, "widget da casa ligado migra: liga o de bateria com a casa, sem trazer o na frente");
                Verdade(!System.IO.File.Exists(antigo) && System.IO.File.Exists(antigo + ".migrado"), "widget-casa.ini vira .migrado, sem perder o arquivo");
                Verdade(!CasaNoWidget.Migrar(m), "a migracao nao se repete");
                System.IO.File.WriteAllText(antigo, "ativo=0\n");
                ConfigDoWidget m2 = new ConfigDoWidget(); m2.Ativo = false;
                Verdade(!CasaNoWidget.Migrar(m2) && !m2.Ativo, "widget da casa desligado nao liga nada");

                string dir = Environment.GetEnvironmentVariable("MHIAGOS_VISUAL_DIR");
                Periferico x11 = new Periferico("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 41, false, EstadoPeriferico.Normal);
                Periferico g325 = new Periferico("G325", TipoPeriferico.Headset, FontePeriferico.GHub, 72, false, EstadoPeriferico.Normal);
                ServicoCasa.Simular(Exemplo());
                using (ItensDaCasa casa = new ItensDaCasa())
                {
                    Verdade(casa.Ar != null && casa.Lavadora != null && casa.Itens().Length == 2, "itens da casa: ar e lavadora secando");
                    foreach (LayoutDoWidget formato in new LayoutDoWidget[] { LayoutDoWidget.Aneis, LayoutDoWidget.Lista })
                    {
                        string nf = formato == LayoutDoWidget.Aneis ? "aneis" : "lista";
                        foreach (float tela in new float[] { 1f, 1.25f, 1.5f })
                        {
                            float s = tela * PintorDoWidget.Ampliacao;
                            string sufixo = nf + "-" + (int)(tela * 100);
                            // 2 perifericos + ar + lavadora em ciclo
                            Verdade(QuadroCabe(new Periferico[] { g325, x11 }, casa, formato, s, dir, "widget-unico-" + sufixo + "-2per-ar-lavadora"),
                                "2 perifericos, ar e lavadora sem corte nem sobreposicao, " + sufixo);
                            Size com = Tamanho(new Periferico[] { x11 }, casa, formato, s);
                            // 1 periferico + ar (lavadora concluida sai)
                            casa.Lavadora.Estado["machine_status"] = "1"; casa.Lavadora.Estado["Current_program_phase"] = "10";
                            Verdade(casa.Itens().Length == 1, "lavadora concluida sai do widget " + sufixo);
                            Verdade(QuadroCabe(new Periferico[] { x11 }, casa, formato, s, dir, "widget-unico-" + sufixo + "-1per-ar"),
                                "1 periferico e ar sem corte nem sobreposicao, " + sufixo);
                            Size sem = Tamanho(new Periferico[] { x11 }, casa, formato, s);
                            Verdade(sem.Height == Tamanho(new Periferico[] { x11 }, null, formato, s).Height || formato == LayoutDoWidget.Lista,
                                "nos aneis a casa nao muda a altura " + sufixo);
                            if (formato == LayoutDoWidget.Lista) Verdade(com.Height > sem.Height && com.Width == sem.Width, "lista cresce uma linha com a lavadora " + sufixo);
                            else Verdade(com.Width > sem.Width && com.Height == sem.Height, "aneis crescem um anel com a lavadora " + sufixo);
                            casa.Lavadora.Estado["machine_status"] = "2"; casa.Lavadora.Estado["Current_program_phase"] = "8";
                            // so a casa (sem perifericos)
                            Verdade(QuadroCabe(new Periferico[0], casa, formato, s, dir, "widget-unico-" + sufixo + "-so-casa"), "so a casa sem corte, " + sufixo);
                            // so perifericos (casa desligada)
                            casa.Ativa = false;
                            Verdade(casa.Itens().Length == 0 && Tamanho(new Periferico[] { g325, x11 }, casa, formato, s) == Tamanho(new Periferico[] { g325, x11 }, null, formato, s),
                                "casa desligada: o widget de bateria de sempre, " + sufixo);
                            casa.Ativa = true;
                        }
                        // estados do ar e da lavadora
                        float s1 = PintorDoWidget.Ampliacao;
                        casa.Ar.Estado["t_power"] = "0";
                        Verdade(QuadroCabe(new Periferico[] { x11 }, casa, formato, s1, dir, "widget-unico-" + nf + "-ar-desligado"), "ar desligado " + nf);
                        casa.Ar.Estado["t_power"] = "1";
                        casa.Lavadora.Estado["machine_status"] = "3";
                        Verdade(casa.Itens().Length == 2 && QuadroCabe(new Periferico[] { x11 }, casa, formato, s1, dir, "widget-unico-" + nf + "-pausada"), "lavadora pausada aparece " + nf);
                        casa.Lavadora.Estado["machine_status"] = "4"; casa.Lavadora.Estado["error_code"] = "5";
                        Verdade(casa.Itens().Length == 2 && QuadroCabe(new Periferico[] { x11 }, casa, formato, s1, null, null), "lavadora com erro aparece " + nf);
                        casa.Lavadora.Estado["machine_status"] = "2"; casa.Lavadora.Estado["error_code"] = "0";
                        casa.Lavadora.Online = false;
                        Verdade(casa.Itens().Length == 1, "lavadora desconectada sai " + nf);
                        casa.Lavadora.Online = true;
                        casa.Lavadora.Estado["machine_status"] = "0"; casa.Lavadora.Estado["Current_program_phase"] = "0";
                        Verdade(casa.Itens().Length == 1, "lavadora desligada sai " + nf);
                        casa.Lavadora.Estado["machine_status"] = "2"; casa.Lavadora.Estado["Current_program_phase"] = "8";
                    }
                    // clique no liga/desliga
                    QuadroCabe(new Periferico[] { x11 }, casa, LayoutDoWidget.Aneis, 1f, null, null);
                    casa.Clicar(2);
                    Verdade(casa.UltimoComando != null && casa.UltimoComando.Count == 1 && casa.UltimoComando["t_power"] == "0", "liga/desliga no widget manda t_power");
                    casa.Ar.Estado["t_power"] = "1";
                    Verdade(casa.Zona(Point.Round(Centro(casa.ZonaLiga))) == 2 && casa.Zona(new Point(1, 1)) == -1, "zonas da casa: botao e fora");
                    // crescer e encolher sem mover: o canto perto da borda fica parado
                    Size a = Tamanho(new Periferico[] { x11 }, casa, LayoutDoWidget.Aneis, 1f);
                    casa.Lavadora.Estado["machine_status"] = "1"; casa.Lavadora.Estado["Current_program_phase"] = "10";
                    Size b = Tamanho(new Periferico[] { x11 }, casa, LayoutDoWidget.Aneis, 1f);
                    casa.Lavadora.Estado["machine_status"] = "2"; casa.Lavadora.Estado["Current_program_phase"] = "8";
                    Rectangle area = new Rectangle(0, 0, 1920, 1040);
                    Rectangle embaixo = new Rectangle(new Point(1920 - 48 - b.Width, 1040 - 48 - b.Height), b);
                    Point p = WidgetDeBateria.AncorarCrescimento(embaixo, a, area);
                    Verdade(p.X + a.Width == embaixo.Right && p.Y + a.Height == embaixo.Bottom, "a lavadora entra sem mover o canto de baixo a direita");
                }
                // a janela de verdade: fora da frente por padrao, a opcao da casa liga e desliga
                ConfigDoWidget cfg = new ConfigDoWidget();
                using (WidgetDeBateria w = new WidgetDeBateria(cfg))
                {
                    w.Show();
                    Application.DoEvents();
                    Verdade(w.Visible && !w.NaFrenteAgora, "widget aberto fora do sempre na frente");
                    w.Renderizar();
                    Verdade(!w.NaFrenteAgora, "atualizar nao traz o widget para a frente");
                    Verdade(w.Casa.Itens().Length == 2, "o widget mostra a casa por padrao");
                    Size com = w.Size;
                    w.TrocarCasa(false);
                    Verdade(w.Casa.Itens().Length == 0 && !ConfigDoWidget.Carregar().Casa, "desligar a casa no widget tira os itens e grava");
                    Verdade(w.Size != com, "o widget encolhe sem a casa");
                    w.TrocarCasa(true);
                    Verdade(w.Casa.Itens().Length == 2 && ConfigDoWidget.Carregar().Casa && w.Size == com, "religar a casa volta os itens e o tamanho");
                    cfg.NaFrente = true; w.AplicarConfig();
                    Verdade(w.NaFrenteAgora, "sempre na frente liga");
                    cfg.NaFrente = false; w.AplicarConfig();
                    Verdade(!w.NaFrenteAgora, "desligar volta ao fundo");
                    w.Close();
                }
                ServicoCasa.PararSimulacao();
                using (ItensDaCasa casa = new ItensDaCasa())
                    Verdade(casa.Itens().Length == 0, "sem conta a casa nao entra no widget");
            }
            finally
            {
                ServicoCasa.PararSimulacao();
                ConfigDoWidget.Caminho = antesVidro;
                CasaNoWidget.CaminhoAntigo = antesCasa;
            }
        }

        private static float[] Uns(int n) { float[] u = new float[n]; for (int i = 0; i < n; i++) u[i] = 1f; return u; }

        private static PointF Centro(RectangleF r) { return new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f); }

        private static PintorDoWidget.Quadro QuadroDe(Periferico[] per, ItensDaCasa casa, LayoutDoWidget formato, float s)
        {
            PintorDoWidget.Quadro q = new PintorDoWidget.Quadro();
            q.Itens = per;
            q.Notas = new string[per.Length];
            q.Layout = formato;
            q.Escuro = true; q.Nitido = true;
            q.Escala = s;
            q.Casa = casa;
            return q;
        }

        private static Size Tamanho(Periferico[] per, ItensDaCasa casa, LayoutDoWidget formato, float s)
        {
            return PintorDoWidget.Medir(QuadroDe(per, casa, formato, s));
        }

        /// <summary>Pinta o widget com a casa; os botoes ficam dentro da placa e nos itens dela, sem encostar um no outro.</summary>
        private static bool QuadroCabe(Periferico[] per, ItensDaCasa casa, LayoutDoWidget formato, float s, string dir, string nome)
        {
            PintorDoWidget.Quadro q = QuadroDe(per, casa, formato, s);
            Size tam = PintorDoWidget.Medir(q);
            int pad = 24;
            using (Bitmap fundo = FundoDeParede(tam.Width + 2 * pad, tam.Height + 2 * pad))
            using (Bitmap bm = new Bitmap(tam.Width, tam.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bm)) { g.Clear(Color.Transparent); PintorDoWidget.Pintar(g, tam, q); }
                if (!string.IsNullOrEmpty(dir) && nome != null)
                {
                    using (Graphics g = Graphics.FromImage(fundo)) g.DrawImage(bm, pad, pad);
                    fundo.Save(System.IO.Path.Combine(dir, nome + ".png"));
                }
            }
            RectangleF placa = new RectangleF(0, 0, tam.Width, tam.Height);
            List<RectangleF> bt = new List<RectangleF>();
            foreach (RectangleF r in new RectangleF[] { casa.ZonaMenos, casa.ZonaMais, casa.ZonaLiga })
            {
                if (r.IsEmpty) continue;
                if (!placa.Contains(r)) return false;
                bt.Add(r);
            }
            for (int i = 0; i < bt.Count; i++)
                for (int j = i + 1; j < bt.Count; j++) if (bt[i].IntersectsWith(bt[j])) return false;
            // os itens da casa nao invadem os perifericos
            RectangleF[] zp = PintorDoWidget.Zonas(formato, PintorDoWidget.ComCasa(Uns(per.Length), casa),
                PintorDoWidget.ComCasa(formato, PintorDoWidget.Larguras(formato, per), casa), tam, s);
            for (int i = 0; i < per.Length; i++)
                foreach (RectangleF r in bt) if (zp[i].IntersectsWith(r)) return false;
            return true;
        }

        /// <summary>O widget sobre um fundo de papel de parede, como fica na area de trabalho.</summary>
        private static Bitmap FundoDeParede(int w, int h)
        {
            Bitmap b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            using (System.Drawing.Drawing2D.LinearGradientBrush br = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, w, h), Color.FromArgb(38, 70, 110), Color.FromArgb(120, 70, 95), 35f))
                g.FillRectangle(br, 0, 0, w, h);
            return b;
        }
        /// <summary>Perifericos escondidos: o filtro, a pagina Baterias e o X11 que fica.</summary>
        private static void PerifericosOcultos(string root)
        {
            Secao("perifericos escondidos (G325 e PRO X fora, X11 fica)");
            string[] antes = Perifericos.Ocultos;
            string pasta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mhiagos-ocultos-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(pasta);
            string histAntes = HistoricoDeBateria.Caminho;
            HistoricoDeBateria.Caminho = System.IO.Path.Combine(pasta, "baterias.dat");
            HistoricoDeBateria.Zerar();
            try
            {
                Perifericos.Ocultos = Perifericos.LerOcultos(new Config().PerifericosOcultos);
                Verdade(Perifericos.Ocultos.Length == 2, "padrao da config: G325 e PRO X");
                Verdade(Perifericos.OcultoChave("GHub|G325") && Perifericos.OcultoChave("GHub|PRO X Wireless Gaming Headset") &&
                    !Perifericos.OcultoChave("AttackShark|X11") && !Perifericos.OcultoChave("Bluetooth|G325"), "casa por fonte e inicio do nome");

                Perifericos.PublicarLogitech(new Periferico[] {
                    Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, 70, false),
                    Periferico.Sem("PRO X Wireless", TipoPeriferico.Headset, FontePeriferico.GHub, EstadoPeriferico.Desligado) });
                Perifericos.PublicarShark(Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 46, false));
                Periferico[] atual = Perifericos.Atual;
                Verdade(atual.Length == 1 && atual[0].Nome == "X11" && atual[0].Bateria == 46, "so o X11 no retrato, com a bateria");

                // A pagina Baterias tambem esquece o que o historico lembra deles.
                Perifericos.Ocultos = new string[0];
                HistoricoDeBateria.Registrar(Perifericos.Atual, DateTime.Now.AddMinutes(-10));
                Perifericos.Ocultos = Perifericos.LerOcultos("GHub|G325,GHub|PRO X");
                List<AnaliseDeBateria> an = HistoricoDeBateria.Analisar(Perifericos.Atual);
                bool soX11 = an.Count > 0;
                foreach (AnaliseDeBateria a in an) if (a.Nome != "X11") soX11 = false;
                Verdade(soX11, "Baterias: so o X11, mesmo com G325 no historico");

                Config config = AuditConfig(root, "ocultos");
                using (SettingsForm form = BugForm(config, new FreshData()))
                {
                    NavBar nav = (NavBar)Field(form, "_nav");
                    for (int i = 0; i < nav.Items.Count; i++) if (nav.Items[i].Key == "baterias") nav.SelectedIndex = i;
                    form.Size = new Size(1300, 760);
                    Field(form, "_bateriasLidas");
                    typeof(SettingsForm).GetField("_bateriasLidas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(form, DateTime.MinValue);
                    InvokePrivate(form, "AtualizarBaterias");
                    InvokePrivate(form, "ArranjarBaterias");
                    System.Collections.IList cartoes = (System.Collections.IList)Field(form, "_cartoesDeBateria");
                    int vivos = 0;
                    foreach (CartaoDeBateria cb in cartoes) if (!cb.Saindo) { vivos++; Verdade(cb.Chave == "AttackShark|X11", "cartao da Baterias: " + cb.Chave); }
                    Igual(1, vivos, "um cartao so na Baterias");
                    CaptureEvolution(form, "baterias-so-x11");
                }
            }
            finally
            {
                Perifericos.Ocultos = antes;
                Perifericos.PublicarLogitech(new Periferico[0]);
                Perifericos.PublicarShark(null);
                HistoricoDeBateria.Caminho = histAntes;
                HistoricoDeBateria.Zerar();
            }
        }

        private static void CasaLavadora(string root)
        {
            Secao("casa: lava e seca");
            CicloAtual c = CicloAtual.De(Lavadora("secando"));
            Verdade(c.Online && c.Rodando && c.Fase == 8 && c.Restante == 85 && c.Total == 389 && Math.Abs(c.Progresso - (1 - 85f / 389)) < 0.001,
                "ciclo lido: secando, 85 de 389 min");
            Igual(T.LavFaseSecagem, c.Titulo(), "titulo pela fase");
            Verdade(Math.Abs(c.Agua.Value - 168.13) < 0.001 && Math.Abs(c.Energia.Value - 2.48) < 0.001, "agua e energia do ciclo");
            Igual("90 °C", TabelaDaLavadora.Temperatura(9), "temperatura 9 = 90 °C");
            Igual("1400 rpm", TabelaDaLavadora.Centrifugacao(14), "centrifugacao 14 = 1400 rpm");
            Igual(T.Pt ? "Algodão" : "Cotton", TabelaDaLavadora.Programa(7), "programa 7 = Algodao, confirmado pelo Thiago (sem o provavel)");
            Igual(string.Format(T.LavProgramaProvavel, 8, T.Pt ? "Sintético" : "Synthetic"), TabelaDaLavadora.Programa(8), "programa 8 segue como provavel");
            Igual(string.Format(T.LavProgramaN, 99), TabelaDaLavadora.Programa(99), "programa desconhecido vai pelo numero");
            Igual(T.LavConcluida, CicloAtual.De(Lavadora("concluido")).Titulo(), "concluido");
            Igual(T.LavDesconectada, CicloAtual.De(Lavadora("offline")).Titulo(), "desconectada");
            Aparelho erro = Lavadora("secando"); erro.Estado["machine_status"] = "4"; erro.Estado["error_code"] = "12";
            Verdade(CicloAtual.De(erro).Erro && CicloAtual.De(erro).Titulo().Contains("12"), "erro com codigo");

            // Item 7: autodiagnostico da lavadora, pela resposta real gravada em docs.
            string arq = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\docs\connectlife-lavadora-levantamento.json");
            Verdade(System.IO.File.Exists(arq), "levantamento da lavadora disponivel para o teste");
            if (System.IO.File.Exists(arq))
            {
                Dictionary<string, object> lev = (Dictionary<string, object>)Json.Ler(System.IO.File.ReadAllText(arq));
                Dictionary<string, object> resp = (Dictionary<string, object>)lev["respostas"];
                List<FalhaOficial> fl = Diagnostico.Interpretar((Dictionary<string, object>)resp["selfcheck"]);
                Verdade(fl.Count == 30 && fl[0].Chave == "error_code", "30 itens na lista oficial, a maioria em error_code");
                FalhaOficial e1 = null;
                foreach (FalhaOficial x in fl) if (x.Chave == "error_code" && x.ValorDeFalha == "1") e1 = x;
                Verdade(e1 != null && e1.Orientacao.Contains(T.Pt ? "torneira" : "tap"), "codigo 1: orientacao da entrada de agua traduzida");
                Aparelho lvd = Lavadora("secando");
                Diagnostico.Definir(lvd.Puid, fl);
                Verdade(!Diagnostico.TemFalha(lvd) && Diagnostico.Texto(lvd).Contains(T.CasaSemErro), "sem erro: nenhum erro agora");
                lvd.Estado["error_code"] = "1";
                Verdade(Diagnostico.TemFalha(lvd) && Diagnostico.Texto(lvd).Contains(string.Format(T.CasaErroCodigo, 1)) &&
                    Diagnostico.Texto(lvd).Contains(e1.Orientacao), "erro 1: codigo e orientacao no diagnostico");
                Diagnostico.Definir(lvd.Puid, new List<FalhaOficial>());
            }
            // Ciclos: comeca ao rodar, termina ao parar, avisa uma vez.
            string pasta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mhiagos-casa-teste3");
            System.IO.Directory.CreateDirectory(pasta);
            string antes = CiclosDaLavadora.Arquivo;
            CiclosDaLavadora.Arquivo = System.IO.Path.Combine(pasta, "casa-ciclos.txt");
            System.IO.File.Delete(CiclosDaLavadora.Arquivo);
            CiclosDaLavadora.Zerar();
            Action<string, string> notifAntes = ServicoCasa.Notificar;
            List<string> avisos = new List<string>();
            ServicoCasa.Notificar = delegate(string t, string x) { avisos.Add(t + "|" + x); };
            try
            {
                PreferenciasCasa p = new PreferenciasCasa();
                DateTime t0 = DateTime.Now.AddHours(-2);
                Aparelho lav = Lavadora("secando");
                lav.Estado["Current_program_phase"] = "3";
                CiclosDaLavadora.Conferir(lav, p, t0);
                lav.Estado["Current_program_phase"] = "8";
                CiclosDaLavadora.Conferir(lav, p, t0.AddMinutes(90));
                Igual(0, avisos.Count, "rodando nao avisa");
                Aparelho fim = Lavadora("concluido");
                CiclosDaLavadora.Conferir(fim, p, t0.AddMinutes(120));
                CiclosDaLavadora.Conferir(fim, p, t0.AddMinutes(121));
                Igual(1, avisos.Count, "fim do ciclo avisa uma vez");
                List<CiclosDaLavadora.Ciclo> u = CiclosDaLavadora.Ultimos("p2", 5);
                Verdade(u.Count == 1 && u[0].Programa == 7 && u[0].Concluido && Math.Abs((u[0].Fim - u[0].Inicio).TotalMinutes - 120) < 0.01,
                    "ciclo registrado: programa 7, 2 h, concluido");
                CiclosDaLavadora.Zerar();
                CiclosDaLavadora.Arquivo = CiclosDaLavadora.Arquivo;
                typeof(CiclosDaLavadora).GetField("_carregado", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, false);
                Igual(1, CiclosDaLavadora.Ultimos("p2", 5).Count, "ciclo lido de volta do arquivo");
                avisos.Clear();
                CiclosDaLavadora.Conferir(erro, p, t0);
                CiclosDaLavadora.Conferir(erro, p, t0.AddMinutes(1));
                Igual(1, avisos.Count, "erro avisa uma vez");
                avisos.Clear();
                CiclosDaLavadora.Conferir(Lavadora("offline"), p, t0.AddMinutes(2));
                Igual(0, avisos.Count, "desconectada nao fecha ciclo nem avisa");
            }
            finally
            {
                ServicoCasa.Notificar = notifAntes;
                CiclosDaLavadora.Arquivo = antes;
                CiclosDaLavadora.Zerar();
            }
        }
    }
}
