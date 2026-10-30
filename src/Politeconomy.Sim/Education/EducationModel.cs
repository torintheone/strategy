using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Education;

/// <summary>
/// Образование: охват и качество зависят от финансирования на ученика (расходы бюджета на образование и число
/// молодых), законов и роста доходов. Охват становится выпуском через годы учёбы, а выпуск медленно обновляет рабочую
/// силу — поэтому квалификация работников меняется с задержкой в годы. Квалификация задаёт состав классов, человеческий
/// капитал — прибавку к росту. MODEL.md, «Образование».
/// </summary>
public static class EducationModel
{
    public static IReadOnlyList<ISimSystem> Systems() => [new Systems.EducationSystem()];

    /// <summary>Старт: охват, выпуск и запас равны данным (страна в равновесии), качество — по доходу и расходам.</summary>
    public static void Setup(World world)
    {
        var b = world.Data.EducationBalance;
        var education = new WorldEducation();
        foreach (var country in world.Countries)
        {
            var income = Math.Clamp((int)country.Income, 0, 3);
            var spending = Spending(world, country);
            var tertiary = Math.Min(b.TertiaryCeiling, Stat(country, "tertiary_enrollment_pct", 40));
            var quality = Math.Clamp(b.Quality[income] + b.QualitySpending * (spending * 100 - b.SpendingReference), 10, 95);
            var school = b.School[income];
            var e = new CountryEducation
            {
                School = school, SchoolGraduates = school, Literacy = school, Literacy0 = school,
                Tertiary = tertiary, TertiaryGraduates = tertiary, TertiaryStock = tertiary, Tertiary0 = tertiary,
                Quality = quality, Quality0 = quality,
                Spending0 = spending, Youth0 = Youth(world, country),
            };
            education.Countries[country.Id] = e;
        }
        world.Education = education;
        foreach (var country in world.Countries)
            Publish(world, country, education[country.Id]);
    }

    static double Stat(Country country, string id, double fallback) =>
        country.Info.Stats.TryGetValue(id, out var v) && double.IsFinite(v.Value) ? v.Value : fallback;

    /// <summary>Расходы на образование, доля ВВП: план бюджета плюс поправки законов.</summary>
    public static double Spending(World world, Country country)
    {
        var items = world.Data.Budget.Spending;
        var k = items.ToList().FindIndex(s => s.Id == "education");
        var e = world.Economy![country.Id];
        return k < 0 || k >= e.SpendingPlan.Length ? 0.045 : Math.Max(0, e.SpendingPlan[k] + LawModel.Effect(world, country.Id, "spending.education") / 100);
    }

    /// <summary>Доля молодёжи 5–24 лет в населении (столько учеников и студентов).</summary>
    static double Youth(World world, Country country)
    {
        var d = world.Demography![country.Id];
        var young = 0.0;
        for (var g = 1; g <= 4; g++)
            young += d.Female[g] + d.Male[g];
        return d.Total > 0 ? young / d.Total : 0.3;
    }

    /// <summary>Месяц: финансирование, качество и охват идут к цели, охват прошлых лет становится выпуском, выпуск обновляет рабочую силу.</summary>
    public static void Update(World world, Country country, CountryEducation e)
    {
        var b = world.Data.EducationBalance;
        var youth = Youth(world, country);
        var spending = Spending(world, country);
        e.Funding = Math.Clamp(e.Spending0 > 0 && youth > 0 ? spending / e.Spending0 / (youth / e.Youth0) : 1, b.FundingMin, b.FundingMax);
        var income = Math.Clamp((int)country.Income, 0, 3);

        var quality = Math.Clamp(e.Quality0 * Math.Pow(e.Funding, b.QualityElasticity), 5, 100);
        e.Quality += (quality - e.Quality) / b.QualityMonths;
        var school = Math.Min(100, Math.Max(e.Literacy0, b.School[income]) * Math.Pow(e.Funding, b.SchoolElasticity));
        var richer = Math.Max(0.1, Society.SocietyModel.IncomeGrowth(world.Economy![country.Id]));
        var tertiary = Math.Min(b.TertiaryCeiling, e.Tertiary0 * Math.Pow(richer, b.TertiaryGrowth) * Math.Pow(e.Funding, b.TertiaryElasticity)
                                                   * Math.Max(0, 1 + LawModel.Effect(world, country.Id, "tertiary")));
        var vocational = Math.Pow(e.Funding, b.VocationalElasticity) * Math.Max(0, 1 + LawModel.Effect(world, country.Id, "vocational"));
        e.School += (school - e.School) / b.EnrollMonths;
        e.Tertiary += (tertiary - e.Tertiary) / b.EnrollMonths;
        e.Vocational += (vocational - e.Vocational) / b.EnrollMonths;

        e.SchoolGraduates += (e.School - e.SchoolGraduates) / (b.SchoolLag * 12);
        e.VocationalGraduates += (e.Vocational - e.VocationalGraduates) / (b.VocationalLag * 12);
        e.TertiaryGraduates += (e.Tertiary - e.TertiaryGraduates) / (b.TertiaryLag * 12);
        var renew = 1 / (b.RenewYears * 12);
        e.Literacy += (e.SchoolGraduates - e.Literacy) * renew;
        e.VocationalStock += (e.VocationalGraduates - e.VocationalStock) * renew;
        e.TertiaryStock += (e.TertiaryGraduates - e.TertiaryStock) * renew;

        var before = e.HumanCapital;
        e.HumanCapital = HumanCapital(b, e);
        // Рост человеческого капитала (годовой темп по месяцу) прибавляет к росту потенциального ВВП.
        var growth = before > 0 && e.HumanCapital > 0 ? Math.Log(e.HumanCapital / before) * 12 * 100 : 0;
        e.Effects["potential_growth"] = b.GrowthElasticity * growth;
        Publish(world, country, e);
    }

    /// <summary>Человеческий капитал: произведение относительных уровней высшего, профессионального, грамотности и качества в степенях-весах.</summary>
    public static double HumanCapital(EducationBalance b, CountryEducation e) =>
        Math.Pow(Ratio(e.TertiaryStock, e.Tertiary0), b.HighWeight) * Math.Pow(Math.Max(0.01, e.VocationalStock), b.VocationalWeight)
        * Math.Pow(Ratio(e.Literacy, e.Literacy0), b.LiteracyWeight) * Math.Pow(Ratio(e.Quality, e.Quality0), b.QualityWeight);

    static double Ratio(double now, double start) => start > 0 ? Math.Max(0.01, now / start) : 1;

    /// <summary>Показатели: охват высшим образованием, качество, грамотность, человеческий капитал (100 на старте).</summary>
    static void Publish(World world, Country country, CountryEducation e)
    {
        var year = world.Date.Year;
        void Set(string id, double value, params StatFactor[] factors) =>
            country.Stats[id] = new StatValue(value, year, StatSources.Model, null, null, factors.Length > 0 ? factors : null);
        Set("tertiary_enrollment_pct", e.Tertiary);
        Set("education_quality", e.Quality,
            new StatFactor("Финансирование на ученика к старту", e.Funding * 100, "number0"));
        Set("literacy_pct", e.Literacy);
        Set("human_capital", e.HumanCapital * 100,
            new StatFactor("Высшее образование в рабочей силе (охват прошлых лет)", e.TertiaryStock, "number1"),
            new StatFactor("Профессиональное (индекс)", e.VocationalStock * 100, "number0"),
            new StatFactor("Грамотность", e.Literacy, "number1"),
            new StatFactor("Качество образования", e.Quality, "number0"));
    }
}
