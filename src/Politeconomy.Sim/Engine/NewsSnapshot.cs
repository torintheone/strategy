using Politeconomy.Sim.Model;
using Politeconomy.Sim.News;

namespace Politeconomy.Sim.Engine;

/// <summary>
/// Лента новостей для интерфейса: новости, телевыпуски и номера газеты (новые — в конце) и номер версии, который растёт с каждой
/// новостью и выпуском. Снимок пересобирается, только когда лента изменилась.
/// </summary>
public sealed record NewsSnapshot(IReadOnlyList<NewsItem> Items, int Version, IReadOnlyList<Broadcast>? Broadcasts = null,
    IReadOnlyList<Newspaper>? Papers = null)
{
    public static readonly NewsSnapshot Empty = new([], 0);

    /// <summary>Последний выпуск для страны (null — выпусков ещё не было).</summary>
    public Broadcast? LastBroadcast(string? country) => country == null ? null : Broadcasts?.LastOrDefault(b => b.Country == country);

    /// <summary>Номера газеты для страны, старые — в начале.</summary>
    public IReadOnlyList<Newspaper> PapersOf(string? country) => country == null ? [] : Papers?.Where(p => p.Country == country).ToList() ?? [];

    public static NewsSnapshot Capture(World world)
    {
        if (world.News is not { } news)
            return Empty;
        if (news.Snapshot is not { } cached || cached.Version != news.Version)
            news.Snapshot = cached = new NewsSnapshot(news.Items.ToList(), news.Version, news.Broadcasts.ToList(), news.Papers.ToList());
        return cached;
    }
}
