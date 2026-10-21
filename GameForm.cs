using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace BattleChess;

public partial class GameForm : Form
{
    readonly Canvas canvas = new();
    readonly MenuStrip menu = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    double lastTick;

    readonly Scene scene = new();
    readonly Camera cam = new();
    readonly Painter painter = new();
    readonly SoundSystem snd = new();

    Position pos = Position.Start();
    readonly List<Move> history = new();
    readonly List<string> sanList = new();
    bool humanWhite = true, humanBlack = false;
    int level = 2;
    GameState state = GameState.Playing;

    int selected = -1;
    List<Move> selMoves = new();
    int hoverSq = -1;
    Move? lastMove;
    List<Move> promoChoices;
    Move? hint;
    float hintT;

    Ai currentAi;
    int aiGen;
    bool thinking;
    Move? pendingMove;

    string banner = "";
    float bannerT;
    bool gameOverShown;

    ToolStripMenuItem[] levelItems;
    ToolStripMenuItem view2d, viewOrig, battlesItem, soundItem, autoRotateItem, cameraMenu;
    bool autoRotate = true;            // два игрока: доска поворачивается к тому, чей ход
    float spinT = -1;                  // время анимации поворота доски; < 0 — доска не вращается
    bool spinQueued;                   // переворот запрошен во время анимации хода
    OriginalArt originalArt;
    OriginalMoveData originalMoves;
    float titleT;
    ToolStripMenuItem[] speedItems;
    ToolStripMenuItem[] cameraItems;

    const int PanelW = 300;

    public GameForm()
    {
        Text = "Боевые шахматы";
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        BuildMenu();
        canvas.Dock = DockStyle.Fill;
        Controls.Add(canvas);
        Controls.Add(menu);
        MainMenuStrip = menu;

        canvas.Paint += (_, e) => Render(e.Graphics);
        canvas.MouseMove += (_, e) => OnCanvasMouseMove(e.Location);
        canvas.MouseDown += (_, e) => OnCanvasMouseDown(e);
        canvas.MouseLeave += (_, _) => hoverSq = -1;
        canvas.Resize += (_, _) => InvalidateBoardCache();
        KeyDown += OnKey;

        scene.Sound = PlaySound;
        scene.LoadCombat = (victim, killer) => cam.Original?.Variants(victim, killer);
        scene.CombatSound = (x, pitch) =>
        {
            if (originalArt == null || !originalArt.SfxWord.TryGetValue(x, out int w0)) return;
            int r = Math.Max(20, w0 - 8 * pitch);
            snd.PlayRate("sfx" + x, (float)w0 / r);
        };
        scene.Finished += OnAnimationFinished;
        timer.Tick += (_, _) => Tick();
        timer.Start();
        FormClosed += (_, _) => { currentAi?.Cancel(); snd.Dispose(); };

        cam.Flat = scene.Flat = true;
        NewGame(true, false);
        if (OriginalArt.FindFolder() != null) SetOriginal(true);
    }

