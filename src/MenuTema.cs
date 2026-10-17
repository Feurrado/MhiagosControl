using System;
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
            Rectangle r = new Rectangle(Ui.S(2), 1, item.Width - Ui.S(4) - 1, item.Height - 3);
            using (GraphicsPath p = Ui.RoundRect(r, Ui.RaioControle))
            using (SolidBrush b = new SolidBrush(Ui.Hover))
                g.FillPath(b, p);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Ui.Text : Ui.Faint;
            e.TextFont = Ui.FontBase;
            base.OnRenderItemText(e);
        }

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
            Graphics g = e.Graphics;
            Ui.Smooth(g);
            Rectangle r = e.ImageRectangle;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            // O texto do item fica um pouco acima do centro da caixa (o espaco do
            // corpo da letra e maior embaixo); medido numa captura, a marca
            // centrada na caixa caia ~1 px abaixo do eixo das letras, e mais
            // em tela de DPI alto. O ajuste cresce com a fonte.
            cy -= Ui.FontBase.Height * 0.07f;
            if (e.Item.Name == MenuTema.Radio)
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
