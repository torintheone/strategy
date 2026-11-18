using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Ai;

/// <summary>
/// Коллапс страны — для проверки «без игрока за 50 лет не меньше 90% стран не впадают в коллапс» (ТЗ, этап 6):
/// реальный ВВП на душу упал больше чем на 30% от старта, гиперинфляция (больше 100%), безработица больше 35%,
/// удовлетворённость ниже 25, три дефолта и больше или восстание прямо сейчас. MODEL.md, «ИИ стран».
/// </summary>
public static class Collapse
{
    public static IReadOnlyList<string> Reasons(World world, Country country)
    {
        var reasons = new List<string>();
        var e = world.Economy![country.Id];
        if (e.RealGdp0 > 0 && e.Population0 > 0 && e.Population > 0)
        {
            var perCapita = e.RealGdp / e.Population / (e.RealGdp0 / e.Population0);
            if (perCapita < 0.7)
                reasons.Add($"ВВП на душу {Fmt.Number(perCapita * 100, 0)}% от старта");
        }
        if (country.Stat("inflation_pct") > 100)
            reasons.Add($"гиперинфляция {Fmt.Number(country.Stat("inflation_pct"), 0)}%");
        if (country.Stat("unemployment_pct") > 35)
            reasons.Add($"безработица {Fmt.Number(country.Stat("unemployment_pct"), 0)}%");
        if (country.Stat("satisfaction") < 25)
            reasons.Add($"удовлетворённость {Fmt.Number(country.Stat("satisfaction"), 0)}");
        if (e.Defaults >= 3)
            reasons.Add($"дефолтов {e.Defaults}");
        if (world.Politics?.Countries.GetValueOrDefault(country.Id)?.Protests.Any(p => p.Stage >= 4) == true)
            reasons.Add("восстание");
        return reasons;
    }
}
