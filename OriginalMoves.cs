using System.Drawing;
using System.Text.Json;

namespace BattleChess;

// Движение фигур так, как его делает движок оригинальной Battle Chess (DOWALK / TURN / WALK / TR / SLIDE):
// поворот к цели последовательностями поворота, ходьба — цикл ходьбы ровно один раз на клетку (кадр за тик),
// ладья превращается из башни в голема и обратно, конь идёт буквой «Г», фигура на его пути отступает.
public class OriginalMoveData
{
    public class Piece
    {
        public int Facings;
        public int[] Stand = Enumerable.Repeat(-1, 8).ToArray();
        public int[] Walk = Enumerable.Repeat(-1, 8).ToArray();
        public int[] Cw = Enumerable.Repeat(-1, 8).ToArray(), CwTo = Enumerable.Repeat(-1, 8).ToArray();
        public int[] Ccw = Enumerable.Repeat(-1, 8).ToArray(), CcwTo = Enumerable.Repeat(-1, 8).ToArray();
        public Dictionary<int, List<(int step, int sfx, int pitch)>> WalkSounds = new();
        public Dictionary<int, List<(int ax, int ay)>> Seqs = new();
        public Dictionary<(int seq, int frame, bool white), Bitmap> Cache = new();
        public string Dir;
    }

    public readonly Dictionary<int, Piece> Pieces = new();
    static readonly string[] FacingNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    public static OriginalMoveData Load(string folder)
    {
        var file = Path.Combine(folder, "movement.json");
        if (!File.Exists(file)) return null;
        var md = new OriginalMoveData();
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var types = new Dictionary<string, (int type, char letter)>
        {
            ["bishop"] = (P.Bishop, 'B'), ["king"] = (P.King, 'K'), ["knight"] = (P.Knight, 'N'),
            ["pawn"] = (P.Pawn, 'P'), ["queen"] = (P.Queen, 'Q'), ["rook"] = (P.Rook, 'R'),
        };
        foreach (var pe in doc.RootElement.GetProperty("pieces").EnumerateObject())
        {
            if (!types.TryGetValue(pe.Name, out var t)) continue;
            var v = pe.Value;
            var p = new Piece { Facings = v.GetProperty("facings").GetInt32(), Dir = Path.Combine(folder, "move", t.letter.ToString()) };
            foreach (var s in v.GetProperty("standing_pose_by_facing").EnumerateObject())
                p.Stand[Array.IndexOf(FacingNames, s.Name)] = s.Value.GetInt32();
            foreach (var w in v.GetProperty("walk").EnumerateObject())
            {
                int f = Array.IndexOf(FacingNames, w.Name);
                p.Walk[f] = w.Value.GetProperty("seq").GetInt32();
                var list = new List<(int, int, int)>();
                if (w.Value.TryGetProperty("sound_script", out var ss) && ss.TryGetProperty("sound_steps", out var steps))
                    foreach (var st in steps.EnumerateArray())
                        foreach (var snd in st.GetProperty("sounds").EnumerateArray())
                            list.Add((st.GetProperty("step").GetInt32(), snd.GetProperty("sfx").GetInt32(), snd.GetProperty("pitch").GetInt32()));
                p.WalkSounds[p.Walk[f]] = list;
            }
            foreach (var (prop, seqArr, toArr) in new[] { ("turn_cw", p.Cw, p.CwTo), ("turn_ccw", p.Ccw, p.CcwTo) })
                if (v.TryGetProperty(prop, out var tt))
                    foreach (var e in tt.EnumerateObject())
                    {
                        int f = Array.IndexOf(FacingNames, e.Name);
                        seqArr[f] = e.Value.GetProperty("seq").GetInt32();
                        toArr[f] = Array.IndexOf(FacingNames, e.Value.GetProperty("to").GetString());
                    }
            var seqFile = Path.Combine(p.Dir, "seqs.json");
            if (File.Exists(seqFile))
            {
                using var sd = JsonDocument.Parse(File.ReadAllText(seqFile));
                foreach (var e in sd.RootElement.EnumerateObject())
                    p.Seqs[int.Parse(e.Name)] = e.Value.GetProperty("frames").EnumerateArray().Select(a => (a[0].GetInt32(), a[1].GetInt32())).ToList();
            }
            md.Pieces[t.type] = p;
        }
        return md;
    }

    public int FrameCount(int type, int seq) => Pieces.TryGetValue(type, out var p) && p.Seqs.TryGetValue(seq, out var f) ? f.Count : 0;

