using System.Drawing;

namespace BattleChess;

// Шесть персонажей: солдат (пешка), всадник (конь), епископ (слон),
// каменный голем-башня (ладья), волшебница (ферзь) и король.
public partial class Painter
{
    struct WPose { public float X, Y, Ang; public WPose(float x, float y, float a) { X = x; Y = y; Ang = a; } }

    static WPose Lerp(WPose a, WPose b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Ang + (b.Ang - a.Ang) * t);

    float Ready()
    {
        float a = pose.Attack;
        if (a < 0) return 0;
        if (a < 0.15f) return Ease(a / 0.15f);
        if (a > 0.85f) return 1 - Ease((a - 0.85f) / 0.15f);
        return 1;
    }

    // Положение руки с оружием: покой → замах → удар, плюс блок.
    WPose Weapon(WPose idle, WPose up, WPose strike, WPose block)
    {
        float sw = Swing(pose.Attack);
        var basePose = Lerp(idle, Lerp(up, strike, 0.5f), Ready());
        var r = sw < 0 ? Lerp(basePose, up, -sw) : Lerp(basePose, strike, sw);
        return Lerp(r, block, Ease(pose.Block));
    }

    static PointF Dir(float deg) => new(MathF.Cos(deg * MathF.PI / 180), MathF.Sin(deg * MathF.PI / 180));

    // ---------------- Пешка: солдат с копьём и щитом ----------------
    void Pawn()
    {
        float b = Bob(), w = WalkSin();
        Leg(pal.Shade, pal.Dark, -2, -34 + b, 34, 0.5f * w, 7);
        var hand = Weapon(new(13, -46, -90), new(-2, -54, -8), new(30, -54, -4), new(16, -60, -50));
        hand.Y += b;
        // древко за телом, если копьё поднято
        PointF d = Dir(hand.Ang);
        Line(pal.Outline, 5, hand.X - d.X * 30, hand.Y - d.Y * 30, hand.X + d.X * 44, hand.Y + d.Y * 44);
        Line(Palette.Mix(pal.Shade, pal.Dark, 0.3f), 2.6f, hand.X - d.X * 30, hand.Y - d.Y * 30, hand.X + d.X * 44, hand.Y + d.Y * 44);
        float tx = hand.X + d.X * 44, ty = hand.Y + d.Y * 44;
        var n = new PointF(-d.Y, d.X);
        Poly(pal.Metal, tx + d.X * 12, ty + d.Y * 12, tx + n.X * 4, ty + n.Y * 4, tx - n.X * 4, ty - n.Y * 4);

        Leg(pal.Main, pal.Dark, 2, -34 + b, 34, -0.5f * w, 7);
        Poly(pal.Cloth, -14, -24 + b, 14, -24 + b, 12, -38 + b, -12, -38 + b);
        Poly(pal.Cloth, -12, -36 + b, 12, -36 + b, 10, -62 + b, -10, -62 + b);
        Line(pal.Dark, 4, -12, -37 + b, 12, -37 + b);
        Oval(pal.Accent, 5, -37 + b, 2.5f, 2.5f, false);
        Oval(pal.Skin, 2, -71 + b, 9, 9.5f);
        Oval(pal.Outline, 8, -72 + b, 1.4f, 1.6f, false);
        Poly(pal.Metal, -12, -72 + b, 14, -72 + b, 11, -80 + b, 2, -85 + b, -8, -81 + b);
        Line(pal.Metal, 2.5f, 10, -72 + b, 10, -66 + b);

        // рука и щит
        Limb(pal.Cloth, 2, -58 + b, hand.X, hand.Y, 6);
        Oval(pal.Skin, hand.X, hand.Y, 3.5f, 3.5f);
        float shx = 6 + Ease(pose.Block) * 8, shy = -48 + b - Ease(pose.Block) * 6;
        Oval(pal.Accent, shx, shy, 8, 12);
        Oval(pal.Main, shx, shy, 3, 4);
    }

