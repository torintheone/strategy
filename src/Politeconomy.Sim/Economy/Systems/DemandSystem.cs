using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy.Systems;

/// <summary>
/// Спрос и цикл: разрыв выпуска (насколько экономика выше или ниже потенциала) от реальной ставки,
/// бюджетного импульса, мирового цикла, цен сырья и курса. Отсюда — фактический выпуск отраслей,
/// инвестиции и внешняя торговля. MODEL.md, «Спрос и цикл».
/// </summary>
public sealed class DemandSystem : EconomySystem
{
    public override string Name => "Спрос";
    public override SimPhase Phase => SimPhase.Economy;

    protected override void Run(World world, WorldEconomy economy, EconomyBalance balance)
    {
        var industries = world.Data.Industries.Industries;
        var b = balance.Demand;
        var logOil = Math.Log(economy.OilPrice);
        var logOres = Math.Log(economy.OresPrice);
        foreach (var country in world.Countries)
        {
            var e = economy[country.Id];

            // Реальная ставка для бизнеса: ключевая минус ожидания плюс половина роста премии за риск страны с начала партии.
            var realRate = e.PolicyRate + 0.5 * (e.Spread - e.Spread0) - e.Expectations;
            var rateGap = Math.Clamp(realRate - e.Neutral, -b.RealRateGapLimit, b.RealRateGapLimit);
            var shock = Shock(world, economy, EconomyBalance.ByIncome(b.Shock, e.IncomeIndex));
            e.GapCore = Math.Clamp(b.GapPersistence * e.GapCore - b.RateSensitivity * rateGap + shock, b.GapMin, b.GapMax);

            var primaryDeficit = (e.NominalGdp > 0 ? (e.TotalSpending - e.TotalRevenue) / e.NominalGdp : 0) + e.ProjectFlow;
            e.PrimaryDeficitSmooth += (primaryDeficit - e.PrimaryDeficitSmooth) / b.FiscalImpulseMonths;
            e.FiscalImpulse = b.FiscalMultiplier * (primaryDeficit - e.PrimaryDeficitSmooth);

            var otherWorld = e.WorldShare < 0.99 ? (economy.WorldGap - e.WorldShare * e.Gap) / (1 - e.WorldShare) : 0;
            var spillover = b.WorldSpillover * e.Openness * otherWorld;
            var commodities = b.CommodityIncome * (e.OilNet * logOil + e.OresNet * logOres);
            var exchange = b.RealExchangeEffect * e.Openness * Math.Log(e.RealExchangeRate);
            e.Gap = Math.Clamp(e.GapCore + e.FiscalImpulse + spillover + commodities + exchange, b.GapMin, b.GapMax);

            // Фактический выпуск: цикличные отрасли (стройка, машиностроение) качаются сильнее.
            var potential = e.Potential.Sum();
            var raw = 0.0;
            for (var i = 0; i < industries.Count; i++)
            {
                e.Output[i] = e.Potential[i] * Math.Max(0.05, 1 + industries[i].Cycle * e.Gap);
                raw += e.Output[i];
            }
            var scale = raw > 0 ? potential * (1 + e.Gap) / raw : 0;
            for (var i = 0; i < industries.Count; i++)
                e.Output[i] *= scale;
            e.RealGdp = potential * (1 + e.Gap);

            var investment = 1 + b.InvestmentCycle * e.Gap - b.InvestmentRate * rateGap;
            e.InvestmentRate = e.InvestmentRate0 * Math.Clamp(investment, b.InvestmentMin, b.InvestmentMax);

            // Торговля: дешёвая валюта помогает экспорту и сдерживает импорт; сырьевой экспорт идёт по мировой цене.
            var commodityPrice = CommodityExportPrice(e, economy);
            var q = e.RealExchangeRate;
            e.Exports = e.Exports0 * ((1 - e.CommodityExports) * Math.Pow(q, b.ExportElasticity) * (1 + 0.5 * otherWorld)
                                      + e.CommodityExports * commodityPrice);
            e.Imports = e.Imports0 * Math.Pow(q, -b.ImportElasticity) * Math.Max(0.2, 1 + b.ImportCycle * e.Gap)
                        * (1 + e.OilImports * (economy.OilPrice - 1));
            e.CurrentAccount = Math.Clamp(e.CurrentAccount0 + (e.Exports - e.Imports) - (e.Exports0 - e.Imports0),
                -b.CurrentAccountLimit, b.CurrentAccountLimit);
        }
    }

    /// <summary>Средняя цена сырьевого экспорта страны: нефть и газ против руд и угля по их ренте.</summary>
    static double CommodityExportPrice(CountryEconomy e, WorldEconomy economy)
    {
        var oil = Math.Max(0, e.OilNet);
        var ores = Math.Max(0, e.OresNet);
        return oil + ores > 0 ? (oil * economy.OilPrice + ores * economy.OresPrice) / (oil + ores) : 1;
    }
}
