using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Health;

/// <summary>
/// Здравоохранение и социальная защита. Врачи и койки идут за финансированием на человека (с поправкой на старение)
/// с задержкой в годы; доступность — по системе здравоохранения (закон) и программам. Индекс здоровья прибавляет к
/// продолжительности жизни и снижает потери рабочего времени из-за болезней (а это — рост). Старение увеличивает
/// расходы на пенсии. MODEL.md, «Здравоохранение».
/// </summary>
public static class HealthModel
{
    public static IReadOnlyList<ISimSystem> Systems() => [new Systems.HealthSystem()];

    /// <summary>Старт (после законов): ресурсы и доступность — по группе дохода и системе здравоохранения; программы.</summary>
    public static void Setup(World world)
    {
        var b = world.Data.HealthBalance;
        var health = new WorldHealth();
        world.Health = health;
        ProgramModel.Setup(world);
        foreach (var country in world.Countries)
        {
            var income = Math.Clamp((int)country.Income, 0, 3);
            var d = world.Demography![country.Id];
            var access = AccessTarget(world, country, b);
            var h = new CountryHealth
            {
                Doctors = b.Doctors[income], Doctors0 = b.Doctors[income],
                Beds = b.Beds[income], Beds0 = b.Beds[income],
                Access = access, Access0 = access,
                Burden = b.Burden[income], Burden0 = b.Burden[income],
                Spending0 = Spending(world, country, "health"),
                Need0 = Need(b, d),
                Elderly0 = d.Total > 0 ? d.Elderly / d.Total : 0.1,
            };
            health.Countries[country.Id] = h;
            ProgramModel.Recompute(world, country.Id);
            Publish(world, country, h);
        }
    }

    /// <summary>Потребность в медицине на человека: старикам её нужно больше.</summary>
    static double Need(HealthBalance b, Demography.CountryDemography d) => 1 + b.ElderlyNeed * (d.Total > 0 ? d.Elderly / d.Total : 0.1);

    /// <summary>Расходы бюджета на статью (доля ВВП): план плюс поправки законов (без поправок самой медицины).</summary>
    static double Spending(World world, Country country, string item)
    {
        var k = world.Data.Budget.Spending.ToList().FindIndex(s => s.Id == item);
        var e = world.Economy![country.Id];
        return k < 0 || k >= e.SpendingPlan.Length ? 0.05 : Math.Max(0, e.SpendingPlan[k] + LawModel.Effect(world, country.Id, "spending." + item) / 100);
    }

    /// <summary>Доступность по системе здравоохранения (закон) и группе дохода плюс программы (медицина на селе).</summary>
    static double AccessTarget(World world, Country country, HealthBalance b)
    {
        var income = Math.Clamp((int)country.Income, 0, 3);
        var law = world.Data.Laws.Laws.FirstOrDefault(l => l.Id == "health_system");
        var system = world.Laws?.Countries.GetValueOrDefault(country.Id)?.Current.GetValueOrDefault("health_system")
                     ?? (law != null ? LawModel.StartVariant(law, country) : "insurance");
        var table = system switch
        {
            "private" => b.AccessPrivate,
            "state" => b.AccessState,
            _ => b.AccessInsurance,
        };
        var programs = world.Programs?.Countries.GetValueOrDefault(country.Id)?.Effects.GetValueOrDefault("health_access") ?? 0;
        return Math.Clamp(table[income] + programs, 5, 100);
    }

    /// <summary>
    /// Месяц: финансирование, врачи, койки и доступность идут к цели; индекс здоровья, потери от болезней, поправки:
    /// продолжительность жизни, рост (от изменения потерь рабочего времени), расходы на пенсии (старение).
    /// </summary>
    public static void Update(World world, Country country, CountryHealth h)
    {
        var b = world.Data.HealthBalance;
        var d = world.Demography![country.Id];
        ProgramModel.Recompute(world, country.Id);
        var programs = world.Programs![country.Id].Effects;
        var need = Need(b, d);
        // Деньги программ идут на сами программы, а не на врачей и койки.
        var spending = Spending(world, country, "health") - programs.GetValueOrDefault("spending.health") / 100;
        h.Funding = Math.Clamp(h.Spending0 > 0 ? spending / h.Spending0 / (need / h.Need0) : 1, b.FundingMin, b.FundingMax);
        h.Doctors += (h.Doctors0 * Math.Pow(h.Funding, b.DoctorsElasticity) - h.Doctors) / (b.DoctorsYears * 12);
        h.Beds += (h.Beds0 * Math.Pow(h.Funding, b.BedsElasticity) - h.Beds) / (b.BedsYears * 12);
        h.Access += (AccessTarget(world, country, b) - h.Access) / b.AccessMonths;

        var capacity = Math.Pow(h.Doctors / h.Doctors0, b.DoctorsWeight) * Math.Pow(h.Beds / h.Beds0, b.BedsWeight)
                       * (1 + programs.GetValueOrDefault("health_capacity"));
        h.Index = Math.Pow(capacity, 1 - b.AccessWeight) * Math.Pow(h.Access / h.Access0, b.AccessWeight);
        var before = h.Burden;
        h.Burden = Math.Clamp(h.Burden0 / Math.Pow(h.Index, b.BurdenElasticity) * (1 + programs.GetValueOrDefault("disease_burden")
                   + (world.Infrastructure?.Countries.GetValueOrDefault(country.Id)?.Effects.GetValueOrDefault("disease_burden") ?? 0)), 0.5, 30);

        h.Effects["life_expectancy"] = b.LifeScale * Math.Log(Math.Max(0.05, h.Index));
        // Меньше болеют — больше работают: изменение доли рабочего времени (годовой темп) — к росту.
        h.Effects["potential_growth"] = Math.Log((100 - h.Burden) / (100 - before)) * 12 * 100;
        // Старение: пенсий платят больше, когда растёт доля людей 65+.
        var elderly = d.Total > 0 ? d.Elderly / d.Total : h.Elderly0;
        var social = SpendingPlan(world, country, "social");
        h.Effects["spending.social"] = social * (Math.Pow(elderly / h.Elderly0, b.PensionAging) - 1) * 100;
        Publish(world, country, h);
    }

    static double SpendingPlan(World world, Country country, string item)
    {
        var k = world.Data.Budget.Spending.ToList().FindIndex(s => s.Id == item);
        var e = world.Economy![country.Id];
        return k < 0 || k >= e.SpendingPlan.Length ? 0 : e.SpendingPlan[k];
    }

    static void Publish(World world, Country country, CountryHealth h)
    {
        var year = world.Date.Year;
        var d = world.Demography![country.Id];
        void Set(string id, double value, params StatFactor[] factors) =>
            country.Stats[id] = new StatValue(value, year, StatSources.Model, null, null, factors.Length > 0 ? factors : null);
        Set("doctors_per_1000", h.Doctors, new StatFactor("Финансирование медицины на человека к старту", h.Funding * 100, "number0"));
        Set("hospital_beds_per_1000", h.Beds);
        Set("health_access_pct", h.Access);
        Set("disease_burden_pct", h.Burden);
        Set("infant_mortality", d.InfantMortality);
    }
}
