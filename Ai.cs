using System.Diagnostics;

namespace BattleChess;

public class Ai
{
    public static readonly string[] LevelNames = { "Новичок", "Любитель", "Клубный игрок", "Мастер", "Гроссмейстер" };

    const int Inf = 1_000_000, Mate = 100_000;
    static readonly int[] Val = { 0, 100, 320, 330, 500, 900, 0 };

    // Таблицы позиционных бонусов (с точки зрения белых, первая строка — 8-я горизонталь).
    static readonly int[][] Pst =
    {
        null,
        new[] {
            0,0,0,0,0,0,0,0, 50,50,50,50,50,50,50,50, 10,10,20,30,30,20,10,10, 5,5,10,25,25,10,5,5,
            0,0,0,20,20,0,0,0, 5,-5,-10,0,0,-10,-5,5, 5,10,10,-20,-20,10,10,5, 0,0,0,0,0,0,0,0 },
        new[] {
            -50,-40,-30,-30,-30,-30,-40,-50, -40,-20,0,0,0,0,-20,-40, -30,0,10,15,15,10,0,-30, -30,5,15,20,20,15,5,-30,
            -30,0,15,20,20,15,0,-30, -30,5,10,15,15,10,5,-30, -40,-20,0,5,5,0,-20,-40, -50,-40,-30,-30,-30,-30,-40,-50 },
        new[] {
            -20,-10,-10,-10,-10,-10,-10,-20, -10,0,0,0,0,0,0,-10, -10,0,5,10,10,5,0,-10, -10,5,5,10,10,5,5,-10,
            -10,0,10,10,10,10,0,-10, -10,10,10,10,10,10,10,-10, -10,5,0,0,0,0,5,-10, -20,-10,-10,-10,-10,-10,-10,-20 },
        new[] {
            0,0,0,0,0,0,0,0, 5,10,10,10,10,10,10,5, -5,0,0,0,0,0,0,-5, -5,0,0,0,0,0,0,-5,
            -5,0,0,0,0,0,0,-5, -5,0,0,0,0,0,0,-5, -5,0,0,0,0,0,0,-5, 0,0,0,5,5,0,0,0 },
        new[] {
            -20,-10,-10,-5,-5,-10,-10,-20, -10,0,0,0,0,0,0,-10, -10,0,5,5,5,5,0,-10, -5,0,5,5,5,5,0,-5,
            0,0,5,5,5,5,0,-5, -10,5,5,5,5,5,0,-10, -10,0,5,0,0,0,0,-10, -20,-10,-10,-5,-5,-10,-10,-20 },
        new[] {
            -30,-40,-40,-50,-50,-40,-40,-30, -30,-40,-40,-50,-50,-40,-40,-30, -30,-40,-40,-50,-50,-40,-40,-30, -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20, -10,-20,-20,-20,-20,-20,-20,-10, 20,20,0,0,0,0,20,20, 20,30,10,0,0,10,30,20 },
    };
    static readonly int[] KingEnd =
    {
        -50,-40,-30,-20,-20,-30,-40,-50, -30,-20,-10,0,0,-10,-20,-30, -30,-10,20,30,30,20,-10,-30, -30,-10,30,40,40,30,-10,-30,
        -30,-10,30,40,40,30,-10,-30, -30,-10,20,30,30,20,-10,-30, -30,-30,0,0,0,0,-30,-30, -50,-30,-30,-30,-30,-30,-30,-50
    };

    int maxDepth, timeMs, noise;
    readonly Random rnd = new();
    readonly Stopwatch sw = new();
    volatile bool stop;
    bool canTimeOut;
    long nodes;
    readonly Dictionary<ulong, Move> bestMoves = new();
    readonly Move[,] killers = new Move[64, 2];

    public int LastScore { get; private set; }
    public int LastDepth { get; private set; }

    public void Cancel() => stop = true;

    public void SetLevel(int level)
    {
        (maxDepth, timeMs, noise) = level switch
        {
            0 => (1, 400, 180),
            1 => (2, 800, 70),
            2 => (3, 1500, 20),
            3 => (6, 2500, 0),
            _ => (8, 5000, 0),
        };
    }

    public static int Evaluate(Position p)
    {
        int score = 0, material = 0;
        for (int s = 0; s < 64; s++)
        {
            int t = Math.Abs(p.B[s]);
            if (t >= P.Knight && t <= P.Queen) material += Val[t];
        }
        bool endgame = material <= 2600;
        for (int s = 0; s < 64; s++)
        {
            int pc = p.B[s];
            if (pc == 0) continue;
            int t = Math.Abs(pc), f = P.File(s), r = P.Rank(s);
            int idx = pc > 0 ? (7 - r) * 8 + f : r * 8 + f;
            int v = Val[t] + (t == P.King && endgame ? KingEnd[idx] : Pst[t][idx]);
            score += pc > 0 ? v : -v;
        }
        return score * p.Side;
    }

