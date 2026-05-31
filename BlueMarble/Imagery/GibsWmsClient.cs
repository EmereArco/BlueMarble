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
    private static readonly TimeSpan CapabilitiesTtl = TimeSpan.FromHours(12);

    private readonly HttpClient _http;
    private readonly ILogger<GibsWmsClient> _logger;
    private readonly SemaphoreSlim _capsGate = new(1, 1);

    private string? _capabilitiesXml;
    private DateTimeOffset _capabilitiesFetchedAt;

    public GibsWmsClient(HttpClient http, ILogger<GibsWmsClient> logger)
    {
        _http = http;
        _logger = logger;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BlueMarble-Desktop/0.1 (+https://github.com/local)");
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Resolves the latest available date for a layer from the service's GetCapabilities
    /// time Dimension default. Returns <c>null</c> for static layers (no time dimension)
    /// or when capabilities cannot be fetched/parsed; callers should fall back accordingly.
    /// </summary>
    public async Task<DateOnly?> GetLatestDateAsync(ImageryLayer layer, CancellationToken cancellationToken)
    {
        var xml = await GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        if (xml is null)
        {
            return null;
        }

        try
        {
            var latest = GibsCapabilities.ParseLatestTime(xml, layer.GibsLayerName);
            if (latest is not null)
            {
                _logger.LogInformation("GIBS latest date for {Layer} = {Date}", layer.GibsLayerName, latest);
            }
            return latest;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse latest date for {Layer}", layer.GibsLayerName);
            return null;
        }
    }

    private async Task<string?> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        if (_capabilitiesXml is not null &&
            DateTimeOffset.UtcNow - _capabilitiesFetchedAt < CapabilitiesTtl)
        {
            return _capabilitiesXml;
        }

        await _capsGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_capabilitiesXml is not null &&
                DateTimeOffset.UtcNow - _capabilitiesFetchedAt < CapabilitiesTtl)
            {
                return _capabilitiesXml;
            }

            var url = $"{BaseUrl}?SERVICE=WMS&REQUEST=GetCapabilities&VERSION=1.3.0";
            _logger.LogInformation("Fetching GIBS WMS GetCapabilities");
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            _capabilitiesXml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _capabilitiesFetchedAt = DateTimeOffset.UtcNow;
            return _capabilitiesXml;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not fetch GIBS GetCapabilities; will use fallback dates");
            return _capabilitiesXml; // may be a stale-but-usable copy, or null
        }
        finally
        {
            _capsGate.Release();
        }
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
