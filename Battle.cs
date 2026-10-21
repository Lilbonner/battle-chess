using System.Drawing;

namespace BattleChess;

// Сценарии ходов и сражений.
public partial class Scene
{
    public string Caption = "";

    public IEnumerable<int> MoveScript(Move m)
    {
        var a = At(m.From);
        if (a == null) yield break;
        float tf = P.File(m.To) + 0.5f, tr = P.Rank(m.To) + 0.5f;

        if (m.IsCastle)
        {
            int rookFrom = m.To > m.From ? m.To + 1 : m.To - 2, rookTo = m.To > m.From ? m.To - 1 : m.To + 1;
            var rook = At(rookFrom);
            Caption = m.To > m.From ? "Короткая рокировка" : "Длинная рокировка";
            if (UseOriginalMoves(a))
            {
                foreach (var x in OriginalMove(a, P.File(m.To), P.Rank(m.To))) yield return x;
                if (rook != null) foreach (var x in OriginalMove(rook, P.File(rookTo), P.Rank(rookTo))) yield return x;
            }
            else
                foreach (var x in All(WalkTo(a, tf, tr),
                             Seq(Wait(0.35f), rook != null ? WalkTo(rook, P.File(rookTo) + 0.5f, P.Rank(rookTo) + 0.5f) : Wait(0))))
                    yield return x;
        }
        else if (!m.IsCapture)
        {
            if (UseOriginalMoves(a)) foreach (var x in OriginalMove(a, P.File(m.To), P.Rank(m.To))) yield return x;
            else foreach (var x in Travel(a, tf, tr)) yield return x;
        }
        else
        {
            var d = At(m.CaptureSquare);
            if (d != null)
            {
                Caption = $"{P.RuNames[a.Type]} атакует: {P.RuNames[d.Type].ToLower()}";
                var fight = Battles && Original ? OriginalSceneFight(a, d) : QuickCapture(a, d);
                foreach (var x in fight) yield return x;
            }
            if (UseOriginalMoves(a))
            {
                foreach (var x in All(ZoomTo(0, 0.8f), OAfterCapture(a, m))) yield return x;
            }
            else foreach (var x in All(WalkTo(a, tf, tr, 1.8f), ZoomTo(0, 0.8f))) yield return x;
            FightA = FightD = null;
        }

        if (m.Promo != 0 && UseOriginalMoves(a))
        {
            // в оригинале превращение мгновенное (PUTPIECE)
            Sound("promote");
            a.Piece = m.Promo * Math.Sign(a.Piece); a.OFacing = -1; a.OSeq = -1; a.OTower = true;
        }
        else if (m.Promo != 0)
        {
            Caption = $"Пешка превращается: {P.RuNames[m.Promo].ToLower()}!";
            foreach (var x in Promote(a, m.Promo)) yield return x;
        }
        foreach (var x in Wait(0.1f)) yield return x;
        Caption = "";
    }

    // Оригинал после боя: победитель уже вернулся в центр клетки взятия; оттуда — поворот в «домашнюю» стойку
    // (ладья — обратно в башню), а при взятии на проходе — ход с клетки взятой пешки на поле назначения.
    IEnumerable<int> OAfterCapture(Actor a, Move m)
    {
        float cf = P.File(m.CaptureSquare) + 0.5f, cr = P.Rank(m.CaptureSquare) + 0.5f;
        if (a.F != cf || a.R != cr || a.OShift != PointF.Empty) { a.F = cf; a.R = cr; a.OShift = PointF.Empty; a.OSeq = -1; }
        if (m.IsEnPassant) { foreach (var x in OriginalMove(a, P.File(m.To), P.Rank(m.To))) yield return x; }
        else if (a.Type == P.Rook) { a.OTower = false; if (a.OFacing < 0) a.OFacing = 0; foreach (var x in OToTower(a)) yield return x; }
        else foreach (var x in OTurn(a, HomeFacing(a))) yield return x;
        a.F = P.File(m.To) + 0.5f; a.R = P.Rank(m.To) + 0.5f;
    }

    IEnumerable<int> Travel(Actor a, float f, float r) =>
        a.Type == P.Knight ? JumpTo(a, f, r) : WalkTo(a, f, r);

