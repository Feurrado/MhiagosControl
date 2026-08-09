using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

/// <summary>
/// Gera os ativos embutidos no executavel a partir dos PNGs originais:
///   - MhiagosControl.ico  (icone do executavel, multi-resolucao)
///   - tray.ico            (icone da bandeja)
///   - cooler.png          (vista superior redimensionada para a previa)
///
/// Os PNGs vem com fundo branco opaco. Removemos o fundo por preenchimento
/// a partir das bordas, o que preserva areas claras internas (o texto "MC"
/// nao encosta na borda, entao nao e afetado).
/// </summary>
static class MakeAssets
{
    static int Main(string[] args)
    {
        try
        {
            string assets = args.Length > 0 ? args[0] : "assets";
            string outDir = args.Length > 1 ? args[1] : "assets";

            // um unico desenho serve aos dois usos
            string icon = Path.Combine(assets, "app-icon.png");

            Build(icon, Path.Combine(outDir, "MhiagosControl.ico"),
                  new int[] { 16, 20, 24, 32, 48, 64, 128, 256 });

            Build(icon, Path.Combine(outDir, "tray.ico"),
                  new int[] { 16, 20, 24, 32, 40, 48, 64 });

            Resize(Path.Combine(assets, "superior-cooler.png"), Path.Combine(outDir, "cooler.png"), 900);

            Console.WriteLine("ativos gerados em " + Path.GetFullPath(outDir));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERRO: " + ex.Message);
            return 1;
        }
    }

    static void Build(string src, string dst, int[] sizes)
    {
        using (Bitmap raw = new Bitmap(src))
        using (Bitmap cut = RemoveBackground(raw))
        using (Bitmap sq = Squarify(Trim(cut)))
        {
            WriteIco(sq, sizes, dst);
            Console.WriteLine(Path.GetFileName(dst) + "  <- " + Path.GetFileName(src) +
                              "  (" + sq.Width + "px, " + sizes.Length + " tamanhos)");
        }
    }

    static void Resize(string src, string dst, int max)
    {
        using (Bitmap raw = new Bitmap(src))
        {
            int w = raw.Width, h = raw.Height;
            double f = (double)max / Math.Max(w, h);
            if (f > 1) f = 1;
            using (Bitmap outp = Scale(raw, (int)(w * f), (int)(h * f)))
                outp.Save(dst, ImageFormat.Png);
            Console.WriteLine(Path.GetFileName(dst) + "  <- " + Path.GetFileName(src) +
                              "  (" + (int)(w * f) + "x" + (int)(h * f) + ")");
        }
    }

    /// <summary>Preenchimento a partir das quatro bordas, tolerancia sobre o branco.</summary>
    static Bitmap RemoveBackground(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        Bitmap outp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        Color[,] px = new Color[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[x, y] = src.GetPixel(x, y);

        bool[,] bg = new bool[w, h];
        Queue<Point> q = new Queue<Point>();

        for (int x = 0; x < w; x++) { Seed(px, bg, q, x, 0); Seed(px, bg, q, x, h - 1); }
        for (int y = 0; y < h; y++) { Seed(px, bg, q, 0, y); Seed(px, bg, q, w - 1, y); }

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };
        while (q.Count > 0)
        {
            Point p = q.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = p.X + dx[i], ny = p.Y + dy[i];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || bg[nx, ny]) continue;
                if (!IsBackground(px[nx, ny])) continue;
                bg[nx, ny] = true;
                q.Enqueue(new Point(nx, ny));
            }
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (bg[x, y]) { outp.SetPixel(x, y, Color.Transparent); continue; }

