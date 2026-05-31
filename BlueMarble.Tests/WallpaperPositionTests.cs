using BlueMarble.Wallpaper;
using Xunit;

namespace BlueMarble.Tests;

public class WallpaperPositionTests
{
    [Theory]
    [InlineData(WallpaperPosition.Tile,    "0", "1")]
    [InlineData(WallpaperPosition.Center,  "0", "0")]
    [InlineData(WallpaperPosition.Stretch, "2", "0")]
    [InlineData(WallpaperPosition.Fit,     "6", "0")]
    [InlineData(WallpaperPosition.Fill,   "10", "0")]
    [InlineData(WallpaperPosition.Span,   "22", "0")]
    public void ToRegistryValues_MapsCorrectly(WallpaperPosition position, string style, string tile)
    {
        var values = position.ToRegistryValues();
        Assert.Equal(style, values.WallpaperStyle);
        Assert.Equal(tile, values.TileWallpaper);
    }

    [Fact]
    public void DefaultFallback_IsFit()
    {
        // Casting an out-of-range value yields the Fit defaults.
        var values = ((WallpaperPosition)999).ToRegistryValues();
        Assert.Equal("6", values.WallpaperStyle);
        Assert.Equal("0", values.TileWallpaper);
    }
}