    IEnumerable<int> QuickCapture(Actor a, Actor d)
    {
        float dx = d.F - a.F, dy = d.R - a.R, len = MathF.Max(0.01f, MathF.Sqrt(dx * dx + dy * dy));
        float stop = Math.Max(0, len - 0.5f);
        foreach (var x in Travel(a, a.F + dx / len * stop, a.R + dy / len * stop)) yield return x;
        Sound("poof");
        d.Pose.DeathKind = 2;
        Burst(d.F, d.R, 0.5f, 18, Color.FromArgb(220, 220, 255), 0, 1.2f, 0.12f, 0.6f);
        for (float t = 0; t < 0.35f; t += Dt) { d.Pose.Death = t / 0.35f; yield return 0; }
        d.Dead = true;
    }

    // Точная сцена поединка из оригинальной Battle Chess: победитель подходит к клетке,
    // затем по тактам (10 в секунду) проигрываются кадры обеих фигур и звуки, как в движке игры.
    IEnumerable<int> OriginalSceneFight(Actor a, Actor d)
    {
        var variants = LoadCombat?.Invoke(d.Type, a.Type);
        if (variants == null || variants.Count == 0)
        {
            foreach (var x in OriginalFight(a, d)) yield return x;
            yield break;
        }
        int vi = ForcedTechnique >= 0 ? ForcedTechnique % variants.Count : Rnd.Next(variants.Count);
        var variant = variants[vi];
        Caption = variants.Count == 1 || vi == 0
            ? $"{P.RuNames[a.Type]} берёт: {P.RuNames[d.Type].ToLower()} — сцена оригинала"
            : $"{P.RuNames[a.Type]} берёт: {P.RuNames[d.Type].ToLower()} — вариант {vi + 1} из {variants.Count}";
        FocusF = d.F; FocusR = d.R; FightA = a; FightD = d;
        if (UseOriginalMoves(a))
        {
            // подход как в DOWALK оригинала: обычная ходьба до соседней клетки, затем жертва отходит на позицию боя,
            // а победитель (идущий на S/SW/W — останавливаясь перед клеткой) выходит на свою позицию лицом к ней
            int toF = (int)MathF.Floor(d.F), toR = (int)MathF.Floor(d.R);
            foreach (var x in All(OriginalMove(a, toF, toR, goHome: false, lastStep: dir => OCaptureStep(a, d, dir)),
                                  Seq(Wait(0.4f), ZoomTo(1, 1.2f)))) yield return x;
        }
        else
            foreach (var x in All(Travel(a, d.F, d.R), Seq(Wait(0.2f), ZoomTo(1, 0.9f)))) yield return x;
        Combat = new CombatView { V = variant, F = d.F, R = d.R, Victim = d, Killer = a };
        // звуки: в точной сцене — все; в сочетании — победителя из атаки и побеждённого из гибели (после удара)
        var sounds = new List<(int tick, int sfx, int pitch)>();
        if (variant.Exact) sounds.AddRange(variant.Attack.Sounds.Select(s => (s.tick, s.sfx, s.pitch)));
        else
        {
            sounds.AddRange(variant.Attack.Sounds.Where(s => s.side == 1).Select(s => (s.tick, s.sfx, s.pitch)));
            int ia = Math.Max(1, variant.Attack.Impact), ip = Math.Max(1, variant.Pre.Impact);
            sounds.AddRange(variant.Pre.Sounds.Where(s => s.side == 0 && s.tick < ip).Select(s => (s.tick * ia / ip, s.sfx, s.pitch)));
            int shift = variant.Attack.Impact - variant.Death.Impact;
            sounds.AddRange(variant.Death.Sounds.Where(s => s.side == 0 && s.tick >= variant.Death.Impact).Select(s => (s.tick + shift, s.sfx, s.pitch)));
        }
        int lastTick = -1, length = Combat.Length;
        float t = 0;
        while (t < length)
        {
            int ti = (int)t;
            foreach (var s in sounds)
                if (s.tick > lastTick && s.tick <= ti) CombatSound(s.sfx, s.pitch);
            lastTick = ti;
            Combat.Tick = t;
            yield return 0;
            t += Dt * 10;
        }
        Combat = null;
        d.Dead = true; d.Pose.Death = 1; d.OShift = PointF.Empty; d.OSeq = -1;
        a.Dir = a.IsWhite ? 0 : 4;
        if (UseOriginalMoves(a))
            foreach (var x in OFromCombat(a, MathF.Floor(d.F) + 0.5f, MathF.Floor(d.R) + 0.5f)) yield return x;
    }

