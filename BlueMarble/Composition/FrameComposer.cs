using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace BlueMarble.Composition;

public sealed record CompositionOptions(
    double TerminatorSoftnessDegrees = 22.0,
    double OceanGlintStrength = 0.6,
    double OceanGlintRadiusDegrees = 35.0,
    double Contrast = 1.0)
{
    public static CompositionOptions Default { get; } = new();

    public static CompositionOptions NoGlint(double softness) =>
        new(TerminatorSoftnessDegrees: softness, OceanGlintStrength: 0.0);
}

public sealed class FrameComposer
{
    private const double DegToRad = Math.PI / 180.0;

    public string Compose(
        string dayPath,
        string nightPath,
        SubsolarPoint subsolar,
        CompositionOptions options,
        int width,
        int height,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dayPath);
        ArgumentNullException.ThrowIfNull(nightPath);
        ArgumentNullException.ThrowIfNull(options);
        if (width <= 1 || height <= 1) throw new ArgumentOutOfRangeException(nameof(width));

        using var day = LoadResized(dayPath, width, height);
        using var night = LoadResized(nightPath, width, height);
        cancellationToken.ThrowIfCancellationRequested();

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var output = new SKBitmap(info);

        Blend(day, night, output, subsolar, options, cancellationToken);

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
        return outputPath;
    }

    public Task<string> ComposeAsync(
        string dayPath,
        string nightPath,
        SubsolarPoint subsolar,
        CompositionOptions options,
        int width,
        int height,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Compose(
            dayPath, nightPath, subsolar, options,
            width, height, outputPath, cancellationToken), cancellationToken);
    }

    private static SKBitmap LoadResized(string path, int width, int height)
    {
        using var source = SKBitmap.Decode(path)
            ?? throw new InvalidDataException($"Cannot decode imagery at {path}");
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

    private static void Blend(
        SKBitmap day,
        SKBitmap night,
        SKBitmap output,
        SubsolarPoint subsolar,
        CompositionOptions options,
        CancellationToken cancellationToken)
    {
        var w = output.Width;
        var h = output.Height;

        var declRad = subsolar.LatitudeDegrees * DegToRad;
        var sinDecl = Math.Sin(declRad);
        var cosDecl = Math.Cos(declRad);
        var lonSubRad = subsolar.LongitudeDegrees * DegToRad;

        var softRad = Math.Max(0.1, options.TerminatorSoftnessDegrees) * DegToRad;
        var softCos = Math.Sin(softRad);

        var glintEnabled = options.OceanGlintStrength > 0.0;
        var glintSigmaRad = Math.Max(0.5, options.OceanGlintRadiusDegrees) * DegToRad;
        var glintInvSigmaSq = 1.0 / (glintSigmaRad * glintSigmaRad);
        var glintPeak = options.OceanGlintStrength * 220.0;

        // Contrast stretch around mid-gray: pushes the bright day side brighter and
        // the dark night side darker, widening the day/night separation.
        const double ContrastPivot = 127.5;
        var contrast = options.Contrast;
        var applyContrast = Math.Abs(contrast - 1.0) > 1e-6;

        unsafe
        {
            var dayPtr = (byte*)day.GetPixels().ToPointer();
            var nightPtr = (byte*)night.GetPixels().ToPointer();
            var outPtr = (byte*)output.GetPixels().ToPointer();
            var rowBytes = output.RowBytes;

            for (var y = 0; y < h; y++)
            {
                if ((y & 0x3F) == 0) cancellationToken.ThrowIfCancellationRequested();

                var lat = (0.5 - (y + 0.5) / h) * 180.0;
                var latRad = lat * DegToRad;
                var sinLat = Math.Sin(latRad);
                var cosLat = Math.Cos(latRad);
                var rowA = sinLat * sinDecl;
                var rowB = cosLat * cosDecl;

                var dayRow = dayPtr + y * rowBytes;
                var nightRow = nightPtr + y * rowBytes;
                var outRow = outPtr + y * rowBytes;

                for (var x = 0; x < w; x++)
                {
                    var lon = ((x + 0.5) / w - 0.5) * 360.0;
                    var hourRad = lon * DegToRad - lonSubRad;
                    var cosZ = rowA + rowB * Math.Cos(hourRad);

                    var t = Smoothstep(-softCos, softCos, cosZ);

                    var px = x * 4;
                    var nB = nightRow[px + 0];
                    var nG = nightRow[px + 1];
                    var nR = nightRow[px + 2];
                    var dB = dayRow[px + 0];
                    var dG = dayRow[px + 1];
                    var dR = dayRow[px + 2];

                    var b = nB + (dB - nB) * t;
                    var g = nG + (dG - nG) * t;
                    var r = nR + (dR - nR) * t;

                    if (glintEnabled && cosZ > 0)
                    {
                        var isWater = dB > dR + 30 && dR < 80;
                        if (isWater)
                        {
                            var clamped = Math.Min(1.0, cosZ);
                            var angDist = Math.Acos(clamped);
                            var boost = glintPeak * Math.Exp(-(angDist * angDist) * glintInvSigmaSq);
                            r += boost;
                            g += boost;
                            b += boost;
                        }
                    }

                    if (applyContrast)
                    {
                        b = (b - ContrastPivot) * contrast + ContrastPivot;
                        g = (g - ContrastPivot) * contrast + ContrastPivot;
                        r = (r - ContrastPivot) * contrast + ContrastPivot;
                    }

                    outRow[px + 0] = (byte)Math.Clamp(b, 0.0, 255.0);
                    outRow[px + 1] = (byte)Math.Clamp(g, 0.0, 255.0);
                    outRow[px + 2] = (byte)Math.Clamp(r, 0.0, 255.0);
                    outRow[px + 3] = 0xFF;
                }
            }
        }
    }

    private static double Smoothstep(double edge0, double edge1, double x)
    {
        if (edge1 <= edge0) return x < edge0 ? 0.0 : 1.0;
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }
}
