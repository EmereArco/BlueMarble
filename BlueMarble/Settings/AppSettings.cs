using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlueMarble.Wallpaper;
using Microsoft.Extensions.Logging;

namespace BlueMarble.Settings;

public sealed record AppSettings
{
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromMinutes(5);

    public int OutputWidth { get; init; } = 3840;
    public int OutputHeight { get; init; } = 2160;

    public bool LaunchAtLogin { get; init; } = false;
    public bool PauseOnBattery { get; init; } = false;
    public bool PauseOnFullscreenApp { get; init; } = true;

    public bool UseDailyTrueColor { get; init; } = false;
    public bool ShowCityLights { get; init; } = true;

    public double TerminatorSoftnessDegrees { get; init; } = 22.0;

    public double OceanGlintStrength { get; init; } = 0.6;
    public double OceanGlintRadiusDegrees { get; init; } = 35.0;

    /// <summary>Contrast stretch applied to the composite (1.0 = none). Slightly &gt;1
    /// widens the day/night brightness separation.</summary>
    public double DayNightContrast { get; init; } = 1.15;

    public WallpaperPosition WallpaperPosition { get; init; } = WallpaperPosition.Fit;

    public static AppSettings Default { get; } = new();
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;

public sealed class SettingsStore : IDisposable
{
    private readonly ILogger<SettingsStore> _logger;
    private readonly string _path;
    private AppSettings _current = AppSettings.Default;

    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounce;
    private System.Threading.Timer? _saveFlagReset;
    private volatile bool _savingInternally;

    public SettingsStore(ILogger<SettingsStore> logger)
    {
        _logger = logger;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BlueMarble");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public AppSettings Current => _current;

    /// <summary>Absolute path of the settings.json file backing this store.</summary>
    public string FilePath => _path;

    public event EventHandler<AppSettings>? Changed;

    /// <summary>
    /// Watches settings.json for external edits (e.g. the user editing it via the tray's
    /// "Impostazioni" command) and reloads on change, raising <see cref="Changed"/> so the
    /// rest of the app can react. Writes coming from <see cref="Save"/> are ignored.
    /// </summary>
    public void StartWatching()
    {
        if (_watcher is not null)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(_path)!;
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
            _logger.LogInformation("Watching settings file for changes: {Path}", _path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start settings file watcher");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_savingInternally)
        {
            return;
        }

        // Editors often fire several events per save; debounce and let the file settle.
        _debounce?.Dispose();
        _debounce = new System.Threading.Timer(_ => ReloadFromDisk(), null, 300, System.Threading.Timeout.Infinite);
    }

    private void ReloadFromDisk()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            AppSettings? loaded;
            using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                loaded = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings);
            }

            var next = loaded ?? AppSettings.Default;
            if (next.Equals(_current))
            {
                return;
            }

            _current = next;
            _logger.LogInformation("Settings reloaded from disk after external edit");
            Changed?.Invoke(this, _current);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload settings after external edit");
        }
    }

    public void Load()
    {
        if (!File.Exists(_path))
        {
            _logger.LogInformation("No settings file at {Path}, using defaults", _path);
            Save();
            return;
        }

        try
        {
            using var stream = File.OpenRead(_path);
            var loaded = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings);
            _current = loaded ?? AppSettings.Default;
            _logger.LogInformation("Settings loaded from {Path}", _path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings, falling back to defaults");
            _current = AppSettings.Default;
        }
    }

    public void Save()
    {
        try
        {
            // Flag the write so the file watcher does not treat our own save as an
            // external edit and reload-loop.
            _savingInternally = true;
            using (var stream = File.Create(_path))
            {
                JsonSerializer.Serialize(stream, _current, SettingsJsonContext.Default.AppSettings);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist settings to {Path}", _path);
        }
        finally
        {
            // Clear shortly after, once the watcher has had a chance to fire and skip.
            _saveFlagReset?.Dispose();
            _saveFlagReset = new System.Threading.Timer(
                _ => _savingInternally = false, null, 500, System.Threading.Timeout.Infinite);
        }
    }

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        var next = mutate(_current);
        if (next.Equals(_current))
        {
            return;
        }
        _current = next;
        Save();
        Changed?.Invoke(this, _current);
    }

    public void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Renamed -= OnFileChanged;
            _watcher.Dispose();
            _watcher = null;
        }
        _debounce?.Dispose();
        _saveFlagReset?.Dispose();
    }
}
