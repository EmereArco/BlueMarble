using System;
using BlueMarble.Imagery;
using Xunit;

namespace BlueMarble.Tests;

public class GibsCapabilitiesTests
{
    // Mirrors the real GIBS WMS GetCapabilities structure: <Name> first, then the
    // time <Dimension>, all inside the <Layer>. The static day layer has no dimension.
    private const string SampleXml = """
        <Capabilities>
          <Layer queryable="0">
            <Name>VIIRS_Black_Marble</Name>
            <Title>VIIRS_Black_Marble</Title>
            <CRS>EPSG:4326</CRS>
            <Dimension name="time" units="ISO8601" default="2016-01-01" nearestValue="0">2012-01-01/2012-01-01/P1Y,2016-01-01/2016-01-01/P1Y</Dimension>
          </Layer>
          <Layer queryable="0">
            <Name>MODIS_Terra_CorrectedReflectance_TrueColor</Name>
            <Title>MODIS_Terra_CorrectedReflectance_TrueColor</Title>
            <CRS>EPSG:4326</CRS>
            <Dimension name="time" units="ISO8601" default="2026-05-29" nearestValue="0">2000-02-24/2026-05-29/P1D</Dimension>
          </Layer>
          <Layer queryable="0">
            <Name>BlueMarble_NextGeneration</Name>
            <Title>BlueMarble_NextGeneration</Title>
            <CRS>EPSG:4326</CRS>
          </Layer>
        </Capabilities>
        """;

    [Fact]
    public void ParseLatestTime_ReturnsDefaultForTimeEnabledLayer()
    {
        var date = GibsCapabilities.ParseLatestTime(SampleXml, "VIIRS_Black_Marble");
        Assert.Equal(new DateOnly(2016, 1, 1), date);
    }

    [Fact]
    public void ParseLatestTime_PicksCorrectLayerAmongMany()
    {
        var date = GibsCapabilities.ParseLatestTime(SampleXml, "MODIS_Terra_CorrectedReflectance_TrueColor");
        Assert.Equal(new DateOnly(2026, 5, 29), date);
    }

    [Fact]
    public void ParseLatestTime_DoesNotBorrowSiblingLayerDimension()
    {
        // The static day layer is followed by a time-enabled sibling; the forward
        // search must stop at </Layer> and not pick up the sibling's dimension.
        var date = GibsCapabilities.ParseLatestTime(SampleXml, "BlueMarble_NextGeneration");
        Assert.Null(date);
    }

    [Fact]
    public void ParseLatestTime_ReturnsNullForStaticLayer()
    {
        // BlueMarble_NextGeneration has no time Dimension.
        var date = GibsCapabilities.ParseLatestTime(SampleXml, "BlueMarble_NextGeneration");
        Assert.Null(date);
    }

    [Fact]
    public void ParseLatestTime_ReturnsNullForMissingLayer()
    {
        var date = GibsCapabilities.ParseLatestTime(SampleXml, "Does_Not_Exist");
        Assert.Null(date);
    }

    [Fact]
    public void ParseLatestTime_HandlesIsoTimestampDefault()
    {
        var xml = """
            <Layer>
              <Name>Some_Layer</Name>
              <Title>Some_Layer</Title>
              <Dimension name="time" units="ISO8601" default="2025-12-01T00:00:00Z">2020-01-01/2025-12-01/P1D</Dimension>
            </Layer>
            """;
        var date = GibsCapabilities.ParseLatestTime(xml, "Some_Layer");
        Assert.Equal(new DateOnly(2025, 12, 1), date);
    }
}
