using System.Drawing;

namespace BattleChess;

public class Actor
{
    public int Piece;
    public float F, R;          // центр клетки = file + 0.5, rank + 0.5
    public Pose Pose = new();
    public bool Dead;
    public int Dir = -1;        // направление взгляда в оригинальной графике (компас 0..7), -1 — по умолчанию
    public int OFacing = -1;    // оригинал: направление 0..7 (N…NW), -1 — «домашнее»
    public int OSeq = -1, OFrame;   // оригинал: проигрываемая последовательность движения и её кадр
    public bool OTower = true;  // оригинал: ладья стоит башней
    public PointF OShift;       // оригинал: сдвиг в пикселях экрана 512x340 (фигура уступает путь коню)
    public bool IsWhite => Piece > 0;
    public int Type => Math.Abs(Piece);
}

public class Particle
{
    public float F, R, H, VF, VR, VH, Life, Max, Size, Grav = 0;
    public Color C;
    public int Kind;            // 0 светящаяся точка, 1 искра, 2 пыль, 3 обломок, 4 звёздочка
    public float Rot, VRot;
}

public class Effect
{
    public int Kind;            // 0 молния, 1 магический шар, 2 след удара, 3 ударная волна, 4 столп света,
                                // 5 луч между точками, 6 брошенное копьё, 7 ледяные шипы, 8 смерч
    public float Arc;           // высота дуги полёта (в клетках)
    public float T, Dur;
    public float F1, R1, H1, F2, R2, H2;
    public Color C;
    public int Seed;
}

// Сцена: актёры, частицы, эффекты и сценарии-корутины.
public partial class Scene
{
    public List<Actor> Actors = new();
    public readonly List<Particle> Particles = new();
    public readonly List<Effect> Effects = new();
    public float Shake, Flash;
    public float ZoomK, FocusF, FocusR;   // наезд камеры на место боя (0..1)
    public Actor FightA, FightD;

    // Идущая сцена поединка из оригинала (кадры рисует GameForm).
    public class CombatView
    {
        public OriginalArt.Variant V;
        public float Tick;
        public float F, R;
        public Actor Victim, Killer;
        public int KillerTick => V.Exact ? Math.Min((int)Tick, V.Attack.Killer.Length - 1)
            : Math.Min(Math.Min((int)Tick, Math.Max(0, V.Attack.Impact - 1)), V.Attack.Killer.Length - 1);
        // в точной сцене обе дорожки идут вместе; в сочетании побеждённый ждёт момента удара атаки
        // Кадр побеждённого: в точной сцене — своя дорожка; в сочетании до удара он сражается
        // (дорожка точной сцены, растянутая до момента удара атаки), после удара — гибель из другой сцены.
        public (OriginalArt.CombatScene sc, int tick) VictimFrame
        {
            get
            {
                int t = (int)Tick;
                if (V.Exact) return (V.Death, Math.Min(t, V.Death.Victim.Length - 1));
                if (t < V.Attack.Impact)
                {
                    int ip = Math.Max(1, V.Pre.Impact);
                    int pt = (int)((long)t * ip / Math.Max(1, V.Attack.Impact));
                    return (V.Pre, Math.Min(pt, V.Pre.Victim.Length - 1));
                }
                return (V.Death, Math.Min(t - V.Attack.Impact + V.Death.Impact, V.Death.Victim.Length - 1));
            }
        }
        public int Length => V.Exact ? V.Attack.Ticks
            : V.Attack.Impact + V.Death.Victim.Length - V.Death.Impact;
    }
    public CombatView Combat;
    public Func<int, int, List<OriginalArt.Variant>> LoadCombat;   // (побеждённый, победитель)
    public Action<int, int> CombatSound = (_, _) => { };          // (номер звука, высота)
    public Color FlashColor = Color.White;
    public float Time, Dt;
    public float Speed = 1;
    public bool Flip;
    public bool Battles = true;
    public bool Flat;
    public bool Original;       // оригинальная графика Battle Chess
    public Action<string> Sound = _ => { };
    public readonly Random Rnd = new();

    IEnumerator<int> script;
    public bool Busy => script != null;
    public event Action Finished;

