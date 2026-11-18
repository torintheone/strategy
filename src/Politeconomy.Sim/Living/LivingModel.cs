using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Living;

/// <summary>
/// Жильё, преступность, коррупция и экология. Жильё строит отрасль «Строительство», оно ветшает, а желаемая площадь
/// растёт с доходом: нехватка поднимает цены, злит людей и снижает рождаемость. Преступность растёт от безработицы,
/// бедности, молодёжи и коррупции и падает от денег на полицию. Коррупция бьёт по налогам, росту и доверию. Загрязнение
/// растёт с промышленностью и падает с технологиями и законами; оно сокращает жизнь и злит горожан.
/// MODEL.md, «Жильё, преступность, коррупция, экология».
/// </summary>
public static class LivingModel
{
    public static IReadOnlyList<ISimSystem> Systems() => [new Systems.LivingSystem()];

    static readonly string[] Taxes = ["revenue.pit", "revenue.cit", "revenue.vat", "revenue.social"];

    public static void Setup(World world)
    {
        var b = world.Data.LivingBalance;
        var living = new WorldLiving();
        foreach (var country in world.Countries)
        {
            var income = Math.Clamp((int)country.Income, 0, 3);
            var d = world.Demography![country.Id];
            var e = world.Economy![country.Id];
            var housing = b.Housing[income];
            var growth = Math.Max(0, Stat(country, "population_growth_pct", 1) / 100);
            var industry = Stat(country, "industry_pct_gdp", 30);
            var c = new CountryLiving
            {
                Housing = housing, Housing0 = housing, Desired = housing, Stock = housing * d.Total,
                Build0 = housing * d.Total * (growth + b.Depreciation),
                Construction0 = Output(world, e, "construction"),
                Crime = b.Crime[income], Crime0 = b.Crime[income],
                Pollution = Math.Clamp(b.Pollution[income] + b.PollutionIndustry * (industry - 30), 5, 95),
                Industry0 = IndustryOutput(world, e),
                Income0 = SocietyModel.IncomeGrowth(e),
                Corruption0 = Stat(country, "corruption_index", 50),
                Administration0 = Spending(world, country, "administration"),
                Police0 = Spending(world, country, "police"),
                Unemployment0 = e.Unemployment * 100,
                Living0 = Stat(country, "standard_of_living", 50),
                Youth0 = Youth(d),
                Mood = new double[ClassIds.Count],
            };
            c.Pollution0 = c.Pollution;
            living.Countries[country.Id] = c;
            Publish(world, country, c);
        }
        world.Living = living;
    }

    /// <summary>Текущий показатель модели, иначе — из данных, иначе — запасное значение.</summary>
    static double Stat(Country country, string id, double fallback) =>
        country.Stat(id) is var v && double.IsFinite(v) ? v
        : country.Info.Stats.TryGetValue(id, out var data) && double.IsFinite(data.Value) ? data.Value : fallback;

    static double Output(World world, Economy.CountryEconomy e, string industry)
    {
        var i = world.Data.Industries.Industries.ToList().FindIndex(x => x.Id == industry);
        return i >= 0 && i < e.Output.Length ? e.Output[i] : 0;
    }

    /// <summary>Выпуск промышленности (добыча, обработка, стройка).</summary>
    static double IndustryOutput(World world, Economy.CountryEconomy e)
    {
        var industries = world.Data.Industries.Industries;
        var total = 0.0;
        for (var i = 0; i < industries.Count && i < e.Output.Length; i++)
            if (industries[i].Sector == "industry")
                total += e.Output[i];
        return total;
    }

    /// <summary>Расходы бюджета на статью, доля ВВП: план плюс поправки законов, без денег программ (они идут на сами программы).</summary>
    static double Spending(World world, Country country, string item)
    {
        var k = world.Data.Budget.Spending.ToList().FindIndex(s => s.Id == item);
        var e = world.Economy![country.Id];
        var programs = world.Programs?.Countries.GetValueOrDefault(country.Id)?.Effects.GetValueOrDefault("spending." + item) ?? 0;
        return k < 0 || k >= e.SpendingPlan.Length ? 0
            : Math.Max(0, e.SpendingPlan[k] + (LawModel.Effect(world, country.Id, "spending." + item) - programs) / 100);
    }

    /// <summary>Доля молодёжи 15–24 лет.</summary>
    static double Youth(Demography.CountryDemography d) =>
        d.Total > 0 ? (d.Female[3] + d.Male[3] + d.Female[4] + d.Male[4]) / d.Total : 0.15;

    static double Ratio(double now, double start) => start > 0 && now > 0 ? now / start : 1;

    /// <summary>Расходы к стартовым — не меньше 1/SpendingRange и не больше SpendingRange раз.</summary>
    static double Funding(double now, double start, LivingBalance b) => Math.Clamp(Ratio(now, start), 1 / b.SpendingRange, b.SpendingRange);