    public Move? Think(Position root)
    {
        var pos = root.Clone();
        var legal = pos.Legal();
        if (legal.Count == 0) return null;
        stop = false; canTimeOut = false; nodes = 0;
        Array.Clear(killers);
        bestMoves.Clear();
        sw.Restart();

        Move best = legal[0];
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            Order(pos, legal, best, 0);
            int alpha = -Inf;
            Move iterBest = legal[0];
            int iterScore = -Inf;
            foreach (var m in legal)
            {
                pos.Make(m);
                int a = noise > 0 ? -Inf : alpha;
                int sc = -Search(pos, depth - 1, -Inf, -a, 1);
                pos.Unmake();
                if (stop) break;
                if (noise > 0) sc += rnd.Next(-noise, noise + 1);
                if (sc > iterScore) { iterScore = sc; iterBest = m; }
                if (sc > alpha) alpha = sc;
            }
            if (stop) break;
            best = iterBest;
            LastScore = iterScore; LastDepth = depth;
            canTimeOut = true;
            if (Math.Abs(iterScore) > Mate - 100) break;
            if (sw.ElapsedMilliseconds > timeMs / 2) break;
        }
        return best;
    }

    int Search(Position p, int depth, int alpha, int beta, int ply)
    {
        if ((++nodes & 1023) == 0 && canTimeOut && sw.ElapsedMilliseconds > timeMs) stop = true;
        if (stop) return 0;
        if (p.Half >= 100 || p.IsRepetition(2)) return 0;

        bool inCheck = p.InCheck(p.Side);
        if (inCheck) depth++;
        if (depth <= 0) return Quiesce(p, alpha, beta, ply, 0);

        var moves = p.Pseudo();
        bestMoves.TryGetValue(p.Hash, out var hashMove);
        Order(p, moves, hashMove, ply);

        int best = -Inf, legal = 0;
        Move bestMove = default;
        foreach (var m in moves)
        {
            p.Make(m);
            if (p.InCheck(-p.Side)) { p.Unmake(); continue; }
            legal++;
            int sc = -Search(p, depth - 1, -beta, -alpha, ply + 1);
            p.Unmake();
            if (stop) return 0;
            if (sc > best) { best = sc; bestMove = m; }
            if (sc > alpha) alpha = sc;
            if (alpha >= beta)
            {
                if (!m.IsCapture && ply < 64)
                {
                    killers[ply, 1] = killers[ply, 0];
                    killers[ply, 0] = m;
                }
                break;
            }
        }
        if (legal == 0) return inCheck ? -Mate + ply : 0;
        bestMoves[p.Hash] = bestMove;
        return best;
    }

    int Quiesce(Position p, int alpha, int beta, int ply, int qdepth)
    {
        if ((++nodes & 1023) == 0 && canTimeOut && sw.ElapsedMilliseconds > timeMs) stop = true;
        if (stop) return 0;
        int stand = Evaluate(p);
        if (stand >= beta || qdepth > 8) return stand;
        if (stand > alpha) alpha = stand;

        var moves = p.Pseudo(capturesOnly: true);
        Order(p, moves, default, -1);
        foreach (var m in moves)
        {
            p.Make(m);
            if (p.InCheck(-p.Side)) { p.Unmake(); continue; }
            int sc = -Quiesce(p, -beta, -alpha, ply + 1, qdepth + 1);
            p.Unmake();
            if (stop) return 0;
            if (sc >= beta) return sc;
            if (sc > alpha) alpha = sc;
        }
        return alpha;
    }

    void Order(Position p, List<Move> moves, Move first, int ply)
    {
        int Key(Move m)
        {
            if (m.SameAs(first) && (first.From != first.To)) return 1_000_000;
            int k = 0;
            if (m.IsCapture) k += 10_000 + Val[Math.Abs(m.Captured)] * 10 - Val[Math.Abs(m.Moved)] / 10;
            if (m.Promo != 0) k += 9_000 + Val[m.Promo];
            if (ply >= 0 && ply < 64 && !m.IsCapture)
            {
                if (m.SameAs(killers[ply, 0])) k += 5_000;
                else if (m.SameAs(killers[ply, 1])) k += 4_000;
            }
            return k;
        }
        var keyed = moves.Select(m => (m, Key(m))).OrderByDescending(x => x.Item2).Select(x => x.m).ToList();
        moves.Clear();
        moves.AddRange(keyed);
    }
}