    // ---------------- меню ----------------
    void BuildMenu()
    {
        menu.RenderMode = ToolStripRenderMode.Professional;
        var game = new ToolStripMenuItem("&Игра");
        game.DropDownItems.Add(Item("Новая игра — за &белых", () => NewGame(true, false), Keys.Control | Keys.N));
        game.DropDownItems.Add(Item("Новая игра — за &чёрных", () => NewGame(false, true)));
        game.DropDownItems.Add(Item("&Два игрока на одном компьютере", () => NewGame(true, true)));
        game.DropDownItems.Add(Item("&Компьютер против компьютера", () => NewGame(false, false)));
        game.DropDownItems.Add(new ToolStripSeparator());
        game.DropDownItems.Add(Item("&Отменить ход", Undo, Keys.Control | Keys.Z));
        game.DropDownItems.Add(Item("&Подсказка", Hint, Keys.Control | Keys.H));
        game.DropDownItems.Add(new ToolStripSeparator());
        game.DropDownItems.Add(Item("В&ыход", Close, Keys.Alt | Keys.F4));

        var lvl = new ToolStripMenuItem("&Сложность");
        levelItems = new ToolStripMenuItem[Ai.LevelNames.Length];
        for (int i = 0; i < Ai.LevelNames.Length; i++)
        {
            int k = i;
            levelItems[i] = Item($"{i + 1}. {Ai.LevelNames[i]}", () => SetLevel(k));
            lvl.DropDownItems.Add(levelItems[i]);
        }

        var view = new ToolStripMenuItem("&Вид");
        viewOrig = Item("&Battle Chess — оригинальная графика", () => SetOriginal(true));
        view2d = Item("&2D — классическая доска", () => SetOriginal(false));
        view.DropDownItems.Add(viewOrig);
        view.DropDownItems.Add(view2d);
        view.DropDownItems.Add(Item("Переключить Battle Chess / 2D", () => SetOriginal(cam.Original == null), Keys.F2));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Item("&Перевернуть доску", FlipBoard, Keys.F3));
        autoRotateItem = Item("&Автоповорот доски для двух игроков", () => { autoRotate = !autoRotate; UpdateChecks(); AutoRotate(); });
        view.DropDownItems.Add(autoRotateItem);
        var camMenu = cameraMenu = new ToolStripMenuItem("&Камера");
        cameraItems = new ToolStripMenuItem[Camera.HeightNames.Length];
        for (int i = 0; i < Camera.HeightNames.Length; i++)
        {
            int k = i;
            cameraItems[i] = Item(Camera.HeightNames[i], () => { cam.Height = k; InvalidateBoardCache(); UpdateChecks(); });
            camMenu.DropDownItems.Add(cameraItems[i]);
        }
        camMenu.DropDownItems.Add(new ToolStripSeparator());
        camMenu.DropDownItems.Add(Item("Поднять камеру", () => { cam.Height = Math.Min(2, cam.Height + 1); InvalidateBoardCache(); UpdateChecks(); }, Keys.Control | Keys.PageUp));
        camMenu.DropDownItems.Add(Item("Опустить камеру", () => { cam.Height = Math.Max(0, cam.Height - 1); InvalidateBoardCache(); UpdateChecks(); }, Keys.Control | Keys.PageDown));
        view.DropDownItems.Add(camMenu);
        view.DropDownItems.Add(new ToolStripSeparator());
        battlesItem = Item("&Анимация сражений", () => { scene.Battles = !scene.Battles; UpdateChecks(); });
        view.DropDownItems.Add(battlesItem);
        var speed = new ToolStripMenuItem("&Скорость анимации");
        string[] sp = { "Медленная", "Обычная", "Быстрая", "Очень быстрая" };
        float[] spv = { 0.7f, 1f, 1.6f, 2.6f };
        speedItems = new ToolStripMenuItem[sp.Length];
        for (int i = 0; i < sp.Length; i++)
        {
            float v = spv[i];
            speedItems[i] = Item(sp[i], () => { scene.Speed = v; UpdateChecks(); });
            speedItems[i].Tag = v;
            speed.DropDownItems.Add(speedItems[i]);
        }
        view.DropDownItems.Add(speed);

        var sound = new ToolStripMenuItem("З&вук");
        soundItem = Item("Звук &включён", () => { snd.Enabled = !snd.Enabled; UpdateChecks(); }, Keys.Control | Keys.M);
        sound.DropDownItems.Add(soundItem);

