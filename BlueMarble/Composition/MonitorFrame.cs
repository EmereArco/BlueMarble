using System;
using System.IO;
using SkiaSharp;

namespace BlueMarble.Composition;

public static class MonitorFrame
{
    public static void RenderLetterbox(string masterPath, int width, int height, string outputPath)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));

        using var src = SKBitmap.Decode(masterPath)
            ?? throw new InvalidDataException($"Cannot decode master frame at {masterPath}");

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var dst = new SKBitmap(info);
        using (var canvas = new SKCanvas(dst))
        {
            canvas.Clear(SKColors.Black);

            var srcAspect = (double)src.Width / src.Height;
            var dstAspect = (double)width / height;

            float drawW, drawH;
            if (srcAspect > dstAspect)
            {
                drawW = width;
                drawH = (float)(width / srcAspect);
            }
            else
            {
                drawH = height;
                drawW = (float)(height * srcAspect);
            }

            var x = (width - drawW) / 2f;
            var y = (height - drawH) / 2f;

            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
            using var srcImage = SKImage.FromBitmap(src);
            canvas.DrawImage(srcImage, new SKRect(x, y, x + drawW, y + drawH), sampling);
        }

        using var image = SKImage.FromBitmap(dst);
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var tempPath = outputPath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            data.SaveTo(stream);
        }
        if (File.Exists(outputPath)) File.Delete(outputPath);
        File.Move(tempPath, outputPath);
    }
}
