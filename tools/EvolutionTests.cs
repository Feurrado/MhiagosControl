using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    public static partial class Tests
    {
        private static void RegressionEvolution(string root)
        {
            Secao("evolucao: backup completo, integridade e restauracao transacional");
            Config live = AuditConfig(root, "evolution");
            live.Language = T.EnUs; live.ShowAllSensors = true;
            live.WindowW = 1100; live.WindowH = 800; live.SidebarCollapsed = true;
            live.IdleBlankMinutes = 17; live.RotateSeconds = 12;
            live.Profiles[0].Rotate = true; live.Profiles[1].Rotate = true;
            live.MetricSizes.Add(2); live.MetricRange = 600;
            live.IdentificarJogo("regression-game.exe", "Jogo Á", "C:\\private-user\\Jogo.exe");
            MetricProfile grid = new MetricProfile { Name = "Grade Á", Range = 300 };
            grid.Ids.Add("cpu"); grid.Sizes.Add(1); live.MetricProfiles.Add(grid);
            string error; Verdade(live.Save(out error), "salva fixture isolada");
            string original = live.Clone().ToText();
            string backup = Path.Combine(root, "full.mhiagos-backup");
            ConfigurationBackup.Write(live, backup);
            Config restored = ConfigurationBackup.Read(backup);
            Igual(original, restored.ToText(), "backup preserva todos os campos serializados, Unicode e listas");
            Igual(original, live.ToText(), "exportacao nao altera configuracao viva");
            Verdade(!restored.Save(out error), "backup lido nao assume destino de gravacao");
            bool refused = false;
            try { ConfigurationBackup.Write(live, live.SavePath); } catch (IOException) { refused = true; }
            Verdade(refused, "exportacao nao pode substituir config.ini");
            File.WriteAllText(Path.Combine(root, "bad.backup"), File.ReadAllText(backup).Substring(0, 80));
            refused = false;
            try { ConfigurationBackup.Read(Path.Combine(root, "bad.backup")); } catch (FormatException) { refused = true; }
            Verdade(refused, "backup truncado e recusado antes de publicar");
            string[] lines = File.ReadAllLines(backup); lines[1] = "corrupted";
            File.WriteAllLines(Path.Combine(root, "bad.backup"), lines);
            refused = false;
            try { ConfigurationBackup.Read(Path.Combine(root, "bad.backup")); } catch (FormatException) { refused = true; }
            Verdade(refused, "checksum divergente e recusado");
            lines = File.ReadAllLines(backup); lines[0] = "MHIAGOS-CONFIG-BACKUP/99";
            File.WriteAllLines(Path.Combine(root, "bad.backup"), lines);
            refused = false;
            try { ConfigurationBackup.Read(Path.Combine(root, "bad.backup")); } catch (FormatException) { refused = true; }
            Verdade(refused, "versao futura nao e interpretada silenciosamente");
            string huge = Path.Combine(root, "huge.backup");
            using (FileStream large = File.Create(huge)) large.SetLength(4 * 1024 * 1024 + 1);
            refused = false;
            try { ConfigurationBackup.Read(huge); } catch (FormatException) { refused = true; }
            Verdade(refused, "backup grande e recusado antes de ler o conteudo");
            SettingsSession session = new SettingsSession(live);
            session.Draft.IdleBlankMinutes = 77;
            ConfigurationBackup.Write(session.SavedConfiguration, backup);
            Igual(17, ConfigurationBackup.Read(backup).IdleBlankMinutes, "exporta estado salvo, nao edicao pendente");
            restored.IdleBlankMinutes = 99; restored.ActiveName = "C";
            string safety;
            using (FileStream locked = File.Open(live.SavePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Verdade(!session.TryRestore(restored, out safety, out error), "falha de disco impede restauracao");
            Igual(17, live.IdleBlankMinutes, "falha nao publica candidato");
            Igual(77, session.Draft.IdleBlankMinutes, "falha preserva edicao pendente");
            Verdade(session.TryRestore(restored, out safety, out error), "restauracao termina apos liberar disco");
            Igual(17, ConfigurationBackup.Read(safety).IdleBlankMinutes, "copia de seguranca guarda o estado anterior");
            Igual(99, Config.LoadFrom(live.SavePath, false).IdleBlankMinutes, "restauracao grava no destino local");
            Igual("C", session.Draft.ActiveName, "restauracao atualiza rascunho e ativo de forma explicita");
            Igual(17, ConfigurationBackup.Read(backup).IdleBlankMinutes, "arquivo importado permaneceu intacto");

            Secao("evolucao: jogo por arquivo e diagnostico sem dados pessoais");
            string executable = Path.Combine(root, "Jogo Á.EXE"); File.WriteAllText(executable, "nao executar");
            GameFileBinding.Bind(session.Draft, executable, "A");
            Igual("A", session.Draft.PerfilDoJogo("jogo á.exe"), "vinculo casa pelo nome independentemente da caixa");
            Igual(Path.GetFullPath(executable), session.Draft.CaminhoDoJogo("Jogo Á.EXE"), "caminho preservado para identidade");
            Verdade(live.PerfilDoJogo("Jogo Á.EXE") == null, "cadastrar jogo nao aplica antes de salvar");
            GameFileBinding.Bind(session.Draft, executable, "B");
            Igual("B", session.Draft.PerfilDoJogo("Jogo Á.EXE"), "substituir vinculo usa perfil novo");
            Igual(2, session.Draft.GameKeys.Count, "substituicao nao duplica executavel");
            refused = false;
            try { GameFileBinding.Bind(session.Draft, backup, "A"); } catch (ArgumentException) { refused = true; }
            Verdade(refused, "cadastro recusa arquivo sem extensao exe");
            List<SensorEntry> sensors = new List<SensorEntry> {
                AuditSensor("cpu", "CPU", SensorType.Temperature, "C", 42),
                AuditSensor("load", "CPU", SensorType.Load, "%", 25),
                AuditSensor("gpu", "GPU", SensorType.Temperature, "C", 54),
                AuditSensor("fan", "CPU", SensorType.Fan, "RPM", 1100) };
            sensors[0].Name = "private-sensor";
            AuditData data = new AuditData(sensors); data.Values["fan"] = float.NaN;
            data.Status = new PanelStatus(new PanelFrame(42, 25, false, true, live.Active), false, true);
            string report = SupportReport.Build(live, sensors, data.Values, data.Status, false, DateTime.UtcNow);
            Verdade(!report.Contains("private-") && !report.Contains("Jogo") && !report.Contains(root), "diagnostico exclui nomes e caminhos");
            Verdade(report.Contains("Panel connected: False") && report.Contains("Panel paused: True"), "diagnostico distingue pausa e desconexao");
            Verdade(report.Contains("4 / 3"), "contagem de leituras ignora NaN");
            sensors[0].Source = "private-source"; sensors[1].Source = null;
            report = SupportReport.Build(live, sensors, data.Values, null, false, DateTime.UtcNow);
            Verdade(!report.Contains("private-source") && report.Contains("Other: 2 / 2"), "fonte nula ou desconhecida nao expoe texto arbitrario");
            live.Profiles[0].Panel1Id = "missing"; live.MetricIds.Add("missing");
            Igual(1, ConfigurationBackup.MissingSensors(live, sensors), "aviso de sensores ausentes nao conta duplicatas");

            Secao("evolucao: janela, teclado e geometria das telas");
            Size fit = SettingsForm.FitInitialClient(new Size(1010, 900), new Size(1366, 728));
            Verdade(fit.Height <= 648 && fit.Width <= 1326, "primeira abertura cabe na area de trabalho de notebook");
            using (NavBar nav = new NavBar())
            {
                nav.Add(new NavItem { Text = "one" }); nav.Add(new NavItem { Text = "two" });
                InvokePrivate(nav, "OnKeyDown", new KeyEventArgs(Keys.Down));
                Igual(1, nav.SelectedIndex, "seta para baixo navega");
                InvokePrivate(nav, "OnKeyDown", new KeyEventArgs(Keys.Home));
                Igual(0, nav.SelectedIndex, "Home volta a primeira pagina");
            }
            foreach (string language in new string[] { T.PtBr, T.EnUs })
            {
                T.Language = language;
                Config screen = AuditConfig(root, "visual-" + language);
                using (SettingsForm form = new SettingsForm(screen, sensors, data))
                {
                    form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-20000, -20000);
                    try
                    {
                        form.Show();
                        ((System.Windows.Forms.Timer)Field(form, "_tick")).Stop();
                        foreach (int width in new int[] { 880, 1100 })
                        {
                            form.Size = new Size(width, 740);
                            ((NavBar)Field(form, "_nav")).SelectedIndex = 5;
                            InvokePrivate(form, "ArrangeSettings");
                            Control page = (Control)Field(form, "_pgConfig");
                            foreach (Control card in page.Controls)
                                if (card is Card)
                                {
                                    Verdade(card.Right <= page.Width, "cartao cabe em Configuracoes " + width + " " + language);
                                    foreach (Control child in card.Controls)
                                        Verdade(child.Right <= card.Width && child.Bottom <= card.Height,
                                            "controle cabe: " + child.GetType().Name + " " + child.Text + " / " + width);
                                }
                            CaptureEvolution(form, "settings-" + language + "-" + width);
                            CaptureEvolution((Control)Field(form, "_backupCard"), "backup-" + language + "-" + width);
                            CaptureEvolution((Control)Field(form, "_diagnosticCard"), "diagnostic-" + language + "-" + width);
                            ((NavBar)Field(form, "_nav")).SelectedIndex = 4;
                            InvokePrivate(form, "ArrangeProfiles");
                            Control profiles = (Control)Field(form, "_pgPerfis");
                            foreach (Control card in profiles.Controls)
                                if (card is Card)
                                    Verdade(card.Right <= profiles.Width, "Perfis cabe na largura " + width);
                            CaptureEvolution(form, "profiles-" + language + "-" + width);
                        }
                        form.Size = new Size(1120, 850);
                        ((NavBar)Field(form, "_nav")).SelectedIndex = 4;
                        CaptureEvolution((Control)Field(form, "_cardGame"), "games-" + language);
                        form.Size = new Size(880, 740);
                        NavBar sidebar = (NavBar)Field(form, "_nav");
                        sidebar.Subtitle = "Perfil com um nome muito comprido para testar a largura";
                        sidebar.AjustarLargura(); sidebar.SelectedIndex = 5;
                        InvokePrivate(form, "ArrangeSettings");
                        Control narrowPage = (Control)Field(form, "_pgConfig");
                        foreach (Control card in narrowPage.Controls)
                            if (card is Card)
                                foreach (Control child in card.Controls)
                                    Verdade(child.Right <= card.Width && child.Bottom <= card.Height,
                                        "controle cabe com barra larga: " + child.Text);
                        CaptureEvolution(form, "settings-wide-sidebar-" + language);
                        sidebar.SelectedIndex = 4;
                        CaptureEvolution((Control)Field(form, "_cardGame"), "games-narrow-" + language);
                        InvokePrivate(form, "ProcessCmdKey", new Message(), Keys.Alt | Keys.D6);
                        Igual(5, sidebar.SelectedIndex, "Alt+6 abre Configuracoes");
                        Config edit = (Config)Field(form, "_cfg");
                        edit.IdleBlankMinutes = 23;
                        typeof(SettingsForm).GetProperty("_dirty", AuditFlags).SetValue(form, true, null);
                        InvokePrivate(form, "ProcessCmdKey", new Message(), Keys.Control | Keys.S);
                        Igual(23, Config.LoadFrom(screen.SavePath, false).IdleBlankMinutes, "Ctrl+S persiste a edicao isolada");
                        using (TextReviewDialog preview = new TextReviewDialog(T.RestorePreview,
                            T.RestoreNote + "\r\n\r\n" + T.SavedProfiles + ": 3\r\n" + T.GameBindings + ": 1", T.RestoreApply))
                        {
                            preview.StartPosition = FormStartPosition.Manual; preview.Location = new Point(-20000, -20000);
                            preview.Show(); CaptureEvolution(preview, "restore-" + language); preview.Close();
                        }
                        using (TextReviewDialog diagnostic = new TextReviewDialog(T.DiagnosticTitle, report, T.SaveFile))
                        {
                            diagnostic.StartPosition = FormStartPosition.Manual; diagnostic.Location = new Point(-20000, -20000);
                            diagnostic.Show(); CaptureEvolution(diagnostic, "diagnostic-preview-" + language); diagnostic.Close();
                        }
                    }
                    finally { form.Close(); }
                }
            }
        }

        private static void CaptureEvolution(Control control, string name)
        {
            string directory = Environment.GetEnvironmentVariable("MHIAGOS_VISUAL_DIR");
            if (string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);
            using (Bitmap bitmap = new Bitmap(control.Width, control.Height))
            {
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
            }
        }
    }
}
