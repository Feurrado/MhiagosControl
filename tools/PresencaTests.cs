using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Aparelhos que entram e saem (fade + deslize/expansao de 250 ms): o motor
    /// de presenca, o desenho do widget com presenca parcial e o cartao da
    /// pagina Baterias. Os quadros sao conduzidos a mao, sem relogio.
    /// </summary>
    public static partial class Tests
    {
        private static void PresencaDosAparelhos()
        {
            Ui.AnimacoesForcadas = true;
            PresencaMotor();
            PresencaWidget();
            PresencaCartao();
        }

        private static List<string> Lista(params string[] c) { return new List<string>(c); }

        private static void PresencaMotor()
        {
            Secao("presenca: entrada e saida de aparelhos");
            using (PresencaAnimada p = new PresencaAnimada())
            {
                int removidos = 0;
                p.Removido += delegate { removidos++; };
                p.Definir(Lista("a", "b"), false);
                Igual(2, p.Quantidade, "sem animar a lista e a leitura");
                Igual(1f, p.Valor(0), "e ja chega inteira");
                Verdade(!p.Animando, "sem relogio");

                p.Definir(Lista("a", "c", "b"), true);
                Igual(3, p.Quantidade, "o novo entra na posicao da leitura");
                Igual("c", p.Chave(1), "entre a e b");
                Igual(0f, p.Valor(1), "comeca ausente");
                Verdade(p.Animando, "e o relogio anda");
                p.Avancar(AnimacaoDeRecolher.DuracaoMs / 2 + 5);
                Verdade(p.Valor(1) > 0.4f && p.Valor(1) < 1f, "no meio do tempo, mais da metade da entrada (" + p.Valor(1).ToString("0.00") + ")");
                Igual(1f, p.Valor(0), "quem ja estava nao mexe");
                p.Avancar(AnimacaoDeRecolher.DuracaoMs * 3);
                Igual(1f, p.Valor(1), "no fim, inteiro");
                Verdade(!p.Animando, "e o relogio para");

                p.Definir(Lista("a", "b"), true);
                Igual(3, p.Quantidade, "quem sumiu continua na lista, saindo");
                Verdade(p.Saindo(1) && p.Chave(1) == "c", "no mesmo lugar, marcado como saindo");
                p.Avancar(AnimacaoDeRecolher.DuracaoMs / 2 + 5);
                Verdade(p.Valor(1) > 0f && p.Valor(1) < 0.6f, "a meio caminho de sair (" + p.Valor(1).ToString("0.00") + ")");
                Verdade(p.Soma > 2f && p.Soma < 3f, "a soma das presencas fica entre 2 e 3");
                p.Avancar(AnimacaoDeRecolher.DuracaoMs * 3);
                Igual(2, p.Quantidade, "no fim sai de vez");
                Igual(1, removidos, "e avisa quem desenha");

                // volta antes de terminar de sair: reaproveita o item, sem salto
                p.Definir(Lista("a", "b", "c"), true);
                p.Avancar(AnimacaoDeRecolher.DuracaoMs * 3);
                p.Definir(Lista("a", "b"), true);
                p.Avancar(AnimacaoDeRecolher.DuracaoMs / 4);
                float onde = p.Valor(2);
                Verdade(onde < 1f && onde > 0f, "saindo, no caminho");
                p.Definir(Lista("a", "b", "c"), true);
                Igual(3, p.Quantidade, "se o aparelho volta, continua o mesmo item");
                Verdade(!p.Saindo(2), "e deixa de sair");
                Igual(onde, p.Valor(2), "parte de onde estava");
                p.Avancar(AnimacaoDeRecolher.DuracaoMs * 3);
                Igual(1f, p.Valor(2), "e termina inteiro");

                Ui.AnimacoesForcadas = false;
                p.Definir(Lista("x"), true);
                Igual(1, p.Quantidade, "com efeitos desligados no Windows a lista vira a leitura");
                Igual(1f, p.Valor(0), "sem animar");
                Ui.AnimacoesForcadas = true;
            }
            using (PresencaAnimada p2 = new PresencaAnimada())
                Verdade(((Timer)typeof(PresencaAnimada).GetField("_relogio", AuditFlags).GetValue(p2)).Interval < 15,
                        "o relogio da presenca tambem fica abaixo do passo de 15,6 ms");
        }

        private static long SomaDeAlfa(Bitmap b)
        {
            long s = 0;
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++) s += b.GetPixel(x, y).A;
            return s;
        }

        private static long AlfaDoWidget(LayoutDoWidget layout, Periferico p, float presenca)
        {
            PintorDoWidget.Quadro q = new PintorDoWidget.Quadro();
            q.Itens = new Periferico[] { p };
            q.Notas = new string[] { null };
            q.Layout = layout;
            q.Presenca = new float[] { presenca };
            Size tam = PintorDoWidget.Medir(layout, 1f, 1f, false);
            using (Bitmap b = new Bitmap(tam.Width, tam.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics gr = Graphics.FromImage(b))
                {
                    gr.Clear(Color.Transparent);
                    PintorDoWidget.Pintar(gr, tam, q);
                }
                return SomaDeAlfa(b);
            }
        }

        private static void PresencaWidget()
        {
            Secao("presenca: widget de bateria");
            foreach (LayoutDoWidget layout in new LayoutDoWidget[] { LayoutDoWidget.Aneis, LayoutDoWidget.Lista })
            {
                bool lista = layout == LayoutDoWidget.Lista;
                Size vazio = PintorDoWidget.Medir(layout, 0f, 1f, false);
                Size um = PintorDoWidget.Medir(layout, 1f, 1f, false);
                Size dois = PintorDoWidget.Medir(layout, 2f, 1f, false);
                Size meio = PintorDoWidget.Medir(layout, 0.5f, 1f, false);
                Size umEMeio = PintorDoWidget.Medir(layout, 1.5f, 1f, false);
                Igual(PintorDoWidget.Medir(layout, 2, 1f, false), dois, layout + ": a medida fracionaria coincide com a inteira");
                Verdade(meio.Width >= Math.Min(vazio.Width, um.Width) && meio.Width <= Math.Max(vazio.Width, um.Width) &&
                        meio.Height >= Math.Min(vazio.Height, um.Height) && meio.Height <= Math.Max(vazio.Height, um.Height),
                        layout + ": entre vazio e um aparelho o tamanho fica no meio");
                Verdade(lista ? umEMeio.Height > um.Height && umEMeio.Height < dois.Height
                              : umEMeio.Width > um.Width && umEMeio.Width < dois.Width,
                        layout + ": com um e meio, entre um e dois");

                RectangleF[] cheio = PintorDoWidget.Zonas(layout, new float[] { 1f, 1f }, 1f, false);
                RectangleF[] primeiroEntrando = PintorDoWidget.Zonas(layout, new float[] { 0.5f, 1f }, 1f, false);
                RectangleF[] ultimoEntrando = PintorDoWidget.Zonas(layout, new float[] { 1f, 0.5f }, 1f, false);
                Verdade(lista ? ultimoEntrando[1].Y == cheio[1].Y : ultimoEntrando[1].X == cheio[1].X,
                        layout + ": quem entra no fim da fila nao empurra o da frente");
                Verdade(lista ? primeiroEntrando[1].Y < cheio[1].Y : primeiroEntrando[1].X < cheio[1].X,
                        layout + ": os de depois deslizam enquanto o da frente entra");
            }

            Periferico g = Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, 72, false);
            foreach (LayoutDoWidget layout in new LayoutDoWidget[] { LayoutDoWidget.Aneis, LayoutDoWidget.Lista })
            {
                long inteiro = AlfaDoWidget(layout, g, 1f), parcial = AlfaDoWidget(layout, g, 0.5f), nenhum = AlfaDoWidget(layout, g, 0f);
                Verdade(parcial < inteiro && parcial > nenhum,
                        layout + ": com meia presenca o item esta mais transparente que inteiro e mais opaco que ausente");
            }

            Periferico longo = Periferico.Lido("WH-1000XM4 Wireless Headphones", TipoPeriferico.Outro, FontePeriferico.Bluetooth, 18, false);
            Periferico curto = Periferico.Lido("BT", TipoPeriferico.Outro, FontePeriferico.Bluetooth, 18, false);
            foreach (LayoutDoWidget layout in new LayoutDoWidget[] { LayoutDoWidget.Aneis, LayoutDoWidget.Lista })
            {
                float nome = PintorDoWidget.LarguraDoNome(longo, layout == LayoutDoWidget.Lista);
                Verdade(nome > 100f, layout + ": o nome longo e medido (" + nome.ToString("0") + ")");
                if (layout == LayoutDoWidget.Aneis) Igual(0f, PintorDoWidget.LarguraDoNome(g, false), layout + ": aparelho que se reconhece pelo desenho nao usa nome");
                else Verdade(PintorDoWidget.LarguraDoNome(g, true) > 0f, layout + ": a lista mostra o nome de todo aparelho");
                float[] larg = PintorDoWidget.Larguras(layout, new Periferico[] { longo, curto, g });
                float[] um = new float[] { 1f, 1f, 1f };
                Size cheio = PintorDoWidget.Medir(layout, um, larg, 1f, 1f);
                RectangleF[] z = PintorDoWidget.Zonas(layout, um, larg, cheio, 1f);
                if (layout == LayoutDoWidget.Aneis)
                {
                    Verdade(z[0].Width >= nome, layout + ": a coluna do nome longo comporta o nome inteiro");
                    Verdade(z[0].Right <= z[1].Left && z[1].Right <= z[2].Left && z[2].Right <= cheio.Width, layout + ": colunas sem sobrepor e dentro do widget");
                }
                else Verdade(cheio.Width >= 16 * 2 + 40 + 12 + nome, layout + ": o widget da lista alarga para o nome caber");
                Size entrando = PintorDoWidget.Medir(layout, new float[] { 1f, 0.5f, 1f }, larg, 1f, 1f);
                Verdade(entrando.Width <= cheio.Width && entrando.Height <= cheio.Height, layout + ": entrando, o widget ainda nao tem o tamanho cheio");
                Size semNome = PintorDoWidget.Medir(layout, new float[] { 1f }, new float[] { 78f }, 1f, 0f);
                Size meioNome = PintorDoWidget.Medir(layout, new float[] { 1f }, new float[] { 78f }, 1f, 0.5f);
                Size comNome = PintorDoWidget.Medir(layout, new float[] { 1f }, new float[] { 78f }, 1f, 1f);
                if (layout == LayoutDoWidget.Aneis)
                    Verdade(semNome.Height < meioNome.Height && meioNome.Height < comNome.Height, layout + ": a linha do nome entra aos poucos, sem pular de altura");
            }
            Igual(0.5f, PintorDoWidget.FracaoDeNome(new Periferico[] { g, longo }, new float[] { 1f, 0.5f }), "a fracao do nome acompanha a presenca de quem a usa");
            Igual(0f, PintorDoWidget.FracaoDeNome(new Periferico[] { g }, new float[] { 1f }), "sem aparelho com nome, nenhuma linha extra");

            // posicao guardada: o canto que fica parado, e nao o de cima a esquerda
            Rectangle areaT = new Rectangle(0, 0, 1920, 1040);
            Rectangle noCanto = new Rectangle(1920 - 48 - 300, 1040 - 48 - 120, 300, 120);
            Point canto = WidgetDeBateria.CantoAncora(noCanto, areaT);
            Igual(new Point(1920 - 48, 1040 - 48), canto, "no canto inferior direito se guarda o canto inferior direito");
            foreach (Size outro in new Size[] { new Size(150, 120), new Size(480, 200) })
            {
                Point topo = WidgetDeBateria.DoCanto(canto, outro, areaT);
                Igual(canto, new Point(topo.X + outro.Width, topo.Y + outro.Height), "com " + outro + " o canto continua no mesmo lugar");
            }
            Igual(new Point(20, 30), WidgetDeBateria.DoCanto(new Point(20, 30), new Size(200, 100), areaT), "na metade de cima e a esquerda o canto e o de cima a esquerda");
            ConfigDoWidget cp = new ConfigDoWidget(); cp.X = 1872; cp.Y = 992;
            string txt = cp.ParaTexto();
            Verdade(txt.Contains("ancora_x=1872") && txt.Contains("ancora_y=992"), "o arquivo guarda a ancora");

            Rectangle tela = new Rectangle(0, 0, 1920, 1080);
            Point perto = WidgetDeBateria.AncorarCrescimento(new Rectangle(1600, 20, 200, 100), new Size(300, 100), tela);
            Igual(1500, perto.X, "na metade direita da tela a borda direita fica parada");
            Igual(20, perto.Y, "e em cima o topo");
            Point baixo = WidgetDeBateria.AncorarCrescimento(new Rectangle(100, 900, 200, 100), new Size(200, 160), tela);
            Igual(100, baixo.X, "na metade esquerda a borda esquerda fica parada");
            Igual(840, baixo.Y, "embaixo a borda de baixo fica parada");

            string[] chaves = WidgetDeBateria.Chaves(new Periferico[] { g, g });
            Verdade(chaves[0] != chaves[1], "duas chaves iguais ganham sufixo para nao se fundirem na lista");
        }

        private static void PresencaCartao()
        {
            Secao("presenca: cartao da pagina Baterias");
            Ui.AnimacoesForcadas = true;
            string arquivo = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mhiagos-pres-" + Guid.NewGuid().ToString("N") + ".txt");
            DetalhesDaBateria._caminho = arquivo;
            DetalhesDaBateria.Recarregar();
            using (Form f = new Form())
            {
                f.ShowInTaskbar = false; f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-20000, -20000);
                f.ClientSize = new Size(940, 900);
                CartaoDeBateria c = new CartaoDeBateria();
                f.Controls.Add(c);
                f.Show();
                c.Mostrar(AnaliseDeExemplo(false), null);
                int cheia = c.AlturaDesejada(900);
                Igual(1f, c.Presenca, "um cartao novo ja esta inteiro");

                int saiu = 0;
                c.Saiu += delegate { saiu++; };
                c.Sair();
                Verdade(c.Saindo, "sair marca o cartao como saindo");
                AnimacaoDeRecolher m = MotorDe(c, "_presenca");
                Verdade(m.Animando, "e anima");
                m.Avancar(AnimacaoDeRecolher.DuracaoMs / 2 + 5);
                int meio = c.AlturaDesejada(900);
                Verdade(meio > 0 && meio < cheia, "no meio a altura esta entre zero e a cheia (" + meio + " de " + cheia + ")");
                Igual(0, saiu, "ainda nao saiu");
                m.Avancar(AnimacaoDeRecolher.DuracaoMs * 3);
                Igual(0, c.AlturaDesejada(900), "no fim a altura e zero");
                Igual(1, saiu, "e o cartao pede para ser removido");

                c.Entrar();
                Verdade(!c.Saindo, "voltar tira a marca de saindo");
                MotorDe(c, "_presenca").Avancar(AnimacaoDeRecolher.DuracaoMs * 3);
                Igual(cheia, c.AlturaDesejada(900), "e volta a altura cheia");

                c.NascerEntrando();
                Igual(0, c.AlturaDesejada(900), "um cartao que chega nasce com altura zero");
                MotorDe(c, "_presenca").Avancar(AnimacaoDeRecolher.DuracaoMs * 3);
                Igual(cheia, c.AlturaDesejada(900), "e cresce ate a cheia");
                f.Close();
            }
            try { System.IO.File.Delete(arquivo); } catch { }
        }
    }
}
