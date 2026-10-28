using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy.Systems;

/// <summary>
/// Потенциальный выпуск отраслей: Y = A · K^α · L^(1−α). Капитал растёт на инвестиции и выбывает,
/// производительность растёт так, чтобы потенциал шёл по прогнозу МВФ, а дальше — по догоняющему росту.
/// MODEL.md, «Производство».
/// </summary>
public sealed class ProductionSystem : EconomySystem
{
    public override string Name => "Производство";
    public override SimPhase Phase => SimPhase.Economy;

    protected override void Run(World world, WorldEconomy economy, EconomyBalance balance)
    {
        var industries = world.Data.Industries.Industries;
        var b = balance.Supply;
        var years = economy.Months / 12.0;
        foreach (var country in world.Countries)
        {
            var e = economy[country.Id];

            // Население и рабочая сила (до этапа 2 — затухающий рост из данных).
            var populationGrowth = e.PopulationGrowth0 * Math.Exp(-years * Math.Log(2) / b.PopulationHalfLifeYears);
            var monthly = Math.Pow(1 + populationGrowth, 1.0 / 12);
            e.Population *= monthly;
            e.LaborForce *= monthly;

            e.PotentialGrowth = TargetGrowth(e, economy, b, years, populationGrowth);
            var path = Math.Min(1, years / b.ForecastYears);
            e.NaturalUnemployment = e.NaturalUnemploymentStart + (e.NaturalUnemploymentLong - e.NaturalUnemploymentStart) * path;
            e.InflationTarget = e.InflationStart + (e.InflationLong - e.InflationStart) * path;

            var investment = e.InvestmentRate * e.RealGdp;
            var potentialLabor = e.LaborForce * (1 - e.NaturalUnemployment);
            for (var i = 0; i < industries.Count; i++)
            {
                var ind = industries[i];
                if (e.Tfp[i] <= 0)
                    continue;
                e.Capital[i] = Math.Max(0, e.Capital[i] + (e.InvestmentShare[i] * investment - ind.Depreciation * e.Capital[i]) / 12);
                e.Tfp[i] *= Math.Pow(1 + (1 - ind.Alpha) * (e.PotentialGrowth - populationGrowth), 1.0 / 12);
                e.Potential[i] = e.Tfp[i] * Math.Pow(e.Capital[i], ind.Alpha) * Math.Pow(potentialLabor * e.EmploymentShare[i], 1 - ind.Alpha);
            }
        }
    }

    /// <summary>
    /// Целевой рост потенциала: первые годы — прогноз МВФ (первый год — прогноз на 2026-й, затем тренд 2027–2030),
    /// дальше вес переходит к догоняющему росту: лидер растёт на FrontierGrowth, отстающие — быстрее.
    /// </summary>
    public static double TargetGrowth(CountryEconomy e, WorldEconomy economy, SupplyBalance b, double years, double populationGrowth)
    {
        var forecast = e.TrendGrowth + (e.FirstYearGrowth - e.TrendGrowth) * Math.Exp(-years / b.FirstYearFadeYears);
        var perCapita = e.Ppp0 * (e.RealGdp / e.RealGdp0) / (e.Population / e.Population0);
        var frontier = economy.FrontierPpp * Math.Pow(1 + b.FrontierGrowth, years);
        var catchUp = Math.Clamp(b.ConvergenceSpeed * b.Institutions * Math.Log(frontier / Math.Max(perCapita, 100)), b.CatchUpMin, b.CatchUpMax);
        var convergence = b.FrontierGrowth + catchUp + populationGrowth;
        var weight = years <= b.ForecastYears ? 1 : Math.Exp(-(years - b.ForecastYears) / b.ForecastFadeYears);
        return Math.Clamp(weight * forecast + (1 - weight) * convergence, b.TrendMin, b.TrendMax);
    }
}
