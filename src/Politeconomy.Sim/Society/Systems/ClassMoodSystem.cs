using Politeconomy.Sim.Data;
using Politeconomy.Sim.Economy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Society.Systems;

/// <summary>
/// Настроение классов за месяц. Ожидания медленно догоняют достигнутый уровень жизни, поэтому удовлетворённость
/// отвечает на перемены: рост радует, падение злит. Её тянут вниз страх безработицы и инфляция. Долгое недовольство
/// растит радикализм. Влияние класса — численность, богатство и организованность с поправкой на политическую
/// систему. MODEL.md, «Классы».
/// </summary>
public sealed class ClassMoodSystem : ISimSystem
{
    public string Name => "Настроение классов";
    public SimPhase Phase => SimPhase.SocialSphere;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var society = world.Society ?? throw new InvalidOperationException("Классы не созданы: вызовите SocietyModel.Setup");
        var classes = world.Data.Classes.Classes;
        var b = world.Data.Society;
        var weight = world.Data.LawsBalance.AttitudeWeight;
        foreach (var country in world.Countries)
        {
            var laws = world.Laws?.Countries.GetValueOrDefault(country.Id);
            var lawMood = laws?.Attitudes.Select(a => a * weight).ToArray();
            // Скандал (раскрытые фальсификации) радикализирует всех, как закон с поправкой «радикализм».
            var scandal = world.Data.PoliticsBalance.ScandalRadicalism * Politics.PoliticsModel.Scandal(world, country.Id);
            Update(country, world.Economy![country.Id], society[country.Id], classes, b, months: 1,
                id => (laws?.Effect(id) ?? 0) + (id == "radicalism" ? scandal : 0), lawMood);
        }
    }

    /// <summary>
    /// Один месяц настроения. При months = 0 (старт) удовлетворённость и радикализм сразу ставятся в равновесие
    /// для текущих условий, ожидания не двигаются.
    /// </summary>
    /// <remarks>
    /// Effect — поправки законов (страх безработицы, радикализм, организованность); lawMood — сдвиг удовлетворённости
    /// каждого класса от законов, принятых после старта.
    /// </remarks>
    public static void Update(Country country, CountryEconomy e, CountrySociety s, IReadOnlyList<ClassRecord> classes,
        SocietyBalance b, int months, Func<string, double>? effect = null, IReadOnlyList<double>? lawMood = null)
    {
        const int n = ClassIds.Count;
        effect ??= _ => 0;
        var fearScale = Math.Max(0, 1 + effect("unemployment_fear"));
        var radicalShift = effect("radicalism");
        var unemploymentGap = Math.Max(0, (e.Unemployment - e.NaturalUnemployment) * 100);
        var excess = Math.Max(0, e.Inflation * 100 - b.InflationThreshold);
        var inflationExcess = b.InflationScale > 0 ? b.InflationScale * Math.Log(1 + excess / b.InflationScale) : excess;
        double change = 0, level = 0, fear = 0, inflation = 0, fromLaws = 0;
        var total = s.Total;
        for (var k = 0; k < n; k++)
        {
            var sol = s.StandardOfLiving[k];
            var changePart = b.ChangeWeight * (sol - s.Expectations[k]);
            var levelPart = b.LevelWeight * (sol - 50);
            var fearPart = b.UnemploymentFear * classes[k].JobRisk * unemploymentGap * fearScale + (k == ClassIds.Unemployed ? b.UnemployedPenalty : 0);
            var inflationPart = b.InflationPain * classes[k].Inflation * inflationExcess;
            var lawPart = lawMood != null && k < lawMood.Count ? lawMood[k] : 0;
            var target = Math.Clamp(50 + changePart + levelPart - fearPart - inflationPart + lawPart, 0, 100);
            var calm = classes[k].Radicalism + radicalShift;
            var radicalTarget = Math.Clamp(calm + b.RadicalismSlope * Math.Max(0, b.RadicalismThreshold - target), 0, 100);
            s.Organization[k] = Math.Clamp(classes[k].Organization + effect("organization." + classes[k].Id), 0, 1);
            if (months == 0)
            {
                s.Satisfaction[k] = target;
                s.Radicalism[k] = radicalTarget;
            }
            else
            {
                s.Satisfaction[k] += (target - s.Satisfaction[k]) / b.SatisfactionMonths;
                var radicalCurrent = Math.Clamp(calm + b.RadicalismSlope * Math.Max(0, b.RadicalismThreshold - s.Satisfaction[k]), 0, 100);
                s.Radicalism[k] += b.RadicalismSpeed * (radicalCurrent - s.Radicalism[k]);
                s.Expectations[k] += (sol - s.Expectations[k]) / b.ExpectationMonths;
            }
            if (total > 0)
            {
                var w = s.Size[k] / total;
                change += w * changePart;
                level += w * levelPart;
                fear += w * fearPart;
                inflation += w * inflationPart;
                fromLaws += w * lawPart;
            }
        }
        (s.MoodChange, s.MoodLevel, s.MoodFear, s.MoodInflation, s.MoodLaws) = (change, level, fear, inflation, fromLaws);
        UpdateInfluence(country, s, b);
    }

    /// <summary>
    /// Влияние = доля в населении^a × доля в доходах^b × организованность × близость к власти, в процентах.
    /// При демократии больше весит численность, при авторитарном режиме — богатство, госаппарат и капитал,
    /// гибридный режим — посередине.
    /// </summary>
    static void UpdateInfluence(Country country, CountrySociety s, SocietyBalance b)
    {
        const int n = ClassIds.Count;
        // Доля «выборности» власти: демократия — 1, гибридный режим — половина, авторитарный — 0.
        var t = SocietyModel.Openness(country.Regime);
        var numbers = t * b.DemocracyNumbers + (1 - t) * b.AutocracyNumbers;
        var wealth = t * b.DemocracyWealth + (1 - t) * b.AutocracyWealth;
        var population = s.Total;
        var income = 0.0;
        for (var k = 0; k < n; k++)
            income += s.Income[k] * s.Size[k];
        var raw = new double[n];
        for (var k = 0; k < n; k++)
        {
            if (population <= 0 || income <= 0 || s.Size[k] <= 0)
                continue;
            var proximity = 1 + (1 - t) * (k switch
            {
                ClassIds.Officials => b.AutocracyOfficials - 1,
                ClassIds.Capitalists => b.AutocracyCapital - 1,
                _ => 0,
            });
            raw[k] = Math.Pow(s.Size[k] / population, numbers) * Math.Pow(s.Income[k] * s.Size[k] / income, wealth)
                * s.Organization[k] * proximity;
        }
        var sum = raw.Sum();
        for (var k = 0; k < n; k++)
            s.Influence[k] = sum > 0 ? raw[k] / sum * 100 : 0;
    }
}
