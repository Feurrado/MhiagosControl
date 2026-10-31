using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    public partial class SettingsForm : Form
    {
        /// <summary>
        /// Quanto dura a carga de cada periferico, quando foi a ultima e como a
        /// porcentagem andou. Os numeros vem de HistoricoDeBateria, que a thread
        /// de atualizacao alimenta mesmo com esta janela fechada.
        /// </summary>
        private Control BuildPageBaterias()
        {
            _pgBaterias = new Pagina();
            _pgBaterias.RolarNaVertical();

            _lbBateriasVazio = MakeLabel(T.BatNoDevices, 2, 4, Ui.FontBase);
            _lbBateriasVazio.ForeColor = Ui.Muted;
            _lbBateriasVazio.AutoSize = false;
            _lbBateriasVazio.Visible = false;
            _pgBaterias.Controls.Add(_lbBateriasVazio);

            _btGHub = new FlatBtn();
            _btGHub.Text = T.OpenGHub;
            _btGHub.Visible = false;
            _btGHub.Click += delegate { Perifericos.AbrirGHub(); };
            _pgBaterias.Controls.Add(_btGHub);

            // Os aparelhos vem primeiro. As opcoes do widget ficam atras de um
            // botao discreto no alto, fechado por padrao; se a pessoa deixar
            // aberto, ele volta aberto. Ligar e desligar segue tambem na bandeja.
            _widget = _data as IControleDoWidget;
            if (_widget != null)
            {
                _btWidget = new BotaoDoWidget();
                _btWidget.Text = T.WidgetButton;
                _btWidget.Aberto = DetalhesDaBateria.Aberto(ChaveDoPainelDoWidget);
                _btWidget.Click += delegate { AlternarPainelDoWidget(); };
                _dicaDoWidget.SetToolTip(_btWidget, T.WidgetButtonTip);
                _pgBaterias.Controls.Add(_btWidget);

                // O cartao mora num CollapsiblePanel: abrir e fechar desliza a
                // altura e os aparelhos de baixo acompanham, em vez de saltar.
                Card c = MontarCartaoDoWidget();
                _painelDoWidget = new CollapsiblePanel(_btWidget.Aberto);
                _painelDoWidget.AccessibleName = T.WidgetCard;
                _painelDoWidget.Conteudo = c;
                _painelDoWidget.AlturaAberta = AlturaDoCartaoDoWidget;
                _painelDoWidget.AlturaMudou += delegate { ArranjarBaterias(); };
                _pgBaterias.Controls.Add(_painelDoWidget);
            }
            return _pgBaterias;
        }

        private CollapsiblePanel _painelDoWidget;

        private BotaoDoWidget _btWidget;
        private readonly ToolTip _dicaDoWidget = new ToolTip();

        /// <summary>
        /// Painel aberto ou fechado, no mesmo arquivo do "Mais detalhes" de cada
        /// aparelho: abrir uma secao nao e mudanca de configuracao para pedir
        /// Salvar. As chaves dos aparelhos tem barra ("GHub|G325"), entao esta
        /// nao colide com nenhuma.
        /// </summary>
        internal const string ChaveDoPainelDoWidget = "painel-do-widget";

        private void AlternarPainelDoWidget()
        {
            if (_btWidget == null) return;
            DetalhesDaBateria.Alternar(ChaveDoPainelDoWidget);
            bool aberto = DetalhesDaBateria.Aberto(ChaveDoPainelDoWidget);
            // O botao muda de estado na hora; a seta dele gira com o painel,
            // pelo ArranjarBaterias de cada quadro.
            _btWidget.Aberto = aberto;
            if (aberto) SincronizarWidget();   // conteudo certo antes de aparecer
            if (_painelDoWidget != null) _painelDoWidget.Definir(aberto, true);
            ArranjarBaterias();
        }

        // ---------------- widget de bateria ----------------

        private IControleDoWidget _widget;
        private Card _cardWidget;
        private Toggle _tgWidget, _tgWidgetFrente, _tgWidgetTrava, _tgWidgetCasa;
        private Segmented _sgWidgetLayout, _sgWidgetVidro, _sgWidgetCor;
        /// <summary>Preenchendo os controles com a configuracao: as mudancas nao sao do usuario.</summary>
        private bool _sincronizandoWidget;
        private static readonly int AlturaDoCartaoDoWidget = Ui.S(330);
        private static readonly int AlturaDoBotaoDoWidget = Ui.S(30);

        /// <summary>
        /// As mesmas opcoes do botao direito do widget, num lugar que se acha
        /// sem saber que o menu existe. Cada mudanca vale na hora.
        /// </summary>
        private Card MontarCartaoDoWidget()
        {
            Card c = new Card();
            c.Title = T.WidgetCard;
            _cardWidget = c;

            _tgWidget = new Toggle();
            _tgWidget.Label = T.WidgetShow;
            _tgWidget.SetBounds(Ui.S(16), Ui.S(50), Ui.S(300), Ui.S(26));
            c.Controls.Add(_tgWidget);

            _tgWidgetCasa = new Toggle();
            _tgWidgetCasa.Label = T.CasaWidgetOpcao;
            _tgWidgetCasa.SetBounds(Ui.S(330), Ui.S(50), Ui.S(240), Ui.S(26));
            c.Controls.Add(_tgWidgetCasa);

            c.Controls.Add(MakeLabel(T.WidgetLayout, 16, 96, Ui.FontMed));
            _sgWidgetLayout = new Segmented();
            _sgWidgetLayout.SetItems(T.WidgetRings, T.WidgetList);
            _sgWidgetLayout.SetBounds(Ui.S(16), Ui.S(120), Ui.S(220), Ui.S(30));
            c.Controls.Add(_sgWidgetLayout);

            c.Controls.Add(MakeLabel(T.WidgetGlass, 260, 96, Ui.FontMed));
            _sgWidgetVidro = new Segmented();
            _sgWidgetVidro.SetItems(T.WidgetSharpShort, T.WidgetClear, T.WidgetFrosted);
            _sgWidgetVidro.SetBounds(Ui.S(260), Ui.S(120), Ui.S(300), Ui.S(30));
            c.Controls.Add(_sgWidgetVidro);

            c.Controls.Add(MakeLabel(T.WidgetTint, 16, 166, Ui.FontMed));
            _sgWidgetCor = new Segmented();
            _sgWidgetCor.SetItems(T.WidgetTintAutoShort, T.WidgetTintDark, T.WidgetTintLight);
            _sgWidgetCor.SetBounds(Ui.S(16), Ui.S(190), Ui.S(450), Ui.S(30));
            c.Controls.Add(_sgWidgetCor);

            _tgWidgetFrente = new Toggle();
            _tgWidgetFrente.Label = T.WidgetOnTop;
            _tgWidgetFrente.SetBounds(Ui.S(16), Ui.S(238), Ui.S(300), Ui.S(26));
            c.Controls.Add(_tgWidgetFrente);

            _tgWidgetTrava = new Toggle();
            _tgWidgetTrava.Label = T.WidgetLock;
            _tgWidgetTrava.SetBounds(Ui.S(330), Ui.S(238), Ui.S(240), Ui.S(26));
            c.Controls.Add(_tgWidgetTrava);

            FlatBtn canto = new FlatBtn();
            canto.Text = T.WidgetBackToCorner;
            canto.SetBounds(Ui.S(16), Ui.S(280), Ui.S(220), Ui.S(32));
            canto.Click += delegate { _widget.WidgetAoCanto(); };
            c.Controls.Add(canto);

            Label nota = MakeLabel(T.WidgetCardNote, 250, 280, Ui.FontSmall);
            nota.AutoSize = false;
            nota.Size = new Size(Ui.S(480), Ui.S(34));
            nota.TextAlign = ContentAlignment.MiddleLeft;
            nota.ForeColor = Ui.Muted;
            c.Controls.Add(nota);

            // janela estreita: o que fica a direita encolhe em vez de passar da borda
            c.Resize += delegate
            {
                int livre = c.Width - Ui.S(16);
                _sgWidgetVidro.Width = Math.Max(Ui.S(180), Math.Min(Ui.S(300), livre - Ui.S(260)));
                _tgWidgetTrava.Width = Math.Max(Ui.S(140), Math.Min(Ui.S(240), livre - Ui.S(330)));
                _tgWidgetCasa.Width = _tgWidgetTrava.Width;
                nota.Width = Math.Max(Ui.S(120), Math.Min(Ui.S(480), livre - Ui.S(250)));
            };

            // preenche antes de ligar os eventos: cada controle avisa toda mudanca
            SincronizarWidget();

            _tgWidget.CheckedChanged += delegate { if (!_sincronizandoWidget) _widget.LigarWidget(_tgWidget.Checked); };
            _sgWidgetLayout.SelectedIndexChanged += delegate
            {
                MudarWidget(delegate(ConfigDoWidget w)
                {
                    w.Layout = _sgWidgetLayout.SelectedIndex == 1 ? LayoutDoWidget.Lista : LayoutDoWidget.Aneis;
                });
            };
            _sgWidgetVidro.SelectedIndexChanged += delegate
            {
                MudarWidget(delegate(ConfigDoWidget w)
                {
                    w.Nitido = _sgWidgetVidro.SelectedIndex == 0;
                    w.Fosco = _sgWidgetVidro.SelectedIndex == 2;
                });
            };
            _sgWidgetCor.SelectedIndexChanged += delegate
            {
                MudarWidget(delegate(ConfigDoWidget w)
                {
                    int i = _sgWidgetCor.SelectedIndex;
                    w.Cor = i == 1 ? CorDoVidro.Escura : i == 2 ? CorDoVidro.Clara : CorDoVidro.Automatica;
                });
            };
            _tgWidgetFrente.CheckedChanged += delegate { MudarWidget(delegate(ConfigDoWidget w) { w.NaFrente = _tgWidgetFrente.Checked; }); };
            _tgWidgetTrava.CheckedChanged += delegate { MudarWidget(delegate(ConfigDoWidget w) { w.Travado = _tgWidgetTrava.Checked; }); };
            _tgWidgetCasa.CheckedChanged += delegate { if (!_sincronizandoWidget && _tgWidgetCasa.Checked != CasaNoWidget.Ativo) CasaNoWidget.Definir(_tgWidgetCasa.Checked); };
            EventHandler casaMudou = delegate { if (!c.IsDisposed) SincronizarWidget(); };
            CasaNoWidget.Mudou += casaMudou;
            c.Disposed += delegate { CasaNoWidget.Mudou -= casaMudou; };
            return c;
        }

        private void MudarWidget(Action<ConfigDoWidget> mudanca)
        {
            if (_sincronizandoWidget || _widget == null) return;
            mudanca(_widget.ConfigDoWidget);
            _widget.WidgetMudou();
        }

        /// <summary>
        /// Traz para os controles o que esta valendo: o menu do proprio widget
        /// e o da bandeja tambem mudam a configuracao.
        /// </summary>
        private void SincronizarWidget()
        {
            if (_widget == null || _cardWidget == null) return;
            ConfigDoWidget w = _widget.ConfigDoWidget;
            _sincronizandoWidget = true;
            try
            {
                _tgWidget.Checked = w.Ativo;
                _sgWidgetLayout.SelectedIndex = w.Layout == LayoutDoWidget.Lista ? 1 : 0;
                _sgWidgetVidro.SelectedIndex = w.Nitido ? 0 : w.Fosco ? 2 : 1;
                _sgWidgetCor.SelectedIndex = w.Cor == CorDoVidro.Escura ? 1 : w.Cor == CorDoVidro.Clara ? 2 : 0;
                _tgWidgetFrente.Checked = w.NaFrente;
                _tgWidgetTrava.Checked = w.Travado;
                _tgWidgetCasa.Checked = w.Casa;
            }
            finally { _sincronizandoWidget = false; }
        }

        private void AtualizarBaterias()
        {
            if (_pgBaterias == null) return;
            // o tick e de 250 ms; a bateria anda em minutos
            if ((DateTime.Now - _bateriasLidas).TotalMilliseconds < 1000) return;
            _bateriasLidas = DateTime.Now;

            Periferico[] vivos = Perifericos.Atual;
            List<AnaliseDeBateria> analises = HistoricoDeBateria.Analisar(vivos);

            // Os cartoes sao reaproveitados por chave: quem chega entra com fade e
            // expansao, quem some sai do mesmo jeito e so e removido ao fim.
            bool animar = _bateriasPopuladas && _pgBaterias.Visible;
            bool mudouLista = false;
            HashSet<string> vivas = new HashSet<string>();
            foreach (AnaliseDeBateria a in analises) vivas.Add(a.Chave);

            foreach (CartaoDeBateria c in _cartoesDeBateria)
                if (!c.Saindo && !vivas.Contains(c.Chave)) { c.Sair(); mudouLista = true; }

            List<CartaoDeBateria> nova = new List<CartaoDeBateria>();
            foreach (AnaliseDeBateria a in analises)
            {
                CartaoDeBateria achado = null;
                foreach (CartaoDeBateria c in _cartoesDeBateria) if (c.Chave == a.Chave) achado = c;
                if (achado == null)
                {
                    achado = new CartaoDeBateria();
                    achado.AlturaMudou += delegate { ArranjarBaterias(); };
                    CartaoDeBateria novo = achado;
                    achado.Saiu += delegate { RemoverCartaoDeBateria(novo); };
                    int? aut = null;
                    foreach (Periferico p in vivos) if (HistoricoDeBateria.Chave(p) == a.Chave) aut = p.Autonomia;
                    achado.Mostrar(a, aut);
                    _pgBaterias.Controls.Add(achado);
                    if (animar) achado.NascerEntrando();
                    mudouLista = true;
                }
                else if (achado.Saindo) { achado.Entrar(); mudouLista = true; }
                nova.Add(achado);
            }

            // quem saiu da leitura fica logo depois do cartao que vinha antes dele
            int ultimo = -1;
            foreach (CartaoDeBateria c in _cartoesDeBateria)
            {
                if (nova.Contains(c)) { ultimo = nova.IndexOf(c); continue; }
                nova.Insert(ultimo + 1, c);
                ultimo++;
            }
            for (int i = 0; !mudouLista && i < nova.Count; i++) mudouLista = !ReferenceEquals(nova[i], _cartoesDeBateria.Count > i ? _cartoesDeBateria[i] : null);
            _cartoesDeBateria.Clear();
            _cartoesDeBateria.AddRange(nova);

            foreach (CartaoDeBateria c in _cartoesDeBateria)
            {
                if (c.Saindo) continue;
                int? autonomia = null;
                foreach (Periferico p in vivos)
                    if (HistoricoDeBateria.Chave(p) == c.Chave) autonomia = p.Autonomia;
                foreach (AnaliseDeBateria a in analises)
                    if (a.Chave == c.Chave) c.Mostrar(a, autonomia);
            }
            _bateriasPopuladas = true;

            if (mudouLista) ArranjarBaterias();
            // o menu do widget e o da bandeja mudam a configuracao sem passar
            // por aqui: o cartao confere a cada leitura
            SincronizarWidget();
        }

        /// <summary>A primeira carga da pagina nao anima: os cartoes ja chegam inteiros.</summary>
        private bool _bateriasPopuladas;

        private void RemoverCartaoDeBateria(CartaoDeBateria c)
        {
            // fora do tick do relogio que acabou de terminar
            BeginInvoke(new Action(delegate
            {
                if (c.IsDisposed || !c.Saindo || !_cartoesDeBateria.Contains(c)) return;
                _pgBaterias.Controls.Remove(c);
                _cartoesDeBateria.Remove(c);
                c.Dispose();
                ArranjarBaterias();
            }));
        }

        private void ArranjarBaterias()
        {
            if (_pgBaterias == null) return;
            int Esp = Ui.S(12);

            int disp = _pgBaterias.Width - Ui.S(4) - Pagina.LarguraDaBarra;
            int larg = disp > LarguraMaxDaPagina ? LarguraMaxDaPagina : disp;
            if (larg < Ui.S(460)) larg = Ui.S(460);
            int x = Ui.S(2) + Math.Max(0, (disp - larg) / 2);

            // Um rearranjo por quadro de animacao: os filhos andam juntos e a
            // pagina so refaz o layout uma vez, no ResumeLayout.
            _pgBaterias.SuspendLayout();
            try
            {
                int y = Ui.S(2);
                if (_btWidget != null)
                {
                    int lb = _btWidget.LarguraDesejada();
                    _btWidget.SetBounds(x + larg - lb, y, lb, AlturaDoBotaoDoWidget);
                    y += AlturaDoBotaoDoWidget + Ui.S(8);
                }
                if (_painelDoWidget != null)
                {
                    // A cada quadro da animacao: a casca na altura do quadro, a
                    // seta do botao no mesmo ponto da curva e o vao abaixo
                    // crescendo junto, para os aparelhos descerem sem degrau.
                    if (_painelDoWidget.Expandido && !_painelDoWidget.Animando) SincronizarWidget();
                    int h = _painelDoWidget.AlturaAtual;
                    _painelDoWidget.SetBounds(x, y, larg, h);
                    if (_btWidget != null) _btWidget.Giro = _painelDoWidget.Progresso;
                    if (h > 0) y += h + (int)Math.Round(Esp * _painelDoWidget.Progresso);
                }

                _lbBateriasVazio.Visible = _cartoesDeBateria.Count == 0;
                _lbBateriasVazio.SetBounds(x, y + Ui.S(2), larg, Ui.S(40));
                if (_temGHub == null) _temGHub = Perifericos.GHubInstalado();
                _btGHub.Visible = _lbBateriasVazio.Visible && _temGHub.Value;
                _btGHub.SetBounds(x, y + Ui.S(46), Ui.S(180), Ui.S(32));

                foreach (CartaoDeBateria c in _cartoesDeBateria)
                {
                    int alt = c.AlturaDesejada(larg);
                    c.SetBounds(x, y, larg, alt);
                    y += alt + (int)Math.Round(Esp * c.Presenca);
                }
            }
            finally { _pgBaterias.ResumeLayout(); }
            _pgBaterias.Sincronizar();
        }

        // ---------------- pagina: Especificacoes ----------------

        private Pagina _pgSpecs;
        private Label _lbSpecsEstado, _lbSpecsNota;
        private FlatBtn _btCopiarSpecs;
        private bool _coletando = false;
        private readonly List<FlatBtn> _specCategoryButtons = new List<FlatBtn>();
        private readonly Dictionary<Card, int> _specFullHeights = new Dictionary<Card, int>();
        private int _specCategory = 0; // zero = resumo; os demais seguem _specCards
    }
}