    public (Bitmap bmp, int ax, int ay)? Frame(int type, int seq, int frame, bool white)
    {
        if (!Pieces.TryGetValue(type, out var p) || !p.Seqs.TryGetValue(seq, out var frames) || frames.Count == 0) return null;
        frame = Math.Clamp(frame, 0, frames.Count - 1);
        if (!p.Cache.TryGetValue((seq, frame, white), out var bmp))
        {
            bmp = OriginalArt.IndexedToColor(Path.Combine(p.Dir, $"{seq}_{frame}.png"), white);
            p.Cache[(seq, frame, white)] = bmp;
        }
        return (bmp, frames[frame].ax, frames[frame].ay);
    }
}

public partial class Scene
{
    public OriginalMoveData Moves;      // задаётся формой, когда загружена оригинальная графика
    public int ForcedTechnique = -1;    // для проверки: номер варианта боя, -1 — случайный
    const float OTick = 0.1f;           // такт движка оригинала: 10 кадров в секунду

    // «Домашнее» направление взгляда на экране (к сопернику); при перевёрнутой доске — противоположное.
    public int HomeFacing(Actor a)
    {
        int h = a.Type == P.Bishop ? (a.IsWhite ? 1 : 5) : (a.IsWhite ? 0 : 4);
        return Flip ? (h + 4) % 8 : h;
    }

    // Направление на экране для хода по доске (df, dr) с учётом переворота.
    int SCompass(float df, float dr) => Flip ? OriginalArt.Compass(-df, -dr) : OriginalArt.Compass(df, dr);

    bool UseOriginalMoves(Actor a) => Original && Moves != null && Moves.Pieces.ContainsKey(a.Type);

    IEnumerable<int> OTicks(int n)
    {
        for (int i = 0; i < n; i++)
            for (float t = 0; t < OTick; t += Dt) yield return 0;
    }

    // Проиграть последовательность движения на месте (поворот, превращение): кадр за тик.
    IEnumerable<int> OPlay(Actor a, int seq)
    {
        int n = Moves.FrameCount(a.Type, seq);
        if (n == 0) yield break;
        a.OSeq = seq;
        for (int i = 0; i < n; i++)
        {
            a.OFrame = i;
            foreach (var x in OTicks(1)) yield return x;
        }
        a.OSeq = -1;
    }

    IEnumerable<int> OTurn(Actor a, int target)
    {
        var p = Moves.Pieces[a.Type];
        if (a.OFacing < 0) a.OFacing = HomeFacing(a);
        int guard = 0;
        while (a.OFacing != target && guard++ < 8)
        {
            int cw = (target - a.OFacing + 8) % 8, ccw = (a.OFacing - target + 8) % 8;
            bool goCw = cw < ccw;
            int seq = goCw ? p.Cw[a.OFacing] : p.Ccw[a.OFacing];
            int to = goCw ? p.CwTo[a.OFacing] : p.CcwTo[a.OFacing];
            if (seq < 0 || to < 0) { a.OFacing = target; break; }
            foreach (var x in OPlay(a, seq)) yield return x;
            a.OFacing = to;
        }
    }

    // Ходьба на одну клетку: цикл ходьбы текущего направления ровно один раз, позиция интерполируется по кадрам.
    IEnumerable<int> OWalk(Actor a, float toF, float toR)
    {
        var p = Moves.Pieces[a.Type];
        int seq = a.OFacing >= 0 ? p.Walk[a.OFacing] : -1;
        if (seq < 0) seq = p.Walk.FirstOrDefault(s => s >= 0);
        int n = Moves.FrameCount(a.Type, seq);
        float f0 = a.F, r0 = a.R;
        p.WalkSounds.TryGetValue(seq, out var sounds);
        a.OSeq = seq;
        for (int i = 0; i < Math.Max(1, n); i++)
        {
            float u = n > 0 ? (float)i / n : 1;
            a.F = f0 + (toF - f0) * u; a.R = r0 + (toR - r0) * u;
            a.OFrame = i;
            if (sounds != null) foreach (var s in sounds) if (s.step == i) CombatSound(s.sfx, s.pitch);
            foreach (var x in OTicks(1)) yield return x;
        }
        a.F = toF; a.R = toR; a.OSeq = -1;
    }

    // Ладья: башня ⇄ голем.
    IEnumerable<int> OToGolem(Actor a, bool faceSouth)
    {
        foreach (var x in OPlay(a, faceSouth ? 15 : 14)) yield return x;
        a.OTower = false; a.OFacing = faceSouth ? 4 : 0;
    }

    IEnumerable<int> OToTower(Actor a)
    {
        int target = a.OFacing == 4 ? 4 : a.OFacing == 0 ? 0 : HomeFacing(a);
        foreach (var x in OTurn(a, target)) yield return x;
        foreach (var x in OPlay(a, target == 0 ? 16 : 17)) yield return x;
        a.OTower = true; a.OFacing = -1;
    }

