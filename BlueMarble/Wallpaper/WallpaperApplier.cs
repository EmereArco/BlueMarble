using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BlueMarble.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BlueMarble.Wallpaper;

public sealed class WallpaperApplier
{
    private const string DesktopRegistryPath = @"Control Panel\Desktop";
    private const string ColorsRegistryPath = @"Control Panel\Colors";
    private const string BlackBackground = "0 0 0";

    private readonly ILogger<WallpaperApplier> _logger;

    public WallpaperApplier(ILogger<WallpaperApplier> logger)
    {
        _logger = logger;
    }

    public bool Apply(string imagePath, WallpaperPosition position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        if (!File.Exists(imagePath))
        {
            _logger.LogError("Wallpaper image not found at {Path}", imagePath);
            return false;
        }

        var absolutePath = Path.GetFullPath(imagePath);

        WritePositionRegistry(position);
        WriteBlackBackground();

        if (TryApplyPerMonitor(absolutePath, position))
        {
            _logger.LogInformation("Wallpaper applied per-monitor (IDesktopWallpaper) {Path}", absolutePath);
            return true;
        }

        if (TryApplySystemParameters(absolutePath))
        {
            _logger.LogInformation("Wallpaper applied via SystemParametersInfo {Path}", absolutePath);
            return true;
        }

        _logger.LogError("All wallpaper application strategies failed for {Path}", absolutePath);
        return false;
    }

    private void WritePositionRegistry(WallpaperPosition position)
    {
        try
        {
            var values = position.ToRegistryValues();
            using var key = Registry.CurrentUser.OpenSubKey(DesktopRegistryPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(DesktopRegistryPath);
            key.SetValue("WallpaperStyle", values.WallpaperStyle, RegistryValueKind.String);
            key.SetValue("TileWallpaper", values.TileWallpaper, RegistryValueKind.String);
            _logger.LogInformation("WallpaperStyle={Style} TileWallpaper={Tile}",
                values.WallpaperStyle, values.TileWallpaper);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write wallpaper position registry values");
        }
    }

    private void WriteBlackBackground()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ColorsRegistryPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(ColorsRegistryPath);
            key.SetValue("Background", BlackBackground, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set black desktop background colour");
        }
    }

    public bool ApplyPerMonitor(IReadOnlyDictionary<string, string> perMonitor)
    {
        ArgumentNullException.ThrowIfNull(perMonitor);
        if (perMonitor.Count == 0)
        {
            _logger.LogWarning("ApplyPerMonitor called with empty mapping");
            return false;
        }

        WritePositionRegistry(WallpaperPosition.Center);
        WriteBlackBackground();

        IDesktopWallpaper? com = null;
        try
        {
            com = DesktopWallpaperFactory.Create();
            com.SetPosition(DesktopWallpaperPosition.Center);
            com.SetBackgroundColor(0x000000);

            var applied = 0;
            foreach (var (monitorId, imagePath) in perMonitor)
            {
                if (string.IsNullOrEmpty(monitorId)) continue;
                if (!File.Exists(imagePath))
                {
                    _logger.LogWarning("Per-monitor image missing for {Id}: {Path}", monitorId, imagePath);
                    continue;
                }
                try
                {
                    com.SetWallpaper(monitorId, Path.GetFullPath(imagePath));
                    applied++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SetWallpaper failed for monitor {Id}", monitorId);
                }
            }
            _logger.LogInformation("Applied per-monitor wallpaper on {Applied}/{Total} monitors",
                applied, perMonitor.Count);
            return applied > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplyPerMonitor failed");
            return false;
        }
        finally
        {
            if (com is not null) Marshal.ReleaseComObject(com);
        }
    }

    private bool TryApplyPerMonitor(string absolutePath, WallpaperPosition position)
    {
        try
        {
            var com = DesktopWallpaperFactory.Create();
            try
            {
                com.SetPosition(DesktopWallpaperFactory.Map(position));
                com.SetBackgroundColor(0x000000);

                var count = com.GetMonitorDevicePathCount();
                if (count == 0)
                {
                    com.SetWallpaper(null, absolutePath);
                }
                else
                {
                    for (uint i = 0; i < count; i++)
                    {
                        var monitorId = com.GetMonitorDevicePathAt(i);
                        if (string.IsNullOrEmpty(monitorId)) continue;
                        com.SetWallpaper(monitorId, absolutePath);
                    }
                }
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(com);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IDesktopWallpaper path failed, falling back to SPI");
            return false;
        }
    }

    private bool TryApplySystemParameters(string absolutePath)
    {
        try
        {
            var ok = Pinvoke.SystemParametersInfoW(
                Pinvoke.SPI_SETDESKWALLPAPER,
                0,
                absolutePath,
                Pinvoke.SPIF_UPDATEINIFILE | Pinvoke.SPIF_SENDCHANGE);
            if (!ok)
            {
                var err = Marshal.GetLastWin32Error();
                _logger.LogError("SystemParametersInfoW returned false, win32 error {Err}", err);
            }
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SystemParametersInfoW threw");
            return false;
        }
    }
}
