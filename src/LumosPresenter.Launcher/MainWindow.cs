using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace LumosPresenter.Launcher;

/// <summary>
/// The window an operator sees when the packaged app starts: the logo, whether the server
/// is up, and one row per page with Open and Copy. Closing it hides to the tray rather
/// than quitting — the server must keep running through a service.
/// </summary>
public sealed class MainWindow : Window
{
    // Brand purple is the one constant: it is the identity, and it carries on either
    // ground. Everything else is a pair — the window follows the OS theme, so each
    // surface colour needs a value for both palettes. Resolved once in the constructor
    // rather than bound, because the OS variant does not change while the window is open
    // without the whole app restyling anyway.
    private static readonly Color Brand = Color.Parse("#863bff");
    private static readonly Color Danger = Color.Parse("#f04438");
    private static readonly Color Online = Color.Parse("#12b76a");

    private readonly bool _dark;
    private readonly Color _ink;      // primary text
    private readonly Color _muted;    // secondary text
    private readonly Color _ground;   // window background
    private readonly Color _card;     // raised row background
    private readonly Color _line;     // hairlines and borders
    private readonly Color _subtle;   // secondary button fill

    // Foregrounds are set in the constructor, once the palette for the active theme is known.
    private readonly TextBlock _statusText = new()
    {
        Text = "Checking…",
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Ellipse _statusDot = new()
    {
        Width = 8,
        Height = 8,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly ComboBox _microphones = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        FontSize = 12,
        PlaceholderText = "No microphones found",
    };
    private readonly TextBlock _microphoneNote = new()
    {
        FontSize = 11,
        IsVisible = false,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Button _listeningButton = new()
    {
        Content = "Start listening",
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Padding = new Thickness(14, 9),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(7),
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
    };
    private readonly TextBlock _listeningNote = new()
    {
        FontSize = 11,
        IsVisible = false,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Button _serverButton = new()
    {
        Content = "Checking…",
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Padding = new Thickness(14, 9),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(7),
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        IsEnabled = false,
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
    };
    private readonly Button _logsButton = new()
    {
        Content = "Show logs",
        Padding = new Thickness(12, 6),
        BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(6),
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
    };
    private readonly TextBlock _serverNote = new()
    {
        FontSize = 11,
        IsVisible = false,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly ServerClient _server;
    // Set while the list is being repopulated, so programmatic selection is not mistaken
    // for the operator picking a device.
    private bool _loadingMicrophones;
    // The server's last known listening state, so the button knows which way to toggle.
    private bool _listening;
    // How many devices the list holds. Tracked here rather than read back from the
    // ComboBox: ItemCount follows the control's own update of ItemsSource, which has not
    // necessarily happened by the time a separately posted callback looks at it — which
    // left the picker disabled at startup, when the listening state and the device list
    // are fetched concurrently and each callback read the other's half of the answer.
    private int _deviceCount;

    public MainWindow(IReadOnlyList<LauncherLink> links, ServerClient server)
    {
        _server = server;

        // Follow the OS. ActualThemeVariant is only settled once the window is attached,
        // so the platform setting is read directly here; Fluent resolves its own brushes
        // from the same setting, which is what keeps hover and disabled states legible.
        _dark = Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant
            == PlatformThemeVariant.Dark;
        _ink = _dark ? Color.Parse("#f2f3f7") : Color.Parse("#0f1017");
        _muted = _dark ? Color.Parse("#9ba0b4") : Color.Parse("#8b8fa3");
        _ground = _dark ? Color.Parse("#16171d") : Color.Parse("#fbfbfd");
        _card = _dark ? Color.Parse("#1f2027") : Color.Parse("#ffffff");
        _line = _dark ? Color.Parse("#2e3038") : Color.Parse("#e6e6ee");
        _subtle = _dark ? Color.Parse("#2a2c34") : Color.Parse("#eef0f6");

        _statusText.Foreground = new SolidColorBrush(_muted);
        _statusDot.Fill = new SolidColorBrush(_muted);
        _microphoneNote.Foreground = new SolidColorBrush(_muted);
        _listeningNote.Foreground = new SolidColorBrush(_muted);
        _serverNote.Foreground = new SolidColorBrush(_muted);
        _logsButton.Background = new SolidColorBrush(_subtle);
        _logsButton.Foreground = new SolidColorBrush(_ink);

        Title = "LumosCast";
        Width = 420;
        // The header art and the sections below it decide the height; hardcoding one
        // clips whenever a section is added, which a fixed non-resizable window hides
        // rather than scrolls.
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(_ground);
        ExtendClientAreaToDecorationsHint = false;

        // Titlebar and taskbar icon on Windows and Linux. macOS takes its dock icon from
        // the bundle's CFBundleIconFile instead — see Info.plist and package-macos.sh.
        try
        {
            using var icon = AssetLoader.Open(new Uri("avares://LumosPresenter.Launcher/Assets/tray-128.png"));
            Icon = new WindowIcon(new Bitmap(icon));
        }
        catch (Exception)
        {
            // The window is perfectly usable without one.
        }

        var body = new StackPanel { Spacing = 0 };
        body.Children.Add(Header());
        body.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(_line),
        });

        var rows = new StackPanel { Spacing = 8, Margin = new Thickness(20, 16, 20, 20) };
        foreach (var link in links)
        {
            rows.Children.Add(LinkRow(link));
        }
        body.Children.Add(rows);
        body.Children.Add(ServerSection());
        body.Children.Add(MicrophoneSection());
        body.Children.Add(ListeningSection());

        Content = body;
        _microphones.SelectionChanged += OnMicrophoneChanged;
        _listeningButton.Click += OnListeningClicked;
        _serverButton.Click += OnServerClicked;
        _logsButton.Click += (_, _) => Reveal.Folder(ServerProcess.LogDirectory);
        SetListeningState(false);
    }

    private Control Header()
    {
        var panel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(20, 24, 20, 20),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        // The full lockup — mark and wordmark together, in the brand's own letterforms
        // rather than the mark beside type set in the system font. Only the width is
        // given so the 2.4:1 art keeps its aspect ratio.
        try
        {
            var art = _dark ? "lockup-dark.png" : "lockup.png";
            using var stream = AssetLoader.Open(new Uri($"avares://LumosPresenter.Launcher/Assets/{art}"));
            panel.Children.Add(new Image
            {
                Source = new Bitmap(stream),
                Width = 248,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
        catch (Exception)
        {
            // Without the art the header would be an empty gap above the status dot,
            // so fall back to setting the name in type.
            panel.Children.Add(new TextBlock
            {
                Text = "LumosCast",
                FontSize = 19,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(_ink),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        var status = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        status.Children.Add(_statusDot);
        status.Children.Add(_statusText);
        panel.Children.Add(status);

        return panel;
    }

    /// <summary>One page: its name, the URL in full, and the two actions.</summary>
    private Control LinkRow(LauncherLink link)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var labels = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = link.Name,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(_ink),
        });
        labels.Children.Add(new TextBlock
        {
            Text = link.Url,
            FontSize = 11,
            FontFamily = new FontFamily("Menlo,Consolas,monospace"),
            Foreground = new SolidColorBrush(_muted),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(labels, 0);
        grid.Children.Add(labels);

        var open = new Button
        {
            Content = "Open",
            Margin = new Thickness(8, 0, 6, 0),
            Padding = new Thickness(14, 6),
            Background = new SolidColorBrush(Brand),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };
        open.Click += (_, _) => Browser.Open(link.Url);
        Grid.SetColumn(open, 1);
        grid.Children.Add(open);

        var copy = new Button
        {
            Content = "Copy",
            Padding = new Thickness(12, 6),
            Background = new SolidColorBrush(_subtle),
            Foreground = new SolidColorBrush(_ink),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            FontSize = 12,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };
        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(link.Url);
                // Momentary confirmation: the operator needs to know the copy landed.
                copy.Content = "Copied";
                await Task.Delay(1200);
                copy.Content = "Copy";
            }
        };
        Grid.SetColumn(copy, 2);
        grid.Children.Add(copy);

        return new Border
        {
            Background = new SolidColorBrush(_card),
            BorderBrush = new SolidColorBrush(_line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(13, 11),
            Child = grid,
        };
    }

    /// <summary>
    /// Start/restart the server, and a way to reach the logs. The server can stop on its
    /// own — a crash in the speech engine, a port taken by something else — and until this
    /// existed the only recovery was to quit the launcher and open the app again, with
    /// nothing on disk to say what had happened.
    /// </summary>
    private Control ServerSection()
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(20, 0, 20, 20) };
        panel.Children.Add(new TextBlock
        {
            Text = "SERVER",
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(_muted),
            FontFamily = new FontFamily("Menlo,Consolas,monospace"),
        });

        // The action and the logs sit side by side: whichever of the two an operator wants
        // after a stop, they want it without hunting.
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_serverButton, 0);
        actions.Children.Add(_serverButton);
        _logsButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_logsButton, 1);
        actions.Children.Add(_logsButton);
        panel.Children.Add(actions);
        panel.Children.Add(_serverNote);
        return panel;
    }

    /// <summary>
    /// Microphone picker. The device belongs to the server, so this reads and writes the
    /// same endpoints the console uses and the two stay in step.
    /// </summary>
    private Control MicrophoneSection()
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(20, 0, 20, 20) };
        panel.Children.Add(new TextBlock
        {
            Text = "MICROPHONE",
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(_muted),
            FontFamily = new FontFamily("Menlo,Consolas,monospace"),
        });
        panel.Children.Add(_microphones);
        panel.Children.Add(_microphoneNote);
        return panel;
    }

