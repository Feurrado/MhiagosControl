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

            RegressionBugGeometry(root);
            RegressionNavOrder(root);
            RegressionMarcaDaPlaca();
            RegressionSlotsDeMemoria();
            RegressionNavegadorSegueCartao(root);
            RegressionLayoutsEscolhidos(root);
            CapturaDasMetricas(root);
            RegressionRevisao(root);
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
            string trocada = "sobre,visao,paineis,metricas,specs,perfis,config,baterias";
            Igual(trocada, junta(SettingsForm.OrdemDaBarra(fabrica, trocada)), "ordem gravada e respeitada");
            Igual(trocada, junta(SettingsForm.OrdemDaBarra(fabrica, "sobre,fantasma,visao,visao,paineis,metricas,specs,perfis,config,baterias")),
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
            config.NavOrder = "sobre,visao,paineis,metricas,specs,config";
            config.Save();
            using (SettingsForm form = BugForm(config, new FreshData()))
            {
                NavBar nav = (NavBar)Field(form, "_nav");
                Igual("sobre", nav.Items[0].Key, "janela abre com a ordem gravada");
                nav.SelectedIndex = 3;
                InvokePrivate(form, "AbrirPaginaPelaChave", "sobre");
                Igual("sobre", nav.Selected.Key, "clique no aparelho da Visao geral abre a pagina onde estiver");
                form.MostrarPagina("metricas");
                Igual("metricas", nav.Selected.Key, "clique no widget leva a janela aberta para a pagina pedida");
                form.MostrarPagina(null);
                Igual("metricas", nav.Selected.Key, "abrir pela bandeja nao mexe na pagina aberta");
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
                            foreach (int index in new int[] { 2, 5, 6 })
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
                                if (index == 5 && size.Width == 880 && sidebar == 330)
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
