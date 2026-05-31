using System.IO;
using BlueMarble.Composition;
using SkiaSharp;
using Xunit;

namespace BlueMarble.Tests;

public class FrameComposerTests
{
    private const int Width = 64;
    private const int Height = 32;

    private static readonly CompositionOptions NoGlintTight = CompositionOptions.NoGlint(3.0);
    private static readonly CompositionOptions NoGlintWide = CompositionOptions.NoGlint(20.0);

    [Fact]
    public void Compose_SubsolarPixel_IsLitFromDayTexture()
    {
        var (dayPath, nightPath, outPath) = PrepareSyntheticInputs(SKColors.White, SKColors.Black);
        try
        {
            new FrameComposer().Compose(
                dayPath, nightPath,
                subsolar: new SubsolarPoint(0.0, 0.0),
                options: NoGlintTight,
                width: Width, height: Height,
                outputPath: outPath);

            using var result = SKBitmap.Decode(outPath);
            Assert.NotNull(result);

            var sub = result.GetPixel(Width / 2, Height / 2);
            Assert.True(sub.Red > 230, $"subsolar pixel should be ~day (white), got R={sub.Red}");
        }
        finally
        {
            Cleanup(dayPath, nightPath, outPath);
        }
    }

    [Fact]
    public void Compose_AntipodalPixel_IsDarkFromNightTexture()
    {
        var (dayPath, nightPath, outPath) = PrepareSyntheticInputs(SKColors.White, SKColors.Black);
        try
        {
            new FrameComposer().Compose(
                dayPath, nightPath,
                subsolar: new SubsolarPoint(0.0, 0.0),
                options: NoGlintTight,
                width: Width, height: Height,
                outputPath: outPath);

            using var result = SKBitmap.Decode(outPath);
            Assert.NotNull(result);

            var anti1 = result.GetPixel(0, Height / 2);
            var anti2 = result.GetPixel(Width - 1, Height / 2);
            Assert.True(anti1.Red < 25, $"antipodal pixel (left) should be ~night, got R={anti1.Red}");
            Assert.True(anti2.Red < 25, $"antipodal pixel (right) should be ~night, got R={anti2.Red}");
        }
        finally
        {
            Cleanup(dayPath, nightPath, outPath);
        }
    }

    [Fact]
    public void Compose_Terminator_ProducesIntermediateBand()
    {
        var (dayPath, nightPath, outPath) = PrepareSyntheticInputs(SKColors.White, SKColors.Black);
        try
        {
            new FrameComposer().Compose(
                dayPath, nightPath,
                subsolar: new SubsolarPoint(0.0, 0.0),
                options: NoGlintWide,
                width: Width, height: Height,
                outputPath: outPath);

            using var result = SKBitmap.Decode(outPath);
            Assert.NotNull(result);

            var termWest = result.GetPixel(Width / 4, Height / 2);
            var termEast = result.GetPixel(3 * Width / 4, Height / 2);
            Assert.InRange((int)termWest.Red, 30, 225);
            Assert.InRange((int)termEast.Red, 30, 225);
            Assert.True(System.Math.Abs((termWest.Red + termEast.Red) - 255) <= 4,
                $"terminator should be mirror-symmetric, got west={termWest.Red} east={termEast.Red}");
        }
        finally
        {
            Cleanup(dayPath, nightPath, outPath);
        }
    }

    [Fact]
    public void Compose_RespectsSubsolarLongitude()
    {
        var (dayPath, nightPath, outPath) = PrepareSyntheticInputs(SKColors.White, SKColors.Black);
        try
        {
            new FrameComposer().Compose(
                dayPath, nightPath,
                subsolar: new SubsolarPoint(0.0, 90.0),
                options: NoGlintTight,
                width: Width, height: Height,
                outputPath: outPath);

            using var result = SKBitmap.Decode(outPath);
            Assert.NotNull(result);

            var lit = result.GetPixel((int)(Width * 0.75), Height / 2);
            Assert.True(lit.Red > 230, $"pixel under shifted subsolar should be lit, got R={lit.Red}");

            var dark = result.GetPixel((int)(Width * 0.25), Height / 2);
            Assert.True(dark.Red < 25, $"opposite hemisphere should be dark, got R={dark.Red}");
        }
        finally
        {
            Cleanup(dayPath, nightPath, outPath);
        }
    }