    // Пересобирает актёров по позиции; сохраняет направление взгляда тех, кто остался на месте.
    public void Sync(Position pos, bool keepFacing = true)
    {
        var old = Actors;
        Actors = new List<Actor>();
        for (int s = 0; s < 64; s++)
        {
            if (pos.B[s] == 0) continue;
            int facing = pos.B[s] > 0 ? 1 : -1;
            if (keepFacing)
            {
                var prev = old.FirstOrDefault(a => !a.Dead && a.Piece == pos.B[s]
                    && (int)MathF.Floor(a.F) == P.File(s) && (int)MathF.Floor(a.R) == P.Rank(s));
                if (prev != null) facing = prev.Pose.Facing;
            }
            Actors.Add(new Actor { Piece = pos.B[s], F = P.File(s) + 0.5f, R = P.Rank(s) + 0.5f, Pose = { Facing = facing } });
        }
    }

    public Actor At(int sq) =>
        Actors.FirstOrDefault(a => !a.Dead && (int)MathF.Floor(a.F) == P.File(sq) && (int)MathF.Floor(a.R) == P.Rank(sq));

    public void Play(IEnumerable<int> s)
    {
        script = s.GetEnumerator();
    }

    public void Skip()
    {
        script = null;
        Shake = 0; Flash = 0; ZoomK = 0;
        FightA = FightD = null;
        Combat = null;
        Effects.Clear();
    }

