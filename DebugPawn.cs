using System.Drawing;
using System.Drawing.Imaging;

namespace BattleChess;

// Отладка анимаций пешки в оригинальной графике: BattleChess.exe --pawn <папка> [фильтр]
// Для каждого сценария ход проигрывается такт за тактом (10 тактов/с), кадры собираются в лист.
public partial class GameForm
{
    record PawnCase(string Name, string Setup, string Uci, int Side);

    public void RenderPawnDebug(string dir, string filter)
    {
        Directory.CreateDirectory(dir);
        timer.Stop(); snd.Enabled = false;
        ClientSize = new Size(1280, 800); CreateControl();
        canvas.Size = new Size(1280, 800 - menu.Height);
        SetOriginal(true); titleT = 0;
        NewGame(true, true);
        bannerT = 0;
        var cases = new List<PawnCase>
        {
            new("w_e2e4", "Ke1 Kh8 Pe2", "e2e4", 1),
            new("b_d7d5", "Ke1 Kh8 pd7", "d7d5", -1),
            new("w_e4xd5_PP", "Ke1 Kh8 Pe4 pd5", "e4d5", 1),
            new("w_e4xf5_PP", "Ke1 Kh8 Pe4 pf5", "e4f5", 1),
            new("b_d5xe4_PP", "Ke1 Kh8 pd5 Pe4", "d5e4", -1),
            new("b_d5xc4_PP", "Ke1 Kh8 pd5 Pc4", "d5c4", -1),
            new("w_ep_e5xd6", "Ke1 Kh8 Pe5 pd5", "e5d6", 1),
            new("w_ep_e5xf6", "Ke1 Kh8 Pe5 pf5", "e5f6", 1),
            new("w_promo_e7e8q", "Ka1 Kh6 Pe7", "e7e8q", 1),
            new("w_d4xe5_PN", "Ke1 Kh8 Pd4 ne5", "d4e5", 1),
            new("w_Nc3xd5_NP", "Ke1 Kh8 Nc3 pd5", "c3d5", 1),
            new("w_Bc4xf7_BP", "Ke1 Kh8 Bc4 pf7", "c4f7", 1),
            new("w_Qd1xd5_QP", "Ke1 Kh8 Qd1 pd5", "d1d5", 1),
            new("w_Ra1xa6_RP", "Ke1 Kh8 Ra1 pa6", "a1a6", 1),
            new("b_Qd8xd4_QP", "Ke1 Kh8 qd8 Pd4", "d8d4", -1),
        };
        foreach (var c in cases)
        {
            if (!string.IsNullOrEmpty(filter) && !c.Name.Contains(filter)) continue;
            var p = new Position();
            foreach (var tok in c.Setup.Split(' '))
            {
                int type = "PNBRQK".IndexOf(char.ToUpper(tok[0])) + 1;
                int sq = (tok[1] - 'a') + (tok[2] - '1') * 8;
                p.B[sq] = char.IsUpper(tok[0]) ? type : -type;
            }
            p.Side = c.Side;
            if (c.Name.Contains("_ep_")) p.Ep = (c.Uci[2] - 'a') + (c.Uci[3] - '1') * 8;
            p.Hash = p.ComputeHash(); p.HashHistory.Add(p.Hash);
            pos = p; history.Clear(); sanList.Clear(); state = GameState.Playing; lastMove = null;
            scene.Skip(); scene.Particles.Clear(); scene.Sync(pos, false);
            var m = pos.Legal().First(x => x.ToString() == c.Uci);
            scene.ForcedTechnique = 0;
            var mover = scene.Actors.First(a => !a.Dead && P.Sq((int)a.F, (int)a.R) == m.From);
            var victim = m.IsCapture ? scene.Actors.First(a => !a.Dead && P.Sq((int)a.F, (int)a.R) == m.CaptureSquare) : null;
            float cf = (P.File(m.From) + P.File(m.To)) / 2f + 0.5f, cr = (P.Rank(m.From) + P.Rank(m.To)) / 2f + 0.5f;
            if (m.IsCapture && Math.Abs(m.Moved) != P.Pawn) { cf = P.File(m.CaptureSquare) + 0.5f; cr = P.Rank(m.CaptureSquare) + 0.5f; }
            DoMove(m);
            var frames = new List<Bitmap>();
            var log = new List<string>();
            int tick = 0;
            float t = 0;
            while (scene.Busy && t < 60)
            {
                for (int k = 0; k < 6; k++) { scene.Update(1 / 60f); t += 1 / 60f; }
                string info = $"{tick} s{mover.OSeq}:{mover.OFrame} f{mover.OFacing} sh{mover.OShift.X:0},{mover.OShift.Y:0}"
                    + (victim != null ? $" | v s{victim.OSeq}:{victim.OFrame} f{victim.OFacing} sh{victim.OShift.X:0},{victim.OShift.Y:0}{(victim.Dead ? " dead" : "")}" : "")
                    + (scene.Combat != null ? $" | C{(int)scene.Combat.Tick}" : "");
                scene.ZoomK = 0; bannerT = 0;
                log.Add(info +$" F{mover.F:0.00} R{mover.R:0.00}");
                frames.Add(GrabAround(cf, cr, tick.ToString() + (scene.Combat != null ? " C" : "")));
                tick++;
            }
            File.WriteAllLines(Path.Combine(dir, c.Name + ".txt"), log);
            SavePawnSheet(Path.Combine(dir, c.Name + ".png"), frames, 12);
        }
        scene.ForcedTechnique = -1;
    }

    Bitmap GrabAround(float f, float r, string label)
    {
        using var full = new Bitmap(canvas.Width, canvas.Height);
        canvas.DrawToBitmap(full, new Rectangle(0, 0, canvas.Width, canvas.Height));
        var c = cam.Project(f, r);
        int w = 240, h = 230;
        float s = cam.OScale / 2;   // 2 пикселя листа на пиксель экрана оригинала
        var src = new RectangleF(c.X - w / 2 * s, c.Y - (h - 50) * s, w * s, h * s);
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImage(full, new RectangleF(0, 0, w, h), src, GraphicsUnit.Pixel);
        using var font = new Font("Consolas", 9, FontStyle.Bold);
        g.DrawString(label, font, Brushes.Yellow, 2, 2);
        return bmp;
    }

    static void SavePawnSheet(string path, List<Bitmap> frames, int cols)
    {
        if (frames.Count == 0) return;
        int rows = (frames.Count + cols - 1) / cols, w = frames[0].Width, h = frames[0].Height;
        using var sheet = new Bitmap(w * cols, h * rows);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.Black);
            for (int i = 0; i < frames.Count; i++) { g.DrawImage(frames[i], (i % cols) * w, (i / cols) * h); frames[i].Dispose(); }
        }
        sheet.Save(path, ImageFormat.Png);
    }
}
