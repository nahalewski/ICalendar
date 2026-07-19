namespace FamilyHub.Contracts;

/// <summary>Maps a WMO weather code to the glyph shown on the dashboard and the companion app.</summary>
public static class WeatherGlyph
{
    public static string For(int code) => code switch
    {
        0 => "☀",
        1 or 2 => "⛅",
        3 => "☁",
        45 or 48 => "🌫",
        51 or 53 or 55 => "🌦",
        61 or 63 or 65 => "🌧",
        71 or 73 or 75 or 77 => "❄",
        80 or 81 or 82 => "🌧",
        85 or 86 => "🌨",
        95 or 96 or 99 => "⛈",
        _ => "🌤"
    };
}
