using System.Drawing;
using System.Drawing.Imaging;

namespace BattleChess;

// Скрытый режим для проверки графики: проигрывает партию и сохраняет кадры (BattleChess.exe --shots <папка>).
public partial class GameForm
{
    public void RenderShots(string dir, string mode)
    {
        Directory.CreateDirectory(dir);
        timer.Stop();
        snd.Enabled = false;
        ClientSize = new Size(1280, 800);
        CreateControl();
        canvas.Size = new Size(1280, 800 - menu.Height);
        autoRotate = false;   // кадры для проверки — без автоповорота доски
        NewGame(true, true);
        if (mode == "orig") { SetOriginal(true); titleT = 0; }
        else SetOriginal(false);
        bannerT = 0;
        string[] moves = { "e2e4", "d7d5", "e4d5", "d8d5", "b1c3", "d5a2", "a1a2", "c8g4", "f2f3", "g4f3", "g1f3", "b8c6", "f3e5", "g8f6", "e5f7", "e8f7" };
        Save(dir, "00_start");
        int idx = 1;
        foreach (var uci in moves)
        {
            var m = pos.Legal().First(x => x.ToString() == uci);
            bool cap = m.IsCapture;
            DoMove(m);
            var frames = new List<Bitmap>();
            float t = 0, next = 0.3f;
            while (scene.Busy && t < 30)
            {
                scene.Update(1 / 60f);
                t += 1 / 60f;
                if (cap && t >= next)
                {
                    frames.Add(Grab(0.5f));
                    if (frames.Count % 3 == 0) Save(dir, $"{idx:00}_{uci}_f{frames.Count:00}");
                    next += 0.35f;
                }
            }
            if (cap && frames.Count > 0) SaveSheet(dir, $"{idx:00}_{uci}_sheet", frames);
            Save(dir, $"{idx:00}_{uci}");
            idx++;
        }
        selected = pos.Legal().First(x => x.ToString() == "d1e2").From;
        selMoves = pos.Legal().Where(x => x.From == selected).ToList();
        Save(dir, "99_selected");
        ClickTest(dir);
        // диалог превращения пешки
        var pp = new Position();
        pp.B[4] = P.King; pp.B[60] = -P.King; pp.B[P.Sq(0, 6)] = P.Pawn;
        pp.Side = 1; pp.Hash = pp.ComputeHash(); pp.HashHistory.Add(pp.Hash);
        pos = pp; scene.Skip(); scene.Sync(pos, false);
        promoChoices = pos.Legal().Where(x => x.Promo != 0).OrderByDescending(x => x.Promo).ToList();
        mouse = new Point(-100, -100);
        Save(dir, "98_promo");
        promoChoices = null;
    }

