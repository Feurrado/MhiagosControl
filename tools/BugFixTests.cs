using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
                            foreach (int index in new int[] { 2, 6 })
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
