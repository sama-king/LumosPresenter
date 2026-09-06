namespace LumosPresenter.Launcher;

/// <summary>Command-line options: the launcher only needs to know which port to point at.</summary>
public static class LauncherOptions
{
    public const int DefaultPort = 5170;

    /// <summary>
    /// Reads --port N (or --port=N) from the arguments, falling back to the WebHost's
    /// default. Keeps the launcher usable against a server started on another port.
    /// </summary>
    public static int Port(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Count && int.TryParse(args[i + 1], out var next))
            {
                return next;
            }
            if (args[i].StartsWith("--port=", StringComparison.Ordinal)
                && int.TryParse(args[i]["--port=".Length..], out var inline))
            {
                return inline;
            }
        }
        return DefaultPort;
    }
}