    [Fact]
    public void Compose_OceanGlint_BrightensWaterUnderSubsolar()
    {
        // Day-tex = deep blue water (R=20, G=40, B=180) → matches water mask B>R+30 && R<80
        var water = new SKColor(20, 40, 180);
        var (dayPath, nightPath, outBase) = PrepareSyntheticInputs(water, SKColors.Black);
        var outGlint = outBase + ".glint.png";

        try
        {
            var composer = new FrameComposer();
            composer.Compose(dayPath, nightPath,
                subsolar: new SubsolarPoint(0.0, 0.0),
                options: CompositionOptions.NoGlint(3.0),
                width: Width, height: Height,
                outputPath: outBase);

            composer.Compose(dayPath, nightPath,
                subsolar: new SubsolarPoint(0.0, 0.0),
                options: new CompositionOptions(
                    TerminatorSoftnessDegrees: 3.0,
                    OceanGlintStrength: 1.0,
                    OceanGlintRadiusDegrees: 25.0),
                width: Width, height: Height,
                outputPath: outGlint);

            using var baseImg = SKBitmap.Decode(outBase);
            using var glintImg = SKBitmap.Decode(outGlint);
            Assert.NotNull(baseImg);
            Assert.NotNull(glintImg);

            var basePx = baseImg.GetPixel(Width / 2, Height / 2);
            var glintPx = glintImg.GetPixel(Width / 2, Height / 2);

            Assert.True(glintPx.Red > basePx.Red + 40,
                $"glint should brighten water under subsolar: base R={basePx.Red} glint R={glintPx.Red}");
        }
        finally
        {
            Cleanup(dayPath, nightPath, outBase, outGlint);
        }
    }

    [Fact]
    public void Compose_OceanGlint_DoesNotBrightenLandUnderSubsolar()
    {
        // Day-tex = sandy/red land (R=170, G=110, B=60) → fails water mask
        var land = new SKColor(170, 110, 60);
        var (dayPath, nightPath, outBase) = PrepareSyntheticInputs(land, SKColors.Black);
        var outGlint = outBase + ".glint.png";

        try
        {
            var composer = new FrameComposer();
            composer.Compose(dayPath, nightPath,
                subsolar: new SubsolarPoint(0.0, 0.0),
                options: CompositionOptions.NoGlint(3.0),
                width: Width, height: Height,
                outputPath: outBase);

            composer.Compose(dayPath, nightPath,
                subsolar: new SubsolarPoint(0.0, 0.0),
                options: new CompositionOptions(
                    TerminatorSoftnessDegrees: 3.0,
                    OceanGlintStrength: 1.0,
                    OceanGlintRadiusDegrees: 25.0),
                width: Width, height: Height,
                outputPath: outGlint);

            using var baseImg = SKBitmap.Decode(outBase);
            using var glintImg = SKBitmap.Decode(outGlint);
            Assert.NotNull(baseImg);
            Assert.NotNull(glintImg);

            var basePx = baseImg.GetPixel(Width / 2, Height / 2);
            var glintPx = glintImg.GetPixel(Width / 2, Height / 2);

            Assert.True(System.Math.Abs(glintPx.Red - basePx.Red) <= 2,
                $"glint must not boost land: base R={basePx.Red} glint R={glintPx.Red}");
        }
        finally
        {
            Cleanup(dayPath, nightPath, outBase, outGlint);
        }
    }

