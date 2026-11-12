using Politeconomy.Sim.Model;
using Politeconomy.Sim.News;

namespace Politeconomy.Sim.Engine;

/// <summary>
/// Лента новостей для интерфейса: новости (новые — в конце) и номер версии, который растёт с каждой новостью.
/// Снимок пересобирается, только когда лента изменилась.
/// </summary>
public sealed record NewsSnapshot(IReadOnlyList<NewsItem> Items, int Version)
{
    public static readonly NewsSnapshot Empty = new([], 0);

    public static NewsSnapshot Capture(World world)
    {
        if (world.News is not { } news)
            return Empty;
        if (news.Snapshot is not { } cached || cached.Version != news.Version)
            news.Snapshot = cached = new NewsSnapshot(news.Items.ToList(), news.Version);
        return cached;
    }
}
