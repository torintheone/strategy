using Politeconomy.Sim.Data;
using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Events;

/// <summary>
/// Условия событий: предикаты из данных (<see cref="EventCondition"/>) по состоянию страны. Проверка заодно находит
/// «героя» события (министра), страну-партнёра и уровень природной угрозы и объясняет словами, почему условие
/// выполнено. MODEL.md, «События».
/// </summary>
public static class EventConditions
{
    /// <summary>Что нашли условия: министр («герой»), партнёр, множитель веса от природной угрозы, объяснения.</summary>
    public sealed class Match
    {
        public string? Subject;
        public string? Partner;
        public double Hazard = 1;
        /// <summary>Собирать ли объяснения (при еженедельном переборе событий они не нужны — только для случившегося).</summary>
        public bool Explain;
        public List<string> Why = [];
    }

    public static bool Holds(World world, Country c, IReadOnlyList<EventCondition> conditions, Match m)
    {
        foreach (var condition in conditions)
            if (!Holds(world, c, condition, m))
                return false;
        return true;
    }

    static bool In(double? value, EventCondition x) =>
        value is { } v && double.IsFinite(v) && (x.Min is not { } min || v >= min) && (x.Max is not { } max || v <= max);

    /// <summary>«выше 8» / «ниже 45» / «от 3 до 8» — порог словами.</summary>
    static string Bound(EventCondition x, Func<double, string> format) =>
        x is { Min: { } min, Max: { } max } ? $"от {format(min)} до {format(max)}" : x.Min is { } lo ? $"не ниже {format(lo)}" : x.Max is { } hi ? $"не выше {format(hi)}" : "";

    public static bool Holds(World world, Country c, EventCondition x, Match m)
    {
        var data = world.Data;
        if (x.Stat is { } stat)
        {
            var value = c.Stats.GetValueOrDefault(stat)?.Value;
            if (!In(value, x))
                return false;
            if (m.Explain)
            {
                var def = data.Indicator(stat);
                m.Why.Add($"{def.Name}: {Fmt.Value(def.Format, value!.Value, def.Unit)} ({Bound(x, v => Fmt.Value(def.Format, v, def.Unit))})");
            }
            return true;
        }
        if (x.World is { } what)
        {
            var value = world.Economy is not { } economy ? (double?)null : what switch
            {
                "oil_price" => economy.OilPrice,
                "ores_price" => economy.OresPrice,
                "food_price" => economy.FoodPrice,
                _ => null,
            };
            if (!In(value, x))
                return false;
            if (m.Explain)
            {
                var name = what switch { "oil_price" => "Мировая цена нефти", "ores_price" => "Мировые цены на руду и металлы", _ => "Мировые цены на продовольствие" };
                m.Why.Add($"{name}: {Fmt.Number(value!.Value * 100, 0)}% от обычного уровня");
            }
            return true;
        }
        if (x.Law is { } law)
        {
            var current = world.Laws?.Countries.GetValueOrDefault(c.Id)?.Current.GetValueOrDefault(law);
            if (current == null || x.In?.Contains(current) != true)
                return false;
            if (m.Explain)
            {
                var record = data.Law(law);
                m.Why.Add($"Закон «{record.Name}»: {record.Variant(current).Name.ToLowerInvariant()}");
            }
            return true;
        }
        if (x.Regime is { } regimes)
            return regimes.Contains(WorldFactory.RegimeId(c.Regime));
        if (x.Income is { } incomes)
            return incomes.Contains(WorldFactory.IncomeId(c.Income));
        if (x.System is { } systems)
            return systems.Contains(WorldFactory.SystemId(c.System));
        if (x.Tech is { } tech)
        {
            var known = world.Science?.Countries.GetValueOrDefault(c.Id)?.Researched.Contains(tech) == true;
            if (known != (x.Is ?? true))
                return false;
            if (m.Explain && known && data.Science.Technologies.FirstOrDefault(t => t.Id == tech) is { } t)
                m.Why.Add($"Освоена технология «{t.Name}»");
            return true;
        }
        if (x.Program is { } program)
            return (world.Programs?.Countries.GetValueOrDefault(c.Id)?.Current.Contains(program) == true) == (x.Is ?? true);
        if (x.Class is { } cls)
        {
            var k = Index(data.Classes.Classes, cls, r => r.Id);
            var s = world.Society?.Countries.GetValueOrDefault(c.Id);
            var radicalism = x.What == "radicalism";
            var value = s == null || k < 0 ? (double?)null : radicalism ? s.Radicalism[k] : s.Satisfaction[k];
            if (!In(value, x))
                return false;
            if (m.Explain) m.Why.Add($"{data.Classes.Classes[k].Name}: {(radicalism ? "радикализм" : "удовлетворённость")} {Fmt.Number(value!.Value, 0)} ({Bound(x, v => Fmt.Number(v, 0))})");
            return true;
        }
        if (x.Group is { } group)
        {
            var i = Index(data.InterestGroups.Groups, group, r => r.Id);
            var groups = world.Politics?.Countries.GetValueOrDefault(c.Id)?.Groups;
            var value = groups == null || i < 0 || i >= groups.Count ? (double?)null : groups[i].Approval;
            if (!In(value, x))
                return false;
            if (m.Explain) m.Why.Add($"{data.InterestGroups.Groups[i].Name}: одобрение власти {Fmt.Number(value!.Value, 0)} ({Bound(x, v => Fmt.Number(v, 0))})");
            return true;
        }
        if (x.Funding is { } item)
        {
            var value = Funding(world, c.Id, item);
            if (!In(value, x))
                return false;
            if (m.Explain) m.Why.Add($"{data.Budget.Spending.First(s => s.Id == item).Name}: {Fmt.Number(value!.Value * 100, 0)}% от расходов на старте");
            return true;
        }
        if (x.Minister is { } post)
            return Minister(world, c, x, post, m);
        if (x.Partner is { } partner)
            return Partner(world, c, partner, m);
        if (x.Hazard is { } hazard)
        {
            var level = data.Events.Hazards.GetValueOrDefault(hazard)?.GetValueOrDefault(c.Id) ?? 0;
            if (level <= 0)
                return false;
            m.Hazard *= level;
            if (m.Explain)
                m.Why.Add($"Страна в зоне риска: {data.Events.HazardNames?.GetValueOrDefault(hazard) ?? hazard} (уровень {Fmt.Number(level, 0)} из 3)");
            return true;
        }
        if (x.War is { } war)
            return Military.WarModel.AtWar(world, c.Id) == war;
        if (x.Any is { } any)
        {
            // Первое выполненное условие объясняет событие; остальные не проверяются.
            foreach (var nested in any)
                if (Holds(world, c, nested, m))
                    return true;
            return false;
        }
        return true;
    }

