using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueMarble.Composition;
using BlueMarble.Imagery;
using BlueMarble.Settings;
using BlueMarble.Wallpaper;
using Microsoft.Extensions.Logging;

namespace BlueMarble.UI;

public sealed class PrefetchRefreshController : IRefreshController
{
    private readonly BlueMarbleProvider _day;
    private readonly BlackMarbleProvider _night;
    private readonly FrameComposer _composer;
    private readonly WallpaperApplier _wallpaper;
    private readonly SettingsStore _settings;
    private readonly ILogger<PrefetchRefreshController> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PrefetchRefreshController(
        BlueMarbleProvider day,
        BlackMarbleProvider night,
        FrameComposer composer,
        WallpaperApplier wallpaper,
        SettingsStore settings,
        ILogger<PrefetchRefreshController> logger)
    {
        _day = day;
        _night = night;
        _composer = composer;
        _wallpaper = wallpaper;
        _settings = settings;
        _logger = logger;
    }

    public bool IsPaused { get; private set; }

    public string? LastFramePath { get; private set; }

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;

    public async Task ForceRefreshAsync()
    {
        // A "force" refresh is explicit (manual tray command or scheduler tick that
        // already honored the pause state) — it always runs. Pause is enforced by the
        // RefreshScheduler for the periodic loop, not here.
        if (!await _gate.WaitAsync(0).ConfigureAwait(false))
        {
            _logger.LogInformation("Refresh already in progress, skipping");
            return;
        }

        try
        {
            var current = _settings.Current;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            var monitors = MonitorEnumerator.Enumerate();
            var (masterW, masterH) = ChooseMasterSize(monitors, current);

            var dayPath = await _day.EnsureEquirectangularAsync(
                masterW, masterH, cts.Token).ConfigureAwait(false);
            var nightPath = await _night.EnsureEquirectangularAsync(
                masterW, masterH, cts.Token).ConfigureAwait(false);

            var subsolar = SolarGeometry.GetSubsolarPoint(DateTimeOffset.UtcNow);
            var outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlueMarble");
            var masterPath = Path.Combine(outputDir, "current.png");

            var compositionOptions = new CompositionOptions(
                TerminatorSoftnessDegrees: current.TerminatorSoftnessDegrees,
                OceanGlintStrength: current.OceanGlintStrength,
                OceanGlintRadiusDegrees: current.OceanGlintRadiusDegrees,
                Contrast: current.DayNightContrast);

            var produced = await _composer.ComposeAsync(
                dayPath, nightPath,
                subsolar,
                compositionOptions,
                masterW, masterH,
                masterPath,
                cts.Token).ConfigureAwait(false);

            LastFramePath = produced;
            _logger.LogInformation(
                "Master frame {W}x{H} composed at {Path} (subsolar lat={Lat:F2} lon={Lon:F2}); {Count} monitor(s)",
                masterW, masterH, produced, subsolar.LatitudeDegrees, subsolar.LongitudeDegrees, monitors.Count);

            if (monitors.Count == 0)
            {
                var applied = _wallpaper.Apply(produced, current.WallpaperPosition);
                _logger.LogInformation("Wallpaper apply (single) result: {Result}", applied);
                return;
            }

            var perMonitor = new Dictionary<string, string>(monitors.Count);
            for (var i = 0; i < monitors.Count; i++)
            {
                var m = monitors[i];
                var perPath = Path.Combine(outputDir, $"current_m{i}.png");
                await Task.Run(
                    () => MonitorFrame.RenderLetterbox(produced, m.Width, m.Height, perPath),
                    cts.Token).ConfigureAwait(false);
                perMonitor[m.Id] = perPath;
                _logger.LogInformation("Per-monitor frame {Index} {W}x{H} -> {Path}",
                    i, m.Width, m.Height, perPath);
            }

            var appliedPm = _wallpaper.ApplyPerMonitor(perMonitor);
            _logger.LogInformation("Wallpaper apply (per-monitor) result: {Result}", appliedPm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static (int Width, int Height) ChooseMasterSize(
        IReadOnlyList<MonitorInfo> monitors, AppSettings settings)
    {
        const int Cap = 8192;
        if (monitors.Count == 0)
        {
            return (settings.OutputWidth, settings.OutputHeight);
        }

        var needed = monitors.Max(m => Math.Max(m.Width, m.Height * 2));
        var w = Math.Clamp(needed, 1920, Cap);
        if ((w & 1) != 0) w++;
        return (w, w / 2);
    }
}