    public void Update(float realDt)
    {
        Dt = Math.Min(realDt, 0.05f) * Speed;
        Time += Dt;
        if (script != null)
        {
            bool more;
            try { more = script.MoveNext(); }
            catch { more = false; }
            if (!more) { script = null; Finished?.Invoke(); }
        }
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i];
            p.Life += Dt;
            if (p.Life >= p.Max) { Particles.RemoveAt(i); continue; }
            p.F += p.VF * Dt; p.R += p.VR * Dt; p.H += p.VH * Dt;
            p.VH -= p.Grav * Dt;
            p.Rot += p.VRot * Dt;
            if (p.H < 0 && p.Grav > 0) { p.H = 0; p.VH = -p.VH * 0.35f; p.VF *= 0.6f; p.VR *= 0.6f; }
            if (p.Kind == 2) { p.VF *= 0.96f; p.VR *= 0.96f; }
        }
        for (int i = Effects.Count - 1; i >= 0; i--)
        {
            Effects[i].T += Dt;
            if (Effects[i].T >= Effects[i].Dur) Effects.RemoveAt(i);
        }
        Shake = Math.Max(0, Shake - Dt * 30);
        Flash = Math.Max(0, Flash - Dt * 2.2f);
        foreach (var a in Actors) if (!a.Pose.Moving) a.Pose.Walk = 0;
    }

    // ---------- частицы ----------
    public void Burst(float f, float r, float h, int count, Color c, int kind, float speed, float size, float life, float grav = 0)
    {
        for (int i = 0; i < count; i++)
        {
            float a = (float)(Rnd.NextDouble() * Math.PI * 2);
            float up = (float)(Rnd.NextDouble() * 2 - 0.4);
            float sp = speed * (0.4f + (float)Rnd.NextDouble() * 0.8f);
            Particles.Add(new Particle
            {
                F = f, R = r, H = h,
                VF = MathF.Cos(a) * sp, VR = MathF.Sin(a) * sp * 0.6f, VH = up * sp,
                Life = 0, Max = life * (0.6f + (float)Rnd.NextDouble() * 0.6f),
                Size = size * (0.6f + (float)Rnd.NextDouble() * 0.8f), C = c, Kind = kind, Grav = grav,
                Rot = (float)Rnd.NextDouble() * 360, VRot = (float)(Rnd.NextDouble() * 600 - 300),
            });
        }
    }

    // ---------- примитивы сценариев ----------
    float ViewX(float f) => Flip ? 8 - f : f;

    public IEnumerable<int> Wait(float sec)
    {
        for (float t = 0; t < sec; t += Dt) yield return 0;
    }

    public IEnumerable<int> All(params IEnumerable<int>[] parts)
    {
        var es = parts.Select(p => p.GetEnumerator()).ToList();
        while (es.Count > 0)
        {
            for (int i = es.Count - 1; i >= 0; i--) if (!es[i].MoveNext()) es.RemoveAt(i);
            if (es.Count > 0) yield return 0;
        }
    }

    public IEnumerable<int> Seq(params IEnumerable<int>[] parts)
    {
        foreach (var p in parts) foreach (var x in p) yield return x;
    }

    void Face(Actor a, float targetF, float targetR)
    {
        float dx = ViewX(targetF) - ViewX(a.F);
        if (Math.Abs(dx) > 0.05f) a.Pose.Facing = dx > 0 ? 1 : -1;
        int dir = OriginalArt.Compass(targetF - a.F, targetR - a.R);
        if (dir >= 0) a.Dir = dir;
    }

    public IEnumerable<int> WalkTo(Actor a, float f, float r, float speed = 1.5f)
    {
        Face(a, f, r);
        if (Flat) speed *= 3;
        if (Original) speed *= 0.8f;
        a.Pose.Moving = true;
        float stepPhase = 0;
        while (true)
        {
            float dx = f - a.F, dy = r - a.R, dist = MathF.Sqrt(dx * dx + dy * dy);
            float step = speed * Dt;
            if (dist <= step) { a.F = f; a.R = r; break; }
            a.F += dx / dist * step; a.R += dy / dist * step;
            a.Pose.Walk += Dt * 10;
            stepPhase += Dt * 10;
            if (stepPhase > MathF.PI) { stepPhase -= MathF.PI; if (!Flat) Sound(a.Type == P.Rook ? "stomp" : "step"); }
            yield return 0;
        }
        a.Pose.Moving = false;
    }

    public IEnumerable<int> JumpTo(Actor a, float f, float r)
    {
        Face(a, f, r);
        float f0 = a.F, r0 = a.R;
        float dur = Flat ? 0.3f : 0.9f;
        if (!Flat) Sound("gallop");
        a.Pose.Moving = true;
        for (float t = 0; t < dur; t += Dt)
        {
            float k = Painter.Ease(t / dur);
            a.F = f0 + (f - f0) * k; a.R = r0 + (r - r0) * k;
            float u = t / dur;
            a.Pose.Lift = Flat ? 0 : 4 * 0.9f * u * (1 - u);
            a.Pose.Walk += Dt * 9;
            yield return 0;
        }
        a.F = f; a.R = r; a.Pose.Lift = 0; a.Pose.Moving = false;
        if (!Flat) Sound("land");
        if (!Flat && !Original) Burst(f, r, 0.05f, 10, Color.FromArgb(150, 140, 120), 2, 0.6f, 0.18f, 0.6f);
    }

    public IEnumerable<int> AttackAnim(Actor a, float dur, Action onImpact)
    {
        bool hit = false;
        for (float t = 0; t < dur; t += Dt)
        {
            a.Pose.Attack = t / dur;
            if (!hit && a.Pose.Attack >= Painter.ImpactAt) { hit = true; onImpact?.Invoke(); }
            yield return 0;
        }
        if (!hit) onImpact?.Invoke();
        a.Pose.Attack = -1;
    }

    public IEnumerable<int> Block(Actor a, float dur)
    {
        for (float t = 0; t < dur; t += Dt)
        {
            float u = t / dur;
            a.Pose.Block = u < 0.2f ? u / 0.2f : u > 0.8f ? (1 - u) / 0.2f : 1;
            yield return 0;
        }
        a.Pose.Block = 0;
    }

    public IEnumerable<int> HurtAnim(Actor a, float dur = 0.4f)
    {
        for (float t = 0; t < dur; t += Dt) { a.Pose.Hurt = 1 - t / dur; yield return 0; }
        a.Pose.Hurt = 0;
    }

    public IEnumerable<int> ZoomTo(float target, float dur)
    {
        float from = ZoomK;
        for (float t = 0; t < dur; t += Dt) { ZoomK = from + (target - from) * Painter.Ease(t / dur); yield return 0; }
        ZoomK = target;
    }

    public IEnumerable<int> GlowUp(Actor a, float dur, float from, float to)
    {
        for (float t = 0; t < dur; t += Dt) { a.Pose.Glow = from + (to - from) * (t / dur); yield return 0; }
        a.Pose.Glow = to;
    }
}
