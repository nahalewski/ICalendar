using FamilyHub.Contracts;
using Xunit;

namespace FamilyHub.Application.Tests;

public sealed class LocationQueryTests
{
    [Theory]
    [InlineData("35.4799,-79.1803", 35.4799, -79.1803)]
    [InlineData(" 35.4799 , -79.1803 ", 35.4799, -79.1803)]
    public void ParsesCoordinates(string query, double latitude, double longitude)
    {
        Assert.True(LocationQuery.TryParseCoordinates(query, out var parsedLatitude, out var parsedLongitude));
        Assert.Equal(latitude, parsedLatitude, 4);
        Assert.Equal(longitude, parsedLongitude, 4);
    }

    [Theory]
    [InlineData("Sanford, NC")]      // a place name, not a coordinate pair
    [InlineData("91.0,-79.0")]       // latitude out of range
    [InlineData("35.4799,-181.0")]   // longitude out of range
    [InlineData("27330")]
    public void RejectsNonCoordinates(string query) =>
        Assert.False(LocationQuery.TryParseCoordinates(query, out _, out _));

    [Fact]
    public void SplitsRegionOffThePlaceName()
    {
        // Open-Meteo returns nothing for "Sanford, NC", so only "Sanford" may be sent.
        var (term, region) = LocationQuery.Split("Sanford, NC");
        Assert.Equal("Sanford", term);
        Assert.Equal("NC", region);
    }

    [Fact]
    public void SplitLeavesABarePlaceNameAlone()
    {
        var (term, region) = LocationQuery.Split("Sanford");
        Assert.Equal("Sanford", term);
        Assert.Null(region);
    }

    [Theory]
    [InlineData("27330", true)]
    [InlineData("SW1A", true)]
    [InlineData("Sanford", false)]
    public void RecognisesPostalCodes(string term, bool expected) =>
        Assert.Equal(expected, LocationQuery.IsPostalCode(term));

    [Fact]
    public void StateAbbreviationOutranksThePopularCity()
    {
        // The bug this guards: a bare "Sanford" resolves to Florida because it is larger.
        var florida = LocationQuery.Score("Sanford", "NC", "Sanford", "Florida", "US", "United States", null);
        var carolina = LocationQuery.Score("Sanford", "NC", "Sanford", "North Carolina", "US", "United States", null);
        Assert.True(carolina > florida);
    }

    [Fact]
    public void FullStateNameRanksTheSameAsItsAbbreviation()
    {
        var abbreviated = LocationQuery.Score("Sanford", "NC", "Sanford", "North Carolina", "US", "United States", null);
        var spelledOut = LocationQuery.Score("Sanford", "North Carolina", "Sanford", "North Carolina", "US", "United States", null);
        Assert.Equal(abbreviated, spelledOut);
    }

    [Fact]
    public void ExactPostcodeMatchWins()
    {
        var matching = LocationQuery.Score("27330", null, "Sanford", "North Carolina", "US", "United States", ["27330", "27331"]);
        var other = LocationQuery.Score("27330", null, "Raleigh", "North Carolina", "US", "United States", ["27601"]);
        Assert.True(matching > other);
    }

    [Fact]
    public void UnrecognisedRegionStillScoresRatherThanRejecting()
    {
        // A typo must degrade to "best guess", never to "location not found": the bad hint simply
        // adds nothing, leaving the candidate ranked as if no region had been supplied.
        var typo = LocationQuery.Score("Sanford", "Nrth Carlina", "Sanford", "North Carolina", "US", "United States", null);
        var noRegion = LocationQuery.Score("Sanford", null, "Sanford", "North Carolina", "US", "United States", null);
        Assert.Equal(noRegion, typo);
        Assert.True(typo > 0);
    }

    [Fact]
    public void DescribeSkipsMissingParts() =>
        Assert.Equal("Sanford, US", LocationQuery.Describe("Sanford", null, "US"));
}
