using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Lista de perfis com o conteudo de cada um a vista.
    ///
    /// Antes era um ListBox cru: quatro nomes numa caixa branca de canto vivo,
    /// sem dizer o que cada perfil mostra nem qual esta valendo. Descobrir a
    /// diferenca entre dois perfis exigia selecionar um, ir para a pagina de
    /// paineis, voltar e selecionar o outro.
    ///
    /// Aqui cada linha traz o nome, os dois sensores que o perfil manda para o
    /// mostrador e um distintivo no que esta ativo.
    /// </summary>
    public class ProfileList : Control
    {
        private readonly List<Profile> _items = new List<Profile>();
        private int _sel = -1, _hover = -1, _top = 0;

        /// <summary>Nome legivel de um identificador de sensor.</summary>
        public Func<string, string> Resolve;

        /// <summary>Nome do perfil que esta valendo no mostrador.</summary>
        public string ActiveName = "";

        public event EventHandler SelectionChanged;

        /// <summary>Duplo clique numa linha - a pagina usa para aplicar o perfil.</summary>
        public event EventHandler ItemActivated;

        private const int RowH = 54;

        public ProfileList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.SurfaceAlt;
            Font = Ui.FontBase;
        }

        public void SetItems(IEnumerable<Profile> items, Profile select)
        {
            _items.Clear();
            _items.AddRange(items);
            _sel = select != null ? _items.IndexOf(select) : -1;
            if (_sel < 0 && _items.Count > 0) _sel = 0;
            Visivel(_sel);
            Invalidate();
        }

        public Profile Selected
        {
            get { return _sel >= 0 && _sel < _items.Count ? _items[_sel] : null; }
            set
            {
                int i = value != null ? _items.IndexOf(value) : -1;
                if (i == _sel) return;
                _sel = i; Visivel(i); Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        private int PorTela { get { return Math.Max(1, (Height - 8) / RowH); } }

        private void Visivel(int i)
        {
            if (i < 0) return;
            if (i < _top) _top = i;
            else if (i >= _top + PorTela) _top = i - PorTela + 1;
            Limitar();
        }

        private void Limitar()
        {
            int max = Math.Max(0, _items.Count - PorTela);
            _top = Math.Max(0, Math.Min(max, _top));
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Limitar(); }

        private int Hit(Point p)
        {
            int i = _top + (p.Y - 4) / RowH;
            return (i >= _top && i < _items.Count && i < _top + PorTela) ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
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

        protected override void OnDoubleClick(EventArgs e)
        {
            if (_sel >= 0 && ItemActivated != null) ItemActivated(this, EventArgs.Empty);
            base.OnDoubleClick(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _top -= Math.Sign(e.Delta);
            Limitar();
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int novo = _sel;
            if (e.KeyCode == Keys.Down) novo = Math.Min(_items.Count - 1, _sel + 1);
            else if (e.KeyCode == Keys.Up) novo = Math.Max(0, _sel - 1);
            if (novo != _sel)
            {
                _sel = novo; Visivel(novo); Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
            base.OnKeyDown(e);
        }

        private string Resumo(Profile p)
        {
            string a = Nome(p.Panel1Id), b = Nome(p.Panel2Id);
            return a + "   ·   " + b;
        }

        private string Nome(string id)
        {
            if (string.IsNullOrEmpty(id)) return "—";
            if (Resolve == null) return id;
            string n = Resolve(id);
            return string.IsNullOrEmpty(n) ? "—" : n;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);

            using (GraphicsPath p = Ui.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), 8))
            using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt))
            using (Pen pen = new Pen(Ui.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            int y = 4;
            for (int i = _top; i < _items.Count && i < _top + PorTela; i++, y += RowH)
            {
                Profile p = _items[i];
                Rectangle r = new Rectangle(4, y, Width - 8, RowH - 2);

                bool sel = i == _sel;
                if (sel || i == _hover)
                {
                    using (GraphicsPath gp = Ui.RoundRect(r, 6))
                    using (SolidBrush b = new SolidBrush(sel ? Ui.AccentSoft : Ui.Hover))
                        g.FillPath(b, gp);
                    if (sel)
                        using (GraphicsPath gp = Ui.RoundRect(r, 6))
                        using (Pen pen = new Pen(Ui.Accent))
                            g.DrawPath(pen, gp);
                }

                // O distintivo e medido antes do nome: e ele que define quanto
                // sobra para o texto, e nao o contrario.
                bool ativo = string.Equals(p.Name, ActiveName, StringComparison.Ordinal);
                int right = r.Right - 10;
                if (ativo)
                {
                    Size bs = TextRenderer.MeasureText(g, T.ActiveBadge, Ui.FontSmall);
                    Rectangle badge = new Rectangle(right - bs.Width - 14, r.Y + 9, bs.Width + 14, 18);
                    using (GraphicsPath gp = Ui.RoundRect(badge, 9))
                    using (SolidBrush b = new SolidBrush(Ui.Accent))
                        g.FillPath(b, gp);
                    TextRenderer.DrawText(g, T.ActiveBadge, Ui.FontSmall, badge, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    right = badge.Left - 8;
                }

                TextRenderer.DrawText(g, p.Name, Ui.FontMed,
                    new Rectangle(r.X + 12, r.Y + 6, right - r.X - 16, 20), Ui.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                TextRenderer.DrawText(g, Resumo(p), Ui.FontSmall,
                    new Rectangle(r.X + 12, r.Y + 27, r.Width - 24, 18), Ui.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            // Sem barra: o indicador so aparece quando ha o que rolar, e ocupa
            // a lateral em vez de roubar largura do resumo.
            if (_items.Count > PorTela)
            {
                int trilho = Height - 12;
                int alt = Math.Max(24, trilho * PorTela / _items.Count);
                int max = _items.Count - PorTela;
                int topo = max <= 0 ? 0 : (trilho - alt) * _top / max;
                using (GraphicsPath gp = Ui.RoundRect(new Rectangle(Width - 8, 6 + topo, 4, alt), 2))
                using (SolidBrush b = new SolidBrush(Ui.Thumb))
                    g.FillPath(b, gp);
            }
        }
    }
}
