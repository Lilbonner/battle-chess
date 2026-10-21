using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace BattleChess;

// Рисует иконку приложения (король на щите) и сохраняет её в .ico.
static class IconMaker
{
    public static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = size / 256f;
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(8 * s, 8 * s, 240 * s, 240 * s);
            using var br = new LinearGradientBrush(new RectangleF(0, 0, size, size), Color.FromArgb(70, 56, 120), Color.FromArgb(18, 14, 36), 90f);
            g.FillPath(br, path);
            using var pen = new Pen(Color.FromArgb(232, 196, 110), Math.Max(1, 10 * s));
            g.DrawPath(pen, path);
        }
        var pose = new Pose { Facing = 1 };
        new Painter().Draw(g, P.King, new PointF(124 * s, 236 * s), 172 * s, pose, 0, true);
        return bmp;
    }

    public static void Save(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        // 256 — PNG, остальные размеры — классический DIB (так иконку понимают все версии Windows).
        var pngs = sizes.Select(sz =>
        {
            using var b = Render(sz);
            using var ms = new MemoryStream();
            if (sz >= 256) b.Save(ms, ImageFormat.Png);
            else WriteDib(ms, b);
            return ms.ToArray();
        }).ToList();
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }

    static void WriteDib(Stream s, Bitmap b)
    {
        int n = b.Width;
        int maskStride = ((n + 31) / 32) * 4;
        using var w = new BinaryWriter(s, System.Text.Encoding.ASCII, true);
        w.Write(40); w.Write(n); w.Write(n * 2);
        w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(n * n * 4 + maskStride * n);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = n - 1; y >= 0; y--)
            for (int x = 0; x < n; x++)
            {
                var c = b.GetPixel(x, y);
                w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
            }
        for (int y = n - 1; y >= 0; y--)
        {
            var row = new byte[maskStride];
            for (int x = 0; x < n; x++)
                if (b.GetPixel(x, y).A < 128) row[x / 8] |= (byte)(0x80 >> (x % 8));
            w.Write(row);
        }
    }
}
