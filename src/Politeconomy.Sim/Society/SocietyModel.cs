using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society.Systems;

namespace Politeconomy.Sim.Society;

/// <summary>Сборка классов: состав, доходы и настроение на старте и системы в порядке MODEL.md.</summary>
public static class SocietyModel
{
    public static IReadOnlyList<ISimSystem> Systems() =>
    [
        new ClassesSystem(),
        new ClassMoodSystem(),
        new SocietyStatsSystem(),
    ];

    /// <summary>Классы на старте: ожидания равны достигнутому уровню жизни, настроение — спокойное для текущих условий.</summary>
    public static void Setup(World world)
    {
        var economy = world.Economy ?? throw new InvalidOperationException("Экономика не создана: вызовите EconomyModel.Setup");
        var demography = world.Demography ?? throw new InvalidOperationException("Демография не создана");
        var data = world.Data;
        var classes = data.Classes.Classes;
        var b = data.Society;
        var society = new WorldSociety();
        foreach (var country in world.Countries)
        {
            var s = new CountrySociety();
            for (var k = 0; k < ClassIds.Count; k++)
            {
                s.Organization[k] = classes[k].Organization;
                s.Radicalism[k] = classes[k].Radicalism;
            }
            var composition = Compose(world, country);
            s.Heads = composition.Heads;
            s.Size = composition.Sizes;
            var e = economy[country.Id];
            var flows = ClassIncome.Compute(data, classes, e, economy, composition, key => Laws.LawModel.Effect(world, country.Id, key));
            s.Scale = CalibrateScale(country, e, flows.Disposable, classes);
            ClassesSystem.UpdateLiving(world, e, s, flows.Disposable, classes, b);
            for (var k = 0; k < ClassIds.Count; k++)
            {
                s.Savings[k] = b.SavingsDecay > 0 ? s.Income[k] * classes[k].Saving / b.SavingsDecay : 0;
                s.Expectations[k] = s.StandardOfLiving[k];
            }
            ClassMoodSystem.Update(country, e, s, classes, b, months: 0);
            society.Countries[country.Id] = s;
        }
        world.Society = society;
        SocietyStatsSystem.Publish(world);
    }

    /// <summary>
    /// Состав классов по текущей экономике и населению страны. Квалификация работников — из модели образования:
    /// высшее и профессиональное образование в рабочей силе (с задержкой в годы после изменения охвата).
    /// </summary>
    public static ClassComposition.Result Compose(World world, Country country)
    {
        double S(string id, double fallback) => country.Info.Stats.TryGetValue(id, out var v) ? v.Value : fallback;
        var b = world.Data.Society;
        var e = world.Economy![country.Id];
        var d = world.Demography![country.Id];
        var selfEmployed = Math.Clamp(S("self_employed_pct", 30) / 100, 0, 0.98);
        var education = world.Education?.Countries.GetValueOrDefault(country.Id);
        var tertiary = S("tertiary_enrollment_pct", 40);
        var vocational = education != null ? education.VocationalStock - 1 : 0;
        var growth = education is { Tertiary0: > 0 } ? education.TertiaryStock / education.Tertiary0 : 1;
        return ClassComposition.Compute(world.Data.Industries.Industries, e, d, selfEmployed, tertiary, b,
            Demography.DemographyModel.OlderParticipation(world, country.Id),
            LawModel.Effect(world, country.Id, "small_business"), vocational, growth);
    }

    /// <summary>Во сколько раз реальный ВВП на душу выше стартового.</summary>
    public static double IncomeGrowth(Economy.CountryEconomy e) =>
        e.RealGdp0 > 0 && e.Population > 0 && e.Population0 > 0 ? e.RealGdp / e.RealGdp0 / (e.Population / e.Population0) : 1;

    /// <summary>
    /// Уровень доходов подгоняется к данным: потребление всех классов на старте равно потреблению домохозяйств
    /// из национальных счетов (по ППС).
    /// </summary>
    static double CalibrateScale(Country country, Economy.CountryEconomy e, double[] disposable, IReadOnlyList<ClassRecord> classes)
    {
        var share = country.Info.Stats.TryGetValue("consumption_private_pct_gdp", out var v) ? v.Value / 100 : 0.6;
        var target = Math.Clamp(share, 0.25, 1.0) * PppGdp(e);
        var model = 0.0;
        var factor = PppFactor(e);
        for (var k = 0; k < ClassIds.Count; k++)
            model += disposable[k] * factor * (1 - classes[k].Saving);
        return model > 0 ? Math.Clamp(target / model, 0.5, 2) : 1;
    }

    /// <summary>ВВП страны по ППС в ценах 2026 года: стартовый уровень × реальный рост.</summary>
    public static double PppGdp(Economy.CountryEconomy e) =>
        e.RealGdp0 > 0 ? e.Ppp0 * e.Population0 * e.RealGdp / e.RealGdp0 : 0;

    /// <summary>Сколько долларов по ППС 2026 года в одной местной единице сегодня.</summary>
    public static double PppFactor(Economy.CountryEconomy e) => e.NominalGdp > 0 ? PppGdp(e) / e.NominalGdp : 0;

    /// <summary>Насколько власть сменяема на выборах: демократия — 1, гибридный режим — 0,5, авторитарный — 0.</summary>
    public static double Openness(Regime regime) => regime switch
    {
        Regime.Democracy => 1,
        Regime.Hybrid => 0.5,
        _ => 0,
    };
}
