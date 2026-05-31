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

    public WallpaperPosition WallpaperPosition { get; init; } = WallpaperPosition.Fit;

    public static AppSettings Default { get; } = new();
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;

public sealed class SettingsStore
{
    private readonly ILogger<SettingsStore> _logger;
    private readonly string _path;
    private AppSettings _current = AppSettings.Default;

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

    public event EventHandler<AppSettings>? Changed;

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
            using var stream = File.Create(_path);
            JsonSerializer.Serialize(stream, _current, SettingsJsonContext.Default.AppSettings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist settings to {Path}", _path);
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
}
