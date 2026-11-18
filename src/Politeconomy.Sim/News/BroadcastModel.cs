using Politeconomy.Sim.Data;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.News;

/// <summary>
/// Телевыпуск: раз в неделю, по понедельникам, для страны игрока. Ведущий (один на страну, меняется раз в несколько
/// лет) рассказывает 3–5 главных сюжетов из ленты: сначала свои, потом мировые, по важности новости. Выпуск ничего
/// не меняет в модели; его текст написан для чтения вслух (озвучка — этап 8). MODEL.md, «Телевыпуск».
/// </summary>
public static class BroadcastModel
{
    /// <summary>День: по понедельникам выходит выпуск для страны игрока (без страны игрока выпуска нет).</summary>
    public static void Day(World world)
    {
        if (world.Date.DayOfWeek == DayOfWeek.Monday && world.PlayerCountry is { } player && world.ById(player) != null)
            Air(world, player);
    }

    /// <summary>Выпустить выпуск для страны. Если за две недели не было ни одной новости, выпуска нет (null).</summary>
    public static Broadcast? Air(World world, string country)
    {
        NewsModel.Ensure(world);
        var news = world.News!;
        if (world.Data.News.Broadcast is not { } tv)
            return null;
        var stories = Stories(world, country, tv);
        if (stories.Count == 0)
            return null;
        var (anchor, debut) = Anchor(world, country);
        var id = news.NextBroadcast++;
        var opening = Speech.Say(Nth(tv.Openings, id).Replace("{show}", tv.Show).Replace("{anchor}", anchor.Name));
        if (debut)
            opening += " " + Speech.Say(tv.Debut);
        var broadcast = new Broadcast(id, world.Date, country, tv.Show, anchor, debut, stories, opening, Speech.Say(Nth(tv.Closings, id)));
        news.Broadcasts.Add(broadcast);
        var keep = Math.Max(1, world.Data.BroadcastBalance.Keep);
        if (news.Broadcasts.Count > keep)
            news.Broadcasts.RemoveRange(0, news.Broadcasts.Count - keep);
        news.Version++;
        return broadcast;
    }

    static string Nth(IReadOnlyList<string> texts, int id) => texts[(id - 1) % texts.Count];

    /// <summary>Касается ли новость самой страны (а не другой страны игрока, если он её сменил).</summary>
    static bool IsHome(NewsItem item, string country) => item.Home && (item.Country == country || item.Partner == country);

