using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Tokens visuais e componentes desenhados.
    ///
    /// O WinForms nao oferece nada com acabamento moderno: botoes, listas e
    /// caixas de selecao carregam o visual do Windows 2000. Aqui definimos
    /// uma paleta unica e desenhamos os controles, o que da consistencia e
    /// permite acompanhar o tema claro/escuro do sistema.
    /// </summary>
    public static class Ui
    {
        public static bool Dark { get { return Theme.IsDark; } }

        // superficies
        public static Color Window { get { return Dark ? C(0x1C, 0x1C, 0x1F) : C(0xF4, 0xF5, 0xF7); } }
        public static Color Sidebar { get { return Dark ? C(0x17, 0x17, 0x1A) : C(0xEA, 0xEC, 0xF0); } }
        public static Color Surface { get { return Dark ? C(0x26, 0x26, 0x2A) : C(0xFF, 0xFF, 0xFF); } }
        public static Color SurfaceAlt { get { return Dark ? C(0x2E, 0x2E, 0x33) : C(0xF7, 0xF8, 0xFA); } }
        public static Color Border { get { return Dark ? C(0x38, 0x38, 0x3E) : C(0xDA, 0xDD, 0xE3); } }

        // texto
        public static Color Text { get { return Dark ? C(0xF0, 0xF0, 0xF3) : C(0x1B, 0x1D, 0x21); } }
        public static Color Muted { get { return Dark ? C(0x96, 0x99, 0xA3) : C(0x6B, 0x70, 0x7B); } }

        // enfase - azul do icone do aplicativo
        public static Color Accent { get { return C(0x2D, 0x7D, 0xF6); } }
        public static Color AccentHover { get { return C(0x4A, 0x91, 0xFF); } }
        public static Color AccentSoft { get { return Dark ? C(0x1E, 0x33, 0x52) : C(0xDE, 0xEA, 0xFF); } }

        // estados
        public static Color Warn { get { return C(0xF0, 0xAF, 0x3C); } }
        public static Color Danger { get { return C(0xEB, 0x5A, 0x4B); } }
        public static Color Hover { get { return Dark ? C(0x32, 0x32, 0x38) : C(0xEC, 0xEF, 0xF4); } }

        public static readonly Font FontBase = new Font("Segoe UI", 9f);
        public static readonly Font FontMed = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font FontTitle = new Font("Segoe UI", 14f, FontStyle.Regular);
        public static readonly Font FontSection = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.25f);

        public const int Radius = 8;
        public const int Gap = 12;

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }
    }

    /// <summary>Painel com cantos arredondados e borda sutil.</summary>
    public class Card : Panel
    {
        public string Title = null;
        public int TitleHeight = 34;

        public Card()
        {
            // SupportsTransparentBackColor e obrigatorio: sem ele, atribuir
            // Color.Transparent lanca "o controle nao da suporte a cores da
            // tela de fundo transparente".
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Ui.FontBase;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(r, Ui.Radius))
            using (SolidBrush b = new SolidBrush(Ui.Surface))
            using (Pen pen = new Pen(Ui.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            if (!string.IsNullOrEmpty(Title))
            {
                using (SolidBrush b = new SolidBrush(Ui.Text))
                    g.DrawString(Title, Ui.FontSection, b, 14, 10);
                using (Pen pen = new Pen(Ui.Border))
                    g.DrawLine(pen, 1, TitleHeight, Width - 2, TitleHeight);
            }
        }
    }

    /// <summary>Botao plano com estados de foco e realce opcional.</summary>
    public class FlatBtn : Control
    {
        public bool Primary = false;
        public bool Danger = false;
        private bool _hover, _down;

        public FlatBtn()
        {
            // SupportsTransparentBackColor e obrigatorio: sem ele, atribuir
            // Color.Transparent lanca "o controle nao da suporte a cores da
            // tela de fundo transparente".
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Ui.FontBase;
            Size = new Size(96, 32);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill, fore, border;
            if (Primary)
            {
                fill = _down ? Ui.Accent : (_hover ? Ui.AccentHover : Ui.Accent);
                fore = Color.White;
                border = fill;
            }
            else if (Danger)
            {
                fill = _hover ? Color.FromArgb(60, Ui.Danger) : Color.Transparent;
                fore = Ui.Danger;
                border = Ui.Danger;
            }
            else
            {
                fill = _down ? Ui.SurfaceAlt : (_hover ? Ui.Hover : Ui.Surface);
                fore = Enabled ? Ui.Text : Ui.Muted;
                border = Ui.Border;
            }

            using (GraphicsPath p = Ui.RoundRect(r, 6))
            {
                if (fill != Color.Transparent)
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }

            TextRenderer.DrawText(g, Text, Font, r, Enabled ? fore : Ui.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Interruptor de duas posicoes, no lugar da caixa de selecao.</summary>
    public class Toggle : Control
    {
        private bool _on;
        private bool _hover;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _on; }
            set
            {
                if (_on == value) return;
                _on = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>Rotulo desenhado a direita do interruptor.</summary>
        public string Label = "";

        public Toggle()
        {
            // SupportsTransparentBackColor e obrigatorio: sem ele, atribuir
            // Color.Transparent lanca "o controle nao da suporte a cores da
            // tela de fundo transparente".
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Ui.FontBase;
            Size = new Size(240, 26);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);

            int h = 20, w = 38;
            int top = (Height - h) / 2;
            Rectangle track = new Rectangle(0, top, w, h);

            Color fill = _on ? (_hover ? Ui.AccentHover : Ui.Accent)
                             : (_hover ? Ui.Border : (Ui.Dark ? Ui.SurfaceAlt : Color.FromArgb(0xCF, 0xD3, 0xDA)));

            using (GraphicsPath p = Ui.RoundRect(track, h / 2))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);

            int knob = h - 6;
            int kx = _on ? track.Right - knob - 3 : track.X + 3;
            using (SolidBrush b = new SolidBrush(Color.White))
                g.FillEllipse(b, kx, top + 3, knob, knob);

            if (!string.IsNullOrEmpty(Label))
            {
                Rectangle tr = new Rectangle(w + 10, 0, Width - w - 10, Height);
                TextRenderer.DrawText(g, Label, Font, tr, Ui.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>Uma entrada da barra lateral.</summary>
    public class NavItem
    {
        public string Text;
        public string Glyph;     // caractere de Segoe MDL2 Assets
        public Control Page;
        internal Rectangle Bounds;
    }

    /// <summary>
    /// Barra lateral de navegacao: cabecalho com o icone do aplicativo e a
    /// lista de secoes. Separar por secao evita a tela unica sobrecarregada.
    /// </summary>
    public class NavBar : Control
    {
        private readonly System.Collections.Generic.List<NavItem> _items = new System.Collections.Generic.List<NavItem>();
        private int _selected = 0, _hover = -1;
        public event EventHandler SelectionChanged;

        public Image Logo;
        public string AppName = "Mhiagos Control";
        public string Subtitle = "";

        public NavBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Ui.FontBase;
        }

        public void Add(NavItem item) { _items.Add(item); Invalidate(); }

        public NavItem Selected
        {
            get { return (_selected >= 0 && _selected < _items.Count) ? _items[_selected] : null; }
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value < 0 || value >= _items.Count || value == _selected) return;
                _selected = value;
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        private const int HeaderH = 92;
        private const int RowH = 40;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h >= 0) SelectedIndex = h;
            base.OnMouseDown(e);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Bounds.Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            using (SolidBrush b = new SolidBrush(Ui.Sidebar)) g.FillRectangle(b, ClientRectangle);

            if (Logo != null)
                g.DrawImage(Logo, 18, 20, 34, 34);

            using (SolidBrush b = new SolidBrush(Ui.Text))
                g.DrawString(AppName, Ui.FontMed, b, 60, 24);
            if (!string.IsNullOrEmpty(Subtitle))
                using (SolidBrush b = new SolidBrush(Ui.Muted))
                    g.DrawString(Subtitle, Ui.FontSmall, b, 60, 40);

            int y = HeaderH;
            for (int i = 0; i < _items.Count; i++)
            {
                NavItem it = _items[i];
                it.Bounds = new Rectangle(8, y, Width - 16, RowH);

                bool sel = (i == _selected);
                if (sel || i == _hover)
                {
                    using (GraphicsPath p = Ui.RoundRect(it.Bounds, 6))
                    using (SolidBrush b = new SolidBrush(sel ? Ui.AccentSoft : Ui.Hover))
                        g.FillPath(b, p);
                }
                if (sel)
                {
                    using (GraphicsPath p = Ui.RoundRect(new Rectangle(it.Bounds.X, it.Bounds.Y + 9, 3, RowH - 18), 2))
                    using (SolidBrush b = new SolidBrush(Ui.Accent))
                        g.FillPath(b, p);
                }

                Color fore = sel ? Ui.Text : Ui.Muted;
                if (!string.IsNullOrEmpty(it.Glyph))
                {
                    using (Font f = new Font("Segoe MDL2 Assets", 11f))
                    using (SolidBrush b = new SolidBrush(sel ? Ui.Accent : fore))
                        g.DrawString(it.Glyph, f, b, it.Bounds.X + 14, it.Bounds.Y + 11);
                }
                TextRenderer.DrawText(g, it.Text, sel ? Ui.FontMed : Ui.FontBase,
                    new Rectangle(it.Bounds.X + 42, it.Bounds.Y, it.Bounds.Width - 46, RowH), fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                y += RowH + 2;
            }
        }
    }
}
