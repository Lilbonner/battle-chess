using System.Drawing;
using System.Drawing.Drawing2D;

namespace BattleChess;

// Состояние анимации одного персонажа.
public class Pose
{
    public float Walk;          // фаза шага (радианы)
    public bool Moving;
    public float Attack = -1;   // 0..1 — ход удара, -1 — нет удара
    public float Block;         // 0..1 — защитная стойка
    public float Hurt;          // 0..1 — вспышка/отдача от удара
    public float Death;         // 0..1 — прогресс гибели
    public int DeathKind;       // 0 падение, 1 раздавлен, 2 рассыпался, 3 отброшен, 4 сожжён,
                                // 5 заморожен и разбит, 6 подброшен ввысь, 7 вбит в землю, 8 окаменел, 9 оглушён
    public int Facing = 1;      // 1 — вправо, -1 — влево
    public float Alpha = 1;
    public float Squash = 1;
    public float Lift;          // подъём над землёй (в клетках)
    public float Tilt;          // наклон (градусы)
    public float Glow;          // магическое свечение
    public float Char;          // обугливание 0..1
    public float Freeze;        // обледенение 0..1
    public float Stone;         // окаменение 0..1
}

public class Palette
{
    public Color Main, Shade, Dark, Accent, Cloth, Skin, Metal, Hair, Outline, Eye;

    public static readonly Palette White = new()
    {
        Main = Color.FromArgb(240, 234, 216), Shade = Color.FromArgb(196, 184, 156), Dark = Color.FromArgb(128, 116, 92),
        Accent = Color.FromArgb(232, 190, 64), Cloth = Color.FromArgb(62, 96, 196), Skin = Color.FromArgb(242, 204, 172),
        Metal = Color.FromArgb(214, 220, 230), Hair = Color.FromArgb(236, 214, 140), Outline = Color.FromArgb(48, 40, 30),
        Eye = Color.FromArgb(120, 200, 255),
    };

    public static readonly Palette Black = new()
    {
        Main = Color.FromArgb(74, 68, 80), Shade = Color.FromArgb(50, 45, 56), Dark = Color.FromArgb(26, 22, 30),
        Accent = Color.FromArgb(200, 40, 48), Cloth = Color.FromArgb(120, 22, 34), Skin = Color.FromArgb(198, 176, 160),
        Metal = Color.FromArgb(118, 124, 138), Hair = Color.FromArgb(30, 26, 30), Outline = Color.FromArgb(8, 6, 10),
        Eye = Color.FromArgb(255, 70, 40),
    };

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(a.A,
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public Palette Tint(Color target, float t, float alpha)
    {
        Color F(Color c)
        {
            var m = t > 0 ? Mix(c, target, t) : c;
            return Color.FromArgb((int)(255 * Math.Clamp(alpha, 0, 1)), m);
        }
        return new Palette
        {
            Main = F(Main), Shade = F(Shade), Dark = F(Dark), Accent = F(Accent), Cloth = F(Cloth), Skin = F(Skin),
            Metal = F(Metal), Hair = F(Hair), Outline = F(Outline), Eye = F(Eye),
        };
    }
}

// Набор примитивов в локальных координатах фигуры: ноги в (0,0), ось Y вверх — отрицательная.
public partial class Painter
{
    Graphics g;
    Palette pal;
    Pose pose;
    float time;
    Pen outline;
    int alphaByte;

