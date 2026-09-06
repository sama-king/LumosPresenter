using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LumosPresenter.Launcher;

/// <summary>One row in the launcher: a human name and the URL it opens.</summary>
/// <param name="IsLocal">
/// True for the operator console, which is only ever opened on this machine. The display
/// links use the LAN address instead, since projectors and OBS run on other devices and
/// cannot resolve localhost.
/// </param>
public sealed record LauncherLink(string Name, string Description, string Url, bool IsLocal);

/// <summary>
/// Builds the URLs the launcher offers. Mirrors the routes the WebHost serves: the
/// console at /scripture and one display page per screen.
/// </summary>
public static class LauncherLinks
{
    /// <summary>
    /// The LAN address other devices reach this machine on, or null when offline. Picks
    /// the ranges a church LAN actually hands out over VPN and virtual-adapter leftovers
    /// — the same ordering the WebHost's /api/network endpoint uses.
    /// </summary>
    public static string? LocalAddress() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
            .Select(ip => ip.ToString())
            .Where(ip => !ip.StartsWith("169.254.", StringComparison.Ordinal))
            .Distinct()
            .OrderByDescending(ip => ip.StartsWith("192.168.", StringComparison.Ordinal))
            .ThenByDescending(ip => ip.StartsWith("10.", StringComparison.Ordinal))
            .FirstOrDefault();

    /// <summary>
    /// The console plus one link per screen. <paramref name="screens"/> defaults to the
    /// two a typical stage runs (main projection + stage/confidence monitor).
    /// </summary>
    public static IReadOnlyList<LauncherLink> Build(int port, string? host, int screens = 2)
    {
        // Without a LAN address the display links still work locally, so fall back rather
        // than hiding them: a single-machine setup is perfectly valid.
        var lanHost = host ?? "localhost";
        var links = new List<LauncherLink>
        {
            new("Control", "Operator console — this machine", $"http://localhost:{port}/scripture", true),
        };
        for (var screen = 1; screen <= screens; screen++)
        {
            links.Add(new LauncherLink(
                $"Screen {screen}",
                screen == 1 ? "Projection output" : "Second output",
                $"http://{lanHost}:{port}/display/{screen}",
                false));
        }
        return links;
    }
}
