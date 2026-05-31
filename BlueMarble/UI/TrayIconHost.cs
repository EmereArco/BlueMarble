using System;
using System.Threading.Tasks;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    private TaskbarIcon? _trayIcon;
    private Window? _hiddenHost;

    public TrayIconHost(ILogger<TrayIconHost> logger, IRefreshController refresh)
    {
        _logger = logger;
        _refresh = refresh;
    }

    public void Show()
    {
        _hiddenHost = new Window { Title = "BlueMarble" };
        _hiddenHost.AppWindow.Hide();

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "BlueMarble Desktop",
            ContextFlyout = BuildMenu(),
        };
        _trayIcon.ForceCreate();

        _logger.LogInformation("Tray icon initialized");
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

        var refreshItem = new MenuFlyoutItem { Text = "Aggiorna ora" };
        refreshItem.Click += async (_, _) =>
        {
            try
            {
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
        settingsItem.Click += (_, _) => _logger.LogInformation("Settings window requested (phase 7)");
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

    public void Dispose()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _hiddenHost?.Close();
        _hiddenHost = null;
    }
}
