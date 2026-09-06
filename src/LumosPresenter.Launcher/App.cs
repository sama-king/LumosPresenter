using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace LumosPresenter.Launcher;

/// <summary>
/// The launcher application: a window plus a tray icon, and the owner of the WebHost
/// process. Closing the window only hides it — the server keeps serving the displays, so
/// an operator who dismisses the launcher mid-service does not take the projection down.
/// Quitting from the tray is the one action that stops the server, and only when this
/// launcher was the one that started it.
/// </summary>
public sealed class App : Application
{
    private MainWindow? _window;
    private TrayIcon? _tray;
    private ServerClient? _server;
    private ServerProcess? _serverProcess;
    private IReadOnlyList<LauncherLink> _links = [];
    private DispatcherTimer? _healthTimer;
    private NativeMenuItem? _microphoneMenu;
    private NativeMenuItem? _listeningMenu;
    private bool _listening;
    // Both tray icons are loaded once: the health poll runs every five seconds, and
    // re-decoding a PNG per tick to hand the platform the same bitmap is waste.
    private WindowIcon? _trayIdleIcon;
    private WindowIcon? _trayActiveIcon;
    // Null until the first reading, so the initial paint always applies.
    private bool? _paintedListening;
    // Rebuilding the submenu on every poll would drop a menu the user has open, so the
    // device list is only rewritten when it actually changed.
    private string _microphoneSignature = string.Empty;

    // The theme variant is left unset so Fluent follows the OS. The window's own palette
    // is defined per variant to match — see MainWindow — because Fluent's hover and
    // disabled brushes come from whichever palette is active, and painting a fixed light
    // ground under dark-mode brushes makes hovered buttons and the disabled microphone
    // picker vanish.
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        // The launcher outlives its window: only the tray's Quit ends the process.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var port = LauncherOptions.Port(desktop.Args ?? []);
        _server = new ServerClient(port);
        _links = LauncherLinks.Build(port, LauncherLinks.LocalAddress());

        // Running the packaged app must bring the server up too. If one is already
        // answering (a developer running the WebHost separately) this leaves it alone,
        // and correspondingly does not stop it on quit.
        _serverProcess = new ServerProcess(port);
        _ = Task.Run(async () =>
        {
            if (!await _server.IsRunningAsync())
            {
                _serverProcess.StartIfNeeded();
            }
        });

        _window = new MainWindow(_links, _server);
        // A toggle in the window must move the tray badge too, not wait for the poll.
        _window.ListeningChanged += RefreshListeningAsync;
        _window.ServerActionRequested += StartOrRestartServerAsync;
        _window.Closing += (_, e) =>
        {
            // Hide rather than close; the tray keeps the app reachable.
            e.Cancel = true;
            _window.Hide();
        };
        _window.Show();

        BuildTray();
        StartHealthPolling();
        _ = _window.RefreshMicrophonesAsync();
        // Populate the tray submenu before it is ever opened.
        _ = RefreshMicrophoneMenuAsync();
        _ = RefreshListeningAsync();

