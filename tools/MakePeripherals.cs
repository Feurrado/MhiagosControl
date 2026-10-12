using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

/// <summary>
/// Desenha os icones dos perifericos (headset G325 e mouse X11, ambos
/// brancos) em PNG transparente de 256 px.
///
/// Desenhados aqui, e nao recortados de foto do fabricante: a imagem e nossa,
/// fica nitida em qualquer escala de DPI e o contorno cinza faz o branco
/// aparecer tambem sobre o cartao claro do tema claro.
///
/// Uso: MakePeripherals.exe [pasta de saida]
/// </summary>
class MakePeripherals
{
    static readonly Color Contorno = Color.FromArgb(0x9A, 0xA0, 0xAA);
    static readonly Color Branco = Color.FromArgb(0xFB, 0xFB, 0xFC);
    static readonly Color Sombra = Color.FromArgb(0xDD, 0xE0, 0xE5);
    static readonly Color Almofada = Color.FromArgb(0xC9, 0xCD, 0xD4);
    static readonly Color Escuro = Color.FromArgb(0x4A, 0x4E, 0x57);

    static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
        Salvar(Headset(), Path.Combine(dir, "g325.png"));
        Salvar(Mouse(), Path.Combine(dir, "x11.png"));
        Salvar(Bluetooth(), Path.Combine(dir, "bluetooth.png"));
    }

    /// <summary>A runa do Bluetooth em azul sobre um disco branco, no mesmo traco dos outros.</summary>
    static Bitmap Bluetooth()
    {
        Bitmap b = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using (Graphics g = Abrir(b))
        {
            using (GraphicsPath disco = new GraphicsPath())
            {
                disco.AddEllipse(30, 30, 196, 196);
                Corpo(g, disco, 4);
            }
            using (GraphicsPath runa = new GraphicsPath())
            using (Pen pen = new Pen(Color.FromArgb(0x00, 0x82, 0xFC), 15))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                runa.AddLines(new PointF[] {
                    new PointF(92, 98), new PointF(166, 160), new PointF(128, 194),
                    new PointF(128, 62), new PointF(166, 96), new PointF(92, 158) });
                g.DrawPath(pen, runa);
            }
        }
        return b;
    }

    static void Salvar(Bitmap b, string path)
    {
        b.Save(path, ImageFormat.Png);
        b.Dispose();
        Console.WriteLine("ok " + path);
    }

    static Graphics Abrir(Bitmap b)
    {
        Graphics g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        return g;
    }

    static GraphicsPath Arredondado(RectangleF r, float raio)
    {
        float d = raio * 2;
        GraphicsPath p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Preenche com degrade vertical branco e contorna.</summary>
    static void Corpo(Graphics g, GraphicsPath p, float traco)
    {
        RectangleF r = p.GetBounds();
        using (LinearGradientBrush b = new LinearGradientBrush(
            new PointF(0, r.Top - 1), new PointF(0, r.Bottom + 1), Branco, Sombra))
            g.FillPath(b, p);
        using (Pen pen = new Pen(Contorno, traco))
        {
            pen.LineJoin = LineJoin.Round;
            g.DrawPath(pen, p);
        }
    }

    /// <summary>Faixa em arco com contorno: tiara e haste do microfone.</summary>
    static void Faixa(Graphics g, GraphicsPath p, float largura, Color cor)
    {
        using (Pen fora = new Pen(Contorno, largura + 5))
        using (Pen dentro = new Pen(cor, largura))
        {
            fora.StartCap = fora.EndCap = LineCap.Round;
            dentro.StartCap = dentro.EndCap = LineCap.Round;
            g.DrawPath(fora, p);
            g.DrawPath(dentro, p);
        }
    }

    static Bitmap Headset()
    {
        Bitmap b = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using (Graphics g = Abrir(b))
        {
            // microfone primeiro, para a concha do lado esquerdo cobrir a raiz
            using (GraphicsPath mic = new GraphicsPath())
            {
                mic.AddBezier(52, 196, 54, 222, 72, 236, 104, 236);
                Faixa(g, mic, 8, Branco);
            }
            using (GraphicsPath capsula = Arredondado(new RectangleF(98, 228, 22, 16), 8))
                Corpo(g, capsula, 3);

            // tiara: arco largo branco com a almofada cinza por dentro
            using (GraphicsPath tiara = new GraphicsPath())
            {
                tiara.AddArc(36, 22, 184, 184, 192, 156);
                Faixa(g, tiara, 18, Branco);
            }
            using (GraphicsPath almofada = new GraphicsPath())
            using (Pen pen = new Pen(Almofada, 7))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                almofada.AddArc(54, 40, 148, 148, 212, 116);
                g.DrawPath(pen, almofada);
            }

            // garfos que descem da tiara ate as conchas
            using (GraphicsPath ge = Arredondado(new RectangleF(34, 102, 14, 40), 6))
                Corpo(g, ge, 3);
            using (GraphicsPath gd = Arredondado(new RectangleF(208, 102, 14, 40), 6))
                Corpo(g, gd, 3);

            // conchas vistas de frente: casco branco e espuma voltada para dentro
            Concha(g, 22, false);
            Concha(g, 198, true);
        }
        return b;
    }

    static void Concha(Graphics g, float x, bool direita)
    {
        RectangleF casco = new RectangleF(x, 124, 36, 90);
        RectangleF espuma = direita
            ? new RectangleF(x - 10, 132, 18, 74)
            : new RectangleF(x + 28, 132, 18, 74);

        using (GraphicsPath p = Arredondado(espuma, 8))
        {
            using (SolidBrush br = new SolidBrush(Almofada)) g.FillPath(br, p);
            using (Pen pen = new Pen(Contorno, 3)) g.DrawPath(pen, p);
        }
        using (GraphicsPath p = Arredondado(casco, 16))
            Corpo(g, p, 3.5f);

        // friso discreto no casco, para nao ficar um bloco liso
        using (Pen pen = new Pen(Sombra, 3))
        {
            pen.StartCap = pen.EndCap = LineCap.Round;
            float cx = x + 18;
            g.DrawLine(pen, cx, 146, cx, 192);
        }
    }

    static Bitmap Mouse()
    {
        Bitmap b = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using (Graphics g = Abrir(b))
        {
            // vista de cima: frente mais estreita, traseira cheia
            GraphicsPath corpo = new GraphicsPath();
            corpo.AddBezier(128, 22, 170, 22, 186, 48, 188, 94);
            corpo.AddBezier(188, 94, 190, 132, 196, 172, 188, 202);
            corpo.AddBezier(188, 202, 180, 228, 154, 236, 128, 236);
            corpo.AddBezier(128, 236, 102, 236, 76, 228, 68, 202);
            corpo.AddBezier(68, 202, 60, 172, 66, 132, 68, 94);
            corpo.AddBezier(68, 94, 70, 48, 86, 22, 128, 22);
            corpo.CloseFigure();
            Corpo(g, corpo, 4);

            using (Pen pen = new Pen(Contorno, 2.5f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                // separacao entre os dois botoes
                g.DrawLine(pen, 128, 25, 128, 104);
                // fim dos botoes, curvado como a palma
                g.DrawBezier(pen, 70, 106, 100, 118, 156, 118, 186, 106);
            }

            // roda de rolagem
            using (GraphicsPath roda = Arredondado(new RectangleF(119, 46, 18, 38), 8))
            {
                using (SolidBrush br = new SolidBrush(Escuro)) g.FillPath(br, roda);
                using (Pen pen = new Pen(Color.FromArgb(0x6E, 0x73, 0x7D), 2))
                    for (int y = 54; y <= 76; y += 7) g.DrawLine(pen, 122, y, 134, y);
            }

            // brilho suave na traseira
            using (GraphicsPath brilho = new GraphicsPath())
            {
                brilho.AddEllipse(96, 140, 64, 60);
                using (PathGradientBrush pg = new PathGradientBrush(brilho))
                {
                    pg.CenterColor = Color.FromArgb(160, 255, 255, 255);
                    pg.SurroundColors = new Color[] { Color.FromArgb(0, 255, 255, 255) };
                    g.FillPath(pg, brilho);
                }
            }
            corpo.Dispose();
        }
        return b;
    }
}
