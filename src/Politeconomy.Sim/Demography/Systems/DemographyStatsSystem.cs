using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Demography.Systems;

/// <summary>Переносит демографию в показатели стран: рождаемость, продолжительность жизни, возраст, города, миграция.</summary>
public sealed class DemographyStatsSystem : ISimSystem
{
    public string Name => "Показатели населения";
    public SimPhase Phase => SimPhase.Demography;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world) => Publish(world);

    public static void Publish(World world)
    {
        var demography = world.Demography ?? throw new InvalidOperationException("Демография не создана");
        var year = world.Date.Year;
        foreach (var country in world.Countries)
        {
            var d = demography[country.Id];
            var stats = country.Stats;
            void Set(string id, double value, params StatFactor[] factors) =>
                stats[id] = new StatValue(value, year, StatSources.Model, null, null, factors.Length > 0 ? factors : null);
            var total = d.Total;
            var yearAgo = d.TotalHistory[d.HistoryIndex];

            Set("population", total);
            Set("fertility_rate", d.Fertility,
                new StatFactor("Обычный уровень для страны", d.BaseFertility, "number2"),
                new StatFactor("Поправка на безработицу", d.Fertility - d.BaseFertility, "number2"));
            Set("life_expectancy", d.LifeExpectancy,
                new StatFactor("Женщины", d.LifeExpectancyFemale, "years"),
                new StatFactor("Мужчины", d.LifeExpectancyMale, "years"));
            Set("age_0_14_pct", d.Children / total * 100);
            Set("age_65_plus_pct", d.Elderly / total * 100);
            Set("median_age", d.MedianAge());
            Set("urban_pct", d.UrbanPct);
            Set("population_growth_pct", yearAgo > 0 ? (total / yearAgo - 1) * 100 : 0,
                new StatFactor("Рождаемость, на 1000 жителей", d.YearSum(d.BirthsHistory) / total * 1000, "number1"),
                new StatFactor("Смертность, на 1000 жителей", d.YearSum(d.DeathsHistory) / total * 1000, "number1"),
                new StatFactor("Миграция, на 1000 жителей", d.YearSum(d.MigrationHistory) / total * 1000, "number1"));
            Set("birth_rate", d.YearSum(d.BirthsHistory) / total * 1000);
            Set("death_rate", d.YearSum(d.DeathsHistory) / total * 1000);
            Set("net_migration_rate", d.YearSum(d.MigrationHistory) / total * 1000);
        }
    }
}
