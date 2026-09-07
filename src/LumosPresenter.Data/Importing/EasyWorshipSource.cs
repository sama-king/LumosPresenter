using LumosPresenter.Data.Importing.Paradox;

namespace LumosPresenter.Data.Importing;

/// <summary>How a given EasyWorship library stores its songs.</summary>
public enum EasyWorshipFormat
{
    /// <summary>EasyWorship 2009 and earlier: a Paradox table (Songs.DB) plus blobs (Songs.MB).</summary>
    Paradox,

    /// <summary>EasyWorship 6/7: two Firebird databases (Songs.db and SongWords.db).</summary>
    Firebird,
}

/// <summary>
/// A located song library: which format it is, and the file(s) that hold the songs and their
/// lyrics. <see cref="Description"/> is operator-facing text for the import screen.
/// </summary>
public sealed record EasyWorshipLibrary(
    EasyWorshipFormat Format,
    string SongsPath,
    string? WordsPath,
    string Description);

/// <summary>
/// Finds an EasyWorship song library on disk and decides which format it is.
///
/// The two generations differ in both storage and layout, so a single "pick a .db file"
/// gesture is not enough: EasyWorship 2009 keeps lyrics in a blob file beside the table
/// (Songs.DB + Songs.MB), while 6/7 splits them across two separate Firebird databases
/// (Songs.db + SongWords.db). Both live in a "Databases\Data" folder whose location the
/// installed profile records, so <see cref="Locate"/> can usually find the library without
/// asking the operator anything.
/// </summary>
public static class EasyWorshipSource
{
    /// <summary>Profile files naming the active data directory, newest generation first.</summary>
    private static readonly string[] ProfileFiles =
    [
        @"Softouch\EasyWorship.v7\Profiles\Default.ewp",
        @"Softouch\EasyWorship\Profiles\Default.ewp",
    ];

    /// <summary>
    /// Auto-detects installed libraries without operator input: it reads the EasyWorship
    /// profile for the configured data directory, then falls back to the default install
    /// locations. Returns every distinct library found, best candidate first.
    /// </summary>
    public static IReadOnlyList<EasyWorshipLibrary> Locate()
    {
        var found = new List<EasyWorshipLibrary>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in CandidateDirectories())
        {
            if (TryResolve(directory, out var library, out _)
                && seen.Add(library.SongsPath))
            {
                found.Add(library);
            }
        }
        return found;
    }

    /// <summary>
    /// Resolves an operator-supplied path — a Databases\Data folder, an EasyWorship data
    /// root, or the songs file itself — into a library, or explains why it could not.
    /// </summary>
    public static bool TryResolve(
        string path, out EasyWorshipLibrary library, out string error)
    {
        library = null!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Enter the path to your EasyWorship data folder.";
            return false;
        }

        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

        string? songsPath = null;
        if (File.Exists(path))
        {
            songsPath = path;
        }
        else if (Directory.Exists(path))
        {
            songsPath = FindSongsFile(path);
            if (songsPath is null)
            {
                error = $"No Songs.db or Songs.DB found in '{path}'.";
                return false;
            }
        }
        else
        {
            error = $"'{path}' does not exist.";
            return false;
        }

        var directory = Path.GetDirectoryName(songsPath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(songsPath);

        // A Paradox table validates strictly (its field widths must total the record size
        // exactly), so a successful parse is a reliable positive; anything else that sits in
        // this folder is treated as Firebird and reported by the Firebird reader if wrong.
        if (ParadoxTable.LooksLikeParadoxTable(songsPath))
        {
            var blobs = Sibling(directory, stem + ".MB");
            library = new EasyWorshipLibrary(
                EasyWorshipFormat.Paradox, songsPath, blobs,
                blobs is null
                    ? "EasyWorship 2009 (Paradox) — lyrics blob file Songs.MB is missing"
                    : "EasyWorship 2009 (Paradox)");
            return true;
        }

        var words = Sibling(directory, "SongWords.db");
        if (words is null)
        {
            error = $"Found '{Path.GetFileName(songsPath)}' but no SongWords.db beside it. " +
                "EasyWorship 6/7 stores lyrics in a separate SongWords.db in the same folder.";
            return false;
        }

        library = new EasyWorshipLibrary(
            EasyWorshipFormat.Firebird, songsPath, words, "EasyWorship 6/7 (Firebird)");
        return true;
    }

    /// <summary>Data directories worth probing: profile-configured first, then the defaults.</summary>
    private static IEnumerable<string> CandidateDirectories()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        foreach (var profile in ProfileFiles)
        {
            var dataDirectory = ReadProfileDataDirectory(Path.Combine(programData, profile));
            if (dataDirectory is not null)
            {
                yield return Path.Combine(dataDirectory, "Databases", "Data");
            }
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                     Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                 })
        {
            if (!string.IsNullOrEmpty(root))
            {
                yield return Path.Combine(
                    root, "Softouch", "EasyWorship", "Default", "Databases", "Data");
            }
        }
    }

    /// <summary>
    /// Pulls AppInstDataDir out of an EasyWorship profile (.ewp), a small INI naming the
    /// directory that actually holds the databases — which is frequently not the default.
    /// </summary>
    private static string? ReadProfileDataDirectory(string profilePath)
    {
        try
        {
            if (!File.Exists(profilePath))
            {
                return null;
            }
            foreach (var line in File.ReadLines(profilePath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("AppInstDataDir=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed["AppInstDataDir=".Length..].Trim();
                    return value.Length == 0 ? null : value;
                }
            }
        }
        catch (IOException)
        {
            // An unreadable profile just means we fall through to the default locations.
        }
        catch (UnauthorizedAccessException)
        {
        }
        return null;
    }

    /// <summary>Songs file inside a folder, tolerating the Data subfolder and case differences.</summary>
    private static string? FindSongsFile(string directory)
    {
        foreach (var candidate in new[] { directory, Path.Combine(directory, "Databases", "Data"), Path.Combine(directory, "Data") })
        {
            if (!Directory.Exists(candidate))
            {
                continue;
            }
            var match = Directory.EnumerateFiles(candidate, "Songs.*")
                .FirstOrDefault(f => Path.GetExtension(f).Equals(".db", StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }
        return null;
    }

    /// <summary>Case-insensitive sibling lookup — the on-disk casing varies between versions.</summary>
    private static string? Sibling(string directory, string fileName)
    {
        var direct = Path.Combine(directory, fileName);
        if (File.Exists(direct))
        {
            return direct;
        }
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory)
                .FirstOrDefault(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase))
            : null;
    }
}
