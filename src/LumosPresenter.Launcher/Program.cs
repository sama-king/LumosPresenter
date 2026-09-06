using Avalonia;

namespace LumosPresenter.Launcher;

/// <summary>
/// Entry point for the desktop launcher shown when the packaged app runs: the logo, links
/// to the console and each screen, and a microphone picker — in a window and in the tray.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Referenced by name by the Avalonia tooling; keep the signature.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
