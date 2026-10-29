using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Laws.Systems;

/// <summary>
/// Индексы институтов (0–100) из практики режима и законов: демократичность, верховенство права, независимость
/// судов, свобода прессы, гражданские свободы, коррупция; эффективность госаппарата — по компетентности министров;
/// легитимность — удовлетворённость классов, взвешенная по их влиянию; одобрение власти. MODEL.md, «Институты».
/// </summary>
public sealed class InstitutionsSystem : ISimSystem
{
    public string Name => "Институты";
    public SimPhase Phase => SimPhase.Politics;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world) => Publish(world);

    /// <summary>Запомнить коррупционность министров на старте.</summary>
    public static void SetupStart(World world)
    {
        foreach (var country in world.Countries)
            world.Laws![country.Id].StartCorruption = MinisterCorruption(world, country.Id);
    }

    static double MinisterCorruption(World world, string id) =>
        world.Government?.Countries.GetValueOrDefault(id) is { } g ? g.Ministers.Values.Average(m => m.Corruption) : 0;

    public static void Publish(World world)
    {
        var laws = world.Laws ?? throw new InvalidOperationException("Законы не созданы");
        var b = world.Data.LawsBalance;
        var year = world.Date.Year;
        foreach (var country in world.Countries)
        {
            var c = laws[country.Id];
            var r = (int)country.Regime;
            var stats = country.Stats;
            void Set(string id, double value, params StatFactor[] factors) =>
                stats[id] = new StatValue(Math.Clamp(value, 0, 100), year, StatSources.Model, null, null, factors.Length > 0 ? factors : null);
            void Index(string id, string effect, IReadOnlyList<double> byRegime)
            {
                var practice = byRegime[Math.Clamp(r, 0, byRegime.Count - 1)];
                var fromLaws = c.Effect(effect);
                Set(id, practice + fromLaws,
                    new StatFactor("Практика власти (режим)", practice, "number0"),
                    new StatFactor("Законы", fromLaws, "number1_signed"));
            }
            Index("democracy_index", "index.democracy", b.Democracy);
            Index("rule_of_law_index", "index.rule_of_law", b.RuleOfLaw);
            Index("judicial_independence_index", "index.judicial_independence", b.JudicialIndependence);
            Index("press_freedom_index", "index.press_freedom", b.PressFreedom);
            Index("civil_liberties_index", "index.civil_liberties", b.CivilLiberties);

            var income = b.Corruption[Math.Clamp((int)country.Income, 0, b.Corruption.Count - 1)];
            var regime = b.CorruptionRegime[Math.Clamp(r, 0, b.CorruptionRegime.Count - 1)];
            var ministers = b.CorruptionMinisters * (MinisterCorruption(world, country.Id) - c.StartCorruption);
            var laws_ = c.Effect("index.corruption");
            Set("corruption_index", income + regime + ministers + laws_,
                new StatFactor("Уровень дохода страны", income, "number0"),
                new StatFactor("Режим", regime, "number1_signed"),
                new StatFactor("Министры", ministers, "number1_signed"),
                new StatFactor("Законы", laws_, "number1_signed"));

            if (world.Government?.Countries.GetValueOrDefault(country.Id) is { } g)
                Set("government_effectiveness", g.Ministers.Values.Average(m => m.Competence));

            if (world.Society?.Countries.GetValueOrDefault(country.Id) is { } s)
            {
                var legitimacy = 0.0;
                for (var k = 0; k < s.Influence.Length; k++)
                    legitimacy += s.Influence[k] / 100 * s.Satisfaction[k];
                Set("legitimacy", legitimacy);
                var satisfaction = s.Average(s.Satisfaction);
                var popularity = world.Government?.Countries.GetValueOrDefault(country.Id)?.Leader.Popularity ?? 45;
                var fromMood = b.ApprovalSatisfaction * (satisfaction - 50);
                var fromLeader = b.ApprovalPopularity * (popularity - 45);
                Set("approval_pct", b.ApprovalBase + fromMood + fromLeader,
                    new StatFactor("Удовлетворённость людей", fromMood, "number1_signed"),
                    new StatFactor("Популярность главы", fromLeader, "number1_signed"));
            }
        }
    }
}
