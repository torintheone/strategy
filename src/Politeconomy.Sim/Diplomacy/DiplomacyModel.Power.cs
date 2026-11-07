using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Diplomacy;

// Индекс мощи: экономика, армия, технологии, влияние. MODEL.md, «Индекс мощи».
public static partial class DiplomacyModel
{
    /// <summary>
    /// Каждая составляющая — доля страны в мире: экономика — ВВП; армия — военные расходы × (1 + военные технологии)
    /// × Nuclear у ядерных держав; технологии — деньги на науку с учётом эффективности; влияние — хорошие отношения
    /// (× отношения / 100), взвешенные по √(наш ВВП × их ВВП), постоянное место в Совете безопасности + PermanentSeat. Индекс =
    /// 100 × √(взвешенная сумма долей / та же сумма у сильнейшей страны); составляющие делят индекс в пропорции своих вкладов.
    /// </summary>
    static void UpdatePower(World world)
    {
        var d = world.Diplomacy!;
        var data = world.Data;
        var b = data.DiplomacyBalance;
        var n = d.Ids.Length;
        var nuclear = data.Diplomacy.Nuclear.ToHashSet(StringComparer.Ordinal);
        var permanent = Permanent(world).ToHashSet(StringComparer.Ordinal);
        var economy = new double[n];
        var military = new double[n];
        var technology = new double[n];
        var influence = new double[n];
        for (var i = 0; i < n; i++)
        {
            var id = d.Ids[i];
            var e = world.Economy![id];
            economy[i] = Math.Max(0, e.GdpUsd);
            military[i] = Military.WarModel.Strength(world, id) * (nuclear.Contains(id) ? b.Nuclear : 1);
            technology[i] = (world.Science?.Countries.GetValueOrDefault(id)?.Points ?? 0) / 100 * economy[i];
        }
        // Влияние: хорошие отношения, взвешенные по √(наш ВВП × их ВВП) — дружба крупной страны весит больше.
        for (var i = 0; i < n; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < n; j++)
                if (i != j && d.Relations[i][j] > 0)
                    sum += d.Relations[i][j] / 100 * Math.Sqrt(economy[i] * economy[j]);
            influence[i] = sum;
        }
        double[] Share(double[] x)
        {
            var total = x.Sum();
            return Array.ConvertAll(x, v => total > 0 ? v / total : 0);
        }
        var (se, sm, st, si) = (Share(economy), Share(military), Share(technology), Share(influence));
        for (var i = 0; i < n; i++)
            if (permanent.Contains(d.Ids[i]))
                si[i] += b.PermanentSeat;
        var composite = new double[n];
        for (var i = 0; i < n; i++)
            composite[i] = b.PowerEconomy * se[i] + b.PowerMilitary * sm[i] + b.PowerTechnology * st[i] + b.PowerInfluence * si[i];
        var top = composite.Max();
        var order = Enumerable.Range(0, n).OrderByDescending(i => composite[i]).ThenBy(i => d.Ids[i], StringComparer.Ordinal).ToArray();
        d.Power.Clear();
        var year = world.Date.Year;
        for (var r = 0; r < n; r++)
        {
            var i = order[r];
            // Корень: доли мира растягивают разрыв (у второй державы вдвое меньше — это не «вдвое слабее во всём»).
            var index = top > 0 ? 100 * Math.Sqrt(composite[i] / top) : 0;
            var k = composite[i] > 0 ? index / composite[i] : 0;
            var score = new PowerScore(index, r + 1, b.PowerEconomy * se[i] * k, b.PowerMilitary * sm[i] * k,
                b.PowerTechnology * st[i] * k, b.PowerInfluence * si[i] * k);
            d.Power[d.Ids[i]] = score;
            var country = world[d.Ids[i]];
            country.Stats["power_index"] = new StatValue(score.Index, year, StatSources.Model, null, null,
            [
                new StatFactor("Экономика", score.Economy, "number1"),
                new StatFactor("Армия", score.Military, "number1"),
                new StatFactor("Технологии", score.Technology, "number1"),
                new StatFactor("Влияние", score.Influence, "number1"),
            ]);
            country.Stats["power_rank"] = new StatValue(r + 1, year, StatSources.Model, null, null, null);
        }
    }
}
