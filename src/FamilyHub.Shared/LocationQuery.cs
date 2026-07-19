using System.Globalization;

namespace FamilyHub.Contracts;

/// <summary>
/// Interprets what a person typed into a location box.
/// </summary>
/// <remarks>
/// The Open-Meteo geocoding API matches a single place name only. It returns nothing at all for
/// "Sanford, NC", and for a bare "Sanford" it returns the most populous match — Sanford, Florida —
/// rather than the one the person meant. So the region is split off here, sent separately, and used
/// to rank the candidates the API does return.
/// </remarks>
public static class LocationQuery
{
    /// <summary>Number of candidates to request so a region hint has something to choose between.</summary>
    public const int CandidateCount = 20;

    public static bool TryParseCoordinates(string query, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        var parts = query.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude)
            && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    }

    /// <summary>Splits "Sanford, NC" into the place name the API can match and the region hint it cannot.</summary>
    public static (string SearchTerm, string? Region) Split(string query)
    {
        var parts = query.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return (query.Trim(), null);
        var region = parts.Length > 1 ? string.Join(", ", parts.Skip(1)) : null;
        return (parts[0], string.IsNullOrWhiteSpace(region) ? null : region);
    }

    public static bool IsPostalCode(string term) =>
        term.Length is >= 3 and <= 10 && term.Any(char.IsDigit) && term.All(c => char.IsLetterOrDigit(c) || c is '-' or ' ');

    /// <summary>
    /// Ranks one candidate. Higher is better; candidates are never rejected outright so that a
    /// misspelled or unrecognised region still yields a result rather than "location not found".
    /// </summary>
    public static int Score(string searchTerm, string? region, string name, string? admin1, string? countryCode,
        string? country, IReadOnlyList<string>? postcodes)
    {
        var score = 0;
        if (IsPostalCode(searchTerm) && postcodes is not null
            && postcodes.Any(code => code.Equals(searchTerm, StringComparison.OrdinalIgnoreCase)))
            score += 100;
        if (name.Equals(searchTerm, StringComparison.OrdinalIgnoreCase)) score += 20;
        if (region is null) return score;

        // A region hint that matches is worth more than an exact name match, because picking the
        // right Sanford matters more than preferring a place literally called "Sanford".
        foreach (var hint in region.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var expanded = ExpandRegion(hint);
            if (Matches(admin1, hint) || Matches(admin1, expanded)) score += 50;
            if (Matches(countryCode, hint) || Matches(country, hint) || Matches(country, expanded)) score += 30;
        }
        return score;
    }

    public static string Describe(string name, string? admin1, string? countryCode) =>
        string.Join(", ", new[] { name, admin1, countryCode }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static bool Matches(string? value, string? candidate) =>
        !string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(candidate)
        && value.Equals(candidate, StringComparison.OrdinalIgnoreCase);

    /// <summary>Expands a US state abbreviation to the full name Open-Meteo reports in admin1.</summary>
    private static string ExpandRegion(string hint) => UsStates.TryGetValue(hint, out var full) ? full : hint;

    private static readonly Dictionary<string, string> UsStates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "Alabama", ["AK"] = "Alaska", ["AZ"] = "Arizona", ["AR"] = "Arkansas", ["CA"] = "California",
        ["CO"] = "Colorado", ["CT"] = "Connecticut", ["DE"] = "Delaware", ["DC"] = "District of Columbia",
        ["FL"] = "Florida", ["GA"] = "Georgia", ["HI"] = "Hawaii", ["ID"] = "Idaho", ["IL"] = "Illinois",
        ["IN"] = "Indiana", ["IA"] = "Iowa", ["KS"] = "Kansas", ["KY"] = "Kentucky", ["LA"] = "Louisiana",
        ["ME"] = "Maine", ["MD"] = "Maryland", ["MA"] = "Massachusetts", ["MI"] = "Michigan", ["MN"] = "Minnesota",
        ["MS"] = "Mississippi", ["MO"] = "Missouri", ["MT"] = "Montana", ["NE"] = "Nebraska", ["NV"] = "Nevada",
        ["NH"] = "New Hampshire", ["NJ"] = "New Jersey", ["NM"] = "New Mexico", ["NY"] = "New York",
        ["NC"] = "North Carolina", ["ND"] = "North Dakota", ["OH"] = "Ohio", ["OK"] = "Oklahoma", ["OR"] = "Oregon",
        ["PA"] = "Pennsylvania", ["RI"] = "Rhode Island", ["SC"] = "South Carolina", ["SD"] = "South Dakota",
        ["TN"] = "Tennessee", ["TX"] = "Texas", ["UT"] = "Utah", ["VT"] = "Vermont", ["VA"] = "Virginia",
        ["WA"] = "Washington", ["WV"] = "West Virginia", ["WI"] = "Wisconsin", ["WY"] = "Wyoming",
        ["PR"] = "Puerto Rico", ["USA"] = "United States", ["US"] = "United States", ["UK"] = "United Kingdom"
    };
}
