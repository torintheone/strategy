using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Politics;

namespace Politeconomy.Sim.Tests;

public class PoliticsTests
{
    static readonly Lazy<World> Start = new(() => GameSetup.NewGame(TestData.Game, 3).Simulation.World);

    [Fact]
    public void EveryCountryWithPartiesHasAGoverningParty()
    {
        var world = Start.Value;
        var withParties = 0;
        foreach (var country in world.Countries)
        {
            var p = world.Politics![country.Id];
            var g = world.Government![country.Id];
            if (!TestData.Game.PoliticsOf(country.Id).Parties)
            {
                Assert.Empty(p.Parties);
                continue;
            }
            withParties++;
            Assert.NotEmpty(p.Parties);
            var head = p.Party(g.Leader.Party);
            Assert.True(head is { Ruling: true, Leader: null }, $"{country.Id}: глава возглавляет правящую партию");
            Assert.InRange(p.Parties.Sum(x => x.Poll), 0.999, 1.001);
            Assert.Equal(p.Parties.Count, p.Parties.Select(x => x.Name).Distinct().Count());
            Assert.Equal(p.Parties.Count, p.Parties.Select(x => x.Short).Distinct().Count());
            Assert.All(p.Parties, x => Assert.InRange(x.Voters.Sum(), 0.999, 1.001));
            if (g.Parliament.Exists)
            {
                Assert.Equal(g.Parliament.Seats, g.Parliament.Factions.Sum(f => f.Seats));
                Assert.All(g.Parliament.Factions, f => Assert.NotNull(p.Party(f.Party)));
            }
            Assert.All(g.Ministers.Values, m => Assert.True(m.Party == null || p.Party(m.Party) is { Ruling: true }));
        }
        Assert.True(withParties > 150);
    }

    [Fact]
    public void DemocraciesHaveSeveralPartiesAndDictatorshipsADominantOne()
    {
        var world = Start.Value;
        var democracies = world.Countries.Where(c => c.Regime == Regime.Democracy && world.Politics![c.Id].Parties.Count > 0).ToList();
        Assert.All(democracies, c => Assert.InRange(world.Politics![c.Id].Parties.Count, 3, 7));
        // В демократии ни одна партия не набирает в опросах больше 65%.
        Assert.True(democracies.Count(c => world.Politics![c.Id].Parties.Max(p => p.Poll) > 0.65) <= democracies.Count / 20);
        var rus = world.Politics!["RUS"];
        Assert.True(rus.Parties.Single(p => p.Power).Poll > 0.6, "партия власти доминирует");
        Assert.True(world.Government!["RUS"].Parliament.RulingSeats > 0.8 * world.Government!["RUS"].Parliament.Seats);
    }

    [Fact]
    public void SeatsFollowTheElectoralSystem()
    {
        double[] votes = [0.42, 0.38, 0.12, 0.08];
        var proportional = Seats.Proportional(votes, 100, threshold: 10);
        Assert.Equal(100, proportional.Sum());
        Assert.Equal(0, proportional[3]);
        Assert.True(proportional[0] > 42 && proportional[0] < 50, "д’Онт слегка поощряет крупных");
        var majoritarian = Seats.Majoritarian(votes, 100, power: 2);
        Assert.Equal(100, majoritarian.Sum());
        Assert.True(majoritarian[0] > 50, "в мажоритарной системе 42% голосов дают большинство мест");
        Assert.True(majoritarian[3] < 3);
        var mixed = Seats.Allocate("mixed", votes, 101, 5, 2);
        Assert.Equal(101, mixed.Sum());
    }

    [Fact]
    public void CoalitionPrefersSmallCloseParties()
    {
        var b = TestData.Game.PoliticsBalance;
        Party P(double economy, double civil) => new() { Position = [economy, civil, 0], Poll = 0.1 };
        var parties = new List<Party> { P(-30, -10), P(30, 10), P(-35, -20), P(-60, 0) };
        // У левой партии 45%: ей нужен партнёр; маленькая близкая партия лучше крупного соперника справа.
        var coalition = Coalition.Form(parties, [45, 35, 12, 8], b);
        Assert.Equal([0, 2], coalition);
        Assert.Equal([0], Coalition.Form(parties, [55, 30, 10, 5], b));
    }

