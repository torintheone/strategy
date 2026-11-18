using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Events;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.News;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Save;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Tests;

public class NewspaperTests
{
    static World NewWorld(string player = "FRA", ulong seed = 1)
    {
        var world = GameSetup.NewGame(TestData.Game, seed).Simulation.World;
        PoliticsActions.PlayAs(world, player);
        world.News = new WorldNews();
        return world;
    }

    static void Set(World world, string country, string stat, double value) =>
        world[country].Stats[stat] = world[country].Stats[stat] with { Value = value };

    static NewsItem Publish(World world, string? country, int weight, string source, string text, string tone = "neutral") =>
        NewsModel.Publish(world, country, "politics", text, weight, tone, source: source)!;

    /// <summary>Все министры страны — честные и толковые: газета ничего не разоблачит.</summary>
    static void HonestCabinet(World world, string country)
    {
        foreach (var m in world.Government![country].Ministers.Values)
        {
            m.Competence = 80;
            m.Corruption = 10;
            m.Traits.Clear();
        }
    }

    [Fact]
    public void PressToneFollowsPressFreedom()
    {
        var world = NewWorld();
        var b = TestData.Game.NewspaperBalance;
        Set(world, "FRA", "press_freedom_index", b.FreeMin + 5);
        Assert.Equal("free", NewspaperModel.Press(world, "FRA"));
        Set(world, "FRA", "press_freedom_index", b.LoyalMin + 1);
        Assert.Equal("loyal", NewspaperModel.Press(world, "FRA"));
        Set(world, "FRA", "press_freedom_index", b.LoyalMin - 1);
        Assert.Equal("state", NewspaperModel.Press(world, "FRA"));
    }

    [Fact]
    public void FreePaperWritesEverythingAndStateMediaHidesTheBad()
    {
        var texts = TestData.Game.News.Newspaper!;
        var b = TestData.Game.NewspaperBalance;
        Newspaper Paper(double freedom)
        {
            var world = NewWorld();
            HonestCabinet(world, "FRA");
            Set(world, "FRA", "press_freedom_index", freedom);
            Publish(world, "FRA", 70, "stat:unemployment_pct", "Безработица выросла до 9,0%", "bad");
            Publish(world, "FRA", 60, "law", "Парламент принял закон", "good");
            Publish(world, "FRA", 40, "budget", "Бюджет принят");
            Publish(world, "DEU", 80, "war", "Война", "bad");
            Publish(world, "USA", 85, "election", "Выборы президента");
            Publish(world, "USA", 80, "default", "Дефолт", "bad");
            return NewspaperModel.Issue(world, "FRA")!;
        }

        // Независимая: заголовок — самое важное дома (плохая новость), три материала, один на страну и источник.
        var free = Paper(b.FreeMin + 10);
        Assert.Equal("free", free.Press);
        Assert.Equal(texts.Free.Name, free.Name);
        Assert.Equal("Безработица выросла до 9,0%", free.Lead.Headline);
        Assert.Contains(free.Lead.Body, texts.Free.Bad);
        Assert.Equal(b.Stories, free.Stories.Count);
        Assert.Single(free.Stories, s => s.Country == "USA");
        Assert.Contains(free.Stories, s => s.Country == "DEU");
        // Мировые материалы — после своих, у мировых свой абзац.
        Assert.Equal(free.Stories.OrderByDescending(s => s.Country == "FRA").ToList(), free.Stories);
        Assert.All(free.Stories.Where(s => s.Country != "FRA"), s => Assert.Contains(s.Body, texts.Free.World));

        // Близкая к власти: о плохом дома молчит, хорошее — заслуга правительства.
        var loyal = Paper(b.LoyalMin + 5);
        Assert.Equal("loyal", loyal.Press);
        var all = new[] { loyal.Lead }.Concat(loyal.Stories).ToList();
        Assert.DoesNotContain(all, s => s.Headline.StartsWith("Безработица"));
        Assert.Contains(all, s => s.Headline == "Парламент принял закон" && texts.Loyal.Good.Contains(s.Body));

        // Государственная: хвалит руководство, о плохом дома ни слова; мировые беды — пожалуйста.
        var state = Paper(b.LoyalMin - 10);
        Assert.Equal("state", state.Press);
        all = [state.Lead, .. state.Stories];
        Assert.DoesNotContain(all, s => s.Country == "FRA" && s.Tone == "bad");
        Assert.Contains(all, s => s.Headline == "Дефолт" || s.Headline == "Война");
        Assert.DoesNotContain("{", string.Join(" ", all.Select(s => s.Headline + s.Body)));
    }

