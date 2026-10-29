using Politeconomy.Sim.Data;
using Politeconomy.Sim.Demography;
using Politeconomy.Sim.Economy;

namespace Politeconomy.Sim.Society;

/// <summary>
/// Из кого состоят классы. Занятые делятся по отраслям и квалификации: без образования — неквалифицированные
/// рабочие; с профессиональным — квалифицированные рабочие в промышленности и служащие в услугах; с высшим —
/// специалисты. Чем выше охват высшим образованием, тем больше в отраслях специалистов. Крестьяне — большая часть
/// занятых в сельском хозяйстве, госаппарат — госуправление и силовики, мелкий бизнес — часть самозанятых вне
/// сельского хозяйства, крупный капитал — доля занятых; силовики, мелкий бизнес и капитал набираются из остальных
/// занятых. Безработные — из рынка труда, пенсионеры — люди 65+ вне рабочей силы. Дети и неработающие взрослые
/// распределяются по семьям работающих и безработных. MODEL.md, «Классы».
/// </summary>
public static class ClassComposition
{
    /// <summary>Классы, из которых набираются силовики, мелкий бизнес и крупный капитал.</summary>
    public static readonly int[] Donors = [ClassIds.Unskilled, ClassIds.Skilled, ClassIds.Clerks, ClassIds.Professionals];

    /// <summary>
    /// Heads — работающие, безработные и пенсионеры; Sizes — вместе с семьями. WagePeople[i, k] — работники
    /// отрасли i из класса k до набора силовиков и предпринимателей: по этой таблице делится фонд оплаты отрасли.
    /// DonorScale — какая доля работников остаётся в классах-донорах; Security, SmallBusiness, Capitalists — набранные.
    /// </summary>
    public sealed record Result(double[] Heads, double[] Sizes, double[,] WagePeople, double DonorScale,
        double Security, double SmallBusiness, double Capitalists);

    /// <summary>SmallBusiness и Vocational — поправки законов (доли): к доле мелкого бизнеса и к работникам с профессиональным образованием.</summary>
    public static Result Compute(IReadOnlyList<IndustryRecord> industries, CountryEconomy e, CountryDemography d,
        double selfEmployed, double tertiary, SocietyBalance b, double olderParticipation, double smallBusiness = 0, double vocational = 0)
    {
        var heads = new double[ClassIds.Count];
        var labor = e.LaborForce;
        var employed = labor * (1 - e.Unemployment);
        var tilt = Math.Clamp(tertiary / b.TertiaryReference, b.SkillTiltMin, b.SkillTiltMax);
        var wagePeople = new double[industries.Count, ClassIds.Count];
        for (var i = 0; i < industries.Count; i++)
        {
            var ind = industries[i];
            var people = employed * e.EmploymentShare[i];
            if (people <= 0)
                continue;
            void Put(int k, double n)
            {
                wagePeople[i, k] += n;
                heads[k] += n;
            }
            switch (ind.Id)
            {
                case "agriculture":
                    Put(ClassIds.Farmers, people * b.FarmerShare);
                    Put(ClassIds.Unskilled, people * (1 - b.FarmerShare));
                    break;
                case "public_admin":
                    Put(ClassIds.Officials, people);
                    break;
                default:
                    var (low, mid, high) = Skills(ind.Skills, tilt, vocational);
                    Put(ClassIds.Unskilled, people * low);
                    Put(ind.Sector == "industry" ? ClassIds.Skilled : ClassIds.Clerks, people * mid);
                    Put(ClassIds.Professionals, people * high);
                    break;
            }
        }

        var security = b.SecurityShare * labor;
        var owners = b.SmallBusinessShare * Math.Max(0, 1 + smallBusiness) * Math.Max(0, selfEmployed * employed - heads[ClassIds.Farmers]);
        var capitalists = b.CapitalistShare * employed;
        var donorTotal = Donors.Sum(k => heads[k]);
        var wanted = security + owners + capitalists;
        var taken = Math.Min(wanted, 0.6 * donorTotal);
        var share = wanted > 0 ? taken / wanted : 0;
        var scale = donorTotal > 0 ? 1 - taken / donorTotal : 1;
        foreach (var k in Donors)
            heads[k] *= scale;
        security *= share;
        owners *= share;
        capitalists *= share;
        heads[ClassIds.Officials] += security;
        heads[ClassIds.SmallBusiness] += owners;
        heads[ClassIds.Capitalists] += capitalists;

        heads[ClassIds.Unemployed] = labor * e.Unemployment;
        heads[ClassIds.Pensioners] = Math.Max(0, d.Elderly * (1 - d.Participation * olderParticipation));

        var sizes = (double[])heads.Clone();
        var households = heads.Sum() - heads[ClassIds.Pensioners];
        var dependents = Math.Max(0, d.Total - heads.Sum());
        if (households > 0)
            for (var k = 0; k < ClassIds.Count; k++)
                if (k != ClassIds.Pensioners)
                    sizes[k] += dependents * heads[k] / households;
        return new Result(heads, sizes, wagePeople, scale, security, owners, capitalists);
    }

    /// <summary>
    /// Доли работников отрасли без образования, с профессиональным и с высшим: высшее — умножается на «наклон»
    /// по охвату высшим образованием, профессиональное — на корень из него, остальное — без образования.
    /// </summary>
    public static (double Low, double Mid, double High) Skills(IReadOnlyList<double> typical, double tilt, double vocational = 0)
    {
        var high = typical[2] * tilt;
        var mid = typical[1] * Math.Sqrt(tilt) * Math.Max(0, 1 + vocational);
        var low = Math.Max(0.02, 1 - high - mid);
        var sum = low + mid + high;
        return (low / sum, mid / sum, high / sum);
    }
}
