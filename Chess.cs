namespace BattleChess;

// Фигуры: >0 белые, <0 чёрные. 1 пешка, 2 конь, 3 слон, 4 ладья, 5 ферзь, 6 король.
public static class P
{
    public const int Pawn = 1, Knight = 2, Bishop = 3, Rook = 4, Queen = 5, King = 6;
    public static int File(int sq) => sq & 7;
    public static int Rank(int sq) => sq >> 3;
    public static int Sq(int f, int r) => (f < 0 || f > 7 || r < 0 || r > 7) ? -1 : r * 8 + f;
    public static string Name(int sq) => $"{(char)('a' + File(sq))}{Rank(sq) + 1}";

    public static readonly string[] RuNames = { "", "Пешка", "Конь", "Слон", "Ладья", "Ферзь", "Король" };
    public static readonly string[] RuLetters = { "", "", "К", "С", "Л", "Ф", "Кр" };
}

public struct Move
{
    public const int FCapture = 1, FEnPassant = 2, FCastle = 4, FDouble = 8;
    public int From, To, Promo, Flags, Moved, Captured;

    public bool IsCapture => (Flags & FCapture) != 0;
    public bool IsEnPassant => (Flags & FEnPassant) != 0;
    public bool IsCastle => (Flags & FCastle) != 0;

    // Клетка, на которой стоит взятая фигура (при взятии на проходе — не To).
    public int CaptureSquare => IsEnPassant ? (To + (Moved > 0 ? -8 : 8)) : To;

    public bool SameAs(Move o) => From == o.From && To == o.To && Promo == o.Promo;
    public override string ToString() => P.Name(From) + P.Name(To) + (Promo != 0 ? "nbrq"[Promo - 2].ToString() : "");
}

public enum GameState { Playing, WhiteMates, BlackMates, Stalemate, FiftyMoves, Repetition, Insufficient }

public class Position
{
    public int[] B = new int[64];
    public int Side = 1;
    public int Castle;          // 1 = белые O-O, 2 = белые O-O-O, 4 = чёрные O-O, 8 = чёрные O-O-O
    public int Ep = -1;
    public int Half;
    public int Full = 1;
    public ulong Hash;

    struct Undo { public Move M; public int Castle, Ep, Half; public ulong Hash; }
    readonly List<Undo> undo = new();
    public readonly List<ulong> HashHistory = new();