        var help = new ToolStripMenuItem("&Справка");
        help.DropDownItems.Add(Item("&Как играть", ShowHelp, Keys.F1));
        help.DropDownItems.Add(Item("&О программе", () => MessageBox.Show(this,
            "Боевые шахматы 1.0\n\nШахматы, в которых фигуры сражаются при каждом взятии.\nВся графика и звук генерируются программой.\n\nВдохновлено классической игрой Battle Chess (Interplay, 1988).",
            "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information)));

        menu.Items.AddRange(new ToolStripItem[] { game, lvl, view, sound, help });
        UpdateChecks();
    }

    static ToolStripMenuItem Item(string text, Action a, Keys keys = Keys.None)
    {
        var it = new ToolStripMenuItem(text);
        it.Click += (_, _) => a();
        if (keys != Keys.None) it.ShortcutKeys = keys;
        if (keys == Keys.F2) it.ShortcutKeyDisplayString = "F2";
        return it;
    }

    void UpdateChecks()
    {
        if (levelItems == null || view2d == null) return;
        for (int i = 0; i < levelItems.Length; i++) levelItems[i].Checked = i == level;
        view2d.Checked = cam.Original == null;
        autoRotateItem.Checked = autoRotate;
        cameraMenu.Enabled = cam.Original != null;
        viewOrig.Checked = cam.Original != null;
        viewOrig.Enabled = originalArt != null || OriginalArt.FindFolder() != null;
        battlesItem.Checked = scene.Battles;
        soundItem.Checked = snd.Enabled;
        foreach (var s in speedItems) s.Checked = Math.Abs((float)s.Tag - scene.Speed) < 0.01f;
        for (int i = 0; i < cameraItems.Length; i++) cameraItems[i].Checked = cam.Height == i;
    }

    void ShowHelp() => MessageBox.Show(this,
        "Цель — поставить мат королю соперника. Действуют все шахматные правила: рокировка, взятие на проходе, превращение пешки, пат, ничья по правилу 50 ходов и троекратному повторению.\n\n" +
        "Управление:\n" +
        "• Щёлкните по своей фигуре, затем по клетке назначения.\n" +
        "• Пробел или правая кнопка мыши — пропустить анимацию.\n" +
        "• F2 — переключить Battle Chess / 2D, F3 — перевернуть доску.\n" +
        "• Для двух игроков доска сама поворачивается к тому, чей ход (Вид → Автоповорот).\n" +
        "• Ctrl+Z — отменить ход, Ctrl+H — подсказка, Ctrl+N — новая игра.\n\n" +
        "При взятии фигуры сходятся в поединке: у каждой свой приём.\n" +
        "Пешка — удар копьём, конь — таранный удар копьём, слон — молния с небес,\n" +
        "ладья — сокрушающий удар голема, ферзь — магический шар, король — удар мечом.",
        "Как играть", MessageBoxButtons.OK, MessageBoxIcon.Information);

    // ---------------- управление партией ----------------
    void NewGame(bool white, bool black)
    {
        CancelAi();
        scene.Skip();
        humanWhite = white; humanBlack = black;
        pos = Position.Start();
        history.Clear(); sanList.Clear();
        state = GameState.Playing;
        selected = -1; selMoves.Clear(); promoChoices = null; lastMove = null; hint = null;
        gameOverShown = false;
        spinT = -1; cam.Spin = 0; spinQueued = false;
        cam.Flip = black && !white;
        scene.Flip = cam.Flip;
        scene.Particles.Clear();
        scene.Sync(pos, false);
        InvalidateBoardCache();
        ShowBanner(white && black ? "Два игрока" : !white && !black ? "Компьютер против компьютера" : white ? "Вы играете белыми" : "Вы играете чёрными");
        MaybeStartAi();
    }

    void SetLevel(int k) { level = k; UpdateChecks(); }

    // Включение графики и звуков оригинальной Battle Chess из папки "original".
    void SetOriginal(bool on)
    {
        if (on && originalArt == null)
        {
            var dir = OriginalArt.FindFolder();
            if (dir == null)
            {
                MessageBox.Show(this, "Папка \"original\" с ресурсами Battle Chess не найдена рядом с программой.", "Оригинальная графика",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                originalArt = OriginalArt.Load(dir);
                foreach (var (role, path) in originalArt.Sounds) snd.LoadWav("o:" + role, path);
                foreach (var (x, path) in originalArt.SfxFiles) snd.LoadWav("sfx" + x, path);
                originalMoves = OriginalMoveData.Load(dir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось загрузить оригинальные ресурсы: " + ex.Message, "Оригинальная графика",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                originalArt = null;
                return;
            }
        }
        cam.Original = on ? originalArt : null;
        scene.Original = on;
        scene.Moves = on ? originalMoves : null;
        cam.Flat = scene.Flat = !on;
        if (on) titleT = 2.5f;
        scene.Sync(pos, false);
        InvalidateBoardCache(); UpdateChecks();
    }

    void PlaySound(string name)
    {
        if (cam.Original != null && snd.Has("o:" + name)) snd.Play("o:" + name);
        else snd.Play(name);
    }

    void FlipBoard()
    {
        if (scene.Busy) spinQueued = true;
        else StartSpin();
    }

    // ---------------- поворот доски ----------------
    bool Spinning => spinT >= 0;
    float SpinDur => (cam.Original != null ? 1.5f : 0.9f) / scene.Speed;

    void StartSpin()
    {
        if (Spinning) return;
        spinT = 0;
        selected = -1; selMoves.Clear(); promoChoices = null;
        snd.Play(cam.Original != null ? "step" : "move", 0.5f);
    }

    void AdvanceSpin(float dt)
    {
        if (!Spinning) return;
        spinT += dt;
        float u = Math.Min(1, spinT / SpinDur);
        cam.Spin = MathF.PI * Painter.Ease(u);
        if (cam.Original == null) InvalidateBoardCache();
        if (u >= 1) FinishSpin();
    }

    // Поворот на 180° закончен: доска перевёрнута, фигуры сохраняют направление взгляда относительно доски.
    void FinishSpin()
    {
        if (!Spinning) return;
        spinT = -1; cam.Spin = 0;
        cam.Flip = !cam.Flip; scene.Flip = cam.Flip;
        foreach (var a in scene.Actors) if (a.OFacing >= 0) a.OFacing = (a.OFacing + 4) % 8;
        InvalidateBoardCache();
        if (pendingMove != null && !scene.Busy) { var m = pendingMove.Value; pendingMove = null; DoMove(m); }
    }

    // Два игрока: повернуть доску к тому, чей ход.
    void AutoRotate()
    {
        if (!autoRotate || !(humanWhite && humanBlack) || state != GameState.Playing || scene.Busy) return;
        if (cam.Flip != (pos.Side < 0)) StartSpin();
    }

    bool HumanToMove => pos.Side > 0 ? humanWhite : humanBlack;

    void CancelAi()
    {
        aiGen++;
        currentAi?.Cancel();
        thinking = false;
        pendingMove = null;
    }

    void MaybeStartAi()
    {
        if (state != GameState.Playing || HumanToMove || thinking) return;
        thinking = true;
        int gen = ++aiGen;
        var snapshot = pos.Clone();
        var ai = new Ai();
        ai.SetLevel(level);
        currentAi = ai;
        Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var m = ai.Think(snapshot);
            int minMs = (!humanWhite && !humanBlack) ? 500 : 250;
            if (sw.ElapsedMilliseconds < minMs) Thread.Sleep(minMs - (int)sw.ElapsedMilliseconds);
            return m;
        }).ContinueWith(t =>
        {
            if (IsDisposed) return;
            try { BeginInvoke(() => OnAiMove(gen, t.IsCompletedSuccessfully ? t.Result : null)); } catch { }
        });
    }

    void OnAiMove(int gen, Move? m)
    {
        if (gen != aiGen) return;
        thinking = false;
        if (m == null) return;
        if (scene.Busy || Spinning) pendingMove = m;
        else DoMove(m.Value);
    }

    void DoMove(Move m)
    {
        string san = pos.San(m);
        pos.Make(m);
        history.Add(m);
        sanList.Add(san);
        lastMove = m;
        selected = -1; selMoves.Clear(); hint = null;
        state = pos.State();
        if (!m.IsCapture || !scene.Battles || cam.Flat) snd.Play(cam.Flat ? "move" : "step", 0.6f);
        scene.Play(scene.MoveScript(m));
        MaybeStartAi();
    }

    void OnAnimationFinished()
    {
        scene.Sync(pos);
        AnnounceState();
        if (spinQueued) { spinQueued = false; StartSpin(); }
        else AutoRotate();
        if (pendingMove != null)
        {
            var m = pendingMove.Value;
            pendingMove = null;
            DoMove(m);
        }
    }

    void AnnounceState()
    {
        if (state == GameState.Playing)
        {
            if (pos.InCheck(pos.Side)) { ShowBanner("Шах!"); snd.Play("check"); }
            return;
        }
        if (gameOverShown) return;
        gameOverShown = true;
        bool humanWon = (state == GameState.WhiteMates && humanWhite && !humanBlack) || (state == GameState.BlackMates && humanBlack && !humanWhite);
        bool humanLost = (state == GameState.WhiteMates && humanBlack && !humanWhite) || (state == GameState.BlackMates && humanWhite && !humanBlack);
        snd.Play(humanLost ? "lose" : "win");
        ShowBanner(StateText(), 6);
        _ = humanWon;
    }

    string StateText() => state switch
    {
        GameState.WhiteMates => "Мат! Победили белые",
        GameState.BlackMates => "Мат! Победили чёрные",
        GameState.Stalemate => "Пат — ничья",
        GameState.FiftyMoves => "Ничья по правилу 50 ходов",
        GameState.Repetition => "Ничья: троекратное повторение",
        GameState.Insufficient => "Ничья: недостаточно материала",
        _ => "",
    };

    void ShowBanner(string text, float sec = 2.2f) { banner = text; bannerT = sec; }

    void Undo()
    {
        if (history.Count == 0) return;
        if (!humanWhite && !humanBlack) return;
        CancelAi();
        scene.Skip();
        FinishSpin();
        do
        {
            pos.Unmake();
            history.RemoveAt(history.Count - 1);
            sanList.RemoveAt(sanList.Count - 1);
        } while (history.Count > 0 && !HumanToMove);
        state = pos.State();
        gameOverShown = false;
        lastMove = history.Count > 0 ? history[^1] : null;
        selected = -1; selMoves.Clear(); promoChoices = null; hint = null;
        scene.Particles.Clear();
        scene.Sync(pos, false);
        AutoRotate();
        MaybeStartAi();
    }

    void Hint()
    {
        if (state != GameState.Playing || !HumanToMove || scene.Busy || Spinning) return;
        var ai = new Ai();
        ai.SetLevel(3);
        var snapshot = pos.Clone();
        int gen = aiGen, ply = history.Count;
        Task.Run(() => ai.Think(snapshot)).ContinueWith(t =>
        {
            if (IsDisposed || !t.IsCompletedSuccessfully) return;
            try { BeginInvoke(() => { if (gen == aiGen && ply == history.Count) { hint = t.Result; hintT = 4; } }); } catch { }
        });
        ShowBanner("Ищу хороший ход...", 1);
    }

    // ---------------- ввод ----------------
    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Escape)
        {
            if (promoChoices != null && e.KeyCode == Keys.Escape) { promoChoices = null; e.Handled = true; return; }
            SkipAnimation();
            e.Handled = true;
        }
    }

