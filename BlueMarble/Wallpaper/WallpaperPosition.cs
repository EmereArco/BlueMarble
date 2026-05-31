namespace BlueMarble.Wallpaper;

public enum WallpaperPosition
{
    Center,
    Tile,
    Stretch,
    Fit,
    Fill,
    Span,
}

public readonly record struct WallpaperRegistryValues(string WallpaperStyle, string TileWallpaper);

public static class WallpaperPositionExtensions
{
    /// <summary>
    /// Values for HKEY_CURRENT_USER\Control Panel\Desktop\{WallpaperStyle,TileWallpaper}.
    /// Documented at https://learn.microsoft.com/windows/win32/shell/themes (legacy values).
    /// </summary>
    public static WallpaperRegistryValues ToRegistryValues(this WallpaperPosition position) => position switch
    {
        WallpaperPosition.Tile     => new("0", "1"),
        WallpaperPosition.Center   => new("0", "0"),
        WallpaperPosition.Stretch  => new("2", "0"),
        WallpaperPosition.Fit      => new("6", "0"),
        WallpaperPosition.Fill     => new("10", "0"),
        WallpaperPosition.Span     => new("22", "0"),
        _ => new("6", "0"),
    };
}
