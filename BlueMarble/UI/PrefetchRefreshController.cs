using System;
using System.IO;
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
        if (IsPaused)
        {
            _logger.LogInformation("Refresh requested but controller is paused");
            return;
        }

        if (!await _gate.WaitAsync(0).ConfigureAwait(false))
        {
            _logger.LogInformation("Refresh already in progress, skipping");
            return;
        }

        try
        {
            var current = _settings.Current;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            var dayPath = await _day.EnsureEquirectangularAsync(
                current.OutputWidth, current.OutputHeight, cts.Token).ConfigureAwait(false);
            var nightPath = await _night.EnsureEquirectangularAsync(
                current.OutputWidth, current.OutputHeight, cts.Token).ConfigureAwait(false);

            var subsolar = SolarGeometry.GetSubsolarPoint(DateTimeOffset.UtcNow);
            var outputPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlueMarble", "current.png");

            var compositionOptions = new CompositionOptions(
                TerminatorSoftnessDegrees: current.TerminatorSoftnessDegrees,
                OceanGlintStrength: current.OceanGlintStrength,
                OceanGlintRadiusDegrees: current.OceanGlintRadiusDegrees);

            var produced = await _composer.ComposeAsync(
                dayPath, nightPath,
                subsolar,
                compositionOptions,
                current.OutputWidth, current.OutputHeight,
                outputPath,
                cts.Token).ConfigureAwait(false);

            LastFramePath = produced;
            _logger.LogInformation(
                "Frame composed at {Path} (subsolar lat={Lat:F2} lon={Lon:F2})",
                produced, subsolar.LatitudeDegrees, subsolar.LongitudeDegrees);

            var applied = _wallpaper.Apply(produced, current.WallpaperPosition);
            _logger.LogInformation("Wallpaper apply result: {Result}", applied);
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
}
