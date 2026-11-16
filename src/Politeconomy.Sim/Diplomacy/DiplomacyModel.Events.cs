using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Diplomacy;

// Дипломатические последствия событий. MODEL.md, «События».
public static partial class DiplomacyModel
{
    /// <summary>
    /// Событие сдвигает отношения страны: with — partner (страна-партнёр события), neighbors (соседи), powers (первые
    /// пять по мощи), all (все). Сдвиг — память о событии: забывается за несколько лет. У игрока отношения
    /// пересчитываются сразу вместе с торговлей и показателями, у остальных — только сами отношения этой страны.
    /// </summary>
    public static void EventRelations(World world, string country, string with, string? partner, double value)
    {
        if (world.Diplomacy is not { } d || value == 0)
            return;
        Ensure(world);
        IEnumerable<string> targets = with switch
        {
            "partner" => partner != null && world.ById(partner) is not null ? [partner] : [],
            "neighbors" => world.Countries.Select(c => c.Id).Where(id => d.Neighbors.Contains(WorldDiplomacy.Key(country, id))),
            // Место по мощи — из показателя страны (он сохраняется): кэш мощи после загрузки пуст до начала месяца.
            "powers" => world.Countries.Where(c => c.Stats.GetValueOrDefault("power_rank")?.Value <= 5).Select(c => c.Id),
            _ => world.Countries.Select(c => c.Id),
        };
        foreach (var target in targets.Where(t => t != country).OrderBy(t => t, StringComparer.Ordinal).ToList())
            Remember(world, country, target, value);
        // Отношения всегда должны совпадать с памятью о событиях: после загрузки они пересчитываются из неё заново.
        if (country == world.PlayerCountry)
            Refresh(world);
        else
            Recompute(world, [d.Index[country]]);
    }

    /// <summary>
    /// Дипломатическое действие варианта события: подписать договор с партнёром события (он сам его предложил, но общие правила договоров соблюдаются: отношения, союзы, нейтралитет) и/или попросить кредит
    /// Валютного совета (совет может отказать — тогда сказано почему). У ИИ — от имени его страны, пересчёт сразу.
    /// </summary>
    public static void EventAction(World world, string country, string? partner, string? treaty, bool fund)
    {
        if (world.Diplomacy is not { } d || (treaty == null && !fund))
            return;
        Ensure(world);
        var (actor, note) = (d.Actor, d.OnNote);
        if (country != world.PlayerCountry)
        {
            d.Actor = country;
            d.OnNote = _ => { };
        }
        if (treaty != null && partner != null && world.ById(partner) is not null && partner != country
            && (partner != world.PlayerCountry || country == world.PlayerCountry)
            && world.Data.Diplomacy.Treaties.FirstOrDefault(t => t.Id == treaty) is { } record)
        {
            // Партнёр предложил договор сам, но за месяц многое могло измениться: общие правила договоров остаются в силе.
            var (accepts, reason) = Acceptance(world, country, partner, treaty);
            if (accepts)
            {
                d.Treaties.Add(new Treaty(country, partner, treaty, world.Date));
                Note(world, country, "diplomacy", $"Подписан договор со {Instrumental(world, partner)}: «{record.Name}».", "treaty", partner);
                Refresh(world);
            }
            else
                Note(world, country, "diplomacy", $"Договор «{record.Name}» с {Instrumental(world, partner)} не состоялся. {reason}", News.NewsModel.None);
        }
        if (fund)
            RequestLoan(world, country);
        (d.Actor, d.OnNote) = (actor, note);
        if (actor == null && country != world.PlayerCountry && d.Dirty)
        {
            d.Dirty = false;
            Refresh(world);
        }
    }
}
