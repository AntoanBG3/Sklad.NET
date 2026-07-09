using System.Runtime.CompilerServices;

namespace Sklad.Tests;

internal static class TestPaths
{
    public static string RepoRoot([CallerFilePath] string callerPath = "") =>
        Directory.GetParent(Path.GetDirectoryName(callerPath)!)!.FullName;

    public static string App([CallerFilePath] string callerPath = "") =>
        Path.Combine(RepoRoot(callerPath), "Sklad.NET");
}