    // Поединок для оригинальной графики: спрайты Battle Chess сходятся, обмениваются выпадами,
    // проигравший мигает и исчезает. Приём и звук зависят от типа атакующей фигуры.
    IEnumerable<int> OriginalFight(Actor a, Actor d)
    {
        float dx = d.F - a.F, dy = d.R - a.R, len = MathF.Max(0.01f, MathF.Sqrt(dx * dx + dy * dy));
        float gap = a.Type == P.Queen ? 1.4f : 0.8f;
        float stop = Math.Max(0, len - gap);
        FocusF = d.F - dx / len * gap / 2; FocusR = d.R - dy / len * gap / 2;
        FightA = a; FightD = d;
        foreach (var x in All(Travel(a, a.F + dx / len * stop, a.R + dy / len * stop), Seq(Wait(0.2f), ZoomTo(1, 0.9f)))) yield return x;
        a.Dir = OriginalArt.Compass(dx, dy);
        d.Dir = OriginalArt.Compass(-dx, -dy);
        foreach (var x in Wait(0.35f)) yield return x;

        if (d.Type != P.King && Rnd.NextDouble() < 0.6)
        {
            foreach (var x in Lunge(d, a, 0.55f, () =>
            {
                Sound("clang");
                Burst((a.F + d.F) / 2, (a.R + d.R) / 2, 0.8f, 14, Color.FromArgb(255, 230, 140), 1, 2.2f, 0.06f, 0.35f, 4);
            })) yield return x;
            foreach (var x in Wait(0.2f)) yield return x;
        }

        Action impact = a.Type switch
        {
            P.Pawn => () => { Sound("stab"); Hit(d, Color.FromArgb(255, 240, 200)); },
            P.Knight => () => { Sound("hit"); Hit(d, Color.FromArgb(255, 230, 160)); Shake = 6; },
            P.King => () => { Sound("slash"); Hit(d, Color.White); Effects.Add(new Effect { Kind = 2, Dur = 0.3f, F1 = d.F, R1 = d.R, H1 = 0.7f, C = Color.White, Seed = 1 }); },
            P.Bishop => () =>
            {
                Sound("thunder"); Hit(d, Color.FromArgb(200, 220, 255));
                Effects.Add(new Effect { Kind = 0, Dur = 0.45f, F1 = d.F, R1 = d.R, H1 = 0.2f, C = Color.FromArgb(220, 230, 255), Seed = Rnd.Next() });
                Flash = 0.7f; FlashColor = Color.FromArgb(230, 235, 255);
            },
            P.Rook => () =>
            {
                Sound("smash"); Shake = 16; d.Pose.Hurt = 1;
                Effects.Add(new Effect { Kind = 3, Dur = 0.6f, F1 = d.F, R1 = d.R, C = Color.FromArgb(200, 180, 150) });
                Burst(d.F, d.R, 0.3f, 16, Color.FromArgb(130, 120, 110), 3, 2.0f, 0.1f, 1.2f, 6);
            },
            _ => () =>
            {
                Sound("zap"); Hit(d, Color.FromArgb(120, 200, 255));
                Effects.Add(new Effect { Kind = 1, Dur = 0.35f, F1 = a.F, R1 = a.R, H1 = 0.9f, F2 = d.F, R2 = d.R, H2 = 0.6f, C = Color.FromArgb(120, 200, 255) });
                Flash = 0.5f; FlashColor = Color.FromArgb(120, 200, 255);
            },
        };
        if (a.Type == P.Queen) Sound("charge");
        foreach (var x in Lunge(a, d, a.Type == P.Rook ? 0.8f : 0.6f, impact)) yield return x;
        foreach (var x in Wait(0.15f)) yield return x;

        // гибель: мигание и исчезновение
        Sound("die");
        d.Pose.DeathKind = 2;
        for (float t = 0; t < 1.3f; t += Dt)
        {
            float u = t / 1.3f;
            d.Pose.Death = u;
            d.Pose.Hurt = ((int)(t * 14) % 2 == 0) ? 1 : 0;
            if (Rnd.NextDouble() < 0.4)
                Burst(d.F + (float)(Rnd.NextDouble() - 0.5) * 0.4f, d.R, (float)Rnd.NextDouble() * 1.2f, 1,
                      Color.FromArgb(230, 230, 230), 4, 0.4f, 0.1f, 0.7f, -1.2f);
            yield return 0;
        }
        d.Dead = true;
        foreach (var x in Wait(0.15f)) yield return x;
    }

