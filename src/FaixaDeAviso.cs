using System;
using System.Drawing;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Faixa de aviso no alto da janela, no lugar de uma caixa de dialogo.
    ///
    /// Falha que da para tentar de novo nao justifica travar a janela com um
    /// MessageBox: a faixa diz o que houve, oferece a acao que resolve (quando
    /// ha) e some sozinha ou no X. Fica Dock.Top, entao o conteudo desce em vez
    /// de ser coberto.
    /// </summary>
    internal sealed class FaixaDeAviso : Control
    {
        private readonly FlatBtn _acao = new FlatBtn();
        private readonly FlatBtn _fechar = new FlatBtn();
        private Action _aoAgir;
        private string _mensagem = "";

        public FaixaDeAviso()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Visible = false;
            Height = Ui.S(56);

            _acao.Primary = true;
            _acao.Visible = false;
            _acao.Click += delegate { Action a = _aoAgir; Ocultar(); if (a != null) a(); };

            _fechar.Text = "\uE711";
            _fechar.Font = Ui.FontGlyph9;
            _fechar.Click += delegate { Ocultar(); };

            Controls.Add(_acao);
            Controls.Add(_fechar);
        }

        public string Mensagem { get { return _mensagem; } }
        public string TextoDaAcao { get { return _acao.Visible ? _acao.Text : null; } }

        /// <summary>Mostra a faixa. Sem acao, so a mensagem e o X.</summary>
        public void Mostrar(string mensagem, string textoDaAcao, Action aoAgir)
        {
            _mensagem = mensagem ?? "";
            _aoAgir = aoAgir;
            _acao.Text = textoDaAcao ?? "";
            _acao.Visible = aoAgir != null && !string.IsNullOrEmpty(textoDaAcao);
            Ajustar();
            Visible = true;
            BringToFront();
            Invalidate();
            AccessibleName = _mensagem;
        }

        public void Ocultar()
        {
            _aoAgir = null;
            Visible = false;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Ajustar();
        }

        private void Ajustar()
        {
            int m = Ui.S(8);
            int altBtn = Ui.S(28);
            int y = (Height - altBtn) / 2;
            _fechar.SetBounds(Width - m - Ui.S(28), y, Ui.S(28), altBtn);
            int larguraAcao = _acao.Visible
                ? TextRenderer.MeasureText(_acao.Text, _acao.Font).Width + Ui.S(28) : 0;
            _acao.SetBounds(_fechar.Left - m - larguraAcao, y, larguraAcao, altBtn);
        }

        /// <summary>Largura que sobra para o texto, entre o icone e os botoes.</summary>
        private Rectangle AreaDoTexto()
        {
            int esq = Ui.S(16);
            int dir = (_acao.Visible ? _acao.Left : _fechar.Left) - Ui.S(8);
            return new Rectangle(esq, 0, Math.Max(Ui.S(40), dir - esq), Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.Window);
            Rectangle faixa = new Rectangle(Ui.S(16), Ui.S(4), Width - Ui.S(32), Height - Ui.S(8));
            using (Brush b = new SolidBrush(Ui.Misturar(Ui.Surface, Ui.Warn, 0.14f)))
                g.FillRectangle(b, faixa);
            using (Brush b = new SolidBrush(Ui.Warn))
                g.FillRectangle(b, new Rectangle(faixa.Left, faixa.Top, Ui.S(3), faixa.Height));

            Rectangle texto = AreaDoTexto();
            texto.X += Ui.S(10);
            texto.Width -= Ui.S(10);
            TextRenderer.DrawText(g, _mensagem, Ui.FontBase, texto, Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }
    }
}
