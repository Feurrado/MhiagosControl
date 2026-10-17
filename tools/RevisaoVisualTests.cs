using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>A revisao visual de 2026-10-17: alinhamento, abertura da janela, menu e curva.</summary>
    public static partial class Tests
    {
        private static void RegressionRevisaoVisual(string root)
        {
            VisualMenuMantemOPontoComTagOcupado();
            VisualAberturaJaChegaMontada(root);
            VisualVisaoGeralAlinhada(root);
            VisualPaineisEstreitosEmpilham(root);
            VisualLinhaDoTempoContinua();
        }

        private static void VisualMenuMantemOPontoComTagOcupado()
        {
            Secao("visual 1: marca do menu de perfis");
            ToolStripMenuItem i = MenuTema.Opcao("A", true, null);
            i.Tag = new object();   // o menu de perfis guarda o perfil no Tag
            Igual(MenuTema.Radio, i.Name, "a escolha unica nao depende do Tag, que o menu de perfis ocupa");
        }

        private static void VisualAberturaJaChegaMontada(string root)
        {
            Secao("visual 3: a janela abre com a pagina montada, antes do Shown");
            Config config = AuditConfig(root, "abertura");
            config.LastPage = "baterias";
            DadosDeCaptura dados = new DadosDeCaptura();
            using (SettingsForm form = new SettingsForm(config, dados.Sensors, dados))
            {
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.Show();
                // Show() devolve antes do Shown, que e postado: aqui a pessoa ja veria o quadro.
                NavBar nav = (NavBar)Field(form, "_nav");
                Igual("baterias", nav.Selected.Key, "a pagina lembrada ja esta aberta no primeiro quadro");
                Control botao = (Control)Field(form, "_btWidget");
                Verdade(botao != null && botao.Width > 0 && botao.Left > 0, "o botao do Widget ja esta no lugar, e nao no canto");
                form.Close();
            }
        }

        private static void VisualVisaoGeralAlinhada(string root)
        {
            Secao("visual 2: Visao geral, colunas do meio nas divisas da fileira de cima");
            Config config = AuditConfig(root, "alinhada");
            config.MetricIds.Clear(); config.MetricSizes.Clear();
            config.MetricIds.AddRange(new string[] { "cpu", "load", "gpu" });
            config.MetricSizes.AddRange(new int[] { 1, 1, 1 });
            DadosDeCaptura dados = new DadosDeCaptura();
            using (SettingsForm form = new SettingsForm(config, dados.Sensors, dados))
            {
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.Show();
                form.Size = new Size(Ui.S(1010), Ui.S(820));
                NavBar nav = (NavBar)Field(form, "_nav");
                nav.SelectedIndex = 0;
                InvokePrivate(form, "ArranjarVisaoGeral");
                List<MetricCard> blocos = (List<MetricCard>)Field(form, "_vgTiles");
                Control cooler = (Control)Field(form, "_cardCooler");
                Control p1 = (Control)Field(form, "_vgP1");
                if (blocos.Count < 2) { Verdade(true, "menos de dois blocos: nada a alinhar"); form.Close(); return; }
                bool divisa = false;
                for (int i = 0; i < blocos.Count - 1; i++)
                    if (blocos[i].Right == cooler.Right && blocos[i + 1].Left == p1.Left) divisa = true;
                Verdade(divisa, "o cartao do cooler termina e o dos mostradores comeca numa divisa dos blocos de cima");
                Igual(blocos[blocos.Count - 1].Right, p1.Right, "e a borda direita dos mostradores e a do ultimo bloco");
                form.Close();
            }
        }

        private static void VisualPaineisEstreitosEmpilham(string root)
        {
            Secao("visual 6: Paineis no tamanho minimo nao corta o segundo cartao");
            Config config = AuditConfig(root, "paineis-estreitos");
            DadosDeCaptura dados = new DadosDeCaptura();
            using (SettingsForm form = new SettingsForm(config, dados.Sensors, dados))
            {
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.Show();
                form.Size = new Size(Ui.S(880), Ui.S(660));
                NavBar nav = (NavBar)Field(form, "_nav");
                for (int i = 0; i < nav.Items.Count; i++)
                    if (nav.Items[i].Key == "paineis") nav.SelectedIndex = i;
                Control pagina = (Control)Field(form, "_pgPaineis");
                InvokePrivate(form, "ArranjarPaineis");
                foreach (Control c in pagina.Controls)
                {
                    if (!(c is Card)) continue;
                    Verdade(c.Right <= pagina.ClientSize.Width, "cartao \"" + ((Card)c).Title + "\" cabe na largura da pagina (" + c.Right + " de " + pagina.ClientSize.Width + ")");
                }
                form.Close();
            }
        }

        private static void VisualLinhaDoTempoContinua()
        {
            Secao("visual 4: a faixa de seis horas nao deixa tracos soltos");
            ResetHistory("cpu");
            DateTime t = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            // sinal que salta entre dois patamares: sem a ligacao entre colunas cada
            // traco vertical ficaria isolado do vizinho
            for (int k = 0; k < 4320; k++)
            {
                float v = (k / 3) % 2 == 0 ? 30f : 80f;
                MetricHistory.AmostrarEm(delegate { return v; }, t.AddSeconds(k * MetricHistory.PassoSeg));
            }
            using (NavegadorDeJanela nav = new NavegadorDeJanela())
            using (Bitmap bmp = new Bitmap(520, 80))
            {
                nav.SensorId = "cpu";
                nav.Size = bmp.Size;
                nav.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
                // coluna de pixels por onde a curva passa, de cima a baixo: nenhum buraco entre os extremos
                int colunasVazias = 0;
                Rectangle f = (Rectangle)typeof(NavegadorDeJanela).GetProperty("Faixa", AuditFlags).GetValue(nav, null);
                for (int x = f.X + 4; x < f.Right - 4; x++)
                {
                    int pintados = 0;
                    for (int y = f.Y + 2; y < f.Bottom - 2; y++)
                        if (bmp.GetPixel(x, y).B > bmp.GetPixel(x, y).R + 25) pintados++;
                    if (pintados == 0) colunasVazias++;
                }
                Verdade(colunasVazias == 0, "toda coluna da faixa tem traco (" + colunasVazias + " vazias)");
            }
            ResetHistory();
        }
    }
}
