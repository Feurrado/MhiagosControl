using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    public partial class SettingsForm : Form
    {
        private Pagina _pgSpecs;
        private Label _lbSpecsEstado, _lbSpecsNota;
        private FlatBtn _btCopiarSpecs;
        private bool _coletando = false;
        private readonly List<FlatBtn> _specCategoryButtons = new List<FlatBtn>();
        private readonly Dictionary<Card, int> _specFullHeights = new Dictionary<Card, int>();
        private int _specCategory = 0; // zero = resumo; os demais seguem _specCards
        /// <summary>
        /// A folha de especificacoes, no espirito do CPU-Z.
        ///
        /// Nasce vazia, com um aviso de que esta consultando: a coleta usa WMI e
        /// custa segundos - o Win32_Processor sozinho levou 1427 ms medidos - e
        /// montar isso no construtor faria a janela inteira demorar para abrir
        /// por causa de uma aba que a pessoa talvez nem visite.
        /// </summary>
        private Control BuildPageSpecs()
        {
            _pgSpecs = new Pagina();
            _pgSpecs.RolarNaVertical();

            // Cabecalho de UMA fileira: botao a esquerda, aviso ao lado.
            //
            // Antes eram tres linhas empilhadas - estado, botao, nota - e o
            // estado ficava invisivel depois da coleta, deixando a altura dele
            // como buraco entre o botao e o primeiro cartao. O estado agora
            // divide a linha com o botao e some sem levar altura junto.
            _btCopiarSpecs = new FlatBtn();
            _btCopiarSpecs.Text = T.SpecsCopy;
            _btCopiarSpecs.SetBounds(Ui.S(2), Ui.S(2), Ui.S(150), Ui.S(30));
            _btCopiarSpecs.Enabled = false;
            _btCopiarSpecs.Click += delegate
            {
                // Depois de uma coleta que falhou, o mesmo botao tenta de novo.
                if (SpecSheet.Folha != null) { CopiarSpecs(); return; }
                _btCopiarSpecs.Text = T.SpecsCopy;
                _btCopiarSpecs.Enabled = false;
                _lbSpecsEstado.Text = T.SpecsLoading;
                _lbSpecsEstado.ForeColor = Ui.Muted;
                GarantirSpecs();
            };
            _pgSpecs.Controls.Add(_btCopiarSpecs);

            _lbSpecsEstado = MakeLabel(T.SpecsLoading, 166, 4, Ui.FontBase);
            _lbSpecsEstado.Size = new Size(Ui.S(560), Ui.S(26));
            _lbSpecsEstado.TextAlign = ContentAlignment.MiddleLeft;
            _lbSpecsEstado.ForeColor = Ui.Muted;
            _pgSpecs.Controls.Add(_lbSpecsEstado);

            _lbSpecsNota = MakeLabel(T.SpecsNote, 166, 4, Ui.FontSmall);
            _lbSpecsNota.Size = new Size(Ui.S(600), Ui.S(28));
            _lbSpecsNota.TextAlign = ContentAlignment.MiddleLeft;
            _lbSpecsNota.ForeColor = Ui.Faint;
            _lbSpecsNota.Visible = false;
            _pgSpecs.Controls.Add(_lbSpecsNota);

            return _pgSpecs;
        }

        /// <summary>
        /// Dispara a coleta, uma vez, quando a aba estreia.
        ///
        /// Em thread propria e com retorno por BeginInvoke: WMI na thread da
        /// interface trava a bomba de mensagens, e alguns segundos travado sao
        /// suficientes para o Windows escurecer a janela e chama-la de "nao
        /// responde".
        /// </summary>
        private void GarantirSpecs()
        {
            if (_pgSpecs == null || _coletando) return;
            if (SpecSheet.Folha != null) { MostrarSpecs(); return; }

            _coletando = true;
            List<SensorEntry> copia = _sensors;

            // Qualificado: importar System.Threading deixaria "Timer" ambiguo
            // entre o de threading e o do Windows Forms, e esta classe usa o
            // segundo. O TrayApp resolve a mesma colisao do mesmo jeito.
            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                try { SpecSheet.Coletar(copia); }
                catch (Exception ex) { Log.Error("coleta de especificacoes", ex); }

                try
                {
                    if (IsHandleCreated) BeginInvoke(new MethodInvoker(MostrarSpecs));
                }
                catch (Exception ex) { Log.Error("retorno da coleta", ex); }
            });
            t.IsBackground = true;
            t.Name = "SpecSheet";
            t.Start();
        }

        private void MostrarSpecs()
        {
            _coletando = false;

            List<SpecGrupo> folha = SpecSheet.Folha;
            if (_pgSpecs == null) return;
            if (folha == null || folha.Count == 0)
            {
                // Estado de erro, e nao o "Consultando" parado para sempre.
                _lbSpecsEstado.Text = T.SpecsFailed;
                _lbSpecsEstado.ForeColor = Ui.Text;
                _lbSpecsEstado.Visible = true;
                _lbSpecsNota.Visible = false;
                _btCopiarSpecs.Text = T.SpecsRetry;
                _btCopiarSpecs.Enabled = true;
                return;
            }

            // Ja montada: a folha nao muda enquanto a maquina esta ligada.
            if (_specCards.Count > 0) return;

            _pgSpecs.SuspendLayout();
            try
            {
                _lbSpecsEstado.Visible = false;
                _lbSpecsNota.Visible = true;
                _btCopiarSpecs.Text = T.SpecsCopy;
                _btCopiarSpecs.Enabled = true;

                foreach (SpecGrupo g in folha)
                {
                    Card c = new Card();
                    c.Title = g.Titulo;
                    _pgSpecs.Controls.Add(c);
                    _specCards.Add(c);

                    List<Label> vals = new List<Label>();
                    int y = 46;
                    foreach (string[] l in g.Linhas)
                    {
                        Label cap = MakeLabel(l[0], 16, y + 2, Ui.FontSmall);
                        cap.ForeColor = Ui.Muted;
                        c.Controls.Add(cap);

                        Label val = MakeLabel(l[1], 0, y, Ui.FontSemi);
                        // Reticencias em vez de corte seco: "American Megatrends
                        // International, LLC." nao cabe numa coluna estreita, e
                        // sem elas o texto acabava no meio de uma letra, sem nada
                        // avisando que havia mais.
                        val.AutoEllipsis = true;
                        val.Tag = cap;
                        c.Controls.Add(val);
                        vals.Add(val);

                        y += 24;
                    }
                    c.Height = Ui.S(y + 10);   // y em medida de projeto
                    c.Tag = vals;
                    _specFullHeights[c] = c.Height;

                    BlocoDeSpec bloco = new BlocoDeSpec(g);
                    int categoria = _specBlocos.Count + 1;
                    bloco.Click += delegate { SelecionarCategoriaDeSpec(categoria); };
                    _pgSpecs.Controls.Add(bloco);
                    _specBlocos.Add(bloco);
                }

                MontarCategoriasDeSpecs(folha);
            }
            finally { _pgSpecs.ResumeLayout(); }

            ArranjarSpecsPagina();
        }

        private readonly List<Card> _specCards = new List<Card>();

        /// <summary>Os blocos do resumo, na mesma ordem de _specCards.</summary>
        private readonly List<BlocoDeSpec> _specBlocos = new List<BlocoDeSpec>();

        private void MontarCategoriasDeSpecs(List<SpecGrupo> folha)
        {
            if (_specCategoryButtons.Count > 0) return;
            string[] nomes = new string[folha.Count + 1];
            nomes[0] = T.SpecsOverview;
            for (int i = 0; i < folha.Count; i++) nomes[i + 1] = folha[i].Titulo;

            for (int i = 0; i < nomes.Length; i++)
            {
                FlatBtn b = new FlatBtn();
                b.Text = nomes[i];
                b.Tag = i;
                b.Primary = i == _specCategory;
                b.Height = Ui.S(32);
                b.Click += delegate(object sender, EventArgs e)
                {
                    FlatBtn clicked = sender as FlatBtn;
                    if (clicked == null) return;
                    SelecionarCategoriaDeSpec((int)clicked.Tag);
                };
                _pgSpecs.Controls.Add(b);
                _specCategoryButtons.Add(b);
            }
        }

        private void SelecionarCategoriaDeSpec(int categoria)
        {
            _specCategory = categoria;
            foreach (FlatBtn item in _specCategoryButtons)
            {
                item.Primary = (int)item.Tag == categoria;
                item.Invalidate();
            }
            ArranjarSpecsPagina();
            _pgSpecs.RolarPor(-100000);   // a ficha abre do topo
        }

        /// <summary>
        /// Duas colunas de cartoes, empacotadas na mais curta.
        ///
        /// Empilhar tudo numa coluna daria uma pagina de tres metros com metade
        /// da largura vazia. Duas colunas com o proximo cartao indo sempre para a
        /// mais BAIXA e o que mantem as duas parelhas quando os cartoes tem
        /// alturas muito diferentes - e tem: "Sistema" sao tres linhas e
        /// "Processador" sao treze.
        /// </summary>
        private void ArranjarSpecsPagina()
        {
            if (_pgSpecs == null || _specCards.Count == 0) return;

            int Esp = Ui.S(12);

            int disp = _pgSpecs.Width - Ui.S(4) - Pagina.LarguraDaBarra;
            if (disp > LarguraMaxDaPagina) disp = LarguraMaxDaPagina;
            if (disp < Ui.S(460)) disp = Ui.S(460);

            // Categorias em pastilhas que quebram de linha. Uma faixa dividida
            // em partes iguais esmagaria "Armazenamento" em janelas estreitas;
            // aqui cada uma ocupa o texto que realmente possui.
            int catX = Ui.S(2), catY = _btCopiarSpecs.Bottom + Ui.S(10);
            foreach (FlatBtn b in _specCategoryButtons)
            {
                int w = TextRenderer.MeasureText(b.Text, Ui.FontBase).Width + Ui.S(30);
                w = Math.Max(Ui.S(82), Math.Min(Ui.S(164), w));
                if (catX > Ui.S(2) && catX + w > disp) { catX = Ui.S(2); catY += Ui.S(36); }
                b.SetBounds(catX, catY, w, Ui.S(30));
                catX += w + Ui.S(6);
            }
            int topo = (_specCategoryButtons.Count > 0 ? catY + Ui.S(30) : _btCopiarSpecs.Bottom) + Ui.S(12);

            if (_specCategory > 0)
            {
                foreach (BlocoDeSpec bl in _specBlocos) bl.Visible = false;
                int escolhido = _specCategory - 1;
                for (int i = 0; i < _specCards.Count; i++)
                {
                    Card c = _specCards[i];
                    c.Visible = i == escolhido;
                    if (!c.Visible) continue;
                    MostrarLinhasDeSpec(c, int.MaxValue);
                    int detailWidth = Math.Min(Ui.S(820), disp);
                    int detailX = Ui.S(2) + (disp - detailWidth) / 2;
                    c.SetBounds(detailX, topo, detailWidth, _specFullHeights[c]);
                    ArranjarLinhasDeSpec(c);
                }
                _pgSpecs.Sincronizar();
                return;
            }

            // Resumo: um mosaico de blocos, cada peca com o que se procura nela.
            // A tabela completa de cada uma fica na categoria, a um clique.
            if (_specBlocos.Count == _specCards.Count && _specBlocos.Count > 0)
            {
                foreach (Card c in _specCards) c.Visible = false;
                ArranjarMosaicoDeSpecs(topo, disp, Esp);
                if (_lbSpecsNota != null)
                    _lbSpecsNota.Width = Math.Max(Ui.S(160), disp - _lbSpecsNota.Left);
                _pgSpecs.Sincronizar();
                return;
            }

            foreach (Card c in _specCards)
            {
                c.Visible = true;
                c.Height = Math.Min(Ui.S(128), _specFullHeights[c]);
                MostrarLinhasDeSpec(c, 3);
            }

            if (disp >= Ui.S(620) && _specCards.Count >= 7)
            {
                // Ordem visual estável: duas fileiras de componentes, discos
                // usando a largura toda e, por fim, rede/sistema. O antigo
                // empacotamento pela coluna mais curta deixava Armazenamento
                // sozinho diante de dois cartões empilhados e um grande vazio.
                int half = (disp - Esp) / 2;
                int y = topo;
                ArranjarParDeSpecs(0, 1, y, half, Esp); y += Ui.S(128) + Esp;
                ArranjarParDeSpecs(2, 3, y, half, Esp); y += Ui.S(128) + Esp;

                Card storage = _specCards[4];
                storage.SetBounds(Ui.S(2), y, disp, Ui.S(104));
                ArranjarLinhasDeSpecEmColunas(storage);
                y += storage.Height + Esp;

                ArranjarParDeSpecs(5, 6, y, half, Esp); y += Ui.S(128) + Esp;

                // Grupos futuros não somem: entram abaixo em largura inteira.
                for (int i = 7; i < _specCards.Count; i++)
                {
                    Card extra = _specCards[i];
                    extra.SetBounds(Ui.S(2), y, disp, Ui.S(128));
                    ArranjarLinhasDeSpec(extra);
                    y += extra.Height + Esp;
                }
            }
            else
            {
                int y = topo;
                foreach (Card c in _specCards)
                {
                    c.SetBounds(Ui.S(2), y, disp, Ui.S(128));
                    ArranjarLinhasDeSpec(c);
                    y += c.Height + Esp;
                }
            }

            // O aviso ao lado do botao acompanha a largura: fixo em 600 px, ele
            // saia cortado no meio de "nome da maquina" numa janela estreita.
            if (_lbSpecsNota != null)
                _lbSpecsNota.Width = Math.Max(Ui.S(160), disp - _lbSpecsNota.Left);

            _pgSpecs.Sincronizar();
        }

        /// <summary>
        /// O mosaico: processador e discos na largura inteira, o resto aos pares.
        ///
        /// A ordem segue o que se procura primeiro - processador, video e
        /// memoria - e nao a ordem da coleta. Os dois blocos de um par ficam com
        /// a altura do maior, para as bordas de baixo alinharem; um bloco que
        /// sobraria sozinho numa linha toma a largura inteira.
        /// </summary>
        private void ArranjarMosaicoDeSpecs(int topo, int disp, int esp)
        {
            string[] ordem = { T.SpecCpu, T.SpecGpu, T.SpecRam, T.SpecStorage, T.SpecBoard, T.SpecOs, T.SpecNetwork };
            List<BlocoDeSpec> fila = new List<BlocoDeSpec>();
            foreach (string t in ordem)
                foreach (BlocoDeSpec b in _specBlocos)
                    if (b.Grupo.Titulo == t && !fila.Contains(b)) fila.Add(b);
            foreach (BlocoDeSpec b in _specBlocos) if (!fila.Contains(b)) fila.Add(b);

            bool duas = disp >= Ui.S(620);
            int meia = (disp - esp) / 2;
            int y = topo;
            int i = 0;
            while (i < fila.Count)
            {
                BlocoDeSpec a = fila[i];
                a.Visible = true;
                BlocoDeSpec b = (duas && !a.Largo && i + 1 < fila.Count && !fila[i + 1].Largo) ? fila[i + 1] : null;
                if (b == null)
                {
                    int h = a.AlturaPara(disp);
                    a.SetBounds(Ui.S(2), y, disp, h);
                    y += h + esp;
                    i++;
                    continue;
                }
                b.Visible = true;
                int alt = Math.Max(a.AlturaPara(meia), b.AlturaPara(meia));
                a.SetBounds(Ui.S(2), y, meia, alt);
                b.SetBounds(Ui.S(2) + meia + esp, y, disp - meia - esp, alt);
                y += alt + esp;
                i += 2;
            }
        }

        private void ArranjarParDeSpecs(int left, int right, int y, int width, int gap)
        {
            Card a = _specCards[left], b = _specCards[right];
            a.SetBounds(Ui.S(2), y, width, Ui.S(128));
            b.SetBounds(Ui.S(2) + width + gap, y, width, Ui.S(128));
            ArranjarLinhasDeSpec(a);
            ArranjarLinhasDeSpec(b);
        }

        /// <summary>Três unidades de armazenamento lado a lado no resumo.</summary>
        private static void ArranjarLinhasDeSpecEmColunas(Card card)
        {
            List<Label> vals = card.Tag as List<Label>;
            if (vals == null || vals.Count == 0) return;
            int count = Math.Min(3, vals.Count);
            int gap = Ui.S(18);
            int width = (card.Width - Ui.S(32) - (count - 1) * gap) / count;
            for (int i = 0; i < vals.Count; i++)
            {
                bool visible = i < count;
                Label val = vals[i];
                Label cap = val.Tag as Label;
                val.Visible = visible;
                if (cap != null) cap.Visible = visible;
                if (!visible) continue;
                int x = Ui.S(16) + i * (width + gap);
                if (cap != null) cap.SetBounds(x, Ui.S(46), width, Ui.S(17));
                val.SetBounds(x, Ui.S(66), width, Ui.S(20));
            }
        }

        private static void MostrarLinhasDeSpec(Card card, int quantidade)
        {
            List<Label> vals = card.Tag as List<Label>;
            if (vals == null) return;
            for (int i = 0; i < vals.Count; i++)
            {
                bool visible = i < quantidade;
                vals[i].Visible = visible;
                Label cap = vals[i].Tag as Label;
                if (cap != null) cap.Visible = visible;
            }
        }

        /// <summary>
        /// As linhas de um cartao de especificacao, medidas pela largura dele.
        ///
        /// Explicito, e nao por ancora. A ancora calcula o deslocamento a partir
        /// do tamanho que o cartao tinha quando o filho entrou - e aqui os filhos
        /// entram antes de o cartao ser dimensionado, entao a conta partia de um
        /// tamanho que nunca foi o de verdade e o valor saia cortado.
        ///
        /// A coluna do rotulo e proporcional, com teto: num cartao estreito ela
        /// cede espaco para o valor, que e a parte que muda de tamanho. Fixa em
        /// 150 px, ela comia mais da metade de uma coluna de tres.
        /// </summary>
        private static void ArranjarLinhasDeSpec(Card c)
        {
            List<Label> vals = c.Tag as List<Label>;
            if (vals == null) return;

            int rotulo = c.Width * 30 / 100;
            if (rotulo < Ui.S(88)) rotulo = Ui.S(88);
            if (rotulo > Ui.S(128)) rotulo = Ui.S(128);

            int xVal = Ui.S(16) + rotulo + Ui.S(8);
            int larg = c.Width - xVal - Ui.S(14);
            if (larg < Ui.S(60)) larg = Ui.S(60);

            for (int i = 0; i < vals.Count; i++)
            {
                Label val = vals[i];
                Label cap = val.Tag as Label;
                int y = Ui.S(46) + i * Ui.S(24);
                if (cap != null) cap.SetBounds(Ui.S(16), y + Ui.S(2), rotulo, Ui.S(17));
                val.SetBounds(xVal, y, larg, Ui.S(19));
            }
        }

        private void CopiarSpecs()
        {
            try
            {
                string t = SpecSheet.EmTexto();
                if (string.IsNullOrEmpty(t)) return;
                Clipboard.SetText(t);
                Aviso(T.SpecsCopied);
            }
            catch (Exception ex) { Log.Error("copiar especificacoes", ex); }
        }
    }
}