    [Fact]
    public void StateMediaPraisesTheLeaderWhenThereIsNothingGoodToSay()
    {
        var world = NewWorld();
        Set(world, "FRA", "press_freedom_index", 5);
        Publish(world, "FRA", 70, "stat:unemployment_pct", "Безработица выросла до 9,0%", "bad");
        var paper = NewspaperModel.Issue(world, "FRA")!;
        Assert.Equal("praise", paper.Lead.Kind);
        var leader = world.Government!["FRA"].Leader.Name;
        Assert.Contains(TestData.Game.News.Newspaper!.Praise, p => p.Replace("{leader}", leader) == paper.Lead.Headline);
        // Месяц без новостей вообще — «месяц без потрясений».
        var quiet = NewWorld();
        Set(quiet, "FRA", "press_freedom_index", 90);
        HonestCabinet(quiet, "FRA");
        Assert.Equal("quiet", NewspaperModel.Issue(quiet, "FRA")!.Lead.Kind);
    }

    [Fact]
    public void WeakOrCorruptMinisterEmbellishesTheReport()
    {
        var data = TestData.Game;
        var b = data.ReportsBalance;
        // Честный и толковый — не врёт; слабый и нечестный — врёт, но не больше предела.
        Assert.Equal(0, MinistryReport.Spin(b, 70, 20, []));
        Assert.True(MinistryReport.Spin(b, 20, 20, []) > 0);
        Assert.True(MinistryReport.Spin(b, 70, 90, ["corrupt"]) > MinistryReport.Spin(b, 70, 90, []));
        Assert.Equal(b.MaxSpin, MinistryReport.Spin(b, 1, 99, ["corrupt", "populist"]));
        // Цифры — в лучшую сторону: инфляция ниже, рост выше; чем хуже дела, тем больше приукрашено; в рамках 0…100.
        Assert.True(MinistryReport.Reported(data, "inflation_pct", 9, 3, 0.3) < 9);
        Assert.True(MinistryReport.Reported(data, "gdp_growth_pct", -2, 3, 0.3) > -2);
        Assert.True(9 - MinistryReport.Reported(data, "inflation_pct", 9, 3, 0.3) > 4 - MinistryReport.Reported(data, "inflation_pct", 4, 3, 0.3));
        Assert.Equal(100, MinistryReport.Reported(data, "literacy_pct", 99, 40, 0.35));
        Assert.True(MinistryReport.Reported(data, "unemployment_pct", 1, 6, 0.35) >= 0);
        // Нейтральные показатели и рейтинг не приукрашиваются.
        Assert.Equal(20, MinistryReport.Reported(data, "policy_rate_pct", 20, 5, 0.3));
        Assert.Equal(12, MinistryReport.Reported(data, "credit_rating", 12, 14, 0.3));
    }

    [Fact]
    public void ReportShowsEmbellishedFiguresAndAdviceByViews()
    {
        var world = NewWorld();
        Set(world, "FRA", "inflation_pct", 12);
        var post = TestData.Game.Government.Posts.First(p => p.Id == "central_bank");
        var minister = world.Government!["FRA"].Minister("central_bank")!;
        List<string> Report()
        {
            world.Government!["FRA"].Version++;
            var snapshot = WorldSnapshot.Capture(world, 1, true, 1);
            return MinistryReport.Lines(TestData.Game, post, snapshot.Countries["FRA"], snapshot, snapshot.Countries["FRA"].Government!.Minister("central_bank")).ToList();
        }
        minister.Competence = 80;
        minister.Corruption = 10;
        minister.Traits.Clear();
        minister.Views[0] = 50;
        var honest = Report();
        Assert.Contains(honest, l => l.StartsWith($"Инфляция: {Fmt.Value("percent", 12)}"));
        // Проблема есть — совет по взглядам: рыночник поднимет ставку, сторонник перераспределения — подождёт.
        Assert.Contains(honest, l => l == "Совет министра: " + char.ToLowerInvariant(post.Advice!.Right[0]) + post.Advice.Right[1..]);
        minister.Views[0] = -50;
        Assert.Contains(Report(), l => l.Contains(post.Advice!.Left[1..]));
        // Слабый министр даёт плохой совет и приукрашивает цифры; в докладе об этом ни слова.
        minister.Competence = 10;
        minister.Corruption = 90;
        var spun = Report();
        Assert.Contains(spun, l => l.Contains(post.Advice!.Weak[1..]));
        var line = spun.First(l => l.StartsWith("Инфляция: "));
        Assert.DoesNotContain($"Инфляция: {Fmt.Value("percent", 12)}", line);
        Assert.DoesNotContain(spun, l => l.Contains("приукрас"));
    }

