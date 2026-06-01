using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BlueMarble.Imagery;

/// <summary>
/// Pure parsing helpers for the GIBS WMS GetCapabilities document. Kept free of
/// I/O and Windows dependencies so the logic can be unit-tested on any platform.
/// </summary>
public static partial class GibsCapabilities
{
    /// <summary>
    /// Returns the latest available date for <paramref name="gibsLayerName"/> as
    /// declared by its time Dimension's <c>default</c> attribute, or <c>null</c> if
    /// the layer has no time dimension (e.g. the static Blue Marble) or is absent.
    /// </summary>
    public static DateOnly? ParseLatestTime(string capabilitiesXml, string gibsLayerName)
    {
        ArgumentNullException.ThrowIfNull(capabilitiesXml);
        ArgumentNullException.ThrowIfNull(gibsLayerName);

        // GIBS emits the <Name> first, then the layer's metadata including the time
        // Dimension, all before the closing </Layer>, e.g.:
        //   <Layer ...>
        //     <Name>VIIRS_Black_Marble</Name>
        //     <Title>VIIRS_Black_Marble</Title>
        //     ...
        //     <Dimension name="time" units="ISO8601" default="2016-01-01" ...>...</Dimension>
        //   </Layer>
        // Anchor on the exact <Name>, then look forward (within the same <Layer>) for
        // the time Dimension's default attribute.
        var nameToken = $"<Name>{Regex.Escape(gibsLayerName)}</Name>";
        var nameMatch = Regex.Match(capabilitiesXml, nameToken);
        if (!nameMatch.Success)
        {
            return null;
        }

        var searchStart = nameMatch.Index + nameMatch.Length;
        // Bound the search to this layer: stop at its closing </Layer> so we never
        // borrow a sibling layer's time dimension when this one is static.
        var layerEnd = capabilitiesXml.IndexOf("</Layer>", searchStart, StringComparison.Ordinal);
        var blockLength = (layerEnd < 0 ? capabilitiesXml.Length : layerEnd) - searchStart;
        var block = capabilitiesXml.Substring(searchStart, blockLength);

        var dim = TimeDimensionRegex().Match(block);
        if (!dim.Success)
        {
            return null;
        }

        var value = dim.Groups["default"].Value;
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        // Some layers use full ISO timestamps for the default; take the date part.
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            return DateOnly.FromDateTime(dto.UtcDateTime);
        }

        return null;
    }

    [GeneratedRegex(
        "<Dimension\\s+name=\"time\"[^>]*\\bdefault=\"(?<default>[^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimeDimensionRegex();
}
