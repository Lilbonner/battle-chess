namespace BattleChess;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--make-icon") { IconMaker.Save(args[1]); return; }
        ApplicationConfiguration.Initialize();
        if (args.Length >= 2 && args[0] == "--playtest")
        {
            using var f = new GameForm();
            f.RunPlaytest(args[1], args.Length > 2 ? int.Parse(args[2]) : 3, args.Length > 3 ? int.Parse(args[3]) : 80);
            return;
        }
        if (args.Length >= 2 && args[0] == "--techniques") { using var f = new GameForm(); f.RenderTechniques(args[1], args.Length > 2 && args[2] == "orig"); return; }
        if (args.Length >= 2 && args[0] == "--pawn") { using var f = new GameForm(); f.RenderPawnDebug(args[1], args.Length > 2 ? args[2] : ""); return; }
        if (args.Length == 2 && args[0] == "--glyphs") { GlyphTest.Render(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--spin") { using var f = new GameForm(); f.RenderSpin(args[1], args.Length > 2 && args[2] == "orig"); return; }
        if (args.Length >= 2 && args[0] == "--shots") { using var f = new GameForm(); f.RenderShots(args[1], args.Length > 2 ? args[2] : ""); return; }
        Application.Run(new GameForm());
    }
}
