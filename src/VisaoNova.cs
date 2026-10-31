using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Management;
using System.Threading;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Pecas da Visao geral nova: fontes monoespacadas, as cores quente e fria,
    /// o nome de quem usa o Windows e o texto do cabecalho.
    ///
    /// Numeros, rotulos tecnicos e graficos vao em Cascadia Mono (Consolas se
    /// nao houver); o resto continua na fonte do app. Valores frios (uso, alvo
    /// do ar, barras) na cor de destaque; quentes (temperaturas, tempo da
    /// lavadora, bateria) em ambar, com tom proprio em cada tema para o
    /// contraste nao cair no claro.
    /// </summary>
    internal static class Visao
    {
        public static readonly string FamiliaMono = Ui.Tipo.Achar(new[] { "Cascadia Mono", "Consolas" }, "Consolas");
        public static readonly Font MonoPequena = new Font(FamiliaMono, Ui.Pt(8f));
        public static readonly Font MonoRotulo = new Font(FamiliaMono, Ui.Pt(8f));
        public static readonly Font MonoTexto = MonoSemi(Ui.Pt(9.5f));
        public static readonly Font MonoValor = MonoSemi(Ui.Pt(15f));
        public static readonly Font MonoGrande = MonoSemi(Ui.Pt(21f));
        public static readonly Font FonteFrase = Ui.Semi(Ui.Pt(17f), Ui.Tipo.Exibicao);

        private static Font MonoSemi(float tamanho)
        {
            try
            {
                Font f = new Font(FamiliaMono + " SemiBold", tamanho);
                // Familia inexistente volta como outra: o negrito da mono e melhor que uma sem serifa.
                if (f.Name.IndexOf(FamiliaMono, StringComparison.OrdinalIgnoreCase) >= 0) return f;
                f.Dispose();
            }
            catch { }
            return new Font(FamiliaMono, tamanho, FontStyle.Bold);
        }

        /// <summary>Ambar dos valores quentes: #f2b84b no escuro, #b35c00 no claro.</summary>
        public static Color Quente { get { return Ui.Dark ? Color.FromArgb(0xF2, 0xB8, 0x4B) : Color.FromArgb(0xB3, 0x5C, 0x00); } }

        public static readonly Color Verde = Color.FromArgb(0x2E, 0xA0, 0x43);

        /// <summary>Roda na thread da interface (o ServicoCasa avisa de outra thread).</summary>
        public static void NaInterface(Control c, MethodInvoker m)
        {
            try
            {
                if (c.IsDisposed) return;
                // sem handle ainda: nada de mexer no controle da thread do servico; vai para a da interface
                if (!c.IsHandleCreated) { Motion.NaInterface(delegate { if (!c.IsDisposed) m(); }); return; }
                if (c.InvokeRequired) c.BeginInvoke(m); else m();
            }
            catch (InvalidOperationException) { }
        }

        public static string Maiusculas(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.ToUpper(T.Pt ? new CultureInfo("pt-BR") : CultureInfo.InvariantCulture);
        }

        /// <summary>"SEG 26/10 · 22:20" (en: "MON 10/26 · 22:20").</summary>
        public static string Data(DateTime d)
        {
            CultureInfo c = new CultureInfo(T.Pt ? "pt-BR" : "en-US");
            string dia = c.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek).TrimEnd('.').ToUpper(c);
            return dia + " " + d.ToString(T.Pt ? "dd/MM" : "MM/dd", c) + " · " + d.ToString("HH:mm", c);
        }

        // ---------------- nome de quem usa ----------------

        private static string _nome;
        private static bool _buscou;

        /// <summary>O nome completo chegou da consulta (em segundo plano): o cabecalho repinta na hora, sem esperar o relogio de 15 s.</summary>
        public static event EventHandler NomeChegou;

        /// <summary>
        /// O primeiro nome do usuario do Windows. A conta local guarda o nome
        /// completo (numa conta Microsoft, o da conta); o nome de login fica
        /// como reserva enquanto a consulta, em segundo plano, nao volta.
        /// </summary>
        public static string NomeDoUsuario
        {
            get
            {
                if (!_buscou)
                {
                    _buscou = true;
                    _nome = PrimeiroNome(Environment.UserName);
                    Thread t = new Thread(BuscarNome);
                    t.IsBackground = true;
                    t.Start();
                }
                return _nome;
            }
        }

        private static void BuscarNome()
        {
            try
            {
                string login = Environment.UserName.Replace("\\", "\\\\").Replace("'", "\\'");
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT FullName FROM Win32_UserAccount WHERE LocalAccount=True AND Name='" + login + "'"))
                    foreach (ManagementBaseObject o in s.Get())
                    {
                        string f = o["FullName"] as string;
                        if (!string.IsNullOrEmpty(f) && f.Trim().Length > 0) { _nome = PrimeiroNome(f); EventHandler h = NomeChegou; if (h != null) h(null, EventArgs.Empty); break; }
                    }
            }
            catch (Exception ex) { Log.Error("nome do usuario", ex); }
        }

        internal static string PrimeiroNome(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string p = s.Trim().Split(new[] { ' ', '.', '_' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return p.Length == 0 ? "" : char.ToUpper(p[0]) + p.Substring(1);
        }
    }

    /// <summary>
    /// O cartao do cooler. Na Visao geral nova pinta tambem "● conectado" no
    /// titulo e a linha fina que separa a previa das duas curvas.
    /// </summary>
    internal sealed class CartaoDoCooler : Card
    {
        public bool Nova, Conectado;
        public int Linha = -1;

        internal string Estado { get { return "● " + (Conectado ? T.VisaoConectado : T.VisaoDesconectado); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!Nova) return;
            Graphics g = e.Graphics;
            int m = Ui.S(14);
            string txt = Conectado ? T.VisaoConectado : T.VisaoDesconectado;
            Size st = TextRenderer.MeasureText(txt, Ui.FontSmall);
            Size sp = TextRenderer.MeasureText("●", Ui.FontSmall);
            int y = (TitleHeight - st.Height) / 2;
            TextRenderer.DrawText(g, txt, Ui.FontSmall, new Point(Width - m - st.Width, y), Ui.Muted, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "●", Ui.FontSmall, new Point(Width - m - st.Width - sp.Width + Ui.S(4), y), Conectado ? Visao.Verde : Ui.Faint, TextFormatFlags.NoPrefix);
            if (Linha > 0)
                using (Pen p = new Pen(Ui.Border)) g.DrawLine(p, m, Linha, Width - m, Linha);
        }
    }



    public static partial class T
    {
        public static string VisaoNovaOpcao { get { return P("Usar a Visão geral clássica", "Use the classic overview"); } }
        public static string VisaoNovaNota { get { return P("A de antes: o display do cooler em destaque e a casa numa faixa embaixo.", "The previous layout: the cooler display up front and the home in a strip below."); } }
        public static string VisaoChipPerifericos { get { return P("Periféricos", "Peripherals"); } }
        public static string VisaoChipCooler { get { return P("Display do cooler", "Cooler display"); } }
        public static string VisaoNenhum { get { return P("nenhum conectado", "none connected"); } }
        public static string VisaoTudoEmOrdem(string nome) { return string.IsNullOrEmpty(nome) ? P("Tudo em ordem.", "All good.") : string.Format(P("Tudo em ordem, {0}.", "All good, {0}."), nome); }
        public static string VisaoAtencaoLavadora { get { return P("A lavadora acusou erro", "The washer reported an error"); } }
        public static string VisaoAtencaoAr { get { return P("O ar-condicionado acusou falha", "The air conditioner reported a fault"); } }
        public static string VisaoAtencaoBateria(string nome) { return string.Format(P("Bateria baixa no {0}", "{0} battery is low"), nome); }
        public static string VisaoRoupaPronta(string h) { return string.Format(P("A roupa fica pronta às {0}", "Laundry done at {0}"), h); }
        public static string VisaoAmbienteNome { get { return P("ambiente", "indoors"); } }
        public static string VisaoEventos { get { return P("EVENTOS", "EVENTS"); } }
        public static string VisaoSemEventos { get { return P("Nada novo desde que o app abriu.", "Nothing new since the app started."); } }
        public static string VisaoBaterias { get { return P("Baterias ▸", "Batteries ▸"); } }
        public static string VisaoAbrirPaineis { get { return P("Abrir Painéis ▸", "Open Panels ▸"); } }
        public static string VisaoAbrirCasa { get { return P("Abrir Casa ▸", "Open Home ▸"); } }
        public static string VisaoConectado { get { return P("conectado", "connected"); } }
        public static string VisaoDesconectado { get { return P("desconectado", "disconnected"); } }
        public static string VisaoConecteCasa { get { return P("Conecte a casa", "Connect your home"); } }
        public static string VisaoSemLeitura { get { return P("sem leitura", "no reading"); } }
        public static string VisaoLeituraAs(string h) { return P("leitura às ", "read at ") + h; }
        public static string VisaoFim(string h) { return P("fim ", "ends ") + h; }

        public static string DashboardMetricMono(int index, string fallback)
        {
            switch (index)
            {
                case 0: return P("CPU · TEMPERATURA", "CPU · TEMPERATURE");
                case 1: return P("CPU · USO", "CPU · USAGE");
                case 2: return P("GPU · TEMPERATURA", "GPU · TEMPERATURE");
                case 3: return P("GPU · USO", "GPU · USAGE");
                case 4: return P("MEMÓRIA · USO", "MEMORY · USAGE");
                default: return Visao.Maiusculas(fallback);
            }
        }

        public static string EvArLigado(string t) { return P("Ar ligado · ", "AC on · ") + t; }
        public static string EvArDesligado { get { return P("Ar desligado", "AC off"); } }
        public static string EvArTemp(string t) { return P("Ar ajustado para ", "AC set to ") + t; }
        public static string EvArModo(string m) { return P("Ar no modo ", "AC mode: ") + m; }
        public static string EvArOffline { get { return P("Ar desconectado", "AC went offline"); } }
        public static string EvLavOffline { get { return P("Lavadora desconectada", "Washer went offline"); } }
        public static string EvLavInicio(string p) { return P("Lavadora começou: ", "Washer started: ") + p; }
        public static string EvLavFase(string f) { return P("Lavadora: ", "Washer: ") + f; }
        public static string EvLavFim { get { return P("Lavadora terminou o ciclo", "Washer finished"); } }
        public static string EvLavErro(int c) { return P("Lavadora com erro ", "Washer error ") + c; }
        public static string EvBateria(string n, int p) { return P("Bateria baixa: ", "Low battery: ") + n + " " + p + "%"; }
    }
}
