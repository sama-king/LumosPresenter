using LumosPresenter.Launcher;

namespace LumosPresenter.Tests.Launcher;

/// <summary>
/// The state the launcher's server button reads. Only the states reachable without a real
/// child process are covered here: starting one would need a packaged WebHost beside the
/// test assembly, which is exactly the case <see cref="ServerProcess.Start"/> reports as
/// unavailable rather than attempting.
/// </summary>
public sealed class ServerProcessTests
{
    [Fact]
    public void NothingAnsweringAndNoChildIsStopped()
    {
        var process = new ServerProcess(5170);

        // The offer has to be Start: this is the state an operator lands in after a crash.
        Assert.Equal(ServerState.Stopped, process.StateFor(answering: false));
    }

    [Fact]
    public void AnsweringWithoutAChildIsExternal()
    {
        var process = new ServerProcess(5170);

        // A developer running the WebHost separately owns it; the launcher must not offer
        // to restart something it did not start and cannot stop.
        Assert.Equal(ServerState.RunningExternal, process.StateFor(answering: true));
    }

    [Fact]
    public void ReportsWhenNoServerIsPackagedAlongside()
    {
        var process = new ServerProcess(5170);

        // The test assembly has no sibling WebHost, the same as the launcher run on its
        // own in development. The button has to say so rather than click to nothing.
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
        var process = new ServerProcess(5170);

        // Quit and the restart path both call this unconditionally.
        process.Stop();
        process.Dispose();

        Assert.False(process.OwnsServer);
    }
}
