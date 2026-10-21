using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace BattleChess;

// Плоская (2D) доска или оригинальная доска Battle Chess; Spin — анимация поворота доски (0..π).
public class Camera
{
    public bool Flat = true, Flip;
    public float Spin;
    public OriginalArt Original;          // не null — оригинальная доска Battle Chess
    public float OScale = 1, OX, OY;
    public float OScaleY = 1, OCrop;     // оригинал: вертикальное растяжение доски («подъём камеры») и обрезка стены сверху
    public float Cell, Left, Top;
    public int Height = 2;               // высота камеры: 0 — низко, 1 — средне, 2 — высоко

    public static readonly string[] HeightNames = { "Низко", "Средне", "Высоко" };

    public void Fit(RectangleF area)
    {
        if (Original != null)
        {
            // Доска оригинала — готовая картинка, поэтому «подъём камеры» — это растяжение доски по вертикали
            // при неизменных спрайтах: ряды расходятся и фигуры меньше заслоняют друг друга.
            float k = Height switch { 0 => 1f, 1 => 1.25f, _ => 1.5f };
            OCrop = Height == 0 ? 0 : 40;                       // сверху — только стена, её частично убираем
            float boardH = OriginalArt.ScreenH - OCrop;
            float over = Math.Max(0, 105 - (75 - OCrop) * k);    // фигуры дальнего ряда выше края картинки
            float oUnits = boardH * k + over;
            OScale = Math.Min(area.Width / OriginalArt.ScreenW, area.Height / oUnits);
            if (OScale >= 2) OScale = MathF.Floor(OScale * 4) / 4;
            OScaleY = OScale * k;
            OX = area.X + (area.Width - OriginalArt.ScreenW * OScale) / 2;
            OY = area.Y + (area.Height - oUnits * OScale) / 2 + over * OScale;
            return;
        }
        Cell = Math.Min(area.Width - 70, area.Height - 70) / 8;
        Left = area.X + (area.Width - 8 * Cell) / 2;
        Top = area.Y + (area.Height - 8 * Cell) / 2;
    }

    // Координаты «как видит зритель»: при перевороте доски — зеркально по обеим осям.
    public float VX(float file) => Flip ? 8 - file : file;
    public float VZ(float rank) => Flip ? 8 - rank : rank;

    // С учётом идущего поворота доски вокруг её центра (против часовой стрелки, если смотреть сверху).
    (float x, float z) View(float file, float rank)
    {
        float vx = VX(file), vz = VZ(rank);
        if (Spin == 0) return (vx, vz);
        float c = MathF.Cos(Spin), s = MathF.Sin(Spin), dx = vx - 4, dz = vz - 4;
        return (4 + dx * c - dz * s, 4 + dx * s + dz * c);
    }

    // Глубина для порядка отрисовки: чем больше, тем дальше от зрителя.
    public float Depth(float file, float rank) => View(file, rank).z;

    public PointF GroundV(float vx, float vz) => new(Left + vx * Cell, Top + (8 - vz) * Cell);

    public PointF Project(float file, float rank, float h = 0)
    {
        var (vx, vz) = View(file, rank);
        if (Original != null)
        {
            var o = Original.ScreenPoint(vx, vz, out float pitch);
            return new PointF(OX + o.X * OScale, OY + (o.Y - OCrop) * OScaleY - h * pitch * 0.8f * OScale);
        }
        var p = GroundV(vx, vz);
        p.Y -= h * Cell * 0.3f;
        return p;
    }

    public float PxPerCell(float rank)
    {
        if (Original == null) return Cell;
        Original.ScreenPoint(4, VZ(rank), out float pitch);
        return pitch * OScale;
    }

    public PointF[] Quad(float f0, float r0, float f1, float r1) => new[]
    {
        Project(f0, r0), Project(f1, r0), Project(f1, r1), Project(f0, r1),
    };