    [Fact]
    public void PartiesVoteByTheirVoters()
    {
        var world = GameSetup.NewGame(TestData.Game, 3).Simulation.World;
        var parliament = new Parliament
        {
            Kind = "real", Seats = 300, Discipline = 0.82,
            Factions =
            [
                new Faction { Party = "left", Seats = 140, Ruling = true },
                new Faction { Party = "workers", Seats = 40 },
                new Faction { Party = "right", Seats = 120 },
            ],
        };
        // Проект нравится избирателям рабочей партии: она поддерживает правительство меньшинства, и он проходит.
        var vote = ParliamentVote.Cast(world, parliament, p => p switch { "workers" => 0.95, "right" => 0.2, _ => 0.5 });
        Assert.True(vote.Passed);
        Assert.Contains(vote.Factions!, f => f.Party == "workers" && f.Yes > f.No);
        Assert.Contains(vote.Factions!, f => f.Party == "right" && f.No > f.Yes);
        // Если он не нравится и им — проваливается.
        Assert.False(ParliamentVote.Cast(world, parliament, p => p switch { "workers" => 0.3, "right" => 0.2, _ => 0.5 }).Passed);
    }

    [Fact]
    public void RecessionLowersTheRulingPartiesRating()
    {
        // Критерий приёмки этапа 4: в рецессию рейтинг правящей партии падает. Смотрим на партии, правившие на старте,
        // через 8 месяцев — до первых выборов в этих странах (в Бразилии они в октябре).
        var crisis = GameSetup.NewGame(TestData.Game, 3).Simulation;
        var baseline = GameSetup.NewGame(TestData.Game, 3).Simulation;
        string[] countries = ["DEU", "BRA", "USA", "IND"];
        var ruling = countries.ToDictionary(id => id, id => crisis.World.Politics![id].Parties.Where(x => x.Ruling).Select(x => x.Id).ToHashSet());
        for (var month = 0; month < 8; month++)
        {
            foreach (var id in countries)
                crisis.World.Economy![id].GapCore = -0.12;
            crisis.RunUntil(crisis.World.Date.AddMonths(1));
            baseline.RunUntil(baseline.World.Date.AddMonths(1));
        }
        double Rating(World w, string id) => w.Politics![id].Parties.Where(x => ruling[id].Contains(x.Id)).Sum(x => x.Poll) * 100;
        foreach (var id in countries)
        {
            var hit = Rating(crisis.World, id);
            var calm = Rating(baseline.World, id);
            Assert.True(hit < calm - 3, $"{id}: правящие в кризис {hit:F1}%, без кризиса {calm:F1}%");
        }
    }

    [Fact]
    public void ViewsRadicaliseInCrisis()
    {
        var b = TestData.Game.PoliticsBalance;
        var worker = TestData.Game.Classes.Classes[1];
        var calm = PoliticalViews.Target(worker, [0, 0, 0], satisfaction: 50, radicalism: worker.Radicalism, b);
        var angry = PoliticalViews.Target(worker, [0, 0, 0], satisfaction: 25, radicalism: worker.Radicalism + 30, b);
        Assert.True(angry[0] < calm[0], "недовольные хотят больше перераспределения");
        Assert.True(angry[1] > calm[1], "и больше порядка");
        Assert.True(angry[2] > calm[2], "и больше суверенитета");
    }

    [Fact]
    public void PartyNamesAreReadable()
    {
        var world = Start.Value;
        var names = world.Politics!.Countries.Values.SelectMany(p => p.Parties).Select(p => p.Name).ToList();
        Assert.All(names, n => Assert.True(char.IsUpper(n[0]), n));
        Assert.All(names, n => Assert.DoesNotContain("  ", n));
        Assert.Equal("СДП", PartyNames.Short("Социал-демократическая партия", null));
        Assert.Equal("ПВТ", PartyNames.Short("Партия веры и традиций", null));
        Assert.Equal("Новый путь", PartyNames.Short("Движение «Новый путь»", "Новый путь"));
    }
}
