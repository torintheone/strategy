using Politeconomy.Sim.Data;
using Politeconomy.Sim.Economy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Society.Systems;

/// <summary>
/// Месяц жизни классов: состав по экономике и населению (люди переходят между классами вслед за занятостью
/// и возрастом и приносят с собой ожидания и настроение), доходы, потребление, сбережения и уровень жизни
/// по трём корзинам потребностей. MODEL.md, «Классы».
/// </summary>
public sealed class ClassesSystem : ISimSystem
{
    public string Name => "Классы: состав и доходы";
    public SimPhase Phase => SimPhase.SocialSphere;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var society = world.Society ?? throw new InvalidOperationException("Классы не созданы: вызовите SocietyModel.Setup");
        var economy = world.Economy!;
        var data = world.Data;
        var classes = data.Classes.Classes;
        var b = data.Society;
        foreach (var country in world.Countries)
        {
            var s = society[country.Id];
            var e = economy[country.Id];
            var composition = SocietyModel.Compose(world, country);
            Move(s, composition.Sizes);
            s.Heads = composition.Heads;
            var flows = ClassIncome.Compute(data, classes, e, economy, composition);
            UpdateLiving(world, e, s, flows.Disposable, classes, b);
            for (var k = 0; k < ClassIds.Count; k++)
                s.Savings[k] = Math.Max(0, s.Savings[k] + (s.Income[k] * classes[k].Saving - s.Savings[k] * b.SavingsDecay) / 12);
        }
    }

    /// <summary>
    /// Новая численность классов. Кто перешёл из класса в класс, приносит ожидания, удовлетворённость и радикализм
    /// своего прежнего класса: уволенные рабочие ждут прежней жизни и недовольны сильнее давних безработных.
    /// </summary>
    static void Move(CountrySociety s, double[] sizes)
    {
        const int n = ClassIds.Count;
        double inflow = 0, outflow = 0, outE = 0, outS = 0, outR = 0;
        for (var k = 0; k < n; k++)
        {
            var delta = sizes[k] - s.Size[k];
            if (delta > 0)
                inflow += delta;
            else
            {
                outflow -= delta;
                outE -= delta * s.Expectations[k];
                outS -= delta * s.Satisfaction[k];
                outR -= delta * s.Radicalism[k];
            }
        }
        s.Mobility = Math.Min(inflow, outflow);
        if (outflow > 0 && inflow > 0)
        {
            var movedShare = Math.Min(1, outflow / inflow);
            for (var k = 0; k < n; k++)
            {
                var delta = sizes[k] - s.Size[k];
                if (delta <= 0 || sizes[k] <= 0)
                    continue;
                var moved = delta * movedShare;
                var stay = s.Size[k];
                var total = stay + moved;
                s.Expectations[k] = (s.Expectations[k] * stay + outE / outflow * moved) / total;
                s.Satisfaction[k] = (s.Satisfaction[k] * stay + outS / outflow * moved) / total;
                s.Radicalism[k] = (s.Radicalism[k] * stay + outR / outflow * moved) / total;
            }
        }
        s.Size = sizes;
    }

    /// <summary>Доход и потребление на человека по ППС, уровень жизни по корзинам.</summary>
    public static void UpdateLiving(World world, CountryEconomy e, CountrySociety s, double[] disposable,
        IReadOnlyList<ClassRecord> classes, SocietyBalance b)
    {
        var factor = SocietyModel.PppFactor(e) * s.Scale;
        var foodPrice = world.Economy?.FoodPrice ?? 1;
        for (var k = 0; k < ClassIds.Count; k++)
        {
            s.Income[k] = s.Size[k] > 0 ? disposable[k] * factor / s.Size[k] : 0;
            s.Consumption[k] = s.Income[k] * (1 - classes[k].Saving);
            s.StandardOfLiving[k] = StandardOfLiving(s.Consumption[k], foodPrice, b);
        }
    }

    /// <summary>
    /// Уровень жизни 0–100 по трём корзинам: базовая (еда, жильё, коммунальные, одежда; дорожает с мировыми ценами
    /// на еду), повседневная (транспорт, связь, медицина, образование детей) и престижная (досуг, путешествия,
    /// автомобиль, роскошь) — по логарифмической шкале до PrestigeMax.
    /// </summary>
    public static double StandardOfLiving(double consumption, double foodPrice, SocietyBalance b)
    {
        var basic = b.BasicBasket * (1 + b.FoodShare * b.FoodPassThrough * (foodPrice - 1));
        var basicPart = Math.Clamp(consumption / basic, 0, 1);
        var everydayPart = Math.Clamp((consumption - basic) / b.EverydayBasket, 0, 1);
        var extra = Math.Max(0, consumption - basic - b.EverydayBasket);
        var prestigePart = Math.Clamp(Math.Log(1 + extra / b.PrestigeBasket) / Math.Log(1 + b.PrestigeMax / b.PrestigeBasket), 0, 1);
        return b.BasicWeight * basicPart + b.EverydayWeight * everydayPart + b.PrestigeWeight * prestigePart;
    }
}
