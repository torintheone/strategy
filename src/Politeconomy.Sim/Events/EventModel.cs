using Politeconomy.Sim.Data;
using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Events;

/// <summary>
/// Движок событий. События — данные (data/events.json): условия по состоянию страны, вес, перезарядка, варианты
/// решений с последствиями, цепочки. В дни событий (несколько раз в месяц) в каждой стране может случиться одно
/// событие: чем больше событий, чьи условия выполняются, и чем больше их вес, тем вероятнее. Игрок решает сам (в срок,
/// иначе — вариант по умолчанию), ИИ — по своим приоритетам, с записью в журнал решений. Последствия: разовые (деньги,
/// популярность, группы, отношения, отставка) и временные поправки к модели и настроению классов. MODEL.md, «События».
/// </summary>
public static class EventModel
{
    /// <summary>Сколько событий может одновременно ждать решения игрока: новые не приходят, пока очередь полна.</summary>
    const int MaxPending = 3;
    const int HistoryLimit = 20;

    public static IReadOnlyList<ISimSystem> Systems() => [new Systems.EventSystem()];

    public static void Setup(World world)
    {
        world.Events = WorldEvents.Create(world.Seed ^ 0xE7E27A11C0FFEE5UL);
        Ensure(world);
    }

    /// <summary>Записи для стран, у которых их нет (старые миры, новые страны).</summary>
    public static void Ensure(World world)
    {
        var events = world.Events ??= WorldEvents.Create(world.Seed ^ 0xE7E27A11C0FFEE5UL);
        foreach (var country in world.Countries)
        {
            if (!events.Countries.TryGetValue(country.Id, out var ce))
                events.Countries[country.Id] = ce = new CountryEvents();
            if (ce.Mood.Length != world.Data.Classes.Classes.Count)
                ce.Mood = new double[world.Data.Classes.Classes.Count];
        }
    }

    /// <summary>
    /// День: сроки решений игрока; 1-го числа — истёкшие последствия; в дни событий — продолжения цепочек и новые
    /// события в каждой стране.
    /// </summary>
    public static void Day(World world)
    {
        Ensure(world);
        var events = world.Events!;
        var b = world.Data.EventsBalance;
        var player = world.PlayerCountry;
        if (player != null && events.Countries.TryGetValue(player, out var mine))
            Expire(world, world[player], mine);
        var first = world.Date.Day == 1;
        var eventDay = b.Days.Contains(world.Date.Day);
        if (!first && !eventDay)
            return;
        var match = new EventConditions.Match();
        var candidates = new List<(EventRecord Event, double Chance)>();
        foreach (var country in world.Countries)
        {
            var ce = events[country.Id];
            if (first && ce.Active.RemoveAll(a => a.Until <= world.Date) > 0)
                Recompute(world, ce);
            // События страны, за которую игрок больше не играет, решает её правительство.
            if (ce.Pending.Count > 0 && country.Id != player)
                foreach (var instance in ce.Pending.ToList())
                    Resolve(world, country, ce, instance, Choose(world, country, world.Data.Events.Event(instance.Event)!), late: false);
            if (!eventDay)
                continue;
            Continue(world, country, ce);
            Roll(world, country, ce, match, candidates);
        }
    }

    /// <summary>Вес события сейчас: × уровень природной угрозы × рост с показателем (Scale).</summary>
    static double Weight(Country country, EventRecord e, double hazard)
    {
        var weight = e.Weight * hazard;
        if (e.Scale is { } scale && country.Stats.GetValueOrDefault(scale.Stat)?.Value is { } value && scale.To != scale.From)
            weight *= 1 + (scale.Factor - 1) * Math.Clamp((value - scale.From) / (scale.To - scale.From), 0, 1);
        return weight;
    }

    /// <summary>
    /// Случится ли событие сегодня: шанс каждого подходящего события — Rate × вес / (12 × число дней событий в месяце);
    /// шанс, что случится хоть одно, — 1 − e^(−сумма); какое именно — пропорционально шансам.
    /// </summary>
    static void Roll(World world, Country country, CountryEvents ce, EventConditions.Match match, List<(EventRecord Event, double Chance)> candidates)
    {
        var b = world.Data.EventsBalance;
        if (ce.LastAny is { } last && last.AddDays(b.QuietDays) > world.Date)
            return;
        if (ce.Pending.Count >= MaxPending)
            return;
        candidates.Clear();
        var total = 0.0;
        foreach (var e in world.Data.Events.Events)
        {
            if (e.Chain || e.Weight <= 0)
                continue;
            if (ce.Last.TryGetValue(e.Id, out var when) && when.AddMonths(e.CooldownMonths) > world.Date)
                continue;
            (match.Subject, match.Partner, match.Hazard) = (null, null, 1);
            if (!EventConditions.Holds(world, country, e.When, match))
                continue;
            var chance = b.Rate * Weight(country, e, match.Hazard) / (12.0 * b.Days.Count);
            if (chance <= 0)
                continue;
            candidates.Add((e, chance));
            total += chance;
        }
        if (total <= 0)
            return;
        var random = world.Events!.Random;
        if (!random.Chance(1 - Math.Exp(-total)))
            return;
        var pick = random.NextDouble() * total;
        var chosen = candidates[^1].Event;
        foreach (var (e, chance) in candidates)
        {
            pick -= chance;
            if (pick < 0)
            {
                chosen = e;
                break;
            }
        }
        Fire(world, country, chosen);
    }

