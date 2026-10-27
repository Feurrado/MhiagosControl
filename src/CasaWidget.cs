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
        internal enum Tipo { Ar, Lavadora }

        /// <summary>Bolhas de - e + (e do liga/desliga na lista): o tamanho do selo de carga, um pouco maior para o dedo.</summary>
        private const float Mini = 22f, VaoMini = 6f;

        private Aparelho _ar, _lav;
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
            if (!ServicoCasa.Logado) return;
            foreach (Aparelho a in ServicoCasa.Aparelhos)
            {
                if (_ar == null && a.ArCondicionado) _ar = a;
                if (_lav == null && a.Lavadora) _lav = a;
            }
        }

        /// <summary>
        /// A lavadora so entra com ciclo ativo: em funcionamento, pausada,
        /// aguardando o inicio programado ou com erro. Parada, concluida,
        /// desligada ou desconectada, ela sai.
        /// </summary>
        internal static bool CicloAtivo(Aparelho lav)
        {
            if (lav == null) return false;
            CicloAtual c = CicloAtual.De(lav);
            return c.Online && (c.Rodando || c.Erro);
        }

        /// <summary>Os itens que o widget mostra agora, na ordem.</summary>
        public Tipo[] Itens()
        {
            List<Tipo> l = new List<Tipo>();
            if (Ativa && ServicoCasa.Logado)
            {
                if (_ar != null) l.Add(Tipo.Ar);
                if (CicloAtivo(_lav)) l.Add(Tipo.Lavadora);
            }
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

        /// <summary>0 menos, 1 mais, 2 liga/desliga, 3 em cima de um item da casa, -1 fora deles.</summary>
        public int Zona(Point p)
        {
            if (_rMenos.Contains(p)) return 0;
            if (_rMais.Contains(p)) return 1;
            if (_rLiga.Contains(p)) return 2;
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
            StringBuilder sb = new StringBuilder();
            if (_ar != null) sb.Append(_ar.Nome).Append("  ").Append(LinhaDoAr()).Append('\n');
            if (CicloAtivo(_lav)) sb.Append(_lav.Nome).Append("  ").Append(LinhaDaLavadora(CicloAtual.De(_lav))).Append('\n');
            return sb.Append(T.CasaResumoConvite).ToString();
        }

        // ---------------- pintura ----------------

        /// <summary>Antes de cada quadro: as zonas sao as do desenho que vem.</summary>
        public void ComecarQuadro()
        {
            _rMenos = _rMais = _rLiga = RectangleF.Empty;
            _zonas.Clear();
        }

        public void Pintar(Graphics g, Tipo t, RectangleF z, LayoutDoWidget layout, float s, bool escuro, PintorDoWidget.Fontes f)
        {
            _zonas.Add(z);
            using (Font glifo = new Font("Segoe MDL2 Assets", 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font peq = new Font("Segoe MDL2 Assets", 9f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                if (layout == LayoutDoWidget.Lista)
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
            string u = _ar.Int("t_temp_type", 0) == 1 ? "°F" : "°C";
            string amb = _ar.Tem("f_temp_in") ? _ar.Num("f_temp_in", 0).ToString("0.#") + u + " · " : "";
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
                if (ativo && _hover == zona)
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(escuro ? 40 : 60, 255, 255, 255))) g.FillEllipse(b, r);
            }
            Glifo(g, glifo, fonte, r, ativo || aceso ? Color.White : Color.FromArgb(110, 255, 255, 255));
        }

        /// <summary>O anel do widget de bateria: trilho apagado na cor, arco com brilho suave por baixo.</summary>
        private static void Anel(Graphics g, RectangleF anel, float st, float fracao, Color cor, bool aceso, bool escuro)
        {
            Color trilho = aceso ? Color.FromArgb(escuro ? 70 : 60, cor) : PintorDoWidget.Trilho(escuro);
            using (Pen pen = new Pen(trilho, st)) g.DrawEllipse(pen, anel);
            if (!aceso || fracao <= 0f) return;
            float arco = 360f * Math.Min(1f, fracao);
            using (Pen luz = PintorDoWidget.Redonda(Color.FromArgb(55, cor), st * 2f)) g.DrawArc(luz, anel, -90f, arco);
            using (Pen pen = PintorDoWidget.Redonda(cor, st)) g.DrawArc(pen, anel, -90f, arco);
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
            Anel(g, anel, st, fracao, Ui.Accent, ligado, escuro);
            float db = d - 2f * st - 6f * s;
            RectangleF bolha = new RectangleF(cx - db / 2f, anel.Y + anel.Height / 2f - db / 2f, db, db);
            PintorDoWidget.Bolha(g, bolha, escuro);
            Glifo(g, "\uE9CA", glifo, bolha, ligado ? Color.White : Color.FromArgb(150, 255, 255, 255));

            float mini = Mini * s, cy = anel.Y + anel.Height / 2f;
            _rMenos = new RectangleF(z.X, cy - mini / 2f, mini, mini);
            _rMais = new RectangleF(z.Right - mini, cy - mini / 2f, mini, mini);
            float r = anel.Width / 2f, ds = 19f * s;   // o selo do widget de bateria
            _rLiga = new RectangleF(cx + r * 0.72f - ds / 2f, cy + r * 0.72f - ds / 2f, ds, ds);
            Botao(g, _rMenos, "\uE738", peq, false, ligado, 0, escuro);
            Botao(g, _rMais, "\uE710", peq, false, ligado, 1, escuro);
            Botao(g, _rLiga, "\uE7E8", peq, ligado, on, 2, escuro);

            RectangleF lValor = new RectangleF(z.X, z.Y + d + 7f * s, z.Width, PintorDoWidget.LinhaDoValor * s);
            PintorDoWidget.Texto(g, t > 0 ? t + "°" : "--", f.Pct, ligado ? PintorDoWidget.Primario(escuro) : PintorDoWidget.Secundario(escuro), escuro,
                lValor, StringAlignment.Center, StringAlignment.Center);
        }

        /// <summary>A lavadora como um item de anel: o arco e o progresso do ciclo, o tempo restante embaixo.</summary>
        private void LavadoraNoAnel(Graphics g, RectangleF z, float s, PintorDoWidget.Fontes f, bool escuro)
        {
            float d = PintorDoWidget.Anel * s, st = PintorDoWidget.Traco * s;
            CicloAtual c = CicloAtual.De(_lav);
            float cx = z.X + z.Width / 2f;
            RectangleF anel = new RectangleF(cx - d / 2f + st / 2f, z.Y + st / 2f, d - st, d - st);
            Anel(g, anel, st, c.Erro ? 1f : c.Progresso, CorDoCiclo(c), true, escuro);
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
            Glifo(g, "\uE9CA", glifo, bolha, ligado ? Color.White : Color.FromArgb(150, 255, 255, 255));

            float x = bolha.Right + 12f * s;
            int t = Alvo;
            string valor = t > 0 ? t + "°" : "--";
            float largValor;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
                largValor = g.MeasureString(valor, f.Pct, PointF.Empty, sf).Width + 2f * s;
            RectangleF lValor = new RectangleF(z.Right - largValor, z.Y, largValor, 21f * s);
            PintorDoWidget.Texto(g, valor, f.Pct, ligado ? PintorDoWidget.Primario(escuro) : PintorDoWidget.Secundario(escuro), escuro,
                lValor, StringAlignment.Far, StringAlignment.Center);
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

        /// <summary>A maquininha (caixa com a porta redonda), em branco como os icones do widget de bateria.</summary>
        private static void IconeDaLavadora(Graphics g, RectangleF bolha, float s)
        {
            float lado = bolha.Width * 0.46f;
            RectangleF r = new RectangleF(bolha.X + (bolha.Width - lado * 0.86f) / 2f, bolha.Y + (bolha.Height - lado) / 2f, lado * 0.86f, lado);
            using (Pen pen = new Pen(Color.White, 1.6f * s))
            {
                using (GraphicsPath gp = PintorDoWidget.Arredondado(r, 2.5f * s)) g.DrawPath(pen, gp);
                float d = r.Width * 0.62f;
                g.DrawEllipse(pen, r.X + (r.Width - d) / 2f, r.Bottom - d - 2f * s, d, d);
                g.DrawLine(pen, r.X + 2.5f * s, r.Y + 3f * s, r.X + 5f * s, r.Y + 3f * s);
            }
        }
    }

    public static partial class T
    {
        public static string CasaWidgetTitulo { get { return P("Widget na área de trabalho", "Desktop widget"); } }
        public static string CasaWidgetOpcao { get { return P("Mostrar a casa no widget", "Show home in the widget"); } }
        public static string CasaWidgetNota { get { return P("O ar e, durante um ciclo, a lavadora aparecem no widget de bateria, depois dos periféricos. As outras opções ficam na página Baterias e no botão direito do widget.", "The AC and, during a cycle, the washer appear in the battery widget after the peripherals. The other options are on the Batteries page and in the widget's right-click menu."); } }
    }
}
