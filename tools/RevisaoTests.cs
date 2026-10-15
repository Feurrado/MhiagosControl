using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Regressoes da revisao de 2026-10-15: um bloco por defeito corrigido, com
    /// o mesmo codigo do relatorio (B1, B2...) na descricao de cada conferencia.
    /// </summary>
    public static partial class Tests
    {
        private static void RegressionRevisao(string root)
        {
            RevisaoSairComJanelaAberta(root);
            RevisaoHistoricoDeBateriaPreso(root);
            RevisaoHistoricoDeMetricasIlegivel(root);
            RevisaoAvisosDepoisDoMostrador(root);
            RevisaoSplashFechadoCedo();
            RevisaoTrocaDeTema();
            RevisaoEscalaEEstados(root);
            RegressionAnimacoes(root);
        }

        private static void RevisaoEscalaEEstados(string root)
        {
            Secao("revisao V1, V5 e V7: escala, desabilitado e erro das Especificacoes");
            if (Ui.Escala == 1f)
                Verdade(Ui.S(15) == 15 && Ui.S(330) == 330 && Ui.S(0) == 0, "V1: a 100% as medidas de projeto ficam como estavam");
            Verdade(Ui.S(15) >= (int)(15 * Ui.Escala), "V1: a escala arredonda para cima, sem cortar texto");

            using (Toggle t = new Toggle())
            using (Bitmap ligado = new Bitmap(120, 26))
            using (Bitmap desligado = new Bitmap(120, 26))
            {
                t.Size = new Size(120, 26); t.Checked = true; t.Label = "x";
                t.DrawToBitmap(ligado, new Rectangle(0, 0, 120, 26));
                t.Enabled = false;
                t.DrawToBitmap(desligado, new Rectangle(0, 0, 120, 26));
                Verdade(ligado.GetPixel(4, 13).ToArgb() != desligado.GetPixel(4, 13).ToArgb(),
                        "V5: interruptor desabilitado tem outra cor no trilho");
            }

            FieldInfo folha = typeof(SpecSheet).GetField("_folha", AuditFlags);
            object antes = folha.GetValue(null);
            try
            {
                folha.SetValue(null, null);
                Config config = AuditConfig(root, "specs-falha");
                List<SensorEntry> nenhum = new List<SensorEntry>();
                using (SettingsForm form = new SettingsForm(config, nenhum, new AuditData(nenhum)))
                {
                    InvokePrivate(form, "MostrarSpecs");
                    FlatBtn botao = (FlatBtn)Field(form, "_btCopiarSpecs");
                    Label estado = (Label)Field(form, "_lbSpecsEstado");
                    Igual(T.SpecsRetry, botao.Text, "V7: coleta sem resultado oferece tentar de novo");
                    Verdade(botao.Enabled, "V7: e o botao fica habilitado");
                    Igual(T.SpecsFailed, estado.Text, "V7: o estado diz que falhou, e nao 'consultando' para sempre");
                }
            }
            finally { folha.SetValue(null, antes); }
        }

        private static void RevisaoSairComJanelaAberta(string root)
        {
            Secao("revisao B1: sair pela bandeja com a configuracao aberta e suja");
            Config config = AuditConfig(root, "sair");
            TrayContext context = AuditContext(config, new AuditSensors());
            try
            {
                List<SensorEntry> nenhum = new List<SensorEntry>();
                using (SettingsForm form = new SettingsForm(config, nenhum, new AuditData(nenhum)))
                {
                    GC.KeepAlive(form.Handle);   // sem handle o Close descarta sem FormClosing
                    typeof(SettingsForm).GetProperty("_dirty", AuditFlags).SetValue(form, true, null);
                    int perguntas = 0;
                    form.PerguntaAoFechar = delegate { perguntas++; return DialogResult.Cancel; };
                    Field(context, "_settingsForm", form);

                    Verdade(!context.Sair(true), "B1: Cancelar na pergunta cancela a saida");
                    Igual(1, perguntas, "B1: a pergunta aparece uma vez");
                    Igual(0, (int)Field(context, "_runtimeState"), "B1: saida cancelada nao desmonta threads, painel nem icone");
                    Verdade(!form.IsDisposed, "B1: a janela continua aberta e utilizavel");

                    Verdade(context.FecharConfiguracao(false), "B1: instalador e fim de sessao fecham sem depender de resposta");
                    Igual(1, perguntas, "B1: fechar sem perguntar nao abre dialogo");
                    Verdade(form.IsDisposed, "B1: a janela fechou");
                }
                Field(context, "_settingsForm", null);
                Verdade(context.FecharConfiguracao(true), "B1: sem janela aberta nada impede a saida");
            }
            finally { DisposeAuditContext(context); }
        }

        private static void RevisaoHistoricoDeBateriaPreso(string root)
        {
            Secao("revisao B2: historico de bateria preso por outro programa no arranque");
            object antes = typeof(HistoricoDeBateria).GetField("_caminho", AuditFlags).GetValue(null);
            string arq = Path.Combine(root, "baterias-preso.dat");
            try
            {
                HistoricoDeBateria.Caminho = arq;
                HistoricoDeBateria.Zerar();
                DateTime t = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Local);
                Periferico x11 = Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 80, false);
                HistoricoDeBateria.Registrar(new Periferico[] { x11 }, t);
                HistoricoDeBateria.Salvar();
                string original = File.ReadAllText(arq);
                Verdade(original.Contains("X11"), "o historico de partida guarda o aparelho");

                Periferico outro = Periferico.Lido("Fone", TipoPeriferico.Headset, FontePeriferico.Bluetooth, 50, true);
                using (new FileStream(arq, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    HistoricoDeBateria.Recarregar();   // a leitura falha: arquivo preso
                    HistoricoDeBateria.Registrar(new Periferico[] { outro }, t.AddMinutes(1));
                }
                HistoricoDeBateria.Salvar();   // o arquivo ja esta livre
                Igual(original, File.ReadAllText(arq), "B2: leitura que falhou nao deixa a gravacao apagar o historico");

                HistoricoDeBateria.Registrar(new Periferico[] { outro }, t.AddMinutes(2));
                HistoricoDeBateria.Salvar();
                string depois = File.ReadAllText(arq);
                Verdade(depois.Contains("X11") && depois.Contains("Fone"),
                        "B2: com o arquivo livre o historico antigo e lido e o novo aparelho entra junto");
            }
            finally
            {
                HistoricoDeBateria.Zerar();
                typeof(HistoricoDeBateria).GetField("_caminho", AuditFlags).SetValue(null, antes);
            }
        }

        private static void RevisaoHistoricoDeMetricasIlegivel(string root)
        {
            Secao("revisao B3: historico de metricas ilegivel ou preso");
            object antes = typeof(MetricHistory).GetField("_caminho", AuditFlags).GetValue(null);
            string pasta = Path.Combine(root, "metricas");
            Directory.CreateDirectory(pasta);
            string arq = Path.Combine(pasta, "history.dat");
            try
            {
                MetricHistory.Arquivo = arq;

                File.WriteAllBytes(arq, new byte[] { 1, 2, 3 });
                MetricHistory.Carregar();
                Verdade(!File.Exists(arq), "B3: arquivo estragado sai do caminho");
                Igual(1, Directory.GetFiles(pasta, "history.dat.ilegivel-*").Length, "B3: e fica guardado ao lado, e nao apagado");

                byte[] guardado = new byte[] { 9, 8, 7, 6, 5 };
                File.WriteAllBytes(arq, guardado);
                using (new FileStream(arq, FileMode.Open, FileAccess.Read, FileShare.None))
                    MetricHistory.Carregar();   // nao abre nem renomeia
                MetricHistory.Seguir(new string[] { "cpu" });
                MetricHistory.AmostrarEm(delegate { return 40f; }, DateTime.UtcNow);
                MetricHistory.Salvar();
                Igual(Convert.ToBase64String(guardado), Convert.ToBase64String(File.ReadAllBytes(arq)),
                      "B3: arquivo preso no arranque nao e substituido durante a sessao");
            }
            finally
            {
                typeof(MetricHistory).GetField("_semGravacao", AuditFlags).SetValue(null, false);
                typeof(MetricHistory).GetField("_caminho", AuditFlags).SetValue(null, antes);
                ResetHistory();
            }
        }

        private static void RevisaoAvisosDepoisDoMostrador(string root)
        {
            Secao("revisao B5: avisos do arranque nao seguram o mostrador");
            object arquivoAntes = typeof(MetricHistory).GetField("_caminho", AuditFlags).GetValue(null);
            MetricHistory.Arquivo = Path.Combine(root, "arranque-history.dat");
            Config config = AuditConfig(root, "arranque");
            AuditSensors sensors = new AuditSensors(); sensors.Fail = true;
            TrayContext context = AuditContext(config, sensors);
            Control marshal = new Control();
            GC.KeepAlive(marshal.Handle);
            PanelKeepalive keepalive = new PanelKeepalive(new AuditPanel(), null);
            Field(context, "_marshal", marshal);
            Field(context, "_panelKeepalive", keepalive);

            int avisos = 0;
            bool workerVivo = false;
            context.MostrarAviso = delegate(string texto, string titulo, MessageBoxButtons b, MessageBoxIcon i)
            {
                avisos++;
                Thread w = (Thread)Field(context, "_worker");
                workerVivo = w != null && w.IsAlive;
                return DialogResult.No;
            };
            try
            {
                InvokePrivate(context, "SensoresProntos", new IOException("fonte simulada ausente"));
                Verdade(Field(context, "_worker") != null, "B5: o worker parte no proprio arranque");
                Igual(0, avisos, "B5: nenhum dialogo antes de o mostrador andar");
                DateTime limite = DateTime.UtcNow.AddSeconds(20);
                while (avisos == 0 && DateTime.UtcNow < limite) { Application.DoEvents(); Thread.Sleep(10); }
                Verdade(avisos >= 1, "B5: a falha dos sensores ainda e avisada");
                Verdade(workerVivo, "B5: quando o aviso aparece o worker ja esta rodando");
            }
            finally
            {
                ((ManualResetEvent)Field(context, "_stop")).Set();
                Thread w = (Thread)Field(context, "_worker");
                if (w != null) w.Join(3000);
                keepalive.Stop(); keepalive.Wait(3000); keepalive.Dispose();
                System.Windows.Forms.Timer jogo = (System.Windows.Forms.Timer)Field(context, "_relogioDoJogo");
                if (jogo != null) { jogo.Stop(); jogo.Dispose(); }
                Microsoft.Win32.SystemEvents.SessionEnded -= (Microsoft.Win32.SessionEndedEventHandler)Delegate.CreateDelegate(
                    typeof(Microsoft.Win32.SessionEndedEventHandler), context,
                    typeof(TrayContext).GetMethod("OnSessionEnded", AuditFlags));
                marshal.Dispose();
                DisposeAuditContext(context);
                typeof(MetricHistory).GetField("_caminho", AuditFlags).SetValue(null, arquivoAntes);
                ResetHistory();
            }
        }

        private static void RevisaoSplashFechadoCedo()
        {
            Secao("revisao B6: tela de carregamento fechada logo depois de aberta");
            for (int i = 0; i < 3; i++)
            {
                Splash.Show("teste " + i);
                Splash.Close();
                Thread t = Splash.ThreadAtual;
                Verdade(t == null || t.Join(5000), "B6: fechar antes de a janela existir ainda a fecha (" + i + ")");
            }
        }

        private static double Luminancia(Color c)
        {
            Func<int, double> canal = delegate(int v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            };
            return 0.2126 * canal(c.R) + 0.7152 * canal(c.G) + 0.0722 * canal(c.B);
        }

        private static double Contraste(Color a, Color b)
        {
            double la = Luminancia(a), lb = Luminancia(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static void RevisaoTrocaDeTema()
        {
            Secao("revisao B7 e V3: troca de tema e contraste");
            Color textoEscuro = Color.FromArgb(unchecked((int)0xFFF0F0F3));
            Color textoClaro = Color.FromArgb(unchecked((int)0xFF1B1D21));
            Igual(textoClaro.ToArgb(), Ui.NoOutroTema(textoEscuro, true).ToArgb(), "B7: texto do tema escuro vira texto do claro");
            Igual(textoEscuro.ToArgb(), Ui.NoOutroTema(textoClaro, false).ToArgb(), "B7: e o caminho de volta tambem");

            using (Panel raiz = new Panel())
            {
                Label rotulo = new Label();
                rotulo.ForeColor = textoEscuro;
                rotulo.BackColor = Color.Transparent;
                Label verde = new Label();
                verde.ForeColor = PeripheralStrip.Verde;
                Panel dentro = new Panel();
                Label fundo = new Label();
                fundo.ForeColor = Color.FromArgb(unchecked((int)0xFF9699A3));   // Muted escuro
                dentro.Controls.Add(fundo);
                raiz.Controls.Add(rotulo); raiz.Controls.Add(verde); raiz.Controls.Add(dentro);

                Theme.Recolorir(raiz, true);
                Igual(textoClaro.ToArgb(), rotulo.ForeColor.ToArgb(), "B7: rotulo montado no escuro passa a ler no claro");
                Igual(Color.Transparent.ToArgb(), rotulo.BackColor.ToArgb(), "B7: fundo transparente fica transparente");
                Igual(PeripheralStrip.Verde.ToArgb(), verde.ForeColor.ToArgb(), "B7: cor fora da paleta nao muda");
                Igual(unchecked((int)0xFF5F646E), fundo.ForeColor.ToArgb(), "B7: controles aninhados tambem mudam");
            }

            Color janelaClara = Color.FromArgb(unchecked((int)0xFFF4F5F7));
            Color superficieClara = Color.White;
            Color superficieEscura = Color.FromArgb(unchecked((int)0xFF26262A));
            Color faintClaro = Ui.NoOutroTema(Color.FromArgb(unchecked((int)0xFF8A8E99)), true);
            Color mutedClaro = Ui.NoOutroTema(Color.FromArgb(unchecked((int)0xFF9699A3)), true);
            Color azulClaro = Ui.NoOutroTema(Color.FromArgb(unchecked((int)0xFF6AA6FF)), true);
            Verdade(Contraste(faintClaro, janelaClara) >= 4.5, "V3: Faint claro legivel sobre a janela (" + Contraste(faintClaro, janelaClara).ToString("0.00") + ":1)");
            Verdade(Contraste(mutedClaro, janelaClara) > Contraste(faintClaro, janelaClara), "V3: Muted continua um degrau acima de Faint");
            Verdade(Contraste(azulClaro, superficieClara) >= 4.5 && Contraste(azulClaro, janelaClara) >= 4.5,
                    "V3: azul de texto legivel no tema claro");
            Verdade(Contraste(Color.FromArgb(unchecked((int)0xFF6AA6FF)), superficieEscura) >= 4.5,
                    "V3: azul de texto legivel no tema escuro");
        }
    }
}
