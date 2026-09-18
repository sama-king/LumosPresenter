using LumosPresenter.Launcher;

namespace LumosPresenter.Tests.Launcher;

/// <summary>
/// The state the launcher's server button reads. Only the states reachable without a real
/// child process are covered, and each test guarantees that by pointing the
/// <see cref="ServerProcess"/> at an empty folder of its own.
///
/// Relying on the default — beside the test assembly — is not safe: what lands there depends
/// on the test project's references, and once the WebHost is one of them its executable sits
/// right beside the tests, so <see cref="ServerProcess.Start"/> launches a real server. That
/// server inherits the test host's handles, including the stdout pipe <c>dotnet test</c>
/// reads, so the run hangs instead of failing and the server is left holding port 5170. The
/// empty folder makes the outcome the same on every machine, whatever was built beside the
/// tests. Every instance is still disposed with <c>using</c>, which kills anything that did
/// start, as a second line of defence.
/// </summary>
public sealed class ServerProcessTests : IDisposable
{
    private readonly string _emptyDir =
        Path.Combine(Path.GetTempPath(), "lumos-launcher-" + Guid.NewGuid().ToString("N"));

    public ServerProcessTests() => Directory.CreateDirectory(_emptyDir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_emptyDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ServerProcess NoServerAlongside() => new(5170, _emptyDir);

    [Fact]
    public void NothingAnsweringAndNoChildIsStopped()
    {
        using var process = NoServerAlongside();

        // The offer has to be Start: this is the state an operator lands in after a crash.
        Assert.Equal(ServerState.Stopped, process.StateFor(answering: false));
    }

    [Fact]
    public void AnsweringWithoutAChildIsExternal()
    {
        using var process = NoServerAlongside();

        // A developer running the WebHost separately owns it; the launcher must not offer
        // to restart something it did not start and cannot stop.
        Assert.Equal(ServerState.RunningExternal, process.StateFor(answering: true));
    }

    [Fact]
    public void ReportsWhenNoServerIsPackagedAlongside()
    {
        using var process = NoServerAlongside();

        // The same situation as the launcher run on its own in development. The button has
        // to say so rather than click to nothing.
        var error = process.Start();

        Assert.NotNull(error);
        Assert.Contains("packaged", error);
        Assert.False(process.OwnsServer);
    }

    [Fact]
    public void LogDirectorySitsBesideTheExecutable()
    {
        // The launcher's Show Logs button and the operator both need one predictable
        // place, resolved from the binary rather than the working directory.
        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "logs"),
            ServerProcess.LogDirectory);
    }

    [Fact]
    public void StoppingWithoutAChildIsHarmless()
    {
        var process = NoServerAlongside();

        // Quit and the restart path both call this unconditionally.
        process.Stop();
        process.Dispose();

        Assert.False(process.OwnsServer);
    }
}
