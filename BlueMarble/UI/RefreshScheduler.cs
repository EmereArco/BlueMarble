using System;
using System.Threading;
using System.Threading.Tasks;
using BlueMarble.Settings;
using Microsoft.Extensions.Logging;

namespace BlueMarble.UI;

public sealed class RefreshScheduler : IDisposable
{
    private readonly IRefreshController _refresh;
    private readonly SettingsStore _settings;
    private readonly ILogger<RefreshScheduler> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private TimeSpan _interval;

    public RefreshScheduler(
        IRefreshController refresh,
        SettingsStore settings,
        ILogger<RefreshScheduler> logger)
    {
        _refresh = refresh;
        _settings = settings;
        _logger = logger;
        _interval = ClampInterval(settings.Current.RefreshInterval);
        _settings.Changed += OnSettingsChanged;
    }

    public void Start()
    {
        if (_loop is not null) return;
        _loop = Task.Run(() => RunAsync(_cts.Token));
        _logger.LogInformation("Refresh scheduler started (interval {Interval})", _interval);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        if (!_refresh.IsPaused)
        {
            try
            {
                await _refresh.ForceRefreshAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Initial refresh failed");
            }
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_refresh.IsPaused)
            {
                _logger.LogInformation("Scheduled refresh skipped (paused)");
                continue;
            }

            try
            {
                await _refresh.ForceRefreshAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled refresh failed");
            }
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        var next = ClampInterval(settings.RefreshInterval);
        if (next != _interval)
        {
            _logger.LogInformation("Refresh interval changed {Old} -> {New}", _interval, next);
            _interval = next;
        }
    }

    private static TimeSpan ClampInterval(TimeSpan value)
        => value < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : value;

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        try { _cts.Cancel(); } catch { }
        _cts.Dispose();
    }
}
