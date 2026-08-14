using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MhiagosControl
{
    internal sealed class GameBindingView
    {
        public string Key;
        public string Name;
        public string Profile;
        public Image Icon;
        public bool Current;
    }

    /// <summary>Icone real do jogo, com glifo neutro quando o arquivo sumiu.</summary>
    internal sealed class GameIconView : Control
    {
        private Image _gameIcon;
        public Image GameIcon
        {
            get { return _gameIcon; }
            set
            {
                if (object.ReferenceEquals(_gameIcon, value)) return;
                _gameIcon = value;
                Invalidate();
            }
        }

        public GameIconView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            // Sem transparencia, o Windows limpa primeiro os 48 x 48 px como
            // um retangulo. O cartao desenhado por cima tem cantos redondos,
            // mas os quatro cantos do fundo quadrado continuam aparecendo.
            BackColor = Color.Transparent;
            Size = new Size(48, 48);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(r, 10))
            using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt))
            using (Pen pen = new Pen(Ui.Border))
            {
                g.FillPath(b, p); g.DrawPath(pen, p);
            }
            if (_gameIcon != null)
            {
                Rectangle icon = Fit(_gameIcon, Rectangle.Inflate(r, -6, -6));
                DrawRounded(g, _gameIcon, icon, Math.Max(10, icon.Width / 3));
            }
            else
                TextRenderer.DrawText(g, "\uE7FC", Ui.FontGlyph18, r, Ui.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        internal static Rectangle Fit(Image image, Rectangle bounds)
        {
            if (image == null || image.Width <= 0 || image.Height <= 0) return bounds;
            float scale = Math.Min(bounds.Width / (float)image.Width,
                                   bounds.Height / (float)image.Height);
            int width = Math.Max(1, (int)Math.Round(image.Width * scale));
            int height = Math.Max(1, (int)Math.Round(image.Height * scale));
            return new Rectangle(bounds.X + (bounds.Width - width) / 2,
                                 bounds.Y + (bounds.Height - height) / 2,
                                 width, height);
        }

        /// <summary>
        /// Arredonda a propria arte do icone, nao apenas o cartao atras dela.
        /// Alguns jogos (como VALORANT) trazem uma moldura quadrada desenhada
        /// dentro do ICO; um raio discreto deixa essa moldura visualmente
        /// quadrada mesmo quando o controle externo ja tem cantos arredondados.
        /// </summary>
        internal static void DrawRounded(Graphics graphics, Image image,
                                         Rectangle bounds, int radius)
        {
            GraphicsState state = graphics.Save();
            try
            {
                using (GraphicsPath clip = Ui.RoundRect(bounds, radius))
                {
                    graphics.SetClip(clip, CombineMode.Intersect);
                    graphics.DrawImage(image, bounds);
                }
            }
            finally { graphics.Restore(state); }
        }
    }

    /// <summary>Regras visiveis no formato jogo real -> perfil.</summary>
    internal sealed class GameBindingList : Control
    {
        private readonly List<GameBindingView> _items = new List<GameBindingView>();
        private int _hover = -1, _top;
        private const int RowH = 62;

        public string EmptyText = "";
        public event Action<string> RemoveRequested;

        public GameBindingList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.SurfaceAlt;
            Cursor = Cursors.Default;
        }

        public void SetItems(IEnumerable<GameBindingView> items)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);
            Limitar(); Invalidate();
        }

        private int PorTela { get { return Math.Max(1, (Height - 8) / RowH); } }
        private void Limitar() { _top = Math.Max(0, Math.Min(_top, Math.Max(0, _items.Count - PorTela))); }

        private int Hit(Point point)
        {
            if (point.Y < 4) return -1;
            int i = _top + (point.Y - 4) / RowH;
            return i >= _top && i < _items.Count && i < _top + PorTela ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = Hit(e.Location);
            if (i != _hover) { _hover = i; Invalidate(); }
            Cursor = i >= 0 && e.X >= Width - 46 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1; Cursor = Cursors.Default; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int i = Hit(e.Location);
            if (i >= 0 && e.X >= Width - 46 && RemoveRequested != null)
                RemoveRequested(_items[i].Key);
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _top -= Math.Sign(e.Delta); Limitar(); Invalidate(); base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            Rectangle outer = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(outer, 8))
            using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt))
            using (Pen pen = new Pen(Ui.Border))
            { g.FillPath(b, p); g.DrawPath(pen, p); }

            if (_items.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, Ui.FontBase, Rectangle.Inflate(outer, -16, -8), Ui.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.WordBreak);
                return;
            }

            int y = 4;
            for (int i = _top; i < _items.Count && i < _top + PorTela; i++, y += RowH)
                DrawRow(g, _items[i], i, new Rectangle(4, y, Width - 8, RowH - 2));

            if (_items.Count > PorTela)
            {
                int track = Height - 12;
                int h = Math.Max(24, track * PorTela / _items.Count);
                int max = _items.Count - PorTela;
                int top = max == 0 ? 0 : (track - h) * _top / max;
                using (GraphicsPath p = Ui.RoundRect(new Rectangle(Width - 7, 6 + top, 3, h), 2))
                using (SolidBrush b = new SolidBrush(Ui.Thumb)) g.FillPath(b, p);
            }
        }

        private void DrawRow(Graphics g, GameBindingView item, int index, Rectangle row)
        {
            bool hover = index == _hover;
            if (item.Current || hover)
                using (GraphicsPath p = Ui.RoundRect(row, 6))
                using (SolidBrush b = new SolidBrush(item.Current ? Ui.AccentSoft : Ui.Hover))
                    g.FillPath(b, p);

            Rectangle icon = new Rectangle(row.X + 10, row.Y + 10, 40, 40);
            using (GraphicsPath p = Ui.RoundRect(icon, 8))
            using (SolidBrush b = new SolidBrush(Ui.Surface)) g.FillPath(b, p);
            if (item.Icon != null)
            {
                Rectangle image = GameIconView.Fit(item.Icon, Rectangle.Inflate(icon, -5, -5));
                GameIconView.DrawRounded(g, item.Icon, image, Math.Max(8, image.Width / 3));
            }
            else TextRenderer.DrawText(g, "\uE7FC", Ui.FontGlyph18, icon, Ui.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            int right = row.Right - 48;
            TextRenderer.DrawText(g, item.Name, Ui.FontMed,
                new Rectangle(icon.Right + 12, row.Y + 9, right - icon.Right - 18, 20), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, "\u2192  " + item.Profile, Ui.FontSmall,
                new Rectangle(icon.Right + 12, row.Y + 31, right - icon.Right - 18, 18), Ui.Accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            Rectangle remove = new Rectangle(row.Right - 40, row.Y + 12, 30, 30);
            if (hover)
                using (GraphicsPath p = Ui.RoundRect(remove, 6))
                using (SolidBrush b = new SolidBrush(Ui.Hover)) g.FillPath(b, p);
            TextRenderer.DrawText(g, "\uE711", Ui.FontGlyph9, remove, hover ? Ui.Danger : Ui.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
