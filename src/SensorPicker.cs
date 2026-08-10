using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>Campo de busca com borda arredondada e lupa.</summary>
    public class SearchBox : Control
    {
        private readonly TextBox _box;
        public event EventHandler QueryChanged;

        public string Query { get { return _box.Text; } }
        public string Placeholder = "Buscar sensor...";

        public SearchBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);

            // a caixa de texto precisa existir ANTES de qualquer atribuicao que
            // dispare OnResize - Height abaixo e uma delas
            _box = new TextBox();
            _box.BorderStyle = BorderStyle.None;
            _box.Font = Ui.FontBase;
            _box.TextChanged += delegate { Invalidate(); if (QueryChanged != null) QueryChanged(this, EventArgs.Empty); };
            Controls.Add(_box);

            Height = 30;
            BackColor = Color.Transparent;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_box == null) return;   // resize pode ocorrer durante a construcao
            _box.SetBounds(30, (Height - _box.PreferredHeight) / 2 + 1, Math.Max(10, Width - 40), _box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            _box.BackColor = Ui.SurfaceAlt;
            _box.ForeColor = Ui.Text;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(r, Height / 2))
            using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt))
            using (Pen pen = new Pen(Ui.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            using (Font f = new Font("Segoe MDL2 Assets", 9f))
            using (SolidBrush b = new SolidBrush(Ui.Muted))
                g.DrawString("", f, b, 9, (Height - 16) / 2);

            if (_box.Text.Length == 0)
                TextRenderer.DrawText(g, Placeholder, Ui.FontBase,
                    new Rectangle(30, 0, Width - 40, Height), Ui.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>
    /// Fila de pilulas de filtro. Selecionar uma categoria reduz a lista ao
    /// hardware correspondente, o que dispensa os cabecalhos de grupo e corta
    /// a rolagem.
    /// </summary>
    public class ChipBar : Control
    {
        private readonly List<string> _items = new List<string>();
        private readonly List<Rectangle> _rects = new List<Rectangle>();
        private int _sel = 0, _hover = -1;

        public event EventHandler SelectionChanged;

        public ChipBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Ui.FontSmall;
            Height = 30;
            Cursor = Cursors.Hand;
        }

        public string SelectedItem { get { return _sel >= 0 && _sel < _items.Count ? _items[_sel] : null; } }

        public void SetItems(IEnumerable<string> items)
        {
            _items.Clear();
            _items.AddRange(items);
            _sel = 0;
            Layout2();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Layout2(); }

        // Folga apertada de proposito: com oito categorias, cada pixel a mais de
        // recheio empurra a ultima pilula para uma segunda fileira que rouba
        // 28 px da lista.
        private const int ChipH = 24, ChipGap = 5, ChipPad = 18;
        private const int TopPad = 6, BottomPad = 10;

        /// <summary>
        /// Posiciona as pilulas quebrando linha quando nao couberem. Sem quebra,
        /// as ultimas ficavam fora da vista.
        /// </summary>
        private void Layout2()
        {
            _rects.Clear();
            int x = 0, y = TopPad;
            for (int i = 0; i < _items.Count; i++)
            {
                int w = TextRenderer.MeasureText(_items[i], Font).Width + ChipPad;
                if (x > 0 && x + w > Width) { x = 0; y += ChipH + ChipGap; }
                _rects.Add(new Rectangle(x, y, w, ChipH));
                x += w + ChipGap;
            }

            int h = y + ChipH + BottomPad;
            if (Height != h) Height = h;   // Dock.Top redistribui o resto sozinho
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h >= 0 && h != _sel)
            {
                _sel = h; Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        private int Hit(Point p)
        {
            for (int i = 0; i < _rects.Count; i++) if (_rects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);

            for (int i = 0; i < _rects.Count && i < _items.Count; i++)
            {
                Rectangle r = _rects[i];

                bool sel = i == _sel;
                using (GraphicsPath p = Ui.RoundRect(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), r.Height / 2))
                using (SolidBrush b = new SolidBrush(sel ? Ui.Accent : (i == _hover ? Ui.Hover : Ui.SurfaceAlt)))
                using (Pen pen = new Pen(sel ? Ui.Accent : Ui.Border))
                {
                    g.FillPath(b, p);
                    g.DrawPath(pen, p);
                }
                TextRenderer.DrawText(g, _items[i], Font, r, sel ? Color.White : Ui.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    /// <summary>
    /// ListBox sem cintilacao.
    ///
    /// Um ListBox desenhado por nos pisca a cada repintura porque o controle
    /// nativo apaga o fundo antes. Descartamos WM_ERASEBKGND e ligamos o
    /// buffer duplo - sem isso, atualizar os valores a cada segundo fazia a
    /// lista inteira tremer.
    /// </summary>
    internal class SmoothListBox : ListBox
    {
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_VSCROLL    = 0x0115;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_KEYDOWN    = 0x0100;
        private const int SB_VERT       = 1;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        /// <summary>Disparado quando a posicao da rolagem pode ter mudado.</summary>
        public event EventHandler Scrolled;

        public SmoothListBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Quantas linhas inteiras cabem na altura visivel.</summary>
        private int PorTela
        {
            get { return Math.Max(1, ClientSize.Height / Math.Max(1, ItemHeight)); }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ERASEBKGND) { m.Result = (IntPtr)1; return; }

            // A barra nativa e escondida antes do calculo da area nao-cliente:
            // e o unico ponto em que a decisao pega, porque o ListBox a
            // reexibe sozinho sempre que o conteudo muda. Quem desenha a
            // rolagem e o ScrollStrip, com a cara do resto do aplicativo.
            if (m.Msg == WM_NCCALCSIZE && IsHandleCreated) ShowScrollBar(Handle, SB_VERT, false);

            // A roda passa a ser nossa. O ListBox so rola com a roda quando a
            // barra nativa esta visivel - escondendo-a, ele engole o
            // WM_MOUSEWHEEL sem fazer nada. Rolamos pelo TopIndex, que nao
            // depende da barra.
            if (m.Msg == WM_MOUSEWHEEL)
            {
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                int linhas = SystemInformation.MouseWheelScrollLines;
                if (linhas <= 0) linhas = 3;          // "uma tela por entalhe" vira -1
                if (linhas > 100) linhas = PorTela;

                int max = Math.Max(0, Items.Count - PorTela);
                int alvo = TopIndex + (delta > 0 ? -linhas : linhas);
                int novo = Math.Max(0, Math.Min(max, alvo));
                if (novo != TopIndex) TopIndex = novo;

                m.Result = IntPtr.Zero;
                if (Scrolled != null) Scrolled(this, EventArgs.Empty);
                return;
            }

            base.WndProc(ref m);

            if ((m.Msg == WM_VSCROLL || m.Msg == WM_KEYDOWN) && Scrolled != null)
                Scrolled(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Barra de rolagem desenhada pelo kit, para acompanhar uma lista.
    ///
    /// A nativa entra como um risco claro sobre o cartao escuro mesmo com o
    /// tema DarkMode_Explorer aplicado, e nao ha como mudar sua espessura nem
    /// seu raio. Como tudo em volta e desenhado, ela era a unica peca fora do
    /// conjunto.
    /// </summary>
    internal class ScrollStrip : Control
    {
        private readonly ListBox _list;
        private bool _arrastando;
        private int _pegaEm;          // deslocamento do clique dentro do polegar
        private bool _hover;

        public ScrollStrip(ListBox list)
        {
            _list = list;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Width = 12;
            Cursor = Cursors.Default;
        }

        private int PorTela
        {
            get { return Math.Max(1, _list.ClientSize.Height / Math.Max(1, _list.ItemHeight)); }
        }

        private int Total { get { return _list.Items.Count; } }

        /// <summary>Some quando tudo cabe: barra de rolagem inutil e ruido.</summary>
        public bool Necessaria { get { return Total > PorTela; } }

        private Rectangle Polegar()
        {
            int total = Total, tela = PorTela;
            if (total <= tela) return Rectangle.Empty;

            int trilho = Height - 8;
            int alt = Math.Max(28, trilho * tela / total);
            int max = total - tela;
            int topo = max <= 0 ? 0 : (trilho - alt) * Math.Min(_list.TopIndex, max) / max;
            return new Rectangle(3, 4 + topo, Width - 6, alt);
        }

        public void Sincronizar()
        {
            bool v = Necessaria;
            if (Visible != v) Visible = v;
            if (v) Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Rectangle t = Polegar();
            if (t.IsEmpty) return;

            if (t.Contains(e.Location)) { _arrastando = true; _pegaEm = e.Y - t.Y; }
            else
            {
                // clique no trilho: leva o polegar para o ponto
                _arrastando = true;
                _pegaEm = t.Height / 2;
                MoverPara(e.Y);
            }
            Capture = true;
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = Polegar().Contains(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
            if (_arrastando) MoverPara(e.Y);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _arrastando = false; Capture = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int max = Math.Max(0, Total - PorTela);
            _list.TopIndex = Math.Max(0, Math.Min(max, _list.TopIndex - Math.Sign(e.Delta) * 3));
            Invalidate();
            base.OnMouseWheel(e);
        }

        private void MoverPara(int y)
        {
            int total = Total, tela = PorTela;
            if (total <= tela) return;

            Rectangle t = Polegar();
            int trilho = Height - 8;
            int curso = trilho - t.Height;
            if (curso <= 0) return;

            int topo = Math.Max(0, Math.Min(curso, y - _pegaEm - 4));
            _list.TopIndex = topo * (total - tela) / curso;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle t = Polegar();
            if (t.IsEmpty) return;

            Graphics g = e.Graphics;
            Ui.Smooth(g);

            using (GraphicsPath p = Ui.RoundRect(new Rectangle(4, 4, Width - 8, Height - 8), (Width - 8) / 2))
            using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt))
                g.FillPath(b, p);

            using (GraphicsPath p = Ui.RoundRect(t, t.Width / 2))
            using (SolidBrush b = new SolidBrush(_hover || _arrastando ? Ui.Muted : Ui.Thumb))
                g.FillPath(b, p);
        }
    }

    /// <summary>Cabecalho de grupo dentro da lista.</summary>
    internal class GroupRow
    {
        public string Name;
        public GroupRow(string n) { Name = n; }
    }

    /// <summary>
    /// Selecao de sensor: busca + lista agrupada por hardware com o valor
    /// atual de cada entrada.
    ///
    /// A lista e desenhada manualmente sobre um ListBox: ganhamos rolagem,
    /// teclado e selecao prontos, e controlamos cores, realce e os cabecalhos
    /// de grupo, que o ListView tema-do-sistema nao permite estilizar.
    /// </summary>
    public class SensorPicker : Panel
    {
        private readonly SearchBox _search;
        private readonly ListBox _list;
        private readonly ChipBar _chips;
        private readonly Panel _spacer;
        private readonly ScrollStrip _scroll;
        private List<SensorEntry> _all = new List<SensorEntry>();
        private readonly Dictionary<string, SensorEntry> _byId = new Dictionary<string, SensorEntry>();
        private string _selectedId = "";
        private int _hover = -1;
        private bool _building = false;
        private int _padBase = -1;
        private bool _ajustando = false;

        public event EventHandler SelectionChanged;

        /// <summary>Duplo clique num sensor - o dialogo usa para confirmar e fechar.</summary>
        public event EventHandler ItemActivated;

        /// <summary>Altura da linha.</summary>
        public int RowHeight
        {
            get { return _list.ItemHeight; }
            set { _list.ItemHeight = value; PerformLayout(); }
        }

        /// <summary>Mostra a fila de pilulas de categoria acima da lista.</summary>
        public bool Categories
        {
            get { return _chips.Visible; }
            set { _chips.Visible = value; }
        }

        public SensorPicker()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            _search = new SearchBox();
            _search.Dock = DockStyle.Top;
            _search.QueryChanged += delegate { Rebuild(); };

            _chips = new ChipBar();
            _chips.Dock = DockStyle.Top;
            _chips.Visible = false;
            _chips.SelectionChanged += delegate { Rebuild(); };

            _list = new SmoothListBox();
            _list.Dock = DockStyle.Fill;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = 26;
            _list.BorderStyle = BorderStyle.None;
            _list.IntegralHeight = false;
            _list.Font = Ui.FontBase;
            // No construtor, e nao so em OnPaint: coberto pela lista, o OnPaint
            // do painel pode nunca rodar, e a faixa abaixo do ultimo item ficava
            // com a cor de sistema - a tarja escura no pe da lista.
            _list.BackColor = Ui.Surface;
            _list.ForeColor = Ui.Text;
            _list.DrawItem += new DrawItemEventHandler(OnDrawItem);
            _list.SelectedIndexChanged += new EventHandler(OnSelect);
            _list.MouseMove += new MouseEventHandler(OnMouseMoveList);
            _list.MouseLeave += delegate { _hover = -1; _list.Invalidate(); };
            _list.DoubleClick += delegate
            {
                if (Selected != null && ItemActivated != null) ItemActivated(this, EventArgs.Empty);
            };

            _spacer = new Panel();
            _spacer.Dock = DockStyle.Top;
            _spacer.Height = 8;
            _spacer.BackColor = Color.Transparent;

            _scroll = new ScrollStrip(_list);
            _scroll.Dock = DockStyle.Right;
            ((SmoothListBox)_list).Scrolled += delegate { _scroll.Sincronizar(); };

            // A ordem de insercao define o empilhamento do Dock: quem entra
            // depois se acomoda primeiro. Busca e pilulas tomam a largura
            // inteira, a barra toma a direita do que sobrou, a lista preenche.
            Controls.Add(_list);
            Controls.Add(_scroll);
            Controls.Add(_spacer);
            Controls.Add(_chips);
            Controls.Add(_search);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            AjustarSobra();
        }

        /// <summary>
        /// Tira a sobra do pe da lista.
        ///
        /// Com IntegralHeight desligado - e precisa ficar desligado, senao o
        /// ListBox encolhe sozinho e briga com o Dock - a altura raramente e
        /// multipla da linha, e o que sobra vira uma ultima linha cortada ao
        /// meio. Parece haver item escondido onde nao ha, e a barra de rolagem
        /// desmente. Empurramos a sobra para o recheio de baixo do painel.
        /// </summary>
        private void AjustarSobra()
        {
            if (_list == null || _ajustando) return;
            int ih = _list.ItemHeight;
            if (ih <= 0) return;
            if (_padBase < 0) _padBase = Padding.Bottom;

            int inteiro = _list.Height + Padding.Bottom - _padBase;   // altura sem a compensacao
            if (inteiro <= ih) return;

            int novo = _padBase + inteiro % ih;
            if (novo == Padding.Bottom) return;

            _ajustando = true;
            try { Padding = new Padding(Padding.Left, Padding.Top, Padding.Right, novo); }
            finally { _ajustando = false; }

            if (_scroll != null) _scroll.Sincronizar();
        }

        public string SelectedId
        {
            get { return _selectedId; }
            set
            {
                _selectedId = value ?? "";
                SyncSelection();
            }
        }

        public SensorEntry Selected
        {
            get
            {
                SensorEntry s;
                return _byId.TryGetValue(_selectedId ?? "", out s) ? s : null;
            }
        }

        public void SetSensors(List<SensorEntry> sensors)
        {
            _all = sensors ?? new List<SensorEntry>();
            _byId.Clear();
            foreach (SensorEntry s in _all) _byId[s.Id] = s;

            // Uma pilula por categoria presente, na ordem canonica - assim os
            // filtros nao trocam de lugar conforme a maquina ou a execucao.
            List<string> cats = new List<string>();
            cats.Add("Todos");
            cats.AddRange(Presentes());
            _chips.SetItems(cats);

            Rebuild();
        }

        /// <summary>Atualiza os valores sem remontar a lista.</summary>
        public void UpdateValues(Dictionary<string, float> snapshot)
        {
            if (snapshot == null || _all.Count == 0) return;
            bool changed = false;
            foreach (SensorEntry s in _all)
            {
                float v;
                if (!snapshot.TryGetValue(s.Id, out v)) continue;
                if (s.Value.HasValue && s.Value.Value == v) continue;
                s.Value = v;
                changed = true;
            }
            if (changed) _list.Invalidate();
        }

        /// <summary>Categoria da entrada, com recuo para o nome do dispositivo.</summary>
        private static string CategoryOf(SensorEntry s)
        {
            if (!string.IsNullOrEmpty(s.Category)) return s.Category;
            return string.IsNullOrEmpty(s.Hardware) ? "Outros" : s.Hardware;
        }

        /// <summary>
        /// As categorias presentes, na ordem canonica primeiro. O rabo de
        /// categorias desconhecidas existe para que uma entrada montada fora do
        /// Sensors nao desapareca da lista por nao constar da tabela.
        /// </summary>
        private List<string> Presentes()
        {
            List<string> ordem = new List<string>();
            foreach (string c in Sensors.Categories)
                foreach (SensorEntry s in _all)
                    if (CategoryOf(s) == c) { ordem.Add(c); break; }

            foreach (SensorEntry s in _all)
            {
                string c = CategoryOf(s);
                if (!ordem.Contains(c)) ordem.Add(c);
            }
            return ordem;
        }

        private void Rebuild()
        {
            string filter = _search.Query.Trim();
            string cat = Categories ? _chips.SelectedItem : null;
            bool byCat = cat != null && cat != "Todos";

            _building = true;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();

                // Percorre por categoria, e nao na ordem de descoberta: as duas
                // fontes intercalam dispositivos, entao varrer a lista crua
                // repetiria o mesmo cabecalho varias vezes.
                foreach (string c in Presentes())
                {
                    if (byCat && c != cat) continue;

                    bool header = false;
                    foreach (SensorEntry s in _all)
                    {
                        if (CategoryOf(s) != c) continue;
                        if (filter.Length > 0 && !Matches(s, filter)) continue;
                        // filtrando por categoria o cabecalho e redundante: a
                        // pilula acesa ja diz de que categoria e a lista
                        if (!byCat && !header) { _list.Items.Add(new GroupRow(c)); header = true; }
                        _list.Items.Add(s);
                    }
                }
            }
            finally { _list.EndUpdate(); _building = false; }
            SyncSelection();
            if (_scroll != null) _scroll.Sincronizar();
        }

        private void SyncSelection()
        {
            _building = true;
            try
            {
                for (int i = 0; i < _list.Items.Count; i++)
                {
                    SensorEntry s = _list.Items[i] as SensorEntry;
                    if (s != null && s.Id == _selectedId)
                    {
                        _list.SelectedIndex = i;
                        try { _list.TopIndex = Math.Max(0, i - 3); } catch { }
                        if (_scroll != null) _scroll.Sincronizar();
                        return;
                    }
                }
                _list.SelectedIndex = -1;
            }
            finally { _building = false; }
        }

        private static bool Matches(SensorEntry s, string filter)
        {
            StringComparison c = StringComparison.OrdinalIgnoreCase;
            foreach (string term in filter.Split(' '))
            {
                if (term.Length == 0) continue;
                bool hit =
                    (s.Name != null && s.Name.IndexOf(term, c) >= 0) ||
                    (s.Hardware != null && s.Hardware.IndexOf(term, c) >= 0) ||
                    (s.Category != null && s.Category.IndexOf(term, c) >= 0) ||
                    s.Type.ToString().IndexOf(term, c) >= 0;
                if (!hit) return false;   // todos os termos precisam bater
            }
            return true;
        }

        private void OnMouseMoveList(object sender, MouseEventArgs e)
        {
            int i = _list.IndexFromPoint(e.Location);
            if (i != _hover) { _hover = i; _list.Invalidate(); }
        }

        private void OnSelect(object sender, EventArgs e)
        {
            if (_building) return;
            int i = _list.SelectedIndex;
            if (i < 0) return;

            SensorEntry s = _list.Items[i] as SensorEntry;
            if (s == null)
            {
                // cabecalho nao e selecionavel: pula para o proximo sensor
                _building = true;
                for (int k = i + 1; k < _list.Items.Count; k++)
                    if (_list.Items[k] is SensorEntry) { _list.SelectedIndex = k; break; }
                _building = false;
                s = _list.SelectedIndex >= 0 ? _list.Items[_list.SelectedIndex] as SensorEntry : null;
                if (s == null) return;
            }

            _selectedId = s.Id;
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _list.Items.Count) return;
            Graphics g = e.Graphics;
            Ui.Smooth(g);

            object item = _list.Items[e.Index];
            Rectangle r = e.Bounds;

            using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillRectangle(b, r);

            GroupRow grp = item as GroupRow;
            if (grp != null)
            {
                TextRenderer.DrawText(g, grp.Name.ToUpperInvariant(), Ui.FontSmall,
                    new Rectangle(r.X + 10, r.Y, r.Width - 14, r.Height), Ui.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                using (Pen pen = new Pen(Ui.Border))
                    g.DrawLine(pen, r.X + 10, r.Bottom - 1, r.Right - 10, r.Bottom - 1);
                return;
            }

            SensorEntry s = item as SensorEntry;
            if (s == null) return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool hovered = (e.Index == _hover) && !selected;

            if (selected || hovered)
            {
                Rectangle rr = new Rectangle(r.X + 4, r.Y + 1, r.Width - 8, r.Height - 2);
                using (GraphicsPath p = Ui.RoundRect(rr, 5))
                using (SolidBrush b = new SolidBrush(selected ? Ui.AccentSoft : Ui.Hover))
                    g.FillPath(b, p);
                if (selected)
                    using (GraphicsPath p = Ui.RoundRect(rr, 5))
                    using (Pen pen = new Pen(Ui.Accent))
                        g.DrawPath(pen, p);
            }

            string value = s.Formatted;
            bool noRead = !s.Value.HasValue || float.IsNaN(s.Value.Value);
            if (noRead) value = "sem leitura";

            Size vs = TextRenderer.MeasureText(g, value, Ui.FontBase);
            Color valColor = noRead ? Ui.Muted : (selected ? Ui.Accent : Ui.Muted);

            TextRenderer.DrawText(g, value, Ui.FontBase,
                new Rectangle(r.Right - vs.Width - 14, r.Y, vs.Width + 4, r.Height), valColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, s.Name, selected ? Ui.FontMed : Ui.FontBase,
                new Rectangle(r.X + 14, r.Y, r.Width - vs.Width - 34, r.Height), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            _list.BackColor = Ui.Surface;
            _list.ForeColor = Ui.Text;
            base.OnPaint(e);
        }
    }
}