    /// <summary>
    /// Start/stop listening. Like the microphone picker this is the server's state, not the
    /// window's, so the tray menu and the web console stay in step with it.
    /// </summary>
    private Control ListeningSection()
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(20, 0, 20, 20) };
        panel.Children.Add(new TextBlock
        {
            Text = "AUTO-DETECTION",
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(_muted),
            FontFamily = new FontFamily("Menlo,Consolas,monospace"),
        });
        panel.Children.Add(_listeningButton);
        panel.Children.Add(_listeningNote);
        return panel;
    }

    private async void OnListeningClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Never flip optimistically: post, then read back what the server actually did.
        _listeningButton.IsEnabled = false;
        var error = await _server.SetListeningAsync(!_listening);
        _listeningNote.Text = error;
        _listeningNote.IsVisible = error is not null;
        _listeningButton.IsEnabled = true;
        // App owns the refresh so the tray badge and this button repaint together;
        // refreshing only here would leave the tray stale until the next poll.
        if (ListeningChanged is { } handler)
        {
            await handler();
        }
        else
        {
            await RefreshListeningAsync();
        }
        // Selecting a device is refused while listening, so keep the picker honest.
        await RefreshMicrophonesAsync();
    }

    /// <summary>Raised after the operator toggles listening from this window.</summary>
    public event Func<Task>? ListeningChanged;

    /// <summary>Reads the server's listening state and repaints the button.</summary>
    public async Task RefreshListeningAsync()
    {
        if (await _server.IsListeningAsync() is { } listening)
        {
            SetListeningState(listening);
        }
    }

    /// <summary>
    /// Paints the toggle for the given state. The microphone picker is disabled while
    /// listening because the server refuses a device change until it is stopped.
    /// </summary>
    public void SetListeningState(bool listening)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _listening = listening;
            _listeningButton.Content = listening ? "Stop listening" : "Start listening";
            _listeningButton.Background = new SolidColorBrush(listening ? Danger : Brand);
            ApplyMicrophoneEnablement();
            // Explain the disabled picker while listening, and take the explanation back
            // down afterwards — leaving it up would tell the operator to stop listening
            // when they already have.
            if (listening)
            {
                _microphoneNote.Text = "Stop listening to change the microphone.";
                _microphoneNote.IsVisible = true;
            }
            else if (_microphoneNote.Text == "Stop listening to change the microphone.")
            {
                _microphoneNote.IsVisible = false;
            }
        });
    }

    /// <summary>
    /// The one rule for whether the picker is usable, from the two things that decide it:
    /// there has to be something to pick, and the server refuses a device change while it
    /// is listening. Both the listening refresh and the device refresh call this, so
    /// whichever lands second cannot undo the other's half of the answer.
    /// </summary>
    private void ApplyMicrophoneEnablement() =>
        _microphones.IsEnabled = _deviceCount > 0 && !_listening;

    /// <summary>Repopulates the device list and marks the server's current selection.</summary>
    public async Task RefreshMicrophonesAsync()
    {
        var (devices, selected) = await _server.GetMicrophonesAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _loadingMicrophones = true;
            _microphones.ItemsSource = devices;
            _microphones.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(MicrophoneDevice.Name));
            _microphones.SelectedItem = devices.FirstOrDefault(d => d.Id == selected);
            _deviceCount = devices.Count;
            ApplyMicrophoneEnablement();
            _loadingMicrophones = false;
        });
    }

    private async void OnMicrophoneChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingMicrophones || _microphones.SelectedItem is not MicrophoneDevice device)
        {
            return;
        }
        var error = await _server.SelectMicrophoneAsync(device.Id);
        _microphoneNote.Text = error ?? $"Listening on {device.Name}.";
        _microphoneNote.IsVisible = true;
        if (error is not null)
        {
            // The change was refused (usually: stop listening first) — show what the
            // server actually has rather than leaving a selection that never took.
            await RefreshMicrophonesAsync();
        }
    }

    /// <summary>Raised when the operator asks to start or restart the server.</summary>
    public event Func<Task<string?>>? ServerActionRequested;

    private async void OnServerClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ServerActionRequested is not { } handler)
        {
            return;
        }
        // Disable for the duration: starting is not instant, and a second click while the
        // first is still working would start a second server to fight over the port.
        _serverButton.IsEnabled = false;
        _serverButton.Content = "Working…";
        var error = await handler();
        _serverNote.Text = error;
        _serverNote.IsVisible = error is not null;
        // The next health poll repaints the button from what the server actually does; a
        // start that failed must not leave it claiming to be running.
    }

    /// <summary>
    /// Reflects server reachability in the header and paints the server button for it.
    /// Called by the health poll, so it is the one place that decides what the button
    /// offers — a start that never came up leaves it offering Start again.
    /// </summary>
    public void SetServerStatus(bool running, ServerState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _statusDot.Fill = new SolidColorBrush(running ? Online : Danger);
            _statusText.Text = running ? "Server running" : "Server not responding";

            switch (state)
            {
                case ServerState.RunningOwned:
                    _serverButton.Content = "Restart server";
                    _serverButton.Background = new SolidColorBrush(_subtle);
                    _serverButton.Foreground = new SolidColorBrush(_ink);
                    _serverButton.IsEnabled = true;
                    ClearOwnedNote();
                    break;
                case ServerState.RunningExternal:
                    // Started outside this launcher, so it is not this launcher's to stop
                    // or restart — saying so beats a button that would do nothing.
                    _serverButton.Content = "Restart server";
                    _serverButton.Background = new SolidColorBrush(_subtle);
                    _serverButton.Foreground = new SolidColorBrush(_muted);
                    _serverButton.IsEnabled = false;
                    _serverNote.Text = "A server started outside the launcher is running on this port.";
                    _serverNote.IsVisible = true;
                    break;
                case ServerState.Starting:
                    // The child is alive but not answering yet: migrations, the speech
                    // model and the api.bible warm-up all run before the first response.
                    _serverButton.Content = "Starting…";
                    _serverButton.Background = new SolidColorBrush(_subtle);
                    _serverButton.Foreground = new SolidColorBrush(_muted);
                    _serverButton.IsEnabled = false;
                    _statusText.Text = "Server starting…";
                    _statusDot.Fill = new SolidColorBrush(_muted);
                    ClearOwnedNote();
                    break;
                default:
                    _serverButton.Content = "Start server";
                    _serverButton.Background = new SolidColorBrush(Brand);
                    _serverButton.Foreground = Brushes.White;
                    _serverButton.IsEnabled = true;
                    ClearOwnedNote();
                    break;
            }
        });
    }

    /// <summary>
    /// Takes down the external-server explanation once it no longer applies, while leaving
    /// a failure message from the operator's own start attempt on screen to be read.
    /// </summary>
    private void ClearOwnedNote()
    {
        if (_serverNote.Text == "A server started outside the launcher is running on this port.")
        {
            _serverNote.IsVisible = false;
        }
    }
}
