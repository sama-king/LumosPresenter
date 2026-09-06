namespace LumosPresenter.WebHost;

/// <summary>
/// Minimal .env reader. ASP.NET Core's configuration has no notion of .env files, so any
/// values in a (gitignored) .env at the repository root are loaded into the environment
/// before configuration is built. Real environment variables always win, and KEY_NAME maps
/// to configuration as Key:Name via the standard double-underscore rules.
///
/// API_BIBLE_KEY is retained only as a legacy seed: the key is a per-installation setting
/// stored in the database now, and a .env value is adopted once, on a database that has
/// none. Nothing new should be added here — settings users own belong in app_settings.
/// </summary>
public static class DotEnv
{
    /// <summary>Maps flat .env names onto the configuration keys the app binds.</summary>
    private static readonly Dictionary<string, string> ConfigurationAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["API_BIBLE_KEY"] = "ApiBible:Key",
    };

    /// <summary>
    /// Loads the nearest .env, searching upward from each of <paramref name="startDirectories"/>
    /// in turn. Callers pass the app's base directory as well as the working directory:
    /// a double-clicked .app gets a working directory of "/", and a WebHost spawned by the
    /// launcher inherits the launcher's, so the binary's own location is the only reliable
    /// anchor back to the repository during development.
    /// </summary>
    public static void Load(params string[] startDirectories)
    {
        foreach (var start in startDirectories)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, ".env");
                if (File.Exists(candidate))
                {
                    Apply(candidate);
                    return;
                }
                directory = directory.Parent;
            }
        }
    }

    private static void Apply(string path)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');

            // A value already in the real environment takes precedence over the file.
            if (Environment.GetEnvironmentVariable(name) is null)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
            if (ConfigurationAliases.TryGetValue(name, out var configurationKey)
                && Environment.GetEnvironmentVariable(configurationKey) is null)
            {
                Environment.SetEnvironmentVariable(configurationKey, value);
            }
        }
    }
}