        base.OnFrameworkInitializationCompleted();
    }

    private void BuildTray()
    {
        var menu = new NativeMenu();
        menu.Add(new NativeMenuItem("Open Launcher") { Command = new Command(ShowWindow) });
        menu.Add(new NativeMenuItemSeparator());
        foreach (var link in _links)
        {
            menu.Add(new NativeMenuItem($"Open {link.Name}")
            {
                Command = new Command(() => Browser.Open(link.Url)),
            });
        }
        menu.Add(new NativeMenuItemSeparator());

        // Listening is the server's state, so this only ever reflects what the server
        // reports; the health poll keeps it right when the console toggles it instead.
        _listeningMenu = new NativeMenuItem("Start Listening")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = false,
            Command = new Command(() => _ = ToggleListeningAsync()),
        };
        menu.Add(_listeningMenu);
        menu.Add(new NativeMenuItemSeparator());

        // The submenu is populated ahead of time and kept current by the health poll, so
        // it is already correct whenever macOS renders it. NeedsUpdate additionally asks
        // for a refresh just before the menu is shown; both paths mutate the same
        // NativeMenu in place, because replacing the object leaves the exported native
        // menu pointing at the old one — which is what made the submenu appear empty and
        // dismiss on click.
        var microphoneSubmenu = new NativeMenu();
        // Never export an empty submenu: macOS treats one with no items as nothing to
        // show, so the placeholder guarantees a real menu exists from the outset and the
        // refresh below only ever swaps its contents.
        microphoneSubmenu.Items.Add(new NativeMenuItem("Loading…") { IsEnabled = false });
        microphoneSubmenu.NeedsUpdate += (_, _) => _ = RefreshMicrophoneMenuAsync();
        _microphoneMenu = new NativeMenuItem("Microphone") { Menu = microphoneSubmenu };
        menu.Add(_microphoneMenu);
        menu.Add(new NativeMenuItemSeparator());
        // The tray is where an operator lands when the window is hidden mid-service, so
        // recovery and the logs have to be reachable from here too, not only the window.
        menu.Add(new NativeMenuItem("Restart Server")
        {
            Command = new Command(() => _ = StartOrRestartServerAsync()),
        });
        menu.Add(new NativeMenuItem("Show Logs")
        {
            Command = new Command(() => Reveal.Folder(ServerProcess.LogDirectory)),
        });
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(new NativeMenuItem("Quit") { Command = new Command(Quit) });

        _tray = new TrayIcon
        {
            ToolTipText = "LumosCast",
            Menu = menu,
            IsVisible = true,
        };
        // The navy tile rather than the bare mark: Avalonia gives macOS tray icons no
        // template treatment, so a single-colour mark would disappear against one
        // menu-bar appearance or the other. The active twin carries the listening badge.
        _trayIdleIcon = LoadIcon("tray-64.png");
        _trayActiveIcon = LoadIcon("tray-active-64.png");
        if (_trayIdleIcon is not null)
        {
            _tray.Icon = _trayIdleIcon;
        }
        _tray.Clicked += (_, _) => ShowWindow();
    }

    /// <summary>Loads a tray bitmap, or null when the asset is missing.</summary>
    private static WindowIcon? LoadIcon(string file)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://LumosPresenter.Launcher/Assets/{file}"));
            return new WindowIcon(new Bitmap(stream));
        }
        catch (Exception)
        {
            // Without an icon the tray entry still works (text-only on some platforms).
            return null;
        }
    }

    /// <summary>Flips listening on the server, then repaints from what the server reports.</summary>
    private async Task ToggleListeningAsync()
    {
        if (_server is null)
        {
            return;
        }
        await _server.SetListeningAsync(!_listening);
        // Read back rather than assuming the post took: a failed start must not leave the
        // tick on. The window shows the reason; the tray has nowhere to put one.
        await RefreshListeningAsync();
        if (_window is not null)
        {
            await _window.RefreshMicrophonesAsync();
        }
    }

    /// <summary>Syncs the tray item and the window to the server's listening state.</summary>
    private async Task RefreshListeningAsync()
    {
        if (_server is null || await _server.IsListeningAsync() is not { } listening)
        {
            return;
        }
        _listening = listening;
        // Cheap and idempotent, so it runs on every poll and keeps the window right even
        // when the tray is already showing the correct state.
        _window?.SetListeningState(listening);
        // The tray is the expensive half: the poll calls this every five seconds, and
        // reassigning the icon each tick is a Shell_NotifyIcon call per tick on Windows,
        // which shows up as a flicker in the notification area.
        if (_paintedListening == listening)
        {
            return;
        }
        _paintedListening = listening;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Mutate the exported item in place — replacing it would leave the native menu
            // pointing at the old object, the same trap as the microphone submenu above.
            if (_listeningMenu is not null)
            {
                _listeningMenu.IsChecked = listening;
                _listeningMenu.Header = listening ? "Stop Listening" : "Start Listening";
            }
            if (_tray is not null)
            {
                // Windows exposes no badge API for a notification-area icon, so the
                // badge is baked into a second icon and swapped in. The tooltip says the
                // same thing in words — the conventional Windows signal, and a fallback
                // wherever the badge is too small to read.
                if ((listening ? _trayActiveIcon : _trayIdleIcon) is { } icon)
                {
                    _tray.Icon = icon;
                }
                _tray.ToolTipText = listening ? "LumosCast — Listening" : "LumosCast";
            }
        });
    }

    /// <summary>Rebuilds the microphone submenu, ticking whichever device is selected.</summary>
    private async Task RefreshMicrophoneMenuAsync()
    {
        if (_server is null || _microphoneMenu is null)
        {
            return;
        }
        var (devices, selected) = await _server.GetMicrophonesAsync();
        var signature = string.Join("|", devices.Select(d => $"{d.Id}:{d.Name}")) + $"#{selected}";
        if (signature == _microphoneSignature)
        {
            return;
        }
        _microphoneSignature = signature;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Mutate the menu the tray was exported with; assigning a new NativeMenu here
            // would leave the native macOS menu pointing at the original object, which is
            // what made the submenu render empty and simply dismiss on click.
            var submenu = _microphoneMenu.Menu ??= new NativeMenu();
            submenu.Items.Clear();
            if (devices.Count == 0)
            {
                submenu.Items.Add(new NativeMenuItem("No microphones found") { IsEnabled = false });
            }
            foreach (var device in devices)
            {
                var id = device.Id;
                submenu.Items.Add(new NativeMenuItem(device.Name)
                {
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = id == selected,
                    Command = new Command(() => _ = SelectMicrophoneAsync(id)),
                });
            }
        });
    }

    private async Task SelectMicrophoneAsync(int deviceId)
    {
        if (_server is not null)
        {
            await _server.SelectMicrophoneAsync(deviceId);
            if (_window is not null)
            {
                await _window.RefreshMicrophonesAsync();
            }
            await RefreshMicrophoneMenuAsync();
        }
    }

    /// <summary>
    /// Starts the server, or restarts it when this launcher already owns one. Returns null
    /// on success or the reason to show. Runs off the UI thread because stopping waits for
    /// the old process to actually exit, which must not freeze the window.
    /// </summary>
    private async Task<string?> StartOrRestartServerAsync()
    {
        if (_serverProcess is not { } process)
        {
            return "The launcher cannot manage the server.";
        }
        var error = await Task.Run(() => process.OwnsServer ? process.Restart() : process.Start());
        // Repaint immediately rather than leaving the button on "Working…" for up to five
        // seconds; the poll then keeps it honest as the server comes up.
        await RefreshServerStatusAsync();
        return error;
    }

    /// <summary>Reads reachability once and paints the window's status and server button.</summary>
    private async Task RefreshServerStatusAsync()
    {
        if (_server is null || _window is null)
        {
            return;
        }
        var running = await _server.IsRunningAsync();
        // The state pairs reachability with what this launcher knows about its own child,
        // which is what separates "still booting" from "stopped" and "not ours to restart".
        _window.SetServerStatus(running, _serverProcess?.StateFor(running) ?? ServerState.RunningExternal);
    }

    /// <summary>Polls the WebHost so the header dot reflects reality without a refresh.</summary>
    private void StartHealthPolling()
    {
        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _healthTimer.Tick += async (_, _) =>
        {
            if (_server is not null && _window is not null)
            {
                await RefreshServerStatusAsync();
                // Keeps the tray's device list current as hardware comes and goes.
                await RefreshMicrophoneMenuAsync();
                // Picks up listening started or stopped from the web console.
                await RefreshListeningAsync();
            }
        };
        _healthTimer.Start();
        _ = Task.Run(RefreshServerStatusAsync);
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
        _ = _window.RefreshMicrophonesAsync();
        _ = RefreshListeningAsync();
    }

    private void Quit()
    {
        _tray?.Dispose();
        // Stops the WebHost only when this launcher started it.
        _serverProcess?.Dispose();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
