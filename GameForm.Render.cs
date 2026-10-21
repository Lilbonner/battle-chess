using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BattleChess;

public partial class GameForm
{
    Bitmap boardCache;
    bool cacheValid;
    Point mouse;
    readonly List<(RectangleF r, string text, Action act)> buttons = new();
    RectangleF[] promoRects = Array.Empty<RectangleF>();
    readonly Random fxRnd = new();

    readonly Font titleFont = new("Georgia", 22f, FontStyle.Bold, GraphicsUnit.Pixel);
    readonly Font textFont = new("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font boldFont = new("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
    readonly Font smallFont = new("Segoe UI", 12.5f, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font moveFont = new("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font captionFont = new("Georgia", 18f, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel);

    static readonly Color Gold = Color.FromArgb(232, 196, 110);
    static readonly Color PanelText = Color.FromArgb(225, 215, 195);
    static readonly Color Dim = Color.FromArgb(150, 140, 125);

    RectangleF BoardArea => new(0, 0, Math.Max(100, canvas.Width - PanelW), canvas.Height);
    RectangleF PanelArea => new(canvas.Width - PanelW, 0, PanelW, canvas.Height);

    void InvalidateBoardCache() => cacheValid = false;

    void RebuildCache()
    {
        if (canvas.Width < 10 || canvas.Height < 10) return;
        if (boardCache == null || boardCache.Size != canvas.Size)
        {
            boardCache?.Dispose();
            boardCache = new Bitmap(canvas.Width, canvas.Height);
        }
        using var g = Graphics.FromImage(boardCache);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        var area = BoardArea;
        BoardArt.Background(g, Rectangle.Round(area), cam.Original != null);
        cam.Fit(new RectangleF(area.X + 16, area.Y + 40, area.Width - 32, area.Height - 52));
        BoardArt.Board(g, cam);
        cacheValid = true;
    }

    void Render(Graphics g)
    {
        if (!cacheValid || boardCache == null || boardCache.Size != canvas.Size) RebuildCache();
        if (boardCache == null) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        float zoomK = cam.Flat ? 0 : scene.ZoomK;
        var st = g.Save();
        if (zoomK > 0.001f)
        {
            BoardArt.Background(g, Rectangle.Round(BoardArea), cam.Original != null);
            g.SetClip(BoardArea);
            ApplyZoom(g, zoomK);
            BoardArt.Board(g, cam);
        }
        else
        {
            g.DrawImageUnscaled(boardCache, 0, 0);
            g.SetClip(BoardArea);
        }

        if (scene.Shake > 0)
            g.TranslateTransform((float)(fxRnd.NextDouble() - 0.5) * scene.Shake, (float)(fxRnd.NextDouble() - 0.5) * scene.Shake);
        if (!(Spinning && cam.Original != null)) DrawHighlights(g);
        DrawActors(g);
        DrawParticles(g);
        DrawEffects(g);
        g.Restore(st);

        if (scene.Flash > 0)
        {
            using var fb = new SolidBrush(Color.FromArgb((int)Math.Min(170, scene.Flash * 220), scene.FlashColor));
            g.FillRectangle(fb, BoardArea);
        }
        if (titleT > 0 && cam.Original != null)
        {
            var area = BoardArea;
            float a = Math.Clamp(titleT, 0, 1);
            var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = a };
            using var ia = new System.Drawing.Imaging.ImageAttributes();
            ia.SetColorMatrix(cm);
            var st2 = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            float ts = Math.Min(area.Width / OriginalArt.ScreenW, area.Height / OriginalArt.ScreenH);
            var r = new RectangleF(area.X + (area.Width - OriginalArt.ScreenW * ts) / 2, area.Y + (area.Height - OriginalArt.ScreenH * ts) / 2,
                                   OriginalArt.ScreenW * ts, OriginalArt.ScreenH * ts);
            g.DrawImage(cam.Original.Title, Rectangle.Round(r), 0, 0, OriginalArt.ScreenW, OriginalArt.ScreenH, GraphicsUnit.Pixel, ia);
            g.Restore(st2);
        }
        DrawOverlays(g);
        g.ResetClip();
        DrawPanel(g);
    }

    // Наезд камеры: точка боя плавно переезжает в центр и увеличивается.
    void ApplyZoom(Graphics g, float k)
    {
        var area = BoardArea;
        var focus = cam.Project(scene.FocusF, scene.FocusR, 0.55f);
        float px = cam.PxPerCell(scene.FocusR);
        float want = Math.Clamp(area.Height * 0.21f / px, 1.2f, 2.6f);
        float z = 1 + (want - 1) * k;
        var dest = new PointF(focus.X + (area.X + area.Width / 2 - focus.X) * k, focus.Y + (area.Y + area.Height * 0.55f - focus.Y) * k);
        g.TranslateTransform(dest.X, dest.Y);
        g.ScaleTransform(z, z);
        g.TranslateTransform(-focus.X, -focus.Y);
    }

    // ---------------- доска ----------------
    void DrawHighlights(Graphics g)
    {
        if (lastMove is Move lm)
        {
            BoardArt.Cell(g, cam, lm.From, Color.FromArgb(60, 255, 214, 90));
            BoardArt.Cell(g, cam, lm.To, Color.FromArgb(85, 255, 214, 90));
        }
        if (hint is Move hm)
        {
            int a = (int)(90 + 60 * MathF.Sin(scene.Time * 6));
            BoardArt.Cell(g, cam, hm.From, Color.FromArgb(a, 90, 190, 255));
            BoardArt.Cell(g, cam, hm.To, Color.FromArgb(a, 90, 190, 255));
        }
        if (!scene.Busy && pos.InCheck(pos.Side))
        {
            int k = pos.KingSquare(pos.Side);
            BoardArt.Cell(g, cam, k, Color.FromArgb((int)(110 + 50 * MathF.Sin(scene.Time * 8)), 255, 40, 30));
        }
        if (selected >= 0)
        {
            BoardArt.Cell(g, cam, selected, Color.FromArgb(110, 90, 255, 130));
            foreach (var m in selMoves)
            {
                if (m.IsCapture) BoardArt.CellOutline(g, cam, m.To, Color.FromArgb(220, 255, 90, 60), Math.Max(2, cam.PxPerCell(P.Rank(m.To) + 0.5f) * 0.05f));
                else
                {
                    using var b = new SolidBrush(Color.FromArgb(170, 50, 200, 100));
                    g.FillPolygon(b, BoardArt.GroundEllipse(cam, P.File(m.To) + 0.5f, P.Rank(m.To) + 0.5f, 0.15f, 0.15f));
                }
            }
        }
        if (hoverSq >= 0 && !scene.Busy && HumanToMove && state == GameState.Playing && promoChoices == null)
        {
            bool own = pos.B[hoverSq] != 0 && Math.Sign(pos.B[hoverSq]) == pos.Side;
            bool target = selMoves.Any(m => m.To == hoverSq);
            if (own || target) BoardArt.CellOutline(g, cam, hoverSq, Color.FromArgb(200, 255, 255, 230), 2.2f);
        }
    }

    void DrawActors(Graphics g)
    {
        var combat = cam.Original != null ? scene.Combat : null;
        var list = scene.Actors.Where(a => !(a.Dead && a.Pose.Death >= 1))
            .Where(a => combat == null || (a != combat.Victim && a != combat.Killer))
            .OrderByDescending(a => cam.Depth(a.F, a.R)).ThenBy(a => a.OShift.Y).ToList();   // на одной клетке перед боем жертва (ниже) впереди
        bool combatDrawn = combat == null;
        foreach (var a in list)
        {
            if (!combatDrawn && cam.Depth(a.F, a.R) < cam.Depth(combat.F, combat.R) - 0.01f) { DrawCombatScene(g, combat); combatDrawn = true; }
            float px = cam.PxPerCell(a.R);
            float fade = a.Pose.Death > 0 ? 1 - a.Pose.Death : 1;
            if (!cam.Flat)
            {
                // тень
                float lift = Math.Clamp(a.Pose.Lift, 0, 2);
                int sa = (int)(110 * fade * (1 - lift * 0.4f));
                if (sa > 4 && cam.Original == null)
                {
                    float rx = (a.Type == P.Knight ? 0.42f : 0.3f) * (1 - lift * 0.25f);
                    using var sb = new SolidBrush(Color.FromArgb(sa, 0, 0, 0));
                    g.FillPolygon(sb, BoardArt.GroundEllipse(cam, a.F, a.R, rx, rx * 0.55f, 16));
                }
                if (selected >= 0 && !scene.Busy && P.Sq((int)a.F, (int)a.R) == selected)
                {
                    int ga = (int)(120 + 80 * MathF.Sin(scene.Time * 6));
                    using var pen = new Pen(Color.FromArgb(ga, 120, 255, 150), Math.Max(2, px * 0.04f));
                    g.DrawPolygon(pen, BoardArt.GroundEllipse(cam, a.F, a.R, 0.38f, 0.3f, 24));
                }
                float oldAlpha = a.Pose.Alpha;
                if (scene.ZoomK > 0 && scene.FightA != null && a != scene.FightA && a != scene.FightD
                    && cam.VZ(a.R) < cam.VZ(scene.FocusR) - 0.1f && Math.Abs(cam.VX(a.F) - cam.VX(scene.FocusF)) < 1.7f)
                    a.Pose.Alpha = oldAlpha * (1 - 0.75f * scene.ZoomK);
                if (cam.Original == null || !DrawOriginalSprite(g, a, fade))
                    painter.Draw(g, a.Piece, cam.Project(a.F, a.R), px, a.Pose, scene.Time, a.IsWhite);
                a.Pose.Alpha = oldAlpha;
            }
            else
            {
                var c = cam.Project(a.F, a.R);
                BoardArt.Glyph(g, a.Piece, c, cam.Cell, fade * a.Pose.Alpha, a.Pose.Lift * cam.Cell * 0.3f);
            }
        }
        if (!combatDrawn) DrawCombatScene(g, combat);
    }

    // Кадр сцены поединка: победитель позади, побеждённый впереди, оба привязаны к клетке взятия.
    void DrawCombatScene(Graphics g, Scene.CombatView c)
    {
        var art = cam.Original;
        var anchor = cam.Project(c.F, c.R);
        float s = cam.OScale;
        var st = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var vf = c.VictimFrame;
        foreach (var (sc, frames, tick, actor) in new[] { (c.V.Attack, c.V.Attack.Killer, c.KillerTick, c.Killer), (vf.sc, vf.sc.Victim, vf.tick, c.Victim) })
        {
            if (frames.Length == 0 || tick < 0) continue;
            int idx = frames[tick];
            if (idx < 0) continue;
            var bmp = art.SceneImage(sc, idx, actor.IsWhite);
            var (ax, ay) = sc.Anchors[idx];
            g.DrawImage(bmp, anchor.X - ax * s, anchor.Y - ay * s, bmp.Width * s, bmp.Height * s);
        }
        g.Restore(st);
    }

    // Точное попадание мышью по оригинальному спрайту (по непрозрачному пикселю кадра).
    bool OriginalSpriteHit(Actor a, Point p)
    {
        var fr = OriginalFrame(a);
        if (fr == null) return false;
        var (bmp, ax, ay, origin) = fr.Value;
        float s = cam.OScale;
        int x = (int)MathF.Floor((p.X - (origin.X - ax * s)) / s), y = (int)MathF.Floor((p.Y - (origin.Y - ay * s)) / s);
        if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) return false;
        return bmp.GetPixel(x, y).A > 0;
    }

    // Текущий кадр фигуры в оригинальной графике и точка привязки на экране.
    (Bitmap bmp, int ax, int ay, PointF origin)? OriginalFrame(Actor a)
    {
        var art = cam.Original;
        var feet = cam.Project(a.F, a.R);
        feet.X += a.OShift.X * cam.OScale; feet.Y += a.OShift.Y * cam.OScale;
        var mv = originalMoves;
        if (mv != null && mv.Pieces.TryGetValue(a.Type, out var mp))
        {
            if (a.OSeq >= 0)
            {
                var f = mv.Frame(a.Type, a.OSeq, a.OFrame, a.IsWhite);
                if (f != null) return (f.Value.bmp, f.Value.ax, f.Value.ay, feet);
            }
            int pose;
            if (a.Type == P.Rook && a.OTower)
                pose = feet.X - a.OShift.X * cam.OScale - cam.OX > 160 * cam.OScale ? 36 : 37;
            else
            {
                int facing = a.OFacing >= 0 ? a.OFacing : scene.HomeFacing(a);
                // при повороте доски фигуры поворачиваются вместе с ней
                int steps = (int)MathF.Round(cam.Spin / (MathF.PI / 4));
                facing = ((facing - steps) % 8 + 8) % 8;
                pose = mp.Stand[facing];
                for (int d = 1; pose < 0 && d <= 4; d++)
                {
                    pose = mp.Stand[(facing + d) % 8];
                    if (pose < 0) pose = mp.Stand[(facing - d + 8) % 8];
                }
            }
            if (art.Poses.TryGetValue(pose, out var pf))
                return (a.IsWhite ? pf.White : pf.Black, pf.Ax, pf.Ay, feet);
        }
        int pose2 = art.PoseFor(a.Type, a.Dir, a.IsWhite, a.Pose.Moving);
        var fr2 = art.FrameFor(pose2, a.Pose.Moving, a.Pose.Walk);
        if (fr2 == null) return null;
        return (a.IsWhite ? fr2.White : fr2.Black, fr2.Ax, fr2.Ay, feet);
    }

    // Спрайт оригинальной Battle Chess: поза по направлению взгляда, цикл ходьбы при движении.
    bool DrawOriginalSprite(Graphics g, Actor a, float fade)
    {
        var of = OriginalFrame(a);
        if (of == null) return false;
        var (bmp, fax, fay, feet) = of.Value;
        float s = cam.OScale;
        feet.Y -= a.Pose.Lift * cam.PxPerCell(a.R) * 0.8f;
        float alpha = Math.Clamp(fade * a.Pose.Alpha, 0, 1);
        if (alpha <= 0.01f) return true;
        float flash = a.Pose.Hurt > 0.5f ? 0.55f : 0;
        var dest = new RectangleF(feet.X - fax * s, feet.Y - fay * s, bmp.Width * s, bmp.Height * s);
        var st = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        if (alpha >= 0.999f && flash == 0) g.DrawImage(bmp, dest);
        else
        {
            var cm = new System.Drawing.Imaging.ColorMatrix(new[]
            {
                new float[] { 1 - flash, 0, 0, 0, 0 },
                new float[] { 0, 1 - flash, 0, 0, 0 },
                new float[] { 0, 0, 1 - flash, 0, 0 },
                new float[] { 0, 0, 0, alpha, 0 },
                new float[] { flash, flash, flash, 0, 1 },
            });
            using var ia = new System.Drawing.Imaging.ImageAttributes();
            ia.SetColorMatrix(cm);
            g.DrawImage(bmp, Rectangle.Round(dest), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
        }
        g.Restore(st);
        return true;
    }

    void DrawParticles(Graphics g)
    {
        foreach (var p in scene.Particles)
        {
            var pt = cam.Project(p.F, p.R, p.H);
            float sc = cam.PxPerCell(p.R), sz = Math.Max(1, p.Size * sc), u = p.Life / p.Max;
            int a = (int)(255 * (1 - u));
            switch (p.Kind)
            {
                case 0:
                    using (var b1 = new SolidBrush(Color.FromArgb(a / 4, p.C))) g.FillEllipse(b1, pt.X - sz * 1.8f, pt.Y - sz * 1.8f, sz * 3.6f, sz * 3.6f);
                    using (var b2 = new SolidBrush(Color.FromArgb(a, p.C))) g.FillEllipse(b2, pt.X - sz * 0.7f, pt.Y - sz * 0.7f, sz * 1.4f, sz * 1.4f);
                    break;
                case 1:
                {
                    var q = cam.Project(p.F - p.VF * 0.05f, p.R - p.VR * 0.05f, p.H - p.VH * 0.05f);
                    using var pen = new Pen(Color.FromArgb(a, p.C), Math.Max(1.2f, sz * 0.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawLine(pen, pt, q);
                    break;
                }
                case 2:
                {
                    float r = sz * (1 + u * 1.6f);
                    using var b = new SolidBrush(Color.FromArgb(a / 2, p.C));
                    g.FillEllipse(b, pt.X - r, pt.Y - r * 0.8f, r * 2, r * 1.6f);
                    break;
                }
                case 3:
                {
                    var s = g.Save();
                    g.TranslateTransform(pt.X, pt.Y);
                    g.RotateTransform(p.Rot);
                    using var b = new SolidBrush(Color.FromArgb(a, p.C));
                    g.FillPolygon(b, new[] { new PointF(-sz, -sz * 0.6f), new PointF(sz * 0.8f, -sz), new PointF(sz, sz * 0.7f), new PointF(-sz * 0.5f, sz) });
                    g.Restore(s);
                    break;
                }
                case 4:
                {
                    float r = sz * (1.2f - u * 0.6f);
                    using var pen = new Pen(Color.FromArgb(a, p.C), Math.Max(1, sz * 0.25f));
                    float c = MathF.Cos(p.Rot * MathF.PI / 180) * r, s = MathF.Sin(p.Rot * MathF.PI / 180) * r;
                    g.DrawLine(pen, pt.X - c, pt.Y - s, pt.X + c, pt.Y + s);
                    g.DrawLine(pen, pt.X + s, pt.Y - c, pt.X - s, pt.Y + c);
                    break;
                }
            }
        }
    }

    void DrawEffects(Graphics g)
    {
        foreach (var e in scene.Effects)
        {
            float u = e.T / e.Dur, sc = cam.PxPerCell(e.R1);
            switch (e.Kind)
            {
                case 0: // молния
                {
                    var top = cam.Project(e.F1, e.R1, 10);
                    var bottom = cam.Project(e.F1, e.R1, e.H1);
                    if (top.Y < 0) top.Y = 0;
                    var rnd = new Random(e.Seed + (int)(e.T * 25));
                    int n = 12;
                    var pts = new PointF[n + 1];
                    for (int i = 0; i <= n; i++)
                    {
                        float k = i / (float)n;
                        float off = (i == 0 || i == n) ? 0 : (float)(rnd.NextDouble() - 0.5) * sc * 0.6f;
                        pts[i] = new PointF(top.X + (bottom.X - top.X) * k + off, top.Y + (bottom.Y - top.Y) * k);
                    }
                    int a = (int)(255 * (1 - u));
                    using (var glow = new Pen(Color.FromArgb(a / 3, e.C), sc * 0.18f) { LineJoin = LineJoin.Round }) g.DrawLines(glow, pts);
                    using (var core = new Pen(Color.FromArgb(a, Color.White), Math.Max(2, sc * 0.04f)) { LineJoin = LineJoin.Round }) g.DrawLines(core, pts);
                    break;
                }
                case 1: // магический шар
                {
                    float f = e.F1 + (e.F2 - e.F1) * u, r = e.R1 + (e.R2 - e.R1) * u, h = e.H1 + (e.H2 - e.H1) * u + e.Arc * 4 * u * (1 - u);
                    var p = cam.Project(f, r, h);
                    float rad = cam.PxPerCell(r) * (e.Arc > 1.5f ? 0.32f : 0.2f);
                    for (int i = 3; i >= 1; i--)
                    {
                        using var b = new SolidBrush(Color.FromArgb(i == 1 ? 255 : 60 / i, i == 1 ? Color.White : e.C));
                        float rr = rad * i * 0.7f;
                        g.FillEllipse(b, p.X - rr, p.Y - rr, rr * 2, rr * 2);
                    }
                    break;
                }
                case 2: // след удара мечом
                {
                    var p = cam.Project(e.F1, e.R1, e.H1);
                    float r = sc * 0.55f;
                    using var pen = new Pen(Color.FromArgb((int)(230 * (1 - u)), e.C), Math.Max(2, sc * 0.06f * (1 - u))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawArc(pen, p.X - r, p.Y - r, r * 2, r * 2, e.Seed > 0 ? 200 : -20, e.Seed > 0 ? 120 * (0.3f + u) : -120 * (0.3f + u));
                    break;
                }
                case 3: // ударная волна
                {
                    float rr = 0.2f + u * 1.4f;
                    using var pen = new Pen(Color.FromArgb((int)(200 * (1 - u)), e.C), Math.Max(2, sc * 0.07f * (1 - u)));
                    g.DrawPolygon(pen, BoardArt.GroundEllipse(cam, e.F1, e.R1, rr, rr, 28));
                    break;
                }
                case 5: // луч между двумя точками
                {
                    var p0 = cam.Project(e.F1, e.R1, e.H1);
                    var p1 = cam.Project(e.F2, e.R2, e.H2);
                    float k = MathF.Sin(MathF.PI * Math.Min(1, u * 1.2f));
                    float w = sc * 0.16f * (0.6f + 0.4f * MathF.Sin(e.T * 60));
                    using (var glow = new Pen(Color.FromArgb((int)(110 * k), e.C), w * 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(glow, p0, p1);
                    using (var core = new Pen(Color.FromArgb((int)(255 * k), Color.White), Math.Max(2, w * 0.45f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(core, p0, p1);
                    break;
                }
                case 6: // брошенное копьё
                {
                    float f = e.F1 + (e.F2 - e.F1) * u, r = e.R1 + (e.R2 - e.R1) * u, h = e.H1 + (e.H2 - e.H1) * u + e.Arc * 4 * u * (1 - u);
                    float u2 = Math.Min(1, u + 0.05f);
                    float f2 = e.F1 + (e.F2 - e.F1) * u2, r2 = e.R1 + (e.R2 - e.R1) * u2, h2 = e.H1 + (e.H2 - e.H1) * u2 + e.Arc * 4 * u2 * (1 - u2);
                    var p = cam.Project(f, r, h); var q = cam.Project(f2, r2, h2);
                    float dx = q.X - p.X, dy = q.Y - p.Y, len = MathF.Max(0.01f, MathF.Sqrt(dx * dx + dy * dy));
                    float L = sc * 0.55f; dx /= len; dy /= len;
                    var tip = new PointF(p.X + dx * L * 0.5f, p.Y + dy * L * 0.5f);
                    var tail = new PointF(p.X - dx * L * 0.5f, p.Y - dy * L * 0.5f);
                    using (var shaft = new Pen(Color.FromArgb(120, 90, 50), Math.Max(2, sc * 0.03f))) g.DrawLine(shaft, tail, tip);
                    using (var head = new SolidBrush(Color.FromArgb(220, 225, 235)))
                        g.FillPolygon(head, new[] { new PointF(tip.X + dx * L * 0.15f, tip.Y + dy * L * 0.15f), new PointF(tip.X - dy * L * 0.06f, tip.Y + dx * L * 0.06f), new PointF(tip.X + dy * L * 0.06f, tip.Y - dx * L * 0.06f) });
                    break;
                }
                case 7: // ледяные шипы вокруг цели
                {
                    var rnd = new Random(e.Seed);
                    float grow = Painter.Ease(Math.Min(1, u * 3)), fade = u > 0.7f ? 1 - (u - 0.7f) / 0.3f : 1;
                    for (int i = 0; i < 9; i++)
                    {
                        float ang = (float)(i * Math.PI * 2 / 9 + rnd.NextDouble() * 0.4);
                        float rr = 0.25f + (float)rnd.NextDouble() * 0.15f;
                        float hh = (0.5f + (float)rnd.NextDouble() * 0.6f) * grow;
                        var b = cam.Project(e.F1 + MathF.Cos(ang) * rr, e.R1 + MathF.Sin(ang) * rr * 0.6f);
                        var top = cam.Project(e.F1 + MathF.Cos(ang) * rr * 0.6f, e.R1 + MathF.Sin(ang) * rr * 0.35f, hh);
                        float bw = sc * 0.06f;
                        using var br = new SolidBrush(Color.FromArgb((int)(210 * fade), 180, 225, 255));
                        g.FillPolygon(br, new[] { new PointF(b.X - bw, b.Y), top, new PointF(b.X + bw, b.Y) });
                        using var pen = new Pen(Color.FromArgb((int)(230 * fade), Color.White), 1.2f);
                        g.DrawLine(pen, b, top);
                    }
                    break;
                }
                case 8: // смерч
                {
                    float fade = MathF.Sin(MathF.PI * u);
                    for (int i = 0; i < 9; i++)
                    {
                        float hh = i * 0.22f;
                        float rad = 0.12f + hh * 0.22f;
                        var c = cam.Project(e.F1 + MathF.Sin(e.T * 9 + i) * 0.06f, e.R1, hh);
                        float rx = rad * sc, ry = rx * 0.32f;
                        using var pen = new Pen(Color.FromArgb((int)(170 * fade), e.C), Math.Max(1.5f, sc * 0.025f));
                        g.DrawArc(pen, c.X - rx, c.Y - ry, rx * 2, ry * 2, (e.T * 900 + i * 40) % 360, 250);
                    }
                    break;
                }
                case 4: // столп света
                {
                    var p0 = cam.Project(e.F1, e.R1, 0);
                    var p1 = cam.Project(e.F1, e.R1, 5);
                    float w = sc * 0.7f;
                    int a = (int)(160 * MathF.Sin(MathF.PI * u));
                    var rect = new RectangleF(p0.X - w / 2, Math.Max(0, p1.Y), w, p0.Y - Math.Max(0, p1.Y));
                    if (rect.Height < 2) break;
                    using var b = new LinearGradientBrush(rect, Color.FromArgb(0, e.C), Color.FromArgb(a, e.C), LinearGradientMode.Vertical);
                    g.FillRectangle(b, rect);
                    break;
                }
            }
        }
    }

    // ---------------- надписи поверх доски ----------------
    void DrawOverlays(Graphics g)
    {
        if (cam.Original != null) { DrawPixelOverlays(g); return; }
        var area = BoardArea;
        var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        if (!string.IsNullOrEmpty(scene.Caption) && !cam.Flat)
        {
            var size = g.MeasureString(scene.Caption, captionFont);
            var r = new RectangleF(area.X + area.Width / 2 - size.Width / 2 - 18, 6, size.Width + 36, size.Height + 8);
            using var bg = new SolidBrush(Color.FromArgb(150, 10, 8, 6));
            FillRound(g, bg, r, 10);
            using var tb = new SolidBrush(Gold);
            g.DrawString(scene.Caption, captionFont, tb, r, center);
        }
        if (bannerT > 0 && banner != "")
        {
            float a = Math.Min(1, bannerT * 1.5f);
            float fs = Math.Clamp(area.Width / 16, 26, 60);
            using var f = new Font("Georgia", fs, FontStyle.Bold, GraphicsUnit.Pixel);
            var rect = new RectangleF(area.X, area.Height * 0.42f - fs, area.Width, fs * 2);
            using var path = new GraphicsPath();
            path.AddString(banner, f.FontFamily, (int)f.Style, fs, rect, center);
            using var pen = new Pen(Color.FromArgb((int)(220 * a), 20, 10, 0), fs * 0.12f) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, path);
            using var br = new LinearGradientBrush(rect, Color.FromArgb((int)(255 * a), 255, 236, 170), Color.FromArgb((int)(255 * a), 214, 150, 40), LinearGradientMode.Vertical);
            g.FillPath(br, path);
        }
        if (promoChoices != null) DrawPromotion(g);
    }

    void DrawPromotion(Graphics g)
    {
        var area = BoardArea;
        using var dim = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
        g.FillRectangle(dim, area);
        float box = Math.Min(110, area.Width / 7);
        float total = box * 4 + 30;
        float x0 = area.X + area.Width / 2 - total / 2, y0 = area.Height / 2 - box / 2;
        using var tb = new SolidBrush(Gold);
        var center = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString("Во что превратить пешку?", captionFont, tb, new RectangleF(area.X, y0 - 44, area.Width, 30), center);
        promoRects = new RectangleF[promoChoices.Count];
        for (int i = 0; i < promoChoices.Count; i++)
        {
            var r = new RectangleF(x0 + i * (box + 10), y0, box, box);
            promoRects[i] = r;
            bool hover = r.Contains(mouse);
            using var bg = new SolidBrush(hover ? Color.FromArgb(230, 90, 70, 40) : Color.FromArgb(220, 50, 40, 30));
            FillRound(g, bg, r, 12);
            using var pen = new Pen(Gold, hover ? 3 : 1.5f);
            DrawRound(g, pen, r, 12);
            int piece = promoChoices[i].Promo * pos.Side;
            BoardArt.Glyph(g, piece, new PointF(r.X + r.Width / 2, r.Y + r.Height / 2 - 8), box * 0.9f, 1);
            using var sb = new SolidBrush(PanelText);
            g.DrawString(P.RuNames[promoChoices[i].Promo], smallFont, sb, new RectangleF(r.X, r.Bottom - 22, r.Width, 20), center);
        }
    }

    int PromoPick(Point p)
    {
        for (int i = 0; i < promoRects.Length; i++) if (promoRects[i].Contains(p)) return i;
        return -1;
    }

    // ---------------- боковая панель ----------------
    void DrawPanel(Graphics g)
    {
        if (cam.Original != null) { DrawPixelPanel(g); return; }
        var pa = PanelArea;
        using (var bg = new LinearGradientBrush(pa, Color.FromArgb(38, 32, 26), Color.FromArgb(16, 13, 11), LinearGradientMode.Vertical))
            g.FillRectangle(bg, pa);
        using (var edge = new Pen(Color.FromArgb(150, Gold), 2)) g.DrawLine(edge, pa.X + 1, 0, pa.X + 1, pa.Height);

        float x = pa.X + 20, w = pa.Width - 40, y = 16;
        using var gold = new SolidBrush(Gold);
        using var text = new SolidBrush(PanelText);
        using var dim = new SolidBrush(Dim);
        g.DrawString("БОЕВЫЕ", titleFont, gold, x, y);
        g.DrawString("ШАХМАТЫ", titleFont, gold, x + 26, y + 26);
        y += 64;
        string mode = humanWhite && humanBlack ? "Два игрока"
            : !humanWhite && !humanBlack ? $"Компьютер против компьютера"
            : (humanWhite ? "Вы — белые" : "Вы — чёрные");
        g.DrawString(mode, smallFont, dim, x, y); y += 18;
        if (!(humanWhite && humanBlack)) { g.DrawString($"Соперник: {Ai.LevelNames[level]}", smallFont, dim, x, y); y += 18; }
        g.DrawString(cam.Original != null ? "Вид: оригинал Battle Chess" : "Вид: 2D", smallFont, dim, x, y);
        y += 28;

        // статус
        var sr = new RectangleF(x - 6, y, w + 12, 56);
        using (var sbg = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) FillRound(g, sbg, sr, 10);
        if (state != GameState.Playing && !scene.Busy)
        {
            g.DrawString(StateText(), boldFont, gold, new RectangleF(sr.X + 10, sr.Y + 6, sr.Width - 20, sr.Height - 8));
        }
        else
        {
            int side = scene.Busy ? -pos.Side : pos.Side;
            using (var dot = new SolidBrush(side > 0 ? Color.FromArgb(240, 234, 216) : Color.FromArgb(40, 36, 44)))
                g.FillEllipse(dot, sr.X + 12, sr.Y + 12, 20, 20);
            using (var dp = new Pen(Gold, 1.5f)) g.DrawEllipse(dp, sr.X + 12, sr.Y + 12, 20, 20);
            g.DrawString(side > 0 ? "Ход белых" : "Ход чёрных", boldFont, text, sr.X + 42, sr.Y + 8);
            string sub = thinking ? "Компьютер думает" + new string('.', 1 + (int)(scene.Time * 3) % 3)
                : scene.Busy ? "Идёт сражение..." : HumanToMove ? "Ваш ход" : "";
            if (!scene.Busy && pos.InCheck(pos.Side)) sub = "Шах!";
            g.DrawString(sub, smallFont, sub == "Шах!" ? Brushes.OrangeRed : dim, sr.X + 42, sr.Y + 30);
        }
        y += 68;

        // взятые фигуры
        var takenByWhite = history.Where(m => m.IsCapture && m.Captured < 0).Select(m => -m.Captured).OrderByDescending(t => t).ToList();
        var takenByBlack = history.Where(m => m.IsCapture && m.Captured > 0).Select(m => m.Captured).OrderByDescending(t => t).ToList();
        int[] val = { 0, 1, 3, 3, 5, 9, 0 };
        int diff = takenByWhite.Sum(t => val[t]) - takenByBlack.Sum(t => val[t]);
        DrawTaken(g, "Белые взяли", takenByWhite.Select(t => -t).ToList(), diff > 0 ? $"+{diff}" : "", x, ref y, w);
        DrawTaken(g, "Чёрные взяли", takenByBlack, diff < 0 ? $"+{-diff}" : "", x, ref y, w);
        y += 6;

        // список ходов
        g.DrawString("Ходы", boldFont, gold, x, y); y += 26;
        float listBottom = pa.Height - 150;
        var lr = new RectangleF(x - 6, y, w + 12, Math.Max(20, listBottom - y));
        using (var lbg = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) FillRound(g, lbg, lr, 8);
        int rowH = 21, rows = Math.Max(1, (int)((lr.Height - 8) / rowH));
        int total = (sanList.Count + 1) / 2;
        int first = Math.Max(0, total - rows);
        for (int i = first; i < total; i++)
        {
            float ry = lr.Y + 4 + (i - first) * rowH;
            g.DrawString($"{i + 1}.", moveFont, dim, x, ry);
            g.DrawString(sanList[2 * i], moveFont, text, x + 44, ry);
            if (2 * i + 1 < sanList.Count) g.DrawString(sanList[2 * i + 1], moveFont, text, x + 150, ry);
        }
        if (total == 0) g.DrawString("Партия ещё не началась", smallFont, dim, x, lr.Y + 6);

        // кнопки
        buttons.Clear();
        float by = pa.Height - 136, bw = (w - 10) / 2, bh = 36;
        AddButton(g, new RectangleF(x, by, bw, bh), "Новая игра", ShowNewGameMenu);
        AddButton(g, new RectangleF(x + bw + 10, by, bw, bh), "Отменить ход", Undo);
        AddButton(g, new RectangleF(x, by + bh + 8, bw, bh), "Подсказка", Hint);
        AddButton(g, new RectangleF(x + bw + 10, by + bh + 8, bw, bh), cam.Original != null ? "Вид 2D" : "Battle Chess", () => SetOriginal(cam.Original == null));
        g.DrawString("Пробел / ПКМ — пропустить бой", smallFont, dim, x, by + 2 * bh + 24);
    }

    void DrawTaken(Graphics g, string label, List<int> pieces, string adv, float x, ref float y, float w)
    {
        using var dim = new SolidBrush(Dim);
        using var gold = new SolidBrush(Gold);
        g.DrawString(label, smallFont, dim, x, y);
        if (adv != "") g.DrawString(adv, smallFont, gold, x + w - 30, y);
        y += 18;
        float gx = x + 10, size = 26;
        int perRow = (int)(w / (size * 0.62f));
        for (int i = 0; i < pieces.Count; i++)
        {
            int row = i / perRow, col = i % perRow;
            BoardArt.Glyph(g, pieces[i], new PointF(gx + col * size * 0.62f, y + 12 + row * size), size, 1);
        }
        y += Math.Max(1, (pieces.Count + perRow - 1) / perRow) * size + 4;
    }

    void AddButton(Graphics g, RectangleF r, string label, Action act)
    {
        buttons.Add((r, label, act));
        bool hover = r.Contains(mouse);
        using var bg = new LinearGradientBrush(r, hover ? Color.FromArgb(120, 90, 50) : Color.FromArgb(84, 64, 40),
            hover ? Color.FromArgb(80, 56, 30) : Color.FromArgb(52, 38, 24), LinearGradientMode.Vertical);
        FillRound(g, bg, r, 8);
        using var pen = new Pen(Color.FromArgb(hover ? 255 : 160, Gold), 1.4f);
        DrawRound(g, pen, r, 8);
        using var tb = new SolidBrush(hover ? Color.White : PanelText);
        g.DrawString(label, textFont, tb, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
    }

    bool HandlePanelClick(Point p)
    {
        if (!PanelArea.Contains(p)) return false;
        foreach (var (r, _, act) in buttons)
            if (r.Contains(p)) { snd.Play("select"); act(); return true; }
        return true;
    }

    void ShowNewGameMenu()
    {
        var cm = new ContextMenuStrip();
        cm.Items.Add("За белых против компьютера", null, (_, _) => NewGame(true, false));
        cm.Items.Add("За чёрных против компьютера", null, (_, _) => NewGame(false, true));
        cm.Items.Add("Два игрока", null, (_, _) => NewGame(true, true));
        cm.Items.Add("Компьютер против компьютера", null, (_, _) => NewGame(false, false));
        cm.Show(canvas, mouse);
    }

    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static void FillRound(Graphics g, Brush b, RectangleF r, float rad) { using var p = Round(r, rad); g.FillPath(b, p); }
    static void DrawRound(Graphics g, Pen pen, RectangleF r, float rad) { using var p = Round(r, rad); g.DrawPath(pen, p); }
}
