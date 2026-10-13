using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    public static partial class Tests
    {
        private static void RegressionBugFixes(string root)
        {
            Secao("bugs: validade de leitura e separacao entre hardware e RTSS");
            long start = Stopwatch.Frequency, later = start + 6 * Stopwatch.Frequency;
            Dictionary<string, float> input = new Dictionary<string, float> { { "cpu", 83 }, { "rtss:fps", 120 } };
            ReadingSnapshot snapshot = ReadingSnapshot.Full(input, start);
            input["cpu"] = 99;
            Igual(83f, snapshot.Read(start, false)["cpu"], "snapshot nao compartilha dicionario mutavel com fonte");
            Igual(ReadingState.Live, snapshot.State("cpu", start, false), "nova leitura valida");
            Igual(0, snapshot.Read(later, false).Count, "leituras vencidas nao chegam a interface");
            snapshot = snapshot.WithFast(new Dictionary<string, float> { { "rtss:fps", 144 } }, later);
            Verdade(!snapshot.Read(later, false).ContainsKey("cpu"), "RTSS nao renova hardware vencido");
            Igual(144f, snapshot.Read(later, false)["rtss:fps"], "RTSS recente permanece disponivel");
            Igual(ReadingState.Expired, snapshot.State("cpu", later, false), "hardware indica expiracao");
            Igual(ReadingState.Live, snapshot.State("rtss:fps", later, false), "FPS tem validade independente");
            Igual(0, snapshot.Read(later, true).Count, "pausa nao conserva numeros ao vivo");
            Igual(ReadingState.Paused, snapshot.State(null, later, true), "pausa tem estado proprio");
            Igual(ReadingState.Failed, ReadingSnapshot.Failed.State(null, later, false), "falha tem estado proprio");
            Igual(0, ReadingSnapshot.Failed.Read(later, false).Count, "falha limpa instantaneo");
            snapshot = ReadingSnapshot.Full(new Dictionary<string, float> { { "cpu", 83 }, { "bad", float.NaN } }, later);
            Igual(83f, snapshot.Read(later, false)["cpu"], "valor igual ao anterior volta apos recuperacao");
            Verdade(!snapshot.Read(later, false).ContainsKey("bad"), "valor nao finito nao e publicado");
            Igual(ReadingState.Waiting, snapshot.State("unknown", later, false), "ausencia nao vira zero");
            Igual(0, snapshot.Read(start, false).Count, "recuo do relogio simulado nao valida leitura futura");

            RegressionFileProtection(root);
            RegressionMouseButtons();
            RegressionMissingMetrics(root);
            RegressionBugGeometry(root);
            RegressionVideoMemory();
            RegressionReviewFixes();
            RegressionPeripheralBattery();
            RegressionBatteryHistory();
            RegressionNavOrder(root);
            RegressionBatteryWidget(root);
            RegressionWidgetCard(root);
        }

        private static void RegressionBatteryWidget(string root)
        {
            Secao("widget de bateria: preferencias, formato e desenho");

            ConfigDoWidget padrao = ConfigDoWidget.DeTexto(null);
            Verdade(!padrao.Ativo && !padrao.TemPosicao, "sem arquivo o widget comeca desligado e sem posicao");
            Igual(LayoutDoWidget.Aneis, padrao.Layout, "formato padrao sao os aneis");
            Verdade(padrao.Nitido && !padrao.Fosco, "vidro padrao e o nitido, sem desfoque");
            Verdade(PintorDoWidget.Pulso(0f) < 0.001f && PintorDoWidget.Pulso(0.5f) > 0.999f && PintorDoWidget.Pulso(1f) < 0.001f,
                    "a respiracao da carga sai de 0, chega a 1 no meio do ciclo e volta");
            Verdade(typeof(IControleDoWidget).IsAssignableFrom(typeof(TrayContext)),
                    "a bandeja entrega o controle do widget para a pagina Baterias");

            // transicao de carga: primeira vista ja no estado certo, depois anda suave
            TransicaoDeCarga.Limpar();
            bool anima;
            int t0 = 1000000, dur = TransicaoDeCarga.DuracaoMs;
            Igual(1f, TransicaoDeCarga.Valor("teste|X11", true, out anima, t0), "aparelho visto pela primeira vez no cabo ja entra cheio");
            Verdade(!anima, "primeira vista nao anima");
            Igual(1f, TransicaoDeCarga.Valor("teste|X11", false, out anima, t0 + 10), "no instante em que sai do cabo ainda esta em 1");
            Verdade(anima, "sair do cabo comeca uma transicao");
            float noMeio = TransicaoDeCarga.Valor("teste|X11", false, out anima, t0 + 10 + dur / 2);
            Verdade(noMeio > 0.4f && noMeio < 0.6f && anima, "no meio da transicao a carga esta no meio");
            float perto = TransicaoDeCarga.Valor("teste|X11", false, out anima, t0 + 10 + dur / 10);
            Verdade(perto > 0.95f, "a saida comeca devagar, sem tranco");
            float virou = TransicaoDeCarga.Valor("teste|X11", true, out anima, t0 + 10 + dur / 2);
            Verdade(Math.Abs(virou - noMeio) < 0.001f && anima, "virar no meio do caminho parte de onde estava, sem salto");
            Igual(1f, TransicaoDeCarga.Valor("teste|X11", true, out anima, t0 + 20 + dur * 2), "no fim chega exatamente ao alvo");
            Verdade(!anima, "terminada a transicao o relogio pode parar");
            Igual(0f, TransicaoDeCarga.Valor(null, false), "sem chave vale o estado de agora");
            TransicaoDeCarga.Limpar();

            Periferico x11 = Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 46, true);
            Igual(PeripheralStrip.Verde.ToArgb(), PintorDoWidget.CorDoNivel(x11, 1f).ToArgb(), "carga 1 e o verde do cabo");
            Igual(PeripheralStrip.CorFora(x11).ToArgb(), PintorDoWidget.CorDoNivel(x11, 0f).ToArgb(), "carga 0 e a cor do nivel");
            Color metade = PeripheralStrip.Cor(x11, 0.5f);
            Verdade(metade.ToArgb() != PeripheralStrip.Verde.ToArgb() && metade.ToArgb() != PeripheralStrip.CorFora(x11).ToArgb(),
                    "no meio da transicao a cor fica entre as duas");

            // icone da aba Baterias, desenhado: contorno, polo e nivel
            using (Bitmap ic = new Bitmap(24, 24, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(ic)) { g.Clear(Color.Transparent); Ui.IconeDeBateria(g, new RectangleF(2, 2, 20, 20), Color.White); }
                Verdade(ic.GetPixel(12, 2).A == 0 && ic.GetPixel(12, 21).A == 0, "icone de bateria e deitado: sobra em cima e embaixo");
                Verdade(ic.GetPixel(9, 12).A > 200, "icone de bateria tem o nivel preenchido");
                Verdade(ic.GetPixel(21, 12).A > 100, "icone de bateria tem o polo a direita");
                string pastaIc = Environment.GetEnvironmentVariable("MHIAGOS_VISUAL_DIR");
                if (!string.IsNullOrEmpty(pastaIc))
                {
                    Directory.CreateDirectory(pastaIc);
                    using (Bitmap big = new Bitmap(ic, 96, 96)) big.Save(Path.Combine(pastaIc, "icone-baterias.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }

            ConfigDoWidget c = new ConfigDoWidget();
            c.Ativo = true; c.X = -1500; c.Y = 40; c.Layout = LayoutDoWidget.Lista; c.Fosco = true;
            c.Cor = CorDoVidro.Clara; c.NaFrente = true; c.Travado = true;
            ConfigDoWidget volta = ConfigDoWidget.DeTexto(c.ParaTexto());
            Igual(c.ParaTexto(), volta.ParaTexto(), "preferencias do widget fazem ida e volta");
            Igual(-1500, volta.X, "posicao em monitor a esquerda (negativa) sobrevive");

            ConfigDoWidget torto = ConfigDoWidget.DeTexto("ativo=1\nx=abc\ny=10\nlayout=hexagono\ncor=roxa\nlixo\n=1");
            Verdade(torto.Ativo, "linha boa vale mesmo com vizinhas estragadas");
            Verdade(!torto.TemPosicao, "meia posicao conta como nenhuma");
            Igual(LayoutDoWidget.Aneis, torto.Layout, "formato desconhecido volta ao padrao");
            Igual(CorDoVidro.Automatica, torto.Cor, "cor desconhecida volta ao padrao");

            string antes = ConfigDoWidget.Caminho;
            try
            {
                ConfigDoWidget.Caminho = Path.Combine(root, "widget.ini");
                Verdade(c.Salvar(), "widget.ini grava");
                c.X = 12;
                Verdade(c.Salvar(), "widget.ini regrava por cima");
                Igual(12, ConfigDoWidget.Carregar().X, "widget.ini carrega o que gravou");
                Verdade(!File.Exists(ConfigDoWidget.Caminho + ".tmp"), "gravacao nao deixa temporario");
            }
            finally { ConfigDoWidget.Caminho = antes; }

            Periferico[] todos = {
                Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, 72, false),
                Periferico.Sem("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, EstadoPeriferico.Desligado),
                Periferico.Sem("Bluetooth", TipoPeriferico.Outro, FontePeriferico.Bluetooth, EstadoPeriferico.NenhumConectado),
                Periferico.Sem("Fone BT", TipoPeriferico.Outro, FontePeriferico.Bluetooth, EstadoPeriferico.SemLeitura),
                Periferico.Lido("WH-1000XM4", TipoPeriferico.Outro, FontePeriferico.Bluetooth, 18, true) };
            Periferico[] vis = PintorDoWidget.Visiveis(todos);
            Igual(3, vis.Length, "Bluetooth sem leitura fica fora do widget");
            Igual("X11", vis[1].Nome, "mouse desligado continua no widget, com --");
            Igual(0, PintorDoWidget.Visiveis(null).Length, "sem lista o widget fica vazio");

            foreach (LayoutDoWidget layout in new LayoutDoWidget[] { LayoutDoWidget.Aneis, LayoutDoWidget.Lista })
            {
                Size um = PintorDoWidget.Medir(layout, 1, 1f), tres = PintorDoWidget.Medir(layout, 3, 1f);
                Verdade(tres.Width * tres.Height > um.Width * um.Height, layout + ": mais aparelhos, placa maior");
                Size dobro = PintorDoWidget.Medir(layout, 3, 2f);
                Verdade(Math.Abs(dobro.Width - 2 * tres.Width) <= 1, layout + ": escala de 200% dobra a largura");
                RectangleF[] zonas = PintorDoWidget.Zonas(layout, 3, 1.5f);
                Size caixa = PintorDoWidget.Medir(layout, 3, 1.5f);
                for (int i = 0; i < zonas.Length; i++)
                {
                    Verdade(zonas[i].Left >= 0 && zonas[i].Top >= 0 && zonas[i].Right <= caixa.Width && zonas[i].Bottom <= caixa.Height,
                        layout + ": aparelho " + i + " cabe na placa");
                    if (i > 0) Verdade(!zonas[i].IntersectsWith(zonas[i - 1]), layout + ": aparelhos " + (i - 1) + " e " + i + " nao se sobrepoem");
                }

                foreach (bool escuro in new bool[] { true, false })
                {
                    PintorDoWidget.Quadro q = new PintorDoWidget.Quadro();
                    q.Itens = vis;
                    q.Notas = new string[] { "~25 h", T.PeripheralOff, T.PeripheralCharging };
                    q.Layout = layout;
                    q.Escuro = escuro;
                    q.Escala = 1.5f;
                    q.Fase = 0.5f;
                    q.Carga = new float[] { 0f, 0f, 0.5f };   // o ultimo no meio da entrada no cabo
                    Size t = PintorDoWidget.Medir(layout, vis.Length, q.Escala);
                    using (Bitmap bmp = new Bitmap(t.Width, t.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); PintorDoWidget.Pintar(g, t, q); }
                        Igual(0, (int)bmp.GetPixel(0, 0).A, layout + (escuro ? " escuro" : " claro") + ": canto fora do vidro fica transparente");
                        int meio = bmp.GetPixel(t.Width / 2, t.Height / 2).A;
                        Verdade(meio > 0, layout + ": miolo desenhado");
                        Verdade(bmp.GetPixel(t.Width / 2, 3).A > 0, layout + ": vidro chega na borda de cima");
                        string pasta = Environment.GetEnvironmentVariable("MHIAGOS_VISUAL_DIR");
                        if (!string.IsNullOrEmpty(pasta))
                        {
                            Directory.CreateDirectory(pasta);
                            bmp.Save(Path.Combine(pasta, "widget-" + layout + (escuro ? "-escuro" : "-claro") + ".png"),
                                     System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                }
            }

            Rectangle area = new Rectangle(0, 0, 1920, 1040);
            Size tam = new Size(300, 130);
            Igual(new Point(0, 0), WidgetDeBateria.Encaixar(new Point(9, 7), tam, area, 12), "perto do canto gruda nele");
            Igual(new Point(1620, 910), WidgetDeBateria.Encaixar(new Point(1611, 903), tam, area, 12), "perto do canto oposto tambem");
            Igual(new Point(500, 400), WidgetDeBateria.Encaixar(new Point(500, 400), tam, area, 12), "longe das bordas fica onde soltou");
            Verdade(!WidgetDeBateria.Visivel(new Rectangle(-40000, -40000, 300, 130)), "posicao de um monitor que saiu nao conta como visivel");

            // a janela de verdade, com o arquivo apontado para a pasta do teste
            antes = ConfigDoWidget.Caminho;
            string log = File.Exists(Log.Path) ? File.ReadAllText(Log.Path) : "";
            try
            {
                ConfigDoWidget.Caminho = Path.Combine(root, "widget-janela.ini");
                ConfigDoWidget cfg = new ConfigDoWidget();
                using (WidgetDeBateria w = new WidgetDeBateria(cfg))
                {
                    w.Show();
                    Application.DoEvents();
                    Verdade(w.Visible, "widget abre");
                    Verdade(w.Width > 0 && w.Height > 0, "widget tem tamanho depois do primeiro desenho");
                    Verdade(WidgetDeBateria.Visivel(w.Bounds), "primeira abertura cai dentro da tela");
                    w.Renderizar();
                    w.Close();
                }
                string depois = File.Exists(Log.Path) ? File.ReadAllText(Log.Path) : "";
                string novo = depois.Length >= log.Length ? depois.Substring(log.Length) : depois;
                bool erro = false;
                foreach (string linha in novo.Split('\n'))
                    if (linha.IndexOf("widget", StringComparison.OrdinalIgnoreCase) >= 0 && linha.Contains(" :: ")) erro = true;
                Verdade(!erro, "widget abre e desenha sem erro no log");
            }
            finally { ConfigDoWidget.Caminho = antes; }
        }

        private static void RegressionNavOrder(string root)
        {
            Secao("barra lateral: ordem escolhida pela pessoa");

            string[] chaves = { "visao", "paineis", "metricas", "specs", "perfis", "config", "baterias", "sobre" };
            NavItem[] fabrica = new NavItem[chaves.Length];
            for (int i = 0; i < chaves.Length; i++) { fabrica[i] = new NavItem(); fabrica[i].Key = chaves[i]; }
            Func<List<NavItem>, string> junta = delegate(List<NavItem> l)
            {
                List<string> s = new List<string>();
                foreach (NavItem it in l) s.Add(it.Key);
                return string.Join(",", s.ToArray());
            };

            Igual(string.Join(",", chaves), junta(SettingsForm.OrdemDaBarra(fabrica, "")), "sem ordem gravada vale a de fabrica");
            string trocada = "baterias,visao,paineis,metricas,specs,perfis,config,sobre";
            Igual(trocada, junta(SettingsForm.OrdemDaBarra(fabrica, trocada)), "ordem gravada e respeitada");
            Igual(trocada, junta(SettingsForm.OrdemDaBarra(fabrica, "baterias,fantasma,visao,visao,paineis,metricas,specs,perfis,config,sobre")),
                "chave desconhecida e repetida sao ignoradas");
            Igual("visao,paineis,metricas,specs,perfis,config,baterias,sobre",
                junta(SettingsForm.OrdemDaBarra(fabrica, "visao,paineis,metricas,specs,perfis,config,sobre")),
                "pagina nova entra no lugar dela, depois da vizinha de fabrica");

            using (NavBar nav = new NavBar())
            {
                foreach (NavItem it in fabrica) nav.Add(it);
                nav.SelectedIndex = 2;   // metricas aberta
                int avisos = 0;
                nav.OrderChanged += delegate { avisos++; };
                nav.MoverItem(2, 6);
                Igual("metricas", nav.Items[6].Key, "item vai para a posicao pedida");
                Igual(6, nav.SelectedIndex, "a pagina aberta continua aberta depois de arrastada");
                Igual(1, avisos, "mudar a ordem avisa uma vez");
                nav.MoverItem(0, 1);
                Igual("metricas", nav.Selected.Key, "mover outra pagina nao troca a aberta");
                nav.MoverItem(3, 3);
                Igual(2, avisos, "mover para o mesmo lugar nao avisa");
            }

            // janela de verdade: a ordem gravada vale, o atalho da Visao geral acha
            // Baterias onde ela estiver, e reordenar grava sozinho
            Config config = AuditConfig(root, "nav-order");
            config.NavOrder = trocada;
            config.Save();
            using (SettingsForm form = BugForm(config, new FreshData()))
            {
                NavBar nav = (NavBar)Field(form, "_nav");
                Igual("baterias", nav.Items[0].Key, "janela abre com a ordem gravada");
                nav.SelectedIndex = 3;
                InvokePrivate(form, "AbrirPaginaPelaChave", "baterias");
                Igual("baterias", nav.Selected.Key, "clique no aparelho da Visao geral abre Baterias");
                form.MostrarPagina("metricas");
                Igual("metricas", nav.Selected.Key, "clique no widget leva a janela aberta para a pagina pedida");
                form.MostrarPagina(null);
                Igual("metricas", nav.Selected.Key, "abrir pela bandeja nao mexe na pagina aberta");
                nav.MoverItem(0, 7);
                Igual("baterias", Config.LoadFrom(config.SavePath, false).NavOrder.Split(',')[7],
                    "reordenar grava a ordem nova na hora");

                PeripheralStrip tira = (PeripheralStrip)Field(form, "_cardPerif");
                MethodInfo vai = typeof(PeripheralStrip).GetMethod("VaiParaBaterias", BindingFlags.NonPublic | BindingFlags.Instance);
                Verdade((bool)vai.Invoke(tira, new object[] {
                    Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, 70, false) }),
                    "headset da Visao geral leva a Baterias");
                Verdade((bool)vai.Invoke(tira, new object[] {
                    Periferico.Sem("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, EstadoPeriferico.Desligado) }),
                    "mouse desligado tambem leva a Baterias, onde esta o historico dele");
                Verdade(!(bool)vai.Invoke(tira, new object[] {
                    Periferico.Sem("Bluetooth", TipoPeriferico.Outro, FontePeriferico.Bluetooth, EstadoPeriferico.RadioDesligado) }),
                    "campo Bluetooth vazio continua abrindo o painel do Windows");
            }
        }

        private static void RegressionBatteryHistory()
        {
            Secao("bateria dos perifericos: historico de cargas e descargas");

            string pasta = Path.Combine(Path.GetTempPath(), "mhiagos-bat-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pasta);
            HistoricoDeBateria.Caminho = Path.Combine(pasta, "baterias.dat");
            HistoricoDeBateria.Zerar();
            try
            {
                const string Nome = "MouseDeTeste";
                string chave = FontePeriferico.AttackShark + "|" + Nome;
                DateTime t = new DateTime(2026, 10, 1, 8, 0, 0);
                Func<int, bool, Periferico[]> leitura = delegate(int pct, bool cabo)
                {
                    return new Periferico[] { Periferico.Lido(Nome, TipoPeriferico.Mouse, FontePeriferico.AttackShark, pct, cabo) };
                };
                // sem leitura viva: a analise sai so do historico
                Func<AnaliseDeBateria> analise = delegate
                {
                    foreach (AnaliseDeBateria a in HistoricoDeBateria.Analisar(null))
                        if (a.Chave == chave) return a;
                    return null;
                };

                // 2 h de uso, de 90 a 70%, em passos de 10 s
                for (int s = 0; s <= 7200; s += 10)
                    HistoricoDeBateria.Registrar(leitura(90 - 20 * s / 7200, false), t.AddSeconds(s));
                AnaliseDeBateria r = analise();
                Verdade(r.Atual != null && r.Atual.Parcial, "abrir no meio da descarga marca a medicao como parcial");
                Igual(2.0, Math.Round(r.Atual.UsoSegundos / 3600.0, 2), "uso conta o tempo ligado");

                // uma hora desligado nao e uso
                t = t.AddSeconds(7200 + 3600);
                HistoricoDeBateria.Registrar(leitura(70, false), t);
                Igual(2.0, Math.Round(analise().Atual.UsoSegundos / 3600.0, 2), "lacuna de aparelho desligado nao conta como uso");

                // carregador: 70 a 100% em 30 min
                for (int s = 10; s <= 1800; s += 10)
                    HistoricoDeBateria.Registrar(leitura(70 + 30 * s / 1800, true), t.AddSeconds(s));
                r = analise();
                Verdade(r.Atual == null, "ir para o carregador encerra a descarga");
                Igual(10.0, Math.Round(r.HorasPorCargaCheia.Value, 1), "20 pontos em 2 h dao 10 h por carga cheia");
                Igual(1, r.DescargasMedidas, "uma descarga medida");

                t = t.AddSeconds(1810);
                HistoricoDeBateria.Registrar(leitura(100, false), t);
                r = analise();
                Igual(100, r.UltimaCarga.PctFim, "ultima carga registra ate onde foi");
                Igual(70, r.UltimaCarga.PctInicio, "e de onde partiu");
                Igual(30.0, Math.Round((r.UltimaCarga.Fim - r.UltimaCarga.Inicio).TotalMinutes), "e quanto levou");
                Verdade(r.Atual != null && !r.Atual.Parcial, "descarga que comeca no fim de uma carga nao e parcial");

                // fora do cabo a leitura assenta (100 cai a 95 sozinha) e so depois gasta
                for (int s = 10; s <= 600; s += 10)
                    HistoricoDeBateria.Registrar(leitura(95, false), t.AddSeconds(s));
                Igual(95, analise().Atual.PctInicio, "queda logo depois do cabo nao conta como gasto");

                // 1 h de uso, 95 a 75%: a media passa a incluir a descarga em andamento
                t = t.AddSeconds(600);
                for (int s = 10; s <= 3600; s += 10)
                    HistoricoDeBateria.Registrar(leitura(95 - 20 * s / 3600, false), t.AddSeconds(s));
                r = analise();
                // faixas: 71-80% gastou 15 pontos em 4500 s, 81-90% 20 em 5400 s,
                // 91-100% 5 em 900 s; as demais ficam na media (270 s/ponto),
                // e cada faixa e puxada para a media com peso de 5 pontos
                Igual(7.4, Math.Round(r.HorasPorCargaCheia.Value, 1), "duracao soma o ritmo de cada faixa");
                Verdade(r.HorasPorPontoNaFaixa[9].Value < r.HorasPorPontoNaFaixa[7].Value,
                    "faixa que gastou mais depressa fica com ritmo mais curto");
                Verdade(!r.HorasPorPontoNaFaixa[3].HasValue, "faixa sem gasto medido fica sem ritmo proprio");
                Igual(3.17, Math.Round(r.UsoTotalHoras, 2), "uso total soma todas as descargas, assentamento incluido");
                Verdade(!r.HorasRestantes.HasValue, "sem leitura viva nao ha previsao de restante");
                AnaliseDeBateria viva = null;
                foreach (AnaliseDeBateria a in HistoricoDeBateria.Analisar(leitura(75, false)))
                    if (a.Chave == chave) viva = a;
                Igual(5.7, Math.Round(viva.HorasRestantes.Value, 1), "restante soma as faixas abaixo da carga atual");
                Igual(viva.HorasRestantes, HistoricoDeBateria.Restante(leitura(75, false)[0]),
                    "a Visao geral usa a mesma previsao da pagina Baterias");

                // um degrau de volta (como o G325 faz em repouso) nao e carga
                t = t.AddSeconds(3610);
                HistoricoDeBateria.Registrar(leitura(84, false), t);
                Verdade(analise().UltimaCarga != null && !analise().UltimaCarga.Deduzida, "subida pequena nao vira carga");

                // subir muito sem aviso de carga conta como carga feita fora da vista
                t = t.AddSeconds(10);
                HistoricoDeBateria.Registrar(leitura(97, false), t);
                r = analise();
                Verdade(r.UltimaCarga.Deduzida, "subida sem aviso vira carga deduzida");
                Igual(2, r.DescargasMedidas, "e fecha a descarga anterior");

                // gravar e reler preserva o essencial
                HistoricoDeBateria.Salvar();
                HistoricoDeBateria.Recarregar();
                AnaliseDeBateria lida = analise();
                Verdade(lida != null, "historico sobrevive a gravacao");
                Igual(r.HorasPorCargaCheia.Value, lida.HorasPorCargaCheia.Value, "media igual depois de reler");
                Igual(r.UltimaCarga.Fim, lida.UltimaCarga.Fim, "ultima carga igual depois de reler");
                Igual(r.Amostras.Length, lida.Amostras.Length, "amostras iguais depois de reler");

                Igual("45 min", HistoricoDeBateria.Duracao(0.75), "duracao curta em minutos");
                Igual("12 h 30 min", HistoricoDeBateria.Duracao(12.5), "duracao em horas e minutos");
                Igual("3 d 4 h", HistoricoDeBateria.Duracao(76), "duracao longa em dias");

                // a carga de 70 a 100% levou 60 s por ponto: de 50 faltam 50 pontos
                Igual(0.83, Math.Round(HistoricoDeBateria.AteEncher(leitura(50, true)[0]).Value, 2),
                    "cheio em sai do ritmo de carga registrado");
                Igual(0.0, HistoricoDeBateria.AteEncher(leitura(100, true)[0]).Value, "em 100% no cabo ja esta cheio");
                Verdade(!HistoricoDeBateria.AteEncher(leitura(50, false)[0]).HasValue, "fora do cabo nao ha cheio em");

                // saude: o mes de outubro mediu 40 pontos em 3 h de uso
                Igual(1, lida.SaudePorMes.Count, "um mes com dado bastante");
                Igual(7.5, Math.Round(lida.SaudePorMes[0].Value, 1), "horas por carga cheia do mes");

                // bateria como sensor: o aparelho conhecido entra na lista mesmo desligado
                SensorEntry sensor = null;
                foreach (SensorEntry e in new BateriaSensorSource().Read(null))
                    if (e.Id == "bat:attackshark:" + Nome) sensor = e;
                Verdade(sensor != null, "aparelho do historico vira sensor");
                Igual("%", sensor.EffectiveUnit, "sensor de bateria em porcentagem");
                Igual(Sensors.CategoriaPerifericos, sensor.Category, "na categoria Perifericos");
                float? atencao, perigo;
                MetricPicker.Faixas("%", sensor.Id, out atencao, out perigo);
                Verdade(!atencao.HasValue && !perigo.HasValue, "bateria cheia nao pinta o grafico de vermelho");
                Igual("41 h", HistoricoDeBateria.DuracaoAproximada(40.69), "previsao longa arredonda na hora");
                Igual("2 h 20 min", HistoricoDeBateria.DuracaoAproximada(2.37), "previsao curta arredonda de 10 em 10 min");
                Igual("20 min", HistoricoDeBateria.DuracaoAproximada(0.31), "previsao de minutos arredonda de 5 em 5");
            }
            finally
            {
                HistoricoDeBateria.Caminho = null;
                // Rele o arquivo de verdade em vez de zerar: com o historico vazio
                // e marcado como lido, o proximo Registrar dos testes da bandeja
                // gravaria por cima do baterias.dat de quem roda a suite.
                HistoricoDeBateria.Recarregar();
                try { Directory.Delete(pasta, true); } catch { }
            }
        }

        private static void RegressionPeripheralBattery()
        {
            Secao("bateria dos perifericos: dica da bandeja e avisos");

            string dica = TrayContext.ComBateria("Mhiagos Control  63C / 42%", "G325 70%  X11 90%");
            Igual("Mhiagos Control  63C / 42%\nG325 70%  X11 90%", dica, "bateria vira a segunda linha da dica");
            Igual("abc", TrayContext.ComBateria("abc", null), "sem perifericos a dica nao muda");
            string longa = TrayContext.ComBateria(new string('x', 80), "G325 70%  X11 90%");
            Verdade(longa.Length <= 63, "dica nunca passa do limite do NotifyIcon");
            Verdade(longa.EndsWith("\nG325 70%  X11 90%"), "quem encolhe e a primeira linha, nao a bateria");

            AvisosDeBateria avisos = new AvisosDeBateria();
            Func<int, bool, int> passo = delegate(int pct, bool carregando)
            {
                List<KeyValuePair<Periferico, int>> r = avisos.Verificar(new Periferico[] {
                    Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, pct, carregando) });
                return r.Count == 0 ? 0 : r[0].Value;
            };
            Igual(0, passo(50, false), "carga alta nao avisa");
            Igual(30, passo(30, false), "30% avisa");
            Igual(0, passo(29, false), "aviso de 30% sai uma vez so");
            Igual(0, passo(31, false), "oscilar perto do limiar nao repete o aviso");
            Igual(10, passo(10, false), "10% avisa de novo");
            Igual(0, passo(8, false), "aviso de 10% sai uma vez so");
            Igual(0, passo(9, true), "no carregador nao avisa");
            Igual(30, passo(12, false), "tirar do carregador ainda abaixo de 30 avisa de novo");
            Igual(10, passo(10, false), "depois do carregador, 10% volta a avisar");
            Igual(0, avisos.Verificar(new Periferico[] {
                Periferico.Sem("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, EstadoPeriferico.Desligado) }).Count,
                "aparelho desligado nao avisa");

            AvisosDeBateria inicio = new AvisosDeBateria();
            List<KeyValuePair<Periferico, int>> ja = inicio.Verificar(new Periferico[] {
                Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 7, false) });
            Igual(1, ja.Count, "abrir o app ja abaixo de 10% gera um aviso so");
            Igual(10, ja[0].Value, "e e o aviso critico");

            // carga completa: uma vez por passagem pelo cabo
            AvisosDeBateria cheio = new AvisosDeBateria();
            Func<int, bool, int> cabo = delegate(int pct, bool carregando)
            {
                return cheio.VerificarCargaCompleta(new Periferico[] {
                    Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, pct, carregando) }).Count;
            };
            Igual(0, cabo(90, true), "no cabo antes de 100% nao avisa");
            Igual(1, cabo(100, true), "chegar a 100% no cabo avisa");
            Igual(0, cabo(100, true), "continuar em 100% nao repete");
            Igual(0, cabo(99, false), "tirar do cabo nao avisa");
            Igual(1, cabo(100, true), "nova passagem pelo cabo avisa de novo");

            // aviso por tempo: 2 h ou menos, uma vez, rearmando acima de 3 h
            AvisosDeBateria tempo = new AvisosDeBateria();
            double? previsto = null;
            Func<double?, bool, int> horas = delegate(double? h, bool carregando)
            {
                previsto = h;
                return tempo.VerificarTempo(new Periferico[] {
                    Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 40, carregando) },
                    delegate(Periferico p) { return previsto; }).Count;
            };
            Igual(0, horas(null, false), "sem previsao nao avisa");
            Igual(0, horas(5, false), "longe do fim nao avisa");
            Igual(1, horas(1.9, false), "2 h ou menos avisa");
            Igual(0, horas(1.5, false), "aviso por tempo sai uma vez so");
            Igual(0, horas(2.5, false), "oscilar perto do limite nao rearma");
            Igual(0, horas(1.8, false), "e nao repete");
            Igual(0, horas(3.5, false), "previsao acima de 3 h rearma");
            Igual(1, horas(1.8, false), "e o aviso volta");
            Igual(0, horas(1.0, true), "no cabo nao avisa");

            // pilhinha no icone: alguem fora do cabo com 30% ou menos
            Verdade(!TrayContext.AlgumBaixo(new Periferico[] {
                Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, 31, false) }), "31% nao marca o icone");
            Verdade(TrayContext.AlgumBaixo(new Periferico[] {
                Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, 70, false),
                Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 30, false) }), "30% em qualquer aparelho marca");
            Verdade(!TrayContext.AlgumBaixo(new Periferico[] {
                Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 12, true) }), "baixo mas no cabo nao marca");
            Verdade(!TrayContext.AlgumBaixo(new Periferico[] {
                Periferico.Sem("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, EstadoPeriferico.Desligado) }), "desligado nao marca");
        }

        private static void RegressionVideoMemory()
        {
            Secao("bugs: memoria de video da Radeon vem do Direct3D");

            const string Placa = "GPU [#0]: AMD Radeon RX 580: Sapphire RX 580 Pulse";
            const string Outra = "GPU [#1]: AMD Radeon RX 6600";
            List<SensorEntry> lista = new List<SensorEntry>();
            SensorEntry driver = Memoria(Placa, "GPU Memory Usage", 86354f, "MB");
            SensorEntry d3d = Memoria(Placa, "GPU D3D Memory Dedicated", 1885f, "MB");
            SensorEntry outraPlaca = Memoria(Outra, "GPU Memory Usage", 50000f, "MB");
            lista.Add(driver); lista.Add(d3d); lista.Add(outraPlaca);
            foreach (SensorEntry e in lista) Sensors.ToGigabytes(e);

            Sensors.CorrigirMemoriaDeVideo(lista);
            Verdade(Math.Abs(driver.Value.Value - 1885f / 1024f) < 0.001f,
                    "GPU Memory Usage do driver passa a mostrar o D3D dedicado");
            Igual("GB", driver.Unit, "unidade preservada");
            Igual("hw:" + Placa + "|8|GPU Memory Usage", driver.Id, "ID salvo nos perfis nao muda");
            Verdade(Math.Abs(outraPlaca.Value.Value - 50000f / 1024f) < 0.001f,
                    "placa sem D3D dedicado nao recebe valor de outra placa");

            SensorEntry nvidia = Memoria(Placa, "GPU Memory Usage", 37f, "%");
            nvidia.Type = SensorType.Load;
            List<SensorEntry> porcentagem = new List<SensorEntry>();
            porcentagem.Add(nvidia); porcentagem.Add(Memoria(Placa, "GPU D3D Memory Dedicated", 2.5f, "GB"));
            Sensors.CorrigirMemoriaDeVideo(porcentagem);
            Igual(37f, nvidia.Value.Value, "uso em porcentagem nao e tocado");

            List<SensorEntry> semantica = new List<SensorEntry>();
            SensorEntry usage = Memoria(Placa, "GPU Memory Usage", 84f, "GB");
            usage.Id = "usage"; usage.Category = "GPU";
            SensorEntry dedicada = Memoria(Placa, "GPU D3D Memory Dedicated", 1.8f, "GB");
            dedicada.Id = "d3d"; dedicada.Category = "GPU";
            semantica.Add(usage); semantica.Add(dedicada);
            Igual("d3d", SensorSemantics.ResolveId(semantica, "semantic:vram-used"),
                  "recuperacao da memoria de video prefere o D3D dedicado");
        }

        private static void RegressionReviewFixes()
        {
            Secao("bugs: revisao de outubro (config, metricas, autostart, winget)");

            string caixa = Path.Combine(Path.GetTempPath(), "MhiagosControlTeste" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(caixa);
            try
            {
                string ini = Path.Combine(caixa, "config.ini");
                File.WriteAllText(ini, "[general]\r\nactive=Meu\r\n" +
                    "metric=hw:CPU [#0]: AMD Ryzen 5 5600X: Enhanced|1|CPU (Tctl/Tdie)\r\n" +
                    "metric=2|rtss:fps\r\n\r\n[profile]\r\nname=Meu\r\npanel1=a\r\npanel2=b\r\n");
                Config lida = Config.LoadFrom(ini, false);
                Igual("hw:CPU [#0]: AMD Ryzen 5 5600X: Enhanced|1|CPU (Tctl/Tdie)", lida.MetricIds[0],
                      "metrica antiga do HWiNFO sem tamanho preserva o ID inteiro");
                Igual(0, lida.MetricSizes[0], "metrica antiga recebe tamanho pequeno");
                Igual("rtss:fps", lida.MetricIds[1], "prefixo numerico continua sendo tamanho");
                Igual(2, lida.MetricSizes[1], "tamanho lido do prefixo");

                // Arquivo travado: o .bak entra e o original nao e sobrescrito por um padrao.
                File.Copy(ini, ini + ".bak", true);
                using (new FileStream(ini, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Config travada = Config.LoadFrom(ini, false);
                    Igual("Meu", travada.Active.Name, "arquivo travado le a copia .bak");
                }
                File.Delete(ini + ".bak");
                string original = File.ReadAllText(ini);
                Config semCopia;
                using (new FileStream(ini, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    semCopia = Config.LoadFrom(ini, false);
                Verdade(!semCopia.Save(), "sem copia, a sessao nao grava por cima do arquivo real");
                Igual(original, File.ReadAllText(ini), "config real permanece intacto");

                Verdade(!Autostart.PastaProtegida(caixa), "pasta temporaria do usuario nao serve para autostart elevado");
                Verdade(!Autostart.PastaProtegida(Path.Combine(caixa, "inexistente")), "pasta inexistente e recusada");
            }
            finally { try { Directory.Delete(caixa, true); } catch { } }

            string programas = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            Verdade(Autostart.PastaProtegida(Path.Combine(windows, "System32")) || !Directory.Exists(programas),
                    "pasta do sistema conta como protegida");

            string winget = Rtss.Winget();
            Verdade(winget == null || winget.EndsWith(@"\Microsoft\WindowsApps\winget.exe", StringComparison.OrdinalIgnoreCase),
                    "winget so vem do atalho do App Installer, nunca do PATH");
        }

        private static SensorEntry Memoria(string placa, string nome, float valor, string unidade)
        {
            SensorEntry e = new SensorEntry();
            e.Hardware = placa; e.Name = nome; e.Label = nome; e.Category = "GPU";
            e.Type = SensorType.Data; e.Value = valor; e.Unit = unidade;
            e.Id = "hw:" + placa + "|8|" + nome; e.Source = "HWiNFO";
            return e;
        }

        private static void RegressionFileProtection(string root)
        {
            Secao("bugs: exportacao protegida e limites de temporizadores");
            Config config = AuditConfig(root, "protected-export");
            string original = File.ReadAllText(config.SavePath), error;
            string alias = Path.Combine(Path.GetDirectoryName(config.SavePath), ".", Path.GetFileName(config.SavePath));
            Verdade(!Config.ExportProfile(config.Active, alias, config.SavePath, out error), "recusa caminho equivalente ao config ativo");
            Igual(T.ExportOwnConfig, error, "erro especifico da protecao");
            string defaultPath = (string)typeof(Config).GetProperty("FilePath", AuditFlags).GetValue(null, null);
            Verdade(!Config.ExportProfile(config.Active, defaultPath, out error), "API padrao protege config real sem abri-lo");
            Igual(original, File.ReadAllText(config.SavePath), "config ativo permanece intacto");
            string export = Path.Combine(root, "single-profile.ini");
            Verdade(Config.ExportProfile(config.Profiles[0], export, config.SavePath, out error), "exportacao normal funciona");
            string previous = File.ReadAllText(export);
            using (FileStream held = File.Open(export, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Verdade(!Config.ExportProfile(config.Profiles[1], export, config.SavePath, out error), "falha ao substituir arquivo bloqueado");
                Igual(previous, File.ReadAllText(export), "falha preserva exportacao anterior");
            }
            Verdade(Config.ExportProfile(config.Profiles[1], export, config.SavePath, out error), "nova tentativa substitui exportacao");
            Igual("B", Config.ImportProfile(export, out error).Name, "perfil exportado pode ser importado");
            foreach (int idle in new int[] { 0, 1, 999 })
            foreach (int rotation in new int[] { 0, 2, 999 })
            {
                config.IdleBlankMinutes = idle; config.RotateSeconds = rotation;
                Config parsed = Config.FromText(config.ToText());
                Igual(idle, parsed.IdleBlankMinutes, "inatividade valida preservada");
                Igual(rotation, parsed.RotateSeconds, "rodizio valido preservado");
            }
            foreach (int[] invalid in new int[][] { new int[] { -1, 20 }, new int[] { 1000, 20 },
                new int[] { 40000, 20 }, new int[] { 0, 1 }, new int[] { 0, -1 }, new int[] { 0, 3000000 } })
            {
                config.IdleBlankMinutes = invalid[0]; config.RotateSeconds = invalid[1];
                bool refused = false;
                try { Config.FromText(config.ToText()); } catch (FormatException) { refused = true; }
                Verdade(refused, "restauracao recusa intervalo fora da faixa");
                // Checksum correto nao torna os intervalos semanticamente validos.
                byte[] bytes = Encoding.UTF8.GetBytes(config.ToText());
                string hash;
                using (SHA256 sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(bytes));
                string backup = Path.Combine(root, "invalid-timing.mhiagos-backup");
                File.WriteAllText(backup, "MHIAGOS-CONFIG-BACKUP/1\n" + hash + "\n" + Convert.ToBase64String(bytes));
                refused = false;
                try { ConfigurationBackup.Read(backup); } catch (FormatException) { refused = true; }
                Verdade(refused, "backup integro mas invalido e recusado");
                Verdade(!config.Save(out error), "gravacao recusa estado invalido");
            }
            config.IdleBlankMinutes = 40000; config.RotateSeconds = 3000000;
            foreach (string invalidText in new string[] { "2147483648", "abc", "1.5" })
            {
                bool refused = false;
                try { Config.FromText("[general]\nactive=A\nidleblank=" + invalidText + "\n[profile]\nname=A\n"); }
                catch (FormatException) { refused = true; }
                Verdade(refused, "backup nao transforma inteiro invalido em desligado");
            }
            string legacy = Path.Combine(root, "legacy-timing.ini");
            File.WriteAllText(legacy, config.ToText()); string legacyText = File.ReadAllText(legacy);
            Config normalized = Config.LoadFrom(legacy, false);
            Igual(999, normalized.IdleBlankMinutes, "config antigo limitado a faixa da interface");
            Igual(999, normalized.RotateSeconds, "rodizio antigo limitado a faixa da interface");
            Igual(legacyText, File.ReadAllText(legacy), "leitura nao reescreve config antigo");
            Igual(59940000, ConfigTiming.IdleMilliseconds(int.MaxValue), "conversao de minutos nao transborda");
            Igual(999000, ConfigTiming.RotationMilliseconds(int.MaxValue), "conversao de segundos nao transborda");
            SettingsSession session = new SettingsSession(AuditConfig(root, "invalid-restore"));
            string before = session.SavedConfiguration.ToText(), safety;
            Verdade(!session.TryRestore(config, out safety, out error), "restauracao direta tambem rejeita candidato invalido");
            Igual(before, session.SavedConfiguration.ToText(), "restauracao recusada preserva estado salvo");
            Igual(before, session.Draft.ToText(), "restauracao recusada preserva rascunho");
        }

        private static void RegressionMouseButtons()
        {
            Secao("bugs: comandos somente com botao esquerdo");
            using (MetricCard card = new MetricCard())
            {
                card.Size = new Size(240, 160); int commands = 0;
                card.Remover += delegate { commands++; }; card.MoverDireita += delegate { commands++; };
                card.MoverEsquerda += delegate { commands++; }; card.TrocarTamanho += delegate { commands++; };
                using (Bitmap image = new Bitmap(240, 160)) card.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                foreach (string name in new string[] { "_bRem", "_bEsq", "_bDir", "_bTam" })
                {
                    Rectangle hit = (Rectangle)Field(card, name);
                    foreach (MouseButtons button in new MouseButtons[] { MouseButtons.Right, MouseButtons.Middle })
                        InvokePrivate(card, "OnMouseDown", new MouseEventArgs(button, 1, hit.X + 4, hit.Y + 4, 0));
                }
                Igual(0, commands, "direito e central nao removem, movem ou redimensionam");
                Rectangle remove = (Rectangle)Field(card, "_bRem");
                InvokePrivate(card, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, remove.X + 4, remove.Y + 4, 0));
                Igual(1, commands, "esquerdo continua acionando comando");
            }
            using (GameBindingList games = new GameBindingList())
            {
                games.Size = new Size(500, 100); int removed = 0;
                games.SetItems(new GameBindingView[] { new GameBindingView { Key = "fake.exe", Name = "Fake", Profile = "A" } });
                games.RemoveRequested += delegate { removed++; };
                foreach (MouseButtons button in new MouseButtons[] { MouseButtons.Right, MouseButtons.Middle })
                    InvokePrivate(games, "OnMouseUp", new MouseEventArgs(button, 1, 480, 25, 0));
                Igual(0, removed, "direito e central nao removem vinculo");
                InvokePrivate(games, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 480, 25, 0));
                Igual(1, removed, "esquerdo remove vinculo");
            }
        }

        private static void RegressionMissingMetrics(string root)
        {
            Secao("bugs: sensores ausentes sobrevivem ao arraste e a reabertura");
            for (int position = 0; position < 3; position++)
            {
                Config config = AuditConfig(root, "missing-" + position);
                config.MetricIds.Clear(); config.MetricSizes.Clear();
                config.MetricIds.AddRange(new string[] { "cpu", "load" }); config.MetricIds.Insert(position, "missing");
                foreach (string id in config.MetricIds) config.MetricSizes.Add(id == "missing" ? 2 : id == "cpu" ? 1 : 0);
                config.Save(); FreshData data = new FreshData();
                using (SettingsForm form = BugForm(config, data))
                {
                    ((NavBar)Field(form, "_nav")).SelectedIndex = 2;
                    List<MetricCard> cards = (List<MetricCard>)Field(form, "_cards");
                    Igual(3, cards.Count, "cartao do sensor ausente continua presente");
                    MetricCard moved = cards[0];
                    InvokePrivate(form, "ComecarArrasteDeCartao", moved);
                    InvokePrivate(form, "ReordenarPara", 2); Field(form, "_destinoDoArraste", 2);
                    InvokePrivate(form, "OnSoltouCartao", form, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                    Config loaded = Config.LoadFrom(config.SavePath, false);
                    Igual(3, loaded.MetricIds.Count, "arraste preserva todos os sensores no disco");
                    Igual(2, loaded.MetricSize(loaded.MetricIds.IndexOf("missing")), "tamanho do ausente acompanha ID");
                    InvokePrivate(form, "MexerNaMetrica", "missing", -1);
                    loaded = Config.LoadFrom(config.SavePath, false);
                    Verdade(loaded.MetricIds.Contains("missing"), "setas preservam sensor ausente");
                    data.Sensors.Add(AuditSensor("missing", "CPU", SensorType.Temperature, "C", 55));
                    data.Values["missing"] = 55;
                    InvokePrivate(form, "AtualizarMetricas", data.CurrentSnapshot());
                    foreach (MetricCard card in (List<MetricCard>)Field(form, "_cards"))
                        if (card.SensorId == "missing")
                        {
                            Verdade(card.Titulo != T.SensorUnavailable, "retorno do sensor atualiza titulo");
                            Igual((float?)55, (float?)Field(card, "_valor"), "retorno publica leitura sem recriar janela");
                        }
                    form.Close();
                }
                using (SettingsForm reopened = BugForm(Config.LoadFrom(config.SavePath, false), data))
                {
                    Igual(3, ((List<MetricCard>)Field(reopened, "_cards")).Count, "reabertura preserva grade completa");
                    reopened.Close();
                }
            }
        }

        private sealed class FreshData : ISettingsData, IReadingStatusData
        {
            public readonly List<SensorEntry> Sensors = new List<SensorEntry> {
                AuditSensor("cpu", "CPU", SensorType.Temperature, "C", 40), AuditSensor("load", "CPU", SensorType.Load, "%", 20) };
            public readonly Dictionary<string, float> Values = new Dictionary<string, float> { { "cpu", 40 }, { "load", 20 } };
            public ReadingState State = ReadingState.Live;
            public Dictionary<string, float> CurrentSnapshot() { return State == ReadingState.Live ? Values : new Dictionary<string, float>(); }
            public List<SensorEntry> RefreshSensorList() { return Sensors; }
            public void SetShowAllSensors(bool value) { }
            public ReadingState CurrentReadingState(string id) { return State; }
        }

        /// <summary>Hospedeiro de mentira que tambem entrega o controle do widget.</summary>
        private sealed class DadosComWidget : ISettingsData, IControleDoWidget
        {
            public readonly ConfigDoWidget Widget = new ConfigDoWidget();
            public int Mudancas, AoCanto;
            public bool? Ligado;
            public Dictionary<string, float> CurrentSnapshot() { return new Dictionary<string, float>(); }
            public List<SensorEntry> RefreshSensorList() { return new List<SensorEntry>(); }
            public void SetShowAllSensors(bool value) { }
            public ConfigDoWidget ConfigDoWidget { get { return Widget; } }
            public void LigarWidget(bool ligar) { Ligado = ligar; Widget.Ativo = ligar; }
            public void WidgetMudou() { Mudancas++; }
            public void WidgetAoCanto() { AoCanto++; }
        }

        private static void RegressionWidgetCard(string root)
        {
            Secao("widget de bateria: cartao na pagina Baterias");
            DadosComWidget dados = new DadosComWidget();
            dados.Widget.Fosco = true; dados.Widget.Nitido = false; dados.Widget.NaFrente = true;
            Config config = AuditConfig(root, "widget-card");
            using (SettingsForm form = new SettingsForm(config, new List<SensorEntry>(), dados))
            {
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
                form.Show(); ((Timer)Field(form, "_tick")).Stop();
                Verdade(Field(form, "_cardWidget") != null, "com o controle do widget, a pagina Baterias ganha o cartao");
                Segmented vidro = (Segmented)Field(form, "_sgWidgetVidro");
                Toggle frente = (Toggle)Field(form, "_tgWidgetFrente");
                Igual(2, vidro.SelectedIndex, "o cartao abre com o vidro que esta valendo");
                Verdade(frente.Checked, "e com o sempre na frente que esta valendo");
                Igual(0, dados.Mudancas, "preencher o cartao nao conta como mudanca");

                vidro.SelectedIndex = 0;
                Verdade(dados.Widget.Nitido && !dados.Widget.Fosco, "escolher nitido no cartao muda a configuracao do widget");
                Igual(1, dados.Mudancas, "e avisa a bandeja para salvar e redesenhar");
                ((Segmented)Field(form, "_sgWidgetLayout")).SelectedIndex = 1;
                Igual(LayoutDoWidget.Lista, dados.Widget.Layout, "o formato tambem vale na hora");
                ((Toggle)Field(form, "_tgWidget")).Checked = true;
                Verdade(dados.Ligado == true, "ligar no cartao liga o widget");

                dados.Widget.Cor = CorDoVidro.Clara;
                InvokePrivate(form, "SincronizarWidget");
                Igual(2, ((Segmented)Field(form, "_sgWidgetCor")).SelectedIndex, "mudanca feita pelo menu do widget aparece no cartao");
                Igual(2, dados.Mudancas, "sincronizar nao devolve a mudanca para a bandeja");
                form.Close();
            }
        }

        private static SettingsForm BugForm(Config config, FreshData data)
        {
            SettingsForm form = new SettingsForm(config, data.Sensors, data);
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
            form.Show(); ((Timer)Field(form, "_tick")).Stop(); return form;
        }

        private static void RegressionBugGeometry(string root)
        {
            Secao("bugs: largura minima, rolagem e estados da interface");
            string languageBefore = T.Language;
            try
            {
                foreach (string language in new string[] { T.PtBr, T.EnUs })
                {
                    T.Language = language;
                    Config config = AuditConfig(root, "bug-geometry-" + language); FreshData data = new FreshData();
                    config.MetricIds.Clear(); config.MetricSizes.Clear();
                    for (int i = 0; i < 30; i++) { config.MetricIds.Add("absent-" + i); config.MetricSizes.Add(i % 3); }
                    config.MetricIds.Insert(0, "cpu"); config.MetricSizes.Insert(0, 0); config.Save();
                    using (SettingsForm form = BugForm(config, data))
                    {
                        NavBar nav = (NavBar)Field(form, "_nav");
                        foreach (Size size in new Size[] { new Size(880, 660), new Size(1100, 800) })
                        foreach (int sidebar in new int[] { 60, 210, 330 })
                        {
                            form.Size = size; nav.Width = sidebar;
                            foreach (int index in new int[] { 2, 6, 7 })
                            {
                                nav.SelectedIndex = index; InvokePrivate(form, "ShowPage");
                                Control page = nav.Selected.Page;
                                foreach (Control child in page.Controls)
                                    if (child.Visible && !(child is ScrollFina))
                                    {
                                        Verdade(child.Left >= 0 && child.Right <= page.Width, "controle cabe na pagina " + index + " largura " + size.Width + " lateral " + sidebar);
                                        if (child is Card)
                                            foreach (Control content in child.Controls)
                                                Verdade(content.Right <= child.Width && content.Bottom <= child.Height, "texto e botao cabem em Sobre");
                                    }
                                CaptureEvolution(form, "bugs-" + language + "-" + size.Width + "-" + sidebar + "-" + index);
                                if (index == 6 && size.Width == 880 && sidebar == 330)
                                {
                                    ScrollFina aboutBar = (ScrollFina)Field(page, "_barra");
                                    aboutBar.Ajustar(int.MaxValue);
                                    CaptureEvolution(form, "bugs-about-bottom-" + language);
                                    aboutBar.Ajustar(0);
                                }
                            }
                        }
                        nav.SelectedIndex = 2;
                        Pagina metrics = (Pagina)Field(form, "_pgMetricas");
                        ScrollFina bar = (ScrollFina)Field(metrics, "_barra");
                        Verdade(!bar.IsDisposed && metrics.Controls.Contains(bar), "barra preservada na montagem");
                        for (int rebuild = 0; rebuild < 3; rebuild++) InvokePrivate(form, "MontarMetricas");
                        Verdade(!bar.IsDisposed && metrics.Controls.Contains(bar), "barra preservada apos reconstrucoes");
                        bar.Ajustar(int.MaxValue);
                        List<MetricCard> cards = (List<MetricCard>)Field(form, "_cards");
                        Verdade(cards[cards.Count - 1].Bottom <= metrics.Height, "barra alcanca ultimo cartao");
                        metrics.RolarPor(-60); Verdade(bar.Valor < bar.Maximo, "roda sobe a grade");
                        bar.Ajustar(0);
                        foreach (ReadingState state in new ReadingState[] { ReadingState.Failed, ReadingState.Expired, ReadingState.Paused, ReadingState.Waiting, ReadingState.Live })
                        {
                            data.State = state; InvokePrivate(form, "AtualizarPaginaAtiva", true);
                            MetricCard cpu = cards[0];
                            Igual(state == ReadingState.Live ? (float?)40 : null, (float?)Field(cpu, "_valor"), "estado controla numero mostrado");
                            if (state != ReadingState.Live) Verdade(cpu.Sub.Length > 0, "estado indisponivel explicado no cartao");
                            CaptureEvolution(form, "bugs-state-" + language + "-" + state);
                        }
                        form.Close();
                    }
                }
            }
            finally { T.Language = languageBefore; }
        }
    }
}