    /// <summary>
    /// Сюжеты: новости за последние WindowDays дней, которых ещё не было в выпусках, по важности (старше недели —
    /// минус Stale за день). Сначала — лучшая своя и лучшая мировая, затем остальные по важности, пока не наберётся
    /// StoriesMin; дальше — только важнее ExtraMin, до StoriesMax. Один сюжет на источник (не три выборы подряд),
    /// мировых сюжетов об одной стране — не больше PerCountry. Если новостей не хватило на StoriesMin, после своих
    /// сюжетов идут «главные цифры страны». В выпуске сначала своё, потом мировое.
    /// </summary>
    public static List<BroadcastStory> Stories(World world, string country, NewsBroadcastRecord tv)
    {
        var news = world.News!;
        var b = world.Data.BroadcastBalance;
        var used = news.Broadcasts.Where(x => x.Country == country).SelectMany(x => x.Stories).Select(s => s.NewsId).ToHashSet();
        var today = world.Date.DayNumber;
        var candidates = news.Items
            .Where(i => i.Date.DayNumber > today - b.WindowDays && i.Date.DayNumber <= today && !used.Contains(i.Id))
            .Select(i => (Item: i, Home: IsHome(i, country), Rank: i.Weight - b.Stale * Math.Max(0, today - i.Date.DayNumber - 7)))
            .OrderByDescending(x => x.Rank).ThenByDescending(x => x.Item.Id)
            .ToList();

        var chosen = new List<(NewsItem Item, bool Home, double Rank)>();
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var perCountry = new Dictionary<string, int>(StringComparer.Ordinal);
        void Take((NewsItem Item, bool Home, double Rank) c)
        {
            if (chosen.Count >= b.StoriesMax || chosen.Any(x => x.Item.Id == c.Item.Id))
                return;
            var source = c.Item.Source.Length > 0 ? c.Item.Source : "id:" + c.Item.Id;
            var place = c.Item.Country ?? "";
            if (sources.Contains(source) || (!c.Home && perCountry.GetValueOrDefault(place) >= b.PerCountry))
                return;
            sources.Add(source);
            if (!c.Home)
                perCountry[place] = perCountry.GetValueOrDefault(place) + 1;
            chosen.Add(c);
        }
        // Свои и мировые новости — в каждом выпуске, если они есть.
        if (candidates.FirstOrDefault(c => c.Home) is { Item: not null } home)
            Take(home);
        if (candidates.FirstOrDefault(c => !c.Home) is { Item: not null } abroad)
            Take(abroad);
        foreach (var c in candidates)
        {
            if (chosen.Count >= b.StoriesMax || (chosen.Count >= b.StoriesMin && c.Rank < b.ExtraMin))
                break;
            Take(c);
        }
        if (chosen.Count == 0)
            return [];

        var names = world.Countries.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        var homeStories = chosen.Where(x => x.Home).OrderByDescending(x => x.Rank).ThenByDescending(x => x.Item.Id).ToList();
        var worldStories = chosen.Where(x => !x.Home).OrderByDescending(x => x.Rank).ThenByDescending(x => x.Item.Id).ToList();
        var stories = new List<BroadcastStory>(chosen.Count + 1);
        foreach (var (item, _, _) in homeStories)
            stories.Add(Story(item, true, stories.Count == 0 ? tv.Home : "", item.Text));
        if (chosen.Count < b.StoriesMin && Figures(world, country, tv.Figures) is { } figures)
            stories.Add(new BroadcastStory(0, country, "economy", "neutral", true, figures,
                Speech.Say(stories.Count == 0 ? tv.HomeQuiet + " " + tv.Figures.Lead : tv.Figures.Lead), Speech.Say(figures)));
        foreach (var (item, _, _) in worldStories)
        {
            var lead = item == worldStories[0].Item ? stories.Count == 0 ? tv.HomeQuiet + " " + tv.World : tv.World : "";
            var place = item.Country == null ? tv.Markets : names.GetValueOrDefault(item.Country, item.Country);
            stories.Add(Story(item, false, lead, $"{place}. {item.Text}"));
        }
        return stories;

        static BroadcastStory Story(NewsItem item, bool home, string lead, string spoken) =>
            new(item.Id, item.Country, item.Topic, item.Tone, home, item.Text, lead.Length > 0 ? Speech.Say(lead) : "", Speech.Say(spoken));
    }

    /// <summary>«Главные цифры страны: рост экономики за год +1,2%, безработица 5,6%…» — показатели, у которых есть значение.</summary>
    static string? Figures(World world, string country, NewsFiguresRecord figures)
    {
        var c = world.ById(country);
        var parts = new List<string>();
        foreach (var f in figures.Stats)
            if (c?.Stats.GetValueOrDefault(f.Stat)?.Value is { } value && double.IsFinite(value))
                parts.Add($"{f.Label} {Fmt.Value(world.Data.Indicator(f.Stat).Format, value)}");
        return parts.Count == 0 ? null : $"{figures.Title}: {string.Join(", ", parts)}";
    }

    /// <summary>
    /// Ведущий страны: прежний, пока не истёк его срок (AnchorYears) и он не дошёл до AnchorRetireAge; иначе — новый
    /// (debut — его первый выпуск). Случайность — своя для страны и номера ведущего, общий генератор мира не трогается.
    /// </summary>
    static (BroadcastAnchor Anchor, bool Debut) Anchor(World world, string country)
    {
        var news = world.News!;
        var b = world.Data.BroadcastBalance;
        var old = news.Anchors.GetValueOrDefault(country);
        if (old != null && world.Date < old.Until && old.Age(world.Date) < b.AnchorRetireAge)
            return (old, false);
        var number = (old?.Number ?? 0) + 1;
        var r = new DeterministicRandom(world.Seed ^ Hash(country) ^ ((ulong)number * 0x9E3779B97F4A7C15UL));
        var female = r.Chance(b.AnchorFemaleShare);
        var age = r.Next(b.AnchorAge[0], b.AnchorAge[1] + 1);
        var years = Math.Max(1, Math.Min(r.Next(b.AnchorYears[0], b.AnchorYears[1] + 1), b.AnchorRetireAge - age));
        var anchor = new BroadcastAnchor(CharacterFactory.RandomName(world.Data, country, female, r), female, world.Date.Year - age,
            world.Date, world.Date.AddYears(years), number);
        news.Anchors[country] = anchor;
        return (anchor, old != null);
    }

    static ulong Hash(string text)
    {
        var h = 14695981039346656037UL;
        foreach (var ch in text)
            h = (h ^ ch) * 1099511628211UL;
        return h;
    }
}
