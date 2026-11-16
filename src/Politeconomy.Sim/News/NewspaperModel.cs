using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.News;

/// <summary>
/// Газета: выходит 1-го числа для страны игрока. Первая полоса — заголовок месяца и три материала из ленты за месяц.
/// Тон зависит от свободы прессы: независимая газета пишет обо всём и разоблачает министров, которые приукрасили
/// доклад; близкая к власти молчит о неприятном дома; государственная молчит обо всём плохом дома и хвалит
/// руководство. Газета ничего не меняет в модели. MODEL.md, «Газета».
/// </summary>
public static class NewspaperModel
{
    /// <summary>Месяц: номер для страны игрока (у наблюдателя газеты нет).</summary>
    public static void Month(World world)
    {
        if (world.PlayerCountry is { } player && world.ById(player) != null)
            Issue(world, player);
    }

    /// <summary>Тон издания по свободе прессы страны: free, loyal или state.</summary>
    public static string Press(World world, string country)
    {
        var b = world.Data.NewspaperBalance;
        var freedom = world.ById(country)?.Stats.GetValueOrDefault("press_freedom_index")?.Value ?? 50;
        return freedom >= b.FreeMin ? "free" : freedom >= b.LoyalMin ? "loyal" : "state";
    }

    public static Newspaper? Issue(World world, string country)
    {
        NewsModel.Ensure(world);
        if (world.Data.News.Newspaper is not { } texts)
            return null;
        var news = world.News!;
        var b = world.Data.NewspaperBalance;
        var press = Press(world, country);
        var desk = texts.Press(press);
        var id = news.NextPaper++;
        var leader = world.Government?.Countries.GetValueOrDefault(country)?.Leader.Name ?? "";
        var from = news.Papers.LastOrDefault(p => p.Country == country)?.Date ?? world.Date.AddMonths(-1);

        var candidates = new List<(PaperStory Story, bool Home, int Weight, string Source, long Order)>();
        foreach (var item in news.Items.Where(i => i.Date > from && i.Date <= world.Date))
        {
            var home = item.Home && (item.Country == country || item.Partner == country);
            // Близкая к власти газета молчит о неприятном дома, кроме того, о чём молчать нельзя; государственная — обо всём.
            if (home && item.Tone == "bad" && (press == "state" || (press == "loyal" && item.Weight < b.HideMax)))
                continue;
            candidates.Add((new PaperStory(item.Id, "news", item.Country, item.Topic, item.Tone, item.Text, ""), home, item.Weight,
                item.Source.Length > 0 ? item.Source : "id:" + item.Id, item.Id));
        }
        if (press == "free" && Expose(world, country, texts) is { } expose)
            candidates.Add((expose, true, b.ExposeWeight, "expose", long.MaxValue));

        var chosen = new List<(PaperStory Story, bool Home, int Weight, string Source, long Order)>();
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var places = new HashSet<string>(StringComparer.Ordinal);
        var ordered = candidates.OrderByDescending(c => c.Weight).ThenByDescending(c => c.Order).ToList();
        void Take((PaperStory Story, bool Home, int Weight, string Source, long Order) c)
        {
            if (chosen.Count > b.Stories || sources.Contains(c.Source) || (!c.Home && !places.Add(c.Story.Country ?? "")))
                return;
            sources.Add(c.Source);
            chosen.Add(c);
        }
        // Заголовок месяца — самое важное дома; среди материалов — хотя бы одна мировая новость, если она была.
        if (ordered.FirstOrDefault(c => c.Home) is { Story: not null } mine)
            Take(mine);
        if (ordered.FirstOrDefault(c => !c.Home) is { Story: not null } abroad)
            Take(abroad);
        foreach (var c in ordered)
            Take(c);

        // Сначала своё, потом мировое — каждое по важности.
        var stories = chosen.OrderByDescending(c => c.Home).ThenByDescending(c => c.Weight).ThenByDescending(c => c.Order)
            .Select((c, k) => c.Story.Kind == "news" ? c.Story with { Body = Body(desk, c.Story, c.Home, id + k, leader) } : c.Story).ToList();
        // Государственная газета без хороших новостей дома начинает с похвалы руководству.
        if (press == "state" && !chosen.Any(c => c.Home))
            stories.Insert(0, Praise(texts, id, leader));
        PaperStory lead;
        if (stories.Count > 0)
        {
            lead = stories[0];
            stories.RemoveAt(0);
        }
        else
            lead = new PaperStory(0, "quiet", country, "society", "neutral", texts.Quiet, texts.QuietBody);
        if (stories.Count > b.Stories)
            stories.RemoveRange(b.Stories, stories.Count - b.Stories);
        var paper = new Newspaper(id, world.Date, country, press, desk.Name, desk.Motto, lead, stories);
        news.Papers.Add(paper);
        var keep = Math.Max(1, b.Keep);
        if (news.Papers.Count > keep)
            news.Papers.RemoveRange(0, news.Papers.Count - keep);
        news.Version++;
        return paper;
    }

