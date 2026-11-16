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

    [Fact]
    public void OfferedTreatyIsSignedOnceAndNotOfferedAgain()
    {
        var world = NewWorld();
        PoliticsActions.PlayAs(world, "DEU");
        // Германия и Франция: хорошие отношения и большая торговля — партнёр предлагает свободную торговлю.
        Assert.True(Holds(world, "DEU", "free_trade_offer", out var match));
        var partner = match.Partner!;
        Assert.False(DiplomacyModel.HasTreaty(world, "DEU", partner, "free_trade"));
        var instance = EventModel.Fire(world, world["DEU"], Event("free_trade_offer"))!;
        Assert.Equal(partner, instance.Partner);
        var option = Event("free_trade_offer").Option("sign")!;
        Assert.Contains(EventText.Consequences(TestData.Game, option), l => l.Contains("Соглашение о свободной торговле"));
        Assert.True(EventModel.Decide(world, "DEU", instance.Id, "sign"));
        Assert.True(DiplomacyModel.HasTreaty(world, "DEU", partner, "free_trade"));
        Assert.Contains(world.Journal, j => j.Country == "DEU" && j.Text.Contains("Подписан договор"));
        // Тот же партнёр договор второй раз не предлагает — найдётся другой или никто.
        Assert.False(Holds(world, "DEU", "free_trade_offer", out var again) && again.Partner == partner);
        // Отказ от предложенного договора его не подписывает.
        var other = EventModel.Fire(world, world["DEU"], Event("investment_treaty_offer"))!;
        Assert.True(EventModel.Decide(world, "DEU", other.Id, "refuse"));
        Assert.False(DiplomacyModel.HasTreaty(world, "DEU", other.Partner!, "investment"));
    }

    [Fact]
    public void AiSignsOfferedTreatiesToo()
    {
        var world = NewWorld();
        var record = Event("investment_treaty_offer");
        var instance = EventModel.Fire(world, world["ITA"], record)!;
        Assert.NotNull(instance.Partner);
        EventModel.Decide(world, "ITA", instance.Id, "sign");
        Assert.True(DiplomacyModel.HasTreaty(world, "ITA", instance.Partner!, "investment"));
        // Пересчёт после подписания договора идёт сразу: у ИИ тоже нет «грязного» состояния.
        Assert.False(world.Diplomacy!.Dirty);
        Assert.Null(world.Diplomacy.Actor);
    }

    [Fact]
    public void DefaultThresholdAsksTheFundOrCutsOrRestructures()
    {
        var world = NewWorld();
        PoliticsActions.PlayAs(world, "ARG");
        var e = world.Economy!["ARG"];
        e.Rating = 6;
        e.Debt = e.NominalGdp * 1.1;
        world["ARG"].Stats["credit_rating"] = world["ARG"].Stats["credit_rating"] with { Value = 6 };
        world["ARG"].Stats["gov_debt_pct_gdp"] = world["ARG"].Stats["gov_debt_pct_gdp"] with { Value = 110 };
        world["ARG"].Stats["interest_pct_gdp"] = world["ARG"].Stats["interest_pct_gdp"] with { Value = 5 };
        Assert.True(Holds(world, "ARG", "default_threshold", out var why));
        Assert.Contains(why.Why, w => w.Contains("Кредитный рейтинг"));
        Assert.False(Holds(world, "DEU", "default_threshold", out _));
        var debt = e.Debt;
        var instance = EventModel.Fire(world, world["ARG"], Event("default_threshold"))!;
        // Реструктуризация списывает часть долга сразу.
        Assert.True(EventModel.Decide(world, "ARG", instance.Id, "restructure"));
        Assert.InRange(e.Debt, debt - e.NominalGdp * 0.07, debt - e.NominalGdp * 0.05);
        // Просьба о кредите — решение совета: если он согласен, появляется программа, если нет — страна узнает почему.
        var second = EventModel.Fire(world, world["ARG"], Event("default_threshold") with { When = [] })!;
        Assert.True(EventModel.Decide(world, "ARG", second.Id, "fund"));
        Assert.True(world.Diplomacy!.Programs.ContainsKey("ARG") || world.Journal.Any(j => j.Country == "ARG" && j.Text.Contains("отказал")));
        Assert.Contains(EventText.Consequences(TestData.Game, Event("default_threshold").Option("fund")!), l => l.Contains("кредите"));
    }

    [Fact]
    public void SecondWaveFollowsTheInfectionAndRemembersIt()
    {
        var world = NewWorld();
        PoliticsActions.PlayAs(world, "BRA");
        Assert.True(Event("infection_second_wave").Chain);
        Assert.All(new[] { "quarantine", "vaccinate", "minimal" }, o =>
            Assert.Contains(Event("new_infection").Option(o)!.Next!, n => n.Event == "infection_second_wave"));
        var instance = EventModel.Fire(world, world["BRA"], Event("new_infection"))!;
        EventModel.Decide(world, "BRA", instance.Id, "minimal");
        // С вероятностью 0,6 за решением следует продолжение; проверяем сам механизм продолжения напрямую.
        var wave = EventModel.Fire(world, world["BRA"], Event("infection_second_wave"), instance);
        Assert.NotNull(wave);
        Assert.Equal("infection_second_wave", wave!.Event);
    }

    [Fact]
    public void NewEventsFollowTheStateOfTheCountry()
    {
        var world = NewWorld();
        // Авария на АЭС — только там, где есть атомная энергетика.
        Assert.True(Holds(world, "FRA", "nuclear_accident", out var nuclear));
        Assert.Contains(nuclear.Why, w => w.Contains("Атом"));
        Assert.False(Holds(world, "ARE", "nuclear_accident", out _) && world["ARE"].Stat("power_nuclear_pct") < 8);
        Assert.False(Holds(world, "NZL", "nuclear_accident", out _));
        // Нефтяной ветер — только при дорогой нефти и у экспортёра.
        Assert.False(Holds(world, "SAU", "oil_windfall", out _));
        world.Economy!.OilPrice = 1.6;
        Assert.True(Holds(world, "SAU", "oil_windfall", out _));
        Assert.False(Holds(world, "DEU", "oil_windfall", out _));
        // Валютный кризис — когда курс упал на четверть, а резервов мало.
        Assert.False(Holds(world, "DEU", "currency_crisis", out _));
        world["TUR"].Stats["exchange_rate_change_pct"] = world["TUR"].Stats["exchange_rate_change_pct"] with { Value = 40 };
        world["TUR"].Stats["reserves_pct_gdp"] = world["TUR"].Stats["reserves_pct_gdp"] with { Value = 5 };
        Assert.True(Holds(world, "TUR", "currency_crisis", out var crisis));
        Assert.Contains(crisis.Why, w => w.Contains("Курс"));
    }

    [Fact]
    public void MinisterEventsPickTheRightMinister()
    {
        var world = NewWorld();
        PoliticsActions.PlayAs(world, "FRA");
        var g = world.Government!["FRA"];
        foreach (var m in g.Ministers.Values)
        {
            m.Competence = 60;
            m.Loyalty = 70;
        }
        Assert.False(Holds(world, "FRA", "incompetent_minister", out _));
        Assert.False(Holds(world, "FRA", "minister_defects", out _));
        g.Minister("transport")!.Competence = 12;
        g.Minister("energy")!.Loyalty = 10;
        Assert.True(Holds(world, "FRA", "incompetent_minister", out var weak));
        Assert.Equal("transport", weak.Subject);
        Assert.True(Holds(world, "FRA", "minister_defects", out var defector));
        Assert.Equal("energy", defector.Subject);
        // Увольнение «героя» ставит на пост нового человека; «число» — сдвиг лояльности.
        var instance = EventModel.Fire(world, world["FRA"], Event("incompetent_minister"))!;
        var before = g.Minister("transport")!.Id;
        Assert.True(EventModel.Decide(world, "FRA", instance.Id, "fire"));
        Assert.NotEqual(before, g.Minister("transport")!.Id);
        var second = EventModel.Fire(world, world["FRA"], Event("minister_defects"))!;
        var loyalty = g.Minister("energy")!.Loyalty;
        Assert.True(EventModel.Decide(world, "FRA", second.Id, "concede"));
        Assert.Equal(40, g.Minister("energy")!.Loyalty);
        Assert.True(loyalty < 40);
    }

    [Fact]
    public void ThirdOfTheLatestEventsAreAboutSocietyAndPolitics()
    {
        var events = TestData.Game.Events.Events;
        Assert.True(events.Count(e => !e.Chain) >= 90);
        // Часть 7.5b: из 30 новых событий не меньше трети — про общество и политику, а не про экономику.
        var latest = events.Skip(64).Take(30).ToList();
        Assert.Equal(30, latest.Count);
        Assert.True(latest.Count(e => e.Category is "social" or "politics" or "scandal" or "characters") >= 10);
        // Все варианты закрыты своими «что будет» и ведут себя по данным.
        Assert.All(latest.SelectMany(e => e.Options), o => Assert.NotEmpty(EventText.Consequences(TestData.Game, o)));
    }

    [Fact]
    public void PoliticalEventsNeedTheRightRegimeAndMood()
    {
        var world = NewWorld();
        void Set(string country, string stat, double value) => world[country].Stats[stat] = world[country].Stats[stat] with { Value = value };
        // Митинг оппозиции — только там, где власть непопулярна и собираться можно.
        Set("DEU", "approval_pct", 30);
        Assert.True(Holds(world, "DEU", "opposition_rally", out var rally));
        Assert.Contains(rally.Why, w => w.Contains("Одобрение власти"));
        Set("DEU", "approval_pct", 70);
        Assert.False(Holds(world, "DEU", "opposition_rally", out _));
        Set("PRK", "approval_pct", 30);
        Assert.False(Holds(world, "PRK", "opposition_rally", out _));
        // Болезнь главы, слухи о заговоре и амнистия — только в несвободных режимах.
        Assert.True(Holds(world, "PRK", "leader_illness", out _));
        Assert.False(Holds(world, "DEU", "leader_illness", out _));
        Assert.False(Holds(world, "DEU", "amnesty_demand", out _));
        // Срок главы заканчивается у президента с высоким рейтингом, а не у канцлера.
        world.Laws!["FRA"].Current["term_limits"] = "two";
        Set("FRA", "approval_pct", 70);
        Assert.True(Holds(world, "FRA", "term_limit_end", out _));
        Set("DEU", "approval_pct", 70);
        Assert.False(Holds(world, "DEU", "term_limit_end", out _));
        // Вотум недоверия — только в парламентских системах.
        Set("DEU", "ruling_poll_pct", 30);
        Set("USA", "ruling_poll_pct", 30);
        Assert.True(Holds(world, "DEU", "no_confidence_threat", out _));
        Assert.False(Holds(world, "USA", "no_confidence_threat", out _));
        // Забастовка учителей — когда школам дают меньше денег, чем на старте.
        Assert.False(Holds(world, "FRA", "teachers_strike", out _));
        var education = TestData.Game.Budget.Spending.ToList().FindIndex(x => x.Id == "education");
        world.Economy!["FRA"].SpendingPlan[education] *= 0.85;
        Assert.True(Holds(world, "FRA", "teachers_strike", out _));
    }
}
