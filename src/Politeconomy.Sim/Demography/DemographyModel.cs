using Politeconomy.Sim.Data;
using Politeconomy.Sim.Demography.Systems;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Demography;

/// <summary>Сборка демографии: стартовое население по данным и системы в порядке MODEL.md.</summary>
public static class DemographyModel
{
    public static IReadOnlyList<ISimSystem> Systems() =>
    [
        new VitalRatesSystem(),
        new PopulationSystem(),
        new DemographyStatsSystem(),
    ];

    public static void Setup(World world)
    {
        var data = world.Data;
        var b = data.Demography;
        var records = data.Population.Countries.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var demography = new WorldDemography();
        foreach (var country in world.Countries)
        {
            if (!records.TryGetValue(country.Id, out var r))
                throw new DataException($"Нет демографии страны {country.Id} в population.json");
            demography.Countries[country.Id] = Calibrate(country, r, b, world.Date.Year);
        }
        world.Demography = demography;
        DemographyStatsSystem.Publish(world);
    }

    static CountryDemography Calibrate(Country country, PopulationRecord r, DemographyBalance b, int startYear)
    {
        double S(string id, double fallback) => country.Info.Stats.TryGetValue(id, out var v) ? v.Value : fallback;
        var d = new CountryDemography();
        var total = S("population", r.Female.Sum() + r.Male.Sum());
        var scale = total / Math.Max(1, r.Female.Sum() + r.Male.Sum());
        for (var g = 0; g < CountryDemography.Groups; g++)
        {
            d.Female[g] = r.Female[g] * scale;
            d.Male[g] = r.Male[g] * scale;
        }

        // Рождаемость: стандартный профиль по возрасту матери, доля 15–19 лет — по данным о подростковых рождениях.
        d.Fertility = d.BaseFertility = Math.Clamp(r.Fertility, 0.5, 8);
        var shares = b.FertilitySchedule.ToArray();
        var teen = Math.Clamp(r.AdolescentFertility / 1000 * 5 / d.Fertility, 0.005, 0.35);
        var restScale = (1 - teen) / shares.Skip(1).Sum();
        d.FertilityShares = shares.Select((x, i) => i == 0 ? teen : x * restScale).ToArray();
        d.FertilityGap0 = r.Path is { } path ? d.Fertility - PathValue(path.Years, path.Fertility, startYear) : 0;

        d.LifeExpectancy = d.LifeExpectancy0 = (r.LifeExpectancyFemale + r.LifeExpectancyMale) / 2;
        d.LifeGap = d.LifeGap0 = r.LifeExpectancyFemale - r.LifeExpectancyMale;
        d.InfantMortality = d.InfantMortality0 = r.InfantMortality;
        UpdateMortality(d, b);

        d.SexRatioAtBirth = b.SexRatioOverrides.GetValueOrDefault(country.Id, b.SexRatioAtBirth);
        d.NetMigrationRate = r.NetMigration / total;
        d.UrbanPct = d.UrbanPct0 = r.UrbanPct;
        d.UrbanChange = r.UrbanChange;
        var labor = S("labor_force", 0.45 * total);
        d.Participation = labor / Math.Max(1, d.WorkingAge + b.OlderParticipation * d.Elderly);

        // История за год до старта: рождения и смерти — по нынешним коэффициентам, численность — по приросту.
        var growth = S("population_growth_pct", 1) / 100;
        var (births, deaths) = MonthlyVitals(d);
        for (var j = 0; j < 12; j++)
        {
            d.BirthsHistory[j] = births;
            d.DeathsHistory[j] = deaths;
            d.MigrationHistory[j] = d.NetMigrationRate * total / 12;
            d.TotalHistory[j] = total / Math.Pow(1 + growth, (12 - j) / 12.0);
        }
        d.LaborGrowth = growth;
        return d;
    }

    /// <summary>Рождения и смерти за месяц при текущих коэффициентах (без изменения численности).</summary>
    public static (double Births, double Deaths) MonthlyVitals(CountryDemography d)
    {
        var births = 0.0;
        for (var k = 0; k < CountryDemography.MotherGroups; k++)
            births += d.Female[CountryDemography.FirstMotherGroup + k] * d.Fertility * d.FertilityShares[k] / 5 / 12;
        var deaths = 0.0;
        for (var g = 0; g < CountryDemography.Groups; g++)
            deaths += (d.Female[g] * d.MortalityFemale[g] + d.Male[g] * d.MortalityMale[g]) / 12;
        return (births, deaths);
    }

    public static void UpdateMortality(CountryDemography d, DemographyBalance b)
    {
        var femaleShare = 2 / (1 + b.InfantMaleExcess);
        d.MortalityFemale = MortalityModel.Rates(d.LifeExpectancyFemale, d.InfantMortality * femaleShare, b);
        d.MortalityMale = MortalityModel.Rates(d.LifeExpectancyMale, d.InfantMortality * femaleShare * b.InfantMaleExcess, b);
    }

    /// <summary>Доля людей 65+ в рабочей силе с поправкой законов о пенсионном возрасте.</summary>
    public static double OlderParticipation(World world, string country) =>
        Math.Clamp(world.Data.Demography.OlderParticipation + Laws.LawModel.Effect(world, country, "older_participation"), 0, 0.6);

    /// <summary>Значение прогноза на год: линейно между точками, за краями — крайнее значение.</summary>
    public static double PathValue(IReadOnlyList<int> years, IReadOnlyList<double> values, double year)
    {
        if (year <= years[0])
            return values[0];
        for (var i = 1; i < years.Count; i++)
            if (year <= years[i])
                return values[i - 1] + (values[i] - values[i - 1]) * (year - years[i - 1]) / (years[i] - years[i - 1]);
        return values[^1];
    }
}
