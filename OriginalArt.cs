using System.Drawing;
using System.Text.Json;

namespace BattleChess;

// Графика и звуки оригинальной Battle Chess (Mac, 1991), извлечённые с диска пользователя
// в папку "original" рядом с программой. Только для личного использования; в exe не встраиваются.
public class OriginalArt
{
    public const int ScreenW = 512, ScreenH = 340;

    public class Frame { public Bitmap White, Black; public int Ax, Ay; }

    public Bitmap Board3D, Title;
    public Bitmap LogoBattle, LogoChess;                       // логотип заставки для пиксельной панели (ui/)
    public readonly Dictionary<int, Bitmap> PromoPict = new(); // картинки диалога превращения пешки (PICT 400..403)
    public readonly Dictionary<int, Frame> Poses = new();
    public readonly Dictionary<int, List<Frame>> Walks = new();
    public readonly Dictionary<string, string> Sounds = new();
    float cx;
    readonly List<(float y, float pitch)> rows = new();
    int[] sqX, rowY;                                   // точные координаты клеток из игры (строка 0 — дальняя)
    public readonly Dictionary<int, int> SfxWord = new();  // частотное слово каждого звука боя
    public readonly Dictionary<int, string> SfxFiles = new();
    string folder;

    // Поединок из оригинала: кадры побеждённой и победившей фигуры на каждый такт (10 тактов в секунду).
    public class CombatScene
    {
        public int Ticks, Impact, KillerPre, VictimPost;
        public int[] Victim, Killer;
        public List<(int tick, int sfx, int pitch, int side)> Sounds = new();   // side: 0 — побеждённый, 1 — победитель
        public List<(int ax, int ay)> Anchors = new();
        public string Dir;
        public readonly Dictionary<(int, bool), Bitmap> Cache = new();
    }
    readonly Dictionary<string, CombatScene> scenes = new();

