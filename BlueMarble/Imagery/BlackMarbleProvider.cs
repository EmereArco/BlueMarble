using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BlueMarble.Imagery;

public sealed class BlackMarbleProvider : IImageryProvider
{
    private readonly GibsWmsClient _client;
    private readonly TileCache _cache;
    private readonly ILogger<BlackMarbleProvider> _logger;

    public BlackMarbleProvider(
        GibsWmsClient client,
        TileCache cache,
        ILogger<BlackMarbleProvider> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
    }

    public ImageryLayer Layer => ImageryLayers.BlackMarble;

    public async Task<string> EnsureEquirectangularAsync(int width, int height, CancellationToken cancellationToken)
    {
        var date = ImageryLayers.BlackMarbleDate;

        if (_cache.TryGet(Layer, date, width, height, out var existing))
        {
            _logger.LogInformation("Black Marble cache hit {Date} {W}x{H}", date, width, height);
            return existing;
        }

        await using var stream = await _client.GetEquirectangularAsync(Layer, date, width, height, cancellationToken)
            .ConfigureAwait(false);
        return await _cache.WriteAsync(Layer, date, width, height, stream, cancellationToken)
            .ConfigureAwait(false);
    }
}
