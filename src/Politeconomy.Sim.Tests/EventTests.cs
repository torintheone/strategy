using Politeconomy.Sim.Data;
using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Events;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Save;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Tests;

public class EventTests
{
    static EventRecord Event(string id) => TestData.Game.Events.Event(id)!;

    static World NewWorld(ulong seed = 1) => GameSetup.NewGame(TestData.Game, seed).Simulation.World;

    static bool Holds(World world, string country, string id, out EventConditions.Match match)
    {
        match = new EventConditions.Match { Explain = true };
        return EventConditions.Holds(world, world[country], Event(id).When, match);
    }

    [Fact]
    public void ThirtyEventsInEveryCategory()
    {
        var events = TestData.Game.Events;
        Assert.True(events.Events.Count(e => !e.Chain) >= 30);
        Assert.All(events.Categories, c => Assert.Contains(events.Events, e => e.Category == c.Id));
        Assert.All(events.Events, e => Assert.InRange(e.Options.Count, 2, 4));
        // У каждого варианта есть что сказать о последствиях — строки собираются из данных.
        Assert.All(events.Events.SelectMany(e => e.Options), o => Assert.NotEmpty(EventText.Consequences(TestData.Game, o)));
    }

    [Fact]
    public void ConditionsFollowTheStateOfTheCountry()
    {
        var world = NewWorld();
        // Нефть на обычном уровне — обвала нет; цена упала — событие возможно у экспортёра, но не у Германии.
        Assert.False(Holds(world, "SAU", "oil_price_crash", out _));
        world.Economy!.OilPrice = 0.6;
        Assert.True(Holds(world, "SAU", "oil_price_crash", out var oil));
        Assert.Contains(oil.Why, w => w.Contains("Мировая цена нефти"));
        Assert.False(Holds(world, "DEU", "oil_price_crash", out _));
        // Демографический спад — только там, где рожают мало.
        Assert.True(Holds(world, "JPN", "demographic_decline", out var births));
        Assert.Contains(births.Why, w => w.Contains("Рождаемость"));
        Assert.False(Holds(world, "NGA", "demographic_decline", out _));
        // Землетрясение — только в сейсмической зоне, и чем опаснее зона, тем чаще.
        Assert.True(Holds(world, "JPN", "earthquake", out var quake));
        Assert.Equal(3, quake.Hazard);
        Assert.False(Holds(world, "BLR", "earthquake", out _));
        // Забастовка врачей — когда отрасли дают меньше денег, чем на старте.
        Assert.False(Holds(world, "FRA", "doctors_strike", out _));
        var health = TestData.Game.Budget.Spending.ToList().FindIndex(s => s.Id == "health");
        world.Economy["FRA"].SpendingPlan[health] *= 0.8;
        Assert.True(Holds(world, "FRA", "doctors_strike", out var strike));
        Assert.Contains(strike.Why, w => w.Contains("80% от расходов на старте"));
    }

    [Fact]
    public void PlayerEventWaitsThenDecisionChangesTheModel()
    {
        var sim = GameSetup.NewGame(TestData.Game, 3).Simulation;
        var world = sim.World;
        PoliticsActions.PlayAs(world, "BRA");
        var record = Event("record_harvest");
        var instance = EventModel.Fire(world, world["BRA"], record)!;
        var ce = world.Events!["BRA"];
        Assert.Contains(ce.Pending, p => p.Id == instance.Id);
        Assert.NotEmpty(instance.Why);
        Assert.Contains(world.Journal, j => j.Kind == "event" && j.Text.Contains("Нужно ваше решение"));
        var before = LawModel.Effect(world, "BRA", "potential_growth");
        var e = world.Economy!["BRA"];
        var debt = e.Debt;

        var snapshot = WorldSnapshot.Capture(world, 1, true, 0).Events!;
        var pending = Assert.Single(snapshot.Pending);
        Assert.Equal("Рекордный урожай", pending.Title);
        Assert.NotNull(pending.Reporter);
        Assert.All(pending.Options, o => Assert.NotEmpty(o.Consequences));

        Assert.True(EventModel.Decide(world, "BRA", instance.Id, "export"));
        Assert.Empty(ce.Pending);
        Assert.Equal(debt - 0.003 * e.NominalGdp, e.Debt, 3);
        Assert.Equal(before + 0.15, LawModel.Effect(world, "BRA", "potential_growth"), 9);
        var farmers = TestData.Game.Classes.Classes.ToList().FindIndex(c => c.Id == "farmers");
        Assert.Equal(3, ce.Mood[farmers]);
        Assert.Contains(ce.History, h => h.Event == "record_harvest" && h.Option == "export");
        Assert.Single(WorldSnapshot.Capture(world, 1, true, 0).Events!.Active);
        // Через год последствия этого решения кончаются: остаются только поправки других решений.
        sim.RunUntil(world.Date.AddMonths(13));
        Assert.DoesNotContain(ce.Active, a => a.Event == "record_harvest");
        var rest = ce.Active.Sum(a => Event(a.Event).Option(a.Option)!.Effects?.GetValueOrDefault("potential_growth") ?? 0);
        Assert.Equal(rest, ce.Effects.GetValueOrDefault("potential_growth"), 9);
    }

