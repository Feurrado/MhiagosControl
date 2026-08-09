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

        public SmoothListBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ERASEBKGND) { m.Result = (IntPtr)1; return; }
            base.WndProc(ref m);
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
        private List<SensorEntry> _all = new List<SensorEntry>();
        private readonly Dictionary<string, SensorEntry> _byId = new Dictionary<string, SensorEntry>();
        private string _selectedId = "";
        private int _hover = -1;
        private bool _building = false;

        public event EventHandler SelectionChanged;

        public SensorPicker()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            _search = new SearchBox();
            _search.Dock = DockStyle.Top;
            _search.QueryChanged += delegate { Rebuild(); };

            _list = new SmoothListBox();
            _list.Dock = DockStyle.Fill;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = 26;
            _list.BorderStyle = BorderStyle.None;
            _list.IntegralHeight = false;
            _list.Font = Ui.FontBase;
            _list.DrawItem += new DrawItemEventHandler(OnDrawItem);
            _list.SelectedIndexChanged += new EventHandler(OnSelect);
            _list.MouseMove += new MouseEventHandler(OnMouseMoveList);
            _list.MouseLeave += delegate { _hover = -1; _list.Invalidate(); };

            Panel spacer = new Panel();
            spacer.Dock = DockStyle.Top;
            spacer.Height = 8;
            spacer.BackColor = Color.Transparent;

            Controls.Add(_list);
            Controls.Add(spacer);
            Controls.Add(_search);
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

        private void Rebuild()
        {
            string filter = _search.Query.Trim();
            _building = true;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                string lastGroup = null;
                foreach (SensorEntry s in _all)
                {
                    if (filter.Length > 0 && !Matches(s, filter)) continue;
                    string hw = string.IsNullOrEmpty(s.Hardware) ? "Outros" : s.Hardware;
                    if (hw != lastGroup) { _list.Items.Add(new GroupRow(hw)); lastGroup = hw; }
                    _list.Items.Add(s);
                }
            }
            finally { _list.EndUpdate(); _building = false; }
            SyncSelection();
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

            Color fore = Ui.Text;
            string value = s.Formatted;
            bool noRead = !s.Value.HasValue || float.IsNaN(s.Value.Value);
            if (noRead) { value = "sem leitura"; }

            Size vs = TextRenderer.MeasureText(g, value, Ui.FontBase);
            TextRenderer.DrawText(g, value, Ui.FontBase,
                new Rectangle(r.Right - vs.Width - 14, r.Y, vs.Width + 4, r.Height),
                noRead ? Ui.Muted : (selected ? Ui.Accent : Ui.Muted),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, s.Name, selected ? Ui.FontMed : Ui.FontBase,
                new Rectangle(r.X + 14, r.Y, r.Width - vs.Width - 34, r.Height), fore,
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
