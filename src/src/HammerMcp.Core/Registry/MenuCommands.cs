namespace HammerMcp.Core.Registry;

/// <summary>
/// Hammer main-menu command ids for WM_COMMAND. Identical in every Source Hammer build checked
/// (stock 2006-2026 x86/x64, Hammer++ 8846-8873, Slammin, Strata, Black Mesa, GMod...).
/// </summary>
public static class MenuCommands
{
    public const int Save = 57603;
    public const int SaveAs = 57604;
    public const int RunMap = 32903;
    public const int Undo = 57643;
    public const int Redo = 57644;
    public const int CheckForProblems = 32930;
    public const int LoadPointfile = 33021;
    public const int UnloadPointfile = 33022;
    public const int CenterViewsOnSelection = 32918;
    public const int GoToBrushNumber = 33061;
    public const int SelectAll = 32875;
    public const int EntityReport = 32996;
    public const int ShowConnections = 32931;
}