    [Fact]
    public void UnansweredEventTakesTheDefaultOption()
    {
        var sim = GameSetup.NewGame(TestData.Game, 3).Simulation;
        var world = sim.World;
        PoliticsActions.PlayAs(world, "BRA");
        EventModel.Fire(world, world["BRA"], Event("record_harvest"));
        sim.RunUntil(world.Date.AddDays(TestData.Game.EventsBalance.DeadlineDays + 2));
        var ce = world.Events!["BRA"];
        Assert.DoesNotContain(ce.Pending, p => p.Event == "record_harvest");
        Assert.Contains(ce.History, h => h.Event == "record_harvest" && h.Option == "reserve");
        Assert.Contains(world.Journal, j => j.Kind == "event" && j.Text.Contains("не принято в срок"));
    }

    [Fact]
    public void ClosedOptionCannotBeChosen()
    {
        var world = NewWorld();
        PoliticsActions.PlayAs(world, "DEU");
        var record = Event("brain_drain") with { When = [] };
        var instance = EventModel.Fire(world, world["DEU"], record)!;
        var restrict = Event("brain_drain").Option("restrict")!;
        Assert.False(EventModel.Enabled(world, world["DEU"], restrict));
        Assert.True(EventModel.Enabled(world, world["CHN"], restrict));
        Assert.False(EventModel.Decide(world, "DEU", instance.Id, "restrict"));
        var option = WorldSnapshot.Capture(world, 1, true, 0).Events!.Pending[0].Options.Single(o => o.Id == "restrict");
        Assert.False(option.Enabled);
        Assert.Contains("суд", option.Reason);
        Assert.True(EventModel.Decide(world, "DEU", instance.Id, "grants"));
    }

    [Fact]
    public void AiChoosesByItsPrioritiesAndExplains()
    {
        var world = NewWorld();
        var country = world["KAZ"];
        var ai = world.Ai!["KAZ"];
        var record = Event("miners_strike");
        ai.Priorities = [0.05, 0.05, 0.85, 0.05];
        Assert.Equal("disperse", EventModel.Choose(world, country, record).Id);
        ai.Priorities = [0.05, 0.6, 0.05, 0.3];
        Assert.Equal("raise", EventModel.Choose(world, country, record).Id);
        var instance = EventModel.Fire(world, country, record with { When = [new EventCondition(Stat: "population", Min: 1)] });
        Assert.NotNull(instance);
        Assert.Empty(world.Events!["KAZ"].Pending);
        var decision = ai.Decisions.Last(d => d.Kind == "event");
        Assert.Contains("Забастовка шахтёров", decision.Text);
        Assert.Contains("Население", decision.Why);
        Assert.Contains("приоритетам", decision.Why);
        Assert.Contains(world.Events.Recent, n => n.Country == "KAZ" && n.Event == "miners_strike" && n.Option == "raise");
    }