    /// <summary>Продолжения цепочек, чей срок пришёл: случаются, если их условия выполняются.</summary>
    static void Continue(World world, Country country, CountryEvents ce)
    {
        foreach (var scheduled in ce.Scheduled.Where(s => s.Date <= world.Date).ToList())
        {
            ce.Scheduled.Remove(scheduled);
            if (world.Data.Events.Event(scheduled.Event) is { } record)
                Fire(world, country, record, scheduled);
        }
    }

    /// <summary>
    /// Событие случилось (если условия выполняются): у игрока — ждёт решения, у остальных — правительство решает сразу.
    /// from — продолжение цепочки: «герой» и партнёр переходят от первого события.
    /// </summary>
    public static EventInstance? Fire(World world, Country country, EventRecord record, EventInstance? from = null)
    {
        Ensure(world);
        var events = world.Events!;
        var ce = events[country.Id];
        var match = new EventConditions.Match { Explain = true };
        if (!EventConditions.Holds(world, country, record.When, match))
            return null;
        if (from?.Parent is { } parentId && world.Data.Events.Event(parentId) is { } parent)
            match.Why.Insert(0, $"Продолжение события «{EventText.Fill(world, country, parent, from, parent.Title)}»");
        // Событие, чей вес зависит от показателя, объясняет себя этим показателем.
        if (record.Scale is { } scale && country.Stats.GetValueOrDefault(scale.Stat)?.Value is { } scaled)
        {
            var def = world.Data.Indicator(scale.Stat);
            var factor = Weight(country, record, 1) / Math.Max(1e-9, record.Weight);
            match.Why.Add($"{def.Name}: {Fmt.Value(def.Format, scaled, def.Unit)} — " + (factor > 1.05 ? $"риск выше обычного в {Fmt.Number(factor, 1)} раза" : "риск обычный"));
        }
        var subject = match.Subject ?? from?.Subject;
        var instance = new EventInstance
        {
            Id = events.NextId++,
            Event = record.Id,
            Date = world.Date,
            Deadline = world.Date.AddDays(world.Data.EventsBalance.DeadlineDays),
            Subject = subject,
            SubjectId = match.Subject == null && from != null ? from.SubjectId
                : subject != null && world.Government?.Countries.GetValueOrDefault(country.Id)?.Minister(subject) is { } person ? person.Id : 0,
            Partner = match.Partner ?? from?.Partner,
            Region = from?.Region ?? (record.Title.Contains("{region}") || record.Text.Contains("{region}") ? PickRegion(world, country) : null),
            Parent = from?.Parent,
            Why = match.Why,
        };
        ce.Last[record.Id] = world.Date;
        ce.LastAny = world.Date;
        events.Counts[record.Id] = events.Counts.GetValueOrDefault(record.Id) + 1;
        if (country.Id == world.PlayerCountry)
        {
            ce.Pending.Add(instance);
            ce.Version++;
            world.Log(country.Id, "event", $"{EventText.Fill(world, country, record, instance, record.Title)}. Нужно ваше решение.");
        }
        else
            Resolve(world, country, ce, instance, Choose(world, country, record), late: false);
        return instance;
    }

    /// <summary>Регион события: случайный регион страны, чаще — населённый.</summary>
    static string? PickRegion(World world, Country country)
    {
        var regions = world.Regions.Where(r => r.Owner == country.Id).ToList();
        var total = regions.Sum(r => r.Info.Population);
        if (regions.Count == 0 || total <= 0)
            return null;
        var pick = world.Events!.Random.NextDouble() * total;
        foreach (var r in regions)
        {
            pick -= r.Info.Population;
            if (pick < 0)
                return r.Id;
        }
        return regions[^1].Id;
    }

    /// <summary>Доступен ли вариант: его условия (Requires) выполняются.</summary>
    public static bool Enabled(World world, Country country, EventOptionRecord option) =>
        option.Requires is not { Count: > 0 } requires || EventConditions.Holds(world, country, requires, new EventConditions.Match());

