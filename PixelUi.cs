using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace BattleChess;

// Пиксельный интерфейс в стиле Macintosh System 6, как у оригинальной Battle Chess:
// всё рисуется в 1 бит (чёрный/белый) в маленький буфер и увеличивается «квадратными» пикселями.
static class MacUi
{
    public const int Px = 2;   // экранных пикселей на пиксель интерфейса

    // Chicago (системный шрифт Mac) не знает кириллицы — ближайший по виду жирный шрифт в 1-битной отрисовке.
    public static readonly Font Chicago = new("Tahoma", 11f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Geneva = new("Tahoma", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Pieces = new("Segoe UI Symbol", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
    static readonly StringFormat Fmt = new(StringFormat.GenericTypographic)
    { FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap };

    public static Graphics Begin(Bitmap b, bool black)
    {
        var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.Clear(black ? Color.Black : Color.White);
        return g;
    }

    // Буфер интерфейса → экран, каждый пиксель — квадрат Px×Px.
    public static void Blit(Graphics g, Bitmap b, float x, float y, int px = Px)
    {
        var st = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;
        g.DrawImage(b, new RectangleF(MathF.Round(x), MathF.Round(y), b.Width * px, b.Height * px), new RectangleF(0, 0, b.Width, b.Height), GraphicsUnit.Pixel);
        g.Restore(st);
    }

    public static int TextW(Graphics g, string s, Font f) => (int)MathF.Ceiling(g.MeasureString(s, f, PointF.Empty, Fmt).Width);

    public static void Text(Graphics g, string s, Font f, int x, int y, bool white = false) =>
        g.DrawString(s, f, white ? Brushes.White : Brushes.Black, x, y, Fmt);

    public static void TextC(Graphics g, string s, Font f, int cx, int y, bool white = false) =>
        Text(g, s, f, cx - TextW(g, s, f) / 2, y, white);

    // Обрезать строку по ширине с многоточием.
    public static string Fit(Graphics g, string s, Font f, int w)
    {
        if (TextW(g, s, f) <= w) return s;
        while (s.Length > 1 && TextW(g, s + "…", f) > w) s = s[..^1];
        return s + "…";
    }

    public static void Fill(Graphics g, int x, int y, int w, int h, bool black) =>
        g.FillRectangle(black ? Brushes.Black : Brushes.White, x, y, w, h);

    public static void Frame(Graphics g, int x, int y, int w, int h, bool black = true)
    {
        var b = black ? Brushes.Black : Brushes.White;
        g.FillRectangle(b, x, y, w, 1); g.FillRectangle(b, x, y + h - 1, w, 1);
        g.FillRectangle(b, x, y, 1, h); g.FillRectangle(b, x + w - 1, y, 1, h);
    }

    // Окно Mac: белое поле, двойная рамка (белая кромка + чёрная линия), заголовок в полоску.
    // Возвращает верхнюю координату рабочей области.
    public static int Window(Graphics g, Rectangle r, string title)
    {
        Fill(g, r.X, r.Y, r.Width, r.Height, false);
        Frame(g, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
        if (title == null) return r.Y + 3;
        int top = r.Y + 2, bar = 15;
        for (int yy = top + 3; yy <= top + bar - 4; yy += 2)
            g.FillRectangle(Brushes.Black, r.X + 3, yy, r.Width - 6, 1);
        int tw = TextW(g, title, Chicago);
        Fill(g, r.X + r.Width / 2 - tw / 2 - 5, top + 1, tw + 10, bar - 2, false);
        TextC(g, title, Chicago, r.X + r.Width / 2, top + 1);
        g.FillRectangle(Brushes.Black, r.X + 2, top + bar - 1, r.Width - 4, 1);
        return top + bar + 1;
    }

    // Кнопка Mac: скруглённый прямоугольник в 1 пиксель; нажатая/под мышью — инверсия.
    public static void Button(Graphics g, Rectangle r, string label, bool inverted)
    {
        int x = r.X, y = r.Y, w = r.Width, h = r.Height;
        var fg = inverted ? Brushes.White : Brushes.Black;
        // заливка внутри скругления
        var fill = inverted ? Brushes.Black : Brushes.White;
        g.FillRectangle(fill, x + 3, y + 1, w - 6, h - 2);
        g.FillRectangle(fill, x + 1, y + 3, w - 2, h - 6);
        g.FillRectangle(fill, x + 2, y + 2, w - 4, h - 4);
        // контур
        g.FillRectangle(Brushes.Black, x + 3, y, w - 6, 1);
        g.FillRectangle(Brushes.Black, x + 3, y + h - 1, w - 6, 1);
        g.FillRectangle(Brushes.Black, x, y + 3, 1, h - 6);
        g.FillRectangle(Brushes.Black, x + w - 1, y + 3, 1, h - 6);
        foreach (var (cx, cy) in new[] { (x + 1, y + 2), (x + 2, y + 1), (x + w - 2, y + 2), (x + w - 3, y + 1),
                                         (x + 1, y + h - 3), (x + 2, y + h - 2), (x + w - 2, y + h - 3), (x + w - 3, y + h - 2) })
            g.FillRectangle(Brushes.Black, cx, cy, 1, 1);
        g.DrawString(label, Chicago, fg, x + w / 2 - TextW(g, label, Chicago) / 2, y + (h - 12) / 2, Fmt);
    }

    // Шахматный символ 1 бит: белые — контурные, чёрные — залитые.
    public static void PieceGlyph(Graphics g, int piece, int x, int y)
    {
        const string white = " ♙♘♗♖♕♔", black = " ♟♞♝♜♛♚";
        string s = (piece > 0 ? white : black)[Math.Abs(piece)].ToString();
        g.DrawString(s, Pieces, Brushes.Black, x, y, Fmt);
    }

    // Инверсия области (выделение, как в Mac).
    public static void Invert(Bitmap b, Rectangle r)
    {
        r.Intersect(new Rectangle(0, 0, b.Width, b.Height));
        if (r.Width <= 0 || r.Height <= 0) return;
        var d = b.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var buf = new int[r.Width];
        for (int yy = 0; yy < r.Height; yy++)
        {
            var row = d.Scan0 + yy * d.Stride;
            System.Runtime.InteropServices.Marshal.Copy(row, buf, 0, r.Width);
            for (int xx = 0; xx < r.Width; xx++) buf[xx] ^= 0x00FFFFFF;
            System.Runtime.InteropServices.Marshal.Copy(buf, 0, row, r.Width);
        }
        b.UnlockBits(d);
    }
}

public partial class GameForm
{
    Bitmap pixelPanel, pixelOverlay;

    Bitmap Buffer(ref Bitmap b, int w, int h)
    {
        w = Math.Max(1, w); h = Math.Max(1, h);
        if (b == null || b.Width != w || b.Height != h) { b?.Dispose(); b = new Bitmap(w, h, PixelFormat.Format32bppArgb); }
        return b;
    }

    // Боковая панель в режиме оригинала: логотип заставки, окна «Партия», «Взятые», «Ходы», кнопки Mac.
    void DrawPixelPanel(Graphics screen)
    {
        var pa = PanelArea;
        int px = MacUi.Px, W = (int)pa.Width / px, H = (int)MathF.Ceiling(pa.Height / px);
        var bmp = Buffer(ref pixelPanel, W, H);
        using var g = MacUi.Begin(bmp, true);
        var art = cam.Original;
        var mouseL = new Point((int)((mouse.X - pa.X) / px), (int)((mouse.Y - pa.Y) / px));
        buttons.Clear();
        int x0 = 3, ww = W - 5, y = 3;

        // логотип, как на заставке оригинала
        if (art?.LogoBattle != null && art.LogoChess != null)
        {
            int lh = art.LogoBattle.Height + art.LogoChess.Height + 9;
            MacUi.Window(g, new Rectangle(x0, y, ww, lh), null);
            g.DrawImageUnscaled(art.LogoBattle, x0 + (ww - art.LogoBattle.Width) / 2, y + 4);
            g.DrawImageUnscaled(art.LogoChess, x0 + (ww - art.LogoChess.Width) / 2 + 1, y + 5 + art.LogoBattle.Height);
            y += lh + 4;
        }
        else
        {
            int cy = MacUi.Window(g, new Rectangle(x0, y, ww, 22), null);
            MacUi.TextC(g, "БОЕВЫЕ ШАХМАТЫ", MacUi.Chicago, x0 + ww / 2, cy + 2);
            y += 26;
        }

        // партия
        {
            var r = new Rectangle(x0, y, ww, 72);
            int cy = MacUi.Window(g, r, "Партия");
            string mode = humanWhite && humanBlack ? "Два игрока"
                : !humanWhite && !humanBlack ? "Компьютер — компьютер"
                : (humanWhite ? "Вы — белые" : "Вы — чёрные");
            MacUi.Text(g, MacUi.Fit(g, mode, MacUi.Geneva, ww - 10), MacUi.Geneva, x0 + 5, cy + 1);
            if (!(humanWhite && humanBlack))
                MacUi.Text(g, MacUi.Fit(g, "Уровень: " + Ai.LevelNames[level], MacUi.Geneva, ww - 10), MacUi.Geneva, x0 + 5, cy + 12);
            int sy = cy + 26;
            if (state != GameState.Playing && !scene.Busy)
                MacUi.Text(g, MacUi.Fit(g, StateText(), MacUi.Chicago, ww - 10), MacUi.Chicago, x0 + 5, sy + 3);
            else
            {
                int side = scene.Busy ? -pos.Side : pos.Side;
                var dot = new Rectangle(x0 + 5, sy + 1, 10, 10);
                g.FillEllipse(Brushes.Black, dot);
                if (side > 0) g.FillEllipse(Brushes.White, new Rectangle(dot.X + 1, dot.Y + 1, dot.Width - 2, dot.Height - 2));
                MacUi.Text(g, side > 0 ? "Ход белых" : "Ход чёрных", MacUi.Chicago, x0 + 21, sy - 1);
                string sub = thinking ? "Компьютер думает" + new string('.', 1 + (int)(scene.Time * 3) % 3)
                    : scene.Busy ? "Идёт сражение..." : Spinning ? "Поворот доски..." : HumanToMove ? "Ваш ход" : "";
                bool check = !scene.Busy && pos.InCheck(pos.Side) && state == GameState.Playing;
                if (check) sub = "Шах!";
                if (sub != "")
                {
                    int tw = MacUi.TextW(g, sub, MacUi.Geneva);
                    MacUi.Text(g, sub, MacUi.Geneva, x0 + 21, sy + 13);
                    if (check) MacUi.Invert(bmp, new Rectangle(x0 + 19, sy + 12, tw + 4, 14));
                }
            }
            y += r.Height + 4;
        }

        // взятые фигуры
        {
            var takenByWhite = history.Where(m => m.IsCapture && m.Captured < 0).Select(m => m.Captured).OrderBy(t => t).ToList();
            var takenByBlack = history.Where(m => m.IsCapture && m.Captured > 0).Select(m => m.Captured).OrderByDescending(t => t).ToList();
            int[] val = { 0, 1, 3, 3, 5, 9, 0 };
            int diff = takenByWhite.Sum(t => val[-t]) - takenByBlack.Sum(t => val[t]);
            var r = new Rectangle(x0, y, ww, 58);
            int cy = MacUi.Window(g, r, "Взятые");
            foreach (var (label, list, adv, row) in new[] { ("Б:", takenByWhite, diff > 0 ? $"+{diff}" : "", 0), ("Ч:", takenByBlack, diff < 0 ? $"+{-diff}" : "", 1) })
            {
                int ry = cy + 2 + row * 19;
                MacUi.Text(g, label, MacUi.Geneva, x0 + 5, ry + 1);
                int advW = adv == "" ? 0 : MacUi.TextW(g, adv, MacUi.Geneva) + 4;
                int room = ww - 26 - advW, step = list.Count > 0 ? Math.Min(10, Math.Max(5, room / Math.Max(1, list.Count))) : 10;
                for (int i = 0; i < list.Count; i++) MacUi.PieceGlyph(g, list[i], x0 + 20 + i * step, ry - 3);
                if (adv != "") MacUi.Text(g, adv, MacUi.Geneva, x0 + ww - advW - 2, ry + 1);
            }
            y += r.Height + 4;
        }

        // кнопки и подсказка внизу
        int footY = H - 15, bh = 19, gap = 4, bw = (ww - gap) / 2;
        int by2 = footY - 5 - bh, by1 = by2 - gap - bh;

        // список ходов
        {
            var r = new Rectangle(x0, y, ww, Math.Max(40, by1 - 5 - y));
            int cy = MacUi.Window(g, r, "Ходы");
            int rowH = 13, rows = Math.Max(1, (r.Bottom - 4 - cy) / rowH);
            int total = (sanList.Count + 1) / 2, first = Math.Max(0, total - rows);
            if (total == 0) MacUi.Text(g, "Партия ещё не началась", MacUi.Geneva, x0 + 5, cy + 2);
            for (int i = first; i < total; i++)
            {
                int ry = cy + 1 + (i - first) * rowH;
                MacUi.Text(g, $"{i + 1}.", MacUi.Geneva, x0 + 5, ry);
                MacUi.Text(g, sanList[2 * i], MacUi.Geneva, x0 + 28, ry);
                if (2 * i + 1 < sanList.Count) MacUi.Text(g, sanList[2 * i + 1], MacUi.Geneva, x0 + 28 + (ww - 30) / 2, ry);
            }
        }

        void Btn(int bx, int by, string label, Action act)
        {
            var r = new Rectangle(bx, by, bw, bh);
            bool hover = r.Contains(mouseL);
            MacUi.Button(g, r, MacUi.Fit(g, label, MacUi.Chicago, bw - 6), hover);
            buttons.Add((new RectangleF(pa.X + r.X * px, pa.Y + r.Y * px, r.Width * px, r.Height * px), label, act));
        }
        Btn(x0, by1, "Новая", ShowNewGameMenu);
        Btn(x0 + bw + gap, by1, "Отменить", Undo);
        Btn(x0, by2, "Подсказка", Hint);
        Btn(x0 + bw + gap, by2, "Вид 2D", () => SetOriginal(false));
        MacUi.TextC(g, MacUi.Fit(g, "Пробел — пропустить бой", MacUi.Geneva, ww), MacUi.Geneva, x0 + ww / 2, footY, true);

        g.Dispose();
        MacUi.Blit(screen, bmp, pa.X, pa.Y);
    }

    // Надписи поверх доски в режиме оригинала: подпись боя, объявления, выбор фигуры при превращении.
    void DrawPixelOverlays(Graphics screen)
    {
        var area = BoardArea;
        int px = MacUi.Px;
        if (!string.IsNullOrEmpty(scene.Caption))
        {
            using var mg = Graphics.FromImage(Buffer(ref pixelOverlay, 1, 1));
            mg.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            int maxW = (int)area.Width / px - 8;
            string cap = MacUi.Fit(mg, scene.Caption, MacUi.Chicago, maxW - 12);
            int w = MacUi.TextW(mg, cap, MacUi.Chicago) + 14, h = 19;
            using var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = MacUi.Begin(b, false))
            {
                MacUi.Frame(g, 1, 1, w - 2, h - 2);
                MacUi.Text(g, cap, MacUi.Chicago, 7, 3);
            }
            MacUi.Blit(screen, b, area.X + (area.Width - w * px) / 2, area.Y + 4);
        }
        if (bannerT > 0 && banner != "")
        {
            using var mg = Graphics.FromImage(Buffer(ref pixelOverlay, 1, 1));
            mg.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            int w = MacUi.TextW(mg, banner, MacUi.Chicago) + 20, h = 24;
            using var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = MacUi.Begin(b, false))
            {
                MacUi.Frame(g, 1, 1, w - 2, h - 2);
                MacUi.Frame(g, 3, 3, w - 6, h - 6);
                MacUi.TextC(g, banner, MacUi.Chicago, w / 2, 5);
            }
            int scale = Math.Clamp((int)(area.Width / (w * 3.2f)), 2, 4);
            MacUi.Blit(screen, b, area.X + (area.Width - w * scale) / 2, area.Y + area.Height * 0.42f - h * scale / 2f, scale);
        }
        if (promoChoices != null) DrawPixelPromotion(screen);
    }

    // Диалог превращения пешки с картинками из оригинала (PICT 400..403).
    void DrawPixelPromotion(Graphics screen)
    {
        var area = BoardArea;
        int px = MacUi.Px, n = promoChoices.Count, cw = 60, ch = 118, gap = 6;
        int w = n * cw + (n + 1) * gap + 4, h = 17 + ch + 2 * gap + 4;
        using var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        float ox = area.X + (area.Width - w * px) / 2, oy = area.Y + (area.Height - h * px) / 2;
        var mouseL = new Point((int)MathF.Floor((mouse.X - ox) / px), (int)MathF.Floor((mouse.Y - oy) / px));
        promoRects = new RectangleF[n];
        using (var g = MacUi.Begin(b, false))
        {
            int cy = MacUi.Window(g, new Rectangle(0, 0, w, h), "Превратить пешку");
            for (int i = 0; i < n; i++)
            {
                int type = promoChoices[i].Promo;
                var r = new Rectangle(2 + gap + i * (cw + gap), cy + gap, cw, ch);
                MacUi.Frame(g, r.X, r.Y, r.Width, r.Height);
                if (cam.Original.PromoPict.TryGetValue(type, out var pic))
                    g.DrawImageUnscaled(pic, r.X + (cw - pic.Width) / 2, r.Y + 3);
                else MacUi.PieceGlyph(g, type * pos.Side, r.X + cw / 2 - 6, r.Y + 40);
                MacUi.TextC(g, P.RuNames[type], MacUi.Geneva, r.X + cw / 2, r.Bottom - 14);
                if (r.Contains(mouseL)) MacUi.Invert(b, new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2));
                promoRects[i] = new RectangleF(ox + r.X * px, oy + r.Y * px, r.Width * px, r.Height * px);
            }
        }
        using (var dim = new SolidBrush(Color.FromArgb(140, 0, 0, 0))) screen.FillRectangle(dim, area);
        MacUi.Blit(screen, b, ox, oy);
    }
}
