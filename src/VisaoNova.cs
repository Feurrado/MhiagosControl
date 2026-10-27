using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Faixa de status da Visao geral nova: um "chip" por area (PC,
    /// perifericos, casa, painel do cooler). Cada chip le o proprio texto na
    /// hora de pintar e leva a pagina da area no clique.
    /// </summary>
    internal sealed class FaixaDeStatus : Control
    {
        internal sealed class Chip
        {
            public string Chave, Titulo, Glifo;
            public Func<string> Valor;
            public Func<bool> Atencao;
        }

        public readonly List<Chip> Chips = new List<Chip>();
        public event Action<string> Abrir;
        private int _hover = -1;
        public const int Altura = 68;

        public FaixaDeStatus()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void Acrescentar(string chave, string titulo, string glifo, Func<string> valor, Func<bool> atencao)
        {
            Chip c = new Chip();
            c.Chave = chave; c.Titulo = titulo; c.Glifo = glifo; c.Valor = valor; c.Atencao = atencao;
            Chips.Add(c);
        }

        internal Rectangle Caixa(int i)
        {
            int n = Math.Max(1, Chips.Count), gap = Ui.S(8);
            int w = (Width - gap * (n - 1)) / n;
            return new Rectangle(i * (w + gap), 0, (i == n - 1 ? Width - i * (w + gap) : w) - 1, Height - 1);
        }

        private int Hit(Point p) { for (int i = 0; i < Chips.Count; i++) if (Caixa(i).Contains(p)) return i; return -1; }

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
            if (h >= 0 && Abrir != null) Abrir(Chips[h].Chave);
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Ui.Window)) g.FillRectangle(b, ClientRectangle);
            Ui.Smooth(g);
            for (int i = 0; i < Chips.Count; i++)
            {
                Chip c = Chips[i];
                Rectangle r = Caixa(i);
                bool atencao = c.Atencao != null && c.Atencao();
                using (GraphicsPath p = Ui.RoundRect(r, Ui.Radius))
                {
                    using (SolidBrush b = new SolidBrush(i == _hover ? Ui.Misturar(Ui.Surface, Ui.Hover, 0.7f) : Ui.Surface)) g.FillPath(b, p);
                    using (Pen pen = new Pen(atencao ? Ui.Warn : Ui.Border)) g.DrawPath(pen, p);
                }
                Rectangle ic = new Rectangle(r.X + Ui.S(12), r.Y + (r.Height - Ui.S(36)) / 2, Ui.S(36), Ui.S(36));
                using (GraphicsPath p = Ui.RoundRect(ic, Ui.S(10)))
                using (SolidBrush b = new SolidBrush(Ui.AccentSoft)) g.FillPath(b, p);
                if (c.Chave == "baterias") Ui.IconeDeBateria(g, RectangleF.Inflate(ic, -Ui.S(9), -Ui.S(11)), Ui.AccentText);
                else TextRenderer.DrawText(g, c.Glifo, Ui.FontGlyph11, ic, Ui.AccentText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                int tx = ic.Right + Ui.S(10), tw = r.Right - tx - Ui.S(8);
                TextRenderer.DrawText(g, c.Titulo, Ui.FontSmall, new Rectangle(tx, r.Y + Ui.S(14), tw, Ui.S(18)), Ui.Muted,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                string v = "";
                try { v = c.Valor != null ? c.Valor() : ""; } catch (Exception ex) { Log.Error("faixa de status", ex); }
                TextRenderer.DrawText(g, v, Ui.FontMed, new Rectangle(tx, r.Y + Ui.S(32), tw, Ui.S(22)), atencao ? Ui.Warn : Ui.Text,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }
    }

    public static partial class T
    {
        public static string VisaoNovaOpcao { get { return P("Visão geral nova (em teste)", "New overview (preview)"); } }
        public static string VisaoNovaNota { get { return P("PC, periféricos e casa com o mesmo peso; o cooler num cartão menor que recolhe.", "PC, peripherals and home with equal weight; the cooler in a smaller, collapsible card."); } }
        public static string VisaoChipPC { get { return P("Computador", "Computer"); } }
        public static string VisaoChipPerifericos { get { return P("Periféricos", "Peripherals"); } }
        public static string VisaoChipCooler { get { return P("Display do cooler", "Cooler display"); } }
        public static string VisaoNenhum { get { return P("nenhum conectado", "none connected"); } }
        public static string VisaoSemConta { get { return P("conectar conta", "connect account"); } }
    }
}
