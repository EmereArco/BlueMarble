using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using BlueMarble.Settings;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BlueMarble.UI;

/// <summary>
/// Minimal ICommand wrapper. H.NotifyIcon's default PopupMenu mode (the only mode that
/// works reliably in unpackaged apps) builds a native Win32 menu and invokes each
/// MenuFlyoutItem's <see cref="ICommand"/> — it does NOT raise the Click event. So every
/// tray menu item must expose its action as a Command, not a Click handler.
/// </summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute) => _execute = execute;

    // Always executable; the event is required by ICommand but never raised.
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}

public interface IRefreshController
{
    bool IsPaused { get; }
    Task ForceRefreshAsync();
    void Pause();
    void Resume();
}

public sealed class TrayIconHost : IDisposable
{
    private readonly ILogger<TrayIconHost> _logger;
    private readonly IRefreshController _refresh;
    private readonly SettingsStore _settings;

    private TaskbarIcon? _trayIcon;
    private Window? _hiddenHost;

    public TrayIconHost(ILogger<TrayIconHost> logger, IRefreshController refresh, SettingsStore settings)
    {
        _logger = logger;
        _refresh = refresh;
        _settings = settings;
    }

    public void Show()
    {
        _hiddenHost = new Window { Title = "BlueMarble" };

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "BlueMarble.ico");
        var hasIcon = File.Exists(iconPath);
        if (hasIcon)
        {
            try
            {
                _hiddenHost.AppWindow.SetIcon(iconPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not set window icon from {Path}", iconPath);
            }
        }
        else
        {
            _logger.LogWarning("App icon not found at {Path}", iconPath);
        }

        _hiddenHost.AppWindow.Hide();

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "BlueMarble Desktop",
            ContextFlyout = BuildMenu(),
        };

        if (hasIcon)
        {
            _trayIcon.IconSource = new BitmapImage(new Uri(iconPath));
        }

        _trayIcon.ForceCreate();

        _logger.LogInformation("Tray icon initialized (icon: {HasIcon})", hasIcon);
    }

    private MenuFlyout BuildMenu()
    {
        // NOTE: items use Command, not Click — see RelayCommand for why.
        var menu = new MenuFlyout();

        var pauseItem = new ToggleMenuFlyoutItem { Text = "Pausa", IsChecked = _refresh.IsPaused };
        pauseItem.Command = new RelayCommand(() =>
        {
            if (_refresh.IsPaused) _refresh.Resume(); else _refresh.Pause();
            pauseItem.IsChecked = _refresh.IsPaused;
            _logger.LogInformation("Refresh paused = {Paused}", _refresh.IsPaused);
        });
        menu.Items.Add(pauseItem);

        // Keep the checkmark in sync with the actual state every time the menu opens.
        menu.Opening += (_, _) => pauseItem.IsChecked = _refresh.IsPaused;

        var refreshItem = new MenuFlyoutItem { Text = "Aggiorna ora" };
        refreshItem.Command = new RelayCommand(() =>
        {
            _logger.LogInformation("Manual refresh requested from tray");
            _ = ForceRefreshSafeAsync();
        });
        menu.Items.Add(refreshItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var settingsItem = new MenuFlyoutItem { Text = "Impostazioni…" };
        settingsItem.Command = new RelayCommand(OpenSettings);
        menu.Items.Add(settingsItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = "Esci" };
        exitItem.Command = new RelayCommand(() =>
        {
            _logger.LogInformation("Exit requested from tray");
            App.Current.Exit();
        });
        menu.Items.Add(exitItem);

        return menu;
    }

    private async Task ForceRefreshSafeAsync()
    {
        try
        {
            await _refresh.ForceRefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Force refresh failed");
        }
    }

    private void OpenSettings()
    {
        var path = _settings.FilePath;

        // Settings.json is written on first Load(); ensure it exists before opening.
        if (!File.Exists(path))
        {
            _settings.Save();
        }

        // Open with Notepad explicitly: many systems have no default handler for .json,
        // so a shell-execute on the file silently fails. Notepad always ships with Windows.
        try
        {
            _logger.LogInformation("Opening settings file in Notepad {Path}", path);
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notepad failed; trying default handler for {Path}", path);
        }

        // Fall back to the default .json handler, then to revealing it in Explorer.
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open settings file {Path}; revealing in Explorer", path);
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "Could not reveal settings file in Explorer");
            }
        }
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _hiddenHost?.Close();
        _hiddenHost = null;
    }
}