    static string Body(NewspaperPressRecord desk, PaperStory story, bool home, int k, string leader)
    {
        var pool = !home ? desk.World : story.Tone switch { "good" => desk.Good, "bad" => desk.Bad, _ => desk.Neutral };
        return pool[k % pool.Count].Replace("{leader}", leader);
    }

    static PaperStory Praise(NewspaperRecord texts, int id, string leader) =>
        new(0, "praise", null, "politics", "good", texts.Praise[id % texts.Praise.Count].Replace("{leader}", leader), texts.PraiseBody.Replace("{leader}", leader));

    /// <summary>
    /// Разоблачение: министр страны, который приукрашивает доклад сильнее всех (не меньше ExposeSpin), и показатель его
    /// ведомства, где цифра в докладе дальше всего от настоящей (не меньше доли ExposeGap). Об одном министре — не
    /// чаще раза в ExposeMonths месяцев, а в стране вообще — не чаще раза в ExposeEvery месяцев.
    /// </summary>
    public static PaperStory? Expose(World world, string country, NewspaperRecord texts)
    {
        var data = world.Data;
        var b = data.NewspaperBalance;
        var news = world.News!;
        if (world.Government?.Countries.GetValueOrDefault(country) is not { } g || world.ById(country) is not { } c)
            return null;
        if (news.Exposed.TryGetValue(country, out var last) && world.Date < last.AddMonths(b.ExposeEvery))
            return null;
        (Government.Character Who, PostRecord Post, string Stat, double Real, double Shown, double Spin)? best = null;
        foreach (var post in data.Government.Posts)
        {
            if (g.Minister(post.Id) is not { } m)
                continue;
            var key = $"{country}/{m.Id}";
            if (news.Exposed.TryGetValue(key, out var when) && world.Date < when.AddMonths(b.ExposeMonths))
                continue;
            var spin = MinistryReport.Spin(data.ReportsBalance, m.Competence, m.Corruption, m.Traits);
            if (spin < b.ExposeSpin || (best is { } top && spin <= top.Spin))
                continue;
            var gap = 0.0;
            (string, double, double)? worst = null;
            foreach (var stat in post.Stats ?? [])
            {
                if (!MinistryReport.Spinnable(data, stat) || c.Stats.GetValueOrDefault(stat)?.Value is not { } real || !double.IsFinite(real))
                    continue;
                var shown = MinistryReport.Reported(data, stat, real, Median(world, stat), spin);
                var format = data.Indicator(stat).Format;
                var relative = Math.Abs(shown - real) / Math.Max(Math.Abs(real), 1e-6);
                if (relative >= b.ExposeGap && relative > gap && Fmt.Value(format, shown) != Fmt.Value(format, real))
                {
                    gap = relative;
                    worst = (stat, real, shown);
                }
            }
            if (worst is { } w)
                best = (m, post, w.Item1, w.Item2, w.Item3, spin);
        }
        if (best is not { } e)
            return null;
        news.Exposed[$"{country}/{e.Who.Id}"] = world.Date;
        news.Exposed[country] = world.Date;
        var def = data.Indicator(e.Stat);
        string Fill(string text) => text
            .Replace("{post_lower}", char.ToLowerInvariant(e.Post.Name[0]) + e.Post.Name[1..])
            .Replace("{post}", e.Post.Name)
            .Replace("{minister}", e.Who.Name)
            .Replace("{stat}", def.Name)
            .Replace("{reported}", Fmt.Value(def.Format, e.Shown, def.Unit))
            .Replace("{real}", Fmt.Value(def.Format, e.Real, def.Unit))
            .Replace("{a}", e.Who.Female ? "а" : "");
        return new PaperStory(0, "expose", country, "politics", "bad", Fill(texts.Expose), Fill(texts.ExposeBody));
    }

    /// <summary>Медиана показателя по странам мира — так же, как в докладе министра.</summary>
    static double? Median(World world, string stat)
    {
        var values = world.Countries.Select(c => c.Stats.GetValueOrDefault(stat)?.Value ?? double.NaN).Where(double.IsFinite).OrderBy(v => v).ToList();
        return values.Count == 0 ? null : values[values.Count / 2];
    }
}
