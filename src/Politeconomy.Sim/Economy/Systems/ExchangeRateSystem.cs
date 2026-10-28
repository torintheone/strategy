using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy.Systems;

/// <summary>
/// Курсы валют к доллару. Номинальный курс следует за разницей цен (паритет покупательной способности),
/// реальный — возвращается к норме, укрепляется при росте реальной ставки и у сырьевых стран при дорогом сырье,
/// плюс случайные колебания. Фиксированный курс повторяет якорную валюту. MODEL.md, «Курсы валют».
/// </summary>
public sealed class ExchangeRateSystem : EconomySystem
{
    public override string Name => "Курсы валют";
    public override SimPhase Phase => SimPhase.PricesAndInflation;

    protected override void Run(World world, WorldEconomy economy, EconomyBalance balance)
    {
        var b = balance.Exchange;
        var referenceId = ReferenceId(economy);
        var reference = economy[referenceId];
        var referenceCurrency = economy.Currencies[reference.Currency];
        var referenceReal = referenceCurrency.PolicyRate - reference.Expectations;
        var logOil = Math.Log(economy.OilPrice);
        var logOres = Math.Log(economy.OresPrice);

        foreach (var cur in economy.Currencies.Values)
        {
            var weights = EconomyCalibration.Weights(economy, cur.Members);
            cur.LogPrice = cur.Members.Sum(id => weights[id] * Math.Log(economy[id].Price));
        }
        foreach (var cur in economy.Currencies.Values.Where(c => c.IsFloating))
        {
            if (cur.Id == reference.Currency)
            {
                cur.ExchangeRate = 1;
                continue;
            }
            var weights = EconomyCalibration.Weights(economy, cur.Members);
            var realRate = cur.PolicyRate - cur.Expectations - referenceReal;
            var change = Math.Clamp(realRate - cur.RealRateDifference, -0.01, 0.01);
            cur.RealRateDifference = realRate;
            var commodity = cur.Members.Sum(id => weights[id] * (economy[id].OilNet * logOil + economy[id].OresNet * logOres));
            var norm = -b.CommodityCurrency * commodity;
            var sigma = EconomyBalance.ByIncome(b.Shock, economy[cur.Home].IncomeIndex);
            cur.LogReal += b.Reversion * (norm - cur.LogReal) - b.RateDifferential * change + Shock(world, economy, sigma);
            cur.LogReal = Math.Clamp(cur.LogReal, -1.5, 1.5);
            cur.ExchangeRate = Math.Exp(cur.LogReal + cur.LogPrice - Math.Log(reference.Price));
        }
        foreach (var cur in economy.Currencies.Values.Where(c => !c.IsFloating))
            cur.ExchangeRate = economy.Currencies[cur.Anchor!].ExchangeRate;

        foreach (var country in world.Countries)
        {
            var e = economy[country.Id];
            e.ExchangeRate = economy.Currencies[e.Currency].ExchangeRate;
            e.RealExchangeRate = e.ExchangeRate * reference.Price / e.Price;
        }
    }
}