    // ---------------- Конь: рыцарь верхом ----------------
    void Knight()
    {
        float b = Bob() * 1.3f;
        float sw = Swing(pose.Attack);
        var st = g.Save();
        float rear = Math.Max(0, -sw) * 18;
        g.TranslateTransform(-24 + Math.Max(0, sw) * 8, 0);
        g.RotateTransform(-rear);
        g.TranslateTransform(24, 0);

        Color horse = pal.Main, horseDark = Palette.Mix(pal.Main, pal.Dark, 0.4f);
        float gp = pose.Walk * 1.4f;
        float LegA(float ph) => pose.Moving ? 0.55f * MathF.Sin(gp + ph) : 0;
        // дальние ноги
        LegH(horseDark, -14, -42 + b, LegA(MathF.PI));
        LegH(horseDark, 22, -42 + b, LegA(0.5f));
        // хвост
        Poly(pal.Hair, -27, -54 + b, -40, -48 + b, -46, -26 + b, -38, -32 + b, -30, -44 + b);
        Oval(horse, 0, -50 + b, 31, 13.5f);
        // ближние ноги
        LegH(horse, -22, -42 + b, LegA(0));
        LegH(horse, 16, -42 + b, LegA(MathF.PI + 0.5f));
        // шея и голова
        Poly(horse, 16, -58 + b, 25, -82 + b, 36, -84 + b, 34, -62 + b, 26, -45 + b);
        Poly(horse, 25, -86 + b, 38, -92 + b, 56, -77 + b, 54, -70 + b, 44, -70 + b, 33, -74 + b);
        Poly(horse, 29, -88 + b, 30, -98 + b, 35, -90 + b);
        Poly(pal.Hair, 23, -84 + b, 30, -92 + b, 22, -60 + b, 15, -55 + b);
        Oval(pal.Outline, 40, -83 + b, 1.6f, 1.6f, false);
        Oval(pal.Outline, 53, -73 + b, 1.2f, 1.2f, false);
        // попона
        Poly(pal.Cloth, -24, -62 + b, 18, -62 + b, 22, -42 + b, 12, -36 + b, 2, -42 + b, -8, -36 + b, -18, -42 + b, -27, -46 + b);
        Line(pal.Accent, 2.5f, -26, -45 + b, 21, -43 + b);
        // всадник
        Limb(pal.Metal, -2, -62 + b, 8, -50 + b, 7);
        Limb(pal.Metal, 8, -50 + b, 6, -38 + b, 6);
        var hand = Weapon(new(9, -74, -62), new(-4, -84, -40), new(26, -74, -4), new(14, -82, -20));
        hand.Y += b;
        Poly(pal.Metal, -8, -62 + b, 8, -62 + b, 7, -89 + b, -7, -89 + b);
        Poly(pal.Cloth, -6, -64 + b, 6, -64 + b, 5, -85 + b, -5, -85 + b);
        Line(pal.Accent, 2.2f, 0, -82 + b, 0, -68 + b);
        Line(pal.Accent, 2.2f, -4, -77 + b, 4, -77 + b);
        Poly(pal.Metal, -8, -88 + b, 8, -88 + b, 9, -100 + b, 0, -107 + b, -8, -100 + b);
        Line(pal.Outline, 2, 2, -96 + b, 8, -96 + b);
        Curve(pal.Accent, -2, -105 + b, -14, -112 + b, -24, -101 + b, -12, -103 + b);
        // копьё
        PointF d = Dir(hand.Ang);
        Line(pal.Outline, 6, hand.X - d.X * 22, hand.Y - d.Y * 22, hand.X + d.X * 72, hand.Y + d.Y * 72);
        Line(pal.Shade, 3.4f, hand.X - d.X * 22, hand.Y - d.Y * 22, hand.X + d.X * 72, hand.Y + d.Y * 72);
        float tx = hand.X + d.X * 72, ty = hand.Y + d.Y * 72;
        var n = new PointF(-d.Y, d.X);
        Poly(pal.Metal, tx + d.X * 14, ty + d.Y * 14, tx + n.X * 5, ty + n.Y * 5, tx - n.X * 5, ty - n.Y * 5);
        Poly(pal.Accent, tx - d.X * 8, ty - d.Y * 8, tx - d.X * 22 + n.X * 10, ty - d.Y * 22 + n.Y * 10, tx - d.X * 22, ty - d.Y * 22);
        Limb(pal.Metal, 2, -82 + b, hand.X, hand.Y, 6);
        g.Restore(st);
    }

