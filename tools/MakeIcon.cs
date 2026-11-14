using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>
/// 生成 app.ico（GammaLift 的太阳/光晕图标 —— 光晕与光线意为"提亮暗部"）。
/// 编译: csc /out:MakeIcon.exe tools\MakeIcon.cs; 运行: MakeIcon.exe app.ico
/// 输出多尺寸 ICO: 16/20/24/32/48/64 用 BMP(DIB) 帧, 128/256 用 PNG 帧。
/// </summary>
static class MakeIcon
{
    private static readonly int[] Sizes = new int[] { 16, 20, 24, 32, 48, 64, 128, 256 };

    private static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "app.ico";

        byte[][] frames = new byte[Sizes.Length][];
        for (int i = 0; i < Sizes.Length; i++)
        {
            using (Bitmap bmp = Draw(Sizes[i]))
                frames[i] = Sizes[i] >= 128 ? ToPng(bmp) : ToDib(bmp);
        }

        using (FileStream fs = new FileStream(path, FileMode.Create))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((ushort)0);              // reserved
            w.Write((ushort)1);              // type = icon
            w.Write((ushort)Sizes.Length);

            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));   // width (0 = 256)
                w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));   // height
                w.Write((byte)0);                                  // color count
                w.Write((byte)0);                                  // reserved
                w.Write((ushort)1);                                // planes
                w.Write((ushort)32);                               // bpp
                w.Write(frames[i].Length);
                w.Write(offset);
                offset += frames[i].Length;
            }
            for (int i = 0; i < Sizes.Length; i++)
                w.Write(frames[i]);
        }

        Console.WriteLine("已生成 " + path + " (" + Sizes.Length + " 个尺寸)");
    }

    /// <summary>画一枚"太阳 + 光晕"图标：深色圆角底板 + 暖色光晕 + 8 道光线 + 渐变核心。</summary>
    private static Bitmap Draw(int s)
    {
        Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float pad = s * 0.02f;
            using (GraphicsPath plate = RoundRect(new RectangleF(pad, pad, s - pad * 2, s - pad * 2), s * 0.24f))
            using (LinearGradientBrush lb = new LinearGradientBrush(
                       new PointF(0, 0), new PointF(0, s),
                       Color.FromArgb(255, 40, 41, 50), Color.FromArgb(255, 18, 18, 23)))
            {
                g.FillPath(lb, plate);
            }

            float c = s / 2f;

            // 外圈光晕
            float glowR = s * 0.44f;
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(c - glowR, c - glowR, glowR * 2, glowR * 2);
                using (PathGradientBrush pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = Color.FromArgb(s <= 20 ? 110 : 150, 255, 176, 64);
                    pb.SurroundColors = new Color[] { Color.FromArgb(0, 255, 176, 64) };
                    g.FillPath(pb, gp);
                }
            }

            // 8 道光线（小尺寸下加大核心、缩短光线, 否则 16px 会糊成一团）
            bool small = s <= 24;
            float r1 = s * (small ? 0.28f : 0.24f);
            float r2 = s * (small ? 0.40f : 0.36f);
            using (Pen pen = new Pen(Color.FromArgb(255, 255, 192, 96), Math.Max(1f, s * (small ? 0.085f : 0.075f))))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4.0;
                    g.DrawLine(pen,
                        c + (float)(Math.Cos(a) * r1), c + (float)(Math.Sin(a) * r1),
                        c + (float)(Math.Cos(a) * r2), c + (float)(Math.Sin(a) * r2));
                }
            }

            // 核心（亮黄 → 琥珀渐变）
            float coreR = s * (small ? 0.21f : 0.16f);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(c - coreR, c - coreR, coreR * 2, coreR * 2);
                using (PathGradientBrush pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = Color.FromArgb(255, 255, 240, 205);
                    pb.SurroundColors = new Color[] { Color.FromArgb(255, 244, 162, 38) };
                    g.FillPath(pb, gp);
                }
            }
        }
        return bmp;
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        GraphicsPath p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>ICO 里的 BMP 帧：BITMAPINFOHEADER(biHeight 为两倍) + 自下而上的 BGRA + 全零 AND 掩码。</summary>
    private static byte[] ToDib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int xorSize = w * h * 4;
        int maskRow = ((w + 31) / 32) * 4;
        int maskSize = maskRow * h;
        byte[] data = new byte[40 + xorSize + maskSize];

        using (MemoryStream ms = new MemoryStream(data))
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            bw.Write(40);
            bw.Write(w);
            bw.Write(h * 2);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write(0);
            bw.Write(xorSize + maskSize);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);
        }

        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] row = new byte[bd.Stride];
            for (int y = 0; y < h; y++)
            {
                IntPtr p = new IntPtr(bd.Scan0.ToInt64() + (long)(h - 1 - y) * bd.Stride);
                Marshal.Copy(p, row, 0, bd.Stride);
                Buffer.BlockCopy(row, 0, data, 40 + y * w * 4, w * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
        return data;
    }

    private static byte[] ToPng(Bitmap bmp)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }
}
