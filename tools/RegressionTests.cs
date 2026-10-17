using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    public static partial class Tests
    {
        private const BindingFlags AuditFlags = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;

        private static object Field(object target, string name)
        { return target.GetType().GetField(name, AuditFlags).GetValue(target); }
        private static void Field(object target, string name, object value)
        { target.GetType().GetField(name, AuditFlags).SetValue(target, value); }
        private static void StaticField(Type type, string name, object value)
        { type.GetField(name, AuditFlags).SetValue(null, value); }
        private static object InvokePrivate(object target, string name, params object[] args)
        { return target.GetType().GetMethod(name, AuditFlags).Invoke(target, args); }

        private static void RegressoesAuditoria()
        {
            string root = Path.Combine(Path.GetTempPath(), "MhiagosControl-regression-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string log = Log.Path;
            string language = T.Language;
            string game = Rtss.JogoAtual;
            StaticField(typeof(Log), "_path", Path.Combine(root, "test.log"));
            T.Language = T.PtBr;
            try
            {
                RegressionUnits();
                RegressionHistory();
                RegressionWindow(root);
                RegressionSession(root);
                RegressionGames(root);
                RegressionWorker(root);
                RegressionInstaller(root);
                RegressionEvolution(root);
                RegressionBugFixes(root);
            }
            finally
            {
                MetricHistory.Seguir(null);
                StaticField(typeof(MetricHistory), "_balde", 0L);
                StaticField(typeof(Log), "_path", log);
                StaticField(typeof(Rtss), "_jogo", game);
                T.Language = language;
                // Somente o diretorio exclusivo criado por esta execucao.
                string full = Path.GetFullPath(root);
                if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(full).StartsWith("MhiagosControl-regression-", StringComparison.Ordinal))
                    Directory.Delete(full, true);
            }
        }

        private static SensorEntry AuditSensor(string id, string category, SensorType type, string unit, float value)
        {
            return new SensorEntry { Id = id, Hardware = category, Category = category,
                Name = id, Label = id, Type = type, Unit = unit, Value = value };
        }

        private static void RegressionUnits()
        {
            Secao("regressao: unidades, escala e ausencia de sensor");
            List<SensorEntry> sensors = new List<SensorEntry> {
                AuditSensor("cpu", "CPU", SensorType.Temperature, null, 40),
                AuditSensor("load", "CPU", SensorType.Load, null, 20),
                AuditSensor("gpu", "GPU", SensorType.Temperature, null, 50) };
            Igual(3, MetricPicker.Montar(null, sensors).Count, "F01: conjunto automatico aceita sensores Libre sem unidade explicita");
            Igual("°C", sensors[0].EffectiveUnit, "temperatura tem unidade de reserva, com o grau");
            Verdade(LibreSensorSource.Dono("/amdcpu/0", new string[] { "/amdcpu/0/load/0" }), "o hardware e dono do sensor dele");
            Verdade(!LibreSensorSource.Dono("/amdcpu/0", new string[] { "/amdcpu/01/load/0" }), "prefixo parcial nao conta (/amdcpu/0 x /amdcpu/01)");
            Verdade(!LibreSensorSource.Dono("/amdcpu/0", new string[] { "hw:ACPI|1|TZ", null }), "sensor do HWiNFO nao pede o hardware da biblioteca");
            Igual("MB", new SensorEntry { Type = SensorType.SmallData }.EffectiveUnit, "VRAM Libre em MB");
            Igual("%", new SensorEntry { Type = SensorType.Control }.EffectiveUnit, "PWM em porcentagem");
            foreach (string unit in new string[] { "KB/s", "MB/s" })
            {
                SensorEntry s = AuditSensor("disk", "Disco", SensorType.Throughput, unit, 500);
                Igual(500, Scaling.Prepare(s, Scaling.Effective(0, s), false).Value, "F02: 500 " + unit + " nao recebe divisao duplicada");
                Igual(50, Scaling.Prepare(s, Scaling.Effective(10, s), false).Value, "divisor manual permanece literal");
            }
            SensorEntry bytes = AuditSensor("disk", "Disco", SensorType.Throughput, "B/s", 500000);
            Igual(500, Scaling.Prepare(bytes, Scaling.Effective(0, bytes), false).Value, "bytes/s mantem escala automatica");
            bytes.Value = 3000000000f;
            PanelValue high = Scaling.Prepare(bytes, 1, false);
            Igual(999, high.Value, "F03: limita antes de converter para inteiro");
            Verdade(high.Clamped, "overflow informa saturacao");
            bytes.Value = float.MaxValue;
            Igual(999, Scaling.Prepare(bytes, 1, false).Value, "maior float finito tambem satura");

            using (SensorPicker picker = new SensorPicker())
            {
                SensorEntry fps = AuditSensor("rtss:fps", "Jogos", SensorType.Factor, "FPS", 120);
                picker.SetSensors(new List<SensorEntry> { fps }); picker.SelectedId = fps.Id;
                picker.UpdateValues(new Dictionary<string, float>());
                Verdade(!picker.Selected.Value.HasValue, "F08: ausencia limpa 120 FPS antigos");
                picker.UpdateValues(new Dictionary<string, float> { { fps.Id, 60 } });
                Igual(60, picker.Selected.Value, "sensor pode voltar a responder");
                picker.UpdateValues(new Dictionary<string, float> { { fps.Id, float.NaN } });
                Verdade(!picker.Selected.Value.HasValue, "leitura invalida tambem limpa o valor");
            }
        }

        private static void ResetHistory(params string[] ids)
        {
            MetricHistory.Seguir(null);
            StaticField(typeof(MetricHistory), "_balde", 0L);
            MetricHistory.Seguir(ids);
        }

        private static float[] History(string id)
        {
            float[] values = null;
            MetricHistory.Janela(id, 3600, ref values);
            return values;
        }

        private static void RegressionHistory()
        {
            Secao("regressao: horario das amostras e retomada");
            DateTime t = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
            ResetHistory("cpu");
            MetricHistory.AmostrarEm(delegate { return 40f; }, t);
            MetricHistory.AmostrarEm(delegate { return 80f; }, t.AddSeconds(5));
            float[] values = History("cpu");
            Igual(40, values[values.Length - 1], "F04: leitura do novo balde nao altera o anterior");
            MetricHistory.AmostrarEm(delegate { return 100f; }, t.AddMinutes(10));
            values = History("cpu");
            Verdade(float.IsNaN(values[values.Length - 1]), "retomada nao preenche a lacuna anterior");
            MetricHistory.AmostrarEm(delegate { return (float?)null; }, t.AddMinutes(10).AddSeconds(5));
            values = History("cpu");
            Igual(100, values[values.Length - 1], "amostra da retomada fica no seu proprio instante");
            Igual(t.AddMinutes(10).ToLocalTime(), MetricHistory.FimDaJanela(), "eixo temporal corresponde ao balde amostrado");
            MetricHistory.AmostrarEm(delegate { return 70f; }, t.AddSeconds(-5));
            MetricHistory.AmostrarEm(delegate { return (float?)null; }, t);
            values = History("cpu");
            Igual(70, values[values.Length - 1], "recuo do relogio nao paralisa novas amostras");
            Verdade(float.IsNaN(values[values.Length - 2]), "dados futuros nao sao datados no passado");
        }

        private static Config AuditConfig(string root, string file)
        {
            Config config = Config.LoadFrom(Path.Combine(root, file + ".ini"), false);
            config.Profiles.Clear();
            config.Profiles.Add(new Profile { Name = "A", Panel1Id = "cpu", Panel2Id = "load" });
            config.Profiles.Add(new Profile { Name = "B", Panel1Id = "gpu", Panel2Id = "load" });
            config.Profiles.Add(new Profile { Name = "C", Panel1Id = "fan", Panel2Id = "load" });
            config.ActiveName = "A"; config.DefaultProfileName = "A";
            config.GameProfiles = true; config.MapearJogo("regression-game.exe", "B");
            config.MetricsChosen = true; config.MetricIds.Add("fan");
            string error;
            if (!config.Save(out error)) throw new Exception(error);
            return config;
        }

        private static void RegressionWindow(string root)
        {
            Secao("regressao: historico compartilhado e estado real do painel");
            List<SensorEntry> sensors = new List<SensorEntry> {
                AuditSensor("cpu", "CPU", SensorType.Temperature, null, 40),
                AuditSensor("load", "CPU", SensorType.Load, null, 20),
                AuditSensor("gpu", "GPU", SensorType.Temperature, null, 50),
                AuditSensor("fan", "CPU", SensorType.Fan, null, 1200) };
            ResetHistory("cpu", "load", "gpu", "fan");
            DateTime t = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
            MetricHistory.AmostrarEm(delegate { return 40f; }, t);
            MetricHistory.AmostrarEm(delegate { return (float?)null; }, t.AddSeconds(5));
            Config config = AuditConfig(root, "window");
            AuditData data = new AuditData(sensors);
            data.Status = new PanelStatus(new PanelFrame(40, 20, false, true, config.Active), true, false);
            using (SettingsForm form = new SettingsForm(config, sensors, data))
            {
                try
                {
                    float[] values = History("cpu");
                    Igual(40, values[values.Length - 1], "F05: criar a grade nao apaga a serie da Visao geral");
                    InvokePrivate(form, "MontarMetricas");
                    values = History("cpu");
                    Igual(40, values[values.Length - 1], "reconstruir a grade tambem preserva historico");
                    data.Values["cpu"] = 95;
                    data.Status = new PanelStatus(new PanelFrame(90, 30, true, false, config.Active), true, false);
                    InvokePrivate(form, "AtualizarPaginaAtiva", false);
                    PanelPreview preview = (PanelPreview)Field(form, "_vgPreview");
                    Igual(90, preview.Value1, "F06: previa acompanha quadro enviado, mesmo com snapshot diferente");
                    Verdade(preview.Fahrenheit && !preview.Percent, "flags da previa sao as do quadro enviado");
                    Config draft = (Config)Field(form, "_cfg");
                    ((ProfileList)Field(form, "_profileList")).Selected = draft.Profiles[1];
                    InvokePrivate(form, "AtualizarPaginaAtiva", false);
                    Igual("A", ((Label)Field(form, "_vgPerfil")).Text, "F07: selecionar B nao o apresenta como ativo");
                    Igual("A", ((NavBar)Field(form, "_nav")).Subtitle, "lateral tambem conserva o perfil realmente ativo");
                    Igual(90, preview.Value1, "previa de edicao nao escreve por cima da Visao geral");
                    data.Status = new PanelStatus(new PanelFrame(120, 30, false, true, config.Profiles[2]), true, false);
                    InvokePrivate(form, "AtualizarPaginaAtiva", false);
                    Igual("C", ((Label)Field(form, "_vgPerfil")).Text, "Visao geral acompanha perfil do rodizio");
                    data.Status = new PanelStatus(data.Status.Frame, true, true);
                    InvokePrivate(form, "AtualizarPaginaAtiva", false);
                    Verdade(!preview.Value1.HasValue, "pausa apaga a previa");
                    data.Status = new PanelStatus(data.Status.Frame, false, false);
                    InvokePrivate(form, "AtualizarPaginaAtiva", false);
                    Verdade(!preview.Value2.HasValue, "desconexao nao aparenta envio valido");

                    data.Values.Clear();
                    ((NavBar)Field(form, "_nav")).SelectedIndex = 4;
                    InvokePrivate(form, "AtualizarPaginaAtiva", false);
                    Verdade(!((PanelPreview)Field(form, "_profilePreview")).Value1.HasValue,
                        "F08: previa do perfil limpa sensor ausente sem visitar Paineis");
                }
                finally { ((System.Windows.Forms.Timer)Field(form, "_tick")).Dispose(); }
            }
        }

        private static void RegressionSession(string root)
        {
            Secao("regressao: rascunho versus ativacao externa");
            Config live = AuditConfig(root, "session");
            SettingsSession session = new SettingsSession(live);
            string error;
            Verdade(ProfileActivation.TryActivate(live, "B", out error), "bandeja ativa B");
            session.Draft.IdleBlankMinutes = 5;
            Verdade(session.TrySaveAll(out error), "salva preferencia do rascunho");
            Igual("B", live.ActiveName, "F09: salvar preferencia preserva ativacao externa");
            Igual("B", Config.LoadFrom(Path.Combine(root, "session.ini"), false).ActiveName, "preservacao tambem chega ao disco");
            Igual(5, live.IdleBlankMinutes, "edicao solicitada chega ao vivo");
            session.Draft.ActiveName = "A";
            Verdade(session.TrySaveAll(true, out error), "aplicar explicitamente A continua permitido");
            Igual("A", live.ActiveName, "aplicacao explicita tem precedencia");
            session.Draft.IdleBlankMinutes = 10;
            using (FileStream locked = File.Open(Path.Combine(root, "session.ini"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Verdade(!session.TrySaveAll(out error), "falha de disco e devolvida");
            Igual(5, live.IdleBlankMinutes, "falha nao publica rascunho");

            Config formConfig = AuditConfig(root, "session-window");
            List<SensorEntry> list = new List<SensorEntry> {
                AuditSensor("cpu", "CPU", SensorType.Temperature, "C", 40),
                AuditSensor("load", "CPU", SensorType.Load, "%", 20),
                AuditSensor("gpu", "GPU", SensorType.Temperature, "C", 50) };
            using (SettingsForm form = new SettingsForm(formConfig, list, new AuditData(list)))
            {
                try
                {
                    ProfileActivation.TryActivate(formConfig, "B", out error);
                    Config draft = (Config)Field(form, "_cfg");
                    draft.IdleBlankMinutes = 8;
                    InvokePrivate(form, "OnSave", null, EventArgs.Empty);
                    Igual("B", formConfig.ActiveName, "F09: botao Salvar preserva escolha externa");
                    draft.IdleBlankMinutes = 9;
                    InvokePrivate(form, "OnSave", null, EventArgs.Empty);
                    Igual("B", formConfig.ActiveName, "F09: segundo Salvar tambem preserva escolha externa");
                    ((ProfileList)Field(form, "_profileList")).Selected = draft.Profiles[2];
                    InvokePrivate(form, "OnSave", null, EventArgs.Empty);
                    Igual("C", formConfig.ActiveName, "selecionar outro perfil e salvar ainda o ativa");
                    ((ProfileList)Field(form, "_profileList")).Selected = draft.Profiles[0];
                    InvokePrivate(form, "AplicarPerfil");
                    Igual("A", formConfig.ActiveName, "Aplicar perfil tem precedencia explicita");
                }
                finally
                {
                    ((System.Windows.Forms.Timer)Field(form, "_tick")).Dispose();
                    System.Windows.Forms.Timer note = (System.Windows.Forms.Timer)Field(form, "_noteTimer");
                    if (note != null) note.Dispose();
                }
            }
        }

        private static TrayContext AuditContext(Config config, AuditSensors sensors)
        {
            // Nao executar o construtor: ele registra bandeja, autostart e fontes reais.
            TrayContext context = (TrayContext)FormatterServices.GetUninitializedObject(typeof(TrayContext));
            Field(context, "_cfg", config); Field(context, "_sensors", sensors);
            Field(context, "_sensorLock", new object()); Field(context, "_cache", new List<SensorEntry>());
            Field(context, "_miProfiles", new ToolStripMenuItem("Profiles"));
            Field(context, "_live", config.Active.Clone()); Field(context, "_sensorCycle", new SensorCycle(sensors));
            Field(context, "_fastSensors", sensors);
            Field(context, "_stop", new ManualResetEvent(false));
            return context;
        }

        private static void DisposeAuditContext(TrayContext context)
        {
            ((ToolStripMenuItem)Field(context, "_miProfiles")).Dispose();
            ((ManualResetEvent)Field(context, "_stop")).Dispose();
        }

        private static void GameTick(TrayContext context)
        { InvokePrivate(context, "OnVigiaDeJogo", null, EventArgs.Empty); }

        private static void RegressionGames(string root)
        {
            Secao("regressao: vinculos de jogo, retentativa e retorno ao padrao");
            Config game = AuditConfig(root, "game");
            TrayContext context = AuditContext(game, new AuditSensors());
            try
            {
                StaticField(typeof(Rtss), "_jogo", "regression-game.exe");
                GameTick(context);
                Igual("B", game.ActiveName, "jogo inicia com o perfil vinculado");
                game.MapearJogo("regression-game.exe", "C");
                Field(context, "_janelaAberta", true); GameTick(context);
                Igual("B", game.ActiveName, "aguarda terminar a edicao");
                Field(context, "_janelaAberta", false); GameTick(context);
                Igual("C", game.ActiveName, "F10: fechar a janela reavalia novo vinculo sem reabrir jogo");
                using (ToolStripMenuItem manual = new ToolStripMenuItem("B"))
                {
                    manual.Tag = game.Profiles[1];
                    InvokePrivate(context, "OnPickProfile", manual, EventArgs.Empty);
                }
                GameTick(context);
                Igual("B", game.ActiveName, "escolha manual dura enquanto o jogo continua");
                StaticField(typeof(Rtss), "_jogo", null);
                using (FileStream locked = File.Open(Path.Combine(root, "game.ini"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    GameTick(context);
                Igual("B", game.ActiveName, "retorno falho nao finge aplicar padrao");
                GameTick(context);
                Igual("A", game.ActiveName, "F12: fim do jogo retorna ao padrao, inclusive apos falha");
                StaticField(typeof(Rtss), "_jogo", "regression-game.exe");
                using (FileStream locked = File.Open(Path.Combine(root, "game.ini"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    GameTick(context);
                Igual("A", game.ActiveName, "ativacao falha conserva estado anterior");
                GameTick(context);
                Igual("C", game.ActiveName, "F11: ativacao tenta novamente depois de liberar arquivo");
                game.DesmapearJogo("regression-game.exe"); GameTick(context);
                Igual("A", game.ActiveName, "remover vinculo devolve ao padrao");
                game.MapearJogo("regression-game.exe", "B");
                using (ToolStripMenuItem manual = new ToolStripMenuItem("C"))
                {
                    manual.Tag = game.Profiles[2];
                    InvokePrivate(context, "OnPickProfile", manual, EventArgs.Empty);
                }
                GameTick(context);
                Igual("C", game.ActiveName, "escolha manual anterior ao tick tambem dura ate o fim do jogo");
                StaticField(typeof(Rtss), "_jogo", null); GameTick(context);
                Igual("A", game.ActiveName, "escolha manual anterior ao tick ainda retorna ao padrao");
            }
            finally { DisposeAuditContext(context); }
        }

        private static void RegressionWorker(string root)
        {
            Secao("regressao: falhas de leitura e validade do keepalive");
            Config config = AuditConfig(root, "worker");
            AuditSensors sensors = new AuditSensors(); sensors.Fail = true;
            TrayContext context = AuditContext(config, sensors);
            Field(context, "_snapshotWanted", true);
            Field(context, "_snapshot", ReadingSnapshot.Full(new Dictionary<string, float> { { "cpu", 83 } }, Stopwatch.GetTimestamp()));
            ResetHistory();
            Exception failure = null;
            Thread worker = new Thread(delegate()
            {
                try { InvokePrivate(context, "WorkerLoop"); }
                catch (Exception ex) { failure = ex; }
            });
            worker.IsBackground = true;
            try
            {
                worker.Start();
                Verdade(sensors.Entered.WaitOne(2000), "worker chegou a fonte simulada");
                Thread.Sleep(400);
            }
            finally
            {
                ((ManualResetEvent)Field(context, "_stop")).Set();
                Verdade(worker.Join(2000), "worker encerra apos falha");
                Igual(0, ((ISettingsData)context).CurrentSnapshot().Count, "falha do worker invalida instantaneo da interface");
                Igual(ReadingState.Failed, ((IReadingStatusData)context).CurrentReadingState("cpu"), "falha publicada para a interface");
                DisposeAuditContext(context); sensors.Entered.Dispose();
            }
            Verdade(failure == null, "falha da fonte e tratada pelo ciclo");
            Verdade(sensors.Reads <= 2, "F13: falha nao dispara tempestade de retentativas (" + sensors.Reads + ")");
            Igual(0, sensors.FastReads, "ciclo rapido nao republica hardware velho depois de falha completa");

            AuditPanel panel = new AuditPanel();
            using (PanelKeepalive keepalive = new PanelKeepalive(panel, null))
            {
                PanelFrame frame = new PanelFrame(60, 20, false, true, config.Active);
                keepalive.Publish(frame); keepalive.DispatchOnce(frame.SampleTicks);
                Igual(60, panel.P1, "keepalive envia leitura nova");
                keepalive.DispatchOnce(frame.SampleTicks + 6 * Stopwatch.Frequency);
                Verdade(!panel.P1.HasValue && !keepalive.LastFrame.Panel1.HasValue, "leitura vencida apaga painel e estado publicado");
                PanelFrame fresh = new PanelFrame(60, 20, false, true, config.Active);
                keepalive.Publish(fresh); keepalive.DispatchOnce(fresh.SampleTicks);
                Igual(60, panel.P1, "valor igual mas renovado volta a ser valido");
                panel.Succeed = false;
                keepalive.Publish(new PanelFrame(80, 40, false, true, config.Profiles[1]));
                keepalive.DispatchOnce();
                Verdade(!keepalive.LastSent, "falha de HID e publicada");
                Igual(60, keepalive.LastFrame.Panel1, "quadro que falhou nao e anunciado como enviado");
            }
        }

        private sealed class AuditData : ISettingsData, IPanelStatusData
        {
            public readonly Dictionary<string, float> Values = new Dictionary<string, float>();
            private readonly List<SensorEntry> _sensors;
            public PanelStatus Status;
            public AuditData(List<SensorEntry> sensors)
            { _sensors = sensors; foreach (SensorEntry s in sensors) Values[s.Id] = s.Value.Value; }
            public Dictionary<string, float> CurrentSnapshot() { return Values; }
            public List<SensorEntry> RefreshSensorList() { return _sensors; }
            public void SetShowAllSensors(bool showAll) { }
            public PanelStatus CurrentPanelStatus() { return Status; }
        }

        private sealed class AuditSensors : ISensorService, IFastSensorService
        {
            public bool Fail;
            public int Reads;
            public int FastReads;
            public readonly ManualResetEvent Entered = new ManualResetEvent(false);
            public bool ShowAll { get; set; }
            public void Open() { }
            public void Refresh() { Interlocked.Increment(ref Reads); Entered.Set(); if (Fail) throw new IOException("falha simulada"); }
            public void Focar(IEnumerable<string> ids) { }
            public List<SensorEntry> List() { return new List<SensorEntry>(); }
            public Dictionary<string, float> Snapshot() { return new Dictionary<string, float>(); }
            public void RefreshFast() { Interlocked.Increment(ref FastReads); }
            public Dictionary<string, float> FastSnapshot() { return new Dictionary<string, float>(); }
            public SensorEntry ReadEntry(string id) { return null; }
            public void Dispose() { Entered.Dispose(); }
        }

        private sealed class AuditPanel : IPanelDevice
        {
            public int? P1;
            public bool Succeed = true;
            public bool Send(int? p1, int? p2, bool fahrenheit, bool percent) { if (Succeed) P1 = p1; return Succeed; }
            public void Close() { }
        }

        // Processos filhos limitados, sem instalacao ou alteracao do sistema.
        private static int RegressionChild(string[] args)
        {
            if (args.Length > 1 && args[1] == "pipes")
            {
                Console.Error.Write(new string('E', 100000));
                Console.Out.Write(new string('O', 100000));
                return 7;
            }
            Console.WriteLine("ready");
            Console.Out.Flush();
            Console.ReadLine();
            return 0;
        }

        private static ProcessStartInfo ChildStart(string mode)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                "--regression-child " + mode);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true;
            return psi;
        }

        private static void RegressionInstaller(string root)
        {
            Secao("regressao: timeout do instalador e limpeza apos encerramento");
            int code;
            string output = MhiagosSetup.Setup.RunProcess(ChildStart("pipes"), 10000, out code);
            Igual(7, code, "I02: processo com stdout e stderr volumosos termina");
            Igual(200000, output.Length, "os dois fluxos sao drenados integralmente");
            Stopwatch elapsed = Stopwatch.StartNew();
            MhiagosSetup.Setup.RunProcess(ChildStart("wait"), 200, out code);
            Igual(-1, code, "I02: processo bloqueado termina por timeout");
            Verdade(elapsed.ElapsedMilliseconds < 5000, "leitura de pipes nao impede timeout");

            string folder = Path.Combine(root, "limpeza ' $dado % acento");
            Directory.CreateDirectory(folder);
            string dummy = Path.Combine(folder, "uninstall.exe");
            string sentinel = Path.Combine(folder, "preservar.txt");
            File.WriteAllText(dummy, "arquivo simulado"); File.WriteAllText(sentinel, "preservar");
            using (Process parent = Process.Start(ChildStart("wait")))
            {
                try
                {
                    Igual("ready", parent.StandardOutput.ReadLine(), "processo simulado esta vivo");
                    string script = MhiagosSetup.Setup.CleanupScript(dummy, parent.Id);
                    ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,
                        @"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -EncodedCommand " +
                        Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
                    psi.UseShellExecute = false; psi.CreateNoWindow = true;
                    using (Process cleanup = Process.Start(psi))
                    {
                        try
                        {
                            Verdade(!cleanup.WaitForExit(1500), "I01: limpeza aguarda o processo, nao um atraso fixo");
                            Verdade(File.Exists(dummy), "desinstalador continua presente enquanto processo esta vivo");
                            parent.StandardInput.WriteLine("exit"); parent.StandardInput.Flush();
                            Verdade(cleanup.WaitForExit(10000), "limpeza termina depois do processo");
                            Verdade(!File.Exists(dummy), "remove apenas o executavel indicado");
                            Verdade(File.Exists(sentinel), "arquivo alheio na pasta e preservado");
                        }
                        finally { if (!cleanup.HasExited) { cleanup.Kill(); cleanup.WaitForExit(2000); } }
                    }
                }
                finally { if (!parent.HasExited) { parent.Kill(); parent.WaitForExit(2000); } }
            }
        }
    }
}
