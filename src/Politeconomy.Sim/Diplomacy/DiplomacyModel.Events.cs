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
}
