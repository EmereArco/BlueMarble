using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BlueMarble.Imagery;

public sealed class TileCache
{
    private readonly ILogger<TileCache> _logger;
    private readonly string _root;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public TileCache(ILogger<TileCache> logger)
    {
        _logger = logger;
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BlueMarble", "cache");
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public string PathFor(ImageryLayer layer, DateOnly date, int width, int height)
    {
        var name = $"{layer.Id}_{date:yyyyMMdd}_{width}x{height}.{layer.FileExtension}";
        return Path.Combine(_root, name);
    }

    public bool TryGet(ImageryLayer layer, DateOnly date, int width, int height, out string path)
    {
        path = PathFor(layer, date, width, height);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    public async Task<string> WriteAsync(
        ImageryLayer layer,
        DateOnly date,
        int width,
        int height,
        Stream content,
        CancellationToken cancellationToken)
    {
        var finalPath = PathFor(layer, date, width, height);
        var tempPath = finalPath + ".tmp";

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using (var output = File.Create(tempPath))
            {
                await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }
            File.Move(tempPath, finalPath);
            _logger.LogInformation("Cached {Layer} {Date} {Width}x{Height} → {Path}",
                layer.Id, date, width, height, finalPath);
            return finalPath;
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* swallow */ }
            }
            throw;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void PruneOlderThan(TimeSpan maxAge)
    {
        var threshold = DateTime.UtcNow - maxAge;
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root))
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < threshold)
                {
                    info.Delete();
                    _logger.LogInformation("Pruned stale cache entry {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache prune failed");
        }
    }
}
