using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BlueMarble.Native;

namespace BlueMarble.Wallpaper;

public readonly record struct MonitorInfo(string Id, int Width, int Height);

internal static class MonitorEnumerator
{
    public static List<MonitorInfo> Enumerate()
    {
        var list = new List<MonitorInfo>();
        IDesktopWallpaper? com = null;
        try
        {
            com = DesktopWallpaperFactory.Create();
            var count = com.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                string id;
                try { id = com.GetMonitorDevicePathAt(i); }
                catch { continue; }
                if (string.IsNullOrEmpty(id)) continue;

                try
                {
                    com.GetMonitorRECT(id, out var rect);
                    var w = rect.Right - rect.Left;
                    var h = rect.Bottom - rect.Top;
                    if (w > 0 && h > 0)
                        list.Add(new MonitorInfo(id, w, h));
                }
                catch
                {
                    // detached / inactive monitor — skip
                }
            }
        }
        catch
        {
            // fall through with whatever we collected
        }
        finally
        {
            if (com is not null) Marshal.ReleaseComObject(com);
        }
        return list;
    }
}
