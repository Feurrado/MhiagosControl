using System;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Erro inesperado sem a excecao crua na cara da pessoa.
    ///
    /// A mensagem de uma excecao ("Referencia de objeto nao definida...") nao
    /// ajuda quem usa e assusta. A janela diz o que aconteceu em linguagem
    /// comum; o texto tecnico fica no botao Copiar detalhes, pronto para colar
    /// num pedido de ajuda, e tambem no registro em disco.
    /// </summary>
    internal static class ErroInesperado
    {
        /// <summary>O texto tecnico que o botao copia.</summary>
        internal static string Detalhes(Exception ex)
        {
            StringBuilder t = new StringBuilder();
            t.AppendLine("Mhiagos Control " + Assembly.GetExecutingAssembly().GetName().Version);
            t.AppendLine("Windows: " + Environment.OSVersion.Version);
            t.AppendLine("Registro: " + Log.Path);
            t.AppendLine();
            t.AppendLine(ex == null ? "(sem excecao)" : ex.ToString());
            return t.ToString();
        }

        internal static void Mostrar(Exception ex)
        {
            try
            {
                using (Form f = new Form())
                {
                    f.Text = T.AppName;
                    f.FormBorderStyle = FormBorderStyle.FixedDialog;
                    f.StartPosition = FormStartPosition.CenterScreen;
                    f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = true;
                    f.ClientSize = new Size(Ui.S(420), Ui.S(170));
                    f.BackColor = Ui.Window;

                    Label titulo = new Label();
                    titulo.Text = T.UnexpectedErrorTitle;
                    titulo.Font = Ui.FontSection; titulo.ForeColor = Ui.Text;
                    titulo.SetBounds(Ui.S(20), Ui.S(18), Ui.S(380), Ui.S(26));

                    Label corpo = new Label();
                    corpo.Text = T.UnexpectedErrorBody;
                    corpo.Font = Ui.FontBase; corpo.ForeColor = Ui.Muted;
                    corpo.SetBounds(Ui.S(20), Ui.S(50), Ui.S(380), Ui.S(64));

                    FlatBtn copiar = new FlatBtn();
                    copiar.Text = T.CopyDetails;
                    copiar.SetBounds(Ui.S(20), Ui.S(122), Ui.S(170), Ui.S(32));
                    copiar.Click += delegate
                    {
                        try { Clipboard.SetText(Detalhes(ex)); copiar.Text = T.DetailsCopied; }
                        catch (Exception e2) { Log.Error("copiar detalhes", e2); }
                    };

                    FlatBtn fechar = new FlatBtn();
                    fechar.Text = T.Close2; fechar.Primary = true;
                    fechar.SetBounds(Ui.S(310), Ui.S(122), Ui.S(90), Ui.S(32));
                    fechar.Click += delegate { f.Close(); };

                    f.Controls.AddRange(new Control[] { titulo, corpo, copiar, fechar });
                    f.AcceptButton = null;
                    Theme.Apply(f);
                    f.ShowDialog();
                }
            }
            catch (Exception e3)
            {
                // ultimo recurso: ate a janela de erro falhou
                Log.Error("janela de erro inesperado", e3);
            }
        }
    }
}