    [Fact]
    public void FreePaperExposesTheEmbellishedReportOnceInAWhile()
    {
        var world = NewWorld();
        var b = TestData.Game.NewspaperBalance;
        Set(world, "FRA", "press_freedom_index", 90);
        HonestCabinet(world, "FRA");
        Set(world, "FRA", "inflation_pct", 12);
        var minister = world.Government!["FRA"].Minister("central_bank")!;
        minister.Competence = 15;
        minister.Corruption = 90;
        Publish(world, "FRA", 50, "law", "Парламент принял закон");
        var paper = NewspaperModel.Issue(world, "FRA")!;
        var expose = new[] { paper.Lead }.Concat(paper.Stories).Single(s => s.Kind == "expose");
        Assert.Contains(minister.Name, expose.Body);
        Assert.Contains(Fmt.Value("percent", 12), expose.Headline);
        Assert.Equal("bad", expose.Tone);
        // Через месяц того же министра не разоблачают; через срок — снова можно.
        world.Date = world.Date.AddMonths(1);
        Assert.DoesNotContain(NewspaperModel.Issue(world, "FRA")!.Stories.Append(world.News!.Papers[^1].Lead), s => s.Kind == "expose");
        world.Date = world.Date.AddMonths(b.ExposeMonths);
        var later = NewspaperModel.Issue(world, "FRA")!;
        Assert.Contains(later.Stories.Append(later.Lead), s => s.Kind == "expose");
        // Несвободная пресса не разоблачает никого.
        world.Date = world.Date.AddMonths(b.ExposeMonths);
        Set(world, "FRA", "press_freedom_index", b.LoyalMin + 1);
        var loyal = NewspaperModel.Issue(world, "FRA")!;
        Assert.DoesNotContain(loyal.Stories.Append(loyal.Lead), s => s.Kind == "expose");
    }

    [Fact]
    public void PaperComesOutOnTheFirstAndAYearOfIssuesSurvivesSaving()
    {
        var sim = GameSetup.NewGame(TestData.Game, 4).Simulation;
        var world = sim.World;
        PoliticsActions.PlayAs(world, "IND");
        var end = world.Date.AddMonths(14);
        var seen = new SortedDictionary<int, Newspaper>();
        while (world.Date < end)
        {
            sim.RunUntil(world.Date.AddDays(7) < end ? world.Date.AddDays(7) : end);
            foreach (var pending in world.Events!["IND"].Pending.ToList())
                EventModel.Decide(world, "IND", pending.Id, EventModel.DefaultOption(world, world["IND"], TestData.Game.Events.Event(pending.Event)!).Id);
            foreach (var p in world.News!.Papers)
                seen[p.Id] = p;
        }
        Assert.Equal(14, seen.Count);
        Assert.All(seen.Values, p =>
        {
            Assert.Equal(1, p.Date.Day);
            Assert.Equal("IND", p.Country);
            Assert.InRange(p.Stories.Count, 0, TestData.Game.NewspaperBalance.Stories);
            Assert.False(string.IsNullOrWhiteSpace(p.Lead.Headline));
        });
        Assert.True(seen.Values.Count(p => p.Stories.Count == TestData.Game.NewspaperBalance.Stories) >= 10);
        // Хранится год номеров.
        Assert.Equal(TestData.Game.NewspaperBalance.Keep, world.News!.Papers.Count);
        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = GameSetup.Resume(SaveGame.Read(TestData.Game, stream));
        Assert.Equal(world.News.Papers.Select(p => p.Lead), loaded.World.News!.Papers.Select(p => p.Lead));
        Assert.Equal(world.News.Exposed, loaded.World.News.Exposed);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded.World));
    }
}
