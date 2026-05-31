using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BlueMarble.Imagery;

public sealed class BlueMarbleProvider : IImageryProvider
{
    private readonly GibsWmsClient _client;
    private readonly TileCache _cache;
    private readonly ILogger<BlueMarbleProvider> _logger;
    private readonly Func<DateTimeOffset> _utcNow;

    public BlueMarbleProvider(
        GibsWmsClient client,
        TileCache cache,
        ILogger<BlueMarbleProvider> logger,
        Func<DateTimeOffset>? utcNow = null)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public ImageryLayer Layer => ImageryLayers.BlueMarbleNextGeneration;

    public async Task<string> EnsureEquirectangularAsync(int width, int height, CancellationToken cancellationToken)
    {
        var date = ImageryLayers.BlueMarbleMonthFor(_utcNow());

        if (_cache.TryGet(Layer, date, width, height, out var existing))
        {
            _logger.LogInformation("Blue Marble cache hit {Date} {W}x{H}", date, width, height);
            return existing;
        }

        await using var stream = await _client.GetEquirectangularAsync(Layer, date, width, height, cancellationToken)
            .ConfigureAwait(false);
        return await _cache.WriteAsync(Layer, date, width, height, stream, cancellationToken)
            .ConfigureAwait(false);
    }
}
