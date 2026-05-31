using System;
using BlueMarble.Composition;
using Xunit;

namespace BlueMarble.Tests;

public class SolarGeometryTests
{
    private static DateTimeOffset Utc(int y, int m, int d, int h, int mm) =>
        new(y, m, d, h, mm, 0, TimeSpan.Zero);

    [Fact]
    public void DaysSinceJ2000_IsZeroAtEpoch()
    {
        var epoch = new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(0.0, SolarGeometry.DaysSinceJ2000(epoch), precision: 6);
    }

    [Fact]
    public void DaysSinceJ2000_AdvancesOnePerDay()
    {
        var epoch = new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(1.0, SolarGeometry.DaysSinceJ2000(epoch.AddDays(1)), precision: 6);
    }

    [Fact]
    public void VernalEquinox_DeclinationIsNearZero()
    {
        // 2025 vernal equinox: 2025-03-20 09:01 UTC
        var decl = SolarGeometry.GetSolarDeclinationDegrees(Utc(2025, 3, 20, 9, 1));
        Assert.InRange(decl, -0.5, 0.5);
    }

    [Fact]
    public void AutumnEquinox_DeclinationIsNearZero()
    {
        // 2025 autumnal equinox: 2025-09-22 18:19 UTC
        var decl = SolarGeometry.GetSolarDeclinationDegrees(Utc(2025, 9, 22, 18, 19));
        Assert.InRange(decl, -0.5, 0.5);
    }

    [Fact]
    public void JuneSolstice_DeclinationIsNearPlus2344()
    {
        // 2025 June solstice: 2025-06-21 02:42 UTC
        var decl = SolarGeometry.GetSolarDeclinationDegrees(Utc(2025, 6, 21, 2, 42));
        Assert.InRange(decl, 23.0, 23.7);
    }

    [Fact]
    public void DecemberSolstice_DeclinationIsNearMinus2344()
    {
        // 2025 December solstice: 2025-12-21 15:03 UTC
        var decl = SolarGeometry.GetSolarDeclinationDegrees(Utc(2025, 12, 21, 15, 3));
        Assert.InRange(decl, -23.7, -23.0);
    }

    [Fact]
    public void Subsolar_LongitudeMatchesSolarTime_NoonUtcAtGreenwich()
    {
        // At 12:00 UTC on an equinox, the subsolar longitude is near 0
        // (modulo equation of time, ~±10 min == ±2.5°)
        var p = SolarGeometry.GetSubsolarPoint(Utc(2025, 3, 20, 12, 0));
        Assert.InRange(p.LongitudeDegrees, -5.0, 5.0);
    }

    [Fact]
    public void Subsolar_LongitudeAdvancesWestwardOverTime()
    {
        var t0 = Utc(2025, 3, 20, 12, 0);
        var a = SolarGeometry.GetSubsolarPoint(t0);
        var b = SolarGeometry.GetSubsolarPoint(t0.AddHours(6));

        // 6 h of Earth rotation == 90° westward (subsolar lon decreases by ~90°)
        var delta = a.LongitudeDegrees - b.LongitudeDegrees;
        if (delta < -180) delta += 360;
        if (delta > 180) delta -= 360;
        Assert.InRange(delta, 89.0, 91.0);
    }

    [Fact]
    public void Subsolar_LatitudeStaysWithinObliquityBounds()
    {
        // Sample every ~10 days through a year; |lat| must never exceed 23.5°.
        var start = Utc(2025, 1, 1, 0, 0);
        for (var i = 0; i < 37; i++)
        {
            var p = SolarGeometry.GetSubsolarPoint(start.AddDays(i * 10));
            Assert.InRange(p.LatitudeDegrees, -23.5, 23.5);
            Assert.InRange(p.LongitudeDegrees, -180.0, 180.0);
        }
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(360.0, 0.0)]
    [InlineData(-1.0, 359.0)]
    [InlineData(720.5, 0.5)]
    public void NormalizeDegrees360_WrapsCorrectly(double input, double expected)
    {
        Assert.Equal(expected, SolarGeometry.NormalizeDegrees360(input), precision: 6);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(180.0, 180.0)]
    [InlineData(181.0, -179.0)]
    [InlineData(-1.0, -1.0)]
    [InlineData(359.0, -1.0)]
    public void NormalizeDegrees180_FoldsToSignedRange(double input, double expected)
    {
        Assert.Equal(expected, SolarGeometry.NormalizeDegrees180(input), precision: 6);
    }
}
