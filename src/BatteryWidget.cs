using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MhiagosControl
{
    public enum LayoutDoWidget { Aneis, Lista }

    public enum CorDoVidro { Automatica, Escura, Clara }

    /// <summary>
    /// Preferencias do widget de bateria, em %LOCALAPPDATA%\MhiagosControl\widget.ini.
    ///
    /// Fora do config.ini de proposito. A janela de configuracao trabalha num
    /// rascunho do config e, ao fechar sem salvar, a bandeja recarrega o
    /// arquivo; arrastar o widget com a janela aberta seria desfeito pelo
    /// Salvar ou pelo Fechar de uma edicao que nao tinha nada a ver com ele.
    /// Posicao na tela tambem e coisa desta maquina, nao de perfil, e nao tem
    /// por que viajar no backup da configuracao.
    /// </summary>
    public sealed class ConfigDoWidget
    {
        public const int SemPosicao = int.MinValue;

        public bool Ativo = false;
        public int X = SemPosicao, Y = SemPosicao;
        public LayoutDoWidget Layout = LayoutDoWidget.Aneis;
        public bool Fosco = false;
        /// <summary>Sem desfoque: o fundo aparece nitido atras de uma tinta leve. Vence o Fosco.</summary>
        public bool Nitido = true;
        public CorDoVidro Cor = CorDoVidro.Automatica;
        public bool NaFrente = false;
        public bool Travado = false;

        private static string _caminho;

        /// <summary>Arquivo do widget; os testes apontam para uma pasta temporaria.</summary>
        internal static string Caminho
        {
            get { return _caminho ?? Path.Combine(Paths.DataDir, "widget.ini"); }
            set { _caminho = value; }
        }

        public bool TemPosicao { get { return X != SemPosicao && Y != SemPosicao; } }

        public static ConfigDoWidget Carregar()
        {
            try
            {
                string arquivo = Caminho;
                if (File.Exists(arquivo)) return DeTexto(File.ReadAllText(arquivo, Encoding.UTF8));
            }
            catch (Exception ex) { Log.Error("leitura do widget.ini", ex); }
            return new ConfigDoWidget();
        }

        public bool Salvar()
        {
            try
            {
                string arquivo = Caminho;
                GravacaoAtomica.Escrever(arquivo, ParaTexto(), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("gravacao do widget.ini", ex);
                return false;
            }
        }

        internal string ParaTexto()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; Mhiagos Control - widget de bateria");
            sb.AppendLine("ativo=" + (Ativo ? "1" : "0"));
            if (TemPosicao)
            {
                sb.AppendLine("x=" + X.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("y=" + Y.ToString(CultureInfo.InvariantCulture));
            }
            sb.AppendLine("layout=" + (Layout == LayoutDoWidget.Lista ? "lista" : "aneis"));
            sb.AppendLine("fosco=" + (Fosco ? "1" : "0"));
            sb.AppendLine("nitido=" + (Nitido ? "1" : "0"));
            sb.AppendLine("cor=" + (Cor == CorDoVidro.Escura ? "escura" : Cor == CorDoVidro.Clara ? "clara" : "auto"));
            sb.AppendLine("frente=" + (NaFrente ? "1" : "0"));
            sb.AppendLine("travado=" + (Travado ? "1" : "0"));
            return sb.ToString();
        }

        /// <summary>Chave desconhecida ou valor estragado fica no padrao, sem derrubar o resto.</summary>
        internal static ConfigDoWidget DeTexto(string texto)
        {
            ConfigDoWidget c = new ConfigDoWidget();
            if (string.IsNullOrEmpty(texto)) return c;
            foreach (string bruta in texto.Split('\n'))
            {
                string linha = bruta.Trim();
                if (linha.Length == 0 || linha[0] == ';') continue;
                int eq = linha.IndexOf('=');
                if (eq <= 0) continue;
                string k = linha.Substring(0, eq).Trim().ToLowerInvariant();
                string v = linha.Substring(eq + 1).Trim().ToLowerInvariant();
                int n;
                bool numero = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                switch (k)
                {
                    case "ativo": c.Ativo = v == "1"; break;
                    case "x": if (numero) c.X = n; break;
                    case "y": if (numero) c.Y = n; break;
                    case "layout": c.Layout = v == "lista" ? LayoutDoWidget.Lista : LayoutDoWidget.Aneis; break;
                    case "fosco": c.Fosco = v == "1"; break;
                    case "nitido": c.Nitido = v == "1"; break;
                    case "cor": c.Cor = v == "escura" ? CorDoVidro.Escura : v == "clara" ? CorDoVidro.Clara : CorDoVidro.Automatica; break;
                    case "frente": c.NaFrente = v == "1"; break;
                    case "travado": c.Travado = v == "1"; break;
                }
            }
            if (!c.TemPosicao) { c.X = SemPosicao; c.Y = SemPosicao; }
            return c;
        }
    }

    /// <summary>
    /// O desenho do widget, separado da janela para os testes poderem pintar
    /// num bitmap e conferir o tamanho de cada formato sem abrir nada na tela.
    ///
    /// O desfoque do que esta atras vem do Windows; tudo o que fica por cima e
    /// daqui: a tinta do vidro, o brilho do topo, a luz que volta pela base,
    /// os aneis e as barras. Medidas em pixels de 96 dpi,
    /// multiplicadas pela escala da tela.
    /// </summary>
    internal static class PintorDoWidget
    {
        internal const float RaioDoVidro = 8f;
        private const float Margem = 16f;

        // aneis
        private const float Coluna = 78f;
        private const float VaoDeColuna = 6f;
        private const float Anel = 56f;
        private const float Traco = 5.5f;

        // lista
        private const float LarguraDaLista = 300f;
        private const float Linha = 50f;
        private const float VaoDeLinha = 10f;
        private const float BolhaDaLista = 40f;

        /// <summary>
        /// O que vai para o widget: todo aparelho do G HUB e da Attack Shark,
        /// mesmo desligado, e do Bluetooth so quem deu leitura. O marcador vazio
        /// do Bluetooth ("nenhum conectado") faz sentido na Visao geral, mas
        /// num widget de bateria seria um anel sem bateria nenhuma.
        /// </summary>
        internal static Periferico[] Visiveis(Periferico[] todos)
        {
            List<Periferico> l = new List<Periferico>();
            if (todos != null)
                foreach (Periferico p in todos)
                    if (p != null && (p.Bateria.HasValue || p.Fonte != FontePeriferico.Bluetooth)) l.Add(p);
            return l.ToArray();
        }

        /// <summary>
        /// O nome so aparece onde a imagem nao diz quem e o aparelho: o G325 e o
        /// X11 se reconhecem pelo desenho, mas todo aparelho Bluetooth ganha o
        /// mesmo simbolo.
        /// </summary>
        internal static bool MostraNome(Periferico p)
        {
            return p != null && p.Fonte == FontePeriferico.Bluetooth;
        }

        /// <summary>Algum aparelho do widget precisa da linha do nome.</summary>
        internal static bool ComNome(Periferico[] itens)
        {
            if (itens != null) foreach (Periferico p in itens) if (MostraNome(p)) return true;
            return false;
        }

        internal static Size Medir(LayoutDoWidget layout, int quantos, float escala, bool comNome)
        {
            float w, h;
            if (quantos <= 0) { w = 240f; h = 64f; }
            else if (layout == LayoutDoWidget.Lista)
            {
                w = LarguraDaLista;
                h = 2 * Margem + quantos * Linha + (quantos - 1) * VaoDeLinha;
            }
            else
            {
                w = 2 * Margem + quantos * Coluna + (quantos - 1) * VaoDeColuna;
                h = 2 * Margem + Anel + 8f + 20f + (comNome ? 18f : 0f);
            }
            return new Size((int)Math.Ceiling(w * escala), (int)Math.Ceiling(h * escala));
        }

        /// <summary>Retangulo de cada aparelho no ultimo desenho, para a dica do mouse.</summary>
        internal static RectangleF[] Zonas(LayoutDoWidget layout, int quantos, float escala, bool comNome)
        {
            RectangleF[] z = new RectangleF[Math.Max(0, quantos)];
            Size tam = Medir(layout, quantos, escala, comNome);
            for (int i = 0; i < z.Length; i++)
            {
                if (layout == LayoutDoWidget.Lista)
                    z[i] = new RectangleF(Margem * escala, (Margem + i * (Linha + VaoDeLinha)) * escala,
                                          tam.Width - 2 * Margem * escala, Linha * escala);
                else
                    z[i] = new RectangleF((Margem + i * (Coluna + VaoDeColuna)) * escala, Margem * escala,
                                          Coluna * escala, tam.Height - 2 * Margem * escala);
            }
            return z;
        }

        /// <summary>Tudo o que muda o desenho, numa peca so.</summary>
        internal sealed class Quadro
        {
            public Periferico[] Itens = new Periferico[0];
            public string[] Notas = new string[0];
            public LayoutDoWidget Layout = LayoutDoWidget.Aneis;
            public bool Escuro = true;
            public bool Fosco = false;
            public bool Nitido = false;
            public bool Sobre = false;
            public float Escala = 1f;
            /// <summary>De 0 a 1: em que ponto do ciclo esta a respiracao dos aparelhos carregando.</summary>
            public float Fase = 0f;
            /// <summary>
            /// De 0 a 1 por item: quanto cada um ja entrou na carga (ver
            /// TransicaoDeCarga). Sem valor, vale o estado de agora, sem transicao.
            /// </summary>
            public float[] Carga = new float[0];
        }

        /// <summary>A carga do item i no quadro: a da transicao, ou 0/1 se ninguem a mediu.</summary>
        internal static float CargaDe(Quadro q, int i)
        {
            Periferico p = q.Itens[i];
            if (!p.Bateria.HasValue) return 0f;
            if (i < q.Carga.Length) return Math.Max(0f, Math.Min(1f, q.Carga[i]));
            return p.Carregando ? 1f : 0f;
        }

        internal static void Pintar(Graphics g, Size tamanho, Quadro q)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // ClearType nao funciona sobre fundo transparente: deixa franja colorida
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            float s = q.Escala;
            Vidro(g, new RectangleF(0, 0, tamanho.Width, tamanho.Height), s, q.Escuro, q.Fosco, q.Nitido, q.Sobre);

            using (Fontes f = new Fontes(s))
            {
                if (q.Itens.Length == 0)
                {
                    Texto(g, T.WidgetNoDevices, f.Nota, Secundario(q.Escuro), q.Escuro,
                          new RectangleF(0, 0, tamanho.Width, tamanho.Height), StringAlignment.Center, StringAlignment.Center);
                    return;
                }
                RectangleF[] zonas = Zonas(q.Layout, q.Itens.Length, s, ComNome(q.Itens));
                for (int i = 0; i < q.Itens.Length; i++)
                {
                    string nota = i < q.Notas.Length ? q.Notas[i] : null;
                    float carga = CargaDe(q, i);
                    if (q.Layout == LayoutDoWidget.Lista) ItemDaLista(g, zonas[i], q.Itens[i], nota, carga, q, f);
                    else ItemDoAnel(g, zonas[i], q.Itens[i], carga, q, f);
                }
            }
        }

        // ---------------- o vidro ----------------

        /// <summary>
        /// A placa de vidro: a tinta e a luz de cima e de baixo, que fazem uma
        /// mancha translucida parecer vidro, sem borda desenhada.
        /// </summary>
        internal static void Vidro(Graphics g, RectangleF area, float s, bool escuro, bool fosco, bool nitido, bool sobre)
        {
            RectangleF r = new RectangleF(area.X + 0.5f, area.Y + 0.5f, area.Width - 1f, area.Height - 1f);
            float raio = RaioDoVidro * s;
            using (GraphicsPath forma = Arredondado(r, raio))
            {
                // tinta leve: a de antes (78/92) fechava o desfoque numa mancha
                // lisa. Sem desfoque precisa de um pouco mais, para o texto
                // nao brigar com um fundo nitido (com 64 ele sumia em listras).
                int a = nitido ? (escuro ? 125 : 135) : fosco ? (escuro ? 150 : 165) : (escuro ? 36 : 44);
                if (sobre) a = Math.Min(255, a + 18);
                using (SolidBrush tinta = new SolidBrush(escuro ? Color.FromArgb(a, 14, 16, 22) : Color.FromArgb(a, 248, 249, 252)))
                    g.FillPath(tinta, forma);

                Region antes = g.Clip;
                g.SetClip(forma, CombineMode.Intersect);
                try
                {
                    // brilho de cima: a luz batendo no vidro, que some antes do meio
                    RectangleF topo = new RectangleF(r.X, r.Y, r.Width, r.Height * 0.55f);
                    using (LinearGradientBrush b = new LinearGradientBrush(topo,
                        Color.FromArgb(escuro ? (sobre ? 46 : 34) : (sobre ? 90 : 70), 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    {
                        b.WrapMode = WrapMode.TileFlipX;
                        g.FillRectangle(b, topo);
                    }

                    // a luz que atravessa e volta pela base, mais fraca
                    float hb = r.Height * 0.35f;
                    RectangleF baixo = new RectangleF(r.X, r.Bottom - hb, r.Width, hb);
                    using (LinearGradientBrush b = new LinearGradientBrush(baixo,
                        Color.FromArgb(0, 255, 255, 255), Color.FromArgb(escuro ? 18 : 34, 255, 255, 255),
                        LinearGradientMode.Vertical))
                    {
                        b.WrapMode = WrapMode.TileFlipX;
                        g.FillRectangle(b, baixo);
                    }
                }
                finally { g.Clip = antes; antes.Dispose(); }

                // sem aro nem contorno: marcavam demais a placa (pedido do Thiago)
            }
        }

        // ---------------- aneis ----------------

        private static void ItemDoAnel(Graphics g, RectangleF z, Periferico p, float carga, Quadro q, Fontes f)
        {
            float s = q.Escala;
            bool escuro = q.Escuro;
            float d = Anel * s, st = Traco * s;
            float cx = z.X + z.Width / 2f;
            RectangleF anel = new RectangleF(cx - d / 2f + st / 2f, z.Y + st / 2f, d - st, d - st);

            Color cor = CorDoNivel(p, carga);
            // trilho na cor do proprio nivel, apagada, como os aneis do relogio
            // da Apple: o branco translucido de antes pegava a cor do fundo e
            // deixava cada anel com cara de lavado
            Color trilho = p.Bateria.HasValue ? Color.FromArgb(escuro ? 70 : 60, cor) : Trilho(escuro);
            using (Pen pen = new Pen(trilho, st)) g.DrawEllipse(pen, anel);

            if (p.Bateria.HasValue && p.Bateria.Value > 0)
            {
                float arco = 360f * Math.Min(100, p.Bateria.Value) / 100f;
                // brilho suave da mesma cor por baixo do arco, no lugar do fio
                // branco, que parecia uma costura
                // carregando, esse brilho respira: sobe e desce em seno. Um
                // brilho correndo pelo arco andava varios graus por quadro e
                // parecia travado; aqui cada quadro muda quase nada.
                // na entrada e na saida do cabo a respiracao cresce e some com a carga
                float pulso = Pulso(q.Fase) * carga;
                using (Pen luz = Redonda(Color.FromArgb(55 + (int)(120 * pulso), cor), st * (2f + 0.7f * pulso)))
                    g.DrawArc(luz, anel, -90f, arco);
                using (Pen pen = Redonda(cor, st)) g.DrawArc(pen, anel, -90f, arco);
            }

            // a bolha de vidro no miolo, com o aparelho dentro
            float db = d - 2f * st - 6f * s;
            RectangleF bolha = new RectangleF(cx - db / 2f, anel.Y + anel.Height / 2f - db / 2f, db, db);
            Bolha(g, bolha, escuro);
            Image img = PeripheralStrip.Imagem(p);
            if (img != null)
            {
                float di = d * 0.44f;
                Rectangle ri = Rectangle.Round(new RectangleF(cx - di / 2f, bolha.Y + (db - di) / 2f, di, di));
                if (Apagado(p)) PeripheralStrip.Apagado(g, img, ri);
                else g.DrawImage(img, ri);
            }

            if (carga > 0.01f)
            {
                // selo com o raio, no canto de baixo a direita do anel; entra
                // crescendo e acendendo, e sai encolhendo e apagando
                float ds = 19f * s * (0.5f + 0.5f * carga);
                float r = anel.Width / 2f;
                PointF c = new PointF(cx + r * 0.72f, anel.Y + anel.Height / 2f + r * 0.72f);
                RectangleF selo = new RectangleF(c.X - ds / 2f, c.Y - ds / 2f, ds, ds);
                using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(255 * carga), PeripheralStrip.Verde))) g.FillEllipse(b, selo);
                using (Pen pen = new Pen(escuro ? Color.FromArgb((int)(200 * carga), 20, 22, 28) : Color.FromArgb((int)(230 * carga), 255, 255, 255), 1.6f * s))
                    g.DrawEllipse(pen, selo);
                float folga = 4.5f * s * (0.5f + 0.5f * carga);
                PeripheralStrip.Raio(g, RectangleF.Inflate(selo, -folga, -folga), carga);
            }

            float y = z.Y + d + 7f * s;
            RectangleF lPct = new RectangleF(z.X, y, z.Width, 20f * s);
            Texto(g, Valor(p), f.Pct, Apagado(p) ? Secundario(escuro) : Primario(escuro), escuro, lPct,
                  StringAlignment.Center, StringAlignment.Center);
            if (MostraNome(p))
            {
                RectangleF lNome = new RectangleF(z.X, y + 20f * s, z.Width, 17f * s);
                Texto(g, p.Nome, f.Nome, Primario(escuro), escuro, lNome, StringAlignment.Center, StringAlignment.Center);
            }
        }

        // ---------------- lista ----------------

        private static void ItemDaLista(Graphics g, RectangleF z, Periferico p, string nota, float carga, Quadro q, Fontes f)
        {
            float s = q.Escala;
            bool escuro = q.Escuro;
            float db = BolhaDaLista * s;
            RectangleF bolha = new RectangleF(z.X, z.Y + (z.Height - db) / 2f, db, db);
            Bolha(g, bolha, escuro);
            Image img = PeripheralStrip.Imagem(p);
            if (img != null)
            {
                float di = db * 0.66f;
                Rectangle ri = Rectangle.Round(new RectangleF(bolha.X + (db - di) / 2f, bolha.Y + (db - di) / 2f, di, di));
                if (Apagado(p)) PeripheralStrip.Apagado(g, img, ri);
                else g.DrawImage(img, ri);
            }

            float x = bolha.Right + 12f * s, w = z.Right - x;
            string valor = Valor(p);
            // medido com o mesmo formato do desenho: o tipografico nao conta a
            // folga que o DrawString poe dos lados, e "72%" virava "7..."
            float largValor;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
                largValor = g.MeasureString(valor, f.Pct, PointF.Empty, sf).Width + 2f * s;
            // sem o nome, a porcentagem vai para o lugar dele, a esquerda, e o
            // raio da carga logo depois dela
            bool nome = MostraNome(p);
            RectangleF lValor = nome ? new RectangleF(z.Right - largValor, z.Y, largValor, 21f * s)
                                     : new RectangleF(x, z.Y, largValor, 21f * s);
            Texto(g, valor, f.Pct, Apagado(p) ? Secundario(escuro) : Primario(escuro), escuro, lValor,
                  nome ? StringAlignment.Far : StringAlignment.Near, StringAlignment.Center);

            if (!nome)
            {
                if (carga > 0.01f)
                {
                    float hr = 13f * s;
                    RectangleF raio = new RectangleF(lValor.Right + 2f * s, lValor.Y + (lValor.Height - hr) / 2f, hr * 0.7f, hr);
                    PeripheralStrip.Raio(g, raio, carga);
                }
            }
            else if (carga > 0.01f)
            {
                // o raio acende aos poucos e o nome cede o lugar dele junto,
                // sem pular de uma vez
                float hr = 13f * s;
                RectangleF raio = new RectangleF(lValor.X - hr * 0.9f, lValor.Y + (lValor.Height - hr) / 2f, hr * 0.7f, hr);
                PeripheralStrip.Raio(g, raio, carga);
                lValor = RectangleF.FromLTRB(lValor.X - hr * 0.9f * carga, lValor.Y, lValor.Right, lValor.Bottom);
            }

            if (nome)
            {
                RectangleF lNome = new RectangleF(x, z.Y, Math.Max(0f, lValor.X - x - 6f * s), 21f * s);
                Texto(g, p.Nome, f.Nome, Primario(escuro), escuro, lNome, StringAlignment.Near, StringAlignment.Center);
            }

            RectangleF barra = new RectangleF(x, z.Y + 25f * s, w, 8f * s);
            Capsula(g, barra, p, escuro, q.Fase, carga);

            if (!string.IsNullOrEmpty(nota))
            {
                RectangleF lNota = new RectangleF(x, barra.Bottom + 2f * s, w, 15f * s);
                Texto(g, nota, f.Nota, Secundario(escuro), escuro, lNota, StringAlignment.Near, StringAlignment.Center);
            }
        }

        /// <summary>A barra de carga: trilho em capsula e o nivel com um reflexo em cima.</summary>
        private static void Capsula(Graphics g, RectangleF r, Periferico p, bool escuro, float fase, float carga)
        {
            float raio = r.Height / 2f;
            using (GraphicsPath trilho = Arredondado(r, raio))
            using (SolidBrush b = new SolidBrush(Trilho(escuro)))
                g.FillPath(b, trilho);

            if (!p.Bateria.HasValue || p.Bateria.Value <= 0) return;
            float w = Math.Max(r.Height, r.Width * Math.Min(100, p.Bateria.Value) / 100f);
            RectangleF cheio = new RectangleF(r.X, r.Y, w, r.Height);
            Color cor = CorDoNivel(p, carga);
            using (GraphicsPath nivel = Arredondado(cheio, raio))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(RectangleF.Inflate(cheio, 0, 1f),
                    Misturar(cor, Color.White, 0.35f), cor, LinearGradientMode.Vertical))
                    g.FillPath(b, nivel);

                RectangleF reflexo = new RectangleF(cheio.X + raio * 0.6f, cheio.Y + cheio.Height * 0.14f,
                                                    Math.Max(0f, cheio.Width - raio * 1.2f), cheio.Height * 0.3f);
                if (reflexo.Width > 1f)
                    using (GraphicsPath rp = Arredondado(reflexo, reflexo.Height / 2f))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(110, 255, 255, 255)))
                        g.FillPath(b, rp);

                if (carga > 0.01f)
                {
                    // a barra inteira clareia e volta, no mesmo ritmo dos aneis
                    int a = (int)(90 * Pulso(fase) * carga);
                    if (a > 0)
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                            g.FillPath(b, nivel);
                }
            }
        }

        /// <summary>De 0 a 1 e de volta, suave nas pontas, ao longo de um ciclo.</summary>
        internal static float Pulso(float fase)
        {
            return (float)((1 - Math.Cos(2 * Math.PI * fase)) / 2);
        }

        // ---------------- pecas ----------------

        /// <summary>Gota de vidro menor, dentro da placa, para o icone do aparelho.</summary>
        private static void Bolha(Graphics g, RectangleF r, bool escuro)
        {
            // miolo denso e neutro, sem contorno: o icone salta e nao sobram
            // dois circulos translucidos de tons diferentes. No vidro claro o
            // miolo e cinza-chumbo, porque os icones sao brancos
            using (SolidBrush b = new SolidBrush(escuro ? Color.FromArgb(130, 12, 14, 20) : Color.FromArgb(150, 72, 78, 90)))
                g.FillEllipse(b, r);
            RectangleF topo = new RectangleF(r.X, r.Y, r.Width, r.Height * 0.6f);
            using (GraphicsPath elipse = new GraphicsPath())
            {
                elipse.AddEllipse(r);
                Region antes = g.Clip;
                g.SetClip(elipse, CombineMode.Intersect);
                using (LinearGradientBrush b = new LinearGradientBrush(RectangleF.Inflate(topo, 0, 1f),
                    Color.FromArgb(escuro ? 34 : 50, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    g.FillRectangle(b, topo);
                g.Clip = antes;
                antes.Dispose();
            }
        }

        /// <summary>
        /// Texto com uma sombra de um pixel no vidro escuro: sobre um papel de
        /// parede claro e movimentado, o branco sozinho se perde.
        /// </summary>
        private static void Texto(Graphics g, string texto, Font fonte, Color cor, bool escuro, RectangleF r,
                                  StringAlignment h, StringAlignment v)
        {
            if (string.IsNullOrEmpty(texto) || r.Width <= 0) return;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            {
                sf.Alignment = h;
                sf.LineAlignment = v;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                // halo em volta das letras, na cor oposta: com o vidro nitido o
                // fundo aparece inteiro, e sem isso o texto se perde nele
                using (SolidBrush b = new SolidBrush(escuro ? Color.FromArgb(90, 0, 0, 0) : Color.FromArgb(110, 255, 255, 255)))
                {
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            RectangleF halo = r;
                            halo.Offset(dx, dy);
                            g.DrawString(texto, fonte, b, halo, sf);
                        }
                }
                using (SolidBrush b = new SolidBrush(cor)) g.DrawString(texto, fonte, b, r, sf);
            }
        }

        private static Pen Redonda(Color cor, float largura)
        {
            Pen p = new Pen(cor, largura);
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.Round;
            return p;
        }

        internal static string Valor(Periferico p)
        {
            return p.Bateria.HasValue ? p.Bateria.Value.ToString(CultureInfo.InvariantCulture) + "%" : "--";
        }

        private static bool Apagado(Periferico p)
        {
            return !p.Bateria.HasValue;
        }

        /// <summary>Mesma regra da Visao geral: verde no cabo, vermelho em 10%, amarelo em 30%.</summary>
        internal static Color CorDoNivel(Periferico p)
        {
            return CorDoNivel(p, p.Carregando ? 1f : 0f);
        }

        /// <summary>A cor do nivel a caminho do verde do cabo: 0 fora, 1 carregando.</summary>
        internal static Color CorDoNivel(Periferico p, float carga)
        {
            if (!p.Bateria.HasValue) return Color.Gray;
            return PeripheralStrip.Misturar(PeripheralStrip.CorFora(p), PeripheralStrip.Verde, carga);
        }

        private static Color Primario(bool escuro)
        {
            return escuro ? Color.FromArgb(245, 255, 255, 255) : Color.FromArgb(235, 22, 24, 30);
        }

        private static Color Secundario(bool escuro)
        {
            return escuro ? Color.FromArgb(175, 255, 255, 255) : Color.FromArgb(160, 22, 24, 30);
        }

        private static Color Trilho(bool escuro)
        {
            return escuro ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
        }

        private static Color Misturar(Color a, Color b, float t)
        {
            return Color.FromArgb(a.A,
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        internal static GraphicsPath Arredondado(RectangleF r, float raio)
        {
            float d = Math.Min(raio * 2, Math.Min(r.Width, r.Height));
            GraphicsPath p = new GraphicsPath();
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>As fontes de um desenho, no tamanho em pixels da escala da tela.</summary>
        private sealed class Fontes : IDisposable
        {
            public readonly Font Pct, Nome, Nota;

            public Fontes(float s)
            {
                // pesos mais grossos: sobre o vidro nitido o traco fino se perde
                Pct = Criar("Segoe UI", 15f * s, FontStyle.Bold);
                Nome = Criar("Segoe UI", 12.5f * s, FontStyle.Bold);
                Nota = Criar("Segoe UI", 11.5f * s, FontStyle.Bold);
            }

            /// <summary>Sem o Semibold instalado, o GDI+ cairia numa fonte qualquer; o negrito comum chega perto.</summary>
            private static Font Criar(string nome, float px, FontStyle estilo)
            {
                Font f = new Font(nome, px, estilo, GraphicsUnit.Pixel);
                if (string.Equals(f.Name, nome, StringComparison.OrdinalIgnoreCase)) return f;
                f.Dispose();
                bool semi = nome.EndsWith("Semibold", StringComparison.Ordinal);
                return new Font("Segoe UI", px, semi ? FontStyle.Bold : estilo, GraphicsUnit.Pixel);
            }

            public void Dispose()
            {
                Pct.Dispose(); Nome.Dispose(); Nota.Dispose();
            }
        }
    }

    /// <summary>
    /// O que a pagina Baterias da configuracao usa para mexer no widget sem
    /// conhecer a bandeja. A configuracao e o mesmo objeto que o widget aberto
    /// le: quem muda um campo chama WidgetMudou para salvar e redesenhar.
    /// </summary>
    internal interface IControleDoWidget
    {
        ConfigDoWidget ConfigDoWidget { get; }
        void LigarWidget(bool ligar);
        void WidgetMudou();
        void WidgetAoCanto();
    }

    /// <summary>
    /// O widget de bateria: uma placa de vidro na area de trabalho com a carga
    /// dos perifericos.
    ///
    /// Janela em camadas (UpdateLayeredWindow) com o desenho inteiro num bitmap
    /// com transparencia por pixel - o WinForms nao desenha cantos suaves nem
    /// meio-transparencia numa janela comum. O desfoque do que fica atras e do
    /// proprio Windows, pedido pela mesma API que a barra de tarefas usa; o
    /// arredondamento do DWM corta esse desfoque nos cantos da placa.
    ///
    /// Fica logo acima dos icones da area de trabalho e abaixo de todo o resto,
    /// como um gadget. Nao rouba o foco, nao aparece na barra de tarefas nem no
    /// Alt+Tab. Arrasta com o botao esquerdo, abre a pagina Baterias com um
    /// clique e tem as opcoes no botao direito.
    /// </summary>
    public sealed class WidgetDeBateria : Form
    {
        private const int IntervaloDeLeitura = 2000;
        private const int IntervaloDeAnimacao = 40;
        private const int CicloMs = 2400;
        private const int Arrasto = 4;

        private readonly ConfigDoWidget _cfg;
        private readonly Timer _leitura = new Timer();
        private readonly Timer _animacao = new Timer();
        /// <summary>Algum aparelho entrava ou saia da carga no ultimo quadro.</summary>
        private bool _transicao;
        private readonly Timer _conferirDesktop = new Timer();
        private readonly ToolTip _dica = new ToolTip();
        private ContextMenu _menu;

        private Periferico[] _itens = new Periferico[0];
        private string[] _notas = new string[0];
        private float _escala = 1f;
        private bool _sobre;
        private int _zonaDaDica = -1;

        private bool _pressionado, _arrastando;
        private Point _inicioDoArrasto, _origemDoArrasto;

        /// <summary>Mostrar a area de trabalho (Win+D) cobriu tudo; o widget sobe ate alguem voltar.</summary>
        private bool _sobreDesktop;
        private IntPtr _gancho = IntPtr.Zero;
        private WinEventDelegate _ganchoProc;

        private readonly AvisosDoSistema _preferencias;

        /// <summary>O quadro desenhado, reaproveitado enquanto o tamanho nao muda.</summary>
        private Bitmap _quadro;

        /// <summary>Clique no widget: a bandeja abre a configuracao na pagina Baterias.</summary>
        public event Action BateriasPedidas;

        /// <summary>"Ocultar widget" no menu dele: a bandeja desliga e desmarca o item.</summary>
        public event Action OcultarPedido;

        public WidgetDeBateria(ConfigDoWidget cfg)
        {
            if (cfg == null) throw new ArgumentNullException("cfg");
            _cfg = cfg;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = T.TrayBatteryWidget;
            ControlBox = false;
            MinimizeBox = false;
            MaximizeBox = false;

            _leitura.Interval = IntervaloDeLeitura;
            _leitura.Tick += delegate { Atualizar(false); };
            _animacao.Interval = IntervaloDeAnimacao;
            _animacao.Tick += delegate { Renderizar(); };
            _conferirDesktop.Interval = 300;
            _conferirDesktop.Tick += delegate { _conferirDesktop.Stop(); ConferirDesktop(); };

            _preferencias = new AvisosDoSistema(this);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                // o construtor do Form ja pergunta isto, antes de _cfg existir
                if (_cfg != null && _cfg.NaFrente) cp.ExStyle |= WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (Graphics g = Graphics.FromHwnd(Handle)) _escala = Math.Max(1f, g.DpiX / 96f);
            ArredondarCantos();
            AplicarDesfoque();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Atualizar(true);
            Posicionar();
            Afundar();
            _leitura.Start();

            try
            {
                _ganchoProc = new WinEventDelegate(AoMudarPrimeiroPlano);
                _gancho = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                                          _ganchoProc, 0, 0, WINEVENT_OUTOFCONTEXT);
            }
            catch (Exception ex) { Log.Error("gancho de primeiro plano do widget", ex); }

            Log.Write("widget de bateria aberto");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _leitura.Stop(); _leitura.Dispose();
                _animacao.Stop(); _animacao.Dispose();
                _conferirDesktop.Stop(); _conferirDesktop.Dispose();
                if (_quadro != null) { _quadro.Dispose(); _quadro = null; }
                _dica.Dispose();
                if (_menu != null) { _menu.Dispose(); _menu = null; }
                if (_preferencias != null) _preferencias.Dispose();
            }
            if (_gancho != IntPtr.Zero) { UnhookWinEvent(_gancho); _gancho = IntPtr.Zero; }
            base.Dispose(disposing);
        }

        // ---------------- dados ----------------

        /// <summary>Le os aparelhos e redesenha so quando algo mudou de fato.</summary>
        private void Atualizar(bool forcar)
        {
            Periferico[] itens;
            string[] notas;
            try
            {
                itens = PintorDoWidget.Visiveis(Perifericos.Atual);
                notas = new string[itens.Length];
                for (int i = 0; i < itens.Length; i++)
                    notas[i] = PeripheralStrip.Nota(itens[i], PeripheralStrip.PrevisaoCurta(itens[i]));
            }
            catch (Exception ex) { Log.Error("leitura do widget", ex); return; }

            bool igual = !forcar && itens.Length == _itens.Length;
            for (int i = 0; igual && i < itens.Length; i++)
                igual = itens[i].Equals(_itens[i]) && notas[i] == _notas[i];
            if (igual) return;

            foreach (Periferico p in itens)
                TransicaoDeCarga.Informar(HistoricoDeBateria.Chave(p), p.Carregando && p.Bateria.HasValue);
            int antes = _itens.Length;
            _itens = itens;
            _notas = notas;
            AjustarAnimacao();
            Renderizar();
            if (antes != itens.Length) ManterNaTela();
        }

        private void AjustarAnimacao()
        {
            bool carregando = false;
            foreach (Periferico p in _itens) if (p.Carregando && p.Bateria.HasValue) carregando = true;
            carregando |= _transicao;
            carregando &= Ui.Animacoes;   // sem animacoes no Windows, o quadro fica parado
            if (carregando && !_animacao.Enabled) _animacao.Start();
            else if (!carregando && _animacao.Enabled) _animacao.Stop();
        }

        private bool Escuro
        {
            get
            {
                if (_cfg.Cor == CorDoVidro.Escura) return true;
                if (_cfg.Cor == CorDoVidro.Clara) return false;
                return Theme.IsDark;
            }
        }

        // ---------------- desenho ----------------

        internal void Renderizar()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                Size tam = PintorDoWidget.Medir(_cfg.Layout, _itens.Length, _escala, PintorDoWidget.ComNome(_itens));
                if (tam != Size)
                {
                    Size = tam;
                    AplicarRegiao(tam);
                }

                PintorDoWidget.Quadro q = new PintorDoWidget.Quadro();
                q.Itens = _itens;
                q.Notas = _notas;
                q.Layout = _cfg.Layout;
                q.Escuro = Escuro;
                q.Fosco = _cfg.Fosco && !_cfg.Nitido;
                q.Nitido = _cfg.Nitido;
                q.Sobre = _sobre;
                q.Escala = _escala;
                q.Fase = Ui.Animacoes ? (Environment.TickCount & int.MaxValue) % CicloMs / (float)CicloMs : 0f;
                bool transicao = false;
                q.Carga = new float[_itens.Length];
                for (int i = 0; i < _itens.Length; i++)
                {
                    bool animando;
                    q.Carga[i] = TransicaoDeCarga.Valor(HistoricoDeBateria.Chave(_itens[i]),
                        _itens[i].Carregando && _itens[i].Bateria.HasValue, out animando);
                    transicao |= animando;
                }
                _transicao = transicao;

                // O mesmo bitmap enquanto o tamanho nao muda: carregando, o
                // widget pinta 25 quadros por segundo, e alocar uma imagem
                // ARGB inteira em cada um so dava trabalho ao coletor.
                if (_quadro == null || _quadro.Size != tam)
                {
                    if (_quadro != null) _quadro.Dispose();
                    _quadro = new Bitmap(tam.Width, tam.Height, PixelFormat.Format32bppArgb);
                }
                using (Graphics g = Graphics.FromImage(_quadro))
                {
                    g.Clear(Color.Transparent);
                    PintorDoWidget.Pintar(g, tam, q);
                }
                AplicarBitmap(_quadro);
                // o ultimo quadro da transicao desliga o relogio se nada mais carrega
                AjustarAnimacao();
            }
            catch (Exception ex)
            {
                // um quadro perdido nao pode virar uma enxurrada no log a 15 por segundo
                _animacao.Stop();
                Log.Error("desenho do widget", ex);
            }
        }

        private void AplicarBitmap(Bitmap bmp)
        {
            IntPtr tela = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(tela);
            IntPtr hbmp = IntPtr.Zero, antigo = IntPtr.Zero;
            try
            {
                hbmp = bmp.GetHbitmap(Color.FromArgb(0));
                antigo = SelectObject(mem, hbmp);
                SIZE tamanho = new SIZE(bmp.Width, bmp.Height);
                POINT origem = new POINT(0, 0);
                POINT topo = new POINT(Left, Top);
                BLENDFUNCTION b = new BLENDFUNCTION();
                b.BlendOp = AC_SRC_OVER;
                b.SourceConstantAlpha = 255;
                b.AlphaFormat = AC_SRC_ALPHA;
                UpdateLayeredWindow(Handle, tela, ref topo, ref tamanho, mem, ref origem, 0, ref b, ULW_ALPHA);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, tela);
                if (hbmp != IntPtr.Zero) { SelectObject(mem, antigo); DeleteObject(hbmp); }
                DeleteDC(mem);
            }
        }

        /// <summary>
        /// Recorta a janela no formato da placa, para o clique.
        /// </summary>
        private void AplicarRegiao(Size tam)
        {
            int d = (int)Math.Round(PintorDoWidget.RaioDoVidro * 2f * _escala);
            try
            {
                IntPtr rgn = CreateRoundRectRgn(0, 0, tam.Width + 1, tam.Height + 1, d, d);
                // a regiao passa a ser do sistema: nao se apaga depois de entregar
                if (SetWindowRgn(Handle, rgn, true) == 0) DeleteObject(rgn);
            }
            catch (Exception ex) { Log.Error("regiao do widget", ex); }
        }

        /// <summary>
        /// O desfoque do Windows ignora a regiao da janela e saia quadrado fora
        /// da curva dos cantos (SetWindowRgn e DwmEnableBlurBehindWindow nao
        /// recortam, nem numa janela comum; visto no Windows 11). So o
        /// arredondamento do proprio DWM corta o desfoque, e o raio dele e fixo
        /// em 8 px na escala 1: por isso a placa e desenhada com esse raio. No
        /// Windows 10 a chamada falha calada e o desfoque fica retangular.
        /// </summary>
        private void ArredondarCantos()
        {
            if (!Windows11) return;
            try
            {
                int redondo = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref redondo, 4);
            }
            catch (Exception ex) { Log.Error("cantos do widget", ex); }
        }

        /// <summary>
        /// Desfoque do que esta atras. O fosco usa o acrilico do Windows 11, de
        /// desfoque mais forte; no Windows 10 o acrilico atrasa ao arrastar, e
        /// os dois ficam no desfoque simples.
        /// </summary>
        private void AplicarDesfoque()
        {
            try
            {
                AccentPolicy ap = new AccentPolicy();
                bool acrilico = _cfg.Fosco && !_cfg.Nitido && Windows11;
                ap.AccentState = _cfg.Nitido ? ACCENT_DISABLED
                               : acrilico ? ACCENT_ENABLE_ACRYLICBLURBEHIND : ACCENT_ENABLE_BLURBEHIND;
                // tinta quase nula: com alfa zero o acrilico some em algumas versoes
                ap.GradientColor = acrilico ? 0x01000000u : 0u;
                int tamanho = Marshal.SizeOf(ap);
                IntPtr p = Marshal.AllocHGlobal(tamanho);
                try
                {
                    Marshal.StructureToPtr(ap, p, false);
                    WindowCompositionAttributeData d = new WindowCompositionAttributeData();
                    d.Attribute = WCA_ACCENT_POLICY;
                    d.SizeOfData = tamanho;
                    d.Data = p;
                    SetWindowCompositionAttribute(Handle, ref d);
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch (Exception ex) { Log.Error("desfoque do widget", ex); }
        }

        /// <summary>
        /// Pelo RtlGetVersion, e nao pelo Environment.OSVersion: sem manifesto
        /// de compatibilidade (a previa, por exemplo) o .NET responde 6.2 num
        /// Windows 11.
        /// </summary>
        private static bool Windows11
        {
            get
            {
                Version v = Environment.OSVersion.Version;
                try
                {
                    OSVERSIONINFOEX info = new OSVERSIONINFOEX();
                    info.dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFOEX));
                    if (RtlGetVersion(ref info) == 0)
                        v = new Version(info.dwMajorVersion, info.dwMinorVersion, info.dwBuildNumber);
                }
                catch (Exception ex) { Log.Error("versao do Windows", ex); }
                return v.Major > 10 || (v.Major == 10 && v.Build >= 22000);
            }
        }

        // ---------------- posicao ----------------

        /// <summary>Volta onde estava; na primeira vez, ou se a tela sumiu, no canto de cima a direita.</summary>
        private void Posicionar()
        {
            Size tam = PintorDoWidget.Medir(_cfg.Layout, _itens.Length, _escala, PintorDoWidget.ComNome(_itens));
            Point p;
            if (_cfg.TemPosicao && Visivel(new Rectangle(_cfg.X, _cfg.Y, tam.Width, tam.Height)))
                p = new Point(_cfg.X, _cfg.Y);
            else
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                int m = (int)(24 * _escala);
                p = new Point(area.Right - tam.Width - m, area.Top + m);
            }
            Location = p;
            ManterNaTela();
            Renderizar();
        }

        /// <summary>Visivel o bastante para alguem achar e arrastar de volta.</summary>
        internal static bool Visivel(Rectangle r)
        {
            foreach (Screen tela in Screen.AllScreens)
            {
                Rectangle i = Rectangle.Intersect(tela.WorkingArea, r);
                if (i.Width >= Math.Min(48, r.Width) && i.Height >= Math.Min(32, r.Height)) return true;
            }
            return false;
        }

        /// <summary>
        /// Encaixa a placa dentro da area util da tela em que esta. Com mais um
        /// aparelho ela cresce para a direita, e no canto direito sairia da tela.
        /// </summary>
        private void ManterNaTela()
        {
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            int x = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            int y = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            if (x != Left || y != Top) Location = new Point(x, y);
        }

        /// <summary>Gruda na borda da tela quando chega a poucos pixels dela, como os gadgets faziam.</summary>
        internal static Point Encaixar(Point p, Size tam, Rectangle area, int ima)
        {
            int x = p.X, y = p.Y;
            if (Math.Abs(x - area.Left) <= ima) x = area.Left;
            else if (Math.Abs(area.Right - (x + tam.Width)) <= ima) x = area.Right - tam.Width;
            if (Math.Abs(y - area.Top) <= ima) y = area.Top;
            else if (Math.Abs(area.Bottom - (y + tam.Height)) <= ima) y = area.Bottom - tam.Height;
            return new Point(x, y);
        }

        private void GuardarPosicao()
        {
            _cfg.X = Left;
            _cfg.Y = Top;
            _cfg.Salvar();
        }

        // ---------------- mouse ----------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _pressionado = true;
            _arrastando = false;
            _inicioDoArrasto = Cursor.Position;
            _origemDoArrasto = Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_sobre) { _sobre = true; Renderizar(); }

            if (_pressionado && !_cfg.Travado)
            {
                Point agora = Cursor.Position;
                int dx = agora.X - _inicioDoArrasto.X, dy = agora.Y - _inicioDoArrasto.Y;
                if (!_arrastando && (Math.Abs(dx) > Arrasto || Math.Abs(dy) > Arrasto))
                {
                    _arrastando = true;
                    _dica.Hide(this);
                }
                if (_arrastando)
                {
                    Point alvo = new Point(_origemDoArrasto.X + dx, _origemDoArrasto.Y + dy);
                    Rectangle area = Screen.FromPoint(agora).WorkingArea;
                    Location = Encaixar(alvo, Size, area, (int)(12 * _escala));
                }
                return;
            }

            AtualizarDica(e.Location);
        }

        /// <summary>
        /// Outra janela tomou o mouse no meio do arrasto (tecla Windows, UAC,
        /// Alt+Tab): o MouseUp nao vem mais, e sem isso o widget seguiria o
        /// cursor so de passar por cima.
        /// </summary>
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            // botao ja solto: e a soltura normal, o MouseUp vem logo depois
            if (Capture || !_pressionado || (Control.MouseButtons & MouseButtons.Left) == 0) return;
            _pressionado = false;
            if (_arrastando)
            {
                _arrastando = false;
                ManterNaTela();
                GuardarPosicao();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { MostrarMenu(e.Location); return; }
            if (e.Button != MouseButtons.Left || !_pressionado) return;
            _pressionado = false;
            Capture = false;
            if (_arrastando)
            {
                _arrastando = false;
                ManterNaTela();
                GuardarPosicao();
                return;
            }
            if (BateriasPedidas != null) BateriasPedidas();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_arrastando) return;
            _sobre = false;
            _zonaDaDica = -1;
            _dica.SetToolTip(this, null);
            Renderizar();
        }

        /// <summary>Nome, carga e a previsao do aparelho sob o ponteiro.</summary>
        private void AtualizarDica(Point p)
        {
            RectangleF[] zonas = PintorDoWidget.Zonas(_cfg.Layout, _itens.Length, _escala, PintorDoWidget.ComNome(_itens));
            int zona = -1;
            for (int i = 0; i < zonas.Length; i++) if (zonas[i].Contains(p)) { zona = i; break; }
            if (zona == _zonaDaDica) return;
            _zonaDaDica = zona;
            if (zona < 0) { _dica.SetToolTip(this, T.OpenBatteriesPage); return; }
            Periferico it = _itens[zona];
            string texto = it.Nome + "  " + PintorDoWidget.Valor(it);
            string nota = _notas[zona];
            if (!string.IsNullOrEmpty(nota)) texto += "\n" + nota;
            _dica.SetToolTip(this, texto + "\n" + T.OpenBatteriesPage);
        }

        // ---------------- menu ----------------

        private void MostrarMenu(Point onde)
        {
            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add(new MenuItem(T.WidgetOpenBatteries, delegate { if (BateriasPedidas != null) BateriasPedidas(); }));
            menu.MenuItems.Add("-");

            MenuItem layout = new MenuItem(T.WidgetLayout);
            layout.MenuItems.Add(Opcao(T.WidgetRings, _cfg.Layout == LayoutDoWidget.Aneis, delegate { TrocarLayout(LayoutDoWidget.Aneis); }));
            layout.MenuItems.Add(Opcao(T.WidgetList, _cfg.Layout == LayoutDoWidget.Lista, delegate { TrocarLayout(LayoutDoWidget.Lista); }));
            menu.MenuItems.Add(layout);

            MenuItem vidro = new MenuItem(T.WidgetGlass);
            vidro.MenuItems.Add(Opcao(T.WidgetSharp, _cfg.Nitido, delegate { TrocarVidro(true, false); }));
            vidro.MenuItems.Add(Opcao(T.WidgetClear, !_cfg.Nitido && !_cfg.Fosco, delegate { TrocarVidro(false, false); }));
            vidro.MenuItems.Add(Opcao(T.WidgetFrosted, !_cfg.Nitido && _cfg.Fosco, delegate { TrocarVidro(false, true); }));
            vidro.MenuItems.Add("-");
            vidro.MenuItems.Add(Opcao(T.WidgetTintAuto, _cfg.Cor == CorDoVidro.Automatica, delegate { TrocarCor(CorDoVidro.Automatica); }));
            vidro.MenuItems.Add(Opcao(T.WidgetTintDark, _cfg.Cor == CorDoVidro.Escura, delegate { TrocarCor(CorDoVidro.Escura); }));
            vidro.MenuItems.Add(Opcao(T.WidgetTintLight, _cfg.Cor == CorDoVidro.Clara, delegate { TrocarCor(CorDoVidro.Clara); }));
            menu.MenuItems.Add(vidro);

            MenuItem frente = new MenuItem(T.WidgetOnTop, delegate { TrocarFrente(!_cfg.NaFrente); });
            frente.Checked = _cfg.NaFrente;
            menu.MenuItems.Add(frente);
            MenuItem trava = new MenuItem(T.WidgetLock, delegate { _cfg.Travado = !_cfg.Travado; _cfg.Salvar(); });
            trava.Checked = _cfg.Travado;
            menu.MenuItems.Add(trava);

            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem(T.WidgetHide, delegate { if (OcultarPedido != null) OcultarPedido(); }));

            // Menu nativo numa janela que nunca fica ativa: sem o primeiro plano
            // ele nao fecha ao clicar fora (comportamento documentado do
            // TrackPopupMenu). A janela volta ao fundo pelo WM_WINDOWPOSCHANGING.
            SetForegroundWindow(Handle);
            _dica.Hide(this);
            // O clique numa opcao chega depois, como WM_COMMAND: descartar o
            // menu logo apos o Show perderia a acao. Fica ate o proximo.
            if (_menu != null) _menu.Dispose();
            _menu = menu;
            menu.Show(this, onde);
        }

        private static MenuItem Opcao(string texto, bool marcado, EventHandler acao)
        {
            MenuItem mi = new MenuItem(texto, acao);
            mi.RadioCheck = true;
            mi.Checked = marcado;
            return mi;
        }

        private void TrocarLayout(LayoutDoWidget layout)
        {
            if (_cfg.Layout == layout) return;
            _cfg.Layout = layout;
            _cfg.Salvar();
            _zonaDaDica = -1;
            Renderizar();
            ManterNaTela();
        }

        private void TrocarVidro(bool nitido, bool fosco)
        {
            if (_cfg.Nitido == nitido && _cfg.Fosco == fosco) return;
            _cfg.Nitido = nitido;
            _cfg.Fosco = fosco;
            _cfg.Salvar();
            AplicarDesfoque();
            Renderizar();
        }

        private void TrocarCor(CorDoVidro cor)
        {
            if (_cfg.Cor == cor) return;
            _cfg.Cor = cor;
            _cfg.Salvar();
            Renderizar();
        }

        private void TrocarFrente(bool frente)
        {
            _cfg.NaFrente = frente;
            _cfg.Salvar();
            AplicarFrente();
        }

        /// <summary>
        /// Reaplica a configuracao inteira, ja salva por quem mudou: a pagina
        /// Baterias mexe nos campos e chama isto.
        /// </summary>
        internal void AplicarConfig()
        {
            if (IsDisposed || !IsHandleCreated) return;
            _zonaDaDica = -1;
            AplicarDesfoque();
            AplicarFrente();
            Renderizar();
            ManterNaTela();
        }

        /// <summary>Volta para o canto de cima a direita da tela principal.</summary>
        internal void VoltarAoCanto()
        {
            if (IsDisposed || !IsHandleCreated) return;
            Posicionar();
        }

        private void AplicarFrente()
        {
            bool frente = _cfg.NaFrente;
            _sobreDesktop = false;
            if (frente) SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            else
            {
                SetWindowPos(Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                Afundar();
            }
        }

        // ---------------- lugar na pilha de janelas ----------------

        /// <summary>
        /// Prende o widget logo acima da area de trabalho a cada mudanca de
        /// ordem. HWND_BOTTOM puro nao serve: ficaria atras da janela dos icones,
        /// que ocupa a tela inteira, e sumiria.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            if (m.Msg == WM_WINDOWPOSCHANGING && _cfg != null && !_cfg.NaFrente && !_sobreDesktop && m.LParam != IntPtr.Zero)
            {
                try
                {
                    WINDOWPOS wp = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
                    if ((wp.flags & SWP_NOZORDER) == 0)
                    {
                        IntPtr depois = LugarNoFundo();
                        if (depois == Handle) wp.flags |= SWP_NOZORDER;
                        else wp.hwndInsertAfter = depois;
                        Marshal.StructureToPtr(wp, m.LParam, false);
                    }
                }
                catch (Exception ex) { Log.Error("ordem do widget", ex); }
            }
            base.WndProc(ref m);
        }

        /// <summary>A janela atras da qual o widget deve ficar: a que esta logo acima da area de trabalho.</summary>
        private IntPtr LugarNoFundo()
        {
            IntPtr desktop = AreaDeTrabalho();
            if (desktop == IntPtr.Zero) return HWND_BOTTOM;
            IntPtr acima = GetWindow(desktop, GW_HWNDPREV);
            if (acima == IntPtr.Zero) return HWND_TOP;   // a area de trabalho esta no topo: Win+D
            return acima;
        }

        private void Afundar()
        {
            if (!IsHandleCreated || _cfg.NaFrente) return;
            // a ordem final vem do WM_WINDOWPOSCHANGING acima
            SetWindowPos(Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// A janela dos icones da area de trabalho: o Progman, ou o WorkerW que
        /// o Explorer cria por cima dele quando ha apresentacao de papel de parede.
        /// </summary>
        private static IntPtr AreaDeTrabalho()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero && FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                return progman;
            IntPtr w = IntPtr.Zero;
            while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
                if (FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return w;
            return progman;
        }

        /// <summary>A area de trabalho subiu para cima do widget? So acontece com o Mostrar area de trabalho.</summary>
        private bool DesktopPorCima()
        {
            IntPtr desktop = AreaDeTrabalho();
            if (desktop == IntPtr.Zero) return false;
            int limite = 4096;
            for (IntPtr h = GetWindow(Handle, GW_HWNDPREV); h != IntPtr.Zero && limite-- > 0; h = GetWindow(h, GW_HWNDPREV))
                if (h == desktop) return true;
            return false;
        }

        private void AoMudarPrimeiroPlano(IntPtr gancho, uint evento, IntPtr hwnd, int objeto, int filho, uint thread, uint tempo)
        {
            if (IsDisposed || !IsHandleCreated || _cfg.NaFrente) return;
            ConferirDesktop();
            // o Explorer as vezes termina de subir a area de trabalho depois de avisar
            _conferirDesktop.Stop();
            _conferirDesktop.Start();
        }

        /// <summary>
        /// Com o Win+D a area de trabalho cobre todas as janelas, inclusive o
        /// widget. Ele sobe para a frente enquanto ela estiver por cima e
        /// volta ao fundo quando qualquer outra janela ganha o primeiro plano.
        /// </summary>
        private void ConferirDesktop()
        {
            if (IsDisposed || !IsHandleCreated || _cfg.NaFrente) return;
            try
            {
                if (!_sobreDesktop && DesktopPorCima())
                {
                    _sobreDesktop = true;
                    SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                else if (_sobreDesktop && !EhAreaDeTrabalho(GetForegroundWindow()))
                {
                    _sobreDesktop = false;
                    SetWindowPos(Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    Afundar();
                }
            }
            catch (Exception ex) { Log.Error("widget e area de trabalho", ex); }
        }

        private static bool EhAreaDeTrabalho(IntPtr h)
        {
            if (h == IntPtr.Zero) return true;
            string c = NomeDaClasse(h);
            return c == "Progman" || c == "WorkerW";
        }

        private static bool Escondida(IntPtr h)
        {
            int cloaked = 0;
            try { DwmGetWindowAttribute(h, DWMWA_CLOAKED, out cloaked, sizeof(int)); }
            catch { }
            return cloaked != 0;
        }

        private static string NomeDaClasse(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>Uma linha sobre escala, posicao e lugar na pilha de janelas, para a previa e o log.</summary>
        internal string Diagnostico()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("windows 11: ").Append(Windows11 ? "sim" : "nao");
            sb.Append(" | escala ").Append(_escala.ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append(" | ").Append(Width).Append('x').Append(Height).Append(" em ").Append(Left).Append(',').Append(Top);
            if (!IsHandleCreated) return sb.ToString();
            IntPtr desktop = AreaDeTrabalho();
            sb.Append(" | area de trabalho: ").Append(desktop == IntPtr.Zero ? "nao achada" : NomeDaClasse(desktop));
            if (desktop != IntPtr.Zero)
            {
                // janelas invisiveis ou escondidas pelo DWM (apps suspensos) nao
                // contam: o que importa e se ha algo visivel entre os dois
                int entre = 0, limite = 4096;
                IntPtr h = GetWindow(Handle, GW_HWNDNEXT);
                for (; h != IntPtr.Zero && h != desktop && limite-- > 0; h = GetWindow(h, GW_HWNDNEXT))
                    if (IsWindowVisible(h) && !Escondida(h)) entre++;
                if (h == desktop) sb.Append(" | janelas visiveis entre o widget e ela: ").Append(entre);
                else sb.Append(" | area de trabalho nao esta abaixo do widget");
            }
            sb.Append(" | ela por cima do widget: ").Append(DesktopPorCima() ? "sim" : "nao");
            sb.Append(" | sempre na frente: ").Append(_cfg.NaFrente || _sobreDesktop ? "sim" : "nao");
            return sb.ToString();
        }

        /// <summary>
        /// Tema e tela mudando. O SystemEvents segura quem assina para sempre,
        /// por isso a assinatura mora num objeto proprio que se desfaz junto.
        /// </summary>
        private sealed class AvisosDoSistema : IDisposable
        {
            private readonly WidgetDeBateria _dono;
            private readonly Microsoft.Win32.UserPreferenceChangedEventHandler _pref;
            private readonly EventHandler _tela;

            public AvisosDoSistema(WidgetDeBateria dono)
            {
                _dono = dono;
                _pref = delegate(object s, Microsoft.Win32.UserPreferenceChangedEventArgs a)
                {
                    if (a.Category == Microsoft.Win32.UserPreferenceCategory.General ||
                        a.Category == Microsoft.Win32.UserPreferenceCategory.Color)
                        Depois(delegate { _dono.Renderizar(); });
                };
                _tela = delegate { Depois(delegate { _dono.ManterNaTela(); _dono.Renderizar(); }); };
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += _pref;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += _tela;
            }

            /// <summary>O aviso pode vir de outra thread; o desenho e da thread da janela.</summary>
            private void Depois(MethodInvoker acao)
            {
                try
                {
                    if (_dono.IsDisposed || !_dono.IsHandleCreated) return;
                    _dono.BeginInvoke(acao);
                }
                catch (Exception ex) { Log.Error("aviso do sistema ao widget", ex); }
            }

            public void Dispose()
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= _pref;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= _tela;
            }
        }

        // ---------------- Win32 ----------------

        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x8000000;
        private const int WS_EX_TOPMOST = 0x8;
        private const int WM_WINDOWPOSCHANGING = 0x46;
        private const int WM_MOUSEACTIVATE = 0x21;
        private const int MA_NOACTIVATE = 3;
        private const int ULW_ALPHA = 2;
        private const byte AC_SRC_OVER = 0;
        private const byte AC_SRC_ALPHA = 1;
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        private const uint GW_HWNDNEXT = 2, GW_HWNDPREV = 3;
        private const int DWMWA_CLOAKED = 14;
        private const uint EVENT_SYSTEM_FOREGROUND = 3;
        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_DISABLED = 0;
        private const int ACCENT_ENABLE_BLURBEHIND = 3;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int Cx, Cy; public SIZE(int cx, int cy) { Cx = cx; Cy = cy; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OSVERSIONINFOEX
        {
            public int dwOSVersionInfoSize, dwMajorVersion, dwMinorVersion, dwBuildNumber, dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
            public ushort wServicePackMajor, wServicePackMinor, wSuiteMask;
            public byte wProductType, wReserved;
        }

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("ntdll.dll")] private static extern int RtlGetVersion(ref OSVERSIONINFOEX info);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        private delegate void WinEventDelegate(IntPtr hook, uint evento, IntPtr hwnd, int objeto, int filho, uint thread, uint tempo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string nome);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr pai, IntPtr depois, string cls, string nome);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventDelegate proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    }
}