    /// <summary>Вариант по умолчанию: помеченный Default, иначе последний из доступных.</summary>
    public static EventOptionRecord DefaultOption(World world, Country country, EventRecord record)
    {
        var enabled = record.Options.Where(o => Enabled(world, country, o)).ToList();
        return enabled.FirstOrDefault(o => o.Default) ?? enabled.LastOrDefault() ?? record.Options[^1];
    }

    /// <summary>
    /// Выбор ИИ: польза варианта = Σ приоритет × насколько вариант ему служит (Ai в данных); в долговом кризисе —
    /// минус DebtAversion × цена. При равной пользе — вариант по умолчанию.
    /// </summary>
    public static EventOptionRecord Choose(World world, Country country, EventRecord record)
    {
        var priorities = world.Ai?.Countries.GetValueOrDefault(country.Id)?.Priorities;
        var crisis = world.Ai != null && Ai.AiModel.Assess(world, country).DebtCrisis(world.Data.AiBalance);
        var fallback = DefaultOption(world, country, record);
        var best = (Option: fallback, Utility: double.NegativeInfinity);
        foreach (var o in record.Options)
        {
            if (!Enabled(world, country, o))
                continue;
            var utility = 0.0;
            for (var k = 0; k < 4; k++)
                utility += (priorities != null && k < priorities.Length ? priorities[k] : 0.25) * (o.Ai != null && k < o.Ai.Count ? o.Ai[k] : 0);
            if (crisis)
                utility -= world.Data.EventsBalance.DebtAversion * Math.Max(0, o.Cost);
            if (utility > best.Utility + 1e-9 || (Math.Abs(utility - best.Utility) <= 1e-9 && o == fallback))
                best = (o, utility);
        }
        return best.Option;
    }

    /// <summary>Решение игрока по событию, которое ждёт решения.</summary>
    public static bool Decide(World world, string countryId, int instanceId, string optionId)
    {
        if (world.PlayerCountry != countryId || world.Events?.Countries.GetValueOrDefault(countryId) is not { } ce)
            return false;
        var country = world[countryId];
        if (ce.Pending.FirstOrDefault(i => i.Id == instanceId) is not { } instance || world.Data.Events.Event(instance.Event) is not { } record)
            return false;
        if (record.Option(optionId) is not { } option || !Enabled(world, country, option))
            return false;
        Resolve(world, country, ce, instance, option, late: false);
        return true;
    }

    /// <summary>Срок решения вышел: срабатывает вариант по умолчанию.</summary>
    static void Expire(World world, Country country, CountryEvents ce)
    {
        foreach (var instance in ce.Pending.Where(i => i.Deadline <= world.Date).ToList())
            if (world.Data.Events.Event(instance.Event) is { } record)
                Resolve(world, country, ce, instance, DefaultOption(world, country, record), late: true);
            else
                ce.Pending.Remove(instance);
    }

    /// <summary>Событие решено: последствия, история, новости, журнал (игрока или решений ИИ).</summary>
    static void Resolve(World world, Country country, CountryEvents ce, EventInstance instance, EventOptionRecord option, bool late)
    {
        var events = world.Events!;
        var record = world.Data.Events.Event(instance.Event)!;
        ce.Pending.Remove(instance);
        Apply(world, country, ce, instance, record, option);
        var title = EventText.Fill(world, country, record, instance, record.Title);
        var news = new EventNews(world.Date, country.Id, record.Id, option.Id, title, option.Name, instance.Region);
        ce.History.Add(news);
        if (ce.History.Count > HistoryLimit)
            ce.History.RemoveRange(0, ce.History.Count - HistoryLimit);
        events.Recent.Add(news);
        News.NewsModel.Event(world, record, instance, news);
        var limit = world.Data.EventsBalance.Recent;
        if (events.Recent.Count > limit)
            events.Recent.RemoveRange(0, events.Recent.Count - limit);
        ce.Version++;
        if (country.Id == world.PlayerCountry)
            world.Log(country.Id, "event", late
                ? $"{title}: решение не принято в срок — «{option.Name}»."
                : $"{title}: {Lower(option.Name)}.");
        else if (world.Ai?.Countries.GetValueOrDefault(country.Id) is { } ai)
            new Ai.AiTurn(world, country, ai).Record("event", $"{title}: {Lower(option.Name)}.",
                (instance.Why.Count > 0 ? string.Join("; ", instance.Why) + " — " : "") + $"выбор по приоритетам: {Ai.AiModel.Describe(ai)}");
    }

