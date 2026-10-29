using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Politics;

/// <summary>Решения игрока о политике и о самой партии. Вызываются в потоке симуляции (через SimulationHost.Post).</summary>
public static class PoliticsActions
{
    /// <summary>Показатели, которые запоминаются в начале правления и сравниваются в итогах.</summary>
    public static readonly string[] ReignStats =
        ["gdp_per_capita_usd", "gdp_growth_pct", "unemployment_pct", "inflation_pct", "gov_debt_pct_gdp", "standard_of_living", "approval_pct", "ruling_poll_pct"];

    /// <summary>Начать игру за страну: запоминается дата и показатели на начало правления.</summary>
    public static void PlayAs(World world, string countryId)
    {
        world.PlayerCountry = countryId;
        world.Defeat = null;
        world.Reign = new Reign { Country = countryId, Since = world.Date, Start = Stats(world, countryId) };
    }

    static Dictionary<string, double> Stats(World world, string countryId)
    {
        var stats = world[countryId].Stats;
        return ReignStats.Where(stats.ContainsKey).ToDictionary(id => id, id => Math.Round(stats[id].Value, 4), StringComparer.Ordinal);
    }

    /// <summary>После поражения на выборах: продолжить за новое правительство той же страны.</summary>
    public static void ContinueAfterDefeat(World world) => world.Defeat = null;

    /// <summary>Закончить партию: итоги правления запоминаются, игрок становится наблюдателем.</summary>
    public static void EndReign(World world)
    {
        if (world.Reign is { } reign)
            world.LastReign = new ReignSummary(reign.Country, reign.Since, world.Date, reign.Start, Stats(world, reign.Country),
                reign.ElectionsWon, reign.ElectionsLost, reign.LawsPassed);
        world.PlayerCountry = null;
        world.Reign = null;
        world.Defeat = null;
    }

    /// <summary>
    /// Досрочные выборы парламента через два месяца: в парламентской республике, конституционной монархии и
    /// смешанной республике (президент распускает парламент), если парламент выбирают всерьёз.
    /// </summary>
    public static bool CallEarlyElection(World world, string countryId)
    {
        var country = world[countryId];
        var p = world.Politics?.Countries.GetValueOrDefault(countryId);
        if (!CanCallEarlyElection(world, country) || p == null)
            return false;
        var date = world.Date.AddMonths(2);
        p.NextElection = new DateOnly(date.Year, date.Month, 1);
        p.Version++;
        world.Log(countryId, "election", $"Парламент распущен. Досрочные выборы — {Text.Fmt.Date(p.NextElection.Value)}.");
        return true;
    }

    public static bool CanCallEarlyElection(World world, Country country) =>
        country.Regime != Regime.Authoritarian
        && country.System is PoliticalSystem.ParliamentaryRepublic or PoliticalSystem.ConstitutionalMonarchy or PoliticalSystem.SemiPresidential
        && world.Politics?.Countries.GetValueOrDefault(country.Id) is { Parties.Count: > 0, Talks: null }
        && world.Government?.Countries.GetValueOrDefault(country.Id)?.Parliament.Exists == true;

    /// <summary>Собрать коалицию с выбранными партнёрами (переговоры после выборов).</summary>
    public static bool FormCoalition(World world, string countryId, IReadOnlyCollection<string> partners) =>
        CoalitionTalksModel.Accept(world, countryId, partners);
}