    static readonly ulong[,] ZPiece = new ulong[13, 64];
    static readonly ulong[] ZCastle = new ulong[16];
    static readonly ulong[] ZEp = new ulong[8];
    static readonly ulong ZSide;
    static readonly int[] CastleMask = new int[64];
    public static readonly int[][] KnightTo = new int[64][];
    public static readonly int[][] KingTo = new int[64][];
    static readonly (int, int)[] Diag = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
    static readonly (int, int)[] Ortho = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    static Position()
    {
        var rnd = new Random(20261019);
        ulong R() { var b = new byte[8]; rnd.NextBytes(b); return BitConverter.ToUInt64(b); }
        for (int p = 0; p < 13; p++) for (int s = 0; s < 64; s++) ZPiece[p, s] = R();
        for (int i = 0; i < 16; i++) ZCastle[i] = R();
        for (int i = 0; i < 8; i++) ZEp[i] = R();
        ZSide = R();
        for (int s = 0; s < 64; s++) CastleMask[s] = 15;
        CastleMask[0] = 13; CastleMask[4] = 12; CastleMask[7] = 14;
        CastleMask[56] = 7; CastleMask[60] = 3; CastleMask[63] = 11;
        (int, int)[] kn = { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) };
        for (int s = 0; s < 64; s++)
        {
            var a = new List<int>(); var k = new List<int>();
            foreach (var (df, dr) in kn) { int t = P.Sq(P.File(s) + df, P.Rank(s) + dr); if (t >= 0) a.Add(t); }
            for (int df = -1; df <= 1; df++) for (int dr = -1; dr <= 1; dr++)
                { if (df == 0 && dr == 0) continue; int t = P.Sq(P.File(s) + df, P.Rank(s) + dr); if (t >= 0) k.Add(t); }
            KnightTo[s] = a.ToArray(); KingTo[s] = k.ToArray();
        }
    }

    public static Position Start()
    {
        var p = new Position();
        int[] back = { 4, 2, 3, 5, 6, 3, 2, 4 };
        for (int f = 0; f < 8; f++)
        {
            p.B[f] = back[f]; p.B[8 + f] = 1;
            p.B[48 + f] = -1; p.B[56 + f] = -back[f];
        }
        p.Castle = 15;
        p.Hash = p.ComputeHash();
        p.HashHistory.Add(p.Hash);
        return p;
    }

    public Position Clone()
    {
        var p = new Position { Side = Side, Castle = Castle, Ep = Ep, Half = Half, Full = Full, Hash = Hash };
        Array.Copy(B, p.B, 64);
        p.HashHistory.AddRange(HashHistory);
        return p;
    }

    public ulong ComputeHash()
    {
        ulong h = 0;
        for (int s = 0; s < 64; s++) if (B[s] != 0) h ^= ZPiece[B[s] + 6, s];
        h ^= ZCastle[Castle];
        if (Ep >= 0) h ^= ZEp[P.File(Ep)];
        if (Side < 0) h ^= ZSide;
        return h;
    }

    public int KingSquare(int side)
    {
        for (int s = 0; s < 64; s++) if (B[s] == 6 * side) return s;
        return -1;
    }

    public bool Attacked(int sq, int by)
    {
        int f = P.File(sq), r = P.Rank(sq);
        // пешки
        int pr = r - by;
        if (pr >= 0 && pr < 8)
        {
            if (f > 0 && B[pr * 8 + f - 1] == by) return true;
            if (f < 7 && B[pr * 8 + f + 1] == by) return true;
        }
        foreach (var t in KnightTo[sq]) if (B[t] == 2 * by) return true;
        foreach (var t in KingTo[sq]) if (B[t] == 6 * by) return true;
        foreach (var (df, dr) in Diag)
            for (int x = f + df, y = r + dr; x >= 0 && x < 8 && y >= 0 && y < 8; x += df, y += dr)
            {
                int p = B[y * 8 + x];
                if (p == 0) continue;
                if (p == 3 * by || p == 5 * by) return true;
                break;
            }
        foreach (var (df, dr) in Ortho)
            for (int x = f + df, y = r + dr; x >= 0 && x < 8 && y >= 0 && y < 8; x += df, y += dr)
            {
                int p = B[y * 8 + x];
                if (p == 0) continue;
                if (p == 4 * by || p == 5 * by) return true;
                break;
            }
        return false;
    }

    public bool InCheck(int side) { int k = KingSquare(side); return k >= 0 && Attacked(k, -side); }

    void Add(List<Move> list, int from, int to, int flags, int promo = 0)
    {
        int cap = B[to];
        if (cap != 0) flags |= Move.FCapture;
        list.Add(new Move { From = from, To = to, Flags = flags, Promo = promo, Moved = B[from], Captured = cap });
    }

    public List<Move> Pseudo(bool capturesOnly = false)
    {
        var list = new List<Move>(48);
        int s0 = Side;
        for (int s = 0; s < 64; s++)
        {
            int p = B[s];
            if (p == 0 || Math.Sign(p) != s0) continue;
            int t = Math.Abs(p), f = P.File(s), r = P.Rank(s);
            switch (t)
            {
                case P.Pawn:
                {
                    int r1 = r + s0, lastRank = s0 > 0 ? 7 : 0, startRank = s0 > 0 ? 1 : 6;
                    void PawnTo(int to, int flags)
                    {
                        if (P.Rank(to) == lastRank)
                            for (int pr = P.Queen; pr >= P.Knight; pr--) Add(list, s, to, flags, pr);
                        else Add(list, s, to, flags);
                    }
                    int fwd = P.Sq(f, r1);
                    if (fwd >= 0 && B[fwd] == 0 && (!capturesOnly || P.Rank(fwd) == lastRank))
                    {
                        PawnTo(fwd, 0);
                        int two = P.Sq(f, r + 2 * s0);
                        if (!capturesOnly && r == startRank && B[two] == 0) Add(list, s, two, Move.FDouble);
                    }
                    foreach (int df in new[] { -1, 1 })
                    {
                        int to = P.Sq(f + df, r1);
                        if (to < 0) continue;
                        if (B[to] != 0 && Math.Sign(B[to]) == -s0) PawnTo(to, 0);
                        else if (to == Ep)
                            list.Add(new Move { From = s, To = to, Flags = Move.FCapture | Move.FEnPassant, Moved = p, Captured = -s0 });
                    }
                    break;
                }
                case P.Knight:
                case P.King:
                    foreach (var to in (t == P.Knight ? KnightTo[s] : KingTo[s]))
                    {
                        if (B[to] != 0 && Math.Sign(B[to]) == s0) continue;
                        if (capturesOnly && B[to] == 0) continue;
                        Add(list, s, to, 0);
                    }
                    if (t == P.King && !capturesOnly) GenCastle(list, s);
                    break;
                default:
                {
                    var dirs = t == P.Bishop ? Diag : t == P.Rook ? Ortho : Diag.Concat(Ortho);
                    foreach (var (df, dr) in dirs)
                        for (int x = f + df, y = r + dr; x >= 0 && x < 8 && y >= 0 && y < 8; x += df, y += dr)
                        {
                            int to = y * 8 + x;
                            if (B[to] == 0) { if (!capturesOnly) Add(list, s, to, 0); continue; }
                            if (Math.Sign(B[to]) == -s0) Add(list, s, to, 0);
                            break;
                        }
                    break;
                }
            }
        }
        return list;
    }

    void GenCastle(List<Move> list, int s)
    {
        int home = Side > 0 ? 4 : 60;
        if (s != home) return;
        int ks = Side > 0 ? 1 : 4, qs = Side > 0 ? 2 : 8;
        if ((Castle & ks) != 0 && B[home + 1] == 0 && B[home + 2] == 0 && B[home + 3] == 4 * Side
            && !Attacked(home, -Side) && !Attacked(home + 1, -Side) && !Attacked(home + 2, -Side))
            Add(list, home, home + 2, Move.FCastle);
        if ((Castle & qs) != 0 && B[home - 1] == 0 && B[home - 2] == 0 && B[home - 3] == 0 && B[home - 4] == 4 * Side
            && !Attacked(home, -Side) && !Attacked(home - 1, -Side) && !Attacked(home - 2, -Side))
            Add(list, home, home - 2, Move.FCastle);
    }

    public List<Move> Legal()
    {
        var res = new List<Move>();
        foreach (var m in Pseudo())
        {
            Make(m);
            if (!InCheck(-Side)) res.Add(m);
            Unmake();
        }
        return res;
    }

    public void Make(Move m)
    {
        undo.Add(new Undo { M = m, Castle = Castle, Ep = Ep, Half = Half, Hash = Hash });
        int p = B[m.From];
        B[m.From] = 0;
        if (m.IsEnPassant) B[m.CaptureSquare] = 0;
        B[m.To] = m.Promo != 0 ? m.Promo * Math.Sign(p) : p;
        if (m.IsCastle)
        {
            if (m.To > m.From) { B[m.To - 1] = B[m.To + 1]; B[m.To + 1] = 0; }
            else { B[m.To + 1] = B[m.To - 2]; B[m.To - 2] = 0; }
        }
        Castle &= CastleMask[m.From] & CastleMask[m.To];
        Ep = (m.Flags & Move.FDouble) != 0 ? (m.From + m.To) / 2 : -1;
        Half = (Math.Abs(p) == P.Pawn || m.IsCapture) ? 0 : Half + 1;
        if (Side < 0) Full++;
        Side = -Side;
        Hash = ComputeHash();
        HashHistory.Add(Hash);
    }

    public void Unmake()
    {
        var u = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        HashHistory.RemoveAt(HashHistory.Count - 1);
        var m = u.M;
        Side = -Side;
        if (Side < 0) Full--;
        B[m.From] = m.Moved;
        B[m.To] = 0;
        if (m.IsEnPassant) B[m.CaptureSquare] = m.Captured;
        else B[m.To] = m.Captured;
        if (m.IsCastle)
        {
            if (m.To > m.From) { B[m.To + 1] = B[m.To - 1]; B[m.To - 1] = 0; }
            else { B[m.To - 2] = B[m.To + 1]; B[m.To + 1] = 0; }
        }
        Castle = u.Castle; Ep = u.Ep; Half = u.Half; Hash = u.Hash;
    }

    public int UndoCount => undo.Count;

    public bool IsRepetition(int times)
    {
        int n = 0;
        for (int i = HashHistory.Count - 1; i >= 0 && i >= HashHistory.Count - 1 - Half; i--)
            if (HashHistory[i] == Hash && ++n >= times) return true;
        return false;
    }

    public bool InsufficientMaterial()
    {
        int minors = 0;
        for (int s = 0; s < 64; s++)
        {
            int t = Math.Abs(B[s]);
            if (t == P.Pawn || t == P.Rook || t == P.Queen) return false;
            if (t == P.Knight || t == P.Bishop) minors++;
        }
        return minors <= 1;
    }

    public GameState State()
    {
        if (Legal().Count == 0)
            return InCheck(Side) ? (Side > 0 ? GameState.BlackMates : GameState.WhiteMates) : GameState.Stalemate;
        if (Half >= 100) return GameState.FiftyMoves;
        if (IsRepetition(3)) return GameState.Repetition;
        if (InsufficientMaterial()) return GameState.Insufficient;
        return GameState.Playing;
    }

    // Нотация в русском стиле: Кf3, Сxb5+, O-O, e8Ф#
    public string San(Move m)
    {
        string s;
        int t = Math.Abs(m.Moved);
        if (m.IsCastle) s = m.To > m.From ? "O-O" : "O-O-O";
        else
        {
            s = P.RuLetters[t];
            if (t == P.Pawn) { if (m.IsCapture) s += (char)('a' + P.File(m.From)); }
            else
            {
                var others = Legal().Where(o => o.To == m.To && o.Moved == m.Moved && o.From != m.From).ToList();
                if (others.Count > 0)
                {
                    if (others.All(o => P.File(o.From) != P.File(m.From))) s += (char)('a' + P.File(m.From));
                    else if (others.All(o => P.Rank(o.From) != P.Rank(m.From))) s += (char)('1' + P.Rank(m.From));
                    else s += P.Name(m.From);
                }
            }
            if (m.IsCapture) s += "x";
            s += P.Name(m.To);
            if (m.Promo != 0) s += P.RuLetters[m.Promo];
        }
        Make(m);
        if (InCheck(Side)) s += Legal().Count == 0 ? "#" : "+";
        Unmake();
        return s;
    }
}