    public bool Pick(PointF pt, out int file, out int rank)
    {
        if (Original != null)
        {
            for (int f = 0; f < 8; f++)
                for (int r = 0; r < 8; r++)
                {
                    using var path = new GraphicsPath();
                    path.AddPolygon(Quad(f, r, f + 1, r + 1));
                    if (path.IsVisible(pt)) { file = f; rank = r; return true; }
                }
            file = rank = -1;
            return false;
        }
        float vx = (pt.X - Left) / Cell, vz = 8 - (pt.Y - Top) / Cell;
        float fc = Flip ? 8 - vx : vx, rc = Flip ? 8 - vz : vz;
        file = (int)MathF.Floor(fc); rank = (int)MathF.Floor(rc);
        return file >= 0 && file < 8 && rank >= 0 && rank < 8;
    }
}

public static class BoardArt
{
    static readonly Color Light = Color.FromArgb(222, 214, 192), Dark = Color.FromArgb(86, 94, 110);

    public static void Background(Graphics g, Rectangle r, bool plain = false)
    {
        if (plain) { g.FillRectangle(Brushes.Black, r); return; }
        using var br = new LinearGradientBrush(r, Color.FromArgb(28, 30, 46), Color.FromArgb(6, 6, 12), LinearGradientMode.Vertical);
        g.FillRectangle(br, r);
        using var path = new GraphicsPath();
        path.AddEllipse(r.X - r.Width * 0.1f, r.Y + r.Height * 0.25f, r.Width * 1.2f, r.Height * 1.0f);
        using var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb(60, 90, 80, 120), SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) } };
        g.FillPath(pg, path);
    }

    public static void Board(Graphics g, Camera cam)
    {
        if (cam.Original != null)
        {
            var st = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            var dest = new RectangleF(cam.OX, cam.OY, OriginalArt.ScreenW * cam.OScale, (OriginalArt.ScreenH - cam.OCrop) * cam.OScaleY);
            g.DrawImage(cam.Original.Board3D, dest, new RectangleF(0, cam.OCrop, OriginalArt.ScreenW, OriginalArt.ScreenH - cam.OCrop), GraphicsUnit.Pixel);
            g.Restore(st);
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        const float m = 0.5f;
        var frame = cam.Quad(-m, -m, 8 + m, 8 + m);
        if (cam.Flip) frame = cam.Quad(8 + m, 8 + m, -m, -m);
        // тень доски
        using (var sh = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            g.FillPolygon(sh, frame.Select(q => new PointF(q.X + 8, q.Y + 8)).ToArray());
        var fb = Bounds(frame);
        using (var wood = new LinearGradientBrush(fb, Color.FromArgb(140, 92, 52), Color.FromArgb(78, 48, 26), LinearGradientMode.Vertical))
            g.FillPolygon(wood, frame);
        using (var pen = new Pen(Color.FromArgb(200, 170, 120), 1.5f)) g.DrawPolygon(pen, frame);

        for (int f = 0; f < 8; f++)
            for (int r = 0; r < 8; r++)
            {
                var q = cam.Quad(f, r, f + 1, r + 1);
                var c = (f + r) % 2 == 1 ? Light : Dark;
                var qb = Bounds(q);
                using var br = new LinearGradientBrush(qb, Palette.Mix(c, Color.White, 0.12f), Palette.Mix(c, Color.Black, 0.12f), 60f);
                g.FillPolygon(br, q);
            }
        var inner = cam.Quad(0, 0, 8, 8);
        using (var pen = new Pen(Color.FromArgb(40, 26, 14), 2f)) g.DrawPolygon(pen, inner);

        // подписи
        using var tb = new SolidBrush(Color.FromArgb(235, 215, 170));
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < 8; i++)
        {
            float nearRank = cam.Flip ? 8.25f : -0.25f, leftFile = cam.Flip ? 8.25f : -0.25f;
            DrawLabel(g, ((char)('a' + i)).ToString(), cam.Project(i + 0.5f, nearRank), cam.PxPerCell(nearRank), tb, sf);
            DrawLabel(g, (i + 1).ToString(), cam.Project(leftFile, i + 0.5f), cam.PxPerCell(i + 0.5f), tb, sf);
        }
    }

    static void DrawLabel(Graphics g, string s, PointF p, float scale, Brush b, StringFormat sf)
    {
        using var f = new Font("Segoe UI", Math.Max(7, scale * 0.2f), FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString(s, f, b, p, sf);
    }

    public static RectangleF Bounds(PointF[] pts)
    {
        float x0 = pts.Min(p => p.X), y0 = pts.Min(p => p.Y), x1 = pts.Max(p => p.X), y1 = pts.Max(p => p.Y);
        return new RectangleF(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    public static void Cell(Graphics g, Camera cam, int sq, Color c, float inset = 0)
    {
        var q = cam.Quad(P.File(sq) + inset, P.Rank(sq) + inset, P.File(sq) + 1 - inset, P.Rank(sq) + 1 - inset);
        using var b = new SolidBrush(c);
        g.FillPolygon(b, q);
    }

    public static void CellOutline(Graphics g, Camera cam, int sq, Color c, float w)
    {
        var q = cam.Quad(P.File(sq) + 0.04f, P.Rank(sq) + 0.04f, P.File(sq) + 0.96f, P.Rank(sq) + 0.96f);
        using var p = new Pen(c, w) { LineJoin = LineJoin.Round };
        g.DrawPolygon(p, q);
    }

    public static PointF[] GroundEllipse(Camera cam, float f, float r, float rx, float ry, int n = 20)
    {
        var pts = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            float a = i * MathF.PI * 2 / n;
            pts[i] = cam.Project(f + MathF.Cos(a) * rx, r + MathF.Sin(a) * ry);
        }
        return pts;
    }

    // Фигура в 2D-режиме: силуэт (внешние контуры контурного шахматного символа), залитый цветом стороны,
    // поверх — линии самого символа. «Чёрные» символы шрифта не сплошные, поэтому их не используем.
    static readonly string Outline = " ♙♘♗♖♕♔";
    public static void Glyph(Graphics g, int piece, PointF center, float cell, float alpha, float lift = 0)
    {
        if (alpha <= 0.01f) return;
        using var ff = new FontFamily("Segoe UI Symbol");
        using var lines = new GraphicsPath(FillMode.Alternate);
        lines.AddString(Outline[Math.Abs(piece)].ToString(), ff, 0, cell * 0.78f, PointF.Empty, StringFormat.GenericTypographic);
        var b = lines.GetBounds();
        using var m = new Matrix();
        m.Translate(center.X - b.X - b.Width / 2, center.Y - b.Y - b.Height / 2 - lift);
        lines.Transform(m);
        using var body = Silhouette(lines);
        int a = (int)(255 * Math.Clamp(alpha, 0, 1));
        bool w = piece > 0;
        using var shadow = new SolidBrush(Color.FromArgb(a / 3, 0, 0, 0));
        g.TranslateTransform(2, 3);
        g.FillPath(shadow, body);
        g.TranslateTransform(-2, -3);
        var bb = body.GetBounds();
        using var fill = new LinearGradientBrush(new RectangleF(bb.X, bb.Y - 1, bb.Width + 1, bb.Height + 2),
            w ? Color.FromArgb(a, 255, 252, 240) : Color.FromArgb(a, 70, 64, 76),
            w ? Color.FromArgb(a, 200, 188, 160) : Color.FromArgb(a, 20, 16, 24), LinearGradientMode.Vertical);
        g.FillPath(fill, body);
        using var ink = new SolidBrush(w ? Color.FromArgb(a, 40, 32, 20) : Color.FromArgb(a, 160, 142, 108));
        g.FillPath(ink, lines);
    }

    // Только внешние контуры: подконтуры, лежащие внутри другого, отбрасываются.
    static GraphicsPath Silhouette(GraphicsPath src)
    {
        var parts = new List<GraphicsPath>();
        using (var it = new GraphicsPathIterator(src))
        {
            using var sub = new GraphicsPath();
            while (it.NextSubpath(sub, out _) > 0) { parts.Add((GraphicsPath)sub.Clone()); sub.Reset(); }
        }
        var bounds = parts.Select(p => p.GetBounds()).ToList();
        var outer = new GraphicsPath(FillMode.Winding);
        for (int i = 0; i < parts.Count; i++)
        {
            bool inside = false;
            for (int j = 0; j < parts.Count && !inside; j++)
                inside = j != i && bounds[j].Width * bounds[j].Height > bounds[i].Width * bounds[i].Height && bounds[j].Contains(bounds[i]);
            if (!inside) outer.AddPath(parts[i], false);
            parts[i].Dispose();
        }
        return outer;
    }
}
