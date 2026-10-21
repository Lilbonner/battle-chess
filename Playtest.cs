using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace BattleChess;

// Автоматический плейтест режима оригинальной Battle Chess:
// BattleChess.exe --playtest <папка> [партий] [ходов]
// Компьютер играет сам с собой, после каждого хода проверяются инварианты, бои сохраняются раскадровками.
public partial class GameForm
{
    public void RunPlaytest(string dir, int games, int maxPlies)
    {
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "captures"));
        timer.Stop();
        snd.Enabled = false;
        ClientSize = new Size(1280, 800);
        CreateControl();
        _ = Handle;
        canvas.Size = new Size(1280, 800 - menu.Height);
        var log = new List<string>();
        var issues = new List<string>();
        var variantStats = new Dictionary<string, int>();
        var rnd = new Random(12345);
        int totalPlies = 0, totalCaptures = 0, pickChecks = 0;
        var swAll = Stopwatch.StartNew();

        for (int g = 0; g < games; g++)
        {
            level = rnd.Next(0, 3);
            NewGame(false, false);
            if (cam.Original == null) SetOriginal(true);
            titleT = 0; bannerT = 0;
            RebuildCache();   // подогнать камеру под окно, как при обычной отрисовке
            log.Add($"=== Партия {g + 1}, уровень {Ai.LevelNames[level]}");
            int ply = 0;
            while (ply < maxPlies && state == GameState.Playing)
            {
                var sw = Stopwatch.StartNew();
                while (history.Count == ply && sw.ElapsedMilliseconds < 20000 && state == GameState.Playing)
                {
                    Application.DoEvents();
                    scene.Update(1 / 30f);
                    Thread.Sleep(2);
                }
                if (history.Count == ply) { if (state == GameState.Playing) issues.Add($"Партия {g + 1}, ход {ply + 1}: компьютер не сделал ход за 20 с"); break; }
                // проигрываем анимации (за это время компьютер может сделать и следующий ход)
                var captions = new Dictionary<int, string>();
                var sheets = new Dictionary<int, List<Bitmap>>();
                Scene.CombatView lastCombat = null;
                float t = 0, next = 0, moveT = 0; int lastCount = history.Count;
                try
                {
                    while ((scene.Busy || history.Count > lastCount) && t < 120)
                    {
                        if (history.Count != lastCount) { lastCount = history.Count; moveT = 0; }
                        scene.Update(1 / 30f); t += 1 / 30f; moveT += 1 / 30f;
                        if (bannerT > 0) bannerT -= 1 / 30f;   // в тесте нет игрового таймера — убираем надписи сами
                        Application.DoEvents();
                        int idx = history.Count - 1;
                        if (scene.Combat != null && scene.Combat != lastCombat) { lastCombat = scene.Combat; captions[idx] = scene.Caption; }
                        if ((history[idx].IsCapture || idx < 8) && t >= next)
                        {
                            if (!sheets.ContainsKey(idx)) sheets[idx] = new List<Bitmap>();
                            sheets[idx].Add(GrabCrop2()); next = t + (history[idx].IsCapture ? 0.7f : 0.35f);
                        }
                        if (moveT > 60) { issues.Add($"Партия {g + 1}, ход {idx + 1} ({sanList[idx]}): анимация хода не закончилась за 60 с"); break; }
                    }
                }
                catch (Exception ex) { issues.Add($"Партия {g + 1}, ход {ply + 1}: исключение в анимации: {ex.Message}"); }
                for (; ply < history.Count; ply++)
                {
                    var m = history[ply]; string san = sanList[ply];
                    if (m.IsCapture)
                    {
                        totalCaptures++;
                        string key = $"{P.RuNames[Math.Abs(m.Moved)]} берёт {P.RuNames[Math.Abs(m.Captured)].ToLower()}";
                        string cap = captions.GetValueOrDefault(ply, "");
                        if (cap == "") issues.Add($"Партия {g + 1}, ход {ply + 1} ({san}): взятие без сцены боя оригинала ({key})");
                        string vkey = cap == "" ? key + ": без сцены" : cap;
                        variantStats[vkey] = variantStats.GetValueOrDefault(vkey) + 1;
                        if (sheets.TryGetValue(ply, out var fr) && fr.Count > 0)
                            SaveSheet2(Path.Combine(dir, "captures", $"g{g + 1}_p{ply + 1:000}_{Safe(san)}.png"), fr, cap == "" ? key : cap);
                        log.Add($"  {ply + 1}. {san} — {cap}");
                    }
                    else if (sheets.TryGetValue(ply, out var mfr) && mfr.Count > 0)
                    {
                        SaveSheet2(Path.Combine(dir, "captures", $"g{g + 1}_p{ply + 1:000}_{Safe(san)}_move.png"), mfr, "Ход " + san);
                        log.Add($"  {ply + 1}. {san}");
                    }
                    else log.Add($"  {ply + 1}. {san}");
                    totalPlies++;
                    if ((ply + 1) % 12 == 0) SaveFull(Path.Combine(dir, $"g{g + 1}_p{ply + 1:000}.png"));
                }
                Application.DoEvents();
                if (!scene.Busy)
                {
                    CheckSync(g, ply - 1, sanList[ply - 1], issues);
                    pickChecks += CheckPicking(g, ply - 1, sanList[ply - 1], issues);
                }
            }
            log.Add($"Итог партии: {(state == GameState.Playing ? "не закончена (лимит ходов)" : StateText())} после {ply} полуходов");
            SaveFull(Path.Combine(dir, $"g{g + 1}_final.png"));
        }

        var report = new List<string>
        {
            $"Плейтест оригинальной Battle Chess: {games} партий, {totalPlies} полуходов, {totalCaptures} взятий, {pickChecks} проверок клика, {swAll.Elapsed.TotalSeconds:0} с",
            "",
            $"ПРОБЛЕМЫ ({issues.Count}):",
        };
        report.AddRange(issues.Count == 0 ? new[] { "  нет" } : issues.Select(i => "  - " + i));
        report.Add("");
        report.Add("ВАРИАНТЫ БОЁВ:");
        report.AddRange(variantStats.OrderBy(k => k.Key).Select(k => $"  {k.Key}: {k.Value}"));
        report.Add("");
        report.Add("ХОД ПАРТИЙ:");
        report.AddRange(log);
        File.WriteAllLines(Path.Combine(dir, "report.txt"), report);
    }

    static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    void CheckSync(int g, int ply, string san, List<string> issues)
    {
        for (int s = 0; s < 64; s++)
        {
            var here = scene.Actors.Where(a => !a.Dead && (int)MathF.Floor(a.F) == P.File(s) && (int)MathF.Floor(a.R) == P.Rank(s)).ToList();
            if (pos.B[s] == 0 && here.Count > 0)
                issues.Add($"Партия {g + 1}, ход {ply + 1} ({san}): на пустой клетке {P.Name(s)} нарисована фигура");
            else if (pos.B[s] != 0 && (here.Count != 1 || here[0].Piece != pos.B[s]))
                issues.Add($"Партия {g + 1}, ход {ply + 1} ({san}): на {P.Name(s)} должна быть фигура {pos.B[s]}, нарисовано {here.Count}");
        }
    }

    // Для каждой фигуры стороны, которой ходить: клик по случайному непрозрачному пикселю её спрайта
    // должен выбирать именно её клетку (если пиксель не закрыт более близкой фигурой).
    int CheckPicking(int g, int ply, string san, List<string> issues)
    {
        if (scene.Busy || state != GameState.Playing) return 0;
        bool hw = humanWhite, hb = humanBlack;
        humanWhite = humanBlack = true;
        int checks = 0;
        var rnd = new Random(ply * 31 + g);
        try
        {
            var art = cam.Original;
            var order = scene.Actors.Where(a => !a.Dead).OrderBy(a => cam.VZ(a.R)).ToList();
            foreach (var a in order.Where(a => Math.Sign(a.Piece) == pos.Side))
            {
                var of = OriginalFrame(a);
                if (of == null) continue;
                var (bmp, fax, fay, feet) = of.Value;
                int pose = -1;
                float s = cam.OScale;
                for (int tries = 0; tries < 40; tries++)
                {
                    int x = rnd.Next(bmp.Width), y = rnd.Next(bmp.Height);
                    if (bmp.GetPixel(x, y).A == 0) continue;
                    var p = new Point((int)MathF.Round(feet.X - fax * s + (x + 0.5f) * s), (int)MathF.Round(feet.Y - fay * s + (y + 0.5f) * s));
                    // пропускаем пиксели, закрытые более близкими фигурами
                    if (order.TakeWhile(o => o != a).Any(o => OriginalSpriteHit(o, p))) continue;
                    selected = -1; selMoves.Clear();
                    int sq = PickSquare(p);
                    int want = P.Sq((int)a.F, (int)a.R);
                    checks++;
                    if (sq != want)
                    {
                        var topA = order.FirstOrDefault(o => OriginalSpriteHit(o, p));
                        issues.Add($"Партия {g + 1}, ход {ply + 1} ({san}): клик по спрайту фигуры на {P.Name(want)} выбрал {(sq >= 0 ? P.Name(sq) : "ничего")}"
                            + $" [dir {a.Dir} pose {pose} moving {a.Pose.Moving} selfHit {OriginalSpriteHit(a, p)} top {(topA == null ? "-" : P.Name(P.Sq((int)topA.F, (int)topA.R)))} F {a.F:0.00} R {a.R:0.00} busy {scene.Busy} zoom {scene.ZoomK:0.00} pt {p}]");
                    }
                    break;
                }
            }
        }
        finally { humanWhite = hw; humanBlack = hb; selected = -1; selMoves.Clear(); }
        return checks;
    }

    Bitmap GrabCrop2()
    {
        using var full = new Bitmap(canvas.Width, canvas.Height);
        canvas.DrawToBitmap(full, new Rectangle(0, 0, canvas.Width, canvas.Height));
        var small = new Bitmap(490, 380);
        using var g = Graphics.FromImage(small);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(full, new Rectangle(0, 0, small.Width, small.Height), new Rectangle(0, 0, (int)BoardArea.Width, canvas.Height), GraphicsUnit.Pixel);
        return small;
    }

    void SaveFull(string path)
    {
        using var full = new Bitmap(canvas.Width, canvas.Height);
        canvas.DrawToBitmap(full, new Rectangle(0, 0, canvas.Width, canvas.Height));
        full.Save(path, ImageFormat.Png);
    }

    static void SaveSheet2(string path, List<Bitmap> frames, string title)
    {
        int cols = 5, rows = (frames.Count + cols - 1) / cols;
        int w = frames[0].Width, h = frames[0].Height;
        using var sheet = new Bitmap(w * cols, h * rows + 28);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.Black);
            using var f = new Font("Segoe UI", 14, FontStyle.Bold);
            g.DrawString(title, f, Brushes.Gold, 6, 3);
            for (int i = 0; i < frames.Count; i++) { g.DrawImage(frames[i], (i % cols) * w, 28 + (i / cols) * h); frames[i].Dispose(); }
        }
        sheet.Save(path, ImageFormat.Png);
    }
}