                Color c = px[x, y];
                // suaviza a borda: pixel claro vizinho de fundo vira semitransparente
                if (Near(c) && HasBackgroundNeighbour(bg, x, y, w, h))
                {
                    int a = (int)(255 * (1.0 - Lightness(c)));
                    outp.SetPixel(x, y, Color.FromArgb(Math.Max(0, Math.Min(255, a)), c));
                }
                else outp.SetPixel(x, y, c);
            }
        }
        return outp;
    }

    static void Seed(Color[,] px, bool[,] bg, Queue<Point> q, int x, int y)
    {
        if (bg[x, y] || !IsBackground(px[x, y])) return;
        bg[x, y] = true;
        q.Enqueue(new Point(x, y));
    }

    static bool IsBackground(Color c) { return c.A > 200 && c.R > 233 && c.G > 233 && c.B > 233; }
    static bool Near(Color c) { return c.R > 200 && c.G > 200 && c.B > 200; }
    static double Lightness(Color c) { return (c.R + c.G + c.B) / 765.0; }

    static bool HasBackgroundNeighbour(bool[,] bg, int x, int y, int w, int h)
    {
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int nx = x + i, ny = y + j;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                if (bg[nx, ny]) return true;
            }
        return false;
    }

    /// <summary>Recorta as margens vazias.</summary>
    static Bitmap Trim(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (src.GetPixel(x, y).A > 8)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return (Bitmap)src.Clone();

        Rectangle r = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        Bitmap outp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(outp))
            g.DrawImage(src, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
        return outp;
    }

    /// <summary>Deixa quadrado, centralizado, com folga de 4% para nao encostar na borda.</summary>
    static Bitmap Squarify(Bitmap src)
    {
        int side = (int)(Math.Max(src.Width, src.Height) * 1.08);
        Bitmap outp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(outp))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, (side - src.Width) / 2, (side - src.Height) / 2, src.Width, src.Height);
        }
        src.Dispose();
        return outp;
    }

    static Bitmap Scale(Bitmap src, int w, int h)
    {
        Bitmap outp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(outp))
        {
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(src, new Rectangle(0, 0, w, h));
        }
        return outp;
    }

    /// <summary>
    /// Escreve um .ico. Cabecalho: ICONDIR (6 bytes) + ICONDIRENTRY (16 bytes).
    ///
    /// Tamanhos ate 128 px vao em DIB (bitmap 32 bits + mascara). A classe
    /// System.Drawing.Icon do .NET Framework NAO le entradas em PNG de forma
    /// confiavel - o Explorer le, mas o icone da bandeja sai quebrado. Apenas
    /// 256 px usa PNG, onde DIB ficaria grande e a classe Icon nao e usada.
    /// </summary>
    static void WriteIco(Bitmap source, int[] sizes, string path)
    {
        List<byte[]> blobs = new List<byte[]>();
        foreach (int s in sizes)
        {
            using (Bitmap b = Scale(source, s, s))
            using (MemoryStream ms = new MemoryStream())
            {
                if (s >= 256) b.Save(ms, ImageFormat.Png);
                else WriteDib(b, ms);
                blobs.Add(ms.ToArray());
            }
        }

        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((short)0);                 // reservado
            w.Write((short)1);                 // 1 = icone
            w.Write((short)sizes.Length);

            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s));   // 0 significa 256
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0);              // paleta
                w.Write((byte)0);              // reservado
                w.Write((short)1);             // planos
                w.Write((short)32);            // bits por pixel
                w.Write(blobs[i].Length);
                w.Write(offset);
                offset += blobs[i].Length;
            }
            foreach (byte[] b in blobs) w.Write(b);
        }
    }

    /// <summary>
    /// Imagem no formato DIB usado dentro de um .ico:
    ///   BITMAPINFOHEADER com altura dobrada (mascara XOR + mascara AND),
    ///   pixels BGRA de baixo para cima, e a mascara AND em 1 bit por pixel
    ///   com linhas alinhadas em 4 bytes.
    /// </summary>
    static void WriteDib(Bitmap b, Stream outp)
    {
        int w = b.Width, h = b.Height;
        BinaryWriter bw = new BinaryWriter(outp);

        bw.Write(40);                 // biSize
        bw.Write(w);                  // biWidth
        bw.Write(h * 2);              // biHeight (XOR + AND)
        bw.Write((short)1);           // biPlanes
        bw.Write((short)32);          // biBitCount
        bw.Write(0);                  // biCompression = BI_RGB
        bw.Write(w * h * 4);          // biSizeImage
        bw.Write(0); bw.Write(0);     // resolucao
        bw.Write(0); bw.Write(0);     // paleta

        // XOR: BGRA, linhas de baixo para cima
        for (int y = h - 1; y >= 0; y--)
            for (int x = 0; x < w; x++)
            {
                Color c = b.GetPixel(x, y);
                bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
            }

        // AND: com alfa de 32 bits a mascara fica zerada, mas o bloco e obrigatorio
        int stride = ((w + 31) / 32) * 4;
        byte[] zeros = new byte[stride];
        for (int y = 0; y < h; y++) bw.Write(zeros);

        bw.Flush();
    }
}
