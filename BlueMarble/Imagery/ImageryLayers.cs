using System;

namespace BlueMarble.Imagery;

public sealed record ImageryLayer(
    string Id,
    string GibsLayerName,
    string Format,
    string FileExtension,
    string TileMatrixSet);

public static class ImageryLayers
{
    public static readonly ImageryLayer BlueMarbleNextGeneration = new(
        Id: "blue-marble-ng",
        GibsLayerName: "BlueMarble_NextGeneration",
        Format: "image/jpeg",
        FileExtension: "jpg",
        TileMatrixSet: "500m");

    public static readonly ImageryLayer BlackMarble = new(
        Id: "black-marble",
        GibsLayerName: "VIIRS_Black_Marble",
        Format: "image/png",
        FileExtension: "png",
        TileMatrixSet: "500m");

    public static readonly ImageryLayer ModisTerraTrueColor = new(
        Id: "modis-terra-true-color",
        GibsLayerName: "MODIS_Terra_CorrectedReflectance_TrueColor",
        Format: "image/jpeg",
        FileExtension: "jpg",
        TileMatrixSet: "250m");

    public static DateOnly BlueMarbleMonthFor(DateTimeOffset utcNow)
    {
        return new DateOnly(2004, utcNow.UtcDateTime.Month, 1);
    }

    /// <summary>
    /// Fallback date for VIIRS Black Marble when GetCapabilities cannot be reached.
    /// The live "latest available" is discovered at runtime; this is only a floor. The
    /// Black Marble composite is published yearly and currently the most recent edition
    /// the service advertises is 2016-01-01 (editions exist only for 2012 and 2016).
    /// </summary>
    public static DateOnly BlackMarbleFallbackDate { get; } = new DateOnly(2016, 1, 1);

    public static DateOnly LatestDailyImageryDate(DateTimeOffset utcNow)
    {
        // Daily products lag real time; step back a couple of days to avoid empty tiles.
        return DateOnly.FromDateTime(utcNow.UtcDateTime.Date.AddDays(-2));
    }
}

public interface IImageryProvider
{
    ImageryLayer Layer { get; }
    Task<string> EnsureEquirectangularAsync(int width, int height, CancellationToken cancellationToken);
}
