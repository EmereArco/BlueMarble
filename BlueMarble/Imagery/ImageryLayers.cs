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

    public static DateOnly BlackMarbleDate { get; } = new DateOnly(2016, 1, 1);

    public static DateOnly LatestDailyImageryDate(DateTimeOffset utcNow)
    {
        return DateOnly.FromDateTime(utcNow.UtcDateTime.Date.AddDays(-1));
    }
}

public interface IImageryProvider
{
    ImageryLayer Layer { get; }
    Task<string> EnsureEquirectangularAsync(int width, int height, CancellationToken cancellationToken);
}