    public static string FindFolder()
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory, @"C:\projects\chess" })
        {
            var p = Path.Combine(dir, "original");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return p;
        }
        return null;
    }

    public static OriginalArt Load(string dir)
    {
        var art = new OriginalArt();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")));
        var root = doc.RootElement;
        Bitmap Img(string rel) => new Bitmap(Path.Combine(dir, rel));
        foreach (var p in root.GetProperty("poses").EnumerateObject())
        {
            int i = int.Parse(p.Name);
            art.Poses[i] = new Frame
            {
                White = Img($"pose/p{i}_w.png"), Black = Img($"pose/p{i}_b.png"),
                Ax = p.Value.GetProperty("ax").GetInt32(), Ay = p.Value.GetProperty("ay").GetInt32(),
            };
        }
        foreach (var w in root.GetProperty("walk").EnumerateObject())
        {
            int i = int.Parse(w.Name);
            var list = new List<Frame>();
            int j = 0;
            foreach (var f in w.Value.EnumerateArray())
            {
                list.Add(new Frame
                {
                    White = Img($"walk/w{i}_{j}_w.png"), Black = Img($"walk/w{i}_{j}_b.png"),
                    Ax = f.GetProperty("ax").GetInt32(), Ay = f.GetProperty("ay").GetInt32(),
                });
                j++;
            }
            if (list.Count > 0) art.Walks[i] = list;
        }
        foreach (var s in root.GetProperty("sounds").EnumerateObject())
            art.Sounds[s.Name] = Path.Combine(dir, s.Value.GetString());
        art.folder = dir;
        var sq = Path.Combine(dir, "squares.json");
        if (File.Exists(sq))
        {
            using var sd = JsonDocument.Parse(File.ReadAllText(sq));
            art.sqX = sd.RootElement.GetProperty("x").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            art.rowY = sd.RootElement.GetProperty("y").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        }
        var sfx = Path.Combine(dir, "sfx", "sfx.json");
        if (File.Exists(sfx))
        {
            using var fd = JsonDocument.Parse(File.ReadAllText(sfx));
            foreach (var e in fd.RootElement.EnumerateObject())
            {
                int x = int.Parse(e.Name);
                art.SfxWord[x] = e.Value.GetInt32();
                art.SfxFiles[x] = Path.Combine(dir, "sfx", $"sfx{x}.wav");
            }
        }
        var grid = root.GetProperty("grid");
        art.cx = grid.GetProperty("cx").GetSingle();
        foreach (var r in grid.GetProperty("rows").EnumerateArray())
            art.rows.Add((r.GetProperty("y").GetSingle(), r.GetProperty("pitch").GetSingle()));
        art.Board3D = Img("board3d.png");
        art.Title = Img("title.png");
        Bitmap Opt(string rel) => File.Exists(Path.Combine(dir, rel)) ? Img(rel) : null;
        art.LogoBattle = Opt("ui/logo_battle.png");
        art.LogoChess = Opt("ui/logo_chess.png");
        foreach (var (t, c) in new[] { (P.Queen, 'Q'), (P.Rook, 'R'), (P.Bishop, 'B'), (P.Knight, 'N') })
            if (Opt($"ui/promo_{c}.png") is Bitmap b) art.PromoPict[t] = b;
        return art;
    }

    // Точка на оригинальном экране 512x340 для непрерывных координат клетки (центр клетки = file+0.5).
    public PointF ScreenPoint(float file, float rank, out float pitch)
    {
        if (sqX != null)
        {
            float fi = file - 0.5f, row = 7 - (rank - 0.5f);
            int f0 = Math.Clamp((int)MathF.Floor(fi), 0, 6), r0 = Math.Clamp((int)MathF.Floor(row), 0, 6);
            float tf = fi - f0, ur = row - r0;
            float X(int rr, int ff) => sqX[rr * 8 + ff];
            float xa = X(r0, f0) + (X(r0, f0 + 1) - X(r0, f0)) * tf;
            float xb = X(r0 + 1, f0) + (X(r0 + 1, f0 + 1) - X(r0 + 1, f0)) * tf;
            float pa = (X(r0, 7) - X(r0, 0)) / 7, pb = (X(r0 + 1, 7) - X(r0 + 1, 0)) / 7;
            pitch = pa + (pb - pa) * ur;
            return new PointF(xa + (xb - xa) * ur, rowY[r0] + (rowY[r0 + 1] - rowY[r0]) * ur);
        }
        float r = rank - 0.5f, f = file - 0.5f;
        int i = (int)MathF.Floor(r);
        i = Math.Clamp(i, 0, rows.Count - 2);
        float t = r - i;
        float y = rows[i].y + (rows[i + 1].y - rows[i].y) * t;
        pitch = rows[i].pitch + (rows[i + 1].pitch - rows[i].pitch) * t;
        return new PointF(cx + (f - 3.5f) * pitch, y);
    }

    // Направление по компасу: 0 = от зрителя (к чёрным), 2 = вправо, 4 = к зрителю, 6 = влево.
    public static int Compass(float df, float dr)
    {
        if (Math.Abs(df) < 0.001f && Math.Abs(dr) < 0.001f) return -1;
        double ang = Math.Atan2(df, dr) * 180 / Math.PI;   // 0 = вперёд (dr>0), 90 = вправо
        return ((int)Math.Round(ang / 45) + 8) % 8;
    }

    // Номер позы для фигуры, смотрящей в заданную сторону; moving — есть ли у позы цикл ходьбы.
    public int PoseFor(int type, int dir, bool white, bool moving)
    {
        if (dir < 0) dir = white ? 0 : 4;
        switch (type)
        {
            case P.Pawn:
            {
                int d4 = Snap4(dir, preferVertical: true);
                return d4 switch { 0 => 16, 2 => 41, 4 => 19, _ => 45 };
            }
            case P.Knight: return 12 + Snap4(dir, true) / 2;
            case P.Bishop:
            {
                int diag = dir % 2 == 1 ? dir : (dir + 1) % 8;
                return diag switch { 1 => 0, 3 => 1, 5 => 2, _ => 3 };
            }
            case P.Rook: return moving ? 32 + Snap4(dir, true) / 2 : 36;
            case P.Queen: return 24 + dir;
            default: return 4 + dir;
        }
    }

    static int Snap4(int dir, bool preferVertical)
    {
        if (dir % 2 == 0) return dir;
        // диагональ: выбираем вертикальную составляющую
        if (preferVertical) return dir is 1 or 7 ? 0 : 4;
        return dir is 1 or 3 ? 2 : 6;
    }

    static char Letter(int type) => type switch
    {
        P.Bishop => 'B', P.King => 'K', P.Knight => 'N', P.Pawn => 'P', P.Queen => 'Q', _ => 'R',
    };

    // Сцена «победитель берёт побеждённого»; null, если её нет.
    public CombatScene Scene(int victimType, int killerType) => SceneByTag($"{Letter(victimType)}{Letter(killerType)}");

    // Сцена по имени папки: "PN" — оригинал, "PN_r2" — бой, собранный по рецепту из последовательностей оригинала.
    public CombatScene SceneByTag(string tag)
    {
        if (scenes.TryGetValue(tag, out var cached)) return cached;
        var dir = Path.Combine(folder, "combat", tag);
        var file = Path.Combine(dir, "scene.json");
        CombatScene sc = null;
        if (File.Exists(file))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var r = doc.RootElement;
            sc = new CombatScene
            {
                Dir = dir,
                Ticks = r.GetProperty("ticks").GetInt32(),
                Victim = r.GetProperty("victim").EnumerateArray().Select(e => e.GetInt32()).ToArray(),
                Killer = r.GetProperty("killer").EnumerateArray().Select(e => e.GetInt32()).ToArray(),
            };
            foreach (var s in r.GetProperty("sounds").EnumerateArray())
                sc.Sounds.Add((s[0].GetInt32(), s[1].GetInt32(), s[2].GetInt32(), s.GetArrayLength() > 3 ? s[3].GetInt32() : 0));
            sc.Impact = r.TryGetProperty("impact", out var imp) ? imp.GetInt32() : sc.Ticks / 2;
            sc.KillerPre = sc.Killer.Take(sc.Impact + 1).Where(i => i >= 0).Distinct().Count();
            sc.VictimPost = sc.Victim.Skip(sc.Impact).Where(i => i >= 0).Distinct().Count();
            foreach (var a in r.GetProperty("images").EnumerateArray())
                sc.Anchors.Add((a[0].GetInt32(), a[1].GetInt32()));
        }
        scenes[tag] = sc;
        return sc;
    }

    // Вариант поединка: атака победителя из сцены Attack, бой побеждённого до удара из сцены Pre
    // (точная сцена этой пары — побеждённый сражается с тем же типом фигуры), гибель — из сцены Death.
    public record Variant(CombatScene Attack, CombatScene Death, CombatScene Pre, bool Exact);

    static readonly int[] AllTypes = { P.Pawn, P.Knight, P.Bishop, P.Rook, P.Queen, P.King };

    // До 5 вариантов: точная сцена оригинала и 4 сочетания атак и гибелей из других сцен оригинала.
    public List<Variant> Variants(int victimType, int killerType)
    {
        // сцена оригинала и бои, собранные по правилам движка оригинала (плейлисты последовательностей
        // тех же боевых блоков + скрипты тайминга), — каждая проигрывается целиком, как настоящая сцена
        var list = new List<Variant>();
        string tag = $"{Letter(victimType)}{Letter(killerType)}";
        var exact = SceneByTag(tag);
        if (exact != null) list.Add(new Variant(exact, exact, exact, true));
        for (int i = 1; i <= 4; i++)
        {
            var r = SceneByTag($"{tag}_r{i}");
            if (r != null) list.Add(new Variant(r, r, r, true));
        }
        return list;
    }

    static readonly Color[] PalW = { Color.Black, Color.White, Color.FromArgb(236, 226, 200), Color.Transparent };
    static readonly Color[] PalB = { Color.Black, Color.FromArgb(172, 172, 184), Color.FromArgb(44, 40, 50), Color.Transparent };

    // Кадр сцены в цвете нужной стороны (PNG хранит индексы 0..3).
    public Bitmap SceneImage(CombatScene sc, int index, bool white)
    {
        if (sc.Cache.TryGetValue((index, white), out var bmp)) return bmp;
        bmp = IndexedToColor(Path.Combine(sc.Dir, $"{index}.png"), white);
        sc.Cache[(index, white)] = bmp;
        return bmp;
    }

    // PNG с индексами 0..3 (контур, блик, тело, прозрачность) → цветной кадр нужной стороны.
    public static Bitmap IndexedToColor(string path, bool white)
    {
        using var loaded = new Bitmap(path);
        using var src = loaded.Clone(new Rectangle(0, 0, loaded.Width, loaded.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var pal = white ? PalW : PalB;
        var bmp = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, src.Width, src.Height);
        var sd = src.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var dd = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var buf = new byte[sd.Stride * src.Height];
        System.Runtime.InteropServices.Marshal.Copy(sd.Scan0, buf, 0, buf.Length);
        var outb = new byte[dd.Stride * src.Height];
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                int v = Math.Min(3, (int)buf[y * sd.Stride + x * 4 + 2]);
                var c = pal[v]; int o = y * dd.Stride + x * 4;
                outb[o] = c.B; outb[o + 1] = c.G; outb[o + 2] = c.R; outb[o + 3] = c.A;
            }
        System.Runtime.InteropServices.Marshal.Copy(outb, 0, dd.Scan0, outb.Length);
        src.UnlockBits(sd); bmp.UnlockBits(dd);
        return bmp;
    }

    public Frame FrameFor(int pose, bool moving, float walkPhase)
    {
        if (moving && Walks.TryGetValue(pose, out var w))
            return w[(int)(walkPhase * 1.6f) % w.Count];
        return Poses.TryGetValue(pose, out var f) ? f : null;
    }
}