    /// <summary>План расходов на статью (доля ВВП) / план на старте; null — нет данных.</summary>
    public static double? Funding(World world, string country, string item)
    {
        var k = Index(world.Data.Budget.Spending, item, s => s.Id);
        var e = world.Economy?.Countries.GetValueOrDefault(country);
        var start = world.Politics?.Countries.GetValueOrDefault(country)?.StartSpending;
        if (e == null || start == null || k < 0 || k >= start.Length || k >= e.SpendingPlan.Length || start[k] <= 0)
            return null;
        return e.SpendingPlan[k] / start[k];
    }

    /// <summary>Министр по условию: самый «подходящий» (с наибольшим значением при Min, с наименьшим — при Max) становится героем события.</summary>
    static bool Minister(World world, Country c, EventCondition x, string post, Match m)
    {
        if (world.Government?.Countries.GetValueOrDefault(c.Id) is not { } g)
            return false;
        var best = (Post: (string?)null, Value: 0.0);
        foreach (var p in world.Data.Government.Posts)
        {
            if ((post != "any" && p.Id != post) || g.Minister(p.Id) is not { } person)
                continue;
            var value = x.What switch
            {
                "competence" => person.Competence,
                "loyalty" => person.Loyalty,
                "popularity" => person.Popularity,
                _ => person.Corruption,
            };
            if (!In(value, x))
                continue;
            var better = best.Post == null || (x.Min != null ? value > best.Value : value < best.Value);
            if (better)
                best = (p.Id, value);
        }
        if (best.Post == null)
            return false;
        m.Subject = best.Post;
        if (!m.Explain)
            return true;
        var name = x.What switch { "competence" => "компетентность", "loyalty" => "лояльность", "popularity" => "популярность", _ => "склонность к коррупции" };
        var record = world.Data.Government.Posts.First(p => p.Id == best.Post);
        m.Why.Add($"{record.Name} {g.Minister(best.Post)!.Name}: {name} {Fmt.Number(best.Value, 0)} ({Bound(x, v => Fmt.Number(v, 0))})");
        return true;
    }

    /// <summary>Страна-партнёр по условиям: из подходящих — с самой большой долей в нашей торговле.</summary>
    static bool Partner(World world, Country c, EventPartner x, Match m)
    {
        if (world.Diplomacy is not { } d)
            return false;
        var best = (Id: (string?)null, Trade: -1.0);
        foreach (var other in world.Countries)
        {
            if (other.Id == c.Id)
                continue;
            if (x.Neighbor is { } neighbor && d.Neighbors.Contains(WorldDiplomacy.Key(c.Id, other.Id)) != neighbor)
                continue;
            var trade = DiplomacyModel.TradeShare(world, c.Id, other.Id);
            if (trade <= best.Trade || (x.TradeMin is { } tradeMin && trade * 100 < tradeMin))
                continue;
            var relations = DiplomacyModel.Relations(world, c.Id, other.Id);
            if ((x.RelationsMin is { } min && relations < min) || (x.RelationsMax is { } max && relations > max))
                continue;
            // Место по мощи — из показателя страны (он сохраняется), а не из кэша дипломатии: после загрузки кэш пуст до начала месяца.
            if (x.RankMax is { } rank && !(other.Stats.GetValueOrDefault("power_rank")?.Value <= rank))
                continue;
            if (x.War is { } war && Military.WarModel.AtWar(world, other.Id) != war)
                continue;
            if (x.Recent is { } category && !Recent(world, other.Id, category))
                continue;
            best = (other.Id, trade);
        }
        if (best.Id == null)
            return false;
        m.Partner = best.Id;
        if (m.Explain) m.Why.Add($"{world[best.Id].Name}: отношения {Fmt.Signed(DiplomacyModel.Relations(world, c.Id, best.Id), 0)}, доля в нашей торговле {Fmt.Number(best.Trade * 100, 1)}%");
        return true;
    }

    /// <summary>Было ли в стране за последние два месяца событие этой категории.</summary>
    static bool Recent(World world, string country, string category)
    {
        if (world.Events?.Countries.GetValueOrDefault(country) is not { } ce)
            return false;
        for (var i = ce.History.Count - 1; i >= 0; i--)
        {
            var news = ce.History[i];
            if (news.Date.AddMonths(2) < world.Date)
                return false;
            if (world.Data.Events.Event(news.Event)?.Category == category)
                return true;
        }
        return false;
    }

    static int Index<T>(IReadOnlyList<T> list, string id, Func<T, string> key)
    {
        for (var i = 0; i < list.Count; i++)
            if (key(list[i]) == id)
                return i;
        return -1;
    }
}