    [Fact]
    public void Compose_Contrast_DeepensNightAndBrightensDay()
    {
        // Mid-tone day and night so the contrast stretch can push both ways.
        var day = new SKColor(200, 200, 200);
        var night = new SKColor(60, 60, 60);
        var (dayPath, nightPath, outFlat) = PrepareSyntheticInputs(day, night);
        var outPunchy = outFlat + ".punchy.png";

        try
        {
            var composer = new FrameComposer();
            composer.Compose(dayPath, nightPath,
                subsolar: new SubsolarPoint(0.0, 0.0),
                options: new CompositionOptions(TerminatorSoftnessDegrees: 3.0, OceanGlintStrength: 0.0, Contrast: 1.0),
                width: Width, height: Height,
                outputPath: outFlat);

            composer.Compose(dayPath, nightPath,
                subsolar: new SubsolarPoint(0.0, 0.0),
                options: new CompositionOptions(TerminatorSoftnessDegrees: 3.0, OceanGlintStrength: 0.0, Contrast: 1.6),
                width: Width, height: Height,
                outputPath: outPunchy);

            using var flat = SKBitmap.Decode(outFlat);
            using var punchy = SKBitmap.Decode(outPunchy);
            Assert.NotNull(flat);
            Assert.NotNull(punchy);

            // Night side (antipodal, left edge) gets darker; day side (subsolar center) gets brighter.
            var flatNight = flat.GetPixel(0, Height / 2).Red;
            var punchyNight = punchy.GetPixel(0, Height / 2).Red;
            var flatDay = flat.GetPixel(Width / 2, Height / 2).Red;
            var punchyDay = punchy.GetPixel(Width / 2, Height / 2).Red;

            Assert.True(punchyNight < flatNight, $"night should darken: flat={flatNight} punchy={punchyNight}");
            Assert.True(punchyDay > flatDay, $"day should brighten: flat={flatDay} punchy={punchyDay}");
        }
        finally
        {
            Cleanup(dayPath, nightPath, outFlat, outPunchy);
        }
    }

    [Fact]
    public void Compose_ContrastOne_IsNeutral()
    {
        // Contrast 1.0 must be a no-op versus the existing default behavior.
        var (dayPath, nightPath, outPath) = PrepareSyntheticInputs(SKColors.White, SKColors.Black);
        try
        {
            new FrameComposer().Compose(
                dayPath, nightPath,
                subsolar: new SubsolarPoint(0.0, 0.0),
                options: new CompositionOptions(TerminatorSoftnessDegrees: 3.0, OceanGlintStrength: 0.0, Contrast: 1.0),
                width: Width, height: Height,
                outputPath: outPath);

            using var result = SKBitmap.Decode(outPath);
            Assert.NotNull(result);
            Assert.True(result.GetPixel(Width / 2, Height / 2).Red > 230);
            Assert.True(result.GetPixel(0, Height / 2).Red < 25);
        }
        finally
        {
            Cleanup(dayPath, nightPath, outPath);
        }
    }

    private static (string day, string night, string output) PrepareSyntheticInputs(SKColor dayColor, SKColor nightColor)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BlueMarbleTests", Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        var dayPath = Path.Combine(dir, "day.png");
        var nightPath = Path.Combine(dir, "night.png");
        var outPath = Path.Combine(dir, "out.png");

        WriteSolid(dayPath, dayColor);
        WriteSolid(nightPath, nightColor);
        return (dayPath, nightPath, outPath);
    }

    private static void WriteSolid(string path, SKColor color)
    {
        using var bmp = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(color);
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static void Cleanup(params string[] paths)
    {
        foreach (var p in paths)
        {
            try
            {
                if (File.Exists(p))
                {
                    var dir = Path.GetDirectoryName(p);
                    File.Delete(p);
                    if (dir is not null && Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).GetEnumerator().MoveNext() == false)
                    {
                        Directory.Delete(dir);
                    }
                }
            }
            catch { /* best effort */ }
        }
    }
}