    void SkipAnimation()
    {
        if (!scene.Busy) return;
        scene.Skip();
        OnAnimationFinished();
    }

    void OnCanvasMouseMove(Point p)
    {
        mouse = p;
        hoverSq = PickSquare(p);
        bool hand = buttons.Any(b => b.r.Contains(p)) || (promoChoices != null && PromoPick(p) >= 0)
            || (hoverSq >= 0 && !scene.Busy && HumanToMove && state == GameState.Playing
                && ((pos.B[hoverSq] != 0 && Math.Sign(pos.B[hoverSq]) == pos.Side) || selMoves.Any(m => m.To == hoverSq)));
        canvas.Cursor = hand ? Cursors.Hand : Cursors.Default;
    }

    void OnCanvasMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { SkipAnimation(); selected = -1; selMoves.Clear(); promoChoices = null; return; }
        if (e.Button != MouseButtons.Left) return;
        if (HandlePanelClick(e.Location)) return;

        if (promoChoices != null)
        {
            int k = PromoPick(e.Location);
            if (k >= 0) { var m = promoChoices[k]; promoChoices = null; DoMove(m); }
            else promoChoices = null;
            return;
        }
        if (scene.Busy || Spinning || state != GameState.Playing || !HumanToMove) return;

        int sq = PickSquare(e.Location);
        if (sq < 0) { selected = -1; selMoves.Clear(); return; }
        if (selected >= 0)
        {
            var targets = selMoves.Where(m => m.To == sq).ToList();
            if (targets.Count == 1) { DoMove(targets[0]); return; }
            if (targets.Count > 1) { promoChoices = targets.OrderByDescending(m => m.Promo).ToList(); return; }
        }
        if (pos.B[sq] != 0 && Math.Sign(pos.B[sq]) == pos.Side)
        {
            selected = sq;
            selMoves = pos.Legal().Where(m => m.From == sq).ToList();
            snd.Play("select");
        }
        else
        {
            if (selected >= 0) snd.Play("error");
            selected = -1; selMoves.Clear();
        }
    }

    int PickSquare(Point p)
    {
        if (!BoardArea.Contains(p) || Spinning) return -1;
        int ground = cam.Pick(p, out int gf, out int gr) ? P.Sq(gf, gr) : -1;
        bool Useful(int sq) => sq >= 0 && HumanToMove &&
            ((pos.B[sq] != 0 && Math.Sign(pos.B[sq]) == pos.Side) || selMoves.Any(m => m.To == sq));

        if (cam.Original != null && !scene.Busy)
        {
            // оригинальные спрайты: решает самая передняя фигура под курсором (по пикселям)
            var top = scene.Actors.Where(a => !a.Dead).OrderBy(a => cam.VZ(a.R)).FirstOrDefault(a => OriginalSpriteHit(a, p));
            if (top != null)
            {
                int sq = P.Sq((int)top.F, (int)top.R);
                if (Useful(sq)) return sq;
            }
            return ground;
        }

        return ground;
    }


    // ---------------- такт ----------------
    void Tick()
    {
        double now = clock.Elapsed.TotalSeconds;
        float dt = (float)(now - lastTick);
        lastTick = now;
        scene.Update(dt);
        AdvanceSpin(dt);
        if (bannerT > 0) bannerT -= dt;
        if (titleT > 0) titleT -= dt;
        if (hintT > 0) { hintT -= dt; if (hintT <= 0) hint = null; }
        canvas.Invalidate();
    }
}

public class Canvas : Control
{
    public Canvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
}
