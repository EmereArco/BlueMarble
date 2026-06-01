using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BlueMarble.Imagery;

/// <summary>
/// Daily true-color daytime imagery (MODIS Terra Corrected Reflectance). Unlike the static
/// Blue Marble composite this reflects current cloud cover and seasonal snow/ice, but it has
/// no-data gaps where the satellite did not observe in daylight that day (polar night, swath
/// edges) — those areas come back near-black and are meant to be filled by a base layer.
/// The latest published date is resolved from GetCapabilities, falling back to a recent date
/// (daily products lag real time by a couple of days).
/// </summary>
public sealed class ModisTrueColorProvider : IImageryProvider
{
    private readonly GibsWmsClient _client;
    private readonly TileCache _cache;
    private readonly ILogger<ModisTrueColorProvider> _logger;
    private readonly Func<DateTimeOffset> _utcNow;

    public ModisTrueColorProvider(
        GibsWmsClient client,
        TileCache cache,
        ILogger<ModisTrueColorProvider> logger,
        Func<DateTimeOffset>? utcNow = null)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public ImageryLayer Layer => ImageryLayers.ModisTerraTrueColor;

    public async Task<string> EnsureEquirectangularAsync(int width, int height, CancellationToken cancellationToken)
    {
        var date = await _client.GetLatestDateAsync(Layer, cancellationToken).ConfigureAwait(false)
                   ?? ImageryLayers.LatestDailyImageryDate(_utcNow());

        if (_cache.TryGet(Layer, date, width, height, out var existing))
        {
            _logger.LogInformation("MODIS true-color cache hit {Date} {W}x{H}", date, width, height);
            return existing;
        }

        await using var stream = await _client.GetEquirectangularAsync(Layer, date, width, height, cancellationToken)
            .ConfigureAwait(false);
        return await _cache.WriteAsync(Layer, date, width, height, stream, cancellationToken)
            .ConfigureAwait(false);
    }
}