    // Выпад к цели и обратно; onImpact — в момент наибольшего сближения.
    IEnumerable<int> Lunge(Actor a, Actor target, float dur, Action onImpact)
    {
        float f0 = a.F, r0 = a.R;
        float dx = target.F - a.F, dy = target.R - a.R, len = MathF.Max(0.01f, MathF.Sqrt(dx * dx + dy * dy));
        float reach = Math.Min(0.35f, len * 0.4f);
        bool hit = false;
        a.Pose.Moving = true;
        for (float t = 0; t < dur; t += Dt)
        {
            float u = t / dur;
            float k = u < 0.5f ? Painter.Ease(u * 2) : 1 - Painter.Ease((u - 0.5f) * 2);
            a.F = f0 + dx / len * reach * k; a.R = r0 + dy / len * reach * k;
            a.Pose.Walk += Dt * 10;
            if (!hit && u >= 0.5f) { hit = true; onImpact?.Invoke(); }
            yield return 0;
        }
        a.F = f0; a.R = r0; a.Pose.Moving = false;
        if (!hit) onImpact?.Invoke();
    }

    void Hit(Actor d, Color c)
    {
        Burst(d.F, d.R, 0.65f, 18, c, 1, 2.6f, 0.07f, 0.4f, 5);
        Burst(d.F, d.R, 0.75f, 6, Color.FromArgb(255, 255, 230), 4, 0.8f, 0.12f, 0.5f);
        d.Pose.Hurt = 1;
    }

