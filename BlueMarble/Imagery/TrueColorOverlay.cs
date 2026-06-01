using System;
using System.IO;
using SkiaSharp;

namespace BlueMarble.Imagery;

/// <summary>
/// Overlays daily MODIS true-color imagery onto a Blue Marble base: each pixel takes MODIS
/// where MODIS has data, and falls back to the base where MODIS is no-data (near-black gaps
/// from polar night / unobserved swaths). The result is an equirectangular day texture with
/// no black holes.
/// </summary>
internal static class TrueColorOverlay
{
    // MODIS no-data fill comes back as black; JPEG compression nudges it a few levels up, so
    // treat any pixel whose brightest channel is below this as a gap to fill from the base.
    private const byte NoDataMaxChannel = 12;

    public static void Combine(string basePath, string modisPath, string outputPath)
    {
        using var baseBmp = Decode(basePath);
        using var modisRaw = Decode(modisPath);
        using var modis = ResizeTo(modisRaw, baseBmp.Width, baseBmp.Height);

        var w = baseBmp.Width;
        var h = baseBmp.Height;
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var output = new SKBitmap(info);

        unsafe
        {
            var basePtr = (byte*)baseBmp.GetPixels().ToPointer();
            var modisPtr = (byte*)modis.GetPixels().ToPointer();
            var outPtr = (byte*)output.GetPixels().ToPointer();
            var rowBytes = output.RowBytes;

            for (var y = 0; y < h; y++)
            {
                var baseRow = basePtr + y * rowBytes;
                var modisRow = modisPtr + y * rowBytes;
                var outRow = outPtr + y * rowBytes;

                for (var x = 0; x < w; x++)
                {
                    var px = x * 4;
                    var mB = modisRow[px + 0];
                    var mG = modisRow[px + 1];
                    var mR = modisRow[px + 2];

                    var hasData = mB > NoDataMaxChannel || mG > NoDataMaxChannel || mR > NoDataMaxChannel;
                    var src = hasData ? modisRow : baseRow;

                    outRow[px + 0] = src[px + 0];
                    outRow[px + 1] = src[px + 1];
                    outRow[px + 2] = src[px + 2];
                    outRow[px + 3] = 0xFF;
                }
            }
        }

        using var image = SKImage.FromBitmap(output);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality: 95);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var tempPath = outputPath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            data.SaveTo(stream);
        }
        if (File.Exists(outputPath)) File.Delete(outputPath);
        File.Move(tempPath, outputPath);
    }

    private static SKBitmap Decode(string path)
    {
        using var source = SKBitmap.Decode(path)
            ?? throw new InvalidDataException($"Cannot decode imagery at {path}");
        return source.Copy(SKColorType.Bgra8888) ?? source.Copy();
    }

    private static SKBitmap ResizeTo(SKBitmap source, int width, int height)
    {
        if (source.Width == width && source.Height == height)
        {
            return source.Copy(SKColorType.Bgra8888) ?? source.Copy();
        }
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var resized = new SKBitmap(info);
        using var canvas = new SKCanvas(resized);
        using var sourceImage = SKImage.FromBitmap(source);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        canvas.DrawImage(sourceImage, new SKRect(0, 0, width, height), sampling);
        return resized;
    }
}
