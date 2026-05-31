using System;
using System.Runtime.InteropServices;

namespace BlueMarble.Native;

/// <summary>
/// Per-monitor wallpaper control. Available since Windows 8.
/// CLSID and IID from shobjidl_core.h.
/// </summary>
[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    void SetWallpaper(
        [MarshalAs(UnmanagedType.LPWStr)] string? monitorID,
        [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetMonitorDevicePathAt(uint monitorIndex);

    uint GetMonitorDevicePathCount();

    void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out NativeRect displayRect);

    void SetBackgroundColor(uint color);

    uint GetBackgroundColor();

    void SetPosition(DesktopWallpaperPosition position);

    DesktopWallpaperPosition GetPosition();

    void SetSlideshow(nint items);

    nint GetSlideshow();

    void SetSlideshowOptions(uint options, uint slideshowTick);

    void GetSlideshowOptions(out uint options, out uint slideshowTick);

    void AdvanceSlideshow(
        [MarshalAs(UnmanagedType.LPWStr)] string? monitorID,
        DesktopSlideshowDirection direction);

    DesktopSlideshowState GetStatus();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

internal enum DesktopWallpaperPosition : uint
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}

internal enum DesktopSlideshowDirection : uint { Forward = 0, Backward = 1 }
internal enum DesktopSlideshowState : uint { Enabled = 0x01, Slideshow = 0x02, DisabledByRemoteSession = 0x04 }

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[ComImport]
[Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
internal class DesktopWallpaperClass { }

internal static class DesktopWallpaperFactory
{
    public static IDesktopWallpaper Create() => (IDesktopWallpaper)new DesktopWallpaperClass();

    public static DesktopWallpaperPosition Map(BlueMarble.Wallpaper.WallpaperPosition position) => position switch
    {
        BlueMarble.Wallpaper.WallpaperPosition.Center => DesktopWallpaperPosition.Center,
        BlueMarble.Wallpaper.WallpaperPosition.Tile => DesktopWallpaperPosition.Tile,
        BlueMarble.Wallpaper.WallpaperPosition.Stretch => DesktopWallpaperPosition.Stretch,
        BlueMarble.Wallpaper.WallpaperPosition.Fit => DesktopWallpaperPosition.Fit,
        BlueMarble.Wallpaper.WallpaperPosition.Fill => DesktopWallpaperPosition.Fill,
        BlueMarble.Wallpaper.WallpaperPosition.Span => DesktopWallpaperPosition.Span,
        _ => DesktopWallpaperPosition.Fit,
    };
}
