using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LumosPresenter.Launcher;

/// <summary>Opens a URL in the operator's default browser.</summary>
public static class Browser
{
    public static void Open(string url)
    {
        try
        {
            // UseShellExecute is what hands the URL to the OS handler; on Linux the
            // shell-execute path is not wired up, so xdg-open is invoked directly.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
                return;
            }
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // A missing browser must never take the launcher down; the URL is on screen
            // and can still be copied.
        }
    }
}
