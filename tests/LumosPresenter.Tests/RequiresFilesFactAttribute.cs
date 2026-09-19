namespace LumosPresenter.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that reports the test as <b>Skipped</b>, naming what is
/// missing, when files it needs are not on this machine — speech models, golden audio, and
/// other downloads that are too large to commit.
///
/// The alternative, returning early from the test body, makes xUnit count the test as
/// <i>Passed</i> having run nothing. The same green result would then mean "transcribed
/// correctly" on a machine with the models and "did nothing" on one without, and a summary
/// could not tell them apart. Skip is decided at discovery, so the run's own totals say how
/// much was actually exercised on each machine.
///
/// Paths are relative to the repository root (the folder holding global.json) and use '/',
/// which every OS we build on accepts.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresFilesFactAttribute : FactAttribute
{
    public RequiresFilesFactAttribute(params string[] repoRelativePaths)
    {
        var missing = repoRelativePaths
            .Where(path => !File.Exists(TestPaths.FromRepoRoot(path)))
            .ToList();
        if (missing.Count > 0)
        {
            Skip = "Missing on this machine: " + string.Join(", ", missing) +
                ". Download the speech models to run it.";
        }
    }
}

/// <summary>Locating files the tests need, the same way on every OS.</summary>
public static class TestPaths
{
    /// <summary>The repository root: the nearest folder above the test binaries holding global.json.</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string FromRepoRoot(string repoRelativePath) =>
        Path.GetFullPath(Path.Combine(RepoRoot, repoRelativePath));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
