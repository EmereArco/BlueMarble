using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BlueMarble.Imagery;

/// <summary>
/// Day texture for the "daily true color" mode: daily MODIS imagery composited over the static
/// Blue Marble base, so MODIS no-data gaps (polar night, unobserved swaths) are filled in. If
/// MODIS can't be fetched (offline, service hiccup) the Blue Marble base is returned unchanged,
/// so the wallpaper still updates.
/// </summary>
public sealed class HybridDayProvider : IImageryProvider
{
    private readonly BlueMarbleProvider _base;
    private readonly ModisTrueColorProvider _modis;
    private readonly TileCache _cache;
    private readonly ILogger<HybridDayProvider> _logger;

    public HybridDayProvider(
        BlueMarbleProvider baseProvider,
        ModisTrueColorProvider modis,
        TileCache cache,
        ILogger<HybridDayProvider> logger)
    {
        _base = baseProvider;
        _modis = modis;
        _cache = cache;
        _logger = logger;
    }

    // Reported layer is the Blue Marble base; the hybrid output is cached separately by file name.
    public ImageryLayer Layer => _base.Layer;

    public async Task<string> EnsureEquirectangularAsync(int width, int height, CancellationToken cancellationToken)
    {
        var basePath = await _base.EnsureEquirectangularAsync(width, height, cancellationToken).ConfigureAwait(false);

        string modisPath;
        try
        {
            modisPath = await _modis.EnsureEquirectangularAsync(width, height, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MODIS true-color fetch failed; using Blue Marble base only");
            return basePath;
        }

        var combinedPath = Path.Combine(
            _cache.Root, $"true-color-hybrid_{Path.GetFileNameWithoutExtension(modisPath)}.png");

        // Reuse the cached composite only if it is at least as new as both inputs (so a new
        // MODIS day or a new Blue Marble month forces a recompose).
        if (File.Exists(combinedPath))
        {
            var combinedAt = File.GetLastWriteTimeUtc(combinedPath);
            if (combinedAt >= File.GetLastWriteTimeUtc(basePath) &&
                combinedAt >= File.GetLastWriteTimeUtc(modisPath))
            {
                _logger.LogInformation("True-color hybrid cache hit {Path}", combinedPath);
                return combinedPath;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => TrueColorOverlay.Combine(basePath, modisPath, combinedPath), cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation("Composed true-color hybrid {W}x{H} -> {Path}", width, height, combinedPath);
        return combinedPath;
    }
}
