using FamilyHub.Contracts;

namespace FamilyHub.Web;

/// <summary>
/// Holds the latest dashboard snapshot for browser clients.
/// </summary>
/// <remarks>
/// The desktop app owns every provider, so rather than duplicating weather, calendar and news
/// fetching inside the web layer, the app publishes a finished snapshot here on its existing timers
/// and the endpoints just hand it out. Keeps the Pi's browser one HTTP round trip away from a full
/// screen of data, and keeps the web project free of provider dependencies.
/// </remarks>
public sealed class DashboardFeed
{
    private readonly object _gate = new();
    private ViewStateDto? _state;
    private IReadOnlyList<ViewNewsDto> _news = [];
    private DateTimeOffset _newsUpdatedAt = DateTimeOffset.MinValue;

    public void Publish(ViewStateDto state) { lock (_gate) _state = state; }

    public void PublishNews(IReadOnlyList<ViewNewsDto> news)
    {
        lock (_gate) { _news = news; _newsUpdatedAt = DateTimeOffset.Now; }
    }

    public ViewStateDto? State { get { lock (_gate) return _state; } }

    public (IReadOnlyList<ViewNewsDto> Stories, DateTimeOffset UpdatedAt) News
    {
        get { lock (_gate) return (_news, _newsUpdatedAt); }
    }

    /// <summary>True once the app has published at least one snapshot.</summary>
    public bool IsReady { get { lock (_gate) return _state is not null; } }
}
