using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    public partial class SettingsForm : Form
    {
        private Control BuildPageMetricas()
        {
            _pgMetricas = new Pagina();
            // fundo opaco vem da propria Pagina

            // Rolagem so na vertical - e com a barra propria, que nao precisa da
            // ginastica de desligar e religar o AutoScroll para nao trazer a
            // horizontal junto: ela simplesmente nao existe.
            _pgMetricas.RolarNaVertical();

            // A grade se refaz com a janela: as larguras saem da area
            // disponivel, e nao de um numero fixo que so serve para o tamanho
            // em que foi escrito.
            _pgMetricas.Resize += delegate { ArranjarMetricas(); };

            // Primeira abertura: monta uma selecao automatica e grava. A partir
            // dai a lista e do usuario, inclusive vazia - por isso o marcador
            // separado, e nao "lista vazia significa recomecar".
            if (!_cfg.MetricsChosen) SelecaoPadrao();

            MontarMetricas();
            return _pgMetricas;
        }

        /// <summary>
        /// Repoe o conjunto basico: uma leitura de cada grandeza por peca.
        ///
        /// Roda sozinha na primeira abertura e fica disponivel num botao. O
        /// botao nao e conveniencia: quem ja abriu o aplicativo antes tem
        /// "escolhido" gravado, e sem ele uma correcao na selecao automatica -
        /// como a temperatura que faltava - nunca chegaria a quem ja usa.
        /// </summary>
        private void SelecaoPadrao()
        {
            AplicarConjunto(null);
        }

        /// <summary>
        /// O menu de conjuntos, aberto sob o botao.
        ///
        /// Menu, e nao quatro botoes na fileira: aplicar um conjunto e um ato
        /// raro - dia da instalacao, e de vez em quando - e quatro alvos
        /// permanentes disputando a barra com "Adicionar metrica" e a escolha da
        /// janela dariam a esse ato o peso de uma acao cotidiana.
        /// </summary>
        private void MenuDeConjuntos()
        {
            // ContextMenuStrip com o renderizador do tema (MenuTema): o menu
            // nativo nao aceitava cor, fonte nem raio.
            ContextMenuStrip m = MenuTema.Novo();

            foreach (MetricPicker.Conjunto c in MetricPicker.Conjuntos)
            {
                MetricPicker.Conjunto alvo = c;   // copia local para o delegate
                m.Items.Add(MenuTema.Item(c.Nome, delegate { AplicarConjunto(alvo); }));
            }

            MostrarMenu(m, _btPadraoMetrica);
        }

        private ContextMenuStrip _menuAberto;

        /// <summary>
        /// Mostra o menu sob o botao. O anterior so e descartado agora: o
        /// clique numa opcao chega depois do fechamento, e descartar o menu no
        /// Closed perderia a acao.
        /// </summary>
        private void MostrarMenu(ContextMenuStrip m, Control ancora)
        {
            if (_menuAberto != null) _menuAberto.Dispose();
            _menuAberto = m;
            m.Show(ancora, new Point(0, ancora.Height));
        }

        /// <summary>
        /// Perfis de métricas ficam num menu hierárquico: a primeira ação cria
        /// um novo e cada perfil existente concentra aplicar/atualizar/renomear/
        /// excluir sem ocupar permanentemente a barra da grade.
        /// </summary>
        private void MenuDePerfisDeMetricas()
        {
            ContextMenuStrip menu = MenuTema.Novo();
            menu.Items.Add(MenuTema.Item(T.SaveMetricProfile,
                delegate { SalvarPerfilDeMetricas(); }));

            if (_cfg.MetricProfiles.Count > 0) MenuTema.Separador(menu.Items);
            foreach (MetricProfile item in _cfg.MetricProfiles)
            {
                MetricProfile profile = item;
                ToolStripMenuItem group = MenuTema.Submenu(profile.Name);
                group.DropDownItems.Add(MenuTema.Item(T.Apply,
                    delegate { AplicarPerfilDeMetricas(profile); }));
                group.DropDownItems.Add(MenuTema.Item(T.UpdateMetricProfile,
                    delegate { AtualizarPerfilDeMetricas(profile); }));
                group.DropDownItems.Add(MenuTema.Item(T.RenameMetricProfile,
                    delegate { RenomearPerfilDeMetricas(profile); }));
                MenuTema.Separador(group.DropDownItems);
                group.DropDownItems.Add(MenuTema.Item(T.DeleteMetricProfile,
                    delegate { ExcluirPerfilDeMetricas(profile); }));
                menu.Items.Add(group);
            }
            MostrarMenu(menu, _btPerfisMetrica);
        }

        private MetricProfile CapturarPerfilDeMetricas(string nome)
        {
            MetricProfile profile = new MetricProfile();
            profile.Name = nome;
            profile.Range = MetricHistory.JanelaValida(_cfg.MetricRange);
            profile.Ids.AddRange(_cfg.MetricIds);
            profile.Sizes.AddRange(_cfg.MetricSizes);
            return profile;
        }

        private static void CopiarPerfilDeMetricas(MetricProfile source, MetricProfile target)
        {
            target.Range = source.Range;
            target.Ids.Clear(); target.Ids.AddRange(source.Ids);
            target.Sizes.Clear(); target.Sizes.AddRange(source.Sizes);
        }

        private void SalvarPerfilDeMetricas()
        {
            string nome = Prompt(T.MetricProfileName,
                T.DefaultMetricProfileName(_cfg.MetricProfiles.Count + 1));
            if (string.IsNullOrWhiteSpace(nome)) return;
            if (_cfg.MetricProfileNameExists(nome)) { Warn(T.NameTaken); return; }

            MetricProfile profile = CapturarPerfilDeMetricas(nome);
            _cfg.MetricProfiles.Add(profile);
            if (!GravarMetricas()) { _cfg.MetricProfiles.Remove(profile); return; }
            Aviso(T.MetricProfileSaved(profile.Name));
        }

        private void AplicarPerfilDeMetricas(MetricProfile profile)
        {
            if (profile == null) return;
            Config antes = _cfg.Clone();
            List<string> oldIds = new List<string>(_cfg.MetricIds);
            List<int> oldSizes = new List<int>(_cfg.MetricSizes);
            int oldRange = _cfg.MetricRange;

            _cfg.MetricIds.Clear(); _cfg.MetricIds.AddRange(profile.Ids);
            _cfg.MetricSizes.Clear(); _cfg.MetricSizes.AddRange(profile.Sizes);
            _cfg.MetricRange = MetricHistory.JanelaValida(profile.Range);
            _cfg.MetricsChosen = true;
            if (!GravarMetricas())
            {
                _cfg.MetricIds.Clear(); _cfg.MetricIds.AddRange(oldIds);
                _cfg.MetricSizes.Clear(); _cfg.MetricSizes.AddRange(oldSizes);
                _cfg.MetricRange = oldRange;
                return;
            }
            MontarMetricas();
            if (Applied != null) Applied(this, EventArgs.Empty);
            OferecerDesfazer(T.MetricProfileApplied(profile.Name), delegate { RestaurarMetricas(antes); });
        }

        private void AtualizarPerfilDeMetricas(MetricProfile profile)
        {
            if (profile == null) return;
            MetricProfile backup = profile.Clone();
            CopiarPerfilDeMetricas(CapturarPerfilDeMetricas(profile.Name), profile);
            if (!GravarMetricas()) CopiarPerfilDeMetricas(backup, profile);
            else Aviso(T.MetricProfileSaved(profile.Name));
        }

        private void RenomearPerfilDeMetricas(MetricProfile profile)
        {
            if (profile == null) return;
            string nome = Prompt(T.MetricProfileName, profile.Name);
            if (string.IsNullOrWhiteSpace(nome) || nome == profile.Name) return;
            MetricProfile existing = _cfg.MetricProfileByName(nome);
            if (existing != null && !ReferenceEquals(existing, profile)) { Warn(T.NameTaken); return; }
            string old = profile.Name;
            profile.Name = nome;
            if (!GravarMetricas()) profile.Name = old;
        }

        private void ExcluirPerfilDeMetricas(MetricProfile profile)
        {
            if (profile == null) return;
            Config antes = _cfg.Clone();
            string nome = profile.Name;
            int index = _cfg.MetricProfiles.IndexOf(profile);
            if (index < 0) return;
            _cfg.MetricProfiles.RemoveAt(index);
            if (!GravarMetricas()) { _cfg.MetricProfiles.Insert(index, profile); return; }
            OferecerDesfazer(T.MetricProfileDeleted(nome), delegate { RestaurarMetricas(antes); });
        }

        /// <summary>
        /// Troca a grade pelo conjunto escolhido. Nulo significa o automatico.
        ///
        /// Conjunto que nao acha nada NAO limpa a grade: numa maquina sem RTSS o
        /// conjunto de jogos traria duas leituras, e numa sem sensor de ventoinha
        /// o silencioso traria zero - e trocar o que a pessoa montou por uma tela
        /// vazia e destruir trabalho para nao entregar nada.
        /// </summary>
        private void AplicarConjunto(MetricPicker.Conjunto c)
        {
            List<SensorEntry> escolhidos = MetricPicker.Montar(c, _sensors);
            if (escolhidos.Count == 0) { Aviso(T.PresetEmpty); return; }

            Config antes = _cfg.Clone();
            _cfg.MetricIds.Clear();
            _cfg.MetricSizes.Clear();
            foreach (SensorEntry s in escolhidos)
            {
                _cfg.MetricIds.Add(s.Id);
                _cfg.MetricSizes.Add(0);
            }
            _cfg.MetricsChosen = true;
            if (!GravarMetricas()) return;
            MontarMetricas();

            // A grade nova precisa de historico: sem avisar quem grava, os
            // cartoes recem-criados ficariam sem curva ate alguem reabrir o
            // aplicativo.
            if (Applied != null) Applied(this, EventArgs.Empty);

            OferecerDesfazer(c != null ? T.PresetApplied(c.Nome, escolhidos.Count) : (T.Pt ? "Grade automática aplicada" : "Automatic grid applied"), delegate { RestaurarMetricas(antes); });
        }

        private bool GravarMetricas()
        {
            try
            {
                string erro;
                bool salvo = _session.TrySavePreferences(delegate(Config draft, Config target)
                {
                    target.MetricIds.Clear(); target.MetricIds.AddRange(draft.MetricIds);
                    target.MetricSizes.Clear(); target.MetricSizes.AddRange(draft.MetricSizes);
                    target.MetricsChosen = draft.MetricsChosen;
                    target.MetricRange = draft.MetricRange;
                    target.MetricNavId = draft.MetricNavId;
                    target.MetricProfiles.Clear();
                    foreach (MetricProfile profile in draft.MetricProfiles)
                        target.MetricProfiles.Add(profile.Clone());
                }, out erro);
                if (!salvo) { _dirty = true; FalhaAoGravar(erro); }
                return salvo;
            }
            catch (Exception ex) { Log.Error("gravar cartoes de metricas", ex); return false; }
        }

        // ---------------- arrastar cartoes ----------------

        private MetricCard _arrastado;
        private int _origemDoArraste = -1;
        private int _destinoDoArraste = -1;
        private Point _pegadaNoCartao;

        /// <summary>
        /// Segue o cartao arrastado ate a soltura.
        ///
        /// O cartao sai do fluxo e passa a acompanhar o ponteiro; a grade se
        /// reorganiza ao vivo em volta dele, mostrando onde ele vai cair. Um
        /// indicador fino no lugar de destino resolveria menos: a pergunta que se
        /// faz arrastando nao e "onde ele entra", e sim "como a grade fica" - e
        /// isso so se responde mostrando a grade como ficaria.
        ///
        /// Capture no proprio formulario, e nao no cartao: solto sobre outro
        /// controle, o cartao pararia de receber o movimento no meio do gesto.
        /// </summary>
        private void ComecarArrasteDeCartao(MetricCard c)
        {
            int i = _cards.IndexOf(c);
            if (i < 0) return;

            _arrastado = c;
            _origemDoArraste = i;
            _destinoDoArraste = i;
            _pegadaNoCartao = c.Pegada;

            c.BringToFront();
            Cursor = Cursors.SizeAll;
            Capture = true;

            _pgMetricas.MouseMove += new MouseEventHandler(OnArrastando);
            _pgMetricas.MouseUp += new MouseEventHandler(OnSoltouCartao);
            MouseMove += new MouseEventHandler(OnArrastando);
            MouseUp += new MouseEventHandler(OnSoltouCartao);
        }

        private void OnArrastando(object sender, MouseEventArgs e)
        {
            if (_arrastado == null) return;

            Point p = _pgMetricas.PointToClient(Cursor.Position);
            _arrastado.Location = new Point(p.X - _pegadaNoCartao.X, p.Y - _pegadaNoCartao.Y);

            int alvo = IndiceSob(p);
            if (alvo >= 0 && alvo != _destinoDoArraste)
            {
                _destinoDoArraste = alvo;
                ReordenarPara(alvo);
            }
        }

        /// <summary>
        /// Qual posicao da grade esta sob o ponteiro.
        ///
        /// Mede pelo CENTRO de cada cartao, e nao pela area dele: com areas, o
        /// cartao arrastado - que esta por cima e no lugar do ponteiro - seria
        /// sempre o alvo de si mesmo, e nada trocaria de lugar.
        /// </summary>
        private int IndiceSob(Point p)
        {
            int melhor = -1;
            long menor = long.MaxValue;

            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] == _arrastado) continue;
                Rectangle r = _cards[i].Bounds;
                long dx = p.X - (r.Left + r.Width / 2);
                long dy = p.Y - (r.Top + r.Height / 2);
                long d = dx * dx + dy * dy;
                if (d < menor) { menor = d; melhor = i; }
            }

            if (melhor < 0) return -1;

            // So troca quando o ponteiro passa do centro do vizinho, senao a
            // grade ficaria oscilando entre duas ordens a cada pixel.
            Rectangle rv = _cards[melhor].Bounds;
            if (!rv.Contains(p)) return -1;
            return melhor;
        }

        /// <summary>Move o cartao arrastado para a posicao alvo e rearranja.</summary>
        private void ReordenarPara(int destino)
        {
            int atual = _cards.IndexOf(_arrastado);
            if (atual < 0 || destino < 0 || destino >= _cards.Count || destino == atual) return;

            MetricCard c = _cards[atual];
            string id = _cardIds[atual];
            int tam = _cardTamanhos[atual];

            _cards.RemoveAt(atual); _cardIds.RemoveAt(atual); _cardTamanhos.RemoveAt(atual);
            _cards.Insert(destino, c); _cardIds.Insert(destino, id); _cardTamanhos.Insert(destino, tam);

            ArranjarMetricas();
            _arrastado.BringToFront();
        }

        private void OnSoltouCartao(object sender, MouseEventArgs e)
        {
            if (_arrastado == null) return;

            _pgMetricas.MouseMove -= new MouseEventHandler(OnArrastando);
            _pgMetricas.MouseUp -= new MouseEventHandler(OnSoltouCartao);
            MouseMove -= new MouseEventHandler(OnArrastando);
            MouseUp -= new MouseEventHandler(OnSoltouCartao);

            Capture = false;
            Cursor = Cursors.Default;

            bool mudou = _destinoDoArraste != _origemDoArraste;
            _arrastado = null;
            _origemDoArraste = -1;
            _destinoDoArraste = -1;

            ArranjarMetricas();

            // Grava so quando a ordem mudou de fato: um arraste que voltou ao
            // lugar nao e uma edicao, e marcar a configuracao como suja obrigaria
            // a pessoa a salvar algo que ela nao fez.
            if (!mudou) return;

            _cfg.MetricIds.Clear();
            _cfg.MetricSizes.Clear();
            for (int i = 0; i < _cardIds.Count; i++)
            {
                _cfg.MetricIds.Add(_cardIds[i]);
                _cfg.MetricSizes.Add(_cardTamanhos[i]);
            }
            GravarMetricas();
        }

        /// <summary>
        /// (Re)monta a grade a partir da lista gravada.
        ///
        /// Remontar zera o historico dos cartoes, entao so acontece quando a
        /// composicao muda - adicionar, remover, mover. O ciclo de um segundo
        /// nunca passa por aqui: ele so empurra valores.
        /// </summary>
        private void MontarMetricas()
        {
            _pgMetricas.SuspendLayout();
            List<Control> velhos = new List<Control>();
            foreach (Control c in _pgMetricas.Controls) if (!(c is ScrollFina)) velhos.Add(c);
            foreach (Control c in velhos) { _pgMetricas.Controls.Remove(c); c.Dispose(); }
            _cards.Clear();
            _cardIds.Clear();
            _cardTamanhos.Clear();
            _lbSemMetricas = null;

            _btAddMetrica = new FlatBtn();
            _btAddMetrica.Text = T.AddMetric;
            _btAddMetrica.Primary = true;
            _btAddMetrica.Click += delegate { AdicionarMetrica(); };
            _pgMetricas.Controls.Add(_btAddMetrica);

            _btPadraoMetrica = new FlatBtn();
            _btPadraoMetrica.Text = T.DefaultMetrics;
            _btPadraoMetrica.Click += delegate { MenuDeConjuntos(); };
            _pgMetricas.Controls.Add(_btPadraoMetrica);

            _btPerfisMetrica = new FlatBtn();
            _btPerfisMetrica.Text = T.MetricProfiles;
            _btPerfisMetrica.Click += delegate { MenuDePerfisDeMetricas(); };
            _pgMetricas.Controls.Add(_btPerfisMetrica);

            // Janela de tempo dos graficos. Vale para a grade inteira: comparar
            // dois cartoes em escalas de tempo diferentes nao diz nada, e e
            // exatamente o que uma escolha por cartao permitiria fazer sem
            // perceber.
            _navJanela = new NavegadorDeJanela();
            _navJanela.Janela = MetricHistory.JanelaValida(_cfg.MetricRange);
            _navJanela.FimBalde = _navFim;
            _navJanela.SensorId = LeituraDoNavegador();
            _navJanela.Mudou += delegate { TrocarJanela(); };
            _navJanela.Confirmado += delegate { GravarJanela(); };
            _pgMetricas.Controls.Add(_navJanela);

            _lbDicaMetricas = MakeLabel(T.MetricsHint, 0, 0, Ui.FontSmall);
            _lbDicaMetricas.ForeColor = Ui.Faint;
            _pgMetricas.Controls.Add(_lbDicaMetricas);

            if (_cfg.MetricIds.Count == 0)
            {
                _lbSemMetricas = MakeLabel(T.NoMetrics, 4, 52, Ui.FontBase);
                _lbSemMetricas.ForeColor = Ui.Muted;
                _pgMetricas.Controls.Add(_lbSemMetricas);
            }

            int janela = MetricHistory.JanelaValida(_cfg.MetricRange);
            for (int i = 0; i < _cfg.MetricIds.Count; i++)
            {
                string id = _cfg.MetricIds[i];
                SensorEntry s = Achar(id);
                MetricCard c = new MetricCard();
                // Um sensor ausente continua configurado e pode voltar a responder.
                // Inclui-lo na grade evita perde-lo ao persistir a ordem do arraste.
                c.SensorId = id;
                c.Titulo = s == null ? T.SensorUnavailable : MetricPicker.Rotulo(s);
                c.Sub = s == null ? id : MetricPicker.Rodape(s);
                c.Unidade = s == null ? "" : s.EffectiveUnit;
                c.Janela = janela;
                c.FimBalde = _navFim;
                float? at, pe;
                MetricPicker.Faixas(c.Unidade, c.SensorId, out at, out pe);
                c.Atencao = at; c.Perigo = pe;

                string alvo = id;
                c.Remover += delegate { MexerNaMetrica(alvo, 0); };
                c.MoverEsquerda += delegate { MexerNaMetrica(alvo, -1); };
                c.MoverDireita += delegate { MexerNaMetrica(alvo, +1); };
                c.TrocarTamanho += delegate { RedimensionarMetrica(alvo); };

                // Copia local: o delegate guarda a variavel do laco, e nao o
                // valor dela no instante em que foi escrito.
                MetricCard esteCartao = c;
                c.Arrastar += delegate { ComecarArrasteDeCartao(esteCartao); };
                c.Escolhido += delegate { EscolherLeituraDoNavegador(alvo); };
                c.Selecionado = id == _navJanela.SensorId;

                _pgMetricas.Controls.Add(c);
                _cards.Add(c);
                _cardIds.Add(id);
                _cardTamanhos.Add(_cfg.MetricSize(i));
            }

            // Quem alimenta a serie e a thread de leitura, que roda com a janela
            // fechada: e ela que precisa saber o que acompanhar.
            // A janela nao e dona das series de outros consumidores. A poda
            // fica com o runtime, ao fechar a sessao de edicao.
            MetricHistory.SeguirTambem(_cfg.MetricIds);

            _pgMetricas.ResumeLayout();
            ArranjarMetricas();
        }

        /// <summary>
        /// Posiciona a barra e a grade na largura que a pagina tem agora.
        ///
        /// Empacotamento por fluxo: cada cartao ocupa 1, 2 ou 4 colunas e entra
        /// na linha corrente se couber; senao a linha fecha e ele comeca a
        /// proxima. A altura da linha e a do cartao mais alto dela, que e o que
        /// evita sobreposicao ao misturar tamanhos.
        ///
        /// O numero de colunas cresce com a largura, em multiplos de quatro: sem
        /// isso, uma janela maximizada em 1920 px daria quatro cartoes de 460 px
        /// - um numero pequeno perdido em cada tapete de cor.
        /// </summary>
        private void ArranjarMetricas()
        {
            if (_pgMetricas == null || _arranjando) return;

            // Mesmo motivo do Elastico: escondida, ela ainda recebe Resize a cada
            // quadro da animacao da lateral, e reposicionar dezenas de cartoes
            // que ninguem ve custa o mesmo que reposicionar os que se ve.
            if (!_pgMetricas.Visible) return;
            _arranjando = true;
            _pgMetricas.SuspendLayout();
            try
            {
                int Esp = Ui.S(12);

                // A largura da barra de rolagem sai da conta SEMPRE, apareca ela
                // ou nao. Medir a area de cliente faria a grade encolher quando
                // a barra surge e alargar quando ela some, e cada troca dispara
                // um novo arranjo - dois estados que se chamam em circulo.
                int disp = _pgMetricas.Width - Pagina.LarguraDaBarra - Ui.S(4);
                if (disp < Ui.S(320)) disp = Ui.S(320);

                int porLinha = disp / Ui.S(200);
                if (porLinha < 4) porLinha = 4;
                if (porLinha > 12) porLinha = 12;
                porLinha -= porLinha % 4;

                // Mantem a distribuicao anterior quando cabe; reduz colunas
                // apenas quando os 140 px minimos ultrapassariam a pagina.
                porLinha = Math.Min(porLinha, Math.Max(1, (disp - Ui.S(4) + Esp) / (Ui.S(140) + Esp)));
                int larg = (disp - Ui.S(4) - (porLinha - 1) * Esp) / porLinha;
                int x = Ui.S(2), y = 0;
                foreach (Control button in new Control[] { _btAddMetrica, _btPadraoMetrica, _btPerfisMetrica })
                {
                    if (button == null) continue;
                    int width = Ui.S(150);
                    if (x + width > disp) { x = Ui.S(2); y += Ui.S(40); }
                    button.SetBounds(x, y, width, Ui.S(32));
                    x += width + Ui.S(6);
                }
                if (_lbDicaMetricas != null)
                {
                    int w = disp - x - Ui.S(6);
                    _lbDicaMetricas.Visible = w >= Ui.S(150);
                    _lbDicaMetricas.SetBounds(w >= Ui.S(150) ? x + Ui.S(6) : Ui.S(2), y + Ui.S(4), w >= Ui.S(150) ? w : 1, w >= Ui.S(150) ? Ui.S(30) : 1);
                }
                y += Ui.S(44);

                if (_navJanela != null)
                {
                    _navJanela.SetBounds(Ui.S(2), y, disp - Ui.S(4), Ui.S(72));
                    y += Ui.S(72) + Esp;
                }

                if (_lbSemMetricas != null) _lbSemMetricas.SetBounds(Ui.S(4), y + Ui.S(8), disp - Ui.S(8), Ui.S(40));

                int col = 0, alturaDaLinha = 0;
                for (int i = 0; i < _cards.Count; i++)
                {
                    int tam = i < _cardTamanhos.Count ? _cardTamanhos[i] : 0;
                    int cols = Math.Min(porLinha, MetricPicker.Colunas(tam));
                    int alt = MetricPicker.Altura(tam);

                    if (col + cols > porLinha && col > 0)
                    {
                        y += alturaDaLinha + Esp;
                        col = 0; alturaDaLinha = 0;
                    }

                    // O cartao na mao consome o lugar dele na conta, mas NAO e
                    // reposicionado: quem manda na posicao dele e o ponteiro.
                    // Sem esta excecao ele voltaria para a grade a cada
                    // movimento, e o arraste viraria um piscar.
                    if (_cards[i] != _arrastado)
                        _cards[i].SetBounds(Ui.S(2) + col * (larg + Esp), y,
                                            cols * larg + (cols - 1) * Esp, alt);
                    else
                        _cards[i].Size = new Size(cols * larg + (cols - 1) * Esp, alt);

                    col += cols;
                    if (alt > alturaDaLinha) alturaDaLinha = alt;
                }
            }
            finally
            {
                _pgMetricas.ResumeLayout();
                _arranjando = false;
            }

            _pgMetricas.Sincronizar();
        }

        /// <summary>
        /// Troca a janela de tempo sem remontar a grade.
        ///
        /// A serie nao esta nos cartoes, entao mudar a escala e so redesenhar -
        /// nada do que foi registrado se perde ao alternar entre dez minutos e
        /// seis horas.
        /// </summary>
        private void TrocarJanela()
        {
            if (_navJanela == null) return;
            _navFim = _navJanela.FimBalde;
            foreach (MetricCard c in _cards)
            {
                c.Janela = _navJanela.Janela;
                c.FimBalde = _navJanela.FimBalde;
                c.Invalidate();
            }
            // A Visao geral fica de fora de proposito: tem janela propria,
            // curta, para os picos aparecerem nos blocos estreitos.
        }

        /// <summary>
        /// A leitura que a faixa do navegador desenha: a ultima escolhida, se o
        /// cartao dela ainda existe, ou o primeiro da grade.
        ///
        /// Antes era sempre o primeiro, e clicar no grafico da GPU deixava a
        /// faixa mostrando a CPU: a forma das seis horas ali em cima nao era a
        /// do grafico que se estava olhando.
        /// </summary>
        private string LeituraDoNavegador()
        {
            if (_cfg.MetricIds.Count == 0) return "";
            if (!string.IsNullOrEmpty(_cfg.MetricNavId) && _cfg.MetricIds.Contains(_cfg.MetricNavId))
                return _cfg.MetricNavId;
            // o cartao escolhido foi removido: esquece, para nao voltar marcado
            // se um dia a mesma leitura for adicionada de novo
            _cfg.MetricNavId = "";
            return _cfg.MetricIds[0];
        }

        /// <summary>
        /// Clique num cartao: a faixa passa a desenhar a leitura dele, na cor
        /// dele, e o cartao fica marcado. O trecho escolhido nao muda.
        /// </summary>
        private void EscolherLeituraDoNavegador(string id)
        {
            if (_navJanela == null || string.IsNullOrEmpty(id)) return;
            foreach (MetricCard c in _cards) c.Selecionado = c.SensorId == id;
            if (_navJanela.SensorId != id)
            {
                _navJanela.SensorId = id;
                AtualizarCorDoNavegador();
                _navJanela.Invalidate();
            }
            if (_cfg.MetricNavId == id) return;
            _cfg.MetricNavId = id;
            GravarMetricas();
        }

        private void AtualizarCorDoNavegador()
        {
            if (_navJanela == null) return;
            Color? cor = null;
            foreach (MetricCard c in _cards)
                if (c.SensorId == _navJanela.SensorId) { cor = c.CorAtual; break; }
            _navJanela.CorDaLinha = cor;
        }

        /// <summary>
        /// Grava o tamanho escolhido ao soltar o arraste, e nao a cada pixel.
        /// A posicao no tempo nao e gravada: reabrir a janela volta ao vivo.
        /// </summary>
        private void GravarJanela()
        {
            if (_navJanela == null || _cfg.MetricRange == _navJanela.Janela) return;
            _cfg.MetricRange = _navJanela.Janela;
            GravarMetricas();
        }

        private void AdicionarMetrica()
        {
            using (SensorDialog d = new SensorDialog(Clone(_sensors), null, T.AddMetric))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string id = d.SelectedId;
                if (string.IsNullOrEmpty(id) || _cfg.MetricIds.Contains(id)) return;
                _cfg.MetricIds.Add(id);
                _cfg.MetricSizes.Add(0);
                GravarMetricas();
                MontarMetricas();
            }
        }

        /// <summary>Remove (passo 0) ou desloca o cartao uma posicao.</summary>
        private void MexerNaMetrica(string id, int passo)
        {
            int i = _cfg.MetricIds.IndexOf(id);
            if (i < 0) return;

            if (passo == 0)
            {
                _cfg.MetricIds.RemoveAt(i);
                if (i < _cfg.MetricSizes.Count) _cfg.MetricSizes.RemoveAt(i);
            }
            else
            {
                int j = i + passo;
                if (j < 0 || j >= _cfg.MetricIds.Count) return;
                Trocar(_cfg.MetricIds, i, j);
                if (j < _cfg.MetricSizes.Count) Trocar(_cfg.MetricSizes, i, j);
            }
            GravarMetricas();
            MontarMetricas();
        }

        private static void Trocar<T>(List<T> lista, int i, int j)
        {
            T t = lista[i]; lista[i] = lista[j]; lista[j] = t;
        }

        /// <summary>
        /// Percorre pequeno, medio e grande e volta ao pequeno.
        ///
        /// Um botao ciclico em vez de tres opcoes: sao tres estados e a ordem e
        /// obvia, entao um menu para escolher entre eles gastaria dois cliques
        /// para o que um resolve.
        /// </summary>
        private void RedimensionarMetrica(string id)
        {
            int i = _cfg.MetricIds.IndexOf(id);
            if (i < 0) return;
            while (_cfg.MetricSizes.Count <= i) _cfg.MetricSizes.Add(0);
            _cfg.MetricSizes[i] = (_cfg.MetricSizes[i] + 1) % 3;
            GravarMetricas();
            MontarMetricas();
        }

        private void AtualizarMetricas(Dictionary<string, float> snap)
        {
            if (snap == null) return;
            IReadingStatusData status = _data as IReadingStatusData;
            for (int i = 0; i < _cards.Count; i++)
            {
                string id = _cardIds[i];
                SensorEntry sensor = Achar(id);
                MetricCard card = _cards[i];
                string title = sensor == null ? T.SensorUnavailable : MetricPicker.Rotulo(sensor);
                string footer = sensor == null ? id : MetricPicker.Rodape(sensor);
                string unit = sensor == null ? "" : sensor.EffectiveUnit;
                if (id.StartsWith(Rtss.Prefixo, StringComparison.Ordinal)) footer = MetricPicker.RodapeJogos();
                if (status != null && sensor != null)
                {
                    ReadingState state = status.CurrentReadingState(id);
                    if (state != ReadingState.Live && !(state == ReadingState.Waiting &&
                        id.StartsWith(Rtss.Prefixo, StringComparison.Ordinal) && string.IsNullOrEmpty(Rtss.JogoAtual)))
                        footer = ReadingStateText(state);
                }
                if (card.Titulo != title || card.Sub != footer || card.Unidade != unit)
                {
                    card.Titulo = title; card.Sub = footer; card.Unidade = unit;
                    float? attention, danger; MetricPicker.Faixas(unit, id, out attention, out danger);
                    card.Atencao = attention; card.Perigo = danger; card.Invalidate();
                }
                float value;
                card.Push(snap.TryGetValue(id, out value) ? (float?)value : null);
            }
            if (_navJanela != null && _navJanela.Visible)
            {
                // a cor do cartao muda com o valor (atencao, perigo); a faixa acompanha
                AtualizarCorDoNavegador();
                _navJanela.Invalidate();
            }
        }

        private static string ReadingStateText(ReadingState state)
        {
            switch (state)
            {
                case ReadingState.Paused: return T.ReadingsPaused;
                case ReadingState.Failed: return T.ReadingsFailed;
                case ReadingState.Expired: return T.ReadingsExpired;
                case ReadingState.Waiting: return T.ReadingsWaiting;
                default: return "";
            }
        }
    }
}
