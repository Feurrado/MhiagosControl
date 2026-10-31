using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// As pendencias da revisao de 2026-10-15 (bugs medios e baixos e as
    /// melhorias), com o codigo do relatorio em cada conferencia.
    /// </summary>
    public static partial class Tests
    {
        private static void RegressionPendencias(string root)
        {
            PendenciaHistoricoDeslocado();
            PendenciaAmostraComRelogioVoltando(root);
            PendenciaGravacaoAtomica(root);
            PendenciaAliasDoWinget();
            PendenciaOrigemDoHwInfo(root);
            PendenciaAcessibilidade();
        }

        private static void PendenciaHistoricoDeslocado()
        {
            Secao("pendencias B12: fechar muitos baldes de uma vez");
            DateTime t = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            ResetHistory("cpu");
            MetricHistory.AmostrarEm(delegate { return 40f; }, t);
            MetricHistory.AmostrarEm(delegate { return 50f; }, t.AddSeconds(5));
            MetricHistory.AmostrarEm(delegate { return 60f; }, t.AddSeconds(5 + 5 * 10));
            float[] v = History("cpu");
            int n = v.Length;
            Igual(50f, v[n - 10], "o balde lido antes da lacuna fica no lugar certo");
            Verdade(float.IsNaN(v[n - 9]) && float.IsNaN(v[n - 1]), "os baldes da lacuna ficam como falha");
            Igual(40f, v[n - 11], "e o anterior desloca junto");

            ResetHistory("cpu");
            MetricHistory.AmostrarEm(delegate { return 40f; }, t);
            System.Diagnostics.Stopwatch w = System.Diagnostics.Stopwatch.StartNew();
            MetricHistory.AmostrarEm(delegate { return 41f; }, t.AddHours(9));
            Verdade(w.ElapsedMilliseconds < 200, "uma noite desligado fecha num deslocamento so (" + w.ElapsedMilliseconds + " ms)");
            v = History("cpu");
            bool tudoFalha = true;
            for (int i = 0; i < v.Length; i++) if (!float.IsNaN(v[i])) tudoFalha = false;
            Verdade(tudoFalha, "mais de seis horas fora: a janela inteira e falha");
            ResetHistory();
        }

        private static void PendenciaAmostraComRelogioVoltando(string root)
        {
            Secao("pendencias B17: relogio voltando nao cala o grafico de bateria");
            object antes = typeof(HistoricoDeBateria).GetField("_caminho", AuditFlags).GetValue(null);
            try
            {
                HistoricoDeBateria.Caminho = Path.Combine(root, "baterias-relogio.dat");
                HistoricoDeBateria.Zerar();
                DateTime t = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Local);
                for (int m = 0; m <= 60; m += 10)
                    HistoricoDeBateria.Registrar(new Periferico[] { Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 90 - m / 10, false) }, t.AddMinutes(m));
                // o relogio volta uma hora
                for (int m = 0; m <= 30; m += 10)
                    HistoricoDeBateria.Registrar(new Periferico[] { Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 83 - m / 10, false) }, t.AddMinutes(m + 5));
                AnaliseDeBateria a = null;
                foreach (AnaliseDeBateria x in HistoricoDeBateria.Analisar(new Periferico[0])) if (x.Nome == "X11") a = x;
                Verdade(a != null && a.Amostras != null && a.Amostras.Length > 0, "o aparelho segue com amostras");
                DateTime ultima = a.Amostras[a.Amostras.Length - 1].Quando;
                Igual(t.AddMinutes(35), ultima, "depois de o relogio voltar, as amostras continuam no horario novo");
                bool ordenado = true;
                for (int i = 1; i < a.Amostras.Length; i++) if (a.Amostras[i].Quando < a.Amostras[i - 1].Quando) ordenado = false;
                Verdade(ordenado, "e ficam em ordem");
            }
            finally
            {
                HistoricoDeBateria.Zerar();
                typeof(HistoricoDeBateria).GetField("_caminho", AuditFlags).SetValue(null, antes);
            }
        }

        private static void PendenciaGravacaoAtomica(string root)
        {
            Secao("pendencias M9, B15 e B18: gravacao atomica unica");
            string arq = Path.Combine(root, "atomico.txt");
            GravacaoAtomica.Escrever(arq, "um", new UTF8Encoding(false));
            GravacaoAtomica.Escrever(arq, "dois", new UTF8Encoding(false));
            Igual("dois", File.ReadAllText(arq), "a segunda gravacao substitui a primeira");
            Igual(0, Directory.GetFiles(root, "atomico.txt.*.tmp").Length, "nenhum temporario fica para tras");
            using (new FileStream(arq, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool falhou = false;
                try { GravacaoAtomica.Escrever(arq, "tres", new UTF8Encoding(false)); }
                catch (IOException) { falhou = true; }
                Verdade(falhou, "arquivo preso: a gravacao falha em vez de truncar");
            }
            Igual("dois", File.ReadAllText(arq), "e o conteudo anterior fica intacto");
            Igual(0, Directory.GetFiles(root, "atomico.txt.*.tmp").Length, "e o temporario da tentativa e apagado");
        }

        private static byte[] DadosDeAlias(uint etiqueta, params string[] textos)
        {
            List<byte> b = new List<byte>();
            b.AddRange(BitConverter.GetBytes(etiqueta));
            b.AddRange(new byte[4]);
            b.AddRange(BitConverter.GetBytes((uint)textos.Length));
            foreach (string t in textos) { b.AddRange(Encoding.Unicode.GetBytes(t)); b.AddRange(new byte[2]); }
            return b.ToArray();
        }

        private static void PendenciaAliasDoWinget()
        {
            Secao("pendencias B21: winget so pelo alias de app de verdade");
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string bom = Path.Combine(pf, @"WindowsApps\Microsoft.DesktopAppInstaller_1.0.0.0_x64__8wekyb3d8bbwe\winget.exe");
            byte[] d = DadosDeAlias(0x8000001B, "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe!winget", bom, "0");
            Igual(bom, AliasDoWindows.AlvoDosDados(d, d.Length), "alias de app apontando para WindowsApps e aceito");
            d = DadosDeAlias(0x8000001B, "x", "x!y", @"C:\Users\Public\winget.exe", "0");
            Igual(null, AliasDoWindows.AlvoDosDados(d, d.Length), "alias que aponta para pasta gravavel e recusado");
            d = DadosDeAlias(0xA000000C, "x", "x!y", bom);
            Igual(null, AliasDoWindows.AlvoDosDados(d, d.Length), "outro tipo de ponto de reparse e recusado");
            Verdade(!AliasDoWindows.Confiavel(Path.Combine(pf, @"WindowsApps\..\Outro\winget.exe")), "subir de pasta nao engana a conferencia");

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string alias = Path.Combine(local, @"Microsoft\WindowsApps\winget.exe");
            if (File.Exists(alias))
                Verdade(AliasDoWindows.Alvo(alias) != null, "o winget instalado nesta maquina continua aceito");
        }

        private static void PendenciaOrigemDoHwInfo(string root)
        {
            Secao("pendencias M12: biblioteca do HWiNFO so de origem protegida");
            string pasta = Path.Combine(root, "origem-hwinfo");
            Directory.CreateDirectory(pasta);
            string arq = Path.Combine(pasta, "biblioteca.dll");
            File.WriteAllText(arq, "falsa");
            FileSecurity acl = File.GetAccessControl(arq);
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"),
                FileSystemRights.Modify, AccessControlType.Allow));
            File.SetAccessControl(arq, acl);
            Verdade(HwInfo.GravavelPorUsuarioComum(arq), "arquivo que Usuarios podem alterar e reconhecido");
            string erro;
            Verdade(!HwInfo.Adotar(arq, out erro) && erro != null, "e a copia para engine\\ e recusada");
            string sistema = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
            Verdade(!HwInfo.GravavelPorUsuarioComum(sistema), "arquivo protegido do sistema passa na conferencia");
        }

        private static void PendenciaAcessibilidade()
        {
            Secao("pendencias M4 e M6: controles proprios para leitor de tela e teclado");
            using (Toggle t = new Toggle())
            {
                t.Label = "Iniciar com o Windows";
                Igual(AccessibleRole.CheckButton, t.AccessibilityObject.Role, "interruptor e caixa de selecao");
                Igual("Iniciar com o Windows", t.AccessibilityObject.Name, "com o rotulo como nome");
                t.Checked = true;
                Igual(AccessibleStates.Checked, t.AccessibilityObject.State & AccessibleStates.Checked, "e diz se esta ligado");
                InvokePrivate(t, "OnKeyDown", new KeyEventArgs(Keys.Enter));
                Verdade(!t.Checked, "M6: Enter tambem alterna");
            }
            using (FlatBtn b = new FlatBtn())
            {
                b.Text = "Salvar";
                int cliques = 0;
                b.Click += delegate { cliques++; };
                Igual(AccessibleRole.PushButton, b.AccessibilityObject.Role, "botao plano e botao");
                Igual("Salvar", b.AccessibilityObject.Name, "com o texto como nome");
                b.AccessibilityObject.DoDefaultAction();
                Igual(1, cliques, "e a acao padrao clica");
            }
            using (Segmented s = new Segmented())
            {
                s.SetItems("°C", "°F");
                s.SelectedIndex = 1;
                Igual(AccessibleRole.List, s.AccessibilityObject.Role, "seletor e lista");
                Igual("°F", s.AccessibilityObject.Value, "com a opcao escolhida como valor");
            }
            using (NumberBox nb = new NumberBox())
            {
                nb.Value = 15;
                Igual(AccessibleRole.SpinButton, nb.AccessibilityObject.Role, "campo numerico e caixa de rotacao");
                Igual("15", nb.AccessibilityObject.Value, "com o numero como valor");
            }
            using (NavBar nav = new NavBar())
            {
                nav.Add(new NavItem { Text = "Visao geral" }); nav.Add(new NavItem { Text = "Baterias" });
                nav.SelectedIndex = 1;
                Igual(AccessibleRole.PageTabList, nav.AccessibilityObject.Role, "barra lateral e lista de abas");
                Igual("Baterias", nav.AccessibilityObject.Value, "com a pagina aberta como valor");
            }
        }
    }
}
