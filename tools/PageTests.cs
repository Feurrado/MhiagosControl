using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    /// <summary>
    /// Versao 2.22: widget recolhido em Baterias, nomes completos dos aparelhos,
    /// marca da placa de video, slots de memoria no lugar certo e o navegador de
    /// Metricas seguindo o cartao clicado.
    /// </summary>
    public static partial class Tests
    {
        private static void RegressionWidgetRecolhido(string root)
        {
            Secao("pagina Baterias: aparelhos primeiro, widget atras do botao");
            string arquivo = Path.Combine(Path.GetTempPath(), "mhiagos-painel-" + Guid.NewGuid().ToString("N") + ".txt");
            DetalhesDaBateria._caminho = arquivo;
            DetalhesDaBateria.Recarregar();
            string languageBefore = T.Language;
            try
            {
                T.Language = T.PtBr;
                Config config = AuditConfig(root, "widget-recolhido");
                DadosComWidget dados = new DadosComWidget();
                using (SettingsForm form = new SettingsForm(config, new List<SensorEntry>(), dados))
                {
                    form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
                    form.Size = new Size(1100, 760);
                    form.Show(); ((Timer)Field(form, "_tick")).Stop();
                    ((NavBar)Field(form, "_nav")).SelectedIndex = 5;
                    Control pagina = (Control)Field(form, "_pgBaterias");
                    Verdade(pagina.Visible, "a setima entrada da barra e a pagina Baterias");

                    BotaoDoWidget botao = (BotaoDoWidget)Field(form, "_btWidget");
                    Card cartao = (Card)Field(form, "_cardWidget");
                    Label vazio = (Label)Field(form, "_lbBateriasVazio");
                    Verdade(botao != null, "a pagina ganha o botao Widget");
                    Igual("Widget", botao.Text, "o botao se chama Widget");
                    Verdade(!botao.Aberto && !cartao.Visible, "o painel do widget comeca fechado");
                    Verdade(botao.Top <= 4 && botao.Height <= 32, "o botao e uma linha fina no alto da pagina");
                    Verdade(botao.Right >= pagina.Width - Pagina.LarguraDaBarra - 40, "e fica a direita, fora do caminho");
                    Verdade(vazio.Top < botao.Bottom + 16, "os aparelhos comecam logo abaixo do botao, sem os 330 px do cartao");
                    CaptureEvolution(form, "baterias-widget-fechado");

                    InvokePrivate(botao, "OnClick", EventArgs.Empty);
                    Verdade(botao.Aberto && cartao.Visible, "clicar abre o painel");
                    Control painel = cartao.Parent;   // o cartao mora no CollapsiblePanel da pagina
                    Verdade(painel is CollapsiblePanel && painel.Top >= botao.Bottom, "o painel abre logo abaixo do botao");
                    Verdade(vazio.Top > painel.Bottom, "e empurra os aparelhos para baixo so enquanto aberto");
                    Igual(0, dados.Mudancas, "abrir o painel nao mexe na configuracao do widget");
                    DetalhesDaBateria.Recarregar();
                    Verdade(DetalhesDaBateria.Aberto(SettingsForm.ChaveDoPainelDoWidget), "aberto fica gravado");
                    Verdade(!DetalhesDaBateria.Aberto("GHub|G325"), "sem abrir os detalhes de aparelho nenhum");
                    CaptureEvolution(form, "baterias-widget-aberto");
                    form.Close();
                }

                using (SettingsForm form = new SettingsForm(config, new List<SensorEntry>(), dados))
                {
                    form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
                    form.Show(); ((Timer)Field(form, "_tick")).Stop();
                    ((NavBar)Field(form, "_nav")).SelectedIndex = 5;
                    BotaoDoWidget botao = (BotaoDoWidget)Field(form, "_btWidget");
                    Verdade(botao.Aberto && ((Card)Field(form, "_cardWidget")).Visible, "reaberto, o painel volta como foi deixado");
                    InvokePrivate(botao, "OnKeyDown", new KeyEventArgs(Keys.Space));
                    Verdade(!botao.Aberto && !((Card)Field(form, "_cardWidget")).Visible, "Espaco no botao fecha de novo");
                    form.Close();
                }

                using (SettingsForm form = new SettingsForm(config, new List<SensorEntry>(), new FreshData()))
                {
                    form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
                    form.Show(); ((Timer)Field(form, "_tick")).Stop();
                    Verdade(Field(form, "_btWidget") == null, "sem o controle do widget nao ha botao para um painel vazio");
                    form.Close();
                }
                Verdade(typeof(TrayContext).GetField("_miWidget", AuditFlags) != null, "ligar e desligar continua no menu da bandeja");
            }
            finally
            {
                T.Language = languageBefore;
                DetalhesDaBateria._caminho = null;
                DetalhesDaBateria.Recarregar();
                try { File.Delete(arquivo); } catch { }
            }
        }

        private static void RegressionNomesCompletos()
        {
            Secao("pagina Baterias: nomes completos dos aparelhos");
            Igual("Logitech G325 Lightspeed", Perifericos.NomeCompleto("G325", FontePeriferico.GHub), "G325 do G HUB ganha o nome completo");
            Igual("Attack Shark X11", Perifericos.NomeCompleto("X11", FontePeriferico.AttackShark), "X11 ganha a marca");
            Igual("G325", Perifericos.NomeCompleto("G325", FontePeriferico.Bluetooth), "outra fonte com o mesmo nome fica como esta");
            Igual("G733", Perifericos.NomeCompleto("G733", FontePeriferico.GHub), "outro aparelho do G HUB fica como esta");
            Igual("Fone Bluetooth", Perifericos.NomeCompleto("Fone Bluetooth", FontePeriferico.Bluetooth), "Bluetooth fica como esta");

            Periferico g325 = Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, 70, false);
            Igual("GHub|G325", HistoricoDeBateria.Chave(g325), "a chave do historico segue com o nome lido");
            Igual("G325", g325.Nome, "o nome lido nao muda");

            string languageBefore = T.Language;
            try
            {
                T.Language = T.PtBr;
                using (Form f = new Form())
                {
                    f.ShowInTaskbar = false; f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-20000, -20000);
                    f.ClientSize = new Size(760, 640);
                    CartaoDeBateria largo = new CartaoDeBateria(), estreito = new CartaoDeBateria();
                    f.Controls.Add(largo); f.Controls.Add(estreito);
                    f.Show();
                    largo.Mostrar(AnaliseDeExemplo(true), null);
                    estreito.Mostrar(AnaliseDeExemplo(false), 38);
                    largo.SetBounds(12, 12, 700, largo.AlturaDesejada(700));
                    estreito.SetBounds(12, largo.Bottom + 12, 300, estreito.AlturaDesejada(300));
                    f.Refresh();
                    CaptureEvolution(f, "baterias-nomes-completos");
                    f.Close();
                }
            }
            finally { T.Language = languageBefore; }
        }

        private static void RegressionMarcaDaPlaca()
        {
            Secao("Especificacoes: marca da placa de video");
            string id;
            Igual("Sapphire", SpecSheet.MarcaDaPlaca(@"PCI\VEN_1002&DEV_67DF&SUBSYS_E3531DA2&REV_E7\6&970B1E8&0&0020020B", out id),
                "SUBSYS terminado em 1DA2 e Sapphire");
            Igual("1DA2", id, "com o ID cru junto");
            Igual("ASUS", SpecSheet.MarcaDaPlaca(@"PCI\VEN_10DE&DEV_2504&SUBSYS_87F21043&REV_A1", out id), "1043 e ASUS");
            Igual("Galax/KFA2", SpecSheet.MarcaDaPlaca(@"PCI\VEN_10DE&DEV_2504&SUBSYS_13951B4C&REV_A1", out id), "1B4C e Galax/KFA2");
            Igual(null, SpecSheet.MarcaDaPlaca(@"PCI\VEN_1002&DEV_73FF&SUBSYS_0E361002&REV_C7", out id), "placa de referencia nao ganha marca");
            Verdade(id == "1002" && SpecSheet.PlacaDeReferencia(id), "e e reconhecida como referencia");
            Igual(null, SpecSheet.MarcaDaPlaca(@"PCI\VEN_10DE&DEV_2504&SUBSYS_0A1B1028&REV_A1", out id), "fabricante fora da tabela nao ganha nome");
            Verdade(id == "1028" && !SpecSheet.PlacaDeReferencia(id), "mas o ID cru fica disponivel para a lista");
            Igual(null, SpecSheet.MarcaDaPlaca(@"PCI\VEN_1002&DEV_67DF&REV_E7", out id), "sem SUBSYS nao ha marca");
            Igual(null, id, "nem ID");

            Igual("Sapphire Radeon RX 580 Series", SpecSheet.ComMarca("Sapphire", "Radeon RX 580 Series"), "a lista mostra a marca antes do modelo");
            Igual("ASUS GeForce RTX 3060", SpecSheet.ComMarca("ASUS", "NVIDIA GeForce RTX 3060"), "sem a marca do chip junto");
            Igual("ASUS Dual RTX 3060", SpecSheet.ComMarca("ASUS", "ASUS Dual RTX 3060"), "modelo que ja traz a marca nao repete");
            Igual("Radeon RX 580 Series", SpecSheet.ComMarca(null, "Radeon RX 580 Series"), "sem marca o modelo fica como hoje");
            Igual("Sapphire Radeon RX 580 8 GB", SpecSheet.NomeComercial("Sapphire", "Radeon RX 580 Series", "8 GB"),
                "o destaque e o nome de loja, com a memoria");

            string languageBefore = T.Language;
            try
            {
                T.Language = T.PtBr;
                SpecGrupo comMarca = GrupoDeVideo("Sapphire Radeon RX 580 Series", "Sapphire Radeon RX 580 8 GB");
                using (BlocoDeSpec b = new BlocoDeSpec(comMarca))
                {
                    Igual("Sapphire Radeon RX 580 8 GB", Field(b, "_destaque"), "o mosaico mostra a marca no destaque");
                    Verdade(!((List<string>)Field(b, "_etiquetas")).Contains("8 GB"), "e nao repete a memoria numa etiqueta");
                    b.Size = new Size(460, b.AlturaPara(460));
                    CaptureControl(b, "specs-video-marca");
                }
                SpecGrupo semMarca = GrupoDeVideo("Radeon RX 580 Series", null);
                using (BlocoDeSpec b = new BlocoDeSpec(semMarca))
                {
                    Igual("Radeon RX 580 Series", Field(b, "_destaque"), "sem marca o destaque fica como hoje");
                    Verdade(((List<string>)Field(b, "_etiquetas")).Contains("8 GB"), "com a memoria na etiqueta");
                }
            }
            finally { T.Language = languageBefore; }
        }

        private static SpecGrupo GrupoDeVideo(string modelo, string destaque)
        {
            SpecGrupo g = new SpecGrupo(T.SpecGpu);
            g.Por(T.SpecModel, modelo);
            if (destaque != null) g.Por(T.SpecBoardMaker, "Sapphire");
            g.Por(T.SpecVram, "8 GB");
            g.Por(T.SpecResolution, "2560 × 1080  ·  75 Hz");
            g.Por(T.SpecDriver, "31.0.21925.1001");
            g.Por(T.SpecMonitor, "LG ULTRAWIDE");
            g.Destaque = destaque;
            return g;
        }

        // ---------------- slots de memoria ----------------

        /// <summary>Monta uma estrutura SMBIOS: cabecalho, parte formatada e strings.</summary>
        private static byte[] Estrutura(int tipo, int handle, byte[] corpo, params string[] strings)
        {
            List<byte> b = new List<byte>();
            b.Add((byte)tipo); b.Add((byte)(4 + corpo.Length));
            b.Add((byte)(handle & 0xFF)); b.Add((byte)(handle >> 8));
            b.AddRange(corpo);
            if (strings.Length == 0) b.Add(0);
            foreach (string s in strings) { b.AddRange(Encoding.ASCII.GetBytes(s)); b.Add(0); }
            b.Add(0);
            return b.ToArray();
        }

        /// <summary>Tipo 16: arranjo de memoria; uso 3 e a memoria do sistema.</summary>
        private static byte[] Arranjo(int handle, byte uso)
        {
            byte[] corpo = new byte[0x17 - 4];
            corpo[0x05 - 4] = uso;
            return Estrutura(16, handle, corpo);
        }

        /// <summary>Tipo 17 com tamanho em MB (0 = vazio), localizador, banco, fabricante e peca.</summary>
        private static byte[] Pente(int handle, int arranjo, int mb, string loc, string banco, string fab, string peca, uint estendido = 0)
        {
            byte[] corpo = new byte[0x22 - 4];
            corpo[0x04 - 4] = (byte)(arranjo & 0xFF); corpo[0x05 - 4] = (byte)(arranjo >> 8);
            corpo[0x0C - 4] = (byte)(mb & 0xFF); corpo[0x0D - 4] = (byte)(mb >> 8);
            corpo[0x10 - 4] = 1; corpo[0x11 - 4] = 2;
            List<string> s = new List<string> { loc, banco };
            if (mb != 0) { s.Add(fab); corpo[0x17 - 4] = 3; s.Add(peca); corpo[0x1A - 4] = 4; }
            BitConverter.GetBytes(estendido).CopyTo(corpo, 0x1C - 4);
            return Estrutura(17, handle, corpo, s.ToArray());
        }

        private static byte[] Tabela(params byte[][] partes)
        {
            List<byte> t = new List<byte>();
            foreach (byte[] p in partes) t.AddRange(p);
            t.AddRange(Estrutura(127, 0xFFFF, new byte[0]));
            return t.ToArray();
        }

        private static List<SlotDeMemoria> Slots(params string[][] pares)
        {
            List<SlotDeMemoria> l = new List<SlotDeMemoria>();
            foreach (string[] p in pares) l.Add(new SlotDeMemoria { Localizador = p[0], Banco = p.Length > 1 ? p[1] : null });
            return l;
        }

        private static string Nomes(List<SlotDeMemoria> l)
        {
            List<string> n = new List<string>();
            foreach (SlotDeMemoria s in l) n.Add(s.Nome);
            return string.Join(",", n.ToArray());
        }

        private static void RegressionSlotsDeMemoria()
        {
            Secao("Especificacoes: slots de memoria no lugar certo");

            // A B450 AORUS ELITE do Thiago: quatro slots, pentes em DIMM 0 de cada canal.
            byte[] t = Tabela(
                Arranjo(0x1000, 3),
                Pente(0x1100, 0x1000, 8192, "DIMM 0", "P0 CHANNEL A", "Kingston", "KHX2666C16/8G"),
                Pente(0x1101, 0x1000, 0, "DIMM 1", "P0 CHANNEL A", null, null),
                Pente(0x1102, 0x1000, 8192, "DIMM 0", "P0 CHANNEL B", "Kingston", "KHX2666C16/8G"),
                Pente(0x1103, 0x1000, 0, "DIMM 1", "P0 CHANNEL B", null, null),
                Arranjo(0x2000, 5),
                Pente(0x2100, 0x2000, 16, "SYSTEM ROM", "BIOS", "x", "y"));
            List<SlotDeMemoria> slots = SlotsDeMemoria.DaTabela(t);
            Igual(4, slots.Count, "a tabela traz os quatro slots, inclusive os vazios, e deixa a ROM de fora");
            Verdade(SlotsDeMemoria.Nomear(slots), "os nomes da placa viram canal e numero");
            Igual("A1,A2,B1,B2", Nomes(slots), "em ordem de canal e numero, contando a partir de 1");
            Verdade(slots[0].Ocupado && !slots[1].Ocupado && slots[2].Ocupado && !slots[3].Ocupado, "acende so onde ha pente");
            Igual(8UL * 1024 * 1024 * 1024, slots[0].Bytes, "tamanho em bytes");
            Igual("Kingston", slots[0].Fabricante, "fabricante do pente");
            Igual("KHX2666C16/8G", slots[2].Peca, "numero de peca do pente");
            Igual("A1: 8 GB  ·  Kingston  ·  KHX2666C16/8G", BlocoDeSpec.TextoDoSlot(slots[0]), "dica do slot ocupado");
            string languageBefore = T.Language;
            try
            {
                T.Language = T.PtBr;
                Igual("A2: vazio", BlocoDeSpec.TextoDoSlot(slots[1]), "dica do slot vazio");
            }
            finally { T.Language = languageBefore; }

            List<SlotDeMemoria> grande = SlotsDeMemoria.DaTabela(Tabela(Arranjo(1, 3),
                Pente(2, 1, 0x7FFF, "DIMM_A1", "BANK 0", "Samsung", "M378", 32768)));
            Igual(32UL * 1024 * 1024 * 1024, grande[0].Bytes, "acima de 32 GB vale o tamanho estendido");
            List<SlotDeMemoria> kb = SlotsDeMemoria.DaTabela(Tabela(Arranjo(1, 3), Pente(2, 1, 0x8000 | 512, "DIMM_A1", "BANK 0", "x", "y")));
            Igual(512UL * 1024, kb[0].Bytes, "bit 15 troca a unidade para KB");

            List<SlotDeMemoria> prontos = Slots(new[] { "DIMM_B2" }, new[] { "DIMM_A1" }, new[] { "DDR4_A2" }, new[] { "DIMM_B1" });
            Verdade(SlotsDeMemoria.Nomear(prontos), "nome pronto no localizador");
            Igual("A1,A2,B1,B2", Nomes(prontos), "DIMM_A1 e DDR4_A2 ordenados pelo nome");

            List<SlotDeMemoria> intel = Slots(new[] { "ChannelA-DIMM0", "BANK 0" }, new[] { "ChannelA-DIMM1", "BANK 1" },
                                              new[] { "ChannelB-DIMM0", "BANK 2" }, new[] { "ChannelB-DIMM1", "BANK 3" });
            Verdade(SlotsDeMemoria.Nomear(intel), "canal e numero no proprio localizador");
            Igual("A1,A2,B1,B2", Nomes(intel), "ChannelA-DIMM0 vira A1");

            Verdade(!SlotsDeMemoria.Nomear(Slots(new[] { "DIMM 0", "BANK 0" }, new[] { "DIMM 1", "BANK 1" })),
                "sem canal nenhum, nao inventa nome e o desenho volta ao antigo");
            Verdade(!SlotsDeMemoria.Nomear(Slots(new[] { "DIMM_A1" }, new[] { "A1" })), "dois slots com o mesmo nome tambem");
            Verdade(!SlotsDeMemoria.Nomear(new List<SlotDeMemoria>()), "nem tabela vazia");

            try
            {
                T.Language = T.PtBr;
                SpecGrupo ram = new SpecGrupo(T.SpecRam);
                ram.Por(T.SpecTotal, "16 GB"); ram.Por(T.SpecType, "DDR4"); ram.Por(T.SpecMemSpeed, "3200 MT/s");
                ram.Por(T.SpecChannels, "2"); ram.Por(T.SpecSlots, T.SpecSlotsUsed(2, 4)); ram.Por(T.SpecMaxRam, "128 GB");
                ram.Por("P0 CHANNEL A", "8 GB  ·  Kingston  ·  KHX2666C16/8G");
                int semSlots;
                using (BlocoDeSpec antigo = new BlocoDeSpec(ram))
                {
                    Verdade(Field(antigo, "_slots") == null, "sem a tabela da BIOS o desenho e o de antes");
                    Igual(4, Field(antigo, "_slotsTotal"), "com os quatro quadrados contados do Windows");
                    semSlots = antigo.AlturaPara(460);
                }
                ram.Slots = slots;
                using (Form f = new Form())
                using (BlocoDeSpec b = new BlocoDeSpec(ram))
                {
                    f.ShowInTaskbar = false; f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-20000, -20000);
                    f.Controls.Add(b);
                    b.SetBounds(0, 0, 460, b.AlturaPara(460));
                    f.Show();
                    // fora da tela o Windows nao pinta; desenhar num bitmap forca o OnPaint
                    using (Bitmap pintura = new Bitmap(b.Width, b.Height)) b.DrawToBitmap(pintura, new Rectangle(Point.Empty, pintura.Size));
                    Verdade(b.AlturaPara(460) > semSlots, "o nome embaixo de cada slot ganha altura");
                    Igual(2, Field(b, "_slotsUsados"), "dois ocupados");
                    List<Rectangle> areas = (List<Rectangle>)Field(b, "_areasDosSlots");
                    Igual(4, areas.Count, "uma area de mouse por slot");
                    Verdade(areas[0].Right < areas[1].Left && areas[1].Right < areas[2].Left, "slots lado a lado, sem sobrepor");
                    Point meio = new Point(areas[1].X + areas[1].Width / 2, areas[1].Y + 4);
                    InvokePrivate(b, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, meio.X, meio.Y, 0));
                    Igual(1, Field(b, "_slotSobMouse"), "o mouse sobre o segundo quadrado aponta o A2");
                    CaptureControl(b, "specs-slots");
                    InvokePrivate(b, "OnMouseLeave", EventArgs.Empty);
                    Igual(-1, Field(b, "_slotSobMouse"), "sair do bloco esquece o slot");
                    f.Close();
                }
            }
            finally { T.Language = languageBefore; }
        }

        // ---------------- metricas ----------------

        private static void RegressionNavegadorSegueCartao(string root)
        {
            Secao("Metricas: o navegador segue o grafico clicado");

            MetricCard solto = new MetricCard();
            int escolhas = 0, arrastes = 0;
            solto.Escolhido += delegate { escolhas++; };
            solto.Arrastar += delegate { arrastes++; };
            solto.Size = new Size(200, 120);
            InvokePrivate(solto, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 60, 60, 0));
            InvokePrivate(solto, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 61, 60, 0));
            Igual(1, escolhas, "clique no cartao escolhe");
            InvokePrivate(solto, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 60, 60, 0));
            InvokePrivate(solto, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 90, 60, 0));
            InvokePrivate(solto, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 90, 60, 0));
            Igual(1, arrastes, "arrastar continua sendo arraste");
            Igual(1, escolhas, "e nao conta como escolha");
            solto.Editavel = false;
            InvokePrivate(solto, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 60, 60, 0));
            InvokePrivate(solto, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 60, 60, 0));
            Igual(1, escolhas, "na Visao geral, onde o cartao nao e editavel, clique nao escolhe");
            solto.Dispose();

            Config config = AuditConfig(root, "navegador-segue");
            config.MetricIds.Clear(); config.MetricSizes.Clear();
            config.MetricIds.AddRange(new string[] { "cpu", "load" });
            config.MetricSizes.AddRange(new int[] { 1, 1 });
            config.Save();
            FreshData data = new FreshData();
            using (SettingsForm form = BugForm(config, data))
            {
                ((NavBar)Field(form, "_nav")).SelectedIndex = 2;
                NavegadorDeJanela nav = (NavegadorDeJanela)Field(form, "_navJanela");
                List<MetricCard> cards = (List<MetricCard>)Field(form, "_cards");
                Igual("cpu", nav.SensorId, "sem escolha, a faixa desenha o primeiro cartao");
                Verdade(cards[0].Selecionado && !cards[1].Selecionado, "e o primeiro aparece marcado");

                nav.Janela = 3600; nav.FimBalde = 0;
                InvokePrivate(cards[1], "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 40, 60, 0));
                InvokePrivate(cards[1], "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 40, 60, 0));
                Igual("load", nav.SensorId, "clicar no segundo troca a linha da faixa");
                Verdade(cards[1].Selecionado && !cards[0].Selecionado, "e a marca passa para ele");
                Igual(3600, nav.Janela, "o trecho escolhido nao muda na troca");
                InvokePrivate(form, "AtualizarMetricas", data.CurrentSnapshot());
                Color? corDaFaixa = nav.CorDaLinha;
                Verdade(corDaFaixa.HasValue && corDaFaixa.Value == cards[1].CorAtual, "a faixa usa a cor do cartao");
                Igual("load", Config.LoadFrom(config.SavePath, false).MetricNavId, "a escolha fica gravada");
                CaptureControl(form, "metricas-navegador-segue");
                form.Close();
            }

            using (SettingsForm form = BugForm(Config.LoadFrom(config.SavePath, false), data))
            {
                ((NavBar)Field(form, "_nav")).SelectedIndex = 2;
                NavegadorDeJanela nav = (NavegadorDeJanela)Field(form, "_navJanela");
                Igual("load", nav.SensorId, "reaberta, a faixa volta na ultima escolha");
                InvokePrivate(form, "MexerNaMetrica", "load", 0);
                nav = (NavegadorDeJanela)Field(form, "_navJanela");
                List<MetricCard> cards = (List<MetricCard>)Field(form, "_cards");
                Igual("cpu", nav.SensorId, "removido o cartao escolhido, volta para o primeiro");
                Verdade(cards.Count == 1 && cards[0].Selecionado, "e o primeiro fica marcado");
                form.Close();
            }

            Config ida = new Config(); ida.MetricNavId = "rtss:fps";
            Igual("rtss:fps", ida.Clone().MetricNavId, "a copia da configuracao leva a escolha");
        }

        // ---------------- layouts escolhidos: 5B, 6A e 7C ----------------

        private static void RegressionLayoutsEscolhidos(string root)
        {
            Secao("layouts 2.22: Configuracoes em linhas, Sobre com identidade, Perfis em duas colunas");
            Igual(MhiagosSetup.Setup.Versao, AppInfo.Versao, "a versao do Sobre e a do instalador andam juntas");
            string languageBefore = T.Language;
            try
            {
                foreach (string language in new string[] { T.PtBr, T.EnUs })
                {
                    T.Language = language;
                    Config config = AuditConfig(root, "layouts-" + language);
                    using (SettingsForm form = BugForm(config, new FreshData()))
                    {
                        NavBar nav = (NavBar)Field(form, "_nav");
                        foreach (int largura in new int[] { 1300, 880 })
                        {
                            form.Size = new Size(largura, 760);
                            string sufixo = language + "-" + largura;

                            // ---- 5B
                            nav.SelectedIndex = 4;
                            InvokePrivate(form, "ArrangeSettings");
                            Control config5 = (Control)Field(form, "_pgConfig");
                            List<Control> itens = (List<Control>)Field(form, "_itensDaConfig");
                            int linhas = 0, grupos = 0, anterior = -1;
                            foreach (Control item in itens)
                            {
                                if (item is LinhaDeOpcao) linhas++;
                                else if ((item.Tag as string) == "grupo") grupos++;
                                Verdade(item.Top >= anterior, "itens das Configuracoes em ordem, sem sobrepor " + sufixo);
                                anterior = item.Bottom;
                            }
                            // Nove desde a Visao geral nova (item 10 do backlog da Casa): a chave dela entrou no grupo Geral.
                            Igual(9, linhas, "nove linhas: idioma, inicio, visao geral nova, sensores, ocioso, RTSS, pasta, backup e diagnostico");
                            Igual(4, grupos, "em quatro grupos: Geral, Painel, FPS e Dados");
                            Verdade(Field(form, "_backupCard") is LinhaDeOpcao && Field(form, "_diagnosticCard") is LinhaDeOpcao,
                                "backup e diagnostico viraram linhas do grupo Dados");
                            LinhaDeOpcao idioma = (LinhaDeOpcao)itens[1];
                            Verdade(idioma.Controls.Contains((Control)Field(form, "_lang")), "o seletor de idioma fica na linha do Idioma");
                            if (largura == 1300) Verdade(idioma.EmUmaLinha, "em janela larga o controle fica a direita do texto");
                            CaptureEvolution(form, "layout-config-" + sufixo);

                            // ---- 6A
                            // Pela chave: a pagina Casa entrou antes do Sobre.
                            for (int i = 0; i < nav.Items.Count; i++)
                                if (nav.Items[i].Key == "sobre") nav.SelectedIndex = i;
                            CartaoDeIdentidade id = (CartaoDeIdentidade)Field(form, "_cartaoIdentidade");
                            Verdade(id.Versao.Contains(AppInfo.Versao), "a versao aparece no cartao de identidade");
                            Verdade(id.Icone != null && id.Icone.Width >= 96, "o icone grande vem da entrada de 128 px");
                            BlocoDeStatus painel = (BlocoDeStatus)Field(form, "_blocoPainel");
                            BlocoDeStatus fontes = (BlocoDeStatus)Field(form, "_blocoFontes");
                            BlocoDeStatus libs = (BlocoDeStatus)Field(form, "_blocoBibliotecas");
                            Verdade(painel.Top > id.Bottom && fontes.Top == painel.Top && libs.Top == painel.Top,
                                "tres blocos de situacao lado a lado, abaixo da identidade " + sufixo);
                            Verdade(painel.Height >= painel.AlturaPara(painel.Width) && libs.Height >= libs.AlturaPara(libs.Width),
                                "nenhum bloco corta o proprio texto");
                            Verdade(fontes.Valor.Contains("HWiNFO"), "o bloco de fontes conta o HWiNFO");
                            Label isencao = (Label)Field(form, "_sobreIsencao");
                            Verdade(isencao.Height >= isencao.GetPreferredSize(new Size(isencao.Width, 0)).Height,
                                "a isencao aparece inteira, sem corte");
                            CaptureEvolution(form, "layout-sobre-" + sufixo);

                            // ---- 7C
                            InvokePrivate(form, "AbrirPaginaPelaChave", "perfis");
                            InvokePrivate(form, "ArrangeProfiles");
                            Card lista = (Card)Field(form, "_profilesListCard"), previa = (Card)Field(form, "_profilesPreviewCard");
                            Card jogo = (Card)Field(form, "_cardGame"), rodizio = (Card)Field(form, "_cardRotation");
                            Control perfis = (Control)Field(form, "_pgPerfis");
                            bool duasColunas = perfis.Width - Pagina.LarguraDaBarra - 4 >= 700;
                            if (duasColunas)
                            {
                                Verdade(lista.Top == previa.Top && previa.Left > lista.Right, "lista e previa lado a lado no alto " + sufixo);
                                Verdade(rodizio.Left == lista.Left && rodizio.Top > lista.Bottom, "o Rodizio fica embaixo da lista " + sufixo);
                                Verdade(jogo.Left == previa.Left && jogo.Top > previa.Bottom, "o Perfil por jogo fica embaixo da previa " + sufixo);
                                Igual(rodizio.Bottom, jogo.Bottom, "as duas colunas terminam juntas " + sufixo);
                            }
                            FlatBtn aplicar = (FlatBtn)Field(form, "_btnApply");
                            Verdade(aplicar.Parent == previa, "Aplicar fica no cartao da previa");
                            List<FlatBtn> botoes = (List<FlatBtn>)Field(form, "_botoesDePerfil");
                            Igual(6, botoes.Count, "os seis botoes de perfil ficam embaixo da lista");
                            foreach (Card cartao in new Card[] { lista, previa, jogo, rodizio })
                                foreach (Control b in cartao.Controls)
                                    if (b is FlatBtn)
                                    {
                                        Verdade(b.Left >= 0 && b.Right <= cartao.Width && b.Bottom <= cartao.Height, "botao dentro do cartao " + sufixo);
                                        foreach (Control o in cartao.Controls)
                                            if (o != b && o is FlatBtn) Verdade(!b.Bounds.IntersectsWith(o.Bounds), "botoes sem sobrepor " + sufixo);
                                    }
                            ProfileList listaDePerfis = (ProfileList)Field(form, "_profileList");
                            Verdade(listaDePerfis.Bottom <= botoes[0].Top, "a lista termina antes dos botoes " + sufixo);
                            Igual(T.AlreadyActive, aplicar.Text, "perfil ja ativo: o botao diz isso, curto");
                            Toggle girar = (Toggle)Field(form, "_tgRotate");
                            NumberBox segundos = (NumberBox)Field(form, "_rotSecs");
                            Verdade(segundos.Top >= girar.Bottom, "o campo de segundos fica abaixo do interruptor, mesmo quando ele quebra");
                            CaptureEvolution(form, "layout-perfis-" + sufixo);
                        }

                        Toggle ocioso = (Toggle)Field(form, "_tgIdle");
                        Label nota = (Label)Field(form, "_idleNote");
                        nav.SelectedIndex = 4;
                        int fechado = nota.Width;
                        ocioso.Checked = true;
                        Igual(T.MinutesIdle, nota.Text, "ligar o apagamento mostra os minutos");
                        Verdade(nota.Width > fechado, "e o rotulo cresce para o texto novo");
                        ocioso.Checked = false;
                        // so mexeu para testar: fechar sem a pergunta de salvar
                        typeof(SettingsForm).GetProperty("_dirty", AuditFlags).SetValue(form, false, null);
                        form.Close();
                    }
                }
            }
            finally { T.Language = languageBefore; }
        }

        /// <summary>Seis horas de leituras de mentira para "cpu" e "load", so para a captura ter forma.</summary>
        private static void SemearHistorico()
        {
            ResetHistory("cpu", "load");
            DateTime agora = DateTime.UtcNow;
            for (int i = 0; i < 4320; i++)
            {
                int k = i;
                MetricHistory.AmostrarEm(delegate(string id)
                {
                    if (id == "cpu") return (float)(42 + 8 * Math.Sin(k / 90.0) + (k % 700 < 40 ? 18 : 0));
                    return (float)(15 + 55 * Math.Pow(Math.Sin(k / 400.0), 2));
                }, agora.AddSeconds(-5 * (4320 - k)));
            }
        }

        private static void CapturaDasMetricas(string root)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MHIAGOS_VISUAL_DIR"))) return;
            string languageBefore = T.Language;
            try
            {
                T.Language = T.PtBr;
                SemearHistorico();
                Config config = AuditConfig(root, "captura-metricas");
                config.MetricIds.Clear(); config.MetricSizes.Clear();
                config.MetricIds.AddRange(new string[] { "cpu", "load" });
                config.MetricSizes.AddRange(new int[] { 1, 1 });
                config.Save();
                using (SettingsForm form = BugForm(config, new FreshData()))
                {
                    form.Size = new Size(1100, 560);
                    ((NavBar)Field(form, "_nav")).SelectedIndex = 2;
                    List<MetricCard> cards = (List<MetricCard>)Field(form, "_cards");
                    InvokePrivate(cards[1], "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 40, 60, 0));
                    InvokePrivate(cards[1], "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 40, 60, 0));
                    CaptureEvolution(form, "depois-metricas");
                    form.Close();
                }
            }
            finally
            {
                T.Language = languageBefore;
                MetricHistory.Seguir(null);
            }
        }

        private static void CaptureControl(Control control, string name)
        {
            string directory = Environment.GetEnvironmentVariable("MHIAGOS_VISUAL_DIR");
            if (string.IsNullOrEmpty(directory) || control.Width <= 0 || control.Height <= 0) return;
            Directory.CreateDirectory(directory);
            using (Bitmap bitmap = new Bitmap(control.Width, control.Height))
            {
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(directory, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}
