using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// "Mostrar a casa no widget": a opcao mora no widget.ini do widget de
    /// bateria (ConfigDoWidget.Casa); a bandeja entrega aqui o controle dele
    /// para a aba Ajustes da Casa ligar e desligar sem conhecer a bandeja.
    /// </summary>
    internal static class CasaNoWidget
    {
        public static IControleDoWidget Controle;

        /// <summary>Mudou de ligado para desligado (Ajustes e pagina Baterias acompanham).</summary>
        public static event EventHandler Mudou;

        public static bool Ativo { get { return Controle != null && Controle.ConfigDoWidget.Casa; } }

        public static void Definir(bool ativo)
        {
            if (Controle == null) return;
            Controle.ConfigDoWidget.Casa = ativo;
            // pedir a casa no widget com ele desligado liga o widget
            if (ativo && !Controle.ConfigDoWidget.Ativo) Controle.LigarWidget(true);
            Controle.WidgetMudou();
            Avisar();
        }

        public static void Avisar() { if (Mudou != null) Mudou(null, EventArgs.Empty); }

        /// <summary>Clique na casa dentro do widget: a bandeja abre a pagina Casa.</summary>
        public static Action AbrirCasa;

        /// <summary>Clique no tempo dentro do widget: a bandeja abre a pagina Casa na aba Clima.</summary>
        public static Action AbrirClima;

        // ---------------- migracao do widget separado ----------------

        /// <summary>O widget-casa.ini do widget separado (2.29.0 de teste); os testes apontam para uma pasta temporaria.</summary>
        internal static string CaminhoAntigo
        {
            get { return _caminhoAntigo ?? Path.Combine(Paths.DataDir, "widget-casa.ini"); }
            set { _caminhoAntigo = value; }
        }
        private static string _caminhoAntigo;

        /// <summary>
        /// O widget da casa virou parte do de bateria. Quem tinha o separado
        /// ligado ganha o de bateria ligado com a casa; a posicao dele nao vem
        /// (o de bateria ja tem a sua). O arquivo vira .migrado: nada se perde
        /// e a migracao nao se repete. Devolve true se mudou a config.
        /// </summary>
        internal static bool Migrar(ConfigDoWidget cfg)
        {
            string arq = CaminhoAntigo;
            try
            {
                if (!File.Exists(arq)) return false;
                bool ativo = false;
                foreach (string linha in File.ReadAllLines(arq, Encoding.UTF8))
                    if (linha.Trim().ToLowerInvariant() == "ativo=1") ativo = true;
                string destino = arq + ".migrado";
                if (File.Exists(destino)) File.Delete(destino);
                File.Move(arq, destino);
                Log.Write("widget da casa separado migrado para o widget de bateria (estava " + (ativo ? "ligado" : "desligado") + ")");
                if (!ativo) return false;
                cfg.Ativo = true;
                cfg.Casa = true;
                return true;
            }
            catch (Exception ex) { Log.Error("migrar o widget da casa", ex); return false; }
        }
    }

    /// <summary>
    /// Os aparelhos da casa como itens do widget de bateria, depois dos
    /// perifericos: o ar sempre (anel com o alvo, - e + dos lados e o
    /// liga/desliga no lugar do selo; na lista, uma linha com bolhas pequenas)
    /// e a lavadora so com ciclo ativo (anel ou capsula do progresso).
    /// Desenha com as pecas e medidas do PintorDoWidget; quem guarda a janela,
    /// o lugar e o menu e o WidgetDeBateria.
    /// </summary>
    internal sealed class ItensDaCasa : IDisposable
    {
        internal enum Tipo { Ar, Tempo, Lavadora }

        /// <summary>Bolhas de - e + (e do liga/desliga na lista): o tamanho do selo de carga, um pouco maior para o dedo.</summary>
        private const float Mini = 24f, VaoMini = 6f;

        private Aparelho _ar, _lav;
        /// <summary>O tempo da cidade da casa, do servidor (SugestaoCasa.Tempo), ou nulo.</summary>
        private Dictionary<string, object> _tempo;
        private RectangleF _rTempo;
        private int _alvoPendente = -1;
        private int _hover = -1;
        private readonly Timer _espera = new Timer();
        private RectangleF _rMenos, _rMais, _rLiga;
        private readonly List<RectangleF> _zonas = new List<RectangleF>();

        /// <summary>Desligado pela opcao "Mostrar a casa no widget": nenhum item.</summary>
        public bool Ativa = true;

        /// <summary>O widget redesenha (mudou o alvo pendente, mandou um comando).</summary>
        public Action Redesenhar;

        internal Aparelho Ar { get { return _ar; } }
        internal Aparelho Lavadora { get { return _lav; } }
        internal Dictionary<string, string> UltimoComando;
        internal RectangleF ZonaMenos { get { return _rMenos; } }
        internal RectangleF ZonaMais { get { return _rMais; } }
        internal RectangleF ZonaLiga { get { return _rLiga; } }

        public ItensDaCasa()
        {
            _espera.Interval = 700;
            _espera.Tick += delegate
            {
                _espera.Stop();
                if (_alvoPendente < 0) return;
                int v = _alvoPendente;
                _alvoPendente = -1;
                Mandar("t_temp", v);
            };
            Puxar();
        }

        public void Dispose() { _espera.Dispose(); }

        /// <summary>Le o ar e a lavadora do servico (na thread da interface).</summary>
        public void Puxar()
        {
            _ar = null; _lav = null;
            _tempo = SugestaoCasa.Tempo();
            if (!ServicoCasa.Logado) return;
            foreach (Aparelho a in ServicoCasa.Aparelhos)
            {
                if (_ar == null && a.ArCondicionado) _ar = a;
                if (_lav == null && a.Lavadora) _lav = a;
            }
        }

        /// <summary>
        /// A lavadora so entra online e em ciclo: em funcionamento, pausada ou
        /// aguardando o inicio programado. Parada, concluida, com erro fora de
        /// ciclo, desligada ou desconectada, ela sai (vale para a Visao geral tambem).
        /// </summary>
        internal static bool CicloAtivo(Aparelho lav)
        {
            if (lav == null) return false;
            CicloAtual c = CicloAtual.De(lav);
            // so com a maquina online e em ciclo; parada, pronta ou desconectada nao aparece
            return c.Online && c.Rodando;
        }

        /// <summary>Os itens que o widget mostra agora, na ordem.</summary>
        public Tipo[] Itens()
        {
            List<Tipo> l = new List<Tipo>();
            if (Ativa && ServicoCasa.Logado && _ar != null) l.Add(Tipo.Ar);
            if (Ativa && _tempo != null) l.Add(Tipo.Tempo);
            if (Ativa && ServicoCasa.Logado && CicloAtivo(_lav)) l.Add(Tipo.Lavadora);
            return l.ToArray();
        }

        /// <summary>Coluna do ar nos aneis: o anel com uma bolha de - e de + de cada lado.</summary>
        internal static float ColunaDoAr { get { return PintorDoWidget.Anel + 2f * (Mini + 4f); } }

        /// <summary>Largura de cada item a 100%, como PintorDoWidget.Larguras faz com os perifericos.</summary>
        public float[] Larguras(LayoutDoWidget layout)
        {
            Tipo[] t = Itens();
            float[] l = new float[t.Length];
            for (int i = 0; i < l.Length; i++)
                l[i] = layout == LayoutDoWidget.Lista ? PintorDoWidget.LarguraDaLista
                     : t[i] == Tipo.Ar ? ColunaDoAr : PintorDoWidget.Coluna;
            return l;
        }

        // ---------------- comandos ----------------

        private void FaixaDoAlvo(out int min, out int max)
        {
            CenaCasa.FaixaC(_ar, out min, out max);
            if (_ar.Int("t_temp_type", 0) == 1) { min = (int)Math.Round(min * 9.0 / 5 + 32); max = (int)Math.Round(max * 9.0 / 5 + 32); }
        }

        private void Mudar(int passo)
        {
            if (_ar == null || !_ar.Online || _ar.Int("t_power", 0) == 0) return;
            int min, max;
            FaixaDoAlvo(out min, out max);
            int atual = _alvoPendente >= 0 ? _alvoPendente : _ar.Int("t_temp", 24);
            _alvoPendente = Math.Max(min, Math.Min(max, atual + passo));
            _espera.Stop(); _espera.Start();
            if (Redesenhar != null) Redesenhar();
        }

        private void Mandar(string chave, int valor)
        {
            if (_ar == null) return;
            Dictionary<string, string> p = new Dictionary<string, string>();
            p[chave] = valor.ToString(CultureInfo.InvariantCulture);
            _ar.Estado[chave] = p[chave];
            if (ServicoCasa.RetornoPuid == _ar.Puid) ServicoCasa.CancelarRetorno();
            UltimoComando = p;
            if (Redesenhar != null) Redesenhar();
            ServicoCasa.Enviar(_ar.Puid, p, null);
        }

        // ---------------- mouse ----------------

        /// <summary>0 menos, 1 mais, 2 liga/desliga, 4 o tempo, 3 em cima de outro item da casa, -1 fora deles.</summary>
        public int Zona(Point p)
        {
            if (_rMenos.Contains(p)) return 0;
            if (_rMais.Contains(p)) return 1;
            if (_rLiga.Contains(p)) return 2;
            if (_rTempo.Contains(p)) return 4;
            foreach (RectangleF z in _zonas) if (z.Contains(p)) return 3;
            return -1;
        }

        /// <summary>Os botoes; o 3 (o resto do item) abre a pagina Casa.</summary>
        public void Clicar(int zona)
        {
            switch (zona)
            {
                case 0: Mudar(-1); break;
                case 1: Mudar(+1); break;
                case 2: if (_ar != null && _ar.Online) Mandar("t_power", _ar.Int("t_power", 0) != 0 ? 0 : 1); break;
                case 3: if (CasaNoWidget.AbrirCasa != null) CasaNoWidget.AbrirCasa(); break;
                case 4:
                    if (CasaNoWidget.AbrirClima != null) CasaNoWidget.AbrirClima();
                    else if (CasaNoWidget.AbrirCasa != null) CasaNoWidget.AbrirCasa();
                    break;
            }
        }

        /// <summary>Acende o botao sob o ponteiro; devolve true se precisa redesenhar.</summary>
        public bool Sobre(int zona)
        {
            int h = zona >= 0 && zona < 3 ? zona : -1;
            if (h == _hover) return false;
            _hover = h;
            return true;
        }

        /// <summary>A dica do mouse: o que nao cabe na placa.</summary>
        public string Dica(int zona)
        {
            if (zona == 0) return T.CasaMenosGrau;
            if (zona == 1) return T.CasaMaisGrau;
            if (zona == 2) return T.CasaLigaDesliga;
            if (zona == 4 && _tempo != null) return LinhaDoTempo(true) + "\n" + T.CasaAbrirClima;
            StringBuilder sb = new StringBuilder();
            if (_ar != null) sb.Append(_ar.Nome).Append("  ").Append(LinhaDoAr()).Append('\n');
            if (CicloAtivo(_lav)) sb.Append(_lav.Nome).Append("  ").Append(LinhaDaLavadora(CicloAtual.De(_lav))).Append('\n');
            return sb.Append(T.CasaResumoConvite).ToString();
        }

        // ---------------- pintura ----------------

        /// <summary>Antes de cada quadro: as zonas sao as do desenho que vem.</summary>
        public void ComecarQuadro()
        {
            _rMenos = _rMais = _rLiga = _rTempo = RectangleF.Empty;
            _zonas.Clear();
        }

        public void Pintar(Graphics g, Tipo t, RectangleF z, LayoutDoWidget layout, float s, bool escuro, PintorDoWidget.Fontes f)
        {
            _zonas.Add(z);
            using (Font glifo = new Font("Segoe MDL2 Assets", 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font peq = new Font("Segoe MDL2 Assets", 11f * s, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                if (t == Tipo.Tempo)
                {
                    _rTempo = z;
                    if (layout == LayoutDoWidget.Lista) TempoNaLista(g, z, s, f, escuro);
                    else TempoNoAnel(g, z, s, f, escuro);
                }
                else if (layout == LayoutDoWidget.Lista)
                {
                    if (t == Tipo.Ar) ArNaLista(g, z, s, f, glifo, peq, escuro);
                    else LavadoraNaLista(g, z, s, f, escuro);
                }
                else
                {
                    if (t == Tipo.Ar) ArNoAnel(g, z, s, f, glifo, peq, escuro);
                    else LavadoraNoAnel(g, z, s, f, escuro);
                }
            }
        }

        private string LinhaDoAr()
        {
            bool on = _ar.Online, ligado = on && _ar.Int("t_power", 0) != 0;
            string amb = T.ClimaLaFora + " " + Clima.Texto() + " · ";
            return amb + (!on ? T.CasaOffline : ligado ? TabelaDoAr.Modo(_ar.Int("t_work_mode", -1), _ar.Tipo) : T.CasaDesligado);
        }

        private static string LinhaDaLavadora(CicloAtual c)
        {
            string fase = c.Titulo();
            if (c.Restante > 0 && !c.Erro && !c.Pausada && !c.Programado)
            {
                DateTime leitura = ServicoCasa.UltimaLeitura == DateTime.MinValue ? DateTime.Now : ServicoCasa.UltimaLeitura;
                fase += " · " + leitura.AddMinutes(c.Restante).ToString("HH:mm");
            }
            return fase;
        }

        private static string Tempo(CicloAtual c)
        {
            return c.Restante > 0 && !c.Erro ? CiclosDaLavadora.Duracao(TimeSpan.FromMinutes(c.Restante)) : "--";
        }

        private static Color CorDoCiclo(CicloAtual c)
        {
            return c.Erro ? Ui.Danger : c.Pausada ? Ui.Warn : Ui.Accent;
        }

        private int Alvo { get { return _alvoPendente >= 0 ? _alvoPendente : _ar.Int("t_temp", 0); } }

        private readonly ArAnimadoNoWidget _anim = new ArAnimadoNoWidget();

        /// <summary>O widget precisa de quadros: alguma animacao do ar esta correndo.</summary>
        internal bool Animando { get { return Ativa && _ar != null && _anim.Animando; } }

        internal ArAnimadoNoWidget Animacao { get { return _anim; } }

        /// <summary>Ao ligar, um anel que cresce e some em volta de r; ao desligar, um anel cinza que se fecha.</summary>
        private void AnelDoPulso(Graphics g, RectangleF r, float st)
        {
            float pu = _anim.Pulso, ap = _anim.Apagando;
            if (pu >= 0f)
            {
                float k = r.Width * 0.3f * pu;
                using (Pen p = new Pen(Color.FromArgb((int)(200 * (1f - pu)), Ui.Accent), Math.Max(2f, st * 0.8f)))
                    g.DrawEllipse(p, RectangleF.Inflate(r, k, k));
            }
            if (ap >= 0f)
            {
                float k = r.Width * 0.3f * (1f - ap);
                using (Pen p = new Pen(Color.FromArgb((int)(170 * (1f - ap)), 160, 160, 170), Math.Max(2f, st * 0.8f)))
                    g.DrawEllipse(p, RectangleF.Inflate(r, k, k));
            }
        }

        /// <summary>O alvo do ar; quando muda, o velho sai e o novo entra deslizando (sobe: por baixo), com fade.</summary>
        private void NumeroDoAr(Graphics g, string valor, Font fonte, Color cor, bool escuro, RectangleF r, StringAlignment alinhar)
        {
            float v = _anim.Troca;
            if (v < 0f || _anim.Antigo == null)
            {
                PintorDoWidget.Texto(g, valor, fonte, cor, escuro, r, alinhar, StringAlignment.Center);
                return;
            }
            float curso = r.Height * 0.6f;
            int dir = _anim.Direcao;
            GraphicsState st = g.Save();
            g.SetClip(RectangleF.Inflate(r, 4f, 2f));
            PintorDoWidget.Texto(g, _anim.Antigo, fonte, Color.FromArgb((int)(cor.A * (1f - v)), cor), escuro, new RectangleF(r.X, r.Y - dir * curso * v, r.Width, r.Height), alinhar, StringAlignment.Center);
            PintorDoWidget.Texto(g, valor, fonte, Color.FromArgb((int)(cor.A * v), cor), escuro, new RectangleF(r.X, r.Y + dir * curso * (1f - v), r.Width, r.Height), alinhar, StringAlignment.Center);
            g.Restore(st);
        }


        private static void Glifo(Graphics g, string texto, Font fonte, RectangleF area, Color cor)
        {
            using (GraphicsPath gp = BotaoRedondo.Glifo(texto, fonte, area))
            using (SolidBrush b = new SolidBrush(cor)) g.FillPath(b, gp);
        }

        /// <summary>Bolha pequena de vidro; acesa (o ar ligado), na cor de destaque.</summary>
        private void Botao(Graphics g, RectangleF r, string glifo, Font fonte, bool aceso, bool ativo, int zona, bool escuro)
        {
            if (aceso)
                using (SolidBrush b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, r);
            else
            {
                PintorDoWidget.Bolha(g, r, escuro);
                // claro e com aro, para - e + nao sumirem no vidro
                using (SolidBrush b = new SolidBrush(Color.FromArgb(ativo ? 70 : 30, 255, 255, 255))) g.FillEllipse(b, r);
                using (Pen aro = new Pen(Color.FromArgb(ativo ? 170 : 70, 255, 255, 255), Math.Max(1f, r.Width / 16f))) g.DrawEllipse(aro, RectangleF.Inflate(r, -0.5f, -0.5f));
                if (ativo && _hover == zona)
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(escuro ? 40 : 60, 255, 255, 255))) g.FillEllipse(b, r);
            }
            Color cg = ativo || aceso ? Color.White : Color.FromArgb(110, 255, 255, 255);
            bool mais;
            if (IconesCasa.EhSinal(glifo, out mais)) IconesCasa.Sinal(g, r, mais, cg, Math.Max(1.5f, r.Width / 14f));
            else Glifo(g, glifo, fonte, r, cg);
        }

        /// <summary>O anel do widget de bateria: trilho apagado na cor, arco com brilho suave por baixo.</summary>
        private static void Anel(Graphics g, RectangleF anel, float st, float fracao, Color cor, float acende, bool escuro)
        {
            // acende vai de 0 (apagado) a 1 (aceso): no ar ele anda em 450 ms ao ligar e desligar
            Color apagado = PintorDoWidget.Trilho(escuro);
            Color trilho = Ui.Misturar(apagado, Color.FromArgb(escuro ? 70 : 60, cor), acende);
            using (Pen pen = new Pen(trilho, st)) g.DrawEllipse(pen, anel);
            if (acende <= 0.01f || fracao <= 0f) return;
            float arco = 360f * Math.Min(1f, fracao);
            using (Pen luz = PintorDoWidget.Redonda(Color.FromArgb((int)(55 * acende), cor), st * 2f)) g.DrawArc(luz, anel, -90f, arco);
            using (Pen pen = PintorDoWidget.Redonda(Color.FromArgb((int)(255 * acende), cor), st)) g.DrawArc(pen, anel, -90f, arco);
        }

        // ---------------- formato aneis ----------------

        /// <summary>
        /// O ar como um item de anel: o arco e o alvo dentro da faixa do aparelho,
        /// o termometro na bolha, o alvo embaixo (onde o de bateria poe a
        /// porcentagem), - e + dos lados e o liga/desliga no lugar do selo de carga.
        /// </summary>
        private void ArNoAnel(Graphics g, RectangleF z, float s, PintorDoWidget.Fontes f, Font glifo, Font peq, bool escuro)
        {
            float d = PintorDoWidget.Anel * s, st = PintorDoWidget.Traco * s;
            bool on = _ar.Online, ligado = on && _ar.Int("t_power", 0) != 0;
            float cx = z.X + z.Width / 2f;
            RectangleF anel = new RectangleF(cx - d / 2f + st / 2f, z.Y + st / 2f, d - st, d - st);
            int t = Alvo, min, max;
            FaixaDoAlvo(out min, out max);
            float fracao = max > min && t > 0 ? 0.15f + 0.85f * (t - min) / (float)(max - min) : 0f;
            // ligar, desligar e o alvo andam por relogio (ArAnimadoNoWidget): o arco acende e corre ate o alvo novo
            _anim.Observar(ligado, t, fracao);
            float lig = _anim.Lig;
            Anel(g, anel, st, _anim.Fracao, Ui.Accent, lig, escuro);
            AnelDoPulso(g, anel, st);
            float db = d - 2f * st - 6f * s;
            RectangleF bolha = new RectangleF(cx - db / 2f, anel.Y + anel.Height / 2f - db / 2f, db, db);
            PintorDoWidget.Bolha(g, bolha, escuro);
            Glifo(g, "\uE9CA", glifo, bolha, Color.FromArgb(150 + (int)(105 * lig), 255, 255, 255));

            float mini = Mini * s, cy = anel.Y + anel.Height / 2f;
            _rMenos = new RectangleF(z.X, cy - mini / 2f, mini, mini);
            _rMais = new RectangleF(z.Right - mini, cy - mini / 2f, mini, mini);
            float r = anel.Width / 2f, ds = 19f * s;   // o selo do widget de bateria
            _rLiga = new RectangleF(cx + r * 0.72f - ds / 2f, cy + r * 0.72f - ds / 2f, ds, ds);
            Botao(g, _rMenos, "\uE738", peq, false, ligado, 0, escuro);
            Botao(g, _rMais, "\uE710", peq, false, ligado, 1, escuro);
            Botao(g, _rLiga, "\uE7E8", peq, ligado, on, 2, escuro);

            RectangleF lValor = new RectangleF(z.X, z.Y + d + 7f * s, z.Width, PintorDoWidget.LinhaDoValor * s);
            // O alvo grande e, ao lado e menor, a temperatura do escritorio agora (a ambiente lida pelo ar).
            string alvoTxt = t > 0 ? t + "°" : "--";
            string amb = Ambiente();
            float wAlvo, wAmb, gap = 5f * s;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            {
                wAlvo = g.MeasureString(alvoTxt, f.Pct, PointF.Empty, sf).Width;
                wAmb = g.MeasureString(amb, f.Nota, PointF.Empty, sf).Width;
            }
            float x0 = z.X + (z.Width - wAlvo - gap - wAmb) / 2f;
            if (x0 < z.X) x0 = z.X;
            NumeroDoAr(g, alvoTxt, f.Pct, Ui.Misturar(PintorDoWidget.Secundario(escuro), PintorDoWidget.Primario(escuro), lig), escuro,
                new RectangleF(x0, lValor.Y, wAlvo + 2f * s, lValor.Height), StringAlignment.Near);
            // na mesma linha de base do alvo (centrado na altura da linha): sobe ou desce pela diferenca de subida das fontes
            float hAlvo = f.Pct.GetHeight(g), hAmb = f.Nota.GetHeight(g);
            float baseAlvo = lValor.Y + (lValor.Height - hAlvo) / 2f + SubidaF(g, f.Pct);
            float yAmb = baseAlvo - SubidaF(g, f.Nota);
            PintorDoWidget.Texto(g, amb, f.Nota, PintorDoWidget.Secundario(escuro), escuro,
                new RectangleF(x0 + wAlvo + gap, yAmb, Math.Max(0f, z.Right - (x0 + wAlvo + gap)), hAmb), StringAlignment.Near, StringAlignment.Near);
        }

        private static float SubidaF(Graphics g, Font f)
        {
            FontFamily fam = f.FontFamily;
            return f.SizeInPoints * g.DpiY / 72f * fam.GetCellAscent(f.Style) / fam.GetEmHeight(f.Style);
        }

        /// <summary>A temperatura la fora, curta ("28,4°"); sem cidade ou sem leitura, "--".</summary>
        internal string Ambiente()
        {
            // A temperatura la fora (Open-Meteo), e nao a do escritorio: "28,4°", ou "--".
            return Clima.Curto();
        }

        /// <summary>A lavadora como um item de anel: o arco e o progresso do ciclo, o tempo restante embaixo.</summary>
        private void LavadoraNoAnel(Graphics g, RectangleF z, float s, PintorDoWidget.Fontes f, bool escuro)
        {
            float d = PintorDoWidget.Anel * s, st = PintorDoWidget.Traco * s;
            CicloAtual c = CicloAtual.De(_lav);
            float cx = z.X + z.Width / 2f;
            RectangleF anel = new RectangleF(cx - d / 2f + st / 2f, z.Y + st / 2f, d - st, d - st);
            Anel(g, anel, st, c.Erro ? 1f : c.Progresso, CorDoCiclo(c), 1f, escuro);
            float db = d - 2f * st - 6f * s;
            RectangleF bolha = new RectangleF(cx - db / 2f, anel.Y + anel.Height / 2f - db / 2f, db, db);
            PintorDoWidget.Bolha(g, bolha, escuro);
            IconeDaLavadora(g, bolha, s);
            RectangleF lValor = new RectangleF(z.X - 4f * s, z.Y + d + 7f * s, z.Width + 8f * s, PintorDoWidget.LinhaDoValor * s);
            PintorDoWidget.Texto(g, Tempo(c), f.Pct, PintorDoWidget.Primario(escuro), escuro, lValor, StringAlignment.Center, StringAlignment.Center);
        }

        // ---------------- formato lista ----------------

        /// <summary>
        /// O ar como uma linha da lista: nome e alvo na linha de cima (como nome
        /// e porcentagem), ambiente e modo embaixo, e - + e liga/desliga em bolhas pequenas.
        /// </summary>
        private void ArNaLista(Graphics g, RectangleF z, float s, PintorDoWidget.Fontes f, Font glifo, Font peq, bool escuro)
        {
            bool on = _ar.Online, ligado = on && _ar.Int("t_power", 0) != 0;
            float db = PintorDoWidget.BolhaDaLista * s;
            RectangleF bolha = new RectangleF(z.X, z.Y + (z.Height - db) / 2f, db, db);
            PintorDoWidget.Bolha(g, bolha, escuro);
            _anim.Observar(ligado, Alvo, 0f);
            float lig = _anim.Lig;
            AnelDoPulso(g, bolha, 2f * s);
            Glifo(g, "\uE9CA", glifo, bolha, Color.FromArgb(150 + (int)(105 * lig), 255, 255, 255));

            float x = bolha.Right + 12f * s;
            int t = Alvo;
            string valor = t > 0 ? t + "°" : "--";
            float largValor;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
                largValor = g.MeasureString(valor, f.Pct, PointF.Empty, sf).Width + 2f * s;
            RectangleF lValor = new RectangleF(z.Right - largValor, z.Y, largValor, 21f * s);
            NumeroDoAr(g, valor, f.Pct, Ui.Misturar(PintorDoWidget.Secundario(escuro), PintorDoWidget.Primario(escuro), lig), escuro,
                lValor, StringAlignment.Far);
            PintorDoWidget.Texto(g, _ar.Nome, f.Nome, PintorDoWidget.Primario(escuro), escuro,
                new RectangleF(x, z.Y, Math.Max(0f, lValor.X - x - 6f * s), 21f * s), StringAlignment.Near, StringAlignment.Center);

            float mini = Mini * s, vao = VaoMini * s, yb = z.Bottom - mini;
            _rLiga = new RectangleF(z.Right - mini, yb, mini, mini);
            _rMais = new RectangleF(_rLiga.X - vao - mini, yb, mini, mini);
            _rMenos = new RectangleF(_rMais.X - vao - mini, yb, mini, mini);
            Botao(g, _rMenos, "\uE738", peq, false, ligado, 0, escuro);
            Botao(g, _rMais, "\uE710", peq, false, ligado, 1, escuro);
            Botao(g, _rLiga, "\uE7E8", peq, ligado, on, 2, escuro);
            PintorDoWidget.Texto(g, LinhaDoAr(), f.Nota, PintorDoWidget.Secundario(escuro), escuro,
                new RectangleF(x, yb, Math.Max(0f, _rMenos.X - x - 6f * s), mini), StringAlignment.Near, StringAlignment.Center);
        }

        /// <summary>A lavadora como uma linha da lista: nome e tempo, a capsula do progresso e a fase embaixo.</summary>
        private void LavadoraNaLista(Graphics g, RectangleF z, float s, PintorDoWidget.Fontes f, bool escuro)
        {
            CicloAtual c = CicloAtual.De(_lav);
            float db = PintorDoWidget.BolhaDaLista * s;
            RectangleF bolha = new RectangleF(z.X, z.Y + (z.Height - db) / 2f, db, db);
            PintorDoWidget.Bolha(g, bolha, escuro);
            IconeDaLavadora(g, bolha, s);

            float x = bolha.Right + 12f * s, w = z.Right - x;
            string valor = Tempo(c);
            float largValor;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
                largValor = g.MeasureString(valor, f.Pct, PointF.Empty, sf).Width + 2f * s;
            RectangleF lValor = new RectangleF(z.Right - largValor, z.Y, largValor, 21f * s);
            PintorDoWidget.Texto(g, valor, f.Pct, PintorDoWidget.Primario(escuro), escuro, lValor, StringAlignment.Far, StringAlignment.Center);
            PintorDoWidget.Texto(g, _lav.Nome, f.Nome, PintorDoWidget.Primario(escuro), escuro,
                new RectangleF(x, z.Y, Math.Max(0f, lValor.X - x - 6f * s), 21f * s), StringAlignment.Near, StringAlignment.Center);

            // a mesma capsula do widget de bateria, na espessura do traco dos aneis
            float h = PintorDoWidget.Traco * s, cy = z.Y + 29f * s;
            Color cor = CorDoCiclo(c);
            using (Pen trilho = PintorDoWidget.Redonda(Color.FromArgb(escuro ? 70 : 60, cor), h)) g.DrawLine(trilho, x + h / 2f, cy, z.Right - h / 2f, cy);
            float wp = (w - h) * (c.Erro ? 1f : Math.Max(0f, Math.Min(1f, c.Progresso)));
            if (wp > 0.5f)
            {
                using (Pen luz = PintorDoWidget.Redonda(Color.FromArgb(55, cor), h * 2f)) g.DrawLine(luz, x + h / 2f, cy, x + h / 2f + wp, cy);
                using (Pen pen = PintorDoWidget.Redonda(cor, h)) g.DrawLine(pen, x + h / 2f, cy, x + h / 2f + wp, cy);
            }
            PintorDoWidget.Texto(g, LinhaDaLavadora(c), f.Nota, c.Erro ? Color.FromArgb(255, 255, 120, 110) : PintorDoWidget.Primario(escuro), escuro,
                new RectangleF(x, cy + h / 2f + 2f * s, w, 17f * s), StringAlignment.Near, StringAlignment.Center);
        }

        // ---------------- o tempo ----------------

        private string TempoTexto(string chave) { return Json.Texto(_tempo, chave) ?? ""; }

        private string Graus(string chave) { string v = Json.Texto(_tempo, chave); return string.IsNullOrEmpty(v) ? "--" : v + "°"; }

        /// <summary>"Garoa · Máx 29° Mín 19° · AQI 91"; com o nome da cidade na frente para a dica.</summary>
        private string LinhaDoTempo(bool comCidade)
        {
            StringBuilder sb = new StringBuilder();
            if (comCidade) sb.Append(TempoTexto("local")).Append("  ").Append(Graus("temp")).Append(' ');
            sb.Append(TempoTexto("texto")).Append(" · ").Append(T.CasaMax).Append(' ').Append(Graus("max"))
              .Append(' ').Append(T.CasaMin).Append(' ').Append(Graus("min"));
            string aqi = Json.Texto(_tempo, "aqi");
            if (!string.IsNullOrEmpty(aqi)) sb.Append(" · AQI ").Append(aqi).Append(comCidade ? " " + TempoTexto("aqiTexto") : "");
            return sb.ToString();
        }

        /// <summary>A cor da faixa do AQI (ja validada como #RRGGBB pelo SugestaoCasa), ou cinza.</summary>
        private Color CorDoAqi()
        {
            string c = Json.Texto(_tempo, "aqiCor");
            try { if (c != null && c.Length == 7) return ColorTranslator.FromHtml(c); }
            catch (Exception) { }
            return Color.FromArgb(255, 136, 136, 136);
        }

        /// <summary>
        /// O tempo como um item de anel: o anel na cor da qualidade do ar (cheio na proporcao do AQI,
        /// ate 200), o icone do ceu na bolha e a temperatura de agora embaixo.
        /// </summary>
        private void TempoNoAnel(Graphics g, RectangleF z, float s, PintorDoWidget.Fontes f, bool escuro)
        {
            float d = PintorDoWidget.Anel * s, st = PintorDoWidget.Traco * s;
            float cx = z.X + z.Width / 2f;
            RectangleF anel = new RectangleF(cx - d / 2f + st / 2f, z.Y + st / 2f, d - st, d - st);
            double aqi;
            bool temAqi = double.TryParse(Json.Texto(_tempo, "aqi"), NumberStyles.Float, CultureInfo.InvariantCulture, out aqi);
            Anel(g, anel, st, temAqi ? (float)Math.Max(0.06, Math.Min(1.0, aqi / 200.0)) : 0f, CorDoAqi(), 1f, escuro);
            float db = d - 2f * st - 6f * s;
            RectangleF bolha = new RectangleF(cx - db / 2f, anel.Y + anel.Height / 2f - db / 2f, db, db);
            PintorDoWidget.Bolha(g, bolha, escuro);
            float m = bolha.Width * 0.2f;
            IconeDoTempo(g, RectangleF.Inflate(bolha, -m, -m), TempoTexto("icone"));
            RectangleF lValor = new RectangleF(z.X - 4f * s, z.Y + d + 7f * s, z.Width + 8f * s, PintorDoWidget.LinhaDoValor * s);
            PintorDoWidget.Texto(g, Graus("temp"), f.Pct, PintorDoWidget.Primario(escuro), escuro, lValor, StringAlignment.Center, StringAlignment.Center);
        }

        /// <summary>O tempo como uma linha da lista: a cidade e a temperatura em cima, condicao, maxima, minima e AQI embaixo.</summary>
        private void TempoNaLista(Graphics g, RectangleF z, float s, PintorDoWidget.Fontes f, bool escuro)
        {
            float db = PintorDoWidget.BolhaDaLista * s;
            RectangleF bolha = new RectangleF(z.X, z.Y + (z.Height - db) / 2f, db, db);
            PintorDoWidget.Bolha(g, bolha, escuro);
            float m = bolha.Width * 0.2f;
            IconeDoTempo(g, RectangleF.Inflate(bolha, -m, -m), TempoTexto("icone"));

            float x = bolha.Right + 12f * s;
            string valor = Graus("temp");
            float largValor;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
                largValor = g.MeasureString(valor, f.Pct, PointF.Empty, sf).Width + 2f * s;
            RectangleF lValor = new RectangleF(z.Right - largValor, z.Y, largValor, 21f * s);
            PintorDoWidget.Texto(g, valor, f.Pct, PintorDoWidget.Primario(escuro), escuro, lValor, StringAlignment.Far, StringAlignment.Center);
            PintorDoWidget.Texto(g, TempoTexto("local"), f.Nome, PintorDoWidget.Primario(escuro), escuro,
                new RectangleF(x, z.Y, Math.Max(0f, lValor.X - x - 6f * s), 21f * s), StringAlignment.Near, StringAlignment.Center);
            // o ponto na cor do AQI, antes da linha de baixo
            float yb = z.Bottom - 24f * s, r = 3.5f * s;
            float xt = x;
            if (!string.IsNullOrEmpty(Json.Texto(_tempo, "aqi")))
            {
                using (SolidBrush b = new SolidBrush(CorDoAqi())) g.FillEllipse(b, x, yb + 12f * s - r, 2f * r, 2f * r);
                xt = x + 2f * r + 5f * s;
            }
            PintorDoWidget.Texto(g, LinhaDoTempo(false), f.Nota, PintorDoWidget.Secundario(escuro), escuro,
                new RectangleF(xt, yb, Math.Max(0f, z.Right - xt), 24f * s), StringAlignment.Near, StringAlignment.Center);
        }

        /// <summary>
        /// O ceu desenhado (as mesmas figuras da pagina da Casa, cheias para ler pequeno): sol amarelo,
        /// lua clara, nuvem branca, gotas azuis, raio amarelo, flocos e neblina.
        /// </summary>
        internal static void IconeDoTempo(Graphics g, RectangleF r, string nome)
        {
            float u = r.Width / 24f;
            Func<float, float, PointF> P = delegate(float x, float y) { return new PointF(r.X + x * u, r.Y + y * u); };
            Color sol = Color.FromArgb(255, 255, 209, 92), lua = Color.FromArgb(255, 243, 227, 166);
            Color nuvem = Color.FromArgb(240, 255, 255, 255), gota = Color.FromArgb(255, 99, 177, 255);
            bool alta = nome == "neblina" || nome == "garoa" || nome == "chuva" || nome == "chuva-forte" || nome == "tempestade" || nome == "neve";
            if (nome == "sol" || nome == "sol-nuvem")
            {
                bool so = nome == "sol";
                float cx = so ? 12f : 9f, cy = so ? 12f : 9f, raio = so ? 4.6f : 3.4f, a = so ? 7f : 5.2f, b = so ? 9.6f : 7f;
                using (SolidBrush br = new SolidBrush(sol)) g.FillEllipse(br, r.X + (cx - raio) * u, r.Y + (cy - raio) * u, 2 * raio * u, 2 * raio * u);
                using (Pen p = PintorDoWidget.Redonda(sol, 1.7f * u))
                    for (int i = 0; i < 8; i++)
                    {
                        double ang = i * Math.PI / 4;
                        if (!so && i >= 1 && i <= 3) continue;    // os raios de baixo ficam atras da nuvem
                        g.DrawLine(p, P(cx + (float)Math.Cos(ang) * a, cy + (float)Math.Sin(ang) * a), P(cx + (float)Math.Cos(ang) * b, cy + (float)Math.Sin(ang) * b));
                    }
            }
            else if (nome == "lua" || nome == "lua-nuvem")
            {
                bool so = nome == "lua";
                float cx = so ? 12f : 8.5f, cy = so ? 12f : 8.5f, raio = so ? 7.5f : 5f;
                using (GraphicsPath cheia = new GraphicsPath())
                using (GraphicsPath corte = new GraphicsPath())
                using (Region reg = new Region())
                using (SolidBrush br = new SolidBrush(lua))
                {
                    cheia.AddEllipse(r.X + (cx - raio) * u, r.Y + (cy - raio) * u, 2 * raio * u, 2 * raio * u);
                    corte.AddEllipse(r.X + (cx - raio * 0.35f) * u, r.Y + (cy - raio * 1.25f) * u, 2 * raio * u, 2 * raio * u);
                    reg.MakeEmpty(); reg.Union(cheia); reg.Exclude(corte);
                    g.FillRegion(br, reg);
                }
            }
            if (nome == "sol" || nome == "lua") return;
            // a nuvem: tres bolas e a base, cheias juntas
            float dy = alta ? -3.4f : nome == "sol-nuvem" || nome == "lua-nuvem" ? 2f : 0f, dx = nome == "sol-nuvem" || nome == "lua-nuvem" ? 2.2f : 0f;
            using (GraphicsPath c = new GraphicsPath(FillMode.Winding))
            using (SolidBrush br = new SolidBrush(nuvem))
            {
                c.AddEllipse(r.X + (3f + dx) * u, r.Y + (11.5f + dy) * u, 8f * u, 7f * u);
                c.AddEllipse(r.X + (7f + dx) * u, r.Y + (6.5f + dy) * u, 10f * u, 10f * u);
                c.AddEllipse(r.X + (13f + dx) * u, r.Y + (10f + dy) * u, 8f * u, 8.5f * u);
                c.AddRectangle(new RectangleF(r.X + (6.5f + dx) * u, r.Y + (14f + dy) * u, 11f * u, 4.5f * u));
                g.FillPath(br, c);
            }
            if (!alta) return;
            if (nome == "tempestade")
            {
                using (SolidBrush br = new SolidBrush(sol))
                    g.FillPolygon(br, new PointF[] { P(13f, 14.5f), P(9.5f, 19.5f), P(12f, 19.5f), P(10.5f, 23.5f), P(15.5f, 17.5f), P(13f, 17.5f), P(14.5f, 14.5f) });
                return;
            }
            if (nome == "neve" || nome == "neblina")
            {
                using (Pen p = PintorDoWidget.Redonda(nuvem, (nome == "neve" ? 2.4f : 1.7f) * u))
                {
                    if (nome == "neblina") { g.DrawLine(p, P(4f, 18.5f), P(20f, 18.5f)); g.DrawLine(p, P(6f, 21.5f), P(18f, 21.5f)); }
                    else foreach (PointF q in new PointF[] { P(8f, 18.5f), P(12f, 19.5f), P(16f, 18.5f), P(10f, 22f), P(14f, 22f) }) g.DrawLine(p, q, new PointF(q.X + 0.1f, q.Y));
                }
                return;
            }
            float[] xs = nome == "garoa" ? new float[] { 8.5f, 12f, 15.5f } : nome == "chuva-forte" ? new float[] { 7f, 10.4f, 13.8f, 17.2f } : new float[] { 8.2f, 12.2f, 16.2f };
            float len = nome == "garoa" ? 1.4f : 3f;
            using (Pen p = PintorDoWidget.Redonda(gota, 1.8f * u))
                foreach (float x in xs) g.DrawLine(p, P(x, 17.5f), P(x - (nome == "garoa" ? 0f : 1f), 17.5f + len));
        }

        /// <summary>A maquininha (caixa com a porta redonda), em branco como os icones do widget de bateria.</summary>
        private static void IconeDaLavadora(Graphics g, RectangleF bolha, float s)
        {
            float m = bolha.Width * 0.26f;
            IconesCasa.Lavadora(g, RectangleF.Inflate(bolha, -m, -m), Color.White);
        }
    }

    public static partial class T
    {
        public static string CasaWidgetTitulo { get { return P("Widget na área de trabalho", "Desktop widget"); } }
        public static string CasaWidgetOpcao { get { return P("Mostrar a casa no widget", "Show home in the widget"); } }
        public static string CasaWidgetNota { get { return P("O ar, o tempo da cidade da casa e, durante um ciclo, a lavadora aparecem no widget de bateria, depois dos periféricos. As outras opções ficam na página Baterias e no botão direito do widget.", "The AC, the weather at home and, during a cycle, the washer appear in the battery widget after the peripherals. The other options are on the Batteries page and in the widget's right-click menu."); } }
        public static string CasaAbrirClima { get { return P("Clique para abrir o clima", "Click to open the weather"); } }
        public static string CasaMax { get { return P("Máx", "High"); } }
        public static string CasaMin { get { return P("Mín", "Low"); } }
    }
}
