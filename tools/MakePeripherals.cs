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

    /// <summary>
    /// G325 de lado, como nas fotos da Logitech: tiara fina, concha oval com
    /// a tampa lisa e o G em relevo, a outra concha aparecendo atras e a haste
    /// curta e reta do microfone saindo da frente da concha esquerda.
    /// </summary>
    static Bitmap Headset()
    {
        Bitmap b = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using (Graphics g = Abrir(b))
        {
            // concha de tras, mais escura e deslocada, so para dar volume
            using (GraphicsPath tras = new GraphicsPath())
            {
                tras.AddEllipse(138, 92, 64, 98);
                using (SolidBrush br = new SolidBrush(Almofada)) g.FillPath(br, tras);
                using (Pen pen = new Pen(Contorno, 3.5f)) g.DrawPath(pen, tras);
            }

            // tiara: arco fino, da concha de tras ate a da frente
            using (GraphicsPath tiara = new GraphicsPath())
            {
                tiara.AddBezier(102, 104, 74, 6, 196, 2, 170, 98);
                Faixa(g, tiara, 13, Branco);
            }
            using (GraphicsPath almofada = new GraphicsPath())
            using (Pen pen = new Pen(Almofada, 5))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                almofada.AddBezier(102, 76, 92, 28, 180, 24, 168, 72);
                g.DrawPath(pen, almofada);
            }

            // haste do microfone: reta, saindo da frente e descendo para a boca
            using (GraphicsPath haste = new GraphicsPath())
            {
                haste.AddLine(74, 196, 34, 230);
                Faixa(g, haste, 10, Branco);
            }
            using (GraphicsPath ponta = Arredondado(new RectangleF(20, 222, 26, 20), 9))
                Corpo(g, ponta, 3);

            // concha da frente: espuma cinza aparecendo atras do casco
            using (GraphicsPath espuma = new GraphicsPath())
            {
                espuma.AddEllipse(86, 98, 70, 112);
                using (SolidBrush br = new SolidBrush(Almofada)) g.FillPath(br, espuma);
                using (Pen pen = new Pen(Contorno, 3)) g.DrawPath(pen, espuma);
            }
            using (GraphicsPath casco = new GraphicsPath())
            {
                casco.AddEllipse(62, 94, 82, 120);
                Corpo(g, casco, 4);
            }
            // tampa lisa por dentro do casco
            using (GraphicsPath tampa = new GraphicsPath())
            using (Pen pen = new Pen(Sombra, 3))
            {
                tampa.AddEllipse(74, 110, 58, 88);
                g.DrawPath(pen, tampa);
            }
            // o G da Logitech em relevo na tampa
            using (Pen pen = new Pen(Contorno, 6))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawArc(pen, 88, 136, 30, 36, 20, 290);
                g.DrawLine(pen, 117, 156, 104, 156);
                g.DrawLine(pen, 117, 156, 117, 166);
            }
        }
        return b;
    }

    /// <summary>
    /// X11 visto de cima: corpo simetrico e baixo, botoes separados da
    /// carcaca por um vinco, roda com o botao de DPI logo atras, os dois
    /// botoes laterais na esquerda e a barbatana da Attack Shark na palma.
    /// </summary>
    static Bitmap Mouse()
    {
        Bitmap b = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using (Graphics g = Abrir(b))
        {
            GraphicsPath corpo = new GraphicsPath();
            corpo.AddBezier(128, 20, 166, 20, 180, 44, 182, 90);
            corpo.AddBezier(182, 90, 184, 130, 192, 168, 186, 200);
            corpo.AddBezier(186, 200, 180, 228, 156, 238, 128, 238);
            corpo.AddBezier(128, 238, 100, 238, 76, 228, 70, 200);
            corpo.AddBezier(70, 200, 64, 168, 72, 130, 74, 90);
            corpo.AddBezier(74, 90, 76, 44, 90, 20, 128, 20);
            corpo.CloseFigure();

            // botoes laterais, saindo um pouco pela borda esquerda
            using (GraphicsPath l1 = Arredondado(new RectangleF(64, 104, 14, 26), 6)) Corpo(g, l1, 3);
            using (GraphicsPath l2 = Arredondado(new RectangleF(66, 134, 14, 24), 6)) Corpo(g, l2, 3);

            Corpo(g, corpo, 4);

            using (Pen pen = new Pen(Contorno, 3f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                // vao entre os dois botoes, aberto como nos mouses leves
                g.DrawLine(pen, 124, 23, 124, 100);
                g.DrawLine(pen, 132, 23, 132, 100);
                // vinco que solta os botoes da carcaca
                g.DrawBezier(pen, 76, 112, 100, 124, 156, 124, 180, 112);
            }
            using (SolidBrush br = new SolidBrush(Sombra))
                g.FillRectangle(br, 125.5f, 24, 5, 76);

            // roda de rolagem
            using (GraphicsPath roda = Arredondado(new RectangleF(119, 40, 18, 36), 8))
            {
                using (SolidBrush br = new SolidBrush(Escuro)) g.FillPath(br, roda);
                using (Pen pen = new Pen(Color.FromArgb(0x6E, 0x73, 0x7D), 2))
                    for (int y = 47; y <= 69; y += 7) g.DrawLine(pen, 122, y, 134, y);
            }
            // botao de DPI atras da roda
            using (GraphicsPath dpi = Arredondado(new RectangleF(121, 84, 14, 12), 5))
            {
                using (SolidBrush br = new SolidBrush(Almofada)) g.FillPath(br, dpi);
                using (Pen pen = new Pen(Contorno, 2)) g.DrawPath(pen, dpi);
            }

            // barbatana de tubarao na palma
            using (GraphicsPath fin = new GraphicsPath())
            {
                fin.AddBezier(108, 196, 116, 182, 126, 166, 146, 156);
                fin.AddBezier(146, 156, 140, 170, 140, 184, 148, 196);
                fin.CloseFigure();
                using (SolidBrush br = new SolidBrush(Contorno)) g.FillPath(br, fin);
            }
            corpo.Dispose();
        }
        return b;
    }
}
