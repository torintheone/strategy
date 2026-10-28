using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Demography.Systems;

/// <summary>
/// Раз в год (1 января) пересчитывает рождаемость и смертность. Обычный уровень рождаемости и продолжительность
/// жизни идут по прогнозу (стартовое отличие от прогноза затухает за 10 лет); без прогноза рождаемость тянется
/// к FertilityFloor, а жизнь удлиняется тем быстрее, чем она короче. Высокая безработица снижает рождаемость.
/// MODEL.md, «Демография».
/// </summary>
public sealed class VitalRatesSystem : ISimSystem
{
    const double FadeYears = 10;

    public string Name => "Рождаемость и смертность";
    public SimPhase Phase => SimPhase.Demography;
    public SimFrequency Frequency => SimFrequency.Yearly;

    public void Run(World world)
    {
        var demography = world.Demography ?? throw new InvalidOperationException("Демография не создана");
        var b = world.Data.Demography;
        var records = world.Data.Population.Countries.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var year = world.Date.Year;
        var startYear = world.Data.Scenario.StartDate.Year;
        var fade = Math.Exp(-(year - startYear) / FadeYears);
        foreach (var country in world.Countries)
        {
            var d = demography[country.Id];
            var path = records[country.Id].Path;
            var lastPathYear = path?.Years[^1] ?? int.MinValue;

            if (path != null && year <= lastPathYear)
                d.BaseFertility = DemographyModel.PathValue(path.Years, path.Fertility, year) + d.FertilityGap0 * fade;
            else
            {
                var speed = d.BaseFertility > b.FertilityFloor ? b.FertilitySpeed : b.FertilitySpeed * 0.3;
                d.BaseFertility += speed * (b.FertilityFloor - d.BaseFertility);
            }
            d.BaseFertility = Math.Clamp(d.BaseFertility, 0.5, 8);
            d.Fertility = d.BaseFertility * (1 - b.FertilityUnemployment * UnemploymentGap(world, country.Id));

            if (path != null && year <= lastPathYear)
            {
                var start = DemographyModel.PathValue(path.Years, path.LifeExpectancy, startYear);
                d.LifeExpectancy = DemographyModel.PathValue(path.Years, path.LifeExpectancy, year) + (d.LifeExpectancy0 - start) * fade;
            }
            else
            {
                var room = Math.Clamp((85 - d.LifeExpectancy) / 25, 0, 1);
                d.LifeExpectancy += b.LifeGainMin + b.LifeGainExtra * room;
            }
            d.LifeExpectancy = Math.Clamp(d.LifeExpectancy, 30, 95);
            d.InfantMortality = Math.Max(1.5, d.InfantMortality0 * Math.Exp(-0.05 * (d.LifeExpectancy - d.LifeExpectancy0)));
            DemographyModel.UpdateMortality(d, b);
        }
    }

    /// <summary>Безработица выше естественной, в процентных пунктах.</summary>
    static double UnemploymentGap(World world, string id)
    {
        if (world.Economy?.Countries.GetValueOrDefault(id) is not { } e)
            return 0;
        return Math.Max(0, (e.Unemployment - e.NaturalUnemployment) * 100);
    }
}
