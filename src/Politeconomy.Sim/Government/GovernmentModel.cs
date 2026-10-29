using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Government.Systems;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Government;

/// <summary>Сборка правительств: глава и министры каждой страны, парламент; системы в порядке MODEL.md.</summary>
public static class GovernmentModel
{
    public static IReadOnlyList<ISimSystem> Systems() => [new CabinetSystem()];

    public static void Setup(World world)
    {
        var data = world.Data;
        var government = new WorldGovernment();
        world.Government = government;
        foreach (var country in world.Countries)
        {
            var g = new CountryGovernment
            {
                Leader = CharacterFactory.Create(world, country, null, leader: true),
                Parliament = CreateParliament(world, country),
            };
            foreach (var post in data.Government.Posts)
                g.Ministers[post.Id] = CharacterFactory.Create(world, country, post, leader: false);
            government.Countries[country.Id] = g;
        }
    }

    public static SystemRecord SystemOf(GameData data, Country country) =>
        data.Government.Systems.First(s => s.Id == WorldFactory.SystemId(country.System));

    /// <summary>Как называется глава исполнительной власти страны (это игрок).</summary>
    public static string LeaderTitle(GameData data, Country country) =>
        data.Government.LeaderTitles.GetValueOrDefault(country.Id) ?? SystemOf(data, country).Leader;

    /// <summary>
    /// Парламент: число мест — из данных или по закону кубического корня от населения; доля правящего блока и
    /// дисциплина — по режиму. Авторитарный режим делает любой парламент формальным.
    /// </summary>
    public static Parliament CreateParliament(World world, Country country)
    {
        var data = world.Data;
        var b = data.GovernmentBalance.Parliament;
        var system = SystemOf(data, country);
        var kind = system.Parliament;
        if (kind == "real" && country.Regime == Regime.Authoritarian)
            kind = "formal";
        var population = country.Stat("population");
        var seats = data.Government.ParliamentSeats.TryGetValue(country.Id, out var known)
            ? known
            : Math.Clamp((int)Math.Round(Math.Cbrt(double.IsNaN(population) ? 1e6 : population)), b.SeatsMin, b.SeatsMax);
        var (range, discipline) = (kind, country.Regime) switch
        {
            ("formal", _) or (_, Regime.Authoritarian) => (b.AuthoritarianRuling, b.AuthoritarianDiscipline),
            (_, Regime.Hybrid) => (b.HybridRuling, b.HybridDiscipline),
            _ when country.System is PoliticalSystem.PresidentialRepublic or PoliticalSystem.SemiPresidential
                => (b.PresidentialRuling, b.DemocracyDiscipline),
            _ => (b.DemocracyRuling, b.DemocracyDiscipline),
        };
        var share = range[0] + world.Random.NextDouble() * (range[1] - range[0]);
        return new Parliament
        {
            Kind = kind,
            Name = system.ParliamentName,
            Seats = kind == "none" ? 0 : seats,
            RulingSeats = kind == "none" ? 0 : (int)Math.Round(seats * share),
            Discipline = discipline,
        };
    }
}