    // Проигрывает все 5 приёмов каждой фигуры и сохраняет раскадровки (по листу на фигуру).
    public void RenderTechniques(string dir, bool orig = false)
    {
        Directory.CreateDirectory(dir);
        timer.Stop(); snd.Enabled = false;
        ClientSize = new Size(1280, 800); CreateControl();
        canvas.Size = new Size(1280, 800 - menu.Height);
        SetOriginal(false);
        if (orig) { SetOriginal(true); titleT = 0; }
        NewGame(true, true);
        bannerT = 0;
        int[] from = { 0, 27, 27, 27, 27, 27, 27 };   // d4
        int[] to = { 0, 36, 44, 45, 43, 45, 36 };     // e5, e6, f6, d6, f6, e5
        for (int type = 1; type <= 6; type++)
        {
            var rows = new List<List<Bitmap>>();
            for (int v = 0; v < 5; v++)
            {
                var p = new Position();
                p.B[7] = 6; p.B[63] = -6;
                if (type == P.King) p.B[7] = 0;
                p.B[from[type]] = type;
                p.B[to[type]] = -P.Pawn;
                p.Side = 1; p.Hash = p.ComputeHash(); p.HashHistory.Add(p.Hash);
                pos = p; history.Clear(); sanList.Clear(); state = GameState.Playing; lastMove = null;
                scene.Skip(); scene.Particles.Clear(); scene.Sync(pos, false);
                var m = pos.Legal().First(x => x.From == from[type] && x.To == to[type]);
                scene.ForcedTechnique = v;
                DoMove(m);
                var frames = new List<Bitmap>();
                float t = 0, next = 1.0f;
                while (scene.Busy && t < 30)
                {
                    scene.Update(1 / 60f); t += 1 / 60f;
                    if (t >= next) { frames.Add(GrabCrop()); next += orig ? 0.9f : 0.28f; }
                }
                rows.Add(frames);
            }
            int cols = rows.Max(r => r.Count), w = rows[0][0].Width, h = rows[0][0].Height;
            using var sheet = new Bitmap(w * cols, h * rows.Count);
            using (var g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.Black);
                for (int r = 0; r < rows.Count; r++)
                    for (int c = 0; c < rows[r].Count; c++) { g.DrawImage(rows[r][c], c * w, r * h); rows[r][c].Dispose(); }
            }
            sheet.Save(Path.Combine(dir, $"tech_{type}_{P.RuNames[type]}.png"), ImageFormat.Png);
        }
        scene.ForcedTechnique = -1;
    }

    Bitmap GrabCrop()
    {
        using var full = new Bitmap(canvas.Width, canvas.Height);
        canvas.DrawToBitmap(full, new Rectangle(0, 0, canvas.Width, canvas.Height));
        var src = new Rectangle(140, 40, 700, 600);
        var small = new Bitmap(233, 200);
        using var g = Graphics.FromImage(small);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(full, new Rectangle(0, 0, small.Width, small.Height), src, GraphicsUnit.Pixel);
        return small;
    }

    void ClickSq(int sq)
    {
        var p = cam.Project(P.File(sq) + 0.5f, P.Rank(sq) + 0.3f);
        var pt = Point.Round(p);
        OnCanvasMouseMove(pt);
        OnCanvasMouseDown(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, pt.X, pt.Y, 0));
    }

    void ClickTest(string dir)
    {
        var log = new List<string> { $"handle created before: {IsHandleCreated}" };
        _ = Handle;
        log.Add($"handle created after: {IsHandleCreated}");
        level = 1;
        NewGame(true, false);
        bannerT = 0;
        string[] plan = { "e2", "e4", "g1", "f3", "f1", "c4", "e1", "g1" };
        for (int i = 0; i < plan.Length; i += 2)
        {
            int from = (plan[i][0] - 'a') + (plan[i][1] - '1') * 8, to = (plan[i + 1][0] - 'a') + (plan[i + 1][1] - '1') * 8;
            int before = history.Count;
            ClickSq(from);
            log.Add($"click {plan[i]}: selected={(selected >= 0 ? P.Name(selected) : "-")} targets={selMoves.Count}");
            if (i == 6) Save(dir, "98_castle_select");
            ClickSq(to);
            log.Add($"click {plan[i + 1]}: history {before} -> {history.Count} last={(history.Count > 0 ? sanList[^1] : "")}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 20000 && (scene.Busy || thinking || pendingMove != null || !HumanToMove))
            {
                System.Windows.Forms.Application.DoEvents();
                scene.Update(1 / 30f);
                Thread.Sleep(1);
            }
            log.Add($"  after AI: plies={history.Count} moves={string.Join(" ", sanList)} state={state} thinking={thinking} busy={scene.Busy}");
        }
        Save(dir, "97_after_clicks");
        log.Add("original sounds: " + string.Join(", ", new[] { "clang", "stab", "hit", "slash", "thunder", "smash", "zap", "die" }.Select(n => $"{n}={snd.Has("o:" + n)}")));
        File.WriteAllLines(Path.Combine(dir, "selftest.txt"), log);
    }

    Bitmap Grab(float scale)
    {
        using var full = new Bitmap(canvas.Width, canvas.Height);
        canvas.DrawToBitmap(full, new Rectangle(0, 0, canvas.Width, canvas.Height));
        var small = new Bitmap((int)(full.Width * scale), (int)(full.Height * scale));
        using var g = Graphics.FromImage(small);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(full, 0, 0, small.Width, small.Height);
        return small;
    }

    void Save(string dir, string name)
    {
        using var b = Grab(1);
        b.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
    }

    static void SaveSheet(string dir, string name, List<Bitmap> frames)
    {
        int cols = 4, rows = (frames.Count + cols - 1) / cols;
        int w = frames[0].Width, h = frames[0].Height;
        using var sheet = new Bitmap(w * cols, h * rows);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.Black);
        for (int i = 0; i < frames.Count; i++) { g.DrawImage(frames[i], (i % cols) * w, (i / cols) * h); frames[i].Dispose(); }
        sheet.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
    }

    // Раскадровка поворота доски для двух игроков (BattleChess.exe --spin <папка> [orig]).
    public void RenderSpin(string dir, bool orig)
    {
        Directory.CreateDirectory(dir);
        timer.Stop(); snd.Enabled = false;
        ClientSize = new Size(1280, 800); CreateControl();
        canvas.Size = new Size(1280, 800 - menu.Height);
        SetOriginal(orig); titleT = 0;
        NewGame(true, true);
        bannerT = 0;
        int idx = 0;
        foreach (var uci in new[] { "e2e4", "d7d5", "e4d5", "d8d5", "b1c3", "d5a2", "a1a2", "g8f6" })
        {
            var m = pos.Legal().First(x => x.ToString() == uci);
            DoMove(m);
            var moveFrames = new List<Bitmap>();
            float t = 0;
            while (scene.Busy)   // по окончании анимации форма сама запускает автоповорот
            {
                scene.Update(1 / 60f); t += 1 / 60f;
                if (t >= 0.4f) { moveFrames.Add(Grab(0.5f)); t = 0; }
            }
            if (moveFrames.Count > 0) SaveSheet(dir, $"{++idx:00}_move_{uci}", moveFrames);
            bannerT = 0;
            var frames = new List<Bitmap>();
            int n = 0;
            while (Spinning)
            {
                AdvanceSpin(1 / 60f);
                if (n % 3 == 0) Save(dir, $"frame_{idx:00}_{n:000}");
                if (n++ % 9 == 0) frames.Add(Grab(0.5f));
            }
            frames.Add(Grab(0.5f));
            SaveSheet(dir, $"{idx:00}_spin_{uci}", frames);
        }
    }
}

static class GlyphTest
{
    // BattleChess.exe --glyphs <файл.png>: все 12 символов фигур 2D-режима на светлой и тёмной клетке.
    public static void Render(string file)
    {
        using var bmp = new Bitmap(6 * 90, 2 * 90);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        for (int row = 0; row < 2; row++)
            for (int t = 1; t <= 6; t++)
            {
                g.FillRectangle(new SolidBrush((t + row) % 2 == 0 ? Color.FromArgb(222, 214, 192) : Color.FromArgb(86, 94, 110)), (t - 1) * 90, row * 90, 90, 90);
                BoardArt.Glyph(g, row == 0 ? t : -t, new PointF((t - 1) * 90 + 45, row * 90 + 45), 81, 1);
            }
        bmp.Save(file);
    }
}
