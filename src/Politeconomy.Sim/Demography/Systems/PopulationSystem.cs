using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Demography.Systems;

/// <summary>
/// Месяц жизни населения: рождения по возрасту матерей, смерти по возрастам, переход 1/60 каждой пятилетней
/// группы в следующую, чистая миграция по возрастному профилю, рост городов. MODEL.md, «Демография».
/// </summary>
public sealed class PopulationSystem : ISimSystem
{
    public string Name => "Население";
    public SimPhase Phase => SimPhase.Demography;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var demography = world.Demography ?? throw new InvalidOperationException("Демография не создана");
        var b = world.Data.Demography;
        const int n = CountryDemography.Groups;
        // Миграционные потоки со временем слабеют (как в прогнозах ООН); в разогреве календарь стоит.
        var years = world.Economy is { } economy ? economy.Months / 12.0 : 0;
        var migrationFade = Math.Pow(0.5, years / b.MigrationHalfLifeYears);
        foreach (var country in world.Countries)
        {
            var d = demography[country.Id];
            var older = DemographyModel.OlderParticipation(world, country.Id);
            var laborBefore = d.LaborForce(older);
            var total = d.Total;
            var (births, deaths) = DemographyModel.MonthlyVitals(d);
            var boys = births * d.SexRatioAtBirth / (1 + d.SexRatioAtBirth);
            var migration = d.NetMigrationRate * migrationFade * total / 12
                            * (1 + Laws.LawModel.Effect(world, country.Id, "migration") * Math.Sign(d.NetMigrationRate));

            Step(d.Female, d.MortalityFemale, births - boys);
            Step(d.Male, d.MortalityMale, boys);
            for (var g = 0; g < n; g++)
            {
                var share = b.MigrationProfile[g] / 2;
                d.Female[g] = Math.Max(0, d.Female[g] + migration * share);
                d.Male[g] = Math.Max(0, d.Male[g] + migration * share);
            }

            var room = Math.Max(0, b.UrbanMax - d.UrbanPct) / Math.Max(1, b.UrbanMax - d.UrbanPct0);
            d.UrbanPct = Math.Clamp(d.UrbanPct + d.UrbanChange * room / 12, 0, 100);

            var laborAfter = d.LaborForce(older);
            d.LaborGrowth = laborBefore > 0 ? Math.Pow(laborAfter / laborBefore, 12) - 1 : 0;
            d.PushHistory(births, deaths, migration);
        }

        // Старение: каждый месяц 1/60 группы переходит в следующую; 80+ — открытая группа.
        static void Step(double[] people, double[] mortality, double newborns)
        {
            var moved = 0.0;
            for (var g = 0; g < n; g++)
            {
                var stay = people[g];
                var dead = stay * mortality[g] / 12;
                var up = g < n - 1 ? stay / 60 : 0;
                people[g] = stay - dead - up + moved;
                moved = up;
            }
            people[0] += newborns;
        }
    }
}
