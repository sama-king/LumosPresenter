using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LumosPresenter.Launcher;

/// <summary>
/// Opens a folder in the operator's file manager. Separate from <see cref="Browser"/>
/// because a path is not a URL: shell-executing one works on Windows but not reliably on
/// macOS, where the folder has to be handed to Finder explicitly.
/// </summary>
public static class Reveal
{
    /// <summary>
    /// Shows the folder, creating it first so an operator who looks before the server has
    /// written anything gets an empty folder rather than nothing happening at all.
    /// </summary>
    public static void Folder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", [path]);
                return;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", [path]);
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // A file manager that will not open must never take the launcher down.
        }
    }
}