    public static float Ease(float t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

    // Кривая удара: замах (до -1), удар (+1), возврат к 0. Момент попадания — ImpactAt.
    public const float ImpactAt = 0.55f;
    public static float Swing(float a)
    {
        if (a < 0) return 0;
        if (a < 0.4f) return -Ease(a / 0.4f);
        if (a < ImpactAt) return -1 + 2 * Ease((a - 0.4f) / (ImpactAt - 0.4f));
        if (a < 0.75f) return 1;
        return 1 - Ease((a - 0.75f) / 0.25f);
    }

    public void Draw(Graphics graphics, int piece, PointF feet, float pxPerCell, Pose p, float t, bool isWhite)
    {
        g = graphics; pose = p; time = t;
        var basePal = isWhite ? Palette.White : Palette.Black;
        float alpha = p.Alpha;
        if (p.DeathKind == 2 && p.Death > 0) alpha *= 1 - Ease(p.Death);
        if (p.DeathKind is 0 or 3 or 4 && p.Death > 0.6f) alpha *= 1 - Ease((p.Death - 0.6f) / 0.4f);
        if (p.DeathKind == 1 && p.Death > 0.7f) alpha *= 1 - Ease((p.Death - 0.7f) / 0.3f);
        if (p.DeathKind == 5 && p.Death > 0.55f) alpha *= 1 - Ease((p.Death - 0.55f) / 0.12f);
        if (p.DeathKind is 6 or 9 && p.Death > 0.65f) alpha *= 1 - Ease((p.Death - 0.65f) / 0.35f);
        if (p.DeathKind == 7 && p.Death > 0.5f) alpha *= 1 - Ease((p.Death - 0.5f) / 0.5f);
        if (p.DeathKind == 8 && p.Death > 0.68f) alpha *= 1 - Ease((p.Death - 0.68f) / 0.2f);
        if (alpha <= 0.01f) return;
        alphaByte = (int)(255 * alpha);

        Color tintTo = Color.White; float tintAmt = 0;
        if (p.Hurt > 0) { tintTo = Color.FromArgb(255, 80, 60); tintAmt = p.Hurt * 0.6f; }
        if (p.Char > 0) { tintTo = Color.FromArgb(20, 14, 10); tintAmt = p.Char * 0.85f; }
        if (p.Freeze > 0) { tintTo = Color.FromArgb(170, 220, 255); tintAmt = p.Freeze * 0.75f; }
        if (p.Stone > 0) { tintTo = Color.FromArgb(128, 126, 120); tintAmt = p.Stone * 0.9f; }
        if (p.DeathKind == 2 && p.Death > 0) { tintTo = Color.FromArgb(200, 230, 255); tintAmt = Math.Min(1, p.Death * 2); }
        pal = basePal.Tint(tintTo, tintAmt, alpha);
        using var op = new Pen(pal.Outline, 2.2f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        outline = op;

        var state = g.Save();
        float s = pxPerCell / 100f;
        float squash = p.Squash;
        float tilt = p.Tilt;
        float recoil = -p.Hurt * 10 * p.Facing;
        if (p.DeathKind == 1) squash *= 1 - 0.85f * Ease(p.Death * 1.6f);
        if (p.DeathKind == 0) tilt += -p.Facing * 90 * Ease(p.Death * 1.4f);
        if (p.DeathKind == 3) tilt += -p.Facing * 540 * p.Death;
        if (p.DeathKind == 6) tilt += p.Facing * 720 * p.Death * p.Death;
        if (p.DeathKind == 9)
        {
            float u = p.Death;
            tilt += u < 0.45f ? MathF.Sin(u * 45) * 14 * (u / 0.45f) : -p.Facing * 90 * Ease((u - 0.45f) * 2.4f);
        }
        if (p.DeathKind == 8 && p.Death > 0.6f) squash *= 1 - 0.35f * Ease((p.Death - 0.6f) / 0.3f);

        g.TranslateTransform(feet.X + recoil * s, feet.Y - p.Lift * pxPerCell);
        if (Math.Abs(tilt) > 0.01f)
        {
            // вращение вокруг середины фигуры при отбрасывании, вокруг ног — при падении
            float pivot = p.DeathKind is 3 or 6 ? -50 * s : 0;
            g.TranslateTransform(0, pivot);
            g.RotateTransform(tilt);
            g.TranslateTransform(0, -pivot);
        }
        g.ScaleTransform(s * p.Facing, s * squash);

        switch (Math.Abs(piece))
        {
            case P.Pawn: Pawn(); break;
            case P.Knight: Knight(); break;
            case P.Bishop: Bishop(); break;
            case P.Rook: Rook(); break;
            case P.Queen: Queen(); break;
            case P.King: King(); break;
        }
        g.Restore(state);
    }

    // ---------- примитивы ----------
    Color A(Color c, int a = 255) => Color.FromArgb(Math.Min(c.A, a * alphaByte / 255), c);

    void Fill(GraphicsPath path, Color top, Color bottom, bool stroke = true)
    {
        var b = path.GetBounds();
        if (b.Height < 0.5f) b.Height = 0.5f;
        using var br = new LinearGradientBrush(new RectangleF(b.X, b.Y - 0.5f, b.Width + 1, b.Height + 1), top, bottom, LinearGradientMode.Vertical);
        g.FillPath(br, path);
        if (stroke) g.DrawPath(outline, path);
    }

    void Poly(Color c, params float[] xy) => Poly(c, Palette.Mix(c, pal.Dark, 0.35f), xy);

    void Poly(Color top, Color bottom, params float[] xy)
    {
        var pts = new PointF[xy.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[2 * i], xy[2 * i + 1]);
        using var path = new GraphicsPath();
        path.AddPolygon(pts);
        Fill(path, top, bottom);
    }

    void Curve(Color c, params float[] xy)
    {
        var pts = new PointF[xy.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[2 * i], xy[2 * i + 1]);
        using var path = new GraphicsPath();
        path.AddClosedCurve(pts, 0.4f);
        Fill(path, c, Palette.Mix(c, pal.Dark, 0.35f));
    }

    void Oval(Color c, float cx, float cy, float rx, float ry, bool stroke = true)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
        Fill(path, Palette.Mix(c, Color.White, 0.15f), Palette.Mix(c, pal.Dark, 0.3f), stroke);
    }

    void Limb(Color c, float x1, float y1, float x2, float y2, float w)
    {
        using var po = new Pen(pal.Outline, w + 4.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(po, x1, y1, x2, y2);
        using var pi = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pi, x1, y1, x2, y2);
    }

    void Line(Color c, float w, float x1, float y1, float x2, float y2)
    {
        using var p = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(p, x1, y1, x2, y2);
    }

    void GlowDot(Color c, float x, float y, float r)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(x - r, y - r, 2 * r, 2 * r);
        using var br = new PathGradientBrush(path) { CenterColor = A(c), SurroundColors = new[] { Color.FromArgb(0, c) } };
        g.FillPath(br, path);
    }

    // Нога с коленом: бедро в (hx,hy), длина len, угол фазы ang.
    void Leg(Color c, Color boot, float hx, float hy, float len, float ang, float w)
    {
        float kx = hx + MathF.Sin(ang) * len * 0.5f, ky = hy + MathF.Cos(ang) * len * 0.5f;
        float bend = Math.Max(0, -MathF.Sin(ang)) * 0.6f;
        float fx = kx + MathF.Sin(ang - bend) * len * 0.5f, fy = ky + MathF.Cos(ang - bend) * len * 0.5f;
        Limb(c, hx, hy, kx, ky, w);
        Limb(c, kx, ky, fx, fy, w * 0.9f);
        Oval(boot, fx + 3, fy - 2, w * 0.75f, w * 0.45f);
    }

    float WalkSin(float offset = 0) => pose.Moving ? MathF.Sin(pose.Walk + offset) : 0;
    float Bob() => pose.Moving ? MathF.Abs(MathF.Sin(pose.Walk)) * -3 : MathF.Sin(time * 2.2f) * 0.8f;
}