    [Fact]
    public void ScandalFindsTheMinisterAndCanFireHim()
    {
        var world = NewWorld();
        PoliticsActions.PlayAs(world, "BRA");
        var g = world.Government!["BRA"];
        foreach (var m in g.Ministers.Values)
            m.Corruption = 10;
        var minister = g.Ministers["transport"];
        minister.Corruption = 90;
        var instance = EventModel.Fire(world, world["BRA"], Event("minister_scandal"))!;
        Assert.Equal("transport", instance.Subject);
        var title = WorldSnapshot.Capture(world, 1, true, 0).Events!.Pending[0].Title;
        Assert.Contains(minister.Name, title);
        Assert.True(EventModel.Decide(world, "BRA", instance.Id, "fire"));
        Assert.NotEqual(minister.Id, g.Ministers["transport"].Id);
        Assert.Contains(world.Journal, j => j.Kind == "resignation" && j.Text.Contains(minister.Name));
    }

    [Fact]
    public void ChainContinuesAndRemembersItsParent()
    {
        var sim = GameSetup.NewGame(TestData.Game, 2).Simulation;
        var world = sim.World;
        PoliticsActions.PlayAs(world, "BRA");
        var ce = world.Events!["BRA"];
        ce.Scheduled.Add(new EventInstance { Event = "housing_crash", Date = world.Date.AddDays(3), Parent = "housing_bubble" });
        sim.RunUntil(world.Date.AddDays(10));
        var crash = Assert.Single(ce.Pending, p => p.Event == "housing_crash");
        Assert.Contains(crash.Why, w => w.Contains("Продолжение события «Пузырь на рынке жилья»"));
        Assert.Empty(ce.Scheduled);
        // Само по себе продолжение не случается.
        Assert.True(Event("housing_crash").Chain);
    }

    [Fact]
    public void EventShiftsRelationsWithThePartner()
    {
        var world = NewWorld();
        PoliticsActions.PlayAs(world, "POL");
        var record = Event("neighbor_tariffs") with { When = [new EventCondition(Partner: new EventPartner(Neighbor: true))] };
        var instance = EventModel.Fire(world, world["POL"], record)!;
        Assert.NotNull(instance.Partner);
        var before = DiplomacyModel.Relations(world, "POL", instance.Partner!);
        Assert.True(EventModel.Decide(world, "POL", instance.Id, "mirror"));
        Assert.InRange(DiplomacyModel.Relations(world, "POL", instance.Partner!), before - 7, before - 5);
    }

    [Fact]
    public void WorldLivesWithEventsAndStaysDeterministic()
    {
        var first = GameSetup.NewGame(TestData.Game, 7).Simulation;
        var second = GameSetup.NewGame(TestData.Game, 7).Simulation;
        var until = first.World.Date.AddYears(2);
        first.RunUntil(until);
        second.RunUntil(until);
        Assert.Equal(SaveGame.Hash(first.World), SaveGame.Hash(second.World));
        var events = first.World.Events!;
        var total = events.Counts.Values.Sum();
        // За два года в 198 странах — сотни событий десятка разных видов; у каждой страны не больше нескольких в год.
        Assert.InRange(total, 150, 198 * 2 * 6);
        Assert.True(events.Counts.Count >= 12, string.Join(", ", events.Counts.Keys));
        var decisions = first.World.Ai!.Countries.Values.SelectMany(c => c.Decisions).Where(d => d.Kind == "event").ToList();
        Assert.NotEmpty(decisions);
        Assert.All(decisions, d => Assert.Contains("приоритетам", d.Why));
    }

    [Fact]
    public void PendingEventsAndEffectsSurviveSaving()
    {
        var sim = GameSetup.NewGame(TestData.Game, 5).Simulation;
        var world = sim.World;
        PoliticsActions.PlayAs(world, "BRA");
        var decided = EventModel.Fire(world, world["BRA"], Event("record_harvest"))!;
        EventModel.Decide(world, "BRA", decided.Id, "export");
        EventModel.Fire(world, world["BRA"], Event("new_infection"));
        sim.RunUntil(world.Date.AddDays(12));
        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = SaveGame.Read(TestData.Game, stream);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded));
        Assert.Contains(loaded.Events!["BRA"].Pending, p => p.Event == "new_infection");
        Assert.Equal(LawModel.Effect(world, "BRA", "potential_growth"), LawModel.Effect(loaded, "BRA", "potential_growth"), 12);
        var resumed = GameSetup.Resume(loaded);
        resumed.RunUntil(world.Date.AddMonths(3));
        sim.RunUntil(world.Date.AddMonths(3));
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded));
    }
}
