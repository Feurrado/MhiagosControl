using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Menus de contexto no tema do aplicativo.
    ///
    /// O menu nativo (ContextMenu) nao aceita cor, fonte nem raio: no tema
    /// escuro aparecia claro e com a fonte antiga do sistema. Aqui e um
    /// ContextMenuStrip com um renderizador proprio - fundo e borda do tema,
    /// destaque arredondado no item sob o mouse, itens de 32 px de altura,
    /// marca de selecao em azul - que le as cores a cada pintura, entao troca
    /// de tema sem reconstruir nada.
    /// </summary>
    internal static class MenuTema
    {
        /// <summary>Marca o item como escolha unica: a marca vira um ponto, e nao um visto.</summary>
        internal const string Radio = "menu-radio";

        public static ContextMenuStrip Novo()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            Aplicar(m);
            return m;
        }

        public static void Aplicar(ToolStripDropDownMenu m)
        {
            m.Renderer = new RenderizadorDeMenu();
            m.Font = Ui.FontBase;
            m.ShowImageMargin = false;
            m.ShowCheckMargin = true;
            m.Padding = new Padding(Ui.S(4));
            m.BackColor = Ui.Surface;
            m.ForeColor = Ui.Text;
        }

        public static ToolStripMenuItem Item(string texto, EventHandler acao)
        {
            ToolStripMenuItem i = new ToolStripMenuItem(texto);
            if (acao != null) i.Click += acao;
            Ajustar(i);
            return i;
        }

        public static ToolStripMenuItem Submenu(string texto)
        {
            ToolStripMenuItem i = new ToolStripMenuItem(texto);
            Ajustar(i);
            Aplicar(i.DropDown as ToolStripDropDownMenu);
            return i;
        }

        public static ToolStripMenuItem Opcao(string texto, bool marcado, EventHandler acao)
        {
            ToolStripMenuItem i = Item(texto, acao);
            i.Name = Radio;   // no Name, para o Tag ficar livre (o menu de perfis guarda o perfil nele)
            i.Checked = marcado;
            return i;
        }

        public static void Separador(ToolStripItemCollection itens)
        {
            itens.Add(new ToolStripSeparator());
        }

        private static void Ajustar(ToolStripMenuItem i)
        {
            i.Font = Ui.FontBase;
            i.Padding = new Padding(Ui.S(2), Ui.S(6), Ui.S(2), Ui.S(6));
            i.AutoSize = true;
        }
    }

    internal sealed class RenderizadorDeMenu : ToolStripProfessionalRenderer
    {
        public RenderizadorDeMenu() : base(new TabelaDeCores()) { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Ui.Surface)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Rectangle r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (Pen p = new Pen(Ui.Border)) e.Graphics.DrawRectangle(p, r);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            ToolStripItem item = e.Item;
            if (!item.Enabled || !item.Selected) return;
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            Rectangle r = new Rectangle(Ui.S(2), 1, item.Width - Ui.S(4) - 1, item.Height - 2);   // centro no meio do item
            using (GraphicsPath p = Ui.RoundRect(r, Ui.RaioControle))
            using (SolidBrush b = new SolidBrush(Ui.Hover))
                g.FillPath(b, p);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Ui.Text : Ui.Faint;
            e.TextFont = Ui.FontBase;
            // A Segoe UI Variable deixa as letras alto na caixa da linha (o espaco de
            // baixo e maior): centrada pela caixa, a palavra ficava uns pixels acima
            // do meio do destaque. Desce o texto o que a tinta medida pede.
            Rectangle tr = e.TextRectangle;
            // E o retangulo que o ToolStrip entrega vem no ALTO do item (Y=2 num item de
            // 34 px), e nao no meio: era isso que tirava o texto do eixo do destaque.
            tr.Y = (e.Item.Height - tr.Height) / 2 - (int)Math.Round(OffsetDoTexto());
            e.TextRectangle = tr;
            base.OnRenderItemText(e);

            // A marca e desenhada aqui, com o texto, e nao no OnRenderItemCheck: so
            // agora se sabe em que linha as letras ficaram, e e nesse eixo que ela
            // tem de ficar (centrar na caixa do item errava, e errava mais em DPI alto).
            float cx;
            ToolStripMenuItem mi = e.Item as ToolStripMenuItem;
            if (mi != null && mi.Checked && _centrosDaMarca.TryGetValue(mi, out cx))
                DesenharMarca(e.Graphics, mi, cx, e.TextRectangle.Y + e.TextRectangle.Height / 2f + OffsetDoTexto());
        }

        private readonly Dictionary<ToolStripItem, float> _centrosDaMarca = new Dictionary<ToolStripItem, float>();

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? Ui.Muted : Ui.Faint;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(Ui.Border))
                e.Graphics.DrawLine(p, Ui.S(8), y, e.Item.Width - Ui.S(8), y);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            _centrosDaMarca[e.Item] = r.X + r.Width / 2f;   // quem desenha e o OnRenderItemText
        }

        private static void DesenharMarca(Graphics g, ToolStripItem item, float cx, float cy)
        {
            Ui.Smooth(g);
            if (item.Name == MenuTema.Radio)
            {
                float d = Ui.S(7);
                using (SolidBrush b = new SolidBrush(Ui.AccentText))
                    g.FillEllipse(b, cx - d / 2f, cy - d / 2f, d, d);
            }
            else
            {
                float s = Ui.S(5);
                using (Pen p = new Pen(Ui.AccentText, 2f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    g.DrawLines(p, new PointF[] { new PointF(cx - s, cy), new PointF(cx - s / 3f, cy + s * 0.7f), new PointF(cx + s, cy - s * 0.7f) });
                }
            }
        }

        private static float _offsetTexto = float.NaN;

        /// <summary>
        /// Quanto o centro das letras fica acima ou abaixo do centro da linha, medido
        /// pelo desenho de verdade (a conta por metricas de fonte errava em DPI alto):
        /// escreve um "H" centrado numa caixa e acha as linhas de tinta.
        /// </summary>
        private static float OffsetDoTexto()
        {
            if (!float.IsNaN(_offsetTexto)) return _offsetTexto;
            try
            {
                int h = Ui.S(32), w = Ui.S(40);
                using (Bitmap b = new Bitmap(w, h))
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Black);
                    TextRenderer.DrawText(g, "H", Ui.FontBase, new Rectangle(0, 0, w, h), Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    int topo = -1, base0 = -1;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            if (b.GetPixel(x, y).R > 90) { if (topo < 0) topo = y; base0 = y; break; }
                    if (topo >= 0) _offsetTexto = (topo + base0 + 1) / 2f - h / 2f;
                }
            }
            catch { }
            if (float.IsNaN(_offsetTexto)) _offsetTexto = 0f;
            return _offsetTexto;
        }

        /// <summary>Tabela neutra: o resto do desenho e do renderizador, mas o que vaza usa as cores do tema.</summary>
        private sealed class TabelaDeCores : ProfessionalColorTable
        {
            public TabelaDeCores() { UseSystemColors = false; }
            public override Color ToolStripDropDownBackground { get { return Ui.Surface; } }
            public override Color MenuBorder { get { return Ui.Border; } }
            public override Color MenuItemBorder { get { return Ui.Hover; } }
            public override Color MenuItemSelected { get { return Ui.Hover; } }
            public override Color ImageMarginGradientBegin { get { return Ui.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Ui.Surface; } }
            public override Color ImageMarginGradientEnd { get { return Ui.Surface; } }
            public override Color SeparatorDark { get { return Ui.Border; } }
            public override Color SeparatorLight { get { return Ui.Surface; } }
        }
    }
}
