using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    public partial class SettingsForm : Form
    {
        // ---------------- pagina: Paineis ----------------

        /// <summary>
        /// O cartao de cada painel mostra apenas o sensor ja escolhido; a
        /// escolha acontece numa janela dedicada.
        ///
        /// Embutida na pagina, a lista ficava com 142 px - cinco linhas e meia
        /// para varias dezenas de sensores, parte delas cabecalhos de grupo -
        /// porque disputava altura com escala, unidade e previa. Num dialogo
        /// ela nao disputa com nada.
        /// </summary>
        private Control BuildPagePaineis()
        {
            Panel page = new Pagina();
            ((Pagina)page).RolarNaVertical();
            // fundo opaco vem da propria Pagina

            // Os seletores continuam sendo o estado da escolha; so nao aparecem
            // na pagina. Quem os edita e o dialogo.
            _pick1 = new SensorPicker();
            _pick1.SetSensors(Clone(_sensors));
            _pick2 = new SensorPicker();
            _pick2.SetSensors(Clone(_sensors));

            Card c1 = new Card();
            _cardPainel1 = c1;
            c1.Title = T.Panel1;
            c1.SetBounds(0, 0, Ui.S(370), Ui.S(282));
            page.Controls.Add(c1);

            _slot1 = new SensorSlot();
            _slot1.SetBounds(Ui.S(12), Ui.S(48), Ui.S(346), Ui.S(80));
            // Ancorado nos dois lados: com o cartao esticado, o nome do sensor e
            // a leitura ganham a largura junto, em vez de deixar um degrau branco
            // dentro de um cartao maior.
            _slot1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _slot1.Button.Click += delegate { TrocarSensor(_pick1, T.PickSensor1); };
            c1.Controls.Add(_slot1);

            c1.Controls.Add(MakeLabel(T.Scale, 14, 142, Ui.FontMed));
            _div1 = MakeDivisorRow(c1, 14, 162, 1);

            c1.Controls.Add(MakeLabel(T.Unit, 14, 196, Ui.FontMed));
            _unit1 = new Segmented();
            _unit1.SetItems("°C", "°F");
            _unit1.SetBounds(Ui.S(14), Ui.S(216), Ui.S(140), Ui.S(30));
            _unit1.SelectedIndexChanged += delegate { OnChanged(); };
            c1.Controls.Add(_unit1);
            c1.Controls.Add(NotaDeUnidade(14, 249));

            Card c2 = new Card();
            _cardPainel2 = c2;
            c2.Title = T.Panel2;
            c2.SetBounds(Ui.S(386), 0, Ui.S(370), Ui.S(282));
            page.Controls.Add(c2);

            _slot2 = new SensorSlot();
            _slot2.SetBounds(Ui.S(12), Ui.S(48), Ui.S(346), Ui.S(80));
            _slot2.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _slot2.Button.Click += delegate { TrocarSensor(_pick2, T.PickSensor2); };
            c2.Controls.Add(_slot2);

            c2.Controls.Add(MakeLabel(T.Scale, 14, 162 - 20, Ui.FontMed));
            _div2 = MakeDivisorRow(c2, 14, 162, 2);

            c2.Controls.Add(MakeLabel(T.Unit, 14, 196, Ui.FontMed));
            _unit2 = new Segmented();
            _unit2.SetItems("%", "W");
            _unit2.SetBounds(Ui.S(14), Ui.S(216), Ui.S(140), Ui.S(30));
            _unit2.SelectedIndexChanged += delegate { OnChanged(); };
            c2.Controls.Add(_unit2);
            c2.Controls.Add(NotaDeUnidade(14, 249));


            _panelPreviewCard = new Card();
            _panelPreviewCard.Title = T.Preview;
            _panelPreviewCard.SetBounds(0, Ui.S(294), Ui.S(756), Ui.S(532));
            page.Controls.Add(_panelPreviewCard);

            _preview = new PanelPreview();
            _preview.SetBounds(Ui.S(12), Ui.S(44), Ui.S(732), Ui.S(476));
            _preview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                              AnchorStyles.Left | AnchorStyles.Right;
            _panelPreviewCard.Controls.Add(_preview);

            return page;
        }

        private void TrocarSensor(SensorPicker alvo, string titulo)
        {
            using (SensorDialog d = new SensorDialog(Clone(_sensors), alvo.SelectedId, titulo))
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    alvo.SelectedId = d.SelectedId;
                    if (alvo == _pick2) AjustarUnidade2();
                    OnChanged();
                }
        }

        /// <summary>
        /// Acende o simbolo certo do painel 2 conforme o sensor escolhido.
        ///
        /// O hardware so tem esses dois: potencia acende W, carga e nivel
        /// acendem %. Nos demais tipos - clock, tensao, RPM - nenhum dos dois
        /// descreve a leitura, entao a escolha anterior fica de pe em vez de
        /// mudar sozinha para algo igualmente errado. Continua editavel: o
        /// ajuste automatico so acontece quando o sensor troca.
        /// </summary>
        private void AjustarUnidade2()
        {
            SensorEntry s = _pick2.Selected;
            if (s == null) return;

            if (s.Type == SensorType.Power) _unit2.SelectedIndex = 1;
            else if (s.Type == SensorType.Load || s.Type == SensorType.Level) _unit2.SelectedIndex = 0;
        }

        private FlatBtn[] MakeDivisorRow(Control parent, int x, int y, int which)
        {
            FlatBtn[] btns = new FlatBtn[DivValues.Length];
            for (int i = 0; i < DivValues.Length; i++)
            {
                FlatBtn b = new FlatBtn();
                b.Text = DivLabels[i];
                b.Font = Ui.FontSmall;
                b.SetBounds(Ui.S(x + i * 68), Ui.S(y), Ui.S(64), Ui.S(26));   // x e y de projeto
                b.Tag = DivValues[i];
                int captured = DivValues[i];
                int target = which;
                b.Click += delegate
                {
                    if (target == 1) _current.Divisor1 = captured; else _current.Divisor2 = captured;
                    OnChanged();
                };
                parent.Controls.Add(b);
                btns[i] = b;
            }
            return btns;
        }

        /// <summary>
        /// Nota de rodape do seletor de unidade. Cabe nos 268 px do cartao sem
        /// mexer no resto da pagina: o rotulo e o seletor sobem 4 px e a nota
        /// ocupa a folga que sobrava embaixo.
        /// </summary>
        private static Label NotaDeUnidade(int x, int y)
        {
            Label l = MakeLabel(T.UnitAlwaysOn, x, y, Ui.FontSmall);
            l.SetBounds(Ui.S(x), Ui.S(y), Ui.S(340), Ui.S(15));
            l.ForeColor = Ui.Muted;
            return l;
        }

        private static Label MakeLabel(string text, int x, int y, Font f)
        {
            Label l = new Label();
            l.Text = text; l.Font = f; l.AutoSize = false;
            l.SetBounds(Ui.S(x), Ui.S(y), Ui.S(300), Ui.S(18));   // medidas de projeto, a 96 dpi
            l.BackColor = Color.Transparent;
            l.ForeColor = Ui.Text;
            return l;
        }

        private static List<SensorEntry> Clone(List<SensorEntry> src)
        {
            List<SensorEntry> copy = new List<SensorEntry>(src.Count);
            foreach (SensorEntry s in src)
            {
                SensorEntry e = new SensorEntry();
                e.Id = s.Id; e.Hardware = s.Hardware; e.Name = s.Name;
                e.Category = s.Category;
                e.Label = s.Label; e.Type = s.Type; e.Value = s.Value; e.Unit = s.Unit;
                // Source e Members faltavam: a lista declarava tudo como
                // LibreHardwareMonitor, o que nao aparecia enquanto a
                // procedencia nao era exibida na linha do sensor.
                e.Source = s.Source; e.Members = s.Members;
                copy.Add(e);
            }
            return copy;
        }
    }
}
