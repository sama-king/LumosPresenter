using LumosPresenter.Launcher;

namespace LumosPresenter.Tests.Launcher;

/// <summary>URL construction and argument parsing for the desktop launcher.</summary>
public sealed class LauncherLinksTests
{
    [Fact]
    public void ControlUsesLocalhostAndScreensUseTheLanAddress()
    {
        var links = LauncherLinks.Build(5170, "192.168.1.20");

        // The console is only ever opened here; projectors and OBS are on other machines
        // and cannot resolve localhost.
        Assert.Equal("http://localhost:5170/scripture", links[0].Url);
        Assert.Equal("http://192.168.1.20:5170/display/1", links[1].Url);
        Assert.Equal("http://192.168.1.20:5170/display/2", links[2].Url);
    }

    [Fact]
    public void FallsBackToLocalhostWhenThereIsNoLanAddress()
    {
        // A single-machine setup with no network is still a valid configuration.
        var links = LauncherLinks.Build(5170, null);

        Assert.Equal("http://localhost:5170/display/1", links[1].Url);
    }

    [Fact]
    public void NamesTheLinksControlAndScreens()
    {
        var links = LauncherLinks.Build(5170, "10.0.0.4");

        Assert.Equal(["Control", "Screen 1", "Screen 2"], links.Select(l => l.Name).ToArray());
    }

    [Fact]
    public void HonoursARequestedScreenCount()
    {
        var links = LauncherLinks.Build(5170, "10.0.0.4", screens: 3);

        Assert.Equal(4, links.Count);
        Assert.Equal("http://10.0.0.4:5170/display/3", links[3].Url);
    }

    [Theory]
    [InlineData(new[] { "--port", "8080" }, 8080)]
    [InlineData(new[] { "--port=8080" }, 8080)]
    [InlineData(new string[0], 5170)]
    [InlineData(new[] { "--port", "not-a-number" }, 5170)]
    public void ParsesThePortArgument(string[] args, int expected) =>
        Assert.Equal(expected, LauncherOptions.Port(args));
}
