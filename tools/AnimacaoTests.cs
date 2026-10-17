using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Os paineis que abrem e fecham: curva, tempo, ordem do conteudo, teclado,
    /// acessibilidade e a opcao do Windows de desligar animacoes. Os quadros
    /// sao conduzidos a mao (AnimacaoDeRecolher.Avancar), sem depender do
    /// relogio nem de bombear mensagens.
    /// </summary>
    public static partial class Tests
    {
        private static void RegressionAnimacoes(string root)
        {
            bool? antes = Ui.AnimacoesForcadas;
            try
            {
                AnimacaoTokens();
                AnimacaoCurva();
                AnimacaoMotor();
                AnimacaoPainel();
                AnimacaoCartaoDeBateria();
                AnimacaoPaginaBaterias(root);
                PresencaDosAparelhos();
            }
            finally { Ui.AnimacoesForcadas = antes; }
        }

        private static void AnimacaoTokens()
        {
            Secao("movimento e tokens: duracoes, curvas, tipografia");
            Igual(83, Motion.Rapida, "duracao rapida 83 ms");
            Igual(167, Motion.Media, "duracao media 167 ms");
            Igual(250, Motion.Lenta, "duracao lenta 250 ms");
            Igual(0f, Motion.Entrada(0f), "entrada comeca em zero");
            Igual(1f, Motion.Saida(1f), "saida termina em um");
            Verdade(Motion.Entrada(0.3f) > 0.3f, "a curva de entrada sai na frente (desacelera)");
            Verdade(Motion.Saida(0.3f) < 0.3f, "a curva de saida fica atras (acelera)");
            Verdade(Ui.FontBadge.SizeInPoints >= 9f - 0.01f, "o selo nunca fica abaixo de 9 pt");
            Igual(1f, Ui.FatorDoTexto(100), "Tamanho do texto 100% nao muda nada");
            Igual(1.5f, Ui.FatorDoTexto(150), "Tamanho do texto 150% cresce a rampa em 1,5");
            Igual(2.25f, Ui.FatorDoTexto(400), "Tamanho do texto limita em 225%");
            Igual(1f, Ui.FatorDoTexto(0), "valor invalido volta a 100%");
            Verdade(Ui.FontBase.SizeInPoints >= 10f - 0.01f, "o corpo e de 10 pt");
            Igual(Ui.S(4), Ui.RaioControle, "raio de controle 4");
            Igual(Ui.S(8), Ui.RaioCartao, "raio de cartao 8");
            using (Bitmap bmp = new Bitmap(4, 4))
            using (Graphics gm = Graphics.FromImage(bmp))
            {
                foreach (Font numero in new Font[] { Ui.FontValue, Ui.FontBigPct, Ui.FontTileValue })
                    Igual(Ui.MedirNumero(gm, "1111", numero).Width, Ui.MedirNumero(gm, "0000", numero).Width,
                          "algarismos de largura fixa nos valores ao vivo (" + numero.Name + ")");
            }
            Verdade(Mica.Compativel(22621, false), "Mica liga no Windows 11 22H2 em diante");
            Verdade(!Mica.Compativel(19045, false), "Windows 10 fica sem Mica: fallback solido");
            Verdade(!Mica.Compativel(26100, true), "alto contraste fica sem Mica");
            Igual(1, SettingsForm.IndiceDoAtalho(Keys.Control | Keys.Tab, 0, 8), "Ctrl+Tab avanca");
            Igual(0, SettingsForm.IndiceDoAtalho(Keys.Control | Keys.Tab, 7, 8), "Ctrl+Tab volta ao inicio no fim");
            Igual(7, SettingsForm.IndiceDoAtalho(Keys.Control | Keys.Shift | Keys.Tab, 0, 8), "Ctrl+Shift+Tab recua em volta");
            Igual(2, SettingsForm.IndiceDoAtalho(Keys.Control | Keys.D3, 0, 8), "Ctrl+3 vai a terceira pagina");
            Igual(-1, SettingsForm.IndiceDoAtalho(Keys.Control | Keys.D7, 0, 5), "Ctrl+7 sem sete paginas nao faz nada");
            Igual(-1, SettingsForm.IndiceDoAtalho(Keys.Tab, 0, 8), "Tab sozinho nao e atalho de pagina");
            Ui.AnimacoesForcadas = false;
            using (FlatBtn botao = new FlatBtn())
            using (Toggle chave = new Toggle())
            {
                Transicao t = new Transicao(botao);
                t.Ir(1f);
                Igual(1f, t.Valor, "sem animacoes (reduzir movimento) o estado chega no mesmo instante");
                Verdade(botao.Height >= Ui.S(32), "botao com alvo de 32 px");
                Verdade(chave.Height >= Ui.S(32), "interruptor com alvo de 32 px");
                chave.Checked = true;
                Verdade(chave.Checked, "interruptor liga sem janela");
            }
            Ui.AnimacoesForcadas = true;
            using (FlatBtn botao = new FlatBtn())
            {
                Transicao t = new Transicao(botao);
                t.Ir(1f);
                Igual(1f, t.Valor, "controle sem janela nao anima: vai direto");
            }
        }

        private static void AnimacaoCurva()
        {
            Secao("animacoes: curva cubic-bezier(0.4, 0, 0.2, 1)");
            Igual(0f, AnimacaoDeRecolher.Suavizar(0f), "comeca em zero");
            Igual(1f, AnimacaoDeRecolher.Suavizar(1f), "termina em um");
            bool cresce = true;
            float anterior = 0f;
            for (int i = 1; i <= 100; i++)
            {
                float v = AnimacaoDeRecolher.Suavizar(i / 100f);
                if (v < anterior - 1e-5f) cresce = false;
                anterior = v;
            }
            Verdade(cresce, "a curva nunca volta para tras");
            float meio = AnimacaoDeRecolher.Suavizar(0.5f);
            Verdade(meio > 0.7f && meio < 0.85f, "na metade do tempo ja percorreu cerca de 3/4 (" + meio.ToString("0.000") + ")");
            Verdade(1f - AnimacaoDeRecolher.Suavizar(0.9f) < 0.03f, "assenta devagar no fim");
            Verdade(AnimacaoDeRecolher.Suavizar(0.05f) < 0.05f, "a saida nao da tranco no primeiro quadro");
            Verdade(AnimacaoDeRecolher.DuracaoMs >= 200 && AnimacaoDeRecolher.DuracaoMs <= 300, "duracao entre 200 e 300 ms");
            using (AnimacaoDeRecolher r = new AnimacaoDeRecolher(false))
                Verdade(((Timer)typeof(AnimacaoDeRecolher).GetField("_relogio", AuditFlags).GetValue(r)).Interval < 15,
                        "timer abaixo do passo de 15,6 ms do Windows: 16 virava 31 ms e a animacao travava");
        }

        private static void AnimacaoMotor()
        {
            Secao("animacoes: relogio, virada no meio e efeitos desligados");
            Ui.AnimacoesForcadas = true;
            using (AnimacaoDeRecolher m = new AnimacaoDeRecolher(false))
            {
                int quadros = 0, fins = 0;
                m.Quadro += delegate { quadros++; };
                m.Terminou += delegate { fins++; };
                m.Ir(true, true);
                Verdade(m.Animando && m.Aberto, "abrir comeca a animar e ja responde aberto");
                Igual(0f, m.Valor, "no primeiro instante ainda esta fechado");
                m.Avancar(AnimacaoDeRecolher.DuracaoMs / 2);
                Verdade(m.Valor > 0.5f && m.Valor < 1f, "no meio do tempo, mais da metade do caminho");
                m.Avancar(AnimacaoDeRecolher.DuracaoMs);
                Igual(1f, m.Valor, "no fim, todo aberto");
                Verdade(!m.Animando, "e o relogio para");
                Igual(1, fins, "um aviso de fim");
                Verdade(quadros >= 3, "cada passo avisa quem desenha");

                m.Ir(false, true);
                m.Avancar(AnimacaoDeRecolher.DuracaoMs / 4);
                float onde = m.Valor;
                Verdade(onde < 1f && onde > 0f, "fechando, no caminho");
                m.Ir(true, true);
                Igual(onde, m.Valor, "virar no meio parte de onde estava, sem salto");
                m.Avancar((long)Math.Ceiling(AnimacaoDeRecolher.DuracaoMs * (1f - onde)));
                Igual(1f, m.Valor, "o trecho que falta leva so a parte dele da duracao");
            }

            Ui.AnimacoesForcadas = false;
            using (AnimacaoDeRecolher m = new AnimacaoDeRecolher(false))
            {
                m.Ir(true, true);
                Verdade(!m.Animando && m.Valor == 1f, "efeitos desligados no Windows: vai direto ao fim");
            }

            Ui.AnimacoesForcadas = true;
            AnimacaoDeRecolher d = new AnimacaoDeRecolher(false);
            d.Ir(true, true);
            d.Dispose();
            Verdade(!d.Animando, "descartado, o relogio para");
            d.Ir(false, true);
            Verdade(!d.Animando, "e nao volta a andar");
        }

        private static AnimacaoDeRecolher MotorDe(object dono, string campo)
        {
            return (AnimacaoDeRecolher)dono.GetType().GetField(campo, AuditFlags).GetValue(dono);
        }

        private static void AnimacaoPainel()
        {
            Secao("animacoes: CollapsiblePanel abre e fecha na ordem combinada");
            Ui.AnimacoesForcadas = true;
            using (Form f = new Form())
            {
                f.ShowInTaskbar = false; f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-20000, -20000); f.ClientSize = new Size(400, 400);
                CollapsiblePanel p = new CollapsiblePanel(false);
                Panel conteudo = new Panel();
                p.Conteudo = conteudo;
                p.AlturaAberta = 200;
                p.Width = 300;
                f.Controls.Add(p);
                f.Show();
                int avisos = 0;
                p.AlturaMudou += delegate { avisos++; };

                Igual(0, p.Height, "fechado nao ocupa altura");
                Verdade(!conteudo.Visible, "e o conteudo fica fora");
                Igual(AccessibleStates.Collapsed, p.AccessibilityObject.State & AccessibleStates.Collapsed, "acessibilidade diz recolhido");

                p.Expandido = true;
                AnimacaoDeRecolher m = MotorDe(p, "_animacao");
                Verdade(p.Animando, "abrir com a janela na tela anima");
                Verdade(!conteudo.Visible, "ao abrir, o conteudo espera a casca terminar");
                Igual(AccessibleStates.Expanded, p.AccessibilityObject.State & AccessibleStates.Expanded, "o estado acessivel ja e o de destino");
                m.Avancar(AnimacaoDeRecolher.DuracaoMs / 2);
                Verdade(p.Height > 100 && p.Height < 200, "no meio, a casca esta no meio do caminho (" + p.Height + ")");
                Verdade(!conteudo.Visible, "e o conteudo ainda nao apareceu");
                m.Avancar(AnimacaoDeRecolher.DuracaoMs);
                Igual(200, p.Height, "aberto, a altura toda");
                Verdade(conteudo.Visible, "o conteudo aparece depois da casca");
                Verdade(avisos >= 3, "cada quadro avisa a pagina para mover os vizinhos");

                p.Expandido = false;
                Verdade(!conteudo.Visible, "ao fechar, o conteudo some antes");
                Igual(200, p.Height, "e a casca ainda nao encolheu");
                MotorDe(p, "_animacao").Avancar(AnimacaoDeRecolher.DuracaoMs);
                Igual(0, p.Height, "fechado no fim");

                Ui.AnimacoesForcadas = false;
                p.Expandido = true;
                Verdade(!p.Animando && p.Height == 200 && conteudo.Visible, "sem efeitos visuais: abre de uma vez");
                Ui.AnimacoesForcadas = true;
                p.Expandido = false;
                p.Dispose();
                Verdade(!m.Animando, "descartar o painel para o relogio");
                f.Close();
            }

            CollapsiblePanel fora = new CollapsiblePanel(false);
            fora.AlturaAberta = 120;
            fora.Expandido = true;
            Verdade(!fora.Animando && fora.Height == 120, "sem janela na tela nao anima: ninguem veria");
            fora.Dispose();
        }

        private static void AnimacaoCartaoDeBateria()
        {
            Secao("animacoes: Mais detalhes do cartao de bateria");
            string languageBefore = T.Language;
            string arquivo = Path.Combine(Path.GetTempPath(), "mhiagos-anim-" + Guid.NewGuid().ToString("N") + ".txt");
            DetalhesDaBateria._caminho = arquivo;
            DetalhesDaBateria.Recarregar();
            Ui.AnimacoesForcadas = true;
            try
            {
                T.Language = T.PtBr;
                using (Form f = new Form())
                {
                    f.ShowInTaskbar = false; f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-20000, -20000);
                    f.ClientSize = new Size(940, 900);
                    CartaoDeBateria c = new CartaoDeBateria();
                    f.Controls.Add(c);
                    f.Show();
                    c.Mostrar(AnaliseDeExemplo(true), null);
                    int fechado = c.AlturaDesejada(900);
                    int aberto = CartaoDeBateria.AlturaPara(900, true);

                    InvokePrivate(c, "OnKeyDown", new KeyEventArgs(Keys.Enter));
                    Verdade(c.DetalhesAbertos, "Enter no cartao abre Mais detalhes");
                    AnimacaoDeRecolher m = MotorDe(c, "_detalhes");
                    Verdade(m.Animando, "e anima");
                    Verdade(!(bool)c.GetType().GetProperty("MostraDetalhes", AuditFlags).GetValue(c, null),
                            "os detalhes so aparecem no fim");
                    m.Avancar(AnimacaoDeRecolher.DuracaoMs / 2);
                    int meio = c.AlturaDesejada(900);
                    Verdade(meio > fechado && meio < aberto, "no meio, a altura esta entre fechado e aberto");
                    Verdade(c.ProgressoDosDetalhes > 0f && c.ProgressoDosDetalhes < 1f, "a seta gira junto");
                    m.Avancar(AnimacaoDeRecolher.DuracaoMs);
                    Igual(aberto, c.AlturaDesejada(900), "no fim, a altura aberta");
                    Verdade((bool)c.GetType().GetProperty("MostraDetalhes", AuditFlags).GetValue(c, null), "e os detalhes aparecem");
                    Igual(AccessibleStates.Expanded, c.AccessibilityObject.State & AccessibleStates.Expanded, "acessibilidade diz expandido");
                    Verdade(c.AccessibilityObject.Name.Contains(T.LessDetails), "o nome acessivel diz o que a acao faz");

                    InvokePrivate(c, "OnKeyDown", new KeyEventArgs(Keys.Space));
                    Verdade(!c.DetalhesAbertos, "Espaco fecha");
                    Verdade(!(bool)c.GetType().GetProperty("MostraDetalhes", AuditFlags).GetValue(c, null),
                            "fechando, os detalhes somem antes de o cartao encolher");
                    MotorDe(c, "_detalhes").Avancar(AnimacaoDeRecolher.DuracaoMs);
                    Igual(fechado, c.AlturaDesejada(900), "fechado no fim");
                    Verdade(c.TabStop, "o cartao entra na ordem do Tab");
                    f.Close();
                }
            }
            finally
            {
                T.Language = languageBefore;
                DetalhesDaBateria._caminho = null;
                DetalhesDaBateria.Recarregar();
                try { File.Delete(arquivo); } catch { }
            }
        }

        private static void AnimacaoPaginaBaterias(string root)
        {
            Secao("animacoes: painel Widget da pagina Baterias");
            string arquivo = Path.Combine(Path.GetTempPath(), "mhiagos-anim-pg-" + Guid.NewGuid().ToString("N") + ".txt");
            DetalhesDaBateria._caminho = arquivo;
            DetalhesDaBateria.Recarregar();
            string languageBefore = T.Language;
            Ui.AnimacoesForcadas = true;
            try
            {
                T.Language = T.PtBr;
                Config config = AuditConfig(root, "anim-baterias");
                using (SettingsForm form = new SettingsForm(config, new List<SensorEntry>(), new DadosComWidget()))
                {
                    form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
                    form.Size = new Size(1100, 760);
                    form.Show(); ((Timer)Field(form, "_tick")).Stop();
                    ((NavBar)Field(form, "_nav")).SelectedIndex = 6;
                    BotaoDoWidget botao = (BotaoDoWidget)Field(form, "_btWidget");
                    CollapsiblePanel painel = (CollapsiblePanel)Field(form, "_painelDoWidget");
                    Label vazio = (Label)Field(form, "_lbBateriasVazio");
                    int vazioFechado = vazio.Top;

                    InvokePrivate(botao, "OnKeyDown", new KeyEventArgs(Keys.Enter));
                    Verdade(botao.Aberto && painel.Animando, "Enter no botao abre o painel animando");
                    Igual(AccessibleStates.Expanded, botao.AccessibilityObject.State & AccessibleStates.Expanded, "o botao anuncia expandido");
                    AnimacaoDeRecolher m = MotorDe(painel, "_animacao");
                    m.Avancar(AnimacaoDeRecolher.DuracaoMs / 2);
                    Verdade(botao.Giro > 0f && botao.Giro < 1f, "a seta do botao gira no mesmo quadro");
                    Verdade(vazio.Top > vazioFechado && vazio.Top < vazioFechado + painel.AlturaAberta, "os vizinhos descem junto, sem salto");
                    m.Avancar(AnimacaoDeRecolher.DuracaoMs);
                    Igual(1f, botao.Giro, "aberto, a seta aponta para cima");
                    Verdade(vazio.Top > painel.Bottom, "aberto, os aparelhos ficam abaixo do painel");

                    InvokePrivate(botao, "OnKeyDown", new KeyEventArgs(Keys.Space));
                    Verdade(!botao.Aberto, "Espaco fecha");
                    MotorDe(painel, "_animacao").Avancar(AnimacaoDeRecolher.DuracaoMs);
                    Igual(vazioFechado, vazio.Top, "fechado, os aparelhos voltam para o lugar");
                    Igual(0f, botao.Giro, "e a seta volta para baixo");
                    form.Close();
                }
            }
            finally
            {
                T.Language = languageBefore;
                DetalhesDaBateria._caminho = null;
                DetalhesDaBateria.Recarregar();
                try { File.Delete(arquivo); } catch { }
            }
        }
    }
}
