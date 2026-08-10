using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Mostra o sensor ja escolhido numa caixa unica, com o valor ao vivo e o
    /// botao que abre a janela de escolha.
    /// </summary>
    public class SensorSlot : Control
    {
        public SensorEntry Entry;
        public readonly FlatBtn Button;

        public SensorSlot()
        {
            // SupportsTransparentBackColor e obrigatorio: sem ele, atribuir
            // Color.Transparent lanca "o controle nao da suporte a cores da
            // tela de fundo transparente".
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Ui.FontBase;
            Height = 74;

            Button = new FlatBtn();
            Button.Text = "Trocar";
            Button.Size = new Size(78, 30);
            Controls.Add(Button);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Button != null) Button.SetBounds(Width - 92, (Height - 30) / 2, 78, 30);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Smooth(g);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Ui.RoundRect(r, 8))
            using (SolidBrush b = new SolidBrush(Ui.SurfaceAlt))
            using (Pen pen = new Pen(Ui.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            if (Entry == null)
            {
                TextRenderer.DrawText(g, "nenhum sensor escolhido", Ui.FontBase, r, Ui.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            // O valor e o que se olha; o nome do sensor so confirma a escolha.
            // Por isso ele vem primeiro na hierarquia e o nome desce a legenda.
            string val = Entry.Value.HasValue ? Entry.Formatted : "sem leitura";
            bool lido = Entry.Value.HasValue;
            Size vs = TextRenderer.MeasureText(g, val, lido ? Ui.FontValue : Ui.FontBase);

            int right = Width - 100;                 // onde comeca o botao Trocar
            int valX = right - vs.Width - 12;

            TextRenderer.DrawText(g, val, lido ? Ui.FontValue : Ui.FontBase,
                new Rectangle(valX, 0, vs.Width + 8, Height),
                lido ? Ui.Accent : Ui.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            // Bloco de texto centrado como um todo, medindo as duas linhas em
            // vez de fixar coordenadas - com fontes diferentes, chutar o topo
            // deixava o conjunto pousado alto no cartao.
            const int TitleH = 22, SubH = 17;
            int top = (Height - (TitleH + SubH)) / 2;
            int tw = valX - 24;

            TextRenderer.DrawText(g, Entry.Name, Ui.FontSection,
                new Rectangle(16, top, tw, TitleH), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // categoria, e nao o nome do dispositivo: "NVIDIA GeForce RTX 3060
            // (Ampere)" nao cabe no espaco que sobra ao lado do valor
            string sub = (string.IsNullOrEmpty(Entry.Category) ? "Outros" : Entry.Category) +
                         (string.IsNullOrEmpty(Entry.Source) ? "" : "  ·  " + Entry.Source);
            TextRenderer.DrawText(g, sub, Ui.FontSmall,
                new Rectangle(16, top + TitleH, tw, SubH), Ui.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>
    /// Janela dedicada a escolha do sensor.
    ///
    /// Aqui a lista nao divide altura com escala, unidade e previa, entao cabe
    /// mais que o dobro de linhas - e o filtro por categoria ainda corta a
    /// rolagem para os poucos sensores do hardware procurado.
    /// </summary>
    public class SensorDialog : Form
    {
        private readonly SensorPicker _pick;

        public string SelectedId { get { return _pick.SelectedId; } }

        public SensorDialog(List<SensorEntry> sensors, string current, string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 600);
            BackColor = Ui.Window;
            Font = Ui.FontBase;

            Card card = new Card();
            card.SetBounds(16, 16, 528, 508);
            Controls.Add(card);

            _pick = new SensorPicker();
            _pick.Categories = true;
            _pick.RowHeight = 30;
            _pick.SetBounds(12, 12, 504, 484);
            _pick.SetSensors(sensors);
            _pick.SelectedId = current;
            // duplo clique escolhe e fecha, como em qualquer seletor
            _pick.ItemActivated += delegate { DialogResult = DialogResult.OK; Close(); };
            card.Controls.Add(_pick);

            FlatBtn ok = new FlatBtn();
            ok.Text = "Usar"; ok.Primary = true;
            ok.SetBounds(448, 540, 96, 32);
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);

            FlatBtn cancel = new FlatBtn();
            cancel.Text = "Cancelar";
            cancel.SetBounds(344, 540, 96, 32);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            Theme.Apply(this);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Esc e Enter fecham na mao: os botoes sao controles proprios, nao
            // Button do WinForms, entao AcceptButton e CancelButton nao os aceitam.
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            if (keyData == Keys.Enter && _pick.SelectedId.Length > 0)
            { DialogResult = DialogResult.OK; Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