    void LegH(Color c, float hx, float hy, float ang)
    {
        float kx = hx + MathF.Sin(ang) * 20, ky = hy + MathF.Cos(ang) * 20;
        float a2 = ang - Math.Max(0, MathF.Sin(ang)) * 0.9f;
        float fx = kx + MathF.Sin(a2) * 20, fy = ky + MathF.Cos(a2) * 20;
        Limb(c, hx, hy, kx, ky, 7);
        Limb(c, kx, ky, fx, fy, 5);
        Oval(pal.Dark, fx + 1, fy - 1, 4.5f, 3);
    }

    // ---------------- Слон: епископ с посохом ----------------
    void Bishop()
    {
        float b = Bob(), w = WalkSin(), sway = w * 3;
        Oval(pal.Dark, -6 + w * 7, -3, 7, 3.5f);
        Oval(pal.Dark, 6 - w * 7, -3, 7, 3.5f);
        Limb(pal.Main, -2, -62 + b, -10, -42 + b, 7);
        Poly(pal.Main, -20 + sway, -3, 22 + sway, -3, 12, -66 + b, -12, -66 + b);
        Poly(pal.Accent, -3 + sway * 0.5f, -3, 5 + sway * 0.5f, -3, 3, -64 + b, -1, -64 + b);
        Poly(pal.Cloth, -15, -67 + b, 15, -67 + b, 11, -56 + b, 1, -51 + b, -11, -56 + b);
        Oval(pal.Skin, 2, -74 + b, 8.5f, 9);
        Oval(pal.Outline, 7, -75 + b, 1.3f, 1.5f, false);
        Poly(pal.Main, -9, -80 + b, 11, -80 + b, 9, -100 + b, 1, -112 + b, -7, -100 + b);
        Line(pal.Accent, 2.2f, -8, -81 + b, 10, -81 + b);
        Line(pal.Accent, 2.2f, 2, -82 + b, 2, -104 + b);
        Line(pal.Accent, 2.2f, -3, -94 + b, 7, -94 + b);

        var hand = Weapon(new(16, -54, -90), new(4, -84, -150), new(26, -64, -25), new(16, -70, -110));
        hand.Y += b;
        PointF d = Dir(hand.Ang);
        float tx = hand.X + d.X * 46, ty = hand.Y + d.Y * 46;
        Line(pal.Outline, 5, hand.X - d.X * 50, hand.Y - d.Y * 50, tx, ty);
        Line(pal.Accent, 2.8f, hand.X - d.X * 50, hand.Y - d.Y * 50, tx, ty);
        using (var pen = new Pen(pal.Accent, 3.2f)) g.DrawArc(pen, tx - 2, ty - 12, 13, 13, 90, 270);
        float glow = Math.Max(pose.Glow, Ready() * 0.6f);
        if (glow > 0) GlowDot(Color.FromArgb(255, 250, 200), tx + 4, ty - 6, 16 * glow);
        Limb(pal.Main, 2, -62 + b, hand.X, hand.Y, 7);
        Oval(pal.Skin, hand.X, hand.Y, 3.5f, 3.5f);
    }