    /// <summary>Последствия варианта: разовые — сразу, поправки к модели и настроению — на срок Months.</summary>
    static void Apply(World world, Country country, CountryEvents ce, EventInstance instance, EventRecord record, EventOptionRecord o)
    {
        var id = country.Id;
        if (o.Cost != 0 && world.Economy?.Countries.GetValueOrDefault(id) is { } e)
            e.Debt = Math.Max(0, e.Debt + o.Cost / 100 * e.NominalGdp);
        var g = world.Government?.Countries.GetValueOrDefault(id);
        if (o.Popularity != 0 && g != null)
            g.Leader.Popularity = Math.Clamp(g.Leader.Popularity + o.Popularity, 1, 99);
        var p = world.Politics?.Countries.GetValueOrDefault(id);
        if (o.Groups is { } groups && p != null)
        {
            var records = world.Data.InterestGroups.Groups;
            for (var i = 0; i < records.Count && i < p.Groups.Count; i++)
                if (groups.TryGetValue(records[i].Id, out var shift))
                    p.Groups[i].Memory += shift;
        }
        if (o.Loyalty is { } loyalty && p != null)
        {
            p.Security.PoliceLoyalty = Math.Clamp(p.Security.PoliceLoyalty + loyalty.GetValueOrDefault("police"), 0, 100);
            p.Security.ArmyLoyalty = Math.Clamp(p.Security.ArmyLoyalty + loyalty.GetValueOrDefault("army"), 0, 100);
        }
        if (o.Relations is { } relations)
            DiplomacyModel.EventRelations(world, id, relations.With, instance.Partner, relations.Value);
        if (o.Subject != null && instance.Subject is { } post && g?.Minister(post) is { } minister && minister.Id == instance.SubjectId)
        {
            if (o.Subject == "fire")
                Dismiss(world, country, g, post, minister);
            else if (double.TryParse(o.Subject, System.Globalization.CultureInfo.InvariantCulture, out var shift))
                minister.Loyalty = Math.Clamp(minister.Loyalty + shift, 0, 100);
        }
        if (o.Next is { } next)
            foreach (var n in next)
                if (world.Events!.Random.Chance(n.Chance))
                    ce.Scheduled.Add(new EventInstance
                    {
                        Event = n.Event, Date = world.Date.AddMonths(n.Months), Subject = instance.Subject, SubjectId = instance.SubjectId,
                        Partner = instance.Partner, Region = instance.Region, Parent = record.Id,
                    });
        if (o.Months > 0 && (o.Effects is { Count: > 0 } || o.Moods is { Count: > 0 }))
        {
            // Тот же вариант того же события не складывается сам с собой: срок просто продлевается.
            ce.Active.RemoveAll(a => a.Event == record.Id && a.Option == o.Id);
            ce.Active.Add(new ActiveEffect(record.Id, o.Id, world.Date, world.Date.AddMonths(o.Months)));
            Recompute(world, ce);
        }
    }

    /// <summary>Министр уходит в отставку по решению события; на пост назначается новый человек.</summary>
    static void Dismiss(World world, Country country, CountryGovernment g, string post, Character minister)
    {
        var record = world.Data.Government.Posts.First(x => x.Id == post);
        var successor = CharacterFactory.Create(world, country, record, leader: false);
        Politics.PoliticsModel.Affiliate(world, country.Id, successor, minister.Party);
        successor.Since = world.Date;
        g.Ministers[post] = successor;
        g.Candidates.Remove(post);
        g.Version++;
        if (country.Id == world.PlayerCountry)
            world.Log(country.Id, "resignation", $"{record.Name} {minister.Name} отправлен{(minister.Female ? "а" : "")} в отставку. " +
                                               $"На пост назначен{(successor.Female ? "а" : "")} {successor.Name}.");
    }

    /// <summary>Сумма действующих последствий: поправки к модели и сдвиги удовлетворённости классов.</summary>
    public static void Recompute(World world, CountryEvents ce)
    {
        var classes = world.Data.Classes.Classes;
        ce.Effects.Clear();
        if (ce.Mood.Length != classes.Count)
            ce.Mood = new double[classes.Count];
        Array.Clear(ce.Mood);
        foreach (var a in ce.Active)
        {
            if (world.Data.Events.Event(a.Event)?.Option(a.Option) is not { } o)
                continue;
            if (o.Effects is { } effects)
                foreach (var (key, value) in effects)
                    ce.Effects[key] = ce.Effects.GetValueOrDefault(key) + value;
            if (o.Moods is { } moods)
                for (var k = 0; k < classes.Count; k++)
                    ce.Mood[k] += moods.GetValueOrDefault(classes[k].Id);
        }
        ce.Version++;
    }

    static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