    // Полный ход фигуры по правилам оригинала; stopBefore — остановиться на клетку раньше (подход к жертве).
    // lastStep — вместо обычной ходьбы на последнюю клетку (взятие): вызывается уже после поворота к ней, получает направление.
    public IEnumerable<int> OriginalMove(Actor a, int toFile, int toRank, bool stopBefore = false, bool goHome = true,
                                         Func<int, IEnumerable<int>> lastStep = null)
    {
        int fromFile = (int)MathF.Floor(a.F), fromRank = (int)MathF.Floor(a.R);
        int df = toFile - fromFile, dr = toRank - fromRank;
        if (df == 0 && dr == 0) yield break;
        var path = new List<(int f, int r)>();
        if (a.Type == P.Knight)
        {
            // первая нога — одна клетка по короткой оси, затем две по длинной
            if (Math.Abs(dr) == 1) { path.Add((fromFile, fromRank + dr)); path.Add((fromFile + Math.Sign(df), fromRank + dr)); path.Add((toFile, toRank)); }
            else { path.Add((fromFile + df, fromRank)); path.Add((fromFile + df, fromRank + Math.Sign(dr))); path.Add((toFile, toRank)); }
        }
        else
        {
            int n = Math.Max(Math.Abs(df), Math.Abs(dr));
            for (int i = 1; i <= n; i++) path.Add((fromFile + Math.Sign(df) * i, fromRank + Math.Sign(dr) * i));
        }
        if (stopBefore && path.Count > 0) path.RemoveAt(path.Count - 1);

        if (a.Type == P.Rook && a.OTower)
            foreach (var x in OToGolem(a, (Flip ? dr > 0 : dr < 0) || (dr == 0 && HomeFacing(a) == 4))) yield return x;

        Actor slid = null; (float dx, float dy) slide = (0, 0);
        int cf = fromFile, cr = fromRank;
        foreach (var (sf, sr) in path)
        {
            int facing = SCompass(sf - cf, sr - cr);
            if (a.Type == P.Pawn && (facing == 2 || facing == 6)) facing = facing == 2 ? 1 : 7;
            if (facing >= 0 && facing != a.OFacing) foreach (var x in OTurn(a, facing)) yield return x;
            // конь: фигура на промежуточной клетке отступает в сторону первой ноги
            if (a.Type == P.Knight && (sf != toFile || sr != toRank))
            {
                var block = Actors.FirstOrDefault(o => o != a && !o.Dead && (int)MathF.Floor(o.F) == sf && (int)MathF.Floor(o.R) == sr);
                if (block != null && block != slid)
                {
                    if (slid != null) foreach (var x in OSlide(slid, -slide.dx, -slide.dy)) yield return x;
                    int d0 = SCompass(path[0].f - fromFile, path[0].r - fromRank);
                    slide = (SlideDX[d0], SlideDY[d0]); slid = block;
                    foreach (var x in OSlide(block, slide.dx, slide.dy)) yield return x;
                }
            }
            if (lastStep != null && sf == path[^1].f && sr == path[^1].r)
                foreach (var x in lastStep(a.OFacing)) yield return x;
            else
                foreach (var x in OWalk(a, sf + 0.5f, sr + 0.5f)) yield return x;
            cf = sf; cr = sr;
        }
        if (slid != null) foreach (var x in OSlide(slid, -slide.dx, -slide.dy)) yield return x;

        if (!goHome) yield break;
        if (a.Type == P.Rook) { foreach (var x in OToTower(a)) yield return x; }
        else foreach (var x in OTurn(a, HomeFacing(a))) yield return x;
    }

    static readonly float[] SlideDX = { 0, 19, 19, 19, 0, -19, -19, -19 };
    static readonly float[] SlideDY = { -9, -9, 0, 9, 9, 9, 0, -9 };

    IEnumerable<int> OSlide(Actor b, float dx, float dy)
    {
        float x0 = b.OShift.X, y0 = b.OShift.Y;
        for (int i = 1; i <= 6; i++)
        {
            b.OShift = new PointF(x0 + dx * i / 6, y0 + dy * i / 6);
            foreach (var x in OTicks(1)) yield return x;
        }
    }

    // ---------- взятие, как его ставит DOWALK оригинала ----------
    // Позиции боя (CP) в пикселях экрана 512x340 относительно центра клетки жертвы.
    public static readonly PointF CpKiller = new(15, -1), CpVictim = new(-25, 13);

    // Ладья и конь встают к бою прямо (N/S), остальные — по диагонали (NE/SW).
    static bool FacesStraight(Actor a) => a.Type is P.Rook or P.Knight;