    // ---------------- Ладья: каменный голем-башня ----------------
    void Rook()
    {
        float b = Bob() * 1.5f, w = WalkSin();
        var fist = Weapon(new(26, -36, 0), new(10, -118, 0), new(48, -16, 0), new(28, -76, 0));
        fist.Y += b;
        // задняя рука
        StoneArm(Palette.Mix(pal.Shade, pal.Dark, 0.4f), -10, -64 + b, fist.X - 18, fist.Y - 2);
        Oval(pal.Shade, -10 + w * 6, -7 - Math.Max(0, w) * 5, 10, 7);
        Oval(pal.Main, 10 - w * 6, -7 - Math.Max(0, -w) * 5, 10, 7);
        Poly(pal.Shade, pal.Dark, -25, -9 + b, 25, -9 + b, 25, -19 + b, -25, -19 + b);
        Poly(pal.Main, pal.Shade, -21, -18 + b, 21, -18 + b, 20, -82 + b, -20, -82 + b);
        var brick = Color.FromArgb(Math.Min(alphaByte, 140), pal.Dark);
        for (int row = 0; row < 5; row++)
        {
            float y = -30 - row * 12 + b;
            Line(brick, 1.4f, -20, y, 20, y);
            for (float x = (row % 2 == 0 ? -12 : -6); x < 20; x += 12) Line(brick, 1.4f, x, y, x, y + 12);
        }
        Poly(pal.Main, pal.Shade, -24, -81 + b, 24, -81 + b, 24, -90 + b, -24, -90 + b);
        foreach (float mx in new[] { -24f, -6f, 12f })
            Poly(pal.Main, pal.Shade, mx, -89 + b, mx + 12, -89 + b, mx + 12, -101 + b, mx, -101 + b);
        // глаза-бойницы
        foreach (float ex in new[] { 2f, 13f })
        {
            Poly(pal.Dark, ex - 3, -64 + b, ex + 3, -64 + b, ex + 3, -71 + b, ex - 3, -71 + b);
            GlowDot(pal.Eye, ex, -67.5f + b, 6 + pose.Glow * 6);
        }
        Line(pal.Dark, 2, 0, -52 + b, 16, -50 + b);
        // передняя рука
        StoneArm(Palette.Mix(pal.Main, pal.Shade, 0.45f), 12, -64 + b, fist.X, fist.Y);
    }

    // Рука голема из трёх каменных глыб: плечо, предплечье, кулак.
    void StoneArm(Color c, float sx, float sy, float fx, float fy)
    {
        float mx = (sx + fx) / 2 + 5, my = (sy + fy) / 2 + 4;
        Poly(c, sx - 8, sy - 7, sx + 8, sy - 8, sx + 9, sy + 7, sx - 7, sy + 8);
        Poly(c, mx - 7, my - 6, mx + 7, my - 7, mx + 8, my + 6, mx - 6, my + 7);
        Poly(Palette.Mix(c, Color.White, 0.1f), fx - 11, fy - 9, fx + 10, fy - 10, fx + 12, fy + 8, fx - 9, fy + 10);
        Line(Color.FromArgb(Math.Min(alphaByte, 120), pal.Dark), 1.4f, fx - 4, fy - 9, fx - 3, fy + 9);
        Line(Color.FromArgb(Math.Min(alphaByte, 120), pal.Dark), 1.4f, fx + 3, fy - 9, fx + 4, fy + 9);
    }

    // ---------------- Ферзь: волшебница ----------------
    void Queen()
    {
        float b = Bob(), w = WalkSin(), sway = w * 3;
        Curve(pal.Hair, -4, -92 + b, -15, -82 + b, -14, -58 + b, -3, -64 + b);
        Limb(pal.Main, -2, -72 + b, -12, -54 + b, 6);
        Poly(pal.Cloth, -27 + sway, -2, 29 + sway, -2, 14, -40 + b, 8, -61 + b, -8, -61 + b, -13, -40 + b);
        Poly(pal.Main, -5 + sway, -2, 11 + sway, -2, 5, -58 + b, -1, -58 + b);
        Line(pal.Accent, 3, -26 + sway, -3, 28 + sway, -3);
        Poly(pal.Main, -8, -60 + b, 8, -60 + b, 7, -77 + b, -7, -77 + b);
        Line(pal.Accent, 2, -6, -75 + b, 6, -75 + b);
        Oval(pal.Skin, 2, -85 + b, 8, 8.5f);
        Oval(pal.Outline, 7, -86 + b, 1.3f, 1.5f, false);
        Oval(Color.FromArgb(alphaByte, 190, 40, 60), 8, -80 + b, 1.5f, 1, false);
        Curve(pal.Hair, -8, -86 + b, -3, -95 + b, 8, -93 + b, 1, -89 + b);
        Poly(pal.Accent, -7, -91 + b, 10, -91 + b, 11, -102 + b, 7, -97 + b, 2, -105 + b, -3, -97 + b, -8, -102 + b);
        Oval(pal.Eye, 2, -95 + b, 1.8f, 1.8f, false);

        var hand = Weapon(new(14, -56, -80), new(8, -104, -100), new(32, -82, -20), new(16, -80, -60));
        hand.Y += b;
        PointF d = Dir(hand.Ang);
        float tx = hand.X + d.X * 20, ty = hand.Y + d.Y * 20;
        Line(pal.Outline, 4.5f, hand.X - d.X * 8, hand.Y - d.Y * 8, tx, ty);
        Line(pal.Accent, 2.4f, hand.X - d.X * 8, hand.Y - d.Y * 8, tx, ty);
        float glow = Math.Max(pose.Glow, Ready() * 0.5f);
        GlowDot(pal.Eye, tx, ty, 6 + glow * 18);
        Oval(pal.Eye, tx, ty, 4, 4);
        Limb(pal.Main, 2, -72 + b, hand.X, hand.Y, 6);
        Oval(pal.Skin, hand.X, hand.Y, 3.2f, 3.2f);
    }

