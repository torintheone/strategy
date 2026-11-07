using Politeconomy.Sim.Economy.Systems;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Diplomacy;

// «Международный валютный совет»: дешёвый кредит с условиями. MODEL.md, «Валютный совет».
public static partial class DiplomacyModel
{
    /// <summary>Предложение Валютного совета: даст ли кредит и почему, сколько (% ВВП), под какую ставку (%), доля голосов «за».</summary>
    public sealed record LoanOffer(bool Accepts, string Reason, double Amount, double Rate, double Support);

    public static LoanOffer FundOffer(World world, string country)
    {
        var b = world.Data.DiplomacyBalance;
        var d = world.Diplomacy!;
        var e = world.Economy![country];
        var economy = world.Economy;
        var rate = economy.Currencies.TryGetValue("USD", out var usd) ? usd.PolicyRate * 100 + b.FundMargin : 4 + b.FundMargin;
        var amount = e.NominalGdp > 0 ? Math.Min(b.FundShare, e.Debt / e.NominalGdp * 100) : 0;
        LoanOffer No(string reason) => new(false, reason, amount, rate, 0);
        if (d.Programs.TryGetValue(country, out var program))
        {
            if (program.Status == "active")
                return No($"Программа уже идёт — до {Fmt.Date(program.Until)}.");
            if (program.PenaltyUntil is { } until && world.Date < until)
                return No($"Программу прекратили за нарушение условий: новых денег не будет до {Fmt.Date(until)}.");
        }
        if (!world[country].Info.UnMember)
            return No("Совет даёт деньги только членам Совета Наций.");
        if (UnderCouncilSanctions(world, country))
            return No("Страна под санкциями Совета Наций.");
        if (e.Rating > b.FundRating)
            return No($"Рейтинг {RatingSystem.Letter(e.Rating)} — страна может занимать на рынке; Совет даёт деньги странам с рейтингом BB+ и ниже.");
        if (amount <= 0)
            return No("У страны нет долга — нечего перекредитовать.");
        // Члены голосуют весом экономики: против, если отношения с заёмщиком хуже FundRelations.
        var gdp = StartGdp(world, d);
        double yes = 0, total = 0;
        var against = new List<(string Id, double Weight)>();
        foreach (var m in Assembly(world).Where(m => m != country))
        {
            var w = gdp[d.Index[m]];
            total += w;
            if (Relations(world, m, country) >= b.FundRelations)
                yes += w;
            else
                against.Add((m, w));
        }
        var support = total > 0 ? yes / total : 0;
        if (support <= 0.5)
            return new LoanOffer(false,
                $"«За» только {Fmt.Value("percent", support * 100)} голосов (по весу экономик). Против: " +
                string.Join(", ", against.OrderByDescending(x => x.Weight).Take(3).Select(x => world[x.Id].Name)) + ".", amount, rate, support);
        return new LoanOffer(true,
            $"Кредит {Fmt.Number(amount, 1)}% ВВП под {Fmt.Number(rate, 1)}% годовых. Условия: через {b.FundTargetYears} года и до конца программы " +
            $"дефицит не больше {Fmt.Number(b.FundDeficit, 0)}% ВВП, без национализации. Нарушите — программу прекратят, занимать станет дороже.",
            amount, rate, support);
    }

    /// <summary>
    /// Взять кредит (игрок): часть долга (Amount % ВВП) перекредитована по ставке Совета, пока программа идёт — премия
    /// за риск ниже (рынок доверяет стране с программой).
    /// </summary>
    public static bool RequestLoan(World world, string country)
    {
        if (!Acts(world, country))
            return false;
        var offer = FundOffer(world, country);
        var name = world.Data.Diplomacy.Fund.Name;
        if (!offer.Accepts)
        {
            Note(world, country, "fund", $"{name} отказал в кредите. {offer.Reason}");
            return false;
        }
        var b = world.Data.DiplomacyBalance;
        var e = world.Economy![country];
        var program = new FundProgram
        {
            Since = world.Date,
            Amount = offer.Amount / 100,
            Rate = offer.Rate / 100,
            Deadline = world.Date.AddYears(b.FundTargetYears),
            Until = world.Date.AddYears(b.FundYears),
        };
        world.Diplomacy!.Programs[country] = program;
        var loan = Math.Min(program.Amount * e.NominalGdp, e.Debt);
        if (e.Debt > 0 && e.EffectiveRate > program.Rate)
            e.EffectiveRate = (e.EffectiveRate * e.Debt - (e.EffectiveRate - program.Rate) * loan) / e.Debt;
        Note(world, country, "fund", $"{name} дал кредит: {offer.Reason}");
        Refresh(world);
        return true;
    }

    /// <summary>Каждый месяц: программа дошла до конца — выполнена; в январе после срока дефицит выше цели — прекращена.</summary>
    static void ReviewPrograms(World world)
    {
        var b = world.Data.DiplomacyBalance;
        foreach (var (country, program) in world.Diplomacy!.Programs)
        {
            if (program.Status != "active" || world.ById(country) is null)
                continue;
            var deficit = -world.Economy![country].BalanceRatio * 100;
            // Условие проверяется по итогам года — в январе, после срока.
            if (world.Date >= program.Deadline && world.Date.Month == 1 && deficit > b.FundDeficit + 1e-9)
                Fail(world, country, program, $"дефицит {Fmt.Number(deficit, 1)}% ВВП вместо {Fmt.Number(b.FundDeficit, 0)}%");
            else if (world.Date >= program.Until)
            {
                program.Status = "done";
                world.Log(country, "fund", $"Программа «{world.Data.Diplomacy.Fund.Name}» выполнена: условия соблюдены.");
            }
        }
    }

    static void Fail(World world, string country, FundProgram program, string why)
    {
        program.Status = "failed";
        program.PenaltyUntil = world.Date.AddYears(world.Data.DiplomacyBalance.FundPenaltyYears);
        world.Log(country, "fund", $"{world.Data.Diplomacy.Fund.Name} прекратил программу: {why}. Рынок не доверяет — занимать дороже до {Fmt.Date(program.PenaltyUntil.Value)}.");
    }
}