    IEnumerable<int> Die(Actor d, int kind, float side, float hitH)
    {
        d.Pose.DeathKind = kind;
        float dir = Flip ? -1 : 1;
        float f0 = d.F, r0 = d.R;
        float dur = kind switch { 1 => 1.0f, 2 => 1.1f, 3 => 1.2f, 4 => 1.4f, 5 => 1.4f, 6 => 1.4f, 7 => 1.5f, 8 => 1.6f, 9 => 1.7f, _ => 1.3f };
        Sound(kind switch { 1 => "crunch", 2 => "shatter", 4 => "burn", 5 => "ice", 6 => "whoosh", 7 => "rumble", 8 => "stone", 9 => "boing", _ => "die" });
        bool dust = false, broke = false;
        for (float t = 0; t < dur; t += Dt)
        {
            float u = t / dur;
            d.Pose.Death = u;
            d.Pose.Hurt = Math.Max(0, d.Pose.Hurt - Dt * 3);
            if (kind == 3)
            {
                d.F = f0 + dir * side * u * 2.2f;
                d.Pose.Lift = 4 * 1.4f * u * (1 - u) + u * 0.3f;
            }
            if (kind == 2 && Rnd.NextDouble() < 0.6)
                Burst(d.F + (float)(Rnd.NextDouble() - 0.5) * 0.4f, d.R, (float)Rnd.NextDouble() * 1.1f, 1,
                      Color.FromArgb(220, 235, 255), 0, 0.5f, 0.1f, 0.8f, -1.5f);
            if (kind == 4 && Rnd.NextDouble() < 0.5)
                Burst(d.F + (float)(Rnd.NextDouble() - 0.5) * 0.3f, d.R, 0.2f + (float)Rnd.NextDouble() * 0.8f, 1,
                      Color.FromArgb(90, 80, 75), 2, 0.25f, 0.12f, 1.0f, -1.2f);
            if (kind == 5)
            {
                d.Pose.Freeze = Math.Min(1, u * 3);
                if (u < 0.55f && Rnd.NextDouble() < 0.4)
                    Burst(d.F + (float)(Rnd.NextDouble() - 0.5) * 0.4f, d.R, (float)Rnd.NextDouble() * 1.1f, 1, Color.FromArgb(220, 240, 255), 4, 0.2f, 0.1f, 0.5f);
                if (!broke && u >= 0.55f)
                {
                    broke = true; Sound("shatter"); Shake = 5;
                    Burst(d.F, d.R, 0.6f, 34, Color.FromArgb(190, 225, 255), 3, 2.2f, 0.09f, 1.2f, 6);
                    Burst(d.F, d.R, 0.6f, 12, Color.White, 4, 1.2f, 0.12f, 0.6f);
                }
            }
            if (kind == 6)
            {
                d.Pose.Lift = u * u * 7;
                if (Rnd.NextDouble() < 0.5) Burst(d.F, d.R, d.Pose.Lift + 0.4f, 1, Color.FromArgb(255, 250, 200), 4, 0.3f, 0.1f, 0.5f);
            }
            if (kind == 7)
            {
                d.Pose.Lift = -Painter.Ease(u) * 1.4f;
                if (Rnd.NextDouble() < 0.6) Burst(d.F + (float)(Rnd.NextDouble() - 0.5) * 0.5f, d.R, 0.05f, 1, Color.FromArgb(150, 135, 110), 2, 0.6f, 0.22f, 0.9f);
                Shake = Math.Max(Shake, 3 * (1 - u));
            }
            if (kind == 8)
            {
                d.Pose.Stone = Math.Min(1, u * 2.2f);
                if (!broke && u >= 0.68f)
                {
                    broke = true; Sound("crunch"); Shake = 6;
                    Burst(d.F, d.R, 0.5f, 28, Color.FromArgb(125, 122, 115), 3, 1.6f, 0.11f, 1.3f, 6);
                    Burst(d.F, d.R, 0.1f, 14, Color.FromArgb(160, 155, 145), 2, 0.8f, 0.25f, 1.0f);
                }
            }
            if (kind == 9)
            {
                if (u < 0.5f && Rnd.NextDouble() < 0.5)
                {
                    float ang = t * 9;
                    Burst(d.F + MathF.Cos(ang) * 0.25f, d.R + MathF.Sin(ang) * 0.1f, 1.25f, 1, Color.FromArgb(255, 240, 120), 4, 0.05f, 0.12f, 0.4f);
                }
                if (!dust && u > 0.75f)
                {
                    dust = true; Sound("thud");
                    Burst(d.F, d.R, 0.05f, 12, Color.FromArgb(160, 150, 130), 2, 0.7f, 0.2f, 0.8f);
                }
            }
            if (kind == 0 && !dust && u > 0.55f)
            {
                dust = true; Sound("thud");
                Burst(d.F - dir * side * -0.5f, d.R, 0.05f, 12, Color.FromArgb(160, 150, 130), 2, 0.7f, 0.2f, 0.8f);
            }
            yield return 0;
        }
        d.Pose.Lift = 0;
        _ = hitH;
    }

    IEnumerable<int> Promote(Actor a, int promo)
    {
        Sound("promote");
        if (!Flat) Effects.Add(new Effect { Kind = 4, Dur = 0.9f, F1 = a.F, R1 = a.R, C = Color.FromArgb(255, 240, 160) });
        for (float t = 0; t < 0.45f; t += Dt)
        {
            a.Pose.Glow = t / 0.45f;
            if (Rnd.NextDouble() < 0.7) Burst(a.F, a.R, (float)Rnd.NextDouble(), 1, Color.FromArgb(255, 240, 160), 4, 0.6f, 0.12f, 0.6f, -0.8f);
            yield return 0;
        }
        a.Piece = promo * Math.Sign(a.Piece);
        Flash = 0.5f; FlashColor = Color.FromArgb(255, 245, 200);
        Burst(a.F, a.R, 0.6f, 30, Color.FromArgb(255, 240, 160), 4, 1.6f, 0.12f, 0.8f);
        for (float t = 0; t < 0.45f; t += Dt) { a.Pose.Glow = 1 - t / 0.45f; yield return 0; }
        a.Pose.Glow = 0;
    }
}
