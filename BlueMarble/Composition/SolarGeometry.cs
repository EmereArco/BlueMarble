using System;

namespace BlueMarble.Composition;

public readonly record struct SubsolarPoint(double LatitudeDegrees, double LongitudeDegrees);

public static class SolarGeometry
{
    private const double DegToRad = Math.PI / 180.0;
    private const double RadToDeg = 180.0 / Math.PI;

    public static double DaysSinceJ2000(DateTimeOffset utc)
    {
        var j2000 = new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
        return (utc - j2000).TotalDays;
    }

    public static double GetSolarDeclinationDegrees(DateTimeOffset utc)
    {
        var (decl, _) = ComputeSunEquatorial(utc);
        return decl;
    }

    public static double GreenwichMeanSiderealTimeDegrees(DateTimeOffset utc)
    {
        var n = DaysSinceJ2000(utc);
        var gmstHours = 18.697374558 + 24.06570982441908 * n;
        var gmstDeg = NormalizeDegrees360(gmstHours * 15.0);
        return gmstDeg;
    }

    public static SubsolarPoint GetSubsolarPoint(DateTimeOffset utc)
    {
        var (declDeg, raDeg) = ComputeSunEquatorial(utc);
        var gmstDeg = GreenwichMeanSiderealTimeDegrees(utc);
        var lonDeg = NormalizeDegrees180(raDeg - gmstDeg);
        return new SubsolarPoint(declDeg, lonDeg);
    }

    private static (double DeclinationDegrees, double RightAscensionDegrees) ComputeSunEquatorial(DateTimeOffset utc)
    {
        var n = DaysSinceJ2000(utc);

        var meanLongDeg = NormalizeDegrees360(280.460 + 0.9856474 * n);
        var meanAnomalyDeg = NormalizeDegrees360(357.528 + 0.9856003 * n);
        var meanAnomalyRad = meanAnomalyDeg * DegToRad;

        var eclipticLongDeg = NormalizeDegrees360(
            meanLongDeg
            + 1.915 * Math.Sin(meanAnomalyRad)
            + 0.020 * Math.Sin(2 * meanAnomalyRad));
        var eclipticLongRad = eclipticLongDeg * DegToRad;

        var obliquityDeg = 23.439 - 0.0000004 * n;
        var obliquityRad = obliquityDeg * DegToRad;

        var sinDecl = Math.Sin(obliquityRad) * Math.Sin(eclipticLongRad);
        var declRad = Math.Asin(sinDecl);

        var raRad = Math.Atan2(
            Math.Cos(obliquityRad) * Math.Sin(eclipticLongRad),
            Math.Cos(eclipticLongRad));

        return (declRad * RadToDeg, NormalizeDegrees360(raRad * RadToDeg));
    }

    public static double NormalizeDegrees360(double deg)
    {
        deg %= 360.0;
        if (deg < 0) deg += 360.0;
        return deg;
    }

    public static double NormalizeDegrees180(double deg)
    {
        deg = NormalizeDegrees360(deg);
        if (deg > 180.0) deg -= 360.0;
        return deg;
    }
}
