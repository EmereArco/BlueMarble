using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BlueMarble.Imagery;

public sealed class GibsWmsClient
{
    private const string BaseUrl = "https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi";

    private readonly HttpClient _http;
    private readonly ILogger<GibsWmsClient> _logger;

    public GibsWmsClient(HttpClient http, ILogger<GibsWmsClient> logger)
    {
        _http = http;
        _logger = logger;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BlueMarble-Desktop/0.1 (+https://github.com/local)");
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<Stream> GetEquirectangularAsync(
        ImageryLayer layer,
        DateOnly time,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width and height must be positive");
        }

        var query =
            "SERVICE=WMS" +
            "&REQUEST=GetMap" +
            "&VERSION=1.3.0" +
            $"&LAYERS={Uri.EscapeDataString(layer.GibsLayerName)}" +
            "&STYLES=" +
            "&CRS=EPSG:4326" +
            "&BBOX=-90,-180,90,180" +
            $"&WIDTH={width.ToString(CultureInfo.InvariantCulture)}" +
            $"&HEIGHT={height.ToString(CultureInfo.InvariantCulture)}" +
            $"&FORMAT={Uri.EscapeDataString(layer.Format)}" +
            $"&TIME={time:yyyy-MM-dd}";

        var url = $"{BaseUrl}?{query}";

        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _logger.LogInformation("GIBS WMS GetMap {Layer} {Time} {W}x{H} (attempt {Attempt})",
                    layer.GibsLayerName, time, width, height, attempt);

                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"GIBS WMS returned {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (contentType is not null && contentType.StartsWith("text/xml", StringComparison.OrdinalIgnoreCase))
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    throw new HttpRequestException($"GIBS WMS service exception: {body}");
                }

                var ms = new MemoryStream(capacity: 4 * 1024 * 1024);
                await response.Content.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                ms.Position = 0;
                return ms;
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is not OperationCanceledException)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(ex, "GIBS request failed, retrying in {Delay}", delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