    // CP: один проход цикла ходьбы текущего направления (целиком или только первая половина)
    // от текущей точки к (toF,toR)+toShift; после последнего кадра — точно в цель.
    IEnumerable<int> OCapturePos(Actor a, float toF, float toR, PointF toShift, bool full)
    {
        var p = Moves.Pieces[a.Type];
        int seq = a.OFacing >= 0 ? p.Walk[a.OFacing] : -1;
        if (seq < 0) seq = p.Walk.FirstOrDefault(s => s >= 0);
        int len = Moves.FrameCount(a.Type, seq), n = full ? len : len / 2;
        float f0 = a.F, r0 = a.R;
        var s0 = a.OShift;
        p.WalkSounds.TryGetValue(seq, out var sounds);
        a.OSeq = seq;
        for (int i = 0; i < n; i++)
        {
            float u = (float)i / n;
            a.F = f0 + (toF - f0) * u; a.R = r0 + (toR - r0) * u;
            a.OShift = new PointF(s0.X + (toShift.X - s0.X) * u, s0.Y + (toShift.Y - s0.Y) * u);
            a.OFrame = i;
            if (sounds != null) foreach (var s in sounds) if (s.step == i) CombatSound(s.sfx, s.pitch);
            foreach (var x in OTicks(1)) yield return x;
        }
        a.F = toF; a.R = toR; a.OShift = toShift; a.OSeq = -1;
    }

    // HWALK: первая половина цикла ходьбы со сдвигом на (dx,dy) пикселей.
    IEnumerable<int> OHalfWalk(Actor a, float dx, float dy)
    {
        foreach (var x in OCapturePos(a, a.F, a.R, new PointF(a.OShift.X + dx, a.OShift.Y + dy), false)) yield return x;
    }

    // Последний шаг взятия (dir — направление хода, у пешки E/W уже заменены на NE/NW):
    // жертва отворачивается, отходит на позицию боя и поворачивается к победителю; победитель с N/NE/E/SE/NW
    // входит на клетку, поворачивается на NE (ладья, конь — на N) и полуциклом выходит на позицию,
    // с S/SW/W — останавливается перед клеткой и выходит на позицию полным циклом; затем поворачивается на SW (S).
    IEnumerable<int> OCaptureStep(Actor a, Actor d, int dir)
    {
        float vf = MathF.Floor(d.F) + 0.5f, vr = MathF.Floor(d.R) + 0.5f;
        bool walkIn = dir is not (4 or 5 or 6);
        bool victimAnim = UseOriginalMoves(d);
        if (victimAnim)
        {
            if (d.Type == P.Rook && d.OTower) foreach (var x in OToGolem(d, true)) yield return x;
            int aside = dir == 1 ? (d.Type == P.Bishop ? 5 : 6) : (d.Type == P.Knight || d.Type == P.Rook ? 4 : 5);
            foreach (var x in OTurn(d, aside)) yield return x;
        }
        foreach (var x in All(KillerToCombat(a, vf, vr, walkIn), victimAnim ? VictimToCombat(d, dir) : Wait(0))) yield return x;
    }

    IEnumerable<int> KillerToCombat(Actor a, float vf, float vr, bool walkIn)
    {
        if (walkIn)
        {
            foreach (var x in OWalk(a, vf, vr)) yield return x;
            foreach (var x in OTurn(a, FacesStraight(a) ? 0 : 1)) yield return x;
        }
        foreach (var x in OCapturePos(a, vf, vr, CpKiller, full: !walkIn)) yield return x;
        foreach (var x in OTurn(a, FacesStraight(a) ? 4 : 5)) yield return x;
    }

    IEnumerable<int> VictimToCombat(Actor d, int dir)
    {
        if (dir == 1)
        {
            foreach (var x in OHalfWalk(d, SlideDX[6], SlideDY[6])) yield return x;
            foreach (var x in OTurn(d, d.Type == P.Bishop ? 3 : 4)) yield return x;
        }
        foreach (var x in OCapturePos(d, d.F, d.R, CpVictim, full: false)) yield return x;
        foreach (var x in OTurn(d, FacesStraight(d) ? 0 : 1)) yield return x;
    }

    // После боя победитель полуциклом возвращается с позиции боя в центр клетки (CP p3=2).
    IEnumerable<int> OFromCombat(Actor a, float f, float r)
    {
        a.F = f; a.R = r; a.OShift = CpKiller;
        a.OFacing = FacesStraight(a) ? 4 : 5;
        if (a.Type == P.Rook) a.OTower = false;
        foreach (var x in OCapturePos(a, f, r, PointF.Empty, full: false)) yield return x;
    }
}
