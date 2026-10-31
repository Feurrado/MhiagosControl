using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

/// <summary>
/// Desenha o icone do Bluetooth em PNG transparente de 256 px.
///
/// O headset G325 e o mouse X11 nao saem mais daqui: desenhados de cabeca,
/// nao pareciam os aparelhos. Agora sao decalcados de fotos e ficam em
/// assets\svg\g325.svg e assets\svg\x11.svg, exportados em 256 x 256 com
/// fundo transparente para assets\g325.png e assets\x11.png. O contorno
/// cinza e o mesmo daqui, para o branco aparecer tambem sobre o cartao claro
/// do tema claro.
///
/// Uso: MakePeripherals.exe [pasta de saida]
/// </summary>
class MakePeripherals
{
    static readonly Color Contorno = Color.FromArgb(0x9A, 0xA0, 0xAA);
    static readonly Color Branco = Color.FromArgb(0xFB, 0xFB, 0xFC);
    static readonly Color Sombra = Color.FromArgb(0xDD, 0xE0, 0xE5);

    static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
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
}
