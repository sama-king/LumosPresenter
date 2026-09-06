using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LumosPresenter.Launcher;

/// <summary>How the server looks to the launcher right now, and what the button should offer.</summary>
public enum ServerState
{
    /// <summary>Nothing is answering and this launcher has no child running.</summary>
    Stopped,

    /// <summary>A child was started but is not answering yet — migrations, model load, warm-up.</summary>
    Starting,

    /// <summary>Answering, and this launcher owns the process.</summary>
    RunningOwned,

    /// <summary>Answering, but started outside this launcher — a developer running the WebHost directly.</summary>
    RunningExternal,
}

/// <summary>
/// Owns the WebHost as a child process, so running the packaged app starts the server as
/// well as the launcher. If a server is already answering on the port — a developer
/// running the WebHost separately — nothing is started and nothing is killed on exit.
///
/// The child's stdout and stderr are captured to a log file beside the server's own.
/// Serilog only records what the app manages to log, which misses precisely the failures
/// worth having: a native crash in the speech engine, an unhandled exception on a
/// background thread, or anything that goes wrong before logging is configured — Kestrel
/// failing to bind the port, a missing model, a database that will not migrate.
/// </summary>
public sealed class ServerProcess(int port) : IDisposable
{
    private Process? _process;
    // Serialises the writes from the stdout and stderr readers, which arrive on separate
    // threads, and from the lifecycle notes written on the UI thread.
    private readonly Lock _logLock = new();

    /// <summary>True when this launcher started the server and is responsible for stopping it.</summary>
    public bool OwnsServer => _process is { HasExited: false };

    /// <summary>
    /// Where the captured output goes. Kept beside the server's own rolling log rather
    /// than merged into it: two writers on one path buys locking and interleaving for
    /// nothing, and this file is the one that survives a crash the server cannot log.
    /// </summary>
    public static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "logs");

    private static string LogPath => Path.Combine(LogDirectory, "launcher-server.log");

    /// <summary>
    /// What to show for the given reachability. <paramref name="answering"/> comes from the
    /// health poll; the rest is what this launcher knows about its own child.
    /// </summary>
    public ServerState StateFor(bool answering)
    {
        if (answering)
        {
            return OwnsServer ? ServerState.RunningOwned : ServerState.RunningExternal;
        }
        // Not answering yet but the child is alive: still booting. The health check gives
        // up after three seconds, and a cold start — migrations, the speech model, the
        // api.bible warm-up — takes longer than that, so without this a Start click during
        // boot would launch a second WebHost to fight over the port.
        return OwnsServer ? ServerState.Starting : ServerState.Stopped;
    }

    /// <summary>
    /// Starts the WebHost beside this executable unless one is already running. Returns
    /// null on success, or the reason it could not start — in development the launcher is
    /// run on its own and the server started separately, which stays supported, but the
    /// button has to say so rather than appear to do nothing.
    /// </summary>
    public string? Start()
    {
        if (OwnsServer)
        {
            return null;
        }
        if (Executable() is not { } path)
        {
            return "No server is packaged beside this launcher. Start the WebHost yourself.";
        }
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(path)
                {
                    // Bind the port the launcher's links point at, and anchor the content root
                    // to the executable so the database and .env resolve regardless of where
                    // the app was launched from.
                    Arguments = $"--urls http://0.0.0.0:{port} --contentRoot \"{Path.GetDirectoryName(path)}\"",
                    WorkingDirectory = Path.GetDirectoryName(path)!,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
                EnableRaisingEvents = true,
            };
            // Both streams must be drained. Redirecting one and not reading it fills the
            // pipe buffer and blocks the child mid-write — the server would hang rather
            // than crash, which is worse than the problem this is here to solve.
            process.OutputDataReceived += (_, e) => Append(e.Data);
            process.ErrorDataReceived += (_, e) => Append(e.Data);
            // "exited with code 134 at 19:42" is the line that was missing: a native crash
            // in the speech engine leaves nothing in the server's own log at all.
            process.Exited += (_, _) => Append(
                $"--- server exited with code {ExitCodeOf(process)} ---", stamped: true);

            if (!process.Start())
            {
                process.Dispose();
                return "The server did not start.";
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            Append($"--- server started (pid {process.Id}, port {port}) ---", stamped: true);
            return null;
        }
        catch (Exception ex)
        {
            Append($"--- could not start the server: {ex.Message} ---", stamped: true);
            return $"Could not start the server: {ex.Message}";
        }
    }

    /// <summary>Exit code, or a placeholder when the process object will not report one.</summary>
    private static string ExitCodeOf(Process process)
    {
        try
        {
            return process.ExitCode.ToString();
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Starts the WebHost on first run. Kept for the startup path, which only wants a
    /// server up and has nowhere to show a failure.
    /// </summary>
    public bool StartIfNeeded() => Start() is null;

    /// <summary>
    /// Stops the server and starts it again, returning null on success or the reason it
    /// could not. The old process must be gone before the new one starts or the two race
    /// for the port, so this waits for a real exit rather than assuming one.
    /// </summary>
    public string? Restart()
    {
        Stop();
        return Start();
    }

    /// <summary>
    /// Ends the child and waits for it to actually exit. CloseMainWindow does nothing for
    /// a console child started with CreateNoWindow — it returns false immediately — so the
    /// kill is direct, and the wait is what keeps a restart from racing the port.
    /// </summary>
    public void Stop()
    {
        if (_process is not { } process)
        {
            return;
        }
        try
        {
            if (!process.HasExited)
            {
                Append("--- stopping the server ---", stamped: true);
                process.Kill(entireProcessTree: true);
                // Generous: SQLite has to close and the speech engine to release the
                // device. Bounded all the same so a wedged child cannot hang the launcher.
                process.WaitForExit(10_000);
            }
        }
        catch (Exception)
        {
            // Nothing useful to do — the state below is reset either way.
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }

    /// <summary>The sibling WebHost executable, or null when it is not packaged alongside.</summary>
    private static string? Executable()
    {
        var name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "LumosPresenter.WebHost.exe"
            : "LumosPresenter.WebHost";
        var path = Path.Combine(AppContext.BaseDirectory, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Appends one captured line. Failing to write a log must never take the launcher
    /// down, so every error here is swallowed deliberately.
    /// </summary>
    private void Append(string? line, bool stamped = false)
    {
        // The readers signal end-of-stream with a null line.
        if (line is null)
        {
            return;
        }
        try
        {
            lock (_logLock)
            {
                Directory.CreateDirectory(LogDirectory);
                var text = stamped ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}" : line;
                File.AppendAllText(LogPath, text + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // A log that cannot be written is not worth a crash.
        }
    }

    /// <summary>Stops the server if this launcher started it.</summary>
    public void Dispose() => Stop();
}
