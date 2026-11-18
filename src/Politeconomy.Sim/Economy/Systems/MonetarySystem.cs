using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy.Systems;

/// <summary>
/// Центробанки. Плавающая валюта: ставка по правилу Тейлора — нейтральная ставка плюс инфляция,
/// плюс реакция на отклонение инфляции от цели и на разрыв выпуска, со сглаживанием.
/// Фиксированный курс: ставка якорной валюты плюс надбавка. Общая валюта — правило по средним союза.
/// MODEL.md, «Центробанк».
/// </summary>
public sealed class MonetarySystem : EconomySystem
{
    public override string Name => "Центробанки";
    public override SimPhase Phase => SimPhase.PricesAndInflation;

    protected override void Run(World world, WorldEconomy economy, EconomyBalance balance)
    {
        var b = balance.Monetary;
        foreach (var cur in economy.Currencies.Values)
        {
            var weights = EconomyCalibration.Weights(economy, cur.Members);
            cur.Inflation = cur.Members.Sum(id => weights[id] * economy[id].Inflation);
            cur.Expectations = cur.Members.Sum(id => weights[id] * economy[id].Expectations);
            cur.Gap = cur.Members.Sum(id => weights[id] * economy[id].Gap);
            cur.Target = cur.Members.Sum(id => weights[id] * economy[id].InflationTarget);
            if (!cur.IsFloating)
                continue;
            // Если центробанк подчинён правительству и оно задало ставку — ставка идёт к ней, иначе по правилу Тейлора.
            if (cur.ManualRate != null && !LawModel.CentralBankObeys(world, cur.Home))
                cur.ManualRate = null;
            var target = cur.ManualRate ?? TaylorRate(cur, b);
            cur.PolicyRate = Math.Clamp(cur.PolicyRate + b.Smoothing * (target - cur.PolicyRate), b.Floor, b.Ceiling);
        }
        foreach (var cur in economy.Currencies.Values.Where(c => !c.IsFloating))
            cur.PolicyRate = Math.Clamp(economy.Currencies[cur.Anchor!].PolicyRate + cur.PegSpread, b.Floor, b.Ceiling);
        foreach (var cur in economy.Currencies.Values)
            foreach (var id in cur.Members)
                economy[id].PolicyRate = cur.PolicyRate;
    }

    public static double TaylorRate(CurrencyState cur, MonetaryBalance b) =>
        cur.Neutral + cur.Inflation + b.TaylorInflation * (cur.Inflation - cur.Target) + b.TaylorGap * cur.Gap;
}
