using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Society.Systems;

/// <summary>Переносит классы в показатели страны: средние по населению уровень жизни, удовлетворённость и радикализм.</summary>
public sealed class SocietyStatsSystem : ISimSystem
{
    public string Name => "Показатели классов";
    public SimPhase Phase => SimPhase.SocialSphere;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world) => Publish(world);

    public static void Publish(World world)
    {
        var society = world.Society ?? throw new InvalidOperationException("Классы не созданы");
        var year = world.Date.Year;
        foreach (var country in world.Countries)
        {
            var s = society[country.Id];
            var stats = country.Stats;
            void Set(string id, double value, params StatFactor[] factors) =>
                stats[id] = new StatValue(value, year, StatSources.Model, null, null, factors.Length > 0 ? factors : null);
            var poorest = Enumerable.Range(0, ClassIds.Count).Where(k => s.Size[k] > 0).MinBy(k => s.StandardOfLiving[k]);
            var richest = Enumerable.Range(0, ClassIds.Count).Where(k => s.Size[k] > 0).MaxBy(k => s.StandardOfLiving[k]);
            var names = world.Data.Classes.Classes;

            Set("standard_of_living", s.Average(s.StandardOfLiving),
                new StatFactor(names[poorest].Name, s.StandardOfLiving[poorest], "number0"),
                new StatFactor(names[richest].Name, s.StandardOfLiving[richest], "number0"));
            Set("satisfaction", s.Average(s.Satisfaction),
                new StatFactor("Перемены в уровне жизни", s.MoodChange, "number1_signed"),
                new StatFactor("Сам уровень жизни", s.MoodLevel, "number1_signed"),
                new StatFactor("Страх безработицы", -s.MoodFear, "number1_signed"),
                new StatFactor("Инфляция", -s.MoodInflation, "number1_signed"));
            Set("radicalism", s.Average(s.Radicalism));
        }
    }
}
