using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy.Systems;

/// <summary>
/// Инфляция: тянется к ожиданиям плюс давление спроса, удорожание импорта из-за курса и цены
/// продовольствия и энергии. Ожидания медленно идут к цели центробанка — тем быстрее, чем больше ему верят.
/// MODEL.md, «Цены и инфляция».
/// </summary>
public sealed class PriceSystem : EconomySystem
{
    public override string Name => "Цены";
    public override SimPhase Phase => SimPhase.PricesAndInflation;

    protected override void Run(World world, WorldEconomy economy, EconomyBalance balance)
    {
        var b = balance.Prices;
        var reference = Reference(economy);
        var worldInflation = reference.Price / reference.YearAgo(reference.PriceHistory) - 1;
        var food = Math.Log(economy.FoodPrice / economy.FoodHistory[economy.HistoryIndex]);
        var energy = Math.Log(economy.OilPrice / economy.OilHistory[economy.HistoryIndex]);
        foreach (var country in world.Countries)
        {
            var e = economy[country.Id];
            var depreciation = Math.Log(e.ExchangeRate / e.YearAgo(e.ExchangeHistory));
            e.DemandPart = b.DemandPressure * e.Gap;
            e.ImportPart = b.ImportPassThrough * Math.Min(e.Imports, 1) * (depreciation + worldInflation - e.Expectations);
            // Контроль цен и льготный НДС ослабляют передачу мировых цен на еду (поправка законов — доля).
            var foodPass = b.FoodPassThrough * Math.Max(0, 1 + LawModel.Effect(world, country.Id, "food_inflation"));
            e.CommodityPart = e.FoodWeight * foodPass * food + b.EnergyWeight * b.EnergyPassThrough * energy;
            e.InflationPull = e.Expectations + e.DemandPart + e.ImportPart + e.CommodityPart;

            e.Inflation += b.Adjustment * (e.InflationPull - e.Inflation) + Shock(world, economy, b.Shock);
            e.Inflation = Math.Clamp(e.Inflation, b.InflationMin, b.InflationMax);
            var credibility = Math.Clamp(e.Credibility + LawModel.Effect(world, country.Id, "credibility"), 0.05, 0.95);
            var anchor = credibility * e.InflationTarget + (1 - credibility) * e.Inflation;
            e.Expectations += b.Expectations * (anchor - e.Expectations);
            e.Price *= Math.Pow(1 + e.Inflation, 1.0 / 12);
        }
    }
}