    /// <summary>Месяц: стройка и износ жилья, преступность, загрязнение, коррупция; настроение классов и поправки к модели.</summary>
    public static void Update(World world, Country country, CountryLiving c)
    {
        var b = world.Data.LivingBalance;
        var d = world.Demography![country.Id];
        var e = world.Economy![country.Id];
        var s = world.Society?.Countries.GetValueOrDefault(country.Id);
        double Fx(string key) => LawModel.Effect(world, country.Id, key);

        // Жильё: строит отрасль «Строительство» — больше, когда жильё дорогое (программы и законы добавляют); фонд ветшает,
        // население его делит; желаемая площадь растёт с доходом.
        var income = Ratio(SocietyModel.IncomeGrowth(e), c.Income0);
        c.Desired = c.Housing0 * Math.Pow(Math.Max(0.1, income), b.DesiredGrowth);
        var build = c.Build0 * Ratio(Output(world, e, "construction"), c.Construction0)
                    * Math.Pow(c.Desired / c.Housing, b.BuildResponse) * Math.Max(0, 1 + Fx("housing_build"));
        c.Stock += (build - c.Stock * b.Depreciation) / 12;
        c.Housing = d.Total > 0 ? c.Stock / d.Total : c.Housing;
        var shortage = Math.Max(0, 1 - c.Housing / c.Desired);

        // Коррупция — от институтов (InstitutionsSystem); здесь — её цена и зарплаты чиновников.
        var corruption = Stat(country, "corruption_index", c.Corruption0) - c.Corruption0;

        // Преступность.
        var unemployment = b.CrimeUnemployment * (e.Unemployment * 100 - c.Unemployment0);
        var poverty = b.CrimeLiving * (c.Living0 - Stat(country, "standard_of_living", c.Living0));
        var youth = b.CrimeYouth * (Youth(d) - c.Youth0);
        var police = -b.CrimePolice * Math.Log(Funding(Spending(world, country, "police"), c.Police0, b));
        var bribes = b.CrimeCorruption * corruption;
        var raw = c.Crime0 + unemployment + poverty + youth + police + bribes;
        var crime = Math.Clamp(raw * Math.Max(0.1, 1 + Fx("crime")), 1, 100);
        c.Crime += (crime - c.Crime) / b.CrimeMonths;
        StatFactor[] causes =
        [
            new("Обычный уровень для страны", c.Crime0, "number0"),
            new("Безработица", unemployment, "number1_signed"),
            new("Бедность", poverty, "number1_signed"),
            new("Молодёжь", youth, "number1_signed"),
            new("Деньги на полицию", police, "number1_signed"),
            new("Коррупция", bribes, "number1_signed"),
            new("Законы и программы", crime - raw, "number1_signed"),
        ];

        // Загрязнение.
        var years = (world.Date.DayNumber - world.Data.Scenario.StartDate.DayNumber) / 365.25;
        var pollution = c.Pollution0 * Math.Pow(Ratio(IndustryOutput(world, e), c.Industry0), b.PollutionOutput)
                        * Math.Pow(Math.Max(0.1, income), -b.PollutionIncome)
                        * Math.Exp(-b.PollutionTech * years) * Math.Max(0.1, 1 + Fx("pollution"));
        c.Pollution += (Math.Clamp(pollution, 1, 100) - c.Pollution) / b.PollutionMonths;

        // Настроение классов.
        var classes = world.Data.Classes.Classes;
        if (c.Mood.Length != classes.Count)
            c.Mood = new double[classes.Count];
        for (var k = 0; k < classes.Count; k++)
        {
            var elite = k is ClassIds.Capitalists or ClassIds.Officials ? 0.5 : 1;
            var urban = k is ClassIds.Professionals or ClassIds.Clerks ? b.PollutionUrban : b.PollutionMood;
            c.Mood[k] = -b.HousingMood * shortage * elite - b.CrimeMood * (c.Crime - c.Crime0) - urban * (c.Pollution - c.Pollution0);
        }

        // Поправки к модели.
        var fx = c.Effects;
        fx.Clear();
        fx["fertility"] = -b.HousingFertility * shortage;
        fx["legitimacy"] = -b.CrimeLegitimacy * (c.Crime - c.Crime0) - b.CorruptionLegitimacy * corruption;
        fx["potential_growth"] = -b.CrimeGrowth * (c.Crime - c.Crime0) - b.CorruptionGrowth * corruption;
        foreach (var tax in Taxes)
            fx[tax] = -b.CorruptionTaxes * corruption;
        fx["index.corruption"] = -b.CorruptionWages * Math.Log(Funding(Spending(world, country, "administration"), c.Administration0, b));
        fx["life_expectancy"] = -b.PollutionLife * (c.Pollution - c.Pollution0);
        Publish(world, country, c, causes);
    }

    /// <summary>Показатели; у преступности — к чему она стремится и почему (стартовый уровень и вклады).</summary>
    static void Publish(World world, Country country, CountryLiving c, StatFactor[]? causes = null)
    {
        var year = world.Date.Year;
        var b = world.Data.LivingBalance;
        void Set(string id, double value, params StatFactor[] factors) =>
            country.Stats[id] = new StatValue(value, year, StatSources.Model, null, null, factors.Length > 0 ? factors : null);
        var shortage = Math.Max(0, 1 - c.Housing / c.Desired);
        Set("housing_m2_per_person", c.Housing, new StatFactor("Хотели бы при нынешних доходах", c.Desired, "number1"));
        Set("housing_price_index", Math.Clamp(100 * Math.Pow(c.Desired / c.Housing, b.PriceElasticity)
                                              * Math.Max(0.1, 1 + LawModel.Effect(world, country.Id, "housing_price")), 10, 1000));
        Set("housing_shortage_pct", shortage * 100);
        Set("crime_index", c.Crime, causes ?? []);
        Set("pollution_index", c.Pollution);
    }
}