    // ---------------- Король ----------------
    void King()
    {
        float b = Bob(), w = WalkSin();
        Poly(pal.Cloth, -4, -82 + b, -26 - w * 3, -4, 8, -4, 6, -72 + b);
        Leg(pal.Shade, pal.Dark, -2, -36 + b, 36, 0.4f * w, 8);
        Limb(pal.Main, -4, -74 + b, -12, -52 + b, 7);
        Leg(pal.Main, pal.Dark, 2, -36 + b, 36, -0.4f * w, 8);
        Poly(pal.Main, -16, -28 + b, 16, -28 + b, 12, -78 + b, -12, -78 + b);
        Poly(pal.Cloth, -4, -28 + b, 6, -28 + b, 4, -76 + b, -2, -76 + b);
        Line(pal.Accent, 4, -14, -44 + b, 14, -44 + b);
        var ermine = Color.FromArgb(alphaByte, 250, 248, 240);
        Oval(ermine, 0, -78 + b, 15, 6);
        foreach (float ex in new[] { -8f, 0f, 8f }) Oval(Color.FromArgb(alphaByte, 20, 20, 20), ex, -78 + b, 1.2f, 1.6f, false);
        Oval(pal.Skin, 2, -91 + b, 10, 10);
        Color beard = Palette.Mix(pal.Main, Color.White, 0.3f);
        if (pal == null || pal.Main.R < 128) beard = Palette.Mix(pal.Shade, Color.Gray, 0.4f);
        Poly(beard, -4, -88 + b, 13, -88 + b, 11, -76 + b, 4, -68 + b, -3, -78 + b);
        Oval(pal.Outline, 8, -93 + b, 1.4f, 1.6f, false);
        Poly(pal.Accent, -9, -99 + b, 12, -99 + b, 13, -111 + b, 8, -105 + b, 2, -115 + b, -4, -105 + b, -10, -111 + b);
        Line(pal.Accent, 2.6f, 2, -114 + b, 2, -124 + b);
        Line(pal.Accent, 2.6f, -2, -120 + b, 6, -120 + b);
        Oval(pal.Eye, 2, -103 + b, 2, 2, false);

        var hand = Weapon(new(15, -52, 70), new(-2, -100, -125), new(26, -62, 15), new(18, -84, -10));
        hand.Y += b;
        PointF d = Dir(hand.Ang);
        var n = new PointF(-d.Y, d.X);
        float tx = hand.X + d.X * 48, ty = hand.Y + d.Y * 48;
        Poly(pal.Metal, hand.X + d.X * 6 + n.X * 3, hand.Y + d.Y * 6 + n.Y * 3, tx, ty, hand.X + d.X * 6 - n.X * 3, hand.Y + d.Y * 6 - n.Y * 3);
        Line(pal.Accent, 3, hand.X + d.X * 5 + n.X * 8, hand.Y + d.Y * 5 + n.Y * 8, hand.X + d.X * 5 - n.X * 8, hand.Y + d.Y * 5 - n.Y * 8);
        Oval(pal.Accent, hand.X - d.X * 8, hand.Y - d.Y * 8, 3, 3);
        Limb(pal.Main, 3, -74 + b, hand.X, hand.Y, 7);
        Oval(pal.Skin, hand.X, hand.Y, 3.8f, 3.8f);
    }
}
