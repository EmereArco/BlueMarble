using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BlueMarble.Settings;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BlueMarble.UI;

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
        var menu = new MenuFlyout();

        var pauseItem = new ToggleMenuFlyoutItem { Text = "Pausa", IsChecked = _refresh.IsPaused };
        pauseItem.Click += (_, _) =>
        {
            if (pauseItem.IsChecked) _refresh.Pause(); else _refresh.Resume();
            _logger.LogInformation("Refresh paused = {Paused}", _refresh.IsPaused);
        };
        menu.Items.Add(pauseItem);

        // Keep the checkmark in sync with the actual state every time the menu opens.
        menu.Opening += (_, _) => pauseItem.IsChecked = _refresh.IsPaused;

        var refreshItem = new MenuFlyoutItem { Text = "Aggiorna ora" };
        refreshItem.Click += async (_, _) =>
        {
            try
            {
                _logger.LogInformation("Manual refresh requested from tray");
                await _refresh.ForceRefreshAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Force refresh failed");
            }
        };
        menu.Items.Add(refreshItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var settingsItem = new MenuFlyoutItem { Text = "Impostazioni…" };
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = "Esci" };
        exitItem.Click += (_, _) =>
        {
            _logger.LogInformation("Exit requested from tray");
            App.Current.Exit();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OpenSettings()
    {
        var path = _settings.FilePath;
        try
        {
            // Settings.json is written on first Load(); ensure it exists before opening.
            if (!File.Exists(path))
            {
                _settings.Save();
            }

            _logger.LogInformation("Opening settings file {Path}", path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open settings file {Path}", path);
            // Fall back to revealing the file in Explorer if no .json handler is set.
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
